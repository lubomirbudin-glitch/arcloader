# Offload pro Mac — dorovnání 3 (Windows plány 8–10 a 0.5.x): implementační plán

> **Pro vykonavatele:** pokračování [`2026-10-08-offload-mac-parity-2.md`](2026-10-08-offload-mac-parity-2.md) (úkoly 32–55).
> Začíná až po úkolu 55 (verze 0.4.0). Platí celé **Global Constraints** a **Review Focus** prvního souboru tohoto plánu
> (formát na disku je smlouva, nic nepřepsat, jedna definice „ověřeno“, zelená jen při „Lze formátovat“, texty doslova jako
> Windows, zámky `flock` jen na místních souborech, testy jen v dočasných složkách). Pořadí 56 → 70.

**Goal:** Plán 8 (projekty, vlastní pravidla LUTů, BRAW Gen 4 → Gen 5, oznámení, kontrola aktualizací, podpis a notarizace,
verze 0.5.0), plán 9 (Pozastavit, „Zkopírovat znovu“, nový vzhled podle `docs/ui-navrh/`, verze 0.6.0) a plán 10
(Verify-in-Proxy: zápis mimo cache s kontrolou na macOS, blokový manifest, ověřovač bloků, odložené ověření s potvrzením
výsledku, HTTP tap pro ffmpeg a čtení před SDK, navázání po pádu, obsahová kontrola, verze 0.7.0).

**Spec:** [`../specs/2026-10-05-offload-mac-parity-design.md`](../specs/2026-10-05-offload-mac-parity-design.md), „Doplněk 3“ —
zejména D.23.1 (`F_NOCACHE` a jeho meze), D.23.5–D.23.7 (oznámení, vložení karty, tap), D.24 (co Mac nezrcadlí) a D.27 (nejistoty).

## Review Focus (soubor 3)

7. **Oprava přepíše dobrou kopii, smaže nesouhlasnou, nebo zapíše, když se karta od zálohy změnila.**
   → Úkol 62 `RepairEngineTests.testGoodCopyNeverWritten`, `testBadCopySetAsideNeverDeleted`, `testCardChangedRefusedNothingWritten`.
8. **Pauza drží zámek dne/historie nebo karta vyměněná během pauzy pokračuje v kopii.**
   → Úkol 61 `PauseTests.testCardSwappedWhilePausedStopsRemainingFiles`, `testPausedJobHoldsNoDayLock`.
9. **„Ověřeno“ z čtení, které mohlo jít z paměti** (macOS `F_NOCACHE` je jen nápověda).
   → Úkol 65 `CacheProbeTests.testResidentPagesAreDetected`, `testWrittenWithNoCacheLeavesNoResidentPages`; úkol 68
   `VerifySessionTests.testResidentFileIsNeverVerifiedFromTheTap`.
10. **Výsledek ověření během proxy se zapíše jen někam, nebo aplikace spadne mezi zápisy** — složka nesmí vypadat ověřeně.
    → Úkol 67 `DeferredVerifyTests.testCommitFailureLeavesIncompleteEverywhereWritten`, `testCrashBeforeCommitLeavesPending`;
    úkol 69 `ResumeTests.testPendingGenerationIsCompletedNotRenumbered`.
11. **Blok na cíli nesouhlasí, ale ověřovač ho přijme** (kus bloku, opakované čtení, jiná délka souboru).
    → Úkol 66 `BlockVerifierTests.testReReadWithDifferentDataIsMismatch`, `testPiecesAssembleByOffset`, `testShorterFileIsMismatch`.

---

## Plán 8 pro Mac — nastavení, oznámení, distribuce (úkoly 56–60, verze 0.5.0)

### Task 56: Projekty — profily nastavení, přepínač, `--project`, projekt v reportu a historii

**Files:**
- Modify: `mac/Sources/OffloadKit/Desktop/AppSettings.swift` (`SettingsProfile`, `ProjectSettings`, `AppSettings.projects`, `activeProject`, `active`)
- Create: `mac/Sources/OffloadKit/Desktop/Projects.swift`
- Modify: `mac/Sources/OffloadKit/Job.swift` (`JobConfig.project`, klon nastavení ve frontě), `Report.swift` (`project` při stavbě)
- Modify: `mac/Sources/offload/main.swift` (`--project`), `mac/Sources/OffloadApp/AppModel.swift`, `Views.swift`, `OffloadApp.swift` (menu Projekt, titulek okna)
- Create: `mac/Tests/OffloadKitTests/ProjectsTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `SettingsStore.decode/encode` se zachováním neznámých klíčů (úkol 35), `HistoryRecorder` (úkol 44), `AppendPlan.project` (úkol 46), `ReelSuggestion.next(project:)` (úkol 43), `DayReport` (úkol 53).
- Produces:
  - `public struct SettingsProfile: Codable, Equatable { destRoots; verify; verifyInProxy = true; verifyPrimary = "fastest"; analyzeClips; proxy; naming; hash; outputs; notify }`
  - `public struct ProjectSettings: Codable, Equatable { name: String; profile: SettingsProfile }` (v JSON plochý objekt `{name, destRoots, …}`)
  - `AppSettings.top: SettingsProfile` (horní klíče = „Bez projektu“), `projects: [ProjectSettings]`, `projectsRaw: [Any]` (nečitelné a duplicitní položky beze změny), `activeProject: String?`, `var active: SettingsProfile { get set }`
  - `public enum Projects { static let noProject = "Bez projektu"; nameProblem(_:existing:except:) -> String?; find; saveAs; rename; delete; activate; activeName; windowTitle(_:); deleteQuestion(_:); unknown(_:_:) }`
  - CLI `offload copy|append … --project <název>`

Pravidla (FORMATY „settings.json“ — Projekty; Windows návrh §4.1, R17):

- Horní klíče profilu (`destRoots`, `verify`, `verifyInProxy`, `verifyPrimary`, `analyzeClips`, `hash`, `naming`, `outputs`, `notify`,
  `proxy`) jsou profil **„Bez projektu“**; `projects` = pole pojmenovaných profilů stejného tvaru s klíčem `name`; `activeProject`
  = jméno nebo `null`. Společné zůstávají `tools`, `portNames`, `updates`. (Mac: `UserDefaults["settings"]`, stejné schéma.)
- Jména: oříznutá, 1–60 znaků, jedinečná bez ohledu na velikost písmen, „Bez projektu“ vyhrazené. Chyby doslova:
  `Název nesmí být prázdný.`, `Název může mít nejvýš 60 znaků.`, `„Bez projektu“ je vyhrazený název.`, `Projekt s tímto názvem už existuje.`
- **Nic se neztratí ani nezdvojí:** duplicitní jména a položky bez platného jména zůstanou v souboru beze změny a při uložení se
  zapíšou zpět **za** čitelné projekty; `projects`, které není pole, se zachová, dokud se v aplikaci neuloží projekt; `activeProject`
  jiného typu než text se zachová; jméno neexistujícího projektu se při čtení změní na `null` (kromě jména položky, kterou tato verze
  nečte — pak platí „Bez projektu“ a jméno se zachová).
- Úpravy v aplikaci mění **aktivní profil**; fronta drží **kopii** nastavení (`JobConfig` se staví při startu), takže přepnutí
  projektu nikdy nezmění zařazené úlohy. Během startu (kontroly LUTu, složky pro proxy, místa, dotazy) je přepínač zamčený;
  když se profil přesto změní, start se zastaví: `Během přípravy startu se změnil projekt — offload se nespustil. Spusťte ho znovu.`
- UI (stávající vzhled; nový vzhled úkol 63): nahoře „Projekt: [Bez projektu ▾]“, menu **Projekt** — „Uložit jako projekt…“
  (nový projekt z aktuálních hodnot, stane se aktivním), „Přejmenovat…“, „Smazat…“ (`Smazat projekt Film X? Nastavení projektu se
  ztratí, zálohy na discích zůstanou.`), dialog „Název projektu“; titulek okna `Offload — Film X`. V UI vždy „Projekt“, nikdy „Preset“.
- Projekt úlohy jde do reportu (`project`, bez projektu chybí) a do historie (`project`); **doplnění zapíše projekt dřívější zálohy**
  (z jejího reportu), ne projekt zobrazený v aplikaci; projekt smazaný mezitím → doplnění běží s nastavením „Bez projektu“ a karta
  úlohy řekne `Projekt Film X už neexistuje — doplnění běží s nastavením Bez projektu`. „Další v řadě“ počítá jen karty stejného
  projektu (úkol 43 už to umí). Denní report ukáže `Projekt: X`, když ho mají všechny karty dne stejný.
- CLI `--project <název>`: načte nastavení aplikace (`UserDefaults` domény aplikace — CLI v balíčku čte `UserDefaults(suiteName:
  "<bundle id aplikace>")`), profil projektu jako výchozí hodnoty, **výslovně zadané volby vyhrávají**; `-d` není povinné, když
  projekt má cíle; neznámý → `Projekt „X“ neexistuje. Projekty: Film X, Reklama Y.` (bez projektů `… Žádný projekt není uložený.`),
  kód 1; vypíše `Projekt: X (výchozí hodnoty z nastavení aplikace, volby z příkazové řádky mají přednost)`.

- [ ] **Step 1: Padající testy** — `ProjectsTests.swift`: round-trip; přepnutí tam a zpět beze ztráty (hodnoty profilů se
  nekopírují); přejmenování aktivního; smazání aktivního → „Bez projektu“; jména (tabulka chyb výše); **duplicitní jméno
  a položka bez jména zůstanou po uložení beze změny za čitelnými**; `projects` jako řetězec se zachová; neznámé klíče v projektu
  zachované; `JobConfig` z profilu je kopie (změna aktivního profilu po startu ji nezmění); report má `project`; doplnění zapíše
  projekt dřívější zálohy.

```swift
    func testDuplicatesAndUnreadableItemsAreKeptAfterReadable() throws {
        let json = #"{"projects":[{"name":"Film X","verify":false},{"name":"film x","verify":true},{"verify":true},"junk"],"activeProject":"FILM X"}"#
        var s = SettingsStore.decode(Data(json.utf8))
        XCTAssertEqual(s.projects.map(\.name), ["Film X"])
        XCTAssertEqual(Projects.activeName(s), "Film X")
        XCTAssertFalse(s.active.verify)
        try Projects.saveAs(&s, "Reklama Y")
        let saved = try XCTUnwrap(JSONSerialization.jsonObject(with: XCTUnwrap(SettingsStore.encode(s))) as? [String: Any])
        let items = try XCTUnwrap(saved["projects"] as? [Any])
        XCTAssertEqual(items.count, 5, "Film X, Reklama Y, then the three kept as they were")
        XCTAssertEqual((items[0] as? [String: Any])?["name"] as? String, "Film X")
        XCTAssertEqual((items[1] as? [String: Any])?["name"] as? String, "Reklama Y")
        XCTAssertEqual((items[2] as? [String: Any])?["name"] as? String, "film x")
        XCTAssertEqual(items[4] as? String, "junk")
    }
```

- [ ] **Step 2: Implementace** — `Projects.swift` (pravidla výše; funkce mění `inout AppSettings` a hází `ProjectError(message)`
  s českým textem), `SettingsStore`: `projects` se čte položku po položce (`ProjectSettings` přes tolerantní dekódování profilu po
  sekcích jako horní úroveň), nečitelné a duplicitní položky do `projectsRaw` (pořadí zachované), při `encode` `projects` =
  čitelné (s jejich neznámými klíči) + `projectsRaw`. `AppModel`: `settings.active` místo přímých polí; `JobConfig` staví
  `JobConfigs.full(from: settings.active, project: Projects.activeName(settings))`.

- [ ] **Step 3: Testy projdou; ruční kontrola** — uložit projekt, přepnout, upravit, přepnout zpět (nic se neztratí); úloha ve
  frontě + přepnutí projektu → úloha jede se svým nastavením; `offload copy … --project "Film X" --proxy-codec hevc` → kodek z CLI,
  ostatní z projektu.

- [ ] **Step 4: Záznam a commit** — FORMATY „settings.json“: projekty „Windows od 0.4.0, Mac od 0.5.0 (`UserDefaults`)“, `project`
  v reportu „Mac od 0.5.0“; ROZDILY řádek „Projekty“ Mac ✅; VYVOJ-MAC `- Úkol 56 — projekty: profily v nastavení (duplicity
  a nečitelné položky zachované), přepínač a menu Projekt, kopie nastavení ve frontě, project v reportu a historii (doplnění podle
  dřívější zálohy), CLI --project`. Run: `git add -A mac docs && git commit -m "Mac: projects"`

---

### Task 57: Vlastní pravidla LUTů a sériové číslo kamery

**Files:**
- Create: `mac/Sources/OffloadKit/LutRules.swift`
- Modify: `mac/Sources/OffloadKit/Luts.swift` (`LutResolver.resolve` v režimu `auto` nejdřív pravidla), `Media.swift` (`ClipInfo.cameraSerial`, `FFProbe`), `Luts.swift` (`SonyXML` čte `Device serialNo`)
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (`prepareLUT` — vlastní `.cube` podle cesty, `lut-custom-<8 hex sha1 cesty>.cube`)
- Modify: `mac/Sources/OffloadKit/Desktop/AppSettings.swift` (`proxy.lutRules` se zachováním neznámých), `ProxyPreflight` (chybějící `.cube` z pravidla)
- Modify: `mac/Sources/offload/main.swift` (`--lut-rule`), `mac/Sources/OffloadApp/LutRulesView.swift` (nové okno), `Views.swift`
- Create: `mac/Tests/OffloadKitTests/LutRulesTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `LutResolver`, `LutUse`, `LutCatalog`, `SonyXML` (úkoly 21, 28), `ReelResolver` (úkol 54), `Msg` (úkol 34).
- Produces:
  - `public enum LutMatch: String { camera, serial, reel, folder }`
  - `public struct LutRule: Equatable { match: LutMatch; value: String; lut: String?; lutPath: String? }`, `public enum LutRuleItem { case rule(LutRule), case kept(Any) }` (položka jiného tvaru / neznámý `match` — zachová se)
  - `public enum LutRules { static func parse(_ json: Any?) -> [LutRuleItem]; static func encode(_ items: [LutRuleItem]) -> [Any]; static func match(_ items: [LutRuleItem], clip: Clip, info: ClipInfo?, reel: String, cardName: String) -> (index: Int, rule: LutRule)?; static func reason(_ index: Int, _ rule: LutRule) -> String; static func parseCLI(_ spec: String) -> LutRule? }`
  - `ClipInfo.cameraSerial: String?` (JSON `cameraSerial`)
  - `LutUse.source = "rule"`

Pravidla (FORMATY „`proxy.lutRules`“, `clips[].lut.source`, `clips[].info.cameraSerial`; Windows návrh §4.2, R18):

- Pravidlo `{ "match": "camera"|"serial"|"reel"|"folder", "value": "…", "lut": "<id vestavěného>"|"none" }` nebo s `"lutPath"` místo
  `lut` (právě jedno). `camera` = model kamery (výrobce + model) **obsahuje** hodnotu, `serial` = sériové číslo **se rovná**,
  `reel` = cívka **začíná** hodnotou, `folder` = `<karta>/<složka klipu>` odpovídá vzoru s `*` a `?` — vše bez ohledu na velikost
  písmen. Platí **jen v režimu `auto`**, před rozpoznáním kamery; první shoda rozhoduje, i s cílem `none`.
