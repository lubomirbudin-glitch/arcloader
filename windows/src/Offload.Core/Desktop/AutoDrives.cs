using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

/// What the volume scan learnt about a connected volume (off the UI thread) for the automatic choice of cards and
/// destinations (task "Automatické načtení disků", 2026-10-09).
public sealed record DriveFacts
{
    public required string Root { get; init; }
    /// "BACKUP (G:)" — how a hint names it.
    public string Name { get; init; } = "";
    public DriveType Type { get; init; }
    public StorageBus Bus { get; init; }
    public bool IsSystem { get; init; }
    /// The device's own name from the storage descriptor ("WD Elements 25A2", "Generic STORAGE DEVICE").
    public string Product { get; init; } = "";
    /// Disks on the same USB device (cfgmgr32): a multi-slot card reader has one per slot.
    public int DisksOnDevice { get; init; } = 1;
    /// Camera folders or clips on it (Volumes.LooksLikeCameraMedia — also two folders down, ARRI and RED).
    public bool LooksLikeCard { get; init; }
    /// Nothing on it but what Windows puts on every volume.
    public bool Empty { get; init; }
    public bool ReadOnly { get; init; }
    public long Total { get; init; }
    public long Free { get; init; }
    /// "1A2B3C4D:2000398934016" — the volume serial and the capacity (AutoDrives.DiskId); null when unknown.
    public string? DiskId { get; init; }
}

/// What a connected volume is to the automatic choice.
public enum DriveRole
{
    /// The system disk or a disk that is not on USB (SATA, NVMe — also a Thunderbolt one, Windows cannot tell): never touched.
    Internal,
    /// A card reader's slot (removable media, the SD bus, a reader's product name, several disks on one USB device).
    ReaderSlot,
    /// Camera folders on it: a card, wherever it sits.
    Card,
    /// An external USB disk: a destination.
    Disk,
}

/// A destination the app added by itself: its root and its disk (serial + capacity) — dropped again when the disk goes.
public sealed record AutoDestination(
    [property: JsonPropertyName("root")] string Root,
    [property: JsonPropertyName("disk")] string Disk);

public sealed record AutoDriveInput(
    IReadOnlyList<DriveFacts> Present,
    IReadOnlyList<string> DestRoots,
    IReadOnlyList<AutoDestination> AutoAdded,
    IReadOnlyList<string> Removed,
    bool AddDisks,
    bool Locked);

/// `Add`: destinations to add (root + disk); `Drop`: roots of auto-added destinations whose disk is gone; `Hints`: why
/// a connected disk was not added (full, read-only), Czech.
public sealed record AutoDrivePlan(IReadOnlyList<AutoDestination> Add, IReadOnlyList<string> Drop, IReadOnlyList<string> Hints)
{
    public static readonly AutoDrivePlan None = new([], [], []);
    public bool Changes => Add.Count > 0 || Drop.Count > 0;
}

/// The decisions — pure, the scan gathers the facts. A disk is never a card: the camera folders, the reader and the bus
/// say so before anything else; only an external USB disk with a known identity is ever added as a destination.
public static class AutoDrives
{
    /// Less free space than this: "plný" — not added, a hint instead.
    public const long FullBelow = 1L << 30;

    static readonly string[] ReaderWords =
        ["reader", "card", "cfexpress", "cfast", "xqd", "sd/mmc", "sdxc", "sdhc", "microsd", "mrw-", "multi-slot", "multislot", "storage device"];

    /// "1A2B3C4D:2000398934016": the volume serial and the capacity — how a removed destination is remembered (a letter
    /// changes, a disk keeps both). Null when either is unknown.
    public static string? DiskId(uint? serial, long total) =>
        serial is uint s && total > 0 ? $"{s:X8}:{total.ToString(CultureInfo.InvariantCulture)}" : null;

    /// A reader's slot by what Windows says of the device (the card in it may be a fixed disk to Windows — CFexpress).
    /// Removable media counts too (a USB flash drive as well: never a destination by itself).
    public static bool ReaderLike(DriveFacts f) => f.Type == DriveType.Removable || KnownReader(f);

    /// The device is a card reader beyond doubt: the SD bus, several disks on one USB device (a slot each), or a reader's
    /// product name — not just removable media, which a USB flash drive is too.
    public static bool KnownReader(DriveFacts f) =>
        f.Bus is StorageBus.Sd or StorageBus.Mmc || f.DisksOnDevice > 1
        || ReaderWords.Any(w => f.Product.Contains(w, StringComparison.OrdinalIgnoreCase));

