import Foundation

/// Bytes one destination volume needs: the card (with the per-file overhead), proxies, report (spec 2.5).
public struct SpaceNeed: Equatable {
    public var card: Int64
    public var proxy: Int64
    public var report: Int64

    public init(card: Int64, proxy: Int64, report: Int64) {
        self.card = card
        self.proxy = proxy
        self.report = report
    }

    public static let none = SpaceNeed(card: 0, proxy: 0, report: 0)

    /// "cards 380 GB, proxy ~30 GB, report 2 GB" — the parts in Msg.noSpace ("cards") and Msg.spaceLine ("card").
    public func partsText(_ cards: String) -> String {
        var p: [String] = []
        if card > 0 { p.append("\(cards) \(formatBytesShort(card))") }
        if proxy > 0 { p.append("proxy ~\(formatBytesShort(proxy))") }
        if report > 0 { p.append("report \(formatBytesShort(report))") }
        return p.joined(separator: ", ")
    }
    public var total: Int64 { card + proxy + report }

    public static func + (a: SpaceNeed, b: SpaceNeed) -> SpaceNeed {
        SpaceNeed(card: a.card + b.card, proxy: a.proxy + b.proxy, report: a.report + b.report)
    }
}

/// What a job needs besides the card itself, per destination (spec 2.5): proxies (on the first destination, on all with
/// "Copy proxies to all destinations"), the report everywhere, the proxies once more in the proxy folder.
public struct SpaceExtra: Equatable {
    public var proxy: Int64
    public var report: Int64
    public var proxyOnAllDestinations: Bool
    public var proxyFolder: String?

    public init(proxy: Int64, report: Int64, proxyOnAllDestinations: Bool, proxyFolder: String?) {
        self.proxy = proxy
        self.report = report
        self.proxyOnAllDestinations = proxyOnAllDestinations
        self.proxyFolder = proxyFolder
    }

    public func forDestination(_ index: Int, card: Int64) -> SpaceNeed {
        SpaceNeed(card: card, proxy: index == 0 || proxyOnAllDestinations ? proxy : 0, report: report)
    }
}

/// The formulas of spec 2.5 — pure functions, no disk (port of Windows `SpaceEstimator`).
public enum SpaceEstimator {
    /// The card on one destination: its bytes + 64 KiB per file + 64 MiB (the engine's formula since 0.1.0).
    public static func cardBytes(_ totalBytes: Int64, files: Int) -> Int64 { totalBytes + Int64(files) * 64 * 1024 + (64 << 20) }

    /// HTML/PDF with thumbnails, MHL, lists: 32 MiB + 1 MiB per clip.
    public static func reportBytes(clips: Int) -> Int64 { (32 << 20) + Int64(clips) * (1 << 20) }

    /// The source data rate assumed before the copy — on the low side, so the clip is assumed longer and the proxy
    /// bigger (careful): MP4/MOV/MTS 50 Mb/s, MXF 100, BRAW 150, R3D 200, Canon RAW 500, ARRIRAW 1 000.
    public static func sourceMbps(_ clip: Clip) -> Double {
        switch clip.format {
        case .braw: return 150
        case .r3d: return 200
        case .canonRaw: return 500
        case .arriraw: return 1000
        default: return (clip.path as NSString).pathExtension.lowercased() == "mxf" ? 100 : 50
        }
    }

    /// AAC rate of one stream: 64 kb/s a channel, 128–512 kb/s (Windows `Encoders.AacKbps`).
    public static func aacKbps(_ channels: Int) -> Int { min(max(64 * channels, 128), 512) }

    /// Apple's ProRes rates for 1080p30 (Proxy 45, LT 102, 422 147, HQ 220 Mb/s).
    static func proresMbps(_ codec: ProxyCodec) -> Double {
        switch codec {
        case .proresProxy: return 45
        case .proresLT: return 102
        case .prores422: return 147
        default: return 220
        }
    }

    /// The proxy's data rate: H.264/HEVC the set bitrate × 1.1 + each audio stream's AAC rate; ProRes Apple's 1080p30
    /// rate × (height/1080)² + 16-bit PCM at 48 kHz, 0.768 Mb/s a channel of every stream.
    public static func proxyMbps(_ p: ProxySettings, audioStreams: [Int], height: Int) -> Double {
        switch p.codec {
        case .h264, .hevc:
            return Double(p.bitrateMbps) * 1.1 + Double(audioStreams.map(aacKbps).reduce(0, +)) / 1000
        default:
            return proresMbps(p.codec) * pow(Double(height) / 1080, 2) + 0.768 * Double(audioStreams.reduce(0, +))
        }
    }

