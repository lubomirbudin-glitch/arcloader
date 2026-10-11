import Foundation

extension OffloadReport {
    public static let verifyInProxyMethod = "verify-in-proxy"
    public static let readBackMethod = "read-back"

    /// Task 67 (Windows `WithVerification`): this report — written at the end of the copy with `verifyPending` — once the
    /// verification of stage 2 ended. `results`: per destination, per file; `verified`: the verification ran to its end
    /// (false = cancelled: unread copies stay "written"); `primary`: the destination verified through the proxies (-1:
    /// none); `classicWhy`: per destination why it was read back instead (the report's `verifyNote`).
    public func withVerification(results: [[DestFileResult]], verified: Bool, primary: Int, classicWhy: [String?], notes: [String],
                                 finished: Date, verifyPaused: TimeInterval) -> OffloadReport {
        var r = self
        func isOk(_ s: FileState) -> Bool { s == .verified || (!verified && s == .written) }
        for i in r.files.indices {
            let before = files[i]
            r.files[i].destinations = results.map { $0[i].state.rawValue }
            r.files[i].errors = results.enumerated().map { n, d in
                d[i].error ?? (d[i].state == .failed && n < before.errors.count ? before.errors[n] : nil)
            }
        }
        for d in r.destinations.indices where d < results.count {
            r.destinations[d].filesOK = results[d].filter { isOk($0.state) }.count
            r.destinations[d].filesFailed = results[d].filter { !isOk($0.state) }.count
            r.destinations[d].verifyMethod = !verified ? nil : d == primary ? OffloadReport.verifyInProxyMethod : OffloadReport.readBackMethod
            r.destinations[d].verifyNote = verified && d != primary && d < classicWhy.count ? classicWhy[d] : nil
        }
        r.verified = verified
        r.verifyPending = nil
        r.verifyIncomplete = nil
        r.finished = finished
        r.verifyPausedSeconds = OffloadEngine.pausedField(verifyPaused)
        r.safeToFormatSeconds = r.safeToFormat
            ? (max(0, finished.timeIntervalSince(started) - r.pausedSeconds) * 10).rounded() / 10 : nil
        r.verifyNotes = notes.isEmpty ? nil : notes
        return r
    }
}

/// The commit of a generation that verified in stage 2 (task 67; Windows `FinishVerification`, ruling M-a, I1): into
/// every card folder the ASC MHL generation and chain first, then the reports, then the hash lists
/// (`OffloadReport.commit`, idempotent). A folder that disappeared meanwhile (an unplugged disk) is never made anew. When
/// the commit fails anywhere, the folders it was written into are written once more with `verifyIncomplete: true` — on
/// its own such a folder would look verified while another destination lacks the result. Never throws.
public enum Verification {
    public struct Outcome {
        /// The result is written into every card folder.
        public let committed: Bool
        /// The folders the result was written into.
        public let written: [String]
        /// The job's warning when not committed everywhere.
        public let warning: String?
        /// The report as it was written last (with `verifyIncomplete` when not committed everywhere).
        public let report: OffloadReport
    }

    /// `beforeCommit`: tests only — called before each folder (a disk unplugged in between).
    public static func commit(_ final: OffloadReport, cardPaths: [String], log: ((String) -> Void)? = nil,
                              beforeCommit: ((String) -> Void)? = nil) -> Outcome {
        var written: [String] = []
        var warning: String?
        var committed = true
        for path in cardPaths {
            do {
                beforeCommit?(path)
                guard FileManager.default.fileExists(atPath: path) else { throw IOError(message: VIPMsg.cardFolderGone) }
                try final.commit(cardPath: path)
                written.append(path)
            } catch {
                committed = false
                log?("commit \(path): \(error)")
                if warning == nil { warning = VIPMsg.verifyNotCommitted("\(path): \(error)") }
            }
        }
        var last = final
        if !committed && !written.isEmpty {
            last.verifyIncomplete = true
            for path in written {
                do { try last.commit(cardPath: path) } catch { log?("commit \(path) (incomplete): \(error)") }
            }
        }
        return Outcome(committed: committed, written: written, warning: warning, report: last)
    }
}
