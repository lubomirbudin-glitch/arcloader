# Offload pro Mac — dorovnání s Windows verzí (návrh)

Datum: 2026-10-05 (doplněno 2026-10-08) · Stav: ke schválení · Implementační plán:
[`../plans/2026-10-05-offload-mac-parity.md`](../plans/2026-10-05-offload-mac-parity.md) (úkoly 1–31), pokračování
[`../plans/2026-10-08-offload-mac-parity-2.md`](../plans/2026-10-08-offload-mac-parity-2.md) (32–55) a
[`../plans/2026-10-08-offload-mac-parity-3.md`](../plans/2026-10-08-offload-mac-parity-3.md) (56–70) · Jak začít na Macu:
[`../plans/README-START.md`](../plans/README-START.md)

Navazuje na: [`docs/ROZDILY.md`](../../../../docs/ROZDILY.md) (co Macu chybí — závazný seznam),
[`docs/VYVOJ-MAC.md`](../../../../docs/VYVOJ-MAC.md) (oddíl „Naplánováno“), [`docs/FORMATY.md`](../../../../docs/FORMATY.md)
(formát na disku), Windows návrh
[`windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md`](../../../../windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md)
(oddíly 4.5, 5, 6.5a, 8, 8a) a historie [`docs/VYVOJ-WINDOWS.md`](../../../../docs/VYVOJ-WINDOWS.md).

## 1. Cíl

Mac verze (`mac/`, Swift 5.9, SwiftUI, macOS 13+) se od verze 0.1.0 nezměnila. Windows verze mezitím dostala
funkce a opravy, které uživatel chce i na Macu. Tento návrh říká **přesně co, jak a kde** — aby práce na Macu
(vývojář nebo Claude Code) mohla začít hned, bez znalosti Windows kódu, a dala se tam ověřit.

Úspěch:

1. Na Macu proběhne `swift build`, `swift test` (nový testovací target) a `scripts/build-app.sh` bez chyb.
2. Report má u každého klipu tři snímky (první / prostřední / poslední) s popiskem TC — stejně jako Windows.
3. Preflight a report se chovají podle pravidel B1–B4 (oddíl 3.1) shodně s Windows.
4. Karta úlohy: české množné číslo, oranžové „Zrušeno — karta není kompletně zálohovaná.“, vysune se jen karta,
   která se kopírovala.
5. Menu *Nástroje → Prověřit USB porty…* (⌘U) otevře okno Hardware: kde jsou karta a disky připojené, jak rychlá
   je linka, doporučení, volitelné měření; během kopie karta úlohy ukazuje řetěz připojení a úzké hrdlo;
   CLI `offload ports [--measure]` a „brzdí: D1“ ve stavovém řádku `offload copy`.
6. Výstupy na disku zůstávají shodné s Windows (FORMATY.md) — kopii z Macu ověří Windows a naopak.
7.–11. Vestavěné LUTy podle kamery, složka pro proxy navíc, barvy proxy BT.709 / náhledy JFIF, ochrana proti zaseknutí
   ffmpeg a ikona aplikace — viz oddíl „Doplněk 2026-10-05“ na konci (úkoly 18–25).
12. Blackmagic RAW jako Windows po plánu 3 (gama → LUT, BRAW jen s knihovnou, limit `--info`, české poznámky, náhledy pod
   hlídačem) — oddíl „Doplněk 2“ na konci (úkoly 27–31; pořadí plánu 1–25, 27–31, 26).
13. Dorovnání s Windows 0.6.0 (plány 6–10): historie karet, doplnění jako generace ASC MHL N, Ověřit zálohu, kontrola místa,
   denní report, seznamy klipů, MD5 a XXH128, projekty, pravidla LUTů, oznámení, Pozastavit, Zkopírovat znovu, nový vzhled
   (po schválení), Verify-in-Proxy s dvěma stavy karty — oddíl „Doplněk 3“ na konci (D.21–D.28, úkoly 32–70 po úkolu 26).

## 2. Výchozí stav

| Oblast | Mac dnes (0.1.0) | Windows (reference) |
|---|---|---|
| Náhledy v reportu | 1 JPEG ve 30 % délky (`ProxyRenderer.thumbnail`) | 3 snímky + popisek TC (`ProxyRenderer.Thumbnails`) |
| Report při zrušeném ověření | „zapsáno“ i „ověřeno“ se počítá jako chyba | v pořádku, jen bez „lze vysunout“ |
| Preflight | bez kontroly voleb, duplicit a „zdroj je offload“ | B2–B4 |
| Zrušení úlohy čekající ve frontě | karta navždy „Čeká“, nikdy nedoběhne | „Zrušeno“, fronta ji ohlásí jako hotovou |
| Karta úlohy | „5 karty“, „1 souborů“, červené „N souborů selhalo“ i u zrušené | `Texts`, oranžové „Zrušeno — …“ |
| Vysunutí | `unmountAndEjectDevice(config.source)` na hlavním vlákně, i u ručně přidané složky | jen kopírovaná karta (identita), nikdy složka/interní disk, mimo UI vlákno |
| Disky na hlavním vlákně | `statfs` cílů při každém překreslení, skenování karet | vše mimo UI vlákno |
| Nastavení | `AppSettings` přes syntetizovaný `Codable` — nový klíč by smazal celé nastavení | odolné načítání po sekcích |
| HW diagnostika | není | USB topologie, rychlosti linek, doporučení, měření, řetěz a brzda při kopii |
| Automatické testy | žádné | 400+ xUnit testů |

## 3. Rozsah

Každá položka: co uživatel uvidí → chování → okrajové případy → soubory Macu → Windows reference.
Texty v uvozovkách jsou **závazné a shodné s Windows** (kromě míst, kde je výslovně uvedena Mac varianta).

### 3.1 Kontroly před kopií a report (B1–B4)

**B1 — report při zrušeném ověření.** Když `verified == false` (ověření vypnuté nebo zrušené), je soubor v pořádku
ve stavu `verified` **i** `written`; „kartu lze vysunout“ (`safeToEject`) jen při `verified == true`.
Platí pro `OffloadReport.failedFiles`, `filesOK`/`filesFailed` v `build` a pro výběr cíle pro analýzu klipů
(`goodDest` v `OffloadJob`). Dnes Mac při zrušeném ověření vypíše už ověřené soubory jako chybné.
- Mac: `Sources/OffloadKit/Report.swift` (`OffloadReport.isGood`, `failedFiles`, `build`), `Job.swift` (`goodDest`).
- Windows: `windows/src/Offload.Core/OffloadReport.cs` (`FailedFiles`, `Build`), `OffloadJob.cs` (`Good`).

**B2 — zdroj, který je sám offload.** Zdroj, jehož nejvyšší úroveň obsahuje *složku* `_REPORT`, `_PROXY` nebo
`ascmhl` (bez ohledu na velikost písmen), se odmítne:
`Source looks like an earlier offload (has _REPORT/_PROXY/ascmhl) — copy its parent card instead, or remove these folders`.

**B3 — dvakrát zadaný cíl.** Dva cíle, které jsou po normalizaci stejné, se odmítnou i s `--allow-same-device`:
`Destination given twice: <cesta>`. Normalizace na Macu: `resolvingSymlinksInPath()` → `standardizedFileURL.path`
→ bez koncových `/` → malá písmena (APFS/HFS+ jsou ve výchozím stavu case-insensitive, stejně jako NTFS).

**B4 — kontrola voleb** ještě před čímkoli jiným:
- `chunkSize` > 0, násobek 4096, nejvýše 1 GiB → jinak `ChunkSize must be a positive multiple of 4096 and at most 1 GiB (got N)`,
- `bufferCount` ≥ 2 → jinak `BufferCount must be at least 2 (got N)`,
- datum (je-li zadané) přesně `RRRR-MM-DD` (regex `^\d{4}-\d{2}-\d{2}\z` — `\z`, aby neprošlo `2026-10-04\n`)
  → jinak `Date must be YYYY-MM-DD (got "…")`.

Pořadí kontrol jako na Windows: volby → datum → zdroj existuje → aspoň jeden cíl → duplicitní cíl → zdroj je
offload → stat zdroje → pro každý cíl (existuje, zapisovatelný, stat, svazek zdroje, sdílený svazek) → scan →
místo. Hlášky preflightu zůstávají anglicky (jako dnes na Macu i na Windows; CLI je anglické).

Testovací přepínače (jen pro testy, výchozí `false`/`nil`): `OffloadOptions.allowSourceVolume` (dočasné složky
testů leží na stejném svazku jako „karta“) a `OffloadOptions.beforeFileCopy: ((Int) -> Void)?` (deterministický
bod pro zrušení). Windows má totéž (`AllowSourceVolume`, `BeforeFileCopy`).

- Mac: `Engine.swift` (`OffloadOptions`, `OffloadError.isPreflight`, `OffloadEngine.prepare`, `normalizedRoot`, `run`).
- Windows: `OffloadEngine.cs` (`Prepare`), testy `PreflightTests.cs`.

**Neplatí pro Mac** (Windows-only podle ROZDILY): kolize velikosti písmen, názvy nepovolené ve Windows,
soubor ≥ 4 GB na FAT32, fallback na buffered I/O.

### 3.2 Zrušení úlohy a souběh

- Úloha zrušená **dřív, než začala** (čeká ve frontě): `status().phase == .cancelled` hned (ne „Čeká“ navždy);
  `runCopy()` po zrušení nic nepřipraví ani nezkopíruje a vrátí `false`; fronta ji ohlásí jako dokončenou
  (`jobFinished`), takže „Vyčistit hotové“ ji odebere a ukončení aplikace na ni nečeká.
- Zrušená běžící úloha nikdy nepřejde z dokončené fáze zpět do běžící (test s `beforeFileCopy`).
- `OffloadJob.plan` a `report` se zapisují na pracovním vlákně a čtou na hlavním — dnes bez zámku (datový závod
  nad strukturou s poli). Nově za zámkem úlohy.
- Mac: `Job.swift` (`OffloadJob`: `started`, `status()`, `runCopy()`, `plan`/`report`, nový `init(config:options:)`),
  `OffloadApp/AppModel.swift` (`start()` — zrušená čekající úloha volá `jobFinished`).
- Windows: `OffloadJob.cs` (`Status`, `RunCopy`, volatile `Plan`/`Report`), `Desktop/JobQueue.cs` (`CopyLoop`).

### 3.3 Tři snímky na klip (spec Windows 6.5a)

Uživatel uvidí: v HTML/PDF reportu v řádku klipu tři náhledy vedle sebe (první, prostřední, poslední frame),
pod každým popisek: TC (`01:00:02:00`), bez platného TC číslo framu (`#50`). Klip bez známé délky nebo
o jednom framu má jen první snímek.

Pozice (`ProxyRenderer.framePositions(_ info: ClipInfo) -> [FramePosition]`, čistá funkce):
- `fps = Timecode.fps(info.frameRate)`; bez `duration > 0` nebo `fps` → jen první.
- `frames = info.frames ?? round(duration × fps)`; `frames ≤ 1` → jen první.
- první = frame 0; prostřední = `frames / 2` (je-li > 0); poslední = `frames − 1` (je-li > prostřední), označený `fromEnd`.

Vytažení snímku (`ProxyRenderer.thumbnails(...) -> [Thumbnail]`):
- **video (ffmpeg):** vstupní seek `-ss` *před* `-i`, bez `-hwaccel` (náhled je jeden frame; software dekódování je
  deterministické). Čas (`seekSeconds`, formát `%.6f`):
  - prostřední frame se hledá o **půl framu dřív**: `(frame − 0,5) / fps` — přesný čas zaokrouhlený nahoru by skočil
    na další frame (23,976 / 24 / 29,97 fps) — *oprava A1 z Windows*;
  - poslední frame: seek na `max(0, čas − 1 s)` a dekódování **do konce video stopy** s `-map 0:v:0 … -update 1`
    (každý dekódovaný frame přepíše JPEG, zůstane poslední). Nikdy relativně ke konci kontejneru (`-sseof`), který
    je později, když je zvuk delší než obraz — *oprava A3*;
  - ostatní: `-frames:v 1`.
- **BRAW:** `braw-decode --start N --count 1`, kde `N = round(sekundy × fps_braw)`, omezeno na `0…frames−1`.
  Mac **zaokrouhluje** (Windows ořezává — viz nález v oddíle 7).
- **R3D / ARRIRAW:** při analýze se snímky nedělají (dekodér by jen hodil chybu „thumbnail from proxy“); vezmou se
  z hotové proxy (bez LUT, už je v proxy). Bez proxy R3D/ARRI náhledy nemají — stejně jako Windows.
