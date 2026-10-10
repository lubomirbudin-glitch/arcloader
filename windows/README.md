# Offload pro Windows

Port Mac aplikace Offload (viz [`../mac/`](../mac/), [`../mac/README.md`](../mac/README.md)) na Windows 10/11 x64, .NET 10.
Kopie karty na více disků najednou, ověření zpětným čtením (xxHash64), report a ASC MHL —
ve stejném formátu jako Mac verze, takže kopii z Windows ověří Mac a naopak.
Ve vývoji — stav a historie v [`../docs/VYVOJ-WINDOWS.md`](../docs/VYVOJ-WINDOWS.md), rozdíly proti Macu
v [`../docs/ROZDILY.md`](../docs/ROZDILY.md).

## Stav

| Část | Stav |
|---|---|
| Jádro + CLI (`bin\offload.exe`) | hotovo (plán 1) — ostrý test na skutečné kartě a měření rychlostí zatím chybí |
| Analýza klipů, 3 snímky, proxy, PDF | hotovo (plán 2) — QSV/AMF a ostrý test na médiu neověřeno |
| RAW | R3D/ARRI: kód hotový, neověřeno se skutečnými nástroji (REDline, art-cmd nejsou nainstalované); BRAW: hotovo (plán 3), čeká ověření se vzorkem .braw / kartou |
| BRAW dekodér (C#, Blackmagic RAW SDK) | hotovo (plán 3, `bin\braw-decode.exe`) — ověřeno jen s knihovnou z DaVinci Resolve, dekódování snímků a rozložení VARIANT (gama/gamut) čeká na vzorek .braw; Film Gen 4 zatím bez LUTu (provizorně); v GUI v seznamu nástrojů řádek "braw-decode" je zelený i bez knihovny `BlackmagicRawAPI.dll` (BRAW je pak jen zkopírovaný a ověřený, důvod je v poznámce u klipu) |
| HW diagnostika USB | hotovo (CLI `offload ports`, v GUI okno Hardware a kontrola na vyžádání) |
| GUI `Offload.exe` | hotovo (čeká ruční test s kartou) |
| Vestavěné LUTy (plán 5) | hotovo (čeká ruční test s klipy z kamer) |
| Karty a bezpečnost dat (plán 6, 0.2.0): historie, doplnění, název z klipů, ověření zálohy, místo, hlášky (od 0.9.0 anglicky) | hotovo (čeká ruční test s kartou) |
| Výstupy pro produkci a střih (plán 7, 0.3.0): denní report, seznam klipů CSV/ALE, proxy pro přelinkování, MD5 | hotovo (čeká ruční test v Resolve, Premiere, Avid, Excelu) |
| Nastavení, oznámení, distribuce (plán 8, 0.4.0): projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, podpis | hotovo (čeká ruční test: projekty a oznámení v GUI, podpis s certifikátem; převod Gen 4 je neověřený, čeká vzorek .braw Gen 4) |
| Nový vzhled (plán 9, 0.5.0, návrh `docs/ui-navrh/`): tmavé i světlé téma podle Windows, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, Pause, Copy again (oprava), TODAY a Card history, stránka Nastavení, štítek kamery a připojení, odhad času | hotovo (čeká ruční test 40–49 s kartou; snímky ukázkového režimu porovnané s `docs/ui-navrh/screeny`) |
| Verify-in-Proxy (plán 10, 0.6.0): ověření primárního disku čtením pro proxy, ostatní na pozadí, "Safe to eject" × "Safe to format", blokový manifest a navázání po pádu, XXH128, obsahová kontrola | hotovo (čeká ruční test 50–58) |

## Build a testy

```powershell
cd windows
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\build.ps1   # → build\Offload\Offload.exe, build\Offload\bin\offload.exe, build\Offload-win-x64-<verze>.zip
```

Balíček `build\Offload-win-x64-<verze>.zip` (verze z `Directory.Build.props`) obsahuje složku `Offload` (`Offload.exe` + `bin\` s `offload.exe`, `braw-decode.exe`, `ffmpeg.exe`, `ffprobe.exe`);
stačí ho rozbalit kamkoli a spustit `Offload.exe`. Neobsahuje nic, co by se muselo instalovat. Knihovnu `BlackmagicRawAPI.dll`
balíček neobsahuje — bez ní se BRAW jen zkopíruje a ověří, proxy a náhledy se přeskočí.

### Vydání verze

Verze je jen na jednom místě: `windows\Directory.Build.props` (`Version`, např. `0.4.0`, a `InformationalVersion`, např. `0.4.0-win`);
aplikace, CLI a report ji čtou odtud (`OffloadVersion`). Číslování: plán 6 → 0.2.0, 7 → 0.3.0, 8 → 0.4.0, 9 → 0.5.0, opravy
zvyšují poslední číslo, 1.0.0 po ostrém testu s kartou.

1. Zvyšte verzi v `Directory.Build.props` (obě hodnoty) a spusťte `dotnet test`.
2. `powershell -ExecutionPolicy Bypass -File scripts\build.ps1 [-Sign …] -ReleaseUrl <kam zip dáte> [-Notes "<co je nového>"]`
   — vznikne `build\Offload-win-x64-<verze>.zip` a `build\offload-update.json` (`{"version", "url", "notes"}`, UTF-8).
   `-ReleaseUrl` je adresa, kterou uživatel otevře tlačítkem *Open* (https adresa, `\\NAS\Offload\`, sdílená složka).
3. Nahrajte zip na web / NAS a **až potom** manifest (manifest nesmí ukazovat na zip, který tam ještě není).
4. **GitHub Releases** (stálé odkazy, vždy na nejnovější build kanálu): po pushnutí commitu, ze kterého je zip sestavený,
   `powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -Channel dev` (vývojová verze) nebo `-Channel stable`
   (stabilní; `-Since <předchozí stabilní>` vypíše do popisu změny všech verzí od ní, `-DryRun` jen ukáže popis). Skript
   nahraje archiv `windows-v<verze>` (Offload-win-x64-<verze>.zip), posune značku kanálu `stable`/`dev` na commit a nahradí
   v jeho release soubor se stálým jménem; u stabilní i `offload-update.json`. Popis verze bere z `docs/VYVOJ-WINDOWS.md`.
   - stabilní: https://github.com/lubomirbudin-glitch/offload/releases/download/stable/Offload-Windows.zip
     (stránka https://github.com/lubomirbudin-glitch/offload/releases/tag/stable, také `/releases/latest`)
   - vývojová: https://github.com/lubomirbudin-glitch/offload/releases/download/dev/Offload-Windows-dev.zip
     (stránka https://github.com/lubomirbudin-glitch/offload/releases/tag/dev)
   - manifest pro kontrolu aktualizací: https://github.com/lubomirbudin-glitch/offload/releases/download/stable/offload-update.json
   - Mac verze může do stejných release přidat `Offload-Mac.zip` / `Offload-Mac-dev.zip`.

Kontrola aktualizací v aplikaci (Settings → App → Updates…) čte manifest z adresy, kterou tam zadáte (`https://…`,
`\\NAS\Offload\offload-update.json`, místní cesta); nejvýš jednou za 24 h, 5 s po startu, nic nestahuje ani nespouští,
čte nejvýš 64 KB. Tlačítko *Open* otevře jen adresu `https://…` nebo existující složku / zip (v Průzkumníku); cokoli
jiného se jen zobrazí. Prázdná adresa = vypnuto (výchozí). Kontrola nečte GitHub (balíčky se tam nezveřejňují).

### Podepisování

Logika vydání (verze, manifest, hledání signtoolu a certifikátu, podpis) je v `scripts\release.ps1`, který `build.ps1` načítá. Podpis je volitelný a nic se při něm nekupuje ani nestahuje.

`build.ps1 -Sign` podepíše `Offload.exe`, `bin\offload.exe` a `bin\braw-decode.exe` (ffmpeg ne — není náš) před
zabalením: `signtool sign /fd SHA256 /td SHA256 /tr <razítko> /d Offload …`, pak `signtool verify /pa /all`.

```powershell
# certifikát v úložišti Windows (CurrentUser\My, pak LocalMachine\My; funguje i s hardwarovým tokenem)
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Sign -CertThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
# nebo soubor .pfx — heslo jen v proměnné prostředí, nikdy na příkazové řádce build.ps1
$env:OFFLOAD_SIGN_PFX = 'C:\certs\offload.pfx'; $env:OFFLOAD_SIGN_PFX_PASSWORD = '…'
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Sign -TimestampUrl http://timestamp.digicert.com
```

- `signtool.exe` se hledá v proměnné `SIGNTOOL`, pak v `PATH`, pak v nejnovějším `Windows Kits\10\bin\10.*\x64\`
  (Windows SDK, komponenta *Signing Tools for Desktop Apps*). Chybí-li, build hned na začátku skončí: „signtool nenalezen —
  nainstalujte Windows SDK (Signing Tools for Desktop Apps), nebo nastavte SIGNTOOL.“ Stejně hned skončí neexistující
  otisk, prošlý certifikát, certifikát bez soukromého klíče nebo ne pro podepisování kódu, chybějící
  `OFFLOAD_SIGN_PFX_PASSWORD`. Je-li zadán otisk i `OFFLOAD_SIGN_PFX`, použije se otisk (a skript to řekne).
- Časové razítko (výchozí `http://timestamp.digicert.com`): 3 pokusy po 5 s, pak „Časové razítko se nepodařilo získat z …“.
- Heslo se nikdy nevypisuje (v chybách je `***`). Pozor: `signtool /p` je vidět v seznamu procesů — na sdíleném počítači
  raději certifikát v úložišti nebo na tokenu.
- Bez `-Sign` se build nemění.

Jak získat certifikát (nic se nekupuje automaticky; ceny orientační, k datu nákupu ověřit):

| Možnost | Co to je | Cena (orientačně) | Poznámka |
|---|---|---|---|
| OV (Organization Validation) | ověřená firma nebo živnost | ~200–500 USD/rok | od 2023 je klíč povinně na tokenu / HSM (USB token, `-CertThumbprint`) |
| EV (Extended Validation) | přísnější ověření firmy | ~300–700 USD/rok | také token; od 2024 už nedává okamžitou reputaci SmartScreen — ověřit aktuální stav |
| Microsoft Artifact Signing (Azure Trusted Signing) | cloudové podepisování | ~10 USD/měsíc | podmínky podle země a historie firmy — ověřit pro ČR; signtool s jejich doplňkem |
| Certum (polská CA) | OV i pro jednotlivce | levnější OV | token / cloud podle nabídky |

SmartScreen buduje reputaci počtem stažení — první verze mohou varovat i s podpisem. Certifikát podepsaný sám sebou
(self-signed) SmartScreenu nepomůže; hodí se jen k vyzkoušení skriptu (`signtool verify /pa` projde jen s certifikátem,
kterému počítač důvěřuje).

### Vzorek BRAW (testy dekodéru Blackmagic RAW)

Dekodér `bin\braw-decode.exe` potřebuje knihovnu `BlackmagicRawAPI.dll` — stačí nainstalovaný DaVinci Resolve,
nebo zdarma „Blackmagic RAW“ (Player + SDK) z https://www.blackmagicdesign.com/products/blackmagicraw.
Testy, které potřebují skutečný klip, se bez vzorku přeskočí. Vzorek:

1. Na https://www.blackmagicdesign.com/products/blackmagicraw (nebo https://www.blackmagicdesign.com/event/blackmagicrawinstaller,
   „Download RAW Samples“) stáhnout jeden ukázkový klip (zdarma, formulář se jménem a e-mailem), rozbalit.
