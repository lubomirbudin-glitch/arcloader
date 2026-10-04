# Offload pro Windows — návrh

Datum: 2026-10-04 · Stav: ke schválení

## 1. Cíl

Windows verze aplikace Offload s **plnou funkční paritou** s macOS verzí (`mac/Sources/`, verze 0.1.0):
kopie karty na více disků najednou, ověření zpětným čtením (xxHash64), report (HTML, JSON, TXT, `.xxh64`,
ASC MHL v2, PDF), analýza klipů s náhledy, proxy s LUT / burn-in / frame lines, RAW (BRAW, R3D, ARRIRAW),
GUI i CLI.

### Úspěch

1. Na Windows 11 PC vložím kartu (nebo vyberu složku), vyberu dva disky, spustím offload.
2. Aplikace kartu zkopíruje, ověří, napíše report vč. PDF, vyrenderuje proxy a nabídne vysunutí karty.
3. Výstup ověří oficiální `ascmhl verify` i Mac `offload verify`; výstup z Macu ověří Windows `offload verify`.
4. Všechny automatické testy projdou; E2E proběhne na skutečném médiu.

### Rozhodnutí (odsouhlaseno)

| Otázka | Rozhodnutí |
|---|---|
| Rozsah | plná parita s Mac verzí |
| Technologie | C# / .NET 10, WPF |
| BRAW | port `mac/Tools/braw-decode` do C++ pro Windows |
| Umístění | stejné repo, složka `windows/`; Mac kód se nemění |
| Jazyk UI | čeština, stejné texty a rozvržení jako na Macu |
| Distribuce | přenosná složka / zip, bez instalátoru |
| Cílová platforma | Windows 10/11 x64 |
| Canon RAW Light, Sony X-OCN | kopie + ověření, proxy přeskočeno (CLI dekodér neexistuje) |

### Mimo rozsah

- Instalátor, podepisování kódu, auto-update.
- ARM64 Windows.
- Sdílení kódu se Swift verzí (dvě kódové báze, společné jsou formáty na disku).
- Nové funkce, které Mac verze nemá (kromě kontrol vynucených Windows, viz 4.5).

## 2. Struktura

```
windows/
  Offload.sln
  Directory.Build.props        společné: net10.0-windows, nullable, warnings as errors, verze
  src/
    Offload.Core/              knihovna: veškerá logika, bez UI
    Offload.Cli/               offload.exe — copy | verify | bench (System.CommandLine)
    Offload.App/               Offload.exe — WPF GUI (MVVM)
  tools/braw-decode/           C++ port, CMakeLists.txt → tools/bin/braw-decode.exe
  tests/Offload.Core.Tests/    xUnit
  scripts/build.ps1            publish → windows/build/Offload/ + Offload-win-x64.zip
  docs/
```

Výsledná distribuční složka:

```
Offload/
  Offload.exe                  GUI (self-contained, single file)
  bin/offload.exe              CLI (self-contained, single file)
  bin/ffmpeg.exe, ffprobe.exe, braw-decode.exe
```

