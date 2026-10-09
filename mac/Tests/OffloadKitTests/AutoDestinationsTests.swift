@testable import OffloadKit
import XCTest

final class AutoDestinationsTests: XCTestCase {
    private func vol(_ name: String, card: Bool = false, writable: Bool = true, id: String? = nil) -> VolumeItem {
        VolumeItem(name: name, path: "/Volumes/" + name, total: 1, free: 1, format: "hfs", looksLikeCard: card,
                   isDestination: false, identity: id, isWritable: writable)
    }

    func testNewDriveJoinsWithTheSameSubfolderAndUnpluggedOnesLeave() {
        let found = [vol("TSOTB_3"), vol("TSOTB_4"), vol("A_0009_1F75", card: true, writable: false)]
        let next = Volumes.autoDestinations(current: ["/Volumes/TSOTB_1/3ND", "/Volumes/TSOTB_3/3ND", "/Users/x/test"],
                                            found: found, mounted: ["/", "/Volumes/TSOTB_3", "/Volumes/TSOTB_4", "/Volumes/A_0009_1F75"],
                                            ignored: [], sources: ["/Volumes/A_0009_1F75"])
        XCTAssertEqual(next, ["/Volumes/TSOTB_3/3ND", "/Users/x/test", "/Volumes/TSOTB_4/3ND"])
    }

    func testCardsReadOnlySourcesAndRemovedDrivesNeverJoin() {
        let found = [vol("CARD", card: true), vol("RO", writable: false), vol("SRC"), vol("GONE", id: "uuid:1"), vol("NAMED")]
        let next = Volumes.autoDestinations(current: [], found: found, mounted: Set(found.map(\.path)),
                                            ignored: ["uuid:1", "NAMED"], sources: ["/Volumes/SRC"])
        XCTAssertEqual(next, [])
    }

    func testNoCommonSubfolderMeansTheVolumeRoot() {
        let next = Volumes.autoDestinations(current: ["/Volumes/A/x", "/Volumes/B/y"], found: [vol("A"), vol("B"), vol("C")],
                                            mounted: ["/Volumes/A", "/Volumes/B", "/Volumes/C"], ignored: [], sources: [])
        XCTAssertEqual(next, ["/Volumes/A/x", "/Volumes/B/y", "/Volumes/C"])
    }
}
