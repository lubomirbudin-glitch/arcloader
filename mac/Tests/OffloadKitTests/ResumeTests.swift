import XCTest
@testable import OffloadKit

/// Task 69: "Finish interrupted backup" — an append completes the generation an interrupted run never committed
/// (Review focus 10: `testPendingGenerationIsCompletedNotRenumbered`).
final class ResumeTests: XCTestCase {
    static let block = 1 << 20

    struct Setup {
        let card: String
        let roots: [String]
        let history: String
    }

    func setup(_ dir: TempDir, _ files: [(String, Int)]) -> Setup {
        let card = dir.sub("A001")
        for (k, (rel, size)) in files.enumerated() { TestFS.writeRandom(card + "/" + rel, size: size, seed: UInt64(k + 21)) }
        return Setup(card: card, roots: [dir.sub("d1"), dir.sub("d2")], history: dir.sub("history"))
    }

    func options(_ cfg: JobConfig) -> OffloadOptions {
        var o = OffloadJob.engineOptions(cfg)
        o.chunkSize = ResumeTests.block
        o.allowSourceVolume = true
        o.allowSameDevice = true
        return o
    }

    /// A full copy with its verification deferred (no proxies needed — the test switch), configured by `change`.
    func fullJob(_ s: Setup, _ change: (inout OffloadOptions) -> Void = { _ in }) -> OffloadJob {
        var cfg = JobConfig(source: s.card, destRoots: s.roots)
        cfg.date = "2026-10-08"
        cfg.cardName = "A001"
        cfg.analyzeClips = false
        cfg.historyFolder = s.history
        var o = options(cfg)
        change(&o)
        let j = OffloadJob(config: cfg, options: o)
        j.deferVerifyOverride = true
        return j
    }

    /// "Finish interrupted backup": an append into the backup's card folders (classic read-back).
    func finishJob(_ s: Setup, _ paths: [String]) -> OffloadJob {
        var cfg = JobConfig(source: s.card, destRoots: [])
        cfg.appendInto = paths
        cfg.analyzeClips = false
        cfg.historyFolder = s.history
        return OffloadJob(config: cfg, options: options(cfg))
    }

    /// The app ends in stage 2, before the commit: the reports say verifyPending, no ASC MHL.
    func crashBeforeCommit(_ s: Setup) throws -> [String] {
        let j = fullJob(s)
        j.onVerifySession = { _ in j.cancel() }
        j.run()
        let paths = try XCTUnwrap(j.plan).cardPaths
        for p in paths { XCTAssertEqual(try OffloadReport.read(p + "/_REPORT/offload_report.json").verifyPending, true) }
        return paths
    }

    func assertCommitted(_ paths: [String], generation g: Int = 1, file: StaticString = #filePath, line: UInt = #line) throws {
        for p in paths {
            let r = try OffloadReport.read(p + "/_REPORT/" + OffloadReport.jsonName(g))
            XCTAssertNil(r.verifyPending, file: file, line: line)
            XCTAssertNil(r.verifyIncomplete, file: file, line: line)
            XCTAssertTrue(r.verified, file: file, line: line)
            XCTAssertEqual(MHLReader.readChain(ascmhlDir: p + "/ascmhl")?.map(\.seq), Array(1...g), file: file, line: line)
            XCTAssertTrue(CardRecord.load(p).verified, p, file: file, line: line)
            XCTAssertFalse(FileManager.default.fileExists(atPath: p + "/_REPORT/" + OffloadReport.jsonName(g + 1)), "never renumbered",
                           file: file, line: line)
        }
    }

    // Review focus 10.
    func testPendingGenerationIsCompletedNotRenumbered() throws {
        let s = setup(tempDir(), [("CLIP/A001C001.mov", (2 << 20) + 5), ("A001.xml", 400)])
        let paths = try crashBeforeCommit(s)
        let u = try XCTUnwrap(try ResumePlanner.find(paths))
        XCTAssertEqual(u.generation, 1)
        let ap = try AppendPlanner.plan(s.card, cardPaths: paths, blockSize: ResumeTests.block)
        XCTAssertTrue(ap.resuming)
        XCTAssertEqual(ap.generation, 1)
        XCTAssertEqual(ap.newFiles.count, 2)
        let j = finishJob(s, paths)
        j.run()
        let st = j.status()
        XCTAssertEqual(st.phase, .done, st.error ?? "")
        XCTAssertTrue(st.safeToFormat, st.warning ?? "")
        try assertCommitted(paths)
        XCTAssertNil(try ResumePlanner.find(paths), "nothing unfinished any more")
        let e = try XCTUnwrap(HistoryStore(folder: s.history).load().first)
        XCTAssertFalse(e.unfinished)
        XCTAssertTrue(e.verified)
        XCTAssertEqual(e.generations.map(\.seq), [1])
    }

