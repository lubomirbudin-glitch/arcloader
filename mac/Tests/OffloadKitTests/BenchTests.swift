import XCTest
@testable import OffloadKit

final class BenchTests: XCTestCase {
    func testWriteMeasuresWriteAndReadBackLeavesNothing() throws {
        let dir = tempDir()
        let lines = try Bench.write(dir: dir.path, size: (1 << 20) + 5, blockSize: 64 * 1024)
        XCTAssertEqual(lines.map(\.label), ["write 64 KB", "read  64 KB + xxh64"])
        for l in lines { XCTAssertEqual(l.bytes, (1 << 20) + 5) }
        XCTAssertEqual(TestFS.files(in: dir.path), [])
    }

    func testReadReadsUpToLimitPerBlockSize() throws {
        let dir = tempDir()
        TestFS.writeRandom(dir.path + "/a.bin", size: 300_000, seed: 1)
        TestFS.writeRandom(dir.path + "/b.bin", size: 100_000, seed: 2)
        let lines = try Bench.read(path: dir.path, limit: 350_000, blockSizes: [64 * 1024, 1 << 20])
        XCTAssertEqual(lines.map(\.label), ["read  64 KB", "read  1 MB"])
        for l in lines { XCTAssertTrue((350_000...400_000).contains(l.bytes), "\(l.bytes)") }
    }

    func testWriteAlreadyCancelledThrowsAndLeavesNothing() {
        let dir = tempDir()
        XCTAssertThrowsError(try Bench.write(dir: dir.path, size: 64 << 20, blockSize: 1 << 20, isCancelled: { true })) {
            XCTAssertTrue($0 is CancellationError)
        }
        XCTAssertEqual(TestFS.files(in: dir.path), [])
    }

    // Review focus 5: cancelled in the middle of the temporary file — stops inside it and still deletes it.
    func testWriteCancelledWhileWritingThrowsAndDeletesTempFile() {
        let dir = tempDir()
        var checks = 0
        XCTAssertThrowsError(try Bench.write(dir: dir.path, size: 512 << 20, blockSize: 1 << 20, isCancelled: {
            checks += 1
            return checks > 3
        })) { XCTAssertTrue($0 is CancellationError) }
        XCTAssertEqual(TestFS.files(in: dir.path), [])
    }

    func testReadCancelledThrows() {
        let dir = tempDir()
        TestFS.writeRandom(dir.path + "/a.bin", size: 300_000, seed: 1)
        XCTAssertThrowsError(try Bench.read(path: dir.path, limit: 350_000, blockSizes: [64 * 1024], isCancelled: { true })) {
            XCTAssertTrue($0 is CancellationError)
        }
    }

    func testHashReportsEightPasses() {
        XCTAssertEqual(Bench.hash(size: 1 << 20).bytes, 8 << 20)
    }
}
