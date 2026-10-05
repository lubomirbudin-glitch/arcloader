# Offload pro Mac — dorovnání s Windows verzí (návrh)

Datum: 2026-10-05 · Stav: ke schválení · Implementační plán:
[`../plans/2026-10-05-offload-mac-parity.md`](../plans/2026-10-05-offload-mac-parity.md) · Jak začít na Macu:
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
ani nezapisuje** (varování „Cíl 1 je na stejném svazku jako Karta — na kartu se nezapisuje, zápis se neměří; zvol
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

- **LUTy Log → Rec.709 vestavěné** — připravovaný plán 5 pro obě platformy.
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
zaokrouhlení (`BRAWBackend.startFrame`). Návrh: stejná oprava ve Windows plánu 3 (braw-decode).

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

Ruční ověření (na Macu, s hardwarem) je v každém úkolu plánu s přesnými kroky; souhrn v README-START.

## 9. Nejistoty API (ověřit na Macu, než se kód považuje za hotový)

| Co | Předpoklad | Jak ověřit |
|---|---|---|
| Třída řadiče | `AppleUSBXHCI` (podtřídy `AppleUSBXHCIPCI`, `AppleT8103USBXHCI` …), báze `AppleUSBHostController` | `ioreg -p IOService -w0 -r -c AppleUSBXHCI -d 1` |
| Třída portu a číslo | `AppleUSBHostPort`, vlastnost `port` = Data LE | `ioreg -p IOService -l -w0 -r -c AppleUSBHostPort \| grep -E '"port"\|class'` |
| Párování HS/SS | `UsbCPortNumber` u USB-C; jinak neznámé → `nil` | stejný výpis: hledat vlastnost se stejnou hodnotou u `…20…Port` a `…30…Port` jedné zásuvky (kandidáti `UsbCPortNumber`, `UsbCompanionPortPresent`, `kUSBHostPortPropertyCompanion…`) |
| Typ konektoru | `UsbConnector` (ACPI `_UPC`: 9/10/11 USB-C, 255 interní) | výpis portů |
| Zařízení je dítě portu | ano (`IOUSBHostDevice` pod `AppleUSB…Port`) | `ioreg -p IOService -w0 -r -c AppleUSBHostPort -d 2` |
| Porty hubu pod ovladačem hubu | porty hubu visí pod `AppleUSB20Hub`/`AppleUSB30Hub…` (dítě `IOUSBHostDevice`), ne přímo pod zařízením; kód bere obojí | `ioreg -p IOService -w0 -r -c IOUSBHostDevice -d 3` s připojeným hubem |
| Hodnoty `port` a Apple Silicon | na `usb-drd*` jeden HS + jeden SS port, možná oba `port` = 1; kód přečísluje a spáruje | výpis portů (`ioreg … -c AppleUSBHostPort`) na Apple Silicon i Intel Macu |
| Přípona „(Thunderbolt)“ | jen PCI řadič za Thunderbolt předkem | `ioreg -p IOService -w0 -t -r -c AppleUSBXHCI` |
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
| `mac/Tests/OffloadKitTests/*.swift` (nové) | 1–18 |
| `Sources/OffloadKit/Engine.swift` | 2 |
| `Sources/OffloadKit/Report.swift` | 3, 6, 7, 18 |
| `Sources/OffloadKit/Job.swift` | 3, 4, 7 |
| `Sources/OffloadKit/Timecode.swift` (nový) | 5 |
| `Sources/OffloadKit/Proxy.swift` | 5, 6, 7 |
| `Sources/OffloadKit/RawBackends.swift` | 6 |
| `Sources/OffloadKit/HTMLReport.swift` | 7 |
| `Sources/OffloadKit/Hash.swift`, `Bench.swift` | 15 |
| `Sources/OffloadKit/Desktop/{Texts,JobProgress,AppSettings,Volumes,QueueRules}.swift` (nové) | 8, 9, 10 |
| `Sources/OffloadKit/Hardware/{BrakeMeter}.swift` (nový) | 8 |
| `Sources/OffloadKit/Hardware/{UsbSpeeds,UsbTopology,PortLabels}.swift` (nové) | 12 |
| `Sources/OffloadKit/Hardware/{DriveLinks,Advisor,ChainView}.swift` (nové) | 13 |
| `Sources/OffloadKit/Hardware/{IORegistry,MacUsbScanner,MacDriveLinks}.swift` (nové) | 14 |
| `Sources/OffloadKit/Hardware/HwTest.swift` (nový) | 15 |
| `Sources/offload/main.swift` | 16 |
| `Sources/OffloadApp/AppModel.swift` | 9, 10, 11, 17 |
| `Sources/OffloadApp/Views.swift`, `OffloadApp.swift` | 11, 17 |
| `Sources/OffloadApp/{HardwareModel,HardwareView}.swift` (nové) | 17 |
| `mac/README.md`, `docs/ROZDILY.md`, `docs/VYVOJ-MAC.md`, `docs/FORMATY.md` | 18 (+ řádek ve VYVOJ-MAC v každém úkolu) |
