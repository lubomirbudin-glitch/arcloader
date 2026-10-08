import Foundation

/// Timecode helpers for the report thumbnails and the clip lists (port of Windows `Timecode.cs`).
public enum Timecode {
    public static func isValid(_ tc: String?) -> Bool {
        guard let tc else { return false }
        return tc.range(of: #"^\d{2}:\d{2}:\d{2}[:;.]\d{2}$"#, options: .regularExpression) != nil
    }

    /// "24000/1001" → 23.976; "25" → 25; nil for anything unusable.
    public static func fps(_ rational: String?) -> Double? {
        guard let r = rational, !r.isEmpty else { return nil }
        let parts = r.split(separator: "/", omittingEmptySubsequences: false)
        guard parts.count <= 2, let num = Double(parts[0]) else { return nil }
        var fps = num
        if parts.count == 2 {
            guard let den = Double(parts[1]), den != 0 else { return nil }
            fps = num / den
        }
        return fps > 0 && fps.isFinite ? fps : nil
    }

    /// The nominal (rounded) rate a timecode counts at: 23.976 → 24, 29.97 → 30, 59.94 → 60.
    public static func nominal(_ fps: Double) -> Int { max(1, Int(fps.rounded(.toNearestOrEven))) }

    /// Drop-frame counting: a ";" timecode at 29.97 or 59.94 (Windows spec 3.3, review focus 1).
    public static func isDropFrame(_ tc: String, fps: Double) -> Bool {
        guard isValid(tc) else { return false }
        let n = nominal(fps)
        return Array(tc)[8] == ";" && (n == 30 || n == 60) && abs(fps - Double(n)) > 0.001
    }

    /// Start timecode plus a number of frames, wrapping at 24 h, with the separator of `tc`. A drop-frame timecode
    /// counts as the camera does: frame numbers 00 and 01 (00–03 at 59.94) are left out at every minute but every tenth.
    public static func add(_ tc: String, frames: Int, fps: Double) -> String {
        let df = isDropFrame(tc, fps: fps)
        let perDay = df ? 144 * framesPerTenMinutes(fps) : 24 * 3600 * nominal(fps)
        let total = ((toFrames(tc, fps: fps) + frames) % perDay + perDay) % perDay
        return fromFrames(total, fps: fps, dropFrame: df, separator: Array(tc)[8])
    }

    /// A length in frames as "HH:MM:SS:FF" at the nominal rate (the Duration of a clip list).
    public static func length(frames: Int, fps: Double) -> String {
        fromFrames(max(0, frames), fps: fps, dropFrame: false, separator: ":")
    }

    static func framesPerTenMinutes(_ fps: Double) -> Int { nominal(fps) * 600 - nominal(fps) / 15 * 9 }

    /// The frame number of `tc` counted from 00:00:00:00.
    static func toFrames(_ tc: String, fps: Double) -> Int {
        let n = nominal(fps)
        let c = Array(tc)
        func num(_ r: Range<Int>) -> Int { Int(String(c[r])) ?? 0 }
        let h = num(0..<2), m = num(3..<5), s = num(6..<8), f = num(9..<11)
        var frames = ((h * 60 + m) * 60 + s) * n + f
        if isDropFrame(tc, fps: fps) {
            let minutes = h * 60 + m
            frames -= n / 15 * (minutes - minutes / 10)
        }
        return frames
    }

    static func fromFrames(_ input: Int, fps: Double, dropFrame: Bool, separator: Character) -> String {
        let n = nominal(fps)
        var frames = input
        if dropFrame {
            let drop = n / 15
            let perTen = framesPerTenMinutes(fps), perMinute = n * 60 - drop
            let tens = frames / perTen, rest = frames % perTen
            frames += drop * 9 * tens + (rest > drop ? drop * ((rest - drop) / perMinute) : 0)
        }
        let ff = frames % n, secs = frames / n
        return String(format: "%02d:%02d:%02d", secs / 3600 % 24, secs / 60 % 60, secs % 60) + String(separator)
            + String(format: "%02d", ff)
    }
}
