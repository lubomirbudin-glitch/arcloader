using System.IO;
using Offload.Core;
using Offload.Core.Desktop;

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
    string? fsWarning;

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
    public void Probe()
    {
        if (probing) return;
        probing = true;
        var root = Root;
        Task.Run(() => ProbeDisk(root)).ContinueWith(t =>
        {
            probing = false;
            if (!owner.IsCurrent(this)) return;   // the row was replaced meanwhile
            bool wasChecking = isChecking, before = exists;
            string beforeText = freeText;
            if (t.IsCompletedSuccessfully)
            {
                var (ex, fs, free, volatileDisk) = t.Result;
                exists = ex;
                mayDropOut = volatileDisk;
                fileSystem = fs;
                freeText = ex ? $"{(free is long f ? Format.Bytes(f) : "?")} volno · {fs}" : "";
                fsWarning = ex && fs is "exFAT" or "FAT32" or "FAT" ? "Doporučeno NTFS — spolehlivější než exFAT/FAT32" : null;
            }
            else
            {
                AppLog.Write($"destination probe {root}: {t.Exception?.GetBaseException().Message}");
                exists = false;
                freeText = "";
            }
            isChecking = false;
            if (!wasChecking && before == exists && beforeText == freeText) return;   // nothing new
            RaiseMany(nameof(Exists), nameof(FileSystem), nameof(FreeText), nameof(FsWarning), nameof(IsChecking), nameof(ShowMissing));
            owner.DestinationInfoChanged();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    static (bool, string, long?, bool) ProbeDisk(string root)
    {
        bool mayDropOut = DestinationHealth.MayDropOut(root, DriveTypeOf(root));
        if (!Directory.Exists(root)) return (false, "", null, mayDropOut);
        return (true, NativeIO.FilesystemType(root), NativeIO.AvailableBytes(root), mayDropOut);
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
    public RelayCommand RemoveCommand { get; }
}