using System.Diagnostics;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class CardInspectorTests
{
    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001_261006_R1AB.mov"), 1000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002_261006_R1AB.mov"), 2000, 2);
        return card;
    }

    [Fact]
    public void Folder_SizeClipsFingerprint_NoHistory()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var insight = CardInspector.Inspect(card, history: null, store: null, CancellationToken.None);
        Assert.Null(insight.Error);
        Assert.Null(insight.Volume);
        Assert.Equal(3000, insight.TotalBytes);
        Assert.Equal(2, insight.FileCount);
        Assert.Equal(["A001C001_261006_R1AB", "A001C002_261006_R1AB"], insight.Clips.Select(c => c.Name));
        Assert.Equal(CardFingerprint.Compute(Scanner.Scan(card)), (insight.Fingerprint, insight.Shape));
        Assert.Null(insight.History);
    }

    [Fact]
    public void Folder_FoundByShape_NewFilesFromTheStoredList()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var store = new HistoryStore(dir.Sub("appdata"));
        var before = Scanner.Scan(card);
        var (fp, shape) = CardFingerprint.Compute(before);
        store.Record(HistoryFixtures.Entry(HistoryFixtures.T0, serial: null, fingerprint: fp, shape: shape), before.Files);

        var same = CardInspector.Inspect(card, store.Load(), store, CancellationToken.None);
        Assert.Equal(HistoryMatchKind.Identical, same.History!.Kind);

        // A folder is found by its shape: new files change it, so a grown folder is no longer recognised (spec 2.1 step 1).
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C003_261006_R1AB.mov"), 500, 3);
        Assert.Null(CardInspector.Inspect(card, store.Load(), store, CancellationToken.None).History);
    }

    [Fact]
    public void Cancelled_Throws()
    {
        using var dir = new TempDir();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => CardInspector.Inspect(Card(dir), null, null, cts.Token));
    }

    // Review focus 1: a card pulled out: an error in the insight, never an exception.
    [Fact]
    public void Unreadable_ErrorInTheInsight()
    {
        using var dir = new TempDir();
        var gone = Path.Combine(dir.Path, "gone");
        var insight = CardInspector.Inspect(gone, null, null, CancellationToken.None);
        Assert.StartsWith("Cannot read the card: ", insight.Error);
    }

    [Fact]
    public void ScanArgumentException_ErrorInTheInsight()
    {
        using var dir = new TempDir();
        // A path with an illegal character makes the scan throw ArgumentException/NotSupported, not IOError.
        var insight = CardInspector.Inspect(Path.Combine(dir.Path, "a<b|c"), null, null, CancellationToken.None);
        Assert.StartsWith("Cannot read the card: ", insight.Error);
    }

    [Fact]
    public void DriveRoot_VolumeUnreadable_IsAnError_NotAShapeMatch()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var insight = CardInspector.Inspect(card, [], null, CancellationToken.None, _ => null, _ => null, _ => @"E:\");
        Assert.Equal(Offload.Core.Msg.CardVolumeUnreadable, insight.Error);
        Assert.Null(insight.History);
    }

    [Fact]
    public void CardSwappedDuringTheScan_ResultDiscarded()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var volume = new CardVolume("card1", "1A2B3C4D", 1000, "A001", "exFAT");
        var insight = CardInspector.Inspect(card, [], null, CancellationToken.None, _ => volume, _ => "card2", _ => @"E:\");
        Assert.Equal(Offload.Core.Msg.CardSwapped, insight.Error);
        Assert.Equal(0, insight.FileCount);
        var same = CardInspector.Inspect(card, [], null, CancellationToken.None, _ => volume, _ => "card1", _ => @"E:\");
        Assert.Null(same.Error);
        Assert.Equal(2, same.FileCount);
    }

    // Requirement 2: the append is offered only when the earlier card folders can be reached (checked off the UI thread, here).
    [Fact]
    public void NewFiles_ReachableFlag_FollowsTheEarlierCardFolders()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var volume = new CardVolume("card1", "1A2B3C4D", 1000, "A001", "exFAT");
        var store = new HistoryStore(dir.Sub("appdata"));
        var before = Scanner.Scan(card);
        var (fp, shape) = CardFingerprint.Compute(before);
        var d1 = dir.Sub("D1");
        var d2 = Path.Combine(dir.Path, "D2");
        var entry = HistoryFixtures.Entry(HistoryFixtures.T0, serial: "1A2B3C4D", capacity: 1000, fingerprint: fp, shape: shape);
        entry.Destinations = [new HistoryDestination { Root = dir.Path, CardPath = d1 }, new HistoryDestination { Root = dir.Path, CardPath = d2 }];
        store.Record(entry, before.Files);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C003_261006_R1AB.mov"), 500, 3);

        var missing = CardInspector.Inspect(card, store.Load(), store, CancellationToken.None, _ => volume, _ => "card1", _ => @"E:");
        Assert.Equal(HistoryMatchKind.NewFiles, missing.History!.Kind);
        Assert.False(missing.AppendReachable);
        Assert.False(AppendRules_Offer(missing));

        Directory.CreateDirectory(d2);
        var ok = CardInspector.Inspect(card, store.Load(), store, CancellationToken.None, _ => volume, _ => "card1", _ => @"E:");
        Assert.True(ok.AppendReachable);
        Assert.True(AppendRules_Offer(ok));
    }

    static bool AppendRules_Offer(CardInsight i) => Offload.Core.Desktop.AppendRules.Offer(i);

    // Spec 5.5: 10 000 files well within the budget (3 s on a USB 3 reader; here a local disk).
    [Fact]
    public void TenThousandFiles_UnderBudget()
    {
        using var dir = new TempDir();
        var card = dir.Sub("big");
        for (int d = 0; d < 100; d++)
        {
            var sub = Directory.CreateDirectory(Path.Combine(card, "DCIM", $"{100 + d}CANON")).FullName;
            for (int f = 0; f < 100; f++) File.WriteAllBytes(Path.Combine(sub, $"IMG_{f:0000}.CR3"), []);
        }
        var sw = Stopwatch.StartNew();
        var insight = CardInspector.Inspect(card, [], null, CancellationToken.None);
        Assert.Equal(10_000, insight.FileCount);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"{sw.Elapsed.TotalSeconds:0.0} s");
    }
}
