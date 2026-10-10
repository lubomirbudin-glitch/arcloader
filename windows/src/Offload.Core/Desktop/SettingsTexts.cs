using Offload.Core.Notify;

namespace Offload.Core.Desktop;

/// One built-in LUT in "LUT by camera": "ARRI · LogC4", "LogC4 / AWG4 → Rec.709" and "in use" for the one chosen.
public sealed record LutRow(string Id, string Title, string Detail, string? Marker);

/// The settings page (UI návrh obr. 4), without a window.
public static class SettingsTexts
{
    /// Plan 10 (R11), Settings → Verification.
    public const string VerifyInProxyLabel = "Verify the primary disk during proxies";
    public const string VerifyInProxyNote =
        "The primary disk is verified while it is read for the proxies, the others at the same time in the background — no extra second read. The card is safe to eject after the copy and safe to format once all disks are verified. Without proxies, verification reads back.";
    public const string VerifyPrimaryFirstLabel = "The primary is the first destination (otherwise the fastest disk measured during the copy)";

    /// Disks loaded automatically (2026-10-09), Settings → Folder structure.
    public const string AutoDestinationsLabel = "Automatically add connected disks as destinations";
    public const string AutoDestinationsNote =
        "External USB disks only — a card, a card reader or an internal disk is never added as a destination. A disk you removed is not added again by itself; an added disk disappears from the destinations when it is disconnected. A full or read-only disk is not added.";
    public const string AutoCardsLabel = "Automatically select cards in readers as sources";
    public const string AutoCardsNote = "A card with a camera folder structure, or a non-empty card in a reader, is ticked in Cards by itself when inserted.";

    /// "Forget removed disks (2)".
    public static string ForgetRemovedLabel(int count) => $"Forget removed disks ({count})";

    /// A copy of the report on this computer too (2026-10-09), Settings → Report.
    public const string LocalReportLabel = "Copy of the report on this computer too";
    public const string LocalReportNote =
        "After every card, _REPORT and ascmhl are copied to <folder>\\<date>\\<card>\\ and refreshed whenever the report is rewritten (analysis, proxies, verification, append, repair, day report) — the media disks get handed on, the report stays here. Media are never copied; if the copy fails, the job carries on.";

    /// "≈ 75 MB / minute" under the bitrate (video only; the audio adds little).
    public static string BitrateHint(int mbps) => $"≈ {mbps * 60 / 8} MB / minute";

    static string Maker(string maker) => maker.StartsWith("Blackmagic", StringComparison.Ordinal) ? "Blackmagic" : maker;

    public static LutRow Row(BuiltinLut l, ProxySettings p)
    {
        var name = l.Name;
        foreach (var prefix in new[] { l.Maker + " ", Maker(l.Maker) + " " })
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name[prefix.Length..];
                break;
            }
        bool used = LutResolver.EffectiveMode(p) == LutMode.Builtin && p.LutBuiltin == l.Id;
        return new LutRow(l.Id, $"{Maker(l.Maker)} · {CameraLabel.ShortCurve(l.Curve)}", name, used ? "in use" : null);
    }

    /// "Windows: on · phone (ntfy): off" / "… phone (ntfy): film-x-secret".
    public static string NotifySummary(NotifySettings n) =>
        $"Windows: {(n.Windows ? "on" : "off")} · phone (ntfy): {(n.Ntfy.Enabled && n.Ntfy.Topic.Length > 0 ? n.Ntfy.Topic : "off")}";

    /// "Crop" in the burn-in preview: "full frame", or "1.85 · 90 %".
    public static string FrameText(ProxySettings p) => p.FrameLabel ?? "full frame";

    /// Where the crop's frame lines sit in a 16:9 picture (fractions of its width and height); null = no crop.
    public static (double X, double Y, double W, double H)? Frame(ProxySettings p) =>
        p.FrameCrop(1920, 1080) is { } c ? (c.X / 1920.0, c.Y / 1080.0, c.W / 1920.0, c.H / 1080.0) : null;
}