- Známé pravidlo s neplatným obsahem (prázdná `value`, `lut` i `lutPath` zároveň nebo ani jedno) se při načtení zahodí. Pravidlo
  s **neznámým `match`** a položka jiného tvaru se **zachovají beze změny** a při hledání se přeskočí; pořadová čísla v důvodu
  (`pravidlo 2`) se počítají **i přes ně**.
- Report: `lut.source = "rule"`, `reason` `pravidlo 2: kamera obsahuje „FX6“` (`serial`: `sériové číslo je „…“`, `reel`: `cívka začíná
  „A“`, `folder`: `složka odpovídá „A001/CLIP*“`), `id` vestavěného; vlastní `.cube` `id` `null`. Pravidlo s cílem „Bez LUT“ dává
  `source: "none"`, ne `rule`.
- `cameraSerial`: Sony `M01.XML` (`<Device … serialNo="…">`), ffprobe tagy `com.apple.quicktime.camera.identifier`,
  `camera_serial_number`; ARRI a BRAW zatím ne (ověří vzorky); neznámé → pravidla `serial` nikdy nesedí.
- Renderer kopíruje každý vlastní LUT zvlášť (`lut-custom-<8 hex sha1 cesty>.cube`), ne jen jeden `look.cube`.
- Start blokuje chybějící `.cube` z pravidla: `LUT z pravidla 2 nenalezen: show.cube` (`Msg.ruleLutNotFound`). Do `Msg` přibudou
  `ruleLutNotFound`, `lutRuleNeedsAuto`, `badLutRule` (texty doslova z Windows `Messages.cs`).
- CLI `--lut-rule <match>=<hodnota>:<lut|cesta.cube|none>` (opakovatelné, pořadí = priorita), jen s `--lut auto`
  (jinak `--lut-rule platí jen s --lut auto`); špatný zápis `Msg.badLutRule(spec)` (text Windows).
- GUI: v sekci Proxy pod LUTem tlačítko „Pravidla LUTů… (2)“ (jen u „Automaticky podle kamery“) → okno „Vlastní pravidla LUTů“:
  řádky `[Podle ▾: Kamera (výrobce/model) | Sériové číslo kamery | Cívka (začátek) | Složka (vzor s * a ?)] [hodnota] → [LUT ▾:
  vestavěné… | Bez LUT | Vlastní…] [↑] [↓] [✕]`, „Přidat pravidlo“, nápověda „Pravidla se zkoušejí shora dolů, před automatickým
  rozpoznáním kamery.“, OK / Zrušit. Pravidla jsou součást projektu.

- [ ] **Step 1: Padající testy** — `LutRulesTests.swift`: každý `match`, pořadí, neplatná pravidla zahozená, neznámý `match`
  zachovaný při uložení a přeskočený s počítáním čísel (`pravidlo 3` po přeskočeném druhém), jen v `auto`, `none` → `source none`,
  `SonyXML` se `serialNo`, `parseCLI` (`camera=FX6:sony-slog3-sgamut3cine`, `folder=A001/*:/Volumes/L/show.cube`, `reel=A:none`,
  špatné vstupy), dva různé vlastní LUTy v jedné úloze (ffmpeg, jinak `XCTSkip`).
- [ ] **Step 2: Implementace** podle pravidel; `LutResolver.resolve` v `auto`: `LutRules.match` před `detect`.
- [ ] **Step 3: Testy; záznam a commit** — FORMATY `proxy.lutRules`, `lut.source = rule`, `cameraSerial` „Mac od 0.5.0“; ROZDILY
  „Vlastní pravidla LUTů“ ✅; VYVOJ-MAC `- Úkol 57 — vlastní pravidla LUTů (camera/serial/reel/folder, neznámá zachovaná),
  sériové číslo kamery (Sony XML, QuickTime), LUT podle cesty, --lut-rule, okno pravidel`.
  Run: `git add -A mac docs && git commit -m "Mac: custom LUT rules and camera serial"`

---

### Task 58: Blackmagic RAW Film Gen 4 → Gen 5 (`braw-decode --gen5`)

**Files:**
- Modify: `mac/Tools/braw-decode/braw-decode.cpp` (volba `--gen5`, `--info` vrací účinné hodnoty + `colorScienceGenOriginal`)
- Modify: `mac/Sources/OffloadKit/RawBackends.swift` (`BRAWBackend` předá `--gen5`), `Luts.swift` (`LutResolver.braw`: účinná Gen 5 → BMD LUT), `Media.swift` (`ClipInfo.colorScienceGenOriginal`)
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (`ProxySettings.brawGen4ToGen5 = false`), `main.swift` (`--braw-gen5`), `Views.swift` (sekce Dekodéry)
- Modify: `mac/Tests/OffloadKitTests/BrawDecodeTests.swift`, `BrawBackendTests.swift`, `LutResolverTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

Pravidla (Windows návrh §4.2 „BRAW Film Gen 4 → Gen 5“, R26; spec D.15):

- `braw-decode --gen5` (pro `--info` i snímky): naklonovat atributy klipu (`CloneClipProcessingAttributes`), `SetClipAttribute(
  blackmagicRawClipProcessingAttributeColorScienceGen, Variant u16 = 5)`, zpětně přečíst gamu/gamut/generaci; snímky přes
  `CreateJobDecodeAndProcessFrame(clipAttributes, nullptr)` (dnes bez atributů). `--info --gen5` vrací účinné hodnoty
  a `"colorScienceGenOriginal": 4`. Nepodaří-li se nastavit → varování na stderr `braw-decode: --gen5: cannot set color science
  generation (0x…), decoding as recorded`, dekóduje se jako Gen 4 (bez LUTu, jako dosud), klíč `colorScienceGenOriginal` chybí.
  Týká se jen gamy „Blackmagic Design Film“; Video a Extended Video zůstanou.
- `proxy.brawGen4ToGen5` (bool, chybí = `false`) — výchozí **vypnuto**, dokud to neověří vzorek. `LutResolver.braw`: účinná Gen 5
  s originálem 4 → `bmd-film-gen5`, důvod `Blackmagic RAW: Film Gen 4 → Gen 5 (převod SDK)`. Volba v sekci Dekodéry:
  `[ ] Blackmagic RAW Film Gen 4 převést na Gen 5 (použije se LUT Gen 5)` + šedě `ověří až vzorek .braw Gen 4`; CLI `--braw-gen5`.
- **Neověřeno** (jako Windows): skutečný převod jen se vzorkem Gen 4 (`BrawSampleTests`, `OFFLOAD_BRAW_SAMPLE_GEN4`).

- [ ] **Step 1: Padající testy** — `BrawDecodeTests.testGen5IsAcceptedAndStrict` (sestavený dekodér: `--gen5` přijme, `--gen6` kód 2),
  `BrawBackendTests.testGen5PassedOnlyWithTheSetting` (falešný dekodér zapíše argumenty), `testInfoWithOriginalGeneration`
  (`colorScienceGenOriginal` → `ClipInfo`), `LutResolverTests.testBrawGen4ConvertedGetsGen5Lut`, `testBrawGen4NotConvertedNoLut`;
  se vzorkem Gen 4 (jinak `XCTSkip`): `--info --gen5` → `colorScienceGen` 5, `colorScienceGenOriginal` 4.
- [ ] **Step 2: Implementace** (C++ podle pravidel; `Tools/braw-decode/build.sh`), Swift napojení.
- [ ] **Step 3: Testy; záznam a commit** — FORMATY `colorScienceGenOriginal`, `proxy.brawGen4ToGen5` „Mac od 0.5.0“; ROZDILY řádek
  „BRAW Film Gen 4 → Gen 5“ ✅ (neověřeno); VYVOJ-MAC `- Úkol 58 — braw-decode --gen5 (klon atributů, Gen 5, colorScienceGenOriginal),
  proxy.brawGen4ToGen5 výchozí vypnuto, LUT Gen 5 po převodu; neověřeno bez vzorku Gen 4`.
  Run: `git add -A mac docs && git commit -m "Mac: braw-decode --gen5 for Film Gen 4"`

---

### Task 59: Oznámení — výsledek úlohy, oznámení macOS, ntfy na telefon

**Files:**
- Create: `mac/Sources/OffloadKit/Notify/JobOutcome.swift`, `Notify/NtfyClient.swift`, `Notify/NotifySettings.swift`
- Create: `mac/Sources/OffloadApp/Notifier.swift` (UNUserNotificationCenter), `mac/Sources/OffloadApp/NotifyView.swift` (dialog)
- Modify: `mac/Sources/OffloadApp/AppModel.swift` (konec fáze 1 a konec úlohy), `OffloadApp.swift` (Nástroje → „Oznámení…“), `mac/Sources/offload/main.swift` (`--ntfy`)
- Modify: `mac/scripts/build-app.sh` (`CFBundleIdentifier` v Info.plist — oznámení bez něj nefungují)
- Create: `mac/Tests/OffloadKitTests/JobOutcomeTests.swift`, `NtfyClientTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `JobStatus` (`safeToEject`, `safeToFormat`, `warning`, `note`, úkoly 39, 47), `OffloadReport`, `Texts`, `plural`.
- Produces:
  - `public enum OutcomeKind { done, attention, error, ejectable }`, `public struct JobOutcome: Equatable { kind; title; message; phoneMessage; var priority: Int; var tags: [String]; var forPhone: Bool }`
  - `public enum JobOutcomes { static func final(card: String, status: JobStatus, report: OffloadReport?) -> JobOutcome?; static func copyDone(card: String, status: JobStatus, proxies: Bool) -> JobOutcome?; static func chime(_ s: JobStatus) -> Bool }`
  - `public struct NotifySettings: Codable, Equatable { mac = true; ntfy: NtfySettings }`, `public struct NtfySettings: Codable, Equatable { enabled = false; server = "https://ntfy.sh"; topic = "" ; static func validTopic(_:) -> Bool }`
  - `public enum NtfyClient { static func send(server: String, topic: String, outcome: JobOutcome, session: URLSession = .shared, timeout: TimeInterval = 10) async -> String? }` (nil = odesláno, jinak chyba)
  - `static func NtfyClient.parseURL(_ s: String) -> (server: String, topic: String)?` (CLI `--ntfy https://ntfy.sh/tema`)

Pravidla (Windows návrh §4.3, R19, R20; Windows `JobOutcome.cs` — texty doslova):

- `final` (null u zrušené úlohy): doplnění bez reportu (nic nového) → `Offload: <karta> hotovo` / `Nic nového — záloha je úplná.`
  (s varováním nebo bez „Lze formátovat“ → pozornost); chyba před kopií → `Offload: <karta> — CHYBA` / text chyby, telefon `Chyba před
  kopírováním — karta NENÍ zálohovaná.`; selhané soubory → `N soubor selhal|soubory selhaly|souborů selhalo — karta NENÍ zálohovaná.`;
  neověřeno → pozornost `Kopie je hotová, ale NEOVĚŘENÁ.`; `copy` = „Kopie je ověřená“ jen při `safeToFormat`, jinak „Kopie je hotová“;
  selhané proxy → `<copy>, ale N proxy selhala|selhaly|selhalo.`; selhání po kopii → `<copy>, ale zpracování po kopii selhalo.`;
  varování → `<copy>, ale: <první řádek varování>` (telefon `<copy>, ale vyžaduje pozornost — podrobnosti v aplikaci.`); bez
  „Lze formátovat“ → `Kopie je hotová, ale kartu zatím neformátujte — není celá ověřená.` (telefon `Kopie je hotová, ale není celá
  ověřená.`); hotovo → `Offload: <karta> hotovo` / `Ověřeno na N disku|discích — kartu lze formátovat.` + ` Proxy: N hotovo.`
  Titulky pozornosti `Offload: <karta> — vyžaduje pozornost`. Priorita ntfy: hotovo 3, pozornost 4, chyba 5; tagy
  `white_check_mark` / `warning` / `rotating_light`.
- `copyDone` (jen oznámení macOS, ne ntfy): po fázi 1, když `safeToEject` a úloha ještě běží — `Offload: <karta> — kartu lze vyjmout`
  / `Proxy se ještě renderují.` | `Klipy se ještě zpracovávají.` | `Ověřuje se s proxy — formátovat až po dokončení.` (bez „Lze formátovat“).
- **Oznámení macOS** (`UNUserNotificationCenter`, ne zastaralé `NSUserNotificationCenter`): jen v balíčku aplikace s
  `CFBundleIdentifier` (pod `swift run` ho nevolat — `UNUserNotificationCenter.current()` mimo balíček spadne); oprávnění
  `requestAuthorization([.alert, .sound])` při prvním zapnutí volby; jen když okno aplikace **není v popředí** (`NSApp.isActive ==
  false`) — jinak stačí dosavadní zvuk a `requestUserAttention`. Volba `notify.mac` (výchozí zapnuto; Windows klíč `notify.windows`
  se zachová beze změny jako neznámý). Klik na oznámení aktivuje okno. CLI oznámení macOS neposílá.
- **ntfy:** `POST <server>/` s JSON `{"topic","title","message","priority","tags"}` (UTF-8), `URLSession` s limitem 10 s, jeden pokus,
  chyba jen do logu, úloha nikdy nečeká. Text **jen název karty + výsledek + počty** (bez cest, jmen souborů, jména Macu).
  Nastavení `notify.ntfy` `enabled`/`server`/`topic` (téma: písmena bez diakritiky, číslice, `-`, `_`, nejvýš 64 znaků), bez tokenu.
  Dialog „Oznámení…“: `[x] Oznámení macOS`, `[ ] Posílat na telefon přes ntfy`, Server, Téma, „Poslat zkušební oznámení“ („Odesláno.“ /
  „Nepodařilo se: …“), poznámka `Téma volte nehádatelné — kdo ho zná, oznámení čte. Posílá se jen název karty a výsledek, žádné cesty.`
  CLI `--ntfy <url>` (server + téma z cesty; špatné `Msg.badNtfyUrl` — přidat do `Msg`, text Windows), nebo z `--project`. `User-Agent: Offload/<verze>`.

- [ ] **Step 1: Padající testy** — `JobOutcomeTests` (tabulka výše, zrušená = nil, `forPhone` false pro `ejectable`, žádný text
  ntfy neobsahuje `/` ani jméno počítače); `NtfyClientTests` s `URLProtocol` podtřídou (tvar JSON, UTF-8 čeština, priorita, tagy;
  server neodpovídá → chyba po limitu 0,2 s v testu; `parseURL`).
- [ ] **Step 2: Implementace** podle pravidel; `Notifier` v aplikaci (`UNUserNotificationCenterDelegate` pro klik).
- [ ] **Step 3: Ruční kontrola** — `scripts/build-app.sh`, spustit `Offload.app`, zapnout oznámení (macOS se zeptá), úloha s oknem
  v pozadí → oznámení „kartu lze vyjmout“ a „hotovo“; ntfy na telefon (aplikace ntfy, téma z dialogu). **Ověřit (D.27 ř. 6):**
  dostane ad-hoc podepsaná aplikace (bez Developer ID) dotaz na oprávnění a doručí oznámení? Výsledek do commitu.
