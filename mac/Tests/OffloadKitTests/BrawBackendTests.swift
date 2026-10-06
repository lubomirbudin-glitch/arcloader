import XCTest
@testable import OffloadKit

final class BrawBackendTests: XCTestCase {
    func testInfoGammaGamutGenReachClipInfo() throws {
        let c = BRAWBackend.clipInfo(try BRAWBackend.parseInfo(
            #"{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976000,"frames":240,"timecode":"01:02:03:04","camera":"URSA Mini Pro 12K","gamma":"Blackmagic Design Film","gamut":"Blackmagic Design","colorScienceGen":5}"#))
        XCTAssertEqual(c.gamma, "Blackmagic Design Film")
        XCTAssertEqual(c.gamut, "Blackmagic Design")
        XCTAssertEqual(c.colorScienceGen, 5)
        XCTAssertEqual(c.width, 11520)
        XCTAssertEqual(c.frameRate, "24000/1001")
        XCTAssertEqual(c.camera, "URSA Mini Pro 12K")
        // braw-decode before task 27 prints no gamma: still parses.
        let old = BRAWBackend.clipInfo(try BRAWBackend.parseInfo(
            #"{"width":1,"height":1,"sourceWidth":1,"sourceHeight":1,"fps":25,"frames":1,"timecode":"","camera":""}"#))
        XCTAssertNil(old.gamma)
        XCTAssertNil(old.gamut)
        XCTAssertNil(old.colorScienceGen)
        // An empty string is "not reported" (as Windows ToClipInfo).
        let empty = BRAWBackend.clipInfo(try BRAWBackend.parseInfo(
            #"{"width":1,"height":1,"sourceWidth":1,"sourceHeight":1,"fps":25,"frames":1,"timecode":"","camera":"","gamma":"","gamut":""}"#))
        XCTAssertNil(empty.gamma)
        XCTAssertNil(empty.gamut)
    }

    // FORMATY: clips[].info.gamma / gamut / colorScienceGen — written only when known, the same keys as Windows.
    func testClipInfoJSONKeys() throws {
        var c = ClipInfo()
        c.gamma = "Blackmagic Design Film"
        c.gamut = "Blackmagic Design"
        c.colorScienceGen = 5
        let json = try JSONSerialization.jsonObject(with: JSONEncoder().encode(c)) as? [String: Any] ?? [:]
        XCTAssertEqual(json["gamma"] as? String, "Blackmagic Design Film")
        XCTAssertEqual(json["gamut"] as? String, "Blackmagic Design")
        XCTAssertEqual(json["colorScienceGen"] as? Int, 5)
        let none = try JSONSerialization.jsonObject(with: JSONEncoder().encode(ClipInfo())) as? [String: Any] ?? [:]
        XCTAssertNil(none["gamma"])
        XCTAssertNil(none["gamut"])
        XCTAssertNil(none["colorScienceGen"])
        XCTAssertEqual(try JSONDecoder().decode(ClipInfo.self, from: JSONEncoder().encode(c)).colorScienceGen, 5)
    }

    private let lib = "/BM/BlackmagicRawAPI.framework"

    // Windows task 1: braw-decode alone is not enough — without the library BRAW is copied and verified only.
    func testAvailableOnlyWithDecoderAndLibrary() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang).path
        var found: String?
        var searched: String?
        let backend = BRAWBackend(findLibrary: { searched = $0; return found })
        XCTAssertFalse(backend.isAvailable(ExternalTools()))
        XCTAssertNil(searched)   // no decoder: the library is not even looked for
        XCTAssertFalse(backend.isAvailable(ExternalTools(brawDecoder: exe)))
        XCTAssertEqual(searched, BRAWBackend.decoderDir(exe))
        XCTAssertTrue(searched?.hasSuffix("/fake-braw") == true, searched ?? "nil")
        found = lib
        XCTAssertTrue(backend.isAvailable(ExternalTools(brawDecoder: exe)))
    }

    func testDefaultSearchFindsTheFrameworkNextToTheDecoder() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang).path
        dir.sub("fake-braw", "BlackmagicRawAPI.framework")
        XCTAssertTrue(BRAWBackend().isAvailable(ExternalTools(brawDecoder: exe)))
    }

    func testWithoutTheLibraryTheClipIsUnsupportedWithTheWindowsHint() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang).path
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools(ffmpeg: "/bin/cat", brawDecoder: exe))
        r.backends[.braw] = BRAWBackend(findLibrary: { _ in nil })
        XCTAssertEqual(r.unsupportedReason(BrawTest.brawClip),
                       "Blackmagic RAW: chybí braw-decode nebo knihovna Blackmagic RAW (SDK, Player nebo DaVinci Resolve)")
    }

    // Windows final fix I1: exit codes 3/4/5 become a Czech note at the clip; the decoder's own line stays inside.
    func testInfoDecoderExitCodeBecomesCzechNote() {
        let cases: [(code: Int, line: String, note: String)] = [
            (3, "braw-decode: CreateCodec failed (0x80004005; library /BM/BlackmagicRawAPI.framework)",
             "Blackmagic RAW: knihovnu /BM/BlackmagicRawAPI.framework nelze použít — nainstalujte aktuální Blackmagic RAW Player"),
            (4, "braw-decode: cannot open /card/A001.braw (0x80004005; library /BM/BlackmagicRawAPI.framework)",
             "Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna /BM/BlackmagicRawAPI.framework starší než kamera (nainstalujte aktuální Blackmagic RAW Player)"),
            (5, "braw-decode: frame 0 failed", "Blackmagic RAW: snímek klipu se nepodařilo dekódovat (frame 0 failed)"),
        ]
        for c in cases {
            let dir = tempDir()
            let exe = BrawTest.fakeDecoder(dir, info: BrawTest.fail(c.code, c.line), frames: BrawTest.fail(c.code, c.line)).path
            XCTAssertThrowsError(try BRAWBackend(findLibrary: { _ in self.lib }).probe(dir.path + "/A001.braw",
                                                                                     tools: ExternalTools(brawDecoder: exe))) { e in
                guard let d = e as? RawDecoderError else { return XCTFail("\(c.code): \(e)") }
                XCTAssertEqual("\(d)", c.note)
                XCTAssertEqual(d.decoderError.status, Int32(c.code))
                XCTAssertEqual(d.decoderError.message.trimmingCharacters(in: .whitespacesAndNewlines), c.line)
            }
        }
    }

    func testOtherExitCodesStayAsTheyAre() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.fail(2, "usage: braw-decode"), frames: BrawTest.hang).path
        XCTAssertThrowsError(try BRAWBackend(findLibrary: { _ in self.lib }).probe(dir.path + "/A001.braw",
                                                                                 tools: ExternalTools(brawDecoder: exe))) { e in
            XCTAssertTrue(e is ToolError, "\(e)")
            XCTAssertEqual("\(e)", "braw-decode failed (exit 2): usage: braw-decode")
        }
    }

    /// The library vanished after the check: the note names the framework by its file name.
    func testNoteWithoutLibraryPathNamesTheFramework() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.fail(3, "braw-decode: BlackmagicRawAPI.framework not found"),
                                       frames: BrawTest.hang).path
        XCTAssertThrowsError(try BRAWBackend(findLibrary: { _ in nil }).probe(dir.path + "/A001.braw",
                                                                            tools: ExternalTools(brawDecoder: exe))) { e in
            XCTAssertEqual("\(e)", "Blackmagic RAW: knihovnu BlackmagicRawAPI.framework nelze použít — nainstalujte aktuální Blackmagic RAW Player")
        }
    }

    // Review focus 11 (Windows ruling M3): `braw-decode --info` runs with a timeout — a hung SDK gives the clip a note.
    func testInfoHungKilledAfterTheTimeout() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.hang, frames: BrawTest.hang).path
        let t0 = Date()
        XCTAssertThrowsError(try BRAWBackend(findLibrary: { _ in self.lib }, infoTimeout: 2)
            .probe(dir.path + "/A001.braw", tools: ExternalTools(brawDecoder: exe))) { e in
            XCTAssertEqual("\(e)", "braw-decode failed (exit -1): neodpověděl do 2 s — ukončen")
        }
        XCTAssertTrue((1.9...30).contains(Date().timeIntervalSince(t0)), "\(Date().timeIntervalSince(t0))")
    }

    func testInfoDefaultTimeoutIs60s() {
        XCTAssertEqual(BRAWBackend.defaultInfoTimeout, 60)
        XCTAssertEqual(BRAWBackend().infoTimeout, 60)
    }

    // The note goes to the clip, the decoder's own words to the job's log (Windows ProxyRenderer.Logged).
    func testProbeDecoderExitCodeOriginalLogged() {
        let dir = tempDir()
        let line = "braw-decode: cannot open /card/A001.braw (0x80004005; library /BM/BlackmagicRawAPI.framework)"
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.fail(4, line), frames: BrawTest.hang).path
        let log = LogLines()
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools(ffmpeg: "/bin/cat", brawDecoder: exe))
        r.backends[.braw] = BRAWBackend(findLibrary: { _ in self.lib })
        r.log = { log.add($0) }
        XCTAssertThrowsError(try r.probe(BrawTest.brawClip, cardPath: dir.sub("card"))) { e in
            XCTAssertTrue("\(e)".hasPrefix("Blackmagic RAW: klip nelze otevřít"), "\(e)")
        }
        XCTAssertTrue(log.lines.contains("braw-decode failed (exit 4): \(line)"), "\(log.lines)")
    }
}
