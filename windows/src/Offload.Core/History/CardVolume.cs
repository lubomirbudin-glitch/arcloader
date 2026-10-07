using Offload.Core.Desktop;

namespace Offload.Core.History;

/// The card volume behind a source that is a drive root, as history stores it (spec 5.2 "card"). Touches the drive —
/// only on a worker thread (the copy thread, a card inspection, the CLI).
public sealed record CardVolume(string? Identity, string FsSerial, long Capacity, string? Label, string Filesystem)
{
    public CardKey Key => new(FsSerial, Capacity);

    public HistoryCard ToHistoryCard() =>
        new() { Identity = Identity, FsSerial = FsSerial, Capacity = Capacity, Label = Label, Filesystem = Filesystem };

    /// Null for a folder (not a drive root) or a volume that cannot be read.
    public static CardVolume? Of(string source)
    {
        try
        {
            if (Layout.DriveRootOf(source) is not string root) return null;
            if (NativeIO.VolumeSerial(root) is not uint serial || NativeIO.TotalBytes(root) is not long capacity) return null;
            return new CardVolume(CardIdentity.Of(root), serial.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), capacity,
                NativeIO.VolumeLabel(root), NativeIO.FilesystemType(root));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
