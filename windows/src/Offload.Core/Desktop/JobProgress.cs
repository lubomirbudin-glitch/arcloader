using System.Globalization;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

public enum CardTone { Waiting, Active, Ok, Warning, Error }

/// What a job card shows, from successive `JobStatus` snapshots (Mac `JobItem.refresh` and the JobCard texts).
public sealed class JobProgress
{
    readonly DateTime started;
    DateTime? firstActive, ended, stage2Start;
    TimeSpan pausedBeforeStage2;
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

    /// How long stage 2 (clips, proxies, PDF, day report) took without its pauses; null without a stage 2, before the end,
    /// and for a job that did not end Done (a cancelled or failed stage 2 would teach RateMemory a far too low cost, M2).
    public TimeSpan? Stage2Elapsed
    {
        get
        {
            if (stage2Start is not DateTime a || ended is not DateTime e || Status.Phase != JobPhase.Done) return null;
            var t = e - a - (Status.PausedTotal - pausedBeforeStage2);
            return t > TimeSpan.Zero ? t : TimeSpan.Zero;
        }
    }

    /// How long the job took, without its pauses — from the first tick it ran to the first tick it was finished; null
    /// before (the "Celkem" of the finished card).
    public TimeSpan? Elapsed
    {
        get
        {
            // Never seen running (the first tick found it finished): the report's times say it instead.
            if (firstActive is not DateTime a || ended is not DateTime e || a == e) return null;
            var t = e - a - Status.PausedTotal;
            return t > TimeSpan.Zero ? t : TimeSpan.Zero;
        }
    }

    public bool IsRunning => Status.Phase != JobPhase.Waiting && !Status.Phase.IsFinished();

    public void Update(JobStatus s, DateTime now)
    {
        if (firstActive == null && s.Phase != JobPhase.Waiting) firstActive = now;
        if (ended == null && s.Phase.IsFinished()) ended = now;
        if (stage2Start == null && s.Phase is JobPhase.Analyzing or JobPhase.Proxies)
        {
            stage2Start = now;
            pausedBeforeStage2 = s.PausedTotal;
        }
        if (s.Paused)
        {
            // Nothing moves: no rate and no ETA, the brake forgets its window; after Pokračovat the rates are measured
            // from the moment the job runs again, never over the pause.
            ReadRate = 0;
            DestRates = new double[DestRates.Length];
            Eta = null;
            Brake = null;
            brakes?.Reset();
            if (s.Copy is { } held)
            {
                bool copying = s.Phase == JobPhase.Copying;
                lastBytes = copying ? held.ReadBytes : (held.VerifiedBytes.Length > 0 ? held.VerifiedBytes.Min() : 0);
                lastDest = (copying ? held.WrittenBytes : held.VerifiedBytes).ToArray();
            }
            lastPhase = s.Phase;
            lastTime = now;
            Status = s;
            return;
        }
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
            v.ToVerifyTotal == 0 ? 1 : v.VerifiedBytes.Sum() / (double)v.ToVerifyTotal,
        { Phase: JobPhase.Analyzing or JobPhase.Proxies, ClipCount: > 0 } s => (s.ClipIndex + s.ClipFraction) / s.ClipCount,
        { Phase: JobPhase.Done } => 1,
        _ => 0,
    };

    public string StatusText => PauseLabel ?? (Status.Phase == JobPhase.Done && Status.CopySucceeded == false ? "Chyba" : Status.Phase.Label());

    /// "Pozastaveno" once the job stands, "Pozastavuje se" until then; null while it runs.
    string? PauseLabel => !Status.Paused ? null : Status.PauseHeld ? "Pozastaveno" : "Pozastavuje se";

    /// What the card says while paused, or null: stage 2 finishes the clip it is on first.
    public string? PauseText => Status switch
    {
        { Paused: false } => null,
        { PauseHeld: true } => "Pozastaveno — Pokračovat spustí úlohu od místa, kde stojí.",
        { Phase: JobPhase.Proxies, ClipName: { Length: > 0 } clip } => $"Pozastaví se po proxy klipu {clip}…",
        { Phase: JobPhase.Analyzing, ClipName: { Length: > 0 } clip } => $"Pozastaví se po klipu {clip}…",
        _ => "Pozastavuje se…",
    };

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
            if (r.Clips.Count(c => c.OriginalLighter) is int lighter and > 0) proxy += $", {lighter} bez proxy (lehčí originál)";
            parts.Add(proxy);
        }
        return string.Join(" · ", parts);
    }
}
