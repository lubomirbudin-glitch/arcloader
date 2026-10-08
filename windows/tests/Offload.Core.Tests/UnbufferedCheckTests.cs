using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 8 "Kontrola za běhu", R8: a destination is verified from the proxy's reads only when its copy was written past
/// the file cache and its volume honours FILE_FLAG_NO_BUFFERING; network volumes never. R12: the volume serial.
public class UnbufferedCheckTests
{
    [Fact]
    public void LocalNtfsTemp_Passes_ProbeRemoved()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-08", "A001");
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));

        Assert.Null(UnbufferedCheck.Problem(card, writtenUnbuffered: true));
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_REPORT")));
    }

    [Fact]
    public void BufferedWrites_Classic()
    {
        using var dir = new TempDir();
        Assert.Equal(Msg.ClassicBufferedWrites, UnbufferedCheck.Problem(dir.Path, writtenUnbuffered: false));
    }

    [Fact]
    public void ProbeCannotBeWritten_Classic_WithTheReason()
    {
        using var dir = new TempDir();
        Assert.StartsWith("kontrolu čtení mimo cache nejde provést", UnbufferedCheck.Problem(Path.Combine(dir.Path, "gone"), writtenUnbuffered: true));
    }

    [Fact]
    public void IsRemote_UncYes_LocalNo()
    {
        Assert.True(NativeIO.IsRemote(@"\\server\share\Zaloha"));
        Assert.False(NativeIO.IsRemote(Path.GetTempPath()));
    }

    [Fact]
    public void VolumeSerialText_FormattedLikeVol()
    {
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}$", NativeIO.VolumeSerialText(Path.GetTempPath()));
    }

    static string CardWithReport(TempDir dir)
    {
        var card = dir.Sub("2026-10-08", "A001");
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        return card;
    }

    // Ruling M3: non-cached I/O on an NTFS-compressed or encrypted stream may still go through the cache — such a card
    // folder (or its _REPORT) is verified the classic way, before any probe is written.
    [Theory]
    [InlineData(FileAttributes.Compressed, false)]
    [InlineData(FileAttributes.Encrypted, false)]
    [InlineData(FileAttributes.Compressed, true)]
    [InlineData(FileAttributes.Encrypted, true)]
    public void CompressedOrEncryptedFolder_Classic(FileAttributes attribute, bool onReport)
    {
        using var dir = new TempDir();
        var card = CardWithReport(dir);
        var marked = onReport ? Path.Combine(card, "_REPORT") : card;
        FileAttributes Attributes(string p) => File.GetAttributes(p) | (string.Equals(Path.GetFullPath(p).TrimEnd('\\'), Path.GetFullPath(marked).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? attribute : 0);

        Assert.Equal(Msg.ClassicCompressedOrEncrypted, UnbufferedCheck.Problem(card, writtenUnbuffered: true, Attributes));
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_REPORT")));
        Assert.Null(UnbufferedCheck.Problem(card, writtenUnbuffered: true, File.GetAttributes));
    }

    // Ruling I3: a volume that refuses FILE_FLAG_NO_BUFFERING is a reason to verify the classic way — never an exception.
    [Fact]
    public void VolumeRefusingNoBuffering_Classic_WithTheReason_ProbeRemoved()
    {
        using var dir = new TempDir();
        var card = CardWithReport(dir);
        NativeIO.TestVolumes[dir.Path] = new NativeIO.TestVolume { RefuseUnbuffered = true };
        try
        {
            Assert.StartsWith("kontrolu čtení mimo cache nejde provést", UnbufferedCheck.Problem(card, writtenUnbuffered: true));
        }
        finally
        {
            NativeIO.TestVolumes.TryRemove(dir.Path, out _);
        }
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_REPORT")));
    }

    // A volume that silently ignores the flag (accepts an unaligned read) fails the cache check.
    [Fact]
    public void VolumeIgnoringNoBuffering_FailsTheCacheCheck()
    {
        using var dir = new TempDir();
        var card = CardWithReport(dir);
        NativeIO.TestVolumes[dir.Path] = new NativeIO.TestVolume { IgnoreUnbuffered = true };
        try
        {
            Assert.Equal(Msg.ClassicCacheCheck, UnbufferedCheck.Problem(card, writtenUnbuffered: true));
        }
        finally
        {
            NativeIO.TestVolumes.TryRemove(dir.Path, out _);
        }
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_REPORT")));
    }
}
