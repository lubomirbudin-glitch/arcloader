import XCTest
@testable import OffloadKit

/// The rename to ArcLoader (user decision 2026-10-11, Windows 0.10.0): what users see says ArcLoader; names inside a
/// backup, JSON keys and `tool: "offload"` stay; the old app folder is copied once and never touched; locks stay where an
/// older Offload takes them; old report copies and user LUTs are still read.
final class RenameTests: XCTestCase {
    func testProductNames() {
        XCTAssertEqual(Product.name, "ArcLoader")
        XCTAssertEqual(Product.cli, "arcloader")
        XCTAssertEqual(Product.toolName("offload"), "ArcLoader")
        XCTAssertEqual(Product.toolName("Offload"), "ArcLoader")
        XCTAssertEqual(Product.toolName("Silverstack"), "Silverstack", "another tool's report keeps its name")
    }

    func testReportSaysArcLoaderButKeepsTheFormat() throws {
        let r = Reports.sample()
        let html = HTMLReport.render(r)
        XCTAssertTrue(html.contains("<title>ArcLoader \(r.cardName) – \(r.date)</title>"))
        XCTAssertFalse(html.contains("Offload"), "no old product name anywhere in the card report")
        let json = try JSONSerialization.jsonObject(with: try JSONEncoder().encode(r)) as? [String: Any]
        XCTAssertEqual(json?["tool"] as? String, "offload", "FORMATY: the value never changes")
        XCTAssertEqual(OffloadReport.jsonName(1), "offload_report.json")
        XCTAssertTrue(Msg.verifyHint.contains("arcloader verify"))
        XCTAssertTrue(Msg.summaryTargetIsSource("/x").hasSuffix("ArcLoader never writes to a card."))
    }

    func testLocalReportFolderNewNameOldNamesStillRead() {
        XCTAssertEqual(LocalReports.folderName, "ArcLoader reports")
        let docs = NSHomeDirectory() + "/Documents"
        let roots = SummaryReport.localRoots
        for name in ["ArcLoader reports", "Offload reports", "Offload reporty"] {
            XCTAssertTrue(roots.contains(docs + "/" + name), name)
        }
        XCTAssertEqual(roots.first, LocalReports.root)
        XCTAssertFalse(LocalReports.enabled, "tests never write into the user's Documents")
    }

    // MARK: App folder

    func testSettingsFolderVariablesNewNameFirst() {
        _ = tempDir()   // OFFLOAD_SETTINGS_DIR → the test folder
        XCTAssertEqual(AppSupport.folder, testSettingsDir)
        XCTAssertNil(AppSupport.oldFolder, "with a settings-folder variable the user's folders are never touched")
        XCTAssertEqual(AppSupport.lockFolder, testSettingsDir + "/locks")
        XCTAssertFalse(AppSupport.migrateFromOffload(log: nil), "never migrates with a settings-folder variable")
        let other = tempDir().path
        setenv("ARCLOADER_SETTINGS_DIR", other, 1)
        defer { unsetenv("ARCLOADER_SETTINGS_DIR") }
        XCTAssertEqual(AppSupport.folder, other, "ARCLOADER_SETTINGS_DIR wins over OFFLOAD_SETTINGS_DIR")
        XCTAssertEqual(Locks.folder, other + "/locks")
        // The real folders, as the app would use them without the variables: the new one, the old one for the locks.
        let base = NSHomeDirectory() + "/Library/Application Support"
        XCTAssertEqual(ExternalTools.supportDirs, [base + "/ArcLoader", base + "/Offload"])
        XCTAssertEqual(LutCatalog.userLutFolders, [base + "/ArcLoader/luts", base + "/Offload/luts"])
    }

    private func tree(_ root: String) -> [String: Data] {
        var out: [String: Data] = [:]
        let fm = FileManager.default
        guard let en = fm.enumerator(atPath: root) else { return out }
        while let rel = en.nextObject() as? String {
            let p = root + "/" + rel
            if let dest = try? fm.destinationOfSymbolicLink(atPath: p) {
                out[rel] = Data(("link:" + dest).utf8)
            } else if let d = fm.contents(atPath: p) {
                out[rel] = d
            } else {
                out[rel] = Data("dir".utf8)
            }
        }
        return out
    }

