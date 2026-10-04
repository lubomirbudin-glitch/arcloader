namespace Offload.Core.Tests;

public class PreflightTests
{
    static OffloadOptions SameVolume() => new() { AllowSameDevice = true, AllowSourceVolume = true };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 1000, 1);
        return card;
    }

    [Fact]
    public void Plan_HasScanDateAndCardFolder()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var d1 = dir.Sub("d1");
        var plan = new OffloadEngine(SameVolume()).Prepare(card, [d1], null, "2026-10-04");
        Assert.Equal("A001", plan.CardName);
        Assert.Equal("2026-10-04", plan.Date);
        Assert.Equal([Path.Combine(d1, "2026-10-04", "A001")], plan.CardPaths);
        Assert.Single(plan.Scan.Files);
    }

    [Fact]
    public void CardName_IsSanitized()
    {
        using var dir = new TempDir();
        var plan = new OffloadEngine(SameVolume()).Prepare(Card(dir), [dir.Sub("d1")], "Den 1: hlavní", "2026-10-04");
        Assert.Equal("Den 1_ hlavní", plan.CardName);
    }

    [Fact]
    public void SourceMissing() => AssertPreflight("Source not found",
        e => e.Prepare(@"C:\offload-no-such-card", [Path.GetTempPath()], null, null));

    [Fact]
    public void NoDestination()
    {
        using var dir = new TempDir();
        AssertPreflight("No destination given", e => e.Prepare(Card(dir), [], null, null));
    }

    [Fact]
    public void DestinationMissing()
    {
        using var dir = new TempDir();
        AssertPreflight("Destination not found", e => e.Prepare(Card(dir), [Path.Combine(dir.Path, "nope")], null, null));
    }

    [Fact]
    public void DestinationOnSourceVolume_IsRejected()
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSameDevice = true }).Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.Contains("same volume as the source", e.Message);
    }

    [Fact]
    public void TwoDestinationsOnOneVolume_NeedAllowSameDevice()
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSourceVolume = true }).Prepare(Card(dir), [dir.Sub("d1"), dir.Sub("d2")], null, null));
        Assert.Contains("--allow-same-device", e.Message);
    }

    [Fact]
    public void EmptySource()
    {
        using var dir = new TempDir();
        AssertPreflight("Source contains no files", e => e.Prepare(dir.Sub("empty"), [dir.Sub("d1")], null, null));
    }

    [Fact]
    public void Fat32Problem_RejectsFourGigabyteFile()
    {
        SourceFile[] files = [new("CLIP/small.mov", 100, DateTime.UtcNow), new("CLIP/A001C002.mov", 4L << 30, DateTime.UtcNow)];
        var problem = OffloadEngine.Fat32Problem("FAT32", files);
        Assert.NotNull(problem);
        Assert.Contains("CLIP/A001C002.mov", problem);
        Assert.Null(OffloadEngine.Fat32Problem("exFAT", files));
        Assert.Null(OffloadEngine.Fat32Problem("NTFS", files));
    }

    [Fact]
    public void Fat32Problem_AllowsJustUnderFourGigabytes() =>
        Assert.Null(OffloadEngine.Fat32Problem("FAT32", [new("a", uint.MaxValue, DateTime.UtcNow)]));

    [Theory]
    [InlineData("_REPORT")]
    [InlineData("_proxy")]
    [InlineData("ascmhl")]
    public void SourceThatIsAnEarlierOffload_IsRejected(string folder)
    {
        using var dir = new TempDir();
        var card = Card(dir);
        Directory.CreateDirectory(Path.Combine(card, folder));
        AssertPreflight("Source looks like an earlier offload", e => e.Prepare(card, [dir.Sub("d1")], null, null));
    }

    [Fact]
    public void DuplicateDestination_IsRejectedEvenWithAllowSameDevice()
    {
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        AssertPreflight("Destination given twice", e => e.Prepare(Card(dir), [d1, d1.ToUpperInvariant() + @"\"], null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4096)]
    [InlineData(1000)]
    [InlineData(4096 + 1)]
    [InlineData((1 << 30) + 4096)]
    public void BadChunkSize_IsRejected(int chunk)
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true, ChunkSize = chunk })
                .Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.True(e.IsPreflight);
        Assert.Contains("ChunkSize", e.Message);
    }

    [Fact]
    public void BufferCountBelowTwo_IsRejected()
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true, BufferCount = 1 })
                .Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.True(e.IsPreflight);
        Assert.Contains("BufferCount", e.Message);
    }

    [Theory]
    [InlineData("2026-1-4")]
    [InlineData("../evil")]
    [InlineData("2026-10-04\n")]
    public void BadDate_IsRejected(string date)
    {
        using var dir = new TempDir();
        AssertPreflight("Date must be", e => e.Prepare(Card(dir), [dir.Sub("d1")], null, date));
    }

    static void AssertPreflight(string message, Action<OffloadEngine> act)
    {
        var e = Assert.Throws<OffloadError>(() => act(new OffloadEngine(SameVolume())));
        Assert.True(e.IsPreflight);
        Assert.Contains(message, e.Message);
    }
}
