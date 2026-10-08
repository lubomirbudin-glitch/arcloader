using System.Net;
using System.Net.Sockets;

namespace Offload.Core.Tests;

/// A tiny HTTP server on 127.0.0.1 and a free port, for tests of ntfy and the update check — no real network.
/// `handle` answers each request (default: 200 with an empty body); every request is recorded.
public sealed class LocalHttp : IDisposable
{
    public sealed record Request(string Method, string Path, string? ContentType, string? UserAgent, byte[] Body);

    readonly HttpListener listener = new();
    readonly Func<HttpListenerContext, Task> handle;
    readonly List<Request> requests = [];
    readonly CancellationTokenSource stop = new();

    public string Url { get; }

    public LocalHttp(Func<HttpListenerContext, Task>? handle = null)
    {
        this.handle = handle ?? (c => { c.Response.StatusCode = 200; c.Response.Close(); return Task.CompletedTask; });
        Url = $"http://127.0.0.1:{FreePort()}/";
        listener.Prefixes.Add(Url);
        listener.Start();
        _ = Task.Run(Loop);
    }

    public IReadOnlyList<Request> Requests
    {
        get { lock (requests) return [.. requests]; }
    }

    /// Waits (bounded) until at least `count` requests are recorded; false when they did not come in time.
    public bool WaitForRequests(int count, TimeSpan timeout) => SpinWait.SpinUntil(() => Requests.Count >= count, timeout);

    /// A port nothing listens on now (another process may take it — rare enough for a test).
    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext c;
            try { c = await listener.GetContextAsync(); }
            catch (Exception) { return; }   // stopped
            using var body = new MemoryStream();
            // A client that gave up meanwhile (a timeout test) breaks the body: the request is recorded anyway and the
            // loop goes on.
            try { await c.Request.InputStream.CopyToAsync(body); }
            catch (Exception) { }
            lock (requests) requests.Add(new Request(c.Request.HttpMethod, c.Request.Url!.AbsolutePath, c.Request.ContentType, c.Request.UserAgent, body.ToArray()));
            _ = Task.Run(async () =>
            {
                try { await handle(c); }
                catch (Exception) { /* the client went away */ }
            });
        }
    }

    /// Answers with `status` and `body` (UTF-8).
    public static async Task Reply(HttpListenerContext c, int status, string body)
    {
        c.Response.StatusCode = status;
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        await c.Response.OutputStream.WriteAsync(bytes);
        c.Response.Close();
    }

    public void Dispose()
    {
        stop.Cancel();
        try { listener.Stop(); listener.Close(); }
        catch (Exception) { }
    }
}
