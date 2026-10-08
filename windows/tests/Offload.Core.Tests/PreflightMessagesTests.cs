namespace Offload.Core.Tests;

/// Spec 2.6: every message of the checks before a copy is Czech, word for word, and carries its PreflightCode.
public class PreflightMessagesTests
{
    static OffloadOptions SameVolume() => new() { AllowSameDevice = true, AllowSourceVolume = true };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 1000, 1);
        return card;
    }

    static OffloadError Fail(Action<OffloadEngine> act, OffloadOptions? options = null) =>
        Assert.Throws<OffloadError>(() => act(new OffloadEngine(options ?? SameVolume())));

    /// Every scenario PreflightTests covers, with the exact Czech text it must produce.
    static List<(PreflightCode Code, string Expected, OffloadError Error)> Scenarios(TempDir dir)
    {
        var card = Card(dir);
        var d1 = dir.Sub("d1");
        var nope = Path.Combine(dir.Path, "nope");
        var missing = Path.Combine(dir.Path, "no-such-card");
        var empty = dir.Sub("empty");
        var offload = dir.Sub("old");
        TestFs.WriteRandom(Path.Combine(offload, "a.mov"), 10, 1);
        Directory.CreateDirectory(Path.Combine(offload, "_REPORT"));
        return
        [
            (PreflightCode.ChunkSize, "Velikost bloku musí být násobek 4096 B a nejvýš 1 GiB (zadáno 1000).",
                Fail(e => e.Prepare(card, [d1], null, null), new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true, ChunkSize = 1000 })),
            (PreflightCode.BufferCount, "Počet bufferů musí být aspoň 2 (zadáno 1).",
                Fail(e => e.Prepare(card, [d1], null, null), new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true, BufferCount = 1 })),
            (PreflightCode.Date, "Datum musí být ve tvaru RRRR-MM-DD (zadáno „2026-1-4“).", Fail(e => e.Prepare(card, [d1], null, "2026-1-4"))),
            (PreflightCode.SourceNotFound, $"Zdroj nenalezen: {missing}", Fail(e => e.Prepare(missing, [d1], null, null))),
            (PreflightCode.NoDestination, "Není zadaný žádný cíl.", Fail(e => e.Prepare(card, [], null, null))),
            (PreflightCode.DuplicateDestination, $"Cíl je zadaný dvakrát: {d1.ToUpperInvariant()}\\",
                Fail(e => e.Prepare(card, [d1, d1.ToUpperInvariant() + @"\"], null, null))),
            (PreflightCode.SourceIsOffload,
                "Zdroj vypadá jako dřívější offload (má _REPORT, _PROXY nebo ascmhl) — kopírujte původní kartu, nebo tyto složky odeberte.",
                Fail(e => e.Prepare(offload, [d1], null, null))),
            (PreflightCode.DestinationNotFound, $"Cíl nenalezen: {nope} — je disk připojený?", Fail(e => e.Prepare(card, [nope], null, null))),
            (PreflightCode.SameVolumeAsSource, $"Cíl {d1} je na stejném disku jako zdroj.",
                Fail(e => e.Prepare(card, [d1], null, null), new OffloadOptions { AllowSameDevice = true })),
            (PreflightCode.SharedVolume,
                $"Dva cíle jsou na stejném disku ({dir.Sub("d2")}) — to není druhá kopie. Povolit jde volbou --allow-same-device.",
                Fail(e => e.Prepare(card, [d1, dir.Sub("d2")], null, null), new OffloadOptions { AllowSourceVolume = true })),
            (PreflightCode.EmptySource, "Na zdroji nejsou žádné soubory.", Fail(e => e.Prepare(empty, [d1], null, null))),
        ];
    }

    [Fact]
    public void EveryScenario_ExactCzechText_AndCode()
    {
        using var dir = new TempDir();
        foreach (var (code, expected, error) in Scenarios(dir))
        {
            Assert.True(error.IsPreflight, expected);
            Assert.Equal(code, error.Code);
            Assert.Equal(expected, error.Message);
        }
    }

    [Fact]
    public void NoEnglishWords()
    {
        using var dir = new TempDir();
        string[] english = ["not found", "cannot", "must", "Destination", "Source"];
        foreach (var (_, _, error) in Scenarios(dir))
            foreach (var word in english)
                Assert.DoesNotContain(word, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextsWithoutAnEasyScenario()
    {
        Assert.Equal(@"Na cíli D:\Zaloha není dost místa: potřeba 412 GB (karty 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB.",
            Msg.NoSpace(@"D:\Zaloha", new Space.SpaceNeed(380_000_000_000, 30_000_000_000, 2_000_000_000), 398_000_000_000));
        Assert.Equal(@"Na cíl nejde zapisovat: D:\Zaloha", Msg.DestinationNotWritable(@"D:\Zaloha"));
        Assert.Equal(@"Cíl nejde přečíst: D:\Zaloha", Msg.DestinationUnreadable(@"D:\Zaloha"));
        Assert.Equal(@"Zdroj nejde přečíst: E:\", Msg.SourceUnreadable(@"E:\"));
        // M22: BytesCs inside the Czech sentence (decimal comma)
        Assert.Equal("FAT32 neumí uložit soubory od 4 GB (CLIP/A001C002.mov, 4,3 GB)",
            OffloadEngine.Fat32Problem("FAT32", [new("CLIP/A001C002.mov", 4L << 30, DateTime.UtcNow)]));
        Assert.Equal("Report se nepodařilo zapsat: disk full", Msg.ReportNotWritten("disk full"));
        Assert.Equal(@"Ve složce D:\A001 není ASC MHL ani seznam součtů — není s čím porovnat.", Msg.NoHashRecord(@"D:\A001"));
        Assert.Equal(@"V E:\ nejsou soubory ke čtení.", Msg.NoFilesToRead(@"E:\"));
    }

    [Fact]
    public void BadNames_ListAndCount()
    {
        var problems = Enumerable.Range(1, 22).Select(i => $"f{i}: {Msg.InvalidChar}").ToList();
        var text = Msg.BadNames(problems);
        Assert.StartsWith("Některé názvy Windows neumí uložit:\n  f1: obsahuje znak, který Windows nepovolí\n", text);
        Assert.EndsWith("\n  … a dalších 2", text);
        var scan = new SourceScan
        {
            Root = @"X:\",
            Files = [new("CLIP/A.MOV", 1, DateTime.UtcNow), new("CLIP/a.mov", 1, DateTime.UtcNow), new("CON.mov", 1, DateTime.UtcNow), new("b. ", 1, DateTime.UtcNow)],
            Dirs = ["CLIP"],
            Skipped = [],
        };
        Assert.Equal(
            ["CLIP/a.mov: stejný název jako CLIP/A.MOV, liší se jen velikostí písmen", "CON.mov: rezervovaný název zařízení ve Windows", "b. : končí tečkou nebo mezerou"],
            Scanner.WindowsProblems(scan));
    }

    [Fact]
    public void CliAndClipTexts()
    {
        Assert.Equal("Neznámý kodek proxy „av1“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.", Msg.UnknownProxyCodec("av1"));
        Assert.Equal("„x“ není kladné číslo.", Msg.NotPositiveNumber("x"));
        Assert.Equal("Blackmagic RAW: chybí dekodér", Msg.NoDecoder(ClipFormat.Braw.Label()));
        Assert.Equal("žádná ověřená kopie", Msg.NoVerifiedCopy);
    }
}
