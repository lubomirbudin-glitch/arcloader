import XCTest
@testable import OffloadKit

final class PreflightTests: XCTestCase {
    /// Temp folders share one volume: tests allow what a real card + disks never need.
    private func options(allowSameDevice: Bool = true, allowSourceVolume: Bool = true) -> OffloadOptions {
        var o = OffloadOptions()
        o.allowSameDevice = allowSameDevice
        o.allowSourceVolume = allowSourceVolume
        return o
    }

    private func card(_ dir: TempDir) -> String {
        let card = dir.sub("A001")
        TestFS.writeRandom(card + "/CLIP/a.mov", size: 1000, seed: 1)
        return card
    }

    private func assertPreflight(_ message: String, options o: OffloadOptions? = nil,
                                 file: StaticString = #filePath, line: UInt = #line,
                                 _ act: (OffloadEngine) throws -> Void) {
        do {
            try act(OffloadEngine(options: o ?? options()))
            XCTFail("expected a preflight error containing \"\(message)\"", file: file, line: line)
        } catch let e as OffloadError {
            XCTAssertTrue(e.isPreflight, "\(e)", file: file, line: line)
            XCTAssertTrue(e.description.contains(message), "\(e)", file: file, line: line)
        } catch {
            XCTFail("unexpected error \(error)", file: file, line: line)
        }
    }

    func testPlanHasScanDateAndCardFolder() throws {
        let dir = tempDir()
        let card = card(dir)
        let d1 = dir.sub("d1")
        let plan = try OffloadEngine(options: options()).prepare(source: card, destRoots: [d1], cardName: nil, date: "2026-10-04")
        XCTAssertEqual(plan.cardName, "A001")
        XCTAssertEqual(plan.date, "2026-10-04")
        XCTAssertEqual(plan.cardPaths, [d1 + "/2026-10-04/A001"])
        XCTAssertEqual(plan.scan.files.count, 1)
    }

    func testCardNameIsSanitized() throws {
        let dir = tempDir()
        let plan = try OffloadEngine(options: options()).prepare(source: card(dir), destRoots: [dir.sub("d1")],
                                                                 cardName: "Den 1: hlavní", date: "2026-10-04")
        XCTAssertEqual(plan.cardName, "Den 1_ hlavní")
    }

    func testDestinationOnSourceVolumeIsRejected() {
        let dir = tempDir()
        assertPreflight("same volume as the source", options: options(allowSourceVolume: false)) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
    }

    func testTwoDestinationsOnOneVolumeNeedAllowSameDevice() {
        let dir = tempDir()
        assertPreflight("--allow-same-device", options: options(allowSameDevice: false)) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1"), dir.sub("d2")], cardName: nil, date: nil)
        }
    }

    func testSourceThatIsAnEarlierOffloadIsRejected() {
        for folder in ["_REPORT", "_proxy", "ascmhl"] {
            let dir = tempDir()
            let card = card(dir)
            dir.sub("A001", folder)
            assertPreflight("Source looks like an earlier offload") {
                _ = try $0.prepare(source: card, destRoots: [dir.sub("d1")], cardName: nil, date: nil)
            }
        }
    }

    func testAFileNamedLikeAMarkerIsFine() throws {
        let dir = tempDir()
        let card = card(dir)
        TestFS.writeRandom(card + "/_REPORT", size: 10, seed: 2)   // a file, not a folder
        _ = try OffloadEngine(options: options()).prepare(source: card, destRoots: [dir.sub("d1")], cardName: nil, date: nil)
    }

    func testDuplicateDestinationIsRejectedEvenWithAllowSameDevice() {
        let dir = tempDir()
        let d1 = dir.sub("d1")
        assertPreflight("Destination given twice") {
            _ = try $0.prepare(source: self.card(dir), destRoots: [d1, d1 + "/"], cardName: nil, date: nil)
        }
        assertPreflight("Destination given twice") {
            _ = try $0.prepare(source: self.card(dir), destRoots: [d1, dir.path + "/./d1"], cardName: nil, date: nil)
        }
    }

    func testNormalizedRootIgnoresCaseAndTrailingSlash() {
        XCTAssertEqual(OffloadEngine.normalizedRoot("/Volumes/RAID_A/"), OffloadEngine.normalizedRoot("/volumes/raid_a"))
        XCTAssertNotEqual(OffloadEngine.normalizedRoot("/Volumes/RAID_A"), OffloadEngine.normalizedRoot("/Volumes/RAID_B"))
        XCTAssertEqual(OffloadEngine.normalizedRoot("/"), "/")
    }

    func testBadChunkSizeIsRejected() {
        for chunk in [0, -4096, 1000, 4096 + 1, (1 << 30) + 4096] {
            let dir = tempDir()
            var o = options()
            o.chunkSize = chunk
            assertPreflight("ChunkSize", options: o) {
                _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
            }
        }
    }

    func testBufferCountBelowTwoIsRejected() {
        let dir = tempDir()
        var o = options()
        o.bufferCount = 1
        assertPreflight("BufferCount", options: o) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
    }

    func testBadDateIsRejected() {
        for date in ["2026-1-4", "../evil", "2026-10-04\n", "2026-10-04x"] {
            let dir = tempDir()
            assertPreflight("Date must be") {
                _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: date)
            }
        }
    }

    func testMissingSourceNoDestinationMissingDestinationEmptySource() {
        let dir = tempDir()
        assertPreflight("Source not found") {
            _ = try $0.prepare(source: dir.path + "/no-such-card", destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
        assertPreflight("No destination given") {
            _ = try $0.prepare(source: self.card(dir), destRoots: [], cardName: nil, date: nil)
        }
        assertPreflight("Destination not found") {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.path + "/nope"], cardName: nil, date: nil)
        }
        assertPreflight("Source contains no files") {
            _ = try $0.prepare(source: dir.sub("empty"), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
    }
}
