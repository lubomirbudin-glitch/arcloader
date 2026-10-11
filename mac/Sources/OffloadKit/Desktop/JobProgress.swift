import Foundation

public enum CardTone: Equatable { case waiting, active, ok, warning, error }

/// One of the card row's two verdicts (Windows R5): "Safe to eject" or "Safe to format" and how it looks.
public struct CardChip: Equatable {
    public let text: String
    public let tone: CardTone

    public init(text: String, tone: CardTone) {
        self.text = text
        self.tone = tone
    }
}

extension JobProgress {
    /// The card row's two verdicts, never merged (R5). Green only where the card may be formatted (ruling I2); "Safe
    /// to eject" alone is neutral. A waiting job shows none (the caller hides them). `contentWarnings` (task 70): the
    /// copy is exact but the decoder found defects — yellow until the user has looked.
    public static func chips(_ s: JobStatus, contentConfirmed: Bool = true, contentWarnings: Int = 0) -> (eject: CardChip, format: CardChip) {
        let running = s.phase != .waiting && !s.phase.isFinished
        let format: CardChip = s.safeToFormat
            ? (contentWarnings > 0 && !contentConfirmed ? CardChip(text: "Safe to format after checking defects", tone: .warning)
                                                        : CardChip(text: "Safe to format", tone: .ok))
            : running ? CardChip(text: s.safeToEject ? "Verifying…" : "Do not format yet", tone: .waiting)
            : CardChip(text: "Do not format", tone: s.safeToEject ? .warning : .error)
        let eject: CardChip = s.safeToEject ? CardChip(text: "Safe to eject", tone: format.tone == .ok ? .ok : .waiting)
            : running ? CardChip(text: "Do not eject yet", tone: .waiting) : CardChip(text: "Do not eject", tone: .error)
        return (eject, format)
    }

    /// The success sound (and the "done" notification): formattable everywhere and nothing to look at.
    public static func chime(_ s: JobStatus) -> Bool {
        s.safeToFormat && s.warning == nil && s.phase != .cancelled && s.phase != .failed
    }
}

/// What a job card shows, from successive `JobStatus` snapshots (port of Windows `Desktop/JobProgress.cs`;
/// the logic of the app's former `JobItem.refresh` and `JobCard` texts, now testable).
public final class JobProgress {
    private let started: Date
    private var lastTime: Date
    private var lastBytes: Int64 = 0
    private var lastDest: [Int64] = []
    private var lastPhase: JobStatus.Phase = .waiting
    private var brakes: BrakeMeter?

    public private(set) var status = JobStatus()
    /// Bytes per second, smoothed: card read while copying, slowest destination while verifying.
    public private(set) var readRate: Double = 0
    public private(set) var destRates: [Double] = []
    public private(set) var eta: Double?
    /// The element braking the copy (0 = card, i = destination i); only while copying.
    public private(set) var brake: Brake?

    public init(now: Date) {
        started = now
        lastTime = now
    }

    public func attachBrakeMeter(_ meter: BrakeMeter) { brakes = meter }

    public var isRunning: Bool { status.phase != .waiting && !status.phase.isFinished }

    public func update(_ s: JobStatus, now: Date) {
        let dt = now.timeIntervalSince(lastTime)
        if let c = s.copy, dt > 0.2 {
            let copying = s.phase == .copying
            let bytes = copying ? c.readBytes : (c.verifiedBytes.min() ?? 0)
            let dests = copying ? c.writtenBytes : c.verifiedBytes
            if s.phase != lastPhase || lastDest.count != dests.count {
                readRate = 0
                destRates = Array(repeating: 0, count: dests.count)
            } else {
                readRate = JobProgress.smooth(readRate, max(0, Double(bytes - lastBytes) / dt))
                destRates = dests.indices.map { JobProgress.smooth(destRates[$0], max(0, Double(dests[$0] - lastDest[$0]) / dt)) }
                eta = readRate > 1 ? Double(c.totalBytes - bytes) / readRate : nil
            }
            lastPhase = s.phase
            lastBytes = bytes
            lastDest = dests
            lastTime = now
        }
        if s.phase == .copying, let cc = s.copy, let brakes {
            brake = brakes.add(seconds: now.timeIntervalSince(started), readBytes: cc.readBytes, writtenBytes: cc.writtenBytes) ?? brake
        } else {
            brake = nil
        }
        status = s
    }