    public static DriveRole Role(DriveFacts f)
    {
        if (f.IsSystem) return DriveRole.Internal;
        if (f.LooksLikeCard) return DriveRole.Card;
        if (ReaderLike(f)) return DriveRole.ReaderSlot;
        return f.Type == DriveType.Fixed && f.Bus == StorageBus.Usb ? DriveRole.Disk : DriveRole.Internal;
    }

    /// A volume that just appeared is ticked in Karty: camera folders, or a card that is not empty in a known card reader
    /// (a sound recorder's card has no camera folders; a USB flash drive is no reader). Nothing when the setting is off.
    public static bool TickOnArrival(DriveFacts f, bool addCards) =>
        addCards && Role(f) switch
        {
            DriveRole.Card => true,
            DriveRole.ReaderSlot => !f.Empty && KnownReader(f),
            _ => false,
        };

    /// The destinations to add and drop now. `Locked` (a Start checking the profile shown — ruling I1 — or the demo):
    /// nothing. Never a disk that is removed (by its identity), already a destination (a root or a folder on it), full
    /// or read-only (a hint says why), or of unknown identity. An auto-added destination whose disk is not connected
    /// any more (or another disk took its letter) is dropped — also with the setting off; one the user added is kept.
    public static AutoDrivePlan Plan(AutoDriveInput input)
    {
        if (input.Locked) return AutoDrivePlan.None;
        var removed = new HashSet<string>(input.Removed, StringComparer.OrdinalIgnoreCase);
        var drop = new List<string>();
        foreach (var auto in input.AutoAdded)
        {
            if (!input.DestRoots.Contains(auto.Root, StringComparer.OrdinalIgnoreCase)) continue;
            bool there = input.Present.Any(f => SameRoot(f.Root, auto.Root) && string.Equals(f.DiskId, auto.Disk, StringComparison.OrdinalIgnoreCase));
            if (!there) drop.Add(auto.Root);
        }
        var dests = input.DestRoots.Where(d => !drop.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
        var add = new List<AutoDestination>();
        var hints = new List<string>();
        if (input.AddDisks)
        {
            foreach (var f in input.Present)
            {
                if (Role(f) != DriveRole.Disk || f.DiskId is not string id || removed.Contains(id)) continue;
                if (dests.Any(d => SameRoot(RootOf(d), f.Root))) continue;
                if (f.ReadOnly) { hints.Add(Msg.AutoDiskReadOnly(f.Name)); continue; }
                if (f.Free < FullBelow) { hints.Add(Msg.AutoDiskFull(f.Name)); continue; }
                add.Add(new AutoDestination(f.Root, id));
            }
        }
        return new AutoDrivePlan(add, drop, hints);
    }

    static bool SameRoot(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static string RootOf(string path)
    {
        try { return Path.GetPathRoot(path) ?? path; }
        catch (ArgumentException) { return path; }
    }

    /// The disk a removed destination stood on, to be remembered (task: "odebraný cíl si zapamatovat"): the connected
    /// external disk holding `root`, or null (a folder on an internal disk, a network path, a disk not connected).
    public static string? RemovedDisk(string root, IEnumerable<DriveFacts> present) =>
        present.FirstOrDefault(f => Role(f) == DriveRole.Disk && SameRoot(RootOf(root), f.Root))?.DiskId;
}

/// The automatic choice in settings.json (per profile, plan 8): `autoDrives`.
public sealed class AutoDriveSettings
{
    /// "Automaticky přidávat připojené disky jako cíle".
    [JsonPropertyName("destinations")] public bool Destinations { get; set; } = true;
    /// "Automaticky označovat karty ve čtečkách jako zdroje".
    [JsonPropertyName("cards")] public bool Cards { get; set; } = true;
    /// The destinations the app added by itself (dropped again when their disk goes).
    [JsonPropertyName("added")] public List<AutoDestination> Added { get; set; } = [];
    /// Disks (AutoDrives.DiskId) the user removed from Cíle — never added again by themselves.
    [JsonPropertyName("removed")] public List<string> Removed { get; set; } = [];

    /// Keys of this section a newer version wrote: kept and written back (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
