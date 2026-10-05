import XCTest
@testable import OffloadKit

final class BrawBackendTests: XCTestCase {
    func testInfoGammaGamutGenReachClipInfo() throws {
        let c = BRAWBackend.clipInfo(try BRAWBackend.parseInfo(
            #"{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976000,"frames":240,"timecode":"01:02:03:04","camera":"URSA Mini Pro 12K","gamma":"Blackmagic Design Film","gamut":"Blackmagic Design","colorScienceGen":5}"#))
        XCTAssertEqual(c.gamma, "Blackmagic Design Film")
        XCTAssertEqual(c.gamut, "Blackmagic Design")
        XCTAssertEqual(c.colorScienceGen, 5)
        XCTAssertEqual(c.width, 11520)
        XCTAssertEqual(c.frameRate, "24000/1001")
        XCTAssertEqual(c.camera, "URSA Mini Pro 12K")
        // braw-decode before task 27 prints no gamma: still parses.
        let old = BRAWBackend.clipInfo(try BRAWBackend.parseInfo(
            #"{"width":1,"height":1,"sourceWidth":1,"sourceHeight":1,"fps":25,"frames":1,"timecode":"","camera":""}"#))
        XCTAssertNil(old.gamma)
        XCTAssertNil(old.gamut)
        XCTAssertNil(old.colorScienceGen)
        // An empty string is "not reported" (as Windows ToClipInfo).
        let empty = BRAWBackend.clipInfo(try BRAWBackend.parseInfo(
            #"{"width":1,"height":1,"sourceWidth":1,"sourceHeight":1,"fps":25,"frames":1,"timecode":"","camera":"","gamma":"","gamut":""}"#))
        XCTAssertNil(empty.gamma)
        XCTAssertNil(empty.gamut)
    }

    // FORMATY: clips[].info.gamma / gamut / colorScienceGen — written only when known, the same keys as Windows.
    func testClipInfoJSONKeys() throws {
        var c = ClipInfo()
        c.gamma = "Blackmagic Design Film"
        c.gamut = "Blackmagic Design"
        c.colorScienceGen = 5
        let json = try JSONSerialization.jsonObject(with: JSONEncoder().encode(c)) as? [String: Any] ?? [:]
        XCTAssertEqual(json["gamma"] as? String, "Blackmagic Design Film")
        XCTAssertEqual(json["gamut"] as? String, "Blackmagic Design")
        XCTAssertEqual(json["colorScienceGen"] as? Int, 5)
        let none = try JSONSerialization.jsonObject(with: JSONEncoder().encode(ClipInfo())) as? [String: Any] ?? [:]
        XCTAssertNil(none["gamma"])
        XCTAssertNil(none["gamut"])
        XCTAssertNil(none["colorScienceGen"])
        XCTAssertEqual(try JSONDecoder().decode(ClipInfo.self, from: JSONEncoder().encode(c)).colorScienceGen, 5)
    }
}
