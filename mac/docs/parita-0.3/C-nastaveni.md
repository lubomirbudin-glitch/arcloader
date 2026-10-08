# Proud C — nastavení, oznámení, distribuce (Windows plán 8 → Mac)

Stav k 2026-10-08. Funkce 11 (projekty), 12 (pravidla LUTů + BRAW Gen 4 → Gen 5), 13 (oznámení), 14 (kontrola
aktualizací). Funkce 15 (podpis) je na Macu mimo rozsah — Mac má vlastní `codesign` + notarizaci.

## Co je hotové

**Kód: nic.** Relace skončila na pevném termínu koordinátora ve fázi analýzy — žádná funkce není rozpracovaná ani
commitnutá, takže není co vracet. Hotová je analýza zdrojů (FORMATY.md, návrh §1, §4.1–4.5, §5, §8, plán 8 části 1–2,
skutečný C# kód ve `windows/src` a testy ve `windows/tests`) a návrh níže, který může další relace rovnou implementovat.

## Zjištění při přípravě (důležité pro koordinátora)

- **Worktree vznikl z `origin/main` (ef25348), ne z místního `main` (48b483f).** `origin/main` ještě nemá Mac 0.2.0
  (žádné `mac/Tests`, `AppSettings`, `Luts.swift` …). Větev jsem posunul `git merge --ff-only main` (rychlé posunutí, bez
  vlastních commitů). Ostatní worktree byly 2026-10-08 13:32 také na ef25348 — ověřit, že se také posunuly.
- Základ na 48b483f: `swift build` OK; `swift test` 272 testů, 0 selhání, 8 přeskočených bez `OFFLOAD_BRAW_SAMPLE`
  (s ním 3 přeskočené). První běh (4 agenti buildují zároveň) měl 1 selhání, druhý 0 — pravděpodobně časově citlivý test
  pod zátěží (asi `FFmpegWatchTests`); při slučování pustit znovu samostatně.
- `mac/Tools/bin` je v tomto worktree skutečná složka s kopiemi (ffmpeg, ffprobe, braw-decode z hlavního checkoutu),
  `braw-decode` jsem **nepřestavoval** — sdílený binár zůstal beze změny. `mac/TestMedia` je symlink (necommitovat).
- Vzorek Gen 4 je na tomto Macu: `/Applications/Blackmagic RAW/Blackmagic RAW Speed Test.app/Contents/Resources/profile.braw`
  (URSA Mini Pro 4.6K, 4 snímky), knihovna z Blackmagic RAW Player 4.3. **Ověření `--gen5` neproběhlo** (kód nevznikl).
- Mac dnes **nemá** `UNUserNotificationCenter` (jen zvuky Glass/Basso a `NSApp.requestUserAttention`) — řádek
  „Mac má notifikace“ v ROZDILY je nepřesný.

## Návrh implementace (pro další relaci)

### 11 Projekty
- `Desktop/Projects.swift`: `SettingsProfile` (destRoots, verify, analyzeClips, proxy, notify + `extra` neznámé klíče,
  `rawSections` nečitelné sekce), `ProjectSettings {name, profile}`, `Projects` (NoProject „Bez projektu“, 60 znaků,
  `nameProblem`, `find`, `saveAs`, `rename`, `delete`, `activate`, `activeName`, `windowTitle`, `deleteQuestion`,
  `unknown`) — texty doslova z `windows/src/Offload.Core/Desktop/Projects.cs`.
- `AppSettings`: horní klíče = profil „Bez projektu“, `projects`, `activeProject` (null zapisovat), `updates`;
  vlastnosti `destRoots/verify/analyzeClips/proxy/notify` přesměrovat na *aktivní* profil (jako Windows
  `MainViewModel.Settings`), takže dnešní vazby GUI a `startJobs` zůstanou beze změny.
- Zachování neznámých klíčů (FORMATY „settings.json“): `JSONValue` (Codable, Equatable); klíče, které verze zná, zjišťovat
  přes `Mirror` (vlastnosti struktur) — odolné proti slučování: pole, které přidá proud A (`hash`, `naming`) nebo B
  (`outputs`), se pak samo zapíše a nebude považováno za neznámé. Duplicitní jméno: platí první, další a položky bez
  platného jména zůstanou syrové za čitelnými; `projects`, které není pole, syrové do uložení projektu; `activeProject`
  neexistujícího projektu → null, jména syrového projektu se zachová.
- `project` v reportu: `OffloadReport.project: String?` (chybí bez projektu), `JobConfig.project`, do enginu přes
  `OffloadOptions.project` (2 řádky v `Engine.run` — kolize s proudem A / MD5). `history.json` je proudu A — jeho záznam
  musí plnit `project` z `JobConfig.project` (doplnění: projekt dřívější zálohy).
- CLI `--project`: nastavení CLI čte z domény `cz.budin.offload` (UserDefaults aplikace), pro testy/vývoj
  `OFFLOAD_SETTINGS_DIR/settings.json`; výslovně zadané volby vyhrávají → volby s výchozí hodnotou v `main.swift` změnit
  na volitelné (`Int?` …), `--dest` nepovinné, když projekt má cíle. Logiku dát do OffloadKit (testovatelné).
- GUI: přepínač projektu nahoře v levém panelu, Uložit jako… / Přejmenovat… / Smazat… (texty Windows), titulek
  „Offload — Film X“, přepnutí nemění zařazené úlohy (JobConfig je hodnota — kopie zadarmo).

### 12 Pravidla LUTů + Gen 4 → Gen 5
- `LutRules.swift`: `LutRule` (match/value/lut/lutPath + `extra`, nepoužitelné pravidlo `raw` beze změny),
  `match`, `describe` („pravidlo 2: kamera obsahuje „FX6““), `use` (Bez LUT → `source none`), `folderOf`, glob `*`/`?`,
  `parseOption` (`--lut-rule`), `problem` (editor), `valid`. Port `windows/src/Offload.Core/LutRules.cs` 1:1.
- `ProxySettings`: uložené `lutRules: [LutRule]?` a `brawGen4ToGen5: Bool?` (nil = klíč chybí, jak chce FORMATY) +
  počítané `rules` / `brawGen5`; řádky v `init(from:)` hned za `lutFallback` (proud P přidává `skipLighterSources` jinde).
- `ClipInfo.cameraSerial` (Sony XML `Device serialNo`, ffprobe `com.apple.quicktime.camera.identifier` /
  `camera_serial_number`), `colorScienceGenOriginal`. Cívka pro pravidlo `reel`: ReelResolver vzniká v proudu P/A —
  do té doby dočasná funkce, po sloučení nahradit `ReelResolver.resolve`.
- Renderer: vlastní LUT podle cesty `lut-custom-<8 hex sha1 cesty>.cube` (dvě pravidla = dva soubory), poznámka
  „LUT nenalezen: x — proxy bez LUTu“; preflight „LUT z pravidla 2 nenalezen: show.cube“.
- `braw-decode.cpp --gen5`: po OpenClip `CloneClipProcessingAttributes`, jen gama „Blackmagic Design Film“ a gen < 5,
  `SetClipAttribute(csgn, Variant U16 = 5)`, zpětné čtení; snímky `CreateJobDecodeAndProcessFrame(clone, nullptr, …)`;
  `--info` účinné hodnoty + `colorScienceGenOriginal`; odmítnutí = varování na stderr, dekóduje se jak natočeno.
  Build do soukromé `Tools/bin` (worktree), nikdy do sdílené. `BRAWBackend(gen5:)`: `--gen5` první argument, starší
  dekodér (exit 2) → jednou varování do logu a dál bez něj. `LutResolver.braw`: „Blackmagic RAW: Film Gen 4 → Gen 5
  (převod SDK)“, „… — převod na Gen 5 se nepodařil“. Ověřit na `profile.braw`: `--info --gen5` → gen 5 + original 4,
  snímek s/bez `--gen5` se liší.

### 13 Oznámení
- `Notify/JobOutcome.swift` (tabulka a texty z `JobOutcome.cs`; Mac `JobStatus` nemá `note`/`warning` → volitelné
  parametry), `NtfyClient` (URLSession, JSON POST na `<server>/`, 10 s, jeden pokus, výsledek místo výjimky, téma
  `[A-Za-z0-9_-]{1,64}`, User-Agent `Offload/<verze>`), testy proti vlastnímu HTTP serveru na 127.0.0.1 (sockety).
- Nastavení: `notify.ntfy {enabled, server, topic}` jako Windows; pro systémová oznámení navrhuji **`notify.mac`**
  (bool, výchozí true) místo `notify.windows` (ten se zachová jako neznámý klíč) — **odchylka, doplnit do FORMATY**.
- Aplikace: protokol `Notifier`, implementace přes `UNUserNotificationCenter` jen v balíčku `.app` (bez bundle ID
  `UNUserNotificationCenter.current()` spadne) a jen když okno není v popředí; povolení žádat při prvním startu úlohy.
  V testech žádné skutečné oznámení. CLI `--ntfy <url>` (server + téma z cesty), čeká nejvýš 11 s.
- `Info.plist`: zvážit `NSAppTransportSecurity/NSAllowsLocalNetworking` (ntfy/manifest přes http v LAN).

### 14 Kontrola aktualizací
- `Updates/UpdateChecker.swift`: port `UpdateChecker.cs` (24 h, `lastCheck` v budoucnosti = hned, jiná adresa = hned,
  64 KB, verze ≤ 32 znaků, url ≤ 1000, notes zkrátit na 2000, řídicí znaky ven, HTTP 10 s, soubor 5 s na vlastním vlákně),
  stav `~/Library/Application Support/Offload/update-check.json` (nebo `OFFLOAD_SETTINGS_DIR`).
- Vlastní manifest Macu: **`offload-update-mac.json`** stejného tvaru `{"version","url","notes"}`; `scripts/build-app.sh`
  s `OFFLOAD_RELEASE_URL` vytvoří `build/Offload-mac-<verze>.zip` (`ditto -c -k --keepParent`) a manifest.
- Odkaz „Nová verze X“: otevřít jen `https://`; místní složku / zip ukázat ve Finderu; síťové cesty (`smb://`,
  `afp://`, `file://server/…`, svazek s `volumeIsLocal == false`) jen zobrazit („Síťová cesta — zkopírujte ji a otevřete
  ručně.“).

## Očekávaná místa kolizí při slučování
`AppSettings.swift` (A: `hash`, `naming`; B: `outputs`), `Proxy.swift` (`ProxySettings` + renderer — P), `Media.swift`
(`ClipInfo`, ffprobe tagy — P přidává `reel`, datový tok), `Engine.swift` (`project` do reportu — A), `Report.swift`,
`main.swift` (volby `copy`), `AppModel.swift`/`Views.swift`.

## Řádky ROZDILY, které by šly pro Mac přepnout
Žádný — implementace neproběhla.
