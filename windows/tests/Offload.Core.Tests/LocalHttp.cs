using System.Net;
using System.Net.Sockets;

namespace Offload.Core.Tests;

/// A tiny HTTP server on 127.0.0.1 and a free port, for tests of ntfy and the update check — no real network.
/// `handle` answers each request (default: 200 with an empty body); every request is recorded.
///
/// Two races made the update-check and ntfy tests fail under load (the full suite), fixed here:
/// - The port: FreePort() releases the port it found before HttpListener registers it, and another listener of a
///   parallel test (RangeServer binds 127.0.0.1:0) could take it in between — the client then talked to a stranger.
///   The constructor now proves the port is its own with a probe request that only this server knows, and takes
///   another port otherwise.
/// - The thread pool: the accept loop and the handlers ran on pool threads (GetContextAsync continuations,
///   Task.Run), so a busy pool delayed the answer past the client's limit. They now run on threads of their own and
///   Reply writes synchronously.
public sealed class LocalHttp : IDisposable
{
    public sealed record Request(string Method, string Path, string? ContentType, string? UserAgent, byte[] Body);

    readonly HttpListener listener;
    readonly Func<HttpListenerContext, Task> handle;
    readonly List<Request> requests = [];
    readonly string probePath = "/__localhttp-probe-" + Guid.NewGuid().ToString("N");
    volatile bool stopped;

    public string Url { get; }

    public LocalHttp(Func<HttpListenerContext, Task>? handle = null)
    {
        this.handle = handle ?? (c => { c.Response.StatusCode = 200; c.Response.Close(); return Task.CompletedTask; });
        for (int attempt = 1; ; attempt++)
        {
            var url = $"http://127.0.0.1:{FreePort()}/";
            var l = new HttpListener();
            l.Prefixes.Add(url);
            try
            {
                l.Start();
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                l.Close();   // the port was taken meanwhile: another one
                continue;
            }
            listener = l;
            new Thread(Loop) { IsBackground = true, Name = "LocalHttp accept" }.Start();
            if (Probe(url)) { Url = url; return; }
            stopped = true;
            try { l.Stop(); l.Close(); }
            catch (Exception) { }
            if (attempt >= 20) throw new InvalidOperationException("LocalHttp: no port of its own after 20 attempts");
            stopped = false;
        }
    }

    /// Whether a request to `url` reaches this server (and not another socket on the same port). Synchronous, on the
    /// calling thread — no pool thread involved.
    bool Probe(string url)
    {
        try
        {
            using var client = new TcpClient();
            var port = new Uri(url).Port;
            client.Connect(IPAddress.Loopback, port);
            client.ReceiveTimeout = client.SendTimeout = 60_000;
            using var stream = client.GetStream();
            var ask = System.Text.Encoding.ASCII.GetBytes($"GET {probePath} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n");
            stream.Write(ask);
            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
            var status = reader.ReadLine() ?? "";
            string? line;
            bool ours = false;
            while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                if (line.Equals("X-LocalHttp: " + probePath, StringComparison.Ordinal)) ours = true;
            return ours && status.Contains(" 204", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            return false;
        }
    }

    public IReadOnlyList<Request> Requests
    {
        get { lock (requests) return [.. requests]; }
    }

    /// Waits (bounded) until at least `count` requests are recorded; false when they did not come in time.
    public bool WaitForRequests(int count, TimeSpan timeout) => SpinWait.SpinUntil(() => Requests.Count >= count, timeout);

    /// A port nothing listens on now (another process may take it — rare enough for a test that wants a closed port;
    /// a LocalHttp checks it got its port with a probe).
    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    void Loop()
    {
        var l = listener;
        while (!stopped)
        {
            HttpListenerContext c;
            try { c = l.GetContext(); }
            catch (Exception) { return; }   // stopped
            if (c.Request.Url!.AbsolutePath == probePath)
            {
                try
                {
                    c.Response.StatusCode = 204;
                    c.Response.Headers["X-LocalHttp"] = probePath;
                    c.Response.Close();
                }
                catch (Exception) { }
                continue;
            }
            new Thread(() => Serve(c)) { IsBackground = true, Name = "LocalHttp request" }.Start();
        }
    }

    void Serve(HttpListenerContext c)
    {
        using var body = new MemoryStream();
        // A client that gave up meanwhile (a timeout test) breaks the body: the request is recorded anyway.
        try { c.Request.InputStream.CopyTo(body); }
        catch (Exception) { }
        lock (requests) requests.Add(new Request(c.Request.HttpMethod, c.Request.Url!.AbsolutePath, c.Request.ContentType, c.Request.UserAgent, body.ToArray()));
        try { handle(c).GetAwaiter().GetResult(); }
        catch (Exception) { /* the client went away, or the server was disposed */ }
    }

    /// Answers with `status` and `body` (UTF-8) — synchronously, so the answer never waits for a pool thread.
    public static Task Reply(HttpListenerContext c, int status, string body)
    {
        c.Response.StatusCode = status;
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        c.Response.ContentLength64 = bytes.Length;
        c.Response.OutputStream.Write(bytes);
        c.Response.Close();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        stopped = true;
        try { listener.Stop(); listener.Close(); }
        catch (Exception) { }
    }
}
