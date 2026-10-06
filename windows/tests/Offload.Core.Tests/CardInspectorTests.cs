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
        Assert.StartsWith("Kartu nejde přečíst: ", insight.Error);
    }

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
