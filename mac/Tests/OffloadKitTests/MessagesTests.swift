import XCTest
@testable import OffloadKit

final class MessagesTests: XCTestCase {
    func testBytesCs() {
        XCTAssertEqual(formatBytesCs(12), "12 B")
        XCTAssertEqual(formatBytesCs(3_200_000_000), "3,2 GB")
        XCTAssertEqual(formatBytesCs(2_000_000_000), "2 GB")
        XCTAssertEqual(formatBytesCs(412_000_000_000), "412 GB")
        XCTAssertEqual(formatBytesCs(850_000_000), "850 MB")
    }

    func testPreflightTextsAreCzechWithCodes() throws {
        let dir = tempDir()
        let e = OffloadEngine()
        do {
            // The plan's dir.sub("missing") would create that folder here (TempDir.sub creates): a path that is not there.
            _ = try e.prepare(source: dir.path + "/missing", destRoots: [dir.path], cardName: nil, date: nil)
            XCTFail("must refuse")
        } catch let err as OffloadError {
            XCTAssertEqual(err.code, .sourceNotFound)
            XCTAssertEqual(err.description, "Zdroj nenalezen: \(dir.path)/missing")
        }
    }

    func testNoEnglishWordsInPreflightMessages() {
        let all = [Msg.chunkSize(1), Msg.bufferCount(1), Msg.date("x"), Msg.sourceNotFound("/a"), Msg.noDestination,
                   Msg.duplicateDestination("/a"), Msg.sourceIsOffload, Msg.sourceUnreadable("/a"), Msg.destinationNotFound("/a"),
                   Msg.destinationNotWritable("/a"), Msg.destinationUnreadable("/a"), Msg.sameVolumeAsSource("/a"),
                   Msg.sharedVolume("/a"), Msg.emptySource, Msg.noHashRecord("/a"), Msg.noFilesToRead("/a")]
        for m in all {
            for word in ["not found", "cannot", "must", "Destination", "Source"] {
                XCTAssertFalse(m.contains(word), "\(m) contains \(word)")
            }
        }
    }

    func testMacPathsUseSlash() {
        XCTAssertEqual(Msg.appendNoReport("/V/D/2026-10-06/A001"),
                       "V /V/D/2026-10-06/A001 chybí report dřívější zálohy (_REPORT/offload_report.json) — doplnit nejde.")
        XCTAssertEqual(Msg.appendNoMhl("/x"), "V /x chybí ASC MHL (ascmhl/ascmhl_chain.xml) — doplnit nejde.")
    }

    func testPluralsAsWindows() {
        XCTAssertEqual(Msg.unverifiedEarlier(1), "1 soubor z dřívější zálohy nebyl ověřen — doporučeno Ověřit zálohu.")
        XCTAssertEqual(Msg.unverifiedEarlier(3), "3 soubory z dřívější zálohy nebyly ověřeny — doporučeno Ověřit zálohu.")
        XCTAssertEqual(Msg.cardFilesNotInBackup(5),
                       "Na kartě je 5 souborů, které záloha nemá — kartu nevysouvejte, zálohu doplňte.")
    }
}
