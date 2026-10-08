using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// R4, R6: `_REPORT\offload-manifest.jsonl` — append-only, a torn line never spoils the next, checkpoints chain up.
public class ManifestFileTests
{
    static readonly DateTime T = new(2026, 10, 8, 9, 30, 15, DateTimeKind.Utc);

    static string Card(TempDir dir)
    {
        var card = dir.Sub("2026-10-08", "A001");
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        return card;
    }

    static ManifestRecord FileRecord(string path, params ulong[] blocks) => new()
    {
        Path = path, Size = blocks.Length * 4096L, Modified = T, Xxh64 = "0123456789abcdef", Xxh128 = new string('a', 32),
        BlockSize = 4096, Blocks = ManifestRecord.Hex(blocks),
    };

    [Fact]
    public void Append_ThenRead_RoundTrip_OneLineEach()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        ManifestFile.Append(card, FileRecord("CLIP/a.mov", 1, 2));
        ManifestFile.Append(card, FileRecord("b.wav", 0xFFFFFFFFFFFFFFFF));

        var text = File.ReadAllText(ManifestFile.PathIn(card));
        Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.EndsWith("\n", text);
        Assert.Contains("\"blocks\":[\"0000000000000001\",\"0000000000000002\"]", text);
        Assert.Contains("\"modified\":\"2026-10-08T09:30:15Z\"", text);
        var all = ManifestFile.Read(card);
        Assert.Equal(["CLIP/a.mov", "b.wav"], all.Select(r => r.Path));
        var m = all[0].ToManifest()!;
        Assert.Equal([1UL, 2UL], m.Blocks);
        Assert.Equal(8192, m.Size);
        Assert.Equal(0x0123456789abcdefUL, all[0].Hashes()!.Value.Xxh64);
        Assert.Equal(new string('a', 32), all[0].Hashes()!.Value.Xxh128);
    }

    // A crash in the middle of a line: that line is skipped, the next append starts on its own line.
    [Fact]
    public void TornLine_IsSkipped_AndTheNextRecordIsWhole()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        ManifestFile.Append(card, FileRecord("a.mov", 1));
        File.AppendAllText(ManifestFile.PathIn(card), "{\"v\":1,\"kind\":\"file\",\"path\":\"b.m");
        ManifestFile.Append(card, FileRecord("c.mov", 3));

        Assert.Equal(["a.mov", "c.mov"], ManifestFile.Read(card).Select(r => r.Path));
    }

    // Never rewritten: what was there stays byte for byte, the new line goes after it.
    [Fact]
    public void Append_KeepsEarlierBytes()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        ManifestFile.Append(card, FileRecord("a.mov", 1));
        var before = File.ReadAllBytes(ManifestFile.PathIn(card));
        ManifestFile.Append(card, FileRecord("b.mov", 2));

        var after = File.ReadAllBytes(ManifestFile.PathIn(card));
        Assert.Equal(before, after[..before.Length]);
    }

    [Fact]
    public void Read_NoManifest_Empty()
    {
        using var dir = new TempDir();
        Assert.Empty(ManifestFile.Read(Card(dir)));
    }

    [Fact]
    public void Files_NewestOfItsGeneration_AnyCase()
    {
        var records = new[]
        {
            FileRecord("A.mov", 1),
            FileRecord("a.mov", 2),
            new ManifestRecord { Path = "a.mov", Generation = 2, BlockSize = 4096, Size = 4096, Blocks = ManifestRecord.Hex([9UL]) },
        };
        var files = ManifestFile.Files(records, 1);
        Assert.Equal([2UL], files["A.MOV"].ToManifest()!.Blocks);
        Assert.Equal([9UL], ManifestFile.Files(records, 2)["a.mov"].ToManifest()!.Blocks);
    }

    static ManifestRecord Part(string path, int first, params ulong[] blocks) => new()
    {
        Kind = ManifestRecord.PartKind, Path = path, Size = 10 * 4096, Modified = T, BlockSize = 4096, First = first, Blocks = ManifestRecord.Hex(blocks),
    };

    [Fact]
    public void Parts_ChainUp_GapIgnored_RestartFromZero()
    {
        var chained = ManifestFile.Parts([Part("a.mov", 0, 1, 2), Part("a.mov", 2, 3), Part("a.mov", 5, 6)], 1);
        Assert.Equal([1UL, 2UL, 3UL], chained["a.mov"].Blocks);

        var restarted = ManifestFile.Parts([Part("a.mov", 0, 1, 2), Part("a.mov", 0, 7)], 1);
        Assert.Equal([7UL], restarted["a.mov"].Blocks);
    }

    [Fact]
    public void ToManifest_WrongCountOrBadHex_Null()
    {
        Assert.Null(new ManifestRecord { Path = "a", Size = 8192, BlockSize = 4096, Blocks = ["0000000000000001"] }.ToManifest());
        Assert.Null(new ManifestRecord { Path = "a", Size = 4096, BlockSize = 4096, Blocks = ["zz"] }.ToManifest());
        Assert.Null(Part("a", 0, 1).ToManifest());
    }
}
