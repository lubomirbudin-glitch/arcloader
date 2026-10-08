using System.IO;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Hardware;
using Offload.Core.Space;

namespace Offload.App;

/// One row in Cíle: number, name, free space and file system; red when the disk is not connected.
/// The disk is probed off the UI thread; until then the row shows "zjišťuji…". A missing disk (a NAS that may come
/// back) and a present network or removable one (a NAS that may drop out) are probed again by MainViewModel every
/// few seconds, every row by the refresh button; one probe per row at a time.
public sealed class DestinationViewModel : ObservableObject
{
    readonly MainViewModel owner;
    bool exists, isChecking = true, probing, mayDropOut;
    string fileSystem = "", freeText = "zjišťuji…", compactFreeText = "";
    string? fsWarning, volume, spaceNote, barTone;
    long? freeBytes, totalBytes;
    long need;
    Task probeTask = Task.CompletedTask;

    public DestinationViewModel(MainViewModel owner, int index, string root)
    {
        this.owner = owner;
        Index = index;
        Root = root;
        Name = Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root;
        RemoveCommand = new RelayCommand(() => owner.RemoveDestination(root));
        Probe();
    }

    /// Probes the disk off the UI thread; while a probe runs (a dead network path can take long) no second one starts.
    /// The row keeps showing the last answer until the new one arrives.
    public Task Probe()
    {
        if (probing) return probeTask;
        probing = true;
        var root = Root;
        return probeTask = Task.Run(() => ProbeDisk(root)).ContinueWith(t =>
        {
            probing = false;
            if (!owner.IsCurrent(this)) return;   // the row was replaced meanwhile
            bool wasChecking = isChecking, before = exists;
            string beforeText = freeText;
            if (t.IsCompletedSuccessfully)
            {
                var (ex, fs, free, volatileDisk, vol, total, connection) = t.Result;
                exists = ex;
                mayDropOut = volatileDisk;
                volume = ex ? vol : null;
                freeBytes = ex ? free : null;
                totalBytes = ex ? total : null;
                fileSystem = fs;
                // "7,20 TB volných z 8 TB · NTFS · NVMe" (UI návrh obr. 1); "7,20 TB volno" in the compact list.
                freeText = ex ? (free is long f ? DestinationTile.FreeText(f, total, fs, connection) : $"? volno · {fs}") : "";
                compactFreeText = ex && free is long cf ? DestinationTile.CompactFree(cf) : "";
                fsWarning = ex && fs is "exFAT" or "FAT32" or "FAT" ? "Doporučeno NTFS — spolehlivější než exFAT/FAT32" : null;
            }
            else
            {
                AppLog.Write($"destination probe {root}: {t.Exception?.GetBaseException().Message}");
                exists = false;
                volume = null;
                freeBytes = null;
                freeText = "";
            }
            isChecking = false;
            if (!wasChecking && before == exists && beforeText == freeText) return;   // nothing new
            RaiseMany(nameof(Exists), nameof(FileSystem), nameof(FreeText), nameof(CompactFreeText), nameof(FsWarning), nameof(IsChecking), nameof(ShowMissing),
                nameof(HasBar), nameof(BarUsed), nameof(BarNeed), nameof(BarRest));
            owner.DestinationInfoChanged();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// A probe that started after this call: waits for one in flight (its answer may be older than what the caller
    /// needs — the free space after a copy), then probes again. Never throws; the row keeps its last answer on failure.
    public async Task ProbeFreshAsync()
    {
        if (probing) await probeTask;
        await Probe();
    }

    static (bool, string, long?, bool, string?, long?, string?) ProbeDisk(string root)
    {
        bool mayDropOut = DestinationHealth.MayDropOut(root, DriveTypeOf(root));
        if (!Directory.Exists(root)) return (false, "", null, mayDropOut, null, null, null);
        // The connection from the HW diagnostics (UI návrh 6): never throws, "unknown" is null.
        var connection = ConnectionLabel.Short(DriveLinks.Resolve(root), card: false);
        return (true, NativeIO.FilesystemType(root), NativeIO.AvailableBytes(root), mayDropOut, SpacePlanner.VolumeKey(root), NativeIO.TotalBytes(root), connection);
    }

    /// The type of the root's drive letter (network, removable, fixed…), null for a UNC path or when unknown.
    static DriveType? DriveTypeOf(string root)
    {
        try
        {
            if (root.StartsWith(@"\\", StringComparison.Ordinal) || Path.GetPathRoot(Path.GetFullPath(root)) is not { Length: > 0 } letter) return null;
            return new DriveInfo(letter).DriveType;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public int Index { get; }
    public string Root { get; }
    public string Name { get; }
    public bool Exists => exists;
    public bool IsChecking => isChecking;
    public bool ShowMissing => !isChecking && !exists;
    /// For the 5 s health check: missing, or present on a disk that may vanish unannounced (network, removable).
    public bool ShouldReprobe => DestinationHealth.ShouldReprobe(isChecking, exists, mayDropOut);
    public string FileSystem => fileSystem;
    public string FreeText => freeText;
    public string CompactFreeText => compactFreeText;
    public string RemoveName => $"Odebrat cíl {Name}";

    // The space bar (UI návrh obr. 1): grey = used, blue = what the ticked cards need here; yellow / red with the space check.
    public bool HasBar => exists && totalBytes > 0 && freeBytes != null;
    (double Used, double Need) Bar => HasBar ? DestinationTile.Bar(totalBytes!.Value, freeBytes!.Value, need) : (0, 0);
    public System.Windows.GridLength BarUsed => new(Math.Max(Bar.Used, 0.0001), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength BarNeed => new(Math.Max(Bar.Need, 0.0001), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength BarRest => new(Math.Max(1 - Bar.Used - Bar.Need, 0.0001), System.Windows.GridUnitType.Star);
    /// null, "Warning" or "Blocking" (SpaceSeverity) — the bar's and the note's colour.
    public string? BarTone => barTone;
    /// The space check's sentence about this disk ("Na cíli … není dost místa …"), or null.
    public string? SpaceNote => spaceNote;

    /// The window's space check ran (3× a second): raised only when something changed.
    internal void SetSpace(long needHere, SpaceSeverity? severity, string? note)
    {
        var tone = severity?.ToString();
        if (needHere == need && tone == barTone && note == spaceNote) return;
        need = needHere;
        barTone = tone;
        spaceNote = note;
        RaiseMany(nameof(BarUsed), nameof(BarNeed), nameof(BarRest), nameof(BarTone), nameof(SpaceNote));
    }
    public string? FsWarning => fsWarning;
    /// For the space check (spec 2.5): the volume and its free bytes from the last probe; null until known or when missing.
    public SpaceTarget? SpaceTarget => exists && volume != null ? new SpaceTarget(Root, volume, HistoryText.DiskName(Root)) : null;
    public long? FreeBytes => freeBytes;
    public RelayCommand RemoveCommand { get; }
}