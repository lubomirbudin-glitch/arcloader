using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

public class VerifyJobTests
{
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

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
        Assert.Equal("Ověřeno", VerifyJobView.StatusText(s));
        Assert.Equal(CardTone.Ok, VerifyJobView.Tone(s));
        Assert.Equal("Ověření A001", VerifyJobView.Title(job));
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
        Assert.Contains("mimo složku karty", VerifyJobView.ReportNote(s));
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
        Assert.Equal("Problémy", VerifyJobView.StatusText(s));
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
        Assert.Contains("není ASC MHL ani seznam součtů", s.Error);
    }

    [Fact]
    public void Detail_Text()
    {
        Assert.Equal("Ověřuji 120 / 1 234 souborů · 45 GB / 512 GB · 380 MB/s",
            VerifyJobView.Detail(new VerifyProgress(119, 1234, 45_000_000_000, 512_000_000_000, "x"), 380_000_000).Replace(' ', ' '));
        Assert.Equal("Ověřuji…", VerifyJobView.Detail(null, 0));
    }
}
