using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

public sealed partial class MainViewModel
{
    readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> cardNames = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> manualSources = [];
    // Volume labels of manual sources that are a drive root the scan does not list (an internal disk, a network
    // letter), read off the UI thread when the source is added.
    readonly Dictionary<string, string?> manualLabels = new(StringComparer.OrdinalIgnoreCase);
    List<VolumeItem> volumes = [];
    ExternalTools resolvedTools = new();
    bool scanning, rescan, useCustomDate;
    DateTime? customDate = DateTime.Today;
    // Cards ejected from this window: the scan does not touch their letters until Windows reports a new arrival
    // there (or the user presses refresh) — a reader that cannot eject media would otherwise mount the card again.
    readonly HashSet<string> ejectedRoots = new(StringComparer.OrdinalIgnoreCase);
    // Every ~5 s off the UI thread: missing destinations (a NAS that came back), present network/removable ones
    // (a NAS that dropped out), the LUT and the proxy folder are checked again.
    readonly DispatcherTimer healthTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    // The LUT's existence, checked off the UI thread (CanStart is evaluated 3×/s and must never touch a disk).
    string? lutCheckedPath;
    bool? lutExists;
    bool lutChecking, lutRecheck;
    // The same for the extra proxy folder (a disk that may be disconnected).
    string? folderCheckedPath;
    bool? folderExists;
    bool folderChecking, folderRecheck;

    public ObservableCollection<SourceItemViewModel> Sources { get; } = [];
    public ObservableCollection<DestinationViewModel> Destinations { get; } = [];
    public ObservableCollection<ToolRowViewModel> Tools { get; } = [];
    public ProxyOptionsViewModel Proxy { get; private set; } = null!;
    public RelayCommand RefreshVolumesCommand { get; private set; } = null!;
    public RelayCommand AddSourceCommand { get; private set; } = null!;
    public RelayCommand AddDestinationCommand { get; private set; } = null!;

    /// Selection, destinations or proxy changed — the start bar listens (Task 8).
    public event Action? SetupChanged;

    void InitSetup()
    {
        Proxy = new ProxyOptionsViewModel(Settings.Proxy, this);
        RefreshVolumesCommand = new RelayCommand(RefreshAll);
        AddSourceCommand = new RelayCommand(AddManualSource);
        AddDestinationCommand = new RelayCommand(AddDestination);
        foreach (var path in Options.Sources) AddManualSourcePath(path);
        RebuildDestinations();
        RefreshTools();
        RefreshVolumes();
        RefreshLut();
        RefreshProxyFolder();
        Task.Run(() => LutCatalog.Default).ContinueWith(t =>
        {
            if (closing) return;
            if (t.IsCompletedSuccessfully) Proxy.CatalogLoaded(t.Result);
            else AppLog.Write($"LUT catalog: {t.Exception?.GetBaseException().Message}");
        }, TaskScheduler.FromCurrentSynchronizationContext());
        healthTimer.Tick += (_, _) => HealthCheck();
        healthTimer.Start();
    }

    /// The refresh button: volumes (also the letters of ejected cards — the user asks for them), every destination,
    /// the LUT and the proxy folder, all off the UI thread.
    void RefreshAll()
    {
        ejectedRoots.Clear();
        RefreshVolumes();
        foreach (var d in Destinations) d.Probe();
        RefreshLut();
        RefreshProxyFolder();
        ReloadHistory();   // and every card is looked at again
    }

    void HealthCheck()
    {
        if (closing) return;
        // Missing rows (a NAS that may come back) and present network/removable ones (a NAS that may drop out);
        // a row whose probe is still running is skipped by Probe itself.
        foreach (var d in Destinations.Where(d => d.ShouldReprobe)) d.Probe();
        RefreshLut();
        RefreshProxyFolder();
    }

    /// Windows reported volumes arriving (WM_DEVICECHANGE): a card put into the slot of an ejected one is scanned again.
    public void VolumesArrived(IReadOnlyList<string> roots)
    {
        foreach (var r in roots) ejectedRoots.Remove(r);
    }

    internal void CardEjected(string source)
    {
        try
        {
            if (Path.GetPathRoot(Path.GetFullPath(source)) is { Length: > 0 } root) ejectedRoots.Add(root);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a drive path — nothing to keep away from the scan.
        }
        RefreshVolumes();   // the ejected letter is skipped: the card leaves the list without being mounted again
    }

