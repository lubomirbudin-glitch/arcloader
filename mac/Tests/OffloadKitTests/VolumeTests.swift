import XCTest
@testable import OffloadKit

final class VolumeTests: XCTestCase {
    private func sector(_ patch: (inout [UInt8]) -> Void) -> [UInt8] {
        var s = [UInt8](repeating: 0, count: 512)
        patch(&s)
        return s
    }

    private func put(_ s: inout [UInt8], _ at: Int, _ text: String) { for (i, b) in text.utf8.enumerated() { s[at + i] = b } }
    private func le32(_ s: inout [UInt8], _ at: Int, _ v: UInt32) { for i in 0..<4 { s[at + i] = UInt8((v >> (8 * UInt32(i))) & 0xff) } }

    func testBootSectorSerials() {
        let exfat = sector { put(&$0, 3, "EXFAT   "); le32(&$0, 0x64, 0x1A2B_3C4D) }
        XCTAssertEqual(BootSector.serial(exfat)?.serial, 0x1A2B_3C4D)
        XCTAssertEqual(BootSector.serial(exfat)?.kind, "exFAT")
        let fat32 = sector { put(&$0, 0x52, "FAT32   "); $0[0x42] = 0x29; le32(&$0, 0x43, 0xDEAD_BEEF) }
        XCTAssertEqual(BootSector.serial(fat32)?.serial, 0xDEAD_BEEF)
        XCTAssertEqual(BootSector.serial(fat32)?.kind, "FAT32")
        let fat16 = sector { put(&$0, 0x36, "FAT16   "); $0[0x26] = 0x29; le32(&$0, 0x27, 0x0102_0304) }
        XCTAssertEqual(BootSector.serial(fat16)?.kind, "FAT")
        let ntfs = sector { put(&$0, 3, "NTFS    "); le32(&$0, 0x48, 0x5566_7788); le32(&$0, 0x4C, 0x1122_3344) }
        XCTAssertEqual(BootSector.serial(ntfs)?.serial, 0x5566_7788, "Windows reports the low 32 bits")
        XCTAssertNil(BootSector.serial(sector { _ in }), "no known file system")
    }

    func testSerialTextsAsWindows() {
        XCTAssertEqual(VolumeFacts.fsSerialText(0x1A2B_3C4D), "1A2B3C4D")
        XCTAssertEqual(VolumeFacts.volumeSerialText(0x1A2B_3C4D), "1A2B-3C4D")
    }

    func testUnderRootForBothPlatforms() {
        XCTAssertEqual(VolumeFacts.underRoot(#"D:\Zaloha\2026-10-08\A001"#), "/Zaloha/2026-10-08/A001")
        XCTAssertEqual(VolumeFacts.underRoot(#"\\nas\share\Zaloha\A001"#), "/Zaloha/A001")
        XCTAssertEqual(VolumeFacts.underRoot("/Volumes/NotMounted-Here/Zaloha/2026-10-08/A001/"), "/Zaloha/2026-10-08/A001")
    }

    func testFolderKeyIgnoresSpelling() throws {
        let dir = tempDir()
        let a = dir.sub("Zaloha", "2026-10-08", "A001")
        try FileManager.default.createDirectory(atPath: a, withIntermediateDirectories: true)
        XCTAssertEqual(VolumeFacts.folderKey(a), VolumeFacts.folderKey(a + "/"))
        XCTAssertNotEqual(VolumeFacts.folderKey(a), VolumeFacts.folderKey(dir.sub("Zaloha")))
    }

    func testCardVolumeOfAFolderIsNil() {
        XCTAssertNil(CardVolume.of(tempDir().path), "a folder is not a volume root")
    }

    /// A path on the startup disk lies under "/" by its firmlink although statfs names /System/Volumes/Data:
    /// only a real prefix is stripped (otherwise two folders could get one lock key).
    func testRelativeOnlyStripsARealPrefix() {
        XCTAssertEqual(VolumeFacts.relative("/Users/x/Zaloha", to: "/System/Volumes/Data"), "/Users/x/Zaloha")
        XCTAssertEqual(VolumeFacts.relative("/Volumes/RAID/Zaloha", to: "/Volumes/RAID/"), "/Zaloha")
        XCTAssertEqual(VolumeFacts.relative("/Volumes/RAID2/Zaloha", to: "/Volumes/RAID"), "/Volumes/RAID2/Zaloha")
        XCTAssertEqual(VolumeFacts.relative("/Users/x", to: "/"), "/Users/x")
    }
}
