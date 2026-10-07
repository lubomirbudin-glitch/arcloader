using System.IO;
using System.Globalization;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

public sealed record Option<T>(T Value, string Label);

/// The Proxy section. Edits the live settings; every started job gets its own copy (SettingsStore.Clone).
public sealed class ProxyOptionsViewModel : ObservableObject
{
    readonly ProxySettings p;
    readonly MainViewModel owner;

    public ProxyOptionsViewModel(ProxySettings p, MainViewModel owner)
    {
        this.p = p;
        this.owner = owner;
        PickLutCommand = new RelayCommand(() => PickLut());
        PickFolderCommand = new RelayCommand(PickFolder);
        ClearFolderCommand = new RelayCommand(() => { p.ExtraFolder = null; Changed(FolderProperties); });
        BitrateDownCommand = new RelayCommand(() => BitrateMbps--, () => p.BitrateMbps > 2);
        BitrateUpCommand = new RelayCommand(() => BitrateMbps++, () => p.BitrateMbps < 50);
        ScaleDownCommand = new RelayCommand(() => ScalePercent--, () => ScalePercent > 50);
        ScaleUpCommand = new RelayCommand(() => ScalePercent++, () => ScalePercent < 100);
        EditRulesCommand = new RelayCommand(() => owner.EditLutRules(catalog));
        CodecChoices = Codecs.Select(c => new Choice(SetupTexts.CodecShort(c.Value), () => p.Codec == c.Value, () => Codec = c)).ToList();
        HeightChoices = Heights.Select(h => new Choice(h.Label, () => p.Height == h.Value, () => Height = h)).ToList();
        BuildLutLists();
    }

    /// The segmented Kodek and Výška of the settings page (UI návrh obr. 4).
    public IReadOnlyList<Choice> CodecChoices { get; }
    public IReadOnlyList<Choice> HeightChoices { get; }

    /// "≈ 75 MB / minutu" next to the bitrate.
    public string BitrateHint => SettingsTexts.BitrateHint(p.BitrateMbps);

    /// "LUT podle kamery": every built-in LUT, the chosen one marked "použito".
    public IReadOnlyList<LutRow> LutRows => catalog.Luts.Select(l => SettingsTexts.Row(l, p)).ToList();

