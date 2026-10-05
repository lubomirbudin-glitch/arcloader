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
        BuildLutLists();
    }

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
        RaiseMany(nameof(LutChoices), nameof(FallbackChoices), nameof(LutChoice), nameof(Fallback));
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

    static readonly string[] LutProperties = [nameof(LutChoice), nameof(ShowFallback), nameof(ShowCustom), nameof(LutName), nameof(LutPath)];

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
        set { if (value != null) { p.Codec = value.Value; Changed(nameof(Codec), nameof(ShowBitrate)); } }
    }

    public Option<int> Height
    {
        get => Heights.FirstOrDefault(h => h.Value == p.Height) ?? Heights[2];
        set { if (value != null) { p.Height = value.Value; Changed(nameof(Height)); } }
    }

    public bool ShowBitrate => p.Codec is ProxyCodec.H264 or ProxyCodec.Hevc;

    public int BitrateMbps
    {
        get => p.BitrateMbps;
        set { p.BitrateMbps = Math.Clamp(value, 2, 50); Changed(nameof(BitrateMbps), nameof(BitrateText)); }
    }

    public string BitrateText => $"{p.BitrateMbps} Mb/s";

    public Option<double?> Aspect
    {
        get => Aspects.FirstOrDefault(a => a.Value == p.FrameAspect) ?? Aspects[0];
        set { if (value != null) { p.FrameAspect = value.Value; Changed(nameof(Aspect), nameof(ShowFrameScale)); } }
    }

    public bool ShowFrameScale => p.FrameAspect != null;

    public int ScalePercent
    {
        get => (int)Math.Round(p.FrameScale * 100);
        set { p.FrameScale = Math.Clamp(value, 50, 100) / 100.0; Changed(nameof(ScalePercent), nameof(ScaleText)); }
    }

    public string ScaleText => $"{ScalePercent} % obrazu";

    public string LutName => p.LutPath is string l ? Path.GetFileName(l) : "nevybráno";

    public bool BurnFilename { get => p.BurnFilename; set { p.BurnFilename = value; Changed(nameof(BurnFilename)); } }
    public bool BurnTimecode { get => p.BurnTimecode; set { p.BurnTimecode = value; Changed(nameof(BurnTimecode)); } }
    public bool Audio { get => p.Audio; set { p.Audio = value; Changed(nameof(Audio)); } }
    public bool SkipCameraProxies { get => p.SkipCameraProxies; set { p.SkipCameraProxies = value; Changed(nameof(SkipCameraProxies)); } }
    public bool CopyToAllDestinations { get => p.CopyToAllDestinations; set { p.CopyToAllDestinations = value; Changed(nameof(CopyToAllDestinations)); } }

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
