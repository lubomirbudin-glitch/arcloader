import XCTest
@testable import OffloadKit

final class HistoryMatcherTests: XCTestCase {
    let t = Date(timeIntervalSince1970: 1_791_000_000)
    func f(_ p: String, _ s: Int64, _ d: Date) -> SourceFile { SourceFile(relPath: p, size: s, modified: d) }
    func scan(_ files: [SourceFile]) -> SourceScan { SourceScan(root: "/c", files: files, dirs: [], skipped: []) }
    func entry(_ fp: String) -> HistoryEntry {
        var e = HistoryEntry(id: "x", source: "/c", cardName: "A001", date: "2026-10-08", finished: t)
        e.fingerprint = fp
        return e
    }

    func testKinds() {
        let stored = [f("A", 1, t), f("B", 2, t)]
        XCTAssertEqual(HistoryMatcher.match(scan(stored), fingerprint: "same", candidate: entry("same"), candidateFiles: nil, filesystem: "exfat").kind, .identical)
        let more = HistoryMatcher.match(scan(stored + [f("C", 3, t)]), fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "exfat")
        XCTAssertEqual(more.kind, .newFiles)
        XCTAssertEqual(more.new.map(\.relPath), ["C"])
        XCTAssertEqual(more.newBytes, 3)
        let changed = HistoryMatcher.match(scan([f("A", 9, t), f("B", 2, t)]), fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "exfat")
        XCTAssertEqual(changed.kind, .changed)
        XCTAssertEqual(changed.changed, ["A"])
        XCTAssertEqual(HistoryMatcher.match(scan(stored), fingerprint: "x", candidate: entry("y"), candidateFiles: nil, filesystem: nil).kind, .notCompared)
        let fewer = HistoryMatcher.match(scan([f("A", 1, t)]), fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "exfat")
        XCTAssertEqual(fewer.kind, .identical)
        XCTAssertEqual(fewer.removed, 1)
    }

    func testFatHourShiftIsNotAChangeButExfatIs() {
        let stored = [f("A", 1, t)]
        let shifted = scan([f("A", 1, t.addingTimeInterval(3600))])
        XCTAssertEqual(HistoryMatcher.match(shifted, fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "msdos").kind, .identical)
        XCTAssertEqual(HistoryMatcher.match(shifted, fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "FAT32").kind, .identical)
        XCTAssertEqual(HistoryMatcher.match(shifted, fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "exfat").kind, .changed)
    }

    /// APFS keeps nanoseconds; the stored list keeps .NET ticks (100 ns): the same file must stay the same.
    func testNanosecondsBeyondTicksAreTheSameTime() throws {
        let card = SourceFile(relPath: "A", size: 1, mtime: timespec(tv_sec: 1_791_000_000, tv_nsec: 123_456_789))
        let dir = tempDir()
        try FileListStore.write(dir.file("l.gz"), [card])
        let stored = try XCTUnwrap(FileListStore.read(dir.file("l.gz")))
        XCTAssertEqual(HistoryMatcher.match(scan([card]), fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "apfs").kind, .identical)
    }

    func testAnotherCardIsChangedNeverNewFiles() {
        let m = HistoryMatcher.match(scan([f("X", 1, t)]), fingerprint: "x", candidate: entry("y"), candidateFiles: [f("A", 1, t)], filesystem: "exfat")
        XCTAssertEqual(m.kind, .changed)
        XCTAssertTrue(m.new.isEmpty)
    }

    func testHistoryLines() {
        var e = entry("y")
        e.success = true; e.verified = true
        e.destinations = [HistoryDestination(root: "/Volumes/RAID-not-here/Zaloha", cardPath: "/Volumes/RAID-not-here/Zaloha/2026-10-08/A001")]
        let m = HistoryMatch(entry: e, kind: .newFiles, new: [f("C", 3_200_000_000, t)], newBytes: 3_200_000_000, changed: [], removed: 0)
        let (text, tone) = HistoryText.line(m)
        XCTAssertEqual(text, "Backed up on \(HistoryText.when(t)) to Zaloha (RAID-not-here) — verified, safe to format · new files: 1 (3.2 GB)")
        XCTAssertEqual(tone, .warning)
        XCTAssertTrue(HistoryText.line(m, fullPaths: true).0.hasPrefix("Backed up on \(HistoryText.when(t)) to /Volumes/RAID-not-here/Zaloha — "))
        let same = HistoryMatch(entry: e, kind: .identical, new: [], newBytes: 0, changed: [], removed: 0)
        XCTAssertEqual(HistoryText.line(same).1, .ok)
        var unverified = e
        unverified.verified = false
        XCTAssertEqual(HistoryText.line(HistoryMatch(entry: unverified, kind: .identical, new: [], newBytes: 0, changed: [], removed: 0)).0,
                       "Backed up on \(HistoryText.when(t)) to Zaloha (RAID-not-here) — NOT VERIFIED (safe to eject, do not format)")
        var failed = e
        failed.success = false; failed.failedFiles = 3
        let bad = HistoryText.line(HistoryMatch(entry: failed, kind: .identical, new: [], newBytes: 0, changed: [], removed: 0))
        XCTAssertEqual(bad.0, "Backed up on \(HistoryText.when(t)) to Zaloha (RAID-not-here) — with errors (3 files)")
        XCTAssertEqual(bad.1, .error)
        let changed = HistoryMatch(entry: e, kind: .changed, new: [], newBytes: 0, changed: ["a", "b", "c", "d"], removed: 0)
        XCTAssertEqual(HistoryText.line(changed).0,
                       "Backed up on \(HistoryText.when(t)) to Zaloha (RAID-not-here), but the card has changed since (4 files differ) — cannot append, only a full copy")
        e.unfinished = true
        XCTAssertEqual(HistoryText.line(HistoryMatch(entry: e, kind: .identical, new: [], newBytes: 0, changed: [], removed: 0)).0,
                       "Unfinished backup on \(HistoryText.when(t)) to Zaloha (RAID-not-here) — the copy was interrupted; it can be finished")
        XCTAssertEqual(HistoryText.emptied(same).0, "The card is empty — backed up earlier on \(HistoryText.when(t)) to Zaloha (RAID-not-here)")
    }

