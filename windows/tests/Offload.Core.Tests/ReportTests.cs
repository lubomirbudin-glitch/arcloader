using System.Text.Json;

namespace Offload.Core.Tests;

public class ReportTests
{
    const string Card = @"D:\dst\2026-10-04\A001";

    [Fact]
    public void Json_TopLevelKeysAreTheMacKeysSorted()
    {
        using var doc = JsonDocument.Parse(Reports.Sample(Card).ToJson());
        string[] expected =
        [
            "cardName", "clips", "copyFinished", "date", "destinations", "files", "finished", "hashAlgorithm",
            "skipped", "source", "sourceFilesystem", "started", "tool", "totalBytes", "totalFiles", "verified", "version",
        ];
        Assert.Equal(expected, doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Json_NestedShapes()
    {
        using var doc = JsonDocument.Parse(Reports.Sample(Card, destError: "boom", unbuffered: false).ToJson());
        var root = doc.RootElement;
        Assert.Equal("2026-10-04T08:00:00Z", root.GetProperty("started").GetString());
        Assert.Equal("offload", root.GetProperty("tool").GetString());
        Assert.Equal("0.1.0-win", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("clips").GetArrayLength());
        Assert.False(root.TryGetProperty("proxy", out _));

        var dest = root.GetProperty("destinations")[0];
        Assert.Equal(["cardPath", "filesFailed", "filesOK", "filesystem", "root", "unbuffered"], dest.EnumerateObject().Select(p => p.Name));
        Assert.False(dest.GetProperty("unbuffered").GetBoolean());

        var f0 = root.GetProperty("files")[0];
        Assert.Equal(["destinations", "errors", "modified", "path", "size", "xxh64"], f0.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, f0.GetProperty("errors")[0].ValueKind);
        Assert.Equal("CLIP/A&B.mov", f0.GetProperty("path").GetString());
        Assert.Equal("boom", root.GetProperty("files")[1].GetProperty("errors")[0].GetString());
    }

    [Fact]
    public void Json_UnbufferedOmittedWhenNormal()
    {
        using var doc = JsonDocument.Parse(Reports.Sample(Card).ToJson());
        Assert.False(doc.RootElement.GetProperty("destinations")[0].TryGetProperty("unbuffered", out _));
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var back = OffloadReport.FromJson(Reports.Sample(Card).ToJson());
        Assert.Equal("A001", back.CardName);
        Assert.Equal(2, back.Files.Count);
        Assert.Equal(new DateTime(2026, 10, 4, 8, 0, 10, DateTimeKind.Utc), back.CopyFinished);
    }

    [Fact]
    public void SuccessAndEject()
    {
        Assert.True(Reports.Sample(Card).SafeToEject);
        var notVerified = Reports.Sample(Card, verified: false);
        Assert.True(notVerified.Success);
        Assert.False(notVerified.SafeToEject);
        var failed = Reports.Sample(Card, destError: "boom");
        Assert.False(failed.Success);
        Assert.Equal(["CLIP/c 02.mov"], failed.FailedFiles.Select(f => f.Path));
    }

    [Fact]
    public void HashList_IsHashTwoSpacesPath()
    {
        Assert.Equal("0123456789abcdef  CLIP/A&B.mov\nfedcba9876543210  CLIP/c 02.mov\n", Reports.Sample(Card).HashList);
    }

    [Fact]
    public void Summary_ShowsStatusTimesAndFailures()
    {
        var ok = Reports.Sample(Card).Summary;
        Assert.StartsWith("OFFLOAD REPORT — OK\nCard:        A001\nSource:      E:\\ [exFAT]\n", ok);
        Assert.Contains("Copy:        10.0 s  0 MB/s\n", ok);
        Assert.Contains($"Destination: {Card} [NTFS]  ok 2  failed 0\n", ok);
        Assert.Contains("Skipped (system files): .DS_Store", ok);

        Assert.Contains("NOT VERIFIED", Reports.Sample(Card, verified: false).Summary);

        var bad = Reports.Sample(Card, destError: "boom", unbuffered: false).Summary;
        Assert.StartsWith("OFFLOAD REPORT — FAILED", bad);
        Assert.Contains("(buffered I/O)", bad);
        Assert.Contains("FAILED FILES (1):\n  CLIP/c 02.mov\n    dest 1: boom\n", bad);
    }

    [Fact]
    public void Build_TakesStatesFromWriterResults()
    {
        var plan = new OffloadPlan
        {
            Source = @"C:\card", Date = "2026-10-04", CardName = "A001", DestRoots = [@"C:\d1", @"C:\d2"],
            Scan = new SourceScan
            {
                Root = @"C:\card",
                Files = [new SourceFile("a", 1, DateTime.UtcNow), new SourceFile("b", 2, DateTime.UtcNow)],
                Dirs = [], Skipped = [],
            },
        };
        var r = OffloadReport.Build(plan, [1UL, null], [null, "read failed"],
            [
                [new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Failed, "source: read failed")],
                [new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Failed, "source: read failed")],
            ],
            [true, false], verified: true, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);
        Assert.Equal("0000000000000001", r.Files[0].Xxh64);
        Assert.Null(r.Files[1].Xxh64);
        Assert.Equal(["verified", "verified"], r.Files[0].Destinations);
        Assert.Equal(1, r.Destinations[1].FilesOK);
        Assert.Equal(1, r.Destinations[1].FilesFailed);
        Assert.Null(r.Destinations[0].Unbuffered);
        Assert.False(r.Destinations[1].Unbuffered);
        Assert.Equal(3, r.TotalBytes);
        Assert.False(r.Success);
    }
}
