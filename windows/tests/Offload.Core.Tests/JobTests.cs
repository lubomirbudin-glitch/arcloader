namespace Offload.Core.Tests;

public class JobTests
{
    static OffloadOptions Options() => new() { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    [Fact]
    public void Run_Success_EndsDoneAndSafeToEject()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 100_000, 1);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-04" }, Options());
        Assert.Equal(JobPhase.Waiting, job.Status().Phase);

        var report = job.Run();

        Assert.NotNull(report);
        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.True(s.CopySucceeded);
        Assert.Equal(100_000, s.Copy!.ReadBytes);
        Assert.Equal("Hotovo", s.Phase.Label());
    }

    [Fact]
    public void Run_PreflightFailure_EndsFailedWithMessage()
    {
        using var dir = new TempDir();
        var job = new OffloadJob(new JobConfig(Path.Combine(dir.Path, "nope"), [dir.Sub("d1")]), Options());
        Assert.Null(job.Run());
        var s = job.Status();
        Assert.Equal(JobPhase.Failed, s.Phase);
        Assert.Contains("Source not found", s.Error);
        Assert.True(s.Phase.IsFinished());
    }

    [Fact]
    public void Cancel_BeforeRun_EndsCancelled()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 1000, 1);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]), Options());
        job.Cancel();
        job.Run();
        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
        Assert.False(job.Status().SafeToEject);
    }

    [Fact]
    public void Config_MapsToEngineOptions()
    {
        var job = new OffloadJob(new JobConfig(@"E:\", [@"D:\"]) { ChunkMB = 8, Buffers = 4, Verify = false, AllowSameDevice = true });
        Assert.Equal(8 << 20, job.Engine.Options.ChunkSize);
        Assert.Equal(4, job.Engine.Options.BufferCount);
        Assert.False(job.Engine.Options.Verify);
        Assert.True(job.Engine.Options.AllowSameDevice);
    }

    [Fact]
    public void Run_UnexpectedException_EndsFailedAndDoesNotThrow()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 1000, 1);
        var job = new OffloadJob(new JobConfig(card, [Path.Combine(dir.Path, "bad\0name")]), Options());

        var report = job.Run();

        Assert.Null(report);
        var s = job.Status();
        Assert.Equal(JobPhase.Failed, s.Phase);
        Assert.NotNull(s.Error);
        Assert.NotEmpty(s.Error!);
    }
}
