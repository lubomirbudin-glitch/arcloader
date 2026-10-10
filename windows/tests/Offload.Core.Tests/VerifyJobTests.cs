using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

public class VerifyJobTests
{
    static readonly TimeSpan Wait = TestAssembly.JobWait;   // fix round 2, item 9: longer than a PDF may take

    static string Offloaded(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 100_000, 1);
        var engine = new OffloadEngine(new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true });
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-06");
        engine.Run(plan);
        return plan.CardPaths[0];
    }

    // Spec 2.4: a verification runs in the copy queue; RunCopy returns false (no stage 2).
    [Fact]
    public void InTheQueue_VerifiesAndWritesTheReport()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        var job = new VerifyJob(card, writeReport: true);
        Assert.Equal(VerifyJobState.Waiting, job.Status().State);
        using var q = new JobQueue();
        using var finished = new ManualResetEventSlim();
        bool copyFinished = false;
        q.CopyFinished += _ => copyFinished = true;
        q.JobFinished += _ => finished.Set();
        q.Enqueue(job);
        Assert.True(finished.Wait(Wait));

        var s = job.Status();
        Assert.True(copyFinished);
        Assert.Equal(VerifyJobState.Done, s.State);
        Assert.True(s.Result!.Success);
        Assert.Equal("Verified", VerifyJobView.StatusText(s));
        Assert.Equal(CardTone.Ok, VerifyJobView.Tone(s));
        Assert.Equal("Verification A001", VerifyJobView.Title(job));
        Assert.StartsWith(Path.Combine(card, "_REPORT", "A001_verify_"), s.ReportPath);
        Assert.True(File.Exists(s.ReportPath));
        Assert.Null(VerifyJobView.ReportNote(s));   // inside the card: no note
    }

    // Ruling V2: a foreign MHL history that does not ignore _REPORT -> the report goes beside the card, and the card says so.
    [Fact]
    public void ForeignHistory_ReportOutsideTheCard_NoteInCzech()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-06", "A001");
        TestFs.WriteRandom(Layout.Join(card, "c.xml"), 1000, 3);
        MhlFixtures.Chain(card, RealAscmhlTests.Gen(card, 1, ("c.xml", RealAscmhlTests.H("xxh64", "original", RealAscmhlTests.X(card, "c.xml")))));
        var job = new VerifyJob(card, writeReport: true);
        job.RunCopy();
        var s = job.Status();

        Assert.Equal(VerifyJobState.Done, s.State);
        Assert.False(s.Result!.ReportInsideCard);
        Assert.False(s.ReportPath!.StartsWith(card + Path.DirectorySeparatorChar));
        Assert.True(File.Exists(s.ReportPath));
        Assert.Equal(VerifyTexts.ReportOutsideNote(s.ReportPath), VerifyJobView.ReportNote(s));
        Assert.Contains("outside the card folder", VerifyJobView.ReportNote(s));
    }

    // A report that cannot be written is a Czech warning on the card, not a silent nothing.
    [Fact]
    public void ReportWriteFails_CzechReportError()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-06", "A001");
        TestFs.WriteRandom(Layout.Join(card, "c.xml"), 1000, 3);
        MhlFixtures.Chain(card, RealAscmhlTests.Gen(card, 1, ("c.xml", RealAscmhlTests.H("xxh64", "original", RealAscmhlTests.X(card, "c.xml")))));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(card)!, "_REPORT"), "in the way");   // the report goes beside the card: its folder cannot be made
        var logged = new List<string>();
        var job = new VerifyJob(card, writeReport: true, logged.Add);
        job.RunCopy();
        var s = job.Status();

        Assert.Equal(VerifyJobState.Done, s.State);
        Assert.True(s.Result!.Success);
        Assert.Null(s.ReportPath);
        Assert.StartsWith("Could not write the report:", s.ReportError);
        Assert.Contains(logged, l => l.StartsWith("verify report:"));
    }

    // A newest `failed` record only counts as superseded when the file matches the older good one.
    [Fact]
    public void FailedNewest_FileDiffersFromOlderGood_IsChanged_NotSuperseded()
    {
        using var dir = new TempDir();
        var card = dir.Sub("D001");
        TestFs.WriteRandom(Layout.Join(card, "CLIP/x.mov"), 2000, 1);
        var g1 = RealAscmhlTests.Gen(card, 1, ("CLIP/x.mov", RealAscmhlTests.H("xxh64", "original", "0000000000000000")));
        var g2 = RealAscmhlTests.Gen(card, 2, ("CLIP/x.mov", RealAscmhlTests.H("xxh64", "failed", "df88698dec264e9a")));
        MhlFixtures.Chain(card, g1, g2);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.False(r.Success);
        Assert.Equal(["CLIP/x.mov"], r.Changed);
        Assert.Empty(r.FailedSuperseded);
    }

    [Fact]
    public void Problems_AreAnErrorTone_NoReportWhenOff()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        File.Delete(Path.Combine(card, "CLIP", "a.mov"));
        var job = new VerifyJob(card, writeReport: false);
        job.RunCopy();
        var s = job.Status();
        Assert.Equal("Problems", VerifyJobView.StatusText(s));
        Assert.Equal(CardTone.Error, VerifyJobView.Tone(s));
        Assert.Null(s.ReportPath);
        Assert.Null(VerifyJobView.ReportNote(s));
        Assert.False(Directory.EnumerateFiles(Path.Combine(card, "_REPORT"), "*_verify_*").Any());
    }

    [Fact]
    public void CancelledWhileQueued_NeverRuns()
    {
        using var dir = new TempDir();
        var job = new VerifyJob(dir.Sub("A001"), writeReport: true);
        job.Cancel();
        Assert.Equal(VerifyJobState.Cancelled, job.Status().State);
        Assert.False(job.RunCopy());
        Assert.Null(job.Status().Result);
    }

    [Fact]
    public void NothingToVerify_Failed_WithCzechText()
    {
        using var dir = new TempDir();
        var job = new VerifyJob(dir.Sub("A001"), writeReport: true);
        job.RunCopy();
        var s = job.Status();
        Assert.Equal(VerifyJobState.Failed, s.State);
        Assert.Contains("has no ASC MHL and no checksum list", s.Error);
    }

    [Fact]
    public void Detail_Text()
    {
        Assert.Equal("Verifying 120 / 1,234 files · 45 GB / 512 GB · 380 MB/s",
            VerifyJobView.Detail(new VerifyProgress(119, 1234, 45_000_000_000, 512_000_000_000, "x"), 380_000_000).Replace(' ', ' '));
        Assert.Equal("Verifying…", VerifyJobView.Detail(null, 0));
    }
}
