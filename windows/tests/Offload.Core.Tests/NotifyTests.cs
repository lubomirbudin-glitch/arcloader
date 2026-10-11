using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Offload.Core.Notify;

namespace Offload.Core.Tests;

/// Spec 4.3: the outcome table, the texts (no paths on the phone) and the ntfy client against a local HTTP server.
public class JobOutcomeTests
{
    static OffloadReport Report(bool verified = true, string? destError = null, string card = @"D:\Zaloha\2026-10-06\A001")
    {
        var r = Reports.Sample(card, verified, destError);
        return r;
    }

    static JobStatus Status(JobPhase phase, int proxiesDone = 0, int proxiesFailed = 0, string? warning = null, string? error = null, string? note = null,
        bool safe = true) =>
        new() { Phase = phase, ProxiesDone = proxiesDone, ProxiesFailed = proxiesFailed, Warning = warning, Error = error, Note = note, SafeToEject = safe, SafeToFormat = safe, CopySucceeded = true };

    [Fact]
    public void Done_VerifiedOnDisks_WithProxies()
    {
        var o = JobOutcomes.Final("A001", Status(JobPhase.Done, proxiesDone: 24), Report())!;
        Assert.Equal((OutcomeKind.Done, "ArcLoader: A001 done", "Verified on 1 disk — the card is safe to format. Proxies: 24 done."), (o.Kind, o.Title, o.Message));
        Assert.Equal(o.Message, o.PhoneMessage);
        Assert.Equal((3, "white_check_mark"), (o.Priority, o.Tags.Single()));
        Assert.Equal("Verified on 1 disk — the card is safe to format.", JobOutcomes.Final("A001", Status(JobPhase.Done), Report())!.Message);
    }

    [Fact]
    public void Attention_Cases()
    {
        var unverified = JobOutcomes.Final("A001", Status(JobPhase.Done), Report(verified: false))!;
        Assert.Equal((OutcomeKind.Attention, "ArcLoader: A001 — needs attention", "The copy is done but NOT VERIFIED."), (unverified.Kind, unverified.Title, unverified.Message));
        Assert.Equal((4, "warning"), (unverified.Priority, unverified.Tags.Single()));
        Assert.Equal("The copy is verified, but 2 proxies failed.", JobOutcomes.Final("A001", Status(JobPhase.Done, proxiesFailed: 2), Report())!.Message);
        Assert.Equal("The copy is verified, but the processing after the copy failed.", JobOutcomes.Final("A001", Status(JobPhase.Failed, error: "x"), Report())!.Message);

        var space = Msg.ProxiesSkippedForSpace("Zaloha (D:)", 30_000_000_000, 12_000_000_000);
        var skipped = JobOutcomes.Final("A001", Status(JobPhase.Done, warning: space), Report())!;
        Assert.Equal(OutcomeKind.Attention, skipped.Kind);
        Assert.Equal("The copy is verified, but: " + space, skipped.Message);
        Assert.Equal("The copy is verified, but needs attention — details in the app.", skipped.PhoneMessage);
    }