    // ── Karty ──────────────────────────────────────────────────────────────

    public bool HasSources => Sources.Count > 0;

    /// Rescans volumes on a background thread; a request during a scan runs once more afterwards.
    public void RefreshVolumes()
    {
        if (scanning)
        {
            rescan = true;
            return;
        }
        scanning = true;
        var dests = Settings.DestRoots.ToList();
        var skip = ejectedRoots.ToList();
        Task.Run(() => Volumes.Scan(dests, skip)).ContinueWith(t =>
        {
            scanning = false;
            try
            {
                if (t.IsCompletedSuccessfully) ApplyVolumes(t.Result);
                else AppLog.Write($"volume scan: {t.Exception?.GetBaseException().Message}");
            }
            catch (Exception e)
            {
                AppLog.Write($"volume apply: {e.Message}");
            }
            finally
            {
                if (rescan)
                {
                    rescan = false;
                    RefreshVolumes();
                }
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    string? sourcesSig, destSig;

    void ApplyVolumes(List<VolumeItem> found)
    {
        var next = Volumes.SelectionAfterRefresh(volumes.Select(v => v.Path), found, selected, manualSources);
        // Another card on the same drive letter (gone, or swapped faster than one scan) must not inherit the name.
        var drop = Volumes.NamesToDrop(volumes, found);
        volumes = found;
        selected.Clear();
        selected.UnionWith(next);
        foreach (var p in drop)
        {
            cardNames.Remove(p);
            inspections.Drop(p);   // its history line belonged to the card that left
        }
        var sig = string.Join("\n", found.Where(v => !v.IsDestination).Select(v => $"{v.Path}|{v.Identity}|{v.Name}|{v.Total - v.Free}|{v.Format}|{v.LooksLikeCard}|{selected.Contains(v.Path)}"))
            + "\n" + string.Join("\n", manualSources.Select(p => $"{p}|{selected.Contains(p)}"));
        if (sig != sourcesSig)
        {
            sourcesSig = sig;
            RebuildSources();
        }
        else SetupChanged?.Invoke();
        // destinations: only when the list or the set of connected volumes changed (a disk may just have been connected)
        var dsig = string.Join("\n", Settings.DestRoots) + "\n" + string.Join("\n", found.Select(v => v.Path));
        if (dsig != destSig)
        {
            destSig = dsig;
            RebuildDestinations();
        }
        InspectMissing();   // new or swapped cards
        foreach (var j in Jobs) j.VolumesChanged();   // "Vysunout kartu" only while the copied card is in its drive
        RefreshLut();   // the LUT may sit on a disk that just came or went
        RefreshProxyFolder();   // and so may the proxy folder
    }

    internal bool IsCurrent(DestinationViewModel row) => Destinations.Contains(row);

    internal void DestinationInfoChanged() => SetupChanged?.Invoke();
    void RebuildSources()
    {
        Sources.Clear();
        foreach (var v in volumes.Where(v => !v.IsDestination))
            Sources.Add(new SourceItemViewModel(this, v.Path, v.Name,
                $"{Format.Bytes(v.Total - v.Free)} · {v.Format}{(v.LooksLikeCard ? "" : " · nevypadá jako karta")}",
                v.LooksLikeCard ? SourceKind.Card : SourceKind.Drive, DefaultCardName(v.Path)));
        foreach (var p in manualSources)
            Sources.Add(new SourceItemViewModel(this, p, Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p, p, SourceKind.Folder, DefaultCardName(p)));
        Raise(nameof(HasSources));
        SetupChanged?.Invoke();
    }

    void AddManualSource()
    {
        if (Dialogs.PickFolder("Vyber kartu nebo složku se záznamem") is string path) AddManualSourcePath(path);
    }

    void AddManualSourcePath(string path)
    {
        if (!manualSources.Contains(path, StringComparer.OrdinalIgnoreCase)) manualSources.Add(path);
        selected.Add(path);
        RebuildSources();
        ReadManualLabel(path);
        Inspect(path);
    }

    /// A drive root picked as a folder: its label is the card name the job will use — read it off the UI thread.
    void ReadManualLabel(string path)
    {
        string? root;
        try
        {
            root = Layout.DriveRootOf(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }
        // re-read on every add: the card behind a drive root can be swapped while it is out of the list
        if (root == null
            || volumes.Any(v => string.Equals(v.Path, root, StringComparison.OrdinalIgnoreCase))) return;   // the scan has its label
        Task.Run(() => NativeIO.VolumeLabel(root)).ContinueWith(t =>
        {
            if (closing) return;
            if (!t.IsCompletedSuccessfully)
            {
                AppLog.Write($"label {root}: {t.Exception?.GetBaseException().Message}");
                return;
            }
            manualLabels[path] = t.Result;
            if (manualSources.Contains(path, StringComparer.OrdinalIgnoreCase)) RebuildSources();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    internal bool IsSelected(string path) => selected.Contains(path);

    /// The ticked sources in the order of the list (volumes, then manual folders).
    List<string> SelectedSources() => volumes.Select(v => v.Path).Concat(manualSources).Where(selected.Contains).ToList();

    /// The card folder name a start would use now: the typed name, otherwise the placeholder.
    internal string PlannedName(string path) =>
        cardNames.GetValueOrDefault(path) is { } typed && typed.Trim().Length > 0 ? typed.Trim() : DefaultCardName(path);

    internal void SetSelected(string path, bool on)
    {
        if (on) selected.Add(path);
        else selected.Remove(path);
        SetupChanged?.Invoke();
    }

    internal string CardName(string path) => cardNames.GetValueOrDefault(path, "");

    /// The card folder name when none is typed: the volume label from the last scan, a folder's own name — never read
    /// from the disk here (UI thread). The job itself works out the same name off the UI thread.
    internal string DefaultCardName(string path)
    {
        var label = volumes.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase))?.Label
            ?? manualLabels.GetValueOrDefault(path);
        try
        {
            return Layout.DefaultCardName(path, label);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    internal void SetCardName(string path, string name) => cardNames[path] = name;

    // ── Cíle ───────────────────────────────────────────────────────────────

    public bool HasNoDestinations => Destinations.Count == 0;
    public bool SingleDestination => Destinations.Count == 1;

    void AddDestination()
    {
        if (Dialogs.PickFolder("Vyber cílovou složku (root)") is not string path) return;
        if (!Settings.DestRoots.Contains(path, StringComparer.OrdinalIgnoreCase)) Settings.DestRoots.Add(path);
        DestinationsChanged();
    }

    internal void RemoveDestination(string root)
    {
        Settings.DestRoots.RemoveAll(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase));
        DestinationsChanged();
    }

    void DestinationsChanged()
    {
        SettingsChanged();
        RebuildDestinations();
        RefreshVolumes();   // a disk holding a destination is no longer offered as a card
    }

    void RebuildDestinations()
    {
        Destinations.Clear();
        for (int i = 0; i < Settings.DestRoots.Count; i++) Destinations.Add(new DestinationViewModel(this, i + 1, Settings.DestRoots[i]));
        RaiseMany(nameof(HasNoDestinations), nameof(SingleDestination));
        SetupChanged?.Invoke();
    }

    // ── Struktura a kontrola ───────────────────────────────────────────────

    public bool UseCustomDate
    {
        get => useCustomDate;
        set { if (Set(ref useCustomDate, value)) Raise(nameof(PathPreview)); }
    }

    public DateTime? CustomDate
    {
        get => customDate;
        set { if (Set(ref customDate, value)) Raise(nameof(PathPreview)); }
    }

    public string DateString => UseCustomDate && CustomDate is DateTime d
        ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : Layout.Today();

    public string PathPreview => $@"…\{DateString}\<název karty>\";

    public bool Verify
    {
        get => Settings.Verify;
        set { Settings.Verify = value; SettingsChanged(); Raise(); }
    }

    public bool AnalyzeClips
    {
        get => Settings.AnalyzeClips;
        set
        {
            Settings.AnalyzeClips = value;
            SettingsChanged();
            RaiseMany(nameof(AnalyzeClips), nameof(LutProblem), nameof(ToolsWarning));
            SetupChanged?.Invoke();
            RefreshLut();
        }
    }

    // ── Proxy a dekodéry ───────────────────────────────────────────────────

    /// A missing LUT blocks the start (red); missing ffmpeg only warns (orange). From the cached check — no disk here.
    public string? LutProblem => ProxyPreflight.Blocking(Settings.Proxy, Settings.AnalyzeClips, CachedLutExists);

    bool? CachedLutExists =>
        ProxyPreflight.LutInUse(Settings.Proxy, Settings.AnalyzeClips) is string lut && string.Equals(lut, lutCheckedPath, StringComparison.OrdinalIgnoreCase)
            ? lutExists
            : null;

    /// Checks the LUT off the UI thread; a request during a check runs once more afterwards.
    void RefreshLut()
    {
        if (lutChecking)
        {
            lutRecheck = true;
            return;
        }
        if (ProxyPreflight.LutInUse(Settings.Proxy, Settings.AnalyzeClips) is not string lut)
        {
            SetLutState(null, null);
            return;
        }
        lutChecking = true;
        Task.Run(() => File.Exists(lut)).ContinueWith(t =>
        {
            lutChecking = false;
            if (t.IsCompletedSuccessfully) SetLutState(lut, t.Result);
            else AppLog.Write($"LUT check: {t.Exception?.GetBaseException().Message}");
            if (lutRecheck)
            {
                lutRecheck = false;
                RefreshLut();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// A new answer about the LUT: the red text and the start button follow it.
    void SetLutState(string? lut, bool? exists)
    {
        var before = LutProblem;
        lutCheckedPath = lut;
        lutExists = exists;
        if (LutProblem == before) return;
        RaiseMany(nameof(LutProblem), nameof(CanStart));
        SetupChanged?.Invoke();
    }
    public string? ToolsWarning => ProxyPreflight.Warning(Settings.Proxy, Settings.AnalyzeClips, resolvedTools);

    /// A missing proxy folder (disconnected disk) blocks the start (red). From the cached check — no disk here.
    public string? ProxyFolderProblem => ProxyPreflight.ExtraFolderBlocking(Settings.Proxy, CachedFolderExists);

    bool? CachedFolderExists =>
        ProxyPreflight.ExtraFolderInUse(Settings.Proxy) is string f && string.Equals(f, folderCheckedPath, StringComparison.OrdinalIgnoreCase)
            ? folderExists
            : null;

    /// Checks the proxy folder off the UI thread; a request during a check runs once more afterwards.
    void RefreshProxyFolder()
    {
        if (folderChecking)
        {
            folderRecheck = true;
            return;
        }
        if (ProxyPreflight.ExtraFolderInUse(Settings.Proxy) is not string folder)
        {
            SetProxyFolderState(null, null);
            return;
        }
        folderChecking = true;
        Task.Run(() => Directory.Exists(folder)).ContinueWith(t =>
        {
            folderChecking = false;
            if (t.IsCompletedSuccessfully) SetProxyFolderState(folder, t.Result);
            else AppLog.Write($"proxy folder check: {t.Exception?.GetBaseException().Message}");
            if (folderRecheck)
            {
                folderRecheck = false;
                RefreshProxyFolder();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// A new answer about the proxy folder: the red text and the start button follow it.
    void SetProxyFolderState(string? folder, bool? exists)
    {
        var before = ProxyFolderProblem;
        folderCheckedPath = folder;
        folderExists = exists;
        if (ProxyFolderProblem == before) return;
        RaiseMany(nameof(ProxyFolderProblem), nameof(CanStart));
        SetupChanged?.Invoke();
    }

    internal void ProxyChanged()
    {
        SettingsChanged();
        RaiseMany(nameof(LutProblem), nameof(ProxyFolderProblem), nameof(ToolsWarning));
        SetupChanged?.Invoke();
        RefreshLut();
        RefreshProxyFolder();
    }

    void RefreshTools()
    {
        resolvedTools = Settings.Tools.Resolved();
        Tools.Clear();
        void Row(string name, string purpose, string? path, Action<string> set) =>
            Tools.Add(new ToolRowViewModel(name, purpose, path, () =>
            {
                if (Dialogs.PickFile($"Vyber {name}", "Programy (*.exe)|*.exe") is not string picked) return;
                set(picked);
                SettingsChanged();
                RefreshTools();
            }));
        Row("ffmpeg", "proxy, náhledy", resolvedTools.Ffmpeg, p => Settings.Tools.Ffmpeg = p);
        Row("ffprobe", "metadata klipů", resolvedTools.Ffprobe, p => Settings.Tools.Ffprobe = p);
        Row("braw-decode", "Blackmagic RAW", resolvedTools.BrawDecoder, p => Settings.Tools.BrawDecoder = p);
        Row("REDline", "RED R3D", resolvedTools.Redline, p => Settings.Tools.Redline = p);
        Row("art-cmd", "ARRIRAW", resolvedTools.ArtCmd, p => Settings.Tools.ArtCmd = p);
        Raise(nameof(ToolsWarning));
    }
}
