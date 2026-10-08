using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core.VerifyInProxy;

/// What the tap serves: the file's length and a way to read it — from the destination's disk, through the verifier.
public interface ITapSource
{
    long Length { get; }
    /// The bytes at `offset` into `buffer`; how many were copied — fewer only at the end of the file. Throws on a disk error.
    int Read(long offset, Span<byte> buffer);
}

/// R3 (mode A): Offload's own HTTP server, so the ffmpeg process reads the clip through Offload and every byte it reads
/// passes the block verifier. Security: bound to 127.0.0.1 only (never another interface) and a peer that is not a
/// loopback address is dropped unread, a random port, a one-time 128-bit token as the first path segment (compared in
/// constant time) and a random id per file — the URL names no path on the disk, so nothing in it can reach any other
/// file (no path traversal: the rest of the path is never decoded or used). Only GET and HEAD, one byte range at a time,
/// a Host header of exactly 127.0.0.1:<port> (a web page cannot reach it through DNS rebinding), at most MaxConnections
/// at once, headers up to MaxHeaderBytes arriving within HeaderTimeout as a whole (preflight M7: a client trickling a
/// byte at a time cannot hold a slot). Dispose (the end of the job's stage 2) stops listening and cuts every connection.
public sealed class RangeServer : IDisposable
{
    public const int MaxConnections = 16;
    public const int MaxHeaderBytes = 8192;
    /// Small slices and a small socket send buffer: when ffmpeg drops a connection to seek (moov at the end, an MXF
    /// footer) the server notices after little read-ahead — every block read is still verified, but a block read ahead
    /// and needed again later may fall out of the tap's cache and be read twice.
    const int SliceBytes = 256 << 10;
    const int SendBufferBytes = 64 << 10;

    readonly TcpListener listener;
    readonly byte[] token = RandomNumberGenerator.GetBytes(16);
    readonly ConcurrentDictionary<string, ITapSource> files = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<TcpClient, byte> clients = new();
    readonly SemaphoreSlim slots = new(MaxConnections, MaxConnections);
    readonly CancellationTokenSource stop = new();
    readonly Task accepting;
    long bytesSent, requests, readErrors;

    public int Port { get; }
    public IPEndPoint EndPoint => (IPEndPoint)listener.LocalEndpoint;
    /// Body bytes sent, requests answered and disk reads that failed under a request (tests, the log).
    public long BytesSent => Interlocked.Read(ref bytesSent);
    public long Requests => Interlocked.Read(ref requests);
    public long ReadErrors => Interlocked.Read(ref readErrors);
    /// The time a request's whole head (request line and headers) has to arrive in; also how long an idle kept-alive
    /// connection stays open. Tests shorten it.
    internal TimeSpan HeaderTimeout { get; init; } = TimeSpan.FromSeconds(30);
    string Token => Convert.ToHexStringLower(token);

