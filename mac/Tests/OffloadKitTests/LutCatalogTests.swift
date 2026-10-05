import XCTest
@testable import OffloadKit

final class LutCatalogTests: XCTestCase {
    func testRepoCatalogHas11ValidLuts() {
        let cat = TestMedia.luts
        XCTAssertEqual(cat.folder, TestMedia.lutFolder)
        XCTAssertEqual(cat.luts.count, 11)
        for l in cat.luts { XCTAssertTrue(LutCatalog.isValidCube(l.path), l.id) }
        XCTAssertEqual(cat.find("sony-slog3-sgamut3cine")?.name, "Sony S-Log3 / S-Gamut3.Cine → Rec.709")
        XCTAssertEqual(cat.find("bmd-film-gen5")?.path, TestMedia.lutFolder + "/Blackmagic_FilmGen5_WideGamutGen5_to_Rec709.cube")
        XCTAssertNil(cat.find("nope"))
        XCTAssertNil(cat.find(nil))
    }

    func testMissingOrBrokenIsEmpty() throws {
        let dir = tempDir()
        XCTAssertTrue(LutCatalog.load(dir.path + "/missing").luts.isEmpty)
        XCTAssertTrue(LutCatalog.load(nil).luts.isEmpty)
        try "{not json".write(toFile: dir.path + "/luts.json", atomically: true, encoding: .utf8)
        XCTAssertTrue(LutCatalog.load(dir.path).luts.isEmpty)
        try #"[{"id":"x","name":"X","maker":"M","file":"x.cube","curve":"c","gamut":"g"}]"#
            .write(toFile: dir.path + "/luts.json", atomically: true, encoding: .utf8)
        XCTAssertTrue(LutCatalog.load(dir.path).luts.isEmpty)   // the listed file does not exist
        XCTAssertNil(LutCatalog.load(dir.path).folder)
    }

    func testEntryWithoutIdIsDroppedTheRestKept() throws {
        let dir = tempDir()
        TestMedia.identityLUT(dir.path + "/a.cube")
        try #"[{"name":"no id","file":"a.cube"},{"id":"a","name":"A","file":"a.cube"}]"#
            .write(toFile: dir.path + "/luts.json", atomically: true, encoding: .utf8)
        XCTAssertEqual(LutCatalog.load(dir.path).luts.map(\.id), ["a"])
    }

    func testIsValidCube() throws {
        let dir = tempDir()
        let ok = dir.path + "/ok.cube"
        try "TITLE \"t\"\nLUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n"
            .write(toFile: ok, atomically: true, encoding: .utf8)
        XCTAssertTrue(LutCatalog.isValidCube(ok))
        let bad = dir.path + "/bad.cube"
        try "LUT_3D_SIZE 2\n0 0 0\n".write(toFile: bad, atomically: true, encoding: .utf8)
        XCTAssertFalse(LutCatalog.isValidCube(bad))
        XCTAssertFalse(LutCatalog.isValidCube(dir.path + "/missing.cube"))
    }

    func testFindFolderAppBundleCLIAndDevBuildOnly() {
        let dir = tempDir()
        func lutsAt(_ folder: String) { TestFS.writeRandom(folder + "/luts.json", size: 2, seed: 1) }
        // Offload.app: Contents/Resources/luts — for the app and for the CLI in Contents/Resources/bin.
        let res = dir.sub("Offload.app", "Contents", "Resources")
        lutsAt(res + "/luts")
        XCTAssertEqual(LutCatalog.findFolder(executable: URL(fileURLWithPath: dir.path + "/Offload.app/Contents/MacOS/Offload"),
                                             resourcePath: res), res + "/luts")
        XCTAssertEqual(LutCatalog.findFolder(executable: URL(fileURLWithPath: res + "/bin/offload"), resourcePath: res + "/bin"),
                       res + "/luts")
        // swift build / swift run: <repo>/mac/.build/<triple>/debug/offload → <repo>/shared/luts.
        let repo = dir.sub("repo")
        lutsAt(repo + "/shared/luts")
        XCTAssertEqual(LutCatalog.findFolder(executable: URL(fileURLWithPath: repo + "/mac/.build/arm64-apple-macosx/debug/offload"),
                                             resourcePath: nil), repo + "/shared/luts")
        // An installed tool never picks up a repo it happens to sit in.
        XCTAssertNil(LutCatalog.findFolder(executable: URL(fileURLWithPath: repo + "/mac/build/bin/offload"), resourcePath: nil))
        XCTAssertNil(LutCatalog.findFolder(executable: nil, resourcePath: nil))
    }
}
