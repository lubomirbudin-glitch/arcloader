using System.IO;
using Offload.Core;

namespace Offload.App;

/// One row in Cíle: number, name, free space and file system; red when the disk is not connected.
/// The disk is probed off the UI thread; until then the row shows "zjišťuji…".
public sealed class DestinationViewModel : ObservableObject
{
    readonly MainViewModel owner;
    bool exists, isChecking = true;
    string fileSystem = "", freeText = "zjišťuji…";
    string? fsWarning;

    public DestinationViewModel(MainViewModel owner, int index, string root)
    {
        this.owner = owner;
        Index = index;
        Root = root;
        Name = Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root;
        RemoveCommand = new RelayCommand(() => owner.RemoveDestination(root));
        Task.Run(() => Probe(root)).ContinueWith(t =>
        {
            if (!owner.IsCurrent(this)) return;   // the row was replaced meanwhile
            if (t.IsCompletedSuccessfully)
            {
                var (ex, fs, free) = t.Result;
                exists = ex;
                fileSystem = fs;
                freeText = ex ? $"{(free is long f ? Format.Bytes(f) : "?")} volno · {fs}" : "";
                fsWarning = ex && fs is "exFAT" or "FAT32" or "FAT" ? "Doporučeno NTFS — spolehlivější než exFAT/FAT32" : null;
            }
            else
            {
                AppLog.Write($"destination probe {root}: {t.Exception?.GetBaseException().Message}");
                freeText = "";
            }
            isChecking = false;
            RaiseMany(nameof(Exists), nameof(FileSystem), nameof(FreeText), nameof(FsWarning), nameof(IsChecking), nameof(ShowMissing));
            owner.DestinationInfoChanged();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    static (bool, string, long?) Probe(string root)
    {
        if (!Directory.Exists(root)) return (false, "", null);
        return (true, NativeIO.FilesystemType(root), NativeIO.AvailableBytes(root));
    }

    public int Index { get; }
    public string Root { get; }
    public string Name { get; }
    public bool Exists => exists;
    public bool IsChecking => isChecking;
    public bool ShowMissing => !isChecking && !exists;
    public string FileSystem => fileSystem;
    public string FreeText => freeText;
    public string? FsWarning => fsWarning;
    public RelayCommand RemoveCommand { get; }
}