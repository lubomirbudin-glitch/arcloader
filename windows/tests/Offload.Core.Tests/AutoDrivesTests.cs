using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

/// Task "Automatické načtení disků" (2026-10-09): connected USB disks become destinations by themselves, cards in readers
/// are ticked as sources — never a card, a reader slot or an internal disk as a destination; a removed one stays removed.
public class AutoDrivesTests
{
    const long GB = 1L << 30;

    static DriveFacts Disk(string root, string id = "AAAA0001:2000000000000", long free = 500 * GB, bool readOnly = false, string product = "WD Elements 25A2") =>
        new() { Root = root, Name = $"BACKUP ({root.TrimEnd('\\')})", Type = DriveType.Fixed, Bus = StorageBus.Usb, Product = product, DiskId = id, Free = free, Total = 2000 * GB, ReadOnly = readOnly };

    static DriveFacts Reader(string root, bool empty = false) =>
        new() { Root = root, Name = $"NO NAME ({root.TrimEnd('\\')})", Type = DriveType.Removable, Bus = StorageBus.Usb, Product = "Generic STORAGE DEVICE", DiskId = "BBBB0002:64000000000", Free = 60 * GB, Total = 64 * GB, Empty = empty };

    static DriveFacts Card(string root) => Disk(root, "CCCC0003:512000000000") with { LooksLikeCard = true, Product = "Sony MRW-G2" };

    static AutoDriveInput Input(IReadOnlyList<DriveFacts> present, IReadOnlyList<string>? dests = null, IReadOnlyList<AutoDestination>? auto = null,
        IReadOnlyList<string>? removed = null, bool addDisks = true, bool locked = false) =>
        new(present, dests ?? [], auto ?? [], removed ?? [], addDisks, locked);

    // ── what a volume is ────────────────────────────────────────────────

