using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Outputs;
using Offload.Core.Space;

namespace Offload.App;

public sealed partial class MainViewModel
{
    readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> cardNames = new(StringComparer.OrdinalIgnoreCase);
    // Cards whose "Doplnit jen nové klipy" is ticked (spec 2.2); it applies only while the card still has new files.
    // A tick belongs to the history entry the user saw and is pruned as soon as its offer goes away (never returns by itself).
    readonly AppendTicks appendTicks = new();
    readonly List<string> manualSources = [];
    // Volume labels of manual sources that are a drive root the scan does not list (an internal disk, a network
    // letter), read off the UI thread when the source is added.
    readonly Dictionary<string, string?> manualLabels = new(StringComparer.OrdinalIgnoreCase);
    // How each source's disk is connected (UI návrh 6), looked up off the UI thread once per card in the drive.
    readonly Dictionary<string, (string? Identity, string? Label)> sourceLinks = new(StringComparer.OrdinalIgnoreCase);
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
    // The own LUT files' existence (custom LUT, rules), checked off the UI thread (CanStart is evaluated 3×/s and must
    // never touch a disk): the files checked and the first missing one.
    string? lutCheckedKey;
    LutFile? lutMissing;
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
        ForgetRemovedDisksCommand = new RelayCommand(ForgetRemovedDisks);
        ChooseLocalReportFolderCommand = new RelayCommand(ChooseLocalReportFolder);
        DefaultLocalReportFolderCommand = new RelayCommand(DefaultLocalReportFolder);
        foreach (var path in Options.Sources) AddManualSourcePath(path);
        RebuildDestinations();
        RefreshTools();
        RefreshVolumes();
        RefreshLut();
        RefreshProxyFolder();
        Task.Run(() => LutCatalog.Default).ContinueWith(t =>
        {
            if (closing) return;
            if (t.IsCompletedSuccessfully) Proxy.CatalogLoaded(lutCatalog = t.Result);
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
        _ = ProbeFreeSpaceAsync(TimeSpan.FromSeconds(30));   // every destination and the proxy folder: free space, volume
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
        ReprobeStaleSpaceProblem();   // a red "není dost místa" on a fixed disk must clear once space is freed
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
        if (IsDemo) return;   // the demo's cards stay as the scenario put them
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
        var next = Volumes.SelectionAfterRefresh(volumes.Select(v => v.Path), found, selected, manualSources, Settings.AutoDrive.Cards);
        // Another card on the same drive letter (gone, or swapped faster than one scan) must not inherit the name.
        var drop = Volumes.NamesToDrop(volumes, found);
        volumes = found;
        selected.Clear();
        selected.UnionWith(next);
        foreach (var p in drop)
        {
            cardNames.Remove(p);
            appendTicks.Drop(p);
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
        ApplyAutoDrives(found);   // connected disks as destinations (may rescan)
        InspectMissing();   // new or swapped cards
        ResolveSourceLinks();   // how the new cards are connected
        foreach (var j in Jobs.OfType<JobItemViewModel>()) j.VolumesChanged();   // "Vysunout kartu" only while the copied card is in its drive
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
                v.LooksLikeCard ? SourceKind.Card : SourceKind.Drive));
        foreach (var p in manualSources)
            Sources.Add(new SourceItemViewModel(this, p, Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p, p, SourceKind.Folder));
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
        ResolveSourceLinks();
    }

