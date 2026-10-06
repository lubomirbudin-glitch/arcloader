# Offload pro Windows — 15 vylepšení (návrh)

Datum: 2026-10-06 · Stav: ke schválení · Navazuje na [návrh Windows verze](2026-10-04-offload-windows-design.md)
a [návrh LUTů](2026-10-05-offload-luts-design.md) · Plány 6 (skupina A), 7 (B), 8 (C)

## 1. Cíl a rámec

Uživatel (DIT / produkce) chce všech 15 vylepšení ze seznamu („udělej všechno“). Tento návrh popisuje každé z nich
nad **současným kódem** (`windows/src`, commit `a5d5ee0`, 558 testů), rozděluje je do tří plánů a říká, co musí
později převzít Mac.

### 1.1 Globální omezení (platí pro všechny funkce)

| Omezení | Co to znamená v návrhu |
|---|---|
| Žádný nový NuGet balíček | jen BCL .NET 10 (`System.Security.Cryptography.MD5`, `System.IO.Compression.GZipStream`, `System.Net.Http.HttpClient`, `System.Net.HttpListener` v testech, `System.IO.Hashing` už je), P/Invoke |
| Texty UI česky | všechny nové hlášky níže doslova; identifikátory v kódu anglicky |
| UI vlákno nikdy nečeká na disk ani síť | historie, otisk karty, kontrola místa, ověření, denní report, ntfy, kontrola aktualizací — vše `Task.Run` + výsledek přes dispatcher, jako dnes `RefreshLut` / `DestinationViewModel.Probe` |
| Na kartu se nikdy nezapisuje | otisk karty, návrh názvu i doplnění kartu jen čtou (výčet složek, `FileReader`) |
| Formáty na disku čitelné Macem | nové soubory a klíče jsou jen **přidané** (Swift `Codable` neznámé klíče ignoruje); každý je níže popsaný na úrovni `docs/FORMATY.md` |
| Testovatelné bez kamer | fixtures (dočasné složky, syntetické klipy z ffmpeg `testsrc2`, ručně psané MHL/XML/JSON), falešný HTTP server (`HttpListener`), falešné hodiny |
| Mac později | u každé funkce odstavec „Mac“; souhrn v §8 pro `docs/VYVOJ-MAC.md`. Mac práce mimo rozsah |

### 1.2 Co už existuje (navrhuje se jen mezera)

| Oblast | Dnes v kódu | Mezera |
|---|---|---|
| Kontrola volného místa | `OffloadEngine.Prepare`: na každém cíli `totalBytes + files × 64 KiB + 64 MiB`, anglická chyba, kontroluje se až když úloha začne kopírovat | proxy, náhledy, víc karet ve frontě, složka pro proxy, kontrola **před** startem v GUI, varování < 5 %, kontrola před renderem proxy |
| Identita karty | `CardIdentity.Of` (`Desktop/QueueRules.cs`): `GUID svazku|sériové číslo FS`; `VolumeItem.Identity`, `Volumes.NamesToDrop` | historie podle ní (viz rozhodnutí v §2.1) |
| Ověření zálohy | CLI `offload verify` (`Verifier.cs`): jen seznam `_REPORT/*.xxh64`, výstup anglicky, bez reportu | ASC MHL (všechny generace, všechny typy hashů), GUI, report ověření, česky |
| ASC MHL | `Mhl.Write`: jedna generace `0001_…`, řetěz s jednou položkou, jen `xxh64` | další generace (doplnění), `md5`, čtení |
| Proxy pro přelinkování | jméno proxy = jméno klipu (`OffloadJob.AnalyzeAndRender`, při shodě `_2`), start TC přes `-timecode` (`ProxyRenderer.RenderOnce`), **burn-in názvu a TC už existuje** (`FilterGraph.BurnIn`, `ProxySettings.BurnFilename/BurnTimecode`, výchozí zapnuto) | reel name (tag QuickTime), všechny zvukové stopy (Premiere „Attach Proxies“ vyžaduje stejné stopy), výchozí hodnota burn-inu |
| Upozornění | zvuk + blikání v hlavním panelu (`MainViewModel.Queue.OnCopyFinished/OnJobFinished`, `NativeMethods.Flash`); toast zamítnut (spec 8.5) | oznámení Windows bez registrace, ntfy |
| BRAW Film Gen 4 | `braw-decode --info` vrací `colorScienceGen`; `LutResolver.Braw`: Gen 4 → bez LUTu („provizorně“) | převod Gen 4 → Gen 5 v SDK |
| Nastavení | `SettingsStore` (po sekcích, atomický zápis, `OFFLOAD_SETTINGS_DIR`), `AppSettings` | nové klíče, projekty, **zachování neznámých klíčů** (viz §5.4) |
| Verze, build | `OffloadVersion.String = "0.1.0-win"`, `Directory.Build.props` `0.1.0`; `build.ps1` → `Offload-win-x64.zip`, bez podpisu | číslování, manifest, `-Sign` |
| Výchozí název karty | `Layout.DefaultCardName` (jmenovka svazku / název složky) | návrh z názvů klipů |

### 1.3 Souhrn rozhodnutí (podrobnosti a „proč“ u funkcí)