2. Nastavit proměnnou na plnou cestu ke klipu a pustit testy:

       setx OFFLOAD_BRAW_SAMPLE "D:\Vzorky\BRAW\A001_01011200_C001.braw"
       # nové okno PowerShellu
       dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~Braw"

   (Jméno souboru je příklad; ukázkové klipy z 12K kamery mají stovky MB až jednotky GB.) Test celé cesty
   (`Offload_ProbeThumbnailsProxy`: analýza, tři náhledy, proxy 540p) potřebuje navíc ffmpeg a ffprobe.

**Ověření se vzorkem — co udělat, až klip bude** (na tomto PC zatím žádný .braw není, nic z toho neproběhlo):

1. `setx OFFLOAD_BRAW_SAMPLE "<cesta ke klipu>.braw"`, nové okno PowerShellu, ve složce `windows`:
   `dotnet test tests\Offload.Core.Tests --filter "FullyQualifiedName~Braw"` — očekává se vše PASS a **0 přeskočených** testů
   (přeskočený test znamená, že chybí proměnná, knihovna `BlackmagicRawAPI.dll`, ffmpeg nebo ffprobe).
2. Stále ve složce `windows`: `build\Offload\bin\braw-decode.exe --info --scale eighth $env:OFFLOAD_BRAW_SAMPLE`
   (po `scripts\build.ps1`; nebo `tools\bin\braw-decode.exe`). **Nejdřív zkontrolovat stderr:** nečekaný typ VARIANT
   u atributu se vypíše tam (např. `braw-decode: gamut: clip attribute 'gamt' is a number, expected a string`) a klíč
   v JSON pak chybí; potichu chybí klíč jen tehdy, když selže samotné volání `GetClipAttribute`. Pak očima zkontrolovat
   JSON: `gamma` (např. „Blackmagic Design Film“) a `gamut` jsou vyplněné, `colorScienceGen` je číslo (5 u Gen 5),
   `width`/`height` odpovídají zmenšení, `frames`, `fps`, `timecode`, `camera` dávají smysl. Chybí-li `gamma`/`gamut`
   s hláškou na stderr, je typ VARIANT jiný, než SDK dokumentuje — porovnat s IDL SDK; bez hlášky atribut SDK u klipu
   nevrátilo. Do opravy se LUT chová jako dosud (gama neuvedena → předpoklad Film Gen 5).
3. `offload copy <složka se vzorkem> <cíl> --proxy`: v reportu klip se **třemi snímky**, LUT „Blackmagic RAW: Blackmagic Design
   Film Gen 5“ (u klipu v Gen 5), proxy `.mov` se vizuálně shoduje s Blackmagic RAW Playerem s LUTem Film → Rec.709
   (barvy, kontrast, orientace, bez posunu o snímek; první/prostřední/poslední snímek v reportu odpovídají časům).
4. Nainstaluje-li uživatel SDK nebo Player, ověřit, že `BrawLibrary.Find` najde jejich cestu (pořadí: vedle exe, SDK, Player, Resolve);
   rovněž klip z kamery s gamou Video (bez LUTu) nebo Film Gen 4 (rozhodnout o LUTu).

Při vývoji (GUI/CLI z `bin\Debug`) se `braw-decode.exe` hledá ve `windows\tools\bin`:
`dotnet publish src\Offload.BrawDecode\Offload.BrawDecode.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:DebugType=none -o tools\bin`.

## Aplikace (GUI)

Spuštění: `Offload\Offload.exe` (jeden soubor, .NET v něm je; první start trvá déle, protože se rozbalí nativní knihovny WPF).
Bez podpisu (build bez `-Sign`, viz Podepisování) Windows při prvním spuštění ukáže "Windows protected your PC" — klikněte na "More info" → "Run anyway".

- **Vzhled (0.5.0, návrh `docs/ui-navrh/`):** horní lišta (Project, stav fronty, Verify backup, Hardware s tečkou, Settings), vlevo kroky "1 · CARDS", "2 · DESTINATIONS", "3 · AFTER COPY" s připnutou lištou Start offload, vpravo Queue / Card history a "TODAY"; volby jsou na stránce Settings (ikona s posuvníky, Ctrl+,; Esc zavře). Tmavé
  i světlé téma podle Windows (`Themes/Dark.xaml`, `Light.xaml`); `OFFLOAD_THEME=dark|light` téma vynutí (pro snímky).
  Ukázkový režim pro vizuální kontrolu: `Offload.exe --demo pripraveno|probiha|hotovo|nastaveni` (stavy obrazovek
  z `docs/ui-navrh/screeny`; výchozí vypnutý) a `--demo overuje` (plán 10: karta zkopírovaná a ověřovaná s proxy "Safe to eject", karta s vadou obsahu a zelená "Safe to format"). Demo je zapečetěné: vlastní složka dat v %TEMP% (nastavení, historie i log;
  `OFFLOAD_SETTINGS_DIR` se ignoruje), nic nekopíruje ani nezapisuje, Start offload, Append, Copy again, měření portů, úpravy projektů, oznámení, aktualizace, Verify backup, Day report i Eject card jsou vypnuté; jen Pause / Resume / Cancel na ukázkové úloze mění, co karta ukazuje. Snímky přes `scripts\app-drive.ps1` (`Start-Offload -Arguments '--demo probiha'`,
  `Resize-Offload $p 1000 900`).
- **Projekt:** rozbalovací "Project <název> ▾" v horní liště — pojmenovaná nastavení (cíle, kontrola, součet, název
  z klipů, výstupy, oznámení, proxy včetně LUTu, pravidel LUTů a složky pro proxy). V seznamu: "Save as new project…", "Rename…", "Delete…"; "Edit project" u kroku 3 otevře Settings. Titulek okna "Offload — Film X". Přepnutí nezmění úlohy ve frontě (mají svou
  kopii nastavení); během přípravy startu je přepínač i menu zamčené (a když se projekt přesto změní, start se
  zruší s hláškou). Doplnění karty se řídí projektem dřívější zálohy; když ten už neexistuje (smazaný, přejmenovaný),
  běží s nastavením "No project" a karta úlohy to napíše. Start kontroluje jen LUTy a složky, které se opravdu
  použijí. Cesty k nástrojům, názvy USB portů a kontrola aktualizací jsou společné.
- **Pravidla LUTů:** u "Automatic by camera" tlačítko "LUT rules… (N)" — řádky *camera / serial number / reel / folder → LUT*, zkoušejí se shora dolů před rozpoznáním kamery; report u klipu ukáže "rule 2: …". Cíl "No LUT" dá klipu bez LUTu. Pravidlo z novější verze Offloadu (neznámý druh, např. `lens`) editor ukáže jen ke čtení,
  přeskočí ho a při OK ho zapíše beze změny.
- **Oznámení:** Settings → Notifications → Edit notifications… — oznámení Windows (když okno není v popředí) a volitelně ntfy
  na telefon (jen název karty, výsledek a počty, nikdy cesty). Selhání kopie se oznámí hned, když kopie skončí.
  Settings → App → Updates… — adresa manifestu (výchozí prázdná = kontrola vypnutá) a "Check now";
  nová verze se ukáže odkazem v horní liště. *Open* otevře jen https adresu nebo místní složku / zip; síťové cesty (UNC, namapovaný síťový disk)
  se nikdy neotevírají, jen zobrazí k ručnímu zkopírování.

- **Karty:** karta vložená do čtečky se objeví sama a je zaškrtnutá; složku lze přidat tlačítkem "From folder…" nebo přetažením
  složky na `Offload.exe`. Dlaždice ukáže kameru ("ALEXA 35 · LogC4" z prvního klipu, ffprobe nejvýš 5 s, jen čtení)
  a připojení ("reader USB 3 · 5 Gb/s" z HW diagnostiky; Thunderbolt Windows nerozliší). Název složky v cíli lze upravit ("Folder name"); "Append" přepne kartu na doplnění nových klipů.
