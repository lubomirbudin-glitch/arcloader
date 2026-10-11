import Foundation

/// `proxy.skipLighterSources` (task 54, Windows 0.5.1): no proxy when it would not be lighter than the original. A card of
/// already compressed H.264 (1.3–21 Mb/s against a 10 Mb/s proxy) gave proxies only 24 % smaller in total, some 3.7× the
/// original. Only for a long-GOP proxy (H.264/HEVC): a ProRes proxy is chosen for smooth editing, so every clip gets it.
/// Rule: the original is H.264 or HEVC, no bigger than the proxy frame in either axis (height ≤ proxy height, width ≤ its
/// 16:9 width) and its video rate is at most the proxy's set bitrate. Every other original codec — ProRes, DNx, MJPEG,
/// MPEG-2, RAW — is heavy to edit even at a low rate and always gets one; so does an original bigger than the frame,
/// however low its rate (4K long-GOP decoding is what makes the timeline stutter). A rate or size not known = a proxy.
/// Only when no LUT and no frame-lines crop is used — then the proxy is also the look (or the framing) and the original is
/// no stand-in for it; burn-in alone does not count. Port of Windows `LighterSource`.
public enum LighterSource {
    /// The note for the clip (`Msg.proxyLighter`) when it gets no proxy, otherwise nil. `lut`: the LUT the clip's proxy
    /// would get (`ClipEntry.lut`; nil or source "none" = none).
    public static func note(_ clip: Clip, info: ClipInfo, settings p: ProxySettings, lut: LutUse? = nil) -> String? {
        if let lut, lut.source != "none" { return nil }
        if p.frameAspect != nil || p.frameScale < 1 { return nil }
        guard p.skipLighterSources, p.codec == .h264 || p.codec == .hevc, clip.format == .video else { return nil }
        let codec = (info.codec ?? "").lowercased()
        guard codec.hasPrefix("h264") || codec.hasPrefix("hevc") else { return nil }   // the long-GOP codecs
        guard let bits = info.videoBitrate, bits > 0, let w = info.width, let h = info.height else { return nil }
        guard h <= p.height, w <= frameWidth(p.height) else { return nil }
        let source = Double(bits) / 1e6
        let target = SpaceEstimator.proxyVideoMbps(p, height: min(p.height, h) & ~1)
        return source <= target ? Msg.proxyLighter(source, target) : nil
    }

    /// The width of a 16:9 proxy of this height (1080 → 1920).
    static func frameWidth(_ height: Int) -> Int { Int((Double(height) * 16 / 9).rounded()) }
}

extension ClipEntry {
    /// No proxy because the original is lighter than one would be (LighterSource): `skipped` with its note — not a failure,
    /// and not a proxy the job still owes. Also the Czech note older Windows reports wrote.
    public var originalLighter: Bool {
        guard proxyStatus == .skipped, let note = proxyNote else { return false }
        return note.hasPrefix(Msg.proxyLighterPrefix) || note.hasPrefix(Msg.proxyLighterPrefixOld)
    }
}
