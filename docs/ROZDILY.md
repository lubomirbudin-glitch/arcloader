# Offload — rozdíly mezi Mac a Windows verzí

Obě verze dělají totéž a zapisují **stejné výstupy na disk** (viz [FORMATY.md](FORMATY.md)) — kopii udělanou
na Macu ověří Windows a naopak. Liší se technologií, některými platformními detaily a stavem vývoje.

Pravidlo: když změna v jedné verzi způsobí, že se verze v něčem rozejdou (funkce, chování, formát),
aktualizuje se tento soubor ve stejném commitu. Historie každé verze je v [VYVOJ-MAC.md](VYVOJ-MAC.md)
a [VYVOJ-WINDOWS.md](VYVOJ-WINDOWS.md).

*Stav k 2026-10-08.*

## Přehled

| | Mac | Windows |
|---|---|---|
| Složka | [`mac/`](../mac/) | [`windows/`](../windows/) |
| Systém | macOS 13+ | Windows 10/11 x64 |
| Jazyk / UI | Swift 5.9, SwiftUI | C# / .NET 10, WPF |
| Verze | 0.1.0 | 0.5.1-win (ve vývoji) |
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
| Blackmagic RAW | ✅ `braw-decode` | ✅ `braw-decode.exe` (C#, knihovna z SDK / Player / Resolve); čeká ověření se vzorkem .braw |
| RED R3D (REDline) | ✅ | ✅ kód, neověřeno s nástroji |
| ARRIRAW / HDE / ARRICORE (art-cmd) | ✅ | ✅ kód, neověřeno s nástroji |
| Canon RAW Light, Sony X-OCN | kopie + ověření, bez proxy | totéž |
| GUI: detekce karet, fronta, vysunutí | ✅ | ✅ (WPF; čeká ruční test s kartou, vysunutí zatím nevyzkoušené) |
| **Test USB portů na vyžádání + doporučení** | ❌ | ✅ CLI + GUI |
| **Vestavěné LUTy + automatický výběr podle kamery** (`proxy.lutMode`, `--lut auto`, `offload luts`) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test s klipy z kamer) |
| **Složka pro proxy navíc** (`proxy.extraFolder`, `--proxy-folder`) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test) |
| **Karta už zálohovaná** (`history.json`, řádek u karty, dotaz před celou kopií) | ⏳ naplánováno | ✅ CLI + GUI (GUI čeká ruční test s kartou) |
| **Doplnit jen nové klipy** (generace ASC MHL N, `offload_report_NNNN`, `offload append`) | ⏳ naplánováno | ✅ CLI + GUI (`ascmhl verify` po doplnění čeká ruční test) |
| **Název karty z klipů** (`A001C003…` → `A001`, další v řadě, `--name auto`) | ⏳ naplánováno | ✅ CLI + GUI |
| **Ověřit zálohu** (všechny generace MHL, hashe xxh64/md5/sha1/xxh3/xxh128/c4, report ověření) | `verify` jen proti `.xxh64` | ✅ CLI `verify` + GUI Nástroje → Ověřit zálohu… |
| **Kontrola místa** (proxy, report, fronta, po svazcích, rezerva 5 %) | jen velikost karty | ✅ CLI + GUI |
| **Hlášky kontrol před kopií česky** (`PreflightCode`) | ⏳ naplánováno (zatím anglicky) | ✅ |
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
| **Propustnost článků HW při kopii** (port/linka/disk, úzké hrdlo) | ❌ | ✅ CLI: úzké hrdlo („brzdí: D1“) ve stavovém řádku; GUI (od 0.5.0): tabulka rychlostí po cílech, bublina se zařízením a připojením, „Nejpomalejší článek: …“ (% linky a součet na řadiči jen v okně Hardware) |
| **Nový vzhled** (`docs/ui-navrh/`: horní lišta, kroky 1–3, karty úloh po stavech, stránka Nastavení, tmavé i světlé téma) | ⏳ po schválení Windows verze | ✅ GUI 0.5.0 (čeká ruční test) |
| **Pozastavit / Pokračovat** (kopie, ověření, 2. fáze mezi klipy; `copyPausedSeconds`, `verifyPausedSeconds`) | ⏳ naplánováno | ✅ GUI (CLI ne) |
| **Zkopírovat znovu** (oprava: jen nesouhlasné soubory, jen na postižený disk, generace ASC MHL N, `kind: "repair"`, `_REPORT/neshoda_NNNN/`) | ⏳ naplánováno | ✅ GUI (CLI ne) |
| **Dnes a historie karet** (z `history.json`), štítek kamery a připojení, odhad času | ⏳ naplánováno | ✅ GUI |
| **Proxy ve složkách jako na kartě** (`_PROXY/<složka klipu>/<klip>.mov`, plochá starší záloha zůstane plochá) | ⏳ naplánováno (proxy přímo v `_PROXY`) | ✅ 0.5.1 |
| **Bez proxy, když je originál lehčí** (`proxy.skipLighterSources`, `--proxy-always`) | ⏳ naplánováno (renderuje vše) | ✅ CLI + GUI 0.5.1 |

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
- **HW diagnostika (USB porty, propustnost článků)** je zatím jen na Windows — na přání uživatele. V CLI se z propustnosti
  článků ukazuje jen to, který článek brzdí (stavový řádek `offload copy`); GUI ukazuje i řetěz připojení a úzké hrdlo
  na kartě úlohy a okno Hardware.
