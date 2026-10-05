import XCTest
@testable import OffloadKit

final class FilterGraphTests: XCTestCase {
    func testThumbnailIsJFIF() {
        XCTAssertEqual(FilterGraph.thumbnail(lutFile: nil, premap: false),
                       "scale=-2:180:flags=bicubic,scale=out_color_matrix=bt601:out_range=pc,format=yuvj420p")
        XCTAssertEqual(FilterGraph.thumbnail(lutFile: "/t/look.cube", premap: false),
                       "scale=-2:180:flags=bicubic,format=rgb48le,lut3d=file=/t/look.cube:interp=tetrahedral,"
                           + "scale=out_color_matrix=bt601:out_range=pc,format=yuvj420p")
        // No LUT → no pre-map either.
        XCTAssertEqual(FilterGraph.thumbnail(lutFile: nil, premap: true), FilterGraph.thumbnail(lutFile: nil, premap: false))
    }

    // Review focus 7 (pre-map): a built-in LUT on a full-range source gets the lutrgb pre-map right before lut3d.
    func testFullRangeSourcePreMappedBeforeBuiltinLut() {
        XCTAssertEqual(FilterGraph.thumbnail(lutFile: "/t/lut-a.cube", premap: true),
                       "scale=-2:180:flags=bicubic,format=rgb48le," + FilterGraph.preMap
                           + ",lut3d=file=/t/lut-a.cube:interp=tetrahedral,scale=out_color_matrix=bt601:out_range=pc,format=yuvj420p")
        XCTAssertTrue(FilterGraph.preMap.hasPrefix("lutrgb=r='"))
        XCTAssertTrue(FilterGraph.preMap.contains("(val*1023/65535-64)/876*65535"))
        XCTAssertEqual(FilterGraph.preMap.components(separatedBy: "clip(").count - 1, 3)   // r, g and b
    }

    func testProxyFullChain() {
        var s = ProxySettings()
        s.frameAspect = 1.66
        s.frameScale = 0.9
        XCTAssertEqual(
            FilterGraph.proxy(s, outHeight: 720, alreadyCropped: false, lutFile: "/t/look.cube", premap: false,
                              burnIn: ["drawbox=x=0:y=ih-42:w=iw:h=42:color=black@0.55:t=fill"]),
            "crop=w='trunc(min(iw*0.9,ih*0.9*1.66)/2)*2':h='trunc(min(iw*0.9,ih*0.9*1.66)/1.66/2)*2',"
                + "scale=-2:720:flags=bicubic,setsar=1,format=rgb48le,lut3d=file=/t/look.cube:interp=tetrahedral,"
                + "drawbox=x=0:y=ih-42:w=iw:h=42:color=black@0.55:t=fill,"
                + "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,setparams=color_primaries=bt709:color_trc=bt709")
    }

    func testProxyBuiltinLutOnFullRangePreMapBeforeLut() {
        XCTAssertEqual(
            FilterGraph.proxy(ProxySettings(), outHeight: 720, alreadyCropped: false, lutFile: "/t/lut-x.cube", premap: true, burnIn: []),
            "scale=-2:720:flags=bicubic,setsar=1,format=rgb48le," + FilterGraph.preMap + ",lut3d=file=/t/lut-x.cube:interp=tetrahedral,"
                + "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,setparams=color_primaries=bt709:color_trc=bt709")
    }

    func testProxyAlreadyCroppedProResNoLut() {
        var s = ProxySettings()
        s.frameAspect = 2.39
        s.codec = .proresProxy
        XCTAssertEqual(
            FilterGraph.proxy(s, outHeight: 1080, alreadyCropped: true, lutFile: nil, premap: false, burnIn: []),
            "scale=-2:1080:flags=bicubic,setsar=1,scale=out_color_matrix=bt709:out_range=tv,format=yuv422p10le,"
                + "setparams=color_primaries=bt709:color_trc=bt709")
    }

    func testRec709EncoderAndContainerTags() {
        XCTAssertEqual(FilterGraph.rec709Args,
                       ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"])
    }
}
