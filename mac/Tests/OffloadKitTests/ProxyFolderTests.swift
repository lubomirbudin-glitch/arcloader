import XCTest
@testable import OffloadKit

/// The extra copy of each proxy into the user's proxy folder: `<folder>/<date>/<card>/<name>.mov`, never overwriting.
final class ProxyFolderTests: XCTestCase {
    private func proxy(_ dir: TempDir, seed: UInt64 = 1) -> String {
        let path = dir.path + "/d1/2026-10-05/A001/_PROXY/C0001.mov"
        TestFS.writeRandom(path, size: 5000, seed: seed)
        return path
    }

    private func data(_ path: String) -> Data? { try? Data(contentsOf: URL(fileURLWithPath: path)) }

    func testCopiesIntoDateAndCardFolder() {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(error)
        XCTAssertEqual(path, extra + "/2026-10-05/A001/C0001.mov")
        XCTAssertEqual(data(src), data(extra + "/2026-10-05/A001/C0001.mov"))
        XCTAssertEqual(TestFS.files(in: extra), ["2026-10-05/A001/C0001.mov"])   // no .part left
    }

    // Review focus 6: an existing file is never overwritten — the copy gets _2, _3 …
    func testExistingNameGetsSuffixNeverOverwrites() {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        let target = extra + "/2026-10-05/A001"
        let old = TestFS.writeRandom(target + "/C0001.mov", size: 100, seed: 9)
        let second = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        let third = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(second.error)
        XCTAssertNil(third.error)
        XCTAssertEqual(second.path, target + "/C0001_2.mov")
        XCTAssertEqual(third.path, target + "/C0001_3.mov")
        XCTAssertEqual(data(target + "/C0001.mov"), old)
        XCTAssertEqual(data(target + "/C0001_3.mov"), data(src))
    }

    func testMissingFolderErrorNotThrowNothingCreated() {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.path + "/disconnected"
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(path)
        XCTAssertEqual(error, "složka není dostupná")
        XCTAssertFalse(FileManager.default.fileExists(atPath: extra))
    }

    func testFailedCopyErrorAndNoTempLeft() throws {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        let target = extra + "/2026-10-05/A001"
        TestFS.writeRandom(target + "/.C0001.mov.part", size: 10, seed: 3)   // left behind by a crash
        try FileManager.default.setAttributes([.posixPermissions: 0o000], ofItemAtPath: src)   // the source cannot be read
        defer { try? FileManager.default.setAttributes([.posixPermissions: 0o644], ofItemAtPath: src) }
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(path)
        XCTAssertFalse((error ?? "").isEmpty)
        XCTAssertEqual(TestFS.files(in: target), [])
    }

