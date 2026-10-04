# Offload — rozdíly mezi Mac a Windows verzí

Obě verze dělají totéž a zapisují **stejné výstupy na disk** (viz [FORMATY.md](FORMATY.md)) — kopii udělanou
na Macu ověří Windows a naopak. Liší se technologií, některými platformními detaily a stavem vývoje.

Pravidlo: když změna v jedné verzi způsobí, že se verze v něčem rozejdou (funkce, chování, formát),
aktualizuje se tento soubor ve stejném commitu. Historie každé verze je v [VYVOJ-MAC.md](VYVOJ-MAC.md)
a [VYVOJ-WINDOWS.md](VYVOJ-WINDOWS.md).

*Stav k 2026-10-04.*

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
| Report HTML | ✅ | 🔨 plán 1 (úkol 9) |
| ASC MHL v2 | ✅ | 🔨 plán 1 (úkol 8) |
| CLI `copy` / `verify` / `bench` | ✅ | 🔨 plán 1 (úkoly 13–16) |
| PDF report | ✅ (WebKit) | ⏳ plán 2 (WebView2) |
| Analýza klipů, metadata | ✅ | ⏳ plán 2 |
| Náhledy v reportu | 1 náhled ve 30 % délky → ⏳ **3 snímky** (první/prostřední/poslední) | ⏳ plán 2: 3 snímky |
| Proxy (LUT, burn-in, frame lines, TC) | ✅ | ⏳ plán 2 |
| Blackmagic RAW | ✅ `braw-decode` | ⏳ plán 3 (port `braw-decode` pro Windows) |
| RED R3D (REDline) | ✅ | ⏳ plán 2 |
| ARRIRAW / HDE / ARRICORE (art-cmd) | ✅ | ⏳ plán 2 |
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
| PDF | WebKit tisk | WebView2 `PrintToPdfAsync` |
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
- **Verze v reportu:** Mac `0.1.0`, Windows `0.1.0-win` — z reportu je vidět, která verze kopírovala.