CLI je v `bin\` (jako na Macu v `Contents/Resources/bin/offload`): `Offload.exe` a `offload.exe` by ve stejné
složce kolidovaly, protože NTFS nerozlišuje velikost písmen.

Závislosti (NuGet): `System.IO.Hashing`, `System.CommandLine`, `Microsoft.Web.WebView2`, `xunit`.
ffmpeg/ffprobe: build gyan.dev (full), přibalený do `bin/`; `windows/tools/bin/` je v `.gitignore`.

## 3. Mapování modulů Mac → Windows

`Offload.Core` drží rozdělení souborů OffloadKit, aby se obě verze daly porovnávat soubor proti souboru.

| Mac (`mac/Sources/OffloadKit`) | Windows (`Offload.Core`) | Poznámka |
|---|---|---|
| `IO.swift` | `NativeIO.cs` | P/Invoke Win32, viz 4.2 |
| `Engine.swift` | `OffloadEngine.cs`, `BufferPool.cs`, `DestWriter.cs`, `SourceHasher.cs` | GCD → vlákna + fronty, viz 4.1 |
| `Hash.swift` + `CXXHash` | `Hashing.cs` | `System.IO.Hashing.XxHash64`, bez C kódu |
| `Scanner.swift` | `Scanner.cs` | + Windows junk, case kolize, viz 4.5 |
| `Layout.swift` | `Layout.cs` | sanitize navíc pro `<>:"/\|?*` a rezervovaná jména |
| `Verifier.swift` | `Verifier.cs` | 1:1 |
| `Report.swift` | `OffloadReport.cs` | JSON kompatibilní, viz 5 |
| `HTMLReport.swift` | `HtmlReport.cs` | 1:1 (stejné HTML/CSS) |
| `MHL.swift` | `Mhl.cs` | C4 přes `SHA512` z .NET |
| `PDFReport.swift` | `PdfReport.cs` | WebView2, viz 7 |
| `Media.swift` | `Media.cs` (`Clip`, `ClipInfo`, `ClipDetector`, `FFProbe`) | 1:1 |
| `Proxy.swift` | `ProxyRenderer.cs`, `ProxySettings.cs`, `ProxyCodec.cs` | HW enkodéry, cesty, viz 6 |
| `RawBackends.swift` | `RawBackends.cs` | Windows cesty k nástrojům |
| `Tools.swift` | `ExternalTools.cs`, `ToolProcess.cs` | hledání nástrojů, viz 6.4 |
| `Job.swift` | `OffloadJob.cs` | 1:1 (stage 1 copy, stage 2 post) |
| `Bench.swift` | `Bench.cs` | 1:1 |
| `mac/Sources/offload/main.swift` | `Offload.Cli/Program.cs` | stejné příkazy a volby |
| `mac/Sources/OffloadApp/*` | `Offload.App/*` | viz 8 |

## 4. Jádro kopírování

### 4.1 Tok dat a vlákna

Stejný princip jako na Macu: karta se čte **jen jednou**, každý blok jde paralelně do hasheru a do všech cílů.

- `BufferPool`: `bufferCount` (výchozí 16) bufferů po `chunkSize` (výchozí 16 MiB), alokovaných
  `NativeMemory.AlignedAlloc` na 4096 B. `Take()` blokuje přes `SemaphoreSlim`, když jsou všechny v oběhu (backpressure).
- `Chunk`: buffer + délka + offset + počet konzumentů; `Release()` přes `Interlocked.Decrement`, poslední vrací buffer.
- `SourceHasher` a každý `DestWriter` mají **jedno vlastní vlákno** a frontu `BlockingCollection<Action>` —
  ekvivalent sériové `DispatchQueue`. Operace (`Begin`, `Write`, `Finish`, `Abort`) se řadí do fronty v pořadí;
  `Wait()` vloží marker a počká na něj.
- Čtecí smyčka běží na volajícím vlákně (`OffloadJob.RunCopy`).
- Flush a ověření běží paralelně přes cíle (`Parallel.For`, jeden cíl = jedno vlákno).
- Počítadla bajtů: `Interlocked.Add` na `long`.
- Stav pro UI: `Snapshot()` pod zámkem, stejná struktura jako `ProgressSnapshot`.

### 4.2 Windows I/O

| Mac | Windows |
|---|---|
| `open` + `F_NOCACHE` | `CreateFileW` s `FILE_FLAG_NO_BUFFERING \| FILE_FLAG_SEQUENTIAL_SCAN` |
| `pread` / `pwrite` | `ReadFile` / `WriteFile` s `OVERLAPPED` offsetem (synchronní handle) |
| `F_FULLFSYNC` | `FlushFileBuffers` (vyprázdní i cache disku) |
| `F_PREALLOCATE` | `SetFileInformationByHandle(FileAllocationInfo)` |
| `futimens` | `SetFileTime` (jen LastWriteTime) |
| `rename` | `MoveFileExW(MOVEFILE_WRITE_THROUGH)` |
| `unlink` | `DeleteFileW` |
| `st_dev` | sériové číslo svazku (`GetVolumeInformationByHandleW`) |
| `statfs` free / fstype | `GetDiskFreeSpaceExW` / `GetVolumeInformationW` (`NTFS`, `exFAT`, `FAT32`, `ReFS`) |
| `beginActivity(idleSystemSleepDisabled)` | `SetThreadExecutionState(ES_CONTINUOUS \| ES_SYSTEM_REQUIRED)` |

