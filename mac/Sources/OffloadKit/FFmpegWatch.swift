import Foundation

/// ffmpeg made no progress for too long and was killed (port of Windows `FfmpegStalledException`).
/// `note` is the text shown at the clip.
struct FFmpegStall: Error, CustomStringConvertible {
    let note: String

    var description: String { note }
}

/// The hardware-decoded attempt and the software retry both failed; the note names both (Windows `ProxyRetryException`).
struct ProxyRetryFailed: Error, CustomStringConvertible {
    let note: String

    var description: String { note }
}

/// Watches a render: only a `frame=` or `out_time_us=` value above any seen before (or the final `progress=end`) is
/// progress — a stuck ffmpeg keeps printing the same block every half second, and the opening "frame=0",
/// "out_time_us=N/A" or negative times say nothing has been written yet. Data a RAW decoder pipes into ffmpeg is
/// progress too. Until the first progress the limit is 3× as long. Lines and data arrive on other threads — a lock.
final class ProgressWatch {
    private let lock = NSLock()
    private var frame: Int64 = 0
    private var outTime: Int64 = 0
    private var lastAdvance: Date
    private var advancedOnce = false

    init(now: Date = Date()) {
        lastAdvance = now
    }

    @discardableResult
    func advanced(_ line: String, now: Date = Date()) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        let moved: Bool
        if line.hasPrefix("frame=") {
            moved = ProgressWatch.rose(&frame, line.dropFirst("frame=".count))
        } else if line.hasPrefix("out_time_us=") {
            moved = ProgressWatch.rose(&outTime, line.dropFirst("out_time_us=".count))
        } else {
            moved = line == "progress=end"
        }
        if moved { mark(now) }
        return moved
    }

    /// A chunk the RAW decoder piped into ffmpeg (a slow decoder is not stuck).
    func dataFlowed(now: Date = Date()) {
        lock.lock()
        mark(now)
        lock.unlock()
    }

    var hasAdvanced: Bool {
        lock.lock()
        defer { lock.unlock() }
        return advancedOnce
    }

    func idleSeconds(now: Date = Date()) -> TimeInterval {
        lock.lock()
        defer { lock.unlock() }
        return now.timeIntervalSince(lastAdvance)
    }

    /// No progress for `limit` — or for 3× `limit` before the first progress (input probing, a RAW decoder starting
    /// up, the encoder's lookahead).
    func isStalled(limit: TimeInterval, now: Date = Date()) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        return now.timeIntervalSince(lastAdvance) > limit * (advancedOnce ? 1 : 3)
    }

    private func mark(_ now: Date) {
        lastAdvance = now
        advancedOnce = true
    }

    private static func rose(_ max: inout Int64, _ value: Substring) -> Bool {
        guard let v = Int64(value.trimmingCharacters(in: .whitespaces)), v > max else { return false }
        max = v
        return true
    }
}
