using Offload.Core.Space;

namespace Offload.Core;

/// `proxy.skipLighterSources` (0.5.1): no proxy when it would not be lighter than the original. A card of already
/// compressed H.264 (1.3–21 Mb/s against a 10 Mb/s proxy) gave proxies only 24 % smaller in total, some 3.7× the
/// original. Only for a long-GOP proxy (H.264/HEVC): a ProRes proxy is chosen for smooth editing, so every clip gets it
/// (controller ruling). Rule: the original is H.264 or HEVC, no bigger than the proxy frame in either axis (height ≤ proxy
/// height, width ≤ its 16:9 width) and its video rate is at most the proxy's set bitrate. Every other original codec —
/// ProRes, DNx, MJPEG, MPEG-2, RAW — is heavy to edit even at a low rate and always gets one. Ruling: an original bigger
/// than the frame always gets a proxy too, however low its rate (4K long-GOP decoding is what makes the timeline stutter;
/// the proxy is the edit size). A rate or size not known = a proxy. Review: only when no LUT and no frame-lines crop
/// is used — then the proxy is also the look (or the framing) and the original is no stand-in for it; burn-in alone
/// does not count.
public static class LighterSource
{
    /// The note for the clip (Msg.ProxyLighter) when it gets no proxy, otherwise null. `lut`: the LUT the clip's proxy
    /// would get (ClipEntry.Lut; null or source "none" = none).
    public static string? Note(Clip clip, ClipInfo info, ProxySettings p, LutUse? lut = null)
    {
        if (lut is { Source: not "none" } || p.FrameAspect != null || p.FrameScale < 1) return null;
        if (!p.SkipLighterSources || p.Codec is not (ProxyCodec.H264 or ProxyCodec.Hevc) || clip.Format != ClipFormat.Video) return null;
        if (!ProxyRenderer.UseHardwareDecoding(info.Codec)) return null;   // h264… / hevc…: the long-GOP codecs
        if (info.VideoBitrate is not long bits || bits <= 0 || info.Width is not int w || info.Height is not int h) return null;
        if (h > p.Height || w > FrameWidth(p.Height)) return null;
        double source = bits / 1e6;
        double target = SpaceEstimator.ProxyVideoMbps(p, Math.Min(p.Height, h) & ~1);
        return source <= target ? Msg.ProxyLighter(source, target) : null;
    }

    /// The width of a 16:9 proxy of this height (1080 → 1920).
    static int FrameWidth(int height) => (int)Math.Round(height * 16 / 9.0);
}
