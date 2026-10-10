import XCTest
@testable import OffloadKit

final class LutSettingsTests: XCTestCase {
    private func load(_ json: String, _ catalog: LutCatalog = TestMedia.luts) -> AppSettings {
        SettingsStore.decode(Data(json.utf8), catalog: catalog)
    }

    // Settings from before the built-in LUTs: a lutPath stays the user's own LUT, without one the choice is automatic.
    func testOldSettingsMigrate() {
        let old = load(#"{"proxy":{"enabled":true,"lutPath":"/Users/x/show.cube"}}"#)
        XCTAssertEqual(old.proxy.lutMode, .custom)
        XCTAssertEqual(old.proxy.lutPath, "/Users/x/show.cube")
        let fresh = load(#"{"proxy":{"enabled":true}}"#)
        XCTAssertEqual(fresh.proxy.lutMode, .auto)
        XCTAssertNil(fresh.proxy.lutFallback)
    }

    func testRoundTripAndUnknownBuiltinDropped() throws {
        var s = AppSettings()
        s.proxy.lutMode = .builtin
        s.proxy.lutBuiltin = "dji-dlog-dgamut"
        s.proxy.lutFallback = "sony-slog3-sgamut3cine"
        let data = try XCTUnwrap(SettingsStore.encode(s))
        XCTAssertTrue(String(decoding: data, as: UTF8.self).contains(#""lutMode":"builtin""#))
        let back = SettingsStore.decode(data, catalog: TestMedia.luts)
        XCTAssertEqual(back.proxy.lutMode, .builtin)
        XCTAssertEqual(back.proxy.lutBuiltin, "dji-dlog-dgamut")
        XCTAssertEqual(back.proxy.lutFallback, "sony-slog3-sgamut3cine")
        let bad = load(#"{"proxy":{"lutMode":"builtin","lutBuiltin":"nope","lutFallback":"nope"}}"#)
        XCTAssertNil(bad.proxy.lutBuiltin)
        XCTAssertNil(bad.proxy.lutFallback)
        XCTAssertEqual(bad.proxy.lutMode, .auto)
        XCTAssertEqual(load(#"{"proxy":{"lutMode":"none"}}"#).proxy.lutMode, .off)
        XCTAssertEqual(load(#"{"proxy":{"lutMode":"future"},"verify":false}"#).proxy.lutMode, .auto)   // unknown → as if absent
    }

    // An empty catalog (luts folder missing in a broken install) must not erase saved choices.
    func testEmptyCatalogKeepsBuiltinChoices() {
        let s = load(#"{"proxy":{"lutMode":"builtin","lutBuiltin":"dji-dlog-dgamut","lutFallback":"sony-slog3-sgamut3cine"}}"#,
                     LutCatalog.load(nil))
        XCTAssertEqual(s.proxy.lutMode, .builtin)
        XCTAssertEqual(s.proxy.lutBuiltin, "dji-dlog-dgamut")
        XCTAssertEqual(s.proxy.lutFallback, "sony-slog3-sgamut3cine")
    }

    // Built-in LUTs ship with the app: only the user's own .cube can be missing and block the start.
    func testMissingLutBlocksOnlyInCustomMode() {
        var p = ProxySettings()
        p.enabled = true
        p.lutMode = .auto
        p.lutPath = "/gone.cube"
        XCTAssertNil(ProxyPreflight.lutInUse(p, analyzeClips: true))
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: false))
        p.lutMode = .custom
        XCTAssertEqual(ProxyPreflight.lutInUse(p, analyzeClips: true), "/gone.cube")
        XCTAssertEqual(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: false), "LUT not found: gone.cube — choose another one or remove it.")
    }
}
