using System.Security.Cryptography;
using System.Text;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class CardFingerprintTests
{
    static readonly DateTime T0 = new(2026, 10, 6, 8, 15, 0, DateTimeKind.Utc);

    static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void Format_IsSortedLinesOfPathSizeTicks()
    {
        SourceFile[] files = [new("CLIP/b.mov", 20, T0.AddSeconds(2)), new("CLIP/a.mov", 10, T0)];
        var (fingerprint, shape) = CardFingerprint.Compute(files);
        Assert.Equal(Sha($"CLIP/a.mov\t10\t{T0.Ticks}\nCLIP/b.mov\t20\t{T0.AddSeconds(2).Ticks}\n"), fingerprint);
        Assert.Equal(Sha("CLIP/a.mov\t10\nCLIP/b.mov\t20\n"), shape);
    }

    [Fact]
    public void Order_DoesNotMatter()
    {
        SourceFile[] a = [new("A/1.mov", 1, T0), new("B/2.mov", 2, T0), new("a/3.mov", 3, T0)];
        Assert.Equal(CardFingerprint.Compute(a), CardFingerprint.Compute(a.Reverse()));
    }

    [Fact]
    public void Mtime_ChangesFingerprintOnly_SizeChangesBoth()
    {
        var before = CardFingerprint.Compute([new SourceFile("a.mov", 10, T0)]);
        var touched = CardFingerprint.Compute([new SourceFile("a.mov", 10, T0.AddHours(1))]);
        var grown = CardFingerprint.Compute([new SourceFile("a.mov", 11, T0)]);
        Assert.NotEqual(before.Fingerprint, touched.Fingerprint);
        Assert.Equal(before.Shape, touched.Shape);
        Assert.NotEqual(before.Fingerprint, grown.Fingerprint);
        Assert.NotEqual(before.Shape, grown.Shape);
    }

    [Fact]
    public void EmptyCard_IsTheHashOfNothing()
    {
        const string empty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        Assert.Equal((empty, empty), CardFingerprint.Compute(Array.Empty<SourceFile>()));
    }

    // Review focus 2: FAT32 card after a daylight-saving change.
    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    [InlineData(-1, true, true)]
    [InlineData(14, true, true)]
    [InlineData(15, true, false)]
    [InlineData(1, false, false)]   // exFAT/NTFS store UTC: an hour off is a changed file
    public void FileTimes_WholeHoursOnlyOnLocalTimeFilesystems(int hours, bool fat, bool same) =>
        Assert.Equal(same, FileTimes.Same(T0, T0.AddHours(hours), fat));

    [Fact]
    public void FileTimes_HourAndASecond_IsChanged()
    {
        Assert.False(FileTimes.Same(T0, T0.AddHours(1).AddSeconds(2), localTimeFilesystem: true));
        Assert.True(FileTimes.StoresLocalTime("FAT32"));
        Assert.True(FileTimes.StoresLocalTime("FAT"));
        Assert.False(FileTimes.StoresLocalTime("exFAT"));
        Assert.False(FileTimes.StoresLocalTime("NTFS"));
        Assert.False(FileTimes.StoresLocalTime(null));
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1_900, false, true)]        // a report keeps whole seconds, FAT 2-second steps
    [InlineData(2_500, false, false)]
    [InlineData(3_600_000, true, true)]     // FAT32 after the change to winter time
    [InlineData(3_601_500, true, true)]
    [InlineData(3_605_000, true, false)]
    [InlineData(3_600_000, false, false)]   // exFAT: an hour off is another file
    public void FileTimes_Close(int millis, bool fat, bool close) =>
        Assert.Equal(close, FileTimes.Close(T0, T0.AddMilliseconds(millis), fat));

    [Fact]
    public void FileList_RoundTrip_MissingAndDamaged()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "history", "x.files.gz");
        SourceFile[] files = [new("CLIP/Příliš žluťoučký.mov", 123_456_789_012, T0), new("a b/c.mxf", 0, T0.AddTicks(1))];
        FileListStore.Write(path, files);
        Assert.Equal(files, FileListStore.Read(path));
        Assert.Null(FileListStore.Read(Path.Combine(dir.Path, "missing.files.gz")));
        File.WriteAllText(path, "not gzip");
        Assert.Null(FileListStore.Read(path));
    }

    [Fact]
    public void FileList_TenThousandFiles_IsSmall()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "big.files.gz");
        var files = Enumerable.Range(0, 10_000).Select(i => new SourceFile($"DCIM/100CANON/IMG_{i:0000}.CR3", 30_000_000 + i, T0.AddSeconds(i))).ToList();
        FileListStore.Write(path, files);
        Assert.True(new FileInfo(path).Length < 400_000);
        Assert.Equal(10_000, FileListStore.Read(path)!.Count);
    }
}