- [ ] **Step 4: Záznam a commit** — FORMATY `settings.json`: `notify` doplnit „Mac: `mac` (bool, `true`) místo `windows`; `windows` se
  na Macu zachová beze změny“; ROZDILY „Oznámení“ ✅ a záměrný rozdíl upravit; VYVOJ-MAC `- Úkol 59 — výsledek úlohy (texty Windows),
  oznámení macOS (UNUserNotificationCenter, jen okno v pozadí), ntfy JSON POST bez cest, --ntfy`.
  Run: `git add -A mac docs && git commit -m "Mac: job outcome notifications and ntfy"`

---

### Task 60: Kontrola aktualizací, podpis a notarizace, verze 0.5.0

**Files:**
- Create: `mac/Sources/OffloadKit/Updates/UpdateChecker.swift`
- Modify: `mac/Sources/OffloadApp/AppModel.swift`, `Views.swift`, `OffloadApp.swift` (odkaz „Nová verze“, Nástroje → „Aktualizace…“)
- Modify: `mac/scripts/build-app.sh` (`--sign`, `--notarize`, `--release-url`, verzovaný zip, `offload-update-mac.json`), `mac/Tools/braw-decode/build.sh`
- Create: `mac/scripts/entitlements/braw-decode.entitlements`, `mac/Tests/OffloadKitTests/UpdateCheckerTests.swift`
- Modify: `mac/Sources/OffloadKit/Report.swift` (`0.5.0`), `mac/README.md` (Vydání, Podepisování), `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Produces:
  - `public struct UpdateManifest: Codable { version: String; url: String; notes: String? }`, `public struct UpdateInfo { version; url; notes }`
  - `public enum UpdateChecker { static let interval: TimeInterval = 86_400; static func compare(_ a: String, _ b: String) -> Int; static func plain(_ v: String) -> String; static func check(source: String, current: String, now: Date, statePath: String, force: Bool) async -> (status: UpdateStatus, info: UpdateInfo?); static func canOpen(_ url: String) -> Bool }`
  - `AppSettings.updates.manifest: String?` (společné, ne v projektu)

Pravidla (FORMATY „settings.json“ `updates`, „Místní soubory“ `update-check.json`; Windows návrh §4.4–4.5, R21–R23):

- Manifest `{"version": "0.5.0", "url": "…", "notes": "…"}` (UTF-8); **Mac má vlastní soubor `offload-update-mac.json`** stejného
  tvaru (rozhodnutí spec D.24: verze a balíčky obou platforem se vydávají zvlášť); zdroj `updates.manifest` = http(s) adresa nebo
  cesta k souboru, `null` = vypnuto. Čte se nejvýš 64 KB, `version` nejvýš 32 znaků, `url` 1000, `notes` se zkrátí na 2000; řídicí
  a formátovací znaky ve verzi nebo adrese manifest vyřadí. Porovnání `MAJOR.MINOR.PATCH` číselně (`0.10.0 > 0.9.0`), přípona za
  `-` se ignoruje. Nejvýš 1× za 24 h, 5 s po startu, mimo hlavní vlákno; HTTP limit 10 s, soubor 5 s (síťový svazek může viset).
  Nikdy nic nestahuje ani nespouští; `User-Agent: Offload/<verze>`, žádné identifikátory.
- Stav `~/Library/Application Support/Offload/update-check.json` (`lastCheck`, `lastSource`, `lastVersion`, `lastUrl`, `lastNotes`):
  jiný zdroj nebo `lastCheck` v budoucnosti = číst hned; poškozený = číst znovu.
- Odkaz „Nová verze 0.5.0“ (tichý, vpravo) → okno s poznámkami a „Otevřít“: otevře jen `https://…` (`NSWorkspace.open`); místní
  složku nebo zip ukáže ve Finderu (`activateFileViewerSelecting`); **síťové cesty se nikdy neotevírají, jen zobrazí** (Mac: `smb://`,
  `afp://`, `nfs://`, `file://server/…`, cesta na svazku bez `MNT_LOCAL`). „Aktualizace…“: „Adresa manifestu (web, NAS nebo sdílená
  složka)“, „Zkontrolovat teď“, poslední výsledek `Máte nejnovější verzi 0.5.0.` / `Manifest nejde načíst: …`.
- **Podpis a notarizace (Mac místo `signtool`, spec D.24):** `scripts/build-app.sh --sign "Developer ID Application: …" [--notarize
  <profil notarytool v klíčence>] [--release-url <adresa>]`. Notarizace vyžaduje podpis **všech** spustitelných souborů v balíčku
  s hardened runtime — na rozdíl od Windows (R23: cizí binárky nepodepisujeme) se podepíše i přibalený ffmpeg/ffprobe:
  `codesign --force --options runtime --timestamp --sign "$ID"` pro `Contents/Resources/bin/*` (ffmpeg, ffprobe, braw-decode,
  offload), pak aplikace; `braw-decode` s entitlementem `com.apple.security.cs.disable-library-validation` (načítá
  `BlackmagicRawAPI.framework` podepsaný jiným vývojářem — **ověřit**, D.27 ř. 7); `codesign --verify --deep --strict`,
  `ditto -c -k --keepParent` → `build/Offload-mac-<verze>.zip`, `xcrun notarytool submit --wait --keychain-profile …`,
  `xcrun stapler staple build/Offload.app` a nový zip; `build/offload-update-mac.json` (verze, `url` = `--release-url`, `notes`).
  Chyby (build skončí): `codesign nenalezen …`, `Identita „…“ není v klíčence.`, `Notarizace selhala: <log>`. Bez `--sign` beze
  změny (ad-hoc podpis jako dosud). Nic se nekupuje — návod v README (Apple Developer Program ~99 USD/rok, bez něj Gatekeeper
  varuje: pravé tlačítko → Otevřít).

- [ ] **Step 1: Padající testy** — `UpdateCheckerTests`: porovnání verzí (tabulka vč. `0.10.0` vs `0.9.0`, přípony, nečíselné), poškozený
  a příliš velký manifest, řídicí znaky, 24 h s falešnými hodinami, hodiny posunuté zpět, zdroj soubor v dočasné složce, HTTP přes
  `URLProtocol`, časový limit, `canOpen` (https ano; `smb://`, `file://server/x`, `/Volumes/<síťový>` ne).
- [ ] **Step 2: Implementace**; `build-app.sh` rozšíření (bez `--sign` výstup beze změny).
- [ ] **Step 3: Verze 0.5.0** (`VersionTests`, FORMATY `version`, ROZDILY), README „Vydání verze“ (1. zvýšit `OffloadVersion.string`,
  2. `scripts/build-app.sh --sign … --notarize … --release-url …`, 3. nahrát zip, pak manifest) a „Podepisování“.
- [ ] **Step 4: Ruční kontrola** — manifest na místním souboru se starší/novější verzí; s Developer ID (je-li): `spctl -a -vv build/Offload.app`
  → „accepted, source=Notarized Developer ID“; BRAW proxy z podepsané aplikace funguje (knihovna se načte).
- [ ] **Step 5: Commit** — FORMATY `updates` a `update-check.json` „Mac od 0.5.0 (vlastní manifest `offload-update-mac.json`)“;
  ROZDILY „Kontrola aktualizací“, „Podpis“ ✅ a záměrné rozdíly; VYVOJ-MAC `- Úkol 60 — kontrola aktualizací (vlastní manifest Macu,
  24 h, síťové cesty se neotevírají), build-app.sh --sign/--notarize (podepíše i ffmpeg — notarizace to vyžaduje), verze 0.5.0 (plán 8)`.
  Run: `git add -A mac docs && git commit -m "Mac 0.5.0: update check, signing and notarization, docs after plan 8"`

---

## Plán 9 pro Mac — pozastavení, oprava, nový vzhled (úkoly 61–64, verze 0.6.0)

### Task 61: Pozastavit / Pokračovat

**Files:**
- Create: `mac/Sources/OffloadKit/PauseGate.swift`
- Modify: `mac/Sources/OffloadKit/Engine.swift` (brána před každým blokem čtení karty a zpětného čtení, před každým souborem; identita karty po pauze), `Job.swift` (brána mezi klipy fáze 2, `pause()`, `resume()`, `JobStatus.paused`, `pauseHeld`), `Report.swift` (`copyPausedSeconds`, `verifyPausedSeconds` z brány)
- Modify: `mac/Sources/OffloadKit/Desktop/JobProgress.swift` (texty, rychlosti stojí), `mac/Sources/OffloadApp/AppModel.swift`, `Views.swift` (tlačítko na kartě úlohy)
- Create: `mac/Tests/OffloadKitTests/PauseTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `CardIdentity.of`, `CardVolume` (úkoly 9, 36), `Msg.cardChangedWhilePaused` (úkol 34), `JobProgress` (úkol 8).
- Produces:
  - `public final class PauseGate { init(clock: @escaping () -> Date = Date.init); func pause(); func resume(); var isPaused: Bool; var isHeld: Bool; var holds: Int; var pausedTotal: TimeInterval; func waitWhilePaused(isCancelled: () -> Bool) -> Bool; func end() }`
  - `OffloadEngine.pauses: PauseGate`, `OffloadJob.pause()`, `resume()`; `JobStatus.paused`, `pauseHeld`
  - chyba souboru `card changed while paused` (JSON, anglicky), HTML „karta se během pozastavení vyměnila nebo vytáhla“ (úkol 55)

Pravidla (FORMATY `copyPausedSeconds`/`verifyPausedSeconds`, `sourceError` `card changed while paused`; Windows `PauseGate`, plán 9 ruling I1):

- Pracovníci úlohy se zastaví na nejbližším **bezpečném místě**: před dalším blokem (16 MiB) čtení karty nebo zpětného čtení,
  před dalším souborem, před dalším klipem fáze 2. **Nikdy uprostřed bloku, reportu, denního reportu ani běhu ffmpeg** — žádný
  proces se neuspává (`SIGSTOP` ne), takže **čekající pracovník nedrží zámek** historie, dne ani MHL. Zámek složek doplnění
  a opravy (úkol 47, 62) zůstává držený celou fázi 1, pozastavenou nebo ne. Jinak úloha nic nedrží.
- Zrušení během pauzy: brána vrátí `false`, úloha skončí jako zrušená hned (rozpracované `.offload-part` se smažou).
- **Identita karty po pauze:** když brána od posledního souboru opravdu držela (`holds` se změnilo), před dalším čtením z karty
  se karta znovu pozná (`CardIdentity.of` + `fsSerial`/kapacita stejné jako na začátku úlohy); jiná nebo žádná → zbylé soubory se
  nekopírují, každý dostane `sourceError` `card changed while paused` a úloha varování `Msg.cardChangedWhilePaused`.
- Report: `copyPausedSeconds`, `verifyPausedSeconds` (od Pozastavit do Pokračovat, zaokrouhleno na 0,1 s; chybí, když se
  nepozastavovalo); rychlosti a doby v TXT/HTML/PDF je odečítají (úkol 33), TXT řádek `Paused:`.
- Karta úlohy: tlačítko **Pozastavit** / **Pokračovat** (jen běžící úloha; CLI pauzu nemá); stav `Pozastavuje se…`, `Pozastaví se
  po klipu <klip>…`, `Pozastaví se po proxy klipu <klip>…`, po zastavení `Pozastaveno — Pokračovat spustí úlohu od místa, kde stojí.`;
  rychlosti a odhad času stojí (po Pokračovat se měří znovu). Pozastavená úloha drží frontu (další karta nezačne). Mac během
  offloadu nespí (`beginActivity` jako dosud) — i během pauzy.

- [ ] **Step 1: Padající testy** — `PauseTests.swift`: brána (pauza → `waitWhilePaused` čeká, `resume` pustí, `holds` +1, čas pauzy
  s falešnými hodinami, zrušení během pauzy vrátí `false`, `end` uzavře pauzu); engine: pauza v půlce kopie dvou souborů, pak
  Pokračovat → oba soubory ověřené a `copyPausedSeconds` ≥ doba pauzy; zrušení během pauzy → `.cancelled`, žádný `.offload-part`;
  `testCardSwappedWhilePausedStopsRemainingFiles` (falešná identita karty přes testovací háček `OffloadOptions.cardIdentity` —
  po pauze vrátí jinou → zbylé soubory `card changed while paused`, první soubor dobrý); `testPausedJobHoldsNoDayLock` (pauza ve
  fázi 2 mezi klipy → `FileLock.tryLock(Locks.day(…))` uspěje); `testPausedAppendKeepsTheFolderLock`.
- [ ] **Step 2: Implementace** — `PauseGate` (`NSCondition`; `waitWhilePaused` se probouzí každých 100 ms kvůli zrušení),
  napojení v `run` (smyčka bloků: `guard pauses.waitWhilePaused(isCancelled:) else { failure = "cancelled"; break }`; před souborem
  kontrola `holds` a identity), `DestWriter.verify` před blokem, fáze 2 před klipem.
- [ ] **Step 3: Testy; ruční kontrola** — Pozastavit během kopie velké karty → rychlosti 0, disk nečte (Monitor činnosti);
  vytáhnout kartu, vložit jinou, Pokračovat → zbylé soubory selžou s českou poznámkou, „Doplnit“ s původní kartou je dokončí.
- [ ] **Step 4: Záznam a commit** — FORMATY `copyPausedSeconds`, `verifyPausedSeconds` „Mac od 0.6.0“ (už ne „Mac ignoruje“);
  ROZDILY „Pozastavit / Pokračovat“ ✅; VYVOJ-MAC `- Úkol 61 — Pozastavit: brána mezi bloky, soubory a klipy (nic se neuspává,
  zámky dne a historie se nedrží), identita karty po pauze, doba pauzy v reportu`.
  Run: `git add -A mac docs && git commit -m "Mac: pause and resume a job"`

---

### Task 62: „Zkopírovat znovu“ — oprava jako generace ASC MHL N

**Files:**
- Create: `mac/Sources/OffloadKit/Append/RepairPlanner.swift`, `mac/Sources/OffloadKit/Desktop/RepairRules.swift`
- Modify: `mac/Sources/OffloadKit/Engine.swift` (režim `.repair`: zápis jen tam, kde se má, odložení nesouhlasné kopie, čtení dobré kopie, `rewritten`, `setAside`), `Job.swift` (úloha opravy, `BackupState`, historie `kind: "repair"`)
- Modify: `mac/Sources/OffloadApp/AppModel.swift`, `Views.swift` (tlačítko „Zkopírovat znovu“ na kartě úlohy, seznam nesouhlasných souborů po cílech)
- Create: `mac/Tests/OffloadKitTests/RepairPlannerTests.swift`, `RepairEngineTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `AppendPlanner.checkBackup` (úkol 46), `CardRecord`, `BackupState` (úkol 38), `MHL.entries` (akce `verified`, úkol 40), `OffloadReport.write` (úkol 41), `FolderLocks` (úkol 37), `hashFile` (úkol 32), `ProxyCopy.renameNoReplace`, `Msg` opravy (úkol 34), `PauseGate` (úkol 61).
- Produces:
  - `public struct RepairPlan { source; cardPaths; destRoots; date; cardName; generation; files: [SourceFile]; dirs; write: [[Bool]]; expected: [FileHashes]; setAside: [[String?]]; readBackBefore: [[Bool]]; hashAlgorithms; notInBackup: Int }`
  - `public enum RepairPlanner { static func plan(_ source: String, cardPaths: [String], sourceFilesystem: String? = nil, isCancelled: () -> Bool) throws -> RepairPlan; static func setAsidePath(generation: Int, relPath: String, attempt: Int = 1) -> String }`
  - `public enum RepairRules { static func offer(phase:report:cardPresent:repairPending:repairedLater:) -> Bool; static func title(_ card: String) -> String }`
  - `OffloadPlan.from(_ r: RepairPlan)`; `JobConfig.repairInto: [String]?`, `JobConfig.repairCardIdentity: String?`

