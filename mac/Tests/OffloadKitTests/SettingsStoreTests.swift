import XCTest
@testable import OffloadKit

final class SettingsStoreTests: XCTestCase {
    private func load(_ json: String) -> AppSettings { SettingsStore.decode(Data(json.utf8)) }

    func testMissingOrCorruptGivesDefaults() {
        XCTAssertEqual(SettingsStore.decode(nil), AppSettings())
        XCTAssertEqual(load(#"{"verify":tru"#), AppSettings())
        XCTAssertEqual(load("[1,2,3]"), AppSettings())
        XCTAssertEqual(load("null"), AppSettings())
    }

    // Review focus 3: what version 0.1.0 stored (no portNames) keeps the destinations after the update.
    func testOldMacSettingsWithoutPortNamesKeepDestinations() throws {
        var old = AppSettings()
        old.destRoots = ["/Volumes/RAID_A", "/Volumes/SHUTTLE_B"]
        old.verify = false
        old.proxy.enabled = true
        old.proxy.codec = .proresLT
        var obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: try JSONEncoder().encode(old)) as? [String: Any])
        obj["portNames"] = nil
        let s = SettingsStore.decode(try JSONSerialization.data(withJSONObject: obj))
        XCTAssertEqual(s.destRoots, ["/Volumes/RAID_A", "/Volumes/SHUTTLE_B"])
        XCTAssertFalse(s.verify)
        XCTAssertTrue(s.proxy.enabled)
        XCTAssertEqual(s.proxy.codec, .proresLT)
        XCTAssertEqual(s.portNames, [:])
    }

    func testPartialAndUnknownKeys() {
        let s = load(#"{"destRoots":["/Volumes/A","/volumes/a"," "],"verify":false,"proxy":{"enabled":true,"codec":"proresLT"},"tools":{"ffmpeg":"/x/ffmpeg"},"futureKey":42}"#)
        XCTAssertEqual(s.destRoots, ["/Volumes/A"])
        XCTAssertFalse(s.verify)
        XCTAssertTrue(s.analyzeClips)
        XCTAssertTrue(s.proxy.enabled)
        XCTAssertEqual(s.proxy.codec, .proresLT)
        XCTAssertEqual(s.proxy.height, 1080)
        XCTAssertTrue(s.proxy.burnTimecode)
        XCTAssertEqual(s.tools.ffmpeg, "/x/ffmpeg")
    }

    // Review focus 3: one bad section never costs the others.
    func testBadSectionKeepsTheRest() {
        let s = load(#"{"destRoots":["/Volumes/Backup"],"proxy":{"codec":"av1"},"verify":"yes","portNames":{"A#1":"vpředu"}}"#)
        XCTAssertEqual(s.destRoots, ["/Volumes/Backup"])
        XCTAssertEqual(s.proxy.codec, .h264)
        XCTAssertTrue(s.verify)
        XCTAssertEqual(s.portNames["A#1"], "vpředu")
    }

    func testToolsOfAWrongTypeFallBackToDefaults() {
        let s = load(#"{"tools":{"ffmpeg":5},"verify":false}"#)
        XCTAssertEqual(s.tools, ExternalTools())
        XCTAssertFalse(s.verify)
    }

    func testDestRootsNonStringEntriesDropped() {
        XCTAssertEqual(load(#"{"destRoots":[null,"/A",5,{"x":1},"/B"]}"#).destRoots, ["/A", "/B"])
        XCTAssertEqual(load(#"{"destRoots":"/A"}"#).destRoots, [])
    }

    func testPortNamesTrimmedAndEmptyOrNonStringDropped() {
        let s = load(#"{"portNames":{"X#1":" přední ","X#2":null,"X#3":7,"":"a","X#4":"  "}}"#)
        XCTAssertEqual(s.portNames, ["X#1": "přední"])
    }

    func testOutOfRangeClamped() {
        let s = load(#"{"proxy":{"height":999,"bitrateMbps":500,"frameScale":0.1,"frameAspect":-1,"lutPath":""}}"#)
        XCTAssertEqual(s.proxy.height, 1080)
        XCTAssertEqual(s.proxy.bitrateMbps, 50)
        XCTAssertEqual(s.proxy.frameScale, 0.5)
        XCTAssertNil(s.proxy.frameAspect)
        XCTAssertNil(s.proxy.lutPath)
    }

    func testRoundTrip() {
        var s = AppSettings()
        s.destRoots = ["/Volumes/A"]
        s.portNames = ["loc:14100000#1": "přední levý"]
        s.proxy.lutPath = "/Users/x/look.cube"
        XCTAssertEqual(SettingsStore.decode(SettingsStore.encode(s)), s)
    }

    func testMissingLutBlocksWhenProxyOrAnalysisUseIt() {
        var p = ProxySettings()
        p.lutPath = "/nope/look.cube"
        XCTAssertEqual(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: false),
                       "LUT nenalezen: look.cube — vyberte jiný nebo ho odeberte.")
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: false, lutExists: false))   // nothing uses it
        p.enabled = true
        XCTAssertNotNil(ProxyPreflight.blocking(p, analyzeClips: false, lutExists: false))
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: nil))      // not checked yet
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: true))
    }

    func testMissingFFmpegOnlyWarns() {
        XCTAssertEqual(ProxyPreflight.warning(ProxySettings(), analyzeClips: true, resolved: ExternalTools()),
                       "ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou.")
        XCTAssertNil(ProxyPreflight.warning(ProxySettings(), analyzeClips: false, resolved: ExternalTools()))
        XCTAssertNil(ProxyPreflight.warning(ProxySettings(), analyzeClips: true, resolved: ExternalTools(ffmpeg: "/f", ffprobe: "/p")))
    }
}