- **Cíle:** tlačítkem "Add disk" (pamatují se; zobrazí "… free of … · FS · connection" a pruh místa, u disku exFAT/FAT32 aplikace upozorní "NTFS recommended — more reliable than exFAT/FAT32").
- **Proxy:** "Also copy proxies to a folder" → "Choose…": každá hotová proxy se kromě `_PROXY` na cílech zkopíruje
  i do `<složka>\<datum>\<karta>\` (nic se nepřepíše, shodné jméno dostane `_2`, `_3`…); křížek volbu zruší.
  Když složka není dostupná (odpojený disk), start nejde a pod krokem 3 i v Settings → Proxies je červeně "The proxy folder is not available: …".
- **LUT:** v Settings → Proxies seznam "Automatic by camera" (výchozí) / vestavěné LUTy / "No LUT" / "Custom…" (`.cube`). "If the camera is not recognised:" je záložní LUT pro klipy, které automatika nepozná (výchozí "No LUT"). Automaticky se pozná:
  Sony podle sidecaru `M01.XML` (jen S-Log3 s S-Gamut3.Cine nebo S-Gamut3, jiná gama = bez LUTu), ARRI podle modelu
  kamery (ALEXA 35 → LogC4, ostatní ALEXA/AMIRA → LogC3), ARRIRAW podle metadat, Blackmagic RAW (BRAW) podle gamy klipu (Film Gen 5 → vestavěný LUT; Film starší generace = bez LUTu, s volbou "Convert Blackmagic RAW Film Gen 4 to Gen 5" (RAW decoders; **neověřené**, čeká vzorek Gen 4) LUT Gen 5; Video, Extended Video a další gamy = bez LUTu; bez údaje o gamě se předpokládá Film Gen 5),
  RED → Log3G10. Canon, Panasonic, DJI a Fuji se zatím nepoznají (chybí vzorové klipy), D-Log M vestavěný LUT nemá —
  takové klipy dostanou záložní LUT. U ARRI jde o předpoklad pro ProRes: klip nahraný přímo v Rec.709 by LUT dostal
  neprávem, pak zvolte "No LUT". Report ukáže u každého klipu použitý LUT a důvod. Vlastní soubor
  `%APPDATA%\Offload\luts\<id>.cube` (např. oficiální `arri-logc4-awg4.cube` od ARRI) nahradí vestavěný LUT se stejným
  id v aplikaci i CLI — v seznamu má "(custom file)", v reportu je jeho cesta v důvodu LUTu. Na vzorcích z kamer se ještě
  ověří rozsah: R3D a ARRIRAW (přes ProRes/MXF mezisoubor) a ProRes z ALEXA 35 (LogC4) se berou jako legální video.
- **Spuštění:** tlačítko "Start offload" (s úlohou ve frontě "Add to queue") nebo Ctrl+Enter; nad ním "1 card → 2 disks · 1.70 GB" a odhad času (jen z rychlostí naměřených v tomto spuštění; jinak jen velikost). Každá karta
  má v pravém sloupci kartu úlohy: ukazatel fází, velké procento, "~0:02 left for the copy · ~6:00 in total", tabulka rychlostí (Card read + každý cíl; bublina = zařízení a připojení), "File 10 / 24", Pause / Resume, Cancel a "Bottleneck: …".
  Karty se kopírují za sebou, proxy předchozí karty se renderují souběžně s kopií další.
- **Pause:** kopie a ověření stojí mezi 16MB bloky, 2. fáze mezi klipy (ffmpeg se neuspává); čas pauzy se do
  rychlostí nepočítá (report `copyPausedSeconds` / `verifyPausedSeconds`); Cancel a zavření aplikace fungují i během pauzy.
- **Po dokončení:** zelená karta "… — safe to format" se souhrnem Copy · Verification · Proxies · Total a náhledy, "Eject card" (karta ve čtečce se vysune jako médium, čtečka zůstane; interní disky a složky se nevysouvají nikdy;
  vysune se jen ta karta, která se kopírovala — po výměně karty ve čtečce tlačítko zmizí), "Report PDF", "Open folder", "Proxies". Žlutá karta ("… — do not eject the card yet") vypíše nesouhlasné soubory po discích a nabídne **Copy again**: úloha "A001 · repair" zkopíruje jen ty soubory a jen na disky, kde nesouhlasí (dobrá kopie se jen
  znovu přečte, nesouhlasná se odloží do `_REPORT\neshoda_NNNN\`), jako další generace ASC MHL; jen s kartou ve čtečce.
  Zrušená úloha je šedá. Upozornění: zvuk a blikání tlačítka v hlavním panelu. Pod frontou "TODAY", záložka "Card history" (z `history.json`).
- **Hardware:** kontrola USB portů se nespouští při startu, jen na vyžádání — tlačítko "Hardware" v horní liště (Ctrl+U; tečka = výsledek posledního testu) nebo Settings → App → "Test USB ports…". Okno Hardware ukazuje Connections, Advice, Measurement
  (s potvrzením) a USB porty s vlastními názvy. Přepínač `--no-hw-prompt` se stále přijímá, ale nic nedělá.
- **Soubory:** nastavení `%APPDATA%\Offload\settings.json`, historie offloadů `history.json` + `history\` (seznamy souborů),
  log `%APPDATA%\Offload\app.log`, poslední test HW `hwtest.json`
  (složku lze přesměrovat proměnnou `OFFLOAD_SETTINGS_DIR`).

### Checklist ručního testu (s kartou)

Vše níže zatím nikdo neověřil na skutečné kartě — GUI je jinak zkoušené jen automaticky a na snímcích.
Od 0.5.0 jsou volby dřívějších sekcí "Struktura a kontrola", "Proxy" a "Dekodéry" na stránce Settings (Ctrl+,; dnes sekce Folder structure, Verification, Proxies, RAW decoders), menu "Nástroje" nahradila horní lišta (Verify backup, Hardware) a Settings (Notifications, Updates, Day report) — mapování
je v plánu 9 (`docs/superpowers/plans/2026-10-07-offload-windows-9-ui.md`).

- [ ] 1. Vložit kartu do čtečky → objeví se v "1 · CARDS" a je zaškrtnutá
- [ ] 2. Přidat dva cíle na různých discích
- [ ] 3. Start offload (Ctrl+Enter) → průběh, tabulka rychlostí (bublina = zařízení a připojení) a "Bottleneck: …"
- [ ] 4. Kartu vysunujte až po "Safe to eject" / "Safe to format": "Eject card" → karta zmizí ze seznamu, čtečka zůstane; vložit další
      kartu → objeví se (skutečné vysunutí je zatím nevyzkoušené — nejdůležitější bod testu)
- [ ] 5. "Report PDF" otevře PDF report, HTML je vedle
- [ ] 6. Během kopie zavřít okno → dotaz "An offload is running. Quit anyway?" → "Yes" → aplikace se zavře, v cílech nezůstanou `.offload-part`.
      Soubory `.offload-part` jsou skryté: v Průzkumníku zapněte "View → Show → Hidden items", nebo použijte `dir /a /s`
      v cílové složce. Karta, jejíž kopie se zavřením přerušila, **není zálohovaná** — offloadujte ji znovu celou.
- [ ] 7. Okno Hardware (Ctrl+U): pojmenovat port, změřit rychlost — před měřením se aplikace zeptá; z karty se jen čte,
      na každý cílový disk se zapíše a hned smaže dočasný soubor 1 GB (během kopírování měření nejde spustit)
- [ ] 8. Proxies → "Also copy proxies to a folder" → "Choose…" (složka na jiném disku) → offload s proxy → proxy jsou
      v `_PROXY` na cílech i v `<složka>\<datum>\<karta>\`; po odpojení toho disku je start zablokovaný červeným textem
- [ ] 9. Proxy z log klipu má přirozené barvy; v reportu je u klipu LUT a důvod
- [ ] 10. BRAW klip: proxy, tři snímky v reportu, LUT podle gamy v reportu (viz Vzorek BRAW výše)

Plán 6 (karty a bezpečnost dat, 0.2.0):

- [ ] 11. Zkopírovanou kartu vytáhnout a vložit znovu → pod kartou šedě "Checking whether the card is already backed up…", pak zeleně "Backed up on <date time> to <destinations> — verified, safe to format"; Start → okno "Card already backed up": "Cancel" nic nespustí, "Skip backed-up" kartu odškrtne a nic nezkopíruje, "Copy again in full" vytvoří `<karta>_2`
- [ ] 12. **Karta ve dvou čtečkách:** tutéž kartu vložit do jiné čtečky (jiné písmeno, jiný GUID svazku) → stále "Backed up on …" (párování podle sériového čísla FS + kapacity, R1); kartu naformátovat v kameře a vložit → žádný
      řádek historie (nová karta)
- [ ] 13. Rychlá výměna dvou karet ve stejné čtečce během "Checking whether the card is already backed up…" → řádek patří vždy kartě, která je ve čtečce
      (výsledek staré karty se zahodí), napsaný název zmizí
- [ ] 14. Kamerou natočit na zálohovanou kartu další klipy → oranžově "· new files: N (… GB)" a tlačítko "Append" (bublina "Append new clips only to the earlier backup (N files · … GB)"); stisknout → místo názvu "Folder: <datum>\<karta> on 2 destinations", Start jde i s prázdnými cíli a bez okna "Card already backed up"; úloha "<karta> · append" →
      na obou cílech `_REPORT\offload_report_0002.json/.txt`, `<karta>_report_0002.html/.pdf`, `ascmhl\0002_…Z.mhl`,
      `ascmhl_chain.xml` se dvěma záznamy, `<karta>.xxh64` delší; původní `offload_report.json`, HTML/PDF a
      `0001_…mhl` beze změny (datum změny souboru); nové proxy v `_PROXY` bez přepsání starých; kartu vložit znovu →
      "Backed up on … — verified, safe to format" bez nových souborů; doplnit znovu z CLI `offload append E:\` → "Nothing new — the backup is complete.", kód 0
- [ ] 14b. Doplnění odmítnuté s anglickou hláškou, nic se nezapíše: jeden z dřívějších cílů odpojit → tlačítko "Append" se nenabídne (po ⟳); z CLI `offload append E:\` → "Error: The earlier backup is not available: … — connect the disk, or make a full copy."; spustit doplnění v GUI a hned v PowerShellu `offload append E:\` téže karty → druhé skončí "Another job is working with this backup right now — an append, a repair or a verification during proxies (…) — wait until it finishes."
- [ ] 15. **`ascmhl verify` po doplnění** (na macOS/Linuxu, kapitola „ASC MHL a ascmhl na Windows“): `ascmhl verify
      <cíl>/<datum>/<karta>` projde bez chyb přes obě generace (a `ascmhl info` ukáže generace 1 a 2); na Windows
      `offload verify <cíl>\<datum>\<karta>` → "… — all OK.", kód 0
- [ ] 16. **Posun času na FAT32:** kartu naformátovanou ve FAT32 (např. malá SD) zkopírovat, pak ve Windows změnit
      časové pásmo o hodinu (Nastavení → Čas a jazyk; simuluje přechod letní/zimní čas) a kartu vložit znovu → stále "Backed up on … — verified, safe to format" (ne "the card has changed since"); přidat na kartu nový soubor a doplnit → projde bez konfliktu (žádné "different size or date"); pásmo vrátit
- [ ] 16b. **Posun času na exFAT (karta z kamery):** totéž s kartou exFAT, na kterou **natáčela kamera** (karta
      naformátovaná a zapsaná ve Windows má u časů posun UTC, takže se neposune — ta test nic neověří). Zapsat, co se
      stane: a) "Backed up on … — verified, safe to format" = kamera zapisuje čas s posunem UTC, v pořádku; b) "…, but the card has changed since (N files differ) — cannot append, only a full copy" = kamera zapisuje místní čas bez posunu → nález M27:
      rozšířit toleranci celých hodin (R3) i na exFAT (`FileTimes.StoresLocalTime`, `AppendPlanner`). Kameru a model
      uvést v nálezu. Pásmo vrátit
- [ ] 17. Název z klipů: karta ARRI/Venice/RED/Blackmagic → šedě "A001" + "from the clip names (A001C001…)"; CLI `offload E:\ -d D:\Backup --name auto --dry-run` u téže karty vypíše "Card name: A001 (from the clips)" a cestu `…\A001`; karta Sony XAVC (`C0001`) po kartě `A001` téhož dne → GUI "A002" + "next after A001 (today)" (CLI `--name auto` "next after …" neumí: vypíše "No name suggestion found; the volume label is used."); **dvě neznámé karty najednou** (obě XAVC, po `A001`) → řádky ukážou "A002" a "A003" a po Startu vzniknou složky `A002` a `A003` (ne `A002_2`); volba "Suggest the card name from the clips" vypnutá → jmenovka svazku
- [ ] 18. Verify backup (horní lišta) (Ctrl+Shift+O) na složku karty i na složku dne → položky "Verification …" ve frontě po kopiích, "Verified: all N files match (ASC MHL, 2 generations, xxHash64).", tlačítko "Verification report" otevře
      `_REPORT\<karta>_verify_…Z.html`; ve zkušební kopii změnit bajt souboru → červeně "Mismatch 1 file, missing 0, unreadable 0 — details in the verification report."; během ověření odpojit disk → nečitelné soubory, "… the disk may have been disconnected."; "Cancel" → "Verification cancelled after …" bez reportu; složka zálohy z jiného programu (MHL bez `_REPORT`
      v ignore) → poznámka "The verification report is saved outside the card folder so it does not disturb its ASC MHL history: …" a report v `<datum>\_REPORT\`
- [ ] 19. Místo: cíl s málo místem (např. malý USB disk) → červeně "Not enough space on destination …: needs … (cards …, proxies ~…, report …), free …." a Start šedý; dvě karty na tentýž disk se sečtou; zapnout proxy → potřeba vzroste;
      složka pro proxy na stejném disku jako cíl se přičte; během běžící kopie vybrat další kartu → počítá se i zbytek
      běžící úlohy; skoro plný disk → oranžově "Only … will be left on destination … after the offload — less than the 5 % reserve, and the proxy estimate is approximate." (Start jde); uvolnit místo → červený text do pár sekund zmizí
- [ ] 19b. Start hned po vložení velké karty, kterou historie zná (řádek ještě "Checking whether the card is already backed up…") → pod lištou "Checking the card before the copy starts…", pak okno "Card already backed up" (ne tichá celá kopie); pomalá karta, která se do
      10 s neprověří → otázka "Card … is still being checked — it is not known yet whether it is backed up. Start a full copy anyway?", "No" nic nespustí; zavřít okno během čekání → zavře se bez hlášky
- [ ] 20. Hlášky anglicky: odpojený cíl při startu z CLI (`offload E:\ -d X:\missing`) → "Error: Destination not found: X:\missing — is the disk connected?"; neznámá volba (`offload E:\ -d D:\ --xyz`) → chyba parseru anglicky; v GUI chyba před kopií na kartě úlohy anglicky

Plán 7 (výstupy pro produkci a střih, 0.3.0):

- [ ] 21. **MD5:** Settings → Verification → Checksum "xxHash64 + MD5" → pod volbou oranžové "MD5 is slower — with fast cards (CFexpress) it may slow the copy down.", řádek "Verification by reading back" v kroku 3 ukáže "xxHash64 + MD5"; offload karty → `_REPORT\<karta>.md5`, v HTML dlaždice "XXH64 + MD5" a sloupec md5; Verify backup (horní lišta) → "… (ASC MHL, 1 generation, xxHash64 + MD5)."; **rychlá karta
      (CFexpress)**: změřit dobu kopie s xxHash64 a s MD5 (a `offload bench --hash md5`) a zapsat do části Naměřeno
- [ ] 22. **Proxy v Resolve:** naimportovat originály, proxy z `_PROXY` připojit (Link Proxy Media) — sedí jméno, TC; sloupec
      Reel ukazuje cívku (u ARRI/RED/Blackmagic `A001`, jinak tag kamery nebo složku karty)
- [ ] 23. **Proxy v Premiere:** Sony MXF se 4–8 mono stopami → proxy se stejným počtem stop; „Attach Proxies“ projde;
      Tape Name = cívka. Dva klipy stejného jména → `_2` a u klipu poznámka "Proxy renamed to … (name clash) — relink it by hand."
- [ ] 24. **CSV:** `_REPORT\<karta>_clips.csv` v Resolve (Import Metadata → Media Pool) doplní metadata; v Excelu přes
      Data → Z textu/CSV (oddělovač čárka) čeština a sloupce sedí
- [ ] 25. **ALE:** volba "Clip list for Avid (ALE)" → `<karta>_clips.ale` jde importovat do Avid Media Composeru (Start,
      End, Tape, Tracks); karta se smíšenými fps (25 a 23.976) → dva soubory `_clips_25.ale`, `_clips_23.976.ale`
- [ ] 26. **Day report:** dvě karty týž den na dva cíle → na obou `<datum>\_REPORT\<datum>_day_report.html/.pdf` s oběma
      kartami, dlaždice "verified cards 2 / 2"; během kopie třetí karty ji denní report ukáže "no report (running, or it was interrupted)"; "Day report…" (Settings → Report, nebo odkaz u TODAY) na složku dne (i na složku karty) report vytvoří a otevře; na složku bez karty s reportem → chyba "Folder … has no card with a report — select a day folder on a destination (<destination>\<date>).", na kořen disku "… is the root of a disk, not a day folder — …"; nic se nezapíše; GUI a `offload day-report` téhož dne zároveň → oba projdou
- [ ] 27. **Otevřené v Excelu:** denní CSV a `<karta>_clips.csv` otevřít v Excelu, pak doplnit kartu → úloha "Done", v `app.log` řádky "clip list …" / "day report …"; po zavření Excelu "Day report…" (Settings → Report, nebo odkaz u TODAY) soubory obnoví; volba "Day report after every card" vypnutá → po úloze se denní report nezmění
- [ ] 28. **Doplnění s MD5:** záloha s "xxHash64 + MD5", pak doplnit kartu → `<karta>.md5` je delší o nové řádky (dřívější
      řádky beze změny; je to třetí soubor, který doplnění přepíše, vedle `.xxh64` a řetězu ascmhl), Verify backup projde s "xxHash64 + MD5"; ostatní dřívější soubory zálohy beze změny
- [ ] 29. **Karta v průběhu:** během kopie je ve složce dne `<karta>\_REPORT` (zatím prázdná) a denní report karta ukáže jako "no report (running, or it was interrupted)"; přerušená kopie zůstane tak označená; karta pojmenovaná `_REPORT`,
      `_PROXY` nebo `ascmhl` dostane na konec `_`
- [ ] 30. **ALE v Avidu:** diakritika v názvech klipů se v Avidu zobrazí správně (ALE je UTF-8 bez BOM; starší Avid ji
      může číst špatně — poznamenat verzi)

Plán 8 (nastavení, oznámení, distribuce, 0.4.0):

- [ ] 31. **Projekty:** "Save as new project…" "Film X" → titulek "Offload — Film X"; změnit cíle a proxy, přepnout na "No project" a zpět — obojí má svoje hodnoty; přejmenovat a smazat (dotaz "Delete project …? The project's settings will be lost; the backups on the disks stay.", po smazání "No project"); jméno prázdné ("The name must not be empty.") / "no project" / už použité ("A project with this name already exists.") → červeně pod polem, OK nejde; stisknout "Start offload" a hned (během kontrol) zkusit přepínač a menu Project → jsou zamčené
- [ ] 32. **Přepnutí s frontou:** dvě karty ve frontě z "Film X" (s proxy) → během renderu proxy přepnout na jiný projekt
      s jinými cíli a kodekem → úlohy dokončí cíle, kodek, LUT i oznámení "Film X"; report a `history.json` mají `project` "Film X"; "next after …" (A002 …) se počítá zvlášť v každém projektu; denní report dne jen z "Film X" má v hlavičce "Project: Film X". **Doplnění:** karta zálohovaná ve "Film X" a doplněná při jiném zobrazeném projektu používá nastavení "Film X" (proxy, výstupy, ntfy téma), report si ponechá "Film X"; po smazání "Film X" doplnění běží s "No project" a karta úlohy to napíše ("Project Film X no longer exists — the append runs with the settings of No project")
- [ ] 33. **Starší / ručně upravený settings.json:** dva projekty stejného jména → platí první, druhý po uložení zůstane
      v souboru; `projects` jako text → aplikace naběhne bez projektů, ostatní nastavení zůstane; spustit 0.3.0 a zase
      0.4.0 → projekty a pravidla zůstaly
- [ ] 34. **Pravidla LUTů:** Sony FX6 (`M01.XML` se `serialNo`) → pravidlo "Camera serial number" → LUT a v reportu "rule 1: camera serial number \"…\""; pravidlo "Folder (pattern with * and ?)" s vlastním `.cube`, pak `.cube` přejmenovat → červeně "LUT of rule 1 not found: …", start nejde; ↑ ↓ ✕ mění pořadí / maže; tlačítko jen u "Automatic by camera"; cíl "No LUT" → klip bez LUTu. **Pravidlo z budoucnosti:** v `settings.json` přidat do `proxy.lutRules` pravidlo
      `{ "match": "lens", "value": "35mm", "lut": "none" }` (mezi dvě běžná) → editor ho ukáže jen ke čtení "Rule from a newer version (lens) — this version skips it", ↑ ↓ ✕ fungují, po OK zůstalo v souboru beze změny, čísla pravidel v reportu
      počítají i s ním; neplatné známé pravidlo (prázdná hodnota) se při načtení zahodí
- [ ] 35. **BRAW Gen 4 (až bude vzorek; do té doby neověřeno):** `OFFLOAD_BRAW_SAMPLE=<klip Gen 4>` → `dotnet test
      --filter BrawSampleTests` (Gen5_ConvertsAGen4Clip_LeavesGen5); v GUI RAW decoders → "Convert Blackmagic RAW Film Gen 4 to Gen 5 (the Gen 5 LUT is used)" → proxy má LUT a vypadá jako v Blackmagic RAW Playeru s Gen 5; v reportu "Blackmagic RAW: Film Gen 4 → Gen 5 (SDK conversion)"; klip Video / Extended Video Gen 4 se nepřevede. Teprve pak zvážit zapnutí jako výchozí (R26)
- [ ] 36. **Oznámení Windows (TrayNotifier, jen ručně):** okno na pozadí → po kopii "Offload: A001 — safe to eject" (s proxy), po konci "Offload: A001 done"; s chybou červená ikona; selhaná kopie ohlásí "Offload: A001 — ERROR" hned po konci kopie; kliknutí na oznámení / ikonu vrátí okno; okno v popředí → žádné oznámení, jen zvuk a blikání; oznámení zůstane
      v Centru oznámení; po zavření aplikace ikona zmizí; restart Průzkumníka → další oznámení ikonu přidá znovu;
      Settings → Notifications → Edit notifications… → "Windows notifications" vypnout → nic
- [ ] 37. **ntfy:** téma `offload-<náhodné>` → "Send a test notification" → "Sent." a zpráva v aplikaci ntfy na telefonu; server `https://nic.invalid` → "Failed: …" do 10 s, aplikace celou dobu reaguje; téma s diakritikou → OK nejde, důvod anglicky ("The topic may contain only letters without accents, digits, - and _ (at most 64 characters)."); v textu na telefonu žádná cesta ani jméno PC (jen název karty, výsledek, počty); CLI
      `--ntfy https://ntfy.sh/<téma>` (po konci čeká nejvýš 11 s, i s nedostupným serverem)
