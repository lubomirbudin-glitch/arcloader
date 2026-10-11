import XCTest
@testable import OffloadKit

final class HistoryStoreTests: XCTestCase {
    private func entry(_ id: String, finished: Date, serial: String? = "1A2B3C4D", shape: String = "s") -> HistoryEntry {
        var e = HistoryEntry(id: id, source: "/Volumes/A001", cardName: "A001", date: "2026-10-08", finished: finished)
        e.card = serial.map { HistoryCard(identity: nil, fsSerial: $0, capacity: 64_000_000_000, label: "A001", filesystem: "exFAT") }
        e.fingerprint = "f"; e.shape = shape; e.success = true; e.verified = true
        e.destinations = [HistoryDestination(root: "/Volumes/RAID", cardPath: "/Volumes/RAID/2026-10-08/A001")]
        e.generations = [HistoryGeneration(seq: 1, kind: "full", finished: finished, files: 1, bytes: 10, verified: true)]
        return e
    }

    func testFingerprintMatchesWindowsLines() {
        let f = [SourceFile(relPath: "b", size: 2, modified: Date(timeIntervalSince1970: 1)),
                 SourceFile(relPath: "a", size: 1, modified: Date(timeIntervalSince1970: 0))]
        let (fp, shape) = CardFingerprint.compute(f)
        XCTAssertEqual(fp, sha256Hex("a\t1\t621355968000000000\nb\t2\t621355968010000000\n"))
        XCTAssertEqual(shape, sha256Hex("a\t1\nb\t2\n"))
    }

    func testOrdinalOrderIsUtf16() {
        // .NET Ordinal: "B" (0x42) < "_" (0x5F) < "a" (0x61).
        let f = [SourceFile(relPath: "a", size: 1, modified: Date(timeIntervalSince1970: 0)),
                 SourceFile(relPath: "_", size: 1, modified: Date(timeIntervalSince1970: 0)),
                 SourceFile(relPath: "B", size: 1, modified: Date(timeIntervalSince1970: 0))]
        XCTAssertEqual(CardFingerprint.compute(f).shape, sha256Hex("B\t1\n_\t1\na\t1\n"))
    }

    func testGzipRoundTripAndWindowsFile() throws {
        let data = Data("hello\tworld\n".utf8)
        XCTAssertEqual(Gzip.decompress(Gzip.compress(data)), data)
        XCTAssertEqual(Gzip.decompress(Gzip.compress(Data())), Data())
        var damaged = Gzip.compress(data)
        damaged[damaged.count - 6] ^= 0xFF
        XCTAssertNil(Gzip.decompress(damaged), "a wrong CRC")
        let fixture = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("Fixtures/windows-history.files.gz").path
        let list = try XCTUnwrap(FileListStore.read(fixture))
        XCTAssertEqual(list.map(\.relPath), ["CLIP/A001C001.mov", "CLIP/A001C002.mov"])
        XCTAssertEqual(FileTimes.ticks(list[1].mtime), 638_640_000_010_000_000)
        XCTAssertNil(FileListStore.read(tempDir().sub("missing.gz")))
    }

    func testFileListRoundTripKeepsTicks() throws {
        let dir = tempDir()
        let files = [SourceFile(relPath: "CLIP/A.mov", size: 7, mtime: timespec(tv_sec: 1_791_000_000, tv_nsec: 123_456_700))]
        try FileListStore.write(dir.file("x", "l.files.gz"), files)
        let back = try XCTUnwrap(FileListStore.read(dir.file("x", "l.files.gz")))
        XCTAssertEqual(back.map(\.ticks), files.map(\.ticks))
        XCTAssertEqual(back.map(\.size), [7])
    }

    func testRoundTripNewestFirstAndFind() {
        let store = HistoryStore(folder: tempDir().path)
        let t = Date(timeIntervalSince1970: 1_791_000_000)
        XCTAssertEqual(store.update(id: "a") { _ in (self.entry("a", finished: t), nil) }, .written)
        XCTAssertEqual(store.update(id: "b") { _ in (self.entry("b", finished: t.addingTimeInterval(60)), nil) }, .written)
        XCTAssertEqual(store.load().map(\.id), ["b", "a"])
        XCTAssertEqual(store.load().first, entry("b", finished: t.addingTimeInterval(60)).with { $0.fileList = "history/b.files.gz" })
        XCTAssertEqual(store.findForCard(CardKey(fsSerial: "1A2B3C4D", capacity: 64_000_000_000), shape: "x")?.id, "b")
        XCTAssertNil(store.findForCard(CardKey(fsSerial: "1A2B3C4D", capacity: 32_000_000_000), shape: "x"), "a clone with another capacity")
    }

    func testFolderFoundByShape() {
        let store = HistoryStore(folder: tempDir().path)
        _ = store.update(id: "f") { _ in (self.entry("f", finished: Date(), serial: nil, shape: "shape1"), nil) }
        XCTAssertEqual(store.findForCard(nil, shape: "shape1")?.id, "f")
        XCTAssertNil(store.findForCard(nil, shape: "other"))
    }

