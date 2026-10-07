namespace Offload.Core.Space;

/// Bytes one destination volume needs: the card (with the per-file overhead), proxies, report.
public sealed record SpaceNeed(long Card, long Proxy, long Report)
{
    public static readonly SpaceNeed None = new(0, 0, 0);
    public long Total => Card + Proxy + Report;
    public static SpaceNeed operator +(SpaceNeed a, SpaceNeed b) => new(a.Card + b.Card, a.Proxy + b.Proxy, a.Report + b.Report);
}

/// What a job needs besides the card itself, per destination (spec 2.5): proxies (on the first destination, on all with
/// "Kopírovat proxy na všechny cíle"), the report everywhere, the proxies once more in the proxy folder.
public sealed record SpaceExtra(long Proxy, long Report, bool ProxyOnAllDestinations, string? ProxyFolder)
{
    public SpaceNeed ForDestination(int index, long card) => new(card, index == 0 || ProxyOnAllDestinations ? Proxy : 0, Report);
}

/// The formulas of spec 2.5 — pure functions, no disk.
public static class SpaceEstimator
{
    /// The card on one destination: its bytes + 64 KiB per file + 64 MiB (the engine's formula since plan 1).
    public static long CardBytes(long totalBytes, int files) => totalBytes + files * 64L * 1024 + (64L << 20);

    /// HTML/PDF with thumbnails, MHL, lists: 32 MiB + 1 MiB per clip.
    public static long ReportBytes(int clips) => (32L << 20) + clips * (1L << 20);

    /// The source data rate assumed before the copy — on the low side, so the clip is assumed longer and the proxy
    /// bigger (careful): MP4/MOV/MTS 50 Mb/s, MXF 100, BRAW 150, R3D 200, Canon RAW 500, ARRIRAW 1 000.
    public static double SourceMbps(Clip clip) => clip.Format switch
    {
        ClipFormat.Braw => 150,
        ClipFormat.R3d => 200,
        ClipFormat.CanonRaw => 500,
        ClipFormat.Arriraw => 1000,
        _ => Path.GetExtension(clip.Path).Equals(".mxf", StringComparison.OrdinalIgnoreCase) ? 100 : 50,
    };

    /// The proxy's data rate: H.264/HEVC the set bitrate × 1.1 + each audio stream's AAC rate (64 kb/s a channel,
    /// 128-512 kb/s, as `Encoders.AacKbps`); ProRes Apple's 1080p30 rates (Proxy 45, LT 102, 422 147, HQ 220 Mb/s) ×
    /// (height/1080)² + 16-bit PCM at 48 kHz, 0.768 Mb/s a channel of every stream (ruling M6 amends spec 2.5).
    public static double ProxyMbps(ProxySettings p, IReadOnlyList<int> audioStreams, int height) => p.Codec switch
    {
        ProxyCodec.H264 or ProxyCodec.Hevc => p.BitrateMbps * 1.1 + audioStreams.Sum(Encoders.AacKbps) / 1000.0,
        _ => (p.Codec switch { ProxyCodec.ProresProxy => 45, ProxyCodec.ProresLT => 102, ProxyCodec.Prores422 => 147, _ => 220 })
             * Math.Pow(height / 1080.0, 2) + 0.768 * audioStreams.Sum(),
    };

    /// `audioTracks` mono streams.
    public static double ProxyMbps(ProxySettings p, int audioTracks, int height) =>
        ProxyMbps(p, Enumerable.Repeat(1, audioTracks).ToList(), height);

    /// A clip that a proxy can be made of with these tools (a missing decoder makes none).
    public static bool Renderable(Clip clip, ExternalTools resolved) => resolved.Ffmpeg != null && clip.Format switch
    {
        ClipFormat.Video => true,
        ClipFormat.Braw => resolved.BrawDecoder != null,
        ClipFormat.R3d => resolved.Redline != null,
        ClipFormat.Arriraw => resolved.ArtCmd != null,
        _ => false,
    };

    /// Σ over the clips of length × proxy rate; camera proxies (when skipped) and clips without a decoder count 0.
    /// `info`: after the analysis the real duration, height and audio (spec 2.5 "přepočet před renderem").
    public static long ProxyBytes(IEnumerable<Clip> clips, ProxySettings p, Func<Clip, bool> renderable, Func<Clip, ClipInfo?>? info = null)
    {
        if (!p.Enabled) return 0;
        double bytes = 0;
        foreach (var c in clips)
        {
            if ((c.IsCameraProxy && p.SkipCameraProxies) || !renderable(c)) continue;
            var i = info?.Invoke(c);
            double seconds = i?.Duration ?? c.Bytes * 8.0 / (SourceMbps(c) * 1e6);
            // Every audio stream becomes a track of the proxy (R14); before the analysis one stereo stream is assumed,
            // a probed clip without the stream list (a RAW intermediate) has one stream of its channels.
            IReadOnlyList<int> audio = !p.Audio ? [] : i == null ? [2] : i.AudioStreams.Count > 0 ? i.AudioStreams : i.AudioChannels > 0 ? [i.AudioChannels] : [];
            int height = Math.Min(p.Height, i?.Height ?? p.Height);
            bytes += seconds * ProxyMbps(p, audio, height) * 1e6 / 8;
        }
        return (long)Math.Ceiling(bytes);
    }

