using System.IO;
using Offload.Core;

namespace Offload.App;

/// One row in Cíle: number, name, free space and file system; red when the disk is not connected.
public sealed class DestinationViewModel : ObservableObject
{
    public DestinationViewModel(MainViewModel owner, int index, string root)
    {
        Index = index;
        Root = root;
        Name = Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root;
        Exists = Directory.Exists(root);
        if (Exists)
        {
            FileSystem = NativeIO.FilesystemType(root);
            FreeText = $"{Format.Bytes(NativeIO.AvailableBytes(root) ?? 0)} volno · {FileSystem}";
            if (FileSystem is "exFAT" or "FAT32" or "FAT") FsWarning = "Doporučeno NTFS — spolehlivější než exFAT/FAT32";
        }
        RemoveCommand = new RelayCommand(() => owner.RemoveDestination(root));
    }

    public int Index { get; }
    public string Root { get; }
    public string Name { get; }
    public bool Exists { get; }
    public string FileSystem { get; } = "";
    public string FreeText { get; } = "";
    public string? FsWarning { get; }
    public RelayCommand RemoveCommand { get; }
}
