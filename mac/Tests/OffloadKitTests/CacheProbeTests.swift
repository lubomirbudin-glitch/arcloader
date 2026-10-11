import Darwin
import XCTest
@testable import OffloadKit

/// Spec D.23.1 / D.27 rows 3, 4, 8. With OFFLOAD_TEST_DEST=<folder on a disk> the files go there (a folder of their own,
/// removed after the test), else into a temporary folder.
final class CacheProbeTests: XCTestCase {
    private func dir() -> String {
        if let dest = ProcessInfo.processInfo.environment["OFFLOAD_TEST_DEST"], !dest.isEmpty {
            let d = dest + "/offload-cacheprobe-" + String(UUID().uuidString.prefix(8))
            try! FileManager.default.createDirectory(atPath: d, withIntermediateDirectories: true)
            addTeardownBlock { try? FileManager.default.removeItem(atPath: d) }
            return d
        }
        return tempDir().path
    }

    // Review focus 9: mincore must see cached pages, or nothing it says is proof (spec D.27 row 3).
    func testResidentPagesAreDetected() throws {
        let p = dir() + "/cached.bin"
        try Data(repeating: 7, count: 8 << 20).write(to: URL(fileURLWithPath: p))   // a normal, cached write
        let r = try XCTUnwrap(CacheProbe.residentPages(p))
        XCTAssertEqual(r.total, (8 << 20) / Int(getpagesize()))
        XCTAssertGreaterThan(r.resident, 0, "a normal write leaves pages in the cache — if 0, mincore is blind (spec D.27)")
        XCTAssertFalse(CacheProbe.blind())
    }

    func testWrittenWithNoCacheLeavesNoResidentPages() throws {
        let p = dir() + "/nocache.bin"
        let fd = try IO.openWrite(p)
        let buf = IO.allocateAligned(4 << 20)
        memset(buf, 9, 4 << 20)
        try IO.pwrite(fd, buf, 4 << 20, 0, path: p)
        try IO.fullSync(fd, path: p)
        close(fd); free(buf)
        XCTAssertEqual(CacheProbe.residentPages(p)?.resident, 0, "F_NOCACHE writes should not stay in the cache (verify on APFS, exFAT, external disks)")
    }

    // D.27 row 8 (no effect on the behaviour): msync(MS_INVALIDATE) drops the clean pages of a file read normally.
    func testEvictDropsCleanPages() throws {
        let p = dir() + "/read.bin"
        try Data(repeating: 3, count: 4 << 20).write(to: URL(fileURLWithPath: p))
        _ = try Data(contentsOf: URL(fileURLWithPath: p))
        XCTAssertEqual(CacheProbe.isResident(p), true)
        XCTAssertTrue(CacheProbe.evict(p))
        XCTAssertEqual(CacheProbe.residentPages(p)?.resident, 0, "record in D.27 row 8 when this fails on a file system")
        XCTAssertTrue(CacheProbe.readsFromDisk(p))
    }

    func testReadWithNoCacheDoesNotFillTheCache() throws {
        let p = dir() + "/nocache-read.bin"
        let fd = try IO.openWrite(p)
        let buf = IO.allocateAligned(4 << 20)
        defer { free(buf) }
        memset(buf, 5, 4 << 20)
        try IO.pwrite(fd, buf, 4 << 20, 0, path: p)
        try IO.fullSync(fd, path: p)
        close(fd)
        let rd = try IO.openRead(p)
        _ = try IO.pread(rd, buf, 4 << 20, 0, path: p)
        close(rd)
        XCTAssertEqual(CacheProbe.residentPages(p)?.resident, 0, "F_NOCACHE reads should not fill the cache")
    }

    func testEmptyAndMissingFiles() {
        let d = dir()
        FileManager.default.createFile(atPath: d + "/empty", contents: Data())
        XCTAssertEqual(CacheProbe.residentPages(d + "/empty")?.total, 0)
        XCTAssertNil(CacheProbe.residentPages(d + "/missing"))
        XCTAssertFalse(CacheProbe.readsFromDisk(d + "/missing"), "what cannot be told is never proof")
    }

    func testProbeOnLocalDestinationPassesAndLeavesNothing() throws {
        let card = dir() + "/D/2026-10-08/A001"
        try FileManager.default.createDirectory(atPath: card + "/_REPORT", withIntermediateDirectories: true)
        XCTAssertNil(CacheProbe.problem(cardPath: card))
        XCTAssertEqual(TestFS.files(in: card + "/_REPORT"), [])
    }

    func testProbeThatCannotBeWrittenSaysWhy() {
        let missing = tempDir().path + "/no/such"
        let why = CacheProbe.problem(cardPath: missing)
        XCTAssertNotNil(why)
        XCTAssertTrue(why?.hasSuffix(" — verified by reading back") ?? false, why ?? "nil")
    }
}
