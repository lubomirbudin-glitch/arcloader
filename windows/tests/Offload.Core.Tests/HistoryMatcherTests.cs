using Offload.Core.History;

namespace Offload.Core.Tests;

public class HistoryMatcherTests
{
    static readonly DateTime T0 = HistoryFixtures.T0;

    static readonly SourceFile[] Backed = [new("CLIP/A001C001.mov", 1000, T0), new("CLIP/A001C002.mov", 2000, T0.AddMinutes(1))];

    static SourceScan Scan(params SourceFile[] files) =>
        new() { Root = @"E:\", Files = files.OrderBy(f => f.RelPath, StringComparer.Ordinal).ToList(), Dirs = ["CLIP"], Skipped = [] };

    static HistoryMatch Match(SourceScan scan, string? filesystem = "exFAT", bool withList = true)
    {
        var (fingerprint, shape) = CardFingerprint.Compute(Backed);
        var entry = HistoryFixtures.Entry(T0, fingerprint: fingerprint, shape: shape);
        return HistoryMatcher.Match(scan, CardFingerprint.Compute(scan).Fingerprint, entry, withList ? Backed : null, filesystem);
    }

    [Fact]
    public void SameCard_Identical()
    {
        var m = Match(Scan(Backed));
        Assert.Equal(HistoryMatchKind.Identical, m.Kind);
        Assert.Empty(m.New);
    }

    [Fact]
    public void MoreClips_NewFiles_WithCountAndBytes()
    {
        var m = Match(Scan([.. Backed, new("CLIP/A001C003.mov", 3000, T0.AddMinutes(5)), new("CLIP/A001C004.mov", 500, T0.AddMinutes(6))]));
        Assert.Equal(HistoryMatchKind.NewFiles, m.Kind);
        Assert.Equal(["CLIP/A001C003.mov", "CLIP/A001C004.mov"], m.New.Select(f => f.RelPath));
        Assert.Equal(3500, m.NewBytes);
    }

    [Fact]
    public void KnownPathWithOtherSize_Changed()
    {
        var m = Match(Scan(new("CLIP/A001C001.mov", 1001, T0), Backed[1], new("CLIP/A001C003.mov", 3000, T0)));
        Assert.Equal(HistoryMatchKind.Changed, m.Kind);
        Assert.Equal(["CLIP/A001C001.mov"], m.Changed);
    }

    [Fact]
    public void DeletedInCamera_IsOnlyInformation()
    {
        var m = Match(Scan(Backed[0]));
        Assert.Equal(HistoryMatchKind.Identical, m.Kind);
        Assert.Equal(1, m.Removed);
    }

    // Ruling I3: a clone (same serial and capacity) or a card emptied and reshot shares no path with the stored list.
    // That is another card, never "new files" to append into the old reel folder.
    [Fact]
    public void DisjointContent_IsChanged_NeverNewFiles()
    {
        var m = Match(Scan(new SourceFile("CLIP/B002C001.mov", 900, T0.AddDays(1)), new SourceFile("CLIP/B002C002.mov", 800, T0.AddDays(1))));
        Assert.Equal(HistoryMatchKind.Changed, m.Kind);
        Assert.Equal(["CLIP/B002C001.mov", "CLIP/B002C002.mov"], m.Changed);
        Assert.Empty(m.New);
    }

    [Fact]
    public void EmptyCard_IsNotChanged()
    {
        var m = Match(Scan());
        Assert.Equal(HistoryMatchKind.Identical, m.Kind);
        Assert.Equal(2, m.Removed);
    }

    // Review focus 2: FAT32 card after the change to winter time: every time an hour off, nothing changed.
    [Fact]
    public void Fat32_HourShift_IsTheSameCard()
    {
        var shifted = Backed.Select(f => f with { ModifiedUtc = f.ModifiedUtc.AddHours(-1) }).ToArray();
        var m = Match(Scan([.. shifted, new("CLIP/A001C003.mov", 3000, T0)]), "FAT32");
        Assert.Equal(HistoryMatchKind.NewFiles, m.Kind);
        Assert.Single(m.New);
    }

    [Fact]
    public void ExFat_HourShift_IsAChange()
    {
        var shifted = Backed.Select(f => f with { ModifiedUtc = f.ModifiedUtc.AddHours(-1) }).ToArray();
        var m = Match(Scan(shifted), "exFAT");
        Assert.Equal(HistoryMatchKind.Changed, m.Kind);
        Assert.Equal(2, m.Changed.Count);
    }

    [Fact]
    public void FileListMissing_NotCompared()
    {
        var m = Match(Scan([.. Backed, new("CLIP/A001C003.mov", 3000, T0)]), withList: false);
        Assert.Equal(HistoryMatchKind.NotCompared, m.Kind);
    }
}
