import XCTest
@testable import OffloadKit

final class CardRulesTests: XCTestCase {
    let t = Date(timeIntervalSince1970: 1_791_000_000)

    func entry(name: String = "A001", date: String = "2026-10-08", verified: Bool = true) -> HistoryEntry {
        var e = HistoryEntry(id: "x", source: "/Volumes/A001", cardName: name, date: date, finished: t)
        e.success = true
        e.verified = verified
        e.destinations = [HistoryDestination(root: "/Volumes/RAID-x/Zaloha", cardPath: "/Volumes/RAID-x/Zaloha/2026-10-08/A001"),
                          HistoryDestination(root: "/Volumes/SSD-x", cardPath: "/Volumes/SSD-x/2026-10-08/A001")]
        return e
    }

    func insight(_ kind: HistoryMatchKind?, volume: Bool = true, reachable: Bool = true, unfinished: Bool = false,
                 new: [SourceFile] = []) -> CardInsight {
        var i = CardInsight(source: "/Volumes/A001")
        i.fileCount = 2
        if volume { i.volume = CardVolume(identity: "uuid:X", fsSerial: "1", capacity: 2, label: "A001", filesystem: "exFAT") }
        if let kind {
            var e = entry()
            e.unfinished = unfinished
            i.match = HistoryMatch(entry: e, kind: kind, new: new, newBytes: new.reduce(0) { $0 + $1.size }, changed: [], removed: 0)
        }
        i.appendReachable = reachable
        return i
    }

    func testBackedUpPrompt() {
        XCTAssertTrue(BackedUpPrompt.shouldAsk(insight(.identical)))
        XCTAssertTrue(BackedUpPrompt.shouldAsk(insight(.newFiles)))
        XCTAssertFalse(BackedUpPrompt.shouldAsk(insight(.changed)), "its own line says only a full copy")
        XCTAssertFalse(BackedUpPrompt.shouldAsk(insight(.identical, unfinished: true)), "an unfinished backup is no backup")
        XCTAssertFalse(BackedUpPrompt.shouldAsk(insight(nil)))
        let ask = BackedUpPrompt.cardsToAsk(["a", "b", "c"], insight: { $0 == "c" ? nil : self.insight(.identical) }, appendChosen: { $0 == "b" })
        XCTAssertEqual(ask, ["a"])
        let m = HistoryMatch(entry: entry(), kind: .newFiles, new: [SourceFile(relPath: "x", size: 1, modified: t)], newBytes: 1, changed: [], removed: 0)
        XCTAssertEqual(BackedUpPrompt.line(m), "A001 — \(HistoryText.when(t)), Zaloha (RAID-x), SSD-x, verified · new files: 1")
        let one = BackedUpPrompt.build([(m, "A001")], date: "2026-10-08")
        XCTAssertEqual(one.title, "Card already backed up")
        XCTAssertEqual(one.question, "Copy it again in full? A new folder will be created (e.g. A001_2).")
        let two = BackedUpPrompt.build([(m, "A001"), (m, "B001")], date: "2026-10-09")
        XCTAssertEqual(two.title, "Cards already backed up")
        XCTAssertEqual(two.question, "Copy them again in full? New folders will be created (e.g. 2026-10-09/A001, 2026-10-09/B001).")
    }

    func testAppendOffer() {
        let new = [SourceFile(relPath: "C", size: 3_200_000_000, modified: t)]
        XCTAssertTrue(AppendRules.offer(insight(.newFiles, new: new)))
        XCTAssertFalse(AppendRules.offer(insight(.newFiles, volume: false, new: new)), "never for a folder")
        XCTAssertFalse(AppendRules.offer(insight(.newFiles, reachable: false, new: new)), "the backup is not connected")
        XCTAssertFalse(AppendRules.offer(insight(.changed)))
        XCTAssertFalse(AppendRules.offer(insight(.identical)))
        let m = insight(.newFiles, new: new).match!
        XCTAssertEqual(AppendRules.checkboxText(m), "Append new clips only to the earlier backup (1 file · 3.2 GB)")
        XCTAssertEqual(AppendRules.folderText(m.entry), "Folder: 2026-10-08/A001 on 2 destinations")
        XCTAssertEqual(AppendRules.title("A001"), "A001 · append")
    }

