using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

/// A mounted volume offered in the Karty section (Mac `VolumeItem`). Name: "EOS_DIGITAL (F:)".
public sealed record VolumeItem(string Name, string Path, long Total, long Free, string Format, bool LooksLikeCard, bool IsDestination);

public static class Volumes
{
    static readonly HashSet<string> Markers = new(StringComparer.OrdinalIgnoreCase)
        { "DCIM", "PRIVATE", "CLIP", "CONTENTS", "XDROOT", "M4ROOT", "AVF_INFO", "CANONMSC", "BRAW", "MISC" };

    static readonly HashSet<string> MediaExt = new(StringComparer.OrdinalIgnoreCase)
        { ".braw", ".r3d", ".rdm", ".rdc", ".mxf", ".mov", ".mp4", ".ari", ".crm" };

    /// Typical camera media (1:1 with the Mac): a known top-level folder, or clips at most one folder down
    /// (ARRI/RED/Blackmagic keep each clip or reel in its own folder).
    public static bool LooksLikeCameraCard(string path)
    {
        static bool HasMedia(IEnumerable<string> names) => names.Any(n => MediaExt.Contains(System.IO.Path.GetExtension(n)));
        try
        {
            var items = Directory.GetFileSystemEntries(path).Select(e => System.IO.Path.GetFileName(e)).ToList();
            if (items.Any(Markers.Contains) || HasMedia(items)) return true;
            foreach (var dir in Directory.GetDirectories(path).Where(d => !System.IO.Path.GetFileName(d).StartsWith('.')).Take(50))
            {
                try
                {
                    if (HasMedia(Directory.GetFileSystemEntries(dir))) return true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // System Volume Information and friends.
                }
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// Offered as a card source: removable media, or a fixed disk on USB/SD/MMC; never the system drive.
    public static bool IsCandidate(DriveType type, StorageBus bus, bool isSystem) =>
        !isSystem && (type == DriveType.Removable
                      || (type == DriveType.Fixed && bus is StorageBus.Usb or StorageBus.Sd or StorageBus.Mmc));

    /// Card candidates, sorted by name like Explorer does. Read-only; a drive pulled out mid-scan is skipped.
    public static List<VolumeItem> Scan(IReadOnlyList<string> destRoots)
    {
        var system = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var found = new List<VolumeItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType is not (DriveType.Removable or DriveType.Fixed) || !d.IsReady) continue;
                var root = d.RootDirectory.FullName;
                bool isSystem = string.Equals(root, system, StringComparison.OrdinalIgnoreCase);
                var bus = d.DriveType == DriveType.Fixed && !isSystem ? DriveLinks.Resolve(root).Bus : StorageBus.Unknown;
                if (!IsCandidate(d.DriveType, bus, isSystem)) continue;
                bool isDest = destRoots.Any(r => HwTest.SameVolume(r, root));
                var letter = root.TrimEnd((char)92);
                var name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? letter : $"{d.VolumeLabel} ({letter})";
                found.Add(new VolumeItem(name, root, d.TotalSize, d.AvailableFreeSpace, d.DriveFormat,
                    !isDest && LooksLikeCameraCard(root), isDest));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Pulled out while we looked at it.
            }
        }
        found.Sort((a, b) => Win32.StrCmpLogicalW(a.Name, b.Name));
        return found;
    }

    /// The selection after a rescan (Mac `refreshVolumes`): a card that just appeared is ticked; a source that
    /// disappeared or now holds a destination drops out; manual folders stay.
    public static HashSet<string> SelectionAfterRefresh(IEnumerable<string> previousPaths, IReadOnlyList<VolumeItem> found,
        IEnumerable<string> selected, IReadOnlyList<string> manual)
    {
        var previous = new HashSet<string>(previousPaths, StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        foreach (var v in found)
            if (v.LooksLikeCard && !previous.Contains(v.Path)) result.Add(v.Path);
        result.RemoveWhere(p =>
            !found.Any(v => string.Equals(v.Path, p, StringComparison.OrdinalIgnoreCase) && !v.IsDestination)
            && !manual.Contains(p, StringComparer.OrdinalIgnoreCase));
        return result;
    }
}
