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
        new() { Phase = phase, ProxiesDone = proxiesDone, ProxiesFailed = proxiesFailed, Warning = warning, Error = error, Note = note, SafeToEject = safe, CopySucceeded = true };

    [Fact]
    public void Done_VerifiedOnDisks_WithProxies()
    {
        var o = JobOutcomes.Final("A001", Status(JobPhase.Done, proxiesDone: 24), Report())!;
        Assert.Equal((OutcomeKind.Done, "Offload: A001 hotovo", "Ověřeno na 1 disku. Proxy: 24 hotovo."), (o.Kind, o.Title, o.Message));
        Assert.Equal(o.Message, o.PhoneMessage);
        Assert.Equal((3, "white_check_mark"), (o.Priority, o.Tags.Single()));
        Assert.Equal("Ověřeno na 1 disku.", JobOutcomes.Final("A001", Status(JobPhase.Done), Report())!.Message);
    }

    [Fact]
    public void Attention_Cases()
    {
        var unverified = JobOutcomes.Final("A001", Status(JobPhase.Done), Report(verified: false))!;
        Assert.Equal((OutcomeKind.Attention, "Offload: A001 — vyžaduje pozornost", "Kopie je hotová, ale NEOVĚŘENÁ."), (unverified.Kind, unverified.Title, unverified.Message));
        Assert.Equal((4, "warning"), (unverified.Priority, unverified.Tags.Single()));
        Assert.Equal("Kopie je ověřená, ale 2 proxy selhaly.", JobOutcomes.Final("A001", Status(JobPhase.Done, proxiesFailed: 2), Report())!.Message);
        Assert.Equal("Kopie je ověřená, ale zpracování po kopii selhalo.", JobOutcomes.Final("A001", Status(JobPhase.Failed, error: "x"), Report())!.Message);

        var space = Msg.ProxiesSkippedForSpace("Zaloha (D:)", 30_000_000_000, 12_000_000_000);
        var skipped = JobOutcomes.Final("A001", Status(JobPhase.Done, warning: space), Report())!;
        Assert.Equal(OutcomeKind.Attention, skipped.Kind);
        Assert.Equal("Kopie je ověřená, ale: " + space, skipped.Message);
        Assert.Equal("Kopie je ověřená, ale vyžaduje pozornost — podrobnosti v aplikaci.", skipped.PhoneMessage);
    }

    [Fact]
    public void Error_FailedFiles_OrBeforeTheCopy()
    {
        var failed = JobOutcomes.Final("A001", Status(JobPhase.Done), Report(destError: "write failed"))!;
        Assert.Equal((OutcomeKind.Error, "Offload: A001 — CHYBA", "1 soubor selhal — karta NENÍ zálohovaná."), (failed.Kind, failed.Title, failed.Message));
        Assert.Equal((5, "rotating_light"), (failed.Priority, failed.Tags.Single()));
        var preflight = JobOutcomes.Final("A001", Status(JobPhase.Failed, error: @"Cíl nenalezen: D:\Zaloha — je disk připojený?"), null)!;
        Assert.Equal(@"Cíl nenalezen: D:\Zaloha — je disk připojený?", preflight.Message);   // Windows: the error itself
        Assert.Equal("Chyba před kopírováním — karta NENÍ zálohovaná.", preflight.PhoneMessage);
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
        Assert.Equal((OutcomeKind.Ejectable, "Offload: A001 — kartu lze vysunout", "Proxy se ještě renderují."), (e.Kind, e.Title, e.Message));
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
            (Status(JobPhase.Done, warning: @"kopie do E:\Proxy selhala"), Report()),
            (Status(JobPhase.Done), Report(destError: @"write failed D:\x")),
            (Status(JobPhase.Failed, error: @"Zdroj nenalezen: E:\"), null),
            (Status(JobPhase.Done, note: Msg.NothingNew), null),
        };
        foreach (var (status, report) in statuses)
        {
            var o = JobOutcomes.Final("A001", status, report)!;
            foreach (var text in new[] { o.Title, o.PhoneMessage })
            {
                Assert.DoesNotContain("\\", text);
                Assert.DoesNotMatch(@"\b[A-Za-z]:", Regex.Replace(text, "^Offload: ", ""));
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
        var o = new JobOutcome(OutcomeKind.Attention, "Offload: A001 — vyžaduje pozornost", "x", "Kopie je ověřená, ale 2 proxy selhaly.");

        var r = await NtfyClient.SendAsync(server.Url.TrimEnd('/'), "film-x_7", o.Title, o.PhoneMessage, o.Priority, o.Tags);

        Assert.Equal(new NtfyResult(true, null), r);
        var req = Assert.Single(server.Requests);
        Assert.Equal(("POST", "/"), (req.Method, req.Path));
        Assert.Equal("application/json; charset=utf-8", req.ContentType);
        Assert.StartsWith("Offload/", req.UserAgent);
        using var doc = JsonDocument.Parse(req.Body);
        var root = doc.RootElement;
        Assert.Equal("film-x_7", root.GetProperty("topic").GetString());
        Assert.Equal("Offload: A001 — vyžaduje pozornost", root.GetProperty("title").GetString());
        Assert.Equal("Kopie je ověřená, ale 2 proxy selhaly.", root.GetProperty("message").GetString());
        Assert.Equal(4, root.GetProperty("priority").GetInt32());
        Assert.Equal("warning", root.GetProperty("tags")[0].GetString());
        Assert.Contains("ověřená", Encoding.UTF8.GetString(req.Body));   // raw UTF-8, not only escapes
    }

    [Fact]
    public async Task ServerError_IsAResult_NotAnException()
    {
        using var server = new LocalHttp(c => LocalHttp.Reply(c, 429, "slow down"));
        var r = await NtfyClient.SendAsync(server.Url, "t", "a", "b", 3, []);
        Assert.False(r.Ok);
        Assert.StartsWith("Server odpověděl 429", r.Error);
    }

    // Review focus 3: a server that never answers — the send gives up after its limit (10 s; here 300 ms).
    [Fact]
    public async Task SilentServer_GivesUpAfterTheLimit()
    {
        using var server = new LocalHttp(_ => Task.Delay(Timeout.Infinite));
        var sw = Stopwatch.StartNew();
        var r = await NtfyClient.SendAsync(server.Url, "t", "a", "b", 3, [], timeout: TimeSpan.FromMilliseconds(300));
        Assert.False(r.Ok);
        Assert.Equal("Server neodpověděl do 0.3 s.", r.Error!.Replace(',', '.'));
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

    // Review focus 3: a topic with diacritics never leaves the PC (ntfy would refuse it) — said in Czech.
    [Theory]
    [InlineData("můj-kanál", "Téma smí mít jen písmena bez diakritiky, číslice, - a _ (nejvýš 64 znaků).")]
    [InlineData("", "Téma není vyplněné.")]
    [InlineData("a/b", "Téma smí mít jen písmena bez diakritiky, číslice, - a _ (nejvýš 64 znaků).")]
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
        var outcome = new JobOutcome(OutcomeKind.Done, "Offload: A001 hotovo", "x", "Ověřeno na 2 discích.");

        var sw = Stopwatch.StartNew();
        var send = NtfyClient.SendOutcomeAsync(settings, outcome, m => { lock (log) log.Add(m); }, TimeSpan.FromMilliseconds(500));
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(400), sw.Elapsed.ToString());
        Assert.False(send.IsCompleted);
        await send;
        lock (log) Assert.Equal("ntfy: Server neodpověděl do 0.5 s.", Assert.Single(log).Replace(',', '.'));

        await NtfyClient.SendOutcomeAsync(new NtfySettings { Enabled = false, Server = server.Url, Topic = "t" }, outcome, log.Add);
        await NtfyClient.SendOutcomeAsync(settings, outcome with { Kind = OutcomeKind.Ejectable }, log.Add);
        Assert.Single(server.Requests);   // off, and "kartu lze vysunout": nothing sent
    }
}
