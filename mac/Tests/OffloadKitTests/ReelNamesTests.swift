import XCTest
@testable import OffloadKit

final class ReelNamesTests: XCTestCase {
    func testPatterns() {
        XCTAssertEqual(ReelNames.fromClipName("A001C003_210101_R1AB"), "A001")
        XCTAssertEqual(ReelNames.fromClipName("a001_c003_0101ab_001"), "A001")
        XCTAssertEqual(ReelNames.fromClipName("A001_08251530_C001"), "A001")
        XCTAssertEqual(ReelNames.match("B002C0012")?.prefix, "B002C0012")
        for no in ["C0001", "A01C003", "AA001C003", "clip"] { XCTAssertNil(ReelNames.fromClipName(no), no) }
    }

    func clip(_ name: String, proxy: Bool = false) -> Clip {
        Clip(name: name, path: "CLIP/\(name).mov", format: .video, segments: ["CLIP/\(name).mov"], bytes: 1, isCameraProxy: proxy)
    }

    func testSuggestionFromClips() {
        let one = ReelSuggestion.fromClips([clip("A001C001"), clip("A001C002"), clip("B007C001", proxy: true)])
        XCTAssertEqual(one.suggestion, NameSuggestion(name: "A001", hint: "from the clip names (A001C001…)"))
        let two = ReelSuggestion.fromClips([clip("A001C001"), clip("A002C001")])
        XCTAssertNil(two.suggestion)
        XCTAssertEqual(two.note, "The card has more than one reel: A001, A002")
        XCTAssertNil(ReelSuggestion.fromClips([clip("C0001")]).suggestion)
    }

    func testNextInLine() {
        func e(_ name: String, _ date: String, _ project: String? = nil) -> HistoryEntry {
            var x = HistoryEntry(id: name, source: "", cardName: name, date: date, finished: Date())
            x.project = project
            return x
        }
        let h = [e("A001", "2026-10-08"), e("A002", "2026-10-08"), e("A009", "2026-10-07"), e("B001", "2026-10-08", "Film X")]
        XCTAssertEqual(ReelSuggestion.next(h, date: "2026-10-08", project: nil, today: "2026-10-08", taken: []),
                       NameSuggestion(name: "A003", hint: "next after A002 (today)"))
        XCTAssertEqual(ReelSuggestion.next(h, date: "2026-10-08", project: nil, today: "2026-10-08", taken: ["a003"])?.name, "A004")
        XCTAssertEqual(ReelSuggestion.next(h, date: "2026-10-08", project: "film x", today: "2026-10-09", taken: [])?.hint, "next after B001 (2026-10-08)")
        XCTAssertNil(ReelSuggestion.next(h + [e("C001", "2026-10-08")], date: "2026-10-08", project: nil, today: "2026-10-08", taken: []), "two letters")
        XCTAssertNil(ReelSuggestion.next([e("A999", "2026-10-08")], date: "2026-10-08", project: nil, today: "2026-10-08"))
    }
}