    func testNamingResolveSkipsTakenNames() {
        func card(_ key: String, typed: String? = nil, clips: String? = nil) -> CardNameInput {
            var i = CardInsight(source: key)
            if let clips { i.nameSuggestion = NameSuggestion(name: clips, hint: "from the clip names (\(clips)C001…)") }
            return CardNameInput(key: key, typed: typed, insight: i, fallback: "CARD_" + key)
        }
        let history = [HistoryEntry(id: "1", source: "", cardName: "A001", date: "2026-10-08", finished: t)]
        let r = CardNaming.resolve(suggestFromClips: true, cards: [card("x"), card("y", typed: "A002"), card("z", clips: "B004"), card("w")],
                                   history: history, date: "2026-10-08", today: "2026-10-08")
        XCTAssertEqual(r["y"]?.name, "A002")
        XCTAssertNil(r["y"]?.hint)
        XCTAssertEqual(r["z"]?.name, "B004")
        XCTAssertEqual(r["x"]?.name, "A003", "next after A001, past the typed A002")
        XCTAssertEqual(r["x"]?.hint, "next after A002 (today)")
        XCTAssertEqual(r["w"]?.name, "A004")
        let off = CardNaming.resolve(suggestFromClips: false, cards: [card("z", clips: "B004")], history: history, date: "2026-10-08", today: "2026-10-08")
        XCTAssertEqual(off["z"]?.name, "CARD_z")
    }

    func testSpaceCheckForCardAndQueue() {
        var i = CardInsight(source: "/Volumes/A001")
        i.totalBytes = 1000
        i.fileCount = 2
        var p = ProxySettings()
        p.enabled = false
        let targets = [SpaceTarget(root: "/D", volume: "V", label: "D"), SpaceTarget(root: "/E", volume: "V", label: "E")]
        let d = SpaceCheck.forCard(i, usedOnVolume: nil, proxy: p, tools: ExternalTools(), targets: targets, folder: nil)
        XCTAssertEqual(d.map(\.demand.need.card), [SpaceEstimator.cardBytes(1000, files: 2), SpaceEstimator.cardBytes(1000, files: 2)])
        XCTAssertTrue(SpaceCheck.forCard(nil, usedOnVolume: nil, proxy: p, tools: ExternalTools(), targets: targets, folder: nil).isEmpty)
        let need = SpaceNeed(card: 100, proxy: 10, report: 1)
        XCTAssertEqual(SpaceCheck.remaining(need, phase: .copying, written: 40), SpaceNeed(card: 60, proxy: 10, report: 1))
        XCTAssertEqual(SpaceCheck.remaining(need, phase: .proxies, written: 100), SpaceNeed(card: 0, proxy: 10, report: 1))
        XCTAssertEqual(SpaceCheck.remaining(need, phase: .done, written: 0), .none)
    }

    func testSpaceCheckForAppendOnlyConnectedDestinations() {
        let new = [SourceFile(relPath: "C", size: 500, modified: t)]
        let m = insight(.newFiles, new: new).match!
        var p = ProxySettings()
        p.enabled = false
        let d = SpaceCheck.forAppend(m, proxy: p, tools: ExternalTools(),
                                     targets: [SpaceTarget(root: "/Volumes/SSD-x", volume: "S", label: "SSD-x")], folder: nil)
        XCTAssertEqual(d.count, 1)
        XCTAssertEqual(d[0].destination, 1)
        XCTAssertEqual(d[0].demand.need.card, SpaceEstimator.cardBytes(500, files: 1))
    }
}