- Filtr: `scale=-2:180:flags=bicubic` → (LUT: `format=rgb48le,lut3d=file=<tempDir>/look.cube:interp=tetrahedral`)
  → `format=yuvj420p`, `-q:v 5`. Na Macu zůstávají absolutní cesty v `tempDir` (`NSTemporaryDirectory()` neobsahuje
  `:` ani `\`; Windows kvůli `C:\` přešel na relativní jména — na Macu to není potřeba).
- **Zrušitelné** (`isCancelled`): zrušení mezi pozicemi i během ffmpeg → klip prostě nemá náhledy, žádná chyba
  — *oprava A4*. Chyba jedné pozice nepřipraví o ostatní; když se nepovede žádná, vyhodí se poslední chyba.
- Burn-in názvu klipu v proxy: `drawtext=…:textfile=…:expansion=none:…` — `%` v názvu by jinak byl expanze
  drawtextu („Stray %“) a render by spadl — *oprava A2*.

Datový model: `ClipEntry.thumbnail: Data?` → `ClipEntry.thumbnails: [Thumbnail]`,
`public struct Thumbnail: Equatable { jpeg: Data; label: String }`. **Do JSON se nezapisují** (`CodingKeys` beze změny).
HTML: `<div class="thumbs"><figure><img class="thumb" …><figcaption>TC</figcaption></figure>…</div>`, CSS
`.thumbs{display:flex;gap:4px}.thumbs figure{margin:0}.thumbs figcaption{font:10px ui-monospace,Menlo,monospace;color:var(--muted);text-align:center}`,
v tisku (PDF) `.thumbs figure img{width:96px;height:54px}`; bez snímků `<div class="nothumb"></div>` jako dnes.

- Mac: nový `Sources/OffloadKit/Timecode.swift`; `Proxy.swift` (`FramePosition`, `framePositions`, `label`,
  `seekSeconds`, `thumbnails`, `frameSource(…, forRender:)`, `burnIn`, odstranit `thumbnail`), `RawBackends.swift`
  (`BRAWBackend.startFrame`), `Report.swift` (`Thumbnail`, `ClipEntry.thumbnails`), `HTMLReport.swift`, `Job.swift`.
- Windows: `ProxyRenderer.cs` (`FramePositions`, `Label`, `SeekSeconds`, `Thumbnails`), `Timecode.cs`,
  `FilterGraph.cs` (`expansion=none`), `HtmlReport.cs` (`AppendClips`, CSS), `OffloadJob.cs`, testy
  `ProxyRendererTests.cs`, `FilterGraphTests.cs`, `StageTwoTests.cs`.

### 3.4 Proxy: hotová proxy přežije selhání kopie na další cíl

Když se proxy vyrenderuje na první dobrý cíl, ale kopie na jiný cíl selže, proxy zůstává `done` (počítá se do
„proxy N“), do `proxyNote` se připíše `kopie na <cardPath> selhala: <chyba>` (více selhání oddělených `; `), do logu
řádek s názvem klipu a cestou; rozpracovaný `.<jméno>.part` na cíli se smaže. Dnes Mac celou proxy označí jako
`failed`. — Mac: `Job.swift` (`analyzeAndRender`, `copyFile`). Windows: `OffloadJob.cs`, test `ProxyCopyFails_RenderedProxyKept`.

### 3.5 České texty a karta úlohy

Nový `Sources/OffloadKit/Desktop/Texts.swift` (port `Desktop/Texts.cs`), množné číslo 1 / 2–4 / ostatní:

| Funkce | Příklady |
|---|---|
| `Texts.cards(n)` | „Vyberte kartu“, „1 karta“, „3 karty“, „5 karet“ |
| `Texts.destinations(n)` | „0 cílů“, „1 cíl“, „2 cíle“, „5 cílů“ |
| `Texts.files(n, bytes)` | „1 soubor · 1.00 KB“, „12 souborů · 2.50 GB“ |
| `Texts.clips(n)` | „1 klip“, „3 klipy“, „5 klipů“ |
| `Texts.safeToEject(n)` | „Všechny soubory jsou na 1 disku a ověřené — kartu lze vysunout.“ / „… na 2 discích …“ |
| `Texts.failed(n)` | „3 soubory selhaly — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.“ |
| `Texts.warnings(n)` | „bez varování“, „2 varování“ |
| `Texts.cancelled` | „Zrušeno — karta není kompletně zálohovaná.“ |

Nový `Desktop/JobProgress.swift` (port `Desktop/JobProgress.cs`): logika dnešního `JobItem.refresh` a textů
`JobCard` přesunutá do OffloadKit, aby šla testovat — rychlosti (vyhlazení 0,6/0,4, reset při změně fáze,
aktualizace jen po > 0,2 s, nikdy záporné), odhad času, `phaseProgress`, `statusText`, `detailText`,
`summaryText`, `etaText`, `tone: CardTone` (`waiting, active, ok, warning, error`), `isError` a `brake` (úzké hrdlo,
jen během kopírování, viz 3.8). Oprava proti dnešnímu Macu: změna fáze uvnitř přeskočené aktualizace (< 0,2 s)
rychlosti vynuluje (dnešní `JobItem` by spočítal záporné rychlosti).

Karta úlohy (SwiftUI `JobCard`):
- hlavička „N souborů · velikost“ přes `Texts.files`, lišta dole „1 karta“/„2 cíle“ přes `Texts.cards`/`destinations`,
- zelený pruh `Texts.safeToEject(cíle)` (beze změny významu),
- **zrušená úloha** (`phase == .cancelled` a ne `safeToEject`): jen oranžový řádek `Texts.cancelled`, **bez**
  červeného „N souborů selhalo…“ a **bez** souhrnu rychlostí (rychlost useknuté kopie klame),
- chyba (`tone == .error`) hraje „Basso“; zrušená úloha **není chyba** (bez zvuku chyby); `NSApp.requestUserAttention`
  zůstává pro každou dokončenou úlohu (Mac obdoba blikání na Windows).

- Mac: `Desktop/Texts.swift`, `Desktop/JobProgress.swift`, `OffloadApp/AppModel.swift` (`JobItem`), `Views.swift`
  (`StartBar`, `JobCard`).
- Windows: `Desktop/Texts.cs`, `Desktop/JobProgress.cs`, `Offload.App/JobItemViewModel.cs`, testy `JobProgressTests.cs`.

### 3.6 Vysunutí karty — jen ta, která se kopírovala

Uživatel uvidí: tlačítko „Vysunout kartu“ jen u úlohy, jejíž karta je ve čtečce; po výměně karty ve stejné čtečce
nebo po vytažení tlačítko zmizí. Vysunutí se odmítne, dokud z karty kopíruje jiná úloha. Ručně přidaná složka,
síťový svazek, interní disk a systémový svazek se nevysouvají nikdy.

- **Identita karty** `CardIdentity.of(path) -> String?` (dotýká se disku → nikdy na hlavním vlákně):
  `"uuid:<UUID>"` z `URLResourceKey.volumeUUIDStringKey` svazku na `path`; když UUID chybí,
  `"created:<epoch>"` z `volumeCreationDateKey`; jinak `nil`. Windows: GUID svazku + sériové číslo FS — Mac
  `volumeUUIDString` je pro APFS/HFS+ UUID svazku a pro FAT/exFAT ho macOS odvozuje ze sériového čísla FS
  (dvě různě naformátované karty → různé UUID). **Ověřit na Macu** (`diskutil info -plist /Volumes/<karta>` →
  `VolumeUUID`, u karty FAT32 i exFAT).
- Identita se zaznamená **na kopírovací frontě těsně před `runCopy()`** (nikdy na hlavním vlákně) a uloží do `JobItem`
  (za zámkem).
- `QueueRules.ejectProblem(copied:current:cardInUse:)`: `current == nil` → „karta není připojená“; jiná/nezjištěná
  identita → „karta v mechanice není ta, která se kopírovala“; jiná úloha ve fázi `waiting/copying/flushing/verifying/reporting`
  ze stejného svazku → „z karty ještě kopíruje jiná úloha — vysuňte ji, až její kopie doběhne“. Fáze 2 (analýza,
  proxy) kartu nedrží — čte jen cíle.
- `QueueRules.cardInDrive(source:copied:scanned:)` (čistá, z posledního skenu svazků) řídí viditelnost tlačítka.
- `CardEject.problem(...)` (čistá funkce nad hodnotami svazku): neexistuje → „karta není připojená“; není
  `volumeIsLocal` → „síťová složka se nevysouvá“; zdroj není kořen svazku → „zdroj je složka, ne celá karta“;
  kořenový FS → „systémový disk nelze vysunout“; interní a zároveň ne vysunutelné ani vyměnitelné → „interní disk
  nelze vysunout“. Spočítá se při skenu svazků (`VolumeItem.ejectProblem`), takže start úlohy disk nečte.
- Vysunutí `CardEject.eject(source)` = `NSWorkspace.shared.unmountAndEjectDevice(at:)` **na pozadí**. Sémantika jako
  na Windows: karta ve čtečce se vysune jako médium (čtečka zůstane připojená, další karta se objeví), USB disk jako
  celé zařízení. Chyba → „Kartu se nepodařilo vysunout: <důvod>“. Dvojklik nevysouvá dvakrát (`ejecting`).
  Ukončení aplikace počká i na rozběhnuté vysunutí.
- Mac: nový `Desktop/QueueRules.swift` (`CardIdentity`, `CardEject`, `QueueRules`), `Desktop/Volumes.swift`
  (`VolumeItem.identity`, `.ejectProblem`), `AppModel.swift` (`eject`, `start`).
- Windows: `Desktop/QueueRules.cs`, `Desktop/CardEject.cs`, `Desktop/JobQueue.cs` (`Runner.SourceIdentity`),
  `Offload.App/MainViewModel.Queue.cs` (`Eject`), testy `QueueRulesTests.cs`, `CardEjectTests.cs`.

### 3.7 GUI: disky mimo hlavní vlákno a další opravy z Windows

- **Svazky:** `Volumes.scan(destRoots:)` (přesun z `AppModel.refreshVolumes`, beze změny pravidel „vypadá jako
  karta“) běží na pozadí; výsledek se použije na hlavním vlákně. Výběr po skenu `Volumes.selectionAfterRefresh` —
  nově zaškrtne jen kartu s **novou cestou** (dnes `VolumeItem` porovnává celou strukturu vč. volného místa, takže by
  se mohla znovu zaškrtnout karta, kterou uživatel odškrtl); zmizelý zdroj nebo svazek s cílem vypadne, ruční složky
  zůstanou.
- **Cíle:** volné místo, FS a existence (`destinationInfo`) se dnes čtou `statfs` při každém překreslení na hlavním
  vlákně (odpojený NAS = zamrzlé okno). Nově cache `destStatus`, obnovovaná na pozadí každých 5 s, po přidání/odebrání
  cíle a tlačítkem obnovit; dokud se cíl zjišťuje, „Spustit offload“ je neaktivní.
- **LUT před startem** (`ProxyPreflight`): když proxy nebo analýza LUT používá a soubor neexistuje → červeně
  „LUT nenalezen: <jméno> — vyberte jiný nebo ho odeberte.“ a start je neaktivní (dnes Mac LUT tiše vynechá a proxy
  vyjdou bez LUT); existence LUT se zjišťuje na pozadí spolu s cíli. Bez ffmpeg/ffprobe jen oranžové
  „ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou.“
- **Název složky karty** z mezer = výchozí název (dnes by z „  “ vzniklo `CARD`). Výchozí název v GUI (zástupný text
  pole „Název složky“, `sourceName`) se počítá bez přístupu na disk: jméno svazku z posledního skenu, u ručně přidané
  složky její název (`Layout.defaultCardName(for:)` čte svazek a volá ho jen engine mimo hlavní vlákno).
- **Zjišťování cílů neuvázne:** kontrola cílů a LUT má vlastní frontu (oddělenou od skenu karet) a nejvýš jednu
  kontrolu v běhu + jednu čekající — zaseknutý `statfs` na NAS nenahromadí kontrolu každých 5 s ani nezablokuje sken karet.
- **Report** se otevře z prvního cíle, kde je (`QueueRules.firstReport`, na pozadí); není-li nikde,
  „Report nenalezen na žádném cíli:“ + seznam cest.
- **Nastavení odolné** (`SettingsStore.decode`): `UserDefaults["settings"]` se čte po sekcích (`destRoots`, `verify`,
  `analyzeClips`, `proxy`, `tools`, `portNames`); vadná sekce dostane výchozí hodnoty, ostatní zůstanou; neznámé klíče
  se ignorují; normalizace jako Windows (`destRoots` bez prázdných a duplicit, výška proxy jen 540/720/1080/2160 jinak
  1080, bitrate 2–50, `frameScale` 0,5–1, záporný `frameAspect` → bez výřezu, prázdný `lutPath` → nil, prázdné názvy
  portů pryč). **Kritické:** bez toho by přidání klíče `portNames` do syntetizovaného `Codable` při prvním spuštění
  nové verze smazalo uložené cíle (chybějící klíč = chyba dekódování = výchozí nastavení).
- **Zavření během kopie:** ⌘Q nebo zavření posledního okna, když běží úloha nebo vysunutí → dotaz
  „Probíhá offload. Opravdu ukončit?“ / „Rozpracované soubory se smažou, hotové zůstanou.“ (tlačítka „Ukončit“,
  „Pokračovat“). Po potvrzení se všechny úlohy zruší, aplikace počká, až se uklidí (smažou `.offload-part`), a pak se
  ukončí sama; další ⌘Q mezitím → `NSAlert` „Ukončuji — čekám, až se zrušené úlohy uklidí.“ (alert, protože hlavní okno už může
být zavřené). Běžící **měření HW** při ukončení: `applicationWillTerminate` zastaví test a počká (nejvýš 10 s), až
`Bench.write` smaže dočasný 1 GB soubor — jako Windows `WaitForHardware`. Při „Pokračovat“ po zavření okna
  běží offload dál bez okna; okno vrátí klik na ikonu v Docku (`applicationShouldHandleReopen`). Dnes Mac proces
  ukončí uprostřed zápisu.
- Mac: `Desktop/Volumes.swift`, `Desktop/AppSettings.swift` (`AppSettings` přesunuto z aplikace, `SettingsStore`,
  `ProxyPreflight`), `AppModel.swift`, `Views.swift`, `OffloadApp.swift`.
- Windows: `Desktop/Volumes.cs`, `Desktop/AppSettings.cs`, `Offload.App/MainViewModel.Setup.cs`,
  `MainViewModel.Queue.cs` (`TryClose`), `MainWindow.xaml.cs`, testy `VolumesTests.cs`, `SettingsStoreTests.cs`.

### 3.8 HW diagnostika — model (platformně nezávislý, port 1:1)

Soubory `Sources/OffloadKit/Hardware/*.swift`, čisté Swift typy, `Codable` (uložení testu), plně testované
na fixturách. Jména typů shodná s Windows (Swift konvence: malé první písmeno vlastností).

- **`UsbLinkSpeed`** `unknown, low, full, high, superSpeed, superPlus` (Comparable). Popisky shodné s Windows:
  „USB 1.0 (1,5 Mb/s)“, „USB 1.1 (12 Mb/s)“, „USB 2.0 (480 Mb/s)“, „USB 3 (5 Gb/s)“, „USB 3 (10 Gb/s a víc)“,
  „neznámá“; praktický strop MB/s 0,15 / 1 / 40 / 450 / 1000. (Mac by uměl 10 vs 20 Gb/s rozlišit, ale kvůli shodě
  textů a doporučení zůstává „10 Gb/s a víc“.)
- **Topologie** `UsbController` → `UsbHub` → `UsbPort` → `UsbDevice` (→ `UsbHub` u hubu); **`UsbConnector`** = jedna
  fyzická zásuvka (USB 2 + USB 3 dvojče sloučené; externí USB 3 hub = dvě poloviny sloučené podle čísla portu),
  `supportsUsb3: Bool?` (`nil` = neznámé, **nikdy** „jen USB 2.0“ bez důkazu), `kind` („USB 3“, „USB-C“,
  „jen USB 2.0“, „USB-C jen USB 2.0“, „“ + „ (interní)“), `label` unikátní („port 6“, „port 7 › 2“, s více řadiči
  „<řadič> · port 3“, stejně pojmenované řadiče „(1)“, „(2)“), `altIds`. Algoritmus `Connectors.build` 1:1
  s `UsbTopology.cs`.
- **`PortLabels`** — jediné místo, které pojmenuje zásuvku (řádek disku, seznam portů, doporučení, řetěz):
  vlastní název uživatele (`portNames[id]`, i pod `altIds`), jinak `label`.
- **`DriveLink`** (`root`, `bus: StorageBus`, `product`, `usb: UsbLink?`), `connection` („USB 3 (5 Gb/s), port 6“
  nebo „SATA“), `describe` („WD Elements 25A2 — USB 3 (5 Gb/s), port 6“). `StorageBus` jako Windows + **`thunderbolt`**
  (Mac: externí PCIe/NVMe přes Thunderbolt, popisek „Thunderbolt“; hwtest.json není sdílený formát).
  Popisky sběrnic: „NVMe“, „SATA“, „SD čtečka“, „virtuální disk“, „síťový disk“, „USB (port nezjištěn)“, „SCSI“,
  „SAS“, „RAID“, „Thunderbolt“, „jiné připojení“, „neznámé připojení“.
- **`Advisor.advise`** — texty 1:1 s `Advisor.cs` (zařízení pod svými možnostmi: port jen USB 2.0 / kabel /
  neznámý port; sdílený řadič podle **identity**, ne jména; rada rozdělit čtečku a disky na různé řadiče; sdílený hub;
  odhad rychlosti offloadu „Odhad rychlosti offloadu s tímto zapojením: N MB/s (brzdí …).“; „brzdí disk, ne port“;
  volné rychlé porty; porty jen USB 2.0), seřazeno varování → info → tip (stabilně).
- **`ChainView`** — řetěz pro kartu úlohy: role, produkt, připojení, `% linky`, součet na sdíleném řadiči
  („Řadič X: N MB/s“), „Úzké hrdlo: …“ (s „— linka je plná (…)“ při > 85 %).
- **`BrakeMeter`** — kdo brzdí, **podle zásoby nezapsaných bloků (lag), ne podle okamžité rychlosti**: engine běží
  v lockstepu (jeden pool `bufferCount × chunkSize`), takže karta i disky mají stejný průměrný pás a krátkodobé
  rychlosti ukazují jen kvantování po blocích. `lag_i = readBytes − writtenBytes[i]`; brzdí zapisovač s největším
  průměrným lagem v okně 4 s, je-li ≥ min(2 bloky, půl poolu) nebo za okno o tolik narostl; jinak brzdí karta. Verdikt
  až po půl okna. 1:1 s `BrakeMeter.cs` vč. testů.

### 3.9 HW diagnostika — macOS technika (místo Win32)

Vše v `Sources/OffloadKit/Hardware/`: `IORegistry.swift` (`IOEntry`), `MacUsbScanner.swift` (`UsbScanner`),
`MacDriveLinks.swift` (`DriveLinks.resolve`). Linkuje `IOKit` a `DiskArbitration` (`linkerSettings` v Package.swift).
Žádný přístup nevyžaduje root ani entitlement (čtení IORegistry a DiskArbitration je pro běžný proces povolené;
aplikace není sandboxovaná).

**Topologie USB (IOService plane):**
- Řadiče: `IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("AppleUSBXHCI"))` (záloha
  `"AppleUSBHostController"`, když první nic nenajde). Identita řadiče = `IORegistryEntryCopyPath(…, kIOServicePlane)`
  (stabilní mezi starty, na rozdíl od registry ID). Název: „USB řadič <jméno v registru>“ (např. „USB řadič XHC1“,
  „USB řadič usb-drd1“), s „ (Thunderbolt)“ jen u řadiče, který je `IOPCIDevice` a má mezi předky třídu obsahující
  `Thunderbolt` (dok, Intel TB řadič) — vestavěné řadiče Apple Silicon příponu mít nesmí (ověřit `ioreg -t`).
  Na Apple Silicon má každý USB-C/Thunderbolt port vlastní řadič (`usb-drd*`) — „sdílený řadič“ tam prakticky
  nenastane, rada „rozdělte na různé řadiče“ se tím přirozeně neobjeví.
- Kořenové porty = děti řadiče, které jsou porty (`IOObjectConformsTo(…, "AppleUSBHostPort")`, záloha: název třídy
  končí na `Port`). **Porty hubu** nejspíš nevisí přímo pod `IOUSBHostDevice` hubu, ale pod jeho ovladačem
  (`AppleUSB20Hub`/`AppleUSB30Hub`…): skener proto bere porty z dětí i vnoučat (dítě, které není port ani zařízení),
  a u disku je „vlastník“ portu první zařízení nebo řadič nad portem (neověřeno — oddíl 9). Druh portu z třídy/jména: `AppleUSB30XHCIPort`, `AppleUSB30XHCITypeCPort`, `AppleUSB30HubPort`,
  jméno `SSxx`/`…-ss@` → USB 3; `AppleUSB20…`, `HSxx`/`…-hs@` → USB 2; jinak neznámý. Číslo portu: vlastnost `port`
  (Data, little-endian). Typ konektoru: `UsbConnector` (hodnoty ACPI `_UPC`: 9/10/11 = USB-C, 255 = interní/proprietární;
  chybí → viditelný, ne USB-C).
- **Párování USB 2 a USB 3 poloviny jedné zásuvky:** podle společného klíče `UsbCPortNumber` (USB-C porty);
  další kandidáty určí ověření na Macu (viz oddíl 9). Řadič/hub s **právě jedním** USB 2 a jedním USB 3 portem
  (Apple Silicon `usb-drd*`: jedna USB-C zásuvka na řadič) se spáruje i bez klíče — jinak by se každá USB-C zásuvka
  ukázala dvakrát. Hodnoty `port` nemusí být v rámci řadiče unikátní (HS i SS port mohou mít `port` = 1): skener
  opakovaná čísla přečísluje (+1000), takže id i párování sedí; popisek bere číslo USB 2 poloviny. **Bezpečné chování bez párovacího klíče:** má-li hub/řadič
  porty obou druhů a USB 2 port nemá dvojče, jeho `supportsUsb3 = nil` (neznámé) — aplikace nikdy neoznačí polovinu
  USB 3 zásuvky jako „jen USB 2.0“. Hub jen s USB 2 porty → `false`. Párování je čistá funkce `UsbScanner.pair`
  (testovaná).
- **Dvojčata přes řadiče (ověřeno úkolem 14 na MacBook Pro 2019 Intel, macOS 26):** USB 2 poloviny všech čtyř USB-C
  zásuvek visí na řadiči PCH `XHC1`, USB 3 poloviny na řadičích Thunderbolt `XHC2`/`XHC3`. Obě poloviny se navzájem
  jmenují cestou v registru (`UsbProtocolCompanion (3.x)` na USB 2 portu, `(2.0)`/`(1.x)` na USB 3 portu);
  `UsbCPortNumber` má jen USB 3 polovina, nepáruje tedy nic. Skener páruje podle těchto cest (oboustranně), USB 2
  polovinu s dvojčetem na jiném řadiči zařadí do kořenového hubu řadiče její USB 3 poloviny (jedna zásuvka, id
  `<USB 3 řadič>#<číslo USB 2 portu>`); řadič bez vlastních zásuvek se nevypíše. Provoz disku jde dál přes skutečný
  řadič nad zařízením (`UsbLink.controllerId`). U externího USB 3 hubu nemají porty polovin žádný údaj o dvojčeti —
  `usbLink` je páruje stejně jako seznam portů (stejné číslo na druhé polovině hubu), takže disk na USB 2 polovině
  USB 3 hubu nedostane „jen USB 2.0“.
- Zařízení = dítě portu, které je `IOUSBHostDevice` (záloha `IOUSBDevice`). Vlastnosti: název `USB Product Name`
  (záloha `kUSBProductString`, jinak jméno v registru), `idVendor`, `idProduct`, `bcdUSB`, `locationID`,
  `bDeviceClass` (9 = hub → rekurze do jeho portů, max. hloubka 6). **Rychlost linky:** `UsbLinkSpeed` (bit/s:
  1,5 M / 12 M / 480 M / 5 G / 10 G / 20 G) když existuje, jinak `Device Speed` (`kUSBDevicePropertySpeed`; 0 low,
  1 full, 2 high, 3 super, 4 SuperSpeedPlus, 5 SuperSpeedPlus by 2). **Schopnost:** `bcdUSB ≥ 0x0300` → USB 3 (5 Gb/s),
  `≥ 0x0200` → USB 2.0; nikdy méně než linka. Omezení: zařízení 10 Gb/s běžící na 5 Gb/s se nepozná (Mac bez čtení
  BOS deskriptoru schopnost SuperSpeedPlus neprozradí) — dokumentováno v ROZDILY.
- Identity: hub zařízení a zařízení `loc:%08x` z `locationID` (stabilní pro danou zásuvku), kořenový „hub“ = cesta
  řadiče; `UsbConnector.id = "<hubId>#<číslo portu>"` (stejný tvar jako Windows). Tyto id jsou klíče `portNames`.

**Kde sedí svazek (`DriveLinks.resolve(path)`):**
1. `statfs(path)` → `f_mntonname` (kořen), `f_mntfromname` (`/dev/disk4s1`), `f_fstypename`. Síťové FS
   (`smbfs`, `afpfs`, `nfs`, `webdav`, `ftp`, `cifs`) → `StorageBus.network`, produkt = `f_mntfromname`
   (`//uzivatel@server/sdileni`).
2. DiskArbitration: `DASessionCreate`, `DADiskCreateFromBSDName(… "disk4s1")`, `DADiskCopyDescription` →
   `kDADiskDescriptionDeviceProtocolKey` (`USB`, `SATA`, `PCI-Express`, `Secure Digital`, `Virtual Interface`, `Apple Fabric` …),
   `DeviceVendorKey` + `DeviceModelKey` = produkt, `DeviceInternalKey`. `DADiskCopyIOMedia` → `IOMedia`.
3. Předci `IOMedia` v IOService plane (přes APFS: svazek → kontejner → fyzické úložiště → disk → ovladač → zařízení):
   je-li mezi nimi `IOUSBHostDevice` → USB; třída obsahující `Thunderbolt` → `thunderbolt`; jinak podle protokolu
   (`PCI-Express`/`Apple Fabric` + třída obsahující `NVMe` → `nvme`, `SATA` → `sata`, `Secure Digital` → `sd`,
   `Virtual Interface`/`Disk Image` → `virtual`, `SAS` → `sas`, jiné → `other`, nic → `unknown`).
4. USB: zařízení = první `IOUSBHostDevice` nad médiem; **UAS** = mezi médiem a zařízením je třída obsahující
   `IOUSBAttachedSCSI`; port = rodič zařízení; hub portu = rodič portu (řadič nebo hub zařízení); huby/doky mezi
   zařízením a řadičem → `UsbLink.hubs`; řadič = první předek `AppleUSBXHCI`/`AppleUSBHostController`.
   `connectorId = "<hubId>#<port>"`, `connectorPort` = číslo USB 2 dvojčete, je-li spárované, `connectorUsb3` ze
   stejné párovací funkce jako topologie (konzistence řádku disku a seznamu portů).
5. Nikdy nehází: cokoli nečitelného → `unknown`/`nil`.

**Měření:** stávající `Bench.read`/`Bench.write` (F_NOCACHE, F_FULLFSYNC) — nově zrušitelné po každém bloku
(16 MB), dočasný soubor `.offload-bench-<pid>` se smaže i při zrušení; `Bench.read` u souboru kratšího než blok
skončí (jako Windows).

### 3.10 Test HW a uložení výsledku

`HwTest.run(cards:destinations:measure:bytesPerDevice:portNames:rolesByDrive:isCancelled:)` 1:1 s `HwTest.cs`:
role „Karta“ / „Karta 1…“, „Cíl 1…“; bez rolí (CLI bez voleb, GUI bez karet a cílů) všechny svazky podle jména
svazku; měření: karta = čtení až 1 GB, cíl = zápis + čtení 1 GB dočasného souboru, **cíl na svazku karty se neměří
ani nezapisuje** (cíl, který ještě neexistuje, patří svazku nejbližší existující nadřazené složky — nová složka
na kartě je karta; varování „Cíl 1 je na stejném svazku jako Karta — na kartu se nezapisuje, zápis se neměří; zvol
jiný cílový disk.“); zrušení přeruší i právě měřené zařízení (nezměřeno) a zbylá přeskočí. Výsledek
`~/Library/Application Support/Offload/hwtest.json` (atomicky přes `IO.writeFileDurably`); načtení poškozeného nebo
neúplného souboru → `nil`. Rychlý test nikdy nepřepíše uložené měření (jen souhrn v GUI).

### 3.11 CLI

- **`offload ports [--measure] [--card X]… [--dest Y]… [--gb 1]`** (ArgumentParser subcommand `Ports`), výstup česky
  jako Windows: „Připojení:“ (role, kořen, popis), „USB porty (<řadič>):“ (řádky `PortLabels.listRow`), „Měření:“,
  „Doporučení:“ (`!` varování, `i` info, `·` tip). Cíl na svazku karty → chyba „cíl X je na stejném svazku jako
  karta Y — na kartu se nikdy nezapisuje“; `--measure` bez rolí → „--measure needs --card and/or --dest“.
  Ctrl+C při měření: „Přerušeno — zastavuji měření a mažu dočasný soubor…“, výpis toho, co se stihlo,
  „Měření přerušeno — výsledek se neukládá.“, exit 1. Jinak výsledek uloží (selhání uložení jen varování).
- **`offload copy`:** po „→ cíl“ řádky „  Připojení:“ + „    card = Karta: …“, „    D1 = Cíl 1: …“ a varování
  z `Advisor` („  ! …“) — informativní, chyba HW nikdy nezastaví kopii. Stavový řádek při kopírování přidá
  **před název souboru** „brzdí: D1  “ nebo „brzdí: card (linka plná)  “ (úzký terminál ořízne konec řádku).
- Mac: `Sources/offload/main.swift` (`Ports`, `Copy.run`, `ProgressDisplay`).
- Windows: `Offload.Cli/PortsCommand.cs`, `ProgressDisplay.cs`, `CopyCommand.cs`.

### 3.12 GUI: Hardware na vyžádání, řetěz na kartě úlohy

- **Menu `Nástroje` → `Prověřit USB porty…` se zkratkou ⌘U.** Proč ⌘U: odpovídá Ctrl+U na Windows (stejná
  dokumentace a návyk), v aplikaci bez menu Formát (SwiftUI `WindowGroup` ho nepřidává) není ⌘U obsazené
  (jinde je to „Podtrhnout“), a nekoliduje s systémovými ⌘H/⌘M/⌘Q/⌘W/⌘, ani s ⌘↩ (Spustit offload).
  Při startu se test **nespouští** (spec 8a.2 v platném znění).
- **Okno „Hardware — USB porty a disky“** (SwiftUI `Window(id: "hardware")`, macOS 13+, jediná instance — otevřené
  okno se jen přenese dopředu): při otevření rychlý test (stav „Zjišťuji připojení…“), sekce „Připojení“,
  „Doporučení“, „Měření“ (tlačítko „Změřit rychlost…“ → dotaz „Změřit rychlost?“ s textem „Z karet se jen čte (až
  1 GB z každé). / Na cílové disky se zapíše a přečte dočasný soubor 1 GB a hned se smaže: / <cíle> / Podle rychlosti
  disků to může trvat několik minut.“; během kopie neaktivní s „Během kopírování nelze měřit — měření zapisuje 1 GB
  na cílové disky.“; bez karty a cíle „Pro měření vyberte kartu nebo přidejte cíl.“), „USB porty“ (skupiny podle
  řadiče, vlastní název portu, přepínač „Zobrazit interní porty“, vysvětlivka „macOS neví, kde je port na počítači
  fyzicky — pojmenujte si ho (např. „přední levý“), název se pak použije všude.“), tlačítka „Prověřit znovu“,
  „Zastavit“ („Zastavuji…“ do skutečného konce), „Zavřít“. Stavové texty: „Měřím rychlost… (může trvat několik
  minut)“, „Čekám, až doběhne předchozí test…“, „Měření přerušeno — zobrazeno, co se stihlo změřit.“, „Test se
  nepodařil: …“. Vždy jen jeden HW test naráz (sériová fronta); zavření okna test zastaví (měření smaže dočasný soubor);
  okno otevřené znovu, dokud předchozí test dobíhá („Zastavuji…“), ukáže „Čekám, až doběhne předchozí test…“ a rychlý
  test spustí, jakmile předchozí skončí (jako Windows).
- **Sekce „Hardware“** v levém panelu (pod „Dekodéry“): „Poslední test d. M. yyyy H:mm · bez varování“ /
  „… · 2 varování“ / „Zatím neproběhl“ a tlačítko „Prověřit…“.
- **Karta úlohy během kopírování a ověřování:** pod řádkem rychlostí řetěz z `ChainView.lines` (např.
  „Karta SanDisk reader — USB 3 (5 Gb/s), port 2  312 MB/s  69 % linky“, „Řadič USB řadič XHC1: 497 MB/s“,
  „Úzké hrdlo: Cíl 1 WD Elements“). Řetěz se zjistí jednou, na pozadí, když úloha poprvé hlásí kopírování; brzda
  (`BrakeMeter`) jen při kopírování. Řádky se nemění, dokud se nemění text (stabilní `ForEach`).
- Názvy portů se ukládají do `AppSettings.portNames` (stejný klíč jako Windows settings.json).
- Mac: nový `OffloadApp/HardwareModel.swift`, `OffloadApp/HardwareView.swift`; `OffloadApp.swift` (scéna `Window`,
  `CommandMenu`), `Views.swift` (`HardwareSection`, `JobCard`), `AppModel.swift` (`JobItem` řetěz, `hardware`).
- Windows: `Offload.App/HardwareViewModel.cs`, `HardwareWindow.xaml`, `MainViewModel.Hardware.cs`, `JobItemViewModel.cs`.

## 4. Mimo rozsah

- ~~**LUTy Log → Rec.709 vestavěné**~~ — původně mimo rozsah (plán 5); Windows je mezitím má, takže jsou v rozsahu:
  oddíl „Doplněk 2026-10-05“ (D.3), úkoly 21–25. Totéž platí pro složku pro proxy, barvy proxy, ikonu a hlídání ffmpeg.
- Windows-only z podstaty: kontroly NTFS/exFAT velikosti písmen, nepovolených jmen a FAT32 ≥ 4 GB, buffered I/O
  fallback a `"unbuffered": false`, atribut skrytý u `.offload-part`, Windows junk soubory, CLI v `bin\`, toast /
  zvuk + blikání v hlavním panelu (Mac má `NSSound` + `requestUserAttention`), Ctrl+Enter (Mac má ⌘↩),
  „Průzkumník“ (Mac „Finder“), rada „Doporučeno NTFS“ (Mac „APFS“), přetažení složky na exe, `--no-hw-prompt`,
  `WindowFit`, `VolumeWatcher` (`WM_DEVICECHANGE`), přeskočení znovupřipojení vysunuté karty rescanem (macOS po
  `unmount` nic znovu nepřipojí), uložení názvu portu po Esc (SwiftUI `TextField` zapisuje průběžně).
- **`proxyEncoder` v JSON:** Mac ho nezapisuje (enkodér je pevně VideoToolbox / `prores_ks`) a při čtení ignoruje.
- Escapování `/` (`\/`) a mezera před dvojtečkou v Mac JSON (`JSONEncoder.prettyPrinted`) — sémanticky shodný JSON,
  neměníme.
- Náhledy R3D/ARRI bez proxy (odloženo i na Windows), cancel `ffprobe`, chybějící selhání kopie proxy v souhrnu CLI
  (otevřené i na Windows).
- Port `braw-decode` (Mac ho má), změny PDF (WebKit zůstává).

## 5. Formáty na disku

Beze změny formátu (FORMATY.md platí): struktura složek, `.xxh64`, MHL, klíče JSON. Konkrétně:

- `offload_report.json`: klíče `cardName, clips, copyFinished, date, destinations, files, finished, hashAlgorithm,
  [proxy], skipped, source, sourceFilesystem, started, tool, totalBytes, totalFiles, verified, version`, seřazené.
  Náhledy (`thumbnails`) se **nezapisují** — `ClipEntry.CodingKeys` zůstává `clip, info, probeError, proxyStatus,
  proxyPath, proxyNote, proxySeconds`. B1 mění jen počítání `filesOK`/`filesFailed` při `verified == false`
  (shodně s Windows), ne klíče.
- Čtení reportu z Windows (`unbuffered`, `proxyEncoder`, `version` `0.1.0-win`) — `JSONDecoder` neznámé klíče
  ignoruje; pokryto testem.
- `version` v reportu: Mac zvedne na **`0.2.0`** v posledním úkolu (z reportu je vidět, která verze kopírovala);
  FORMATY.md tabulka a ROZDILY.md se upraví.
- Nastavení (`UserDefaults` klíč `settings`, JSON) dostane `portNames` (`{"<connector id>": "název"}`) — stejný
  klíč jako Windows `settings.json`; id se mezi platformami liší (nepřenáší se).
- `hwtest.json` je lokální soubor každé platformy, ne sdílený formát.

## 6. Opravy ze závěrečných kontrol Windows — co platí pro Mac

| Windows oprava (VYVOJ-WINDOWS) | Mac | Důvod |
|---|---|---|
| Plán 1: konce řádků `\n` v JSON/TXT/HTML | neplatí | Mac už píše `\n` |
| Plán 1: nulování přesahu nebufferovaného zápisu | neplatí | jen `FILE_FLAG_NO_BUFFERING` |
| Plán 1: report při zrušeném ověření (B1) | **platí** | stejná chyba v `Report.swift` (3.1) |
| Plán 1: zdroj, který je offload (B2) | **platí** | Mac nekontroluje (3.1) |
| Plán 1: dvakrát zadaný cíl (B3) | **platí** | Mac odmítne jen sdílený svazek, a to jde obejít `--allow-same-device` (3.1) |
| Plán 1: kontrola voleb a data (B4) | **platí** | `--chunk-mb 0` nebo `--date ../x` dnes projde (3.1) |
| Plán 1 (odloženo): bezpečnost vláken `OffloadJob` | **platí** | `plan`/`report` bez zámku (3.2) |
| Plán 1 (odloženo): JSON fixture z Macu | **platí** | test tvaru JSON a čtení Windows JSON (5) |
| Plán 2: přesný prostřední snímek (A1) | **platí** | 3.3 |
| Plán 2: `%` v názvu klipu (A2) | **platí** | Mac `burnIn` bez `expansion=none` |
| Plán 2: poslední snímek u delšího zvuku (A3) | **platí** | 3.3 |
| Plán 2: enkodér v reportu (`proxyEncoder`) | neplatí | Mac enkodér nevolí; klíč volitelný |
| Plán 2: zrušitelné náhledy (A4) | **platí** | Mac `thumbnail` není zrušitelný |
| Plán 2: R3D/ARRI bez falešné chyby náhledu | **platí** (zjednodušeně) | Mac chybu polyká `try?`; analýza je teď přeskočí výslovně |
| Plán 2: proxy zůstane hotová při selhání kopie | **platí** | 3.4 |
| Plán 2: test HEVC (`hvc1`) | **platí** | test v úkolu 7 |
| Plán 2: záložní enkodér, `tools\bin` jen pro vývoj, shimy v PATH | neplatí | Windows enkodéry / build skript |
| HW: jeden popisek portu všude (`PortLabels`) | **platí** | 3.8 |
| HW: řadiče podle identity, ne jména | **platí** | identita = cesta v IORegistry |
| HW: USB 3 hub = dvě poloviny sloučené | **platí** | macOS ukazuje USB 3 hub také jako dvě zařízení |
| HW: neznámý typ portu ≠ „jen USB 2.0“ | **platí** | o to důležitější: Mac párovací klíč nemusí mít (3.9) |
| HW: síťový disk, české názvy sběrnic | **platí** | `StorageBuses.label` |
| HW: sdílený hub + rada rozdělit na různé řadiče | **platí** | `Advisor` |
| HW: „brzdí“ podle lagu, před názvem souboru | **platí** | `BrakeMeter`, `ProgressDisplay` |
| HW: cíl na svazku karty se neměří/nepřijme | **platí** | `HwTest`, `offload ports` |
| HW: Ctrl+C dokončí/zruší měření a smaže dočasný soubor | **platí** | `Bench` zrušitelný |
| HW: štítek písmene disku u portu | **platí** (jméno svazku) | `PortLabels.listRow` |
| HW: sken pokračuje při chybě jednoho řadiče | **platí** | `UsbScanner.scan` nikdy nehází |
| HW (odloženo): `HwTest.Load` s chybějícími seznamy → nic | **platí** | `Codable` neúplný soubor odmítne → `nil` |
| HW (odloženo): příznak `IsCard` místo prefixu „Karta“ | neplatí | Windows zůstal u prefixu; shoda chování |
| HW (odloženo): atomické uložení hwtest.json | **platí** | `IO.writeFileDurably` |
| HW (odloženo): UAS u složených zařízení | **platí** | UAS z ovladače mezi médiem a zařízením |
| Plán 4: settings.json nikdy nezastaví start | **platí** | `SettingsStore.decode` (3.7) |
| Plán 4: deterministický test zrušení | **platí** | `beforeFileCopy` |
| Plán 4: identita karty, kdy smí „Vysunout“, dvojklik | **platí** | 3.6 |
| Plán 4: vysunutá karta se nepřipojí rescanem | neplatí | macOS nepřipojuje odpojený svazek při výčtu |
| Plán 4: LUT a cíle mimo UI vlákno, každých 5 s | **platí** | 3.7 |
| Plán 4: okna se vejdou na malou obrazovku | neplatí | macOS okna omezuje sám; minimum 940×640 se vejde na 13" |
| Plán 4: měření nejde během kopie | **platí** | `QueueRules.measureBlocked` |
| Plán 4: zrušená úloha bez chybového zvuku | **platí** | `JobProgress.isError` |
| Plán 4: název portu se uloží i po Esc | neplatí | SwiftUI `TextField` ukládá průběžně |
| Plán 4: report z prvního cíle, kde je | **platí** | `QueueRules.firstReport` |
| Plán 4: zavření okna během kopie, čekání na vysunutí, hláška při čekání | **platí** | 3.7 |
| Plán 4: zrušená karta = oranžový text | **platí** | 3.5 |
| Plán 4: asynchronní zjišťování cílů, úklid názvu karty, bezpečné použití skenu | **platí** | 3.7 |
| Plán 4: zrušená čekající úloha → „Zrušeno“ | **platí** | 3.2 |
| Plán 4: výběr karet po připojení/odpojení | **platí** | Mac porovnává celou `VolumeItem` (3.7) |
| Plán 4: hlídání chyb při startu WPF, dialogy, log | neplatí | WPF specifické |

## 7. Nálezy při návrhu

Mac (opraví tento plán): vysunutí nabízené i u ručně přidané složky; `statfs` cílů na hlavním vlákně při každém
překreslení; tichá ztráta LUT, když soubor chybí; zrušená čekající úloha „Čeká“ navždy; datový závod na
`OffloadJob.plan/report`; synthesized `Codable` u `AppSettings` (nový klíč = ztráta nastavení); `copyFile` v `Job.swift`
nechá `.part` při selhání přejmenování; zavření aplikace uprostřed kopie bez dotazu a bez úklidu.

**Windows (jen zaznamenat, Windows se tímto plánem nemění):** `RawBackends.cs` BRAW `Source` počítá start
`(int)(t * fps)`; `t = frame / fps_racionální` a `fps` z `braw-decode` je zaokrouhlené (23,976), takže
`frame × 0,999999` se ořízne na `frame − 1` — prostřední/poslední snímek BRAW je o frame vedle. Mac používá
zaokrouhlení (`BRAWBackend.startFrame`). Návrh: stejná oprava ve Windows plánu 3 (braw-decode). *Stav: Windows opraveno
v plánu 3 (`66412e4`, `BrawBackend.StartFrame`); Mac úkol 6.*

## 8. Testování

Mac dnes nemá žádné automatické testy. Přibude target **`OffloadKitTests`** (`Package.swift` `testTarget`,
`Tests/OffloadKitTests/`, XCTest, `@testable import OffloadKit`). Spouští se `swift test` (vyžaduje plné Xcode;
viz README-START). Testy, které potřebují ffmpeg, se bez něj přeskočí (`XCTSkip`); hledá se v `mac/Tools/bin`,
`~/Library/Application Support/Offload/bin`, `/opt/homebrew/bin`, `/usr/local/bin`.

| Oblast | Soubor testů | Port z Windows |
|---|---|---|
| Layout, formátování (kostra) | `LayoutTests.swift` | `LayoutTests.cs`, `FormatTests.cs` |
| B2–B4 preflight | `PreflightTests.swift` | `PreflightTests.cs` |
| B1, tvar JSON, čtení Windows JSON | `ReportTests.swift` | `ReportTests.cs` |
| Zrušení úlohy, fáze | `JobTests.swift` | `JobProgressTests.cs` (část), `JobTests.cs` |
| Timecode, pozice snímků, seek, burn-in | `TimecodeTests.swift`, `FramePositionTests.swift` | `ProxyRendererTests.cs`, `FilterGraphTests.cs`, `TimecodeEncoderTests.cs` |
| Tři snímky z ffmpeg (ffmpeg) | `ThumbnailTests.swift` | `ProxyRendererTests.cs` (A1–A4) |
| HTML se třemi snímky, fáze 2 (ffmpeg) | `HTMLReportTests.swift`, `StageTwoTests.swift` | `HtmlReportTests.cs`, `StageTwoTests.cs` |
| Texty, průběh karty, brzda | `JobProgressTests.swift`, `BrakeMeterTests.swift` | `JobProgressTests.cs`, `ChainViewTests.cs` (BrakeMeter) |
| Nastavení, LUT, svazky | `SettingsStoreTests.swift`, `VolumesTests.swift` | `SettingsStoreTests.cs`, `VolumesTests.cs` |
| Vysunutí a pravidla fronty | `QueueRulesTests.swift` | `QueueRulesTests.cs`, `CardEjectTests.cs` |
| USB rychlosti, topologie, popisky | `UsbSpeedsTests.swift`, `UsbTopologyTests.swift` | `UsbSpeedsTests.cs`, `UsbTopologyTests.cs` |
| Připojení disků, doporučení, řetěz | `AdvisorTests.swift`, `ChainViewTests.swift` | `AdvisorTests.cs`, `ChainViewTests.cs`, `DriveLinksTests.cs` |
| IOKit/DA (kouřové testy na skutečném Macu) | `MacHardwareTests.swift` | `HardwareFacts.cs`, `UsbNativeTests.cs` |
| Bench, test HW | `BenchTests.swift`, `HwTestTests.swift` | `BenchTests.cs`, `HwTestTests.cs` |
| Doplněk (LUTy, složka pro proxy, barvy, hlídač) | tabulka D.9 | — |
| Doplněk 2 (BRAW: `braw-decode`, LUT podle gamy, poznámky, náhledy pod hlídačem; část jen s knihovnou nebo vzorkem `.braw`) | tabulka D.19 | `BrawCoreTests.cs`, `BrawDecodeTests.cs`, `BrawSampleTests.cs`, `ProxyWatchdogTests.cs` (BRAW) |

Ruční ověření (na Macu, s hardwarem) je v každém úkolu plánu s přesnými kroky; souhrn v README-START.

## 9. Nejistoty API (ověřit na Macu, než se kód považuje za hotový)

| Co | Předpoklad | Jak ověřit |
|---|---|---|
| Třída řadiče | `AppleUSBXHCI` (podtřídy `AppleUSBXHCIPCI`, `AppleT8103USBXHCI` …), báze `AppleUSBHostController` | `ioreg -p IOService -w0 -r -c AppleUSBXHCI -d 1` |
| Třída portu a číslo | `AppleUSBHostPort`, vlastnost `port` = Data LE | `ioreg -p IOService -l -w0 -r -c AppleUSBHostPort \| grep -E '"port"\|class'` |
| Párování HS/SS | `UsbCPortNumber` u USB-C; jinak neznámé → `nil`. **Ověřeno (Intel MBP 2019, macOS 26):** páruje `UsbProtocolCompanion (3.x)`/`(2.0)` (cesta dvojčete, i přes řadiče XHC1 ↔ XHC2/XHC3); `UsbCPortNumber` jen na USB 3 polovině | stejný výpis: hledat vlastnost se stejnou hodnotou u `…20…Port` a `…30…Port` jedné zásuvky (kandidáti `UsbCPortNumber`, `UsbCompanionPortPresent`, `kUSBHostPortPropertyCompanion…`) |
| Typ konektoru | `UsbConnector` (ACPI `_UPC`: 9/10/11 USB-C, 255 interní) | výpis portů |
| Zařízení je dítě portu | ano (`IOUSBHostDevice` pod `AppleUSB…Port`) | `ioreg -p IOService -w0 -r -c AppleUSBHostPort -d 2` |
| Porty hubu pod ovladačem hubu | porty hubu visí pod `AppleUSB20Hub`/`AppleUSB30Hub…` (dítě `IOUSBHostDevice`), ne přímo pod zařízením; kód bere obojí. **Ověřeno:** ano, pod ovladačem; porty hubu nemají údaj o dvojčeti | `ioreg -p IOService -w0 -r -c IOUSBHostDevice -d 3` s připojeným hubem |
| Hodnoty `port` a Apple Silicon | na `usb-drd*` jeden HS + jeden SS port, možná oba `port` = 1; kód přečísluje a spáruje | výpis portů (`ioreg … -c AppleUSBHostPort`) na Apple Silicon i Intel Macu |
| Přípona „(Thunderbolt)“ | jen PCI řadič za Thunderbolt předkem. **Ověřeno (Intel):** XHC2/XHC3 (`AppleUSBXHCITR`) mají mezi předky jen `IOPCIDevice`/`IOPCI2PCIBridge` → bez přípony | `ioreg -p IOService -w0 -t -r -c AppleUSBXHCI` |
| Rychlost linky | `UsbLinkSpeed` (bit/s), záloha `Device Speed` 0–5 | `ioreg -p IOUSB -l -w0 \| grep -E '"(USB Product Name\|UsbLinkSpeed\|Device Speed\|bcdUSB\|locationID)"'`; porovnat se `system_profiler SPUSBDataType -json` (`"speed"`) |
| UAS | třída `IOUSBAttachedSCSI` mezi médiem a zařízením | `ioreg -l -w0 -r -c IOUSBHostDevice \| grep -iE 'AttachedSCSI\|MassStorage'` |
| Protokol disku přes APFS | `DADiskCopyDescription` u `diskNsM` vrací `DeviceProtocol` fyzického disku | `diskutil info -plist /Volumes/X` (`BusProtocol`, `DeviceModel`), a v kódu kouřový test |
| Thunderbolt disk | předek s třídou obsahující `Thunderbolt` (`IOThunderboltPort`, `AppleThunderboltPCIDownAdapter` …); v „Kartách“ se ukáže, jen když macOS hlásí svazek jako externí | `ioreg -p IOService -w0 -l -r -c IOMedia` u Thunderbolt disku; `system_profiler SPThunderboltDataType`; `diskutil info /Volumes/X` (Device Location) |
| UUID svazku FAT/exFAT | `volumeUUIDStringKey` vrací hodnotu | `diskutil info -plist /Volumes/<karta>` (`VolumeUUID`) u FAT32 i exFAT karty |
| `unmountAndEjectDevice` mimo hlavní vlákno | NSWorkspace je thread-safe | ruční test vysunutí (úkol 11) |
| `@Environment(\.openWindow)` v `CommandMenu` | funguje na macOS 13 | ruční test ⌘U (úkol 17) |
| SF Symbol `cable.connector` | existuje na macOS 13 | náhled v Xcode / SF Symbols; jinak `cpu` |

## 10. Mapa změn souborů (Mac)

| Soubor | Úkoly plánu |
|---|---|
| `mac/Package.swift` | 1 (testTarget), 14 (IOKit, DiskArbitration) |
| `mac/Tests/OffloadKitTests/*.swift` (nové) | 1–25 |
| `Sources/OffloadKit/Engine.swift` | 2 |
| `Sources/OffloadKit/Report.swift` | 3, 6, 7, 22, 26 |
| `Sources/OffloadKit/Job.swift` | 3, 4, 7, 19, 20, 22 |
| `Sources/OffloadKit/Timecode.swift` (nový) | 5 |
| `Sources/OffloadKit/Proxy.swift` | 5, 6, 7, 18, 19, 20, 21, 22 |
| `Sources/OffloadKit/RawBackends.swift` | 6, 21, 22 |
| `Sources/OffloadKit/HTMLReport.swift` | 7, 20, 22 |
| `Sources/OffloadKit/Hash.swift`, `Bench.swift` | 15 |
| `Sources/OffloadKit/Desktop/{Texts,JobProgress,AppSettings,Volumes,QueueRules}.swift` (nové) | 8, 9, 10 (`AppSettings` i 20, 21) |
| `Sources/OffloadKit/Hardware/{BrakeMeter}.swift` (nový) | 8 |
| `Sources/OffloadKit/Hardware/{UsbSpeeds,UsbTopology,PortLabels}.swift` (nové) | 12 |
| `Sources/OffloadKit/Hardware/{DriveLinks,Advisor,ChainView}.swift` (nové) | 13 |
| `Sources/OffloadKit/Hardware/{IORegistry,MacUsbScanner,MacDriveLinks}.swift` (nové) | 14 |
| `Sources/OffloadKit/Hardware/HwTest.swift` (nový) | 15 |
| `Sources/offload/main.swift` | 16, 23 |
| `Sources/OffloadApp/AppModel.swift` | 9, 10, 11, 17, 24 |
| `Sources/OffloadApp/Views.swift`, `OffloadApp.swift` | 11, 17 (`Views.swift` i 24) |
| `Sources/OffloadApp/{HardwareModel,HardwareView}.swift` (nové) | 17 |
| `mac/README.md`, `docs/ROZDILY.md`, `docs/VYVOJ-MAC.md`, `docs/FORMATY.md` | 26 (+ řádek ve VYVOJ-MAC v každém úkolu) |
| Nové soubory doplňku (`FilterGraph`, `FFmpegWatch`, `ProxyCopy`, `Luts`, `Media.swift`, `scripts/build-app.sh` …) | 18–25, podrobně D.11 |

## Doplněk 2026-10-05: složka pro proxy, vestavěné LUTy, barvy proxy, ikona, ochrana proti zaseknutí ffmpeg

Windows verze dostala po sepsání tohoto návrhu další funkce, které uživatel chce i na Macu. Doplněk říká co, jak a kde;
implementují ho úkoly **18–25** plánu (dokumentace a verze 0.2.0 se přesunuly z úkolu 18 do úkolu **26**). Platí pravidla
oddílů 1–10 (texty shodné s Windows, výstupy podle FORMATY.md, UI nikdy nečeká na disk, test před kódem).

### D.1 Cíl a úspěch

7. Proxy i náhledy v reportu z log záznamů vypadají jako Rec.709 bez ručního hledání LUTu: aplikace vybere jeden z 11
   vestavěných LUTů podle metadat **každého klipu**; v reportu je u klipu „LUT: … (důvod)“ a v JSON klíč `lut`.
8. Každá hotová proxy se volitelně zkopíruje i do uživatelovy složky `<složka>/<datum>/<karta>/` — nikdy nic nepřepíše.
9. Proxy mají správné barvy v každém přehrávači (BT.709 v legálním rozsahu, označené), náhledy v HTML/PDF taky (JFIF).
10. Zaseknutý ffmpeg nikdy nezablokuje frontu: hlídač 60 s, u hardwarového dekódování jedno opakování softwarově.
11. `Offload.app` má ikonu (sdílenou s Windows).

Windows reference: `windows/src/Offload.Core/ProxyCopy.cs`, `OffloadJob.cs`, `Media.cs` (`ProxySettings`, `ClipInfo.ColorRange`,
`ClipEntry.Lut`), `Luts.cs`, `FilterGraph.cs`, `Encoders.cs` (`Rec709Args`), `ProxyRenderer.cs`, `RawBackends.cs`, `FrameSource.cs`,
`ClipDetector.cs` (`FFProbe`), `HtmlReport.cs`, `Desktop/AppSettings.cs`, `Offload.Cli/CopyCommand.cs`, `LutsCommand.cs`,
`Offload.App/ProxyOptionsViewModel.cs`, `SetupPanel.xaml`, `MainViewModel.Setup.cs`, `MainViewModel.Queue.cs`; návrh LUTů
[`windows/docs/superpowers/specs/2026-10-05-offload-luts-design.md`](../../../../windows/docs/superpowers/specs/2026-10-05-offload-luts-design.md);
sdílené soubory [`shared/luts/`](../../../../shared/luts/README.md) a [`shared/icon/`](../../../../shared/icon/README.md).

### D.2 Složka pro proxy navíc (`proxy.extraFolder`)

Uživatel uvidí: v sekci Proxy pod přepínači řádek **„Kopírovat proxy i do složky“** — název vybrané složky (plná cesta
v nápovědě) nebo „nevybráno“, tlačítko „Vybrat…“ (panel „Vyber složku pro proxy“) a křížek s nápovědou „Nekopírovat do
složky“. V CLI `offload copy … --proxy --proxy-folder <složka>`.

Chování (1:1 s Windows `ProxyCopy.ToExtraFolder` a `OffloadJob`):
- Po vyrenderování proxy na první dobrý cíl a po kopii na ostatní cíle (`copyToAllDestinations`) se proxy zkopíruje do
  `<extraFolder>/<datum>/<karta>/<jméno proxy>.mov` (`<datum>` a `<karta>` jako na cílech — `plan.date`, `plan.cardName`).
- Kopie přes `.<jméno>.part` vedle cíle a přejmenování **bez nahrazení**. Je-li jméno obsazené (soubor i složka), zkusí
  `<jméno>_2.mov`, `_3` … nejvýš do `_1000`; pak chyba `žádné volné jméno (C0001.mov … C0001_1000.mov)`. Nikdy nepřepíše.
- Složka neexistuje (odpojený disk) → `složka není dostupná`; nic se nevytvoří.
- Jakékoli selhání je jen **poznámka u klipu** `kopie do <složka> selhala: <důvod>` (více poznámek oddělených `; `, jako
  „kopie na … selhala“ z oddílu 3.4) a řádek logu; proxy zůstává `done`, úloha neselže.
- Start: nedostupná složka **zablokuje start**, když se proxy renderují — GUI červeně „Složka pro proxy není dostupná: <cesta>“
  (zjišťuje se na pozadí spolu s cíli každých 5 s, po změně a ještě jednou těsně před startem, nejvýš 5 s); CLI s `--proxy`
  `Error: Proxy folder not found: <cesta>`, bez `--proxy` jen `Note: --proxy-folder has no effect without --proxy.`; výpis
  plánu `  proxy also → <složka>/<datum>/<karta>`.
- Report HTML/PDF: řádek „Proxy: …“ končí `· také ve složce <span class="mono"><cesta></span>`. JSON: `proxy.extraFolder`.
- Nastavení: klíč `proxy.extraFolder`, prázdný nebo z mezer → `nil`; chybí, když není nastavený.

Mac technika: `renamex_np(part, dst, RENAME_EXCL)` (APFS, HFS+). Kde FS volbu nepodporuje (`ENOTSUP`/`EINVAL`, pravděpodobně
exFAT/FAT32/SMB), kontrola existence těsně před obyčejným `rename` (stejné okno souběhu jako Windows `Path.Exists` + `Move`).
Soubory: nový `Sources/OffloadKit/ProxyCopy.swift` (také náhrada soukromého `copyFile` v `Job.swift`), `Proxy.swift`
(`ProxySettings.extraFolder`), `Job.swift`, `HTMLReport.swift`, `Desktop/AppSettings.swift` (`ProxyPreflight.extraFolder*`,
`isDirectory`), `offload/main.swift`, `OffloadApp/AppModel.swift`, `Views.swift`.

### D.3 Vestavěné LUTy a výběr LUTu podle klipu

Návrh LUTů (§2–5) platí beze změny; tady jen co a jak na Macu.

**D.3.1 Katalog a přibalení.** `shared/luts/` (11 `.cube` 33³ + `luts.json` `{id, name, maker, file, curve, gamut}`) je commitnutý —
build ani aplikace Python nepotřebují (generátor `generate.py` jen pro přegenerování). `scripts/build-app.sh` je zkopíruje do
`Offload.app/Contents/Resources/luts/`. `LutCatalog.defaultFolder` hledá: `Bundle.main.resourcePath/luts` (aplikace),
`<exe>/luts`, `<exe>/../luts` (CLI v `Contents/Resources/bin`), a **jen** u SwiftPM buildu (cesta spustitelného souboru obsahuje
`/.build/`) až 8 úrovní nahoru `shared/luts` repa — nainstalovaná kopie si nikdy nevezme repo, ve kterém náhodou leží.
Testy předávají katalog výslovně (`TestMedia.luts` z `#filePath`), protože pod `swift test` je spustitelný soubor Xcode `xctest`.
Chybějící/poškozená složka → prázdný katalog (nikdy nespadne); položka bez `id`/`file` nebo s chybějícím souborem se vynechá.
`LutCatalog.shared` se načte jednou (pár malých souborů v balíčku aplikace, i na hlavním vlákně při startu přes
`SettingsStore.decode`).

**D.3.2 Režimy a nastavení** (`proxy.lutMode` `auto|builtin|custom|none`, `lutBuiltin`, `lutFallback`, `lutPath` beze změny):
- chybí-li `lutMode` (nastavení z 0.1.0): neprázdný `lutPath` → `custom` (vlastní LUT jako dřív), jinak `auto` se záložním
  „bez LUT“; neznámá hodnota `lutMode` (novější verze) se čte jako chybějící;
- s neprázdným katalogem: neznámé `lutBuiltin`/`lutFallback` → `nil`; `builtin` bez platného id → `auto`. **Prázdný katalog
  uložené volby nemaže** (rozbitá instalace nesmí smazat nastavení);
- `lutFallback` je jen id vestavěného LUTu nebo `nil` (vlastní `.cube` jako záloha nejde);
- blokovat start může jen chybějící **vlastní** `.cube` (`ProxyPreflight.lutInUse` jen v režimu `custom`) — text z oddílu 3.7.
- Swift: režim „bez LUT“ je `LutMode.off` s JSON hodnotou `"none"` a `LutUse.off(_:)` — `case none` by se u `LutMode?`
  pletl s `Optional.none`.

**D.3.3 Pravidla výběru** (`LutResolver.resolve`, čisté funkce, 1:1 s Windows, první platné vyhrává):

| Režim / zdroj | Výsledek (`source`, `id`, `reason`) |
|---|---|
| `none` | `none`, „vypnuto v nastavení“ |
| `custom` | `custom`, název souboru, „vlastní LUT“; bez cesty `none`, „vlastní LUT není vybraný“ |
| `builtin` | `builtin`, id, „vybraný v nastavení“; neznámé id `none`, „vestavěný LUT nenalezen“ |
| `auto`, prázdný katalog | `none`, „vestavěné LUTy chybí“ |
| BRAW | úkol 21: `bmd-film-gen5`, „Blackmagic RAW (gama Film Gen 5)“; **úkol 28 (Doplněk 2, D.15) to nahradí výběrem podle gamy klipu** z `braw-decode --info` (jako Windows po plánu 3) |
| R3D | `red-log3g10-rwg`, „RED R3D (IPP2 Log3G10)“ |
| ARRIRAW | `colorSpace(lut: nil, info)` obsahuje LogC4 → `arri-logc4-awg4` „ARRIRAW (LogC4)“, jinak `arri-logc3-awg3` „ARRIRAW (LogC3)“ |
| Sony `<klip>M01.XML` | `CaptureGammaEquation` začíná `s-log3` → `sony-slog3-sgamut3cine` (primaries obsahují „cine“, nebo chybí a gama končí „cine“) jinak `sony-slog3-sgamut3`, důvod „Sony XML: <gama> / <primaries>“ (bez koncových „ /“); `s-log…` jiné → bez LUTu „Sony XML: <gama> (bez vestavěného LUTu)“; jiná neprázdná gama → bez LUTu „Sony XML: <gama> (není log)“ — **ani záložní LUT** |
| kamera obsahuje ALEXA/AMIRA | `\bALEXA\s*35\b` → `arri-logc4-awg4` „ARRI kamera (předpoklad LogC4)“, jinak `arri-logc3-awg3` „… (předpoklad LogC3)“ |
| nic | záložní LUT: `fallback`, id, „kamera nerozpoznána — záložní LUT“; bez zálohy `none`, „kamera nerozpoznána“ |

Kamera z ffprobe: **výrobce + model** spojené (`com.apple.quicktime.make` „ARRI“ + `…model` „ALEXA 35“ → „ARRI ALEXA 35“; model,
který výrobcem začíná, zůstane), jinak `product_name`, `company_name`. Sony XML se čte z **cíle** (leží vedle klipu,
zkopíroval se). Mac čte XML přes `XMLParser` (první prvek s atributem `name`, jeho `value`; jmenné prostory nevadí).

**D.3.4 Render a náhledy** (`ProxyRenderer`, 1:1 s Windows `PrepareLut`/`LookFor`/`NeedsPreMap`):
- LUT se předává **po klipech** (`thumbnails(…, lut:)`, `render(…, lut:, …)`); `settings.lutPath` sám nic neaplikuje.
- Kopie do dočasné složky rendereru (filtry bez uvozovek): vlastní LUT jednou při vytvoření rendereru jako `look.cube`
  (jen v režimu `custom` a když soubor existuje), vestavěný při prvním použití jako `lut-<id>.cube`, jednou na id.
- **Pre-map jen pro vestavěný LUT na zdroji v plném rozsahu**: `FrameSource.fullRange` (braw-decode dává RGBU16 = plný rozsah)
  nebo `ClipInfo.colorRange == "pc"` (ffprobe `color_range`). Filtr `FilterGraph.preMap` (`lutrgb`, `x = (v·1023 − 64)/876`)
  těsně před `lut3d`. Vlastní `.cube` se aplikuje beze změny.
- **RAW dávají log jen když LUT opravdu následuje** (`RawLook`): R3D `--colorSciVersion 3 --colorSpace 25 --gammaCurve 34`,
  ARRI `--target-colorspace` podle vestavěného ARRI LUTu (`AWG4/D65/LogC4`, `AWG3/D65/LogC3`), u vlastního podle názvu
  souboru/metadat (`ARRIRAWBackend.colorSpace`); bez LUTu vlastní Rec.709 vzhled — RED výchozí, ARRI jen `--embedded-look`
  (oprava 2026-10-06: art-cmd odmítne `--target-colorspace` u vzhledu bez DRT LUTu, skončí kódem 0 a nic nezapíše; klip bez
  vloženého vzhledu dá LogC). Prázdná cesta k LUTu = bez LUTu.
- R3D/ARRI náhledy dál z hotové proxy — **bez** LUTu (proxy ho už má).

**D.3.5 Report.** U klipu volitelný klíč `lut` = `{source, id?, name, reason}` (`id` chybí u `custom`/`none` — `nil` se
nezapisuje, jako Windows), `file` se nikdy nezapisuje. HTML u klipu `<div class="note">LUT: <název> (<důvod>)</div>`;
bez LUTu `LUT: bez LUT (<důvod>)`, kromě „vypnuto v nastavení“. CSS `.path,.note{color:var(--muted);font-size:12px}`.
Řádek „Proxy: …, LUT: <způsob>“: `automaticky podle kamery` / název vestavěného (z katalogu, jinak id) / název vlastního
souboru / `bez LUT`.

**D.3.6 CLI.** `--lut auto|none|<id>|<cesta.cube>` (výchozí `auto`; cesta = končí `.cube` nebo obsahuje `/` či `\`),
`--lut-fallback <id>|none`; chyby `Unknown LUT '<x>'. Use auto, none, a .cube path or one of: <ids>`,
`Unknown fallback LUT '<x>'. Built-in: <ids>`, `LUT not found: <cesta>` (Mac je hlásí jako `ValidationError` — exit 64
a nápověda, jako dosavadní „LUT not found“). Před kopií řádek `LUT: auto (fallback <id>)` / `none` / `<id>` / `<cesta>` —
jen když se renderují proxy nebo analyzují klipy (`--no-analyze` bez `--proxy` ho nevypíše, jako Windows `LutOption.PlanLine`).
`offload luts` vypíše `<id> — <název>`; prázdný katalog → na stderr `No built-in LUTs found (luts folder missing).`, exit 0.
**Změna chování proti 0.1.0:** bez `--lut` se LUT vybírá automaticky (dřív žádný).

**D.3.7 GUI** (sekce Proxy, řádek LUT): seznam „Automaticky podle kamery“ · 11 vestavěných (název z `luts.json`) · „Bez LUT“ ·
„Vlastní…“. Při „Automaticky“ pod ním „Když kameru nepoznám:“ se seznamem „Bez LUT“ + vestavěné. Při „Vlastní…“ název
souboru (nebo „nevybráno“) a „Vybrat…“; „Vlastní…“ bez vybraného souboru otevře panel „Vyber LUT (.cube)“, „Zrušit“ vrátí
předchozí volbu. Neznámé id vestavěného se ukáže jako „Automaticky“. Červené „LUT nenalezen: …“ (oddíl 3.7) jen u vlastního.

### D.4 Barvy proxy a náhledů

Změřené důvody (ROZDILY „Barvy proxy“, návrh LUTů §2): bez výslovné matice swscale po kroku v RGB (LUT, RGB dekodér)
převede do YUV maticí **BT.601** a soubor zůstane neoznačený — přehrávač u HD použije BT.709 a čistá červená vyjde
(255, 23, 0). Samotné výstupní volby nestačí: ffmpeg 8 bere primaries a přenos ze snímků, proto i `setparams`. JPEG (JFIF)
každý prohlížeč a PDF dekóduje jako BT.601 v plném rozsahu.

- Proxy: `… → scale=out_color_matrix=bt709:out_range=tv → format=<yuv420p|yuv422p10le> → setparams=color_primaries=bt709:color_trc=bt709`
  a za argumenty enkodéru `-colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv`.
- Náhledy: `scale=-2:180:flags=bicubic → [LUT] → scale=out_color_matrix=bt601:out_range=pc → format=yuvj420p`.
- Řetěz proxy celý: ořez na frame lines → `scale` → `setsar=1` → `[format=rgb48le,(pre-map,)lut3d]` → burn-in → BT.709 →
  pixelový formát → značky. Nový `Sources/OffloadKit/FilterGraph.swift` (čisté funkce, test řetězců 1:1 s Windows
  `FilterGraphTests`); Mac dává do filtrů absolutní cesty z `$TMPDIR` (bez `:`, `,`, `'`, `\`).
- **VideoToolbox:** `h264_videotoolbox`/`hevc_videotoolbox` převezmou `-colorspace/-color_primaries/-color_trc` z kontextu
  kodéru do `kVTCompressionPropertyKey_ColorPrimaries/TransferFunction/YCbCrMatrix` a MOV muxer zapíše atom `colr`, když
  barvy nejsou „unspecified“; `prores_ks` je zapisuje do hlavičky snímku. **Ověřit na Macu** (D.10 ř. 1) — test
  `ColorTests.testProxyRedIsBT709MatrixAndTagged` to ověří pro H.264 (VideoToolbox) i ProRes LT, s LUTem i bez.

### D.5 Ochrana proti zaseknutí ffmpeg

1:1 s **konečnou** Windows verzí (`windows/src/Offload.Core/ProxyRenderer.cs`, commit `a3f047c` na `windows-followups`;
testy `ProxyWatchdogTests.cs`). Důvod: ffmpeg 8.1 s `-hwaccel auto` se na Windows na FFV1 navždy zasekl (Vulkan
VK_ERROR_DEVICE_LOST) a úloha visela.
- **Hardwarové dekódování jen když kodek začíná `h264` nebo `hevc`** (`ProxyRenderer.usesHardwareDecode(info.codec)`, bez
  ohledu na velikost písmen). Vše ostatní (FFV1, ProRes, MJPEG, DNx …) softwarově — i když VideoToolbox umí ProRes (ffmpeg 6+,
  Apple Silicon), pravidlo je stejné jako na Windows. Náhledy vždy softwarově (oddíl 3.3); RAW formáty hwaccel nepoužívají.
- **Hlídač jen pro render proxy** (`stallTimeout = defaultStallTimeout = 60` s). Pokrok = nová **vyšší** hodnota `frame=` nebo
  `out_time_us=` z `-progress pipe:1` (úvodní `frame=0`, `out_time_us=N/A` a záporné časy nejsou pokrok), závěrečné
  `progress=end`, **nebo data, která dekodér RAW poslal do ffmpeg** (braw-decode jde nově přes pumpu: `Pipe` → vlákno →
  `Pipe`, každý přečtený blok vynuluje hodinky; zapisovací deskriptor s `F_SETNOSIGPIPE`, aby zápis do mrtvého ffmpeg
  nevyvolal SIGPIPE). Lhůta 60 s, **do prvního pokroku 3× (180 s)** — načtení vstupu, rozběh dekodéru, lookahead enkodéru.
  Zrušení se kontroluje první a vyhrává. Náhledy hlídané nejsou (jako Windows).
- **Ukončení:** `Process.terminate()` (SIGTERM) ffmpeg i dekodéru, čekání **nejvýš 10 s** (`killWait`, smyčka po 50 ms),
  pak `kill(pid, SIGKILL)`; když proces ani pak neskončí (uvízl v ovladači GPU), do logu úlohy
  `ffmpeg (pid N) did not end 10 s after kill` a úloha jede dál — nikdy se nečeká bez limitu (Windows: `Kill` + 10 s).
  Kontrola běží ve stávající smyčce po 200 ms (obdoba Windows `WaitForExit(200)`; `DispatchSource` časovač není potřeba).
- **Opakování jen po hardwarovém dekódování:** zasekl-li se nebo selhal (chyba ffmpeg) běh **s** hwaccel, render se jednou
  zopakuje bez něj; těsně před opakováním se zkontroluje zrušení (zrušení se nikdy neopakuje). Bez hwaccel se neopakuje nic.
- **Texty poznámky u klipu** (přesně jako Windows; proxy `failed`, úloha pokračuje):
  - zasekl se bez hwaccel: `ffmpeg se zasekl (60 s bez nového snímku); proxy nevznikla`
  - zasekl se s hwaccel i bez něj: `ffmpeg se zasekl (60 s bez nového snímku) i bez hardwarového dekódování; proxy nevznikla`
  - zasekl se s hwaccel, bez něj selhal: `ffmpeg se zasekl (60 s bez nového snímku); bez hardwarového dekódování selhal: <chyba>`
  - selhal s hwaccel, bez něj selhal nebo se zasekl: `<2. chyba>; s hardwarovým dekódováním předtím: <1. chyba>`
  - chyba bez hwaccel: ta chyba beze změny.
- **Log úlohy** (`ProxyRenderer.log`, z `OffloadJob`): `proxy <klip>: with hardware decoding: <chyba> — retrying in software`,
  `proxy <klip>: software decoding succeeded`, `proxy <klip>: software decoding failed too: <chyba>`.
- REDline a art-cmd (mezisoubory před ffmpeg) hlídač nemají — mohou dlouho legitimně pracovat bez výstupu (jako Windows).
- Mac: `-hwaccel videotoolbox` (Windows `-hwaccel auto`) — jediný rozdíl, zapíše se do ROZDILY (Technologie).

### D.6 Ikona aplikace

`shared/icon/` (generuje `make_icon.py`, Pillow; commitnuté): `Offload-1024.png`, `Offload.ico` (Windows),
`Offload.iconset/` (`icon_16x16.png` … `icon_512x512@2x.png`). `scripts/build-app.sh`:
`iconutil -c icns ../shared/icon/Offload.iconset -o build/Offload.app/Contents/Resources/Offload.icns` a v Info.plist
`<key>CFBundleIconFile</key><string>Offload</string>`. Chybí-li iconset, build jen varuje (ikona je kosmetika); chybějící
`shared/luts` je chyba buildu. CLI ikonu nemá.

### D.7 Formáty na disku

- `offload_report.json`: u klipu volitelný `lut` (D.3.5); v `proxy` klíče `lutMode`, `lutBuiltin`, `lutFallback`, `extraFolder`
  (chybějící = nenastaveno); u `info` volitelný `colorRange` (`tv`/`pc`). Starší reporty bez nich zůstávají platné, JSON
  z Macu i Windows má stejný tvar. Náhledy se dál nezapisují.
- Nastavení (`UserDefaults` `settings`): `proxy.lutMode`, `proxy.lutBuiltin`, `proxy.lutFallback`, `proxy.extraFolder` —
  stejné klíče jako Windows `settings.json`; migrace D.3.2.
- Složka pro proxy: `<extraFolder>/<YYYY-MM-DD>/<karta>/<klip>[_N].mov` — mimo cíle, do `.xxh64`/MHL se nezapisuje.
- FORMATY.md: u `clips[].lut` a `proxy.extraFolder` zmizí „jen Windows / Mac ignoruje“ (úkoly 20, 22, 26).

### D.8 Poznámky k macOS technice

- `Bundle.main.executableURL?.resolvingSymlinksInPath()` místo `CommandLine.arguments[0]` (CLI spuštěné z `PATH` má
  v `arguments[0]` jen jméno).
- `ProxyRenderer` slouží jedné úloze na jednom vlákně (kopie vestavěných LUTů bez zámku); `ProgressWatch` má zámek
  (řádky přicházejí z obsluhy roury).
- `OffloadJob.init(config:options:luts:)` — testy předají katalog z repa; aplikace a CLI `LutCatalog.shared`.
- Ruční ověření GUI: vlastní LUT z verze 0.1.0 musí po aktualizaci zůstat „Vlastní…“ (migrace), ne zmizet.

### D.9 Testy (nové soubory)

| Oblast | Soubor testů | Port z Windows |
|---|---|---|
| Filter graph, pre-map, BT.709/JFIF řetězce | `FilterGraphTests.swift` | `FilterGraphTests.cs`, `ProxyRendererTests.FullRangeSource_PreMapped` |
| Barvy proxy a náhledů (ffmpeg) | `ColorTests.swift` | `ProxyRendererTests.Proxy_Red_Bt709MatrixAndTags`, `Thumbnail_Red_JfifBt601` |
| Hlídač, hwaccel jen H.264/HEVC, opakování a texty (falešný ffmpeg skript) | `FFmpegWatchTests.swift` | `ProxyWatchdogTests.cs` |
| Složka pro proxy, preflight, nastavení, HTML, úloha | `ProxyFolderTests.swift` | `ProxyCopyTests.cs`, `StageTwoTests.ProxyFolder_*`, `SettingsStoreTests.ExtraFolder_*`, `HtmlReportTests` |
| Katalog, hledání složky | `LutCatalogTests.swift` | `LutCatalogTests.cs` |
| Výběr LUTu, Sony XML, ARRI, ffprobe | `LutResolverTests.swift` | `LutResolverTests.cs`, `RawBackendsTests.Arri_ColorSpace` |
| `--lut` / `--lut-fallback` | `LutOptionTests.swift` | `LutOptionTests.cs` |
| Migrace a pravidla nastavení LUTu | `LutSettingsTests.swift` | `SettingsStoreTests.LutMode_*`, `Normalize_EmptyCatalog_*` |
| Pre-map, RawLook, REDline/art-cmd argumenty, S-Log3 šedá (ffmpeg) | `LutRenderTests.swift` | `ProxyRendererTests` (LUT), `RawBackendsTests` |
| LUT v HTML/JSON, výběr LUTu v úloze (ffmpeg) | `LutReportTests.swift` | `HtmlReportTests.Clip_ShowsLut_AndJsonKey`, `StageTwoTests.Report_RecordsLutPerClip` |

### D.10 Nejistoty (ověřit na Macu)

| # | Co | Předpoklad | Jak ověřit |
|---|---|---|---|
| 1 | VideoToolbox enkodéry zapíší BT.709 značky | `-colorspace/-color_primaries/-color_trc/-color_range` + `setparams` stačí, MOV dostane `colr` | `ffmpeg -f lavfi -i color=c=red:s=320x180:d=1 -vf scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,setparams=color_primaries=bt709:color_trc=bt709 -c:v h264_videotoolbox -allow_sw 1 -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv /tmp/r.mov && ffprobe -v error -select_streams v:0 -show_entries stream=color_range,color_space,color_transfer,color_primaries -of default=nw=1 /tmp/r.mov` → vše `bt709`, `tv`; totéž s `hevc_videotoolbox -tag:v hvc1`. Kdyby chyběl `colr`: přidat `-movflags +write_colr` do `FilterGraph.rec709Args` |
| 2 | `setparams` s `color_primaries`/`color_trc` v Homebrew ffmpeg | ano (ffmpeg ≥ 4.4) | `ffmpeg -hide_banner -h filter=setparams \| grep -E 'color_(primaries\|trc)'` |
| 3 | `renamex_np` + `RENAME_EXCL` | importuje se ze `<stdio.h>`; na APFS `EEXIST`, na exFAT/FAT32/SMB možná `ENOTSUP`/`EINVAL` (pak záložní kontrola) | `swift build` (překlad `ProxyCopy.swift`); `hdiutil create -size 200m -fs ExFAT -volname PXEXF /tmp/pxexf.dmg && hdiutil attach /tmp/pxexf.dmg` a úkol 20 krok 9; totéž na SMB sdílení, je-li po ruce |
| 4 | VideoToolbox dekódování drží rozsah | H.264/HEVC `color_range=tv` → po `format=rgb48le` legální roztažení jako software | test `LutRenderTests.testSLog3GreyProxyIs042` (H.264 zdroj jde přes VT); při odchylce porovnat s `usesHardwareDecode → false` |
| 5 | `-progress` během zaseknutí | ffmpeg buď přestane psát, nebo opakuje stejný blok (`frame=` beze změny) — obojí hlídač pozná (jako Windows) | `ffmpeg -nostats -progress pipe:1 -f lavfi -i testsrc2=d=3 -f null - \| grep -E '^(frame\|out_time_us)='` (bloky každých ~0,5 s); skutečné zaseknutí VT se reprodukovat nedá — chování pokrývají testy s falešným ffmpeg |
| 6 | `Bundle.main.executableURL` u CLI | v `Contents/Resources/bin` i přes symlink z `/usr/local/bin` vrátí skutečnou cestu | úkol 25 krok 3 (`offload luts` z balíčku i přes symlink → 11 řádků) |
| 7 | `iconutil` přijme iconset | 10 PNG se správnými rozměry | `sips -g pixelWidth -g pixelHeight ../shared/icon/Offload.iconset/*.png`; `iconutil -c icns ../shared/icon/Offload.iconset -o /tmp/O.icns && ls -l /tmp/O.icns` |
| 8 | `XMLParser` a Sony XML s `xmlns` | atributy `name`/`value` přijdou bez ohledu na jmenný prostor | `LutResolverTests.testSonySLog3FromXML` (fixture s `xmlns`); se skutečným `C0001M01.XML` z kamery: `swift test --filter LutResolverTests` a ručně úkol 22 krok 9 |
| 9 | ProRes přes VideoToolbox | záměrně se nepoužívá | volitelně změřit: `time ffmpeg -hwaccel videotoolbox -i prores.mov -f null -` vs. bez `-hwaccel`; rozdíl zapsat do VYVOJ-MAC (rozhodnutí se mění jen společně s Windows) |
| 10 | Rozsah mezisouborů R3D/ARRIRAW a ProRes z ALEXA 35 | legální video (bez pre-map) | na vzorcích: `ffprobe -show_streams` mezisouboru (`color_range`); jako na Windows (návrh LUTů §6 bod 5) |
| 11 | Pumpa dekodéru RAW | rodičovská kopie zapisovacího konce roury dekodéru se po spuštění zavře (EOF pro pumpu); `F_SETNOSIGPIPE` se přeloží a zápis do ukončeného ffmpeg vrátí chybu místo SIGPIPE; SIGKILL ukončí ffmpeg do 10 s | `FFmpegWatchTests.testFeederDataIsProgress`, `testNoFeederDataAtAllKilledAfterTheGrace`; ručně BRAW proxy (úkol 19 krok 7) a `Zrušit` uprostřed BRAW renderu — aplikace nespadne, v cíli žádný `.part` |

### D.11 Mapa změn souborů (doplněk)

| Soubor | Úkoly |
|---|---|
| `Sources/OffloadKit/FilterGraph.swift` (nový) | 18 |
| `Sources/OffloadKit/FFmpegWatch.swift` (nový) | 19 |
| `Sources/OffloadKit/ProxyCopy.swift` (nový) | 20 |
| `Sources/OffloadKit/Luts.swift` (nový) | 21 |
| `Sources/OffloadKit/Proxy.swift` | 18, 19, 20, 21, 22 |
| `Sources/OffloadKit/RawBackends.swift` | 21, 22 |
| `Sources/OffloadKit/Media.swift` | 21 |
| `Sources/OffloadKit/Report.swift` | 22 |
| `Sources/OffloadKit/Job.swift` | 19, 20, 22 |
| `Sources/OffloadKit/HTMLReport.swift` | 20, 22 |
| `Sources/OffloadKit/Desktop/AppSettings.swift` | 20, 21 |
| `Sources/offload/main.swift` | 23 |
| `Sources/OffloadApp/AppModel.swift`, `Views.swift` | 24 |
| `scripts/build-app.sh` | 25 |
| `Tests/OffloadKitTests/{FilterGraph,Color,FFmpegWatch,ProxyFolder,LutCatalog,LutResolver,LutOption,LutSettings,LutRender,LutReport}Tests.swift`, `LutTestSupport.swift` (nové); `ThumbnailTests.swift`, `SettingsStoreTests.swift` (jeden řádek), `ColorTests.swift` (úkol 22) | 18–22 |
| `docs/VYVOJ-MAC.md`, `docs/ROZDILY.md`, `docs/FORMATY.md`, `mac/README.md` | 18–26 |

### D.12 Pokrytí ROZDILY a „Naplánováno“ (VYVOJ-MAC)

| Mezera Macu (stav 2026-10-05) | Kde se zavírá |
|---|---|
| ROZDILY „Náhledy v reportu“ (1 náhled) | úkol 7 |
| ROZDILY „Test USB portů“, „Propustnost článků HW“, záměrný rozdíl HW diagnostika, „GUI navíc na Windows“ (okno Hardware) | úkoly 16, 17 |
| ROZDILY „Vestavěné LUTy …“ (řádek i záměrný rozdíl, „Mac … ignoruje“) | úkoly 21–24 (GUI 24) |
| ROZDILY „Složka pro proxy navíc“ (řádek i záměrný rozdíl) | úkoly 20, 23, 24 |
| ROZDILY „Barvy proxy“ | úkol 18 |
| ROZDILY „Report při zrušeném ověření“, „Zdroj, který je sám offload“, „Dvakrát zadaný cíl“, „Kontrola voleb“ | úkoly 2, 3 |
| ROZDILY „Zrušená úloha v GUI“, „Vysunutí karty“ | úkol 11 |
| Ochrana proti zaseknutí ffmpeg (Windows `f7d49a1`, konečná podoba `a3f047c`) | úkol 19 |
| Ikona aplikace (Windows `9915690`) | úkol 25 |
| ROZDILY „Verze v reportu“ | úkol 26 |
| Zůstává záměrně: `proxyEncoder` (Mac enkodér nevolí), Windows-only kontroly (oddíl 4) | — |
| BRAW po Windows plánu 3 (dekodér v C# hotový 2026-10-05) | Doplněk 2, úkoly 27–31 (D.20) |
| VYVOJ-MAC Naplánováno: složka pro proxy, vestavěné LUTy, 3 snímky, „Totéž chování…“, „Podklady pro sestavení…“ | úkoly 7, 20–24; odrážky 2026-10-05 nahradil jeden odkaz na plán, ten smaže úkol 26 |

## Doplněk 2 (2026-10-05): Blackmagic RAW po Windows plánu 3

Windows verze dokončila plán 3 — dekodér BRAW v C# (`windows/src/Offload.BrawDecode/`, sloučeno v `4138680`, commity
`ef53f0d..8e10416`; plán `windows/docs/superpowers/plans/2026-10-05-offload-windows-3-braw.md`, historie
`docs/VYVOJ-WINDOWS.md` „plán 3“). Navenek se chová jako Mac `braw-decode`, ale přidal chování, které Mac nemá. Tento doplněk
říká, co z toho Mac převezme a jak; implementují ho úkoly **27–31** plánu, dělané **po úkolu 25 a před úkolem 26**. Platí
pravidla oddílů 1–10 a D.1–D.12. Mac `braw-decode` zůstává C++ nad `BlackmagicRawAPI.framework` (dispatch SDK přes `CFBundle`).

### D.13 Co se převezme — přehled

Každá položka ověřena proti Windows kódu (`Decoder.cs`, `InfoJson.cs`, `Options.cs`, `BrawApi.cs`, `Offload.Core/RawBackends.cs`,
`Luts.cs`, `ProxyRenderer.cs`, `ToolProcess.cs`, `BrawLibrary.cs`) a proti dnešnímu Mac kódu (`Tools/braw-decode/braw-decode.cpp`,
`RawBackends.swift`) a plánu (úkoly 6, 19, 21, 22).

| # | Chování (Windows) | Mac dnes / po úkolech 1–25 | Kde |
|---|---|---|---|
| 1 | `--info` vydává `gamma`, `gamut`, `colorScienceGen` (atributy klipu `gama`, `gamt`, `csgn`); klíč chybí, když ho SDK nevrátí; nečitelný atribut = varování na stderr, kód zůstává 0 | nevydává | D.14, úkol 27 |
| 2 | Pipeline (CPU) se nastaví **před** `OpenClip` — SDK čte konfiguraci při prvním otevření klipu | nastavuje se až po `OpenClip`, tedy bez účinku | D.14, úkol 27 |
| 3 | LUT podle gamy (`LutResolver.Braw`) | vždy Film Gen 5 | D.15, úkol 28 |
| 4 | Start náhledu BRAW zaokrouhlený (`BrawBackend.StartFrame`) | dnešní `RawBackends.swift:77` usekává; **plán to už řeší úkolem 6** (`BRAWBackend.startFrame`, `.rounded()` = od nuly jako `MidpointRounding.AwayFromZero`) | úkol 6, beze změny |
| 5 | Hlášky kódů 3/4 jmenují načtenou knihovnu; Offload dělá z kódů 3/4/5 českou poznámku u klipu (texty z `RawBackends.cs`) a původní hlášku zapíše do logu — při `--info` i u dekodéru v rouře | hláška bez knihovny; kód bez poznámky; stderr dekodéru v rouře se zahazuje (`/dev/null`) | D.14, D.16, D.17; úkoly 27, 29, 30 |
| 6 | `--info` s limitem 60 s, pak ukončen, poznámka „neodpověděl do 60 s — ukončen“ | bez limitu | D.16, úkol 29 |
| 7 | Náhledy z dekodéru v rouře pod hlídačem; po prvním zaseknutí nebo limitu `--info` se další snímky klipu nezkoušejí a log to řekne; text „dekodér nebo ffmpeg se zasekl (… bez nového snímku)“ | náhledy nehlídané (úkol 19: „jen render proxy“) | D.17, úkol 30 |
| 8 | Když ffmpeg přestane číst, pumpa zavře výstup dekodéru; jeho kód se pak ignoruje (rozhoduje ffmpeg); dekodér, který selže, zatímco ffmpeg čte, je chyba; `braw-decode` na zavřené rouře končí kódem 0 | **Zjištění:** Mac `braw-decode` kódem 0 nekončí — SIGPIPE není ignorovaný, proces zabije signál dřív, než `write` vrátí chybu (`_exit(0)` ve `writeAll` je mrtvý kód). Offload to přežije jen proto, že `runFFmpeg` posuzuje jen `terminationReason == .exit`. Pumpa z úkolu 19 zavírá jen vstup ffmpeg — dekodér pak visí na plné rouře, dokud ho `terminate()` nezabije | D.14 (SIGPIPE), D.17; úkoly 27, 30 |
| 9 | stderr dekodéru v UTF-8 | **potvrzeno, beze změny kódu:** `fprintf` píše bajty cesty tak, jak přišly v `argv` (Foundation `Process` předává UTF-8), `ToolProcess` čte stderr jako UTF-8 | test `testStderrKeepsADiacriticPath` (úkol 27) |
| 10 | Továrna: objekt z `CreateBlackmagicRawFactoryInstance` se přes `QueryInterface` zeptá na `IID_IBlackmagicRawFactory`; jinak kód 3 s jasnou hláškou | volá se přímo | D.14, úkol 27 — **platí i pro Mac**, viz níže |
| 11 | Další, co je vidět nebo leží na disku: BRAW „nepodporovaný“ bez knihovny s nápovědou (text Windows); přísné argumenty (kód 2); hláška `stdout closed by the reader`; JSON `clips[].info.gamma/gamut/colorScienceGen`; po konci ffmpeg se na dekodér čeká nejvýš 2 s; chyba dekodéru má přednost před chybou ffmpeg | nic z toho | D.14–D.18 |

**K bodu 10:** na Windows C# volá sloty vtable objektu z DLL bez typové kontroly, proto se ptá přes `QueryInterface` (slot 0
IUnknown je stejný u každého COM objektu). Na Macu je `braw-decode` přeložený proti hlavičce SDK a dispatch vrací typovaný
`IBlackmagicRawFactory*` — kompilátor rozložení vtable hlídá, ale jen proti **hlavičce v repu**; framework za běhu je ten, který
uživatel nainstaloval (Player nebo Resolve jiné generace). Kontrola stojí pár řádků a dává čistý kód 3 místo pádu, proto se
převezme — s ověřením, že framework svou vlastní IID vrací (D.19 ř. 1; jinak se kontrola vypustí a zapíše sem).

### D.14 `braw-decode` (C++)

- **CLI beze změny:** `--info [--scale S] klip`, `[--scale S] [--start N] [--count N] klip`. Nově **přísné** (jako Windows `Options.Parse`):
  neznámá volba `--…`, chybějící nebo špatná hodnota `--scale` (jen `full|half|quarter|eighth`), `--start`/`--count` jiné než
  číslice, chybějící klip → `usage: braw-decode [--info] [--scale full|half|quarter|eighth] [--start N] [--count N] clip.braw`
  na stderr, kód 2 — ještě před hledáním knihovny (Offload nikdy nic jiného nepředává; překlep má selhat hlasitě).
- **`--info`** — jeden řádek JSON, sdílené klíče bajtově jako dosud (`"fps"` na 6 desetinných míst), za `camera` volitelně
  `"gamma":"…"`, `"gamut":"…"`, `"colorScienceGen":N`. Zdroj: `clip->CloneClipProcessingAttributes` → `GetClipAttribute`
  s `blackmagicRawClipProcessingAttributeGamma` (`'gama'`, string), `…Gamut` (`'gamt'`, string), `…ColorScienceGen` (`'csgn'`, u16);
  `Variant` s `VariantInit`/`VariantClear` (řetězec je `CFStringRef bstrVal`). Volání, které selže → klíč chybí, potichu.
  Jiný typ hodnoty → klíč chybí a na stderr `braw-decode: gamut: clip attribute 'gamt' is a number, expected a string`
  (u čísla `… is a string, expected a number`, `… is out of range`, `… has an unexpected variant type`); kód zůstává 0.
- **Pipeline před `OpenClip`:** `QueryInterface(IID_IBlackmagicRawConfiguration)` → `SetPipeline(blackmagicRawPipelineCPU)` hned
  po `CreateCodec`.
- **Knihovna v hláškách:** `loadFactory` si pamatuje, odkud framework načetl (`<složka>/BlackmagicRawAPI.framework`; „@exe“ =
  skutečná složka spustitelného souboru, `_NSGetExecutablePath` + `realpath`). Kódy a texty (HRESULT velkými písmeny jako
  Windows `Com.Hex`):
  - 3 `braw-decode: BlackmagicRawAPI.framework not found (install Blackmagic RAW Player, the Blackmagic RAW SDK or DaVinci Resolve)`
  - 3 `braw-decode: <knihovna>: the created object is not an IBlackmagicRawFactory (0x…)` (bod 10)
  - 3 `braw-decode: CreateCodec failed (0x…; library <knihovna>)`, `braw-decode: SetCallback failed (0x…; library <knihovna>)`
  - 4 `braw-decode: cannot open <klip> (0x…; library <knihovna>)`
  - 5 `braw-decode: frame N failed` (beze změny)
- **Zavřená roura:** `signal(SIGPIPE, SIG_IGN)` na začátku `main`; zápis, který selže (EPIPE), vypíše
  `braw-decode: stdout closed by the reader, stopping` a skončí **kódem 0** — i u `--info` (JSON se nově píše přes `write`, ne `printf`).
- Hledání knihovny beze změny pořadí: vedle `braw-decode`, SDK (`/Applications/Blackmagic RAW/Blackmagic RAW SDK/Mac/Libraries`),
  Player (`…/Blackmagic RAW Player.app/Contents/Frameworks`), DaVinci Resolve (`/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Frameworks`
  — ověřeno 2026-10-06, D.19 ř. 2; dřívější `Contents/Libraries` neexistuje).
  Swift `BrawLibrary` má stejný seznam (test porovná se zdrojem C++).

### D.15 LUT podle gamy klipu

`ClipInfo` dostane `gamma`, `gamut`, `colorScienceGen` (z `braw-decode --info`; prázdný řetězec = neuvedeno). `LutResolver.detect`
pro BRAW volá `LutResolver.braw(info)` — 1:1 s Windows `LutResolver.Braw` (`label` = `<gama> Gen <n>`, bez generace jen `<gama>`):

| Gama | Výsledek | Důvod u klipu |
|---|---|---|
| neuvedena (starší `braw-decode`, nebo ji SDK nevrátilo) | `auto`, `bmd-film-gen5` | `Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)` |
| jiná než „Blackmagic Design Film“ (velikost písmen nerozhoduje) — Video, Extended Video, Custom, HDR … | bez LUTu, **ani záložní** | `Blackmagic RAW: <label> (není log)` |
| „Blackmagic Design Film“, generace < 5 | bez LUTu | `Blackmagic RAW: <label> (bez vestavěného LUTu)` |
| „Blackmagic Design Film“, generace ≥ 5 nebo neuvedená | `auto`, `bmd-film-gen5` | `Blackmagic RAW: <label>` |

**Otevřené (jako Windows):** Film Gen 4 je zatím bez LUTu — provizorně, ke schválení uživatelem (vestavěný LUT je pro Gen 5;
převod Gen 4 by potřeboval vlastní LUT nebo rozhodnutí, že Gen 5 LUT stačí). Změní-li se rozhodnutí, mění se v obou verzích.

### D.16 BRAW jen s knihovnou, limit `--info`, české poznámky

- **Dostupnost** (`BRAWBackend.isAvailable`): `braw-decode` **a** `BlackmagicRawAPI.framework` v některé složce z D.14 (hledá se od
  skutečné složky dekodéru). Bez knihovny je BRAW „nepodporovaný“ — klip se zkopíruje a ověří, proxy a náhledy se přeskočí
  s poznámkou `Blackmagic RAW: chybí braw-decode nebo knihovna Blackmagic RAW (SDK, Player nebo DaVinci Resolve)` (text Windows;
  dosavadní Mac text „… nebo Blackmagic RAW Player/SDK“ zaniká). GUI řádek nástroje „braw-decode“ dál ukazuje jen nalezený
  dekodér (stejné otevřené místo jako Windows, VYVOJ-MAC „Naplánováno“).
- **Limit `--info`:** `ToolProcess.run(…, timeout:)`; `BRAWBackend.defaultInfoTimeout = 60` s (testy zkracují). Po limitu SIGKILL
  a chyba `braw-decode failed (exit -1): neodpověděl do 60 s — ukončen` (Windows ukončí celý strom procesů; `braw-decode` potomky nemá).
- **Poznámky** (`BRAWBackend.explain` → `RawDecoderError`, `description` = poznámka; texty z Windows `BrawBackend.Explain`,
  `<knihovna>` = nalezená cesta, jinak `BlackmagicRawAPI.framework`):
  - 3 → `Blackmagic RAW: knihovnu <knihovna> nelze použít — nainstalujte aktuální Blackmagic RAW Player`
  - 4 → `Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna <knihovna> starší než kamera (nainstalujte aktuální Blackmagic RAW Player)`
  - 5 → `Blackmagic RAW: snímek klipu se nepodařilo dekódovat (<poslední řádek stderr bez „braw-decode: “>)`
  - jiný kód (2, limit −1) → chyba beze změny.
  Poznámka jde do `probeError`/`proxyNote` (`"\(error)"`), **původní** `ToolError` (`braw-decode failed (exit 4): braw-decode: cannot
  open … (0x…; library …)`) do logu úlohy přes `ProxyRenderer.log` (`logged { }` kolem `probe` a `source` backendu — Windows `Logged`).
- **`ToolProcess`:** stderr se čte přes `StderrTail` (posledních 4000 znaků) a po skončení procesu se neblokujícím čtením dočte —
  chybová hláška nástroje, který hned skončí, je celá (dřív mohl obslužný blok roury zaostat za koncem procesu).

### D.17 Náhledy BRAW pod hlídačem, pumpa a posouzení dekodéru

- **Hlídač i pro náhledy s dekodérem v rouře** (`FrameSource.feeder != nil`): `runFFmpeg(…, stallAfter: stallTimeout)`; pokrok jsou
  data z dekodéru (ffmpeg u náhledu `-progress` nepíše); 60 s, do prvních dat 180 s. Náhledy z video souborů hlídané nejsou
  (beze změny, jako Windows).
- **Zastavení po prvním zaseknutí:** chyba náhledu `dekodér nebo ffmpeg se zasekl (60 s bez nového snímku)`, další pozice klipu
  se nezkoušejí (nejhůř 1 × 180 s místo 3 ×), log `thumbnails <klip>: dekodér nebo ffmpeg se zasekl (60 s bez nového snímku); the
  remaining frames are skipped`. **Totéž po limitu `--info`** (chyba `braw-decode`, kód −1, vznikla ještě před spuštěním ffmpeg):
  log `thumbnails <klip>: braw-decode failed (exit -1): neodpověděl do 60 s — ukončen; the remaining frames are skipped`.
  Texty poznámek u proxy (D.5) beze změny.
- **Pumpa:** když zápis do ffmpeg selže (ffmpeg skončil — náhled má svůj snímek, nebo selhal), pumpa si **nejdřív** označí „odříznuto“
  (`Flag`) a pak zavře vstup ffmpeg **i výstup dekodéru**. `braw-decode` uvidí zavřenou rouru a skončí sám kódem 0.
- **Po konci ffmpeg** se na dekodér čeká nejvýš 2 s (pak `terminate()`). Dekodér, který skončil sám s kódem ≠ 0 a nebyl odříznutý,
  je chyba klipu — **před** chybou ffmpeg (ten viděl jen useknutý proud; Windows pořadí). `FrameSource.explainFeederError` z ní
  udělá poznámku z D.16 a původní hláška (stderr dekodéru, nově zachycené) jde do logu. Ukončený signálem (2 s, zrušení, hlídač)
  nebo odříznutý dekodér se neposuzuje — rozhoduje kód ffmpeg.

### D.18 Formáty na disku

- `offload_report.json`: u `clips[].info` volitelné `gamma`, `gamut` (řetězce), `colorScienceGen` (číslo) — jen když je dekodér zná;
  stejné klíče jako Windows. FORMATY.md: „zatím jen Windows / Mac ignoruje“ zmizí (úkol 28).
- HTML report: nové důvody LUTu u BRAW klipů (D.15), nové poznámky u klipu (D.16, D.17). Nastavení beze změny.
- `braw-decode --info` JSON: viz D.14 (sdílené klíče beze změny — starší Offload ho dál přečte).

### D.19 Testy a nejistoty

| Oblast | Soubor testů | Potřebuje | Port z Windows |
|---|---|---|---|
| Hledání knihovny, stejné složky jako C++ | `BrawLibraryTests.swift` | nic | `BrawCoreTests.Library_*` |
| Argumenty `braw-decode` (kód 2) | `BrawDecodeTests.swift` | sestavený `Tools/bin/braw-decode` | `BrawDecodeTests` (Options) |
| Kód 4 s knihovnou v hlášce, UTF-8 stderr | `BrawDecodeTests.swift` | + knihovna Blackmagic RAW | `BrawDecodeTests` (chybějící klip) |
| `--info` s gamou, snímky RGB48, dávky, konec klipu, zavřená roura = 0 | `BrawDecodeTests.swift` | + vzorek (`OFFLOAD_BRAW_SAMPLE`) | `BrawSampleTests` |
| `--info` → `ClipInfo`, JSON klíče | `BrawBackendTests.swift` | nic | `BrawCoreTests.Info_GammaGamutGen_*` |
| LUT podle gamy | `LutResolverTests.swift` | nic | `LutResolverTests.Braw_*` |
| Dostupnost, poznámky 3/4/5, limit `--info`, log | `BrawBackendTests.swift`, `ToolProcessTests.swift` | nic (falešný dekodér = sh skript) | `BrawCoreTests` (I1), `ProxyWatchdogTests.BrawInfo_*` |
| Náhledy pod hlídačem, jeden pokus, pumpa a odříznutý dekodér | `BrawFeederTests.swift` | nic | `ProxyWatchdogTests.BrawThumbnail_*`, `ConsumerEndsFirst_*`, `BrawCoreTests.Feeder_*` |
| Celá cesta (analýza, LUT, 3 náhledy, proxy 540p) | `BrawSampleTests.swift` | + vzorek, ffmpeg | `BrawSampleTests.Offload_ProbeThumbnailsProxy` |

Nejistoty (ověřit na Macu; výsledek do commitu úkolu a sem):

| # | Co | Předpoklad | Jak ověřit |
|---|---|---|---|
| 1 | Framework vrátí na `QueryInterface(IID_IBlackmagicRawFactory)` sám sebe | ano (Windows ověřeno s knihovnou z Resolve) | úkol 27 krok 6: s knihovnou kód 4 u chybějícího klipu, ne 3 |
| 2 | Cesty k frameworku | jako v dosavadním `braw-decode.cpp` | `ls -d …/BlackmagicRawAPI.framework` u Playeru, SDK a Resolve; `mdfind -name BlackmagicRawAPI.framework` |
| 3 | Typy `Variant` u `gama`/`gamt`/`csgn` | string, string, u16 (komentáře v hlavičce) | jen se vzorkem: `braw-decode --info` — stderr bez varování, JSON s klíči |
| 4 | `trap '' PIPE` v testovacím skriptu se přenese na `/bin/dd` | ano (ignorovaný signál přežije `exec`) | `BrawFeederTests.testConsumerEndsFirst…` |
| 5 | CPU pipeline nastavená před `OpenClip` nezpomalí proxy | dosud se nastavovala bez účinku, tedy běžela výchozí pipeline SDK | se vzorkem změřit `time` proxy 8K/12K klipu před úkolem 27 a po něm; výrazné zpomalení zapsat do VYVOJ-MAC — volba jiné pipeline (Metal) je samostatné rozhodnutí pro obě verze |
| 6 | Film Gen 4 | bez LUTu (provizorně) | rozhodnutí uživatele; se vzorkem z kamery Gen 4 |

Výsledky ověření na Macu (2026-10-06, MacBook Pro Intel, macOS 26, Blackmagic RAW Player 4.3 a DaVinci Resolve s knihovnou 4.3,
vzorek `profile.braw` z Blackmagic RAW Speed Test — URSA Mini Pro 4.6K, 4 snímky):

1. `QueryInterface(IID_IBlackmagicRawFactory)` vrací továrnu — chybějící i neplatný klip dává kód 4 (Player i Resolve); kontrola zůstává.
2. Player: `/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks` (potvrzeno). Resolve:
   `/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Frameworks` — **ne** `Contents/Libraries`; opraveno v `kLibrarySearchPaths`
   i `BrawLibrary.searchDirs` (a v očekávání `BrawLibraryTests`). SDK nenainstalováno — cesta neověřena.
3. `gama` a `gamt` jsou řetězce, `csgn` celé číslo; stderr bez varování. Vzorek: „Blackmagic Design Film“, „Blackmagic Design“, **4**.
5. CPU pipeline před `OpenClip`: 4 snímky 4608×2592 plně 0,40 s, ve čtvrtině 0,06 s — před úkolem 27 i po něm stejně (8K/12K vzorek
   není k dispozici).

### D.20 Mapa změn souborů a pokrytí

| Soubor | Úkoly |
|---|---|
| `Tools/braw-decode/braw-decode.cpp` | 27 |
| `Sources/OffloadKit/BrawLibrary.swift` (nový) | 27 |
| `Sources/OffloadKit/Media.swift` (`ClipInfo`) | 28 |
| `Sources/OffloadKit/Luts.swift` (`LutResolver.braw`) | 28 |
| `Sources/OffloadKit/RawBackends.swift` (`BRAWBackend`, `RawDecoderError`) | 28, 29 |
| `Sources/OffloadKit/Tools.swift` (`StderrTail`, `ToolProcess`) | 29 |
| `Sources/OffloadKit/Proxy.swift` (`FrameSource.explainFeederError`, `backends`, `logged`, `thumbnails`, `runFFmpeg`) | 29, 30 |
| `Sources/OffloadKit/FFmpegWatch.swift` (`Flag`) | 30 |
| `Tests/OffloadKitTests/{BrawTestSupport,BrawLibraryTests,BrawDecodeTests,BrawBackendTests,ToolProcessTests,BrawFeederTests,BrawSampleTests}.swift` (nové), `LutResolverTests.swift` | 27–31 |
| `docs/FORMATY.md` | 28 |
| `mac/README.md`, `docs/ROZDILY.md` | 31 |

| Mezera Macu | Kde se zavírá |
|---|---|
| VYVOJ-MAC „Naplánováno“: Mac `braw-decode` (1) gama/gamut/generace, pipeline, LUT podle gamy | úkoly 27, 28 |
| VYVOJ-MAC „Naplánováno“: (2) zaokrouhlený start náhledu BRAW | úkol 6 |
| ROZDILY „Blackmagic RAW (`braw-decode`)“ v záměrných rozdílech; FORMATY `info.gamma…` „zatím jen Windows“ | úkoly 31, 28 |
| Windows plán 3 závěrečné opravy I1, M1–M4 (poznámky, limit, jeden pokus, QueryInterface) | úkoly 27, 29, 30 |
| Zůstává otevřené (jako Windows): Film Gen 4; GUI řádek „braw-decode“ bez ohledu na knihovnu; vše, co ověří jen vzorek | VYVOJ-MAC „Naplánováno“ |

## Doplněk 3 (2026-10-08): Windows plány 6–10 a dodatky 0.5.x — co Mac převezme, jak a proč

Windows verze mezitím došla k **0.6.0** (plány 6–10). Tento doplněk říká, co z toho Mac převezme, **podle formátu a pravidel**
(ne podle jmen tříd Windows), co se na macOS dělá jinak a proč, a co se zrcadlit nedá. Implementují ho úkoly **32–70** plánů
[`../plans/2026-10-08-offload-mac-parity-2.md`](../plans/2026-10-08-offload-mac-parity-2.md) (32–55: plány 6–7) a
[`../plans/2026-10-08-offload-mac-parity-3.md`](../plans/2026-10-08-offload-mac-parity-3.md) (56–70: plány 8–10). Začínají po
úkolech 1–31 (verze 0.2.0). Platí oddíly 1–10 a doplňky 1–2 (texty shodné s Windows, výstupy podle FORMATY.md, UI nikdy nečeká na
disk, test před kódem).

Windows reference: návrh
[`windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md`](../../../../windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md)
(§2 plán 6, §3 plán 7, §4 plán 8, R1–R27), návrh
[`windows/docs/superpowers/specs/2026-10-08-offload-verify-in-proxy-design.md`](../../../../windows/docs/superpowers/specs/2026-10-08-offload-verify-in-proxy-design.md)
(plán 10, R1–R12; zdrojem je specifikace uživatele „Offload — Verify-in-Proxy: technická specifikace“ v Claude Docs),
[`docs/ui-navrh/README.md`](../../../../docs/ui-navrh/README.md) (plán 9, „Rozhodnutí (2026-10-07, závazné)“ v hlavičce Windows
plánu 9), [`docs/FORMATY.md`](../../../../docs/FORMATY.md) (smlouva formátu), [`docs/VYVOJ-WINDOWS.md`](../../../../docs/VYVOJ-WINDOWS.md)
(0.5.1, 0.5.2, „report: uložení do PDF“, 0.6.0) a kód `windows/src/Offload.Core` (jen pro texty a hraniční případy).

### D.21 Přehled

| Windows | Co Mac převezme | Úkoly | Windows reference |
|---|---|---|---|
| Plán 6 (0.2.0) | historie karet (`history.json`, otisk, `fsSerial`+kapacita), doplnění jako generace ASC MHL N, název z klipů, Ověřit zálohu proti všem generacím (report ověření), kontrola místa, české hlášky, zachování neznámých klíčů | 32–50 | návrh §2, R1–R11, R24, R27; `Append/AppendPlanner.cs`, `AppendLock.cs`, `History/*`, `Verification/*`, `Space/SpaceEstimator.cs`, `Messages.cs`, `CardRecord.cs` |
| Plán 7 (0.3.0) | MD5 navíc k xxh64, seznamy klipů CSV/ALE, denní report, proxy pro přelinkování | 51–54 | návrh §3, R12–R16, R25; `Outputs/*`, `ReelNames.cs`, `Hashing.cs` |
| 0.5.1, 2026-10-08 | proxy ve složkách jako na kartě, bez proxy u lehčího originálu, HTML lišta „Uložit jako PDF“, tisk, šířka tabulky, české chyby v HTML | 54, 55 | VYVOJ-WINDOWS 0.5.1 a „report: uložení do PDF“; `ProxyPaths.cs`, `LighterSource.cs`, `HtmlReport.cs` |
| Plán 8 (0.4.0) | projekty, vlastní pravidla LUTů, `braw-decode --gen5`, oznámení + ntfy, kontrola aktualizací; podpis po svém (`codesign` + notarizace) | 56–60 | návrh §4, R17–R23, R26; `Desktop/Projects.cs`, `LutRules.cs`, `Notify/*`, `Updates/UpdateChecker.cs` |
| Plán 9 (0.5.0), 0.5.2 | Pozastavit, Zkopírovat znovu (`neshoda_NNNN`), nový vzhled (po schválení Windows verze), Dnes, štítek kamery, odhad času, klikací „Po zkopírování“ | 61–64 | ui-navrh, rozhodnutí plánu 9; `PauseGate.cs`, `Append/RepairPlanner.cs`, `Desktop/JobCard.cs` |
| Plán 10 (0.6.0) | Verify-in-Proxy (tap, čtení před SDK, pozadí, dorovnání), `verifyPending`/`verifyIncomplete` a potvrzení, blokový manifest a navázání po pádu, XXH128, obsahová kontrola, „Lze vyjmout × Lze formátovat“, `volumeSerial` | 39, 65–70 | verify-in-proxy návrh R1–R12; `VerifyInProxy/*`, `OffloadReport.Verify.cs`, `Append/ResumePlanner.cs` |

**Proč v tomto pořadí:** nejdřív to, na čem stojí bezpečnost dat a kompatibilita formátu (hashe, report, „ověřeno“, dva stavy
karty, ASC MHL, pořadí zápisu, zámky — úkoly 32–41), pak funkce, které je používají. Záloha z Macu musí jít na Windows ověřit,
doplnit, opravit i dokončit a naopak; každý krok, který by mohl přepsat záznam nebo falešně zezelenat, má vlastní test.

### D.22 Pravidla formátu a bezpečnosti dat (závazná pro Mac)

**D.22.1 Pravdou je cíl, historie jen hledá.** Doplnění, oprava, navázání, denní report a Ověřit zálohu čtou reporty všech generací
a soubory na cílových discích (`CardRecord`, úkol 38); `history.json` jen najde složky a ukáže řádek u karty (Windows R4). Historie je
místní (`~/Library/Application Support/Offload/history.json` + `history/<id>.files.gz`, stejný tvar jako Windows — FORMATY „Místní
soubory“), zápis pod zámkem, vyšší verze jen číst, poškozený soubor odložit. Klíč karty `fsSerial` + `capacity` (R1); otisk SHA-256
nad `relpath\tsize\tmtimeTicksUtc` s .NET ticks, aby přenesená historie párovala (úkoly 36, 42).

**D.22.2 Generace a dovolené přepisy.** Na cíli se smí přepsat jen: `<karta>.xxh64` a `<karta>.md5` (**jen prodloužit** — dřívější
bajty beze změny, chybějící řádky z MHL, žádné opakované), `ascmhl/ascmhl_chain.xml` (dřívější záznamy stejné, jeden přibude, zápis
atomicky), odvozené soubory (seznamy klipů, denní report) a reporty generace, kterou úloha právě píše a obnovuje, nebo která ještě
nebyla potvrzená (`verifyPending`). Nikdy: dřívější reporty, dřívější generace MHL, datové soubory, proxy, soubory ověření. Nová
generace = max + 1 a **stejná na všech cílech**; reporty `_NNNN` a soubor generace se vytvářejí „jen nové“ (`renamex_np(RENAME_EXCL)`).
Pořadí: reporty → soubor generace → řetěz (**bod potvrzení**) → seznamy součtů; každé přerušení nechá složku, kterou další doplnění
správně odmítne (`highestReportNumber ≠ poslední generace`) nebo dokončí (seznamy). Generace 1 nikdy přes existující řetěz.
Doplnění odmítne nedostupný cíl, chybějící report/MHL, poškozený řetěz, rozdílné generace, cizí soubor na cíli, jinou kartu, konflikt
velikosti/času (FAT ±celé hodiny), soubor dobrý jen na části cílů (úkoly 40, 41, 46, 47).

**D.22.3 Oprava „Zkopírovat znovu“.** Generace N jako u doplnění, **zaznamenaný hash se nikdy nemění** (z celé kopie nebo doplnění),
karta se přečte a porovná **před** zápisem, dobrá kopie se nikdy nepřepíše (jen přečte → MHL `action="verified"`), nesouhlasná se
přesune do `_REPORT/neshoda_NNNN/<cesta>` (obsazené → `_2`, `_3`), nikdy nesmaže; report `kind: "repair"`, `rewritten`, `setAside`
(úkol 62).

**D.22.4 Report ověření a jeho umístění.** Ověřit zálohu nikdy nezapisuje do `ascmhl/`. Report do `<karta>/_REPORT/` jako
`<karta>_verify_<stamp>Z.html/.pdf` + `offload_verify_<stamp>Z.json`; když historie MHL `_REPORT` neignoruje (cizí program), report
jde do `<datum>/_REPORT/` vedle karty (JSON `<karta>_verify_<stamp>Z.json`) s českou větou o umístění (Windows R8 a oprava po revizi;
úkol 45).

**D.22.5 `verifyPending` a `verifyIncomplete`.** S ověřením během proxy zapíše fáze 1 reporty s `verifyPending: true` **bez generace
MHL a bez seznamů součtů**; potvrzení po ověření zapíše **nejdřív MHL a řetěz**, pak reporty, pak seznamy (idempotentně). Selže-li
potvrzení na některém cíli, složky, kam se výsledek zapsal, dostanou `verifyIncomplete: true`. Obojí znamená **„záloha nedokončená“**:
nikdy „ověřeno“, nikdy „Lze formátovat“, Ověřit zálohu hlásí `unfinished`, oprava se odmítne, „Dokončit přerušenou zálohu“ (doplnění)
dokončí **tutéž** generaci podle manifestu (úkoly 33, 38, 41, 45, 67, 69).

**D.22.6 Jediná definice „ověřeno“.** `CardRecord.readBack` (úkol 38): nejnovější záznam souboru v této složce říká `verified` (nebo
report ověřený a stav ne `written`; selhání je selhání) a report nemá `verifyPending` ani `verifyIncomplete`. Sloupec reportu se
hledá podle sériového čísla svazku a cesty pod kořenem (`volumeSerial`, R12) nebo podle cesty; nenajde-li se (cizí cesty z Windows),
musí `verified` říct **každý** sloupec. Používá ji historie, denní report, doplnění (`unverifiedEarlier`), oprava, `BackupState`
a karta úlohy — nikdo jiný si „ověřeno“ nepočítá.

**D.22.7 „Lze vyjmout“ × „Lze formátovat“.** Vyjmout = vše zapsané a vyprázdněné na všech cílech; formátovat = vše ověřené všude,
výsledek zapsaný všude, u doplnění/opravy celá záloha bez selhání a neověřených souborů, žádný soubor karty mimo zálohu. Zelená,
zvuk úspěchu, oznámení „hotovo“ a CLI kód 0 jen při „Lze formátovat“ bez varování; „Lze vyjmout“ samo je neutrální (Windows R5, I2;
úkol 39).

**D.22.8 Hashe.** `xxh64` vždy (16 hex; `.xxh64` a `hashAlgorithm` beze změny, aby zálohu ověřil starší Offload), `xxh128`
(XXH3-128, 32 hex, kanonicky — horních 64 bitů první) u každého souboru a v MHL vedle `xxh64` (Windows R2), `md5` jen navíc
(`hash: "both"`, `hashAlgorithms`), XXH3-64 bloků jen v manifestu. Doplnění a oprava počítají součty dřívější zálohy (úkoly 32, 51).

**D.22.9 Nastavení.** `UserDefaults["settings"]` se stejným schématem jako Windows `settings.json`; neznámé klíče a nečitelné sekce se
při uložení zachovají (R24), projekty s duplicitami a nečitelnými položkami se nemažou (úkoly 35, 56). Mac-only klíče: `notify.mac`
(Windows `notify.windows` se na Macu zachová beze změny); `updates.manifest` ukazuje na Mac manifest (D.24).

### D.23 macOS technika

**D.23.1 `F_NOCACHE` a jeho meze (ověření během proxy).** Specifikace uživatele (tabulka „Platforma“) říká pro macOS: čtení bez
cache `fcntl(fd, F_NOCACHE, 1)`, **spolehlivé veřejné API pro vyhození z cache není** (záložně `msync(MS_INVALIDATE)` na `mmap`
souboru), `F_NOCACHE` je **jen nápověda** a testem je třeba ověřit, že druhé čtení jde opravdu z disku; hlavní pravidlo — zapisovat
bez cache už ve fázi 1 (na macOS jediná spolehlivá cesta). Otevřená otázka specifikace: „Dá se na macOS spolehlivě ověřit, že čtení
s `F_NOCACHE` nejde z cache, i na APFS a externích discích přes Thunderbolt?“ Mac pravidla (úkoly 65, 66, 68):
1. Zápis na cíle s `F_NOCACHE` (jako od 0.1.0); kontrolní bod manifestu až po `F_FULLFSYNC` souboru (`F_NOCACHE` nezaručuje médium).
2. **Důkaz místo nápovědy:** `mincore` nad sdíleným read-only mapováním souboru ukáže stránky v paměti. Cíl smí být ověřen z čtení
   pro proxy, jen když sonda zapsaná s `F_NOCACHE` + `F_FULLFSYNC` po zavření **nemá v paměti nic**; a jen když `mincore` vůbec vidí
   cache (soubor zapsaný normálně musí ukázat rezidentní stránky — jinak je „slepé“ a vše se ověřuje klasicky).
3. Před každým ověřujícím čtením souboru kontrola rezidence; rezidentní → `msync(MS_INVALIDATE)` a znovu; pořád rezidentní →
   tap se pro soubor nepoužije a klasické čtení se zapíše s poznámkou „Ověřeno čtením přes cache macOS …“ a varováním (obdoba
   Windows ruling I3/C2 — nikdy tiché zelené).
4. Síťové svazky (`statfs` bez `MNT_LOCAL`) vždy klasicky. Příkaz `purge` (celá cache, vyžaduje root) se nepoužívá.

**D.23.2 Sériové číslo svazku.** IOKit a DiskArbitration dávají BSD jméno, médium a UUID svazku, **ne** 32bitové sériové číslo FS,
které Windows ukládá. Mac ho čte z **boot sektoru** (`/dev/rdiskNsM`, 4096 B od nuly, jen čtení): exFAT 0x64, FAT32 0x43, FAT12/16
0x27, NTFS 0x48 (dolních 32 bitů) — stejná hodnota jako Windows `GetVolumeInformation`. Když uzel zařízení nejde otevřít (patří
rootovi) nebo FS není FAT/exFAT/NTFS: `fsSerial = "uuid:<VolumeUUID>"` (karta se pozná na tomtéž Macu) a `volumeSerial` v reportu
`"uuid:<UUID>"` (Mac-only tvar; Windows takový disk nečte, a když ho čte, nepáruje — bezpečný směr: všechny sloupce musí říct
`verified`). Kapacita `statfs` `f_blocks × f_bsize` (úkol 36).

**D.23.3 Časy.** Na Macu je FAT `f_fstypename == "msdos"` (místní čas, tolerance celých hodin), exFAT `exfat` (UTC). Ticks .NET pro
otisk a `.files.gz` se počítají přesně z `timespec`.

**D.23.4 Zámky.** Windows: pojmenované mutexy `Local\Offload-history-<sha256>`, `Local\Offload-append-<sha256(svazek|cesta)>`,
`Local\Offload-day-<sha1>` (jedna relace Windows). Mac: `flock(LOCK_EX|LOCK_NB)` na místních souborech
`~/Library/Application Support/Offload/locks/{history,append,day}-<hash>.lock` (stejné hashe klíčů; klíč složky = UUID svazku + cesta
pod kořenem, velkými). Proč `flock`: zámek patří otevřenému souboru (vylučuje i dvě úlohy v jednom procesu), jádro ho uvolní při pádu
procesu (POSIX pojmenované semafory by zůstaly obsazené) a smí ho uvolnit jiné vlákno (Windows potřebuje držitelské vlákno). Zámkový
soubor **nikdy na cíli** (cíl je smlouva formátu, často síťový). **Zámky Macu a Windows se navzájem nevidí** — Mac a PC, které píšou
do téže sdílené zálohy, se nevylučují, stejně jako dva PC; chrání jen zápis „jen nový“ reportů `_NNNN` a generace MHL (úkol 37).

**D.23.5 Oznámení.** `UNUserNotificationCenter` (macOS 10.14+), ne zastaralé `NSUserNotificationCenter`. Funguje jen v balíčku
s `CFBundleIdentifier` (pod `swift run` se nevolá), oprávnění se vyžádá při zapnutí volby; oznámení jen když aplikace není v popředí
(jinak zvuk a `requestUserAttention` jako dosud); CLI oznámení macOS neposílá, ntfy ano (úkol 59).

**D.23.6 Vložení a výměna karty.** Detekce karet zůstává na `NSWorkspace` notifikacích (`didMount`/`didUnmount`, úkol 11) se
skenem svazků na pozadí; FSEvents ani `DARegisterDiskAppearedCallback` nejsou potřeba (svazek je pro Offload zajímavý až připojený).
DiskArbitration slouží ke čtení faktů (BSD jméno, protokol, UUID — úkol 14). Výměnu karty během pauzy pozná nové zjištění identity
(`CardIdentity` + `fsSerial`/kapacita) před dalším čtením z karty (úkol 61); vytažení při čtení se projeví chybou I/O jako dosud.

**D.23.7 Tap pro ffmpeg.** Proxy dělá ffmpeg i na Macu, proto tap = místní HTTP server na `127.0.0.1` jako Windows R3 (token, id,
Host, jeden rozsah, limity), `-short_seek_size 65536`. `AVAssetResourceLoaderDelegate` (AVFoundation) by obsloužil jen přehrávání
přes AVFoundation, ne ffmpeg — nepoužije se (úkol 68).

**D.23.8 Knihovny.** XXH3-64/XXH3-128 z přibaleného xxHash 0.8.3 (`Sources/CXXHash`); MD5/SHA-1/SHA-512 přes CryptoKit
(`Insecure.MD5`, `Insecure.SHA1` — `CC_MD5` je od 10.15 zastaralé); gzip `.files.gz` = vlastní hlavička + raw DEFLATE z Foundation
(`NSData.compressed(using: .zlib)`) + CRC-32 a délka (přečte i soubor z .NET `GZipStream`). Žádné nové SwiftPM závislosti.

**D.23.9 Zápis bez nahrazení.** `renamex_np(src, dst, RENAME_EXCL)` (APFS, HFS+); kde FS volbu nepodporuje (exFAT, FAT, SMB —
`ENOTSUP`/`EINVAL`), kontrola existence těsně před `rename` (okno souběhu jako Windows `Path.Exists` + `Move` u složky pro proxy;
u doplnění kryté zámkem složky). `IO.writeFileNew` = celý soubor pod dočasným jménem + `F_FULLFSYNC` + přejmenování bez nahrazení.

### D.24 Co Mac nezrcadlí — a co platí místo toho

| Windows | Proč na Macu ne | Pravidlo Macu |
|---|---|---|
| `FILE_FLAG_NO_BUFFERING` + kontrola nezarovnaným čtením (UnbufferedCheck), vyhození z cache zavřením handlu | macOS nemá vynucené čtení mimo cache ani API pro vyhození | `F_NOCACHE` + důkaz `mincore`, `msync(MS_INVALIDATE)`, jinak klasicky s poznámkou (D.23.1) |
| `WRITE_THROUGH` zápis, kontrolní bod hned | `F_NOCACHE` nezaručuje médium | kontrolní bod po `F_FULLFSYNC` souboru |
| Pojmenované mutexy `Local\…`, držitelské vlákno | jiný systém; mutex jen pro vlákno, které ho vzalo | `flock` v `Application Support/Offload/locks`, uvolnitelný odkudkoli; mezi Macem a Windows žádné vyloučení (D.23.4) |
| `fsSerial` z `GetVolumeInformation` | API neexistuje | boot sektor, záložně `uuid:` (D.23.2) |
| `Shell_NotifyIconW`, `notify.windows` | Windows API | `UNUserNotificationCenter`, `notify.mac` (D.23.5) |
| `build.ps1 -Sign`, podepsat jen vlastní exe (R23) | notarizace vyžaduje podpis **všech** Mach-O v balíčku s hardened runtime | `build-app.sh --sign --notarize`, podepíše i přibalený ffmpeg/ffprobe; `braw-decode` s `disable-library-validation` (úkol 60) |
| Manifest aktualizací společný | Mac a Windows se vydávají zvlášť | vlastní `offload-update-mac.json` stejného tvaru (úkol 60) |
| Lokalizace chyb parseru `System.CommandLine` | `swift-argument-parser` lokalizaci nemá | chyby parseru CLI anglicky (ROZDILY) |
| Názvy disků „Zaloha (D:)“ | Mac nemá písmena | „Zaloha (RAID)“ = složka (jméno svazku) |
| Kontroly názvů Windows, FAT32 ≥ 4 GB, buffered I/O fallback, `"unbuffered": false` | Windows-only (spec oddíl 4) | beze změny; Mac klíč `unbuffered` ani `proxyEncoder` nepíše |
| Ukázkový režim `--demo` (plán 9) | pomůcka pro snímky Windows GUI | Mac ho nemá (ROZDILY) |
| Thunderbolt čtečky jako NVMe | Windows Thunderbolt nerozliší | Mac ukáže „Thunderbolt“ (štítek připojení, úkol 64) |

### D.25 Nový vzhled — brána schválení

`docs/ui-navrh/README.md` i rozhodnutí Windows plánu 9: **Mac převezme vzhled až po schválení Windows verze.** Úkoly 63–64 začínají
dotazem na uživatele; neschválený vzhled = úkoly se přeskočí a všechny funkce plánů 6–10 mají ovládání ve stávajícím vzhledu (úkoly
49, 56–62, 70). Rozhodnutí pro Mac: tmavé i světlé téma podle systému, systémové písmo (SF Pro, SF Mono), „Projekt“ místo
„Preset“, šedý rám burn-inu (ne skutečný snímek), „Zkopírovat znovu“ jen nesouhlasné soubory a jen na postižený disk.

### D.26 Testy (nové soubory)

| Oblast | Soubor testů | Úkol |
|---|---|---|
| Hashe, vektory, bloky, registr | `HashingTests.swift` | 32 |
| Klíče reportu, fixture Windows 0.6.0, „jen nový“ zápis | `ReportKeysTests.swift` | 33 |
| Hlášky, kódy, `formatBytesCs` | `MessagesTests.swift`, `PreflightTests.swift` | 34 |
| Nastavení s neznámými klíči | `SettingsStoreTests.swift` | 35 |
| Boot sektor, klíč složky, časy FAT, ticks | `VolumeTests.swift`, `FileTimesTests.swift` | 36 |
| Zámky | `FileLockTests.swift` | 37 |
| `CardRecord`, „ověřeno“, `BackupState` | `CardRecordTests.swift` | 38 |
| Dva stavy karty | `SafeStatesTests.swift` | 39 |
| ASC MHL čtení a generace N | `MHLTests.swift` (+ fixture Windows) | 40 |
| Pořadí zápisu, seznamy součtů, potvrzení | `GenerationWriteTests.swift` | 41 |
| Historie, gzip, otisk | `HistoryStoreTests.swift`, `HistoryMatcherTests.swift`, `ReelNamesTests.swift`, `HistoryJobTests.swift` | 42–44 |
| Ověřit zálohu | `BackupVerifierTests.swift`, `VerifyReportTests.swift` | 45 |
| Doplnění | `AppendPlannerTests.swift`, `AppendEngineTests.swift` | 46–47 |
| Místo | `SpaceTests.swift` | 48 |
| Plán 7 | `HashCopyTests.swift`, `ClipListTests.swift`, `DayReportTests.swift`, `ProxyRelinkTests.swift`, `ProxyPathsTests.swift`, `LighterSourceTests.swift`, `HTMLErrorsTests.swift` | 51–55 |
| Plán 8 | `ProjectsTests.swift`, `LutRulesTests.swift`, `BrawDecodeTests.swift` (gen5), `JobOutcomeTests.swift`, `NtfyClientTests.swift`, `UpdateCheckerTests.swift` | 56–60 |
| Plán 9 | `PauseTests.swift`, `RepairPlannerTests.swift`, `RepairEngineTests.swift`, `SetupTextsTests.swift`, `JobCardTests.swift`, `TimeEstimateTests.swift`, `CardFactsTests.swift` | 61–64 |
| Plán 10 | `CacheProbeTests.swift`, `ManifestTests.swift`, `BlockVerifierTests.swift`, `DeferredVerifyTests.swift`, `RangeServerTests.swift`, `VerifySessionTests.swift`, `ResumeTests.swift`, `ContentCheckTests.swift`, `VerifyInProxyCLITests.swift` | 65–70 |

### D.27 Nejistoty (ověřit na Macu; výsledek do commitu úkolu a sem)

| # | Co | Předpoklad | Jak ověřit |
|---|---|---|---|
| 1 | Čtení boot sektoru karty bez sudo | uzel `/dev/rdiskNsM` vyměnitelné karty smí přihlášený uživatel číst (u obrazů `hdiutil` ano) | úkol 36 krok 3: `ls -l /dev/rdisk*` u skutečné karty ve čtečce (USB, SD slot, Thunderbolt CFexpress); když ne, platí `uuid:` a ROZDILY to řekne **Výsledek 2026-10-10 (úkol 36):** obrazy `hdiutil` patří uživateli (`crw-r----- probudin staff`) a čtou se; skutečné externí disky mají `crw-r----- root operator` a uživatel ve skupině `operator` není → bez sudo nejde, platí `uuid:` (ROZDILY). Karta ve čtečce zatím nevyzkoušená (stejné vlastnictví čekáme). |
| 2 | `fsSerial` a kapacita jako Windows | boot sektor = `vol E:`; `f_blocks × f_bsize` = `TotalNumberOfBytes` | táž karta na Windows a Macu (úkol 36 krok 3) **Výsledek 2026-10-10:** na obrazech exFAT, FAT16 a FAT32 souhlasí sériové číslo s bajty boot sektoru (0x64 / 0x27 / 0x43, LE) a kapacita s „Volume Total Space“ z `diskutil`; srovnání s Windows `vol`/`fsutil` čeká (bez Windows stroje). |
| 3 | `mincore` vidí stránky UBC souboru, který proces nemá namapovaný | ano (stránky jsou v objektu VM souboru) | `CacheProbeTests.testResidentPagesAreDetected`; když selže, Mac verify-in-proxy nepoužije (bezpečně) |
| 4 | Zápis s `F_NOCACHE` nezůstane v cache na APFS, exFAT, HFS+, SMB, Thunderbolt RAID | ano, kromě sítě | `CacheProbeTests` s `OFFLOAD_TEST_DEST` na každém (úkol 65 krok 3) |
| 5 | ffmpeg přes tap: počet čtení a rychlost | ≤ 1,05 × soubor s `-short_seek_size 65536`, ≤ 5 % pomalejší | úkol 68 krok 3; MOV `moov` na konci, MP4, MXF |
| 6 | Oznámení u ad-hoc podepsané aplikace | `requestAuthorization` se zeptá a oznámení dojde | úkol 59 krok 3 |
| 7 | `braw-decode` s hardened runtime načte `BlackmagicRawAPI.framework` | s `com.apple.security.cs.disable-library-validation` ano | úkol 60 krok 4 (BRAW proxy z notarizované aplikace) |
| 8 | `msync(MS_INVALIDATE)` vyhodí čisté stránky souboru | někdy; proto jen pojistka | `CacheProbeTests` — po normálním zápisu `evict` a `residentPages` (výsledek zapsat, na chování nemá vliv) |
| 9 | `PRIO_DARWIN_BG` pro čtení na pozadí | sníží prioritu I/O, nezastaví čtení | úkol 68 krok 3 (rychlost pozadí, dopad na proxy) |
| 10 | Jména souborů NFC/NFD v otisku a historii | karta (FAT/exFAT) vrací jména tak, jak jsou uložená; otisk se shodne s Windows | karta s diakritikou v názvu: otisk Mac × Windows (jen přenos historie — na zálohu vliv nemá) |
| 11 | Gatekeeper a notarizace s přibaleným statickým ffmpeg | podepsaný s hardened runtime projde | úkol 60 krok 4, `spctl -a -vv` |

### D.28 Mapa změn souborů a pokrytí

| Soubor (Mac) | Úkoly |
|---|---|
| `Sources/OffloadKit/Hashing.swift` (nový), `Hash.swift`, `MHL.swift`, `MHLReader.swift` (nový) | 32, 40 |
| `Sources/OffloadKit/Report.swift`, `IO.swift`, `HTMLReport.swift` | 33, 39, 41, 51, 55, 70 |
| `Sources/OffloadKit/Messages.swift` (nový), `Engine.swift` | 34, 47, 48, 51, 61, 62, 65, 67 |
| `Sources/OffloadKit/Volume.swift`, `FileLock.swift`, `CardRecord.swift` (nové) | 36–38 |
| `Sources/OffloadKit/History/*` (nové) | 42–44 |
| `Sources/OffloadKit/Verification/*` (nové) | 45 |
| `Sources/OffloadKit/Append/{AppendPlanner,RepairPlanner,ResumePlanner}.swift` (nové) | 46, 62, 69 |
| `Sources/OffloadKit/Space/SpaceEstimator.swift`, `Outputs/*`, `ProxyPaths.swift`, `LighterSource.swift`, `ReelNames.swift` (nové) | 43, 48, 52–54 |
| `Sources/OffloadKit/Desktop/{AppSettings,Projects,VerifyJob,RepairRules,JobCard,SetupTexts,TimeEstimate,CardFacts}.swift` | 35, 49, 56, 62–64 |
| `Sources/OffloadKit/LutRules.swift`, `Luts.swift`, `Media.swift`, `RawBackends.swift`, `Tools/braw-decode/braw-decode.cpp` | 57, 58 |
| `Sources/OffloadKit/Notify/*`, `Updates/*`, `PauseGate.swift` (nové) | 59–61 |
| `Sources/OffloadKit/VerifyInProxy/*` (nové) | 65–70 |
| `Sources/OffloadKit/Job.swift`, `Proxy.swift`, `ProxyCopy.swift` | 39, 44, 47, 48, 51–54, 57, 61, 62, 67, 68, 70 |
| `Sources/offload/main.swift` | 34, 44, 45, 47, 48, 51–54, 56, 57, 59, 70 |
| `Sources/OffloadApp/*` | 39, 49, 56, 57, 59–64, 69, 70 |
| `scripts/build-app.sh` | 59, 60 |
| `docs/FORMATY.md`, `docs/ROZDILY.md`, `docs/VYVOJ-MAC.md`, `mac/README.md` | každý úkol podle textu; verze 0.3.0 (50), 0.4.0 (55), 0.5.0 (60), 0.6.0 (64), 0.7.0 (70) |

| „Naplánováno“ ve VYVOJ-MAC (stav 2026-10-08) | Kde se zavírá |
|---|---|
| Karty a bezpečnost dat (plán 6) | úkoly 32–50 |
| Výstupy pro produkci a střih (plán 7) | úkoly 51–55 |
| Nastavení, oznámení, distribuce (plán 8) | úkoly 56–60 |
| Nový vzhled (plán 9) — po schválení | úkoly 61–64 (63–64 s bránou D.25) |
| Převzít z Windows — HTML reporty (2026-10-08) | úkol 55 |
| Převzít z Windows 0.6.0 — Verify-in-Proxy | úkoly 39, 65–70 (místo AVFoundation HTTP tap — D.23.7) |
| Převzít z Windows 0.5.1 — proxy | úkol 54 |
| Mimo plán (Film Gen 4 rozhodnutí, řádek „braw-decode nalezeno“ bez knihovny, ostré testy s hardwarem, náhledy R3D/ARRI bez proxy, zrušení `ffprobe`, selhání kopie proxy v souhrnu CLI) | zůstává ve VYVOJ-MAC „Naplánováno — mimo plán“ |