| # | Rozhodnutí | Proč (jedna věta) |
|---|---|---|
| R1 | Historie páruje kartu podle **sériového čísla FS + kapacity**, GUID svazku se ukládá, ale nepožaduje | GUID svazku dává Windows podle čtečky/slotu — tatáž karta v jiné čtečce by jinak byla „nová“ |
| R2 | Otisk karty = dva SHA-256: `fingerprint` (cesta+velikost+mtime) a `shape` (cesta+velikost); seznam souborů v gz souboru vedle historie | rychlé „beze změny“ bez načítání seznamu; seznam jen když se karta změnila |
| R3 | mtime na FAT/FAT32 kartě se porovnává s tolerancí celých hodin (±14 h, shodné minuty a sekundy) | FAT ukládá místní čas — po přechodu letní/zimní čas by se karta tvářila jako změněná |
| R4 | Doplnění bere jako pravdu **obsah cílové složky** (reporty všech generací + soubory na disku), historie jen najde složky | funguje i po zrušené úloze, na jiném PC a pro zálohu z Macu |
| R5 | Doplnění zapíše novou generaci ASC MHL jen s novými soubory (`action="original"`, `process` `transfer`) a rozšíří `<karta>.xxh64` | `ascmhl verify` pokryje vše přes řetěz; starší `offload verify` (Win i Mac) čte jen jeden `.xxh64` |
| R6 | Report doplnění: `_REPORT/offload_report_NNNN.json/.txt`, `<karta>_report_NNNN.html/.pdf` (NNNN = generace MHL); původní soubory se nemění | jednoznačná vazba report ↔ generace, nic se nepřepisuje |
| R7 | Doplnění vyžaduje dostupné **všechny** dřívější cíle a shodný počet generací MHL | jinak by se kopie rozešly (viz otevřená otázka O1) |
| R8 | Ověření zálohy **nezapisuje do `ascmhl/`**; výsledek jde do `_REPORT/<karta>_verify_<stamp>Z.html/.pdf` + `offload_verify_<stamp>Z.json` | `ascmhl verify` je také jen čtení; `_REPORT` je v ignore vzorech MHL, takže report ověření nerozbije další `ascmhl verify` (soubor vedle `_REPORT` by byl „nový nezaznamenaný soubor“) |
| R9 | Ověření umí všechny hashe ASC MHL, které má BCL: `xxh64`, `md5`, `sha1`, `xxh3`, `xxh128`, `c4` | stejný kód ověří i zálohy ze Silverstacku/ShotPutu; náklad je jen tabulka |
| R10 | Odhad proxy před kopií heuristikou (bez čtení klipů z karty), přesný přepočet z délky klipů **před renderem proxy** | sondovat stovky klipů na pomalé kartě před kopií by trvalo minuty |
| R11 | Varování „< 5 %“ = volno po offloadu menší než 5 % potřebného místa | rezerva má krýt nepřesnost odhadu proxy, ne velikost disku |
| R12 | Denní report v `<cíl>\<datum>\_REPORT\` (otevřená otázka O4) | stejná konvence jako u karty, Mac `UniqueCardFolder` ani `ascmhl` (pracuje po kartách) nekoliduje |
| R13 | Seznam klipů: CSV zapnuto, ALE vypnuto; hlavička anglicky (názvy polí Resolve) | CSV čte Resolve i Excel; ALE potřebují jen uživatelé Avidu |
| R14 | Proxy dostanou **všechny** zvukové stopy zdroje | Premiere připojí proxy jen se shodným počtem stop |
| R15 | Burn-in beze změny: výchozí zůstává **zapnuto** jako dnes, volby i `--no-burn-in` beze změny (rozhodnutí řídicí relace 2026-10-06) | nic se nemění tomu, kdo burn-in používá |
| R16 | MD5 jen **navíc** k xxh64: volba `xxh64 | both` (O3 rozhodnuto řídicí relací 2026-10-06) | každou zálohu dál ověří Mac i starší `offload verify`; MD5 pro produkce/pojišťovny je k dispozici vždy s xxh64 |
| R17 | Projekty: horní klíče `settings.json` = profil „Bez projektu“, pojmenované projekty v `projects[]`, `activeProject` | přepnutí tam a zpět nic neztratí; fronta drží klon nastavení |
| R18 | Pravidla LUTů platí jen v režimu „Automaticky podle kamery“, jedna podmínka na pravidlo | jiné režimy jsou výslovná globální volba; jednoduchý seznam v GUI |
| R19 | Oznámení Windows přes **`Shell_NotifyIconW` (P/Invoke)**, ne WinRT toast ani WinForms | WinRT toast u nebalené aplikace potřebuje zástupce s AUMID v nabídce Start (instalace) a změnu TFM s projekcí Windows SDK; WinForms by přidal kolize jmen s WPF |
| R20 | ntfy: publikace jako JSON na `<server>/`, text = název karty + výsledek, bez cest | HTTP hlavičky neunesou češtinu; soukromí |
| R21 | Aktualizace: manifest z URL nebo cesty, nejvýš 1× za 24 h, nic nestahuje | repozitář je soukromý |
| R22 | Verze SemVer, plán 6 → 0.2.0, plán 7 → 0.3.0, plán 8 → 0.4.0, opravy patch, 1.0.0 po ostrém E2E | jednoduché, porovnatelné v manifestu |
| R23 | Podepisují se jen naše exe (`Offload.exe`, `bin\offload.exe`, `bin\braw-decode.exe`), ne ffmpeg | cizí binárky nepodepisujeme |
| R24 | `SettingsStore` od plánu 6 **zachová neznámé klíče** při uložení | návrat na starší verzi nesmí smazat projekty a pravidla |
| R25 | Seznam klipů a denní report jsou odvozené soubory — přepisují se celé (tmp + přejmenování) | nejsou záznamem kopie, ten je v MHL a reportu karty |
| R26 | Převod BRAW Gen 4 → Gen 5 výchozí **vypnuto**, dokud ho neověří vzorek | neověřené chování SDK nesmí měnit vzhled proxy potichu |
| R27 | CLI zapisuje historii také (vypnout `--no-history`) | karta zkopírovaná z CLI má být v GUI poznaná |

## 2. Skupina A — karty a bezpečnost dat (plán 6)

### 2.1 Karta už zálohovaná (funkce 1)

**Cíl.** Po vložení karty hned vidět, jestli už je zálohovaná, kdy, kam a jestli ověřeně; nezkopírovat ji omylem
podruhé celou.

**UX.** Řádek karty v sekci *Karty* (`SetupPanel.xaml`, `SourceItemViewModel`) dostane pod `Detail` řádek historie:

| Stav | Text | Barva |
|---|---|---|
| zjišťuje se | „Zjišťuji, jestli už je karta zálohovaná…“ | šedá (`Caption`) |
| beze změny, ověřeno | „Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha2 (F:) — ověřeno“ | zelená |
| přibyly soubory | „Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha2 (F:) — ověřeno · nové soubory: 12 (3,2 GB)“ | oranžová |
| neověřeno | „Zálohováno … — NEOVĚŘENO“ | oranžová |
| s chybami | „Zálohováno … — s chybami (3 soubory)“ | červená |
| karta se změnila | „Zálohováno …, ale karta se od té doby změnila (4 soubory jinak) — doplnit nejde, jen celá kopie“ | oranžová |
| neznámá | nic | — |

Datum a čas = `finished` posledního záznamu v místním čase, formát `d. M. yyyy H:mm`. Disky = název cílové
složky + písmeno, jako `DestinationViewModel.Name`. Víc záznamů téže karty: zobrazí se poslední (nejnovější `finished`).

**Varování před celou kopií.** `MainViewModel.Start` po kontrole LUTu a složky pro proxy: pokud má některá vybraná
karta stav „zálohováno“ (kromě karet se zaškrtnutým doplněním, §2.2), otevře se okno `BackedUpPromptWindow`:

> **Karty už jsou zálohované**
> • A001 — 6. 10. 2026 8:15, Zaloha (D:), Zaloha2 (F:), ověřeno
> • A002 — 6. 10. 2026 9:02, Zaloha (D:), Zaloha2 (F:), ověřeno · nové soubory: 12
>
> Zkopírovat je znovu celé? Vznikne nová složka (A001_2).
>
> [Kopírovat znovu celé] [Vynechat zálohované] [Zrušit]

„Vynechat zálohované“ spustí jen ostatní karty; „Zrušit“ nespustí nic. Okno je vlastní WPF (`MessageBox` neumí
tři vlastní tlačítka); vlastník = hlavní okno.

**Data.** `%APPDATA%\Offload\history.json` + `%APPDATA%\Offload\history\<id>.files.gz` — formát v §5.2.

**Jádro.** Nový jmenný prostor `Offload.Core.History`:

- `CardFingerprint.Compute(SourceScan) → (string Fingerprint, string Shape)`: SHA-256 (hex, malá písmena) přes
  seřazené řádky `relpath\tsize\tmtimeTicksUtc\n` resp. `relpath\tsize\n` (UTF-8, `SourceScan.Files` už jsou
  seřazené ordinálně).
- `FileListStore.Write(path, files)` / `Read(path)`: gz, řádky `relpath\tsize\tmtimeTicksUtc\n`.
- `HistoryStore`: `Load()`, `Record(HistoryRecord)`, `FindForCard(CardKey, fingerprint, shape)`. Čtení-úprava-zápis
  pod pojmenovaným mutexem `Local\Offload-history` (GUI a CLI mohou běžet zároveň), zápis tmp + `NativeIO.Move(replace)`
  jako `SettingsStore.Save`.
- `HistoryMatcher.Match(current scan, candidate, filesystem) → HistoryMatch { Entry, Kind (Identical | NewFiles |
  Changed), New, NewBytes, Changed, Removed }`:
  1. kandidáti: u kořene svazku záznamy se shodným `fsSerial` a `capacity` (R1); u ručně přidané složky záznamy se
     shodným `shape`;
  2. nejnovější kandidát: `fingerprint` shodný → `Identical`;
  3. jinak načíst jeho seznam souborů a porovnat: stejná cesta + velikost + mtime (u FAT/FAT32 s tolerancí R3) →
     známý; stejná cesta, jiná velikost/mtime → `Changed`; cesta navíc → nový; cesta chybí na kartě → `Removed`
     (jen informace, karta se mohla promazat ve kameře).
- `CardInspector.Inspect(string source, VolumeItem? volume, HistoryStore, CancellationToken) → CardInsight`
  (sdílí jeden `Scanner.Scan` s funkcemi 3 a 5): `TotalBytes`, `FileCount`, `Clips` (`ClipDetector.Detect`),
  `Fingerprint`, `Shape`, `HistoryMatch?`, `NameSuggestion?` (§2.3).
- Zápis historie: `OffloadJob.RunCopy` po dokončení stage 1 (úspěch i s chybami; **zrušená úloha se nezapisuje**).
  Identitu karty zjišťuje nově `OffloadJob` sám na vlákně kopie (dnes `JobQueue.Runner.RunCopy` — přesune se do
  Core, Runner ji jen čte), aby ji měl i CLI. `JobConfig.RecordHistory` (výchozí true; CLI `--no-history`).

**Vlákna.** `MainViewModel.ApplyVolumes` / přidání složky spustí pro každý zdroj `Task.Run(Inspect)` s vlastním
`CancellationTokenSource`; zdroj zmizí nebo změní `Identity` → zrušit a zahodit výsledek (stejné pravidlo jako
`NamesToDrop`). Jeden běžící inspect na cestu; žádost během běhu → po doběhnutí jednou znovu (vzor `RefreshLut`).
`history.json` se načte při startu mimo UI vlákno; dokud není načtený, řádky ukazují „Zjišťuji…“.

**CLI.** `offload copy` vypíše před plánem `Zálohováno 6. 10. 2026 8:15 na D:\Zaloha, F:\Zaloha — ověřeno` (bez
dotazu — skript nemá kdo potvrdit); `--no-history` vypne čtení i zápis.

**Okrajové případy.**

| Případ | Chování |
|---|---|
| výměna karty ve stejné čtečce během zjišťování | jiná `Identity` → výsledek se zahodí, spustí se nový |
| cíl z historie odpojený | text historie se ukáže stejně (je to záznam), doplnění viz §2.2 |
| zrušení úlohy uprostřed | do historie se nic nezapíše |
| poškozený `history.json` | přejmenovat na `history.json.corrupt-<yyyyMMdd-HHmmss>`, začít prázdný, zapsat do `app.log`; offload nikdy neblokuje |
| chybějící `.files.gz` u záznamu | `fingerprint` shodný → funguje; jinak stav „Zálohováno …“ bez počtu nových souborů |
| změna hodin / časového pásma | časy v UTC; FAT tolerance R3; „poslední záznam“ podle `finished`, ne podle pořadí v souboru |
| přeformátovaná karta | nové sériové číslo → nová karta (žádný text) |
| dvě karty se stejným sériovým číslem (klon) | rozliší je kapacita; i při shodě rozhodne otisk (jiný obsah → `Changed`, žádné „Zálohováno beze změny“) |
| CLI a GUI zapisují zároveň | mutex; čekání nejvýš 5 s, pak se záznam vynechá s řádkem v logu |

**Testy.** `CardFingerprintTests` (pořadí, mtime, prázdná karta), `HistoryStoreTests` (round-trip, poškozený soubor,
souběh dvou zapisovatelů ve dvou vláknech s mutexem, prořezání), `HistoryMatcherTests` (identical / new / changed /
removed / FAT posun o 1 h / exFAT posun o 1 h = změna), `CardInspectorTests` nad dočasnou složkou (10 000 prázdných
souborů → doba pod rozpočtem §5.5), test `OffloadJob` zapíše záznam po úspěchu a nezapíše po zrušení.

**Mac.** `HistoryStore` v `~/Library/Application Support/Offload/history.json` ve stejném formátu; klíč karty
= UUID svazku z Disk Arbitration + kapacita (u FAT/exFAT je UUID odvozené ze sériového čísla FS — ověřit na Macu,
že vyjde stejné `fsSerial` jako na Windows, aby šla historie přenést); stejné texty v `Views.swift`.

### 2.2 Doplnit nové klipy (funkce 2)

**Cíl.** Kartu, na kterou kamera po offloadu natočila další klipy, doplnit do **stejné** složky karty na všech
cílech — jen nové soubory, nikdy nic nepřepsat.

**UX.** Když `HistoryMatch.Kind == NewFiles`, řádek karty ukáže (jen u vybrané karty):

`[ ] Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)`

Zaškrtnuté: pole „Název složky“ se zablokuje a místo něj text „Složka: 2026-10-06\A001 na 2 cílech“. Doplnění
zapisuje do cílů z historie, **ne** do sekce *Cíle* (ta může být i prázdná — `CanStart` to pro výběr složený jen
z doplnění dovolí). Karta úlohy: název „A001 · doplnění“, cíle z plánu.

Hlášky před kopírováním (vše `OffloadError.Preflight`, úloha skončí dřív, než se cokoli zapíše):

| Situace | Hláška |
|---|---|
| složka zálohy chybí | „Dřívější záloha není dostupná: D:\Zaloha\2026-10-06\A001 — připojte disk, nebo udělejte celou kopii.“ |
| chybí report | „V D:\Zaloha\2026-10-06\A001 chybí report dřívější zálohy (_REPORT\offload_report.json) — doplnit nejde.“ |
| chybí MHL | „V D:\Zaloha\2026-10-06\A001 chybí ASC MHL (ascmhl\ascmhl_chain.xml) — doplnit nejde.“ |
| cíle se liší | „Zálohy na cílech se liší (D: má 2 generace MHL, F: 1) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.“ |
| konflikt | „Doplnění zastaveno — nic se nezkopírovalo. 2 soubory na kartě mají stejné jméno jako v záloze, ale jinou velikost nebo datum:\n  CLIP/A001C003.mov (karta 1,2 GB · 6. 10. 9:14, D: 1,1 GB · 6. 10. 9:10)\n  …\nUdělejte celou kopii (vznikne A001_2).“ |
| soubor v záloze chybí | „Na D: chybí soubor z dřívější zálohy: CLIP/A001C002.mov — zkontrolujte zálohu přes Nástroje → Ověřit zálohu…“ |
| soubor v záloze selhal | „CLIP/A001C002.mov se při dřívější záloze na D: nezkopíroval správně — doplnit nejde, udělejte celou kopii.“ |
| nic nového | úloha skončí **Hotovo** (ne chyba) s textem „Nic nového — záloha je úplná.“ |

Varování (neblokuje, na kartě úlohy oranžově): „5 souborů z dřívější zálohy nebylo ověřeno — doporučeno Ověřit zálohu.“

**Data na disku.**

```
<karta>/
    …nové soubory na svých místech…
    _REPORT/
        offload_report.json            původní, beze změny
        offload_report_0002.json       report doplnění (jen nové soubory), klíč "generation": 2
        offload_report_0002.txt
        <karta>_report_0002.html
        <karta>_report_0002.pdf
        <karta>.xxh64                  přepsaný (tmp + přejmenování): původní řádky + nové
        <karta>.md5                    jen s MD5 (§3.4), stejně
    ascmhl/
        0001_<karta>_<…>Z.mhl          beze změny
        0002_<karta>_<YYYY-MM-DD_HHMMSS>Z.mhl   nová generace: jen nové soubory, action="original"
        ascmhl_chain.xml               přepsaný: + <hashlist sequencenr="2"> s C4 nové generace
    _PROXY/                            nové proxy; existující jména se nikdy nepřepíší