    func testKnownFilesNotReadAgain() throws {
        let s = setup(tempDir(), [("CLIP/A001C001.mov", (3 << 20) + 5), ("A001.xml", 400)])
        let paths = try crashBeforeCommit(s)
        let j = finishJob(s, paths)
        j.run()
        XCTAssertEqual(j.status().phase, .done, j.status().error ?? "")
        XCTAssertEqual(j.engine.snapshot().readBytes, 0, "every file is complete everywhere with its record: the card is not read")
        XCTAssertTrue(j.status().safeToFormat)
        try assertCommitted(paths)
        // The copies were read back (verified), with the hashes the manifest recorded.
        let r = try OffloadReport.read(paths[0] + "/_REPORT/offload_report.json")
        XCTAssertTrue(r.files.allSatisfy { $0.destinations == ["verified", "verified"] && $0.xxh64 != nil })
    }

    /// A crash in the middle of a 5-block file after a checkpoint of 2 blocks.
    func crashInsideTheFile(_ s: Setup) throws -> [String] {
        let j = fullJob(s) { o in
            o.checkpointBlocks = 2
            o.crashAfterBlock = { file, block in file == 0 && block == 2 }
        }
        XCTAssertFalse(j.runCopy())
        XCTAssertEqual(j.status().phase, .failed)
        let paths = try XCTUnwrap(j.plan).cardPaths
        for p in paths {
            XCTAssertTrue(FileManager.default.fileExists(atPath: p + "/" + DestWriter.partName("CLIP/A001C001.mov")), "the part stays")
            XCTAssertEqual(ManifestFile.parts(ManifestFile.read(p), generation: 1)["clip/a001c001.mov"]?.blocks.count, 2)
            XCTAssertFalse(FileManager.default.fileExists(atPath: p + "/_REPORT/offload_report.json"))
        }
        return paths
    }

