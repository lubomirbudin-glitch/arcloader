# Offload pro Mac — dorovnání 2 (Windows plány 6–7): implementační plán

> **Pro vykonavatele (člověk nebo Claude Code na Macu):** tento plán navazuje na
> [`2026-10-05-offload-mac-parity.md`](2026-10-05-offload-mac-parity.md) (úkoly 1–31, verze 0.2.0) a **začíná až po něm**.
> Postupujte úkol po úkolu v pořadí 32 → 70 (tento soubor 32–55, pokračování
> [`2026-10-08-offload-mac-parity-3.md`](2026-10-08-offload-mac-parity-3.md) 56–70). U každého kroku s testem nejdřív
> spusťte test a ověřte, že **selže** (nebo se nesestaví), teprve pak implementujte. Kde plán u Apple API není jistý, říká
> to výslovně a dává příkaz k ověření (spec D.27); výsledek ověření zapište do commitu úkolu.

**Goal:** Dorovnat Mac verzi s Windows 0.6.0 tam, kde jde o bezpečnost dat a sdílený formát na disku: historie karet,
doplnění nových klipů jako generace ASC MHL N, ověření zálohy proti všem generacím, kontrola místa, české hlášky (Windows
plán 6), denní report, seznamy klipů CSV/ALE, proxy pro přelinkování, MD5 navíc k xxh64 (plán 7) — a v souboru `-3`
projekty, pravidla LUTů, oznámení, aktualizace a podpis (plán 8), pozastavení, opravu „Zkopírovat znovu“ a nový vzhled
(plán 9), dodatky 0.5.x a Verify-in-Proxy s dvěma stavy karty (plán 10).

**Architecture:** Jako první plán: logika bez UI v `OffloadKit` (nové podsložky `History/`, `Append/`, `Verification/`,
`Space/`, `Outputs/`, `Notify/`, `Updates/`, `VerifyInProxy/`), testovaná XCTestem; platformní vrstvy (čtení boot sektoru,
`flock`, `mincore`, `UNUserNotificationCenter`, sockety) v malých samostatných souborech nad čistými funkcemi. **Pravdou
o záloze je vždy obsah cílové složky** (reporty všech generací, ASC MHL, soubory na disku) — historie jen najde složky
(spec D.22.1). Kód se řídí formátem a pravidly (spec D.22, `docs/FORMATY.md`), ne jmény tříd Windows verze.

**Tech Stack:** Swift 5.9, SwiftUI + AppKit, macOS 13+, Foundation, CryptoKit (`Insecure.MD5`, `Insecure.SHA1`, `SHA512`),
Compression (raw DEFLATE pro gzip), xxHash 0.8.3 z `Sources/CXXHash` (XXH64, XXH3-64, XXH3-128 — už v repu), IOKit
a DiskArbitration (už linkované od úkolu 14), Darwin (`flock`, `mincore`, `renamex_np`, `fcntl`), XCTest. Žádné nové SwiftPM
závislosti.

**Spec:** [`mac/docs/superpowers/specs/2026-10-05-offload-mac-parity-design.md`](../specs/2026-10-05-offload-mac-parity-design.md),
oddíl **„Doplněk 3“ (D.21–D.28)**. Windows reference (jen ke čtení, ne k překladu 1:1):
[`windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md`](../../../../windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md),
[`windows/docs/superpowers/specs/2026-10-08-offload-verify-in-proxy-design.md`](../../../../windows/docs/superpowers/specs/2026-10-08-offload-verify-in-proxy-design.md),
[`docs/ui-navrh/README.md`](../../../../docs/ui-navrh/README.md), [`docs/FORMATY.md`](../../../../docs/FORMATY.md).

## Global Constraints

- Platí celé „Global Constraints“ prvního plánu (macOS 13+, Swift 5.9, žádné nové závislosti, na kartu se nikdy
  nezapisuje, UI nikdy nečeká na disk ani síť, příkazy ze složky `mac/`, commit po každém úkolu, řádek do
  `docs/VYVOJ-MAC.md`).
- **Formát na disku je smlouva** (`docs/FORMATY.md`): co Mac zapíše, musí přečíst Windows 0.6.0 a naopak. Nové klíče jsou
  jen **přidané a volitelné**; čtení toleruje chybějící klíče (Mac 0.1.0/0.2.0, Windows 0.1.0–0.6.0) i neznámé klíče.
  Kde se tento plán od FORMATY odchýlí (nový klíč, Mac-only tvar), úkol upraví FORMATY ve stejném commitu.
- **Nikdy nic nepřepsat na cíli** kromě souborů, které FORMATY výslovně dovoluje přepsat: `<karta>.xxh64`, `<karta>.md5`
  (jen prodloužit, dřívější bajty zůstanou), `ascmhl/ascmhl_chain.xml` (dřívější záznamy stejné, jeden přibude),
  odvozené soubory (`_clips.csv/.ale`, denní report, HTML/JSON/TXT **téže** generace, kterou úloha právě píše a obnovuje)
  a reporty generace, která ještě nebyla potvrzená (`verifyPending`, spec D.22.5). Dřívější reporty a generace MHL se
  nikdy nemění; datové soubory a proxy se nikdy nepřepíšou (přejmenování bez nahrazení).
- **„Ověřeno“ má jednu definici** (spec D.22.6, úkol 38 `CardRecord.readBack`) — používá ji historie, denní report,
  doplnění, oprava, ověření zálohy i karta úlohy. Žádné jiné místo si „ověřeno“ nepočítá samo.
- **Zelená, zvuk úspěchu, oznámení „hotovo“ a CLI kód 0 po kopii jen při „Lze formátovat“** (spec D.22.7, úkol 39).
- Texty pro uživatele česky a **doslova jako Windows** (citované v úkolech; zdroj textů je `windows/src/Offload.Core/Messages.cs`
  a soubory, které úkol jmenuje). Rozdíl Macu: cesty v hláškách s `/` (`_REPORT/offload_report.json`, `<cíl>/<datum>/<karta>`),
  „Finder“ místo „Průzkumník“. Logy, TXT report, chyby souborů v JSON (`size mismatch`, `HASH MISMATCH` …) a `--help` zůstávají
  anglicky.
- Zámky (spec D.23.4): jen `flock` na místních souborech v `~/Library/Application Support/Offload/locks/` (nebo
  `$OFFLOAD_SETTINGS_DIR/locks/`), nikdy zámkový soubor na cíli. Zámky Macu a pojmenované mutexy Windows o sobě nevědí.
- Testy nikdy nezapisují do skutečného `~/Library/Application Support/Offload`: `OFFLOAD_SETTINGS_DIR` nastaví `TestSupport`
  (úkol 37) na dočasnou složku pro celý běh.
- Verze: konec plánu 6 (úkol 50) **0.3.0**, plánu 7 (úkol 55) **0.4.0**; dál soubor `-3`.
- Kód v plánu je **celý tam, kde jde o data** (hashe, report, MHL, historie, zámky, „ověřeno“, doplnění, ověření zálohy,
  oprava, manifest, ověřovač bloků). U GUI a u částí, které jen napojují hotová jádra, plán dává rozhraní, pravidla, texty
  a klíčové úseky kódu; zbytek je běžné SwiftUI nad těmi rozhraními.

## Review Focus

Chyby, které by uživatele kously nejvíc — a test, který je hlídá:

1. **Doplnění nebo oprava přepíše dřívější report, generaci MHL, datový soubor nebo dobrou kopii.**
   → Úkol 41 `GenerationWriteTests.testAppendCreatesNumberedReportsNeverReplaces`, `testHashListKeepsEarlierBytes`;
   úkol 47 `AppendEngineTests.testOriginalReportsAndGeneration1ByteIdentical`, `testFileThatAppearsMeanwhileIsNotOverwritten`;
   soubor -3 úkol 62 `RepairEngineTests.testGoodCopyNeverWritten`, `testBadCopySetAsideNeverDeleted`.
2. **Záloha je hlášená jako ověřená, i když není** (report `verifyPending`/`verifyIncomplete`, „written“ ve zrušeném
   ověření, report z Windows s jinými cestami, disk pod jiným jménem).
   → Úkol 38 `CardRecordTests.testPendingOrIncompleteNeverVerified`, `testForeignPathsNeedEveryColumnVerified`,
   `testSerialMatchesWindowsReportOnSameDisk`; úkol 39 `SafeStatesTests.testGreenOnlyWhenSafeToFormat`.
3. **Doplnění do zálohy, jejíž cíle se rozešly** (jiný počet generací, report bez generace MHL, cizí soubor na cíli,
   jiná karta) nebo dvě doplnění najednou.
   → Úkol 46 `AppendPlannerTests.testDivergedGenerationsRefused`, `testReportsWithoutChainRefused`,
   `testUnknownFileOnDestinationIsConflict`, `testOtherCardRefused`; úkol 37 `FileLockTests.testSecondHolderRefusedAtOnce`.
4. **Report ověření rozbije `ascmhl verify`** (zapsaný do složky karty, jejíž MHL `_REPORT` neignoruje) nebo zapíše do `ascmhl/`.
   → Úkol 45 `VerifyReportTests.testForeignMhlPutsReportBesideTheCard`, `testNothingWrittenIntoAscmhl`.
5. **Historie se rozbije souběhem GUI a CLI nebo poškozeným souborem**, nebo karta po přechodu na letní čas (FAT) vypadá
   jako změněná.
   → Úkol 42 `HistoryStoreTests.testTwoWritersUnderTheLock`, `testCorruptFileSetAside`; úkol 36
   `FileTimesTests.testFatShiftByWholeHoursIsSame`, `testExfatShiftIsChange`.
6. **Kopie z Macu nejde ověřit na Windows nebo naopak** (xxh128 jiné endianity, jiné jméno generace, chybějící `.xxh64`).
   → Úkol 32 `HashingTests.testVectors`; úkol 40 `MHLTests.testGenerationFileShapeMatchesWindowsFixture`; ruční křížový
   test v úkolu 50.

---

## Plán 6 pro Mac — karty a bezpečnost dat (úkoly 32–50, verze 0.3.0)

Úkoly 32–41 jsou základ, na kterém stojí všechno další (hashe, report, hlášky, nastavení, identita svazku, zámky, sdílená
definice „ověřeno“, dva stavy karty, ASC MHL, zápis generace). Teprve pak historie (42–44), ověření zálohy (45), doplnění
(46–47), místo (48) a GUI (49).

### Task 32: Hash jádro — XXH3-128, XXH3-64 bloků, MD5 a registr hashů ASC MHL

**Files:**
- Create: `mac/Sources/OffloadKit/Hashing.swift`
- Modify: `mac/Sources/OffloadKit/Hash.swift` (`hashFile` vrací `FileHashes`)
- Modify: `mac/Sources/OffloadKit/MHL.swift` (`c4FromSHA512` vedle `c4`)
- Create: `mac/Tests/OffloadKitTests/HashingTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `XXH64Stream`, `hashFile(_:chunkSize:buffer:progress:isCancelled:)` (úkol 15), `MHL.c4(_:)`, `IO.openRead`, `IO.pread`.
- Produces:
  - `public struct HashKinds: OptionSet, Hashable { static let xxh64, md5, both; var names: [String]; static func from(names: [String]?) -> HashKinds; static func fromSetting(_ s: String?) -> HashKinds }`
  - `public struct FileHashes: Equatable { var xxh64: UInt64?; var md5: Data?; var xxh128: String?; var xxh64Hex: String?; var md5Hex: String? }`
  - `final class XXH128Stream { init(); update(_:_:); func hex() -> String }`
  - `enum BlockHash { static func xxh3(_ p: UnsafeRawPointer, _ n: Int) -> UInt64; static func hex(_ v: UInt64) -> String }`
  - `final class MultiHasher { init(kinds: HashKinds, blockSize: Int); func update(_ p: UnsafeRawPointer, _ n: Int); func finish() -> (hashes: FileHashes, blocks: [UInt64]) }`
  - `protocol RunningHash: AnyObject { func update(_ p: UnsafeRawPointer, _ n: Int); func value() -> String }`
  - `public enum HashAlgo { static let known: [String]; static func make(_ name: String) -> RunningHash?; static func equal(_ algorithm: String, _ a: String, _ b: String) -> Bool; static func label(_ name: String) -> String }`
  - `hashFile(…)` → `(hashes: FileHashes, size: Int64)` s parametrem `kinds: HashKinds = .xxh64` (stávající volání dál dostanou `.xxh64`)
  - `static func MHL.c4FromSHA512(_ digest: [UInt8]) -> String`

Pravidla (FORMATY „`offload_report.json`“, „ASC MHL v2“, „Report ověření“; spec D.22.8):

- `xxh64` = 16 malých hex (jako dosud). `xxh128` = XXH3-128 jako **32 malých hex v kanonické podobě** (horních 64 bitů
  první) — tak ho píše Windows i `ascmhl`. `md5` = 32 malých hex. Blokový hash = XXH3-64 (16 malých hex) každého bloku
  velikosti `blockSize` (poslední kratší, prázdný soubor žádný blok).
- MD5 **nikdy samo** — `HashKinds` vždy obsahuje `xxh64` (R16 Windows návrhu).
- `HashAlgo` umí čtení všech hashů ASC MHL, které Windows ověřuje: `xxh64`, `md5`, `sha1`, `xxh3` (XXH3-64), `xxh128`, `c4`.
  Hex se porovnává bez ohledu na velikost písmen, `c4` (base58) přesně.
- MD5 a SHA-1 přes **CryptoKit** `Insecure.MD5` / `Insecure.SHA1` (macOS 10.15+), ne `CC_MD5` z CommonCrypto — to je od
  macOS 10.15 označené jako zastaralé a hlásilo by varování překladu.

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/HashingTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class HashingTests: XCTestCase {
    private func run(_ data: Data, kinds: HashKinds, blockSize: Int = 4) -> (hashes: FileHashes, blocks: [UInt64]) {
        let h = MultiHasher(kinds: kinds, blockSize: blockSize)
        data.withUnsafeBytes { if data.count > 0 { h.update($0.baseAddress!, data.count) } }
        return h.finish()
    }

    // Review focus 6: the values Windows and ascmhl write. If one differs, check with `brew install xxhash`:
    // printf '' | xxhsum -H1 (xxh64), -H3 (xxh3), -H2 (xxh128); printf '' | md5.
    func testVectors() {
        let empty = run(Data(), kinds: .both)
        XCTAssertEqual(empty.hashes.xxh64Hex, "ef46db3751d8e999")
        XCTAssertEqual(empty.hashes.xxh128, "99aa06d3014798d86001c324468d497f")
        XCTAssertEqual(empty.hashes.md5Hex, "d41d8cd98f00b204e9800998ecf8427e")
        XCTAssertEqual(empty.blocks, [], "an empty file has no block")
        let abc = run(Data("abc".utf8), kinds: .both)
        XCTAssertEqual(abc.hashes.xxh64Hex, "44bc2cf5ad770999")
        XCTAssertEqual(abc.hashes.md5Hex, "900150983cd24fb0d6963f7d28e17f72")
        XCTAssertEqual(BlockHash.hex(BlockHash.xxh3(UnsafeRawPointer(bitPattern: 1)!, 0)), "2d06800538d394c2")
        let sha1 = HashAlgo.make("sha1")!
        XCTAssertEqual(sha1.value(), "da39a3ee5e6b4b0d3255bfef95601890afd80709")
    }

    func testXxh64OnlyHasNoMd5ButAlwaysXxh128() {
        let r = run(Data("abc".utf8), kinds: .xxh64)
        XCTAssertNil(r.hashes.md5)
        XCTAssertNotNil(r.hashes.xxh128, "xxh128 is computed for every file (FORMATY, Windows 0.6.0)")
    }

    func testBlocksSplitAtBlockSizeAndFeedOrderDoesNotMatter() {
        let data = Data((0..<10).map { UInt8($0) })
        let whole = run(data, kinds: .xxh64, blockSize: 4)
        XCTAssertEqual(whole.blocks.count, 3, "4 + 4 + 2 bytes")
        // The same bytes fed in odd pieces give the same blocks and hashes.
        let h = MultiHasher(kinds: .xxh64, blockSize: 4)
        data.withUnsafeBytes { p in
            h.update(p.baseAddress!, 3); h.update(p.baseAddress! + 3, 6); h.update(p.baseAddress! + 9, 1)
        }
        let pieces = h.finish()
        XCTAssertEqual(pieces.blocks, whole.blocks)
        XCTAssertEqual(pieces.hashes, whole.hashes)
        let third = data.subdata(in: 8..<10)
        XCTAssertEqual(third.withUnsafeBytes { BlockHash.xxh3($0.baseAddress!, 2) }, whole.blocks[2])
    }

    func testKindsNamesAndSettings() {
        XCTAssertEqual(HashKinds.xxh64.names, ["xxh64"])
        XCTAssertEqual(HashKinds.both.names, ["xxh64", "md5"])
        XCTAssertEqual(HashKinds.from(names: nil), .xxh64)
        XCTAssertEqual(HashKinds.from(names: ["xxh64", "md5"]), .both)
        XCTAssertEqual(HashKinds.from(names: ["md5"]), .both, "MD5 is never alone")
        XCTAssertEqual(HashKinds.fromSetting("both"), .both)
        XCTAssertEqual(HashKinds.fromSetting("sha256"), .xxh64, "a value from a newer version reads as xxh64")
    }

    func testHashAlgoRegistry() {
        XCTAssertEqual(Set(HashAlgo.known), ["xxh64", "md5", "sha1", "xxh3", "xxh128", "c4"])
        XCTAssertNil(HashAlgo.make("sha256"))
        XCTAssertTrue(HashAlgo.equal("xxh64", "EF46DB3751D8E999", "ef46db3751d8e999"))
        XCTAssertFalse(HashAlgo.equal("c4", "c4ABC", "c4abc"), "base58 is case sensitive")
        let c4 = HashAlgo.make("c4")!
        XCTAssertEqual(c4.value(), MHL.c4(Data()))
        XCTAssertEqual(c4.value().count, 90)
        XCTAssertEqual(HashAlgo.label("xxh128"), "xxHash128")
    }

    func testHashFileGivesAllKinds() throws {
        let dir = tempDir()
        let path = dir.sub("a.bin")
        let data = TestFS.writeRandom(path, size: 3 << 20, seed: 7)
        let buf = IO.allocateAligned(1 << 20)
        defer { free(buf) }
        let r = try hashFile(path, chunkSize: 1 << 20, buffer: buf, kinds: .both)
        XCTAssertEqual(r.size, Int64(data.count))
        XCTAssertEqual(r.hashes, run(data, kinds: .both).hashes)
    }
}
```

- [ ] **Step 2: Ověřit selhání**

Run: `swift test --filter HashingTests`
Expected: nesestaví se (`cannot find 'MultiHasher' in scope`).

- [ ] **Step 3: Implementace** — `mac/Sources/OffloadKit/Hashing.swift` (celý soubor):

```swift
import CXXHash
import CryptoKit
import Foundation

/// The checksums a copy computes (spec 3.5 of the Windows design, R16): xxh64 always, MD5 only on top of it.
public struct HashKinds: OptionSet, Hashable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue | 1 }   // xxh64 is never left out
    public static let xxh64 = HashKinds(rawValue: 1)
    public static let md5 = HashKinds(rawValue: 3)
    public static let both = HashKinds(rawValue: 3)

    /// Report `hashAlgorithms` / history `hashAlgorithms`: ["xxh64"] or ["xxh64", "md5"].
    public var names: [String] { contains(.md5) ? ["xxh64", "md5"] : ["xxh64"] }

    /// From a report's `hashAlgorithms` (missing = xxh64 only).
    public static func from(names: [String]?) -> HashKinds {
        (names ?? []).contains { $0.lowercased() == "md5" } ? .both : .xxh64
    }

    /// From settings `hash`: "both" = xxh64 + MD5; anything else (also a value of a newer version) = xxh64.
    public static func fromSetting(_ s: String?) -> HashKinds { s == "both" ? .both : .xxh64 }
}

/// What a file's hashes are, as the report and the MHL record them.
public struct FileHashes: Equatable {
    public var xxh64: UInt64?
    public var md5: Data?
    /// XXH3-128, 32 lower-case hex digits, high 64 bits first (canonical form).
    public var xxh128: String?

    public init(xxh64: UInt64? = nil, md5: Data? = nil, xxh128: String? = nil) {
        self.xxh64 = xxh64
        self.md5 = md5
        self.xxh128 = xxh128
    }

    public var xxh64Hex: String? { xxh64.map(hex64) }
    public var md5Hex: String? { md5.map(hexBytes) }
}

func hexBytes(_ d: Data) -> String { d.map { String(format: "%02x", $0) }.joined() }

func bytesFromHex(_ s: String) -> Data? {
    guard s.count % 2 == 0 else { return nil }
    var out = Data(capacity: s.count / 2)
    var i = s.startIndex
    while i < s.endIndex {
        let j = s.index(i, offsetBy: 2)
        guard let b = UInt8(s[i..<j], radix: 16) else { return nil }
        out.append(b)
        i = j
    }
    return out
}

/// Streaming XXH3-128 (ASC MHL "xxh128").
final class XXH128Stream {
    private let state: OpaquePointer

    init() {
        state = XXH3_createState()
        XXH3_128bits_reset(state)
    }

    deinit { XXH3_freeState(state) }

    func update(_ p: UnsafeRawPointer, _ n: Int) { XXH3_128bits_update(state, p, n) }

    func hex() -> String {
        let h = XXH3_128bits_digest(state)
        return String(format: "%016llx%016llx", h.high64, h.low64)
    }
}

/// XXH3-64 of one block (the block manifest, verify-in-proxy).
enum BlockHash {
    static func xxh3(_ p: UnsafeRawPointer, _ n: Int) -> UInt64 { XXH3_64bits(p, n) }
    static func hex(_ v: UInt64) -> String { String(format: "%016llx", v) }
}

/// Every hash of one file in one pass: xxh64, xxh128, MD5 (when asked) and the XXH3-64 of every `blockSize` block.
/// Pieces may be fed in any sizes, in order.
final class MultiHasher {
    private let kinds: HashKinds
    private let blockSize: Int
    private let x64 = XXH64Stream()
    private let x128 = XXH128Stream()
    private var md5: Insecure.MD5?
    private var block: UnsafeMutableRawPointer
    private var inBlock = 0
    private(set) var blocks: [UInt64] = []

    init(kinds: HashKinds, blockSize: Int) {
        precondition(blockSize > 0)
        self.kinds = kinds
        self.blockSize = blockSize
        block = UnsafeMutableRawPointer.allocate(byteCount: blockSize, alignment: 16)
        if kinds.contains(.md5) { md5 = Insecure.MD5() }
    }

    deinit { block.deallocate() }

    func update(_ p: UnsafeRawPointer, _ n: Int) {
        guard n > 0 else { return }
        x64.update(p, n)
        x128.update(p, n)
        md5?.update(bufferPointer: UnsafeRawBufferPointer(start: p, count: n))
        var done = 0
        while done < n {
            if inBlock == 0 && n - done >= blockSize {
                blocks.append(BlockHash.xxh3(p + done, blockSize))   // a whole block straight from the caller's buffer
                done += blockSize
                continue
            }
            let take = min(blockSize - inBlock, n - done)
            block.advanced(by: inBlock).copyMemory(from: p + done, byteCount: take)
            inBlock += take
            done += take
            if inBlock == blockSize {
                blocks.append(BlockHash.xxh3(block, blockSize))
                inBlock = 0
            }
        }
    }

    func finish() -> (hashes: FileHashes, blocks: [UInt64]) {
        if inBlock > 0 {
            blocks.append(BlockHash.xxh3(block, inBlock))
            inBlock = 0
        }
        let m = md5.map { Data($0.finalize()) }
        return (FileHashes(xxh64: x64.digest(), md5: m, xxh128: x128.hex()), blocks)
    }
}

/// One running hash of a file for "Ověřit zálohu" (every ASC MHL hash type Windows verifies, R9).
protocol RunningHash: AnyObject {
    func update(_ p: UnsafeRawPointer, _ n: Int)
    func value() -> String
}

public enum HashAlgo {
    public static let known = ["xxh64", "md5", "sha1", "xxh3", "xxh128", "c4"]

    final class X64: RunningHash {
        let s = XXH64Stream()
        func update(_ p: UnsafeRawPointer, _ n: Int) { s.update(p, n) }
        func value() -> String { hex64(s.digest()) }
    }

    final class X3: RunningHash {
        private let state: OpaquePointer = XXH3_createState()
        init() { XXH3_64bits_reset(state) }
        deinit { XXH3_freeState(state) }
        func update(_ p: UnsafeRawPointer, _ n: Int) { XXH3_64bits_update(state, p, n) }
        func value() -> String { BlockHash.hex(XXH3_64bits_digest(state)) }
    }

    final class X128: RunningHash {
        let s = XXH128Stream()
        func update(_ p: UnsafeRawPointer, _ n: Int) { s.update(p, n) }
        func value() -> String { s.hex() }
    }

    final class Kit<H: HashFunction>: RunningHash {
        var h = H()
        let finish: (H.Digest) -> String
        init(_ finish: @escaping (H.Digest) -> String) { self.finish = finish }
        func update(_ p: UnsafeRawPointer, _ n: Int) { h.update(bufferPointer: UnsafeRawBufferPointer(start: p, count: n)) }
        func value() -> String { finish(h.finalize()) }
    }

    static func make(_ name: String) -> RunningHash? {
        switch name.lowercased() {
        case "xxh64": return X64()
        case "xxh3": return X3()
        case "xxh128": return X128()
        case "md5": return Kit<Insecure.MD5> { hexBytes(Data($0)) }
        case "sha1": return Kit<Insecure.SHA1> { hexBytes(Data($0)) }
        case "c4": return Kit<SHA512> { MHL.c4FromSHA512(Array($0)) }
        default: return nil
        }
    }

    /// Hex compares in any case; a c4 id is base58, where case matters.
    static func equal(_ algorithm: String, _ a: String, _ b: String) -> Bool {
        algorithm.lowercased() == "c4" ? a == b : a.lowercased() == b.lowercased()
    }

    /// For people: "xxHash64", "MD5", "SHA-1", "xxHash3", "xxHash128", "C4".
    static func label(_ name: String) -> String {
        ["xxh64": "xxHash64", "md5": "MD5", "sha1": "SHA-1", "xxh3": "xxHash3", "xxh128": "xxHash128", "c4": "C4"][name.lowercased()] ?? name
    }
}
```

V `MHL.swift` rozdělit `c4` (výpočet base58 přesunout do nové funkce, `c4(_:)` ji volá):

```swift
    /// C4 ID: "c4" + base58(SHA-512), left-padded with "1" to 88 characters.
    static func c4(_ data: Data) -> String {
        var digest = [UInt8](repeating: 0, count: Int(CC_SHA512_DIGEST_LENGTH))
        data.withUnsafeBytes { _ = CC_SHA512($0.baseAddress, CC_LONG(data.count), &digest) }
        return c4FromSHA512(digest)
    }

    /// The C4 ID of data whose SHA-512 is `digest` (a file hashed block by block).
    static func c4FromSHA512(_ digest: [UInt8]) -> String {
        let alphabet = Array("123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz")
        var num = digest
        var out: [Character] = []
        while num.contains(where: { $0 != 0 }) {
            var rem = 0
            var next: [UInt8] = []
            for b in num {
                let acc = rem * 256 + Int(b)
                let q = acc / 58
                rem = acc % 58
                if !next.isEmpty || q != 0 { next.append(UInt8(q)) }
            }
            out.append(alphabet[rem])
            num = next
        }
        let s = String(out.reversed())
        return "c4" + String(repeating: "1", count: max(0, 88 - s.count)) + s
    }
```

`Hash.swift` — `hashFile` počítá přes `MultiHasher` (celá funkce; `isCancelled` z úkolu 15 zůstává):

```swift
/// Hashes a whole file straight from the media (no page cache) with every kind asked for.
func hashFile(_ path: String, chunkSize: Int, buffer: UnsafeMutableRawPointer, kinds: HashKinds = .xxh64,
              progress: ((Int) -> Void)? = nil, isCancelled: () -> Bool = { false }) throws -> (hashes: FileHashes, size: Int64) {
    let fd = try IO.openRead(path)
    defer { close(fd) }
    let h = MultiHasher(kinds: kinds, blockSize: chunkSize)
    var offset: Int64 = 0
    while true {
        if isCancelled() { throw CancellationError() }
        let n = try IO.pread(fd, buffer, chunkSize, offset, path: path)
        if n == 0 { break }
        h.update(buffer, n)
        offset += Int64(n)
        progress?(n)
    }
    return (h.finish().hashes, offset)
}
```

Stávající volání (`DestWriter.verify`, `Verifier`, `Bench`) upravit z `r.hash` na `r.hashes.xxh64!` — hodnota je pro
xxh64 vždy přítomná. (`DestWriter.verify` přestaví úkol 51 na porovnání všech druhů.)

- [ ] **Step 4: Testy projdou**

Run: `swift test --filter HashingTests` → `Executed 6 tests, with 0 failures`; pak `swift test` (vše zelené).
Kdyby `testVectors` u XXH3 nesouhlasil, ověřit hodnoty `xxhsum` (viz komentář) a zapsat do commitu; **neupravovat
očekávanou hodnotu bez důkazu** — XXH3-128 musí být bajtově stejné jako na Windows.

- [ ] **Step 5: Záznam a commit**

`docs/VYVOJ-MAC.md` (nový oddíl `## <datum> — dorovnání 2 (plány 6–10), průběh`, nejnovější nahoře):
`- Úkol 32 — hash jádro: XXH3-128 (kanonický hex), XXH3-64 bloků, MD5 přes CryptoKit, registr hashů ASC MHL (xxh64, md5, sha1, xxh3, xxh128, c4)`

Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: hash core — XXH3-128, block XXH3-64, MD5, ASC MHL hash registry"`

---

### Task 33: Report — sdílené klíče Windows 0.2–0.6, jména generací, zápis „nový“ a „nahradit“

**Files:**
- Modify: `mac/Sources/OffloadKit/Report.swift` (struktury `OffloadReport`, `Destination`, `FileEntry`, nové `SetAsideCopy`, jména souborů, `summary`)
- Modify: `mac/Sources/OffloadKit/IO.swift` (`writeFileNew`, `FileExists`)
- Create: `mac/Tests/OffloadKitTests/ReportKeysTests.swift`, `mac/Tests/OffloadKitTests/Fixtures/windows-0.6.0-report.json`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `OffloadReport.isGood`, `jsonData()` (úkol 3), `IO.writeFileDurably`, `ProxyCopy.renameNoReplace` (úkol 20).
- Produces:
  - `OffloadReport` — všechna pole `var`; nové volitelné klíče `generation`, `kind`, `project`, `hashAlgorithms`, `verifyPending`,
    `verifyIncomplete`, `safeToFormatSeconds`, `verifyNotes`, `copyPausedSeconds`, `verifyPausedSeconds`, `setAside`, `proxyEncoder` (jen čtení)
  - `Destination`: `unbuffered`, `verifyMethod`, `verifyNote`, `volumeSerial` (volitelné); `FileEntry`: `xxh128`, `md5`, `rewritten`
  - `public struct SetAsideCopy: Codable, Equatable { destination: Int; path: String; movedTo: String }`
  - `static func OffloadReport.jsonName(_ g: Int) / textName(_ g: Int) / htmlName(_ card: String, _ g: Int) / pdfName(_ card: String, _ g: Int) -> String`
  - `var OffloadReport.generationNumber: Int`, `isRepair: Bool`, `algorithms: [String]`, `pausedSeconds: Double`, `safeToFormat: Bool`
  - `static func OffloadReport.read(_ path: String) throws -> OffloadReport` (tolerantní dekódování, data ISO 8601)
  - `struct FileExists: Error`; `static func IO.writeFileNew(_ path: String, _ data: Data) throws` (nikdy nenahradí; `FileExists`, když cíl existuje)

Pravidla (FORMATY „`offload_report.json`“):

- Klíče seřazené, `null` vlastnosti se **vynechávají** (syntetizovaný `Codable` zapisuje volitelné přes `encodeIfPresent`),
  `null` uvnitř polí (`errors`) zůstává. Mac **nikdy nezapisuje** `unbuffered` ani `proxyEncoder` (jen je čte z Windows).
- `generation` jen u generace ≥ 2; `kind` jen `"repair"`; `hashAlgorithms` jen když není jen `xxh64`; `hashAlgorithm` je vždy `"xxh64"`.
- `safeToFormat` (report) = `success && verified && verifyPending != true && verifyIncomplete != true` — report, jehož ověření
  neskončilo nebo jehož výsledek není zapsaný všude, **nikdy** není „lze formátovat“ (spec D.22.5).
- Jména generace: 1 = `offload_report.json`, `offload_report.txt`, `<karta>_report.html`, `<karta>_report.pdf`; N ≥ 2 = `…_NNNN`
  (4 číslice) před příponou.
- `writeFileNew`: soubor vznikne celý (dočasné jméno, `F_FULLFSYNC`), pak přejmenování **bez nahrazení**
  (`ProxyCopy.renameNoReplace` z úkolu 20 — `renamex_np(RENAME_EXCL)`, na FS bez podpory kontrola existence těsně před
  `rename`, spec D.23.9). Existující cíl = `FileExists`, dočasný soubor se smaže.

- [ ] **Step 1: Fixture z Windows 0.6.0** — `mac/Tests/OffloadKitTests/Fixtures/windows-0.6.0-report.json` (report doplnění
  generace 2 s ověřením během proxy, MD5, pauzou a jedním odloženým souborem — všechny klíče, které Mac musí přečíst):

```json
{
  "cardName": "A001",
  "clips": [],
  "copyFinished": "2026-10-08T08:00:10Z",
  "copyPausedSeconds": 12.5,
  "date": "2026-10-08",
  "destinations": [
    {"cardPath": "D:\\Zaloha\\2026-10-08\\A001", "filesFailed": 0, "filesOK": 1, "filesystem": "NTFS",
     "root": "D:\\Zaloha", "unbuffered": true, "verifyMethod": "verify-in-proxy", "volumeSerial": "1A2B-3C4D"},
    {"cardPath": "E:\\B\\2026-10-08\\A001", "filesFailed": 0, "filesOK": 1, "filesystem": "exFAT",
     "root": "E:\\B", "verifyMethod": "read-back", "verifyNote": "síťový disk", "volumeSerial": "5E6F-7A8B"}
  ],
  "files": [
    {"destinations": ["verified", "verified"], "errors": [null, null], "md5": "900150983cd24fb0d6963f7d28e17f72",
     "modified": "2026-10-08T07:59:00Z", "path": "CLIP/A001C002.mov", "rewritten": [true, false], "size": 3,
     "xxh128": "06b05ab6733a618578af5f94892f3950", "xxh64": "44bc2cf5ad770999"}
  ],
  "finished": "2026-10-08T08:01:00Z",
  "generation": 2,
  "hashAlgorithm": "xxh64",
  "hashAlgorithms": ["xxh64", "md5"],
  "kind": "repair",
  "project": "Film X",
  "proxyEncoder": "h264_nvenc",
  "safeToFormatSeconds": 48.5,
  "setAside": [{"destination": 0, "movedTo": "_REPORT/neshoda_0002/CLIP/A001C002.mov", "path": "CLIP/A001C002.mov"}],
  "skipped": [],
  "source": "F:\\",
  "sourceFilesystem": "exFAT",
  "started": "2026-10-08T08:00:00Z",
  "tool": "offload",
  "totalBytes": 3,
  "totalFiles": 1,
  "verified": true,
  "verifyNotes": ["blok 3 souboru CLIP/A001C002.mov se při opakovaném čtení lišil"],
  "verifyPausedSeconds": 0.5,
  "version": "0.6.0-win"
}
```

(Hodnota `xxh128` je jen tvar — test ji neporovnává s výpočtem.)

- [ ] **Step 2: Padající testy** — `mac/Tests/OffloadKitTests/ReportKeysTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class ReportKeysTests: XCTestCase {
    static var fixture: String {
        URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("Fixtures/windows-0.6.0-report.json").path
    }

    func testReadsEveryWindows060Key() throws {
        let r = try OffloadReport.read(Self.fixture)
        XCTAssertEqual(r.generationNumber, 2)
        XCTAssertTrue(r.isRepair)
        XCTAssertEqual(r.project, "Film X")
        XCTAssertEqual(r.algorithms, ["xxh64", "md5"])
        XCTAssertEqual(r.destinations[0].verifyMethod, "verify-in-proxy")
        XCTAssertEqual(r.destinations[1].volumeSerial, "5E6F-7A8B")
        XCTAssertEqual(r.files[0].md5, "900150983cd24fb0d6963f7d28e17f72")
        XCTAssertEqual(r.files[0].rewritten, [true, false])
        XCTAssertEqual(r.setAside?.first?.movedTo, "_REPORT/neshoda_0002/CLIP/A001C002.mov")
        XCTAssertEqual(r.pausedSeconds, 13.0, accuracy: 0.001)
        XCTAssertTrue(r.safeToFormat)
    }

    func testPendingOrIncompleteIsNeverSafeToFormat() {
        var r = Reports.sample()
        XCTAssertTrue(r.safeToFormat)
        r.verifyPending = true
        XCTAssertFalse(r.safeToFormat)
        r.verifyPending = nil
        r.verifyIncomplete = true
        XCTAssertFalse(r.safeToFormat)
    }

    func testMacNeverWritesWindowsOnlyKeysAndOmitsNils() throws {
        var r = try OffloadReport.read(Self.fixture)
        r.version = OffloadVersion.string
        let obj = try XCTUnwrap(JSONSerialization.jsonObject(with: try r.jsonData()) as? [String: Any])
        XCTAssertNil(obj["proxyEncoder"], "the Mac never writes the encoder (it is fixed)")
        let d0 = try XCTUnwrap((obj["destinations"] as? [[String: Any]])?.first)
        XCTAssertNil(d0["unbuffered"], "Windows only")
        let plain = try XCTUnwrap(JSONSerialization.jsonObject(with: try Reports.sample().jsonData()) as? [String: Any])
        for key in ["generation", "kind", "project", "hashAlgorithms", "verifyPending", "verifyIncomplete", "safeToFormatSeconds",
                    "verifyNotes", "copyPausedSeconds", "verifyPausedSeconds", "setAside"] {
            XCTAssertNil(plain[key], "\(key) is absent when not set")
        }
        XCTAssertEqual(plain["hashAlgorithm"] as? String, "xxh64")
    }

    func testFileNamesPerGeneration() {
        XCTAssertEqual(OffloadReport.jsonName(1), "offload_report.json")
        XCTAssertEqual(OffloadReport.textName(1), "offload_report.txt")
        XCTAssertEqual(OffloadReport.htmlName("A001", 1), "A001_report.html")
        XCTAssertEqual(OffloadReport.jsonName(2), "offload_report_0002.json")
        XCTAssertEqual(OffloadReport.pdfName("A001", 12), "A001_report_0012.pdf")
    }

    func testWriteFileNewNeverReplaces() throws {
        let dir = tempDir()
        let p = dir.sub("x.json")
        try IO.writeFileNew(p, Data("one".utf8))
        XCTAssertThrowsError(try IO.writeFileNew(p, Data("two".utf8))) { XCTAssertTrue($0 is FileExists) }
        XCTAssertEqual(try String(contentsOfFile: p, encoding: .utf8), "one")
        XCTAssertEqual(TestFS.files(in: dir.path), ["x.json"], "no temporary file left behind")
    }

    func testSummaryShowsPendingHashesAndPause() throws {
        var r = try OffloadReport.read(Self.fixture)
        XCTAssertTrue(r.summary.contains("Hash:        xxh64+md5"))
        XCTAssertTrue(r.summary.contains("Paused:      13.0 s (not in the speeds)"))
        r.verified = false
        r.verifyPending = true
        XCTAssertTrue(r.summary.contains("Verify:      PENDING (with the proxies)"))
    }
}
```

- [ ] **Step 3: Ověřit selhání** — Run: `swift test --filter ReportKeysTests` → nesestaví se (`has no member 'read'`).

- [ ] **Step 4: Implementace** — v `Report.swift` nahradit hlavičku struktury `OffloadReport` až po `build(...)` (vlastnosti
a vnořené typy; `build`, `hashList`, `write` zůstávají a upraví je úkoly 41 a 51):

```swift
public struct SetAsideCopy: Codable, Equatable {
    /// Index of the destination in `destinations`.
    public var destination: Int
    /// Path on the card ("/").
    public var path: String
    /// "_REPORT/neshoda_NNNN/<path>" relative to the card folder.
    public var movedTo: String
}

public struct OffloadReport: Codable {
    public struct Destination: Codable {
        public var root: String
        public var cardPath: String
        public var filesystem: String
        public var filesOK: Int
        public var filesFailed: Int
        /// Windows only (`false` = buffered I/O); the Mac reads it and never writes it.
        public var unbuffered: Bool?
        /// "verify-in-proxy" or "read-back"; absent without verification.
        public var verifyMethod: String?
        /// Czech: why this destination was read back although verify-in-proxy ran.
        public var verifyNote: String?
        /// "1A2B-3C4D" (FAT/exFAT/NTFS) or "uuid:<volume UUID>" (Mac, other file systems) — spec D.23.2.
        public var volumeSerial: String?

        public init(root: String, cardPath: String, filesystem: String, filesOK: Int, filesFailed: Int) {
            self.root = root; self.cardPath = cardPath; self.filesystem = filesystem
            self.filesOK = filesOK; self.filesFailed = filesFailed
        }
    }

    public struct FileEntry: Codable {
        public var path: String
        public var size: Int64
        public var modified: Date
        public var xxh64: String?
        /// XXH3-128, 32 lower-case hex.
        public var xxh128: String?
        /// Only with MD5 (`hashAlgorithms` contains "md5").
        public var md5: String?
        public var sourceError: String?
        public var destinations: [String]
        public var errors: [String?]
        /// Repair only: per destination true = a new copy, false = a good copy only read back.
        public var rewritten: [Bool]?

        public init(path: String, size: Int64, modified: Date, xxh64: String?, xxh128: String? = nil, md5: String? = nil,
                    sourceError: String?, destinations: [String], errors: [String?], rewritten: [Bool]? = nil) {
            self.path = path; self.size = size; self.modified = modified; self.xxh64 = xxh64; self.xxh128 = xxh128
            self.md5 = md5; self.sourceError = sourceError; self.destinations = destinations; self.errors = errors
            self.rewritten = rewritten
        }
    }

    public var tool = "offload"
    public var version = OffloadVersion.string
    public var hashAlgorithm = "xxh64"
    /// Only when not just xxh64: ["xxh64", "md5"].
    public var hashAlgorithms: [String]?
    /// ASC MHL generation of an append or a repair (`offload_report_NNNN.json`); absent = 1.
    public var generation: Int?
    /// "repair" for "Zkopírovat znovu"; absent otherwise.
    public var kind: String?
    public var project: String?
    public var cardName: String
    public var date: String
    public var source: String
    public var sourceFilesystem: String
    public var started: Date
    public var copyFinished: Date
    public var finished: Date
    public var verified: Bool
    /// The copy is done and verifies with the proxies; no ASC MHL generation yet (spec D.22.5).
    public var verifyPending: Bool?
    /// The verification result is not written into every card folder: never "verified" until a resume rewrites it.
    public var verifyIncomplete: Bool?
    public var safeToFormatSeconds: Double?
    public var verifyNotes: [String]?
    public var copyPausedSeconds: Double?
    public var verifyPausedSeconds: Double?
    public var setAside: [SetAsideCopy]?
    public var totalFiles: Int
    public var totalBytes: Int64
    public var destinations: [Destination]
    public var files: [FileEntry]
    public var skipped: [String]
    public var clips: [ClipEntry] = []
    public var proxy: ProxySettings?
    /// Windows only; read, never written (its value is not carried over by the Mac).
    public var proxyEncoder: String?

    public init(cardName: String, date: String, source: String, sourceFilesystem: String, started: Date, copyFinished: Date,
                finished: Date, verified: Bool, totalFiles: Int, totalBytes: Int64, destinations: [Destination],
                files: [FileEntry], skipped: [String]) {
        self.cardName = cardName; self.date = date; self.source = source; self.sourceFilesystem = sourceFilesystem
        self.started = started; self.copyFinished = copyFinished; self.finished = finished; self.verified = verified
        self.totalFiles = totalFiles; self.totalBytes = totalBytes; self.destinations = destinations; self.files = files
        self.skipped = skipped
    }

    enum CodingKeys: String, CodingKey {
        case tool, version, hashAlgorithm, hashAlgorithms, generation, kind, project, cardName, date, source, sourceFilesystem
        case started, copyFinished, finished, verified, verifyPending, verifyIncomplete, safeToFormatSeconds, verifyNotes
        case copyPausedSeconds, verifyPausedSeconds, setAside, totalFiles, totalBytes, destinations, files, skipped, clips, proxy
        case proxyEncoder
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(tool, forKey: .tool); try c.encode(version, forKey: .version)
        try c.encode(hashAlgorithm, forKey: .hashAlgorithm)
        try c.encodeIfPresent(hashAlgorithms, forKey: .hashAlgorithms)
        try c.encodeIfPresent((generation ?? 1) > 1 ? generation : nil, forKey: .generation)
        try c.encodeIfPresent(kind, forKey: .kind); try c.encodeIfPresent(project, forKey: .project)
        try c.encode(cardName, forKey: .cardName); try c.encode(date, forKey: .date)
        try c.encode(source, forKey: .source); try c.encode(sourceFilesystem, forKey: .sourceFilesystem)
        try c.encode(started, forKey: .started); try c.encode(copyFinished, forKey: .copyFinished)
        try c.encode(finished, forKey: .finished); try c.encode(verified, forKey: .verified)
        try c.encodeIfPresent(verifyPending == true ? true : nil, forKey: .verifyPending)
        try c.encodeIfPresent(verifyIncomplete == true ? true : nil, forKey: .verifyIncomplete)
        try c.encodeIfPresent(safeToFormatSeconds, forKey: .safeToFormatSeconds)
        try c.encodeIfPresent(verifyNotes?.isEmpty == false ? verifyNotes : nil, forKey: .verifyNotes)
        try c.encodeIfPresent(copyPausedSeconds, forKey: .copyPausedSeconds)
        try c.encodeIfPresent(verifyPausedSeconds, forKey: .verifyPausedSeconds)
        try c.encodeIfPresent(setAside?.isEmpty == false ? setAside : nil, forKey: .setAside)
        try c.encode(totalFiles, forKey: .totalFiles); try c.encode(totalBytes, forKey: .totalBytes)
        try c.encode(destinations.map { d -> Destination in var x = d; x.unbuffered = nil; return x }, forKey: .destinations)
        try c.encode(files, forKey: .files); try c.encode(skipped, forKey: .skipped)
        try c.encode(clips, forKey: .clips); try c.encodeIfPresent(proxy, forKey: .proxy)
        // proxyEncoder: never written by the Mac.
    }

    /// Tolerant reading: every key added since 0.1.0 may be missing; `clips` missing or null (a report without analysis,
    /// an older Mac) is an empty list — the files are the record of the copy, the clips only describe them.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        tool = try c.decodeIfPresent(String.self, forKey: .tool) ?? "offload"
        version = try c.decodeIfPresent(String.self, forKey: .version) ?? "?"
        hashAlgorithm = try c.decodeIfPresent(String.self, forKey: .hashAlgorithm) ?? "xxh64"
        hashAlgorithms = try c.decodeIfPresent([String].self, forKey: .hashAlgorithms)
        generation = try c.decodeIfPresent(Int.self, forKey: .generation)
        kind = try c.decodeIfPresent(String.self, forKey: .kind)
        project = try c.decodeIfPresent(String.self, forKey: .project)
        cardName = try c.decode(String.self, forKey: .cardName)
        date = try c.decode(String.self, forKey: .date)
        source = try c.decode(String.self, forKey: .source)
        sourceFilesystem = try c.decodeIfPresent(String.self, forKey: .sourceFilesystem) ?? "?"
        started = try c.decode(Date.self, forKey: .started)
        copyFinished = try c.decodeIfPresent(Date.self, forKey: .copyFinished) ?? started
        finished = try c.decodeIfPresent(Date.self, forKey: .finished) ?? copyFinished
        verified = try c.decodeIfPresent(Bool.self, forKey: .verified) ?? false
        verifyPending = try c.decodeIfPresent(Bool.self, forKey: .verifyPending)
        verifyIncomplete = try c.decodeIfPresent(Bool.self, forKey: .verifyIncomplete)
        safeToFormatSeconds = try c.decodeIfPresent(Double.self, forKey: .safeToFormatSeconds)
        verifyNotes = try c.decodeIfPresent([String].self, forKey: .verifyNotes)
        copyPausedSeconds = try c.decodeIfPresent(Double.self, forKey: .copyPausedSeconds)
        verifyPausedSeconds = try c.decodeIfPresent(Double.self, forKey: .verifyPausedSeconds)
        setAside = try c.decodeIfPresent([SetAsideCopy].self, forKey: .setAside)
        destinations = try c.decode([Destination].self, forKey: .destinations)
        files = try c.decode([FileEntry].self, forKey: .files)
        totalFiles = try c.decodeIfPresent(Int.self, forKey: .totalFiles) ?? files.count
        totalBytes = try c.decodeIfPresent(Int64.self, forKey: .totalBytes) ?? files.reduce(0) { $0 + $1.size }
        skipped = try c.decodeIfPresent([String].self, forKey: .skipped) ?? []
        clips = (try? c.decodeIfPresent([ClipEntry?].self, forKey: .clips))?.compactMap { $0 } ?? []
        proxy = try? c.decodeIfPresent(ProxySettings.self, forKey: .proxy)
        proxyEncoder = try c.decodeIfPresent(String.self, forKey: .proxyEncoder)
    }

    public static func jsonName(_ g: Int) -> String { g <= 1 ? "offload_report.json" : String(format: "offload_report_%04d.json", g) }
    public static func textName(_ g: Int) -> String { g <= 1 ? "offload_report.txt" : String(format: "offload_report_%04d.txt", g) }
    public static func htmlName(_ card: String, _ g: Int) -> String { g <= 1 ? "\(card)_report.html" : String(format: "%@_report_%04d.html", card, g) }
    public static func pdfName(_ card: String, _ g: Int) -> String { g <= 1 ? "\(card)_report.pdf" : String(format: "%@_report_%04d.pdf", card, g) }

    public var generationNumber: Int { generation ?? 1 }
    public var isRepair: Bool { kind == "repair" }
    public var algorithms: [String] { hashAlgorithms ?? ["xxh64"] }
    public var pausedSeconds: Double { (copyPausedSeconds ?? 0) + (verifyPausedSeconds ?? 0) }
    public var copySeconds: Double { max(0, copyFinished.timeIntervalSince(started) - (copyPausedSeconds ?? 0)) }
    public var verifySeconds: Double { max(0, finished.timeIntervalSince(copyFinished) - (verifyPausedSeconds ?? 0)) }
    public var failedFiles: [FileEntry] {
        files.filter { f in f.sourceError != nil || f.destinations.contains { !OffloadReport.isGood($0, verified: verified) } }
    }
    public var success: Bool { failedFiles.isEmpty }
    /// Spec D.22.7: every file on every destination verified, and the result written everywhere.
    public var safeToFormat: Bool { success && verified && verifyPending != true && verifyIncomplete != true }

    /// Reads a report written by any Offload (Mac 0.1+, Windows 0.1+): unknown keys ignored, dates ISO 8601.
    public static func read(_ path: String) throws -> OffloadReport {
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        return try dec.decode(OffloadReport.self, from: Data(contentsOf: URL(fileURLWithPath: path)))
    }
```

`safeToEject` reportu zaniká (nahrazuje ho `safeToFormat`; staré použití v `HTMLReport.swift`, `Job.swift` a CLI přejmenovat —
význam „všechno ověřené“ je stejný). `init(from:)` je vlastní (výše) — chybějící `clips` nebo `null` v nich nesmí znemožnit čtení zálohy; nečitelná sekce `proxy`
se přeskočí. `ClipEntry` dál syntetizovaný (klíč `content` přidá úkol 70).

`summary` — řádky jako Windows (TXT je sdílený formát), nahradit řádky `Copy:` až `Hash:`:

```swift
        Copy:        \(String(format: "%.1f s", copySeconds))  \(formatRate(totalBytes, copySeconds))
        Verify:      \(verified ? String(format: "%.1f s", verifySeconds) + "  " + formatRate(totalBytes, verifySeconds) : verifyPending == true ? "PENDING (with the proxies)" : "NOT VERIFIED")

        """
        if let t = safeToFormatSeconds { s += String(format: "Safe to format after: %.1f s\n", t) }
        if pausedSeconds > 0 { s += String(format: "Paused:      %.1f s (not in the speeds)\n", pausedSeconds) }
        s += "Hash:        \(algorithms.joined(separator: "+"))\n"
```

a řádek cíle: `Destination: \(d.cardPath) [\(d.filesystem)\(d.volumeSerial.map { " " + $0 } ?? "")]  ok \(d.filesOK)  failed \(d.filesFailed)\(d.verifyMethod.map { "  verify: " + $0 } ?? "")`.

`IO.swift`:

```swift
/// A file that must be created new is already there (an earlier generation's report, another job).
public struct FileExists: Error, CustomStringConvertible {
    public let path: String
    public var description: String { "file exists: \(path)" }
}

extension IO {
    /// Writes `data` as a new file: complete under a temporary name (F_FULLFSYNC), then renamed into place without
    /// replacing anything (spec D.23.9). Throws FileExists when `path` is taken; the temporary file is removed.
    static func writeFileNew(_ path: String, _ data: Data) throws {
        if FileManager.default.fileExists(atPath: path) { throw FileExists(path: path) }
        let tmp = (path as NSString).deletingLastPathComponent + "/." + (path as NSString).lastPathComponent + ".new.tmp"
        let fd = try openWrite(tmp)
        do {
            if !data.isEmpty { try data.withUnsafeBytes { try pwrite(fd, $0.baseAddress!, data.count, 0, path: tmp) } }
            try fullSync(fd, path: tmp)
            close(fd)
        } catch {
            close(fd); unlink(tmp)
            throw error
        }
        do {
            try ProxyCopy.renameNoReplace(tmp, path)
        } catch is ProxyCopy.Taken {
            unlink(tmp)
            throw FileExists(path: path)
        } catch {
            unlink(tmp)
            throw error
        }
    }
}
```

- [ ] **Step 5: Testy projdou** — Run: `swift test --filter ReportKeysTests` → 6 testů OK; `swift test` celé zelené (úkol 3
  `testJSONKeysAreTheSharedFormatSorted` platí beze změny — nové klíče bez hodnoty se nezapisují).

- [ ] **Step 6: Záznam a commit** — `docs/VYVOJ-MAC.md`: `- Úkol 33 — report čte i zapisuje sdílené klíče Windows 0.2–0.6
  (generace, oprava, projekt, MD5, xxh128, ověření během proxy, pauza, odložené kopie); Mac nikdy nepíše unbuffered
  ani proxyEncoder; zápis souboru „jen nový“ (renamex_np RENAME_EXCL); report s verifyPending/verifyIncomplete nikdy není
  „lze formátovat“`. Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: report reads and writes the shared Windows 0.2-0.6 keys, write-new files"`

---

### Task 34: České hlášky a kódy kontrol (`Msg`, `PreflightCode`), CLI „Chyba: “

**Files:**
- Create: `mac/Sources/OffloadKit/Messages.swift`
- Modify: `mac/Sources/OffloadKit/Engine.swift` (`OffloadError`, texty v `prepare`, `run`), `mac/Sources/OffloadKit/Verifier.swift`,
  `mac/Sources/OffloadKit/Bench.swift`, `mac/Sources/offload/main.swift` (chyby a hlášky CLI), `mac/Sources/OffloadKit/IO.swift` (`formatBytesCs`)
- Modify: `mac/Tests/OffloadKitTests/PreflightTests.swift` (očekávané texty a kódy)
- Create: `mac/Tests/OffloadKitTests/MessagesTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `OffloadError.preflight`, `isPreflight` (úkol 2), `Texts.plural` (úkol 8).
- Produces:
  - `public enum PreflightCode: String { other, chunkSize, bufferCount, date, sourceNotFound, noDestination, duplicateDestination, sourceIsOffload, sourceUnreadable, destinationNotFound, destinationNotWritable, destinationUnreadable, sameVolumeAsSource, sharedVolume, emptySource, noSpace, noHashRecord, noFilesToRead, appendUnavailable, appendNoReport, appendNoMhl, appendDiverged, appendConflict, appendMissingFile, appendFailedFile, appendOtherCard, appendMhlDamaged, appendBusy, nothingNew, repairNothing, repairConflict }`
  - `OffloadError.preflight(String, code: PreflightCode = .other)`, `var OffloadError.code: PreflightCode?`
  - `public enum Msg` — statické funkce a konstanty níže (doslova Windows `Messages.cs`, cesty s `/`)
  - `public func formatBytesCs(_ b: Int64) -> String` („3,2 GB“, „412 GB“, „850 MB“, „12 B“)

Pravidla: každá hláška, kterou uživatel uvidí z kontrol a validace (karta úlohy, CLI), je česky a **doslova** jako
Windows; kód (`PreflightCode`) rozlišuje případy v testech a v GUI, nikdy text. Na Macu chybí Windows-only kontroly
(`BadNames`, `Fat32`, kolize velikosti písmen — ROZDILY „Záměrné rozdíly“). Mac rozdíl: v cestách `/` místo `\`.
Logy, TXT report, chyby souborů v JSON a `--help` zůstávají anglicky.

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/MessagesTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class MessagesTests: XCTestCase {
    func testBytesCs() {
        XCTAssertEqual(formatBytesCs(12), "12 B")
        XCTAssertEqual(formatBytesCs(3_200_000_000), "3,2 GB")
        XCTAssertEqual(formatBytesCs(2_000_000_000), "2 GB")
        XCTAssertEqual(formatBytesCs(412_000_000_000), "412 GB")
        XCTAssertEqual(formatBytesCs(850_000_000), "850 MB")
    }

    func testPreflightTextsAreCzechWithCodes() throws {
        let dir = tempDir()
        let e = OffloadEngine()
        do {
            _ = try e.prepare(source: dir.sub("missing"), destRoots: [dir.path], cardName: nil, date: nil)
            XCTFail("must refuse")
        } catch let err as OffloadError {
            XCTAssertEqual(err.code, .sourceNotFound)
            XCTAssertEqual(err.description, "Zdroj nenalezen: \(dir.sub("missing"))")
        }
    }

    func testNoEnglishWordsInPreflightMessages() {
        let all = [Msg.chunkSize(1), Msg.bufferCount(1), Msg.date("x"), Msg.sourceNotFound("/a"), Msg.noDestination,
                   Msg.duplicateDestination("/a"), Msg.sourceIsOffload, Msg.sourceUnreadable("/a"), Msg.destinationNotFound("/a"),
                   Msg.destinationNotWritable("/a"), Msg.destinationUnreadable("/a"), Msg.sameVolumeAsSource("/a"),
                   Msg.sharedVolume("/a"), Msg.emptySource, Msg.noHashRecord("/a"), Msg.noFilesToRead("/a")]
        for m in all {
            for word in ["not found", "cannot", "must", "Destination", "Source"] {
                XCTAssertFalse(m.contains(word), "\(m) contains \(word)")
            }
        }
    }

    func testMacPathsUseSlash() {
        XCTAssertEqual(Msg.appendNoReport("/V/D/2026-10-06/A001"),
                       "V /V/D/2026-10-06/A001 chybí report dřívější zálohy (_REPORT/offload_report.json) — doplnit nejde.")
        XCTAssertEqual(Msg.appendNoMhl("/x"), "V /x chybí ASC MHL (ascmhl/ascmhl_chain.xml) — doplnit nejde.")
    }

    func testPluralsAsWindows() {
        XCTAssertEqual(Msg.unverifiedEarlier(1), "1 soubor z dřívější zálohy nebyl ověřen — doporučeno Ověřit zálohu.")
        XCTAssertEqual(Msg.unverifiedEarlier(3), "3 soubory z dřívější zálohy nebyly ověřeny — doporučeno Ověřit zálohu.")
        XCTAssertEqual(Msg.cardFilesNotInBackup(5),
                       "Na kartě je 5 souborů, které záloha nemá — kartu nevysouvejte, zálohu doplňte.")
    }
}
```

Run: `swift test --filter MessagesTests` → nesestaví se (`cannot find 'Msg'`).

- [ ] **Step 2: Implementace** — `mac/Sources/OffloadKit/Messages.swift` (celý soubor; texty doslova z
  `windows/src/Offload.Core/Messages.cs` ke dni 2026-10-08, cesty s `/`):

```swift
import Foundation

/// Which check stopped a job. Tests and the GUI tell the cases apart by the code, never by the text.
public enum PreflightCode: String {
    case other, chunkSize, bufferCount, date, sourceNotFound, noDestination, duplicateDestination, sourceIsOffload
    case sourceUnreadable, destinationNotFound, destinationNotWritable, destinationUnreadable, sameVolumeAsSource
    case sharedVolume, emptySource, noSpace, noHashRecord, noFilesToRead
    case appendUnavailable, appendNoReport, appendNoMhl, appendDiverged, appendConflict, appendMissingFile, appendFailedFile
    case appendOtherCard, appendMhlDamaged, appendBusy
    /// Not an error: the backup already has everything (the job ends "Hotovo").
    case nothingNew
    /// Not an error: a repair found every file good on every destination.
    case repairNothing
    case repairConflict
}

/// Czech text with the right plural: 1 / 2–4 / other.
func plural(_ n: Int, _ one: String, _ few: String, _ many: String) -> String { n == 1 ? one : (2...4).contains(n) ? few : many }

/// Every message a user sees from checks and validation — Czech, word for word as Windows (Messages.cs), paths with "/".
public enum Msg {
    public static func chunkSize(_ n: Int) -> String { "Velikost bloku musí být násobek 4096 B a nejvýš 1 GiB (zadáno \(n))." }
    public static func bufferCount(_ n: Int) -> String { "Počet bufferů musí být aspoň 2 (zadáno \(n))." }
    public static func date(_ d: String) -> String { "Datum musí být ve tvaru RRRR-MM-DD (zadáno „\(d)“)." }
    public static func sourceNotFound(_ p: String) -> String { "Zdroj nenalezen: \(p)" }
    public static let noDestination = "Není zadaný žádný cíl."
    public static func duplicateDestination(_ r: String) -> String { "Cíl je zadaný dvakrát: \(r)" }
    public static let sourceIsOffload =
        "Zdroj vypadá jako dřívější offload (má _REPORT, _PROXY nebo ascmhl) — kopírujte původní kartu, nebo tyto složky odeberte."
    public static func sourceUnreadable(_ p: String) -> String { "Zdroj nejde přečíst: \(p)" }
    public static func destinationNotFound(_ r: String) -> String { "Cíl nenalezen: \(r) — je disk připojený?" }
    public static func destinationNotWritable(_ r: String) -> String { "Na cíl nejde zapisovat: \(r)" }
    public static func destinationUnreadable(_ r: String) -> String { "Cíl nejde přečíst: \(r)" }
    public static func sameVolumeAsSource(_ r: String) -> String { "Cíl \(r) je na stejném disku jako zdroj." }
    public static func sharedVolume(_ r: String) -> String {
        "Dva cíle jsou na stejném disku (\(r)) — to není druhá kopie. Povolit jde volbou --allow-same-device."
    }
    public static let emptySource = "Na zdroji nejsou žádné soubory."
    public static func reportNotWritten(_ d: String) -> String { "Report se nepodařilo zapsat: \(d)" }
    public static func noHashRecord(_ dir: String) -> String { "Ve složce \(dir) není ASC MHL ani seznam součtů — není s čím porovnat." }
    public static func noFilesToRead(_ p: String) -> String { "V \(p) nejsou soubory ke čtení." }
    public static let cardVolumeUnreadable = "Karta se nedá přečíst."
    public static let cardInspectFailed = "Kartu se nepodařilo prověřit."
    public static let cardSwapped = "Karta se během čtení vyměnila."
    public static let cardNameAutoNone = "Návrh názvu nenalezen, použije se jmenovka svazku."
    public static func cardNameAuto(_ n: String) -> String { "Název karty: \(n) (podle klipů)" }
    public static func cardUnreadable(_ d: String) -> String { "Kartu nejde přečíst: \(d)" }
    /// Where a backup is checked (the new look: top bar; before it: menu Nástroje).
    public static let verifyHint = "Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify)"

    // Space (úkol 48 fills the parts)
    public static func noSpace(_ whereTo: String, total: Int64, parts: String, free: Int64) -> String {
        "Na cíli \(whereTo) není dost místa: potřeba \(formatBytesCs(total)) (\(parts)), volno \(formatBytesCs(free))."
    }
    public static func spaceLine(_ whereTo: String, total: Int64, parts: String, free: Int64) -> String {
        "Místo: \(whereTo) potřeba \(formatBytesCs(total)) (\(parts)), volno \(formatBytesCs(free))"
    }
    public static func lowSpace(_ whereTo: String, left: Int64) -> String {
        "Na cíli \(whereTo) zbude po offloadu jen \(formatBytesCs(left)) — rezerva pod 5 %, odhad proxy je přibližný."
    }
    public static func proxiesSkippedForSpace(_ whereTo: String, need: Int64, free: Int64) -> String {
        "Proxy se nevyrenderovaly: na \(whereTo) chybí místo (potřeba ~\(formatBytesCs(need)), volno \(formatBytesCs(free)))."
    }

    // Append
    public static func appendUnavailable(_ p: String) -> String { "Dřívější záloha není dostupná: \(p) — připojte disk, nebo udělejte celou kopii." }
    public static func appendNoReport(_ p: String) -> String { "V \(p) chybí report dřívější zálohy (_REPORT/offload_report.json) — doplnit nejde." }
    public static func appendBadReport(_ p: String, _ d: String) -> String { "Report dřívější zálohy v \(p) nejde přečíst (\(d)) — doplnit nejde." }
    public static func appendNoMhl(_ p: String) -> String { "V \(p) chybí ASC MHL (ascmhl/ascmhl_chain.xml) — doplnit nejde." }
    public static func appendDiverged(_ details: String) -> String {
        "Zálohy na cílech se liší (\(details)) — zkontrolujte je přes \(verifyHint); doplnit nejde."
    }
    public static func appendReportsMismatch(_ p: String, report: Int, mhl: Int) -> String {
        "V \(p) nesouhlasí reporty s ASC MHL (poslední report je generace \(report), poslední generace MHL \(mhl)) — dřívější doplnění se nedokončilo. "
            + "Zkontrolujte zálohu přes \(verifyHint) a složku opravte ručně; doplnit nejde."
    }
    public static func appendNotACardFolder(_ p: String) -> String { "\(p) není složka karty v záloze (<cíl>/<datum>/<karta>) — doplnit nejde." }
    public static func appendDifferentFolders(_ paths: [String]) -> String {
        "Složky dřívější zálohy se jmenují různě (\(paths.joined(separator: ", "))) — doplnit jde jen do složek se stejným datem a názvem karty."
    }
    public static func appendConflict(_ lines: [String], fullCopyFolder: String) -> String {
        "Doplnění zastaveno — nic se nezkopírovalo. \(lines.count) \(plural(lines.count, "soubor se neshoduje", "soubory se neshodují", "souborů se neshoduje")) "
            + "se zálohou:\n  " + lines.prefix(10).joined(separator: "\n  ") + (lines.count > 10 ? "\n  …" : "")
            + "\nUdělejte celou kopii (vznikne \(fullCopyFolder))."
    }
    public static func appendMissingFile(_ disk: String, _ path: String) -> String {
        "Na \(disk) chybí soubor z dřívější zálohy: \(path) — zkontrolujte zálohu přes \(verifyHint)"
    }
    public static func appendFailedFile(_ path: String, _ disk: String) -> String {
        "\(path) se při dřívější záloze na \(disk) nezkopíroval správně — doplnit nejde, udělejte celou kopii."
    }
    public static let appendOtherCard = "Na kartě není žádný soubor z dřívější zálohy — jde o jinou kartu? Udělejte celou kopii."
    public static func appendMhlDamaged(_ p: String) -> String { "Historie ASC MHL v \(p) je poškozená — zkontrolujte ji přes \(verifyHint); doplnit nejde." }
    public static func appendUnknownFile(_ disk: String, _ path: String) -> String { "na \(disk) je soubor \(path), který žádný report nezná (nedokončené doplnění?)" }
    public static func appendPartNameTaken(_ path: String, _ part: String) -> String { "\(path) (dočasné jméno kopie \(part) je jiný soubor zálohy nebo karty)" }
    public static func hashListNotHealed(_ p: String, _ e: String) -> String { "Seznam kontrolních součtů (.xxh64) v \(p) nejde doplnit o chybějící řádky: \(e)" }
    public static func appendBusy(_ p: String) -> String {
        "S touto zálohou právě pracuje jiná úloha — doplnění, oprava nebo ověření s proxy (\(p)) — počkejte, až skončí."
    }
    public static let nothingNew = "Nic nového — záloha je úplná."
    public static func unverifiedEarlier(_ n: Int) -> String {
        "\(n) \(plural(n, "soubor", "soubory", "souborů")) z dřívější zálohy \(plural(n, "nebyl ověřen", "nebyly ověřeny", "nebylo ověřeno")) — doporučeno Ověřit zálohu."
    }
    public static let cardNotInHistory = "Karta není v historii — zadejte složky zálohy volbou --into."

    // Repair (soubor -3, úkol 62)
    public static let repairNothing = "Nic k opravě — všechny soubory jsou na všech cílech v pořádku."
    public static func repairConflict(_ lines: [String]) -> String {
        "Oprava zastavena — nic se nezkopírovalo. \(lines.count) \(plural(lines.count, "soubor nejde opravit", "soubory nejde opravit", "souborů nejde opravit")):\n  "
            + lines.prefix(10).joined(separator: "\n  ") + (lines.count > 10 ? "\n  …" : "")
            + "\nKarta se od zálohy změnila nebo ve složce zálohy chybí soubor — zkontrolujte ji přes Ověřit zálohu, nebo udělejte celou kopii."
    }
    public static func repairNotOnCard(_ p: String) -> String { "\(p): na kartě už není" }
    public static func repairCardDiffers(_ p: String, _ algorithm: String, now: String, recorded: String) -> String {
        "\(p): na kartě je jiný obsah než při záloze (\(algorithm) \(now), v záloze \(recorded))"
    }
    public static func repairCardUnreadable(_ p: String, _ d: String) -> String { "\(p): na kartě nejde přečíst (\(d))" }
    public static let repairNeedsVerify = "Oprava se vždy ověřuje zpětným čtením — bez ověření ji spustit nejde."
    public static func mhlRepairComment(copied: Int, readBack: Int, source: String) -> String {
        "Offload oprava: \(copied) \(plural(copied, "soubor", "soubory", "souborů")) znovu z \(source)"
            + (readBack > 0 ? "; \(readBack) \(plural(readBack, "soubor jen přečten", "soubory jen přečteny", "souborů jen přečteno")) zpět" : "")
    }
    public static let repairOtherCard =
        "Ve čtečce není karta, ze které se záloha dělala (jiná karta, nebo žádná) — nic se nezkopírovalo. Vložte původní kartu a spusťte Zkopírovat znovu."
    public static func cardFilesNotInBackup(_ n: Int) -> String {
        "Na kartě \(plural(n, "je", "jsou", "je")) \(n) \(plural(n, "soubor, který", "soubory, které", "souborů, které")) záloha nemá — kartu nevysouvejte, zálohu doplňte."
    }
    public static func backupStillFailed(_ n: Int) -> String {
        "V záloze \(n) \(plural(n, "soubor stále nesouhlasí", "soubory stále nesouhlasí", "souborů stále nesouhlasí")) — kartu nevysouvejte, spusťte Zkopírovat znovu."
    }
    public static func backupStateUnreadable(_ d: String) -> String { "Stav zálohy nejde přečíst (\(d)) — nic se nezkontrolovalo, kartu zatím nevysouvejte." }
    public static func forRepair(_ appendRefusal: String) -> String {
        appendRefusal.replacingOccurrences(of: "doplnit nejde", with: "opravit nejde").replacingOccurrences(of: "doplnit jde jen", with: "opravit jde jen")
    }
    public static let cardChangedWhilePaused =
        "Během pozastavení se karta ve čtečce vyměnila nebo vytáhla — zbylé soubory se nezkopírovaly. Vložte původní kartu a zálohu doplňte."

    // CLI
    public static let cliError = "Chyba: "
    public static func unknownProxyCodec(_ x: String?) -> String {
        "Neznámý kodek proxy „\(x ?? "")“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ."
    }
    public static func lutNotFound(_ p: String) -> String { "LUT nenalezen: \(p)" }
    public static func unknownHash(_ x: String?) -> String { "Neznámý kontrolní součet „\(x ?? "")“ — použijte xxh64 nebo both (xxh64 + MD5)." }
    public static func proxyFolderNotFound(_ p: String) -> String { "Složka pro proxy nenalezena: \(p)" }
    public static let proxyFolderNoEffect = "Poznámka: --proxy-folder bez --proxy nic nedělá."
    public static let cancelling = "Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…"
    public static func notPositiveNumber(_ s: String?) -> String { "„\(s ?? "")“ není kladné číslo." }
    public static func unknownFallbackLut(_ x: String, ids: String) -> String { "Neznámý záložní LUT „\(x)“. Vestavěné: \(ids)" }
    public static func unknownLut(_ x: String, ids: String) -> String { "Neznámý LUT „\(x)“. Použijte auto, none, cestu k .cube nebo jedno z: \(ids)" }
    public static let noBuiltinLuts = "Vestavěné LUTy nenalezeny (chybí složka luts)."

    // Clip notes
    public static let noVerifiedCopy = "žádná ověřená kopie"
    public static func proxyRenamed(_ name: String) -> String { "Proxy přejmenována na \(name) (shoda jmen) — přelinkujte ji ručně." }
    public static let ffmpegNotFound = "ffmpeg nenalezen"
    public static let ffprobeNotFound = "ffprobe nenalezen"
    public static let unknownFormat = "neznámý formát"
    public static func noDecoder(_ f: String) -> String { "\(f): chybí dekodér" }
}
```

`IO.swift` — `formatBytesCs` (desetinné jednotky jako `formatBytes`, čárka, nejvýš jedno desetinné místo pod 10):

```swift
/// A size inside a Czech sentence: "3,2 GB", "2 GB", "412 GB", "850 MB", "12 B".
public func formatBytesCs(_ b: Int64) -> String {
    let units = ["B", "KB", "MB", "GB", "TB"]
    var v = Double(b)
    var i = 0
    while v >= 1000 && i < units.count - 1 { v /= 1000; i += 1 }
    if i == 0 { return "\(b) B" }
    let f = NumberFormatter()
    f.locale = Locale(identifier: "cs_CZ")
    f.minimumFractionDigits = 0
    f.maximumFractionDigits = v < 10 ? 1 : 0
    f.usesGroupingSeparator = false
    return (f.string(from: NSNumber(value: v)) ?? "\(v)") + " " + units[i]
}
```

`Engine.swift`:

```swift
public enum OffloadError: Error, CustomStringConvertible {
    case preflight(String, code: PreflightCode = .other)
    case failed(String)

    public var description: String {
        switch self {
        case .preflight(let m, _), .failed(let m): return m
        }
    }

    public var code: PreflightCode? {
        if case .preflight(_, let c) = self { return c }
        return nil
    }

    public var isPreflight: Bool { code != nil }
}
```

V `prepare` nahradit anglické texty: `.preflight(Msg.chunkSize(n), code: .chunkSize)`, `Msg.bufferCount`, `Msg.date`,
`Msg.sourceNotFound`, `Msg.noDestination`, `Msg.duplicateDestination`, `Msg.sourceIsOffload`, `Msg.sourceUnreadable`,
`Msg.destinationNotFound`, `Msg.destinationNotWritable`, `Msg.destinationUnreadable`, `Msg.sameVolumeAsSource`,
`Msg.sharedVolume`, `Msg.emptySource` s odpovídajícím kódem; kontrolu místa nahradí úkol 48 (do té doby
`Msg.noSpace(root, total: needed, parts: "karty \(formatBytesCs(needed))", free: free)`, kód `.noSpace`). V `run`:
`OffloadError.failed(Msg.reportNotWritten(…))`. `Verifier`: `Msg.noHashRecord(reportDir)` s kódem `.noHashRecord`;
`Bench`: `Msg.noFilesToRead`. Každé `case .preflight(let m)` v kódu a testech změnit na `case .preflight(let m, _)`.

`main.swift`: chyby vypisovat `FileHandle.standardError` jako `Msg.cliError + "\(error)"`; `ValidationError` texty
(`--chunk-mb`, `--proxy-codec`, LUT, složka pro proxy) přes `Msg` (`notPositiveNumber`, `unknownProxyCodec`, `lutNotFound`,
`unknownLut`, `unknownFallbackLut`, `proxyFolderNotFound`); poznámka `Msg.proxyFolderNoEffect`; Ctrl+C `Msg.cancelling`;
`offload luts` s prázdným katalogem `Msg.noBuiltinLuts`. `--help` zůstává anglicky. Chyby parseru `swift-argument-parser`
(neznámá volba…) zůstávají anglicky — knihovna nemá lokalizaci (rozdíl proti Windows, zapsat do ROZDILY).

- [ ] **Step 3: Upravit `PreflightTests`** — každé `XCTAssertEqual(error.description, "…anglicky…")` nahradit českým
  textem z `Msg` a doplnit `XCTAssertEqual(error.code, .<kód>)`. Run: `swift test` → zelené.

- [ ] **Step 4: Záznam a commit** — `docs/VYVOJ-MAC.md`: `- Úkol 34 — hlášky kontrol a CLI česky (Msg, PreflightCode),
  „Chyba: “; chyby parseru swift-argument-parser zůstávají anglicky`; `docs/ROZDILY.md`: řádek „Hlášky kontrol před kopií
  česky“ Mac ✅ (bez chyb parseru — knihovna je nepřekládá), záměrné rozdíly „Hlášky česky“ upravit na obě verze.
  Run: `git add -A mac/Sources mac/Tests docs && git commit -m "Mac: Czech preflight and CLI messages with codes"`

---

### Task 35: Nastavení — zachování neznámých klíčů a nové klíče plánů 6–7

**Files:**
- Modify: `mac/Sources/OffloadKit/Desktop/AppSettings.swift` (`AppSettings`, `SettingsStore.decode/encode`)
- Modify: `mac/Tests/OffloadKitTests/SettingsStoreTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `SettingsStore.decode(_:catalog:)`, `encode(_:)`, `normalize` (úkoly 10, 21).
- Produces:
  - `public struct NamingSettings: Codable, Equatable { var suggestFromClips = true }`
  - `public struct OutputSettings: Codable, Equatable { var dayReport = true; var clipListCsv = true; var clipListAle = false }`
  - `AppSettings.naming: NamingSettings`, `.hash: String` (`"xxh64"` | `"both"`, jiné se zachová), `.outputs: OutputSettings`,
    `.raw: [String: Any]` (mimo `Equatable`, poslední načtený JSON), `.unreadable: Set<String>` (sekce, které nešly přečíst)
  - `SettingsStore.encode(_:)` slučuje do `raw`: neznámé klíče nahoře, v `proxy`, `tools`, `naming`, `outputs` zůstanou

Pravidla (FORMATY „settings.json“; na Macu `UserDefaults["settings"]` se stejným schématem, spec D.22.9):
- Neznámé klíče se při uložení **zachovají** (na horní úrovni i v každé sekci); sekce, kterou tahle verze nedokáže přečíst
  (jiný typ, poškozená), zůstane v uloženém JSON **beze změny** — její výchozí hodnoty platí jen v paměti.
- `hash`: `"both"` = xxh64 + MD5; jiná hodnota se zachová a čte jako `"xxh64"`.
- Bez tohoto úkolu by první uložení po spuštění starší verze Macu smazalo projekty a pravidla z novější (R24).

- [ ] **Step 1: Padající testy** (do `SettingsStoreTests.swift`):

```swift
    func testUnknownKeysSurviveASave() throws {
        let json = """
        {"destRoots":["/Volumes/A"],"verify":true,"futureTop":{"x":1},
         "proxy":{"enabled":true,"height":1080,"codec":"h264","bitrateMbps":10,"futureProxy":"keep"},
         "projects":[{"name":"Film X","destRoots":["/Volumes/B"]}],"activeProject":"Film X",
         "hash":"sha256","outputs":{"dayReport":false,"futureOut":2}}
        """
        var s = SettingsStore.decode(Data(json.utf8))
        XCTAssertEqual(s.hash, "sha256")
        XCTAssertEqual(HashKinds.fromSetting(s.hash), .xxh64)
        XCTAssertFalse(s.outputs.dayReport)
        s.verify = false
        let saved = try XCTUnwrap(JSONSerialization.jsonObject(with: XCTUnwrap(SettingsStore.encode(s))) as? [String: Any])
        XCTAssertNotNil(saved["futureTop"])
        XCTAssertNotNil(saved["projects"], "a newer version's projects stay")
        XCTAssertEqual(saved["activeProject"] as? String, "Film X")
        XCTAssertEqual((saved["proxy"] as? [String: Any])?["futureProxy"] as? String, "keep")
        XCTAssertEqual((saved["outputs"] as? [String: Any])?["futureOut"] as? Int, 2)
        XCTAssertEqual(saved["verify"] as? Bool, false)
        XCTAssertEqual(saved["hash"] as? String, "sha256", "an unknown hash value is kept as it was")
    }

    func testUnreadableSectionIsKeptUnchanged() throws {
        let json = #"{"destRoots":["/Volumes/A"],"proxy":"from a future version"}"#
        let s = SettingsStore.decode(Data(json.utf8))
        XCTAssertEqual(s.destRoots, ["/Volumes/A"])
        XCTAssertTrue(s.unreadable.contains("proxy"))
        let saved = try XCTUnwrap(JSONSerialization.jsonObject(with: XCTUnwrap(SettingsStore.encode(s))) as? [String: Any])
        XCTAssertEqual(saved["proxy"] as? String, "from a future version")
    }

    func testDefaultsOfNewKeys() {
        let s = SettingsStore.decode(nil)
        XCTAssertTrue(s.naming.suggestFromClips)
        XCTAssertEqual(s.hash, "xxh64")
        XCTAssertEqual(s.outputs, OutputSettings())
    }
```

Run: `swift test --filter SettingsStoreTests` → nesestaví se.

- [ ] **Step 2: Implementace** — v `AppSettings.swift`:

```swift
public struct NamingSettings: Codable, Equatable {
    public var suggestFromClips = true
    public init() {}
}

public struct OutputSettings: Codable, Equatable {
    public var dayReport = true
    public var clipListCsv = true
    public var clipListAle = false
    public init() {}
}
```

`AppSettings` dostane `naming`, `hash = "xxh64"`, `outputs` (do `CodingKeys` a do čtení po sekcích jako ostatní klíče —
vadná sekce = výchozí hodnota **a** jméno sekce do `unreadable`), a pole mimo `Codable`/`Equatable`:

```swift
    /// The JSON object last read (unknown keys of newer versions); written back under the known keys on save.
    public var raw: [String: Any] = [:]
    /// Sections this version could not read: saved back exactly as they were.
    public var unreadable: Set<String> = []

    public static func == (a: AppSettings, b: AppSettings) -> Bool {
        a.destRoots == b.destRoots && a.verify == b.verify && a.analyzeClips == b.analyzeClips && a.proxy == b.proxy
            && a.tools == b.tools && a.portNames == b.portNames && a.naming == b.naming && a.hash == b.hash && a.outputs == b.outputs
    }
```

`SettingsStore.decode`: na začátku `raw = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:]`, při
selhání sekce `unreadable.insert(name)`. `encode`:

```swift
    /// Known keys over the last read JSON: unknown keys (top level and in every section) and unreadable sections stay.
    public static func encode(_ s: AppSettings) -> Data? {
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys]
        guard let known = try? JSONSerialization.jsonObject(with: enc.encode(s)) as? [String: Any] else { return nil }
        var out = s.raw
        for (key, value) in known where !s.unreadable.contains(key) {
            if let section = value as? [String: Any], let old = out[key] as? [String: Any] {
                out[key] = old.merging(section) { _, new in new }
            } else {
                out[key] = value
            }
        }
        return try? JSONSerialization.data(withJSONObject: out, options: [.sortedKeys])
    }
```

`AppModel` po každém `decode` drží vrácenou hodnotu (s `raw`) a ukládá přes `encode` — `raw` se tak nese dál.

- [ ] **Step 3: Testy projdou** — Run: `swift test --filter SettingsStoreTests`, pak `swift test`.

- [ ] **Step 4: Záznam a commit** — `docs/FORMATY.md`, oddíl „settings.json“: první odrážka „Neznámé klíče se při uložení
  zachovají …“ doplnit „— Windows od 0.2.0, Mac od 0.3.0 (`UserDefaults` klíč `settings`, stejné schéma)“.
  `docs/VYVOJ-MAC.md`: `- Úkol 35 — nastavení zachová neznámé klíče a nečitelné sekce (projekty a pravidla z novější verze
  nezmizí); klíče naming, hash, outputs`. Run: `git add -A mac docs && git commit -m "Mac: settings keep unknown keys; naming, hash, outputs"`

---

### Task 36: Svazek karty a cíle — sériové číslo FS, kapacita, `volumeSerial`, klíč složky, časy souborů

**Files:**
- Create: `mac/Sources/OffloadKit/Volume.swift`
- Create: `mac/Tests/OffloadKitTests/VolumeTests.swift`, `mac/Tests/OffloadKitTests/FileTimesTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `IO.filesystemType`, `CardIdentity.of` (úkol 9), DiskArbitration (úkol 14 linkuje framework).
- Produces:
  - `public struct CardKey: Hashable, Codable { fsSerial: String; capacity: Int64 }`
  - `public struct CardVolume: Equatable { identity: String?; fsSerial: String; capacity: Int64; label: String?; filesystem: String; var key: CardKey; static func of(_ source: String) -> CardVolume? }`
  - `enum BootSector { static func serial(_ sector: [UInt8]) -> (serial: UInt32, kind: String)?; static func read(device: String) -> [UInt8]? }`
  - `enum VolumeFacts { mount(of:) -> Mount?; fsSerial(_ root: String) -> String?; volumeSerialText(_ path: String) -> String?; folderKey(_ path: String) -> String; filesystemName(_ path: String) -> String; capacity(_ root: String) -> Int64?; isLocal(_ path: String) -> Bool; underRoot(_ path: String) -> String }`
  - `public enum FileTimes { static func storesLocalTime(_ fs: String?) -> Bool; static func same(_ a: Date, _ b: Date, localTime: Bool) -> Bool; static func close(_ a: Date, _ b: Date, localTime: Bool) -> Bool; static func ticks(_ d: Date) -> Int64; static func date(ticks: Int64) -> Date }`

Pravidla (FORMATY „Místní soubory“, „`offload_report.json`“ `destinations[].volumeSerial`; spec D.23.2):

- **`fsSerial`** (klíč karty v historii) = sériové číslo souborového systému z **boot sektoru**, 8 velkých hex (`1A2B3C4D`)
  — stejné číslo, jaké dává Windows `GetVolumeInformation`: exFAT `VolumeSerialNumber` (offset 0x64, LE u32), FAT32
  `BS_VolID` (0x43, když 0x42 = 0x29), FAT12/16 `BS_VolID` (0x27, když 0x26 = 0x29), NTFS dolních 32 bitů `SerialNumber`
  (0x48, LE u64). Boot sektor se čte z `/dev/rdiskNsM` (BSD jméno z `statfs.f_mntfromname` / DiskArbitration), 4096 bajtů
  od nuly, `F_NOCACHE`. **Na kartu se nezapisuje.** Když zařízení nejde otevřít (`EACCES` — uzel zařízení patří rootovi,
  spec D.27 ř. 1) nebo FS není FAT/exFAT/NTFS: `fsSerial = "uuid:" + VolumeUUID` z `URLResourceKey.volumeUUIDStringKey`
  (historie pak kartu pozná na tomtéž Macu, jen ne v historii přenesené z Windows); bez UUID `nil` → karta jako ručně přidaná
  složka (pozná se podle `shape`).
- `capacity` = `statfs` `f_blocks × f_bsize` (velikost souborového systému, jako Windows `TotalNumberOfBytes`; shodu ověřit,
  spec D.27 ř. 2).
- **`volumeSerial`** (report, R12): FAT/exFAT/NTFS `"1A2B-3C4D"` (horních 16 bitů, pomlčka, dolních 16, velkými) — tutéž
  hodnotu napíše Windows pro tentýž disk; jiné FS (APFS, HFS+) **`"uuid:<VolumeUUID>"`** (Mac-only tvar, Windows takový disk
  nečte; FORMATY doplní úkol 50); `nil`, když nejde zjistit.
- **`folderKey`** (zámky doplnění a dne): `"<VolumeUUID>|<cesta pod kořenem svazku>"` velkými písmeny, bez koncového `/`;
  bez UUID `"|<plná cesta>"`. Stejná složka jinak napsaná (velikost písmen, `/` na konci, symlink) dá stejný klíč
  (`resolvingSymlinksInPath`).
- **`underRoot`** — cesta bez kořene svazku, oddělovač `/`: Mac `/Volumes/RAID/Zaloha/…` → `/Zaloha/…` (kořen = `f_mntonname`;
  pro cesty z cizích reportů, které na tomto Macu neexistují, `/Volumes/<jméno>` nebo `/`), Windows `D:\Zaloha\…` → `/Zaloha/…`,
  UNC `\\server\share\Zaloha\…` → `/Zaloha/…`. Slouží k porovnání sloupce reportu podle sériového čísla (úkol 38).
- **Časy souborů** (R3): FAT12/16/32 ukládá místní čas — dva časy jsou „stejné“, když se liší o celé hodiny (nejvýš 14)
  se stejnými minutami, sekundami a zlomky; exFAT, NTFS, APFS jen přesná shoda. „Blízké“ (report vs disk): do 2 s, na FAT
  i po posunu o celé hodiny ±2 s. Na Macu je FAT `f_fstypename == "msdos"`; z Windows reportu/historie `FAT`, `FAT32`.
- **Ticks** (`mtimeTicksUtc` historie): .NET ticks UTC = `sekundy od 1970 × 10 000 000 + nanosekundy / 100 + 621 355 968 000 000 000`
  — otisk karty tak vyjde bajtově stejně jako na Windows.

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/VolumeTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class VolumeTests: XCTestCase {
    private func sector(_ patch: (inout [UInt8]) -> Void) -> [UInt8] {
        var s = [UInt8](repeating: 0, count: 512)
        patch(&s)
        return s
    }

    private func put(_ s: inout [UInt8], _ at: Int, _ text: String) { for (i, b) in text.utf8.enumerated() { s[at + i] = b } }
    private func le32(_ s: inout [UInt8], _ at: Int, _ v: UInt32) { for i in 0..<4 { s[at + i] = UInt8((v >> (8 * UInt32(i))) & 0xff) } }

    func testBootSectorSerials() {
        let exfat = sector { put(&$0, 3, "EXFAT   "); le32(&$0, 0x64, 0x1A2B_3C4D) }
        XCTAssertEqual(BootSector.serial(exfat)?.serial, 0x1A2B_3C4D)
        XCTAssertEqual(BootSector.serial(exfat)?.kind, "exFAT")
        let fat32 = sector { put(&$0, 0x52, "FAT32   "); $0[0x42] = 0x29; le32(&$0, 0x43, 0xDEAD_BEEF) }
        XCTAssertEqual(BootSector.serial(fat32)?.serial, 0xDEAD_BEEF)
        XCTAssertEqual(BootSector.serial(fat32)?.kind, "FAT32")
        let fat16 = sector { put(&$0, 0x36, "FAT16   "); $0[0x26] = 0x29; le32(&$0, 0x27, 0x0102_0304) }
        XCTAssertEqual(BootSector.serial(fat16)?.kind, "FAT")
        let ntfs = sector { put(&$0, 3, "NTFS    "); le32(&$0, 0x48, 0x5566_7788); le32(&$0, 0x4C, 0x1122_3344) }
        XCTAssertEqual(BootSector.serial(ntfs)?.serial, 0x5566_7788, "Windows reports the low 32 bits")
        XCTAssertNil(BootSector.serial(sector { _ in }), "no known file system")
    }

    func testSerialTextsAsWindows() {
        XCTAssertEqual(VolumeFacts.fsSerialText(0x1A2B_3C4D), "1A2B3C4D")
        XCTAssertEqual(VolumeFacts.volumeSerialText(0x1A2B_3C4D), "1A2B-3C4D")
    }

    func testUnderRootForBothPlatforms() {
        XCTAssertEqual(VolumeFacts.underRoot(#"D:\Zaloha\2026-10-08\A001"#), "/Zaloha/2026-10-08/A001")
        XCTAssertEqual(VolumeFacts.underRoot(#"\\nas\share\Zaloha\A001"#), "/Zaloha/A001")
        XCTAssertEqual(VolumeFacts.underRoot("/Volumes/NotMounted-Here/Zaloha/2026-10-08/A001/"), "/Zaloha/2026-10-08/A001")
    }

    func testFolderKeyIgnoresSpelling() throws {
        let dir = tempDir()
        let a = dir.sub("Zaloha", "2026-10-08", "A001")
        try FileManager.default.createDirectory(atPath: a, withIntermediateDirectories: true)
        XCTAssertEqual(VolumeFacts.folderKey(a), VolumeFacts.folderKey(a + "/"))
        XCTAssertNotEqual(VolumeFacts.folderKey(a), VolumeFacts.folderKey(dir.sub("Zaloha")))
    }

    func testCardVolumeOfAFolderIsNil() {
        XCTAssertNil(CardVolume.of(tempDir().path), "a folder is not a volume root")
    }
}
```

`mac/Tests/OffloadKitTests/FileTimesTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class FileTimesTests: XCTestCase {
    let t = Date(timeIntervalSince1970: 1_791_000_000)

    func testFatShiftByWholeHoursIsSame() {
        XCTAssertTrue(FileTimes.storesLocalTime("msdos"))
        XCTAssertTrue(FileTimes.storesLocalTime("FAT32"))
        XCTAssertTrue(FileTimes.same(t, t.addingTimeInterval(3600), localTime: true))
        XCTAssertTrue(FileTimes.same(t, t.addingTimeInterval(-14 * 3600), localTime: true))
        XCTAssertFalse(FileTimes.same(t, t.addingTimeInterval(15 * 3600), localTime: true))
        XCTAssertFalse(FileTimes.same(t, t.addingTimeInterval(3601), localTime: true))
    }

    func testExfatShiftIsChange() {
        XCTAssertFalse(FileTimes.storesLocalTime("exfat"))
        XCTAssertFalse(FileTimes.same(t, t.addingTimeInterval(3600), localTime: false))
    }

    func testCloseAllowsTwoSecondsAndOnFatWholeHours() {
        XCTAssertTrue(FileTimes.close(t, t.addingTimeInterval(1.9), localTime: false))
        XCTAssertFalse(FileTimes.close(t, t.addingTimeInterval(2.5), localTime: false))
        XCTAssertTrue(FileTimes.close(t, t.addingTimeInterval(3600 + 1), localTime: true))
    }

    func testDotNetTicks() {
        XCTAssertEqual(FileTimes.ticks(Date(timeIntervalSince1970: 0)), 621_355_968_000_000_000)
        XCTAssertEqual(FileTimes.ticks(Date(timeIntervalSince1970: 1.5)), 621_355_968_015_000_000)
        XCTAssertEqual(FileTimes.date(ticks: 621_355_968_015_000_000).timeIntervalSince1970, 1.5, accuracy: 1e-6)
    }
}
```

Run: `swift test --filter "VolumeTests|FileTimesTests"` → nesestaví se.

- [ ] **Step 2: Implementace** — `mac/Sources/OffloadKit/Volume.swift` (celý soubor):

```swift
import Darwin
import Foundation

/// How a card volume is recognised again (Windows R1): the file system's serial number plus the capacity.
public struct CardKey: Hashable, Codable {
    public let fsSerial: String
    public let capacity: Int64
}

/// The card volume behind a source that is a volume root, as history stores it. Touches the disk — never on the main thread.
public struct CardVolume: Equatable {
    public let identity: String?
    public let fsSerial: String
    public let capacity: Int64
    public let label: String?
    public let filesystem: String
    public var key: CardKey { CardKey(fsSerial: fsSerial, capacity: capacity) }

    /// Nil for a folder (not a volume root) or a volume that cannot be read.
    public static func of(_ source: String) -> CardVolume? {
        let url = URL(fileURLWithPath: source).standardizedFileURL
        guard let m = VolumeFacts.mount(of: url.path), m.mountPoint == url.path.trimmingTrailingSlash(),
              let serial = VolumeFacts.fsSerial(m.mountPoint), let capacity = VolumeFacts.capacity(m.mountPoint) else { return nil }
        let label = (try? url.resourceValues(forKeys: [.volumeNameKey]))?.volumeName
        return CardVolume(identity: CardIdentity.of(source), fsSerial: serial, capacity: capacity, label: label,
                          filesystem: VolumeFacts.filesystemName(m.mountPoint))
    }
}

extension String {
    func trimmingTrailingSlash() -> String {
        var s = self
        while s.count > 1 && s.hasSuffix("/") { s.removeLast() }
        return s
    }
}

/// The first sector of a FAT/exFAT/NTFS volume: its serial number as Windows reports it.
enum BootSector {
    static func serial(_ s: [UInt8]) -> (serial: UInt32, kind: String)? {
        guard s.count >= 0x70 else { return nil }
        func text(_ at: Int, _ n: Int) -> String { String(decoding: s[at..<(at + n)], as: UTF8.self) }
        func le32(_ at: Int) -> UInt32 { (0..<4).reduce(UInt32(0)) { $0 | UInt32(s[at + $1]) << (8 * UInt32($1)) } }
        if text(3, 8) == "EXFAT   " { return (le32(0x64), "exFAT") }
        if text(3, 8) == "NTFS    " { return (le32(0x48), "NTFS") }
        if text(0x52, 8).hasPrefix("FAT32"), s[0x42] == 0x29 { return (le32(0x43), "FAT32") }
        if text(0x36, 8).hasPrefix("FAT"), s[0x26] == 0x29 { return (le32(0x27), "FAT") }
        return nil
    }

    /// 4096 bytes from the start of the raw device ("/dev/disk4s1" → "/dev/rdisk4s1"), read only. Nil when it cannot be
    /// opened (EACCES: the device node is root's — spec D.27) or read.
    static func read(device: String) -> [UInt8]? {
        let raw = device.hasPrefix("/dev/disk") ? "/dev/r" + device.dropFirst("/dev/".count) : device
        let fd = open(raw, O_RDONLY)
        guard fd >= 0 else { return nil }
        defer { close(fd) }
        _ = fcntl(fd, F_NOCACHE, 1)
        var buf = [UInt8](repeating: 0, count: 4096)
        let n = buf.withUnsafeMutableBytes { Darwin.pread(fd, $0.baseAddress, 4096, 0) }
        return n >= 512 ? Array(buf[0..<n]) : nil
    }
}

enum VolumeFacts {
    struct Mount { let mountPoint: String; let device: String; let fsType: String; let local: Bool }

    static func mount(of path: String) -> Mount? {
        var fs = statfs()
        guard statfs(path, &fs) == 0 else { return nil }
        func str<T>(_ t: T) -> String { withUnsafeBytes(of: t) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) } }
        return Mount(mountPoint: str(fs.f_mntonname), device: str(fs.f_mntfromname), fsType: str(fs.f_fstypename),
                     local: fs.f_flags & UInt32(MNT_LOCAL) != 0)
    }

    static func isLocal(_ path: String) -> Bool { mount(of: path)?.local ?? false }

    static func fsSerialText(_ v: UInt32) -> String { String(format: "%08X", v) }
    static func volumeSerialText(_ v: UInt32) -> String { String(format: "%04X-%04X", v >> 16, v & 0xFFFF) }

    static func bootSerial(_ path: String) -> (serial: UInt32, kind: String)? {
        guard let m = mount(of: path), ["msdos", "exfat", "ntfs"].contains(m.fsType), m.device.hasPrefix("/dev/") else { return nil }
        return BootSector.read(device: m.device).flatMap(BootSector.serial)
    }

    static func volumeUUID(_ path: String) -> String? {
        (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeUUIDStringKey]))?.volumeUUIDString
    }

    /// History key part: "1A2B3C4D", or "uuid:<volume UUID>" when the boot sector cannot be read (spec D.23.2).
    static func fsSerial(_ root: String) -> String? {
        if let b = bootSerial(root) { return fsSerialText(b.serial) }
        return volumeUUID(root).map { "uuid:" + $0 }
    }

    /// Report `destinations[].volumeSerial`: "1A2B-3C4D" (FAT/exFAT/NTFS, as Windows), else "uuid:<volume UUID>".
    static func volumeSerialText(_ path: String) -> String? {
        if let b = bootSerial(path) { return volumeSerialText(b.serial) }
        return volumeUUID(path).map { "uuid:" + $0 }
    }

    /// "exFAT", "FAT32", "FAT", "NTFS", "APFS", "HFS+", or what statfs says.
    static func filesystemName(_ path: String) -> String {
        if let b = bootSerial(path) { return b.kind }
        guard let m = mount(of: path) else { return "?" }
        return ["apfs": "APFS", "hfs": "HFS+", "exfat": "exFAT", "msdos": "FAT32", "ntfs": "NTFS", "smbfs": "SMB"][m.fsType] ?? m.fsType
    }

    static func capacity(_ root: String) -> Int64? {
        var fs = statfs()
        guard statfs(root, &fs) == 0 else { return nil }
        return Int64(fs.f_blocks) * Int64(fs.f_bsize)
    }

    /// Lock key of a folder: the volume's UUID and the path under its root, upper-cased (spec D.23.4).
    static func folderKey(_ path: String) -> String {
        let full = URL(fileURLWithPath: path).resolvingSymlinksInPath().standardizedFileURL.path.trimmingTrailingSlash()
        if let m = mount(of: full), let uuid = volumeUUID(full) {
            let root = m.mountPoint.trimmingTrailingSlash()
            let rel = root == "/" ? full : String(full.dropFirst(root.count))
            return (uuid + "|" + (rel.isEmpty ? "/" : rel)).uppercased()
        }
        return ("|" + full).uppercased()
    }

    /// The path without its volume root, "/" separated: for comparing a report's card path with a folder by disk (R12).
    static func underRoot(_ path: String) -> String {
        var p = path.replacingOccurrences(of: "\\", with: "/")
        if p.hasPrefix("//") {                                   // \\server\share\rest
            let parts = p.split(separator: "/", omittingEmptySubsequences: true)
            p = "/" + parts.dropFirst(2).joined(separator: "/")
        } else if p.count >= 2, p.dropFirst().first == ":" {      // D:\rest
            p = String(p.dropFirst(2))
        } else if let m = mount(of: p), FileManager.default.fileExists(atPath: p) {
            let root = m.mountPoint.trimmingTrailingSlash()
            if root != "/" { p = String(p.dropFirst(root.count)) }
        } else if p.hasPrefix("/Volumes/") {                     // a Mac path of a disk not here
            let parts = p.split(separator: "/", omittingEmptySubsequences: true)
            p = "/" + parts.dropFirst(2).joined(separator: "/")
        }
        p = p.trimmingTrailingSlash()
        return p.hasPrefix("/") ? p : "/" + p
    }
}

/// File times compared over a card's life (Windows R3).
public enum FileTimes {
    public static func storesLocalTime(_ fs: String?) -> Bool {
        guard let fs = fs?.lowercased() else { return false }
        return fs == "msdos" || fs.hasPrefix("fat")
    }

    public static func same(_ a: Date, _ b: Date, localTime: Bool) -> Bool {
        let ta = ticks(a), tb = ticks(b)
        if ta == tb { return true }
        guard localTime else { return false }
        let diff = abs(ta - tb), hour: Int64 = 36_000_000_000
        return diff % hour == 0 && diff <= 14 * hour
    }

    public static func close(_ a: Date, _ b: Date, localTime: Bool) -> Bool {
        let slack: Int64 = 20_000_000, hour: Int64 = 36_000_000_000
        let diff = abs(ticks(a) - ticks(b))
        if diff <= slack { return true }
        guard localTime, diff <= 14 * hour + slack else { return false }
        let off = diff % hour
        return off <= slack || hour - off <= slack
    }

    /// .NET ticks (100 ns since 0001-01-01 UTC) — the `mtimeTicksUtc` of history file lists and fingerprints.
    public static func ticks(_ d: Date) -> Int64 {
        let ns = (d.timeIntervalSince1970 * 1e7).rounded()
        return Int64(ns) + 621_355_968_000_000_000
    }

    public static func ticks(_ t: timespec) -> Int64 { Int64(t.tv_sec) * 10_000_000 + Int64(t.tv_nsec) / 100 + 621_355_968_000_000_000 }

    public static func date(ticks: Int64) -> Date { Date(timeIntervalSince1970: Double(ticks - 621_355_968_000_000_000) / 1e7) }
}
```

`SourceFile` dostane `var modifiedUtc: Date` (z `mtime`) a `var ticks: Int64 { FileTimes.ticks(mtime) }` — používá je historie,
doplnění a oprava (přesně z `timespec`, bez zaokrouhlení přes `Double`).

- [ ] **Step 3: Ověřit na Macu** (spec D.27 ř. 1–2; výsledek do zprávy commitu):

```sh
hdiutil create -size 200m -fs ExFAT -volname EXF /tmp/exf.dmg && hdiutil attach /tmp/exf.dmg
hdiutil create -size 200m -fs MS-DOS -volname FATV /tmp/fat.dmg && hdiutil attach /tmp/fat.dmg
ls -l /dev/rdisk*                      # vlastník uzlů obrazu i skutečné karty ve čtečce
swift run offload ports --card /Volumes/EXF   # (nebo malý swift skript) — VolumeFacts.fsSerial("/Volumes/EXF")
```

Porovnat `fsSerial` s Windows (`vol E:` na tomtéž obrazu nebo kartě) a `capacity` s Windows `fsutil volume diskfree E:`
(„Total bytes“). Skutečná karta ve čtečce: jde-li `/dev/rdiskN` otevřít bez sudo. Když ne, platí záložní `uuid:` (pravidlo
výše) — zapsat do ROZDILY (historie z Windows se na Macu kartou nepárují) a do D.27.

- [ ] **Step 4: Testy projdou, záznam a commit** — Run: `swift test`. `docs/VYVOJ-MAC.md`: `- Úkol 36 — svazek: sériové číslo
  FS z boot sektoru (exFAT/FAT/NTFS, jako Windows), záložně uuid:, kapacita, volumeSerial, klíč složky pro zámky, časy FAT
  s tolerancí celých hodin, .NET ticks; ověřeno: <výsledek kroku 3>`.
  Run: `git add -A mac docs && git commit -m "Mac: volume serial from the boot sector, folder keys, FAT time tolerance, ticks"`

---

### Task 37: Zámky — historie, složka karty, den (`flock`)

**Files:**
- Create: `mac/Sources/OffloadKit/FileLock.swift`
- Create: `mac/Tests/OffloadKitTests/FileLockTests.swift`
- Modify: `mac/Tests/OffloadKitTests/TestSupport.swift` (`OFFLOAD_SETTINGS_DIR` na dočasnou složku)
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `VolumeFacts.folderKey` (úkol 36).
- Produces:
  - `public enum AppSupport { static var folder: String }` — `$OFFLOAD_SETTINGS_DIR`, jinak `~/Library/Application Support/Offload`
    (jako Windows `%APPDATA%\Offload` s `OFFLOAD_SETTINGS_DIR`); historie, zámky, `update-check.json` (`hwtest.json` z úkolu 15
    se na `AppSupport.folder` přepne taky)
  - `public final class FileLock { static func tryLock(_ path: String) -> FileLock?; static func lock(_ path: String, timeout: TimeInterval) -> FileLock?; func release() }` (deinit uvolní)
  - `public enum Locks { static var folder: String; static func history(_ historyDir: String) -> String; static func cardFolder(_ cardPath: String) -> String; static func day(_ dayFolder: String) -> String }`
  - `public final class FolderLocks { static func acquire(_ cardPaths: [String]) throws -> FolderLocks; func release() }` — hází `OffloadError.preflight(Msg.appendBusy(path), code: .appendBusy)`

Pravidla (spec D.23.4; Windows: pojmenované mutexy `Local\Offload-history-…`, `Local\Offload-append-…`, `Local\Offload-day-…`):

- `flock(LOCK_EX | LOCK_NB)` na **místním** souboru `<supportDir>/locks/<jméno>.lock` (soubor zůstává, prázdný; zámek drží
  otevřený deskriptor). Zemře-li proces, jádro zámek uvolní (jako „abandoned mutex“ na Windows). Na cíl se zámkový soubor
  nikdy nepíše (cíl je smlouva FORMATY a může to být síťový disk). POSIX pojmenované semafory se nepoužijí — po pádu procesu
  zůstanou obsazené.
- Jména: historie `history-<sha256(plná cesta složky historie velkými)>.lock`; složka karty `append-<sha256(folderKey)>.lock`;
  den `day-<sha1(folderKey složky dne)>.lock`. Hex malými písmeny.
- `flock` patří otevřenému souboru: dva `open` téhož souboru ve **stejném** procesu se vylučují taky (GUI s dvěma úlohami).
- Složka karty: zámky všech složek se berou v **pevném pořadí** (podle jména zámku), bez čekání; obsazená = nic se nedrží
  a `appendBusy`. Historie: čekání až do limitu (5 s pro čtení/úpravu, úloha 60 s — úkol 44). Den: úloha nejvýš 5 s na cíl.
- **Mezi Macem a Windows se zámky nesdílejí** (jiný systém, jiný mechanismus): Mac a PC, které zapisují do téže sdílené
  zálohy, se nevylučují — stejně jako dva Windows počítače (ROZDILY „Doplnění do zálohy — zámek složky karty“). Chrání
  jen zápis „nový soubor“ reportů `_NNNN` a generace MHL (úkol 41).

- [ ] **Step 1: `TestSupport.swift`** — dočasná složka nastavení pro celý běh testů (historie, zámky, kontrola aktualizací
  nikdy do skutečného `~/Library/Application Support/Offload`):

```swift
/// Every test run writes history, locks and update checks into a temporary folder, never into the user's own.
/// Started by the first tempDir() (every test that touches the disk calls it) and by TestMedia.
let testSettingsDir: String = {
    let d = NSTemporaryDirectory() + "offload-tests-\(getpid())"
    try? FileManager.default.createDirectory(atPath: d, withIntermediateDirectories: true)
    setenv("OFFLOAD_SETTINGS_DIR", d, 1)
    return d
}()
```

a v `tempDir()` (úkol 1) jako první řádek `_ = testSettingsDir`. (Přepsat `XCTestCase.setUp()` v rozšíření nejde a SwiftPM
testy nemají `NSPrincipalClass` pro pozorovatele — proto líná globální konstanta.)

- [ ] **Step 1b: Padající testy** — `mac/Tests/OffloadKitTests/FileLockTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class FileLockTests: XCTestCase {
    func testSecondHolderRefusedAtOnce() throws {
        let p = tempDir().sub("x.lock")
        let a = try XCTUnwrap(FileLock.tryLock(p))
        XCTAssertNil(FileLock.tryLock(p), "the same process, another descriptor: refused")
        a.release()
        XCTAssertNotNil(FileLock.tryLock(p))
    }

    func testWaitUpToTimeout() throws {
        let p = tempDir().sub("w.lock")
        let a = try XCTUnwrap(FileLock.tryLock(p))
        let start = Date()
        XCTAssertNil(FileLock.lock(p, timeout: 0.3))
        XCTAssertGreaterThanOrEqual(Date().timeIntervalSince(start), 0.3)
        DispatchQueue.global().asyncAfter(deadline: .now() + 0.2) { a.release() }
        XCTAssertNotNil(FileLock.lock(p, timeout: 2))
    }

    func testFolderLocksAllOrNothing() throws {
        let d = tempDir()
        let one = d.sub("D", "2026-10-08", "A001"), two = d.sub("E", "2026-10-08", "A001")
        for p in [one, two] { try FileManager.default.createDirectory(atPath: p, withIntermediateDirectories: true) }
        let held = try FolderLocks.acquire([two])
        XCTAssertThrowsError(try FolderLocks.acquire([one, two])) { e in
            XCTAssertEqual((e as? OffloadError)?.code, .appendBusy)
        }
        XCTAssertNotNil(FileLock.tryLock(Locks.cardFolder(one)), "nothing held after a refusal")
        held.release()
    }

    func testDifferentSpellingSameLock() throws {
        let p = tempDir().sub("Zaloha", "A001")
        try FileManager.default.createDirectory(atPath: p, withIntermediateDirectories: true)
        XCTAssertEqual(Locks.cardFolder(p), Locks.cardFolder(p + "/"))
    }

    func testLocksLiveInTheSupportFolderNeverOnTheDestination() {
        XCTAssertTrue(Locks.cardFolder("/Volumes/RAID/2026-10-08/A001").hasPrefix(Locks.folder))
    }
}
```

- [ ] **Step 2: Implementace** — `mac/Sources/OffloadKit/FileLock.swift`:

```swift
import CryptoKit
import Darwin
import Foundation

/// An exclusive advisory lock on a local file (flock). Released by `release()`, by deinit, or by the kernel when the
/// process dies. Spec D.23.4.
public final class FileLock {
    private var fd: Int32

    private init(fd: Int32) { self.fd = fd }
    deinit { release() }

    public static func tryLock(_ path: String) -> FileLock? {
        try? FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        let fd = open(path, O_RDWR | O_CREAT | O_CLOEXEC, 0o600)
        guard fd >= 0 else { return nil }
        if flock(fd, LOCK_EX | LOCK_NB) == 0 { return FileLock(fd: fd) }
        close(fd)
        return nil
    }

    /// Tries every 50 ms until `timeout`; nil when the lock stayed held.
    public static func lock(_ path: String, timeout: TimeInterval) -> FileLock? {
        let end = Date().addingTimeInterval(timeout)
        while true {
            if let l = tryLock(path) { return l }
            if Date() >= end { return nil }
            usleep(50_000)
        }
    }

    public func release() {
        guard fd >= 0 else { return }
        flock(fd, LOCK_UN)
        close(fd)
        fd = -1
    }
}

/// Offload's own folder of this Mac user (history, locks, update check); OFFLOAD_SETTINGS_DIR moves it (tests).
public enum AppSupport {
    public static var folder: String {
        if let d = ProcessInfo.processInfo.environment["OFFLOAD_SETTINGS_DIR"], !d.isEmpty { return d }
        return NSHomeDirectory() + "/Library/Application Support/Offload"
    }
}

public enum Locks {
    public static var folder: String { AppSupport.folder + "/locks" }

    static func hex<H: HashFunction>(_ h: H.Type, _ s: String) -> String { H.hash(data: Data(s.utf8)).map { String(format: "%02x", $0) }.joined() }

    public static func history(_ historyDir: String) -> String {
        let full = URL(fileURLWithPath: historyDir).standardizedFileURL.path.trimmingTrailingSlash().uppercased()
        return folder + "/history-" + hex(SHA256.self, full) + ".lock"
    }

    public static func cardFolder(_ cardPath: String) -> String { folder + "/append-" + hex(SHA256.self, VolumeFacts.folderKey(cardPath)) + ".lock" }

    public static func day(_ dayFolder: String) -> String { folder + "/day-" + hex(Insecure.SHA1.self, VolumeFacts.folderKey(dayFolder)) + ".lock" }
}

/// One job at a time per card folder (append, repair, a copy verifying with its proxies). Never waits: a busy folder
/// refuses at once and nothing stays held.
public final class FolderLocks {
    private var held: [FileLock]

    private init(_ held: [FileLock]) { self.held = held }
    deinit { release() }

    public static func acquire(_ cardPaths: [String]) throws -> FolderLocks {
        var held: [FileLock] = []
        var seen = Set<String>()
        let ordered = cardPaths.map { (Locks.cardFolder($0), $0) }.filter { seen.insert($0.0).inserted }.sorted { $0.0 < $1.0 }
        for (name, path) in ordered {
            guard let l = FileLock.tryLock(name) else {
                held.reversed().forEach { $0.release() }
                throw OffloadError.preflight(Msg.appendBusy(path), code: .appendBusy)
            }
            held.append(l)
        }
        return FolderLocks(held)
    }

    /// Any thread, any number of times (flock is not tied to a thread, unlike a Windows mutex).
    public func release() {
        held.reversed().forEach { $0.release() }
        held = []
    }
}
```

Pozn.: Windows potřebuje „držitelské vlákno“, protože mutex uvolní jen vlákno, které ho vzalo; `flock` takové omezení nemá —
zámek vzatý ve fázi 1 smí uvolnit fáze 2 na jiném vlákně (úkol 67).

- [ ] **Step 3: Testy projdou, záznam a commit** — Run: `swift test --filter FileLockTests`, `swift test`.
  `docs/VYVOJ-MAC.md`: `- Úkol 37 — zámky historie, složky karty a dne přes flock v Application Support/Offload/locks
  (nikdy na cíli), složky v pevném pořadí bez čekání`. `docs/ROZDILY.md` (záměrné rozdíly, „Doplnění do zálohy — zámek
  složky karty“): doplnit „Mac: `flock` na místním souboru — platí pro všechny procesy jednoho uživatele na jednom Macu;
  Mac a Windows se navzájem nevylučují“. Run: `git add -A mac docs && git commit -m "Mac: history, card folder and day locks with flock"`

---

### Task 38: `CardRecord` — co říkají reporty složky karty, a jediná definice „ověřeno“

**Files:**
- Create: `mac/Sources/OffloadKit/CardRecord.swift`
- Create: `mac/Tests/OffloadKitTests/CardRecordTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `OffloadReport.read`, `jsonName`, `generationNumber`, `isGood` (úkoly 3, 33), `VolumeFacts.volumeSerialText`, `underRoot` (úkol 36), `ClipDetector.detect`.
- Produces:
  - `public struct RecordedFile { let file: OffloadReport.FileEntry; let generation: Int; let report: OffloadReport }`
  - `public struct CardRecord { cardPath; generations: [(generation: Int, report: OffloadReport)]; files: [String: RecordedFile] (klíč = cesta malými písmeny); clips: [ClipEntry]; reportMissing; error: String?; problems: [String]; highestReportNumber: Int; volumeSerial: String?; incomplete: Bool }`
  - `CardRecord.load(_ cardPath: String, maxGeneration: Int? = nil, serial: String?? = nil) -> CardRecord`
  - `var verified: Bool`, `verifyPending: Bool`, `failedFiles: [String]`, `project: String?`, `folderName: String`, `started/finished: Date?`
  - `static func CardRecord.readBack(_ rec: RecordedFile, cardPath: String, serial: String?) -> Bool` — **jediná definice „ověřeno“**
  - `static func CardRecord.columnOf(_ r: OffloadReport, cardPath: String, serial: String?) -> Int` (−1 = žádný)
  - `static func CardRecord.destinationIndex(_ r: OffloadReport, cardPath: String, position: Int) -> Int`
  - `static func CardRecord.stateOf(_ rec: RecordedFile, cardPath: String, position: Int) -> String`
  - `public struct BackupState: Equatable { failed: Int; unverified: Int; static func of(_ cardPaths: [String]) -> BackupState }`

Pravidla (spec D.22.6; FORMATY „`offload_report.json`“, „Doplnění“):

- `load` přečte `_REPORT/offload_report.json` (generace 1, nebo `generation` v něm) a všechna `offload_report_NNNN.json`
  (bez ohledu na velikost písmen); generace se **seřadí** a pro každou cestu platí **nejnovější** generace, která ji uvádí.
  Chybí hlavní report → `reportMissing`; nečitelný hlavní report nebo složka `_REPORT` → `error` (nic dalšího); nečitelný
  report doplnění → `problems` (doplnění/oprava se pak odmítnou). `highestReportNumber` = nejvyšší číslo ze všech
  `*_report_NNNN.(json|txt|html|pdf)` a přečtených generací — i soubor, který zbyl po přerušeném doplnění bez JSON.
  `maxGeneration` vynechá vyšší generace (navázání, úkol 69).
- Klipy: po generacích; generace bez analýzy dá klipy z `ClipDetector.detect` nad svými soubory (bez metadat) a **nikdy**
  nenahradí klip popsaný dřív; analyzovaná generace ano.
- **`readBack(rec, cardPath, serial)`** — kopie v této složce je ověřená, když nejnovější záznam souboru říká `verified`, nebo
  report byl ověřený a stav není `written` (selhání je selhání, ne „neověřeno“) — **a** report nemá `verifyPending` ani
  `verifyIncomplete`. Sloupec = `columnOf`; když žádný (cizí cesty — report z Windows na Macu nebo naopak, disk pod jiným
  jménem), musí to říct **každý** sloupec (nikdy falešně zeleně).
- **`columnOf`**: report, který zná sériová čísla svých cílů (`volumeSerial`), se páruje **podle disku**: sloupec s tímtéž
  `volumeSerial` a stejnou cestou pod kořenem (`underRoot`, bez ohledu na velikost písmen a oddělovač) — právě jeden, jinak −1;
  když report sériová čísla má, ale tato složka svoje zjistit nejde, −1 (nikdy hádat podle cesty). Report bez sériových
  čísel (Mac ≤ 0.2, Windows < 0.6) se páruje podle normalizované plné cesty.
- `destinationIndex` (jen plánovače, ne „ověřeno“): sloupec se stejnou cestou; jinak jediný cíl → 0; jinak pozice omezená na rozsah.
- `verified` (složka) = aspoň jedna generace, žádné `problems`, ne `incomplete` a každý soubor `readBack` (složka bez souborů:
  všechny reporty `verified`).
- `incomplete`: nejnovější generace má tady `verifyIncomplete`, nebo report téže generace na **jiném** cíli (cesta z reportu,
  jen když na tomto počítači existuje) se stejným `started` má `verifyPending`.
- `BackupState.of(cardPaths)`: `failed` = cesty, jejichž nejnovější záznam selhal v kterékoli složce; `unverified` = cesty
  dobré všude, ale někde ne `readBack` (bez těch, které selhaly).

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/CardRecordTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class CardRecordTests: XCTestCase {
    /// Writes report `g` of `card` (destinations as given) into `<card>/_REPORT`.
    private func write(_ r: OffloadReport, card: String) throws {
        try FileManager.default.createDirectory(atPath: card + "/_REPORT", withIntermediateDirectories: true)
        try r.jsonData().write(to: URL(fileURLWithPath: card + "/_REPORT/" + OffloadReport.jsonName(r.generationNumber)))
    }

    private func report(card: String, gen: Int = 1, states: [String], verified: Bool = true, paths: [String]? = nil,
                        cardPaths: [String]? = nil, serials: [String?]? = nil) -> OffloadReport {
        var r = Reports.sample(verified: verified, states: states)
        r.generation = gen > 1 ? gen : nil
        let cps = cardPaths ?? [card]
        r.destinations = cps.enumerated().map { i, p in
            var d = OffloadReport.Destination(root: (p as NSString).deletingLastPathComponent, cardPath: p, filesystem: "apfs", filesOK: 2, filesFailed: 0)
            d.volumeSerial = serials?[i]
            return d
        }
        if let paths { for i in r.files.indices { r.files[i].path = paths[i] } }
        r.files = r.files.map { f in var x = f; x.destinations = Array(repeating: f.destinations[0], count: cps.count); x.errors = Array(repeating: nil, count: cps.count); return x }
        return r
    }

    func testNewestGenerationWinsPerPath() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try write(report(card: card, states: ["failed", "verified"]), card: card)
        try write(report(card: card, gen: 2, states: ["verified", "verified"], paths: ["CLIP/A&B.mov", "CLIP/new.mov"]), card: card)
        let rec = CardRecord.load(card, serial: .some(nil))
        XCTAssertEqual(rec.generations.map(\.generation), [1, 2])
        XCTAssertEqual(rec.files.count, 3)
        XCTAssertEqual(rec.files["clip/a&b.mov"]?.generation, 2)
        XCTAssertTrue(rec.failedFiles.isEmpty)
        XCTAssertTrue(rec.verified)
    }

    // Review focus 2.
    func testPendingOrIncompleteNeverVerified() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        var r = report(card: card, states: ["verified", "verified"])
        r.verifyPending = true
        try write(r, card: card)
        XCTAssertFalse(CardRecord.load(card, serial: .some(nil)).verified)
        r.verifyPending = nil
        r.verifyIncomplete = true
        try write(r, card: card)
        let rec = CardRecord.load(card, serial: .some(nil))
        XCTAssertTrue(rec.incomplete)
        XCTAssertFalse(rec.verified)
    }

    func testWrittenInVerifiedReportIsNotReadBackButFailedCountsAsFailed() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try write(report(card: card, states: ["written", "failed"]), card: card)
        let rec = CardRecord.load(card, serial: .some(nil))
        XCTAssertFalse(rec.verified)
        XCTAssertEqual(rec.failedFiles, ["CLIP/c 02.mov"])
        XCTAssertFalse(CardRecord.readBack(rec.files["clip/a&b.mov"]!, cardPath: card, serial: nil))
    }

    func testForeignPathsNeedEveryColumnVerified() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        var r = report(card: card, states: ["verified", "verified"], cardPaths: [#"D:\Z\2026-10-08\A001"#, #"E:\Z\2026-10-08\A001"#])
        r.files[0].destinations = ["verified", "written"]
        try write(r, card: card)
        let rec = CardRecord.load(card, serial: .some(nil))
        XCTAssertFalse(CardRecord.readBack(rec.files["clip/a&b.mov"]!, cardPath: card, serial: nil), "no column: every column must say verified")
        XCTAssertTrue(CardRecord.readBack(rec.files["clip/c 02.mov"]!, cardPath: card, serial: nil))
    }

    func testSerialMatchesWindowsReportOnSameDisk() throws {
        let root = tempDir()
        let card = root.sub("Z", "2026-10-08", "A001")
        var r = report(card: card, states: ["verified", "verified"], cardPaths: [#"D:\Z\2026-10-08\A001"#, #"E:\Z\2026-10-08\A001"#],
                       serials: ["1A2B-3C4D", "5E6F-7A8B"])
        r.files[0].destinations = ["written", "verified"]
        try write(r, card: card)
        // This folder sits under "/Z/2026-10-08/A001" of a disk whose serial is 5E6F-7A8B (the second column).
        let rec = CardRecord.load(card, serial: .some("5E6F-7A8B"))
        let column = CardRecord.columnOf(rec.files["clip/a&b.mov"]!.report, cardPath: "/Volumes/X/Z/2026-10-08/A001", serial: "5E6F-7A8B")
        XCTAssertEqual(column, 1)
        XCTAssertEqual(CardRecord.columnOf(r, cardPath: "/Volumes/X/Z/2026-10-08/A001", serial: nil), -1,
                       "the report knows serials, this folder's is unknown: no guess by path")
        XCTAssertEqual(CardRecord.columnOf(r, cardPath: "/Volumes/X/Other/A001", serial: "5E6F-7A8B"), -1)
    }

    func testReportMissingErrorAndProblems() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        XCTAssertTrue(CardRecord.load(card).reportMissing)
        try FileManager.default.createDirectory(atPath: card + "/_REPORT", withIntermediateDirectories: true)
        try Data("{".utf8).write(to: URL(fileURLWithPath: card + "/_REPORT/offload_report.json"))
        XCTAssertNotNil(CardRecord.load(card).error)
        try write(report(card: card, states: ["verified", "verified"]), card: card)
        try Data("{".utf8).write(to: URL(fileURLWithPath: card + "/_REPORT/offload_report_0002.json"))
        let rec = CardRecord.load(card)
        XCTAssertEqual(rec.problems.count, 1)
        XCTAssertFalse(rec.verified)
        XCTAssertEqual(rec.highestReportNumber, 2)
    }

    func testLeftoverNumberedReportCountsAsHighest() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try write(report(card: card, states: ["verified", "verified"]), card: card)
        try Data().write(to: URL(fileURLWithPath: card + "/_REPORT/A001_report_0003.html"))
        XCTAssertEqual(CardRecord.load(card).highestReportNumber, 3)
    }

    func testBackupStateAcrossFolders() throws {
        let root = tempDir()
        let d1 = root.sub("D", "2026-10-08", "A001"), d2 = root.sub("E", "2026-10-08", "A001")
        try write(report(card: d1, states: ["verified", "failed"]), card: d1)
        try write(report(card: d2, states: ["written", "verified"], verified: true), card: d2)
        XCTAssertEqual(BackupState.of([d1, d2]), BackupState(failed: 1, unverified: 1))
    }
}
```

Run: `swift test --filter CardRecordTests` → nesestaví se.

- [ ] **Step 2: Implementace** — `mac/Sources/OffloadKit/CardRecord.swift` (celý soubor):

```swift
import Foundation

public struct RecordedFile {
    public let file: OffloadReport.FileEntry
    public let generation: Int
    public let report: OffloadReport
}

/// What the reports in `<card>/_REPORT` say about a card folder — every generation, reports from Windows and older Macs
/// included. The truth about a backup (spec D.22.1): appends, repairs, the day report, clip lists and history read it.
public struct CardRecord {
    public static let mainReport = "offload_report.json"

    public var cardPath: String
    public var generations: [(generation: Int, report: OffloadReport)] = []
    /// Newest record per path; key = path lower-cased (APFS/NTFS/exFAT compare names in any case).
    public var files: [String: RecordedFile] = [:]
    public var clips: [ClipEntry] = []
    public var reportMissing = false
    public var error: String?
    public var problems: [String] = []
    public var highestReportNumber = 0
    public var volumeSerial: String?
    public var incomplete = false

    public var folderName: String { (cardPath.trimmingTrailingSlash() as NSString).lastPathComponent }
    public var started: Date? { generations.map(\.report.started).min() }
    public var finished: Date? { generations.map(\.report.finished).max() }
    public var project: String? { generations.compactMap { $0.report.project?.trimmingCharacters(in: .whitespaces) }.first { !$0.isEmpty } }
    public var verifyPending: Bool { generations.contains { $0.report.verifyPending == true } }
    public var failedFiles: [String] {
        files.values.filter { $0.report.isFailed($0.file) }.map(\.file.path).sorted()
    }
    public var verified: Bool {
        guard !generations.isEmpty, problems.isEmpty, !incomplete else { return false }
        if files.isEmpty { return generations.allSatisfy { $0.report.verified } }
        return files.values.allSatisfy { CardRecord.readBack($0, cardPath: cardPath, serial: volumeSerial) }
    }

    static let appendReport = try! NSRegularExpression(pattern: #"^offload_report_(\d{4})\.json$"#, options: .caseInsensitive)
    static let numberedFile = try! NSRegularExpression(pattern: #"_report_(\d{4})\.(json|txt|html|pdf)$"#, options: .caseInsensitive)

    static func number(_ re: NSRegularExpression, _ name: String) -> Int? {
        guard let m = re.firstMatch(in: name, range: NSRange(name.startIndex..., in: name)),
              let r = Range(m.range(at: 1), in: name) else { return nil }
        return Int(name[r])
    }

    /// `serial`: tests give the folder's volume serial; nil = read it (VolumeFacts), .some(nil) = unknown.
    public static func load(_ cardPath: String, maxGeneration: Int? = nil, serial: String?? = nil) -> CardRecord {
        var rec = CardRecord(cardPath: cardPath)
        let dir = cardPath + "/" + Layout.reportDirName
        let main = dir + "/" + mainReport
        guard FileManager.default.fileExists(atPath: main) else { rec.reportMissing = true; return rec }
        do {
            let r = try readChecked(main)
            rec.generations.append((r.generationNumber, r))
        } catch {
            rec.error = "\(error)"
            return rec
        }
        var highest = rec.generations[0].generation
        let names: [String]
        do { names = try FileManager.default.contentsOfDirectory(atPath: dir) } catch { rec.error = "\(error)"; return rec }
        let limit = maxGeneration ?? Int.max
        for name in names {
            if let n = number(numberedFile, name), n <= limit { highest = max(highest, n) }
            guard let n = number(appendReport, name), n <= limit else { continue }
            do {
                let r = try readChecked(dir + "/" + name)
                let g = r.generation ?? n
                rec.generations.append((g, r))
                highest = max(highest, g)
            } catch {
                rec.problems.append("\(name): \(error)")
            }
        }
        rec.generations.sort { $0.generation < $1.generation }
        rec.highestReportNumber = highest
        var clipAt: [String: Int] = [:]
        for g in rec.generations {
            for f in g.report.files { rec.files[f.path.lowercased()] = RecordedFile(file: f, generation: g.generation, report: g.report) }
            let analysed = !g.report.clips.isEmpty
            let clips = analysed ? g.report.clips
                : ClipDetector.detect(g.report.files.map { SourceFile(relPath: $0.path, size: $0.size, modified: $0.modified) }).map { ClipEntry(clip: $0) }
            for c in clips {
                let key = c.clip.path.lowercased()
                if let i = clipAt[key] {
                    if analysed || rec.clips[i].info == nil { rec.clips[i] = c }
                } else {
                    clipAt[key] = rec.clips.count
                    rec.clips.append(c)
                }
            }
        }
        rec.volumeSerial = serial ?? VolumeFacts.volumeSerialText(cardPath)
        rec.incomplete = incompleteElsewhere(cardPath, rec.generations)
        return rec
    }

    /// A report whose lists hold together (a hand-edited null must not crash later).
    static func readChecked(_ path: String) throws -> OffloadReport {
        let r = try OffloadReport.read(path)
        if r.files.contains(where: { $0.destinations.isEmpty && !r.destinations.isEmpty }) {
            throw OffloadError.failed("chybí seznam souborů nebo cílů")
        }
        return r
    }

    static func incompleteElsewhere(_ cardPath: String, _ gens: [(generation: Int, report: OffloadReport)]) -> Bool {
        guard let newest = gens.max(by: { $0.generation < $1.generation })?.report else { return false }
        if newest.verifyIncomplete == true { return true }
        let me = normal(cardPath)
        for d in newest.destinations where normal(d.cardPath) != me && d.cardPath.hasPrefix("/") {
            let json = d.cardPath + "/" + Layout.reportDirName + "/" + OffloadReport.jsonName(newest.generationNumber)
            if let other = try? OffloadReport.read(json), other.verifyPending == true, other.started == newest.started { return true }
        }
        return false
    }

    static func normal(_ p: String) -> String {
        (p.hasPrefix("/") ? URL(fileURLWithPath: p).standardizedFileURL.path : p).trimmingTrailingSlash().lowercased()
    }

    /// THE one definition of "verified for this destination folder" (spec D.22.6).
    public static func readBack(_ rec: RecordedFile, cardPath: String, serial: String?) -> Bool {
        let r = rec.report
        if r.verifyPending == true || r.verifyIncomplete == true { return false }
        let column = columnOf(r, cardPath: cardPath, serial: serial)
        let states: [String] = column >= 0
            ? [column < rec.file.destinations.count ? rec.file.destinations[column] : "pending"]
            : (rec.file.destinations.isEmpty ? ["pending"] : rec.file.destinations)
        return states.allSatisfy { $0 == "verified" || (r.verified && $0 != "written" && $0 != "pending") }
    }

    public static func columnOf(_ r: OffloadReport, cardPath: String, serial: String?) -> Int {
        let knowsSerials = r.destinations.contains { $0.volumeSerial != nil }
        if knowsSerials {
            guard let serial else { return -1 }
            let mine = VolumeFacts.underRoot(cardPath).lowercased()
            let hits = r.destinations.indices.filter {
                r.destinations[$0].volumeSerial == serial && VolumeFacts.underRoot(r.destinations[$0].cardPath).lowercased() == mine
            }
            return hits.count == 1 ? hits[0] : -1
        }
        let me = normal(cardPath)
        return r.destinations.firstIndex { normal($0.cardPath) == me } ?? -1
    }

    /// Which column belongs to `cardPath` for planning: the same path, else the only one, else the same position.
    public static func destinationIndex(_ r: OffloadReport, cardPath: String, position: Int) -> Int {
        let me = normal(cardPath)
        if let i = r.destinations.firstIndex(where: { normal($0.cardPath) == me }) { return i }
        return r.destinations.count == 1 ? 0 : min(max(position, 0), max(0, r.destinations.count - 1))
    }

    public static func stateOf(_ rec: RecordedFile, cardPath: String, position: Int) -> String {
        let c = destinationIndex(rec.report, cardPath: cardPath, position: position)
        return c < rec.file.destinations.count ? rec.file.destinations[c] : "pending"
    }
}

/// The whole backup as its card folders' reports record it now: failed paths, and paths good everywhere but not read
/// back somewhere (CardRecord.readBack).
public struct BackupState: Equatable {
    public let failed: Int
    public let unverified: Int

    public static func of(_ cardPaths: [String]) -> BackupState {
        var failed = Set<String>(), unverified = Set<String>()
        for p in cardPaths {
            let rec = CardRecord.load(p)
            failed.formUnion(rec.failedFiles.map { $0.lowercased() })
            for (key, f) in rec.files where !CardRecord.readBack(f, cardPath: p, serial: rec.volumeSerial) { unverified.insert(key) }
        }
        return BackupState(failed: failed.count, unverified: unverified.subtracting(failed).count)
    }
}
```

`SourceFile` dostane inicializátor `init(relPath:size:modified:)` (z `Date` do `timespec`; používá ho `CardRecord`
a historie). Do `Report.swift` přibude `isFailed` (B1: při `verified == false` jsou `written` i `verified` v pořádku) a
`failedFiles` ho použije:

```swift
    public func isFailed(_ f: FileEntry) -> Bool {
        f.sourceError != nil || f.destinations.contains { !OffloadReport.isGood($0, verified: verified) }
    }
    public var failedFiles: [FileEntry] { files.filter(isFailed) }
```

- [ ] **Step 3: Testy projdou** — Run: `swift test --filter CardRecordTests`, `swift test`.

- [ ] **Step 4: Záznam a commit** — `docs/VYVOJ-MAC.md`: `- Úkol 38 — CardRecord (všechny generace, reporty z Windows i Macu)
  a jediná definice „ověřeno“ (verifyPending/verifyIncomplete nikdy; cizí cesty = všechny sloupce; podle sériového čísla
  disku, R12); BackupState`. Run: `git add -A mac docs && git commit -m "Mac: CardRecord and the one definition of verified"`

---

### Task 39: Dva stavy karty — „Lze vyjmout“ × „Lze formátovat“

**Files:**
- Modify: `mac/Sources/OffloadKit/Job.swift` (`JobStatus.safeToEject`, nové `safeToFormat`, `warning`)
- Modify: `mac/Sources/OffloadKit/Desktop/Texts.swift`, `Desktop/JobProgress.swift` (`tone`, `isError`, `chime`)
- Modify: `mac/Sources/OffloadKit/HTMLReport.swift` (pruh „lze vysunout“), `mac/Sources/offload/main.swift` (exit kód, souhrn)
- Modify: `mac/Sources/OffloadApp/AppModel.swift`, `Views.swift` (`JobCard`: dva štítky, zvuk jen při `safeToFormat`)
- Create: `mac/Tests/OffloadKitTests/SafeStatesTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `OffloadReport.safeToFormat` (úkol 33), `BackupState` (úkol 38), `JobProgress`, `Texts` (úkol 8), `QueueRules.ejectProblem` (úkol 9).
- Produces:
  - `JobStatus.safeToEject: Bool` — **nově**: všechno zapsané (flush) na všech cílech; `JobStatus.safeToFormat: Bool` — vše
    ověřené všude a výsledek zapsaný; `JobStatus.warning: String?` (první varování, oranžově)
  - `Texts.safeToEject(_ disks: Int) -> String`, `Texts.safeToFormat(_ disks: Int) -> String`
  - `public struct CardChip: Equatable { text: String; tone: CardTone }`, `JobProgress.chips(_ s: JobStatus, contentConfirmed: Bool) -> (eject: CardChip, format: CardChip)`
  - `JobProgress.chime(_ s: JobStatus) -> Bool`

Pravidla (spec D.22.7; Windows R5, ruling I2):

- „Lze vyjmout“ = všechny soubory zapsané a vyprázdněné na všech cílech (`report.success`); ověření může ještě běžet
  (v plánu 10) nebo být vypnuté. Tlačítko „Vysunout kartu“ se smí nabídnout po `safeToEject` (pravidla úkolu 9 dál platí).
- „Lze formátovat“ = `report.safeToFormat` **a** pro doplnění/opravu celá záloha (`BackupState`: 0 selhaných, 0 neověřených)
  **a** na kartě není soubor, který záloha nemá. Jen tehdy: zelená, zvuk úspěchu (`Glass`), oznámení „hotovo“ (úkol 59),
  zelený řádek historie, CLI kód 0 po kopii s ověřením. Varování (`warning`) bere zelenou i zvuk (oranžová, „vyžaduje pozornost“).
- Texty (doslova Windows `Texts.cs`, `JobCard.cs`):
  - `Texts.safeToEject(n)` = „Všechny soubory jsou na {n} {disku|discích} — kartu lze vyjmout. Formátovat až po ověření.“
  - `Texts.safeToFormat(n)` = „Všechny soubory jsou na {n} {disku|discích} a ověřené — kartu lze vyjmout i formátovat.“
  - štítky: formátovat — `Lze formátovat` (zelená; s vadami obsahu nepotvrzenými `Lze formátovat po kontrole vad` žlutě —
    úkol 70), běží: `Ověřuje se…` (po vyjmutí) / `Formátovat zatím ne`, skončilo: `Neformátovat` (žlutě po vyjmutí, jinak
    červeně); vyjmout — `Lze vyjmout` (zelená jen spolu s „Lze formátovat“, jinak neutrální), běží: `Vyjmout zatím ne`,
    skončilo: `Nevyjímat` (červeně). Čekající úloha štítky nemá.
- Dosavadní zelený pruh `Texts.safeToEject` (úkol 8, „kartu lze vysunout“) se mapuje na `safeToFormat`; HTML report
  zeleně „kartu lze vyjmout i formátovat“ jen při `report.safeToFormat`.
- CLI `copy`/`append`: kód 1, když kopie skončila bez chyby, ale kartu nelze formátovat (výsledek ověření nezapsán, neověřené
  dřívější soubory); `--no-verify` zůstává 0 (uživatel si ověření vypnul) — jako Windows 0.6.0.

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/SafeStatesTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class SafeStatesTests: XCTestCase {
    private func status(_ phase: JobStatus.Phase, eject: Bool, format: Bool, warning: String? = nil) -> JobStatus {
        var s = JobStatus()
        s.phase = phase; s.safeToEject = eject; s.safeToFormat = format; s.warning = warning
        return s
    }

    func testTexts() {
        XCTAssertEqual(Texts.safeToEject(1), "Všechny soubory jsou na 1 disku — kartu lze vyjmout. Formátovat až po ověření.")
        XCTAssertEqual(Texts.safeToFormat(2), "Všechny soubory jsou na 2 discích a ověřené — kartu lze vyjmout i formátovat.")
    }

    // Review focus 2: green only when the card may be formatted.
    func testGreenOnlyWhenSafeToFormat() {
        let done = JobProgress.chips(status(.done, eject: true, format: true), contentConfirmed: true)
        XCTAssertEqual(done.format, CardChip(text: "Lze formátovat", tone: .ok))
        XCTAssertEqual(done.eject, CardChip(text: "Lze vyjmout", tone: .ok))
        let notVerified = JobProgress.chips(status(.done, eject: true, format: false), contentConfirmed: true)
        XCTAssertEqual(notVerified.eject.tone, .waiting, "Lze vyjmout alone is neutral")
        XCTAssertEqual(notVerified.format, CardChip(text: "Neformátovat", tone: .warning))
        let failed = JobProgress.chips(status(.done, eject: false, format: false), contentConfirmed: true)
        XCTAssertEqual(failed.eject, CardChip(text: "Nevyjímat", tone: .error))
        let running = JobProgress.chips(status(.verifying, eject: true, format: false), contentConfirmed: true)
        XCTAssertEqual(running.format.text, "Ověřuje se…")
    }

    func testChimeOnlyWhenFormattableAndNoWarning() {
        XCTAssertTrue(JobProgress.chime(status(.done, eject: true, format: true)))
        XCTAssertFalse(JobProgress.chime(status(.done, eject: true, format: false)))
        XCTAssertFalse(JobProgress.chime(status(.done, eject: true, format: true, warning: "3 soubory z dřívější zálohy nebyly ověřeny")))
        XCTAssertFalse(JobProgress.chime(status(.cancelled, eject: true, format: true)))
    }

    func testCopyWithoutVerifyIsEjectableNotFormattable() throws {
        let dir = tempDir()
        let card = dir.sub("card"); let dest = dir.sub("dest")
        _ = TestFS.writeRandom(card + "/A.bin", size: 1000, seed: 1)
        try FileManager.default.createDirectory(atPath: dest, withIntermediateDirectories: true)
        var cfg = JobConfig(source: card, destRoots: [dest]); cfg.verify = false; cfg.analyzeClips = false
        var o = OffloadJob.engineOptions(cfg); o.allowSourceVolume = true
        let job = OffloadJob(config: cfg, options: o)
        job.run()
        XCTAssertTrue(job.status().safeToEject)
        XCTAssertFalse(job.status().safeToFormat)
    }
}
```

Run: `swift test --filter SafeStatesTests` → nesestaví se.

- [ ] **Step 2: Implementace**

`Job.swift` — `JobStatus`:

```swift
    /// Everything is written (and flushed) on every destination: the card may be taken out (Windows R5 "Lze vyjmout").
    public var safeToEject = false
    /// Every file verified on every destination and the result written everywhere (R5 "Lze formátovat"); for an append
    /// or a repair the whole backup (BackupState). The only state that is green.
    public var safeToFormat = false
    /// The first warning (orange, "vyžaduje pozornost"): no chime, no "hotovo".
    public var warning: String?

    mutating func addWarning(_ w: String) { if warning == nil { warning = w } }
```

V `OffloadJob.run` po kopii (dnešní místo, kde se nastavuje `safeToEject = report.safeToEject`):

```swift
            update {
                $0.safeToEject = report.success
                $0.safeToFormat = report.safeToFormat
                $0.copySucceeded = report.success
            }
```

(Úkoly 47 a 62 doplní celou zálohu přes `BackupState` a „soubory, které záloha nemá“.)

`Texts.swift`: `safeToEject(_:)` a `safeToFormat(_:)` s texty výše (`plural(disks, "disku", "discích", "discích")`).

`JobProgress.swift`:

```swift
public struct CardChip: Equatable {
    public let text: String
    public let tone: CardTone
}

extension JobProgress {
    /// The card row's two verdicts, never merged (R5). Green only where the card may be formatted (ruling I2).
    public static func chips(_ s: JobStatus, contentConfirmed: Bool = true, contentWarnings: Int = 0) -> (eject: CardChip, format: CardChip) {
        let running = s.phase != .waiting && !s.phase.isFinished
        let format: CardChip = s.safeToFormat
            ? (contentWarnings > 0 && !contentConfirmed ? CardChip(text: "Lze formátovat po kontrole vad", tone: .warning) : CardChip(text: "Lze formátovat", tone: .ok))
            : running ? CardChip(text: s.safeToEject ? "Ověřuje se…" : "Formátovat zatím ne", tone: .waiting)
            : CardChip(text: "Neformátovat", tone: s.safeToEject ? .warning : .error)
        let eject: CardChip = s.safeToEject ? CardChip(text: "Lze vyjmout", tone: format.tone == .ok ? .ok : .waiting)
            : running ? CardChip(text: "Vyjmout zatím ne", tone: .waiting) : CardChip(text: "Nevyjímat", tone: .error)
        return (eject, format)
    }

    /// The success sound: formattable everywhere and nothing to look at.
    public static func chime(_ s: JobStatus) -> Bool { s.safeToFormat && s.warning == nil && s.phase != .cancelled && s.phase != .failed }
}
```

`tone` karty úlohy: `.ok` jen při `chime`; hotová úloha s `safeToEject && !safeToFormat` nebo s `warning` → `.warning`.
`Views.swift` (`JobCard`): místo jednoho zeleného pruhu dva štítky (`chips`) vedle sebe + text `Texts.safeToFormat` (zeleně)
nebo `Texts.safeToEject` (neutrálně) a `warning` oranžově; `AppModel`: `NSSound(named: "Glass")` jen při `chime`, „Basso“
při chybě (beze změny); `requestUserAttention` zůstává. CLI `main.swift` (`Copy.run`): souhrn řádkem `Texts.safeToFormat`
nebo `Texts.safeToEject`, kód 1 podle pravidla výše.

`HTMLReport.swift`: zelený pruh jen při `report.safeToFormat` s textem „Všechny soubory jsou na N discích a ověřené — kartu
lze vyjmout i formátovat.“; jinak při `success` šedě „… — kartu lze vyjmout. Formátovat až po ověření.“

- [ ] **Step 3: Testy projdou** — Run: `swift test` (upravit očekávání starších testů úkolů 8 a 11, které čekaly
  „kartu lze vysunout“ — význam „ověřeno“ má teď `safeToFormat`).

- [ ] **Step 4: Ruční kontrola** — kopie na dva cíle s ověřením → oba štítky zeleně, zvuk; s vypnutým ověřením → „Lze
  vyjmout“ neutrálně, „Neformátovat“ žlutě, bez zvuku úspěchu; CLI `--no-verify` exit 0.

- [ ] **Step 5: Záznam a commit** — `docs/VYVOJ-MAC.md`: `- Úkol 39 — dva stavy karty: Lze vyjmout (vše zapsané) × Lze
  formátovat (vše ověřené a zapsané); zelená, zvuk a CLI 0 jen při Lze formátovat`; `docs/ROZDILY.md`: řádek „Lze vyjmout ×
  Lze formátovat“ Mac ✅ (bez ověření během proxy — úkol 67); záměrný rozdíl „Lze vyjmout × Lze formátovat“ upravit.
  Run: `git add -A mac docs && git commit -m "Mac: two card states — safe to eject vs safe to format"`

---

### Task 40: ASC MHL — čtení všech generací a zápis generace N

**Files:**
- Create: `mac/Sources/OffloadKit/MHLReader.swift`
- Modify: `mac/Sources/OffloadKit/MHL.swift` (celý `write`, nové `entries`, `hasGeneration`)
- Create: `mac/Tests/OffloadKitTests/MHLTests.swift`, `mac/Tests/OffloadKitTests/Fixtures/ascmhl-two-generations/` (viz krok 1)
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `OffloadReport` (úkol 33), `Msg.mhlRepairComment` (úkol 34), `IO.writeFileNew` (úkol 33), `MHL.c4`, `isoDate`, `esc`.
- Produces:
  - `struct MHLHash { algorithm, value, action: String }`, `struct MHLFileRecord { path: String; size: Int64?; modified: String?; hashes: [MHLHash]; generation: Int; var failed: Bool }`
  - `struct MHLGeneration { seq: Int; file: String; files: [MHLFileRecord]; ignore: [String] }`, `struct MHLChainEntry: Equatable { seq: Int; path: String; c4: String }`
  - `struct MHLHistory { generations; chain; files: [String: MHLFileRecord]; lastGood: [String: MHLFileRecord]; ignore: [String]; chainProblems: [String]; var ignoresReportFolder: Bool }`
  - `enum MHLReader { static func read(_ cardPath: String) -> MHLHistory?; static func readChain(_ ascmhlDir: String) -> [MHLChainEntry]?; static func parseGeneration(_ path: String, seq: Int) throws -> MHLGeneration }`
  - `MHL.write(report:cardPath:now:host:) throws` (generace podle `report.generationNumber`), `MHL.hasGeneration(_:cardPath:) throws -> Bool`,
    `MHL.entries(_ report: OffloadReport, destination d: Int) -> [(file: OffloadReport.FileEntry, action: String)]`

Pravidla (FORMATY „ASC MHL v2“, „Doplnění“, „Oprava“; spec D.22.3):

- **Čtení:** `ascmhl/ascmhl_chain.xml` určuje pořadí a C4 každé generace. Problémy (česky, do `chainProblems`): generace
  v řetězu bez souboru — „V řetězu ASC MHL je generace 0002, ale její soubor chybí: <jméno>“; jiné C4 — „ASC MHL generace 0002
  neodpovídá řetězu (soubor MHL byl změněn).“; `.mhl` mimo řetěz — „Soubor ASC MHL není v řetězu: <jméno>“; chybí řetěz, ale
  jsou `.mhl` — „Chybí ascmhl_chain.xml — generace se čtou podle čísla v názvu souboru.“; nečitelná generace — „ASC MHL
  generaci 0002 nejde přečíst: <důvod>“; nečitelný řetěz — „ascmhl_chain.xml nejde přečíst: <důvod>“. Bez `ascmhl/` nebo bez
  `.mhl` → `nil`. Hashe podle prvku (`xxh64`, `md5`, `sha1`, `xxh3`, `xxh128`, `c4`) s `action`; jen přímé děti `<hash>`
  (ne `<directoryhash>`). `files` = pro každou cestu nejnovější generace, která ji uvádí; `lastGood` = nejnovější záznam,
  který není `failed`. Cesty se klíčují malými písmeny.
- **Zápis generace N** (Windows `Mhl.Write` 1:1):
  - generace 1 **nikdy přes existující řetěz** (`IOError` „ASC MHL v <složka> už má řetěz — generace 1 se nezapsala.“);
    generace N > 1 jen když poslední generace v řetězu je N − 1 (jinak „ASC MHL v <složka> se během doplnění změnil (poslední
    generace X, doplnění píše N) — generace se nezapsala.“);
  - soubor `NNNN_<složka karty>_<yyyy-MM-dd_HHmmss>Z.mhl` (čas v UTC); generace N > 1 přes `writeFileNew` (nikdy nenahradí);
  - `<comment>`: oprava `Msg.mhlRepairComment`, doplnění „Offload doplnění: 12 nových souborů z <zdroj>“ (1 nový soubor,
    2–4 nové soubory, 5+ nových souborů), generace 1 „Offload of <karta> from <zdroj>“; s metodou ověření cíle navíc
    „ · ověření: verify-in-proxy“ / „ · ověření: read-back“ (`<process>` zůstává `transfer` — schéma nic jiného nedovolí);
  - `<ignore>` = vzory **generace 1** (doplnění je převezme), u generace 1 `.DS_Store`, `ascmhl`, `ascmhl/`, `_REPORT`, `_PROXY`
    + jména vynechaných systémových souborů;
  - `<hash>`: `<xxh64>`, `<xxh128>` (když je), `<md5>` (když je), stejné `action` a `hashdate`. Akce (`entries`): u opravy
    (`rewritten` je) — `pending` a dobrá kopie, která v této generaci nebyla přečtena, **vynechat**; `failed` → `failed`;
    nová kopie → `original`; přečtená dobrá → `verified`. Jinak: `written` v **ověřené** generaci se vynechá (do generace jde
    jen ověřený nebo selhaný soubor), `verified`/`written` → `original`, ostatní → `failed`;
  - `ascmhl_chain.xml` = dřívější záznamy **tak, jak byly přečtené** (stejné `sequencenr`, cesta, C4) + nový; zapisuje se
    atomicky (dočasné jméno + `rename`). Zápis řetězu je **bod potvrzení** generace.
- `hasGeneration`: řetěz už má generaci reportu → soubor existuje, C4 sedí a obsahuje přesně tytéž cesty, xxh64 a akce →
  `true`; jinak chyba (nic se nepřepíše); řetěz ji ještě nemá → `false`. Pro opakovaný zápis výsledku ověření (úkol 67).

- [ ] **Step 1: Fixture** — `mac/Tests/OffloadKitTests/Fixtures/ascmhl-two-generations/`: složka karty se dvěma generacemi tak,
  jak ji zapsal Windows 0.6.0 (generace 1 + doplnění generace 2; zkopírovat z Windows PC příkazem
  `bin\offload.exe copy` + `append` nad malou složkou se dvěma soubory, nebo z testovacích fixture Windows `MhlReaderTests`).
  Obsah: `A.bin`, `B.bin`, `_REPORT/offload_report.json`, `_REPORT/offload_report_0002.json`, `_REPORT/<karta>.xxh64`,
  `ascmhl/0001_….mhl`, `ascmhl/0002_….mhl`, `ascmhl/ascmhl_chain.xml`. Není-li Windows po ruce, vytvoří fixture test sám
  (Mac `copy` + ruční druhá generace přes `MHL.write`) a fixture z Windows se doplní později — **tvar** generace pak hlídá
  `testGenerationFileShapeMatchesWindowsFixture` jen s Windows soubory (bez nich `XCTSkip`).

- [ ] **Step 2: Padající testy** — `mac/Tests/OffloadKitTests/MHLTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class MHLTests: XCTestCase {
    private func card(_ dir: TempDir, files: [String: String]) throws -> String {
        let card = dir.sub("D", "2026-10-08", "A001")
        for (p, text) in files {
            try FileManager.default.createDirectory(atPath: ((card + "/" + p) as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
            try Data(text.utf8).write(to: URL(fileURLWithPath: card + "/" + p))
        }
        return card
    }

    private func report(_ card: String, gen: Int, files: [(String, String)], states: [String]? = nil, verified: Bool = true) -> OffloadReport {
        var r = Reports.sample(verified: verified)
        r.generation = gen > 1 ? gen : nil
        r.destinations = [OffloadReport.Destination(root: "/r", cardPath: card, filesystem: "apfs", filesOK: files.count, filesFailed: 0)]
        r.files = files.enumerated().map { i, f in
            OffloadReport.FileEntry(path: f.0, size: Int64(f.1.utf8.count), modified: Reports.t0, xxh64: hex64(XXH64Once(f.1)),
                                    xxh128: "0123456789abcdef0123456789abcdef", sourceError: nil,
                                    destinations: [states?[i] ?? (verified ? "verified" : "written")], errors: [nil])
        }
        return r
    }

    func XXH64Once(_ s: String) -> UInt64 { let h = XXH64Stream(); Data(s.utf8).withUnsafeBytes { h.update($0.baseAddress!, $0.count) }; return h.digest() }

    func testGenerationOneThenTwoAndChain() throws {
        let card = try card(tempDir(), files: ["A.bin": "a", "B.bin": "b"])
        try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a")]), cardPath: card)
        try MHL.write(report: report(card, gen: 2, files: [("B.bin", "b")]), cardPath: card)
        let h = try XCTUnwrap(MHLReader.read(card))
        XCTAssertEqual(h.chain.map(\.seq), [1, 2])
        XCTAssertTrue(h.chainProblems.isEmpty, "\(h.chainProblems)")
        XCTAssertEqual(h.files["a.bin"]?.generation, 1)
        XCTAssertEqual(h.files["b.bin"]?.generation, 2)
        XCTAssertTrue(h.ignoresReportFolder)
        let gen2 = try String(contentsOfFile: card + "/ascmhl/" + h.chain[1].path, encoding: .utf8)
        XCTAssertTrue(gen2.contains("<comment>Offload doplnění: 1 nový soubor z /Volumes/A001</comment>"))
        XCTAssertTrue(gen2.contains("<xxh128 action=\"original\""))
        XCTAssertTrue(gen2.contains("<pattern>_REPORT</pattern>"), "ignore patterns of generation 1")
    }

    func testGenerationOneNeverOverAChainAndGapRefused() throws {
        let card = try card(tempDir(), files: ["A.bin": "a"])
        try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a")]), cardPath: card)
        XCTAssertThrowsError(try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a")]), cardPath: card))
        XCTAssertThrowsError(try MHL.write(report: report(card, gen: 3, files: [("A.bin", "a")]), cardPath: card), "2 is missing")
    }

    func testWrittenInVerifiedGenerationIsLeftOutFailedIsFailed() throws {
        let card = try card(tempDir(), files: ["A.bin": "a", "B.bin": "b"])
        try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a"), ("B.bin", "b")], states: ["written", "failed"]), cardPath: card)
        let h = try XCTUnwrap(MHLReader.read(card))
        XCTAssertNil(h.files["a.bin"], "written but the generation was verified: not in it")
        XCTAssertEqual(h.files["b.bin"]?.hashes.first?.action, "failed")
    }

    func testRepairActions() throws {
        var r = report("/c", gen: 3, files: [("A.bin", "a"), ("B.bin", "b"), ("C.bin", "c")])
        r.kind = "repair"
        r.destinations.append(OffloadReport.Destination(root: "/r2", cardPath: "/c2", filesystem: "apfs", filesOK: 3, filesFailed: 0))
        r.files[0].destinations = ["verified", "verified"]; r.files[0].rewritten = [true, false]
        r.files[1].destinations = ["failed", "verified"]; r.files[1].rewritten = [true, false]
        r.files[2].destinations = ["pending", "written"]; r.files[2].rewritten = [false, false]
        XCTAssertEqual(MHL.entries(r, destination: 0).map { "\($0.file.path)=\($0.action)" }, ["A.bin=original", "B.bin=failed"])
        XCTAssertEqual(MHL.entries(r, destination: 1).map { "\($0.file.path)=\($0.action)" }, ["A.bin=verified", "B.bin=verified"],
                       "a kept copy not read back in this generation is not in it")
    }

    func testChainProblems() throws {
        let card = try card(tempDir(), files: ["A.bin": "a"])
        try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a")]), cardPath: card)
        let chain = try XCTUnwrap(MHLReader.readChain(card + "/ascmhl"))
        let file = card + "/ascmhl/" + chain[0].path
        try (try String(contentsOfFile: file, encoding: .utf8)).replacingOccurrences(of: "A.bin", with: "X.bin")
            .write(toFile: file, atomically: true, encoding: .utf8)
        XCTAssertEqual(MHLReader.read(card)?.chainProblems, ["ASC MHL generace 0001 neodpovídá řetězu (soubor MHL byl změněn)."])
        try Data().write(to: URL(fileURLWithPath: card + "/ascmhl/0009_x.mhl"))
        XCTAssertTrue(MHLReader.read(card)!.chainProblems.contains("Soubor ASC MHL není v řetězu: 0009_x.mhl"))
    }

    func testHasGenerationIsIdempotentAndRefusesAnother() throws {
        let card = try card(tempDir(), files: ["A.bin": "a"])
        let r = report(card, gen: 1, files: [("A.bin", "a")])
        XCTAssertFalse(try MHL.hasGeneration(r, cardPath: card))
        try MHL.write(report: r, cardPath: card)
        XCTAssertTrue(try MHL.hasGeneration(r, cardPath: card))
        var other = r
        other.files[0].xxh64 = "0000000000000000"
        XCTAssertThrowsError(try MHL.hasGeneration(other, cardPath: card))
    }

    // Review focus 6.
    func testGenerationFileShapeMatchesWindowsFixture() throws {
        let fixture = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("Fixtures/ascmhl-two-generations").path
        guard let h = MHLReader.read(fixture), h.chain.count == 2 else { throw XCTSkip("Windows fixture not in the repo yet") }
        XCTAssertTrue(h.chainProblems.isEmpty, "\(h.chainProblems)")
        XCTAssertTrue(h.ignoresReportFolder)
        XCTAssertEqual(h.generations[1].files.first?.hashes.map(\.algorithm).sorted(), ["xxh128", "xxh64"])
    }
}
```

Run: `swift test --filter MHLTests` → nesestaví se.

- [ ] **Step 3: Čtení** — `mac/Sources/OffloadKit/MHLReader.swift`:

```swift
import Foundation

struct MHLHash: Equatable { let algorithm: String; let value: String; let action: String }

struct MHLFileRecord {
    let path: String
    let size: Int64?
    let modified: String?
    let hashes: [MHLHash]
    let generation: Int
    var failed: Bool { hashes.contains { $0.action == "failed" } }
}

struct MHLGeneration { let seq: Int; let file: String; let files: [MHLFileRecord]; let ignore: [String] }
struct MHLChainEntry: Equatable { let seq: Int; let path: String; let c4: String }

struct MHLHistory {
    var generations: [MHLGeneration] = []
    var chain: [MHLChainEntry] = []
    /// Newest record per path (key lower-cased).
    var files: [String: MHLFileRecord] = [:]
    /// Newest record per path that is not "failed" — what a file whose newest record failed is verified against.
    var lastGood: [String: MHLFileRecord] = [:]
    var ignore: [String] = []
    var chainProblems: [String] = []
    /// A report written into `<card>/_REPORT` does not break `ascmhl verify` (R8 exception, spec D.22.4).
    var ignoresReportFolder: Bool { BackupVerifier.ignored(Layout.reportDirName + "/x", ignore) }
}

/// One XML file's elements (a tiny tree is enough: MHL files are flat and small).
final class XMLNode {
    let name: String
    var attributes: [String: String]
    var text = ""
    var children: [XMLNode] = []
    init(name: String, attributes: [String: String]) { self.name = name; self.attributes = attributes }

    func child(_ n: String) -> XMLNode? { children.first { $0.name == n } }
    func all(_ n: String) -> [XMLNode] { children.filter { $0.name == n } }

    static func parse(_ data: Data) throws -> XMLNode {
        final class Builder: NSObject, XMLParserDelegate {
            var stack: [XMLNode] = [XMLNode(name: "#doc", attributes: [:])]
            func parser(_ p: XMLParser, didStartElement e: String, namespaceURI: String?, qualifiedName: String?, attributes a: [String: String]) {
                let n = XMLNode(name: e, attributes: a)
                stack.last!.children.append(n)
                stack.append(n)
            }
            func parser(_ p: XMLParser, didEndElement e: String, namespaceURI: String?, qualifiedName: String?) { stack.removeLast() }
            func parser(_ p: XMLParser, foundCharacters s: String) { stack.last!.text += s }
        }
        let p = XMLParser(data: data)
        let b = Builder()
        p.delegate = b
        guard p.parse(), let root = b.stack.first?.children.first else {
            throw OffloadError.failed(p.parserError.map { "\($0.localizedDescription)" } ?? "není to XML")
        }
        return root
    }
}

enum MHLReader {
    static let chainFileName = "ascmhl_chain.xml"
    static func gen(_ n: Int) -> String { String(format: "%04d", n) }

    static func read(_ cardPath: String) -> MHLHistory? {
        let dir = cardPath + "/" + MHL.dirName
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: dir) else { return nil }
        let mhlFiles = names.filter { $0.lowercased().hasSuffix(".mhl") }.sorted()
        guard !mhlFiles.isEmpty else { return nil }
        var h = MHLHistory()
        var order: [(Int, String)] = []
        if FileManager.default.fileExists(atPath: dir + "/" + chainFileName) {
            var problems: [String] = []
            let chain = readChain(dir, problems: &problems) ?? []
            h.chainProblems += problems
            h.chain = chain
            for c in chain {
                let path = dir + "/" + c.path
                guard !c.path.contains("/"), !c.path.contains("\\"), let data = FileManager.default.contents(atPath: path) else {
                    h.chainProblems.append("V řetězu ASC MHL je generace \(gen(c.seq)), ale její soubor chybí: \(c.path)")
                    continue
                }
                if MHL.c4(data) != c.c4 { h.chainProblems.append("ASC MHL generace \(gen(c.seq)) neodpovídá řetězu (soubor MHL byl změněn).") }
                order.append((c.seq, c.path))
            }
            let inChain = Set(chain.map { $0.path.lowercased() })
            for extra in mhlFiles where !inChain.contains(extra.lowercased()) { h.chainProblems.append("Soubor ASC MHL není v řetězu: \(extra)") }
        } else {
            h.chainProblems.append("Chybí \(chainFileName) — generace se čtou podle čísla v názvu souboru.")
            for f in mhlFiles { if let n = Int(f.prefix(4)) { order.append((n, f)) } }
        }
        for (seq, file) in order.sorted(by: { $0.0 < $1.0 }) {
            do {
                let g = try parseGeneration(dir + "/" + file, seq: seq)
                h.generations.append(g)
                h.ignore += g.ignore
                for f in g.files {
                    h.files[f.path.lowercased()] = f
                    if !f.failed { h.lastGood[f.path.lowercased()] = f }
                }
            } catch {
                h.chainProblems.append("ASC MHL generaci \(gen(seq)) nejde přečíst: \(error)")
            }
        }
        return h
    }

    static func readChain(_ dir: String) -> [MHLChainEntry]? {
        var ignored: [String] = []
        return readChain(dir, problems: &ignored)
    }

    static func readChain(_ dir: String, problems: inout [String]) -> [MHLChainEntry]? {
        guard let data = FileManager.default.contents(atPath: dir + "/" + chainFileName) else { return nil }
        do {
            let root = try XMLNode.parse(data)
            return root.all("hashlist").compactMap { e in
                guard let seq = Int(e.attributes["sequencenr"] ?? ""), let p = e.child("path")?.text.trimmingCharacters(in: .whitespacesAndNewlines),
                      let c4 = e.child("c4")?.text.trimmingCharacters(in: .whitespacesAndNewlines) else { return nil }
                return MHLChainEntry(seq: seq, path: p, c4: c4)
            }
        } catch {
            problems.append("\(chainFileName) nejde přečíst: \(error)")
            return nil
        }
    }

    static func parseGeneration(_ path: String, seq: Int) throws -> MHLGeneration {
        guard let data = FileManager.default.contents(atPath: path) else { throw IOError("read", path, ENOENT) }
        let root = try XMLNode.parse(data)
        let ignore = root.child("processinfo")?.child("ignore")?.all("pattern").map { $0.text.trimmingCharacters(in: .whitespacesAndNewlines) } ?? []
        let files: [MHLFileRecord] = (root.child("hashes")?.all("hash") ?? []).compactMap { h in
            guard let p = h.child("path") else { return nil }
            let hashes = h.children.filter { HashAlgo.known.contains($0.name.lowercased()) }.map {
                MHLHash(algorithm: $0.name.lowercased(), value: $0.text.trimmingCharacters(in: .whitespacesAndNewlines), action: $0.attributes["action"] ?? "")
            }
            return MHLFileRecord(path: p.text.trimmingCharacters(in: .whitespacesAndNewlines), size: p.attributes["size"].flatMap { Int64($0) },
                                 modified: p.attributes["lastmodificationdate"], hashes: hashes, generation: seq)
        }
        return MHLGeneration(seq: seq, file: (path as NSString).lastPathComponent, files: files, ignore: ignore)
    }
}
```

(`BackupVerifier.ignored` — porovnání se vzory ASC MHL — přidá úkol 45; do té doby v tomto souboru dočasná funkce
`ignored(_:_:)` se stejným tělem jako v úkolu 45, úkol 45 ji přesune.)

- [ ] **Step 4: Zápis** — v `MHL.swift` nahradit `write` celé:

```swift
    static func write(report: OffloadReport, cardPath: String, now: Date = Date(), host: String = ProcessInfo.processInfo.hostName) throws {
        guard let d = report.destinations.firstIndex(where: { $0.cardPath == cardPath }) else { return }
        let dir = cardPath + "/" + dirName
        try IO.mkdirs(dir)
        let generation = report.generationNumber
        if generation == 1 && FileManager.default.fileExists(atPath: dir + "/" + MHLReader.chainFileName) {
            throw IOError(message: "ASC MHL v \(cardPath) už má řetěz — generace 1 se nezapsala.")
        }
        let earlier = generation > 1 ? MHLReader.read(cardPath) : nil
        if generation > 1 {
            let last = earlier?.chain.map(\.seq).max() ?? 0
            guard last == generation - 1 else {
                throw IOError(message: "ASC MHL v \(cardPath) se během doplnění změnil (poslední generace \(last), doplnění píše \(generation)) — generace se nezapsala.")
            }
        }
        let stamp = isoDate(now)
        let utc = DateFormatter()
        utc.locale = Locale(identifier: "en_US_POSIX")
        utc.timeZone = TimeZone(identifier: "UTC")
        utc.dateFormat = "yyyy-MM-dd_HHmmss'Z'"
        let folder = (cardPath.trimmingTrailingSlash() as NSString).lastPathComponent
        let fileName = String(format: "%04d_%@_%@.mhl", generation, folder, utc.string(from: now))
        let listed = report.files.filter { $0.xxh64 != nil }.count
        let copied = report.files.filter { $0.xxh64 != nil && ($0.rewritten?.contains(true) ?? false) }.count
        var comment = report.isRepair ? Msg.mhlRepairComment(copied: copied, readBack: listed - copied, source: report.source)
            : generation > 1 ? "Offload doplnění: \(listed) \(plural(listed, "nový soubor", "nové soubory", "nových souborů")) z \(report.source)"
            : "Offload of \(report.cardName) from \(report.source)"
        if let method = report.destinations[d].verifyMethod { comment += " · ověření: \(method)" }
        let ignore = earlier?.generations.first?.ignore ?? (ignorePatterns + report.skipped.map { ($0 as NSString).lastPathComponent })

        var x = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<hashlist version=\"2.0\" xmlns=\"urn:ASC:MHL:v2.0\">\n  <creatorinfo>\n"
        x += "    <creationdate>\(stamp)</creationdate>\n    <hostname>\(esc(host))</hostname>\n"
        x += "    <tool version=\"\(esc(report.version))\">\(esc(report.tool))</tool>\n    <comment>\(esc(comment))</comment>\n"
        x += "  </creatorinfo>\n  <processinfo>\n    <process>transfer</process>\n    <ignore>\n"
        for p in ignore { x += "      <pattern>\(esc(p))</pattern>\n" }
        x += "    </ignore>\n  </processinfo>\n  <hashes>\n"
        for (f, action) in entries(report, destination: d) {
            x += "    <hash>\n      <path size=\"\(f.size)\" lastmodificationdate=\"\(isoDate(f.modified))\">\(esc(f.path))</path>\n"
            x += "      <xxh64 action=\"\(action)\" hashdate=\"\(stamp)\">\(f.xxh64!)</xxh64>\n"
            if let v = f.xxh128 { x += "      <xxh128 action=\"\(action)\" hashdate=\"\(stamp)\">\(v)</xxh128>\n" }
            if let v = f.md5 { x += "      <md5 action=\"\(action)\" hashdate=\"\(stamp)\">\(v)</md5>\n" }
            x += "    </hash>\n"
        }
        x += "  </hashes>\n</hashlist>\n"
        let data = Data(x.utf8)
        if generation > 1 { try IO.writeFileNew(dir + "/" + fileName, data) } else { try IO.writeFileDurably(dir + "/" + fileName, data) }

        // The chain: every earlier generation exactly as read, then this one — written atomically: the commit point.
        var chain = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ascmhldirectory xmlns=\"urn:ASC:MHL:DIRECTORY:v2.0\">\n"
        for e in (earlier?.chain ?? []) + [MHLChainEntry(seq: generation, path: fileName, c4: c4(data))] {
            chain += "  <hashlist sequencenr=\"\(e.seq)\">\n    <path>\(esc(e.path))</path>\n    <c4>\(esc(e.c4))</c4>\n  </hashlist>\n"
        }
        chain += "</ascmhldirectory>\n"
        try IO.writeFileDurably(dir + "/" + MHLReader.chainFileName, Data(chain.utf8))
    }

    /// The files generation `report` records for destination `d`, with their action (Windows Mhl.Entries).
    static func entries(_ report: OffloadReport, destination d: Int) -> [(file: OffloadReport.FileEntry, action: String)] {
        report.files.compactMap { f in
            guard f.xxh64 != nil, d < f.destinations.count else { return nil }
            let state = f.destinations[d]
            if let rw = f.rewritten, d < rw.count {
                if state == "pending" || (!rw[d] && (state == "written" || !report.verified)) { return nil }
                return (f, state == "failed" ? "failed" : rw[d] ? "original" : "verified")
            }
            if state == "written" && report.verified { return nil }
            return (f, state == "verified" || state == "written" ? "original" : "failed")
        }
    }

    /// The chain has this report's generation already (a commit retried) — exactly these files, hashes and actions.
    /// False when not reached yet; throws when it has another one, or a damaged one (nothing is overwritten).
    static func hasGeneration(_ report: OffloadReport, cardPath: String) throws -> Bool {
        guard let d = report.destinations.firstIndex(where: { $0.cardPath == cardPath }) else { return true }
        let dir = cardPath + "/" + dirName
        guard let entry = (MHLReader.readChain(dir) ?? []).first(where: { $0.seq == report.generationNumber }) else { return false }
        let file = dir + "/" + entry.path
        guard !entry.path.contains("/"), let data = FileManager.default.contents(atPath: file), c4(data) == entry.c4 else {
            throw IOError(message: "ASC MHL v \(cardPath) má generaci \(report.generationNumber), ale její soubor chybí nebo byl změněn — generace se nezapsala znovu.")
        }
        let have = try MHLReader.parseGeneration(file, seq: entry.seq).files
            .map { f -> String in let h = f.hashes.first { $0.algorithm == "xxh64" }; return "\(f.path)|\(h?.value ?? "")|\(h?.action ?? "")" }.sorted()
        let want = entries(report, destination: d).map { "\($0.file.path)|\($0.file.xxh64!)|\($0.action)" }.sorted()
        guard have == want else {
            throw IOError(message: "ASC MHL v \(cardPath) už má jinou generaci \(report.generationNumber) — generace se nezapsala znovu.")
        }
        return true
    }
```

`IOError` dostane inicializátor pro hotovou větu (`description` je jen ona):

```swift
    public init(message: String) { self.op = message; self.path = ""; self.code = 0 }
    public var description: String { path.isEmpty && code == 0 ? op : "\(op) \(path): \(String(cString: strerror(code))) (errno \(code))" }
```

- [ ] **Step 5: Testy projdou** — Run: `swift test --filter MHLTests`, `swift test`. Když je `pip install ascmhl`:
  `ascmhl verify <karta z testu testGenerationOneThenTwoAndChain>` (test vypíše cestu do logu, `XCTContext`), výsledek do commitu.

- [ ] **Step 6: Záznam a commit** — `docs/VYVOJ-MAC.md`: `- Úkol 40 — ASC MHL: čtení všech generací (řetěz, C4, problémy
  česky, všechny hashe), zápis generace N (nikdy přes řetěz, jen N = poslední + 1, ignore z generace 1, xxh128 a md5,
  akce opravy, metoda ověření v komentáři), hasGeneration`.
  Run: `git add -A mac docs && git commit -m "Mac: ASC MHL reader and generation N writer"`

---

### Task 41: Zápis generace na disk — pořadí, prodloužení `.xxh64`/`.md5`, potvrzení výsledku ověření

**Files:**
- Modify: `mac/Sources/OffloadKit/Report.swift` (`write(cardPath:includeHashRecords:createNew:)`, `commit(cardPath:)`, `hashList(of:)`, `healHashList`)
- Create: `mac/Tests/OffloadKitTests/GenerationWriteTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `MHL.write`, `hasGeneration`, `MHLReader.read` (úkol 40), `IO.writeFileNew`, jména generací (úkol 33).
- Produces:
  - `public func OffloadReport.write(cardPath: String, includeHashRecords: Bool, createNew: Bool? = nil) throws`
  - `public func OffloadReport.commit(cardPath: String) throws` (zápis výsledku ověření během proxy — úkol 67)
  - `public func OffloadReport.hashList(of algorithm: String) -> String` (`"xxh64"` | `"md5"`; `hashList` = `hashList(of: "xxh64")`)
  - `static func OffloadReport.healHashList(cardPath: String, cardName: String) throws -> Bool`

Pravidla (FORMATY „Doplnění“, „`<karta>.xxh64`“, „`<karta>.md5`“; spec D.22.2):

- **Pořadí zápisu** generace (každá chyba nechá složku ve stavu, který další doplnění správně odmítne nebo dokončí):
  1. reporty generace (`JSON`, `TXT`, `HTML`; PDF píše úloha po nich) — u generace ≥ 2 při prvním zápisu **jen nové**
     (`writeFileNew`; existující soubor zastaví všechno dřív, než se sáhne na sdílený soubor); obnova téže generace úlohou
     (analýza klipů, proxy) je přepisuje (`includeHashRecords == false`);
  2. soubor generace MHL, pak **řetěz** (bod potvrzení);
  3. seznamy součtů naposled: `<karta>.xxh64` vždy, `<karta>.md5` jen s MD5. Generace 1 je zapíše celé; generace ≥ 2:
     **dřívější bajty beze změny** (chybí-li na konci `\n`, doplní se), za ně řádky, které MHL zná a seznam ne (dřívější
     doplnění, jemuž selhal tento krok), pak řádky této generace, které v seznamu **doslova** nejsou (oprava neopakuje řádky).
     Nic nového = soubor se nemění. Zápis atomicky (dočasné jméno + `rename`).
- `commit` (výsledek ověření během proxy, úkol 67): **nejdřív** MHL a řetěz (když `hasGeneration` ještě není), pak reporty
  generace (nahradit — ty s `verifyPending`), pak seznamy součtů se sloučením (i u generace 1 zachová existující seznam
  a jen doplní chybějící řádky). Report s `verifyPending == true` se nikdy nepotvrdí (chyba).
- `healHashList`: doplnění, které nic nového nekopíruje, doplní do seznamů řádky, které MHL zná a seznam ne.
- Doplnění počítá součty **dřívější zálohy** (`algorithms` jejího reportu), ne aktuální volbu (úkol 46).

- [ ] **Step 1: Padající testy** — `mac/Tests/OffloadKitTests/GenerationWriteTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class GenerationWriteTests: XCTestCase {
    private func gen(_ card: String, _ g: Int, files: [(String, String)], md5: Bool = false) -> OffloadReport {
        var r = Reports.sample()
        r.cardName = "A001"
        r.generation = g > 1 ? g : nil
        r.hashAlgorithms = md5 ? ["xxh64", "md5"] : nil
        r.destinations = [OffloadReport.Destination(root: "/r", cardPath: card, filesystem: "apfs", filesOK: files.count, filesFailed: 0)]
        r.files = files.map { OffloadReport.FileEntry(path: $0.0, size: 1, modified: Reports.t0, xxh64: $0.1, md5: md5 ? String(repeating: "a", count: 32) : nil,
                                                      sourceError: nil, destinations: ["verified"], errors: [nil]) }
        return r
    }

    // Review focus 1.
    func testAppendCreatesNumberedReportsNeverReplaces() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111")]).write(cardPath: card, includeHashRecords: true)
        let main = try Data(contentsOf: URL(fileURLWithPath: card + "/_REPORT/offload_report.json"))
        let g2 = gen(card, 2, files: [("B.bin", "2222222222222222")])
        try g2.write(cardPath: card, includeHashRecords: true)
        XCTAssertTrue(FileManager.default.fileExists(atPath: card + "/_REPORT/offload_report_0002.json"))
        XCTAssertTrue(FileManager.default.fileExists(atPath: card + "/_REPORT/A001_report_0002.html"))
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: card + "/_REPORT/offload_report.json")), main, "generation 1 untouched")
        XCTAssertThrowsError(try g2.write(cardPath: card, includeHashRecords: true), "a second first write of generation 2 is refused")
        try g2.write(cardPath: card, includeHashRecords: false)   // the job's own refresh of its generation
    }

    func testHashListKeepsEarlierBytes() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111")]).write(cardPath: card, includeHashRecords: true)
        let list = card + "/_REPORT/A001.xxh64"
        // A Windows tool may have written CRLF: the earlier bytes stay exactly as they are.
        try Data("1111111111111111  A.bin\r\n".utf8).write(to: URL(fileURLWithPath: list))
        try gen(card, 2, files: [("B.bin", "2222222222222222")]).write(cardPath: card, includeHashRecords: true)
        XCTAssertEqual(try String(contentsOfFile: list, encoding: .utf8), "1111111111111111  A.bin\r\n2222222222222222  B.bin\n")
    }

    func testMissedLinesFromMhlAndNoDuplicates() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111")]).write(cardPath: card, includeHashRecords: true)
        try gen(card, 2, files: [("B.bin", "2222222222222222")]).write(cardPath: card, includeHashRecords: true)
        let list = card + "/_REPORT/A001.xxh64"
        try Data("1111111111111111  A.bin\n".utf8).write(to: URL(fileURLWithPath: list))   // generation 2's lines were lost
        XCTAssertTrue(try OffloadReport.healHashList(cardPath: card, cardName: "A001"))
        XCTAssertEqual(try String(contentsOfFile: list, encoding: .utf8), "1111111111111111  A.bin\n2222222222222222  B.bin\n")
        XCTAssertFalse(try OffloadReport.healHashList(cardPath: card, cardName: "A001"), "nothing missed now")
    }

    func testMd5ListOnlyWithMd5() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111")]).write(cardPath: card, includeHashRecords: true)
        XCTAssertFalse(FileManager.default.fileExists(atPath: card + "/_REPORT/A001.md5"))
        let card2 = tempDir().sub("E", "2026-10-08", "A001")
        try gen(card2, 1, files: [("A.bin", "1111111111111111")], md5: true).write(cardPath: card2, includeHashRecords: true)
        XCTAssertEqual(try String(contentsOfFile: card2 + "/_REPORT/A001.md5", encoding: .utf8), String(repeating: "a", count: 32) + "  A.bin\n")
    }

    func testCommitWritesMhlFirstAndRefusesPending() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        var r = gen(card, 1, files: [("A.bin", "1111111111111111")])
        r.verifyPending = true
        try r.write(cardPath: card, includeHashRecords: false)        // stage 1: reports only, no generation yet
        XCTAssertNil(MHLReader.read(card))
        XCTAssertThrowsError(try r.commit(cardPath: card))
        r.verifyPending = nil
        try r.commit(cardPath: card)
        try r.commit(cardPath: card)                                   // retried: idempotent
        XCTAssertEqual(MHLReader.read(card)?.chain.count, 1)
        XCTAssertNil(try OffloadReport.read(card + "/_REPORT/offload_report.json").verifyPending)
    }
}
```

- [ ] **Step 2: Implementace** — v `Report.swift` nahradit `write` a `hashList`:

```swift
    public var hashList: String { hashList(of: "xxh64") }

    /// "<hash>  <path>\n" per file that has `algorithm` ("xxh64" or "md5", the md5sum format).
    public func hashList(of algorithm: String) -> String {
        files.compactMap { f in (algorithm == "md5" ? f.md5 : f.xxh64).map { "\($0)  \(f.path)\n" } }.joined()
    }

    static func hashListPath(_ cardPath: String, _ cardName: String, _ algorithm: String) -> String {
        cardPath + "/" + Layout.reportDirName + "/" + cardName + "." + algorithm
    }

    /// Spec D.22.2 order: 1. this generation's reports (new ones refuse an existing file), 2. the MHL generation and the
    /// chain (the commit point), 3. the hash lists last.
    public func write(cardPath: String, includeHashRecords: Bool, createNew: Bool? = nil) throws {
        try writeReports(cardPath, create: createNew ?? (generationNumber > 1 && includeHashRecords))
        guard includeHashRecords else { return }
        try MHL.write(report: self, cardPath: cardPath)
        try writeHashLists(cardPath)
    }

    /// The verification result of a generation that verified with its proxies: MHL + chain first (unless a retry finds
    /// them), then the reports written with verifyPending are replaced, then the hash lists (merged).
    public func commit(cardPath: String) throws {
        if verifyPending == true { throw OffloadError.failed("a report still pending verification is never committed") }
        if !(try MHL.hasGeneration(self, cardPath: cardPath)) { try MHL.write(report: self, cardPath: cardPath) }
        try writeReports(cardPath, create: false)
        try writeHashLists(cardPath, merge: true)
    }

    func writeReports(_ cardPath: String, create: Bool) throws {
        let dir = cardPath + "/" + Layout.reportDirName
        try IO.mkdirs(dir)
        let g = generationNumber
        let outputs: [(String, Data)] = [
            (OffloadReport.jsonName(g), try jsonData()),
            (OffloadReport.textName(g), Data(summary.utf8)),
            (OffloadReport.htmlName(cardName, g), Data(HTMLReport.render(self).utf8)),
        ]
        for (name, data) in outputs {
            if create { try IO.writeFileNew(dir + "/" + name, data) } else { try IO.writeFileDurably(dir + "/" + name, data) }
        }
    }

    func writeHashLists(_ cardPath: String, merge: Bool = false) throws {
        for algorithm in algorithms where algorithm == "xxh64" || algorithm == "md5" {
            let path = OffloadReport.hashListPath(cardPath, cardName, algorithm)
            let lines = hashList(of: algorithm)
            if generationNumber == 1 && !(merge && FileManager.default.fileExists(atPath: path)) {
                try IO.writeFileDurably(path, Data(lines.utf8))
                continue
            }
            let (earlier, missed) = OffloadReport.listAndMissed(path, cardPath: cardPath, alsoListed: files.map(\.path), algorithm: algorithm)
            let added = missed + OffloadReport.notIn(earlier, lines)
            if added.isEmpty && FileManager.default.fileExists(atPath: path) { continue }
            try IO.writeFileDurably(path, earlier + Data(added.utf8))
        }
    }

    /// The lines of `lines` the list does not have word for word (a repair's file is listed already with its recorded hash).
    static func notIn(_ earlier: Data, _ lines: String) -> String {
        let have = Set(String(decoding: earlier, as: UTF8.self).split(separator: "\n").map { $0.hasSuffix("\r") ? String($0.dropLast()) : String($0) })
        return lines.split(separator: "\n").map(String.init).filter { !have.contains($0) }.map { $0 + "\n" }.joined()
    }

    /// The list as it is (exact bytes, a final "\n" added when missing) and the lines of every path the ASC MHL history
    /// records but neither the list nor `alsoListed` has.
    static func listAndMissed(_ path: String, cardPath: String, alsoListed: [String], algorithm: String) -> (Data, String) {
        var earlier = FileManager.default.contents(atPath: path) ?? Data()
        if let last = earlier.last, last != UInt8(ascii: "\n") { earlier.append(UInt8(ascii: "\n")) }
        var listed = Set(alsoListed.map { $0.lowercased() })
        for line in String(decoding: earlier, as: UTF8.self).split(separator: "\n") {
            let l = line.hasSuffix("\r") ? line.dropLast() : line[...]
            if let r = l.range(of: "  ") { listed.insert(String(l[r.upperBound...]).lowercased()) }
        }
        var missed = ""
        if let mhl = MHLReader.read(cardPath) {
            for rec in mhl.lastGood.values.sorted(by: { $0.path < $1.path }) where !listed.contains(rec.path.lowercased()) {
                if let h = rec.hashes.first(where: { $0.algorithm == algorithm }) { missed += "\(h.value)  \(rec.path)\n" }
            }
        }
        return (earlier, missed)
    }

    /// An append with nothing new: add the lines the hash lists missed. True when something was added.
    public static func healHashList(cardPath: String, cardName: String) throws -> Bool {
        var healed = false
        for algorithm in ["xxh64", "md5"] {
            let path = hashListPath(cardPath, cardName, algorithm)
            let (earlier, missed) = listAndMissed(path, cardPath: cardPath, alsoListed: [], algorithm: algorithm)
            guard !missed.isEmpty else { continue }
            try IO.writeFileDurably(path, earlier + Data(missed.utf8))
            healed = true
        }
        return healed
    }
```

Engine (`OffloadEngine.run`) dál volá `report.write(cardPath:includeHashRecords: true)`; obnova reportu ve fázi 2
(`Job.swift`) `includeHashRecords: false` — beze změny volání.

- [ ] **Step 3: Testy projdou** — Run: `swift test --filter GenerationWriteTests`, `swift test`.

- [ ] **Step 4: Záznam a commit** — `docs/VYVOJ-MAC.md`: `- Úkol 41 — zápis generace: reporty (nové) → MHL → řetěz →
  seznamy součtů; .xxh64/.md5 jen prodloužit (dřívější bajty zůstanou, chybějící řádky z MHL, bez opakování), commit
  výsledku ověření (MHL nejdřív, idempotentní), healHashList`.
  Run: `git add -A mac docs && git commit -m "Mac: generation write order, hash lists only extended, commit of a verification"`

---

### Task 42: Historie — otisk karty, `history/<id>.files.gz`, `history.json` pod zámkem

**Files:**
- Create: `mac/Sources/OffloadKit/History/CardFingerprint.swift` (+ `Gzip`), `History/HistoryEntry.swift`, `History/HistoryStore.swift`
- Create: `mac/Tests/OffloadKitTests/HistoryStoreTests.swift`, `mac/Tests/OffloadKitTests/Fixtures/windows-history.files.gz`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `FileTimes.ticks` (úkol 36), `FileLock`, `Locks.history`, `AppSupport.folder` (úkol 37), `SourceFile`, `CardKey`.
- Produces:
  - `public enum CardFingerprint { static func compute(_ files: [SourceFile]) -> (fingerprint: String, shape: String) }`
  - `enum Gzip { static func compress(_ d: Data) -> Data; static func decompress(_ d: Data) -> Data?; static func crc32(_ d: Data) -> UInt32 }`
  - `public enum FileListStore { static func write(_ path: String, _ files: [SourceFile]) throws; static func read(_ path: String) -> [SourceFile]? }`
  - `public struct HistoryCard, HistoryDestination, HistoryGeneration, HistoryEntry: Codable, Equatable` (klíče FORMATY „Místní soubory“)
  - `public enum HistoryWrite { case written, unchanged, busy, failed }`
  - `public final class HistoryStore { init(folder: String = AppSupport.folder, lockTimeout: TimeInterval = 5); load() -> [HistoryEntry]; update(id:timeout:_:) -> HistoryWrite; remove(id:timeout:) -> HistoryWrite; readFileList(_:) -> [SourceFile]?; findForCard(_ key: CardKey?, shape: String) -> HistoryEntry?; static func newId(finished: Date) -> String; static let maxEntries = 5000 }`

Pravidla (FORMATY „Místní soubory“; spec D.22.1):

- `~/Library/Application Support/Offload/history.json` + `history/<id>.files.gz` (složku přesměruje `OFFLOAD_SETTINGS_DIR`).
  UTF-8 bez BOM, odsazené, data ISO 8601 UTC na sekundy; kořen `{ "version": 1, "entries": [...] }`. **Vyšší neznámá verze =
  jen číst, nikdy nezapsat.** Nejvýš 5 000 záznamů (nejstarší podle `finished` se smažou i se seznamem souborů). Poškozený
  soubor → přejmenovat na `history.json.corrupt-<yyyyMMdd-HHmmss>` (jen pod zámkem) a začít prázdný; offload nikdy neblokuje.
- Každé čtení-úprava-zápis **pod zámkem historie** (`Locks.history(folder)`, čekání do `lockTimeout`, pak `busy`), zápis přes
  dočasné jméno + `rename`. Úprava a seznam souborů jedním držením zámku.
- `id` = `yyyyMMdd'T'HHmmss'Z'` konce kopie v UTC + `-` + 8 náhodných hex. Cesta seznamu souborů se **vždy odvodí z `id`**
  (nikdy z uloženého `fileList` — ručně upravený soubor nesmí ukázat jinam).
- **Otisk**: SHA-256 (malé hex) přes řádky seřazené podle cesty **ordinálně v UTF-16** (jako .NET `StringComparer.Ordinal`):
  `fingerprint` = `relpath\tsize\tmtimeTicksUtc\n`, `shape` = `relpath\tsize\n`, UTF-8. `files.gz` = gzip týchž řádků jako
  `fingerprint` (soubor z Windows `GZipStream` se musí přečíst a naopak — fixture).
- gzip: Foundation `NSData.compressed(using: .zlib)` dává raw DEFLATE (RFC 1951) — kolem něj hlavička gzip (`1f 8b 08`,
  bez příznaků, OS 255), CRC-32 a délka (LE). Čtení přeskočí volitelná pole hlavičky (FEXTRA, FNAME, FCOMMENT, FHCRC)
  a ověří CRC i délku; vadný seznam = `nil` (stojí jen počet nových souborů, nikdy řádek historie).
- `findForCard`: u kořene svazku nejnovější záznam se shodným `fsSerial` **a** `capacity` (R1); u složky se shodným `shape`.

- [ ] **Step 1: Fixture** — `mac/Tests/OffloadKitTests/Fixtures/windows-history.files.gz`: na Windows PC
  `[IO.File]::WriteAllBytes(...)` přes `FileListStore.Write` se dvěma řádky `CLIP/A001C001.mov\t1000\t638640000000000000`
  a `CLIP/A001C002.mov\t2000\t638640000010000000` (nebo `printf 'CLIP/A001C001.mov\t1000\t638640000000000000\nCLIP/A001C002.mov\t2000\t638640000010000000\n' | gzip -n > …`
  na Macu, když Windows není po ruce — `gzip` dává stejný formát jako .NET). Test ji jen čte.

- [ ] **Step 2:** (Testy používají dočasnou složku nastavení z úkolu 37 — `testSettingsDir` v `TestSupport.swift`.)

- [ ] **Step 3: Padající testy** — `mac/Tests/OffloadKitTests/HistoryStoreTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class HistoryStoreTests: XCTestCase {
    private func entry(_ id: String, finished: Date, serial: String? = "1A2B3C4D", shape: String = "s") -> HistoryEntry {
        var e = HistoryEntry(id: id, source: "/Volumes/A001", cardName: "A001", date: "2026-10-08", finished: finished)
        e.card = serial.map { HistoryCard(identity: nil, fsSerial: $0, capacity: 64_000_000_000, label: "A001", filesystem: "exFAT") }
        e.fingerprint = "f"; e.shape = shape; e.success = true; e.verified = true
        e.destinations = [HistoryDestination(root: "/Volumes/RAID", cardPath: "/Volumes/RAID/2026-10-08/A001")]
        e.generations = [HistoryGeneration(seq: 1, kind: "full", finished: finished, files: 1, bytes: 10, verified: true)]
        return e
    }

    func testFingerprintMatchesWindowsLines() {
        let f = [SourceFile(relPath: "b", size: 2, modified: Date(timeIntervalSince1970: 1)),
                 SourceFile(relPath: "a", size: 1, modified: Date(timeIntervalSince1970: 0))]
        let (fp, shape) = CardFingerprint.compute(f)
        // sha256("a\t1\t621355968000000000\nb\t2\t621355968010000000\n") and sha256("a\t1\nb\t2\n")
        XCTAssertEqual(fp, sha256Hex("a\t1\t621355968000000000\nb\t2\t621355968010000000\n"))
        XCTAssertEqual(shape, sha256Hex("a\t1\nb\t2\n"))
    }

    func testGzipRoundTripAndWindowsFile() throws {
        let data = Data("hello\tworld\n".utf8)
        XCTAssertEqual(Gzip.decompress(Gzip.compress(data)), data)
        let fixture = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("Fixtures/windows-history.files.gz").path
        let list = try XCTUnwrap(FileListStore.read(fixture))
        XCTAssertEqual(list.map(\.relPath), ["CLIP/A001C001.mov", "CLIP/A001C002.mov"])
        XCTAssertEqual(FileTimes.ticks(list[1].mtime), 638_640_000_010_000_000)
        XCTAssertNil(FileListStore.read(tempDir().sub("missing.gz")))
    }

    func testRoundTripNewestFirstAndFind() {
        let store = HistoryStore(folder: tempDir().path)
        let t = Date(timeIntervalSince1970: 1_791_000_000)
        XCTAssertEqual(store.update(id: "a") { _ in (self.entry("a", finished: t), nil) }, .written)
        XCTAssertEqual(store.update(id: "b") { _ in (self.entry("b", finished: t.addingTimeInterval(60)), nil) }, .written)
        XCTAssertEqual(store.load().map(\.id), ["b", "a"])
        XCTAssertEqual(store.findForCard(CardKey(fsSerial: "1A2B3C4D", capacity: 64_000_000_000), shape: "x")?.id, "b")
        XCTAssertNil(store.findForCard(CardKey(fsSerial: "1A2B3C4D", capacity: 32_000_000_000), shape: "x"), "a clone with another capacity")
    }

    func testCorruptFileSetAside() throws {
        let dir = tempDir()
        try Data("{ not json".utf8).write(to: URL(fileURLWithPath: dir.sub("history.json")))
        let store = HistoryStore(folder: dir.path)
        XCTAssertEqual(store.load().count, 0)
        XCTAssertTrue(TestFS.files(in: dir.path).contains { $0.hasPrefix("history.json.corrupt-") })
    }

    func testNewerVersionIsReadOnly() throws {
        let dir = tempDir()
        try Data(#"{"version":2,"entries":[]}"#.utf8).write(to: URL(fileURLWithPath: dir.sub("history.json")))
        let store = HistoryStore(folder: dir.path)
        XCTAssertEqual(store.update(id: "a") { _ in (self.entry("a", finished: Date()), nil) }, .failed)
        XCTAssertEqual(try String(contentsOfFile: dir.sub("history.json"), encoding: .utf8), #"{"version":2,"entries":[]}"#)
    }

    // Review focus 5.
    func testTwoWritersUnderTheLock() {
        let store = HistoryStore(folder: tempDir().path)
        DispatchQueue.concurrentPerform(iterations: 20) { i in
            _ = store.update(id: "e\(i)") { _ in (self.entry("e\(i)", finished: Date(timeIntervalSince1970: Double(i))), nil) }
        }
        XCTAssertEqual(store.load().count, 20, "no write lost")
    }

    func testBusyLockReportsBusy() throws {
        let dir = tempDir()
        let held = try XCTUnwrap(FileLock.tryLock(Locks.history(dir.path)))
        let store = HistoryStore(folder: dir.path, lockTimeout: 0.2)
        XCTAssertEqual(store.update(id: "a") { _ in (self.entry("a", finished: Date()), nil) }, .busy)
        held.release()
    }

    func testPruneKeepsNewestAndDeletesLists() throws {
        let dir = tempDir()
        let store = HistoryStore(folder: dir.path, maxEntries: 2)
        for i in 0..<3 {
            _ = store.update(id: "e\(i)") { _ in (self.entry("e\(i)", finished: Date(timeIntervalSince1970: Double(i))), [SourceFile(relPath: "x", size: 1, modified: Date())]) }
        }
        XCTAssertEqual(store.load().map(\.id), ["e2", "e1"])
        XCTAssertFalse(FileManager.default.fileExists(atPath: dir.sub("history", "e0.files.gz")))
    }

    private func sha256Hex(_ s: String) -> String { SHA256Hex.of(Data(s.utf8)) }
}
```

Run: `swift test --filter HistoryStoreTests` → nesestaví se.

- [ ] **Step 4: Implementace** — `History/CardFingerprint.swift`:

```swift
import CryptoKit
import Foundation

enum SHA256Hex { static func of(_ d: Data) -> String { SHA256.hash(data: d).map { String(format: "%02x", $0) }.joined() } }

/// What a card looked like (Windows R2): SHA-256 over "relpath\tsize\tmtimeTicksUtc\n" (fingerprint) and
/// "relpath\tsize\n" (shape), the files sorted ordinally (UTF-16 code units, as .NET).
public enum CardFingerprint {
    static func ordinal(_ a: String, _ b: String) -> Bool { a.utf16.lexicographicallyPrecedes(b.utf16) }

    public static func compute(_ files: [SourceFile]) -> (fingerprint: String, shape: String) {
        var full = SHA256(), shape = SHA256()
        for f in files.sorted(by: { ordinal($0.relPath, $1.relPath) }) {
            shape.update(data: Data("\(f.relPath)\t\(f.size)\n".utf8))
            full.update(data: Data("\(f.relPath)\t\(f.size)\t\(FileTimes.ticks(f.mtime))\n".utf8))
        }
        let hex = { (d: SHA256.Digest) in d.map { String(format: "%02x", $0) }.joined() }
        return (hex(full.finalize()), hex(shape.finalize()))
    }
}

/// gzip (RFC 1952) around Foundation's raw DEFLATE.
enum Gzip {
    static let table: [UInt32] = (0..<256).map { n -> UInt32 in
        var c = UInt32(n)
        for _ in 0..<8 { c = c & 1 != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1 }
        return c
    }

    static func crc32(_ d: Data) -> UInt32 {
        var c: UInt32 = 0xFFFF_FFFF
        for b in d { c = table[Int((c ^ UInt32(b)) & 0xFF)] ^ (c >> 8) }
        return c ^ 0xFFFF_FFFF
    }

    static func le32(_ v: UInt32) -> Data { Data((0..<4).map { UInt8((v >> (8 * UInt32($0))) & 0xFF) }) }

    static func compress(_ d: Data) -> Data {
        let body = (try? (d as NSData).compressed(using: .zlib) as Data) ?? Data()
        return Data([0x1F, 0x8B, 0x08, 0, 0, 0, 0, 0, 0, 0xFF]) + body + le32(crc32(d)) + le32(UInt32(truncatingIfNeeded: d.count))
    }

    static func decompress(_ g: Data) -> Data? {
        let b = [UInt8](g)
        guard b.count >= 18, b[0] == 0x1F, b[1] == 0x8B, b[2] == 8 else { return nil }
        let flags = b[3]
        var i = 10
        if flags & 4 != 0 { guard i + 2 <= b.count else { return nil }; i += 2 + Int(b[i]) | Int(b[i + 1]) << 8 }
        if flags & 8 != 0 { while i < b.count && b[i] != 0 { i += 1 }; i += 1 }
        if flags & 16 != 0 { while i < b.count && b[i] != 0 { i += 1 }; i += 1 }
        if flags & 2 != 0 { i += 2 }
        guard i <= b.count - 8 else { return nil }
        let body = Data(b[i..<(b.count - 8)])
        guard let out = try? (body as NSData).decompressed(using: .zlib) as Data else { return nil }
        let crc = UInt32(b[b.count - 8]) | UInt32(b[b.count - 7]) << 8 | UInt32(b[b.count - 6]) << 16 | UInt32(b[b.count - 5]) << 24
        let size = UInt32(b[b.count - 4]) | UInt32(b[b.count - 3]) << 8 | UInt32(b[b.count - 2]) << 16 | UInt32(b[b.count - 1]) << 24
        return crc == crc32(out) && size == UInt32(truncatingIfNeeded: out.count) ? out : nil
    }
}

/// `history/<id>.files.gz`: gzip of UTF-8 lines "relpath\tsize\tmtimeTicksUtc\n".
public enum FileListStore {
    public static func write(_ path: String, _ files: [SourceFile]) throws {
        let text = files.map { "\($0.relPath)\t\($0.size)\t\(FileTimes.ticks($0.mtime))\n" }.joined()
        try IO.mkdirs((path as NSString).deletingLastPathComponent)
        try IO.writeFileDurably(path, Gzip.compress(Data(text.utf8)))
    }

    /// Nil when missing or damaged — that only costs the "new files" count, never the history line.
    public static func read(_ path: String) -> [SourceFile]? {
        guard let g = FileManager.default.contents(atPath: path), let d = Gzip.decompress(g) else { return nil }
        var out: [SourceFile] = []
        for line in String(decoding: d, as: UTF8.self).split(separator: "\n", omittingEmptySubsequences: true) {
            let p = line.split(separator: "\t", omittingEmptySubsequences: false)
            guard p.count == 3, let size = Int64(p[1]), let ticks = Int64(p[2]) else { return nil }
            let unix = ticks - 621_355_968_000_000_000
            out.append(SourceFile(relPath: String(p[0]), size: size, mtime: timespec(tv_sec: Int(unix / 10_000_000), tv_nsec: Int(unix % 10_000_000) * 100)))
        }
        return out
    }
}
```

`History/HistoryEntry.swift`:

```swift
import Foundation

public struct HistoryCard: Codable, Equatable {
    /// CardIdentity at the time — stored, never required to match (R1).
    public var identity: String?
    public var fsSerial: String
    public var capacity: Int64
    public var label: String?
    public var filesystem: String?
}

public struct HistoryDestination: Codable, Equatable { public var root: String; public var cardPath: String }

/// One copy into the card folder: full copy (seq 1), append or repair (seq = its ASC MHL generation).
public struct HistoryGeneration: Codable, Equatable {
    public var seq: Int
    public var kind: String          // "full" | "append" | "repair"
    public var finished: Date
    public var files: Int
    public var bytes: Int64
    public var verified: Bool
}

public struct HistoryEntry: Codable, Equatable {
    public var id: String
    public var card: HistoryCard?
    public var source: String
    public var cardName: String
    public var date: String
    public var project: String?
    public var finished: Date
    public var success = false
    public var verified = false
    public var failedFiles: Int?
    public var hashAlgorithms = ["xxh64"]
    public var files = 0
    public var bytes: Int64 = 0
    public var fingerprint = ""
    public var shape = ""
    public var fileList: String?
    public var destinations: [HistoryDestination] = []
    public var generations: [HistoryGeneration] = []
    public var reels: [String] = []
    /// A full copy that started and has not ended (R6): its card offers "Dokončit přerušenou zálohu". Absent when false.
    public var unfinished = false

    public init(id: String, source: String, cardName: String, date: String, finished: Date) {
        self.id = id; self.source = source; self.cardName = cardName; self.date = date; self.finished = finished
    }

    enum CodingKeys: String, CodingKey {
        case id, card, source, cardName, date, project, finished, success, verified, failedFiles, hashAlgorithms, files, bytes
        case fingerprint, shape, fileList, destinations, generations, reels, unfinished
    }

    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        id = try c.decode(String.self, forKey: .id)
        card = try c.decodeIfPresent(HistoryCard.self, forKey: .card)
        source = try c.decodeIfPresent(String.self, forKey: .source) ?? ""
        cardName = try c.decodeIfPresent(String.self, forKey: .cardName) ?? ""
        date = try c.decodeIfPresent(String.self, forKey: .date) ?? ""
        project = try c.decodeIfPresent(String.self, forKey: .project)
        finished = try c.decode(Date.self, forKey: .finished)
        success = try c.decodeIfPresent(Bool.self, forKey: .success) ?? false
        verified = try c.decodeIfPresent(Bool.self, forKey: .verified) ?? false
        failedFiles = try c.decodeIfPresent(Int.self, forKey: .failedFiles)
        hashAlgorithms = try c.decodeIfPresent([String].self, forKey: .hashAlgorithms) ?? ["xxh64"]
        files = try c.decodeIfPresent(Int.self, forKey: .files) ?? 0
        bytes = try c.decodeIfPresent(Int64.self, forKey: .bytes) ?? 0
        fingerprint = try c.decodeIfPresent(String.self, forKey: .fingerprint) ?? ""
        shape = try c.decodeIfPresent(String.self, forKey: .shape) ?? ""
        fileList = try c.decodeIfPresent(String.self, forKey: .fileList)
        destinations = try c.decodeIfPresent([HistoryDestination].self, forKey: .destinations) ?? []
        generations = try c.decodeIfPresent([HistoryGeneration].self, forKey: .generations) ?? []
        reels = try c.decodeIfPresent([String].self, forKey: .reels) ?? []
        unfinished = try c.decodeIfPresent(Bool.self, forKey: .unfinished) ?? false
    }

    public func encode(to e: Encoder) throws {
        var c = e.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id); try c.encode(card, forKey: .card)          // null for a folder, as Windows
        try c.encode(source, forKey: .source); try c.encode(cardName, forKey: .cardName); try c.encode(date, forKey: .date)
        try c.encode(project, forKey: .project); try c.encode(finished, forKey: .finished)
        try c.encode(success, forKey: .success); try c.encode(verified, forKey: .verified)
        if !success { try c.encode(failedFiles ?? 0, forKey: .failedFiles) }
        try c.encode(hashAlgorithms, forKey: .hashAlgorithms); try c.encode(files, forKey: .files); try c.encode(bytes, forKey: .bytes)
        try c.encode(fingerprint, forKey: .fingerprint); try c.encode(shape, forKey: .shape)
        try c.encode("history/\(id).files.gz", forKey: .fileList)
        try c.encode(destinations, forKey: .destinations); try c.encode(generations, forKey: .generations)
        try c.encode(reels, forKey: .reels)
        if unfinished { try c.encode(true, forKey: .unfinished) }
    }
}

public enum HistoryWrite: Equatable { case written, unchanged, busy, failed }
```

`History/HistoryStore.swift`:

```swift
import Foundation

/// history.json + history/<id>.files.gz — local to this Mac, never sent anywhere. Never throws: a broken history must
/// never stop an offload.
public final class HistoryStore {
    public static let currentVersion = 1
    public let folder: String
    let lockTimeout: TimeInterval
    let maxEntries: Int
    public var log: ((String) -> Void)?

    public init(folder: String = AppSupport.folder, lockTimeout: TimeInterval = 5, maxEntries: Int = 5000) {
        self.folder = folder; self.lockTimeout = lockTimeout; self.maxEntries = maxEntries
    }

    var file: String { folder + "/history.json" }
    func listPath(_ id: String) -> String { folder + "/history/" + id + ".files.gz" }

    /// "20261006T081500Z-3f2a9c1b".
    public static func newId(finished: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX"); f.timeZone = TimeZone(identifier: "UTC"); f.dateFormat = "yyyyMMdd'T'HHmmss'Z'"
        return f.string(from: finished) + "-" + String(format: "%08x", UInt32.random(in: 0...UInt32.max))
    }

    struct Root: Codable { var version: Int; var entries: [HistoryEntry] }

    enum Read { case ok([HistoryEntry]), newer([HistoryEntry]), corrupt }

    func read() -> Read {
        guard let data = FileManager.default.contents(atPath: file) else { return .ok([]) }
        let dec = JSONDecoder(); dec.dateDecodingStrategy = .iso8601
        if let v = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["version"] as? Int, v > Self.currentVersion {
            return .newer((try? dec.decode(Root.self, from: data))?.entries ?? [])
        }
        guard let root = try? dec.decode(Root.self, from: data) else { return .corrupt }
        return .ok(root.entries)
    }

    func setAsideCorrupt() {
        let f = DateFormatter(); f.locale = Locale(identifier: "en_US_POSIX"); f.dateFormat = "yyyyMMdd-HHmmss"
        let to = file + ".corrupt-" + f.string(from: Date())
        if rename(file, to) == 0 { log?("history: damaged history.json set aside as \(to)") }
    }

    /// Every entry, newest `finished` first.
    public func load() -> [HistoryEntry] {
        guard let held = FileLock.lock(Locks.history(folder), timeout: lockTimeout) else { return peek() }
        defer { held.release() }
        switch read() {
        case .ok(let e), .newer(let e): return e.sorted { $0.finished > $1.finished }
        case .corrupt: setAsideCorrupt(); return []
        }
    }

    /// Without the lock (it stayed busy): what can be read now.
    func peek() -> [HistoryEntry] {
        if case .ok(let e) = read() { return e.sorted { $0.finished > $1.finished } }
        return []
    }

    /// Load, modify and save under ONE hold of the lock. `modify` gets the entry with this id (nil when none) and
    /// returns the entry to store (same id) and optionally its new file list — or nil to change nothing.
    @discardableResult
    public func update(id: String, timeout: TimeInterval? = nil, _ modify: (HistoryEntry?) -> (HistoryEntry, [SourceFile]?)?) -> HistoryWrite {
        guard let held = FileLock.lock(Locks.history(folder), timeout: timeout ?? lockTimeout) else { return .busy }
        defer { held.release() }
        var entries: [HistoryEntry]
        switch read() {
        case .newer: log?("history: written by a newer version — read only"); return .failed
        case .corrupt: setAsideCorrupt(); entries = []
        case .ok(let e): entries = e
        }
        let index = entries.firstIndex { $0.id == id }
        guard let (entry, files) = modify(index.map { entries[$0] }), entry.id == id else { return .unchanged }
        if let index { entries[index] = entry } else { entries.append(entry) }
        do {
            if let files { try FileListStore.write(listPath(id), files) }
            entries.sort { $0.finished > $1.finished }
            if entries.count > maxEntries {
                for old in entries[maxEntries...] { unlink(listPath(old.id)) }
                entries = Array(entries[..<maxEntries])
            }
            try save(entries)
            return .written
        } catch {
            log?("history: \(error)")
            return .failed
        }
    }

    @discardableResult
    public func remove(id: String, timeout: TimeInterval? = nil) -> HistoryWrite {
        guard let held = FileLock.lock(Locks.history(folder), timeout: timeout ?? lockTimeout) else { return .busy }
        defer { held.release() }
        guard case .ok(var entries) = read(), let i = entries.firstIndex(where: { $0.id == id }) else { return .unchanged }
        entries.remove(at: i)
        unlink(listPath(id))
        do { try save(entries); return .written } catch { log?("history: \(error)"); return .failed }
    }

    func save(_ entries: [HistoryEntry]) throws {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes]
        enc.dateEncodingStrategy = .iso8601
        try IO.mkdirs(folder)
        try IO.writeFileDurably(file, enc.encode(Root(version: Self.currentVersion, entries: entries)))
    }

    public func readFileList(_ e: HistoryEntry) -> [SourceFile]? { FileListStore.read(listPath(e.id)) }

    /// The newest entry this card may be: a volume root by file-system serial + capacity (R1), a folder by its shape.
    public func findForCard(_ key: CardKey?, shape: String, in entries: [HistoryEntry]? = nil) -> HistoryEntry? {
        (entries ?? load()).first { e in
            if let key { return e.card?.fsSerial == key.fsSerial && e.card?.capacity == key.capacity }
            return e.card == nil && e.shape == shape
        }
    }
}
```

`SourceFile` potřebuje `init(relPath:size:mtime:)` (memberwise je `internal` — zpřístupnit jako `public init`).

- [ ] **Step 5: Testy projdou** — Run: `swift test --filter HistoryStoreTests`, `swift test`.

- [ ] **Step 6: Záznam a commit** — `docs/VYVOJ-MAC.md`: `- Úkol 42 — historie karet: history.json ve formátu Windows
  (verze 1, 5 000 záznamů, poškozený → .corrupt-…, vyšší verze jen číst) pod flock zámkem, otisk karty (SHA-256, .NET ticks,
  ordinální řazení), history/<id>.files.gz (gzip kolem raw DEFLATE, čte i soubor z Windows)`.
  Run: `git add -A mac docs && git commit -m "Mac: card history store in the Windows format"`

---

### Task 43: Karta už zálohovaná — shoda s historií, prohlídka karty, název z klipů

**Files:**
- Create: `mac/Sources/OffloadKit/History/HistoryMatcher.swift`, `History/HistoryText.swift`, `History/CardInspector.swift`, `ReelNames.swift`
- Create: `mac/Tests/OffloadKitTests/HistoryMatcherTests.swift`, `mac/Tests/OffloadKitTests/ReelNamesTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `HistoryStore`, `CardFingerprint`, `FileListStore` (úkol 42), `CardVolume`, `FileTimes` (úkol 36), `Scanner.scan`, `ClipDetector.detect`.
- Produces:
  - `public enum HistoryMatchKind { identical, newFiles, changed, notCompared }`, `public struct HistoryMatch { entry; kind; new: [SourceFile]; newBytes: Int64; changed: [String]; removed: Int }`
  - `public enum HistoryMatcher { static func match(_ scan: SourceScan, fingerprint: String, candidate: HistoryEntry, candidateFiles: [SourceFile]?, filesystem: String?) -> HistoryMatch }`
  - `public enum HistoryTone { neutral, ok, warning, error }`, `public enum HistoryText { inspecting; when(_:); diskName(_:); line(_:fullPaths:) -> (String, HistoryTone); emptied(_:) }`
  - `public enum ReelNames { static func match(_ name: String) -> (reel: String, prefix: String)?; static func fromClipName(_:) -> String? }`
  - `public struct NameSuggestion: Equatable { name: String; hint: String }`
  - `public enum ReelSuggestion { static func fromClips(_ clips: [Clip]) -> (suggestion: NameSuggestion?, note: String?, reels: [String]); static func next(_ history: [HistoryEntry], date: String, project: String?, today: String, taken: [String]) -> NameSuggestion? }`
  - `public struct CardInsight { totalBytes; fileCount; clips: [Clip]; fingerprint; shape; volume: CardVolume?; match: HistoryMatch?; nameSuggestion: NameSuggestion?; nameNote: String? }`
  - `public enum CardInspector { static func inspect(_ source: String, store: HistoryStore?, history: [HistoryEntry], isCancelled: () -> Bool) throws -> CardInsight }`

Pravidla (Windows návrh §2.1, §2.3; FORMATY „Místní soubory“):

- `match`: shodný `fingerprint` → `identical`. Jinak bez seznamu souborů `notCompared`; se seznamem: stejná cesta + velikost +
  čas (`FileTimes.same`, na FAT tolerance celých hodin) = známý; stejná cesta, jiná velikost/čas → `changed`; cesta navíc → nový;
  chybí na kartě → `removed` (jen informace). **Karta nemá ani jednu cestu ze seznamu, ale soubory má → `changed`** (jiná karta,
  klon, karta přeformátovaná a natočená znovu — nikdy „nové soubory“).
- Texty řádku u karty (doslova Windows `HistoryText`): `Zjišťuji, jestli už je karta zálohovaná…`; datum `d. M. yyyy H:mm`
  v místním čase; `Zálohováno 6. 10. 2026 8:15 na Zaloha (RAID), Zaloha2 (SSD)` + ` — ověřeno, lze formátovat` (zelená) /
  ` — NEOVĚŘENO (lze vyjmout, neformátovat)` (oranžová) / ` — s chybami (3 soubory)` (červená); nové soubory
  ` · nové soubory: 12 (3,2 GB)` (zelená se změní na oranžovou); změna `…, ale karta se od té doby změnila (4 soubory jinak) —
  doplnit nejde, jen celá kopie` (oranžová); nedokončená `Nedokončená záloha 6. 10. 2026 8:15 na … — kopie se přerušila, lze ji
  dokončit` (oranžová); prázdná karta `Karta je prázdná — dřív zálohováno … na …` (neutrální). **Mac `diskName`:** „<složka> (<jméno
  svazku>)“, kořen svazku jen jméno svazku (Windows „Zaloha (D:)“); CLI plné cesty.
- Názvy z klipů (bez ohledu na velikost písmen, výstup velkými): Blackmagic `^([A-Z])(\d{3})_\d{8}_C(\d{3})`, RED
  `^([A-Z])(\d{3})_C(\d{3})`, ARRI/Sony Venice/Canon `^([A-Z])(\d{3})C(\d{3,4})` (v tomto pořadí). Jedna cívka všech
  rozpoznaných klipů (bez proxy kamery) → návrh s nápovědou `podle názvů klipů (A001C003…)`; víc → bez návrhu, poznámka
  `Na kartě je víc cívek: A001, A002`. „Další v řadě“: záznamy historie se stejným datem a projektem (bez ohledu na velikost
  písmen; `nil` = Bez projektu) a jménem `^[A-Z]\d{3}$`, právě jedno písmeno → písmeno + (max + 1), nápověda
  `další v řadě po A001 (dnes)` (jiný den `… (2026-10-07)`); jména už napsaná u jiných karet téhož startu se počítají jako obsazená;
  `A999` → nic.
- `CardInspector` jeden `Scanner.scan` (zrušitelný), otisk, kandidát z historie (`findForCard`), seznam souborů jen když se
  otisk liší, návrh názvu. Jen čte; **nikdy na hlavním vlákně**.

- [ ] **Step 1: Padající testy** — `HistoryMatcherTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class HistoryMatcherTests: XCTestCase {
    let t = Date(timeIntervalSince1970: 1_791_000_000)
    func f(_ p: String, _ s: Int64, _ d: Date) -> SourceFile { SourceFile(relPath: p, size: s, modified: d) }
    func scan(_ files: [SourceFile]) -> SourceScan { SourceScan(root: "/c", files: files, dirs: [], skipped: []) }
    func entry(_ fp: String) -> HistoryEntry { var e = HistoryEntry(id: "x", source: "/c", cardName: "A001", date: "2026-10-08", finished: t); e.fingerprint = fp; return e }

    func testKinds() {
        let stored = [f("A", 1, t), f("B", 2, t)]
        XCTAssertEqual(HistoryMatcher.match(scan(stored), fingerprint: "same", candidate: entry("same"), candidateFiles: nil, filesystem: "exfat").kind, .identical)
        let more = HistoryMatcher.match(scan(stored + [f("C", 3, t)]), fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "exfat")
        XCTAssertEqual(more.kind, .newFiles)
        XCTAssertEqual(more.new.map(\.relPath), ["C"])
        XCTAssertEqual(more.newBytes, 3)
        let changed = HistoryMatcher.match(scan([f("A", 9, t), f("B", 2, t)]), fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "exfat")
        XCTAssertEqual(changed.kind, .changed)
        XCTAssertEqual(HistoryMatcher.match(scan(stored), fingerprint: "x", candidate: entry("y"), candidateFiles: nil, filesystem: nil).kind, .notCompared)
    }

    func testFatHourShiftIsNotAChangeButExfatIs() {
        let stored = [f("A", 1, t)]
        let shifted = scan([f("A", 1, t.addingTimeInterval(3600))])
        XCTAssertEqual(HistoryMatcher.match(shifted, fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "msdos").kind, .identical)
        XCTAssertEqual(HistoryMatcher.match(shifted, fingerprint: "x", candidate: entry("y"), candidateFiles: stored, filesystem: "exfat").kind, .changed)
    }

    func testAnotherCardIsChangedNeverNewFiles() {
        let m = HistoryMatcher.match(scan([f("X", 1, t)]), fingerprint: "x", candidate: entry("y"), candidateFiles: [f("A", 1, t)], filesystem: "exfat")
        XCTAssertEqual(m.kind, .changed)
    }

    func testHistoryLines() {
        var e = entry("y")
        e.success = true; e.verified = true
        e.destinations = [HistoryDestination(root: "/Volumes/RAID/Zaloha", cardPath: "/Volumes/RAID/Zaloha/2026-10-08/A001")]
        let m = HistoryMatch(entry: e, kind: .newFiles, new: [f("C", 3_200_000_000, t)], newBytes: 3_200_000_000, changed: [], removed: 0)
        let (text, tone) = HistoryText.line(m)
        XCTAssertTrue(text.hasPrefix("Zálohováno \(HistoryText.when(t)) na Zaloha (RAID) — ověřeno, lze formátovat · nové soubory: 1 (3,2 GB)"), text)
        XCTAssertEqual(tone, .warning)
        e.unfinished = true
        XCTAssertTrue(HistoryText.line(HistoryMatch(entry: e, kind: .identical, new: [], newBytes: 0, changed: [], removed: 0)).0.hasPrefix("Nedokončená záloha"))
    }
}
```

`ReelNamesTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class ReelNamesTests: XCTestCase {
    func testPatterns() {
        XCTAssertEqual(ReelNames.fromClipName("A001C003_210101_R1AB"), "A001")
        XCTAssertEqual(ReelNames.fromClipName("a001_c003_0101ab_001"), "A001")
        XCTAssertEqual(ReelNames.fromClipName("A001_08251530_C001"), "A001")
        for no in ["C0001", "A01C003", "AA001C003", "clip"] { XCTAssertNil(ReelNames.fromClipName(no), no) }
    }

    func clip(_ name: String, proxy: Bool = false) -> Clip {
        Clip(name: name, path: "CLIP/\(name).mov", format: .video, segments: ["CLIP/\(name).mov"], bytes: 1, isCameraProxy: proxy)
    }

    func testSuggestionFromClips() {
        let one = ReelSuggestion.fromClips([clip("A001C001"), clip("A001C002"), clip("B007C001", proxy: true)])
        XCTAssertEqual(one.suggestion, NameSuggestion(name: "A001", hint: "podle názvů klipů (A001C001…)"))
        let two = ReelSuggestion.fromClips([clip("A001C001"), clip("A002C001")])
        XCTAssertNil(two.suggestion)
        XCTAssertEqual(two.note, "Na kartě je víc cívek: A001, A002")
    }

    func testNextInLine() {
        func e(_ name: String, _ date: String, _ project: String? = nil) -> HistoryEntry {
            var x = HistoryEntry(id: name, source: "", cardName: name, date: date, finished: Date()); x.project = project; return x
        }
        let h = [e("A001", "2026-10-08"), e("A002", "2026-10-08"), e("A009", "2026-10-07"), e("B001", "2026-10-08", "Film X")]
        XCTAssertEqual(ReelSuggestion.next(h, date: "2026-10-08", project: nil, today: "2026-10-08", taken: []),
                       NameSuggestion(name: "A003", hint: "další v řadě po A002 (dnes)"))
        XCTAssertEqual(ReelSuggestion.next(h, date: "2026-10-08", project: nil, today: "2026-10-08", taken: ["a003"])?.name, "A004")
        XCTAssertEqual(ReelSuggestion.next(h, date: "2026-10-08", project: "film x", today: "2026-10-09", taken: [])?.hint, "další v řadě po B001 (2026-10-08)")
        XCTAssertNil(ReelSuggestion.next(h + [e("C001", "2026-10-08")], date: "2026-10-08", project: nil, today: "2026-10-08", taken: []), "two letters")
    }
}
```

- [ ] **Step 2: Implementace** — jádro (`HistoryMatcher.swift`):

```swift
import Foundation

public enum HistoryMatchKind: Equatable { case identical, newFiles, changed, notCompared }

public struct HistoryMatch {
    public let entry: HistoryEntry
    public let kind: HistoryMatchKind
    public let new: [SourceFile]
    public let newBytes: Int64
    public let changed: [String]
    public let removed: Int
}

public enum HistoryMatcher {
    public static func match(_ scan: SourceScan, fingerprint: String, candidate: HistoryEntry, candidateFiles: [SourceFile]?, filesystem: String?) -> HistoryMatch {
        if candidate.fingerprint == fingerprint { return HistoryMatch(entry: candidate, kind: .identical, new: [], newBytes: 0, changed: [], removed: 0) }
        guard let stored = candidateFiles else { return HistoryMatch(entry: candidate, kind: .notCompared, new: [], newBytes: 0, changed: [], removed: 0) }
        let local = FileTimes.storesLocalTime(filesystem)
        var known = Dictionary(stored.map { ($0.relPath, $0) }, uniquingKeysWith: { _, b in b })
        let storedCount = known.count
        var added: [SourceFile] = [], changed: [String] = []
        for f in scan.files {
            guard let k = known.removeValue(forKey: f.relPath) else { added.append(f); continue }
            if k.size != f.size || !FileTimes.same(k.modifiedUtc, f.modifiedUtc, localTime: local) { changed.append(f.relPath) }
        }
        if storedCount > 0 && known.count == storedCount && !added.isEmpty {
            return HistoryMatch(entry: candidate, kind: .changed, new: [], newBytes: 0, changed: added.map(\.relPath), removed: known.count)
        }
        let kind: HistoryMatchKind = !changed.isEmpty ? .changed : !added.isEmpty ? .newFiles : .identical
        return HistoryMatch(entry: candidate, kind: kind, new: added, newBytes: added.reduce(0) { $0 + $1.size }, changed: changed, removed: known.count)
    }
}
```

`ReelNames.swift` (vzory výše; `NSRegularExpression` s `.caseInsensitive`, `match` vrací cívku velkými a shodu jako `prefix`),
`ReelSuggestion.fromClips/next` podle pravidel; `ARRIRAW` sekvence `.ari` se posuzuje podle názvu složky (= jméno klipu z
`ClipDetector`). `HistoryText.swift` s texty výše (`when`: `DateFormatter` `d. M. yyyy H:mm`, `cs_CZ`, místní časové pásmo;
množná čísla přes `plural`; velikosti `formatBytesCs`); `diskName`:

```swift
    /// "Zaloha (RAID)": the destination folder and its volume; a volume root alone is its volume name.
    public static func diskName(_ root: String) -> String {
        let path = root.trimmingTrailingSlash()
        let volume = (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeNameKey]))?.volumeName
            ?? (path.hasPrefix("/Volumes/") ? path.split(separator: "/").dropFirst().first.map(String.init) : nil)
        let name = (path as NSString).lastPathComponent
        if let m = VolumeFacts.mount(of: path), m.mountPoint.trimmingTrailingSlash() == path { return volume ?? name }
        if path.hasPrefix("/Volumes/"), path.split(separator: "/").count == 2 { return volume ?? name }
        return volume.map { "\(name) (\($0))" } ?? name
    }
```

`CardInspector.inspect`: `Scanner.scan` (s `isCancelled` po každých 1 000 položkách — doplnit do `Scanner` volitelný parametr),
`CardFingerprint.compute`, `CardVolume.of(source)` (jen u kořene svazku), kandidát `store.findForCard(volume?.key, shape:, in: history)`,
seznam `store.readFileList` jen když se otisk liší, `HistoryMatcher.match`, `ReelSuggestion.fromClips(ClipDetector.detect(scan.files))`.

- [ ] **Step 3: Testy projdou; záznam a commit** — Run: `swift test`. `docs/VYVOJ-MAC.md`: `- Úkol 43 — shoda karty s historií
  (stejná / nové soubory / změněná / jiná karta, FAT tolerance), texty řádku historie, název karty z klipů a další v řadě,
  prohlídka karty`. Run: `git add -A mac docs && git commit -m "Mac: history match, card inspection, card names from clips"`

---

### Task 44: Zápis historie z úlohy, CLI `--no-history`, `--name auto`

**Files:**
- Modify: `mac/Sources/OffloadKit/Job.swift` (`JobConfig.recordHistory`, `historyFolder`, `project`; `run`: začátek, konec, zrušení)
- Create: `mac/Sources/OffloadKit/History/HistoryRecorder.swift`
- Modify: `mac/Sources/offload/main.swift` (`--no-history`, `--name auto`, řádek historie před plánem)
- Create: `mac/Tests/OffloadKitTests/HistoryJobTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `HistoryStore`, `HistoryEntry` (úkol 42), `CardInspector`, `HistoryText`, `ReelSuggestion` (úkol 43), `CardVolume` (úkol 36), `BackupState` (úkol 38).
- Produces:
  - `JobConfig.recordHistory = true`, `JobConfig.historyFolder: String?` (testy), `JobConfig.project: String?` (plní úkol 56)
  - `enum HistoryRecorder { static func start(_ plan: OffloadPlan, config: JobConfig, volume: CardVolume?, store: HistoryStore) -> String?; static func finish(id:plan:report:config:volume:scan:store:kind:) -> HistoryWrite; static func forgetStart(id:store:) }`
  - `JobStatus.historyWarning` → přes `warning` (texty níže)
  - CLI: `offload copy … [--no-history] [--name auto]`

Pravidla (FORMATY „Místní soubory“; Windows R27, R6):

- Zapisuje GUI i CLI (`--no-history` vypne čtení i zápis). Celá kopie zapíše **na začátku** záznam s `unfinished: true`
  (kopie, kterou aplikace nedokončí, je pak známá a karta nabídne „Dokončit přerušenou zálohu“ — úkol 69), na konci ho
  nahradí: `success`, `verified` (= `CardRecord.readBack` všech souborů na všech cílech, tedy totéž co „Lze formátovat“ kromě
  pravidla o souborech mimo zálohu), `failedFiles`, `files`/`bytes`, `fingerprint`/`shape` (karta při **startu** — úloha ji
  nečte znovu), seznam souborů, `generations[0]` (`kind: "full"`), `reels` (cívky z názvů klipů), `card` (svazek; u složky
  `null`), `project` (projekt úlohy, plní úkol 56). **Zrušená kopie** záznam ze startu smaže (nezapisuje se).
- Zámek historie: úloha čeká až 60 s; když se nedočká, zapíše záznam **na pozadí** (opakuje po 5 s, dokud aplikace běží)
  a kartě úlohy dá varování `Historie karet je právě zaneprázdněná — záznam této zálohy se zapíše, jakmile to půjde (aplikaci
  zatím nezavírejte).`; když se nepovede ani tak (aplikace končí), `Záznam této zálohy se do historie karet nepodařilo zapsat
  (historie byla dlouho zaneprázdněná) — záloha na discích je v pořádku, jen ji karta při dalším vložení nepozná.` — nikdy se
  tiše neztratí (log). CLI čeká 60 s a pak vypíše varování a skončí bez zápisu.
- Doplnění (úkol 47) a oprava (úkol 62) upraví **existující** záznam jedním držením zámku: nová položka `generations`
  (`kind` `append` / `repair`), `files`/`bytes`/`fileList` = sjednocení se starým seznamem, `fingerprint`/`shape` = karta teď,
  `finished` = teď, `project` se **nemění**; po opravě `success`/`failedFiles` znovu z reportů (`BackupState`).
- CLI: před plánem řádek `HistoryText.line(…, fullPaths: true)` (bez dotazu — skript nemá kdo potvrdit). `--name auto`:
  návrh z klipů, jinak jmenovka; vypíše `Msg.cardNameAuto(name)` nebo `Msg.cardNameAutoNone`. Bez `--name` jako dosud.

- [ ] **Step 1: Padající testy** — `HistoryJobTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class HistoryJobTests: XCTestCase {
    func job(_ dir: TempDir, history: String, cancelAtFile: Int? = nil) throws -> OffloadJob {
        let card = dir.sub("card"), dest = dir.sub("dest")
        _ = TestFS.writeRandom(card + "/CLIP/A001C001.mov", size: 2000, seed: 1)
        _ = TestFS.writeRandom(card + "/CLIP/A001C002.mov", size: 2000, seed: 2)
        try FileManager.default.createDirectory(atPath: dest, withIntermediateDirectories: true)
        var cfg = JobConfig(source: card, destRoots: [dest]); cfg.analyzeClips = false; cfg.historyFolder = history
        var o = OffloadJob.engineOptions(cfg); o.allowSourceVolume = true
        weak var ref: OffloadJob?
        if let n = cancelAtFile { o.beforeFileCopy = { i in if i == n { ref?.cancel() } } }
        let job = OffloadJob(config: cfg, options: o)
        ref = job
        return job
    }

    func testFinishedCopyRecorded() throws {
        let dir = tempDir()
        let j = try job(dir, history: dir.sub("hist"))
        j.run()
        let e = try XCTUnwrap(HistoryStore(folder: dir.sub("hist")).load().first)
        XCTAssertFalse(e.unfinished)
        XCTAssertTrue(e.success); XCTAssertTrue(e.verified)
        XCTAssertNil(e.card, "a folder source")
        XCTAssertEqual(e.files, 2)
        XCTAssertEqual(e.reels, ["A001"])
        XCTAssertEqual(e.generations.map(\.kind), ["full"])
        XCTAssertNotNil(HistoryStore(folder: dir.sub("hist")).readFileList(e))
    }

    func testCancelledCopyNotRecorded() throws {
        let dir = tempDir()
        let j = try job(dir, history: dir.sub("hist"), cancelAtFile: 1)
        j.run()
        XCTAssertEqual(j.status().phase, .cancelled)
        XCTAssertTrue(HistoryStore(folder: dir.sub("hist")).load().isEmpty, "the start entry is removed")
    }

    func testBusyHistoryGivesAWarningNeverLosesTheEntry() throws {
        let dir = tempDir()
        let held = try XCTUnwrap(FileLock.tryLock(Locks.history(dir.sub("hist"))))
        let j = try job(dir, history: dir.sub("hist"))
        HistoryRecorder.waitLimit = 0.2
        j.run()
        XCTAssertEqual(j.status().warning, "Historie karet je právě zaneprázdněná — záznam této zálohy se zapíše, jakmile to půjde (aplikaci zatím nezavírejte).")
        held.release()
        XCTAssertTrue(HistoryRecorder.drain(timeout: 5), "the background write gets through")
        XCTAssertEqual(HistoryStore(folder: dir.sub("hist")).load().count, 1)
        HistoryRecorder.waitLimit = 60
    }
}
```

- [ ] **Step 2: Implementace** — `HistoryRecorder` (nové) a napojení v `OffloadJob.run`: `start` po `prepare`, těsně před
  `engine.run` (jen celá kopie a `recordHistory`), `finish` po fázi 1 (úspěch i s chybami), `forgetStart` při zrušení.
  Fronta zápisů na pozadí: `static var pending: [() -> HistoryWrite]`, jedno sériové vlákno, opakování po 5 s; `drain(timeout:)`
  pro testy a pro `applicationShouldTerminate` (aplikace před ukončením počká nejvýš 10 s a pak zapíše do logu
  `HistoryNotWritten`). Kód `finish`:

```swift
    static func entry(_ e: HistoryEntry?, id: String, plan: OffloadPlan, report: OffloadReport, config: JobConfig, volume: CardVolume?,
                      scan: SourceScan, kind: String) -> HistoryEntry {
        var x = e ?? HistoryEntry(id: id, source: plan.source, cardName: plan.cardName, date: plan.date, finished: report.finished)
        let (fp, shape) = CardFingerprint.compute(scan.files)
        let records = plan.cardPaths.map { CardRecord.load($0) }
        let verified = zip(plan.cardPaths, records).allSatisfy { p, r in
            r.files.values.allSatisfy { CardRecord.readBack($0, cardPath: p, serial: r.volumeSerial) } && !r.incomplete
        }
        x.card = volume.map { HistoryCard(identity: $0.identity, fsSerial: $0.fsSerial, capacity: $0.capacity, label: $0.label, filesystem: $0.filesystem) }
        x.finished = report.finished
        x.success = report.success
        x.failedFiles = report.success ? nil : report.failedFiles.count
        x.verified = report.success && verified
        x.hashAlgorithms = report.algorithms
        x.files = scan.files.count
        x.bytes = scan.totalBytes
        x.fingerprint = fp
        x.shape = shape
        x.destinations = zip(plan.destRoots, plan.cardPaths).map { HistoryDestination(root: $0, cardPath: $1) }
        x.generations = [HistoryGeneration(seq: 1, kind: kind, finished: report.finished, files: report.files.count,
                                           bytes: report.totalBytes, verified: report.safeToFormat)]
        x.reels = Array(Set(ClipDetector.detect(scan.files).compactMap { ReelNames.fromClipName($0.name) })).sorted()
        x.project = config.project
        x.unfinished = false
        return x
    }
```

CLI `main.swift` (`Copy`): volby `@Flag var noHistory = false`, `--name` přijme `auto`; před `prepare` (když není
`--no-history`) `CardInspector.inspect` a výpis řádku historie; `cfg.recordHistory = !noHistory`.

- [ ] **Step 3: Testy projdou; ruční kontrola** — Run: `swift test`. Ručně: `offload copy <karta> -d <cíl>` dvakrát → podruhé
  řádek `Zálohováno … na <cíl> — ověřeno, lze formátovat`; `cat ~/Library/Application\ Support/Offload/history.json`.

- [ ] **Step 4: Záznam a commit** — `docs/VYVOJ-MAC.md`: `- Úkol 44 — úloha i CLI zapisují historii (nedokončeno od startu,
  konec kopie, zrušená se smaže; zaneprázdněná historie = zápis na pozadí s varováním), CLI řádek historie, --no-history,
  --name auto`. Run: `git add -A mac docs && git commit -m "Mac: jobs and the CLI record card history"`

---

### Task 45: Ověřit zálohu — všechny generace MHL, report ověření a jeho umístění, CLI `verify`

**Files:**
- Create: `mac/Sources/OffloadKit/Verification/BackupVerifier.swift`, `Verification/VerifyReport.swift`
- Modify: `mac/Sources/OffloadKit/Verifier.swift` (zůstane jen tenká obálka nad `BackupVerifier`, nebo smazat a CLI přepojit)
- Modify: `mac/Sources/offload/main.swift` (`Verify`: `--report`, složka dne, strojové řádky, český souhrn, kód 0/1)
- Modify: `mac/Sources/OffloadKit/PDFReport.swift` (`writeHTML(_:to:)` pro libovolné HTML)
- Create: `mac/Tests/OffloadKitTests/BackupVerifierTests.swift`, `mac/Tests/OffloadKitTests/VerifyReportTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `MHLReader`, `MHLHistory` (úkol 40), `HashAlgo`, `RunningHash` (úkol 32), `CardRecord` (úkol 38), `Scanner.scan`, `IO.openRead/pread`, `formatBytesCs`.
- Produces:
  - `public struct VerifyResult { cardPath; started; finished; basis: String; generations: Int; hashAlgorithms: [String]; totalFiles; totalBytes; ok; changed; missing; extra; failedAtCopy; failedSuperseded; unreadable: [(path: String, error: String)]; chainProblems; checked; bytesRead; cancelled: Bool; folderGone: Bool; reportInsideCard: Bool; unfinished: Bool; var success: Bool }`
  - `public enum BackupVerifier { static func verify(_ cardPath: String, chunkSize: Int = 16 << 20, progress: ((String, Int64) -> Void)?, isCancelled: () -> Bool) throws -> VerifyResult; static func cardFolders(in folder: String) -> [String]; static func ignored(_ relPath: String, _ patterns: [String]) -> Bool }`
  - `public enum VerifyReport { static func jsonData(_ r: VerifyResult) throws -> Data; static func html(_ r: VerifyResult) -> String; static func write(_ r: VerifyResult) throws -> (html: String, json: String, inside: Bool); static func summary(_ r: VerifyResult) -> (text: String, tone: CardTone); static func cliSummary(_ r: VerifyResult, seconds: Double) -> String; static func warnings(_ r: VerifyResult) -> [String]; static let unfinishedText: String }`

Pravidla (FORMATY „Report ověření“; Windows návrh §2.4, R8, R9; spec D.22.4):

- Každý soubor se čte **jednou** (`F_NOCACHE`) a hashuje **všemi** algoritmy, které pro něj záznam má (z `lastGood`, když
  nejnovější záznam je `failed`); velikost se kontroluje zvlášť (jiná velikost = `changed`). Bez `ascmhl/` základ
  `hashList` (`_REPORT/*.xxh64` a `*.md5`, tolerují `\r\n`). Bez obojího `OffloadError.preflight(Msg.noHashRecord(dir), code: .noHashRecord)`.
- `failedAtCopy` = nejnovější záznam `failed` a žádný platný starší; `failedSuperseded` = nejnovější `failed`, ale starší
  platný existuje (ověří se proti němu). `extra` = soubory na disku, které záznam nezná, mimo `_REPORT/`, `_PROXY/`, `ascmhl/`
  a mimo ignore vzory MHL (gitwildmatch: jméno bez `/` platí pro kteroukoli složku cesty, `name/` jen pro složku, vzor s `/`
  od kořene karty, `*`/`?` nepřekročí `/`, `**` ano, bez ohledu na velikost písmen; odpovídající složka ignoruje vše pod sebou).
- `success` = nic nesedí špatně, nechybí, není nečitelné a nic nezůstalo `failedAtCopy` (soubory navíc výsledek neovlivní).
- `unfinished` = nejnovější generace složky není dokončená (`verifyPending` nebo `verifyIncomplete` v jejím reportu, nebo
  `verifyPending` v reportu téže generace na jiném cíli — `CardRecord.incomplete`). I se `success` **není** záloha ověřená:
  souhrn oranžově `unfinishedText`, CLI kód 1. `basis: "pending"`, když generace 1 se ještě ověřuje (žádný záznam — nic k porovnání).
- **Nic se nezapisuje do `ascmhl/`.** Report ověření (volitelně, výchozí zapnuto): do `<karta>/_REPORT/` jako
  `<karta>_verify_<YYYY-MM-DD_HHMMSS>Z.html/.pdf` + `offload_verify_<stamp>Z.json` (čas začátku v UTC). **Výjimka:** když
  historie MHL `_REPORT` **neignoruje** (cizí program), report jde do `<cíl>/<datum>/_REPORT/` vedle složky karty a JSON se
  jmenuje `<karta>_verify_<stamp>Z.json`; aplikace i CLI řeknou `Report ověření je uložený mimo složku karty, aby nenarušil
  její historii ASC MHL: <cesta>`. Do karty se nezapisuje nikdy. Zrušené ověření report nezapíše.
- JSON (UTF-8 bez BOM, seřazené klíče, data ISO 8601 UTC): `tool`, `version`, `cardPath`, `started`, `finished`, `basis`,
  `generations`, `hashAlgorithms`, `totalFiles`, `totalBytes`, `ok`, `changed[]`, `missing[]`, `extra[]`, `failedAtCopy[]`,
  `failedSuperseded[]`, `unreadable[]` (`{path, error}`), `chainProblems[]`, `success`, `unfinished`.
- Texty (doslova Windows `VerifyReport.cs`; čísla s českým oddělovačem tisíců): základ `ASC MHL, 2 generace, xxHash64 + xxHash128`
  / `seznam součtů, xxHash64 + MD5` / u `pending` `záznam nedokončené generace ještě není zapsaný` (algoritmy `HashAlgo.label`
  spojené „ + “); souhrn `Ověřeno: všech 1 234 souborů sedí (<základ>).` (1: „soubor sedí“, 2–4: „všechny N soubory sedí“); s `unfinished`
  a `success` oranžově `<unfinishedText> Soubory se zapsaným záznamem sedí (N, <základ>).` (bez souborů jen `unfinishedText`); chyba `Nesedí 2 soubory, chybí 1, nečitelný 0 — podrobnosti
  v reportu ověření.`, jen selhání při kopii `Nelze ověřit: 1 soubor selhal už při kopii a v záznamu není žádný platný součet —
  podrobnosti v reportu ověření.`, zrušeno `Ověření zrušeno po 120 / 1 234 souborech`, varování `3 soubory navíc (nejsou v MHL).`,
  `1 soubor selhal už při kopii.`, `2 soubory mají v nejnovější generaci záznam „selhalo při kopii“ — ověřeno proti starší platné
  generaci.` + `chainProblems`; nedokončené `Záloha není dokončená: nejnovější generace se ověřovala s proxy a ověření neskončilo
  (nebo jeho výsledek není zapsaný na všech cílech). Kartu zatím neformátujte — vložte ji znovu a zvolte „Dokončit přerušenou zálohu“.`
  CLI souhrn `Ověřeno 1 234 souborů (512 GB) za 22:10 (385 MB/s) — vše v pořádku.` / `… — NALEZENY PROBLÉMY: nesedí 2, chybí 1,
  nečitelné 0, selhalo při kopii 0, navíc 3.` / `… — ZÁLOHA NEDOKONČENÁ: <unfinishedText>`; strojové řádky `MISMATCH  `,
  `MISSING   `, `ERROR     <cesta>: <chyba>`, `EXTRA     `, `FAILED-AT-COPY  `, `FAILED-SUPERSEDED  `, `CHAIN     ` (anglicky kvůli skriptům).
- Vybraná složka bez `ascmhl/` i `_REPORT/` s reportem, ale s podsložkami, které je mají (složka dne) → ověří se každá karta
  zvlášť; kód 1, když selže kterákoli.
- Disk odpojený během ověření → zbylé soubory `unreadable`, `folderGone`, souhrn přidá „disk se možná odpojil“.

- [ ] **Step 1: Padající testy** — `BackupVerifierTests.swift` (karty se staví přes `OffloadEngine` jako v úkolu 3, tedy se
  skutečným MHL; MHL ze „Silverstacku“ ručně):

```swift
import XCTest
@testable import OffloadKit

final class BackupVerifierTests: XCTestCase {
    /// A real offload of two files into `dest`; returns the card folder.
    func offload(_ dir: TempDir) throws -> String {
        let card = dir.sub("card"), dest = dir.sub("dest")
        _ = TestFS.writeRandom(card + "/CLIP/A.mov", size: 3000, seed: 1)
        _ = TestFS.writeRandom(card + "/CLIP/B.mov", size: 5000, seed: 2)
        try FileManager.default.createDirectory(atPath: dest, withIntermediateDirectories: true)
        var o = OffloadOptions(); o.allowSourceVolume = true
        let e = OffloadEngine(options: o)
        let plan = try e.prepare(source: card, destRoots: [dest], cardName: "A001", date: "2026-10-08")
        _ = try e.run(plan)
        return plan.cardPaths[0]
    }

    func testAllGood() throws {
        let r = try BackupVerifier.verify(try offload(tempDir()), progress: nil, isCancelled: { false })
        XCTAssertEqual(r.basis, "ascmhl")
        XCTAssertEqual(r.ok, 2)
        XCTAssertTrue(r.success)
        // "xxHash64" alone until the engine writes xxh128 too (task 51): then "xxHash64 + xxHash128".
        XCTAssertTrue(VerifyReport.summary(r).text.hasPrefix("Ověřeno: všechny 2 soubory sedí (ASC MHL, 1 generace, xxHash64"))
    }

    func testChangedMissingExtra() throws {
        let card = try offload(tempDir())
        let fh = try FileHandle(forUpdating: URL(fileURLWithPath: card + "/CLIP/A.mov"))
        try fh.seek(toOffset: 10); try fh.write(contentsOf: Data([0xFF])); try fh.close()
        try FileManager.default.removeItem(atPath: card + "/CLIP/B.mov")
        try Data("x".utf8).write(to: URL(fileURLWithPath: card + "/CLIP/new.txt"))
        try Data("x".utf8).write(to: URL(fileURLWithPath: card + "/_PROXY_note.txt"))
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        XCTAssertEqual(r.changed, ["CLIP/A.mov"])
        XCTAssertEqual(r.missing, ["CLIP/B.mov"])
        XCTAssertEqual(r.extra.sorted(), ["CLIP/new.txt", "_PROXY_note.txt"])
        XCTAssertFalse(r.success)
    }

    func testHashListFallbackWithCRLF() throws {
        let card = try offload(tempDir())
        try FileManager.default.removeItem(atPath: card + "/ascmhl")
        let list = card + "/_REPORT/A001.xxh64"
        let text = try String(contentsOfFile: list, encoding: .utf8).replacingOccurrences(of: "\n", with: "\r\n")
        try text.write(toFile: list, atomically: true, encoding: .utf8)
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        XCTAssertEqual(r.basis, "hashList")
        XCTAssertEqual(r.generations, 0)
        XCTAssertTrue(r.success)
    }

    func testCancelledWritesNoReport() throws {
        let card = try offload(tempDir())
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { true })
        XCTAssertTrue(r.cancelled)
        XCTAssertThrowsError(try VerifyReport.write(r))
    }

    func testUnfinishedNeverOk() throws {
        let card = try offload(tempDir())
        var rep = try OffloadReport.read(card + "/_REPORT/offload_report.json")
        rep.verifyIncomplete = true
        try rep.jsonData().write(to: URL(fileURLWithPath: card + "/_REPORT/offload_report.json"))
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        XCTAssertTrue(r.unfinished)
        XCTAssertEqual(VerifyReport.summary(r).tone, .warning)
        XCTAssertTrue(VerifyReport.cliSummary(r, seconds: 1).contains("ZÁLOHA NEDOKONČENÁ"))
    }

    func testIgnorePatterns() {
        XCTAssertTrue(BackupVerifier.ignored("a/b/.DS_Store", [".DS_Store"]))
        XCTAssertTrue(BackupVerifier.ignored("_REPORT/x.html", ["_REPORT"]))
        XCTAssertTrue(BackupVerifier.ignored("x/tmp/y", ["tmp/"]))
        XCTAssertFalse(BackupVerifier.ignored("tmp", ["tmp/"]), "name/ is a folder only")
        XCTAssertTrue(BackupVerifier.ignored("CLIP/a.TMP", ["*.tmp"]))
        XCTAssertFalse(BackupVerifier.ignored("CLIP/a.tmp", ["/*.tmp"]), "anchored at the card folder")
        XCTAssertTrue(BackupVerifier.ignored("a/b/c.x", ["a/**/c.x"]))
    }

    func testDayFolderMeansEveryCard() throws {
        let card = try offload(tempDir())
        XCTAssertEqual(BackupVerifier.cardFolders(in: (card as NSString).deletingLastPathComponent), [card])
        XCTAssertEqual(BackupVerifier.cardFolders(in: card), [card])
    }
}
```

`VerifyReportTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class VerifyReportTests: XCTestCase {
    // Review focus 4.
    func testNothingWrittenIntoAscmhl() throws {
        let card = try BackupVerifierTests().offload(tempDir())
        let before = TestFS.files(in: card + "/ascmhl")
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        let (html, json, inside) = try VerifyReport.write(r)
        XCTAssertTrue(inside)
        XCTAssertTrue(html.hasPrefix(card + "/_REPORT/A001_verify_"))
        XCTAssertTrue((json as NSString).lastPathComponent.hasPrefix("offload_verify_"))
        XCTAssertEqual(TestFS.files(in: card + "/ascmhl"), before)
    }

    func testForeignMhlPutsReportBesideTheCard() throws {
        let card = try BackupVerifierTests().offload(tempDir())
        // A tool that ignores only .DS_Store and ascmhl (ascmhl 1.0.1): rewrite the ignore list of generation 1 and its C4.
        let chainDir = card + "/ascmhl"
        let chain = try XCTUnwrap(MHLReader.readChain(chainDir))
        let file = chainDir + "/" + chain[0].path
        var text = try String(contentsOfFile: file, encoding: .utf8)
        for p in ["_REPORT", "_PROXY", "ascmhl/"] { text = text.replacingOccurrences(of: "<pattern>\(p)</pattern>\n", with: "") }
        try text.write(toFile: file, atomically: true, encoding: .utf8)
        let c4 = MHL.c4(Data(text.utf8))
        let chainText = try String(contentsOfFile: chainDir + "/ascmhl_chain.xml", encoding: .utf8).replacingOccurrences(of: chain[0].c4, with: c4)
        try chainText.write(toFile: chainDir + "/ascmhl_chain.xml", atomically: true, encoding: .utf8)
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        XCTAssertFalse(r.reportInsideCard)
        let (html, json, inside) = try VerifyReport.write(r)
        XCTAssertFalse(inside)
        let day = (card as NSString).deletingLastPathComponent
        XCTAssertTrue(html.hasPrefix(day + "/_REPORT/A001_verify_"))
        XCTAssertTrue((json as NSString).lastPathComponent.hasPrefix("A001_verify_"))
        XCTAssertFalse(TestFS.files(in: card + "/_REPORT").contains { $0.contains("_verify_") }, "nothing inside the card")
    }

    func testJsonShape() throws {
        let card = try BackupVerifierTests().offload(tempDir())
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        let obj = try XCTUnwrap(JSONSerialization.jsonObject(with: VerifyReport.jsonData(r)) as? [String: Any])
        XCTAssertEqual(obj.keys.sorted(), ["basis", "cardPath", "chainProblems", "changed", "extra", "failedAtCopy", "failedSuperseded",
                                           "finished", "generations", "hashAlgorithms", "missing", "ok", "started", "success", "tool",
                                           "totalBytes", "totalFiles", "unfinished", "unreadable", "version"])
    }
}
```

- [ ] **Step 2: Implementace** — `Verification/BackupVerifier.swift` (jádro):

```swift
import Foundation

public struct VerifyResult {
    public var cardPath: String
    public var started = Date()
    public var finished = Date()
    public var basis = "ascmhl"
    public var generations = 0
    public var hashAlgorithms: [String] = []
    public var totalFiles = 0
    public var totalBytes: Int64 = 0
    public var ok = 0
    public var changed: [String] = []
    public var missing: [String] = []
    public var extra: [String] = []
    public var failedAtCopy: [String] = []
    public var failedSuperseded: [String] = []
    public var unreadable: [(path: String, error: String)] = []
    public var chainProblems: [String] = []
    public var checked = 0
    public var bytesRead: Int64 = 0
    public var cancelled = false
    public var folderGone = false
    public var reportInsideCard = true
    public var unfinished = false
    public var success: Bool { changed.isEmpty && missing.isEmpty && unreadable.isEmpty && failedAtCopy.isEmpty }
    public init(cardPath: String) { self.cardPath = cardPath }
}

public enum BackupVerifier {
    struct Expected { let path: String; let size: Int64?; let hashes: [MHLHash] }
    static let ours = [Layout.reportDirName + "/", Layout.proxyDirName + "/", MHL.dirName + "/"]

    public static func verify(_ cardPath: String, chunkSize: Int = 16 << 20, progress: ((String, Int64) -> Void)? = nil,
                              isCancelled: () -> Bool = { false }) throws -> VerifyResult {
        var r = VerifyResult(cardPath: cardPath)
        r.started = Date()
        var expected: [Expected] = []
        var ignore: [String] = []
        if let mhl = MHLReader.read(cardPath) {
            r.basis = "ascmhl"
            r.generations = mhl.generations.count
            r.chainProblems = mhl.chainProblems
            r.reportInsideCard = mhl.ignoresReportFolder
            ignore = mhl.ignore
            for (key, rec) in mhl.files.sorted(by: { $0.key < $1.key }) {
                if rec.failed {
                    if let good = mhl.lastGood[key] {
                        r.failedSuperseded.append(rec.path)
                        expected.append(Expected(path: good.path, size: good.size, hashes: good.hashes))
                    } else {
                        r.failedAtCopy.append(rec.path)
                    }
                } else {
                    expected.append(Expected(path: rec.path, size: rec.size, hashes: rec.hashes))
                }
            }
        } else if let lists = hashLists(cardPath) {
            r.basis = "hashList"
            expected = lists
        } else if CardRecord.load(cardPath).verifyPending {
            r.basis = "pending"
        } else {
            throw OffloadError.preflight(Msg.noHashRecord(cardPath + "/" + Layout.reportDirName), code: .noHashRecord)
        }
        r.unfinished = r.basis == "pending" || CardRecord.load(cardPath).incomplete
            || CardRecord.load(cardPath).generations.last?.report.verifyPending == true
        r.hashAlgorithms = Array(Set(expected.flatMap { $0.hashes.map(\.algorithm) })).sorted { HashAlgo.known.firstIndex(of: $0) ?? 9 < HashAlgo.known.firstIndex(of: $1) ?? 9 }
        r.totalFiles = expected.count + r.failedAtCopy.count
        r.totalBytes = expected.reduce(0) { $0 + ($1.size ?? 0) }
        let buf = IO.allocateAligned(chunkSize)
        defer { free(buf) }
        for e in expected {
            if isCancelled() { r.cancelled = true; break }
            r.checked += 1
            let full = cardPath + "/" + e.path
            if !FileManager.default.fileExists(atPath: cardPath) { r.folderGone = true }
            guard FileManager.default.fileExists(atPath: full) else {
                if r.folderGone { r.unreadable.append((e.path, "složka karty zmizela")) } else { r.missing.append(e.path) }
                continue
            }
            let running = e.hashes.compactMap { h in HashAlgo.make(h.algorithm).map { (h, $0) } }
            do {
                let fd = try IO.openRead(full)
                defer { close(fd) }
                var off: Int64 = 0
                while true {
                    if isCancelled() { r.cancelled = true; break }
                    let n = try IO.pread(fd, buf, chunkSize, off, path: full)
                    if n == 0 { break }
                    running.forEach { $0.1.update(buf, n) }
                    off += Int64(n); r.bytesRead += Int64(n)
                    progress?(e.path, r.bytesRead)
                }
                if r.cancelled { break }
                let sizeOK = e.size.map { $0 == off } ?? true
                let hashOK = running.allSatisfy { HashAlgo.equal($0.0.algorithm, $0.0.value, $0.1.value()) }
                if sizeOK && hashOK && !running.isEmpty { r.ok += 1 } else { r.changed.append(e.path) }
            } catch {
                r.unreadable.append((e.path, "\(error)"))
            }
        }
        if !r.cancelled { addExtra(&r, cardPath, expected.map(\.path) + r.failedAtCopy, ignore) }
        r.finished = Date()
        return r
    }

    static func hashLists(_ cardPath: String) -> [Expected]? {
        let dir = cardPath + "/" + Layout.reportDirName
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: dir) else { return nil }
        var byPath: [String: Expected] = [:]
        var any = false
        for name in names.sorted() {
            let algorithm = name.hasSuffix(".xxh64") ? "xxh64" : name.hasSuffix(".md5") ? "md5" : nil
            guard let algorithm, let text = try? String(contentsOfFile: dir + "/" + name, encoding: .utf8) else { continue }
            any = true
            for raw in text.split(separator: "\n") {
                let line = raw.hasSuffix("\r") ? raw.dropLast() : raw[...]
                guard let sep = line.range(of: "  ") else { continue }
                let path = String(line[sep.upperBound...]), value = String(line[..<sep.lowerBound])
                let had = byPath[path.lowercased()]?.hashes ?? []
                byPath[path.lowercased()] = Expected(path: path, size: nil, hashes: had + [MHLHash(algorithm: algorithm, value: value, action: "original")])
            }
        }
        return any ? byPath.values.sorted { $0.path < $1.path } : nil
    }

    static func addExtra(_ r: inout VerifyResult, _ cardPath: String, _ known: [String], _ ignore: [String]) {
        let k = Set(known.map { $0.lowercased() })
        guard let scan = try? Scanner.scan(cardPath) else { return }
        r.extra = scan.files.map(\.relPath).filter { p in
            !k.contains(p.lowercased()) && !ours.contains { p.lowercased().hasPrefix($0.lowercased()) } && !ignored(p, ignore)
        }
    }

    /// ASC MHL ignore patterns (gitwildmatch).
    public static func ignored(_ relPath: String, _ patterns: [String]) -> Bool {
        let parts = relPath.split(separator: "/").map(String.init)
        for raw in patterns {
            let folderOnly = raw.hasSuffix("/")
            let pattern = folderOnly ? String(raw.dropLast()) : raw
            if pattern.isEmpty { continue }
            if pattern.contains("/") {
                let body = wildcard(pattern.hasPrefix("/") ? String(pattern.dropFirst()) : pattern)
                if relPath.range(of: "^" + body + (folderOnly ? "/.*" : "(/.*)?") + "$", options: [.regularExpression, .caseInsensitive]) != nil { return true }
                continue
            }
            let name = "^" + wildcard(pattern) + "$"
            let candidates = folderOnly ? Array(parts.dropLast()) : parts
            if candidates.contains(where: { $0.range(of: name, options: [.regularExpression, .caseInsensitive]) != nil }) { return true }
        }
        return false
    }

    static func wildcard(_ glob: String) -> String {
        var rx = ""
        let c = Array(glob)
        var i = 0
        while i < c.count {
            if c[i] == "*", i + 1 < c.count, c[i + 1] == "*" {
                i += 1
                if i + 1 < c.count, c[i + 1] == "/" { i += 1; rx += "(.*/)?" } else { rx += ".*" }
            } else if c[i] == "*" { rx += "[^/]*" }
            else if c[i] == "?" { rx += "[^/]" }
            else { rx += NSRegularExpression.escapedPattern(for: String(c[i])) }
            i += 1
        }
        return rx
    }

    /// A card folder (ascmhl/, or a report or hash list in _REPORT/) is itself; a day folder means every card folder in it.
    public static func cardFolders(in folder: String) -> [String] {
        func isCard(_ p: String) -> Bool {
            let fm = FileManager.default
            if fm.fileExists(atPath: p + "/" + MHL.dirName) { return true }
            let names = (try? fm.contentsOfDirectory(atPath: p + "/" + Layout.reportDirName)) ?? []
            return names.contains { $0 == CardRecord.mainReport || $0.hasSuffix(".xxh64") || $0.hasSuffix(".md5") }
        }
        if isCard(folder) { return [folder] }
        let subs = ((try? FileManager.default.contentsOfDirectory(atPath: folder)) ?? []).filter { !$0.hasPrefix(".") }.sorted()
        return subs.map { folder + "/" + $0 }.filter(isCard)
    }
}
```

`Verification/VerifyReport.swift`: `jsonData` (vlastní `Encodable` pohled s klíči výše, `unreadable` jako `[{path, error}]`,
`sortedKeys`, `.iso8601`, `tool` „offload“, `version` `OffloadVersion.string`), `html` ve stylu `HTMLReport` (stejné CSS,
lišta „Uložit jako PDF“ přidá úkol 55; nadpis `Ověření <karta>`, odznak, souhrn, varování, dlaždice, sekce `Nesedí (obsah se
liší od záznamu)`, `Chybí`, `Nečitelné`, `Selhaly už při kopii (bez platného záznamu)`, `Nejnovější záznam selhal při kopii
(ověřeno proti starší generaci)`, `Navíc (nejsou v MHL)` / `Navíc (nejsou v seznamu součtů)`, patička `offload <verze> · report
ověření · <čas>`), `write` (umístění podle pravidla výše; HTML a JSON přes `IO.writeFileNew`, PDF přes `PDFReport.writeHTML`;
zrušený výsledek → `OffloadError.failed("cancelled")`), `summary`, `cliSummary`, `warnings`, texty doslova podle pravidel výše.
Čas `<stamp>` = `started` v UTC `yyyy-MM-dd_HHmmss`.

`main.swift` — `Verify` (nahradit): argument složky (karta nebo den), `--report`; pro každou kartu výsledek, strojové řádky,
`VerifyReport.cliSummary`, při `--report` cesta (`Report: <html>` nebo věta o uložení mimo kartu); kód 1, když kterákoli karta
není `success` nebo je `unfinished`. Průběh `<i>/<n>  <bajty>  <soubor>` na jednom řádku (`\r`).

- [ ] **Step 3: Testy projdou** — Run: `swift test --filter "BackupVerifierTests|VerifyReportTests"`, `swift test`.

- [ ] **Step 4: Ruční kontrola** — `offload verify <karta zkopírovaná Windows 0.6.0> --report` (záloha s doplněním
  a s `xxh128`): `Ověřeno: …`; totéž na kartu ze Silverstacku/ShotPutu, je-li po ruce (report mimo složku karty).
  `ascmhl verify <karta>` po zápisu reportu ověření projde.

- [ ] **Step 5: Záznam a commit** — `docs/VYVOJ-MAC.md`: `- Úkol 45 — Ověřit zálohu: všechny generace ASC MHL a hashe xxh64,
  md5, sha1, xxh3, xxh128, c4, záloha bez MHL proti .xxh64/.md5, nedokončená záloha nikdy „v pořádku“, report ověření do
  _REPORT karty (u cizího MHL vedle karty), nic do ascmhl/; CLI verify česky se strojovými řádky`; `docs/ROZDILY.md`: řádek
  „Ověřit zálohu“ Mac ✅ (GUI úkol 49); záměrný rozdíl „Ověření zálohy“ přepsat na obě verze.
  Run: `git add -A mac docs && git commit -m "Mac: verify a backup against every ASC MHL generation, verify report"`

---

### Task 46: Plánovač doplnění — co se smí doplnit a kam

**Files:**
- Create: `mac/Sources/OffloadKit/Append/AppendPlanner.swift`
- Create: `mac/Tests/OffloadKitTests/AppendPlannerTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `CardRecord` (úkol 38), `MHLReader` (úkol 40), `FileTimes` (úkol 36), `Msg` (úkol 34), `Scanner.scan`, `DestWriter.partName`.
- Produces:
  - `public struct AppendPlan { source; cardPaths; destRoots; date; cardName; generation: Int; fullScan: SourceScan; newFiles: [SourceFile]; newDirs: [String]; unverifiedEarlier: Int; hashAlgorithms: [String]; project: String?; resuming: Bool (úkol 69); later: Int }`
  - `struct Backup { paths; roots; date; cardName; records: [CardRecord]; nextGeneration: Int }`
  - `public enum AppendPlanner { static func plan(_ source: String, cardPaths: [String], sourceFilesystem: String? = nil, destFilesystem: ((String) -> String)? = nil, isCancelled: () -> Bool = { false }) throws -> AppendPlan; static func checkBackup(_ cardPaths: [String], unfinishedGeneration: Int? = nil) throws -> Backup; static func disk(_ cardPath: String) -> String; static func conflict(_ card: SourceFile, _ disk: String, size: Int64, modified: Date) -> String }`

Pravidla (FORMATY „Doplnění“; Windows návrh §2.2, R4, R7, rozhodnutí I1, I3, I5; spec D.22.2):

- **Pravdou je obsah cílových složek** (reporty všech generací + soubory na disku), ne historie. Plánovač jen čte (kartu
  i cíle) a vše odmítne `OffloadError.preflight(…)` **dřív, než se cokoli zapíše**.
- Krok 0 — jedna záloha: složky `<kořen>/<datum>/<karta>` se stejným datem a jménem všude (jinak `appendDifferentFolders`,
  složka bez dvou rodičů `appendNotACardFolder`).
- Krok 1 — každá dřívější záloha je dostupná (`appendUnavailable`), má čitelný report (`appendNoReport`, `appendBadReport` i pro
  nečitelný report doplnění), ASC MHL s řetězem (`appendNoMhl`) bez problémů (`appendMhlDamaged`), **stejný počet generací na
  všech cílech** (`appendDiverged` „D má 2 generace MHL, E 1“ — Mac: disk = jméno svazku) a v každé složce **nejvyšší číslo
  reportu = poslední generace řetězu** (`appendReportsMismatch` — doplnění, jehož MHL se nezapsal). Nová generace = max + 1.
- Krok 2 — karta proti každé záloze: soubor, který report zná, musí mít na kartě i v reportu i na disku stejnou velikost
  a čas (`FileTimes.close`; na FAT kartě nebo FAT cíli i posun o celé hodiny) — jinak konflikt `<cesta>: jiná velikost nebo datum
  (karta 1,2 GB · 6. 10. 9:14, D 1,1 GB · 6. 10. 9:10)`; soubor v reportu dobrý, ale na disku chybí → `appendMissingFile`; selhaný
  soubor, který na disku **je** → `appendFailedFile` (selhaný, který na disku není, se prostě zkopíruje); soubor na disku, který
  žádný report nezná → konflikt `appendUnknownFile`; soubor dobrý jen na části cílů a na ostatních chybí → `appendDiverged`.
  **Karta bez jediného souboru zálohy** → `appendOtherCard` (I3). Dočasné jméno `.<jméno>.offload-part` nového souboru, které
  je jiným souborem karty nebo zálohy → konflikt `appendPartNameTaken`. Konflikty dohromady → `appendConflict(lines, fullCopyFolder: "<karta>_2")`.
- `unverifiedEarlier` = soubory dřívější zálohy dobré, ale ne `CardRecord.readBack` (varování `Msg.unverifiedEarlier` a karta
  nebude „Lze formátovat“ — úkol 47). `hashAlgorithms` = algoritmy **dřívější zálohy** (report generace 1), ne aktuální volba.
  `project` = projekt dřívější zálohy (`CardRecord.project`), ne projekt v aplikaci.
- Nic nového → plán s prázdným `newFiles` (úloha skončí „Hotovo“ s `Msg.nothingNew`, úkol 47).

- [ ] **Step 1: Padající testy** — `AppendPlannerTests.swift` (zálohy se staví skutečným `OffloadEngine`, karta se pak upraví):

```swift
import XCTest
@testable import OffloadKit

final class AppendPlannerTests: XCTestCase {
    struct Setup { let card: String; let cardPaths: [String] }

    /// A card with two clips copied to two destinations.
    func setup(_ dir: TempDir) throws -> Setup {
        let card = dir.sub("card")
        _ = TestFS.writeRandom(card + "/CLIP/A001C001.mov", size: 3000, seed: 1)
        _ = TestFS.writeRandom(card + "/CLIP/A001C002.mov", size: 3000, seed: 2)
        let roots = [dir.sub("D"), dir.sub("E")]
        for r in roots { try FileManager.default.createDirectory(atPath: r, withIntermediateDirectories: true) }
        var o = OffloadOptions(); o.allowSourceVolume = true; o.allowSameDevice = true
        let e = OffloadEngine(options: o)
        let plan = try e.prepare(source: card, destRoots: roots, cardName: "A001", date: "2026-10-08")
        _ = try e.run(plan)
        return Setup(card: card, cardPaths: plan.cardPaths)
    }

    func code(_ f: () throws -> Void) -> PreflightCode? {
        do { try f(); return nil } catch { return (error as? OffloadError)?.code }
    }

    func testNewFilesOnly() throws {
        let s = try setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let p = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths)
        XCTAssertEqual(p.generation, 2)
        XCTAssertEqual(p.newFiles.map(\.relPath), ["CLIP/A001C003.mov"])
        XCTAssertEqual(p.newDirs, ["CLIP"])
        XCTAssertEqual(p.unverifiedEarlier, 0)
        XCTAssertEqual(p.hashAlgorithms, ["xxh64"])
    }

    func testNothingNew() throws {
        let s = try setup(tempDir())
        XCTAssertTrue(try AppendPlanner.plan(s.card, cardPaths: s.cardPaths).newFiles.isEmpty)
    }

    func testSizeConflict() throws {
        let s = try setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C002.mov", size: 3001, seed: 2)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendConflict)
    }

    // Review focus 3.
    func testDivergedGenerationsRefused() throws {
        let s = try setup(tempDir())
        var r = try OffloadReport.read(s.cardPaths[0] + "/_REPORT/offload_report.json")
        r.generation = 2; r.files = []
        try r.write(cardPath: s.cardPaths[0], includeHashRecords: true)   // generation 2 only on D
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendDiverged)
    }

    func testReportsWithoutChainRefused() throws {
        let s = try setup(tempDir())
        for p in s.cardPaths { try Data("{}".utf8).write(to: URL(fileURLWithPath: p + "/_REPORT/A001_report_0002.html")) }
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendDiverged)
    }

    func testUnknownFileOnDestinationIsConflict() throws {
        let s = try setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        _ = TestFS.writeRandom(s.cardPaths[1] + "/CLIP/A001C003.mov", size: 3000, seed: 9)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendConflict)
    }

    func testOtherCardRefused() throws {
        let s = try setup(tempDir())
        let other = tempDir().sub("other")
        _ = TestFS.writeRandom(other + "/X/B001C001.mov", size: 10, seed: 5)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(other, cardPaths: s.cardPaths) }, .appendOtherCard)
    }

    func testMissingAndFailedFiles() throws {
        let s = try setup(tempDir())
        try FileManager.default.removeItem(atPath: s.cardPaths[0] + "/CLIP/A001C001.mov")
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendMissingFile)
    }

    func testFatHourShiftOnTheCardIsNoConflict() throws {
        let s = try setup(tempDir())
        let p = s.card + "/CLIP/A001C001.mov"
        let m = try FileManager.default.attributesOfItem(atPath: p)[.modificationDate] as! Date
        try FileManager.default.setAttributes([.modificationDate: m.addingTimeInterval(3600)], ofItemAtPath: p)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths, sourceFilesystem: "exfat") }, .appendConflict)
        XCTAssertNil(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths, sourceFilesystem: "msdos") })
    }

    func testUnverifiedEarlierCounted() throws {
        let s = try setup(tempDir())
        for p in s.cardPaths {
            var r = try OffloadReport.read(p + "/_REPORT/offload_report.json")
            r.verified = false
            r.files = r.files.map { var f = $0; f.destinations = f.destinations.map { _ in "written" }; return f }
            try r.jsonData().write(to: URL(fileURLWithPath: p + "/_REPORT/offload_report.json"))
        }
        XCTAssertEqual(try AppendPlanner.plan(s.card, cardPaths: s.cardPaths).unverifiedEarlier, 2)
    }
}
```

- [ ] **Step 2: Implementace** — `Append/AppendPlanner.swift` (port pravidel; kostra a krok 2 celé):

```swift
import Foundation

public struct AppendPlan {
    public let source: String
    public let cardPaths: [String]
    public let destRoots: [String]
    public let date: String
    public let cardName: String
    public let generation: Int
    public let fullScan: SourceScan
    public let newFiles: [SourceFile]
    public let newDirs: [String]
    public var unverifiedEarlier = 0
    public var hashAlgorithms = ["xxh64"]
    public var project: String?
    public var resuming = false
    public var later = 0
}

public enum AppendPlanner {
    struct Backup { let paths: [String]; let roots: [String]; let date: String; let cardName: String; let records: [CardRecord]; let nextGeneration: Int }

    /// "RAID" for "/Volumes/RAID/Zaloha/2026-10-08/A001" (the volume name), "/" for the startup disk.
    public static func disk(_ cardPath: String) -> String {
        (try? URL(fileURLWithPath: cardPath).resourceValues(forKeys: [.volumeNameKey]))?.volumeName ?? "/"
    }

    static func when(_ d: Date) -> String {
        let f = DateFormatter(); f.locale = Locale(identifier: "cs_CZ"); f.dateFormat = "d. M. H:mm"
        return f.string(from: d)
    }

    public static func conflict(_ card: SourceFile, _ disk: String, size: Int64, modified: Date) -> String {
        let (a, b) = formatBytesCs(card.size) == formatBytesCs(size) && card.size != size
            ? ("\(card.size) B", "\(size) B") : (formatBytesCs(card.size), formatBytesCs(size))
        return "\(card.relPath): jiná velikost nebo datum (karta \(a) · \(when(card.modifiedUtc)), \(disk) \(b) · \(when(modified)))"
    }

    static func checkBackup(_ cardPaths: [String], unfinishedGeneration: Int? = nil) throws -> Backup {
        guard !cardPaths.isEmpty else { throw OffloadError.preflight(Msg.noDestination, code: .noDestination) }
        let paths = cardPaths.map { URL(fileURLWithPath: $0).standardizedFileURL.path.trimmingTrailingSlash() }
        // 0. One backup: <root>/<date>/<card>, the same date and name everywhere.
        var roots: [String] = []
        let date = ((paths[0] as NSString).deletingLastPathComponent as NSString).lastPathComponent
        let name = (paths[0] as NSString).lastPathComponent
        for p in paths {
            let parent = (p as NSString).deletingLastPathComponent, root = (parent as NSString).deletingLastPathComponent
            guard parent != p, root != parent, !root.isEmpty else { throw OffloadError.preflight(Msg.appendNotACardFolder(p), code: .appendUnavailable) }
            guard (parent as NSString).lastPathComponent.lowercased() == date.lowercased(), (p as NSString).lastPathComponent.lowercased() == name.lowercased() else {
                throw OffloadError.preflight(Msg.appendDifferentFolders(paths), code: .appendDiverged)
            }
            roots.append(root)
        }
        if unfinishedGeneration == 1 {
            for p in paths where !FileManager.default.fileExists(atPath: p) { throw OffloadError.preflight(Msg.appendUnavailable(p), code: .appendUnavailable) }
            return Backup(paths: paths, roots: roots, date: date, cardName: name, records: paths.map { CardRecord(cardPath: $0) }, nextGeneration: 1)
        }
        // 1. Every earlier backup is there, with its reports and an intact ASC MHL chain of the same length.
        var records: [CardRecord] = [], gens: [Int] = []
        for p in paths {
            guard FileManager.default.fileExists(atPath: p) else { throw OffloadError.preflight(Msg.appendUnavailable(p), code: .appendUnavailable) }
            let rec = CardRecord.load(p, maxGeneration: unfinishedGeneration.map { $0 - 1 })
            if rec.reportMissing { throw OffloadError.preflight(Msg.appendNoReport(p), code: .appendNoReport) }
            if let e = rec.error { throw OffloadError.preflight(Msg.appendBadReport(p, e), code: .appendNoReport) }
            if let first = rec.problems.first { throw OffloadError.preflight(Msg.appendBadReport(p, first), code: .appendNoReport) }
            guard let mhl = MHLReader.read(p), !mhl.chain.isEmpty else { throw OffloadError.preflight(Msg.appendNoMhl(p), code: .appendNoMhl) }
            if !mhl.chainProblems.isEmpty { throw OffloadError.preflight(Msg.appendMhlDamaged(p), code: .appendMhlDamaged) }
            records.append(rec)
            gens.append(min(mhl.chain.map(\.seq).max()!, (unfinishedGeneration ?? Int.max) - 1))
        }
        if Set(gens).count > 1 {
            let details = paths.enumerated().map { i, p in i == 0 ? "\(disk(p)) má \(gens[i]) \(plural(gens[i], "generaci", "generace", "generací")) MHL" : "\(disk(p)) \(gens[i])" }
            throw OffloadError.preflight(Msg.appendDiverged(details.joined(separator: ", ")), code: .appendDiverged)
        }
        for (i, p) in paths.enumerated() where records[i].highestReportNumber != gens[i] {
            throw OffloadError.preflight(Msg.appendReportsMismatch(p, report: records[i].highestReportNumber, mhl: gens[i]), code: .appendDiverged)
        }
        return Backup(paths: paths, roots: roots, date: date, cardName: name, records: records, nextGeneration: gens[0] + 1)
    }

    public static func plan(_ source: String, cardPaths: [String], sourceFilesystem: String? = nil,
                            destFilesystem: ((String) -> String)? = nil, isCancelled: () -> Bool = { false }) throws -> AppendPlan {
        let b = try checkBackup(cardPaths)                       // úkol 69: s nedokončenou generací (ResumePlanner)
        let scan = try Scanner.scan(source)
        let cardLocal = FileTimes.storesLocalTime(sourceFilesystem ?? IO.filesystemType(source))
        let diskLocal = b.paths.map { cardLocal || FileTimes.storesLocalTime(destFilesystem?($0) ?? IO.filesystemType($0)) }
        var newFiles: [SourceFile] = [], conflicts: [String] = []
        var unverified = 0, known = 0
        for f in scan.files {
            if isCancelled() { throw CancellationError() }
            var present = 0, goodInRecord = 0, unverifiedFile = false
            for (d, p) in b.paths.enumerated() {
                let full = p + "/" + f.relPath
                let attrs = try? FileManager.default.attributesOfItem(atPath: full)
                let exists = attrs != nil
                if exists { present += 1 }
                guard let rec = b.records[d].files[f.relPath.lowercased()] else {
                    if exists { conflicts.append(Msg.appendUnknownFile(disk(p), f.relPath)) }
                    continue
                }
                let state = CardRecord.stateOf(rec, cardPath: p, position: d)
                guard state == "verified" || state == "written" else {
                    if exists { throw OffloadError.preflight(Msg.appendFailedFile(f.relPath, disk(p)), code: .appendFailedFile) }
                    continue
                }
                goodInRecord += 1
                if !CardRecord.readBack(rec, cardPath: p, serial: b.records[d].volumeSerial) { unverifiedFile = true }
                if rec.file.size != f.size || !FileTimes.close(rec.file.modified, f.modifiedUtc, localTime: cardLocal) {
                    conflicts.append(conflict(f, disk(p), size: rec.file.size, modified: rec.file.modified))
                } else if !exists {
                    throw OffloadError.preflight(Msg.appendMissingFile(disk(p), f.relPath), code: .appendMissingFile)
                } else if let size = (attrs?[.size] as? NSNumber)?.int64Value, let m = attrs?[.modificationDate] as? Date,
                          size != f.size || !FileTimes.close(m, f.modifiedUtc, localTime: diskLocal[d]) {
                    conflicts.append(conflict(f, disk(p), size: size, modified: m))
                }
            }
            if unverifiedFile { unverified += 1 }
            if goodInRecord > 0 { known += 1 }
            if goodInRecord == 0 && present == 0 {
                newFiles.append(f)
            } else if goodInRecord > 0 && goodInRecord < b.paths.count && present < b.paths.count {
                let on = b.paths.filter { FileManager.default.fileExists(atPath: $0 + "/" + f.relPath) }.map(disk).joined(separator: ", ")
                throw OffloadError.preflight(Msg.appendDiverged("\(f.relPath) je jen na \(on)"), code: .appendDiverged)
            }
        }
        if !scan.files.isEmpty && known == 0 { throw OffloadError.preflight(Msg.appendOtherCard, code: .appendOtherCard) }
        let taken = Set(scan.files.map { $0.relPath.lowercased() } + b.records.flatMap { $0.files.keys })
        for f in newFiles where taken.contains(DestWriter.partName(f.relPath).lowercased()) {
            conflicts.append(Msg.appendPartNameTaken(f.relPath, DestWriter.partName(f.relPath)))
        }
        if !conflicts.isEmpty {
            var seen = Set<String>()
            throw OffloadError.preflight(Msg.appendConflict(conflicts.filter { seen.insert($0).inserted }, fullCopyFolder: b.cardName + "_2"), code: .appendConflict)
        }
        let dirs = Set(newFiles.flatMap { f -> [String] in
            let parts = f.relPath.split(separator: "/").dropLast()
            return parts.indices.map { parts[parts.startIndex...$0].joined(separator: "/") }
        }).sorted()
        var plan = AppendPlan(source: URL(fileURLWithPath: source).standardizedFileURL.path, cardPaths: b.paths, destRoots: b.roots, date: b.date,
                              cardName: b.cardName, generation: b.nextGeneration, fullScan: scan, newFiles: newFiles, newDirs: dirs)
        plan.unverifiedEarlier = unverified
        plan.hashAlgorithms = b.records[0].generations.first?.report.algorithms ?? ["xxh64"]
        plan.project = b.records[0].project
        return plan
    }
}
```

- [ ] **Step 3: Testy projdou; záznam a commit** — Run: `swift test --filter AppendPlannerTests`, `swift test`.
  `docs/VYVOJ-MAC.md`: `- Úkol 46 — plánovač doplnění: jedna záloha, stejné generace všude, reporty = řetěz, karta proti
  každé záloze (velikost, čas, FAT tolerance), cizí soubor na cíli, jiná karta, dočasné jméno obsazené, neověřené dřívější
  soubory, součty a projekt dřívější zálohy; jen čte`.
  Run: `git add -A mac docs && git commit -m "Mac: append planner — what may be appended and where"`

---

### Task 47: Engine v režimu doplnění a `offload append`

**Files:**
- Modify: `mac/Sources/OffloadKit/Engine.swift` (`OffloadPlan.mode`, `generation`, `hashKinds`; `DestWriter.finish` bez nahrazení; `run`)
- Modify: `mac/Sources/OffloadKit/Job.swift` (`JobConfig.appendInto: [String]?`; `prepare` přes `AppendPlanner`; zámek složek; historie; „Nic nového“; fáze 2 jen nové klipy)
- Modify: `mac/Sources/OffloadKit/ProxyCopy.swift`, `Proxy.swift` (obsazená jména v `_PROXY` cíle, nikdy nepřepsat)
- Modify: `mac/Sources/offload/main.swift` (subcommand `Append`)
- Create: `mac/Tests/OffloadKitTests/AppendEngineTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `AppendPlanner.plan` (úkol 46), `FolderLocks` (úkol 37), `OffloadReport.write` (úkol 41), `HistoryRecorder` (úkol 44), `HistoryStore.findForCard`, `CardInspector` (úkoly 42–43), `BackupState` (úkol 38), `ProxyCopy.renameNoReplace` (úkol 20).
- Produces:
  - `public enum PlanMode { case full, append, repair }`; `OffloadPlan.mode = .full`, `.generation = 1`, `.hashKinds: HashKinds = .xxh64`, `.notInBackup = 0`, `.unverifiedEarlier = 0`
  - `static func OffloadPlan.from(_ a: AppendPlan) -> OffloadPlan` (scan = jen nové soubory a jejich složky; `cardPaths` = složky zálohy)
  - `JobConfig.appendInto: [String]?` (složky zálohy; `nil` = celá kopie)
  - `offload append <zdroj> [--into <složka karty>]… [--no-verify] [--no-history] [--proxy …]` (volby fáze 2 jako `copy`)

Pravidla (FORMATY „Doplnění“; spec D.22.2):

- Úloha doplnění: `FolderLocks.acquire(cardPaths)` **před plánováním**, drží ho celou fázi 1 (obsazená složka = `appendBusy` hned,
  nic se nezapíše); pak `AppendPlanner.plan`; nic nového → `PreflightCode.nothingNew` → úloha **Hotovo** s poznámkou
  `Msg.nothingNew` (a `OffloadReport.healHashList` na každé složce — doplní řádky, které dřívějšímu doplnění chyběly).
- Kopie: jen nové soubory do **stejných** složek karty (žádné `uniqueCardFolder`, žádné `_2`); cílové složky nových souborů
  se vytvoří; **soubor, který se mezitím na cíli objeví, se nepřepíše** — `DestWriter.finish` přejmenovává `.offload-part`
  bez nahrazení (`ProxyCopy.renameNoReplace`; obsazeno → `failed` s chybou `file appeared meanwhile: <cesta>`, `.part` se smaže).
  Celá kopie dál `rename` (nová složka — nic nepřepíše, `uniqueCardFolder`).
- Součty podle dřívější zálohy (`plan.hashKinds = HashKinds.from(names: appendPlan.hashAlgorithms)`).
- Report generace N: `generation = N`, `project` = projekt dřívější zálohy; zápis podle úkolu 41 (reporty nové → MHL → řetěz →
  seznamy). Dřívější HTML/JSON/PDF a generace MHL **bajtově beze změny**.
- „Lze formátovat“ po doplnění = generace ověřená **a** `unverifiedEarlier == 0` (jinak varování `Msg.unverifiedEarlier(n)`).
- Fáze 2: klipy jen z nových souborů; obsazená jména proxy = všechny `.mov` pod `_PROXY` (i v podsložkách) na **všech** cílech;
  render ani kopie proxy nikdy nepřepíše (`renameNoReplace`). (Uspořádání `_PROXY` jako na kartě × ploché dořeší úkol 54.)
- Historie: existující záznam karty (podle `fsSerial`+kapacity, u složky podle `shape`) dostane generaci `kind: "append"`,
  sjednocený seznam souborů, otisk karty teď, `finished` teď; projekt se nemění; bez záznamu se založí nový.
- Zrušení uprostřed: rozpracované `.offload-part` se smažou, hotové soubory zůstanou a report + MHL generace je zaznamenají
  (`failed` u nedokončených) — jako zrušená celá kopie; historie se nezapíše.
- CLI `append`: bez `--into` najde složky v historii (`CardInspector` + `HistoryStore.findForCard`); nenajde →
  `Msg.cardNotInHistory`, kód 1. Kód 0 = doplněno a lze formátovat, nebo nic nového; 1 = chyba nebo nelze formátovat.

- [ ] **Step 1: Padající testy** — `AppendEngineTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class AppendEngineTests: XCTestCase {
    func appendJob(_ s: AppendPlannerTests.Setup, verify: Bool = true) -> OffloadJob {
        var cfg = JobConfig(source: s.card, destRoots: []); cfg.appendInto = s.cardPaths; cfg.analyzeClips = false; cfg.verify = verify
        cfg.historyFolder = tempDir().path
        var o = OffloadJob.engineOptions(cfg); o.allowSourceVolume = true; o.allowSameDevice = true
        return OffloadJob(config: cfg, options: o)
    }

    // Review focus 1.
    func testOriginalReportsAndGeneration1ByteIdentical() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        let before = try s.cardPaths.flatMap { p in try ["/_REPORT/offload_report.json", "/_REPORT/A001_report.html"].map { try Data(contentsOf: URL(fileURLWithPath: p + $0)) } }
        let gen1 = try s.cardPaths.map { p in try TestFS.files(in: p + "/ascmhl").filter { $0.hasPrefix("0001_") }.map { try Data(contentsOf: URL(fileURLWithPath: p + "/ascmhl/" + $0)) } }
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let job = appendJob(s)
        job.run()
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertTrue(job.status().safeToFormat)
        let after = try s.cardPaths.flatMap { p in try ["/_REPORT/offload_report.json", "/_REPORT/A001_report.html"].map { try Data(contentsOf: URL(fileURLWithPath: p + $0)) } }
        XCTAssertEqual(after, before)
        XCTAssertEqual(try s.cardPaths.map { p in try TestFS.files(in: p + "/ascmhl").filter { $0.hasPrefix("0001_") }.map { try Data(contentsOf: URL(fileURLWithPath: p + "/ascmhl/" + $0)) } }, gen1)
        for p in s.cardPaths {
            XCTAssertTrue(FileManager.default.fileExists(atPath: p + "/_REPORT/offload_report_0002.json"))
            XCTAssertEqual(MHLReader.read(p)?.chain.map(\.seq), [1, 2])
            let list = try String(contentsOfFile: p + "/_REPORT/A001.xxh64", encoding: .utf8)
            XCTAssertEqual(list.split(separator: "\n").count, 3)
            XCTAssertTrue(list.hasSuffix("  CLIP/A001C003.mov\n"))
        }
        let r = try BackupVerifier.verify(s.cardPaths[1], progress: nil, isCancelled: { false })
        XCTAssertEqual(r.ok, 3); XCTAssertEqual(r.generations, 2)
    }

    func testFileThatAppearsMeanwhileIsNotOverwritten() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let job = appendJob(s)
        let intruder = s.cardPaths[0] + "/CLIP/A001C003.mov"
        job.engine.testBeforeRename = { path in if path == intruder { try? Data("other".utf8).write(to: URL(fileURLWithPath: intruder)) } }
        job.run()
        XCTAssertEqual(try String(contentsOfFile: intruder, encoding: .utf8), "other")
        let r = try OffloadReport.read(s.cardPaths[0] + "/_REPORT/offload_report_0002.json")
        XCTAssertEqual(r.files[0].destinations, ["failed", "verified"])
        XCTAssertFalse(job.status().safeToFormat)
    }

    func testNothingNewIsDoneWithNote() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        let job = appendJob(s)
        job.run()
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertEqual(job.status().note, Msg.nothingNew)
        XCTAssertFalse(FileManager.default.fileExists(atPath: s.cardPaths[0] + "/_REPORT/offload_report_0002.json"))
    }

    func testBusyFolderRefusedWithoutWriting() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let held = try FolderLocks.acquire([s.cardPaths[1]])
        let job = appendJob(s)
        job.run()
        XCTAssertEqual(job.status().phase, .failed)
        XCTAssertEqual(job.status().errorCode, .appendBusy)
        XCTAssertFalse(FileManager.default.fileExists(atPath: s.cardPaths[0] + "/CLIP/A001C003.mov"))
        held.release()
    }

    func testUnverifiedEarlierKeepsCardFromFormat() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        for p in s.cardPaths {
            var r = try OffloadReport.read(p + "/_REPORT/offload_report.json")
            r.verified = false
            r.files = r.files.map { var f = $0; f.destinations = f.destinations.map { _ in "written" }; return f }
            try r.jsonData().write(to: URL(fileURLWithPath: p + "/_REPORT/offload_report.json"))
        }
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let job = appendJob(s)
        job.run()
        XCTAssertFalse(job.status().safeToFormat)
        XCTAssertEqual(job.status().warning, Msg.unverifiedEarlier(2))
    }
}
```

(`OffloadEngine.testBeforeRename: ((String) -> Void)?` — interní háček jen pro testy, volaný v `DestWriter.finish` těsně před
přejmenováním s cílovou cestou; `JobStatus.note: String?` a `JobStatus.errorCode: PreflightCode?` přibudou v tomto úkolu.)

- [ ] **Step 2: Implementace**

`Engine.swift`:

```swift
public enum PlanMode: Equatable { case full, append, repair }

public struct OffloadPlan {
    public let source: String
    public let scan: SourceScan
    public let date: String
    public let cardName: String
    public let destRoots: [String]
    public var mode: PlanMode = .full
    public var generation = 1
    public var hashKinds: HashKinds = .xxh64
    /// Card files no report of the backup records (a repair, a resume of a committed generation): not safe to format.
    public var notInBackup = 0
    public var unverifiedEarlier = 0
    public var project: String?
    /// Append/repair: the backup's card folders (the same on every destination); a full copy derives them.
    public var fixedCardPaths: [String]?
    public var cardPaths: [String] { fixedCardPaths ?? destRoots.map { "\($0)/\(date)/\(cardName)" } }

    public static func from(_ a: AppendPlan) -> OffloadPlan {
        var p = OffloadPlan(source: a.source, scan: SourceScan(root: a.source, files: a.newFiles, dirs: a.newDirs, skipped: a.fullScan.skipped),
                            date: a.date, cardName: a.cardName, destRoots: a.destRoots)
        p.mode = .append; p.generation = a.generation; p.hashKinds = HashKinds.from(names: a.hashAlgorithms)
        p.unverifiedEarlier = a.unverifiedEarlier; p.project = a.project; p.fixedCardPaths = a.cardPaths
        return p
    }
}
```

`DestWriter` dostane `let noReplace: Bool` (= `plan.mode != .full`) a ve `finish` místo `rename`:

```swift
                self.engine?.testBeforeRename?(finalPath)
                if self.noReplace {
                    do { try ProxyCopy.renameNoReplace(self.partPath, finalPath) }
                    catch is ProxyCopy.Taken { throw IOError(message: "file appeared meanwhile: \(file.relPath)") }
                } else {
                    guard rename(self.partPath, finalPath) == 0 else { throw IOError("rename", finalPath) }
                }
```

(Výjimka v `catch` bloku vede na existující `closeAndRemovePart()` + `failed` — `.part` se smaže, cizí soubor zůstane.)
`run` vytváří složky jen pro `plan.scan.dirs` (u doplnění nové složky), report staví s `generation`, `project`,
`hashAlgorithms = plan.hashKinds == .xxh64 ? nil : plan.hashKinds.names`.

`Job.swift` (`OffloadJob.run`, větev doplnění — kostra):

```swift
        if let into = config.appendInto {
            let held: FolderLocks
            do { held = try FolderLocks.acquire(into) } catch { return fail(error) }        // appendBusy at once, nothing written
            defer { held.release() }
            let ap: AppendPlan
            do { ap = try AppendPlanner.plan(config.source, cardPaths: into, isCancelled: { self.isCancelled }) } catch { return fail(error) }
            if ap.newFiles.isEmpty {
                for p in ap.cardPaths { _ = try? OffloadReport.healHashList(cardPath: p, cardName: ap.cardName) }
                update { $0.phase = .done; $0.note = Msg.nothingNew; $0.safeToEject = true
                         $0.safeToFormat = BackupState.of(ap.cardPaths) == BackupState(failed: 0, unverified: 0) }
                return nil
            }
            let plan = OffloadPlan.from(ap)
            // … stage 1 as a full copy (engine.run(plan)), then:
            let whole = BackupState.of(plan.cardPaths)
            update {
                $0.safeToEject = report.success && whole.failed == 0
                $0.safeToFormat = report.safeToFormat && whole.failed == 0 && whole.unverified == 0 && plan.unverifiedEarlier == 0
                if plan.unverifiedEarlier > 0 { $0.addWarning(Msg.unverifiedEarlier(plan.unverifiedEarlier)) }
            }
            // history: HistoryRecorder.finish(…, kind: "append") — updates the entry found by CardInspector, union of file lists
        }
```

`fail(_:)` nastaví `phase = .failed`, `error = "\(e)"`, `errorCode = (e as? OffloadError)?.code`. Fáze 2: `usedNames` předvyplnit
všemi `.mov` pod `_PROXY` (rekurzivně) na všech cílech; render do `.part` a přesun do `_PROXY` přes `renameNoReplace`.

`main.swift` — `struct Append: ParsableCommand` (volby fáze 2 sdílené s `Copy` přes `@OptionGroup`), výpis jako `copy`
(řádek historie, „→ <složka>“ pro každou složku zálohy, „Doplnění: N nových souborů (velikost), generace G“).

- [ ] **Step 3: Testy projdou** — Run: `swift test --filter AppendEngineTests`, `swift test`.

- [ ] **Step 4: Ruční kontrola** — karta → `offload copy` na 2 disky, na kartu přidat soubor (nebo natočit klip), `offload append
  <karta>` → „Doplnění: 1 nový soubor …, generace 2“; `ascmhl verify <složka karty>` (pip `ascmhl`) projde; `offload verify` → 2
  generace; Windows `bin\offload.exe verify` téže složky (je-li po ruce) → v pořádku; `ls _REPORT` — `offload_report_0002.*`.

- [ ] **Step 5: Záznam a commit** — `docs/FORMATY.md`: „Doplnění (Windows od 0.2.0, Mac naplánováno)“ → „(Windows od 0.2.0,
  Mac od 0.3.0)“; v pravidlech zámku doplnit „Mac: `flock` na místním souboru (úkol 37) — mezi Macem a Windows se zámek neuplatní“.
  `docs/ROZDILY.md`: řádek „Doplnit jen nové klipy“ Mac ✅ (GUI úkol 49); záměrný rozdíl „Doplnění“ upravit.
  `docs/VYVOJ-MAC.md`: `- Úkol 47 — doplnění: zámek složek před plánem, jen nové soubory do stejných složek bez přepsání
  (renamex_np RENAME_EXCL), generace N, nic nového = Hotovo + doplnění chybějících řádků .xxh64, neověřené dřívější soubory
  = nelze formátovat, historie generace append; CLI offload append`.
  Run: `git add -A mac docs && git commit -m "Mac: append mode and offload append"`

---

### Task 48: Kontrola místa — karta, proxy, report, fronta, po svazcích

**Files:**
- Create: `mac/Sources/OffloadKit/Space/SpaceEstimator.swift`
- Modify: `mac/Sources/OffloadKit/Engine.swift` (`prepare`: parametr `extra`), `Job.swift` (požadavek z `config.proxy`; přepočet před proxy)
- Modify: `mac/Sources/offload/main.swift` (`--dry-run` rozpis, varování < 5 %)
- Create: `mac/Tests/OffloadKitTests/SpaceTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `Clip`, `ClipInfo`, `ProxySettings`, `ExternalTools.resolved()`, `Msg.noSpace/lowSpace/spaceLine/proxiesSkippedForSpace` (úkol 34), `VolumeFacts.folderKey` (úkol 36).
- Produces:
  - `public struct SpaceNeed: Equatable { card, proxy, report: Int64; total; static func + }`
  - `public struct SpaceExtra { proxy: Int64; report: Int64; proxyOnAllDestinations: Bool; proxyFolder: String?; func forDestination(_ i: Int, card: Int64) -> SpaceNeed }`
  - `public enum SpaceEstimator { static func cardBytes(_ total: Int64, files: Int) -> Int64; reportBytes(clips:); sourceMbps(_:); proxyMbps(_:audioStreams:height:); proxyVideoMbps(_:height:); renderable(_:tools:); proxyBytes(_:settings:renderable:info:lut:); extra(clips:settings:tools:) -> SpaceExtra }`
  - `public enum SpaceSeverity { warning, blocking }`, `public struct SpaceProblem { severity; label; need; free; text }`, `public struct VolumeDemand { volume; label; need }`
  - `public enum SpacePlanner { static func check(_ demands: [VolumeDemand], free: [String: Int64]) -> [SpaceProblem]; static func volumeKey(_ path: String) -> String }`
  - `OffloadEngine.prepare(…, extra: ((Int, String) -> SpaceNeed)? = nil)`

Pravidla (Windows návrh §2.5, R10, R11 — chování, ne formát):

- Na jednom svazku cíle: karta = `totalBytes + soubory × 64 KiB + 64 MiB`; report = `32 MiB + klipy × 1 MiB`; proxy (jen se
  zapnutým proxy; cíl 1 vždy, ostatní jen s „Kopírovat proxy na všechny cíle“; složka pro proxy navíc je svazek s požadavkem =
  proxy) = Σ délka × tok. Délka před kopií `bytes × 8 / předpokládaný tok zdroje` (MP4/MOV/MTS 50, MXF 100, BRAW 150, R3D 200,
  Canon RAW 500, ARRIRAW 1 000 Mb/s), po analýze skutečná délka. Tok proxy: H.264/HEVC `bitrateMbps × 1,1 + AAC zvukových proudů`
  (64 kb/s na kanál, proud 128–512 kb/s); ProRes Apple 1080p30 (Proxy 45, LT 102, 422 147, HQ 220 Mb/s) × (výška/1080)² +
  PCM 0,768 Mb/s na kanál. Před analýzou jeden stereo proud. Proxy kamery (když se přeskakují) a klipy bez dekodéru 0; klip
  s lehčím originálem 0 (úkol 54).
- Součet **po svazcích** (dva cíle na jednom svazku se sečtou): vybrané karty + úlohy ve frontě, které nezačaly + zbytek
  běžících; `need > free` → blokuje (`Msg.noSpace`, rozpis „karty X, proxy ~Y, report Z“); `(free − need) × 20 < need` →
  varování (`Msg.lowSpace`). Neznámé volno nikdy neblokuje v GUI; engine při startu úlohy je poslední slovo (neznámé volno = 0).
- Před renderem proxy se požadavek přepočítá ze skutečných délek; nevejde-li se → proxy se nerenderují, varování
  `Msg.proxiesSkippedForSpace` (karta zůstává ověřená, úloha „vyžaduje pozornost“).
- CLI: stejné hlášky; varování jako řádek `Varování: …` a pokračuje se; `--dry-run` vypíše `Msg.spaceLine` pro každý svazek.
- Svazek = `VolumeFacts.folderKey(kořen svazku)` (UUID svazku), neznámý → plná cesta.

- [ ] **Step 1: Padající testy** — `SpaceTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class SpaceTests: XCTestCase {
    func clip(_ ext: String, bytes: Int64, format: ClipFormat = .video, proxy: Bool = false) -> Clip {
        Clip(name: "A001C001", path: "CLIP/A001C001.\(ext)", format: format, segments: [], bytes: bytes, isCameraProxy: proxy)
    }

    func testFormulas() {
        XCTAssertEqual(SpaceEstimator.cardBytes(1000, files: 2), 1000 + 2 * 65536 + 64 << 20)
        XCTAssertEqual(SpaceEstimator.reportBytes(clips: 3), (32 << 20) + 3 * (1 << 20))
        var p = ProxySettings(); p.enabled = true; p.codec = .h264; p.bitrateMbps = 10; p.height = 1080
        XCTAssertEqual(SpaceEstimator.proxyMbps(p, audioStreams: [2], height: 1080), 10 * 1.1 + 0.128, accuracy: 1e-9)
        p.codec = .proresProxy
        XCTAssertEqual(SpaceEstimator.proxyMbps(p, audioStreams: [2], height: 2160), 45 * 4 + 0.768 * 2, accuracy: 1e-9)
        XCTAssertEqual(SpaceEstimator.sourceMbps(clip("mxf", bytes: 1)), 100)
        XCTAssertEqual(SpaceEstimator.sourceMbps(clip("braw", bytes: 1, format: .braw)), 150)
    }

    func testProxyBytesSkipsCameraProxiesAndUndecodable() {
        var p = ProxySettings(); p.enabled = true; p.codec = .h264; p.bitrateMbps = 10; p.skipCameraProxies = true
        let one = SpaceEstimator.proxyBytes([clip("mov", bytes: 50_000_000 / 8 * 60)], settings: p, renderable: { _ in true })
        XCTAssertEqual(Double(one), 60 * (11 + 0.128) * 1e6 / 8, accuracy: 2)
        XCTAssertEqual(SpaceEstimator.proxyBytes([clip("mov", bytes: 1 << 30, proxy: true)], settings: p, renderable: { _ in true }), 0)
        XCTAssertEqual(SpaceEstimator.proxyBytes([clip("r3d", bytes: 1 << 30, format: .r3d)], settings: p, renderable: { _ in false }), 0)
        p.enabled = false
        XCTAssertEqual(SpaceEstimator.proxyBytes([clip("mov", bytes: 1 << 30)], settings: p, renderable: { _ in true }), 0)
    }

    func testPlannerSumsPerVolumeBlocksAndWarns() {
        let a = VolumeDemand(volume: "V1", label: "Zaloha (RAID)", need: SpaceNeed(card: 60, proxy: 0, report: 0))
        let b = VolumeDemand(volume: "V1", label: "Zaloha2 (RAID)", need: SpaceNeed(card: 50, proxy: 0, report: 0))
        XCTAssertEqual(SpacePlanner.check([a, b], free: ["V1": 100]).first?.severity, .blocking)
        XCTAssertEqual(SpacePlanner.check([a], free: ["V1": 62]).first?.severity, .warning, "2 left of 60 is under 5 %")
        XCTAssertTrue(SpacePlanner.check([a], free: ["V1": 64]).isEmpty)
        XCTAssertTrue(SpacePlanner.check([a], free: [:]).isEmpty, "unknown free space never blocks")
    }

    func testBlockingText() {
        let need = SpaceNeed(card: 380_000_000_000, proxy: 30_000_000_000, report: 2_000_000_000)
        let p = SpacePlanner.check([VolumeDemand(volume: "V", label: "Zaloha (RAID)", need: need)], free: ["V": 398_000_000_000])
        XCTAssertEqual(p.first?.text, "Na cíli Zaloha (RAID) není dost místa: potřeba 412 GB (karty 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB.")
    }
}
```

- [ ] **Step 2: Implementace** — `Space/SpaceEstimator.swift` podle pravidel (vzorce 1:1 s výše; `SpacePlanner.check` seskupí
  `demands` podle `volume`, `label` = jména oddělená čárkou, pořadí výsledků: blokující první); `Msg.noSpace(label, total:, parts:, free:)`
  s `parts` = „karty X“, „proxy ~Y“, „report Z“ (jen nenulové, odděleno čárkou). `OffloadEngine.prepare` nahradí dnešní vzorec
  místa: pro každý cíl `SpaceNeed(card: cardBytes, …) + extra?(index, root)`, `SpacePlanner.check` nad svazky (volno
  `IO.availableBytes`, neznámé = 0), blokující → `OffloadError.preflight(text, code: .noSpace)`, varování do
  `OffloadEngine.warnings` (CLI je vypíše, úloha je dá do `JobStatus.warning`). `OffloadJob.prepare` předá `extra` z
  `SpaceEstimator.extra(clips: ClipDetector.detect(scan.files), settings: config.proxy, tools: config.tools.resolved())`.
  Fáze 2 (`analyzeAndRender`) po analýze: `proxyBytes(…, info:)` pro cíle, které proxy dostanou, proti volnu; nevejde-li se →
  žádný render, `addWarning(Msg.proxiesSkippedForSpace(…))`, klipy `proxyStatus = .skipped` s touto poznámkou.

- [ ] **Step 3: Testy projdou; záznam a commit** — Run: `swift test`. `docs/ROZDILY.md`: řádek „Kontrola místa“ Mac ✅ (GUI
  úkol 49), záměrný rozdíl „Kontrola místa“ na obě verze. `docs/VYVOJ-MAC.md`: `- Úkol 48 — kontrola místa po svazcích (karta,
  proxy z odhadu, report, fronta, složka pro proxy), rezerva 5 %, přepočet před proxy, CLI --dry-run`.
  Run: `git add -A mac docs && git commit -m "Mac: space check per volume with proxies, report and queue"`

---

### Task 49: GUI plánu 6 — řádek historie, „Karty už jsou zálohované“, Doplnit, název, místo, Ověřit zálohu

Úkol staví na **stávajícím** vzhledu (první plán, úkoly 11, 17, 24). Nový vzhled podle `docs/ui-navrh/` je v souboru `-3`
(úkoly 63–64) a převezme tyto prvky.

**Files:**
- Modify: `mac/Sources/OffloadApp/AppModel.swift` (`inspect`, `insights`, `backedUpPrompt`, `appendSelected`, `space`, `verifyBackup`)
- Modify: `mac/Sources/OffloadApp/Views.swift` (`SourcesSection`: řádek historie, zaškrtávátko Doplnit, zástupný text názvu; `StartBar`: místo; `QueuePanel`: položka ověření)
- Modify: `mac/Sources/OffloadApp/OffloadApp.swift` (menu Nástroje → „Ověřit zálohu…“ ⇧⌘O)
- Create: `mac/Sources/OffloadApp/BackedUpPrompt.swift`, `mac/Sources/OffloadKit/Desktop/VerifyJob.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `CardInspector`, `HistoryText`, `HistoryStore`, `ReelSuggestion` (úkoly 42–43), `AppendPlanner` (úkol 46), `JobConfig.appendInto` (úkol 47), `SpaceEstimator`, `SpacePlanner` (úkol 48), `BackupVerifier`, `VerifyReport` (úkol 45), `SettingsStore` (`naming.suggestFromClips`, úkol 35).
- Produces:
  - `AppModel.insights: [String: CardInsight]`, `inspect(_ source: String)` (na pozadí; výměna karty = zrušit a zahodit), `historyLine(_ source:) -> (String, HistoryTone)?`
  - `AppModel.appendSelected: Set<String>`, `canAppend(_:) -> Bool`, `spaceProblems: [SpaceProblem]`, `verifyBackup()`
  - `public final class VerifyJob` (položka kopírovací fronty: `run(isCancelled:)`, `result`, `reportPaths`, texty karty)
  - SwiftUI `BackedUpPrompt` (dialog), `VerifyCard` (karta ověření ve frontě)

Pravidla a texty (Windows návrh §2.1–2.5, doslova):

- Řádek pod kartou: `HistoryText.line` s barvou podle tónu (zelená / oranžová / červená / šedá „Zjišťuji, jestli už je karta
  zálohovaná…“); karta bez historie nic. Inspect běží pro každý zdroj na pozadí (`DispatchQueue.global`), jeden na cestu, žádost
  během běhu → po doběhnutí ještě jednou; výsledek se zahodí, když se mezitím změnila identita karty (`CardIdentity`) nebo
  zdroj zmizel. `history.json` se načte při startu mimo hlavní vlákno; dokud není načtený, řádek „Zjišťuji…“.
- `[ ] Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)` u vybrané karty s `newFiles`; u nedokončené zálohy
  (`unfinished`) text `Dokončit přerušenou zálohu` (úkol 69 dodá logiku). Zaškrtnuté: pole „Název složky“ zablokované a místo
  něj `Složka: 2026-10-06/A001 na 2 cílech`; doplnění zapisuje do cílů z historie, ne do sekce Cíle (ta může být prázdná —
  `canStart` to pro výběr složený jen z doplnění dovolí). Karta úlohy „A001 · doplnění“.
- **Dialog před celou kopií** (karta má stav „zálohováno“ a nemá zaškrtnuté doplnění): `NSAlert` / SwiftUI `.alert` se třemi
  tlačítky — titulek `Karty už jsou zálohované`, text `• A001 — 6. 10. 2026 8:15, Zaloha (RAID), Zaloha2 (SSD), ověřeno` (· `nové
  soubory: 12`) a `Zkopírovat je znovu celé? Vznikne nová složka (A001_2).`; tlačítka `Kopírovat znovu celé`, `Vynechat zálohované`,
  `Zrušit`. „Vynechat zálohované“ spustí jen ostatní karty.
- Název: zástupný text pole „Název složky“ = návrh (z klipů, jinak „další v řadě“ — `ReelSuggestion.next` s historií a jmény
  napsanými u ostatních karet), pod polem šedá nápověda (`podle názvů klipů (A001C001…)` / `další v řadě po A001 (dnes)`),
  „Na kartě je víc cívek: …“; napsaný název má vždy přednost; volba v nastavení `Navrhovat název karty podle klipů`.
- Místo: pod tlačítkem Start červeně (blokuje) / oranžově texty `SpaceProblem.text`; přepočet z paměti při každé změně výběru
  (karty z `insights`, volno z `destStatus` úkolu 10, fronta z `JobItem`); Start počká na inspect vybraných karet nejvýš 10 s
  (mimo hlavní vlákno, tlačítko neaktivní), pak rozhodne podle toho, co ví (karta ze svazku: obsazené místo svazku jako horní odhad).
- **Ověřit zálohu…** (menu Nástroje, ⇧⌘O): `NSOpenPanel` „Vyberte složku karty na cíli (nebo celý den)“ + zaškrtávátko
  `Uložit report ověření` (výchozí zapnuto, `NSOpenPanel.accessoryView`). Každá karta = položka **kopírovací fronty** (sériově
  s kopiemi — zatěžují tytéž disky): titulek `Ověření A001`, průběh `Ověřuji 120 / 1 234 souborů · 45 GB / 512 GB · 380 MB/s`,
  `Zrušit`; výsledek `VerifyReport.summary` zeleně/červeně/oranžově + `warnings` oranžově, tlačítka `Report ověření` (otevře
  HTML) a `Finder` (`NSWorkspace.activateFileViewerSelecting`). Report mimo kartu → věta `Report ověření je uložený mimo složku
  karty, aby nenarušil její historii ASC MHL: <cesta>`.

- [ ] **Step 1: Jádro testované** — `VerifyJob` v `OffloadKit/Desktop` (test: `VerifyJobTests.testQueueItemTexts` — průběh
  a výsledek z `VerifyResult` přes `VerifyReport`; zrušení = „Ověření zrušeno po …“, bez reportu). Run: `swift test`.

- [ ] **Step 2: GUI** — podle pravidel výše; vše, co čte disk (inspect, historie, volno, ověření), mimo hlavní vlákno.

- [ ] **Step 3: Ruční kontrola** (README-START oddíl 7, nové body 14–18): karta podruhé → zelený řádek a dialog; přidaný klip →
  oranžový řádek „nové soubory“ a Doplnit; FAT32 karta po změně časového pásma systému (Nastavení → Datum a čas) → stále
  „beze změny“; dva cíle na jednom disku s malým volnem → červená hláška; Ověřit zálohu na složce dne → položka za každou kartu.

- [ ] **Step 4: Záznam a commit** — `docs/ROZDILY.md`: řádky „Karta už zálohovaná“, „Doplnit jen nové klipy“, „Název karty
  z klipů“, „Ověřit zálohu“, „Kontrola místa“ Mac ✅. `docs/VYVOJ-MAC.md`: `- Úkol 49 — GUI: řádek historie u karty, dialog
  Karty už jsou zálohované, Doplnit, návrh názvu, hlášky místa, Ověřit zálohu (⇧⌘O) jako položka fronty`.
  Run: `git add -A mac docs && git commit -m "Mac GUI: history line, backed-up prompt, append, names, space, verify backup"`

---

### Task 50: Dokumentace a verze 0.3.0 (plán 6)

**Files:**
- Modify: `mac/Sources/OffloadKit/Report.swift` (`OffloadVersion.string = "0.3.0"`), `mac/Tests/OffloadKitTests/VersionTests.swift`
- Modify: `mac/README.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`, `docs/VYVOJ-MAC.md`

- [ ] **Step 1:** `VersionTests` na `0.3.0` (selže), `OffloadVersion.string = "0.3.0"`, `swift test` zelené.
- [ ] **Step 2: `docs/FORMATY.md`** — `version` „`0.3.0` (Mac)“; `destinations[].volumeSerial`: doplnit „Mac: u FAT/exFAT/NTFS
  stejně jako Windows, u ostatních FS `uuid:<UUID svazku>`“; „Místní soubory“: „Mac převezme stejný tvar“ → „Mac od 0.3.0 stejný
  tvar; zámek `flock` v `locks/history-<sha256>.lock`; `fsSerial` z boot sektoru, záložně `uuid:<UUID>`“; „Report ověření“,
  „Doplnění“ a „`<karta>.xxh64`“ — Mac od 0.3.0.
- [ ] **Step 3: `docs/ROZDILY.md`** — verze Mac `0.3.0`; řádky plánu 6 podle úkolů 34–49; záměrné rozdíly: „Historie karet“,
  „Doplnění“, „Ověření zálohy“, „Kontrola místa“, „Hlášky česky“ přepsat na obě verze s Mac odlišnostmi (zámky, `fsSerial`
  záložně `uuid:`, chyby parseru CLI anglicky).
- [ ] **Step 4: `mac/README.md`** — příkazy `offload append`, `offload verify [--report]`, `--name auto`, `--no-history`; GUI
  (řádek historie, Doplnit, Ověřit zálohu ⇧⌘O).
- [ ] **Step 5: Závěrečné ověření** — `swift build -c release && swift test && scripts/build-app.sh`. **Křížově s Windows**
  (Review focus 6): (a) karta zkopírovaná a doplněná Macem → Windows `bin\offload.exe verify` OK, Windows `append` do ní přidá
  generaci 3 a Mac `offload verify` → 3 generace OK; (b) karta zkopírovaná a doplněná Windows 0.6.0 → Mac `verify` OK, Mac
  `append` generace N+1 a Windows `verify` OK; (c) `ascmhl verify` na obou. Výsledky do commitu a do VYVOJ-MAC.
- [ ] **Step 6: Commit** — VYVOJ-MAC: `- Úkol 50 — verze 0.3.0 (plán 6): FORMATY, ROZDILY, README; křížové ověření s Windows: <výsledek>`.
  Run: `git add -A mac docs && git commit -m "Mac 0.3.0: docs after plan 6 (cards and data safety)"`

---

## Plán 7 pro Mac — výstupy pro produkci a střih (úkoly 51–55, verze 0.4.0)

### Task 51: Hashe kopie end-to-end — XXH3-128 u každého souboru, MD5 navíc (`hash: both`), `--hash`

**Files:**
- Modify: `mac/Sources/OffloadKit/Engine.swift` (`SourceHasher` přes `MultiHasher`, `DestWriter.verify` všemi druhy, `OffloadEngine.blocks`)
- Modify: `mac/Sources/OffloadKit/Report.swift` (`build`: `xxh128`, `md5`, `hashAlgorithms`), `HTMLReport.swift` (dlaždice, sloupce)
- Modify: `mac/Sources/OffloadKit/Job.swift` (`JobConfig.hash`), `Bench.swift` (`--hash md5`), `mac/Sources/offload/main.swift` (`--hash xxh64|both`)
- Modify: `mac/Sources/OffloadApp/Views.swift` (volba „Kontrolní součet“, popisek ověření)
- Create: `mac/Tests/OffloadKitTests/HashCopyTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `MultiHasher`, `HashKinds`, `FileHashes` (úkol 32), `OffloadPlan.hashKinds` (úkol 47), `AppSettings.hash` (úkol 35), `Msg.unknownHash`.
- Produces:
  - `SourceHasher.hashes: [FileHashes?]`, `SourceHasher.blocks: [[UInt64]]` (XXH3-64 bloků každého souboru — pro manifest, úkol 65)
  - `OffloadReport.build(plan:hashes: [FileHashes?], …)` (místo `[UInt64?]`)
  - `JobConfig.hash: HashKinds = .xxh64` (z nastavení `hash`, CLI `--hash`)
  - `Texts.verifyLabel(_ k: HashKinds) -> String`, `Texts.md5Slower`

Pravidla (FORMATY „`offload_report.json`“, „`<karta>.md5`“, „ASC MHL v2“; Windows návrh §3.5, verify-in-proxy R2):

- Každý soubor dostane `xxh64` **a** `xxh128` (XXH3-128 — Windows 0.6.0 ho zapisuje vždy; `.xxh64` a `hashAlgorithms` se tím
  nemění); s volbou `both` navíc `md5`. Všechno v jednom průchodu čtení karty (`MultiHasher`, blok = `chunkSize`), hasher na
  vlastní sériové frontě, karta se tím nebrzdí (MD5 ~0,6–0,8 GB/s — u CFexpress zpomalí, u SD/USB 3 ne).
- Zpětné čtení porovná **všechny** druhy, které kopie má: jiná velikost `size mismatch: <disk> != <karta>`, xxh64 `HASH MISMATCH:
  dest <x> != source <y>`, MD5 `MD5 MISMATCH: dest <x> != source <y>` (anglicky — JSON je sdílený; HTML je přeloží, úkol 55).
- Report: `hashAlgorithms` jen s MD5 (`["xxh64","md5"]`), `files[].xxh128`, `files[].md5`; `<karta>.md5` (úkol 41); MHL `<xxh128>`
  a `<md5>` (úkol 40); TXT `Hash:        xxh64+md5`; HTML dlaždice „XXH64 + MD5“ a sloupec na algoritmus v seznamu souborů.
- Volba (nastavení `hash`, výchozí `xxh64`; GUI „Kontrolní součet“ `xxHash64 (rychlý)` / `xxHash64 + MD5`; zaškrtávátko ověření
  `Ověřit kopie zpětným čtením (xxHash64)` / `(xxHash64 + MD5)`; při MD5 šedě `MD5 je pomalejší — u rychlých karet (CFexpress)
  může zpomalit kopii.`). CLI `--hash xxh64|both`, jiná hodnota `Msg.unknownHash`. Doplnění používá součty dřívější zálohy.
- `offload bench --hash md5` měří čtení s MD5 navíc.

- [ ] **Step 1: Padající testy** — `HashCopyTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class HashCopyTests: XCTestCase {
    func copy(_ kinds: HashKinds, corrupt: Bool = false) throws -> (OffloadReport, String) {
        let dir = tempDir()
        let card = dir.sub("card"), dest = dir.sub("dest")
        let data = TestFS.writeRandom(card + "/A.bin", size: 5 << 20, seed: 4)
        try FileManager.default.createDirectory(atPath: dest, withIntermediateDirectories: true)
        var o = OffloadOptions(); o.allowSourceVolume = true; o.chunkSize = 1 << 20
        let e = OffloadEngine(options: o)
        var plan = try e.prepare(source: card, destRoots: [dest], cardName: "A001", date: "2026-10-08")
        plan.hashKinds = kinds
        if corrupt { e.afterWrite = { path in if path.hasSuffix("/A.bin") { try? Data([0]).write(to: URL(fileURLWithPath: path)) } } }
        let r = try e.run(plan)
        let h = MultiHasher(kinds: .both, blockSize: 1 << 20)
        data.withUnsafeBytes { h.update($0.baseAddress!, data.count) }
        XCTAssertEqual(e.blocks(0), h.finish().blocks, "block hashes of the card read")
        return (r, plan.cardPaths[0])
    }

    func testXxh64CopyHasXxh128ButNoMd5() throws {
        let (r, card) = try copy(.xxh64)
        XCTAssertNil(r.hashAlgorithms)
        XCTAssertEqual(r.files[0].xxh128?.count, 32)
        XCTAssertNil(r.files[0].md5)
        XCTAssertFalse(FileManager.default.fileExists(atPath: card + "/_REPORT/A001.md5"))
        XCTAssertTrue(try String(contentsOfFile: card + "/ascmhl/" + MHLReader.readChain(card + "/ascmhl")![0].path, encoding: .utf8).contains("<xxh128 "))
    }

    func testBothWritesMd5Everywhere() throws {
        let (r, card) = try copy(.both)
        XCTAssertEqual(r.hashAlgorithms, ["xxh64", "md5"])
        XCTAssertEqual(r.files[0].md5?.count, 32)
        XCTAssertTrue(FileManager.default.fileExists(atPath: card + "/_REPORT/A001.md5"))
        XCTAssertTrue(r.summary.contains("Hash:        xxh64+md5"))
    }

    func testCorruptedDestinationFailsVerification() throws {
        let (r, _) = try copy(.both, corrupt: true)
        XCTAssertTrue(r.files[0].errors[0]?.hasPrefix("size mismatch") ?? false)
        XCTAssertFalse(r.safeToFormat)
    }

    func testVerifyLabel() {
        XCTAssertEqual(Texts.verifyLabel(.xxh64), "Ověřit kopie zpětným čtením (xxHash64)")
        XCTAssertEqual(Texts.verifyLabel(.both), "Ověřit kopie zpětným čtením (xxHash64 + MD5)")
    }
}
```

(`OffloadEngine.afterWrite: ((String) -> Void)?` — testovací háček po přejmenování souboru na cíli, před ověřením;
`OffloadEngine.blocks(_ i: Int) -> [UInt64]` — bloky souboru `i` z hasheru.)

- [ ] **Step 2: Implementace** — `SourceHasher` drží `MultiHasher` na soubor (`begin(kinds:blockSize:)`, `update(chunk)`,
  `finish(i)` uloží `hashes[i]` a `blocks[i]`); `DestWriter.verify(files:sourceHashes: [FileHashes?], kinds:)` hashuje přes
  `hashFile(…, kinds:)` a porovná velikost → xxh64 → MD5 v tomto pořadí s texty výše; `OffloadReport.build` zapíše `xxh64Hex`,
  `xxh128`, `md5Hex`, `hashAlgorithms = kinds == .xxh64 ? nil : kinds.names`. `JobConfig.hash` → `plan.hashKinds` (celá kopie);
  doplnění bere `plan.hashKinds` z plánu (úkol 47). CLI `copy`/`append` `--hash`, `bench --hash md5`. GUI volba.

- [ ] **Step 3: Testy projdou; záznam a commit** — Run: `swift test`. `docs/FORMATY.md`: `<karta>.md5` „(Windows od 0.3.0, Mac
  od 0.4.0)“, `files[].xxh128` „Windows od 0.6.0, Mac od 0.4.0“, MHL `<xxh128>` totéž. `docs/ROZDILY.md`: řádky „MD5 navíc
  k xxh64“ a „XXH128 v ASC MHL“ Mac ✅. `docs/VYVOJ-MAC.md`: `- Úkol 51 — kopie počítá xxh64 + XXH3-128 (+ MD5 s volbou both)
  a XXH3-64 bloků v jednom průchodu, ověření všemi druhy; --hash, bench --hash md5, volba v GUI`.
  Run: `git add -A mac docs && git commit -m "Mac: xxh128 for every file, optional MD5, block hashes of the card read"`

---

### Task 52: Seznam klipů — CSV a Avid ALE (karta i den)

**Files:**
- Create: `mac/Sources/OffloadKit/Outputs/ClipList.swift`
- Modify: `mac/Sources/OffloadKit/Job.swift` (zápis při každém přepisu reportu ve fázi 2), `Timecode.swift` (`length(frames:fps:)`, `add`)
- Modify: `mac/Sources/offload/main.swift` (`--no-clip-list`, `--ale`), `mac/Sources/OffloadApp/Views.swift` (dvě volby)
- Create: `mac/Tests/OffloadKitTests/ClipListTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `CardRecord.clips`, `files` (úkol 38), `Timecode` (úkol 5), `ReelResolver` (úkol 54 — do té doby cívka z `ReelNames` a jména karty), `OutputSettings` (úkol 35).
- Produces:
  - `public struct ClipRow { clip: Clip; info: ClipInfo?; card: String; reel: String; lut: String?; xxh64: String?; md5: String?; size: Int64 }`
  - `public enum ClipList { static func rows(_ record: CardRecord) -> [ClipRow]; static func csv(_ rows: [ClipRow]) -> Data; static func ale(_ rows: [ClipRow]) -> [(suffix: String?, data: Data)]; static func write(rows: [ClipRow], folder: String, baseName: String, csv: Bool, ale: Bool) throws }`

Pravidla (FORMATY „Seznam klipů“ — tady je přesná specifikace sloupců; Windows návrh §3.3):

- Soubory karty `_REPORT/<karta>_clips.csv`, `_REPORT/<karta>_clips.ale` (dne: úkol 53). **Odvozené**: přepisují se celé
  (dočasné jméno + `rename`) při každém zápisu reportu ve fázi 2 a pokrývají **všechny generace** (`CardRecord`). Proxy kamery
  (`SUB`, `PROXY`) se neuvádějí; bez analýzy klipy z `ClipDetector`, metadata prázdná.
- **CSV:** UTF-8 **s** BOM, čárka, CRLF, RFC 4180 (pole s čárkou, uvozovkou, CR/LF v uvozovkách, uvozovky zdvojené),
  hlavička vždy: `Clip Name,File Name,Path,Card,Camera,Reel,Start TC,Duration,FPS,Resolution,Codec,LUT,Size (bytes),XXH64,MD5`.
  `Start TC` jak ho dá zdroj (`;` u drop-frame), prázdné bez TC; `Duration` `HH:MM:SS:FF` v nominálních snímcích, bez fps
  `H:MM:SS`; `FPS` invariantně (`23.976`, `25`, `29.97`); `Resolution` `3840x2160`; `Codec` u RAW formát (`Blackmagic RAW`);
  `XXH64`/`MD5` hlavního souboru (u vícedílných klipů prvního dílu). Hodnoty začínající `=`, `+`, `-`, `@` se **neupravují**.
- **ALE:** UTF-8 **bez** BOM, CRLF, tabulátory (tabulátor a konec řádku v hodnotě → mezera): `Heading`, `FIELD_DELIM\tTABS`,
  `VIDEO_FORMAT\t<1080|720|CUSTOM>` (nejčastější výška), `AUDIO_FORMAT\t48khz`, `FPS\t<fps>`, prázdný řádek, `Column`,
  `Name\tTape\tStart\tEnd\tTracks\tSource File\tCamera\tLUT\tResolution\tCodec\tXXH64\tMD5`, prázdný řádek, `Data`, řádky.
  `Tape` = cívka, `End` = start + délka (výlučně; drop-frame jako kamera), bez TC `00:00:00:00`, `Tracks` = `V` + `A1…An`
  (kanály, nejvýš 8). **Smíšené fps** → soubor na každé: `<název>_clips_25.ale`, `<název>_clips_23.976.ale` (klip bez fps patří
  k nejčastějšímu; když fps nezná nikdo, `FPS\t25`). ALE, které se už nevytváří (změnily se frekvence), se smaže — jen když je
  ALE zapnuté nebo už existuje. Seznam vypnutý, ale už ve složce (CSV i ALE) se při dalším zápisu přesto přepíše; nikdy se nemaže
  a nově nevytvoří.
- Volby `outputs.clipListCsv` (výchozí zapnuto), `outputs.clipListAle` (vypnuto); GUI „Seznam klipů CSV“, „Seznam klipů pro Avid
  (ALE)“; CLI `--no-clip-list`, `--ale`.

- [ ] **Step 1: Padající testy** — `ClipListTests.swift`: escapování CSV (`a,b` → `"a,b"`, `a"b` → `"a""b"`, nový řádek,
  čeština), BOM a CRLF; ALE hlavička a tabulátory (hodnota s tabulátorem → mezera), smíšené fps → dva soubory se správnými
  příponami, klip bez TC (`Start` `00:00:00:00`), drop-frame `End` (`00:59:59;29` + 2 snímky při 29.97 DF → `01:00:00;03`),
  `Tracks` `VA1A2`, klip bez kamery; `write` smaže ALE frekvence, která zmizela, a nevytvoří vypnutý seznam, ale přepíše existující.

```swift
    func testCsvEscapingBomCrlf() throws {
        let row = ClipRow(clip: Clip(name: "A,1", path: "CLIP/A,1.mov", format: .video, segments: ["CLIP/A,1.mov"], bytes: 5, isCameraProxy: false),
                          info: nil, card: "A001", reel: "A001", lut: "Q\"x", xxh64: "1111111111111111", md5: nil, size: 5)
        let text = String(decoding: ClipList.csv([row]), as: UTF8.self)
        XCTAssertTrue(text.hasPrefix("\u{FEFF}Clip Name,File Name,Path,Card,Camera,Reel,Start TC,Duration,FPS,Resolution,Codec,LUT,Size (bytes),XXH64,MD5\r\n"))
        XCTAssertTrue(text.contains("\"A,1\",\"A,1.mov\",\"CLIP/A,1.mov\",A001,,A001,,,,,,\"Q\"\"x\",5,1111111111111111,\r\n"))
    }
```

- [ ] **Step 2: Implementace** podle pravidel; `Timecode.length(frames:fps:)` (nominální snímky), `Timecode.add` s drop-frame
  (`;`) jako kamera. Zápis z `OffloadJob` po každém `report.write(…, includeHashRecords: false)` ve fázi 2 a po doplnění (nad
  `CardRecord.load(cardPath)`).

- [ ] **Step 3: Testy projdou; ruční kontrola** — `<karta>_clips.csv` otevřít v Numbers/Excelu (diakritika), importovat do
  DaVinci Resolve (Media Pool → Import Metadata), ALE do Avidu, je-li po ruce; výsledek do VYVOJ-MAC.

- [ ] **Step 4: Záznam a commit** — FORMATY „Seznam klipů (Windows od 0.3.0, Mac od 0.4.0)“; ROZDILY řádek ✅; VYVOJ-MAC
  `- Úkol 52 — seznam klipů CSV (BOM, CRLF, RFC 4180) a ALE (bez BOM, soubor na fps) karty, odvozený ze všech generací`.
  Run: `git add -A mac docs && git commit -m "Mac: clip lists CSV and Avid ALE"`

---

### Task 53: Denní report — `<datum>/_REPORT/<datum>_day_report.html/.pdf`, regenerace po úloze, `offload day-report`

**Files:**
- Create: `mac/Sources/OffloadKit/Outputs/DayReport.swift`, `Outputs/DayReportService.swift`
- Modify: `mac/Sources/OffloadKit/Job.swift` (po úloze pro každý cíl), `mac/Sources/offload/main.swift` (`day-report`, `--no-day-report`)
- Modify: `mac/Sources/OffloadApp/OffloadApp.swift` (Nástroje → „Denní report…“), `Views.swift` (volba)
- Create: `mac/Tests/OffloadKitTests/DayReportTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `CardRecord` (úkol 38), `ClipList` (úkol 52), `FileLock`, `Locks.day` (úkol 37), `HTMLReport` (styl), `PDFReport.writeHTML` (úkol 45), `Msg` (dny).
- Produces:
  - `public struct DayCard { folder: String; record: CardRecord?; kind: DayCardKind }` (`card`, `noReport`, `otherFolder`)
  - `public enum DayReport { static func cards(_ dayFolder: String) throws -> [DayCard]; static func html(_ dayFolder: String, cards: [DayCard]) -> String }`
  - `public enum DayReportService { static func regenerate(root: String, date: String, outputs: OutputSettings, wait: TimeInterval) -> Result<String, Error>; static func forFolder(_ path: String) throws -> String }`

Pravidla (FORMATY „Denní report“; Windows návrh §3.2):

- Na každém cíli úlohy po skončení každé úlohy (i doplnění a zrušené) a na vyžádání; `<cíl>/<YYYY-MM-DD>/_REPORT/<YYYY-MM-DD>_day_report.html/.pdf`
  + `<datum>_clips.csv` / `.ale` (podle `outputs`). **Odvozené** soubory — přepisují se celé; nikdy nesahá na soubory ověření
  (`*_verify_*`) v téže `_REPORT`; smaže ALE frekvence, která se už nevytváří.
- Karty dne = podsložky `<datum>/` kromě `_REPORT` a skrytých; jen podsložky s `_REPORT/` nebo `ascmhl/` jsou karty (bez
  `offload_report.json` → řádek „bez reportu (probíhá, nebo byl přerušen)“); ostatní (`SOUND`) „jiná složka (bez reportu)“.
- Kořen disku, složka karty ani složka bez karty s reportem se za den nepovažují: `Msg.dayIsVolumeRoot`, `Msg.notADayFolder`,
  karta jménem `_REPORT` → `Msg.dayReportFolderIsCard` (nic se nezapíše).
- Obsah (styl `HTMLReport`, A4 na šířku, bez náhledů): `Denní report — 6. 10. 2026`, cíl, `Projekt: X` (když ho mají všechny karty
  stejný); dlaždice karty, soubory, data, klipy, celková délka, ověřené karty „7 / 8“ (**ověřená = `CardRecord.verified`** —
  nedokončená `NEDOKONČENO` žlutě, s `verifyPending` `OVĚŘUJE SE`); tabulka karet *Karta · Čas · Soubory · Data · Klipy ·
  Délka · Kamery · Ověření · Chyby · Proxy (hotovo/selhalo/bez proxy) · LUTy · Doplnění*; sekce Problémy (selhané soubory,
  prvních 5 cest, neověřené karty, nečitelné reporty), LUTy (název → počet klipů); patička s verzí.
- Jeden zapisovatel na den: `FileLock` `Locks.day(<cíl>/<datum>)`; úloha čeká nejvýš 5 s (zrušená taky), pak denní report
  přeskočí (log); CLI/menu čeká 5 s a pak `Msg.dayReportBusy`. Selhání se jen zaloguje, úlohu nikdy neshodí.
- Zámek dne **se nesdílí s Windows** — Mac a PC píšící do téhož dne: vyhraje poslední zápis (odvozené soubory, FORMATY).
- `<karta>/_REPORT` vzniká **hned na začátku kopie** (prázdná), aby byla rozpracovaná karta v denním reportu vidět (změna
  `OffloadEngine.run`). Jména karet `_REPORT`, `_PROXY`, `ascmhl` (jakákoli velikost písmen) dostanou na konec `_`
  (`Layout.sanitize`) — změna i pro celou kopii.
- Volba `outputs.dayReport` (výchozí zapnuto), GUI „Denní report po každé kartě“, CLI `--no-day-report`, `offload day-report
  <složka dne>` (vypíše `Msg.dayReportWritten(path)`, kód 0/1).

- [ ] **Step 1: Padající testy** — `DayReportTests.swift`: složka dne se třemi kartami (report z Macu 0.2.0 bez nových klíčů,
  report z Windows 0.6.0 s doplněním — fixture z úkolu 33, poškozený JSON), složka bez reportu, `SOUND`; `cards` rozliší druhy;
  HTML obsahuje „bez reportu (probíhá, nebo byl přerušen)“, „jiná složka (bez reportu)“, „1 / 2“ ověřených; `verifyIncomplete`
  karta není ověřená; soubory `_verify_` v `_REPORT` dne zůstanou bajtově beze změny; zámek dne obsazený → `regenerate` vrátí
  chybu po `wait`; `Layout.sanitize("_report")` → `_report_`.

- [ ] **Step 2: Implementace** podle pravidel. PDF přes `PDFReport.writeHTML` (WebKit, mimo hlavní vlákno jako dosud).

- [ ] **Step 3: Testy projdou; záznam a commit** — FORMATY „Denní report (Windows od 0.3.0, Mac od 0.4.0)“, v pravidlech zámku
  „Mac: `flock` `locks/day-<sha1>.lock`, se zámkem Windows se nevylučuje“; ROZDILY řádek ✅ a záměrný rozdíl „Denní report a seznamy
  klipů“ na obě verze; VYVOJ-MAC `- Úkol 53 — denní report (HTML/PDF, seznamy klipů dne) po každé úloze pod zámkem dne,
  offload day-report, prázdný _REPORT karty od začátku kopie, vyhrazená jména karet s _`.
  Run: `git add -A mac docs && git commit -m "Mac: day report, regenerated after each job"`

---

### Task 54: Proxy — přelinkování (cívka, `reel_name`, všechny zvukové stopy), složky jako na kartě, bez proxy u lehčího originálu

**Files:**
- Create: `mac/Sources/OffloadKit/ProxyPaths.swift`, `mac/Sources/OffloadKit/LighterSource.swift`
- Modify: `mac/Sources/OffloadKit/Media.swift` (`ClipInfo.reel`, `audioStreams: [Int]`, `videoBitrate`, `FFProbe`), `ReelNames.swift` (`ReelResolver`)
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (`-metadata:s:v:0 reel_name=`, `-map 0:a?`, AAC/PCM na proud), `FilterGraph.swift` (beze změny burn-inu)
- Modify: `mac/Sources/OffloadKit/Job.swift` (cesty proxy, `_2` jen ve stejné složce, poznámka, `skipLighterSources`, místo)
- Modify: `mac/Sources/OffloadKit/ProxyCopy.swift` (`toExtraFolder` s relativní složkou), `HTMLReport.swift` (odkaz „hotovo“, „bez proxy“ šedě)
- Modify: `mac/Sources/offload/main.swift` (`--proxy-always`), `Desktop/AppSettings.swift` (`proxy.skipLighterSources`), `Views.swift`
- Create: `mac/Tests/OffloadKitTests/ProxyRelinkTests.swift`, `ProxyPathsTests.swift`, `LighterSourceTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `ReelNames` (úkol 43), `SpaceEstimator.proxyVideoMbps` (úkol 48), `ProxyRenderer.usesHardwareDecode` (úkol 19), `LutUse` (úkol 21), `CardRecord` (úkol 38), `ProxyCopy.renameNoReplace` (úkol 20).
- Produces:
  - `public enum ReelResolver { static func resolve(_ clip: Clip, info: ClipInfo?, cardName: String) -> String }`
  - `public enum ProxyPaths { static func folder(of clip: Clip) -> String; static func relative(_ clip: Clip, flat: Bool) -> String; static func pick(_ wanted: String, taken: Set<String>) -> String; static func isFlat(_ records: [CardRecord]) -> Bool }`
  - `public enum LighterSource { static func note(_ clip: Clip, info: ClipInfo, settings: ProxySettings, lut: LutUse?) -> String? }`
  - `ProxySettings.skipLighterSources: Bool = true`; `ClipEntry.originalLighter: Bool` (odvozené z poznámky, mimo JSON)

Pravidla (FORMATY „Struktura složek“ (proxy ve složkách), „`proxy.skipLighterSources`“, `clips[].info.reel`; Windows návrh §3.4, VYVOJ-WINDOWS 0.5.1):

- **Cívka** (`clips[].info.reel`): tag kamery (ffprobe `reel_name`, `tape_name`, `reel` ve formátu i proudech), jinak cívka z názvu
  klipu (`ReelNames`), jinak jméno složky karty. S platným TC proxy dostane `-metadata:s:v:0 reel_name=<cívka>` (MOV ho zapíše do
  stopy `tmcd`; ověřit ffprobe). Klip bez TC bez `-timecode` i bez `reel_name`.
- **Zvuk:** `-map 0:a?` (všechny stopy); H.264/HEVC AAC na každý proud `-b:a:<i>` = 64 kb/s × kanály (128–512k), ProRes `pcm_s16le`.
- Přejmenovaná proxy (`_2`) → poznámka `Msg.proxyRenamed("<jméno>_2")`.
- **Složky jako na kartě** (0.5.1): `_PROXY/<složka klipu>/<klip>.mov` (klip v kořeni karty `_PROXY/<klip>.mov`; sekvence `.ari`
  je složka — proxy vedle ní, pojmenovaná po ní; R3D ve složce `.RDC`); složky kamery (`PRIVATE/M4ROOT/CLIP` …) zůstanou;
  `_2`, `_3` jen při shodě **ve stejné složce** nebo s proxy, která tam už je; i ve složce pro proxy navíc
  `<složka>/<datum>/<karta>/<složka klipu>/`. **Starší zálohy se nepřesouvají:** doplnění pozná plochou zálohu z reportů
  (`isFlat`: některý klip z podsložky má `proxyPath` přímo v `_PROXY/` a žádný ve složce) a nové proxy pak jdou přímo do
  `_PROXY` se starým pravidlem `_2`; jinak složky. Obsazená jména = všechny `.mov` pod `_PROXY` na všech cílech; nic se nepřepíše.
  Mac 0.1–0.3 zálohy jsou ploché.
- **Bez proxy u lehčího originálu** (`proxy.skipLighterSources`, výchozí `true`, CLI `--proxy-always` = `false`): **jen u proxy
  H.264/HEVC** a **jen když se nepoužije LUT ani ořez** (klip s `lut.source` jiným než `none` nebo s `frameAspect`/`frameScale < 1`
  proxy dostane vždy; burn-in nevadí): originál H.264/HEVC (`info.codec` začíná `h264`/`hevc`), výška ≤ výška proxy a šířka ≤
  výška × 16/9, datový tok videa (ffprobe `bit_rate` proudu, jinak kontejneru minus zvukové proudy) ≤ `bitrateMbps` →
  `proxyStatus: skipped` + poznámka `bez proxy — originál je lehčí (5,2 Mb/s ≤ 10 Mb/s)` (česky, jedno desetinné místo bez
  koncové nuly). Neznámý tok nebo rozměr = proxy. Náhledy vznikají; kontrola místa s takovým klipem nepočítá. HTML „bez proxy“
  šedě, karta úlohy „Proxy x / y“ ho nepočítá do y, souhrn „…, 1 bez proxy (lehčí originál)“.
- HTML: stav „hotovo“ odkazuje na soubor proxy (`../_PROXY/idle/1.mov`); sloupec Proxy bez dělení slov (úkol 55).

- [ ] **Step 1: Padající testy** — `ProxyRelinkTests` (ffmpeg, jinak `XCTSkip`): syntetický klip s TC `01:00:00:00` a dvěma
  zvukovými proudy (`sine` ×2) → proxy má `timecode`, `reel_name` u proudu `tmcd`, dva zvukové proudy; `ReelResolver` (tag >
  jméno > karta); `ProxyPathsTests` (tabulka cest včetně `.ari`, `.RDC`, kořene karty, `_2` jen ve stejné složce, plochá záloha);
  `LighterSourceTests` (pravidlo: ProRes proxy vždy, LUT vždy, ořez vždy, 4K s nízkým tokem vždy, neznámý tok vždy, H.264 1080p
  5,2 Mb/s ≤ 10 → poznámka přesně `bez proxy — originál je lehčí (5,2 Mb/s ≤ 10 Mb/s)`).

- [ ] **Step 2: Implementace** podle pravidel; `FFProbe.parse` čte `bit_rate` proudu i formátu, `channels` každého zvukového
  proudu, tagy `reel_name`/`tape_name`/`reel`.

- [ ] **Step 3: Testy projdou; ruční kontrola** — proxy přelinkovat v DaVinci Resolve (Relink / „Link Proxy Media“) a v Premiere
  („Attach Proxies“, vyžaduje stejný počet stop); karta se stejnými jmény v různých složkách (`idle/1.mp4`, `listening/1.mp4`) →
  `_PROXY/idle/1.mov`, `_PROXY/listening/1.mov`; doplnění do zálohy z Macu 0.2 zůstane ploché.

- [ ] **Step 4: Záznam a commit** — FORMATY: „Proxy ve složkách jako na kartě (Windows od 0.5.1, Mac od 0.4.0)“, `proxy.skipLighterSources`
  „Mac od 0.4.0“, `clips[].info.reel` „Mac od 0.4.0“; ROZDILY: řádky „Proxy pro přelinkování“, „Proxy ve složkách jako na kartě“,
  „Bez proxy, když je originál lehčí“ Mac ✅ a záměrné rozdíly přepsat; VYVOJ-MAC `- Úkol 54 — proxy: cívka a reel_name ve stopě
  TC, všechny zvukové stopy, složky jako na kartě (plochá starší záloha zůstane plochá), bez proxy u lehčího H.264/HEVC originálu,
  --proxy-always`. Run: `git add -A mac docs && git commit -m "Mac: relinkable proxies, proxy folders as on the card, lighter originals"`

---

### Task 55: HTML reporty (české chyby, „Uložit jako PDF“, tisk, šířka tabulky), dokumentace a verze 0.4.0

**Files:**
- Modify: `mac/Sources/OffloadKit/HTMLReport.swift` (`errorText`, lišta PDF, CSS tisku, `<colgroup>`), `Verification/VerifyReport.swift`, `Outputs/DayReport.swift` (stejná lišta)
- Modify: `mac/Sources/OffloadKit/Report.swift` (`OffloadVersion.string = "0.4.0"`), `VersionTests.swift`
- Create: `mac/Tests/OffloadKitTests/HTMLErrorsTests.swift`
- Modify: `mac/README.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`, `docs/VYVOJ-MAC.md`

Pravidla (VYVOJ-WINDOWS „2026-10-08 — report: uložení do PDF“; Windows `HtmlReport.ErrorText`):

- **Chyby souborů česky v HTML/PDF** (JSON zůstává anglicky): `source: X` → `karta: <X česky>`; `verify: X` → `chyba při ověření:
  <X>`; `set aside: X` → `nesouhlasnou kopii nejde odložit, zůstala na místě: X`; `HASH MISMATCH: block N dest A != source B` →
  `nesouhlasí blok N (cíl A, karta B)`; `HASH MISMATCH: dest A != source B` → `nesouhlasí kontrolní součet (cíl A, karta B)`;
  `MD5 MISMATCH: …` → `nesouhlasí MD5 (cíl A, karta B)`; `size mismatch: X` → `jiná velikost: X`; `source size changed during copy X`
  → `soubor na kartě se během kopie změnil X`; `write failed` → `zápis selhal`; `cancelled` → `zrušeno`; `card changed while paused`
  → `karta se během pozastavení vyměnila nebo vytáhla`; `card differs from the backup: <algo> N != recorded R` → `karta se od zálohy
  liší (<algo> N, v záloze R) — nic se nepřesunulo ani nezapsalo`; ` (after N retries)` → ` (po N opakováních)`; neznámé beze změny.
- **Lišta jen pro obrazovku** nahoře v HTML reportu karty, denním a ověření: tlačítko `Uložit jako PDF` (`window.print()`),
  nápověda `v tisku zvolte „Uložit jako PDF“` a odkaz `Otevřít PDF report` na PDF vedle HTML; v `@media print` skrytá.
- **Tisk:** `@page{size:A4 landscape;margin:10mm}`, světlé barvy i z tmavého režimu, `tr{break-inside:avoid}`.
- **Tabulka klipů se vejde do okna:** `table-layout:fixed` + `<colgroup>` v % (sloupec Proxy vždy vidět, 11 %, náhledy 20 %),
  `overflow-wrap:anywhere`, náhledy `flex:1 1 0;max-width:128px`, do 760 px šířky skrýt Formát, TC a Audio; sloupec Proxy
  `word-break:normal;hyphens:none`.

- [ ] **Step 1:** `HTMLErrorsTests` — tabulka výše (každý řádek jeden `XCTAssertEqual(HTMLReport.errorText(…), …)`), lišta je
  v HTML a v `@media print` má `display:none`. Run → selže; implementace; `swift test`.
- [ ] **Step 2: Verze 0.4.0** (`VersionTests`), FORMATY `version` „`0.4.0` (Mac)“, ROZDILY verze a řádky plánu 7, README (příkazy
  `day-report`, `--hash`, `--ale`, `--no-clip-list`, `--no-day-report`, `--proxy-always`).
- [ ] **Step 3: Závěrečné ověření** — `swift build -c release && swift test && scripts/build-app.sh`; ruční: report karty v Safari
  → „Uložit jako PDF“ → A4 na šířku, světle; úzké okno → tabulka bez vodorovného posuvníku.
- [ ] **Step 4: Commit** — VYVOJ-MAC `- Úkol 55 — HTML: chyby souborů česky, lišta Uložit jako PDF, tisk A4 na šířku, tabulka
  klipů se vejde do okna; verze 0.4.0 (plán 7)`. Run: `git add -A mac docs && git commit -m "Mac 0.4.0: Czech file errors in HTML, PDF bar, docs after plan 7"`

---

Pokračování: [`2026-10-08-offload-mac-parity-3.md`](2026-10-08-offload-mac-parity-3.md) — úkoly 56–70 (plány 8, 9, 10).
