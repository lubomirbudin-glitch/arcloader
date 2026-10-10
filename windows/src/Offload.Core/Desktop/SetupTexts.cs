namespace Offload.Core.Desktop;

/// Which of the four rows of "3 · After copy" it is.
public enum AfterCopyKind { Verify, Analysis, Proxy, Report }

/// One row of "3 · After copy": the green tick (or grey ring) and the value on the right; `Warn`: the yellow row of
/// switched-off verification. `Kind` says what a click opens (`Section`) and whether the tick is a switch (`HasSwitch`).
public sealed record AfterCopyRow(string Label, string Value, bool On, bool Warn = false, AfterCopyKind Kind = AfterCopyKind.Report)
{
    /// The settings-page section a click on the row opens.
    public string Section => AfterCopyRules.Section(Kind);
    public bool HasSwitch => AfterCopyRules.HasSwitch(Kind);
    /// The tooltip of the tick that is not a switch.
    public string? SwitchTip => HasSwitch ? null : "The report is always created";
}

/// The rules of the clickable rows (0.5.2), without a window.
public static class AfterCopyRules
{
    public static string Section(AfterCopyKind kind) => kind switch
    {
        AfterCopyKind.Verify => SettingsSections.Verify,
        AfterCopyKind.Analysis => SettingsSections.Analysis,
        AfterCopyKind.Proxy => SettingsSections.Proxy,
        _ => SettingsSections.Report,
    };

    /// The report is always written: its tick is fixed.
    public static bool HasSwitch(AfterCopyKind kind) => kind != AfterCopyKind.Report;

    /// A switch works unless Start is running (as the project switcher) or the demo forbids saving.
    public static bool CanToggle(AfterCopyKind kind, bool canSwitchProject, bool settingsSaveAllowed) =>
        HasSwitch(kind) && canSwitchProject && settingsSaveAllowed;

    /// Flips the setting of the profile; false for the report (nothing to flip). Proxy and analysis do not depend on each
    /// other (stage 2 runs when either is on), so there is no rule between them.
    public static bool Toggle(SettingsProfile p, AfterCopyKind kind)
    {
        switch (kind)
        {
            case AfterCopyKind.Verify: p.Verify = !p.Verify; return true;
            case AfterCopyKind.Analysis: p.AnalyzeClips = !p.AnalyzeClips; return true;
            case AfterCopyKind.Proxy: p.Proxy.Enabled = !p.Proxy.Enabled; return true;
            default: return false;
        }
    }
}