- Zdroj se otevírá `GENERIC_READ`, `FILE_SHARE_READ`. **Na kartu se nikdy nezapisuje.**
- Dlouhé cesty: .NET API je zvládají samo; přímá Win32 volání (`MoveFileExW`) dostávají cestu s prefixem `\\?\`.
  V reportech a UI vždy bez něj.
- Chyby: `IOError(op, path, win32Code)` se zprávou z `FormatMessage`.

### 4.3 Zarovnání při NO_BUFFERING

- Velikost sektoru cíle: `GetFileInformationByHandleEx(FileStorageInfo)` →
  `PhysicalBytesPerSectorForPerformance`; použije se `align = max(4096, sektor)`. `chunkSize` je jeho násobek.
- Čtení: požadavek vždy na celý `chunkSize`; na konci souboru `ReadFile` vrátí méně bajtů — v pořádku.
- Zápis posledního neúplného bloku: délka se zaokrouhlí nahoru na `align` (buffer je dost velký, přesah
  vynulovaný), po posledním zápisu `SetFileInformationByHandle(FileEndOfFileInfo)` na přesnou velikost souboru.
- Prázdný soubor: jen vytvoření, flush, mtime, rename.
- Fallback: když `CreateFileW` s `NO_BUFFERING` selže s `ERROR_INVALID_PARAMETER` nebo zápis selže na zarovnání
  (typicky síťový svazek), cíl přejde na buffered I/O + `FlushFileBuffers` a report u cíle uvede
  `"unbuffered": false` s varováním v TXT a HTML.

### 4.4 Bezpečnost dat

- Rozpracovaný soubor: `.<jméno>.offload-part` ve stejné složce + atribut `FILE_ATTRIBUTE_HIDDEN`.
  Po zápisu: flush → mtime → close → rename na finální jméno → odebrání atributu hidden.
- Soubory `>= 256 MiB` dostanou `FlushFileBuffers` hned; menší se vyprázdní jedním flushem cíle na konci
  (zápis a flush pomocného souboru `.offload-flush`, jako na Macu).
- Reporty, MHL a hash list: zápis do `.<jméno>.tmp` → flush → `MoveFileExW(MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)`.
- Čtecí chyba: 3 opakování po 250 ms; potom soubor `failed` a kopie pokračuje dalším souborem.
- Selhání zápisu na jednom cíli neovlivní ostatní cíle.
- Zrušení: `CancellationToken`; rozpracované `.offload-part` se smažou.
- „Kartu lze vysunout“ jen když `success && verified` (všechny soubory na všech cílech ověřené).

### 4.5 Kontroly před kopírováním (preflight)

Stejné jako na Macu:
- zdroj existuje a je složka; aspoň jeden cíl; cíle existují a jsou zapisovatelné,
- cíl není na stejném svazku jako zdroj,
- dva cíle na stejném svazku jen s `--allow-same-device`,
- volné místo: `totalBytes + files * 64 KiB + 64 MiB` na každém cíli.

Navíc kvůli Windows:
- **case kolize**: dvě cesty na kartě lišící se jen velikostí písmen → chyba preflightu (NTFS/exFAT jsou case-insensitive).
- **nepovolené názvy**: znaky `<>:"\|?*`, řídicí znaky, koncová tečka/mezera, rezervovaná jména (`CON`, `NUL`, `COM1`…)
  → chyba preflightu se seznamem souborů.
- **FAT32 cíl**: soubor ≥ 4 GiB na cíl s FAT/FAT32 → chyba preflightu (jinak by kopie selhala až v půlce).

Scanner — přeskočené („junk“) položky: Mac seznam (`.Spotlight-V100`, `.fseventsd`, `.Trashes`, `.TemporaryItems`,
`.DocumentRevisions-V100`, `.DS_Store`, `._*`) + Windows `System Volume Information`, `$RECYCLE.BIN`, `desktop.ini`, `Thumbs.db`.
Symbolické odkazy a reparse pointy se nekopírují a jdou do `skipped` (jako ne-regulární soubory na Macu).

## 5. Formáty na disku (kompatibilita s Mac verzí)

Musí být **shodné** s Mac verzí, aby šlo ověřovat křížem:

- Struktura: `<root>/<YYYY-MM-DD>/<karta>/`, `_REPORT/`, `_PROXY/`, `ascmhl/`; unikátní název karty `NAME`, `NAME_2`…
- Cesty v reportech, MHL a `.xxh64` vždy s `/`.
- `offload_report.json`: stejné klíče (camelCase podle Swift `Codable`), `sortedKeys`, odsazení, data ISO 8601
  (`yyyy-MM-ddTHH:mm:ssZ`), stejné hodnoty enumů (`pending|written|verified|failed`, `none|skipped|unsupported|done|failed`,
  `video|braw|r3d|arriraw|canonRaw|unknown`, `h264|hevc|proresProxy|…`). `null` hodnoty se vynechají jako ve Swift
  `encodeIfPresent`. Nová pole jen jako volitelná (`unbuffered` u cíle).
- `offload_report.txt`, `<karta>.xxh64` (`<hash>  <cesta>`), `<karta>_report.html`, `<karta>_report.pdf`: stejný obsah a texty.
- ASC MHL v2: stejná struktura, `ascmhl_chain.xml` s C4 (base58 SHA-512, 88 znaků).
- `tool` = `offload`, `version` = `0.1.0-win`.
- Mtime souborů na cílech = mtime na kartě.

## 6. Proxy a RAW

### 6.1 Enkodéry

`EncoderProbe` při prvním použití zkusí zakódovat 1 snímek (`-f lavfi -i color=black:s=256x144 -frames:v 1 -f null -`)
v pořadí a výsledek drží v paměti po dobu běhu procesu:

| Kodek | Pořadí |
|---|---|
| H.264 | `h264_nvenc` → `h264_qsv` → `h264_amf` → `libx264` |
| HEVC | `hevc_nvenc` → `hevc_qsv` → `hevc_amf` → `libx265` (vždy `-tag:v hvc1`) |
| ProRes | `prores_ks` s profilem 0–3, `-vendor apl0` (beze změny) |

Bitrate argumenty podle enkodéru (`-b:v`, `-maxrate` = 1,5×; NVENC `-rc vbr`; x264/x265 `-preset fast`).
Report uvádí použitý enkodér.

Dekódování: `-hwaccel auto` místo `-hwaccel videotoolbox`.

### 6.2 Filter graph bez escapování

