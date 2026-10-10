import XCTest
@testable import OffloadKit

final class MessagesTests: XCTestCase {
    func testBytesShort() {
        XCTAssertEqual(formatBytesShort(12), "12 B")
        XCTAssertEqual(formatBytesShort(3_200_000_000), "3.2 GB")
        XCTAssertEqual(formatBytesShort(2_000_000_000), "2 GB")
        XCTAssertEqual(formatBytesShort(412_000_000_000), "412 GB")
        XCTAssertEqual(formatBytesShort(850_000_000), "850 MB")
    }

    func testPreflightTextsAreEnglishWithCodes() throws {
        let dir = tempDir()
        let e = OffloadEngine()
        do {
            // The plan's dir.sub("missing") would create that folder here (TempDir.sub creates): a path that is not there.
            _ = try e.prepare(source: dir.path + "/missing", destRoots: [dir.path], cardName: nil, date: nil)
            XCTFail("must refuse")
        } catch let err as OffloadError {
            XCTAssertEqual(err.code, .sourceNotFound)
            XCTAssertEqual(err.description, "Source not found: \(dir.path)/missing")
        }
    }

    func testNoCzechInPreflightMessages() {
        let all = [Msg.chunkSize(1), Msg.bufferCount(1), Msg.date("x"), Msg.sourceNotFound("/a"), Msg.noDestination,
                   Msg.duplicateDestination("/a"), Msg.sourceIsOffload, Msg.sourceUnreadable("/a"), Msg.destinationNotFound("/a"),
                   Msg.destinationNotWritable("/a"), Msg.destinationUnreadable("/a"), Msg.sameVolumeAsSource("/a"),
                   Msg.sharedVolume("/a"), Msg.emptySource, Msg.noHashRecord("/a"), Msg.noFilesToRead("/a")]
        let czech = CharacterSet(charactersIn: "áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ")
        for m in all {
            XCTAssertNil(m.rangeOfCharacter(from: czech), "\(m) has Czech letters")
            for word in ["Cíl", "Zdroj", "nejde", "není", "karta"] {
                XCTAssertFalse(m.contains(word), "\(m) contains \(word)")
            }
        }
    }

    func testMacPathsUseSlash() {
        XCTAssertEqual(Msg.appendNoReport("/V/D/2026-10-06/A001"),
                       "/V/D/2026-10-06/A001 has no report of the earlier backup (_REPORT/offload_report.json) — cannot append.")
        XCTAssertEqual(Msg.appendNoMhl("/x"), "/x has no ASC MHL (ascmhl/ascmhl_chain.xml) — cannot append.")
    }

    func testEnglishPlurals() {
        XCTAssertEqual(Msg.unverifiedEarlier(1), "1 file of the earlier backup was not verified — Verify backup recommended.")
        XCTAssertEqual(Msg.unverifiedEarlier(3), "3 files of the earlier backup were not verified — Verify backup recommended.")
        XCTAssertEqual(Msg.cardFilesNotInBackup(5),
                       "The card has 5 files that the backup does not — do not eject the card, append to the backup.")
        XCTAssertEqual(Msg.cardFilesNotInBackup(1),
                       "The card has 1 file that the backup does not — do not eject the card, append to the backup.")
        XCTAssertEqual(Msg.backupStillFailed(1), "1 file in the backup still does not match — do not eject the card, run Copy again.")
        XCTAssertEqual(Msg.backupStillFailed(2), "2 files in the backup still do not match — do not eject the card, run Copy again.")
        XCTAssertEqual(Msg.forRepair(Msg.appendNoMhl("/x")), "/x has no ASC MHL (ascmhl/ascmhl_chain.xml) — cannot repair.")
        XCTAssertTrue(Msg.forRepair(Msg.appendDifferentFolders(["/a", "/b"])).hasSuffix("— can repair only into folders with the same date and card name."))
    }
}
