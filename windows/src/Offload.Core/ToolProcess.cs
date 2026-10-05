using System.ComponentModel;
using System.Diagnostics;
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
        process.BeginErrorReadLine();
        if (onStdoutLine != null)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) onStdoutLine(e.Data); };
            process.BeginOutputReadLine();
        }
        if (!pipeStdin) process.StandardInput.Close();
    }

    public void WaitForExit() => process.WaitForExit();

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

    /// Runs to completion and returns stdout.
    public static string Run(string exe, IReadOnlyList<string> args, string? cwd = null)
    {
        using var p = new ToolProcess(exe, args, cwd, pipeStdin: false);
        var output = new StringBuilder();
        p.Start(line => { lock (output) output.Append(line).Append('\n'); });
        p.WaitForExit();
        if (p.ExitCode != 0) throw new ToolError(p.Name, p.ExitCode, p.StderrTail);
        lock (output) return output.ToString();
    }
}
