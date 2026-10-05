# Offload — rozdíly mezi Mac a Windows verzí

Obě verze dělají totéž a zapisují **stejné výstupy na disk** (viz [FORMATY.md](FORMATY.md)) — kopii udělanou
na Macu ověří Windows a naopak. Liší se technologií, některými platformními detaily a stavem vývoje.

Pravidlo: když změna v jedné verzi způsobí, že se verze v něčem rozejdou (funkce, chování, formát),
aktualizuje se tento soubor ve stejném commitu. Historie každé verze je v [VYVOJ-MAC.md](VYVOJ-MAC.md)
a [VYVOJ-WINDOWS.md](VYVOJ-WINDOWS.md).

*Stav k 2026-10-05.*

## Přehled

| | Mac | Windows |
|---|---|---|
| Složka | [`mac/`](../mac/) | [`windows/`](../windows/) |
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.1.0 | 0.1.0-win (ve vývoji) |
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
| Blackmagic RAW | ✅ `braw-decode` | ✅ `braw-decode.exe` (C#, knihovna z SDK / Player / Resolve); čeká ověření se vzorkem .braw |
| RED R3D (REDline) | ✅ | ✅ kód, neověřeno s nástroji |
| ARRIRAW / HDE / ARRICORE (art-cmd) | ✅ | ✅ kód, neověřeno s nástroji |
| Canon RAW Light, Sony X-OCN | kopie + ověření, bez proxy | totéž |
| GUI: detekce karet, fronta, vysunutí | ✅ | ✅ (WPF; čeká ruční test s kartou, vysunutí zatím nevyzkoušené) |
| **Test USB portů na vyžádání + doporučení** | ✅ CLI (`offload ports`) + GUI (okno Hardware ⌘U, vlastní názvy portů; GUI čeká ruční test) | ✅ CLI + GUI |
| **Vestavěné LUTy + automatický výběr podle kamery** (`proxy.lutMode`, `--lut auto`, `offload luts`) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test s klipy z kamer) |
| **Složka pro proxy navíc** (`proxy.extraFolder`, `--proxy-folder`) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test) |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ✅ CLI + GUI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; řetěz připojení, % linky a úzké hrdlo na kartě úlohy (GUI čeká ruční test) | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |

✅ hotovo · 🔨 rozpracováno · ⏳ naplánováno · ❌ není

## Technologie — co čemu odpovídá

| Oblast | Mac | Windows |
|---|---|---|
| Čtení/zápis mimo cache | `F_NOCACHE` | `FILE_FLAG_NO_BUFFERING` (+ zápis celých sektorů, konec souboru doříznut) |
| Vyprázdnění cache disku | `F_FULLFSYNC` | `FlushFileBuffers` |
| Předalokace | `F_PREALLOCATE` | `preallocationSize` při otevření |
| Atomické přejmenování | `rename` | `MoveFileExW(MOVEFILE_WRITE_THROUGH)` |
| Identita svazku | `st_dev` | GUID svazku (`GetVolumeNameForVolumeMountPointW`) |
| Nespat během offloadu | `beginActivity` | `SetThreadExecutionState` |
| Paralelismus | GCD sériové fronty | vlákno + fronta na cíl (`SerialWorker`) |
| xxHash64 | C knihovna xxhash | `System.IO.Hashing.XxHash64` |
| HW enkodér proxy | VideoToolbox | NVENC → QSV → AMF → software (zjistí se automaticky) |
| Dekódování pro proxy | VideoToolbox (-hwaccel videotoolbox) jen pro H.264/HEVC | -hwaccel auto jen pro H.264/HEVC |
| PDF | WebKit tisk | Edge headless `--print-to-pdf` |
| Detekce karet | `NSWorkspace` notifikace | `WM_DEVICECHANGE` |
| Vysunutí | `unmountAndEjectDevice` | `FSCTL_LOCK/DISMOUNT_VOLUME` + `CM_Request_Device_Eject` |
| Nastavení | `UserDefaults` | `%APPDATA%\Offload\settings.json` (stejné schéma) |

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
- **USB rychlosti a porty:** 10 vs 20 Gb/s se nerozlišuje (Windows to bez neověřeného IOCTL nehlásí) — zobrazuje se
  „10 Gb/s a víc“; fyzickou polohu portu Windows neznají spolehlivě, porty pojmenuje uživatel.
