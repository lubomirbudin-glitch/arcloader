namespace Offload.Core.Tests;

public class FilterGraphTests
{
    [Fact]
    public void Thumbnail()
    {
        Assert.Equal("scale=-2:180:flags=bicubic,scale=out_color_matrix=bt601:out_range=pc,format=yuvj420p", FilterGraph.Thumbnail(null, premap: false));
        Assert.Equal("scale=-2:180:flags=bicubic,format=rgb48le,lut3d=file=look.cube:interp=tetrahedral,scale=out_color_matrix=bt601:out_range=pc,format=yuvj420p", FilterGraph.Thumbnail(FilterGraph.LutFile, premap: false));
    }

    [Fact]
    public void BurnIn_NameAndTimecode()
    {
        var s = new ProxySettings();
        Assert.Equal(
        [
            "drawbox=x=0:y=ih-64:w=iw:h=64:color=black@0.55:t=fill",
            "drawtext=fontfile=font.ttf:fontcolor=white:fontsize=32:y=h-48:textfile=label-1.txt:expansion=none:x=21",
            "drawtext=fontfile=font.ttf:fontcolor=white:fontsize=32:y=h-48:timecode='01\\:00\\:00\\:00':rate=25/1:x=w-tw-21",
        ], FilterGraph.BurnIn(s, 1080, hasFont: true, "label-1.txt", "01:00:00:00", "25/1"));
    }

    // Final review A2: a % in the clip name must not be read as a drawtext expansion.
    [Fact]
    public void BurnIn_NameIsLiteral()
    {
        var name = FilterGraph.BurnIn(new ProxySettings { BurnTimecode = false }, 720, true, "label-2.txt", null, null)[1];
        Assert.Contains(":textfile=label-2.txt:expansion=none:", name);
    }

    [Fact]
    public void BurnIn_InvalidTimecodeStartsAtZero_DropFrameEscaped()
    {
        var s = new ProxySettings { BurnFilename = false };
        Assert.Equal("drawtext=fontfile=font.ttf:fontcolor=white:fontsize=12:y=h-18:timecode='00\\:00\\:00\\:00':rate=24/1:x=w-tw-8",
            FilterGraph.BurnIn(s, 360, true, null, "bad", "24/1")[1]);
        Assert.EndsWith("timecode='10\\:00\\:00\\;00':rate=30000/1001:x=w-tw-8",
            FilterGraph.BurnIn(s, 360, true, null, "10:00:00;00", "30000/1001")[1]);
    }

    [Fact]
    public void BurnIn_NothingWithoutFontOrWhenDisabled()
    {
        Assert.Empty(FilterGraph.BurnIn(new ProxySettings(), 1080, hasFont: false, "l.txt", "01:00:00:00", "25/1"));
        Assert.Empty(FilterGraph.BurnIn(new ProxySettings { BurnFilename = false, BurnTimecode = false }, 1080, true, "l.txt", null, null));
    }

    [Fact]
    public void Proxy_FullChain()
    {
        var s = new ProxySettings { FrameAspect = 1.66, FrameScale = 0.9, BurnTimecode = false };
        Assert.Equal(
            "crop=w='trunc(min(iw*0.9,ih*0.9*1.66)/2)*2':h='trunc(min(iw*0.9,ih*0.9*1.66)/1.66/2)*2'," +
            "scale=-2:720:flags=bicubic,setsar=1,format=rgb48le,lut3d=file=look.cube:interp=tetrahedral," +
            "drawbox=x=0:y=ih-42:w=iw:h=42:color=black@0.55:t=fill," +
            "drawtext=fontfile=font.ttf:fontcolor=white:fontsize=21:y=h-31:textfile=label.txt:expansion=none:x=14," +
            "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,setparams=color_primaries=bt709:color_trc=bt709",
            FilterGraph.Proxy(s, 720, alreadyCropped: false, lutFile: FilterGraph.LutFile, premap: false, hasFont: true, "label.txt", "01:00:00:00", "25/1"));
    }

    [Fact]
    public void Proxy_BuiltinLutOnFullRange_PreMapBeforeLut()
    {
        var s = new ProxySettings { BurnFilename = false, BurnTimecode = false };
        Assert.Equal("scale=-2:720:flags=bicubic,setsar=1,format=rgb48le," + FilterGraph.PreMap + ",lut3d=file=lut-x.cube:interp=tetrahedral," +
            "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,setparams=color_primaries=bt709:color_trc=bt709",
            FilterGraph.Proxy(s, 720, alreadyCropped: false, lutFile: "lut-x.cube", premap: true, hasFont: true, null, null, null));
    }

    [Fact]
    public void Proxy_AlreadyCroppedProresNoLut()
    {
        var s = new ProxySettings { FrameAspect = 2.39, Codec = ProxyCodec.ProresProxy, BurnFilename = false, BurnTimecode = false };
        Assert.Equal("scale=-2:1080:flags=bicubic,setsar=1,scale=out_color_matrix=bt709:out_range=tv,format=yuv422p10le,setparams=color_primaries=bt709:color_trc=bt709",
            FilterGraph.Proxy(s, 1080, alreadyCropped: true, lutFile: null, premap: false, hasFont: true, null, null, null));
    }
}