ffmpeg se spouští s pracovní složkou `tempDir` (`%TEMP%\offload-<id>`). Do ní se zkopíruje LUT jako `look.cube`,
font jako `font.ttf` (`C:\Windows\Fonts\consola.ttf`, záloha `cour.ttf`) a texty burn-inu jako `label-<id>.txt`.
Ve filtrech se používají jen tato relativní jména — žádné `C:` ani `\` ve filter graphu.

Zbytek beze změny proti Macu: crop na frame lines, scale, `setsar=1`, LUT v `rgb48le` (tetrahedral), drawbox +
drawtext (název vlevo, TC vpravo), TC stopa `-timecode`, výstup `.<jméno>.mov.part` → rename, kopie na všechny cíle,
náhledy 180 px pro report (u R3D/ARRI z hotové proxy), unikátní jména proxy.

### 6.3 Procesy

- `ToolProcess`: `System.Diagnostics.Process`, stdout po řádcích, posledních 4000 znaků stderr pro chybové hlášky.
- Roura dekodér → ffmpeg (BRAW): vlákno kopíruje `feeder.StandardOutput.BaseStream` → `ffmpeg.StandardInput.BaseStream`
  (bloky 4 MiB).
- Průběh: `-progress pipe:1`, `out_time_us` / délka klipu.
- Zrušení: `Process.Kill(entireProcessTree: true)` pro ffmpeg i dekodér.

### 6.4 Hledání nástrojů

Pořadí: explicitní cesta z nastavení → `bin\` vedle exe → vývojové `windows\tools\bin\` (až 5 úrovní nahoru) →
`%LOCALAPPDATA%\Offload\bin` → `PATH` → známé instalační cesty:

- REDline: `C:\Program Files\REDCINE-X PRO 64-bit\REDline.exe` (+ varianty v `Program Files`).
- art-cmd: `C:\Program Files\ARRI\ARRI Reference Tool\art-cmd.exe` (+ `%LOCALAPPDATA%\Offload\arri\art-cmd.exe`).
- Přesné cesty se ověří při implementaci podle skutečných instalátorů; každou lze přepsat v nastavení.

### 6.5 RAW backendy

| Formát | Windows | Logika |
|---|---|---|
| BRAW | `braw-decode.exe` | 1:1 s Macem (scale podle cílové výšky, RGB48 rourou) |
| RED R3D | `REDline.exe` | 1:1 (ProRes LT meziprodukt, IPP2 Log3G10 s LUT) |
| ARRIRAW / HDE / ARRICORE | `art-cmd.exe` | 1:1 (log podle názvu LUT / metadat, crop před debayerem, parametry JSON) |
| MXF s ARRIRAW | překlasifikace jako na Macu | |
| Canon RAW Light, X-OCN | — | kopie + ověření, proxy přeskočeno s poznámkou |

`art-cmd` se spouští s pracovní složkou v tempu (píše `art.log`).

### 6.5a Tři snímky na klip (Windows i Mac)

Doplněno 2026-10-04 na žádost uživatele; mění i Mac verzi.

- Každý klip v reportu má **tři snímky: první, prostřední a poslední frame** (místo jednoho náhledu ve 30 % délky).
- Časy: první = 0, prostřední = `frames / 2` (nebo `duration / 2`), poslední = `frames − 1` (nebo `duration − 1/fps`);
  u klipů bez známé délky jen první snímek.
- Výška 180 px, LUT aplikovaný stejně jako dnes u náhledu; JPEG vložený v HTML i PDF.
- `ClipEntry.thumbnail` → `thumbnails` (pole až tří JPEG); v JSON se dál neukládají.
- HTML: v řádku klipu tři náhledy vedle sebe s popiskem TC (nebo čísla framu) pod každým; v tisku (PDF) menší.
- Zdroj snímků:
  - video (ffmpeg): `-ss` na daný čas, 1 frame; poslední frame přes `-sseof -1/fps` s `-update 1`, aby se trefil opravdu poslední,
  - BRAW: `braw-decode --start N --count 1`,
  - R3D / ARRIRAW: z hotové proxy; když se proxy nerenderují, dekodér vytáhne tři jednotlivé framy
    (REDline `--start/--end`, art-cmd s rozsahem framů) do tempu.
- Mac: stejná změna v `mac/Sources/OffloadKit/Proxy.swift`, `Job.swift`, `Report.swift`, `HTMLReport.swift`.
  **Na tomto PC ji nelze sestavit ani otestovat** — Mac část se dokončí a ověří na Macu.

### 6.6 braw-decode pro Windows

- Port `mac/Tools/braw-decode/braw-decode.cpp` do `windows/tools/braw-decode/`, MSVC (C++17), CMake.
- Stejné CLI: `--info [--scale S] clip`, `[--scale S] [--start N] [--count N] clip`; stejný JSON a výstup RGB48LE.
- Windows SDK API je COM (`IUnknown`, `BSTR` místo `CFStringRef`); factory přes
  `CreateBlackmagicRawFactoryInstanceFromPath` z Windows dispatch souboru SDK.
- Hledání `BlackmagicRawAPI.dll`: složka exe → `C:\Program Files\Blackmagic Design\Blackmagic RAW\Blackmagic RAW SDK\Win\Libraries`
  → Blackmagic RAW Player → `C:\Program Files\Blackmagic Design\DaVinci Resolve`.
- stdout v binárním režimu (`_setmode(_fileno(stdout), _O_BINARY)`), `CoInitializeEx` na začátku.
- **Předpoklady:** Visual Studio workload „Vývoj desktopových aplikací pomocí C++“; Blackmagic RAW SDK pro Windows
  (zdarma, stahuje uživatel z blackmagicdesign.com — hlavičky a dispatch soubor se do repa nedávají, pokud to licence nedovolí;
  build skript je hledá v cestě z proměnné `BRAW_SDK`).

## 7. PDF report

- `PdfReport.Write(report, path)`: vlastní STA vlákno s WPF `Dispatcher` (funguje z GUI i CLI).
- `CoreWebView2Environment` s user data folder `%LOCALAPPDATA%\Offload\WebView2`; controller na message-only okně (`HWND_MESSAGE`).
- HTML (`HtmlReport.Render(report, forPrint: true)`) se zapíše do temp souboru a otevře přes `file:///`
  (`NavigateToString` má limit 2 MB, náhledy ho překročí).
