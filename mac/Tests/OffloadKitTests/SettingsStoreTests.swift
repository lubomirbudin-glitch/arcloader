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
        s.proxy.lutMode = .custom   // what normalize derives from an old lutPath (task 21)
        XCTAssertEqual(SettingsStore.decode(SettingsStore.encode(s)), s)
    }

    func testMissingLutBlocksWhenProxyOrAnalysisUseIt() {
        var p = ProxySettings()
        p.lutPath = "/nope/look.cube"
        XCTAssertEqual(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: false),
                       "LUT not found: look.cube — choose another one or remove it.")
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: false, lutExists: false))   // nothing uses it
        p.enabled = true
        XCTAssertNotNil(ProxyPreflight.blocking(p, analyzeClips: false, lutExists: false))
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: nil))      // not checked yet
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: true))
    }

    func testMissingFFmpegOnlyWarns() {
        XCTAssertEqual(ProxyPreflight.warning(ProxySettings(), analyzeClips: true, resolved: ExternalTools()),
                       "ffmpeg/ffprobe not found — thumbnails and proxies will be skipped, copy and verification will run.")
        XCTAssertNil(ProxyPreflight.warning(ProxySettings(), analyzeClips: false, resolved: ExternalTools()))
        XCTAssertNil(ProxyPreflight.warning(ProxySettings(), analyzeClips: true, resolved: ExternalTools(ffmpeg: "/f", ffprobe: "/p")))
    }

    func testUnknownKeysSurviveASave() throws {
        let json = """
        {"destRoots":["/Volumes/A"],"verify":true,"futureTop":{"x":1},
         "proxy":{"enabled":true,"height":1080,"codec":"h264","bitrateMbps":10,"futureProxy":"keep"},
         "projects":[{"name":"Film X","destRoots":["/Volumes/B"]}],"activeProject":"Film X",
         "hash":"sha256","outputs":{"dayReport":false,"futureOut":2}}
        """
        var s = SettingsStore.decode(Data(json.utf8))
        XCTAssertEqual(s.hash, "sha256")
        XCTAssertEqual(HashKinds.fromSetting(s.hash), .xxh64)
        XCTAssertFalse(s.outputs.dayReport)
        s.verify = false
        let saved = try XCTUnwrap(JSONSerialization.jsonObject(with: XCTUnwrap(SettingsStore.encode(s))) as? [String: Any])
        XCTAssertNotNil(saved["futureTop"])
        XCTAssertNotNil(saved["projects"], "a newer version's projects stay")
        XCTAssertEqual(saved["activeProject"] as? String, "Film X")
        XCTAssertEqual((saved["proxy"] as? [String: Any])?["futureProxy"] as? String, "keep")
        XCTAssertEqual((saved["outputs"] as? [String: Any])?["futureOut"] as? Int, 2)
        XCTAssertEqual(saved["verify"] as? Bool, false)
        XCTAssertEqual(saved["hash"] as? String, "sha256", "an unknown hash value is kept as it was")
    }

    func testUnreadableSectionIsKeptUnchanged() throws {
        let json = #"{"destRoots":["/Volumes/A"],"proxy":"from a future version"}"#
        let s = SettingsStore.decode(Data(json.utf8))
        XCTAssertEqual(s.destRoots, ["/Volumes/A"])
        XCTAssertTrue(s.unreadable.contains("proxy"))
        let saved = try XCTUnwrap(JSONSerialization.jsonObject(with: XCTUnwrap(SettingsStore.encode(s))) as? [String: Any])
        XCTAssertEqual(saved["proxy"] as? String, "from a future version")
    }

    func testDefaultsOfNewKeys() {
        let s = SettingsStore.decode(nil)
        XCTAssertTrue(s.naming.suggestFromClips)
        XCTAssertEqual(s.hash, "xxh64")
        XCTAssertEqual(s.outputs, OutputSettings())
    }

    /// Merging over the last read JSON must not bring back a value the user cleared (an optional the encoder leaves out).
    func testClearedOptionalIsNotRevivedFromTheLastRead() throws {
        let json = #"{"proxy":{"lutPath":"/Users/x/look.cube","lutMode":"custom","futureProxy":1},"tools":{"ffmpeg":"/opt/ffmpeg"}}"#
        var s = SettingsStore.decode(Data(json.utf8))
        XCTAssertEqual(s.proxy.lutPath, "/Users/x/look.cube")
        s.proxy.lutPath = nil
        s.proxy.lutMode = .auto
        s.tools.ffmpeg = nil
        s.portNames = ["a": "x"]
        let saved = try XCTUnwrap(JSONSerialization.jsonObject(with: XCTUnwrap(SettingsStore.encode(s))) as? [String: Any])
        let proxy = try XCTUnwrap(saved["proxy"] as? [String: Any])
        XCTAssertNil(proxy["lutPath"], "a cleared LUT path stays cleared")
        XCTAssertEqual(proxy["futureProxy"] as? Int, 1)
        XCTAssertNil((saved["tools"] as? [String: Any])?["ffmpeg"], "a tool path set back to automatic stays automatic")
        var reread = SettingsStore.decode(SettingsStore.encode(s))
        XCTAssertEqual(reread.portNames, ["a": "x"])
        reread.portNames = [:]
        let again = try XCTUnwrap(JSONSerialization.jsonObject(with: XCTUnwrap(SettingsStore.encode(reread))) as? [String: Any])
        XCTAssertEqual((again["portNames"] as? [String: Any])?.count, 0, "a removed port name is not merged back")
    }
}