    func testPartResumesFromCommonCheckpoint() throws {
        let s = setup(tempDir(), [("CLIP/A001C001.mov", 5 << 20)])
        let paths = try crashInsideTheFile(s)
        let ap = try AppendPlanner.plan(s.card, cardPaths: paths, blockSize: ResumeTests.block)
        XCTAssertEqual(ap.resume?.first??.blocks.count, 2)
        XCTAssertEqual(ap.write?.first, [true, true])
        let j = finishJob(s, paths)
        j.run()
        XCTAssertEqual(j.status().phase, .done, j.status().error ?? "")
        XCTAssertEqual(j.engine.resumedBytes, 2 << 20, "blocks 0–1 from the checkpoint")
        XCTAssertEqual(j.engine.snapshot().readBytes, 3 << 20, "the card is read from block 2")
        XCTAssertTrue(j.status().safeToFormat, j.status().warning ?? "")
        try assertCommitted(paths)
        let card = try Data(contentsOf: URL(fileURLWithPath: s.card + "/CLIP/A001C001.mov"))
        for p in paths { XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: p + "/CLIP/A001C001.mov")), card) }
    }

    func testCheckpointMismatchStartsOver() throws {
        let s = setup(tempDir(), [("CLIP/A001C001.mov", 5 << 20)])
        let paths = try crashInsideTheFile(s)
        let part = paths[1] + "/" + DestWriter.partName("CLIP/A001C001.mov")
        let h = try XCTUnwrap(FileHandle(forUpdatingAtPath: part))
        try h.seek(toOffset: 100)
        h.write(Data([0xEE, 0xEE]))
        try h.close()
        let j = finishJob(s, paths)
        j.run()
        XCTAssertEqual(j.status().phase, .done, j.status().error ?? "")
        XCTAssertEqual(j.engine.resumedBytes, 0, "a checkpoint that does not read back is not trusted")
        XCTAssertEqual(j.engine.snapshot().readBytes, 5 << 20)
        XCTAssertTrue(j.status().safeToFormat)
        let card = try Data(contentsOf: URL(fileURLWithPath: s.card + "/CLIP/A001C001.mov"))
        for p in paths { XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: p + "/CLIP/A001C001.mov")), card) }
    }

    // The commit wrote the ASC MHL into the second folder but not its reports; the first says verifyIncomplete.
    func testIncompleteCommitIsFinished() throws {
        let s = setup(tempDir(), [("CLIP/A001C001.mov", (2 << 20) + 1), ("A001.xml", 300)])
        let j = fullJob(s)
        var locked: String?
        j.beforeCommit = { path in
            guard path.hasPrefix(s.roots[1]) else { return }
            locked = path + "/_REPORT"
            try? FileManager.default.setAttributes([.posixPermissions: 0o555], ofItemAtPath: locked!)
        }
        j.run()
        if let locked { try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: locked) }
        let paths = try XCTUnwrap(j.plan).cardPaths
        XCTAssertFalse(j.status().safeToFormat)
        XCTAssertEqual(try OffloadReport.read(paths[0] + "/_REPORT/offload_report.json").verifyIncomplete, true)
        XCTAssertEqual(try OffloadReport.read(paths[1] + "/_REPORT/offload_report.json").verifyPending, true)
        XCTAssertEqual(MHLReader.readChain(ascmhlDir: paths[1] + "/ascmhl")?.map(\.seq), [1], "the MHL came first")
        let u = try XCTUnwrap(try ResumePlanner.find(paths))
        XCTAssertEqual(u.generation, 1)
        XCTAssertNotNil(u.fixedFiles, "in a chain already: its files are fixed")
        let f = finishJob(s, paths)
        f.run()
        XCTAssertEqual(f.status().phase, .done, f.status().error ?? "")
        XCTAssertTrue(f.status().safeToFormat, f.status().warning ?? "")
        try assertCommitted(paths)
    }

    func testDivergedRefused() throws {
        let s = setup(tempDir(), [("A.bin", 3000)])
        let paths = try crashBeforeCommit(s)
        // The second folder's report no longer says pending, and it has no generation: it does not stand before g.
        var r = try OffloadReport.read(paths[1] + "/_REPORT/offload_report.json")
        r.verifyPending = nil
        try r.jsonData().write(to: URL(fileURLWithPath: paths[1] + "/_REPORT/offload_report.json"))
        XCTAssertThrowsError(try ResumePlanner.find(paths)) { e in
            XCTAssertEqual((e as? OffloadError)?.code, .appendDiverged)
            XCTAssertEqual("\(e)", VIPMsg.resumeDiverged(paths[1], 1))
        }
        let j = finishJob(s, paths)
        j.run()
        XCTAssertEqual(j.status().phase, .failed)
        XCTAssertEqual(j.status().errorCode, .appendDiverged)
    }

    // A file at its place that no report lists: the interrupted run's own (its manifest knows it) is no conflict.
    func testUnknownFileOwnedByTheRunIsNoConflict() throws {
        let s = setup(tempDir(), [("A.bin", (2 << 20) + 3), ("B.bin", 3 << 20)])
        let j = fullJob(s) { o in
            o.fullSyncThreshold = 0   // every file flushed alone: its record is written at once
            o.crashAfterBlock = { file, block in file == 1 && block == 0 }
        }
        j.runCopy()
        let paths = try XCTUnwrap(j.plan).cardPaths
        for p in paths { XCTAssertTrue(FileManager.default.fileExists(atPath: p + "/A.bin")) }
        let ap = try AppendPlanner.plan(s.card, cardPaths: paths, blockSize: ResumeTests.block)
        let ia = try XCTUnwrap(ap.newFiles.firstIndex { $0.relPath == "A.bin" })
        XCTAssertNotNil(ap.known?[ia] ?? nil, "complete everywhere with its record")
        let f = finishJob(s, paths)
        f.run()
        XCTAssertEqual(f.status().phase, .done, f.status().error ?? "")
        XCTAssertEqual(f.engine.snapshot().readBytes, 3 << 20, "only B read from the card")
        XCTAssertTrue(f.status().safeToFormat)
        try assertCommitted(paths)
    }

    func testForeignFileIsStillAConflict() throws {
        let s = setup(tempDir(), [("A.bin", 3000), ("B.bin", 4000)])
        let j = fullJob(s) { o in
            o.fullSyncThreshold = 0   // A.bin recorded at once: the manifest knows the interrupted generation
            o.crashAfterBlock = { file, _ in file == 1 }
        }
        j.runCopy()
        let paths = try XCTUnwrap(j.plan).cardPaths
        // Someone else's file where the card's B.bin goes: not in the manifest — never trusted, never overwritten.
        try Data(repeating: 1, count: 4000).write(to: URL(fileURLWithPath: paths[0] + "/B.bin"))
        XCTAssertThrowsError(try AppendPlanner.plan(s.card, cardPaths: paths, blockSize: ResumeTests.block)) { e in
            XCTAssertEqual((e as? OffloadError)?.code, .appendConflict)
        }
    }

    func testCommittedBackupIsNotUnfinished() throws {
        let s = setup(tempDir(), [("A.bin", 3000)])
        let j = fullJob(s)
        j.run()
        XCTAssertTrue(j.status().safeToFormat)
        XCTAssertNil(try ResumePlanner.find(try XCTUnwrap(j.plan).cardPaths))
    }

    func testFinishTextsAndOffer() {
        var e = HistoryEntry(id: "x", source: "/Volumes/A001", cardName: "A001", date: "2026-10-08", finished: Date())
        e.unfinished = true
        e.destinations = [HistoryDestination(root: "/r", cardPath: "/r/2026-10-08/A001")]
        let m = HistoryMatch(entry: e, kind: .identical, new: [], newBytes: 0, changed: [], removed: 0)
        XCTAssertTrue(AppendRules.isResume(m))
        XCTAssertEqual(AppendRules.checkboxText(m), "Finish interrupted backup (finishes the verification)")
        var i = CardInsight(source: "/Volumes/A001")
        i.volume = CardVolume(identity: "id", fsSerial: "1", capacity: 1, label: "A001", filesystem: "exfat")
        i.appendReachable = true
        i.match = m
        XCTAssertTrue(AppendRules.offer(i), "offered with nothing new on the card")
        e.unfinished = false
        i.match = HistoryMatch(entry: e, kind: .identical, new: [], newBytes: 0, changed: [], removed: 0)
        XCTAssertFalse(AppendRules.offer(i))
    }
}
