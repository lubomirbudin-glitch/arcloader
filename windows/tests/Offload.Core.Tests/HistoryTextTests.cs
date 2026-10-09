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
    public void Verified() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno, lze formátovat", HistoryTone.Ok),
        HistoryText.Line(Match(HistoryMatchKind.Identical)));

    [Fact]
    public void NewFiles() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno, lze formátovat · nové soubory: 12 (3,2 GB)", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.NewFiles, newFiles: 12, newBytes: 3_200_000_000)));

    [Fact]
    public void NotVerified() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — NEOVĚŘENO (lze vyjmout, neformátovat)", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.Identical, verified: false)));

    [Fact]
    public void WithErrors() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — s chybami (3 soubory)", HistoryTone.Error),
        HistoryText.Line(Match(HistoryMatchKind.Identical, success: false)));

    [Fact]
    public void Changed() => Assert.Equal(
        ("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:), ale karta se od té doby změnila (4 soubory jinak) — doplnit nejde, jen celá kopie", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.Changed, changed: 4)));

    [Fact]
    public void NotCompared_IsThePlainLine() => Assert.Equal("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno, lze formátovat",
        HistoryText.Line(Match(HistoryMatchKind.NotCompared)).Text);

    [Fact]
    public void Cli_FullPaths() => Assert.Equal(@"Zálohováno 6. 10. 2026 8:15 na D:\Zaloha, F:\Zaloha — ověřeno, lze formátovat",
        HistoryText.Line(Match(HistoryMatchKind.Identical), fullPaths: true).Text);

    [Theory]
    [InlineData(@"D:\Zaloha", "Zaloha (D:)")]
    [InlineData(@"d:\Backup\Zaloha\", "Zaloha (D:)")]
    [InlineData(@"D:\", "D:")]
    [InlineData(@"\\NAS\share\Zaloha", "Zaloha")]
    public void DiskName(string root, string name) => Assert.Equal(name, HistoryText.DiskName(root));

}
