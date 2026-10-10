# Offload — rozdíly mezi Mac a Windows verzí

Obě verze dělají totéž a zapisují **stejné výstupy na disk** (viz [FORMATY.md](FORMATY.md)) — kopii udělanou
na Macu ověří Windows a naopak. Liší se technologií, některými platformními detaily a stavem vývoje.

Pravidlo: když změna v jedné verzi způsobí, že se verze v něčem rozejdou (funkce, chování, formát),
aktualizuje se tento soubor ve stejném commitu. Historie každé verze je v [VYVOJ-MAC.md](VYVOJ-MAC.md)
a [VYVOJ-WINDOWS.md](VYVOJ-WINDOWS.md).

*Stav k 2026-10-10.*

## Přehled

| | Mac | Windows |
|---|---|---|
| Složka | [`mac/`](../mac/) | [`windows/`](../windows/) |
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.2.0C | 0.8.0-win (ve vývoji) |
| Aplikace | `Offload.app` (CLI uvnitř `Contents/Resources/bin/offload`) | `Offload.exe` + CLI `bin\offload.exe` |
| Build | `mac/scripts/build-app.sh` | `windows/scripts/build.ps1` |

## Funkce a stav

| Funkce | Mac | Windows |
|---|---|---|
| Kopie karty na více disků, karta čtená jednou | ✅ | ✅ jádro hotové, testováno |
| Ověření zpětným čtením (xxHash64) | ✅ | ✅ jádro |
| Report JSON / TXT / `.xxh64` | ✅ | ✅ |
| Report HTML | ✅ | ✅ |
| ASC MHL v2 | ✅ | ✅ |
| CLI `copy` / `verify` / `bench` | ✅ | ✅ (čeká ostrý test na skutečné kartě) |
| PDF report | ✅ (WebKit) | ✅ (Edge headless) |
| Analýza klipů, metadata | ✅ | ✅ |
| Náhledy v reportu | ✅ 3 snímky | ✅ 3 snímky |
| Proxy (LUT, burn-in, frame lines, TC) | ✅ | ✅ ověřeno NVENC + software (libx264); QSV/AMF neověřeno (není HW); čeká ostrý test na skutečném médiu |
| Blackmagic RAW | ✅ `braw-decode` (C++, knihovna z SDK / Player / Resolve); ověřeno se vzorkem URSA Mini Pro 4.6K (Film Gen 4, 4 snímky), vzorek Gen 5 chybí | ✅ `braw-decode.exe` (C#, knihovna z SDK / Player / Resolve); čeká ověření se vzorkem .braw |
| RED R3D (REDline) | ✅ | ✅ kód, neověřeno s nástroji |
| ARRIRAW / HDE / ARRICORE (art-cmd) | ✅ ověřeno se skutečným art-cmd (ALEXA 35, ALEXA Mini) | ✅ kód, neověřeno s nástroji (bez LUTu jen `--embedded-look` jako Mac `3b43352`; kód 0 bez výstupu = chyba s poznámkou) |
| Canon RAW Light, Sony X-OCN | kopie + ověření, bez proxy | totéž |
| GUI: detekce karet, fronta, vysunutí | ✅ | ✅ (WPF; čeká ruční test s kartou, vysunutí zatím nevyzkoušené) |
| **Test USB portů na vyžádání + doporučení** | ✅ CLI (`offload ports`) + GUI (okno Hardware ⌘U, vlastní názvy portů; GUI čeká ruční test) | ✅ CLI + GUI |
| **Vestavěné LUTy + automatický výběr podle kamery** (`proxy.lutMode`, `--lut auto`, `offload luts`) | ✅ CLI + GUI (GUI čeká ruční test) | ✅ CLI + GUI (GUI čeká ruční test s klipy z kamer) |
| **Složka pro proxy navíc** (`proxy.extraFolder`, `--proxy-folder`) | ✅ CLI + GUI (GUI čeká ruční test) | ✅ CLI + GUI (GUI čeká ruční test) |
| **Karta už zálohovaná** (`history.json`, řádek u karty, dotaz před celou kopií) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test s kartou) |
| **Doplnit jen nové klipy** (generace ASC MHL N, `offload_report_NNNN`, `offload append`) | ⏳ naplánováno | ✅ CLI + GUI (`ascmhl verify` po doplnění čeká ruční test) |
| **Název karty z klipů** (`A001C003…` → `A001`, další v řadě, `--name auto`) | ⏳ naplánováno | ✅ CLI + GUI |
| **Ověřit zálohu** (všechny generace MHL, hashe xxh64/md5/sha1/xxh3/xxh128/c4, report ověření) | `verify` jen proti `.xxh64` | ✅ CLI `verify` + GUI Nástroje → Ověřit zálohu… |
| **Kontrola místa** (proxy, report, fronta, po svazcích, rezerva 5 %) | jen velikost karty | ✅ CLI + GUI |
| **Hlášky kontrol před kopií česky** (`PreflightCode`) | ✅ (chyby parseru swift-argument-parser anglicky — knihovna je nepřekládá) | ✅ |
| **Denní report** (`<datum>/_REPORT/<datum>_day_report.html/.pdf`, `offload day-report`) | ⏳ naplánováno | ✅ CLI + GUI (čeká ruční test) |
| **Seznam klipů CSV / Avid ALE** (`_clips.csv/.ale` karty i dne) | ⏳ naplánováno | ✅ CLI + GUI (import do Resolve/Avid čeká ruční test) |
| **Proxy pro přelinkování** (`reel_name` ve stopě TC, všechny zvukové stopy) | ⏳ naplánováno (1 zvuková stopa) | ✅ (Resolve/Premiere čeká ruční test) |
| **MD5 navíc k xxh64** (`hash: both`, `--hash both`, `<karta>.md5`, MHL `<md5>`) | ⏳ naplánováno (MD5 ignoruje, xxh64 ověří) | ✅ CLI + GUI |
| **Projekty** (`projects`, `activeProject`, `--project`, `project` v reportu a historii) | ⏳ naplánováno | ✅ CLI + GUI (čeká ruční test) |
| **Vlastní pravidla LUTů** (`proxy.lutRules`, `--lut-rule`, `lut.source = rule`, sériové číslo kamery) | ⏳ naplánováno | ✅ CLI + GUI (čeká ruční test) |
| **BRAW Film Gen 4 → Gen 5** (`braw-decode --gen5`, `proxy.brawGen4ToGen5`, `--braw-gen5`) | ⏳ naplánováno (C++ `--gen5`) | ✅ kód, výchozí vypnuto — **neověřeno**, čeká vzorek .braw Gen 4 |
| **Oznámení** (Windows: oznamovací oblast; ntfy na telefon, `--ntfy`) | Mac má notifikace; ntfy ⏳ naplánováno | ✅ CLI (ntfy) + GUI (čeká ruční test) |
| **Kontrola aktualizací** (manifest z URL / NAS, 1× za 24 h, ve výchozím stavu vypnuto) | ⏳ naplánováno (vlastní manifest) | ✅ GUI (čeká ruční test) |
| **Podpis exe** (`build.ps1 -Sign`, signtool) | `codesign` + notarizace (mimo tento návrh) | ✅ skript (čeká certifikát; nic se nekupuje) |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ✅ CLI + GUI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; řetěz připojení, % linky a úzké hrdlo na kartě úlohy (GUI čeká ruční test) | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |
| **Nový vzhled** (`docs/ui-navrh/`: horní lišta, kroky 1–3, karty úloh po stavech, stránka Nastavení, tmavé i světlé téma) | ⏳ po schválení Windows verze | ✅ GUI 0.5.0 (čeká ruční test) |
| **Pozastavit / Pokračovat** (kopie, ověření, 2. fáze mezi klipy; `copyPausedSeconds`, `verifyPausedSeconds`) | ⏳ naplánováno | ✅ GUI (CLI ne) |
| **Pauza po dokončení souboru / klipu** (rozpracovaný soubor kopie — na všech cílech — a ověření, klip analýzy a proxy doběhne, pak „Pozastaveno“; „Pozastavuji (dokončuji …)“; čas pauzy jen doba stání) | ⏳ naplánováno | ✅ GUI 0.8.0 (čeká ruční test) |
| **Zkopírovat znovu** (oprava: jen nesouhlasné soubory, jen na postižený disk, generace ASC MHL N, `kind: "repair"`, `_REPORT/neshoda_NNNN/`) | ⏳ naplánováno | ✅ GUI (CLI ne) |
| **Dnes a historie karet** (z `history.json`), štítek kamery a připojení, odhad času | ⏳ naplánováno | ✅ GUI |
| **Proxy ve složkách jako na kartě** (`_PROXY/<složka klipu>/<klip>.mov`, plochá starší záloha zůstane plochá) | ⏳ naplánováno (proxy přímo v `_PROXY`) | ✅ 0.5.1 |
| **Bez proxy, když je originál lehčí** (`proxy.skipLighterSources`, `--proxy-always`) | ⏳ naplánováno (renderuje vše) | ✅ CLI + GUI 0.5.1 |
| **Verify-in-Proxy** (primární disk ověřený čtením pro proxy, ostatní na pozadí; `--classic-verify`, `--primary`) | ⏳ naplánováno (zpětné čtení) | ✅ CLI + GUI 0.6.0 |
| **Lze vyjmout × Lze formátovat** (dva stavy karty, oznámení „hotovo“ až po ověření všech disků) | ⏳ naplánováno („kartu lze vysunout“ po ověření) | ✅ GUI + CLI 0.6.0 |
| **XXH128 v ASC MHL** (vedle xxh64) a `files[].xxh128` | ⏳ naplánováno (xxh64) | ✅ 0.6.0 |
| **Blokový manifest a navázání po pádu** (`offload-manifest.jsonl`, „Dokončit přerušenou zálohu“) | ⏳ naplánováno | ✅ 0.6.0 |
| **Obsahová kontrola dekodérem** (`clips[].content`, vady s timecodem a náhledem) | ⏳ naplánováno | ✅ 0.6.0 |
| **Kopie reportu i na počítači** (`_REPORT` + `ascmhl` do `Dokumenty\Offload reporty\<datum>\<karta>\`, obnoví se při každém přepisu; `outputs.localReport`, `--no-local-report`) | ✅ 0.2.0C (`~/Documents/Offload reporty`) | ✅ CLI + GUI (čeká ruční test) |
| **Souhrnný report z vybraných záloh** (zaškrtnuté karty v historii → jeden HTML + PDF do `Offload reporty\souhrn\`, volitelně na vybraný disk; `offload summary-report`; `clips[].info.recorded`) | ⏳ naplánováno | ✅ CLI + GUI (čeká ruční test) |
| **Automatické načtení disků** (externí USB disk → cíl, karta ve čtečce → zaškrtnutá; odebraný disk si pamatuje, `autoDrives`) | ✅ 0.2.0C (zapisovatelné externí disky do cílů, `ignoredVolumes`) | ✅ GUI (čeká ruční test se skutečným diskem a čtečkou) |
| **Snímací fps z metadat prvního snímku** (`clips[].info.captureFrameRate`; „35 fps · přehrávání 24“ v HTML/PDF, TXT, denním reportu; CSV `Capture FPS`, ALE `Sensor_fps`; stopáž z přehrávací rychlosti) | ⏳ naplánováno (report uvádí přehrávací fps souboru) | ✅ 0.8.0: ARRIRAW `sensorSampleRate`, BRAW `sensorRate` (braw-decode --info), RED record fps (přehrávací = projektová), Sony `M01.XML` `captureFps`; Canon zatím jen přehrávací rychlost (chybí vzorek metadat); ostatní video = přehrávací. Čeká ruční test s art-cmd / REDline / vzorkem BRAW |

✅ hotovo · 🔨 rozpracováno · ⏳ naplánováno · ❌ není

## Technologie — co čemu odpovídá

| Oblast | Mac | Windows |
|---|---|---|
| Čtení/zápis mimo cache | `F_NOCACHE` | `FILE_FLAG_NO_BUFFERING` (+ zápis celých sektorů, konec souboru doříznut) |
| Vyprázdnění cache disku | `F_FULLFSYNC` | `FlushFileBuffers` |
| Předalokace | `F_PREALLOCATE` | `preallocationSize` při otevření |
| Atomické přejmenování | `rename`; složka pro proxy `renamex_np(RENAME_EXCL)`, na exFAT (fskit vrací `ENOTSUP`) kontrola existence těsně před `rename` | `MoveFileExW(MOVEFILE_WRITE_THROUGH)` |
| Identita svazku | `st_dev` | GUID svazku (`GetVolumeNameForVolumeMountPointW`) |
| Nespat během offloadu | `beginActivity` | `SetThreadExecutionState` |
| Paralelismus | GCD sériové fronty | vlákno + fronta na cíl (`SerialWorker`) |
| xxHash64 | C knihovna xxhash | `System.IO.Hashing.XxHash64` |
| HW enkodér proxy | VideoToolbox | NVENC → QSV → AMF → software (zjistí se automaticky) |
| Dekódování pro proxy | softwarově (VideoToolbox vypnutý, zelené snímky) | softwarově; volitelně `-hwaccel auto` jen pro H.264/HEVC (`proxy.hardwareDecode`, `--hw-decode`, experimentální) |
| PDF | WebKit tisk; co je širší než A4 na šířku, se před tiskem zmenší | Edge headless `--print-to-pdf`; co je širší než A4 na šířku, se před tiskem zmenší |
| Detekce karet | `NSWorkspace` notifikace | `WM_DEVICECHANGE` |
| Vysunutí | `unmountAndEjectDevice` | `FSCTL_LOCK/DISMOUNT_VOLUME` + `CM_Request_Device_Eject` |
| Nastavení | `UserDefaults` (stejné schéma vč. `portNames`, `proxy.lutMode/lutBuiltin/lutFallback/extraFolder`) | `%APPDATA%\Offload\settings.json` (stejné schéma) |
| USB topologie | IOKit (IORegistry) | cfgmgr32 + IOCTL hubů |
| Kde sedí svazek | DiskArbitration + IORegistry | IOCTL_STORAGE_QUERY_PROPERTY |
| Identita karty | UUID svazku | GUID svazku + sériové číslo FS |
| Ikona aplikace | Offload.icns z shared/icon/Offload.iconset (iconutil) | Offload.ico ze shared/icon (exe i okna) |
| Vestavěné LUTy v balíčku | Offload.app/Contents/Resources/luts | Offload\luts |
| Dekodér BRAW | braw-decode (C++ nad BlackmagicRawAPI.framework, CFBundle) | braw-decode.exe (C# nad COM BlackmagicRawAPI.dll) |

## Záměrné rozdíly

- **Kontroly před kopií navíc na Windows:** názvy lišící se jen velikostí písmen (NTFS/exFAT je nerozliší),
  názvy, které Windows nepovolí (`<>:"|?*`, `CON`, `NUL`…), soubor ≥ 4 GB na cíl s FAT32.
- **Svazky bez přímého zápisu** (některé síťové): Windows přejde na zápis přes cache a report to u cíle uvede
  (`"unbuffered": false` v JSON, „(buffered I/O)“ v TXT, varování v HTML). Mac to nepotřebuje.
- **Skryté systémové soubory:** Windows navíc přeskakuje `System Volume Information`, `$RECYCLE.BIN`,
  `desktop.ini`, `Thumbs.db`.
- **Rozpracované soubory** mají na Windows kromě tečky na začátku i atribut „skrytý“.
- **CLI na Windows je v `bin\`**, protože `Offload.exe` a `offload.exe` by ve stejné složce kolidovaly
  (NTFS nerozlišuje velikost písmen).
- **USB rychlosti a porty (obě verze):** 10 vs 20 Gb/s se nerozlišuje — zobrazuje se „10 Gb/s a víc“; fyzickou polohu
  portu systém spolehlivě nezná, porty pojmenuje uživatel. Windows: rychlost bez neověřeného IOCTL nehlásí. Mac: schopnost
  10 Gb/s z `bcdUSB` nepozná (zařízení 10 Gb/s běžící na 5 Gb/s se nepozná); bez párovacího klíče je USB 2 polovina portu
  „neznámý typ“ (nikdy „jen USB 2.0“). Na MacBook Pro 16,1 leží USB 2 a USB 3 polovina USB-C zásuvky na různých řadičích
  (XHC1 ↔ XHC2/XHC3) — Mac je páruje podle `UsbProtocolCompanion`. Apple Silicon (`usb-drd*`) zatím neověřen.
- **Blackmagic RAW:** obě verze vybírají LUT podle gamy klipu z braw-decode --info (Film Gen 5 → vestavěný LUT; Video, Extended Video a Film Gen 4 bez LUTu — Gen 4 provizorně, ke schválení). Liší se jen technika dekodéru (Mac C++ a framework, Windows C# a COM DLL) a ukončení po časovém limitu --info (Windows ukončí strom procesů, Mac proces braw-decode — ten potomky nemá).
- **Vestavěné LUTy** (obě verze; sdílené soubory v [`shared/luts/`](../shared/luts/)): 11 LUTů Log → Rec.709, `auto`
  vybere LUT podle kamery každého klipu a použitý LUT s důvodem je v reportu (klíč `lut` u klipu, FORMATY.md). Zatím se
  automaticky nepoznají Canon, Panasonic, DJI a Fuji (chybí vzorové klipy, přijdou do záložního LUTu „Když kameru
  nepoznám:“, výchozí Bez LUT); D-Log M nemá vestavěný LUT. ARRI se pozná podle modelu kamery, takže klip z ARRI nahraný
  přímo v Rec.709 by dostal LUT neprávem — uživatel zvolí „Bez LUT“. Na vzorcích se ještě ověří rozsah R3D/ARRIRAW
  mezisouborů a ProRes z ALEXA 35 (LogC4) — berou se jako legální video (Mac: mezisoubory ARRIRAW ověřené jako legální video).
- **Upozornění po dokončení:** Windows hraje zvuk a bliká tlačítkem v hlavním panelu místo notifikace Macu
  (nebalená aplikace umí toast jen po registraci).
- **Vysunutí karty (obě verze):** karta ve čtečce se vysune jako médium, čtečka zůstane připojená; interní disky,
  síťové složky a složky se nevysouvají nikdy. Vysune se jen karta, která se kopírovala — identita zapsaná na začátku
  kopie (Windows: svazek + sériové číslo souborového systému; Mac: UUID svazku, u FAT/exFAT odvozené ze sériového čísla):
  po výměně karty ve stejné čtečce tlačítko zmizí a vysunutí se odmítne; ani dokud z karty kopíruje jiná úloha.
  Skutečné vysunutí zatím není vyzkoušené na kartě ve čtečce (položka 4 ručního testu; Mac ověřen na obrazech disku).
- **Thunderbolt (čtečky CFexpress, externí NVMe):** Mac hlásí externí PCIe/NVMe disky jako „Thunderbolt“; v Kartách se
  zobrazí, pokud macOS hlásí svazek jako externí (neověřeno — chybí Thunderbolt disk; ověřit `volumeIsInternal`:
  `diskutil info /Volumes/X` → Device Location). Windows je bere jako NVMe disk a sám je jako kartu nenabídne (nabízí jen
  vyměnitelná média a USB/SD disky) — kartu v takové čtečce přidejte tlačítkem „Složka…“.
- **GUI navíc na Windows:** zkratka Ctrl+Enter pro spuštění, tlačítko „Průzkumník“, upozornění „Doporučeno NTFS“
  u cíle na exFAT/FAT32 (Mac doporučuje APFS) — jen na Windows.
- **Složka jako argument:** aplikace přijme složku jako argument, takže ji lze přetáhnout na `Offload.exe`.
  `--no-hw-prompt` se přijímá, ale nic nedělá (kontrola USB portů se při startu nespouští).
- **R3D/ARRI bez proxy** zatím nemají náhledy (stejně jako na Macu).
- **Enkodér proxy v reportu:** Windows zapisuje do JSON volitelné `proxyEncoder` (např. `h264_nvenc`, `libx264`)
  a ukazuje ho v řádku „Proxy: H.264 (h264_nvenc), …“ v HTML/PDF. Mac klíč nezná a ignoruje ho.
- **Verze v reportu:** Mac `0.2.0`, Windows `0.3.0-win` — z reportu je vidět, která verze kopírovala.
- **Hardwarové dekódování proxy** (2026-10-08/09): obě verze dekódují H.264/HEVC softwarově (Mac na Intelu dostal
  z VideoToolboxu zelené snímky bez chyby); kódování zůstává hardwarové. Windows ho umí zapnout volbou „Hardwarové
  dekódování (experimentální)“ / `--hw-decode` (`proxy.hardwareDecode`), Mac volbu nemá.
- **Odkazy na stažení dekodérů** (2026-10-10): Windows u chybějícího nástroje ukáže „Stáhnout…“ (Nastavení →
  Dekodéry RAW) a adresu výrobce i v poznámce klipu bez proxy; Mac zatím ne (VYVOJ-MAC, Naplánováno).
- **Vlastní soubor místo vestavěného LUTu:** obě verze — Mac `~/Library/Application Support/Offload/luts/<id>.cube`,
  Windows `%APPDATA%\Offload\luts\<id>.cube`; v seznamu „(vlastní soubor)“. Windows navíc zapíše cestu k souboru do
  důvodu LUTu v reportu (`clips[].lut.reason`).
- **Rychlá analýza ARRIRAW** (2026-10-09): obě verze exportují art-cmd metadata jen prvního snímku (`--duration 1`)
  a počet snímků berou z ffprobe (sekvence `.ari`: počet souborů). Rozdíly: Windows má limit exportu 120 s a ffprobe
  na MXF 60 s (Mac 180 s a 120 s); když zkrácený export selže u klipu, který je podle značek nebo názvu ARRI, Windows
  pustí celý export jako dřív; když ffprobe nedoběhne a export počet neřekne, délka klipu zůstane neznámá (prázdná).
  art-cmd a REDline jdou na Windows zrušit uprostřed dekódování (Mac čeká, až doběhnou).
- **Zámky dne a doplnění** (Windows): klíč je „id svazku + cesta na něm“ — namapovaný disk a jeho UNC cesta, `subst`,
  junction a krátká jména 8.3 dávají jiné klíče a zámek se neuplatní. Mac žádný z těchto zámků nezná (poslední zápis vyhraje).
- **Doplnění z jiného procesu** při přepisu seznamu klipů a denního reportu (stage 2): nehlídá se; vyhraje poslední zápis a
  další zápis reportu soubor obnoví. Soubory ověření v `<datum>\_REPORT\` denní report nikdy nemění.
- **Jména karet** `_REPORT`, `_PROXY`, `ascmhl` dostanou na konec `_` (Windows); `<karta>\_REPORT` vzniká při začátku kopie,
  rozpracovaná karta je v denním reportu „bez reportu (probíhá, nebo byl přerušen)“.
- **MD5** (zatím jen Windows): volba xxHash64 + MD5 přidá `hashAlgorithms`, `files[].md5`, `<karta>.md5` a MHL `<md5>`;
  xxh64 je vždy, takže zálohu s MD5 Mac ověří dál (MD5 přeskočí). Doplnění počítá součty dřívější zálohy.
- **Projekty** (zatím jen Windows): horní klíče `settings.json` jsou profil „Bez projektu“, `projects[]` pojmenované
  profily, `activeProject` (FORMATY.md, settings.json); duplicitní nebo nečitelné položky Windows zachová beze změny.
  Report má navíc `project` (Mac ignoruje), `history.json` ho plní; „další v řadě“ počítá karty jen v rámci projektu.
  Mac uloží totéž do `UserDefaults`.
- **Vlastní pravidla LUTů** (zatím jen Windows): `proxy.lutRules` před automatikou, `clips[].lut.source = rule`, nové
  `clips[].info.cameraSerial`; pravidla neznámého typu se zachovají a přeskočí.
- **Oznámení** (Windows): ikona v oznamovací oblasti přes `Shell_NotifyIconW` (ne WinRT toast — ten u nebalené aplikace
  potřebuje zástupce s AUMID v nabídce Start), jen když okno není v popředí; ntfy jako JSON POST s pevným textem
  (název karty, výsledek a počty; nikdy cesty, názvy souborů ani jméno počítače). Mac má `UNUserNotificationCenter`;
  texty a ntfy převezme.
- **Denní report a seznamy klipů** (zatím jen Windows): `<datum>/_REPORT/` se denním reportem a `_clips.csv/.ale` karty
  i dne jsou odvozené soubory (přepisují se celé); Mac je nečte a jeho `UniqueCardFolder` s `_REPORT` ve složce dne
  nekoliduje. Kdyby Mac zapisoval stejný den, vyhraje poslední zápis.
- **Proxy pro přelinkování** (zatím jen Windows): s timecodem `reel_name` ve stopě TC proxy (cívka: tag kamery, název
  klipu, složka karty; v reportu `clips[].info.reel`), všechny zvukové stopy zdroje (AAC 64 kb/s na kanál, ProRes PCM);
  přejmenovaná proxy (`_2`) má poznámku. Mac dává proxy jen první zvukovou stopu a bez cívky.
- **Oprava — „Zkopírovat znovu“** (zatím jen Windows GUI, od 0.5.0): další generace ASC MHL N ve stejné složce karty
  na **všech** cílech (jako doplnění, stejný zámek složky), report `offload_report_NNNN.*` s `kind: "repair"`,
  `files[].rewritten` a `setAside`; dobrá kopie má v MHL `action="verified"`, nová `original`; nesouhlasná kopie se
  přesune do `_REPORT/neshoda_NNNN/<cesta>` (nemaže se). Mac takovou složku ověří ze seznamu `<karta>.xxh64` (oprava
  neopakuje řádky) a klíče ignoruje; `ascmhl verify` projde, protože `_REPORT` je ignorovaný. CLI opravu nemá.
- **Pozastavení** (zatím jen Windows GUI, od 0.5.0): report `copyPausedSeconds` / `verifyPausedSeconds` (chybí, když se
  nepozastavovalo), rychlosti a TXT `Paused:` bez pauzy. Mac klíče ignoruje — jeho rychlosti by pauzu zahrnuly. Od
  0.8.0 se pauza čeká až po dokončení rozpracovaného souboru (kopie na všech cílech, zpětné čtení, čtení na pozadí
  Verify-in-Proxy) nebo klipu (analýza, proxy) — dřív mezi 16MB bloky; klíče měří jen dobu, kdy úloha stála.
- **Vzhled** (Windows od 0.5.0): podle `docs/ui-navrh/` s přepínačem **projektů** místo presetů, systémovým písmem
  (Segoe UI, Cascadia Mono) a tmavým i světlým tématem podle Windows; Mac převezme po schválení Windows verze. Thunderbolt
  Windows nerozliší (čtečka za ním je „NVMe“ / „USB“), Mac ano.
- **Proxy ve složkách jako na kartě** (Windows od 0.5.1): `_PROXY/idle/1.mov` místo `_PROXY/1.mov` (a `1_2.mov` pro
  `listening/1.mp4`), stejně ve složce pro proxy navíc; `_2` jen při shodě ve stejné složce. Mac dává proxy přímo do
  `_PROXY`. Doplnění na Windows do zálohy z Macu nebo z Windows 0.5.0 pozná plochou zálohu z reportů a zůstane ploché
  (nic se nepřesouvá). `proxyPath` v reportu je relativní cesta jako dřív, jen s podsložkou — Mac ji jen zobrazí.
- **Bez proxy, když je originál lehčí** (Windows od 0.5.1, `proxy.skipLighterSources`, výchozí zapnuto, jen u proxy
  H.264/HEVC — s proxy ProRes se renderuje vždy — a jen když se nepoužije LUT ani ořez): H.264/HEVC originál ne větší než proxy a s datovým tokem nejvýš jako proxy nedostane proxy (`proxyStatus: skipped` + poznámka
  „bez proxy — originál je lehčí (X Mb/s ≤ Y Mb/s)“); originál větší než proxy ji dostane vždy (rozhodnutí: 4K long-GOP
  se ve střižně dekóduje těžce i s nízkým tokem). Mac klíč ignoruje a renderuje proxy pro všechny klipy.
- **Odkaz na proxy v HTML reportu** (Windows od 0.5.1): stav „hotovo“ odkazuje na soubor proxy (`../_PROXY/…`).
- **Verify-in-Proxy** (Windows od 0.6.0): s proxy a ověřením se primární disk ověří z čtení pro proxy, ostatní disky na
  pozadí; report ze stage 1 má `verifyPending` a generace ASC MHL se zapíše až po ověření. Mac klíče `verifyPending`,
  `verifyIncomplete`, `unfinished`, `verifyMethod`, `volumeSerial`, `safeToFormatSeconds`, `verifyNotes`, `xxh128`, `content` ignoruje; záloha z Windows se
  na Macu ověří z `<karta>.xxh64` nebo xxh64 v ASC MHL jako dřív. Report s `verifyPending` (aplikace spadla během ověření)
  Mac považuje za neověřený.
- **CLI `--classic-verify` a `--primary N`** (Windows od 0.6.0, `copy` i `append`): `--primary` mimo 1…počet cílů (`--dest`) skončí českou chybou a kódem 1, nikdy tichým návratem k nejrychlejšímu disku. Kód 1 má i kopie, po které kartu nelze formátovat (výsledek ověření nezapsán, neověřené dřívější soubory; `--no-verify` zůstává 0).
- **Lze vyjmout × Lze formátovat** (Windows od 0.6.0): „kartu lze vysunout“ se rozdělilo — vyjmout po zapsání, formátovat
  až po ověření všech disků; Mac zatím hlásí jen „kartu lze vysunout“ po zpětném čtení.
- **Chyby souborů v HTML česky** (Windows): JSON je sdílený, takže chyby v něm zůstávají anglicky (`size mismatch`,
  `HASH MISMATCH` …); HTML/PDF report je zobrazí česky.
- **Historie karet** (zatím jen Windows, pro Mac naplánováno): `%APPDATA%\Offload\history.json` (formát ve FORMATY.md,
  „Místní soubory“) — řádek „Zálohováno … — ověřeno“ u karty, dotaz „Karty už jsou zálohované“ před celou kopií, CLI
  vypíše řádek před plánem (bez dotazu); `--no-history` historii vypne. Karta se pozná podle sériového čísla souborového
  systému + kapacity, ne podle GUID svazku. Mac čte sériové číslo z boot sektoru (`Volume.swift`) jen tam, kde smí
  číst `/dev/rdiskN` (obrazy disků); skutečné disky a karty mají uzel `root:operator`, takže Mac bez sudo použije
  `uuid:<UUID svazku>` — historie přenesená z Windows kartu na Macu nespáruje (Mac pozná kartu jen ze své historie).
  Totéž `destinations[].volumeSerial` v reportu: Mac píše `1A2B-3C4D` jen po přečtení boot sektoru, jinak `uuid:…`.
- **Doplnění** (zatím jen Windows): generace ASC MHL N a `offload_report_NNNN.*` (FORMATY.md). Mac `verify` čte jen
  `<karta>.xxh64`, který doplnění rozšíří — doplněnou zálohu tedy ověří i Mac. Doplnění drží zámek složky karty (viz
  níže) a odmítne kartu, na které není žádný soubor dřívější zálohy, i zálohu s nedokončeným dřívějším doplněním.
- **Ověření zálohy:** Windows ověřuje proti ASC MHL (všechny generace, i MHL ze Silverstacku/ShotPutu), bez `ascmhl/`
  proti `.xxh64`; Mac zatím jen proti `.xxh64`. Report ověření jde do `_REPORT` karty, a když MHL složky `_REPORT`
  neignoruje (cizí program), do `_REPORT` vedle složky karty — do karty ani do `ascmhl/` se nezapisuje. Výstup `verify`
  na Windows: strojové řádky (`MISMATCH`, `MISSING`, `ERROR`, `EXTRA`, nově `FAILED-AT-COPY`, `FAILED-SUPERSEDED`,
  `CHAIN`) a český souhrn místo `OK`/`FAILED`.
- **Kontrola místa** (chování, ne formát): Windows sčítá požadavky po svazcích (karta + 64 KiB na soubor + 64 MiB, proxy
  podle odhadu z délky a datového toku, report 32 MiB + 1 MiB na klip, čekající a běžící úlohy, složka pro proxy) a před
  renderem proxy přepočítá podle skutečné délky klipů (nevejde-li se, proxy se přeskočí, kopie zůstane ověřená). Mac
  kontroluje jen kartu.
- **Hlášky česky:** kontroly před kopií a chyby CLI („Chyba: …“) jsou v obou verzích česky a doslova stejně (Mac
  píše cesty s `/`); logy, TXT report a `--help` zůstávají anglicky. Chyby parseru jsou česky jen na Windows (kde je
  System.CommandLine přeložené) — swift-argument-parser na Macu lokalizaci nemá (neznámá volba, hodnota, která není
  číslo …). Souhrn `verify` je na Macu zatím anglicky (nové `offload verify` přinese úkol 45).
- **Doplnění do zálohy — zámek složky karty** (Windows): dvě doplnění do stejné zálohy najednou se nepustí (druhé hned
  skončí „S touto zálohou právě pracuje jiná úloha …“; s ověřením během proxy drží zámek úloha až do zápisu výsledku ověření). Zámek platí jen v jedné přihlášené relaci Windows na jednom
  počítači — nechrání před doplněním z jiného účtu ani z jiného počítače do sdílené složky — a stejnou složku pod jiným
  jménem (`subst`, `\\localhost\D$`, krátké jméno 8.3) bere jako jinou. Oprava z jiné relace tak může smazat
  hotovou kopii běžící opravy, která čeká pod skrytým jménem `.offload-part` (třeba během pauzy). Neproběhne to
  potichu: soubor běžící opravy skončí chybou a stačí znovu „Zkopírovat znovu“.
