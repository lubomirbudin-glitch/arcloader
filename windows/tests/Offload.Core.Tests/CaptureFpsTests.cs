using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Offload.BrawDecode;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Task 2026-10-10 (bug): the capture frame rate from the metadata of each clip's first frame, next to the playback rate
/// of the file — ALEXA 35 shot at 35 fps showed 24 fps everywhere. Report key `clips[].info.captureFrameRate`.
public class CaptureFpsTests
{
    // --- ClipInfo: the key, the labels ---

    [Fact]
    public void Key_InTheReportOnlyWhenKnown_OlderReportsLoad()
    {
        Assert.Contains("\"captureFrameRate\":\"35\"", JsonSerializer.Serialize(new ClipInfo { FrameRate = "24", CaptureFrameRate = "35" }));
        Assert.DoesNotContain("captureFrameRate", JsonSerializer.Serialize(new ClipInfo { FrameRate = "24" }));
        var old = JsonSerializer.Deserialize<ClipInfo>("""{"codec":"ARRIRAW","frameRate":"24","duration":2.0}""")!;
        Assert.Null(old.CaptureFrameRate);
        Assert.Equal("24", old.FpsSummary);
        Assert.False(old.CaptureDiffers);
    }

    [Theory]
    [InlineData("24", "35", "35 fps · playback 24", true)]
    [InlineData("24000/1001", "120000/1001", "119.880 fps · playback 23.976", true)]
    [InlineData("25/1", "25", "25", false)]
    [InlineData("24000/1001", "23.976", "23.976", false)]   // the same rate written two ways
    [InlineData("25", null, "25", false)]                    // ffprobe only: capture = playback
    [InlineData(null, "48", "48 fps", false)]               // playback unknown: the capture rate alone
    [InlineData(null, null, "", false)]
    public void Summary(string? playback, string? capture, string expected, bool differs)
    {
        var i = new ClipInfo { FrameRate = playback, CaptureFrameRate = capture };
        Assert.Equal(expected, i.FpsSummary);
        Assert.Equal(differs, i.CaptureDiffers);
    }

    [Fact]
    public void Rational_LikeFrameRate()
    {
        Assert.Equal("35", ClipInfo.NormalizeRate("35/1"));
        Assert.Equal("24000/1001", ClipInfo.NormalizeRate("24000/1001"));
        Assert.Equal("120000/1001", ClipInfo.NormalizeRate("119.88"));
        Assert.Equal("48", ClipInfo.NormalizeRate("48.000"));
        Assert.Null(ClipInfo.NormalizeRate("0/0"));
        // Review F-4: ARRI writes some rates in thousandths — a sensible rate, the exact one as a rational where it is one.
        Assert.Equal("35", ClipInfo.NormalizeRate("35000/1000"));
        Assert.Equal("48000/1001", ClipInfo.NormalizeRate("47952/1000"));
        Assert.Equal("35", ClipInfo.NormalizeRate("35000"));
        Assert.Equal("24000/1001", ClipInfo.NormalizeRate("23976"));
        Assert.Null(ClipInfo.NormalizeRate("5000000"));
        Assert.Null(ClipInfo.NormalizeRate("abc"));
        Assert.Null(ClipInfo.NormalizeRate(null));
    }

    // --- ARRIRAW: Sensor State.sensorSampleRate of the first frame ---

    const string Alexa35At35 = """
        {"clipBasedMetadata":{"cameraModel":"ALEXA 35","videoCodec":"ARRIRAW","storedSize":{"width":4608,"height":3164},
          "clipDuration":70,"nativePictureEssenceDescriptor":{"file":{"sampleRate":"24/1"}}},
         "frameBasedMetadata":{"frames":[
           {"timecode":"10:00:00:00","Sensor State":{"sensorSampleRate":"35/1","exposureTime":"1/70"}},
           {"timecode":"10:00:00:01","Sensor State":{"sensorSampleRate":"36/1"}}]}}
        """;

    [Fact]
    public void Arri_SensorSampleRateOfTheFirstFrame_PlaybackStaysTheFiles()
    {
        var c = ArriBackend.ParseMetadata(Alexa35At35);
        Assert.Equal("24", c.FrameRate);
        Assert.Equal("35", c.CaptureFrameRate);
        Assert.Equal(70, c.Frames);
        Assert.Equal(70 / 24.0, c.Duration);   // the editing length: frames ÷ playback rate
        Assert.Equal("35 fps · playback 24", c.FpsSummary);
    }

