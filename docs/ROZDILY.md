# Offload — rozdíly mezi Mac a Windows verzí

Obě verze dělají totéž a zapisují **stejné výstupy na disk** (viz [FORMATY.md](FORMATY.md)) — kopii udělanou
na Macu ověří Windows a naopak. Liší se technologií, některými platformními detaily a stavem vývoje.

Pravidlo: když změna v jedné verzi způsobí, že se verze v něčem rozejdou (funkce, chování, formát),
aktualizuje se tento soubor ve stejném commitu. Historie každé verze je v [VYVOJ-MAC.md](VYVOJ-MAC.md)
a [VYVOJ-WINDOWS.md](VYVOJ-WINDOWS.md).

*Stav k 2026-10-06.*

## Přehled

| | Mac | Windows |
|---|---|---|
| Složka | [`mac/`](../mac/) | [`windows/`](../windows/) |
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.2.0 | 0.1.0-win (ve vývoji) |
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
| ARRIRAW / HDE / ARRICORE (art-cmd) | ✅ ověřeno se skutečným art-cmd (ALEXA 35, ALEXA Mini) | ✅ kód, neověřeno s nástroji; **známá chyba:** bez LUTu předá art-cmd `--target-colorspace` spolu s `--embedded-look` (`RawBackends.cs` ř. 381) — art-cmd to odmítne, skončí kódem 0 a nic nezapíše → klip bez proxy a náhledů (Mac opraveno v `3b43352`: bez LUTu jen `--embedded-look`) |
| Canon RAW Light, Sony X-OCN | kopie + ověření, bez proxy | totéž |
| GUI: detekce karet, fronta, vysunutí | ✅ | ✅ (WPF; čeká ruční test s kartou, vysunutí zatím nevyzkoušené) |
| **Test USB portů na vyžádání + doporučení** | ✅ CLI (`offload ports`) + GUI (okno Hardware ⌘U, vlastní názvy portů; GUI čeká ruční test) | ✅ CLI + GUI |
| **Vestavěné LUTy + automatický výběr podle kamery** (`proxy.lutMode`, `--lut auto`, `offload luts`) | ✅ CLI + GUI (GUI čeká ruční test) | ✅ CLI + GUI (GUI čeká ruční test s klipy z kamer) |
| **Složka pro proxy navíc** (`proxy.extraFolder`, `--proxy-folder`) | ✅ CLI + GUI (GUI čeká ruční test) | ✅ CLI + GUI (GUI čeká ruční test) |
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ✅ CLI + GUI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; řetěz připojení, % linky a úzké hrdlo na kartě úlohy (GUI čeká ruční test) | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI: řetěz připojení s rychlostí, % linky, součtem na řadiči a úzkým hrdlem na kartě úlohy |

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
| Dekódování pro proxy | VideoToolbox (-hwaccel videotoolbox) jen pro H.264/HEVC | -hwaccel auto jen pro H.264/HEVC |
| PDF | WebKit tisk | Edge headless `--print-to-pdf` |
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
- **Verze v reportu:** Mac `0.2.0`, Windows `0.1.0-win` — z reportu je vidět, která verze kopírovala.
