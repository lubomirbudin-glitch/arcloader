using System.IO.Hashing;
using System.Text.Json.Nodes;
using Offload.Core.Append;

namespace Offload.Core.Tests;

public static class AppendFixtures
{
    public static OffloadOptions Options() => new() { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    /// Card A001 with two clips, copied in full onto d1 and d2 into 2026-10-06\A001.
    public static (string Card, IReadOnlyList<string> CardPaths) CopiedCard(TempDir dir, bool verify = true)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 60_000, 2);
        var options = Options();
        options.Verify = verify;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-06");
        engine.Run(plan);
        return (card, plan.CardPaths);
    }

    public static void NewClip(string card, string name = "A001C003.mov", int size = 30_000, int seed = 3) =>
        TestFs.WriteRandom(Path.Combine(card, "CLIP", name), size, seed);

    /// Every file and its bytes under `dir` (relative paths) — "nothing was written" is this, unchanged.
    public static Dictionary<string, byte[]> Tree(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    public static void AssertSameTree(Dictionary<string, byte[]> before, string dir)
    {
        var after = Tree(dir);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (name, bytes) in before) Assert.Equal(bytes, after[name]);
    }
}

public class AppendPlannerTests
{
    static OffloadError Refused(string card, IReadOnlyList<string> cardPaths, string? filesystem = null, Func<string, string>? destFilesystem = null) =>
        Assert.Throws<OffloadError>(() => AppendPlanner.Plan(card, cardPaths, CancellationToken.None, filesystem, destFilesystem));

