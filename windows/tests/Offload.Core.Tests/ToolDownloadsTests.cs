using Offload.Core;
using Offload.Core.Desktop;
using Xunit;

namespace Offload.Core.Tests;

/// A clip that got no proxy for a missing decoder, and Settings → Dekodéry RAW, say where to download it.
public class ToolDownloadsTests
{
    [Fact]
    public void Links_AreTheMakersHttpsPages()
    {
        foreach (var url in new[] { ToolDownloads.Ffmpeg, ToolDownloads.BlackmagicRaw, ToolDownloads.Redline, ToolDownloads.ArtCmd })
            Assert.StartsWith("https://", url);
        Assert.Contains("blackmagicdesign.com", ToolDownloads.BlackmagicRaw);
        Assert.Contains("reddigitalcinema.com", ToolDownloads.Redline);
        Assert.Contains("arri.com", ToolDownloads.ArtCmd);
    }

    [Theory]
    [InlineData(ClipFormat.Braw, "b.braw", ToolDownloads.BlackmagicRaw)]
    [InlineData(ClipFormat.R3d, "r.R3D", ToolDownloads.Redline)]
    [InlineData(ClipFormat.Arriraw, "a.mxf", ToolDownloads.ArtCmd)]
    public void MissingDecoder_NoteHasTheDownloadPage(ClipFormat format, string file, string url)
    {
        var ff = new ExternalTools { Ffmpeg = @"C:\Windows\System32\cmd.exe", NoSearch = true }.Resolved();
        using var r = new ProxyRenderer(new ProxySettings(), ff);
        var note = r.UnsupportedReason(new Clip { Name = "x", Path = file, Format = format, Segments = [file] });
        Assert.EndsWith("ke stažení: " + url, note);
    }

    [Fact]
    public void NoFfmpeg_WarningHasTheDownloadPage()
    {
        var none = new ExternalTools { NoSearch = true }.Resolved();
        Assert.EndsWith("ke stažení: " + ToolDownloads.Ffmpeg, ProxyPreflight.Warning(new ProxySettings { Enabled = true }, analyzeClips: false, none));
    }
}