- [ ] 38. **Aktualizace:** `build.ps1 -ReleaseUrl \\NAS\Offload\` → `offload-update.json`; Settings → App → Updates… → cesta k manifestu → "Check now" → "Version … is available (you have …)." / "You have the latest version …", odkaz "New version …" vpravo v horní liště; s `https://` adresou v manifestu *Open* otevře prohlížeč, s místní složkou nebo zipem Průzkumník
      s výběrem; **adresa na NAS (`\\NAS\…`) nebo namapovaný síťový disk → bez tlačítka, jen text a "Network path — copy it and open it by hand."**; odpojený NAS → "Cannot read the manifest: … did not respond within 5 s" a okno nezamrzne;
      prázdná adresa → žádná kontrola; po 24 h se čte znovu
- [ ] 39. **Podpis:** (a) stroj s Windows SDK: `build.ps1 -Sign` bez signtoolu / s neznámým otiskem → česká chyba hned na
      začátku; testovací PFX (`New-SelfSignedCertificate -CodeSigningCert`, export) přes `OFFLOAD_SIGN_PFX` → podepíše tři
      exe, ve vlastnostech souboru je karta Digitální podpisy; nedostupné razítko (`-TimestampUrl http://nic.invalid`) →
      3 pokusy, „Časové razítko se nepodařilo získat …“; heslo se nikde nevypíše. (b) **skutečný certifikát (až bude
      koupený, viz Podepisování):** `build.ps1 -Sign -CertThumbprint …` → `signtool verify /pa` projde, SmartScreen na
      čistém počítači po stažení zipu ukáže vydavatele (reputace se buduje časem)