```

- `NNNN` = číslo nové generace MHL (4 číslice), stejné na všech cílech (R7).
- MHL generace N: `<process>transfer</process>`, `<comment>Offload doplnění: 12 nových souborů z E:\</comment>`,
  `<ignore>` stejné jako generace 1. Soubory ze starších generací se znovu nehashují ani nezapisují (R5).
- JSON doplnění: stejné klíče jako `offload_report.json` + `generation` (číslo; chybí = 1).
- Původní HTML/PDF/JSON se nepřepíší nikdy; seznamy součtů (`.xxh64`, `.md5`) ano — jsou odvozené z MHL.

**Jádro.**

- `AppendPlanner.Plan(source, IReadOnlyList<string> cardPaths, CancellationToken) → AppendPlan` (čisté funkce nad
  `Scanner.Scan` a obsahem cílů, testovatelné): načte `offload_report.json` + `offload_report_NNNN.json` každého cíle
  (`CardRecord.Load`, §3.1 — v plánu 6 se zavede jeho jádro), čísla generací z `ascmhl_chain.xml`, porovná kartu
  s každým cílem (velikost + mtime s tolerancí R3). Výsledek: nové soubory, konflikty, neověřené, generace.
- `OffloadPlan` dostane `Mode` (`Full | Append`) a `Generation` (1 u celé kopie). U doplnění se `DestRoots`, `Date`
  a `CardName` odvodí z cest složek (`<root>\<datum>\<karta>`), takže `OffloadPlan.CardPaths` zůstane beze změny;
  `Scan` obsahuje **jen nové soubory** a jejich složky. Nové `OffloadEngine.PrepareAppend(source, cardPaths)`
  dělá stejné kontroly jako `Prepare` (zdroj ≠ cíl, názvy Windows, FAT32, místo) nad novými soubory.
- `OffloadEngine.Run` beze změny: `DestWriter` už přejmenovává `replace: false` (soubor, který se mezitím objeví,
  selže, nepřepíše se).
- `OffloadReport.Write(cardPath, includeHashRecords, generation)`: jména souborů podle generace; seznam součtů =
  přečíst existující + připojit (`NativeIO.WriteFileDurably`); `Mhl.WriteGeneration(report, cardPath, n)` + přepis
  řetězu.
- Stage 2: klipy jen z nových souborů; `usedNames` se předvyplní jmény v `_PROXY\` cíle; `ProxyRenderer` a
  `ProxyCopy.CopyFile` v režimu doplnění s `replace: false`.
- Historie: záznam karty dostane další generaci, seznam souborů = sjednocení, `fingerprint` = aktuální karta.

**CLI.** `offload append <source> [--into <složka karty>]… [--no-verify] [--hash …] [--proxy …]` (volby stage 2
jako `copy`). Bez `--into` najde složky v historii; nenajde → „Karta není v historii — zadejte složky zálohy volbou
--into.“ Kód 0 = doplněno a ověřeno nebo nic nového, 1 = chyba.

**Okrajové případy.**

| Případ | Chování |
|---|---|
| výměna karty mezi zobrazením a startem | `AppendPlanner` čte kartu znovu při startu úlohy; rozhoduje jen to |
| cíl odpojený před startem | hláška „Dřívější záloha není dostupná…“, nic se nekopíruje |
| cíl odpojený během kopie | jako dnes: soubory na tom cíli `failed`, ostatní pokračují; report a MHL generace se zapíší jen tam, kde to jde; příští doplnění odmítne „cíle se liší“ → uživatel ověří zálohu / udělá celou kopii |
| zrušení uprostřed | rozpracované `.offload-part` se smažou; hotové soubory zůstanou a report + MHL generace je zaznamenají (`failed` u nedokončených) — stejně jako zrušená celá kopie dnes; historie se nezapíše |
| dva cíle se liší | konflikt / „cíle se liší“, nic se nekopíruje |
| záloha udělaná na Macu | funguje (stejné soubory); generace 2 do Mac řetězu je platné ASC MHL |
| poškozená historie | doplnění z GUI nejde nabídnout (není co porovnat); CLI `--into` funguje vždy |
| změna času (FAT) | tolerance R3 na kartě i na cíli |
| stejná karta už je ve frontě | Start ji nenabídne (vybraná karta se po startu odškrtne jako dnes); dvě doplnění téže složky po sobě: druhé uvidí soubory prvního jako známé |

**Testy.** `AppendPlannerTests` nad dočasnými složkami (nové soubory, konflikt velikosti, konflikt mtime, FAT posun,
chybějící soubor na cíli, `failed` v reportu, rozdílné generace, report z Macu jako fixture), `AppendEngineTests`
(dva cíle, generace 2 se zapíše, řetěz má dvě položky s platným C4, `.xxh64` obsahuje vše, původní HTML/JSON
bajtově beze změny, existující proxy se nepřepíše), test kompatibility `ascmhl verify` nad výsledkem (jako dnes
test 3 v §10 hlavní spec, ručně / když je `ascmhl` k dispozici).

**Mac.** Stejný `AppendPlanner` v OffloadKit, `MHL.swift` musí umět generaci N a připsání do řetězu, `Report.swift`
klíč `generation` a jména `_NNNN`; `offload append`.

### 2.3 Název karty z kamery (funkce 3)

**Cíl.** Navrhnout název složky karty z názvů klipů (`A001C003…` → `A001`), jinak „další v řadě“; nikdy nepřepsat
napsaný název.

**Rozpoznávání (jen názvy souborů, R10 — klipy se před kopií nečtou).** `ReelNames.FromClipName(string name)`,
bez ohledu na velikost písmen, výstup velkými písmeny:

| Kamera | Vzor (regex nad názvem bez přípony) | Příklad → návrh |
|---|---|---|
| ARRI, Sony Venice, Canon Cinema EOS | `^(?<cam>[A-Z])(?<reel>\d{3})C(?<clip>\d{3,4})` | `A001C003_210101_R1AB` → `A001` |
| RED | `^(?<cam>[A-Z])(?<reel>\d{3})_C(?<clip>\d{3})` | `A001_C003_0101AB_001` → `A001` |
| Blackmagic | `^(?<cam>[A-Z])(?<reel>\d{3})_\d{8}_C(?<clip>\d{3})` | `A001_08251530_C001` → `A001` |
| Sony XAVC (`C0001`), Panasonic, DJI, telefony | — | žádný návrh |

`ReelSuggestion.FromClips(clips)`: všechny rozpoznané klipy (bez proxy kamery) mají stejnou cívku → návrh;
víc cívek → žádný návrh, pod řádkem „Na kartě je víc cívek: A001, A002“; ARRIRAW `.ari` sekvence se posuzuje podle
názvu složky.

**Další v řadě.** `ReelSuggestion.Next(history, date, project)`: záznamy se stejným datem složky (a od plánu 8 se
stejným projektem), jejichž `cardName` odpovídá `^[A-Z]\d{3}$`; právě jedno písmeno kamery → písmeno + (max + 1)
na tři číslice (`A001` → `A002`); víc písmen → nic (nelze poznat, která kamera to je).

**UX.** Pole „Název složky“: zástupný text (`CardNamePlaceholder`, dnes jmenovka svazku) = návrh; pod polem malý
šedý text „podle názvů klipů (A001C001…)“ nebo „další v řadě po A001 (dnes)“. Napsaný název má vždy přednost.
Start použije přesně to, co je v tu chvíli vidět (návrh, jinak jmenovka); `StartJobs` předá návrh jako
`JobConfig.CardName`. Volba v *Struktura a kontrola*: `[x] Navrhovat název karty podle klipů` (`naming.suggestFromClips`,
výchozí zapnuto). Doplnění (§2.2) název nemění.

**CLI.** `--name auto` = návrh z klipů, jinak jmenovka; bez `--name` jako dnes (jmenovka, parita s Macem).

**Okrajové případy.** Výměna karty → nový inspect, starý návrh se zahodí (`NamesToDrop` už maže napsaný název);
návrh `A001` už na cíli existuje → `Layout.UniqueCardFolder` dá `A001_2` jako dnes; poškozená historie → jen návrh
z klipů; změna hodin nevadí (rozhoduje datum složky `DateString`).

**Testy.** `ReelNamesTests` (tabulka výše + negativní: `C0001`, `A01C003`, `AA001C003`, malá písmena), `ReelSuggestionTests`
(jedna cívka, víc cívek, proxy kamery ignorované, další v řadě s jedním / dvěma písmeny, jiný den).

**Mac.** Stejné vzory a pravidla v OffloadKit, zástupný text v `Views.swift`.

### 2.4 Ověřit zálohu (funkce 4)

**Cíl.** Kdykoli později přepočítat hashe složky karty proti jejímu ASC MHL (všechny generace) a dostat česky
srozumitelný výsledek a volitelně report.

**UX.**

- Menu *Nástroje* → „Ověřit zálohu…“ (`Ctrl+Shift+O`) → `OpenFolderDialog` „Vyberte složku karty na cíli (nebo
  celý den)“. Vybraná složka bez `ascmhl\` i `_REPORT\`, ale s podsložkami, které je mají (složka dne) → ověří se
  každá karta zvlášť.
- Každé ověření je položka ve *Frontě* (běží v **kopírovací** frontě `JobQueue` sériově s kopiemi — ověření i kopie
  zatěžují tytéž disky): titulek „Ověření A001“, průběh „Ověřuji 120 / 1 234 souborů · 45 GB / 512 GB · 380 MB/s“,
  tlačítko *Zrušit*.
- Výsledek zeleně: „Ověřeno: všech 1 234 souborů sedí (ASC MHL, 2 generace, xxHash64).“ Červeně: „Nesedí 2 soubory,
  chybí 1, nečitelný 0 — podrobnosti v reportu ověření.“ Oranžově navíc: „3 soubory navíc (nejsou v MHL).“,
  „1 soubor selhal už při kopii.“, „ASC MHL generace 0002 neodpovídá řetězu (soubor MHL byl změněn).“
- Tlačítka *Report ověření* (otevře HTML) a *Průzkumník*. Volba v dialogu „Uložit report ověření“ (výchozí zapnuto).

**Data na disku (R8).** Do `ascmhl\` se nezapisuje nic.

```
<karta>/_REPORT/
    <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.html
    <karta>_verify_<YYYY-MM-DD_HHMMSS>Z.pdf
    offload_verify_<YYYY-MM-DD_HHMMSS>Z.json
```

Výjimka (oprava po revizi): když historie `ascmhl` karty `_REPORT` **neignoruje** (cizí nástroj, např. `ascmhl` 1.0.1 ignoruje jen `.DS_Store` a `ascmhl`),
report v kartě by byl pro `ascmhl verify` „nový nezaznamenaný soubor“. Pak jde report do denního `_REPORT` vedle složky karty
(`<datum>/_REPORT/<karta>_verify_<stamp>Z.html/.pdf/.json`) a CLI/GUI to česky oznámí („Report ověření je uložený mimo složku karty, aby nenarušil její
historii ASC MHL: <cesta>“). Historie zapsané Offloadem `_REPORT` ignorují, tam zůstává report v kartě.

`offload_verify_….json` (UTF-8 bez BOM, seřazené klíče, data ISO 8601 UTC jako report):
`tool`, `version`, `cardPath`, `started`, `finished`, `basis` (`ascmhl` | `hashList`), `generations` (počet),
`hashAlgorithms` (např. `["xxh64"]`), `totalFiles`, `totalBytes`, `ok` (počet), `changed[]`, `missing[]`,
`unreadable[]` ({`path`, `error`}), `extra[]`, `failedAtCopy[]`, `failedSuperseded[]` (soubory v pořádku, jejichž nejnovější záznam je `failed`), `chainProblems[]` (texty), `success` (bool).
Pole jsou relativní cesty s `/`.

**Jádro.** `Offload.Core.Verification`:

- `MhlReader.Read(cardPath) → MhlHistory`: `ascmhl_chain.xml` (pořadí, C4 každé generace — nesouhlas → `chainProblems`),
  každá generace: `path`, `size`, `lastmodificationdate`, hashe podle prvku (`xxh64`, `md5`, `sha1`, `xxh3`, `xxh128`,
  `c4`) s `action`, `<ignore>` vzory. Pro každou cestu platí **nejnovější** generace, která ji uvádí. Chybí řetěz,
  ale jsou `.mhl` → číst je podle čísla v názvu s varováním.
- `HashAlgo` (registr): `xxh64` (`XxHash64`), `md5` (`MD5`/`IncrementalHash`), `sha1`, `xxh3` (`XxHash3`), `xxh128`
  (`XxHash128`), `c4` (`SHA512` + `Mhl.C4`); hex podle kanonické big-endian podoby (testy proti vektorům z ascmhl).
- `BackupVerifier.Verify(cardPath, progress, ct) → VerifyResult`: každý soubor se čte jednou (`FileReader`, bez
  cache) a hashuje všemi algoritmy, které pro něj MHL má; velikost se kontroluje zvlášť. `action="failed"` v MHL
  → `failedAtCopy`. Navíc = `Scanner.Scan` minus MHL minus ignore vzory minus `_REPORT/`, `_PROXY/`, `ascmhl/`.
  Bez `ascmhl\` → dnešní `Verifier` nad `<karta>.xxh64` / `<karta>.md5` (`basis: hashList`).
- `VerifyReport.Render/Write`: HTML ve stylu `HtmlReport` (společné CSS), PDF přes `PdfReport` (nová přetížená metoda
  `PdfReport.WriteHtml(html, path)`).
- GUI: `VerifyJob : IQueuedJob` (`RunCopy` = ověření, vrací false; `RunPost` nic), `VerifyItemViewModel` +
  `DataTemplateSelector` v `QueuePanel.xaml`.

**CLI.** `offload verify <složka> [--report]`. Řádky problémů zůstávají strojové (`MISMATCH`, `MISSING`, `ERROR`,
`EXTRA` + nové `FAILED-AT-COPY`, `CHAIN`) kvůli skriptům; souhrn česky:
„Ověřeno 1 234 souborů (512 GB) za 22:10 (385 MB/s) — vše v pořádku.“ / „… — NALEZENY PROBLÉMY: nesedí 2, chybí 1,
nečitelné 0, navíc 3.“ Kód 0 / 1. Složka dne → ověří všechny karty, kód 1 když selže kterákoli.

**Okrajové případy.** Disk se odpojí během ověření → zbylé soubory `unreadable`, souhrn „disk se možná odpojil“;
zrušení → výsledek „Ověření zrušeno po 120 / 1 234 souborech“, report se nezapíše; ověření složky, do které právě
renderuje proxy stage 2 → `_PROXY` a `_REPORT` se ignorují; cíle se liší → ověřuje se každá složka sama za sebe;
změna hodin nevadí (hashe).

**Testy.** `MhlReaderTests` (fixture z naší `Mhl.Write`, dvě generace, ruční MHL s `md5`/`sha1`/`xxh128`,
poškozený řetěz, chybějící řetěz), `HashAlgoTests` (vektory), `BackupVerifierTests` (OK, změněný bajt, chybějící,
navíc, `failed`, `.xxh64` fallback, zrušení), `VerifyReportTests` (JSON tvar, HTML obsahuje české texty).

**Mac.** `MHLReader` + `verify` nad MHL v OffloadKit, report ověření stejných jmen; GUI menu.

### 2.5 Kontrola volného místa před startem (funkce 5)

**Cíl.** Nespustit offload, na který není místo — včetně proxy, náhledů a víc karet najednou; varovat, když je
rezerva malá.

**Potřebné místo na jednom svazku cíle** (`SpaceEstimator`, čisté funkce):

```
karta     = totalBytes + files × 64 KiB + 64 MiB             (dnešní vzorec z OffloadEngine.Prepare)
report    = 32 MiB + clips × 1 MiB                            (HTML/PDF s náhledy, MHL, seznamy, CSV)
proxy     = Σ přes klipy odhad(klip)                          jen když proxy zapnuté; cíl 1 vždy, ostatní jen
                                                              s „Kopírovat proxy na všechny cíle“
