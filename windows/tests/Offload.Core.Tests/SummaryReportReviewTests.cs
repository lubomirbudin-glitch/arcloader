using System.Diagnostics;
using Offload.Core.History;
using Offload.Core.Outputs;
using static Offload.Core.Tests.SummaryReportTests;

namespace Offload.Core.Tests;

/// The review of the summary report (S-1 … S-10): never into any backup's card folder or onto a card, one card once in
/// the totals, no foreign local copy, unreadable reports fall through, implausible record times, 200 cards.
public class SummaryReportReviewTests
{
    static SummaryModel Model(TempDir dir, string card) => SummaryReport.Load([SummaryInput.Of(card)], [dir.Sub("local")]);

    static string Refused(string target, IEnumerable<string>? cards = null) =>
        Assert.ThrowsAny<Exception>(() => SummaryReport.CheckTarget(target, cards)).Message;

    // S-1: a card folder that is still being copied or was stopped — an empty `_REPORT`, a part file, a manifest alone.
    [Fact]
    public void CheckTarget_AnyBackupCardFolder_EvenUnfinished_IsRefused()
    {
        using var dir = new TempDir();
        var day = dir.Sub("d1", "2026-10-06");
        var empty = dir.Sub("d1", "2026-10-06", "A002", "_REPORT");                       // a copy that just started
        var manifest = dir.Sub("d1", "2026-10-06", "A003", "_REPORT");
        System.IO.File.WriteAllText(Path.Combine(manifest, "offload-manifest.jsonl"), "{}");
        var part = dir.Sub("d1", "2026-10-06", "A004", "CLIP");                           // nothing but a part file yet
        System.IO.File.WriteAllText(Path.Combine(part, "A004C001.mxf.offload-part"), "x");
        var mhl = dir.Sub("x", "B001", "ascmhl");
        var under = dir.Sub("x", "B001", "CLIP", "deep");

        foreach (var target in new[] { Path.GetDirectoryName(empty)!, empty, Path.GetDirectoryName(manifest)!, part, Path.GetDirectoryName(part)!, mhl, under })
            Assert.Contains("ve složce zálohy karty", Refused(target));
        SummaryReport.CheckTarget(day);              // the day folder itself holds no card's files
        SummaryReport.CheckTarget(dir.Sub("picked"));
    }

    // S-2: the card protection is in Core — the CLI's -o gets it too.
    [Fact]
    public void CheckTarget_CameraCard_Removable_ReadOnly_Source_AreRefused()
    {
        using var dir = new TempDir();
        var camera = dir.Sub("camera");
        Directory.CreateDirectory(Path.Combine(camera, "DCIM"));
        var into = dir.Sub("camera", "export");
        using (SummaryReport.OverrideVolume(_ => new SummaryVolume(camera, DriveType.Fixed, false)))
            Assert.Contains("karta z kamery", Refused(into));

        var plain = dir.Sub("plain");
        using (SummaryReport.OverrideVolume(_ => new SummaryVolume(plain, DriveType.Removable, false)))
            Assert.Contains("výměnném", Refused(plain));
        using (SummaryReport.OverrideVolume(_ => new SummaryVolume(plain, DriveType.Fixed, true)))
            Assert.Contains("jen pro čtení", Refused(plain));
        using (SummaryReport.OverrideVolume(_ => new SummaryVolume(plain, DriveType.Fixed, false)))
        {
            SummaryReport.CheckTarget(plain);
            Assert.Contains("na kartě", Refused(dir.Sub("plain", "sub"), [plain]));
        }

        // A backup disk: camera folders below its dated backups never make it a card.
        var backup = dir.Sub("backup");
        Backup(backup, "2026-10-06", "A001", [Clip("A001C001", 10)]);
        Directory.CreateDirectory(Path.Combine(backup, "PRIVATE"));
        using (SummaryReport.OverrideVolume(_ => new SummaryVolume(backup, DriveType.Fixed, false)))
            SummaryReport.CheckTarget(backup);
    }