    /// The proxy's video rate as the encoder is told: H.264/HEVC the set bitrate, ProRes Apple's 1080p30 rate ×
    /// (height/1080)².
    public static func proxyVideoMbps(_ p: ProxySettings, height: Int) -> Double {
        switch p.codec {
        case .h264, .hevc: return Double(p.bitrateMbps)
        default: return proresMbps(p.codec) * pow(Double(height) / 1080, 2)
        }
    }

    /// A clip that a proxy can be made of with these tools (a missing decoder makes none).
    public static func renderable(_ clip: Clip, _ resolved: ExternalTools) -> Bool {
        guard resolved.ffmpeg != nil else { return false }
        switch clip.format {
        case .video: return true
        case .braw: return resolved.brawDecoder != nil
        case .r3d: return resolved.redline != nil
        case .arriraw: return resolved.artCmd != nil
        default: return false
        }
    }

    /// Σ over the clips of length × proxy rate; camera proxies (when skipped) and clips without a decoder count 0.
    /// `info`: after the analysis the real duration, height and audio (spec 2.5 "přepočet před renderem").
    /// `noProxy`: a clip that gets no proxy for another reason (e.g. a lighter original) counts 0.
    public static func proxyBytes(_ clips: [Clip], _ p: ProxySettings, renderable: (Clip) -> Bool,
                                  info: ((Clip) -> ClipInfo?)? = nil, noProxy: ((Clip) -> Bool)? = nil) -> Int64 {
        guard p.enabled else { return 0 }
        var bytes = 0.0
        for c in clips {
            if (c.isCameraProxy && p.skipCameraProxies) || !renderable(c) { continue }
            if noProxy?(c) == true { continue }
            let i = info?(c)
            let seconds = i?.duration ?? Double(c.bytes) * 8 / (sourceMbps(c) * 1e6)
            // Every audio stream becomes a track of the proxy (task 54, Windows R14); before the analysis one stereo stream
            // is assumed, a probed clip without the stream list (a RAW intermediate) has one stream of its channels.
            let audio: [Int] = !p.audio ? [] : i == nil ? [2]
                : !i!.audioStreams.isEmpty ? i!.audioStreams : (i!.audioChannels > 0 ? [i!.audioChannels] : [])
            let height = min(p.height, i?.height ?? p.height)
            bytes += seconds * proxyMbps(p, audioStreams: audio, height: height) * 1e6 / 8
        }
        return Int64(bytes.rounded(.up))
    }

    /// Before the copy, from the card's clip names only (R10).
    public static func extra(_ clips: [Clip], _ p: ProxySettings, _ resolved: ExternalTools) -> SpaceExtra {
        SpaceExtra(proxy: proxyBytes(clips, p, renderable: { renderable($0, resolved) }), report: reportBytes(clips: clips.count),
                   proxyOnAllDestinations: p.copyToAllDestinations, proxyFolder: p.enabled ? p.extraFolder : nil)
    }
}

/// A path that needs room (a destination, the proxy folder), how messages name it, and what it needs.
public struct SpacePlace: Equatable {
    public var path: String
    public var label: String
    public var need: SpaceNeed

    public init(path: String, label: String, need: SpaceNeed) {
        self.path = path
        self.label = label
        self.need = need
    }
}

public enum SpaceSeverity: Int, Comparable {
    case warning, blocking
    public static func < (a: SpaceSeverity, b: SpaceSeverity) -> Bool { a.rawValue < b.rawValue }
}

/// `volume`: which volume (two destinations on one volume add up); `label`: how the messages name it.
public struct VolumeDemand: Equatable {
    public var volume: String
    public var label: String
    public var need: SpaceNeed

    public init(volume: String, label: String, need: SpaceNeed) {
        self.volume = volume
        self.label = label
        self.need = need
    }
}

public struct SpaceProblem: Equatable {
    public let severity: SpaceSeverity
    public let label: String
    public let need: SpaceNeed
    public let free: Int64
    public let text: String
}