odhad(klip) = délka × datový tok proxy
   délka   = před kopií: bytes × 8 / předpokládaný tok zdroje (tabulka), po analýze: ClipInfo.Duration
   tok     = H.264/HEVC: bitrateMbps × 1,1 + 0,2 Mb/s × zvukové stopy
             ProRes: tabulka Apple pro 1080p30 (Proxy 45, LT 102, 422 147, HQ 220 Mb/s) × (výška/1080)²
```

Předpokládaný tok zdroje (spodní odhad → delší délka → větší proxy, tedy opatrně): `.mp4/.mov/.mts` 50 Mb/s,
`.mxf` 100, BRAW 150, R3D 200, CRM 500, ARRIRAW 1 000 Mb/s. Proxy kamery (`SkipCameraProxies`) a nepodporované
formáty (`ProxyRenderer.UnsupportedReason` bez ffmpeg) se nepočítají. Složka pro proxy navíc (`proxy.extraFolder`)
je samostatný svazek s požadavkem = proxy.

**Součet (`SpacePlanner.Check`).** Pro každý **svazek** (ne cestu — dva cíle na jednom svazku s `--allow-same-device`
se sečtou): vybrané karty + úlohy ve frontě, které ještě nezačaly (jejich požadavek) + běžící úlohy (požadavek −
už zapsáno) ≤ volno. Výsledek: `Blocking` (nevejde se) nebo `Warning` (volno po offloadu < 5 % potřebného, R11) nebo nic.

**UX (GUI).** Pod lištou *Spustit offload*, stejně jako `LutProblem` (červené, blokuje) a `ToolsWarning` (oranžové):

- červeně: „Na cíli Zaloha (D:) není dost místa: potřeba 412 GB (karty 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB.“
- oranžově: „Na cíli Zaloha (D:) zbude po offloadu jen 6 GB — rezerva pod 5 %, odhad proxy je přibližný.“
- ještě neznámá velikost (inspect běží) → nic neblokuje; Start počká na výsledek inspectu vybraných karet nejvýš 10 s
  (mimo UI vlákno, tlačítko neaktivní jako při kontrole LUTu), pak rozhodne podle toho, co ví (karta ze svazku:
  obsazené místo svazku `Total − Free` jako horní odhad).

Vstupy: velikost karet z `CardInsight` (§2.1), volno z `DestinationViewModel.Probe` (už dnes, každých 5 s u síťových
a vyměnitelných), požadavky fronty z `JobItemViewModel`. Výpočet v paměti (vyhodnocuje se 3×/s jako `CanStart`).

**Jádro, úloha.** `OffloadJob.Prepare` spočítá požadavek z `Config.Proxy` a předá ho do `OffloadEngine.Prepare`
(nový parametr `Func<string root, long extraBytes>`), takže kontrola zůstane na jednom místě a platí i pro CLI
a pro úlohu, která se spustí až za hodinu. **Před renderem proxy** (`AnalyzeAndRender`, po analýze) se požadavek
přepočítá z `ClipInfo.Duration`; nevejde-li se → proxy se nerenderují, u úlohy oranžově „Proxy se nevyrenderovaly:
na Zaloha (D:) chybí místo (potřeba ~30 GB, volno 12 GB).“ (stav „vyžaduje pozornost“, §4.3), kopie zůstává ověřená.

**CLI.** Stejné hlášky; varování < 5 % jako řádek `Varování: …` a pokračuje se. `--dry-run` vypíše rozpis
„Místo: D:\ potřeba 412 GB (karta 380, proxy ~30, report 2), volno 398 GB“.

**Okrajové případy.** Disk odpojený → už dnes červené „Disk není připojen“; volno se mění, protože jiný program
zapisuje → kontrola při startu úlohy (engine) je poslední slovo; zrušená úloha ve frontě se nepočítá; doplnění
(§2.2) počítá jen nové soubory na cílech z historie.

**Testy.** `SpaceEstimatorTests` (tabulky, ProRes škálování, proxy vypnuté, kamera proxy), `SpacePlannerTests`
(dvě karty na jeden cíl, fronta + běžící úloha, dva cíle na jednom svazku, složka pro proxy na stejném svazku jako
cíl, hranice 5 %), engine test „nevejde se kvůli proxy“ s falešným volnem (`NativeIO.AvailableBytes` za rozhraním
v testech jako dnes `OffloadOptions` interní háčky), stage 2 test „proxy přeskočeny pro místo“.

**Mac.** Stejný `SpaceEstimator` a tabulka toků (je to chování, ne formát — zápis do ROZDILY, ne do FORMATY);
Mac má dnes jen vzorec karty.

### 2.6 České hlášky před kopírováním (funkce 6)

**Cíl.** Každá hláška, kterou uživatel uvidí z kontrol a validace (GUI na kartě úlohy, CLI), je česky. Logy nástrojů
(`app.log`, stderr ffmpeg/braw-decode), TXT report (sdílený formát s Macem, FORMATY „stejný obsah a texty“) a
`--help` zůstávají anglicky.

**Jádro.** `Offload.Core/Messages.cs` — statická třída `Msg` s metodou na každou hlášku (jeden jazyk, bez resx);
`OffloadError.Preflight(PreflightCode code, string message)` — kód pro testy a pro GUI (např. rozlišit „nic nového“).

| # | Kde | Dnes | Česky |
|---|---|---|---|
| 1 | `OffloadEngine.Prepare` | ChunkSize must be a positive multiple of 4096… | „Velikost bloku musí být násobek 4096 B a nejvýš 1 GiB (zadáno {n}).“ |
| 2 | 〃 | BufferCount must be at least 2 | „Počet bufferů musí být aspoň 2 (zadáno {n}).“ |
| 3 | 〃 | Date must be YYYY-MM-DD | „Datum musí být ve tvaru RRRR-MM-DD (zadáno „{d}“).“ |
| 4 | 〃 | Source not found | „Zdroj nenalezen: {path}“ |
| 5 | 〃 | No destination given | „Není zadaný žádný cíl.“ |
| 6 | 〃 | Destination given twice | „Cíl je zadaný dvakrát: {root}“ |
| 7 | 〃 | Source looks like an earlier offload… | „Zdroj vypadá jako dřívější offload (má _REPORT, _PROXY nebo ascmhl) — kopírujte původní kartu, nebo tyto složky odeberte.“ |
| 8 | 〃 | Cannot stat source | „Zdroj nejde přečíst: {path}“ |
| 9 | 〃 | Destination not found | „Cíl nenalezen: {root} — je disk připojený?“ |
| 10 | 〃 | Destination not writable | „Na cíl nejde zapisovat: {root}“ |
| 11 | 〃 | Cannot stat destination | „Cíl nejde přečíst: {root}“ |
| 12 | 〃 | Destination … is on the same volume as the source | „Cíl {root} je na stejném disku jako zdroj.“ |
| 13 | 〃 | Destinations share a volume… | „Dva cíle jsou na stejném disku ({root}) — to není druhá kopie. Povolit jde volbou --allow-same-device.“ |
| 14 | 〃 | Source contains no files | „Na zdroji nejsou žádné soubory.“ |
| 15 | 〃 | Names Windows cannot store: | „Některé názvy Windows neumí uložit:“ + seznam + „… a dalších {n}“ |
| 16 | `Scanner.WindowsProblems` | same name as X except for upper/lower case | „{p}: stejný název jako {x}, liší se jen velikostí písmen“ |
| 17 | `Layout.InvalidNameReason` | contains a character Windows does not allow / ends with a dot or space / reserved device name on Windows | „obsahuje znak, který Windows nepovolí“ / „končí tečkou nebo mezerou“ / „rezervovaný název zařízení ve Windows“ |
| 18 | `OffloadEngine.Fat32Problem` | {fs} cannot store files of 4 GB or more | „{root}: {fs} neumí uložit soubory od 4 GB ({path}, {size})“ |
| 19 | `OffloadEngine.Prepare` | Not enough space on… | „Na cíli {root} není dost místa: potřeba {need}, volno {free}.“ (+ rozpis §2.5) |
| 20 | `OffloadEngine.Run` | Report could not be written | „Report se nepodařilo zapsat: {…}“ |
| 21 | `Verifier` | No .xxh64 hash list in… | „Ve složce {dir} není ASC MHL ani seznam součtů — není s čím porovnat.“ |
| 22 | `Bench` | No files to read in… | „V {path} nejsou soubory ke čtení.“ |
| 23 | CLI všude | `Error: ` | „Chyba: “ |
| 24 | `CopyCommand` | Unknown proxy codec | „Neznámý kodek proxy „{x}“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.“ |
| 25 | 〃 | LUT not found | „LUT nenalezen: {path}“ |
| 26 | 〃 | Proxy folder not found | „Složka pro proxy nenalezena: {path}“ |
| 27 | 〃 | Note: --proxy-folder has no effect without --proxy. | „Poznámka: --proxy-folder bez --proxy nic nedělá.“ |
| 28 | 〃 | Cancelling — finishing current block… | „Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…“ |
| 29 | `CliParse` | Cannot parse '…' as a positive number. | „„{s}“ není kladné číslo.“ |
| 30 | `LutOption.Parse` | Unknown fallback LUT… | „Neznámý záložní LUT „{x}“. Vestavěné: {ids}“ |
| 31 | 〃 | Unknown LUT… | „Neznámý LUT „{x}“. Použijte auto, none, cestu k .cube nebo jedno z: {ids}“ |
| 32 | `LutsCommand` | No built-in LUTs found… | „Vestavěné LUTy nenalezeny (chybí složka luts).“ |
| 33 | `OffloadJob` (poznámka u klipu, HTML report) | no verified copy | „žádná ověřená kopie“ |
| 34 | `ProxyRenderer` | ffmpeg not found / ffprobe not found | „ffmpeg nenalezen“ / „ffprobe nenalezen“ |
| 35 | 〃 | unknown format / {f}: no decoder available | „neznámý formát“ / „{f}: chybí dekodér“ |

Nové hlášky funkcí 2, 4, 5, 12–14 jsou česky od začátku. Chyby jednotlivých souborů v reportu (`size mismatch`,
`HASH MISMATCH`, `verify:`, `write failed` z `DestWriter`) zůstávají anglicky — jsou v JSON sdíleném s Macem;
HTML je může přeložit při zobrazení (mapa v `HtmlReport`, plán 7, úkol „drobnosti“).

Vestavěné chyby parseru `System.CommandLine` (neznámá volba…): CLI nastaví `CultureInfo.DefaultThreadCurrentUICulture
= cs-CZ` a `Offload.Cli.csproj` povolí satelit `cs` (`SatelliteResourceLanguages` `en;cs`) — **ověřit při
implementaci**, že balíček 2.0.12 českou lokalizaci obsahuje; jinak zůstanou anglicky a návod to řekne.

**Testy.** `PreflightMessagesTests`: každý scénář z `PreflightTests` + kontrola, že zpráva = přesný český text
a `PreflightCode`; test, že žádná zpráva `OffloadError.Preflight` neobsahuje anglická slova ze seznamu
(`not found`, `cannot`, `must`, `Destination`, `Source`).

**Mac.** Mac je dnes anglicky i v CLI — převezme `Msg` texty (stejné kódy) v `Engine.swift` a `main.swift`.

## 3. Skupina B — výstupy pro produkci a střih (plán 7)

### 3.1 Společný základ: `CardRecord`

`CardRecord.Load(cardPath)` (zavádí se v plánu 6 pro §2.2, rozšiřuje plán 7): přečte `offload_report.json`
a všechna `offload_report_NNNN.json`, sloučí soubory a klipy (generace se nepřekrývají), poznamená, které generace
existují a jestli je každá ověřená. Toleruje reporty z Macu (chybí `lut`, `proxyEncoder`, `generation`, `md5`)
i nečitelný JSON (`CardRecord.Error`). Je zdrojem pro denní report i seznam klipů.

### 3.2 Denní report (funkce 7)

**Cíl.** Jeden přehled natáčecího dne na každém cíli: všechny karty té složky data.

**Data na disku (R12).**

```
<cíl>/<YYYY-MM-DD>/_REPORT/
    <YYYY-MM-DD>_day_report.html
    <YYYY-MM-DD>_day_report.pdf
    <YYYY-MM-DD>_clips.csv          (§3.3, když je CSV zapnuté)
    <YYYY-MM-DD>_clips.ale          (§3.3, když je ALE zapnuté)
