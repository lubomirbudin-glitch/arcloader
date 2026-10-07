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
| BRAW dekodér (C#, Blackmagic RAW SDK) | hotovo (plán 3, `bin\braw-decode.exe`) — ověřeno jen s knihovnou z DaVinci Resolve, dekódování snímků a rozložení VARIANT (gama/gamut) čeká na vzorek .braw; Film Gen 4 zatím bez LUTu (provizorně); v GUI v seznamu nástrojů řádek „braw-decode“ ukazuje „nalezeno“ i bez knihovny `BlackmagicRawAPI.dll` (BRAW je pak jen zkopírovaný a ověřený, důvod je v poznámce u klipu) |
| HW diagnostika USB | hotovo (CLI `offload ports`, v GUI okno Hardware a kontrola na vyžádání) |
| GUI `Offload.exe` | hotovo (čeká ruční test s kartou) |
| Vestavěné LUTy (plán 5) | hotovo (čeká ruční test s klipy z kamer) |
| Karty a bezpečnost dat (plán 6, 0.2.0): historie, doplnění, název z klipů, ověření zálohy, místo, české hlášky | hotovo (čeká ruční test s kartou) |
| Výstupy pro produkci a střih (plán 7, 0.3.0): denní report, seznam klipů CSV/ALE, proxy pro přelinkování, MD5 | hotovo (čeká ruční test v Resolve, Premiere, Avid, Excelu) |
| Nastavení, oznámení, distribuce (plán 8, 0.4.0): projekty, vlastní pravidla LUTů, BRAW Film Gen 4 → Gen 5, oznámení Windows a ntfy, kontrola aktualizací, podpis | hotovo (čeká ruční test: projekty a oznámení v GUI, podpis s certifikátem; převod Gen 4 je neověřený, čeká vzorek .braw Gen 4) |
| Nový vzhled (plán 9, 0.5.0, návrh `docs/ui-navrh/`): tmavé i světlé téma podle Windows, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, Pozastavit, Zkopírovat znovu (oprava), dnes a historie karet, stránka Nastavení, štítek kamery a připojení, odhad času | hotovo (čeká ruční test 40–49 s kartou; snímky ukázkového režimu porovnané s `docs/ui-navrh/screeny`) |

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
   `-ReleaseUrl` je adresa, kterou uživatel otevře tlačítkem *Otevřít* (https adresa, `\\NAS\Offload\`, sdílená složka).
3. Nahrajte zip na web / NAS a **až potom** manifest (manifest nesmí ukazovat na zip, který tam ještě není).

Kontrola aktualizací v aplikaci (Nastavení → Aplikace → Aktualizace…) čte manifest z adresy, kterou tam zadáte (`https://…`,
`\\NAS\Offload\offload-update.json`, místní cesta); nejvýš jednou za 24 h, 5 s po startu, nic nestahuje ani nespouští,
čte nejvýš 64 KB. Tlačítko *Otevřít* otevře jen adresu `https://…` nebo existující složku / zip (v Průzkumníku); cokoli
jiného se jen zobrazí. Prázdná adresa = vypnuto (výchozí). Repozitář je soukromý, proto se nečte GitHub.

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
Bez podpisu (build bez `-Sign`, viz Podepisování) Windows při prvním spuštění ukáže „Windows ochránil váš počítač“ —
klikněte na „Další informace“ → „Přesto spustit“.

- **Vzhled (0.5.0, návrh `docs/ui-navrh/`):** horní lišta (Projekt, stav fronty, Ověřit zálohu, Hardware s tečkou,
  Nastavení), vlevo kroky „1 · Karty“, „2 · Cíle“, „3 · Po zkopírování“ s připnutou lištou Spustit offload, vpravo
  Fronta / Historie karet a „DNES“; volby jsou na stránce Nastavení (ikona s posuvníky, Ctrl+,; Esc zavře). Tmavé
  i světlé téma podle Windows (`Themes/Dark.xaml`, `Light.xaml`); `OFFLOAD_THEME=dark|light` téma vynutí (pro snímky).
  Ukázkový režim pro vizuální kontrolu: `Offload.exe --demo pripraveno|probiha|hotovo|nastaveni` (stavy obrazovek
  z `docs/ui-navrh/screeny`; výchozí vypnutý). Demo je zapečetěné: vlastní složka dat v %TEMP% (nastavení, historie i log;
  `OFFLOAD_SETTINGS_DIR` se ignoruje), nic nekopíruje ani nezapisuje, Spustit, Doplnit, Zkopírovat znovu, měření portů,
  úpravy projektů, oznámení, aktualizace, Ověřit zálohu, Denní report i Vysunout jsou vypnuté; jen Pozastavit / Pokračovat /
  Zrušit na ukázkové úloze mění, co karta ukazuje. Snímky přes `scripts\app-drive.ps1` (`Start-Offload -Arguments '--demo probiha'`,
  `Resize-Offload $p 1000 900`).
- **Projekt:** rozbalovací „Projekt <název> ▾“ v horní liště — pojmenovaná nastavení (cíle, kontrola, součet, název
  z klipů, výstupy, oznámení, proxy včetně LUTu, pravidel LUTů a složky pro proxy). V seznamu: „Uložit jako nový
  projekt…“, „Přejmenovat…“, „Smazat…“; „Upravit projekt“ u kroku 3 otevře Nastavení. Titulek okna „Offload — Film X“. Přepnutí nezmění úlohy ve frontě (mají svou
  kopii nastavení); během přípravy startu je přepínač i menu zamčené (a když se projekt přesto změní, start se
  zruší s hláškou). Doplnění karty se řídí projektem dřívější zálohy; když ten už neexistuje (smazaný, přejmenovaný),
  běží s nastavením „Bez projektu“ a karta úlohy to napíše. Start kontroluje jen LUTy a složky, které se opravdu
  použijí. Cesty k nástrojům, názvy USB portů a kontrola aktualizací jsou společné.
- **Pravidla LUTů:** u „Automaticky podle kamery“ tlačítko „Pravidla LUTů… (N)“ — řádky *kamera / sériové číslo / cívka /
  složka → LUT*, zkoušejí se shora dolů před rozpoznáním kamery; report u klipu ukáže „pravidlo 2: …“. Cíl „Bez LUT“
  dá klipu bez LUTu. Pravidlo z novější verze Offloadu (neznámý druh, např. `lens`) editor ukáže jen ke čtení,
  přeskočí ho a při OK ho zapíše beze změny.
- **Oznámení:** Nastavení → Oznámení → Upravit oznámení… — oznámení Windows (když okno není v popředí) a volitelně ntfy
  na telefon (jen název karty, výsledek a počty, nikdy cesty). Selhání kopie se oznámí hned, když kopie skončí.
  Nastavení → Aplikace → Aktualizace… — adresa manifestu (výchozí prázdná = kontrola vypnutá) a „Zkontrolovat teď“;
  nová verze se ukáže odkazem v horní liště. *Otevřít* otevře jen https adresu nebo místní složku / zip; síťové cesty (UNC, namapovaný síťový disk)
  se nikdy neotevírají, jen zobrazí k ručnímu zkopírování.

- **Karty:** karta vložená do čtečky se objeví sama a je zaškrtnutá; složku lze přidat tlačítkem „Ze složky…“ nebo přetažením
  složky na `Offload.exe`. Dlaždice ukáže kameru („ALEXA 35 · LogC4“ z prvního klipu, ffprobe nejvýš 5 s, jen čtení)
  a připojení („čtečka USB 3 · 5 Gb/s“ z HW diagnostiky; Thunderbolt Windows nerozliší). Název složky v cíli lze upravit;
  „Doplnit“ přepne kartu na doplnění nových klipů.
- **Cíle:** tlačítkem „+ Přidat disk“ (pamatují se; zobrazí „… volných z … · FS · připojení“ a pruh místa, u disku
  exFAT/FAT32 aplikace upozorní „Doporučeno NTFS“).
- **Proxy:** „Kopírovat proxy i do složky“ → „Vybrat…“: každá hotová proxy se kromě `_PROXY` na cílech zkopíruje
  i do `<složka>\<datum>\<karta>\` (nic se nepřepíše, shodné jméno dostane `_2`, `_3`…); křížek volbu zruší.
  Když složka není dostupná (odpojený disk), start nejde a pod krokem 3 i v Nastavení → Proxy je červeně „Složka pro
  proxy není dostupná“.
- **LUT:** v Nastavení → Proxy seznam „Automaticky podle kamery“ (výchozí) / vestavěné LUTy / „Bez LUT“ / „Vlastní…“ (`.cube`).
  „Když kameru nepoznám:“ je záložní LUT pro klipy, které automatika nepozná (výchozí Bez LUT). Automaticky se pozná:
  Sony podle sidecaru `M01.XML` (jen S-Log3 s S-Gamut3.Cine nebo S-Gamut3, jiná gama = bez LUTu), ARRI podle modelu
  kamery (ALEXA 35 → LogC4, ostatní ALEXA/AMIRA → LogC3), ARRIRAW podle metadat, Blackmagic RAW (BRAW) podle gamy klipu (Film Gen 5 → vestavěný LUT; Film starší generace = bez LUTu, s volbou „převést na Gen 5“ (Dekodéry; **neověřené**, čeká vzorek Gen 4) LUT Gen 5; Video, Extended Video a další gamy = bez LUTu; bez údaje o gamě se předpokládá Film Gen 5),
  RED → Log3G10. Canon, Panasonic, DJI a Fuji se zatím nepoznají (chybí vzorové klipy), D-Log M vestavěný LUT nemá —
  takové klipy dostanou záložní LUT. U ARRI jde o předpoklad pro ProRes: klip nahraný přímo v Rec.709 by LUT dostal
  neprávem, pak zvolte „Bez LUT“. Report ukáže u každého klipu použitý LUT a důvod. Na vzorcích z kamer se ještě
  ověří rozsah: R3D a ARRIRAW (přes ProRes/MXF mezisoubor) a ProRes z ALEXA 35 (LogC4) se berou jako legální video.
- **Spuštění:** tlačítko „▶ Spustit offload“ (s úlohou ve frontě „Zařadit do fronty“) nebo Ctrl+Enter; nad ním
  „1 karta → 2 disky · 1,70 GB“ a odhad času (jen z rychlostí naměřených v tomto spuštění; jinak jen velikost). Každá karta
  má v pravém sloupci kartu úlohy: ukazatel fází, velké procento, „zbývá …“, tabulka rychlostí (Čtení karty + každý cíl;
  bublina = zařízení a připojení), „Soubor 10 / 24“, Pozastavit / Pokračovat, Zrušit a „Nejpomalejší článek: …“.
  Karty se kopírují za sebou, proxy předchozí karty se renderují souběžně s kopií další.
- **Pozastavit:** kopie a ověření stojí mezi 16MB bloky, 2. fáze mezi klipy (ffmpeg se neuspává); čas pauzy se do
  rychlostí nepočítá (report `copyPausedSeconds` / `verifyPausedSeconds`); Zrušit a zavření aplikace fungují i během pauzy.
- **Po dokončení:** zelená karta „… — kartu lze vysunout“ se souhrnem Kopie · Ověření · Proxy · Celkem a náhledy,
  „Vysunout kartu“ (karta ve čtečce se vysune jako médium, čtečka zůstane; interní disky a složky se nevysouvají nikdy;
  vysune se jen ta karta, která se kopírovala — po výměně karty ve čtečce tlačítko zmizí), „Report PDF“, „Otevřít
  složku“, „Proxy“. Žlutá karta („… — kartu zatím nevysunujte“) vypíše nesouhlasné soubory po discích a nabídne
  **Zkopírovat znovu**: úloha „A001 · oprava“ zkopíruje jen ty soubory a jen na disky, kde nesouhlasí (dobrá kopie se jen
  znovu přečte, nesouhlasná se odloží do `_REPORT\neshoda_NNNN\`), jako další generace ASC MHL; jen s kartou ve čtečce.
  Zrušená úloha je šedá. Upozornění: zvuk a blikání tlačítka v hlavním panelu. Pod frontou „DNES“, záložka
  „Historie karet“ (z `history.json`).
- **Hardware:** kontrola USB portů se nespouští při startu, jen na vyžádání — tlačítko „Hardware“ v horní liště (Ctrl+U;
  tečka = výsledek posledního testu) nebo Nastavení → Aplikace → „Prověřit USB porty…“. Okno Hardware ukazuje připojení, doporučení, měření rychlosti
  (s potvrzením) a USB porty s vlastními názvy. Přepínač `--no-hw-prompt` se stále přijímá, ale nic nedělá.
- **Soubory:** nastavení `%APPDATA%\Offload\settings.json`, historie offloadů `history.json` + `history\` (seznamy souborů),
  log `%APPDATA%\Offload\app.log`, poslední test HW `hwtest.json`
  (složku lze přesměrovat proměnnou `OFFLOAD_SETTINGS_DIR`).

### Checklist ručního testu (s kartou)

Vše níže zatím nikdo neověřil na skutečné kartě — GUI je jinak zkoušené jen automaticky a na snímcích.
Od 0.5.0 jsou volby dřívějších sekcí „Struktura a kontrola“, „Proxy“ a „Dekodéry“ na stránce Nastavení (Ctrl+,), menu
„Nástroje“ nahradila horní lišta (Ověřit zálohu, Hardware) a Nastavení (Oznámení, Aktualizace, Denní report) — mapování
je v plánu 9 (`docs/superpowers/plans/2026-10-07-offload-windows-9-ui.md`).

- [ ] 1. Vložit kartu do čtečky → objeví se v „Karty“ a je zaškrtnutá
- [ ] 2. Přidat dva cíle na různých discích
- [ ] 3. Spustit (Ctrl+Enter) → průběh, tabulka rychlostí (bublina = zařízení a připojení) a „Nejpomalejší článek“
- [ ] 4. Kartu vysunujte až po zeleném pruhu: „Vysunout kartu“ → karta zmizí ze seznamu, čtečka zůstane; vložit další
      kartu → objeví se (skutečné vysunutí je zatím nevyzkoušené — nejdůležitější bod testu)
- [ ] 5. „Report“ otevře HTML report, PDF je vedle
- [ ] 6. Během kopie zavřít okno → dotaz → „Ano“ → aplikace se zavře, v cílech nezůstanou `.offload-part`.
      Soubory `.offload-part` jsou skryté: v Průzkumníku zapněte „Zobrazit → Skryté položky“, nebo použijte `dir /a /s`
      v cílové složce. Karta, jejíž kopie se zavřením přerušila, **není zálohovaná** — offloadujte ji znovu celou.
- [ ] 7. Okno Hardware (Ctrl+U): pojmenovat port, změřit rychlost — před měřením se aplikace zeptá; z karty se jen čte,
      na každý cílový disk se zapíše a hned smaže dočasný soubor 1 GB (během kopírování měření nejde spustit)
- [ ] 8. Proxy → „Kopírovat proxy i do složky“ → „Vybrat…“ (složka na jiném disku) → offload s proxy → proxy jsou
      v `_PROXY` na cílech i v `<složka>\<datum>\<karta>\`; po odpojení toho disku je start zablokovaný červeným textem
- [ ] 9. Proxy z log klipu má přirozené barvy; v reportu je u klipu LUT a důvod
- [ ] 10. BRAW klip: proxy, tři snímky v reportu, LUT podle gamy v reportu (viz „Vzorek BRAW“ výše)

Plán 6 (karty a bezpečnost dat, 0.2.0):

- [ ] 11. Zkopírovanou kartu vytáhnout a vložit znovu → pod kartou šedě „Zjišťuji, jestli už je karta zálohovaná…“,
      pak zeleně „Zálohováno <datum čas> na <cíle> — ověřeno“; Start → okno „Karta už je zálohovaná“: „Zrušit“ nic
      nespustí, „Vynechat zálohované“ kartu odškrtne a nic nezkopíruje, „Kopírovat znovu celé“ vytvoří `<karta>_2`
- [ ] 12. **Karta ve dvou čtečkách:** tutéž kartu vložit do jiné čtečky (jiné písmeno, jiný GUID svazku) → stále
      „Zálohováno …“ (párování podle sériového čísla FS + kapacity, R1); kartu naformátovat v kameře a vložit → žádný
      řádek historie (nová karta)
- [ ] 13. Rychlá výměna dvou karet ve stejné čtečce během „Zjišťuji…“ → řádek patří vždy kartě, která je ve čtečce
      (výsledek staré karty se zahodí), napsaný název zmizí
- [ ] 14. Kamerou natočit na zálohovanou kartu další klipy → oranžově „· nové soubory: N (… GB)“ a zaškrtávátko
      „Doplnit jen nové klipy do dřívější zálohy (N souborů · … GB)“; zaškrtnout → místo názvu „Složka: <datum>\<karta>
      na 2 cílech“, Start jde i s prázdnými Cíli a bez okna „Karta už je zálohovaná“; úloha „<karta> · doplnění“ →
      na obou cílech `_REPORT\offload_report_0002.json/.txt`, `<karta>_report_0002.html/.pdf`, `ascmhl\0002_…Z.mhl`,
      `ascmhl_chain.xml` se dvěma záznamy, `<karta>.xxh64` delší; původní `offload_report.json`, HTML/PDF a
      `0001_…mhl` beze změny (datum změny souboru); nové proxy v `_PROXY` bez přepsání starých; kartu vložit znovu →
      „Zálohováno … — ověřeno“ bez nových souborů; doplnit znovu z CLI `offload append E:\` → „Nic nového — záloha je
      úplná.“, kód 0
- [ ] 14b. Doplnění odmítnuté česky, nic se nezapíše: jeden z dřívějších cílů odpojit → zaškrtávátko se nenabídne
      (po ⟳); z CLI `offload append E:\` → „Chyba: Dřívější záloha není dostupná: … — připojte disk, nebo udělejte celou
      kopii.“; spustit doplnění v GUI a hned v PowerShellu `offload append E:\` téže karty → druhé skončí „Do této
      zálohy právě doplňuje jiná úloha (…) — počkejte, až skončí.“
- [ ] 15. **`ascmhl verify` po doplnění** (na macOS/Linuxu, kapitola „ASC MHL a ascmhl na Windows“): `ascmhl verify
      <cíl>/<datum>/<karta>` projde bez chyb přes obě generace (a `ascmhl info` ukáže generace 1 a 2); na Windows
      `offload verify <cíl>\<datum>\<karta>` → „… — vše v pořádku.“, kód 0
- [ ] 16. **Posun času na FAT32:** kartu naformátovanou ve FAT32 (např. malá SD) zkopírovat, pak ve Windows změnit
      časové pásmo o hodinu (Nastavení → Čas a jazyk; simuluje přechod letní/zimní čas) a kartu vložit znovu → stále
      „Zálohováno … — ověřeno“ (ne „karta se od té doby změnila“); přidat na kartu nový soubor a doplnit → projde bez
      konfliktu (žádné „jiná velikost nebo datum“); pásmo vrátit
- [ ] 16b. **Posun času na exFAT (karta z kamery):** totéž s kartou exFAT, na kterou **natáčela kamera** (karta
      naformátovaná a zapsaná ve Windows má u časů posun UTC, takže se neposune — ta test nic neověří). Zapsat, co se
      stane: a) „Zálohováno … — ověřeno“ = kamera zapisuje čas s posunem UTC, v pořádku; b) „…, ale karta se od té doby
      změnila (N souborů jinak) — doplnit nejde, jen celá kopie“ = kamera zapisuje místní čas bez posunu → nález M27:
      rozšířit toleranci celých hodin (R3) i na exFAT (`FileTimes.StoresLocalTime`, `AppendPlanner`). Kameru a model
      uvést v nálezu. Pásmo vrátit
- [ ] 17. Název z klipů: karta ARRI/Venice/RED/Blackmagic → šedě „A001“ + „podle názvů klipů (A001C001…)“; CLI
      `offload E:\ -d D:\Zaloha --name auto --dry-run` u téže karty vypíše „Název karty: A001 (podle klipů)“ a cestu
      `…\A001`; karta Sony XAVC (`C0001`) po kartě `A001` téhož dne → GUI „A002“ + „další v řadě po A001 (dnes)“
      (CLI `--name auto` „další v řadě“ neumí: vypíše „Návrh názvu nenalezen, použije se jmenovka svazku.“); **dvě
      neznámé karty najednou** (obě XAVC, po `A001`) → řádky ukážou „A002“ a „A003“ a po Startu vzniknou složky
      `A002` a `A003` (ne `A002_2`); volba „Navrhovat název karty podle klipů“ vypnutá → jmenovka svazku
- [ ] 18. Ověřit zálohu… (horní lišta) (Ctrl+Shift+O) na složku karty i na složku dne → položky „Ověření …“ ve frontě po
      kopiích, „Ověřeno: všech N souborů sedí (ASC MHL, 2 generace, xxHash64).“, tlačítko „Report ověření“ otevře
      `_REPORT\<karta>_verify_…Z.html`; ve zkušební kopii změnit bajt souboru → červeně „Nesedí 1 soubor, chybí 0,
      nečitelný 0 — podrobnosti v reportu ověření.“; během ověření odpojit disk → nečitelné soubory, „… disk se možná
      odpojil.“; „Zrušit“ → „Ověření zrušeno po …“ bez reportu; složka zálohy z jiného programu (MHL bez `_REPORT`
      v ignore) → poznámka „Report ověření je uložený mimo složku karty …“ a report v `<datum>\_REPORT\`
- [ ] 19. Místo: cíl s málo místem (např. malý USB disk) → červeně „Na cíli … není dost místa: potřeba … (karty …,
      proxy ~…, report …), volno …“ a Start šedý; dvě karty na tentýž disk se sečtou; zapnout proxy → potřeba vzroste;
      složka pro proxy na stejném disku jako cíl se přičte; během běžící kopie vybrat další kartu → počítá se i zbytek
      běžící úlohy; skoro plný disk → oranžově „… zbude po offloadu jen … — rezerva pod 5 %, odhad proxy je
      přibližný.“ (Start jde); uvolnit místo → červený text do pár sekund zmizí
- [ ] 19b. Start hned po vložení velké karty, kterou historie zná (řádek ještě „Zjišťuji…“) → pod lištou „Prověřuji
      kartu, než začnu kopírovat…“, pak okno „Karta už je zálohovaná“ (ne tichá celá kopie); pomalá karta, která se do
      10 s neprověří → otázka „Karta … se ještě prověřuje — nevím, jestli už je zálohovaná. Spustit přesto celou
      kopii?“, „Ne“ nic nespustí; zavřít okno během čekání → zavře se bez hlášky
- [ ] 20. Hlášky česky: odpojený cíl při startu z CLI (`offload E:\ -d X:\nic`) → „Chyba: Cíl nenalezen: X:\nic — je
      disk připojený?“; neznámá volba (`offload E:\ -d D:\ --xyz`) → chyba parseru česky (satelit `cs` v `bin\cs\`);
      v GUI chyba před kopií na kartě úlohy česky

Plán 7 (výstupy pro produkci a střih, 0.3.0):

- [ ] 21. **MD5:** Struktura a kontrola → Kontrolní součet „xxHash64 + MD5“ → pod volbou oranžové „MD5 je pomalejší …“,
      zaškrtávátko „Ověřit kopie zpětným čtením (xxHash64 + MD5)“; offload karty → `_REPORT\<karta>.md5`, v HTML dlaždice
      „XXH64 + MD5“ a sloupec md5; Ověřit zálohu… (horní lišta) → „… (ASC MHL, 1 generace, xxHash64 + MD5).“; **rychlá karta
      (CFexpress)**: změřit dobu kopie s xxHash64 a s MD5 (a `offload bench --hash md5`) a zapsat do „Naměřeno“
- [ ] 22. **Proxy v Resolve:** naimportovat originály, proxy z `_PROXY` připojit (Link Proxy Media) — sedí jméno, TC; sloupec
      Reel ukazuje cívku (u ARRI/RED/Blackmagic `A001`, jinak tag kamery nebo složku karty)
- [ ] 23. **Proxy v Premiere:** Sony MXF se 4–8 mono stopami → proxy se stejným počtem stop; „Attach Proxies“ projde;
      Tape Name = cívka. Dva klipy stejného jména → `_2` a u klipu poznámka „Proxy přejmenována na … — přelinkujte ji ručně.“
- [ ] 24. **CSV:** `_REPORT\<karta>_clips.csv` v Resolve (Import Metadata → Media Pool) doplní metadata; v Excelu přes
      Data → Z textu/CSV (oddělovač čárka) čeština a sloupce sedí
- [ ] 25. **ALE:** volba „Seznam klipů pro Avid (ALE)“ → `<karta>_clips.ale` jde importovat do Avid Media Composeru (Start,
      End, Tape, Tracks); karta se smíšenými fps (25 a 23.976) → dva soubory `_clips_25.ale`, `_clips_23.976.ale`
- [ ] 26. **Denní report:** dvě karty týž den na dva cíle → na obou `<datum>\_REPORT\<datum>_day_report.html/.pdf` s oběma
      kartami, dlaždice „ověřené karty 2 / 2“; během kopie třetí karty ji denní report ukáže „bez reportu (probíhá …)“;
      Denní report… (Nastavení → Report, nebo odkaz u DNES) na složku dne (i na složku karty) report vytvoří a otevře; na složku bez karty s reportem → česká chyba
      „… není žádná karta s reportem …“, na kořen disku „… je kořen disku …“; nic se nezapíše; GUI a `offload day-report` téhož dne zároveň → oba projdou
- [ ] 27. **Otevřené v Excelu:** denní CSV a `<karta>_clips.csv` otevřít v Excelu, pak doplnit kartu → úloha „Hotovo“,
      v `app.log` řádky „clip list …“ / „day report …“; po zavření Excelu Denní report… (Nastavení → Report, nebo odkaz u DNES) soubory obnoví;
      volba „Denní report po každé kartě“ vypnutá → po úloze se denní report nezmění
- [ ] 28. **Doplnění s MD5:** záloha s „xxHash64 + MD5“, pak doplnit kartu → `<karta>.md5` je delší o nové řádky (dřívější
      řádky beze změny; je to třetí soubor, který doplnění přepíše, vedle `.xxh64` a řetězu ascmhl), Ověřit zálohu projde
      s „xxHash64 + MD5“; ostatní dřívější soubory zálohy beze změny
- [ ] 29. **Karta v průběhu:** během kopie je ve složce dne `<karta>\_REPORT` (zatím prázdná) a denní report karta ukáže jako
      „bez reportu (probíhá, nebo byl přerušen)“; přerušená kopie zůstane tak označená; karta pojmenovaná `_REPORT`,
      `_PROXY` nebo `ascmhl` dostane na konec `_`
- [ ] 30. **ALE v Avidu:** diakritika v názvech klipů se v Avidu zobrazí správně (ALE je UTF-8 bez BOM; starší Avid ji
      může číst špatně — poznamenat verzi)

Plán 8 (nastavení, oznámení, distribuce, 0.4.0):

- [ ] 31. **Projekty:** „Uložit jako projekt…“ „Film X“ → titulek „Offload — Film X“; změnit cíle a proxy, přepnout na „Bez
      projektu“ a zpět — obojí má svoje hodnoty; přejmenovat a smazat (dotaz „Smazat projekt …? … zálohy na discích
      zůstanou.“, po smazání „Bez projektu“); jméno prázdné / „bez projektu“ / už použité → červeně pod polem, OK nejde;
      stisknout „Spustit offload“ a hned (během kontrol) zkusit přepínač a menu Projekt → jsou zamčené
- [ ] 32. **Přepnutí s frontou:** dvě karty ve frontě z „Film X“ (s proxy) → během renderu proxy přepnout na jiný projekt
      s jinými cíli a kodekem → úlohy dokončí cíle, kodek, LUT i oznámení „Film X“; report a `history.json` mají `project`
      „Film X“; další v řadě (A002 …) se počítá zvlášť v každém projektu; denní report dne jen z „Film X“ má v hlavičce
      „Projekt: Film X“. **Doplnění:** karta zálohovaná ve „Film X“ a doplněná při jiném zobrazeném projektu používá
      nastavení „Film X“ (proxy, výstupy, ntfy téma), report si ponechá „Film X“; po smazání „Film X“ doplnění běží
      s „Bez projektu“ a karta úlohy to napíše („… už neexistuje — doplnění běží s nastavením Bez projektu“)
- [ ] 33. **Starší / ručně upravený settings.json:** dva projekty stejného jména → platí první, druhý po uložení zůstane
      v souboru; `projects` jako text → aplikace naběhne bez projektů, ostatní nastavení zůstane; spustit 0.3.0 a zase
      0.4.0 → projekty a pravidla zůstaly
- [ ] 34. **Pravidla LUTů:** Sony FX6 (`M01.XML` se `serialNo`) → pravidlo „Sériové číslo kamery“ → LUT a v reportu
      „pravidlo 1: sériové číslo kamery „…““; pravidlo „Složka“ s vlastním `.cube`, pak `.cube` přejmenovat → červeně „LUT
      z pravidla 1 nenalezen: …“, start nejde; ↑ ↓ ✕ mění pořadí / maže; tlačítko jen u „Automaticky podle kamery“;
      cíl „Bez LUT“ → klip bez LUTu. **Pravidlo z budoucnosti:** v `settings.json` přidat do `proxy.lutRules` pravidlo
      `{ "match": "lens", "value": "35mm", "lut": "none" }` (mezi dvě běžná) → editor ho ukáže jen ke čtení
      „Pravidlo z novější verze (lens) …“, ↑ ↓ ✕ fungují, po OK zůstalo v souboru beze změny, čísla pravidel v reportu
      počítají i s ním; neplatné známé pravidlo (prázdná hodnota) se při načtení zahodí
- [ ] 35. **BRAW Gen 4 (až bude vzorek; do té doby neověřeno):** `OFFLOAD_BRAW_SAMPLE=<klip Gen 4>` → `dotnet test
      --filter BrawSampleTests` (Gen5_ConvertsAGen4Clip_LeavesGen5); v GUI Dekodéry → „Blackmagic RAW Film Gen 4 převést na
      Gen 5“ → proxy má LUT a vypadá jako v Blackmagic RAW Playeru s Gen 5; v reportu „Blackmagic RAW: Film Gen 4 → Gen 5
      (převod SDK)“; klip Video / Extended Video Gen 4 se nepřevede. Teprve pak zvážit zapnutí jako výchozí (R26)
- [ ] 36. **Oznámení Windows (TrayNotifier, jen ručně):** okno na pozadí → po kopii „Offload: A001 — kartu lze vysunout“
      (s proxy), po konci „Offload: A001 hotovo“; s chybou červená ikona; selhaná kopie ohlásí „CHYBA“ hned po konci
      kopie; kliknutí na oznámení / ikonu vrátí okno; okno v popředí → žádné oznámení, jen zvuk a blikání; oznámení zůstane
      v Centru oznámení; po zavření aplikace ikona zmizí; restart Průzkumníka → další oznámení ikonu přidá znovu;
      Nastavení → Oznámení… → „Oznámení Windows“ vypnout → nic
- [ ] 37. **ntfy:** téma `offload-<náhodné>` → „Poslat zkušební oznámení“ → „Odesláno.“ a zpráva v aplikaci ntfy na
      telefonu; server `https://nic.invalid` → „Nepodařilo se: …“ do 10 s, aplikace celou dobu reaguje; téma s diakritikou
      → OK nejde, česky proč; v textu na telefonu žádná cesta ani jméno PC (jen název karty, výsledek, počty); CLI
      `--ntfy https://ntfy.sh/<téma>` (po konci čeká nejvýš 11 s, i s nedostupným serverem)
- [ ] 38. **Aktualizace:** `build.ps1 -ReleaseUrl \\NAS\Offload\` → `offload-update.json`; Nastavení → Aplikace → Aktualizace… → cesta
      k manifestu → „Zkontrolovat teď“ → „Je k dispozici verze …“ / „Máte nejnovější verzi …“, odkaz „Nová verze …“ vpravo
      v menu; s `https://` adresou v manifestu *Otevřít* otevře prohlížeč, s místní složkou nebo zipem Průzkumník
      s výběrem; **adresa na NAS (`\\NAS\…`) nebo namapovaný síťový disk → bez tlačítka, jen text a „Síťová cesta —
      zkopírujte ji a otevřete ručně.“**; odpojený NAS → „Manifest nejde načíst: … neodpověděl do 5 s“ a okno nezamrzne;
      prázdná adresa → žádná kontrola; po 24 h se čte znovu
- [ ] 39. **Podpis:** (a) stroj s Windows SDK: `build.ps1 -Sign` bez signtoolu / s neznámým otiskem → česká chyba hned na
      začátku; testovací PFX (`New-SelfSignedCertificate -CodeSigningCert`, export) přes `OFFLOAD_SIGN_PFX` → podepíše tři
      exe, ve vlastnostech souboru je karta Digitální podpisy; nedostupné razítko (`-TimestampUrl http://nic.invalid`) →
      3 pokusy, „Časové razítko se nepodařilo získat …“; heslo se nikde nevypíše. (b) **skutečný certifikát (až bude
      koupený, viz Podepisování):** `build.ps1 -Sign -CertThumbprint …` → `signtool verify /pa` projde, SmartScreen na
      čistém počítači po stažení zipu ukáže vydavatele (reputace se buduje časem)
- [ ] 40. **Vzhled (0.5.0):** Windows v tmavém režimu → tmavé okno jako `docs/ui-navrh/screeny`; přepnout Režim aplikací na
      světlý za běhu → okno se přebarví bez restartu, všechny texty čitelné (karty úloh, žlutá / červená hlášení, Nastavení);
      dialogy (Hardware, Oznámení, Karty už jsou zálohované) jsou ve stejném tématu; okno 1000 px široké → nic se neořízne
- [ ] 41. **Dlaždice karty:** karta z ALEXA 35 → „ALEXA 35 · LogC4“, z FX6 → „FX6 · S-Log3“, BRAW → „Blackmagic RAW“;
      čtečka USB 3 → „čtečka USB 3 · 5 Gb/s“, SD čtečka v notebooku → „SD čtečka“, CFexpress přes Thunderbolt → „NVMe“;
      karta s novými klipy → žluté „Doplnit“ (bublina = počet a velikost), po stisku „Složka: … na 2 cílech“
- [ ] 42. **Pozastavit při kopii:** karta 50+ GB na 2 disky → Pozastavit během kopie → „Pozastavuje se…“, do pár sekund
      „Pozastaveno“, rychlosti „—“, Správce úloh ukáže 0 MB/s na kartě i discích; po 2 min Pokračovat → kopie doběhne,
      „kartu lze vysunout“; v reportu „pozastaveno · mimo rychlosti“ a rychlost kopie bez pauzy; totéž během ověření
- [ ] 43. **Pozastavit ve 2. fázi:** během proxy → „Pozastaví se po proxy klipu …“, ffmpeg po doběhnutí klipu zmizí ze
      Správce úloh a nic se neobnoví; po 3 min Pokračovat → proxy pokračují (žádné „ffmpeg se zasekl“); během pauzy jiná
      karta ve frontě čeká; denní report se mezitím vytvořit dá (Denní report… u DNES)
- [ ] 44. **Pauza a výpadek:** pozastavit, vytáhnout druhý cílový disk, Pokračovat → soubory na něm selžou, první disk
      dokončí, karta žlutá „… nesouhlasí“ s „✕ zápis“; pozastavit a zavřít aplikaci → dotaz, po Ano se okno zavře do pár
      sekund a na discích nezůstane `.offload-part`; pozastavené doplnění + druhé okno se stejným doplněním → „Do této
      zálohy právě doplňuje jiná úloha …“
- [ ] 45. **Zkopírovat znovu:** po bodu 44 připojit disk → Zkopírovat znovu → úloha „A001 · oprava“ zkopíruje jen selhané
      soubory na druhý disk, první disk se jen přečte (Správce úloh: zápis jen na druhý); zelená karta; ve složce karty
      `_REPORT\offload_report_0002.json` s `"kind": "repair"`, `ascmhl\0002_…mhl` na obou discích (`verified` na prvním,
      `original` na druhém), řádek u karty zase „— ověřeno“; `offload verify` a `ascmhl verify` (na macOS) projdou.
      Ručně poškodit jeden soubor na disku (hex editor, stejná velikost) → Ověřit zálohu → nesedí; nová kopie → žlutá →
      Zkopírovat znovu → poškozený soubor je v `_REPORT\neshoda_0003\…`, nový sedí
- [ ] 46. **Zkopírovat znovu odmítne:** vyměnit kartu ve čtečce → tlačítko zmizí; zformátovat a nahrát jiný klip stejného
      jména → „Oprava zastavena — nic se nezkopírovalo …“, na discích se nic nezměnilo
- [ ] 47. **Odhad a dnešek:** první karta po spuštění → lišta jen „1 karta → 2 disky · … GB“; po první dokončené kartě
      (od 200 MB) druhá karta stejného projektu → „odhad ~… vč. proxy“, po dokončení porovnat se skutečností (±30 %);
      DNES ukazuje obě karty, Historie karet včerejší karty pod „Včera“, „Denní report…“ otevře report dne
- [ ] 48. **Pozastavená úloha drží PC vzhůru (M6) a frontu:** nastavit v systému uspání po 1–2 min nečinnosti, spustit dvě karty,
      první pozastavit → druhá čeká („Čeká na pozastavenou úlohu.“), PC po uplynutí doby neusne (`powercfg /requests`
      ukáže Offload u SYSTEM) a disky zůstanou připojené; Pokračovat → první doběhne, pak se rozběhne druhá; Pozastavit a
      Zrušit → po zrušení `powercfg /requests` Offload už neukazuje
- [ ] 49. **Ukázkový režim (`--demo`):** `Offload.exe --demo pripraveno` / `probiha` / `hotovo` / `nastaveni` ukáže čtyři
      obrazovky z `docs/ui-navrh/screeny` (01–04), srovnat se snímky v tmavém i světlém tématu (`OFFLOAD_THEME`) a v úzkém okně
      (`Resize-Offload $p 1000 900`); Spustit, Doplnit, Zkopírovat znovu, Vysunout a Ověřit zálohu jsou vypnuté, v `%APPDATA%\Offload`
      se nic nezměnilo (nastavení, historie ani log), složka dat je `%TEMP%\Offload-demo-…`; bez `--demo` se okno chová jako dřív

Návrh: `docs/superpowers/specs/`, plány: `docs/superpowers/plans/`.

## Použití

```powershell
offload E:\ -d D:\Zaloha -d F:\Zaloha          # karta E: na dva disky, ověření, report
offload verify D:\Zaloha\2026-10-04\A001        # znovu ověřit hotovou kopii
offload bench --read E:\ --write D:\ --gb 2     # rychlost karty a disků
offload ports                                 # porty, kde a jak rychle jsou disky připojené, doporučení
offload ports --measure --card E:\ --dest D:\   # + změřit skutečnou rychlost
offload E:\ -d D:\Zaloha -d F:\Zaloha --proxy --lut look.cube --frame-aspect 1.85   # + proxy s LUT a výřezem
offload E:\ -d D:\Zaloha --proxy --proxy-folder G:\Proxy   # proxy navíc do G:\Proxy\<datum>\<karta>\
offload E:\ -d D:\Zaloha --proxy --lut sony-slog3-sgamut3   # vestavěný LUT podle id
offload luts                                                # seznam vestavěných LUTů (id — název)
offload E:\ -d D:\Zaloha -d F:\Zaloha --name auto            # název složky z názvů klipů (A001C003… → A001)
offload append E:\                                          # doplnit jen nové klipy do dřívější zálohy (cíle z historie)
offload append E:\ --into D:\Zaloha\2026-10-06\A001 --into F:\Zaloha\2026-10-06\A001
offload verify D:\Zaloha\2026-10-06 --report                # ověřit všechny karty dne proti ASC MHL + report ověření
offload E:\ -d D:\Zaloha -d F:\Zaloha --hash both --ale     # xxHash64 + MD5, seznam klipů i pro Avid
offload day-report D:\Zaloha\2026-10-06                     # denní report znovu (HTML, PDF, seznam klipů dne)
offload bench --hash md5                                    # rychlost MD5 proti xxh64 (CPU)
offload E:\ --project "Film X"                              # nastavení projektu z aplikace; volby z příkazové řádky mají přednost
offload E:\ -d D:\Zaloha --proxy --lut-rule camera=FX6:sony-slog3-sgamut3cine --lut-rule "folder=A00*/*:C:\luts\show.cube"
offload E:\ -d D:\Zaloha --proxy --braw-gen5                 # BRAW Film Gen 4 jako Gen 5 (neověřeno, čeká vzorek Gen 4)
offload E:\ -d D:\Zaloha --ntfy https://ntfy.sh/muj-tajny-kanal   # výsledek na telefon (jen název karty, výsledek, počty)
```

Plán 8: `--project <název>` vezme projekt uložený v aplikaci (`settings.json`; cíle, ověření, součet, výstupy, oznámení,
proxy) jako výchozí hodnoty — co je napsané na příkazové řádce, má přednost (`-d` nahradí cíle projektu, `--lut-rule`
jeho pravidla); projekt, který navrhuje název z klipů, se chová jako `--name auto`; neznámý projekt → „Chyba: Projekt
„X“ neexistuje. Projekty: …“ (kód 1); `--project "Bez projektu"` vezme nastavení bez projektu. U `append` dává
`--project` jen nastavení — projekt zálohy zůstane podle dřívějšího reportu.
`--lut-rule <camera|serial|reel|folder>=<hodnota>:<lut id|none|cesta.cube>` (opakovatelné, pořadí = priorita; hodnota
nesmí obsahovat `:`; platí jen s `--lut auto` — s jiným `--lut` je to chyba; chybějící `.cube` je chyba, i s `--dry-run`).
`--ntfy <url>` pošle výsledek (i odmítnutí před kopií) na ntfy, po konci čeká nejvýš 11 s (10 s odeslání + 1 s rezerva; pak „ntfy: nečekám déle“);
oznámení Windows CLI neposílá.

Výstupy (plán 7): po každé kopii nebo doplnění vznikne `_REPORT\<karta>_clips.csv` (`--no-clip-list` vypne, `--ale`
přidá `<karta>_clips.ale` pro Avid) a na každém cíli znovu denní report `<datum>\_REPORT\<datum>_day_report.html/.pdf`
se seznamem klipů dne (`--no-day-report` vypne); CLI vypíše „Denní report: …“. `--hash both` počítá navíc MD5
(`<karta>.md5`, MD5 v reportu a ASC MHL); `append` volbu nemá — doplnění počítá součty dřívější zálohy. `day-report
<složka>` vytvoří denní report znovu (složka dne, nebo karty v ní), kód 0/1.

Historie (`%APPDATA%\Offload\history.json`): `copy` před plánem vypíše „Zálohováno … — ověřeno“, když kartu zná (bez
dotazu), a po dokončené kopii ji zapíše; `--no-history` čtení i zápis vypne. `append` bez `--into` najde složky zálohy
v historii (jinak „Chyba: Karta není v historii — zadejte složky zálohy volbou --into.“; s `--no-history` je `--into`
povinné); kód 0 = doplněno, nebo „Nic nového — záloha je úplná.“, kód 1 = odmítnuto (jiná karta, změněné soubory,
nedostupný dřívější cíl, běží jiné doplnění téže zálohy …, česky s důvodem). `--dry-run` plán jen vypíše (běh plánuje
znovu pod zámkem složky karty). `--name auto` vezme cívku z názvů klipů, jinak jmenovku svazku (bez „další v řadě“,
to umí jen GUI). `verify` čte ASC MHL všech generací (bez `ascmhl\` seznam `_REPORT\*.xxh64`), řádky problémů zůstávají
strojové (`MISMATCH`, `MISSING`, `ERROR`, `EXTRA`, `FAILED-AT-COPY`, `FAILED-SUPERSEDED`, `CHAIN`), souhrn je česky, kód
1 při jakémkoli problému; složka dne ověří každou kartu; `--report` zapíše report ověření do `_REPORT` karty (u MHL,
které `_REPORT` neignoruje, vedle složky karty). Chyby a souhrny CLI jsou česky, průběh a `--help` anglicky (nadpisy
nápovědy a chyby parseru česky tam, kde je System.CommandLine 2.0.12 přeložené).

`--lut auto|none|<id>|<cesta.cube>` (výchozí `auto`): `auto` vybere LUT podle kamery každého klipu, `none` žádný,
`<id>` vestavěný LUT, cesta s `.cube` nebo lomítkem vlastní LUT. Seznam id vypíše `offload luts` — samostatný příkaz,
protože `copy` vyžaduje kartu a cíl, takže `copy --lut list` by neprošlo parsováním. `--lut-fallback <id|none>` je LUT
pro klipy, které `auto` nepozná (výchozí `none`). Neznámé id skončí chybou (exit 1). Vestavěné LUTy jsou v balíčku
ve složce `luts\` vedle `Offload.exe` (11 souborů `.cube` + `luts.json`); chybějící složka nic neshodí, `auto` pak
LUT nepoužije.

`--proxy-folder <složka>` (jen s `--proxy`): každá hotová proxy se kromě `_PROXY` na cílech zkopíruje i do
`<složka>\<datum>\<karta>\` (stejný název souboru; když už tam soubor je, uloží se jako `_2`, `_3`… — nic se nepřepíše).
Neexistující složka zastaví start chybou (exit 1); když složka zmizí během úlohy (odpojený disk), proxy zůstanou na cílech
a u klipu je v reportu poznámka „kopie do … selhala“.

`offload ports` pojmenuje každý USB konektor jedním popiskem, který platí všude (řádek disku, seznam portů,
doporučení): „port 6“ (číslo USB 2 poloviny konektoru), port za hubem „port 7 › 2“, při více řadičích s řadičem
„Intel USB 3.20 xHCI · port 3“. U portu je písmeno disku, který v něm je („← E:“). Cíl na stejném svazku jako karta
se neměří (na kartu se nikdy nezapisuje); Ctrl+C při `--measure` dokončí měření aktuálního zařízení a smaže jeho
dočasný soubor.

Stavový řádek `offload copy` ukazuje, který článek brzdí („brzdí: D1“, „brzdí: card“, „(linka plná)“) — podle toho,
kolik bloků čeká na zápis na každý disk, ne podle okamžité rychlosti (karta i disky jedou v kopii stejným tempem).
Seznam „Připojení:“ nad ním ukazuje, co je co („D1 = Cíl 1: WD Elements 25A2 — USB 3 (5 Gb/s), port 6“).

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
- [ ] Ctrl+C během kopie a během ověření — nezůstane žádný `.offload-part`, report říká „neověřeno“
- [ ] vytažení karty uprostřed kopie
- [ ] vytažení cílového disku uprostřed kopie
- [ ] naměřené rychlosti zapsat do „Naměřeno“

## Naměřeno

(doplnit po ostrém testu: karta, čtečka, disky, MB/s)

Každá změna Windows verze přidá záznam do `../docs/VYVOJ-WINDOWS.md` ve stejném commitu.