- [ ] 40. **Vzhled (0.5.0):** Windows v tmavém režimu → tmavé okno jako `docs/ui-navrh/screeny`; přepnout Režim aplikací na
      světlý za běhu → okno se přebarví bez restartu, všechny texty čitelné (karty úloh, žlutá / červená hlášení, Settings);
      dialogy (Hardware, Notifications, Cards already backed up) jsou ve stejném tématu; okno 1000 px široké → nic se neořízne
- [ ] 41. **Dlaždice karty:** karta z ALEXA 35 → "ALEXA 35 · LogC4", z FX6 → "FX6 · S-Log3", BRAW → "Blackmagic RAW"; čtečka USB 3 → "reader USB 3 · 5 Gb/s", SD čtečka v notebooku → "SD reader", CFexpress přes Thunderbolt → "NVMe"; karta s novými klipy → žluté "Append" (bublina = počet a velikost), po stisku "Folder: … on 2 destinations"
- [ ] 42. **Pause při kopii:** karta 50+ GB na 2 disky → Pause během kopie → "Pausing…" / "Pausing (finishing file …)", do pár sekund "Paused", rychlosti "—", Správce úloh ukáže 0 MB/s na kartě i discích; po 2 min Resume → kopie doběhne, "Safe to eject"; v reportu "paused · not in the speeds" a rychlost kopie bez pauzy; totéž během ověření
- [ ] 43. **Pause ve 2. fázi:** během proxy → "Pausing (finishing the proxy of clip …)", ffmpeg po doběhnutí klipu zmizí ze Správce úloh a nic se neobnoví; po 3 min Resume → proxy pokračují (žádné "ffmpeg stalled"); během pauzy jiná
      karta ve frontě čeká; denní report se mezitím vytvořit dá ("Day report…" u TODAY)
- [ ] 44. **Pauza a výpadek:** Pause, vytáhnout druhý cílový disk, Resume → soubory na něm selžou, první disk dokončí, karta žlutá "… does not match the card" s "✕ write"; Pause a zavřít aplikaci → dotaz, po "Yes" se okno zavře do pár
      sekund a na discích nezůstane `.offload-part`; pozastavené doplnění + druhé okno se stejným doplněním → "Another job is working with this backup right now …"
- [ ] 45. **Copy again:** po bodu 44 připojit disk → Copy again → úloha "A001 · repair" zkopíruje jen selhané
      soubory na druhý disk, první disk se jen přečte (Správce úloh: zápis jen na druhý); zelená karta; ve složce karty
      `_REPORT\offload_report_0002.json` s `"kind": "repair"`, `ascmhl\0002_…mhl` na obou discích (`verified` na prvním,
      `original` na druhém), řádek u karty zase "— verified, safe to format"; `offload verify` a `ascmhl verify` (na macOS) projdou.
      Ručně poškodit jeden soubor na disku (hex editor, stejná velikost) → Verify backup → nesedí; nová kopie → žlutá → Copy again → poškozený soubor je v `_REPORT\neshoda_0003\…`, nový sedí
- [ ] 46. **Copy again odmítne:** vyměnit kartu ve čtečce → tlačítko zmizí; zformátovat a nahrát jiný klip stejného jména → "Repair stopped — nothing was copied. …", na discích se nic nezměnilo
- [ ] 47. **Odhad a dnešek:** první karta po spuštění → lišta jen "1 card → 2 disks · … GB"; po první dokončené kartě (od 200 MB) druhá karta stejného projektu → "estimate ~… incl. proxies", po dokončení porovnat se skutečností (±30 %); TODAY ukazuje obě karty, Card history včerejší karty pod "Yesterday", "Day report…" otevře report dne
- [ ] 48. **Pozastavená úloha drží PC vzhůru (M6) a frontu:** nastavit v systému uspání po 1–2 min nečinnosti, spustit dvě karty,
      první pozastavit → druhá čeká ("Waiting for a paused job."), PC po uplynutí doby neusne (`powercfg /requests`
      ukáže Offload u SYSTEM) a disky zůstanou připojené; Resume → první doběhne, pak se rozběhne druhá; Pause a Cancel → po zrušení `powercfg /requests` Offload už neukazuje
- [ ] 49. **Ukázkový režim (`--demo`):** `Offload.exe --demo pripraveno` / `probiha` / `hotovo` / `nastaveni` ukáže čtyři
      obrazovky z `docs/ui-navrh/screeny` (01–04), srovnat se snímky v tmavém i světlém tématu (`OFFLOAD_THEME`) a v úzkém okně
      (`Resize-Offload $p 1000 900`); Start offload, Append, Copy again, Eject card a Verify backup jsou vypnuté, v `%APPDATA%\Offload`
      se nic nezměnilo (nastavení, historie ani log), složka dat je `%TEMP%\Offload-demo-…`; bez `--demo` se okno chová jako dřív
- [ ] 50. **Verify-in-Proxy na kartě (0.6.0):** karta s MOV/MP4/MXF, proxy zapnuté, 2 disky (NVMe + USB HDD) → po kopii
      čip "Safe to eject" neutrálně (ne zeleně) a "Verifying…"; Správce úloh: disk primárního cíle (nejrychlejší) se během proxy čte jen
      jednou (Monitor prostředků: čtení ≈ velikost klipů), druhý disk se čte souběžně; po proxy krátce "Finishing verification", pak "Safe to format" zeleně a oznámení "Offload: A001 done" ("Verified on 2 disks — the card is safe to format."); report: Verification "during proxies" u primárního cíle, "read back" u druhého, sériová čísla svazků, "until “safe to format”"
