import XCTest
@testable import OffloadKit

final class AppendEngineTests: XCTestCase {
    func appendJob(_ s: AppendPlannerTests.Setup, verify: Bool = true, history: String? = nil) -> OffloadJob {
        var cfg = JobConfig(source: s.card, destRoots: [])
        cfg.appendInto = s.cardPaths
        cfg.analyzeClips = false
        cfg.verify = verify
        cfg.historyFolder = history ?? tempDir().path
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        o.allowSameDevice = true
        return OffloadJob(config: cfg, options: o)
    }

    func bytes(_ paths: [String]) throws -> [Data] { try paths.map { try Data(contentsOf: URL(fileURLWithPath: $0)) } }

    /// Every file of the card folders with its bytes — except the three FORMATY lets an append extend: the hash list (only
    /// extended, checked on its own), the chain (its earlier entries the same, checked on its own) and the block manifest
    /// (only appended to — `assertManifestsOnlyExtended`).
    func earlierFiles(_ s: AppendPlannerTests.Setup) throws -> [String: Data] {
        var out: [String: Data] = [:]
        for p in s.cardPaths {
            for f in TestFS.files(in: p) where !f.hasSuffix(".xxh64") && f != "ascmhl/ascmhl_chain.xml"
                && f != "_REPORT/" + ManifestFile.fileName {
                out[p + "/" + f] = try Data(contentsOf: URL(fileURLWithPath: p + "/" + f))
            }
        }
        return out
    }

    /// The block manifests of the card folders as they are now (absent = empty).
    func manifests(_ s: AppendPlannerTests.Setup) -> [Data] {
        s.cardPaths.map { FileManager.default.contents(atPath: ManifestFile.path(in: $0)) ?? Data() }
    }

    /// FORMATY „Blokový manifest“: an append only adds lines — every earlier byte stays where it was.
    func assertManifestsOnlyExtended(_ s: AppendPlannerTests.Setup, _ before: [Data], file: StaticString = #filePath, line: UInt = #line) {
        for (i, now) in manifests(s).enumerated() {
            XCTAssertEqual(now.prefix(before[i].count), before[i], "the manifest is only appended to", file: file, line: line)
        }
    }

