# Offload pro Mac — dorovnání s Windows verzí: implementační plán

> **Pro vykonavatele (člověk nebo Claude Code na Macu):** postupujte úkol po úkolu, v uvedeném pořadí. U každého
> kroku s testem nejdřív spusťte test a ověřte, že **selže** (nebo se nesestaví), teprve pak implementujte.
> Kód v plánu je kompletní — kde se mění existující funkce, je uvedena celá nová podoba. Kde si plán u Apple API
> není jistý, říká to výslovně a dává příkaz k ověření; výsledek ověření zapište do commitu úkolu.

**Goal:** Dorovnat Mac verzi (Swift, `mac/`) s Windows verzí: tři snímky na klip, pravidla B1–B4, zrušení úloh,
české texty karty úlohy, vysunutí jen kopírované karty, GUI bez blokování na discích, HW diagnostika (USB topologie
přes IOKit, připojení disků přes DiskArbitration, doporučení, měření, řetěz a brzda při kopii, `offload ports`,
okno Hardware z menu ⌘U) — se shodnými výstupy na disku a novým XCTest targetem.

**Doplněk (úkoly 18–25, spec oddíl „Doplněk 2026-10-05“):** funkce, které Windows verze dostala po sepsání tohoto plánu —
barvy proxy BT.709 a náhledy JFIF (18), ochrana proti zaseknutí ffmpeg (19), složka pro proxy navíc (20), vestavěné LUTy
s automatickým výběrem podle kamery (21–22), jejich CLI (23) a GUI (24), LUTy a ikona v balíčku (25). Dokumentace a verze
0.2.0 uzavírají plán úkolem 26 (dřívější úkol 18).

**Doplněk 2 (úkoly 27–31, spec oddíl „Doplněk 2“, D.13–D.20):** Blackmagic RAW po Windows plánu 3 — `braw-decode` vydává
gamu, gamut a generaci barev, pipeline před `OpenClip`, knihovna v hláškách, zavřená roura = kód 0 (27); LUT podle gamy klipu
(28); BRAW jen s knihovnou, limit `--info` 60 s, české poznámky z kódů 3/4/5 (29); náhledy BRAW pod hlídačem, pumpa zavře výstup
dekodéru (30); test celé cesty se vzorkem `.braw` a dokumentace (31). **Pořadí celého plánu: 1 → 25, 27 → 31, nakonec 26.**

**Architecture:** Logika bez UI patří do `OffloadKit` (`Sources/OffloadKit/`, nové podsložky `Desktop/` a `Hardware/`
jako Windows `Offload.Core/Desktop` a `Offload.Core/Hardware`), aby šla testovat XCTestem. Platformní části
(IOKit, DiskArbitration, NSWorkspace) jsou tenké vrstvy v samostatných souborech (`IORegistry.swift`,
`MacUsbScanner.swift`, `MacDriveLinks.swift`, `QueueRules.swift`) nad čistými modely. SwiftUI aplikace
(`Sources/OffloadApp/`) jen napojuje modely, nikdy nečte disk na hlavním vlákně. CLI (`Sources/offload/main.swift`)
používá stejné modely.

**Tech Stack:** Swift 5.9 (swift-tools-version 5.9), SwiftUI + AppKit, macOS 13+, Foundation, IOKit, DiskArbitration,
WebKit (beze změny), swift-argument-parser (beze změny), XCTest, ffmpeg/ffprobe (Homebrew nebo `mac/Tools/bin`). Doplněk: Foundation `XMLParser`
(Sony XML), `renamex_np` (Darwin), `iconutil` (build); sdílené soubory `shared/luts/` a `shared/icon/` (commitnuté — Python
k sestavení netřeba).

**Spec:** [`mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`](../specs/2026-10-05-offload-mac-parity-design.md)

## Global Constraints

- macOS 13+ (`platforms: [.macOS(.v13)]`); API novější než macOS 13 jen za `if #available` — tento plán žádné nepotřebuje.
- Swift 5.9, swift-tools-version 5.9; žádné nové SwiftPM závislosti (IOKit a DiskArbitration jsou systémové frameworky).
- Výstupy na disku shodné s Windows podle `docs/FORMATY.md` (JSON klíče, `.xxh64`, MHL, struktura složek); náhledy se do JSON nezapisují.
- Texty pro uživatele česky a **přesně** jak je uvádí spec (shodné s Windows); hlášky preflightu a CLI kopie anglicky jako dnes.
- Na kartu se nikdy nezapisuje — ani při měření (cíl na svazku karty se neměří).
- UI nikdy nečeká na disk: skeny svazků, `statfs` cílů, identita karty, vysunutí, report, HW test — vše mimo hlavní vlákno.
- Každý úkol přidá řádek do `docs/VYVOJ-MAC.md` (oddíl „<datum práce, `date +%F`> — dorovnání s Windows (plán mac-parity), průběh“, nejnovější nahoře); když tím zmizí rozdíl, upraví ve stejném commitu `docs/ROZDILY.md`.
- Commit po každém úkolu; trailer (`Co-Authored-By: …`) doplní vykonavatel podle svých pravidel.
- Všechny příkazy se spouštějí ze složky `mac/` (pokud není uvedeno jinak).

## Review Focus

Chyby, které by uživatele kously nejvíc — a test, který je hlídá:

1. **Zrušené ověření označí dobrou kopii jako chybnou, nebo naopak karta „lze vysunout“, i když není ověřená.**
   → Úkol 3 `ReportTests.testCancelledVerifyWrittenOrVerifiedFilesAreNotFailures`, `testBuildNotVerifiedCountsWrittenAndVerifiedAsOK`;
   Úkol 4 `JobTests.testStartedJobCancelledDuringRunNeverGoesBackFromFinished` (konec `safeToEject == false`).
2. **„Vysunout kartu“ vysune jinou kartu (výměna ve čtečce) nebo celý disk, na kterém leží ručně přidaná složka.**
   → Úkol 9 `QueueRulesTests.testEjectProblemOtherCardOrGoneRefused`, `testCardEjectProblemRules`, `testCardInDriveOnlyWhenTheScanShowsTheCopiedCard`.
3. **Aktualizace aplikace smaže uložené cíle** (nový klíč `portNames` v nastavení, syntetizovaný `Codable` selže).
   → Úkol 10 `SettingsStoreTests.testOldMacSettingsWithoutPortNamesKeepDestinations`, `testBadSectionKeepsTheRest`.
4. **Popisek snímku neodpovídá snímku** (prostřední o frame vedle, poslední chybí u klipu s delším zvukem).
   → Úkol 5 `FramePositionTests.testSeekSecondsMiddleHalfFrameEarlyLastOneSecondBefore`; Úkol 6
   `ThumbnailTests.testMiddleFrameMatchesCaption24fps`, `testAudioLongerThanVideoStillThree`.
5. **Měření HW zapíše na kartu nebo po zrušení nechá na disku 1 GB dočasný soubor.**
   → Úkol 15 `HwTestTests.testMeasureDestinationOnCardVolumeSkipped`, `testMeasureCancelledDuringWriteStopsAtOnceNoBenchFileLeft`;
   `BenchTests.testWriteCancelledWhileWritingThrowsAndDeletesTempFile`.

Doplněk (úkoly 18–25):

6. **Kopie do složky pro proxy přepíše starší soubor stejného jména** (druhý offload téže karty, stejný den).
   → Úkol 20 `ProxyFolderTests.testExistingNameGetsSuffixNeverOverwrites`, `testAllNamesTakenErrorAfterCap`,
   `testCopyFileWithoutReplaceRefusesAnExistingFile`.
7. **Klip dostane špatný LUT nebo LUT ve špatném rozsahu** (Rec.709 klip s log LUTem, BRAW bez přepočtu rozsahu,
   LUT se tiše neuplatní). → Úkol 21 `LutResolverTests.testSonyRec709NoLut`; úkol 18
   `FilterGraphTests.testFullRangeSourcePreMappedBeforeBuiltinLut`; úkol 22 `LutRenderTests.testNeedsPreMapOnlyBuiltinLutOnFullRangeSource`,
   `testSLog3GreyProxyIs042`.
8. **Proxy s posunutými barvami** (BT.601 matice nebo neoznačený soubor — červená vyjde (255, 23, 0)).
   → Úkol 18 `ColorTests.testProxyRedIsBT709MatrixAndTagged`, `testThumbnailRedIsJFIFBT601`.
9. **Zaseknutý ffmpeg drží frontu navždy** (hardwarový dekodér visí, žádná další karta se nezpracuje).
   → Úkol 19 `FFmpegWatchTests.testStalledWithHwaccelSoftwareRetrySucceedsAndIsLogged`, `testStalledTwiceFailsWithTheNote`,
   `testSoftwareCodecStallsNoRetryNoteWithoutSoftwareClaim`, `testCancelledWhileStuckNoRetry`, `testFeederDataIsProgress`.

Doplněk 2 (úkoly 27–31):

10. **BRAW klip dostane LUT, který k jeho gamě nepatří** (Video / Extended Video s log LUTem, nebo záložní LUT u rozpoznané gamy).
    → Úkol 28 `LutResolverTests.testBrawLutFromGamma`, `testBrawVideoGammaIgnoresFallback`.
11. **Zaseknutá knihovna Blackmagic RAW drží frontu** (`--info`, nebo dekodér v rouře u náhledu) a klip zkouší znovu a znovu.
    → Úkol 29 `BrawBackendTests.testInfoHungKilledAfterTheTimeout`; úkol 30 `BrawFeederTests.testThumbnailDecoderHangsKilledAsStalled`,
    `testThumbnailDecoderHangsOnlyOneStallAttempt`, `testThumbnailInfoHangsOnlyOneAttempt`.

---

### Task 1: Testovací target a pomocníci

**Files:**
- Modify: `mac/Package.swift`
- Create: `mac/Tests/OffloadKitTests/TestSupport.swift`
- Create: `mac/Tests/OffloadKitTests/LayoutTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `Layout.sanitize(_:) -> String`, `Layout.uniqueCardFolder(roots:date:name:) -> String`, `formatBytes(_:) -> String`, `formatRate(_:_:) -> String`, `hex64(_:) -> String`, `ExternalTools.resolved()`, `ToolProcess.run(_:_:cwd:)` (vše existuje).
- Produces (jen testy):
  - `final class TempDir { let path: String; func sub(_ parts: String...) -> String }`
  - `extension XCTestCase { func tempDir() -> TempDir }`
  - `enum TestFS { static func writeRandom(_ path: String, size: Int, seed: UInt64) -> Data; static func files(in dir: String) -> [String] }`
  - `enum TestMedia { static let tools: ExternalTools; static func requireFFmpeg() throws -> (ffmpeg: String, ffprobe: String); static func makeClip(_ path: String, seconds: Double, fps: Int, timecode: String, audio: Bool, width: Int, height: Int) throws; static func identityLUT(_ path: String) }`

- [ ] **Step 1: Ověřit, že testy zatím nejdou spustit**

Run: `swift test`
Expected: chyba typu `error: no tests found; create a target in the 'Tests' directory` (target neexistuje).

- [ ] **Step 2: Přidat testovací target do `mac/Package.swift`** (celý soubor):

```swift
// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Offload",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "offload", targets: ["offload"]),
        .library(name: "OffloadKit", targets: ["OffloadKit"]),
    ],
    dependencies: [
        .package(url: "https://github.com/apple/swift-argument-parser", from: "1.5.0"),
    ],
    targets: [
        .target(name: "CXXHash", path: "Sources/CXXHash"),
        .target(name: "OffloadKit", dependencies: ["CXXHash"]),
        .executableTarget(name: "OffloadApp", dependencies: ["OffloadKit"]),
        .executableTarget(
            name: "offload",
            dependencies: [
                "OffloadKit",
                .product(name: "ArgumentParser", package: "swift-argument-parser"),
            ]
        ),
        .testTarget(name: "OffloadKitTests", dependencies: ["OffloadKit"], path: "Tests/OffloadKitTests"),
    ]
)
```

- [ ] **Step 3: Pomocníci testů** — `mac/Tests/OffloadKitTests/TestSupport.swift`:

```swift
import Foundation
import XCTest
@testable import OffloadKit

/// Unique folder under $TMPDIR/offload-tests. Use `tempDir()` in a test: it is removed after the test
/// (ARC may free a local object before the end of its scope, so the folder is not tied to `deinit`).
final class TempDir {
    let path: String

    init() {
        path = NSTemporaryDirectory() + "offload-tests/" + String(UUID().uuidString.prefix(8))
        try! FileManager.default.createDirectory(atPath: path, withIntermediateDirectories: true)
    }

    /// Creates (if needed) and returns `path/parts…`.
    @discardableResult
    func sub(_ parts: String...) -> String {
        let p = ([path] + parts).joined(separator: "/")
        try! FileManager.default.createDirectory(atPath: p, withIntermediateDirectories: true)
        return p
    }
}

extension XCTestCase {
    /// A fresh folder, removed after the test.
    func tempDir() -> TempDir {
        let dir = TempDir()
        addTeardownBlock { try? FileManager.default.removeItem(atPath: dir.path) }
        return dir
    }
}

enum TestFS {
    /// Deterministic pseudo-random bytes (SplitMix64) written to `path`; parent folders are created.
    @discardableResult
    static func writeRandom(_ path: String, size: Int, seed: UInt64) -> Data {
        try! FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent,
                                                 withIntermediateDirectories: true)
        var state = seed
        var data = Data(count: size)
        data.withUnsafeMutableBytes { (buf: UnsafeMutableRawBufferPointer) in
            var i = 0
            while i < size {
                state &+= 0x9E37_79B9_7F4A_7C15
                var z = state
                z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
                z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
                z ^= z >> 31
                var b = 0
                while b < 8 && i < size {
                    buf[i] = UInt8(truncatingIfNeeded: z >> UInt64(8 * b))
                    b += 1
                    i += 1
                }
            }
        }
        try! data.write(to: URL(fileURLWithPath: path))
        return data
    }

    /// Every regular file under `dir` (hidden ones too), relative paths, sorted.
    static func files(in dir: String) -> [String] {
        guard let en = FileManager.default.enumerator(atPath: dir) else { return [] }
        var out: [String] = []
        while let rel = en.nextObject() as? String {
            var isDir: ObjCBool = false
            if FileManager.default.fileExists(atPath: dir + "/" + rel, isDirectory: &isDir), !isDir.boolValue {
                out.append(rel)
            }
        }
        return out.sorted()
    }
}

enum TestMedia {
    /// ffmpeg / ffprobe / braw-decode: mac/Tools/bin first (like the app build), then the usual search
    /// (~/Library/Application Support/Offload/bin, /opt/homebrew/bin, /usr/local/bin, /usr/bin).
    static let tools: ExternalTools = {
        let mac = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // OffloadKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // mac
        let bin = mac.appendingPathComponent("Tools/bin").path
        func local(_ name: String) -> String? {
            let p = bin + "/" + name
            return FileManager.default.isExecutableFile(atPath: p) ? p : nil
        }
        return ExternalTools(ffmpeg: local("ffmpeg"), ffprobe: local("ffprobe"), brawDecoder: local("braw-decode")).resolved()
    }()

    static func requireFFmpeg() throws -> (ffmpeg: String, ffprobe: String) {
        guard let f = tools.ffmpeg, let p = tools.ffprobe else { throw XCTSkip("ffmpeg/ffprobe not found") }
        return (f, p)
    }

    /// Test pattern with a TC track and optional sine audio, H.264 (libx264) in MOV/MP4 by extension.
    static func makeClip(_ path: String, seconds: Double = 2, fps: Int = 25, timecode: String = "01:00:00:00",
                         audio: Bool = true, width: Int = 1280, height: Int = 720) throws {
        let (ffmpeg, _) = try requireFFmpeg()
        try FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent,
                                                withIntermediateDirectories: true)
        let d = String(seconds)
        var args = ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                    "-f", "lavfi", "-i", "testsrc2=s=\(width)x\(height):r=\(fps):d=\(d)"]
        if audio { args += ["-f", "lavfi", "-i", "sine=d=\(d)"] }
        args += ["-timecode", timecode, "-c:v", "libx264", "-pix_fmt", "yuv420p"]
        if audio { args += ["-c:a", "aac", "-shortest"] }
        args.append(path)
        try ToolProcess.run(ffmpeg, args)
    }

    /// 2×2×2 identity 3D LUT.
    static func identityLUT(_ path: String) {
        try! "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n"
            .write(toFile: path, atomically: true, encoding: .utf8)
    }
}
```

- [ ] **Step 4: První testy (existující chování, ověří, že target funguje)** — `mac/Tests/OffloadKitTests/LayoutTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class LayoutTests: XCTestCase {
    func testSanitize() {
        XCTAssertEqual(Layout.sanitize("Den 1: hlavní"), "Den 1_ hlavní")
        XCTAssertEqual(Layout.sanitize("a/b\\c"), "a_b_c")
        XCTAssertEqual(Layout.sanitize("  "), "CARD")
        XCTAssertEqual(Layout.sanitize(".."), "CARD")
    }

    func testUniqueCardFolderNeverReusesAnExistingOne() {
        let dir = tempDir()
        let d1 = dir.sub("d1"), d2 = dir.sub("d2")
        XCTAssertEqual(Layout.uniqueCardFolder(roots: [d1, d2], date: "2026-10-05", name: "A001"), "A001")
        dir.sub("d2", "2026-10-05", "A001")
        XCTAssertEqual(Layout.uniqueCardFolder(roots: [d1, d2], date: "2026-10-05", name: "A001"), "A001_2")
    }

    func testFormatting() {
        XCTAssertEqual(formatBytes(999), "999 B")
        XCTAssertEqual(formatBytes(1000), "1.00 KB")
        XCTAssertEqual(formatBytes(2_500_000_000), "2.50 GB")
        XCTAssertEqual(formatRate(1_000_000_000, 10), "100 MB/s")
        XCTAssertEqual(formatRate(1, 0), "-")
        XCTAssertEqual(hex64(0xabc), "0000000000000abc")
    }

    func testRandomFilesAreDeterministic() {
        let dir = tempDir()
        let a = TestFS.writeRandom(dir.path + "/x/a.bin", size: 1001, seed: 7)
        let b = TestFS.writeRandom(dir.path + "/x/b.bin", size: 1001, seed: 7)
        XCTAssertEqual(a, b)
        XCTAssertEqual(TestFS.files(in: dir.path), ["x/a.bin", "x/b.bin"])
    }
}
```

- [ ] **Step 5: Spustit testy**

Run: `swift test`
Expected: sestaví se a projde 4 testy (`Executed 4 tests, with 0 failures`).

- [ ] **Step 6: Build všeho a aplikace**

Run: `swift build && scripts/build-app.sh`
Expected: `built build/Offload.app` (kód aplikace se nezměnil).

- [ ] **Step 7: Záznam a commit**

Do `docs/VYVOJ-MAC.md` nad oddíl „2026-10-04 — přesun do `mac/`“ přidat:

```markdown
## <datum práce: výstup `date +%F`> — dorovnání s Windows (plán mac-parity), průběh

Plán: [`mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md`](../mac/docs/superpowers/plans/2026-10-05-offload-mac-parity.md)

- Úkol 1 — testovací target `OffloadKitTests` (XCTest, `swift test`), pomocníci pro dočasné složky, náhodná data a ffmpeg klipy
```

Run: `git add mac/Package.swift mac/Tests docs/VYVOJ-MAC.md && git commit -m "Mac: XCTest target OffloadKitTests"`

---

### Task 2: Preflight B2–B4 a testovací přepínače

**Files:**
- Modify: `mac/Sources/OffloadKit/Engine.swift` (`OffloadOptions`, `OffloadError`, `OffloadEngine.prepare`, `OffloadEngine.run`, nové `OffloadEngine.normalizedRoot`)
- Create: `mac/Tests/OffloadKitTests/PreflightTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `Layout.reportDirName`, `Layout.proxyDirName`, `MHL.dirName`, `IO.device(of:)`.
- Produces:
  - `OffloadOptions.allowSourceVolume: Bool` (internal, výchozí `false`), `OffloadOptions.beforeFileCopy: ((Int) -> Void)?` (internal, výchozí `nil`) — jen pro testy přes `@testable`
  - `OffloadError.isPreflight: Bool`
  - `static func OffloadEngine.normalizedRoot(_ path: String) -> String`
  - `OffloadEngine.prepare(source:destRoots:cardName:date:)` — nová pravidla (signatura beze změny)

- [ ] **Step 1: Napsat padající testy** — `mac/Tests/OffloadKitTests/PreflightTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class PreflightTests: XCTestCase {
    /// Temp folders share one volume: tests allow what a real card + disks never need.
    private func options(allowSameDevice: Bool = true, allowSourceVolume: Bool = true) -> OffloadOptions {
        var o = OffloadOptions()
        o.allowSameDevice = allowSameDevice
        o.allowSourceVolume = allowSourceVolume
        return o
    }

    private func card(_ dir: TempDir) -> String {
        let card = dir.sub("A001")
        TestFS.writeRandom(card + "/CLIP/a.mov", size: 1000, seed: 1)
        return card
    }

    private func assertPreflight(_ message: String, options o: OffloadOptions? = nil,
                                 file: StaticString = #filePath, line: UInt = #line,
                                 _ act: (OffloadEngine) throws -> Void) {
        do {
            try act(OffloadEngine(options: o ?? options()))
            XCTFail("expected a preflight error containing \"\(message)\"", file: file, line: line)
        } catch let e as OffloadError {
            XCTAssertTrue(e.isPreflight, "\(e)", file: file, line: line)
            XCTAssertTrue(e.description.contains(message), "\(e)", file: file, line: line)
        } catch {
            XCTFail("unexpected error \(error)", file: file, line: line)
        }
    }

    func testPlanHasScanDateAndCardFolder() throws {
        let dir = tempDir()
        let card = card(dir)
        let d1 = dir.sub("d1")
        let plan = try OffloadEngine(options: options()).prepare(source: card, destRoots: [d1], cardName: nil, date: "2026-10-04")
        XCTAssertEqual(plan.cardName, "A001")
        XCTAssertEqual(plan.date, "2026-10-04")
        XCTAssertEqual(plan.cardPaths, [d1 + "/2026-10-04/A001"])
        XCTAssertEqual(plan.scan.files.count, 1)
    }

    func testCardNameIsSanitized() throws {
        let dir = tempDir()
        let plan = try OffloadEngine(options: options()).prepare(source: card(dir), destRoots: [dir.sub("d1")],
                                                                 cardName: "Den 1: hlavní", date: "2026-10-04")
        XCTAssertEqual(plan.cardName, "Den 1_ hlavní")
    }

    func testDestinationOnSourceVolumeIsRejected() {
        let dir = tempDir()
        assertPreflight("same volume as the source", options: options(allowSourceVolume: false)) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
    }

    func testTwoDestinationsOnOneVolumeNeedAllowSameDevice() {
        let dir = tempDir()
        assertPreflight("--allow-same-device", options: options(allowSameDevice: false)) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1"), dir.sub("d2")], cardName: nil, date: nil)
        }
    }

    func testSourceThatIsAnEarlierOffloadIsRejected() {
        for folder in ["_REPORT", "_proxy", "ascmhl"] {
            let dir = tempDir()
            let card = card(dir)
            dir.sub("A001", folder)
            assertPreflight("Source looks like an earlier offload") {
                _ = try $0.prepare(source: card, destRoots: [dir.sub("d1")], cardName: nil, date: nil)
            }
        }
    }

    func testAFileNamedLikeAMarkerIsFine() throws {
        let dir = tempDir()
        let card = card(dir)
        TestFS.writeRandom(card + "/_REPORT", size: 10, seed: 2)   // a file, not a folder
        _ = try OffloadEngine(options: options()).prepare(source: card, destRoots: [dir.sub("d1")], cardName: nil, date: nil)
    }

    func testDuplicateDestinationIsRejectedEvenWithAllowSameDevice() {
        let dir = tempDir()
        let d1 = dir.sub("d1")
        assertPreflight("Destination given twice") {
            _ = try $0.prepare(source: self.card(dir), destRoots: [d1, d1 + "/"], cardName: nil, date: nil)
        }
        assertPreflight("Destination given twice") {
            _ = try $0.prepare(source: self.card(dir), destRoots: [d1, dir.path + "/./d1"], cardName: nil, date: nil)
        }
    }

    func testNormalizedRootIgnoresCaseAndTrailingSlash() {
        XCTAssertEqual(OffloadEngine.normalizedRoot("/Volumes/RAID_A/"), OffloadEngine.normalizedRoot("/volumes/raid_a"))
        XCTAssertNotEqual(OffloadEngine.normalizedRoot("/Volumes/RAID_A"), OffloadEngine.normalizedRoot("/Volumes/RAID_B"))
        XCTAssertEqual(OffloadEngine.normalizedRoot("/"), "/")
    }

    func testBadChunkSizeIsRejected() {
        for chunk in [0, -4096, 1000, 4096 + 1, (1 << 30) + 4096] {
            let dir = tempDir()
            var o = options()
            o.chunkSize = chunk
            assertPreflight("ChunkSize", options: o) {
                _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
            }
        }
    }

    func testBufferCountBelowTwoIsRejected() {
        let dir = tempDir()
        var o = options()
        o.bufferCount = 1
        assertPreflight("BufferCount", options: o) {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
    }

    func testBadDateIsRejected() {
        for date in ["2026-1-4", "../evil", "2026-10-04\n", "2026-10-04x"] {
            let dir = tempDir()
            assertPreflight("Date must be") {
                _ = try $0.prepare(source: self.card(dir), destRoots: [dir.sub("d1")], cardName: nil, date: date)
            }
        }
    }

    func testMissingSourceNoDestinationMissingDestinationEmptySource() {
        let dir = tempDir()
        assertPreflight("Source not found") {
            _ = try $0.prepare(source: dir.path + "/no-such-card", destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
        assertPreflight("No destination given") {
            _ = try $0.prepare(source: self.card(dir), destRoots: [], cardName: nil, date: nil)
        }
        assertPreflight("Destination not found") {
            _ = try $0.prepare(source: self.card(dir), destRoots: [dir.path + "/nope"], cardName: nil, date: nil)
        }
        assertPreflight("Source contains no files") {
            _ = try $0.prepare(source: dir.sub("empty"), destRoots: [dir.sub("d1")], cardName: nil, date: nil)
        }
    }
}
```

- [ ] **Step 2: Ověřit, že testy selžou**

Run: `swift test --filter PreflightTests`
Expected: nesestaví se — `value of type 'OffloadOptions' has no member 'allowSourceVolume'`, `'isPreflight'`, `'normalizedRoot'`.

- [ ] **Step 3: Implementace v `Engine.swift`**

`OffloadOptions` (celá nová podoba):

```swift
public struct OffloadOptions {
    /// Read/write block size. Large sequential blocks are what cards and disks like best.
    public var chunkSize = 16 << 20
    /// Blocks in flight. Lets a slower destination lag behind without stalling the card read.
    public var bufferCount = 16
    public var readRetries = 3
    public var verify = true
    /// Files at least this large get F_FULLFSYNC individually; the rest are flushed once at the end.
    public var fullSyncThreshold: Int64 = 256 << 20
    public var allowSameDevice = false
    /// Tests only (internal, reached through @testable): temporary folders share the source's volume.
    var allowSourceVolume = false
    /// Tests only (internal): called on the copy thread before file `i` is copied (a deterministic point to cancel at).
    var beforeFileCopy: ((Int) -> Void)?

    public init() {}
}
```

`OffloadError` — přidat vlastnost:

```swift
public enum OffloadError: Error, CustomStringConvertible {
    case preflight(String)
    case failed(String)

    public var description: String {
        switch self {
        case .preflight(let m), .failed(let m): return m
        }
    }

    public var isPreflight: Bool {
        if case .preflight = self { return true }
        return false
    }
}
```

V `OffloadEngine` nahradit celou `prepare` a přidat `normalizedRoot`:

```swift
    /// Same destination given twice: compared after resolving symlinks, without trailing "/", ignoring case
    /// (APFS and HFS+ are case-insensitive by default, like NTFS on Windows).
    static func normalizedRoot(_ path: String) -> String {
        var s = URL(fileURLWithPath: path).resolvingSymlinksInPath().standardizedFileURL.path
        while s.count > 1 && s.hasSuffix("/") { s.removeLast() }
        return s.lowercased()
    }

    /// Scans the card and checks the destinations before a single byte is written.
    public func prepare(source: String, destRoots: [String], cardName: String?, date: String?) throws -> OffloadPlan {
        guard options.chunkSize > 0, options.chunkSize % 4096 == 0, options.chunkSize <= 1 << 30 else {
            throw OffloadError.preflight("ChunkSize must be a positive multiple of 4096 and at most 1 GiB (got \(options.chunkSize))")
        }
        guard options.bufferCount >= 2 else {
            throw OffloadError.preflight("BufferCount must be at least 2 (got \(options.bufferCount))")
        }
        // \z, not $: "$" would also accept a trailing newline.
        if let date, date.range(of: #"^\d{4}-\d{2}-\d{2}\z"#, options: .regularExpression) == nil {
            throw OffloadError.preflight("Date must be YYYY-MM-DD (got \"\(date)\")")
        }
        let fm = FileManager.default
        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: source, isDirectory: &isDir), isDir.boolValue else {
            throw OffloadError.preflight("Source not found: \(source)")
        }
        guard !destRoots.isEmpty else { throw OffloadError.preflight("No destination given") }
        var seen: [String] = []
        for root in destRoots {
            let n = OffloadEngine.normalizedRoot(root)
            if seen.contains(n) { throw OffloadError.preflight("Destination given twice: \(root)") }
            seen.append(n)
        }
        let top = (try? fm.contentsOfDirectory(atPath: source)) ?? []
        for marker in [Layout.reportDirName, Layout.proxyDirName, MHL.dirName] {
            let found = top.contains { name in
                var d: ObjCBool = false
                return name.caseInsensitiveCompare(marker) == .orderedSame
                    && fm.fileExists(atPath: source + "/" + name, isDirectory: &d) && d.boolValue
            }
            if found {
                throw OffloadError.preflight("Source looks like an earlier offload (has _REPORT/_PROXY/ascmhl) — copy its parent card instead, or remove these folders")
            }
        }
        guard let srcDev = IO.device(of: source) else { throw OffloadError.preflight("Cannot stat source \(source)") }

        var devices: [dev_t] = []
        for root in destRoots {
            guard fm.fileExists(atPath: root, isDirectory: &isDir), isDir.boolValue else {
                throw OffloadError.preflight("Destination not found: \(root)")
            }
            guard fm.isWritableFile(atPath: root) else { throw OffloadError.preflight("Destination not writable: \(root)") }
            guard let dev = IO.device(of: root) else { throw OffloadError.preflight("Cannot stat destination \(root)") }
            if dev == srcDev && !options.allowSourceVolume {
                throw OffloadError.preflight("Destination \(root) is on the same volume as the source")
            }
            if devices.contains(dev) && !options.allowSameDevice {
                throw OffloadError.preflight("Destinations share a volume (\(root)) — that is not a second copy. Use --allow-same-device to override.")
            }
            devices.append(dev)
        }

        let scan = try Scanner.scan(source)
        guard !scan.files.isEmpty else { throw OffloadError.preflight("Source contains no files") }
        let needed = scan.totalBytes + Int64(scan.files.count) * 64 * 1024 + 64 << 20
        for root in destRoots {
            let free = IO.availableBytes(root) ?? 0
            if free < needed {
                throw OffloadError.preflight("Not enough space on \(root): need \(formatBytes(needed)), free \(formatBytes(free))")
            }
        }

        let day = date ?? Layout.today()
        let name = Layout.uniqueCardFolder(roots: destRoots, date: day, name: cardName.map(Layout.sanitize) ?? Layout.defaultCardName(for: source))
        return OffloadPlan(source: source, scan: scan, date: day, cardName: name, destRoots: destRoots)
    }
```

V `run(_:)` ve smyčce souborů přidat volání háčku hned po kontrole zrušení:

```swift
        for (i, f) in files.enumerated() {
            if isCancelled { sourceErrors[i] = "cancelled"; continue }
            options.beforeFileCopy?(i)
            lock.lock(); fileIndex = i; currentFile = f.relPath; lock.unlock()
```

(zbytek smyčky beze změny).

- [ ] **Step 4: Testy projdou**

Run: `swift test --filter PreflightTests`
Expected: `Executed 12 tests, with 0 failures`.

- [ ] **Step 5: Build + ruční kontrola CLI**

Run: `swift build && .build/debug/offload copy /tmp -d /tmp/x -d /tmp/x/ --dry-run; echo "exit $?"`
Expected: `Error: Destination given twice: /tmp/x` a `exit 1` (CLI cesty normalizuje funkcí `abs`, takže koncové `/` zmizí;
ArgumentParser vypíše chybu vyhozenou z `run()`).

Run: `.build/debug/offload copy /tmp -d /tmp/x --date 2026-1-4 --dry-run; echo "exit $?"`
Expected: `Error: Date must be YYYY-MM-DD (got "2026-1-4")`, `exit 1`.

- [ ] **Step 6: Záznam a commit**

`docs/VYVOJ-MAC.md` (pod nadpis průběhu, nahoru): `- Úkol 2 — preflight jako Windows: kontrola voleb (blok, buffery, datum), odmítnutí dvakrát zadaného cíle a zdroje, který je sám offload (B2–B4)`

`docs/ROZDILY.md` — v oddíle „Záměrné rozdíly“ smazat odrážky „Zdroj, který je sám offload“, „Dvakrát zadaný cíl“ a „Kontrola voleb před kopií“ (obě verze se teď chovají stejně).

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: preflight B2-B4 (options, duplicate destination, earlier offload as source)"`

---

### Task 3: Report při zrušeném ověření (B1) a tvar JSON

**Files:**
- Modify: `mac/Sources/OffloadKit/Report.swift` (`OffloadReport.isGood`, `failedFiles`, `build`, nové `jsonData()`, `write`)
- Modify: `mac/Sources/OffloadKit/Job.swift` (`goodDest` v `analyzeAndRender`)
- Create: `mac/Tests/OffloadKitTests/Reports.swift`, `mac/Tests/OffloadKitTests/ReportTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Produces:
  - `static func OffloadReport.isGood(_ state: String, verified: Bool) -> Bool`
  - `public func OffloadReport.jsonData() throws -> Data`
  - `OffloadReport.failedFiles`, `OffloadReport.build(...)` — nová pravidla (signatury beze změny)
  - testy: `enum Reports { static let t0: Date; static func sample(verified: Bool, destError: String?, states: [String]?) -> OffloadReport }`

- [ ] **Step 1: Ukázkový report pro testy** — `mac/Tests/OffloadKitTests/Reports.swift`:

```swift
import Foundation
@testable import OffloadKit

enum Reports {
    static let t0 = ISO8601DateFormatter().date(from: "2026-10-04T08:00:00Z")!

    /// Two-file report for one destination; `destError` makes the second file fail there; `states` overrides both.
    static func sample(verified: Bool = true, destError: String? = nil, states: [String]? = nil) -> OffloadReport {
        let ok = verified ? "verified" : "written"
        let s = states ?? [ok, destError == nil ? ok : "failed"]
        let card = "/Volumes/RAID/2026-10-04/A001"
        return OffloadReport(
            cardName: "A001", date: "2026-10-04", source: "/Volumes/A001", sourceFilesystem: "exfat",
            started: t0, copyFinished: t0.addingTimeInterval(10), finished: t0.addingTimeInterval(15),
            verified: verified, totalFiles: 2, totalBytes: 3000,
            destinations: [OffloadReport.Destination(root: "/Volumes/RAID", cardPath: card, filesystem: "apfs",
                                                     filesOK: destError == nil ? 2 : 1, filesFailed: destError == nil ? 0 : 1)],
            files: [
                OffloadReport.FileEntry(path: "CLIP/A&B.mov", size: 1000, modified: t0, xxh64: "0123456789abcdef",
                                        sourceError: nil, destinations: [s[0]], errors: [nil]),
                OffloadReport.FileEntry(path: "CLIP/c 02.mov", size: 2000, modified: t0, xxh64: "fedcba9876543210",
                                        sourceError: nil, destinations: [s[1]], errors: [destError]),
            ],
            skipped: [".DS_Store"])
    }
}
```

- [ ] **Step 2: Padající testy** — `mac/Tests/OffloadKitTests/ReportTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class ReportTests: XCTestCase {
    func testSuccessAndEject() {
        XCTAssertTrue(Reports.sample().safeToEject)
        let notVerified = Reports.sample(verified: false)
        XCTAssertTrue(notVerified.success)
        XCTAssertFalse(notVerified.safeToEject)
        let failed = Reports.sample(destError: "boom")
        XCTAssertFalse(failed.success)
        XCTAssertEqual(failed.failedFiles.map(\.path), ["CLIP/c 02.mov"])
    }

    // Review focus 1: a cancelled verification leaves files "written" or already "verified" — both are fine then.
    func testCancelledVerifyWrittenOrVerifiedFilesAreNotFailures() {
        for state in ["verified", "written"] {
            let r = Reports.sample(verified: false, states: [state, state])
            XCTAssertTrue(r.success, state)
            XCTAssertFalse(r.safeToEject, state)
            XCTAssertTrue(r.failedFiles.isEmpty, state)
        }
        let mixed = Reports.sample(verified: false, states: ["verified", "failed"])
        XCTAssertEqual(mixed.failedFiles.map(\.path), ["CLIP/c 02.mov"])
    }

    func testVerifiedReportStillNeedsVerifiedState() {
        let r = Reports.sample(verified: true, states: ["verified", "written"])
        XCTAssertEqual(r.failedFiles.map(\.path), ["CLIP/c 02.mov"])
    }

    func testBuildNotVerifiedCountsWrittenAndVerifiedAsOK() {
        let scan = SourceScan(root: "/c", files: [SourceFile(relPath: "a", size: 1, mtime: timespec()),
                                                  SourceFile(relPath: "b", size: 2, mtime: timespec())],
                              dirs: [], skipped: [])
        let plan = OffloadPlan(source: "/c", scan: scan, date: "2026-10-04", cardName: "A001", destRoots: ["/d1"])
        let r = OffloadReport.build(plan: plan, hashes: [1, 2], sourceErrors: [nil, nil],
                                    destResults: [[DestFileResult(state: .verified, error: nil), DestFileResult(state: .written, error: nil)]],
                                    verified: false, started: Date(), copyFinished: Date(), finished: Date())
        XCTAssertEqual(r.destinations[0].filesOK, 2)
        XCTAssertEqual(r.destinations[0].filesFailed, 0)
        XCTAssertTrue(r.success)
        XCTAssertFalse(r.safeToEject)
    }

    func testJSONKeysAreTheSharedFormatSorted() throws {
        let data = try Reports.sample().jsonData()
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        let expected = ["cardName", "clips", "copyFinished", "date", "destinations", "files", "finished", "hashAlgorithm",
                        "skipped", "source", "sourceFilesystem", "started", "tool", "totalBytes", "totalFiles", "verified", "version"]
        XCTAssertEqual(obj.keys.sorted(), expected)
        XCTAssertEqual(obj["started"] as? String, "2026-10-04T08:00:00Z")
        XCTAssertEqual(obj["tool"] as? String, "offload")
        let file = try XCTUnwrap((obj["files"] as? [[String: Any]])?.first)
        XCTAssertEqual(file.keys.sorted(), ["destinations", "errors", "modified", "path", "size", "xxh64"])
        XCTAssertTrue((file["errors"] as? [Any])?.first is NSNull, "null inside arrays stays")
        // The file itself has the keys in sorted order (top level, two-space indent).
        let text = String(decoding: data, as: UTF8.self)
        let positions = expected.compactMap { text.range(of: "\n  \"\($0)\"")?.lowerBound }
        XCTAssertEqual(positions.count, expected.count)
        XCTAssertEqual(positions, positions.sorted())
    }

    func testReadsAReportWrittenByWindows() throws {
        let json = """
        {
          "cardName": "A001", "clips": [], "copyFinished": "2026-10-04T08:00:10Z", "date": "2026-10-04",
          "destinations": [{"cardPath": "D:/dst/2026-10-04/A001", "filesFailed": 0, "filesOK": 1, "filesystem": "NTFS",
                            "root": "D:/dst", "unbuffered": false}],
          "files": [{"destinations": ["verified"], "errors": [null], "modified": "2026-10-04T08:00:00Z",
                     "path": "CLIP/a.mov", "size": 1, "xxh64": "0123456789abcdef"}],
          "finished": "2026-10-04T08:00:15Z", "hashAlgorithm": "xxh64", "proxyEncoder": "h264_nvenc", "skipped": [],
          "source": "E:\\\\", "sourceFilesystem": "exFAT", "started": "2026-10-04T08:00:00Z", "tool": "offload",
          "totalBytes": 1, "totalFiles": 1, "verified": true, "version": "0.1.0-win"
        }
        """
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        let r = try dec.decode(OffloadReport.self, from: Data(json.utf8))
        XCTAssertEqual(r.version, "0.1.0-win")
        XCTAssertEqual(r.destinations.first?.filesOK, 1)
        XCTAssertTrue(r.safeToEject)
    }

    func testHashListIsHashTwoSpacesPath() {
        XCTAssertEqual(Reports.sample().hashList, "0123456789abcdef  CLIP/A&B.mov\nfedcba9876543210  CLIP/c 02.mov\n")
    }
}
```

- [ ] **Step 3: Ověřit selhání**

Run: `swift test --filter ReportTests`
Expected: nesestaví se (`value of type 'OffloadReport' has no member 'jsonData'`). Po dočasném zakomentování JSON testů by `testCancelledVerify…` a `testBuildNotVerified…` selhaly — stávající kód počítá při `verified == false` stav „verified“ jako chybu.

- [ ] **Step 4: Implementace v `Report.swift`**

Nahradit `failedFiles` a přidat `isGood`:

```swift
    /// A file state that counts as good: "verified" — or, when verification did not run to the end
    /// (off or cancelled), also "written". Shared with Windows `OffloadReport.FailedFiles`.
    static func isGood(_ state: String, verified: Bool) -> Bool {
        state == FileState.verified.rawValue || (!verified && state == FileState.written.rawValue)
    }

    public var failedFiles: [FileEntry] {
        files.filter { f in f.sourceError != nil || f.destinations.contains { !OffloadReport.isGood($0, verified: verified) } }
    }
```

V `build(...)` nahradit `let okState…` a počítání:

```swift
    static func build(plan: OffloadPlan, hashes: [UInt64?], sourceErrors: [String?], destResults: [[DestFileResult]],
                      verified: Bool, started: Date, copyFinished: Date, finished: Date) -> OffloadReport {
        let files = plan.scan.files.enumerated().map { i, f in
            FileEntry(path: f.relPath, size: f.size,
                      modified: Date(timeIntervalSince1970: Double(f.mtime.tv_sec) + Double(f.mtime.tv_nsec) / 1e9),
                      xxh64: hashes[i].map(hex64), sourceError: sourceErrors[i],
                      destinations: destResults.map { $0[i].state.rawValue },
                      errors: destResults.map { $0[i].error })
        }
        let dests = plan.destRoots.enumerated().map { d, root in
            Destination(root: root, cardPath: plan.cardPaths[d], filesystem: IO.filesystemType(root),
                        filesOK: destResults[d].filter { isGood($0.state.rawValue, verified: verified) }.count,
                        filesFailed: destResults[d].filter { !isGood($0.state.rawValue, verified: verified) }.count)
        }
        return OffloadReport(cardName: plan.cardName, date: plan.date, source: plan.source,
                             sourceFilesystem: IO.filesystemType(plan.source),
                             started: started, copyFinished: copyFinished, finished: finished, verified: verified,
                             totalFiles: files.count, totalBytes: plan.scan.totalBytes,
                             destinations: dests, files: files, skipped: plan.scan.skipped)
    }
```

Přidat `jsonData()` a použít ho ve `write`:

```swift
    /// Pretty JSON with sorted keys and second-precision ISO 8601 dates — the shared format (docs/FORMATY.md).
    public func jsonData() throws -> Data {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        enc.dateEncodingStrategy = .iso8601
        return try enc.encode(self)
    }

    public func write(cardPath: String, includeHashRecords: Bool) throws {
        let dir = cardPath + "/" + Layout.reportDirName
        try IO.mkdirs(dir)
        var outputs: [(String, Data)] = [
            ("offload_report.json", try jsonData()),
            ("offload_report.txt", Data(summary.utf8)),
            ("\(cardName)_report.html", Data(HTMLReport.render(self).utf8)),
        ]
        if includeHashRecords {
            outputs.append(("\(cardName).xxh64", Data(hashList.utf8)))
        }
        for (name, data) in outputs {
            try IO.writeFileDurably(dir + "/" + name, data)
        }
        if includeHashRecords {
            try MHL.write(report: self, cardPath: cardPath)
        }
    }
```

V `Job.swift` (`analyzeAndRender`) nahradit `let okState…` a `goodDest`:

```swift
        let verified = report.verified
        let fileIndex = Dictionary(uniqueKeysWithValues: report.files.enumerated().map { ($0.element.path, $0.offset) })
        /// First destination that holds a good copy of every segment — clips are read from disk, never from the card.
        func goodDest(_ clip: Clip) -> Int? {
            report.destinations.indices.first { d in
                clip.segments.allSatisfy { seg in
                    fileIndex[seg].map { OffloadReport.isGood(report.files[$0].destinations[d], verified: verified) } ?? false
                }
            }
        }
```

- [ ] **Step 5: Testy projdou**

Run: `swift test --filter ReportTests` → `Executed 7 tests, with 0 failures`. Pak `swift test` (vše zelené).

- [ ] **Step 6: Ruční kontrola**

Cíl musí být na jiném svazku než zdroj (jinak preflight odmítne „same volume as the source“) — použijte USB disk:

Run: `mkdir -p /tmp/t3/card && dd if=/dev/urandom of=/tmp/t3/card/big.bin bs=1m count=2000 && swift build -c release && .build/release/offload copy /tmp/t3/card -d /Volumes/<USB disk>/t3 --no-analyze`
(složku `/Volumes/<USB disk>/t3` předem vytvořit; jakmile stavový řádek ukáže `verify`, stisknout Ctrl+C)
Expected: souhrn `OFFLOAD REPORT — OK`, `Verify: NOT VERIFIED`, žádné „FAILED FILES“; exit kód 1 (fáze `cancelled`).

- [ ] **Step 7: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 3 — report při zrušeném nebo vypnutém ověření nepočítá zapsané/ověřené soubory jako chybné (B1); test tvaru JSON a čtení reportu z Windows`

`docs/ROZDILY.md` — smazat odrážku „Report při zrušeném ověření“.

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: report with cancelled verification counts written files as OK (B1), JSON shape tests"`

---

### Task 4: Zrušení úlohy a bezpečnost vláken `OffloadJob`

**Files:**
- Modify: `mac/Sources/OffloadKit/Job.swift` (`OffloadJob`)
- Create: `mac/Tests/OffloadKitTests/JobTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `OffloadOptions.allowSourceVolume`, `OffloadOptions.beforeFileCopy` (úkol 2).
- Produces:
  - `public convenience init(config: JobConfig)` (dřív designated `init(config:)`, chování stejné)
  - `init(config: JobConfig, options: OffloadOptions)` (internal, pro testy)
  - `static func OffloadJob.engineOptions(_ config: JobConfig) -> OffloadOptions`
  - `public var plan: OffloadPlan? { get }`, `public var report: OffloadReport? { get }` — za zámkem
  - `status()` vrací `.cancelled` pro úlohu zrušenou před startem; `runCopy()` po zrušení nic nedělá

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/JobTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class JobTests: XCTestCase {
    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 64 * 1024
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    // Review focus 1 (queue): cancelled while queued → "Zrušeno", not "Čeká" forever.
    func testCancelledBeforeStartReportsCancelled() {
        let dir = tempDir()
        let job = OffloadJob(config: JobConfig(source: dir.sub("card"), destRoots: [dir.sub("d1")]))
        XCTAssertEqual(job.status().phase, .waiting)
        job.cancel()
        XCTAssertEqual(job.status().phase, .cancelled)
    }

    func testCancelledThenRunCopyStaysCancelledCopiesNothing() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        TestFS.writeRandom(card + "/A.bin", size: 1 << 20, seed: 1)
        let d1 = dir.sub("d1")
        var config = JobConfig(source: card, destRoots: [d1])
        config.analyzeClips = false
        let job = OffloadJob(config: config, options: options())
        job.cancel()
        XCTAssertFalse(job.runCopy())
        XCTAssertEqual(job.status().phase, .cancelled)
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: d1), [])
        XCTAssertNil(job.plan)
    }

    // Review focus 1: a cancelled copy never goes back from a finished phase and is never safe to eject.
    func testStartedJobCancelledDuringRunNeverGoesBackFromFinished() {
        let dir = tempDir()
        let card = dir.sub("card")
        TestFS.writeRandom(card + "/A.bin", size: 8 << 20, seed: 1)
        TestFS.writeRandom(card + "/B.bin", size: 1 << 20, seed: 2)
        let atFirstFile = DispatchSemaphore(value: 0)
        let cancelled = DispatchSemaphore(value: 0)
        var o = options()
        // Deterministic cancel point: the copy thread waits before its first file until the test has cancelled.
        o.beforeFileCopy = { i in
            guard i == 0 else { return }
            atFirstFile.signal()
            _ = cancelled.wait(timeout: .now() + 30)
        }
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.analyzeClips = false
        let job = OffloadJob(config: config, options: o)
        let done = DispatchSemaphore(value: 0)
        Thread.detachNewThread {
            job.runCopy()
            done.signal()
        }
        XCTAssertEqual(atFirstFile.wait(timeout: .now() + 30), .success, "the copy never reached its first file")
        XCTAssertEqual(job.status().phase, .copying)
        job.cancel()
        cancelled.signal()
        var finished = false
        let deadline = Date().addingTimeInterval(60)
        while done.wait(timeout: .now()) == .timedOut {
            guard Date() < deadline else { return XCTFail("the cancelled copy did not end within 60 s") }
            let phase = job.status().phase
            if finished { XCTAssertTrue(phase.isFinished, "phase went back to \(phase) after a finished phase") }
            finished = finished || phase.isFinished
        }
        XCTAssertEqual(job.status().phase, .cancelled)
        XCTAssertFalse(job.status().safeToEject)
        XCTAssertEqual(TestFS.files(in: dir.path + "/d1").filter { $0.hasSuffix(".offload-part") }, [])
    }

    func testRunSuccessEndsDoneAndSafeToEject() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        let a = TestFS.writeRandom(card + "/CLIP/A.bin", size: 300_000, seed: 3)
        TestFS.writeRandom(card + "/B.bin", size: 0, seed: 4)
        var config = JobConfig(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")])
        config.analyzeClips = false
        config.date = "2026-10-05"
        let job = OffloadJob(config: config, options: options())
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertTrue(job.status().safeToEject)
        XCTAssertTrue(report.safeToEject)
        for path in try XCTUnwrap(job.plan).cardPaths {
            XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: path + "/CLIP/A.bin")), a)
            XCTAssertTrue(FileManager.default.fileExists(atPath: path + "/_REPORT/card.xxh64"))
        }
    }

    func testConfigMapsToEngineOptions() {
        var config = JobConfig(source: "/a", destRoots: ["/b"])
        config.chunkMB = 4
        config.buffers = 8
        config.verify = false
        config.allowSameDevice = true
        let o = OffloadJob.engineOptions(config)
        XCTAssertEqual(o.chunkSize, 4 << 20)
        XCTAssertEqual(o.bufferCount, 8)
        XCTAssertFalse(o.verify)
        XCTAssertTrue(o.allowSameDevice)
        XCTAssertFalse(o.allowSourceVolume)
    }
}
```

Pozn. k `testRunSuccess…`: úloha bez analýzy píše i PDF (`writePDFReports`) — v xctestu se tiskne přes WebKit na hlavním vlákně (testy běží na hlavním vlákně). Selhání PDF nikdy nehází, test kontroluje jen fázi a soubory. Kdyby WebKit v xctestu visel, `PDFReport` má limit 60 s; zapište to do commitu.

- [ ] **Step 2: Ověřit selhání**

Run: `swift test --filter JobTests`
Expected: nesestaví se (`extra argument 'options' in call`, `type 'OffloadJob' has no member 'engineOptions'`).

- [ ] **Step 3: Implementace v `Job.swift`** — nahradit začátek třídy `OffloadJob` až po konec `runPost` touto podobou (část `// MARK: - Clips` a dál zůstává):

```swift
/// One card: copy → verify → report → clip analysis → optional proxies. Used by both the CLI and the app.
public final class OffloadJob {
    public let config: JobConfig
    public let engine: OffloadEngine
    private var _plan: OffloadPlan?
    private var _report: OffloadReport?
    private var state = JobStatus()
    private var cancelled = false
    private var started = false
    private let lock = NSLock()

    /// Set on a worker thread, read by the app's main thread — always under the lock.
    public var plan: OffloadPlan? { lock.lock(); defer { lock.unlock() }; return _plan }
    public var report: OffloadReport? { lock.lock(); defer { lock.unlock() }; return _report }

    public convenience init(config: JobConfig) {
        self.init(config: config, options: OffloadJob.engineOptions(config))
    }

    /// Tests pass their own engine options (allowSourceVolume, beforeFileCopy …).
    init(config: JobConfig, options: OffloadOptions) {
        self.config = config
        engine = OffloadEngine(options: options)
    }

    static func engineOptions(_ config: JobConfig) -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = config.chunkMB << 20
        o.bufferCount = config.buffers
        o.verify = config.verify
        o.allowSameDevice = config.allowSameDevice
        return o
    }

    private func setReport(_ r: OffloadReport) {
        lock.lock(); _report = r; lock.unlock()
    }

    @discardableResult
    public func prepare() throws -> OffloadPlan {
        if let p = plan { return p }
        let p = try engine.prepare(source: config.source, destRoots: config.destRoots, cardName: config.cardName, date: config.date)
        lock.lock(); _plan = p; lock.unlock()
        return p
    }

    public func cancel() {
        lock.lock(); cancelled = true; lock.unlock()
        engine.cancel()
    }

    public var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return cancelled }

    public func status() -> JobStatus {
        lock.lock()
        var s = state
        let wasStarted = started
        let wasCancelled = cancelled
        lock.unlock()
        if s.phase == .copying || s.phase == .verifying || s.phase == .flushing || s.phase == .reporting {
            let snap = engine.snapshot()
            s.copy = snap
            switch snap.phase {
            case .copy: s.phase = .copying
            case .flush: s.phase = .flushing
            case .verify: s.phase = .verifying
            case .report, .done: s.phase = .reporting
            }
        }
        // Cancelled while still queued: it never runs, so say so instead of "Čeká" forever.
        if s.phase == .waiting && !wasStarted && wasCancelled { s.phase = .cancelled }
        return s
    }

    private func update(_ f: (inout JobStatus) -> Void) {
        lock.lock(); f(&state); lock.unlock()
    }

    /// Runs the whole job synchronously. Never throws: the outcome is in `status()` and the returned report.
    @discardableResult
    public func run(log: ((String) -> Void)? = nil) -> OffloadReport? {
        guard runCopy() else { return report }
        runPost(log: log)
        return report
    }

    /// Stage 1 — card → disks + verification. Returns true when stage 2 should follow.
    /// After this the card is no longer needed, so the app can start the next card while stage 2 runs.
    @discardableResult
    public func runCopy() -> Bool {
        lock.lock()
        started = true
        let cancelledBeforeStart = cancelled
        if cancelledBeforeStart { state.phase = .cancelled }
        lock.unlock()
        // Cancelled before it began: stay Cancelled, never prepare or copy.
        if cancelledBeforeStart { return false }
        let activity = ProcessInfo.processInfo.beginActivity(
            options: [.userInitiated, .idleSystemSleepDisabled], reason: "Offloading camera card")
        defer { ProcessInfo.processInfo.endActivity(activity) }
        do {
            let plan = try prepare()
            update { $0.phase = .copying }
            let report = try engine.run(plan)
            setReport(report)
            update {
                $0.safeToEject = report.safeToEject
                $0.copySucceeded = report.success
                $0.copy = engine.snapshot()
            }
            if isCancelled {
                update { $0.phase = .cancelled }
                return false
            }
            guard config.analyzeClips || config.proxy.enabled else {
                writePDFReports(report, plan: plan)
                update { $0.phase = .done }
                return false
            }
            update { $0.phase = .analyzing }
            return true
        } catch {
            update {
                $0.phase = .failed
                $0.error = "\(error)"
            }
            return false
        }
    }

    /// Stage 2 — clip analysis, report with thumbnails, optional proxies. Reads only from the destinations.
    public func runPost(log: ((String) -> Void)? = nil) {
        guard let plan, let report else { return }
        let activity = ProcessInfo.processInfo.beginActivity(
            options: [.userInitiated, .idleSystemSleepDisabled], reason: "Rendering proxies")
        defer { ProcessInfo.processInfo.endActivity(activity) }
        let final = analyzeAndRender(plan: plan, report: report, log: log)
        setReport(final)
        update { $0.phase = .reporting }
        writePDFReports(final, plan: plan)
        let finalPhase: JobStatus.Phase = isCancelled ? .cancelled : .done
        update { $0.phase = finalPhase }
    }
```

- [ ] **Step 4: Testy projdou**

Run: `swift test --filter JobTests` → `Executed 5 tests, with 0 failures`; pak `swift test`.

- [ ] **Step 5: Build** — Run: `swift build` (CLI i aplikace používají `OffloadJob(config:)`, beze změny volání).

- [ ] **Step 6: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 4 — úloha zrušená ve frontě hlásí „Zrušeno“ a nic nekopíruje; plán a report úlohy za zámkem (čtení z hlavního vlákna); testovací háček pro deterministické zrušení`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: job cancelled while queued is Cancelled, plan/report under lock"`

---

### Task 5: Timecode, pozice tří snímků a burn-in bez expanze `%`

**Files:**
- Create: `mac/Sources/OffloadKit/Timecode.swift`
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (nový `FramePosition`; `ProxyRenderer.framePositions`, `.label`, `.seekSeconds`; `isValidTimecode`; `burnIn`)
- Create: `mac/Tests/OffloadKitTests/TimecodeTests.swift`, `mac/Tests/OffloadKitTests/FramePositionTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Produces:
  - `public enum Timecode { public static func isValid(_ tc: String?) -> Bool; public static func fps(_ rational: String?) -> Double?; public static func add(_ tc: String, frames: Int, fps: Double) -> String }`
  - `struct FramePosition: Equatable { let seconds: Double; let frame: Int; let fromEnd: Bool }`
  - `static func ProxyRenderer.framePositions(_ info: ClipInfo) -> [FramePosition]`
  - `static func ProxyRenderer.label(_ info: ClipInfo, _ p: FramePosition) -> String`
  - `static func ProxyRenderer.seekSeconds(_ info: ClipInfo, _ p: FramePosition) -> String`
  - `ProxyRenderer.burnIn(clip:info:height:)` — název přes `textfile=…:expansion=none`

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/TimecodeTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class TimecodeTests: XCTestCase {
    func testFps() {
        XCTAssertEqual(Timecode.fps("24000/1001")!, 23.976, accuracy: 0.001)
        XCTAssertEqual(Timecode.fps("25"), 25)
        XCTAssertEqual(Timecode.fps("25/1"), 25)
        XCTAssertNil(Timecode.fps("0/0"))
        XCTAssertNil(Timecode.fps("25/0"))
        XCTAssertNil(Timecode.fps(nil))
        XCTAssertNil(Timecode.fps(""))
        XCTAssertNil(Timecode.fps("x"))
    }

    func testIsValid() {
        XCTAssertTrue(Timecode.isValid("01:00:00:00"))
        XCTAssertTrue(Timecode.isValid("10:00:00;12"))
        XCTAssertFalse(Timecode.isValid("1:00:00:00"))
        XCTAssertFalse(Timecode.isValid(nil))
    }

    func testAddCountsFramesAndWrapsAtMidnight() {
        XCTAssertEqual(Timecode.add("01:00:00:00", frames: 99, fps: 25), "01:00:03:24")
        XCTAssertEqual(Timecode.add("01:00:00:00", frames: 50, fps: 25), "01:00:02:00")
        XCTAssertEqual(Timecode.add("23:59:59:24", frames: 1, fps: 25), "00:00:00:00")
        XCTAssertEqual(Timecode.add("10:00:00;00", frames: 30, fps: 29.97), "10:00:01;00")
    }
}
```

`mac/Tests/OffloadKitTests/FramePositionTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class FramePositionTests: XCTestCase {
    private func info(rate: String? = "25/1", duration: Double? = 4, frames: Int? = 100, tc: String? = nil) -> ClipInfo {
        var i = ClipInfo()
        i.frameRate = rate
        i.duration = duration
        i.frames = frames
        i.timecode = tc
        return i
    }

    func testFirstMiddleLast() {
        XCTAssertEqual(ProxyRenderer.framePositions(info()), [
            FramePosition(seconds: 0, frame: 0, fromEnd: false),
            FramePosition(seconds: 2, frame: 50, fromEnd: false),
            FramePosition(seconds: 3.96, frame: 99, fromEnd: true),
        ])
        let withTC = info(tc: "01:00:00:00")
        XCTAssertEqual(ProxyRenderer.framePositions(withTC).map { ProxyRenderer.label(withTC, $0) },
                       ["01:00:00:00", "01:00:02:00", "01:00:03:24"])
        let noTC = info()
        XCTAssertEqual(ProxyRenderer.label(noTC, ProxyRenderer.framePositions(noTC)[1]), "#50")
    }

    // Review focus 4: the middle frame is sought half a frame early, the last one a second before it.
    func testSeekSecondsMiddleHalfFrameEarlyLastOneSecondBefore() {
        let i = info(rate: "24000/1001", duration: 2.085417, frames: 50)
        XCTAssertEqual(ProxyRenderer.framePositions(i).map { ProxyRenderer.seekSeconds(i, $0) },
                       ["0.000000", "1.021854", "1.043708"])
        let short = info(rate: "25/1", duration: 0.12, frames: 3)
        XCTAssertEqual(ProxyRenderer.framePositions(short).map { ProxyRenderer.seekSeconds(short, $0) },
                       ["0.000000", "0.020000", "0.000000"])
    }

    func testUnknownLengthOrSingleFrameOnlyFirst() {
        XCTAssertEqual(ProxyRenderer.framePositions(ClipInfo()).count, 1)
        XCTAssertEqual(ProxyRenderer.framePositions(info(duration: 0.04, frames: 1)).count, 1)
        XCTAssertEqual(ProxyRenderer.framePositions(info(duration: 0.08, frames: 2)).map(\.frame), [0, 1])
        // Without nb_frames (MKV, some MXF): from duration × fps.
        XCTAssertEqual(ProxyRenderer.framePositions(info(duration: 4, frames: nil)).map(\.frame), [0, 50, 99])
    }

    // Spec 3.3 / Windows A2: "%" in a clip name is literal text, not a drawtext expansion.
    func testBurnInNameIsLiteral() {
        var s = ProxySettings()
        s.burnFilename = true
        s.burnTimecode = false
        let r = ProxyRenderer(settings: s, tools: ExternalTools())
        let clip = Clip(name: "Take 100% A", path: "a.mov", format: .video, segments: ["a.mov"], bytes: 1, isCameraProxy: false)
        let f = r.burnIn(clip: clip, info: ClipInfo(), height: 1080)
        XCTAssertTrue(f.contains { $0.hasPrefix("drawtext=") && $0.contains(":expansion=none:") }, "\(f)")
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "TimecodeTests|FramePositionTests"` → nesestaví se (`cannot find 'Timecode'`, `'FramePosition'`).

- [ ] **Step 3: `mac/Sources/OffloadKit/Timecode.swift`**:

```swift
import Foundation

/// Timecode helpers for the report thumbnails (port of Windows `Timecode.cs`).
public enum Timecode {
    public static func isValid(_ tc: String?) -> Bool {
        guard let tc else { return false }
        return tc.range(of: #"^\d{2}:\d{2}:\d{2}[:;.]\d{2}$"#, options: .regularExpression) != nil
    }

    /// "24000/1001" → 23.976; "25" → 25; nil for anything unusable.
    public static func fps(_ rational: String?) -> Double? {
        guard let r = rational, !r.isEmpty else { return nil }
        let parts = r.split(separator: "/", omittingEmptySubsequences: false)
        guard parts.count <= 2, let num = Double(parts[0]) else { return nil }
        var fps = num
        if parts.count == 2 {
            guard let den = Double(parts[1]), den != 0 else { return nil }
            fps = num / den
        }
        return fps > 0 && fps.isFinite ? fps : nil
    }

    /// Start timecode plus a number of frames, counted at the nominal (rounded) rate and wrapping at 24 h.
    /// Drop-frame timecode is labelled approximately — good enough for a thumbnail caption.
    public static func add(_ tc: String, frames: Int, fps: Double) -> String {
        let nominal = max(1, Int(fps.rounded()))
        let c = Array(tc)
        func num(_ r: Range<Int>) -> Int { Int(String(c[r])) ?? 0 }
        let h = num(0..<2), m = num(3..<5), s = num(6..<8), f = num(9..<11)
        let perDay = 24 * 3600 * nominal
        let total = (((h * 3600 + m * 60 + s) * nominal + f + frames) % perDay + perDay) % perDay
        let ff = total % nominal, secs = total / nominal
        return String(format: "%02d:%02d:%02d", secs / 3600, secs / 60 % 60, secs % 60) + String(c[8]) + String(format: "%02d", ff)
    }
}
```

- [ ] **Step 4: `Proxy.swift`** — nad `public final class ProxyRenderer` přidat:

```swift
/// Where a report thumbnail is taken: frame number, its time, and whether it is the clip's last frame.
struct FramePosition: Equatable {
    let seconds: Double
    let frame: Int
    let fromEnd: Bool
}
```

Do `ProxyRenderer` přidat (např. pod `probe`):

```swift
    /// First, middle and last frame; only the first when the length is unknown or the clip has one frame.
    static func framePositions(_ info: ClipInfo) -> [FramePosition] {
        let first = FramePosition(seconds: 0, frame: 0, fromEnd: false)
        guard let duration = info.duration, duration > 0, let fps = Timecode.fps(info.frameRate) else { return [first] }
        let frames = info.frames ?? Int((duration * fps).rounded())
        guard frames > 1 else { return [first] }
        let middle = frames / 2, last = frames - 1
        var all = [first]
        if middle > 0 { all.append(FramePosition(seconds: Double(middle) / fps, frame: middle, fromEnd: false)) }
        if last > middle { all.append(FramePosition(seconds: Double(last) / fps, frame: last, fromEnd: true)) }
        return all
    }

    /// Caption under a thumbnail: the source timecode of that frame, else its number.
    static func label(_ info: ClipInfo, _ p: FramePosition) -> String {
        if let tc = info.timecode, Timecode.isValid(tc), let fps = Timecode.fps(info.frameRate) {
            return Timecode.add(tc, frames: p.frame, fps: fps)
        }
        return "#\(p.frame)"
    }

    /// Where ffmpeg's input seek (-ss before -i) lands for a thumbnail position. A frame in the middle is sought
    /// half a frame early: its exact time rounded up would skip to the next frame (23.976, 24, 29.97 fps).
    /// The last frame is sought a second before it and decoded to the end of the video stream — never relative
    /// to the container end, which is later when the audio runs longer than the picture.
    static func seekSeconds(_ info: ClipInfo, _ p: FramePosition) -> String {
        var at = p.seconds
        if p.fromEnd {
            at = max(0, p.seconds - 1)
        } else if p.frame > 0, let fps = Timecode.fps(info.frameRate) {
            at = (Double(p.frame) - 0.5) / fps
        }
        return String(format: "%.6f", at)
    }
```

Nahradit `isValidTimecode` (volají ho `render` a `burnIn`):

```swift
    func isValidTimecode(_ tc: String) -> Bool { Timecode.isValid(tc) }
```

V `burnIn` nahradit řádek s názvem klipu:

```swift
        if settings.burnFilename {
            let label = tempDir + "/label-\(UUID().uuidString.prefix(8)).txt"
            try? Data(clip.name.utf8).write(to: URL(fileURLWithPath: label))
            // expansion=none: the name is literal text — a "%" in it would otherwise be a drawtext expansion.
            f.append("drawtext=\(common):textfile=\(label):expansion=none:x=\(margin)")
        }
```

- [ ] **Step 5: Testy projdou** — Run: `swift test --filter "TimecodeTests|FramePositionTests"` → 7 testů (3 + 4), 0 chyb; pak `swift test`.

- [ ] **Step 6: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 5 — timecode a pozice tří snímků (první, prostřední o půl framu dřív, poslední od konce video stopy); burn-in názvu s „%“ už nespadne`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: timecode helpers, thumbnail frame positions, literal clip names in burn-in"`

---

### Task 6: Tři snímky z ffmpeg a BRAW

**Files:**
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (`frameSource(…, forRender:)`, `thumbnailFilter`, `thumbnails`, `thumbnail` jako dočasný obal, `render`)
- Modify: `mac/Sources/OffloadKit/RawBackends.swift` (`BRAWBackend.startFrame`, `BRAWBackend.source`)
- Modify: `mac/Sources/OffloadKit/Report.swift` (nový `Thumbnail`)
- Create: `mac/Tests/OffloadKitTests/ThumbnailTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `FramePosition`, `ProxyRenderer.framePositions/label/seekSeconds` (úkol 5), `TestMedia` (úkol 1).
- Produces:
  - `public struct Thumbnail: Equatable { public let jpeg: Data; public let label: String; public init(jpeg: Data, label: String) }`
  - `public func ProxyRenderer.thumbnails(_ clip: Clip, cardPath: String, info: ClipInfo, applyLUT: Bool = true, isCancelled: () -> Bool = { false }) throws -> [Thumbnail]`
  - `func ProxyRenderer.thumbnailFilter(applyLUT: Bool) -> String`
  - `static func BRAWBackend.startFrame(seconds: Double, fps: Double, frames: Int) -> Int`

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/ThumbnailTests.swift` (ffmpeg testy se bez ffmpeg přeskočí):

```swift
import XCTest
@testable import OffloadKit

final class ThumbnailTests: XCTestCase {
    private func videoClip(_ rel: String) -> Clip {
        Clip(name: ((rel as NSString).lastPathComponent as NSString).deletingPathExtension, path: rel, format: .video,
             segments: [rel], bytes: 1, isCameraProxy: false)
    }

    /// Mean grey level of a JPEG, decoded by ffmpeg.
    private func meanGrey(_ ffmpeg: String, _ jpeg: String, _ dir: String) throws -> Double {
        let raw = dir + "/grey-\(UUID().uuidString).raw"
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", jpeg,
                                     "-f", "rawvideo", "-pix_fmt", "gray", raw])
        let bytes = try Data(contentsOf: URL(fileURLWithPath: raw))
        return bytes.reduce(0.0) { $0 + Double($1) } / Double(max(bytes.count, 1))
    }

    func testThreeJpegsWithTimecodeLabels() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try TestMedia.makeClip(card + "/CLIP/C0001.mov", seconds: 4, timecode: "01:00:00:00")
        TestMedia.identityLUT(dir.path + "/look.cube")
        var s = ProxySettings()
        s.lutPath = dir.path + "/look.cube"
        let r = ProxyRenderer(settings: s, tools: TestMedia.tools)
        let clip = videoClip("CLIP/C0001.mov")
        let thumbs = try r.thumbnails(clip, cardPath: card, info: try r.probe(clip, cardPath: card))
        XCTAssertEqual(thumbs.count, 3)
        for t in thumbs { XCTAssertEqual(Array(t.jpeg.prefix(2)), [0xFF, 0xD8]) }
        XCTAssertEqual(thumbs.map(\.label), ["01:00:00:00", "01:00:02:00", "01:00:03:24"])
    }

    // Review focus 4 (Windows A1): the middle thumbnail is exactly the frame its caption names.
    func testMiddleFrameMatchesCaption24fps() throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        let file = card + "/ramp.mov"
        // Every frame is one flat grey level, 8 steps above the previous one: a frame off is plain to see.
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                                     "-f", "lavfi", "-i", "color=c=black:s=320x180:r=24",
                                     "-vf", "geq=lum='mod(N*8,256)':cb=128:cr=128", "-frames:v", "50",
                                     "-c:v", "libx264", "-pix_fmt", "yuv420p", file])
        let r = ProxyRenderer(settings: ProxySettings(), tools: TestMedia.tools)
        let clip = videoClip("ramp.mov")
        let info = try r.probe(clip, cardPath: card)
        XCTAssertEqual(ProxyRenderer.framePositions(info)[1].frame, 25)
        let thumbs = try r.thumbnails(clip, cardPath: card, info: info)
        XCTAssertEqual(thumbs.count, 3)
        let thumb = dir.path + "/thumb.jpg"
        try thumbs[1].jpeg.write(to: URL(fileURLWithPath: thumb))
        let actual = try meanGrey(ffmpeg, thumb, dir.path)

        // Decode-exact reference: the frame picked by number, through the same thumbnail filter.
        func reference(_ n: Int) throws -> Double {
            let jpg = dir.path + "/ref-\(n).jpg"
            try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", file,
                                         "-vf", "select='eq(n,\(n))',\(r.thumbnailFilter(applyLUT: false))",
                                         "-frames:v", "1", "-q:v", "5", jpg])
            return try meanGrey(ffmpeg, jpg, dir.path)
        }
        let exact = try reference(25), next = try reference(26), previous = try reference(24)
        XCTAssertLessThan(abs(actual - exact), 2,
                          "thumbnail grey \(actual), frame 25 \(exact), frame 26 \(next), frame 24 \(previous)")
    }

    // Review focus 4 (Windows A3): audio longer than the picture — still three, the last one is the last frame.
    func testAudioLongerThanVideoStillThree() throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                                     "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=4", "-f", "lavfi", "-i", "sine=d=6",
                                     "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", card + "/long-audio.mov"])
        let r = ProxyRenderer(settings: ProxySettings(), tools: TestMedia.tools)
        let clip = videoClip("long-audio.mov")
        let info = try r.probe(clip, cardPath: card)
        let thumbs = try r.thumbnails(clip, cardPath: card, info: info)
        XCTAssertEqual(thumbs.count, 3)
        XCTAssertEqual(thumbs[2].label, ProxyRenderer.label(info, ProxyRenderer.framePositions(info)[2]))
    }

    func testSingleFrameClip() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try TestMedia.makeClip(card + "/one.mov", seconds: 0.04, audio: false)
        let r = ProxyRenderer(settings: ProxySettings(), tools: TestMedia.tools)
        let clip = videoClip("one.mov")
        XCTAssertEqual(try r.thumbnails(clip, cardPath: card, info: try r.probe(clip, cardPath: card)).count, 1)
    }

    // Windows A4: cancelled → no thumbnails and no error.
    func testCancelledNoneAndNoError() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try TestMedia.makeClip(card + "/a.mov", seconds: 2)
        let r = ProxyRenderer(settings: ProxySettings(), tools: TestMedia.tools)
        let clip = videoClip("a.mov")
        let info = try r.probe(clip, cardPath: card)
        XCTAssertEqual(try r.thumbnails(clip, cardPath: card, info: info, isCancelled: { true }), [])
    }

    func testNoFFmpegThrows() {
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        XCTAssertThrowsError(try r.thumbnails(videoClip("a.mov"), cardPath: "/nonexistent", info: ClipInfo()))
    }

    // Spec 7: braw-decode's fps is rounded (23.976) while positions use the exact rate — round, never truncate.
    func testBrawStartFrameIsRounded() {
        let exact = 24000.0 / 1001
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 25 / exact, fps: 23.976, frames: 50), 25)
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 49 / exact, fps: 23.976, frames: 50), 49)
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 100, fps: 25, frames: 50), 49)
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 1, fps: 0, frames: 50), 25)   // unknown fps → 25
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 1, fps: 25, frames: 0), 0)
    }
}
```

Pozn.: `ExternalTools()` bez cest — `ProxyRenderer` ho nevolá `resolved()`, takže `tools.ffmpeg == nil` a `thumbnails` hodí „ffmpeg not found“.

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter ThumbnailTests` → nesestaví se (`no member 'thumbnails'`, `'startFrame'`, `cannot find 'Thumbnail'`).

- [ ] **Step 3: `Report.swift`** — nad `public struct ClipEntry` přidat:

```swift
/// One report thumbnail: a small JPEG and its caption (source timecode or "#frame"). Never written to the JSON.
public struct Thumbnail: Equatable {
    public let jpeg: Data
    public let label: String

    public init(jpeg: Data, label: String) {
        self.jpeg = jpeg
        self.label = label
    }
}
```

- [ ] **Step 4: `Proxy.swift`** — nahradit `frameSource` a `thumbnail`, upravit `render`:

```swift
    private func frameSource(_ clip: Clip, cardPath: String, info: ClipInfo, singleFrameAt: Double?, forRender: Bool) throws -> FrameSource {
        let path = cardPath + "/" + clip.path
        if clip.format == .video {
            // Thumbnails decode one frame in software (exact seeking); proxies use VideoToolbox.
            return FrameSource(inputArgs: forRender ? ["-hwaccel", "videotoolbox", "-i", path] : ["-i", path])
        }
        guard let b = backends[clip.format] else { throw ToolError(tool: clip.format.label, status: -1, message: "no decoder") }
        return try b.source(path, info: info, settings: settings, tools: tools, tempDir: tempDir, singleFrameAt: singleFrameAt)
    }

    /// The report thumbnail filter: 180 px high, with the LUT so log footage is readable.
    func thumbnailFilter(applyLUT: Bool) -> String {
        var vf = ["scale=-2:180:flags=bicubic"]
        if hasLUT && applyLUT { vf += ["format=rgb48le", "lut3d=file=\(lutCopy):interp=tetrahedral"] }
        vf.append("format=yuvj420p")
        return vf.joined(separator: ",")
    }

    /// First, middle and last frame as small JPEGs with captions. When `isCancelled` turns true the clip simply
    /// gets no thumbnails. One bad position does not cost the others; if none works, the last error is thrown.
    public func thumbnails(_ clip: Clip, cardPath: String, info: ClipInfo, applyLUT: Bool = true,
                           isCancelled: () -> Bool = { false }) throws -> [Thumbnail] {
        guard let ffmpeg = tools.ffmpeg else { throw ToolError(tool: "ffmpeg", status: -1, message: "ffmpeg not found") }
        let filter = thumbnailFilter(applyLUT: applyLUT)
        let video = clip.format == .video
        var result: [Thumbnail] = []
        var lastError: Error?
        for pos in ProxyRenderer.framePositions(info) {
            if isCancelled() { return [] }
            let out = tempDir + "/thumb-\(UUID().uuidString).jpg"
            defer { unlink(out) }
            do {
                let src = try frameSource(clip, cardPath: cardPath, info: info, singleFrameAt: pos.seconds, forRender: false)
                defer { src.removeIntermediates() }
                var args = ["-hide_banner", "-loglevel", "error", "-y"]
                if video { args += ["-ss", ProxyRenderer.seekSeconds(info, pos)] }
                args += src.inputArgs
                // Last frame: decode the final second and keep overwriting the image — the last write is the last frame.
                args += video && pos.fromEnd ? ["-map", "0:v:0", "-vf", filter, "-update", "1"] : ["-frames:v", "1", "-vf", filter]
                args += ["-q:v", "5", out]
                try runFFmpeg(ffmpeg, args, feeder: src.feeder, duration: nil, progress: nil, isCancelled: isCancelled)
                guard FileManager.default.fileExists(atPath: out) else {
                    throw ToolError(tool: "ffmpeg", status: 0, message: "no frame")
                }
                result.append(Thumbnail(jpeg: try Data(contentsOf: URL(fileURLWithPath: out)), label: ProxyRenderer.label(info, pos)))
            } catch {
                if isCancelled() { return [] }
                lastError = error
            }
        }
        if result.isEmpty, let lastError { throw lastError }
        return result
    }

    /// Single thumbnail — kept only until `OffloadJob` uses `thumbnails` (Task 7 removes it).
    public func thumbnail(_ clip: Clip, cardPath: String, info: ClipInfo, applyLUT: Bool = true) throws -> Data {
        guard let first = try thumbnails(clip, cardPath: cardPath, info: info, applyLUT: applyLUT).first else {
            throw ToolError(tool: "ffmpeg", status: 0, message: "no frame")
        }
        return first.jpeg
    }
```

V `render(...)` změnit řádek se zdrojem:

```swift
        let src = try frameSource(clip, cardPath: cardPath, info: info, singleFrameAt: nil, forRender: true)
```

- [ ] **Step 5: `RawBackends.swift`** — do `BRAWBackend` přidat a použít v `source`:

```swift
    /// The frame braw-decode starts at for a thumbnail `seconds` into the clip. Rounded: `seconds` comes from the
    /// exact rational rate (24000/1001), braw-decode reports a rounded fps (23.976) — truncating would land a frame early.
    static func startFrame(seconds: Double, fps: Double, frames: Int) -> Int {
        let rate = fps > 0 ? fps : 25
        return min(max(0, frames - 1), max(0, Int((seconds * rate).rounded())))
    }
```

V `source(...)` nahradit blok `if let t = singleFrameAt { … }`:

```swift
        if let t = singleFrameAt {
            args += ["--start", String(BRAWBackend.startFrame(seconds: t, fps: dims.fps, frames: dims.frames)), "--count", "1"]
        }
```

- [ ] **Step 6: Testy projdou** — Run: `swift test --filter ThumbnailTests` → 7 testů (s ffmpeg 0 přeskočených, bez ffmpeg 5 přeskočených), 0 chyb. Pak `swift test`.

Pokud `testMiddleFrameMatchesCaption24fps` selže o jeden frame: vypište `ffprobe -show_streams ramp.mov` (`nb_frames`, `avg_frame_rate`, `start_time`) a porovnejte s Windows testem — oprava patří do `seekSeconds`, ne do testu.

- [ ] **Step 7: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 6 — tři snímky z ffmpeg (přesný prostřední, poslední i u delšího zvuku, zrušitelné) a z braw-decode (zaokrouhlený start)`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: three thumbnails per clip (ffmpeg, braw-decode), cancellable"`

---

### Task 7: Tři snímky v reportu a fáze 2

**Files:**
- Modify: `mac/Sources/OffloadKit/Report.swift` (`ClipEntry.thumbnail` → `thumbnails`)
- Modify: `mac/Sources/OffloadKit/HTMLReport.swift` (CSS, buňka náhledů)
- Modify: `mac/Sources/OffloadKit/Job.swift` (`analyzeAndRender`, nové `tryThumbnails`, `copyFile`)
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (smazat dočasný `thumbnail`)
- Create: `mac/Tests/OffloadKitTests/HTMLReportTests.swift`, `mac/Tests/OffloadKitTests/StageTwoTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `ProxyRenderer.thumbnails` (úkol 6), `OffloadReport.isGood` (úkol 3), `OffloadJob.init(config:options:)` (úkol 4).
- Produces: `ClipEntry.thumbnails: [Thumbnail]` (mimo `CodingKeys`); HTML `<div class="thumbs">…<figcaption>`; proxy `done` i při selhání kopie na další cíl (`proxyNote` „kopie na <cesta> selhala: …“).

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/HTMLReportTests.swift`:

```swift
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
        XCTAssertTrue(HTMLReport.render(report(thumbnails: []), forPrint: true).contains(".thumbs figure img{width:96px;height:54px}"))
    }

    func testThumbnailsNeverInJSON() throws {
        let data = try report(thumbnails: [Thumbnail(jpeg: Data([1]), label: "x")]).jsonData()
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        let clip = try XCTUnwrap((obj["clips"] as? [[String: Any]])?.first)
        XCTAssertEqual(clip.keys.sorted(), ["clip", "proxyStatus"])
        XCTAssertFalse(String(decoding: data, as: UTF8.self).contains("thumb"))
    }
}
```

`mac/Tests/OffloadKitTests/StageTwoTests.swift` (ffmpeg; úloha píše i PDF přes WebKit na hlavním vlákně testu):

```swift
import XCTest
@testable import OffloadKit

final class StageTwoTests: XCTestCase {
    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 1 << 20
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    private func proxySettings(_ codec: ProxyCodec = .h264) -> ProxySettings {
        var p = ProxySettings()
        p.enabled = true
        p.height = 360
        p.codec = codec
        return p
    }

    private func sonyCard(_ dir: TempDir) throws -> String {
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/CLIP/C0001.MP4", seconds: 2, timecode: "01:00:00:00")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/CLIP/C0002.MP4", seconds: 2, timecode: "01:00:10:00")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/SUB/C0001S03.MP4", seconds: 2, width: 640, height: 360)
        try "<x/>".write(toFile: card + "/PRIVATE/M4ROOT/MEDIAPRO.XML", atomically: true, encoding: .utf8)
        return card
    }

    func testProxiesAndThreeThumbnailsOnEveryDestination() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        var config = JobConfig(source: try sonyCard(dir), destRoots: [dir.sub("d1"), dir.sub("d2")])
        config.date = "2026-10-05"
        config.proxy = proxySettings()
        config.tools = TestMedia.tools
        let job = OffloadJob(config: config, options: options())
        let report = try XCTUnwrap(job.run())
        let s = job.status()
        XCTAssertEqual(s.phase, .done)
        XCTAssertTrue(s.safeToEject)
        XCTAssertEqual(s.proxiesDone, 2)
        XCTAssertEqual(s.proxiesFailed, 0)
        XCTAssertEqual(report.clips.count, 3)
        for c in report.clips { XCTAssertEqual(c.thumbnails.count, 3, c.clip.name) }
        XCTAssertEqual(report.clips.first { $0.clip.isCameraProxy }?.proxyStatus, .skipped)
        for cardPath in try XCTUnwrap(job.plan).cardPaths {
            XCTAssertTrue(FileManager.default.fileExists(atPath: cardPath + "/_PROXY/C0001.mov"))
            XCTAssertTrue(FileManager.default.fileExists(atPath: cardPath + "/_PROXY/C0002.mov"))
            let html = try String(contentsOfFile: cardPath + "/_REPORT/A001_report.html", encoding: .utf8)
            XCTAssertTrue(html.contains("<figcaption>01:00:10:00</figcaption>"))
        }
        // The hash list only covers the card's files, not proxies.
        let list = try String(contentsOfFile: try XCTUnwrap(job.plan).cardPaths[0] + "/_REPORT/A001.xxh64", encoding: .utf8)
        XCTAssertFalse(list.contains("_PROXY"))
    }

    // Windows A7: a failed copy to another destination keeps the rendered proxy.
    func testProxyCopyFailsRenderedProxyKept() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/C0001.mov", seconds: 1)
        var config = JobConfig(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")])
        config.proxy = proxySettings()
        config.tools = TestMedia.tools
        let job = OffloadJob(config: config, options: options())
        XCTAssertTrue(job.runCopy())
        let paths = try XCTUnwrap(job.plan).cardPaths
        try FileManager.default.createDirectory(atPath: paths[1] + "/_PROXY/C0001.mov", withIntermediateDirectories: true)  // blocks the copy on d2
        var log: [String] = []
        job.runPost { log.append($0) }

        let s = job.status()
        XCTAssertEqual(s.phase, .done)
        XCTAssertEqual(s.proxiesDone, 1)
        XCTAssertEqual(s.proxiesFailed, 0)
        let clip = try XCTUnwrap(job.report?.clips.first)
        XCTAssertEqual(clip.proxyStatus, .done)
        XCTAssertEqual(clip.proxyPath, "_PROXY/C0001.mov")
        XCTAssertTrue(clip.proxyNote?.hasPrefix("kopie na \(paths[1]) selhala: ") == true, clip.proxyNote ?? "nil")
        XCTAssertTrue(log.contains { $0.contains("C0001") && $0.contains(paths[1]) })
        XCTAssertTrue(FileManager.default.fileExists(atPath: paths[0] + "/_PROXY/C0001.mov"))
        XCTAssertEqual(TestFS.files(in: paths[1] + "/_PROXY").filter { $0.hasSuffix(".part") }, [])
    }

    func testNoVerifyProxiesFromWrittenCopy() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/clip.mov", seconds: 1)
        var o = options()
        o.verify = false
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.verify = false
        config.proxy = proxySettings()
        config.tools = TestMedia.tools
        let job = OffloadJob(config: config, options: o)
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertFalse(job.status().safeToEject)
        XCTAssertEqual(report.clips.first?.proxyStatus, .done)
    }

    // Windows C1: HEVC proxies are tagged hvc1 (QuickTime / Resolve need it).
    func testHevcProxyIsTaggedHvc1() throws {
        let (_, ffprobe) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try TestMedia.makeClip(card + "/a.mov", seconds: 1)
        let r = ProxyRenderer(settings: proxySettings(.hevc), tools: TestMedia.tools)
        let clip = Clip(name: "a", path: "a.mov", format: .video, segments: ["a.mov"], bytes: 1, isCameraProxy: false)
        let out = card + "/_PROXY/a.mov"
        try r.render(clip, cardPath: card, info: try r.probe(clip, cardPath: card), output: out, progress: { _ in }, isCancelled: { false })
        let tag = try ToolProcess.run(ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_entries",
                                                "stream=codec_tag_string", "-of", "csv=p=0", out])
        XCTAssertEqual(tag.trimmingCharacters(in: .whitespacesAndNewlines), "hvc1")
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "HTMLReportTests|StageTwoTests"` → nesestaví se (`value of type 'ClipEntry' has no member 'thumbnails'`).

- [ ] **Step 3: `Report.swift`** — v `ClipEntry` nahradit vlastnost `thumbnail`:

```swift
    /// First, middle and last frame (JPEG + caption); embedded in the HTML/PDF report only.
    public var thumbnails: [Thumbnail] = []
```

(`CodingKeys` zůstává beze změny.)

- [ ] **Step 4: `HTMLReport.swift`**

`printCSS` (celý):

```swift
    static let printCSS = "body{background:#fff;font-size:11px}main{padding:0;max-width:none}th,td{padding:5px 7px}tr{break-inside:avoid}h2{break-after:avoid}"
        + ".stat b{font-size:17px}.thumb,.nothumb{width:112px;height:63px}.thumbs figure img{width:96px;height:54px}"
```

V hlavním CSS za řádek `.nothumb{…}` přidat řádek:

```
        .thumbs{display:flex;gap:4px}.thumbs figure{margin:0}.thumbs figcaption{font:10px ui-monospace,Menlo,monospace;color:var(--muted);text-align:center}
```

V cyklu klipů nahradit `let img = …`:

```swift
                let img = c.thumbnails.isEmpty ? "<div class=\"nothumb\"></div>"
                    : "<div class=\"thumbs\">" + c.thumbnails.map { t in
                        "<figure><img class=\"thumb\" src=\"data:image/jpeg;base64,\(t.jpeg.base64EncodedString())\" alt=\"\"><figcaption>\(esc(t.label))</figcaption></figure>"
                    }.joined() + "</div>"
```

- [ ] **Step 5: `Job.swift`** — v `analyzeAndRender` nahradit řádek s `entries[i].thumbnail = try? renderer.thumbnail(...)` (analýza):

```swift
                entries[i].info = info
                // RED and ARRI raw cannot give a single frame directly; their thumbnails come from the proxy later.
                if entries[i].clip.format != .r3d && entries[i].clip.format != .arriraw {
                    entries[i].thumbnails = tryThumbnails(renderer, entries[i].clip, cardPath: plan.cardPaths[d], info: info,
                                                          applyLUT: true, log: log)
                }
```

V části proxy nahradit celý blok `do { try renderer.render(…) … } catch { … }`:

```swift
            let t0 = Date()
            do {
                try renderer.render(clip, cardPath: plan.cardPaths[d], info: info, output: plan.cardPaths[d] + "/" + rel,
                                    progress: { f in self.update { $0.clipFraction = f } },
                                    isCancelled: { self.isCancelled })
                entries[i].proxyStatus = .done
                entries[i].proxyPath = rel
                if config.proxy.copyToAllDestinations {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
                    for (o, other) in plan.cardPaths.enumerated() where o != d {
                        do {
                            try copyFile(plan.cardPaths[d] + "/" + rel, to: other + "/" + rel)
                        } catch {
                            let note = "kopie na \(other) selhala: \(error)"
                            entries[i].proxyNote = entries[i].proxyNote.map { "\($0); \(note)" } ?? note
                            log?("proxy \(clip.name): copy to \(other) failed: \(error)")
                        }
                    }
                }
                if entries[i].thumbnails.isEmpty {
                    // RAW formats rendered via an intermediate: take the thumbnails from the finished proxy.
                    let proxyClip = Clip(name: name, path: rel, format: .video, segments: [rel], bytes: 0, isCameraProxy: false)
                    entries[i].thumbnails = tryThumbnails(renderer, proxyClip, cardPath: plan.cardPaths[d], info: info,
                                                          applyLUT: false, log: log)
                }
                entries[i].proxySeconds = Date().timeIntervalSince(t0)
                update { $0.proxiesDone += 1 }
            } catch {
                entries[i].proxyStatus = .failed
                entries[i].proxyNote = "\(error)"
                update { $0.proxiesFailed += 1 }
                log?("proxy \(clip.name): \(error)")
            }
```

Přidat `tryThumbnails` a nahradit `copyFile`:

```swift
    private func tryThumbnails(_ renderer: ProxyRenderer, _ clip: Clip, cardPath: String, info: ClipInfo,
                               applyLUT: Bool, log: ((String) -> Void)?) -> [Thumbnail] {
        do {
            return try renderer.thumbnails(clip, cardPath: cardPath, info: info, applyLUT: applyLUT, isCancelled: { self.isCancelled })
        } catch {
            if !isCancelled { log?("thumbnails \(clip.name): \(error)") }
            return []
        }
    }

    private func copyFile(_ src: String, to dst: String) throws {
        try IO.mkdirs((dst as NSString).deletingLastPathComponent)
        let part = (dst as NSString).deletingLastPathComponent + "/." + (dst as NSString).lastPathComponent + ".part"
        unlink(part)
        do {
            try FileManager.default.copyItem(atPath: src, toPath: part)
            guard rename(part, dst) == 0 else { throw IOError("rename", dst) }
        } catch {
            unlink(part)
            throw error
        }
    }
```

V `Proxy.swift` smazat dočasnou funkci `thumbnail(_:cardPath:info:applyLUT:)` z úkolu 6.

- [ ] **Step 6: Testy projdou** — Run: `swift test --filter "HTMLReportTests|StageTwoTests"`, pak `swift test`.

- [ ] **Step 7: Ruční ověření reportu**

Run: `swift build -c release && .build/release/offload <karta nebo složka s klipy> -d <USB disk> --proxy`
Expected: v `…/_REPORT/<karta>_report.html` má každý video klip tři náhledy s TC pod nimi; v PDF jsou menší; BRAW (je-li `braw-decode`) taky tři; R3D/ARRI bez proxy žádné, s proxy tři.

- [ ] **Step 8: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 7 — report: tři snímky na klip s popiskem TC (HTML i PDF), R3D/ARRI z hotové proxy; proxy zůstane hotová i při selhání kopie na další cíl`

`docs/VYVOJ-MAC.md` oddíl „Naplánováno“: beze změny (od 2026-10-05 je tam jen odkaz na tento plán a co žádný úkol neřeší).
`docs/ROZDILY.md` — řádek „Náhledy v reportu“: Mac `✅ 3 snímky`.

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: three thumbnails in the report, proxy kept when copying it elsewhere fails"`

---

### Task 8: České texty, průběh karty úlohy a brzda

**Files:**
- Create: `mac/Sources/OffloadKit/Desktop/Texts.swift`, `mac/Sources/OffloadKit/Desktop/JobProgress.swift`, `mac/Sources/OffloadKit/Hardware/BrakeMeter.swift`
- Create: `mac/Tests/OffloadKitTests/JobProgressTests.swift`, `mac/Tests/OffloadKitTests/BrakeMeterTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `JobStatus`, `JobStatus.Phase.label`, `ProgressSnapshot`, `OffloadReport`, `formatBytes`, `formatRate`.
- Produces:
  - `public enum Texts { plural, cards, destinations, files, clips, safeToEject, failed, warnings, cancelled }`
  - `public struct Brake: Equatable { element: Int; mbps: Double; utilization: Double?; var linkFull: Bool }`
  - `public final class BrakeMeter { init(chunkBytes: Int64, bufferCount: Int, linkMBps: [Double?] = [], windowSeconds: Double = 4); func add(seconds: Double, readBytes: Int64, writtenBytes: [Int64]) -> Brake? }`
  - `public enum CardTone { waiting, active, ok, warning, error }`
  - `public final class JobProgress { init(now: Date); attachBrakeMeter(_:); update(_ s: JobStatus, now: Date); status; readRate; destRates; eta; brake; isRunning; phaseProgress; statusText; tone; isError; detailText; static etaText(_:); static summaryText(_:) }`

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/BrakeMeterTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class BrakeMeterTests: XCTestCase {
    private let chunk: Int64 = 16 << 20

    /// A copy that moves `rate` bytes/s through a lockstep engine with a fixed backlog per writer,
    /// quantised by whole chunks like `DestWriter` (it reports only finished blocks).
    private func run(_ m: BrakeMeter, seconds: Double, rate: Int64, lags: [Int64]) -> Brake? {
        var last: Brake?
        var t = 0.0
        while t <= seconds + 1e-9 {
            let read = Int64(Double(rate) * t) + lags.max()! * chunk
            last = m.add(seconds: t, readBytes: read, writtenBytes: lags.map { (read - $0 * chunk) / chunk * chunk })
            t += 0.5
        }
        return last
    }

    func testLockstepEqualRatesLaggingWriterBrakes() {
        let b = run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 57_000_000, lags: [0, 15])
        XCTAssertEqual(b?.element, 2)
        XCTAssertTrue((50...65).contains(b!.mbps), "\(b!.mbps)")
    }

    func testAllLagsSmallCardBrakes() {
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 300_000_000, lags: [1, 0, 1])?.element, 0)
    }

    func testFirstOfEqualLagsAndNeverTheCardOnATie() {
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 100_000_000, lags: [14, 14])?.element, 1)
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 100_000_000, lags: [3, 0])?.element, 1)
    }

    // At the start the pool is still filling: the slow writer's backlog grows before it is large on average.
    func testGrowingBacklogWriterBrakesBeforeThePoolIsFull() {
        let m = BrakeMeter(chunkBytes: chunk, bufferCount: 16)
        var b: Brake?
        var t = 0.0
        while t <= 2.0 + 1e-9 {
            b = m.add(seconds: t, readBytes: Int64(100_000_000 * t), writtenBytes: [Int64(60_000_000 * t)])   // card 100, disk 60 MB/s
            t += 0.5
        }
        XCTAssertEqual(b?.element, 1)
    }

    func testSmallPoolThresholdIsHalfThePool() {
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 2), seconds: 6, rate: 50_000_000, lags: [1])?.element, 1)
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 50_000_000, lags: [1])?.element, 0)
    }

    func testLinkFullFromTheBrakingElementsLink() {
        var b = run(BrakeMeter(chunkBytes: chunk, bufferCount: 16, linkMBps: [450, 40]), seconds: 6, rate: 38_000_000, lags: [15])
        XCTAssertEqual(b?.element, 1)
        XCTAssertEqual(b?.linkFull, true)
        b = run(BrakeMeter(chunkBytes: chunk, bufferCount: 16, linkMBps: [450, nil]), seconds: 6, rate: 38_000_000, lags: [15])
        XCTAssertEqual(b?.linkFull, false)
        XCTAssertNil(b?.utilization)
    }

    func testNoVerdictUntilHalfTheWindowAndSmoothedOverIt() {
        let m = BrakeMeter(chunkBytes: chunk, bufferCount: 16, windowSeconds: 4)
        XCTAssertNil(m.add(seconds: 0, readBytes: 0, writtenBytes: [0]))
        XCTAssertNil(m.add(seconds: 1, readBytes: 100 * chunk, writtenBytes: [100 * chunk - 15 * chunk]))
        XCTAssertEqual(m.add(seconds: 2, readBytes: 200 * chunk, writtenBytes: [200 * chunk - 15 * chunk])?.element, 1)
        // One momentary catch-up of the writer does not flip the verdict.
        XCTAssertEqual(m.add(seconds: 2.5, readBytes: 210 * chunk, writtenBytes: [210 * chunk])?.element, 1)
    }
}
```

`mac/Tests/OffloadKitTests/JobProgressTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class JobProgressTests: XCTestCase {
    private let t0 = Date(timeIntervalSince1970: 1_791_201_600)

    private func snap(_ read: Int64, _ written: [Int64], _ verified: [Int64]? = nil, total: Int64 = 1_000_000_000) -> ProgressSnapshot {
        ProgressSnapshot(phase: .copy, totalBytes: total, totalFiles: 10, fileIndex: 1, currentFile: "DCIM/100CANON/A001.MOV",
                         readBytes: read, writtenBytes: written, verifiedBytes: verified ?? written.map { _ in 0 }, errors: 0)
    }

    private func st(_ phase: JobStatus.Phase, _ copy: ProgressSnapshot? = nil) -> JobStatus {
        var s = JobStatus()
        s.phase = phase
        s.copy = copy
        return s
    }

    private func copying() -> JobProgress {
        let p = JobProgress(now: t0)
        p.update(st(.copying, snap(0, [0, 0])), now: t0 + 0.5)
        p.update(st(.copying, snap(100_000_000, [100_000_000, 50_000_000])), now: t0 + 1.5)
        p.update(st(.copying, snap(150_000_000, [150_000_000, 100_000_000])), now: t0 + 2.5)
        return p
    }

    func testRatesResetOnPhaseChangeThenSmoothed() {
        let p = copying()
        XCTAssertEqual(p.readRate, 80e6, accuracy: 1)     // 0.6 × 100 + 0.4 × 50 MB/s
        XCTAssertEqual(p.destRates[0], 80e6, accuracy: 1)
        XCTAssertEqual(p.destRates[1], 50e6, accuracy: 1)
        XCTAssertEqual(p.eta!, 850e6 / 80e6, accuracy: 0.001)
        p.update(st(.verifying, snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [0, 0])), now: t0 + 3.5)
        XCTAssertEqual(p.readRate, 0)
        XCTAssertEqual(p.destRates, [0, 0])
    }

    func testUpdatesCloserThan200msKeepRates() {
        let p = copying()
        p.update(st(.copying, snap(900_000_000, [900_000_000, 900_000_000])), now: t0 + 2.6)
        XCTAssertEqual(p.readRate, 80e6, accuracy: 1)
    }

    func testPhaseChangeInsideSkippedUpdateResetsRates() {
        let p = copying()
        p.update(st(.verifying, snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [0, 0])), now: t0 + 2.6)
        p.update(st(.verifying, snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [100_000_000, 100_000_000])), now: t0 + 3.6)
        XCTAssertEqual(p.readRate, 0)
        XCTAssertTrue(p.destRates.allSatisfy { $0 >= 0 })
    }

    func testVerifyingEtaFromTheSlowestDestination() {
        let p = JobProgress(now: t0)
        p.update(st(.verifying, snap(0, [0, 0], [0, 0])), now: t0 + 0.5)
        p.update(st(.verifying, snap(0, [0, 0], [200_000_000, 100_000_000])), now: t0 + 1.5)
        XCTAssertEqual(p.readRate, 100e6, accuracy: 1)
        XCTAssertEqual(p.eta!, 9.0, accuracy: 0.001)
        XCTAssertEqual(p.detailText, "D1 200 MB/s · D2 100 MB/s · zbývá 0:09")
    }

    func testPhaseProgressEachPhase() {
        let p = JobProgress(now: t0)
        p.update(st(.copying, snap(250_000_000, [0])), now: t0)
        XCTAssertEqual(p.phaseProgress, 0.25, accuracy: 0.001)
        p.update(st(.verifying, snap(0, [0, 0], [500_000_000, 250_000_000])), now: t0)
        XCTAssertEqual(p.phaseProgress, 0.375, accuracy: 0.001)
        var a = JobStatus()
        a.phase = .analyzing
        a.clipIndex = 1
        a.clipCount = 4
        a.clipFraction = 0.5
        p.update(a, now: t0)
        XCTAssertEqual(p.phaseProgress, 0.375, accuracy: 0.001)
        p.update(st(.done), now: t0)
        XCTAssertEqual(p.phaseProgress, 1)
        p.update(JobStatus(), now: t0)
        XCTAssertEqual(p.phaseProgress, 0)
    }

    func testDetailTexts() {
        XCTAssertEqual(copying().detailText, "Karta 80 MB/s · D1 80 MB/s · D2 50 MB/s · zbývá 0:10 · A001.MOV")
        let p = JobProgress(now: t0)
        var s = JobStatus()
        s.phase = .proxies
        s.clipIndex = 2
        s.clipCount = 5
        s.clipName = "A003"
        s.clipFraction = 0.426
        p.update(s, now: t0)
        XCTAssertEqual(p.detailText, "Proxy 3/5 · A003 · 43 %")
        s.phase = .analyzing
        s.clipIndex = 0
        s.clipCount = 3
        s.clipName = "A001"
        p.update(s, now: t0)
        XCTAssertEqual(p.detailText, "Klip 1/3 · A001")
        p.update(st(.flushing), now: t0)
        XCTAssertEqual(p.detailText, "Zápis cache disků…")
    }

    func testEtaText() {
        XCTAssertEqual(JobProgress.etaText(59), "0:59")
        XCTAssertEqual(JobProgress.etaText(600), "10:00")
        XCTAssertEqual(JobProgress.etaText(3725), "1:02:05")
        XCTAssertEqual(JobProgress.etaText(-5), "0:00")
        XCTAssertEqual(JobProgress.etaText(.nan), "0:00")
    }

    func testStatusTextToneAndErrorNotForCancelled() {
        let cases: [(JobStatus.Phase, Bool?, String, CardTone, Bool)] = [
            (.done, false, "Chyba", .error, true),
            (.done, true, "Hotovo", .ok, false),
            (.failed, nil, "Chyba", .error, true),
            (.cancelled, nil, "Zrušeno", .warning, false),
            (.cancelled, false, "Zrušeno", .warning, false),
            (.copying, nil, "Kopírování", .active, false),
            (.waiting, nil, "Čeká", .waiting, false),
        ]
        for (phase, ok, text, tone, error) in cases {
            let p = JobProgress(now: t0)
            var s = JobStatus()
            s.phase = phase
            s.copySucceeded = ok
            p.update(s, now: t0)
            XCTAssertEqual(p.statusText, text, "\(phase)")
            XCTAssertEqual(p.tone, tone, "\(phase)")
            XCTAssertEqual(p.isError, error, "\(phase)")
        }
    }

    func testBrakeOnlyWhileCopying() {
        let mb: Int64 = 1_000_000
        let p = JobProgress(now: t0)
        p.attachBrakeMeter(BrakeMeter(chunkBytes: 16 << 20, bufferCount: 4))
        for i in 0...8 {
            let read = Int64(i) * 50 * mb
            p.update(st(.copying, snap(read, [read, max(0, read - 48 * mb)])), now: t0 + Double(i) * 0.5)
        }
        XCTAssertEqual(p.brake?.element, 2)
        p.update(st(.verifying, snap(400 * mb, [400 * mb, 400 * mb])), now: t0 + 5)
        XCTAssertNil(p.brake)
    }

    func testSummaryText() {
        let clip = Clip(name: "A001", path: "A001.MOV", format: .video, segments: ["A001.MOV"], bytes: 1, isCameraProxy: false)
        func entry(_ s: ProxyStatus) -> ClipEntry { var e = ClipEntry(clip: clip); e.proxyStatus = s; return e }
        var r = OffloadReport(cardName: "A001", date: "2026-10-05", source: "/Volumes/A001", sourceFilesystem: "exfat",
                              started: t0, copyFinished: t0 + 10, finished: t0 + 15, verified: true,
                              totalFiles: 3, totalBytes: 1_000_000_000, destinations: [], files: [], skipped: [])
        r.clips = [entry(.done), entry(.failed), entry(.unsupported)]
        var p = ProxySettings()
        p.enabled = true
        r.proxy = p
        XCTAssertEqual(JobProgress.summaryText(r), "kopie 100 MB/s · ověření 200 MB/s · 3 klipy · proxy 1, 1 chyba, 1 nepodporováno")
    }

    func testCzechPlurals() {
        XCTAssertEqual(Texts.cards(0), "Vyberte kartu")
        XCTAssertEqual(Texts.cards(1), "1 karta")
        XCTAssertEqual(Texts.cards(3), "3 karty")
        XCTAssertEqual(Texts.cards(5), "5 karet")
        XCTAssertEqual(Texts.destinations(0), "0 cílů")
        XCTAssertEqual(Texts.destinations(1), "1 cíl")
        XCTAssertEqual(Texts.destinations(2), "2 cíle")
        XCTAssertEqual(Texts.files(1, 1000), "1 soubor · 1.00 KB")
        XCTAssertEqual(Texts.files(12, 2_500_000_000), "12 souborů · 2.50 GB")
        XCTAssertEqual(Texts.clips(1), "1 klip")
        XCTAssertEqual(Texts.clips(4), "4 klipy")
        XCTAssertEqual(Texts.clips(7), "7 klipů")
        XCTAssertEqual(Texts.safeToEject(1), "Všechny soubory jsou na 1 disku a ověřené — kartu lze vysunout.")
        XCTAssertEqual(Texts.safeToEject(2), "Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.")
        XCTAssertEqual(Texts.failed(3), "3 soubory selhaly — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.")
        XCTAssertEqual(Texts.failed(1), "1 soubor selhal — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.")
        XCTAssertEqual(Texts.warnings(0), "bez varování")
        XCTAssertEqual(Texts.warnings(2), "2 varování")
        XCTAssertEqual(Texts.cancelled, "Zrušeno — karta není kompletně zálohovaná.")
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "BrakeMeterTests|JobProgressTests"` → nesestaví se (`cannot find 'BrakeMeter'`, `'JobProgress'`, `'Texts'`).

- [ ] **Step 3: `mac/Sources/OffloadKit/Desktop/Texts.swift`**:

```swift
import Foundation

/// Czech texts with the right plural (1 karta, 3 karty, 5 karet) — port of Windows `Desktop/Texts.cs`.
public enum Texts {
    public static func plural(_ n: Int, _ one: String, _ few: String, _ many: String) -> String {
        n == 1 ? one : (2...4).contains(n) ? few : many
    }

    public static func cards(_ n: Int) -> String { n == 0 ? "Vyberte kartu" : "\(n) \(plural(n, "karta", "karty", "karet"))" }

    public static func destinations(_ n: Int) -> String { "\(n) \(plural(n, "cíl", "cíle", "cílů"))" }

    public static func files(_ n: Int, _ bytes: Int64) -> String { "\(n) \(plural(n, "soubor", "soubory", "souborů")) · \(formatBytes(bytes))" }

    public static func clips(_ n: Int) -> String { "\(n) \(plural(n, "klip", "klipy", "klipů"))" }

    public static func safeToEject(_ disks: Int) -> String {
        "Všechny soubory jsou na \(disks) \(plural(disks, "disku", "discích", "discích")) a ověřené — kartu lze vysunout."
    }

    public static func failed(_ n: Int) -> String {
        "\(n) \(plural(n, "soubor selhal", "soubory selhaly", "souborů selhalo")) — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu."
    }

    public static func warnings(_ n: Int) -> String { n == 0 ? "bez varování" : "\(n) varování" }

    /// A cancelled job card: one orange line instead of "N souborů selhalo" and the rate summary.
    public static let cancelled = "Zrušeno — karta není kompletně zálohovaná."
}
```

- [ ] **Step 4: `mac/Sources/OffloadKit/Hardware/BrakeMeter.swift`**:

```swift
import Foundation

/// The element holding the copy back. Element 0 = the card (source read), i = destination i.
/// `mbps`: its pace over the window; `utilization`: of its USB link (nil without one).
public struct Brake: Equatable {
    public let element: Int
    public let mbps: Double
    public let utilization: Double?

    public var linkFull: Bool { (utilization ?? 0) > 0.85 }
}

/// Decides which element brakes the copy, from the backlog rather than from rates (port of Windows `BrakeMeter.cs`).
///
/// The engine runs in lockstep: one bounded buffer pool (bufferCount × chunkSize); a chunk returns to it only when
/// every writer and the hasher are done with it. So the card and all disks move at the same average pace and
/// short-window rates only show chunk quantisation. What differs is the backlog: lag_i = readBytes − writtenBytes[i].
/// The slow writer keeps the pool full (its lag is near the pool size), a fast one stays within a chunk or two.
/// → The writer with the largest lag brakes, if that lag (averaged over the window) is at least ~2 chunks (half the
///   pool when the pool is smaller) or grew by that much within the window; otherwise every disk keeps up and the card brakes.
public final class BrakeMeter {
    private let threshold: Double
    private let windowSeconds: Double
    private let linkMBps: [Double?]
    private var samples: [(time: Double, read: Int64, written: [Int64])] = []

    /// linkMBps: ceiling per element (card first, then destinations), nil = no USB link.
    public init(chunkBytes: Int64, bufferCount: Int, linkMBps: [Double?] = [], windowSeconds: Double = 4) {
        let pool = Double(chunkBytes) * Double(max(bufferCount, 1))
        threshold = min(2.0 * Double(chunkBytes), pool / 2.0)
        self.windowSeconds = windowSeconds
        self.linkMBps = linkMBps
    }

    /// Feed one progress snapshot (seconds from any fixed start). Returns the brake once the window holds enough
    /// history (half the window), else nil.
    public func add(seconds: Double, readBytes: Int64, writtenBytes: [Int64]) -> Brake? {
        if let first = samples.first, let last = samples.last,
           first.written.count != writtenBytes.count || seconds < last.time {
            samples.removeAll()
        }
        samples.append((seconds, readBytes, writtenBytes))
        while samples.count > 2 && seconds - samples[0].time > windowSeconds { samples.removeFirst() }

        let first = samples[0]
        let span = seconds - first.time
        guard samples.count >= 2, span > 0, span >= windowSeconds / 2 else { return nil }

        // A writer brakes when its backlog is large, or still growing (the pool is filling up: the card is faster).
        // Of those, the largest backlog; equal backlogs → the first. No such writer → the card.
        var brake = 0
        var worst = -Double.infinity
        let last = samples[samples.count - 1]
        for i in writtenBytes.indices {
            let lag = samples.reduce(0.0) { $0 + Double($1.read - $1.written[i]) } / Double(samples.count)
            let growth = Double((last.read - last.written[i]) - (first.read - first.written[i]))
            if (lag >= threshold || growth >= threshold) && lag > worst {
                worst = lag
                brake = i + 1
            }
        }
        let moved = brake == 0 ? readBytes - first.read : writtenBytes[brake - 1] - first.written[brake - 1]
        let mbps = Double(max(0, moved)) / span / 1e6
        var utilization: Double?
        if brake < linkMBps.count, let l = linkMBps[brake], l > 0 { utilization = mbps / l }
        return Brake(element: brake, mbps: mbps, utilization: utilization)
    }
}
```

- [ ] **Step 5: `mac/Sources/OffloadKit/Desktop/JobProgress.swift`**:

```swift
import Foundation

public enum CardTone: Equatable { case waiting, active, ok, warning, error }

/// What a job card shows, from successive `JobStatus` snapshots (port of Windows `Desktop/JobProgress.cs`;
/// the logic of the app's former `JobItem.refresh` and `JobCard` texts, now testable).
public final class JobProgress {
    private let started: Date
    private var lastTime: Date
    private var lastBytes: Int64 = 0
    private var lastDest: [Int64] = []
    private var lastPhase: JobStatus.Phase = .waiting
    private var brakes: BrakeMeter?

    public private(set) var status = JobStatus()
    /// Bytes per second, smoothed: card read while copying, slowest destination while verifying.
    public private(set) var readRate: Double = 0
    public private(set) var destRates: [Double] = []
    public private(set) var eta: Double?
    /// The element braking the copy (0 = card, i = destination i); only while copying.
    public private(set) var brake: Brake?

    public init(now: Date) {
        started = now
        lastTime = now
    }

    public func attachBrakeMeter(_ meter: BrakeMeter) { brakes = meter }

    public var isRunning: Bool { status.phase != .waiting && !status.phase.isFinished }

    public func update(_ s: JobStatus, now: Date) {
        let dt = now.timeIntervalSince(lastTime)
        if let c = s.copy, dt > 0.2 {
            let copying = s.phase == .copying
            let bytes = copying ? c.readBytes : (c.verifiedBytes.min() ?? 0)
            let dests = copying ? c.writtenBytes : c.verifiedBytes
            if s.phase != lastPhase || lastDest.count != dests.count {
                readRate = 0
                destRates = Array(repeating: 0, count: dests.count)
            } else {
                readRate = JobProgress.smooth(readRate, max(0, Double(bytes - lastBytes) / dt))
                destRates = dests.indices.map { JobProgress.smooth(destRates[$0], max(0, Double(dests[$0] - lastDest[$0]) / dt)) }
                eta = readRate > 1 ? Double(c.totalBytes - bytes) / readRate : nil
            }
            lastPhase = s.phase
            lastBytes = bytes
            lastDest = dests
            lastTime = now
        }
        if s.phase == .copying, let cc = s.copy, let brakes {
            brake = brakes.add(seconds: now.timeIntervalSince(started), readBytes: cc.readBytes, writtenBytes: cc.writtenBytes) ?? brake
        } else {
            brake = nil
        }
        status = s
    }

    static func smooth(_ old: Double, _ v: Double) -> Double { old == 0 ? v : old * 0.6 + v * 0.4 }

    /// 0…1 within the current phase.
    public var phaseProgress: Double { min(1, max(0, rawProgress)) }

    private var rawProgress: Double {
        switch status.phase {
        case .copying:
            guard let c = status.copy, c.totalBytes > 0 else { return 0 }
            return Double(c.readBytes) / Double(c.totalBytes)
        case .verifying:
            guard let c = status.copy, c.totalBytes > 0 else { return 0 }
            return Double(c.verifiedBytes.reduce(0, +)) / (Double(c.totalBytes) * Double(max(1, c.verifiedBytes.count)))
        case .analyzing, .proxies:
            guard status.clipCount > 0 else { return 0 }
            return (Double(status.clipIndex) + status.clipFraction) / Double(status.clipCount)
        case .done:
            return 1
        default:
            return 0
        }
    }

    public var statusText: String { status.phase == .done && status.copySucceeded == false ? "Chyba" : status.phase.label }

    /// Failed, or copied with errors — a job the user cancelled is not an error (no error sound).
    public var isError: Bool { tone == .error }

    public var tone: CardTone {
        switch status.phase {
        case .done: return status.copySucceeded == false ? .error : .ok
        case .failed: return .error
        case .cancelled: return .warning
        case .waiting: return .waiting
        default: return .active
        }
    }

    static func mb(_ bytesPerSecond: Double) -> String { String(format: "%.0f MB/s", bytesPerSecond / 1_000_000) }

    public static func etaText(_ seconds: Double) -> String {
        let i = seconds.isNaN || seconds < 0 ? 0 : Int(min(seconds, Double(Int32.max)))
        return i >= 3600 ? String(format: "%d:%02d:%02d", i / 3600, i / 60 % 60, i % 60) : String(format: "%d:%02d", i / 60, i % 60)
    }

    public var detailText: String {
        let s = status
        let etaPart = eta.map { " · zbývá " + JobProgress.etaText($0) } ?? ""
        let dests = destRates.enumerated().map { "D\($0.offset + 1) \(JobProgress.mb($0.element))" }.joined(separator: " · ")
        switch s.phase {
        case .copying:
            let file = ((s.copy?.currentFile ?? "") as NSString).lastPathComponent
            return "Karta \(JobProgress.mb(readRate)) · \(dests)\(etaPart) · \(file)"
        case .verifying:
            return dests + etaPart
        case .analyzing:
            return "Klip \(s.clipIndex + 1)/\(s.clipCount) · \(s.clipName)"
        case .proxies:
            return "Proxy \(s.clipIndex + 1)/\(s.clipCount) · \(s.clipName) · " + String(format: "%.0f", s.clipFraction * 100) + " %"
        default:
            return s.phase.label + "…"
        }
    }

    public static func summaryText(_ r: OffloadReport) -> String {
        var parts = ["kopie \(formatRate(r.totalBytes, r.copySeconds))"]
        if r.verified { parts.append("ověření \(formatRate(r.totalBytes, r.verifySeconds))") }
        if !r.clips.isEmpty { parts.append(Texts.clips(r.clips.count)) }
        if r.proxy?.enabled == true {
            func count(_ st: ProxyStatus) -> Int { r.clips.filter { $0.proxyStatus == st }.count }
            var proxy = "proxy \(count(.done))"
            if count(.failed) > 0 { proxy += ", \(count(.failed)) chyba" }
            if count(.unsupported) > 0 { proxy += ", \(count(.unsupported)) nepodporováno" }
            parts.append(proxy)
        }
        return parts.joined(separator: " · ")
    }
}
```

- [ ] **Step 6: Testy projdou** — Run: `swift test --filter "BrakeMeterTests|JobProgressTests"` → 7 + 11 testů, 0 chyb; pak `swift test`.

- [ ] **Step 7: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 8 — české texty s množným číslem, průběh karty úlohy (rychlosti, odhad, fáze, tón; zrušená úloha není chyba) a brzda podle zásoby bloků — v OffloadKit s testy`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: Czech plurals, job card progress and backlog-based brake meter in OffloadKit"`

---

### Task 9: Vysunutí jen kopírované karty — pravidla a identita

**Files:**
- Create: `mac/Sources/OffloadKit/Desktop/QueueRules.swift` (`CardIdentity`, `CardEject`, `QueueRules`)
- Create: `mac/Sources/OffloadKit/Desktop/Volumes.swift` (zatím jen `VolumeItem`, přesunutý z aplikace a rozšířený)
- Modify: `mac/Sources/OffloadApp/AppModel.swift` (smazat `struct VolumeItem`)
- Create: `mac/Tests/OffloadKitTests/QueueRulesTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `JobStatus.Phase`, `Layout.reportDirName`.
- Produces:
  - `public struct VolumeItem: Identifiable, Hashable { name, path, total, free, format, looksLikeCard, isDestination, identity: String?, ejectProblem: String?; public init(name:path:total:free:format:looksLikeCard:isDestination:identity:ejectProblem:) }`
  - `public enum CardIdentity { static func of(_ path: String) -> String? }`
  - `public enum CardEject { static func problem(exists:isVolumeRoot:isLocal:isRootFileSystem:isInternal:isEjectable:isRemovable:) -> String?; static func problem(source: String) -> String?; static func eject(_ source: String) -> (ok: Bool, error: String?) }`
  - `public enum QueueRules { otherCardText; inUseText; ejectProblem(copied:current:cardInUse:); cardInUse(_:others:); cardInDrive(_:copied:scanned:); measureBlocked(_:); reportPath(cardPath:cardName:); firstReport(cardPaths:cardName:) }`

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/QueueRulesTests.swift` (nikdy nevysouvá skutečné zařízení):

```swift
import XCTest
@testable import OffloadKit

final class QueueRulesTests: XCTestCase {
    func testEjectProblemSameCardNotInUseAllowed() {
        XCTAssertNil(QueueRules.ejectProblem(copied: "uuid:AB12", current: "UUID:ab12", cardInUse: false))
    }

    // Review focus 2: another card in the slot, or the card gone → never eject.
    func testEjectProblemOtherCardOrGoneRefused() {
        XCTAssertEqual(QueueRules.ejectProblem(copied: "uuid:AB12", current: "uuid:CD34", cardInUse: false), QueueRules.otherCardText)
        XCTAssertEqual(QueueRules.ejectProblem(copied: nil, current: "uuid:CD34", cardInUse: false), QueueRules.otherCardText)
        XCTAssertEqual(QueueRules.ejectProblem(copied: "uuid:AB12", current: nil, cardInUse: false), "karta není připojená")
        XCTAssertEqual(QueueRules.otherCardText, "karta v mechanice není ta, která se kopírovala")
    }

    func testEjectProblemCardStillNeededByAnotherJobRefused() {
        XCTAssertEqual(QueueRules.ejectProblem(copied: "uuid:AB12", current: "uuid:AB12", cardInUse: true), QueueRules.inUseText)
        XCTAssertEqual(QueueRules.inUseText, "z karty ještě kopíruje jiná úloha — vysuňte ji, až její kopie doběhne")
    }

    func testCardInUseByPhaseOfAnotherJobOnTheSameVolume() {
        let cases: [(JobStatus.Phase, Bool)] = [
            (.waiting, true), (.copying, true), (.flushing, true), (.verifying, true), (.reporting, true),
            (.analyzing, false), (.proxies, false), (.done, false), (.failed, false), (.cancelled, false),
        ]
        for (phase, inUse) in cases {
            XCTAssertEqual(QueueRules.cardInUse("/Volumes/A001", others: [("/Volumes/A001/DCIM", phase)]), inUse, "\(phase)")
            XCTAssertEqual(QueueRules.cardInUse("/Volumes/A001/", others: [("/Volumes/A001", phase)]), inUse, "\(phase)")
            XCTAssertFalse(QueueRules.cardInUse("/Volumes/A001", others: [("/Volumes/A0012", phase)]))
            XCTAssertFalse(QueueRules.cardInUse("/Volumes/A001", others: [("/Volumes/B", phase)]))
        }
    }

    // Review focus 2: the button shows only while the scan shows the copied card in its slot.
    func testCardInDriveOnlyWhenTheScanShowsTheCopiedCard() {
        func item(_ identity: String?) -> VolumeItem {
            VolumeItem(name: "A001", path: "/Volumes/A001", total: 10, free: 5, format: "exfat", looksLikeCard: true,
                       isDestination: false, identity: identity)
        }
        XCTAssertTrue(QueueRules.cardInDrive("/Volumes/A001", copied: "uuid:AB12", scanned: [item("uuid:AB12")]))
        XCTAssertFalse(QueueRules.cardInDrive("/Volumes/A001", copied: "uuid:CD34", scanned: [item("uuid:AB12")]))   // another card
        XCTAssertFalse(QueueRules.cardInDrive("/Volumes/A001", copied: nil, scanned: [item("uuid:AB12")]))
        XCTAssertFalse(QueueRules.cardInDrive("/Volumes/B", copied: "uuid:AB12", scanned: [item("uuid:AB12")]))      // pulled out
        XCTAssertFalse(QueueRules.cardInDrive("/Volumes/A001", copied: "uuid:AB12", scanned: [item(nil)]))
    }

    func testCardIdentitySameVolumeSameMissingNil() {
        let dir = tempDir()
        let a = CardIdentity.of(dir.path)
        XCTAssertNotNil(a, "the startup volume reports a volume UUID")
        XCTAssertEqual(a, CardIdentity.of(dir.sub("x")))
        XCTAssertTrue(a?.hasPrefix("uuid:") == true)
        XCTAssertNil(CardIdentity.of("/Volumes/offload-no-such-volume-\(UUID().uuidString)"))
    }

    // Review focus 2: folders, network shares, the system volume and internal disks are never ejected.
    func testCardEjectProblemRules() {
        func p(exists: Bool = true, root: Bool = true, local: Bool = true, rootFS: Bool = false,
               internal: Bool? = false, ejectable: Bool? = true, removable: Bool? = true) -> String? {
            CardEject.problem(exists: exists, isVolumeRoot: root, isLocal: local, isRootFileSystem: rootFS,
                              isInternal: internal, isEjectable: ejectable, isRemovable: removable)
        }
        XCTAssertNil(p())
        XCTAssertEqual(p(exists: false), "karta není připojená")
        XCTAssertEqual(p(local: false), "síťová složka se nevysouvá")
        XCTAssertEqual(p(root: false), "zdroj je složka, ne celá karta")
        XCTAssertEqual(p(rootFS: true), "systémový disk nelze vysunout")
        XCTAssertEqual(p(internal: true, ejectable: false, removable: false), "interní disk nelze vysunout")
        XCTAssertEqual(p(internal: nil, ejectable: nil, removable: nil), "interní disk nelze vysunout")
        XCTAssertNil(p(internal: true, ejectable: true, removable: true))     // built-in SD slot
        XCTAssertNil(p(internal: false, ejectable: false, removable: false))  // USB / Thunderbolt disk
    }

    func testCardEjectProblemFromTheDisk() {
        let dir = tempDir()
        XCTAssertEqual(CardEject.problem(source: dir.path), "zdroj je složka, ne celá karta")
        XCTAssertEqual(CardEject.problem(source: "/"), "systémový disk nelze vysunout")
        XCTAssertEqual(CardEject.problem(source: dir.path + "/missing"), "karta není připojená")
        let r = CardEject.eject(dir.path)
        XCTAssertFalse(r.ok)
        XCTAssertEqual(r.error, "zdroj je složka, ne celá karta")
    }

    func testMeasureBlockedWhileACopyWritesOrVerifies() {
        XCTAssertTrue(QueueRules.measureBlocked([.done, .copying]))
        XCTAssertTrue(QueueRules.measureBlocked([.flushing]))
        XCTAssertTrue(QueueRules.measureBlocked([.verifying]))
        XCTAssertFalse(QueueRules.measureBlocked([.waiting, .proxies, .done, .cancelled]))
        XCTAssertFalse(QueueRules.measureBlocked([]))
    }

    func testReportFirstExistingDestinationWins() throws {
        let dir = tempDir()
        let cards = [dir.sub("d1", "2026-10-05", "A001"), dir.sub("d2", "2026-10-05", "A001"), dir.sub("d3", "2026-10-05", "A001")]
        XCTAssertNil(QueueRules.firstReport(cardPaths: cards, cardName: "A001"))
        for c in cards.dropFirst() {
            try FileManager.default.createDirectory(atPath: c + "/_REPORT", withIntermediateDirectories: true)
            try "<html/>".write(toFile: QueueRules.reportPath(cardPath: c, cardName: "A001"), atomically: true, encoding: .utf8)
        }
        XCTAssertEqual(QueueRules.firstReport(cardPaths: cards, cardName: "A001"), cards[1] + "/_REPORT/A001_report.html")
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter QueueRulesTests` → nesestaví se (`cannot find 'QueueRules'`, `'CardEject'`, `'CardIdentity'`, `VolumeItem` neexistuje v OffloadKit).

- [ ] **Step 3: `mac/Sources/OffloadKit/Desktop/Volumes.swift`** (úkol 10 sem přidá `enum Volumes`):

```swift
import Foundation

/// A mounted volume offered in the Karty section. `identity` (`CardIdentity`) tells two cards in the same slot
/// apart; `ejectProblem` (`CardEject.problem`) is nil when "Vysunout kartu" may eject it.
public struct VolumeItem: Identifiable, Hashable {
    public var id: String { path }
    public let name: String
    public let path: String
    public let total: Int64
    public let free: Int64
    public let format: String
    public let looksLikeCard: Bool
    public let isDestination: Bool
    public let identity: String?
    public let ejectProblem: String?

    public init(name: String, path: String, total: Int64, free: Int64, format: String, looksLikeCard: Bool,
                isDestination: Bool, identity: String? = nil, ejectProblem: String? = nil) {
        self.name = name
        self.path = path
        self.total = total
        self.free = free
        self.format = format
        self.looksLikeCard = looksLikeCard
        self.isDestination = isDestination
        self.identity = identity
        self.ejectProblem = ejectProblem
    }
}
```

V `mac/Sources/OffloadApp/AppModel.swift` smazat celou `struct VolumeItem: Identifiable, Hashable { … }` (aplikace teď používá tu z OffloadKit; volání `VolumeItem(name:path:total:free:format:looksLikeCard:isDestination:)` zůstává platné).

- [ ] **Step 4: `mac/Sources/OffloadKit/Desktop/QueueRules.swift`**:

```swift
import AppKit
import Foundation

/// Which card is mounted at a path: the volume UUID (APFS/HFS+), or the one macOS derives from the FAT/exFAT
/// serial number. A card reader mounts every card at the same kind of path; two formatted cards differ here.
public enum CardIdentity {
    /// nil when there is no readable volume at `path`. Touches the drive — never call it on the main thread.
    public static func of(_ path: String) -> String? {
        guard FileManager.default.fileExists(atPath: path),
              let v = try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeUUIDStringKey, .volumeCreationDateKey])
        else { return nil }
        if let uuid = v.volumeUUIDString, !uuid.isEmpty { return "uuid:" + uuid }
        if let created = v.volumeCreationDate { return String(format: "created:%.0f", created.timeIntervalSince1970) }
        return nil
    }
}

/// Safely removes a card: `NSWorkspace.unmountAndEjectDevice` unmounts the volume and ejects the media — a card in a
/// reader leaves the reader connected for the next card; a USB disk is ejected as a whole device.
public enum CardEject {
    /// Why this volume must not be ejected, or nil. Allowed only for a local, whole, non-system volume that is
    /// ejectable, removable or external (USB, Thunderbolt, SD).
    public static func problem(exists: Bool, isVolumeRoot: Bool, isLocal: Bool, isRootFileSystem: Bool,
                               isInternal: Bool?, isEjectable: Bool?, isRemovable: Bool?) -> String? {
        guard exists else { return "karta není připojená" }
        guard isLocal else { return "síťová složka se nevysouvá" }
        guard isVolumeRoot else { return "zdroj je složka, ne celá karta" }
        if isRootFileSystem { return "systémový disk nelze vysunout" }
        if !(isEjectable == true || isRemovable == true || isInternal == false) { return "interní disk nelze vysunout" }
        return nil
    }

    /// Reads the volume at `source`. Touches the disk — never on the main thread.
    public static func problem(source: String) -> String? {
        let url = URL(fileURLWithPath: source)
        let keys: Set<URLResourceKey> = [.volumeURLKey, .volumeIsLocalKey, .volumeIsRootFileSystemKey, .volumeIsInternalKey,
                                         .volumeIsEjectableKey, .volumeIsRemovableKey]
        guard FileManager.default.fileExists(atPath: source), let v = try? url.resourceValues(forKeys: keys) else {
            return problem(exists: false, isVolumeRoot: false, isLocal: true, isRootFileSystem: false,
                           isInternal: nil, isEjectable: nil, isRemovable: nil)
        }
        let isRoot = v.volume.map { $0.standardizedFileURL.path == url.standardizedFileURL.path } ?? false
        return problem(exists: true, isVolumeRoot: isRoot, isLocal: v.volumeIsLocal ?? true,
                       isRootFileSystem: v.volumeIsRootFileSystem ?? false, isInternal: v.volumeIsInternal,
                       isEjectable: v.volumeIsEjectable, isRemovable: v.volumeIsRemovable)
    }

    /// Never throws; the error completes "Kartu se nepodařilo vysunout: …". Call off the main thread.
    public static func eject(_ source: String) -> (ok: Bool, error: String?) {
        if let p = problem(source: source) { return (false, p) }
        do {
            try NSWorkspace.shared.unmountAndEjectDevice(at: URL(fileURLWithPath: source))
            return (true, nil)
        } catch {
            return (false, error.localizedDescription)
        }
    }
}

/// Decisions of the queue panel that need no window (port of Windows `Desktop/QueueRules.cs`).
public enum QueueRules {
    public static let otherCardText = "karta v mechanice není ta, která se kopírovala"
    public static let inUseText = "z karty ještě kopíruje jiná úloha — vysuňte ji, až její kopie doběhne"

    /// Why "Vysunout kartu" must not eject, or nil. `copied` = the card recorded when the copy started,
    /// `current` = the card at the source now (both `CardIdentity`), `cardInUse` = `cardInUse` for the other jobs.
    public static func ejectProblem(copied: String?, current: String?, cardInUse: Bool) -> String? {
        guard let current else { return "karta není připojená" }
        guard let copied, copied.caseInsensitiveCompare(current) == .orderedSame else { return otherCardText }
        return cardInUse ? inUseText : nil
    }

    /// Another job still needs the card: it is queued or in stage 1 (copy, flush, verification, report) on the same
    /// volume. Stage 2 (clips, proxies) reads only the destinations, so it does not hold the card.
    public static func cardInUse(_ source: String, others: [(source: String, phase: JobStatus.Phase)]) -> Bool {
        let holding: Set<JobStatus.Phase> = [.waiting, .copying, .flushing, .verifying, .reporting]
        return others.contains { o in holding.contains(o.phase) && (inside(o.source, source) || inside(source, o.source)) }
    }

    /// The last scan shows the copied card at `source` (not gone, not another card in the same slot). Pure.
    public static func cardInDrive(_ source: String, copied: String?, scanned: [VolumeItem]) -> Bool {
        guard let copied else { return false }
        return scanned.contains { $0.path == trimmed(source) && $0.identity?.caseInsensitiveCompare(copied) == .orderedSame }
    }

    /// Measuring writes 1 GB to the destinations — not while a copy writes or verifies them.
    public static func measureBlocked(_ phases: [JobStatus.Phase]) -> Bool {
        phases.contains { $0 == .copying || $0 == .flushing || $0 == .verifying }
    }

    public static func reportPath(cardPath: String, cardName: String) -> String {
        cardPath + "/" + Layout.reportDirName + "/" + cardName + "_report.html"
    }

    /// The HTML report from the first destination that has it (one may be disconnected), or nil.
    /// Touches the destinations — never call it on the main thread.
    public static func firstReport(cardPaths: [String], cardName: String) -> String? {
        cardPaths.map { reportPath(cardPath: $0, cardName: cardName) }.first { FileManager.default.fileExists(atPath: $0) }
    }

    static func trimmed(_ path: String) -> String {
        var p = path
        while p.count > 1 && p.hasSuffix("/") { p.removeLast() }
        return p
    }

    /// `path` is `volume` or inside it (by path; no disk access).
    static func inside(_ path: String, _ volume: String) -> Bool {
        let p = trimmed(path), v = trimmed(volume)
        return p == v || p.hasPrefix(v == "/" ? "/" : v + "/")
    }
}
```

Pozn.: `inside(_, "/")` by vrátilo `true` pro cokoli — `cardInUse` se volá jen pro zdroj, který je vysunutelný svazek (`/Volumes/…`), nikdy `/`.

- [ ] **Step 5: Testy projdou** — Run: `swift test --filter QueueRulesTests` → 10 testů, 0 chyb. Pokud `testCardIdentity…` hlásí `nil`: spusťte `diskutil info -plist / | grep -A1 VolumeUUID` a zapište výsledek do commitu (spec 9).

- [ ] **Step 6: Ověření identity karty na Macu (ručně, s kartou ve čtečce)**

Run: `diskutil info -plist /Volumes/<karta> | plutil -extract VolumeUUID raw -` pro kartu FAT32 i exFAT (dvě různé karty).
Expected: obě vrátí UUID a liší se. Pak `swift test --filter QueueRulesTests` a v `swift repl` / malém skriptu `CardIdentity.of("/Volumes/<karta>")` vrátí `uuid:<totéž UUID>`. Když FAT/exFAT UUID nemá, identita padne na `created:<čas>` — zapište do commitu.

- [ ] **Step 7: Build** — Run: `swift build` (aplikace se sestaví s `VolumeItem` z OffloadKit).

- [ ] **Step 8: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 9 — pravidla vysunutí: jen karta, která se kopírovala (UUID svazku), nikdy složka, síťový ani interní disk, ne dokud z karty kopíruje jiná úloha; report z prvního cíle, který ho má`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: eject rules (card identity, folder/internal refused, card in use), first report lookup"`

---

### Task 10: Nastavení odolné vůči chybám, kontrola LUT, svazky mimo hlavní vlákno

**Files:**
- Create: `mac/Sources/OffloadKit/Desktop/AppSettings.swift` (`AppSettings` přesunuté z aplikace + `portNames`, `SettingsStore`, `ProxyPreflight`)
- Modify: `mac/Sources/OffloadKit/Desktop/Volumes.swift` (přidat `enum Volumes`)
- Modify: `mac/Sources/OffloadApp/AppModel.swift` (smazat `struct AppSettings`, `looksLikeCameraCard`, `filesystemName`; načtení/uložení nastavení; `refreshVolumes` na pozadí)
- Create: `mac/Tests/OffloadKitTests/SettingsStoreTests.swift`, `mac/Tests/OffloadKitTests/VolumesTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `CardIdentity.of`, `CardEject.problem(exists:…)` (úkol 9), `IO.filesystemType`, `ProxySettings.init(from:)` (tolerantní).
- Produces:
  - `public struct AppSettings: Codable, Equatable { destRoots, verify, analyzeClips, proxy, tools, portNames: [String: String]; public init() }`
  - `public enum SettingsStore { static let proxyHeights; static let defaultsKey = "settings"; static func decode(_ data: Data?) -> AppSettings; static func encode(_ s: AppSettings) -> Data? }`
  - `public enum ProxyPreflight { lutInUse(_:analyzeClips:); blocking(_:analyzeClips:lutExists:); warning(_:analyzeClips:resolved:) }`
  - `public enum Volumes { looksLikeCameraCard(_:); selectionAfterRefresh(previousPaths:found:selected:manual:) -> Set<String>; scan(destRoots:) -> [VolumeItem] }`

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/SettingsStoreTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class SettingsStoreTests: XCTestCase {
    private func load(_ json: String) -> AppSettings { SettingsStore.decode(Data(json.utf8)) }

    func testMissingOrCorruptGivesDefaults() {
        XCTAssertEqual(SettingsStore.decode(nil), AppSettings())
        XCTAssertEqual(load(#"{"verify":tru"#), AppSettings())
        XCTAssertEqual(load("[1,2,3]"), AppSettings())
        XCTAssertEqual(load("null"), AppSettings())
    }

    // Review focus 3: what version 0.1.0 stored (no portNames) keeps the destinations after the update.
    func testOldMacSettingsWithoutPortNamesKeepDestinations() throws {
        var old = AppSettings()
        old.destRoots = ["/Volumes/RAID_A", "/Volumes/SHUTTLE_B"]
        old.verify = false
        old.proxy.enabled = true
        old.proxy.codec = .proresLT
        var obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: try JSONEncoder().encode(old)) as? [String: Any])
        obj["portNames"] = nil
        let s = SettingsStore.decode(try JSONSerialization.data(withJSONObject: obj))
        XCTAssertEqual(s.destRoots, ["/Volumes/RAID_A", "/Volumes/SHUTTLE_B"])
        XCTAssertFalse(s.verify)
        XCTAssertTrue(s.proxy.enabled)
        XCTAssertEqual(s.proxy.codec, .proresLT)
        XCTAssertEqual(s.portNames, [:])
    }

    func testPartialAndUnknownKeys() {
        let s = load(#"{"destRoots":["/Volumes/A","/volumes/a"," "],"verify":false,"proxy":{"enabled":true,"codec":"proresLT"},"tools":{"ffmpeg":"/x/ffmpeg"},"futureKey":42}"#)
        XCTAssertEqual(s.destRoots, ["/Volumes/A"])
        XCTAssertFalse(s.verify)
        XCTAssertTrue(s.analyzeClips)
        XCTAssertTrue(s.proxy.enabled)
        XCTAssertEqual(s.proxy.codec, .proresLT)
        XCTAssertEqual(s.proxy.height, 1080)
        XCTAssertTrue(s.proxy.burnTimecode)
        XCTAssertEqual(s.tools.ffmpeg, "/x/ffmpeg")
    }

    // Review focus 3: one bad section never costs the others.
    func testBadSectionKeepsTheRest() {
        let s = load(#"{"destRoots":["/Volumes/Backup"],"proxy":{"codec":"av1"},"verify":"yes","portNames":{"A#1":"vpředu"}}"#)
        XCTAssertEqual(s.destRoots, ["/Volumes/Backup"])
        XCTAssertEqual(s.proxy.codec, .h264)
        XCTAssertTrue(s.verify)
        XCTAssertEqual(s.portNames["A#1"], "vpředu")
    }

    func testToolsOfAWrongTypeFallBackToDefaults() {
        let s = load(#"{"tools":{"ffmpeg":5},"verify":false}"#)
        XCTAssertEqual(s.tools, ExternalTools())
        XCTAssertFalse(s.verify)
    }

    func testDestRootsNonStringEntriesDropped() {
        XCTAssertEqual(load(#"{"destRoots":[null,"/A",5,{"x":1},"/B"]}"#).destRoots, ["/A", "/B"])
        XCTAssertEqual(load(#"{"destRoots":"/A"}"#).destRoots, [])
    }

    func testPortNamesTrimmedAndEmptyOrNonStringDropped() {
        let s = load(#"{"portNames":{"X#1":" přední ","X#2":null,"X#3":7,"":"a","X#4":"  "}}"#)
        XCTAssertEqual(s.portNames, ["X#1": "přední"])
    }

    func testOutOfRangeClamped() {
        let s = load(#"{"proxy":{"height":999,"bitrateMbps":500,"frameScale":0.1,"frameAspect":-1,"lutPath":""}}"#)
        XCTAssertEqual(s.proxy.height, 1080)
        XCTAssertEqual(s.proxy.bitrateMbps, 50)
        XCTAssertEqual(s.proxy.frameScale, 0.5)
        XCTAssertNil(s.proxy.frameAspect)
        XCTAssertNil(s.proxy.lutPath)
    }

    func testRoundTrip() {
        var s = AppSettings()
        s.destRoots = ["/Volumes/A"]
        s.portNames = ["loc:14100000#1": "přední levý"]
        s.proxy.lutPath = "/Users/x/look.cube"
        XCTAssertEqual(SettingsStore.decode(SettingsStore.encode(s)), s)
    }

    func testMissingLutBlocksWhenProxyOrAnalysisUseIt() {
        var p = ProxySettings()
        p.lutPath = "/nope/look.cube"
        XCTAssertEqual(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: false),
                       "LUT nenalezen: look.cube — vyberte jiný nebo ho odeberte.")
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: false, lutExists: false))   // nothing uses it
        p.enabled = true
        XCTAssertNotNil(ProxyPreflight.blocking(p, analyzeClips: false, lutExists: false))
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: nil))      // not checked yet
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: true))
    }

    func testMissingFFmpegOnlyWarns() {
        XCTAssertEqual(ProxyPreflight.warning(ProxySettings(), analyzeClips: true, resolved: ExternalTools()),
                       "ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou.")
        XCTAssertNil(ProxyPreflight.warning(ProxySettings(), analyzeClips: false, resolved: ExternalTools()))
        XCTAssertNil(ProxyPreflight.warning(ProxySettings(), analyzeClips: true, resolved: ExternalTools(ffmpeg: "/f", ffprobe: "/p")))
    }
}
```

`mac/Tests/OffloadKitTests/VolumesTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class VolumesTests: XCTestCase {
    private func touch(_ path: String) {
        try! FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try! "x".write(toFile: path, atomically: true, encoding: .utf8)
    }

    func testLooksLikeCardMarkerFolderAnyCase() {
        let dir = tempDir()
        dir.sub("dcim")
        XCTAssertTrue(Volumes.looksLikeCameraCard(dir.path))
    }

    func testLooksLikeCardMediaAtRootOrOneLevelDown() {
        let a = tempDir()
        touch(a.path + "/A001.MOV")
        XCTAssertTrue(Volumes.looksLikeCameraCard(a.path))
        let b = tempDir()
        touch(b.path + "/A001/A001C001.braw")
        XCTAssertTrue(Volumes.looksLikeCameraCard(b.path))
        let red = tempDir()
        red.sub("A001_C001.RDM")
        XCTAssertTrue(Volumes.looksLikeCameraCard(red.path))
    }

    func testLooksLikeCardNo() {
        let deep = tempDir()
        touch(deep.path + "/a/b/c.mxf")
        touch(deep.path + "/notes.txt")
        touch(deep.path + "/.hidden/x.mov")
        XCTAssertFalse(Volumes.looksLikeCameraCard(deep.path))
        XCTAssertFalse(Volumes.looksLikeCameraCard(deep.path + "/missing"))
    }

    func testSelectionAfterRefresh() {
        func v(_ path: String, card: Bool = true, dest: Bool = false) -> VolumeItem {
            VolumeItem(name: path, path: path, total: 100, free: 50, format: "exfat", looksLikeCard: card && !dest, isDestination: dest)
        }
        let found = [v("/Volumes/A001"), v("/Volumes/B"), v("/Volumes/RAID", dest: true)]
        let sel = Volumes.selectionAfterRefresh(previousPaths: ["/Volumes/A001", "/Volumes/RAID", "/Volumes/GONE"], found: found,
                                                selected: ["/Volumes/GONE", "/Volumes/RAID", "/Users/x/card"], manual: ["/Users/x/card"])
        // New card ticked; A001 was there before and unticked — stays unticked; gone and destination dropped; manual kept.
        XCTAssertEqual(sel, ["/Volumes/B", "/Users/x/card"])
    }

    func testScanNeverOffersTheStartupVolumeAndDoesNotThrow() {
        let found = Volumes.scan(destRoots: [])
        XCTAssertFalse(found.contains { $0.path == "/" })
        for v in found { XCTAssertTrue(v.path.hasPrefix("/Volumes/"), v.path) }
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "SettingsStoreTests|VolumesTests"` → nesestaví se (`cannot find 'SettingsStore'`, `'Volumes'`, `AppSettings` není v OffloadKit).

- [ ] **Step 3: `mac/Sources/OffloadKit/Desktop/AppSettings.swift`**:

```swift
import Foundation

/// The app's settings (UserDefaults key "settings", JSON). Same keys as Windows settings.json.
/// `portNames`: the user's names for USB sockets (`UsbConnector.id` → name).
public struct AppSettings: Codable, Equatable {
    public var destRoots: [String] = []
    public var verify = true
    public var analyzeClips = true
    public var proxy = ProxySettings()
    public var tools = ExternalTools()
    public var portNames: [String: String] = [:]

    public init() {}
}

/// Reading settings never fails and never loses the good parts (port of Windows `SettingsStore.Load`):
/// each top-level section is read on its own; a section that does not parse keeps its defaults.
public enum SettingsStore {
    public static let proxyHeights = [540, 720, 1080, 2160]
    public static let defaultsKey = "settings"

    public static func decode(_ data: Data?) -> AppSettings {
        var s = AppSettings()
        guard let data, let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return s }
        if let roots = root["destRoots"] as? [Any] { s.destRoots = roots.compactMap { $0 as? String } }
        if let v = bool(root["verify"]) { s.verify = v }
        if let v = bool(root["analyzeClips"]) { s.analyzeClips = v }
        if let p = section(root["proxy"], as: ProxySettings.self) { s.proxy = p }
        if let t = section(root["tools"], as: ExternalTools.self) { s.tools = t }
        if let names = root["portNames"] as? [String: Any] { s.portNames = names.compactMapValues { $0 as? String } }
        return normalize(s)
    }

    public static func encode(_ s: AppSettings) -> Data? { try? JSONEncoder().encode(s) }

    /// Only a real JSON boolean (not 1, not "yes").
    static func bool(_ v: Any?) -> Bool? {
        guard let n = v as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
        return n.boolValue
    }

    static func section<T: Decodable>(_ v: Any?, as type: T.Type) -> T? {
        guard let dict = v as? [String: Any], let data = try? JSONSerialization.data(withJSONObject: dict) else { return nil }
        return try? JSONDecoder().decode(T.self, from: data)
    }

    static func normalize(_ input: AppSettings) -> AppSettings {
        var s = input
        var roots: [String] = []
        for r in s.destRoots.map({ $0.trimmingCharacters(in: .whitespaces) }) where !r.isEmpty {
            if !roots.contains(where: { $0.caseInsensitiveCompare(r) == .orderedSame }) { roots.append(r) }
        }
        s.destRoots = roots
        var names: [String: String] = [:]
        for (id, name) in s.portNames {
            let i = id.trimmingCharacters(in: .whitespaces), n = name.trimmingCharacters(in: .whitespaces)
            if !i.isEmpty && !n.isEmpty { names[i] = n }
        }
        s.portNames = names
        if !proxyHeights.contains(s.proxy.height) { s.proxy.height = 1080 }
        s.proxy.bitrateMbps = min(max(s.proxy.bitrateMbps, 2), 50)
        s.proxy.frameScale = s.proxy.frameScale.isFinite ? min(max(s.proxy.frameScale, 0.5), 1.0) : 1.0
        if let a = s.proxy.frameAspect, !a.isFinite || a <= 0 { s.proxy.frameAspect = nil }
        if let l = s.proxy.lutPath, l.trimmingCharacters(in: .whitespaces).isEmpty { s.proxy.lutPath = nil }
        return s
    }
}

/// Checks before a start that the proxy/thumbnail settings can work.
public enum ProxyPreflight {
    /// The LUT the next start needs (proxies or thumbnails use it), or nil.
    public static func lutInUse(_ p: ProxySettings, analyzeClips: Bool) -> String? {
        (p.enabled || analyzeClips) ? p.lutPath : nil
    }

    /// Why "Spustit offload" must stay disabled, or nil. `lutExists` nil = not checked yet (does not block).
    public static func blocking(_ p: ProxySettings, analyzeClips: Bool, lutExists: Bool?) -> String? {
        guard let lut = lutInUse(p, analyzeClips: analyzeClips), lutExists == false else { return nil }
        return "LUT nenalezen: \((lut as NSString).lastPathComponent) — vyberte jiný nebo ho odeberte."
    }

    /// A warning that does not block (copy and verification still run), or nil.
    public static func warning(_ p: ProxySettings, analyzeClips: Bool, resolved: ExternalTools) -> String? {
        (p.enabled || analyzeClips) && (resolved.ffmpeg == nil || resolved.ffprobe == nil)
            ? "ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou." : nil
    }
}
```

- [ ] **Step 4: `Volumes.swift`** — pod `VolumeItem` přidat:

```swift
public enum Volumes {
    static let markers: Set<String> = ["DCIM", "PRIVATE", "CLIP", "CONTENTS", "XDROOT", "M4ROOT", "AVF_INFO", "CANONMSC", "BRAW", "MISC"]
    static let mediaExt: Set<String> = ["braw", "r3d", "rdm", "rdc", "mxf", "mov", "mp4", "ari", "crm"]

    /// Typical structure of camera media: known top-level folders, or clips at most one folder down
    /// (ARRI/RED/Blackmagic keep each clip or reel in its own folder). Moved from the app, same rules.
    public static func looksLikeCameraCard(_ path: String) -> Bool {
        let fm = FileManager.default
        guard let items = try? fm.contentsOfDirectory(atPath: path) else { return false }
        if items.contains(where: { markers.contains($0.uppercased()) }) { return true }
        func hasMedia(_ names: [String]) -> Bool { names.contains { mediaExt.contains(($0 as NSString).pathExtension.lowercased()) } }
        if hasMedia(items) { return true }
        for dir in items.prefix(50) where !dir.hasPrefix(".") {
            if let sub = try? fm.contentsOfDirectory(atPath: path + "/" + dir), hasMedia(sub) { return true }
        }
        return false
    }

    /// The selection after a rescan: a card that just appeared (a new path) is ticked; a source that disappeared or
    /// now holds a destination drops out; manual folders stay.
    public static func selectionAfterRefresh(previousPaths: [String], found: [VolumeItem], selected: Set<String>,
                                             manual: [String]) -> Set<String> {
        let previous = Set(previousPaths)
        var result = selected
        for v in found where v.looksLikeCard && !previous.contains(v.path) { result.insert(v.path) }
        return result.filter { p in found.contains { $0.path == p && !$0.isDestination } || manual.contains(p) }
    }

    /// Card candidates under /Volumes (external, ejectable or removable; never the startup volume), sorted by name.
    /// Touches every mounted volume — never call it on the main thread.
    public static func scan(destRoots: [String]) -> [VolumeItem] {
        let keys: [URLResourceKey] = [.volumeNameKey, .volumeIsInternalKey, .volumeIsEjectableKey, .volumeIsRemovableKey,
                                      .volumeTotalCapacityKey, .volumeAvailableCapacityKey, .volumeIsRootFileSystemKey,
                                      .volumeIsLocalKey]
        let urls = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes]) ?? []
        var found: [VolumeItem] = []
        for url in urls {
            guard let v = try? url.resourceValues(forKeys: Set(keys)) else { continue }
            if v.volumeIsRootFileSystem == true { continue }
            let external = v.volumeIsInternal == false || v.volumeIsEjectable == true || v.volumeIsRemovable == true
            guard external, url.path.hasPrefix("/Volumes/") else { continue }
            let path = url.path
            let isDest = destRoots.contains { $0 == path || $0.hasPrefix(path + "/") }
            let problem = CardEject.problem(exists: true, isVolumeRoot: true, isLocal: v.volumeIsLocal ?? true,
                                            isRootFileSystem: false, isInternal: v.volumeIsInternal,
                                            isEjectable: v.volumeIsEjectable, isRemovable: v.volumeIsRemovable)
            found.append(VolumeItem(name: v.volumeName ?? url.lastPathComponent, path: path,
                                    total: Int64(v.volumeTotalCapacity ?? 0), free: Int64(v.volumeAvailableCapacity ?? 0),
                                    format: IO.filesystemType(path), looksLikeCard: !isDest && looksLikeCameraCard(path),
                                    isDestination: isDest, identity: CardIdentity.of(path), ejectProblem: problem))
        }
        return found.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }
}
```

- [ ] **Step 5: Aplikace (`AppModel.swift`)**

1. Smazat `struct AppSettings: Codable, Equatable { … }` (je v OffloadKit).
2. Smazat `private func filesystemName(_:)` a `private func looksLikeCameraCard(_:)`.
3. Přidat vlastnost `private let probeQueue = DispatchQueue(label: "app.probe", qos: .userInitiated)` k ostatním frontám.
4. V `init()` nahradit načtení nastavení:

```swift
        settings = SettingsStore.decode(UserDefaults.standard.data(forKey: SettingsStore.defaultsKey))
```

5. `save()`:

```swift
    private func save() {
        if let data = SettingsStore.encode(settings) { UserDefaults.standard.set(data, forKey: SettingsStore.defaultsKey) }
        let t = settings.tools.resolved()
        if t != tools { tools = t }
    }
```

6. `refreshVolumes()` (nová podoba) a nová `applyVolumes`:

```swift
    /// Scans the mounted volumes on a background queue (a card is read to see whether it looks like one).
    func refreshVolumes() {
        let roots = settings.destRoots
        probeQueue.async { [weak self] in
            let found = Volumes.scan(destRoots: roots)
            DispatchQueue.main.async { self?.applyVolumes(found) }
        }
    }

    private func applyVolumes(_ found: [VolumeItem]) {
        selectedSources = Volumes.selectionAfterRefresh(previousPaths: volumes.map(\.path), found: found,
                                                        selected: selectedSources, manual: manualSources)
        volumes = found
    }
```

- [ ] **Step 6: Testy projdou** — Run: `swift test --filter "SettingsStoreTests|VolumesTests"` → 11 + 5 testů, 0 chyb; pak `swift test`.

- [ ] **Step 7: Build a ruční kontrola aplikace**

Run: `scripts/build-app.sh && open build/Offload.app`
Expected: aplikace naběhne s dříve uloženými cíli (stejný klíč `settings` v UserDefaults domény `cz.budin.offload`; při spuštění z `swift run` je doména jiná — testujte zabalenou aplikaci). Vložená karta se objeví a zaškrtne; když ji uživatel odškrtne, další sken (připojení jiného disku) ji znovu nezaškrtne.

Kontrola zálohy nastavení před testem: `defaults read cz.budin.offload settings > /tmp/offload-settings-backup.txt`.

- [ ] **Step 8: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 10 — nastavení se čte po sekcích (vadná sekce ani nový klíč nesmažou cíle), kontrola LUT před startem, sken svazků mimo hlavní vlákno a výběr nové karty podle cesty`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: tolerant settings, LUT preflight, volume scan off the main thread"`

---

### Task 11: GUI — karta úlohy, vysunutí, disky mimo hlavní vlákno, zavření během kopie

**Files:**
- Modify (celý soubor): `mac/Sources/OffloadApp/AppModel.swift`
- Modify (celý soubor): `mac/Sources/OffloadApp/OffloadApp.swift`
- Modify: `mac/Sources/OffloadApp/Views.swift` (`DestinationsSection`, `StructureSection`, `ProxySection` řádek LUT, `StartBar`, `JobCard`)
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `JobProgress`, `CardTone`, `Texts` (úkol 8), `CardIdentity`, `CardEject`, `QueueRules`, `VolumeItem` (úkol 9), `SettingsStore`, `ProxyPreflight`, `Volumes` (úkol 10), `OffloadJob` (úkol 4).
- Produces (aplikace):
  - `struct DestinationStatus { free; format; exists; checking }`
  - `final class JobItem { job; sourceName; canEject; ejected; ejecting; status; plan; report; isRunning; isFinished; isCancelled; phaseProgress; detailText; statusText; tone; isError; copiedIdentity; recordCopiedCard(); refresh(); safeText; failedText; cancelledText; summaryText }`
  - `AppModel`: `destStatus`, `lutProblem`, `refreshDestinations()`, `destinationInfo(_:) -> DestinationStatus`, `canShowEject(_:)`, `eject(_:)`, `openReport(_:)`, `hasBusyJobs`, `shouldTerminate() -> NSApplication.TerminateReply`, `copyRunning`
  - `AppDelegate.model`

- [ ] **Step 1: Test-first** — logika je pokrytá testy úkolů 8–10 (`JobProgressTests`, `QueueRulesTests`, `SettingsStoreTests`); GUI se ověřuje ručně (kroky 6–7). Run: `swift test` → vše zelené před změnou.

- [ ] **Step 2: `mac/Sources/OffloadApp/AppModel.swift`** — celý nový obsah:

```swift
import AppKit
import Combine
import OffloadKit
import SwiftUI

/// Free space, filesystem and existence of a destination — probed off the main thread.
struct DestinationStatus: Equatable {
    var free: Int64 = 0
    var format = ""
    var exists = false
    var checking = true
}

/// One card in the queue; mirrors the job's status for SwiftUI. Main thread only, except `recordCopiedCard`.
final class JobItem: ObservableObject, Identifiable {
    let id = UUID()
    let job: OffloadJob
    let sourceName: String
    /// The source is a whole card volume that may be ejected (from the volume scan when the job was created).
    let canEject: Bool
    @Published var ejected = false
    @Published var ejecting = false
    private let progress = JobProgress(now: Date())
    private let lock = NSLock()
    private var copied: String?

    init(job: OffloadJob, sourceName: String, canEject: Bool) {
        self.job = job
        self.sourceName = sourceName
        self.canEject = canEject
    }

    var plan: OffloadPlan? { job.plan }
    var report: OffloadReport? { job.report }
    var status: JobStatus { progress.status }
    var isRunning: Bool { progress.isRunning }
    var isFinished: Bool { progress.status.phase.isFinished }
    var isCancelled: Bool { progress.status.phase == .cancelled }
    var phaseProgress: Double { progress.phaseProgress }
    var detailText: String { progress.detailText }
    var statusText: String { progress.statusText }
    var tone: CardTone { progress.tone }
    var isError: Bool { progress.isError }

    /// The card recorded right before the copy (`CardIdentity`); nil before or when unknown.
    var copiedIdentity: String? { lock.lock(); defer { lock.unlock() }; return copied }

    /// On the copy queue, right before `runCopy` — never on the main thread (it reads the card's volume).
    func recordCopiedCard() {
        guard canEject else { return }
        let id = CardIdentity.of(job.config.source)
        lock.lock(); copied = id; lock.unlock()
    }

    func refresh() {
        objectWillChange.send()
        progress.update(job.status(), now: Date())
    }

    var safeText: String { Texts.safeToEject(plan?.cardPaths.count ?? 0) }

    /// Not for a cancelled job: it shows one orange line instead (a cut-short copy is not "N souborů selhalo").
    var failedText: String? {
        guard !isCancelled, let r = report, !r.success else { return nil }
        return Texts.failed(r.failedFiles.count)
    }

    var cancelledText: String? { isCancelled && !status.safeToEject ? Texts.cancelled : nil }

    /// No rate summary for a cancelled job — the rate of a cut-short copy would mislead.
    var summaryText: String? {
        guard isFinished, !isCancelled, let r = report else { return nil }
        return JobProgress.summaryText(r)
    }
}

@MainActor
final class AppModel: ObservableObject {
    @Published var settings: AppSettings { didSet { save() } }
    @Published var volumes: [VolumeItem] = []
    @Published var selectedSources: Set<String> = []
    @Published var manualSources: [String] = []
    @Published var jobs: [JobItem] = []
    @Published var useCustomDate = false
    @Published var customDate = Date()
    @Published var cardNames: [String: String] = [:]
    @Published var tools = ExternalTools()
    @Published var message: String?
    @Published private(set) var destStatus: [String: DestinationStatus] = [:]
    @Published private(set) var lutExists: Bool?
    @Published private(set) var terminating = false
    private var lutChecked: String?
    private var probedRoots: [String] = []
    private var probedLut: String?
    /// One destination check in flight, at most one more requested — a hung NAS never stacks a check every 5 s.
    private var probing = false
    private var probeAgain = false

    private let copyQueue = DispatchQueue(label: "app.copy", qos: .userInitiated)
    private let postQueue = DispatchQueue(label: "app.post", qos: .utility)
    /// Volume scans (cards). Separate from destination checks, so a hung network destination never blocks them.
    private let probeQueue = DispatchQueue(label: "app.probe", qos: .userInitiated)
    /// Destination and LUT checks; a hung network disk stalls only this queue, never the window or the card scan.
    private let destQueue = DispatchQueue(label: "app.dest", qos: .utility)
    private var timers: [Timer] = []
    private var observers: [NSObjectProtocol] = []

    init() {
        settings = SettingsStore.decode(UserDefaults.standard.data(forKey: SettingsStore.defaultsKey))
        tools = settings.tools.resolved()
        refreshVolumes()
        refreshDestinations()
        let nc = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didMountNotification, NSWorkspace.didUnmountNotification, NSWorkspace.didRenameVolumeNotification] {
            observers.append(nc.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                Task { @MainActor in
                    self?.refreshVolumes()
                    self?.refreshDestinations()
                }
            })
        }
        // Common modes: the ticks keep coming while a menu or an alert is open.
        let tick = Timer(timeInterval: 0.33, repeats: true) { [weak self] _ in Task { @MainActor in self?.tick() } }
        let health = Timer(timeInterval: 5, repeats: true) { [weak self] _ in Task { @MainActor in self?.refreshDestinations() } }
        for t in [tick, health] {
            RunLoop.main.add(t, forMode: .common)
            timers.append(t)
        }
    }

    private func save() {
        if let data = SettingsStore.encode(settings) { UserDefaults.standard.set(data, forKey: SettingsStore.defaultsKey) }
        let t = settings.tools.resolved()
        if t != tools { tools = t }
        if settings.destRoots != probedRoots || ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips) != probedLut {
            refreshDestinations()
        }
    }

    private func tick() {
        for j in jobs where !j.isFinished { j.refresh() }
    }

    var dateString: String {
        guard useCustomDate else { return Layout.today() }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f.string(from: customDate)
    }

    // MARK: Volumes and destinations (always off the main thread)

    func refreshVolumes() {
        let roots = settings.destRoots
        probeQueue.async { [weak self] in
            let found = Volumes.scan(destRoots: roots)
            DispatchQueue.main.async { self?.applyVolumes(found) }
        }
    }

    private func applyVolumes(_ found: [VolumeItem]) {
        selectedSources = Volumes.selectionAfterRefresh(previousPaths: volumes.map(\.path), found: found,
                                                        selected: selectedSources, manual: manualSources)
        volumes = found
        for j in jobs { j.objectWillChange.send() }   // "Vysunout kartu" follows the scan
    }

    /// Free space / filesystem / existence of every destination and whether the LUT exists — every 5 s,
    /// after a change and from the refresh button.
    func refreshDestinations() {
        if probing {
            probeAgain = true   // runs once the check in flight returns (it may hang on a network disk)
            return
        }
        probing = true
        let roots = settings.destRoots
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        probedRoots = roots
        probedLut = lut
        destQueue.async { [weak self] in
            var found: [String: DestinationStatus] = [:]
            for r in roots { found[r] = AppModel.probe(r) }
            let lutOK = lut.map { FileManager.default.fileExists(atPath: $0) }
            DispatchQueue.main.async {
                guard let self else { return }
                self.destStatus = found
                self.lutExists = lutOK
                self.lutChecked = lut
                self.probing = false
                if self.probeAgain {
                    self.probeAgain = false
                    self.refreshDestinations()
                }
            }
        }
    }

    nonisolated static func probe(_ path: String) -> DestinationStatus {
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue else {
            return DestinationStatus(checking: false)
        }
        var fs = statfs()
        guard statfs(path, &fs) == 0 else { return DestinationStatus(checking: false) }
        let format = withUnsafeBytes(of: fs.f_fstypename) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
        return DestinationStatus(free: Int64(fs.f_bavail) * Int64(fs.f_bsize), format: format, exists: true, checking: false)
    }

    func destinationInfo(_ path: String) -> DestinationStatus { destStatus[path] ?? DestinationStatus() }

    /// A missing LUT the next start needs — from the last background check; nil until checked.
    var lutProblem: String? {
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        return ProxyPreflight.blocking(settings.proxy, analyzeClips: settings.analyzeClips,
                                       lutExists: lut == lutChecked ? lutExists : nil)
    }

    func addManualSource() {
        guard let path = pickFolder(title: "Vyber kartu nebo složku se záznamem") else { return }
        if !manualSources.contains(path) { manualSources.append(path) }
        selectedSources.insert(path)
    }

    func addDestination() {
        guard let path = pickFolder(title: "Vyber cílovou složku (root)") else { return }
        if !settings.destRoots.contains(path) { settings.destRoots.append(path) }
        refreshVolumes()
    }

    func removeDestination(_ path: String) {
        settings.destRoots.removeAll { $0 == path }
        refreshVolumes()
    }

    func pickLUT() {
        let panel = NSOpenPanel()
        panel.title = "Vyber LUT (.cube)"
        panel.allowedContentTypes = [.init(filenameExtension: "cube")!]
        if panel.runModal() == .OK, let url = panel.url { settings.proxy.lutPath = url.path }
    }

    func pickTool(_ keyPath: WritableKeyPath<ExternalTools, String?>) {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        if panel.runModal() == .OK, let url = panel.url { settings.tools[keyPath: keyPath] = url.path }
    }

    private func pickFolder(title: String) -> String? {
        let panel = NSOpenPanel()
        panel.title = title
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        return panel.runModal() == .OK ? panel.url?.path : nil
    }

    /// The card folder name: the typed name (blank = default), else the default name.
    func sourceName(_ path: String) -> String {
        customName(path) ?? defaultName(path)
    }

    /// The default card folder name without touching the disk (main thread): the scanned volume's name, else the
    /// folder's name — what `Layout.defaultCardName(for:)` gives, which the engine itself still calls off the main thread.
    func defaultName(_ path: String) -> String {
        Layout.sanitize(volumes.first { $0.path == path }?.name ?? (path as NSString).lastPathComponent)
    }

    private func customName(_ path: String) -> String? {
        let t = cardNames[path]?.trimmingCharacters(in: .whitespaces) ?? ""
        return t.isEmpty ? nil : t
    }

    // MARK: Jobs

    var canStart: Bool {
        !terminating && !selectedSources.isEmpty && !settings.destRoots.isEmpty
            && settings.destRoots.allSatisfy { destStatus[$0]?.exists == true } && lutProblem == nil
    }

    /// A copy writes or verifies the destinations — no speed measurement now.
    var copyRunning: Bool { QueueRules.measureBlocked(jobs.map(\.status.phase)) }

    func start() {
        guard canStart else { return }
        let sources = (volumes.map(\.path) + manualSources).filter { selectedSources.contains($0) }
        for src in sources {
            var config = JobConfig(source: src, destRoots: settings.destRoots)
            config.cardName = customName(src)
            config.date = dateString
            config.verify = settings.verify
            config.analyzeClips = settings.analyzeClips
            config.proxy = settings.proxy
            config.tools = settings.tools
            let volume = volumes.first { $0.path == src }
            let item = JobItem(job: OffloadJob(config: config), sourceName: sourceName(src),
                               canEject: volume != nil && volume?.ejectProblem == nil)
            jobs.append(item)
            selectedSources.remove(src)
            // Copies run one after another (they share the destination disks); post-processing runs
            // in parallel on its own queue, so the next card starts copying while proxies render.
            copyQueue.async { [weak self] in
                // Cancelled while queued: it never runs, but the queue still reports it finished.
                if item.job.isCancelled {
                    DispatchQueue.main.async { self?.jobFinished(item) }
                    return
                }
                item.recordCopiedCard()
                let needsPost = item.job.runCopy()
                DispatchQueue.main.async {
                    item.refresh()
                    self?.copyFinished(item)
                }
                if needsPost {
                    self?.postQueue.async {
                        item.job.runPost()
                        DispatchQueue.main.async { self?.jobFinished(item) }
                    }
                } else {
                    DispatchQueue.main.async { self?.jobFinished(item) }
                }
            }
        }
    }

    private func copyFinished(_ item: JobItem) {
        guard !terminating, item.status.safeToEject else { return }
        NSSound(named: "Glass")?.play()
    }

    private func jobFinished(_ item: JobItem) {
        item.refresh()
        if !terminating {
            if item.isError { NSSound(named: "Basso")?.play() }   // a job the user cancelled is not an error
            NSApp.requestUserAttention(.informationalRequest)
        }
        checkTerminateReady()
    }

    func cancel(_ item: JobItem) {
        item.job.cancel()
        item.refresh()
    }

    func clearFinished() { jobs.removeAll { $0.isFinished && !$0.ejecting } }

    /// "Vysunout kartu" only while the copied card is in its slot per the last scan.
    func canShowEject(_ item: JobItem) -> Bool {
        item.status.safeToEject && !item.ejected && item.canEject
            && QueueRules.cardInDrive(item.job.config.source, copied: item.copiedIdentity, scanned: volumes)
    }

    /// Ejects only the card that was copied: the card at the source now is identified off the main thread and
    /// compared with the one recorded when the copy started; never while another job still needs the card.
    func eject(_ item: JobItem) {
        guard !item.ejecting, !item.ejected else { return }   // a double click must not start a second eject
        let source = item.job.config.source
        let copied = item.copiedIdentity
        let inUse = QueueRules.cardInUse(source, others: jobs.filter { $0 !== item }.map { ($0.job.config.source, $0.status.phase) })
        item.ejecting = true
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let result: (ok: Bool, error: String?)
            if let problem = QueueRules.ejectProblem(copied: copied, current: CardIdentity.of(source), cardInUse: inUse) {
                result = (false, problem)
            } else {
                result = CardEject.eject(source)
            }
            DispatchQueue.main.async {
                item.ejecting = false
                if result.ok {
                    item.ejected = true
                    self?.refreshVolumes()
                } else {
                    self?.message = "Kartu se nepodařilo vysunout: \(result.error ?? "")"
                }
                self?.checkTerminateReady()
            }
        }
    }

    func reveal(_ item: JobItem) {
        guard let p = item.plan?.cardPaths.first else { return }
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: p)])
    }

    /// The report from the first destination that has it, looked up off the main thread.
    func openReport(_ item: JobItem) {
        guard let plan = item.plan else { return }
        let cards = plan.cardPaths
        let name = plan.cardName
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let path = QueueRules.firstReport(cardPaths: cards, cardName: name)
            DispatchQueue.main.async {
                if let path {
                    NSWorkspace.shared.open(URL(fileURLWithPath: path))
                } else {
                    self?.message = "Report nenalezen na žádném cíli:\n"
                        + cards.map { QueueRules.reportPath(cardPath: $0, cardName: name) }.joined(separator: "\n")
                }
            }
        }
    }

    // MARK: Quit while copying

    /// A job that has not finished, or an eject in flight.
    var hasBusyJobs: Bool { jobs.contains { !$0.isFinished || $0.ejecting } }

    /// Asked by the app delegate. With unfinished jobs: confirm, cancel them all and quit once they have cleaned up.
    func shouldTerminate() -> NSApplication.TerminateReply {
        for j in jobs { j.refresh() }
        guard hasBusyJobs else { return .terminateNow }
        if terminating {
            // An NSAlert, not `message`: the main window (and its alert) may already be closed.
            let notice = NSAlert()
            notice.messageText = "Ukončuji — čekám, až se zrušené úlohy uklidí."
            notice.runModal()
            return .terminateCancel
        }
        if jobs.contains(where: { !$0.isFinished }) {
            let alert = NSAlert()
            alert.messageText = "Probíhá offload. Opravdu ukončit?"
            alert.informativeText = "Rozpracované soubory se smažou, hotové zůstanou."
            alert.addButton(withTitle: "Ukončit")
            alert.addButton(withTitle: "Pokračovat")
            guard alert.runModal() == .alertFirstButtonReturn else { return .terminateCancel }
        }
        terminating = true
        for j in jobs {
            j.job.cancel()
            j.refresh()
        }
        if !hasBusyJobs { return .terminateNow }
        return .terminateCancel   // checkTerminateReady() quits once everything has cleaned up
    }

    private func checkTerminateReady() {
        guard terminating, !hasBusyJobs else { return }
        NSApp.terminate(nil)
    }
}
```

- [ ] **Step 3: `mac/Sources/OffloadApp/OffloadApp.swift`** — celý nový obsah:

```swift
import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    weak var model: AppModel?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    /// Quitting while a copy runs asks first, cancels the jobs and quits once they have cleaned up.
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        MainActor.assumeIsolated { model?.shouldTerminate() ?? .terminateNow }
    }

    /// "Pokračovat" after closing the window: a click on the Dock icon brings the window back.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { true }
}

@main
struct OffloadApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var model = AppModel()

    var body: some Scene {
        WindowGroup("Offload") {
            ContentView()
                .environmentObject(model)
                .onAppear { delegate.model = model }
        }
        .defaultSize(width: 1240, height: 860)
        .windowToolbarStyle(.unifiedCompact)
        .commands {
            CommandGroup(replacing: .newItem) {}
        }
    }
}
```

- [ ] **Step 4: `Views.swift`** — nahradit tyto struktury celé:

`SourceRow` — zástupný text pole „Název složky“ dnes volá `Layout.defaultCardName(for:)` (čte svazek) při každém
překreslení na hlavním vlákně; nahradit:

```swift
                        TextField(model.defaultName(path), text: Binding(
                            get: { model.cardNames[path] ?? "" },
                            set: { model.cardNames[path] = $0 }))
```

`DestinationsSection`:

```swift
struct DestinationsSection: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionHeader(title: "Cíle", systemImage: "externaldrive.connected.to.line.below",
                          trailing: AnyView(HStack(spacing: 4) {
                              Button { model.refreshDestinations() } label: { Image(systemName: "arrow.clockwise") }
                                  .help("Znovu zjistit volné místo a připojení cílů")
                              Button("Přidat…") { model.addDestination() }
                          }.buttonStyle(.borderless)))
            if model.settings.destRoots.isEmpty {
                Text("Přidejte dva disky — každá karta se zapíše na oba zároveň.")
                    .font(.callout).foregroundStyle(.secondary)
            }
            ForEach(Array(model.settings.destRoots.enumerated()), id: \.element) { i, root in
                let info = model.destinationInfo(root)
                HStack(spacing: 10) {
                    Text("\(i + 1)")
                        .font(.caption.bold()).foregroundStyle(.white)
                        .frame(width: 20, height: 20)
                        .background(Circle().fill(info.exists || info.checking ? Color.accentColor : .red))
                    VStack(alignment: .leading, spacing: 2) {
                        Text((root as NSString).lastPathComponent).fontWeight(.medium)
                        if info.checking {
                            Text("Zjišťuji…").font(.caption).foregroundStyle(.secondary)
                        } else if info.exists {
                            Text("\(formatBytes(info.free)) volno · \(info.format)").font(.caption).foregroundStyle(.secondary)
                        } else {
                            Text("Disk není připojen").font(.caption).foregroundStyle(.red)
                        }
                        if info.format.lowercased() == "exfat" || info.format.lowercased() == "msdos" {
                            Label("Doporučeno APFS — spolehlivější a ověřování obchází cache", systemImage: "exclamationmark.triangle")
                                .font(.caption).foregroundStyle(.orange)
                        }
                    }
                    Spacer()
                    Button { model.removeDestination(root) } label: { Image(systemName: "minus.circle") }
                        .buttonStyle(.borderless).help("Odebrat cíl")
                }
                .padding(10)
                .background(RoundedRectangle(cornerRadius: 8).fill(Color.secondary.opacity(0.05)))
            }
            if model.settings.destRoots.count == 1 {
                Label("Jen jeden cíl — doporučeny jsou dvě nezávislé kopie.", systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(.orange)
            }
        }
    }
}
```

`StructureSection` — za `Toggle("Analýza klipů a náhledy v reportu", …)` přidat:

```swift
            if let w = ProxyPreflight.warning(model.settings.proxy, analyzeClips: model.settings.analyzeClips, resolved: model.tools) {
                Label(w, systemImage: "exclamationmark.triangle").font(.caption).foregroundStyle(.orange)
            }
```

`ProxySection` — v `GridRow` „LUT“ pod `HStack` s tlačítky (uvnitř stejné buňky obalit do `VStack(alignment: .leading)`):

```swift
                    GridRow {
                        Text("LUT").foregroundStyle(.secondary)
                        VStack(alignment: .leading, spacing: 2) {
                            HStack {
                                Text(model.settings.proxy.lutPath.map { ($0 as NSString).lastPathComponent } ?? "bez LUT")
                                    .lineLimit(1).truncationMode(.middle)
                                    .foregroundStyle(model.settings.proxy.lutPath == nil ? .secondary : .primary)
                                Spacer()
                                Button("Vybrat…") { model.pickLUT() }
                                if model.settings.proxy.lutPath != nil {
                                    Button { model.settings.proxy.lutPath = nil } label: { Image(systemName: "xmark.circle.fill") }
                                        .buttonStyle(.borderless)
                                }
                            }
                            if let p = model.lutProblem { Text(p).font(.caption).foregroundStyle(.red) }
                        }
                    }
```

`StartBar`:

```swift
struct StartBar: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(Texts.cards(model.selectedSources.count)).fontWeight(.medium)
                Text(Texts.destinations(model.settings.destRoots.count) + (model.settings.proxy.enabled ? " · proxy" : ""))
                    .font(.caption).foregroundStyle(.secondary)
                // The LUT is also used by thumbnails when proxies are off: say so here, where the start button is.
                if let p = model.lutProblem, !model.settings.proxy.enabled {
                    Text(p).font(.caption).foregroundStyle(.red)
                }
            }
            Spacer()
            Button {
                model.start()
            } label: {
                Label("Spustit offload", systemImage: "play.fill").frame(minWidth: 150)
            }
            .controlSize(.large)
            .buttonStyle(.borderedProminent)
            .keyboardShortcut(.return, modifiers: .command)
            .disabled(!model.canStart)
        }
        .padding(14)
    }
}
```

`JobCard` (celý):

```swift
struct JobCard: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject var item: JobItem

    var phaseColor: Color {
        switch item.tone {
        case .ok: return .green
        case .error: return .red
        case .warning: return .orange
        case .waiting: return .secondary
        case .active: return .accentColor
        }
    }

    var body: some View {
        let s = item.status
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Image(systemName: "sdcard.fill").foregroundStyle(phaseColor)
                Text(item.plan?.cardName ?? item.sourceName).font(.headline)
                if let p = item.plan {
                    Text(Texts.files(p.scan.files.count, p.scan.totalBytes)).font(.callout).foregroundStyle(.secondary)
                }
                Spacer()
                Text(item.statusText).font(.caption.weight(.semibold))
                    .padding(.horizontal, 8).padding(.vertical, 3)
                    .background(Capsule().fill(phaseColor.opacity(0.15)))
                    .foregroundStyle(phaseColor)
            }
            if let p = item.plan {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(Array(p.destRoots.enumerated()), id: \.offset) { i, root in
                        HStack(spacing: 4) {
                            Image(systemName: "externaldrive.fill").font(.caption2)
                            Text((root as NSString).lastPathComponent).fontWeight(.medium)
                            Text("› \(p.date)/\(p.cardName)").font(.system(.caption, design: .monospaced))
                        }
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .help(p.cardPaths[i])
                    }
                }
            }
            if item.isRunning {
                ProgressView(value: item.phaseProgress)
                Text(item.detailText).font(.callout.monospacedDigit()).foregroundStyle(.secondary).lineLimit(1)
            }
            if s.safeToEject && !item.ejected {
                HStack {
                    Label(item.safeText, systemImage: "checkmark.shield.fill").foregroundStyle(.green).font(.callout)
                    Spacer()
                    if model.canShowEject(item) {
                        Button("Vysunout kartu") { model.eject(item) }.disabled(item.ejecting)
                    }
                }
            } else if item.ejected {
                Label("Karta vysunuta", systemImage: "eject.fill").foregroundStyle(.secondary).font(.callout)
            }
            if let t = item.cancelledText {
                Label(t, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.orange).font(.callout)
            }
            if let t = item.failedText {
                Label(t, systemImage: "xmark.octagon.fill").foregroundStyle(.red).font(.callout)
            }
            if let e = s.error {
                Label(e, systemImage: "xmark.octagon.fill").foregroundStyle(.red).font(.callout)
            }
            if let summary = item.summaryText {
                Text(summary).font(.caption).foregroundStyle(.secondary)
            }
            HStack {
                if item.report != nil {
                    Button { model.openReport(item) } label: { Label("Report", systemImage: "doc.richtext") }
                    Button { model.reveal(item) } label: { Label("Finder", systemImage: "folder") }
                }
                Spacer()
                if item.isRunning || s.phase == .waiting {
                    Button(role: .destructive) { model.cancel(item) } label: { Text("Zrušit") }
                }
            }
            .buttonStyle(.bordered)
            .controlSize(.small)
        }
        .padding(16)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color(nsColor: .windowBackgroundColor)))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.secondary.opacity(0.15)))
    }
}
```

V `QueuePanel` se `model.jobs.contains(where: { $0.status.phase.isFinished })` nahradí `model.jobs.contains(where: \.isFinished)`.

- [ ] **Step 5: Build** — Run: `swift build && swift test && scripts/build-app.sh` → bez chyb. Pokud kompilátor hlásí izolaci (`call to main actor-isolated … in a synchronous nonisolated context`) v uzávěrech `copyQueue.async`/`probeQueue.async`, obalte volání v nich do `DispatchQueue.main.async { … }` (Swift 5.9 je bere jako `@MainActor`) — nikdy nevolejte metody `AppModel` přímo z pozadí.

- [ ] **Step 6: Ruční test (bez karty)** — `open build/Offload.app`:
  1. „Složka…“ → vybrat složku s pár soubory; cíl na USB disku. Lišta dole: „1 karta“, „1 cíl“. Spustit → karta úlohy „N souborů · velikost“; po ověření zelený pruh „Všechny soubory jsou na 1 disku a ověřené — kartu lze vysunout.“ **bez** tlačítka „Vysunout kartu“ (zdroj je složka).
  2. Spustit dvě úlohy za sebou, druhou hned „Zrušit“ → ukáže „Zrušeno“ (ne „Čeká“), oranžové „Zrušeno — karta není kompletně zálohovaná.“, žádný zvuk Basso; „Vyčistit hotové“ ji odebere.
  3. Během kopie ⌘Q → dotaz „Probíhá offload. Opravdu ukončit?“; „Pokračovat“ → kopie běží dál; znovu ⌘Q → „Ukončit“ → aplikace se po chvíli sama ukončí; v cíli `find <cíl> -name '.*offload-part'` nic nenajde.
  4. Odpojit USB cíl → do 5 s „Disk není připojen“, „Spustit offload“ neaktivní; okno nezamrzne.
  5. Nastavit LUT, aplikaci zavřít, LUT soubor přejmenovat, znovu otevřít → u LUT červeně „LUT nenalezen: … — vyberte jiný nebo ho odeberte.“, start neaktivní.

- [ ] **Step 7: Ruční test s kartou** (položky 1–4 z README-START): vložit kartu → objeví se zaškrtnutá; po zeleném pruhu „Vysunout kartu“ → karta zmizí z „Karty“ (čtečka zůstane připojená); vložit jinou kartu do téže čtečky → u předchozí úlohy tlačítko není. Výsledek zapsat do commitu.

- [ ] **Step 8: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 11 — GUI: karta úlohy s českým množným číslem a oranžovým „Zrušeno — …“, zrušená úloha není chyba, vysunutí jen kopírované karty mimo hlavní vlákno, cíle a LUT se zjišťují na pozadí, ukončení během kopie se ptá a počká na úklid`

`docs/ROZDILY.md` — v „Záměrné rozdíly“ smazat odrážku „Zrušená úloha v GUI“; odrážku „Vysunutí karty“ přepsat, aby platila pro obě verze (Mac: identita = UUID svazku); řádek tabulky „GUI: detekce karet, fronta, vysunutí“ beze změny.

Run: `git add -A mac/Sources docs && git commit -m "Mac: GUI job card texts, cancelled card, eject only the copied card, no disk access on the main thread, quit while copying"`

---

### Task 12: HW model — rychlosti linek, USB zásuvky, popisky portů

**Files:**
- Create: `mac/Sources/OffloadKit/Hardware/UsbSpeeds.swift`, `mac/Sources/OffloadKit/Hardware/UsbTopology.swift`, `mac/Sources/OffloadKit/Hardware/PortLabels.swift`
- Create: `mac/Tests/OffloadKitTests/UsbSpeedsTests.swift`, `mac/Tests/OffloadKitTests/UsbFixtures.swift`, `mac/Tests/OffloadKitTests/UsbTopologyTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Produces:
  - `public enum UsbLinkSpeed: Int, Codable, Comparable { unknown, low, full, high, superSpeed, superPlus }`
  - `public enum UsbSpeeds { link(linkBitsPerSecond: UInt64?, deviceSpeed: Int?) -> UsbLinkSpeed; capability(bcdUSB: Int?, link:) -> UsbLinkSpeed; label(_:) -> String; practicalMBps(_:) -> Double }`
  - `public struct UsbDevice`, `UsbPort`, `UsbHub`, `UsbController`, `UsbConnector` (`has(_:)`, `kind`, `defaultLabel`, `altIds`), `UsbTopology` (`connectors`) — všechny `Codable, Equatable`
  - `public enum Connectors { static func build(_ hub: UsbHub, controllerId: String, controllerName: String, prefix: String = "") -> [UsbConnector] }`
  - `public enum PortLabels { name(_:_:); withKind(_:_:); find(_:_:); shortController(_:); padRight(_:_:) }` (úkol 13 přidá `of` a `listRow`)

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/UsbSpeedsTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class UsbSpeedsTests: XCTestCase {
    func testLinkFromUsbLinkSpeedElseDeviceSpeed() {
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 5_000_000_000, deviceSpeed: 2), .superSpeed)   // UsbLinkSpeed wins
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 10_000_000_000, deviceSpeed: nil), .superPlus)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 20_000_000_000, deviceSpeed: nil), .superPlus)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 480_000_000, deviceSpeed: nil), .high)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 12_000_000, deviceSpeed: nil), .full)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 1_500_000, deviceSpeed: nil), .low)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 0, deviceSpeed: 3), .superSpeed)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 0), .low)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 1), .full)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 2), .high)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 4), .superPlus)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 5), .superPlus)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 9), .unknown)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: nil), .unknown)
    }

    func testCapabilityFromBcdUsbNeverBelowTheLink() {
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: 0x0320, link: .high), .superSpeed)
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: 0x0300, link: .superPlus), .superPlus)
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: 0x0200, link: .high), .high)
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: 0x0110, link: .full), .full)
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: nil, link: .high), .high)
    }

    func testLabelsAndCeilings() {
        XCTAssertEqual(UsbSpeeds.label(.high), "USB 2.0 (480 Mb/s)")
        XCTAssertEqual(UsbSpeeds.label(.superSpeed), "USB 3 (5 Gb/s)")
        XCTAssertEqual(UsbSpeeds.label(.superPlus), "USB 3 (10 Gb/s a víc)")
        XCTAssertEqual(UsbSpeeds.label(.unknown), "neznámá")
        XCTAssertEqual(UsbSpeeds.practicalMBps(.high), 40)
        XCTAssertEqual(UsbSpeeds.practicalMBps(.superSpeed), 450)
        XCTAssertEqual(UsbSpeeds.practicalMBps(.superPlus), 1000)
        XCTAssertEqual(UsbSpeeds.practicalMBps(.unknown), 0)
        XCTAssertLessThan(UsbLinkSpeed.high, UsbLinkSpeed.superSpeed)
    }
}
```

`mac/Tests/OffloadKitTests/UsbFixtures.swift` (sdílené fixtury topologie — používají je `UsbTopologyTests`, `AdvisorTests` a `MacHardwareTests`):

```swift
import XCTest
@testable import OffloadKit

/// USB topology fixtures shared by several test classes (no test class instantiates another).
extension XCTestCase {
    func dev(_ name: String, _ link: UsbLinkSpeed, _ cap: UsbLinkSpeed, hub: UsbHub? = nil) -> UsbDevice {
        UsbDevice(instanceId: "USB-\(name)", vid: 1, pid: 2, name: name, link: link, capable: cap, hub: hub)
    }

    func p(_ n: Int, _ usb3: Bool?, visible: Bool = true, typeC: Bool = false, companion: Int = 0, _ device: UsbDevice? = nil) -> UsbPort {
        UsbPort(number: n, supportsUsb3: usb3, userVisible: visible, typeC: typeC, companionPort: companion, device: device)
    }

    /// Root hub: USB 2 ports 1…3, USB 3 ports 17…18 as companions of 1 and 2.
    func root(onPort1: UsbDevice? = nil, onPort17: UsbDevice? = nil, onPort3: UsbDevice? = nil) -> UsbHub {
        UsbHub(instanceId: "ROOT", name: "Root hub", ports: [
            p(1, false, companion: 17, onPort1), p(2, false, typeC: true, companion: 18), p(3, false, onPort3),
            p(17, true, companion: 1, onPort17), p(18, true, typeC: true, companion: 2),
        ])
    }

    func build(_ hub: UsbHub) -> [UsbConnector] { Connectors.build(hub, controllerId: "HC", controllerName: "USB řadič XHC1") }

    // Windows final review 5: an external USB 3 hub is two hub devices — merged by port number; nothing vanishes.
    func dualHalfHub() -> [UsbConnector] {
        let ss = UsbHub(instanceId: "SS", name: "USB3.0 Hub", ports: [
            p(1, true, companion: 1, dev("ssd", .superSpeed, .superSpeed)), p(2, true, companion: 2), p(3, true, companion: 3),
        ])
        let hs = UsbHub(instanceId: "HS", name: "USB2.0 Hub", ports: [
            p(1, false, companion: 1), p(2, false, companion: 2, dev("reader", .high, .superSpeed)), p(3, false, companion: 3),
        ])
        return build(root(onPort1: dev("hs-hub", .high, .high, hub: hs), onPort17: dev("ss-hub", .superSpeed, .superSpeed, hub: ss)))
    }
}
```

`mac/Tests/OffloadKitTests/UsbTopologyTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class UsbTopologyTests: XCTestCase {
    func testPairUsb2AndUsb3Ports() throws {
        let c = build(root(onPort17: dev("disk", .superSpeed, .superSpeed)))
        XCTAssertEqual(c.count, 3)
        let a = try XCTUnwrap(c.first { $0.usb2Port == 1 })
        XCTAssertEqual(a.usb3Port, 17)
        XCTAssertEqual(a.supportsUsb3, true)
        XCTAssertEqual(a.device?.name, "disk")
        XCTAssertEqual(a.id, "ROOT#1")
        XCTAssertTrue(a.has("ROOT#17"))
        XCTAssertEqual(a.controllerId, "HC")
        XCTAssertEqual(a.label, "port 1")
        XCTAssertEqual(a.defaultLabel, "port 1 USB 3")
        let typeC = try XCTUnwrap(c.first { $0.usb2Port == 2 })
        XCTAssertTrue(typeC.typeC)
        XCTAssertEqual(typeC.defaultLabel, "port 2 USB-C")
        let usb2Only = try XCTUnwrap(c.first { $0.usb2Port == 3 })
        XCTAssertEqual(usb2Only.supportsUsb3, false)
        XCTAssertEqual(usb2Only.defaultLabel, "port 3 jen USB 2.0")
    }

    func testDeviceOnUsb2HalfOfUsb3Connector() throws {
        let a = try XCTUnwrap(build(root(onPort1: dev("disk", .high, .superSpeed))).first { $0.usb2Port == 1 })
        XCTAssertEqual(a.supportsUsb3, true)
        XCTAssertEqual(a.device?.link, .high)
    }

    func testTolerateMissingCompanionAndDevice() throws {
        let c = build(UsbHub(instanceId: "H", name: "hub", ports: [p(1, false, companion: 99), p(20, true, visible: false)]))
        XCTAssertEqual(c.count, 2)
        XCTAssertEqual(c.first { $0.usb2Port == 1 }?.usb3Port, 0)
        XCTAssertEqual(c.first { $0.usb3Port == 20 }?.defaultLabel, "port 20 USB 3 (interní)")
    }

    func testRecurseIntoExternalHubs() {
        let ext = UsbHub(instanceId: "EXT", name: "USB2.0 Hub", ports: [p(1, false, dev("reader", .high, .high))])
        let c = build(root(onPort3: dev("hub", .high, .high, hub: ext)))
        XCTAssertTrue(c.contains { $0.hubInstanceId == "EXT" && $0.device?.name == "reader" })
    }

    // Windows final review 1: hub ports are named by their path, never "port 1" again.
    func testHubPortsDoNotCollideWithRootPorts() {
        let ext = UsbHub(instanceId: "EXT", name: "USB2.0 Hub", ports: [
            p(1, false, dev("headset", .full, .full)), p(2, false), p(3, false),
        ])
        let c = build(root(onPort3: dev("hub", .high, .high, hub: ext)))
        XCTAssertEqual(Set(c.map(\.label)).count, c.count)
        XCTAssertEqual(c.first { $0.id == "EXT#1" }?.label, "port 3 › 1")
        XCTAssertEqual(c.first { $0.id == "EXT#2" }?.defaultLabel, "port 3 › 2 jen USB 2.0")
        XCTAssertEqual(c.first { $0.id == "ROOT#1" }?.label, "port 1")
    }

    // Windows final review 1 + 4: the controller goes into the name only with more than one; same names get numbers.
    func testControllerPrefixOnlyWithSeveralControllers() {
        func r(_ id: String) -> UsbHub { UsbHub(instanceId: id, name: "Root", ports: [p(1, false)]) }
        let one = UsbTopology(controllers: [UsbController(instanceId: "A", name: "USB řadič XHC1", rootHubs: [r("RA")])])
        XCTAssertEqual(one.connectors.map(\.label), ["port 1"])
        let two = UsbTopology(controllers: [UsbController(instanceId: "A", name: "USB řadič usb-drd0", rootHubs: [r("RA")]),
                                            UsbController(instanceId: "B", name: "USB řadič usb-drd1", rootHubs: [r("RB")])])
        XCTAssertEqual(two.connectors.map(\.label), ["USB řadič usb-drd0 · port 1", "USB řadič usb-drd1 · port 1"])
        XCTAssertEqual(two.connectors.map(\.controllerId), ["A", "B"])
        let amd = "AMD USB 3.10 eXtensible Host Controller - 1.10 (Microsoft)"
        let same = UsbTopology(controllers: [UsbController(instanceId: "A", name: amd, rootHubs: [r("RA")]),
                                             UsbController(instanceId: "B", name: amd, rootHubs: [r("RB")])])
        XCTAssertEqual(same.connectors.map(\.label), ["AMD USB 3.10 xHCI (1) · port 1", "AMD USB 3.10 xHCI (2) · port 1"])
    }

    // Windows final review 8: unknown kind → no speed claim; mutual companions still make a USB 3 socket.
    func testUnknownPortKind() throws {
        let c = build(UsbHub(instanceId: "ROOT", name: "Root", ports: [p(1, nil, companion: 5), p(2, nil), p(5, nil, companion: 1)]))
        XCTAssertEqual(c.count, 2)
        let paired = try XCTUnwrap(c.first { $0.number == 1 })
        XCTAssertEqual(paired.supportsUsb3, true)
        XCTAssertEqual(paired.usb3Port, 5)
        let unknown = try XCTUnwrap(c.first { $0.number == 2 })
        XCTAssertNil(unknown.supportsUsb3)
        XCTAssertEqual(unknown.defaultLabel, "port 2")
    }

    func testDualHalfHubMergedByPortNumber() throws {
        let hubPorts = dualHalfHub().filter { $0.label.hasPrefix("port 1 › ") }
        XCTAssertEqual(hubPorts.count, 3)
        XCTAssertTrue(hubPorts.allSatisfy { $0.supportsUsb3 == true })
        XCTAssertEqual(hubPorts.first { $0.number == 1 }?.device?.name, "ssd")
        let reader = try XCTUnwrap(hubPorts.first { $0.number == 2 })
        XCTAssertEqual(reader.device?.name, "reader")
        XCTAssertTrue(reader.has("HS#2") && reader.has("SS#2"))
        XCTAssertNil(hubPorts.first { $0.number == 3 }?.device)
        XCTAssertEqual(hubPorts.first { $0.number == 3 }?.defaultLabel, "port 1 › 3 USB 3")
    }

    func testPortLabelsUseTheUsersNameUnderAnyId() throws {
        let c = try XCTUnwrap(build(root()).first { $0.usb2Port == 1 })
        XCTAssertEqual(PortLabels.name(c, ["ROOT#17": "přední levý"]), "přední levý")
        XCTAssertEqual(PortLabels.withKind(c, ["ROOT#1": " zadní "]), "zadní USB 3")
        XCTAssertEqual(PortLabels.withKind(c, ["ROOT#1": "  "]), "port 1 USB 3")
        XCTAssertEqual(PortLabels.find([c], "ROOT#17")?.id, "ROOT#1")
        XCTAssertEqual(PortLabels.shortController("Intel(R) USB 3.20 eXtensible Host Controller - 1.20 (Microsoft)"), "Intel USB 3.20 xHCI")
        XCTAssertEqual(PortLabels.shortController("USB řadič XHC1"), "USB řadič XHC1")
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "UsbSpeedsTests|UsbTopologyTests"` → nesestaví se (`cannot find 'UsbSpeeds'`, `'Connectors'`…).

- [ ] **Step 3: `mac/Sources/OffloadKit/Hardware/UsbSpeeds.swift`**:

```swift
import Foundation

public enum UsbLinkSpeed: Int, Codable, Comparable {
    case unknown, low, full, high, superSpeed, superPlus

    public static func < (a: UsbLinkSpeed, b: UsbLinkSpeed) -> Bool { a.rawValue < b.rawValue }
}

public enum UsbSpeeds {
    /// The speed the link really runs at, from an IOUSBHostDevice: "UsbLinkSpeed" (bit/s) when the system reports it,
    /// else "Device Speed" (kUSBDevicePropertySpeed: 0 low, 1 full, 2 high, 3 super, 4 SuperSpeedPlus, 5 SuperSpeedPlus by 2).
    public static func link(linkBitsPerSecond: UInt64?, deviceSpeed: Int?) -> UsbLinkSpeed {
        if let bps = linkBitsPerSecond, bps > 0 {
            if bps >= 10_000_000_000 { return .superPlus }
            if bps >= 5_000_000_000 { return .superSpeed }
            if bps >= 480_000_000 { return .high }
            if bps >= 12_000_000 { return .full }
            return .low
        }
        switch deviceSpeed {
        case 0?: return .low
        case 1?: return .full
        case 2?: return .high
        case 3?: return .superSpeed
        case 4?, 5?: return .superPlus
        default: return .unknown
        }
    }

    /// What the device could do on a good port and cable — never less than what it runs at. macOS does not expose the
    /// SuperSpeedPlus capability without reading the BOS descriptor, so bcdUSB ≥ 3.0 means "USB 3 (5 Gb/s)".
    public static func capability(bcdUSB: Int?, link: UsbLinkSpeed) -> UsbLinkSpeed {
        guard let bcd = bcdUSB else { return link }
        let cap: UsbLinkSpeed = bcd >= 0x0300 ? .superSpeed : bcd >= 0x0200 ? .high : .full
        return max(cap, link)
    }

    public static func label(_ s: UsbLinkSpeed) -> String {
        switch s {
        case .low: return "USB 1.0 (1,5 Mb/s)"
        case .full: return "USB 1.1 (12 Mb/s)"
        case .high: return "USB 2.0 (480 Mb/s)"
        case .superSpeed: return "USB 3 (5 Gb/s)"
        case .superPlus: return "USB 3 (10 Gb/s a víc)"
        case .unknown: return "neznámá"
        }
    }

    /// Realistic ceiling for a disk or card reader over this link, MB/s (protocol overhead included).
    public static func practicalMBps(_ s: UsbLinkSpeed) -> Double {
        switch s {
        case .low: return 0.15
        case .full: return 1
        case .high: return 40
        case .superSpeed: return 450
        case .superPlus: return 1000
        case .unknown: return 0
        }
    }
}
```

- [ ] **Step 4: `mac/Sources/OffloadKit/Hardware/UsbTopology.swift`**:

```swift
import Foundation

public struct UsbDevice: Codable, Equatable {
    public let instanceId: String
    public let vid: Int
    public let pid: Int
    public let name: String
    public let link: UsbLinkSpeed
    public let capable: UsbLinkSpeed
    public let hub: UsbHub?
}

/// supportsUsb3: nil when the registry could not say — never "USB 2.0 only" without proof.
public struct UsbPort: Codable, Equatable {
    public let number: Int
    public let supportsUsb3: Bool?
    public let userVisible: Bool
    public let typeC: Bool
    public let companionPort: Int
    public let device: UsbDevice?
}

public struct UsbHub: Codable, Equatable {
    public let instanceId: String
    public let name: String
    public let ports: [UsbPort]
}

public struct UsbController: Codable, Equatable {
    public let instanceId: String
    public let name: String
    public let rootHubs: [UsbHub]
}

/// One physical socket: a root hub lists it twice — a USB 2 port and its USB 3 twin; an external USB 3 hub is two hub
/// devices (USB 3 half + USB 2 half) with the same port numbers. `label` is the socket's unique default name
/// ("port 6", "port 7 › 2", "<řadič> · port 3"). Port of Windows `UsbTopology.cs`.
public struct UsbConnector: Codable, Equatable {
    public let id: String
    public let hubInstanceId: String
    public let controllerId: String
    public let controllerName: String
    public let number: Int
    public let usb2Port: Int
    public let usb3Port: Int
    public let supportsUsb3: Bool?
    public let typeC: Bool
    public let userVisible: Bool
    public let device: UsbDevice?
    public let label: String
    /// Other ids under which a drive may report this socket (the port on the other half).
    public var altIds: [String] = []

    public func has(_ connectorId: String) -> Bool { id == connectorId || altIds.contains(connectorId) }

    /// "USB 3", "USB-C", "jen USB 2.0", "USB-C jen USB 2.0", "" (unknown) + " (interní)".
    public var kind: String {
        let speed: String
        switch supportsUsb3 {
        case true?: speed = typeC ? "USB-C" : "USB 3"
        case false?: speed = typeC ? "USB-C jen USB 2.0" : "jen USB 2.0"
        case nil: speed = typeC ? "USB-C" : ""
        }
        return userVisible ? speed : (speed + " (interní)").trimmingCharacters(in: .whitespaces)
    }

    public var defaultLabel: String { kind.isEmpty ? label : "\(label) \(kind)" }
}

public struct UsbTopology: Codable, Equatable {
    public let controllers: [UsbController]

    public var connectors: [UsbConnector] {
        let short = controllers.map { PortLabels.shortController($0.name) }
        return controllers.enumerated().flatMap { i, c -> [UsbConnector] in
            // Only with more than one controller does a port need the controller in its name; same names get a number.
            let prefix: String
            if controllers.count <= 1 {
                prefix = ""
            } else if short.filter({ $0 == short[i] }).count > 1 {
                prefix = "\(short[i]) (\(short[...i].filter { $0 == short[i] }.count)) · "
            } else {
                prefix = "\(short[i]) · "
            }
            return c.rootHubs.flatMap { Connectors.build($0, controllerId: c.instanceId, controllerName: c.name, prefix: prefix) }
        }
    }
}

public enum Connectors {
    struct Slot {
        var usb2: UsbPort? = nil
        var usb2Hub: String? = nil
        var usb3: UsbPort? = nil
        var usb3Hub: String? = nil
        var unknown: UsbPort? = nil
        var unknownHub: String? = nil

        var ports: [UsbPort] { [usb2, usb3, unknown].compactMap { $0 } }
        var number: Int { usb2?.number ?? usb3?.number ?? unknown!.number }
    }

    /// The sockets of a root hub (pairs its USB 2 / USB 3 companions) and of every hub below it.
    public static func build(_ hub: UsbHub, controllerId: String, controllerName: String, prefix: String = "") -> [UsbConnector] {
        build(hub, twin: nil, controllerId: controllerId, controllerName: controllerName, labelStart: prefix + "port ")
    }

    // hub: one hub, or (when twin is set) the USB 2 half of an external USB 3 hub whose USB 3 half is `twin`.
    static func build(_ hub: UsbHub, twin: UsbHub?, controllerId: String, controllerName: String, labelStart: String) -> [UsbConnector] {
        var result: [UsbConnector] = []
        for slot in twin.map({ pairAcrossHalves(hub, $0) }) ?? pairWithinHub(hub) {
            let number = slot.number
            let mainHub = slot.usb2Hub ?? slot.usb3Hub ?? slot.unknownHub!
            var ids: [String] = []
            for (h, p) in [(slot.usb2Hub, slot.usb2), (slot.usb3Hub, slot.usb3), (slot.unknownHub, slot.unknown)] {
                if let h, let p, !ids.contains("\(h)#\(p.number)") { ids.append("\(h)#\(p.number)") }
            }
            let id = "\(mainHub)#\(number)"
            let usb3: Bool? = slot.usb3 != nil ? true : (slot.unknown != nil ? slot.unknown!.supportsUsb3 : false)
            let usb3Device = slot.usb3?.device
            let usb2Device = slot.usb2?.device ?? slot.unknown?.device
            let label = labelStart + String(number)
            var c = UsbConnector(id: id, hubInstanceId: mainHub, controllerId: controllerId, controllerName: controllerName,
                                 number: number, usb2Port: slot.usb2?.number ?? 0, usb3Port: slot.usb3?.number ?? 0,
                                 supportsUsb3: usb3, typeC: slot.ports.contains { $0.typeC },
                                 userVisible: slot.ports.contains { $0.userVisible }, device: usb3Device ?? usb2Device, label: label)
            c.altIds = ids.filter { $0 != id }
            result.append(c)
            // A USB 3 hub shows up on both halves of the socket: walk both, so USB 2 devices behind it don't vanish.
            let childStart = label + " › "
            if let h2 = usb2Device?.hub, let h3 = usb3Device?.hub {
                result += build(h2, twin: h3, controllerId: controllerId, controllerName: controllerName, labelStart: childStart)
            } else if let child = usb3Device?.hub ?? usb2Device?.hub {
                result += build(child, twin: nil, controllerId: controllerId, controllerName: controllerName, labelStart: childStart)
            }
        }
        return result
    }

    // Root hub: USB 2 port n and its USB 3 companion m are one socket. Ports of unknown kind pair only when both
    // name each other as companions (then the socket must be USB 3: only USB 3 sockets have companions).
    static func pairWithinHub(_ hub: UsbHub) -> [Slot] {
        var slots: [Slot] = []
        var done = Set<Int>()
        let ordered = hub.ports.sorted { ($0.supportsUsb3 == true ? 1 : 0, $0.number) < ($1.supportsUsb3 == true ? 1 : 0, $1.number) }
        for port in ordered {
            guard done.insert(port.number).inserted else { continue }
            var twin: UsbPort?
            if port.companionPort > 0 && port.companionPort != port.number {
                twin = hub.ports.first { $0.number == port.companionPort && !done.contains($0.number) && isTwin(port, $0) }
            }
            if let t = twin { done.insert(t.number) }
            if twin == nil && port.supportsUsb3 == nil {
                slots.append(Slot(unknown: port, unknownHub: hub.instanceId))
                continue
            }
            let usb2: UsbPort?, usb3: UsbPort?
            if let t = twin {
                (usb2, usb3) = order(port, t)
            } else if port.supportsUsb3 == true {
                (usb2, usb3) = (nil, port)
            } else {
                (usb2, usb3) = (port, nil)
            }
            slots.append(Slot(usb2: usb2, usb2Hub: usb2 == nil ? nil : hub.instanceId,
                              usb3: usb3, usb3Hub: usb3 == nil ? nil : hub.instanceId))
        }
        return slots.sorted { $0.number < $1.number }
    }

    static func isTwin(_ a: UsbPort, _ b: UsbPort) -> Bool {
        if let x = a.supportsUsb3, let y = b.supportsUsb3 { return x != y }
        return b.companionPort == a.number
    }

    static func order(_ a: UsbPort, _ b: UsbPort) -> (UsbPort?, UsbPort?) {
        if a.supportsUsb3 == true || b.supportsUsb3 == false { return (b, a) }
        if b.supportsUsb3 == true || a.supportsUsb3 == false { return (a, b) }
        return a.number < b.number ? (a, b) : (b, a)
    }

    // External USB 3 hub: port n of the USB 2 half and port n of the USB 3 half are the same socket.
    static func pairAcrossHalves(_ usb2Half: UsbHub, _ usb3Half: UsbHub) -> [Slot] {
        let numbers = Set(usb2Half.ports.map(\.number)).union(usb3Half.ports.map(\.number)).sorted()
        return numbers.map { n in
            let p2 = usb2Half.ports.first { $0.number == n }
            let p3 = usb3Half.ports.first { $0.number == n }
            return Slot(usb2: p2, usb2Hub: p2 == nil ? nil : usb2Half.instanceId, usb3: p3, usb3Hub: p3 == nil ? nil : usb3Half.instanceId)
        }
    }
}
```

- [ ] **Step 5: `mac/Sources/OffloadKit/Hardware/PortLabels.swift`**:

```swift
import Foundation

/// The one place that names a USB socket — drive lines, the port list, advice and the copy chain all go through here,
/// so one socket always reads the same. User names (`UsbConnector.id` → název) replace the default label.
public enum PortLabels {
    /// The socket's name: the user's name (under its id or one of its alt ids), else the default unique label.
    public static func name(_ c: UsbConnector, _ names: [String: String]? = nil) -> String {
        if let names {
            for id in [c.id] + c.altIds {
                if let n = names[id]?.trimmingCharacters(in: .whitespaces), !n.isEmpty { return n }
            }
        }
        return c.label
    }

    /// Name plus what the socket can do: "port 6 USB 3", "port 3 jen USB 2.0", "port 4" (unknown).
    public static func withKind(_ c: UsbConnector, _ names: [String: String]? = nil) -> String {
        c.kind.isEmpty ? name(c, names) : "\(name(c, names)) \(c.kind)"
    }

    public static func find(_ connectors: [UsbConnector]?, _ connectorId: String) -> UsbConnector? {
        connectors?.first { $0.has(connectorId) }
    }

    /// "Intel(R) USB 3.20 eXtensible Host Controller - 1.20 (Microsoft)" → "Intel USB 3.20 xHCI"; Mac names pass unchanged.
    public static func shortController(_ name: String) -> String {
        var s = name.replacingOccurrences(of: #"\((R|TM|C)\)|®|™"#, with: "", options: [.regularExpression, .caseInsensitive])
        s = s.replacingOccurrences(of: #"\s+-\s+[\d.]+(\s*\([^)]*\))?\s*$|\s*\((Microsoft|AMD|Intel)\)\s*$"#, with: "",
                                   options: .regularExpression)
        for (long, short) in [("eXtensible Host Controller", "xHCI"), ("Enhanced Host Controller", "EHCI")] {
            s = s.replacingOccurrences(of: long, with: short, options: .caseInsensitive)
        }
        s = s.replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression).trimmingCharacters(in: .whitespaces)
        return s.isEmpty ? name : s
    }

    static func padRight(_ s: String, _ width: Int) -> String {
        s.count >= width ? s : s + String(repeating: " ", count: width - s.count)
    }
}
```

- [ ] **Step 6: Testy projdou** — Run: `swift test --filter "UsbSpeedsTests|UsbTopologyTests"` → 3 + 9 testů, 0 chyb.

- [ ] **Step 7: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 12 — HW model: rychlosti linek a popisky, USB zásuvky (dvojčata USB 2/3, poloviny USB 3 hubu, neznámý typ ≠ „jen USB 2.0“), jeden popisek portu všude`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: USB link speeds, socket topology and port labels (pure model)"`

---

### Task 13: HW model — připojení disků, doporučení, řetěz

**Files:**
- Create: `mac/Sources/OffloadKit/Hardware/DriveLinks.swift`, `mac/Sources/OffloadKit/Hardware/Advisor.swift`, `mac/Sources/OffloadKit/Hardware/ChainView.swift`
- Create: `mac/Tests/OffloadKitTests/AdvisorTests.swift`, `mac/Tests/OffloadKitTests/ChainViewTests.swift`, `mac/Tests/OffloadKitTests/DriveLinksTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: úkol 12, `Brake` (úkol 8).
- Produces:
  - `public enum StorageBus: String, Codable { unknown, usb, sata, nvme, sd, mmc, scsi, sas, raid, virtual, network, thunderbolt, other }`
  - `public enum StorageBuses { static func label(_:) -> String }`
  - `public struct UsbHubRef`, `UsbLink` (`controllerId, controllerName, connectorId, connectorPort, port, link, capable, connectorUsb3: Bool?, uasp, hubs`), `DriveLink` (`root, bus, product, usb`; `connection(_:_:)`, `describe(_:_:)`) — `Codable, Equatable`
  - `public enum DriveLinks { static func network(_ root: String, _ share: String?) -> DriveLink }` (`network` internal — volá ho jen `resolve` a testy přes `@testable`; úkol 14 přidá `public static func resolve`)
  - `extension PortLabels { of(_:_:_:); listRow(_:drives:names:labelWidth:); volumeLabel(_:) }`
  - `public enum AdviceKind: Int, Codable, Comparable { warning, info, tip }`, `public struct Advice`, `public struct RoleLink { role; link; isCard; public init(role:link:) }`
  - `public enum Advisor { static func advise(_ drives: [RoleLink], connectors: [UsbConnector], measuredMBps: [String: Double]? = nil, portNames: [String: String]? = nil) -> [Advice] }`
  - `public struct ChainElement`, `public enum ChainView { build(_:mbps:connectors:portNames:); linkCeiling(_:); describe(_:_:); lines(_:brake:) }`
  - `func orderedGroups<T>(_ items: [T], by key: (T) -> String) -> [[T]]` (internal)

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/AdvisorTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class AdvisorTests: XCTestCase {
    let hc = "Intel(R) USB 3.20 eXtensible Host Controller"
    let hcId = "HC-INTEL"

    func usb(_ role: String, _ product: String, _ link: UsbLinkSpeed, _ cap: UsbLinkSpeed, _ usb3: Bool?, _ port: Int,
             controller: String? = nil, controllerId: String? = nil, hubs: [UsbHubRef] = []) -> RoleLink {
        RoleLink(role: role, link: DriveLink(root: "/Volumes/\(role)", bus: .usb, product: product,
                                             usb: UsbLink(controllerId: controllerId ?? hcId, controllerName: controller ?? hc,
                                                          connectorId: "ROOT#\(port)", connectorPort: port, port: port, link: link,
                                                          capable: cap, connectorUsb3: usb3, uasp: false, hubs: hubs)))
    }

    func port(_ usb2: Int, _ usb3: Int, typeC: Bool = false, visible: Bool = true, device: UsbDevice? = nil,
              controllerId: String? = nil, label: String = "") -> UsbConnector {
        UsbConnector(id: "ROOT#\(usb2)", hubInstanceId: "ROOT", controllerId: controllerId ?? hcId, controllerName: hc,
                     number: usb2, usb2Port: usb2, usb3Port: usb3, supportsUsb3: usb3 > 0, typeC: typeC, userVisible: visible,
                     device: device, label: label.isEmpty ? "port \(usb2)" : label)
    }

    var ports: [UsbConnector] {
        [port(9, 0), port(5, 20), port(11, 26, typeC: true),
         port(1, 17, device: UsbDevice(instanceId: "x", vid: 1, pid: 1, name: "Keyboard", link: .full, capable: .full, hub: nil)),
         port(14, 0, visible: false)]
    }

    func testCapableDeviceOnUsb2OnlyPort() throws {
        let a = Advisor.advise([usb("Cíl 1", "WD Elements", .high, .superSpeed, false, 9)], connectors: ports)
        let w = try XCTUnwrap(a.first { $0.kind == .warning })
        XCTAssertEqual(a.filter { $0.kind == .warning }.count, 1)
        XCTAssertTrue(w.text.contains("Cíl 1 WD Elements"))
        XCTAssertTrue(w.text.contains("umí USB 3 (5 Gb/s)"))
        XCTAssertTrue(w.text.contains("jen USB 2.0 (port 9)"))
        XCTAssertTrue(w.text.contains("port 5 USB 3"))
        XCTAssertTrue(w.text.contains("port 11 USB-C"))
    }

    func testWarningUsesSocketLabelAndNames() {
        let a = Advisor.advise([usb("Cíl 1", "WD", .high, .superSpeed, false, 9)], connectors: ports, portNames: ["ROOT#9": "zadní dole"])
        XCTAssertTrue(a.first { $0.kind == .warning }!.text.contains("jen USB 2.0 (zadní dole)"))
    }

    func testCapableDeviceSlowLinkOnUsb3Connector() {
        let w = Advisor.advise([usb("Karta", "SanDisk reader", .high, .superSpeed, true, 5)], connectors: ports).first { $0.kind == .warning }!
        XCTAssertTrue(w.text.contains("kabel"))
        XCTAssertFalse(w.text.contains("jen USB 2.0"))
    }

    func testUnknownSocketKindNoUsb2Claim() {
        let w = Advisor.advise([usb("Cíl 1", "SSD", .high, .superSpeed, nil, 5)], connectors: ports).first { $0.kind == .warning }!
        XCTAssertFalse(w.text.contains("jen USB 2.0"))
        XCTAssertTrue(w.text.contains("USB 3 port"))
        XCTAssertFalse(w.text.contains("port 5 USB 3"))   // never "move to the socket you are in"
    }

    func testSharedController() {
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 5),
                                usb("Cíl 1", "SSD", .superSpeed, .superSpeed, true, 11)], connectors: ports)
        XCTAssertTrue(a.contains { $0.kind == .info && $0.text.contains("Karta, Cíl 1") && $0.text.contains("jeden řadič") })
        XCTAssertFalse(a.contains { $0.text.contains("různé řadiče") })
    }

    func testSameNamedControllersNotShared() {
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 5, controller: "USB řadič", controllerId: "A"),
                                usb("Cíl 1", "SSD", .superSpeed, .superSpeed, true, 11, controller: "USB řadič", controllerId: "B")],
                               connectors: ports)
        XCTAssertFalse(a.contains { $0.text.contains("jeden řadič") })
    }

    func testSplitAcrossControllers() throws {
        let more = ports + [port(3, 4, controllerId: "ASMEDIA", label: "ASMedia xHCI · port 3")]
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 5),
                                usb("Cíl 1", "SSD", .superSpeed, .superSpeed, true, 11)], connectors: more)
        let tip = try XCTUnwrap(a.first { $0.text.contains("různé řadiče") })
        XCTAssertEqual(tip.kind, .tip)
        XCTAssertTrue(tip.text.contains("ASMedia xHCI · port 3 USB 3"))
        XCTAssertFalse(tip.text.contains("port 5"))
    }

    func testSharedHub() throws {
        let dock = UsbHubRef(instanceId: "loc:14100000", name: "USB3.0 Hub")
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 1, hubs: [dock]),
                                usb("Cíl 1", "SSD", .superSpeed, .superSpeed, true, 2, hubs: [UsbHubRef(instanceId: "loc:14110000", name: "Hub"), dock]),
                                usb("Cíl 2", "HDD", .superSpeed, .superSpeed, true, 11)], connectors: ports)
        let info = try XCTUnwrap(a.first { $0.text.contains("jeden hub") })
        XCTAssertEqual(a.filter { $0.text.contains("jeden hub") }.count, 1)
        XCTAssertEqual(info.kind, .info)
        XCTAssertEqual(info.text, "Karta, Cíl 1 jdou přes jeden hub (USB3.0 Hub) — dělí se o jeho linku.")
    }

    func testPortListsAndNames() {
        let a = Advisor.advise([], connectors: ports, portNames: ["ROOT#5": "přední levý"])
        XCTAssertTrue(a.contains { $0.kind == .tip && $0.text.hasPrefix("Volné rychlé porty: ") && $0.text.contains("přední levý") && $0.text.contains("port 11 USB-C") })
        XCTAssertTrue(a.contains { $0.kind == .tip && $0.text.hasSuffix("myš): port 9.") })
    }

    func testMeasuredEstimateAndSlowDisk() {
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 5),
                                usb("Cíl 1", "WD Elements", .superSpeed, .superSpeed, true, 11),
                                RoleLink(role: "Cíl 2", link: DriveLink(root: "/", bus: .sata, product: "ST1000LM035", usb: nil))],
                               connectors: ports, measuredMBps: ["Karta": 300, "Cíl 1": 26, "Cíl 2": 98])
        XCTAssertTrue(a.contains { $0.kind == .info && $0.text == "Odhad rychlosti offloadu s tímto zapojením: 26 MB/s (brzdí Cíl 1 WD Elements)." })
        XCTAssertTrue(a.contains { $0.text.contains("Cíl 1 WD Elements") && $0.text.contains("brzdí disk, ne port") })
    }

    func testOrderWarningsFirst() {
        let a = Advisor.advise([usb("Cíl 1", "WD", .high, .superSpeed, false, 9)], connectors: ports)
        XCTAssertEqual(a.first?.kind, .warning)
        XCTAssertEqual(a.map(\.kind), a.map(\.kind).sorted())
    }

    // From the topology fixtures: unknown kind is listed nowhere; a reader at USB 2.0 behind a merged USB 3 hub socket
    // gets "cable", never "move to <the same port>".
    func testTopologyFixturesThroughTheAdvisor() throws {
        let unknown = Connectors.build(UsbHub(instanceId: "ROOT", name: "Root", ports: [p(1, nil, companion: 5), p(2, nil), p(5, nil, companion: 1)]),
                                       controllerId: "HC", controllerName: "USB řadič XHC1")
        XCTAssertFalse(Advisor.advise([], connectors: unknown).contains { $0.text.contains("port 2") })

        let c = dualHalfHub()
        let link = DriveLink(root: "/Volumes/F", bus: .usb, product: "reader",
                             usb: UsbLink(controllerId: "HC", controllerName: "USB řadič XHC1", connectorId: "HS#2", connectorPort: 2, port: 2,
                                          link: .high, capable: .superSpeed, connectorUsb3: true, uasp: false))
        let w = try XCTUnwrap(Advisor.advise([RoleLink(role: "Karta", link: link)], connectors: c).first { $0.kind == .warning })
        XCTAssertTrue(w.text.contains("kabel"))
        XCTAssertFalse(w.text.contains("port 1 › 2"))
        XCTAssertEqual(link.describe(c), "reader — USB 2.0 (480 Mb/s), port 1 › 2")
    }
}
```

`mac/Tests/OffloadKitTests/ChainViewTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class ChainViewTests: XCTestCase {
    func usb(_ role: String, _ product: String, _ link: UsbLinkSpeed, controllerId: String = "HC") -> RoleLink {
        RoleLink(role: role, link: DriveLink(root: "/Volumes/x", bus: .usb, product: product,
                                             usb: UsbLink(controllerId: controllerId, controllerName: "USB řadič XHC1", connectorId: "R#5",
                                                          connectorPort: 5, port: 5, link: link, capable: link, connectorUsb3: true, uasp: false)))
    }

    func sata(_ role: String) -> RoleLink { RoleLink(role: role, link: DriveLink(root: "/", bus: .sata, product: "ST1000", usb: nil)) }

    func testBuildUtilizationAndConnection() {
        let c = ChainView.build([usb("Karta", "reader", .superSpeed), sata("Cíl 1")], mbps: [225, 98])
        XCTAssertEqual(c[0].utilization!, 0.5, accuracy: 0.01)
        XCTAssertEqual(c[0].connection, "USB 3 (5 Gb/s), port 5")
        XCTAssertNil(c[1].utilization)
        XCTAssertEqual(c[1].connection, "SATA")
    }

    func testDescribeBrakeAndFullLink() {
        let chain = ChainView.build([usb("Karta", "reader", .high), sata("Cíl 1")], mbps: [38, 38])
        XCTAssertEqual(ChainView.describe(Brake(element: 1, mbps: 38, utilization: nil), chain), "Cíl 1 ST1000")
        XCTAssertEqual(ChainView.describe(Brake(element: 0, mbps: 38, utilization: 0.95), chain), "Karta reader — linka je plná (USB 2.0 (480 Mb/s))")
        XCTAssertEqual(ChainView.describe(Brake(element: 0, mbps: 20, utilization: 0.5), chain), "Karta reader")
        XCTAssertEqual(ChainView.describe(Brake(element: 7, mbps: 20, utilization: nil), chain), "")
    }

    func testLinesIncludeControllerSum() {
        let chain = ChainView.build([usb("Karta", "reader", .superSpeed), usb("Cíl 1", "SSD", .superSpeed)], mbps: [300, 290])
        let lines = ChainView.lines(chain, brake: Brake(element: 1, mbps: 290, utilization: 0.64))
        XCTAssertTrue(lines.contains { $0.hasPrefix("Karta") && $0.contains("300 MB/s") && $0.contains("67 % linky") })
        XCTAssertTrue(lines.contains("Řadič USB řadič XHC1: 590 MB/s"))
        XCTAssertTrue(lines.contains("Úzké hrdlo: Cíl 1 SSD"))
    }

    func testSameNamedControllersNotSummed() {
        let chain = ChainView.build([usb("Karta", "reader", .superSpeed, controllerId: "A"), usb("Cíl 1", "SSD", .superSpeed, controllerId: "B")],
                                    mbps: [300, 290])
        XCTAssertFalse(ChainView.lines(chain, brake: nil).contains { $0.hasPrefix("Řadič") })
    }
}
```

`mac/Tests/OffloadKitTests/DriveLinksTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class DriveLinksTests: XCTestCase {
    func testNetworkAndBusLabels() {
        XCTAssertEqual(DriveLinks.network("/Volumes/media", "//nas/media").describe(), "//nas/media — síťový disk")
        XCTAssertEqual(DriveLinks.network("/Volumes/media", nil).describe(), "/Volumes/media — síťový disk")
        XCTAssertEqual(DriveLink(root: "/Volumes/Q", bus: .unknown, product: "", usb: nil).describe(), "neznámé připojení")
        XCTAssertEqual(DriveLink(root: "/Volumes/G", bus: .sd, product: "", usb: nil).describe(), "SD čtečka")
        XCTAssertEqual(DriveLink(root: "/Volumes/V", bus: .virtual, product: "Disk", usb: nil).describe(), "Disk — virtuální disk")
        XCTAssertEqual(DriveLink(root: "/Volumes/T", bus: .thunderbolt, product: "OWC Atlas", usb: nil).describe(), "OWC Atlas — Thunderbolt")
        XCTAssertEqual(StorageBuses.label(.usb), "USB (port nezjištěn)")
        // No raw English enum names reach the user.
        for bus in [StorageBus.usb, .sd, .mmc, .virtual, .network, .unknown, .other] {
            XCTAssertNotEqual(bus.rawValue.lowercased(), StorageBuses.label(bus).lowercased())
        }
    }

    // Windows final review 1: the drive line and the port list name one device's socket the same way.
    func testDriveLineAndPortListShareTheLabel() throws {
        let hub = UsbHub(instanceId: "ROOT", name: "Root", ports: [
            UsbPort(number: 6, supportsUsb3: false, userVisible: true, typeC: false, companionPort: 19, device: nil),
            UsbPort(number: 19, supportsUsb3: true, userVisible: true, typeC: false, companionPort: 6,
                    device: UsbDevice(instanceId: "loc:14600000", vid: 1, pid: 2, name: "Elements 25A2", link: .superSpeed, capable: .superSpeed, hub: nil)),
        ])
        let connectors = Connectors.build(hub, controllerId: "HC", controllerName: "USB řadič XHC1")
        let socket = try XCTUnwrap(connectors.first)
        XCTAssertEqual(connectors.count, 1)
        let link = DriveLink(root: "/Volumes/E", bus: .usb, product: "WD Elements 25A2",
                             usb: UsbLink(controllerId: "HC", controllerName: "USB řadič XHC1", connectorId: "ROOT#19", connectorPort: 6,
                                          port: 19, link: .superSpeed, capable: .superSpeed, connectorUsb3: true, uasp: true))
        XCTAssertEqual(link.describe(connectors), "WD Elements 25A2 — USB 3 (5 Gb/s), port 6")
        let row = PortLabels.listRow(socket, drives: [RoleLink(role: "E", link: link)])
        XCTAssertTrue(row.hasPrefix("port 6 USB 3"))
        XCTAssertTrue(row.hasSuffix("← E"))
        XCTAssertEqual(PortLabels.of(link.usb!, connectors), "port 6")
        // A user's name replaces the label everywhere, whichever half the drive reports.
        let names = ["ROOT#6": "přední levý"]
        XCTAssertEqual(link.describe(connectors, names), "WD Elements 25A2 — USB 3 (5 Gb/s), přední levý")
        XCTAssertTrue(PortLabels.listRow(socket, drives: [RoleLink(role: "Cíl 1", link: link)], names: names).hasPrefix("přední levý USB 3"))
        XCTAssertTrue(PortLabels.listRow(socket, drives: [RoleLink(role: "Cíl 1", link: link)], names: names).hasSuffix("← E (Cíl 1)"))
        // Without the topology: the socket number the link carries, not the raw USB 3 port.
        XCTAssertEqual(link.describe(), "WD Elements 25A2 — USB 3 (5 Gb/s), port 6")
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "AdvisorTests|ChainViewTests|DriveLinksTests"` → nesestaví se.

- [ ] **Step 3: `mac/Sources/OffloadKit/Hardware/DriveLinks.swift`**:

```swift
import Foundation

public enum StorageBus: String, Codable {
    case unknown, usb, sata, nvme, sd, mmc, scsi, sas, raid, virtual, network, thunderbolt, other
}

public enum StorageBuses {
    /// Czech name of a connection without a USB link.
    public static func label(_ bus: StorageBus) -> String {
        switch bus {
        case .nvme: return "NVMe"
        case .sata: return "SATA"
        case .sd, .mmc: return "SD čtečka"
        case .virtual: return "virtuální disk"
        case .network: return "síťový disk"
        case .usb: return "USB (port nezjištěn)"
        case .scsi: return "SCSI"
        case .sas: return "SAS"
        case .raid: return "RAID"
        case .thunderbolt: return "Thunderbolt"
        case .other: return "jiné připojení"
        case .unknown: return "neznámé připojení"
        }
    }
}

/// A hub between a device and its controller (a dock, an external hub, a monitor's hub).
public struct UsbHubRef: Codable, Equatable {
    public let instanceId: String
    public let name: String
}

/// Where a USB drive sits. `connectorId` matches `UsbConnector.id` or one of its `altIds`; `connectorPort` is the
/// socket's number for the fallback label; `port` the raw port of the half the device is on.
/// `connectorUsb3`: nil = unknown. `hubs`: from the device up to the controller, nearest first; empty = straight in.
public struct UsbLink: Codable, Equatable {
    public let controllerId: String
    public let controllerName: String
    public let connectorId: String
    public let connectorPort: Int
    public let port: Int
    public let link: UsbLinkSpeed
    public let capable: UsbLinkSpeed
    public let connectorUsb3: Bool?
    public let uasp: Bool
    public var hubs: [UsbHubRef] = []
}

public struct DriveLink: Codable, Equatable {
    public let root: String
    public let bus: StorageBus
    public let product: String
    public let usb: UsbLink?

    /// "USB 3 (5 Gb/s), port 6" or "SATA" — the socket named through `PortLabels`, like the port list.
    public func connection(_ connectors: [UsbConnector]? = nil, _ names: [String: String]? = nil) -> String {
        if let u = usb { return "\(UsbSpeeds.label(u.link)), \(PortLabels.of(u, connectors, names))" }
        return StorageBuses.label(bus)
    }

    /// "WD Elements 25A2 — USB 3 (5 Gb/s), port 6"; without a product just the connection.
    public func describe(_ connectors: [UsbConnector]? = nil, _ names: [String: String]? = nil) -> String {
        product.isEmpty ? connection(connectors, names) : "\(product) — \(connection(connectors, names))"
    }
}

public enum DriveLinks {
    /// A network location: the share as the product, else the mount point.
    static func network(_ root: String, _ share: String?) -> DriveLink {
        let s = share?.trimmingCharacters(in: .whitespaces) ?? ""
        return DriveLink(root: root, bus: .network, product: s.isEmpty ? root : s, usb: nil)
    }
}

extension PortLabels {
    /// The name of the socket a drive is plugged into; without the topology, "port N" from the link itself.
    public static func of(_ u: UsbLink, _ connectors: [UsbConnector]?, _ names: [String: String]? = nil) -> String {
        if let c = find(connectors, u.connectorId) { return name(c, names) }
        return "port \(u.connectorPort)"
    }

    /// One row of the port list: "port 6 USB 3   Elements 25A2 — USB 3 (5 Gb/s)  ← SSD_A (Cíl 1)".
    public static func listRow(_ c: UsbConnector, drives: [RoleLink], names: [String: String]? = nil, labelWidth: Int = 24) -> String {
        let device = c.device.map { "\($0.name) — \(UsbSpeeds.label($0.link))" } ?? "volný"
        let here = drives.filter { d in d.link.usb.map { c.has($0.connectorId) } ?? false }.map { d -> String in
            let volume = volumeLabel(d.link.root)
            return d.role == volume ? volume : "\(volume) (\(d.role))"
        }
        let tag = here.isEmpty ? "" : "  ← " + here.joined(separator: ", ")
        return "\(padRight(withKind(c, names), labelWidth)) \(device)\(tag)"
    }

    /// "/Volumes/SSD_A" → "SSD_A"; "/" stays "/".
    static func volumeLabel(_ root: String) -> String { root == "/" ? "/" : (root as NSString).lastPathComponent }
}
```

- [ ] **Step 4: `mac/Sources/OffloadKit/Hardware/Advisor.swift`** (texty 1:1 s Windows `Advisor.cs`):

```swift
import Foundation

public enum AdviceKind: Int, Codable, Comparable {
    case warning, info, tip

    public static func < (a: AdviceKind, b: AdviceKind) -> Bool { a.rawValue < b.rawValue }
}

public struct Advice: Codable, Equatable {
    public let kind: AdviceKind
    public let text: String
}

/// A drive in its role for this offload ("Karta", "Cíl 1" …, or a volume name when listing every drive).
public struct RoleLink: Codable, Equatable {
    public let role: String
    public let link: DriveLink

    public init(role: String, link: DriveLink) {
        self.role = role
        self.link = link
    }

    public var isCard: Bool { role.hasPrefix("Karta") }
}

/// Groups in order of first appearance (like LINQ GroupBy).
func orderedGroups<T>(_ items: [T], by key: (T) -> String) -> [[T]] {
    var keys: [String] = []
    var groups: [String: [T]] = [:]
    for item in items {
        let k = key(item)
        if groups[k] == nil { keys.append(k) }
        groups[k, default: []].append(item)
    }
    return keys.map { groups[$0]! }
}

public enum Advisor {
    public static func advise(_ drives: [RoleLink], connectors: [UsbConnector], measuredMBps: [String: Double]? = nil,
                              portNames: [String: String]? = nil) -> [Advice] {
        var advice: [Advice] = []
        func who(_ r: RoleLink) -> String { "\(r.role) \(r.link.product)".trimmingCharacters(in: .whitespaces) }
        let freeFastPorts = connectors.filter { $0.userVisible && $0.supportsUsb3 == true && $0.device == nil }
        let freeFast = freeFastPorts.map { PortLabels.withKind($0, portNames) }
        // Bare names: the sentence already says "jen USB 2.0". Ports of unknown kind are in neither list.
        let slowOnly = connectors.filter { $0.userVisible && $0.supportsUsb3 == false }.map { PortLabels.name($0, portNames) }

        for r in drives {
            guard let u = r.link.usb, u.capable > u.link else { continue }
            // Never send the drive to the socket it is already in.
            let elsewhere = freeFastPorts.filter { !$0.has(u.connectorId) }.map { PortLabels.withKind($0, portNames) }
            let whereText = elsewhere.isEmpty ? "" : ": " + elsewhere.joined(separator: ", ")
            let port = PortLabels.of(u, connectors, portNames)
            let text: String
            switch u.connectorUsb3 {
            case false?:
                text = "\(who(r)): umí \(UsbSpeeds.label(u.capable)), ale je v portu jen USB 2.0 (\(port)) — přepoj do USB 3 portu\(whereText)."
            case true?:
                text = "\(who(r)): umí \(UsbSpeeds.label(u.capable)), ale linka běží jako \(UsbSpeeds.label(u.link)) — pravděpodobně kabel jen pro USB 2.0 nebo špatný kontakt; zkus jiný kabel nebo port."
            case nil:
                text = "\(who(r)): umí \(UsbSpeeds.label(u.capable)), ale linka běží jako \(UsbSpeeds.label(u.link)) (\(port)) — zkus jiný kabel nebo USB 3 port\(whereText)."
            }
            advice.append(Advice(kind: .warning, text: text))
        }

        // Shared controller: grouped by the controller's identity, not its name.
        let usbDrives = drives.filter { $0.link.usb != nil }
        for group in orderedGroups(usbDrives, by: { $0.link.usb!.controllerId.lowercased() }) where group.count >= 2 {
            let key = group[0].link.usb!.controllerId.lowercased()
            advice.append(Advice(kind: .info,
                                 text: "\(group.map(\.role).joined(separator: ", ")) jdou přes jeden řadič (\(group[0].link.usb!.controllerName)) — dělí se o jeho kapacitu."))
            // Reader and disks on one controller while another controller has a free fast socket: split them.
            if group.contains(where: \.isCard) && group.contains(where: { !$0.isCard }) {
                let other = freeFastPorts.filter { $0.controllerId.lowercased() != key }.map { PortLabels.withKind($0, portNames) }
                if !other.isEmpty {
                    advice.append(Advice(kind: .tip,
                                         text: "Čtečku a cílové disky rozděl na různé řadiče — volné rychlé porty na jiném řadiči: \(other.joined(separator: ", "))."))
                }
            }
        }

        // Shared external hub / dock: the roles share its one uplink. Nearest hub first, each set of roles once.
        var hubs: [(hub: UsbHubRef, depth: Int, roles: [String])] = []
        for r in usbDrives {
            for (depth, h) in r.link.usb!.hubs.enumerated() {
                if let i = hubs.firstIndex(where: { $0.hub.instanceId.lowercased() == h.instanceId.lowercased() }) {
                    hubs[i].depth = min(hubs[i].depth, depth)
                    if !hubs[i].roles.contains(r.role) { hubs[i].roles.append(r.role) }
                } else {
                    hubs.append((h, depth, [r.role]))
                }
            }
        }
        var reported = Set<String>()
        let shared = hubs.enumerated().filter { $0.element.roles.count >= 2 }
            .sorted { ($0.element.depth, $0.offset) < ($1.element.depth, $1.offset) }.map(\.element)
        for h in shared {
            let whoText = h.roles.joined(separator: ", ")
            if reported.insert(whoText).inserted {
                advice.append(Advice(kind: .info, text: "\(whoText) jdou přes jeden hub (\(h.hub.name)) — dělí se o jeho linku."))
            }
        }

        if let measured = measuredMBps, !measured.isEmpty {
            let withPace = drives.filter { measured[$0.role] != nil }
            if let slowest = withPace.min(by: { measured[$0.role]! < measured[$1.role]! }) {
                advice.append(Advice(kind: .info,
                                     text: "Odhad rychlosti offloadu s tímto zapojením: \(mb(measured[slowest.role]!)) MB/s (brzdí \(who(slowest)))."))
            }
            for r in withPace {
                guard let u = r.link.usb, u.link >= .superSpeed else { continue }
                let ceiling = UsbSpeeds.practicalMBps(u.link)
                if measured[r.role]! < ceiling / 2 {
                    advice.append(Advice(kind: .info,
                                         text: "\(who(r)): linka zvládne ~\(mb(ceiling)) MB/s, disk sám dává \(mb(measured[r.role]!)) MB/s — brzdí disk, ne port."))
                }
            }
        }

        if !freeFast.isEmpty { advice.append(Advice(kind: .tip, text: "Volné rychlé porty: \(freeFast.joined(separator: ", ")).")) }
        if !slowOnly.isEmpty {
            advice.append(Advice(kind: .tip, text: "Porty jen USB 2.0 — na offload je nepoužívej (hodí se pro klávesnici, myš): \(slowOnly.joined(separator: ", "))."))
        }
        return stable(advice)
    }

    /// Warnings first, then info, then tips — keeping the order within each kind.
    static func stable(_ advice: [Advice]) -> [Advice] {
        advice.enumerated().sorted { ($0.element.kind.rawValue, $0.offset) < ($1.element.kind.rawValue, $1.offset) }.map(\.element)
    }

    static func mb(_ v: Double) -> String { String(format: "%.0f", v) }
}
```

- [ ] **Step 5: `mac/Sources/OffloadKit/Hardware/ChainView.swift`**:

```swift
import Foundation

/// One link of the chain. `link`: the USB speed label ("USB 3 (5 Gb/s)") or nil for disks without a USB link.
public struct ChainElement: Equatable {
    public let role: String
    public let product: String
    public let connection: String
    public let link: String?
    public let linkMBps: Double?
    public let mbps: Double
    public let controllerId: String?
    public let controllerName: String?

    public var utilization: Double? {
        guard let l = linkMBps, l > 0 else { return nil }
        return mbps / l
    }
}

/// The chain card → reader/link → controller → disks with live throughput, and what is holding it back.
/// In the CLI only the brake shows (status line); the full view is the GUI job card.
public enum ChainView {
    public static func build(_ links: [RoleLink], mbps: [Double], connectors: [UsbConnector]? = nil,
                             portNames: [String: String]? = nil) -> [ChainElement] {
        links.enumerated().map { i, r in
            let u = r.link.usb
            return ChainElement(role: r.role, product: r.link.product, connection: r.link.connection(connectors, portNames),
                                link: u.map { UsbSpeeds.label($0.link) }, linkMBps: linkCeiling(r),
                                mbps: i < mbps.count ? mbps[i] : 0, controllerId: u?.controllerId, controllerName: u?.controllerName)
        }
    }

    /// Realistic MB/s ceiling of the element's USB link; nil without one (no "% linky").
    public static func linkCeiling(_ r: RoleLink) -> Double? {
        guard let u = r.link.usb else { return nil }
        let c = UsbSpeeds.practicalMBps(u.link)
        return c > 0 ? c : nil
    }

    /// "Cíl 1 ST1000" or "Karta reader — linka je plná (USB 2.0 (480 Mb/s))".
    public static func describe(_ brake: Brake, _ chain: [ChainElement]) -> String {
        guard brake.element >= 0, brake.element < chain.count else { return "" }
        let e = chain[brake.element]
        let who = "\(e.role) \(e.product)".trimmingCharacters(in: .whitespaces)
        if brake.linkFull, let link = e.link { return "\(who) — linka je plná (\(link))" }
        return who
    }

    public static func lines(_ chain: [ChainElement], brake: Brake?) -> [String] {
        var lines = chain.map { e -> String in
            let util = e.utilization.map { "  " + String(format: "%.0f", $0 * 100) + " % linky" } ?? ""
            return "\(e.role) \(e.product) — \(e.connection)  \(String(format: "%.0f", e.mbps)) MB/s\(util)"
        }
        // Shared controller: by identity — two controllers may carry the same name.
        for g in orderedGroups(chain.filter { $0.controllerId != nil }, by: { $0.controllerId!.lowercased() }) where g.count >= 2 {
            lines.append("Řadič \(g[0].controllerName ?? ""): \(String(format: "%.0f", g.reduce(0) { $0 + $1.mbps })) MB/s")
        }
        if let b = brake {
            let text = describe(b, chain)
            if !text.isEmpty { lines.append("Úzké hrdlo: \(text)") }
        }
        return lines
    }
}
```

- [ ] **Step 6: Testy projdou** — Run: `swift test --filter "AdvisorTests|ChainViewTests|DriveLinksTests"` → 12 + 4 + 2 testů, 0 chyb; pak `swift test`.

- [ ] **Step 7: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 13 — HW model: kde sedí disk (sběrnice, port, linka, UAS), doporučení zapojení (texty jako Windows), řetěz připojení s % linky, součtem na řadiči a úzkým hrdlem`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: drive links, advisor and connection chain (pure model)"`

---

### Task 14: macOS — USB topologie (IOKit) a připojení svazků (DiskArbitration)

**Files:**
- Modify: `mac/Package.swift` (linker settings OffloadKit)
- Create: `mac/Sources/OffloadKit/Hardware/IORegistry.swift`, `mac/Sources/OffloadKit/Hardware/MacUsbScanner.swift`, `mac/Sources/OffloadKit/Hardware/MacDriveLinks.swift`
- Create: `mac/Tests/OffloadKitTests/MacHardwareTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: úkoly 12–13.
- Produces:
  - `final class IOEntry` (internal): `property(_:)`, `int(_:)`, `uint64(_:)`, `string(_:)`, `className`, `conforms(to:)`, `name`, `path`, `parent`, `children`, `ancestors`, `static matching(_:)`
  - `public enum UsbScanner { static func scan() -> UsbTopology }` + internal `Keys`, `RawPort`, `pair(_:)`, `uniqueNumbers(_:)`, `portEntries(of:)`, `rawPorts(of:)`, `isPort/isDevice/isController`, `portKind`, `deviceId`, `productName`, `controllerName`, `linkSpeed`
  - `extension DriveLinks { public static func resolve(_ path: String) -> DriveLink; static func busFrom(protocol:classes:hasUsbDevice:) -> StorageBus }`

> **Nejistoty (spec oddíl 9):** názvy tříd a vlastností níže jsou nejlepší znalost, ne ověřený fakt. Krok 1 je
> ověření na Macu; všechny klíče jsou v `UsbScanner.Keys`, takže oprava je na jednom místě. Výsledky výpisů
> (zkrácené) dejte do zprávy commitu.

- [ ] **Step 1: Ověřit IORegistry na tomto Macu (s připojeným USB diskem a čtečkou s kartou)**

```sh
ioreg -p IOService -w0 -r -c AppleUSBXHCI -d 1                 # řadiče: třída a jméno (XHC1 / usb-drd0 …)
ioreg -p IOService -l -w0 -r -c AppleUSBHostPort | grep -E '^[ |]*\+-o|"port"|"UsbCPortNumber"|"UsbConnector"|Companion'
ioreg -p IOService -w0 -r -c AppleUSBHostPort -d 2            # je IOUSBHostDevice dítětem portu?
ioreg -p IOService -w0 -r -c IOUSBHostDevice -d 3             # S PŘIPOJENÝM HUBEM: visí porty hubu přímo pod zařízením, nebo pod ovladačem (AppleUSB20Hub/AppleUSB30Hub…)?
ioreg -p IOService -w0 -t -r -c AppleUSBXHCI                  # cesta každého řadiče ke kořeni: je mezi předky třída s „Thunderbolt“? je řadič IOPCIDevice?
ioreg -p IOUSB -l -w0 | grep -E '"(USB Product Name|kUSBProductString|UsbLinkSpeed|Device Speed|bcdUSB|locationID|bDeviceClass)"'
ioreg -l -w0 -r -c IOUSBHostDevice | grep -iE 'AttachedSCSI|MassStorage'
system_profiler SPUSBDataType -json | grep -E '"(_name|speed|location_id)"'
diskutil info -plist /Volumes/<USB disk> | plutil -p - | grep -E 'BusProtocol|DeviceModel|MediaName|Internal|VolumeUUID'
```

Expected/zapsat: (a) třída řadiče (pokud `AppleUSBXHCI` nic nevrátí, zkusit `-c AppleUSBHostController` a upravit `Keys.controllerClasses`); (b) třída portů a zda mají `port` (Data) a `UsbConnector`; (c) **která vlastnost páruje HS a SS polovinu jedné zásuvky** — najít dvojici `…20…Port`/`…30…Port` se stejnou hodnotou (kandidát `UsbCPortNumber`); dát její jméno do `Keys.pairKeys` (víc kandidátů = víc položek); když žádná neexistuje, nechat `["UsbCPortNumber"]` — kód pak USB 2 poloviny bez páru hlásí jako neznámé (bezpečné); (d) zda zařízení má `UsbLinkSpeed` a/nebo `Device Speed` a jejich hodnoty odpovídají `system_profiler` `speed`; (e) jméno UAS ovladače (`IOUSBAttachedSCSI…`); (f) **kde visí porty hubu** — přímo pod `IOUSBHostDevice`, nebo o úroveň níž pod ovladačem hubu (kód bere obojí: děti i vnoučata, viz `portEntries`); (g) **hodnoty `port`**: jsou v rámci jednoho řadiče/hubu unikátní? Na Apple Silicon má každý `usb-drd*` nejspíš jeden HS a jeden SS port, možná oba s `port` = 1 — kód duplicitní čísla přečísluje (`uniqueNumbers`) a jediný HS + jediný SS port spáruje i bez klíče; (h) **Thunderbolt**: přípona „ (Thunderbolt)“ se dává jen řadiči, který je `IOPCIDevice` a má mezi předky třídu s „Thunderbolt“ (dok / TB řadič); vestavěné `usb-drd*` na Apple Silicon ji mít nesmí — když ji výpis `-t` ukáže u všech, upravit `controllerName`.

- [ ] **Step 2: Padající testy** — `mac/Tests/OffloadKitTests/MacHardwareTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class MacHardwareTests: XCTestCase {
    // Pure: how the HS/SS halves of one socket are paired, and the safe fallback without a pairing key.
    func testPairByKeyAndUnknownWithoutIt() {
        let paired = UsbScanner.pair([
            .init(number: 1, usb3: false, pairKey: 7), .init(number: 2, usb3: false, pairKey: nil),
            .init(number: 17, usb3: true, pairKey: 7), .init(number: 18, usb3: true, pairKey: nil),
        ])
        XCTAssertEqual(paired.map(\.number), [1, 2, 17, 18])
        XCTAssertEqual(paired.map(\.companion), [17, 0, 1, 0])
        XCTAssertEqual(paired[0].usb3, false)
        XCTAssertNil(paired[1].usb3, "a USB 2 port without a twin next to USB 3 ports is of unknown kind")
        XCTAssertEqual(paired[3].usb3, true)
    }

    // Apple Silicon: one HS and one SS port per usb-drd controller, no pairing key → still one socket.
    func testSingleHsAndSsPortAreOneSocketWithoutAKey() {
        let paired = UsbScanner.pair([.init(number: 1, usb3: false, pairKey: nil), .init(number: 1001, usb3: true, pairKey: nil)])
        XCTAssertEqual(paired.map(\.companion), [1001, 1])
        XCTAssertEqual(paired.map(\.usb3), [false, true])
        let hub = UsbHub(instanceId: "C", name: "C", ports: paired.map {
            UsbPort(number: $0.number, supportsUsb3: $0.usb3, userVisible: true, typeC: true, companionPort: $0.companion, device: nil)
        })
        XCTAssertEqual(Connectors.build(hub, controllerId: "C", controllerName: "USB řadič usb-drd0").map(\.defaultLabel), ["port 1 USB-C"])
    }

    func testRepeatedPortNumbersAreMadeUnique() {
        XCTAssertEqual(UsbScanner.uniqueNumbers([1, 1, 2, 1]), [1, 1001, 2, 2001])
        XCTAssertEqual(UsbScanner.uniqueNumbers([3, 17]), [3, 17])
    }

    func testUsb2OnlyHubStaysUsb2() {
        let paired = UsbScanner.pair([.init(number: 1, usb3: false, pairKey: nil), .init(number: 2, usb3: false, pairKey: nil)])
        XCTAssertEqual(paired.map(\.usb3), [false, false])
    }

    func testPairedRootFeedsTheConnectorBuilder() {
        let paired = UsbScanner.pair([.init(number: 1, usb3: false, pairKey: 7), .init(number: 2, usb3: false, pairKey: nil),
                                      .init(number: 17, usb3: true, pairKey: 7)])
        let hub = UsbHub(instanceId: "C", name: "C", ports: paired.map {
            UsbPort(number: $0.number, supportsUsb3: $0.usb3, userVisible: true, typeC: false, companionPort: $0.companion, device: nil)
        })
        let c = Connectors.build(hub, controllerId: "C", controllerName: "USB řadič XHC1")
        XCTAssertEqual(c.map(\.defaultLabel), ["port 1 USB 3", "port 2"])
        XCTAssertFalse(Advisor.advise([], connectors: c).contains { $0.text.contains("jen USB 2.0") })
    }

    func testBusFromProtocolAndClasses() {
        XCTAssertEqual(DriveLinks.busFrom(protocol: "USB", classes: [], hasUsbDevice: true), .usb)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "PCI-Express", classes: ["IOMedia", "IONVMeController", "AppleThunderboltPCIDownAdapter"], hasUsbDevice: false), .thunderbolt)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "Apple Fabric", classes: ["IOMedia", "AppleANS3NVMeController"], hasUsbDevice: false), .nvme)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "PCI-Express", classes: ["IOMedia", "IONVMeController"], hasUsbDevice: false), .nvme)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "SATA", classes: [], hasUsbDevice: false), .sata)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "Secure Digital", classes: [], hasUsbDevice: false), .sd)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "Disk Image", classes: [], hasUsbDevice: false), .virtual)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "USB", classes: [], hasUsbDevice: false), .usb)   // USB without a port found
        XCTAssertEqual(DriveLinks.busFrom(protocol: nil, classes: [], hasUsbDevice: false), .unknown)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "FireWire", classes: [], hasUsbDevice: false), .other)
    }

    // Smoke tests on the real Mac: never crash, sensible answers.
    func testScanNeverCrashesAndLabelsAreUnique() {
        let connectors = UsbScanner.scan().connectors
        XCTAssertEqual(Set(connectors.map(\.label)).count, connectors.count)
        for c in connectors { print("USB", c.controllerName, "|", c.defaultLabel, "|", c.device.map { "\($0.name) \(UsbSpeeds.label($0.link))" } ?? "volný") }
    }

    func testStartupVolumeIsInternalWithAProduct() {
        let d = DriveLinks.resolve("/")
        print("startup volume:", d.describe())
        XCTAssertNotEqual(d.bus, .usb)
        XCTAssertNotEqual(d.bus, .network)
        XCTAssertFalse(d.product.isEmpty, "DiskArbitration model of the startup disk")
    }

    func testMissingPathIsUnknownNotACrash() {
        XCTAssertEqual(DriveLinks.resolve("/no/such/path-\(UUID().uuidString)").bus, .unknown)
    }

    func testUsbVolumesResolveToASocket() throws {
        let volumes = (FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: [.skipHiddenVolumes]) ?? [])
            .map(\.path).filter { $0.hasPrefix("/Volumes/") }
        let usb = volumes.map(DriveLinks.resolve).filter { $0.bus == .usb }
        guard !usb.isEmpty else { throw XCTSkip("no USB volume connected") }
        let connectors = UsbScanner.scan().connectors
        for d in usb {
            print("USB volume:", d.root, "→", d.describe(connectors), "uasp:", d.usb?.uasp as Any)
            let u = try XCTUnwrap(d.usb, "\(d.root): USB volume without a port")
            XCTAssertNotEqual(u.link, .unknown)
            XCTAssertNotNil(PortLabels.find(connectors, u.connectorId), "\(d.root): the drive's socket is not in the port list")
        }
    }
}
```

- [ ] **Step 3: Ověřit selhání** — Run: `swift test --filter MacHardwareTests` → nesestaví se (`cannot find 'UsbScanner'`, `type 'DriveLinks' has no member 'resolve'`).

- [ ] **Step 4: `mac/Package.swift`** — target OffloadKit:

```swift
        .target(name: "OffloadKit", dependencies: ["CXXHash"],
                linkerSettings: [.linkedFramework("IOKit"), .linkedFramework("DiskArbitration")]),
```

- [ ] **Step 5: `mac/Sources/OffloadKit/Hardware/IORegistry.swift`**:

```swift
import Foundation
import IOKit

/// An IOKit registry object owned by Swift: released when this goes away. Reads only the IOService plane.
final class IOEntry {
    let raw: io_object_t

    init?(_ raw: io_object_t) {
        guard raw != 0 else { return nil }
        self.raw = raw
    }

    deinit { IOObjectRelease(raw) }

    func property(_ key: String) -> Any? {
        IORegistryEntryCreateCFProperty(raw, key as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue()
    }

    /// A number, or little-endian Data such as "port" = <01000000>.
    func int(_ key: String) -> Int? {
        switch property(key) {
        case let n as NSNumber:
            return n.intValue
        case let d as Data where !d.isEmpty:
            var v = 0
            for (i, b) in d.prefix(8).enumerated() { v |= Int(b) << (8 * i) }
            return v
        default:
            return nil
        }
    }

    func uint64(_ key: String) -> UInt64? { (property(key) as? NSNumber)?.uint64Value }

    func string(_ key: String) -> String? {
        switch property(key) {
        case let s as String:
            return s
        case let d as Data:
            return String(data: d, encoding: .utf8)?.trimmingCharacters(in: CharacterSet.controlCharacters.union(.whitespaces))
        default:
            return nil
        }
    }

    var className: String { IOObjectCopyClass(raw).map { $0.takeRetainedValue() as String } ?? "" }

    func conforms(to cls: String) -> Bool { IOObjectConformsTo(raw, cls) != 0 }

    var name: String {
        var buf = [CChar](repeating: 0, count: 128)
        return IORegistryEntryGetName(raw, &buf) == KERN_SUCCESS ? String(cString: buf) : ""
    }

    /// Stable across restarts (unlike the registry entry id) — used as a controller's identity.
    var path: String { IORegistryEntryCopyPath(raw, kIOServicePlane).map { $0.takeRetainedValue() as String } ?? "" }

    var parent: IOEntry? {
        var p: io_registry_entry_t = 0
        return IORegistryEntryGetParentEntry(raw, kIOServicePlane, &p) == KERN_SUCCESS ? IOEntry(p) : nil
    }

    var children: [IOEntry] {
        var it: io_iterator_t = 0
        guard IORegistryEntryGetChildIterator(raw, kIOServicePlane, &it) == KERN_SUCCESS else { return [] }
        defer { IOObjectRelease(it) }
        var out: [IOEntry] = []
        while let e = IOEntry(IOIteratorNext(it)) { out.append(e) }
        return out
    }

    /// This entry, then each parent up to the root of the service plane.
    var ancestors: [IOEntry] {
        var out = [self]
        var cur = parent
        while let c = cur, out.count < 64 {
            out.append(c)
            cur = c.parent
        }
        return out
    }

    static func matching(_ className: String) -> [IOEntry] {
        var it: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching(className), &it) == KERN_SUCCESS else { return [] }
        defer { IOObjectRelease(it) }
        var out: [IOEntry] = []
        while let e = IOEntry(IOIteratorNext(it)) { out.append(e) }
        return out
    }
}
```

- [ ] **Step 6: `mac/Sources/OffloadKit/Hardware/MacUsbScanner.swift`**:

```swift
import Foundation

/// The USB topology from the IORegistry (IOService plane): controllers → ports → devices → hubs → …
/// Never throws: what cannot be read is left out. All registry names are in `Keys` (verify with ioreg, spec 9).
public enum UsbScanner {
    enum Keys {
        static let controllerClasses = ["AppleUSBXHCI", "AppleUSBHostController"]
        static let portClass = "AppleUSBHostPort"
        static let deviceClasses = ["IOUSBHostDevice", "IOUSBDevice"]
        static let portNumber = "port"
        /// The property the HS and SS halves of one socket share (Task 14 step 1 decides; more candidates allowed).
        static let pairKeys = ["UsbCPortNumber"]
        /// ACPI _UPC connector type: 9/10/11 = USB-C, 255 = internal / proprietary.
        static let connectorType = "UsbConnector"
        static let productKeys = ["USB Product Name", "kUSBProductString"]
        static let linkSpeed = "UsbLinkSpeed"
        static let deviceSpeed = "Device Speed"
        static let bcdUSB = "bcdUSB"
        static let deviceClass = "bDeviceClass"
        static let locationID = "locationID"
        static let vendorID = "idVendor"
        static let productID = "idProduct"
        static let uasClassFragment = "IOUSBAttachedSCSI"
    }

    /// One port as the registry shows it: number, kind (nil = unknown) and the key the two halves of one socket share.
    struct RawPort: Equatable {
        let number: Int
        let usb3: Bool?
        let pairKey: Int?
    }

    public static func scan() -> UsbTopology {
        var controllers: [UsbController] = []
        var seen = Set<String>()
        for cls in Keys.controllerClasses where controllers.isEmpty {   // the base class only as a fallback
            for c in IOEntry.matching(cls) {
                let id = c.path
                guard !id.isEmpty, seen.insert(id).inserted else { continue }
                let name = controllerName(c)
                controllers.append(UsbController(instanceId: id, name: name, rootHubs: [readHub(c, id: id, name: name, depth: 0)]))
            }
        }
        return UsbTopology(controllers: controllers.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending })
    }

    /// Companion numbers from the shared key; a hub/controller with exactly one USB 2 and one USB 3 port (Apple Silicon
    /// `usb-drd*`: one USB-C socket per controller) pairs them even without a key. A USB 2 port still without a twin
    /// on a hub that also has USB 3 ports is of unknown kind — never claim "jen USB 2.0" without proof.
    /// Port numbers must be unique here (`rawPorts` makes them so).
    static func pair(_ ports: [RawPort]) -> [(number: Int, usb3: Bool?, companion: Int)] {
        let hasUsb3 = ports.contains { $0.usb3 == true }
        let usb2Ports = ports.filter { $0.usb3 == false }, usb3Ports = ports.filter { $0.usb3 == true }
        let single: (usb2: Int, usb3: Int)? = usb2Ports.count == 1 && usb3Ports.count == 1
            ? (usb2Ports[0].number, usb3Ports[0].number) : nil
        return ports.map { p in
            var companion = p.pairKey.flatMap { key in
                ports.first { $0.number != p.number && $0.pairKey == key && $0.usb3 != nil && $0.usb3 != p.usb3 }
            }?.number ?? 0
            if companion == 0, let single {
                if p.number == single.usb2 {
                    companion = single.usb3
                } else if p.number == single.usb3 {
                    companion = single.usb2
                }
            }
            var usb3 = p.usb3
            if companion == 0 && p.usb3 == false && hasUsb3 { usb3 = nil }
            return (p.number, usb3, companion)
        }
    }

    /// Port numbers made unique within one hub/controller: a repeated number (e.g. HS and SS port both "port" = 1)
    /// gets +1000 per repeat. Only ids and pairing see it; labels use the USB 2 twin's number.
    static func uniqueNumbers(_ numbers: [Int]) -> [Int] {
        var seen = Set<Int>()
        return numbers.map { n in
            var m = n
            while !seen.insert(m).inserted { m += 1000 }
            return m
        }
    }

    static func isPort(_ e: IOEntry) -> Bool { e.conforms(to: Keys.portClass) || e.className.hasSuffix("Port") }
    static func isDevice(_ e: IOEntry) -> Bool { Keys.deviceClasses.contains { e.conforms(to: $0) } }
    static func isController(_ e: IOEntry) -> Bool { Keys.controllerClasses.contains { e.conforms(to: $0) } }

    /// USB 3 from the port class or name (AppleUSB30XHCIPort, AppleUSB30HubPort, "SS01", "…-ss@…"), USB 2 likewise; else nil.
    static func portKind(_ e: IOEntry) -> Bool? {
        let cls = e.className, name = e.name.lowercased()
        if cls.contains("USB30") || cls.contains("USB31") || name.hasPrefix("ss") || name.contains("-ss") { return true }
        if cls.contains("USB20") || name.hasPrefix("hs") || name.contains("-hs") { return false }
        return nil
    }

    /// The ports of a controller or hub device: its children that are ports, and the ports one level down under a
    /// child that is neither port nor device (a hub's ports likely hang under its driver, e.g. AppleUSB30Hub — spec 9).
    static func portEntries(of owner: IOEntry) -> [IOEntry] {
        var out: [IOEntry] = []
        for child in owner.children {
            if isPort(child) {
                out.append(child)
            } else if !isDevice(child) {
                out += child.children.filter(isPort)
            }
        }
        return out
    }

    /// Both the scan and `DriveLinks.usbLink` number ports through here, so ids and pairing always agree.
    static func rawPorts(of owner: IOEntry) -> [(entry: IOEntry, raw: RawPort)] {
        let entries = portEntries(of: owner)
        let numbers = uniqueNumbers(entries.enumerated().map { i, e in e.int(Keys.portNumber) ?? (i + 1) })
        return entries.enumerated().map { i, e in
            (e, RawPort(number: numbers[i], usb3: portKind(e), pairKey: Keys.pairKeys.lazy.compactMap { e.int($0) }.first))
        }
    }

    static func readHub(_ owner: IOEntry, id: String, name: String, depth: Int) -> UsbHub {
        let raws = rawPorts(of: owner)
        let paired = pair(raws.map(\.raw))
        var ports: [UsbPort] = []
        for (i, item) in raws.enumerated() {
            let connector = item.entry.int(Keys.connectorType)
            let device = item.entry.children.first(where: isDevice).map { readDevice($0, depth: depth) }
            // A device already running at SuperSpeed proves the socket can do USB 3.
            let usb3 = device.map { $0.link >= .superSpeed } == true ? true : paired[i].usb3
            ports.append(UsbPort(number: paired[i].number, supportsUsb3: usb3, userVisible: connector != 255,
                                 typeC: [9, 10, 11].contains(connector ?? -1), companionPort: paired[i].companion, device: device))
        }
        return UsbHub(instanceId: id, name: name, ports: ports)
    }

    static func readDevice(_ e: IOEntry, depth: Int) -> UsbDevice {
        let link = linkSpeed(e)
        var hub: UsbHub?
        if e.int(Keys.deviceClass) == 9, depth < 6 {
            hub = readHub(e, id: deviceId(e), name: productName(e), depth: depth + 1)
        }
        return UsbDevice(instanceId: deviceId(e), vid: e.int(Keys.vendorID) ?? 0, pid: e.int(Keys.productID) ?? 0,
                         name: productName(e), link: link, capable: UsbSpeeds.capability(bcdUSB: e.int(Keys.bcdUSB), link: link), hub: hub)
    }

    static func linkSpeed(_ e: IOEntry) -> UsbLinkSpeed {
        UsbSpeeds.link(linkBitsPerSecond: e.uint64(Keys.linkSpeed), deviceSpeed: e.int(Keys.deviceSpeed))
    }

    /// "loc:<locationID>" — stable for one socket position; the registry path when locationID is missing.
    static func deviceId(_ e: IOEntry) -> String {
        if let loc = e.int(Keys.locationID) { return String(format: "loc:%08x", loc) }
        return e.path
    }

    static func productName(_ e: IOEntry) -> String {
        for key in Keys.productKeys {
            if let s = e.string(key), !s.isEmpty { return s }
        }
        return e.name
    }

    /// "USB řadič XHC1", "USB řadič usb-drd1", "… (Thunderbolt)" only for a PCI controller behind a Thunderbolt
    /// tunnel (a dock, an Intel TB controller) — built-in Apple Silicon controllers are not PCI devices (verify, step 1h).
    static func controllerName(_ c: IOEntry) -> String {
        let above = c.ancestors
        let pci = above.contains { $0.conforms(to: "IOPCIDevice") }
        let thunderbolt = pci && above.dropFirst().contains { $0.className.contains("Thunderbolt") }
        return "USB řadič \(c.name)" + (thunderbolt ? " (Thunderbolt)" : "")
    }
}
```

- [ ] **Step 7: `mac/Sources/OffloadKit/Hardware/MacDriveLinks.swift`**:

```swift
import DiskArbitration
import Foundation

extension DriveLinks {
    static let networkFilesystems: Set<String> = ["smbfs", "afpfs", "nfs", "webdav", "ftp", "cifs"]

    /// Where the volume holding `path` is connected. Never throws: anything unreadable stays unknown/nil.
    public static func resolve(_ path: String) -> DriveLink {
        var fs = statfs()
        guard statfs(path, &fs) == 0 else { return DriveLink(root: path, bus: .unknown, product: "", usb: nil) }
        let root = cString(fs.f_mntonname), from = cString(fs.f_mntfromname), type = cString(fs.f_fstypename)
        if networkFilesystems.contains(type) { return network(root, from) }
        guard from.hasPrefix("/dev/"), let session = DASessionCreate(kCFAllocatorDefault),
              let disk = DADiskCreateFromBSDName(kCFAllocatorDefault, session, String(from.dropFirst(5))) else {
            return DriveLink(root: root, bus: .unknown, product: "", usb: nil)
        }
        let desc = (DADiskCopyDescription(disk) as? [String: Any]) ?? [:]
        func str(_ key: CFString) -> String? { (desc[key as String] as? String)?.trimmingCharacters(in: .whitespaces) }
        let product = [str(kDADiskDescriptionDeviceVendorKey), str(kDADiskDescriptionDeviceModelKey)]
            .compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " ")
        // Up from the media: through APFS (volume → container → physical store → disk → driver → device).
        let chain = IOEntry(DADiskCopyIOMedia(disk))?.ancestors ?? []
        let bus = busFrom(protocol: str(kDADiskDescriptionDeviceProtocolKey), classes: chain.map(\.className),
                          hasUsbDevice: chain.contains(where: UsbScanner.isDevice))
        guard bus == .usb, let usb = usbLink(chain) else { return DriveLink(root: root, bus: bus, product: product, usb: nil) }
        return DriveLink(root: root, bus: bus, product: product, usb: usb)
    }

    /// From the DiskArbitration protocol and the registry classes between the media and the root.
    static func busFrom(protocol p: String?, classes: [String], hasUsbDevice: Bool) -> StorageBus {
        if hasUsbDevice { return .usb }
        if classes.contains(where: { $0.contains("Thunderbolt") }) { return .thunderbolt }
        switch (p ?? "").lowercased() {
        case "usb": return .usb
        case "sata", "serial ata", "ata": return .sata
        case "secure digital": return .sd
        case "pci-express", "pci", "apple fabric", "nvme":
            return classes.contains { $0.contains("NVMe") } ? .nvme : .other
        case "virtual interface", "disk image": return .virtual
        case "sas": return .sas
        case "scsi", "parallel scsi": return .scsi
        case "": return .unknown
        default: return .other
        }
    }

    /// The USB device above the media, its port, hubs on the way and the controller.
    static func usbLink(_ chain: [IOEntry]) -> UsbLink? {
        guard let di = chain.firstIndex(where: UsbScanner.isDevice), di + 2 < chain.count else { return nil }
        let device = chain[di]
        let port = chain[di + 1]
        guard UsbScanner.isPort(port) else { return nil }
        // The port's owner (for its id and its sibling ports): the first device or controller above the port —
        // a hub's ports may hang under a hub driver (AppleUSB30Hub…) between the port and the hub device (spec 9).
        guard let oi = chain[(di + 2)...].firstIndex(where: { UsbScanner.isDevice($0) || UsbScanner.isController($0) }) else {
            return nil
        }
        let owner = chain[oi]
        let uasp = chain[..<di].contains { $0.className.contains(UsbScanner.Keys.uasClassFragment) }
        var hubs: [UsbHubRef] = []
        var controller: IOEntry?
        for e in chain[oi...] {
            if UsbScanner.isController(e) {
                controller = e
                break
            }
            if UsbScanner.isDevice(e) { hubs.append(UsbHubRef(instanceId: UsbScanner.deviceId(e), name: UsbScanner.productName(e))) }
        }
        guard let controller else { return nil }
        let hubId = UsbScanner.isController(owner) ? owner.path : UsbScanner.deviceId(owner)
        let raws = UsbScanner.rawPorts(of: owner)
        // The number as the scan made it (unique per owner), found by the port's registry path.
        let number = raws.first { $0.entry.path == port.path }?.raw.number ?? port.int(UsbScanner.Keys.portNumber) ?? 0
        let paired = UsbScanner.pair(raws.map(\.raw)).first { $0.number == number }
        let kind = UsbScanner.portKind(port)
        let companion = paired?.companion ?? 0
        let link = UsbScanner.linkSpeed(device)
        // The same pairing as the port list: a paired or USB 3 port is a USB 3 socket, named by its USB 2 twin.
        let connectorUsb3: Bool? = kind == true || companion > 0 || link >= .superSpeed ? true : (paired?.usb3 ?? kind)
        let connectorPort = kind == true && companion > 0 ? companion : number
        return UsbLink(controllerId: controller.path, controllerName: UsbScanner.controllerName(controller),
                       connectorId: "\(hubId)#\(number)", connectorPort: connectorPort, port: number, link: link,
                       capable: UsbSpeeds.capability(bcdUSB: device.int(UsbScanner.Keys.bcdUSB), link: link),
                       connectorUsb3: connectorUsb3, uasp: uasp, hubs: hubs)
    }

    static func cString<T>(_ tuple: T) -> String {
        withUnsafeBytes(of: tuple) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
    }
}
```

- [ ] **Step 8: Testy** — Run: `swift test --filter MacHardwareTests` → 10 testů; s připojeným USB diskem 0 přeskočených. Projít `print` výstupy: řadiče, porty a disky musí odpovídat realitě (porovnat se `system_profiler SPUSBDataType`).

Pokud `testUsbVolumesResolveToASocket` selže na „socket is not in the port list“: vypsat `ioreg -p IOService -w0 -r -c IOUSBHostDevice -d 1` a `UsbScanner.scan()` a sjednotit tvar id (`hubId#port`) v `readHub` a `usbLink` — test je správně, oprava patří do skeneru.

- [ ] **Step 9: Ruční ověření s hardwarem** (výsledky do commitu):
  1. USB-C SSD 10 Gb/s v USB-C portu → `DriveLinks.resolve` linka „USB 3 (10 Gb/s a víc)“, zásuvka USB-C.
  2. Stejný disk přes USB 2 kabel / USB 2 hub → linka „USB 2.0 (480 Mb/s)“, `Advisor` varování „… umí USB 3 (5 Gb/s), ale …“.
  3. Čtečka karet za USB 3 hubem/dokem → `hubs` obsahuje hub, zásuvka hubu má label „port N › M“.
  4. Thunderbolt disk (je-li) → `bus == .thunderbolt`, popis „<produkt> — Thunderbolt“.
  5. SMB svazek (je-li) → „//server/share — síťový disk“.

- [ ] **Step 10: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 14 — USB topologie z IORegistry (řadiče, porty, USB 2/3 páry, huby, rychlost linky) a kde sedí svazek (DiskArbitration: sběrnice, produkt, port, UAS, Thunderbolt, síť); ověřeno na <model Macu, macOS>`

Run: `git add -A mac/Package.swift mac/Sources mac/Tests docs && git commit -m "Mac: USB topology via IOKit, volume connection via DiskArbitration"` (do těla commitu zkrácené výpisy z kroku 1).

---

### Task 15: Zrušitelné měření a test HW

**Files:**
- Modify: `mac/Sources/OffloadKit/Hash.swift` (`hashFile(…, isCancelled:)`), `mac/Sources/OffloadKit/Bench.swift` (`read`, `write` zrušitelné)
- Create: `mac/Sources/OffloadKit/Hardware/HwTest.swift`
- Create: `mac/Tests/OffloadKitTests/BenchTests.swift`, `mac/Tests/OffloadKitTests/HwTestTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `DriveLinks.resolve`, `UsbScanner.scan` (úkol 14), `Advisor`, `RoleLink`, `Advice` (úkol 13), `IO.device`, `IO.writeFileDurably`, `ExternalTools.supportDir`.
- Produces:
  - `hashFile(_:chunkSize:buffer:progress:isCancelled:) throws -> (hash: UInt64, size: Int64)` (nový poslední parametr s výchozí hodnotou)
  - `Bench.read(path:limit:blockSizes:isCancelled:)`, `Bench.write(dir:size:blockSize:isCancelled:)` — hází `CancellationError`
  - `public struct DriveMeasurement`, `public struct HwTestResult` (`time, drives, connectors, measurements, advice`), `public final class CancelFlag`
  - `public enum HwTest { defaultPath; roles(cards:destinations:rolesByDrive:); sameVolume(_:_:); allVolumes(); targets(cards:destinations:byDrive:); run(cards:destinations:measure:bytesPerDevice:portNames:rolesByDrive:isCancelled:); advise(drives:connectors:measurements:portNames:); save(_:to:); load(_:) }`

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/BenchTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class BenchTests: XCTestCase {
    func testWriteMeasuresWriteAndReadBackLeavesNothing() throws {
        let dir = tempDir()
        let lines = try Bench.write(dir: dir.path, size: (1 << 20) + 5, blockSize: 64 * 1024)
        XCTAssertEqual(lines.map(\.label), ["write 64 KB", "read  64 KB + xxh64"])
        for l in lines { XCTAssertEqual(l.bytes, (1 << 20) + 5) }
        XCTAssertEqual(TestFS.files(in: dir.path), [])
    }

    func testReadReadsUpToLimitPerBlockSize() throws {
        let dir = tempDir()
        TestFS.writeRandom(dir.path + "/a.bin", size: 300_000, seed: 1)
        TestFS.writeRandom(dir.path + "/b.bin", size: 100_000, seed: 2)
        let lines = try Bench.read(path: dir.path, limit: 350_000, blockSizes: [64 * 1024, 1 << 20])
        XCTAssertEqual(lines.map(\.label), ["read  64 KB", "read  1 MB"])
        for l in lines { XCTAssertTrue((350_000...400_000).contains(l.bytes), "\(l.bytes)") }
    }

    func testWriteAlreadyCancelledThrowsAndLeavesNothing() {
        let dir = tempDir()
        XCTAssertThrowsError(try Bench.write(dir: dir.path, size: 64 << 20, blockSize: 1 << 20, isCancelled: { true })) {
            XCTAssertTrue($0 is CancellationError)
        }
        XCTAssertEqual(TestFS.files(in: dir.path), [])
    }

    // Review focus 5: cancelled in the middle of the temporary file — stops inside it and still deletes it.
    func testWriteCancelledWhileWritingThrowsAndDeletesTempFile() {
        let dir = tempDir()
        var checks = 0
        XCTAssertThrowsError(try Bench.write(dir: dir.path, size: 512 << 20, blockSize: 1 << 20, isCancelled: {
            checks += 1
            return checks > 3
        })) { XCTAssertTrue($0 is CancellationError) }
        XCTAssertEqual(TestFS.files(in: dir.path), [])
    }

    func testReadCancelledThrows() {
        let dir = tempDir()
        TestFS.writeRandom(dir.path + "/a.bin", size: 300_000, seed: 1)
        XCTAssertThrowsError(try Bench.read(path: dir.path, limit: 350_000, blockSizes: [64 * 1024], isCancelled: { true })) {
            XCTAssertTrue($0 is CancellationError)
        }
    }

    func testHashReportsEightPasses() {
        XCTAssertEqual(Bench.hash(size: 1 << 20).bytes, 8 << 20)
    }
}
```

`mac/Tests/OffloadKitTests/HwTestTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class HwTestTests: XCTestCase {
    func testRolesCardsThenDestinations() {
        let r = HwTest.roles(cards: ["/Volumes/E"], destinations: ["/Volumes/D/a", "/Volumes/F"])
        XCTAssertEqual(r.map(\.role), ["Karta", "Cíl 1", "Cíl 2"])
        XCTAssertEqual(r.map(\.root), ["/Volumes/E", "/Volumes/D/a", "/Volumes/F"])
        XCTAssertEqual(HwTest.roles(cards: ["/Volumes/E", "/Volumes/G"], destinations: []).map(\.role), ["Karta 1", "Karta 2"])
    }

    func testRolesByDriveUseTheVolumeName() {
        let r = HwTest.roles(cards: ["/Volumes/G"], destinations: ["/Volumes/offload-test-E", "/Volumes/offload-test-D"], rolesByDrive: true)
        XCTAssertEqual(r.map(\.role), ["Karta", "offload-test-E", "offload-test-D"])
    }

    func testQuickTestNoMeasurementNothingWritten() {
        let dir = tempDir()
        let dest = dir.sub("dest")
        let r = HwTest.run(cards: [dir.sub("card")], destinations: [dest], measure: false)
        XCTAssertEqual(r.drives.count, 2)
        XCTAssertEqual(r.measurements, [])
        XCTAssertEqual(TestFS.files(in: dest), [])
    }

    func testMeasureReadsCard() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        TestFS.writeRandom(card + "/clip.bin", size: 4 << 20, seed: 1)
        let r = HwTest.run(cards: [card], destinations: [], measure: true, bytesPerDevice: 8 << 20)
        XCTAssertEqual(r.measurements.count, 1)
        let m = try XCTUnwrap(r.measurements.first)
        XCTAssertGreaterThan(m.readMBps ?? 0, 0)
        XCTAssertNil(m.writeMBps)
        XCTAssertEqual(TestFS.files(in: card), ["clip.bin"])
        XCTAssertTrue(r.advice.contains { $0.text.hasPrefix("Odhad rychlosti offloadu") })
    }

    func testMeasureWritesDestinationAndCleansUp() throws {
        let dir = tempDir()
        let dest = dir.sub("dest")
        let r = HwTest.run(cards: [], destinations: [dest], measure: true, bytesPerDevice: 8 << 20)
        let m = try XCTUnwrap(r.measurements.first)
        XCTAssertGreaterThan(m.writeMBps ?? 0, 0)
        XCTAssertGreaterThan(m.readMBps ?? 0, 0)
        XCTAssertEqual(TestFS.files(in: dest), [])
    }

    // Review focus 5: a destination on the card's volume is never written to — no measurement, a warning first.
    func testMeasureDestinationOnCardVolumeSkipped() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        TestFS.writeRandom(card + "/clip.bin", size: 4 << 20, seed: 1)
        let dest = dir.sub("dest")
        XCTAssertTrue(HwTest.sameVolume(card, dest))
        let r = HwTest.run(cards: [card], destinations: [dest], measure: true, bytesPerDevice: 8 << 20)
        let m = Dictionary(uniqueKeysWithValues: r.measurements.map { ($0.role, $0) })
        XCTAssertGreaterThan(m["Karta"]?.readMBps ?? 0, 0)
        XCTAssertNil(m["Cíl 1"]?.readMBps)
        XCTAssertNil(m["Cíl 1"]?.writeMBps)
        XCTAssertEqual(TestFS.files(in: dest), [])
        let warnings = r.advice.filter { $0.kind == .warning && $0.text.contains("stejném svazku") }
        XCTAssertEqual(warnings.count, 1)
        XCTAssertTrue(warnings[0].text.hasPrefix("Cíl 1 je na stejném svazku jako Karta"))
        XCTAssertEqual(r.advice.first?.kind, .warning)
    }

    func testMeasureCancelledStopsBeforeNextDevice() {
        let dir = tempDir()
        let dest = dir.sub("dest")
        let r = HwTest.run(cards: [], destinations: [dest], measure: true, bytesPerDevice: 8 << 20, isCancelled: { true })
        XCTAssertEqual(r.measurements, [])
        XCTAssertEqual(TestFS.files(in: dest), [])
    }

    // Review focus 5: cancelling stops inside the 1 GB-style file, deletes it, and skips the remaining devices.
    func testMeasureCancelledDuringWriteStopsAtOnceNoBenchFileLeft() {
        final class Checks { var n = 0 }
        let checks = Checks()
        let dir = tempDir()
        let dest1 = dir.sub("dest1"), dest2 = dir.sub("dest2")
        let r = HwTest.run(cards: [], destinations: [dest1, dest2], measure: true, bytesPerDevice: 512 << 20, isCancelled: {
            checks.n += 1
            return checks.n > 5
        })
        XCTAssertEqual(r.measurements, [])
        XCTAssertEqual(TestFS.files(in: dest1), [])
        XCTAssertEqual(TestFS.files(in: dest2), [])
    }

    func testLoadMissingOrPartialIsNil() throws {
        let dir = tempDir()
        let path = dir.path + "/hwtest.json"
        XCTAssertNil(HwTest.load(path))
        try "{}".write(toFile: path, atomically: true, encoding: .utf8)
        XCTAssertNil(HwTest.load(path))
        try #"{"time":"2026-10-05T10:00:00Z","drives":[],"connectors":[],"measurements":[]}"#.write(toFile: path, atomically: true, encoding: .utf8)
        XCTAssertNil(HwTest.load(path))
    }

    func testSaveLoadRoundTripNestedHubsMeasurementsAndAdvice() throws {
        let dir = tempDir()
        let path = dir.path + "/sub/hwtest.json"
        let inner = UsbHub(instanceId: "loc:14110000", name: "Hub",
                           ports: [UsbPort(number: 1, supportsUsb3: true, userVisible: true, typeC: false, companionPort: 0, device: nil)])
        let dev = UsbDevice(instanceId: "loc:14100000", vid: 0x1234, pid: 0x5678, name: "Disk", link: .superSpeed, capable: .superPlus, hub: inner)
        var conn = UsbConnector(id: "C#1", hubInstanceId: "C", controllerId: "C", controllerName: "USB řadič XHC1", number: 1,
                                usb2Port: 1, usb3Port: 2, supportsUsb3: true, typeC: true, userVisible: true, device: dev, label: "port 1")
        conn.altIds = ["C#2"]
        let link = DriveLink(root: "/Volumes/E", bus: .usb, product: "SSD",
                             usb: UsbLink(controllerId: "C", controllerName: "USB řadič XHC1", connectorId: "C#1", connectorPort: 1, port: 2,
                                          link: .superSpeed, capable: .superPlus, connectorUsb3: nil, uasp: true,
                                          hubs: [UsbHubRef(instanceId: "loc:14100000", name: "Dock")]))
        let time = Date(timeIntervalSince1970: 1_791_201_600)
        let r = HwTestResult(time: time, drives: [RoleLink(role: "Karta", link: link)], connectors: [conn],
                             measurements: [DriveMeasurement(role: "Karta", root: "/Volumes/E", readMBps: 123.5, writeMBps: nil)],
                             advice: [Advice(kind: .info, text: "Ahoj č")])
        try HwTest.save(r, to: path)
        let back = try XCTUnwrap(HwTest.load(path))
        XCTAssertEqual(back.time, time)
        XCTAssertEqual(back.drives, r.drives)
        XCTAssertEqual(back.connectors, r.connectors)
        XCTAssertTrue(back.connectors[0].has("C#2"))
        XCTAssertEqual(back.measurements, r.measurements)
        XCTAssertEqual(back.advice, r.advice)
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "BenchTests|HwTestTests"` → nesestaví se (`extra argument 'isCancelled'`, `cannot find 'HwTest'`).

- [ ] **Step 3: `Hash.swift`** — `hashFile` (celá nová podoba; volající s koncovým uzávěrem `progress` se nemění):

```swift
/// Hashes a whole file straight from the media (no page cache). `isCancelled` is checked before every block.
func hashFile(_ path: String, chunkSize: Int, buffer: UnsafeMutableRawPointer, progress: ((Int) -> Void)? = nil,
              isCancelled: () -> Bool = { false }) throws -> (hash: UInt64, size: Int64) {
    let fd = try IO.openRead(path)
    defer { close(fd) }
    let h = XXH64Stream()
    var offset: Int64 = 0
    while true {
        if isCancelled() { throw CancellationError() }
        let n = try IO.pread(fd, buffer, chunkSize, offset, path: path)
        if n == 0 { break }
        h.update(buffer, n)
        offset += Int64(n)
        progress?(n)
    }
    return (h.digest(), offset)
}
```

- [ ] **Step 4: `Bench.swift`** — nahradit `read` a `write`:

```swift
    /// Sequential read of existing files (largest first) at several block sizes, bypassing the page cache.
    /// `isCancelled`: checked before every block — a cancelled read throws `CancellationError`.
    public static func read(path: String, limit: Int64, blockSizes: [Int], isCancelled: () -> Bool = { false }) throws -> [Line] {
        var targets: [(String, Int64)] = []
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDir) else { throw IOError("open", path, ENOENT) }
        if isDir.boolValue {
            let scan = try Scanner.scan(path)
            targets = scan.files.sorted { $0.size > $1.size }.map { (path + "/" + $0.relPath, $0.size) }
        } else {
            var st = stat()
            stat(path, &st)
            targets = [(path, Int64(st.st_size))]
        }
        guard !targets.isEmpty else { throw OffloadError.preflight("No files to read in \(path)") }

        var lines: [Line] = []
        for bs in blockSizes {
            let buf = IO.allocateAligned(bs)
            defer { free(buf) }
            var total: Int64 = 0
            let t0 = Date()
            outer: for (file, _) in targets {
                let fd = try IO.openRead(file)
                defer { close(fd) }
                var off: Int64 = 0
                while total < limit {
                    if isCancelled() { throw CancellationError() }
                    let n = try IO.pread(fd, buf, bs, off, path: file)
                    if n == 0 { break }
                    off += Int64(n)
                    total += Int64(n)
                    if n < bs { break }
                }
                if total >= limit { break outer }
            }
            lines.append(Line(label: "read  \(blockLabel(bs))", bytes: total, seconds: Date().timeIntervalSince(t0)))
        }
        return lines
    }

    /// Writes a temporary file (no cache, flushed to the media), reads it back, deletes it — also when cancelled.
    /// `isCancelled`: checked before every block of the write and of the read-back (`CancellationError`).
    public static func write(dir: String, size: Int64, blockSize: Int, isCancelled: () -> Bool = { false }) throws -> [Line] {
        if isCancelled() { throw CancellationError() }
        let path = dir + "/.offload-bench-\(getpid())"
        defer { unlink(path) }
        let buf = IO.allocateAligned(blockSize)
        defer { free(buf) }
        arc4random_buf(buf, blockSize)

        let t0 = Date()
        let wfd = try IO.openWrite(path)
        do {
            IO.preallocate(wfd, size)
            var off: Int64 = 0
            while off < size {
                if isCancelled() { throw CancellationError() }
                let n = Int(min(Int64(blockSize), size - off))
                try IO.pwrite(wfd, buf, n, off, path: path)
                off += Int64(n)
            }
            try IO.fullSync(wfd, path: path)
            close(wfd)
        } catch {
            close(wfd)
            throw error
        }
        let w = Line(label: "write \(blockLabel(blockSize))", bytes: size, seconds: Date().timeIntervalSince(t0))

        let t1 = Date()
        let r = try hashFile(path, chunkSize: blockSize, buffer: buf, isCancelled: isCancelled)
        let rl = Line(label: "read  \(blockLabel(blockSize)) + xxh64", bytes: r.size, seconds: Date().timeIntervalSince(t1))
        return [w, rl]
    }
```

- [ ] **Step 5: `mac/Sources/OffloadKit/Hardware/HwTest.swift`**:

```swift
import Foundation

public struct DriveMeasurement: Codable, Equatable {
    public let role: String
    public let root: String
    public let readMBps: Double?
    public let writeMBps: Double?
}

public struct HwTestResult: Codable {
    public let time: Date
    public let drives: [RoleLink]
    public let connectors: [UsbConnector]
    public let measurements: [DriveMeasurement]
    public let advice: [Advice]
}

/// A thread-safe stop flag shared by the UI or a signal handler and a running test.
public final class CancelFlag: @unchecked Sendable {
    private let lock = NSLock()
    private var value = false

    public init() {}

    public func cancel() {
        lock.lock(); value = true; lock.unlock()
    }

    public var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return value }
}

/// The hardware check (spec 8a.2): topology and links in a moment; with `measure`, a short read of each card and
/// write + read of each destination (a temporary file, deleted). Nothing is ever written to a card.
public enum HwTest {
    public static var defaultPath: String { ExternalTools.supportDir + "/hwtest.json" }

    static func roles(cards: [String], destinations: [String], rolesByDrive: Bool = false) -> [(role: String, root: String)] {
        var list: [(role: String, root: String)] = cards.enumerated().map { (cards.count == 1 ? "Karta" : "Karta \($0.offset + 1)", $0.element) }
        list += destinations.enumerated().map { (rolesByDrive ? volumeName($0.element) : "Cíl \($0.offset + 1)", $0.element) }
        return list
    }

    static func volumeName(_ path: String) -> String {
        if let name = (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeNameKey]))?.volumeName, !name.isEmpty,
           (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeURLKey]))?.volume?.path == path {
            return name
        }
        return PortLabels.volumeLabel(path)
    }

    static func isCard(_ role: String) -> Bool { role.hasPrefix("Karta") }

    /// Whether two paths are on the same volume (unknown → false).
    public static func sameVolume(_ a: String, _ b: String) -> Bool {
        guard let da = IO.device(of: a), let db = IO.device(of: b) else { return false }
        return da == db
    }

    /// "/" and every volume under /Volumes — the drives listed when no card and no destination is given.
    public static func allVolumes() -> [String] {
        (FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: [.skipHiddenVolumes]) ?? [])
            .map(\.path).filter { $0 == "/" || $0.hasPrefix("/Volumes/") }
    }

    /// Off the main thread: the destinations that exist; with neither cards nor destinations, every volume (byDrive).
    public static func targets(cards: [String], destinations: [String], byDrive: Bool) -> (destinations: [String], byDrive: Bool) {
        let dests = destinations.filter { var d: ObjCBool = false; return FileManager.default.fileExists(atPath: $0, isDirectory: &d) && d.boolValue }
        if !byDrive && cards.count + dests.count > 0 { return (dests, false) }
        return (allVolumes(), true)
    }

    // MB/s, or nil when the timing is unusable (zero / not finite).
    static func mbps(_ line: Bench.Line) -> Double? {
        line.seconds > 0 && line.seconds.isFinite ? Double(line.bytes) / line.seconds / 1e6 : nil
    }

    /// `isCancelled` stops the measurement at once — inside the device being measured too (its temporary file is
    /// deleted, it counts as not measured) — and the rest are skipped. A destination on a card's volume is never
    /// written to (no measurement, a warning).
    public static func run(cards: [String], destinations: [String], measure: Bool, bytesPerDevice: Int64 = 1_000_000_000,
                           portNames: [String: String]? = nil, rolesByDrive: Bool = false,
                           isCancelled: @escaping () -> Bool = { false }) -> HwTestResult {
        let list = HwTest.roles(cards: cards, destinations: destinations, rolesByDrive: rolesByDrive)
        let drives = list.map { RoleLink(role: $0.role, link: DriveLinks.resolve($0.root)) }
        let connectors = UsbScanner.scan().connectors
        let cardRoots = list.filter { isCard($0.role) }.map(\.root)
        let onCard = Set(list.filter { r in !isCard(r.role) && cardRoots.contains { sameVolume($0, r.root) } }.map(\.role))

        var measurements: [DriveMeasurement] = []
        if measure {
            measuring: for r in list {
                if isCancelled() { break }
                do {
                    if isCard(r.role) {
                        let line = try Bench.read(path: r.root, limit: bytesPerDevice, blockSizes: [16 << 20], isCancelled: isCancelled)[0]
                        measurements.append(DriveMeasurement(role: r.role, root: r.root, readMBps: mbps(line), writeMBps: nil))
                    } else if onCard.contains(r.role) {
                        measurements.append(DriveMeasurement(role: r.role, root: r.root, readMBps: nil, writeMBps: nil))   // never write to a card
                    } else {
                        let lines = try Bench.write(dir: r.root, size: bytesPerDevice, blockSize: 16 << 20, isCancelled: isCancelled)
                        measurements.append(DriveMeasurement(role: r.role, root: r.root, readMBps: mbps(lines[1]), writeMBps: mbps(lines[0])))
                    }
                } catch is CancellationError {
                    break measuring   // interrupted: not measured, the rest skipped
                } catch {
                    measurements.append(DriveMeasurement(role: r.role, root: r.root, readMBps: nil, writeMBps: nil))
                }
            }
        }
        return HwTestResult(time: Date(), drives: drives, connectors: connectors, measurements: measurements,
                            advice: advise(drives: drives, connectors: connectors, measurements: measurements, portNames: portNames))
    }

    /// The advice for a test result: a destination on a card's volume, then the Advisor's advice with the measured
    /// pace (cards read, destinations written). The app calls it again after a port is renamed.
    public static func advise(drives: [RoleLink], connectors: [UsbConnector], measurements: [DriveMeasurement],
                              portNames: [String: String]?) -> [Advice] {
        let cards = drives.filter(\.isCard)
        var advice: [Advice] = []
        for d in drives where !d.isCard {
            if let card = cards.first(where: { sameVolume($0.link.root, d.link.root) }) {
                advice.append(Advice(kind: .warning,
                                     text: "\(d.role) je na stejném svazku jako \(card.role) — na kartu se nezapisuje, zápis se neměří; zvol jiný cílový disk."))
            }
        }
        var pace: [String: Double] = [:]
        for m in measurements {
            if let v = isCard(m.role) ? m.readMBps : m.writeMBps, v > 0 { pace[m.role] = v }
        }
        advice += Advisor.advise(drives, connectors: connectors, measuredMBps: pace.isEmpty ? nil : pace, portNames: portNames)
        return Advisor.stable(advice)
    }

    /// Atomic (temporary file + rename).
    public static func save(_ result: HwTestResult, to path: String) throws {
        try IO.mkdirs((path as NSString).deletingLastPathComponent)
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        enc.dateEncodingStrategy = .iso8601
        try IO.writeFileDurably(path, try enc.encode(result))
    }

    /// nil for a missing, partial or foreign file (never hands out half a result).
    public static func load(_ path: String) -> HwTestResult? {
        guard let data = FileManager.default.contents(atPath: path) else { return nil }
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        return try? dec.decode(HwTestResult.self, from: data)
    }
}
```

Pozn.: `sameVolume($0.link.root, d.link.root)` porovnává kořeny svazků z `DriveLinks.resolve` (`f_mntonname`) — u dočasných složek testů jsou oba na systémovém datovém svazku, takže test „cíl na svazku karty“ funguje bez skutečné karty.

- [ ] **Step 6: Testy projdou** — Run: `swift test --filter "BenchTests|HwTestTests"` → 6 + 10 testů, 0 chyb; pak `swift test`.

- [ ] **Step 7: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 15 — test HW: role, rychlý test, měření (karta jen čtení, cíl zápis + čtení 1 GB), cíl na svazku karty se neměří, zrušení uprostřed smaže dočasný soubor, výsledek atomicky v hwtest.json`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: cancellable bench and the hardware test (roles, measurement, advice, saved result)"`

---

### Task 16: CLI — `offload ports` a „brzdí“ ve stavovém řádku kopie

**Files:**
- Modify: `mac/Sources/offload/main.swift` (`Offload.configuration`, `Copy.run`, nový `Ports`, `ProgressDisplay`)
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `HwTest`, `CancelFlag` (úkol 15), `DriveLinks.resolve`, `UsbScanner.scan` (úkol 14), `Advisor`, `RoleLink`, `ChainView.linkCeiling`, `PortLabels.listRow` (úkol 13), `BrakeMeter` (úkol 8).
- Produces: subcommand `offload ports [--measure] [--card X]… [--dest Y]… [--gb N]`; `ProgressDisplay(job:chain:)`, `ProgressDisplay.shortName(_:)`.

- [ ] **Step 1: Test-first** — logika `ports` a brzdy je pokrytá testy úkolů 8, 13, 15. CLI se ověřuje ručně (krok 4). Run: `swift test` → zelené.

- [ ] **Step 2: `main.swift`** — v `Offload.configuration` přidat podpříkaz:

```swift
        subcommands: [Copy.self, Verify.self, Bench.self, Ports.self],
```

V `Copy.run()` nahradit úsek od `fflush(stdout)` po `display.start()`:

```swift
        // Connection chain: informational only — resolving it never fails the copy.
        var chain = [RoleLink(role: "Karta", link: DriveLinks.resolve(plan.source))]
        chain += plan.destRoots.enumerated().map { RoleLink(role: "Cíl \($0.offset + 1)", link: DriveLinks.resolve($0.element)) }
        let connectors = UsbScanner.scan().connectors
        // Status-line names first ("card", "D1"), so "brzdí: D1" there reads against this list.
        print("  Připojení:")
        for (i, l) in chain.enumerated() { print("    \(ProgressDisplay.shortName(i)) = \(l.role): \(l.link.describe(connectors))") }
        for a in Advisor.advise(chain, connectors: connectors) where a.kind == .warning { print("  ! \(a.text)") }
        fflush(stdout)
        if dryRun { return }

        signal(SIGINT, SIG_IGN)
        let sigint = DispatchSource.makeSignalSource(signal: SIGINT, queue: .global())
        sigint.setEventHandler {
            fputs("\nCancelling — finishing current block, partial files will be removed…\n", stderr)
            job.cancel()
        }
        sigint.resume()

        let display = ProgressDisplay(job: job, chain: chain)
        display.start()
```

Za `struct Bench` přidat:

```swift
struct Ports: ParsableCommand {
    static let configuration = CommandConfiguration(
        abstract: "Show USB ports, where cards and disks are connected and how fast, with advice.")

    @Flag(help: "Also measure card read and destination write/read speed (temporary file, deleted).")
    var measure = false

    @Option(help: "Card or reader to check (repeatable).")
    var card: [String] = []

    @Option(help: "Destination to check (repeatable).")
    var dest: [String] = []

    @Option(help: "Amount of data per device when measuring, in GB.")
    var gb: Double = 1

    func run() throws {
        let cards = card.map(abs)
        var dests = dest.map(abs)
        for d in dests {
            if let c = cards.first(where: { HwTest.sameVolume($0, d) }) {
                fputs("Error: cíl \(d) je na stejném svazku jako karta \(c) — na kartu se nikdy nezapisuje\n", stderr)
                throw ExitCode(1)
            }
        }
        var byDrive = false
        if cards.isEmpty && dests.isEmpty {
            // No roles given: every volume, labelled by its name.
            if measure {
                fputs("Error: --measure needs --card and/or --dest\n", stderr)
                throw ExitCode(1)
            }
            dests = HwTest.allVolumes()
            byDrive = true
        }
        let flag = CancelFlag()
        signal(SIGINT, SIG_IGN)
        let sigint = DispatchSource.makeSignalSource(signal: SIGINT, queue: .global())
        sigint.setEventHandler {
            // Stops the measurement at once; the process stays until the temporary file is deleted.
            guard !flag.isCancelled else { return }
            fputs("\nPřerušeno — zastavuji měření a mažu dočasný soubor…\n", stderr)
            flag.cancel()
        }
        sigint.resume()
        let result = HwTest.run(cards: cards, destinations: dests, measure: measure, bytesPerDevice: Int64(gb * 1_000_000_000),
                                rolesByDrive: byDrive, isCancelled: { flag.isCancelled })
        printResult(result)
        if flag.isCancelled {
            print("\nMěření přerušeno — výsledek se neukládá.")
            throw ExitCode(1)
        }
        do {
            try HwTest.save(result, to: HwTest.defaultPath)
        } catch {
            fputs("Varování: výsledek testu nešlo uložit: \(error)\n", stderr)
        }
    }

    private func printResult(_ r: HwTestResult) {
        func pad(_ s: String, _ n: Int) -> String { s.count >= n ? s : s + String(repeating: " ", count: n - s.count) }
        print("Připojení:")
        for d in r.drives {
            // Listed by volume: the role is the volume name — no second column repeating it.
            let name = d.link.root == "/" ? "/" : (d.link.root as NSString).lastPathComponent
            let whereText = d.role == name ? pad(d.role, 8) : pad(d.role, 8) + " " + pad(d.link.root, 14)
            print("  \(whereText) \(d.link.describe(r.connectors))")
        }
        var groups: [(id: String, name: String, ports: [UsbConnector])] = []
        for c in r.connectors where c.userVisible {
            if let i = groups.firstIndex(where: { $0.id == c.controllerId }) {
                groups[i].ports.append(c)
            } else {
                groups.append((c.controllerId, c.controllerName, [c]))
            }
        }
        for g in groups {
            print("\nUSB porty (\(g.name)):")
            for c in g.ports { print("  \(PortLabels.listRow(c, drives: r.drives))") }
        }
        if !r.measurements.isEmpty {
            print("\nMěření:")
            for m in r.measurements { print("  \(pad(m.role, 8)) \(pad(m.root, 14)) čtení \(mb(m.readMBps))  zápis \(mb(m.writeMBps))") }
        }
        print("\nDoporučení:")
        for a in r.advice { print("  \(a.kind == .warning ? "!" : a.kind == .info ? "i" : "·") \(a.text)") }
    }

    private func mb(_ v: Double?) -> String { v.map { String(format: "%.0f MB/s", $0) } ?? "—" }
}
```

`ProgressDisplay` — celá nová podoba (brzda podle zásoby bloků, **před** názvem souboru):

```swift
/// One status line on stderr, refreshed twice a second.
final class ProgressDisplay {
    let job: OffloadJob
    private let timer = DispatchSource.makeTimerSource(queue: .global())
    private let brakes: BrakeMeter
    private let started = Date()
    private var lastTime = Date()
    private var lastRead: Int64 = 0
    private var lastWritten: [Int64] = []
    private var lastVerified: [Int64] = []
    private var lastPhase = ""

    /// chain: the drives in the order card, D1, D2 … (for the "linka plná" check).
    init(job: OffloadJob, chain: [RoleLink]? = nil) {
        self.job = job
        brakes = BrakeMeter(chunkBytes: Int64(job.config.chunkMB) << 20, bufferCount: job.config.buffers,
                            linkMBps: chain?.map(ChainView.linkCeiling) ?? [])
    }

    /// The status-line name of a chain element: "card", "D1", "D2" …
    static func shortName(_ element: Int) -> String { element == 0 ? "card" : "D\(element)" }

    func start() {
        timer.schedule(deadline: .now() + 0.5, repeating: 0.5)
        timer.setEventHandler { [weak self] in self?.tick() }
        timer.resume()
    }

    func stop() {
        timer.cancel()
        fputs("\r\u{1B}[K", stderr)
    }

    /// Which element sets the pace — decided from the backlog (BrakeMeter), not from the rates on this line.
    private func brake(_ s: ProgressSnapshot) -> String {
        guard let b = brakes.add(seconds: Date().timeIntervalSince(started), readBytes: s.readBytes, writtenBytes: s.writtenBytes) else { return "" }
        return "brzdí: \(ProgressDisplay.shortName(b.element))\(b.linkFull ? " (linka plná)" : "")  "
    }

    private func tick() {
        let st = job.status()
        let now = Date()
        let dt = max(0.001, now.timeIntervalSince(lastTime))
        if st.phase.rawValue != lastPhase {
            if !lastPhase.isEmpty { fputs("\n", stderr) }
            lastPhase = st.phase.rawValue
        }
        var line: String
        if let s = st.copy, [.copying, .verifying, .flushing, .reporting].contains(st.phase) {
            if lastWritten.count != s.writtenBytes.count {
                lastWritten = s.writtenBytes
                lastVerified = s.verifiedBytes
            }
            let rate = { (now: Int64, before: Int64) in String(format: "%4.0f", Double(now - before) / dt / 1_000_000) }
            switch st.phase {
            case .copying:
                let pct = s.totalBytes > 0 ? Double(s.readBytes) / Double(s.totalBytes) * 100 : 0
                let dests = s.writtenBytes.indices.map { "D\($0 + 1) \(rate(s.writtenBytes[$0], lastWritten[$0]))" }.joined(separator: "  ")
                line = String(format: "copy %d/%d  %@ / %@  %3.0f%%  card %@ MB/s  %@ MB/s",
                              s.fileIndex + 1, s.totalFiles, formatBytes(s.readBytes), formatBytes(s.totalBytes), pct,
                              rate(s.readBytes, lastRead), dests)
                // The brake goes before the file name: a narrow terminal cuts the end of the line.
                line += "  " + brake(s) + (s.currentFile as NSString).lastPathComponent
            case .verifying:
                line = "verify  " + s.verifiedBytes.indices.map {
                    String(format: "D%d %3.0f%% %@ MB/s", $0 + 1, Double(s.verifiedBytes[$0]) / Double(max(s.totalBytes, 1)) * 100,
                           rate(s.verifiedBytes[$0], lastVerified[$0]))
                }.joined(separator: "  ")
            default:
                line = st.phase.rawValue + "…"
            }
            if s.errors > 0 { line += "  ERRORS: \(s.errors)" }
            lastRead = s.readBytes
            lastWritten = s.writtenBytes
            lastVerified = s.verifiedBytes
        } else if st.phase == .analyzing || st.phase == .proxies {
            line = String(format: "%@ %d/%d  %3.0f%%  %@", st.phase.rawValue, st.clipIndex + 1, st.clipCount, st.clipFraction * 100, st.clipName)
            if st.safeToEject { line += "  (card safe to eject)" }
        } else {
            line = st.phase.rawValue
        }
        fputs("\r\u{1B}[K" + line, stderr)
        lastTime = now
    }
}
```

- [ ] **Step 3: Build** — Run: `swift build -c release`.

- [ ] **Step 4: Ruční ověření**
  1. `.build/release/offload ports` → „Připojení:“ (všechny svazky), „USB porty (USB řadič …):“ s řádky `port N USB 3 …`, „Doporučení:“. Exit 0, vznikne `~/Library/Application Support/Offload/hwtest.json`.
  2. `.build/release/offload ports --measure` → `Error: --measure needs --card and/or --dest`, exit 1.
  3. `.build/release/offload ports --card /Volumes/<karta> --dest /Volumes/<karta>/x` → chyba „cíl … je na stejném svazku jako karta …“, exit 1.
  4. `.build/release/offload ports --card /Volumes/<karta> --dest /Volumes/<SSD> --measure --gb 2`, během měření SSD Ctrl+C → „Přerušeno — zastavuji měření a mažu dočasný soubor…“, výpis, „Měření přerušeno — výsledek se neukládá.“, exit 1; `ls -a /Volumes/<SSD> | grep offload-bench` nic.
  5. `.build/release/offload /Volumes/<karta> -d /Volumes/<SSD> -d /Volumes/<HDD>` → před kopií „  Připojení:“ s `card = Karta: …`, `D1 = Cíl 1: …`; ve stavovém řádku po ~2 s „brzdí: D2  “ (pomalejší HDD) **před** názvem souboru.

- [ ] **Step 5: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 16 — CLI: offload ports [--measure] (připojení, USB porty, měření, doporučení; Ctrl+C smaže dočasný soubor), řetěz připojení před kopií a „brzdí: Dn“ ve stavovém řádku`

`docs/ROZDILY.md` (pravidlo z Global Constraints — rozdíl, který tento úkol zavírá, se upraví hned): řádek
„Test USB portů na vyžádání + doporučení“ Mac `🔨 CLI (offload ports), GUI ⏳`; řádek „Propustnost článků HW při kopii“
Mac `🔨 CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku, GUI ⏳`; v „Záměrných rozdílech“ odrážku „HW diagnostika …
zatím jen na Windows“ přepsat na „HW diagnostika: na Macu zatím jen CLI, GUI v úkolu 17“.

Run: `git add -A mac/Sources docs && git commit -m "Mac: offload ports and the brake in the copy status line"`

---

### Task 17: GUI — okno Hardware (⌘U), sekce Hardware, řetěz na kartě úlohy

**Files:**
- Create: `mac/Sources/OffloadApp/HardwareModel.swift`, `mac/Sources/OffloadApp/HardwareView.swift`
- Modify: `mac/Sources/OffloadApp/OffloadApp.swift` (scéna `Window`, `CommandMenu("Nástroje")`, `HardwareMenuItem`)
- Modify: `mac/Sources/OffloadApp/AppModel.swift` (`JobItem` řetěz; `AppModel.hardware`, `hardwareRoles()`, `tick`)
- Modify: `mac/Sources/OffloadApp/Views.swift` (`SetupPanel` + `HardwareSection`, `JobCard` řetěz)
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `HwTest`, `HwTestResult`, `CancelFlag` (úkol 15), `ChainView`, `RoleLink`, `PortLabels`, `UsbConnector`, `AdviceKind` (úkoly 12–13), `DriveLinks.resolve`, `UsbScanner.scan` (úkol 14), `BrakeMeter` (úkol 8), `QueueRules.measureBlocked` (úkol 9), `AppModel.copyRunning` (úkol 11).
- Produces:
  - `@MainActor final class HardwareModel: ObservableObject` (`drives`, `advice`, `measurements`, `portGroups`, `busy`, `status`, `lastTest`, `showInternal`, `askMeasure`, `summary`, `measureHint`, `canMeasure`, `canStop`, `measureQuestion`, `windowOpened()`, `windowClosed()`, `run(measure:)`, `stop()`, `stopAndWait(timeout:)`, `portName(_:)`, `rename(_:_:)`)
  - `AppModel.hardware: HardwareModel` (vytvoří se při prvním použití), `AppModel.waitForHardware()`, `AppDelegate.applicationWillTerminate`, `AppModel.hardwareRoles() -> (cards: [String], destinations: [String], byDrive: Bool)`
  - `JobItem.chainLines: [String]`, `JobItem.resolveChainIfNeeded(portNames:)`
  - SwiftUI: `HardwareView`, `HardwareMenuItem`, `HardwareSection`, `PortNameField`

- [ ] **Step 1: Test-first** — logika okna je v OffloadKit a testovaná (`HwTestTests`, `AdvisorTests`, `ChainViewTests`, `QueueRulesTests.testMeasureBlocked…`). GUI se ověřuje ručně (krok 7). Run: `swift test` → zelené.

- [ ] **Step 2: `mac/Sources/OffloadApp/HardwareModel.swift`**:

```swift
import AppKit
import OffloadKit
import SwiftUI

/// The Hardware window (spec 8a.2, on demand): where the cards and disks are connected, advice, optional measurement,
/// the USB ports with user names. Tests run on one serial queue — one at a time; nothing is ever written to a card.
@MainActor
final class HardwareModel: ObservableObject {
    struct DriveRow: Identifiable, Equatable {
        var id: String { role + "|" + root }
        let role: String
        let root: String
        let description: String
    }

    struct AdviceRow: Identifiable, Equatable {
        let id: Int
        let kind: AdviceKind
        let text: String
    }

    struct MeasureRow: Identifiable, Equatable {
        var id: String { role }
        let role: String
        let root: String
        let read: String
        let write: String
    }

    struct PortRow: Identifiable {
        var id: String { connector.id }
        let connector: UsbConnector
        let label: String
        let device: String
        let drives: String
    }

    struct PortGroup: Identifiable {
        var id: String { controllerId }
        let controllerId: String
        let controller: String
        var ports: [PortRow]
    }

    @Published private(set) var drives: [DriveRow] = []
    @Published private(set) var advice: [AdviceRow] = []
    @Published private(set) var measurements: [MeasureRow] = []
    @Published private(set) var portGroups: [PortGroup] = []
    @Published private(set) var busy = false
    @Published private(set) var stopping = false
    @Published private(set) var status = ""
    @Published private(set) var lastTest: HwTestResult?
    @Published var showInternal = false { didSet { buildPorts() } }
    @Published var askMeasure = false

    private unowned let app: AppModel
    private var result: HwTestResult?
    private var byDrive = false
    private var savedMeasured = false
    private var flag: CancelFlag?
    /// The window was reopened while the previous run was still stopping: a quick test follows once it has ended.
    private var rerunQuick = false
    private let queue = DispatchQueue(label: "app.hwtest", qos: .userInitiated)

    init(app: AppModel) {
        self.app = app
        queue.async { [weak self] in
            let saved = HwTest.load(HwTest.defaultPath)
            DispatchQueue.main.async {
                self?.lastTest = saved
                self?.savedMeasured = !(saved?.measurements.isEmpty ?? true)
            }
        }
    }

    /// The Hardware section: "Poslední test 5. 10. 2026 14:03 · bez varování" / "Zatím neproběhl".
    var summary: String {
        guard let r = lastTest else { return "Zatím neproběhl" }
        let f = DateFormatter()
        f.locale = Locale(identifier: "cs_CZ")
        f.dateFormat = "d. M. yyyy H:mm"
        return "Poslední test \(f.string(from: r.time)) · \(Texts.warnings(r.advice.filter { $0.kind == .warning }.count))"
    }

    var measureHint: String {
        if byDrive { return "Pro měření vyberte kartu nebo přidejte cíl." }
        if app.copyRunning { return "Během kopírování nelze měřit — měření zapisuje 1 GB na cílové disky." }
        return ""
    }

    var canMeasure: Bool { !busy && !byDrive && result != nil && !app.copyRunning }
    var canStop: Bool { busy && !stopping }

    var measureQuestion: String {
        let dests = app.hardwareRoles().destinations
        let list = dests.isEmpty ? "   (žádné)" : dests.map { "   " + $0 }.joined(separator: "\n")
        return "Z karet se jen čte (až 1 GB z každé).\nNa cílové disky se zapíše a přečte dočasný soubor 1 GB a hned se smaže:\n\(list)\n\nPodle rychlosti disků to může trvat několik minut."
    }

    /// The window opened (menu Nástroje › Prověřit USB porty…, ⌘U, or the Hardware section): a quick test.
    func windowOpened() {
        if busy {
            // Like Windows (one HW run at a time): the previous run (of a closed window) is still finishing.
            rerunQuick = true
            status = "Čekám, až doběhne předchozí test…"
        } else {
            run(measure: false)
        }
    }

    /// The window closed: the run stops (a measurement deletes its temporary file).
    func windowClosed() { stop() }

    func run(measure: Bool) {
        guard !busy else { return }
        if measure && app.copyRunning { return }   // a copy may have started while the question was open
        let roles = app.hardwareRoles()
        let names = app.settings.portNames
        let flag = CancelFlag()
        self.flag = flag
        busy = true
        let running = measure ? "Měřím rychlost… (může trvat několik minut)" : "Zjišťuji připojení…"
        status = running
        queue.async { [weak self] in
            let (targets, all) = HwTest.targets(cards: roles.cards, destinations: roles.destinations, byDrive: roles.byDrive)
            // Never measure by volume: every disk would get a 1 GB file (the button is off then anyway).
            let r = HwTest.run(cards: roles.cards, destinations: targets, measure: measure && !all, portNames: names,
                               rolesByDrive: all, isCancelled: { flag.isCancelled })
            DispatchQueue.main.async { self?.finished(r, byDrive: all, measured: measure, cancelled: flag.isCancelled) }
        }
    }

    /// Zastavit, or the window closed: stops at once; "Zastavuji…" until the run has really ended.
    func stop() {
        guard busy, !stopping, let flag else { return }
        stopping = true
        rerunQuick = false
        flag.cancel()
        status = "Zastavuji…"
    }

    /// On quit (mirrors Windows `MainViewModel.WaitForHardware`): stop a measurement and wait — at most `timeout` s —
    /// until the hardware queue is idle, so `Bench.write` deletes its 1 GB temporary file before the process exits.
    /// Cancelling is checked per 16 MB block, so this is quick; the bound only guards against a hung disk.
    func stopAndWait(timeout: TimeInterval = 10) {
        flag?.cancel()
        rerunQuick = false
        let idle = DispatchSemaphore(value: 0)
        queue.async { idle.signal() }   // runs after the current test has returned (temporary file deleted)
        if idle.wait(timeout: .now() + timeout) == .timedOut { NSLog("%@", "hwtest: still running at exit") }
    }

    private func finished(_ r: HwTestResult, byDrive: Bool, measured: Bool, cancelled: Bool) {
        defer {
            if rerunQuick {
                rerunQuick = false
                run(measure: false)
            }
        }
        busy = false
        stopping = false
        result = r
        self.byDrive = byDrive
        status = cancelled && measured ? "Měření přerušeno — zobrazeno, co se stihlo změřit." : ""
        rebuild()
        guard !cancelled else { return }
        lastTest = r
        // A quick test never replaces a saved measurement on disk (only the summary shows it).
        let hasMeasurement = !r.measurements.isEmpty
        if !hasMeasurement && savedMeasured { return }
        savedMeasured = hasMeasurement
        queue.async {
            do { try HwTest.save(r, to: HwTest.defaultPath) } catch { NSLog("%@", "hwtest save: \(error)") }
        }
    }

    private func rebuild() {
        guard let r = result else { return }
        measurements = r.measurements.map { MeasureRow(role: $0.role, root: $0.root, read: mb($0.readMBps), write: mb($0.writeMBps)) }
        rebuildNamed()
        buildPorts()
    }

    /// What shows port names: the drive lines and the advice.
    private func rebuildNamed() {
        guard let r = result else { return }
        let names = app.settings.portNames
        drives = r.drives.map { DriveRow(role: $0.role, root: $0.link.root, description: $0.link.describe(r.connectors, names)) }
        advice = HwTest.advise(drives: r.drives, connectors: r.connectors, measurements: r.measurements, portNames: names)
            .enumerated().map { AdviceRow(id: $0.offset, kind: $0.element.kind, text: $0.element.text) }
    }

    private func buildPorts() {
        guard let r = result else {
            portGroups = []
            return
        }
        var groups: [PortGroup] = []
        for c in r.connectors where c.userVisible || showInternal {
            let device = c.device.map { "\($0.name) — \(UsbSpeeds.label($0.link))" } ?? "volný"
            let here = r.drives.filter { d in d.link.usb.map { c.has($0.connectorId) } ?? false }.map(\.role).joined(separator: ", ")
            let row = PortRow(connector: c, label: c.defaultLabel, device: device, drives: here)
            if let i = groups.firstIndex(where: { $0.controllerId == c.controllerId }) {
                groups[i].ports.append(row)
            } else {
                groups.append(PortGroup(controllerId: c.controllerId, controller: PortLabels.shortController(c.controllerName), ports: [row]))
            }
        }
        portGroups = groups
    }

    /// The user's name of a socket (its id, then the other half of a USB 3 hub).
    func portName(_ c: UsbConnector) -> String {
        let names = app.settings.portNames
        return ([c.id] + c.altIds).compactMap { names[$0] }.first { !$0.trimmingCharacters(in: .whitespaces).isEmpty } ?? ""
    }

    /// One name per socket, saved under its id; the drive lines and advice follow.
    func rename(_ c: UsbConnector, _ name: String) {
        var names = app.settings.portNames
        for alt in c.altIds { names[alt] = nil }
        let trimmed = name.trimmingCharacters(in: .whitespaces)
        names[c.id] = trimmed.isEmpty ? nil : trimmed
        if names != app.settings.portNames { app.settings.portNames = names }
        rebuildNamed()
    }

    private func mb(_ v: Double?) -> String { v.map { String(format: "%.0f MB/s", $0) } ?? "—" }
}
```

- [ ] **Step 3: `mac/Sources/OffloadApp/HardwareView.swift`**:

```swift
import AppKit
import OffloadKit
import SwiftUI

struct HardwareView: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject var hw: HardwareModel

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    section("Připojení") {
                        ForEach(hw.drives) { d in
                            HStack(alignment: .firstTextBaseline, spacing: 10) {
                                Text(d.role).fontWeight(.medium).frame(width: 110, alignment: .leading)
                                Text(d.root).font(.caption.monospaced()).foregroundStyle(.secondary)
                                    .lineLimit(1).truncationMode(.middle).frame(width: 160, alignment: .leading)
                                Text(d.description).textSelection(.enabled)
                            }
                        }
                    }
                    section("Doporučení") {
                        ForEach(hw.advice) { a in
                            Label(a.text, systemImage: icon(a.kind)).foregroundStyle(color(a.kind)).textSelection(.enabled)
                        }
                    }
                    section("Měření") {
                        HStack {
                            Button("Změřit rychlost…") { hw.askMeasure = true }.disabled(!hw.canMeasure)
                            Text(hw.measureHint).font(.caption).foregroundStyle(.secondary)
                        }
                        ForEach(hw.measurements) { m in
                            HStack(spacing: 10) {
                                Text(m.role).frame(width: 110, alignment: .leading)
                                Text(m.root).font(.caption.monospaced()).foregroundStyle(.secondary)
                                    .lineLimit(1).truncationMode(.middle).frame(width: 160, alignment: .leading).help(m.root)
                                Text("čtení \(m.read)").monospacedDigit()
                                Text("zápis \(m.write)").monospacedDigit()
                            }
                        }
                    }
                    section("USB porty") {
                        Toggle("Zobrazit interní porty", isOn: $hw.showInternal)
                        Text("macOS neví, kde je port na počítači fyzicky — pojmenujte si ho (např. „přední levý“), název se pak použije všude.")
                            .font(.caption).foregroundStyle(.secondary)
                        ForEach(hw.portGroups) { g in
                            Text(g.controller).font(.subheadline.weight(.semibold)).padding(.top, 6)
                            ForEach(g.ports) { p in
                                HStack(spacing: 10) {
                                    Text(p.label).frame(width: 210, alignment: .leading)
                                    Text(p.device).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading)
                                    Text(p.drives).foregroundStyle(.secondary)
                                    PortNameField(hw: hw, connector: p.connector).frame(width: 180)
                                }
                                .font(.callout)
                            }
                        }
                    }
                }
                .padding(20)
            }
            Divider()
            HStack {
                Text(hw.status).foregroundStyle(.secondary).lineLimit(2)
                Spacer()
                if hw.busy { Button("Zastavit") { hw.stop() }.disabled(!hw.canStop) }
                Button("Prověřit znovu") { hw.run(measure: false) }.disabled(hw.busy)
                Button("Zavřít") { NSApp.keyWindow?.performClose(nil) }
            }
            .padding(14)
        }
        .frame(minWidth: 680, minHeight: 480)
        .onAppear { hw.windowOpened() }
        .onDisappear { hw.windowClosed() }
        .alert("Změřit rychlost?", isPresented: $hw.askMeasure) {
            Button("Změřit") { hw.run(measure: true) }
            Button("Zrušit", role: .cancel) {}
        } message: {
            Text(hw.measureQuestion)
        }
    }

    @ViewBuilder
    private func section<Content: View>(_ title: String, @ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(.headline)
            content()
        }
    }

    private func icon(_ kind: AdviceKind) -> String {
        switch kind {
        case .warning: return "exclamationmark.triangle.fill"
        case .info: return "info.circle"
        case .tip: return "lightbulb"
        }
    }

    private func color(_ kind: AdviceKind) -> Color {
        switch kind {
        case .warning: return .orange
        case .info: return .primary
        case .tip: return .secondary
        }
    }
}

/// The user's name of one socket. Keeps what is typed (spaces included) and saves the trimmed name on every change.
struct PortNameField: View {
    @ObservedObject var hw: HardwareModel
    let connector: UsbConnector
    @State private var text = ""

    var body: some View {
        TextField("vlastní název", text: $text)
            .textFieldStyle(.roundedBorder)
            .help("Vlastní název portu, např. přední levý")
            .onAppear { text = hw.portName(connector) }
            .onChange(of: text) { hw.rename(connector, $0) }
    }
}
```

- [ ] **Step 4: `AppModel.swift`** — doplnit:

V `JobItem` přidat vlastnosti a metody níže a **nahradit `refresh()` z úkolu 11** touto podobou (navíc volá `updateChain()`); v souboru smí zůstat jen jedna `refresh()`:

```swift
    @Published private(set) var chainLines: [String] = []
    private var links: [RoleLink]?
    private var connectors: [UsbConnector]?
    private var portNames: [String: String] = [:]
    private var chainRequested = false

    func refresh() {
        objectWillChange.send()
        progress.update(job.status(), now: Date())
        updateChain()
    }

    /// Where the card and each destination are connected — resolved once, off the main thread, when copying starts.
    func resolveChainIfNeeded(portNames names: [String: String]) {
        guard !chainRequested, let plan = job.plan, status.phase == .copying else { return }
        chainRequested = true
        let config = job.config
        DispatchQueue.global(qos: .utility).async { [weak self] in
            var l = [RoleLink(role: "Karta", link: DriveLinks.resolve(plan.source))]
            l += plan.destRoots.enumerated().map { RoleLink(role: "Cíl \($0.offset + 1)", link: DriveLinks.resolve($0.element)) }
            let c = UsbScanner.scan().connectors
            DispatchQueue.main.async {
                guard let self else { return }
                self.links = l
                self.connectors = c
                self.portNames = names
                self.progress.attachBrakeMeter(BrakeMeter(chunkBytes: Int64(config.chunkMB) << 20, bufferCount: config.buffers,
                                                          linkMBps: l.map(ChainView.linkCeiling)))
            }
        }
    }

    /// The chain with live throughput and the brake (spec 8a.3) while copying or verifying; unchanged lines are kept.
    private func updateChain() {
        var lines: [String] = []
        if let links, status.phase == .copying || status.phase == .verifying {
            let rates = [status.phase == .copying ? progress.readRate / 1e6 : 0] + progress.destRates.map { $0 / 1e6 }
            let chain = ChainView.build(links, mbps: rates, connectors: connectors, portNames: portNames)
            lines = ChainView.lines(chain, brake: status.phase == .copying ? progress.brake : nil)
        }
        if lines != chainLines { chainLines = lines }
    }
```

V `AppModel`:

```swift
    private var _hardware: HardwareModel?
    /// Created on first use (the Hardware section or window); `waitForHardware` never creates it.
    var hardware: HardwareModel {
        if let h = _hardware { return h }
        let h = HardwareModel(app: self)
        _hardware = h
        return h
    }

    /// Before the process exits: a running measurement stops and deletes its temporary file.
    func waitForHardware() { _hardware?.stopAndWait(timeout: 10) }
    private var shownCopyRunning = false

    /// Who takes part in the HW test: the ticked cards and the destinations; with neither, every volume (byDrive).
    /// No disk is touched: destinations come from the probed status (one still being checked counts).
    func hardwareRoles() -> (cards: [String], destinations: [String], byDrive: Bool) {
        let cards = (volumes.map(\.path) + manualSources).filter { selectedSources.contains($0) }
        let dests = settings.destRoots.filter { destStatus[$0].map { $0.exists || $0.checking } ?? true }
        return cards.count + dests.count > 0 ? (cards, dests, false) : ([], [], true)
    }
```

`tick()` (nová podoba):

```swift
    private func tick() {
        for j in jobs where !j.isFinished {
            j.refresh()
            j.resolveChainIfNeeded(portNames: settings.portNames)
        }
        // A copy started or ended: the Hardware window's measure button and hint follow.
        let running = copyRunning
        if running != shownCopyRunning {
            shownCopyRunning = running
            hardware.objectWillChange.send()
        }
    }
```

- [ ] **Step 5: `OffloadApp.swift`** — do `AppDelegate` přidat (⌘Q během měření nesmí nechat na cíli 1 GB `.offload-bench-*`;
`shouldTerminate` HW test nečeká, proto se čeká až tady, jako Windows `WaitForHardware`):

```swift
    /// A running hardware measurement stops and deletes its temporary file before the process exits (≤ 10 s).
    func applicationWillTerminate(_ notification: Notification) {
        MainActor.assumeIsolated { model?.waitForHardware() }
    }
```

Dále `OffloadApp.body` a nový `HardwareMenuItem`:

```swift
    var body: some Scene {
        WindowGroup("Offload") {
            ContentView()
                .environmentObject(model)
                .onAppear { delegate.model = model }
        }
        .defaultSize(width: 1240, height: 860)
        .windowToolbarStyle(.unifiedCompact)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandMenu("Nástroje") { HardwareMenuItem() }
        }
        // One Hardware window: opening it again only brings it to the front (no second run).
        Window("Hardware — USB porty a disky", id: "hardware") {
            HardwareView(hw: model.hardware)
                .environmentObject(model)
        }
        .defaultSize(width: 860, height: 700)
    }
```

```swift
/// Nástroje › Prověřit USB porty… (⌘U — like Ctrl+U on Windows; free in an app without a Format menu).
/// A View, so it can use `openWindow` (macOS 13+).
struct HardwareMenuItem: View {
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Button("Prověřit USB porty…") { openWindow(id: "hardware") }
            .keyboardShortcut("u", modifiers: .command)
    }
}
```

- [ ] **Step 6: `Views.swift`**

`SetupPanel` — do `VStack` za `ToolsSection()` přidat `HardwareSection(hw: model.hardware)`.

Nová struktura:

```swift
struct HardwareSection: View {
    @ObservedObject var hw: HardwareModel
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            SectionHeader(title: "Hardware", systemImage: "cable.connector",
                          trailing: AnyView(Button("Prověřit…") { openWindow(id: "hardware") }.buttonStyle(.borderless)
                              .help("Prověřit USB porty (⌘U)")))
            Text(hw.summary).font(.caption).foregroundStyle(.secondary)
        }
    }
}
```

(Pokud SF Symbol `cable.connector` na macOS 13 chybí — v Xcode se zobrazí prázdná ikona —, použít `cpu`.)

`JobCard` — za `Text(item.detailText)…` uvnitř `if item.isRunning { … }` přidat:

```swift
                if !item.chainLines.isEmpty {
                    VStack(alignment: .leading, spacing: 1) {
                        ForEach(Array(item.chainLines.enumerated()), id: \.offset) { _, line in
                            Text(line)
                                .font(.caption.monospacedDigit())
                                .foregroundStyle(line.hasPrefix("Úzké hrdlo") ? Color.orange : Color.secondary)
                                .lineLimit(1).truncationMode(.middle)
                        }
                    }
                }
```

- [ ] **Step 7: Build a ruční test**

Run: `swift build && swift test && scripts/build-app.sh && open build/Offload.app`

1. Při startu se **nic** nespouští; menu **Nástroje › Prověřit USB porty…** se zkratkou ⌘U otevře okno „Hardware — USB porty a disky“ se stavem „Zjišťuji připojení…“, pak sekce Připojení / Doporučení / Měření / USB porty. Druhé ⌘U okno jen přenese dopředu (žádný druhý běh). Pokud ⌘U nic neudělá, ověřit, že `@Environment(\.openWindow)` funguje v `CommandMenu` na této verzi macOS (spec 9) — náhradně tlačítko v sekci Hardware.
2. Bez karty a cíle: „Pro měření vyberte kartu nebo přidejte cíl.“, „Změřit rychlost…“ neaktivní, připojení podle svazků.
3. S kartou a cílem: „Změřit rychlost…“ → dotaz s výpisem cílů → „Změřit“ → „Měřím rychlost… (může trvat několik minut)“; „Zastavit“ → „Zastavuji…“ → „Měření přerušeno — zobrazeno, co se stihlo změřit.“; na cíli nezůstane `.offload-bench-*` (`ls -a`).
4. Pojmenovat port „přední levý“ (mezery jdou psát) → název se hned objeví v Připojení a Doporučení; po restartu aplikace zůstane.
5. Během kopie: „Změřit rychlost…“ neaktivní s textem „Během kopírování nelze měřit — …“.
5a. **⌘Q během měření** (Měřím rychlost…) → aplikace se do pár sekund ukončí; `ls -a /Volumes/<cíl>` neukáže žádný `.offload-bench-*`.
5b. Okno zavřít během „Zastavuji…“ a hned znovu ⌘U → stav „Čekám, až doběhne předchozí test…“, pak proběhne rychlý test.
6. Karta úlohy během kopie: pod rychlostmi řádky řetězu („Karta … — USB 3 (5 Gb/s), port N  … MB/s  … % linky“, „Cíl 1 …“, „Úzké hrdlo: …“ oranžově); při ověřování bez úzkého hrdla; po dokončení řetěz zmizí.
7. Sekce Hardware vlevo: po testu „Poslední test d. M. yyyy H:mm · bez varování“ / „… · N varování“.

- [ ] **Step 8: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 17 — GUI Hardware: menu Nástroje › Prověřit USB porty… (⌘U) jen na vyžádání, okno Hardware (připojení, doporučení, měření s dotazem a okamžitým zastavením, USB porty s vlastními názvy), sekce Hardware s posledním testem, řetěz připojení a úzké hrdlo na kartě úlohy`

`docs/ROZDILY.md` (tento úkol rozdíl zavírá — upravit hned, ne až v úkolu 26): řádky „Test USB portů na vyžádání
+ doporučení“ a „Propustnost článků HW při kopii“ Mac `✅ CLI + GUI` (řetěz připojení, % linky, úzké hrdlo na kartě
úlohy, okno Hardware ⌘U); v „Záměrných rozdílech“ smazat odrážku o HW diagnostice jen na Windows / jen CLI na Macu;
v „GUI navíc na Windows“ odebrat okno Hardware s kontrolou USB portů a vlastními názvy portů (je i na Macu, ⌘U).

Run: `git add -A mac/Sources docs && git commit -m "Mac: Hardware window on demand (Cmd+U), Hardware section, connection chain on the job card"`

---

### Task 18: Filter graph a barvy proxy (BT.709) a náhledů (JFIF)

Doplněk ke spec — oddíl D.4. Port `windows/src/Offload.Core/FilterGraph.cs` (`ToRec709`, `TagRec709`, `ToJfif`, `PreMap`,
`Thumbnail`, `Proxy`) a `Encoders.Rec709Args`. Pre-map pro vestavěné LUTy je tu jen jako čistá funkce s testem; použije
ho až úkol 22.

**Files:**
- Create: `mac/Sources/OffloadKit/FilterGraph.swift`
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (`thumbnails` — filtr z `FilterGraph.thumbnail`; `render` — řetěz z `FilterGraph.proxy` + `FilterGraph.rec709Args`; smazat `thumbnailFilter(applyLUT:)` z úkolu 6)
- Modify: `mac/Tests/OffloadKitTests/ThumbnailTests.swift` (jeden řádek — referenční filtr)
- Create: `mac/Tests/OffloadKitTests/FilterGraphTests.swift`, `mac/Tests/OffloadKitTests/ColorTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `ProxySettings`, `ProxyCodec.pixelFormat` (internal), `ProxyRenderer.thumbnails(_:cardPath:info:applyLUT:isCancelled:)` (úkol 6), `ProxyRenderer.render(_:cardPath:info:output:progress:isCancelled:)`, `TestMedia` (úkol 1).
- Produces:
  - `enum FilterGraph { static let preMap: String; static let toRec709: String; static let tagRec709: String; static let toJFIF: String; static let rec709Args: [String]; static func lut(_ file: String, premap: Bool) -> String; static func thumbnail(lutFile: String?, premap: Bool) -> String; static func proxy(_ s: ProxySettings, outHeight: Int, alreadyCropped: Bool, lutFile: String?, premap: Bool, burnIn: [String]) -> String }`
  - `ProxyRenderer.thumbnailFilter(applyLUT:)` **zaniká** (nahrazuje ho `FilterGraph.thumbnail`).

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/FilterGraphTests.swift`:

```swift
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
```

`mac/Tests/OffloadKitTests/ColorTests.swift` (ffmpeg; bez něj se přeskočí):

```swift
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

    // How a LUT reaches the renderer. Here (task 18) through settings.lutPath; task 22 switches these three helpers
    // to the per-clip `lut:` parameter — the tests themselves stay the same.
    private func renderer(_ settings: ProxySettings, lut: String?) -> ProxyRenderer {
        var s = settings
        s.lutPath = lut
        return ProxyRenderer(settings: s, tools: TestMedia.tools)
    }

    private func render(_ r: ProxyRenderer, _ clip: Clip, card: String, to out: String, lut: String?) throws {
        try r.render(clip, cardPath: card, info: try r.probe(clip, cardPath: card), output: out,
                     progress: { _ in }, isCancelled: { false })
    }

    private func thumbnails(_ r: ProxyRenderer, _ clip: Clip, card: String, lut: String?) throws -> [Thumbnail] {
        try r.thumbnails(clip, cardPath: card, info: try r.probe(clip, cardPath: card), applyLUT: lut != nil)
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
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "FilterGraphTests|ColorTests"` → nesestaví se (`cannot find 'FilterGraph' in scope`).
Kdo chce vidět i skutečnou chybu barev: dočasně zakomentovat `FilterGraphTests` — `testProxyRedIsBT709MatrixAndTagged`
selže s `[color_range=unknown …]` nebo `rgb=(…,23,…)` (dnešní Mac proxy neoznačuje a po kroku v RGB bere BT.601).

- [ ] **Step 3: `mac/Sources/OffloadKit/FilterGraph.swift`**:

```swift
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
```

- [ ] **Step 4: `Proxy.swift`**

1. Smazat celou funkci `thumbnailFilter(applyLUT:)` (úkol 6).
2. V `thumbnails(…)` nahradit řádek `let filter = thumbnailFilter(applyLUT: applyLUT)` řádkem:

```swift
        let filter = FilterGraph.thumbnail(lutFile: hasLUT && applyLUT ? lutCopy : nil, premap: false)
```

3. V `render(…)` nahradit celý blok od `var vf: [String] = []` po `vf.append("format=\(settings.codec.pixelFormat)")` (včetně) tímto:

```swift
        let vf = FilterGraph.proxy(settings, outHeight: outHeight, alreadyCropped: src.alreadyCropped,
                                   lutFile: hasLUT ? lutCopy : nil, premap: false,
                                   burnIn: burnIn(clip: clip, info: info, height: outHeight))
```

a řádek `args += ["-vf", vf.joined(separator: ",")] + settings.codec.encoderArgs(bitrateMbps: settings.bitrateMbps)` tímto:

```swift
        args += ["-vf", vf] + settings.codec.encoderArgs(bitrateMbps: settings.bitrateMbps) + FilterGraph.rec709Args
```

4. `mac/Tests/OffloadKitTests/ThumbnailTests.swift` (úkol 6, `testMiddleFrameMatchesCaption24fps`, funkce `reference`) —
referenční snímek musí jít stejným filtrem jako náhled; nahradit řádek
`"-vf", "select='eq(n,\(n))',\(r.thumbnailFilter(applyLUT: false))",` řádkem:

```swift
                                         "-vf", "select='eq(n,\(n))',\(FilterGraph.thumbnail(lutFile: nil, premap: false))",
```

- [ ] **Step 5: Testy projdou** — Run: `swift test --filter "FilterGraphTests|ColorTests|ThumbnailTests|StageTwoTests"` → 0 chyb
(s ffmpeg nic přeskočeno); pak `swift test`.

**Ověřit na Macu (Doplněk D.10, řádky 1–2):** jestli `testProxyRedIsBT709MatrixAndTagged` hlásí u `h264` špatné značky,
jde o `h264_videotoolbox` — výsledek ručního příkazu z D.10 zapsat do commitu; oprava patří do `FilterGraph`/argumentů
enkodéru (např. `-movflags +write_colr`), ne do testu.

- [ ] **Step 6: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 18 — barvy proxy výslovně BT.709 (matice, legální rozsah, značky ve VideoToolbox/ProRes i v MOV), náhledy JPEG výslovně BT.601 v plném rozsahu (JFIF); filter graph jako samostatný testovaný modul`

`docs/ROZDILY.md` — v „Záměrných rozdílech“ smazat odrážku „**Barvy proxy:** …“ (obě verze se teď chovají stejně;
popis barev je v FORMATY/README, ne mezi rozdíly).

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: proxies explicitly BT.709 and tagged, JPEG thumbnails BT.601 (JFIF)"`

---

### Task 19: Ochrana proti zaseknutí ffmpeg (VideoToolbox jen H.264/HEVC, hlídač 60 s, opakování po hwaccel)

Doplněk ke spec — oddíl D.5. Port **konečné** Windows verze (`windows/src/Offload.Core/ProxyRenderer.cs`, commit `a3f047c` na
`windows-followups`: `UseHardwareDecoding`, `DefaultStallTimeout`, `KillWait`, `Log`, `Render`/`RenderOnce`, hlídač a pumpa
dekodéru v `RunFfmpeg`, `StallNote`, `FfmpegStalledException`, `ProxyRetryException`, `ProgressWatch`; testy
`ProxyWatchdogTests.cs`). Důvod na Windows: ffmpeg 8.1 s `-hwaccel auto` se na FFV1 navždy zasekl (Vulkan
VK_ERROR_DEVICE_LOST) a úloha visela.

**Files:**
- Create: `mac/Sources/OffloadKit/FFmpegWatch.swift` (`FFmpegStall`, `ProxyRetryFailed`, `ProgressWatch`)
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (`ProxyRenderer`: `defaultStallTimeout`, `killWait`, `stallTimeout`, `log`, `usesHardwareDecode`, `stallNote`, `frameSource(…, hwaccel:)`, `thumbnails` (jen volání `frameSource`), `render` → `render` + `renderOnce`, `runFFmpeg(…, stallAfter:)` s pumpou dekodéru, nový `stop`)
- Modify: `mac/Sources/OffloadKit/Job.swift` (`analyzeAndRender`: `renderer.log = log`)
- Create: `mac/Tests/OffloadKitTests/FFmpegWatchTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `FilterGraph.proxy`, `FilterGraph.rec709Args` (úkol 18), `Timecode.isValid` (úkol 5), `TempDir`, `TestFS` (úkol 1).
- Produces:
  - `struct FFmpegStall: Error, CustomStringConvertible { let note: String }`, `struct ProxyRetryFailed: Error, CustomStringConvertible { let note: String }` — `description` je poznámka u klipu
  - `final class ProgressWatch { init(now: Date = Date()); @discardableResult func advanced(_ line: String, now: Date = Date()) -> Bool; func dataFlowed(now: Date = Date()); var hasAdvanced: Bool { get }; func idleSeconds(now: Date = Date()) -> TimeInterval; func isStalled(limit: TimeInterval, now: Date = Date()) -> Bool }`
  - `static let ProxyRenderer.defaultStallTimeout: TimeInterval = 60`, `static let ProxyRenderer.killWait: TimeInterval = 10`, `var ProxyRenderer.stallTimeout: TimeInterval` (testy zkracují), `var ProxyRenderer.log: ((String) -> Void)?`
  - `static func ProxyRenderer.usesHardwareDecode(_ codec: String?) -> Bool`
  - `static func ProxyRenderer.stallNote(_ seconds: TimeInterval, retriedInSoftware: Bool) -> String`
  - `private func ProxyRenderer.frameSource(_:cardPath:info:singleFrameAt:hwaccel:)` (místo `forRender:` z úkolu 6)
  - `func ProxyRenderer.runFFmpeg(_:_:feeder:duration:progress:isCancelled:stallAfter: TimeInterval? = nil)` — hlídá jen render proxy
  - `render(…)` — signatura beze změny; pravidla v tabulce níže

Pravidla (1:1 s konečným Windows `Render`/`RunFfmpeg`):

- **Pokrok** = nová, **vyšší** hodnota `frame=` nebo `out_time_us=` z `-progress pipe:1` (úvodní `frame=0`, `out_time_us=N/A`
  a záporné časy pokrok nejsou), závěrečné `progress=end`, **nebo data, která dekodér RAW (braw-decode) poslal do ffmpeg**.
  Každý pokrok vynuluje hodinky.
- **Lhůta:** 60 s bez pokroku; **do prvního pokroku 3× tolik (180 s)** — načtení vstupu, rozběh dekodéru, lookahead enkodéru.
- **Ukončení:** zaseknutý (nebo zrušený) ffmpeg dostane SIGTERM (`Process.terminate`), čeká se **nejvýš 10 s**, pak SIGKILL;
  když ani pak neskončí (proces uvízlý v ovladači GPU), zapíše se do logu `ffmpeg (pid N) did not end 10 s after kill`
  a úloha jede dál — nikdy se nečeká bez limitu.

| První běh | Druhý běh (bez hwaccel) | Výsledek / poznámka u klipu |
|---|---|---|
| hotovo | — | hotovo |
| zaseknutí **bez** hwaccel | — (neopakuje se) | `ffmpeg se zasekl (60 s bez nového snímku); proxy nevznikla` |
| chyba ffmpeg bez hwaccel | — | ta chyba |
| zaseknutí nebo chyba **s** hwaccel | hotovo | hotovo; log `proxy <klip>: with hardware decoding: <1. chyba> — retrying in software`, `proxy <klip>: software decoding succeeded` |
| zaseknutí s hwaccel | zaseknutí | `ffmpeg se zasekl (60 s bez nového snímku) i bez hardwarového dekódování; proxy nevznikla` |
| zaseknutí s hwaccel | chyba ffmpeg | `ffmpeg se zasekl (60 s bez nového snímku); bez hardwarového dekódování selhal: <2. chyba>` |
| chyba s hwaccel | chyba nebo zaseknutí | `<2. chyba>; s hardwarovým dekódováním předtím: <1. chyba>` |
| zrušeno (kdykoli; kontroluje se i těsně před opakováním) | — | „cancelled“, nikdy se neopakuje |

Po neúspěšném opakování navíc log `proxy <klip>: software decoding failed too: <2. chyba>`. Chyba se v úloze zapíše jako
`proxyNote` (`"\(error)"`) a proxy je `failed`; úloha pokračuje dalším klipem. Náhledy hlídané nejsou (jako Windows —
jeden snímek, vždy softwarově).

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/FFmpegWatchTests.swift` (bez ffmpeg — „ffmpeg“ je skript):

```swift
import XCTest
@testable import OffloadKit

final class FFmpegWatchTests: XCTestCase {
    func testHardwareDecodeOnlyForH264AndHEVC() {
        for (codec, expected) in [("h264", true), ("hevc", true), ("HEVC", true), ("h264 High", true), ("hevc Main 10", true),
                                  ("ffv1", false), ("prores HQ", false), ("mjpeg", false), ("dnxhd", false), ("", false)] {
            XCTAssertEqual(ProxyRenderer.usesHardwareDecode(codec), expected, codec)
        }
        XCTAssertFalse(ProxyRenderer.usesHardwareDecode(nil))
    }

    func testProgressWatchOnlyARisingFrameOrTimeCounts() {
        let w = ProgressWatch()
        // The opening block says nothing has been written yet (encoder lookahead, input probing).
        XCTAssertFalse(w.advanced("frame=0"))
        XCTAssertFalse(w.advanced("fps=0.00"))
        XCTAssertFalse(w.advanced("out_time_us=N/A"))
        XCTAssertFalse(w.advanced("out_time_us=-9223372036854775807"))
        XCTAssertFalse(w.advanced("progress=continue"))
        XCTAssertFalse(w.hasAdvanced)
        XCTAssertTrue(w.advanced("frame=1"))
        XCTAssertTrue(w.advanced("out_time_us=40000"))
        // ffmpeg keeps printing the same block every half second while it is stuck.
        XCTAssertFalse(w.advanced("frame=1"))
        XCTAssertFalse(w.advanced("out_time_us=40000"))
        XCTAssertFalse(w.advanced("progress=continue"))
        XCTAssertTrue(w.advanced("frame=2"))
        XCTAssertTrue(w.advanced("progress=end"))
        XCTAssertTrue(w.hasAdvanced)
    }

    // Before the first progress 3× the limit applies; data piped in by a RAW decoder is progress too.
    func testGraceUntilTheFirstProgressAndFeederDataCounts() {
        let t0 = Date()
        let w = ProgressWatch(now: t0)
        XCTAssertFalse(w.isStalled(limit: 60, now: t0.addingTimeInterval(179)))
        XCTAssertTrue(w.isStalled(limit: 60, now: t0.addingTimeInterval(181)))
        w.dataFlowed(now: t0.addingTimeInterval(10))
        XCTAssertTrue(w.hasAdvanced)
        XCTAssertEqual(w.idleSeconds(now: t0.addingTimeInterval(40)), 30, accuracy: 0.001)
        XCTAssertFalse(w.isStalled(limit: 60, now: t0.addingTimeInterval(69)))
        XCTAssertTrue(w.isStalled(limit: 60, now: t0.addingTimeInterval(71)))
    }

    func testStallNotesAreTheWindowsTexts() {
        XCTAssertEqual(ProxyRenderer.stallNote(60, retriedInSoftware: false), "ffmpeg se zasekl (60 s bez nového snímku)")
        XCTAssertEqual(ProxyRenderer.stallNote(60, retriedInSoftware: true),
                       "ffmpeg se zasekl (60 s bez nového snímku) i bez hardwarového dekódování; proxy nevznikla")
        XCTAssertEqual(ProxyRenderer.defaultStallTimeout, 60)
        XCTAssertEqual(ProxyRenderer.killWait, 10)
    }

    private enum Mode: String { case ok, stall, fail, slowStart }

    private struct Fake {
        let path: String
        let calls: String
        /// One line per run: the arguments it got.
        var invocations: [String] {
            ((try? String(contentsOfFile: calls, encoding: .utf8)) ?? "").split(separator: "\n").map(String.init)
        }
    }

    /// A stand-in "ffmpeg": logs its arguments, then — depending on whether it got -hwaccel — stalls (no progress,
    /// sleeps), fails (exit 1, "boom hw" / "boom sw" on stderr), starts slowly (2 s without progress) or prints `steps`
    /// rising progress blocks 0.3 s apart and creates its output (the last argument).
    private func fakeFFmpeg(_ dir: TempDir, hw: Mode, sw: Mode, steps: Int = 2) -> Fake {
        let path = dir.path + "/fake-ffmpeg"
        let calls = dir.path + "/calls.txt"
        let script = """
        #!/bin/sh
        echo "$*" >> '\(calls)'
        case " $* " in *" -hwaccel "*) MODE=\(hw.rawValue); WHO=hw;; *) MODE=\(sw.rawValue); WHO=sw;; esac
        [ "$MODE" = stall ] && exec sleep 30
        if [ "$MODE" = fail ]; then echo "boom $WHO" >&2; exit 1; fi
        [ "$MODE" = slowStart ] && sleep 2
        i=0
        while [ $i -lt \(steps) ]; do i=$((i+1)); echo "frame=$i"; echo "out_time_us=${i}00000"; sleep 0.3; done
        for last; do :; done
        : > "$last"
        """
        try! script.write(toFile: path, atomically: true, encoding: .utf8)
        try! FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: path)
        return Fake(path: path, calls: calls)
    }

    private func script(_ dir: TempDir, _ name: String, _ body: String) -> String {
        let path = dir.path + "/" + name
        try! ("#!/bin/sh\n" + body + "\n").write(toFile: path, atomically: true, encoding: .utf8)
        try! FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: path)
        return path
    }

    private func clip() -> Clip {
        Clip(name: "a", path: "a.mov", format: .video, segments: ["a.mov"], bytes: 1, isCameraProxy: false)
    }

    private func info(_ codec: String = "h264 High") -> ClipInfo {
        var info = ClipInfo()
        info.codec = codec
        info.frameRate = "25/1"
        info.duration = 1
        return info
    }

    private final class Lines {
        private let lock = NSLock()
        private var all: [String] = []
        func add(_ s: String) { lock.lock(); all.append(s); lock.unlock() }
        var lines: [String] { lock.lock(); defer { lock.unlock() }; return all }
    }

    private func renderer(_ fake: Fake, stall: TimeInterval = 1, log: Lines? = nil) -> ProxyRenderer {
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools(ffmpeg: fake.path))
        r.stallTimeout = stall
        if let log { r.log = { log.add($0) } }
        return r
    }

    private func render(_ r: ProxyRenderer, _ dir: TempDir, codec: String = "h264 High",
                        isCancelled: @escaping () -> Bool = { false }) throws -> String {
        let card = dir.sub("card")
        try r.render(clip(), cardPath: card, info: info(codec), output: card + "/_PROXY/a.mov", progress: { _ in }, isCancelled: isCancelled)
        return card + "/_PROXY/a.mov"
    }

    // Review focus 9: a stalled VideoToolbox decode never blocks the queue — stopped, rendered once more in software.
    func testStalledWithHwaccelSoftwareRetrySucceedsAndIsLogged() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .ok)
        let log = Lines()
        let t0 = Date()
        let out = try render(renderer(fake, log: log), dir)
        XCTAssertLessThan(Date().timeIntervalSince(t0), 20)
        XCTAssertTrue(FileManager.default.fileExists(atPath: out))
        let calls = fake.invocations
        XCTAssertEqual(calls.count, 2, "\(calls)")
        XCTAssertTrue(calls[0].contains("-hwaccel videotoolbox"), calls[0])
        XCTAssertFalse(calls[1].contains("-hwaccel"), calls[1])
        XCTAssertEqual(TestFS.files(in: dir.path + "/card/_PROXY"), ["a.mov"])   // no .part left behind
        XCTAssertTrue(log.lines.contains { $0.contains("with hardware decoding: ffmpeg se zasekl") && $0.contains("retrying in software") }, "\(log.lines)")
        XCTAssertTrue(log.lines.contains("proxy a: software decoding succeeded"), "\(log.lines)")
    }

    // Review focus 9: stuck with and without hardware decoding → the Windows note, no proxy, no .part.
    func testStalledTwiceFailsWithTheNote() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .stall)
        let log = Lines()
        XCTAssertThrowsError(try render(renderer(fake, log: log), dir)) { e in
            XCTAssertEqual("\(e)", "ffmpeg se zasekl (1 s bez nového snímku) i bez hardwarového dekódování; proxy nevznikla")
        }
        XCTAssertEqual(fake.invocations.count, 2)
        XCTAssertEqual(TestFS.files(in: dir.path + "/card/_PROXY"), [])
        XCTAssertTrue(log.lines.contains { $0.contains("software decoding failed too") }, "\(log.lines)")
    }

    func testStalledWithHwaccelThenSoftwareFailsSaysBoth() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .fail)
        XCTAssertThrowsError(try render(renderer(fake), dir)) { e in
            XCTAssertTrue("\(e)".hasPrefix("ffmpeg se zasekl (1 s bez nového snímku); bez hardwarového dekódování selhal: ffmpeg failed (exit 1)"), "\(e)")
        }
        XCTAssertEqual(fake.invocations.count, 2)
    }

    func testHwaccelFailsSoftwareFailsNoteNamesBoth() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .fail, sw: .fail)
        XCTAssertThrowsError(try render(renderer(fake), dir)) { e in
            let note = "\(e)"
            XCTAssertTrue(note.hasPrefix("ffmpeg failed (exit 1)"), note)
            XCTAssertTrue(note.contains("; s hardwarovým dekódováním předtím: ffmpeg failed (exit 1)"), note)
        }
        XCTAssertEqual(fake.invocations.count, 2)
    }

    // A software codec that stalls is not retried (there is nothing to switch off) — and the note does not claim it was.
    func testSoftwareCodecStallsNoRetryNoteWithoutSoftwareClaim() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .ok, sw: .stall)
        XCTAssertThrowsError(try render(renderer(fake), dir, codec: "ffv1")) { e in
            XCTAssertEqual("\(e)", "ffmpeg se zasekl (1 s bez nového snímku); proxy nevznikla")
        }
        XCTAssertEqual(fake.invocations.count, 1)
    }

    func testHwaccelFailureRetriedInSoftware() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .fail, sw: .ok)
        XCTAssertTrue(FileManager.default.fileExists(atPath: try render(renderer(fake), dir)))
        XCTAssertEqual(fake.invocations.count, 2)
    }

    func testSoftwareFailureIsNotRetried() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .ok, sw: .fail)
        XCTAssertThrowsError(try render(renderer(fake), dir, codec: "prores HQ")) { e in
            XCTAssertTrue(e is ToolError, "\(e)")
        }
        XCTAssertEqual(fake.invocations.count, 1)
    }

    func testSoftwareCodecNoHwaccelAtAll() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .ok)
        _ = try render(renderer(fake), dir, codec: "ffv1")
        XCTAssertEqual(fake.invocations.count, 1)
        XCTAssertFalse(fake.invocations[0].contains("-hwaccel"))
    }

    // Cancelling while ffmpeg is stuck stops at once and is never retried.
    func testCancelledWhileStuckNoRetry() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .ok)
        let t0 = Date()
        XCTAssertThrowsError(try render(renderer(fake, stall: 30), dir, isCancelled: { !fake.invocations.isEmpty })) { e in
            XCTAssertTrue("\(e)".contains("cancelled"), "\(e)")
        }
        XCTAssertEqual(fake.invocations.count, 1)
        XCTAssertLessThan(Date().timeIntervalSince(t0), 20)
    }

    // Cancelled while the hardware-decoded attempt failed: no software retry.
    func testCancelledAfterAHwaccelFailureNoRetry() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .fail, sw: .ok)
        XCTAssertThrowsError(try render(renderer(fake), dir, isCancelled: { !fake.invocations.isEmpty }))
        XCTAssertEqual(fake.invocations.count, 1)
    }

    func testSlowButMovingRenderIsNotStopped() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .ok, sw: .ok, steps: 6)   // 1.8 s in all, but a new frame every 0.3 s
        _ = try render(renderer(fake), dir)
        XCTAssertEqual(fake.invocations.count, 1)
        XCTAssertTrue(fake.invocations[0].contains("-hwaccel videotoolbox"))
    }

    // Review: a slow first frame (2 s, limit 1 s) is within the 3× grace before the first progress.
    func testSlowFirstFrameWithinGraceNotKilled() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .slowStart, sw: .ok)
        _ = try render(renderer(fake), dir)
        XCTAssertEqual(fake.invocations.count, 1)
    }

    // Data a RAW decoder pipes into ffmpeg is progress: a consumer that prints nothing but takes data every 0.5 s for
    // 3.5 s (longer than the 3 s grace) is not killed.
    func testFeederDataIsProgress() throws {
        let dir = tempDir()
        let consumer = script(dir, "consumer", "cat > /dev/null")
        let feederPath = script(dir, "feeder", "i=0\nwhile [ $i -lt 7 ]; do i=$((i+1)); echo data; sleep 0.5; done")
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: feederPath)
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        try r.runFFmpeg(consumer, [], feeder: feeder, duration: nil, progress: nil, isCancelled: { false }, stallAfter: 1)
    }

    func testNoFeederDataAtAllKilledAfterTheGrace() {
        let dir = tempDir()
        let consumer = script(dir, "consumer", "cat > /dev/null")
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: script(dir, "feeder", "exec sleep 60"))
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        let t0 = Date()
        XCTAssertThrowsError(try r.runFFmpeg(consumer, [], feeder: feeder, duration: nil, progress: nil, isCancelled: { false },
                                             stallAfter: 1)) { e in
            XCTAssertEqual("\(e)", "ffmpeg se zasekl (1 s bez nového snímku)")
        }
        XCTAssertTrue((2.8...20).contains(Date().timeIntervalSince(t0)), "\(Date().timeIntervalSince(t0))")
    }

    func testThumbnailsNeverUseHardwareDecode() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .ok)
        let thumbs = try renderer(fake).thumbnails(clip(), cardPath: dir.sub("card"), info: info())
        XCTAssertEqual(thumbs.count, 3)
        XCTAssertFalse(fake.invocations.contains { $0.contains("-hwaccel") }, "\(fake.invocations)")
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter FFmpegWatchTests` → nesestaví se (`cannot find 'ProgressWatch'`,
`'FFmpegStall'`, `has no member 'usesHardwareDecode'`, `'stallTimeout'`, `'stallNote'`, `'killWait'`, `'log'`).

- [ ] **Step 3: `mac/Sources/OffloadKit/FFmpegWatch.swift`**:

```swift
import Foundation

/// ffmpeg made no progress for too long and was killed (port of Windows `FfmpegStalledException`).
/// `note` is the text shown at the clip.
struct FFmpegStall: Error, CustomStringConvertible {
    let note: String

    var description: String { note }
}

/// The hardware-decoded attempt and the software retry both failed; the note names both (Windows `ProxyRetryException`).
struct ProxyRetryFailed: Error, CustomStringConvertible {
    let note: String

    var description: String { note }
}

/// Watches a render: only a `frame=` or `out_time_us=` value above any seen before (or the final `progress=end`) is
/// progress — a stuck ffmpeg keeps printing the same block every half second, and the opening "frame=0",
/// "out_time_us=N/A" or negative times say nothing has been written yet. Data a RAW decoder pipes into ffmpeg is
/// progress too. Until the first progress the limit is 3× as long. Lines and data arrive on other threads — a lock.
final class ProgressWatch {
    private let lock = NSLock()
    private var frame: Int64 = 0
    private var outTime: Int64 = 0
    private var lastAdvance: Date
    private var advancedOnce = false

    init(now: Date = Date()) {
        lastAdvance = now
    }

    @discardableResult
    func advanced(_ line: String, now: Date = Date()) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        let moved: Bool
        if line.hasPrefix("frame=") {
            moved = ProgressWatch.rose(&frame, line.dropFirst("frame=".count))
        } else if line.hasPrefix("out_time_us=") {
            moved = ProgressWatch.rose(&outTime, line.dropFirst("out_time_us=".count))
        } else {
            moved = line == "progress=end"
        }
        if moved { mark(now) }
        return moved
    }

    /// A chunk the RAW decoder piped into ffmpeg (a slow decoder is not stuck).
    func dataFlowed(now: Date = Date()) {
        lock.lock()
        mark(now)
        lock.unlock()
    }

    var hasAdvanced: Bool {
        lock.lock()
        defer { lock.unlock() }
        return advancedOnce
    }

    func idleSeconds(now: Date = Date()) -> TimeInterval {
        lock.lock()
        defer { lock.unlock() }
        return now.timeIntervalSince(lastAdvance)
    }

    /// No progress for `limit` — or for 3× `limit` before the first progress (input probing, a RAW decoder starting
    /// up, the encoder's lookahead).
    func isStalled(limit: TimeInterval, now: Date = Date()) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        return now.timeIntervalSince(lastAdvance) > limit * (advancedOnce ? 1 : 3)
    }

    private func mark(_ now: Date) {
        lastAdvance = now
        advancedOnce = true
    }

    private static func rose(_ max: inout Int64, _ value: Substring) -> Bool {
        guard let v = Int64(value.trimmingCharacters(in: .whitespaces)), v > max else { return false }
        max = v
        return true
    }
}
```

- [ ] **Step 4: `Proxy.swift` (`ProxyRenderer`)**

1. Pod `let tempDir: String` přidat:

```swift
    /// A proxy render with no progress for this long is stuck (3× as long before the first progress): ffmpeg is killed
    /// and, when it decoded in hardware, the clip is retried once in software.
    static let defaultStallTimeout: TimeInterval = 60
    /// How long a killed ffmpeg is waited for: a process stuck in a GPU driver may never end — the job must go on.
    static let killWait: TimeInterval = 10
    /// Tests shorten it.
    var stallTimeout: TimeInterval = ProxyRenderer.defaultStallTimeout
    /// Where retries and unkillable processes are reported (the job's log).
    var log: ((String) -> Void)?
```

2. Pod `seekSeconds(_:_:)` (úkol 5) přidat:

```swift
    /// Hardware decoding only where it helps and is safe: H.264 and HEVC (`ClipInfo.codec` starts with the ffprobe
    /// codec name). Everything else (FFV1, ProRes, MJPEG, DNx …) decodes in software — same rule as Windows, where
    /// ffmpeg 8.1 with -hwaccel hung forever on FFV1.
    static func usesHardwareDecode(_ codec: String?) -> Bool {
        let c = (codec ?? "").lowercased()
        return c.hasPrefix("h264") || c.hasPrefix("hevc")
    }

    /// The note at a clip whose ffmpeg stalled (texts shared with Windows `StallNote`).
    static func stallNote(_ seconds: TimeInterval, retriedInSoftware: Bool) -> String {
        "ffmpeg se zasekl (\(String(format: "%.0f", seconds)) s bez nového snímku)"
            + (retriedInSoftware ? " i bez hardwarového dekódování; proxy nevznikla" : "")
    }
```

3. `frameSource` (úkol 6) nahradit celou:

```swift
    private func frameSource(_ clip: Clip, cardPath: String, info: ClipInfo, singleFrameAt: Double?, hwaccel: Bool) throws -> FrameSource {
        let path = cardPath + "/" + clip.path
        if clip.format == .video {
            // VideoToolbox only for H.264/HEVC proxies (usesHardwareDecode); thumbnails always decode in software.
            return FrameSource(inputArgs: hwaccel ? ["-hwaccel", "videotoolbox", "-i", path] : ["-i", path])
        }
        guard let b = backends[clip.format] else { throw ToolError(tool: clip.format.label, status: -1, message: "no decoder") }
        return try b.source(path, info: info, settings: settings, tools: tools, tempDir: tempDir, singleFrameAt: singleFrameAt)
    }
```

4. V `thumbnails(…)` nahradit `forRender: false` za `hwaccel: false` (náhledy hlídač nemají, argumenty beze změny).

5. Celou funkci `render(…)` nahradit funkcemi `render` a `renderOnce`:

```swift
    /// Renders the proxy. A render that stalls (no progress for `stallTimeout`) is killed. With hardware decoding the clip
    /// is then retried once in software — as is one that failed with it; without, the stall fails the proxy. Cancelling
    /// never retries. Notes and log lines as on Windows.
    public func render(_ clip: Clip, cardPath: String, info: ClipInfo, output: String,
                       progress: @escaping (Double) -> Void, isCancelled: @escaping () -> Bool) throws {
        guard let ffmpeg = tools.ffmpeg else { throw ToolError(tool: "ffmpeg", status: -1, message: "not found") }
        let hw = clip.format == .video && ProxyRenderer.usesHardwareDecode(info.codec)
        let first: Error
        do {
            try renderOnce(ffmpeg, clip, cardPath: cardPath, info: info, hwaccel: hw, output: output,
                           progress: progress, isCancelled: isCancelled)
            return
        } catch let e as FFmpegStall where !hw && !isCancelled() {
            throw FFmpegStall(note: e.note + "; proxy nevznikla")
        } catch where hw && (error is FFmpegStall || error is ToolError) && !isCancelled() {
            first = error   // hardware decoding stalled or failed: once more in software
        }
        if isCancelled() { throw ToolError(tool: "ffmpeg", status: -1, message: "cancelled") }
        log?("proxy \(clip.name): with hardware decoding: \(first) — retrying in software")
        do {
            try renderOnce(ffmpeg, clip, cardPath: cardPath, info: info, hwaccel: false, output: output,
                           progress: progress, isCancelled: isCancelled)
            log?("proxy \(clip.name): software decoding succeeded")
        } catch where (error is FFmpegStall || error is ToolError) && !isCancelled() {
            log?("proxy \(clip.name): software decoding failed too: \(error)")
            switch (first, error) {
            case (is FFmpegStall, is FFmpegStall):
                throw ProxyRetryFailed(note: ProxyRenderer.stallNote(stallTimeout, retriedInSoftware: true))
            case (is FFmpegStall, _):
                throw ProxyRetryFailed(note: "\(first); bez hardwarového dekódování selhal: \(error)")
            default:
                throw ProxyRetryFailed(note: "\(error); s hardwarovým dekódováním předtím: \(first)")
            }
        }
    }

    private func renderOnce(_ ffmpeg: String, _ clip: Clip, cardPath: String, info: ClipInfo, hwaccel: Bool, output: String,
                            progress: @escaping (Double) -> Void, isCancelled: @escaping () -> Bool) throws {
        let src = try frameSource(clip, cardPath: cardPath, info: info, singleFrameAt: nil, hwaccel: hwaccel)
        defer { src.removeIntermediates() }
        let outHeight = min(settings.height, info.height ?? settings.height) & ~1
        let vf = FilterGraph.proxy(settings, outHeight: outHeight, alreadyCropped: src.alreadyCropped,
                                   lutFile: hasLUT ? lutCopy : nil, premap: false,
                                   burnIn: burnIn(clip: clip, info: info, height: outHeight))
        var args = ["-hide_banner", "-loglevel", "error", "-y", "-nostats", "-progress", "pipe:1"] + src.inputArgs
        args += ["-map", "0:v:0"]
        if settings.audio && info.audioChannels > 0 && src.audioFromInput {
            args += ["-map", "0:a:0?"] + settings.codec.audioArgs
        }
        args += ["-vf", vf] + settings.codec.encoderArgs(bitrateMbps: settings.bitrateMbps) + FilterGraph.rec709Args
        if let tc = info.timecode, Timecode.isValid(tc) { args += ["-timecode", tc] }

        let dir = (output as NSString).deletingLastPathComponent
        try IO.mkdirs(dir)
        let part = dir + "/." + (output as NSString).lastPathComponent + ".part"
        args += ["-f", "mov", part]
        do {
            try runFFmpeg(ffmpeg, args, feeder: src.feeder, duration: info.duration, progress: progress,
                          isCancelled: isCancelled, stallAfter: stallTimeout)
            guard rename(part, output) == 0 else { throw IOError("rename", output) }
        } catch {
            unlink(part)
            throw error
        }
    }
```

6. `runFFmpeg` nahradit celou a přidat `stop`:

```swift
    /// `stallAfter` (renders only, with -progress pipe:1 in the args): ffmpeg is killed and `FFmpegStall` thrown when it
    /// has reported no new frame or output time and taken no data from the feeder for that long (3× before the first
    /// progress). Cancelling is checked first and wins.
    func runFFmpeg(_ ffmpeg: String, _ args: [String], feeder: Process?, duration: Double?,
                   progress: ((Double) -> Void)?, isCancelled: () -> Bool, stallAfter: TimeInterval? = nil) throws {
        let p = ToolProcess(ffmpeg, args)
        p.captureStderr()
        let watch = ProgressWatch()
        var pump: Thread?
        if let feeder {
            // The decoder's frames go through a pump: every chunk ffmpeg takes is progress (a slow RAW decoder is not stuck).
            let fromFeeder = Pipe(), toFFmpeg = Pipe()
            feeder.standardOutput = fromFeeder
            feeder.standardError = FileHandle.nullDevice
            p.process.standardInput = toFFmpeg
            // A write after ffmpeg died must fail with EPIPE, not take the app down with SIGPIPE.
            _ = fcntl(toFFmpeg.fileHandleForWriting.fileDescriptor, F_SETNOSIGPIPE, 1)
            pump = Thread {
                // Plain read(2)/write(2): read returns whatever the decoder has written so far. FileHandle.read(upToCount:)
                // on a pipe waits for the whole count (or EOF) — a slow decoder would look stuck.
                let inFD = fromFeeder.fileHandleForReading.fileDescriptor
                let outFD = toFFmpeg.fileHandleForWriting.fileDescriptor
                let size = 4 << 20
                let buffer = UnsafeMutableRawPointer.allocate(byteCount: size, alignment: 16)
                defer { buffer.deallocate() }
                pumping: while true {
                    let n = Darwin.read(inFD, buffer, size)
                    if n < 0 && errno == EINTR { continue }
                    if n <= 0 { break }   // the decoder is done (EOF) or gone
                    var sent = 0
                    while sent < n {
                        let w = Darwin.write(outFD, buffer + sent, n - sent)
                        if w < 0 && errno == EINTR { continue }
                        if w <= 0 { break pumping }   // ffmpeg is gone (EPIPE, no signal thanks to F_SETNOSIGPIPE)
                        sent += w
                    }
                    watch.dataFlowed()
                }
                try? toFFmpeg.fileHandleForWriting.close()
            }
        } else {
            p.process.standardInput = FileHandle.nullDevice
        }
        p.onStdoutLine { line in
            watch.advanced(line)
            guard let progress, let d = duration, d > 0, line.hasPrefix("out_time_us="),
                  let us = Double(line.dropFirst("out_time_us=".count)) else { return }
            progress(min(1, us / 1_000_000 / d))
        }
        try feeder?.run()
        do {
            try p.process.run()
        } catch {
            feeder?.terminate()
            throw error
        }
        pump?.start()
        while p.process.isRunning {
            if isCancelled() {
                stop(p, feeder)
                throw ToolError(tool: "ffmpeg", status: -1, message: "cancelled")
            }
            if let limit = stallAfter, watch.isStalled(limit: limit) {
                stop(p, feeder)
                throw FFmpegStall(note: ProxyRenderer.stallNote(limit, retriedInSoftware: false))
            }
            usleep(200_000)
        }
        if let feeder, feeder.isRunning { feeder.terminate() }
        feeder?.waitUntilExit()
        guard p.process.terminationStatus == 0 else {
            throw ToolError(tool: "ffmpeg", status: p.process.terminationStatus, message: p.stderrText)
        }
        if let feeder, feeder.terminationStatus != 0, feeder.terminationReason == .exit {
            throw ToolError(tool: (feeder.executableURL?.lastPathComponent ?? "decoder"), status: feeder.terminationStatus, message: "")
        }
    }

    /// SIGTERM, at most `killWait` for ffmpeg to end, then SIGKILL — never an unbounded wait (a process stuck in a GPU
    /// driver may survive even that; the job goes on without it and the log says so).
    private func stop(_ p: ToolProcess, _ feeder: Process?) {
        let pid = p.process.processIdentifier
        p.process.terminate()
        if let feeder, feeder.isRunning { feeder.terminate() }
        let deadline = Date().addingTimeInterval(ProxyRenderer.killWait)
        while p.process.isRunning && Date() < deadline { usleep(50_000) }
        guard p.process.isRunning else { return }
        kill(pid, SIGKILL)
        if let feeder, feeder.isRunning { kill(feeder.processIdentifier, SIGKILL) }
        usleep(500_000)
        if p.process.isRunning { log?("ffmpeg (pid \(pid)) did not end \(Int(ProxyRenderer.killWait)) s after kill") }
    }
```

- [ ] **Step 5: `Job.swift`** — v `analyzeAndRender` hned za řádek `let renderer = ProxyRenderer(settings: config.proxy, tools: tools)` přidat:

```swift
        renderer.log = log   // software retries and unkillable ffmpeg processes go to the job's log
```

- [ ] **Step 6: Testy projdou** — Run: `swift test --filter "FFmpegWatchTests|ThumbnailTests|StageTwoTests|ColorTests"` → 0 chyb
(FFmpegWatchTests trvají desítky sekund — skripty spí a do prvního pokroku platí trojnásobná lhůta); pak `swift test`.

**Ověřit na Macu (Doplněk D.10, řádek 11):** `F_SETNOSIGPIPE` se přeloží (makro z `<fcntl.h>`), pumpa dostane EOF, když
dekodér skončí (Foundation po spuštění zavře rodičovskou kopii zapisovacího konce roury), a `testFeederDataIsProgress`
projde; jinak výsledek do commitu, oprava do `runFFmpeg`, ne do testu.

- [ ] **Step 7: Ruční kontrola (volitelná, s ffmpeg)** — skutečné zaseknutí VideoToolbox se reprodukovat nedá; ověřit, že
render H.264 jede s hwaccel a ProRes/FFV1 bez: `swift build -c release && .build/release/offload <karta s H.264 a ProRes klipy> -d <USB disk> --proxy`
a během renderu `ps -o args= -p $(pgrep -n ffmpeg)` → u H.264 klipu `-hwaccel videotoolbox`, u ProRes ne. FFV1 (kodek,
který na Windows zasekl ffmpeg): `mkdir -p /tmp/ffv1card && ffmpeg -f lavfi -i testsrc2=s=320x180:r=25:d=1 -c:v ffv1 -pix_fmt yuv422p10le /tmp/ffv1card/a.mkv`
a `.build/release/offload /tmp/ffv1card -d <USB disk> --proxy` → proxy hotová do minuty. BRAW (je-li `braw-decode`):
proxy z 8K klipu se nezastaví ani při pomalém prvním snímku.

- [ ] **Step 8: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 19 — ffmpeg se nezasekne navždy (jako Windows): VideoToolbox dekódování jen pro H.264/HEVC; render proxy bez pokroku 60 s (nový snímek nebo data z dekodéru RAW; do prvního pokroku 180 s) se ukončí (SIGTERM, nejvýš 10 s, pak SIGKILL); po hardwarovém dekódování jedno opakování softwarově, jinak poznámka „ffmpeg se zasekl (60 s bez nového snímku); proxy nevznikla“; zrušení se neopakuje; opakování a jeho výsledek v logu`

`docs/ROZDILY.md` — tabulka „Technologie“: přidat řádek
`| Dekódování pro proxy | VideoToolbox (-hwaccel videotoolbox) jen pro H.264/HEVC | -hwaccel auto jen pro H.264/HEVC |`
(hlídač, lhůty, opakování a texty jsou na obou stejné — mezi rozdíly nepatří). Pokud Windows práce přidala do
„Záměrných rozdílů“ odrážku, že hlídač je jen na Windows, smazat ji.

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: VideoToolbox decode only for H.264/HEVC, ffmpeg stall watchdog with grace, bounded kill and a software retry after hwaccel"`

---

### Task 20: Složka pro proxy navíc — jádro (`proxy.extraFolder`)

Doplněk ke spec — oddíl D.2. Port `windows/src/Offload.Core/ProxyCopy.cs`, kopie do složky v `OffloadJob.cs`,
`ProxySettings.ExtraFolder` (`Media.cs`), `ProxyPreflight.ExtraFolder*` (`Desktop/AppSettings.cs`) a řádek proxy
v `HtmlReport.cs`. CLI je v úkolu 23, GUI v úkolu 24.

**Files:**
- Create: `mac/Sources/OffloadKit/ProxyCopy.swift`
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (`ProxySettings.extraFolder`, dekódování)
- Modify: `mac/Sources/OffloadKit/Job.swift` (kopie do složky v části proxy; smazat `private func copyFile`, volání přes `ProxyCopy.copyFile`)
- Modify: `mac/Sources/OffloadKit/HTMLReport.swift` (řádek „Proxy: …“)
- Modify: `mac/Sources/OffloadKit/Desktop/AppSettings.swift` (`SettingsStore.normalize`; `ProxyPreflight.extraFolderInUse`, `extraFolderBlocking`, `isDirectory`)
- Create: `mac/Tests/OffloadKitTests/ProxyFolderTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `Reports.sample` (úkol 3), `OffloadJob.init(config:options:)` (úkol 4), `TestMedia`, `TestFS` (úkol 1), `SettingsStore`, `ProxyPreflight` (úkol 10).
- Produces:
  - `public var ProxySettings.extraFolder: String?` (JSON `extraFolder`, chybí = vypnuto)
  - `public enum ProxyCopy { struct Taken: Error; static func copyFile(_ src: String, to dst: String, replace: Bool = true) throws; static func renameNoReplace(_ from: String, _ to: String) throws; public static func toExtraFolder(_ source: String, extraFolder: String, date: String, cardName: String, fileName: String, maxAttempts: Int = 1000) -> (path: String?, error: String?) }`
  - `ProxyPreflight.extraFolderInUse(_ p: ProxySettings) -> String?`, `extraFolderBlocking(_ p: ProxySettings, folderExists: Bool?) -> String?`, `extraFolderBlocking(_ p: ProxySettings) -> String?` (čte disk — jen CLI), `isDirectory(_ path: String) -> Bool`
  - Poznámka u klipu `kopie do <složka> selhala: <důvod>`; v HTML `· také ve složce <span class="mono">…</span>`

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/ProxyFolderTests.swift`:

```swift
import XCTest
@testable import OffloadKit

/// The extra copy of each proxy into the user's proxy folder: `<folder>/<date>/<card>/<name>.mov`, never overwriting.
final class ProxyFolderTests: XCTestCase {
    private func proxy(_ dir: TempDir, seed: UInt64 = 1) -> String {
        let path = dir.path + "/d1/2026-10-05/A001/_PROXY/C0001.mov"
        TestFS.writeRandom(path, size: 5000, seed: seed)
        return path
    }

    private func data(_ path: String) -> Data? { try? Data(contentsOf: URL(fileURLWithPath: path)) }

    func testCopiesIntoDateAndCardFolder() {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(error)
        XCTAssertEqual(path, extra + "/2026-10-05/A001/C0001.mov")
        XCTAssertEqual(data(src), data(extra + "/2026-10-05/A001/C0001.mov"))
        XCTAssertEqual(TestFS.files(in: extra), ["2026-10-05/A001/C0001.mov"])   // no .part left
    }

    // Review focus 6: an existing file is never overwritten — the copy gets _2, _3 …
    func testExistingNameGetsSuffixNeverOverwrites() {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        let target = extra + "/2026-10-05/A001"
        let old = TestFS.writeRandom(target + "/C0001.mov", size: 100, seed: 9)
        let second = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        let third = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(second.error)
        XCTAssertNil(third.error)
        XCTAssertEqual(second.path, target + "/C0001_2.mov")
        XCTAssertEqual(third.path, target + "/C0001_3.mov")
        XCTAssertEqual(data(target + "/C0001.mov"), old)
        XCTAssertEqual(data(target + "/C0001_3.mov"), data(src))
    }

    func testMissingFolderErrorNotThrowNothingCreated() {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.path + "/disconnected"
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(path)
        XCTAssertEqual(error, "složka není dostupná")
        XCTAssertFalse(FileManager.default.fileExists(atPath: extra))
    }

    func testFailedCopyErrorAndNoTempLeft() throws {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        let target = extra + "/2026-10-05/A001"
        TestFS.writeRandom(target + "/.C0001.mov.part", size: 10, seed: 3)   // left behind by a crash
        try FileManager.default.setAttributes([.posixPermissions: 0o000], ofItemAtPath: src)   // the source cannot be read
        defer { try? FileManager.default.setAttributes([.posixPermissions: 0o644], ofItemAtPath: src) }
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(path)
        XCTAssertFalse((error ?? "").isEmpty)
        XCTAssertEqual(TestFS.files(in: target), [])
    }

    // Review focus 6: a folder full of taken names ends with an error, never an endless loop or an overwrite.
    func testAllNamesTakenErrorAfterCap() throws {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        dir.sub("proxies", "2026-10-05", "A001", "C0001.mov")      // folders with the proxy's names
        for n in 2...3 { dir.sub("proxies", "2026-10-05", "A001", "C0001_\(n).mov") }
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001",
                                                    fileName: "C0001.mov", maxAttempts: 3)
        XCTAssertNil(path)
        XCTAssertEqual(error, "žádné volné jméno (C0001.mov … C0001_3.mov)")
        XCTAssertEqual(TestFS.files(in: extra), [])
    }

    func testStaleTempReplaced() {
        let dir = tempDir()
        let src = proxy(dir)
        let extra = dir.sub("proxies")
        TestFS.writeRandom(extra + "/2026-10-05/A001/.C0001.mov.part", size: 10, seed: 3)
        let (path, error) = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "C0001.mov")
        XCTAssertNil(error)
        XCTAssertEqual(path, extra + "/2026-10-05/A001/C0001.mov")
        XCTAssertEqual(TestFS.files(in: extra + "/2026-10-05/A001"), ["C0001.mov"])
    }

    // Mac: renamex_np(RENAME_EXCL) — a file that appears between the check and the rename is never replaced.
    func testCopyFileWithoutReplaceRefusesAnExistingFile() throws {
        let dir = tempDir()
        let src = proxy(dir)
        let dst = dir.path + "/out/C0001.mov"
        let old = TestFS.writeRandom(dst, size: 7, seed: 5)
        XCTAssertThrowsError(try ProxyCopy.copyFile(src, to: dst, replace: false)) { XCTAssertTrue($0 is ProxyCopy.Taken, "\($0)") }
        XCTAssertEqual(data(dst), old)
        XCTAssertEqual(TestFS.files(in: dir.path + "/out"), ["C0001.mov"])
        try ProxyCopy.copyFile(src, to: dst)   // replace: true (copies to other destinations, the PDF) still replaces
        XCTAssertEqual(data(dst), data(src))
    }

    func testPreflightBlocksOnlyAMissingFolderInUse() {
        var p = ProxySettings()
        p.enabled = true
        p.extraFolder = "/Volumes/Proxy"
        XCTAssertEqual(ProxyPreflight.extraFolderInUse(p), "/Volumes/Proxy")
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p, folderExists: nil))   // not checked yet
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p, folderExists: true))
        XCTAssertEqual(ProxyPreflight.extraFolderBlocking(p, folderExists: false), "Složka pro proxy není dostupná: /Volumes/Proxy")
        p.enabled = false
        XCTAssertNil(ProxyPreflight.extraFolderInUse(p))
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p, folderExists: false))
        p.enabled = true
        p.extraFolder = nil
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p, folderExists: false))
    }

    func testPreflightChecksTheDisk() {
        let dir = tempDir()
        var p = ProxySettings()
        p.enabled = true
        p.extraFolder = dir.path
        XCTAssertNil(ProxyPreflight.extraFolderBlocking(p))
        p.extraFolder = dir.path + "/nope"
        XCTAssertEqual(ProxyPreflight.extraFolderBlocking(p), "Složka pro proxy není dostupná: \(dir.path)/nope")
    }

    func testSettingsExtraFolderTrimmedBlankIsNilAbsentWhenUnset() throws {
        func load(_ json: String) -> AppSettings { SettingsStore.decode(Data(json.utf8)) }
        XCTAssertNil(load(#"{"proxy":{"enabled":true}}"#).proxy.extraFolder)
        XCTAssertNil(load(#"{"proxy":{"enabled":true,"extraFolder":"  "}}"#).proxy.extraFolder)
        XCTAssertEqual(load(#"{"proxy":{"extraFolder":" /Volumes/P "}}"#).proxy.extraFolder, "/Volumes/P")
        var s = AppSettings()
        s.proxy.extraFolder = "/Volumes/Proxy"
        XCTAssertEqual(SettingsStore.decode(SettingsStore.encode(s)).proxy.extraFolder, "/Volumes/Proxy")
        let empty = String(decoding: try XCTUnwrap(SettingsStore.encode(AppSettings())), as: UTF8.self)
        XCTAssertFalse(empty.contains("extraFolder"))
    }

    func testHTMLProxyLineNamesTheFolder() {
        var r = Reports.sample()
        r.clips = [ClipEntry(clip: Clip(name: "C0001", path: "C0001.mov", format: .video, segments: ["C0001.mov"],
                                        bytes: 1, isCameraProxy: false))]
        var p = ProxySettings()
        p.enabled = true
        r.proxy = p
        XCTAssertFalse(HTMLReport.render(r).contains("také ve složce"))
        p.extraFolder = "/Volumes/Proxy & co"
        r.proxy = p
        XCTAssertTrue(HTMLReport.render(r).contains(
            "<span class=\"mono\">_PROXY/</span> · také ve složce <span class=\"mono\">/Volumes/Proxy &amp; co</span></p>"))
    }

    // MARK: Whole job (ffmpeg)

    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 1 << 20
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    private func job(_ dir: TempDir, extra: String) throws -> OffloadJob {
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/C0001.mov", seconds: 1)
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.date = "2026-10-05"
        config.proxy.enabled = true
        config.proxy.height = 360
        config.proxy.extraFolder = extra
        config.tools = TestMedia.tools
        return OffloadJob(config: config, options: options())
    }

    // Every proxy also lands in <folder>/<date>/<card>/; an older file of the same name stays, the copy gets _2.
    func testJobCopiesEveryProxyIntoTheFolder() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let extra = dir.sub("proxies")
        TestFS.writeRandom(extra + "/2026-10-05/A001/C0001.mov", size: 100, seed: 4)   // from an earlier run
        let job = try job(dir, extra: extra)
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertEqual(report.clips.count, 1)
        let clip = try XCTUnwrap(report.clips.first)
        XCTAssertEqual(clip.proxyStatus, .done)
        XCTAssertNil(clip.proxyNote)
        let backup = try XCTUnwrap(job.plan).cardPaths[0] + "/_PROXY/C0001.mov"
        XCTAssertEqual(data(backup), data(extra + "/2026-10-05/A001/C0001_2.mov"))
        XCTAssertEqual(data(extra + "/2026-10-05/A001/C0001.mov")?.count, 100)
        XCTAssertEqual(report.proxy?.extraFolder, extra)
    }

    // The folder went away mid-job (disconnected disk): a note, never a failed proxy or job.
    func testFolderGoneIsANoteOnly() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let extra = dir.path + "/unplugged"
        let job = try job(dir, extra: extra)
        var log: [String] = []
        let report = try XCTUnwrap(job.run { log.append($0) })
        let s = job.status()
        XCTAssertEqual(s.phase, .done)
        XCTAssertEqual(s.proxiesDone, 1)
        XCTAssertEqual(s.proxiesFailed, 0)
        let clip = try XCTUnwrap(report.clips.first)
        XCTAssertEqual(clip.proxyStatus, .done)
        XCTAssertEqual(clip.proxyNote, "kopie do \(extra) selhala: složka není dostupná")
        XCTAssertTrue(log.contains { $0.contains("C0001") && $0.contains(extra) }, "\(log)")
        XCTAssertFalse(FileManager.default.fileExists(atPath: extra))
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter ProxyFolderTests` → nesestaví se (`cannot find 'ProxyCopy'`,
`value of type 'ProxySettings' has no member 'extraFolder'`, `'extraFolderInUse'`).

- [ ] **Step 3: `mac/Sources/OffloadKit/ProxyCopy.swift`**:

```swift
import Darwin
import Foundation

/// Safe copies of finished files (proxies, the PDF report): a temporary `.<name>.part` next to the target, then a rename.
/// Port of Windows `ProxyCopy.cs`.
public enum ProxyCopy {
    /// The target name is taken (`copyFile(…, replace: false)` found something there).
    struct Taken: Error {}

    /// Copies `src` to `dst` via `.<name>.part`; a failure leaves no temporary file behind.
    /// `replace: false` never overwrites: an existing `dst` throws `Taken`.
    static func copyFile(_ src: String, to dst: String, replace: Bool = true) throws {
        let dir = (dst as NSString).deletingLastPathComponent
        try IO.mkdirs(dir)
        let part = dir + "/." + (dst as NSString).lastPathComponent + ".part"
        unlink(part)
        do {
            try FileManager.default.copyItem(atPath: src, toPath: part)
            if replace {
                guard rename(part, dst) == 0 else { throw IOError("rename", dst) }
            } else {
                try renameNoReplace(part, dst)
            }
        } catch {
            unlink(part)
            throw error
        }
    }

    /// rename(2) that never replaces: `renamex_np(RENAME_EXCL)` where the filesystem supports it (APFS, HFS+);
    /// where it does not (ENOTSUP/EINVAL — possibly exFAT, FAT32, SMB), a check right before a plain rename.
    static func renameNoReplace(_ from: String, _ to: String) throws {
        if renamex_np(from, to, UInt32(RENAME_EXCL)) == 0 { return }
        let e = errno
        if e == EEXIST { throw Taken() }
        guard e == ENOTSUP || e == EINVAL else { throw IOError("rename", to, e) }
        if FileManager.default.fileExists(atPath: to) { throw Taken() }
        guard rename(from, to) == 0 else { throw IOError("rename", to) }
    }

    /// The extra copy of a proxy into the user's proxy folder: `<extraFolder>/<date>/<cardName>/<fileName>`, or
    /// `<name>_2.mov`, `_3` … when the name is taken there — never overwrites. Never throws: a missing folder
    /// (disconnected disk) or a failed copy comes back as the error text. At most `maxAttempts` names
    /// (`_2` … `_1000`) are tried; then it gives up with an error.
    public static func toExtraFolder(_ source: String, extraFolder: String, date: String, cardName: String,
                                     fileName: String, maxAttempts: Int = 1000) -> (path: String?, error: String?) {
        guard ProxyPreflight.isDirectory(extraFolder) else { return (nil, "složka není dostupná") }
        let dir = URL(fileURLWithPath: extraFolder).appendingPathComponent(date).appendingPathComponent(cardName).path
        let stem = (fileName as NSString).deletingPathExtension
        let ext = (fileName as NSString).pathExtension
        func name(_ n: Int) -> String { n == 1 ? fileName : "\(stem)_\(n)" + (ext.isEmpty ? "" : "." + ext) }
        do {
            for n in 1...max(1, maxAttempts) {
                let target = dir + "/" + name(n)
                if FileManager.default.fileExists(atPath: target) { continue }
                do {
                    try copyFile(source, to: target, replace: false)
                    return (target, nil)
                } catch is Taken {
                    // Taken between the check and the rename: try the next number.
                }
            }
            return (nil, "žádné volné jméno (\(name(1)) … \(name(max(1, maxAttempts))))")
        } catch let e as IOError {
            return (nil, e.description)
        } catch {
            return (nil, error.localizedDescription)
        }
    }
}
```

- [ ] **Step 4: `Proxy.swift` (`ProxySettings`)** — pod `public var copyToAllDestinations = true` přidat:

```swift
    /// Optional: every proxy is also copied to `<folder>/<date>/<card>/` (never overwriting). nil = off (key absent).
    public var extraFolder: String?
```

a do `init(from:)` za řádek s `copyToAllDestinations`:

```swift
        extraFolder = try c.decodeIfPresent(String.self, forKey: .extraFolder)
```

(Syntetizované `encode(to:)` vynechá `nil` — klíč pak v JSON chybí, jako na Windows.)

- [ ] **Step 5: `Job.swift`**

1. Smazat `private func copyFile(_:to:)` (úkol 7). V `writePDFReports` nahradit `try? copyFile(first, to: …)` za
   `try? ProxyCopy.copyFile(first, to: …)` (zbytek řádku beze změny) a v části proxy
   `try copyFile(plan.cardPaths[d] + "/" + rel, to: other + "/" + rel)` za
   `try ProxyCopy.copyFile(plan.cardPaths[d] + "/" + rel, to: other + "/" + rel)`.
2. V části proxy hned **za** blok `if config.proxy.copyToAllDestinations { … }` (úkol 7) vložit:

```swift
                if let extra = config.proxy.extraFolder {
                    // The user's proxy folder: also never fails the proxy (the disk may have been disconnected).
                    let (_, error) = ProxyCopy.toExtraFolder(plan.cardPaths[d] + "/" + rel, extraFolder: extra, date: plan.date,
                                                             cardName: plan.cardName, fileName: name + ".mov")
                    if let error {
                        let note = "kopie do \(extra) selhala: \(error)"
                        entries[i].proxyNote = entries[i].proxyNote.map { "\($0); \(note)" } ?? note
                        log?("proxy \(clip.name): copy to \(extra) failed: \(error)")
                    }
                }
```

- [ ] **Step 6: `HTMLReport.swift`** — nahradit celý blok `if let p = r.proxy, p.enabled { … }` (uvnitř `if !r.clips.isEmpty`):

```swift
            if let p = r.proxy, p.enabled {
                let lut = p.lutPath.map { ($0 as NSString).lastPathComponent } ?? "bez LUT"
                let frame = p.frameLabel.map { ", výřez \($0)" } ?? ""
                let extra = p.extraFolder.map { " · také ve složce <span class=\"mono\">\(esc($0))</span>" } ?? ""
                h += "<p class=\"sub\">Proxy: \(esc(p.codec.label)), \(p.height)p\(frame), LUT: \(esc(lut)) → <span class=\"mono\">\(Layout.proxyDirName)/</span>\(extra)</p>\n"
            }
```

- [ ] **Step 7: `Desktop/AppSettings.swift`**

V `SettingsStore.normalize` před `return s` přidat:

```swift
        if let f = s.proxy.extraFolder?.trimmingCharacters(in: .whitespaces) { s.proxy.extraFolder = f.isEmpty ? nil : f }
```

Do `ProxyPreflight` přidat:

```swift
    /// The extra proxy folder the next start copies into (only when proxies are rendered), or nil.
    public static func extraFolderInUse(_ p: ProxySettings) -> String? { p.enabled ? p.extraFolder : nil }

    /// Why the start must wait: the proxy folder is set but missing (a disconnected disk).
    /// `folderExists` nil = not checked yet (does not block). The app passes its background answer.
    public static func extraFolderBlocking(_ p: ProxySettings, folderExists: Bool?) -> String? {
        guard let f = extraFolderInUse(p), folderExists == false else { return nil }
        return "Složka pro proxy není dostupná: \(f)"
    }

    /// The same, asking the disk now — the CLI only (the app never touches a disk on the main thread).
    public static func extraFolderBlocking(_ p: ProxySettings) -> String? {
        extraFolderBlocking(p, folderExists: extraFolderInUse(p).map(isDirectory))
    }

    public static func isDirectory(_ path: String) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: path, isDirectory: &isDir) && isDir.boolValue
    }
```

- [ ] **Step 8: Testy projdou** — Run: `swift test --filter "ProxyFolderTests|StageTwoTests|SettingsStoreTests"` → 0 chyb; pak `swift test`.

**Ověřit na Macu (Doplněk D.10, řádek 3):** `renamex_np`/`RENAME_EXCL` se v Swiftu sestaví (makro z `<stdio.h>`); když
překladač `RENAME_EXCL` nezná, nahradit `UInt32(0x00000004)` s komentářem. Chování na exFAT ověřit ručně (krok 9).

- [ ] **Step 9: Ruční kontrola na exFAT** (cílová složka pro proxy bývá na externím disku):

```sh
hdiutil create -size 200m -fs ExFAT -volname PXEXF /tmp/pxexf.dmg && hdiutil attach /tmp/pxexf.dmg
swift test --filter ProxyFolderTests/testJobCopiesEveryProxyIntoTheFolder   # na APFS
```

a pak stejný test s `extra` na `/Volumes/PXEXF` (dočasně v testu nahradit `dir.sub("proxies")` za
`"/Volumes/PXEXF/proxies"` a složku předem vytvořit — **změnu testu necommitovat**): kopie `C0001_2.mov` vznikne, původní
soubor zůstane. Výsledek (jaké `errno` vrátil `renamex_np` na exFAT) zapsat do commitu. `hdiutil detach /Volumes/PXEXF`.

- [ ] **Step 10: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 20 — složka pro proxy navíc (jádro): každá hotová proxy i do <složka>/<datum>/<karta>/ (při shodě jména _2, _3…, nikdy nepřepíše, nejvýš 1000 jmen), nedostupná složka nebo chyba kopie = jen poznámka u klipu; v reportu „· také ve složce …“`

`docs/ROZDILY.md` — řádek „**Složka pro proxy navíc**“: Mac `🔨 jádro (CLI a GUI ⏳)`.
`docs/FORMATY.md` — u `proxy.extraFolder` místo „zatím jen Windows … Mac ho zatím nezná a ignoruje“: obě verze; cesta
podle systému (`/Volumes/…` na Macu, `E:\…` na Windows), na druhé platformě se jen zobrazí v reportu.

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: extra proxy folder (copy, never overwrite, note on failure, report line)"`

---

### Task 21: Vestavěné LUTy — katalog, výběr LUTu podle klipu, nastavení

Doplněk ke spec — oddíl D.3.1–D.3.3. Port `windows/src/Offload.Core/Luts.cs` (`LutCatalog`, `LutMode`, `LutUse`, `SonyXml`,
`LutResolver`, `LutOption`), `ClipInfo.ColorRange` a výrobce + model kamery z `FFProbe` (`ClipDetector.cs`),
`ArriBackend.ColorSpace`/`IsAlexa35` a pravidla nastavení z `Desktop/AppSettings.cs` (`Normalize`, `ProxyPreflight.LutInUse`).
Čisté funkce — do renderu a reportu se LUT dostane v úkolu 22.

**Files:**
- Create: `mac/Sources/OffloadKit/Luts.swift`
- Modify: `mac/Sources/OffloadKit/Media.swift` (`ClipInfo.colorRange`; `FFProbe.parse`, `FFProbe.camera`)
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (celá `ProxySettings` — nové klíče `lutMode`, `lutBuiltin`, `lutFallback`)
- Modify: `mac/Sources/OffloadKit/RawBackends.swift` (`ARRIRAWBackend.colorSpace(lut:info:)` statická, `isAlexa35`)
- Modify: `mac/Sources/OffloadKit/Desktop/AppSettings.swift` (`SettingsStore.decode`/`normalize` s katalogem; `ProxyPreflight.lutInUse` jen pro vlastní LUT)
- Modify: `mac/Tests/OffloadKitTests/SettingsStoreTests.swift` (`testRoundTrip`, jeden řádek)
- Create: `mac/Tests/OffloadKitTests/LutTestSupport.swift`, `LutCatalogTests.swift`, `LutResolverTests.swift`, `LutOptionTests.swift`, `LutSettingsTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `Clip`, `ClipInfo`, `ProxySettings`, `SettingsStore`, `ProxyPreflight` (úkoly 10, 20), `TempDir`, `TestFS` (úkol 1).
- Produces:
  - `public struct BuiltinLut: Codable, Equatable { id, name, maker, file, curve, gamut: String; path: String }`
  - `public struct LutCatalog { folder: String?; luts: [BuiltinLut]; init(folder:luts:); func find(_ id: String?) -> BuiltinLut?; static let shared: LutCatalog; static var defaultFolder: String?; static func findFolder(executable: URL?, resourcePath: String?) -> String?; static func load(_ folder: String?) -> LutCatalog; static func isValidCube(_ path: String) -> Bool }`
  - `public enum LutMode: String, Codable, CaseIterable { case auto, builtin, custom, off = "none" }`
  - `public struct LutUse: Codable, Equatable { source, id: String?, name, reason; file: String? (mimo JSON); isBuiltin; init(source:id:name:reason:file:); static func off(_ reason: String) -> LutUse }`
  - `public enum SonyXML { static func read(_ clipFullPath: String) -> (gamma: String?, primaries: String?)? }`
  - `public enum LutResolver { static func effectiveMode(_ s: ProxySettings) -> LutMode; static func resolve(_ clip: Clip, info: ClipInfo?, clipFullPath: String, settings: ProxySettings, catalog: LutCatalog) -> LutUse; static func detect(_ clip: Clip, info: ClipInfo?, clipFullPath: String) -> (id: String?, reason: String) }`
  - `public enum LutOption { struct Parsed: Equatable { mode, builtin, path, fallback, error }; static func parse(_ value: String?, fallback: String?, catalog: LutCatalog) -> Parsed }`
  - `ProxySettings.lutMode: LutMode?`, `.lutBuiltin: String?`, `.lutFallback: String?`; `ClipInfo.colorRange: String?`
  - `FFProbe.parse(_ data: Data) throws -> ClipInfo`, `FFProbe.camera(make:model:) -> String?`
  - `static func ARRIRAWBackend.colorSpace(lut: String?, info: ClipInfo) -> String`, `static func ARRIRAWBackend.isAlexa35(_ camera: String?) -> Bool`
  - `SettingsStore.decode(_ data: Data?, catalog: LutCatalog = .shared)`, `SettingsStore.normalize(_:catalog:)`
  - testy: `TestMedia.lutFolder`, `TestMedia.luts`, `TestMedia.customLut(_:) -> LutUse`

Pozn. k názvům: režim „bez LUT“ je ve Swiftu `LutMode.off` (JSON hodnota zůstává `"none"`) a `LutUse.off(_:)` (zdroj `"none"`) —
`case none` u výčtu, který se používá jako `LutMode?`, by se pletl s `Optional.none` (`settings.proxy.lutMode == .none`
by tiše znamenalo „nenastaveno“).

- [ ] **Step 1: Pomocníci testů** — `mac/Tests/OffloadKitTests/LutTestSupport.swift`:

```swift
import Foundation
@testable import OffloadKit

extension TestMedia {
    /// The repo's shared/luts — tests never depend on where xctest runs from (its executable is Xcode's xctest).
    static let lutFolder: String = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent()   // OffloadKitTests
        .deletingLastPathComponent()   // Tests
        .deletingLastPathComponent()   // mac
        .deletingLastPathComponent()   // repo
        .appendingPathComponent("shared/luts").path

    static let luts = LutCatalog.load(lutFolder)

    /// A custom .cube as the resolver hands it over.
    static func customLut(_ path: String) -> LutUse {
        LutUse(source: "custom", id: nil, name: (path as NSString).lastPathComponent, reason: "vlastní LUT", file: path)
    }
}
```

- [ ] **Step 2: Padající testy**

`mac/Tests/OffloadKitTests/LutCatalogTests.swift`:

```swift
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
```

`mac/Tests/OffloadKitTests/LutResolverTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class LutResolverTests: XCTestCase {
    private let cat = TestMedia.luts

    private func videoClip(_ path: String = "XDROOT/Clip/C0001.MP4") -> Clip {
        Clip(name: ((path as NSString).lastPathComponent as NSString).deletingPathExtension, path: path, format: .video,
             segments: [path], bytes: 1, isCameraProxy: false)
    }

    private func rawClip(_ format: ClipFormat, _ path: String) -> Clip {
        Clip(name: "R", path: path, format: format, segments: [path], bytes: 1, isCameraProxy: false)
    }

    private func info(camera: String? = nil, log: String? = nil) -> ClipInfo {
        var i = ClipInfo()
        i.camera = camera
        i.logEncoding = log
        return i
    }

    /// A clip file and its Sony sidecar "<clip>M01.XML" (NonRealTimeMeta, with the namespace a camera writes).
    private func sonyXML(_ dir: TempDir, _ clipRel: String, gamma: String, primaries: String) -> String {
        let clip = dir.path + "/" + clipRel
        try! FileManager.default.createDirectory(atPath: (clip as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try! "x".write(toFile: clip, atomically: true, encoding: .utf8)
        try! """
        <?xml version="1.0" encoding="UTF-8"?>
        <NonRealTimeMeta xmlns="urn:schemas-professionalDisc:nonRealTimeMeta:ver.2.10">
          <AcquisitionRecord><Group name="CameraUnitMetadataSet">
            <Item name="CaptureGammaEquation" value="\(gamma)"/>
            <Item name="CaptureColorPrimaries" value="\(primaries)"/>
          </Group></AcquisitionRecord>
        </NonRealTimeMeta>
        """.write(toFile: (clip as NSString).deletingPathExtension + "M01.XML", atomically: true, encoding: .utf8)
        return clip
    }

    private func auto(fallback: String? = nil) -> ProxySettings {
        var s = ProxySettings()
        s.enabled = true
        s.lutMode = .auto
        s.lutFallback = fallback
        return s
    }

    func testSonySLog3FromXML() {
        for (gamma, primaries, id) in [("s-log3-cine", "S-Gamut3Cine", "sony-slog3-sgamut3cine"),
                                       ("s-log3", "S-Gamut3", "sony-slog3-sgamut3"),
                                       ("s-log3-cine", "", "sony-slog3-sgamut3cine")] {
            let dir = tempDir()
            let clip = sonyXML(dir, "XDROOT/Clip/C0001.MP4", gamma: gamma, primaries: primaries)
            let use = LutResolver.resolve(videoClip(), info: ClipInfo(), clipFullPath: clip, settings: auto(), catalog: cat)
            XCTAssertEqual(use.source, "auto", gamma)
            XCTAssertEqual(use.id, id, gamma)
            XCTAssertTrue(use.reason.contains(gamma), use.reason)
            XCTAssertFalse(use.reason.hasSuffix("/"), use.reason)
            XCTAssertTrue(FileManager.default.fileExists(atPath: use.file ?? ""), id)
        }
    }

    // Review focus 7: a clip the camera recorded in Rec.709 never gets a log LUT, not even the fallback.
    func testSonyRec709NoLut() {
        let dir = tempDir()
        let clip = sonyXML(dir, "PRIVATE/M4ROOT/CLIP/C0002.MP4", gamma: "rec709", primaries: "rec709")
        let use = LutResolver.resolve(videoClip("PRIVATE/M4ROOT/CLIP/C0002.MP4"), info: ClipInfo(), clipFullPath: clip,
                                      settings: auto(fallback: "sony-slog3-sgamut3cine"), catalog: cat)
        XCTAssertEqual(use.source, "none")
        XCTAssertNil(use.file)
        XCTAssertTrue(use.reason.contains("rec709"), use.reason)
    }

    // S-Log2 / S-Log is log, just without a built-in LUT — only the rest is "not log".
    func testSonyOtherGammaNoLutWithHonestReason() {
        for (gamma, reason) in [("s-log2", "Sony XML: s-log2 (bez vestavěného LUTu)"), ("S-Log", "Sony XML: S-Log (bez vestavěného LUTu)"),
                                ("hlg2", "Sony XML: hlg2 (není log)"), ("s-cinetone", "Sony XML: s-cinetone (není log)")] {
            let dir = tempDir()
            let clip = sonyXML(dir, "XDROOT/Clip/C0003.MP4", gamma: gamma, primaries: "rec709")
            let use = LutResolver.resolve(videoClip(), info: ClipInfo(), clipFullPath: clip,
                                          settings: auto(fallback: "sony-slog3-sgamut3cine"), catalog: cat)
            XCTAssertEqual(use.source, "none", gamma)
            XCTAssertEqual(use.reason, reason)
        }
    }

    func testSonyUpperCaseAndHyphenatedPrimaries() {
        let dir = tempDir()
        let clip = sonyXML(dir, "XDROOT/Clip/C0004.MP4", gamma: "S-LOG3-CINE", primaries: "s-gamut3-cine")
        XCTAssertEqual(LutResolver.resolve(videoClip(), info: ClipInfo(), clipFullPath: clip, settings: auto(), catalog: cat).id,
                       "sony-slog3-sgamut3cine")
    }

    func testSonyXMLMissingOrBroken() throws {
        let dir = tempDir()
        XCTAssertNil(SonyXML.read(dir.path + "/C0001.MP4"))
        try "<NonRealTimeMeta><Item".write(toFile: dir.path + "/C0001M01.XML", atomically: true, encoding: .utf8)
        XCTAssertNil(SonyXML.read(dir.path + "/C0001.MP4"))
    }

    func testArriFromCameraModel() {
        for (camera, id) in [("ARRI ALEXA 35", "arri-logc4-awg4"), ("ALEXA Mini LF", "arri-logc3-awg3"), ("AMIRA", "arri-logc3-awg3"),
                             ("ALEXA Mini LF 35mm", "arri-logc3-awg3"), ("ALEXA35", "arri-logc4-awg4")] {
            let use = LutResolver.resolve(videoClip("A001C001.mov"), info: info(camera: camera), clipFullPath: "/nonexistent/A001C001.mov",
                                          settings: auto(), catalog: cat)
            XCTAssertEqual(use.id, id, camera)
            let raw = LutResolver.resolve(rawClip(.arriraw, "A/A.ari"), info: info(camera: camera), clipFullPath: "/nonexistent/A",
                                          settings: auto(), catalog: cat)
            XCTAssertEqual(raw.id, id, "ARRIRAW " + camera)
        }
    }

    func testArrirawUsesLogEncoding() {
        let clip = rawClip(.arriraw, "A1/A1.ari")
        XCTAssertEqual(LutResolver.resolve(clip, info: info(log: "LogC4"), clipFullPath: "/x/A1", settings: auto(), catalog: cat).id, "arri-logc4-awg4")
        XCTAssertEqual(LutResolver.resolve(clip, info: info(camera: "ALEXA Mini"), clipFullPath: "/x/A1", settings: auto(), catalog: cat).id, "arri-logc3-awg3")
    }

    func testArriColorSpace() {
        let cases: [(String?, String?, String?, String)] = [
            ("ARRI_LogC4-to-Gamma24_Rec709.cube", nil, nil, "AWG4/D65/LogC4"),
            ("show_LogC3_709.cube", "LogC4", nil, "AWG3/D65/LogC3"),
            (nil, "LogC4", nil, "AWG4/D65/LogC4"),
            (nil, nil, "ALEXA Mini", "AWG3/D65/LogC3"),
            (nil, nil, "ALEXA 35", "AWG4/D65/LogC4"),
        ]
        for (lut, log, camera, expected) in cases {
            XCTAssertEqual(ARRIRAWBackend.colorSpace(lut: lut, info: info(camera: camera, log: log)), expected)
        }
    }

    func testBrawAndR3D() {
        XCTAssertEqual(LutResolver.resolve(rawClip(.braw, "B.braw"), info: nil, clipFullPath: "/x/B.braw", settings: auto(), catalog: cat).id,
                       "bmd-film-gen5")
        XCTAssertEqual(LutResolver.resolve(rawClip(.r3d, "R.RDC/R_001.R3D"), info: nil, clipFullPath: "/x/R", settings: auto(), catalog: cat).id,
                       "red-log3g10-rwg")
    }

    func testUnknownCameraUsesTheFallback() {
        let clip = videoClip("DCIM/100CANON/MVI_0001.MP4")
        let none = LutResolver.resolve(clip, info: info(camera: "Canon EOS R5"), clipFullPath: "/x/x.mp4", settings: auto(), catalog: cat)
        XCTAssertEqual(none.source, "none")
        XCTAssertEqual(none.reason, "kamera nerozpoznána")
        let fb = LutResolver.resolve(clip, info: ClipInfo(), clipFullPath: "/x/x.mp4", settings: auto(fallback: "canon-clog3-cinemagamut"), catalog: cat)
        XCTAssertEqual(fb.source, "fallback")
        XCTAssertEqual(fb.id, "canon-clog3-cinemagamut")
        XCTAssertEqual(fb.reason, "kamera nerozpoznána — záložní LUT")
    }

    func testModesBuiltinCustomOff() {
        let clip = videoClip()
        var b = ProxySettings()
        b.lutMode = .builtin
        b.lutBuiltin = "dji-dlog-dgamut"
        let builtin = LutResolver.resolve(clip, info: nil, clipFullPath: "/x", settings: b, catalog: cat)
        XCTAssertEqual([builtin.source, builtin.id, builtin.reason], ["builtin", "dji-dlog-dgamut", "vybraný v nastavení"])
        var c = ProxySettings()
        c.lutMode = .custom
        c.lutPath = "/Users/x/luts/show.cube"
        let custom = LutResolver.resolve(clip, info: nil, clipFullPath: "/x", settings: c, catalog: cat)
        XCTAssertEqual(custom, LutUse(source: "custom", id: nil, name: "show.cube", reason: "vlastní LUT", file: "/Users/x/luts/show.cube"))
        var n = ProxySettings()
        n.lutMode = .off
        let off = LutResolver.resolve(clip, info: nil, clipFullPath: "/x", settings: n, catalog: cat)
        XCTAssertEqual([off.source, off.name, off.reason], ["none", "bez LUT", "vypnuto v nastavení"])
        // No mode stored (older settings): a lutPath means custom, otherwise automatic.
        var old = ProxySettings()
        old.lutPath = "/l.cube"
        XCTAssertEqual(LutResolver.resolve(clip, info: nil, clipFullPath: "/x", settings: old, catalog: cat).source, "custom")
        XCTAssertEqual(LutResolver.effectiveMode(ProxySettings()), .auto)
    }

    func testEmptyLutPathIsNotCustom() {
        var s = ProxySettings()
        s.lutPath = ""
        XCTAssertEqual(LutResolver.effectiveMode(s), .auto)
        s.lutMode = .custom
        XCTAssertEqual(LutResolver.resolve(videoClip(), info: nil, clipFullPath: "/x", settings: s, catalog: cat).reason,
                       "vlastní LUT není vybraný")
    }

    func testBuiltinIdMissingFromTheCatalog() {
        var s = ProxySettings()
        s.lutMode = .builtin
        s.lutBuiltin = "gone"
        XCTAssertEqual(LutResolver.resolve(videoClip(), info: nil, clipFullPath: "/x", settings: s, catalog: cat).reason,
                       "vestavěný LUT nenalezen")
    }

    func testAutoWithoutCatalogNoLut() {
        let dir = tempDir()
        let clip = sonyXML(dir, "XDROOT/Clip/C0001.MP4", gamma: "s-log3-cine", primaries: "S-Gamut3Cine")
        let use = LutResolver.resolve(videoClip(), info: nil, clipFullPath: clip, settings: auto(), catalog: LutCatalog.load(nil))
        XCTAssertEqual(use.source, "none")
        XCTAssertEqual(use.reason, "vestavěné LUTy chybí")
    }

    func testLutUseJSONHasNoFile() throws {
        var s = ProxySettings()
        s.lutMode = .builtin
        s.lutBuiltin = "dji-dlog-dgamut"
        let use = LutResolver.resolve(videoClip(), info: nil, clipFullPath: "/x", settings: s, catalog: cat)
        XCTAssertNotNil(use.file)
        let json = String(decoding: try JSONEncoder().encode(use), as: UTF8.self)
        XCTAssertFalse(json.contains("file"))
        XCTAssertFalse(json.contains(".cube"))
        XCTAssertTrue(json.contains(#""source":"builtin""#))
        let back = try JSONDecoder().decode(LutUse.self, from: Data(json.utf8))
        XCTAssertEqual(back.id, "dji-dlog-dgamut")
        XCTAssertNil(back.file)
    }

    func testFFProbeReadsColorRangeAndMakePlusModel() throws {
        let range = try FFProbe.parse(Data(#"{"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"color_range":"pc"}],"format":{}}"#.utf8))
        XCTAssertEqual(range.colorRange, "pc")
        let arri = try FFProbe.parse(Data(#"{"streams":[{"codec_type":"video","codec_name":"prores"}],"format":{"tags":{"com.apple.quicktime.make":"ARRI","com.apple.quicktime.model":"ALEXA 35"}}}"#.utf8))
        XCTAssertEqual(arri.camera, "ARRI ALEXA 35")
        XCTAssertEqual(FFProbe.camera(make: "ARRI", model: "ARRI ALEXA 35"), "ARRI ALEXA 35")
        XCTAssertEqual(FFProbe.camera(make: " Sony ", model: nil), "Sony")
        XCTAssertEqual(FFProbe.camera(make: "", model: "ILME-FX6"), "ILME-FX6")
        XCTAssertNil(FFProbe.camera(make: nil, model: "  "))
    }
}
```

`mac/Tests/OffloadKitTests/LutOptionTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class LutOptionTests: XCTestCase {
    private let cat = TestMedia.luts

    private func parsed(_ mode: LutMode, builtin: String? = nil, path: String? = nil, fallback: String? = nil) -> LutOption.Parsed {
        LutOption.Parsed(mode: mode, builtin: builtin, path: path, fallback: fallback, error: nil)
    }

    func testParse() {
        XCTAssertEqual(LutOption.parse(nil, fallback: nil, catalog: cat), parsed(.auto))
        XCTAssertEqual(LutOption.parse("AUTO", fallback: "dji-dlog-dgamut", catalog: cat), parsed(.auto, fallback: "dji-dlog-dgamut"))
        XCTAssertEqual(LutOption.parse("none", fallback: nil, catalog: cat), parsed(.off))
        XCTAssertEqual(LutOption.parse("sony-slog3-sgamut3", fallback: nil, catalog: cat), parsed(.builtin, builtin: "sony-slog3-sgamut3"))
        XCTAssertEqual(LutOption.parse("/Users/x/show.cube", fallback: nil, catalog: cat), parsed(.custom, path: "/Users/x/show.cube"))
        XCTAssertEqual(LutOption.parse("look.CUBE", fallback: nil, catalog: cat), parsed(.custom, path: "look.CUBE"))
        XCTAssertEqual(LutOption.parse("luts/x", fallback: nil, catalog: cat).mode, .custom)
        XCTAssertTrue(LutOption.parse("nope", fallback: nil, catalog: cat).error?.hasPrefix("Unknown LUT 'nope'. Use auto, none, a .cube path or one of: arri-logc3-awg3, ") == true)
        XCTAssertTrue(LutOption.parse("auto", fallback: "nope", catalog: cat).error?.hasPrefix("Unknown fallback LUT 'nope'. Built-in: ") == true)
        XCTAssertNil(LutOption.parse("auto", fallback: "none", catalog: cat).fallback)
        XCTAssertNil(LutOption.parse("auto", fallback: "None", catalog: cat).error)
    }
}
```

`mac/Tests/OffloadKitTests/LutSettingsTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class LutSettingsTests: XCTestCase {
    private func load(_ json: String, _ catalog: LutCatalog = TestMedia.luts) -> AppSettings {
        SettingsStore.decode(Data(json.utf8), catalog: catalog)
    }

    // Settings from before the built-in LUTs: a lutPath stays the user's own LUT, without one the choice is automatic.
    func testOldSettingsMigrate() {
        let old = load(#"{"proxy":{"enabled":true,"lutPath":"/Users/x/show.cube"}}"#)
        XCTAssertEqual(old.proxy.lutMode, .custom)
        XCTAssertEqual(old.proxy.lutPath, "/Users/x/show.cube")
        let fresh = load(#"{"proxy":{"enabled":true}}"#)
        XCTAssertEqual(fresh.proxy.lutMode, .auto)
        XCTAssertNil(fresh.proxy.lutFallback)
    }

    func testRoundTripAndUnknownBuiltinDropped() throws {
        var s = AppSettings()
        s.proxy.lutMode = .builtin
        s.proxy.lutBuiltin = "dji-dlog-dgamut"
        s.proxy.lutFallback = "sony-slog3-sgamut3cine"
        let data = try XCTUnwrap(SettingsStore.encode(s))
        XCTAssertTrue(String(decoding: data, as: UTF8.self).contains(#""lutMode":"builtin""#))
        let back = SettingsStore.decode(data, catalog: TestMedia.luts)
        XCTAssertEqual(back.proxy.lutMode, .builtin)
        XCTAssertEqual(back.proxy.lutBuiltin, "dji-dlog-dgamut")
        XCTAssertEqual(back.proxy.lutFallback, "sony-slog3-sgamut3cine")
        let bad = load(#"{"proxy":{"lutMode":"builtin","lutBuiltin":"nope","lutFallback":"nope"}}"#)
        XCTAssertNil(bad.proxy.lutBuiltin)
        XCTAssertNil(bad.proxy.lutFallback)
        XCTAssertEqual(bad.proxy.lutMode, .auto)
        XCTAssertEqual(load(#"{"proxy":{"lutMode":"none"}}"#).proxy.lutMode, .off)
        XCTAssertEqual(load(#"{"proxy":{"lutMode":"future"},"verify":false}"#).proxy.lutMode, .auto)   // unknown → as if absent
    }

    // An empty catalog (luts folder missing in a broken install) must not erase saved choices.
    func testEmptyCatalogKeepsBuiltinChoices() {
        let s = load(#"{"proxy":{"lutMode":"builtin","lutBuiltin":"dji-dlog-dgamut","lutFallback":"sony-slog3-sgamut3cine"}}"#,
                     LutCatalog.load(nil))
        XCTAssertEqual(s.proxy.lutMode, .builtin)
        XCTAssertEqual(s.proxy.lutBuiltin, "dji-dlog-dgamut")
        XCTAssertEqual(s.proxy.lutFallback, "sony-slog3-sgamut3cine")
    }

    // Built-in LUTs ship with the app: only the user's own .cube can be missing and block the start.
    func testMissingLutBlocksOnlyInCustomMode() {
        var p = ProxySettings()
        p.enabled = true
        p.lutMode = .auto
        p.lutPath = "/gone.cube"
        XCTAssertNil(ProxyPreflight.lutInUse(p, analyzeClips: true))
        XCTAssertNil(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: false))
        p.lutMode = .custom
        XCTAssertEqual(ProxyPreflight.lutInUse(p, analyzeClips: true), "/gone.cube")
        XCTAssertEqual(ProxyPreflight.blocking(p, analyzeClips: true, lutExists: false), "LUT nenalezen: gone.cube — vyberte jiný nebo ho odeberte.")
    }
}
```

`mac/Tests/OffloadKitTests/SettingsStoreTests.swift` (úkol 10), `testRoundTrip` — normalizace teď doplní chybějící režim
(`lutPath` → „vlastní“), takže výchozí hodnota s `lutPath` musí mít režim nastavený. Za řádek
`s.proxy.lutPath = "/Users/x/look.cube"` přidat:

```swift
        s.proxy.lutMode = .custom   // what normalize derives from an old lutPath (task 21)
```

- [ ] **Step 3: Ověřit selhání** — Run: `swift test --filter "LutCatalogTests|LutResolverTests|LutOptionTests|LutSettingsTests"` →
nesestaví se (`cannot find 'LutCatalog'`, `'LutResolver'`, `'LutMode'`, `has no member 'colorRange'`, `'parse'`).

- [ ] **Step 4: `mac/Sources/OffloadKit/Luts.swift`**:

```swift
import Foundation

/// One built-in LUT from shared/luts/luts.json. `path` is filled in when the catalog is loaded.
public struct BuiltinLut: Codable, Equatable {
    public var id: String
    public var name: String
    public var maker: String
    public var file: String
    public var curve: String
    public var gamut: String
    /// Full path of the .cube (not in luts.json).
    public var path = ""

    enum CodingKeys: String, CodingKey { case id, name, maker, file, curve, gamut }

    /// Lenient like Windows: a missing field is empty; an entry without id or file is dropped by `LutCatalog.load`.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func text(_ k: CodingKeys) -> String { (try? c.decodeIfPresent(String.self, forKey: k)) ?? "" }
        id = text(.id)
        name = text(.name)
        maker = text(.maker)
        file = text(.file)
        curve = text(.curve)
        gamut = text(.gamut)
    }
}

/// The built-in LUTs shipped in Offload.app/Contents/Resources/luts or, in a development build, the repo's shared/luts.
public struct LutCatalog {
    public let folder: String?
    public let luts: [BuiltinLut]

    public init(folder: String?, luts: [BuiltinLut]) {
        self.folder = folder
        self.luts = luts
    }

    public func find(_ id: String?) -> BuiltinLut? {
        guard let id else { return nil }
        return luts.first { $0.id == id }
    }

    /// Loaded once, on first use (a few small local files in the app bundle). Never fails: no folder → empty catalog.
    public static let shared = LutCatalog.load(defaultFolder)

    public static var defaultFolder: String? {
        findFolder(executable: Bundle.main.executableURL?.resolvingSymlinksInPath(), resourcePath: Bundle.main.resourcePath)
    }

    /// The app bundle's Resources/luts; next to the executable (luts/, or ../luts/ for the CLI in Contents/Resources/bin);
    /// and only for a SwiftPM build (the executable under ".build/") the repo's shared/luts — an installed copy never
    /// picks up a repository it happens to sit in.
    static func findFolder(executable: URL?, resourcePath: String?) -> String? {
        func ok(_ dir: String) -> Bool { FileManager.default.fileExists(atPath: dir + "/luts.json") }
        var candidates: [String] = []
        if let resourcePath { candidates.append(resourcePath + "/luts") }
        if let executable {
            let dir = executable.deletingLastPathComponent()
            candidates.append(dir.appendingPathComponent("luts").path)
            candidates.append(dir.deletingLastPathComponent().appendingPathComponent("luts").path)
        }
        if let hit = candidates.first(where: ok) { return hit }
        guard let executable, executable.path.contains("/.build/") else { return nil }
        var d = executable.deletingLastPathComponent()
        for _ in 0..<8 {
            d = d.deletingLastPathComponent()
            let repo = d.appendingPathComponent("shared/luts").path
            if ok(repo) { return repo }
        }
        return nil
    }

    /// Never throws: a missing or broken folder gives an empty catalog; entries whose file is missing are left out.
    public static func load(_ folder: String?) -> LutCatalog {
        guard let folder, let data = FileManager.default.contents(atPath: folder + "/luts.json"),
              let list = try? JSONDecoder().decode([BuiltinLut].self, from: data) else {
            return LutCatalog(folder: nil, luts: [])
        }
        let ok = list
            .filter { !$0.id.trimmingCharacters(in: .whitespaces).isEmpty && !$0.file.trimmingCharacters(in: .whitespaces).isEmpty }
            .map { l -> BuiltinLut in
                var l = l
                l.path = folder + "/" + l.file
                return l
            }
            .filter { FileManager.default.fileExists(atPath: $0.path) }
        return LutCatalog(folder: ok.isEmpty ? nil : folder, luts: ok)
    }

    /// A 3D .cube whose data rows match its LUT_3D_SIZE.
    public static func isValidCube(_ path: String) -> Bool {
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else { return false }
        var size = 0, rows = 0
        for raw in text.split(whereSeparator: \.isNewline) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("LUT_3D_SIZE") {
                guard let n = Int(line.dropFirst("LUT_3D_SIZE".count).trimmingCharacters(in: .whitespaces)) else { return false }
                size = n
            } else if let c = line.first, c.isASCII, c.isNumber || c == "-" || c == "." {
                rows += 1
            }
        }
        return size >= 2 && rows == size * size * size
    }
}

/// Which LUT source applies (`proxy.lutMode`). JSON values "auto", "builtin", "custom", "none" — shared with Windows.
/// `off` (not `none`): a `LutMode?` compared with `.none` would silently mean Optional.none.
public enum LutMode: String, Codable, CaseIterable {
    case auto, builtin, custom
    case off = "none"
}

/// The LUT a clip got, and why — goes into the report (key "lut"). `file` is the .cube to apply (never serialised).
public struct LutUse: Codable, Equatable {
    public let source: String
    public let id: String?
    public let name: String
    public let reason: String
    public var file: String?

    enum CodingKeys: String, CodingKey { case source, id, name, reason }

    public init(source: String, id: String?, name: String, reason: String, file: String? = nil) {
        self.source = source
        self.id = id
        self.name = name
        self.reason = reason
        self.file = file
    }

    public var isBuiltin: Bool { id != nil }

    /// No LUT, with the reason for the report (source "none").
    public static func off(_ reason: String) -> LutUse { LutUse(source: "none", id: nil, name: "bez LUT", reason: reason) }
}

/// Sony XAVC sidecar "<clip>M01.XML" (NonRealTimeMeta): the gamma and primaries the camera recorded in.
public enum SonyXML {
    public static func read(_ clipFullPath: String) -> (gamma: String?, primaries: String?)? {
        let clip = URL(fileURLWithPath: clipFullPath)
        let xml = clip.deletingLastPathComponent().appendingPathComponent(clip.deletingPathExtension().lastPathComponent + "M01.XML")
        guard FileManager.default.fileExists(atPath: xml.path), let parser = XMLParser(contentsOf: xml) else { return nil }
        let items = ItemCollector()
        parser.delegate = items
        guard parser.parse() else { return nil }
        return ((items.values["CaptureGammaEquation"] ?? nil), (items.values["CaptureColorPrimaries"] ?? nil))
    }

    /// The first element carrying each `name` attribute, and its `value` attribute (namespace-agnostic).
    private final class ItemCollector: NSObject, XMLParserDelegate {
        var values: [String: String?] = [:]

        func parser(_ parser: XMLParser, didStartElement elementName: String, namespaceURI: String?,
                    qualifiedName qName: String?, attributes attributeDict: [String: String] = [:]) {
            if let name = attributeDict["name"], values.index(forKey: name) == nil { values[name] = .some(attributeDict["value"]) }
        }
    }
}

/// Which LUT a clip gets (LUT design §3; port of Windows `LutResolver`). Pure rules over metadata; never throws.
public enum LutResolver {
    /// The stored mode, or for older settings: a non-empty lutPath means custom, otherwise automatic.
    public static func effectiveMode(_ s: ProxySettings) -> LutMode {
        s.lutMode ?? ((s.lutPath ?? "").isEmpty ? .auto : .custom)
    }

    public static func resolve(_ clip: Clip, info: ClipInfo?, clipFullPath: String, settings s: ProxySettings,
                               catalog: LutCatalog) -> LutUse {
        switch effectiveMode(s) {
        case .off:
            return .off("vypnuto v nastavení")
        case .custom:
            guard let path = s.lutPath, !path.isEmpty else { return .off("vlastní LUT není vybraný") }
            return LutUse(source: "custom", id: nil, name: (path as NSString).lastPathComponent, reason: "vlastní LUT", file: path)
        case .builtin:
            return builtin(catalog, s.lutBuiltin, source: "builtin", reason: "vybraný v nastavení") ?? .off("vestavěný LUT nenalezen")
        case .auto:
            break
        }
        if catalog.luts.isEmpty { return .off("vestavěné LUTy chybí") }
        let (id, reason) = detect(clip, info: info, clipFullPath: clipFullPath)
        if id == "" { return .off(reason) }   // recognised, but not log footage
        if let id { return builtin(catalog, id, source: "auto", reason: reason) ?? .off("vestavěný LUT nenalezen") }
        return builtin(catalog, s.lutFallback, source: "fallback", reason: "kamera nerozpoznána — záložní LUT")
            ?? .off("kamera nerozpoznána")
    }

    private static func builtin(_ catalog: LutCatalog, _ id: String?, source: String, reason: String) -> LutUse? {
        guard let lut = catalog.find(id) else { return nil }
        return LutUse(source: source, id: lut.id, name: lut.name, reason: reason, file: lut.path)
    }

    /// (id, reason): id nil = not recognised, "" = recognised as non-log (no LUT).
    static func detect(_ clip: Clip, info: ClipInfo?, clipFullPath: String) -> (id: String?, reason: String) {
        switch clip.format {
        case .braw:
            return ("bmd-film-gen5", "Blackmagic RAW (gama Film Gen 5)")
        case .r3d:
            return ("red-log3g10-rwg", "RED R3D (IPP2 Log3G10)")
        case .arriraw:
            return ARRIRAWBackend.colorSpace(lut: nil, info: info ?? ClipInfo()).contains("LogC4")
                ? ("arri-logc4-awg4", "ARRIRAW (LogC4)") : ("arri-logc3-awg3", "ARRIRAW (LogC3)")
        default:
            break
        }
        if let xml = SonyXML.read(clipFullPath) {
            let gamma = xml.gamma ?? "", primaries = xml.primaries ?? ""
            let g = gamma.lowercased(), p = primaries.lowercased()
            if g.hasPrefix("s-log3") {
                let cine = p.contains("cine") || (p.isEmpty && g.hasSuffix("cine"))
                var reason = "Sony XML: \(gamma) / \(primaries)"
                while reason.hasSuffix(" ") || reason.hasSuffix("/") { reason.removeLast() }
                return (cine ? "sony-slog3-sgamut3cine" : "sony-slog3-sgamut3", reason)
            }
            // S-Log2 / S-Log is log too, there is just no built-in LUT for it; the rest (rec709, hlg, s-cinetone) is not log.
            if g.hasPrefix("s-log") { return ("", "Sony XML: \(gamma) (bez vestavěného LUTu)") }
            if !g.isEmpty { return ("", "Sony XML: \(gamma) (není log)") }
        }
        let camera = (info?.camera ?? "").uppercased()
        if camera.contains("ALEXA") || camera.contains("AMIRA") {
            return ARRIRAWBackend.isAlexa35(camera)
                ? ("arri-logc4-awg4", "ARRI kamera (předpoklad LogC4)") : ("arri-logc3-awg3", "ARRI kamera (předpoklad LogC3)")
        }
        return (nil, "kamera nerozpoznána")
    }
}

/// `--lut` / `--lut-fallback` of the CLI: auto, none, a built-in id, or a path to a .cube (anything ending in ".cube"
/// or containing a path separator).
public enum LutOption {
    public struct Parsed: Equatable {
        public var mode: LutMode
        public var builtin: String?
        public var path: String?
        public var fallback: String?
        public var error: String?
    }

    public static func parse(_ value: String?, fallback: String?, catalog: LutCatalog) -> Parsed {
        let ids = catalog.luts.map(\.id).joined(separator: ", ")
        var fb: String?
        if let fallback, fallback.lowercased() != "none" {
            guard catalog.find(fallback) != nil else {
                return Parsed(mode: .auto, error: "Unknown fallback LUT '\(fallback)'. Built-in: \(ids)")
            }
            fb = fallback
        }
        guard let value, value.lowercased() != "auto" else { return Parsed(mode: .auto, fallback: fb) }
        if value.lowercased() == "none" { return Parsed(mode: .off, fallback: fb) }
        if value.lowercased().hasSuffix(".cube") || value.contains("/") || value.contains("\\") {
            return Parsed(mode: .custom, path: value, fallback: fb)
        }
        return catalog.find(value) != nil
            ? Parsed(mode: .builtin, builtin: value, fallback: fb)
            : Parsed(mode: .auto, fallback: fb, error: "Unknown LUT '\(value)'. Use auto, none, a .cube path or one of: \(ids)")
    }
}
```

(`Parsed` má syntetizovaný členský inicializátor; volitelné vlastnosti v něm mají výchozí `nil`, takže
`Parsed(mode: .auto, error: …)` i `Parsed(mode:builtin:path:fallback:error:)` v testu fungují bez dalšího kódu.)

- [ ] **Step 5: `Media.swift`**

V `ClipInfo` za `squeeze` přidat:

```swift
    /// "tv" (legal) or "pc" (full) as ffprobe reports it; full-range sources need a pre-map before built-in LUTs.
    public var colorRange: String?
```

`enum FFProbe` nahradit celý:

```swift
enum FFProbe {
    static func probe(_ ffprobe: String, _ path: String) throws -> ClipInfo {
        let out = try ToolProcess.run(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path])
        return try parse(Data(out.utf8))
    }

    static func parse(_ data: Data) throws -> ClipInfo {
        guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw ToolError(tool: "ffprobe", status: 0, message: "unreadable output")
        }
        let streams = json["streams"] as? [[String: Any]] ?? []
        let format = json["format"] as? [String: Any] ?? [:]
        var info = ClipInfo()
        if let v = streams.first(where: { $0["codec_type"] as? String == "video" && ($0["disposition"] as? [String: Any])?["attached_pic"] as? Int != 1 }) {
            let name = v["codec_name"] as? String
            let profile = v["profile"] as? String
            info.codec = [name, profile].compactMap { $0 }.joined(separator: " ")
            // ffmpeg reports codecs it cannot decode (ARRIRAW, X-OCN …) as "unknown", 0×0.
            if name != nil && name != "unknown" {
                info.width = (v["width"] as? Int).flatMap { $0 > 0 ? $0 : nil }
                info.height = (v["height"] as? Int).flatMap { $0 > 0 ? $0 : nil }
            }
            if let r = v["avg_frame_rate"] as? String, r != "0/0" { info.frameRate = r } else { info.frameRate = v["r_frame_rate"] as? String }
            info.frames = (v["nb_frames"] as? String).flatMap(Int.init)
            info.timecode = (v["tags"] as? [String: Any])?["timecode"] as? String
            info.colorRange = v["color_range"] as? String
            if info.duration == nil { info.duration = (v["duration"] as? String).flatMap(Double.init) }
        }
        info.audioChannels = streams.filter { $0["codec_type"] as? String == "audio" }.reduce(0) { $0 + ($1["channels"] as? Int ?? 0) }
        if let d = (format["duration"] as? String).flatMap(Double.init) { info.duration = d }
        let ftags = format["tags"] as? [String: Any] ?? [:]
        if info.timecode == nil {
            info.timecode = (ftags["timecode"] as? String)
                ?? streams.lazy.compactMap { ($0["tags"] as? [String: Any])?["timecode"] as? String }.first
        }
        info.camera = camera(make: ftags["com.apple.quicktime.make"] as? String, model: ftags["com.apple.quicktime.model"] as? String)
            ?? (ftags["product_name"] as? String) ?? (ftags["company_name"] as? String)
        return info
    }

    /// Make and model together ("ARRI" + "ALEXA 35" → "ARRI ALEXA 35"), unless the model already starts with the make.
    static func camera(make: String?, model: String?) -> String? {
        let mk = make?.trimmingCharacters(in: .whitespaces), md = model?.trimmingCharacters(in: .whitespaces)
        let m = (mk?.isEmpty ?? true) ? nil : mk, n = (md?.isEmpty ?? true) ? nil : md
        guard let m, let n else { return m ?? n }
        return n.lowercased().hasPrefix(m.lowercased()) ? n : m + " " + n
    }
}
```

- [ ] **Step 6: `Proxy.swift`** — `public struct ProxySettings` nahradit celou (přibývá `lutMode`, `lutBuiltin`, `lutFallback`;
`extraFolder` je z úkolu 20):

```swift
public struct ProxySettings: Codable, Equatable {
    public var enabled = false
    public var height = 1080
    public var codec: ProxyCodec = .h264
    public var bitrateMbps = 10
    /// The user's own .cube (mode custom), applied after downscaling (cheap at proxy resolution).
    public var lutPath: String?
    /// Which LUT source applies; nil = not stored (older settings): a lutPath means custom, otherwise automatic.
    public var lutMode: LutMode?
    /// Built-in LUT id used in builtin mode.
    public var lutBuiltin: String?
    /// Built-in LUT id used in auto mode when the camera is not recognised (nil = no LUT).
    public var lutFallback: String?
    public var burnFilename = true
    public var burnTimecode = true
    public var audio = true
    /// Skip the low-res files cameras record themselves (Sony SUB, PROXY folders).
    public var skipCameraProxies = true
    /// Proxies are rendered once, onto the first destination, then copied to the others.
    public var copyToAllDestinations = true
    /// Optional: every proxy is also copied to `<folder>/<date>/<card>/` (never overwriting). nil = off (key absent).
    public var extraFolder: String?
    /// Crop to the frame lines: aspect ratio of the frame (e.g. 1.66) and how much of the image
    /// it covers (0.9 = 90 %, i.e. 10 % overscan around it). nil = full image.
    public var frameAspect: Double?
    public var frameScale: Double = 1.0

    public init() {}

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = ProxySettings()
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? d.enabled
        height = try c.decodeIfPresent(Int.self, forKey: .height) ?? d.height
        codec = (try? c.decodeIfPresent(ProxyCodec.self, forKey: .codec)) ?? d.codec
        bitrateMbps = try c.decodeIfPresent(Int.self, forKey: .bitrateMbps) ?? d.bitrateMbps
        lutPath = try c.decodeIfPresent(String.self, forKey: .lutPath)
        // An unknown mode (a newer version) reads as "not stored" — normalize then derives it like for old settings.
        lutMode = try? c.decodeIfPresent(LutMode.self, forKey: .lutMode)
        lutBuiltin = try c.decodeIfPresent(String.self, forKey: .lutBuiltin)
        lutFallback = try c.decodeIfPresent(String.self, forKey: .lutFallback)
        burnFilename = try c.decodeIfPresent(Bool.self, forKey: .burnFilename) ?? d.burnFilename
        burnTimecode = try c.decodeIfPresent(Bool.self, forKey: .burnTimecode) ?? d.burnTimecode
        audio = try c.decodeIfPresent(Bool.self, forKey: .audio) ?? d.audio
        skipCameraProxies = try c.decodeIfPresent(Bool.self, forKey: .skipCameraProxies) ?? d.skipCameraProxies
        copyToAllDestinations = try c.decodeIfPresent(Bool.self, forKey: .copyToAllDestinations) ?? d.copyToAllDestinations
        extraFolder = try c.decodeIfPresent(String.self, forKey: .extraFolder)
        frameAspect = try c.decodeIfPresent(Double.self, forKey: .frameAspect)
        frameScale = try c.decodeIfPresent(Double.self, forKey: .frameScale) ?? d.frameScale
    }

    /// Centered crop rectangle (x, y, w, h) inside a w×h image, even-sized, or nil when no crop is set.
    public func frameCrop(width W: Int, height H: Int) -> (x: Int, y: Int, w: Int, h: Int)? {
        guard let aspect = frameAspect, aspect > 0, W > 0, H > 0 else { return nil }
        let scale = min(max(frameScale, 0.1), 1.0)
        var w = Double(W) * scale
        var h = w / aspect
        if h > Double(H) * scale {
            h = Double(H) * scale
            w = h * aspect
        }
        let cw = Int(w) & ~1, ch = Int(h) & ~1
        return ((W - cw) / 2 & ~1, (H - ch) / 2 & ~1, cw, ch)
    }

    public var frameLabel: String? {
        guard let a = frameAspect else { return nil }
        return String(format: "%.2f", a) + (frameScale < 1 ? String(format: " · %.0f %%", frameScale * 100) : "")
    }
}
```

(Syntetizované `encode(to:)` vynechá `nil` klíče — `lutMode` zapíše vždy, jakmile ho nastaví normalizace nebo GUI.)

- [ ] **Step 7: `RawBackends.swift` (`ARRIRAWBackend`)** — instanční `func colorSpace(for lut: String?, info: ClipInfo) -> String`
nahradit statickými funkcemi a v `source(…)` volání `colorSpace(for: settings.lutPath, info: c)` nahradit
`ARRIRAWBackend.colorSpace(lut: settings.lutPath, info: c)` (úkol 22 tu větev přepíše na `RawLook`):

```swift
    /// The LUT decides which log it needs when its name says so; otherwise the clip's own log encoding
    /// from metadata, then the camera (ALEXA 35 family → LogC4, older ALEXAs → LogC3).
    static func colorSpace(lut: String?, info: ClipInfo) -> String {
        let name = ((lut ?? "") as NSString).lastPathComponent.lowercased()
        if name.contains("logc4") { return "AWG4/D65/LogC4" }
        if name.contains("logc3") || name.contains("logc ") || name.contains("logc_") || name.contains("logc-") { return "AWG3/D65/LogC3" }
        if let log = info.logEncoding?.lowercased() { return log.contains("4") ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3" }
        return isAlexa35(info.camera) ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3"
    }

    /// "ALEXA 35" / "ALEXA35" as a whole word — not "ALEXA Mini LF 35mm" or a serial number.
    static func isAlexa35(_ camera: String?) -> Bool {
        guard let camera else { return false }
        return camera.range(of: #"\bALEXA\s*35\b"#, options: [.regularExpression, .caseInsensitive]) != nil
    }
```

- [ ] **Step 8: `Desktop/AppSettings.swift`**

`SettingsStore.decode` — hlavička a poslední řádek:

```swift
    public static func decode(_ data: Data?, catalog: LutCatalog = .shared) -> AppSettings {
```

```swift
        return normalize(s, catalog: catalog)
```

`SettingsStore.normalize` nahradit celou:

```swift
    static func normalize(_ input: AppSettings, catalog: LutCatalog = .shared) -> AppSettings {
        var s = input
        var roots: [String] = []
        for r in s.destRoots.map({ $0.trimmingCharacters(in: .whitespaces) }) where !r.isEmpty {
            if !roots.contains(where: { $0.caseInsensitiveCompare(r) == .orderedSame }) { roots.append(r) }
        }
        s.destRoots = roots
        var names: [String: String] = [:]
        for (id, name) in s.portNames {
            let i = id.trimmingCharacters(in: .whitespaces), n = name.trimmingCharacters(in: .whitespaces)
            if !i.isEmpty && !n.isEmpty { names[i] = n }
        }
        s.portNames = names
        if !proxyHeights.contains(s.proxy.height) { s.proxy.height = 1080 }
        s.proxy.bitrateMbps = min(max(s.proxy.bitrateMbps, 2), 50)
        s.proxy.frameScale = s.proxy.frameScale.isFinite ? min(max(s.proxy.frameScale, 0.5), 1.0) : 1.0
        if let a = s.proxy.frameAspect, !a.isFinite || a <= 0 { s.proxy.frameAspect = nil }
        if let l = s.proxy.lutPath, l.trimmingCharacters(in: .whitespaces).isEmpty { s.proxy.lutPath = nil }
        // Settings from before the built-in LUTs: a lutPath stays the user's own LUT, otherwise automatic.
        if s.proxy.lutMode == nil { s.proxy.lutMode = s.proxy.lutPath != nil ? .custom : .auto }
        if !catalog.luts.isEmpty {   // an empty catalog (luts folder missing) must not erase saved choices
            if catalog.find(s.proxy.lutBuiltin) == nil { s.proxy.lutBuiltin = nil }
            if catalog.find(s.proxy.lutFallback) == nil { s.proxy.lutFallback = nil }
            if s.proxy.lutMode == .builtin && s.proxy.lutBuiltin == nil { s.proxy.lutMode = .auto }
        }
        if let f = s.proxy.extraFolder?.trimmingCharacters(in: .whitespaces) { s.proxy.extraFolder = f.isEmpty ? nil : f }
        return s
    }
```

`ProxyPreflight.lutInUse` nahradit:

```swift
    /// The user's own .cube the next start needs (only in custom mode — built-in LUTs ship with the app), or nil.
    public static func lutInUse(_ p: ProxySettings, analyzeClips: Bool) -> String? {
        (p.enabled || analyzeClips) && LutResolver.effectiveMode(p) == .custom ? p.lutPath : nil
    }
```

- [ ] **Step 9: Testy projdou** — Run: `swift test --filter "Lut|SettingsStoreTests"` → 0 chyb; pak `swift test`.

- [ ] **Step 10: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 21 — vestavěné LUTy: katalog (Offload.app/Contents/Resources/luts, při vývoji shared/luts), výběr LUTu podle klipu (Sony XML, ARRI výrobce + model, ARRIRAW, BRAW, RED, záložní LUT), nastavení lutMode/lutBuiltin/lutFallback (starý lutPath = vlastní LUT), ffprobe čte color_range a výrobce + model; chybějící LUT blokuje start jen u vlastního`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: built-in LUT catalog, per-clip LUT rules, LUT settings and options"`

---

### Task 22: LUT pro každý klip v proxy a náhledech, přepočet rozsahu, LUT v reportu

Doplněk ke spec — oddíl D.3.4–D.3.5. Port `ProxyRenderer.cs` (`PrepareLut`, `LookFor`, `NeedsPreMap`, `Thumbnails(…, lut)`,
`Render(…, lut, …)`), `RawBackends.cs` (`RawLook`, `FrameSource.FullRange`, `R3dBackend.Args`, `ArriBackend.Args`),
výběr LUTu v `OffloadJob.cs` a LUT v `HtmlReport.cs`.

**Files:**
- Modify: `mac/Sources/OffloadKit/Proxy.swift` — nahradit vše od komentáře `/// How frames reach ffmpeg` do konce souboru (`FrameSource`, nový `RawLook`, `RawBackend`, `ProxyRenderer`)
- Modify (celý soubor): `mac/Sources/OffloadKit/RawBackends.swift`
- Modify: `mac/Sources/OffloadKit/Report.swift` (`ClipEntry.lut`, `CodingKeys`)
- Modify: `mac/Sources/OffloadKit/Job.swift` (`luts`, `init(config:options:luts:)`, celé `analyzeAndRender`, `tryThumbnails`)
- Modify: `mac/Sources/OffloadKit/HTMLReport.swift` (CSS `.note`, celý blok klipů)
- Modify: `mac/Tests/OffloadKitTests/ColorTests.swift` (tři pomocné funkce z úkolu 18)
- Create: `mac/Tests/OffloadKitTests/LutRenderTests.swift`, `mac/Tests/OffloadKitTests/LutReportTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `LutUse`, `LutResolver`, `LutCatalog`, `ARRIRAWBackend.colorSpace(lut:info:)` (úkol 21), `FilterGraph` (úkol 18), `FFmpegStall`, `ProxyRetryFailed`, `ProgressWatch`, `ProxyRenderer.stallNote`, `usesHardwareDecode(_:)`, `killWait`, `log` (úkol 19), `ProxyCopy` (úkol 20), `Timecode`, `FramePosition` (úkol 5), `Thumbnail`, `BRAWBackend.startFrame` (úkol 6), `OffloadReport.isGood` (úkol 3).
- Produces:
  - `struct FrameSource { …; var fullRange = false; … }`, `struct RawLook: Equatable { let log: Bool; let arriColorSpace: String? }`
  - `protocol RawBackend { func source(_ path: String, info: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools, tempDir: String, singleFrameAt: Double?) throws -> FrameSource; … }`
  - `public func ProxyRenderer.thumbnails(_ clip: Clip, cardPath: String, info: ClipInfo, lut: LutUse? = nil, isCancelled: () -> Bool = { false }) throws -> [Thumbnail]` (místo `applyLUT:`)
  - `public func ProxyRenderer.render(_ clip: Clip, cardPath: String, info: ClipInfo, lut: LutUse? = nil, output: String, progress: @escaping (Double) -> Void, isCancelled: @escaping () -> Bool) throws`
  - `func ProxyRenderer.prepareLUT(_ lut: LutUse?) throws -> String?`, `static func ProxyRenderer.lookFor(_ lut: LutUse?, info: ClipInfo) -> RawLook`, `static func ProxyRenderer.needsPreMap(_ lut: LutUse?, source: FrameSource, info: ClipInfo) -> Bool`
  - `static func R3DBackend.args(input: String, outDir: String, info: ClipInfo, settings: ProxySettings, look: RawLook) -> [String]`
  - `static func ARRIRAWBackend.args(input: String, output: String, info: ClipInfo, settings: ProxySettings, look: RawLook, parametersFile: String) -> (args: [String], parameters: Data?, cropped: Bool)`
  - `public var ClipEntry.lut: LutUse?` (JSON `lut`), `let OffloadJob.luts: LutCatalog`, `init(config: JobConfig, options: OffloadOptions, luts: LutCatalog = .shared)`
  - zanikají: `ProxyRenderer.hasLUT`, `lutCopy`, `isValidTimecode`, parametr `applyLUT`

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/LutRenderTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class LutRenderTests: XCTestCase {
    private func builtin(_ id: String) -> LutUse { LutUse(source: "auto", id: id, name: "n", reason: "r", file: "/t/\(id).cube") }

    // Review focus 7: the pre-map only for a built-in LUT on a full-range source (BRAW RGB, video tagged pc).
    func testNeedsPreMapOnlyBuiltinLutOnFullRangeSource() {
        let braw = FrameSource(inputArgs: ["-i", "-"], fullRange: true)
        let video = FrameSource(inputArgs: ["-i", "a.mov"])
        var pc = ClipInfo()
        pc.colorRange = "pc"
        var tv = ClipInfo()
        tv.colorRange = "tv"
        XCTAssertTrue(ProxyRenderer.needsPreMap(builtin("bmd-film-gen5"), source: braw, info: ClipInfo()))
        XCTAssertTrue(ProxyRenderer.needsPreMap(builtin("sony-slog3-sgamut3"), source: video, info: pc))
        XCTAssertFalse(ProxyRenderer.needsPreMap(builtin("sony-slog3-sgamut3"), source: video, info: tv))
        XCTAssertFalse(ProxyRenderer.needsPreMap(builtin("sony-slog3-sgamut3"), source: video, info: ClipInfo()))
        XCTAssertFalse(ProxyRenderer.needsPreMap(TestMedia.customLut("/t/look.cube"), source: braw, info: ClipInfo()))   // custom: as is
        XCTAssertFalse(ProxyRenderer.needsPreMap(nil, source: braw, info: pc))
    }

    // RAW decoders hand over log only when a LUT follows; ARRI's target space follows a built-in ARRI LUT.
    func testLookForLogWhenAnyLutArriSpaceFromBuiltin() {
        var info = ClipInfo()
        info.camera = "ALEXA Mini"
        XCTAssertEqual(ProxyRenderer.lookFor(nil, info: info), RawLook(log: false, arriColorSpace: nil))
        XCTAssertEqual(ProxyRenderer.lookFor(.off("x"), info: info), RawLook(log: false, arriColorSpace: nil))
        XCTAssertEqual(ProxyRenderer.lookFor(builtin("arri-logc4-awg4"), info: info), RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4"))
        XCTAssertEqual(ProxyRenderer.lookFor(builtin("arri-logc3-awg3"), info: info), RawLook(log: true, arriColorSpace: "AWG3/D65/LogC3"))
        XCTAssertEqual(ProxyRenderer.lookFor(builtin("red-log3g10-rwg"), info: info), RawLook(log: true, arriColorSpace: nil))
        XCTAssertEqual(ProxyRenderer.lookFor(TestMedia.customLut("/l/show_LogC4_709.cube"), info: info),
                       RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4"))
        XCTAssertEqual(ProxyRenderer.lookFor(TestMedia.customLut("/l/look.cube"), info: info),
                       RawLook(log: true, arriColorSpace: "AWG3/D65/LogC3"))
    }

    func testR3DArgsPickResolutionAndLogOnlyWithLut() {
        var info = ClipInfo()
        info.height = 4320
        var s = ProxySettings()
        s.height = 1080
        XCTAssertEqual(R3DBackend.args(input: "/c/A.R3D", outDir: "/t/r3d", info: info, settings: s, look: RawLook(log: true, arriColorSpace: nil)),
                       ["--i", "/c/A.R3D", "--outDir", "/t/r3d", "--o", "intermediate", "--format", "201", "--PRcodec", "2", "--res", "4",
                        "--useMeta", "--colorSciVersion", "3", "--colorSpace", "25", "--gammaCurve", "34"])
        XCTAssertFalse(R3DBackend.args(input: "a", outDir: "b", info: info, settings: s, look: RawLook(log: false, arriColorSpace: nil))
            .contains("--gammaCurve"))
        info.height = 2160
        XCTAssertTrue(R3DBackend.args(input: "a", outDir: "b", info: info, settings: s, look: RawLook(log: false, arriColorSpace: nil)).contains("3"))
        info.height = 1080
        XCTAssertTrue(R3DBackend.args(input: "a", outDir: "b", info: info, settings: s, look: RawLook(log: false, arriColorSpace: nil)).contains("1"))
    }

    func testArriArgsCropOnSensorRec709WithoutLut() throws {
        var info = ClipInfo()
        info.width = 4608
        info.height = 3164
        var s = ProxySettings()
        s.frameAspect = 1.66
        s.height = 1080
        let a = ARRIRAWBackend.args(input: "/c/c.mxf", output: "/t/i.mxf", info: info, settings: s,
                                    look: RawLook(log: false, arriColorSpace: nil), parametersFile: "/t/p.json")
        XCTAssertTrue(a.cropped)
        XCTAssertEqual(a.args, ["process", "--input", "/c/c.mxf", "--video-codec", "prores422lt", "--deflip", "--output", "/t/i.mxf",
                                "--parameters", "/t/p.json", "--target-colorspace", "Rec.709/D65/BT.1886", "--embedded-look"])
        let p = try XCTUnwrap(try JSONSerialization.jsonObject(with: try XCTUnwrap(a.parameters)) as? [String: [String: Int]])
        XCTAssertEqual(p["inputCrop"], ["displayXOffsetOverride": 0, "displayYOffsetOverride": 194,
                                        "displayWidthOverride": 4608, "displayHeightOverride": 2774])
        XCTAssertEqual(p["outputScaling"], ["targetWidth": 1794, "targetHeight": 1080])
    }

    func testArriArgsWithoutCropDesqueezeAndLog() {
        var info = ClipInfo()
        info.camera = "ALEXA 35"
        var s = ProxySettings()
        s.height = 720
        let a = ARRIRAWBackend.args(input: "in", output: "out", info: info, settings: s,
                                    look: ProxyRenderer.lookFor(TestMedia.customLut("look.cube"), info: info), parametersFile: "p.json")
        XCTAssertFalse(a.cropped)
        XCTAssertNil(a.parameters)
        XCTAssertEqual(a.args, ["process", "--input", "in", "--video-codec", "prores422lt", "--deflip", "--output", "out",
                                "--desqueeze", "--output-height", "720", "--target-colorspace", "AWG4/D65/LogC4"])
        XCTAssertTrue(ARRIRAWBackend.args(input: "in", output: "out", info: ClipInfo(), settings: ProxySettings(),
                                          look: RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4"), parametersFile: "p").args.contains("AWG4/D65/LogC4"))
    }

    /// Mean Y (8-bit code values as stored) of the first frame, via signalstats.
    private func meanLuma(_ ffmpeg: String, _ video: String, _ dir: String) throws -> Double {
        let stats = dir + "/yavg-\(UUID().uuidString).txt"
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", video,
                                     "-vf", "signalstats,metadata=print:key=lavfi.signalstats.YAVG:file=\(stats)",
                                     "-frames:v", "1", "-f", "null", "-"])
        let text = try String(contentsOfFile: stats, encoding: .utf8)
        let line = try XCTUnwrap(text.split(whereSeparator: \.isNewline).first { $0.hasPrefix("lavfi.signalstats.YAVG=") })
        return try XCTUnwrap(Double(line.dropFirst("lavfi.signalstats.YAVG=".count)))
    }

    private func greyClip(_ ffmpeg: String, _ path: String, y: Int) throws {
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi",
                                     "-i", "color=c=0x696969:s=320x180:d=1:r=25", "-vf", "format=yuv420p,lutyuv=y=\(y):u=128:v=128",
                                     "-c:v", "libx264", "-color_range", "tv", path])
    }

    // Review focus 7 — real ffmpeg (H.264 source → VideoToolbox decode): an S-Log3 grey clip with its Sony XML renders a
    // proxy whose grey is ~0.42; a brighter level proves the LUT is really applied.
    func testSLog3GreyProxyIs042() throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        let clipDir = dir.sub("card", "XDROOT", "Clip")
        // S-Log3 grey = 10-bit code 420 → 8-bit legal Y ≈ 105; neutral chroma.
        try greyClip(ffmpeg, clipDir + "/C0001.MP4", y: 105)
        try greyClip(ffmpeg, clipDir + "/C0002.MP4", y: 150)
        try #"<NonRealTimeMeta><Item name="CaptureGammaEquation" value="s-log3-cine"/><Item name="CaptureColorPrimaries" value="S-Gamut3Cine"/></NonRealTimeMeta>"#
            .write(toFile: clipDir + "/C0001M01.XML", atomically: true, encoding: .utf8)
        var s = ProxySettings()
        s.enabled = true
        s.height = 180
        s.lutMode = .auto
        s.burnFilename = false
        s.burnTimecode = false
        let r = ProxyRenderer(settings: s, tools: TestMedia.tools)
        func clip(_ name: String) -> Clip {
            Clip(name: name, path: "XDROOT/Clip/\(name).MP4", format: .video, segments: ["XDROOT/Clip/\(name).MP4"], bytes: 1, isCameraProxy: false)
        }
        let info = try r.probe(clip("C0001"), cardPath: card)
        let lut = LutResolver.resolve(clip("C0001"), info: info, clipFullPath: clipDir + "/C0001.MP4", settings: s, catalog: TestMedia.luts)
        XCTAssertEqual(lut.id, "sony-slog3-sgamut3cine")
        try r.render(clip("C0001"), cardPath: card, info: info, lut: lut, output: dir.path + "/p.mov", progress: { _ in }, isCancelled: { false })
        XCTAssertEqual((try meanLuma(ffmpeg, dir.path + "/p.mov", dir.path) - 16) / 219, 0.42, accuracy: 0.03)
        // Grey maps almost onto itself, so also a level the LUT clearly moves: Y=150 (≈ 0.61 without a LUT) comes out
        // well above 0.75 through it. Fails if the LUT is dropped.
        let bright = try r.probe(clip("C0002"), cardPath: card)
        try r.render(clip("C0002"), cardPath: card, info: bright, lut: lut, output: dir.path + "/p2.mov", progress: { _ in }, isCancelled: { false })
        XCTAssertGreaterThanOrEqual((try meanLuma(ffmpeg, dir.path + "/p2.mov", dir.path) - 16) / 219, 0.75)
        try r.render(clip("C0002"), cardPath: card, info: bright, lut: nil, output: dir.path + "/p3.mov", progress: { _ in }, isCancelled: { false })
        let plain = (try meanLuma(ffmpeg, dir.path + "/p3.mov", dir.path) - 16) / 219
        XCTAssertTrue((0.58...0.64).contains(plain), "\(plain)")
    }

    func testBuiltinLutIsCopiedOncePerIdAndCustomOnlyInCustomMode() throws {
        let dir = tempDir()
        let cube = dir.path + "/my look.cube"
        TestMedia.identityLUT(cube)
        var s = ProxySettings()
        s.lutPath = cube
        s.lutMode = .auto   // a saved custom path does not count outside custom mode
        let auto = ProxyRenderer(settings: s, tools: ExternalTools())
        XCTAssertNil(try auto.prepareLUT(TestMedia.customLut(cube)))
        let lut = try XCTUnwrap(TestMedia.luts.find("dji-dlog-dgamut"))
        let use = LutUse(source: "builtin", id: lut.id, name: lut.name, reason: "r", file: lut.path)
        let first = try XCTUnwrap(try auto.prepareLUT(use))
        XCTAssertEqual(first, auto.tempDir + "/lut-dji-dlog-dgamut.cube")
        XCTAssertEqual(try auto.prepareLUT(use), first)
        s.lutMode = .custom
        let custom = ProxyRenderer(settings: s, tools: ExternalTools())
        XCTAssertEqual(try custom.prepareLUT(TestMedia.customLut(cube)), custom.tempDir + "/look.cube")   // a plain path, no space
        XCTAssertNil(try custom.prepareLUT(nil))
        XCTAssertNil(try custom.prepareLUT(.off("x")))
    }
}
```

`mac/Tests/OffloadKitTests/LutReportTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class LutReportTests: XCTestCase {
    private func report(lut: LutUse?, proxy: ProxySettings? = nil) -> OffloadReport {
        var r = Reports.sample()
        var e = ClipEntry(clip: Clip(name: "C0001", path: "C0001.MP4", format: .video, segments: ["C0001.MP4"], bytes: 1, isCameraProxy: false))
        e.lut = lut
        r.clips = [e]
        r.proxy = proxy
        return r
    }

    func testClipShowsLutAndJSONKey() throws {
        let r = report(lut: LutUse(source: "auto", id: "sony-slog3-sgamut3cine", name: "Sony S-Log3 / S-Gamut3.Cine → Rec.709",
                                   reason: "Sony XML: s-log3-cine / S-Gamut3Cine", file: "/x/lut.cube"))
        XCTAssertTrue(HTMLReport.render(r).contains(
            "<div class=\"note\">LUT: Sony S-Log3 / S-Gamut3.Cine → Rec.709 (Sony XML: s-log3-cine / S-Gamut3Cine)</div>"))
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: try r.jsonData()) as? [String: Any])
        let clip = try XCTUnwrap((obj["clips"] as? [[String: Any]])?.first)
        let lut = try XCTUnwrap(clip["lut"] as? [String: Any])
        XCTAssertEqual(lut.keys.sorted(), ["id", "name", "reason", "source"])
        XCTAssertEqual(lut["source"] as? String, "auto")
    }

    func testNoLutReasonShownUnlessSwitchedOff() throws {
        XCTAssertTrue(HTMLReport.render(report(lut: .off("kamera nerozpoznána"))).contains("<div class=\"note\">LUT: bez LUT (kamera nerozpoznána)</div>"))
        XCTAssertFalse(HTMLReport.render(report(lut: .off("vypnuto v nastavení"))).contains("LUT: bez LUT"))
        XCTAssertFalse(HTMLReport.render(report(lut: nil)).contains("class=\"note\""))
        let none = try XCTUnwrap(try JSONSerialization.jsonObject(with: try report(lut: .off("x")).jsonData()) as? [String: Any])
        let lut = try XCTUnwrap(((none["clips"] as? [[String: Any]])?.first)?["lut"] as? [String: Any])
        XCTAssertNil(lut["id"])   // omitted, like Windows (null is not written)
    }

    func testProxyLineSaysHowTheLutIsChosen() {
        var p = ProxySettings()
        p.enabled = true
        p.frameAspect = 1.66
        p.lutMode = .auto
        XCTAssertTrue(HTMLReport.render(report(lut: nil, proxy: p)).contains("Proxy: H.264, 1080p, výřez 1.66, LUT: automaticky podle kamery"))
        p.lutMode = .builtin
        p.lutBuiltin = "dji-dlog-dgamut"
        // The name comes from LutCatalog.shared — empty under xctest, so the id shows (the app shows the name).
        let builtin = HTMLReport.render(report(lut: nil, proxy: p))
        XCTAssertTrue(builtin.contains("LUT: dji-dlog-dgamut") || builtin.contains("LUT: DJI D-Log / D-Gamut → Rec.709"))
        p.lutMode = .custom
        p.lutPath = "/Users/x/show.cube"
        XCTAssertTrue(HTMLReport.render(report(lut: nil, proxy: p)).contains("LUT: show.cube"))
        p.lutMode = .off
        XCTAssertTrue(HTMLReport.render(report(lut: nil, proxy: p)).contains("LUT: bez LUT"))
    }

    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 1 << 20
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    // The LUT is chosen per clip (the Sony XML read from the destination copy) and recorded in the report.
    func testJobRecordsLutPerClip() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/CLIP/C0001.MP4", seconds: 2, timecode: "01:00:00:00")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/CLIP/C0002.MP4", seconds: 2, timecode: "01:00:10:00")
        try #"<NonRealTimeMeta><Item name="CaptureGammaEquation" value="s-log3-cine"/><Item name="CaptureColorPrimaries" value="S-Gamut3Cine"/></NonRealTimeMeta>"#
            .write(toFile: card + "/PRIVATE/M4ROOT/CLIP/C0001M01.XML", atomically: true, encoding: .utf8)
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.date = "2026-10-05"
        config.proxy.enabled = true
        config.proxy.height = 360
        config.tools = TestMedia.tools
        let job = OffloadJob(config: config, options: options(), luts: TestMedia.luts)
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        let c1 = try XCTUnwrap(report.clips.first { $0.clip.name == "C0001" })
        XCTAssertEqual(c1.lut?.source, "auto")
        XCTAssertEqual(c1.lut?.id, "sony-slog3-sgamut3cine")
        XCTAssertEqual(c1.proxyStatus, .done)
        XCTAssertEqual(c1.thumbnails.count, 3)
        let c2 = try XCTUnwrap(report.clips.first { $0.clip.name == "C0002" })
        XCTAssertEqual(c2.lut?.source, "none")
        XCTAssertEqual(c2.lut?.reason, "kamera nerozpoznána")
        let json = try String(contentsOfFile: try XCTUnwrap(job.plan).cardPaths[0] + "/_REPORT/offload_report.json", encoding: .utf8)
        XCTAssertTrue(json.contains("\"lut\""))
    }
}
```

`mac/Tests/OffloadKitTests/ColorTests.swift` (úkol 18) — tři pomocné funkce nahradit (LUT teď jde po klipech; testy
samotné beze změny):

```swift
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
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "LutRenderTests|LutReportTests|ColorTests"` → nesestaví se
(`extra argument 'fullRange'`, `cannot find 'RawLook'`, `has no member 'needsPreMap'`, `'lut'`, `extra argument 'luts'`).

- [ ] **Step 3: `Proxy.swift`** — nahradit vše od řádku `/// How frames reach ffmpeg: straight from a file, or piped from a vendor RAW decoder.`
do konce souboru (ProxyCodec a ProxySettings nad tím zůstávají):

```swift
/// How frames reach ffmpeg: straight from a file, or piped from a vendor RAW decoder.
struct FrameSource {
    var inputArgs: [String]
    var feeder: Process?
    var audioFromInput = true
    /// The decoder already cropped to the frame lines (ARRI does it before debayering).
    var alreadyCropped = false
    /// Full-range RGB (braw-decode) — a built-in LUT then needs `FilterGraph.preMap` first.
    var fullRange = false
    /// Intermediate files to delete once ffmpeg is done.
    var cleanup: [String] = []

    func removeIntermediates() {
        for p in cleanup { try? FileManager.default.removeItem(atPath: p) }
    }
}

/// What a RAW decoder hands ffmpeg: camera log (a LUT follows) or its own Rec.709 look. `arriColorSpace` is the ARRI
/// target space matching the LUT (nil = from the clip's metadata). BRAW always decodes in the clip's gamma; Canon has
/// no decoder.
struct RawLook: Equatable {
    let log: Bool
    let arriColorSpace: String?
}

/// Per-format decoding for formats ffmpeg cannot read itself.
protocol RawBackend {
    var format: ClipFormat { get }
    var missingHint: String { get }
    func isAvailable(_ tools: ExternalTools) -> Bool
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo
    /// `singleFrameAt` (seconds) asks for just one frame — used for report thumbnails.
    func source(_ path: String, info: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource
}

/// Where a report thumbnail is taken: frame number, its time, and whether it is the clip's last frame.
struct FramePosition: Equatable {
    let seconds: Double
    let frame: Int
    let fromEnd: Bool
}

/// Probes clips, makes the report thumbnails (first, middle, last frame) and renders proxies — each clip with its own
/// LUT (`LutUse`). Works in its own temp folder. One renderer serves one job, on one thread.
public final class ProxyRenderer {
    public let settings: ProxySettings
    public let tools: ExternalTools
    let backends: [ClipFormat: RawBackend]
    let tempDir: String
    /// A proxy render with no progress for this long is stuck (3× as long before the first progress): ffmpeg is killed
    /// and, when it decoded in hardware, the clip is retried once in software.
    static let defaultStallTimeout: TimeInterval = 60
    /// How long a killed ffmpeg is waited for: a process stuck in a GPU driver may never end — the job must go on.
    static let killWait: TimeInterval = 10
    /// Tests shorten it.
    var stallTimeout: TimeInterval = ProxyRenderer.defaultStallTimeout
    /// Where retries and unkillable processes are reported (the job's log).
    var log: ((String) -> Void)?
    /// The custom LUT (custom mode) was copied to `tempDir/look.cube`.
    private let hasCustomLUT: Bool
    /// Built-in LUT ids already copied into `tempDir`.
    private var builtinLUTs: Set<String> = []

    public init(settings: ProxySettings, tools: ExternalTools) {
        self.settings = settings
        self.tools = tools
        backends = Dictionary(uniqueKeysWithValues: RawBackends.all.map { ($0.format, $0) })
        let dir = NSTemporaryDirectory() + "offload-" + UUID().uuidString.prefix(8)
        tempDir = dir
        try? IO.mkdirs(dir)
        // Filter graphs are picky about quoting: LUTs are used from a plain path in the temp folder. The custom LUT is
        // copied up front, built-in ones per clip (prepareLUT).
        var custom = false
        if LutResolver.effectiveMode(settings) == .custom, let lut = settings.lutPath, !lut.isEmpty,
           FileManager.default.fileExists(atPath: lut) {
            custom = (try? FileManager.default.copyItem(atPath: lut, toPath: dir + "/look.cube")) != nil
        }
        hasCustomLUT = custom
    }

    deinit { try? FileManager.default.removeItem(atPath: tempDir) }

    /// nil when the clip can be rendered, otherwise the reason it cannot.
    public func unsupportedReason(_ clip: Clip) -> String? {
        guard tools.ffmpeg != nil else { return "ffmpeg not found" }
        switch clip.format {
        case .video: return nil
        case .unknown: return "unknown format"
        default:
            guard let b = backends[clip.format] else { return "\(clip.format.label): no decoder available" }
            return b.isAvailable(tools) ? nil : b.missingHint
        }
    }

    /// MXF is a container for many codecs; ARRIRAW/ARRICORE inside it only the ARRI tool can read.
    /// Returns the clip reclassified as ARRIRAW when ffmpeg cannot decode it but art-cmd can.
    public func reclassifyUndecodableMXF(_ clip: Clip, cardPath: String) -> (Clip, ClipInfo)? {
        guard clip.format == .video, (clip.path as NSString).pathExtension.lowercased() == "mxf",
              let arri = backends[.arriraw], arri.isAvailable(tools),
              let info = try? arri.probe(cardPath + "/" + clip.path, tools: tools), info.width != nil else { return nil }
        var c = clip
        c.format = .arriraw
        return (c, info)
    }

    public func probe(_ clip: Clip, cardPath: String) throws -> ClipInfo {
        let path = cardPath + "/" + clip.path
        if clip.format == .video {
            guard let ffprobe = tools.ffprobe else { throw ToolError(tool: "ffprobe", status: -1, message: "ffprobe not found") }
            return try FFProbe.probe(ffprobe, path)
        }
        guard let b = backends[clip.format], b.isAvailable(tools) else {
            throw ToolError(tool: clip.format.label, status: -1, message: unsupportedReason(clip) ?? "")
        }
        return try b.probe(path, tools: tools)
    }

    // MARK: Thumbnail positions

    /// First, middle and last frame; only the first when the length is unknown or the clip has one frame.
    static func framePositions(_ info: ClipInfo) -> [FramePosition] {
        let first = FramePosition(seconds: 0, frame: 0, fromEnd: false)
        guard let duration = info.duration, duration > 0, let fps = Timecode.fps(info.frameRate) else { return [first] }
        let frames = info.frames ?? Int((duration * fps).rounded())
        guard frames > 1 else { return [first] }
        let middle = frames / 2, last = frames - 1
        var all = [first]
        if middle > 0 { all.append(FramePosition(seconds: Double(middle) / fps, frame: middle, fromEnd: false)) }
        if last > middle { all.append(FramePosition(seconds: Double(last) / fps, frame: last, fromEnd: true)) }
        return all
    }

    /// Caption under a thumbnail: the source timecode of that frame, else its number.
    static func label(_ info: ClipInfo, _ p: FramePosition) -> String {
        if let tc = info.timecode, Timecode.isValid(tc), let fps = Timecode.fps(info.frameRate) {
            return Timecode.add(tc, frames: p.frame, fps: fps)
        }
        return "#\(p.frame)"
    }

    /// Where ffmpeg's input seek (-ss before -i) lands for a thumbnail position. A frame in the middle is sought
    /// half a frame early: its exact time rounded up would skip to the next frame (23.976, 24, 29.97 fps).
    /// The last frame is sought a second before it and decoded to the end of the video stream — never relative
    /// to the container end, which is later when the audio runs longer than the picture.
    static func seekSeconds(_ info: ClipInfo, _ p: FramePosition) -> String {
        var at = p.seconds
        if p.fromEnd {
            at = max(0, p.seconds - 1)
        } else if p.frame > 0, let fps = Timecode.fps(info.frameRate) {
            at = (Double(p.frame) - 0.5) / fps
        }
        return String(format: "%.6f", at)
    }

    /// Hardware decoding only where it helps and is safe: H.264 and HEVC (`ClipInfo.codec` starts with the ffprobe
    /// codec name). Everything else (FFV1, ProRes, MJPEG, DNx …) decodes in software — same rule as Windows, where
    /// ffmpeg 8.1 with -hwaccel hung forever on FFV1.
    static func usesHardwareDecode(_ codec: String?) -> Bool {
        let c = (codec ?? "").lowercased()
        return c.hasPrefix("h264") || c.hasPrefix("hevc")
    }

    /// The note at a clip whose ffmpeg stalled (texts shared with Windows `StallNote`).
    static func stallNote(_ seconds: TimeInterval, retriedInSoftware: Bool) -> String {
        "ffmpeg se zasekl (\(String(format: "%.0f", seconds)) s bez nového snímku)"
            + (retriedInSoftware ? " i bez hardwarového dekódování; proxy nevznikla" : "")
    }

    // MARK: LUT per clip

    /// The .cube for this clip as a plain path in the temp folder, or nil for no LUT. The custom LUT was copied in init
    /// (nil when it was missing); a built-in one is copied on first use, once per id.
    func prepareLUT(_ lut: LutUse?) throws -> String? {
        guard let lut, let file = lut.file else { return nil }
        guard let id = lut.id else { return hasCustomLUT ? tempDir + "/look.cube" : nil }
        let copy = tempDir + "/lut-\(id).cube"
        if !builtinLUTs.contains(id) {
            try? FileManager.default.removeItem(atPath: copy)
            try FileManager.default.copyItem(atPath: file, toPath: copy)
            builtinLUTs.insert(id)
        }
        return copy
    }

    /// RAW decoders hand over log whenever a LUT follows; ARRI's target space must match a built-in ARRI LUT.
    static func lookFor(_ lut: LutUse?, info: ClipInfo) -> RawLook {
        guard let lut, let file = lut.file else { return RawLook(log: false, arriColorSpace: nil) }
        switch lut.id {
        case "arri-logc4-awg4"?: return RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4")
        case "arri-logc3-awg3"?: return RawLook(log: true, arriColorSpace: "AWG3/D65/LogC3")
        case nil: return RawLook(log: true, arriColorSpace: ARRIRAWBackend.colorSpace(lut: file, info: info))
        default: return RawLook(log: true, arriColorSpace: nil)
        }
    }

    /// Built-in LUTs expect legal-range input: full-range sources (BRAW, video tagged "pc") are pre-mapped first.
    /// A custom .cube is applied as it is.
    static func needsPreMap(_ lut: LutUse?, source: FrameSource, info: ClipInfo) -> Bool {
        lut?.isBuiltin == true && (source.fullRange || info.colorRange == "pc")
    }

    private func frameSource(_ clip: Clip, cardPath: String, info: ClipInfo, look: RawLook, singleFrameAt: Double?,
                             hwaccel: Bool) throws -> FrameSource {
        let path = cardPath + "/" + clip.path
        if clip.format == .video {
            // VideoToolbox only for H.264/HEVC proxies (usesHardwareDecode); thumbnails always decode in software.
            return FrameSource(inputArgs: hwaccel ? ["-hwaccel", "videotoolbox", "-i", path] : ["-i", path])
        }
        guard let b = backends[clip.format] else { throw ToolError(tool: clip.format.label, status: -1, message: "no decoder") }
        return try b.source(path, info: info, settings: settings, look: look, tools: tools, tempDir: tempDir, singleFrameAt: singleFrameAt)
    }

    // MARK: Thumbnails and proxies

    /// First, middle and last frame as small JPEGs with captions, with the clip's LUT so log footage is readable.
    /// `lut` nil = no LUT (thumbnails taken from a finished proxy, which already has it). When `isCancelled` turns
    /// true the clip simply gets no thumbnails. One bad position does not cost the others; if none works, the last
    /// error is thrown.
    public func thumbnails(_ clip: Clip, cardPath: String, info: ClipInfo, lut: LutUse? = nil,
                           isCancelled: () -> Bool = { false }) throws -> [Thumbnail] {
        guard let ffmpeg = tools.ffmpeg else { throw ToolError(tool: "ffmpeg", status: -1, message: "ffmpeg not found") }
        let lutFile = try prepareLUT(lut)
        let look = ProxyRenderer.lookFor(lutFile != nil ? lut : nil, info: info)   // log only when a LUT really follows
        let video = clip.format == .video
        var result: [Thumbnail] = []
        var lastError: Error?
        for pos in ProxyRenderer.framePositions(info) {
            if isCancelled() { return [] }
            let out = tempDir + "/thumb-\(UUID().uuidString).jpg"
            defer { unlink(out) }
            do {
                let src = try frameSource(clip, cardPath: cardPath, info: info, look: look, singleFrameAt: pos.seconds, hwaccel: false)
                defer { src.removeIntermediates() }
                // The pre-map depends on the source (BRAW, full-range video).
                let filter = FilterGraph.thumbnail(lutFile: lutFile, premap: ProxyRenderer.needsPreMap(lut, source: src, info: info))
                var args = ["-hide_banner", "-loglevel", "error", "-y"]
                if video { args += ["-ss", ProxyRenderer.seekSeconds(info, pos)] }
                args += src.inputArgs
                // Last frame: decode the final second and keep overwriting the image — the last write is the last frame.
                args += video && pos.fromEnd ? ["-map", "0:v:0", "-vf", filter, "-update", "1"] : ["-frames:v", "1", "-vf", filter]
                args += ["-q:v", "5", out]
                try runFFmpeg(ffmpeg, args, feeder: src.feeder, duration: nil, progress: nil, isCancelled: isCancelled)
                guard FileManager.default.fileExists(atPath: out) else {
                    throw ToolError(tool: "ffmpeg", status: 0, message: "no frame")
                }
                result.append(Thumbnail(jpeg: try Data(contentsOf: URL(fileURLWithPath: out)), label: ProxyRenderer.label(info, pos)))
            } catch {
                if isCancelled() { return [] }
                lastError = error
            }
        }
        if result.isEmpty, let lastError { throw lastError }
        return result
    }

    /// Renders the proxy. A render that stalls (no progress for `stallTimeout`) is killed. With hardware decoding the clip
    /// is then retried once in software — as is one that failed with it; without, the stall fails the proxy. Cancelling
    /// never retries. Notes and log lines as on Windows.
    public func render(_ clip: Clip, cardPath: String, info: ClipInfo, lut: LutUse? = nil, output: String,
                       progress: @escaping (Double) -> Void, isCancelled: @escaping () -> Bool) throws {
        guard let ffmpeg = tools.ffmpeg else { throw ToolError(tool: "ffmpeg", status: -1, message: "not found") }
        let lutFile = try prepareLUT(lut)
        let look = ProxyRenderer.lookFor(lutFile != nil ? lut : nil, info: info)
        let hw = clip.format == .video && ProxyRenderer.usesHardwareDecode(info.codec)
        let first: Error
        do {
            try renderOnce(ffmpeg, clip, cardPath: cardPath, info: info, lut: lut, lutFile: lutFile, look: look, hwaccel: hw,
                           output: output, progress: progress, isCancelled: isCancelled)
            return
        } catch let e as FFmpegStall where !hw && !isCancelled() {
            throw FFmpegStall(note: e.note + "; proxy nevznikla")
        } catch where hw && (error is FFmpegStall || error is ToolError) && !isCancelled() {
            first = error   // hardware decoding stalled or failed: once more in software
        }
        if isCancelled() { throw ToolError(tool: "ffmpeg", status: -1, message: "cancelled") }
        log?("proxy \(clip.name): with hardware decoding: \(first) — retrying in software")
        do {
            try renderOnce(ffmpeg, clip, cardPath: cardPath, info: info, lut: lut, lutFile: lutFile, look: look, hwaccel: false,
                           output: output, progress: progress, isCancelled: isCancelled)
            log?("proxy \(clip.name): software decoding succeeded")
        } catch where (error is FFmpegStall || error is ToolError) && !isCancelled() {
            log?("proxy \(clip.name): software decoding failed too: \(error)")
            switch (first, error) {
            case (is FFmpegStall, is FFmpegStall):
                throw ProxyRetryFailed(note: ProxyRenderer.stallNote(stallTimeout, retriedInSoftware: true))
            case (is FFmpegStall, _):
                throw ProxyRetryFailed(note: "\(first); bez hardwarového dekódování selhal: \(error)")
            default:
                throw ProxyRetryFailed(note: "\(error); s hardwarovým dekódováním předtím: \(first)")
            }
        }
    }

    private func renderOnce(_ ffmpeg: String, _ clip: Clip, cardPath: String, info: ClipInfo, lut: LutUse?, lutFile: String?,
                            look: RawLook, hwaccel: Bool, output: String,
                            progress: @escaping (Double) -> Void, isCancelled: @escaping () -> Bool) throws {
        let src = try frameSource(clip, cardPath: cardPath, info: info, look: look, singleFrameAt: nil, hwaccel: hwaccel)
        defer { src.removeIntermediates() }
        let outHeight = min(settings.height, info.height ?? settings.height) & ~1
        let vf = FilterGraph.proxy(settings, outHeight: outHeight, alreadyCropped: src.alreadyCropped, lutFile: lutFile,
                                   premap: ProxyRenderer.needsPreMap(lut, source: src, info: info),
                                   burnIn: burnIn(clip: clip, info: info, height: outHeight))
        var args = ["-hide_banner", "-loglevel", "error", "-y", "-nostats", "-progress", "pipe:1"] + src.inputArgs
        args += ["-map", "0:v:0"]
        if settings.audio && info.audioChannels > 0 && src.audioFromInput {
            args += ["-map", "0:a:0?"] + settings.codec.audioArgs
        }
        args += ["-vf", vf] + settings.codec.encoderArgs(bitrateMbps: settings.bitrateMbps) + FilterGraph.rec709Args
        if let tc = info.timecode, Timecode.isValid(tc) { args += ["-timecode", tc] }

        let dir = (output as NSString).deletingLastPathComponent
        try IO.mkdirs(dir)
        let part = dir + "/." + (output as NSString).lastPathComponent + ".part"
        args += ["-f", "mov", part]
        do {
            try runFFmpeg(ffmpeg, args, feeder: src.feeder, duration: info.duration, progress: progress,
                          isCancelled: isCancelled, stallAfter: stallTimeout)
            guard rename(part, output) == 0 else { throw IOError("rename", output) }
        } catch {
            unlink(part)
            throw error
        }
    }

    /// Bottom bar: clip name left, source timecode right.
    func burnIn(clip: Clip, info: ClipInfo, height: Int) -> [String] {
        guard settings.burnFilename || settings.burnTimecode else { return [] }
        let font = ["/System/Library/Fonts/Menlo.ttc", "/System/Library/Fonts/Monaco.ttf"].first { FileManager.default.fileExists(atPath: $0) }
        guard let font else { return [] }
        let size = max(12, height * 3 / 100)
        let margin = max(8, height * 2 / 100)
        var f = ["drawbox=x=0:y=ih-\(size * 2):w=iw:h=\(size * 2):color=black@0.55:t=fill"]
        let common = "fontfile=\(font):fontcolor=white:fontsize=\(size):y=h-\(size * 3 / 2)"
        if settings.burnFilename {
            let label = tempDir + "/label-\(UUID().uuidString.prefix(8)).txt"
            try? Data(clip.name.utf8).write(to: URL(fileURLWithPath: label))
            // expansion=none: the name is literal text — a "%" in it would otherwise be a drawtext expansion.
            f.append("drawtext=\(common):textfile=\(label):expansion=none:x=\(margin)")
        }
        if settings.burnTimecode, let rate = info.frameRate {
            let tc = info.timecode.flatMap { Timecode.isValid($0) ? $0 : nil } ?? "00:00:00:00"
            let escaped = tc.replacingOccurrences(of: ":", with: "\\:").replacingOccurrences(of: ";", with: "\\;")
            f.append("drawtext=\(common):timecode='\(escaped)':rate=\(rate):x=w-tw-\(margin)")
        }
        return f
    }

    /// `stallAfter` (renders only, with -progress pipe:1 in the args): ffmpeg is killed and `FFmpegStall` thrown when it
    /// has reported no new frame or output time and taken no data from the feeder for that long (3× before the first
    /// progress). Cancelling is checked first and wins.
    func runFFmpeg(_ ffmpeg: String, _ args: [String], feeder: Process?, duration: Double?,
                   progress: ((Double) -> Void)?, isCancelled: () -> Bool, stallAfter: TimeInterval? = nil) throws {
        let p = ToolProcess(ffmpeg, args)
        p.captureStderr()
        let watch = ProgressWatch()
        var pump: Thread?
        if let feeder {
            // The decoder's frames go through a pump: every chunk ffmpeg takes is progress (a slow RAW decoder is not stuck).
            let fromFeeder = Pipe(), toFFmpeg = Pipe()
            feeder.standardOutput = fromFeeder
            feeder.standardError = FileHandle.nullDevice
            p.process.standardInput = toFFmpeg
            // A write after ffmpeg died must fail with EPIPE, not take the app down with SIGPIPE.
            _ = fcntl(toFFmpeg.fileHandleForWriting.fileDescriptor, F_SETNOSIGPIPE, 1)
            pump = Thread {
                // Plain read(2)/write(2): read returns whatever the decoder has written so far. FileHandle.read(upToCount:)
                // on a pipe waits for the whole count (or EOF) — a slow decoder would look stuck.
                let inFD = fromFeeder.fileHandleForReading.fileDescriptor
                let outFD = toFFmpeg.fileHandleForWriting.fileDescriptor
                let size = 4 << 20
                let buffer = UnsafeMutableRawPointer.allocate(byteCount: size, alignment: 16)
                defer { buffer.deallocate() }
                pumping: while true {
                    let n = Darwin.read(inFD, buffer, size)
                    if n < 0 && errno == EINTR { continue }
                    if n <= 0 { break }   // the decoder is done (EOF) or gone
                    var sent = 0
                    while sent < n {
                        let w = Darwin.write(outFD, buffer + sent, n - sent)
                        if w < 0 && errno == EINTR { continue }
                        if w <= 0 { break pumping }   // ffmpeg is gone (EPIPE, no signal thanks to F_SETNOSIGPIPE)
                        sent += w
                    }
                    watch.dataFlowed()
                }
                try? toFFmpeg.fileHandleForWriting.close()
            }
        } else {
            p.process.standardInput = FileHandle.nullDevice
        }
        p.onStdoutLine { line in
            watch.advanced(line)
            guard let progress, let d = duration, d > 0, line.hasPrefix("out_time_us="),
                  let us = Double(line.dropFirst("out_time_us=".count)) else { return }
            progress(min(1, us / 1_000_000 / d))
        }
        try feeder?.run()
        do {
            try p.process.run()
        } catch {
            feeder?.terminate()
            throw error
        }
        pump?.start()
        while p.process.isRunning {
            if isCancelled() {
                stop(p, feeder)
                throw ToolError(tool: "ffmpeg", status: -1, message: "cancelled")
            }
            if let limit = stallAfter, watch.isStalled(limit: limit) {
                stop(p, feeder)
                throw FFmpegStall(note: ProxyRenderer.stallNote(limit, retriedInSoftware: false))
            }
            usleep(200_000)
        }
        if let feeder, feeder.isRunning { feeder.terminate() }
        feeder?.waitUntilExit()
        guard p.process.terminationStatus == 0 else {
            throw ToolError(tool: "ffmpeg", status: p.process.terminationStatus, message: p.stderrText)
        }
        if let feeder, feeder.terminationStatus != 0, feeder.terminationReason == .exit {
            throw ToolError(tool: (feeder.executableURL?.lastPathComponent ?? "decoder"), status: feeder.terminationStatus, message: "")
        }
    }

    /// SIGTERM, at most `killWait` for ffmpeg to end, then SIGKILL — never an unbounded wait (a process stuck in a GPU
    /// driver may survive even that; the job goes on without it and the log says so).
    private func stop(_ p: ToolProcess, _ feeder: Process?) {
        let pid = p.process.processIdentifier
        p.process.terminate()
        if let feeder, feeder.isRunning { feeder.terminate() }
        let deadline = Date().addingTimeInterval(ProxyRenderer.killWait)
        while p.process.isRunning && Date() < deadline { usleep(50_000) }
        guard p.process.isRunning else { return }
        kill(pid, SIGKILL)
        if let feeder, feeder.isRunning { kill(feeder.processIdentifier, SIGKILL) }
        usleep(500_000)
        if p.process.isRunning { log?("ffmpeg (pid \(pid)) did not end \(Int(ProxyRenderer.killWait)) s after kill") }
    }
}
```

(`FramePosition` z úkolu 5 je součástí nahrazovaného úseku — v kódu výše je beze změny.)

- [ ] **Step 4: `mac/Sources/OffloadKit/RawBackends.swift`** — celý nový obsah:

```swift
import Foundation

/// Vendor decoders for formats ffmpeg cannot read. When a LUT is applied they hand ffmpeg the camera's log
/// (`RawLook.log`; BRAW always decodes in the clip's gamma), so the same LUT / scale / burn-in chain fits every
/// camera; without a LUT, RED and ARRI deliver their own Rec.709 look.
enum RawBackends {
    static let all: [RawBackend] = [BRAWBackend(), R3DBackend(), ARRIRAWBackend(), CanonRawBackend()]

    /// ffmpeg wants a rational frame rate; cameras report floats.
    static func rational(_ fps: Double) -> String {
        for (v, r) in [(23.976, "24000/1001"), (29.97, "30000/1001"), (47.952, "48000/1001"), (59.94, "60000/1001"), (119.88, "120000/1001")]
            where abs(fps - v) < 0.01 { return r }
        return fps == fps.rounded() ? String(Int(fps)) : String(format: "%.3f", fps)
    }

    static func newestFile(in dir: String, ext: String) -> String? {
        let fm = FileManager.default
        let items = (try? fm.contentsOfDirectory(atPath: dir)) ?? []
        return items.filter { ($0 as NSString).pathExtension.lowercased() == ext }
            .map { dir + "/" + $0 }
            .max { ((try? fm.attributesOfItem(atPath: $0)[.modificationDate] as? Date) ?? .distantPast)
                < ((try? fm.attributesOfItem(atPath: $1)[.modificationDate] as? Date) ?? .distantPast) }
    }

    static func freshDir(_ tempDir: String, _ name: String) throws -> String {
        let d = tempDir + "/" + name + "-" + UUID().uuidString.prefix(6)
        try IO.mkdirs(d)
        return d
    }
}

// MARK: - Blackmagic RAW (braw-decode helper → raw RGB48 frames on a pipe)

struct BRAWBackend: RawBackend {
    let format = ClipFormat.braw
    let missingHint = "Blackmagic RAW: chybí braw-decode nebo Blackmagic RAW Player/SDK"

    func isAvailable(_ tools: ExternalTools) -> Bool { tools.brawDecoder != nil }

    private struct Info: Decodable {
        let width: Int, height: Int, sourceWidth: Int, sourceHeight: Int
        let fps: Double, frames: Int, timecode: String, camera: String
    }

    private func info(_ path: String, scale: String, tools: ExternalTools) throws -> Info {
        let out = try ToolProcess.run(tools.brawDecoder!, ["--info", "--scale", scale, path])
        return try JSONDecoder().decode(Info.self, from: Data(out.utf8))
    }

    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        let i = try info(path, scale: "eighth", tools: tools)
        var c = ClipInfo()
        c.codec = "Blackmagic RAW"
        c.width = i.sourceWidth
        c.height = i.sourceHeight
        c.frameRate = RawBackends.rational(i.fps)
        c.frames = i.frames
        c.duration = i.fps > 0 ? Double(i.frames) / i.fps : nil
        c.timecode = i.timecode.isEmpty ? nil : i.timecode
        c.camera = i.camera.isEmpty ? nil : i.camera
        return c
    }

    /// Smallest decode scale that still covers the target height — decoding less is much faster.
    private func scale(sourceHeight: Int, target: Int) -> String {
        for (name, div) in [("eighth", 8), ("quarter", 4), ("half", 2)] where sourceHeight / div >= target { return name }
        return "full"
    }

    /// The frame braw-decode starts at for a thumbnail `seconds` into the clip. Rounded: `seconds` comes from the
    /// exact rational rate (24000/1001), braw-decode reports a rounded fps (23.976) — truncating would land a frame early.
    static func startFrame(seconds: Double, fps: Double, frames: Int) -> Int {
        let rate = fps > 0 ? fps : 25
        return min(max(0, frames - 1), max(0, Int((seconds * rate).rounded())))
    }

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        let target = singleFrameAt == nil ? settings.height : 180
        let s = scale(sourceHeight: c.height ?? 2160, target: target)
        let dims = try info(path, scale: s, tools: tools)
        var args = ["--scale", s]
        if let t = singleFrameAt {
            args += ["--start", String(BRAWBackend.startFrame(seconds: t, fps: dims.fps, frames: dims.frames)), "--count", "1"]
        }
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: tools.brawDecoder!)
        feeder.arguments = args + [path]
        // braw-decode hands over full-range RGB (RGBU16) in the clip's own gamma: a built-in LUT needs the pre-map first.
        return FrameSource(inputArgs: ["-f", "rawvideo", "-pix_fmt", "rgb48le", "-s", "\(dims.width)x\(dims.height)",
                                       "-r", c.frameRate ?? "25", "-i", "-"],
                           feeder: feeder, audioFromInput: false, fullRange: true)
    }
}

// MARK: - RED (REDline → ProRes LT intermediate)

struct R3DBackend: RawBackend {
    let format = ClipFormat.r3d
    let missingHint = "RED R3D: nainstalujte REDCINE-X PRO (REDline)"

    func isAvailable(_ tools: ExternalTools) -> Bool { tools.redline != nil }

    /// `--printMeta 1` prints "Key: value" lines; the key names differ between versions, so match loosely.
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        let out = try ToolProcess.run(tools.redline!, ["--i", path, "--printMeta", "1"])
        var meta: [String: String] = [:]
        for line in out.split(separator: "\n") {
            guard let colon = line.firstIndex(of: ":") else { continue }
            let k = line[..<colon].trimmingCharacters(in: .whitespaces).lowercased()
            let v = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
            if meta[k] == nil { meta[k] = v }
        }
        func find(_ needles: [String]) -> String? {
            for n in needles { if let hit = meta.first(where: { $0.key.contains(n) })?.value, !hit.isEmpty { return hit } }
            return nil
        }
        var c = ClipInfo()
        c.codec = "REDCODE RAW"
        c.width = find(["frame width", "width"]).flatMap { Int($0.filter(\.isNumber)) }
        c.height = find(["frame height", "height"]).flatMap { Int($0.filter(\.isNumber)) }
        if let fps = find(["record fps", "timebase", "fps", "frame rate"]).flatMap({ Double($0.split(separator: " ").first ?? "") }) {
            c.frameRate = RawBackends.rational(fps)
            if let n = find(["total frames", "frame count", "frames"]).flatMap({ Int($0.filter(\.isNumber)) }) {
                c.frames = n
                c.duration = Double(n) / fps
            }
        }
        c.timecode = find(["abs tc", "absolute timecode", "timecode", "edge tc"])
        c.camera = find(["camera model", "camera"])
        return c
    }

    /// REDline arguments: the smallest decode resolution that covers the proxy height; log (IPP2, REDWideGamutRGB /
    /// Log3G10) only when a LUT follows, otherwise RED's own Rec.709 look.
    static func args(input: String, outDir: String, info c: ClipInfo, settings: ProxySettings, look: RawLook) -> [String] {
        let h = c.height ?? 4320
        let res = h / 4 >= settings.height ? "4" : (h / 2 >= settings.height ? "3" : "1")
        var args = ["--i", input, "--outDir", outDir, "--o", "intermediate", "--format", "201", "--PRcodec", "2", "--res", res, "--useMeta"]
        if look.log { args += ["--colorSciVersion", "3", "--colorSpace", "25", "--gammaCurve", "34"] }
        return args
    }

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        guard singleFrameAt == nil else {
            // Thumbnails come from the finished proxy instead (one REDline run per clip is enough).
            throw ToolError(tool: "REDline", status: -1, message: "thumbnail from proxy")
        }
        let dir = try RawBackends.freshDir(tempDir, "r3d")
        try ToolProcess.run(tools.redline!, R3DBackend.args(input: path, outDir: dir, info: c, settings: settings, look: look))
        guard let mov = RawBackends.newestFile(in: dir, ext: "mov") else {
            throw ToolError(tool: "REDline", status: -1, message: "no output in \(dir)")
        }
        return FrameSource(inputArgs: ["-i", mov], cleanup: [dir])
    }
}

// MARK: - ARRIRAW (ARRI Reference Tool CLI → ProRes LT MXF intermediate)

struct ARRIRAWBackend: RawBackend {
    let format = ClipFormat.arriraw
    let missingHint = "ARRIRAW: nainstalujte ARRI Reference Tool (art-cmd)"

    func isAvailable(_ tools: ExternalTools) -> Bool { tools.artCmd != nil }

    /// `art-cmd export` writes the clip's metadata as JSON: lists of metadata sets per clip, plus one entry per frame.
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        let json = NSTemporaryDirectory() + "art-meta-\(UUID().uuidString).json"
        defer { try? FileManager.default.removeItem(atPath: json) }
        // art-cmd writes art.log into the working directory — never let that be the card or a destination.
        try ToolProcess.run(tools.artCmd!, ["export", "--input", path, "--output", json, "--skip-audio", "--skip-look"],
                            cwd: NSTemporaryDirectory())
        guard var root = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: json))) as? [String: Any] else {
            throw ToolError(tool: "art-cmd", status: -1, message: "unreadable metadata")
        }
        // Only the first frame matters (start timecode); the rest would just slow the walk down.
        if var fb = root["frameBasedMetadata"] as? [String: Any], let frames = fb["frames"] as? [Any] {
            fb["frames"] = Array(frames.prefix(1))
            root["frameBasedMetadata"] = fb
        }
        var flat: [String: Any] = [:]
        func walk(_ v: Any, _ prefix: String) {
            if let d = v as? [String: Any] { for (k, x) in d { walk(x, prefix + k + ".") } }
            else if let a = v as? [Any] { for x in a { walk(x, prefix) } }
            else if flat[String(prefix.dropLast())] == nil { flat[String(prefix.dropLast())] = v }
        }
        walk(root, "")
        func value(_ suffix: String) -> Any? { flat.first { $0.key.hasSuffix(suffix) }?.value }
        func int(_ suffix: String) -> Int? { (value(suffix) as? NSNumber)?.intValue ?? (value(suffix) as? String).flatMap { Int($0) } }

        var c = ClipInfo()
        c.codec = (value("videoCodec") as? String) ?? "ARRIRAW"
        c.width = int("storedSize.width") ?? int("displayRect.width")
        c.height = int("storedSize.height") ?? int("displayRect.height")
        if let rate = value("nativePictureEssenceDescriptor.file.sampleRate") as? String {
            c.frameRate = rate.hasSuffix("/1") ? String(rate.dropLast(2)) : rate
        }
        c.frames = int("clipDuration")
        let parts = (c.frameRate ?? "").split(separator: "/").compactMap { Double($0) }
        let fps = parts.count == 2 ? parts[0] / parts[1] : parts.first
        if let n = c.frames, let fps, fps > 0 { c.duration = Double(n) / fps }
        c.timecode = value("frames.timecode") as? String
        c.camera = value("cameraModel") as? String
        c.logEncoding = value("sceneColorEncodingTransferCurve") as? String
        // Lens squeeze; ARRI writes it as a factor or in thousandths depending on the field.
        if let raw = flat.first(where: { $0.key.lowercased().contains("squeeze") && ($0.value as? NSNumber) != nil })?.value as? NSNumber {
            let v = raw.doubleValue > 10 ? raw.doubleValue / 1000 : raw.doubleValue
            if v > 1.01 { c.squeeze = v }
        }
        c.audioChannels = flat.keys.contains { $0.hasSuffix("nativeSoundEssenceDescriptorList.blockAlign") } ? 2 : 0
        return c
    }

    /// The LUT decides which log it needs when its name says so; otherwise the clip's own log encoding
    /// from metadata, then the camera (ALEXA 35 family → LogC4, older ALEXAs → LogC3).
    static func colorSpace(lut: String?, info: ClipInfo) -> String {
        let name = ((lut ?? "") as NSString).lastPathComponent.lowercased()
        if name.contains("logc4") { return "AWG4/D65/LogC4" }
        if name.contains("logc3") || name.contains("logc ") || name.contains("logc_") || name.contains("logc-") { return "AWG3/D65/LogC3" }
        if let log = info.logEncoding?.lowercased() { return log.contains("4") ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3" }
        return isAlexa35(info.camera) ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3"
    }

    /// "ALEXA 35" / "ALEXA35" as a whole word — not "ALEXA Mini LF 35mm" or a serial number.
    static func isAlexa35(_ camera: String?) -> Bool {
        guard let camera else { return false }
        return camera.range(of: #"\bALEXA\s*35\b"#, options: [.regularExpression, .caseInsensitive]) != nil
    }

    /// art-cmd arguments and, for a frame-lines crop, the parameters JSON (written to `parametersFile`).
    /// Frame lines are defined on the desqueezed picture: the crop is computed there, then mapped back to sensor pixels
    /// and done before debayer/scaling (full detail inside the frame lines). Size and desqueeze go into the same file
    /// (art-cmd refuses --desqueeze / --output-height with it, and --output-height would stretch the crop back).
    static func args(input: String, output: String, info c: ClipInfo, settings: ProxySettings, look: RawLook,
                     parametersFile: String) -> (args: [String], parameters: Data?, cropped: Bool) {
        var args = ["process", "--input", input, "--video-codec", "prores422lt", "--deflip", "--output", output]
        var parameters: Data?
        let sq = c.squeeze ?? 1
        if let w = c.width, let h = c.height, let fc = settings.frameCrop(width: Int(Double(w) * sq), height: h) {
            let crop = (x: Int(Double(fc.x) / sq) & ~1, y: fc.y, w: Int(Double(fc.w) / sq) & ~1, h: fc.h)
            let outH = settings.height & ~1  // same height for every proxy, whatever the crop
            let outW = Int((Double(outH) * Double(crop.w) * sq / Double(crop.h) / 2).rounded()) * 2
            let params: [String: Any] = [
                "inputCrop": ["displayXOffsetOverride": crop.x, "displayYOffsetOverride": crop.y,
                              "displayWidthOverride": crop.w, "displayHeightOverride": crop.h],
                "outputScaling": ["targetWidth": outW, "targetHeight": outH],
            ]
            parameters = try? JSONSerialization.data(withJSONObject: params)
        }
        if parameters != nil {
            args += ["--parameters", parametersFile]
        } else {
            args += ["--desqueeze", "--output-height", String(settings.height)]
        }
        if look.log {
            args += ["--target-colorspace", look.arriColorSpace ?? colorSpace(lut: nil, info: c)]
        } else {
            args += ["--target-colorspace", "Rec.709/D65/BT.1886", "--embedded-look"]
        }
        return (args, parameters, parameters != nil)
    }

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        guard singleFrameAt == nil else { throw ToolError(tool: "art-cmd", status: -1, message: "thumbnail from proxy") }
        let dir = try RawBackends.freshDir(tempDir, "ari")
        let out = dir + "/intermediate.mxf"
        let file = dir + "/parameters.json"
        let a = ARRIRAWBackend.args(input: path, output: out, info: c, settings: settings, look: look, parametersFile: file)
        if let json = a.parameters { try json.write(to: URL(fileURLWithPath: file)) }
        try ToolProcess.run(tools.artCmd!, a.args, cwd: dir)
        guard FileManager.default.fileExists(atPath: out) else { throw ToolError(tool: "art-cmd", status: -1, message: "no output") }
        return FrameSource(inputArgs: ["-i", out], alreadyCropped: a.cropped, cleanup: [dir])
    }
}

// MARK: - Canon Cinema RAW Light — no command-line decoder exists for macOS

struct CanonRawBackend: RawBackend {
    let format = ClipFormat.canonRaw
    let missingHint = "Canon Cinema RAW Light: pro macOS neexistuje dekodér pro příkazovou řádku — zkopírováno a ověřeno, proxy přeskočeno"

    func isAvailable(_ tools: ExternalTools) -> Bool { false }
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo { throw ToolError(tool: "Canon RAW", status: -1, message: missingHint) }
    func source(_ path: String, info: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        throw ToolError(tool: "Canon RAW", status: -1, message: missingHint)
    }
}
```

- [ ] **Step 5: `Report.swift` (`ClipEntry`)** — za `thumbnails` přidat vlastnost a rozšířit `CodingKeys`:

```swift
    /// The LUT applied to this clip's proxy and thumbnails, and why (JSON "lut"; absent when nil, like Windows).
    public var lut: LutUse?

    enum CodingKeys: String, CodingKey {
        case clip, info, probeError, proxyStatus, proxyPath, proxyNote, proxySeconds, lut
    }
```

(staré `CodingKeys` nahradit; `thumbnails` v nich dál nejsou.)

- [ ] **Step 6: `Job.swift`**

1. K vlastnostem `OffloadJob` (úkol 4) přidat a interní `init` nahradit:

```swift
    /// The built-in LUTs the job chooses from (tests pass the repo's shared/luts).
    let luts: LutCatalog

    /// Tests pass their own engine options (allowSourceVolume, beforeFileCopy …) and LUT catalog.
    init(config: JobConfig, options: OffloadOptions, luts: LutCatalog = .shared) {
        self.config = config
        self.luts = luts
        engine = OffloadEngine(options: options)
    }
```

2. Celé `analyzeAndRender(plan:report:log:)` nahradit (obsahuje úpravy úkolů 3, 7, 19 a 20):

```swift
    private func analyzeAndRender(plan: OffloadPlan, report input: OffloadReport, log: ((String) -> Void)?) -> OffloadReport {
        var report = input
        let tools = config.tools.resolved()
        let renderer = ProxyRenderer(settings: config.proxy, tools: tools)
        renderer.log = log   // software retries and unkillable ffmpeg processes go to the job's log
        let clips = ClipDetector.detect(plan.scan.files)
        guard !clips.isEmpty else { return report }

        let verified = report.verified
        let fileIndex = Dictionary(uniqueKeysWithValues: report.files.enumerated().map { ($0.element.path, $0.offset) })
        /// First destination that holds a good copy of every segment — clips are read from disk, never from the card.
        func goodDest(_ clip: Clip) -> Int? {
            report.destinations.indices.first { d in
                clip.segments.allSatisfy { seg in
                    fileIndex[seg].map { OffloadReport.isGood(report.files[$0].destinations[d], verified: verified) } ?? false
                }
            }
        }

        var entries = clips.map(ClipEntry.init)
        update {
            $0.phase = .analyzing
            $0.clipCount = entries.count
        }
        for i in entries.indices {
            if isCancelled { break }
            let clip = entries[i].clip
            update {
                $0.clipIndex = i
                $0.clipName = clip.name
                $0.clipFraction = 0
            }
            guard let d = goodDest(clip) else {
                entries[i].probeError = "no verified copy"
                continue
            }
            if let reason = renderer.unsupportedReason(clip), clip.format != .video {
                entries[i].probeError = reason
                continue
            }
            do {
                var info = try renderer.probe(clip, cardPath: plan.cardPaths[d])
                if clip.format == .video && info.width == nil {
                    if let (arri, arriInfo) = renderer.reclassifyUndecodableMXF(clip, cardPath: plan.cardPaths[d]) {
                        entries[i].clip = arri
                        info = arriInfo
                    } else {
                        entries[i].info = info
                        entries[i].probeError = "kodek nepodporován ffmpegem (např. Sony X-OCN) — zkopírováno a ověřeno, bez proxy"
                        continue
                    }
                }
                entries[i].info = info
                // The Sony XML is read from the destination: it sits next to the clip on the card, so it was copied too.
                entries[i].lut = LutResolver.resolve(entries[i].clip, info: info,
                                                     clipFullPath: plan.cardPaths[d] + "/" + entries[i].clip.path,
                                                     settings: config.proxy, catalog: luts)
                // RED and ARRI raw cannot give a single frame directly; their thumbnails come from the proxy later.
                if entries[i].clip.format != .r3d && entries[i].clip.format != .arriraw {
                    entries[i].thumbnails = tryThumbnails(renderer, entries[i].clip, cardPath: plan.cardPaths[d], info: info,
                                                          lut: entries[i].lut, log: log)
                }
            } catch {
                entries[i].probeError = "\(error)"
                log?("probe \(clip.name): \(error)")
            }
        }
        report.clips = entries
        report.proxy = config.proxy
        rewriteReports(report, plan: plan)

        guard config.proxy.enabled, !isCancelled else { return report }
        update {
            $0.phase = .proxies
            $0.clipCount = entries.count
        }
        var usedNames = Set<String>()
        for i in entries.indices {
            if isCancelled { break }
            let clip = entries[i].clip  // may have been reclassified during analysis (MXF → ARRIRAW)
            update {
                $0.clipIndex = i
                $0.clipName = clip.name
                $0.clipFraction = 0
            }
            if clip.isCameraProxy && config.proxy.skipCameraProxies {
                entries[i].proxyStatus = .skipped
                continue
            }
            if let reason = renderer.unsupportedReason(clip) ?? (entries[i].info != nil && entries[i].info?.width == nil ? entries[i].probeError : nil) {
                entries[i].proxyStatus = .unsupported
                entries[i].proxyNote = reason
                continue
            }
            guard let d = goodDest(clip), let info = entries[i].info else {
                entries[i].proxyStatus = .failed
                entries[i].proxyNote = entries[i].probeError ?? "no verified copy"
                update { $0.proxiesFailed += 1 }
                continue
            }
            var name = clip.name
            var n = 2
            while usedNames.contains(name.lowercased()) { name = "\(clip.name)_\(n)"; n += 1 }
            usedNames.insert(name.lowercased())
            let rel = Layout.proxyDirName + "/" + name + ".mov"

            let t0 = Date()
            do {
                try renderer.render(clip, cardPath: plan.cardPaths[d], info: info, lut: entries[i].lut,
                                    output: plan.cardPaths[d] + "/" + rel,
                                    progress: { f in self.update { $0.clipFraction = f } },
                                    isCancelled: { self.isCancelled })
                entries[i].proxyStatus = .done
                entries[i].proxyPath = rel
                if config.proxy.copyToAllDestinations {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
                    for (o, other) in plan.cardPaths.enumerated() where o != d {
                        do {
                            try ProxyCopy.copyFile(plan.cardPaths[d] + "/" + rel, to: other + "/" + rel)
                        } catch {
                            let note = "kopie na \(other) selhala: \(error)"
                            entries[i].proxyNote = entries[i].proxyNote.map { "\($0); \(note)" } ?? note
                            log?("proxy \(clip.name): copy to \(other) failed: \(error)")
                        }
                    }
                }
                if let extra = config.proxy.extraFolder {
                    // The user's proxy folder: also never fails the proxy (the disk may have been disconnected).
                    let (_, error) = ProxyCopy.toExtraFolder(plan.cardPaths[d] + "/" + rel, extraFolder: extra, date: plan.date,
                                                             cardName: plan.cardName, fileName: name + ".mov")
                    if let error {
                        let note = "kopie do \(extra) selhala: \(error)"
                        entries[i].proxyNote = entries[i].proxyNote.map { "\($0); \(note)" } ?? note
                        log?("proxy \(clip.name): copy to \(extra) failed: \(error)")
                    }
                }
                if entries[i].thumbnails.isEmpty {
                    // RAW formats rendered via an intermediate: take the thumbnails from the finished proxy (it has the LUT).
                    let proxyClip = Clip(name: name, path: rel, format: .video, segments: [rel], bytes: 0, isCameraProxy: false)
                    entries[i].thumbnails = tryThumbnails(renderer, proxyClip, cardPath: plan.cardPaths[d], info: info,
                                                          lut: nil, log: log)
                }
                entries[i].proxySeconds = Date().timeIntervalSince(t0)
                update { $0.proxiesDone += 1 }
            } catch {
                entries[i].proxyStatus = .failed
                entries[i].proxyNote = "\(error)"
                update { $0.proxiesFailed += 1 }
                log?("proxy \(clip.name): \(error)")
            }
        }
        report.clips = entries
        rewriteReports(report, plan: plan)
        return report
    }
```

3. `tryThumbnails` (úkol 7) nahradit:

```swift
    private func tryThumbnails(_ renderer: ProxyRenderer, _ clip: Clip, cardPath: String, info: ClipInfo,
                               lut: LutUse?, log: ((String) -> Void)?) -> [Thumbnail] {
        do {
            return try renderer.thumbnails(clip, cardPath: cardPath, info: info, lut: lut, isCancelled: { self.isCancelled })
        } catch {
            if !isCancelled { log?("thumbnails \(clip.name): \(error)") }
            return []
        }
    }
```

- [ ] **Step 7: `HTMLReport.swift`**

1. V hlavním CSS nahradit `.clipname{font-weight:600}.path{color:var(--muted);font-size:12px}` za
   `.clipname{font-weight:600}.path,.note{color:var(--muted);font-size:12px}`.
2. Celý blok `if !r.clips.isEmpty { … }` nahradit:

```swift
        if !r.clips.isEmpty {
            h += "<h2>Klipy</h2><div class=\"panel\"><table>\n"
            h += "<tr><th></th><th>Klip</th><th>Formát</th><th>Rozlišení</th><th class=\"num\">FPS</th><th class=\"num\">Délka</th><th>TC</th><th class=\"num\">Audio</th><th class=\"num\">Velikost</th><th>Proxy</th></tr>\n"
            for c in r.clips {
                let img = c.thumbnails.isEmpty ? "<div class=\"nothumb\"></div>"
                    : "<div class=\"thumbs\">" + c.thumbnails.map { t in
                        "<figure><img class=\"thumb\" src=\"data:image/jpeg;base64,\(t.jpeg.base64EncodedString())\" alt=\"\"><figcaption>\(esc(t.label))</figcaption></figure>"
                    }.joined() + "</div>"
                let i = c.info
                var codecParts: [String] = []
                if c.clip.format != .video { codecParts.append(c.clip.format.label) }
                if let codec = i?.codec, !codecParts.contains(codec) { codecParts.append(codec) }
                let codec = codecParts.joined(separator: " · ")
                let (pc, pl) = proxyLabel(c)
                var notes: [String] = []
                for n in [c.probeError, c.proxyNote].compactMap({ $0 }) where !notes.contains(n) { notes.append(n) }
                let note = notes.joined(separator: " · ")
                var noteHTML = note.isEmpty ? "" : "<div class=\"path err\">\(esc(note))</div>"
                // Which LUT and why — also "bez LUT (reason)", unless the user switched LUTs off.
                if let lu = c.lut, lu.source != "none" {
                    noteHTML += "<div class=\"note\">LUT: \(esc(lu.name)) (\(esc(lu.reason)))</div>"
                } else if let ln = c.lut, ln.reason != "vypnuto v nastavení" {
                    noteHTML += "<div class=\"note\">LUT: bez LUT (\(esc(ln.reason)))</div>"
                }
                h += "<tr><td>\(img)</td><td class=\"clip\"><div class=\"clipname\">\(esc(c.clip.name))\(c.clip.isCameraProxy ? " <span class=\"muted\">(proxy kamery)</span>" : "")</div>"
                h += "<div class=\"path\">\(esc(c.clip.path))</div>\(noteHTML)</td>"
                h += "<td class=\"wrap\">\(esc(codec))</td><td>\(esc(i?.resolutionLabel ?? ""))</td><td class=\"num\">\(esc(i?.fpsLabel ?? ""))</td>"
                h += "<td class=\"num\">\(esc(i?.durationLabel ?? ""))</td><td class=\"mono\">\(esc(i?.timecode ?? ""))</td>"
                h += "<td class=\"num\">\(i.map { $0.audioChannels > 0 ? "\($0.audioChannels) ch" : "—" } ?? "")</td>"
                h += "<td class=\"num\">\(formatBytes(c.clip.bytes))</td><td class=\"\(pc)\">\(pl)</td></tr>\n"
            }
            h += "</table></div>\n"
            if let p = r.proxy, p.enabled {
                let lut: String
                switch LutResolver.effectiveMode(p) {
                case .auto: lut = "automaticky podle kamery"
                case .builtin: lut = LutCatalog.shared.find(p.lutBuiltin)?.name ?? p.lutBuiltin ?? "bez LUT"
                case .custom: lut = p.lutPath.flatMap { $0.isEmpty ? nil : ($0 as NSString).lastPathComponent } ?? "bez LUT"
                case .off: lut = "bez LUT"
                }
                let frame = p.frameLabel.map { ", výřez \($0)" } ?? ""
                let extra = p.extraFolder.map { " · také ve složce <span class=\"mono\">\(esc($0))</span>" } ?? ""
                h += "<p class=\"sub\">Proxy: \(esc(p.codec.label)), \(p.height)p\(frame), LUT: \(esc(lut)) → <span class=\"mono\">\(Layout.proxyDirName)/</span>\(extra)</p>\n"
            }
        }
```

- [ ] **Step 8: Testy projdou** — Run: `swift test --filter "LutRenderTests|LutReportTests|ColorTests|ThumbnailTests|StageTwoTests|FFmpegWatchTests|ProxyFolderTests|HTMLReportTests"` → 0 chyb; pak `swift test`.

Pokud `testSLog3GreyProxyIs042` vyjde mimo 0,42 ± 0,03: vypsat `ffprobe -show_streams` zdrojového klipu (`color_range`)
a zkusit totéž bez VideoToolbox (dočasně `usesHardwareDecode` → `false`) — rozdíl znamená, že VT dekodér mění rozsah
(Doplněk D.10, řádek 4); výsledek do commitu, oprava do `needsPreMap`/filtru, ne do testu.

- [ ] **Step 9: Ruční kontrola** (vzorky z kamer, jsou-li): `swift build -c release && .build/release/offload <karta> -d <USB disk> --proxy`
→ v HTML reportu u každého klipu řádek „LUT: … (důvod)“, proxy z log záznamu vypadají jako Rec.709; Sony klip natočený
v Rec.709 „LUT: bez LUT (Sony XML: rec709 (není log))“. BRAW (je-li `braw-decode`): šedá v proxy přirozená, ne vybledlá
(bez pre-map by byla posunutá). Na vzorcích ověřit i rozsah R3D/ARRIRAW mezisouborů (spec LUTů §6 bod 5).

- [ ] **Step 10: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 22 — LUT pro každý klip v proxy i náhledech; BRAW a plnorozsahové video přepočtené na konvenci vestavěných LUTů (lutrgb), RED a ARRIRAW vydají log jen s LUTem; v reportu u klipu „LUT: … (důvod)“ a klíč lut, v řádku Proxy způsob výběru LUTu`

`docs/ROZDILY.md` — řádek „**Vestavěné LUTy + automatický výběr podle kamery**“: Mac `🔨 jádro (CLI a GUI ⏳)`.
`docs/FORMATY.md` — u `clips[].lut` místo „Windows od plánu 5, Mac zatím ignoruje“ jen „(obě verze)“; u `proxy` zrušit
poznámku, že Mac nové klíče LUTu ignoruje (je-li tam).

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: per-clip LUT in proxies and thumbnails, full-range pre-map, RAW log only with a LUT, LUT in the report"`

---

### Task 23: CLI — `--lut auto|none|<id>|<cesta>`, `--lut-fallback`, `offload luts`, `--proxy-folder`

Doplněk ke spec — oddíly D.2 a D.3.6. Port `windows/src/Offload.Cli/CopyCommand.cs` (volby LUTu a složky pro proxy,
výpis „LUT: …“ a „proxy also → …“) a `LutsCommand.cs`.

**Files:**
- Modify: `mac/Sources/offload/main.swift` (`Offload.configuration`, `Copy` — volby a začátek `run()`, nový `Copy.lutLine`, nový `Luts`)
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `LutOption.parse`, `LutCatalog.shared`, `LutResolver.effectiveMode` (úkol 21), `ProxyPreflight.extraFolderInUse`, `ProxyPreflight.isDirectory` (úkol 20), `abs(_:)` (main.swift).
- Produces: `offload copy … [--lut auto|none|<id>|<cesta.cube>] [--lut-fallback <id>|none] [--proxy-folder <složka>]`,
  subcommand `offload luts`, `static func Copy.lutLine(_ p: ProxySettings) -> String`.
  **Změna chování:** bez `--lut` je teď režim `auto` (LUT podle kamery), dřív žádný LUT — stejně jako Windows.

- [ ] **Step 1: Test-first** — parsování voleb je pokryté `LutOptionTests` (úkol 21), pravidla složky `ProxyFolderTests`
(úkol 20). CLI se ověřuje ručně (krok 4). Run: `swift test` → zelené.

- [ ] **Step 2: `main.swift`**

`Offload.configuration` — podpříkazy (po úkolu 16):

```swift
        subcommands: [Copy.self, Verify.self, Bench.self, Ports.self, Luts.self],
```

V `struct Copy` nahradit volbu `lut` a za `noBurnIn` přidat dvě nové:

```swift
    @Option(help: "LUT for proxies and thumbnails: auto (by camera, default), none, a built-in id (offload luts prints them) or a .cube path.")
    var lut: String?
```

```swift
    @Option(help: "Built-in LUT id (or none) for clips whose camera is not recognised with --lut auto.")
    var lutFallback: String?

    @Option(help: "Also copy every proxy to <folder>/<date>/<card name>/ (with --proxy; never overwrites).")
    var proxyFolder: String?
```

V `Copy.run()` nahradit vše od začátku funkce až **po** řádek `// Connection chain: informational only — resolving it never fails the copy.`
(ten a vše za ním z úkolu 16 zůstává):

```swift
    func run() throws {
        var config = JobConfig(source: abs(source), destRoots: dest.map(abs))
        config.cardName = name
        config.date = date
        config.chunkMB = chunkMb
        config.buffers = buffers
        config.verify = !noVerify
        config.allowSameDevice = allowSameDevice
        config.analyzeClips = !noAnalyze
        config.proxy.enabled = proxy
        let choice = LutOption.parse(lut, fallback: lutFallback, catalog: LutCatalog.shared)
        if let e = choice.error { throw ValidationError(e) }
        config.proxy.lutMode = choice.mode
        config.proxy.lutBuiltin = choice.builtin
        config.proxy.lutPath = choice.path.map(abs)
        config.proxy.lutFallback = choice.fallback
        if let p = config.proxy.lutPath, !FileManager.default.fileExists(atPath: p) {
            throw ValidationError("LUT not found: \(p)")
        }
        config.proxy.height = proxyHeight
        guard let codec = ProxyCodec(rawValue: proxyCodec) else {
            throw ValidationError("Unknown proxy codec \(proxyCodec)")
        }
        config.proxy.codec = codec
        config.proxy.bitrateMbps = proxyBitrate
        config.proxy.frameAspect = frameAspect
        config.proxy.frameScale = frameScale / 100
        config.proxy.burnFilename = !noBurnIn
        config.proxy.burnTimecode = !noBurnIn
        config.proxy.extraFolder = proxyFolder.map(abs)
        if config.proxy.extraFolder != nil && !proxy {
            fputs("Note: --proxy-folder has no effect without --proxy.\n", stderr)
        }
        if let folder = ProxyPreflight.extraFolderInUse(config.proxy), !ProxyPreflight.isDirectory(folder) {
            throw ValidationError("Proxy folder not found: \(folder)")
        }
        // Only when proxies or clip analysis (thumbnails) are on — otherwise no LUT is used at all (Windows LutOption.PlanLine).
        if proxy || config.analyzeClips { print("LUT: " + Copy.lutLine(config.proxy)) }

        let job = OffloadJob(config: config)
        let plan = try job.prepare()
        print("Card \(plan.cardName): \(plan.scan.files.count) files, \(formatBytes(plan.scan.totalBytes))")
        for p in plan.cardPaths { print("  → \(p)") }
        if let extra = ProxyPreflight.extraFolderInUse(config.proxy) {
            let target = URL(fileURLWithPath: extra).appendingPathComponent(plan.date).appendingPathComponent(plan.cardName).path
            print("  proxy also → \(target)")
        }
        let tools = config.tools.resolved()
        if config.analyzeClips || proxy {
            print("  ffmpeg: \(tools.ffmpeg ?? "NOT FOUND")")
        }
```

Do `struct Copy` (za `run()`) přidat:

```swift
    /// The "LUT: …" line before the copy (same wording as Windows).
    static func lutLine(_ p: ProxySettings) -> String {
        switch LutResolver.effectiveMode(p) {
        case .off: return "none"
        case .custom: return p.lutPath ?? ""
        case .builtin: return p.lutBuiltin ?? ""
        case .auto: return "auto" + (p.lutFallback.map { " (fallback \($0))" } ?? "")
        }
    }
```

Za `struct Ports` (úkol 16) přidat:

```swift
struct Luts: ParsableCommand {
    static let configuration = CommandConfiguration(abstract: "List the built-in LUTs (ids for --lut and --lut-fallback).")

    func run() throws {
        let cat = LutCatalog.shared
        if cat.luts.isEmpty { fputs("No built-in LUTs found (luts folder missing).\n", stderr) }
        for l in cat.luts { print("\(l.id) — \(l.name)") }
    }
}
```

- [ ] **Step 3: Build** — Run: `swift build`.

- [ ] **Step 4: Ruční ověření** (vývojový build najde LUTy v `shared/luts` repa — `LutCatalog.findFolder`, cesta s `/.build/`):
  1. `.build/debug/offload luts` → 11 řádků, první `arri-logc3-awg3 — ARRI LogC3 / AWG3 → Rec.709`; exit 0.
  2. `.build/debug/offload copy /tmp -d /tmp/x --lut nope --dry-run; echo "exit $?"` →
     `Error: Unknown LUT 'nope'. Use auto, none, a .cube path or one of: arri-logc3-awg3, …` a nápověda; exit 64
     (ArgumentParser `ValidationError`, stejně jako stávající „LUT not found“).
  3. `.build/debug/offload copy /tmp -d /tmp/x --lut auto --lut-fallback nope --dry-run` → `Error: Unknown fallback LUT 'nope'. Built-in: …`.
  4. `.build/debug/offload copy /tmp -d /tmp/x --proxy --proxy-folder /nonexistent --dry-run` → `Error: Proxy folder not found: /nonexistent`.
  5. `.build/debug/offload copy <karta> -d /Volumes/<USB disk> --proxy-folder /tmp --dry-run` → na stderr
     `Note: --proxy-folder has no effect without --proxy.`, `LUT: auto`, žádný řádek „proxy also“.
  6. `.build/debug/offload copy <karta> -d /Volumes/<USB disk> --proxy --proxy-folder /tmp/px --lut-fallback dji-dlog-dgamut --dry-run`
     (složku `/tmp/px` předem vytvořit) → `LUT: auto (fallback dji-dlog-dgamut)`, `  proxy also → /tmp/px/<datum>/<karta>`.
  7. Ostrý běh s `--proxy --proxy-folder /tmp/px` na kartě s klipem → `/tmp/px/<datum>/<karta>/<klip>.mov`; druhý běh
     stejné karty → `<klip>_2.mov`, první soubor beze změny (`shasum`).

- [ ] **Step 5: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 23 — CLI: --lut auto|none|<id>|<cesta> (bez volby automaticky podle kamery), --lut-fallback, offload luts, --proxy-folder (neexistující složka s --proxy = chyba před startem, bez --proxy jen poznámka)`

`docs/ROZDILY.md` — řádky „Vestavěné LUTy …“ a „Složka pro proxy navíc“: Mac `🔨 jádro + CLI, GUI ⏳`.

Run: `git add -A mac/Sources docs && git commit -m "Mac CLI: --lut auto|none|id|path, --lut-fallback, offload luts, --proxy-folder"`

---

### Task 24: GUI — výběr LUTu, záložní LUT, „Kopírovat proxy i do složky“

Doplněk ke spec — oddíly D.2 a D.3.7. Port `windows/src/Offload.App/ProxyOptionsViewModel.cs`, `SetupPanel.xaml` (řádky
LUT a složka pro proxy) a kontrol před startem z `MainViewModel.Setup.cs`/`MainViewModel.Queue.cs` (`ProxyFolderProblem`,
`RefreshProxyFolder`, kontrola LUT a složky těsně před startem s limitem 5 s).

**Files:**
- Modify: `mac/Sources/OffloadApp/AppModel.swift` (`AppModel`: nové vlastnosti, `save`, `refreshDestinations`, `proxyFolderProblem`, `canStart`, `start` + `startJobs`, `answer(within:_:)`, `pickLUT`, `lutChoice`, `setLutChoice`, `pickProxyFolder`)
- Modify: `mac/Sources/OffloadApp/Views.swift` (celá `ProxySection`)
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `LutCatalog.shared`, `LutResolver.effectiveMode`, `LutMode` (úkol 21), `ProxyPreflight.extraFolderInUse`, `extraFolderBlocking(_:folderExists:)`, `isDirectory` (úkol 20), `ProxyPreflight.lutInUse`, `blocking` (úkoly 10, 21), `AppModel` z úkolů 11 a 17.
- Produces (aplikace): `AppModel.luts: LutCatalog`, `folderExists: Bool?`, `starting: Bool`, `proxyFolderProblem: String?`,
  `lutChoice: String` (`"auto"`, id vestavěného, `"none"`, `"custom"`), `setLutChoice(_:)`, `@discardableResult pickLUT() -> Bool`,
  `pickProxyFolder()`, `nonisolated static func answer(within:_:) -> Bool`.

- [ ] **Step 1: Test-first** — logika je v OffloadKit a testovaná (`LutSettingsTests`, `LutResolverTests`, `ProxyFolderTests`).
GUI se ověřuje ručně (krok 5). Run: `swift test` → zelené.

- [ ] **Step 2: `AppModel.swift` (`AppModel`)**

1. K vlastnostem (za `@Published private(set) var terminating = false`) přidat:

```swift
    @Published private(set) var folderExists: Bool?
    /// The LUT and proxy folder are being checked right before a start (at most 5 s each).
    @Published private(set) var starting = false
    /// The built-in LUTs (the app bundle's Resources/luts; already loaded by SettingsStore.decode at launch).
    let luts = LutCatalog.shared
    private var folderChecked: String?
    private var probedFolder: String?
```

2. `save()` nahradit:

```swift
    private func save() {
        if let data = SettingsStore.encode(settings) { UserDefaults.standard.set(data, forKey: SettingsStore.defaultsKey) }
        let t = settings.tools.resolved()
        if t != tools { tools = t }
        if settings.destRoots != probedRoots
            || ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips) != probedLut
            || ProxyPreflight.extraFolderInUse(settings.proxy) != probedFolder {
            refreshDestinations()
        }
    }
```

3. `refreshDestinations()` nahradit (kontroluje i složku pro proxy, na stejné frontě jako cíle):

```swift
    /// Free space / filesystem / existence of every destination, whether the custom LUT and the proxy folder exist —
    /// every 5 s, after a change and from the refresh button. Never on the main thread.
    func refreshDestinations() {
        if probing {
            probeAgain = true   // runs once the check in flight returns (it may hang on a network disk)
            return
        }
        probing = true
        let roots = settings.destRoots
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        let folder = ProxyPreflight.extraFolderInUse(settings.proxy)
        probedRoots = roots
        probedLut = lut
        probedFolder = folder
        destQueue.async { [weak self] in
            var found: [String: DestinationStatus] = [:]
            for r in roots { found[r] = AppModel.probe(r) }
            let lutOK = lut.map { FileManager.default.fileExists(atPath: $0) }
            let folderOK = folder.map { ProxyPreflight.isDirectory($0) }
            DispatchQueue.main.async {
                guard let self else { return }
                self.destStatus = found
                self.lutExists = lutOK
                self.lutChecked = lut
                self.folderExists = folderOK
                self.folderChecked = folder
                self.probing = false
                if self.probeAgain {
                    self.probeAgain = false
                    self.refreshDestinations()
                }
            }
        }
    }
```

4. Za `lutProblem` přidat:

```swift
    /// A missing proxy folder (disconnected disk) blocks the start — from the last background check; nil until checked.
    var proxyFolderProblem: String? {
        let folder = ProxyPreflight.extraFolderInUse(settings.proxy)
        return ProxyPreflight.extraFolderBlocking(settings.proxy, folderExists: folder == folderChecked ? folderExists : nil)
    }
```

5. `canStart` nahradit:

```swift
    var canStart: Bool {
        !terminating && !starting && !selectedSources.isEmpty && !settings.destRoots.isEmpty
            && settings.destRoots.allSatisfy { destStatus[$0]?.exists == true }
            && lutProblem == nil && proxyFolderProblem == nil
    }
```

6. `start()` (úkol 11) přejmenovat na `private func startJobs()` a smazat v ní první řádek `guard canStart else { return }`
   (zbytek těla beze změny). Nad ni přidat nové `start()` a pomocnou `answer`:

```swift
    /// The custom LUT and the proxy folder are checked once more right before the start (they may sit on a disk that
    /// just went away) — off the main thread, at most 5 s each; meanwhile the start button is disabled.
    func start() {
        guard canStart else { return }
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        let folder = ProxyPreflight.extraFolderInUse(settings.proxy)
        guard lut != nil || folder != nil else { return startJobs() }
        starting = true
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let lutOK = lut.map { path in AppModel.answer(within: 5) { FileManager.default.fileExists(atPath: path) } }
            let folderOK = folder.map { path in AppModel.answer(within: 5) { ProxyPreflight.isDirectory(path) } }
            DispatchQueue.main.async {
                guard let self else { return }
                self.starting = false
                // Settings may have changed meanwhile: only an answer for what is still in use counts.
                if let lut, lut == ProxyPreflight.lutInUse(self.settings.proxy, analyzeClips: self.settings.analyzeClips) {
                    self.lutExists = lutOK
                    self.lutChecked = lut
                }
                if let folder, folder == ProxyPreflight.extraFolderInUse(self.settings.proxy) {
                    self.folderExists = folderOK
                    self.folderChecked = folder
                }
                if self.canStart { self.startJobs() }   // otherwise the red text says why
            }
        }
    }

    /// `check` on its own thread; false when it has not answered within `seconds` (a hung network disk).
    nonisolated static func answer(within seconds: Double, _ check: @escaping () -> Bool) -> Bool {
        final class Box: @unchecked Sendable { var value = false }
        let box = Box()
        let done = DispatchSemaphore(value: 0)
        DispatchQueue.global(qos: .userInitiated).async {
            box.value = check()
            done.signal()
        }
        return done.wait(timeout: .now() + seconds) == .success && box.value
    }
```

7. `pickLUT()` nahradit a přidat výběr LUTu a složky:

```swift
    /// The .cube dialog; a picked file switches to custom mode. False when cancelled (nothing changes).
    @discardableResult
    func pickLUT() -> Bool {
        let panel = NSOpenPanel()
        panel.title = "Vyber LUT (.cube)"
        panel.allowedContentTypes = [.init(filenameExtension: "cube")!]
        guard panel.runModal() == .OK, let url = panel.url else { return false }
        settings.proxy.lutPath = url.path
        settings.proxy.lutMode = .custom
        return true
    }

    /// The LUT picker's value: "auto", a built-in id, "none" or "custom". An unknown built-in id shows as auto (like Windows).
    var lutChoice: String {
        switch LutResolver.effectiveMode(settings.proxy) {
        case .auto: return "auto"
        case .off: return "none"
        case .custom: return "custom"
        case .builtin:
            let id = settings.proxy.lutBuiltin ?? ""
            return luts.find(id) != nil ? id : "auto"
        }
    }

    func setLutChoice(_ choice: String) {
        switch choice {
        case "auto":
            settings.proxy.lutMode = .auto
        case "none":
            settings.proxy.lutMode = .off
        case "custom":
            if (settings.proxy.lutPath ?? "").isEmpty {
                // Not inside the Picker's own update: the panel opens right after; Cancel keeps the previous choice.
                DispatchQueue.main.async { [weak self] in
                    guard let self else { return }
                    if !self.pickLUT() { self.objectWillChange.send() }
                }
                return
            }
            settings.proxy.lutMode = .custom
        default:
            settings.proxy.lutBuiltin = choice
            settings.proxy.lutMode = .builtin
        }
    }

    func pickProxyFolder() {
        guard let path = pickFolder(title: "Vyber složku pro proxy") else { return }
        settings.proxy.extraFolder = path
    }
```

- [ ] **Step 3: `Views.swift`** — `struct ProxySection` nahradit celou:

```swift
struct ProxySection: View {
    @EnvironmentObject var model: AppModel

    private var lutMode: LutMode { LutResolver.effectiveMode(model.settings.proxy) }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionHeader(title: "Proxy", systemImage: "film.stack")
            Toggle("Renderovat proxy po ověření", isOn: $model.settings.proxy.enabled)
            if model.settings.proxy.enabled {
                Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 8) {
                    GridRow {
                        Text("Kodek").foregroundStyle(.secondary)
                        Picker("", selection: $model.settings.proxy.codec) {
                            ForEach(ProxyCodec.allCases) { Text($0.label).tag($0) }
                        }.labelsHidden()
                    }
                    GridRow {
                        Text("Výška").foregroundStyle(.secondary)
                        Picker("", selection: $model.settings.proxy.height) {
                            ForEach([540, 720, 1080, 2160], id: \.self) { Text("\($0)p").tag($0) }
                        }.labelsHidden().pickerStyle(.segmented)
                    }
                    if model.settings.proxy.codec == .h264 || model.settings.proxy.codec == .hevc {
                        GridRow {
                            Text("Datový tok").foregroundStyle(.secondary)
                            Stepper("\(model.settings.proxy.bitrateMbps) Mb/s", value: $model.settings.proxy.bitrateMbps, in: 2...50)
                        }
                    }
                    GridRow {
                        Text("Výřez").foregroundStyle(.secondary)
                        HStack {
                            Picker("", selection: $model.settings.proxy.frameAspect) {
                                Text("celý obraz").tag(Double?.none)
                                ForEach([1.33, 1.66, 1.78, 1.85, 2.0, 2.39], id: \.self) { a in
                                    Text(String(format: "%.2f", a)).tag(Double?.some(a))
                                }
                            }.labelsHidden().frame(maxWidth: 130)
                            if model.settings.proxy.frameAspect != nil {
                                Stepper("\(Int((model.settings.proxy.frameScale * 100).rounded())) % obrazu",
                                        value: $model.settings.proxy.frameScale, in: 0.5...1.0, step: 0.01)
                                    .help("Velikost frame lines vůči obrazu – 90 % = 10 % overscan")
                            }
                        }
                    }
                    GridRow {
                        Text("LUT").foregroundStyle(.secondary)
                        VStack(alignment: .leading, spacing: 4) {
                            Picker("", selection: Binding(get: { model.lutChoice }, set: { model.setLutChoice($0) })) {
                                Text("Automaticky podle kamery").tag("auto")
                                ForEach(model.luts.luts, id: \.id) { Text($0.name).tag($0.id) }
                                Text("Bez LUT").tag("none")
                                Text("Vlastní…").tag("custom")
                            }.labelsHidden()
                            if lutMode == .auto {
                                Text("Když kameru nepoznám:").font(.caption).foregroundStyle(.secondary)
                                Picker("", selection: Binding(get: { model.settings.proxy.lutFallback ?? "none" },
                                                              set: { model.settings.proxy.lutFallback = $0 == "none" ? nil : $0 })) {
                                    Text("Bez LUT").tag("none")
                                    ForEach(model.luts.luts, id: \.id) { Text($0.name).tag($0.id) }
                                }.labelsHidden()
                            }
                            if lutMode == .custom {
                                HStack {
                                    Text(model.settings.proxy.lutPath.map { ($0 as NSString).lastPathComponent } ?? "nevybráno")
                                        .lineLimit(1).truncationMode(.middle)
                                        .help(model.settings.proxy.lutPath ?? "")
                                    Spacer()
                                    Button("Vybrat…") { model.pickLUT() }
                                }
                            }
                        }
                    }
                }
                Toggle("Burn-in názvu klipu", isOn: $model.settings.proxy.burnFilename)
                Toggle("Burn-in timecodu", isOn: $model.settings.proxy.burnTimecode)
                Toggle("Zvuk", isOn: $model.settings.proxy.audio)
                Toggle("Přeskočit proxy nahrané kamerou (SUB/PROXY)", isOn: $model.settings.proxy.skipCameraProxies)
                Toggle("Kopírovat proxy na všechny cíle", isOn: $model.settings.proxy.copyToAllDestinations)
                Text("Kopírovat proxy i do složky").font(.caption).foregroundStyle(.secondary).padding(.top, 4)
                HStack {
                    Text(model.settings.proxy.extraFolder.map { ($0 as NSString).lastPathComponent } ?? "nevybráno")
                        .lineLimit(1).truncationMode(.middle)
                        .foregroundStyle(model.settings.proxy.extraFolder == nil ? .secondary : .primary)
                        .help(model.settings.proxy.extraFolder ?? "")
                    Spacer()
                    Button("Vybrat…") { model.pickProxyFolder() }
                    if model.settings.proxy.extraFolder != nil {
                        Button { model.settings.proxy.extraFolder = nil } label: { Image(systemName: "xmark.circle.fill") }
                            .buttonStyle(.borderless).help("Nekopírovat do složky")
                    }
                }
            }
            // Outside the proxy block (as on Windows): a missing custom LUT also blocks a start with proxies off
            // when clip analysis (thumbnails) uses it — the reason must stay visible.
            if let p = model.lutProblem { Text(p).font(.caption).foregroundStyle(.red) }
            if let p = model.proxyFolderProblem { Text(p).font(.caption).foregroundStyle(.red) }
        }
    }
}
```

(Kořen svazku jako složka pro proxy, např. `/Volumes/PROXY`, má `lastPathComponent` „PROXY“ — Windows ukazuje u kořene
celou cestu; na Macu název svazku stačí, plná cesta je v nápovědě.)

- [ ] **Step 4: Build** — Run: `swift build && swift test && scripts/build-app.sh` → bez chyb. Hlášku izolace v uzávěrech
řešit jako v úkolu 11 (krok 5).

- [ ] **Step 5: Ruční test** — `open build/Offload.app` (nastavení zálohovat: `defaults read cz.budin.offload settings > /tmp/offload-settings-backup.txt`):
  1. **Starý vlastní LUT přežije aktualizaci:** s nastavením z verze 0.1.0, kde byl vybraný `.cube`, ukáže LUT „Vlastní…“
     s názvem souboru (migrace `lutPath` → `custom`); bez LUTu „Automaticky podle kamery“ a pod ním „Když kameru nepoznám: Bez LUT“.
  2. Seznam LUT: „Automaticky podle kamery“, 11 vestavěných (název z `luts.json`), „Bez LUT“, „Vlastní…“. „Vlastní…“
     bez vybraného souboru otevře dialog; „Zrušit“ vrátí předchozí volbu.
  3. Vybrat vestavěný LUT, aplikaci zavřít a otevřít → volba zůstane (`defaults read cz.budin.offload settings` → `"lutMode":"builtin"`).
  4. „Kopírovat proxy i do složky“ → „Vybrat…“ složku na USB disku → název složky (plná cesta v nápovědě). USB disk
     odpojit → do 5 s červeně „Složka pro proxy není dostupná: /Volumes/…“ a „Spustit offload“ neaktivní; připojit → zmizí.
     Křížek složku zruší.
  5. Odpojit disk se složkou těsně před kliknutím na „Spustit offload“ (než to stihne pětisekundová kontrola) → start se
     neprovede, objeví se červený text (kontrola těsně před startem).
  6. Ostrý offload karty s proxy a složkou → proxy v `_PROXY` na cílech i v `<složka>/<datum>/<karta>/`; v reportu
     u klipů „LUT: … (důvod)“ a v řádku Proxy „· také ve složce …“.

- [ ] **Step 6: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 24 — GUI: výběr LUTu (automaticky / vestavěný / bez / vlastní) se záložním LUTem „Když kameru nepoznám:“, „Kopírovat proxy i do složky“; nedostupná složka nebo chybějící vlastní LUT blokuje start (ověřuje se na pozadí každých 5 s i těsně před startem, nejvýš 5 s)`

`docs/VYVOJ-MAC.md` oddíl „Naplánováno“: beze změny (samostatné odrážky o složce pro proxy a LUTech nahradil 2026-10-05 odkaz na plán).

`docs/ROZDILY.md`:
- řádky „**Vestavěné LUTy + automatický výběr podle kamery**“ a „**Složka pro proxy navíc**“: Mac `✅ CLI + GUI (GUI čeká ruční test)`;
- v „Záměrných rozdílech“ smazat odrážku „**Složka pro proxy navíc** (zatím jen Windows, pro Mac naplánováno) …“ a odrážku
  „**Vestavěné LUTy** …“ přepsat na obě verze — ponechat jen omezení, která platí pro obě (Canon/Panasonic/DJI/Fuji se zatím
  automaticky nepoznají, D-Log M bez LUTu, ARRI v Rec.709 dostane LUT podle modelu, rozsah R3D/ARRIRAW mezisouborů a ProRes
  z ALEXA 35 ověřit na vzorcích), smazat větu „Mac nová nastavení … ignoruje a dál používá jen `proxy.lutPath`“.

Run: `git add -A mac/Sources docs && git commit -m "Mac GUI: LUT choice with fallback, extra proxy folder, start blocked by a missing folder or custom LUT"`

---

### Task 25: Balíček — vestavěné LUTy a ikona v `Offload.app`

Doplněk ke spec — oddíly D.3.1 a D.6. LUTy (`shared/luts/*.cube` + `luts.json`) do `Contents/Resources/luts/`, ikona
z `shared/icon/Offload.iconset` přes `iconutil` do `Contents/Resources/Offload.icns`, `CFBundleIconFile` v Info.plist.
Python k sestavení **není** potřeba — `.cube` i PNG ikony jsou v repu commitnuté.

**Files:**
- Modify (celý soubor): `mac/scripts/build-app.sh`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `shared/luts/luts.json`, `shared/luts/*.cube`, `shared/icon/Offload.iconset/icon_{16,32,128,256,512}x…{,@2x}.png`, `LutCatalog.findFolder` (úkol 21 — CLI v `Contents/Resources/bin` najde `../luts`).
- Produces: `build/Offload.app/Contents/Resources/luts/` (12 souborů), `build/Offload.app/Contents/Resources/Offload.icns`, Info.plist `CFBundleIconFile = Offload`.

- [ ] **Step 1: Ověřit, že dnes chybí** — Run: `scripts/build-app.sh && ls build/Offload.app/Contents/Resources/luts; build/Offload.app/Contents/Resources/bin/offload luts; echo "exit $?"`
Expected: `ls: … No such file or directory`, `No built-in LUTs found (luts folder missing).`, exit 0; Info.plist bez `CFBundleIconFile`.

Předem ověřit iconset (Doplněk D.10, řádek 7): `sips -g pixelWidth -g pixelHeight ../shared/icon/Offload.iconset/*.png` →
16, 32, 32, 64, 128, 256, 256, 512, 512, 1024 podle jmen (`icon_16x16@2x.png` = 32 px …).

- [ ] **Step 2: `mac/scripts/build-app.sh`** — celý nový obsah:

```zsh
#!/bin/zsh
# Builds build/Offload.app: SwiftUI app + offload CLI + bundled ffmpeg/ffprobe/braw-decode, the built-in LUTs and the icon.
# The LUTs and the icon come from the repo's shared/ folder (committed; Python is needed only to regenerate them).
set -euo pipefail
cd "$(dirname "$0")/.."
SHARED=../shared
if [[ ! -f $SHARED/luts/luts.json ]]; then
    echo "error: $SHARED/luts/luts.json missing — build from a full clone of the repo (mac/ and shared/)"
    exit 1
fi
swift build -c release --product OffloadApp
swift build -c release --product offload
[[ -x Tools/bin/braw-decode ]] || Tools/braw-decode/build.sh

APP=build/Offload.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/bin" "$APP/Contents/Resources/luts"
cp .build/release/OffloadApp "$APP/Contents/MacOS/Offload"
cp .build/release/offload "$APP/Contents/Resources/bin/offload"
for t in ffmpeg ffprobe braw-decode; do
    [[ -x Tools/bin/$t ]] && cp Tools/bin/$t "$APP/Contents/Resources/bin/" || echo "warning: Tools/bin/$t missing"
done
# Built-in LUTs: the app finds Resources/luts, the CLI in Resources/bin finds ../luts (LutCatalog.findFolder).
cp $SHARED/luts/luts.json $SHARED/luts/*.cube "$APP/Contents/Resources/luts/"
# App icon from the shared iconset (16–512 px, @1x and @2x).
if [[ -d $SHARED/icon/Offload.iconset ]]; then
    iconutil -c icns $SHARED/icon/Offload.iconset -o "$APP/Contents/Resources/Offload.icns"
else
    echo "warning: $SHARED/icon/Offload.iconset missing — the app has no icon"
fi
VERSION=$(sed -n 's/.*static let string = "\(.*\)".*/\1/p' Sources/OffloadKit/Report.swift)
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Offload</string>
  <key>CFBundleDisplayName</key><string>Offload</string>
  <key>CFBundleIdentifier</key><string>cz.budin.offload</string>
  <key>CFBundleExecutable</key><string>Offload</string>
  <key>CFBundleIconFile</key><string>Offload</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSRemovableVolumesUsageDescription</key><string>Offload čte záznam z paměťových karet.</string>
  <key>NSNetworkVolumesUsageDescription</key><string>Offload může zapisovat na síťové disky.</string>
</dict></plist>
PLIST
codesign --force --deep --sign - "$APP" 2>/dev/null || true
echo "built $APP"
```

- [ ] **Step 3: Build a ověření**

Run: `scripts/build-app.sh && ls build/Offload.app/Contents/Resources/luts | wc -l && build/Offload.app/Contents/Resources/bin/offload luts | head -2 && plutil -p build/Offload.app/Contents/Info.plist | grep CFBundleIconFile && ls -l build/Offload.app/Contents/Resources/Offload.icns`
Expected: `12`; `arri-logc3-awg3 — ARRI LogC3 / AWG3 → Rec.709`, `arri-logc4-awg4 — ARRI LogC4 / AWG4 → Rec.709`;
`"CFBundleIconFile" => "Offload"`; soubor `.icns` (desítky až stovky kB).

Ruční: `open build/Offload.app` → ikona (karta s fajfkou na modro-tyrkysovém čtverci) v Docku, v přepínači ⌘⇥ a ve Finderu;
v sekci Proxy seznam LUT s 11 vestavěnými. Pokud Dock ukazuje starou/obecnou ikonu: `touch build/Offload.app && killall Dock`.
Symlink CLI mimo balíček (`ln -s "$PWD/build/Offload.app/Contents/Resources/bin/offload" /usr/local/bin/offload`) →
`offload luts` vypíše 11 LUTů (`resolvingSymlinksInPath`; Doplněk D.10, řádek 6).

- [ ] **Step 4: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 25 — balíček: vestavěné LUTy v Offload.app/Contents/Resources/luts (CLI je najde z Resources/bin), ikona aplikace Offload.icns z shared/icon/Offload.iconset (iconutil), CFBundleIconFile`

`docs/ROZDILY.md` — tabulka „Technologie“: přidat řádek `| Ikona aplikace | Offload.icns z shared/icon/Offload.iconset (iconutil) | Offload.ico ze shared/icon (exe i okna) |`
a řádek `| Vestavěné LUTy v balíčku | Offload.app/Contents/Resources/luts | Offload\luts |`.

Run: `git add -A mac/scripts docs && git commit -m "Mac: bundle the built-in LUTs and the app icon"`

---

### Task 26: Dokumentace, verze a ROZDILY

(Dřívější úkol 18 — přečíslovaný, protože úkoly 18–25 doplňku musí proběhnout před uzavřením dokumentace a verzí 0.2.0.
Totéž platí pro úkoly 27–31 doplňku 2 níže: **dělají se před tímto úkolem**.)

**Files:**
- Modify: `mac/Sources/OffloadKit/Report.swift` (`OffloadVersion.string`)
- Modify: `mac/README.md`, `docs/ROZDILY.md`, `docs/VYVOJ-MAC.md`, `docs/FORMATY.md`
- Create: `mac/Tests/OffloadKitTests/VersionTests.swift`

**Interfaces:**
- Produces: `OffloadVersion.string == "0.2.0"` (report, `offload --version`, `CFBundleShortVersionString` přes `build-app.sh`).

- [ ] **Step 1: Padající test** — `mac/Tests/OffloadKitTests/VersionTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class VersionTests: XCTestCase {
    func testVersionIsTheParityRelease() {
        XCTAssertEqual(OffloadVersion.string, "0.2.0")
        XCTAssertTrue(HTMLReport.render(Reports.sample()).contains("offload 0.2.0"))
    }
}
```

Run: `swift test --filter VersionTests` → selže (`"0.1.0" != "0.2.0"`).

- [ ] **Step 2: Verze** — v `Report.swift`: `public static let string = "0.2.0"`. Run: `swift test` → vše zelené.

- [ ] **Step 3: `mac/README.md`** — doplnit (za oddíl „CLI“):

~~~markdown
## Testy

```sh
swift test                    # XCTest (Tests/OffloadKitTests); ffmpeg testy se bez ffmpeg přeskočí
```

## Hardware (USB porty)

- Aplikace: menu **Nástroje › Prověřit USB porty…** (⌘U) nebo tlačítko v sekci *Hardware* — kde jsou karta a disky
  připojené, rychlost linky, doporučení zapojení, volitelné měření (z karty se jen čte, na cíl se zapíše a smaže 1 GB),
  vlastní názvy portů. Při startu se nic nespouští.
- Během kopie karta úlohy ukazuje řetěz připojení s rychlostí, % linky a úzkým hrdlem.
- CLI: `offload ports [--measure] [--card /Volumes/A001] [--dest /Volumes/RAID]`; `offload copy` ukáže připojení
  a ve stavovém řádku „brzdí: D1“.
- Omezení: zařízení 10 Gb/s běžící na 5 Gb/s se nepozná (macOS neprozradí schopnost bez BOS deskriptoru);
  fyzickou polohu portu macOS nezná — porty si pojmenujte.

## LUTy, proxy a složka pro proxy

- Proxy i náhledy v reportu dostanou LUT podle kamery každého klipu: 11 vestavěných LUTů Log → Rec.709
  (`Offload.app/Contents/Resources/luts`, sdílené s Windows ve `shared/luts`). Aplikace: *Proxy › LUT* — Automaticky podle
  kamery (+ „Když kameru nepoznám:“), vestavěný LUT, Bez LUT, Vlastní… (`.cube`). Použitý LUT a důvod je v reportu u klipu.
- CLI: `offload luts` (seznam id), `offload copy … --proxy --lut auto|none|<id>|<cesta.cube> [--lut-fallback <id>|none]`;
  bez `--lut` platí `auto`.
- Složka pro proxy navíc: *Proxy › Kopírovat proxy i do složky* nebo `--proxy-folder <složka>` — každá proxy i do
  `<složka>/<datum>/<karta>/` (při shodě jména `_2`, `_3`…, nic se nepřepíše); nedostupná složka zablokuje start.
- Proxy jsou BT.709 v legálním rozsahu a tak i označené, náhledy JPEG BT.601 (JFIF). VideoToolbox dekóduje jen H.264/HEVC;
  render proxy bez pokroku 60 s (nový snímek nebo data z dekodéru RAW; do prvního pokroku 180 s) se ukončí — po hardwarovém
  dekódování se jednou zkusí znovu softwarově, jinak proxy selže s poznámkou „ffmpeg se zasekl (60 s bez nového snímku);
  proxy nevznikla“ (po neúspěšném opakování „… i bez hardwarového dekódování; proxy nevznikla“).
- Python k sestavení netřeba: `.cube` i ikona jsou v repu. Jen kdo mění `shared/luts/generate.py` nebo
  `shared/icon/make_icon.py`, potřebuje Python (návod v `shared/luts/README.md` a `shared/icon/README.md`).
~~~

a do úvodu odkaz na návrh a plán: `Návrh dorovnání s Windows: docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`.

- [ ] **Step 4: `docs/ROZDILY.md`**
  - „Stav k …“ datum; tabulka Přehled: Mac verze `0.2.0`.
  - Funkce a stav: zkontrolovat, že úkoly 7, 16, 17 a 24 už nastavily „Náhledy v reportu“, „Test USB portů…“,
    „Propustnost článků HW…“, „Vestavěné LUTy…“ a „Složka pro proxy navíc“ na Mac ✅ (jinak doplnit).
  - Technologie: nové řádky „USB topologie | IOKit (IORegistry) | cfgmgr32 + IOCTL hubů“, „Kde sedí svazek | DiskArbitration + IORegistry | IOCTL_STORAGE_QUERY_PROPERTY“, „Identita karty | UUID svazku | GUID svazku + sériové číslo FS“, „Nastavení“ Mac `UserDefaults` (stejné schéma vč. `portNames`, `proxy.lutMode/lutBuiltin/lutFallback/extraFolder`); ověřit řádky z úkolů 19 („Dekódování pro proxy“) a 25 („Ikona aplikace“, „Vestavěné LUTy v balíčku“).
  - Záměrné rozdíly: ověřit, že odrážky o HW diagnostice (úkol 17), „Barvy proxy“ (úkol 18) a „Složka pro proxy navíc“
    (úkol 24) zmizely a „Vestavěné LUTy“ platí pro obě verze (úkol 24); „USB rychlosti a porty“ přepsat na obě platformy (Mac: schopnost 10 Gb/s z bcdUSB nepozná; bez párovacího klíče je USB 2 polovina portu „neznámý typ“); přidat „Thunderbolt: Mac hlásí externí PCIe/NVMe disky jako ‚Thunderbolt‘; v Kartách se zobrazí, pokud macOS hlásí svazek jako externí (ověřit `volumeIsInternal` u Thunderbolt disku: `diskutil info /Volumes/X` → Device Location); Windows je bere jako NVMe a nenabízí (Složka…)“; „Enkodér proxy v reportu“ beze změny (Mac `proxyEncoder` nepíše); „Verze v reportu“: Mac `0.2.0`.
- [ ] **Step 5: `docs/FORMATY.md`** — tabulka JSON, řádek `version`: `` `0.2.0` (Mac), `0.1.0-win` (Windows) ``; ověřit, že
  `clips[].lut` a `proxy.extraFolder` už nejsou „jen Windows“ (úkoly 20 a 22) a že v řádku `proxy` nezůstalo „jen Windows,
  volitelně `extraFolder`“ (→ „volitelně `extraFolder`“).
- [ ] **Step 6: `docs/VYVOJ-MAC.md`** — oddíl „Naplánováno“: smazat odrážku „Dorovnání s Windows verzí — podklady pro Mac“
  (plán je hotový); ponechat „Mimo plán“ bez položek, které mezitím někdo vyřešil — oddíl obsahuje jen to, co opravdu zbývá. Do průběhu: `- Úkol 26 — verze 0.2.0, README (testy, Hardware, LUTy a složka pro proxy), ROZDILY a FORMATY podle stavu po dorovnání`. Nadpis průběhu přejmenovat na `## <datum> — verze 0.2.0: dorovnání s Windows (plán mac-parity)`.
- [ ] **Step 7: Závěrečné ověření**

Run: `swift build -c release && swift test && scripts/build-app.sh`
Expected: vše zelené; `build/Offload.app/Contents/Info.plist` má `CFBundleShortVersionString` `0.2.0` a `CFBundleIconFile`
`Offload`; `build/Offload.app/Contents/Resources/bin/offload --version` → `0.2.0`; `… /bin/offload luts | wc -l` → `11`.

Křížové ověření formátu (s Windows PC nebo kopií výstupu z něj): `offload verify <karta zkopírovaná Windows verzí>` → `OK`; Windows `bin\offload.exe verify <karta zkopírovaná Macem>` → `OK`; `ascmhl verify <karta>` (pip `ascmhl`) → bez chyb.
Report z Macu s LUTy otevřít ve Windows verzi není potřeba (HTML je samostatný); `offload_report.json` z Macu i Windows
mají u klipů stejný tvar `lut` (`{source, id?, name, reason}`).

- [ ] **Step 8: Commit**

Run: `git add -A mac docs && git commit -m "Mac 0.2.0: docs, ROZDILY and FORMATY after the Windows parity work"`

---

## Doplněk 2 (úkoly 27–31): Blackmagic RAW po Windows plánu 3

Spec oddíl „Doplněk 2“ (D.13–D.20). Windows dostal dekodér BRAW v C# (`windows/src/Offload.BrawDecode/`, commity
`2bd6a4b..4138680`, plán `windows/docs/superpowers/plans/2026-10-05-offload-windows-3-braw.md`) a s ním chování, které
Mac převezme: gama/gamut/generace v `--info` a LUT podle gamy, BRAW jen s knihovnou, časový limit `--info`, české
poznámky z kódů 3/4/5, náhledy BRAW pod hlídačem, pumpa zavře výstup dekodéru. Zaokrouhlený start náhledu BRAW už je
v úkolu 6 (`BRAWBackend.startFrame`), tady se nic nepřidává.

**Pořadí:** úkoly 27–31 se dělají **po úkolu 25 a před úkolem 26** (26 uzavírá dokumentaci a verzi 0.2.0 — má vidět
i BRAW). Platí Global Constraints nahoře; všechny příkazy ze složky `mac/`.

**Které testy co potřebují** (jinak se přeskočí přes `XCTSkip`):

| Potřebuje | Testy |
|---|---|
| nic (jen `swift test`) | `BrawLibraryTests`, `BrawBackendTests`, `ToolProcessTests`, `BrawFeederTests`, nové testy v `LutResolverTests` — falešný `braw-decode` a „ffmpeg“ jsou shell skripty |
| sestavený `Tools/bin/braw-decode` | `BrawDecodeTests.testBadCommandLineIsExit2` |
| + knihovna Blackmagic RAW (Player, SDK nebo Resolve) | `BrawDecodeTests.testMissingOrInvalidClipIsExit4AndNamesTheLibrary`, `testStderrKeepsADiacriticPath` |
| + vzorek `.braw` (`OFFLOAD_BRAW_SAMPLE=<plná cesta>`, README-START „Vzorek BRAW“) | `BrawDecodeTests.testInfo…`, `testFrames…`, `testReaderClosesThePipe…`; s ffmpeg i `BrawSampleTests` |

Pustit se vzorkem: `OFFLOAD_BRAW_SAMPLE=/cesta/A001_….braw swift test --filter Braw` — očekává se **0 přeskočených**.

---

### Task 27: braw-decode — gama, gamut a generace v `--info`, pipeline před `OpenClip`, knihovna v hláškách, zavřená roura

Doplněk 2 ke spec — oddíl D.14. Port chování Windows `braw-decode.exe` (`Offload.BrawDecode/Decoder.cs`, `InfoJson.cs`,
`Options.cs`, `BrawApi.cs` `FromCreated`; commity `d75955c`, `5a4b7ef`, `3f7a96f`, `d3d3bc2`, `5b375ce`) do C++ nástroje
a `Offload.Core/BrawLibrary.cs` do Swiftu.

**Files:**
- Modify (celý soubor): `mac/Tools/braw-decode/braw-decode.cpp`
- Create: `mac/Sources/OffloadKit/BrawLibrary.swift`
- Create: `mac/Tests/OffloadKitTests/BrawTestSupport.swift`, `mac/Tests/OffloadKitTests/BrawLibraryTests.swift`, `mac/Tests/OffloadKitTests/BrawDecodeTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `TempDir`, `tempDir()` (úkol 1). Z `BlackmagicRawAPI.h` (beze změny): `IBlackmagicRawClip::CloneClipProcessingAttributes`,
  `IBlackmagicRawClipProcessingAttributes::GetClipAttribute`, `blackmagicRawClipProcessingAttributeGamma` (`'gama'`, string),
  `…Gamut` (`'gamt'`, string), `…ColorScienceGen` (`'csgn'`, u16), `struct Variant`, `VariantInit`, `VariantClear`, `IID_IBlackmagicRawFactory`.
- Produces:
  - `braw-decode` — argumenty beze změny; `--info` navíc `"gamma"`, `"gamut"` (řetězce) a `"colorScienceGen"` (číslo), **jen když
    je SDK vrátí**; kódy 0 (i zavřená roura — na stderr `braw-decode: stdout closed by the reader, stopping`), 2 (chybný příkaz,
    na stderr `usage: …`), 3 (`… not found …`, `<knihovna>: the created object is not an IBlackmagicRawFactory (0x…)`, `CreateCodec failed
    (0x…; library <cesta>)`, `SetCallback failed (0x…; library <cesta>)`), 4 (`cannot open <klip> (0x…; library <cesta>)`), 5 (snímek).
  - `public enum BrawLibrary { static let frameworkName: String; static func searchDirs(decoderDir: String) -> [String]; static func find(in dirs: [String]) -> String?; static func find(decoderDir: String) -> String? }`
  - testy: `enum BrawTest { static let decoder: String?; static func requireDecoder() throws -> String; static func requireLibrary() throws -> String; static func requireSample() throws -> String; static func run(_ exe: String, _ args: [String]) throws -> (code: Int32, stdout: Data, stderr: String); static func json(_ data: Data) throws -> [String: Any] }`

- [ ] **Step 1: Pomocníci testů** — `mac/Tests/OffloadKitTests/BrawTestSupport.swift`:

```swift
import Foundation
import XCTest
@testable import OffloadKit

/// braw-decode tests: the decoder built into mac/Tools/bin, the Blackmagic RAW library where it looks, and a sample clip
/// (OFFLOAD_BRAW_SAMPLE=<full path to a .braw>, see README-START „Vzorek BRAW“). A missing requirement skips the test.
enum BrawTest {
    static let decoder: String? = {
        let p = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // OffloadKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // mac
            .appendingPathComponent("Tools/bin/braw-decode").path
        return FileManager.default.isExecutableFile(atPath: p) ? p : nil
    }()

    static func requireDecoder() throws -> String {
        guard let d = decoder else { throw XCTSkip("mac/Tools/bin/braw-decode missing — run Tools/braw-decode/build.sh") }
        return d
    }

    static func requireLibrary() throws -> String {
        let exe = try requireDecoder()
        guard let lib = BrawLibrary.find(decoderDir: (exe as NSString).deletingLastPathComponent) else {
            throw XCTSkip("BlackmagicRawAPI.framework not found (Blackmagic RAW Player, SDK or DaVinci Resolve)")
        }
        return lib
    }

    static func requireSample() throws -> String {
        _ = try requireLibrary()
        guard let p = ProcessInfo.processInfo.environment["OFFLOAD_BRAW_SAMPLE"], FileManager.default.fileExists(atPath: p) else {
            throw XCTSkip("OFFLOAD_BRAW_SAMPLE not set or the file does not exist")
        }
        return p
    }

    private final class Box {
        var data = Data()
    }

    /// Runs `exe` to the end: exit code, stdout and stderr (read in parallel, so neither pipe fills up).
    static func run(_ exe: String, _ args: [String]) throws -> (code: Int32, stdout: Data, stderr: String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: exe)
        p.arguments = args
        let out = Pipe(), err = Pipe()
        p.standardOutput = out
        p.standardError = err
        try p.run()
        let errBox = Box()
        let group = DispatchGroup()
        group.enter()
        DispatchQueue.global().async {
            errBox.data = err.fileHandleForReading.readDataToEndOfFile()
            group.leave()
        }
        let outData = out.fileHandleForReading.readDataToEndOfFile()
        group.wait()
        p.waitUntilExit()
        return (p.terminationStatus, outData, String(decoding: errBox.data, as: UTF8.self))
    }

    static func json(_ data: Data) throws -> [String: Any] {
        guard let o = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw ToolError(tool: "braw-decode", status: 0, message: "not a JSON object: \(String(decoding: data, as: UTF8.self))")
        }
        return o
    }
}
```

- [ ] **Step 2: Padající testy**

`mac/Tests/OffloadKitTests/BrawLibraryTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class BrawLibraryTests: XCTestCase {
    func testSearchOrderDecoderFolderFirstThenSdkPlayerResolve() {
        XCTAssertEqual(BrawLibrary.searchDirs(decoderDir: "/Applications/Offload.app/Contents/Resources/bin"), [
            "/Applications/Offload.app/Contents/Resources/bin",
            "/Applications/Blackmagic RAW/Blackmagic RAW SDK/Mac/Libraries",
            "/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks",
            "/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Libraries",
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
```

`mac/Tests/OffloadKitTests/BrawDecodeTests.swift`:

```swift
import XCTest
@testable import OffloadKit

/// The real braw-decode (mac/Tools/bin). The command line needs nothing else, exit codes the Blackmagic RAW library,
/// the rest a sample clip (OFFLOAD_BRAW_SAMPLE).
final class BrawDecodeTests: XCTestCase {
    // Strict like Windows Options.Parse: checked before the library is looked for, so it runs on any Mac.
    func testBadCommandLineIsExit2() throws {
        let exe = try BrawTest.requireDecoder()
        let bad: [[String]] = [[], ["--info"], ["--scale", "huge", "a.braw"], ["--scale"], ["--start", "abc", "a.braw"],
                               ["--start", "-1", "a.braw"], ["--count", "1x", "a.braw"], ["--count", "", "a.braw"],
                               ["--bogus", "a.braw"]]
        for args in bad {
            let r = try BrawTest.run(exe, args)
            XCTAssertEqual(r.code, 2, "\(args)")
            XCTAssertTrue(r.stderr.hasPrefix("usage: braw-decode"), "\(args): \(r.stderr)")
            XCTAssertTrue(r.stdout.isEmpty, "\(args)")
        }
    }

    // Windows final fix I1: exit 4 names the library in use (an old one cannot open clips from newer cameras).
    func testMissingOrInvalidClipIsExit4AndNamesTheLibrary() throws {
        let exe = try BrawTest.requireDecoder()
        _ = try BrawTest.requireLibrary()
        let dir = tempDir()
        let invalid = dir.path + "/not-a-clip.braw"
        try "x".write(toFile: invalid, atomically: true, encoding: .utf8)
        for clip in [dir.path + "/missing.braw", invalid] {
            let r = try BrawTest.run(exe, ["--info", clip])
            XCTAssertEqual(r.code, 4, r.stderr)
            XCTAssertTrue(r.stderr.contains("braw-decode: cannot open \(clip) (0x"), r.stderr)
            XCTAssertTrue(r.stderr.contains("; library /"), r.stderr)
            XCTAssertTrue(r.stderr.contains("/BlackmagicRawAPI.framework)"), r.stderr)
        }
    }

    // Spec D.14 (Windows task 6, fix 2): stderr is UTF-8 — a path with diacritics comes back intact in the note.
    func testStderrKeepsADiacriticPath() throws {
        let exe = try BrawTest.requireDecoder()
        _ = try BrawTest.requireLibrary()
        let clip = tempDir().path + "/Natáčení den 1/Klip č. 1.braw"
        let r = try BrawTest.run(exe, ["--info", clip])
        XCTAssertEqual(r.code, 4, r.stderr)
        XCTAssertTrue(r.stderr.contains(clip), r.stderr)
    }

    // MARK: with a sample clip

    private func info(_ exe: String, _ sample: String, _ scale: String = "eighth") throws -> [String: Any] {
        let r = try BrawTest.run(exe, ["--info", "--scale", scale, sample])
        XCTAssertEqual(r.code, 0, r.stderr)
        return try BrawTest.json(r.stdout)
    }

    private func frameBytes(_ i: [String: Any]) -> Int { (i["width"] as? Int ?? 0) * (i["height"] as? Int ?? 0) * 6 }

    // Spec D.14: the SDK's Variant for 'gama'/'gamt'/'csgn' — only a sample proves the types.
    func testInfoDescribesTheClipWithGammaGamutGen() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let r = try BrawTest.run(exe, ["--info", "--scale", "eighth", sample])
        XCTAssertEqual(r.code, 0, r.stderr)
        // A variant of an unexpected type is a warning here and the key is missing — read stderr first.
        XCTAssertFalse(r.stderr.contains("clip attribute"), r.stderr)
        let i = try BrawTest.json(r.stdout)
        let sw = i["sourceWidth"] as? Int ?? 0, sh = i["sourceHeight"] as? Int ?? 0, w = i["width"] as? Int ?? 0
        XCTAssertTrue(sw >= 1920 && sh >= 1080, "\(sw)x\(sh)")
        XCTAssertTrue((sw / 9...sw / 4).contains(w), "processed width \(w) of \(sw) at 1/8")
        XCTAssertGreaterThan(i["fps"] as? Double ?? 0, 0)
        XCTAssertGreaterThan(i["frames"] as? Int ?? 0, 0)
        XCTAssertNotNil((i["timecode"] as? String)?.range(of: #"^\d\d:\d\d:\d\d[:;]\d\d$"#, options: .regularExpression))
        XCTAssertFalse((i["camera"] as? String ?? "").isEmpty)
        let gamma = i["gamma"] as? String ?? ""
        XCTAssertFalse(gamma.isEmpty, "gamma missing")
        XCTAssertFalse((i["gamut"] as? String ?? "").isEmpty, "gamut missing")
        XCTAssertTrue((4...9).contains(i["colorScienceGen"] as? Int ?? 0), "\(i["colorScienceGen"] ?? "nil")")
        // The LUT rule (task 28) compares the gamma with exactly this SDK string.
        if gamma.localizedCaseInsensitiveContains("film") { XCTAssertEqual(gamma, "Blackmagic Design Film") }
        let full = try info(exe, sample, "full")
        XCTAssertEqual(full["width"] as? Int, sw)
        XCTAssertEqual(full["height"] as? Int, sh)
    }

    func testFramesExactRGB48BytesInOrder() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let frame = frameBytes(try info(exe, sample))
        let two = try BrawTest.run(exe, ["--scale", "eighth", "--start", "0", "--count", "2", sample])
        XCTAssertEqual(two.code, 0, two.stderr)
        XCTAssertEqual(two.stdout.count, 2 * frame)
        let second = try BrawTest.run(exe, ["--scale", "eighth", "--start", "1", "--count", "1", sample])
        XCTAssertEqual(two.stdout.subdata(in: frame..<2 * frame), second.stdout)   // the same frame decodes to the same bytes
        XCTAssertTrue(two.stdout.contains { $0 != 0 })                              // not a black buffer
    }

    /// More than one batch of 8: batches are written in frame order.
    func testFramesAcrossBatchesInOrder() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let i = try info(exe, sample)
        try XCTSkipUnless((i["frames"] as? Int ?? 0) >= 10, "the sample needs at least 10 frames")
        let frame = frameBytes(i)
        let ten = try BrawTest.run(exe, ["--scale", "eighth", "--count", "10", sample])
        XCTAssertEqual(ten.code, 0, ten.stderr)
        XCTAssertEqual(ten.stdout.count, 10 * frame)
        let ninth = try BrawTest.run(exe, ["--scale", "eighth", "--start", "9", "--count", "1", sample])
        XCTAssertEqual(ten.stdout.subdata(in: 9 * frame..<10 * frame), ninth.stdout)
    }

    func testFramesClampedAtTheEnd() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let i = try info(exe, sample)
        let frames = i["frames"] as? Int ?? 0
        let last = try BrawTest.run(exe, ["--scale", "eighth", "--start", String(frames - 1), "--count", "5", sample])
        XCTAssertEqual(last.code, 0, last.stderr)
        XCTAssertEqual(last.stdout.count, frameBytes(i))
        let past = try BrawTest.run(exe, ["--scale", "eighth", "--start", String(frames), sample])
        XCTAssertEqual(past.code, 0, past.stderr)
        XCTAssertTrue(past.stdout.isEmpty)
    }

    /// Windows ruling M1: when the reader closes the pipe (ffmpeg has what it wanted) braw-decode stops promptly and
    /// exits 0 — not killed by SIGPIPE, and not decoding the rest of the clip for nobody.
    func testReaderClosesThePipeStopsWithExit0() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let p = Process()
        p.executableURL = URL(fileURLWithPath: exe)
        p.arguments = ["--scale", "eighth", sample]   // the whole clip
        let out = Pipe(), err = Pipe()
        p.standardOutput = out
        p.standardError = err
        try p.run()
        defer { if p.isRunning { p.terminate() } }
        XCTAssertEqual(try out.fileHandleForReading.read(upToCount: 1024)?.count, 1024)   // decoding has started
        try out.fileHandleForReading.close()                                                // "ffmpeg exited"
        let deadline = Date().addingTimeInterval(60)
        while p.isRunning && Date() < deadline { usleep(50_000) }
        XCTAssertFalse(p.isRunning, "braw-decode kept running after its reader went away")
        XCTAssertEqual(p.terminationReason, .exit, "ended by a signal — SIGPIPE not ignored?")
        XCTAssertEqual(p.terminationStatus, 0)
        let stderr = String(decoding: err.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
        XCTAssertTrue(stderr.contains("braw-decode: stdout closed by the reader, stopping"), stderr)
    }
}
```

- [ ] **Step 3: Ověřit selhání** — Run: `swift test --filter "BrawLibraryTests|BrawDecodeTests"` → nesestaví se (`cannot find 'BrawLibrary'`).

- [ ] **Step 4: `mac/Sources/OffloadKit/BrawLibrary.swift`**:

```swift
import Foundation

/// Where the Blackmagic RAW library (BlackmagicRawAPI.framework) can be — the same folders, in the same order, as
/// braw-decode looks in (kLibrarySearchPaths in Tools/braw-decode/braw-decode.cpp). Offload never ships it: it comes with
/// the Blackmagic RAW SDK, the free Blackmagic RAW Player or DaVinci Resolve. Port of Windows `BrawLibrary`.
public enum BrawLibrary {
    public static let frameworkName = "BlackmagicRawAPI.framework"

    /// Next to braw-decode first, then the SDK, the Player and DaVinci Resolve.
    public static func searchDirs(decoderDir: String) -> [String] {
        [
            decoderDir,
            "/Applications/Blackmagic RAW/Blackmagic RAW SDK/Mac/Libraries",
            "/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks",
            "/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Libraries",
        ]
    }

    /// The first `<dir>/BlackmagicRawAPI.framework` that is a folder (a bundle), or nil.
    public static func find(in dirs: [String]) -> String? {
        for dir in dirs {
            let path = dir + "/" + frameworkName
            var isDir: ObjCBool = false
            if FileManager.default.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue { return path }
        }
        return nil
    }

    public static func find(decoderDir: String) -> String? { find(in: searchDirs(decoderDir: decoderDir)) }
}
```

- [ ] **Step 5: `mac/Tools/braw-decode/braw-decode.cpp`** — celý nový obsah:

```cpp
// braw-decode — minimal Blackmagic RAW decoder for the offload proxy pipeline (same CLI as the Windows braw-decode.exe).
//
//   braw-decode --info [--scale S] clip.braw
//       one JSON line: width, height (of the processed image at scale S), sourceWidth, sourceHeight, fps, frames,
//       timecode, camera, and when the SDK reports them gamma, gamut, colorScienceGen
//   braw-decode [--scale S] [--start N] [--count N] clip.braw
//       writes frames to stdout as raw interleaved RGB 16-bit little endian (ffmpeg: -f rawvideo -pix_fmt rgb48le)
//
//   S = full | half | quarter | eighth
//
// Exit codes (as on Windows): 0 OK (also when the reader closed the pipe early), 2 usage, 3 library/codec missing,
// 4 clip cannot be opened, 5 a frame failed. Every exit-3/4 line names the library in use: an old one cannot open
// clips from newer cameras, and Offload's note at the clip says so. stderr is UTF-8 (paths come through as given).
// Frames are decoded in the clip's own gamma/gamut (as recorded); the LUT is applied later by ffmpeg.
// Build: Tools/braw-decode/build.sh (links against BlackmagicRawAPI.framework at runtime via the dispatch loader).

#include "BlackmagicRawAPI.h"

#include <CoreFoundation/CoreFoundation.h>
#include <mach-o/dyld.h>
#include <algorithm>
#include <atomic>
#include <cerrno>
#include <climits>
#include <csignal>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <libgen.h>
#include <string>
#include <unistd.h>
#include <vector>

static const char* kFramework = "BlackmagicRawAPI.framework";

// Keep in sync with OffloadKit BrawLibrary.searchDirs (BrawLibraryTests checks the folders are here).
static const char* kLibrarySearchPaths[] = {
    // next to this executable (bundled), then the SDK, then the free Blackmagic RAW Player, then DaVinci Resolve
    "@exe",
    "/Applications/Blackmagic RAW/Blackmagic RAW SDK/Mac/Libraries",
    "/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks",
    "/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Libraries",
};

static const char* kUsage = "usage: braw-decode [--info] [--scale full|half|quarter|eighth] [--start N] [--count N] clip.braw";

// The framework that was loaded (named in every exit-3/4 line).
static std::string gLibrary = kFramework;

static std::string toStd(CFStringRef s) {
    if (!s) return "";
    char buf[1024];
    if (CFStringGetCString(s, buf, sizeof buf, kCFStringEncodingUTF8)) return buf;
    return "";
}

static std::string jsonEscape(const std::string& s) {
    std::string o;
    for (char c : s) {
        if (c == '"' || c == '\\') o += '\\';
        if ((unsigned char)c >= 0x20) o += c;
    }
    return o;
}

struct FrameSlot {
    std::vector<uint8_t> data;
    uint32_t width = 0, height = 0;
    bool ok = false;
};

class Callback : public IBlackmagicRawCallback {
public:
    BlackmagicRawResolutionScale scale = blackmagicRawResolutionScaleFull;
    std::vector<FrameSlot>* slots = nullptr;
    uint64_t base = 0;
    std::atomic<int> errors{0};

    void ReadComplete(IBlackmagicRawJob* readJob, HRESULT result, IBlackmagicRawFrame* frame) override {
        IBlackmagicRawJob* decodeJob = nullptr;
        void* user = nullptr;
        readJob->GetUserData(&user);
        if (result == S_OK) result = frame->SetResourceFormat(blackmagicRawResourceFormatRGBU16);
        if (result == S_OK) result = frame->SetResolutionScale(scale);
        if (result == S_OK) result = frame->CreateJobDecodeAndProcessFrame(nullptr, nullptr, &decodeJob);
        if (result == S_OK) {
            decodeJob->SetUserData(user);
            result = decodeJob->Submit();
        }
        if (result != S_OK) {
            fprintf(stderr, "braw-decode: read/decode setup failed (0x%08X)\n", (unsigned)result);
            errors++;
            if (decodeJob) decodeJob->Release();
        }
        readJob->Release();
    }

    void ProcessComplete(IBlackmagicRawJob* job, HRESULT result, IBlackmagicRawProcessedImage* img) override {
        void* user = nullptr;
        job->GetUserData(&user);
        uint64_t index = (uint64_t)(uintptr_t)user;
        if (result == S_OK && slots && index >= base && index - base < slots->size()) {
            FrameSlot& slot = (*slots)[index - base];
            uint32_t size = 0;
            void* res = nullptr;
            img->GetWidth(&slot.width);
            img->GetHeight(&slot.height);
            img->GetResourceSizeBytes(&size);
            img->GetResource(&res);
            // The SDK pads the buffer; stdout must carry exactly width*height RGB48 pixels.
            size_t frameBytes = (size_t)slot.width * slot.height * 6;
            if (size > frameBytes) size = (uint32_t)frameBytes;
            slot.data.assign((uint8_t*)res, (uint8_t*)res + size);
            slot.ok = true;
        } else {
            fprintf(stderr, "braw-decode: process failed (0x%08X)\n", (unsigned)result);
            errors++;
        }
        job->Release();
    }

    void DecodeComplete(IBlackmagicRawJob*, HRESULT) override {}
    void TrimProgress(IBlackmagicRawJob*, float) override {}
    void TrimComplete(IBlackmagicRawJob*, HRESULT) override {}
    void SidecarMetadataParseWarning(IBlackmagicRawClip*, CFStringRef, uint32_t, CFStringRef) override {}
    void SidecarMetadataParseError(IBlackmagicRawClip*, CFStringRef, uint32_t, CFStringRef) override {}
    void PreparePipelineComplete(void*, HRESULT) override {}

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, LPVOID*) override { return E_NOTIMPL; }
    ULONG STDMETHODCALLTYPE AddRef() override { return 0; }
    ULONG STDMETHODCALLTYPE Release() override { return 0; }
};

// The folder of this executable with symlinks resolved — where "@exe" looks (as the SDK's dispatch does).
static std::string exeDir() {
    char path[PATH_MAX], resolved[PATH_MAX];
    uint32_t size = sizeof path;
    if (_NSGetExecutablePath(path, &size) != 0 || !realpath(path, resolved)) return ".";
    return dirname(resolved);
}

static IBlackmagicRawFactory* loadFactory() {
    for (const char* p : kLibrarySearchPaths) {
        IBlackmagicRawFactory* f = nullptr;
        std::string dir;
        if (strcmp(p, "@exe") == 0) {
            dir = exeDir();
            f = CreateBlackmagicRawFactoryInstanceFromExeRelativePath(CFSTR("."));
        } else {
            dir = p;
            CFStringRef path = CFStringCreateWithCString(nullptr, p, kCFStringEncodingUTF8);
            f = CreateBlackmagicRawFactoryInstanceFromPath(path);
            CFRelease(path);
        }
        if (f) {
            gLibrary = dir + "/" + kFramework;
            return f;
        }
    }
    return nullptr;
}

// Ask the created object for IBlackmagicRawFactory (QueryInterface is IUnknown's first slot on every object): a
// framework of another SDK generation whose object lacks it is a clear exit 3, never a call into the wrong slot.
// Takes over (and releases) the created reference. Same as Windows BrawRuntime.FromCreated.
static IBlackmagicRawFactory* asFactory(IBlackmagicRawFactory* created) {
    IBlackmagicRawFactory* factory = nullptr;
    HRESULT r = created->QueryInterface(IID_IBlackmagicRawFactory, (LPVOID*)&factory);
    created->Release();
    if (r != S_OK || !factory) {
        if (factory) factory->Release();
        fprintf(stderr, "braw-decode: %s: the created object is not an IBlackmagicRawFactory (0x%08X)\n", gLibrary.c_str(),
                (unsigned)r);
        return nullptr;
    }
    return factory;
}

struct Options {
    bool info = false;
    BlackmagicRawResolutionScale scale = blackmagicRawResolutionScaleFull;
    uint64_t start = 0;
    uint64_t count = UINT64_MAX;   // UINT64_MAX = to the end of the clip
    const char* path = nullptr;
};

static bool parseScale(const char* s, BlackmagicRawResolutionScale& out) {
    if (!strcmp(s, "full")) out = blackmagicRawResolutionScaleFull;
    else if (!strcmp(s, "half")) out = blackmagicRawResolutionScaleHalf;
    else if (!strcmp(s, "quarter")) out = blackmagicRawResolutionScaleQuarter;
    else if (!strcmp(s, "eighth")) out = blackmagicRawResolutionScaleEighth;
    else return false;
    return true;
}

// Digits only (strtoull alone takes "-1", "12abc" and "").
static bool parseNumber(const char* s, uint64_t& out) {
    if (!*s) return false;
    for (const char* c = s; *c; c++)
        if (*c < '0' || *c > '9') return false;
    errno = 0;
    out = strtoull(s, nullptr, 10);
    return errno == 0;
}

// Strict like the Windows decoder: Offload never passes anything else, so a typo fails loudly (exit 2).
static bool parseArgs(int argc, char** argv, Options& o) {
    for (int i = 1; i < argc; i++) {
        const char* a = argv[i];
        const char* value = i + 1 < argc ? argv[i + 1] : nullptr;
        if (!strcmp(a, "--info")) {
            o.info = true;
        } else if (!strcmp(a, "--scale")) {
            if (!value || !parseScale(value, o.scale)) return false;
            i++;
        } else if (!strcmp(a, "--start")) {
            if (!value || !parseNumber(value, o.start)) return false;
            i++;
        } else if (!strcmp(a, "--count")) {
            if (!value || !parseNumber(value, o.count)) return false;
            i++;
        } else if (!strncmp(a, "--", 2)) {
            return false;
        } else {
            o.path = a;
        }
    }
    return o.path != nullptr;
}

// false when the reader went away (EPIPE — SIGPIPE is ignored, see main).
static bool writeAll(const uint8_t* p, size_t n) {
    while (n > 0) {
        ssize_t w = write(STDOUT_FILENO, p, n);
        if (w < 0) {
            if (errno == EINTR) continue;
            return false;
        }
        p += w;
        n -= (size_t)w;
    }
    return true;
}

// ffmpeg has what it wanted (a thumbnail needs one frame) and closed the pipe: a success, not a failure.
[[noreturn]] static void readerGone() {
    fprintf(stderr, "braw-decode: stdout closed by the reader, stopping\n");
    _exit(0);
}

static std::string fourcc(uint32_t v) {
    char s[5] = {(char)(v >> 24), (char)(v >> 16), (char)(v >> 8), (char)v, 0};
    return s;
}

static void attributeWarning(const char* key, uint32_t attribute, const char* what) {
    fprintf(stderr, "braw-decode: %s: clip attribute '%s' %s\n", key, fourcc(attribute).c_str(), what);
}

// Gamma and gamut are strings. An attribute the SDK does not return is left out silently; one of another variant
// type is a warning on stderr and left out too — never a failure of --info (Windows ruling T6-attr).
static bool attributeText(IBlackmagicRawClipProcessingAttributes* attrs, BlackmagicRawClipProcessingAttribute attribute,
                          const char* key, std::string& out) {
    Variant v;
    VariantInit(&v);
    bool ok = false;
    if (attrs->GetClipAttribute(attribute, &v) == S_OK) {
        if (v.vt == blackmagicRawVariantTypeString) {
            out = toStd(v.bstrVal);
            ok = true;
        } else if (v.vt != blackmagicRawVariantTypeEmpty) {
            attributeWarning(key, attribute, "is a number, expected a string");
        }
    }
    VariantClear(&v);
    return ok;
}

// The colour science generation is a u16; any integer type is taken.
static bool attributeNumber(IBlackmagicRawClipProcessingAttributes* attrs, BlackmagicRawClipProcessingAttribute attribute,
                            const char* key, int& out) {
    Variant v;
    VariantInit(&v);
    bool ok = false;
    if (attrs->GetClipAttribute(attribute, &v) == S_OK) {
        ok = true;
        switch (v.vt) {
        case blackmagicRawVariantTypeU8: out = (int)(v.uiVal & 0xFF); break;
        case blackmagicRawVariantTypeS16: out = v.iVal; break;
        case blackmagicRawVariantTypeU16: out = v.uiVal; break;
        case blackmagicRawVariantTypeS32: out = v.intVal; break;
        case blackmagicRawVariantTypeU32:
            if (v.uintVal > (uint32_t)INT_MAX) {
                attributeWarning(key, attribute, "is out of range");
                ok = false;
            } else {
                out = (int)v.uintVal;
            }
            break;
        case blackmagicRawVariantTypeEmpty: ok = false; break;
        case blackmagicRawVariantTypeString:
            attributeWarning(key, attribute, "is a string, expected a number");
            ok = false;
            break;
        default:
            attributeWarning(key, attribute, "has an unexpected variant type");
            ok = false;
            break;
        }
    }
    VariantClear(&v);
    return ok;
}

int main(int argc, char** argv) {
    // A reader that goes away must be a failed write we can see (EPIPE → exit 0 with a note), not a death by SIGPIPE.
    signal(SIGPIPE, SIG_IGN);

    Options o;
    if (!parseArgs(argc, argv, o)) {
        fprintf(stderr, "%s\n", kUsage);
        return 2;
    }

    IBlackmagicRawFactory* created = loadFactory();
    if (!created) {
        fprintf(stderr, "braw-decode: %s not found (install Blackmagic RAW Player, the Blackmagic RAW SDK or DaVinci Resolve)\n",
                kFramework);
        return 3;
    }
    IBlackmagicRawFactory* factory = asFactory(created);
    if (!factory) return 3;
    IBlackmagicRaw* codec = nullptr;
    IBlackmagicRawClip* clip = nullptr;
    HRESULT r = factory->CreateCodec(&codec);
    if (r != S_OK || !codec) {
        fprintf(stderr, "braw-decode: CreateCodec failed (0x%08X; library %s)\n", (unsigned)r, gLibrary.c_str());
        return 3;
    }

    // The configuration is read on the first OpenClip, so the pipeline is set before it (CPU).
    IBlackmagicRawConfiguration* config = nullptr;
    if (codec->QueryInterface(IID_IBlackmagicRawConfiguration, (LPVOID*)&config) == S_OK && config) {
        config->SetPipeline(blackmagicRawPipelineCPU, nullptr, nullptr);
        config->Release();
    }

    CFStringRef cfPath = CFStringCreateWithCString(nullptr, o.path, kCFStringEncodingUTF8);
    r = cfPath ? codec->OpenClip(cfPath, &clip) : E_FAIL;
    if (cfPath) CFRelease(cfPath);
    if (r != S_OK || !clip) {
        fprintf(stderr, "braw-decode: cannot open %s (0x%08X; library %s)\n", o.path, (unsigned)r, gLibrary.c_str());
        return 4;
    }

    uint32_t width = 0, height = 0;
    float fps = 0;
    uint64_t frames = 0;
    clip->GetWidth(&width);
    clip->GetHeight(&height);
    clip->GetFrameRate(&fps);
    clip->GetFrameCount(&frames);

    Callback cb;
    cb.scale = o.scale;
    r = codec->SetCallback(&cb);
    if (r != S_OK) {
        fprintf(stderr, "braw-decode: SetCallback failed (0x%08X; library %s)\n", (unsigned)r, gLibrary.c_str());
        return 3;
    }

    if (o.info) {
        // Decode frame 0 at the requested scale: the processed size is the truth, not width/scale.
        std::vector<FrameSlot> slots(1);
        cb.slots = &slots;
        cb.base = 0;
        IBlackmagicRawJob* job = nullptr;
        if (frames > 0 && clip->CreateJobReadFrame(0, &job) == S_OK) {
            job->SetUserData((void*)(uintptr_t)0);
            if (job->Submit() != S_OK) job->Release();
            codec->FlushJobs();
        }
        // What the clip was shot in — Offload picks the LUT by it (LutResolver.braw).
        std::string gamma, gamut;
        int gen = 0;
        bool hasGamma = false, hasGamut = false, hasGen = false;
        IBlackmagicRawClipProcessingAttributes* attrs = nullptr;
        if (clip->CloneClipProcessingAttributes(&attrs) == S_OK && attrs) {
            hasGamma = attributeText(attrs, blackmagicRawClipProcessingAttributeGamma, "gamma", gamma);
            hasGamut = attributeText(attrs, blackmagicRawClipProcessingAttributeGamut, "gamut", gamut);
            hasGen = attributeNumber(attrs, blackmagicRawClipProcessingAttributeColorScienceGen, "colorScienceGen", gen);
            attrs->Release();
        }
        CFStringRef tc = nullptr, cam = nullptr;
        clip->GetTimecodeForFrame(0, &tc);
        clip->GetCameraType(&cam);
        char head[256];
        snprintf(head, sizeof head, "{\"width\":%u,\"height\":%u,\"sourceWidth\":%u,\"sourceHeight\":%u,\"fps\":%.6f,\"frames\":%llu,",
                 slots[0].ok ? slots[0].width : width, slots[0].ok ? slots[0].height : height, width, height, fps,
                 (unsigned long long)frames);
        std::string json = head;
        json += "\"timecode\":\"" + jsonEscape(toStd(tc)) + "\",\"camera\":\"" + jsonEscape(toStd(cam)) + "\"";
        if (hasGamma) json += ",\"gamma\":\"" + jsonEscape(gamma) + "\"";
        if (hasGamut) json += ",\"gamut\":\"" + jsonEscape(gamut) + "\"";
        if (hasGen) json += ",\"colorScienceGen\":" + std::to_string(gen);
        json += "}\n";
        if (tc) CFRelease(tc);
        if (cam) CFRelease(cam);
        if (!writeAll((const uint8_t*)json.data(), json.size())) readerGone();
    } else {
        // Decode in small batches: jobs run in parallel inside the SDK, output stays in frame order.
        const uint64_t batch = 8;
        uint64_t end = std::min(frames, o.start + std::min(o.count, UINT64_MAX - o.start));
        std::vector<FrameSlot> slots(batch);
        cb.slots = &slots;
        for (uint64_t b = o.start; b < end; b += batch) {
            uint64_t n = std::min(batch, end - b);
            for (auto& s : slots) s.ok = false;
            cb.base = b;
            for (uint64_t i = 0; i < n; i++) {
                IBlackmagicRawJob* job = nullptr;
                if (clip->CreateJobReadFrame(b + i, &job) != S_OK) { cb.errors++; continue; }
                job->SetUserData((void*)(uintptr_t)(b + i));
                if (job->Submit() != S_OK) { job->Release(); cb.errors++; }
            }
            codec->FlushJobs();
            for (uint64_t i = 0; i < n; i++) {
                if (!slots[i].ok) {
                    fprintf(stderr, "braw-decode: frame %llu failed\n", (unsigned long long)(b + i));
                    _exit(5);
                }
                if (!writeAll(slots[i].data.data(), slots[i].data.size())) readerGone();
            }
        }
    }

    clip->Release();
    codec->Release();
    factory->Release();
    _exit(cb.errors > 0 ? 5 : 0);
}
```

Pozn.: chybové kódy HRESULT velkými písmeny (`0x80004005`) jako Windows `Com.Hex`; dřívější `0x%08x` v callbacku sjednocen.
`v.uiVal & 0xFF` pro `U8`: `Variant` nemá 8bitový člen, malý endian (arm64 i x86_64) má nejnižší bajt na začátku unie.

- [ ] **Step 6: Sestavit a testy projdou**

Run: `Tools/braw-decode/build.sh && swift test --filter "BrawLibraryTests|BrawDecodeTests"`
Expected: `built …/Tools/bin/braw-decode` bez varování; testy 0 chyb. Bez knihovny přeskočeno 7 (projde
`testBadCommandLineIsExit2` a `BrawLibraryTests`), s knihovnou bez vzorku 5, se vzorkem 0.

**Ověřit na Macu** (spec D.19, ř. 1–3), výsledek do commitu:
1. QueryInterface továrny: s knihovnou `testMissingOrInvalidClipIsExit4AndNamesTheLibrary` dává 4, ne 3. Skončí-li kódem 3
   s `the created object is not an IBlackmagicRawFactory`, framework svou vlastní IID nevrací — pak `asFactory` z `main`
   vyhodit (vrátit přímé použití `created`) a zapsat to do spec D.14.
2. Kde je framework: `ls -d "/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks/BlackmagicRawAPI.framework"
   "/Applications/Blackmagic RAW/Blackmagic RAW SDK/Mac/Libraries/BlackmagicRawAPI.framework"
   "/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Libraries/BlackmagicRawAPI.framework"` (co je nainstalované) —
   cesta, která neexistuje, ale framework je jinde (`mdfind -name BlackmagicRawAPI.framework`), se opraví v `kLibrarySearchPaths`
   i `BrawLibrary.searchDirs` najednou (hlídá `testSameFoldersAsTheDecoderSource`).
3. Typy `Variant` u `gama`/`gamt`/`csgn`: se vzorkem `Tools/bin/braw-decode --info --scale eighth "$OFFLOAD_BRAW_SAMPLE"` —
   **nejdřív stderr** (varování `clip attribute '…'` = jiný typ, než hlavička uvádí), pak JSON: `gamma` např. „Blackmagic
   Design Film“, `colorScienceGen` 5 u Gen 5.

- [ ] **Step 7: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 27 — braw-decode jako Windows: --info vydává gamma, gamut a colorScienceGen (atributy klipu, nečitelný atribut = varování na stderr), pipeline před OpenClip, hlášky kódů 3/4 jmenují načtenou knihovnu, továrna přes QueryInterface, zavřená roura = kód 0 s hláškou (SIGPIPE ignorován — dřív proces zabil signál), přísné argumenty (kód 2); BrawLibrary se stejnými složkami jako dekodér`

Run: `git add -A mac/Tools/braw-decode mac/Sources mac/Tests docs && git commit -m "Mac braw-decode: gamma/gamut/gen in --info, pipeline before OpenClip, library named in errors, exit 0 on a closed pipe"`

(`Tools/bin/` je v `.gitignore` — sestavený `braw-decode` se necommituje.)

---

### Task 28: Gama BRAW v metadatech klipu a LUT podle gamy

Doplněk 2 ke spec — oddíly D.15 a D.18. Port `windows/src/Offload.Core/Luts.cs` (`LutResolver.Braw`), `Media.cs`
(`ClipInfo.Gamma/Gamut/ColorScienceGen`) a `RawBackends.cs` (`BrawInfo`, `ToClipInfo`); testy `LutResolverTests.Braw_*`,
`BrawCoreTests.Info_GammaGamutGen_ReachClipInfo`.

**Files:**
- Modify: `mac/Sources/OffloadKit/Media.swift` (`ClipInfo`)
- Modify: `mac/Sources/OffloadKit/RawBackends.swift` (`BRAWBackend.Info`, `parseInfo`, `clipInfo`, `info`, `probe`)
- Modify: `mac/Sources/OffloadKit/Luts.swift` (`LutResolver.braw`, `detect`)
- Create: `mac/Tests/OffloadKitTests/BrawBackendTests.swift`
- Modify: `mac/Tests/OffloadKitTests/LutResolverTests.swift` (dva nové testy ve třídě)
- Modify: `docs/VYVOJ-MAC.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `LutResolver.resolve/detect`, `LutUse`, `TestMedia.luts` a soukromé pomocníky `LutResolverTests` (`rawClip`, `auto(fallback:)`, `cat`) (úkol 21); `BRAWBackend` (úkol 22).
- Produces:
  - `ClipInfo.gamma: String?`, `.gamut: String?`, `.colorScienceGen: Int?` (JSON klíče `gamma`, `gamut`, `colorScienceGen`; `nil` se nezapisuje)
  - `struct BRAWBackend.Info: Decodable` (už ne `private`; nové volitelné `gamma`, `gamut`, `colorScienceGen`), `static func BRAWBackend.parseInfo(_ json: String) throws -> Info`, `static func BRAWBackend.clipInfo(_ i: Info) -> ClipInfo`
  - `static func LutResolver.braw(_ info: ClipInfo?) -> (id: String?, reason: String)`

Pravidla (1:1 s Windows `LutResolver.Braw`; `label` = `<gama> Gen <n>`, bez generace jen `<gama>`):

| Gama z `braw-decode --info` | `id` | Důvod u klipu |
|---|---|---|
| chybí nebo prázdná (starší `braw-decode`) | `bmd-film-gen5` | `Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)` |
| jiná než „Blackmagic Design Film“ (bez ohledu na velikost písmen) | `""` (bez LUTu, **ani záložní**) | `Blackmagic RAW: <label> (není log)` |
| „Blackmagic Design Film“, generace < 5 | `""` | `Blackmagic RAW: <label> (bez vestavěného LUTu)` — Film Gen 4 provizorně, ke schválení |
| „Blackmagic Design Film“, generace ≥ 5 nebo neuvedená | `bmd-film-gen5` | `Blackmagic RAW: <label>` |

- [ ] **Step 1: Padající testy**

`mac/Tests/OffloadKitTests/BrawBackendTests.swift`:

```swift
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
```

Do třídy `LutResolverTests` (`mac/Tests/OffloadKitTests/LutResolverTests.swift`, za `testBrawAndR3D`) přidat:

```swift
    // Review focus 10 (Windows LutResolverTests.Braw_LutFromGamma): the LUT follows the gamma the clip was shot in.
    func testBrawLutFromGamma() {
        let cases: [(gamma: String?, gen: Int?, source: String, id: String?, reason: String)] = [
            ("Blackmagic Design Film", 5, "auto", "bmd-film-gen5", "Blackmagic RAW: Blackmagic Design Film Gen 5"),
            ("blackmagic design film", 6, "auto", "bmd-film-gen5", "Blackmagic RAW: blackmagic design film Gen 6"),
            ("Blackmagic Design Film", nil, "auto", "bmd-film-gen5", "Blackmagic RAW: Blackmagic Design Film"),
            ("Blackmagic Design Film", 4, "none", nil, "Blackmagic RAW: Blackmagic Design Film Gen 4 (bez vestavěného LUTu)"),
            ("Blackmagic Design Video", 5, "none", nil, "Blackmagic RAW: Blackmagic Design Video Gen 5 (není log)"),
            ("Blackmagic Design Extended Video", 5, "none", nil, "Blackmagic RAW: Blackmagic Design Extended Video Gen 5 (není log)"),
            (nil, nil, "auto", "bmd-film-gen5", "Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)"),
            ("", nil, "auto", "bmd-film-gen5", "Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)"),
        ]
        for c in cases {
            var info = ClipInfo()
            info.gamma = c.gamma
            info.colorScienceGen = c.gen
            let use = LutResolver.resolve(rawClip(.braw, "A001.braw"), info: info, clipFullPath: "/x/A001.braw",
                                          settings: auto(), catalog: cat)
            XCTAssertEqual(use.source, c.source, c.reason)
            XCTAssertEqual(use.id, c.id, c.reason)
            XCTAssertEqual(use.reason, c.reason)
        }
        XCTAssertEqual(LutResolver.resolve(rawClip(.braw, "A001.braw"), info: nil, clipFullPath: "/x/A001.braw",
                                           settings: auto(), catalog: cat).reason,
                       "Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)")
    }

    // Recognised as display-ready: the fallback LUT is only for cameras that were not recognised.
    func testBrawVideoGammaIgnoresFallback() {
        var info = ClipInfo()
        info.gamma = "Blackmagic Design Video"
        let use = LutResolver.resolve(rawClip(.braw, "A001.braw"), info: info, clipFullPath: "/x/A001.braw",
                                      settings: auto(fallback: "bmd-film-gen5"), catalog: cat)
        XCTAssertEqual(use.source, "none")
        XCTAssertNil(use.file)
    }
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter "BrawBackendTests|LutResolverTests"` → nesestaví se
(`value of type 'ClipInfo' has no member 'gamma'`, `type 'BRAWBackend' has no member 'parseInfo'`).

- [ ] **Step 3: `Media.swift`** — v `ClipInfo` za `colorRange` (úkol 21) přidat:

```swift
    /// Blackmagic RAW: gamma, gamut and colour science generation the clip was recorded in (from braw-decode --info);
    /// the LUT is chosen by them (LutResolver.braw). Not written when unknown.
    public var gamma: String?
    public var gamut: String?
    public var colorScienceGen: Int?
```

- [ ] **Step 4: `RawBackends.swift` (`BRAWBackend`)** — `private struct Info`, `private func info` a `func probe` nahradit:

```swift
    /// One line of `braw-decode --info`. gamma / gamut / colorScienceGen only when the SDK reports them (braw-decode
    /// since task 27, and Windows); an older decoder leaves them out.
    struct Info: Decodable {
        let width: Int, height: Int, sourceWidth: Int, sourceHeight: Int
        let fps: Double, frames: Int, timecode: String, camera: String
        let gamma: String?, gamut: String?, colorScienceGen: Int?
    }

    static func parseInfo(_ json: String) throws -> Info {
        try JSONDecoder().decode(Info.self, from: Data(json.utf8))
    }

    static func clipInfo(_ i: Info) -> ClipInfo {
        var c = ClipInfo()
        c.codec = "Blackmagic RAW"
        c.width = i.sourceWidth
        c.height = i.sourceHeight
        c.frameRate = RawBackends.rational(i.fps)
        c.frames = i.frames
        c.duration = i.fps > 0 ? Double(i.frames) / i.fps : nil
        c.timecode = i.timecode.isEmpty ? nil : i.timecode
        c.camera = i.camera.isEmpty ? nil : i.camera
        c.gamma = i.gamma.flatMap { $0.isEmpty ? nil : $0 }
        c.gamut = i.gamut.flatMap { $0.isEmpty ? nil : $0 }
        c.colorScienceGen = i.colorScienceGen
        return c
    }

    private func info(_ path: String, scale: String, tools: ExternalTools) throws -> Info {
        try BRAWBackend.parseInfo(try ToolProcess.run(tools.brawDecoder!, ["--info", "--scale", scale, path]))
    }

    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        BRAWBackend.clipInfo(try info(path, scale: "eighth", tools: tools))
    }
```

- [ ] **Step 5: `Luts.swift` (`LutResolver`)** — v `detect` nahradit `case .braw:` a jeho `return` za `case .braw: return braw(info)`
a nad `detect` přidat:

```swift
    /// BRAW decodes in the gamma the clip was shot in: only "Blackmagic Design Film" Gen 5 matches the built-in LUT.
    /// Video / Extended Video / Custom / HDR gammas are display-ready already; a decoder without the gamma in --info keeps
    /// the old assumption (Film Gen 5). Film Gen 4 has no built-in LUT (provisional, as Windows). Port of `LutResolver.Braw`.
    static func braw(_ info: ClipInfo?) -> (id: String?, reason: String) {
        guard let gamma = info?.gamma, !gamma.isEmpty else {
            return ("bmd-film-gen5", "Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)")
        }
        let gen = info?.colorScienceGen
        let label = gen.map { "\(gamma) Gen \($0)" } ?? gamma
        if gamma.caseInsensitiveCompare("Blackmagic Design Film") != .orderedSame {
            return ("", "Blackmagic RAW: \(label) (není log)")
        }
        if let gen, gen < 5 { return ("", "Blackmagic RAW: \(label) (bez vestavěného LUTu)") }
        return ("bmd-film-gen5", "Blackmagic RAW: \(label)")
    }
```

- [ ] **Step 6: Testy projdou** — Run: `swift test --filter "BrawBackendTests|LutResolverTests|LutReportTests"` → 0 chyb; pak `swift test`.
(`testBrawAndR3D` z úkolu 21 kontroluje jen `id` — zůstává beze změny.)

- [ ] **Step 7: `docs/FORMATY.md`** — u odrážky `clips[].info.gamma`, `gamut`, `colorScienceGen`: smazat „(volitelné, zatím jen
Windows)“ → „(volitelné)“ a větu „Mac je zatím nezapisuje a při čtení ignoruje.“ nahradit „Zapisují je obě verze, jen když je
dekodér zná.“

- [ ] **Step 8: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 28 — BRAW: gama, gamut a generace barev z braw-decode v metadatech klipu (JSON info.gamma/gamut/colorScienceGen jako Windows); LUT jen pro Blackmagic Design Film Gen 5, Video / Extended Video a Film Gen 4 bez LUTu (ani záložního), bez údaje o gamě předpoklad Film Gen 5`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: BRAW gamma/gamut/gen in the clip info; LUT chosen from the clip's gamma"`

---

### Task 29: BRAW jen s knihovnou, časový limit `--info`, české poznámky z kódů 3/4/5

Doplněk 2 ke spec — oddíl D.16. Port `windows/src/Offload.Core/RawBackends.cs` (`BrawBackend`: `IsAvailable` s knihovnou,
`DefaultInfoTimeout`, `Info`, `Explain`, `LastLine`, `RawDecoderException`), `ToolProcess.Run(…, timeout)`, `ProxyRenderer.Logged`;
testy `BrawCoreTests` (knihovna, I1), `ProxyWatchdogTests.BrawInfo_*`.

**Files:**
- Modify: `mac/Sources/OffloadKit/Tools.swift` (nová `StderrTail`, celá `ToolProcess`)
- Modify: `mac/Sources/OffloadKit/RawBackends.swift` (nová `RawDecoderError`, celý `BRAWBackend`)
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (`FrameSource.explainFeederError`; `ProxyRenderer.backends` jako `var`, `logged`, `probe`, `frameSource`)
- Modify: `mac/Tests/OffloadKitTests/BrawTestSupport.swift` (falešný dekodér), `mac/Tests/OffloadKitTests/BrawBackendTests.swift` (nové testy)
- Create: `mac/Tests/OffloadKitTests/ToolProcessTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `BrawLibrary` (úkol 27), `BRAWBackend.Info/parseInfo/clipInfo` (úkol 28), `BRAWBackend.startFrame` (úkol 6), `FrameSource`, `ProxyRenderer.log` (úkoly 19, 22).
- Produces:
  - `final class StderrTail { init(_ process: Process); var text: String { get }; func drain() -> String }`
  - `ToolProcess.run(_:_:cwd:timeout: TimeInterval? = nil)`, `ToolProcess.drainStderr() -> String`
  - `struct RawDecoderError: Error, CustomStringConvertible { let note: String; let decoderError: ToolError }` — `description` = `note`
  - `BRAWBackend`: `missingHint` (text Windows), `static let defaultInfoTimeout: TimeInterval = 60`, `var findLibrary: (String) -> String?`, `var infoTimeout: TimeInterval` (memberwise `BRAWBackend(findLibrary:infoTimeout:)`), `static func decoderDir(_ exe: String) -> String`, `func explain(_ e: ToolError, tools: ExternalTools) -> Error`, `static func lastLine(_ stderr: String) -> String`
  - `FrameSource.explainFeederError: ((ToolError) -> Error)?` (použije úkol 30)
  - `ProxyRenderer.backends` je `var` (testy nahradí backend formátu)
  - testy: `BrawTest.fakeDecoder(_:info:frames:) -> (path: String, calls: String)`, `printInfo(_:)`, `fail(_:_:)`, `hang`, `script(_:_:_:)`, `script(in:_:_:)`, `calls(_:)`, `brawClip`, `clipInfo(frames:)`, `final class LogLines`

Poznámky u klipu (přesně jako Windows; `<knihovna>` = cesta k nalezenému `BlackmagicRawAPI.framework`, jinak jen jeho jméno):

| Kód `braw-decode` | Poznámka |
|---|---|
| 3 | `Blackmagic RAW: knihovnu <knihovna> nelze použít — nainstalujte aktuální Blackmagic RAW Player` |
| 4 | `Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna <knihovna> starší než kamera (nainstalujte aktuální Blackmagic RAW Player)` |
| 5 | `Blackmagic RAW: snímek klipu se nepodařilo dekódovat (<poslední řádek stderr bez „braw-decode: “>)` |
| jiný (2, časový limit −1) | chyba beze změny, např. `braw-decode failed (exit -1): neodpověděl do 60 s — ukončen` |

Původní hláška (`braw-decode failed (exit 4): braw-decode: cannot open … (0x…; library …)`) jde do logu úlohy.

- [ ] **Step 1: Falešný dekodér** — na konec `mac/Tests/OffloadKitTests/BrawTestSupport.swift` přidat:

```swift
extension BrawTest {
    /// One-frame 320×180 clip as braw-decode --info prints it (no gamma — like an older decoder).
    static let oneFrameInfo = #"{"width":320,"height":180,"sourceWidth":320,"sourceHeight":180,"fps":25,"frames":1,"timecode":"","camera":""}"#

    static let brawClip = Clip(name: "A001", path: "A001.braw", format: .braw, segments: ["A001.braw"], bytes: 1, isCameraProxy: false)

    static func clipInfo(frames: Int) -> ClipInfo {
        var i = ClipInfo()
        i.codec = "Blackmagic RAW"
        i.width = 320
        i.height = 180
        i.frameRate = "25/1"
        i.frames = frames
        i.duration = Double(frames) / 25
        return i
    }

    static func script(_ dir: TempDir, _ name: String, _ body: String) -> String { script(in: dir.path, name, body) }

    /// An executable sh script `<folder>/<name>`.
    static func script(in folder: String, _ name: String, _ body: String) -> String {
        let path = folder + "/" + name
        try! ("#!/bin/sh\n" + body + "\n").write(toFile: path, atomically: true, encoding: .utf8)
        try! FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: path)
        return path
    }

    /// A stand-in braw-decode: a sh script named braw-decode (errors carry that tool name) in `<dir>/fake-braw/`. `info`
    /// runs for `--info`, `frames` for frame decoding; every run first appends its first argument to `calls`.
    static func fakeDecoder(_ dir: TempDir, info: String, frames: String) -> (path: String, calls: String) {
        let sub = dir.sub("fake-braw")
        let calls = sub + "/calls.txt"
        let body = """
        echo "$1" >> '\(calls)'
        if [ "$1" = "--info" ]; then
        \(info)
        else
        \(frames)
        fi
        """
        return (script(in: sub, "braw-decode", body), calls)
    }

    static func printInfo(_ json: String = oneFrameInfo) -> String { "echo '\(json)'\nexit 0" }
    static func fail(_ code: Int, _ line: String) -> String { "echo '\(line)' >&2\nexit \(code)" }
    static let hang = "exec sleep 30"

    /// The first argument of every run of a fake decoder ("--info" or "--scale").
    static func calls(_ file: String) -> [String] {
        ((try? String(contentsOfFile: file, encoding: .utf8)) ?? "").split(separator: "\n").map(String.init)
    }
}

/// Log lines collected from another thread.
final class LogLines {
    private let lock = NSLock()
    private var all: [String] = []
    func add(_ s: String) { lock.lock(); all.append(s); lock.unlock() }
    var lines: [String] { lock.lock(); defer { lock.unlock() }; return all }
}
```

- [ ] **Step 2: Padající testy**

`mac/Tests/OffloadKitTests/ToolProcessTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class ToolProcessTests: XCTestCase {
    // Spec D.16 (Windows ruling M3): a hung tool never hangs the job — killed after the timeout, the message says so.
    func testTimeoutKillsAndSaysSo() {
        let hang = BrawTest.script(tempDir(), "hang", "exec sleep 30")
        let t0 = Date()
        XCTAssertThrowsError(try ToolProcess.run(hang, [], timeout: 1)) { e in
            XCTAssertEqual("\(e)", "hang failed (exit -1): neodpověděl do 1 s — ukončen")
        }
        XCTAssertLessThan(Date().timeIntervalSince(t0), 10)
    }

    func testWithinTheTimeoutStdoutAsBefore() throws {
        let ok = BrawTest.script(tempDir(), "ok", "sleep 0.3\necho hello")
        XCTAssertEqual(try ToolProcess.run(ok, [], timeout: 5), "hello\n")
        XCTAssertEqual(try ToolProcess.run(ok, []), "hello\n")
    }

    // The error carries the whole stderr even when the tool ends at once (the pipe handler may lag behind the exit).
    func testStderrCompleteInTheError() {
        let fail = BrawTest.script(tempDir(), "fail", "echo 'first line' >&2\necho 'braw-decode: cannot open /x/Klip č. 1.braw' >&2\nexit 4")
        for _ in 0..<20 {
            XCTAssertThrowsError(try ToolProcess.run(fail, [])) { e in
                XCTAssertEqual((e as? ToolError)?.status, 4)
                XCTAssertEqual((e as? ToolError)?.message, "first line\nbraw-decode: cannot open /x/Klip č. 1.braw\n")
            }
        }
    }
}
```

Do `mac/Tests/OffloadKitTests/BrawBackendTests.swift` (úkol 28) do třídy přidat:

```swift
    private let lib = "/BM/BlackmagicRawAPI.framework"

    // Windows task 1: braw-decode alone is not enough — without the library BRAW is copied and verified only.
    func testAvailableOnlyWithDecoderAndLibrary() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang).path
        var found: String?
        var searched: String?
        let backend = BRAWBackend(findLibrary: { searched = $0; return found })
        XCTAssertFalse(backend.isAvailable(ExternalTools()))
        XCTAssertNil(searched)   // no decoder: the library is not even looked for
        XCTAssertFalse(backend.isAvailable(ExternalTools(brawDecoder: exe)))
        XCTAssertEqual(searched, BRAWBackend.decoderDir(exe))
        XCTAssertTrue(searched?.hasSuffix("/fake-braw") == true, searched ?? "nil")
        found = lib
        XCTAssertTrue(backend.isAvailable(ExternalTools(brawDecoder: exe)))
    }

    func testDefaultSearchFindsTheFrameworkNextToTheDecoder() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang).path
        dir.sub("fake-braw", "BlackmagicRawAPI.framework")
        XCTAssertTrue(BRAWBackend().isAvailable(ExternalTools(brawDecoder: exe)))
    }

    func testWithoutTheLibraryTheClipIsUnsupportedWithTheWindowsHint() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang).path
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools(ffmpeg: "/bin/cat", brawDecoder: exe))
        r.backends[.braw] = BRAWBackend(findLibrary: { _ in nil })
        XCTAssertEqual(r.unsupportedReason(BrawTest.brawClip),
                       "Blackmagic RAW: chybí braw-decode nebo knihovna Blackmagic RAW (SDK, Player nebo DaVinci Resolve)")
    }

    // Windows final fix I1: exit codes 3/4/5 become a Czech note at the clip; the decoder's own line stays inside.
    func testInfoDecoderExitCodeBecomesCzechNote() {
        let cases: [(code: Int, line: String, note: String)] = [
            (3, "braw-decode: CreateCodec failed (0x80004005; library /BM/BlackmagicRawAPI.framework)",
             "Blackmagic RAW: knihovnu /BM/BlackmagicRawAPI.framework nelze použít — nainstalujte aktuální Blackmagic RAW Player"),
            (4, "braw-decode: cannot open /card/A001.braw (0x80004005; library /BM/BlackmagicRawAPI.framework)",
             "Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna /BM/BlackmagicRawAPI.framework starší než kamera (nainstalujte aktuální Blackmagic RAW Player)"),
            (5, "braw-decode: frame 0 failed", "Blackmagic RAW: snímek klipu se nepodařilo dekódovat (frame 0 failed)"),
        ]
        for c in cases {
            let dir = tempDir()
            let exe = BrawTest.fakeDecoder(dir, info: BrawTest.fail(c.code, c.line), frames: BrawTest.fail(c.code, c.line)).path
            XCTAssertThrowsError(try BRAWBackend(findLibrary: { _ in self.lib }).probe(dir.path + "/A001.braw",
                                                                                     tools: ExternalTools(brawDecoder: exe))) { e in
                guard let d = e as? RawDecoderError else { return XCTFail("\(c.code): \(e)") }
                XCTAssertEqual("\(d)", c.note)
                XCTAssertEqual(d.decoderError.status, Int32(c.code))
                XCTAssertEqual(d.decoderError.message.trimmingCharacters(in: .whitespacesAndNewlines), c.line)
            }
        }
    }

    func testOtherExitCodesStayAsTheyAre() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.fail(2, "usage: braw-decode"), frames: BrawTest.hang).path
        XCTAssertThrowsError(try BRAWBackend(findLibrary: { _ in self.lib }).probe(dir.path + "/A001.braw",
                                                                                 tools: ExternalTools(brawDecoder: exe))) { e in
            XCTAssertTrue(e is ToolError, "\(e)")
            XCTAssertEqual("\(e)", "braw-decode failed (exit 2): usage: braw-decode")
        }
    }

    /// The library vanished after the check: the note names the framework by its file name.
    func testNoteWithoutLibraryPathNamesTheFramework() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.fail(3, "braw-decode: BlackmagicRawAPI.framework not found"),
                                       frames: BrawTest.hang).path
        XCTAssertThrowsError(try BRAWBackend(findLibrary: { _ in nil }).probe(dir.path + "/A001.braw",
                                                                            tools: ExternalTools(brawDecoder: exe))) { e in
            XCTAssertEqual("\(e)", "Blackmagic RAW: knihovnu BlackmagicRawAPI.framework nelze použít — nainstalujte aktuální Blackmagic RAW Player")
        }
    }

    // Review focus 11 (Windows ruling M3): `braw-decode --info` runs with a timeout — a hung SDK gives the clip a note.
    func testInfoHungKilledAfterTheTimeout() {
        let dir = tempDir()
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.hang, frames: BrawTest.hang).path
        let t0 = Date()
        XCTAssertThrowsError(try BRAWBackend(findLibrary: { _ in self.lib }, infoTimeout: 2)
            .probe(dir.path + "/A001.braw", tools: ExternalTools(brawDecoder: exe))) { e in
            XCTAssertEqual("\(e)", "braw-decode failed (exit -1): neodpověděl do 2 s — ukončen")
        }
        XCTAssertTrue((1.9...30).contains(Date().timeIntervalSince(t0)), "\(Date().timeIntervalSince(t0))")
    }

    func testInfoDefaultTimeoutIs60s() {
        XCTAssertEqual(BRAWBackend.defaultInfoTimeout, 60)
        XCTAssertEqual(BRAWBackend().infoTimeout, 60)
    }

    // The note goes to the clip, the decoder's own words to the job's log (Windows ProxyRenderer.Logged).
    func testProbeDecoderExitCodeOriginalLogged() {
        let dir = tempDir()
        let line = "braw-decode: cannot open /card/A001.braw (0x80004005; library /BM/BlackmagicRawAPI.framework)"
        let exe = BrawTest.fakeDecoder(dir, info: BrawTest.fail(4, line), frames: BrawTest.hang).path
        let log = LogLines()
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools(ffmpeg: "/bin/cat", brawDecoder: exe))
        r.backends[.braw] = BRAWBackend(findLibrary: { _ in self.lib })
        r.log = { log.add($0) }
        XCTAssertThrowsError(try r.probe(BrawTest.brawClip, cardPath: dir.sub("card"))) { e in
            XCTAssertTrue("\(e)".hasPrefix("Blackmagic RAW: klip nelze otevřít"), "\(e)")
        }
        XCTAssertTrue(log.lines.contains("braw-decode failed (exit 4): \(line)"), "\(log.lines)")
    }
```

- [ ] **Step 3: Ověřit selhání** — Run: `swift test --filter "ToolProcessTests|BrawBackendTests"` → nesestaví se
(`extra argument 'timeout'`, `cannot find 'RawDecoderError'`, `argument passed to call that takes no arguments` u `BRAWBackend(findLibrary:)`,
`cannot assign to property: 'backends' is a 'let' constant`).

- [ ] **Step 4: `Tools.swift`** — celou třídu `ToolProcess` nahradit (a nad ni přidat `StderrTail`):

```swift
/// The last 4000 characters a child process wrote to stderr. The pipe is read as data arrives (a chatty tool never
/// blocks on a full pipe); `drain()` after the process ended picks up what the handler has not delivered yet —
/// non-blocking, so a grandchild still holding the pipe cannot hang the caller. The handler reads under the same lock
/// and never after `drain()`: a read racing drain's non-blocking one could hit EAGAIN, which `availableData` raises as
/// an Objective-C exception (a crash, not a Swift error). Bytes are kept and decoded only in `text`, so a UTF-8
/// character split between two reads (a path with diacritics) comes out whole.
final class StderrTail {
    private let pipe = Pipe()
    private let lock = NSLock()
    private var bytes = Data()
    private var drained = false

    init(_ process: Process) {
        process.standardError = pipe
        pipe.fileHandleForReading.readabilityHandler = { [weak self] h in
            guard let self else { return }
            self.lock.lock()
            defer { self.lock.unlock() }
            guard !self.drained else { return }
            let data = h.availableData
            if data.isEmpty {
                h.readabilityHandler = nil   // EOF: otherwise the handler keeps firing with empty data
            } else {
                self.appendLocked(data)
            }
        }
    }

    var text: String {
        lock.lock()
        defer { lock.unlock() }
        return String(String(decoding: bytes, as: UTF8.self).suffix(4000))
    }

    func drain() -> String {
        let handle = pipe.fileHandleForReading
        handle.readabilityHandler = nil   // outside the lock: a handler still waiting for it then sees `drained`
        lock.lock()
        if !drained {
            drained = true
            let fd = handle.fileDescriptor
            _ = fcntl(fd, F_SETFL, fcntl(fd, F_GETFL) | O_NONBLOCK)
            var buffer = [UInt8](repeating: 0, count: 65536)
            while true {
                let n = buffer.withUnsafeMutableBytes { Darwin.read(fd, $0.baseAddress, $0.count) }
                if n < 0 && errno == EINTR { continue }
                if n <= 0 { break }   // EOF, or nothing more right now (EAGAIN)
                appendLocked(Data(buffer[0..<n]))
            }
        }
        lock.unlock()
        return text
    }

    /// Caller holds `lock`. At most 16 000 bytes are kept (≥ 4000 characters of UTF-8).
    private func appendLocked(_ data: Data) {
        bytes.append(data)
        if bytes.count > 16_000 { bytes = Data(bytes.suffix(16_000)) }
    }
}

/// Runs a child process, streams stdout lines to a handler, captures the stderr tail for error messages.
final class ToolProcess {
    let process = Process()
    private var stderr: StderrTail?

    init(_ executable: String, _ args: [String]) {
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = args
    }

    var stderrText: String { stderr?.text ?? "" }

    func captureStderr() {
        stderr = StderrTail(process)
    }

    /// After the process ended: the complete stderr tail.
    func drainStderr() -> String { stderr?.drain() ?? "" }

    func onStdoutLine(_ handler: @escaping (String) -> Void) {
        let pipe = Pipe()
        process.standardOutput = pipe
        var pending = ""
        pipe.fileHandleForReading.readabilityHandler = { h in
            let data = h.availableData
            guard !data.isEmpty else { return }
            pending += String(decoding: data, as: UTF8.self)
            while let nl = pending.firstIndex(of: "\n") {
                handler(String(pending[..<nl]))
                pending = String(pending[pending.index(after: nl)...])
            }
        }
    }

    private final class Output {
        var data = Data()
        let done = DispatchSemaphore(value: 0)
    }

    /// Runs to completion and returns stdout (only when no line handler is installed). With `timeout`, a tool still
    /// running after it is killed (SIGKILL) and a ToolError says so — a hung tool must never hang the job (Windows
    /// `ToolProcess.Run(…, timeout)`; Windows kills the whole process tree, here the tool itself).
    @discardableResult
    static func run(_ executable: String, _ args: [String], cwd: String? = nil, timeout: TimeInterval? = nil) throws -> String {
        let p = ToolProcess(executable, args)
        if let cwd { p.process.currentDirectoryURL = URL(fileURLWithPath: cwd) }
        let name = (executable as NSString).lastPathComponent
        let out = Pipe()
        p.process.standardOutput = out
        p.captureStderr()
        try p.process.run()
        // stdout on its own thread, so the wait below can give up on a tool that never ends.
        let output = Output()
        Thread {
            output.data = out.fileHandleForReading.readDataToEndOfFile()
            output.done.signal()
        }.start()
        if let timeout {
            let deadline = Date().addingTimeInterval(timeout)
            while p.process.isRunning && Date() < deadline { usleep(20_000) }
            if p.process.isRunning {
                kill(p.process.processIdentifier, SIGKILL)
                throw ToolError(tool: name, status: -1, message: "neodpověděl do \(String(format: "%.0f", timeout)) s — ukončen")
            }
        }
        p.process.waitUntilExit()
        output.done.wait()
        guard p.process.terminationStatus == 0 else {
            throw ToolError(tool: name, status: p.process.terminationStatus, message: p.drainStderr())
        }
        return String(decoding: output.data, as: UTF8.self)
    }
}
```

- [ ] **Step 5: `RawBackends.swift`** — celý blok od `// MARK: - Blackmagic RAW` po `// MARK: - RED` nahradit:

```swift
// MARK: - Blackmagic RAW (braw-decode helper → raw RGB48 frames on a pipe)

/// A RAW decoder's error turned into a note for the clip (Windows `RawDecoderException`). `description` is the note;
/// the decoder's own error, with its stderr (e.g. the library it loaded), stays in `decoderError` for the job's log.
struct RawDecoderError: Error, CustomStringConvertible {
    let note: String
    let decoderError: ToolError

    var description: String { note }
}

struct BRAWBackend: RawBackend {
    let format = ClipFormat.braw
    let missingHint = "Blackmagic RAW: chybí braw-decode nebo knihovna Blackmagic RAW (SDK, Player nebo DaVinci Resolve)"

    /// How long `braw-decode --info` may take: opening a clip is quick; a hung SDK must never hang the job (the clip
    /// then gets a note instead).
    static let defaultInfoTimeout: TimeInterval = 60

    /// Decoder folder → library path. Injectable so tests do not depend on what is installed on the Mac.
    var findLibrary: (String) -> String? = { BrawLibrary.find(decoderDir: $0) }
    /// Tests shorten it.
    var infoTimeout: TimeInterval = BRAWBackend.defaultInfoTimeout

    /// braw-decode alone is not enough: without BlackmagicRawAPI.framework it can only fail (exit 3).
    func isAvailable(_ tools: ExternalTools) -> Bool {
        guard let exe = tools.brawDecoder else { return false }
        return findLibrary(BRAWBackend.decoderDir(exe)) != nil
    }

    /// The folder braw-decode looks in first ("@exe": its real path, symlinks resolved).
    static func decoderDir(_ exe: String) -> String {
        URL(fileURLWithPath: exe).resolvingSymlinksInPath().deletingLastPathComponent().path
    }

    /// One line of `braw-decode --info`. gamma / gamut / colorScienceGen only when the SDK reports them (braw-decode
    /// since task 27, and Windows); an older decoder leaves them out.
    struct Info: Decodable {
        let width: Int, height: Int, sourceWidth: Int, sourceHeight: Int
        let fps: Double, frames: Int, timecode: String, camera: String
        let gamma: String?, gamut: String?, colorScienceGen: Int?
    }

    static func parseInfo(_ json: String) throws -> Info {
        try JSONDecoder().decode(Info.self, from: Data(json.utf8))
    }

    static func clipInfo(_ i: Info) -> ClipInfo {
        var c = ClipInfo()
        c.codec = "Blackmagic RAW"
        c.width = i.sourceWidth
        c.height = i.sourceHeight
        c.frameRate = RawBackends.rational(i.fps)
        c.frames = i.frames
        c.duration = i.fps > 0 ? Double(i.frames) / i.fps : nil
        c.timecode = i.timecode.isEmpty ? nil : i.timecode
        c.camera = i.camera.isEmpty ? nil : i.camera
        c.gamma = i.gamma.flatMap { $0.isEmpty ? nil : $0 }
        c.gamut = i.gamut.flatMap { $0.isEmpty ? nil : $0 }
        c.colorScienceGen = i.colorScienceGen
        return c
    }

    private func info(_ path: String, scale: String, tools: ExternalTools) throws -> Info {
        let out: String
        do {
            out = try ToolProcess.run(tools.brawDecoder!, ["--info", "--scale", scale, path], timeout: infoTimeout)
        } catch let e as ToolError {
            throw explain(e, tools: tools)
        }
        return try BRAWBackend.parseInfo(out)
    }

    /// braw-decode's exit codes 3 (library unusable), 4 (clip cannot be opened) and 5 (a frame failed) as a note for
    /// the clip; the original error, with braw-decode's stderr, stays inside it for the log. Anything else (a timeout,
    /// a usage error) is returned as it is. Texts as Windows `BrawBackend.Explain`.
    func explain(_ e: ToolError, tools: ExternalTools) -> Error {
        let lib = tools.brawDecoder.flatMap { findLibrary(BRAWBackend.decoderDir($0)) } ?? BrawLibrary.frameworkName
        let note: String
        switch e.status {
        case 3: note = "Blackmagic RAW: knihovnu \(lib) nelze použít — nainstalujte aktuální Blackmagic RAW Player"
        case 4: note = "Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna \(lib) starší než kamera (nainstalujte aktuální Blackmagic RAW Player)"
        case 5: note = "Blackmagic RAW: snímek klipu se nepodařilo dekódovat (\(BRAWBackend.lastLine(e.message)))"
        default: return e
        }
        return RawDecoderError(note: note, decoderError: e)
    }

    /// braw-decode's last stderr line without its "braw-decode: " prefix.
    static func lastLine(_ stderr: String) -> String {
        let line = stderr.split(separator: "\n").map { $0.trimmingCharacters(in: .whitespaces) }.last { !$0.isEmpty } ?? ""
        return line.hasPrefix("braw-decode: ") ? String(line.dropFirst("braw-decode: ".count)) : line
    }

    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        BRAWBackend.clipInfo(try info(path, scale: "eighth", tools: tools))
    }

    /// Smallest decode scale that still covers the target height — decoding less is much faster.
    private func scale(sourceHeight: Int, target: Int) -> String {
        for (name, div) in [("eighth", 8), ("quarter", 4), ("half", 2)] where sourceHeight / div >= target { return name }
        return "full"
    }

    /// The frame braw-decode starts at for a thumbnail `seconds` into the clip. Rounded: `seconds` comes from the
    /// exact rational rate (24000/1001), braw-decode reports a rounded fps (23.976) — truncating would land a frame early.
    static func startFrame(seconds: Double, fps: Double, frames: Int) -> Int {
        let rate = fps > 0 ? fps : 25
        return min(max(0, frames - 1), max(0, Int((seconds * rate).rounded())))
    }

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        let target = singleFrameAt == nil ? settings.height : 180
        let s = scale(sourceHeight: c.height ?? 2160, target: target)
        let dims = try info(path, scale: s, tools: tools)
        var args = ["--scale", s]
        if let t = singleFrameAt {
            args += ["--start", String(BRAWBackend.startFrame(seconds: t, fps: dims.fps, frames: dims.frames)), "--count", "1"]
        }
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: tools.brawDecoder!)
        feeder.arguments = args + [path]
        // braw-decode hands over full-range RGB (RGBU16) in the clip's own gamma: a built-in LUT needs the pre-map first.
        return FrameSource(inputArgs: ["-f", "rawvideo", "-pix_fmt", "rgb48le", "-s", "\(dims.width)x\(dims.height)",
                                       "-r", c.frameRate ?? "25", "-i", "-"],
                           feeder: feeder, audioFromInput: false, fullRange: true,
                           explainFeederError: { self.explain($0, tools: tools) })
    }
}
```

(`RawBackends.all` dál vytváří `BRAWBackend()` — výchozí hledání knihovny a limit 60 s.)

- [ ] **Step 6: `Proxy.swift`**

1. Do `struct FrameSource` za `cleanup` přidat:

```swift
    /// Turns the feeder's own failure (it ended non-zero while ffmpeg still read) into the clip's note — a
    /// `RawDecoderError`; nil = the feeder's ToolError as it is (Windows `FrameSource.ExplainFeederError`).
    var explainFeederError: ((ToolError) -> Error)?
```

2. V `ProxyRenderer` nahradit `let backends: [ClipFormat: RawBackend]` za:

```swift
    /// One backend per RAW format. Tests replace one (e.g. BRAW with a fake library search or a short --info timeout).
    var backends: [ClipFormat: RawBackend]
```

3. V `probe(_:cardPath:)` poslední řádek `return try b.probe(path, tools: tools)` nahradit
`return try logged { try b.probe(path, tools: tools) }`; ve `frameSource(…)` poslední řádek nahradit
`return try logged { try b.source(path, info: info, settings: settings, look: look, tools: tools, tempDir: tempDir, singleFrameAt: singleFrameAt) }`
a pod `frameSource` přidat:

```swift
    /// A decoder error turned into a note for the clip (`RawDecoderError`): the decoder's own words — its stderr, e.g.
    /// the library it loaded — go to the job's log, the note to the clip (Windows `Logged`).
    private func logged<T>(_ decode: () throws -> T) throws -> T {
        do {
            return try decode()
        } catch let e as RawDecoderError {
            log?("\(e.decoderError)")
            throw e
        }
    }
```

- [ ] **Step 7: Testy projdou** — Run: `swift test --filter "ToolProcessTests|BrawBackendTests|LutResolverTests"` → 0 chyb
(`testInfoHungKilledAfterTheTimeout` ~2 s); pak celé `swift test`.

- [ ] **Step 8: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 29 — BRAW jen s knihovnou Blackmagic RAW (bez ní se klip jen zkopíruje a ověří, nápověda jako Windows); braw-decode --info s limitem 60 s (zaseknutá knihovna úlohu nezasekne, poznámka „neodpověděl do 60 s — ukončen“); kódy 3/4/5 → česká poznámka u klipu, původní hláška braw-decode do logu; ToolProcess čte stderr celé i u nástroje, který hned skončí`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: BRAW only with the library, --info timeout, Czech notes for braw-decode exit codes"`

---

### Task 30: Náhledy BRAW pod hlídačem, pumpa zavře výstup dekodéru

Doplněk 2 ke spec — oddíl D.17. Port `windows/src/Offload.Core/ProxyRenderer.cs` (`Thumbnails` s hlídačem pro dekodér
v rouře a zastavením po prvním zaseknutí / limitu `--info`, `IsBrawDecoder`; `RunFfmpeg`: `feederCut`, pumpa zavře výstup
dekodéru, čekání 2 s na dekodér, `ExplainFeederError`); commity `7c3019f`, `c3a04f4`, `222d833`, `9db140f`, `b967c94`, `d3d3bc2`;
testy `ProxyWatchdogTests.BrawThumbnail_*`, `ConsumerEndsFirst_*`, `BrawCoreTests.Feeder_*`.

**Files:**
- Modify: `mac/Sources/OffloadKit/FFmpegWatch.swift` (nová `Flag`)
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (`thumbnails`, `isBrawDecoder`, `runFFmpeg`, volání v `renderOnce`)
- Create: `mac/Tests/OffloadKitTests/BrawFeederTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `StderrTail`, `RawDecoderError`, `FrameSource.explainFeederError`, `BrawTest` (úkoly 27, 29), `FFmpegStall`, `ProgressWatch`, `stallTimeout`, `log` (úkol 19).
- Produces:
  - `final class Flag { func set(); var isSet: Bool { get } }`
  - `func ProxyRenderer.runFFmpeg(_:_:feeder:explainFeederError: ((ToolError) -> Error)? = nil, duration:progress:isCancelled:stallAfter:)` — dosavadní volání (FFmpegWatchTests) platí beze změny
  - `thumbnails(…)` — signatura beze změny; náhledy z dekodéru v rouře hlídané (`stallTimeout`)

Pravidla (1:1 s Windows):

- **Náhled s dekodérem v rouře** (BRAW) běží pod hlídačem jako proxy: pokrok = data z dekodéru (ffmpeg u náhledu `-progress`
  nepíše); 60 s, do prvních dat 180 s. Video soubory hlídané nejsou (beze změny).
- **Po prvním zaseknutí** se další pozice klipu nezkoušejí (jinak až 3 × 180 s na klip). Chyba náhledu
  `dekodér nebo ffmpeg se zasekl (60 s bez nového snímku)`, log `thumbnails <klip>: dekodér nebo ffmpeg se zasekl (60 s bez
  nového snímku); the remaining frames are skipped`. Totéž po vypršení limitu `braw-decode --info`: log `thumbnails <klip>:
  braw-decode failed (exit -1): neodpověděl do 60 s — ukončen; the remaining frames are skipped`. Texty poznámek u proxy beze změny.
- **ffmpeg skončí dřív než dekodér** (náhled potřebuje jeden snímek, nebo ffmpeg selhal): pumpa si označí „odříznuto“ a zavře
  i výstup dekodéru — `braw-decode` uvidí zavřenou rouru a skončí sám (kód 0); na dekodér se čeká nejvýš 2 s, pak SIGTERM.
- **Posouzení dekodéru po konci ffmpeg:** skončil sám s kódem ≠ 0, zatímco ffmpeg ještě četl → chyba klipu (a přednost před
  chybou ffmpeg, který viděl jen useknutý proud), přes `explainFeederError` česká poznámka a původní hláška do logu. Ukončený
  signálem (SIGTERM po 2 s, zrušení, hlídač) nebo odříznutý pumpou → jeho kód se ignoruje, rozhoduje ffmpeg.

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/BrawFeederTests.swift` (bez knihovny i ffmpeg — skripty):

```swift
import XCTest
@testable import OffloadKit

final class BrawFeederTests: XCTestCase {
    private let lib = "/BM/BlackmagicRawAPI.framework"

    /// Stand-in ffmpeg that reads its input to the end and prints no progress.
    private func consumer(_ dir: TempDir) -> String { BrawTest.script(dir, "ffmpeg", "cat > /dev/null") }

    private func renderer(_ dir: TempDir, decoder: String, log: LogLines? = nil, infoTimeout: TimeInterval = 60) -> ProxyRenderer {
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools(ffmpeg: consumer(dir), brawDecoder: decoder))
        r.stallTimeout = 1
        r.backends[.braw] = BRAWBackend(findLibrary: { _ in "/BM/BlackmagicRawAPI.framework" }, infoTimeout: infoTimeout)
        if let log { r.log = { log.add($0) } }
        return r
    }

    // Review focus 11 (Windows ruling M3): a BRAW thumbnail (decoder piped into ffmpeg) runs under the stall watchdog.
    func testThumbnailDecoderHangsKilledAsStalled() {
        let dir = tempDir()
        let fake = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang)
        let t0 = Date()
        XCTAssertThrowsError(try renderer(dir, decoder: fake.path).thumbnails(BrawTest.brawClip, cardPath: dir.sub("card"),
                                                                          info: BrawTest.clipInfo(frames: 1))) { e in
            XCTAssertEqual("\(e)", "dekodér nebo ffmpeg se zasekl (1 s bez nového snímku)")
        }
        XCTAssertLessThan(Date().timeIntervalSince(t0), 30)
    }

    // Windows ruling T8-extra, final fix M2: one stall per clip, and the log says why the other frames are missing.
    func testThumbnailDecoderHangsOnlyOneStallAttempt() {
        let dir = tempDir()
        let fake = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang)
        let log = LogLines()
        let info = BrawTest.clipInfo(frames: 100)
        XCTAssertEqual(ProxyRenderer.framePositions(info).count, 3)
        let t0 = Date()
        XCTAssertThrowsError(try renderer(dir, decoder: fake.path, log: log).thumbnails(BrawTest.brawClip, cardPath: dir.sub("card"),
                                                                                    info: info)) { e in
            XCTAssertTrue("\(e)".hasPrefix("dekodér nebo ffmpeg se zasekl"), "\(e)")
        }
        XCTAssertEqual(BrawTest.calls(fake.calls).filter { $0 != "--info" }.count, 1)
        XCTAssertLessThan(Date().timeIntervalSince(t0), 60)
        XCTAssertTrue(log.lines.contains("thumbnails A001: dekodér nebo ffmpeg se zasekl (1 s bez nového snímku); the remaining frames are skipped"),
                      "\(log.lines)")
    }

    // Windows final fix M1: a hung `braw-decode --info` (timeout) is not tried again at the next positions either.
    func testThumbnailInfoHangsOnlyOneAttempt() {
        let dir = tempDir()
        let fake = BrawTest.fakeDecoder(dir, info: BrawTest.hang, frames: BrawTest.hang)
        let log = LogLines()
        let info = BrawTest.clipInfo(frames: 100)
        XCTAssertThrowsError(try renderer(dir, decoder: fake.path, log: log, infoTimeout: 1)
            .thumbnails(BrawTest.brawClip, cardPath: dir.sub("card"), info: info)) { e in
            XCTAssertEqual("\(e)", "braw-decode failed (exit -1): neodpověděl do 1 s — ukončen")
        }
        XCTAssertEqual(BrawTest.calls(fake.calls), ["--info"])
        XCTAssertTrue(log.lines.contains("thumbnails A001: braw-decode failed (exit -1): neodpověděl do 1 s — ukončen; the remaining frames are skipped"),
                      "\(log.lines)")
    }

    // Windows final fix I1: the decoder fails while ffmpeg reads its frames — the same Czech note, its own line logged.
    func testFeederDecoderExitCodeBecomesCzechNoteOriginalLogged() {
        let dir = tempDir()
        let line = "braw-decode: cannot open /card/A001.braw (0x80004005; library /BM/BlackmagicRawAPI.framework)"
        let fake = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.fail(4, line))
        let log = LogLines()
        XCTAssertThrowsError(try renderer(dir, decoder: fake.path, log: log).thumbnails(BrawTest.brawClip, cardPath: dir.sub("card"),
                                                                                    info: BrawTest.clipInfo(frames: 1))) { e in
            XCTAssertTrue(e is RawDecoderError, "\(e)")
            XCTAssertTrue("\(e)".hasPrefix("Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna \(lib)"), "\(e)")
        }
        XCTAssertTrue(log.lines.contains("braw-decode failed (exit 4): \(line)"), "\(log.lines)")
    }

    // A decoder failing while ffmpeg still reads stays the error — with its stderr, no explanation without one.
    func testFeederFailsWhileFFmpegReadsIsTheError() {
        let dir = tempDir()
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: BrawTest.script(dir, "feeder", "echo boom >&2\nexit 3"))
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        XCTAssertThrowsError(try r.runFFmpeg(consumer(dir), [], feeder: feeder, duration: nil, progress: nil,
                                             isCancelled: { false }, stallAfter: 30)) { e in
            XCTAssertEqual("\(e)", "feeder failed (exit 3): boom")
        }
    }

    /// A decoder that writes until a write fails (SIGPIPE ignored, like braw-decode), then writes `stopped` and exits
    /// `code`; and a consumer that takes 1 KiB and ends, like ffmpeg with its one thumbnail frame.
    private func writesUntilCut(_ dir: TempDir, code: Int) -> (feeder: Process, consumer: String, stopped: String) {
        let stopped = dir.path + "/stopped.txt"
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: BrawTest.script(dir, "feeder",
            "trap '' PIPE\n/bin/dd if=/dev/zero bs=1024 2>/dev/null\necho stopped > '\(stopped)'\nexit \(code)"))
        return (feeder, BrawTest.script(dir, "consumer", "head -c 1024 > /dev/null"), stopped)
    }

    // Windows task 7: ffmpeg ends first → the pump closes the decoder's stdout, the decoder sees the broken pipe and
    // stops by itself instead of blocking on a full pipe until it is terminated.
    func testConsumerEndsFirstFeederSeesTheClosedPipeExitsByItself() throws {
        let dir = tempDir()
        let (feeder, consumer, stopped) = writesUntilCut(dir, code: 0)
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        try r.runFFmpeg(consumer, [], feeder: feeder, duration: nil, progress: nil, isCancelled: { false }, stallAfter: 30)
        XCTAssertTrue(FileManager.default.fileExists(atPath: stopped), "the feeder never saw its stdout closed (it was terminated)")
        XCTAssertEqual(feeder.terminationReason, .exit)
    }

    // Windows task 7, fix 1: a decoder that fails because the pump cut its stdout is not the clip's error — ffmpeg decides.
    func testConsumerEndsFirstFeederFailsOnTheCutPipeNotAnError() throws {
        let dir = tempDir()
        let (feeder, consumer, stopped) = writesUntilCut(dir, code: 1)
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        try r.runFFmpeg(consumer, [], feeder: feeder, duration: nil, progress: nil, isCancelled: { false }, stallAfter: 30)
        XCTAssertTrue(FileManager.default.fileExists(atPath: stopped), "the feeder never saw its stdout closed (it was terminated)")
        XCTAssertEqual(feeder.terminationStatus, 1)
    }
}
```

- [ ] **Step 2: Ověřit selhání** — Run: `swift test --filter BrawFeederTests` → sestaví se (testy volají jen API z úkolů 19–29),
ale selžou: náhled čeká 30 s na falešný dekodér a končí „no frame“ místo zaseknutí po 3 s, `stopped.txt` chybí (dekodér byl
ukončen), poznámka u klipu není česká a v logu chybí původní hláška.

- [ ] **Step 3: `FFmpegWatch.swift`** — na konec přidat:

```swift
/// A one-way flag set on one thread and read on another (the pump marks a decoder pipe it cut).
final class Flag {
    private let lock = NSLock()
    private var value = false

    func set() {
        lock.lock()
        value = true
        lock.unlock()
    }

    var isSet: Bool {
        lock.lock()
        defer { lock.unlock() }
        return value
    }
}
```

- [ ] **Step 4: `Proxy.swift` (`ProxyRenderer`)**

1. Celou funkci `thumbnails(…)` nahradit:

```swift
    /// First, middle and last frame as small JPEGs with captions, with the clip's LUT so log footage is readable.
    /// `lut` nil = no LUT (thumbnails taken from a finished proxy, which already has it). When `isCancelled` turns
    /// true the clip simply gets no thumbnails. One bad position does not cost the others — except a hung RAW decoder
    /// (a stall, or `braw-decode --info` timing out), which would hang at the next positions too: the rest is skipped and
    /// the log says so. If none works, the last error is thrown.
    public func thumbnails(_ clip: Clip, cardPath: String, info: ClipInfo, lut: LutUse? = nil,
                           isCancelled: () -> Bool = { false }) throws -> [Thumbnail] {
        guard let ffmpeg = tools.ffmpeg else { throw ToolError(tool: "ffmpeg", status: -1, message: "ffmpeg not found") }
        let lutFile = try prepareLUT(lut)
        let look = ProxyRenderer.lookFor(lutFile != nil ? lut : nil, info: info)   // log only when a LUT really follows
        let video = clip.format == .video
        var result: [Thumbnail] = []
        var lastError: Error?
        for pos in ProxyRenderer.framePositions(info) {
            if isCancelled() { return [] }
            let out = tempDir + "/thumb-\(UUID().uuidString).jpg"
            defer { unlink(out) }
            var src: FrameSource?
            do {
                let s = try frameSource(clip, cardPath: cardPath, info: info, look: look, singleFrameAt: pos.seconds, hwaccel: false)
                src = s
                defer { s.removeIntermediates() }
                // The pre-map depends on the source (BRAW, full-range video).
                let filter = FilterGraph.thumbnail(lutFile: lutFile, premap: ProxyRenderer.needsPreMap(lut, source: s, info: info))
                var args = ["-hide_banner", "-loglevel", "error", "-y"]
                if video { args += ["-ss", ProxyRenderer.seekSeconds(info, pos)] }
                args += s.inputArgs
                // Last frame: decode the final second and keep overwriting the image — the last write is the last frame.
                args += video && pos.fromEnd ? ["-map", "0:v:0", "-vf", filter, "-update", "1"] : ["-frames:v", "1", "-vf", filter]
                args += ["-q:v", "5", out]
                // A RAW decoder piped in (BRAW) runs under the stall watchdog: the data it hands ffmpeg is the progress, and
                // a hung SDK must not hang the job. A file input prints no progress here, so it is left alone (as Windows).
                try runFFmpeg(ffmpeg, args, feeder: s.feeder, explainFeederError: s.explainFeederError, duration: nil,
                              progress: nil, isCancelled: isCancelled, stallAfter: s.feeder != nil ? stallTimeout : nil)
                guard FileManager.default.fileExists(atPath: out) else {
                    throw ToolError(tool: "ffmpeg", status: 0, message: "no frame")
                }
                result.append(Thumbnail(jpeg: try Data(contentsOf: URL(fileURLWithPath: out)), label: ProxyRenderer.label(info, pos)))
            } catch let e as FFmpegStall where src?.feeder != nil {
                if isCancelled() { return [] }
                // A hung decoder would hang at the next positions too (3 × the grace per clip): stop here.
                let stall = FFmpegStall(note: "dekodér nebo " + e.note)
                lastError = stall
                log?("thumbnails \(clip.name): \(stall); the remaining frames are skipped")
                break
            } catch let e as ToolError where src == nil && e.status == -1 && isBrawDecoder(e.tool) {
                if isCancelled() { return [] }
                // `braw-decode --info` timed out: it would at the next positions too.
                lastError = e
                log?("thumbnails \(clip.name): \(e); the remaining frames are skipped")
                break
            } catch {
                if isCancelled() { return [] }
                lastError = error
            }
        }
        if result.isEmpty, let lastError { throw lastError }
        return result
    }

    /// A ToolError raised by braw-decode (ToolProcess names errors by the executable's file name).
    private func isBrawDecoder(_ tool: String) -> Bool {
        tools.brawDecoder.map { ($0 as NSString).lastPathComponent == tool } ?? false
    }
```

2. V `renderOnce` volání `runFFmpeg` nahradit:

```swift
            try runFFmpeg(ffmpeg, args, feeder: src.feeder, explainFeederError: src.explainFeederError, duration: info.duration,
                          progress: progress, isCancelled: isCancelled, stallAfter: stallTimeout)
```

3. Celou funkci `runFFmpeg` nahradit (`stop` beze změny):

```swift
    /// `stallAfter`: ffmpeg is killed and `FFmpegStall` thrown when it has reported no new frame or output time and taken
    /// no data from the feeder for that long (3× before the first progress) — every proxy, and thumbnails with a feeder.
    /// Cancelling is checked first and wins. A feeder (RAW decoder) is judged as on Windows: one that ended non-zero on
    /// its own while ffmpeg still read is the clip's error, before ffmpeg's (`explainFeederError` may turn it into the
    /// note); one that was terminated, or whose stdout the pump closed because ffmpeg had stopped reading, is not.
    func runFFmpeg(_ ffmpeg: String, _ args: [String], feeder: Process?, explainFeederError: ((ToolError) -> Error)? = nil,
                   duration: Double?, progress: ((Double) -> Void)?, isCancelled: () -> Bool,
                   stallAfter: TimeInterval? = nil) throws {
        let p = ToolProcess(ffmpeg, args)
        p.captureStderr()
        let watch = ProgressWatch()
        // Set by the pump when ffmpeg stopped taking data and the feeder's stdout was closed under it.
        let feederCut = Flag()
        var feederStderr: StderrTail?
        var pump: Thread?
        if let feeder {
            // The decoder's frames go through a pump: every chunk ffmpeg takes is progress (a slow RAW decoder is not stuck).
            let fromFeeder = Pipe(), toFFmpeg = Pipe()
            feeder.standardOutput = fromFeeder
            // Its stderr names the cause when it fails on its own (braw-decode: the clip and the library it loaded).
            feederStderr = StderrTail(feeder)
            p.process.standardInput = toFFmpeg
            // A write after ffmpeg died must fail with EPIPE, not take the app down with SIGPIPE.
            _ = fcntl(toFFmpeg.fileHandleForWriting.fileDescriptor, F_SETNOSIGPIPE, 1)
            pump = Thread {
                // Plain read(2)/write(2): read returns whatever the decoder has written so far. FileHandle.read(upToCount:)
                // on a pipe waits for the whole count (or EOF) — a slow decoder would look stuck.
                let inFD = fromFeeder.fileHandleForReading.fileDescriptor
                let outFD = toFFmpeg.fileHandleForWriting.fileDescriptor
                let size = 4 << 20
                let buffer = UnsafeMutableRawPointer.allocate(byteCount: size, alignment: 16)
                defer { buffer.deallocate() }
                pumping: while true {
                    let n = Darwin.read(inFD, buffer, size)
                    if n < 0 && errno == EINTR { continue }
                    if n <= 0 { break }   // the decoder is done (EOF) or gone
                    var sent = 0
                    while sent < n {
                        let w = Darwin.write(outFD, buffer + sent, n - sent)
                        if w < 0 && errno == EINTR { continue }
                        if w <= 0 {
                            // ffmpeg went away first (a thumbnail needs one frame, or it failed). Marked before the
                            // decoder's end is closed below, so its exit code is known to be a consequence.
                            feederCut.set()
                            break pumping
                        }
                        sent += w
                    }
                    watch.dataFlowed()
                }
                try? toFFmpeg.fileHandleForWriting.close()
                // Close the decoder's end too: a decoder still writing sees the broken pipe and stops by itself
                // (braw-decode exits 0) instead of blocking on a full pipe until it is terminated.
                try? fromFeeder.fileHandleForReading.close()
            }
        } else {
            p.process.standardInput = FileHandle.nullDevice
        }
        p.onStdoutLine { line in
            watch.advanced(line)
            guard let progress, let d = duration, d > 0, line.hasPrefix("out_time_us="),
                  let us = Double(line.dropFirst("out_time_us=".count)) else { return }
            progress(min(1, us / 1_000_000 / d))
        }
        try feeder?.run()
        do {
            try p.process.run()
        } catch {
            feeder?.terminate()
            throw error
        }
        pump?.start()
        while p.process.isRunning {
            if isCancelled() {
                stop(p, feeder)
                throw ToolError(tool: "ffmpeg", status: -1, message: "cancelled")
            }
            if let limit = stallAfter, watch.isStalled(limit: limit) {
                stop(p, feeder)
                throw FFmpegStall(note: ProxyRenderer.stallNote(limit, retriedInSoftware: false))
            }
            usleep(200_000)
        }
        if let feeder {
            // ffmpeg is done. A decoder still writing gets the closed pipe from the pump and ends by itself; one that has
            // not ended within 2 s (decoding a frame nobody wants) is terminated — its exit code then says nothing.
            let deadline = Date().addingTimeInterval(2)
            while feeder.isRunning && Date() < deadline { usleep(20_000) }
            if feeder.isRunning { feeder.terminate() }
            feeder.waitUntilExit()
            if feeder.terminationReason == .exit, feeder.terminationStatus != 0, !feederCut.isSet {
                let error = ToolError(tool: feeder.executableURL?.lastPathComponent ?? "decoder", status: feeder.terminationStatus,
                                      message: feederStderr?.drain() ?? "")
                let explained = explainFeederError?(error) ?? error
                if explained is RawDecoderError { log?("\(error)") }   // the decoder's own words for the log
                throw explained
            }
        }
        guard p.process.terminationStatus == 0 else {
            // drain: ffmpeg has ended, the whole stderr tail (the handler may lag behind the exit — task 29)
            throw ToolError(tool: "ffmpeg", status: p.process.terminationStatus, message: p.drainStderr())
        }
    }
```

- [ ] **Step 5: Testy projdou** — Run: `swift test --filter "BrawFeederTests|FFmpegWatchTests|ThumbnailTests|BrawBackendTests"` → 0 chyb
(desítky sekund — skripty spí); pak celé `swift test`.

**Ověřit na Macu** (spec D.19, ř. 4): `trap '' PIPE` se přenese na `/bin/dd` (ignorovaný signál přežije `exec`) a `dd` po EPIPE
skončí — jinak `testConsumerEndsFirst…` hlásí „it was terminated“; pak místo `dd` použít malý Swift/C pomocník a zapsat do commitu.
Oprava patří do testu jen tehdy, když `feeder.terminationReason` je `.uncaughtSignal` kvůli SIGPIPE skriptu — ne do `runFFmpeg`.

- [ ] **Step 6: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 30 — náhledy BRAW pod hlídačem jako proxy (zaseknutý dekodér se ukončí, klip náhled nedostane, úloha jede dál); po prvním zaseknutí nebo limitu --info se další snímky klipu nezkoušejí a log to řekne; když ffmpeg skončí dřív, pumpa zavře i výstup dekodéru (braw-decode skončí sám s kódem 0, odříznutý dekodér se neposuzuje); dekodér, který selže, zatímco ffmpeg čte, je chyba klipu s českou poznámkou`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: BRAW thumbnails under the stall watchdog; the pump closes the decoder's stdout when ffmpeg ends first"`

---

### Task 31: BRAW se vzorkem — celá cesta, dokumentace

Doplněk 2 ke spec — oddíly D.18–D.20. Port `BrawSampleTests.Offload_ProbeThumbnailsProxy` a README Windows „Vzorek BRAW“.

**Files:**
- Create: `mac/Tests/OffloadKitTests/BrawSampleTests.swift`
- Modify: `mac/README.md`, `docs/ROZDILY.md`, `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: vše z úkolů 27–30; `LutResolver`, `TestMedia.luts`, `TestMedia.requireFFmpeg`, `FFProbe.probe`.
- Produces: jen test a dokumentace.

- [ ] **Step 1: Test celé cesty** — `mac/Tests/OffloadKitTests/BrawSampleTests.swift`:

```swift
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
```

Run: `swift test --filter BrawSampleTests` → bez vzorku přeskočeno; se vzorkem (`OFFLOAD_BRAW_SAMPLE=… swift test --filter Braw`)
vše PASS a 0 přeskočených.

- [ ] **Step 2: Ruční ověření se vzorkem** (až bude `.braw`; README-START „Vzorek BRAW“ krok 3–5):
`Tools/bin/braw-decode --info --scale eighth "$OFFLOAD_BRAW_SAMPLE"` — nejdřív stderr, pak JSON; `swift run offload <složka se vzorkem>
-d <cíl> --proxy` → v reportu tři snímky, „LUT: Blackmagic Film Gen 5 → Rec.709 (Blackmagic RAW: Blackmagic Design Film Gen 5)“
(u klipu v Gen 5), proxy `.mov` se vizuálně shoduje s Blackmagic RAW Playerem s LUTem Film → Rec.709 (barvy, orientace, bez posunu
o snímek; první/prostřední/poslední snímek odpovídají časům). Výsledek (kamera, gama, generace, co nesedělo) do `docs/VYVOJ-MAC.md`.

- [ ] **Step 3: `mac/README.md`** — v tabulce formátů řádek Blackmagic RAW: sloupec nástroje `braw-decode → ffmpeg (LUT podle
gamy klipu)`, sloupec instalace `Blackmagic RAW Player, SDK nebo DaVinci Resolve (bez knihovny se BRAW jen zkopíruje a ověří)`;
za oddíl „Build“ přidat:

~~~markdown
### Vzorek BRAW (testy dekodéru Blackmagic RAW)

`Tools/braw-decode/build.sh` sestaví `Tools/bin/braw-decode` (stačí Xcode; `scripts/build-app.sh` ho sestaví jen když chybí —
po změně `braw-decode.cpp` spustit ručně). Za běhu potřebuje knihovnu `BlackmagicRawAPI.framework` — Blackmagic RAW Player
nebo SDK (zdarma, https://www.blackmagicdesign.com/products/blackmagicraw) nebo DaVinci Resolve. Testy se skutečným klipem:

```sh
OFFLOAD_BRAW_SAMPLE="$HOME/Vzorky/A001_01011200_C001.braw" swift test --filter Braw   # 0 přeskočených
```

Ukázkové klipy: stránka Blackmagic RAW → „Download RAW Samples“ (formulář, zdarma; stovky MB až jednotky GB).
~~~

- [ ] **Step 4: `docs/ROZDILY.md`**
  - Funkce a stav, řádek „Blackmagic RAW“, sloupec Mac: `✅ braw-decode (C++, knihovna z SDK / Player / Resolve); čeká ověření se vzorkem .braw`.
  - Technologie: přidat řádek `| Dekodér BRAW | braw-decode (C++ nad BlackmagicRawAPI.framework, CFBundle) | braw-decode.exe (C# nad COM BlackmagicRawAPI.dll) |`.
  - Záměrné rozdíly: odrážku „**Blackmagic RAW (`braw-decode`):** Windows `--info` navíc vydává …“ nahradit:
    `- **Blackmagic RAW:** obě verze vybírají LUT podle gamy klipu z braw-decode --info (Film Gen 5 → vestavěný LUT; Video, Extended Video a Film Gen 4 bez LUTu — Gen 4 provizorně, ke schválení). Liší se jen technika dekodéru (Mac C++ a framework, Windows C# a COM DLL) a ukončení po časovém limitu --info (Windows ukončí strom procesů, Mac proces braw-decode — ten potomky nemá).`

- [ ] **Step 5: Záznam a commit**

`docs/VYVOJ-MAC.md`: `- Úkol 31 — BRAW: test celé cesty se vzorkem (OFFLOAD_BRAW_SAMPLE: analýza s gamou, LUT podle gamy, tři náhledy, proxy 540p), README „Vzorek BRAW“, ROZDILY (BRAW na obou stejně)`; ověření se vzorkem
zapsat zvlášť, až proběhne (kamera, gama, výsledek).

Run: `git add -A mac/Tests mac/README.md docs && git commit -m "Mac: BRAW end-to-end sample test, README and ROZDILY"`