    [Fact]
    public void Arri_SensorSampleRate_NumberOrMissing()
    {
        var number = ArriBackend.ParseMetadata("""
            {"clipBasedMetadata":{"nativePictureEssenceDescriptor":{"file":{"sampleRate":"24000/1001"}}},
             "frameBasedMetadata":{"frames":[{"Sensor State":{"sensorSampleRate":47.952}}]}}
            """);
        Assert.Equal(("24000/1001", "48000/1001"), (number.FrameRate, number.CaptureFrameRate));
        // Review F-4: in thousandths (35000 → 35, 23976 → 23.976), never "35000 fps".
        foreach (var (raw, expected) in new[] { ("35000", "35"), ("23976", "24000/1001"), ("\"35000/1000\"", "35") })
        {
            var milli = ArriBackend.ParseMetadata(
                """{"clipBasedMetadata":{"nativePictureEssenceDescriptor":{"file":{"sampleRate":"24/1"}}},"frameBasedMetadata":{"frames":[{"Sensor State":{"sensorSampleRate":"""
                + raw + "}}]}}");
            Assert.Equal(expected, milli.CaptureFrameRate);
        }
        Assert.Equal("23.976 fps · playback 24", new ClipInfo { FrameRate = "24", CaptureFrameRate = "24000/1001" }.FpsSummary);
        var none = ArriBackend.ParseMetadata("""{"clipBasedMetadata":{"nativePictureEssenceDescriptor":{"file":{"sampleRate":"25/1"}}}}""");
        Assert.Equal(("25", (string?)null), (none.FrameRate, none.CaptureFrameRate));
    }

    // --- RED: record fps (capture) vs. project / timebase fps (playback) ---

    [Fact]
    public void R3d_RecordVsProjectFps()
    {
        var c = R3dBackend.ParseMeta("Frame Width: 8192\nFrame Height: 4320\nRecord FPS: 120.000\nProject FPS: 24.000\nTotal Frames: 240\nAbs TC: 01:00:00:00\n");
        Assert.Equal(("24", "120"), (c.FrameRate, c.CaptureFrameRate));
        Assert.Equal(10.0, c.Duration);
        var timebase = R3dBackend.ParseMeta("Record FPS: 47.952\nTimebase: 23.976\nTotal Frames: 48\n");
        Assert.Equal(("24000/1001", "48000/1001"), (timebase.FrameRate, timebase.CaptureFrameRate));
        // Only the record rate known (an older REDline): the playback rate falls back to it, as before.
        var only = R3dBackend.ParseMeta("Record FPS: 23.976\nTotal Frames: 240\n");
        Assert.Equal(("24000/1001", "24000/1001"), (only.FrameRate, only.CaptureFrameRate));
        Assert.False(only.CaptureDiffers);
        var plain = R3dBackend.ParseMeta("FPS: 25.000\nTotal Frames: 25\n");
        Assert.Equal(("25", (string?)null), (plain.FrameRate, plain.CaptureFrameRate));
        // Review F-3: a generic name counts only as itself ("FPS", "Frame Rate"), never another key that merely contains
        // "fps" — without the project rate the record rate stays the playback rate, as before.
        var monitor = R3dBackend.ParseMeta("Monitor FPS: 60.000\nRecord FPS: 120.000\nTotal Frames: 240\n");
        Assert.Equal(("120", "120"), (monitor.FrameRate, monitor.CaptureFrameRate));
        var frameRate = R3dBackend.ParseMeta("Record FPS: 48.000\nFrame Rate: 24.000\nTotal Frames: 48\n");
        Assert.Equal(("24", "48"), (frameRate.FrameRate, frameRate.CaptureFrameRate));
    }

    // --- Blackmagic RAW: braw-decode --info "sensorRate" (IBlackmagicRawFrame::GetSensorRate of frame 0) ---

