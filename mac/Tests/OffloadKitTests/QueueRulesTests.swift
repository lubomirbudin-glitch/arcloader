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
        XCTAssertEqual(QueueRules.ejectProblem(copied: "uuid:AB12", current: nil, cardInUse: false), "karta není připojená")
        XCTAssertEqual(QueueRules.otherCardText, "karta v mechanice není ta, která se kopírovala")
    }

    func testEjectProblemCardStillNeededByAnotherJobRefused() {
        XCTAssertEqual(QueueRules.ejectProblem(copied: "uuid:AB12", current: "uuid:AB12", cardInUse: true), QueueRules.inUseText)
        XCTAssertEqual(QueueRules.inUseText, "z karty ještě kopíruje jiná úloha — vysuňte ji, až její kopie doběhne")
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
        XCTAssertEqual(p(exists: false), "karta není připojená")
        XCTAssertEqual(p(local: false), "síťová složka se nevysouvá")
        XCTAssertEqual(p(root: false), "zdroj je složka, ne celá karta")
        XCTAssertEqual(p(rootFS: true), "systémový disk nelze vysunout")
        XCTAssertEqual(p(internal: true, ejectable: false, removable: false), "interní disk nelze vysunout")
        XCTAssertEqual(p(internal: nil, ejectable: nil, removable: nil), "interní disk nelze vysunout")
        XCTAssertNil(p(internal: true, ejectable: true, removable: true))     // built-in SD slot
        XCTAssertNil(p(internal: false, ejectable: false, removable: false))  // USB / Thunderbolt disk
    }

    func testCardEjectProblemFromTheDisk() {
        let dir = tempDir()
        XCTAssertEqual(CardEject.problem(source: dir.path), "zdroj je složka, ne celá karta")
        XCTAssertEqual(CardEject.problem(source: "/"), "systémový disk nelze vysunout")
        XCTAssertEqual(CardEject.problem(source: dir.path + "/missing"), "karta není připojená")
        let r = CardEject.eject(dir.path)
        XCTAssertFalse(r.ok)
        XCTAssertEqual(r.error, "zdroj je složka, ne celá karta")
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
