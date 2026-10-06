import XCTest
@testable import OffloadKit

/// End to end through Offload with a real clip: probe (gamma), LUT from the gamma, three thumbnails, a short proxy.
/// Needs braw-decode, the library, OFFLOAD_BRAW_SAMPLE and ffmpeg/ffprobe.
final class BrawSampleTests: XCTestCase {
    func testOffloadProbeThumbnailsProxy() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let (ffmpeg, ffprobe) = try TestMedia.requireFFmpeg()
        let name = (sample as NSString).lastPathComponent
        let size = (try FileManager.default.attributesOfItem(atPath: sample)[.size] as? NSNumber)?.int64Value ?? 0
        let clip = Clip(name: (name as NSString).deletingPathExtension, path: name, format: .braw, segments: [name],
                        bytes: size, isCameraProxy: false)
        let card = (sample as NSString).deletingLastPathComponent
        var s = ProxySettings()
        s.enabled = true
        s.height = 540
        let r = ProxyRenderer(settings: s, tools: ExternalTools(ffmpeg: ffmpeg, ffprobe: ffprobe, brawDecoder: exe))
        XCTAssertNil(r.unsupportedReason(clip))
        let info = try r.probe(clip, cardPath: card)
        XCTAssertEqual(info.codec, "Blackmagic RAW")
        XCTAssertNotNil(info.gamma)
        let lut = LutResolver.resolve(clip, info: info, clipFullPath: sample, settings: s, catalog: TestMedia.luts)
        XCTAssertTrue(lut.reason.hasPrefix("Blackmagic RAW: "), lut.reason)   // from the gamma, not the old assumption
        let thumbs = try r.thumbnails(clip, cardPath: card, info: info, lut: lut)
        XCTAssertEqual(thumbs.count, min(3, info.frames ?? 0))
        for t in thumbs { XCTAssertGreaterThan(t.jpeg.count, 1000) }
        let out = tempDir().path + "/proxy.mov"
        try r.render(clip, cardPath: card, info: info, lut: lut, output: out, progress: { _ in }, isCancelled: { false })
        let probed = try FFProbe.probe(ffprobe, out)
        XCTAssertEqual(probed.height, 540)
        XCTAssertEqual(probed.frames, info.frames)
    }
}
