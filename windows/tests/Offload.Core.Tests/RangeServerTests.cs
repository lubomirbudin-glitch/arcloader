using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// R3 tap server: Range semantics ffmpeg relies on, and its security — 127.0.0.1 only, token, no path in the URL, Host
/// header, methods, header size, connection limit, a client or a disk going away mid-body. Network: 127.0.0.1 only.
public class RangeServerTests
{
    sealed class Bytes(byte[] data) : ITapSource
    {
        public int Reads;
        public Exception? FailAt;
        public long Length => data.Length;

        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref Reads);
            if (FailAt != null && offset > 0) throw FailAt;
            int n = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
    }

    static byte[] Data(int size)
    {
        var d = new byte[size];
        new Random(size).NextBytes(d);
        return d;
    }

    /// Sends `request` as written (CRLF line ends) and returns the status line, the headers and the body.
    static (string Status, Dictionary<string, string> Headers, byte[] Body) Raw(int port, string request)
    {
        using var c = new TcpClient();
        c.Connect(IPAddress.Loopback, port);
        var s = c.GetStream();
        s.Write(Encoding.ASCII.GetBytes(request));
        using var all = new MemoryStream();
        try { s.CopyTo(all); } catch (IOException) { }
        var bytes = all.ToArray();
        int end = IndexOf(bytes, "\r\n\r\n"u8);
        if (end < 0) return ("", [], []);
        var head = Encoding.ASCII.GetString(bytes, 0, end).Split("\r\n");
        var headers = head.Skip(1).Select(h => h.Split(':', 2)).ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
        return (head[0], headers, bytes[(end + 4)..]);
    }

    static int IndexOf(byte[] a, ReadOnlySpan<byte> what) => a.AsSpan().IndexOf(what);

    static string Get(RangeServer server, string url, string? range = null, string method = "GET", string? host = null)
    {
        var path = new Uri(url).AbsolutePath;
        return $"{method} {path} HTTP/1.1\r\nHost: {host ?? $"127.0.0.1:{server.Port}"}\r\n{(range != null ? $"Range: {range}\r\n" : "")}Connection: close\r\n\r\n";
    }

    [Fact]
    public void ListensOnLoopbackOnly()
    {
        using var server = new RangeServer();
        Assert.Equal(IPAddress.Loopback, server.EndPoint.Address);
    }

    [Fact]
    public async Task FullGet_200_ExactBytes_AndAcceptsRanges()
    {
        var data = Data(3_000_001);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "A001C003.mov");
        Assert.EndsWith("/A001C003.mov", url);

        using var http = new HttpClient();
        var r = await http.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("bytes", Assert.Single(r.Headers.AcceptRanges));
        Assert.Equal(data, await r.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("bytes=10-19", 10, 19)]
    [InlineData("bytes=999990-", 999990, 999999)]
    [InlineData("bytes=-5", 999995, 999999)]
    [InlineData("bytes=5-5000000", 5, 999999)]
    public void Range_206_ContentRange_AndTheBytes(string range, int from, int to)
    {
        var data = Data(1_000_000);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "a.mov");

        var (status, headers, body) = Raw(server.Port, Get(server, url, range));

        Assert.Equal("HTTP/1.1 206 Partial Content", status);
        Assert.Equal($"bytes {from}-{to}/1000000", headers["Content-Range"]);
        Assert.Equal((to - from + 1).ToString(), headers["Content-Length"]);
        Assert.Equal(data[from..(to + 1)], body);
    }

    [Theory]
    [InlineData("bytes=1000000-")]
    [InlineData("bytes=0-1,5-6")]
    [InlineData("bytes=9-3")]
    [InlineData("items=0-1")]
    [InlineData("bytes=-0")]
    public void UnsatisfiableOrSeveralRanges_416(string range)
    {
        using var server = new RangeServer();
        var url = server.Register(new Bytes(Data(1_000_000)), "a.mov");
        var (status, headers, body) = Raw(server.Port, Get(server, url, range));
        Assert.Equal("HTTP/1.1 416 Range Not Satisfiable", status);
        Assert.Equal("bytes */1000000", headers["Content-Range"]);
        Assert.Empty(body);
    }

    [Fact]
    public void Head_HeadersOnly_NoRead()
    {
        var src = new Bytes(Data(1000));
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");
        var (status, headers, body) = Raw(server.Port, Get(server, url, method: "HEAD"));
        Assert.Equal("HTTP/1.1 200 OK", status);
        Assert.Equal("1000", headers["Content-Length"]);
        Assert.Empty(body);
        Assert.Equal(0, src.Reads);
    }

    // Security: without the exact token and a registered id nothing is served — and the URL's path never reaches the disk.
    [Theory]
    [InlineData("/{token}/{id}/../../Windows/win.ini")]
    [InlineData("/{token}/../{id}/a.mov")]
    [InlineData("/{token}/%2e%2e/{id}/a.mov")]
    [InlineData("/{token}/{id}")]
    [InlineData("/{token}/{id}/a.mov/extra")]
    [InlineData("/{wrongtoken}/{id}/a.mov")]
    [InlineData("/{token}/0000000000000000/a.mov")]
    [InlineData("/{token}x/{id}/a.mov")]
    [InlineData("//{token}/{id}/a.mov")]
    [InlineData("http://127.0.0.1/{token}/{id}/a.mov")]
    public void WrongToken_UnknownId_OrTraversal_404_NothingRead(string pattern)
    {
        var src = new Bytes(Data(1000));
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");
        var parts = new Uri(url).AbsolutePath.Split('/');
        var wrong = new string(parts[1].Reverse().ToArray());
        var target = pattern.Replace("{token}", parts[1]).Replace("{id}", parts[2]).Replace("{wrongtoken}", wrong);

        var (status, _, body) = Raw(server.Port, $"GET {target} HTTP/1.1\r\nHost: 127.0.0.1:{server.Port}\r\nConnection: close\r\n\r\n");

        Assert.Equal("HTTP/1.1 404 Not Found", status);
        Assert.Empty(body);
        Assert.Equal(0, src.Reads);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public void OtherMethods_405(string method)
    {
        var src = new Bytes(Data(10));
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");
        Assert.Equal("HTTP/1.1 405 Method Not Allowed", Raw(server.Port, Get(server, url, method: method)).Status);
        Assert.Equal(0, src.Reads);
    }

    // A web page that rebinds a name to 127.0.0.1 sends its own Host: refused.
    [Theory]
    [InlineData("evil.example:80")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void ForeignHost_403(string host)
    {
        var src = new Bytes(Data(10));
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");
        Assert.Equal("HTTP/1.1 403 Forbidden", Raw(server.Port, Get(server, url, host: host)).Status);
        Assert.Equal(0, src.Reads);
    }

    [Fact]
    public void OversizedHead_ConnectionClosed_ServerStillServes()
    {
        var data = Data(100);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "a.mov");
        var huge = $"GET {new Uri(url).AbsolutePath} HTTP/1.1\r\nHost: 127.0.0.1:{server.Port}\r\nX-Pad: {new string('a', RangeServer.MaxHeaderBytes)}\r\n\r\n";
        Assert.Equal("", Raw(server.Port, huge).Status);

        Assert.Equal(data, Raw(server.Port, Get(server, url)).Body);
    }

    [Fact]
    public void Unregistered_404()
    {
        using var server = new RangeServer();
        var url = server.Register(new Bytes(Data(10)), "a.mov");
        server.Unregister(url);
        Assert.Equal("HTTP/1.1 404 Not Found", Raw(server.Port, Get(server, url)).Status);
    }

    // ffmpeg closes a connection when it seeks: the server goes on with the next one.
    [Fact]
    public void ClientGoesAwayMidBody_NextRequestServed()
    {
        var data = Data(20_000_000);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "a.mov");
        using (var c = new TcpClient())
        {
            c.Connect(IPAddress.Loopback, server.Port);
            c.GetStream().Write(Encoding.ASCII.GetBytes(Get(server, url)));
            var some = new byte[1000];
            c.GetStream().ReadExactly(some);
        }
        var (status, _, body) = Raw(server.Port, Get(server, url, "bytes=19999990-"));
        Assert.Equal("HTTP/1.1 206 Partial Content", status);
        Assert.Equal(data[19999990..], body);
    }

    // A disk that fails mid-body: the connection is cut (ffmpeg sees a short read), the server stays up.
    [Fact]
    public void ReadFailsMidBody_ConnectionCut_Counted()
    {
        var data = Data(5_000_000);
        var src = new Bytes(data) { FailAt = new IOError("read", "x", 21) };
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");

        var (status, _, body) = Raw(server.Port, Get(server, url));

        Assert.Equal("HTTP/1.1 200 OK", status);
        Assert.True(body.Length < data.Length);
        Assert.Equal(1, server.ReadErrors);
        src.FailAt = null;
        Assert.Equal(data, Raw(server.Port, Get(server, url)).Body);
    }

    [Fact]
    public async Task KeepAlive_TwoRequestsOnOneConnection()
    {
        var data = Data(100);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "a.mov");
        using var http = new HttpClient();
        var a = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9) } });
        var b = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new System.Net.Http.Headers.RangeHeaderValue(90, 99) } });
        Assert.Equal(data[..10], await a.Content.ReadAsByteArrayAsync());
        Assert.Equal(data[90..], await b.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, server.Requests);
    }

    [Fact]
    public void ConnectionsOverTheLimit_Refused_NotQueued()
    {
        using var server = new RangeServer();
        var url = server.Register(new Bytes(Data(10)), "a.mov");
        var idle = Enumerable.Range(0, RangeServer.MaxConnections).Select(_ =>
        {
            var c = new TcpClient();
            c.Connect(IPAddress.Loopback, server.Port);
            return c;
        }).ToList();
        try
        {
            Thread.Sleep(300);   // every idle one holds a slot now
            Assert.Equal("", Raw(server.Port, Get(server, url)).Status);
        }
        finally
        {
            idle.ForEach(c => c.Dispose());
        }
        Assert.True(SpinWait.SpinUntil(() => Raw(server.Port, Get(server, url)).Status == "HTTP/1.1 200 OK", TimeSpan.FromSeconds(10)));
    }

    // ---- Plan 10 batch 7–9 security belts (beyond the brief) ----

    // Bound to 127.0.0.1 only: the machine's other addresses (and ::1) have no listener on that port.
    [Fact]
    public async Task NotReachable_OnAnyOtherAddressOfThisMachine()
    {
        using var server = new RangeServer();
        var others = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(u => u.Address))
            .Where(a => !IPAddress.IsLoopback(a) && a.AddressFamily == AddressFamily.InterNetwork)
            .Append(IPAddress.IPv6Loopback)
            .ToList();
        foreach (var address in others)
        {
            using var c = new TcpClient(address.AddressFamily);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            bool connected;
            try
            {
                await c.ConnectAsync(address, server.Port, cts.Token);
                connected = c.Connected;
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException) { connected = false; }
            Assert.False(connected, $"reachable on {address}");
        }
    }

    // A connection whose peer is not a loopback address is closed before a byte of it is read (belt behind the bind).
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.2", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.1.20", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("::ffff:192.168.1.20", false)]
    [InlineData("fe80::1", false)]
    public void PeerCheck_LoopbackOnly(string address, bool allowed) =>
        Assert.Equal(allowed, RangeServer.IsAllowedPeer(new IPEndPoint(IPAddress.Parse(address), 50000)));

    [Fact]
    public void PeerCheck_UnknownEndPoint_Refused() => Assert.False(RangeServer.IsAllowedPeer(null));

    // The token is random per server, the id random per file: 128 and 64 bits, in hex.
    [Fact]
    public void TokenPerServer_IdPerFile_Random()
    {
        using var a = new RangeServer();
        using var b = new RangeServer();
        var u1 = new Uri(a.Register(new Bytes(Data(10)), "a.mov")).AbsolutePath.Split('/');
        var u2 = new Uri(a.Register(new Bytes(Data(10)), "a.mov")).AbsolutePath.Split('/');
        var u3 = new Uri(b.Register(new Bytes(Data(10)), "a.mov")).AbsolutePath.Split('/');
        Assert.Equal(32, u1[1].Length);
        Assert.Equal(16, u1[2].Length);
        Assert.True(u1[1].All(char.IsAsciiHexDigitLower) && u1[2].All(char.IsAsciiHexDigitLower));
        Assert.Equal(u1[1], u2[1]);
        Assert.NotEqual(u1[2], u2[2]);
        Assert.NotEqual(u1[1], u3[1]);
    }

    // The URL never carries the file's folder: only a sanitised name (the demuxer's hint) after the token and the id.
    [Theory]
    [InlineData(@"D:\Media\Secret Project\CLIP\A001C003.mov", "A001C003.mov")]
    [InlineData(@"..\..\Windows\win.ini", "win.ini")]
    [InlineData("a b%2f?#.mov", "a_b_2f__.mov")]
    [InlineData(@"C:\", "clip")]
    public void Url_CarriesNoPath(string name, string last)
    {
        using var server = new RangeServer();
        var url = server.Register(new Bytes(Data(10)), name);
        var parts = new Uri(url).AbsolutePath.Split('/');
        Assert.Equal(4, parts.Length);
        Assert.Equal(last, parts[3]);
        Assert.DoesNotContain("Media", url);
        Assert.DoesNotContain("Windows", url);
    }

    // A range starting at or past the end is refused for GET and HEAD alike, and nothing is read; an empty file has no
    // satisfiable range at all.
    [Theory]
    [InlineData(1000, "bytes=1000-", "GET")]
    [InlineData(1000, "bytes=5000-6000", "GET")]
    [InlineData(1000, "bytes=1000-", "HEAD")]
    [InlineData(1000, "bytes=99999999999999999999-", "GET")]
    [InlineData(0, "bytes=0-", "GET")]
    [InlineData(0, "bytes=-1", "GET")]
    public void RangeBeyondTheEnd_416_NothingRead(int size, string range, string method)
    {
        var src = new Bytes(Data(size));
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");
        var (status, headers, body) = Raw(server.Port, Get(server, url, range, method));
        Assert.Equal("HTTP/1.1 416 Range Not Satisfiable", status);
        Assert.Equal($"bytes */{size}", headers["Content-Range"]);
        Assert.Empty(body);
        Assert.Equal(0, src.Reads);
    }

    // Preflight M7 (slowloris): the whole head must arrive within the header time, not each byte of it.
    [Fact]
    public void SlowHead_ClosedAtTheDeadline_EvenWhileBytesTrickleIn()
    {
        using var server = new RangeServer { HeaderTimeout = TimeSpan.FromSeconds(1) };
        var url = server.Register(new Bytes(Data(10)), "a.mov");
        using var c = new TcpClient();
        c.Connect(IPAddress.Loopback, server.Port);
        var s = c.GetStream();
        var request = Encoding.ASCII.GetBytes(Get(server, url));
        var started = Environment.TickCount64;
        bool closed = false;
        for (int i = 0; i < request.Length - 4 && Environment.TickCount64 - started < 8000; i++)
        {
            try { s.Write(request, i, 1); }
            catch (IOException) { closed = true; break; }
            Thread.Sleep(250);   // each gap well under the timeout
            if (c.Client.Poll(0, SelectMode.SelectRead) && c.Available == 0) { closed = true; break; }
        }
        Assert.True(closed, "the server kept a trickling head open");
        Assert.InRange(Environment.TickCount64 - started, 500, 6000);
    }

    // After the job (Dispose) nothing listens any more and open connections are cut.
    [Fact]
    public void Dispose_StopsListening_AndCutsOpenConnections()
    {
        var server = new RangeServer();
        var url = server.Register(new Bytes(Data(100)), "a.mov");
        int port = server.Port;
        using var open = new TcpClient();
        open.Connect(IPAddress.Loopback, port);
        Thread.Sleep(100);

        server.Dispose();

        var s = open.GetStream();
        s.ReadTimeout = 5000;
        int got;
        try { got = s.Read(new byte[16]); }
        catch (IOException) { got = 0; }
        Assert.Equal(0, got);
        using var late = new TcpClient();
        Assert.ThrowsAny<SocketException>(() => late.Connect(IPAddress.Loopback, port));
    }
}
