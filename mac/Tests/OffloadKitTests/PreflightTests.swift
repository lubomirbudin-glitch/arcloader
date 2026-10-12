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

    /// The check is told by its code; the text is the Czech one of `Msg` (task 34).
    private func assertPreflight(_ code: PreflightCode, _ message: String, options o: OffloadOptions? = nil,
                                 file: StaticString = #filePath, line: UInt = #line,
                                 _ act: (OffloadEngine) throws -> Void) {
        do {
            try act(OffloadEngine(options: o ?? options()))
            XCTFail("expected a preflight error \(code) \"\(message)\"", file: file, line: line)
        } catch let e as OffloadError {
            XCTAssertTrue(e.isPreflight, "\(e)", file: file, line: line)
            XCTAssertEqual(e.code, code, "\(e)", file: file, line: line)
            XCTAssertEqual(e.description, message, file: file, line: line)
        } catch {
            XCTFail("unexpected error \(error)", file: file, line: line)
        }
    }

    // A card macOS refuses to list (privacy) stops with how to allow it — never as an empty card (field report 2026-10-11).
    func testUnlistableSourceSaysHowToAllowIt() throws {
        let dir = tempDir()
        let card = card(dir)
        chmod(card, 0)
        defer { chmod(card, 0o755) }
        let d1 = dir.sub("d1")
        assertPreflight(.noReadAccess, Msg.noReadAccess(card)) { e in
            _ = try e.prepare(source: card, destRoots: [d1], cardName: nil, date: "2026-10-04")
        }
        XCTAssertThrowsError(try Scanner.scan(card))
        XCTAssertTrue(Msg.noReadAccess(card).contains("Removable Volumes"))
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
        let d1 = dir.sub("d1")
        assertPreflight(.sameVolumeAsSource, Msg.sameVolumeAsSource(d1), options: options(allowSourceVolume: false)) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [d1], cardName: nil, date: nil)
        }
    }

    func testTwoDestinationsOnOneVolumeNeedAllowSameDevice() {
        let dir = tempDir()
        let d2 = dir.sub("d2")
        assertPreflight(.sharedVolume, Msg.sharedVolume(d2), options: options(allowSameDevice: false)) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1"), d2], cardName: nil, date: nil)
        }
    }

    func testSourceThatIsAnEarlierOffloadIsRejected() {
        for folder in ["_REPORT", "_proxy", "ascmhl"] {
            let dir = tempDir()
            let card = card(dir)
            dir.sub("A001", folder)
            assertPreflight(.sourceIsOffload, Msg.sourceIsOffload) {
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
        assertPreflight(.duplicateDestination, Msg.duplicateDestination(d1 + "/")) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [d1, d1 + "/"], cardName: nil, date: nil)
        }
        assertPreflight(.duplicateDestination, Msg.duplicateDestination(dir.path + "/./d1")) {
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
            assertPreflight(.chunkSize, Msg.chunkSize(chunk), options: o) {
                _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
            }
        }
    }

    func testBufferCountBelowTwoIsRejected() {
        let dir = tempDir()
        var o = options()
        o.bufferCount = 1
        assertPreflight(.bufferCount, Msg.bufferCount(1), options: o) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
    }

    func testBadDateIsRejected() {
        for date in ["2026-1-4", "../evil", "2026-10-04\n", "2026-10-04x"] {
            let dir = tempDir()
            assertPreflight(.date, Msg.date(date)) {
                _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: date)
            }
        }
    }

    func testMissingSourceNoDestinationMissingDestinationEmptySource() {
        let dir = tempDir()
        assertPreflight(.sourceNotFound, Msg.sourceNotFound(dir.path + "/no-such-card")) {
            _ = try $0.prepare(source: dir.path + "/no-such-card", destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
        assertPreflight(.noDestination, Msg.noDestination) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [], cardName: nil, date: nil)
        }
        assertPreflight(.destinationNotFound, Msg.destinationNotFound(dir.path + "/nope")) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.path + "/nope"], cardName: nil, date: nil)
        }
        assertPreflight(.emptySource, Msg.emptySource) {
            _ = try $0.prepare(source: dir.sub("empty"), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
    }
}