    // Final review, item 5: a report verified as read, but the card not SafeToFormat (e.g. the results not written into
    // every folder) — never "ověřená" in the notification, only "hotová".
    [Fact]
    public void Attention_NotSafeToFormat_NeverSaysVerified()
    {
        var space = Msg.ProxiesSkippedForSpace("Zaloha (D:)", 30_000_000_000, 12_000_000_000);
        var outcomes = new[]
        {
            JobOutcomes.Final("A001", Status(JobPhase.Done, proxiesFailed: 2, safe: false), Report())!,
            JobOutcomes.Final("A001", Status(JobPhase.Failed, error: "x", safe: false), Report())!,
            JobOutcomes.Final("A001", Status(JobPhase.Done, warning: space, safe: false), Report())!,
        };
        Assert.Equal("The copy is done, but 2 proxies failed.", outcomes[0].Message);
        Assert.Equal("The copy is done, but the processing after the copy failed.", outcomes[1].Message);
        Assert.Equal("The copy is done, but: " + space, outcomes[2].Message);
        Assert.Equal("The copy is done, but needs attention — details in the app.", outcomes[2].PhoneMessage);
        Assert.All(outcomes, o =>
        {
            Assert.Equal(OutcomeKind.Attention, o.Kind);
            Assert.DoesNotContain("verified", (o.Message + o.PhoneMessage).Replace("NOT VERIFIED", "").Replace("not fully verified", ""), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Error_FailedFiles_OrBeforeTheCopy()
    {
        var failed = JobOutcomes.Final("A001", Status(JobPhase.Done), Report(destError: "write failed"))!;
        Assert.Equal((OutcomeKind.Error, "ArcLoader: A001 — ERROR", "1 file failed — the card is NOT backed up."), (failed.Kind, failed.Title, failed.Message));
        Assert.Equal((5, "rotating_light"), (failed.Priority, failed.Tags.Single()));
        var preflight = JobOutcomes.Final("A001", Status(JobPhase.Failed, error: @"Destination not found: D:\Backup — is the disk connected?"), null)!;
        Assert.Equal(@"Destination not found: D:\Backup — is the disk connected?", preflight.Message);   // Windows: the error itself
        Assert.Equal("Error before copying — the card is NOT backed up.", preflight.PhoneMessage);
    }

    [Fact]
    public void Cancelled_NoNotification_NothingNew_Done()
    {
        Assert.Null(JobOutcomes.Final("A001", Status(JobPhase.Cancelled), Report()));
        var nothing = JobOutcomes.Final("A001", Status(JobPhase.Done, note: Msg.NothingNew), null)!;
        Assert.Equal((OutcomeKind.Done, Msg.NothingNew, Msg.NothingNew), (nothing.Kind, nothing.Message, nothing.PhoneMessage));
        var unverifiedEarlier = JobOutcomes.Final("A001", Status(JobPhase.Done, note: Msg.NothingNew, warning: Msg.UnverifiedEarlier(5)), null)!;
        Assert.Equal(OutcomeKind.Attention, unverifiedEarlier.Kind);
    }

    [Fact]
    public void Ejectable_OnlyWhileStageTwoRuns_WindowsOnly()
    {
        var e = JobOutcomes.CopyDone("A001", Status(JobPhase.Proxies), proxies: true)!;
        Assert.Equal((OutcomeKind.Ejectable, "ArcLoader: A001 — safe to eject", "Proxies are still rendering."), (e.Kind, e.Title, e.Message));
        Assert.False(e.ForPhone);
        Assert.Null(JobOutcomes.CopyDone("A001", Status(JobPhase.Done), proxies: true));
        Assert.Null(JobOutcomes.CopyDone("A001", Status(JobPhase.Analyzing, safe: false), proxies: false));
    }

    // R20 / spec 5.6: the phone gets no path, no file name: no "\" and no drive letter "X:" in the title or the text.
    [Fact]
    public void PhoneTexts_NeverAPath()
    {
        var statuses = new (JobStatus Status, OffloadReport? Report)[]
        {
            (Status(JobPhase.Done, proxiesDone: 3), Report()),
            (Status(JobPhase.Done), Report(verified: false)),
            (Status(JobPhase.Done, warning: Msg.ProxiesSkippedForSpace("Zaloha (D:)", 1, 1)), Report()),
            (Status(JobPhase.Done, warning: @"the copy to E:\Proxy failed"), Report()),
            (Status(JobPhase.Done), Report(destError: @"write failed D:\x")),
            (Status(JobPhase.Failed, error: @"Source not found: E:\"), null),
            (Status(JobPhase.Done, note: Msg.NothingNew), null),
        };
        foreach (var (status, report) in statuses)
        {
            var o = JobOutcomes.Final("A001", status, report)!;
            foreach (var text in new[] { o.Title, o.PhoneMessage })
            {
                Assert.DoesNotContain("\\", text);
                Assert.DoesNotMatch(@"\b[A-Za-z]:", Regex.Replace(text, "^ArcLoader: ", ""));
            }
        }
    }
}

public class NtfyClientTests
{
    [Fact]
    public async Task Send_JsonShape_Utf8_Priority_NoPathInUrl()
    {
        using var server = new LocalHttp();
        var o = new JobOutcome(OutcomeKind.Attention, "ArcLoader: Č001 — needs attention", "x", "The copy is verified, but 2 proxies failed.");

        var r = await NtfyClient.SendAsync(server.Url.TrimEnd('/'), "film-x_7", o.Title, o.PhoneMessage, o.Priority, o.Tags);

        Assert.Equal(new NtfyResult(true, null), r);
        var req = Assert.Single(server.Requests);
        Assert.Equal(("POST", "/"), (req.Method, req.Path));
        Assert.Equal("application/json; charset=utf-8", req.ContentType);
        Assert.Matches(@"^ArcLoader/\d+\.\d+\.\d+$", req.UserAgent);   // M17: no "-win" suffix
        using var doc = JsonDocument.Parse(req.Body);
        var root = doc.RootElement;
        Assert.Equal("film-x_7", root.GetProperty("topic").GetString());
        Assert.Equal("ArcLoader: Č001 — needs attention", root.GetProperty("title").GetString());
        Assert.Equal("The copy is verified, but 2 proxies failed.", root.GetProperty("message").GetString());
        Assert.Equal(4, root.GetProperty("priority").GetInt32());
        Assert.Equal("warning", root.GetProperty("tags")[0].GetString());
        Assert.Contains("Č001 —", Encoding.UTF8.GetString(req.Body));   // raw UTF-8, not only escapes (a card name may have any letters)
    }

    [Fact]
    public async Task ServerError_IsAResult_NotAnException()
    {
        using var server = new LocalHttp(c => LocalHttp.Reply(c, 429, "slow down"));
        var r = await NtfyClient.SendAsync(server.Url, "t", "a", "b", 3, []);
        Assert.False(r.Ok);
        Assert.StartsWith("The server answered 429", r.Error);
    }

    // Review focus 3: a server that never answers — the send gives up after its limit (10 s; here 300 ms).
    [Fact]
    public async Task SilentServer_GivesUpAfterTheLimit()
    {
        using var server = new LocalHttp(_ => Task.Delay(Timeout.Infinite));
        var sw = Stopwatch.StartNew();
        var r = await NtfyClient.SendAsync(server.Url, "t", "a", "b", 3, [], timeout: TimeSpan.FromMilliseconds(300));
        Assert.False(r.Ok);
        Assert.Equal("The server did not answer within 0.3 s.", r.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
    }

    [Fact]
    public async Task NothingListening_IsAResult()
    {
        var r = await NtfyClient.SendAsync($"http://127.0.0.1:{LocalHttp.FreePort()}", "t", "a", "b", 3, [], timeout: TimeSpan.FromSeconds(5));
        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
    }

    // Review focus 3: a TLS failure (https to a server that speaks plain HTTP) is a result too, never an exception.
    [Fact]
    public async Task TlsFailure_IsAResult()
    {
        using var server = new LocalHttp();
        var r = await NtfyClient.SendAsync(server.Url.Replace("http://", "https://"), "t", "a", "b", 3, [], timeout: TimeSpan.FromSeconds(5));
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    // Review focus 3: a topic with diacritics never leaves the PC (ntfy would refuse it) — said why.
    [Theory]
    [InlineData("můj-kanál", "The topic may contain only letters without accents, digits, - and _ (at most 64 characters).")]
    [InlineData("", "The topic is empty.")]
    [InlineData("a/b", "The topic may contain only letters without accents, digits, - and _ (at most 64 characters).")]
    public async Task BadTopic_RefusedLocally(string topic, string error)
    {
        using var server = new LocalHttp();
        var r = await NtfyClient.SendAsync(server.Url, topic, "a", "b", 3, []);
        Assert.Equal(new NtfyResult(false, error), r);
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("https://ntfy.sh/muj-tajny-kanal", "https://ntfy.sh", "muj-tajny-kanal")]
    [InlineData("https://ntfy.example.com/sub/kanal_1/", "https://ntfy.example.com/sub", "kanal_1")]
    public void ParseUrl_ServerAndTopic(string url, string server, string topic) =>
        Assert.Equal((server, topic), NtfyClient.ParseUrl(url));

    [Theory]
    [InlineData("ntfy.sh/kanal")]
    [InlineData("https://ntfy.sh")]
    [InlineData("https://ntfy.sh/kanál")]
    [InlineData("ftp://x/kanal")]
    public void ParseUrl_Bad_IsNull(string url) => Assert.Null(NtfyClient.ParseUrl(url));

    // The job never waits: the phone notification is started and the caller goes on; its result goes to the log.
    [Fact]
    public async Task SendOutcome_DoesNotHoldTheCaller_LogsTheResult()
    {
        using var server = new LocalHttp(_ => Task.Delay(Timeout.Infinite));
        var settings = new NtfySettings { Enabled = true, Server = server.Url, Topic = "t" };
        var log = new List<string>();
        var outcome = new JobOutcome(OutcomeKind.Done, "ArcLoader: A001 done", "x", "Verified on 2 disks.");

        var send = NtfyClient.SendOutcomeAsync(settings, outcome, m => { lock (log) log.Add(m); }, TimeSpan.FromMilliseconds(500));
        // No wall-clock assert: the server never answers, so the call returning with an unfinished task IS the proof that
        // the caller is not held; the result arrives later (bounded wait, never an elapsed-time check).
        Assert.False(send.IsCompleted);
        await send.WaitAsync(TimeSpan.FromSeconds(30));
        lock (log) Assert.Equal("ntfy: The server did not answer within 0.5 s.", Assert.Single(log));
        // Whether the request reached the listener before the 0.5 s limit cancelled it depends on the machine's load (a
        // connect or a pool thread starved for longer than the limit: the client gives up before sending anything), so
        // nothing here waits for it. "Nothing is sent" is proven on a server of its own: those calls are awaited and
        // finish without a request, so it holds the moment they return.
        using var untouched = new LocalHttp();
        var off = new NtfySettings { Enabled = false, Server = untouched.Url, Topic = "t" };
        await NtfyClient.SendOutcomeAsync(off, outcome, log.Add);
        await NtfyClient.SendOutcomeAsync(new NtfySettings { Enabled = true, Server = untouched.Url, Topic = "t" }, outcome with { Kind = OutcomeKind.Ejectable }, log.Add);
        Assert.Empty(untouched.Requests);   // off, and "kartu lze vysunout": nothing sent
        lock (log) Assert.Single(log);
    }

    // Final fix 2: only the headers are read — a server that streams a huge body after an error status does not hold
    // the client (and its memory) until the body ends; the reason phrase is capped.
    [Fact]
    public async Task ErrorWithEndlessBody_ReturnsPromptly_ReasonPhraseCapped()
    {
        using var server = new LocalHttp(async c =>
        {
            c.Response.StatusCode = 500;
            c.Response.StatusDescription = new string('x', 2000);
            c.Response.SendChunked = true;
            var chunk = new byte[64 * 1024];
            var until = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < until)
            {
                await c.Response.OutputStream.WriteAsync(chunk);
                await c.Response.OutputStream.FlushAsync();
                await Task.Delay(5);
            }
            c.Response.Close();
        });
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var r = await NtfyClient.SendAsync(server.Url, "t", "a", "b", 3, [], timeout: TimeSpan.FromSeconds(6));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
        Assert.False(r.Ok);
        Assert.StartsWith("The server answered 500", r.Error);
        Assert.True(r.Error!.Length < 600, $"reason phrase not capped: {r.Error.Length}");
    }
}
