using System.Text;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Spec 3.3: the clip list of a card — CSV (RFC 4180, UTF-8 with BOM, CRLF) and Avid ALE (tabs, one FPS per file).
public class ClipListTests
{
    const string Xx = "0123456789abcdef";

    static ClipList.Row Row(string name, ClipInfo? info = null, LutUse? lut = null, string? md5 = null, ClipFormat format = ClipFormat.Video) =>
        new("A001", new Clip { Name = name, Path = $"CLIP/{name}.mov", Format = format, Segments = [$"CLIP/{name}.mov"], Bytes = 1234 }, info, lut, Xx, md5);

    static readonly LutUse LogC4 = new("auto", "arri-logc4-awg4", "ARRI LogC4 → Rec.709", "ARRI");

    // Review focus 2: comma, quote, line break and Czech in values — quoted and doubled, nothing else changed.
    [Fact]
    public void Csv_EscapingAndColumns()
    {
        var a = Row("Záběr, 1", new ClipInfo
        {
            Camera = "ARRI \"ALEXA\" 35", Timecode = "01:00:00:00", FrameRate = "25/1", Duration = 12.4, Frames = 310,
            Width = 3840, Height = 2160, Codec = "prores 4444", AudioChannels = 2,
        }, LogC4, md5: "900150983cd24fb0d6963f7d28e17f72");
        var b = Row("B002C001_x", new ClipInfo { Timecode = "10:00:00:00", FrameRate = "24000/1001", Duration = 2.0 },
            new LutUse("custom", null, "look\nB", "vlastní"));
        var c = Row("-take", format: ClipFormat.Braw);

        var csv = ClipList.Csv([a, b, c]);

        Assert.Equal(
            "Clip Name,File Name,Path,Card,Camera,Reel,Start TC,Duration,FPS,Capture FPS,Resolution,Codec,LUT,Size (bytes),XXH64,MD5\r\n"
            + "\"Záběr, 1\",\"Záběr, 1.mov\",\"CLIP/Záběr, 1.mov\",A001,\"ARRI \"\"ALEXA\"\" 35\",A001,01:00:00:00,00:00:12:10,25,25,3840x2160,prores 4444,ARRI LogC4 → Rec.709,1234,0123456789abcdef,900150983cd24fb0d6963f7d28e17f72\r\n"
            + "B002C001_x,B002C001_x.mov,CLIP/B002C001_x.mov,A001,,B002,10:00:00:00,00:00:02:00,23.976,23.976,,,\"look\nB\",1234,0123456789abcdef,\r\n"
            + "-take,-take.mov,CLIP/-take.mov,A001,,A001,,,,,,Blackmagic RAW,,1234,0123456789abcdef,\r\n",
            csv);
    }

    // Review focus 1+2: a tab inside a value becomes a space; no TC = 00:00:00:00; tracks from the audio channels.
    [Fact]
    public void Ale_HeadingTabsTracks()
    {
        var a = Row("A001C003", new ClipInfo
        {
            Camera = "ARRI ALEXA 35", Timecode = "01:00:00:00", FrameRate = "25/1", Frames = 310, Width = 1920, Height = 1080,
            Codec = "prores 4444", AudioChannels = 2,
        }, LogC4);
        var d = Row("C0001", new ClipInfo { Camera = "Sony\tFX6", FrameRate = "25/1", Frames = 50, Width = 1920, Height = 1080, Codec = "h264 High" });

        var (suffix, text) = Assert.Single(ClipList.Ale([a, d]));

        Assert.Null(suffix);
        Assert.Equal(
            "Heading\r\nFIELD_DELIM\tTABS\r\nVIDEO_FORMAT\t1080\r\nAUDIO_FORMAT\t48khz\r\nFPS\t25\r\n\r\n"
            + "Column\r\nName\tTape\tStart\tEnd\tTracks\tSource File\tCamera\tSensor_fps\tLUT\tResolution\tCodec\tXXH64\tMD5\r\nData\r\n"
            + "A001C003\tA001\t01:00:00:00\t01:00:12:10\tVA1A2\tA001C003.mov\tARRI ALEXA 35\t25\tARRI LogC4 → Rec.709\t1920x1080\tprores 4444\t0123456789abcdef\t\r\n"
            + "C0001\tA001\t00:00:00:00\t00:00:02:00\tV\tC0001.mov\tSony FX6\t25\t\t1920x1080\th264 High\t0123456789abcdef\t\r\n",
            text);
    }

