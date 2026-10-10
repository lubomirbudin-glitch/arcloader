import XCTest
@testable import OffloadKit

final class FileLockTests: XCTestCase {
    func testSecondHolderRefusedAtOnce() throws {
        let p = tempDir().file("x.lock")
        let a = try XCTUnwrap(FileLock.tryLock(p))
        XCTAssertNil(FileLock.tryLock(p), "the same process, another descriptor: refused")
        a.release()
        XCTAssertNotNil(FileLock.tryLock(p))
    }

    func testWaitUpToTimeout() throws {
        let p = tempDir().file("w.lock")
        let a = try XCTUnwrap(FileLock.tryLock(p))
        let start = Date()
        XCTAssertNil(FileLock.lock(p, timeout: 0.3))
        XCTAssertGreaterThanOrEqual(Date().timeIntervalSince(start), 0.3)
        DispatchQueue.global().asyncAfter(deadline: .now() + 0.2) { a.release() }
        XCTAssertNotNil(FileLock.lock(p, timeout: 2))
    }

    func testFolderLocksAllOrNothing() throws {
        let d = tempDir()
        let one = d.sub("D", "2026-10-08", "A001"), two = d.sub("E", "2026-10-08", "A001")
        let held = try FolderLocks.acquire([two])
        XCTAssertThrowsError(try FolderLocks.acquire([one, two])) { e in
            XCTAssertEqual((e as? OffloadError)?.code, .appendBusy)
        }
        XCTAssertNotNil(FileLock.tryLock(Locks.cardFolder(one)), "nothing held after a refusal")
        held.release()
    }

    func testDifferentSpellingSameLock() throws {
        let p = tempDir().sub("Zaloha", "A001")
        XCTAssertEqual(Locks.cardFolder(p), Locks.cardFolder(p + "/"))
    }

    func testLocksLiveInTheSupportFolderNeverOnTheDestination() {
        _ = testSettingsDir
        XCTAssertTrue(Locks.cardFolder("/nonexistent-offload-test/RAID/2026-10-08/A001").hasPrefix(Locks.folder))
        XCTAssertEqual(AppSupport.folder, testSettingsDir, "tests never use the real Application Support folder")
    }
}
