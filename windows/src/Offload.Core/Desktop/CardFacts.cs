using Offload.Core.Hardware;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core.Desktop;

/// "ALEXA 35 · LogC4" (UI návrh, nová věc 6): the camera and its log curve, from one clip of the card.
public sealed record CameraLabel(string? Camera, string? Curve)
{
    /// "ALEXA 35 · LogC4", "ALEXA 35", "S-Log3", or null when nothing is known.
    public string? Text
    {
        get
        {
            var parts = new[] { Camera, Curve }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
    }

    static readonly string[] Makers = ["Blackmagic Design", "Blackmagic", "ARRI", "Sony", "Canon", "Panasonic", "FUJIFILM", "Fujifilm", "DJI", "RED"];

    /// The model without its maker when the model says enough on its own: "ARRI ALEXA 35" → "ALEXA 35".
    public static string? Model(string? camera)
    {
        if (string.IsNullOrWhiteSpace(camera)) return null;
        var c = camera.Trim();
        foreach (var m in Makers)
            if (c.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase) && c.Length - m.Length > 3) return c[(m.Length + 1)..].Trim();
        return c;
    }

    /// The curve of a built-in LUT as people say it: "ARRI LogC4" → "LogC4", "Blackmagic Film Generation 5" → "Film Gen 5",
    /// "Canon Log 3" → "C-Log3".
    public static string ShortCurve(string curve)
    {
        var c = curve.Trim();
        if (c.StartsWith("ARRI ", StringComparison.Ordinal)) c = c["ARRI ".Length..];
        c = c.Replace("Blackmagic Film Generation ", "Film Gen ", StringComparison.Ordinal);
        if (c.StartsWith("Canon Log ", StringComparison.Ordinal)) c = "C-Log" + c["Canon Log ".Length..];
        return c;
    }
}

/// The camera label of a card at insertion (off the UI thread, reading only): the first clip of the card that is not a
/// camera proxy. A video clip is asked with ffprobe (at most 5 s) and the automatic LUT choice names its curve; a RAW
/// clip is named by its format (its decoder is too slow for a card that was just put in).
public static class CameraHints
{
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// `probe`: ffprobe of a file (tests pass a fake); null = FFProbe with ProbeTimeout when the tools have ffprobe.
    public static CameraLabel? Probe(CardInsight insight, ExternalTools resolvedTools, LutCatalog catalog, CancellationToken ct,
        Func<string, ClipInfo?>? probe = null)
    {
        if (insight.Error != null || insight.Clips.FirstOrDefault(c => !c.IsCameraProxy) is not { } clip) return null;
        string? raw = clip.Format switch
        {
            ClipFormat.Braw => "Blackmagic RAW",
            ClipFormat.R3d => "RED",
            ClipFormat.Arriraw => "ARRIRAW",
            ClipFormat.CanonRaw => "Canon RAW",
            _ => null,
        };
        if (raw != null) return new CameraLabel(raw, null);
        if (clip.Format != ClipFormat.Video) return null;
        ct.ThrowIfCancellationRequested();
        var path = Layout.Join(insight.Source, clip.Path);
        probe ??= resolvedTools.Ffprobe is string ffprobe ? p => FFProbe.Probe(ffprobe, p, ProbeTimeout) : null;
        ClipInfo? info;
        try
        {
            info = probe?.Invoke(path);
        }
        catch (Exception e) when (e is ToolError or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            info = null;   // a clip ffprobe cannot read: the label just stays empty
        }
        var lut = LutResolver.Resolve(clip, info, path, new ProxySettings { LutMode = LutMode.Auto }, catalog);
        var curve = catalog.Find(lut.Id)?.Curve is string c ? CameraLabel.ShortCurve(c) : null;
        var label = new CameraLabel(CameraLabel.Model(info?.Camera), curve);
        return label.Text == null ? null : label;
    }
}

/// How a disk is connected, in a word or two (UI návrh, nová věc 6): from the HW diagnostics (`DriveLinks`).
public static class ConnectionLabel
{
    /// "reader USB 3 · 5 Gb/s", "USB 2.0", "NVMe", "SATA", "SD reader", "network" — or null when unknown. Thunderbolt is not
    /// told apart by Windows: a disk behind it shows by its bus (NVMe, USB).
    public static string? Short(DriveLink link, bool card)
    {
        string? bus = link.Usb is { } u ? Usb(u.Link) : link.Bus switch
        {
            StorageBus.Sd or StorageBus.Mmc => card ? "SD reader" : "SD",
            StorageBus.Nvme => "NVMe",
            StorageBus.Sata => "SATA",
            StorageBus.Network => "network",
            StorageBus.Raid => "RAID",
            StorageBus.Sas => "SAS",
            StorageBus.Scsi => "SCSI",
            StorageBus.Virtual => "virtual disk",
            StorageBus.Usb => "USB",
            _ => null,
        };
        if (bus == null) return null;
        return card && link.Bus == StorageBus.Usb ? $"reader {bus}" : bus;
    }

    static string? Usb(UsbLinkSpeed s) => s switch
    {
        UsbLinkSpeed.Low or UsbLinkSpeed.Full => "USB 1.1",
        UsbLinkSpeed.High => "USB 2.0",
        UsbLinkSpeed.Super => "USB 3 · 5 Gb/s",
        UsbLinkSpeed.SuperPlus => "USB 3 · 10 Gb/s",
        _ => "USB",
    };
}

/// The destination tile (UI návrh obr. 1): its free-space line and the space bar.
public static class DestinationTile
{
    /// "7.20 TB free of 8 TB · NTFS · NVMe".
    public static string FreeText(long free, long? total, string filesystem, string? connection)
    {
        var parts = new List<string> { total is long t && t > 0 ? $"{Format.Bytes3(free)} free of {Format.BytesShort(t)}" : $"{Format.Bytes3(free)} free" };
        if (filesystem.Length > 0) parts.Add(filesystem);
        if (connection != null) parts.Add(connection);
        return string.Join(" · ", parts);
    }

    /// The compact list while a copy runs: "7.20 TB free".
    public static string CompactFree(long free) => $"{Format.Bytes3(free)} free";

    /// The bar: grey = used, blue = what the cards ticked now need there (with proxies and the report); yellow / red with
    /// the space warning / problem. Shares of the capacity, the need cut to what is free.
    public static (double Used, double Need) Bar(long total, long free, long need)
    {
        if (total <= 0) return (0, 0);
        double used = Math.Clamp((double)(total - free) / total, 0, 1);
        double want = Math.Clamp((double)Math.Max(0, need) / total, 0, 1 - used);
        // A need too small to see still shows as a sliver.
        if (need > 0 && want < 0.01) want = Math.Min(0.01, 1 - used);
        return (used, want);
    }

    /// What the cards ticked now need on one volume (their demands from SpaceCheck, summed).
    public static long Need(IEnumerable<VolumeDemand> demands, string volume) =>
        demands.Where(d => string.Equals(d.Volume, volume, StringComparison.OrdinalIgnoreCase)).Sum(d => d.Need.Total);

    /// How the bar is coloured on this volume: the space check's verdict for it, if any.
    public static SpaceSeverity? Severity(IReadOnlyList<SpaceProblem> problems, string label) =>
        problems.Where(p => p.Label.Split(", ").Contains(label, StringComparer.OrdinalIgnoreCase)).Select(p => (SpaceSeverity?)p.Severity).Max();
}