/// The sections of the settings page (the keys the navigation and the panels compare with).
public static class SettingsSections
{
    public const string Folders = "folders";
    public const string Verify = "verify";
    public const string Analysis = "analysis";
    public const string Proxy = "proxy";
    public const string Report = "report";
    public const string Notify = "notify";
    public const string Decoders = "decoders";
    public const string App = "app";
}

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
        LutMode.None => "no LUT",
        LutMode.Custom => "custom LUT",
        LutMode.Builtin => catalog?.Find(p.LutBuiltin)?.Curve is string curve ? $"{CameraLabel.ShortCurve(curve)} → 709" : "built-in LUT",
        _ => camera?.Curve is string c ? $"{c} → 709" : "LUT by camera",
    };

    /// The four rows: Verification by reading back · Clip analysis and thumbnails · Proxies · Report.
    public static IReadOnlyList<AfterCopyRow> AfterCopy(SettingsProfile p, LutCatalog? catalog, CameraLabel? camera)
    {
        var report = "PDF · HTML · ASC MHL" + (p.Outputs.ClipListCsv ? " · CSV" : "") + (p.Outputs.ClipListAle ? " · ALE" : "");
        return
        [
            new("Verification by reading back", p.Verify ? HashKinds.Label(HashKinds.FromSetting(p.Hash)) : "off", p.Verify, Warn: !p.Verify, Kind: AfterCopyKind.Verify),
            new("Clip analysis and thumbnails", p.AnalyzeClips ? "3 frames / clip" : "off", p.AnalyzeClips, Kind: AfterCopyKind.Analysis),
            new("Proxies", p.Proxy.Enabled ? $"{p.Proxy.Height}p · {CodecShort(p.Proxy.Codec)} · {LutShort(p.Proxy, catalog, camera)}" : "off", p.Proxy.Enabled, Kind: AfterCopyKind.Proxy),
            new("Report", report, true, Kind: AfterCopyKind.Report),
        ];
    }

    /// Step 3 while a copy runs, in one line: "Verification xxHash64 · analysis and thumbnails · proxies 1080p H.264 · report PDF, HTML, MHL".
    public static string AfterCopyLine(SettingsProfile p) => string.Join(" · ", new[]
    {
        p.Verify ? $"Verification {HashKinds.Label(HashKinds.FromSetting(p.Hash))}" : "No verification",
        p.AnalyzeClips ? "analysis and thumbnails" : null,
        p.Proxy.Enabled ? $"proxies {p.Proxy.Height}p {CodecShort(p.Proxy.Codec)}" : null,
        "report PDF, HTML, MHL",
    }.Where(x => x != null));

    public const string VerifyOffWarning = "Without verification there is no certainty that the data on the disk is intact.";

    /// The card tile's line (obr. 1): "24 clips · 1.70 GB · exFAT · reader USB 3 · 5 Gb/s". Before the card is looked at,
    /// the used space of its volume stands for the size; a disk that is not a camera card says so.
    public static string CardFacts(History.CardInsight? insight, long? usedOnVolume, string? filesystem, string? connection, bool looksLikeCard)
    {
        var parts = new List<string>();
        if (insight is { Error: null } i)
        {
            parts.Add(i.Clips.Count > 0 ? Texts.Clips(i.Clips.Count) : Texts.N(i.FileCount, "file"));
            parts.Add(Format.Bytes3(i.TotalBytes));
        }
        else if (usedOnVolume is long used) parts.Add(Format.Bytes3(used));
        if (!string.IsNullOrEmpty(filesystem)) parts.Add(filesystem);
        if (connection != null) parts.Add(connection);
        if (!looksLikeCard) parts.Add("does not look like a card");
        return string.Join(" · ", parts);
    }

    /// The size of everything ticked, or null while one card's size is not known (the bar then shows only the counts).
    public static long? Total(IEnumerable<long?> sizes)
    {
        long sum = 0;
        foreach (var s in sizes)
        {
            if (s is not long b) return null;
            sum += b;
        }
        return sum;
    }

    /// "1 card → 2 disks" (bold in the bar) and " · 1.70 GB" (null while a size is not known); nothing ticked: "Select a card".
    public static (string Counts, string? Size) StartSummary(int cards, int disks, long? bytes)
    {
        if (cards == 0) return ("Select a card", null);
        var counts = $"{Texts.N(cards, "card")} → {Texts.N(disks, "disk")}";
        return (counts, bytes is long b && b > 0 ? $" · {Format.Bytes3(b)}" : null);
    }

    /// The empty step 1: "Insert a card into the reader" / "Insert another card" while offloads run.
    public static (string Title, string Text) NoCard(bool jobsRunning) => jobsRunning
        ? ("Insert another card", "It appears here by itself and joins the queue after the running offload.")
        : ("Insert a card into the reader", "It appears here automatically.");

    /// The left column shrinks (steps 2 and 3 compact) while there is no card and the queue has jobs (obr. 2, 3).
    public static bool Compact(bool hasSources, bool hasJobs) => !hasSources && hasJobs;

    /// "…6-10-07\A035_ARRI\" (Windows separators), "…6-10-07\<card name>\" without one card.
    public static string PathPreview(string date, string? cardName) => $@"…\{date}\{cardName ?? "<card name>"}\";

    /// The empty queue: "Ready — A035_ARRI to 2 disks" with a ticked card, else "No offload yet".
    public static (string Title, string Text) QueueEmpty(IReadOnlyList<string> cards, int disks, bool verify)
    {
        if (cards.Count == 0 || disks == 0) return ("No offload yet", "Select a card and destinations on the left and press Ctrl+Enter.");
        var what = cards.Count == 1 ? cards[0] : Texts.N(cards.Count, "card");
        var where = Texts.N(disks, "disk");
        var write = disks switch
        {
            1 => "written to the disk",
            2 => "written to both disks at once",
            _ => $"written to all {disks} disks at once",
        };
        var check = verify ? ", and every file is verified by reading back" : "; verification by reading back is off";
        return ($"Ready — {what} to {where}",
            $"The card is read once and {write}{check}. You can insert another card at any time — it joins the queue.");
    }

    /// The five icons of the empty queue: Card → 2 disks at once → Verification → Proxies → Report (only what the profile does).
    public static IReadOnlyList<string> Flow(int disks, bool verify, bool analyze, bool proxy)
    {
        var steps = new List<string> { "Card", disks <= 1 ? "1 disk" : $"{disks} disks at once" };
        if (verify) steps.Add("Verification");
        if (proxy) steps.Add("Proxies");
        else if (analyze) steps.Add("Analysis");
        steps.Add("Report");
        return steps;
    }
}