    /// The burn-in preview's frame lines (the crop of "Výřez"): shown only with a crop; star sizes of a 3 × 3 grid.
    public bool HasFrame => SettingsTexts.Frame(p) != null;
    public string FrameText => SettingsTexts.FrameText(p);
    System.Windows.GridLength Star(Func<(double X, double Y, double W, double H), double> part) =>
        new(SettingsTexts.Frame(p) is { } f ? Math.Max(part(f), 0.0001) : 1, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength FrameLeft => Star(f => f.X);
    public System.Windows.GridLength FrameWidth => Star(f => f.W);
    public System.Windows.GridLength FrameRight => Star(f => 1 - f.X - f.W);
    public System.Windows.GridLength FrameTop => Star(f => f.Y);
    public System.Windows.GridLength FrameHeight => Star(f => f.H);
    public System.Windows.GridLength FrameBottom => Star(f => 1 - f.Y - f.H);
    static readonly string[] FrameProperties =
        [nameof(HasFrame), nameof(FrameText), nameof(FrameLeft), nameof(FrameWidth), nameof(FrameRight), nameof(FrameTop), nameof(FrameHeight), nameof(FrameBottom)];

    // The built-in LUTs are loaded off the UI thread (MainViewModel); until then only auto / none / custom are offered.
    LutCatalog catalog = new(null, []);
    IReadOnlyList<Option<string>> lutChoices = [];
    IReadOnlyList<Option<string>> fallbackChoices = [];

    public IReadOnlyList<Option<string>> LutChoices => lutChoices;
    public IReadOnlyList<Option<string>> FallbackChoices => fallbackChoices;

    void BuildLutLists()
    {
        var builtin = catalog.Luts.Select(l => new Option<string>(l.Id, l.Name)).ToList();
        lutChoices = [new("auto", "Automaticky podle kamery"), .. builtin, new("none", "Bez LUT"), new("custom", "Vlastní…")];
        fallbackChoices = [new("none", "Bez LUT"), .. builtin];
    }

    /// The catalog arrived (UI thread): the lists are rebuilt and the selections re-read.
    public void CatalogLoaded(LutCatalog loaded)
    {
        catalog = loaded;
        BuildLutLists();
        RaiseMany(nameof(LutChoices), nameof(FallbackChoices), nameof(LutChoice), nameof(Fallback), nameof(LutRows));
    }

    LutMode Mode => LutResolver.EffectiveMode(p);

    /// Always an instance of LutChoices (ComboBox compares by reference); an unknown built-in id shows as auto.
    public Option<string> LutChoice
    {
        get
        {
            var id = Mode switch
            {
                LutMode.Builtin => p.LutBuiltin,
                LutMode.Custom => "custom",
                LutMode.None => "none",
                _ => "auto",
            };
            return LutChoices.FirstOrDefault(c => c.Value == id) ?? LutChoices[0];
        }
        set
        {
            if (value == null) return;
            switch (value.Value)
            {
                case "auto": p.LutMode = LutMode.Auto; break;
                case "none": p.LutMode = LutMode.None; break;
                case "custom":
                    if (string.IsNullOrEmpty(p.LutPath))
                    {
                        // The dialog must not open inside the ComboBox's own selection change; a cancel restores the old choice.
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            if (PickLut()) return;
                            RaiseMany(nameof(LutChoice));
                        });
                        return;
                    }
                    p.LutMode = LutMode.Custom;
                    break;
                default:
                    p.LutMode = LutMode.Builtin;
                    p.LutBuiltin = value.Value;
                    break;
            }
            Changed(LutProperties);
        }
    }

    static readonly string[] LutProperties = [nameof(LutChoice), nameof(ShowFallback), nameof(ShowCustom), nameof(LutName), nameof(LutPath), nameof(ShowRules), nameof(LutRows)];

    /// "Pravidla LUTů… (2)" (spec 4.2) — only with "Automaticky podle kamery": the rules apply in that mode only (R18).
    public bool ShowRules => Mode == LutMode.Auto;
    public string RulesButtonText => $"Pravidla LUTů… ({p.LutRules.Count})";
    public RelayCommand EditRulesCommand { get; }

    /// The rules were edited (MainViewModel.EditLutRules wrote them into the profile).
    internal void RulesChanged() => Changed(nameof(RulesButtonText));

    public Option<string> Fallback
    {
        get => FallbackChoices.FirstOrDefault(c => c.Value == (p.LutFallback ?? "none")) ?? FallbackChoices[0];
        set
        {
            if (value == null) return;
            p.LutFallback = value.Value == "none" ? null : value.Value;
            Changed(nameof(Fallback));
        }
    }

    public bool ShowFallback => Mode == LutMode.Auto;
    public bool ShowCustom => Mode == LutMode.Custom;
    public string? LutPath => p.LutPath;

    public static IReadOnlyList<Option<ProxyCodec>> Codecs { get; } =
        Enum.GetValues<ProxyCodec>().Select(c => new Option<ProxyCodec>(c, c.Label())).ToList();

    public static IReadOnlyList<Option<int>> Heights { get; } =
        SettingsStore.ProxyHeights.Select(h => new Option<int>(h, $"{h}p")).ToList();

    public static IReadOnlyList<Option<double?>> Aspects { get; } =
    [
        new(null, "celý obraz"),
        .. new[] { 1.33, 1.66, 1.78, 1.85, 2.0, 2.39 }.Select(a => new Option<double?>(a, a.ToString("0.00", CultureInfo.InvariantCulture))),
    ];

    public RelayCommand PickLutCommand { get; }
    public RelayCommand PickFolderCommand { get; }
    public RelayCommand ClearFolderCommand { get; }
    public RelayCommand BitrateDownCommand { get; }
    public RelayCommand BitrateUpCommand { get; }
    public RelayCommand ScaleDownCommand { get; }
    public RelayCommand ScaleUpCommand { get; }

    void Changed(params string[] names)
    {
        RaiseMany(names);
        owner.ProxyChanged();
    }

    public bool Enabled { get => p.Enabled; set { p.Enabled = value; Changed(nameof(Enabled)); } }

    public Option<ProxyCodec> Codec
    {
        get => Codecs.First(c => c.Value == p.Codec);
        set
        {
            if (value == null) return;
            p.Codec = value.Value;
            Changed(nameof(Codec), nameof(ShowBitrate));
            foreach (var c in CodecChoices) c.Changed();
        }
    }

    public Option<int> Height
    {
        get => Heights.FirstOrDefault(h => h.Value == p.Height) ?? Heights[2];
        set
        {
            if (value == null) return;
            p.Height = value.Value;
            Changed(nameof(Height));
            foreach (var h in HeightChoices) h.Changed();
        }
    }

    public bool ShowBitrate => p.Codec is ProxyCodec.H264 or ProxyCodec.Hevc;

    public int BitrateMbps
    {
        get => p.BitrateMbps;
        set { p.BitrateMbps = Math.Clamp(value, 2, 50); Changed(nameof(BitrateMbps), nameof(BitrateText), nameof(BitrateHint)); }
    }

    public string BitrateText => $"{p.BitrateMbps} Mb/s";

    public Option<double?> Aspect
    {
        get => Aspects.FirstOrDefault(a => a.Value == p.FrameAspect) ?? Aspects[0];
        set { if (value != null) { p.FrameAspect = value.Value; Changed([nameof(Aspect), nameof(ShowFrameScale), .. FrameProperties]); } }
    }

    public bool ShowFrameScale => p.FrameAspect != null;

    public int ScalePercent
    {
        get => (int)Math.Round(p.FrameScale * 100);
        set { p.FrameScale = Math.Clamp(value, 50, 100) / 100.0; Changed([nameof(ScalePercent), nameof(ScaleText), .. FrameProperties]); }
    }

    public string ScaleText => $"{ScalePercent} % obrazu";

    public string LutName => p.LutPath is string l ? Path.GetFileName(l) : "nevybráno";

    public bool BurnFilename { get => p.BurnFilename; set { p.BurnFilename = value; Changed(nameof(BurnFilename)); } }
    public bool BurnTimecode { get => p.BurnTimecode; set { p.BurnTimecode = value; Changed(nameof(BurnTimecode)); } }
    public bool Audio { get => p.Audio; set { p.Audio = value; Changed(nameof(Audio)); } }
    public bool SkipCameraProxies { get => p.SkipCameraProxies; set { p.SkipCameraProxies = value; Changed(nameof(SkipCameraProxies)); } }
    public bool CopyToAllDestinations { get => p.CopyToAllDestinations; set { p.CopyToAllDestinations = value; Changed(nameof(CopyToAllDestinations)); } }
    /// Spec 4.2, R26: off by default until a Gen 4 sample confirms the SDK conversion.
    public bool BrawGen4ToGen5 { get => p.BrawGen4ToGen5; set { p.BrawGen4ToGen5 = value; Changed(nameof(BrawGen4ToGen5)); } }

    static readonly string[] FolderProperties = [nameof(ExtraFolderName), nameof(ExtraFolderPath), nameof(HasExtraFolder)];

    /// "Kopírovat proxy i do složky": the folder's name (the full path is in the tooltip), or "nevybráno".
    public string ExtraFolderName => p.ExtraFolder is string f
        ? (Path.GetFileName(f.TrimEnd('\\')) is { Length: > 0 } n ? n : f)
        : "nevybráno";
    public string? ExtraFolderPath => p.ExtraFolder;
    public bool HasExtraFolder => p.ExtraFolder != null;

    void PickFolder()
    {
        if (Dialogs.PickFolder("Vyber složku pro proxy") is not string path) return;
        p.ExtraFolder = path;
        Changed(FolderProperties);
    }

    /// The .cube dialog; a picked file switches to custom mode. False when cancelled (nothing changes).
    bool PickLut()
    {
        if (Dialogs.PickFile("Vyber LUT (.cube)", "LUT (*.cube)|*.cube") is not string path) return false;
        p.LutPath = path;
        p.LutMode = LutMode.Custom;
        Changed(LutProperties);
        return true;
    }
}
