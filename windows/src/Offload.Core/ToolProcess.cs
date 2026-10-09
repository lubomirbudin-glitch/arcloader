using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Offload.Core;

/// Runs a child process: stdout as lines (or as a binary stream), the last 4000 characters of stderr
/// kept for error messages. Dispose kills the whole process tree if it is still running.
internal sealed class ToolProcess : IDisposable
{
    readonly Process process;
    readonly bool pipeStdin;
    readonly StringBuilder stderr = new();
    readonly Lock gate = new();

    public string Name { get; }
    /// Every stderr line as it comes (plan 10: the proxy's content check), besides the tail kept for errors.
    public Action<string>? StderrLine { get; set; }

    public ToolProcess(string exe, IEnumerable<string> args, string? workingDirectory, bool pipeStdin)
    {
        Name = Path.GetFileNameWithoutExtension(exe);
        this.pipeStdin = pipeStdin;
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        if (workingDirectory != null) psi.WorkingDirectory = workingDirectory;
        foreach (var a in args) psi.ArgumentList.Add(a);
        process = new Process { StartInfo = psi };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (gate)
            {
                stderr.Append(e.Data).Append('\n');
                if (stderr.Length > 4000) stderr.Remove(0, stderr.Length - 4000);
            }
            StderrLine?.Invoke(e.Data);
        };
    }

    public string StderrTail
    {
        get { lock (gate) return stderr.ToString(); }
    }

    /// Binary stdout — only when Start was called without a line handler.
    public Stream Stdout => process.StandardOutput.BaseStream;

    public Stream Stdin => process.StandardInput.BaseStream;

    public int ExitCode => process.ExitCode;

    public int Id => process.Id;

    /// CPU class and I/O priority hint (2 = normal) of the running tool — for tests.
    internal (ProcessPriorityClass Cpu, int Io) Priority() => (process.PriorityClass, Win32.ProcessIoPriority(process));

    public void Start(Action<string>? onStdoutLine)
    {
        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            throw new ToolError(Name, -1, e.Message);
        }
        // Normal CPU and I/O priority whatever Offload itself runs at (Win32.RaiseToNormal): proxies and RAW decodes are
        // what the user waits for. (Plan 10's verify readers lower only their own threads — nothing a child inherits.)
        Win32.RaiseToNormal(process);
        process.BeginErrorReadLine();
        if (onStdoutLine != null)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) onStdoutLine(e.Data); };
            process.BeginOutputReadLine();
        }
        if (!pipeStdin) process.StandardInput.Close();
    }

    public void WaitForExit() => process.WaitForExit();

    /// After the process exited: waits (bounded) until the redirected stderr reached EOF, so `StderrTail` holds the last
    /// lines. `WaitForExit(timeout)` returns before the asynchronous readers are done — under load the final line was missing.
    public void DrainStderr(int milliseconds = 5000)
    {
        try
        {
            if (process.HasExited) Task.Run(process.WaitForExit).Wait(milliseconds);
        }
        catch (Exception e) when (e is InvalidOperationException or AggregateException)
        {
        }
    }

    public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);

    public void Kill()
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
        catch (AggregateException) { }
    }

    public void Dispose()
    {
        Kill();
        process.Dispose();
    }

    /// Runs to completion and returns stdout. With `timeout`, a process still running after it is killed (with its
    /// children) and a ToolError says so — a hung tool must never hang the job.
    public static string Run(string exe, IReadOnlyList<string> args, string? cwd = null, TimeSpan? timeout = null)
    {
        using var p = new ToolProcess(exe, args, cwd, pipeStdin: false);
        var output = new StringBuilder();
        p.Start(line => { lock (output) output.Append(line).Append('\n'); });
        if (timeout is TimeSpan limit && !p.WaitForExit((int)limit.TotalMilliseconds))
        {
            p.Kill();
            throw new ToolError(p.Name, -1, $"neodpověděl do {limit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s — ukončen");
        }
        p.WaitForExit();
        if (p.ExitCode != 0) throw new ToolError(p.Name, p.ExitCode, p.StderrTail);
        lock (output) return output.ToString();
    }

    /// For tools that log to stdout and may report a failure there with exit code 0 (art-cmd, REDline): runs to completion
    /// and returns the exit code with stdout and stderr together, in the order the lines came; the caller decides what
    /// failed. A RAW decode takes minutes, so it is looked at every 200 ms: when `isCancelled` turns true, or after
    /// `timeout`, the tool is killed with its children and a ToolError (exit -1) says why.
    public static (int ExitCode, string Output) RunCombined(string exe, IReadOnlyList<string> args, string? cwd = null, TimeSpan? timeout = null,
        Func<bool>? isCancelled = null)
    {
        using var p = new ToolProcess(exe, args, cwd, pipeStdin: false);
        var output = new StringBuilder();
        void Add(string line)
        {
            lock (output) output.Append(line).Append('\n');
        }
        p.StderrLine = Add;
        long started = Environment.TickCount64;
        p.Start(Add);
        while (!p.WaitForExit(200))
        {
            bool cancelled = isCancelled?.Invoke() == true;
            if (!cancelled && !(timeout is TimeSpan limit && Environment.TickCount64 - started > limit.TotalMilliseconds)) continue;
            p.Kill();
            // Bounded: the caller removes the tool's folder next, which a dying process may still hold.
            p.WaitForExit(5000);
            throw new ToolError(p.Name, -1, cancelled
                ? "cancelled"
                : $"neodpověděl do {timeout!.Value.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s — ukončen");
        }
        p.WaitForExit();   // without a limit it also waits until both pipes reached their end
        lock (output) return (p.ExitCode, output.ToString());
    }
}
