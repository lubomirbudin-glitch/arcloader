import XCTest
@testable import OffloadKit

final class BrawLibraryTests: XCTestCase {
    func testSearchOrderDecoderFolderFirstThenSdkPlayerResolve() {
        XCTAssertEqual(BrawLibrary.searchDirs(decoderDir: "/Applications/Offload.app/Contents/Resources/bin"), [
            "/Applications/Offload.app/Contents/Resources/bin",
            "/Applications/Blackmagic RAW/Blackmagic RAW SDK/Mac/Libraries",
            "/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks",
            "/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Frameworks",
        ])
    }

    func testFindFirstExistingFrameworkFolder() throws {
        let dir = tempDir()
        let a = dir.sub("a"), b = dir.sub("b")
        XCTAssertNil(BrawLibrary.find(in: [a, b]))
        // A plain file of that name is not the framework (a bundle is a folder).
        try "x".write(toFile: a + "/BlackmagicRawAPI.framework", atomically: true, encoding: .utf8)
        XCTAssertNil(BrawLibrary.find(in: [a, b]))
        dir.sub("b", "BlackmagicRawAPI.framework")
        XCTAssertEqual(BrawLibrary.find(in: [a, b]), b + "/BlackmagicRawAPI.framework")
    }

    // Offload and braw-decode must look in the same places (kLibrarySearchPaths in braw-decode.cpp).
    func testSameFoldersAsTheDecoderSource() throws {
        let cpp = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Tools/braw-decode/braw-decode.cpp")
        let source = try String(contentsOf: cpp, encoding: .utf8)
        for dir in BrawLibrary.searchDirs(decoderDir: "@exe") {
            XCTAssertTrue(source.contains("\"\(dir)\""), dir)
        }
    }
}
