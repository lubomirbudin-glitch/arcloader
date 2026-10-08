using System.Globalization;
using Offload.Core;
using Offload.Core.Hardware;

namespace Offload.Cli;

/// Status line refreshed twice a second while a job runs.
sealed class ProgressDisplay : IDisposable
{
    readonly OffloadJob job;
    readonly Timer timer;
    readonly Lock gate = new();
    DateTime lastTime = DateTime.UtcNow;
    long lastRead;
    long[] lastWritten = [];
    long[] lastVerified = [];
    JobPhase? lastPhase;

    readonly BrakeMeter? brakes;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    /// chain: the drives in the order card, D1, D2 … (for the "linka plná" check); optional.
    public ProgressDisplay(OffloadJob job, IReadOnlyList<RoleLink>? chain = null)
    {
        this.job = job;
        try
        {
            brakes = new BrakeMeter((long)job.Config.ChunkMB << 20, job.Config.Buffers, chain?.Select(ChainView.LinkCeiling).ToList());
        }
        catch (Exception)
        {
            brakes = null;
        }
        timer = new Timer(_ => Tick(), null, 500, 500);
    }

    /// The status-line name of a chain element: "card", "D1", "D2" …
    public static string ShortName(int element) => element == 0 ? "card" : $"D{element}";

    public void Dispose()
    {
        using (var done = new ManualResetEvent(false))
        {
            timer.Dispose(done);
            done.WaitOne();
        }
        lock (gate) ConsoleLine.Clear();
    }

    // Which element sets the pace (decided in Core from the backlog); optional, never throws.
    string Brake(ProgressSnapshot s)
    {
        try
        {
            if (brakes?.Add(clock.Elapsed.TotalSeconds, s.ReadBytes, s.WrittenBytes) is not { } b) return "";
            return $"brzdí: {ShortName(b.Element)}{(b.LinkFull ? " (linka plná)" : "")}  ";
        }
        catch (Exception)
        {
            return "";
        }
    }

    void Tick()
    {
        lock (gate)
        {
            try
            {
            var st = job.Status();
            var now = DateTime.UtcNow;
            double dt = Math.Max(0.001, (now - lastTime).TotalSeconds);
            if (st.Phase != lastPhase)
            {
                if (lastPhase != null && !Console.IsErrorRedirected) Console.Error.WriteLine();
                lastPhase = st.Phase;
            }
            string line;
            if (st.Copy is { } s && st.Phase is JobPhase.Copying or JobPhase.Verifying or JobPhase.Flushing or JobPhase.Reporting)
            {
                if (lastWritten.Length != s.WrittenBytes.Length)
                {
                    lastWritten = s.WrittenBytes;
                    lastVerified = s.VerifiedBytes;
                }
                string Rate(long current, long before) =>
                    ((current - before) / dt / 1_000_000).ToString("0", CultureInfo.InvariantCulture).PadLeft(4);
                switch (st.Phase)
                {
                    case JobPhase.Copying:
                        double pct = s.TotalBytes > 0 ? s.ReadBytes * 100.0 / s.TotalBytes : 0;
                        var dests = string.Join("  ", s.WrittenBytes.Select((b, i) => $"D{i + 1} {Rate(b, lastWritten[i])}"));
                        // The brake goes before the file name: a narrow console cuts the end of the line.
                        line = $"copy {s.FileIndex + 1}/{s.TotalFiles}  {Format.Bytes(s.ReadBytes)} / {Format.Bytes(s.TotalBytes)}  "
                             + $"{pct,3:0}%  card {Rate(s.ReadBytes, lastRead)} MB/s  {dests} MB/s  {Brake(s)}{Path.GetFileName(s.CurrentFile)}";
                        break;
                    case JobPhase.Verifying:
                        line = "verify  " + string.Join("  ", s.VerifiedBytes.Select((b, i) =>
                            $"D{i + 1} {b * 100.0 / Math.Max(s.TotalBytes, 1),3:0}% {Rate(b, lastVerified[i])} MB/s"));
                        break;
                    default:
                        line = st.Phase.ToString().ToLowerInvariant() + "…";
                        break;
                }
                if (s.Errors > 0) line += $"  ERRORS: {s.Errors}";
                lastRead = s.ReadBytes;
                lastWritten = s.WrittenBytes;
                lastVerified = s.VerifiedBytes;
            }
            else if (st.Phase is JobPhase.Analyzing or JobPhase.Proxies)
            {
                line = $"{st.Phase.ToString().ToLowerInvariant()} {st.ClipIndex + 1}/{st.ClipCount}  {st.ClipFraction * 100,3:0}%  {st.ClipName}";
                // R5 (plan 10): "can be removed" once copied; formatting only after the verification.
                if (st.SafeToFormat) line += "  (card safe to format)";
                else if (st.SafeToEject) line += "  (card can be removed — verifying, do not format)";
            }
            else if (st.Phase == JobPhase.Finishing)
            {
                line = $"finishing verify  {Format.Bytes(st.VerifiedBytes)} / {Format.Bytes(st.VerifyTotal)}";
            }
            else
            {
                line = st.Phase.ToString().ToLowerInvariant();
            }
            ConsoleLine.Write(line);
            lastTime = now;
            }
            catch (Exception)
            {
                // Progress display is optional; never take the copy down.
            }
        }
    }
}
