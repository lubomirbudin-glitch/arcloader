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
            (PreflightCode.ChunkSize, "The block size must be a multiple of 4096 B and at most 1 GiB (given 1000).",
                Fail(e => e.Prepare(card, [d1], null, null), new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true, ChunkSize = 1000 })),
            (PreflightCode.BufferCount, "The buffer count must be at least 2 (given 1).",
                Fail(e => e.Prepare(card, [d1], null, null), new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true, BufferCount = 1 })),
            (PreflightCode.Date, "The date must be YYYY-MM-DD (given \"2026-1-4\").", Fail(e => e.Prepare(card, [d1], null, "2026-1-4"))),
            (PreflightCode.SourceNotFound, $"Source not found: {missing}", Fail(e => e.Prepare(missing, [d1], null, null))),
            (PreflightCode.NoDestination, "No destination given.", Fail(e => e.Prepare(card, [], null, null))),
            (PreflightCode.DuplicateDestination, $"Destination given twice: {d1.ToUpperInvariant()}\\",
                Fail(e => e.Prepare(card, [d1, d1.ToUpperInvariant() + @"\"], null, null))),
            (PreflightCode.SourceIsOffload,
                "The source looks like an earlier offload (it has _REPORT, _PROXY or ascmhl) — copy the original card, or remove these folders.",
                Fail(e => e.Prepare(offload, [d1], null, null))),
            (PreflightCode.DestinationNotFound, $"Destination not found: {nope} — is the disk connected?", Fail(e => e.Prepare(card, [nope], null, null))),
            (PreflightCode.SameVolumeAsSource, $"Destination {d1} is on the same disk as the source.",
                Fail(e => e.Prepare(card, [d1], null, null), new OffloadOptions { AllowSameDevice = true })),
            (PreflightCode.SharedVolume,
                $"Two destinations are on the same disk ({dir.Sub("d2")}) — that is not a second copy. Allow it with --allow-same-device.",
                Fail(e => e.Prepare(card, [d1, dir.Sub("d2")], null, null), new OffloadOptions { AllowSourceVolume = true })),
            (PreflightCode.EmptySource, "There are no files on the source.", Fail(e => e.Prepare(empty, [d1], null, null))),
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
    public void NoCzechWords()
    {
        using var dir = new TempDir();
        string[] czech = ["nenalezen", "nejde", "musí", "Cíl", "Zdroj", "soubor", "karta"];
        foreach (var (_, _, error) in Scenarios(dir))
        {
            foreach (var word in czech)
                Assert.DoesNotContain(word, error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch("[ěščřžýáíéůúňťďĚŠČŘŽÝÁÍÉŮÚŇŤĎ„“]", error.Message);
        }
    }

    [Fact]
    public void TextsWithoutAnEasyScenario()
    {
        Assert.Equal(@"Not enough space on destination D:\Zaloha: needs 412 GB (cards 380 GB, proxies ~30 GB, report 2 GB), free 398 GB.",
            Msg.NoSpace(@"D:\Zaloha", new Space.SpaceNeed(380_000_000_000, 30_000_000_000, 2_000_000_000), 398_000_000_000));
        Assert.Equal(@"Cannot write to the destination: D:\Zaloha", Msg.DestinationNotWritable(@"D:\Zaloha"));
        Assert.Equal(@"Cannot read the destination: D:\Zaloha", Msg.DestinationUnreadable(@"D:\Zaloha"));
        Assert.Equal(@"Cannot read the source: E:\", Msg.SourceUnreadable(@"E:\"));
        // M22: BytesShort inside the sentence (a decimal point, whatever the PC's culture)
        Assert.Equal("FAT32 cannot store files of 4 GB or more (CLIP/A001C002.mov, 4.3 GB)",
            OffloadEngine.Fat32Problem("FAT32", [new("CLIP/A001C002.mov", 4L << 30, DateTime.UtcNow)]));
        Assert.Equal("Could not write the report: disk full", Msg.ReportNotWritten("disk full"));
        Assert.Equal(@"Folder D:\A001 has no ASC MHL and no checksum list — nothing to compare with.", Msg.NoHashRecord(@"D:\A001"));
        Assert.Equal(@"No files to read in E:\.", Msg.NoFilesToRead(@"E:\"));
    }

    [Fact]
    public void BadNames_ListAndCount()
    {
        var problems = Enumerable.Range(1, 22).Select(i => $"f{i}: {Msg.InvalidChar}").ToList();
        var text = Msg.BadNames(problems);
        Assert.StartsWith("Windows cannot store some names:\n  f1: contains a character Windows does not allow\n", text);
        Assert.EndsWith("\n  … and 2 more", text);
        var scan = new SourceScan
        {
            Root = @"X:\",
            Files = [new("CLIP/A.MOV", 1, DateTime.UtcNow), new("CLIP/a.mov", 1, DateTime.UtcNow), new("CON.mov", 1, DateTime.UtcNow), new("b. ", 1, DateTime.UtcNow)],
            Dirs = ["CLIP"],
            Skipped = [],
        };
        Assert.Equal(
            ["CLIP/a.mov: same name as CLIP/A.MOV, differs only in letter case", "CON.mov: reserved device name in Windows", "b. : ends with a dot or a space"],
            Scanner.WindowsProblems(scan));
    }

    [Fact]
    public void CliAndClipTexts()
    {
        Assert.Equal("Unknown proxy codec \"av1\" — use h264, hevc, proresProxy, proresLT, prores422 or prores422HQ.", Msg.UnknownProxyCodec("av1"));
        Assert.Equal("\"x\" is not a positive number.", Msg.NotPositiveNumber("x"));
        Assert.Equal("Blackmagic RAW: no decoder", Msg.NoDecoder(ClipFormat.Braw.Label()));
        Assert.Equal("no verified copy", Msg.NoVerifiedCopy);
    }
}