    [Fact]
    public void Role_UsbDisk_IsDisk()
    {
        Assert.Equal(DriveRole.Disk, AutoDrives.Role(Disk(@"G:\")));
    }

    [Fact]
    public void Role_CameraStructure_IsCard_EvenOnAFixedUsbDisk()
    {
        // A CFexpress reader that shows the card as a fixed USB disk: the camera folders decide.
        Assert.Equal(DriveRole.Card, AutoDrives.Role(Card(@"H:\")));
    }

    [Fact]
    public void Role_RemovableMedia_IsReaderSlot()
    {
        Assert.Equal(DriveRole.ReaderSlot, AutoDrives.Role(Reader(@"F:\")));
    }

    [Theory]
    [InlineData(StorageBus.Sd)]
    [InlineData(StorageBus.Mmc)]
    public void Role_SdBus_IsReaderSlot_EvenWhenFixed(StorageBus bus)
    {
        Assert.Equal(DriveRole.ReaderSlot, AutoDrives.Role(Disk(@"F:\") with { Bus = bus }));
    }

    [Theory]
    [InlineData("Generic USB3.0 Card Reader")]
    [InlineData("Lexar CFexpress Reader")]
    [InlineData("ProGrade CFast 2.0")]
    [InlineData("Sony MRW-G2")]
    [InlineData("Generic- SD/MMC")]
    [InlineData("SanDisk XQD Reader")]
    public void Role_ReaderProduct_IsReaderSlot(string product)
    {
        Assert.Equal(DriveRole.ReaderSlot, AutoDrives.Role(Disk(@"F:\", product: product)));
    }

    [Fact]
    public void Role_SeveralDisksOnOneUsbDevice_IsReaderSlot()
    {
        // A multi-slot reader: one USB device, a disk per slot.
        Assert.Equal(DriveRole.ReaderSlot, AutoDrives.Role(Disk(@"F:\") with { DisksOnDevice = 3 }));
    }

    [Theory]
    [InlineData(StorageBus.Sata)]
    [InlineData(StorageBus.Nvme)]
    [InlineData(StorageBus.Raid)]
    [InlineData(StorageBus.Unknown)]
    [InlineData(StorageBus.Virtual)]
    [InlineData(StorageBus.Network)]
    public void Role_NonUsbFixedDisk_IsInternal(StorageBus bus)
    {
        Assert.Equal(DriveRole.Internal, AutoDrives.Role(Disk(@"D:\") with { Bus = bus }));
    }

    [Fact]
    public void Role_SystemDisk_IsInternal_Always()
    {
        Assert.Equal(DriveRole.Internal, AutoDrives.Role(Disk(@"C:\") with { IsSystem = true }));
        Assert.Equal(DriveRole.Internal, AutoDrives.Role(Card(@"C:\") with { IsSystem = true }));
    }

    // ── destinations ────────────────────────────────────────────────────

    [Fact]
    public void Plan_NewUsbDisk_IsAdded_WithItsIdentity()
    {
        var plan = AutoDrives.Plan(Input([Disk(@"G:\")]));
        var added = Assert.Single(plan.Add);
        Assert.Equal(@"G:\", added.Root);
        Assert.Equal("AAAA0001:2000000000000", added.Disk);
        Assert.Empty(plan.Drop);
        Assert.Empty(plan.Hints);
    }

    [Fact]
    public void Plan_CardReaderAndInternal_NeverAdded()
    {
        var plan = AutoDrives.Plan(Input([Card(@"H:\"), Reader(@"F:\"), Disk(@"D:\") with { Bus = StorageBus.Sata }, Disk(@"C:\") with { IsSystem = true }]));
        Assert.Empty(plan.Add);
        Assert.Empty(plan.Hints);
    }

    [Fact]
    public void Plan_RemovedDisk_NotAddedAgain()
    {
        var plan = AutoDrives.Plan(Input([Disk(@"G:\")], removed: ["AAAA0001:2000000000000"]));
        Assert.Empty(plan.Add);
        Assert.Empty(plan.Hints);
    }

    [Fact]
    public void Plan_RemovedDisk_IsKnownByIdentity_NotByLetter()
    {
        // The removed disk is back under another letter: still removed. Another disk on the old letter: added.
        var plan = AutoDrives.Plan(Input([Disk(@"K:\"), Disk(@"G:\", id: "DDDD0004:1000000000000")], removed: ["aaaa0001:2000000000000"]));
        Assert.Equal(@"G:\", Assert.Single(plan.Add).Root);
    }

    [Fact]
    public void Plan_DiskAlreadyADestination_NotAddedTwice()
    {
        Assert.Empty(AutoDrives.Plan(Input([Disk(@"G:\")], dests: [@"g:\"])).Add);
        Assert.Empty(AutoDrives.Plan(Input([Disk(@"G:\")], dests: [@"G:\Zaloha"])).Add);   // a folder on it
    }

    [Fact]
    public void Plan_FullOrReadOnly_NotAdded_HintInstead()
    {
        var plan = AutoDrives.Plan(Input([Disk(@"G:\", free: GB / 2), Disk(@"K:\", id: "EEEE0005:1", readOnly: true)]));
        Assert.Empty(plan.Add);
        Assert.Equal(2, plan.Hints.Count);
        Assert.Contains("BACKUP (G:) je plný", plan.Hints[0]);
        Assert.Contains("jako cíl se nepřidal", plan.Hints[0]);
        Assert.Contains("BACKUP (K:) je jen pro čtení", plan.Hints[1]);
    }

    [Fact]
    public void Plan_FullButRemoved_NoHint()
    {
        Assert.Empty(AutoDrives.Plan(Input([Disk(@"G:\", free: 0)], removed: ["AAAA0001:2000000000000"])).Hints);
    }

    [Fact]
    public void Plan_UnknownIdentity_NotAdded()
    {
        // Without serial and capacity a removal could not be remembered: such a disk is left to the user.
        Assert.Empty(AutoDrives.Plan(Input([Disk(@"G:\") with { DiskId = null }])).Add);
    }

    [Fact]
    public void Plan_SettingOff_NothingAdded_AutoAddedStillDroppedWhenGone()
    {
        var plan = AutoDrives.Plan(Input([Disk(@"G:\")], dests: [@"K:\"], auto: [new(@"K:\", "FFFF0006:1")], addDisks: false));
        Assert.Empty(plan.Add);
        Assert.Equal(@"K:\", Assert.Single(plan.Drop));
    }

    [Fact]
    public void Plan_Locked_ChangesNothing()
    {
        // Ruling I1: while a Start checks the profile shown, nothing may change it — neither add nor drop. The demo too.
        var plan = AutoDrives.Plan(Input([Disk(@"G:\", free: 0)], dests: [@"K:\"], auto: [new(@"K:\", "FFFF0006:1")], locked: true));
        Assert.Empty(plan.Add);
        Assert.Empty(plan.Drop);
        Assert.Empty(plan.Hints);
    }

    [Fact]
    public void Plan_AutoAddedDiskGone_IsDropped_ManualOneKept()
    {
        var plan = AutoDrives.Plan(Input([], dests: [@"K:\", @"L:\"], auto: [new(@"K:\", "FFFF0006:1")]));
        Assert.Equal(@"K:\", Assert.Single(plan.Drop));
    }

    [Fact]
    public void Plan_AutoAddedLetterTakenByAnotherDisk_OldDropped_NewAdded()
    {
        var plan = AutoDrives.Plan(Input([Disk(@"K:\", id: "1111:2")], dests: [@"K:\"], auto: [new(@"K:\", "FFFF0006:1")]));
        Assert.Equal(@"K:\", Assert.Single(plan.Drop));
        var added = Assert.Single(plan.Add);
        Assert.Equal("1111:2", added.Disk);
    }

    [Fact]
    public void Plan_AutoAddedDiskStillThere_Kept()
    {
        var plan = AutoDrives.Plan(Input([Disk(@"K:\", id: "FFFF0006:1")], dests: [@"K:\"], auto: [new(@"K:\", "ffff0006:1")]));
        Assert.Empty(plan.Drop);
        Assert.Empty(plan.Add);
    }

    [Fact]
    public void Plan_AutoAddedButRemovedByUserMeanwhile_NotDroppedTwice()
    {
        // The user removed it (it is no destination any more): nothing to drop.
        Assert.Empty(AutoDrives.Plan(Input([], dests: [], auto: [new(@"K:\", "FFFF0006:1")])).Drop);
    }

    // ── cards ───────────────────────────────────────────────────────────

    [Fact]
    public void TickOnArrival_CameraCardOrNonEmptyReaderCard()
    {
        Assert.True(AutoDrives.TickOnArrival(Card(@"H:\"), addCards: true));
        Assert.True(AutoDrives.TickOnArrival(Reader(@"F:\"), addCards: true));        // e.g. a sound recorder's SD card
        Assert.False(AutoDrives.TickOnArrival(Reader(@"F:\", empty: true), addCards: true));
        Assert.False(AutoDrives.TickOnArrival(Disk(@"G:\"), addCards: true));          // a disk is no card
        Assert.False(AutoDrives.TickOnArrival(Card(@"H:\"), addCards: false));         // the setting is off
        Assert.False(AutoDrives.TickOnArrival(Card(@"C:\") with { IsSystem = true }, addCards: true));
    }

    [Fact]
    public void UsbFlashDrive_NeitherTickedNorADestination()
    {
        // Removable media, but no reader: a stick with documents is not ticked as a card — and never a destination.
        var stick = Reader(@"F:\") with { Product = "SanDisk Cruzer Blade" };
        Assert.Equal(DriveRole.ReaderSlot, AutoDrives.Role(stick));
        Assert.False(AutoDrives.TickOnArrival(stick, addCards: true));
        Assert.Empty(AutoDrives.Plan(Input([stick])).Add);
        // The same stick with camera folders on it is a card.
        Assert.True(AutoDrives.TickOnArrival(stick with { LooksLikeCard = true }, addCards: true));
    }

    [Fact]
    public void SelectionAfterRefresh_UsesTheRule_ReaderCardTicked_SettingOffTicksNothing()
    {
        var reader = new VolumeItem(@"F:\", @"F:\", 100, 50, "exFAT", false, false) { Facts = Reader(@"F:\") };
        var card = new VolumeItem(@"H:\", @"H:\", 100, 50, "exFAT", true, false) { Facts = Card(@"H:\") };
        var on = Volumes.SelectionAfterRefresh([], [reader, card], [], [], autoCards: true);
        Assert.Contains(@"F:\", on);
        Assert.Contains(@"H:\", on);
        Assert.Empty(Volumes.SelectionAfterRefresh([], [reader, card], [], [], autoCards: false));
    }

    // ── settings (per profile, plan 8) ──────────────────────────────────

    [Fact]
    public void Settings_DefaultOn_RoundTrip_PerProject()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var s = SettingsStore.Load(path);
        Assert.True(s.AutoDrive.Destinations);
        Assert.True(s.AutoDrive.Cards);
        Assert.Empty(s.AutoDrive.Removed);

        s.AutoDrive.Removed.Add("AAAA0001:2000000000000");
        s.AutoDrive.Added.Add(new AutoDestination(@"G:\", "BBBB:1"));
        var project = Projects.SaveAs(s, "Film X");   // a new project starts as a copy of the profile shown
        project.AutoDrive.Destinations = false;
        SettingsStore.Save(s, path);

        var back = SettingsStore.Load(path);
        Assert.Equal(["AAAA0001:2000000000000"], back.AutoDrive.Removed);
        Assert.Equal(new AutoDestination(@"G:\", "BBBB:1"), Assert.Single(back.AutoDrive.Added));
        Assert.True(back.AutoDrive.Destinations);
        var x = Projects.Find(back, "Film X")!;
        Assert.False(x.AutoDrive.Destinations);
        Assert.Equal(["AAAA0001:2000000000000"], x.AutoDrive.Removed);
    }

    [Fact]
    public void Settings_BadEntries_Dropped_DuplicatesOnce()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """{"autoDrives":{"cards":false,"removed":["A:1"," a:1 ",""],"added":[{"root":"G:\\","disk":""}],"future":1}}""");
        var s = SettingsStore.Load(path);
        Assert.False(s.AutoDrive.Cards);
        Assert.Equal(["A:1"], s.AutoDrive.Removed);
        Assert.Empty(s.AutoDrive.Added);
        SettingsStore.Save(s, path);
        Assert.Contains("\"future\": 1", File.ReadAllText(path));   // a newer version's key survives (R24)
    }

    // ── identity and the deeper card check ──────────────────────────────

    [Fact]
    public void DiskId_SerialAndCapacity()
    {
        Assert.Equal("1A2B3C4D:2000398934016", AutoDrives.DiskId(0x1A2B3C4D, 2000398934016));
        Assert.Null(AutoDrives.DiskId(null, 1));
        Assert.Null(AutoDrives.DiskId(1, 0));
    }

    static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Theory]
    [InlineData(@"PRIVATE\M4ROOT\CLIP\C0001.MP4")]
    [InlineData(@"CLIP\A001C001.mov")]
    [InlineData(@"DCIM\100CANON\IMG_0001.JPG")]
    [InlineData(@"ARRI\readme.txt")]
    [InlineData(@"CONTENTS\CLIPS001\AA0001.MXF")]
    [InlineData(@"A001_0101XX.RDM\A001_C001_0101AB.RDC\A001_C001_0101AB_001.R3D")]
    [InlineData(@"A_0009\A_0009C001_251009_R1AB\A_0009C001_251009_R1AB.mxf")]   // ARRI Codex: two levels down
    public void LooksLikeCameraMedia_CameraStructures(string file)
    {
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, file));
        Assert.True(Volumes.LooksLikeCameraMedia(dir.Path));
    }

    [Fact]
    public void LooksLikeCameraMedia_OffloadBackupDisk_IsNoCard()
    {
        // A backup disk: <date>\<card>\… with our own folders — clips three levels down are never taken for a card.
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, "2026-10-09", "A001", "CLIP", "A001C001.mov"));
        Touch(Path.Combine(dir.Path, "2026-10-09", "A001", "_REPORT", "offload_report.json"));
        Touch(Path.Combine(dir.Path, "2026-10-09", "_REPORT", "2026-10-09_day_report.html"));
        Touch(Path.Combine(dir.Path, "Projekty", "notes.txt"));
        Assert.False(Volumes.LooksLikeCameraMedia(dir.Path));
    }

    // ── destination decision (controller ruling 2026-10-09) ─────────────

    /// The facts the scan gathers for a fixed USB disk at `path` (Volumes.FactsOf): the source heuristic as it is, Offload
    /// output first, the camera structure at the root only without it.
    static DriveFacts Scanned(string path)
    {
        bool output = Volumes.HasOffloadOutput(path);
        return Disk(path) with
        {
            LooksLikeCard = Volumes.LooksLikeCameraCard(path) || Volumes.LooksLikeCameraMedia(path),
            OffloadOutput = output,
            CameraAtRoot = !output && Volumes.CameraAtRoot(path),
        };
    }

    static bool Added(DriveFacts f) => AutoDrives.Plan(Input([f])).Add.Count == 1;

    [Fact]
    public void Destination_BackupWithClipsOneLevelDown_Added()
    {
        // The user's E: (WD Elements): clips one folder down — the source heuristic still calls it a card (Karty
        // unchanged), but as a destination it is a backup disk.
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, "Footage", "A001C001.mov"));
        var f = Scanned(dir.Path);
        Assert.True(Volumes.LooksLikeCameraCard(dir.Path));
        Assert.False(f.CameraAtRoot);
        Assert.True(Added(f));
    }

    [Fact]
    public void Destination_ClipsSeveralLevelsDown_Added()
    {
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, "Natáčení", "Den 1", "A001", "CLIP", "A001C001.braw"));
        Touch(Path.Combine(dir.Path, "Archiv", "A_0009", "A_0009C001_251009_R1AB", "A_0009C001_251009_R1AB.mxf"));
        Assert.True(Added(Scanned(dir.Path)));
    }

    [Theory]
    [InlineData("A001_C001_1009AB.braw")]   // Blackmagic SSD (BRAW)
    [InlineData("ATOMOS_0001.mov")]         // Atomos SSD
    [InlineData("C0001.MP4")]
    public void Destination_CameraSsdWithClipsAtTheRoot_NotAdded(string file)
    {
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, file));
        var f = Scanned(dir.Path);
        Assert.True(f.CameraAtRoot);
        Assert.False(Added(f));
    }

    [Theory]
    [InlineData(@"PRIVATE\M4ROOT\CLIP\C0001.MP4")]
    [InlineData(@"PRIVATE\AVCHD\BDMV\STREAM\00000.MTS")]
    [InlineData(@"DCIM\100CANON\IMG_0001.JPG")]
    [InlineData(@"CLIP\A001C001.mov")]
    [InlineData(@"CONTENTS\CLIPS001\AA0001.MXF")]
    [InlineData(@"XDROOT\Clip\C0001.MXF")]
    [InlineData(@"A001_0101XX.RDM\A001_C001_0101AB.RDC\A001_C001_0101AB_001.R3D")]
    [InlineData(@"A001_C001_0101AB.RDC\A001_C001_0101AB_001.R3D")]
    [InlineData(@"ARRI\readme.txt")]
    [InlineData(@"A_0009\A_0009C001_251009_R1AB\A_0009C001_251009_R1AB.mxf")]   // ARRI Codex reel
    [InlineData(@"A002R1AB\A002C001_150101_R1AB\A002C001_150101_R1AB.mxf")]     // ARRI reel, older naming
    public void Destination_CameraStructureAtTheRoot_NotAdded(string file)
    {
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, file));
        var f = Scanned(dir.Path);
        Assert.True(f.CameraAtRoot);
        Assert.False(Added(f));
    }

    [Fact]
    public void CameraAtRoot_ReelNameWithoutClipFolders_NoCamera()
    {
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, "A_0009", "notes.txt"));
        Assert.False(Volumes.CameraAtRoot(dir.Path));
    }

    [Theory]
    [InlineData(@"2026-10-09\A001\_REPORT\offload_report.json")]
    [InlineData(@"2026-10-09\A001\_REPORT\offload_report_0002.json")]
    [InlineData(@"Projekt\A001\_REPORT\offload-manifest.jsonl")]
    [InlineData(@"2026-10-09\A001\ascmhl\0001_A001_2026-10-09_120000Z.mhl")]
    [InlineData(@"_REPORT\offload_report.json")]
    public void HasOffloadOutput_WithinThreeFolders(string file)
    {
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, file));
        Assert.True(Volumes.HasOffloadOutput(dir.Path));
    }

    [Theory]
    [InlineData(@"Projekt\2026-10-09\A001\_REPORT\offload_report.json")]   // four down: past the bound
    [InlineData(@"2026-10-09\A001\_REPORT\notes.txt")]                      // a _REPORT that is not ours
    [InlineData(@"Footage\A001C001.mov")]
    public void HasOffloadOutput_NotThere(string file)
    {
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, file));
        Assert.False(Volumes.HasOffloadOutput(dir.Path));
    }

    [Fact]
    public void HasOffloadOutput_Bounded_StopsAtTheBudget()
    {
        using var dir = new TempDir();
        for (int i = 0; i < 10; i++) Directory.CreateDirectory(Path.Combine(dir.Path, $"a{i}"));
        Touch(Path.Combine(dir.Path, "z", "A001", "_REPORT", "offload_report.json"));
        Assert.True(Volumes.HasOffloadOutput(dir.Path));
        Assert.False(Volumes.HasOffloadOutput(dir.Path, budget: 5));
    }

    [Fact]
    public void Destination_OffloadOutputBeatsCameraStructure_Added()
    {
        // A backup disk that also has a DCIM folder and a loose clip at the root: Offload's output settles it.
        using var dir = new TempDir();
        Touch(Path.Combine(dir.Path, "DCIM", "100CANON", "IMG_0001.JPG"));
        Touch(Path.Combine(dir.Path, "A001C001.mov"));
        Touch(Path.Combine(dir.Path, "2026-10-09", "A001", "_REPORT", "offload_report.json"));
        var f = Scanned(dir.Path);
        Assert.True(f.OffloadOutput);
        Assert.True(Added(f));
    }

    [Fact]
    public void Destination_ReaderOrRemovable_NeverAdded_EvenWithOffloadOutput()
    {
        Assert.False(Added(Reader(@"F:\") with { OffloadOutput = true }));
        Assert.False(Added(Disk(@"F:\", product: "SanDisk CFexpress Reader") with { OffloadOutput = true }));
        Assert.False(Added(Disk(@"F:\") with { DisksOnDevice = 2, OffloadOutput = true }));
        Assert.False(Added(Disk(@"F:\") with { Type = DriveType.Removable, OffloadOutput = true }));
        Assert.False(Added(Disk(@"D:\") with { Bus = StorageBus.Sata, OffloadOutput = true }));
    }

    [Fact]
    public void RemovedDisk_BackupWithClipsDown_Remembered()
    {
        var backup = Disk(@"E:\", "EEEE0005:4000000000000") with { LooksLikeCard = true };
        Assert.True(Added(backup));
        Assert.Equal("EEEE0005:4000000000000", AutoDrives.RemovedDisk(@"E:\", [backup]));
    }

    // ── one decision per volume (controller ruling 2026-10-09) ───────────

    /// The user's E: as the scan lists it: clips one folder down, so the card rule (as in 0.6.1) calls it a card.
    static VolumeItem BackupE() =>
        new("WD Elements (E:)", @"E:\", 4000 * GB, 1000 * GB, "NTFS", LooksLikeCard: true, IsDestination: false)
        { Facts = Disk(@"E:\", "EEEE0005:4000000000000") with { LooksLikeCard = true } };

    static VolumeItem CameraSsd() =>
        new("BRAW (F:)", @"F:\", 1000 * GB, 200 * GB, "exFAT", LooksLikeCard: true, IsDestination: false)
        { Facts = Disk(@"F:\", "FFFF0006:1000000000000", product: "Samsung T7") with { LooksLikeCard = true, CameraAtRoot = true } };

    static AutoDriveInput Auto(IReadOnlyList<VolumeItem> found, bool addDisks = true, bool locked = false, IReadOnlyList<string>? removed = null) =>
        Input(found.Select(v => v.Facts!).ToList(), removed: removed, addDisks: addDisks, locked: locked);

    [Fact]
    public void Decide_BackupDiskArrives_DestinationOnly_NeverTicked()
    {
        IReadOnlyList<VolumeItem> found = [BackupE()];
        var d = Volumes.Decide([], found, [], [], autoCards: true, Auto(found));
        Assert.Equal([@"E:\"], d.Plan.Add.Select(a => a.Root));
        Assert.True(d.Found.Single().IsDestination);
        Assert.False(d.Found.Single().LooksLikeCard);
        Assert.DoesNotContain(@"E:\", d.Selected);
        Assert.Equal(VolumeUse.Destination, Volumes.ArrivalUse(found[0], autoDestination: true, autoCards: true));
    }

    [Fact]
    public void Decide_DuringStart_NotAdded_NotTickedEither()
    {
        IReadOnlyList<VolumeItem> found = [BackupE()];
        var d = Volumes.Decide([], found, [], [], autoCards: true, Auto(found, locked: true));
        Assert.False(d.Plan.Changes);
        Assert.False(d.Found.Single().IsDestination);
        Assert.DoesNotContain(@"E:\", d.Selected);
        // After the Start the next scan adds it — and it was never ticked in between.
        var after = Volumes.Decide([@"E:\"], d.Found, d.Selected, [], autoCards: true, Auto(found));
        Assert.Single(after.Plan.Add);
        Assert.Empty(after.Selected);
    }

    [Fact]
    public void Decide_AutoAddOffOrDiskRemoved_Ticked_AsIn061()
    {
        // 0.6.1 already ticks E: in Karty (clips one folder down); without the automatic destination that stays so.
        IReadOnlyList<VolumeItem> found = [BackupE()];
        Assert.Contains(@"E:\", Volumes.Decide([], found, [], [], autoCards: true, Auto(found, addDisks: false)).Selected);
        Assert.Contains(@"E:\", Volumes.Decide([], found, [], [], autoCards: true, Auto(found, removed: ["EEEE0005:4000000000000"])).Selected);
        Assert.Contains(@"E:\", Volumes.SelectionAfterRefresh([], found, [], []));
    }

    [Fact]
    public void Decide_CameraSsdAndBackupTogether_SsdSource_BackupDestination()
    {
        IReadOnlyList<VolumeItem> found = [BackupE(), CameraSsd()];
        var d = Volumes.Decide([], found, [], [], autoCards: true, Auto(found));
        Assert.Equal([@"E:\"], d.Plan.Add.Select(a => a.Root));
        Assert.Equal([@"F:\"], d.Selected);
        Assert.Equal(VolumeUse.Source, Volumes.ArrivalUse(found[1], autoDestination: false, autoCards: true));
        Assert.Equal(VolumeUse.Neither, Volumes.ArrivalUse(found[1], autoDestination: false, autoCards: false));
    }

    [Fact]
    public void Decide_Demo_NoAutomaticDestinations()
    {
        IReadOnlyList<VolumeItem> found = [BackupE()];
        var d = Volumes.Decide([], found, [], [], autoCards: true, auto: null);
        Assert.False(d.Plan.Changes);
        Assert.Contains(@"E:\", d.Selected);
    }
}
