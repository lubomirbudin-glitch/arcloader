using System.Text.Json.Nodes;

namespace Offload.Core.Tests;

public class RawBackendsTests
{
    [Theory]
    [InlineData(23.976, "24000/1001")]
    [InlineData(29.97, "30000/1001")]
    [InlineData(25.0, "25")]
    [InlineData(12.5, "12.500")]
    public void Rational(double fps, string expected) => Assert.Equal(expected, RawBackends.Rational(fps));

    [Fact]
    public void Braw_InfoAndScale()
    {
        var i = BrawBackend.ParseInfo("""{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976,"frames":240,"timecode":"01:02:03:04","camera":"URSA Mini Pro 12K"}""");
        var c = BrawBackend.ToClipInfo(i);
        Assert.Equal("Blackmagic RAW", c.Codec);
        Assert.Equal(11520, c.Width);
        Assert.Equal("24000/1001", c.FrameRate);
        Assert.Equal(240, c.Frames);
        Assert.Equal(10.01, c.Duration!.Value, 2);
        Assert.Equal("01:02:03:04", c.Timecode);
        Assert.Equal("quarter", BrawBackend.Scale(6480, 1080));
        Assert.Equal("eighth", BrawBackend.Scale(6480, 180));
        Assert.Equal("full", BrawBackend.Scale(1080, 1080));
    }

    [Fact]
    public void R3d_ParseMeta()
    {
        var c = R3dBackend.ParseMeta("Frame Width: 8192\nFrame Height: 4320\nRecord FPS: 23.976\nTotal Frames: 240\nAbs TC: 12:34:56:01\nCamera Model: RED V-RAPTOR\n");
        Assert.Equal("REDCODE RAW", c.Codec);
        Assert.Equal(8192, c.Width);
        Assert.Equal(4320, c.Height);
        Assert.Equal("24000/1001", c.FrameRate);
        Assert.Equal(240, c.Frames);
        Assert.Equal("12:34:56:01", c.Timecode);
        Assert.Equal("RED V-RAPTOR", c.Camera);
    }

    [Fact]
    public void R3d_ArgsPickResolutionAndLogWithLut()
    {
        var info = new ClipInfo { Height = 4320 };
        var args = R3dBackend.Args(@"E:\A.R3D", @"C:\t\r3d", info, new ProxySettings { Height = 1080, LutPath = "x.cube" });
        Assert.Equal(["--i", @"E:\A.R3D", "--outDir", @"C:\t\r3d", "--o", "intermediate", "--format", "201", "--PRcodec", "2", "--res", "4", "--useMeta",
            "--colorSciVersion", "3", "--colorSpace", "25", "--gammaCurve", "34"], args);
        Assert.Contains("3", R3dBackend.Args("a", "b", new ClipInfo { Height = 2160 }, new ProxySettings()));
        Assert.Contains("1", R3dBackend.Args("a", "b", new ClipInfo { Height = 1080 }, new ProxySettings()));
    }

    const string ArriJson = """
        {"clipBasedMetadata":{"cameraModel":"ALEXA 35","videoCodec":"ARRIRAW","storedSize":{"width":4608,"height":3164},
          "clipDuration":48,"sceneColorEncodingTransferCurve":"LogC4","lensSqueeze":2000,
          "nativePictureEssenceDescriptor":{"file":{"sampleRate":"24/1"}}},
         "frameBasedMetadata":{"frames":[{"timecode":"10:00:00:00"},{"timecode":"10:00:00:01"}]}}
        """;

    [Fact]
    public void Arri_ParseMetadata()
    {
        var c = ArriBackend.ParseMetadata(ArriJson);
        Assert.Equal("ARRIRAW", c.Codec);
        Assert.Equal(4608, c.Width);
        Assert.Equal(3164, c.Height);
        Assert.Equal("24", c.FrameRate);
        Assert.Equal(48, c.Frames);
        Assert.Equal(2.0, c.Duration);
        Assert.Equal("10:00:00:00", c.Timecode);
        Assert.Equal("ALEXA 35", c.Camera);
        Assert.Equal("LogC4", c.LogEncoding);
        Assert.Equal(2.0, c.Squeeze);
        Assert.Equal(0, c.AudioChannels);
    }

    [Theory]
    [InlineData("ARRI_LogC4-to-Gamma24_Rec709.cube", null, null, "AWG4/D65/LogC4")]
    [InlineData("show_LogC3_709.cube", "LogC4", null, "AWG3/D65/LogC3")]
    [InlineData(null, "LogC4", null, "AWG4/D65/LogC4")]
    [InlineData(null, null, "ALEXA Mini", "AWG3/D65/LogC3")]
    [InlineData(null, null, "ALEXA 35", "AWG4/D65/LogC4")]
    public void Arri_ColorSpace(string? lut, string? log, string? camera, string expected) =>
        Assert.Equal(expected, ArriBackend.ColorSpace(lut, new ClipInfo { LogEncoding = log, Camera = camera }));

    [Fact]
    public void Arri_ArgsWithFrameLinesCropOnSensor()
    {
        var info = new ClipInfo { Width = 4608, Height = 3164 };
        var (args, json, cropped) = ArriBackend.Args(@"E:\c.mxf", @"C:\t\i.mxf", info, new ProxySettings { FrameAspect = 1.66, Height = 1080 }, @"C:\t\p.json");
        Assert.True(cropped);
        Assert.Equal(["process", "--input", @"E:\c.mxf", "--video-codec", "prores422lt", "--deflip", "--output", @"C:\t\i.mxf",
            "--parameters", @"C:\t\p.json", "--target-colorspace", "Rec.709/D65/BT.1886", "--embedded-look"], args);
        var p = JsonNode.Parse(json!)!;
        Assert.Equal(0, (int)p["inputCrop"]!["displayXOffsetOverride"]!);
        Assert.Equal(194, (int)p["inputCrop"]!["displayYOffsetOverride"]!);
        Assert.Equal(4608, (int)p["inputCrop"]!["displayWidthOverride"]!);
        Assert.Equal(2774, (int)p["inputCrop"]!["displayHeightOverride"]!);
        Assert.Equal(1794, (int)p["outputScaling"]!["targetWidth"]!);
        Assert.Equal(1080, (int)p["outputScaling"]!["targetHeight"]!);
    }

    [Fact]
    public void Arri_ArgsWithoutCropDesqueezeAndLog()
    {
        var (args, json, cropped) = ArriBackend.Args("in", "out", new ClipInfo { Camera = "ALEXA 35" }, new ProxySettings { Height = 720, LutPath = "look.cube" }, "p.json");
        Assert.False(cropped);
        Assert.Null(json);
        Assert.Equal(["process", "--input", "in", "--video-codec", "prores422lt", "--deflip", "--output", "out",
            "--desqueeze", "--output-height", "720", "--target-colorspace", "AWG4/D65/LogC4"], args);
    }

    [Fact]
    public void Availability_FollowsTools()
    {
        var none = new ExternalTools { NoSearch = true }.Resolved();
        Assert.All(RawBackends.All, b => Assert.False(b.IsAvailable(none)));
        Assert.Equal([ClipFormat.Braw, ClipFormat.R3d, ClipFormat.Arriraw, ClipFormat.CanonRaw], RawBackends.All.Select(b => b.Format));
    }
}