- `PrintToPdfAsync`: A4 na šířku (11,69 × 8,27 in), okraje 28 pt (0,389 in), pozadí ano, bez hlaviček/patiček.
  Zápis do `.<jméno>.tmp` → rename.
- Timeout 60 s. Selhání PDF se zapíše do logu a **nikdy neshodí offload**.
- Chybějící WebView2 Runtime (Windows 10 bez Edge) → PDF se přeskočí s hláškou.

## 8. GUI (WPF)

### 8.1 Rozvržení

Jako na Macu. Okno rozdělené na:
- **vlevo** panel nastavení se sekcemi *Karty*, *Cíle*, *Struktura a kontrola*, *Proxy*, *Nástroje*
  a dolní lištou se souhrnem („1 karta · 2 cíle · proxy“) a tlačítkem **Spustit offload** (Ctrl+Enter),
- **vpravo** *Fronta* s kartami úloh, tlačítko *Vyčistit hotové*, prázdný stav „Zatím žádný offload“.

Vzhled: WPF Fluent theme (`ThemeMode="System"`), světlý i tmavý režim podle systému. Texty 1:1 z `Views.swift`
(„Finder“ → „Průzkumník“).

### 8.2 Detekce svazků

- `VolumeWatcher`: skryté okno přijímá `WM_DEVICECHANGE` (`DBT_DEVICEARRIVAL`/`REMOVECOMPLETE`, `DBT_DEVTYP_VOLUME`);
  při změně a při startu se svazky načtou znovu (s debounce 500 ms).
- Kandidáti: `DriveInfo.GetDrives()` připravené jednotky, které nejsou systémový disk:
  `DriveType.Removable`, nebo `Fixed` s `BusType` USB/SD/MMC/1394/Thunderbolt (`IOCTL_STORAGE_QUERY_PROPERTY`).
- „Vypadá jako karta“: 1:1 s Macem (markery `DCIM`, `PRIVATE`, `CLIP`, `CONTENTS`, `XDROOT`, `M4ROOT`, `AVF_INFO`,
  `CANONMSC`, `BRAW`, `MISC`; přípony `braw r3d rdm rdc mxf mov mp4 ari crm` do dvou úrovní).
- Nově připojená karta se automaticky zaškrtne; svazek, na kterém je cíl, se jako karta nenabízí.
- Ruční zdroj: tlačítko *Složka…* (`OpenFolderDialog`).
- Název karty: jmenovka svazku (`GetVolumeInformationW`), jinak název složky; lze přepsat.

### 8.3 Fronta a souběh

- `MainViewModel` + `JobItemViewModel` (`INotifyPropertyChanged`), `DispatcherTimer` 330 ms obnovuje stav.
- Kopie běží **sériově** na jedné frontě (sdílené cílové disky); stage 2 (analýza + proxy) na druhé frontě,
  takže další karta se kopíruje, zatímco se renderují proxy předchozí.
- Karta úlohy 1:1 s Macem: název, počet souborů a velikost, cíle s `datum/karta`, průběh fáze,
  rychlosti „Karta X MB/s · D1 … · D2 …“, odhad zbývajícího času, aktuální soubor/klip, zelený pruh
  „Všechny soubory jsou na N discích a ověřené — kartu lze vysunout.“ s tlačítkem *Vysunout kartu*,
  červené varování při chybách, souhrn po dokončení, tlačítka *Report*, *Průzkumník*, *Zrušit*.

### 8.4 Vysunutí karty

`FSCTL_LOCK_VOLUME` → `FSCTL_DISMOUNT_VOLUME` → `CM_Request_Device_EjectW` na rodičovské zařízení svazku
(`CM_Get_Parent` z device instance). Selhání (např. soubor otevřený jiným procesem, `PNP_VetoType`) → hláška
„Kartu se nepodařilo vysunout: …“.

### 8.5 Upozornění

- Ověřeno a lze vysunout: `SystemSounds.Asterisk` + toast notifikace.
- Chyba: `SystemSounds.Hand` + `FlashWindowEx` v hlavním panelu.

### 8.6 Nastavení

`%APPDATA%\Offload\settings.json`, schéma `AppSettings` z Macu (`destRoots`, `verify`, `analyzeClips`, `proxy`, `tools`).
Neznámé nebo chybějící klíče → výchozí hodnoty (jako `ProxySettings.init(from:)`).

