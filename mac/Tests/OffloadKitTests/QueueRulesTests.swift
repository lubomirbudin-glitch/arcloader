import XCTest
@testable import OffloadKit

final class QueueRulesTests: XCTestCase {
    func testEjectProblemSameCardNotInUseAllowed() {
        XCTAssertNil(QueueRules.ejectProblem(copied: "uuid:AB12", current: "UUID:ab12", cardInUse: false))
    }

    // Review focus 2: another card in the slot, or the card gone → never eject.
    func testEjectProblemOtherCardOrGoneRefused() {
        XCTAssertEqual(QueueRules.ejectProblem(copied: "uuid:AB12", current: "uuid:CD34", cardInUse: false), QueueRules.otherCardText)
        XCTAssertEqual(QueueRules.ejectProblem(copied: nil, current: "uuid:CD34", cardInUse: false), QueueRules.otherCardText)
        XCTAssertEqual(QueueRules.ejectProblem(copied: "uuid:AB12", current: nil, cardInUse: false), "card is not connected")
        XCTAssertEqual(QueueRules.otherCardText, "the card in the reader is not the one that was copied")
    }

    func testEjectProblemCardStillNeededByAnotherJobRefused() {
        XCTAssertEqual(QueueRules.ejectProblem(copied: "uuid:AB12", current: "uuid:AB12", cardInUse: true), QueueRules.inUseText)
        XCTAssertEqual(QueueRules.inUseText, "another job is still copying from the card — eject it when that copy has finished")
    }

    func testCardInUseByPhaseOfAnotherJobOnTheSameVolume() {
        let cases: [(JobStatus.Phase, Bool)] = [
            (.waiting, true), (.copying, true), (.flushing, true), (.verifying, true), (.reporting, true),
            (.analyzing, false), (.proxies, false), (.done, false), (.failed, false), (.cancelled, false),
        ]
        for (phase, inUse) in cases {
            XCTAssertEqual(QueueRules.cardInUse("/Volumes/A001", others: [("/Volumes/A001/DCIM", phase)]), inUse, "\(phase)")
            XCTAssertEqual(QueueRules.cardInUse("/Volumes/A001/", others: [("/Volumes/A001", phase)]), inUse, "\(phase)")
            XCTAssertFalse(QueueRules.cardInUse("/Volumes/A001", others: [("/Volumes/A0012", phase)]))
            XCTAssertFalse(QueueRules.cardInUse("/Volumes/A001", others: [("/Volumes/B", phase)]))
        }
    }

    // Review focus 2: the button shows only while the scan shows the copied card in its slot.
    func testCardInDriveOnlyWhenTheScanShowsTheCopiedCard() {
        func item(_ identity: String?) -> VolumeItem {
            VolumeItem(name: "A001", path: "/Volumes/A001", total: 10, free: 5, format: "exfat", looksLikeCard: true,
                       isDestination: false, identity: identity)
        }
        XCTAssertTrue(QueueRules.cardInDrive("/Volumes/A001", copied: "uuid:AB12", scanned: [item("uuid:AB12")]))
        XCTAssertFalse(QueueRules.cardInDrive("/Volumes/A001", copied: "uuid:CD34", scanned: [item("uuid:AB12")]))   // another card
        XCTAssertFalse(QueueRules.cardInDrive("/Volumes/A001", copied: nil, scanned: [item("uuid:AB12")]))
        XCTAssertFalse(QueueRules.cardInDrive("/Volumes/B", copied: "uuid:AB12", scanned: [item("uuid:AB12")]))      // pulled out
        XCTAssertFalse(QueueRules.cardInDrive("/Volumes/A001", copied: "uuid:AB12", scanned: [item(nil)]))
    }

    func testCardIdentitySameVolumeSameMissingNil() {
        let dir = tempDir()
        let a = CardIdentity.of(dir.path)
        XCTAssertNotNil(a, "the startup volume reports a volume UUID")
        XCTAssertEqual(a, CardIdentity.of(dir.sub("x")))
        XCTAssertTrue(a?.hasPrefix("uuid:") == true)
        XCTAssertNil(CardIdentity.of("/Volumes/offload-no-such-volume-\(UUID().uuidString)"))
    }

    // Review focus 2: folders, network shares, the system volume and internal disks are never ejected.
    func testCardEjectProblemRules() {
        func p(exists: Bool = true, root: Bool = true, local: Bool = true, rootFS: Bool = false,
               internal: Bool? = false, ejectable: Bool? = true, removable: Bool? = true) -> String? {
            CardEject.problem(exists: exists, isVolumeRoot: root, isLocal: local, isRootFileSystem: rootFS,
                              isInternal: `internal`, isEjectable: ejectable, isRemovable: removable)
        }
        XCTAssertNil(p())
        XCTAssertEqual(p(exists: false), "card is not connected")
        XCTAssertEqual(p(local: false), "a network folder is not ejected")
        XCTAssertEqual(p(root: false), "the source is a folder, not a whole card")
        XCTAssertEqual(p(rootFS: true), "the system disk cannot be ejected")
        XCTAssertEqual(p(internal: true, ejectable: false, removable: false), "an internal disk cannot be ejected")
        XCTAssertEqual(p(internal: nil, ejectable: nil, removable: nil), "an internal disk cannot be ejected")
        XCTAssertNil(p(internal: true, ejectable: true, removable: true))     // built-in SD slot
        XCTAssertNil(p(internal: false, ejectable: false, removable: false))  // USB / Thunderbolt disk
    }

    func testCardEjectProblemFromTheDisk() {
        let dir = tempDir()
        XCTAssertEqual(CardEject.problem(source: dir.path), "the source is a folder, not a whole card")
        XCTAssertEqual(CardEject.problem(source: "/"), "the system disk cannot be ejected")
        XCTAssertEqual(CardEject.problem(source: dir.path + "/missing"), "card is not connected")
        let r = CardEject.eject(dir.path)
        XCTAssertFalse(r.ok)
        XCTAssertEqual(r.error, "the source is a folder, not a whole card")
    }

    func testMeasureBlockedWhileACopyWritesOrVerifies() {
        XCTAssertTrue(QueueRules.measureBlocked([.done, .copying]))
        XCTAssertTrue(QueueRules.measureBlocked([.flushing]))
        XCTAssertTrue(QueueRules.measureBlocked([.verifying]))
        XCTAssertFalse(QueueRules.measureBlocked([.waiting, .proxies, .done, .cancelled]))
        XCTAssertFalse(QueueRules.measureBlocked([]))
    }

    func testReportFirstExistingDestinationWins() throws {
        let dir = tempDir()
        let cards = [dir.sub("d1", "2026-10-05", "A001"), dir.sub("d2", "2026-10-05", "A001"), dir.sub("d3", "2026-10-05", "A001")]
        XCTAssertNil(QueueRules.firstReport(cardPaths: cards, cardName: "A001"))
        for c in cards.dropFirst() {
            try FileManager.default.createDirectory(atPath: c + "/_REPORT", withIntermediateDirectories: true)
            try "<html/>".write(toFile: QueueRules.reportPath(cardPath: c, cardName: "A001"), atomically: true, encoding: .utf8)
        }
        XCTAssertEqual(QueueRules.firstReport(cardPaths: cards, cardName: "A001"), cards[1] + "/_REPORT/A001_report.html")
    }
}
