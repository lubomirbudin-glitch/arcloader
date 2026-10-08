using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// R3 mode A with ffmpeg 8.1: the proxy reads the clip through the tap — moov at the end (MOV), faststart (MP4), MXF —
/// every block it reads is verified, the fill-in reads only the rest, a flipped byte on the disk is found.
public class TapRenderTests
{
    const int Block = 64 * 1024;
    /// What the tap may read ahead on the connections ffmpeg drops to seek (socket buffers; measured ~0.6 MB each with ffmpeg
    /// 8.1 — with 16 MiB blocks at most one block: MOV 101.8 %, MXF 116.7 % of an 80 MB file, plan 10 measurements).
    const long Slack = 2 << 20;

    static BlockManifest ManifestOf(byte[] data) => new(data.Length, Block,
        Enumerable.Range(0, BlockManifest.CountFor(data.Length, Block))
            .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, Math.Min(Block, data.Length - b * Block)))).ToList());

    static ProxyRenderer Renderer() => new(new ProxySettings { Enabled = true, Height = 240, BurnFilename = false, BurnTimecode = false }, new ExternalTools().Resolved())
    {
        ForcedEncoder = "libx264",
    };

    /// Renders `rel` from `card` through the tap; returns the verifier, the bytes the tap read from the disk and the proxy.
    static (BlockVerifier Verifier, long ReadFromDisk, string Proxy) RenderThroughTap(string card, string rel, byte[] cardBytes)
    {
        var verifier = new BlockVerifier(ManifestOf(cardBytes));
        using var renderer = Renderer();
        var clip = new Clip { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = cardBytes.Length };
        var info = renderer.Probe(clip, card);
        var proxy = Path.Combine(card, "_PROXY", clip.Name + ".mov");
        using var server = new RangeServer();
        using var tap = new TapFile(Layout.Join(card, rel), verifier);
        var url = server.Register(tap, Path.GetFileName(rel));
        renderer.Render(clip, card, info, null, proxy, _ => { }, () => false, url);
        return (verifier, tap.ReadFromDisk, proxy);
    }

    [FfmpegFact]
    public void Mov_MoovAtEnd_ReadOnce_FillInVerifiesTheRest()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C001.mov"), seconds: 2, width: 640, height: 360);
        var bytes = File.ReadAllBytes(Path.Combine(card, "CLIP", "A001C001.mov"));

        var (v, read, proxy) = RenderThroughTap(card, "CLIP/A001C001.mov", bytes);

        Assert.True(File.Exists(proxy));
        // Measured with ffmpeg 8.1: the header, the moov at the end, then the mdat in order — each block read once.
        Assert.True(read <= bytes.Length + Slack, $"{read} of {bytes.Length}");
        Assert.True(v.VerifiedBytes >= bytes.Length - 2 * Block, $"verified {v.VerifiedBytes} of {bytes.Length}");
        Assert.NotEqual(BlockOutcomeKind.Mismatch, v.Finish().Kind);
        Assert.Equal(BlockOutcomeKind.Verified, BlockReads.Complete(Path.Combine(card, "CLIP", "A001C001.mov"), v).Kind);
    }

    [FfmpegFact]
    public void Mp4_Faststart_Verified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "M4ROOT", "CLIP", "C0001.MP4");
        TestMedia.MakeFaststart(path);
        var bytes = File.ReadAllBytes(path);

        var (v, read, _) = RenderThroughTap(card, "M4ROOT/CLIP/C0001.MP4", bytes);

        Assert.True(read <= bytes.Length + Slack, $"{read} of {bytes.Length}");
        Assert.Equal(BlockOutcomeKind.Verified, BlockReads.Complete(path, v).Kind);
    }

    [FfmpegFact]
    public void Mxf_FooterAndIndex_Verified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "XDROOT", "Clip", "C0001.MXF");
        TestMedia.MakeMxf(path, monoTracks: 2, seconds: 1);
        var bytes = File.ReadAllBytes(path);

        var (v, read, proxy) = RenderThroughTap(card, "XDROOT/Clip/C0001.MXF", bytes);

        Assert.True(File.Exists(proxy));
        Assert.True(read <= bytes.Length + Slack, $"{read} of {bytes.Length}");
        Assert.Equal(BlockOutcomeKind.Verified, BlockReads.Complete(path, v).Kind);
    }

    // Spec 11: a bit flipped on the destination (outside Offload, past the file cache) is found by the tap's reads.
    [FfmpegFact]
    public void FlippedByteOnTheDisk_TheTapFindsIt()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "A001C002.mov");
        TestMedia.MakeClip(path, seconds: 2, width: 640, height: 360);
        var bytes = File.ReadAllBytes(path);
        long at = bytes.Length / 2;
        DiskFlip.Flip(path, at);

        var (v, _, _) = RenderThroughTap(card, "A001C002.mov", bytes);

        var outcome = v.Finish();
        Assert.Equal(BlockOutcomeKind.Mismatch, outcome.Kind);
        Assert.Equal((int)(at / Block), outcome.Block);
        Assert.Equal(BlockOutcomeKind.Mismatch, BlockReads.Complete(path, v).Kind);   // read again: still wrong → failed
        Assert.Empty(v.UnstableBlocks);
    }
}