    // Review focus 1.
    func testOriginalReportsAndGeneration1ByteIdentical() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        let before = try earlierFiles(s)
        let manifestsBefore = manifests(s)
        let lists = try bytes(s.cardPaths.map { $0 + "/_REPORT/A001.xxh64" })
        let chains = s.cardPaths.map { MHLReader.readChain(ascmhlDir: $0 + "/ascmhl") }
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let job = appendJob(s)
        job.run()
        for (i, p) in s.cardPaths.enumerated() {
            let chain = try XCTUnwrap(MHLReader.readChain(ascmhlDir: p + "/ascmhl"))
            XCTAssertEqual(Array(chain.prefix(1)), chains[i], "the earlier chain entries stay as they were")
        }
        XCTAssertEqual(job.status().phase, .done, job.status().error ?? "")
        XCTAssertTrue(job.status().safeToFormat)
        XCTAssertEqual(job.plan?.cardPaths, s.cardPaths, "the same card folders, no _2")
        // Every earlier file — data, reports, PDF-less HTML, generation 1 of the MHL — byte for byte as it was.
        for (path, data) in before {
            XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: path)), data, path)
        }
        assertManifestsOnlyExtended(s, manifestsBefore)
        for (i, p) in s.cardPaths.enumerated() {
            XCTAssertTrue(FileManager.default.fileExists(atPath: p + "/_REPORT/offload_report_0002.json"))
            XCTAssertTrue(FileManager.default.fileExists(atPath: p + "/_REPORT/A001_report_0002.html"))
            XCTAssertEqual(MHLReader.read(cardPath: p)?.chain.map(\.seq), [1, 2])
            let list = try Data(contentsOf: URL(fileURLWithPath: p + "/_REPORT/A001.xxh64"))
            XCTAssertEqual(list.prefix(lists[i].count), lists[i], "the hash list is only extended")
            let text = String(decoding: list, as: UTF8.self)
            XCTAssertEqual(text.split(separator: "\n").count, 3)
            XCTAssertTrue(text.hasSuffix("  CLIP/A001C003.mov\n"))
            let r = try OffloadReport.read(p + "/_REPORT/offload_report_0002.json")
            XCTAssertEqual(r.generation, 2)
            XCTAssertEqual(r.files.map(\.path), ["CLIP/A001C003.mov"])
        }
        let r = try BackupVerifier.verify(s.cardPaths[1], progress: nil, isCancelled: { false })
        XCTAssertEqual(r.ok, 3)
        XCTAssertEqual(r.generations, 2)
        XCTAssertTrue(r.success)
        XCTAssertEqual(BackupState.of(s.cardPaths), BackupState(failed: 0, unverified: 0))
    }

    func testFileThatAppearsMeanwhileIsNotOverwritten() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let job = appendJob(s)
        let intruder = s.cardPaths[0] + "/CLIP/A001C003.mov"
        job.engine.testBeforeRename = { path in
            if path == intruder { try? Data("other".utf8).write(to: URL(fileURLWithPath: intruder)) }
        }
        job.run()
        XCTAssertEqual(try String(contentsOfFile: intruder, encoding: .utf8), "other")
        let r = try OffloadReport.read(s.cardPaths[0] + "/_REPORT/offload_report_0002.json")
        XCTAssertEqual(r.files[0].destinations, ["failed", "verified"])
        XCTAssertTrue(r.files[0].errors[0]?.contains("file appeared meanwhile") == true)
        XCTAssertFalse(job.status().safeToFormat)
        XCTAssertFalse(job.status().safeToEject)
        XCTAssertFalse(TestFS.files(in: s.cardPaths[0]).contains { $0.hasSuffix(".offload-part") }, "the temporary copy is gone")
    }

    func testNothingNewIsDoneWithNote() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        let job = appendJob(s)
        job.run()
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertEqual(job.status().note, Msg.nothingNew)
        XCTAssertTrue(job.status().safeToFormat)
        XCTAssertFalse(FileManager.default.fileExists(atPath: s.cardPaths[0] + "/_REPORT/offload_report_0002.json"))
    }

    func testNothingNewHealsAHashListThatMissedLines() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        let list = s.cardPaths[0] + "/_REPORT/A001.xxh64"
        let full = try String(contentsOfFile: list, encoding: .utf8)
        let first = String(full.split(separator: "\n")[0]) + "\n"
        try first.write(toFile: list, atomically: true, encoding: .utf8)
        appendJob(s).run()
        let healed = try String(contentsOfFile: list, encoding: .utf8)
        XCTAssertTrue(healed.hasPrefix(first))
        XCTAssertEqual(Set(healed.split(separator: "\n")), Set(full.split(separator: "\n")))
    }

    func testBusyFolderRefusedWithoutWriting() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let held = try FolderLocks.acquire([s.cardPaths[1]])
        let job = appendJob(s)
        job.run()
        XCTAssertEqual(job.status().phase, .failed)
        XCTAssertEqual(job.status().errorCode, .appendBusy)
        XCTAssertFalse(FileManager.default.fileExists(atPath: s.cardPaths[0] + "/CLIP/A001C003.mov"))
        held.release()
    }

    func testRefusedPlanWritesNothing() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C002.mov", size: 2999, seed: 2)
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let before = s.cardPaths.map { TestFS.files(in: $0) }
        let job = appendJob(s)
        job.run()
        XCTAssertEqual(job.status().errorCode, .appendConflict)
        XCTAssertEqual(s.cardPaths.map { TestFS.files(in: $0) }, before)
    }

    func testUnverifiedEarlierKeepsCardFromFormat() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        for p in s.cardPaths {
            var r = try OffloadReport.read(p + "/_REPORT/offload_report.json")
            r.verified = false
            r.files = r.files.map { var f = $0; f.destinations = f.destinations.map { _ in "written" }; return f }
            try r.jsonData().write(to: URL(fileURLWithPath: p + "/_REPORT/offload_report.json"))
        }
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let job = appendJob(s)
        job.run()
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertTrue(job.status().safeToEject)
        XCTAssertFalse(job.status().safeToFormat)
        XCTAssertEqual(job.status().warning, Msg.unverifiedEarlier(2))
    }

    func testCancelledAppendKeepsEverythingEarlier() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        let before = try earlierFiles(s)
        let manifestsBefore = manifests(s)
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C004.mov", size: 3000, seed: 4)
        var cfg = JobConfig(source: s.card, destRoots: [])
        cfg.appendInto = s.cardPaths
        cfg.analyzeClips = false
        cfg.historyFolder = tempDir().path
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        o.allowSameDevice = true
        weak var ref: OffloadJob?
        o.beforeFileCopy = { i in if i == 1 { ref?.cancel() } }
        let job = OffloadJob(config: cfg, options: o)
        ref = job
        job.run()
        XCTAssertEqual(job.status().phase, .cancelled)
        XCTAssertFalse(job.status().safeToFormat)
        for (path, data) in before { XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: path)), data, path) }
        assertManifestsOnlyExtended(s, manifestsBefore)
        for p in s.cardPaths {
            XCTAssertFalse(TestFS.files(in: p).contains { $0.hasSuffix(".offload-part") })
            XCTAssertFalse(FileManager.default.fileExists(atPath: p + "/CLIP/A001C004.mov"))
        }
        XCTAssertTrue(HistoryStore(folder: cfg.historyFolder!).load().isEmpty, "a cancelled append is not recorded")
    }

    func testHistoryGetsAnAppendGeneration() throws {
        let dir = tempDir()
        let hist = dir.sub("hist")
        let card = dir.sub("card")
        _ = TestFS.writeRandom(card + "/CLIP/A001C001.mov", size: 3000, seed: 1)
        var cfg = JobConfig(source: card, destRoots: [dir.sub("D"), dir.sub("E")])
        cfg.analyzeClips = false
        cfg.historyFolder = hist
        cfg.project = "Film X"
        cfg.date = "2026-10-08"
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        o.allowSameDevice = true
        let full = OffloadJob(config: cfg, options: o)
        full.run()
        let cardPaths = try XCTUnwrap(full.plan?.cardPaths)
        _ = TestFS.writeRandom(card + "/CLIP/A001C002.mov", size: 2000, seed: 2)
        let a = AppendPlannerTests.Setup(card: card, cardPaths: cardPaths)
        let job = appendJob(a, history: hist)
        job.run()
        XCTAssertEqual(job.status().phase, .done)
        let entries = HistoryStore(folder: hist).load()
        XCTAssertEqual(entries.count, 1)
        let e = try XCTUnwrap(entries.first)
        XCTAssertEqual(e.generations.map(\.kind), ["full", "append"])
        XCTAssertEqual(e.generations.map(\.seq), [1, 2])
        XCTAssertEqual(e.files, 2)
        XCTAssertEqual(e.bytes, 5000)
        XCTAssertEqual(e.project, "Film X", "the project of the backup is kept")
        XCTAssertTrue(e.verified)
        XCTAssertEqual(HistoryStore(folder: hist).readFileList(e)?.count, 2, "the union with the earlier list")
        XCTAssertEqual(try OffloadReport.read(cardPaths[0] + "/_REPORT/offload_report_0002.json").project, "Film X")
        // The card inspected again: the entry has the card as it is now (fingerprint and shape after the append).
        let insight = try CardInspector.inspect(card, store: HistoryStore(folder: hist), history: entries)
        XCTAssertEqual(insight.match?.kind, .identical)
    }

    func testProxyNamesOnEveryDestinationAreTaken() throws {
        let dir = tempDir()
        _ = TestFS.writeRandom(dir.path + "/D/2026-10-08/A001/_PROXY/A001C001.mov", size: 1, seed: 1)
        _ = TestFS.writeRandom(dir.path + "/E/2026-10-08/A001/_PROXY/CLIP/A001C002.MOV", size: 1, seed: 1)
        // Keys are the path below _PROXY (task 54: proxies in folders as on the card), any case.
        XCTAssertEqual(ProxyPaths.existing([dir.path + "/D/2026-10-08/A001", dir.path + "/E/2026-10-08/A001"]), ["a001c001", "clip/a001c002"])
    }
}