    static func smooth(_ old: Double, _ v: Double) -> Double { old == 0 ? v : old * 0.6 + v * 0.4 }

    /// 0…1 within the current phase.
    public var phaseProgress: Double { min(1, max(0, rawProgress)) }

    private var rawProgress: Double {
        switch status.phase {
        case .copying:
            guard let c = status.copy, c.totalBytes > 0 else { return 0 }
            return Double(c.readBytes) / Double(c.totalBytes)
        case .verifying:
            guard let c = status.copy, c.totalBytes > 0 else { return 0 }
            return Double(c.verifiedBytes.reduce(0, +)) / (Double(c.totalBytes) * Double(max(1, c.verifiedBytes.count)))
        case .analyzing, .proxies:
            guard status.clipCount > 0 else { return 0 }
            return (Double(status.clipIndex) + status.clipFraction) / Double(status.clipCount)
        case .done:
            return 1
        default:
            return 0
        }
    }

    public var statusText: String { status.phase == .done && status.copySucceeded == false ? "Error" : status.phase.label }

    /// Failed, or copied with errors — a job the user cancelled is not an error (no error sound).
    public var isError: Bool { tone == .error }

    /// Spec D.22.7 (Windows ruling I2): green only when the card may be formatted and nothing needs a look; copied
    /// alone ("Safe to eject") or a warning is orange.
    public var tone: CardTone {
        switch status.phase {
        case .done:
            if status.copySucceeded == false { return .error }
            return status.safeToFormat && status.warning == nil ? .ok : .warning
        case .failed: return .error
        case .cancelled: return .warning
        case .waiting: return .waiting
        default: return .active
        }
    }

    static func mb(_ bytesPerSecond: Double) -> String { String(format: "%.0f MB/s", bytesPerSecond / 1_000_000) }

    public static func etaText(_ seconds: Double) -> String {
        let i = seconds.isNaN || seconds < 0 ? 0 : Int(min(seconds, Double(Int32.max)))
        return i >= 3600 ? String(format: "%d:%02d:%02d", i / 3600, i / 60 % 60, i % 60) : String(format: "%d:%02d", i / 60, i % 60)
    }

    public var detailText: String {
        let s = status
        let etaPart = eta.map { " · " + JobProgress.etaText($0) + " left" } ?? ""
        let dests = destRates.enumerated().map { "D\($0.offset + 1) \(JobProgress.mb($0.element))" }.joined(separator: " · ")
        switch s.phase {
        case .copying:
            let file = ((s.copy?.currentFile ?? "") as NSString).lastPathComponent
            return "Card \(JobProgress.mb(readRate)) · \(dests)\(etaPart) · \(file)"
        case .verifying:
            return dests + etaPart
        case .analyzing:
            return "Clip \(s.clipIndex + 1)/\(s.clipCount) · \(s.clipName)"
        case .proxies:
            return "Proxy \(s.clipIndex + 1)/\(s.clipCount) · \(s.clipName) · " + String(format: "%.0f", s.clipFraction * 100) + "%"
        default:
            return s.phase.label + "…"
        }
    }

    public static func summaryText(_ r: OffloadReport) -> String {
        var parts = ["copy \(formatRate(r.totalBytes, r.copySeconds))"]
        if r.verified { parts.append("verify \(formatRate(r.totalBytes, r.verifySeconds))") }
        if !r.clips.isEmpty { parts.append(Texts.clips(r.clips.count)) }
        if r.proxy?.enabled == true {
            func count(_ st: ProxyStatus) -> Int { r.clips.filter { $0.proxyStatus == st }.count }
            var proxy = "proxy \(count(.done))"
            if count(.failed) > 0 { proxy += ", \(count(.failed)) failed" }
            if count(.unsupported) > 0 { proxy += ", \(count(.unsupported)) unsupported" }
            parts.append(proxy)
        }
        return parts.joined(separator: " · ")
    }
}