- **Blackmagic RAW (`braw-decode`):** Windows `--info` navíc vydává `gamma`, `gamut` a `colorScienceGen` a LUT se volí
  podle gamy klipu (Film Gen 5 → vestavěný LUT; Video, Extended Video a starší generace = bez LUTu; Film Gen 4 zatím bez LUTu —
  provizorně, ke schválení). Mac zatím vždy předpokládá Film Gen 5. Chybný argument = kód 2; konfigurace pipeline se
  nastavuje před `OpenClip` (SDK ji čte při prvním otevření klipu). Bez knihovny `BlackmagicRawAPI.dll` se BRAW
  jen zkopíruje a ověří — Offload proxy a náhledy přeskočí s nápovědou (`braw-decode` sám by skončil kódem 3).
- **Složka pro proxy navíc** (zatím jen Windows, pro Mac naplánováno): každá hotová proxy se kromě `_PROXY` na cílech
  zkopíruje i do `<složka>\<datum>\<karta>\` (při shodě jména `_2`, `_3`…, nic se nepřepíše). Nastavení
  `proxy.extraFolder` v settings.json a v reportu (Mac neznámý klíč ignoruje), v CLI `--proxy-folder`. Nedostupná
  složka zablokuje start; zmizí-li během úlohy, je to jen poznámka u klipu.
- **Vestavěné LUTy** (zatím jen Windows, pro Mac naplánováno; sdílené soubory v [`shared/luts/`](../shared/luts/)):
  11 LUTů Log → Rec.709, `auto` vybere LUT podle kamery každého klipu a použitý LUT s důvodem je v reportu
  (klíč `lut` u klipu, FORMATY.md). Zatím se automaticky nepoznají Canon, Panasonic, DJI a Fuji (chybí vzorové klipy,
  přijdou do záložního LUTu „Když kameru nepoznám:“, výchozí Bez LUT); D-Log M nemá vestavěný LUT. ARRI se pozná podle
  modelu kamery, takže klip z ARRI nahraný přímo v Rec.709 by dostal LUT neprávem — uživatel zvolí „Bez LUT“.
  Na vzorcích se ještě ověří rozsah R3D/ARRIRAW mezisouborů a ProRes z ALEXA 35 (LogC4) — berou se jako legální video.
  Mac nová nastavení `proxy.lutMode/lutBuiltin/lutFallback` ignoruje a dál používá jen `proxy.lutPath`.
- **Upozornění po dokončení:** Windows hraje zvuk a bliká tlačítkem v hlavním panelu místo notifikace Macu
  (nebalená aplikace umí toast jen po registraci).
- **Vysunutí karty (obě verze):** karta ve čtečce se vysune jako médium, čtečka zůstane připojená; interní disky,
  síťové složky a složky se nevysouvají nikdy. Vysune se jen karta, která se kopírovala — identita zapsaná na začátku
  kopie (Windows: svazek + sériové číslo souborového systému; Mac: UUID svazku, u FAT/exFAT odvozené ze sériového čísla):
  po výměně karty ve stejné čtečce tlačítko zmizí a vysunutí se odmítne; ani dokud z karty kopíruje jiná úloha.
  Skutečné vysunutí zatím není vyzkoušené na kartě ve čtečce (položka 4 ručního testu; Mac ověřen na obrazech disku).
- **Thunderbolt čtečky CFexpress:** Windows je hlásí jako NVMe disk, takže je aplikace sama nenabídne jako kartu
  (nabízí jen vyměnitelná média a USB/SD disky) — kartu v takové čtečce přidejte tlačítkem „Složka…“.
- **GUI navíc na Windows:** zkratka Ctrl+Enter pro spuštění, tlačítko „Průzkumník“, upozornění „Doporučeno NTFS“
  u cíle na exFAT/FAT32 (Mac doporučuje APFS) — jen na Windows.
- **Složka jako argument:** aplikace přijme složku jako argument, takže ji lze přetáhnout na `Offload.exe`.
  `--no-hw-prompt` se přijímá, ale nic nedělá (kontrola USB portů se při startu nespouští).
- **R3D/ARRI bez proxy** zatím nemají náhledy (stejně jako na Macu).
- **Enkodér proxy v reportu:** Windows zapisuje do JSON volitelné `proxyEncoder` (např. `h264_nvenc`, `libx264`)
  a ukazuje ho v řádku „Proxy: H.264 (h264_nvenc), …“ v HTML/PDF. Mac klíč nezná a ignoruje ho.
- **Verze v reportu:** Mac `0.1.0`, Windows `0.1.0-win` — z reportu je vidět, která verze kopírovala.