- [ ] 51. **Čas proti samotné proxy (spec 11):** stejná karta jednou s `offload copy --proxy --classic-verify` a jednou
      bez → čas do "Safe to format" (`safeToFormatSeconds`) s jedním cílem nejvýš o 5 % delší než render proxy samotný;
      se dvěma cíli proxy nezpomalí o víc než 5 %
- [ ] 52. **RAW (režim B):** karta BRAW (a R3D/ARRIRAW, když je nástroj) → Monitor prostředků: soubor klipu se z disku
      přečte asi 2× (ruling B1: Offload ho ověří čtením mimo cache a SDK si ho pak přečte z disku znovu, ne z paměti —
      zrychlení 1,05× se u RAW nesplňuje), report "during proxies"; klip větší než polovina volné RAM → ověří ho dorovnání zpětným čtením (report pořád správně)
- [ ] 53. **Test cache s přímým zápisem na zařízení (vyžaduje správce, spec 11):** VHDX 4 GB (Správa disků → Vytvořit
      VHD), NTFS, jako druhý cíl; po kopii (čip "Safe to eject") zapsat jeden bajt doprostřed klipu **přímo na zařízení**
      (`\\.\PhysicalDriveN`, HxD → Otevřít disk, bez souborového systému, takže cache Windows o změně neví) → cíl na VHDX
      musí skončit "block … mismatch (destination …, card …)", žádné "Safe to format"; totéž pro primární cíl (změna před proxy)
- [ ] 54. **Vada obsahu:** klip z kamery s poškozeným snímkem (nebo uříznutý MP4) → kopie "verified", žlutý štítek vady s timecodem, Preview otevře obrázek místa; "Safe to format after checking defects", po "Checked — the card is safe to format" zeleně
- [ ] 55. **Pád během kopie:** velká karta, během kopie velkého klipu ukončit Offload ve Správci úloh → znovu spustit,
      vložit kartu → řádek "Unfinished backup on … to … — the copy was interrupted; it can be finished" a zaškrtávátko "Finish interrupted backup"; Start →
      dokončené soubory se znovu nečtou (Monitor prostředků: karta se čte jen pro zbytek), rozepsaný klip pokračuje, výsledek
      ověřený, `ascmhl verify` projde
- [ ] 56. **Pád během ověření:** s proxy zavřít Offload ve Správci úloh během "Verifying…" → report `verifyPending`, denní report "VERIFYING"; Append / Finish interrupted backup → karta se nečte, disky se ověří a generace se zapíše
- [ ] 57. **Odpojený disk během ověření:** během proxy odpojit druhý disk → jeho soubory "✕ read", "Do not format", primární cíl ověřený, záloha "unfinished" (výsledek se do odpojeného disku nezapsal; Copy again se zatím odmítne s odkazem na Finish interrupted backup); po připojení vložit kartu → nejdřív "Finish interrupted backup" → karta se nečte, druhý disk se přečte a ověří, "Safe to format"; jen když tam pak některé soubory nesouhlasí, Copy again
- [ ] 58. **Nastavení a CLI:** Settings → Verification → vypnout "Verify the primary disk during proxies" → ověření zase hned po kopii; `offload copy … --primary 2` → report "during proxies" u cíle 2; `--primary 3` se dvěma cíli → chyba "--primary 3: there are only 2 destinations (numbered from 1 in the order of --dest)." a kód 1, nic se nekopíruje; síťový cíl (NAS) → "network destination — verified by reading back"
- [ ] 59. **Automatické načtení disků — USB disk:** připojit externí USB disk (NTFS, prázdný nebo se staršími zálohami
      `<datum>\<karta>\…`) → do 2 s se sám objeví v "2 · DESTINATIONS" (a zmizí z "1 · CARDS"); odpojit ho → z cílů zmizí; připojit znovu → zase je tam; křížkem ho odebrat → po odpojení a připojení se už nepřidá (Settings → Folder structure → "Forget removed disks (1)" to vrátí); disk s méně než 1 GB volna nebo jen pro čtení (`diskpart` →
      `attributes volume set readonly`) → nepřidá se, pod cíli oranžově "Disk … is full — not added as a destination." / "Disk … is read-only — not added as a destination."; interní disk SATA/NVMe ani systémový disk se nepřidá nikdy; záložní disk s klipy ve složce (uživatelův
      E: WD Elements) → přidá se (v "1 · CARDS" zůstane jen do přidání); SSD z kamery s klipy BRAW/MOV přímo v kořeni nebo
      disk se strukturou kamery v kořeni (PRIVATE, DCIM, CLIP, ARRI, RED…) → nepřidá se; disk se zálohou z Offloadu
      (`_REPORT` nebo `ascmhl` do 3 složek) → přidá se vždy
- [ ] 60. **Automatické načtení disků — čtečka:** čtečka SD/CFexpress s kartou z kamery → karta je v "1 · CARDS" zaškrtnutá
      a **nikdy** není v cílech (ani CFexpress čtečka, kterou Windows hlásí jako pevný disk USB — rozhodne struktura kamery,
      název výrobku čtečky nebo víc slotů na jednom zařízení); karta ze zvukového rekordéru (jen WAV) → zaškrtnutá; prázdná
      karta → v seznamu, nezaškrtnutá; vypnout "Automatically select cards in readers as sources" → nová karta se nezaškrtne; během kontrol "Start offload" připojit disk → přidá se až po spuštění (ruling I1); `--demo` → nic se nepřidá