    /// Before the copy, from the card's clip names only (R10).
    public static SpaceExtra Extra(IReadOnlyList<Clip> clips, ProxySettings p, ExternalTools resolved) =>
        new(ProxyBytes(clips, p, c => Renderable(c, resolved)), ReportBytes(clips.Count), p.CopyToAllDestinations, p.Enabled ? p.ExtraFolder : null);
}

/// A path that needs room (a destination, the proxy folder), how messages name it, and what it needs.
public sealed record SpacePlace(string Path, string Label, SpaceNeed Need);

public enum SpaceSeverity { Warning, Blocking }

/// `Volume`: which volume (two destinations on one volume add up); `Label`: how the messages name it.
public sealed record VolumeDemand(string Volume, string Label, SpaceNeed Need);

public sealed record SpaceProblem(SpaceSeverity Severity, string Label, SpaceNeed Need, long Free, string Text);

/// One volume of a plan: what it needs in total and what it has free.
public sealed record SpaceLine(string Label, SpaceNeed Need, long Free);

/// Spec 2.5 "Součet": per volume, every demand (selected cards, queued jobs, the rest of running ones) against the free
/// space. Blocking when it does not fit, Warning when less than 5 % of the need would be left (R11). Memory only.
public static class SpacePlanner
{
    /// Which volume a path is on, so demands on one volume add up; a path that cannot be asked counts on its own.
    /// Touches the disk — never on the UI thread.
    public static string VolumeKey(string path) => NativeIO.VolumeId(path) ?? Path.GetFullPath(path).ToLowerInvariant();

    public static IReadOnlyList<SpaceProblem> Check(IEnumerable<VolumeDemand> demands, IReadOnlyDictionary<string, long> free)
    {
        var problems = new List<SpaceProblem>();
        foreach (var g in demands.GroupBy(d => d.Volume, StringComparer.OrdinalIgnoreCase))
        {
            if (!free.TryGetValue(g.Key, out var f)) continue;   // not known yet: never blocks
            var need = g.Select(d => d.Need).Aggregate(SpaceNeed.None, (a, b) => a + b);
            var label = string.Join(", ", g.Select(d => d.Label).Distinct(StringComparer.OrdinalIgnoreCase));
            if (need.Total > f)
                problems.Add(new SpaceProblem(SpaceSeverity.Blocking, label, need, f, Msg.NoSpace(label, need, f)));
            else if ((f - need.Total) * 20 < need.Total)
                problems.Add(new SpaceProblem(SpaceSeverity.Warning, label, need, f, Msg.LowSpace(label, f - need.Total)));
        }
        return problems.OrderByDescending(p => p.Severity).ToList();
    }

    /// The one place list both the engine (before the copy) and the job (before the proxies) build: the destinations,
    /// and the proxy folder as a place of its own when it exists. `card` 0 and no report = only the proxies.
    public static List<SpacePlace> Places(IReadOnlyList<string> roots, SpaceExtra? extra, long card, Func<string, string> label)
    {
        var places = roots.Select((root, i) => new SpacePlace(root, label(root), extra?.ForDestination(i, card) ?? new SpaceNeed(card, 0, 0))).ToList();
        if (extra is { ProxyFolder: string folder, Proxy: > 0 } && Directory.Exists(folder))
            places.Add(new SpacePlace(folder, label(folder), new SpaceNeed(0, extra.Proxy, 0)));
        return places;
    }

    /// Free space of every volume the places are on (`freeOf` unknown = 0, so it blocks rather than risks), then the check.
    /// Touches the disk — never on the UI thread.
    public static (IReadOnlyList<SpaceProblem> Problems, IReadOnlyList<SpaceLine> Lines) CheckPlaces(
        IReadOnlyList<SpacePlace> places, Func<string, long?> freeOf)
    {
        var demands = places.Select(p => new VolumeDemand(VolumeKey(p.Path), p.Label, p.Need)).ToList();
        var free = places.Select((p, i) => (Key: demands[i].Volume, p.Path))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => freeOf(g.First().Path) ?? 0, StringComparer.OrdinalIgnoreCase);
        return (Check(demands, free), Lines(demands, free));
    }

    /// Per volume with a known free space: the summed need (the CLI's --dry-run breakdown).
    public static IReadOnlyList<SpaceLine> Lines(IEnumerable<VolumeDemand> demands, IReadOnlyDictionary<string, long> free) =>
        demands.GroupBy(d => d.Volume, StringComparer.OrdinalIgnoreCase)
            .Where(g => free.ContainsKey(g.Key))
            .Select(g => new SpaceLine(string.Join(", ", g.Select(d => d.Label).Distinct(StringComparer.OrdinalIgnoreCase)),
                g.Select(d => d.Need).Aggregate(SpaceNeed.None, (a, b) => a + b), free[g.Key]))
            .ToList();
}