## 8a. HW diagnostika: USB porty a propustnost

Doplněno 2026-10-04 na žádost uživatele.

### 8a.1 Co se dá a nedá změřit

- Windows prozradí **topologii**: USB řadiče (xHCI, USB4/Thunderbolt), huby, porty, co je v kterém portu,
  co port umí (USB 2.0 / USB 3.x / SuperSpeedPlus) a **dohodnutou rychlost linky** zapojeného zařízení
  (480 Mb/s, 5, 10, 20, 40 Gb/s) — SetupAPI + `IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX(_V2)`,
  `IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES`, `IOCTL_STORAGE_QUERY_PROPERTY` (vazba svazek → disk → USB zařízení).
- **Propustnost prázdného portu změřit nejde** — měří se vždy zapojené zařízení (čtečka s kartou, disk).
- **Kabel samostatně změřit nejde.** Pozná se nepřímo: zařízení umí USB 3, ale linka běží jako USB 2
  → „pravděpodobně kabel nebo port“.
- Rychlost linky je strop daný nejslabším z port + kabel + zařízení; skutečná rychlost je naměřená.

### 8a.2 Test při startu

- **Při každém spuštění** se aplikace zeptá „Prověřit USB porty?“ (Ano / Ne).
- **Rychlá část** (sekundy, nic nezapisuje): topologie, rychlosti linek, varování.
- **Měření** (volitelné, na potvrzení): pro každé zapojené úložiště krátký test — čtení ~1 GB (karta/čtečka)
  nebo zápis + čtení ~1 GB do dočasného souboru (disky, smaže se). Na kartu se nikdy nezapisuje.
- Výstup = **doporučení**:
  - kam zapojit čtečku a kam cílové disky (nejrychlejší porty, pokud možno čtečka a disky na různých řadičích),
  - které porty na offload nepoužívat (USB 2.0) a k čemu se hodí (klávesnice, myš…),
  - zařízení běžící pod svými možnostmi („WD Elements umí 5 Gb/s, běží na 480 Mb/s — vyměň kabel nebo port“),
  - sdílený řadič/hub („čtečka i oba disky jdou přes jeden hub — dělí se o 5 Gb/s“),
  - odhad rychlosti offloadu s aktuálním zapojením (min. z naměřených rychlostí karty a disků).
- Porty se v UI pojmenují podle řadiče a čísla portu („Intel USB 3.20 xHCI · port 3“); fyzické umístění Windows
  nezná — uživatel může portu dát vlastní název („přední levý“), uloží se v nastavení.
- Výsledek posledního testu se uloží (`%APPDATA%\Offload\hwtest.json`) a zobrazí v sekci *Hardware*.
- CLI: `offload ports [--measure]` vypíše totéž textově.

### 8a.3 Propustnost během kopírování

Karta úlohy (GUI) a stavový řádek (CLI) ukazují pro **každý článek řetězu**:

```
Karta  E: SanDisk 256GB  ── čtečka (USB 3.2 Gen1, 5 Gb/s) ── řadič Intel xHCI   312 MB/s  ▓▓▓▓▓▓▓░ 50 % linky
D1     D: WD Elements    ── USB 3.0 (5 Gb/s) ── řadič Intel xHCI                 185 MB/s  ▓▓▓░░░░░ 30 % linky
D2     F: Samsung T7     ── USB 3.2 Gen2 (10 Gb/s) ── řadič ASMedia               311 MB/s
Řadič  Intel xHCI: 497 MB/s ze sdílených 5 Gb/s
Úzké hrdlo: karta / čtečka
```

- Naměřená rychlost: čtení karty, zápis a ověření každého disku (už v `ProgressSnapshot`).
- Strop linky: dohodnutá rychlost z topologie (8a.1), využití v %.
- Součet přes sdílený řadič/hub.
- Úzké hrdlo = článek, který brzdí (nejnižší rychlost; při > 85 % linky „linka je plná“).
- Interní (SATA/NVMe) disky: typ sběrnice místo USB linky, bez % linky.

### 8a.4 Zařazení

Nový plán **„HW diagnostika“** (mezi plány 2 a 4): modul `Offload.Core/Hardware/` (topologie, mapování
svazek → USB, měření, doporučení), `offload ports`, rozšíření stavového řádku CLI. GUI části (dotaz při startu,
sekce *Hardware*, řetěz na kartě úlohy) jsou v plánu 4.