- **USB rychlosti a porty:** 10 vs 20 Gb/s se nerozlišuje (Windows to bez neověřeného IOCTL nehlásí) — zobrazuje se
  „10 Gb/s a víc“; fyzickou polohu portu Windows neznají spolehlivě, porty pojmenuje uživatel.
- **Report při zrušeném ověření:** když je `verified` = false, Windows počítá soubory „zapsáno“ i „ověřeno“ jako
  v pořádku (zrušené ověření nevypíše už ověřené soubory jako chybné bez důvodu); „kartu lze vysunout“ platí
  jen při `verified` = true. Totéž je naplánováno i pro Mac.
- **Zdroj, který je sám offload:** Windows odmítne kopírovat zdroj s nejvyšší úrovní složek `_REPORT`, `_PROXY`
  nebo `ascmhl` (pravděpodobně dřívější offload). Naplánováno i pro Mac.
- **Dvakrát zadaný cíl:** Windows odmítne dvě shodné cílové cesty (bez ohledu na velikost písmen a koncové `\`),
  i s `--allow-same-device`. Naplánováno i pro Mac.
- **Kontrola voleb před kopií:** Windows ověří velikost bloku (násobek 4096, nejvýše 1 GiB), počet bufferů (≥ 2)
  a tvar data `RRRR-MM-DD` ještě před vytvořením čehokoli. Naplánováno i pro Mac.
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
- **Barvy proxy:** Windows převádí do YUV výslovně maticí BT.709 v legálním rozsahu a proxy označí BT.709
  (`color_space/primaries/transfer = bt709`, `color_range = tv`); náhledy JPEG výslovně BT.601 v plném rozsahu (JFIF).
  Mac zatím nechá volbu matice na ffmpeg a proxy neoznačí (po kroku v RGB tak může vyjít BT.601) — naplánováno.
- **Zrušená úloha v GUI:** karta úlohy na Windows ukáže jen oranžové „Zrušeno — karta není kompletně zálohovaná.“
  místo červeného „N souborů selhalo…“ a bez souhrnu rychlosti (rychlost useknuté kopie by klamala). Mac zatím
  ukazuje červený text i souhrn.
- **Upozornění po dokončení:** Windows hraje zvuk a bliká tlačítkem v hlavním panelu místo notifikace Macu
  (nebalená aplikace umí toast jen po registraci).
- **Vysunutí karty:** karta ve čtečce se vysune jako médium, čtečka zůstane připojená; interní disky a složky
  se nevysouvají nikdy. Vysune se jen karta, která se kopírovala (svazek + sériové číslo souborového systému zapsané
  na začátku kopie): po výměně karty ve stejné čtečce tlačítko zmizí a vysunutí se odmítne; ani dokud z karty kopíruje
  jiná úloha. Skutečné vysunutí zatím není vyzkoušené na kartě (položka 4 ručního testu).
- **Thunderbolt čtečky CFexpress:** Windows je hlásí jako NVMe disk, takže je aplikace sama nenabídne jako kartu
  (nabízí jen vyměnitelná média a USB/SD disky) — kartu v takové čtečce přidejte tlačítkem „Složka…“.
- **GUI navíc na Windows:** zkratka Ctrl+Enter pro spuštění, tlačítko „Průzkumník“, upozornění „Doporučeno NTFS“
  u cíle na exFAT/FAT32 (Mac doporučuje APFS), okno Hardware s kontrolou USB portů na vyžádání (Ctrl+U) a vlastními
  názvy portů — jen na Windows.
- **Složka jako argument:** aplikace přijme složku jako argument, takže ji lze přetáhnout na `Offload.exe`.
  `--no-hw-prompt` se přijímá, ale nic nedělá (kontrola USB portů se při startu nespouští).
- **R3D/ARRI bez proxy** zatím nemají náhledy (stejně jako na Macu).
- **Enkodér proxy v reportu:** Windows zapisuje do JSON volitelné `proxyEncoder` (např. `h264_nvenc`, `libx264`)
  a ukazuje ho v řádku „Proxy: H.264 (h264_nvenc), …“ v HTML/PDF. Mac klíč nezná a ignoruje ho.
- **Verze v reportu:** Mac `0.1.0`, Windows `0.3.0-win` — z reportu je vidět, která verze kopírovala.
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
  nepozastavovalo), rychlosti a TXT `Paused:` bez pauzy. Mac klíče ignoruje — jeho rychlosti by pauzu zahrnuly.
- **Vzhled** (Windows od 0.5.0): podle `docs/ui-navrh/` s přepínačem **projektů** místo presetů, systémovým písmem
  (Segoe UI, Cascadia Mono) a tmavým i světlým tématem podle Windows; Mac převezme po schválení Windows verze. Thunderbolt
  Windows nerozliší (čtečka za ním je „NVMe“ / „USB“), Mac ano.
- **Proxy ve složkách jako na kartě** (Windows od 0.5.1): `_PROXY/idle/1.mov` místo `_PROXY/1.mov` (a `1_2.mov` pro
  `listening/1.mp4`), stejně ve složce pro proxy navíc; `_2` jen při shodě ve stejné složce. Mac dává proxy přímo do
  `_PROXY`. Doplnění na Windows do zálohy z Macu nebo z Windows 0.5.0 pozná plochou zálohu z reportů a zůstane ploché
  (nic se nepřesouvá). `proxyPath` v reportu je relativní cesta jako dřív, jen s podsložkou — Mac ji jen zobrazí.
- **Bez proxy, když je originál lehčí** (Windows od 0.5.1, `proxy.skipLighterSources`, výchozí zapnuto): H.264/HEVC
  originál ne větší než proxy a s datovým tokem nejvýš jako proxy nedostane proxy (`proxyStatus: skipped` + poznámka
  „bez proxy — originál je lehčí (X Mb/s ≤ Y Mb/s)“); originál větší než proxy ji dostane vždy (rozhodnutí: 4K long-GOP
  se ve střižně dekóduje těžce i s nízkým tokem). Mac klíč ignoruje a renderuje proxy pro všechny klipy.
- **Odkaz na proxy v HTML reportu** (Windows od 0.5.1): stav „hotovo“ odkazuje na soubor proxy (`../_PROXY/…`).
- **Chyby souborů v HTML česky** (Windows): JSON je sdílený, takže chyby v něm zůstávají anglicky (`size mismatch`,
  `HASH MISMATCH` …); HTML/PDF report je zobrazí česky.
- **Historie karet** (zatím jen Windows, pro Mac naplánováno): `%APPDATA%\Offload\history.json` (formát ve FORMATY.md,
  „Místní soubory“) — řádek „Zálohováno … — ověřeno“ u karty, dotaz „Karty už jsou zálohované“ před celou kopií, CLI
  vypíše řádek před plánem (bez dotazu); `--no-history` historii vypne. Karta se pozná podle sériového čísla souborového
  systému + kapacity, ne podle GUID svazku — Mac musí vyjít na stejné `fsSerial` (u FAT/exFAT ověřit).
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
- **Hlášky česky:** kontroly před kopií, CLI („Chyba: …“, souhrn `verify`) a chyby parseru (kde je System.CommandLine
  přeložené) jsou na Windows česky; logy, TXT report a `--help` zůstávají anglicky. Mac je zatím anglicky.
- **Doplnění do zálohy — zámek složky karty** (Windows): dvě doplnění do stejné zálohy najednou se nepustí (druhé hned
  skončí „Do této zálohy právě doplňuje jiná úloha …“). Zámek platí jen v jedné přihlášené relaci Windows na jednom
  počítači — nechrání před doplněním z jiného účtu ani z jiného počítače do sdílené složky — a stejnou složku pod jiným
  jménem (`subst`, `\\localhost\D$`, krátké jméno 8.3) bere jako jinou.
