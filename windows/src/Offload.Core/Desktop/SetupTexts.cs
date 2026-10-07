namespace Offload.Core.Desktop;

/// One row of "3 · Po zkopírování": the green tick (or grey ring) and the value on the right; `Warn`: the yellow row of
/// switched-off verification.
public sealed record AfterCopyRow(string Label, string Value, bool On, bool Warn = false);

/// The left column's texts (UI návrh obr. 1–3), without a window.
public static class SetupTexts
{
    /// The codec as the summary says it: "H.264", "H.265", "ProRes Proxy".
    public static string CodecShort(ProxyCodec c) => c switch
    {
        ProxyCodec.H264 => "H.264",
        ProxyCodec.Hevc => "H.265",
        ProxyCodec.ProresProxy => "ProRes Proxy",
        ProxyCodec.ProresLT => "ProRes LT",
        ProxyCodec.Prores422 => "ProRes 422",
        _ => "ProRes HQ",
    };

    /// "LogC4 → 709" for a built-in LUT or the camera of the card (automatic), else what the mode says.
    public static string LutShort(ProxySettings p, LutCatalog? catalog, CameraLabel? camera) => LutResolver.EffectiveMode(p) switch
    {
        LutMode.None => "bez LUTu",
        LutMode.Custom => "vlastní LUT",
        LutMode.Builtin => catalog?.Find(p.LutBuiltin)?.Curve is string curve ? $"{CameraLabel.ShortCurve(curve)} → 709" : "vestavěný LUT",
        _ => camera?.Curve is string c ? $"{c} → 709" : "LUT podle kamery",
    };

    /// The four rows: Ověření zpětným čtením · Analýza klipů a náhledy · Proxy · Report.
    public static IReadOnlyList<AfterCopyRow> AfterCopy(SettingsProfile p, LutCatalog? catalog, CameraLabel? camera)
    {
        var report = "PDF · HTML · ASC MHL" + (p.Outputs.ClipListCsv ? " · CSV" : "") + (p.Outputs.ClipListAle ? " · ALE" : "");
        return
        [
            new("Ověření zpětným čtením", p.Verify ? HashKinds.Label(HashKinds.FromSetting(p.Hash)) : "vypnuto", p.Verify, Warn: !p.Verify),
            new("Analýza klipů a náhledy", p.AnalyzeClips ? "3 snímky / klip" : "vypnuto", p.AnalyzeClips),
            new("Proxy", p.Proxy.Enabled ? $"{p.Proxy.Height}p · {CodecShort(p.Proxy.Codec)} · {LutShort(p.Proxy, catalog, camera)}" : "vypnuto", p.Proxy.Enabled),
            new("Report", report, true),
        ];
    }

    /// Step 3 while a copy runs, in one line: "Ověření xxHash64 · analýza a náhledy · proxy 1080p H.264 · report PDF, HTML, MHL".
    public static string AfterCopyLine(SettingsProfile p) => string.Join(" · ", new[]
    {
        p.Verify ? $"Ověření {HashKinds.Label(HashKinds.FromSetting(p.Hash))}" : "Bez ověření",
        p.AnalyzeClips ? "analýza a náhledy" : null,
        p.Proxy.Enabled ? $"proxy {p.Proxy.Height}p {CodecShort(p.Proxy.Codec)}" : null,
        "report PDF, HTML, MHL",
    }.Where(x => x != null));

    public const string VerifyOffWarning = "Bez ověření není jisté, že data na disku jsou v pořádku.";

    /// "1 karta → 2 disky" (bold in the bar) and " · 1,70 GB" (null while a size is not known); nothing ticked: "Vyberte kartu".
    public static (string Counts, string? Size) StartSummary(int cards, int disks, long? bytes)
    {
        if (cards == 0) return ("Vyberte kartu", null);
        var counts = $"{cards} {Texts.Plural(cards, "karta", "karty", "karet")} → {disks} {Texts.Plural(disks, "disk", "disky", "disků")}";
        return (counts, bytes is long b && b > 0 ? $" · {Format.BytesCs3(b)}" : null);
    }

    /// The empty step 1: "Vložte kartu do čtečky" / "Vložte další kartu" while offloads run.
    public static (string Title, string Text) NoCard(bool jobsRunning) => jobsRunning
        ? ("Vložte další kartu", "Objeví se tady sama a zařadí se za běžící offload.")
        : ("Vložte kartu do čtečky", "Objeví se tady automaticky.");

    /// The left column shrinks (steps 2 and 3 compact) while there is no card and the queue has jobs (obr. 2, 3).
    public static bool Compact(bool hasSources, bool hasJobs) => !hasSources && hasJobs;

    /// "…\2026-10-07\A035_ARRI\" (Windows separators), "…\2026-10-07\<název karty>\" without one card.
    public static string PathPreview(string date, string? cardName) => $@"…\{date}\{cardName ?? "<název karty>"}\";

    /// The empty queue: "Připraveno — A035_ARRI na 2 disky" with a ticked card, else "Zatím žádný offload".
    public static (string Title, string Text) QueueEmpty(IReadOnlyList<string> cards, int disks, bool verify)
    {
        if (cards.Count == 0 || disks == 0) return ("Zatím žádný offload", "Vyberte kartu a cíle vlevo a stiskněte Ctrl+Enter.");
        var what = cards.Count == 1 ? cards[0] : $"{cards.Count} {Texts.Plural(cards.Count, "karta", "karty", "karet")}";
        var where = $"{disks} {Texts.Plural(disks, "disk", "disky", "disků")}";
        var write = disks switch
        {
            1 => "zapíše na disk",
            2 => "zapíše na oba disky zároveň",
            _ => $"zapíše na {Texts.Plural(disks, "", $"všechny {disks} disky", $"všech {disks} disků")} zároveň",
        };
        var check = verify ? " a každý soubor se zpětně ověří" : "; zpětné ověření je vypnuté";
        return ($"Připraveno — {what} na {where}",
            $"Karta se přečte jednou, {write}{check}. Další kartu můžete vložit kdykoli — zařadí se do fronty.");
    }

    /// The five icons of the empty queue: Karta → 2 disky naráz → Ověření → Proxy → Report (only what the profile does).
    public static IReadOnlyList<string> Flow(int disks, bool verify, bool analyze, bool proxy)
    {
        var steps = new List<string> { "Karta", disks <= 1 ? "1 disk" : $"{disks} {Texts.Plural(disks, "disk", "disky", "disků")} naráz" };
        if (verify) steps.Add("Ověření");
        if (proxy) steps.Add("Proxy");
        else if (analyze) steps.Add("Analýza");
        steps.Add("Report");
        return steps;
    }
}