Pravidla (FORMATY „Oprava — Zkopírovat znovu“; Windows `RepairPlanner`, rulings C1, M7, M16, I1; spec D.22.3):

- Nabízí se (jen GUI, CLI opravu nemá) u úlohy, která skončila (ne zrušená) se selhanými soubory, když je **ta karta** ve čtečce
  (identita z úlohy; složka je „ve čtečce“ vždy), žádná oprava téže zálohy nečeká ani neběží a žádná pozdější už neuspěla.
  Karta úlohy opravy `A001 · oprava`. Jiná karta ve čtečce → odmítnutí bez zápisu `Msg.repairOtherCard`. Oprava **vždy ověřuje**
  (bez ověření `Msg.repairNeedsVerify`).
- Kontroly zálohy jako u doplnění (`checkBackup`: složky, reporty, řetěz, stejný počet generací — s textem `Msg.forRepair`, tj.
  „opravit nejde“); záloha s nedokončenou generací (`verifyPending`/`verifyIncomplete`, úkol 69) → odmítnutí
  `Záloha nebyla dokončena (aplikace skončila během kopie nebo ověření) — dokončete ji nejdřív volbou „Dokončit přerušenou zálohu“
  u karty (offload append).` Generace N = max + 1 na **všech** cílech; zámek složek celou fázi 1.
- Co se dělá, se čte z reportů na cílech (nejnovější záznam cesty): kopie, která selhala → zkopírovat znovu **jen tam**; dobrá kopie,
  jejíž záznam nebyl přečten zpět (`!readBack` — zrušené ověření, kopie bez ověření) → **jen přečíst** (karta se pro ni nepotřebuje).
  Dobrá kopie se **nikdy nepřepíše**; dobrá kopie, která na disku chybí → odmítnutí (`Msg.appendMissingFile`, „opravit nejde“).
  Nic k opravě → úloha Hotovo `Msg.repairNothing` (soubory na kartě mimo zálohu ji přesto drží od „Lze formátovat“).
- **Zaznamenaný hash se nikdy nemění** (C1): očekávaný hash = nejnovější generace, která **není oprava** (celá kopie nebo doplnění;
  z opravy jen pro soubor, který žádná jiná generace nehashovala). Kopírovaný soubor musí na kartě mít velikost a čas jako v záloze
  a **karta se přečte a porovná před jakýmkoli zápisem**; jiný obsah → celá oprava odmítnuta bez zápisu, nový hash jen v hlášce
  `Msg.repairCardDiffers(path, "xxh64", now:, recorded:)`. Změní-li se karta až mezi plánem a kopií: nová kopie se zahodí, nesouhlasná
  se nepřesune, soubor zůstane `failed` se `sourceError` `card differs from the backup: xxh64 <nový> != recorded <zaznamenaný>`;
  v reportu, MHL i seznamech zůstává zaznamenaný hash.
- **Nesouhlasná kopie se nikdy nesmaže ani nepřepíše:** před novou se přesune do `_REPORT/neshoda_NNNN/<cesta>` (`rename` ve stejném
  svazku; složky se vytvoří); obsazené místo → první volné `neshoda_NNNN_2`, `_3`, … (M7); obsadí-li se až po plánu, soubor na tom cíli
  selže s `set aside: <důvod>` a nic se nepřepíše.
- Generace MHL N: nová kopie `original`, přečtená dobrá `verified` (`hashdate` = čas opravy), komentář `Msg.mhlRepairComment`.
  Report `offload_report_NNNN.*` s `kind: "repair"`, `files[].rewritten` (pro každý cíl `true` = nová kopie), `setAside`
  (`destination`, `path`, `movedTo`); `<karta>.xxh64` (a `.md5`) bez opakovaných řádků (úkol 41).
- „Lze formátovat“ po opravě = celá záloha (`BackupState`: 0 selhaných, 0 neověřených) a žádný soubor na kartě mimo zálohu
  (`Msg.cardFilesNotInBackup`); jinak varování. Historie: generace `kind: "repair"`, `success`/`failedFiles` znovu z reportů na cílech.
  Oprava proxy nerenderuje.

- [ ] **Step 1: Padající testy** — `RepairPlannerTests` (nad zálohou z engine se zfalšovaným `failed` stavem v reportu a poškozenou
  kopií na disku): jen selhané soubory jen na cílech, kde selhaly; dobrá neověřená kopie jen čtená; `testCardChangedRefusedNothingWritten`
  (karta má jiný obsah → `repairConflict`, na cílech nic nového, žádné `neshoda_`); soubor už není na kartě → konflikt; nedokončená
  záloha odmítnuta; nic k opravě → `repairNothing` s `notInBackup`; `setAsidePath(generation: 3, relPath: "CLIP/a.mov", attempt: 2)`
  → `_REPORT/neshoda_0003_2/CLIP/a.mov`. `RepairEngineTests`: `testGoodCopyNeverWritten` (bajty a čas dobré kopie beze změny,
  v MHL `action="verified"`), `testBadCopySetAsideNeverDeleted` (původní vadná kopie je v `_REPORT/neshoda_0002/…` bajtově stejná,
  report `setAside`), `testCardDiffersWhenReadFileStaysFailed`, `testCancelledRepairGoodCopyStaysGoodNextRepairFinishes`,
  `testTakenSetAsidePlaceGetsNextFreeName`.
- [ ] **Step 2: Implementace** — `RepairPlanner` podle pravidel (plánovač jen čte; čtení karty pro porovnání hashe je součást plánu),
  engine režim `.repair`: `DestWriter` dostane masku `write[i][d]`, cestu odložení a `readBackBefore`; pořadí na cíli pro soubor
  s `write`: přesun nesouhlasné kopie do `neshoda_…` (`renameNoReplace`; selže → `failed` `set aside: …`) → zápis `.offload-part`
  → přejmenování bez nahrazení; bez `write` jen zpětné čtení a porovnání s očekávaným hashem. Report s `rewritten` a `setAside`.
- [ ] **Step 3: Testy; ruční kontrola** — kopie na 2 disky, na jednom disku vadu (přepsat bajt `dd conv=notrunc`), Ověřit zálohu →
  nesedí, karta úlohy „Zkopírovat znovu“ → oprava, `ls _REPORT/neshoda_0002/…`, `ascmhl verify` projde, Ověřit zálohu → vše sedí.
- [ ] **Step 4: Záznam a commit** — FORMATY „Oprava (Windows od 0.5.0, Mac od 0.6.0)“, `kind`, `rewritten`, `setAside` už ne „Mac
  ignoruje“; ROZDILY „Zkopírovat znovu“ ✅, záměrný rozdíl přepsat; VYVOJ-MAC `- Úkol 62 — Zkopírovat znovu: jen selhané soubory
  jen tam, kde selhaly, karta porovnaná před zápisem, zaznamenaný hash se nemění, nesouhlasná kopie do _REPORT/neshoda_NNNN (nikdy
  smazat), dobrá kopie jen přečtená (verified v MHL), kind repair, celá záloha rozhoduje o Lze formátovat`.
  Run: `git add -A mac docs && git commit -m "Mac: repair (Zkopírovat znovu) as ASC MHL generation N"`

---

### Task 63: Nový vzhled I — téma, horní lišta, kroky 1–3, stránka Nastavení

> **Brána:** README `docs/ui-navrh/` a rozhodnutí plánu 9 říkají „Mac převezme **až po schválení Windows verze**“. Než úkol začne,
> zeptat se uživatele, jestli je Windows 0.5+ vzhled schválený. **Neschválený → úkoly 63–64 přeskočit** (vše z plánů 6–10 má
> funkční ovládání ve stávajícím vzhledu z úkolů 49, 56–62), v úkolu 64 udělat jen krok „Verze a dokumentace“ a pokračovat úkolem 65.