/// Batch 7–9 (beyond the brief): a slow destination and the proxy's stall watchdog. The tap's disk activity counts as
/// progress — a destination that is slow but alive never gets ffmpeg killed; one that hangs still does.
public class TapWatchdogTests
{
    /// Serves `data`; the first read in the middle of the file takes 6 s — with activity every 200 ms (`alive`: a slow
    /// disk, each piece of the block arriving) or none (a hung disk).
    sealed class SlowMiddle(byte[] data, bool alive) : ITapSource
    {
        long activity;
        int slowed;
        public long Length => data.Length;
        public long Activity => Interlocked.Read(ref activity);

        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= data.Length * 2L / 5 && offset < data.Length * 3L / 5 && Interlocked.Exchange(ref slowed, 1) == 0)
            {
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(200);
                    if (alive) Interlocked.Add(ref activity, 1 << 20);
                }
            }
            int n = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
    }

    /// MPEG-2 in a MOV (moov at the end): decoded in software, so a stall is not retried with another decoder.
    static (string Card, Clip Clip, byte[] Bytes) Mpeg2Mov(TempDir dir)
    {
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "A001C009.mov");
        ToolProcess.Run(TestMedia.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=6",
            "-c:v", "mpeg2video", "-q:v", "2", "-g", "12", path]);
        var bytes = File.ReadAllBytes(path);
        return (card, new Clip { Name = "A001C009", Path = "A001C009.mov", Format = ClipFormat.Video, Segments = ["A001C009.mov"], Bytes = bytes.Length }, bytes);
    }

    static ProxyRenderer Renderer() => new(new ProxySettings { Enabled = true, Height = 240, BurnFilename = false, BurnTimecode = false }, new ExternalTools().Resolved())
    {
        ForcedEncoder = "libx264",
        StallTimeout = TimeSpan.FromSeconds(1.5),
    };

    [FfmpegFact]
    public void SlowButAliveDestination_NotKilled()
    {
        using var dir = new TempDir();
        var (card, clip, bytes) = Mpeg2Mov(dir);
        using var renderer = Renderer();
        var info = renderer.Probe(clip, card);
        Assert.False(ProxyRenderer.UseHardwareDecoding(info.Codec));
        using var server = new RangeServer();
        var url = server.Register(new SlowMiddle(bytes, alive: true), "A001C009.mov");
        var proxy = Path.Combine(card, "_PROXY", "A001C009.mov");

        renderer.Render(clip, card, info, null, proxy, _ => { }, () => false, url);

        Assert.True(File.Exists(proxy));
    }

    [FfmpegFact]
    public void HungDestination_StillKilledByTheWatchdog()
    {
        using var dir = new TempDir();
        var (card, clip, bytes) = Mpeg2Mov(dir);
        using var renderer = Renderer();
        var info = renderer.Probe(clip, card);
        using var server = new RangeServer();
        var url = server.Register(new SlowMiddle(bytes, alive: false), "A001C009.mov");
        var proxy = Path.Combine(card, "_PROXY", "A001C009.mov");

        Assert.Throws<FfmpegStalledException>(() => renderer.Render(clip, card, info, null, proxy, _ => { }, () => false, url));
        Assert.False(File.Exists(proxy));
    }
}

/// Spec 11 "test cache": changes a byte of a file through a second NO_BUFFERING | WRITE_THROUGH handle — the sector is
/// read, changed and written back past the file cache (on the disk now; NTFS and exFAT keep a cached copy coherent, so
/// any later read sees it). A change behind the file system's back (a raw device write) needs admin rights: manual test.
public static class DiskFlip
{
    public static void Flip(string path, long offset)
    {
        using var h = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, (FileOptions)0x20000000 | FileOptions.WriteThrough);
        using var sector = new AlignedBuffer(AlignedBuffer.Alignment);
        long at = offset / AlignedBuffer.Alignment * AlignedBuffer.Alignment;
        int n = RandomAccess.Read(h, sector.Span, at);
        sector.Span[(int)(offset - at)] ^= 0x01;
        RandomAccess.Write(h, sector.Span, at);
        if (n < AlignedBuffer.Alignment) RandomAccess.SetLength(h, at + n);   // the last sector: keep the file's length
    }
}