    func testJsonShape() throws {
        let dir = tempDir()
        let store = HistoryStore(folder: dir.path)
        var e = entry("a", finished: Date(timeIntervalSince1970: 1_791_000_000), serial: nil)
        e.success = false
        _ = store.update(id: "a") { _ in (e, nil) }
        let text = try String(contentsOfFile: dir.path + "/history.json", encoding: .utf8)
        let root = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any])
        XCTAssertEqual(root["version"] as? Int, 1)
        let first = try XCTUnwrap((root["entries"] as? [[String: Any]])?.first)
        XCTAssertTrue(first["card"] is NSNull, "a folder: card null, as Windows")
        XCTAssertEqual(first["failedFiles"] as? Int, 0)
        XCTAssertEqual(first["fileList"] as? String, "history/a.files.gz")
        XCTAssertNil(first["unfinished"], "absent when false")
        XCTAssertEqual(first["finished"] as? String, "2026-10-03T04:00:00Z")
        XCTAssertFalse(text.hasPrefix("\u{FEFF}"))
    }

    func testCorruptFileSetAside() throws {
        let dir = tempDir()
        try Data("{ not json".utf8).write(to: URL(fileURLWithPath: dir.path + "/history.json"))
        let store = HistoryStore(folder: dir.path)
        XCTAssertEqual(store.load().count, 0)
        XCTAssertTrue(TestFS.files(in: dir.path).contains { $0.hasPrefix("history.json.corrupt-") })
        XCTAssertEqual(store.update(id: "a") { _ in (self.entry("a", finished: Date()), nil) }, .written)
    }

    func testNewerVersionIsReadOnly() throws {
        let dir = tempDir()
        try Data(#"{"version":2,"entries":[]}"#.utf8).write(to: URL(fileURLWithPath: dir.path + "/history.json"))
        let store = HistoryStore(folder: dir.path)
        XCTAssertEqual(store.update(id: "a") { _ in (self.entry("a", finished: Date()), nil) }, .failed)
        XCTAssertEqual(try String(contentsOfFile: dir.path + "/history.json", encoding: .utf8), #"{"version":2,"entries":[]}"#)
    }

    // Review focus 5.
    func testTwoWritersUnderTheLock() {
        let store = HistoryStore(folder: tempDir().path)
        DispatchQueue.concurrentPerform(iterations: 20) { i in
            _ = store.update(id: "e\(i)") { _ in (self.entry("e\(i)", finished: Date(timeIntervalSince1970: Double(i))), nil) }
        }
        XCTAssertEqual(store.load().count, 20, "no write lost")
    }

    func testBusyLockReportsBusy() throws {
        let dir = tempDir()
        let held = try XCTUnwrap(FileLock.tryLock(Locks.history(dir.path)))
        let store = HistoryStore(folder: dir.path, lockTimeout: 0.2)
        XCTAssertEqual(store.update(id: "a") { _ in (self.entry("a", finished: Date()), nil) }, .busy)
        held.release()
    }

    func testPruneKeepsNewestAndDeletesLists() throws {
        let dir = tempDir()
        let store = HistoryStore(folder: dir.path, maxEntries: 2)
        for i in 0..<3 {
            _ = store.update(id: "e\(i)") { _ in
                (self.entry("e\(i)", finished: Date(timeIntervalSince1970: Double(i))), [SourceFile(relPath: "x", size: 1, modified: Date())])
            }
        }
        XCTAssertEqual(store.load().map(\.id), ["e2", "e1"])
        XCTAssertFalse(FileManager.default.fileExists(atPath: dir.path + "/history/e0.files.gz"))
        XCTAssertTrue(FileManager.default.fileExists(atPath: dir.path + "/history/e2.files.gz"))
    }

    func testRemoveDeletesEntryAndList() {
        let dir = tempDir()
        let store = HistoryStore(folder: dir.path)
        _ = store.update(id: "a") { _ in (self.entry("a", finished: Date()), [SourceFile(relPath: "x", size: 1, modified: Date())]) }
        XCTAssertEqual(store.remove(id: "a"), .written)
        XCTAssertTrue(store.load().isEmpty)
        XCTAssertFalse(FileManager.default.fileExists(atPath: dir.path + "/history/a.files.gz"))
        XCTAssertEqual(store.remove(id: "a"), .unchanged)
    }

    func testNewId() {
        let id = HistoryStore.newId(finished: Date(timeIntervalSince1970: 1_791_000_000))
        XCTAssertNotNil(id.range(of: #"^20261003T040000Z-[0-9a-f]{8}$"#, options: .regularExpression), id)
    }

    private func sha256Hex(_ s: String) -> String { SHA256Hex.of(Data(s.utf8)) }
}

extension HistoryEntry {
    func with(_ f: (inout HistoryEntry) -> Void) -> HistoryEntry {
        var x = self
        f(&x)
        return x
    }
}