## 9. CLI

`offload.exe` se stejnými příkazy a volbami jako Mac `offload`:

- `offload copy <source> -d|--dest <root> [-d …] [--name] [--date] [--chunk-mb 16] [--buffers 16] [--no-verify]
  [--allow-same-device] [--dry-run] [--no-analyze] [--proxy] [--lut] [--proxy-height 1080] [--frame-aspect]
  [--frame-scale 100] [--proxy-codec h264] [--proxy-bitrate 10] [--no-burn-in]`
- `offload verify <cardPath>`
- `offload bench [--read <card|file>] [--write <folder> …] [--gb 2]`

Názvy a výchozí hodnoty jsou převzaté ze `mac/Sources/offload/main.swift`. Výstup průběhu na konzoli jako na Macu.
Exit kód jako na Macu: 0 = vše hotovo a ověřeno, 1 = chyba (preflight, selhané soubory, neúspěšné ověření).

## 10. Testování

1. **Unit (xUnit):** `Layout` (sanitize, unikátní složka), `Scanner` (junk, skipped, case kolize, nepovolené názvy),
   `ClipDetector` (R3D segmenty, ARI sekvence, SUB/PROXY, prázdné soubory), `ProxySettings.FrameCrop`,
   `RawBackends.Rational`, xxh64 proti referenčním vektorům, C4 proti referenční implementaci, escapování v MHL,
   JSON round-trip a tvar JSON (golden file sestavený podle Swift `Codable`).
2. **Integrace enginu** (temp složky, `allowSameDevice`): kopie na 2 cíle (obsah, mtime, žádné `.offload-part`),
   velikosti souborů 0, 1, 4095, 4096, 4097 B, 16 MiB − 1, 16 MiB, 16 MiB + 1; prázdné složky; zrušení uprostřed;
   poškození cíle po kopii → `Verifier` hlásí nesoulad; zdroj změněný během kopie; nedostatek místa (preflight);
   cíl nezapisovatelný.
3. **Kompatibilita:** `pip install ascmhl` → `ascmhl verify` nad výstupem Windows verze.
4. **Proxy pipeline:** syntetické klipy z ffmpeg (`testsrc2` s TC + `sine` audio) → H.264, HEVC, ProRes; s LUT,
   burn-in, frame lines; zrušení renderu; výběr enkodéru (NVENC na tomto PC, fallback při vynucení).
5. **PDF:** report s náhledy > 2 MB → PDF vznikne, má A4 na šířku.
6. **E2E na tomto PC:** skutečná karta nebo složka → 2 cíle → report + PDF + proxy → vysunutí. RAW vzorky:
   BRAW (Blackmagic), ARRIRAW (ARRI FTP); R3D jen pokud je k dispozici vzorek a REDline.

**Hotovo** = všechny automatické testy projdou, `ascmhl verify` OK, E2E na skutečném médiu proběhne,
GUI ručně otestované (detekce karty, fronta, vysunutí).

## 11. Rizika

| Riziko | Dopad | Opatření |
|---|---|---|
| BRAW SDK pro Windows vyžaduje registraci | BRAW proxy nepůjdou bez SDK | uživatel stáhne SDK; bez něj BRAW funguje jako Canon RAW (kopie + ověření) |
| Chybí C++ toolchain | braw-decode nejde sestavit | doinstalovat VS workload C++ |
| NO_BUFFERING nefunguje na některých svazcích | pomalejší / chyba zápisu | automatický fallback na buffered (4.3) |
| Cesty k REDline / art-cmd na Windows | RAW proxy se nenajdou | ověřit při implementaci, nastavitelné v GUI |
| WebView2 Runtime chybí (Win10) | bez PDF | PDF se přeskočí, HTML report zůstává |
| Ovladače zařízení blokují vysunutí | nelze vysunout z aplikace | jasná hláška; vysunout ručně |
| Některé USB řadiče/huby (zejména Thunderbolt docky) nevrací rychlost linky | chybí strop a % linky | zobrazit jen naměřenou rychlost, „rychlost linky neznámá“ |
| Mac část tří snímků nejde otestovat na Windows | chyba se projeví až na Macu | Mac změna jako samostatný commit, ověření na Macu před sloučením |
