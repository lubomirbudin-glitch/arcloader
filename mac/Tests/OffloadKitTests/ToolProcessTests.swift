import XCTest
@testable import OffloadKit

final class ToolProcessTests: XCTestCase {
    // Spec D.16 (Windows ruling M3): a hung tool never hangs the job — killed after the timeout, the message says so.
    func testTimeoutKillsAndSaysSo() {
        let hang = BrawTest.script(tempDir(), "hang", "exec sleep 30")
        let t0 = Date()
        XCTAssertThrowsError(try ToolProcess.run(hang, [], timeout: 1)) { e in
            XCTAssertEqual("\(e)", "hang failed (exit -1): neodpověděl do 1 s — ukončen")
        }
        XCTAssertLessThan(Date().timeIntervalSince(t0), 10)
    }

    func testWithinTheTimeoutStdoutAsBefore() throws {
        let ok = BrawTest.script(tempDir(), "ok", "sleep 0.3\necho hello")
        XCTAssertEqual(try ToolProcess.run(ok, [], timeout: 5), "hello\n")
        XCTAssertEqual(try ToolProcess.run(ok, []), "hello\n")
    }

    // The error carries the whole stderr even when the tool ends at once (the pipe handler may lag behind the exit).
    func testStderrCompleteInTheError() {
        let fail = BrawTest.script(tempDir(), "fail", "echo 'first line' >&2\necho 'braw-decode: cannot open /x/Klip č. 1.braw' >&2\nexit 4")
        for _ in 0..<20 {
            XCTAssertThrowsError(try ToolProcess.run(fail, [])) { e in
                XCTAssertEqual((e as? ToolError)?.status, 4)
                XCTAssertEqual((e as? ToolError)?.message, "first line\nbraw-decode: cannot open /x/Klip č. 1.braw\n")
            }
        }
    }
}