/// One volume of a plan: what it needs in total and what it has free.
public struct SpaceLine: Equatable {
    public let label: String
    public let need: SpaceNeed
    public let free: Int64
}

/// Spec 2.5 "Součet": per volume, every demand (selected cards, queued jobs, the rest of running ones) against the free
/// space. Blocking when it does not fit, Warning when less than 5 % of the need would be left (R11). Memory only.
public enum SpacePlanner {
    /// Which volume a path is on, so demands on one volume add up; a path that cannot be asked counts on its own.
    /// Touches the disk — never on the main thread.
    public static func volumeKey(_ path: String) -> String {
        if let dev = IO.device(of: path) { return "dev:\(dev)" }
        return URL(fileURLWithPath: path).standardizedFileURL.path.lowercased()
    }

    public static func check(_ demands: [VolumeDemand], free: [String: Int64]) -> [SpaceProblem] {
        var problems: [SpaceProblem] = []
        for key in orderedKeys(demands) {
            guard let f = free[key] else { continue }   // not known yet: never blocks
            let group = demands.filter { $0.volume == key }
            let need = group.map(\.need).reduce(SpaceNeed.none, +)
            let label = labels(group)
            if need.total > f {
                problems.append(SpaceProblem(severity: .blocking, label: label, need: need, free: f, text: Msg.noSpace(label, total: need.total, parts: need.partsText("cards"), free: f)))
            } else if (f - need.total) * 20 < need.total {
                problems.append(SpaceProblem(severity: .warning, label: label, need: need, free: f, text: Msg.lowSpace(label, left: f - need.total)))
            }
        }
        // Blocking first, otherwise in the order of the volumes.
        return problems.enumerated().sorted { $0.element.severity != $1.element.severity ? $0.element.severity > $1.element.severity : $0.offset < $1.offset }
            .map(\.element)
    }

    /// The one place list both the engine (before the copy) and the job (before the proxies) build: the destinations,
    /// and the proxy folder as a place of its own when it exists. `card` 0 and no report = only the proxies.
    public static func places(_ roots: [String], extra: SpaceExtra?, card: Int64, label: (String) -> String) -> [SpacePlace] {
        var places = roots.enumerated().map { i, root in
            SpacePlace(path: root, label: label(root), need: extra?.forDestination(i, card: card) ?? SpaceNeed(card: card, proxy: 0, report: 0))
        }
        if let extra, let folder = extra.proxyFolder, extra.proxy > 0, ProxyPreflight.isDirectory(folder) {
            places.append(SpacePlace(path: folder, label: label(folder), need: SpaceNeed(card: 0, proxy: extra.proxy, report: 0)))
        }
        return places
    }

    /// Free space of every volume the places are on (`freeOf` unknown = 0, so it blocks rather than risks), then the
    /// check. Touches the disk — never on the main thread.
    public static func checkPlaces(_ places: [SpacePlace], freeOf: (String) -> Int64?) -> (problems: [SpaceProblem], lines: [SpaceLine]) {
        let demands = places.map { VolumeDemand(volume: volumeKey($0.path), label: $0.label, need: $0.need) }
        var free: [String: Int64] = [:]
        for (i, p) in places.enumerated() where free[demands[i].volume] == nil {
            free[demands[i].volume] = freeOf(p.path) ?? 0
        }
        return (check(demands, free: free), lines(demands, free: free))
    }

    /// Per volume with a known free space: the summed need (the CLI's --dry-run breakdown).
    public static func lines(_ demands: [VolumeDemand], free: [String: Int64]) -> [SpaceLine] {
        orderedKeys(demands).compactMap { key in
            guard let f = free[key] else { return nil }
            let group = demands.filter { $0.volume == key }
            return SpaceLine(label: labels(group), need: group.map(\.need).reduce(SpaceNeed.none, +), free: f)
        }
    }

    static func orderedKeys(_ demands: [VolumeDemand]) -> [String] {
        var keys: [String] = []
        for d in demands where !keys.contains(d.volume) { keys.append(d.volume) }
        return keys
    }

    static func labels(_ group: [VolumeDemand]) -> String {
        var out: [String] = []
        for d in group where !out.contains(where: { $0.caseInsensitiveCompare(d.label) == .orderedSame }) { out.append(d.label) }
        return out.joined(separator: ", ")
    }
}
