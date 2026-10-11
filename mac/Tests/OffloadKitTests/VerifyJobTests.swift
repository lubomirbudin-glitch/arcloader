import XCTest
@testable import OffloadKit

final class VerifyJobTests: XCTestCase {
    func testQueueItemTexts() throws {
        let card = try BackupVerifierTests().offload(tempDir())
        let job = VerifyJob(cardPath: card, writeReport: false)
        XCTAssertEqual(VerifyJobView.title(job.cardName), "Verification A001")
        XCTAssertEqual(VerifyJobView.statusText(job.currentStatus()), "Waiting")
        XCTAssertEqual(VerifyJobView.detail(nil, bytesPerSecond: 0), "Verifying…")
        XCTAssertEqual(VerifyJobView.detail(VerifyProgress(file: "a", fileIndex: 119, fileCount: 1234, bytes: 45_000_000_000, totalBytes: 512_000_000_000),
                                            bytesPerSecond: 380_000_000),
                       "Verifying 120 / 1,234 files · 45 GB / 512 GB · 380 MB/s")
        job.run()
        let s = job.currentStatus()
        XCTAssertEqual(s.state, .done)
        XCTAssertEqual(VerifyJobView.statusText(s), "Verified")
        XCTAssertEqual(VerifyJobView.tone(s), .ok)
        XCTAssertTrue(VerifyJobView.result(s)?.text.hasPrefix("Verified: all 2 files match") == true)
        XCTAssertNil(s.reportPath, "no report asked for")
        XCTAssertEqual(s.progress?.fileCount, 2)
        XCTAssertEqual(VerifyJobView.fraction(s.progress), 1)
    }

    func testReportWrittenAndProblemsAreRed() throws {
        let card = try BackupVerifierTests().offload(tempDir())
        try FileManager.default.removeItem(atPath: card + "/CLIP/B.mov")
        let job = VerifyJob(cardPath: card, writeReport: true)
        job.run()
        let s = job.currentStatus()
        XCTAssertEqual(VerifyJobView.statusText(s), "Problems")
        XCTAssertEqual(VerifyJobView.tone(s), .error)
        XCTAssertTrue(s.reportPath?.hasPrefix(card + "/_REPORT/A001_verify_") == true)
        XCTAssertNil(VerifyJobView.reportNote(s), "inside the card folder: no note")
    }

    func testCancelledWritesNoReport() throws {
        let card = try BackupVerifierTests().offload(tempDir())
        let job = VerifyJob(cardPath: card, writeReport: true)
        job.cancel()
        XCTAssertEqual(job.currentStatus().state, .cancelled, "cancelled while queued")
        job.run()
        XCTAssertEqual(job.currentStatus().state, .cancelled)
        XCTAssertFalse(TestFS.files(in: card + "/_REPORT").contains { $0.contains("_verify_") })
    }

    func testMissingRecordFails() throws {
        let dir = tempDir()
        let job = VerifyJob(cardPath: dir.sub("nothing"), writeReport: true)
        job.run()
        XCTAssertEqual(job.currentStatus().state, .failed)
        XCTAssertEqual(VerifyJobView.tone(job.currentStatus()), .error)
        XCTAssertNotNil(VerifyJobView.result(job.currentStatus()))
    }
}