```

Karty dne = podsložky `<datum>\`, které mají `_REPORT\offload_report.json` (kromě složky `_REPORT` samotné).
Složka karty bez reportu → řádek „bez reportu (probíhá, nebo byl přerušen)“.

**Obsah HTML/PDF** (styl `HtmlReport`, A4 na šířku, bez náhledů): hlavička „Denní report — 6. 10. 2026“, cíl, projekt
(když mají všechny karty stejný, plán 8); dlaždice: karty, soubory, data, klipy, celková délka, ověřené karty
„7 / 8“; tabulka karet: *Karta · Čas (začátek–konec) · Soubory · Data · Klipy · Délka · Kamery · Ověření · Chyby ·
Proxy (hotovo/selhalo) · LUTy · Doplnění (počet generací)*; sekce *Problémy* (karty s chybou: počet selhaných
souborů + prvních 5 cest, neověřené karty, nečitelné reporty); sekce *LUTy* (název LUTu → počet klipů); patička
s verzí a časem vytvoření.

**Kdy se tvoří.**

- Automaticky po skončení každé úlohy (stage 2, nebo stage 1 když stage 2 není; i doplnění) pro každý cíl úlohy —
  `DayReportService.Regenerate(root, date)` volaný z `OffloadJob` na pracovním vlákně. Zámek: `SemaphoreSlim`
  na složku dne v procesu + pojmenovaný mutex `Local\Offload-day-<sha1(path)>` mezi procesy (GUI + CLI). Selhání se
  jen zaloguje, úlohu nikdy neshodí.
- Na vyžádání: *Nástroje* → „Denní report…“ (výběr složky dne) → vytvoří a otevře HTML; CLI
  `offload day-report <složka dne>` (vypíše cestu, kód 0/1).
- Vypnutí: `outputs.dayReport` (výchozí zapnuto), checkbox „Denní report po každé kartě“; CLI `--no-day-report`.

**Okrajové případy.** Dvě úlohy skončí zároveň → zámek, druhá přepíše novějším stavem; Mac zapisuje do stejného
dne současně → poslední zápis vyhraje (soubory jsou odvozené, R25); cíl odpojený → pro něj se report nevytvoří
(log); cíle se liší (karta jen na jednom) → každý cíl má report podle svého obsahu (záměrně — ukáže chybějící kartu);
vlastní datum → rozhoduje složka, ne hodiny; zrušená úloha → regenerace proběhne (karta se ukáže jako neúplná).

**Testy.** `DayReportTests` nad dočasnou složkou dne se třemi kartami z fixture reportů (Windows, Mac bez nových
klíčů, s doplněním generace 2, poškozený JSON, složka bez reportu); souběh dvou regenerací; PDF (`[EdgeFact]`).

**Mac.** Stejná jména a obsah; spouštění po úloze v `Job.swift`.

### 3.3 Seznam klipů — CSV a Avid ALE (funkce 8)

**Cíl.** Seznam klipů karty a dne pro střih a produkci.

**Soubory.** Karta: `_REPORT\<karta>_clips.csv`, `_REPORT\<karta>_clips.ale`; den: viz §3.2. Píšou se při každém
přepisu reportu ve stage 2 (`RewriteReports`) a po doplnění pokrývají všechny generace (R25). Bez analýzy klipů
se řádky vytvoří z `ClipDetector.Detect` (metadata prázdná).

**CSV** — UTF-8 s BOM, oddělovač čárka (otevřená otázka O2), konce řádků CRLF, RFC 4180 (pole s čárkou, uvozovkou
nebo koncem řádku v uvozovkách, uvozovky zdvojené), hlavička vždy. Sloupce v tomto pořadí:

| Sloupec | Obsah |
|---|---|
| `Clip Name` | název klipu (`Clip.Name`) |
| `File Name` | jméno hlavního souboru s příponou (pro import metadat do Resolve) |
| `Path` | relativní cesta v kartě s `/` |
| `Card` | název složky karty (u denního seznamu důležité) |
| `Camera` | `ClipInfo.Camera` |
| `Reel` | cívka (§3.4 `ReelResolver`) |
| `Start TC` | `HH:MM:SS:FF` (`;` u drop-frame tak, jak ho dá zdroj), prázdné bez TC |
| `Duration` | délka jako TC v nominálních snímcích, bez fps `H:MM:SS` |
| `FPS` | invariantně, např. `23.976`, `25` |
| `Resolution` | `3840x2160` |
| `Codec` | kodek / formát RAW |
| `LUT` | `LutUse.Name` nebo prázdné |
| `Size (bytes)` | celé číslo, součet segmentů |
| `XXH64` | hash hlavního souboru (u vícesegmentových klipů prvního segmentu) |
| `MD5` | dtto, když je MD5 (§3.4) |

Hodnoty začínající `=`, `+`, `-`, `@` se **neupravují** (úprava by rozbila import do Resolve) — navod upozorní.

**ALE** — UTF-8 bez BOM, CRLF, tabulátory; tabulátory a konce řádků uvnitř hodnot → mezera:

```
Heading
FIELD_DELIM	TABS
VIDEO_FORMAT	1080
AUDIO_FORMAT	48khz
FPS	25