    // Review focus 1: mixed rates — one ALE per rate; a clip without a rate goes with the most common one.
    [Fact]
    public void Ale_MixedFps_OneFilePerRate()
    {
        var rows = new[]
        {
            Row("A", new ClipInfo { FrameRate = "24000/1001", Frames = 24 }),
            Row("B", new ClipInfo { FrameRate = "25/1", Frames = 25, Height = 2160 }),
            Row("C", new ClipInfo { FrameRate = "25/1", Frames = 25, Height = 2160 }),
            Row("D"),
        };

        var files = ClipList.Ale(rows);

        Assert.Equal(["23.976", "25"], files.Select(f => f.Suffix));
        Assert.Contains("FPS\t23.976\r\n", files[0].Text);
        Assert.Contains("\r\nA\t", files[0].Text);
        Assert.Contains("FPS\t25\r\n", files[1].Text);
        Assert.Contains("VIDEO_FORMAT\tCUSTOM\r\n", files[1].Text);
        Assert.Contains("\r\nB\t", files[1].Text);
        Assert.Contains("\r\nD\tA001\t00:00:00:00\t00:00:00:00\tV\t", files[1].Text);
        // Nothing analysed at all: one ALE, FPS 25 (Avid needs a rate).
        var (suffix, text) = Assert.Single(ClipList.Ale([Row("D")]));
        Assert.Null(suffix);
        Assert.Contains("FPS\t25\r\n", text);
    }

    // Review focus 1: drop-frame — End counts as the camera does; the CSV duration is nominal frames.
    [Fact]
    public void DropFrame_EndAndDuration()
    {
        var df = Row("A", new ClipInfo { FrameRate = "30000/1001", Timecode = "00:00:59;29", Frames = 2 });
        Assert.Contains("\tA001\t00:00:59;29\t00:01:00;03\t", Assert.Single(ClipList.Ale([df])).Text);
        Assert.Contains(",00:00:59;29,00:00:00:02,29.97,", ClipList.Csv([df]));
    }

    // R25: the files are rewritten whole; an ALE the rates no longer call for is removed; CSV has the BOM, ALE none.
    [Fact]
    public void Write_BomCrlf_StaleAleRemoved()
    {
        using var dir = new TempDir();
        var both = new OutputSettings { ClipListCsv = true, ClipListAle = true };
        File.WriteAllText(Path.Combine(dir.Path, "A001_clips_50.ale"), "old");
        File.WriteAllText(Path.Combine(dir.Path, "other_clips.ale"), "someone else's");

        ClipList.Write(dir.Path, "A001", [Row("A", new ClipInfo { FrameRate = "25/1", Frames = 25 })], both);

        var csv = File.ReadAllBytes(Path.Combine(dir.Path, "A001_clips.csv"));
        Assert.Equal(Encoding.UTF8.GetPreamble(), csv[..3]);
        Assert.EndsWith("\r\n", Encoding.UTF8.GetString(csv));
        var ale = File.ReadAllBytes(Path.Combine(dir.Path, "A001_clips.ale"));
        Assert.Equal((byte)'H', ale[0]);
        Assert.False(File.Exists(Path.Combine(dir.Path, "A001_clips_50.ale")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "other_clips.ale")));

