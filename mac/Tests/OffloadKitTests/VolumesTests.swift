import XCTest
@testable import OffloadKit

final class VolumesTests: XCTestCase {
    private func touch(_ path: String) {
        try! FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try! "x".write(toFile: path, atomically: true, encoding: .utf8)
    }

    func testLooksLikeCardMarkerFolderAnyCase() {
        let dir = tempDir()
        dir.sub("dcim")
        XCTAssertTrue(Volumes.looksLikeCameraCard(dir.path))
    }

    func testLooksLikeCardMediaAtRootOrOneLevelDown() {
        let a = tempDir()
        touch(a.path + "/A001.MOV")
        XCTAssertTrue(Volumes.looksLikeCameraCard(a.path))
        let b = tempDir()
        touch(b.path + "/A001/A001C001.braw")
        XCTAssertTrue(Volumes.looksLikeCameraCard(b.path))
        let red = tempDir()
        red.sub("A001_C001.RDM")
        XCTAssertTrue(Volumes.looksLikeCameraCard(red.path))
    }

    func testLooksLikeCardNo() {
        let deep = tempDir()
        touch(deep.path + "/a/b/c.mxf")
        touch(deep.path + "/notes.txt")
        touch(deep.path + "/.hidden/x.mov")
        XCTAssertFalse(Volumes.looksLikeCameraCard(deep.path))
        XCTAssertFalse(Volumes.looksLikeCameraCard(deep.path + "/missing"))
    }

    func testSelectionAfterRefresh() {
        func v(_ path: String, card: Bool = true, dest: Bool = false) -> VolumeItem {
            VolumeItem(name: path, path: path, total: 100, free: 50, format: "exfat", looksLikeCard: card && !dest, isDestination: dest)
        }
        let found = [v("/Volumes/A001"), v("/Volumes/B"), v("/Volumes/RAID", dest: true)]
        let sel = Volumes.selectionAfterRefresh(previousPaths: ["/Volumes/A001", "/Volumes/RAID", "/Volumes/GONE"], found: found,
                                                selected: ["/Volumes/GONE", "/Volumes/RAID", "/Users/x/card"], manual: ["/Users/x/card"])
        // New card ticked; A001 was there before and unticked — stays unticked; gone and destination dropped; manual kept.
        XCTAssertEqual(sel, ["/Volumes/B", "/Users/x/card"])
    }

    func testScanNeverOffersTheStartupVolumeAndDoesNotThrow() {
        let found = Volumes.scan(destRoots: [])
        XCTAssertFalse(found.contains { $0.path == "/" })
        for v in found { XCTAssertTrue(v.path.hasPrefix("/Volumes/"), v.path) }
    }
}
