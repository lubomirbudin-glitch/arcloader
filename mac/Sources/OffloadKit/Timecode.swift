import Foundation

/// Timecode helpers for the report thumbnails (port of Windows `Timecode.cs`).
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

    /// Start timecode plus a number of frames, counted at the nominal (rounded) rate and wrapping at 24 h.
    /// Drop-frame timecode is labelled approximately — good enough for a thumbnail caption.
    public static func add(_ tc: String, frames: Int, fps: Double) -> String {
        let nominal = max(1, Int(fps.rounded()))
        let c = Array(tc)
        func num(_ r: Range<Int>) -> Int { Int(String(c[r])) ?? 0 }
        let h = num(0..<2), m = num(3..<5), s = num(6..<8), f = num(9..<11)
        let perDay = 24 * 3600 * nominal
        let total = (((h * 3600 + m * 60 + s) * nominal + f + frames) % perDay + perDay) % perDay
        let ff = total % nominal, secs = total / nominal
        return String(format: "%02d:%02d:%02d", secs / 3600, secs / 60 % 60, secs % 60) + String(c[8]) + String(format: "%02d", ff)
    }
}