Column
Name	Tape	Start	End	Tracks	Source File	Camera	LUT	Resolution	Codec	XXH64	MD5
Data
A001C003	A001	01:00:00:00	01:00:12:10	VA1A2	A001C003.mov	ARRI ALEXA 35	ARRI LogC4 → Rec.709	3840x2160	ProRes 4444	1a2b…	
```

`VIDEO_FORMAT` = nejčastější výška (`1080`, `720`, jinak `CUSTOM`); `End` = start + délka (výlučně); `Tracks` = `V` +
`A1…An` podle zvukových kanálů (nejvýš 8); klip bez TC má `Start` `00:00:00:00`. Smíšené fps na kartě/dni → jeden
soubor na fps: `<karta>_clips_25.ale`, `<karta>_clips_23.976.ale` (ALE má jedno FPS v hlavičce).

**Volby.** `outputs.clipListCsv` (výchozí zapnuto), `outputs.clipListAle` (výchozí vypnuto) — checkboxy „Seznam klipů
CSV“ a „Seznam klipů pro Avid (ALE)“ v *Struktura a kontrola*; CLI `--no-clip-list`, `--ale`.

**Testy.** `ClipListTests`: escapování CSV (čárka, uvozovka, nový řádek, čeština), BOM, CRLF; ALE hlavička, tabulátory,
smíšené fps → dva soubory, klip bez TC, drop-frame; fixtures `ClipEntry` bez kamery.

**Mac.** Stejné soubory a sloupce.

### 3.4 Proxy pro přelinkování (funkce 9)

**Cíl.** Proxy, které Resolve i Premiere připojí k originálům: stejný základ jména, start TC, reel name, stejné
zvukové stopy.

**Co už je.** Jméno `<klip>.mov` (při shodě jmen `_2`), `-timecode` ze `ClipInfo.Timecode`, stejné fps (bez `-r`),
burn-in názvu a TC (`FilterGraph.BurnIn`).

**Mezera a návrh.**

- `ReelResolver.Resolve(clip, info, cardName) → string`: (1) tag z metadat — ffprobe `reel_name`, `tape_name`,
  `reel` (formát i proudy; `FFProbe` je začne číst do nového `ClipInfo.Reel`); (2) vzor z názvu klipu (§2.3);
  (3) název složky karty (jako „Reel“ u DIT nástrojů). Výsledek do reportu `clips[].info.reel`.
- `ProxyRenderer.RenderOnce`: s platným TC navíc `-metadata:s:v:0 reel_name=<reel>` — muxer MOV ho zapíše do
  stopy `tmcd` (zdrojová reference, kterou čte Premiere jako *Tape Name* a Resolve jako *Reel*). **Ověří test**:
  ffprobe proxy ukáže `reel_name` u datového proudu `tmcd`.
- Zvuk (R14): `-map 0:a?` místo `-map 0:a:0?`; H.264/HEVC: AAC na každý proud s `-b:a:<i>` = 64 kb/s × kanály
  (min. 128k, max. 512k); ProRes: `pcm_s16le` na každý proud (beze změny kodeku).
- Proxy přejmenovaná kvůli shodě (`_2`) → poznámka u klipu „Proxy přejmenována na A001C003_2 (shoda jmen) —
  přelinkujte ji ručně.“
- Burn-in (R15): beze změny — volby „Burn-in názvu klipu“ a „Burn-in timecodu“, výchozí zapnuto, `--no-burn-in`.

**Okrajové případy.** Klip bez TC → bez `-timecode` i bez `reel_name` (Resolve přelinkuje podle jména); RAW přes
mezisoubor (R3D, ARRI) → TC a reel z `ClipInfo` stejně; přerušený render → `.part` se smaže jako dnes.

**Testy.** `ProxyRendererTests` (`[FfmpegFact]`): syntetický klip s TC `01:00:00:00` a dvěma zvukovými proudy
(`sine` ×2) → proxy má `timecode`, `reel_name`, dva zvukové proudy; `ReelResolverTests` (tag > jméno > karta).

**Mac.** `Proxy.swift`: `reel_name`, všechny zvukové stopy.

### 3.5 MD5 (funkce 10)

**Cíl.** Volba kontrolního součtu `xxHash64` (výchozí) | `xxHash64 + MD5` (R16: MD5 nikdy samotné).

**UX.** *Struktura a kontrola*: „Kontrolní součet“ `[xxHash64 (rychlý) ▾ | xxHash64 + MD5]`; checkbox ověření
se jmenuje „Ověřit kopie zpětným čtením ({algoritmus})“. Při MD5 tooltip/řádek „MD5 je pomalejší — u rychlých karet
(CFexpress) může zpomalit kopii.“ CLI `--hash xxh64|both`.

**Data na disku.**

- `offload_report.json`: `hashAlgorithm` = `xxh64` vždy (Mac ho jen zobrazí); nový volitelný `hashAlgorithms`
  (pole, jen když není `["xxh64"]`, tj. `["xxh64","md5"]`); u souboru volitelný `md5` (32 malých hex); `xxh64` je vždy.
- `_REPORT\<karta>.md5`: `<md5><dvě mezery><cesta>` (formát `md5sum`), UTF-8 bez BOM, `\n`. `<karta>.xxh64` vždy.
- MHL: v `<hash>` jeden prvek na algoritmus (`<xxh64 …>`, `<md5 …>`), stejné `action` a `hashdate`.
- TXT: `Hash:        xxh64+md5`; HTML: dlaždice „XXH64 + MD5“, v seznamu souborů sloupec na algoritmus.

**Jádro.** `HashKind` (`Xxh64 = 1, Md5 = 2`, příznaky) v `OffloadOptions`/`JobConfig`; `SourceHasher` jeden na
algoritmus (vlastní `SerialWorker` → paralelně; `Chunk` má o konzumenta víc); `FileHashes(ulong? Xxh64, byte[]? Md5)`
místo `ulong?[]`; `DestWriter.Verify` hashuje všemi zvolenými v jednom průchodu (`Hashing.HashFile(path, buf, kinds)`);
`OffloadReport.Build` a `Mhl.Write` podle `FileHashes`. Ověření zálohy (§2.4) už MD5 umí.

**Výkon.** xxh64 ~ 10 GB/s, MD5 ~ 0,6–0,8 GB/s na jádro (CNG) → u CFexpress karet (1,5 GB/s) MD5 kopii zpomalí;
u SD/SSD přes USB 3 ne. Měření `offload bench` rozšířit o `--hash md5`.

**Okrajové případy.** Zrušení → jako dnes; starší `offload verify` (Win < 0.3, Mac) ověří xxh64 a MD5 ignoruje; doplnění (§2.2) použije algoritmy původní zálohy (z jejího reportu), ne aktuální volbu.

**Testy.** `HashingTests` (MD5 vektory), `EngineTests` pro oba režimy (report klíče, `.md5`, MHL prvky, zfalšovaný
bajt na cíli selže v obou algoritmech), JSON golden file pro `both`.

**Mac.** `Hash.swift` + CommonCrypto MD5, report klíče, `.md5`, MHL, `verify` z MHL.

## 4. Skupina C — nastavení, oznámení, distribuce (plán 8)

### 4.1 Projekty (funkce 11)

**Cíl.** Pojmenované předvolby (např. „Film X“, „Reklama Y“) s cíli a volbami; rychlé přepínání.

**Co projekt obsahuje** (podmnožina nastavení): `destRoots`, `verify`, `analyzeClips`, `hash`, `naming`, `outputs`
(denní report, CSV, ALE), `notify`, `proxy` (vč. LUTu, `lutRules`, `extraFolder`, `brawGen4ToGen5`). Globální zůstává:
`tools`, `portNames`, `updates`, okno.

**Úložiště (R17).** Horní klíče `settings.json` = profil „Bez projektu“; `projects[]` = pojmenované profily stejného
tvaru; `activeProject` = jméno nebo `null`. V paměti VM upravuje **aktivní profil** (horní klíče nebo projekt);
každá změna se uloží do něj (dnešní `SettingsChanged` 400 ms). Fronta drží klon (`SettingsStore.Clone` v `StartJobs`),
takže přepnutí projektu nikdy nezmění zařazené úlohy.

**UX.** Nahoře v levém panelu: „Projekt: [Bez projektu ▾]“. Menu *Projekt*: „Uložit jako projekt…“ (nový projekt
z aktuálních hodnot, stane se aktivním), „Přejmenovat…“, „Smazat…“ (potvrzení „Smazat projekt Film X? Nastavení
projektu se ztratí, zálohy na discích zůstanou.“; smazání aktivního → „Bez projektu“). Dialog jména: „Název projektu“;
chyby „Projekt s tímto názvem už existuje.“, „Název nesmí být prázdný.“ Titulek okna „Offload — Film X“.
Jména: oříznutá, 1–60 znaků, unikátní bez ohledu na velikost písmen, „Bez projektu“ je rezervované.
Historie zapisuje `project` (§5.2) → „další v řadě“ (§2.3) a denní report podle projektu.

**CLI.** `--project <název>`: CLI načte `settings.json` (`AppData.Folder`, respektuje `OFFLOAD_SETTINGS_DIR`), vezme
profil projektu jako výchozí hodnoty; **výslovně zadané volby vyhrávají** (`ParseResult.GetResult(option)` není
implicitní); `-d` není povinné, když projekt má cíle. Neznámý → „Projekt „X“ neexistuje. Projekty: Film X, Reklama Y.“
(kód 1).

**Okrajové případy.** Cíl projektu odpojený → jako dnes červeně; ručně upravený soubor se dvěma stejnými jmény →
platí poslední; poškozená sekce `projects` → bez projektů, ostatní nastavení zůstane (sekční načítání `SettingsStore`);
přepnutí během běžící úlohy → nic se jí nestane.

**Testy.** `ProjectsTests` (round-trip, přepnutí tam a zpět beze ztráty, přejmenování aktivního, smazání aktivního,
duplicitní jména, neznámé klíče zachované), CLI test přednosti voleb (`--project` + `--proxy-codec`).

**Mac.** `AppSettings` + `UserDefaults` klíč `projects`; stejné chování přepínače.

### 4.2 Vlastní pravidla LUTů a BRAW Film Gen 4 (funkce 12)

**Cíl.** Vlastní pořadí „tahle kamera / cívka / složka → tenhle LUT“, před automatikou; Gen 4 BRAW s LUTem Gen 5.

**Pravidlo** (`proxy.lutRules`, pořadí = priorita):

```json
{ "match": "camera", "value": "FX6", "lut": "sony-slog3-sgamut3cine", "lutPath": null }
```

| `match` | Porovnání |
|---|---|
| `camera` | `ClipInfo.Camera` (výrobce + model) obsahuje `value`, bez ohledu na velikost písmen |
| `serial` | `ClipInfo.CameraSerial` = `value` (bez ohledu na velikost písmen) |
| `reel` | cívka (`ReelResolver`, §3.4) začíná `value` (`A` = celá kamera A, `A00` …) |
| `folder` | vzor s `*` a `?` nad `<karta>/<relativní složka klipu>` s `/` |

Výsledek: právě jedno z `lut` (id vestavěného nebo `"none"`) a `lutPath` (vlastní `.cube`). Neplatné pravidlo
(prázdná hodnota, obojí/nic) se při načtení zahodí.

**Jádro.** `LutRules.Match(rules, clip, info, reel, cardName) → (int Index, LutRule)?`; `LutResolver.Resolve` v režimu
`Auto` zkusí pravidla **před** `Detect` (R18). Report: `lut.source = "rule"`, `reason` „pravidlo 2: kamera obsahuje
„FX6““. Nové `ClipInfo.CameraSerial`: Sony XML `<Device serialNo>` (`SonyXml` se rozšíří), ffprobe tagy
(`com.apple.quicktime.camera.identifier`, `camera_serial_number`), ARRI z metadat art-cmd, BRAW z metadat SDK —
u ARRI a BRAW **ověří vzorky**; neznámé číslo → pravidla `serial` nikdy nesedí. `ProxyRenderer.PrepareLut` kopíruje
vlastní LUT podle cesty (`lut-custom-<8 hex sha1 cesty>.cube`), ne jen jeden `look.cube`.
`ProxyPreflight`: chybějící `.cube` z pravidla blokuje start: „LUT z pravidla 2 nenalezen: show.cube“.

**UX.** V sekci *Proxy* pod LUTem tlačítko „Pravidla LUTů… (2)“ (vidět jen u „Automaticky podle kamery“) → okno
„Vlastní pravidla LUTů“: řádky `[Podle ▾: Kamera (výrobce/model) | Sériové číslo kamery | Cívka (začátek) | Složka
(vzor s * a ?)] [hodnota] → [LUT ▾: vestavěné… | Bez LUT | Vlastní…] [↑] [↓] [✕]`, tlačítko „Přidat pravidlo“,
nápověda „Pravidla se zkoušejí shora dolů, před automatickým rozpoznáním kamery.“, *OK* / *Zrušit*. Pravidla jsou
součástí projektu.

**CLI.** `--lut-rule <match>=<hodnota>:<lut|cesta.cube|none>` (opakovatelné, pořadí = priorita), např.
`--lut-rule camera=FX6:sony-slog3-sgamut3cine`.

**BRAW Film Gen 4 → Gen 5.** `braw-decode` nová volba `--gen5` (pro `--info` i snímky): naklonuje atributy klipu
(`CloneClipProcessingAttributes`), `SetClipAttribute(ColorScienceGen, VARIANT u16 = 5)` (slot 4), pak zpětně přečte
gamu/gamut/generaci; snímky jdou přes `CreateJobDecodeAndProcessFrame(clipAttrs, null)` (dnes `0, 0`). `--info --gen5`
vrací účinné hodnoty + `"colorScienceGenOriginal": 4`. Nepodaří-li se nastavit → varování na stderr, dekóduje se jako
Gen 4 (bez LUTu, jako dnes). `BrawBackend` předá `--gen5`, když `proxy.brawGen4ToGen5`; `LutResolver.Braw`: účinná
Gen 5 → BMD LUT s důvodem „Blackmagic RAW: Film Gen 4 → Gen 5 (převod SDK)“. Volba v sekci *Dekodéry*:
`[ ] Blackmagic RAW Film Gen 4 převést na Gen 5 (použije se LUT Gen 5)` + šedě „ověří až vzorek .braw Gen 4“;
výchozí vypnuto (R26). CLI `--braw-gen5`.

**Testy.** `LutRulesTests` (každý `match`, pořadí, neplatná pravidla, jen v Auto, reason), `SonyXmlTests` se `serialNo`,
renderer test dvou různých vlastních LUTů v jedné úloze, `BrawDecodeTests` parsování `--gen5`, `LutResolverTests`
Gen 4 s převodem / bez převodu. Skutečný převod: `[BrawSampleFact]` až se vzorkem Gen 4.

**Mac.** `LutResolver` pravidla; Mac `braw-decode` C++ stejná volba `--gen5`.

### 4.3 Oznámení (funkce 13)

**Cíl.** Vědět o konci úlohy, i když okno není vidět — v PC (oznámení Windows) a volitelně na telefonu (ntfy).

**Výsledek úlohy (`JobOutcome`)** — čistá funkce nad `JobStatus` + reportem:

| Výsledek | Kdy | Titulek / text (Windows) |
|---|---|---|
| Hotovo | kopie OK + ověřeno + stage 2 bez selhání | „Offload: A001 hotovo“ / „Ověřeno na 2 discích. Proxy: 24 hotovo.“ |
| Pozor | kopie OK, ale neověřeno, nebo proxy/náhledy selhaly, nebo proxy přeskočeny pro místo, nebo „5 souborů z dřívější zálohy nebylo ověřeno“ | „Offload: A001 — vyžaduje pozornost“ / „Kopie je ověřená, ale 2 proxy selhaly.“ |
| Chyba | `Failed` nebo selhané soubory | „Offload: A001 — CHYBA“ / „3 soubory selhaly — karta NENÍ zálohovaná.“ (nebo text chyby před kopií) |
| Kartu lze vysunout | stage 1 ověřená a následuje stage 2 (jen Windows oznámení, ne ntfy) | „Offload: A001 — kartu lze vysunout“ / „Proxy se ještě renderují.“ |
| Zrušeno | — | bez oznámení |

**Windows (R19).** `TrayNotifier` v `Offload.App`: `Shell_NotifyIconW` (`NIM_ADD` s ikonou aplikace, pak `NIM_MODIFY`
s `NIF_INFO`, `NIIF_INFO` / `NIIF_WARNING` / `NIIF_ERROR`), okno zpráv přes `HwndSource` (jako `VolumeWatcher`).
Windows 10/11 ukáže moderní oznámení i v Centru oznámení bez registrace AUMID. Ikona v oznamovací oblasti zůstane
do zavření aplikace; klik ji obnoví a aktivuje hlavní okno. Oznámení se ukáže, jen když hlavní okno **není v popředí**
(jinak stačí dnešní zvuk + blikání, které zůstávají). Volba `notify.windows` (výchozí zapnuto).

**ntfy (R20).** `NtfyClient.SendAsync(server, topic, title, message, priority, tags)`: `POST <server>/` s JSON
`{"topic","title","message","priority","tags"}` (UTF-8), `HttpClient` s časovým limitem 10 s, jeden pokus, chyba
jen do logu. Priorita: hotovo 3, pozor 4, chyba 5; tagy `white_check_mark` / `warning` / `rotating_light`.
Text: **jen název karty + výsledek + počty** (bez cest, jmen souborů, názvu PC). Nastavení `notify.ntfy`:
`enabled` (false), `server` (`https://ntfy.sh`), `topic` (""). Bez přihlašovacího tokenu (uložit ho bez DPAPI by
znamenalo prostý text; DPAPI v .NET Core = NuGet) — později.

**UX.** *Nástroje* → „Oznámení…“: `[x] Oznámení Windows`, `[ ] Posílat na telefon přes ntfy`, *Server*, *Téma*,
tlačítko „Poslat zkušební oznámení“ (výsledek „Odesláno.“ / „Nepodařilo se: …“), poznámka „Téma volte nehádatelné —
kdo ho zná, oznámení čte. Posílá se jen název karty a výsledek, žádné cesty.“ Telegram: zatím ne (potřebuje token
bota) — možné později.

