import XCTest
@testable import OffloadKit

final class HTMLReportTests: XCTestCase {
    private func report(thumbnails: [Thumbnail]) -> OffloadReport {
        var r = Reports.sample()
        var e = ClipEntry(clip: Clip(name: "A001", path: "CLIP/A001.mov", format: .video, segments: ["CLIP/A001.mov"],
                                     bytes: 1000, isCameraProxy: false))
        e.thumbnails = thumbnails
        r.clips = [e]
        return r
    }

    func testThreeThumbnailsWithCaptions() {
        let jpeg = Data([0xFF, 0xD8, 0xFF])
        let html = HTMLReport.render(report(thumbnails: [
            Thumbnail(jpeg: jpeg, label: "01:00:00:00"), Thumbnail(jpeg: jpeg, label: "01:00:02:00"), Thumbnail(jpeg: jpeg, label: "#99"),
        ]))
        XCTAssertTrue(html.contains("<div class=\"thumbs\"><figure><img class=\"thumb\""))
        XCTAssertEqual(html.components(separatedBy: "<figure>").count - 1, 3)
        XCTAssertTrue(html.contains("<figcaption>01:00:02:00</figcaption>"))
        XCTAssertTrue(html.contains("<figcaption>#99</figcaption>"))
        XCTAssertTrue(html.contains("data:image/jpeg;base64,\(jpeg.base64EncodedString())"))
        XCTAssertTrue(html.contains(".thumbs{display:flex;gap:4px}"))
    }

    func testNoThumbnailsPlaceholder() {
        let html = HTMLReport.render(report(thumbnails: []))
        XCTAssertTrue(html.contains("<div class=\"nothumb\"></div>"))
        XCTAssertFalse(html.contains("<figure>"))
    }

    func testPrintShrinksThumbnails() {
        let html = HTMLReport.render(report(thumbnails: []), forPrint: true)
        XCTAssertTrue(html.contains(".thumbs figure img{width:64px;height:36px}"))
        // print rules come after the base rules, so they win (otherwise the PDF table is cut off on the right)
        XCTAssertGreaterThan(html.range(of: ".thumbs figure img{width:64px")!.lowerBound, html.range(of: ".thumb{width:104px")!.lowerBound)
    }

    func testThumbnailsNeverInJSON() throws {
        let data = try report(thumbnails: [Thumbnail(jpeg: Data([1]), label: "x")]).jsonData()
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        let clip = try XCTUnwrap((obj["clips"] as? [[String: Any]])?.first)
        XCTAssertEqual(clip.keys.sorted(), ["clip", "proxyStatus"])
        XCTAssertFalse(String(decoding: data, as: UTF8.self).contains("thumb"))
    }
}
