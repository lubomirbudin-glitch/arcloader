using Offload.Core.History;

namespace Offload.Core.Tests;

/// Spec 2.1 UX table, word for word.
public class HistoryTextTests
{
    static readonly DateTime Finished = new DateTime(2026, 10, 6, 8, 15, 0, DateTimeKind.Local).ToUniversalTime();

    static HistoryMatch Match(HistoryMatchKind kind, bool success = true, bool verified = true, int newFiles = 0, long newBytes = 0, int changed = 0)
    {
        var e = HistoryFixtures.Entry(Finished, success: success, verified: verified);
        var added = Enumerable.Range(0, newFiles).Select(i => new SourceFile($"n{i}", 1, Finished)).ToList();
        return new HistoryMatch(e, kind, added, newBytes, Enumerable.Range(0, changed).Select(i => $"c{i}").ToList(), 0);
    }

    [Fact]
    public void Verified() => Assert.Equal(("Backed up on 2026-10-06 08:15 to Zaloha (D:), Zaloha (F:) — verified, safe to format", HistoryTone.Ok),
        HistoryText.Line(Match(HistoryMatchKind.Identical)));

    [Fact]
    public void NewFiles() => Assert.Equal(("Backed up on 2026-10-06 08:15 to Zaloha (D:), Zaloha (F:) — verified, safe to format · new files: 12 (3.2 GB)", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.NewFiles, newFiles: 12, newBytes: 3_200_000_000)));

    [Fact]
    public void NotVerified() => Assert.Equal(("Backed up on 2026-10-06 08:15 to Zaloha (D:), Zaloha (F:) — NOT VERIFIED (safe to eject, do not format)", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.Identical, verified: false)));

    [Fact]
    public void WithErrors() => Assert.Equal(("Backed up on 2026-10-06 08:15 to Zaloha (D:), Zaloha (F:) — with errors (3 files)", HistoryTone.Error),
        HistoryText.Line(Match(HistoryMatchKind.Identical, success: false)));

    [Fact]
    public void Changed() => Assert.Equal(
        ("Backed up on 2026-10-06 08:15 to Zaloha (D:), Zaloha (F:), but the card has changed since (4 files differ) — cannot append, only a full copy", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.Changed, changed: 4)));

    [Fact]
    public void NotCompared_IsThePlainLine() => Assert.Equal("Backed up on 2026-10-06 08:15 to Zaloha (D:), Zaloha (F:) — verified, safe to format",
        HistoryText.Line(Match(HistoryMatchKind.NotCompared)).Text);

    [Fact]
    public void Cli_FullPaths() => Assert.Equal(@"Backed up on 2026-10-06 08:15 to D:\Zaloha, F:\Zaloha — verified, safe to format",
        HistoryText.Line(Match(HistoryMatchKind.Identical), fullPaths: true).Text);

    [Theory]
    [InlineData(@"D:\Zaloha", "Zaloha (D:)")]
    [InlineData(@"d:\Backup\Zaloha\", "Zaloha (D:)")]
    [InlineData(@"D:\", "D:")]
    [InlineData(@"\\NAS\share\Zaloha", "Zaloha")]
    public void DiskName(string root, string name) => Assert.Equal(name, HistoryText.DiskName(root));

}
