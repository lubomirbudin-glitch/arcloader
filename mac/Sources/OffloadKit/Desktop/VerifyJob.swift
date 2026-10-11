import Foundation

/// "Verify backup…" as a queue item (Windows spec 2.4): it runs on the copy queue, one after another with the copies —
/// both load the same disks. Reads only; the report (optional) goes where `VerifyReport.write` puts it.
public final class VerifyJob {
    public enum State: Equatable { case waiting, running, done, cancelled, failed }

    public struct Status {
        public var state: State = .waiting
        public var progress: VerifyProgress?
        public var result: VerifyResult?
        public var error: String?
        public var reportPath: String?
        public var reportError: String?

        public init() {}
    }

    public let cardPath: String
    public let writeReport: Bool
    private let lock = NSLock()
    private var status = Status()
    private var cancelled = false
    public var log: ((String) -> Void)?

    public init(cardPath: String, writeReport: Bool) {
        self.cardPath = cardPath
        self.writeReport = writeReport
    }

    public var cardName: String { (cardPath.trimmingTrailingSlash() as NSString).lastPathComponent }

    public var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return cancelled }

    public func cancel() {
        lock.lock(); cancelled = true; lock.unlock()
    }

    /// Read by the main thread every tick. Cancelled while queued: it never runs, so it says so instead of waiting forever.
    public func currentStatus() -> Status {
        lock.lock(); defer { lock.unlock() }
        var s = status
        if s.state == .waiting && cancelled { s.state = .cancelled }
        return s
    }

    private func set(_ f: (inout Status) -> Void) {
        lock.lock(); f(&status); lock.unlock()
    }

    /// Runs synchronously (never on the main thread — the PDF of the report waits for the main run loop).
    public func run() {
        if isCancelled {
            set { $0.state = .cancelled }
            return
        }
        set { $0.state = .running }
        let activity = ProcessInfo.processInfo.beginActivity(options: [.userInitiated, .idleSystemSleepDisabled], reason: "Verifying a backup")
        defer { ProcessInfo.processInfo.endActivity(activity) }
        do {
            let r = try BackupVerifier.verify(cardPath, progress: { p in self.set { $0.progress = p } }, isCancelled: { self.isCancelled })
            var report: String?, reportError: String?
            if writeReport && !r.cancelled {
                // Short, and a half-written report is worse: it ignores Cancel.
                do {
                    report = try VerifyReport.write(r, log: log).html
                } catch {
                    log?("verify report: \(error)")
                    reportError = Msg.reportNotWritten("\(error)")
                }
            }
            set {
                $0.state = r.cancelled ? .cancelled : .done
                $0.result = r
                $0.reportPath = report
                $0.reportError = reportError
            }
        } catch {
            log?("verify: \(error)")
            set {
                $0.state = .failed
                $0.error = "\(error)"
            }
        }
    }
}

/// What a verification card in the queue shows — the English texts of Windows `VerifyJobView`.
public enum VerifyJobView {
    public static func title(_ cardName: String) -> String { "Verification \(cardName)" }

    public static func statusText(_ s: VerifyJob.Status) -> String {
        switch s.state {
        case .waiting: return "Waiting"
        case .running: return "Verifying"
        case .done:
            guard let r = s.result, r.success else { return "Problems" }
            return r.unfinished ? "Unfinished" : "Verified"
        case .cancelled: return "Cancelled"
        case .failed: return "Error"
        }
    }

    /// Never green for a backup whose newest generation is not finished.
    public static func tone(_ s: VerifyJob.Status) -> CardTone {
        switch s.state {
        case .waiting: return .waiting
        case .running: return .active
        case .done:
            guard let r = s.result, r.success else { return .error }
            return r.unfinished ? .warning : .ok
        case .cancelled: return .warning
        case .failed: return .error
        }
    }

    /// "Verifying 120 / 1,234 files · 45 GB / 512 GB · 380 MB/s"
    public static func detail(_ p: VerifyProgress?, bytesPerSecond: Double) -> String {
        guard let p else { return "Verifying…" }
        return "Verifying \(VerifyReport.n(p.fileIndex + 1)) / \(VerifyReport.n(p.fileCount)) files · \(formatBytesShort(p.bytes)) / "
            + "\(formatBytesShort(p.totalBytes)) · \(String(format: "%.0f", bytesPerSecond / 1_000_000)) MB/s"
    }

    /// The result line and its tone (VerifyReport.summary), or nil while it runs.
    public static func result(_ s: VerifyJob.Status) -> (text: String, tone: CardTone)? {
        if let e = s.error { return (e, .error) }
        guard let r = s.result else { return nil }
        return VerifyReport.summary(r)
    }

    /// The note when the report went outside the card folder, or why it was not written; nil otherwise.
    public static func reportNote(_ s: VerifyJob.Status) -> String? {
        if let e = s.reportError { return e }
        guard let path = s.reportPath, s.result?.reportInsideCard == false else { return nil }
        return VerifyReport.reportOutsideNote(path)
    }

    public static func fraction(_ p: VerifyProgress?) -> Double {
        guard let p, p.totalBytes > 0 else { return 0 }
        return min(1, max(0, Double(p.bytes) / Double(p.totalBytes)))
    }
}