    [Fact]
    public void Braw_SensorRate_ReachesClipInfo()
    {
        var c = BrawBackend.ToClipInfo(BrawBackend.ParseInfo("""{"width":1,"height":1,"sourceWidth":1,"sourceHeight":1,"fps":24.000000,"frames":96,"timecode":"","camera":"","sensorRate":48.000000}"""));
        Assert.Equal(("24", "48"), (c.FrameRate, c.CaptureFrameRate));
        Assert.Equal(4.0, c.Duration);
        // The Mac decoder (and older builds) print no sensorRate; an SDK that says 0 does not know it.
        Assert.Null(BrawBackend.ToClipInfo(BrawBackend.ParseInfo("""{"width":1,"height":1,"sourceWidth":1,"sourceHeight":1,"fps":25,"frames":1,"timecode":"","camera":""}""")).CaptureFrameRate);
        Assert.Null(BrawBackend.ToClipInfo(BrawBackend.ParseInfo("""{"width":1,"height":1,"sourceWidth":1,"sourceHeight":1,"fps":25,"frames":1,"timecode":"","camera":"","sensorRate":0.000000}""")).CaptureFrameRate);
    }

    [Fact]
    public void BrawInfoJson_SensorRateLast_MacKeysUntouched()
    {
        var f = new ClipFacts(1, 2, 3, 4, 23.976f, 0, "", "", "Blackmagic Design Film", null, 5, SensorRate: 119.88f);
        Assert.Equal("""{"width":1,"height":2,"sourceWidth":3,"sourceHeight":4,"fps":23.976000,"frames":0,"timecode":"","camera":"","gamma":"Blackmagic Design Film","colorScienceGen":5,"sensorRate":119.879997}""" + "\n",
            InfoJson.Format(f));
        Assert.DoesNotContain("sensorRate", InfoJson.Format(f with { SensorRate = null }));
        Assert.Equal("120000/1001", BrawBackend.ToClipInfo(BrawBackend.ParseInfo(InfoJson.Format(f))).CaptureFrameRate);
    }

