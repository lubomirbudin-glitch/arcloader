using Offload.Core.Notify;

namespace Offload.Core.Desktop;

/// One built-in LUT in "LUT podle kamery": "ARRI · LogC4", "LogC4 / AWG4 → Rec.709" and "použito" for the one chosen.
public sealed record LutRow(string Id, string Title, string Detail, string? Marker);

/// The settings page (UI návrh obr. 4), without a window.
public static class SettingsTexts
{
    /// Plan 10 (R11), Nastavení → Ověření.
    public const string VerifyInProxyLabel = "Ověřit primární disk během proxy";
    public const string VerifyInProxyNote =
        "Primární disk se ověří při čtení pro proxy, ostatní souběžně na pozadí — bez druhého čtení navíc. Kartu lze vyjmout po kopii, formátovat až po ověření všech disků. Bez proxy se ověřuje zpětným čtením.";
    public const string VerifyPrimaryFirstLabel = "Primární je první cíl (jinak nejrychlejší disk změřený při kopii)";

    /// Automatické načtení disků (2026-10-09), Nastavení → Struktura složek.
    public const string AutoDestinationsLabel = "Automaticky přidávat připojené disky jako cíle";
    public const string AutoDestinationsNote =
        "Jen externí disky USB — karta, čtečka karet ani interní disk se jako cíl nikdy nepřidá. Odebraný disk se znovu sám nepřidá; přidaný disk po odpojení z cílů zase zmizí. Plný disk nebo disk jen pro čtení se nepřidá.";
    public const string AutoCardsLabel = "Automaticky označovat karty ve čtečkách jako zdroje";
    public const string AutoCardsNote = "Karta se strukturou kamery nebo neprázdná karta ve čtečce se po vložení sama zaškrtne v Kartách.";

    /// "Zapomenout odebrané disky (2)".
    public static string ForgetRemovedLabel(int count) => $"Zapomenout odebrané disky ({count})";

    /// Kopie reportu i na počítači (2026-10-09), Nastavení → Report.
    public const string LocalReportLabel = "Kopie reportu i na tomto počítači";
    public const string LocalReportNote =
        "Po každé kartě se _REPORT a ascmhl zkopírují do <složka>\\<datum>\\<karta>\\ a obnoví se při každém přepisu reportu (analýza, proxy, ověření, doplnění, oprava, denní report) — disky s materiálem se odevzdávají dál, report zůstane tady. Média se nikdy nekopírují; když se kopie nepovede, úloha běží dál.";

    /// "≈ 75 MB / minutu" under the bitrate (video only; the audio adds little).
    public static string BitrateHint(int mbps) => $"≈ {mbps * 60 / 8} MB / minutu";

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
        return new LutRow(l.Id, $"{Maker(l.Maker)} · {CameraLabel.ShortCurve(l.Curve)}", name, used ? "použito" : null);
    }

    /// "Windows: zapnuto · telefon (ntfy): vypnuto" / "… telefon (ntfy): film-x-tajne".
    public static string NotifySummary(NotifySettings n) =>
        $"Windows: {(n.Windows ? "zapnuto" : "vypnuto")} · telefon (ntfy): {(n.Ntfy.Enabled && n.Ntfy.Topic.Length > 0 ? n.Ntfy.Topic : "vypnuto")}";

    /// "Výřez" in the burn-in preview: "celý obraz", or "1.85 · 90 %".
    public static string FrameText(ProxySettings p) => p.FrameLabel ?? "celý obraz";

    /// Where the crop's frame lines sit in a 16:9 picture (fractions of its width and height); null = no crop.
    public static (double X, double Y, double W, double H)? Frame(ProxySettings p) =>
        p.FrameCrop(1920, 1080) is { } c ? (c.X / 1920.0, c.Y / 1080.0, c.W / 1920.0, c.H / 1080.0) : null;
}
