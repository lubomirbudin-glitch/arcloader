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
| Náhledy v reportu | 1 náhled ve 30 % délky → ⏳ **3 snímky** (první/prostřední/poslední) | ✅ 3 snímky |
| Proxy (LUT, burn-in, frame lines, TC) | ✅ | ✅ ověřeno NVENC + software (libx264); QSV/AMF neověřeno (není HW); čeká ostrý test na skutečném médiu |
| Blackmagic RAW | ✅ `braw-decode` | ⏳ plán 3 (port `braw-decode` pro Windows) |
| RED R3D (REDline) | ✅ | ✅ kód, neověřeno s nástroji |
| ARRIRAW / HDE / ARRICORE (art-cmd) | ✅ | ✅ kód, neověřeno s nástroji |
| Canon RAW Light, Sony X-OCN | kopie + ověření, bez proxy | totéž |
| GUI: detekce karet, fronta, vysunutí | ✅ | ⏳ plán 4 |
| **Test USB portů při startu + doporučení** | ❌ | ⏳ plán HW (jen Windows) |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ⏳ plán HW (jen Windows) |

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
- **HW diagnostika (USB porty, propustnost článků)** je zatím jen na Windows — na přání uživatele.
- **Report při zrušeném ověření:** když je `verified` = false, Windows počítá soubory „zapsáno“ i „ověřeno“ jako
  v pořádku (zrušené ověření nevypíše už ověřené soubory jako chybné bez důvodu); „kartu lze vysunout“ platí
  jen při `verified` = true. Totéž je naplánováno i pro Mac.
- **Zdroj, který je sám offload:** Windows odmítne kopírovat zdroj s nejvyšší úrovní složek `_REPORT`, `_PROXY`
  nebo `ascmhl` (pravděpodobně dřívější offload). Naplánováno i pro Mac.
- **Dvakrát zadaný cíl:** Windows odmítne dvě shodné cílové cesty (bez ohledu na velikost písmen a koncové `\`),
  i s `--allow-same-device`. Naplánováno i pro Mac.
- **Kontrola voleb před kopií:** Windows ověří velikost bloku (násobek 4096, nejvýše 1 GiB), počet bufferů (≥ 2)
  a tvar data `RRRR-MM-DD` ještě před vytvořením čehokoli. Naplánováno i pro Mac.
- **R3D/ARRI bez proxy** zatím nemají náhledy (stejně jako na Macu).
- **Enkodér proxy v reportu:** Windows zapisuje do JSON volitelné `proxyEncoder` (např. `h264_nvenc`, `libx264`)
  a ukazuje ho v řádku „Proxy: H.264 (h264_nvenc), …“ v HTML/PDF. Mac klíč nezná a ignoruje ho.
- **Verze v reportu:** Mac `0.1.0`, Windows `0.1.0-win` — z reportu je vidět, která verze kopírovala.