    func testDiskNames() {
        XCTAssertEqual(HistoryText.diskName("/Volumes/RAID-not-here/Zaloha"), "Zaloha (RAID-not-here)")
        XCTAssertEqual(HistoryText.diskName("/Volumes/RAID-not-here/"), "RAID-not-here")
    }

    func testWhenFormat() {
        XCTAssertNotNil(HistoryText.when(t).range(of: #"^2026-10-0\d \d\d:\d\d$"#, options: .regularExpression))
    }

    func testInspectFolderAgainstHistory() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        _ = TestFS.writeRandom(card + "/CLIP/A001C001.mov", size: 100, seed: 1)
        _ = TestFS.writeRandom(card + "/CLIP/A001C002.mov", size: 100, seed: 2)
        let store = HistoryStore(folder: dir.sub("hist"))
        // Nothing in history yet: no match, but the name from the clips.
        var insight = try CardInspector.inspect(card, store: store, history: store.load())
        XCTAssertNil(insight.match)
        XCTAssertNil(insight.volume, "a folder, not a volume root")
        XCTAssertEqual(insight.nameSuggestion?.name, "A001")
        XCTAssertEqual(insight.fileCount, 2)
        let scan = try Scanner.scan(card)
        let (fp, shape) = CardFingerprint.compute(scan.files)
        _ = store.update(id: "h") { _ in
            var e = HistoryEntry(id: "h", source: card, cardName: "A001", date: "2026-10-08", finished: Date())
            e.fingerprint = fp; e.shape = shape; e.success = true; e.verified = true
            e.destinations = [HistoryDestination(root: dir.path, cardPath: dir.sub("dest", "2026-10-08", "A001"))]
            return (e, scan.files)
        }
        insight = try CardInspector.inspect(card, store: store, history: store.load())
        XCTAssertEqual(insight.match?.kind, .identical)
        XCTAssertFalse(insight.appendReachable, "a folder is never appended to by its shape alone")
        // A new clip changes the shape, so a folder is no longer found (shape match only) — volumes match by serial.
        _ = TestFS.writeRandom(card + "/CLIP/A001C003.mov", size: 100, seed: 3)
        insight = try CardInspector.inspect(card, store: store, history: store.load())
        XCTAssertNil(insight.match)
        XCTAssertThrowsError(try CardInspector.inspect(card, store: store, history: nil, isCancelled: { true }))
    }

    func testInspectVolumeFindsNewFilesAndAppendReachable() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        _ = TestFS.writeRandom(card + "/CLIP/A001C001.mov", size: 100, seed: 1)
        let store = HistoryStore(folder: dir.sub("hist"))
        let vol = CardVolume(identity: "uuid:X", fsSerial: "1A2B3C4D", capacity: 64, label: "A001", filesystem: "exFAT")
        let before = try Scanner.scan(card)
        let cardFolder = dir.sub("dest", "2026-10-08", "A001")
        _ = store.update(id: "h") { _ in
            var e = HistoryEntry(id: "h", source: card, cardName: "A001", date: "2026-10-08", finished: Date())
            e.card = HistoryCard(identity: "uuid:X", fsSerial: "1A2B3C4D", capacity: 64, label: "A001", filesystem: "exFAT")
            e.fingerprint = CardFingerprint.compute(before.files).fingerprint; e.success = true; e.verified = true
            e.destinations = [HistoryDestination(root: dir.sub("dest"), cardPath: cardFolder)]
            return (e, before.files)
        }
        _ = TestFS.writeRandom(card + "/CLIP/A001C002.mov", size: 50, seed: 2)
        let insight = try CardInspector.inspect(card, store: store, history: store.load(), isCancelled: { false },
                                                volumeOf: { _ in vol }, identify: { _ in "uuid:X" }, isVolumeRoot: { _ in true })
        XCTAssertEqual(insight.match?.kind, .newFiles)
        XCTAssertEqual(insight.match?.newBytes, 50)
        XCTAssertTrue(insight.appendReachable)
        let swapped = try CardInspector.inspect(card, store: store, history: store.load(), isCancelled: { false },
                                                volumeOf: { _ in vol }, identify: { _ in "uuid:Y" }, isVolumeRoot: { _ in true })
        XCTAssertEqual(swapped.error, Msg.cardSwapped)
        let unreadable = try CardInspector.inspect(card, store: store, history: store.load(), isCancelled: { false },
                                                   volumeOf: { _ in nil }, identify: { _ in nil }, isVolumeRoot: { _ in true })
        XCTAssertEqual(unreadable.error, Msg.cardVolumeUnreadable)
    }
}