    public RangeServer()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = Task.Run(AcceptLoop);
    }

    /// Serves `source` until Unregister; returns its URL — `name` only gives ffmpeg the extension (the demuxer's hint).
    public string Register(ITapSource source, string name)
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        files[id] = source;
        var safe = new string(Path.GetFileName(name).Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
        return $"http://127.0.0.1:{Port}/{Token}/{id}/{(safe.Length > 0 ? safe : "clip")}";
    }

    /// Stops serving the file of `url` (a request for it is then 404).
    public void Unregister(string url)
    {
        if (IdOf(url) is string id) files.TryRemove(id, out _);
    }

    /// The file id of a URL this server made, else null.
    static string? IdOf(string url)
    {
        var parts = new Uri(url).AbsolutePath.Split('/');
        return parts.Length >= 4 ? parts[2] : null;
    }

    /// Only a loopback peer is served. The listener is bound to 127.0.0.1, so no other peer can connect — this is the
    /// belt behind it (an IPv4-mapped address of another host is not loopback).
    internal static bool IsAllowedPeer(EndPoint? peer) => peer is IPEndPoint ip && IPAddress.IsLoopback(ip.Address);

    async Task AcceptLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false); }
            catch (Exception) { return; }   // stopped
            if (!IsAllowedPeer(SafePeer(client)) || !slots.Wait(0))
            {
                client.Dispose();   // not from this machine's loopback, or too many at once: refused, never queued
                continue;
            }
            clients[client] = 0;
            _ = Task.Run(() =>
            {
                try { Serve(client); }
                catch (Exception) { /* the client went away, or the server stopped */ }
                finally
                {
                    clients.TryRemove(client, out _);
                    client.Dispose();
                    slots.Release();
                }
            });
        }
    }

    static EndPoint? SafePeer(TcpClient client)
    {
        try { return client.Client.RemoteEndPoint; }
        catch (Exception) { return null; }
    }

    void Serve(TcpClient client)
    {
        client.NoDelay = true;
        client.SendBufferSize = SendBufferBytes;
        client.SendTimeout = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;
        using var stream = client.GetStream();
        var head = new HeadReader(client.Client, stream);
        var buffer = new byte[SliceBytes];
        while (!stop.IsCancellationRequested)
        {
            var lines = head.Next(HeaderTimeout);
            if (lines == null) return;   // closed, too long, not ASCII or not complete in time
            Interlocked.Increment(ref requests);
            if (!Answer(stream, lines, buffer)) return;
        }
    }

    /// Reads request heads off one connection in chunks (not a system call per byte), keeping what follows a head for
    /// the next request.
    sealed class HeadReader(Socket socket, NetworkStream stream)
    {
        readonly byte[] data = new byte[MaxHeaderBytes + 1];
        int count;

        /// The request line and headers (ASCII, up to the empty line); null when the client closed, the head is longer
        /// than MaxHeaderBytes, is not ASCII, or did not arrive within `timeout` as a whole.
        public List<string>? Next(TimeSpan timeout)
        {
            long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            int searched = 0;
            while (true)
            {
                int end = data.AsSpan(0, count).IndexOf("\r\n\r\n"u8);
                if (end >= 0 && end + 4 <= MaxHeaderBytes)
                {
                    int length = end + 4;
                    var head = data.AsSpan(0, length);
                    if (head.ContainsAnyInRange((byte)128, (byte)255)) return null;
                    var text = Encoding.ASCII.GetString(head);
                    data.AsSpan(length, count - length).CopyTo(data);
                    count -= length;
                    return text.Split("\r\n").Where(l => l.Length > 0).ToList();
                }
                if (count > MaxHeaderBytes) return null;
                if (data.AsSpan(searched, count - searched).ContainsAnyInRange((byte)128, (byte)255)) return null;
                searched = count;
                long left = deadline - Environment.TickCount64;
                if (left <= 0) return null;
                socket.ReceiveTimeout = (int)Math.Max(1, left);
                int n;
                try { n = stream.Read(data, count, data.Length - count); }
                catch (IOException) { return null; }   // timed out or reset
                if (n <= 0) return null;
                count += n;
            }
        }
    }

    /// One request. False = close the connection.
    bool Answer(NetworkStream stream, List<string> head, byte[] buffer)
    {
        var line = head[0].Split(' ');
        if (line.Length != 3 || !line[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return Status(stream, 400, "Bad Request", close: true);
        string method = line[0], target = line[1];
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in head.Skip(1))
        {
            int colon = h.IndexOf(':');
            if (colon <= 0) return Status(stream, 400, "Bad Request", close: true);
            headers[h[..colon].Trim()] = h[(colon + 1)..].Trim();
        }
        bool close = line[2] == "HTTP/1.0" || headers.TryGetValue("Connection", out var c) && c.Equals("close", StringComparison.OrdinalIgnoreCase);
        if (method is not ("GET" or "HEAD")) return Status(stream, 405, "Method Not Allowed", close: true, "Allow: GET, HEAD\r\n");
        if (!headers.TryGetValue("Host", out var host) || !host.Equals($"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase))
            return Status(stream, 403, "Forbidden", close: true);
        if (Find(target) is not { } source) return Status(stream, 404, "Not Found", close);

        long length = source.Length;
        long start = 0, end = length - 1;
        bool partial = false;
        if (headers.TryGetValue("Range", out var range))
        {
            if (ParseRange(range, length) is not { } r) return Status(stream, 416, "Range Not Satisfiable", close, $"Content-Range: bytes */{length}\r\n");
            (start, end, partial) = (r.Start, r.End, true);
        }
        long count = Math.Max(0, end - start + 1);
        var sb = new StringBuilder();
        sb.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
        sb.Append("Content-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"Content-Length: {count}\r\n");
        if (partial) sb.Append(CultureInfo.InvariantCulture, $"Content-Range: bytes {start}-{end}/{length}\r\n");
        sb.Append(close ? "Connection: close\r\n\r\n" : "Connection: keep-alive\r\n\r\n");
        stream.Write(Encoding.ASCII.GetBytes(sb.ToString()));
        if (method == "HEAD") return !close;

        for (long at = start; at <= end;)
        {
            int want = (int)Math.Min(buffer.Length, end - at + 1);
            int n;
            try { n = source.Read(at, buffer.AsSpan(0, want)); }
            catch (Exception)
            {
                Interlocked.Increment(ref readErrors);
                return false;   // the headers are out: only a cut connection can say it failed
            }
            if (n <= 0) return false;   // the file got shorter than it said: cut, ffmpeg sees a short read
            stream.Write(buffer, 0, n);
            Interlocked.Add(ref bytesSent, n);
            at += n;
        }
        return !close;
    }

    /// The source a target names: "/<token>/<id>/<name>" with this server's token and a registered id; else null.
    ITapSource? Find(string target)
    {
        var parts = target.Split('/');
        if (parts.Length != 4 || parts[0].Length != 0 || parts[1].Length != token.Length * 2) return null;
        byte[] given;
        try { given = Convert.FromHexString(parts[1]); }
        catch (FormatException) { return null; }
        if (!CryptographicOperations.FixedTimeEquals(given, token)) return null;
        return files.TryGetValue(parts[2], out var s) ? s : null;
    }

    /// One range: "bytes=a-b", "bytes=a-", "bytes=-n" within `length`; null for anything else (several ranges included).
    internal static (long Start, long End)? ParseRange(string value, long length)
    {
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
        var spec = value[6..].Trim();
        if (spec.Contains(',')) return null;
        int dash = spec.IndexOf('-');
        if (dash < 0) return null;
        string a = spec[..dash].Trim(), b = spec[(dash + 1)..].Trim();
        static bool Num(string s, out long v) => long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);
        if (a.Length == 0)
        {
            if (!Num(b, out var suffix) || suffix == 0 || length == 0) return null;
            return (Math.Max(0, length - suffix), length - 1);
        }
        if (!Num(a, out var start) || start >= length) return null;
        if (b.Length == 0) return (start, length - 1);
        if (!Num(b, out var end) || end < start) return null;
        return (start, Math.Min(end, length - 1));
    }

    static bool Status(NetworkStream stream, int code, string text, bool close, string extra = "")
    {
        stream.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {text}\r\n{extra}Content-Length: 0\r\n{(close ? "Connection: close\r\n" : "")}\r\n"));
        return !close;
    }

    public void Dispose()
    {
        stop.Cancel();
        try { listener.Stop(); } catch (Exception) { }
        foreach (var c in clients.Keys) c.Dispose();
        try { accepting.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        stop.Dispose();
    }
}