    [Fact]
    public void NewFiles_Generation2_FoldersFromThePaths()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);

        var plan = AppendPlanner.Plan(card, paths);

        Assert.Equal(["CLIP/A001C003.mov"], plan.NewFiles.Select(f => f.RelPath));
        Assert.Equal(["CLIP"], plan.NewDirs);
        Assert.Equal(2, plan.Generation);
        Assert.Equal(("2026-10-06", "A001"), (plan.Date, plan.CardName));
        Assert.Equal([dir.Sub("d1"), dir.Sub("d2")], plan.DestRoots);
        Assert.Equal(3, plan.FullScan.Files.Count);
        Assert.Equal(0, plan.UnverifiedEarlier);
        Assert.Equal(["xxh64"], plan.HashAlgorithms);
    }

    [Fact]
    public void NothingNew_EmptyPlan()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        Assert.Empty(AppendPlanner.Plan(card, paths).NewFiles);
    }

    [Fact]
    public void SizeConflict_StopsWithTheCzechList()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var c1 = Path.Combine(card, "CLIP", "A001C001.mov");
        var t = File.GetLastWriteTimeUtc(c1);
        File.AppendAllText(c1, "x");
        File.SetLastWriteTimeUtc(c1, t);

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendConflict, e.Code);
        Assert.StartsWith("Doplnění zastaveno — nic se nezkopírovalo. 1 soubor se neshoduje se zálohou:\n  CLIP/A001C001.mov: jiná velikost nebo datum (karta ", e.Message);
        Assert.EndsWith("\nUdělejte celou kopii (vznikne A001_2).", e.Message);
    }

    // A difference below the rounding must not read "100 KB, 100 KB".
    [Fact]
    public void SizeConflict_BelowTheRounding_ShowsExactBytes()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var c1 = Path.Combine(card, "CLIP", "A001C001.mov");
        File.AppendAllText(c1, "xx");
        var e = Refused(card, paths);
        Assert.Contains("(karta 100002 B · ", e.Message);
        Assert.Contains(", " + AppendPlanner.Disk(paths[0]) + " 100000 B · ", e.Message);
    }

    [Fact]
    public void MtimeConflict()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var c1 = Path.Combine(card, "CLIP", "A001C001.mov");
        File.SetLastWriteTimeUtc(c1, File.GetLastWriteTimeUtc(c1).AddMinutes(10));
        Assert.Equal(PreflightCode.AppendConflict, Refused(card, paths).Code);
    }

    // Review focus 2: a FAT32 card after the change of time — every file an hour off, still the same files.
    [Fact]
    public void FatHourShift_IsNoConflict_ButOnExFatItIs()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var f in Directory.GetFiles(card, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(f, File.GetLastWriteTimeUtc(f).AddHours(-1));
        AppendFixtures.NewClip(card);

        Assert.Single(AppendPlanner.Plan(card, paths, CancellationToken.None, "FAT32").NewFiles);
        Assert.Equal(PreflightCode.AppendConflict, Refused(card, paths, "exFAT").Code);
    }

    // Ruling I5: a FAT32 destination shifts its own times after the change of time; the exFAT card does not.
    [Fact]
    public void FatDestinationHourShift_IsNoConflict_ButOnNtfsItIs()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var f in Directory.GetFiles(Path.Combine(paths[1], "CLIP")))
            File.SetLastWriteTimeUtc(f, File.GetLastWriteTimeUtc(f).AddHours(1));
        AppendFixtures.NewClip(card);
        string Fat(string p) => p.StartsWith(dir.Sub("d2"), StringComparison.OrdinalIgnoreCase) ? "FAT32" : "NTFS";

        var plan = AppendPlanner.Plan(card, paths, CancellationToken.None, "exFAT", Fat);

        Assert.Equal(["CLIP/A001C003.mov"], plan.NewFiles.Select(f => f.RelPath));
        var e = Refused(card, paths, "exFAT", _ => "NTFS");
        Assert.Equal(PreflightCode.AppendConflict, e.Code);
        Assert.StartsWith("Doplnění zastaveno — nic se nezkopírovalo. 2 soubory se neshodují se zálohou:\n  CLIP/A001C001.mov: jiná velikost nebo datum (karta ", e.Message);   // both clips, on d2 only
    }

    [Fact]
    public void FileMissingOnADestination()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        File.Delete(Layout.Join(paths[1], "CLIP/A001C002.mov"));
        var e = Refused(card, paths);
        Assert.Equal(PreflightCode.AppendMissingFile, e.Code);
        Assert.Equal($"Na {AppendPlanner.Disk(paths[1])} chybí soubor z dřívější zálohy: CLIP/A001C002.mov — zkontrolujte zálohu přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify)", e.Message);
    }

    static void MarkFailed(string cardPath, string rel)
    {
        var json = Path.Combine(cardPath, "_REPORT", "offload_report.json");
        var doc = JsonNode.Parse(File.ReadAllText(json))!;
        foreach (var f in doc["files"]!.AsArray())
            if ((string?)f!["path"] == rel) f["destinations"] = new JsonArray("failed", "failed");
        File.WriteAllText(json, doc.ToJsonString());
    }

    [Fact]
    public void FailedInTheReport_FileStillThere_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        MarkFailed(paths[0], "CLIP/A001C002.mov");
        var e = Refused(card, paths);
        Assert.Equal(PreflightCode.AppendFailedFile, e.Code);
        Assert.Equal($"CLIP/A001C002.mov se při dřívější záloze na {AppendPlanner.Disk(paths[0])} nezkopíroval správně — doplnit nejde, udělejte celou kopii.", e.Message);
    }

    // A cancelled append leaves "failed" records of files it never wrote: they are simply copied now.
    [Fact]
    public void FailedInTheReport_FileNowhere_IsCopiedAgain()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var p in paths)
        {
            MarkFailed(p, "CLIP/A001C002.mov");
            File.Delete(Layout.Join(p, "CLIP/A001C002.mov"));
        }
        Assert.Equal(["CLIP/A001C002.mov"], AppendPlanner.Plan(card, paths).NewFiles.Select(f => f.RelPath));
    }

    // Review focus 3: the destinations have a different number of MHL generations.
    [Fact]
    public void DifferentGenerations_Diverged()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var gen1 = Path.GetFileName(Directory.GetFiles(Path.Combine(paths[1], "ascmhl"), "0001_*.mhl").Single());
        var gen2 = MhlFixtures.Generation(paths[1], 2, [("CLIP/x.mov", 1, [("xxh64", "0000000000000000")], "original")]);
        MhlFixtures.Chain(paths[1], gen1, gen2);

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        var disk = AppendPlanner.Disk(paths[0]);
        Assert.Equal($"Zálohy na cílech se liší ({disk} má 1 generaci MHL, {disk} 2) — zkontrolujte je přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify); doplnit nejde.", e.Message);
    }

    // Ruling I1: an append whose reports landed but whose MHL generation did not — the next one must not reuse 0002.
    [Fact]
    public void ReportAheadOfTheMhl_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var p in paths)
        {
            var report = OffloadReport.FromJson(File.ReadAllText(Path.Combine(p, "_REPORT", "offload_report.json")));
            report.Generation = 2;
            File.WriteAllText(Path.Combine(p, "_REPORT", "offload_report_0002.json"), report.ToJson());
        }
        AppendFixtures.NewClip(card);

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        Assert.Equal($"V {paths[0]} nesouhlasí reporty s ASC MHL (poslední report je generace 2, poslední generace MHL 1) — dřívější doplnění se nedokončilo. "
            + "Zkontrolujte zálohu přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify) a složku opravte ručně; doplnit nejde.", e.Message);
    }

    [Fact]
    public void OnlyAnHtmlOfTheNextGeneration_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var p in paths) File.WriteAllText(Path.Combine(p, "_REPORT", "A001_report_0002.html"), "<html/>");
        AppendFixtures.NewClip(card);
        Assert.Equal(PreflightCode.AppendDiverged, Refused(card, paths).Code);
    }

    // The other way round (e.g. "ascmhl create" added a generation): the chain is ahead of the reports.
    [Fact]
    public void MhlAheadOfTheReports_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var p in paths)
        {
            var gen1 = Path.GetFileName(Directory.GetFiles(Path.Combine(p, "ascmhl"), "0001_*.mhl").Single());
            var gen2 = MhlFixtures.Generation(p, 2, [("CLIP/A001C001.mov", 100_000, [("xxh64", "0000000000000000")], "verified")]);
            MhlFixtures.Chain(p, gen1, gen2);
        }
        var e = Refused(card, paths);
        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        Assert.Contains("(poslední report je generace 1, poslední generace MHL 2)", e.Message);
    }

    [Fact]
    public void BrokenAppendReport_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        File.WriteAllText(Path.Combine(paths[0], "_REPORT", "offload_report_0002.json"), "{broken");
        var e = Refused(card, paths);
        Assert.Equal(PreflightCode.AppendNoReport, e.Code);
        Assert.StartsWith($"Report dřívější zálohy v {paths[0]} nejde přečíst (offload_report_0002.json: ", e.Message);
    }

    [Fact]
    public void UnknownFileOnADestination_IsAConflict()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        AppendFixtures.NewClip(Path.Combine(paths[0]), size: 999, seed: 9);   // the same name already on d1, not in any report

        var e = Refused(card, paths);

        // Fix round 2, item 8: its own line (a crashed append leaves exactly this), not "jiná velikost nebo datum".
        Assert.Equal(PreflightCode.AppendConflict, e.Code);
        Assert.Equal("Doplnění zastaveno — nic se nezkopírovalo. 1 soubor se neshoduje se zálohou:\n"
                     + $"  na {AppendPlanner.Disk(paths[0])} je soubor CLIP/A001C003.mov, který žádný report nezná (nedokončené doplnění?)\n"
                     + "Udělejte celou kopii (vznikne A001_2).", e.Message);
    }

    // The folders of one backup must be <root>\<date>\<card> with the same date and name: the copy goes to exactly those.
    [Fact]
    public void FoldersWithDifferentNames_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var other = Path.Combine(Path.GetDirectoryName(paths[1])!, "A002");
        Directory.Move(paths[1], other);
        var e = Refused(card, [paths[0], other]);
        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        Assert.Equal($"Složky dřívější zálohy se jmenují různě ({paths[0]}, {other}) — doplnit jde jen do složek se stejným datem a názvem karty.", e.Message);
    }

    // Fix round 1, item 2 (ruling I3): another card in the reader (swapped between the prompt and Start, or a wrong
    // --into) shares no file with the backup — appending it wholesale into A001 would mix two cards.
    [Fact]
    public void OtherCard_NoFileOfTheBackup_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        Directory.Delete(card, recursive: true);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "B007C001.mov"), 70_000, 9);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "B007C002.mov"), 70_000, 10);
        var backups = paths.Select(AppendFixtures.Tree).ToList();

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendOtherCard, e.Code);
        Assert.Equal("Na kartě není žádný soubor z dřívější zálohy — jde o jinou kartu? Udělejte celou kopii.", e.Message);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(backups[d], paths[d]);
    }

    // Fix round 1, item 4: a new file's temporary copy name (".<name>.offload-part") is a file the backup recorded —
    // the copy would write over it.
    [Fact]
    public void PartNameOfANewFile_IsARecordedFile_Conflict()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", ".A001C003.mov.offload-part"), 5_000, 7);   // a camera/user file of that name
        var engine = new OffloadEngine(AppendFixtures.Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-06");
        engine.Run(plan);
        File.Delete(Path.Combine(card, "CLIP", ".A001C003.mov.offload-part"));   // gone from the card, still in the backup
        AppendFixtures.NewClip(card);

        var e = Refused(card, plan.CardPaths);

        Assert.Equal(PreflightCode.AppendConflict, e.Code);
        Assert.Contains("CLIP/A001C003.mov (dočasné jméno kopie CLIP/.A001C003.mov.offload-part je jiný soubor zálohy nebo karty)", e.Message);

        // Both new: one would be copied over the other.
        File.Delete(Path.Combine(card, "CLIP", "A001C003.mov"));
        AppendFixtures.NewClip(card, "A001C004.mov", seed: 4);
        AppendFixtures.NewClip(card, ".A001C004.mov.offload-part", seed: 5);
        Assert.Contains("CLIP/A001C004.mov (dočasné jméno kopie CLIP/.A001C004.mov.offload-part", Refused(card, plan.CardPaths).Message);
    }

    // Fix round 1, item 5: a changed generation, or a generation file outside the chain, is a damaged history.
    [Fact]
    public void DamagedMhlHistory_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var gen1 = Directory.GetFiles(Path.Combine(paths[1], "ascmhl"), "0001_*.mhl").Single();
        File.AppendAllText(gen1, " ");

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendMhlDamaged, e.Code);
        Assert.Equal($"Historie ASC MHL v {paths[1]} je poškozená — zkontrolujte ji přes Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify); doplnit nejde.", e.Message);

        File.WriteAllText(gen1, File.ReadAllText(gen1)[..^1]);
        Assert.Single(AppendPlanner.Plan(card, paths).NewFiles);   // repaired: fine again
        File.Copy(gen1, Path.Combine(paths[0], "ascmhl", "0002_A001_2026-10-06_090000Z.mhl"));   // not in the chain
        Assert.Equal(PreflightCode.AppendMhlDamaged, Refused(card, paths).Code);
    }

    // Fix round 1, item 7: _REPORT that cannot be listed is a Czech "report nejde přečíst", not a crash.
    [Fact]
    public void UnlistableReportFolder_Refused()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var report = new DirectoryInfo(Path.Combine(paths[0], "_REPORT"));
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(me, System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var acl = report.GetAccessControl();
        acl.AddAccessRule(deny);
        report.SetAccessControl(acl);
        try
        {
            var e = Refused(card, paths);
            Assert.Equal(PreflightCode.AppendNoReport, e.Code);
            Assert.StartsWith($"Report dřívější zálohy v {paths[0]} nejde přečíst (", e.Message);
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            report.SetAccessControl(acl);
        }
    }

    [Fact]
    public void UnverifiedEarlierBackup_CountsFiles()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir, verify: false);
        AppendFixtures.NewClip(card);
        Assert.Equal(2, AppendPlanner.Plan(card, paths).UnverifiedEarlier);
    }

    // Plan 9 fix round 2, item D (ruling R2): the planner counts unverified files by the one definition
    // (CardRecord.ReadBack), as the day report does — a copy recorded "verified" was read back, also in a report whose
    // read-back as a whole did not end (cancelled after the last file).
    [Fact]
    public void UnverifiedEarlier_TheSharedDefinition()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var p in paths)
        {
            var json = Path.Combine(p, "_REPORT", "offload_report.json");
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(json))!;
            node["verified"] = false;
            File.WriteAllText(json, node.ToJsonString());
            Assert.True(CardRecord.Load(p).Verified);
        }
        AppendFixtures.NewClip(card);

        Assert.Equal(0, AppendPlanner.Plan(card, paths).UnverifiedEarlier);
    }

    [Fact]
    public void MissingBackup_Report_Mhl()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var gone = Path.Combine(dir.Path, "gone", "2026-10-06", "A001");
        Assert.Equal(PreflightCode.AppendUnavailable, Refused(card, [paths[0], gone]).Code);
        Assert.Equal($"Dřívější záloha není dostupná: {gone} — připojte disk, nebo udělejte celou kopii.", Refused(card, [gone]).Message);

        Directory.Delete(Path.Combine(paths[1], "ascmhl"), recursive: true);
        Assert.Equal($"V {paths[1]} chybí ASC MHL (ascmhl\\ascmhl_chain.xml) — doplnit nejde.", Refused(card, paths).Message);

        File.Delete(Path.Combine(paths[0], "_REPORT", "offload_report.json"));
        Assert.Equal($"V {paths[0]} chybí report dřívější zálohy (_REPORT\\offload_report.json) — doplnit nejde.", Refused(card, paths).Message);
    }

    // Spec 2.2 "záloha udělaná na Macu": a report without the Windows-only keys, a Mac card path.
    [Fact]
    public void MacBackup_Fixture()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var t = new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc);
        var data = TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 1000, 1);
        File.SetLastWriteTimeUtc(Path.Combine(card, "CLIP", "A001C001.mov"), t);
        var backup = dir.Sub("mac", "2026-10-06", "A001");
        Directory.CreateDirectory(Path.Combine(backup, "CLIP"));
        File.WriteAllBytes(Path.Combine(backup, "CLIP", "A001C001.mov"), data);
        File.SetLastWriteTimeUtc(Path.Combine(backup, "CLIP", "A001C001.mov"), t);
        var hash = Format.Hex64(XxHash64.HashToUInt64(data));
        Directory.CreateDirectory(Path.Combine(backup, "_REPORT"));
        File.WriteAllText(Path.Combine(backup, "_REPORT", "offload_report.json"), $$"""
            {
              "cardName" : "A001", "clips" : [ ], "copyFinished" : "2026-10-06T07:10:00Z", "date" : "2026-10-06",
              "destinations" : [ { "cardPath" : "/Volumes/Zaloha/2026-10-06/A001", "filesFailed" : 0, "filesOK" : 1, "filesystem" : "apfs", "root" : "/Volumes/Zaloha" } ],
              "files" : [ { "destinations" : [ "verified" ], "errors" : [ null ], "modified" : "2026-10-06T07:00:00Z", "path" : "CLIP/A001C001.mov", "size" : 1000, "xxh64" : "{{hash}}" } ],
              "finished" : "2026-10-06T07:11:00Z", "hashAlgorithm" : "xxh64", "skipped" : [ ], "source" : "/Volumes/A001", "sourceFilesystem" : "exfat",
              "started" : "2026-10-06T07:09:00Z", "tool" : "offload", "totalBytes" : 1000, "totalFiles" : 1, "verified" : true, "version" : "0.1.0"
            }
            """);
        var gen1 = MhlFixtures.Generation(backup, 1, [("CLIP/A001C001.mov", 1000, [("xxh64", hash)], "original")]);
        MhlFixtures.Chain(backup, gen1);
        AppendFixtures.NewClip(card);

        var plan = AppendPlanner.Plan(card, [backup]);

        Assert.Equal(["CLIP/A001C003.mov"], plan.NewFiles.Select(f => f.RelPath));
        Assert.Equal(2, plan.Generation);
    }

    // The planner only reads: neither the card nor the backup changes, also when it refuses.
    [Fact]
    public void Planning_WritesNothing()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var cardBefore = AppendFixtures.Tree(card);
        var backups = paths.Select(AppendFixtures.Tree).ToList();

        AppendPlanner.Plan(card, paths);
        File.Delete(Layout.Join(paths[1], "CLIP/A001C002.mov"));
        backups[1].Remove(@"CLIP\A001C002.mov");
        Refused(card, paths);

        AppendFixtures.AssertSameTree(cardBefore, card);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(backups[d], paths[d]);
    }
}
