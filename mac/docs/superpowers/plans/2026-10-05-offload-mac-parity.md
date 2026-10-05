# Offload pro Mac — dorovnání s Windows verzí: implementační plán

> **Pro vykonavatele (člověk nebo Claude Code na Macu):** postupujte úkol po úkolu, v uvedeném pořadí. U každého
> kroku s testem nejdřív spusťte test a ověřte, že **selže** (nebo se nesestaví), teprve pak implementujte.
> Kód v plánu je kompletní — kde se mění existující funkce, je uvedena celá nová podoba. Kde si plán u Apple API
> není jistý, říká to výslovně a dává příkaz k ověření; výsledek ověření zapište do commitu úkolu.

**Goal:** Dorovnat Mac verzi (Swift, `mac/`) s Windows verzí: tři snímky na klip, pravidla B1–B4, zrušení úloh,
české texty karty úlohy, vysunutí jen kopírované karty, GUI bez blokování na discích, HW diagnostika (USB topologie
přes IOKit, připojení disků přes DiskArbitration, doporučení, měření, řetěz a brzda při kopii, `offload ports`,
okno Hardware z menu ⌘U) — se shodnými výstupy na disku a novým XCTest targetem.

**Architecture:** Logika bez UI patří do `OffloadKit` (`Sources/OffloadKit/`, nové podsložky `Desktop/` a `Hardware/`
jako Windows `Offload.Core/Desktop` a `Offload.Core/Hardware`), aby šla testovat XCTestem. Platformní části
(IOKit, DiskArbitration, NSWorkspace) jsou tenké vrstvy v samostatných souborech (`IORegistry.swift`,
`MacUsbScanner.swift`, `MacDriveLinks.swift`, `QueueRules.swift`) nad čistými modely. SwiftUI aplikace
(`Sources/OffloadApp/`) jen napojuje modely, nikdy nečte disk na hlavním vlákně. CLI (`Sources/offload/main.swift`)
používá stejné modely.

**Tech Stack:** Swift 5.9 (swift-tools-version 5.9), SwiftUI + AppKit, macOS 13+, Foundation, IOKit, DiskArbitration,
WebKit (beze změny), swift-argument-parser (beze změny), XCTest, ffmpeg/ffprobe (Homebrew nebo `mac/Tools/bin`).

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

Pět chyb, které by uživatele kously nejvíc — a test, který je hlídá:

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

`docs/VYVOJ-MAC.md` oddíl „Naplánováno“: smazat odrážku „3 snímky na klip v reportu“.
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

`docs/ROZDILY.md` (tento úkol rozdíl zavírá — upravit hned, ne až v úkolu 18): řádky „Test USB portů na vyžádání
+ doporučení“ a „Propustnost článků HW při kopii“ Mac `✅ CLI + GUI` (řetěz připojení, % linky, úzké hrdlo na kartě
úlohy, okno Hardware ⌘U); v „Záměrných rozdílech“ smazat odrážku o HW diagnostice jen na Windows / jen CLI na Macu;
v „GUI navíc na Windows“ odebrat okno Hardware s kontrolou USB portů a vlastními názvy portů (je i na Macu, ⌘U).

Run: `git add -A mac/Sources docs && git commit -m "Mac: Hardware window on demand (Cmd+U), Hardware section, connection chain on the job card"`

---

### Task 18: Dokumentace, verze a ROZDILY

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
~~~

a do úvodu odkaz na návrh a plán: `Návrh dorovnání s Windows: docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`.

- [ ] **Step 4: `docs/ROZDILY.md`**
  - „Stav k …“ datum; tabulka Přehled: Mac verze `0.2.0`.
  - Funkce a stav: zkontrolovat, že úkoly 7, 16 a 17 už nastavily „Náhledy v reportu“, „Test USB portů…“ a „Propustnost článků HW…“ na Mac ✅ (jinak doplnit).
  - Technologie: nové řádky „USB topologie | IOKit (IORegistry) | cfgmgr32 + IOCTL hubů“, „Kde sedí svazek | DiskArbitration + IORegistry | IOCTL_STORAGE_QUERY_PROPERTY“, „Identita karty | UUID svazku | GUID svazku + sériové číslo FS“, „Nastavení“ Mac `UserDefaults` (stejné schéma vč. `portNames`).
  - Záměrné rozdíly: ověřit, že odrážka o HW diagnostice zmizela (úkol 17); „USB rychlosti a porty“ přepsat na obě platformy (Mac: schopnost 10 Gb/s z bcdUSB nepozná; bez párovacího klíče je USB 2 polovina portu „neznámý typ“); přidat „Thunderbolt: Mac hlásí externí PCIe/NVMe disky jako ‚Thunderbolt‘; v Kartách se zobrazí, pokud macOS hlásí svazek jako externí (ověřit `volumeIsInternal` u Thunderbolt disku: `diskutil info /Volumes/X` → Device Location); Windows je bere jako NVMe a nenabízí (Složka…)“; „Verze v reportu“: Mac `0.2.0`.
- [ ] **Step 5: `docs/FORMATY.md`** — tabulka JSON, řádek `version`: `` `0.2.0` (Mac), `0.1.0-win` (Windows) ``.
- [ ] **Step 6: `docs/VYVOJ-MAC.md`** — oddíl „Naplánováno“: smazat odrážku „Totéž chování jako ve Windows verzi“ (hotovo), přidat „LUTy Log → Rec.709 (plán 5, obě platformy)“; do průběhu: `- Úkol 18 — verze 0.2.0, README (testy, Hardware), ROZDILY a FORMATY podle stavu po dorovnání`. Nadpis průběhu přejmenovat na `## <datum> — verze 0.2.0: dorovnání s Windows (plán mac-parity)`.
- [ ] **Step 7: Závěrečné ověření**

Run: `swift build -c release && swift test && scripts/build-app.sh`
Expected: vše zelené; `build/Offload.app/Contents/Info.plist` má `CFBundleShortVersionString` `0.2.0`; `build/Offload.app/Contents/Resources/bin/offload --version` → `0.2.0`.

Křížové ověření formátu (s Windows PC nebo kopií výstupu z něj): `offload verify <karta zkopírovaná Windows verzí>` → `OK`; Windows `bin\offload.exe verify <karta zkopírovaná Macem>` → `OK`; `ascmhl verify <karta>` (pip `ascmhl`) → bez chyb.

- [ ] **Step 8: Commit**

Run: `git add -A mac docs && git commit -m "Mac 0.2.0: docs, ROZDILY and FORMATY after the Windows parity work"`
