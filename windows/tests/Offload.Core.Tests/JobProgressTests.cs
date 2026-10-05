using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class JobProgressTests
{
    static readonly DateTime T0 = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    static ProgressSnapshot Snap(long read, long[] written, long[]? verified = null, long total = 1_000_000_000) =>
        new(CopyPhase.Copy, total, 10, 1, "DCIM/100CANON/A001.MOV", read, written, verified ?? new long[written.Length], 0);

    static JobStatus St(JobPhase phase, ProgressSnapshot? copy = null) => new() { Phase = phase, Copy = copy };

    static JobProgress Copying()
    {
        var p = new JobProgress(T0);
        p.Update(St(JobPhase.Copying, Snap(0, [0, 0])), T0.AddSeconds(0.5));
        p.Update(St(JobPhase.Copying, Snap(100_000_000, [100_000_000, 50_000_000])), T0.AddSeconds(1.5));
        p.Update(St(JobPhase.Copying, Snap(150_000_000, [150_000_000, 100_000_000])), T0.AddSeconds(2.5));
        return p;
    }

    [Fact]
    public void Rates_ResetOnPhaseChange_ThenSmoothed()
    {
        var p = Copying();
        Assert.Equal(80e6, p.ReadRate, 1);                  // 0.6 × 100 + 0.4 × 50 MB/s
        Assert.Equal(80e6, p.DestRates[0], 1);
        Assert.Equal(50e6, p.DestRates[1], 1);
        Assert.Equal(850e6 / 80e6, p.Eta!.Value, 3);
        p.Update(St(JobPhase.Verifying, Snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [0, 0])), T0.AddSeconds(3.5));
        Assert.Equal(0, p.ReadRate);
        Assert.Equal([0.0, 0.0], p.DestRates);
    }

    [Fact]
    public void Updates_CloserThan200ms_KeepRates()
    {
        var p = Copying();
        p.Update(St(JobPhase.Copying, Snap(900_000_000, [900_000_000, 900_000_000])), T0.AddSeconds(2.6));
        Assert.Equal(80e6, p.ReadRate, 1);
    }

    [Fact]
    public void Verifying_EtaFromTheSlowestDestination()
    {
        var p = new JobProgress(T0);
        p.Update(St(JobPhase.Verifying, Snap(0, [0, 0], [0, 0])), T0.AddSeconds(0.5));
        p.Update(St(JobPhase.Verifying, Snap(0, [0, 0], [200_000_000, 100_000_000])), T0.AddSeconds(1.5));
        Assert.Equal(100e6, p.ReadRate, 1);
        Assert.Equal(9.0, p.Eta!.Value, 3);
        Assert.Equal("D1 200 MB/s · D2 100 MB/s · zbývá 0:09", p.DetailText);
    }

    [Fact]
    public void PhaseProgress_EachPhase()
    {
        var p = new JobProgress(T0);
        p.Update(St(JobPhase.Copying, Snap(250_000_000, [0])), T0);
        Assert.Equal(0.25, p.PhaseProgress, 3);
        p.Update(St(JobPhase.Verifying, Snap(0, [0, 0], [500_000_000, 250_000_000])), T0);
        Assert.Equal(0.375, p.PhaseProgress, 3);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, ClipIndex = 1, ClipCount = 4, ClipFraction = 0.5 }, T0);
        Assert.Equal(0.375, p.PhaseProgress, 3);
        p.Update(new JobStatus { Phase = JobPhase.Done }, T0);
        Assert.Equal(1, p.PhaseProgress);
        p.Update(new JobStatus(), T0);
        Assert.Equal(0, p.PhaseProgress);
    }

    [Fact]
    public void DetailTexts()
    {
        Assert.Equal("Karta 80 MB/s · D1 80 MB/s · D2 50 MB/s · zbývá 0:10 · A001.MOV", Copying().DetailText);
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = JobPhase.Proxies, ClipIndex = 2, ClipCount = 5, ClipName = "A003", ClipFraction = 0.426 }, T0);
        Assert.Equal("Proxy 3/5 · A003 · 43 %", p.DetailText);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, ClipIndex = 0, ClipCount = 3, ClipName = "A001" }, T0);
        Assert.Equal("Klip 1/3 · A001", p.DetailText);
        p.Update(new JobStatus { Phase = JobPhase.Flushing }, T0);
        Assert.Equal("Zápis cache disků…", p.DetailText);
    }

    [Theory]
    [InlineData(59, "0:59")]
    [InlineData(600, "10:00")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-5, "0:00")]
    public void EtaText(double seconds, string text) => Assert.Equal(text, JobProgress.EtaText(seconds));

    [Theory]
    [InlineData(JobPhase.Done, false, "Chyba", CardTone.Error)]
    [InlineData(JobPhase.Done, true, "Hotovo", CardTone.Ok)]
    [InlineData(JobPhase.Failed, null, "Chyba", CardTone.Error)]
    [InlineData(JobPhase.Cancelled, null, "Zrušeno", CardTone.Warning)]
    [InlineData(JobPhase.Copying, null, "Kopírování", CardTone.Active)]
    [InlineData(JobPhase.Waiting, null, "Čeká", CardTone.Waiting)]
    public void StatusText_AndTone(JobPhase phase, bool? copySucceeded, string text, CardTone tone)
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = phase, CopySucceeded = copySucceeded }, T0);
        Assert.Equal(text, p.StatusText);
        Assert.Equal(tone, p.Tone);
    }

    [Fact]
    public void Brake_OnlyWhileCopying()
    {
        const long mb = 1_000_000;
        var p = new JobProgress(T0);
        p.AttachBrakeMeter(new BrakeMeter(16 << 20, 4));
        for (int i = 0; i <= 8; i++)
        {
            long read = i * 50 * mb;
            p.Update(St(JobPhase.Copying, Snap(read, [read, Math.Max(0, read - 48 * mb)])), T0.AddSeconds(i * 0.5));
        }
        Assert.Equal(2, p.Brake?.Element);
        p.Update(St(JobPhase.Verifying, Snap(400 * mb, [400 * mb, 400 * mb])), T0.AddSeconds(5));
        Assert.Null(p.Brake);
    }

    [Fact]
    public void SummaryText()
    {
        var clip = new Clip { Name = "A001", Path = "A001.MOV", Format = ClipFormat.Video, Segments = ["A001.MOV"], Bytes = 1 };
        var report = new OffloadReport
        {
            CardName = "A001", Date = "2026-10-05", Source = @"F:\", SourceFilesystem = "exFAT",
            Started = T0, CopyFinished = T0.AddSeconds(10), Finished = T0.AddSeconds(15),
            Verified = true, TotalBytes = 1_000_000_000, TotalFiles = 3,
            Destinations = [], Files = [], Skipped = [],
            Clips =
            [
                new ClipEntry { Clip = clip, ProxyStatus = ProxyStatus.Done },
                new ClipEntry { Clip = clip, ProxyStatus = ProxyStatus.Failed },
                new ClipEntry { Clip = clip, ProxyStatus = ProxyStatus.Unsupported },
            ],
            Proxy = new ProxySettings { Enabled = true },
        };
        Assert.Equal("kopie 100 MB/s · ověření 200 MB/s · 3 klipy · proxy 1, 1 chyba, 1 nepodporováno", JobProgress.SummaryText(report));
    }

    [Fact]
    public void Texts_CzechPlurals()
    {
        Assert.Equal("Vyberte kartu", Texts.Cards(0));
        Assert.Equal("1 karta", Texts.Cards(1));
        Assert.Equal("3 karty", Texts.Cards(3));
        Assert.Equal("5 karet", Texts.Cards(5));
        Assert.Equal("0 cílů", Texts.Destinations(0));
        Assert.Equal("1 cíl", Texts.Destinations(1));
        Assert.Equal("2 cíle", Texts.Destinations(2));
        Assert.Equal("1 soubor · 1.00 KB", Texts.Files(1, 1000));
        Assert.Equal("12 souborů · 2.50 GB", Texts.Files(12, 2_500_000_000));
        Assert.Equal("Všechny soubory jsou na 1 disku a ověřené — kartu lze vysunout.", Texts.SafeToEject(1));
        Assert.Equal("Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.", Texts.SafeToEject(2));
        Assert.Equal("3 soubory selhaly — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.", Texts.Failed(3));
        Assert.Equal("bez varování", Texts.Warnings(0));
        Assert.Equal("2 varování", Texts.Warnings(2));
    }

    // Review focus 5
    [Fact]
    public void CancelledBeforeStart_ReportsCancelled()
    {
        using var dir = new TempDir();
        var job = new OffloadJob(new JobConfig(dir.Sub("card"), [dir.Sub("d1")]));
        job.Cancel();
        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
    }

    [Fact]
    public void PhaseChangeInsideSkippedUpdate_ResetsRates()
    {
        var p = Copying();
        p.Update(St(JobPhase.Verifying, Snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [0, 0])), T0.AddSeconds(2.6));
        p.Update(St(JobPhase.Verifying, Snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [100_000_000, 100_000_000])), T0.AddSeconds(3.6));
        Assert.True(p.ReadRate >= 0);
        Assert.All(p.DestRates, r => Assert.True(r >= 0));
        Assert.Equal(0, p.ReadRate);
    }

    [Fact]
    public void StartedJob_CancelledDuringRun_NeverGoesBackFromFinished()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestFs.WriteRandom(Path.Combine(card, "A.bin"), 8 << 20, 1);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { AnalyzeClips = false },
            new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true });
        var t = new Thread(() => job.RunCopy());
        t.Start();
        // Cancel only once RunCopy has begun (cancelling before it is the legitimate never-started case).
        while (t.IsAlive && job.Status().Phase == JobPhase.Waiting) Thread.Yield();
        job.Cancel();
        bool finished = false;
        while (t.IsAlive)
        {
            var ph = job.Status().Phase;
            if (finished) Assert.True(ph.IsFinished());
            finished |= ph.IsFinished();
        }
        t.Join();
        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
    }
}
