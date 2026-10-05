using System.Globalization;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

public enum CardTone { Waiting, Active, Ok, Warning, Error }

/// What a job card shows, from successive `JobStatus` snapshots (Mac `JobItem.refresh` and the JobCard texts).
public sealed class JobProgress
{
    readonly DateTime started;
    DateTime lastTime;
    long lastBytes;
    long[] lastDest = [];
    JobPhase lastPhase = JobPhase.Waiting;
    BrakeMeter? brakes;

    public JobProgress(DateTime now)
    {
        started = now;
        lastTime = now;
    }

    public JobStatus Status { get; private set; } = new();
    /// Bytes per second, smoothed: card read while copying, slowest destination while verifying.
    public double ReadRate { get; private set; }
    public double[] DestRates { get; private set; } = [];
    public double? Eta { get; private set; }
    /// The element braking the copy (0 = card, i = destination i); only while copying.
    public Brake? Brake { get; private set; }

    public void AttachBrakeMeter(BrakeMeter meter) => brakes = meter;

    public bool IsRunning => Status.Phase != JobPhase.Waiting && !Status.Phase.IsFinished();

    public void Update(JobStatus s, DateTime now)
    {
        double dt = (now - lastTime).TotalSeconds;
        if (s.Copy is { } c && dt > 0.2)
        {
            bool copying = s.Phase == JobPhase.Copying;
            long bytes = copying ? c.ReadBytes : (c.VerifiedBytes.Length > 0 ? c.VerifiedBytes.Min() : 0);
            var dests = copying ? c.WrittenBytes : c.VerifiedBytes;
            if (s.Phase != lastPhase || lastDest.Length != dests.Length)
            {
                ReadRate = 0;
                DestRates = new double[dests.Length];
            }
            else
            {
                ReadRate = Smooth(ReadRate, Math.Max(0, (bytes - lastBytes) / dt));
                var rates = new double[dests.Length];
                for (int i = 0; i < dests.Length; i++) rates[i] = Smooth(DestRates[i], Math.Max(0, (dests[i] - lastDest[i]) / dt));
                DestRates = rates;
                Eta = ReadRate > 1 ? (c.TotalBytes - bytes) / ReadRate : null;
            }
            lastPhase = s.Phase;
            lastBytes = bytes;
            lastDest = dests.ToArray();
            lastTime = now;
        }
        Brake = s.Phase == JobPhase.Copying && s.Copy is { } cc && brakes != null
            ? brakes.Add((now - started).TotalSeconds, cc.ReadBytes, cc.WrittenBytes) ?? Brake
            : null;
        Status = s;
    }

    static double Smooth(double old, double v) => old == 0 ? v : old * 0.6 + v * 0.4;

    /// 0…1 within the current phase.
    public double PhaseProgress => Math.Clamp(RawProgress, 0, 1);

    double RawProgress => Status switch
    {
        { Phase: JobPhase.Copying, Copy: { TotalBytes: > 0 } c } => (double)c.ReadBytes / c.TotalBytes,
        { Phase: JobPhase.Verifying, Copy: { TotalBytes: > 0 } v } =>
            v.VerifiedBytes.Sum() / ((double)v.TotalBytes * Math.Max(1, v.VerifiedBytes.Length)),
        { Phase: JobPhase.Analyzing or JobPhase.Proxies, ClipCount: > 0 } s => (s.ClipIndex + s.ClipFraction) / s.ClipCount,
        { Phase: JobPhase.Done } => 1,
        _ => 0,
    };

    public string StatusText => Status.Phase == JobPhase.Done && Status.CopySucceeded == false ? "Chyba" : Status.Phase.Label();

    /// Failed, or copied with errors — a job the user cancelled is not an error (no error sound or flash).
    public bool IsError => Tone == CardTone.Error;

    public CardTone Tone => Status.Phase switch
    {
        JobPhase.Done => Status.CopySucceeded == false ? CardTone.Error : CardTone.Ok,
        JobPhase.Failed => CardTone.Error,
        JobPhase.Cancelled => CardTone.Warning,
        JobPhase.Waiting => CardTone.Waiting,
        _ => CardTone.Active,
    };

    static string Mb(double bytesPerSecond) => (bytesPerSecond / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s";

    public static string EtaText(double seconds)
    {
        int i = double.IsNaN(seconds) || seconds < 0 ? 0 : (int)Math.Min(seconds, int.MaxValue);
        return i >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{i / 3600}:{i / 60 % 60:00}:{i % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{i / 60}:{i % 60:00}");
    }

    public string DetailText
    {
        get
        {
            var s = Status;
            var eta = Eta is double t ? " · zbývá " + EtaText(t) : "";
            string Dests() => string.Join(" · ", DestRates.Select((r, i) => $"D{i + 1} {Mb(r)}"));
            return s.Phase switch
            {
                JobPhase.Copying => $"Karta {Mb(ReadRate)} · {Dests()}{eta} · {Path.GetFileName(s.Copy?.CurrentFile ?? "")}",
                JobPhase.Verifying => Dests() + eta,
                JobPhase.Analyzing => $"Klip {s.ClipIndex + 1}/{s.ClipCount} · {s.ClipName}",
                JobPhase.Proxies =>
                    $"Proxy {s.ClipIndex + 1}/{s.ClipCount} · {s.ClipName} · {(s.ClipFraction * 100).ToString("0", CultureInfo.InvariantCulture)} %",
                _ => s.Phase.Label() + "…",
            };
        }
    }

    public static string SummaryText(OffloadReport r)
    {
        var parts = new List<string> { "kopie " + Format.Rate(r.TotalBytes, r.CopySeconds) };
        if (r.Verified) parts.Add("ověření " + Format.Rate(r.TotalBytes, r.VerifySeconds));
        if (r.Clips.Count > 0) parts.Add(Texts.Clips(r.Clips.Count));
        if (r.Proxy?.Enabled == true)
        {
            int Count(ProxyStatus st) => r.Clips.Count(c => c.ProxyStatus == st);
            var proxy = $"proxy {Count(ProxyStatus.Done)}";
            if (Count(ProxyStatus.Failed) > 0) proxy += $", {Count(ProxyStatus.Failed)} chyba";
            if (Count(ProxyStatus.Unsupported) > 0) proxy += $", {Count(ProxyStatus.Unsupported)} nepodporováno";
            parts.Add(proxy);
        }
        return string.Join(" · ", parts);
    }
}
