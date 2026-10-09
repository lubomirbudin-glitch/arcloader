using System.Text.RegularExpressions;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

/// A mounted volume offered in the Karty section (Mac `VolumeItem`). Name: "EOS_DIGITAL (F:)".
/// `Identity` (`CardIdentity`) tells two cards in the same slot apart. `Label`: the volume label as scanned (the
/// default card name — the UI thread never reads it from the disk).
public sealed record VolumeItem(string Name, string Path, long Total, long Free, string Format, bool LooksLikeCard, bool IsDestination,
    string? Identity = null, string? Label = null)
{
    /// What the automatic choice of cards and destinations needs (AutoDrives), gathered by the scan; null in a fixture.
    public DriveFacts? Facts { get; init; }
}

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

    /// Top-level folders of camera media the destination check knows besides Markers (ARRI, RED).
    static readonly HashSet<string> DeepMarkers = new(Markers.Concat(["ARRI", "RDC"]), StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> OwnFolders = new([Layout.ReportDirName, Layout.ProxyDirName, Mhl.DirName], StringComparer.OrdinalIgnoreCase);

    /// The stricter check before a volume may become a destination (task "Automatické načtení disků"): LooksLikeCameraCard,
    /// ARRI and RED folders at the top, or clips two folders down (an ARRI Codex drive: reel\clip\clip.mxf). A date
    /// folder (an offload backup: date\card\…) and our own folders are not looked into. Reads only; false when unreadable.
    public static bool LooksLikeCameraMedia(string path)
    {
        if (LooksLikeCameraCard(path)) return true;
        static bool HasMedia(string dir)
        {
            try
            {
                return Directory.EnumerateFileSystemEntries(dir).Take(500).Any(n => MediaExt.Contains(System.IO.Path.GetExtension(n)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        try
        {
            if (Directory.EnumerateDirectories(path).Any(d => DeepMarkers.Contains(System.IO.Path.GetFileName(d)))) return true;
            foreach (var top in Directory.EnumerateDirectories(path).Where(d => Looked(System.IO.Path.GetFileName(d))).Take(20))
            {
                try
                {
                    if (Directory.EnumerateDirectories(top).Where(d => Looked(System.IO.Path.GetFileName(d))).Take(20).Any(HasMedia)) return true;
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

    /// A folder the deeper check looks into: not hidden, not ours, not a date folder of an offload backup.
    static bool Looked(string name) =>
        !name.StartsWith('.') && !name.StartsWith('$') && !OwnFolders.Contains(name)
        && !string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase)
        && !DateTime.TryParseExact(name, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

    /// Root folders of an ARRI drive (Codex, SxS): the reel ("A_0009", "A002R1AB") holding clip folders ("A_0009C001_…",
    /// "A002C001_…").
    static readonly Regex ArriReel = new(@"^[A-Z](_\d{4}|\d{3}R[A-Z0-9]{3,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex ArriClip = new(@"^[A-Z]_?\d{3,4}C\d{3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// A camera's own structure at the root of the volume — what keeps it from becoming a destination by itself (ruling
    /// 2026-10-09): a camera folder (Markers, ARRI), RED's .RDM/.RDC folders, an ARRI reel with clip folders, or clips
    /// right in the root (Blackmagic, Atomos and BRAW SSDs record there). Clips further down do not count: that is how a
    /// backup looks. Reads the root and at most 50 reel folders; false when unreadable.
    public static bool CameraAtRoot(string root)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root).Take(2000))
            {
                var name = System.IO.Path.GetFileName(entry);
                if (File.Exists(entry))
                {
                    if (MediaExt.Contains(System.IO.Path.GetExtension(name))) return true;
                    continue;
                }
                if (Markers.Contains(name) || string.Equals(name, "ARRI", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".RDM", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".RDC", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            foreach (var reel in Directory.EnumerateDirectories(root).Where(d => ArriReel.IsMatch(System.IO.Path.GetFileName(d))).Take(50))
            {
                try
                {
                    if (Directory.EnumerateDirectories(reel).Take(200).Any(d => ArriClip.IsMatch(System.IO.Path.GetFileName(d)))) return true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// Offload's own output on the volume within `maxDepth` folders (ruling 2026-10-09): a `_REPORT` with an Offload
    /// report or `offload-manifest.jsonl`, or an `ascmhl` folder — `<date>\<card>\_REPORT` is three down. Breadth first,
    /// bounded (at most `budget` folders, 200 per folder), stops at the first hit; hidden and system folders are skipped.
    /// Reads only — never call it on the UI thread.
    public static bool HasOffloadOutput(string root, int maxDepth = 3, int budget = 3000)
    {
        var level = new List<string> { root };
        for (int depth = 1; depth <= maxDepth && level.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var dir in level)
            {
                IEnumerable<string> subs;
                try
                {
                    subs = Directory.EnumerateDirectories(dir).Take(200).ToList();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    continue;
                }
                foreach (var sub in subs)
                {
                    var name = System.IO.Path.GetFileName(sub);
                    if (string.Equals(name, Mhl.DirName, StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(name, Layout.ReportDirName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsOffloadReportDir(sub)) return true;
                        continue;
                    }
                    if (name.StartsWith('.') || name.StartsWith('$') || string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;
                    if (--budget <= 0) return false;
                    next.Add(sub);
                }
            }
            level = next;
        }
        return false;
    }

    static bool IsOffloadReportDir(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir).Take(500).Select(f => System.IO.Path.GetFileName(f)).Any(n =>
                string.Equals(n, VerifyInProxy.ManifestFile.FileName, StringComparison.OrdinalIgnoreCase)
                || (n.StartsWith("offload_report", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// Nothing on the volume but what Windows keeps on every one. Reads only; unreadable = not empty.
    public static bool IsEmptyVolume(string root)
    {
        try
        {
            return !Directory.EnumerateFileSystemEntries(root).Any(e => System.IO.Path.GetFileName(e) is var n
                && !string.Equals(n, "System Volume Information", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(n, "$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// Offered as a card source: removable media, or a fixed disk on USB/SD/MMC; never the system drive.
    public static bool IsCandidate(DriveType type, StorageBus bus, bool isSystem) =>
        !isSystem && (type == DriveType.Removable
                      || (type == DriveType.Fixed && bus is StorageBus.Usb or StorageBus.Sd or StorageBus.Mmc));

    /// Card candidates, sorted by name like Explorer does. Read-only; a drive pulled out mid-scan is skipped.
    /// `skip`: roots not to touch at all — a card just ejected from a reader that cannot eject media stays in its
    /// slot dismounted, and any access (even `IsReady`) would mount it again.
    public static List<VolumeItem> Scan(IReadOnlyList<string> destRoots, IReadOnlyCollection<string>? skip = null)
    {
        var system = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var found = new List<VolumeItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (skip != null && skip.Contains(d.Name, StringComparer.OrdinalIgnoreCase)) continue;
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
                bool card = !isDest && LooksLikeCameraCard(root);
                found.Add(new VolumeItem(name, root, d.TotalSize, d.AvailableFreeSpace, d.DriveFormat,
                    card, isDest, CardIdentity.Of(root), d.VolumeLabel) { Facts = FactsOf(d, root, name, bus, card, isDest) });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Pulled out while we looked at it.
            }
        }
        found.Sort((a, b) => Win32.StrCmpLogicalW(a.Name, b.Name));
        return found;
    }

    /// The facts of a listed volume for AutoDrives — only what its role needs: the reader's device for a fixed USB disk,
    /// the deeper camera check for a disk that may become a destination, emptiness for a reader's card. Never throws.
    static DriveFacts FactsOf(DriveInfo d, string root, string name, StorageBus bus, bool card, bool isDest)
    {
        var facts = new DriveFacts
        {
            Root = root, Name = name, Type = d.DriveType, Bus = bus, LooksLikeCard = card,
            Total = d.TotalSize, Free = d.AvailableFreeSpace,
        };
        try
        {
            facts = facts with { DiskId = AutoDrives.DiskId(NativeIO.VolumeSerial(root), d.TotalSize), ReadOnly = NativeIO.IsReadOnlyVolume(root) };
            if (d.DriveType == DriveType.Removable || (d.DriveType == DriveType.Fixed && bus == StorageBus.Usb))
            {
                var (deviceBus, product, disks) = DriveLinks.Device(root);
                facts = facts with { Bus = bus == StorageBus.Unknown ? deviceBus : bus, Product = product, DisksOnDevice = disks };
            }
            var role = AutoDrives.Role(facts);
            // Only a disk that may become a destination is looked at deeper (one that already is one is not).
            if (role == DriveRole.Disk && !isDest && LooksLikeCameraMedia(root)) facts = facts with { LooksLikeCard = true };
            // The destination decision (ruling 2026-10-09): Offload output settles it, else the camera structure at the root.
            if (!isDest && !facts.IsSystem && !AutoDrives.ReaderLike(facts) && d.DriveType == DriveType.Fixed && facts.Bus == StorageBus.Usb)
            {
                bool output = HasOffloadOutput(root);
                facts = facts with { OffloadOutput = output, CameraAtRoot = !output && CameraAtRoot(root) };
            }
            if (role == DriveRole.ReaderSlot) facts = facts with { Empty = IsEmptyVolume(root) };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Pulled out meanwhile: what is known stays; an unknown identity is never added.
        }
        return facts;
    }

    /// Every ready fixed or removable disk by its root (the Hardware test with no card and no destination chosen).
    /// `skip` (ejected cards) is left out before anything asks the drive — even `IsReady` would mount the card again.
    /// Touches the drives — never call it on the UI thread.
    public static List<string> ReadyDiskRoots(IReadOnlyCollection<string> skip)
    {
        var roots = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (skip.Contains(d.Name, StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                if (d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady) roots.Add(d.RootDirectory.FullName);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Pulled out while we looked at it.
            }
        }
        return roots;
    }

    /// The paths whose typed card name no longer belongs to the card there: the volume is gone, or another card is in
    /// its drive (both identities known and different — a fast swap on the same letter).
    public static List<string> NamesToDrop(IReadOnlyList<VolumeItem> before, IReadOnlyList<VolumeItem> after)
    {
        var drop = new List<string>();
        foreach (var b in before)
        {
            var now = after.FirstOrDefault(a => string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            if (now == null || (b.Identity != null && now.Identity != null && !string.Equals(b.Identity, now.Identity, StringComparison.OrdinalIgnoreCase)))
                drop.Add(b.Path);
        }
        return drop;
    }

    /// The drive roots in a `DEV_BROADCAST_VOLUME.dbcv_unitmask` (bit 0 = A:).
    public static List<string> RootsFromUnitMask(uint mask)
    {
        var roots = new List<string>();
        for (int i = 0; i < 26; i++)
            if ((mask & (1u << i)) != 0) roots.Add($"{(char)('A' + i)}:\\");
        return roots;
    }

    /// The selection after a rescan (Mac `refreshVolumes`): a card that just appeared is ticked (`autoCards`, the
    /// setting "karty ve čtečkách jako zdroje": camera folders, or a reader's card that is not empty — AutoDrives); a
    /// source that disappeared or now holds a destination drops out; manual folders stay.
    public static HashSet<string> SelectionAfterRefresh(IEnumerable<string> previousPaths, IReadOnlyList<VolumeItem> found,
        IEnumerable<string> selected, IReadOnlyList<string> manual, bool autoCards = true)
    {
        var previous = new HashSet<string>(previousPaths, StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        foreach (var v in found)
        {
            if (v.IsDestination || previous.Contains(v.Path)) continue;
            bool tick = v.Facts is { } f ? AutoDrives.TickOnArrival(f with { LooksLikeCard = v.LooksLikeCard || f.LooksLikeCard }, autoCards)
                : autoCards && v.LooksLikeCard;
            if (tick) result.Add(v.Path);
        }
        result.RemoveWhere(p =>
            !found.Any(v => string.Equals(v.Path, p, StringComparison.OrdinalIgnoreCase) && !v.IsDestination)
            && !manual.Contains(p, StringComparer.OrdinalIgnoreCase));
        return result;
    }
}
