import Foundation

/// The element holding the copy back. Element 0 = the card (source read), i = destination i.
/// `mbps`: its pace over the window; `utilization`: of its USB link (nil without one).
public struct Brake: Equatable {
    public let element: Int
    public let mbps: Double
    public let utilization: Double?

    public var linkFull: Bool { (utilization ?? 0) > 0.85 }
}

/// Decides which element brakes the copy, from the backlog rather than from rates (port of Windows `BrakeMeter.cs`).
///
/// The engine runs in lockstep: one bounded buffer pool (bufferCount × chunkSize); a chunk returns to it only when
/// every writer and the hasher are done with it. So the card and all disks move at the same average pace and
/// short-window rates only show chunk quantisation. What differs is the backlog: lag_i = readBytes − writtenBytes[i].
/// The slow writer keeps the pool full (its lag is near the pool size), a fast one stays within a chunk or two.
/// → The writer with the largest lag brakes, if that lag (averaged over the window) is at least ~2 chunks (half the
///   pool when the pool is smaller) or grew by that much within the window; otherwise every disk keeps up and the card brakes.
public final class BrakeMeter {
    private let threshold: Double
    private let windowSeconds: Double
    private let linkMBps: [Double?]
    private var samples: [(time: Double, read: Int64, written: [Int64])] = []

    /// linkMBps: ceiling per element (card first, then destinations), nil = no USB link.
    public init(chunkBytes: Int64, bufferCount: Int, linkMBps: [Double?] = [], windowSeconds: Double = 4) {
        let pool = Double(chunkBytes) * Double(max(bufferCount, 1))
        threshold = min(2.0 * Double(chunkBytes), pool / 2.0)
        self.windowSeconds = windowSeconds
        self.linkMBps = linkMBps
    }

    /// Forgets the window (a pause: the time standing says nothing about who brakes).
    public func reset() { samples.removeAll() }

    /// Feed one progress snapshot (seconds from any fixed start). Returns the brake once the window holds enough
    /// history (half the window), else nil.
    public func add(seconds: Double, readBytes: Int64, writtenBytes: [Int64]) -> Brake? {
        if let first = samples.first, let last = samples.last,
           first.written.count != writtenBytes.count || seconds < last.time {
            samples.removeAll()
        }
        samples.append((seconds, readBytes, writtenBytes))
        while samples.count > 2 && seconds - samples[0].time > windowSeconds { samples.removeFirst() }

        let first = samples[0]
        let span = seconds - first.time
        guard samples.count >= 2, span > 0, span >= windowSeconds / 2 else { return nil }

        // A writer brakes when its backlog is large, or still growing (the pool is filling up: the card is faster).
        // Of those, the largest backlog; equal backlogs → the first. No such writer → the card.
        var brake = 0
        var worst = -Double.infinity
        let last = samples[samples.count - 1]
        for i in writtenBytes.indices {
            let lag = samples.reduce(0.0) { $0 + Double($1.read - $1.written[i]) } / Double(samples.count)
            let growth = Double((last.read - last.written[i]) - (first.read - first.written[i]))
            if (lag >= threshold || growth >= threshold) && lag > worst {
                worst = lag
                brake = i + 1
            }
        }
        let moved = brake == 0 ? readBytes - first.read : writtenBytes[brake - 1] - first.written[brake - 1]
        let mbps = Double(max(0, moved)) / span / 1e6
        var utilization: Double?
        if brake < linkMBps.count, let l = linkMBps[brake], l > 0 { utilization = mbps / l }
        return Brake(element: brake, mbps: mbps, utilization: utilization)
    }
}