    func testOldFolderIsCopiedOnceNeverChanged() throws {
        let dir = tempDir()
        let old = dir.sub("Application Support", "Offload")
        let new = dir.path + "/Application Support/ArcLoader"
        try Data(#"{"version":1,"entries":[]}"#.utf8).write(to: URL(fileURLWithPath: old + "/history.json"))
        TestFS.writeRandom(old + "/history/20261011T080000Z-1a2b3c4d.files.gz", size: 300, seed: 1)
        TestMedia.identityLUT(dir.sub("Application Support", "Offload", "luts") + "/arri-logc4-awg4.cube")
        TestFS.writeRandom(old + "/arri/ART/art-cmd", size: 2000, seed: 2)
        try FileManager.default.createDirectory(atPath: old + "/bin", withIntermediateDirectories: true)
        try FileManager.default.createSymbolicLink(atPath: old + "/bin/art-cmd", withDestinationPath: "../arri/ART/art-cmd")
        TestFS.writeRandom(old + "/locks/append-x.lock", size: 0, seed: 3)
        try FileManager.default.createSymbolicLink(atPath: old + "/latest", withDestinationPath: "history.json")
        let before = tree(old)

        var lines: [String] = []
        XCTAssertTrue(AppSupport.copyOldFolder(old, to: new, log: { lines.append($0) }))
        XCTAssertEqual(tree(old), before, "the old folder is only read")
        let copied = tree(new)
        XCTAssertEqual(copied["history.json"], before["history.json"])
        XCTAssertEqual(copied["history/20261011T080000Z-1a2b3c4d.files.gz"], before["history/20261011T080000Z-1a2b3c4d.files.gz"])
        XCTAssertEqual(copied["luts/arri-logc4-awg4.cube"], before["luts/arri-logc4-awg4.cube"])
        XCTAssertEqual(copied["latest"], Data("link:history.json".utf8), "a symbolic link stays a link")
        XCTAssertNil(copied["bin"], "tools are searched in the old folder, not copied")
        XCTAssertNil(copied["arri"])
        XCTAssertNil(copied["locks"], "locks stay where an older Offload takes them")
        XCTAssertEqual(lines.count, 1)
        XCTAssertTrue(lines[0].hasPrefix("settings copied from \(old) to \(new)"), lines[0])
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: dir.path + "/Application Support").sorted(), ["ArcLoader", "Offload"],
                       "no temporary folder left")
        XCTAssertEqual(HistoryStore(folder: new).load().count, 0, "the copy reads as a history")

        // Once only: the new folder is never replaced (the user's newer files there win).
        try Data("new".utf8).write(to: URL(fileURLWithPath: new + "/hwtest.json"))
        try Data(#"{"version":1,"entries":[{"x":1}]}"#.utf8).write(to: URL(fileURLWithPath: old + "/history.json"))
        let newBefore = tree(new)
        XCTAssertFalse(AppSupport.copyOldFolder(old, to: new, log: { lines.append($0) }))
        XCTAssertEqual(tree(new), newBefore)
        XCTAssertEqual(lines.count, 1)
    }

    func testNothingToCopyNothingMade() {
        let dir = tempDir()
        XCTAssertFalse(AppSupport.copyOldFolder(dir.path + "/Offload", to: dir.path + "/ArcLoader", log: nil))
        XCTAssertFalse(FileManager.default.fileExists(atPath: dir.path + "/ArcLoader"))
    }

    func testUserLutsFromTheNewFolderThenTheOld() throws {
        let dir = tempDir()
        let newLuts = dir.sub("ArcLoader", "luts"), oldLuts = dir.sub("Offload", "luts")
        TestMedia.identityLUT(oldLuts + "/arri-logc4-awg4.cube")
        TestMedia.identityLUT(oldLuts + "/sony-slog3-sgamut3cine.cube")
        TestMedia.identityLUT(newLuts + "/sony-slog3-sgamut3cine.cube")
        let cat = TestMedia.luts.withUserOverrides([newLuts, oldLuts])
        XCTAssertEqual(cat.find("arri-logc4-awg4")?.path, oldLuts + "/arri-logc4-awg4.cube", "a file put in the old folder still counts")
        XCTAssertEqual(cat.find("sony-slog3-sgamut3cine")?.path, newLuts + "/sony-slog3-sgamut3cine.cube", "the new folder wins")
        XCTAssertTrue(cat.find("arri-logc4-awg4")?.name.hasSuffix("(own file)") == true)
        XCTAssertEqual(cat.find("bmd-film-gen5")?.path, TestMedia.luts.find("bmd-film-gen5")?.path)
    }
}
