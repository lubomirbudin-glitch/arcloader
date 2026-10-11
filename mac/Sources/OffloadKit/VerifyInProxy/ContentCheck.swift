import Foundation

/// Spec 7 (content_ok / content_warn), report key `clips[].content` (Windows `ContentResult`, FORMATY): what the decoder
/// said while it made the proxy. A warning is about the file itself (the copy matches the card bit for bit): it never fails
/// the copy and never blocks "Safe to format", but formatting asks for an explicit confirmation. Absent for a clip without
/// a proxy.
public struct ClipContent: Codable, Equatable {
    public static let ok = "ok"
    public static let warn = "warn"

    /// "ok" or "warn".
    public var state = ClipContent.ok
    /// "decode", "audio", "timestamps", "frames" (more frames than the file says), "end" (fewer: the clip ends early).
    public var kind: String?
    /// The frame near the first defect (approximate: ffmpeg reports its progress every half second).
    public var frame: Int64?
    /// That frame as the clip's timecode, or "#<frame>" without one.
    public var timecode: String?
    /// The decoder's first line about it (as ffmpeg wrote it), or the frame counts.
    public var message: String?
    public var framesDecoded: Int64?
    public var framesExpected: Int64?
    /// The picture at that place, in the HTML report only.
    public var jpeg: Data?

    enum CodingKeys: String, CodingKey { case state, kind, frame, timecode, message, framesDecoded, framesExpected }

    public init(state: String = ClipContent.ok) { self.state = state }

    public var isWarn: Bool { state == ClipContent.warn }

    /// "corrupted data", "corrupted audio", "bad timestamps", "extra frames", "clip ends early" (Windows 0.9.0).
    public static func kindText(_ kind: String?) -> String {
        switch kind {
        case "decode": return "corrupted data"
        case "audio": return "corrupted audio"
        case "timestamps": return "bad timestamps"
        case "frames": return "extra frames"
        case "end": return "clip ends early"
        default: return "content defect"
        }
    }

    public static func == (a: ClipContent, b: ClipContent) -> Bool {
        a.state == b.state && a.kind == b.kind && a.frame == b.frame && a.timecode == b.timecode && a.message == b.message
            && a.framesDecoded == b.framesDecoded && a.framesExpected == b.framesExpected
    }
}

/// Watches one ffmpeg proxy run (Windows `ContentWatch`; R9: never -xerror, it would stop the proxy): the stderr lines at
/// -loglevel warning and the `frame=` counter of -progress. Lines about damaged data or time stamps are a warning; any
/// other line (encoder notes …) is not. Thread-safe: stdout and stderr are read on two threads.
public final class ContentCheck {
    /// ffmpeg's own words (measured by Windows with ffmpeg 8.1 on a clip with garbage in its mdat and a truncated MP4).
    static let decodeWords = [
        "corrupt decoded frame", "decoding error", "error while decoding", "concealing", "invalid nal", "corrupt input packet",
        "packet corrupt", "invalid data found", "missing picture", "error splitting", "partial file", "decode_slice",
        "error submitting packet to decoder",
    ]

    /// What an audio decoder says about a damaged packet besides the words above — only for an audio decoder's line: the
    /// proxy's own AAC encoder writes "[aac @ …]" lines too, which are not about the file.
    static let audioWords = [
        "exceeds limit", "is not allocated", "is not allowed", "reserved bit set", "input buffer exhausted", "invalid pcm packet",
        "header missing", "does not match the sample rate", "overread", "crc mismatch", "invalid frame", "error decoding",
    ]

    static let audioDecoders = [
        "aac", "pcm_", "mp3", "mp2", "ac3", "eac3", "opus", "flac", "alac", "dca", "truehd", "vorbis", "adpcm_", "dolby_e", "wmav", "amr",
    ]

    static let timestampWords = ["non-monotonic", "non monotonically", "invalid timestamp", "timestamps are unset", "invalid dts"]

    private let lock = NSLock()
    private var frame: Int64 = 0
    private var kind: String?
    private var message: String?
    private var at: Int64?

    public init() {}

    /// A line of -progress (stdout).
    public func progress(_ line: String) {
        guard line.hasPrefix("frame="), let n = Int64(line.dropFirst("frame=".count).trimmingCharacters(in: .whitespaces)) else { return }
        lock.lock(); frame = max(frame, n); lock.unlock()
    }

    /// A line of stderr.
    public func stderr(_ line: String) {
        let lower = line.lowercased()
        let audio = ContentCheck.isAudio(lower)
        let k: String?
        if ContentCheck.decodeWords.contains(where: lower.contains) {
            k = audio ? "audio" : "decode"
        } else if audio && ContentCheck.audioWords.contains(where: lower.contains) {
            k = "audio"
        } else if ContentCheck.timestampWords.contains(where: lower.contains) {
            k = "timestamps"
        } else {
            k = nil
        }
        guard let k else { return }
        lock.lock()
        defer { lock.unlock() }
        guard kind == nil else { return }   // the first defect is the one reported
        kind = k
        message = line.trimmingCharacters(in: .whitespaces)
        at = frame
    }

    static func isAudio(_ lower: String) -> Bool {
        if lower.contains("[aist#") { return true }
        guard lower.hasPrefix("["), let r = lower.range(of: " @") else { return false }
        let name = lower[lower.index(after: lower.startIndex)..<r.lowerBound]
        guard !name.isEmpty else { return false }
        return audioDecoders.contains { name.hasPrefix($0) }
    }

    /// A retry of the clip (in software after the hardware decoder failed): what the first attempt saw is forgotten.
    public func reset() {
        lock.lock()
        frame = 0
        kind = nil
        message = nil
        at = nil
        lock.unlock()
    }

    /// The verdict once ffmpeg ended: the first defect line; else the frame count against the file's own (`nb_frames` — a
    /// container that does not say it is not counted, a guess from the duration would warn about good files).
    public func verdict(_ info: ClipInfo) -> ClipContent {
        lock.lock()
        defer { lock.unlock() }
        let expected = info.frames.flatMap { $0 > 0 ? Int64($0) : nil }
        var k = kind, msg = message, place = at
        if k == nil, let e = expected, frame < e - 1 {
            (k, msg, place) = ("end", "decoded \(frame) of \(e) frames", frame)
        } else if k == nil, let e = expected, frame > e + 1 {
            (k, msg, place) = ("frames", "decoded \(frame) of \(e) frames", e)
        }
        var c = ClipContent()
        c.framesDecoded = frame
        c.framesExpected = expected
        guard let k else { return c }
        c.state = ClipContent.warn
        c.kind = k
        c.message = msg
        c.frame = place
        c.timecode = ContentCheck.timecodeAt(info, place ?? 0)
        return c
    }

    static func timecodeAt(_ info: ClipInfo, _ frame: Int64) -> String {
        if let tc = info.timecode, Timecode.isValid(tc), let fps = Timecode.fps(info.frameRate) {
            return Timecode.add(tc, frames: Int(frame), fps: fps)
        }
        return "#\(frame)"
    }
}