    /// The connection of every listed source whose card has not been looked at yet ("čtečka USB 3 · 5 Gb/s"), each off
    /// the UI thread (DriveLinks asks the device; it never throws).
    void ResolveSourceLinks()
    {
        if (IsDemo) return;
        foreach (var s in Sources)
        {
            var path = s.Path;
            var identity = IdentityOf(path);
            if (sourceLinks.TryGetValue(path, out var known) && string.Equals(known.Identity, identity, StringComparison.OrdinalIgnoreCase)) continue;
            sourceLinks[path] = (identity, null);
            Task.Run(() => ConnectionLabel.Short(Offload.Core.Hardware.DriveLinks.Resolve(path), card: true)).ContinueWith(t =>
            {
                if (closing || !t.IsCompletedSuccessfully) return;
                if (!sourceLinks.TryGetValue(path, out var now) || !string.Equals(now.Identity, identity, StringComparison.OrdinalIgnoreCase)) return;
                sourceLinks[path] = (identity, t.Result);
                RowFor(path)?.FactsChanged();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    /// The card tile's line: clips, size, file system, connection (memory only).
    internal string SourceFacts(string path)
    {
        var volume = volumes.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase));
        var link = sourceLinks.TryGetValue(path, out var l) && string.Equals(l.Identity, IdentityOf(path), StringComparison.OrdinalIgnoreCase) ? l.Label : null;
        return SetupTexts.CardFacts(Insight(path), volume is { } v ? v.Total - v.Free : null, volume?.Format ?? Insight(path)?.Filesystem, link,
            volume?.LooksLikeCard ?? true);
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
        cardNames.GetValueOrDefault(path) is { } typed && typed.Trim().Length > 0 ? typed.Trim() : Placeholder(path).Name;

    /// Spec 2.3: the suggestion from the clips or the next in line, else the volume label / folder name. Memory only.
    /// Cards are numbered in list order together with the other ticked cards, so the rows and Start agree.
    internal (string Name, string? Hint) Placeholder(string path)
    {
        var paths = SelectedSources();
        if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
        var cards = paths.Select(p => new CardNameInput(p, cardNames.GetValueOrDefault(p), Insight(p), DefaultCardName(p))).ToList();
        return CardNaming.Resolve(Settings.Naming.SuggestFromClips, cards, history, DateString, Layout.Today(), ActiveProjectName)[path];
    }

    void NamingChanged()
    {
        foreach (var s in Sources) s.NamingChanged();
    }

    internal void SetSelected(string path, bool on)
    {
        if (on) selected.Add(path);
        else selected.Remove(path);
        NamingChanged();   // the numbering of "next in line" follows the ticked cards
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

    internal void SetCardName(string path, string name)
    {
        cardNames[path] = name;
        NamingChanged();   // a typed name is taken for the other cards' suggestions
    }

    /// The append checkbox's text, or null when the card has no new files to append (spec 2.2). Memory only.
    internal string? AppendOffer(string path) =>
        Insight(path) is { } i && AppendRules.Offer(i) ? AppendRules.CheckboxText(i.History!) : null;

    internal bool AppendChosen(string path) => appendTicks.Chosen(path, Insight(path));

    /// Ticked, or ticked until the offer went away: such a card is never started as a full copy (Start tells the user).
    bool AppendWanted(string path) => appendTicks.Wants(path);

    /// Removes the ticks whose offer is gone (the card changed, was swapped, the inspection failed). UI thread, memory only.
    void PruneAppend() => appendTicks.Prune(Insight);

    internal string? AppendFolder(string path) => AppendChosen(path) ? AppendRules.FolderText(Insight(path)!.History!.Entry) : null;

    internal void SetAppend(string path, bool on)
    {
        if (!Allowed(DemoCommand.Append)) { SetupChanged?.Invoke(); return; }   // the toggle snaps back
        if (on) appendTicks.Tick(path, Insight(path));
        else appendTicks.Untick(path);
        SetupChanged?.Invoke();
    }

    // ── Cíle ───────────────────────────────────────────────────────────────

    public bool HasNoDestinations => Destinations.Count == 0;
    public bool SingleDestination => Destinations.Count == 1;

    void AddDestination()
    {
        if (Dialogs.PickFolder("Vyber cílovou složku (root)") is not string path) return;
        if (!Settings.DestRoots.Contains(path, StringComparer.OrdinalIgnoreCase)) Settings.DestRoots.Add(path);
        // Added by hand: a disk removed earlier is no longer remembered as unwanted (from the last scan — memory only).
        if (AutoDrives.RemovedDisk(path, ScannedFacts) is string disk) Settings.AutoDrive.Removed.RemoveAll(r => string.Equals(r, disk, StringComparison.OrdinalIgnoreCase));
        DestinationsChanged();
    }

    internal void RemoveDestination(string root)
    {
        Settings.DestRoots.RemoveAll(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase));
        // Task 2026-10-09: a removed disk is remembered by its identity (serial + capacity) and never added by itself again
        // — the one the app added (also while it is disconnected), or a connected external disk the destination was on.
        var auto = Settings.AutoDrive;
        var disk = auto.Added.FirstOrDefault(a => string.Equals(a.Root, root, StringComparison.OrdinalIgnoreCase))?.Disk
            ?? AutoDrives.RemovedDisk(root, ScannedFacts);
        auto.Added.RemoveAll(a => string.Equals(a.Root, root, StringComparison.OrdinalIgnoreCase));
        if (disk != null && !auto.Removed.Contains(disk, StringComparer.OrdinalIgnoreCase)) auto.Removed.Add(disk);
        Raise(nameof(ForgetRemovedText));
        DestinationsChanged();
    }

    // ── Automatické načtení disků (2026-10-09) ─────────────────────────────

    string? autoDriveHint;
    bool autoDriveDeferred;

    IEnumerable<DriveFacts> ScannedFacts => volumes.Select(v => v.Facts).OfType<DriveFacts>();

    /// Why a connected disk was not added as a destination (full, read-only), or null.
    public string? AutoDriveHint => autoDriveHint;

    /// A scan's answer on the UI thread (memory only — the scan gathered the facts off it): connected external disks
    /// are added as destinations, auto-added ones whose disk is gone are dropped. Never during a Start (ruling I1: the
    /// profile shown must not change under its checks — done after it) nor in the demo.
    void ApplyAutoDrives(List<VolumeItem> found)
    {
        if (IsDemo) return;
        if (starting)
        {
            autoDriveDeferred = true;
            return;
        }
        var auto = Settings.AutoDrive;
        var plan = AutoDrives.Plan(new AutoDriveInput(found.Select(v => v.Facts).OfType<DriveFacts>().ToList(), Settings.DestRoots,
            auto.Added, auto.Removed, auto.Destinations, Locked: false));
        var hint = plan.Hints.Count > 0 ? string.Join("\n", plan.Hints) : null;
        if (hint != autoDriveHint)
        {
            autoDriveHint = hint;
            Raise(nameof(AutoDriveHint));
        }
        if (!plan.Changes) return;
        foreach (var root in plan.Drop)
        {
            Settings.DestRoots.RemoveAll(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase));
            auto.Added.RemoveAll(a => string.Equals(a.Root, root, StringComparison.OrdinalIgnoreCase));
            AppLog.Write($"auto drives: {root} disconnected — dropped from the destinations");
        }
        foreach (var add in plan.Add)
        {
            Settings.DestRoots.Add(add.Root);
            auto.Added.Add(add);
            AppLog.Write($"auto drives: {add.Root} ({add.Disk}) added as a destination");
        }
        DestinationsChanged();   // saves, rebuilds the rows and rescans (the disk leaves Karty)
    }

    /// Start has ended: a scan it held back is applied now.
    void AutoDrivesAfterStart()
    {
        if (!autoDriveDeferred) return;
        autoDriveDeferred = false;
        RefreshVolumes();
    }

    public bool AutoAddDestinations
    {
        get => Settings.AutoDrive.Destinations;
        set
        {
            Settings.AutoDrive.Destinations = value;
            SettingsChanged();
            Raise();
            RefreshVolumes();
        }
    }

    public bool AutoAddCards
    {
        get => Settings.AutoDrive.Cards;
        set
        {
            Settings.AutoDrive.Cards = value;
            SettingsChanged();
            Raise();
        }
    }

    /// "Zapomenout odebrané disky (2)", or null when none is remembered.
    public string? ForgetRemovedText => Settings.AutoDrive.Removed.Count > 0 ? SettingsTexts.ForgetRemovedLabel(Settings.AutoDrive.Removed.Count) : null;

    void ForgetRemovedDisks()
    {
        Settings.AutoDrive.Removed.Clear();
        SettingsChanged();
        Raise(nameof(ForgetRemovedText));
        RefreshVolumes();
    }

    public RelayCommand ForgetRemovedDisksCommand { get; private set; } = null!;

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
        set
        {
            if (!Set(ref useCustomDate, value)) return;
            Raise(nameof(PathPreview));
            NamingChanged();   // "next in line" counts within the date folder
        }
    }

    public DateTime? CustomDate
    {
        get => customDate;
        set
        {
            if (!Set(ref customDate, value)) return;
            Raise(nameof(PathPreview));
            NamingChanged();
        }
    }

    public bool SuggestCardNames
    {
        get => Settings.Naming.SuggestFromClips;
        set
        {
            Settings.Naming.SuggestFromClips = value;
            SettingsChanged();
            Raise();
            NamingChanged();
        }
    }

    public string DateString => UseCustomDate && CustomDate is DateTime d
        ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : Layout.Today();

    /// "…\2026-10-07\A035_ARRI\" with one card ticked, else "…\2026-10-07\<název karty>\".
    public string PathPreview => SetupTexts.PathPreview(DateString, SelectedSources() is [var only] ? PlannedName(only) : null);

    public bool Verify
    {
        get => Settings.Verify;
        set { Settings.Verify = value; SettingsChanged(); Raise(); }
    }

    /// Plan 10 (R11): "Ověřit primární disk během proxy" — `verifyInProxy` in settings.json.
    public bool VerifyInProxy
    {
        get => Settings.VerifyInProxy;
        set { Settings.VerifyInProxy = value; SettingsChanged(); Raise(); }
    }

    /// Plan 10 (spec 6): the first destination is the primary one instead of the fastest — `verifyPrimary`.
    public bool VerifyPrimaryFirst
    {
        get => Settings.VerifyPrimary == SettingsProfile.PrimaryFirst;
        set { Settings.VerifyPrimary = value ? SettingsProfile.PrimaryFirst : SettingsProfile.PrimaryFastest; SettingsChanged(); Raise(); }
    }

    /// "Kontrolní součet" (spec 3.5): xxHash64 or xxHash64 + MD5 — `hash` in settings.json.
    public IReadOnlyList<HashChoice> HashChoices => HashChoice.All;

    public HashChoice Hash
    {
        get => HashChoice.Of(HashKinds.FromSetting(Settings.Hash));
        set
        {
            if (value == null) return;
            Settings.Hash = HashKinds.Wire(value.Kind);
            SettingsChanged();
            RaiseMany(nameof(Hash), nameof(VerifyLabel), nameof(Md5Note));
        }
    }

    public string VerifyLabel => Texts.VerifyLabel(HashKinds.FromSetting(Settings.Hash));

    /// Shown under the choice while MD5 is on.
    public string? Md5Note => HashKinds.FromSetting(Settings.Hash).HasFlag(HashKind.Md5) ? Texts.Md5Slower : null;

    /// "Seznam klipů CSV" / "Seznam klipů pro Avid (ALE)" (spec 3.3) — `outputs` in settings.json.
    public bool ClipListCsv
    {
        get => Settings.Outputs.ClipListCsv;
        set { Settings.Outputs.ClipListCsv = value; SettingsChanged(); Raise(); }
    }

    public bool ClipListAle
    {
        get => Settings.Outputs.ClipListAle;
        set { Settings.Outputs.ClipListAle = value; SettingsChanged(); Raise(); }
    }

    /// "Denní report po každé kartě" (spec 3.2).
    public bool DayReportAfterEachCard
    {
        get => Settings.Outputs.DayReport;
        set { Settings.Outputs.DayReport = value; SettingsChanged(); Raise(); }
    }

    /// "Kopie reportu i na tomto počítači" (task 2026-10-09) — `outputs.localReport`.
    public bool LocalReportCopy
    {
        get => Settings.Outputs.LocalReport;
        set { Settings.Outputs.LocalReport = value; SettingsChanged(); Raise(); }
    }

    /// The folder the reports are copied to: the chosen one, else `<Dokumenty>\Offload reporty` (memory only).
    public string LocalReportFolderText => Settings.Outputs.LocalReportFolder ?? LocalReports.DefaultRoot;

    public bool LocalReportFolderCustom => Settings.Outputs.LocalReportFolder != null;

    public RelayCommand ChooseLocalReportFolderCommand { get; private set; } = null!;
    public RelayCommand DefaultLocalReportFolderCommand { get; private set; } = null!;

    void ChooseLocalReportFolder()
    {
        if (Dialogs.PickFolder("Vyberte složku pro kopie reportů na tomto počítači") is not string folder) return;
        Settings.Outputs.LocalReportFolder = folder;
        SettingsChanged();
        RaiseMany(nameof(LocalReportFolderText), nameof(LocalReportFolderCustom));
    }

    void DefaultLocalReportFolder()
    {
        Settings.Outputs.LocalReportFolder = null;
        SettingsChanged();
        RaiseMany(nameof(LocalReportFolderText), nameof(LocalReportFolderCustom));
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

    /// A missing LUT (custom, or an own rule's) blocks the start (red); missing ffmpeg only warns (orange). From the cached
    /// check — no disk here; while the files in use have not been checked yet, nothing blocks.
    public string? LutProblem =>
        lutCheckedKey != null && lutCheckedKey == ProxyPreflight.LutKey(LutFiles) ? ProxyPreflight.MissingText(lutMissing) : null;

    IReadOnlyList<LutFile> LutFiles => ProxyPreflight.LutFilesInUse(Settings.Proxy, Settings.AnalyzeClips);

    /// Checks the LUT files off the UI thread; a request during a check runs once more afterwards.
    void RefreshLut()
    {
        if (lutChecking)
        {
            lutRecheck = true;
            return;
        }
        var files = LutFiles;
        if (files.Count == 0)
        {
            SetLutState(null, null);
            return;
        }
        lutChecking = true;
        Task.Run(() => ProxyPreflight.FirstMissing(files)).ContinueWith(t =>
        {
            lutChecking = false;
            if (t.IsCompletedSuccessfully) SetLutState(ProxyPreflight.LutKey(files), t.Result);
            else AppLog.Write($"LUT check: {t.Exception?.GetBaseException().Message}");
            if (lutRecheck)
            {
                lutRecheck = false;
                RefreshLut();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// A new answer about the LUT files (`key` = which, null = none in use): the red text and the start button follow it.
    void SetLutState(string? key, LutFile? missing)
    {
        var before = LutProblem;
        lutCheckedKey = key;
        lutMissing = missing;
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
            folderTarget = null;
            folderFree = null;
            SetProxyFolderState(null, null);
            return;
        }
        folderChecking = true;
        Task.Run(() => ProbeFolder(folder)).ContinueWith(t =>
        {
            folderChecking = false;
            if (t.IsCompletedSuccessfully) ApplyFolderProbe(folder, t.Result);
            else AppLog.Write($"proxy folder check: {t.Exception?.GetBaseException().Message}");
            if (folderRecheck)
            {
                folderRecheck = false;
                RefreshProxyFolder();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// The proxy folder with its volume and free space (spec 2.5: the proxies count there too). Off the UI thread.
    static (bool Exists, string? Volume, long? Free) ProbeFolder(string folder) =>
        Directory.Exists(folder) ? (true, SpacePlanner.VolumeKey(folder), NativeIO.AvailableBytes(folder)) : (false, null, null);

    /// UI thread: keeps the answer for the space check and the red text - unless the folder was changed meanwhile.
    void ApplyFolderProbe(string folder, (bool Exists, string? Volume, long? Free) r)
    {
        if (ProxyPreflight.ExtraFolderInUse(Settings.Proxy) is not string now || !string.Equals(now, folder, StringComparison.OrdinalIgnoreCase)) return;
        folderTarget = r.Exists && r.Volume != null ? new SpaceTarget(folder, r.Volume, folder) : null;
        folderFree = r.Free;
        SetProxyFolderState(folder, r.Exists);
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

    /// Opens "Vlastní pravidla LUTů" (set by the window): the rules to edit, the built-in LUTs; the new rules or null.
    public Func<IReadOnlyList<LutRule>, LutCatalog, List<LutRule>?>? EditLutRulesDialog { get; set; }

    /// "Pravidla LUTů…": OK writes the rules into the active profile; their .cube files are checked again (off the UI thread).
    internal void EditLutRules(LutCatalog catalog)
    {
        if (EditLutRulesDialog?.Invoke(Settings.Proxy.LutRules, catalog) is not { } rules) return;
        Settings.Proxy.LutRules = rules;
        Proxy.RulesChanged();   // saves (ProxyChanged) and rechecks the LUT files
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
        resolvedTools = AllSettings.Tools.Resolved();
        Tools.Clear();
        void Row(string name, string purpose, string? path, Action<string> set) =>
            Tools.Add(new ToolRowViewModel(name, purpose, path, () =>
            {
                if (Dialogs.PickFile($"Vyber {name}", "Programy (*.exe)|*.exe") is not string picked) return;
                set(picked);
                SettingsChanged();
                RefreshTools();
            }));
        Row("ffmpeg", "proxy, náhledy", resolvedTools.Ffmpeg, p => AllSettings.Tools.Ffmpeg = p);
        Row("ffprobe", "metadata klipů", resolvedTools.Ffprobe, p => AllSettings.Tools.Ffprobe = p);
        Row("braw-decode", "Blackmagic RAW", resolvedTools.BrawDecoder, p => AllSettings.Tools.BrawDecoder = p);
        Row("REDline", "RED R3D", resolvedTools.Redline, p => AllSettings.Tools.Redline = p);
        Row("art-cmd", "ARRIRAW", resolvedTools.ArtCmd, p => AllSettings.Tools.ArtCmd = p);
        Raise(nameof(ToolsWarning));
    }
}