    // Review focus 6: a folder full of taken names ends with an error, never an endless loop or an overwrite.
    func testAllNamesTakenErrorAfterCap() throws {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        dir.sub("proxies", "2026-10-05", "A001", "C0001.mov")      // folders with the proxy's names
        for n in 2...3 { dir.sub("proxies", "2026-10-05", "A001", "C0001_\(n).mov") }
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001",
                                                    fileName: "C0001.mov", maxAttempts: 3)
        XCTAssertNil(path)
        XCTAssertEqual(error, "žádné volné jméno (C0001.mov … C0001_3.mov)")
        XCTAssertEqual(TestFS.files(in: extra), [])
    }

    func testStaleTempReplaced() {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        TestFS.writeRandom(extra + "/2026-10-05/A001/.C0001.mov.part", size: 10, seed: 3)
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(error)
        XCTAssertEqual(path, extra + "/2026-10-05/A001/C0001.mov")
        XCTAssertEqual(TestFS.files(in: extra + "/2026-10-05/A001"), ["C0001.mov"])
    }

    // Mac: renamex_np(RENAME_EXCL) — a file that appears between the check and the rename is never replaced.
    func testCopyFileWithoutReplaceRefusesAnExistingFile() throws {
        let dir = tempDir()
        let src = proxy(dir)
        let dst = dir.path + "/out/C0001.mov"
        let old = TestFS.writeRandom(dst, size: 7, seed: 5)
        XCTAssertThrowsError(try ProxyCopy.copyFile(src, to: dst, replace: false)) { XCTAssertTrue($0 is ProxyCopy.Taken, "\($0)") }
        XCTAssertEqual(data(dst), old)
        XCTAssertEqual(TestFS.files(in: dir.path + "/out"), ["C0001.mov"])
        try ProxyCopy.copyFile(src, to: dst)   // replace: true (copies to other destinations, the PDF) still replaces
        XCTAssertEqual(data(dst), data(src))
    }

    func testPreflightBlocksOnlyAMissingFolderInUse() {
        var p = ProxySettings()
        p.enabled = true
        p.extraFolder = "/Volumes/Proxy"
        XCTAssertEqual(ProxyPreflight.extraFolderInUse(p), "/Volumes/Proxy")
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p, folderExists: nil))   // not checked yet
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p, folderExists: true))
        XCTAssertEqual(ProxyPreflight.extraFolderBlocking(p, folderExists: false), "Složka pro proxy není dostupná: /Volumes/Proxy")
        p.enabled = false
        XCTAssertNil(ProxyPreflight.extraFolderInUse(p))
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p, folderExists: false))
        p.enabled = true
        p.extraFolder = nil
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p, folderExists: false))
    }

    func testPreflightChecksTheDisk() {
        let dir = tempDir()
        var p = ProxySettings()
        p.enabled = true
        p.extraFolder = dir.path
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p))
        p.extraFolder = dir.path + "/nope"
        XCTAssertEqual(ProxyPreflight.extraFolderBlocking(p), "Složka pro proxy není dostupná: \(dir.path)/nope")
    }

    func testSettingsExtraFolderTrimmedBlankIsNilAbsentWhenUnset() throws {
        func load(_ json: String) -> AppSettings { SettingsStore.decode(Data(json.utf8)) }
        XCTAssertNil(load(#"{"proxy":{"enabled":true}}"#).proxy.extraFolder)
        XCTAssertNil(load(#"{"proxy":{"enabled":true,"extraFolder":"  "}}"#).proxy.extraFolder)
        XCTAssertEqual(load(#"{"proxy":{"extraFolder":" /Volumes/P "}}"#).proxy.extraFolder, "/Volumes/P")
        var s = AppSettings()
        s.proxy.extraFolder = "/Volumes/Proxy"
        XCTAssertEqual(SettingsStore.decode(SettingsStore.encode(s)).proxy.extraFolder, "/Volumes/Proxy")
        let empty = String(decoding: try XCTUnwrap(SettingsStore.encode(AppSettings())), as: UTF8.self)
        XCTAssertFalse(empty.contains("extraFolder"))
    }

    func testHTMLProxyLineNamesTheFolder() {
        var r = Reports.sample()
        r.clips = [ClipEntry(clip: Clip(name: "C0001", path: "C0001.mov", format: .video, segments: ["C0001.mov"],
                                        bytes: 1, isCameraProxy: false))]
        var p = ProxySettings()
        p.enabled = true
        r.proxy = p
        XCTAssertFalse(HTMLReport.render(r).contains("také ve složce"))
        p.extraFolder = "/Volumes/Proxy & co"
        r.proxy = p
        XCTAssertTrue(HTMLReport.render(r).contains(
            "<span class=\"mono\">_PROXY/</span> · také ve složce <span class=\"mono\">/Volumes/Proxy &amp; co</span></p>"))
    }

    // MARK: Whole job (ffmpeg)

    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 1 << 20
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    private func job(_ dir: TempDir, extra: String) throws -> OffloadJob {
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/C0001.mov", seconds: 1)
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.date = "2026-10-05"
        config.proxy.enabled = true
        config.proxy.height = 360
        config.proxy.extraFolder = extra
        config.tools = TestMedia.tools
        return OffloadJob(config: config, options: options())
    }

    // Every proxy also lands in <folder>/<date>/<card>/; an older file of the same name stays, the copy gets _2.
    func testJobCopiesEveryProxyIntoTheFolder() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let extra = dir.sub("proxies")
        TestFS.writeRandom(extra + "/2026-10-05/A001/C0001.mov", size: 100, seed: 4)   // from an earlier run
        let job = try job(dir, extra: extra)
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertEqual(report.clips.count, 1)
        let clip = try XCTUnwrap(report.clips.first)
        XCTAssertEqual(clip.proxyStatus, .done)
        XCTAssertNil(clip.proxyNote)
        let backup = try XCTUnwrap(job.plan).cardPaths[0] + "/_PROXY/C0001.mov"
        XCTAssertEqual(data(backup), data(extra + "/2026-10-05/A001/C0001_2.mov"))
        XCTAssertEqual(data(extra + "/2026-10-05/A001/C0001.mov")?.count, 100)
        XCTAssertEqual(report.proxy?.extraFolder, extra)
    }

    // The folder went away mid-job (disconnected disk): a note, never a failed proxy or job.
    func testFolderGoneIsANoteOnly() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let extra = dir.path + "/unplugged"
        let job = try job(dir, extra: extra)
        var log: [String] = []
        let report = try XCTUnwrap(job.run { log.append($0) })
        let s = job.status()
        XCTAssertEqual(s.phase, .done)
        XCTAssertEqual(s.proxiesDone, 1)
        XCTAssertEqual(s.proxiesFailed, 0)
        let clip = try XCTUnwrap(report.clips.first)
        XCTAssertEqual(clip.proxyStatus, .done)
        XCTAssertEqual(clip.proxyNote, "kopie do \(extra) selhala: složka není dostupná")
        XCTAssertTrue(log.contains { $0.contains("C0001") && $0.contains(extra) }, "\(log)")
        XCTAssertFalse(FileManager.default.fileExists(atPath: extra))
    }
}
