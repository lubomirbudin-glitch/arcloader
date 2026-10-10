import XCTest
@testable import OffloadKit

final class LutOptionTests: XCTestCase {
    private let cat = TestMedia.luts

    private func parsed(_ mode: LutMode, builtin: String? = nil, path: String? = nil, fallback: String? = nil) -> LutOption.Parsed {
        LutOption.Parsed(mode: mode, builtin: builtin, path: path, fallback: fallback, error: nil)
    }

    func testParse() {
        XCTAssertEqual(LutOption.parse(nil, fallback: nil, catalog: cat), parsed(.auto))
        XCTAssertEqual(LutOption.parse("AUTO", fallback: "dji-dlog-dgamut", catalog: cat), parsed(.auto, fallback: "dji-dlog-dgamut"))
        XCTAssertEqual(LutOption.parse("none", fallback: nil, catalog: cat), parsed(.off))
        XCTAssertEqual(LutOption.parse("sony-slog3-sgamut3", fallback: nil, catalog: cat), parsed(.builtin, builtin: "sony-slog3-sgamut3"))
        XCTAssertEqual(LutOption.parse("/Users/x/show.cube", fallback: nil, catalog: cat), parsed(.custom, path: "/Users/x/show.cube"))
        XCTAssertEqual(LutOption.parse("look.CUBE", fallback: nil, catalog: cat), parsed(.custom, path: "look.CUBE"))
        XCTAssertEqual(LutOption.parse("luts/x", fallback: nil, catalog: cat).mode, .custom)
        XCTAssertTrue(LutOption.parse("nope", fallback: nil, catalog: cat).error?.hasPrefix("Neznámý LUT „nope“. Použijte auto, none, cestu k .cube nebo jedno z: arri-logc3-awg3, ") == true)
        XCTAssertTrue(LutOption.parse("auto", fallback: "nope", catalog: cat).error?.hasPrefix("Neznámý záložní LUT „nope“. Vestavěné: ") == true)
        XCTAssertNil(LutOption.parse("auto", fallback: "none", catalog: cat).fallback)
        XCTAssertNil(LutOption.parse("auto", fallback: "None", catalog: cat).error)
    }
}
