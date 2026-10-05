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

    readonly IReadOnlyList<RoleLink>? chain;

    public ProgressDisplay(OffloadJob job, IReadOnlyList<RoleLink>? chain = null)
    {
        this.job = job;
        this.chain = chain;
        timer = new Timer(_ => Tick(), null, 500, 500);
    }

    public void Dispose()
    {
        using (var done = new ManualResetEvent(false))
        {
            timer.Dispose(done);
            done.WaitOne();
        }
        lock (gate) ConsoleLine.Clear();
    }

    // Which link of the chain sets the pace right now; optional, never throws.
    string Bottleneck(ProgressSnapshot s, double dt)
    {
        try
        {
            if (chain == null || chain.Count != 1 + s.WrittenBytes.Length || lastWritten.Length != s.WrittenBytes.Length) return "";
            var rates = new List<double> { (s.ReadBytes - lastRead) / dt / 1_000_000 };
            rates.AddRange(s.WrittenBytes.Select((b, i) => (b - lastWritten[i]) / dt / 1_000_000));
            return ChainView.Bottleneck(ChainView.Build(chain, rates)) is string slow ? $"  | brzdí: {slow}" : "";
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
                        line = $"copy {s.FileIndex + 1}/{s.TotalFiles}  {Format.Bytes(s.ReadBytes)} / {Format.Bytes(s.TotalBytes)}  "
                             + $"{pct,3:0}%  card {Rate(s.ReadBytes, lastRead)} MB/s  {dests} MB/s  {Path.GetFileName(s.CurrentFile)}";
                        line += Bottleneck(s, dt);
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
                if (st.SafeToEject) line += "  (card safe to eject)";
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