**Files:**
- Create: `mac/Sources/OffloadApp/Theme.swift`, `TopBar.swift`, `SetupColumn.swift` (kroky 1–3 a připnutá spodní lišta), `SettingsPage.swift`
- Modify: `mac/Sources/OffloadApp/Views.swift` (rozvržení dvou sloupců), `OffloadApp.swift` (menu → horní lišta; zkratky zůstávají)
- Create: `mac/Sources/OffloadKit/Desktop/SetupTexts.swift` (texty a pravidla kroků, testovatelné), `mac/Tests/OffloadKitTests/SetupTextsTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

Pravidla (`docs/ui-navrh/README.md` — zadání; přesné barvy, rozměry a texty z `docs/ui-navrh/mockup/*.html`; rozhodnutí plánu 9):

- **Téma:** tokeny z tabulky „Design tokeny“ (`Bg #0E1012`, `Panel #14171A`, `Raised #1B1F23`, `Accent #2366D1`, `Success #42D392`,
  `Warn #F2B04A`, `Danger #FF7D6B` …) jako `Color` v `Theme.swift`; **tmavé i světlé podle systému** (`@Environment(\.colorScheme)`;
  světlá sada odvozená, kontrast textu min. 4,5 : 1 — test spočítá kontrast každé dvojice text/pozadí z obou sad). Písmo systémové
  SF Pro + SF Mono (IBM Plex se nepřibaluje). Barvy stavů se liší i světlostí. Velikosti: základ 14, popisky 13, drobné 12, nadpisy sekcí
  12 SemiBold VERSALKY; zaoblení dlaždic 12, karet ve frontě 16, tlačítek 8, primárního 10; levý sloupec 380–460 px; okno min 940×640.
- **Horní lišta:** logo + „Offload“ · přepínač **Projekt** (`Projekt  Film X ▾`, nikdy „Preset“) · `Ověřit zálohu` (⇧⌘O) · `Hardware`
  se stavovou tečkou (zelená = bez varování, žlutá = doporučení) · Nastavení (⌘,); při běhu pilulka `● Běží 1 offload · 1 ve frontě`.
  Menu Nástroje zaniká (položky přejdou sem a do Nastavení), zkratky ⌘↩, ⌘U, ⇧⌘O zůstávají.
- **Levý sloupec, kroky:** `1 · Karty` (vybraná karta: název, štítek kamery `ALEXA 35 · LogC4` — úkol 64, řádek `24 klipů · 1,70 GB · exFAT ·
  čtečka USB 3.2` (Mac pozná i `Thunderbolt`), checkbox, pole Název složky s nápovědou; zálohovaná karta: žlutý řádek historie
  a tlačítko **Doplnit**; prázdný stav `Vložte kartu do čtečky — objeví se tady automaticky.`; odkaz `Ze složky…`), `2 · Cíle`
  (dlaždice disku: číslo, název, `7,20 TB volných z 8 TB · APFS · Thunderbolt`, ✕; **pruh místa**: šedá obsazeno, modrá tahle
  karta (vč. proxy a reportu z úkolu 48), žlutý/červený podle hlášek; náhled cesty `…/2026-10-07/A035_ARRI/`; jeden cíl → žlutě
  `Jen jeden cíl — doporučeny jsou dvě nezávislé kopie.`), `3 · Po zkopírování` (4 řádky: Ověření `xxHash64`, Analýza `3 snímky / klip`,
  Proxy `1080p · H.264 · LogC4 → 709`, Report `PDF · HTML · ASC MHL`; vypnuté šedě „vypnuto“; vypnuté ověření žlutě `Bez ověření není
  jisté, že data na disku jsou v pořádku.`; podle Windows 0.5.2 jsou značky Ověření/Analýza/Proxy **přepínače** aktivního projektu
  a klik na řádek otevře příslušnou sekci Nastavení; Report má pevnou značku „Report se vytváří vždy“). **Spodní lišta připnutá:**
  `1 karta → 2 disky · 1,70 GB` + odhad, `▶ Spustit offload ⌘↩`, nad ním `startProblem` (neaktivní tlačítko).
- **Stránka Nastavení** (místo hlavního obsahu): `‹ Zpět` · název projektu · `Uložit jako projekt…` · `Hotovo`; sekce Struktura složek,
  Ověření (`Ověřit kopie zpětným čtením (…)`, kontrolní součet; úkol 70 přidá „Ověřit primární disk během proxy“), Analýza a náhledy,
  **Proxy** (přepínač, kodek a výška jako segmenty, tok `− 10 Mb/s +` s odhadem MB/min, LUT podle kamery — tabulka vestavěných
  s použitým zvýrazněným, Vlastní .cube…, Když kameru nepoznám, Pravidla LUTů…, zvuk, proxy kamery, kopírovat na všechny cíle,
  složka pro proxy, Bez proxy když je originál lehčí; vpravo **šedý rám** burn-inu s frame lines, názvem klipu a TC — skutečný snímek
  neschválen), Report (denní report, CSV, ALE), Dekodéry RAW (+ BRAW Gen 4 → Gen 5), Oznámení, Aktualizace.

- [ ] **Step 1:** `SetupTextsTests` — texty a pravidla kroků bez UI (souhrn `1 karta → 2 disky · 1,70 GB`, `Texts.bytes3` tři
  platné číslice „1,70 GB“, „14,2 GB“, „412 GB“; řádky kroku 3 podle nastavení; jeden cíl; kontrast obou sad tokenů ≥ 4,5).
- [ ] **Step 2:** SwiftUI podle pravidel; nic nečte disk na hlavním vlákně (vše z modelů úkolů 10, 43, 48).
- [ ] **Step 3: Ruční kontrola** proti `docs/ui-navrh/screeny/01-pripraveno.png` a `04-nastaveni-proxy.png` (okno 1360×900 a 940×640,
  tmavý i světlý režim systému); snímky obrazovky do `mac/docs/ui/` a cestu do VYVOJ-MAC.
- [ ] **Step 4: Commit** — VYVOJ-MAC `- Úkol 63 — nový vzhled I: téma (tmavé i světlé podle systému), horní lišta s přepínačem
  projektů, kroky 1–3 s připnutým Spustit, stránka Nastavení`. Run: `git add -A mac docs && git commit -m "Mac: new look I — theme, top bar, steps, settings page"`

---

### Task 64: Nový vzhled II — fronta po stavech, Dnes a historie karet, štítek kamery, odhad; verze 0.6.0

**Files:**
- Create: `mac/Sources/OffloadApp/QueueColumn.swift` (karty úloh), `TodayView.swift`
- Create: `mac/Sources/OffloadKit/Desktop/JobCard.swift` (druh karty, štítky, fáze, souhrn po polích, seznam nesouhlasných souborů — testovatelné), `Desktop/TimeEstimate.swift`, `Desktop/CardFacts.swift` (štítek kamery a připojení)
- Create: `mac/Tests/OffloadKitTests/JobCardTests.swift`, `TimeEstimateTests.swift`, `CardFactsTests.swift`
- Modify: `mac/Sources/OffloadKit/Report.swift` (`0.6.0`), `mac/README.md`, `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`

Pravidla (`docs/ui-navrh/README.md` „Pravý sloupec — fronta“, „Nové věci“ 1–4, 6, 7, 9, 10; Windows `JobCard.cs`):

- Záložky `Fronta` / `Historie karet`. Prázdná fronta: pět ikon (Karta → 2 disky naráz → Ověření → Proxy → Report), `Připraveno —
  A035_ARRI na 2 disky`; bez karty `Zatím žádný offload`. **Dnes:** tabulka dnešních karet z `history.json` (název, velikost, čas,
  `✓ 2 kopie ověřeny` — „ověřeny“ jen při `verified` záznamu) a souhrn `3 karty · 412 GB · vše ověřeno`.
- **Karta běží:** hlavička (název, `24 klipů · 1,70 GB · ALEXA 35`, velké `38 %`, `zbývá ~0:02 kopie · ~0:06 celkem`), **ukazatel fází**
  (Kopírování · Ověření · Proxy · Report; s ověřením během proxy z úkolu 67 pořadí Kopírování · Proxy · Ověření · Report; vypnuté fáze
  se nezobrazují), **tabulka rychlostí** (Čtení karty + řádek na cíl: název, pruh, MB/s — strukturovaná data z `JobProgress`/`ChainView`),
  `Soubor 10 / 24` + aktuální soubor, `Pozastavit` (úkol 61), `Zrušit` (červený obrys), pod kartou šedě `Nejpomalejší článek: …` (brzda).
- **Čeká:** kompaktní řádek (+ „— doplnění“ / „· oprava“), pilulka `Čeká ve frontě`.
- **Hotovo (zelená):** štít, `A035_ARRI — kartu lze formátovat`, dva štítky (úkol 39), tlačítko `Vysunout kartu`; čtyři čísla Kopie
  (MB/s) · Ověření (MB/s) · Proxy (x / y) · Celkem; náhledy prvních 5 klipů + „+18“; `Report PDF`, `Otevřít složku` (Finder), `Proxy`.
- **Nesouhlasí (žlutá):** `B012_FX6 — kartu zatím neformátujte`, popis `1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše
  v pořádku.`, **Zkopírovat znovu** (úkol 62), seznam souborů `cesta · RAID_A ✓ · SHUTTLE_B ✕ hash`. Tvrdá chyba červeně, zrušeno šedě.
- **Štítek kamery a připojení** (nová věc 6): z rychlé prohlídky karty — ffprobe **jednoho** klipu (nejvýš 5 s, mimo hlavní vlákno)
  → `ALEXA 35 · LogC4` (model a gama/LUT); připojení z `DriveLinks.resolve` (úkol 14) → `čtečka USB 3.2`, **`Thunderbolt`** (Mac
  ho pozná, Windows ne — ROZDILY).
- **Odhad času** (nová věc 10): jen z rychlostí naměřených **v této relaci** (žádné uložené odhady); bez měření jen velikost.

- [ ] **Step 1:** `JobCardTests` (druh karty a nadpis podle stavu, štítky, kroky fází, souhrn po polích, seznam nesouhlasných souborů
  po cílech z reportu), `TimeEstimateTests`, `CardFactsTests` (štítek z `ClipInfo` a `LutUse`, typ připojení z `DriveLink`).
- [ ] **Step 2:** SwiftUI; ruční kontrola proti `screeny/02-probiha-offload.png`, `03-hotovo-a-chyba.png`.
- [ ] **Step 3: Verze a dokumentace** — `0.6.0` (`VersionTests`), FORMATY `version` „`0.6.0` (Mac)“; ROZDILY: „Nový vzhled“ ✅ (nebo
  „odloženo — Windows vzhled neschválen“, když se 63–64 přeskočily), „Dnes a historie karet, štítek kamery a připojení, odhad času“,
  verze; README (GUI). VYVOJ-MAC `- Úkol 64 — nový vzhled II: fronta po stavech, Dnes a historie karet, štítek kamery a připojení
  (Thunderbolt), odhad času z této relace; verze 0.6.0 (plán 9)`.
  Run: `git add -A mac docs && git commit -m "Mac 0.6.0: new look II — queue cards, today, camera label; docs after plan 9"`

---

## Plán 10 pro Mac — Verify-in-Proxy (úkoly 65–70, verze 0.7.0)

Zdroj: specifikace uživatele „Offload — Verify-in-Proxy: technická specifikace“ (Claude Docs) a její převzetí ve Windows návrhu
[`2026-10-08-offload-verify-in-proxy-design.md`](../../../../windows/docs/superpowers/specs/2026-10-08-offload-verify-in-proxy-design.md)
(R1–R12). **Hlavní rozdíl macOS** (specifikace, tabulka „Platforma“ a otevřená otázka): `F_NOCACHE` je jen **nápověda**, veřejné API
pro vyhození souboru z cache neexistuje (záložně `msync(MS_INVALIDATE)` na mapování souboru) a test musí dokázat, že druhé čtení
jde opravdu z disku. Mac proto „ověřeno“ z čtení pro proxy uzná jen s **důkazem z `mincore`** (spec D.23.1) — jinak klasické
zpětné čtení s poznámkou.

### Task 65: Zápis pro ověření během proxy — kontrola cache na macOS, blokový manifest, sériové číslo cíle

**Files:**
- Create: `mac/Sources/OffloadKit/VerifyInProxy/CacheProbe.swift`, `VerifyInProxy/Manifest.swift`, `VerifyInProxy/Msg+VerifyInProxy.swift`
- Modify: `mac/Sources/OffloadKit/Engine.swift` (`DestWriter`: kontrolní body po 64 blocích, záznam `file` po přejmenování; `OffloadOptions.checkpointBlocks = 64`; report `volumeSerial`)
- Create: `mac/Tests/OffloadKitTests/CacheProbeTests.swift`, `ManifestTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/FORMATY.md`

**Interfaces:**
- Consumes: `IO.openWrite` (`F_NOCACHE`), `IO.fullSync`, `SourceHasher.blocks` (úkol 51), `VolumeFacts.volumeSerialText`, `isLocal` (úkol 36), `OffloadPlan.generation` (úkol 47).
- Produces:
  - `public enum CacheProbe { static func residentPages(_ path: String) -> (resident: Int, total: Int)?; static func evict(_ path: String) -> Bool; static func blind() -> Bool; static func problem(cardPath: String) -> String? }`
  - `public struct ManifestRecord: Codable, Equatable { v = 1; kind ("file"|"part"); gen; path; size; modified: Date; xxh64?; xxh128?; md5?; blockSize; first (jen part, vynechá se při 0); blocks: [String] }`
  - `public struct PartCheckpoint: Equatable { path; size; modified; blockSize; blocks: [UInt64] }`, `public struct BlockManifest: Equatable { size: Int64; blockSize: Int; blocks: [UInt64]; count; offset(_:); length(_:) }`
  - `public enum ManifestFile { static let fileName = "offload-manifest.jsonl"; static func path(in cardPath: String) -> String; static func append(_ cardPath: String, _ records: [ManifestRecord]) throws; static func read(_ cardPath: String) -> [ManifestRecord]; static func files(_ records: [ManifestRecord], generation: Int) -> [String: ManifestRecord]; static func parts(_ records: [ManifestRecord], generation: Int) -> [String: PartCheckpoint] }`
  - `enum VIPMsg` — české poznámky (níže)

Pravidla (FORMATY „Blokový manifest“; Windows R4, R6, R8, R12; specifikace „Platforma“; spec D.23.1):

- **Zápis bez cache už ve fázi 1** (Mac to dělá od 0.1.0: `F_NOCACHE` na zapisovacím deskriptoru) — hlavní pravidlo specifikace;
  vyhazování z cache je jen pojistka. Mac navíc: **kontrolní bod `part` jen po `F_FULLFSYNC`** zapisovaného souboru (`F_NOCACHE`
  nezaručuje, že data jsou na médiu; Windows tu má `WRITE_THROUGH`). Cena: jeden `F_FULLFSYNC` na 1 GiB.
- **Kontrola cache cíle** (`problem(cardPath:)`, jednou na relaci a cíl; Windows `UnbufferedCheck`): síťový svazek (`!isLocal`)
  → `síťový cíl — ověřeno zpětným čtením`; sonda `.offload-cachecheck-<pid>-<uuid>` v `<karta>/_REPORT` (4 MiB náhodných dat,
  `F_NOCACHE`, `F_FULLFSYNC`, zavřít) → `residentPages(sonda).resident == 0`, jinak `kontrola čtení mimo cache neprošla — ověřeno
  zpětným čtením`; když sonda nejde zapsat/změřit `kontrolu čtení mimo cache nejde provést (<detail>) — ověřeno zpětným čtením`;
  sonda se vždy smaže. **`blind()`** (jednou za běh aplikace): soubor zapsaný **normálně** (s cache) musí `mincore` ukázat jako
  rezidentní — když ne, `mincore` cache nevidí a důkaz neexistuje → verify-in-proxy se na tomto Macu nepoužije (vše klasicky,
  poznámka `macOS neumí ukázat, co je v cache — ověřeno zpětným čtením`).
- `residentPages`: `open` + `mmap(PROT_READ, MAP_SHARED)` celého souboru (po částech 1 GiB u velkých), `mincore`, počet stránek
  s `MINCORE_INCORE`; prázdný soubor `(0, 0)`; chyba `nil`. `evict`: `mmap` + `msync(MS_INVALIDATE)` + `munmap` (nejlepší snaha).
- **Manifest** `<karta>/_REPORT/offload-manifest.jsonl` na každém cíli: **jen připisovaný** (otevřít `O_WRONLY|O_APPEND|O_CREAT`;
  když poslední bajt není `\n`, napsat `\n` — přetržený řádek zůstane sám; řádky a `F_FULLFSYNC`); nic se v něm nepřepisuje;
  čtení přeskočí řádky, které nejdou přečíst nebo mají `v` ≠ 1 či prázdnou `path`. Klíče: `v`, `kind`, `gen`, `path`, `size`,
  `modified` (UTC na sekundy), `xxh64`, `xxh128`, `md5` (jen `file`, `md5` jen s MD5), `blockSize`, `first` (jen `part`, 0 se nepíše),
  `blocks` (XXH3-64 16 malých hex). Záznam `file` se zapíše, **až je soubor na svém místě** (po přejmenování; malé soubory po
  `flushDevice` celého cíle). `part` po každých 64 blocích (1 GiB při bloku 16 MiB) souboru zapisovaného mimo cache.
- `parts`: záznam s `first` 0 začíná nový pokus; navazující (`first` = dosavadní počet) ho prodlouží; mezera se ignoruje; změna
  velikosti, času nebo velikosti bloku začne znovu tím záznamem. `files`: nejnovější `file` záznam cesty dané generace (bez ohledu
  na velikost písmen).
- Report: každý cíl `volumeSerial` (`VolumeFacts.volumeSerialText`; úkol 36) — podle něj se cíl pozná i pod jiným jménem (R12).
- Poznámky (`VIPMsg`, česky, doslova Windows kromě slov „macOS“): `classicNetwork` „síťový cíl — ověřeno zpětným čtením“,
  `classicCacheCheck`, `classicCacheCheckFailed(_:)`, `classicSetting` „ověření během proxy je vypnuté — ověřeno zpětným čtením“,
  `classicSecondary` „další cíl — ověřeno čtením na pozadí“, `classicBlind` (výše), `verifiedThroughCache(disk, n)` „Ověřeno čtením
  přes cache macOS: <disk>, N souborů — soubor zůstal v paměti a nešel z ní vyhodit, data mohla přijít z paměti místo z disku.“,
  `unstableRead`, `sameBlockWrong`, `sameBlockWrongMore`, `cardNotReadAgain`, `cardGivesRecorded`, `cardGivesWhatDisksHave`,
  `cardGivesOther`, `verifyNotCommitted(_:)`, `historyDelayed`, `historyNotWritten`, `primaryNotPositive`, `primaryTooHigh`,
  `proxyKeptOnResume`, `resumeDiverged`, `repairUnfinished`, `cardFolderGone`, `contentWarnings(_:)` — texty z
  `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs`.

- [ ] **Step 1: Padající testy** — `CacheProbeTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class CacheProbeTests: XCTestCase {
    // Review focus 9: mincore must see cached pages, or nothing it says is proof (spec D.27).
    func testResidentPagesAreDetected() throws {
        let p = tempDir().sub("cached.bin")
        try Data(repeating: 7, count: 8 << 20).write(to: URL(fileURLWithPath: p))   // a normal, cached write
        let r = try XCTUnwrap(CacheProbe.residentPages(p))
        XCTAssertEqual(r.total, (8 << 20) / Int(getpagesize()))
        XCTAssertGreaterThan(r.resident, 0, "a normal write leaves pages in the cache — if 0, mincore is blind (spec D.27)")
        XCTAssertFalse(CacheProbe.blind())
    }

    func testWrittenWithNoCacheLeavesNoResidentPages() throws {
        let p = tempDir().sub("nocache.bin")
        let fd = try IO.openWrite(p)
        let buf = IO.allocateAligned(4 << 20)
        memset(buf, 9, 4 << 20)
        try IO.pwrite(fd, buf, 4 << 20, 0, path: p)
        try IO.fullSync(fd, path: p)
        close(fd); free(buf)
        XCTAssertEqual(CacheProbe.residentPages(p)?.resident, 0, "F_NOCACHE writes should not stay in the cache (verify on APFS, exFAT, external disks)")
    }

    func testProbeOnLocalDestinationPassesAndLeavesNothing() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try FileManager.default.createDirectory(atPath: card + "/_REPORT", withIntermediateDirectories: true)
        XCTAssertNil(CacheProbe.problem(cardPath: card))
        XCTAssertEqual(TestFS.files(in: card + "/_REPORT"), [])
    }
}
```

`ManifestTests.swift`: zápis dvou záznamů → dva řádky s `\n`; přetržený poslední řádek (`{"v":1,"ki`) → další záznam začne na novém
řádku a čtení přetržený přeskočí; `parts` skládá navazující kontrolní body, mezeru ignoruje, `first` 0 začne znovu; `files` vrátí
nejnovější; tvar JSON (`first` chybí u `file`, `md5` chybí bez MD5, data `…Z`); engine: kopie 3 bloků po 1 MiB s
`checkpointBlocks = 2` zapíše `part` (first 0, 2 bloky) a pak `file` se 3 bloky = bloky z hasheru; report má `volumeSerial`.

- [ ] **Step 2: Implementace** — `CacheProbe.swift` (jádro):

```swift
import Darwin
import Foundation

public enum CacheProbe {
    /// Pages of `path` in memory (mincore over a read-only shared mapping), or nil when it cannot be told.
    public static func residentPages(_ path: String) -> (resident: Int, total: Int)? {
        let fd = open(path, O_RDONLY)
        guard fd >= 0 else { return nil }
        defer { close(fd) }
        var st = stat()
        guard fstat(fd, &st) == 0 else { return nil }
        let size = Int(st.st_size), page = Int(getpagesize())
        if size == 0 { return (0, 0) }
        var resident = 0, total = 0, off = 0
        let window = 1 << 30
        while off < size {
            let len = min(window, size - off)
            guard let m = mmap(nil, len, PROT_READ, MAP_SHARED, fd, off_t(off)), m != MAP_FAILED else { return nil }
            defer { munmap(m, len) }
            let pages = (len + page - 1) / page
            var vec = [CChar](repeating: 0, count: pages)
            guard mincore(m, len, &vec) == 0 else { return nil }
            resident += vec.filter { Int32($0) & MINCORE_INCORE != 0 }.count
            total += pages
            off += len
        }
        return (resident, total)
    }

    /// Best effort: drop the file's cached pages (no public eviction API on macOS — spec D.23.1).
    @discardableResult
    public static func evict(_ path: String) -> Bool {
        let fd = open(path, O_RDONLY)
        guard fd >= 0 else { return false }
        defer { close(fd) }
        var st = stat()
        guard fstat(fd, &st) == 0, st.st_size > 0 else { return true }
        guard let m = mmap(nil, Int(st.st_size), PROT_READ, MAP_SHARED, fd, 0), m != MAP_FAILED else { return false }
        defer { munmap(m, Int(st.st_size)) }
        return msync(m, Int(st.st_size), MS_INVALIDATE) == 0
    }

    private static let blindOnce: Bool = {
        let p = NSTemporaryDirectory() + ".offload-mincore-\(getpid())"
        defer { unlink(p) }
        guard (try? Data(repeating: 1, count: 1 << 20).write(to: URL(fileURLWithPath: p))) != nil,
              let r = residentPages(p) else { return true }
        return r.resident == 0
    }()

    /// mincore does not see cached pages on this Mac: nothing it says is proof.
    public static func blind() -> Bool { blindOnce }

    /// Nil when the destination of `cardPath` may be verified from reads (the proxies' and the fill-in's); else the Czech
    /// reason it is read back the classic way.
    public static func problem(cardPath: String) -> String? {
        if blind() { return VIPMsg.classicBlind }
        if !VolumeFacts.isLocal(cardPath) { return VIPMsg.classicNetwork }
        let probe = cardPath + "/" + Layout.reportDirName + "/.offload-cachecheck-\(getpid())-\(UUID().uuidString)"
        defer { unlink(probe) }
        do {
            let fd = try IO.openWrite(probe)
            let n = 4 << 20
            let buf = IO.allocateAligned(n)
            defer { free(buf) }
            arc4random_buf(buf, n)
            do { try IO.pwrite(fd, buf, n, 0, path: probe); try IO.fullSync(fd, path: probe) } catch { close(fd); throw error }
            close(fd)
            guard let r = residentPages(probe) else { return VIPMsg.classicCacheCheckFailed("mincore") }
            return r.resident == 0 ? nil : VIPMsg.classicCacheCheck
        } catch {
            return VIPMsg.classicCacheCheckFailed("\(error)")
        }
    }
}
```

(Konstanta `MINCORE_INCORE` = 0x1 z `<sys/mman.h>`; kdyby se nepřeložila, použít `0x1`.) `Manifest.swift` podle pravidel
(`JSONEncoder` `.sortedKeys`, data `.iso8601`, jeden řádek; `append` drží zámek v procesu na cestu — `NSLock` ve slovníku — a
`F_FULLFSYNC` po zápisu). `DestWriter`: po každém bloku hash bloku z `SourceHasher` nestačí (hash je na jiné frontě) — `DestWriter`
si XXH3-64 bloku spočítá sám nad bufferem, který zapsal (`BlockHash.xxh3(c.buf, c.length)`), a po 64 blocích `IO.fullSync` +
`ManifestFile.append(.part)`; po přejmenování (velké soubory) nebo po `flushDevice` (malé) `ManifestFile.append(.file)` s hashi
souboru z hasheru (po `hasher.wait()` v `run`).

- [ ] **Step 3: Ověřit na Macu** (spec D.27 ř. 3–4; výsledky do commitu): `swift test --filter CacheProbeTests` na interním APFS,
  na externím USB SSD (APFS i exFAT) a, je-li po ruce, na Thunderbolt disku a na RAIDu —
  `OFFLOAD_TEST_DEST=/Volumes/X swift test --filter CacheProbeTests` (testy píšou do `OFFLOAD_TEST_DEST`, jinak do dočasné složky). Když `testWrittenWithNoCacheLeavesNoResidentPages`
  na některém FS selže (zápis `F_NOCACHE` přesto zůstal v cache), zapsat FS do D.27 — `problem` pro něj vrátí `classicCacheCheck`
  a ověření během proxy se tam nepoužije (správné chování, ne chyba).

- [ ] **Step 4: Testy; záznam a commit** — FORMATY „Blokový manifest (Windows od 0.6.0, Mac od 0.7.0)“ + věta „Mac: kontrolní bod
  `part` až po `F_FULLFSYNC` souboru“; VYVOJ-MAC `- Úkol 65 — kontrola cache na macOS (mincore; sonda zapsaná s F_NOCACHE nesmí
  zůstat v paměti; slepé mincore = vše klasicky), blokový manifest jen připisovaný (part po F_FULLFSYNC, file po přejmenování),
  volumeSerial cílů; ověřeno na: <FS a disky>`.
  Run: `git add -A mac docs && git commit -m "Mac: cache residency probe, block manifest, destination serials"`

---

### Task 66: Ověřovač bloků a dočtení

**Files:**
- Create: `mac/Sources/OffloadKit/VerifyInProxy/BlockVerifier.swift`, `VerifyInProxy/BlockReads.swift`
- Create: `mac/Tests/OffloadKitTests/BlockVerifierTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `BlockManifest` (úkol 65), `BlockHash` (úkol 32), `IO.openRead`, `IO.pread`, `CacheProbe` (úkol 65), `PauseGate` (úkol 61).
- Produces:
  - `public enum BlockOutcome: Equatable { case verified, mismatch(block: Int), sizeDiffers(onDisk: Int64), incomplete }`
  - `public struct ByteRange: Equatable { offset: Int64; length: Int64 }`
  - `public final class BlockVerifier { static let maxPartials = 8; init(_ m: BlockManifest); feed(offset: Int64, _ p: UnsafeRawPointer, _ n: Int); observeLength(_:); missingRanges() -> [ByteRange]; finish() -> BlockOutcome; verifiedBytes; fedBytes; unstableBlocks: [Int]; diskHash(_ block: Int) -> UInt64?; lengthObserved: Bool }`
  - `public enum BlockReads { static func complete(_ path: String, _ v: BlockVerifier, gate: PauseGate?, isCancelled: () -> Bool, progress: ((Int) -> Void)?) throws -> (outcome: BlockOutcome, throughCache: Bool); static func result(_ v: BlockVerifier, _ o: BlockOutcome) -> DestFileResult }`

Pravidla (specifikace oddíl 5 „BlockVerifier“ a 10; Windows `BlockVerifier`, `BlockReads`):

- Každý přečtený bajt z cíle (tap v libovolném pořadí a se skoky, dočtení, čtení na pozadí) jde přes `feed`; blok se posoudí, jakmile
  má všechny bajty. Celý blok se hashuje hned; kusy bloku se skládají podle offsetu (nejvýš 8 rozpracovaných bloků — nejstarší se
  zahodí a zůstane chybějící). **Ověřený blok přečtený znovu celý:** jiná odpověď = `mismatch` a blok je „nestabilní“ (nesoulad při
  opakování = chyba I/O, nikdy se neschová); kus už ověřeného bloku se ignoruje. Nesouhlasný blok, který při novém celém čtení sedí,
  je ověřený, ale „nestabilní“ (poznámka do reportu). Bajty za koncem podle manifestu se ignorují; `observeLength` s jinou délkou =
  `sizeDiffers`. Bezpečné pro více vláken (tap čte víc spojeními).
- `missingRanges`: neověřené bloky (chybějící, rozpracované, nesouhlasné), sousední spojené — to dočte dorovnání („nic se nečte celé znovu“).
- `finish`: jiná délka → `sizeDiffers`; první nesouhlasný blok → `mismatch`; vše ověřené → `verified`; jinak `incomplete`.
- `BlockReads.complete` (dorovnání a klasické čtení): **před čtením** `CacheProbe.residentPages(path)`; rezidentní stránky → `evict`
  a znovu; pořád rezidentní → čte se dál, ale `throughCache = true` (poznámka `verifiedThroughCache`, varování úlohy — Mac obdoba
  Windows I3/C2). Čte chybějící bloky přes `IO.openRead` (`F_NOCACHE`) celé (poslední kratší), pak **znovu jednou** každý
  nesouhlasný blok (sedí-li teď → nestabilní čtení, ne selhání). Před každým blokem brána pauzy a zrušení (`CancellationError`).
  Chyba otevření/čtení (odpojený disk, soubor zmizel) = `IOError` — nikdy verdikt bez celého čtení.
- `result`: `verified` → stav `verified`; `sizeDiffers` → `failed` `size mismatch: <disk> != <karta>`; `mismatch(b)` → `failed`
  `HASH MISMATCH: block <b> dest <16 hex> != source <16 hex>`; `incomplete` → `written` (bez chyby — zrušené čtení).

- [ ] **Step 1: Padající testy** — `BlockVerifierTests.swift`:

```swift
import XCTest
@testable import OffloadKit

final class BlockVerifierTests: XCTestCase {
    let data = Data((0..<10).map { UInt8($0) })
    var manifest: BlockManifest {
        BlockManifest(size: 10, blockSize: 4, blocks: [0..<4, 4..<8, 8..<10].map { r in data.subdata(in: r).withUnsafeBytes { BlockHash.xxh3($0.baseAddress!, r.count) } })
    }
    func feed(_ v: BlockVerifier, _ off: Int, _ d: Data) { d.withUnsafeBytes { v.feed(offset: Int64(off), $0.baseAddress!, d.count) } }

    func testWholeFileVerified() {
        let v = BlockVerifier(manifest)
        feed(v, 0, data); v.observeLength(10)
        XCTAssertEqual(v.finish(), .verified)
        XCTAssertEqual(v.verifiedBytes, 10)
    }

    // Review focus 11.
    func testPiecesAssembleByOffset() {
        let v = BlockVerifier(manifest)
        feed(v, 6, data.subdata(in: 6..<10)); feed(v, 4, data.subdata(in: 4..<6)); feed(v, 0, data.subdata(in: 0..<4))
        XCTAssertEqual(v.finish(), .verified)
    }

    func testReReadWithDifferentDataIsMismatch() {
        let v = BlockVerifier(manifest)
        feed(v, 0, data)
        var bad = data.subdata(in: 4..<8); bad[bad.startIndex] ^= 0xFF
        feed(v, 4, bad)
        XCTAssertEqual(v.finish(), .mismatch(block: 1))
        XCTAssertEqual(v.unstableBlocks, [1])
    }

    func testMismatchThenGoodReadIsUnstableButVerified() {
        let v = BlockVerifier(manifest)
        var bad = data; bad[5] ^= 1
        feed(v, 0, bad); feed(v, 4, data.subdata(in: 4..<8))
        XCTAssertEqual(v.finish(), .verified)
        XCTAssertEqual(v.unstableBlocks, [1])
    }

    func testShorterFileIsMismatch() {
        let v = BlockVerifier(manifest)
        feed(v, 0, data.subdata(in: 0..<8)); v.observeLength(8)
        XCTAssertEqual(v.finish(), .sizeDiffers(onDisk: 8))
    }

    func testMissingRangesJoinNeighbours() {
        let v = BlockVerifier(manifest)
        feed(v, 4, data.subdata(in: 4..<8))
        XCTAssertEqual(v.missingRanges(), [ByteRange(offset: 0, length: 4), ByteRange(offset: 8, length: 2)])
        XCTAssertEqual(v.finish(), .incomplete)
    }

    func testCompleteReadsOnlyWhatIsMissingAndFlipsAreFound() throws {
        let p = tempDir().sub("f.bin")
        var bad = data; bad[9] ^= 1
        try bad.write(to: URL(fileURLWithPath: p))
        let v = BlockVerifier(manifest)
        feed(v, 0, data.subdata(in: 0..<8))
        let (o, _) = try BlockReads.complete(p, v, gate: nil, isCancelled: { false }, progress: nil)
        XCTAssertEqual(o, .mismatch(block: 2), "a flipped bit in a block the decoder never read is found by the fill-in")
        XCTAssertEqual(v.fedBytes, 8 + 2 + 2, "only block 2 read, then once more because it did not match")
        XCTAssertTrue(BlockReads.result(v, o).error?.hasPrefix("HASH MISMATCH: block 2 dest ") ?? false)
    }
}
```

- [ ] **Step 2: Implementace** — port pravidel (Windows `BlockVerifier.cs` je předloha logiky: stavy `missing/partial/verified/mismatch`,
  `Partial` s pokrytými intervaly, `NSLock`). `BlockReads.complete` čte přes `IO.openRead` do zarovnaného bufferu `blockSize`.
- [ ] **Step 3: Testy; záznam a commit** — VYVOJ-MAC `- Úkol 66 — ověřovač bloků (libovolné pořadí, kusy, opakované čtení = chyba,
  nestabilní čtení, délka) a dočtení jen chybějících bloků s druhým čtením nesouhlasných; kontrola cache před čtením`.
  Run: `git add -A mac docs && git commit -m "Mac: block verifier and fill-in reads"`

---

### Task 67: Odložené ověření — `verifyPending`, potvrzení výsledku, `verifyIncomplete`, zámek složek, dva stavy karty

**Files:**
- Modify: `mac/Sources/OffloadKit/Engine.swift` (`OffloadOptions.deferVerify`), `Report.swift` (`withVerification`), `Job.swift` (fáze 2: ověření, `finishVerification`, zámek složek, historie)
- Create: `mac/Sources/OffloadKit/VerifyInProxy/Verification.swift` (výsledek po cílech, zápis do složek)
- Create: `mac/Tests/OffloadKitTests/DeferredVerifyTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `OffloadReport.commit` (úkol 41), `MHL.hasGeneration` (úkol 40), `FolderLocks` (úkol 37), `CardRecord`, `BackupState` (úkol 38), `JobStatus.safeToEject/safeToFormat` (úkol 39), `HistoryStore.update` (úkol 42), `BlockReads` (úkol 66), `VIPMsg` (úkol 65).
- Produces:
  - `OffloadOptions.deferVerify: Bool` (fáze 1 bez zpětného čtení; report `verifyPending: true`, bez generace MHL a seznamů součtů)
  - `public func OffloadReport.withVerification(results: [[DestFileResult]], verified: Bool, primary: Int, classicWhy: [String?], notes: [String], finished: Date, verifyPaused: TimeInterval) -> OffloadReport`
  - `enum Verification { static func commit(_ final: OffloadReport, cardPaths: [String], log: (String) -> Void) -> (committed: Bool, written: [String], warning: String?) }`
  - `JobStatus.phase .finishingVerification` („Dokončení ověření“)

Pravidla (Windows R5, R6, rulings I1, I2, I4, I5, M-a; FORMATY `verifyPending`, `verifyIncomplete`; spec D.22.5, D.22.7):

- S proxy **a** ověřením **a** volbou `verifyInProxy` (úkol 70; výchozí zapnuto) a cílem, který prošel `CacheProbe.problem`, běží
  `deferVerify`: fáze 1 zapíše, vyprázdní (`flushDevice`), přejmenuje, zapíše manifest a **reporty s `verifyPending: true`** (stavy
  `written`, `verified: false`) — **bez generace ASC MHL a bez seznamů součtů** (ty zapíše až potvrzení). Pak „Lze vyjmout“
  (`safeToEject`), „Lze formátovat“ ne.
- **Zámek složek (I4):** celá kopie s `deferVerify` vezme `FolderLocks` svých nových složek **před** zápisem reportů a drží ho do zápisu
  výsledku ověření ve fázi 2 (`flock` se smí uvolnit z jiného vlákna — úkol 37). Doplnění, oprava ani navázání na tutéž zálohu mezitím
  nejde (`appendBusy`).
- Po ověření (úkol 68): `withVerification` — nový objekt reportu (stavy z ověření; `verified` = ověření doběhlo, zrušené nechá
  `written`), `verifyPending` pryč, `destinations[].verifyMethod` (`verify-in-proxy` u primárního, `read-back` u ostatních),
  `verifyNote` (proč klasicky), `safeToFormatSeconds` (jen když je vše `verified`, bez pauz), `verifyNotes`, `verifyPausedSeconds`.
- **Potvrzení** do každé složky (`report.commit`: MHL + řetěz **nejdřív**, pak reporty, pak seznamy součtů; idempotentní). Složka,
  která mezitím zmizela, se **nikdy nevytvoří znovu** (`Msg`: `složka karty na cíli zmizela (odpojený disk?)`). Selže-li potvrzení
  kdekoli: složky, kam se výsledek **zapsal**, se přepíšou ještě jednou s `verifyIncomplete: true` (sama by vypadala ověřeně, zatímco
  jiný cíl výsledek nemá); úloha varování `Výsledek ověření se nepodařilo zapsat (<detail>) — kartu zatím neformátujte; zálohu dokončí
  „Dokončit přerušenou zálohu“ (Doplnit, offload append).` a **nikdy zeleně**. Opraví to navázání (úkol 69).
- „Lze formátovat“ = potvrzeno všude **a** `final.safeToFormat` **a** `BackupState` celé zálohy 0/0 **a** `notInBackup == 0`.
  Historie: záznam zůstane `unfinished`, dokud se výsledek nezapíše (I5), pak `verified` podle `CardRecord`; zrušení během ověření —
  generace se nepotvrdí, reporty zůstanou `verifyPending` (navázání je dokončí), historie „nedokončeno“.
- Pád aplikace mezi fází 1 a potvrzením: reporty `verifyPending` bez MHL = záloha **není** ověřená (úkol 38, 45), další start karty
  nabídne „Dokončit přerušenou zálohu“ (úkol 69).

- [ ] **Step 1: Padající testy** — `DeferredVerifyTests.swift` (bez proxy — ověření ve fázi 2 nahradí testovací háček `classic`
  `BlockReads.complete` přes všechny soubory; tap je úkol 68):
  `testStage1WritesPendingReportsWithoutMhl` (po fázi 1: report `verifyPending`, žádné `ascmhl/`, žádné `.xxh64`, `safeToEject` ano,
  `safeToFormat` ne, `CardRecord.verified` ne); `testCommitWritesMhlThenReportsThenLists` (po fázi 2: řetěz 1, report bez `verifyPending`,
  `verifyMethod` u cílů, `.xxh64`; `safeToFormat`); `testCommitFailureLeavesIncompleteEverywhereWritten` (druhý cíl smazaný před
  potvrzením → první složka `verifyIncomplete: true`, druhá se nevytvořila, varování, `safeToFormat` false, `BackupVerifier` na první
  složce `unfinished`); `testCrashBeforeCommitLeavesPending` (úloha zrušená ve fázi 2 → reporty `verifyPending`, bez MHL, historie
  `unfinished`); `testFolderLockHeldUntilCommit` (během fáze 2 `FolderLocks.acquire` → `appendBusy`; po potvrzení projde).
- [ ] **Step 2: Implementace** podle pravidel; v `Job.swift` fáze 2 po proxy volá `VerifySession.finish` (úkol 68) nebo — bez proxy —
  klasické čtení, pak `withVerification` → `Verification.commit` → stav a historie.
- [ ] **Step 3: Testy; záznam a commit** — VYVOJ-MAC `- Úkol 67 — odložené ověření: reporty verifyPending bez MHL, zámek složek do
  potvrzení, potvrzení MHL → reporty → seznamy, verifyIncomplete při částečném zápisu, Lze formátovat jen po potvrzení všude`.
  Run: `git add -A mac docs && git commit -m "Mac: deferred verification with commit and verifyIncomplete"`

---

### Task 68: Ověření během proxy — HTTP tap pro ffmpeg, čtení před SDK, sezení ověření

**Files:**
- Create: `mac/Sources/OffloadKit/VerifyInProxy/RangeServer.swift`, `VerifyInProxy/TapFile.swift`, `VerifyInProxy/VerifySession.swift`, `VerifyInProxy/TapRegistry.swift`
- Modify: `mac/Sources/OffloadKit/Proxy.swift` (vstup ffmpeg = URL tapu, `-short_seek_size 65536`; hlídač bere aktivitu tapu jako pokrok), `Job.swift` (fáze 2 se sezením)
- Create: `mac/Tests/OffloadKitTests/RangeServerTests.swift`, `VerifySessionTests.swift`
- Modify: `docs/VYVOJ-MAC.md`

**Interfaces:**
- Consumes: `BlockVerifier`, `BlockReads` (úkol 66), `CacheProbe` (úkol 65), `ManifestFile`/`BlockManifest` (úkol 65), `SourceHasher.blocks` (úkol 51), `PauseGate` (úkol 61), `ProxyRenderer.runFFmpeg` + hlídač (úkoly 19, 30), `VolumeFacts.folderKey` (úkol 36).
- Produces:
  - `public enum TapMode { none, http, preRead }`, `public enum TapRegistry { static func mode(for clip: Clip) -> TapMode }` (MOV/MP4/MXF video → `http`; BRAW, R3D, ARRIRAW → `preRead`; jinak `none`)
  - `protocol TapSource: AnyObject { var length: Int64 { get }; func read(_ offset: Int64, _ buffer: UnsafeMutableRawPointer, _ count: Int) throws -> Int; var activity: Int64 { get } }`
  - `public final class RangeServer { init() throws; var port: UInt16; func register(_ source: TapSource, name: String) -> String (URL); func unregister(_ url: String); func activity(_ url: String) -> Int64?; func close(); static func parseRange(_ header: String, length: Int64) -> (Int64, Int64)?; static func parseTarget(_ target: String, token: String) -> (id: String, name: String)? }`
  - `final class TapFile: TapSource` (cíl primárního disku čtený po celých blocích `F_NOCACHE`, 4 bloky v paměti, každý přečtený blok do ověřovače)
  - `public final class VerifySession { init(report: OffloadReport, cardPaths: [String], blocks: [[UInt64]], rates: [Double], preferred: Int?, classicWhy: [String?], gate: PauseGate, cardBlock: ((Int, Int) -> UInt64?)?); primary: Int; startBackground(); prepare(_ clip: Clip, dest: Int) -> (url: String?, done: () -> Void); finish(isCancelled: () -> Bool) -> (results: [[DestFileResult]], verified: Bool, notes: [String], throughCache: [Int]) }`

Pravidla (specifikace oddíly 3, 5, 6, 10, 11; Windows R1, R3, R10, batch 7–9 ruling B1, I3; spec D.23.1, D.23.7):

- **Režim A (MOV/MP4/MXF):** Offload nevolá libavformat, ale ffmpeg CLI → **místní HTTP server Offloadu** jen na `127.0.0.1`
  (náhodný port), cesta `/<128bit token>/<náhodné id souboru>/<jméno pro příponu>` (žádná cesta na disku — nic se nedá
  podstrčit), jen `GET`/`HEAD`, jeden rozsah (`bytes=a-b`, `bytes=a-`, `bytes=-n`), hlavička `Host` přesně `127.0.0.1:<port>`
  (ochrana proti DNS rebinding), protistrana mimo loopback se zahodí nepřečtená, nejvýš 8 spojení, hlavička celá do 30 s (proti
  slowloris), malý socket buffer a malé kusy (když ffmpeg spojení zavře kvůli skoku — `moov` na konci, MXF patička — server přestane
  číst brzy). Implementace: BSD sockety (`socket`/`bind` na `INADDR_LOOPBACK`/`listen`/`accept`, vlákno na spojení) nebo
  `Network.framework` `NWListener` s `requiredLocalEndpoint` 127.0.0.1 — rozhodne vykonavatel, pravidla platí stejně. **AVFoundation
  (`AVAssetResourceLoaderDelegate`) se nepoužije** — proxy dělá ffmpeg, tap musí obsloužit jeho čtení (spec D.24).
- ffmpeg dostane `-short_seek_size 65536 -i http://127.0.0.1:<port>/<token>/<id>/<jméno>` (bez něj ffmpeg 8.1 četl MOV s `moov`
  na konci i MXF dvakrát — měření Windows). Hlídač zaseknutí (úkol 19) bere za pokrok i aktivitu tapu (bajty čtené z disku po kusech
  1 MiB) — pomalý cíl proxy nezabije, zaseknutý ano.
- **`TapFile`**: před otevřením `CacheProbe.residentPages`; rezidentní → `evict` a znovu; **pořád rezidentní → tap se pro ten soubor
  nepoužije** (proxy čte soubor přímo, ověří ho dorovnání klasicky s poznámkou) — Review focus 9. Čte celé bloky `F_NOCACHE`,
  každý blok jednou do ověřovače, ffmpegu servíruje z malé cache (4 bloky); blok, který z cache vypadl a čte se znovu, jde do
  ověřovače znovu (jiná odpověď = nesoulad). Po ukončení nečeká na čtení, které visí na disku.
- **Režim B (BRAW, R3D, ARRIRAW — SDK bez vlastního I/O):** před SDK Offload soubor (po kontrole a pokusu o vyhození z cache)
  přečte **jednou po blocích přímo z disku** (`F_NOCACHE`) do ověřovače; SDK pak čte samo (z disku znovu — ruling B1: „ověřeno“ nikdy
  z čtení přes cache). Hlídač: čtení bez nového bloku déle než limit hlídače proxy (60 s) = vzdát, soubor ověří dorovnání.
- **Primární cíl** = nejrychlejší při kopii (zápisová rychlost cíle z fáze 1), nebo zvolený (`verifyPrimary: "first"` = první;
  CLI `--primary N`), a jen cíl, který prošel `CacheProbe.problem` (jinak se čte na pozadí s poznámkou `verifyNote`). Ostatní cíle
  se čtou **na pozadí**: jedno vlákno na fyzický svazek (`folderKey` svazku), nižší priorita (`setpriority(PRIO_DARWIN_THREAD, 0,
  PRIO_DARWIN_BG)` — I/O throttling; ověřit dopad, D.27), před každým blokem brána pauzy; soubory primárního disku, které žádná proxy
  nečte (zvuk, sidecar, další díly, „bez proxy — originál je lehčí“), čte vlákno primárního svazku, jen když proxy primární disk nečte.
- **Dorovnání** po proxy: `missingRanges` → `BlockReads.complete` (jen chybějící, nesouhlasné ještě jednou). Proxy spadne nebo ji
  uživatel zruší → ověřené bloky platí, zbytek dorovná. Zrušení úlohy zastaví i ověřování (stavy zůstanou `written`).
- **Poznámky (spec 10):** nestabilní čtení `Nestabilní čtení: <cesta> na <disk> (blok 3) dal při jednom čtení jiná data — při dalším
  souhlasil s kartou.`; stejný blok špatně na více cílech → blok se přečte z karty znovu (jen když je ve čtečce **ta** karta)
  a poznámka `<cesta>: blok N nesouhlasí stejně na <disky> — <karta teď dává zaznamenaná data: …|stejná data jako disky: …|jiná data …|
  kartu se nepodařilo přečíst znovu …>`; čtení přes cache macOS `verifiedThroughCache` (úkol 65) → varování (bez zelené zvuku).
- Bez proxy pro celou kartu, vypnutá volba, oprava → klasické zpětné čtení (jako dosud, ale přes `BlockReads` s kontrolou cache).

- [ ] **Step 1: Padající testy** — `RangeServerTests.swift`: `parseRange` (tabulka: `bytes=0-9`, `bytes=5-`, `bytes=-3`, víc rozsahů
  → nil, mimo délku → nil), `parseTarget` (cizí token → nil, `../` v jméně se nepoužije), skutečný server: `URLSession` GET s rozsahem
  → `206` a správné bajty, `Host: localhost:<port>` → `400`, neznámé id → `404`, `POST` → `405`; ffmpeg (jinak `XCTSkip`): `ffprobe`
  přes URL tapu čte MOV s `moov` na konci → délka sedí a ověřovač má všechny bloky po dorovnání. `VerifySessionTests.swift`:
  `testFlippedBitOnPrimaryFoundInModeA` (bit změněný na cíli po kopii, mimo Offload — `dd conv=notrunc` → `failed` s blokem),
  `testFlippedBitInTrailingIndexFoundByFillIn`, `testResidentFileIsNeverVerifiedFromTheTap` (soubor znovu načtený do cache běžným
  čtením a `evict` vypnutý háčkem → tap se nepoužije, výsledek z dorovnání s `throughCache`), `testSecondaryReadInBackground`,
  `testCancelLeavesWritten`, `testPrimaryIsFastestUnlessChosen`.
- [ ] **Step 2: Implementace** podle pravidel. `ProxyRenderer.render` dostane volitelnou URL vstupu (místo cesty) a aktivitu pro hlídač.
- [ ] **Step 3: Ověřit na Macu** (D.27 ř. 5; výsledky do commitu) — ffmpeg z Homebrew i statický v balíčku: MOV s `moov` na konci,
  MP4 faststart, MXF (Sony XAVC, ARRI ProRes MXF): čtení přes tap ≤ 1,05 × velikost souboru (`fedBytes`), doba dekódování přes tap
  proti čtení z disku (cíl spec 11: ≤ 5 % navíc); dva cíle — čtení na pozadí nezpomalí proxy o víc než 5 %; `PRIO_DARWIN_BG` nepodtrhne
  rychlost čtení pozadí pod rozumnou mez (jinak priorita normální a zapsat).
- [ ] **Step 4: Testy; záznam a commit** — VYVOJ-MAC `- Úkol 68 — ověření během proxy: HTTP tap na 127.0.0.1 pro ffmpeg (token,
  Host, rozsahy, limity), čtení před SDK u RAW, primární disk nejrychlejší nebo zvolený, ostatní na pozadí po svazcích, dorovnání,
  nikdy „ověřeno“ z tapu u souboru v cache; naměřeno: <čísla>`.
  Run: `git add -A mac docs && git commit -m "Mac: verify-in-proxy — HTTP tap, pre-read for RAW SDKs, verify session"`

---

### Task 69: Navázání po pádu — „Dokončit přerušenou zálohu“ přes doplnění

**Files:**
- Create: `mac/Sources/OffloadKit/Append/ResumePlanner.swift`
- Modify: `mac/Sources/OffloadKit/Append/AppendPlanner.swift` (nedokončená generace), `Engine.swift` (`known`, `hashOnly`, `resume` z kontrolního bodu), `Job.swift`, `History/HistoryText.swift`, `mac/Sources/OffloadApp/Views.swift` (text u karty)
- Create: `mac/Tests/OffloadKitTests/ResumeTests.swift`
- Modify: `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`

**Interfaces:**
- Consumes: `ManifestFile.files/parts`, `PartCheckpoint` (úkol 65), `AppendPlanner.checkBackup(_:unfinishedGeneration:)` (úkol 46), `MHLReader.readChain`, `OffloadReport.read`, `FileTimes.close`, `HistoryEntry.unfinished` (úkol 42).
- Produces:
  - `public struct Unfinished { generation: Int; manifests: [[ManifestRecord]]; fixedFiles: Set<String>?; func fileOf(_ d: Int, _ f: SourceFile, localTime: Bool) -> ManifestRecord?; func partOf(…) -> PartCheckpoint?; func owns(_ d: Int, _ f: SourceFile, sizeOnDisk: Int64, localTime: Bool) -> Bool; func mentions(_ f: SourceFile) -> Bool }`
  - `public enum ResumePlanner { static func find(_ cardPaths: [String]) throws -> Unfinished?; static func decide(_ u: Unfinished, cardPaths: [String], files: [SourceFile], present: [[Bool]], localTime: [Bool]) -> (write: [[Bool]], known: [ManifestRecord?], hashOnly: [Bool], resume: [PartCheckpoint?]) }`
  - `AppendPlan.resuming`, `write`, `known`, `hashOnly`, `resume`, `later`

Pravidla (FORMATY „Blokový manifest“ — Navázání (R6); Windows `ResumePlanner`, rulings I1, I5, M1, M2):

- Složka je **otevřená** u generace g, když: report g má `verifyPending` (g za potvrzenou — běh skončil před potvrzením; nebo g je
  potvrzená v řetězu, ale reporty ne), nebo report potvrzené g má `verifyIncomplete`, nebo report g po potvrzené chybí a manifest má
  záznamy g (běh skončil během kopie). Ostatní složky musí stát těsně před g bez reportu g, nebo mít g potvrzenou **týmž během**
  (stejné `started` a stejné cíle) — jinak `appendDiverged` `<složka>: nedokončená záloha (generace g) nesouhlasí s ostatními cíli —
  zkontrolujte ji ručně.` Je-li g už v některém řetězu, soubory generace jsou **pevné** (jen ty, které report g uvádí); soubory karty
  natočené později čekají na další doplnění (`later`, karta pak není „Lze formátovat“).
- Doplnění, které najde nedokončenou generaci, **dokončí tutéž generaci g** (žádné nové číslo): soubor na místě se záznamem `file`
  všude (stejný xxh64) se znovu nečte (`known`); na místě bez záznamu se z karty jen přepočítá (`hashOnly`); rozepsaný
  `.offload-part` pokračuje od **společného** kontrolního bodu všech cílů, kam se zapisuje (bloky do kontrolního bodu se z disku přečtou
  a porovnají; nesedí → od nuly) — jen pro cíl zapisovaný mimo cache (M1), velikost bloku z manifestu (M2); zbytek se zkopíruje. Pak
  ověření a zápis generace jako u každé kopie; reporty generace g z přerušeného běhu se nahradí (ještě nebyly potvrzené). Soubor na
  cíli, který neuvádí žádný report, je „vlastní“ přerušenému běhu jen když ho manifest zná se stejnou velikostí a časem (`owns`) —
  jinak konflikt jako dřív.
- Generace 1 bez reportu (pád během celé kopie): složky nemusí mít report, kontroly zálohy se přeskočí (jen dostupnost), součty podle
  manifestu (MD5, když ho záznamy mají).
- **Oprava** nedokončenou zálohu odmítne (`VIPMsg.repairUnfinished`); **Ověřit zálohu** ji hlásí jako nedokončenou (úkol 45).
- Karta: historie `unfinished` (úkol 44) nebo nalezená nedokončená generace → řádek `Nedokončená záloha … — kopie se přerušila, lze ji
  dokončit` a zaškrtávátko **„Dokončit přerušenou zálohu“** (i bez nového souboru — doplnění pak nic nového nekopíruje, ale dokončí
  potvrzení). Hotové proxy přerušeného běhu se nerenderují znovu (poznámka `proxy z přerušeného běhu — nerenderovala se znovu (obsah
  klipu dekodér tentokrát nekontroloval)`), nikdy `_2`, nic se nepřepíše.

- [ ] **Step 1: Padající testy** — `ResumeTests.swift` (pád simulovaný zrušením po zápisu reportů s `verifyPending`, nebo ukončením
  kopie po kontrolním bodu — testovací háček `OffloadOptions.stopAfterCheckpoint`): `testPendingGenerationIsCompletedNotRenumbered`
  (doplnění dokončí generaci 1, řetěz `[1]`, reporty bez `verifyPending`), `testKnownFilesNotReadAgain` (čítač čtení karty), `testPartResumesFromCommonCheckpoint`
  (v testu soubor 5 bloků po 1 MiB, kontrolní bod po 2 blocích → karta se čte od bloku 2), `testCheckpointMismatchStartsOver`,
  `testIncompleteCommitIsFinished` (`verifyIncomplete` na jedné složce, druhá bez výsledku → dokončit = obě potvrzené), `testDivergedRefused`,
  `testRepairRefusesUnfinished`, `testUnknownFileOwnedByTheRunIsNoConflict`.
- [ ] **Step 2: Implementace** podle pravidel (Windows `ResumePlanner.cs` je předloha logiky `find`/`decide`/`commonCheckpoint`).
- [ ] **Step 3: Ruční kontrola** — velká karta na dva disky, během kopie `kill -9` aplikace; znovu spustit, vložit kartu → „Nedokončená
  záloha“, „Dokončit přerušenou zálohu“ → dokončí, Ověřit zálohu → v pořádku; totéž během fáze 2 (ověřování s proxy).
- [ ] **Step 4: Záznam a commit** — ROZDILY „Blokový manifest a navázání po pádu“ Mac ✅; VYVOJ-MAC `- Úkol 69 — navázání po pádu:
  doplnění dokončí nepotvrzenou generaci (známé soubory nečte, rozepsaný soubor od společného kontrolního bodu, verifyIncomplete
  dopotvrdí), oprava ji odmítne, karta nabídne Dokončit přerušenou zálohu`.
  Run: `git add -A mac docs && git commit -m "Mac: resume an interrupted backup through append"`

---

### Task 70: Obsahová kontrola dekodérem, volby a CLI, dva stavy v GUI s vadami obsahu, dokumentace a verze 0.7.0

**Files:**
- Create: `mac/Sources/OffloadKit/VerifyInProxy/ContentCheck.swift`
- Modify: `mac/Sources/OffloadKit/Media.swift`/`Report.swift` (`ClipEntry.content`), `Proxy.swift` (stderr na úrovni `warning`, `-progress` snímky), `HTMLReport.swift` (metoda ověření a sériové číslo u cíle, čas do „lze formátovat“, tabulka vad obsahu s náhledem, `verifyIncomplete` žlutě NEDOKONČENO)
- Modify: `mac/Sources/OffloadKit/Desktop/AppSettings.swift` (`verifyInProxy`, `verifyPrimary` v profilu), `mac/Sources/offload/main.swift` (`--classic-verify`, `--primary N`, kód 1 bez „Lze formátovat“), `mac/Sources/OffloadApp/*` (Nastavení → Ověření, štítky, potvrzení vad)
- Create: `mac/Tests/OffloadKitTests/ContentCheckTests.swift`, `VerifyInProxyCLITests.swift`
- Modify: `mac/Sources/OffloadKit/Report.swift` (`0.7.0`), `mac/README.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`, `docs/VYVOJ-MAC.md`, `mac/docs/superpowers/plans/README-START.md` (stav)

Pravidla (specifikace oddíl 7, 9; Windows R9, R11, úkoly 10, 15–18; FORMATY `clips[].content`):

- **Obsahová kontrola** (R9 — nikdy `-xerror`, zastavil by proxy): stderr ffmpeg na úrovni `warning` a `frame=` z `-progress`.
  Varování = řádky o poškozených datech (`corrupt decoded frame`, `decoding error`, `error while decoding`, `concealing`, `invalid nal`,
  `corrupt input packet`, `packet corrupt`, `invalid data found`, `missing picture`, `error splitting`, `partial file`, `decode_slice`,
  `error submitting packet to decoder`), o časových značkách (`non-monotonic`, `non monotonically`, `invalid timestamp`, `timestamps are
  unset`, `invalid dts`), řádky **zvukového dekodéru** (`[aac @`, `[pcm_…`, `mp3`, `ac3`, … s `exceeds limit`, `is not allocated`, `crc
  mismatch`, `invalid frame`, `error decoding` …; vlastní AAC enkodér proxy se nepočítá), méně snímků než `nb_frames` (`end`), víc
  (`frames`). Výsledek `clips[].content` `{state: ok|warn, kind: decode|audio|timestamps|frames|end, frame, timecode, message,
  framesDecoded, framesExpected}`; `warn` je vada souboru z kamery (kopie sedí s kartou) — **nikdy selhání kopie a neblokuje
  „Lze formátovat“**, ale formátování chce výslovné potvrzení: štítek `Lze formátovat po kontrole vad` (žlutě), tlačítko
  `Zkontrolováno — kartu lze formátovat`, varování `2 klipy mají vadu obsahu — před formátováním je zkontrolujte (kopie souhlasí
  s kartou).`; náhled místa vady (JPEG jen v HTML). Platí i při klasickém ověření s proxy. Klip bez proxy `content` nemá.
- **Volby:** profil `verifyInProxy` (bool, výchozí `true`; Nastavení → Ověření „Ověřit primární disk během proxy“), `verifyPrimary`
  (`"fastest"` | `"first"`, „Primární disk: nejrychlejší / první“; neznámá hodnota = `fastest`); obě součástí projektu. CLI `copy`
  i `append`: `--classic-verify` (zpětné čtení jako dřív), `--primary N` (1…počet cílů; mimo rozsah `--primary 0: cíle se číslují od 1
  (pořadí --dest).` / `--primary 5: cílů je jen 2 (číslují se od 1 v pořadí --dest).`, kód 1). Kód 1 i po kopii, po které kartu nelze
  formátovat (výsledek ověření nezapsán, neověřené dřívější soubory); `--no-verify` zůstává 0.
- **Report:** HTML/PDF a TXT — metoda ověření a sériové číslo u každého cíle, `Safe to format after:` / „Lze formátovat po …“, tabulka
  vad obsahu (klip, druh česky `poškozená data`, `poškozený zvuk`, `chybné časové značky`, `snímky navíc`, `klip končí předčasně`,
  TC, náhled), poznámky ověření, `verifyIncomplete` žlutě NEDOKONČENO (nikdy zeleně OVĚŘENO). Kroky karty úlohy s ověřením během proxy:
  Kopírování · Proxy · Ověření · Report; fáze „Dokončení ověření“.
- **FORMATY** (tento úkol, Mac přestává klíče ignorovat): `verifyPending`, `verifyIncomplete`, `safeToFormatSeconds`, `verifyNotes`,
  `destinations[].verifyMethod`, `verifyNote`, `volumeSerial`, `files[].xxh128`, `clips[].content`, metoda v komentáři MHL, „Blokový
  manifest … Mac od 0.7.0“; `settings.json` doplnit `verifyInProxy`, `verifyPrimary` (chybí i u Windows — doplnit pro obě verze).

- [ ] **Step 1: Padající testy** — `ContentCheckTests` (falešný ffmpeg skript se stderr řádky a `-progress`: každá skupina slov, AAC
  enkodér nepočítat, méně snímků → `end`, ok bez řádků, opakování v softwaru zapomene první pokus; JSON tvar `content`);
  `VerifyInProxyCLITests` (`--primary` mimo rozsah — texty a kód 1; `--classic-verify` → `verifyMethod: read-back` u všech cílů).
- [ ] **Step 2: Implementace** podle pravidel; GUI (stávající vzhled i nový z úkolů 63–64): dva štítky, vady obsahu se seznamem,
  náhledem a potvrzením jen na kartě, kterou jinak lze formátovat.
- [ ] **Step 3: Verze 0.7.0 a dokumentace** — `VersionTests`, FORMATY (výše, `version` „`0.7.0` (Mac)“), ROZDILY (řádky plánu 10
  Mac ✅; záměrné rozdíly „Verify-in-Proxy“, „Lze vyjmout × Lze formátovat“, „CLI --classic-verify a --primary“ přepsat na obě verze;
  přidat Mac rozdíl „kontrola cache: mincore místo NO_BUFFERING, čtení přes cache macOS jen s poznámkou“), README (příkazy, ruční test),
  README-START (stav: plán dorovnání 2 hotový), VYVOJ-MAC: **oddíl „Naplánováno“ — smazat odrážku „Dorovnání 2 s Windows 0.6.0 — plány 6–10
  a dodatky 0.5.x“** (plán je hotový), ponechat jen položky „Mimo plán“, které opravdu zbývají; nadpis průběhu přejmenovat na
  `## <datum> — verze 0.7.0: dorovnání 2 s Windows 0.6.0 (plány 6–10)`.
- [ ] **Step 4: Závěrečné ověření** — `swift build -c release && swift test && scripts/build-app.sh`; **ruční test s hardwarem**
  (README-START oddíl 7, body 14–24): karta na 2 disky s proxy → „Lze vyjmout“ po kopii, „Lze formátovat“ až po ověření; `dd` vada na
  primárním disku po kopii → nalezena; RAW (BRAW) → režim B; síťový cíl → `read-back` s poznámkou; `kill -9` během ověřování →
  Dokončit přerušenou zálohu; vadný klip (uříznutý MP4) → `content_warn` s TC a potvrzením. Křížově s Windows 0.6.0: záloha Macu
  s `xxh128` a manifestem → Windows `verify` OK a Windows „Dokončit přerušenou zálohu“ na zálohu přerušenou na Macu (a naopak) —
  výsledky do VYVOJ-MAC.
- [ ] **Step 5: Commit** — VYVOJ-MAC `- Úkol 70 — obsahová kontrola dekodérem (content ok/warn, potvrzení před formátováním), volby
  verifyInProxy/verifyPrimary, CLI --classic-verify a --primary, report s metodou ověření a vadami obsahu; verze 0.7.0 (plán 10)`.
  Run: `git add -A mac docs && git commit -m "Mac 0.7.0: content check, verify-in-proxy options and CLI, docs after plan 10"`

---

## Pokrytí — Windows plány 6–10 a „Naplánováno“ ve VYVOJ-MAC

| Oblast (Windows) | Úkoly Macu |
|---|---|
| Plán 6: historie, otisk, zámek historie | 36, 37, 42–44, 49 |
| Plán 6: doplnění, generace MHL N, zámek složky | 37, 40, 41, 46, 47, 49 |
| Plán 6: název z klipů, další v řadě | 43, 44, 49 |
| Plán 6: Ověřit zálohu, report ověření a jeho umístění | 40, 45, 49 |
| Plán 6: kontrola místa | 48, 49 |
| Plán 6: české hlášky, neznámé klíče nastavení | 34, 35 |
| Plán 7: MD5 navíc, `xxh128` (0.6.0) | 32, 51 |
| Plán 7: seznamy klipů, denní report | 52, 53 |
| Plán 7: proxy pro přelinkování; 0.5.1 proxy ve složkách, lehčí originál | 54 |
| 2026-10-08 HTML reporty (PDF lišta, tisk, šířka) | 55 |
| Plán 8: projekty, pravidla LUTů, `--gen5`, oznámení, aktualizace, podpis | 56–60 |
| Plán 9: Pozastavit, Zkopírovat znovu, nový vzhled, Dnes, štítek kamery, odhad | 61–64 |
| 0.5.2 Po zkopírování — klikací volby | 63 |
| Plán 10: zápis mimo cache, manifest, ověřovač, odložené ověření, tap, navázání, obsah, dva stavy | 39, 65–70 |
| Sdílená definice „ověřeno“ a `volumeSerial` (R12) | 36, 38 |
