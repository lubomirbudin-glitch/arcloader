using System.Diagnostics;

namespace Offload.Core.Tests;

/// Every tool Offload runs (ffmpeg, art-cmd, REDline, braw-decode, ffprobe) works at normal CPU and I/O priority. On the
/// Mac, proxies at low priority made an ARRIRAW 4.6K decode crawl (1.4 fps in the app, 11.6 from the shell). Windows
/// hands a child the parent's class when that is BelowNormal or Idle, and very low I/O priority in background mode
/// (measured 2026-10-09); a thread in background mode (plan 10's verify readers) passes nothing on.
public class ToolPriorityTests
{
    /// A child that lives long enough to be looked at.
    static ToolProcess Waiting() => new(Path.Combine(Environment.SystemDirectory, "ping.exe"), ["-n", "30", "127.0.0.1"], null, pipeStdin: false);

    // A tool started from a normal Offload: see ToolPriorityInheritanceTests for a low one (with its control).

    // Plan 10: the background verify readers read at low I/O priority on their own thread — a tool started from such a
    // thread still runs at normal priority, and the reader keeps its low priority.
    [Fact]
    public void StartedFromABackgroundModeThread_Normal_ThreadStaysLow()
    {
        (ProcessPriorityClass, int) child = default;
        int threadIo = -1;
        var t = new Thread(() =>
        {
            using var low = Win32.BackgroundIo();
            using var p = Waiting();
            p.Start(_ => { });
            child = p.Priority();
            threadIo = Win32.IoPriority();
        });
        t.Start();
        t.Join();
        Assert.Equal((ProcessPriorityClass.Normal, 2), child);
        Assert.True(threadIo < 2, $"thread I/O priority {threadIo}");
    }

    // What a low parent hands down is lifted to normal right after the start.
    [Fact]
    public void LoweredChild_RaisedToNormal()
    {
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            p.PriorityClass = ProcessPriorityClass.BelowNormal;
            Assert.True(Win32.SetProcessIoPriority(p, 0));
            Assert.Equal(0, Win32.ProcessIoPriority(p));

            Win32.RaiseToNormal(p);

            Assert.Equal(ProcessPriorityClass.Normal, p.PriorityClass);
            Assert.Equal(2, Win32.ProcessIoPriority(p));
        }
        finally
        {
            p.Kill();
        }
    }

    // A child never goes down: one started above normal keeps it.
    [Fact]
    public void HigherChild_LeftAlone()
    {
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            p.PriorityClass = ProcessPriorityClass.AboveNormal;
            Win32.RaiseToNormal(p);
            Assert.Equal(ProcessPriorityClass.AboveNormal, p.PriorityClass);
        }
        finally
        {
            p.Kill();
        }
    }
}

/// The whole test process low (BelowNormal, then background mode): alone, after the parallel tests. Each case starts a
/// plain child first — the control: it really is low, so the tool's normal priority comes from ToolProcess raising it,
/// not from a Windows build that hands nothing down.
[Collection("LowPriorityProcess")]
public class ToolPriorityInheritanceTests
{
    static string Ping => Path.Combine(Environment.SystemDirectory, "ping.exe");

    static Process PlainChild() =>
        Process.Start(new ProcessStartInfo(Ping, "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;

    static (ProcessPriorityClass, int) Tool()
    {
        using var p = new ToolProcess(Ping, ["-n", "30", "127.0.0.1"], null, pipeStdin: false);
        p.Start(_ => { });
        return p.Priority();
    }

    [Fact]
    public void BelowNormalParent_PlainChildLow_ToolNormal()
    {
        var self = Process.GetCurrentProcess();
        var before = self.PriorityClass;
        try
        {
            self.PriorityClass = ProcessPriorityClass.BelowNormal;
            using (var plain = PlainChild())
            {
                try { Assert.Equal(ProcessPriorityClass.BelowNormal, plain.PriorityClass); }
                finally { plain.Kill(); }
            }
            Assert.Equal((ProcessPriorityClass.Normal, 2), Tool());
        }
        finally
        {
            self.PriorityClass = before;
        }
    }

    [Fact]
    public void BackgroundModeParent_PlainChildIoZero_ToolNormal()
    {
        using var background = Win32.BackgroundProcess();
        using (var plain = PlainChild())
        {
            try { Assert.Equal(0, Win32.ProcessIoPriority(plain)); }
            finally { plain.Kill(); }
        }
        Assert.Equal((ProcessPriorityClass.Normal, 2), Tool());
    }
}

[CollectionDefinition("LowPriorityProcess", DisableParallelization = true)]
public class LowPriorityProcessCollection;