        ClipList.Write(dir.Path, "B002", [Row("A")], new OutputSettings());   // defaults: CSV only (R13)
        Assert.True(File.Exists(Path.Combine(dir.Path, "B002_clips.csv")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "B002_clips.ale")));
    }


    // Task 1-2 review: a clip whose probe failed (Info == null) still gets a reel, and a list is made.
    [Fact]
    public void NoInfo_ReelFromNameOrCard()
    {
        var named = Row("A003C005_220101_R1AB");
        var plain = Row("interview");
        Assert.Equal("A003", named.Reel);
        Assert.Equal("A001", plain.Reel);
        Assert.Contains("\r\nA003C005_220101_R1AB,A003C005_220101_R1AB.mov,CLIP/A003C005_220101_R1AB.mov,A001,,A003,,,,,,,,1234,", ClipList.Csv([named, plain]));
        var ale = Assert.Single(ClipList.Ale([named, plain])).Text;
        Assert.Contains("\r\ninterview\tA001\t00:00:00:00\t00:00:00:00\tV\t", ale);
    }

    // Review focus 2: a line break and a tab in ALE values become spaces, Czech letters stay.
    [Fact]
    public void Ale_NewlineTabDiacritics()
    {
        var r = Row("Žluťoučký\r\nkůň\t1", new ClipInfo { Camera = "Kamera \"č\"", FrameRate = "25/1", Frames = 25 });
        var text = Assert.Single(ClipList.Ale([r])).Text;
        Assert.Contains("\r\nŽluťoučký kůň 1\tA001\t", text);
        Assert.Contains("\tKamera \"č\"\t", text);
    }

    // Ruling M5: a list that is switched off but already there is rewritten (derived, R25), not left stale and not deleted.
    [Fact]
    public void Write_SwitchedOffButExisting_RewrittenNotDeleted()
    {
        using var dir = new TempDir();
        var csvPath = Path.Combine(dir.Path, "A001_clips.csv");
        var alePath = Path.Combine(dir.Path, "A001_clips.ale");
        ClipList.Write(dir.Path, "A001", [Row("A")], new OutputSettings { ClipListCsv = true, ClipListAle = true });

        ClipList.Write(dir.Path, "A001", [Row("A"), Row("B")], new OutputSettings { ClipListCsv = false, ClipListAle = false });
        Assert.Contains("\r\nB\t", File.ReadAllText(alePath));
        Assert.Contains("\r\nB,", File.ReadAllText(csvPath));

        File.Delete(alePath);   // never switched on: nothing is created
        ClipList.Write(dir.Path, "A001", [Row("A")], new OutputSettings { ClipListCsv = false, ClipListAle = false });
        Assert.False(File.Exists(alePath));
        Assert.DoesNotContain("\r\nB,", File.ReadAllText(csvPath));
    }

    // Final fix 1: ALE switched off but already there, then a new frame rate arrives (append): every rate file is
    // written, and a single file made stale by the mix is removed; nothing is created when no ALE ever existed.
    [Fact]
    public void Write_AleOffButExisting_NewRateGetsItsFile_StaleSingleRemoved()
    {
        using var dir = new TempDir();
        var on = new OutputSettings { ClipListCsv = false, ClipListAle = true };
        var off = new OutputSettings { ClipListCsv = false, ClipListAle = false };
        var r24 = Row("A", new ClipInfo { FrameRate = "24000/1001", Frames = 24 });
        var r25 = Row("B", new ClipInfo { FrameRate = "25/1", Frames = 25 });
        ClipList.Write(dir.Path, "A001", [r24], on);
        Assert.True(File.Exists(Path.Combine(dir.Path, "A001_clips.ale")));

        ClipList.Write(dir.Path, "A001", [r24, r25], off);

        Assert.True(File.Exists(Path.Combine(dir.Path, "A001_clips_23.976.ale")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "A001_clips_25.ale")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "A001_clips.ale")));

        using var none = new TempDir();
        ClipList.Write(none.Path, "A001", [r24, r25], off);
        Assert.Empty(Directory.EnumerateFiles(none.Path, "*.ale"));
    }

    // Task 6 review: each list is written on its own — a CSV held open in Excel (no delete sharing) does not stop the
    // ALE or the stale-ALE cleanup; the first error is thrown afterwards (the caller logs it), the CSV stays as it was.
    [Fact]
    public void Write_CsvHeldOpen_AleStillWritten_ErrorThrownAfter()
    {
        using var dir = new TempDir();
        var both = new OutputSettings { ClipListCsv = true, ClipListAle = true };
        var csvPath = Path.Combine(dir.Path, "A001_clips.csv");
        var alePath = Path.Combine(dir.Path, "A001_clips.ale");
        ClipList.Write(dir.Path, "A001", [Row("A", new ClipInfo { FrameRate = "25/1", Frames = 25 })], both);
        var csvBefore = File.ReadAllBytes(csvPath);
        File.WriteAllText(Path.Combine(dir.Path, "A001_clips_50.ale"), "stale");

        using (new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var e = Record.Exception(() =>
                ClipList.Write(dir.Path, "A001", [Row("A", new ClipInfo { FrameRate = "25/1", Frames = 25 }), Row("B", new ClipInfo { FrameRate = "25/1", Frames = 25 })], both));
            Assert.True(e is IOException or UnauthorizedAccessException, e?.ToString());
        }

        Assert.Contains("\r\nB\t", File.ReadAllText(alePath));
        Assert.False(File.Exists(Path.Combine(dir.Path, "A001_clips_50.ale")));
        Assert.Equal(csvBefore, File.ReadAllBytes(csvPath));
        Assert.Empty(Directory.GetFiles(dir.Path, ".*"));
    }

    static OffloadJob Job(string card, IReadOnlyList<string> dests, string history, OutputSettings outputs, IReadOnlyList<string>? appendInto = null) =>
        new(new JobConfig(card, dests) { Date = "2026-10-07", AnalyzeClips = false, HistoryFolder = history, Outputs = outputs, AppendInto = appendInto },
            AppendFixtures.Options());

    // Spec 3.3: without clip analysis the rows come from the files (metadata empty); an append lists every generation.
    [Fact]
    public void Job_ListsFromTheFiles_AppendCoversEveryGeneration()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var outputs = new OutputSettings { ClipListCsv = true, ClipListAle = true };
        var full = Job(card, [dir.Sub("d1"), dir.Sub("d2")], history, outputs);
        var report = full.Run()!;
        var paths = full.Plan!.CardPaths;

        foreach (var p in paths)
        {
            var csv = File.ReadAllText(Path.Combine(p, "_REPORT", "A001_clips.csv"));
            Assert.Contains($"A001C001,A001C001.mov,CLIP/A001C001.mov,A001,,A001,,,,,,,,10000,{report.Files[0].Xxh64},\r\n", csv);
            Assert.True(File.Exists(Path.Combine(p, "_REPORT", "A001_clips.ale")));
        }

        AppendFixtures.NewClip(card);
        var append = Job(card, [], history, outputs, appendInto: paths);
        append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        foreach (var p in paths)
        {
            var lines = File.ReadAllLines(Path.Combine(p, "_REPORT", "A001_clips.csv"));
            Assert.Equal(["CLIP/A001C001.mov", "CLIP/A001C003.mov"], lines.Skip(1).Select(l => l.Split(',')[2]));
        }
    }

    // A list held open (Excel locks a CSV it shows) is not rewritten — the job is still Done, the log says why.
    [Fact]
    public void Job_ListHeldOpen_StillDone_Logged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history, new OutputSettings());
        full.Run();
        var csvPath = Path.Combine(full.Plan!.CardPaths[0], "_REPORT", "A001_clips.csv");
        var before = File.ReadAllBytes(csvPath);
        AppendFixtures.NewClip(card);
        var log = new List<string>();

        using (new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var append = Job(card, [], history, new OutputSettings(), appendInto: full.Plan.CardPaths);
            append.Run(m => { lock (log) log.Add(m); });
            Assert.Equal(JobPhase.Done, append.Status().Phase);
            Assert.True(append.Status().SafeToEject);
        }

        Assert.Contains(log, m => m.StartsWith("clip list ", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(csvPath));
    }
}