**CLI.** `--ntfy <url>` (např. `https://ntfy.sh/muj-tajny-kanal`, server + téma z cesty) nebo z `--project`;
Windows oznámení CLI neposílá.

**Testy.** `JobOutcomeTests` (tabulka), `NotifyTextTests` (žádné `\` ani `:` cesty v textu ntfy), `NtfyClientTests`
s `HttpListener` na `127.0.0.1` náhodném portu (tvar JSON, UTF-8, priorita; server neodpovídá → zrušení po limitu,
úloha nečeká), `TrayNotifier` ručně (položka ručního testu).

**Mac.** Mac má notifikace (`UNUserNotificationCenter`); převezme `JobOutcome` texty a ntfy klienta.

### 4.4 Kontrola aktualizací (funkce 14)

**Cíl.** Tiše dát vědět, že je nová verze, bez stahování a bez veřejného GitHubu (repo je soukromé).

**Manifest.** `{ "version": "0.4.0", "url": "https://… nebo \\\\NAS\\Offload\\", "notes": "Denní report, MD5…" }`
(UTF-8 JSON). Zdroj `updates.manifest` = http(s) URL nebo cesta k souboru (místní, UNC); výchozí `null` = vypnuto.

**Jádro.** `UpdateChecker.CheckAsync(source, currentVersion, clock, state) → UpdateInfo?`: nejvýš jednou za 24 h
(stav v `%APPDATA%\Offload\update-check.json`: `lastCheck` UTC, `lastVersion`; `lastCheck` v budoucnosti → zkontrolovat),
5 s po startu, mimo UI vlákno; HTTP s limitem 10 s, soubor přes `Task.Run` + `WhenAny` 5 s (mrtvá UNC cesta by
blokovala desítky sekund). Porovnání `MAJOR.MINOR.PATCH` (přípona za `-` se ignoruje). Nikdy nic nestahuje
a nespouští. `User-Agent: Offload/<verze>`, žádné identifikátory.

**UX.** Vpravo v řádku menu tichý odkaz „Nová verze 0.4.0“ → okno s poznámkami a tlačítkem *Otevřít* (`Shell.Open(url)`
— prohlížeč nebo Průzkumník). *Nástroje* → „Aktualizace…“: pole „Adresa manifestu (web, NAS nebo sdílená složka)“,
„Zkontrolovat teď“, poslední výsledek („Máte nejnovější verzi 0.4.0.“ / „Manifest nejde načíst: …“).

**Číslování verzí (R22).** Jediný zdroj `Directory.Build.props` (`Version` `0.4.0`, `InformationalVersion` `0.4.0-win`);
`OffloadVersion.String` se čte z `AssemblyInformationalVersionAttribute` (test hlídá tvar `x.y.z-win`). Report dál
`version` = `x.y.z-win`. Plán 6 → 0.2.0, 7 → 0.3.0, 8 → 0.4.0, opravy → patch, 1.0.0 po ostrém E2E na kartě.

**Vydání verze** (do `windows/README.md`): 1) zvýšit verzi v `Directory.Build.props`; 2) `build.ps1 [-Sign] -ReleaseUrl
<kam zip dáte>`; vznikne `build\Offload-win-x64-<verze>.zip` a `build\offload-update.json` (verze z props, `url`
= `-ReleaseUrl`, `notes` z `-Notes` nebo prázdné); 3) zip + manifest nahrát na web / NAS; manifest jako poslední
(nejdřív musí existovat zip).

**Testy.** `UpdateCheckerTests`: porovnání verzí (tabulka vč. `0.10.0 > 0.9.0`, přípony), poškozený manifest, 24 h
s falešnými hodinami, posun hodin zpět, zdroj soubor v temp, `HttpListener`, časový limit.

**Mac.** Stejný manifest (jiné pole nebo druhý manifest `offload-update-mac.json` — rozhodne se u Macu).

### 4.5 Digitální podpis (funkce 15)

**Cíl.** Podepsat exe, aby Windows/SmartScreen ukazoval vydavatele; nic nekupovat.

**build.ps1.** Nové parametry: `-Sign`, `-CertThumbprint <sha1>` (úložiště `CurrentUser\My`, pak `LocalMachine\My`;
funguje i s hardwarovým tokenem) **nebo** proměnné `OFFLOAD_SIGN_PFX` + `OFFLOAD_SIGN_PFX_PASSWORD`, `-TimestampUrl`
(výchozí `http://timestamp.digicert.com`). signtool: `$env:SIGNTOOL` → `PATH` → nejnovější
`${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\signtool.exe`. Podepíše `Offload.exe`, `bin\offload.exe`,
`bin\braw-decode.exe` (R23) **před** zabalením: `signtool sign /fd SHA256 /td SHA256 /tr <url> (/sha1 … | /f … /p …)
/d "Offload" <soubory>`, pak `signtool verify /pa /all`. Razítko: 3 pokusy po 5 s.
Chyby (build skončí): „signtool nenalezen — nainstalujte Windows SDK (Signing Tools for Desktop Apps), nebo nastavte
SIGNTOOL.“, „Certifikát s otiskem … nenalezen v CurrentUser\My ani LocalMachine\My.“, „Chybí OFFLOAD_SIGN_PFX_PASSWORD.“,
„Časové razítko se nepodařilo získat z …“, „Podpis nejde ověřit: …“. Heslo se nikdy nevypisuje (pozn.: signtool
`/p` je vidět v seznamu procesů — na sdíleném stroji raději úložiště/token).

**Dokumentace** (`windows/README.md`, kapitola Podepisování): OV certifikát (ověření firmy; ~200–500 USD/rok, od 2023
klíč povinně na tokenu/HSM), EV (~300–700 USD/rok, také token; od 2024 už nedává okamžitou reputaci SmartScreen —
ověřit aktuální stav), cloudové podepisování Microsoft (Azure Trusted Signing / Artifact Signing, ~10 USD/měsíc;
podmínky podle země a historie firmy — ověřit pro ČR), Certum (polská CA, levnější OV pro jednotlivce). Reputace
SmartScreen se buduje počtem stažení — první verze mohou varovat i s podpisem. Self-signed certifikát SmartScreenu
nepomůže. Ceny orientační, k datu nákupu ověřit.

**Testy.** Pester-like kontrola není v repu; ruční: build bez `-Sign` beze změny, `-Sign` bez signtool → jasná chyba,
s testovacím self-signed PFX → `signtool verify /pa` projde (jen na stroji s SDK).

**Mac.** Mac má vlastní (`codesign` + notarizace) — mimo tento návrh.

## 5. Průřezové věci

### 5.1 Nové klíče `settings.json` (vše volitelné, chybí → výchozí)

| Klíč | Typ | Výchozí | Plán |
|---|---|---|---|
| `naming.suggestFromClips` | bool | `true` | 6 |
| `hash` | `"xxh64"` \| `"both"` | `"xxh64"` | 7 |
| `outputs.dayReport` | bool | `true` | 7 |
| `outputs.clipListCsv` | bool | `true` | 7 |
| `outputs.clipListAle` | bool | `false` | 7 |
| `proxy.lutRules` | pole pravidel (§4.2) | `[]` | 8 |
| `proxy.brawGen4ToGen5` | bool | `false` | 8 |
| `notify.windows` | bool | `true` | 8 |
| `notify.ntfy.enabled` / `.server` / `.topic` | bool / string / string | `false` / `https://ntfy.sh` / `""` | 8 |
| `updates.manifest` | string \| null | `null` | 8 |
| `projects` | pole `{name, destRoots, verify, analyzeClips, hash, naming, outputs, notify, proxy}` | `[]` | 8 |
| `activeProject` | string \| null | `null` | 8 |

`proxy.*` klíče se dostanou i do `offload_report.json` (`proxy` objekt je serializované `ProxySettings`) — přidané,
Mac je ignoruje. Zapisuje se do `docs/FORMATY.md` (oddíl nastavení) a do ROZDILY.

### 5.2 `history.json` (místní, jen tento počítač)

`%APPDATA%\Offload\history.json`, UTF-8 bez BOM, odsazené, data ISO 8601 UTC na sekundy:

```json
{
  "version": 1,
  "entries": [
    {
      "id": "20261006T081500Z-3f2a9c1b",
      "card": { "identity": "\\\\?\\Volume{…}\\|1A2B3C4D", "fsSerial": "1A2B3C4D", "capacity": 256060514304,
                "label": "A001", "filesystem": "exFAT" },
      "source": "E:\\",
      "cardName": "A001",
      "date": "2026-10-06",
      "project": null,
      "finished": "2026-10-06T08:15:00Z",
      "success": true,
      "verified": true,
      "hashAlgorithms": ["xxh64"],
      "files": 1234,
      "bytes": 512000000000,
      "fingerprint": "<sha256 hex>",
      "shape": "<sha256 hex>",
      "fileList": "history/20261006T081500Z-3f2a9c1b.files.gz",
      "destinations": [ { "root": "D:\\Zaloha", "cardPath": "D:\\Zaloha\\2026-10-06\\A001" } ],
      "generations": [ { "seq": 1, "kind": "full", "finished": "2026-10-06T08:15:00Z", "files": 1234,
                         "bytes": 512000000000, "verified": true } ],
      "reels": ["A001"]
    }
  ]
}
```

- `card` u ručně přidané složky: `null`. `failedFiles` (počet) jen když `success` = false.
- Doplnění upraví existující záznam (nová položka `generations`, `files`/`bytes`/`fingerprint`/`shape`/`fileList`
  = sjednocení, `finished` = doplnění).
- Prořezání: nejvýš 5 000 záznamů, nejstarší podle `finished` se smažou i s `.files.gz`.
- Neznámá vyšší `version` → jen číst, nezapisovat (log).
- Zapisuje GUI i CLI (R27), pod mutexem (§2.1). Do `docs/FORMATY.md` jako „Místní soubory“ (Mac převezme stejný tvar).

### 5.3 Souhrn změn formátu na cíli (do `docs/FORMATY.md`)

| Soubor / klíč | Plán | Mac při čtení |
|---|---|---|
| `_REPORT/offload_report_NNNN.json/.txt`, `<karta>_report_NNNN.html/.pdf`, klíč `generation` | 6 | ignoruje (nečte) |
| `<karta>.xxh64` rozšířený o soubory doplnění | 6 | čte beze změny |
| `ascmhl/NNNN_<karta>_…Z.mhl`, řetěz s víc položkami | 6 | `ascmhl` OK; Mac `verify` čte jen `.xxh64` — v pořádku |
| `_REPORT/<karta>_verify_<stamp>Z.html/.pdf`, `offload_verify_<stamp>Z.json` (u cizí MHL, která `_REPORT` neignoruje: `<datum>/_REPORT/<karta>_verify_<stamp>Z.*`) | 6 | ignoruje |
| `<datum>/_REPORT/<datum>_day_report.html/.pdf`, `<datum>_clips.csv/.ale` | 7 | ignoruje; `UniqueCardFolder` nekoliduje |
| `_REPORT/<karta>_clips.csv/.ale` | 7 | ignoruje |
| `hashAlgorithms`, `files[].md5`, `<karta>.md5`, MHL `<md5>` | 7 | ignoruje MD5, xxh64 ověří dál |
| `clips[].info.reel`, `clips[].info.cameraSerial`, `clips[].info.colorScienceGenOriginal` | 7, 8 | ignoruje |
| `clips[].lut.source = "rule"` | 8 | ignoruje (`lut` celý) |
| `proxy.lutRules`, `proxy.brawGen4ToGen5` | 8 | ignoruje |

### 5.4 Migrace a kompatibilita

- **Zachování neznámých klíčů (R24, plán 6 úkol 2):** `AppSettings` a `ProxySettings` dostanou `[JsonExtensionData]`;
  `SettingsStore.Load` po sekcích je přenese, `Save` je zapíše zpět. Verze 0.2.0+ tak nesmaže klíče 0.4.0.
  Verze 0.1.0 by je smazala — uživatel má 0.1.0 jen na vývojovém PC; v návodu „nevracejte se pod 0.2.0“.
- `history.json` neexistuje → prázdná historie; karty zálohované před plánem 6 se nepoznají (není z čeho).
  Nápad mimo rozsah: import historie z existujících `_REPORT` na cílech.
- Report: čtení starších reportů (bez `generation`, `md5`, `reel`) vždy funguje (`CardRecord`).

### 5.5 Výkon (rozpočty)

| Operace | Rozpočet | Jak |
|---|---|---|
| otisk karty 10 000 souborů (výčet + SHA-256) | ≤ 3 s USB 3 čtečka, ≤ 10 s USB 2; UI nikdy | `Scanner.Scan` na `Task.Run`, zrušitelné |
| načtení `history.json` (5 000 záznamů ≈ 5 MB) | ≤ 300 ms, při startu mimo UI | jednou, pak v paměti; zápis jen po úloze |
| `.files.gz` 10 000 souborů | ~200 KB, čtení ≤ 100 ms | jen když se `fingerprint` liší |
| `SpacePlanner.Check` | ≤ 1 ms (3×/s na UI vlákně) | jen paměť |
| denní report 20 karet | HTML ≤ 1 s, PDF 2–5 s na cíl | pracovní vlákno po úloze |
| MD5 | ~0,7 GB/s na jádro | vlastní vlákno; nebrzdí xxh64 |
| ověření zálohy | rychlost disku (čtení bez cache) | kopírovací fronta, ne souběžně s kopií |

### 5.6 Soukromí

- `history.json` obsahuje cesty a názvy karet — jen v `%APPDATA%` uživatele, nikam se neposílá.
- ntfy: název karty + výsledek + počty; bez cest, jmen souborů, názvu PC; veřejný ntfy.sh čte každý, kdo zná téma.
- Kontrola aktualizací: jen GET manifestu, `User-Agent` s verzí, žádná telemetrie.
- Podpis: heslo PFX jen v proměnné prostředí, nikdy v logu.

### 5.7 Vlákna — shrnutí

UI vlákno jen čte stav v paměti. Mimo UI: inspect karty (Task na zdroj), kontrola místa (z cache), historie
(načtení Task, zápis na vlákně úlohy), ověření zálohy (kopírovací fronta), denní report (vlákno úlohy / Task),
ntfy (`HttpClient` async, fire-and-forget s logem), aktualizace (Task po startu), tray (UI vlákno, ale bez I/O).

## 6. Rozdělení do plánů

### 6.1 Plán 6 — karty a bezpečnost dat (skupina A) → 0.2.0

1. **České hlášky + `PreflightCode`** — `Msg`, tabulka §2.6, testy; první, ať další úkoly píšou česky.
2. **Zachování neznámých klíčů v nastavení** — `JsonExtensionData`, test round-tripu (R24).
3. **Otisk karty a seznam souborů** — `CardFingerprint`, `FileListStore` (gz), FAT tolerance.
4. **`HistoryStore`** — formát §5.2, mutex, poškozený soubor, prořezání.
5. **Zápis historie z úlohy** — identita karty v `OffloadJob`, `RecordHistory`, CLI `--no-history`.
6. **`CardInspector` + `HistoryMatcher`** — sken → otisk → shoda → podklady pro název a místo.
7. **GUI: řádek historie u karty** — životní cyklus inspectu, zrušení při výměně karty.
8. **GUI: okno „Karty už jsou zálohované“** před celou kopií.
9. **Název karty z klipů** — `ReelNames`, `ReelSuggestion` (+ další v řadě), zástupný text, `naming.suggestFromClips`, CLI `--name auto`.
10. **`MhlReader` + `HashAlgo`** — generace, řetěz, C4, všechny hashe BCL.
11. **`BackupVerifier` + report ověření** — JSON/HTML/PDF (`PdfReport.WriteHtml`), CLI `verify` přepsat.
12. **GUI ověření** — menu, `VerifyJob` ve frontě, `VerifyItemViewModel`, výběr složky dne.
13. **`CardRecord` (jádro) + `AppendPlanner`** — rozdíl, konflikty, generace, neověřené.
14. **Engine v režimu doplnění** — `OffloadPlan.Mode/Generation`, `_NNNN` jména, `.xxh64` sloučení, MHL generace + řetěz, proxy bez přepisu, historie.
15. **Doplnění v GUI a CLI** — checkbox u karty, `CanStart` bez *Cílů*, `offload append`.
16. **`SpaceEstimator` + `SpacePlanner`** — vzorce §2.5, engine parametr, přepočet před proxy.
17. **GUI místo** — červená/oranžová hláška, čekání na inspect při startu.
18. **Dokumentace** — FORMATY, ROZDILY, VYVOJ-WINDOWS, VYVOJ-MAC (naplánováno), návod (kap. 3, 5, 8, 10, 11, 12; věta „hlášky jsou zatím anglicky“ pryč), verze 0.2.0.
19. **Závěrečná revize + seznam ručního testu** (karta ve dvou čtečkách, DST posun na FAT32, `ascmhl verify` po doplnění).

**Review Focus (5 nejrizikovějších vstupů):**
1. Karta s 10 000+ soubory a hlubokým stromem, vytažená uprostřed skenu.
2. FAT32 karta po změně letního/zimního času (otisk, konflikty doplnění).
3. Doplnění do složky, jejíž MHL řetěz vytvořil Mac / jiný nástroj, má víc generací nebo se cíle liší.
4. `history.json` poškozený, ze zrušené úlohy nebo zapisovaný zároveň GUI a CLI.
5. Místo: dvě karty do fronty na tentýž disk + běžící úloha + složka pro proxy na stejném svazku.

### 6.2 Plán 7 — výstupy pro produkci a střih (skupina B) → 0.3.0

1. **`CardRecord` úplný** — sloučení generací, Mac reporty, chybné JSON.
2. **`ReelResolver` + `ClipInfo.Reel`** — tagy ffprobe, vzor jména, karta.
3. **Proxy pro přelinkování** — `reel_name`, všechny zvukové stopy, poznámka u `_2`.
4. **Hash jádro** — `HashKind`, souběžné `SourceHasher`y, `FileHashes`, `DestWriter.Verify` víc algoritmů.
5. **Hash výstupy** — report klíče, `.md5`, MHL `<md5>`, HTML/TXT, nastavení `hash`, GUI, CLI `--hash`, `bench --hash`.
6. **Seznam klipů** — CSV/ALE writer, per karta, volby `outputs`, CLI `--no-clip-list`/`--ale`.
7. **Denní report** — builder, HTML, PDF, denní CSV/ALE.
8. **Regenerace denního reportu** — po úloze, zámky, menu „Denní report…“, CLI `day-report`.
9. **Drobnosti** — české zobrazení chyb souborů v HTML (`size mismatch` …), návod kap. 6, 7, 10, 11.
10. **Dokumentace + 0.3.0 + závěrečná revize.**

**Review Focus:**
1. Klipy bez TC, drop-frame TC, smíšené fps na kartě/dni (ALE).
2. Názvy s čárkou, uvozovkou, novým řádkem, tabulátorem a diakritikou (CSV/ALE).
3. Dvě regenerace denního reportu najednou + Mac zapisující do stejného dne.
4. Propustnost MD5 na rychlé kartě a ověření v režimu `both`.
5. Zdroje s víc zvukovými proudy (Sony MXF 4–8 mono stop) a `reel_name` v `tmcd`.

### 6.3 Plán 8 — nastavení, oznámení, distribuce (skupina C) → 0.4.0

1. **Profily/projekty v `AppSettings`** — úložiště §4.1, přepínání, testy.
2. **GUI projektů** — přepínač, menu, dialogy, titulek.
3. **CLI `--project`** — přednost výslovných voleb.
4. **Pravidla LUTů jádro** — `LutRules`, `LutResolver`, `source: rule`, `CameraSerial`, LUT podle cesty v rendereru, preflight.
5. **Editor pravidel + CLI `--lut-rule`.**
6. **BRAW `--gen5`** — braw-decode, `BrawBackend`, `LutResolver`, checkbox, `--braw-gen5` (čeká na vzorek).
7. **Oznámení jádro** — `JobOutcome`, texty, `NtfyClient` + `HttpListener` testy.
8. **Tray + dialog Oznámení + CLI `--ntfy`.**
9. **Kontrola aktualizací** — `UpdateChecker`, stav, odkaz v menu, dialog.
10. **build.ps1** — verzovaný zip, manifest `-ReleaseUrl`, `-Sign`; README (vydání, podepisování); `OffloadVersion` z assembly.
11. **Dokumentace + 0.4.0 + závěrečná revize.**

**Review Focus:**
1. Přepnutí projektu, když jsou úlohy ve frontě a běží render proxy.
2. Ručně upravený / starší `settings.json` (duplicitní projekty, neznámé klíče, chybějící sekce).
3. ntfy server nedostupný, pomalý, TLS chyba, téma s diakritikou — úloha nikdy nečeká.
4. Manifest na odpojené UNC cestě, poškozený JSON, verze `0.10.0` vs `0.9.0`, hodiny posunuté zpět.
5. `build.ps1 -Sign` bez signtoolu, s prošlým certifikátem, nedostupným razítkem.

### 6.4 Závislosti

- Plán 7 potřebuje z plánu 6: `CardRecord` jádro a jména `_NNNN` (denní report, seznam klipů), `MhlReader`/`HashAlgo`
  (ověření MD5), `ReelNames` (vzor jména pro `ReelResolver`).
- Plán 8 potřebuje z plánu 7: `ReelResolver` (pravidla `reel`), klíče `hash` a `outputs` (obsah projektu);
  z plánu 6: `history.project` (pole existuje od plánu 6, plní se v plánu 8), `naming`.
- Funkce 13–15 jsou nezávislé a mohou jít i dřív, pokud by uživatel chtěl změnit pořadí.

## 7. Otevřené otázky (jen kde je špatný odhad drahý)

| # | Otázka | Výchozí, když se neozvete |
|---|---|---|
| O1 | Doplnění, když je dostupná jen část dřívějších cílů (jeden disk odvezený do produkce): zastavit, nebo doplnit jen dostupné a ten druhý později? | **zastavit** (R7) — kopie se nesmí rozejít bez vědomí uživatele |
| O2 | Oddělovač CSV: čárka (Resolve, mezinárodní, zadání) nebo středník (český Excel ho otevře rovnou)? | **čárka**; návod popíše import v Excelu přes *Data → Z textu/CSV* |
| O3 | Režim „jen MD5“: opravdu bez xxHash64 (Mac a starší `offload verify` ho neověří), nebo MD5 vždy **navíc** k xxh64 (volba by byla jen „xxh64“ / „xxh64 + MD5“)? | **rozhodnuto: MD5 jen navíc k xxh64** (R16) |
| O4 | Umístění denního reportu: `<cíl>\<datum>\_REPORT\` (výchozí), nebo jinde (např. `<cíl>\_DAY\<datum>\`)? Mění formát na disku pro obě verze. | **`<cíl>\<datum>\_REPORT\`** |

## 8. Mac — co převzít (pro `docs/VYVOJ-MAC.md`, sekce Naplánováno)

| Funkce | Mac převezme |
|---|---|
| 1 Historie | `history.json` (§5.2) v Application Support, klíč karty z UUID svazku + kapacita, texty řádku karty, dialog před celou kopií |
| 2 Doplnění | `AppendPlanner`, generace MHL N + řetěz, `offload_report_NNNN.*`, rozšíření `.xxh64`, `offload append` |
| 3 Název | vzory `ReelNames`, další v řadě, zástupný text |
| 4 Ověření | čtení MHL (všechny generace a hashe), report ověření `_verify_<stamp>Z`, GUI |
| 5 Místo | `SpaceEstimator`/`SpacePlanner`, přepočet před proxy |
| 6 Hlášky | české texty `Msg` v Engine a CLI |
| 7 Denní report | `<datum>/_REPORT/<datum>_day_report.*`, regenerace po úloze |
| 8 Seznam klipů | CSV/ALE stejných sloupců |
| 9 Proxy | `reel_name`, všechny zvukové stopy |
| 10 MD5 | volba, `md5` klíče, `.md5`, MHL `<md5>`, verify |
| 11 Projekty | `projects`/`activeProject` v `UserDefaults` |
| 12 Pravidla LUTů | `lutRules`, `source: rule`; `braw-decode --gen5` v C++ |
| 13 Oznámení | texty `JobOutcome`, ntfy |
| 14 Aktualizace | manifest (formát; Mac vlastní soubor) |
| 15 Podpis | mimo (Mac `codesign`/notarizace) |