- [ ] 61. **Kopie reportu na počítači:** po kopii karty je `Documents\Offload reports\<datum>\<karta>\` s `_REPORT`
      (HTML, PDF, JSON, TXT, `.xxh64`, CSV, manifest) a `ascmhl`, bez klipů a proxy; po proxy/analýze se HTML s náhledy
      obnoví; denní report v `…\<datum>\_REPORT\`; Append / Copy again / Finish interrupted backup / Verify backup → kopie se obnoví; druhá karta téhož jména téhož dne → `A001_2`; Settings → Report → "Copy of the report on this computer too" vypnout nebo "Change…" složku (síťová složka, odpojený disk) → úloha doběhne, na kartě úlohy jen šedá poznámka "Could not copy the report to this computer: … — the report is only on the destination disks."; Dokumenty přesunuté na OneDrive/jiný disk → kopie jde tam; `offload copy … --no-local-report` → nic

Zjištění z ostrého nasazení Mac verze (2026-10-09; art-cmd ani REDline na vývojovém PC nejsou — testy jen s náhradami):

- [ ] 62. **Proxy bez hardwarového dekódování:** karta s H.264/HEVC 4K → proxy bez zeleného snímku (kontrola
      `ffprobe -f lavfi -i "movie=<proxy>,signalstats"` — žádný snímek s UAVG i VAVG u nuly); Settings → Proxies "Hardware decoding (experimental)" zapnout → log ffmpeg má `-hwaccel auto`, proxy dál bez zelených snímků;
      `offload … --proxy --hw-decode` totéž.
- [ ] 63. **ARRIRAW sférický / anamorfní (art-cmd):** sférický klip ALEXA 35 4.6K OG → art-cmd bez `--desqueeze`
      (Správce úloh → Podrobnosti → příkazový řádek), rychlost dekódování ~2× vyšší než dřív; anamorfní klip (2×) →
      s `--desqueeze`, proxy roztažená na správný poměr stran.
- [ ] 64. **Priorita nástrojů:** spustit Offload s nízkou prioritou (`start /belownormal Offload.exe`) → během proxy
      ve Správci úloh (Podrobnosti → Priorita, Process Explorer → I/O priorita) ffmpeg, art-cmd, REDline a braw-decode
      „Normální“; ARRIRAW 4.6K dekóduje stejně rychle jako stejné volání art-cmd z terminálu.
- [ ] 65. **Průběh ARRIRAW:** proxy klipu ARRIRAW → pruh klipu během art-cmd plynule roste do ~76 %, pak ffmpeg do 100 %;
      nikdy nestojí na 0 % celé dekódování.
- [ ] 66. **Analýza ARRIRAW:** karta ALEXA 35 s velkými klipy (100+ GB) → analýza v řádu sekund na klip (art-cmd
      `export … --duration 1`); v reportu rozlišení, fps, délka (počet snímků jako v ARRI Reference Tool), TC prvního
      snímku, kamera, log (LogC4) a squeeze stejné jako dřív; art-cmd, který `--duration` nezná → v logu „exporting the
      metadata of every frame“ a analýza projde (pomalu); sekvence `.ari` → počet snímků = počet souborů. Zjistit, jaké
      `clipDuration` vrátí export jednoho snímku: když 1 a ffprobe na MXF nedoběhne, délka zůstane prázdná (v logu „length
      unknown“). X-OCN MXF s nainstalovaným art-cmd → art-cmd jen jednou, poznámka cituje jeho chybu. Zrušení úlohy během
      dekódování art-cmd → art-cmd do vteřiny skončí.
- [ ] 67. **ARRIRAW bez LUTu:** `offload … --proxy --lut none` s klipem ALEXA 35 a ALEXA Mini → proxy v Rec.709
      (vestavěný vzhled kamery), art-cmd bez `--target-colorspace`; poškozený klip / art-cmd, který skončí kódem 0 bez
      souboru → poznámka "ARRIRAW: art-cmd ended without an error but wrote nothing — no proxy was made ([error] …)".
- [ ] 68. **Snímací fps — ARRIRAW:** karta ALEXA 35 natočená 35 fps (projekt 24, např. A_0012_1F75) → v HTML/PDF u každého klipu FPS "35 fps" a pod ním "playback 24", pod tabulkou vysvětlivka "… Duration = frame count ÷ playback fps (the length in the edit).", Duration = snímky ÷ 24 (stejná jako v ARRI Reference Tool / Resolve); `offload_report.json` `clips[].info.captureFrameRate`
      `"35"`, `frameRate` `"24"`; TXT `Frame rate:  35 fps capture, 24 playback (N clips)`; CSV `FPS` 24, `Capture FPS`
      35; ALE `FPS 24` v hlavičce a sloupec `Sensor_fps` 35 (import do Avidu: sloupec se objeví v binu, `End` sedí);
      denní report: sloupec FPS "35 fps · playback 24", dlaždice "total duration". Karta natočená 24 fps → jen "24".
- [ ] 69. **Snímací fps — BRAW, RED, Sony:** klip BRAW natočený off-speed (např. 48 fps v projektu 24; nejdřív
      `braw-decode.exe --info` → klíč `"sensorRate":48.000000` na konci řádku), klip R3D s jiným record a project fps
      (zkontrolovat, že `REDline --printMeta 1` vypisuje "Record FPS" a "Project FPS" / "Timebase" — jinak upravit
      `R3dBackend.ParseMeta`), Sony S&Q (FX3/FX6, 120 fps do 23.98p: `M01.XML` `captureFps="119.88p"`) → FPS "119.880 fps" + "playback 23.976" / "48 fps" + "playback 24". Starší report (0.7.x) bez klíče se otevře a ukáže jen přehrávací fps.
      V HTML/PDF jsou obě rychlosti ve sloupci FPS na dvou řádcích (náhled 1000 px, 1300 px i PDF): text nepřetéká do sloupce Duration.
- [ ] 70. **Snímací fps — Canon (chybí vzorek):** karta Canon (C70/C300 III/R5 C, XF-AVC MXF a Cinema RAW Light .CRM)
      natočená S&F (Slow & Fast, např. 100 fps do 25p) → zatím se ukáže jen přehrávací rychlost (25). Prohlédnout, co karta
      nese: XML vedle klipu (`CONTENTS\CLIPS001\…\*.XML`), `ffprobe -show_format -show_streams` (tagy MXF) — kde je snímací
      rychlost, poslat vzorek; podle něj doplnit čtení do `ProxyRenderer.Probe` (jako Sony `M01.XML`).

- [ ] 72. **Pause během velkého souboru (kopie):** karta s klipem 20+ GB na dva disky → Pause uprostřed souboru → karta úlohy "Pausing (finishing file A001C001.mxf…)", rychlosti dál běží, soubor se dokopíruje na **oba** disky → "Paused — Resume continues with the next file.", rychlosti "—"; v cílových složkách žádný
      `.offload-part`, soubor je na místě v plné velikosti a v `_REPORT\offload-manifest.jsonl` (i malé soubory —
      při zastavení se vyprázdní cache disků). Resume → kopie pokračuje dalším souborem (čtení karty neopakuje hotový
      soubor), report: `copyPausedSeconds` ≈ doba, kdy úloha **stála** (ne od stisku tlačítka). Pause, vyměnit
      kartu za jinou (stejná kamera) a Resume ještě během "Pausing" → úloha selže s hláškou "The card in the reader was swapped or removed while paused — the remaining files were not copied. Insert the original card and append to the backup.", z jiné karty se nic nezkopíruje.
- [ ] 73. **Pause během ověření:** klasické ověření (Settings → Verification, nebo bez proxy) → Pause během čtení velkého souboru → "Pausing (finishing file …)", soubor se dočte na všech discích, pak "Paused"; Resume → ověří zbytek, `verifyPausedSeconds` v reportu. S Verify-in-Proxy (proxy zapnuté):
      Pause během proxy → klip se dorenderuje, čtení na pozadí sekundárního disku dočte svůj soubor, pak "Paused"; 5 minut pauzy → žádné "the disk stopped responding during verification", po Resume vše ověřené. Sekundární disk během "Pausing" odpojit tak, aby visel (nebo zablokovat) → po doběhnutí rozpracovaného klipu a limitu hlídače jeho soubory selžou a úloha se zastaví; disk jen vytížený renderem (pomalé čtení, ale čte) během "Pausing" nikdy neselže.
- [ ] 74. **Pause během analýzy a proxy:** "Pausing (finishing clip A001C003…)" / "Pausing (finishing the proxy of clip A001C003…)" → klip doběhne (proxy kompletní, přehratelná), pak "Paused — Resume continues with the next clip."; Resume → další klip, hotový se neopakuje.
- [ ] 75. **Cancel během "Pausing":** Pause uprostřed souboru 100+ GB a hned Cancel → úloha skončí do pár vteřin jako Cancelled (nečeká na dokopírování souboru), žádný `.offload-part`; totéž v "Paused" a při zavření aplikace. Výjimka: přijde-li Cancel ve chvíli, kdy se před "Paused" vyprazdňuje cache cílových disků, počká na dokončení tohoto vyprázdnění (obvykle vteřiny; jako vyprázdnění na konci kopie, které proběhne i po Cancel). Append a Copy again: Pause → soubor se dokopíruje, pak stojí; vysunout kartu jde až po kopii
      (jako bez pauzy).

Souhrnný report z vybraných záloh (úkol 2026-10-09):

- [ ] 76. **Souhrnný report v aplikaci:** Card history → zaškrtnout 3 karty ze dvou dnů a dvou disků → "Summary report (3)…" (i v TODAY) → otázka "Also save the summary report (3 cards) to a disk of your choice?" → No: otevře se `Documents\Offload reports\summary\<datum>_<čas>_summary.html`, vedle PDF (A4 na šířku, nic neuříznuté); natáčecí dny,
      tabulka dnů a karet s klipy, soubory, daty, stopáží, první a poslední klapkou (TC + klip; ALEXA "time-of-day TC"), kamery, ověření, "Where everything is backed up" (Disk, Path, Disk connected, Verified, Report copy on this computer). Yes → vybrat disk → soubory i tam; podruhé tamtéž → `_2`, nic se nepřepíše; vybrat složku karty se zálohou (i rozpracované —
      jen prázdný `_REPORT`), kartu ve čtečce (i mimo frontu), flash disk nebo disk jen pro čtení → anglická hláška proč (např. "… is on removable media (a card in a reader, a flash drive) — the summary report is not saved there; select a disk."), report jen na počítači (totéž `offload summary-report … -o <karta>` → kód 1). Tutéž kartu zálohovanou dvěma
      úlohami na dva disky zaškrtnout obě → v součtech jednou, v "Where everything is backed up" oba disky. "Clear selection" odškrtne vše; během tvorby odkaz "Making summary report…" a okno reaguje; 200 karet do pár sekund.
- [ ] 77. **Odpojený disk:** jednu z vybraných karet zálohovat na disk, který pak odpojit → v souhrnu údaje z kopie reportu na počítači ("the backup disk is not connected — data from the report copy on this computer", sloupec Disk connected "not connected", sloupec Verified s poznámkou o zdroji v závorce); smazat i kopii v `Offload reports` → karta v reportu je, "report not available", soubory a velikost podle historie, report vznikne. Karty, disky ani `Offload reports\<datum>` se nezmění (porovnat časy souborů).
- [ ] 78. **Klapky ze skutečných karet:** karta Sony/Canon (MXF/MP4 s `creation_time`) s klipy natočenými přes půlnoc nebo
      s resetovaným číslováním → první a poslední klapka podle času záznamu (`clips[].info.recorded` v reportu); ARRIRAW /
      BRAW (bez času záznamu) → podle čísla klipu; ALEXA → časy první a poslední klapky odpovídají TC denní doby na
      klapce. `offload summary-report <složka dne> -o <složka>` vypíše obě cesty.

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

## Použití

```powershell
offload E:\ -d D:\Backup -d F:\Backup          # karta E: na dva disky, ověření, report
offload verify D:\Backup\2026-10-04\A001        # znovu ověřit hotovou kopii
offload bench --read E:\ --write D:\ --gb 2     # rychlost karty a disků
offload ports                                 # porty, kde a jak rychle jsou disky připojené, doporučení
offload ports --measure --card E:\ --dest D:\   # + změřit skutečnou rychlost
offload E:\ -d D:\Backup -d F:\Backup --proxy --lut look.cube --frame-aspect 1.85   # + proxy s LUT a výřezem
offload E:\ -d D:\Backup --proxy --proxy-folder G:\Proxy   # proxy navíc do G:\Proxy\<datum>\<karta>\
offload E:\ -d D:\Backup --proxy --lut sony-slog3-sgamut3   # vestavěný LUT podle id
offload luts                                                # seznam vestavěných LUTů (id — název)
offload E:\ -d D:\Backup -d F:\Backup --name auto            # název složky z názvů klipů (A001C003… → A001)
offload append E:\                                          # doplnit jen nové klipy do dřívější zálohy (cíle z historie)
offload append E:\ --into D:\Backup\2026-10-06\A001 --into F:\Backup\2026-10-06\A001
offload verify D:\Backup\2026-10-06 --report                # ověřit všechny karty dne proti ASC MHL + report ověření
offload E:\ -d D:\Backup -d F:\Backup --hash both --ale     # xxHash64 + MD5, seznam klipů i pro Avid
offload day-report D:\Backup\2026-10-06                     # denní report znovu (HTML, PDF, seznam klipů dne)
offload summary-report D:\Backup\2026-10-06\A001 F:\Backup\2026-10-07 -o G:\   # souhrnný report vybraných záloh (+ kopie na G:\)
offload bench --hash md5                                    # rychlost MD5 proti xxh64 (CPU)
offload E:\ --project "Film X"                              # nastavení projektu z aplikace; volby z příkazové řádky mají přednost
offload E:\ -d D:\Backup --proxy --lut-rule camera=FX6:sony-slog3-sgamut3cine --lut-rule "folder=A00*/*:C:\luts\show.cube"
offload E:\ -d D:\Backup --proxy --braw-gen5                 # BRAW Film Gen 4 jako Gen 5 (neověřeno, čeká vzorek Gen 4)
offload E:\ -d D:\Backup --proxy --hw-decode                 # H.264/HEVC dekóduje grafická karta (experimentální)
offload E:\ -d D:\Backup --ntfy https://ntfy.sh/my-secret-channel   # výsledek na telefon (jen název karty, výsledek, počty)
```

Plán 8: `--project <název>` vezme projekt uložený v aplikaci (`settings.json`; cíle, ověření, součet, výstupy, oznámení,
proxy) jako výchozí hodnoty — co je napsané na příkazové řádce, má přednost (`-d` nahradí cíle projektu, `--lut-rule`
jeho pravidla); projekt, který navrhuje název z klipů, se chová jako `--name auto`; neznámý projekt → `Error: Project "X" does not exist. Projects: …` (kód 1); `--project "No project"` vezme nastavení bez projektu (starý název `"Bez projektu"` se stále přijímá). U `append` dává
`--project` jen nastavení — projekt zálohy zůstane podle dřívějšího reportu.
`--lut-rule <camera|serial|reel|folder>=<hodnota>:<lut id|none|cesta.cube>` (opakovatelné, pořadí = priorita; hodnota
nesmí obsahovat `:`; platí jen s `--lut auto` — s jiným `--lut` je to chyba; chybějící `.cube` je chyba, i s `--dry-run`).
`--ntfy <url>` pošle výsledek (i odmítnutí před kopií) na ntfy, po konci čeká nejvýš 11 s (10 s odeslání + 1 s rezerva; pak "ntfy: not waiting any longer");
oznámení Windows CLI neposílá.

Výstupy (plán 7): po každé kopii nebo doplnění vznikne `_REPORT\<karta>_clips.csv` (`--no-clip-list` vypne, `--ale`
přidá `<karta>_clips.ale` pro Avid) a na každém cíli znovu denní report `<datum>\_REPORT\<datum>_day_report.html/.pdf`
se seznamem klipů dne (`--no-day-report` vypne); CLI vypíše "Day report: …". `--hash both` počítá navíc MD5
(`<karta>.md5`, MD5 v reportu a ASC MHL); `append` volbu nemá — doplnění počítá součty dřívější zálohy. `day-report
<složka>` vytvoří denní report znovu (složka dne, nebo karty v ní), kód 0/1.
`summary-report <složky karet | složky dne | id z historie…> [-o <složka>]` (úkol 2026-10-09) udělá jeden souhrnný
report (HTML + PDF, bez náhledů) z vybraných záloh — i z různých dnů a disků: natáčecí dny, za kartu a celkem klipy,
soubory, velikost, stopáž, první a poslední klapka (startovní TC prvního a posledního nahraného klipu, u ALEXY TC denní
doby), kamery, ověření a kde je co zálohované. Čte reporty `offload_report*.json` ze zálohy (všechny generace), když disk
není připojený, z kopie v `Documents\Offload reports` (čte i starou složku `Offload reporty`); když není ani ta, řekne to v reportu. Uloží se vždy do
`<Offload reports>\summary\<datum>_<čas>_summary.html/.pdf`, s `-o` i do zadané složky (nikdy přes existující soubor —
přípona `_2`, `_3` …; do složky karty se zálohou nikdy). Vypíše "Summary report: …"; kód 0, kód 1 = nic nevzniklo nebo
se nepovedlo `-o`.

Historie (`%APPDATA%\Offload\history.json`): `copy` před plánem vypíše "Backed up on … to … — verified, safe to format", když kartu zná (bez
dotazu), a po dokončené kopii ji zapíše; `--no-history` čtení i zápis vypne. `append` bez `--into` najde složky zálohy
v historii (jinak "Error: The card is not in the history — give the backup folders with --into."; s `--no-history` je `--into`
povinné); kód 0 = doplněno, nebo "Nothing new — the backup is complete.", kód 1 = odmítnuto (jiná karta, změněné soubory, nedostupný dřívější cíl, běží jiné doplnění téže zálohy …, anglicky s důvodem). `--dry-run` plán jen vypíše (běh plánuje
znovu pod zámkem složky karty). `--name auto` vezme cívku z názvů klipů, jinak jmenovku svazku (bez "next after …", to umí jen GUI). `verify` čte ASC MHL všech generací (bez `ascmhl\` seznam `_REPORT\*.xxh64`), řádky problémů zůstávají
strojové (`MISMATCH`, `MISSING`, `ERROR`, `EXTRA`, `FAILED-AT-COPY`, `FAILED-SUPERSEDED`, `CHAIN`), souhrn je anglicky ("Verified N files (… GB) in … (… MB/s) — all OK." / "… — PROBLEMS FOUND: mismatch N, missing N, unreadable N, failed during the copy N, extra N."), kód 1 při jakémkoli problému; složka dne ověří každou kartu; `--report` zapíše report ověření do `_REPORT` karty (u MHL,
které `_REPORT` neignoruje, vedle složky karty). Celé CLI (nápověda, průběh, chyby i souhrny) je anglicky.

`--lut auto|none|<id>|<cesta.cube>` (výchozí `auto`): `auto` vybere LUT podle kamery každého klipu, `none` žádný,
`<id>` vestavěný LUT, cesta s `.cube` nebo lomítkem vlastní LUT. Seznam id vypíše `offload luts` — samostatný příkaz,
protože `copy` vyžaduje kartu a cíl, takže `copy --lut list` by neprošlo parsováním. `--lut-fallback <id|none>` je LUT
pro klipy, které `auto` nepozná (výchozí `none`). Neznámé id skončí chybou (exit 1). Vestavěné LUTy jsou v balíčku
ve složce `luts\` vedle `Offload.exe` (11 souborů `.cube` + `luts.json`); chybějící složka nic neshodí, `auto` pak
LUT nepoužije. Soubor `%APPDATA%\Offload\luts\<id>.cube` nahradí vestavěný LUT se stejným id (`offload luts` ho vypíše s "(custom file)").

`--proxy-folder <složka>` (jen s `--proxy`): každá hotová proxy se kromě `_PROXY` na cílech zkopíruje i do
`<složka>\<datum>\<karta>\<složka klipu>\` (od 0.5.1 stejné podsložky jako `_PROXY`, tedy jako na kartě; stejný název
souboru; když už tam soubor je, uloží se jako `_2`, `_3`… — nic se nepřepíše).
Neexistující složka zastaví start chybou (exit 1); když složka zmizí během úlohy (odpojený disk), proxy zůstanou na cílech
a u klipu je v reportu poznámka "the copy to … failed: …".

`--hw-decode` (2026-10-09, `proxy.hardwareDecode`, v GUI Settings → Proxies "Hardware decoding (experimental)"):
originály H.264/HEVC dekóduje pro proxy grafická karta (`-hwaccel auto`). Výchozí je softwarové dekódování — Mac dostal
z hardwarového dekodéru zelené snímky bez chyby; na tomto PC (i5-14400F, RTX 3070, ffmpeg 8.1, `-hwaccel auto` = DXVA2)
byly všechny snímky v pořádku a 4K H.264 bylo softwarově rychlejší (3,3–4,4 s proti 5,7–6,2 s), 4K HEVC hardwarově
(3,6 s proti 5,1–8,4 s). Kódování (NVENC/QSV/AMF) zůstává hardwarové; selhání s hardwarovým dekódováním se zkusí znovu softwarově.

`--proxy-always` (od 0.5.1): proxy i pro klip, jehož originál je lehčí — ve výchozím stavu (`proxy.skipLighterSources`, jen u proxy
H.264/HEVC — s `--proxy-codec prores…` se proxy renderuje vždy — a jen když se nepoužije LUT ani ořez) originál H.264/HEVC, který není větší než proxy a nemá vyšší datový tok, proxy nedostane ("no proxy — the original is lighter (X Mb/s ≤ Y Mb/s)" v reportu, souhrn "none (original lighter) N").

`offload ports` pojmenuje každý USB konektor jedním popiskem, který platí všude (řádek disku, seznam portů,
doporučení): „port 6“ (číslo USB 2 poloviny konektoru), port za hubem „port 7 › 2“, při více řadičích s řadičem
„Intel USB 3.20 xHCI · port 3“. U portu je písmeno disku, který v něm je („← E:“). Cíl na stejném svazku jako karta
se neměří (na kartu se nikdy nezapisuje); Ctrl+C při `--measure` dokončí měření aktuálního zařízení a smaže jeho
dočasný soubor.

Stavový řádek `offload copy` ukazuje, který článek brzdí ("bottleneck: D1", "bottleneck: card", "(link full)") — podle toho,
kolik bloků čeká na zápis na každý disk, ne podle okamžité rychlosti (karta i disky jedou v kopii stejným tempem).
Seznam "Connections:" nad ním ukazuje, co je co ("D1 = Destination 1: WD Elements 25A2 — USB 3 (5 Gb/s), port 6").

Na Windows se čte i zapisuje mimo systémovou cache (`FILE_FLAG_NO_BUFFERING`), po zápisu
`FlushFileBuffers`; ověření tak čte data z disku, ne z RAM. Svazky, které to neumí (některé síťové),
přejdou na běžný zápis a report to uvede.

PDF tiskne Microsoft Edge v režimu bez okna (je ve Windows vždy). ffmpeg/ffprobe jsou v `bin\` vedle `offload.exe` (build skript je vezme z `windows\tools\bin`, jinak z PATH —
na tomto PC build gyan.dev, licence GPL; při dalším šíření je třeba přiložit licenci a zdroj ffmpeg).

## ASC MHL a `ascmhl` na Windows

Nástroj `ascmhl` 1.0.1 má příkaz `verify` jen ve skriptu `ascmhl-debug` (`python -m ascmhl` nemá vstupní bod).
Na Windows hlásí falešné „new/missing file“, protože porovnává cesty s `\` s cestami s `/` v MHL (MHL je
v tom správně). Po pouhé normalizaci oddělovačů ověřil všechny hashe složek vytvořených na Windows.
Plné `ascmhl verify` proto pouštějte na macOS/Linuxu; na Windows použijte `offload verify`.

## Ruční test (checklist)

Ostrý test na skutečné kartě:

- [ ] kopie ze skutečné karty na 2 disky
- [ ] `offload verify` na hotovou kopii
- [ ] Ctrl+C během kopie a během ověření — nezůstane žádný `.offload-part`, report říká "NOT VERIFIED"
- [ ] vytažení karty uprostřed kopie
- [ ] vytažení cílového disku uprostřed kopie
- [ ] naměřené rychlosti zapsat do části Naměřeno

## Naměřeno

(doplnit po ostrém testu: karta, čtečka, disky, MB/s)

Každá změna Windows verze přidá záznam do `../docs/VYVOJ-WINDOWS.md` ve stejném commitu.