    /// A stand-in for the SDK's read job (GetUserData = the frame index, Release a no-op) and frame (format and scale
    /// accepted, GetSensorRate a fixed value, the decode job refused — no image is needed here).
    static unsafe class FakeRead
    {
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static uint Release(nint self) => 0;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int GetUserData(nint self, nint* value)
        {
            *value = ((nint*)self)[1];
            return Com.S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int SetValue(nint self, uint value) => Com.S_OK;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int GetSensorRate(nint self, float* value)
        {
            *value = BitConverter.Int32BitsToSingle((int)((nint*)self)[1]);
            return Com.S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int CreateJob(nint self, nint clipAttributes, nint frameAttributes, nint* job)
        {
            *job = 0;
            return Com.E_FAIL;
        }

        static nint Object(nint* vtbl, nint value)
        {
            var obj = (nint*)NativeMemory.Alloc(2, (nuint)sizeof(nint));
            obj[0] = (nint)vtbl;
            obj[1] = value;
            return (nint)obj;
        }

        public static nint Job(ulong index)
        {
            var v = (nint*)NativeMemory.AllocZeroed(7, (nuint)sizeof(nint));
            v[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
            v[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetUserData;
            return Object(v, (nint)index);
        }

        public static nint Frame(float sensorRate)
        {
            var v = (nint*)NativeMemory.AllocZeroed(15, (nuint)sizeof(nint));
            v[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
            v[9] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, int>)&SetValue;
            v[11] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, int>)&SetValue;
            v[13] = (nint)(delegate* unmanaged[Stdcall]<nint, float*, int>)&GetSensorRate;
            v[14] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)&CreateJob;
            return Object(v, BitConverter.SingleToInt32Bits(sensorRate));
        }

        public static void Free(nint obj)
        {
            NativeMemory.Free(*(void**)obj);
            NativeMemory.Free((void*)obj);
        }
    }

    [Fact]
    public void BrawFrameSink_SensorRateOfFrameZero()
    {
        using var stderr = new StringWriter();
        var sink = new FrameSink(Scale.Eighth, stderr);
        Assert.Null(sink.SensorRate);
        nint job3 = FakeRead.Job(3), frame3 = FakeRead.Frame(60f), job0 = FakeRead.Job(0), frame0 = FakeRead.Frame(48f), zero = FakeRead.Frame(0f), job0b = FakeRead.Job(0);
        try
        {
            sink.ReadComplete(job3, Com.S_OK, frame3);
            Assert.Null(sink.SensorRate);   // only the first frame counts
            sink.ReadComplete(job0b, Com.S_OK, zero);
            Assert.Null(sink.SensorRate);   // 0 = the SDK does not know it
            sink.ReadComplete(job0, Com.S_OK, frame0);
            Assert.Equal(48f, sink.SensorRate);
        }
        finally
        {
            foreach (var p in new[] { job3, frame3, job0, frame0, zero, job0b }) FakeRead.Free(p);
        }
    }

    // --- Sony XAVC: <VideoFrame captureFps="…" formatFps="…"/> in the clip's M01.XML (S&Q) ---

    static string SonyClip(TempDir dir, string? videoFrame)
    {
        var clip = Path.Combine(dir.Path, "C0001.MP4");
        File.WriteAllText(clip, "x");
        File.WriteAllText(Path.Combine(dir.Path, "C0001M01.XML"), $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <NonRealTimeMeta xmlns="urn:schemas-professionalDisc:nonRealTimeMeta:ver.2.10">
              <VideoFormat>{videoFrame}<VideoLayout pixel="3840" numOfVerticalLine="2160" aspectRatio="16:9"/></VideoFormat>
              <Device manufacturer="Sony" modelName="ILME-FX6V" serialNo="5012345"/>
            </NonRealTimeMeta>
            """);
        return clip;
    }

    [Theory]
    [InlineData("""<VideoFrame videoCodec="AVC_3840_2160_HP@L51" captureFps="119.88p" formatFps="23.98p"/>""", "120000/1001")]
    [InlineData("""<VideoFrame videoCodec="AVC_3840_2160_HP@L51" captureFps="25p" formatFps="25p"/>""", "25")]
    [InlineData("""<VideoFrame videoCodec="MPEG2HD422" captureFps="59.94i" formatFps="59.94i"/>""", "30000/1001")]
    [InlineData("""<VideoFrame videoCodec="AVC_3840_2160_HP@L51" formatFps="25p"/>""", null)]
    [InlineData("", null)]
    public void Sony_CaptureFps(string videoFrame, string? expected)
    {
        using var dir = new TempDir();
        Assert.Equal(expected, SonyXml.CaptureFrameRate(SonyClip(dir, videoFrame)));
    }

    [Fact]
    public void Sony_NoXml_Unknown()
    {
        using var dir = new TempDir();
        Assert.Null(SonyXml.CaptureFrameRate(Path.Combine(dir.Path, "C0009.MP4")));
    }

    [Fact]
    public void Video_Probe_TakesSonyCaptureFps_OthersStayUnknown()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        File.WriteAllText(Path.Combine(dir.Path, "probe.json"), """
            {"streams":[{"index":0,"codec_type":"video","codec_name":"h264","width":3840,"height":2160,"r_frame_rate":"24000/1001","avg_frame_rate":"24000/1001","duration":"10.0"}],
             "format":{"format_name":"mov","duration":"10.0"}}
            """);
        var ffprobe = Path.Combine(dir.Path, "ffprobe.cmd");
        File.WriteAllText(ffprobe, "@echo off\r\ntype \"%~dp0probe.json\"\r\nexit /b 0\r\n");
        using var r = new ProxyRenderer(new ProxySettings(), new ExternalTools { Ffprobe = ffprobe, NoSearch = true }.Resolved());
        File.WriteAllText(Path.Combine(card, "C0001.MP4"), "x");
        File.WriteAllText(Path.Combine(card, "C0001M01.XML"), """
            <NonRealTimeMeta xmlns="urn:schemas-professionalDisc:nonRealTimeMeta:ver.2.10">
              <VideoFormat><VideoFrame videoCodec="AVC" captureFps="119.88p" formatFps="23.98p"/></VideoFormat>
            </NonRealTimeMeta>
            """);
        File.WriteAllText(Path.Combine(card, "A001.MOV"), "x");
        static Clip V(string rel) => new() { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = 1 };

        var sony = r.Probe(V("C0001.MP4"), card);
        Assert.Equal(("24000/1001", "120000/1001"), (sony.FrameRate, sony.CaptureFrameRate));
        Assert.Equal("119.880 fps · playback 23.976", sony.FpsSummary);
        var other = r.Probe(V("A001.MOV"), card);
        Assert.Equal(("24000/1001", (string?)null), (other.FrameRate, other.CaptureFrameRate));
        Assert.Equal("23.976", other.FpsSummary);
    }

    // --- Where a clip's fps shows: HTML/PDF, TXT, CSV/ALE, day report ---

    static ClipEntry Entry(string name, string playback, string? capture, int frames) => new()
    {
        Clip = new Clip { Name = name, Path = $"CLIP/{name}.mxf", Format = ClipFormat.Arriraw, Segments = [$"CLIP/{name}.mxf"], Bytes = 1000 },
        Info = new ClipInfo
        {
            Codec = "ARRIRAW", Camera = "ALEXA 35", FrameRate = playback, CaptureFrameRate = capture, Frames = frames,
            Duration = frames / Timecode.Fps(playback)!.Value, Timecode = "10:00:00:00", Width = 4608, Height = 3164,
        },
    };

    static OffloadReport Report(string card)
    {
        var r = Reports.Sample(card);
        r.Clips = [Entry("A_0012C001", "24", "35", 240), Entry("A_0012C002", "24", "35", 120), Entry("A_0012C003", "25", null, 50)];
        return r;
    }

    [Fact]
    public void Html_CaptureAndPlayback_DurationIsTheEditingLength()
    {
        var html = HtmlReport.Render(Report(@"D:\dst\2026-10-10\A_0012"));
        // Review F-1: two lines in the narrow column — the capture rate, then the playback rate small and muted; the
        // cell may wrap (td.num never does).
        Assert.Contains("<td class=\"num fps\">35 fps<div class=\"note\">playback 24</div></td>", html);
        Assert.Contains("<td class=\"num fps\">25</td>", html);
        Assert.Contains("table.clips td.fps{white-space:normal}", html);
        Assert.Contains("<th class=\"num\">Duration</th>", html);
        Assert.DoesNotContain("<th class=\"num\">Length</th>", html);
        Assert.Contains("Duration = frame count ÷ playback fps", html);
        // Without a differing clip there is nothing to explain.
        var plain = Reports.Sample(@"D:\dst\2026-10-10\A001");
        plain.Clips = [Entry("C0001", "25", "25", 50)];
        Assert.DoesNotContain("Duration = frame count", HtmlReport.Render(plain));
    }

    [Fact]
    public void Txt_FrameRates()
    {
        var s = Report(@"D:\dst\2026-10-10\A_0012").Summary;
        Assert.Contains("Frame rate:  35 fps capture, 24 playback (2 clips); 25 fps (1 clip)\n", s);
        Assert.DoesNotContain("Frame rate:", Reports.Sample(@"D:\dst\2026-10-10\A001").Summary);
    }

    [Fact]
    public void ClipLists_CaptureFpsColumns()
    {
        var rows = Report(@"D:\dst\2026-10-10\A_0012").Clips
            .Select(c => new ClipList.Row("A_0012", c.Clip, c.Info, null, null, null)).ToList();
        var csv = ClipList.Csv(rows).Split("\r\n");
        Assert.StartsWith("Clip Name,File Name,Path,Card,Camera,Reel,Start TC,Duration,FPS,Capture FPS,Resolution,", csv[0]);
        Assert.Contains(",00:00:10:00,24,35,4608x3164,", csv[1]);   // the duration at the playback rate
        Assert.Contains(",00:00:02:00,25,25,4608x3164,", csv[3]);   // nothing better known: capture = playback

        var ales = ClipList.Ale(rows);
        var main = ales.Single(a => a.Suffix == "24").Text.Split("\r\n");
        Assert.Contains("FPS\t24", main);
        Assert.Equal("Name\tTape\tStart\tEnd\tTracks\tSource File\tCamera\tSensor_fps\tLUT\tResolution\tCodec\tXXH64\tMD5", main[Array.IndexOf(main, "Column") + 1]);
        Assert.Contains(main, l => l.StartsWith("A_0012C001\t", StringComparison.Ordinal) && l.Contains("\t10:00:10:00\t", StringComparison.Ordinal) && l.Contains("\tALEXA 35\t35\t", StringComparison.Ordinal));
    }

    [Fact]
    public void DayReport_FpsColumn()
    {
        using var dir = new TempDir();
        var day = dir.Sub("D", "2026-10-10");
        var card = Path.Combine(day, "A_0012");
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.json"), Report(card).ToJson());
        var html = DayReport.Html(DayReport.Load(day));
        Assert.Contains("<th class=\"num\">Duration</th><th>FPS</th>", html);
        Assert.Contains("<td class=\"wrap\">35 fps · playback 24, 25</td>", html);
        Assert.Contains("<span>total duration</span>", html);
    }
}
