namespace Offload.Core.Tests;

public class CardRecordTests
{
    [Fact]
    public void Generations_Merged_NewestWins()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var full = Reports.Sample(card);
        full.Write(card, includeHashRecords: false);
        var append = Reports.Sample(card);
        append.Generation = 2;
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), append.ToJson());

        var r = CardRecord.Load(card);

        Assert.Null(r.Error);
        Assert.False(r.ReportMissing);
        Assert.Equal([1, 2], r.Generations.Select(g => g.Generation));
        Assert.Equal(2, r.Files["CLIP/A&B.mov"].Generation);
        Assert.Equal(0, CardRecord.DestinationIndex(full, card, 5));
    }

    [Fact]
    public void Missing_Broken_AndABrokenAppendReport()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Assert.True(CardRecord.Load(card).ReportMissing);
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.json"), "{broken");
        Assert.NotNull(CardRecord.Load(card).Error);

        Reports.Sample(card).Write(card, includeHashRecords: false);
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), "{broken");
        var r = CardRecord.Load(card);
        Assert.Null(r.Error);
        Assert.Single(r.Generations);
        Assert.StartsWith("offload_report_0002.json: ", Assert.Single(r.Problems));
    }

    [Fact]
    public void DestinationIndex_ByPath_OnlyOne_OrPosition()
    {
        var two = Reports.Sample(@"D:\Zaloha\2026-10-04\A001");
        Assert.Equal(0, CardRecord.DestinationIndex(two, @"d:\zaloha\2026-10-04\A001\", 3));   // the only destination
    }

    // Ruling I1: every "_NNNN" report file counts, also one without its JSON (an append that stopped half way).
    [Fact]
    public void HighestReportNumber_CountsEveryNumberedReportFile()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);
        Assert.Equal(1, CardRecord.Load(card).HighestReportNumber);

        File.WriteAllText(Path.Combine(card, "_REPORT", "A001_report_0003.html"), "<html/>");
        Assert.Equal(3, CardRecord.Load(card).HighestReportNumber);
    }

    static ClipEntry Entry(string path, ProxyStatus proxy = ProxyStatus.None) => new()
    {
        Clip = new Clip { Name = Path.GetFileNameWithoutExtension(path), Path = path, Segments = [path], Bytes = 1000 },
        ProxyStatus = proxy,
    };

    // Spec 3.1: the clips of every generation, an append's after the earlier ones; a path listed again is the newest.
    [Fact]
    public void Clips_EveryGeneration_NewestEntryWins()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var full = Reports.Sample(card);
        full.Clips = [Entry("CLIP/A&B.mov", ProxyStatus.Failed), Entry("CLIP/c 02.mov")];
        full.Write(card, includeHashRecords: false);
        var append = Reports.Sample(card);
        append.Generation = 2;
        append.Clips = [Entry("CLIP/A&B.mov", ProxyStatus.Done), Entry("CLIP/d 03.mov")];
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), append.ToJson());

        var r = CardRecord.Load(card);

        Assert.Equal(["CLIP/A&B.mov", "CLIP/c 02.mov", "CLIP/d 03.mov"], r.Clips.Select(c => c.Entry.Clip.Path));
        Assert.Equal([2, 1, 2], r.Clips.Select(c => c.Generation));
        Assert.Equal(ProxyStatus.Done, r.Clips[0].Entry.ProxyStatus);
        Assert.Equal("A001", r.FolderName);
    }

    // Spec 3.1: totals over the generations; a file whose newest record is good is not failed any more.
    [Fact]
    public void Totals_FailedAndVerified_AcrossGenerations()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Reports.Sample(card, destError: "write failed").Write(card, includeHashRecords: false);
        var once = CardRecord.Load(card);
        Assert.Equal(["CLIP/c 02.mov"], once.FailedFiles);
        Assert.True(once.Verified);

        // The append copied the failed file again (it was not on the disk), but did not read it back.
        var t1 = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var append = new OffloadReport
        {
            CardName = "A001", Date = "2026-10-04", Source = @"E:\", SourceFilesystem = "exFAT",
            Started = t1, CopyFinished = t1.AddSeconds(5), Finished = t1.AddSeconds(9), Verified = false, Generation = 2,
            TotalFiles = 2, TotalBytes = 2500,
            Destinations = once.Generations[0].Report.Destinations,
            Files =
            [
                new ReportFile { Path = "CLIP/c 02.mov", Size = 2000, Modified = t1, Xxh64 = "fedcba9876543210", Destinations = ["written"], Errors = [null] },
                new ReportFile { Path = "CLIP/e 05.mov", Size = 500, Modified = t1, Xxh64 = "00000000000000aa", Destinations = ["written"], Errors = [null] },
            ],
            Skipped = [],
        };
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), append.ToJson());

        var r = CardRecord.Load(card);

        Assert.Empty(r.FailedFiles);
        Assert.False(r.Verified);
        Assert.Equal(3, r.TotalFiles);
        Assert.Equal(1000 + 2000 + 500, r.TotalBytes);
        Assert.Equal(new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc), r.Started);
        Assert.Equal(t1.AddSeconds(9), r.Finished);
    }

    // Spec 3.1: a report the Mac wrote — no "generation", "lut", "proxyEncoder", "md5", clips with fewer info keys.
    [Fact]
    public void MacReport_WithClips_Reads()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-06", "A001");
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.json"), """
            {
              "cardName" : "A001", "copyFinished" : "2026-10-06T07:10:00Z", "date" : "2026-10-06",
              "clips" : [ { "clip" : { "bytes" : 1000, "format" : "video", "isCameraProxy" : false, "name" : "A001C001", "path" : "CLIP/A001C001.mov", "segments" : [ "CLIP/A001C001.mov" ] },
                            "info" : { "audioChannels" : 2, "camera" : "ARRI ALEXA 35", "codec" : "prores", "duration" : 12.4, "frameRate" : "25/1", "height" : 2160, "timecode" : "01:00:00:00", "width" : 3840 },
                            "proxyPath" : "_PROXY/A001C001.mov", "proxyStatus" : "done" },
                          { "clip" : { "bytes" : 10, "format" : "video", "isCameraProxy" : false, "name" : "broken", "path" : "broken.mov", "segments" : [ "broken.mov" ] },
                            "probeError" : "moov atom not found", "proxyStatus" : "failed" } ],
              "destinations" : [ { "cardPath" : "/Volumes/Zaloha/2026-10-06/A001", "filesFailed" : 0, "filesOK" : 2, "filesystem" : "apfs", "root" : "/Volumes/Zaloha" } ],
              "files" : [ { "destinations" : [ "verified" ], "errors" : [ null ], "modified" : "2026-10-06T07:00:00Z", "path" : "CLIP/A001C001.mov", "size" : 1000, "xxh64" : "0123456789abcdef" },
                          { "destinations" : [ "verified" ], "errors" : [ null ], "modified" : "2026-10-06T07:00:00Z", "path" : "broken.mov", "size" : 10, "xxh64" : "0123456789abcdee" } ],
              "finished" : "2026-10-06T07:11:00Z", "hashAlgorithm" : "xxh64", "skipped" : [ ], "source" : "/Volumes/A001", "sourceFilesystem" : "exfat",
              "started" : "2026-10-06T07:09:00Z", "tool" : "offload", "totalBytes" : 1010, "totalFiles" : 2, "verified" : true, "version" : "0.1.0"
            }
            """);

        var r = CardRecord.Load(card);

        Assert.Null(r.Error);
        Assert.Equal(1, Assert.Single(r.Generations).Generation);
        Assert.True(r.Verified);
        Assert.Equal(["A001C001", "broken"], r.Clips.Select(c => c.Entry.Clip.Name));
        var info = r.Clips[0].Entry.Info!;
        Assert.Equal(("ARRI ALEXA 35", "01:00:00:00", 2), (info.Camera, info.Timecode, info.AudioChannels));
        Assert.Null(r.Clips[0].Entry.Lut);
        Assert.Null(r.Clips[1].Entry.Info);
    }

    // A null "clips" (hand-edited, an older writer) is no clips; a null clip entry is dropped, the card stays.
    [Fact]
    public void NullClips_AndANullEntry_AreTolerated()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);
        var json = Path.Combine(card, "_REPORT", "offload_report.json");
        File.WriteAllText(json, File.ReadAllText(json).Replace("\"clips\": []", "\"clips\": null"));
        Assert.Equal(2, CardRecord.Load(card).Clips.Count);   // none recorded: found in the files
        File.WriteAllText(json, File.ReadAllText(json).Replace("\"clips\": null", "\"clips\": [ null ]"));
        var r = CardRecord.Load(card);
        Assert.Null(r.Error);
        Assert.Equal(2, r.Clips.Count);
        Assert.Equal(2, r.TotalFiles);
    }

    // Spec 3.3: a copy without clip analysis still has clips — found in its files, without metadata.
    [Fact]
    public void Clips_WithoutAnalysis_DetectedFromTheFiles()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);   // two .mov files, no clips in the report

        var r = CardRecord.Load(card);

        Assert.Equal(["CLIP/A&B.mov", "CLIP/c 02.mov"], r.Clips.Select(c => c.Entry.Clip.Path));
        Assert.All(r.Clips, c => Assert.Null(c.Entry.Info));
        Assert.Equal([1000L, 2000L], r.Clips.Select(c => c.Entry.Clip.Bytes));
    }

    // An append report nobody can read: what it recorded is unknown — never "ověřeno".
    [Fact]
    public void UnreadableAppendReport_IsNotVerified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);
        Assert.True(CardRecord.Load(card).Verified);
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), "{broken");
        Assert.False(CardRecord.Load(card).Verified);
    }
}
