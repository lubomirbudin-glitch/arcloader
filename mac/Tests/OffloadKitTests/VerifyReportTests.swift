import XCTest
@testable import OffloadKit

final class VerifyReportTests: XCTestCase {
    // Review focus 4.
    func testNothingWrittenIntoAscmhl() throws {
        let card = try BackupVerifierTests().offload(tempDir())
        let before = try TestFS.files(in: card + "/ascmhl").map { try Data(contentsOf: URL(fileURLWithPath: card + "/ascmhl/" + $0)) }
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        let (html, json, inside) = try VerifyReport.write(r, pdf: false)
        XCTAssertTrue(inside)
        XCTAssertTrue(html.hasPrefix(card + "/_REPORT/A001_verify_"), html)
        XCTAssertTrue(html.hasSuffix("Z.html"))
        XCTAssertTrue((json as NSString).lastPathComponent.hasPrefix("offload_verify_"))
        XCTAssertEqual(try TestFS.files(in: card + "/ascmhl").map { try Data(contentsOf: URL(fileURLWithPath: card + "/ascmhl/" + $0)) }, before)
        // The card still verifies afterwards: the report is inside the ignored _REPORT, so nothing is extra.
        XCTAssertTrue(try BackupVerifier.verify(card).extra.isEmpty)
        // A second report of the same second is never written over the first.
        XCTAssertThrowsError(try VerifyReport.write(r, pdf: false))
    }

    func testForeignMhlPutsReportBesideTheCard() throws {
        let card = try BackupVerifierTests().offload(tempDir())
        // A tool that ignores only .DS_Store and ascmhl: rewrite the ignore list of generation 1 and its C4 in the chain.
        let chainDir = card + "/ascmhl"
        let chain = try XCTUnwrap(MHLReader.readChain(ascmhlDir: chainDir))
        let file = chainDir + "/" + chain[0].path
        var text = try String(contentsOfFile: file, encoding: .utf8)
        for p in ["_REPORT", "_PROXY", "ascmhl/"] { text = text.replacingOccurrences(of: "      <pattern>\(p)</pattern>\n", with: "") }
        try text.write(toFile: file, atomically: true, encoding: .utf8)
        let c4 = MHL.c4(Data(text.utf8))
        let chainText = try String(contentsOfFile: chainDir + "/ascmhl_chain.xml", encoding: .utf8).replacingOccurrences(of: chain[0].c4, with: c4)
        try chainText.write(toFile: chainDir + "/ascmhl_chain.xml", atomically: true, encoding: .utf8)
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        XCTAssertTrue(r.chainProblems.isEmpty, "\(r.chainProblems)")
        XCTAssertFalse(r.reportInsideCard)
        let (html, json, inside) = try VerifyReport.write(r, pdf: false)
        XCTAssertFalse(inside)
        let day = (card as NSString).deletingLastPathComponent
        XCTAssertTrue(html.hasPrefix(day + "/_REPORT/A001_verify_"))
        XCTAssertTrue((json as NSString).lastPathComponent.hasPrefix("A001_verify_"))
        XCTAssertFalse(TestFS.files(in: card + "/_REPORT").contains { $0.contains("_verify_") }, "nothing inside the card")
        XCTAssertEqual(VerifyReport.reportOutsideNote(html),
                       "The verification report is saved outside the card folder so it does not disturb its ASC MHL history: \(html)")
    }

    func testJsonShape() throws {
        let card = try BackupVerifierTests().offload(tempDir())
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        let data = try VerifyReport.jsonData(r)
        let obj = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        XCTAssertEqual(obj.keys.sorted(), ["basis", "cardPath", "chainProblems", "changed", "extra", "failedAtCopy", "failedSuperseded",
                                           "finished", "generations", "hashAlgorithms", "missing", "ok", "started", "success", "tool",
                                           "totalBytes", "totalFiles", "unfinished", "unreadable", "version"])
        XCTAssertEqual(obj["success"] as? Bool, true)
        XCTAssertNotNil((obj["started"] as? String)?.range(of: #"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$"#, options: .regularExpression))
        XCTAssertFalse(String(decoding: data, as: UTF8.self).contains("\\/"), "paths unescaped")
    }

    func testHtmlTexts() throws {
        var r = VerifyResult(cardPath: "/x/2026-10-08/A001")
        r.ok = 1
        r.generations = 2
        r.hashAlgorithms = ["xxh64", "xxh128"]
        XCTAssertEqual(VerifyReport.summary(r).text, "Verified: the file matches (ASC MHL, 2 generations, xxHash64 + xxHash128).")
        let html = VerifyReport.html(r)
        XCTAssertTrue(html.contains("<title>Verification A001</title>"))
        XCTAssertTrue(html.contains("VERIFIED"))
        r.failedAtCopy = ["CLIP/X.mov"]
        XCTAssertEqual(VerifyReport.summary(r).text,
                       "Cannot verify: 1 file already failed during the copy and the record has no valid checksum — details in the verification report.")
        XCTAssertTrue(VerifyReport.html(r).contains("PROBLEMS FOUND"))
        r.ok = 1234
        r.failedAtCopy = []
        XCTAssertEqual(VerifyReport.summary(r).text, "Verified: all 1,234 files match (ASC MHL, 2 generations, xxHash64 + xxHash128).")
    }
}
