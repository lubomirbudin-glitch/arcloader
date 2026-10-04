namespace Offload.Core.Tests;

public class ClipDetectorTests
{
    static SourceFile F(string rel, long size = 100) => new(rel, size, DateTime.UtcNow);

    [Fact]
    public void Detect_GroupsFormatsAndMarksCameraProxies()
    {
        var clips = ClipDetector.Detect(
        [
            F("_REPORT/x.mov"),
            F("PRIVATE/M4ROOT/CLIP/C0010.MP4"),
            F("PRIVATE/M4ROOT/CLIP/C0002.MP4"),
            F("PRIVATE/M4ROOT/SUB/C0002S03.MP4"),
            F("PRIVATE/M4ROOT/CLIP/C0002M01.XML"),
            F("empty.mov", 0),
            F("A001_C002_0101AB.RDM/A001_C002_0101AB_002.R3D", 200),
            F("A001_C002_0101AB.RDM/A001_C002_0101AB_001.R3D", 300),
            F("ARRI/A_0001C001/A_0001C001.0000001.ari", 10),
            F("ARRI/A_0001C001/A_0001C001.0000000.ari", 10),
            F("BRAW/A001_08151200_C001.braw"),
            F("CRM/A001C001.CRM"),
        ]);
        Assert.Equal(
        [
            ("A001_C002_0101AB", ClipFormat.R3d, "A001_C002_0101AB.RDM/A001_C002_0101AB_001.R3D"),
            ("A_0001C001", ClipFormat.Arriraw, "ARRI/A_0001C001"),
            ("A001_08151200_C001", ClipFormat.Braw, "BRAW/A001_08151200_C001.braw"),
            ("A001C001", ClipFormat.CanonRaw, "CRM/A001C001.CRM"),
            ("C0002", ClipFormat.Video, "PRIVATE/M4ROOT/CLIP/C0002.MP4"),
            ("C0010", ClipFormat.Video, "PRIVATE/M4ROOT/CLIP/C0010.MP4"),
            ("C0002S03", ClipFormat.Video, "PRIVATE/M4ROOT/SUB/C0002S03.MP4"),
        ], clips.Select(c => (c.Name, c.Format, c.Path)));
        var r3d = clips[0];
        Assert.Equal(["A001_C002_0101AB.RDM/A001_C002_0101AB_001.R3D", "A001_C002_0101AB.RDM/A001_C002_0101AB_002.R3D"], r3d.Segments);
        Assert.Equal(500, r3d.Bytes);
        Assert.Equal(2, clips[1].Segments.Count);
        Assert.True(clips[6].IsCameraProxy);
        Assert.False(clips[4].IsCameraProxy);
    }

    [Fact]
    public void Parse_ReadsVideoAudioTimecodeAndCamera()
    {
        const string json = """
            {"streams":[
              {"codec_type":"video","codec_name":"mjpeg","disposition":{"attached_pic":1},"width":320,"height":240},
              {"codec_type":"video","codec_name":"h264","profile":"High","width":1920,"height":1080,
               "avg_frame_rate":"25/1","r_frame_rate":"25/1","nb_frames":"100","duration":"4.000000",
               "disposition":{"attached_pic":0},"tags":{"timecode":"01:00:00:00"}},
              {"codec_type":"audio","channels":2},
              {"codec_type":"audio","channels":2}],
             "format":{"duration":"4.010000","tags":{"com.apple.quicktime.make":"Sony"}}}
            """;
        var i = FFProbe.Parse(json);
        Assert.Equal("h264 High", i.Codec);
        Assert.Equal(1920, i.Width);
        Assert.Equal(1080, i.Height);
        Assert.Equal("25/1", i.FrameRate);
        Assert.Equal(100, i.Frames);
        Assert.Equal(4.01, i.Duration);
        Assert.Equal("01:00:00:00", i.Timecode);
        Assert.Equal(4, i.AudioChannels);
        Assert.Equal("Sony", i.Camera);
    }

    [Fact]
    public void Parse_UndecodableCodecHasNoSize_TimecodeFromAnyStream()
    {
        const string json = """
            {"streams":[{"codec_type":"video","codec_name":"unknown","width":0,"height":0,"avg_frame_rate":"0/0","r_frame_rate":"24/1"},
                        {"codec_type":"data","tags":{"timecode":"10:00:00:00"}}],"format":{}}
            """;
        var i = FFProbe.Parse(json);
        Assert.Null(i.Width);
        Assert.Equal("24/1", i.FrameRate);
        Assert.Equal("10:00:00:00", i.Timecode);
        Assert.Equal(0, i.AudioChannels);
    }

    [FfmpegFact]
    public void Probe_RealClip()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "Záběr 'A' 01.mov");
        TestMedia.MakeClip(path, seconds: 4, timecode: "01:00:00:00");
        var i = FFProbe.Probe(TestMedia.Ffprobe!, path);
        Assert.Equal(1280, i.Width);
        Assert.Equal("25/1", i.FrameRate);
        Assert.Equal("01:00:00:00", i.Timecode);
        Assert.True(i.AudioChannels > 0);
        Assert.InRange(i.Duration ?? 0, 3.9, 4.2);
    }
}
