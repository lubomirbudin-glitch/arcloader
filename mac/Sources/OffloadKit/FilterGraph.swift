import Foundation

/// ffmpeg filter graphs for report thumbnails and proxies — port of Windows `FilterGraph.cs` (same chains, same order).
/// The Mac passes absolute paths from the renderer's temp folder: $TMPDIR has no ":", ",", "'" or "\" that a filter
/// graph would need escaped (Windows uses relative names because of "C:\").
enum FilterGraph {
    /// Full-range RGB (the BRAW decoder, video tagged color_range=pc) → the legal-expanded convention the built-in
    /// LUTs expect (code = x·876 + 64). On rgb48le: v16 → ((v16/65535·1023 − 64)/876)·65535, clipped.
    static let preMap = "lutrgb=r='clip((val*1023/65535-64)/876*65535,0,65535)'"
        + ":g='clip((val*1023/65535-64)/876*65535,0,65535)':b='clip((val*1023/65535-64)/876*65535,0,65535)'"

    /// Proxies: explicit BT.709 matrix to legal range — swscale would otherwise pick BT.601 after an RGB step (LUT,
    /// RGB decoder) or keep whatever the source had.
    static let toRec709 = "scale=out_color_matrix=bt709:out_range=tv"
    /// The frames are tagged BT.709 too: ffmpeg 8 takes primaries and transfer for the stream from the frames.
    static let tagRec709 = "setparams=color_primaries=bt709:color_trc=bt709"
    /// Encoder and container tags of every proxy (VideoToolbox and prores_ks write them into the bitstream, MOV into `colr`).
    static let rec709Args = ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"]

    /// Thumbnails: JPEG (JFIF) is BT.601 full range for every browser and PDF viewer, whatever the source was.
    static let toJFIF = "scale=out_color_matrix=bt601:out_range=pc"

    /// RGB48 → (pre-map) → 3D LUT. `premap` only for a built-in LUT on a full-range source.
    static func lut(_ file: String, premap: Bool) -> String {
        "format=rgb48le," + (premap ? preMap + "," : "") + "lut3d=file=\(file):interp=tetrahedral"
    }

    /// Small JPEG for the report, with the LUT (if any) so log footage is readable.
    static func thumbnail(lutFile: String?, premap: Bool) -> String {
        var vf = ["scale=-2:180:flags=bicubic"]
        if let lutFile { vf.append(lut(lutFile, premap: premap)) }
        vf += [toJFIF, "format=yuvj420p"]
        return vf.joined(separator: ",")
    }

    /// Proxy chain: frame-lines crop → scale → LUT → burn-in → BT.709 legal range → pixel format → BT.709 tags.
    static func proxy(_ s: ProxySettings, outHeight: Int, alreadyCropped: Bool, lutFile: String?, premap: Bool,
                      burnIn: [String]) -> String {
        var vf: [String] = []
        if let aspect = s.frameAspect, !alreadyCropped {
            // Crop to the frame lines first, sized from whatever resolution the decoder delivers.
            let sc = min(max(s.frameScale, 0.1), 1.0)
            vf.append("crop=w='trunc(min(iw*\(sc),ih*\(sc)*\(aspect))/2)*2':h='trunc(min(iw*\(sc),ih*\(sc)*\(aspect))/\(aspect)/2)*2'")
        }
        vf += ["scale=-2:\(outHeight):flags=bicubic", "setsar=1"]
        if let lutFile { vf.append(lut(lutFile, premap: premap)) }
        vf += burnIn
        vf += [toRec709, "format=\(s.codec.pixelFormat)", tagRec709]
        return vf.joined(separator: ",")
    }
}