    [Fact]
    public void Save_OntoACameraCard_ExtraErrorOnly_NothingWrittenThere()
    {
        using var dir = new TempDir();
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 60)]);
        var camera = dir.Sub("camera");
        Directory.CreateDirectory(Path.Combine(camera, "PRIVATE", "M4ROOT"));
        using var _ = SummaryReport.OverrideVolume(_ => new SummaryVolume(camera, DriveType.Fixed, false));

        var saved = SummaryReport.Save(Model(dir, d1), dir.Sub("local"), camera, null, new DateTime(2026, 10, 10, 9, 0, 0));

        Assert.Contains("karta z kamery", saved.ExtraError);
        Assert.Empty(Directory.GetFiles(camera, "*", SearchOption.AllDirectories));
    }

    // S-3: the same card backed up twice (two jobs, two disks) is one card; every copy is listed.
    [Fact]
    public void SameCard_TwoBackups_OneCardInTheTotals_EveryCopyListed()
    {
        using var dir = new TempDir();
        var clips = new[] { Clip("A001C001", 60), Clip("A001C002", 30) };
        var raid = Backup(dir.Sub("raid"), "2026-10-06", "A001", clips);
        var shuttle = Backup(dir.Sub("shuttle"), "2026-10-07", "A001", clips, verified: false);   // the next morning, not verified
        var other = Backup(dir.Sub("raid"), "2026-10-06", "B001", [Clip("B001C001", 10)], files: 3);
        var first = Entry("A001", "2026-10-06", raid);
        var second = Entry("A001", "2026-10-07", shuttle);
        second.Id = HistoryStore.NewId(Utc(7, 9));

        var model = SummaryReport.Load([SummaryInput.Of(first), SummaryInput.Of(second), SummaryInput.Of(other)], [dir.Sub("local")]);

        Assert.Equal(["A001", "B001"], model.Cards.Select(c => c.Name));
        var a = model.Cards[0];
        Assert.Equal([raid, shuttle], a.Copies.Select(c => c.CardPath));
        Assert.Equal([true, false], a.Copies.Select(c => c.Verified));
        Assert.False(a.Verified);                      // not every copy is verified
        Assert.Equal(3, model.Total.ClipCount);
        Assert.Equal(5, model.Total.Files);
        Assert.Equal(5000, model.Total.Bytes);
        Assert.Equal(100, model.Total.Seconds);
        var html = SummaryReport.Html(model);
        Assert.Contains(raid, html);
        Assert.Contains(shuttle, html);
    }

    [Fact]
    public void SameCard_AFolderAndAnEntryWithoutAReport_OneCard()
    {
        using var dir = new TempDir();
        var raid = Backup(dir.Sub("raid"), "2026-10-06", "A001", [Clip("A001C001", 60)]);
        var gone = Path.Combine(dir.Path, "shuttle", "2026-10-06", "A001");
        var entry = Entry("A001", "2026-10-06", gone);
        entry.Shape = SummaryReport.ShapeOf(CardRecord.Load(raid))!;   // history knows the card by its shape

        var model = SummaryReport.Load([SummaryInput.Of(raid), SummaryInput.Of(entry)], [dir.Sub("local")]);

        var card = Assert.Single(model.Cards);
        Assert.Equal(SummarySource.Destination, card.Source);
        Assert.Equal(2, card.Files);
        Assert.Equal([raid, gone], card.Copies.Select(c => c.CardPath));
        Assert.Equal([true, false], card.Copies.Select(c => c.Connected));
    }

    [Fact]
    public void ShapeOf_TheHistorysShape()
    {
        using var dir = new TempDir();
        var raid = Backup(dir.Sub("raid"), "2026-10-06", "A001", [Clip("A001C001", 60)]);
        var record = CardRecord.Load(raid);
        var expected = CardFingerprint.Compute(record.Files.Values.Select(f => new SourceFile(f.File.Path, f.File.Size, f.File.Modified))).Shape;
        Assert.Equal(expected, SummaryReport.ShapeOf(record));
    }

    // S-4: two local copies fit the card and the source, neither by its end: neither is taken.
    [Fact]
    public void LocalCopy_Ambiguous_IsNotFound_NeverAForeignCard()
    {
        using var dir = new TempDir();
        var local = dir.Sub("local");
        var one = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 60)], day: 6);
        var two = Backup(dir.Sub("d2"), "2026-10-06", "A001", [Clip("A001C009", 60)], day: 7);
        LocalReports.MirrorCard([one], local);
        LocalReports.MirrorCard([two], local);
        Directory.Delete(dir.Sub("d1"), true);
        Directory.Delete(dir.Sub("d2"), true);

        var unknown = Entry("A001", "2026-10-06", one);
        unknown.Finished = Utc(9, 1);
        unknown.Generations[0].Finished = Utc(9, 1);
        var card = Assert.Single(SummaryReport.Load([SummaryInput.Of(unknown)], [local]).Cards);
        Assert.Equal(SummarySource.None, card.Source);
        Assert.Null(card.LocalCopy);

        var known = Entry("A001", "2026-10-06", two);   // ends when the second copy did
        known.Finished = Utc(7, 8, 15);
        known.Generations[0].Finished = Utc(7, 8, 15);
        var found = Assert.Single(SummaryReport.Load([SummaryInput.Of(known)], [local]).Cards);
        Assert.Equal(SummarySource.LocalCopy, found.Source);
        Assert.Equal("A001C009", found.First!.Clip);
    }

    // S-5: an unreadable report on the disk falls through to the local copy, then to "not available".
    [Fact]
    public void UnreadableReportOnTheDisk_LocalCopy_ThenNotAvailable()
    {
        using var dir = new TempDir();
        var local = dir.Sub("local");
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 60)]);
        LocalReports.MirrorCard([d1], local);
        System.IO.File.WriteAllText(Path.Combine(d1, "_REPORT", "offload_report.json"), "{broken");
        var entry = Entry("A001", "2026-10-06", d1);

        var card = Assert.Single(SummaryReport.Load([SummaryInput.Of(entry)], [local]).Cards);
        Assert.Equal(SummarySource.LocalCopy, card.Source);
        Assert.Equal(1, card.ClipCount);

        var none = Assert.Single(SummaryReport.Load([SummaryInput.Of(entry)], [dir.Sub("empty")]).Cards);
        Assert.Equal(SummarySource.None, none.Source);
        Assert.Equal(7, none.Files);   // history
        Assert.Contains(SummaryReport.Notes(new SummaryModel { Cards = [none] }), n => n.Text.Contains("nejde přečíst"));
    }

    [Fact]
    public void UnreadableReport_OnOneDisk_TheOtherDiskIsRead()
    {
        using var dir = new TempDir();
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 60)]);
        var d2 = Backup(dir.Sub("d2"), "2026-10-06", "A001", [Clip("A001C001", 60)]);
        System.IO.File.WriteAllText(Path.Combine(d1, "_REPORT", "offload_report.json"), "{broken");

        var card = Assert.Single(SummaryReport.Load([SummaryInput.Of(Entry("A001", "2026-10-06", d1, d2))], [dir.Sub("local")]).Cards);
        Assert.Equal(SummarySource.Destination, card.Source);
        Assert.Equal(d2, card.ReadFrom);
    }

    // S-7: a camera without its clock set writes 1904 or 1970 — unknown, never the first clapper.
    [Fact]
    public void Recorded_ZeroEpochsAndTheFuture_AreUnknown()
    {
        foreach (var t in new[] { "1904-01-01T00:00:00.000000Z", "1970-01-01T00:00:00.000000Z", "1999-12-31T23:00:00Z", DateTime.UtcNow.AddDays(3).ToString("o") })
            Assert.Null(FFProbe.Parse("{\"streams\":[],\"format\":{\"tags\":{\"creation_time\":\"" + t + "\"}}}").Recorded);
        Assert.NotNull(FFProbe.Parse("""{"streams":[],"format":{"tags":{"creation_time":"2026-10-06T08:00:00Z"}}}""").Recorded);

        using var dir = new TempDir();
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001",
        [
            Clip("A001C002", 10, tc: "09:00:00:00", recorded: new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Clip("A001C001", 10, tc: "08:00:00:00", recorded: Utc(6, 12)),
        ]);
        var card = Assert.Single(Model(dir, d1).Cards);
        Assert.Equal("A001C001", card.First!.Clip);   // the 1970 time is unknown: by the clip number
    }

    // S-8: a "recorded" this version cannot read (a number, another shape) is unknown — the report stays readable.
    [Fact]
    public void Recorded_Unreadable_IsNull_TheReportLoads()
    {
        using var dir = new TempDir();
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 10, recorded: Utc(6, 9))]);
        var json = Path.Combine(d1, "_REPORT", "offload_report.json");
        System.IO.File.WriteAllText(json, System.IO.File.ReadAllText(json).Replace("\"recorded\": \"2026-10-06T09:00:00Z\"", "\"recorded\": 781344000"));
        Assert.Contains("781344000", System.IO.File.ReadAllText(json));

        var record = CardRecord.Load(d1);
        Assert.Null(record.Error);
        Assert.Null(Assert.Single(record.Clips).Entry.Info!.Recorded);
    }

    // S-9: 200 cards of 40 clips — read, ordered and rendered in seconds.
    [Fact]
    public void TwoHundredCards_InSeconds()
    {
        using var dir = new TempDir();
        var inputs = new List<SummaryInput>();
        for (int i = 0; i < 200; i++)
        {
            var name = $"A{i:000}";
            var clips = Enumerable.Range(1, 40).Select(n => Clip($"{name}C{n:000}", 30, recorded: n % 7 == 0 ? null : Utc(6, 8, n % 60))).ToList();
            inputs.Add(SummaryInput.Of(Backup(dir.Sub("d" + i % 4), $"2026-10-{6 + i % 3:00}", name, clips, files: 40)));
        }
        var clock = Stopwatch.StartNew();
        var model = SummaryReport.Load(inputs, [dir.Sub("local")]);
        var html = SummaryReport.Html(model);
        clock.Stop();

        Assert.Equal(200, model.Cards.Count);
        Assert.Equal(8000, model.Total.ClipCount);
        Assert.Contains("A199", html);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"200 cards took {clock.Elapsed.TotalSeconds:0.0} s");
    }
}
