using System.IO;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
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
    string fileSystem = "", freeText = "zjišťuji…";
    string? fsWarning, volume;
    long? freeBytes;
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
                var (ex, fs, free, volatileDisk, vol) = t.Result;
                exists = ex;
                mayDropOut = volatileDisk;
                volume = ex ? vol : null;
                freeBytes = ex ? free : null;
                fileSystem = fs;
                freeText = ex ? $"{(free is long f ? Format.Bytes(f) : "?")} volno · {fs}" : "";
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
            RaiseMany(nameof(Exists), nameof(FileSystem), nameof(FreeText), nameof(FsWarning), nameof(IsChecking), nameof(ShowMissing));
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

    static (bool, string, long?, bool, string?) ProbeDisk(string root)
    {
        bool mayDropOut = DestinationHealth.MayDropOut(root, DriveTypeOf(root));
        if (!Directory.Exists(root)) return (false, "", null, mayDropOut, null);
        return (true, NativeIO.FilesystemType(root), NativeIO.AvailableBytes(root), mayDropOut, SpacePlanner.VolumeKey(root));
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
    public string? FsWarning => fsWarning;
    /// For the space check (spec 2.5): the volume and its free bytes from the last probe; null until known or when missing.
    public SpaceTarget? SpaceTarget => exists && volume != null ? new SpaceTarget(Root, volume, HistoryText.DiskName(Root)) : null;
    public long? FreeBytes => freeBytes;
    public RelayCommand RemoveCommand { get; }
}