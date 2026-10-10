using System.IO.Hashing;
using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Verification;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// R6: a copy the app never finished — crashed mid-copy (only the manifest says what is on the disk) or mid-verification
/// (its reports say verifyPending) — is completed by an append: complete files are not copied again, a part goes on from
/// its checkpoint, nothing on the disks is overwritten, the result is verified and committed like any copy.
public class ResumeTests
{
    internal const int Block = 64 * 1024;
    internal static readonly DateTime T = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    internal static OffloadOptions Options() => new() { ChunkSize = Block, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, CheckpointBlocks = 1 };

    internal static byte[] CardFile(string card, string rel, int size, int seed)
    {
        var data = TestFs.WriteRandom(Layout.Join(card, rel), size, seed);
        File.SetLastWriteTimeUtc(Layout.Join(card, rel), T);
        return data;
    }

    internal static ulong[] Blocks(byte[] data) => Enumerable.Range(0, BlockManifest.CountFor(data.Length, Block))
        .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, Math.Min(Block, data.Length - b * Block)))).ToArray();

    internal static ManifestRecord FileRecord(string rel, byte[] data)
    {
        var x128 = new XxHash128();
        x128.Append(data);
        return new ManifestRecord
        {
            Path = rel, Size = data.Length, Modified = T, Xxh64 = Format.Hex64(XxHash64.HashToUInt64(data)),
            Xxh128 = Convert.ToHexStringLower(x128.GetCurrentHash()), BlockSize = Block, Blocks = ManifestRecord.Hex(Blocks(data)),
        };
    }

    /// What a copy that crashed leaves: A in its place with its record, B's first `partBlocks` blocks in its part with a
    /// checkpoint, nothing of C — and no report, no ASC MHL.
    internal static string CrashedCopy(TempDir dir, string root, byte[] a, byte[] b, int partBlocks)
    {
        var cardPath = Path.Combine(root, "2026-10-08", "A001");
        Directory.CreateDirectory(Path.Combine(cardPath, "_REPORT"));
        Directory.CreateDirectory(Path.Combine(cardPath, "CLIP"));
        File.WriteAllBytes(Path.Combine(cardPath, "CLIP", "A.mov"), a);
        File.SetLastWriteTimeUtc(Path.Combine(cardPath, "CLIP", "A.mov"), T);
        ManifestFile.Append(cardPath, FileRecord("CLIP/A.mov", a));
        File.WriteAllBytes(Path.Combine(cardPath, "CLIP", ".B.mov.offload-part"), b[..(partBlocks * Block)]);
        ManifestFile.Append(cardPath, new ManifestRecord
        {
            Kind = ManifestRecord.PartKind, Path = "CLIP/B.mov", Size = b.Length, Modified = T, BlockSize = Block,
            Blocks = ManifestRecord.Hex(Blocks(b)[..partBlocks]),
        });
        return cardPath;
    }

    [Fact]
    public void CrashMidCopy_AppendResumes_CompleteFileNotRead_PartGoesOn()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", 3 * Block + 10, 1);
        var b = CardFile(card, "CLIP/B.mov", 5 * Block + 20, 2);
        var c = CardFile(card, "CLIP/C.mov", 2 * Block, 3);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 3);
        var read = new List<string>();
        var options = Options();
        var job = new OffloadJob(new JobConfig(card, []) { AppendInto = [cardPath], AnalyzeClips = false, HistoryFolder = dir.Sub("h"),
            Outputs = new Outputs.OutputSettings { DayReport = false } }, options);
        options.BeforeFileCopy = i => read.Add(job.Plan!.Scan.Files[i].RelPath);

        job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToFormat);
        var plan = job.Plan!;
        Assert.True(plan.Resuming);
        Assert.Equal(1, plan.Generation);
        Assert.Equal(["CLIP/B.mov", "CLIP/C.mov"], read);                 // A is not read from the card again
        Assert.Equal(3, plan.Resume![plan.Scan.Files.ToList().FindIndex(f => f.RelPath == "CLIP/B.mov")]!.Blocks.Length);
        Assert.Equal(a, File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "A.mov")));
        Assert.Equal(b, File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "B.mov")));
        Assert.Equal(c, File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "C.mov")));
        Assert.Empty(Directory.GetFiles(cardPath, "*.offload-part", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "offload_report.json")));
        Assert.Null(job.Report!.Files[0].Rewritten);                       // a resume is no repair
        Assert.True(BackupVerifier.Verify(cardPath, chunkSize: 4096).Success);
        Assert.True(CardRecord.Load(cardPath).Verified);
        Assert.Equal(HistoryGeneration.Full, Assert.Single(Assert.Single(new HistoryStore(Path.Combine(dir.Path, "h")).Load()).Generations).Kind);
    }

    // The part's blocks feed the hashes from the disk, not from the card (each checked against its checkpoint): the card
    // is read only from where the part ends.
    [Fact]
    public void Resume_PrefixFromThePart_CardReadOnlyAfterIt()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 4 * Block, 2);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 2);
        var job = new OffloadJob(new JobConfig(card, []) { AppendInto = [cardPath], AnalyzeClips = false, RecordHistory = false,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, Options());

        job.Run();

        Assert.True(job.Status().SafeToFormat);
        Assert.Equal(4L * Block, job.Status().Copy!.ReadBytes);   // B: 2 blocks from the part + 2 from the card; A not read
    }

    // The checkpoint does not match the part any more (the part changed since): the file fails, nothing is trusted.
    [Fact]
    public void Resume_PartDiffersFromItsCheckpoint_FileFails_NeverVerified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 4 * Block, 2);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 2);
        var part = Path.Combine(cardPath, "CLIP", ".B.mov.offload-part");
        var bytes = File.ReadAllBytes(part);
        bytes[5] ^= 1;
        File.WriteAllBytes(part, bytes);
        var job = new OffloadJob(new JobConfig(card, []) { AppendInto = [cardPath], AnalyzeClips = false, RecordHistory = false,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, Options());

        job.Run();

        var f = job.Report!.Files.Single(x => x.Path == "CLIP/B.mov");
        Assert.StartsWith("resume: ", f.SourceError);
        Assert.False(job.Status().SafeToFormat);
        Assert.False(File.Exists(Path.Combine(cardPath, "CLIP", "B.mov")));
    }

    // Crashed during the verification (verifyPending, no MHL): the append verifies and commits it — the card is not read.
    [Fact]
    public void PendingVerification_AppendFinishesIt_WithoutTheCard()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        CardFile(card, "CLIP/A.mov", 3 * Block, 1);
        CardFile(card, "CLIP/B.mov", 2 * Block + 7, 2);
        var options = Options();
        options.DeferVerify = true;   // as a verify-in-proxy job whose stage 2 never ran
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");
        var report = engine.Run(plan);
        Assert.True(report.VerifyPending);
        var cardPath = plan.CardPaths[0];
        Assert.Throws<OffloadError>(() => AppendPlanner.CheckBackup([cardPath]));   // as before: no MHL
        var read = new List<int>();
        var resume = Options();
        resume.BeforeFileCopy = read.Add;
        var job = new OffloadJob(new JobConfig(card, []) { AppendInto = [cardPath], AnalyzeClips = false, RecordHistory = false,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, resume);

        job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(job.Status().SafeToFormat);
        Assert.Empty(read);
        Assert.Equal((1, true), (job.Plan!.Generation, job.Plan.Resuming));
        Assert.Single(Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl"));
        Assert.Null(OffloadReport.FromJson(File.ReadAllText(Path.Combine(cardPath, "_REPORT", "offload_report.json"))).VerifyPending);
        Assert.True(BackupVerifier.Verify(cardPath, chunkSize: 4096).Success);
    }

    // Nothing of the card is someone else's: a file in its place that the manifest does not name stops the resume.
    [Fact]
    public void Resume_FileWithoutRecord_Conflict_NothingWritten()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 2 * Block, 2);
        CardFile(card, "CLIP/C.mov", Block, 3);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 1);
        File.WriteAllBytes(Path.Combine(cardPath, "CLIP", "C.mov"), new byte[Block]);

        var e = Assert.Throws<OffloadError>(() => AppendPlanner.Plan(card, [cardPath], CancellationToken.None, sourceFilesystem: "NTFS"));
        Assert.Equal(PreflightCode.AppendConflict, e.Code);
        Assert.Equal(new byte[Block], File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "C.mov")));
    }

    [Fact]
    public void Repair_OfAnUnfinishedBackup_Refused()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 2 * Block, 2);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 1);

        var e = Assert.Throws<OffloadError>(() => RepairPlanner.Plan(card, [cardPath]));
        Assert.Equal(PreflightCode.RepairConflict, e.Code);
        Assert.Equal(Msg.ForRepair(Msg.RepairUnfinished), e.Message);
    }

    [Fact]
    public void Find_OneFolderUnfinished_OtherNot_Diverged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 2 * Block, 2);
        var crashed = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 1);
        var engine = new OffloadEngine(Options());
        var whole = engine.Run(engine.Prepare(card, [dir.Sub("d2")], null, "2026-10-08")).Destinations[0].CardPath;

        var e = Assert.Throws<OffloadError>(() => ResumePlanner.Find([crashed, whole]));
        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        Assert.Null(ResumePlanner.Find([whole]));
    }

    [Fact]
    public void History_StartEntryUnfinished_ThenTheCopysEntry_CancelLeavesNone()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        CardFile(card, "CLIP/A.mov", Block, 1);
        CardFile(card, "CLIP/B.mov", Block, 2);
        var history = dir.Sub("h");
        var options = Options();
        HistoryEntry? during = null;
        options.BeforeFileCopy = i => { if (i == 1) during = Assert.Single(new HistoryStore(history).Load()); };
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-08", AnalyzeClips = false, HistoryFolder = history,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, options);

        job.Run();

        Assert.True(during!.Unfinished);
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.False(entry.Unfinished);
        Assert.Equal(during.Id, entry.Id);
        Assert.True(entry.Success && entry.Verified);

        var history2 = dir.Sub("h2");
        var cancel = Options();
        var cancelled = new OffloadJob(new JobConfig(card, [dir.Sub("d2")]) { Date = "2026-10-08", AnalyzeClips = false, HistoryFolder = history2,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, cancel);
        cancel.BeforeFileCopy = _ => cancelled.Cancel();
        cancelled.Run();
        Assert.Empty(new HistoryStore(history2).Load());
    }

    [Fact]
    public void Texts_UnfinishedEntry()
    {
        var e = new HistoryEntry { Id = "x", CardName = "A001", Unfinished = true, Finished = T, Destinations = [new() { Root = @"D:\Zaloha", CardPath = @"D:\Zaloha\2026-10-08\A001" }] };
        var m = new HistoryMatch(e, HistoryMatchKind.NewFiles, [new SourceFile("a", 10, T)], 1_000_000, [], 0);
        Assert.Equal(("Unfinished backup on 2026-10-08 " + T.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " to Zaloha (D:) — the copy was interrupted; it can be finished", HistoryTone.Warning),
            HistoryText.Line(m));
        Assert.StartsWith("Finish interrupted backup (1 file · ", AppendRules.CheckboxText(m));
        Assert.False(BackedUpPrompt.ShouldAsk(new CardInsight { Source = "E:\\", History = m }));
        Assert.Equal(("! unfinished", HistoryTone.Warning), (HistoryView.Row(e).Status, HistoryView.Row(e).Tone));
    }
}
