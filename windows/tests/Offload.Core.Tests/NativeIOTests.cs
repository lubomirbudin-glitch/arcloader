namespace Offload.Core.Tests;

public class NativeIOTests
{
    [Fact]
    public void UnbufferedWrite_PaddedThenTruncated_HasExactContent()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f.bin");
        using var buf = new AlignedBuffer(8192);
        var data = new byte[5000];
        new Random(1).NextBytes(data);
        data.CopyTo(buf.Span);
        using (var h = NativeIO.OpenWrite(path, unbuffered: true, preallocate: 5000))
        {
            NativeIO.Write(h, buf.Span[..8192], 0, path);
            NativeIO.SetLength(h, 5000, path);
            NativeIO.Flush(h, path);
        }
        Assert.Equal(data, File.ReadAllBytes(path));
    }

    [Fact]
    public void FileReader_ReturnsShortReadAtEndOfFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f.bin");
        var data = TestFs.WriteRandom(path, 5000, 2);
        using var buf = new AlignedBuffer(8192);
        using var reader = new FileReader(path);
        Assert.Equal(5000, reader.Read(buf.Span, 0));
        Assert.Equal(data, buf.Span[..5000].ToArray());
        Assert.True(reader.Unbuffered);
    }

    [Fact]
    public void WriteFileDurably_CreatesAndReplaces_NoTempLeft()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "report.txt");
        NativeIO.WriteFileDurably(path, "one"u8);
        NativeIO.WriteFileDurably(path, "two"u8);
        Assert.Equal("two", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Move_WithoutReplace_FailsWhenTargetExists()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "a");
        var b = Path.Combine(dir.Path, "b");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        var e = Assert.Throws<IOError>(() => NativeIO.Move(a, b, replace: false));
        Assert.Equal("rename", e.Op);
        Assert.Equal("b", File.ReadAllText(b));
    }

    [Fact]
    public void SetModified_SetsLastWriteTime()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f");
        var when = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        using (var h = NativeIO.OpenWrite(path, unbuffered: false)) NativeIO.SetModified(h, when, path);
        Assert.Equal(when, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void VolumeQueries_DescribeTheTempVolume()
    {
        using var dir = new TempDir();
        var a = dir.Sub("a");
        var b = dir.Sub("b");
        Assert.NotNull(NativeIO.VolumeId(a));
        Assert.Equal(NativeIO.VolumeId(a), NativeIO.VolumeId(b));
        Assert.Contains(NativeIO.FilesystemType(a), new[] { "NTFS", "ReFS" });
        Assert.True(NativeIO.AvailableBytes(a) > 0);
        using var h = NativeIO.OpenWrite(Path.Combine(a, "x"), unbuffered: true);
        Assert.True(NativeIO.SectorSize(h) >= 4096);
    }

    [Fact]
    public void AlignedBuffer_RejectsUnalignedSize() => Assert.Throws<ArgumentException>(() => new AlignedBuffer(1000));

    [Fact]
    public void OpenRead_MissingFile_ThrowsIOErrorWithPath()
    {
        var e = Assert.Throws<IOError>(() => NativeIO.OpenRead(@"C:\nope\missing.bin", unbuffered: true));
        Assert.Equal("open", e.Op);
        Assert.Contains("missing.bin", e.Message);
    }

    // Ruling I1: a file that must be new (an append's reports, its MHL generation) is never written over another.
    [Fact]
    public void WriteFileNew_CreatesDurably_NeverReplaces_LeavesNoTemporaryFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "offload_report_0002.json");
        NativeIO.WriteFileNew(path, "first"u8);
        Assert.Equal("first", File.ReadAllText(path));

        var e = Assert.Throws<IOError>(() => NativeIO.WriteFileNew(path, "second"u8));

        Assert.Equal("rename", e.Op);
        Assert.Equal("first", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(dir.Path));
    }
}
