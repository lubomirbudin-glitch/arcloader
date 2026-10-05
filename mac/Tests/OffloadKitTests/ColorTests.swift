import XCTest
@testable import OffloadKit

final class ColorTests: XCTestCase {
    private func run(_ ffmpeg: String, _ args: [String]) throws {
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y"] + args)
    }

    /// RGB of a flat first frame (a pixel in the middle), decoded with the given YCbCr matrix and range.
    private func flatRGB(_ ffmpeg: String, _ file: String, matrix: String, range: String, dir: String) throws -> (r: Int, g: Int, b: Int) {
        let raw = dir + "/rgb-\(UUID().uuidString).raw"
        try run(ffmpeg, ["-i", file, "-frames:v", "1", "-vf", "scale=in_color_matrix=\(matrix):in_range=\(range),format=rgb24",
                         "-f", "rawvideo", raw])
        let b = [UInt8](try Data(contentsOf: URL(fileURLWithPath: raw)))
        let i = b.count / 6 * 3
        return (Int(b[i]), Int(b[i + 1]), Int(b[i + 2]))
    }

    private func videoClip(_ rel: String) -> Clip {
        Clip(name: (rel as NSString).deletingPathExtension, path: rel, format: .video, segments: [rel], bytes: 1, isCameraProxy: false)
    }

    /// Pure red twice: RGB (PNG in MOV — like the BRAW decoder's RGB) and BT.709 legal-range YUV (H.264).
    private func redCard(_ ffmpeg: String, _ dir: TempDir) throws -> String {
        let card = dir.sub("card")
        try run(ffmpeg, ["-f", "lavfi", "-i", "color=c=red:s=320x180:d=1:r=25", "-pix_fmt", "rgb24", "-c:v", "png", card + "/rgb.mov"])
        try run(ffmpeg, ["-f", "lavfi", "-i", "color=c=red:s=320x180:d=1:r=25",
                         "-vf", "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p",
                         "-c:v", "libx264", "-colorspace", "bt709", "-color_range", "tv", card + "/yuv.mov"])
        return card
    }

    // How a LUT reaches the renderer: per clip (task 22); custom mode so the renderer copies the .cube.
    private func renderer(_ settings: ProxySettings, lut: String?) -> ProxyRenderer {
        var s = settings
        s.lutPath = lut
        s.lutMode = lut == nil ? .off : .custom
        return ProxyRenderer(settings: s, tools: TestMedia.tools)
    }

    private func render(_ r: ProxyRenderer, _ clip: Clip, card: String, to out: String, lut: String?) throws {
        try r.render(clip, cardPath: card, info: try r.probe(clip, cardPath: card), lut: lut.map(TestMedia.customLut), output: out,
                     progress: { _ in }, isCancelled: { false })
    }

    private func thumbnails(_ r: ProxyRenderer, _ clip: Clip, card: String, lut: String?) throws -> [Thumbnail] {
        try r.thumbnails(clip, cardPath: card, info: try r.probe(clip, cardPath: card), lut: lut.map(TestMedia.customLut))
    }

    // Review focus 8 — pure red through the proxy path (RGB source like the BRAW decoder, and BT.709 video; with and
    // without a LUT; VideoToolbox H.264 and ProRes) is converted with the BT.709 matrix to legal range and tagged
    // BT.709, so a player decoding it as BT.709 sees red. The BT.601 matrix would leave G ≈ 23.
    func testProxyRedIsBT709MatrixAndTagged() throws {
        let (ffmpeg, ffprobe) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = try redCard(ffmpeg, dir)
        let cube = dir.path + "/look.cube"
        TestMedia.identityLUT(cube)
        var failures: [String] = []
        for codec in [ProxyCodec.h264, .proresLT] {
            for name in ["rgb.mov", "yuv.mov"] {
                for withLut in [false, true] {
                    var s = ProxySettings()
                    s.enabled = true
                    s.height = 180
                    s.codec = codec
                    s.burnFilename = false
                    s.burnTimecode = false
                    let lut = withLut ? cube : nil
                    let out = dir.path + "/\(codec.rawValue)-\(name)-\(withLut).mov"
                    try render(renderer(s, lut: lut), videoClip(name), card: card, to: out, lut: lut)
                    let tags = try ToolProcess.run(ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_entries",
                                                             "stream=color_space,color_transfer,color_primaries,color_range",
                                                             "-of", "default=nw=1", out])
                        .split(whereSeparator: \.isNewline).joined(separator: " ")
                    let (red, green, blue) = try flatRGB(ffmpeg, out, matrix: "bt709", range: "tv", dir: dir.path)
                    if tags != "color_range=tv color_space=bt709 color_transfer=bt709 color_primaries=bt709"
                        || red < 240 || green > 8 || blue > 8 {
                        failures.append("\(codec.rawValue) \(name) lut=\(withLut): [\(tags)] rgb=(\(red),\(green),\(blue))")
                    }
                }
            }
        }
        XCTAssertEqual(failures, [])
    }

    // JPEG (JFIF) is BT.601 full range for every browser and PDF viewer: a BT.709 red clip's thumbnail must decode as
    // red that way (keeping the BT.709 matrix gives R ≈ 230).
    func testThumbnailRedIsJFIFBT601() throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = try redCard(ffmpeg, dir)
        let cube = dir.path + "/look.cube"
        TestMedia.identityLUT(cube)
        for name in ["yuv.mov", "rgb.mov"] {
            for lut in [nil, cube] as [String?] {
                let thumbs = try thumbnails(renderer(ProxySettings(), lut: lut), videoClip(name), card: card, lut: lut)
                let jpg = dir.path + "/t-\(name)-\(lut != nil).jpg"
                try XCTUnwrap(thumbs.first).jpeg.write(to: URL(fileURLWithPath: jpg))
                let (red, green, blue) = try flatRGB(ffmpeg, jpg, matrix: "bt601", range: "pc", dir: dir.path)
                XCTAssertTrue(red >= 242 && green <= 8 && blue <= 8, "\(name) lut=\(lut != nil): rgb=(\(red),\(green),\(blue))")
            }
        }
    }
}
