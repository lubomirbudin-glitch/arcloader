using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core.Tests;

public class SpaceCheckTests
{
    const long GB = 1_000_000_000;
    static readonly ExternalTools Tools = new() { Ffmpeg = "f" };
    static readonly SpaceTarget D = new(@"D:\Zaloha", "vol-d", "Zaloha (D:)");
    static readonly SpaceTarget F = new(@"F:\Zaloha", "vol-f", "Zaloha (F:)");

    static CardInsight Card(long bytes, int files) =>
        new() { Source = @"E:\", TotalBytes = bytes, FileCount = files, Clips = [] };

    [Fact]
    public void InspectedCard_OnEveryDestination()
    {
        var demands = SpaceCheck.ForCard(Card(100 * GB, 10), null, new ProxySettings(), Tools, [D, F], null);
        Assert.Equal(["vol-d", "vol-f"], demands.Select(d => d.Volume));
        Assert.All(demands, d => Assert.Equal(SpaceEstimator.CardBytes(100 * GB, 10), d.Need.Card));
    }

    [Fact]
    public void NotInspectedYet_UsedSpaceOfTheVolume_OrNothing()
    {
        Assert.Equal(SpaceEstimator.CardBytes(64 * GB, 0), SpaceCheck.ForCard(null, 64 * GB, new ProxySettings(), Tools, [D], null).Single().Need.Card);
        Assert.Empty(SpaceCheck.ForCard(null, null, new ProxySettings(), Tools, [D], null));
    }

    // Review focus 5: two cards for the same disk + a running job + the proxy folder on that disk.
    [Fact]
    public void TwoCards_RunningJob_ProxyFolder_OnOneVolume()
    {
        var proxy = new ProxySettings { Enabled = true, ExtraFolder = @"D:\Proxy" };
        var clip = new Clip { Name = "A001C001", Path = "CLIP/A001C001.mov", Format = ClipFormat.Video, Segments = ["CLIP/A001C001.mov"], Bytes = 50 * GB };
        var withClip = Card(50 * GB, 1) with { Clips = [clip] };
        var folder = new SpaceTarget(@"D:\Proxy", "vol-d", @"D:\Proxy");
        var demands = new List<VolumeDemand>();
        demands.AddRange(SpaceCheck.ForCard(withClip, null, proxy, Tools, [D], folder));
        demands.AddRange(SpaceCheck.ForCard(Card(100 * GB, 1), null, proxy, Tools, [D], folder));
        var running = new VolumeDemand("vol-d", "Zaloha (D:)", new SpaceNeed(200 * GB, 0, 0));
        demands.Add(running with { Need = SpaceCheck.Remaining(running.Need, JobPhase.Copying, 150 * GB) });

        var p = Assert.Single(SpacePlanner.Check(demands, new Dictionary<string, long> { ["vol-d"] = 200 * GB }));

        Assert.Equal(SpaceSeverity.Blocking, p.Severity);
        Assert.Equal(@"Zaloha (D:), D:\Proxy", p.Label);
        Assert.True(p.Need.Proxy > 2 * 8 * GB);   // the proxy twice: on the destination and in the folder
    }

    [Fact]
    public void Append_OnlyNewFiles_OnTheBackupsDestinations()
    {
        var e = HistoryFixtures.Entry(HistoryFixtures.T0);
        var m = new HistoryMatch(e, HistoryMatchKind.NewFiles, [new SourceFile("CLIP/A001C003.mov", 3 * GB, HistoryFixtures.T0)], 3 * GB, [], 0);
        var demands = SpaceCheck.ForAppend(m, new ProxySettings(), Tools, [D, new SpaceTarget(@"G:\Other", "vol-g", "Other (G:)")], null);
        var d = Assert.Single(demands);   // F:\Zaloha of the backup is not in the Cíle section: the engine checks it
        Assert.Equal(("vol-d", SpaceEstimator.CardBytes(3 * GB, 1)), (d.Volume, d.Need.Card));
    }

    [Fact]
    public void Append_KeepsTheOriginalDestinationIndex_ProxyStaysOnTheFirstDestination()
    {
        var e = HistoryFixtures.Entry(HistoryFixtures.T0);   // D:\Zaloha (first), F:\Zaloha (second)
        var clip = new SourceFile("CLIP/A001C003.mov", 3 * GB, HistoryFixtures.T0);
        var m = new HistoryMatch(e, HistoryMatchKind.NewFiles, [clip], 3 * GB, [], 0);
        var proxy = new ProxySettings { Enabled = true, CopyToAllDestinations = false };
        var onlyF = SpaceCheck.ForAppend(m, proxy, Tools, [F], null);
        Assert.Equal(0, Assert.Single(onlyF).Need.Proxy);   // F is the second destination: no proxy there
        var both = SpaceCheck.ForAppend(m, proxy, Tools, [D, F], null);
        Assert.True(both[0].Need.Proxy >= 0 && both[1].Need.Proxy == 0);
    }

    [Theory]
    [InlineData(JobPhase.Waiting, 100, 10)]
    [InlineData(JobPhase.Copying, 60, 10)]
    [InlineData(JobPhase.Proxies, 0, 10)]
    [InlineData(JobPhase.Done, 0, 0)]
    [InlineData(JobPhase.Cancelled, 0, 0)]
    public void Remaining_ByPhase(JobPhase phase, long card, long proxy)
    {
        var r = SpaceCheck.Remaining(new SpaceNeed(100, 10, 0), phase, written: 40);
        Assert.Equal((card, proxy), (r.Card, r.Proxy));
    }
}

public class StartWaitTests
{
    [Fact]
    public void PendingInspections_NoResultYet_AndHistoryNotLoadedOrLookRunning()
    {
        string[] cards = ["E:\\", "F:\\", "G:\\", "H:\\"];
        var done = new HashSet<string> { "G:\\" };
        var running = new HashSet<string> { "E:\\" };
        // history loaded: only the card whose look is running waits (F: has none planned — the scan starts it, not Start)
        Assert.Equal(["E:\\"], StartWait.PendingInspections(cards, done.Contains, running.Contains, historyLoaded: true));
        // history still loading: every card without a result waits
        Assert.Equal(["E:\\", "F:\\", "H:\\"], StartWait.PendingInspections(cards, done.Contains, running.Contains, historyLoaded: false));
    }

    [Fact]
    public async Task UntilAsync_ReturnsAsSoonAsNothingIsPending_WithCzechStatus()
    {
        int left = 3;
        var seen = new List<string>();
        bool ok = await StartWait.UntilAsync(() => left--, n => seen.Add(StartWait.InspectionStatus(n)), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(5));
        Assert.True(ok);
        Assert.Equal(["Prověřuji karty (3), než začnu kopírovat…", "Prověřuji karty (2), než začnu kopírovat…", "Prověřuji kartu, než začnu kopírovat…"], seen);
    }

    [Fact]
    public async Task UntilAsync_IsBounded_WhenACardNeverAnswers()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool ok = await StartWait.UntilAsync(() => 1, null, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(10));
        Assert.False(ok);
        Assert.InRange(clock.ElapsedMilliseconds, 100, 3000);
    }

    [Fact]
    public async Task AllWithin_TrueWhenAnswered_FalseOnTimeout()
    {
        Assert.True(await StartWait.AllWithinAsync([Task.Delay(10), Task.CompletedTask], TimeSpan.FromSeconds(5)));
        var never = new TaskCompletionSource();
        Assert.False(await StartWait.AllWithinAsync([never.Task], TimeSpan.FromMilliseconds(80)));
    }

    // Ruling S1: the wait ran out - the question names only the cards still pending; Ne stops, Ano goes on.
    [Fact]
    public void WaitTimedOut_AsksAboutThePendingCardsOnly()
    {
        string? asked = null;
        Assert.False(StartWait.ContinueAfterWait(false, ["A001"], q => { asked = q; return false; }));
        Assert.Equal("Karta A001 se ještě prověřuje — nevím, jestli už je zálohovaná. Spustit přesto celou kopii?", asked);
        Assert.True(StartWait.ContinueAfterWait(false, ["A001", "A002"], q => { asked = q; return true; }));
        Assert.Equal("Karty A001, A002 se ještě prověřují — nevím, jestli už jsou zálohované. Spustit přesto celou kopii?", asked);
        asked = null;
        Assert.True(StartWait.ContinueAfterWait(true, [], q => { asked = q; return false; }));   // finished in time: no question
        Assert.Null(asked);
    }

    [Fact]
    public async Task AllWithin_CancelledByTheClosingWindow_Throws()
    {
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StartWait.AllWithinAsync([new TaskCompletionSource().Task], TimeSpan.FromSeconds(30), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StartWait.UntilAsync(() => 1, null, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10), cts.Token));
    }

    // Item 3: a stale red text on a fixed disk is cleared by the health check - one read at a time, only while it is shown.
    [Fact]
    public void FreeSpaceReprobe_OnlyWhileTheProblemShows_OneInFlight()
    {
        var r = new FreeSpaceReprobe();
        Assert.False(r.TryBegin(spaceProblemShown: false));
        Assert.True(r.TryBegin(true));
        Assert.False(r.TryBegin(true));   // still running
        r.End();
        Assert.True(r.TryBegin(true));
    }

    // Ruling I4 (Review focus 5): free space probed before the copy started, a second card for the same disk.
    [Fact]
    public void FreeSpaceBaseline_NeverSubtractsBytesWrittenSinceTheProbe()
    {
        const long GB = 1_000_000_000;
        var job = new object();
        var baseline = new FreeSpaceBaseline<object>();
        var need = new SpaceNeed(200 * GB, 0, 0);
        // The disk was probed when the job started (nothing written, free 220 GB); the job has now written 150 GB.
        baseline.Commit(new Dictionary<object, long> { [job] = 0 });
        var running = new VolumeDemand("v", "Zaloha (D:)", SpaceCheck.Remaining(need, JobPhase.Copying, baseline.WrittenAtProbe(job)));
        var next = new VolumeDemand("v", "Zaloha (D:)", new SpaceNeed(50 * GB, 0, 0));
        var problems = SpacePlanner.Check([running, next], new Dictionary<string, long> { ["v"] = 220 * GB });
        Assert.Equal(SpaceSeverity.Blocking, Assert.Single(problems).Severity);   // 250 GB > 220 GB: the stale free is not trusted
        // Probed again after those 150 GB: free is 70 GB, the job's remaining need is 50 GB — and then it fits.
        baseline.Commit(new Dictionary<object, long> { [job] = 150 * GB });
        running = running with { Need = SpaceCheck.Remaining(need, JobPhase.Copying, baseline.WrittenAtProbe(job)) };
        Assert.Empty(SpacePlanner.Check([running, next with { Need = new SpaceNeed(10 * GB, 0, 0) }], new Dictionary<string, long> { ["v"] = 70 * GB }));
        // A job that started after the last probe counts in full.
        Assert.Equal(0, baseline.WrittenAtProbe(new object()));
    }
}
