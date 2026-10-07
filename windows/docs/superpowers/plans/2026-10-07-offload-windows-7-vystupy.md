# Offload pro Windows — plán 7: výstupy pro produkci a střih (úkoly 1–5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Skupina B návrhu 15 vylepšení — denní report natáčecího dne, seznam klipů CSV a Avid ALE, proxy připravené k přelinkování (reel name, všechny zvukové stopy) a MD5 navíc k xxHash64; výsledek je verze 0.3.0.

**Architecture:** `CardRecord` (plán 6) se stane úplným zdrojem pravdy o složce karty (klipy a součty všech generací, reporty z Macu); nad ním stojí nový jmenný prostor `Offload.Core.Outputs` (`ClipList`, `DayReport`, `DayReportService`, `OutputSettings`). Hashování kopie dostane `HashKind` (xxh64 vždy, MD5 navíc) s jedním `SourceHasher` na algoritmus a ověřením všemi součty v jednom čtení; proxy dostanou cívku (`ReelResolver` → `reel_name` ve stopě timecodu) a všechny zvukové proudy. Úloha (`OffloadJob`) po sobě zapíše seznamy klipů a znovu vytvoří denní report každého cíle; GUI jen váže (volby, menu), logika se testuje v Core.

**Tech Stack:** C# / .NET 10, WPF, xUnit, `System.IO.Hashing` (už v projektu), BCL (`IncrementalHash` MD5, `SHA1`, pojmenovaný `Mutex`, `SemaphoreSlim`), ffmpeg/ffprobe 8.1 jen v testech `[FfmpegFact]`, Microsoft Edge jen v testech `[EdgeFact]` (kolekce „Edge“).

**Spec:** `windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md` — skupina B (§3.1–3.5), průřezové věci (§5), plán 7 (§6.2). Rozhodnutí řídicí relace platí: **R15** burn-in zůstává výchozí zapnutý (beze změny), **R16** MD5 jen navíc k xxh64 — nastavení `hash` = `"xxh64"` | `"both"`. Plán má dvě části: tento soubor (úkoly 1–5) a `windows/docs/superpowers/plans/2026-10-07-offload-windows-7-vystupy-2.md` (úkoly 6–10).

## Global Constraints

- Žádný nový NuGet balíček — jen BCL .NET 10 (`System.Security.Cryptography` pro MD5 a SHA-1, `System.IO.Hashing` už je), P/Invoke.
- Texty UI česky, doslova podle návrhu; identifikátory v kódu anglicky. Logy nástrojů (`app.log`, stderr ffmpeg), TXT report (sdílený s Macem) a `--help` zůstávají anglicky (§2.6); chyby souborů v `offload_report.json` zůstávají anglicky (sdílený s Macem), HTML/PDF je zobrazí česky (úkol 9).
- UI vlákno nikdy nečeká na disk ani síť — denní report na vyžádání `Task.Run` + výsledek přes dispatcher (jako `PickAndVerify`); po úloze se tvoří na vlákně úlohy.
- Na kartu se nikdy nezapisuje — seznamy klipů a denní report čtou jen reporty na cílech.
- Doplnění nikdy nepřepíše existující soubor na cíli (reporty `_NNNN`, generace MHL, proxy → `_2`). Přepisovat celé (dočasné jméno + nahrazení) se smí jen odvozené soubory: `<karta>.xxh64`, `<karta>.md5`, `ascmhl_chain.xml` a výstupy plánu 7 — `_clips.csv/.ale` karty i dne a denní report (R25).
- Formáty na disku čitelné Macem: nové soubory a klíče jsou jen **přidané** (Swift `Codable` neznámé klíče ignoruje); popis do `docs/FORMATY.md` (úkol 10).
- Testovatelné bez kamer: dočasné složky, syntetické klipy z ffmpeg `testsrc2` + `sine` (MOV s timecodem a `reel_name`, MXF jako Sony s 8 mono stopami), ručně psané JSON reporty (i ve tvaru Macu).
- `mac/` se nemění; co má Mac převzít, jde do `docs/VYVOJ-MAC.md` „Naplánováno“ (úkol 10).
- GUI se v testech ani při práci nikdy nespouští (`Offload.exe` ne). Rozhodování je v `Offload.Core` a testuje se tam; WPF části ověří `dotnet build` (XAML se kompiluje) a ruční test (úkol 10).
- Testy nikdy nezapíšou do skutečného `%APPDATA%`: `TestEnvironment` (module initializer) přesměruje `HistoryStore.Default`; nastavení jen přes `SettingsStore.Load/Save(path)` v `TempDir`; denní reporty a seznamy klipů jen do `TempDir`.
- R15: burn-in beze změny (výchozí zapnuto, volby i `--no-burn-in` beze změny). R16: MD5 nikdy samotné — `HashKind` vždy obsahuje xxh64.
- Verze: plán 7 → 0.3.0 (`0.3.0-win`, R22) — nastaví úkol 10 i v testech, které řetězec verze kontrolují (`FormatTests`, `ReportTests`, `HtmlReportTests`).
- `TreatWarningsAsErrors`: každý úkol končí `dotnet build windows\Offload.sln -warnaserror` bez varování a celou zelenou sadou `dotnet test windows\Offload.sln` (dnes 890 testů + 7 přeskočených, po plánu ~970). `ProxyWatchdogTests` je časově citlivý — když pod zátěží celé sady jednou selže, spusťte ho znovu samostatně (`--filter "FullyQualifiedName~ProxyWatchdogTests"`).
- PDF v testech: `PdfReport.SkipUnlessAsked` je pro celou sadu zapnuté (úloha v testu PDF netiskne, jen zaloguje); test, který PDF kontroluje, je v kolekci `[Collection("Edge")]` a volá `using var _ = PdfReport.PrintHere();`.
- Každý commit: nahoře v sekci `## 2026-10-07 — plán 7: výstupy pro produkci a střih` v `docs/VYVOJ-WINDOWS.md` řádek úkolu (sekci založí úkol 1 nad sekcí `## 2026-10-06 — plán 6: karty a bezpečnost dat`; nejnovější řádek nahoře, bez SHA). Soubory se přidávají výslovně (`git add <cesty>`, nikdy `-A`), zpráva se zapíše do `.git/COMMIT_MSG_PLAN7` (nástrojem Write, UTF-8) a commituje `git commit -F .git/COMMIT_MSG_PLAN7`; poslední řádek zprávy přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Všechny příkazy git a dotnet se spouštějí z kořene repa `D:\My\Projekt\offload`.
- Bloky „najít / nahradit“ uvádějí řádky podle stavu souboru **před** daným úkolem (výchozí stav = `windows-plan7` na `e2452f9`, pak předchozí úkoly plánu). Kód plánu byl postavený a otestovaný v kopii repa mimo strom repa (každý úkol se zkompiluje bez varování a celá sada je po něm zelená; ffmpeg 8.1, Edge). Pracovní kopie má konce řádků CRLF (`core.autocrlf=true`), bloky v plánu LF — nástroj Edit je sjednotí; kdo upravuje skriptem, porovnává bez ohledu na `\r`.

## Review Focus

Návrh v §6.2 jmenuje pět vstupů s největším rizikem; každý má v plánu test v úkolu, který vlastní ten kód:

1. **Klipy bez TC, drop-frame TC, smíšené fps na kartě / dni** → ALE má soubor na každé fps (klip bez fps patří k nejčastějšímu), klip bez TC začíná `00:00:00:00`, `End` u drop-frame počítá jako kamera (bez `;00`/`;01` v minutě kromě každé desáté). Testy: úkol 6 `ClipListTests.Ale_MixedFps_OneFilePerRate`, `DropFrame_EndAndDuration`, `Ale_HeadingTabsTracks`; `TimecodeEncoderTests.Add` (řádky drop-frame 29.97 i 59.94), `Length_NominalFrames`.
2. **Názvy a hodnoty s čárkou, uvozovkou, novým řádkem, tabulátorem a diakritikou** → CSV je dá do uvozovek a uvozovky zdvojí, nic jiného nezmění (ani `=`/`-` na začátku); ALE nahradí tabulátor a konec řádku mezerou. Testy: úkol 6 `ClipListTests.Csv_EscapingAndColumns`, `Ale_HeadingTabsTracks`; úkol 7 `DayReportTests.Html_EscapesCardNames`.
3. **Dvě regenerace denního reportu najednou + Mac (nebo Excel, prohlížeč) se stejným dnem** → druhá počká (nebo po limitu česky vzdá), nic se nezapíše napůl; čtenář se sdílením mazání nevadí, čtenář bez něj (Excel) zápis zastaví jen v logu, úloha je dál „Hotovo“. Testy: úkol 8 `DayReportServiceTests.TwoWriters_OneAtATime`, `OtherProcessHoldsTheDay_Busy`, `ReaderOfTheDayReport_SharingDecides_JobNeverFails`; úkol 6 `ClipListTests.Job_ListHeldOpen_StillDone_Logged`.
4. **Propustnost MD5 na rychlé kartě a ověření v režimu `both`** → MD5 běží na vlastním vlákně jako další konzument bloku (xxh64 ani čtení karty nečeká, dokud má pool volné bloky), ověření čte každý soubor jednou oběma součty a zfalšovaný bajt odhalí. Testy: úkol 4 `EngineTests.Both_TwoBlockPool_EveryFileVerified`, `TamperedDestination_FailsInBothModes`, `DestWriterTests.Verify_Both_Md5Mismatch_Fails`; úkol 5 `EngineTests.Both_ReportHashListMhlAndBackupVerification`, `BenchTests.Md5_HashLine_AndReadBackThroughBoth`; skutečná rychlost na CFexpress je ruční test 21 (úkol 10).
5. **Zdroje s víc zvukovými proudy (Sony MXF 4–8 mono stop) a `reel_name` v `tmcd`** → proxy má všechny proudy (ProRes PCM, H.264 AAC na každý proud) a cívku ve stopě timecodu; bez TC žádná stopa timecodu ani cívka. Testy: úkol 3 `ProxyRendererTests.Render_SonyMxf_EightMonoTracks`, `Render_ReelNameOnTimecodeTrack_EveryAudioStream`, `Render_NoTimecode_NoTimecodeTrackNoReel`.

## Ověřeno s ffmpeg 8.1 na tomto PC (podklad úkolu 3)

ffmpeg/ffprobe `8.1-full_build-www.gyan.dev` (PATH). Příkazy a výsledky:

```bash
# zdroj: H.264 MOV s TC 01:00:00:00 a dvěma mono zvukovými proudy
ffmpeg -hide_banner -loglevel error -nostdin -y -f lavfi -i testsrc2=s=640x360:r=25:d=2 -f lavfi -i "sine=f=440:d=2" \
  -f lavfi -i "sine=f=880:d=2" -map 0:v -map 1:a -map 2:a -timecode 01:00:00:00 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest src2a.mov
# proxy jako v úkolu 3: všechny zvukové proudy, AAC na každý, TC + reel_name na video proudu
ffmpeg -hide_banner -loglevel error -nostdin -y -i src2a.mov -map 0:v:0 -map "0:a?" -c:v libx264 -preset fast -b:v 2M \
  -c:a aac -b:a:0 128k -b:a:1 128k -timecode 01:00:00:00 -metadata:s:v:0 reel_name=A001 -f mov proxy_a.mov
ffprobe -v error -show_entries stream=index,codec_type,codec_tag_string,channels,bit_rate:stream_tags -of compact proxy_a.mov
#   stream|index=1|codec_type=audio|codec_tag_string=mp4a|channels=1|…
#   stream|index=2|codec_type=audio|codec_tag_string=mp4a|channels=1|…
#   stream|index=3|codec_type=data|codec_tag_string=tmcd|…|tag:reel_name=A001|tag:timecode=01:00:00:00
```

- `-metadata:s:v:0 reel_name=<cívka>` muxer MOV zapíše do stopy timecodu (`tmcd`, v ffprobe `codec_type=data`) jako její zdrojovou referenci; ffprobe ji ukáže jako tag `reel_name` **na datovém proudu**, ne na videu. Správná volba je tedy právě tahle.
- Bez `-timecode` (a bez TC ve zdroji) žádná `tmcd` nevznikne a `reel_name` se tiše zahodí (`-metadata:s:v:0 reel_name=A001` bez TC → proud jen video). TC ze zdroje (tag `timecode` videa) se ale převezme i bez `-timecode` — proto renderer dává `reel_name` jen spolu s `-timecode` z platného `ClipInfo.Timecode`.
- MXF (`-f mxf`, `-metadata reel_name=B002`): ffprobe ukáže `reel_name` jako tag **každého proudu** (video i audio), ne ve `format` — `FFProbe.Parse` proto čte cívku z formátu i ze všech proudů (úkol 2).
- `-map 0:a?` vezme všechny zvukové proudy (MXF s 8 mono PCM proudy → 8 proudů AAC, resp. `pcm_s16le` u ProRes; ověřeno příkazem s `-c:v prores_ks -profile:v 0 -vendor apl0 -c:a pcm_s16le`). Bitrate na proud `-b:a:<i>` funguje; `-b:a:<i>` pro proud, který neexistuje, je jen varování (kód 0). Bez zvuku ve zdroji `?` mapování nezastaví.

## Rozhodnutí tam, kde návrh nestačil (platí pro oba soubory plánu)

- **`append --hash` (§2.2) × „doplnění použije algoritmy původní zálohy“ (§3.5):** platí §3.5 — `offload append` volbu `--hash` nemá; součty určí `hashAlgorithms` dřívější zálohy (`AppendPlan.HashAlgorithms` → `OffloadPlan.Hashes`). Proto `HashKind` žije i v `OffloadPlan` (návrh ho jmenuje jen v `OffloadOptions`/`JobConfig`).
- **Hodnota `hash`, které verze nerozumí** (např. `"xxh128"` z budoucí verze): zachová se v `settings.json` (R24) a čte se jako `xxh64`.
- **Kanály zvukových proudů:** AAC bitrate na proud potřebuje kanály každého proudu; `ClipInfo.AudioStreams` je `[JsonIgnore]` (zná se jen po sondě v téže úloze), takže se formát reportu nemění. Neznámé proudy (RAW mezisoubor) dostanou společné `-b:a 192k` jako dnes. Odhad místa proxy počítá 0,2 Mb/s na každý proud (§2.5 „× zvukové stopy“).
- **Klipy bez analýzy:** `CardRecord.Clips` dá u generace bez klipů v reportu klipy nalezené `ClipDetector.Detect` v jejích souborech (bez metadat) — tak je mají denní report (počet klipů) i seznamy klipů (§3.3 „Bez analýzy klipů se řádky vytvoří z ClipDetector.Detect“).
- **Proxy kamery (SUB, PROXY) v seznamech klipů** se neuvádějí (nejsou to klipy pro střih; v reportu se také nepočítají do „klipů“).
- **ALE bez fps:** klip bez známé snímkové frekvence jde do souboru nejčastějšího fps; když fps nezná žádný klip, hlavička je `FPS 25` (Avid hodnotu vyžaduje). ALE, které nový zápis už nevytvoří (změnila se fps), se smaže — jen soubory přesně `<název>_clips[_<fps>].ale`.
- **Sloupec `Duration` (§3.3 „délka jako TC v nominálních snímcích, bez fps `H:MM:SS`“):** se známým fps `HH:MM:SS:FF` v nominálních snímcích (vždy `:`), bez fps délka `H:MM:SS`, bez délky prázdné. Sloupec `MD5` je v hlavičce vždy (stabilní pro import), bez MD5 prázdný.
- **Drop-frame:** `Timecode.Add` počítá drop-frame správně (dřív přibližně) — popisky náhledů tím jen zpřesní.
- **Karty dne (§3.2):** všechny podsložky `<datum>\` kromě `_REPORT` a skrytých (`.…`); bez `_REPORT\offload_report.json` řádek „bez reportu (probíhá, nebo byl přerušen)“. Denní report se nikdy nezapíše do složky, ve které není ani jedna karta s reportem („Ve složce … není žádná karta s reportem — vyberte složku dne na cíli (<cíl>\<datum>).“) — výběr kořene cíle v menu tak nevytvoří `_REPORT` v kořeni. Výběr složky karty znamená její den.
- **Dlaždice „ověřené karty N / M“:** karta je ověřená, když jsou ověřené všechny generace a nemá selhaný soubor; M = všechny karty dne (i bez reportu). Projekt v hlavičce dne přijde až s projekty (plán 8).
- **Čekání na zámek dne:** nejvýš 2 min (PDF trvá sekundy na cíl); pak `TimeoutException` s českou hláškou — úloha ji jen zaloguje.
- **Návod (§6.2 úkol 9 jmenuje kap. 6, 7, 10, 11):** mění se i kap. 1, 3 a 5 (menu Nástroje, volby Kontrolní součet / seznamy klipů / denní report jsou v sekci Struktura a kontrola).

---

### Task 1: `CardRecord` úplný

Spec §3.1, §6.2 úkol 1. Sloučení generací už plán 6 zavedl pro doplnění (`Generations`, `Files`, `Problems`); plán 7 přidá klipy všech generací, součty a stav, které čte denní report (úkol 7) a seznam klipů (úkol 6), a snese report z Macu i `"clips": null`.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 18–23)
- Modify: `windows/src/Offload.Core/CardRecord.cs` (řádky 10–17, 19–24, 84–95, 97–102)
- Modify: `windows/src/Offload.Core/OffloadReport.cs` (řádky 58–72)
- Modify: `windows/tests/Offload.Core.Tests/CardRecordTests.cs` (řádky 59–62)

**Interfaces:**
- Consumes: `CardRecord.Load(string cardPath)`, `RecordedFile(ReportFile File, int Generation, OffloadReport Report)`, `ClipDetector.Detect(IReadOnlyList<SourceFile>) → List<Clip>` (vše už existuje).
- Produces: `sealed record RecordedClip(ClipEntry Entry, int Generation)`; na `CardRecord`: `IReadOnlyList<RecordedClip> Clips`, `string FolderName`, `DateTime? Started`, `DateTime? Finished`, `int TotalFiles`, `long TotalBytes`, `bool Verified`, `IReadOnlyList<string> FailedFiles`; `OffloadReport.IsFailed(ReportFile f) → bool` (sdílí ho `FailedFiles`).

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/CardRecordTests.cs`, řádky 59–62 — najít:

```csharp
        File.WriteAllText(Path.Combine(card, "_REPORT", "A001_report_0003.html"), "<html/>");
        Assert.Equal(3, CardRecord.Load(card).HighestReportNumber);
    }
}
```

nahradit:

```csharp
        File.WriteAllText(Path.Combine(card, "_REPORT", "A001_report_0003.html"), "<html/>");
        Assert.Equal(3, CardRecord.Load(card).HighestReportNumber);
    }

    static ClipEntry Entry(string path, ProxyStatus proxy = ProxyStatus.None) => new()
    {
        Clip = new Clip { Name = Path.GetFileNameWithoutExtension(path), Path = path, Segments = [path], Bytes = 1000 },
        ProxyStatus = proxy,
    };

    // Spec 3.1: the clips of every generation, an append's after the earlier ones; a path listed again is the newest.
    [Fact]
    public void Clips_EveryGeneration_NewestEntryWins()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var full = Reports.Sample(card);
        full.Clips = [Entry("CLIP/A&B.mov", ProxyStatus.Failed), Entry("CLIP/c 02.mov")];
        full.Write(card, includeHashRecords: false);
        var append = Reports.Sample(card);
        append.Generation = 2;
        append.Clips = [Entry("CLIP/A&B.mov", ProxyStatus.Done), Entry("CLIP/d 03.mov")];
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), append.ToJson());

        var r = CardRecord.Load(card);

        Assert.Equal(["CLIP/A&B.mov", "CLIP/c 02.mov", "CLIP/d 03.mov"], r.Clips.Select(c => c.Entry.Clip.Path));
        Assert.Equal([2, 1, 2], r.Clips.Select(c => c.Generation));
        Assert.Equal(ProxyStatus.Done, r.Clips[0].Entry.ProxyStatus);
        Assert.Equal("A001", r.FolderName);
    }

    // Spec 3.1: totals over the generations; a file whose newest record is good is not failed any more.
    [Fact]
    public void Totals_FailedAndVerified_AcrossGenerations()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Reports.Sample(card, destError: "write failed").Write(card, includeHashRecords: false);
        var once = CardRecord.Load(card);
        Assert.Equal(["CLIP/c 02.mov"], once.FailedFiles);
        Assert.True(once.Verified);

        // The append copied the failed file again (it was not on the disk), but did not read it back.
        var t1 = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var append = new OffloadReport
        {
            CardName = "A001", Date = "2026-10-04", Source = @"E:\", SourceFilesystem = "exFAT",
            Started = t1, CopyFinished = t1.AddSeconds(5), Finished = t1.AddSeconds(9), Verified = false, Generation = 2,
            TotalFiles = 2, TotalBytes = 2500,
            Destinations = once.Generations[0].Report.Destinations,
            Files =
            [
                new ReportFile { Path = "CLIP/c 02.mov", Size = 2000, Modified = t1, Xxh64 = "fedcba9876543210", Destinations = ["written"], Errors = [null] },
                new ReportFile { Path = "CLIP/e 05.mov", Size = 500, Modified = t1, Xxh64 = "00000000000000aa", Destinations = ["written"], Errors = [null] },
            ],
            Skipped = [],
        };
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), append.ToJson());

        var r = CardRecord.Load(card);

        Assert.Empty(r.FailedFiles);
        Assert.False(r.Verified);
        Assert.Equal(3, r.TotalFiles);
        Assert.Equal(1000 + 2000 + 500, r.TotalBytes);
        Assert.Equal(new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc), r.Started);
        Assert.Equal(t1.AddSeconds(9), r.Finished);
    }

    // Spec 3.1: a report the Mac wrote — no "generation", "lut", "proxyEncoder", "md5", clips with fewer info keys.
    [Fact]
    public void MacReport_WithClips_Reads()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-06", "A001");
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.json"), """
            {
              "cardName" : "A001", "copyFinished" : "2026-10-06T07:10:00Z", "date" : "2026-10-06",
              "clips" : [ { "clip" : { "bytes" : 1000, "format" : "video", "isCameraProxy" : false, "name" : "A001C001", "path" : "CLIP/A001C001.mov", "segments" : [ "CLIP/A001C001.mov" ] },
                            "info" : { "audioChannels" : 2, "camera" : "ARRI ALEXA 35", "codec" : "prores", "duration" : 12.4, "frameRate" : "25/1", "height" : 2160, "timecode" : "01:00:00:00", "width" : 3840 },
                            "proxyPath" : "_PROXY/A001C001.mov", "proxyStatus" : "done" },
                          { "clip" : { "bytes" : 10, "format" : "video", "isCameraProxy" : false, "name" : "broken", "path" : "broken.mov", "segments" : [ "broken.mov" ] },
                            "probeError" : "moov atom not found", "proxyStatus" : "failed" } ],
              "destinations" : [ { "cardPath" : "/Volumes/Zaloha/2026-10-06/A001", "filesFailed" : 0, "filesOK" : 2, "filesystem" : "apfs", "root" : "/Volumes/Zaloha" } ],
              "files" : [ { "destinations" : [ "verified" ], "errors" : [ null ], "modified" : "2026-10-06T07:00:00Z", "path" : "CLIP/A001C001.mov", "size" : 1000, "xxh64" : "0123456789abcdef" },
                          { "destinations" : [ "verified" ], "errors" : [ null ], "modified" : "2026-10-06T07:00:00Z", "path" : "broken.mov", "size" : 10, "xxh64" : "0123456789abcdee" } ],
              "finished" : "2026-10-06T07:11:00Z", "hashAlgorithm" : "xxh64", "skipped" : [ ], "source" : "/Volumes/A001", "sourceFilesystem" : "exfat",
              "started" : "2026-10-06T07:09:00Z", "tool" : "offload", "totalBytes" : 1010, "totalFiles" : 2, "verified" : true, "version" : "0.1.0"
            }
            """);

        var r = CardRecord.Load(card);

        Assert.Null(r.Error);
        Assert.Equal(1, Assert.Single(r.Generations).Generation);
        Assert.True(r.Verified);
        Assert.Equal(["A001C001", "broken"], r.Clips.Select(c => c.Entry.Clip.Name));
        var info = r.Clips[0].Entry.Info!;
        Assert.Equal(("ARRI ALEXA 35", "01:00:00:00", 2), (info.Camera, info.Timecode, info.AudioChannels));
        Assert.Null(r.Clips[0].Entry.Lut);
        Assert.Null(r.Clips[1].Entry.Info);
    }

    // A null "clips" (hand-edited, an older writer) is no clips; a null clip entry is dropped, the card stays.
    [Fact]
    public void NullClips_AndANullEntry_AreTolerated()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);
        var json = Path.Combine(card, "_REPORT", "offload_report.json");
        File.WriteAllText(json, File.ReadAllText(json).Replace("\"clips\": []", "\"clips\": null"));
        Assert.Equal(2, CardRecord.Load(card).Clips.Count);   // none recorded: found in the files
        File.WriteAllText(json, File.ReadAllText(json).Replace("\"clips\": null", "\"clips\": [ null ]"));
        var r = CardRecord.Load(card);
        Assert.Null(r.Error);
        Assert.Equal(2, r.Clips.Count);
        Assert.Equal(2, r.TotalFiles);
    }

    // Spec 3.3: a copy without clip analysis still has clips — found in its files, without metadata.
    [Fact]
    public void Clips_WithoutAnalysis_DetectedFromTheFiles()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);   // two .mov files, no clips in the report

        var r = CardRecord.Load(card);

        Assert.Equal(["CLIP/A&B.mov", "CLIP/c 02.mov"], r.Clips.Select(c => c.Entry.Clip.Path));
        Assert.All(r.Clips, c => Assert.Null(c.Entry.Info));
        Assert.Equal([1000L, 2000L], r.Clips.Select(c => c.Entry.Clip.Bytes));
    }

    // An append report nobody can read: what it recorded is unknown — never "ověřeno".
    [Fact]
    public void UnreadableAppendReport_IsNotVerified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);
        Assert.True(CardRecord.Load(card).Verified);
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), "{broken");
        Assert.False(CardRecord.Load(card).Verified);
    }
}
```

- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CardRecordTests"`
Expected: build FAIL — `CS1061: 'CardRecord' neobsahuje definici pro 'Clips'` (a `FolderName`, `TotalFiles`, `Verified`, `FailedFiles` …).

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/OffloadReport.cs`, řádky 58–72 — najít:

```csharp
    [JsonIgnore] public double VerifySeconds => (Finished - CopyFinished).TotalSeconds;

    [JsonIgnore]
    public IReadOnlyList<ReportFile> FailedFiles
    {
        get
        {
            // A cancelled verification leaves files "written" or already "verified": both are fine then.
            bool IsOk(string d) => d == "verified" || (!Verified && d == "written");
            return Files.Where(f => f.SourceError != null || f.Destinations.Any(d => !IsOk(d))).ToList();
        }
    }

    [JsonIgnore] public bool Success => FailedFiles.Count == 0;
    /// Every file is on every destination and was read back with a matching hash.
```

nahradit:

```csharp
    [JsonIgnore] public double VerifySeconds => (Finished - CopyFinished).TotalSeconds;

    [JsonIgnore]
    public IReadOnlyList<ReportFile> FailedFiles => Files.Where(IsFailed).ToList();

    /// The card could not be read, or a destination does not hold a good copy. A cancelled verification leaves files
    /// "written" or already "verified": both are fine then.
    public bool IsFailed(ReportFile f) =>
        f.SourceError != null || f.Destinations.Any(d => !(d == "verified" || (!Verified && d == "written")));

    [JsonIgnore] public bool Success => FailedFiles.Count == 0;
    /// Every file is on every destination and was read back with a matching hash.
```

`windows/src/Offload.Core/CardRecord.cs`, řádky 10–17 — najít:

```csharp
/// A file as the card folder's reports record it (spec 3.1): the newest generation that lists it.
public sealed record RecordedFile(ReportFile File, int Generation, OffloadReport Report);

/// What the reports in `<card>\_REPORT` say about a card folder — every generation, also reports written by the Mac
/// (no "generation", "lut", "proxyEncoder" …). The core for appends (plan 6); plan 7 adds the day report and clip lists.
public sealed partial class CardRecord
{
    public const string MainReport = "offload_report.json";
```

nahradit:

```csharp
/// A file as the card folder's reports record it (spec 3.1): the newest generation that lists it.
public sealed record RecordedFile(ReportFile File, int Generation, OffloadReport Report);

/// A clip as the card folder's reports record it (spec 3.1): the newest generation that lists its path.
public sealed record RecordedClip(ClipEntry Entry, int Generation);

/// What the reports in `<card>\_REPORT` say about a card folder — every generation, also reports written by the Mac
/// (no "generation", "lut", "proxyEncoder", "md5" …). The source of appends (plan 6), the day report and the clip lists
/// (plan 7).
public sealed partial class CardRecord
{
    public const string MainReport = "offload_report.json";
```

`windows/src/Offload.Core/CardRecord.cs`, řádky 19–24 — najít:

```csharp
    public required string CardPath { get; init; }
    public IReadOnlyList<CardRecordGeneration> Generations { get; init; } = [];
    public IReadOnlyDictionary<string, RecordedFile> Files { get; init; } = new Dictionary<string, RecordedFile>();
    /// _REPORT\offload_report.json is not there.
    public bool ReportMissing { get; init; }
    /// Why the main report could not be read; null when it was read (or is missing).
```

nahradit:

```csharp
    public required string CardPath { get; init; }
    public IReadOnlyList<CardRecordGeneration> Generations { get; init; } = [];
    public IReadOnlyDictionary<string, RecordedFile> Files { get; init; } = new Dictionary<string, RecordedFile>();
    /// Every clip of every generation, in generation order (an append adds the clips of its new files); a clip path a
    /// later generation lists again replaces the earlier entry. A generation without clip analysis (no clips in its
    /// report) gets the clips ClipDetector finds in its files — without metadata (Info null).
    public IReadOnlyList<RecordedClip> Clips { get; init; } = [];

    /// The card folder's name ("A001") — how the day report and the clip lists call the card.
    public string FolderName => Path.GetFileName(CardPath.TrimEnd('\\', '/'));
    /// The first start and the last end over the generations read; null when none was.
    public DateTime? Started => Generations.Count > 0 ? Generations.Min(g => g.Report.Started) : null;
    public DateTime? Finished => Generations.Count > 0 ? Generations.Max(g => g.Report.Finished) : null;
    public int TotalFiles => Files.Count;
    public long TotalBytes => Files.Values.Sum(f => f.File.Size);
    /// Every generation was read back — an append report nobody can read leaves it unknown, so not verified.
    public bool Verified => Generations.Count > 0 && Problems.Count == 0 && Generations.All(g => g.Report.Verified);
    /// Paths whose newest record failed (the card or a destination), sorted.
    public IReadOnlyList<string> FailedFiles =>
        Files.Values.Where(f => f.Report.IsFailed(f.File)).Select(f => f.File.Path).Order(StringComparer.Ordinal).ToList();
    /// _REPORT\offload_report.json is not there.
    public bool ReportMissing { get; init; }
    /// Why the main report could not be read; null when it was read (or is missing).
```

`windows/src/Offload.Core/CardRecord.cs`, řádky 84–95 — najít:

```csharp
        }
        generations.Sort((a, b) => a.Generation.CompareTo(b.Generation));
        var files = new Dictionary<string, RecordedFile>(StringComparer.Ordinal);
        foreach (var g in generations)
            foreach (var f in g.Report.Files) files[f.Path] = new RecordedFile(f, g.Generation, g.Report);
        return new CardRecord { CardPath = cardPath, Generations = generations, Files = files, Problems = problems, HighestReportNumber = highest };
    }

    /// A report whose lists are there: a hand-edited null would otherwise surface later as a crash, not as "nejde přečíst".
    static OffloadReport Read(string path)
    {
        var r = OffloadReport.FromJson(File.ReadAllText(path));
```

nahradit:

```csharp
        }
        generations.Sort((a, b) => a.Generation.CompareTo(b.Generation));
        var files = new Dictionary<string, RecordedFile>(StringComparer.Ordinal);
        var clips = new List<RecordedClip>();
        var clipAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in generations)
        {
            foreach (var f in g.Report.Files) files[f.Path] = new RecordedFile(f, g.Generation, g.Report);
            var generationClips = g.Report.Clips.Count > 0 ? g.Report.Clips
                : ClipDetector.Detect(g.Report.Files.Select(f => new SourceFile(f.Path, f.Size, f.Modified)).ToList()).Select(c => new ClipEntry { Clip = c });
            foreach (var c in generationClips)
            {
                var recorded = new RecordedClip(c, g.Generation);
                if (clipAt.TryGetValue(c.Clip.Path, out var i)) clips[i] = recorded;
                else
                {
                    clipAt[c.Clip.Path] = clips.Count;
                    clips.Add(recorded);
                }
            }
        }
        return new CardRecord
        {
            CardPath = cardPath, Generations = generations, Files = files, Clips = clips, Problems = problems, HighestReportNumber = highest,
        };
    }

    /// A report whose lists are there: a hand-edited null would otherwise surface later as a crash, not as "nejde přečíst".
    /// A missing or null "clips" (a report without analysis, an older Mac) is an empty list — Load then finds the clips
    /// in the files; a null clip entry is dropped — the files are the record of the copy, the clips only describe them.
    static OffloadReport Read(string path)
    {
        var r = OffloadReport.FromJson(File.ReadAllText(path));
```

`windows/src/Offload.Core/CardRecord.cs`, řádky 97–102 — najít:

```csharp
            || r.Files.Any(f => f == null || f.Path == null || f.Destinations == null)
            || r.Destinations.Any(d => d == null || d.CardPath == null))
            throw new InvalidOperationException("chybí seznam souborů nebo cílů");
        return r;
    }

```

nahradit:

```csharp
            || r.Files.Any(f => f == null || f.Path == null || f.Destinations == null)
            || r.Destinations.Any(d => d == null || d.CardPath == null))
            throw new InvalidOperationException("chybí seznam souborů nebo cílů");
        r.Clips = (r.Clips ?? []).Where(c => c?.Clip?.Path != null && c.Clip.Name != null && c.Clip.Segments != null).ToList();
        return r;
    }

```

- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CardRecordTests|FullyQualifiedName~AppendPlannerTests|FullyQualifiedName~ReportTests"`
Expected: PASS. Pak celá sada: `dotnet build windows\Offload.sln -warnaserror` (0 varování) a `dotnet test windows\Offload.sln` (vše zelené).

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 18–23 — najít:

````markdown
| 5 | vestavěné LUTy Log → Rec.709 (ARRI, Sony, Blackmagic, RED, Canon, Panasonic, DJI, Fuji), automatický výběr podle metadat klipu | hotovo (čeká ruční test s klipy z kamer) |
| 6 | karty a bezpečnost dat: historie karet, doplnění nových klipů, název z klipů, ověření zálohy proti ASC MHL, kontrola místa, české hlášky (0.2.0) | hotovo (čeká ruční test s kartou) |

## 2026-10-06 — plán 6: karty a bezpečnost dat

- Závěrečné opravy — Start pojmenuje karty podle názvů zobrazených před spuštěním (dvě neznámé karty A002, A003, ne A002_2); kontrola místa při doplnění drží původní pořadí cíle (proxy na prvním cíli zálohy); `naming` v settings.json zachová neznámé klíče; doplnění v GUI s `AllowSameDevice` (cíle určuje dřívější záloha); FORMATY (`files`/`bytes` = všechny generace); návod (text hlášky, poznámka „nevracejte se k verzi před 0.2.0“, nové PDF); ruční test plánu 6 v README
````

nahradit:

````markdown
| 5 | vestavěné LUTy Log → Rec.709 (ARRI, Sony, Blackmagic, RED, Canon, Panasonic, DJI, Fuji), automatický výběr podle metadat klipu | hotovo (čeká ruční test s klipy z kamer) |
| 6 | karty a bezpečnost dat: historie karet, doplnění nových klipů, název z klipů, ověření zálohy proti ASC MHL, kontrola místa, české hlášky (0.2.0) | hotovo (čeká ruční test s kartou) |

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 1 — `CardRecord` úplný: klipy všech generací (doplnění za dřívějšími, tatáž cesta = nejnovější záznam), součty přes generace (soubory, data, začátek–konec), selhané soubory podle nejnovějšího záznamu, „ověřeno“ jen když jsou ověřené všechny generace a žádný report doplnění není nečitelný; generace bez analýzy klipů dostane klipy nalezené v jejích souborech (bez metadat); snese report z Macu (bez `generation`, `lut`, `proxyEncoder`, `md5`) i `"clips": null`

## 2026-10-06 — plán 6: karty a bezpečnost dat

- Závěrečné opravy — Start pojmenuje karty podle názvů zobrazených před spuštěním (dvě neznámé karty A002, A003, ne A002_2); kontrola místa při doplnění drží původní pořadí cíle (proxy na prvním cíli zálohy); `naming` v settings.json zachová neznámé klíče; doplnění v GUI s `AllowSameDevice` (cíle určuje dřívější záloha); FORMATY (`files`/`bytes` = všechny generace); návod (text hlášky, poznámka „nevracejte se k verzi před 0.2.0“, nové PDF); ruční test plánu 6 v README
````

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Windows: full CardRecord (clips of every generation, totals, Mac reports)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/CardRecord.cs windows/src/Offload.Core/OffloadReport.cs windows/tests/Offload.Core.Tests/CardRecordTests.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

---

### Task 2: `ReelResolver` + `ClipInfo.Reel`

Spec §3.4 (první odrážka), §6.2 úkol 2. Cívka klipu: (1) tag kamery z ffprobe — `reel_name`, `tape_name`, `reel` ve formátu i v proudech (MOV ji má na stopě timecodu, MXF na každém proudu — ověřeno, viz „Ověřeno s ffmpeg 8.1“), (2) vzor z názvu klipu (`ReelNames` z plánu 6), (3) název složky karty. Výsledek jde do reportu `clips[].info.reel`; proxy ho použije v úkolu 3, pravidla LUTů v plánu 8.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 20–25)
- Modify: `windows/src/Offload.Core/ClipDetector.cs` (řádky 123–131)
- Modify: `windows/src/Offload.Core/Media.cs` (řádky 61–66)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 439–449)
- Modify: `windows/src/Offload.Core/ReelNames.cs` (řádky 30–35)
- Modify: `windows/tests/Offload.Core.Tests/ClipDetectorTests.cs` (řádky 102–107)
- Modify: `windows/tests/Offload.Core.Tests/ReelNamesTests.cs` (řádky 29–34)
- Modify: `windows/tests/Offload.Core.Tests/StageTwoTests.cs` (řádky 182–187)
- Modify: `windows/tests/Offload.Core.Tests/TestMedia.cs` (řádky 14–22, 25–30)

**Interfaces:**
- Consumes: `ReelNames.FromClipName(string name) → string?` (plán 6), `FFProbe.Parse(string json) → ClipInfo`.
- Produces: `ClipInfo.Reel` (`string?`, JSON `reel`, vynechá se při `null`); `static class ReelResolver { static string Resolve(Clip clip, ClipInfo? info, string cardName) }`; `OffloadJob` po sondě nastaví `info.Reel = ReelResolver.Resolve(clip, info, plan.CardName)`; testovací `TestMedia.MakeClip(…, string? reel = null)` (MOV s `reel_name` ve stopě TC).

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/ReelNamesTests.cs`, řádky 29–34 — najít:

```csharp
    static Clip C(string name, bool cameraProxy = false) =>
        new() { Name = name, Path = name + ".mov", Format = ClipFormat.Video, Segments = [name + ".mov"], Bytes = 1, IsCameraProxy = cameraProxy };

    [Fact]
    public void OneReel_Suggested_WithHint()
    {
```

nahradit:

```csharp
    static Clip C(string name, bool cameraProxy = false) =>
        new() { Name = name, Path = name + ".mov", Format = ClipFormat.Video, Segments = [name + ".mov"], Bytes = 1, IsCameraProxy = cameraProxy };

    // Spec 3.4: the camera's tag first, then the reel in the clip name, then the card folder.
    [Theory]
    [InlineData("TAPE07", "A001C003_210101_R1AB", "TAPE07")]
    [InlineData(" TAPE07 ", "C0001", "TAPE07")]
    [InlineData("  ", "A001C003_210101_R1AB", "A001")]
    [InlineData(null, "a002_c004_0101ab", "A002")]
    [InlineData(null, "C0001", "KARTA")]
    public void Resolve_TagThenClipNameThenCard(string? tag, string clipName, string reel) =>
        Assert.Equal(reel, ReelResolver.Resolve(C(clipName), tag == null ? null : new ClipInfo { Reel = tag }, "KARTA"));

    [Fact]
    public void OneReel_Suggested_WithHint()
    {
```

`windows/tests/Offload.Core.Tests/ClipDetectorTests.cs`, řádky 102–107 — najít:

```csharp
        Assert.Equal(0, i.AudioChannels);
    }

    [FfmpegFact]
    public void Probe_RealClip()
    {
```

nahradit:

```csharp
        Assert.Equal(0, i.AudioChannels);
    }

    // Spec 3.4: MOV keeps the reel on the timecode track, MXF on every stream; other writers use tape_name or reel.
    [Theory]
    [InlineData("""{"streams":[{"codec_type":"video"},{"codec_type":"data","codec_tag_string":"tmcd","tags":{"reel_name":"A001","timecode":"01:00:00:00"}}],"format":{}}""", "A001")]
    [InlineData("""{"streams":[{"codec_type":"video","tags":{"reel_name":"B002"}},{"codec_type":"audio","tags":{"reel_name":"B002"}}],"format":{}}""", "B002")]
    [InlineData("""{"streams":[],"format":{"tags":{"tape_name":" T7 "}}}""", "T7")]
    [InlineData("""{"streams":[{"codec_type":"video","tags":{"Reel":"R9"}}]}""", "R9")]
    [InlineData("""{"streams":[{"codec_type":"video","tags":{"reel_name":" "}}],"format":{}}""", null)]
    public void Parse_Reel(string json, string? reel) => Assert.Equal(reel, FFProbe.Parse(json).Reel);

    [FfmpegFact]
    public void Probe_RealClip()
    {
```

`windows/tests/Offload.Core.Tests/TestMedia.cs`, řádky 14–22 — najít:

```csharp
    public static string? Ffmpeg => Tools.Ffmpeg;
    public static string? Ffprobe => Tools.Ffprobe;

    /// Test pattern with burnt-in TC track and optional sine audio, H.264 in MOV/MP4 (by extension).
    public static void MakeClip(string path, double seconds = 2, int fps = 25, string timecode = "01:00:00:00",
        bool audio = true, int width = 1280, int height = 720)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
```

nahradit:

```csharp
    public static string? Ffmpeg => Tools.Ffmpeg;
    public static string? Ffprobe => Tools.Ffprobe;

    /// Test pattern with burnt-in TC track and optional sine audio, H.264 in MOV/MP4 (by extension). `reel`: the
    /// camera's reel name on the timecode track (MOV `reel_name`).
    public static void MakeClip(string path, double seconds = 2, int fps = 25, string timecode = "01:00:00:00",
        bool audio = true, int width = 1280, int height = 720, string? reel = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
```

`windows/tests/Offload.Core.Tests/TestMedia.cs`, řádky 25–30 — najít:

```csharp
        if (audio) args.AddRange(["-f", "lavfi", "-i", $"sine=d={d}"]);
        args.AddRange(["-timecode", timecode, "-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (audio) args.AddRange(["-c:a", "aac", "-shortest"]);
        args.Add(path);
        ToolProcess.Run(Ffmpeg!, args);
    }
```

nahradit:

```csharp
        if (audio) args.AddRange(["-f", "lavfi", "-i", $"sine=d={d}"]);
        args.AddRange(["-timecode", timecode, "-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (audio) args.AddRange(["-c:a", "aac", "-shortest"]);
        if (reel != null) args.AddRange(["-metadata:s:v:0", $"reel_name={reel}"]);
        args.Add(path);
        ToolProcess.Run(Ffmpeg!, args);
    }
```

`windows/tests/Offload.Core.Tests/StageTwoTests.cs`, řádky 182–187 — najít:

```csharp
        Assert.False(Directory.Exists(Path.Combine(job.Plan!.CardPaths[0], "_PROXY")));
    }

    [Fact]
    public void NothingToAnalyze_StillDone()
    {
```

nahradit:

```csharp
        Assert.False(Directory.Exists(Path.Combine(job.Plan!.CardPaths[0], "_PROXY")));
    }

    // Spec 3.4: the reel of every clip in the report — the camera's tag, else the clip name, else the card folder.
    [FfmpegFact]
    public void Report_RecordsReel_TagNameOrCard()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C003_210101_R1AB.mov"), seconds: 1);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "C0001.mov"), seconds: 1);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "take.mov"), seconds: 1, reel: "TAPE07");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-05", CardName = "KARTA" }, Options());

        var report = job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Equal(["A001", "KARTA", "TAPE07"], report!.Clips.Select(c => c.Info?.Reel));
        Assert.Contains("\"reel\": \"TAPE07\"", File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json")));
    }

    [Fact]
    public void NothingToAnalyze_StillDone()
    {
```

- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ReelNamesTests|FullyQualifiedName~ClipDetectorTests|FullyQualifiedName~StageTwoTests"`
Expected: build FAIL — `CS0103: Název 'ReelResolver' v aktuálním kontextu neexistuje`, `CS0117: 'ClipInfo' neobsahuje definici pro 'Reel'`.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Media.cs`, řádky 61–66 — najít:

```csharp
    [JsonPropertyName("gamma")] public string? Gamma { get; set; }
    [JsonPropertyName("gamut")] public string? Gamut { get; set; }
    [JsonPropertyName("colorScienceGen")] public int? ColorScienceGen { get; set; }

    [JsonIgnore]
    public string FpsLabel
```

nahradit:

```csharp
    [JsonPropertyName("gamma")] public string? Gamma { get; set; }
    [JsonPropertyName("gamut")] public string? Gamut { get; set; }
    [JsonPropertyName("colorScienceGen")] public int? ColorScienceGen { get; set; }
    /// The reel (tape name): ffprobe's `reel_name` / `tape_name` / `reel` tag after the probe, then ReelResolver's
    /// answer (tag, else the clip name, else the card folder) once the job has the clip (spec 3.4). Report key
    /// `clips[].info.reel`, absent when unknown.
    [JsonPropertyName("reel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reel { get; set; }

    [JsonIgnore]
    public string FpsLabel
```

`windows/src/Offload.Core/ClipDetector.cs`, řádky 123–131 — najít:

```csharp
        info.Timecode ??= Str(ftags, "timecode") ?? streams.Select(x => Str(Tags(x), "timecode")).FirstOrDefault(t => t != null);
        info.Camera = Camera(Str(ftags, "com.apple.quicktime.make"), Str(ftags, "com.apple.quicktime.model"))
            ?? Str(ftags, "product_name") ?? Str(ftags, "company_name");
        return info;
    }

    /// Make and model together ("ARRI" + "ALEXA 35" → "ARRI ALEXA 35"), unless the model already starts with the make.
    static string? Camera(string? make, string? model)
    {
```

nahradit:

```csharp
        info.Timecode ??= Str(ftags, "timecode") ?? streams.Select(x => Str(Tags(x), "timecode")).FirstOrDefault(t => t != null);
        info.Camera = Camera(Str(ftags, "com.apple.quicktime.make"), Str(ftags, "com.apple.quicktime.model"))
            ?? Str(ftags, "product_name") ?? Str(ftags, "company_name");
        info.Reel = new[] { format }.Concat(streams).Select(x => Reel(Tags(x))).FirstOrDefault(r => r != null);
        return info;
    }

    /// Tags that name the reel: MOV keeps it on the timecode track (tmcd "reel_name"), MXF on every stream.
    static readonly string[] ReelTags = ["reel_name", "tape_name", "reel"];

    static string? Reel(JsonElement tags)
    {
        if (tags.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in ReelTags)
            foreach (var p in tags.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String
                    && p.Value.GetString()!.Trim() is { Length: > 0 } reel)
                    return reel;
        return null;
    }

    /// Make and model together ("ARRI" + "ALEXA 35" → "ARRI ALEXA 35"), unless the model already starts with the make.
    static string? Camera(string? make, string? model)
    {
```

`windows/src/Offload.Core/ReelNames.cs`, řádky 30–35 — najít:

```csharp
    public static string? FromClipName(string name) => Match(name)?.Reel;
}

/// A card folder name proposed instead of the volume label (spec 2.3). `Hint` is the grey line under the name field.
public sealed record NameSuggestion(string Name, string Hint);

```

nahradit:

```csharp
    public static string? FromClipName(string name) => Match(name)?.Reel;
}

/// The reel of a clip (spec 3.4) — what the proxy's timecode track, the report, the clip lists and (plan 8) the LUT
/// rules call it, like DIT tools do: (1) the camera's own tag (ffprobe `reel_name`, `tape_name`, `reel`, in ClipInfo.Reel),
/// (2) the reel in the clip name (ReelNames: "A001C003…" → "A001"), (3) the card folder's name.
public static class ReelResolver
{
    public static string Resolve(Clip clip, ClipInfo? info, string cardName) =>
        info?.Reel?.Trim() is { Length: > 0 } tag ? tag
        : ReelNames.FromClipName(clip.Name) ?? cardName;
}

/// A card folder name proposed instead of the volume label (spec 2.3). `Hint` is the grey line under the name field.
public sealed record NameSuggestion(string Name, string Hint);

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 439–449 — najít:

```csharp
                    }
                    else
                    {
                        entries[i].Info = info;
                        entries[i].ProbeError = "kodek nepodporován ffmpegem (např. Sony X-OCN) — zkopírováno a ověřeno, bez proxy";
                        continue;
                    }
                }
                entries[i].Info = info;
                // The Sony XML is read from the destination: it sits next to the clip on the card, so it was copied too.
                entries[i].Lut = LutResolver.Resolve(entries[i].Clip, info, Layout.Join(cardPaths[d], entries[i].Clip.Path), Config.Proxy, LutCatalog.Default);
```

nahradit:

```csharp
                    }
                    else
                    {
                        info.Reel = ReelResolver.Resolve(clip, info, plan.CardName);
                        entries[i].Info = info;
                        entries[i].ProbeError = "kodek nepodporován ffmpegem (např. Sony X-OCN) — zkopírováno a ověřeno, bez proxy";
                        continue;
                    }
                }
                // Spec 3.4: the camera's tag, else the clip name, else the card folder — into the report and the proxy.
                info.Reel = ReelResolver.Resolve(entries[i].Clip, info, plan.CardName);
                entries[i].Info = info;
                // The Sony XML is read from the destination: it sits next to the clip on the card, so it was copied too.
                entries[i].Lut = LutResolver.Resolve(entries[i].Clip, info, Layout.Join(cardPaths[d], entries[i].Clip.Path), Config.Proxy, LutCatalog.Default);
```

- [ ] **Step 4: Spustit — projdou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ReelNamesTests|FullyQualifiedName~ClipDetectorTests|FullyQualifiedName~StageTwoTests"`
Expected: PASS (`Report_RecordsReel_TagNameOrCard` je `[FfmpegFact]` — s ffmpeg na PATH nebo ve `windows\tools\bin` běží). Pak `dotnet build windows\Offload.sln -warnaserror` a celá sada.

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 20–25 — najít:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 1 — `CardRecord` úplný: klipy všech generací (doplnění za dřívějšími, tatáž cesta = nejnovější záznam), součty přes generace (soubory, data, začátek–konec), selhané soubory podle nejnovějšího záznamu, „ověřeno“ jen když jsou ověřené všechny generace a žádný report doplnění není nečitelný; generace bez analýzy klipů dostane klipy nalezené v jejích souborech (bez metadat); snese report z Macu (bez `generation`, `lut`, `proxyEncoder`, `md5`) i `"clips": null`

## 2026-10-06 — plán 6: karty a bezpečnost dat
````

nahradit:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 2 — cívka klipu (`ReelResolver`): tag kamery z ffprobe (`reel_name`, `tape_name`, `reel` — u MOV na stopě timecodu, u MXF na každém proudu), jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty; v reportu `clips[].info.reel`
- Úkol 1 — `CardRecord` úplný: klipy všech generací (doplnění za dřívějšími, tatáž cesta = nejnovější záznam), součty přes generace (soubory, data, začátek–konec), selhané soubory podle nejnovějšího záznamu, „ověřeno“ jen když jsou ověřené všechny generace a žádný report doplnění není nečitelný; generace bez analýzy klipů dostane klipy nalezené v jejích souborech (bez metadat); snese report z Macu (bez `generation`, `lut`, `proxyEncoder`, `md5`) i `"clips": null`

## 2026-10-06 — plán 6: karty a bezpečnost dat
````

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Windows: clip reel from the camera tag, the clip name or the card folder

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/ClipDetector.cs windows/src/Offload.Core/Media.cs windows/src/Offload.Core/OffloadJob.cs windows/src/Offload.Core/ReelNames.cs windows/tests/Offload.Core.Tests/ClipDetectorTests.cs windows/tests/Offload.Core.Tests/ReelNamesTests.cs windows/tests/Offload.Core.Tests/StageTwoTests.cs windows/tests/Offload.Core.Tests/TestMedia.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

---

### Task 3: Proxy pro přelinkování

Spec §3.4, R14, R15, §6.2 úkol 3; ověřeno s ffmpeg 8.1 (oddíl výše). S platným TC navíc `-metadata:s:v:0 reel_name=<cívka>` (MOV ho zapíše do stopy `tmcd`), bez TC ani `-timecode`, ani cívka. Zvuk `-map 0:a?` místo `-map 0:a:0?`: H.264/HEVC AAC na každý proud `-b:a:<i>` = 64 kb/s × kanály (128–512k), ProRes `pcm_s16le` beze změny. Proxy přejmenovaná kvůli shodě (`_2`) dostane poznámku. Burn-in beze změny (R15).

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 20–25)
- Modify: `windows/src/Offload.Core/ClipDetector.cs` (řádky 117–123)
- Modify: `windows/src/Offload.Core/Encoders.cs` (řádky 57–64)
- Modify: `windows/src/Offload.Core/Media.cs` (řádky 50–55)
- Modify: `windows/src/Offload.Core/Messages.cs` (řádky 161–166)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 517–522)
- Modify: `windows/src/Offload.Core/ProxyRenderer.cs` (řádky 368–380)
- Modify: `windows/src/Offload.Core/Space/SpaceEstimator.cs` (řádky 65–71)
- Modify: `windows/tests/Offload.Core.Tests/AppendEngineTests.cs` (řádky 609–614)
- Modify: `windows/tests/Offload.Core.Tests/ClipDetectorTests.cs` (řádky 62–67)
- Modify: `windows/tests/Offload.Core.Tests/ProxyRendererTests.cs` (řádky 379–384)
- Modify: `windows/tests/Offload.Core.Tests/SpaceTests.cs` (řádky 47–52)
- Modify: `windows/tests/Offload.Core.Tests/TestMedia.cs` (řádky 15–36)
- Modify: `windows/tests/Offload.Core.Tests/TimecodeEncoderTests.cs` (řádky 41–51)

**Interfaces:**
- Consumes: `ClipInfo.Reel` (úkol 2), `ProxyRenderer.RenderOnce` (soukromá), `Encoders`, `SpaceEstimator.ProxyBytes`.
- Produces: `ClipInfo.AudioStreams` (`List<int>`, kanály každého zvukového proudu, `[JsonIgnore]`, plní `FFProbe.Parse`); `Encoders.AudioArgs(ProxyCodec codec, IReadOnlyList<int> streamChannels) → List<string>` (stará `AudioArgs(ProxyCodec)` zmizí); `Encoders.AacKbps(int channels) → int`; `Msg.ProxyRenamed(string name)`; testovací `TestMedia.MakeClip(…, string? timecode = "01:00:00:00", …, int audioStreams = 1)` (`timecode: null` = bez TC), `TestMedia.MakeMxf(string path, int monoTracks, string timecode = "10:00:00:00", double seconds = 1)`, `TestMedia.Streams(string path) → List<(string Type, string? Codec, int Channels, Dictionary<string,string> Tags)>`.

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/TestMedia.cs`, řádky 15–36 — najít:

```csharp
    public static string? Ffprobe => Tools.Ffprobe;

    /// Test pattern with burnt-in TC track and optional sine audio, H.264 in MOV/MP4 (by extension). `reel`: the
    /// camera's reel name on the timecode track (MOV `reel_name`).
    public static void MakeClip(string path, double seconds = 2, int fps = 25, string timecode = "01:00:00:00",
        bool audio = true, int width = 1280, int height = 720, string? reel = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc2=s={width}x{height}:r={fps}:d={d}" };
        if (audio) args.AddRange(["-f", "lavfi", "-i", $"sine=d={d}"]);
        args.AddRange(["-timecode", timecode, "-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (audio) args.AddRange(["-c:a", "aac", "-shortest"]);
        if (reel != null) args.AddRange(["-metadata:s:v:0", $"reel_name={reel}"]);
        args.Add(path);
        ToolProcess.Run(Ffmpeg!, args);
    }

    /// 2×2×2 identity 3D LUT.
    public static void IdentityLut(string path) => File.WriteAllText(path,
        "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
```

nahradit:

```csharp
    public static string? Ffprobe => Tools.Ffprobe;

    /// Test pattern with burnt-in TC track and optional sine audio, H.264 in MOV/MP4 (by extension). `reel`: the
    /// camera's reel name on the timecode track (MOV `reel_name`); `audioStreams`: that many mono sine streams;
    /// `timecode` null: no timecode at all.
    public static void MakeClip(string path, double seconds = 2, int fps = 25, string? timecode = "01:00:00:00",
        bool audio = true, int width = 1280, int height = 720, string? reel = null, int audioStreams = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc2=s={width}x{height}:r={fps}:d={d}" };
        int streams = audio ? audioStreams : 0;
        for (int i = 0; i < streams; i++) args.AddRange(["-f", "lavfi", "-i", $"sine=f={440 * (i + 1)}:d={d}"]);
        args.AddRange(["-map", "0:v"]);
        for (int i = 0; i < streams; i++) args.AddRange(["-map", $"{i + 1}:a"]);
        if (timecode != null) args.AddRange(["-timecode", timecode]);
        args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (streams > 0) args.AddRange(["-c:a", "aac", "-shortest"]);
        if (reel != null) args.AddRange(["-metadata:s:v:0", $"reel_name={reel}"]);
        args.Add(path);
        ToolProcess.Run(Ffmpeg!, args);
    }

    /// A Sony-like MXF: MPEG-2 1080p25 with `monoTracks` 24-bit PCM mono streams and timecode `timecode`.
    public static void MakeMxf(string path, int monoTracks, string timecode = "10:00:00:00", double seconds = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc2=s=1920x1080:r=25:d={d}" };
        for (int i = 0; i < monoTracks; i++) args.AddRange(["-f", "lavfi", "-i", $"sine=f={110 * (i + 1)}:d={d}"]);
        args.AddRange(["-map", "0:v"]);
        for (int i = 0; i < monoTracks; i++) args.AddRange(["-map", $"{i + 1}:a"]);
        args.AddRange(["-c:v", "mpeg2video", "-b:v", "30M", "-c:a", "pcm_s24le", "-ar", "48000", "-timecode", timecode, "-f", "mxf", path]);
        ToolProcess.Run(Ffmpeg!, args);
    }

    /// ffprobe's streams of a file: (codec_type, codec_name, channels, tags).
    public static List<(string Type, string? Codec, int Channels, Dictionary<string, string> Tags)> Streams(string path)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(ToolProcess.Run(Ffprobe!, ["-v", "error", "-print_format", "json", "-show_streams", path]));
        return doc.RootElement.GetProperty("streams").EnumerateArray().Select(s => (
            s.GetProperty("codec_type").GetString()!,
            s.TryGetProperty("codec_name", out var c) ? c.GetString() : null,
            s.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0,
            s.TryGetProperty("tags", out var t) ? t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "") : new Dictionary<string, string>()))
            .ToList();
    }

    /// 2×2×2 identity 3D LUT.
    public static void IdentityLut(string path) => File.WriteAllText(path,
        "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
```

`windows/tests/Offload.Core.Tests/ProxyRendererTests.cs`, řádky 379–384 — najít:

```csharp
        Assert.Equal("prores", FFProbe.Probe(TestMedia.Ffprobe!, output).Codec!.Split(' ')[0]);
    }

    [FfmpegFact]
    public void Render_Cancelled_LeavesNothing()
    {
```

nahradit:

```csharp
        Assert.Equal("prores", FFProbe.Probe(TestMedia.Ffprobe!, output).Codec!.Split(' ')[0]);
    }

    // Spec 3.4: the proxy relinks — start TC and reel name on the timecode track, every audio stream of the source.
    [FfmpegFact]
    public void Render_ReelNameOnTimecodeTrack_EveryAudioStream()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "a.mov"), seconds: 1, timecode: "01:00:00:00", audioStreams: 2);
        using var r = new ProxyRenderer(new ProxySettings { Height = 360 }, Tools) { ForcedEncoder = "libx264" };
        var clip = VideoClip("a.mov");
        var info = r.Probe(clip, card);
        Assert.Equal([1, 1], info.AudioStreams);
        info.Reel = "A001";
        var output = Path.Combine(card, "_PROXY", "a.mov");

        r.Render(clip, card, info, null, output, _ => { }, () => false);

        var streams = TestMedia.Streams(output);
        var tmcd = Assert.Single(streams, s => s.Type == "data");
        Assert.Equal(("A001", "01:00:00:00"), (tmcd.Tags["reel_name"], tmcd.Tags["timecode"]));
        Assert.Equal([("aac", 1), ("aac", 1)], streams.Where(s => s.Type == "audio").Select(s => (s.Codec, s.Channels)));
        Assert.Equal("A001", FFProbe.Probe(TestMedia.Ffprobe!, output).Reel);
    }

    // Review focus 5: a Sony MXF with 8 mono tracks — the ProRes proxy keeps all 8 (PCM) and gets the reel.
    [FfmpegFact]
    public void Render_SonyMxf_EightMonoTracks()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeMxf(Path.Combine(card, "C0001.MXF"), monoTracks: 8);
        using var r = new ProxyRenderer(new ProxySettings { Height = 360, Codec = ProxyCodec.ProresProxy }, Tools);
        var clip = VideoClip("C0001.MXF");
        var info = r.Probe(clip, card);
        Assert.Equal(8, info.AudioStreams.Count);
        info.Reel = "A001";
        var output = Path.Combine(card, "_PROXY", "C0001.mov");

        r.Render(clip, card, info, null, output, _ => { }, () => false);

        var streams = TestMedia.Streams(output);
        Assert.Equal(Enumerable.Repeat<(string?, int)>(("pcm_s16le", 1), 8), streams.Where(s => s.Type == "audio").Select(s => (s.Codec, s.Channels)));
        var tmcd = Assert.Single(streams, s => s.Type == "data");
        Assert.Equal(("A001", "10:00:00:00"), (tmcd.Tags["reel_name"], tmcd.Tags["timecode"]));
    }

    // Spec 3.4: without timecode there is no timecode track — and so no reel name (Resolve relinks by name).
    [FfmpegFact]
    public void Render_NoTimecode_NoTimecodeTrackNoReel()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "a.mov"), seconds: 1, timecode: null);
        using var r = new ProxyRenderer(new ProxySettings { Height = 360 }, Tools) { ForcedEncoder = "libx264" };
        var clip = VideoClip("a.mov");
        var info = r.Probe(clip, card);
        Assert.Null(info.Timecode);
        info.Reel = "A001";
        var output = Path.Combine(card, "_PROXY", "a.mov");

        r.Render(clip, card, info, null, output, _ => { }, () => false);

        var streams = TestMedia.Streams(output);
        Assert.DoesNotContain(streams, s => s.Type == "data");
        Assert.DoesNotContain(streams, s => s.Tags.ContainsKey("reel_name"));
        Assert.Single(streams, s => s.Type == "audio");
    }

    [FfmpegFact]
    public void Render_Cancelled_LeavesNothing()
    {
```

`windows/tests/Offload.Core.Tests/TimecodeEncoderTests.cs`, řádky 41–51 — najít:

```csharp
            Encoders.VideoArgs("libx264", ProxyCodec.H264, 10));
        Assert.Equal(["-c:v", "prores_ks", "-profile:v", "1", "-vendor", "apl0"],
            Encoders.VideoArgs("prores_ks", ProxyCodec.ProresLT, 10));
        Assert.Equal(["-c:a", "pcm_s16le"], Encoders.AudioArgs(ProxyCodec.Prores422HQ));
        Assert.Equal("yuv422p10le", Encoders.PixelFormat(ProxyCodec.ProresProxy));
        Assert.Equal("yuv420p", Encoders.PixelFormat(ProxyCodec.Hevc));
    }

    [Fact]
    public void CodecWireNames_RoundTrip()
    {
```

nahradit:

```csharp
            Encoders.VideoArgs("libx264", ProxyCodec.H264, 10));
        Assert.Equal(["-c:v", "prores_ks", "-profile:v", "1", "-vendor", "apl0"],
            Encoders.VideoArgs("prores_ks", ProxyCodec.ProresLT, 10));
        Assert.Equal("yuv422p10le", Encoders.PixelFormat(ProxyCodec.ProresProxy));
        Assert.Equal("yuv420p", Encoders.PixelFormat(ProxyCodec.Hevc));
    }

    // Spec 3.4, R14: every audio stream — AAC 64 kb/s per channel of each stream (128k–512k), ProRes 16-bit PCM.
    [Fact]
    public void AudioArgs_EveryStream()
    {
        Assert.Equal(["-c:a", "pcm_s16le"], Encoders.AudioArgs(ProxyCodec.Prores422HQ, [1, 1, 1, 1]));
        Assert.Equal(["-c:a", "aac", "-b:a:0", "128k", "-b:a:1", "128k", "-b:a:2", "384k", "-b:a:3", "512k"],
            Encoders.AudioArgs(ProxyCodec.H264, [1, 2, 6, 16]));
        Assert.Equal(["-c:a", "aac", "-b:a:0", "128k"], Encoders.AudioArgs(ProxyCodec.Hevc, [0]));
        Assert.Equal(["-c:a", "aac", "-b:a", "192k"], Encoders.AudioArgs(ProxyCodec.H264, []));
    }

    [Fact]
    public void CodecWireNames_RoundTrip()
    {
```

`windows/tests/Offload.Core.Tests/ClipDetectorTests.cs`, řádky 62–67 — najít:

```csharp
        Assert.Equal(4.01, i.Duration);
        Assert.Equal("01:00:00:00", i.Timecode);
        Assert.Equal(4, i.AudioChannels);
        Assert.Equal("Sony", i.Camera);
    }

```

nahradit:

```csharp
        Assert.Equal(4.01, i.Duration);
        Assert.Equal("01:00:00:00", i.Timecode);
        Assert.Equal(4, i.AudioChannels);
        Assert.Equal([2, 2], i.AudioStreams);
        Assert.Equal("Sony", i.Camera);
    }

```

`windows/tests/Offload.Core.Tests/SpaceTests.cs`, řádky 47–52 — najít:

```csharp
        Assert.Equal(220, SpaceEstimator.ProxyMbps(new ProxySettings { Codec = ProxyCodec.Prores422HQ }, 0, 1080));
    }

    [Fact]
    public void Proxy_OffOrSkippedClips_Zero()
    {
```

nahradit:

```csharp
        Assert.Equal(220, SpaceEstimator.ProxyMbps(new ProxySettings { Codec = ProxyCodec.Prores422HQ }, 0, 1080));
    }

    // R14: every audio stream becomes a proxy track — 8 mono tracks of a Sony MXF are 8 × 0.2 Mb/s.
    [Fact]
    public void Proxy_H264_EveryAudioStreamCounts()
    {
        var p = new ProxySettings { Enabled = true, Codec = ProxyCodec.H264, BitrateMbps = 10 };
        var info = new ClipInfo { Duration = 100, Height = 1080, AudioChannels = 8, AudioStreams = [1, 1, 1, 1, 1, 1, 1, 1] };
        // (10 × 1.1 + 0.2 × 8) Mb/s × 100 s = 12.6 Mb/s × 100 s = 157.5 MB
        Assert.Equal(157_500_000, SpaceEstimator.ProxyBytes([C("C0001.MXF", 1)], p, _ => true, _ => info));
    }

    [Fact]
    public void Proxy_OffOrSkippedClips_Zero()
    {
```

`windows/tests/Offload.Core.Tests/AppendEngineTests.cs`, řádky 609–614 — najít:

```csharp
        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(cardPath, "_PROXY", "C0001.mov")));
        Assert.Equal("_PROXY/C0001_2.mov", Assert.Single(report!.Clips).ProxyPath);
        Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "A001_report_0002.html")));
    }
}
```

nahradit:

```csharp
        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(cardPath, "_PROXY", "C0001.mov")));
        Assert.Equal("_PROXY/C0001_2.mov", Assert.Single(report!.Clips).ProxyPath);
        Assert.Equal("Proxy přejmenována na C0001_2 (shoda jmen) — přelinkujte ji ručně.", report.Clips[0].ProxyNote);
        Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "A001_report_0002.html")));
    }
}
```

- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ProxyRendererTests|FullyQualifiedName~TimecodeEncoderTests|FullyQualifiedName~SpaceEstimatorTests|FullyQualifiedName~ClipDetectorTests|FullyQualifiedName~Proxies_ExistingNeverOverwritten"`
Expected: build FAIL — `CS1061: 'ClipInfo' neobsahuje definici pro 'AudioStreams'`, `CS1501: Žádné přetížení pro metodu 'AudioArgs' nepřijímá 2 argumenty`.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Media.cs`, řádky 50–55 — najít:

```csharp
    [JsonPropertyName("frames")] public int? Frames { get; set; }
    [JsonPropertyName("timecode")] public string? Timecode { get; set; }
    [JsonPropertyName("audioChannels")] public int AudioChannels { get; set; }
    [JsonPropertyName("camera")] public string? Camera { get; set; }
    /// Log curve the camera recorded in (e.g. "LogC4"), when the format reports it.
    [JsonPropertyName("logEncoding")] public string? LogEncoding { get; set; }
```

nahradit:

```csharp
    [JsonPropertyName("frames")] public int? Frames { get; set; }
    [JsonPropertyName("timecode")] public string? Timecode { get; set; }
    [JsonPropertyName("audioChannels")] public int AudioChannels { get; set; }
    /// Channels of each audio stream, in stream order (ffprobe) — the proxy gets every one of them (R14). Known only right
    /// after the probe (not in the report); empty for RAW decoders and reports read back.
    [JsonIgnore] public List<int> AudioStreams { get; set; } = [];
    [JsonPropertyName("camera")] public string? Camera { get; set; }
    /// Log curve the camera recorded in (e.g. "LogC4"), when the format reports it.
    [JsonPropertyName("logEncoding")] public string? LogEncoding { get; set; }
```

`windows/src/Offload.Core/ClipDetector.cs`, řádky 117–123 — najít:

```csharp
            info.ColorRange = Str(v, "color_range");
            info.Duration = Dbl(Str(v, "duration"));
        }
        info.AudioChannels = streams.Where(x => Str(x, "codec_type") == "audio").Sum(x => Int(x, "channels") ?? 0);
        if (Dbl(Str(format, "duration")) is double fd) info.Duration = fd;
        var ftags = Tags(format);
        info.Timecode ??= Str(ftags, "timecode") ?? streams.Select(x => Str(Tags(x), "timecode")).FirstOrDefault(t => t != null);
```

nahradit:

```csharp
            info.ColorRange = Str(v, "color_range");
            info.Duration = Dbl(Str(v, "duration"));
        }
        info.AudioStreams = streams.Where(x => Str(x, "codec_type") == "audio").Select(x => Int(x, "channels") ?? 0).ToList();
        info.AudioChannels = info.AudioStreams.Sum();
        if (Dbl(Str(format, "duration")) is double fd) info.Duration = fd;
        var ftags = Tags(format);
        info.Timecode ??= Str(ftags, "timecode") ?? streams.Select(x => Str(Tags(x), "timecode")).FirstOrDefault(t => t != null);
```

`windows/src/Offload.Core/Encoders.cs`, řádky 57–64 — najít:

```csharp
    /// Every proxy is BT.709 legal range (FilterGraph.ToRec709 / TagRec709); the encoder and container say so as well.
    public static readonly string[] Rec709Args = ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"];

    public static string[] AudioArgs(ProxyCodec codec) =>
        codec is ProxyCodec.H264 or ProxyCodec.Hevc ? ["-c:a", "aac", "-b:a", "192k"] : ["-c:a", "pcm_s16le"];

    public static string PixelFormat(ProxyCodec codec) =>
        codec is ProxyCodec.H264 or ProxyCodec.Hevc ? "yuv420p" : "yuv422p10le";
```

nahradit:

```csharp
    /// Every proxy is BT.709 legal range (FilterGraph.ToRec709 / TagRec709); the encoder and container say so as well.
    public static readonly string[] Rec709Args = ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"];

    /// Every audio stream of the source (R14 — Premiere attaches a proxy only with the same tracks). H.264/HEVC: AAC with
    /// 64 kb/s per channel of each stream (`streamChannels`, at least 128k, at most 512k); streams not known (a RAW
    /// decoder's intermediate) all get 192k. ProRes: 16-bit PCM, as before.
    public static List<string> AudioArgs(ProxyCodec codec, IReadOnlyList<int> streamChannels)
    {
        if (codec is not (ProxyCodec.H264 or ProxyCodec.Hevc)) return ["-c:a", "pcm_s16le"];
        var args = new List<string> { "-c:a", "aac" };
        if (streamChannels.Count == 0) args.AddRange(["-b:a", "192k"]);
        for (int i = 0; i < streamChannels.Count; i++)
            args.AddRange([$"-b:a:{i}", $"{AacKbps(streamChannels[i])}k"]);
        return args;
    }

    /// AAC bitrate of one stream: 64 kb/s per channel, 128–512 kb/s.
    public static int AacKbps(int channels) => Math.Clamp(64 * channels, 128, 512);

    public static string PixelFormat(ProxyCodec codec) =>
        codec is ProxyCodec.H264 or ProxyCodec.Hevc ? "yuv420p" : "yuv422p10le";
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 368–380 — najít:

```csharp
            args.AddRange(["-map", "0:v:0"]);
            if (Settings.Audio && info.AudioChannels > 0 && src.AudioFromInput)
            {
                args.AddRange(["-map", "0:a:0?"]);
                args.AddRange(Encoders.AudioArgs(Settings.Codec));
            }
            args.AddRange(["-vf", vf]);
            args.AddRange(Encoders.VideoArgs(EncoderName!, Settings.Codec, Settings.BitrateMbps));
            args.AddRange(Encoders.Rec709Args);
            if (Timecode.IsValid(info.Timecode)) args.AddRange(["-timecode", info.Timecode!]);

            var dir = Path.GetDirectoryName(output)!;
            NativeIO.Mkdirs(dir);
```

nahradit:

```csharp
            args.AddRange(["-map", "0:v:0"]);
            if (Settings.Audio && info.AudioChannels > 0 && src.AudioFromInput)
            {
                args.AddRange(["-map", "0:a?"]);   // every audio stream (R14)
                args.AddRange(Encoders.AudioArgs(Settings.Codec, info.AudioStreams));
            }
            args.AddRange(["-vf", vf]);
            args.AddRange(Encoders.VideoArgs(EncoderName!, Settings.Codec, Settings.BitrateMbps));
            args.AddRange(Encoders.Rec709Args);
            if (Timecode.IsValid(info.Timecode))
            {
                args.AddRange(["-timecode", info.Timecode!]);
                // The MOV muxer writes it into the timecode track (tmcd) as its source reference: Premiere reads it as
                // Tape Name, Resolve as Reel (ffprobe shows `reel_name` on the data stream). Without TC there is no tmcd.
                if (info.Reel is { Length: > 0 } reel) args.AddRange(["-metadata:s:v:0", $"reel_name={reel}"]);
            }

            var dir = Path.GetDirectoryName(output)!;
            NativeIO.Mkdirs(dir);
```

`windows/src/Offload.Core/Messages.cs`, řádky 161–166 — najít:

```csharp

    // Clip notes (report, job card)
    public const string NoVerifiedCopy = "žádná ověřená kopie";
    public const string FfmpegNotFound = "ffmpeg nenalezen";
    public const string FfprobeNotFound = "ffprobe nenalezen";
    public const string UnknownFormat = "neznámý formát";
```

nahradit:

```csharp

    // Clip notes (report, job card)
    public const string NoVerifiedCopy = "žádná ověřená kopie";
    public static string ProxyRenamed(string name) => $"Proxy přejmenována na {name} (shoda jmen) — přelinkujte ji ručně.";
    public const string FfmpegNotFound = "ffmpeg nenalezen";
    public const string FfprobeNotFound = "ffprobe nenalezen";
    public const string UnknownFormat = "neznámý formát";
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 517–522 — najít:

```csharp
                    f => Update(s => s with { ClipFraction = f }), () => IsCancelled);
                entries[i].ProxyStatus = ProxyStatus.Done;
                entries[i].ProxyPath = rel;
                if (Config.Proxy.CopyToAllDestinations)
                {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
```

nahradit:

```csharp
                    f => Update(s => s with { ClipFraction = f }), () => IsCancelled);
                entries[i].ProxyStatus = ProxyStatus.Done;
                entries[i].ProxyPath = rel;
                // Spec 3.4: relinking goes by name — a proxy that had to be renamed must be attached by hand.
                if (name != clip.Name) entries[i].ProxyNote = entries[i].ProxyNote is string earlier ? $"{earlier}; {Msg.ProxyRenamed(name)}" : Msg.ProxyRenamed(name);
                if (Config.Proxy.CopyToAllDestinations)
                {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
```

`windows/src/Offload.Core/Space/SpaceEstimator.cs`, řádky 65–71 — najít:

```csharp
            if ((c.IsCameraProxy && p.SkipCameraProxies) || !renderable(c)) continue;
            var i = info?.Invoke(c);
            double seconds = i?.Duration ?? c.Bytes * 8.0 / (SourceMbps(c) * 1e6);
            int audio = p.Audio && (i == null || i.AudioChannels > 0) ? 1 : 0;
            int height = Math.Min(p.Height, i?.Height ?? p.Height);
            bytes += seconds * ProxyMbps(p, audio, height) * 1e6 / 8;
        }
```

nahradit:

```csharp
            if ((c.IsCameraProxy && p.SkipCameraProxies) || !renderable(c)) continue;
            var i = info?.Invoke(c);
            double seconds = i?.Duration ?? c.Bytes * 8.0 / (SourceMbps(c) * 1e6);
            // Every audio stream becomes a track of the proxy (R14); before the analysis, and for RAW, one is assumed.
            int audio = !p.Audio ? 0 : i == null ? 1 : i.AudioStreams.Count > 0 ? i.AudioStreams.Count : i.AudioChannels > 0 ? 1 : 0;
            int height = Math.Min(p.Height, i?.Height ?? p.Height);
            bytes += seconds * ProxyMbps(p, audio, height) * 1e6 / 8;
        }
```

- [ ] **Step 4: Spustit — projdou**

Run: stejný filtr jako v kroku 2.
Expected: PASS — `Render_ReelNameOnTimecodeTrack_EveryAudioStream`, `Render_SonyMxf_EightMonoTracks`, `Render_NoTimecode_NoTimecodeTrackNoReel` běží s ffmpeg (bez něj se přeskočí — pak je spusťte na PC s ffmpeg, jinak úkol není hotový). Pak `dotnet build windows\Offload.sln -warnaserror` a celá sada.

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 20–25 — najít:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 2 — cívka klipu (`ReelResolver`): tag kamery z ffprobe (`reel_name`, `tape_name`, `reel` — u MOV na stopě timecodu, u MXF na každém proudu), jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty; v reportu `clips[].info.reel`
- Úkol 1 — `CardRecord` úplný: klipy všech generací (doplnění za dřívějšími, tatáž cesta = nejnovější záznam), součty přes generace (soubory, data, začátek–konec), selhané soubory podle nejnovějšího záznamu, „ověřeno“ jen když jsou ověřené všechny generace a žádný report doplnění není nečitelný; generace bez analýzy klipů dostane klipy nalezené v jejích souborech (bez metadat); snese report z Macu (bez `generation`, `lut`, `proxyEncoder`, `md5`) i `"clips": null`

````

nahradit:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 3 — proxy pro přelinkování: s platným TC navíc `reel_name` (muxer MOV ho zapíše do stopy timecodu `tmcd`, ffprobe ho tam ukáže; ověřeno s ffmpeg 8.1), všechny zvukové proudy zdroje (`-map 0:a?`; H.264/HEVC AAC 64 kb/s na kanál každého proudu, 128–512 kb/s; ProRes PCM), bez TC ani `tmcd`, ani cívka; proxy přejmenovaná kvůli shodě jmen má poznámku „Proxy přejmenována na … (shoda jmen) — přelinkujte ji ručně.“; odhad místa počítá 0,2 Mb/s na každou zvukovou stopu
- Úkol 2 — cívka klipu (`ReelResolver`): tag kamery z ffprobe (`reel_name`, `tape_name`, `reel` — u MOV na stopě timecodu, u MXF na každém proudu), jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty; v reportu `clips[].info.reel`
- Úkol 1 — `CardRecord` úplný: klipy všech generací (doplnění za dřívějšími, tatáž cesta = nejnovější záznam), součty přes generace (soubory, data, začátek–konec), selhané soubory podle nejnovějšího záznamu, „ověřeno“ jen když jsou ověřené všechny generace a žádný report doplnění není nečitelný; generace bez analýzy klipů dostane klipy nalezené v jejích souborech (bez metadat); snese report z Macu (bez `generation`, `lut`, `proxyEncoder`, `md5`) i `"clips": null`

````

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Windows: proxies for relinking (reel name on the timecode track, every audio stream)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/ClipDetector.cs windows/src/Offload.Core/Encoders.cs windows/src/Offload.Core/Media.cs windows/src/Offload.Core/Messages.cs windows/src/Offload.Core/OffloadJob.cs windows/src/Offload.Core/ProxyRenderer.cs windows/src/Offload.Core/Space/SpaceEstimator.cs windows/tests/Offload.Core.Tests/AppendEngineTests.cs windows/tests/Offload.Core.Tests/ClipDetectorTests.cs windows/tests/Offload.Core.Tests/ProxyRendererTests.cs windows/tests/Offload.Core.Tests/SpaceTests.cs windows/tests/Offload.Core.Tests/TestMedia.cs windows/tests/Offload.Core.Tests/TimecodeEncoderTests.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

---

### Task 4: Hash jádro — `HashKind`, souběžné `SourceHasher`y, `FileHashes`, ověření všemi součty

Spec §3.5 „Jádro“, R16, §6.2 úkol 4. `HashKind` (`Xxh64 = 1`, `Md5 = 2`, příznaky; xxh64 vždy). Jeden `SourceHasher` na algoritmus, každý na vlastním `SerialWorker` (MD5 ~0,7 GB/s nebrzdí xxh64 ani čtení karty — jen pool bloků, když MD5 zaostane o celý pool); `Chunk` má o konzumenta víc. `FileHashes(ulong? Xxh64, byte[]? Md5)` místo `ulong?[]`. `DestWriter.Verify` hashuje všemi zvolenými v jednom čtení (`Hashing.HashFile(path, buf, kinds)`), nesouhlas MD5 = `MD5 MISMATCH`. Celá kopie bere součty z `OffloadOptions.Hashes`, doplnění z dřívější zálohy (`OffloadPlan.Hashes` z `AppendPlan.HashAlgorithms`). Výstupy (report, `.md5`, MHL, GUI, CLI) jsou úkol 5 — tady MD5 jen počítá a ověřuje.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 20–25)
- Modify: `windows/src/Offload.Core/DestWriter.cs` (řádky 169–178, 180–191)
- Modify: `windows/src/Offload.Core/Hashing.cs` (řádky 2–7, 12–17)
- Modify: `windows/src/Offload.Core/Models.cs` (řádky 8–13, 59–64)
- Modify: `windows/src/Offload.Core/OffloadEngine.Run.cs` (řádky 20–30, 42–63, 67–79, 88–104, 122–129)
- Modify: `windows/src/Offload.Core/OffloadEngine.cs` (řádky 42–48, 78–83)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 14–19, 107–112)
- Modify: `windows/src/Offload.Core/OffloadReport.cs` (řádky 69–75, 79–85)
- Modify: `windows/src/Offload.Core/SourceHasher.cs` (řádky 1–18, 22–28)
- Modify: `windows/tests/Offload.Core.Tests/DestWriterTests.cs` (řádky 124–130, 150–173)
- Modify: `windows/tests/Offload.Core.Tests/EngineTests.cs` (řádky 63–68)
- Modify: `windows/tests/Offload.Core.Tests/HashingTests.cs` (řádky 34–37)
- Modify: `windows/tests/Offload.Core.Tests/ReportTests.cs` (řádky 101–107, 146–152)

**Interfaces:**
- Consumes: `HashAlgo.Create(string)` / `IFileHash` (plán 6), `SerialWorker`, `Chunk(AlignedBuffer, int, long, int consumers, BufferPool)`, `AppendPlan.HashAlgorithms` (plán 6, zatím vždy `["xxh64"]`).
- Produces: `[Flags] enum HashKind { Xxh64 = 1, Md5 = 2, Both = 3 }`; `static class HashKinds { Normalize(HashKind) → HashKind; Names(HashKind) → IReadOnlyList<string>; FromNames(IEnumerable<string>?) → HashKind }`; `readonly record struct FileHashes(ulong? Xxh64, byte[]? Md5)` s `Xxh64Hex`, `Md5Hex`; `Hashing.HashFile(string path, AlignedBuffer buffer, HashKind kinds, Action<int>? progress = null, CancellationToken ct = default) → (FileHashes Hashes, long Size)`; `internal SourceHasher(HashKind kind, int fileCount)` s `Kind`, `Values` (`string?[]` hex), `static For(HashKind kinds, int fileCount) → List<SourceHasher>`, `static Combine(IReadOnlyList<SourceHasher>, int fileCount) → FileHashes[]`; `DestWriter.Verify(IReadOnlyList<SourceFile> files, IReadOnlyList<FileHashes> sourceHashes, HashKind kinds, CancellationToken ct)`; `OffloadOptions.Hashes`, `OffloadPlan.Hashes` (`init`), `JobConfig.Hashes` (vše `HashKind`, výchozí `Xxh64`); `OffloadReport.Build(OffloadPlan, IReadOnlyList<FileHashes> hashes, …)`.

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/HashingTests.cs`, řádky 34–37 — najít:

```csharp
        Assert.Equal(size, read);
        Assert.Equal(size, progress);
    }
}
```

nahradit:

```csharp
        Assert.Equal(size, read);
        Assert.Equal(size, progress);
    }

    // Spec 3.5: xxh64 and MD5 in one read — RFC 1321 vectors, and across block boundaries.
    [Fact]
    public void HashFile_Both_Md5Vectors()
    {
        using var dir = new TempDir();
        using var buf = new AlignedBuffer(4096);
        var empty = Path.Combine(dir.Path, "empty");
        var abc = Path.Combine(dir.Path, "abc");
        File.WriteAllBytes(empty, []);
        File.WriteAllText(abc, "abc");
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", Hashing.HashFile(empty, buf, HashKind.Both).Hashes.Md5Hex);
        var (h, size) = Hashing.HashFile(abc, buf, HashKind.Both);
        Assert.Equal(("900150983cd24fb0d6963f7d28e17f72", 0x44bc2cf5ad770999UL, 3L), (h.Md5Hex, h.Xxh64!.Value, size));
        Assert.Null(Hashing.HashFile(abc, buf, HashKind.Xxh64).Hashes.Md5);

        var big = Path.Combine(dir.Path, "big");
        var data = TestFs.WriteRandom(big, 3 * 4096 + 1, 7);
        var (hb, _) = Hashing.HashFile(big, buf, HashKind.Md5);   // R16: MD5 alone still brings xxh64
        Assert.Equal(System.Security.Cryptography.MD5.HashData(data), hb.Md5);
        Assert.Equal(XxHash64.HashToUInt64(data), hb.Xxh64);
    }

    [Fact]
    public void HashKinds_Xxh64Always()
    {
        Assert.Equal(HashKind.Both, HashKinds.Normalize(HashKind.Md5));
        Assert.Equal(["xxh64", "md5"], HashKinds.Names(HashKind.Md5));
        Assert.Equal(["xxh64"], HashKinds.Names(HashKind.Xxh64));
        Assert.Equal(HashKind.Both, HashKinds.FromNames(["xxh64", "md5"]));
        Assert.Equal(HashKind.Xxh64, HashKinds.FromNames(null));
        Assert.Equal(HashKind.Xxh64, HashKinds.FromNames(["xxh64"]));
    }
}
```

`windows/tests/Offload.Core.Tests/DestWriterTests.cs`, řádky 124–130 — najít:

```csharp
        File.WriteAllBytes(Path.Combine(card, "b"), bytes);
        File.WriteAllBytes(Path.Combine(card, "c"), c[..4000]);

        w.Verify(files, [XxHash64.HashToUInt64(a), XxHash64.HashToUInt64(b), XxHash64.HashToUInt64(c)], CancellationToken.None);

        Assert.Equal(FileState.Verified, w.Results[0].State);
        Assert.Equal(FileState.Failed, w.Results[1].State);
```

nahradit:

```csharp
        File.WriteAllBytes(Path.Combine(card, "b"), bytes);
        File.WriteAllBytes(Path.Combine(card, "c"), c[..4000]);

        w.Verify(files, [Xx(a), Xx(b), Xx(c)], HashKind.Xxh64, CancellationToken.None);

        Assert.Equal(FileState.Verified, w.Results[0].State);
        Assert.Equal(FileState.Failed, w.Results[1].State);
```

`windows/tests/Offload.Core.Tests/DestWriterTests.cs`, řádky 150–173 — najít:

```csharp
        Assert.Equal("A/B/.x.mov.offload-part", DestWriter.PartName("A/B/x.mov"));
    }

    [Fact]
    public void SourceHasher_HashesChunkStream()
    {
        var data = new byte[3 * Chunk + 5];
        new Random(9).NextBytes(data);
        using var pool = new BufferPool(4, Chunk);
        using var hasher = new SourceHasher(2);
        hasher.Begin();
        for (long off = 0; off < data.Length; off += Chunk)
        {
            int n = (int)Math.Min(Chunk, data.Length - off);
            var buf = pool.Take();
            data.AsSpan((int)off, n).CopyTo(buf.Span);
            hasher.Update(new Chunk(buf, n, off, 1, pool));
        }
        hasher.Finish(1);
        hasher.Wait();
        Assert.Null(hasher.Hashes[0]);
        Assert.Equal(XxHash64.HashToUInt64(data), hasher.Hashes[1]);
    }
}
```

nahradit:

```csharp
        Assert.Equal("A/B/.x.mov.offload-part", DestWriter.PartName("A/B/x.mov"));
    }

    static FileHashes Xx(byte[] data) => new(XxHash64.HashToUInt64(data), null);

    // Spec 3.5: one hasher per algorithm, each on its own thread, every block shared by both (two consumers).
    [Fact]
    public void SourceHashers_Xxh64AndMd5_HashTheChunkStream()
    {
        var data = new byte[3 * Chunk + 5];
        new Random(9).NextBytes(data);
        using var pool = new BufferPool(4, Chunk);
        var hashers = SourceHasher.For(HashKind.Both, 2);
        try
        {
            Assert.Equal([HashKind.Xxh64, HashKind.Md5], hashers.Select(h => h.Kind));
            foreach (var h in hashers) h.Begin();
            for (long off = 0; off < data.Length; off += Chunk)
            {
                int n = (int)Math.Min(Chunk, data.Length - off);
                var buf = pool.Take();
                data.AsSpan((int)off, n).CopyTo(buf.Span);
                var chunk = new Chunk(buf, n, off, hashers.Count, pool);
                foreach (var h in hashers) h.Update(chunk);
            }
            foreach (var h in hashers) h.Finish(1);
            foreach (var h in hashers) h.Wait();
            var hashes = SourceHasher.Combine(hashers, 2);
            Assert.Equal(default, hashes[0]);
            Assert.Equal(XxHash64.HashToUInt64(data), hashes[1].Xxh64);
            Assert.Equal(System.Security.Cryptography.MD5.HashData(data), hashes[1].Md5);
            Assert.Single(SourceHasher.For(HashKind.Xxh64, 1));
        }
        finally
        {
            foreach (var h in hashers) h.Dispose();
        }
    }

    // Spec 3.5: in "both" a copy whose MD5 differs fails even when its xxh64 matches (a wrong source MD5 stands in for it).
    [Fact]
    public void Verify_Both_Md5Mismatch_Fails()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var a = new byte[5000]; new Random(1).NextBytes(a);
        var b = new byte[5000]; new Random(2).NextBytes(b);
        var files = new[] { new SourceFile("a", 5000, When), new SourceFile("b", 5000, When) };
        using var pool = new BufferPool(4, Chunk);
        using var w = new DestWriter(0, card, 2, new OffloadOptions { ChunkSize = Chunk });
        Feed(w, pool, a, 0, files[0]);
        Feed(w, pool, b, 1, files[1]);
        var wrongMd5 = System.Security.Cryptography.MD5.HashData(a);

        w.Verify(files, [new FileHashes(XxHash64.HashToUInt64(a), System.Security.Cryptography.MD5.HashData(a)), new FileHashes(XxHash64.HashToUInt64(b), wrongMd5)],
            HashKind.Both, CancellationToken.None);

        Assert.Equal(FileState.Verified, w.Results[0].State);
        Assert.Equal(FileState.Failed, w.Results[1].State);
        Assert.StartsWith("MD5 MISMATCH: dest ", w.Results[1].Error);
        Assert.Equal(10000, w.Verified);
    }
}
```

`windows/tests/Offload.Core.Tests/EngineTests.cs`, řádky 63–68 — najít:

```csharp
        Assert.All(snap.VerifiedBytes, v => Assert.Equal(report.TotalBytes, v));
    }

    [Fact]
    public void DefaultBlockSize_FileOneByteOverBlock()
    {
```

nahradit:

```csharp
        Assert.All(snap.VerifiedBytes, v => Assert.Equal(report.TotalBytes, v));
    }

    // Spec 3.5 / review focus 4: with MD5 a second hasher shares every block — a pool of just two blocks still flows
    // (a wrong consumer count would hang or hand a block back early), and every file verifies through both hashes.
    [Fact]
    public async Task Both_TwoBlockPool_EveryFileVerified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var files = MakeCard(card);
        var options = Options();
        options.BufferCount = 2;
        options.Hashes = HashKind.Both;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");
        Assert.Equal(HashKind.Both, plan.Hashes);

        // A block that never comes back to the pool hangs the copy: TimeoutException instead of a hung test run.
        var report = await Task.Run(() => engine.Run(plan)).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToEject);
        foreach (var cardPath in plan.CardPaths)
            foreach (var (rel, data) in files)
                Assert.Equal(data, File.ReadAllBytes(Layout.Join(cardPath, rel)));
    }

    // Spec 3.5: a byte changed on a destination after the copy fails verification in both modes.
    [Theory]
    [InlineData(HashKind.Xxh64, "HASH MISMATCH")]
    [InlineData(HashKind.Both, "HASH MISMATCH")]
    public void TamperedDestination_FailsInBothModes(HashKind kinds, string error)
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 3 * Chunk + 7, 1);
        var options = Options();
        options.Hashes = kinds;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");
        options.BeforeVerify = () =>
        {
            var copy = Layout.Join(plan.CardPaths[1], "CLIP/a.mov");
            var bytes = File.ReadAllBytes(copy);
            bytes[Chunk + 3] ^= 0x01;
            File.WriteAllBytes(copy, bytes);
        };

        var report = engine.Run(plan);

        var f = Assert.Single(report.Files);
        Assert.Equal(["verified", "failed"], f.Destinations);
        Assert.StartsWith(error, f.Errors[1]);
        Assert.False(report.Success);
    }

    [Fact]
    public void DefaultBlockSize_FileOneByteOverBlock()
    {
```

`windows/tests/Offload.Core.Tests/ReportTests.cs`, řádky 101–107 — najít:

```csharp
                Dirs = [], Skipped = [],
            },
        };
        var r = OffloadReport.Build(plan, [1UL, 2UL], [null, null],
            [[new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Written, null)]],
            [true], verified: false, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);
        Assert.Equal(2, r.Destinations[0].FilesOK);
```

nahradit:

```csharp
                Dirs = [], Skipped = [],
            },
        };
        var r = OffloadReport.Build(plan, [new FileHashes(1UL, null), new FileHashes(2UL, null)], [null, null],
            [[new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Written, null)]],
            [true], verified: false, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);
        Assert.Equal(2, r.Destinations[0].FilesOK);
```

`windows/tests/Offload.Core.Tests/ReportTests.cs`, řádky 146–152 — najít:

```csharp
                Dirs = [], Skipped = [],
            },
        };
        var r = OffloadReport.Build(plan, [1UL, null], [null, "read failed"],
            [
                [new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Failed, "source: read failed")],
                [new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Failed, "source: read failed")],
```

nahradit:

```csharp
                Dirs = [], Skipped = [],
            },
        };
        var r = OffloadReport.Build(plan, [new FileHashes(1UL, null), default], [null, "read failed"],
            [
                [new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Failed, "source: read failed")],
                [new DestFileResult(FileState.Verified, null), new DestFileResult(FileState.Failed, "source: read failed")],
```

- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~HashingTests|FullyQualifiedName~DestWriterTests|FullyQualifiedName~EngineTests|FullyQualifiedName~ReportTests"`
Expected: build FAIL — `CS0246: Typ nebo název oboru názvů 'HashKind' se nenašel`, `'FileHashes' se nenašel`.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Hashing.cs`, řádky 2–7 — najít:

```csharp

namespace Offload.Core;

public static class Hashing
{
    /// Hashes a whole file straight from the media (no file cache). XXH64 is the hash DIT tools and ASC MHL use.
```

nahradit:

```csharp

namespace Offload.Core;

/// The checksums a copy computes (spec 3.5, R16): xxh64 always, MD5 only in addition — never MD5 alone, so every
/// backup stays verifiable by the Mac and by older `offload verify`.
[Flags]
public enum HashKind
{
    Xxh64 = 1,
    Md5 = 2,
    Both = Xxh64 | Md5,
}

public static class HashKinds
{
    /// R16: xxh64 is always in.
    public static HashKind Normalize(HashKind kinds) => kinds | HashKind.Xxh64;

    /// The ASC MHL / report names, in that order: ["xxh64"] or ["xxh64", "md5"].
    public static IReadOnlyList<string> Names(HashKind kinds) =>
        Normalize(kinds).HasFlag(HashKind.Md5) ? ["xxh64", "md5"] : ["xxh64"];

    /// From a report's hash names ("md5" anywhere → both); anything else is xxh64.
    public static HashKind FromNames(IEnumerable<string>? names) =>
        names != null && names.Contains("md5", StringComparer.OrdinalIgnoreCase) ? HashKind.Both : HashKind.Xxh64;
}

/// The checksums of one file; null = not computed (the file failed, or MD5 was not asked for).
public readonly record struct FileHashes(ulong? Xxh64, byte[]? Md5)
{
    public string? Xxh64Hex => Xxh64 is ulong x ? Format.Hex64(x) : null;
    public string? Md5Hex => Md5 is { } m ? Convert.ToHexStringLower(m) : null;
}

public static class Hashing
{
    /// Hashes a whole file straight from the media (no file cache). XXH64 is the hash DIT tools and ASC MHL use.
```

`windows/src/Offload.Core/Hashing.cs`, řádky 12–17 — najít:

```csharp
        return (Convert.ToUInt64(xx.Finish(), 16), size);
    }

    /// The one read loop: the file is read once, straight from the media, and every hash in `hashers` is fed each block.
    /// Returns the size read.
    public static long HashFile(string path, AlignedBuffer buffer, IReadOnlyList<IFileHash> hashers, Action<int>? progress = null, CancellationToken ct = default)
```

nahradit:

```csharp
        return (Convert.ToUInt64(xx.Finish(), 16), size);
    }

    /// Every hash in `kinds` (xxh64 always) in one read of the file — the read-back verification of a copy.
    public static (FileHashes Hashes, long Size) HashFile(string path, AlignedBuffer buffer, HashKind kinds, Action<int>? progress = null, CancellationToken ct = default)
    {
        var xx = HashAlgo.Create("xxh64");
        var md5 = HashKinds.Normalize(kinds).HasFlag(HashKind.Md5) ? HashAlgo.Create("md5") : null;
        long size = HashFile(path, buffer, md5 == null ? [xx] : [xx, md5], progress, ct);
        return (new FileHashes(Convert.ToUInt64(xx.Finish(), 16), md5 == null ? null : Convert.FromHexString(md5.Finish())), size);
    }

    /// The one read loop: the file is read once, straight from the media, and every hash in `hashers` is fed each block.
    /// Returns the size read.
    public static long HashFile(string path, AlignedBuffer buffer, IReadOnlyList<IFileHash> hashers, Action<int>? progress = null, CancellationToken ct = default)
```

`windows/src/Offload.Core/SourceHasher.cs`, řádky 1–18 — najít:

```csharp
using System.IO.Hashing;

namespace Offload.Core;

/// Hashes the source stream on its own thread, so hashing never blocks the card read.
internal sealed class SourceHasher : IDisposable
{
    readonly SerialWorker worker = new("offload.hash");
    XxHash64? stream;

    public ulong?[] Hashes { get; }

    public SourceHasher(int fileCount) => Hashes = new ulong?[fileCount];

    public void Begin() => worker.Post(() => stream = new XxHash64());

    public void Update(Chunk c) => worker.Post(() =>
    {
```

nahradit:

```csharp
using Offload.Core.Verification;

namespace Offload.Core;

/// Hashes the source stream on its own thread, one algorithm per hasher (spec 3.5): with MD5 a second hasher runs
/// beside xxh64 on its own thread, so the slower MD5 never holds up xxh64 or the card read — only the buffer pool,
/// when it falls a whole pool behind.
internal sealed class SourceHasher : IDisposable
{
    readonly SerialWorker worker;
    readonly string algorithm;
    IFileHash? stream;

    public HashKind Kind { get; }
    /// Lower-case hex per file; null when the file was not hashed to the end (it failed).
    public string?[] Values { get; }

    public SourceHasher(HashKind kind, int fileCount)
    {
        Kind = kind;
        algorithm = kind == HashKind.Md5 ? "md5" : "xxh64";
        worker = new SerialWorker("offload.hash." + algorithm);
        Values = new string?[fileCount];
    }

    /// One hasher per kind in `kinds` (xxh64 always first).
    public static List<SourceHasher> For(HashKind kinds, int fileCount)
    {
        var all = new List<SourceHasher> { new(HashKind.Xxh64, fileCount) };
        if (HashKinds.Normalize(kinds).HasFlag(HashKind.Md5)) all.Add(new SourceHasher(HashKind.Md5, fileCount));
        return all;
    }

    /// File `index`'s hashes from every hasher (after Wait).
    public static FileHashes[] Combine(IReadOnlyList<SourceHasher> hashers, int fileCount)
    {
        var xx = hashers.FirstOrDefault(h => h.Kind == HashKind.Xxh64);
        var md5 = hashers.FirstOrDefault(h => h.Kind == HashKind.Md5);
        return Enumerable.Range(0, fileCount).Select(i => new FileHashes(
            xx?.Values[i] is string x ? Convert.ToUInt64(x, 16) : null,
            md5?.Values[i] is string m ? Convert.FromHexString(m) : null)).ToArray();
    }

    public void Begin() => worker.Post(() => stream = HashAlgo.Create(algorithm));

    public void Update(Chunk c) => worker.Post(() =>
    {
```

`windows/src/Offload.Core/SourceHasher.cs`, řádky 22–28 — najít:

```csharp

    public void Finish(int index) => worker.Post(() =>
    {
        Hashes[index] = stream?.GetCurrentHashAsUInt64();
        stream = null;
    });

```

nahradit:

```csharp

    public void Finish(int index) => worker.Post(() =>
    {
        Values[index] = stream?.Finish();
        stream = null;
    });

```

`windows/src/Offload.Core/DestWriter.cs`, řádky 169–178 — najít:

```csharp

    public void Wait() => worker.Wait();

    /// Re-reads every written file from the disk and compares it with the source hash.
    public void Verify(IReadOnlyList<SourceFile> files, ulong?[] sourceHashes, CancellationToken ct)
    {
        using var buf = new AlignedBuffer(options.ChunkSize);
        for (int i = 0; i < files.Count; i++)
        {
            if (Results[i].State != FileState.Written) continue;
```

nahradit:

```csharp

    public void Wait() => worker.Wait();

    /// Re-reads every written file from the disk — once, through every hash in `kinds` — and compares it with the
    /// source hashes. The error texts stay English: they go into the report JSON shared with the Mac.
    public void Verify(IReadOnlyList<SourceFile> files, IReadOnlyList<FileHashes> sourceHashes, HashKind kinds, CancellationToken ct)
    {
        using var buf = new AlignedBuffer(options.ChunkSize);
        bool md5 = HashKinds.Normalize(kinds).HasFlag(HashKind.Md5);
        for (int i = 0; i < files.Count; i++)
        {
            if (Results[i].State != FileState.Written) continue;
```

`windows/src/Offload.Core/DestWriter.cs`, řádky 180–191 — najít:

```csharp
            var f = files[i];
            try
            {
                var (hash, size) = Hashing.HashFile(Layout.Join(CardPath, f.RelPath), buf, n => Interlocked.Add(ref verified, n), ct);
                if (size != f.Size)
                    Results[i] = new DestFileResult(FileState.Failed, $"size mismatch: {size} != {f.Size}");
                else if (hash != sourceHashes[i])
                    Results[i] = new DestFileResult(FileState.Failed,
                        $"HASH MISMATCH: dest {Format.Hex64(hash)} != source {(sourceHashes[i] is ulong s ? Format.Hex64(s) : "-")}");
                else
                    Results[i] = new DestFileResult(FileState.Verified, null);
            }
```

nahradit:

```csharp
            var f = files[i];
            try
            {
                var (got, size) = Hashing.HashFile(Layout.Join(CardPath, f.RelPath), buf, kinds, n => Interlocked.Add(ref verified, n), ct);
                var want = sourceHashes[i];
                if (size != f.Size)
                    Results[i] = new DestFileResult(FileState.Failed, $"size mismatch: {size} != {f.Size}");
                else if (got.Xxh64 != want.Xxh64)
                    Results[i] = new DestFileResult(FileState.Failed, $"HASH MISMATCH: dest {got.Xxh64Hex} != source {want.Xxh64Hex ?? "-"}");
                else if (md5 && (want.Md5 == null || !got.Md5!.AsSpan().SequenceEqual(want.Md5)))
                    Results[i] = new DestFileResult(FileState.Failed, $"MD5 MISMATCH: dest {got.Md5Hex} != source {want.Md5Hex ?? "-"}");
                else
                    Results[i] = new DestFileResult(FileState.Verified, null);
            }
```

`windows/src/Offload.Core/OffloadEngine.Run.cs`, řádky 20–30 — najít:

```csharp
        }
        // Created before the try and disposed last: writer queues may still hold chunks that point into the pool's native buffers.
        BufferPool? pool = null;
        SourceHasher? hasher = null;
        try
        {
            pool = new BufferPool(Options.BufferCount, Options.ChunkSize);
            hasher = new SourceHasher(files.Count);
            var sourceErrors = new string?[files.Count];

            // Copy: the card is read exactly once; every block fans out to the hasher and all destinations.
```

nahradit:

```csharp
        }
        // Created before the try and disposed last: writer queues may still hold chunks that point into the pool's native buffers.
        BufferPool? pool = null;
        var hashers = new List<SourceHasher>();
        try
        {
            pool = new BufferPool(Options.BufferCount, Options.ChunkSize);
            hashers = SourceHasher.For(plan.Hashes, files.Count);   // xxh64, and MD5 on its own thread (spec 3.5)
            var sourceErrors = new string?[files.Count];

            // Copy: the card is read exactly once; every block fans out to the hasher and all destinations.
```

`windows/src/Offload.Core/OffloadEngine.Run.cs`, řádky 42–63 — najít:

```csharp
                    fileIndex = i;
                    currentFile = f.RelPath;
                }
                var failure = CopyFile(plan.Source, f, ws, hasher, pool);
                if (failure != null)
                {
                    sourceErrors[i] = failure;
                    hasher.Abort();
                    foreach (var w in ws) w.Abort(i, "source: " + failure);
                    lock (gate) errorCount++;
                }
                else
                {
                    hasher.Finish(i);
                    foreach (var w in ws) w.Finish(i, f);
                }
            }
            hasher.Wait();
            foreach (var w in ws) w.Wait();

            SetPhase(CopyPhase.Flush);
            OnEachDestination(ws, w => w.FlushDevice());
```

nahradit:

```csharp
                    fileIndex = i;
                    currentFile = f.RelPath;
                }
                var failure = CopyFile(plan.Source, f, ws, hashers, pool);
                if (failure != null)
                {
                    sourceErrors[i] = failure;
                    foreach (var h in hashers) h.Abort();
                    foreach (var w in ws) w.Abort(i, "source: " + failure);
                    lock (gate) errorCount++;
                }
                else
                {
                    foreach (var h in hashers) h.Finish(i);
                    foreach (var w in ws) w.Finish(i, f);
                }
            }
            foreach (var h in hashers) h.Wait();
            foreach (var w in ws) w.Wait();
            var hashes = SourceHasher.Combine(hashers, files.Count);

            SetPhase(CopyPhase.Flush);
            OnEachDestination(ws, w => w.FlushDevice());
```

`windows/src/Offload.Core/OffloadEngine.Run.cs`, řádky 67–79 — najít:

```csharp
            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                var hashes = hasher.Hashes;
                OnEachDestination(ws, w => w.Verify(files, hashes, cts.Token));
            }
            var finished = DateTime.UtcNow;

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, hasher.Hashes, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished);
            var reportErrors = new List<string>();
            foreach (var path in cardPaths)
```

nahradit:

```csharp
            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                OnEachDestination(ws, w => w.Verify(files, hashes, plan.Hashes, cts.Token));
            }
            var finished = DateTime.UtcNow;

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, hashes, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished);
            var reportErrors = new List<string>();
            foreach (var path in cardPaths)
```

`windows/src/Offload.Core/OffloadEngine.Run.cs`, řádky 88–104 — najít:

```csharp
        finally
        {
            foreach (var w in ws) w.Dispose();
            hasher?.Dispose();
            pool?.Dispose();
        }
    }

    /// Reads one file block by block and hands every block to the hasher and all writers. Returns the failure, or null.
    string? CopyFile(string sourceRoot, SourceFile f, DestWriter[] ws, SourceHasher hasher, BufferPool pool)
    {
        var src = Layout.Join(sourceRoot, f.RelPath);
        foreach (var w in ws) w.Begin(f);
        hasher.Begin();
        FileReader reader;
        try { reader = new FileReader(src); }
        catch (Exception e) { return e.Message; }
```

nahradit:

```csharp
        finally
        {
            foreach (var w in ws) w.Dispose();
            foreach (var h in hashers) h.Dispose();
            pool?.Dispose();
        }
    }

    /// Reads one file block by block and hands every block to the hashers and all writers. Returns the failure, or null.
    string? CopyFile(string sourceRoot, SourceFile f, DestWriter[] ws, IReadOnlyList<SourceHasher> hashers, BufferPool pool)
    {
        var src = Layout.Join(sourceRoot, f.RelPath);
        foreach (var w in ws) w.Begin(f);
        foreach (var h in hashers) h.Begin();
        FileReader reader;
        try { reader = new FileReader(src); }
        catch (Exception e) { return e.Message; }
```

`windows/src/Offload.Core/OffloadEngine.Run.cs`, řádky 122–129 — najít:

```csharp
                    break;
                }
                if (n < Options.ChunkSize) buf.Span[n..].Clear();   // unbuffered writes round up to the sector size: zero the overhang
                var chunk = new Chunk(buf, n, offset, ws.Length + 1, pool);
                hasher.Update(chunk);
                foreach (var w in ws) w.Write(chunk);
                offset += n;
                Interlocked.Add(ref readBytes, n);
```

nahradit:

```csharp
                    break;
                }
                if (n < Options.ChunkSize) buf.Span[n..].Clear();   // unbuffered writes round up to the sector size: zero the overhang
                // Every hasher and every writer releases the block once (MD5 is one consumer more, spec 3.5).
                var chunk = new Chunk(buf, n, offset, ws.Length + hashers.Count, pool);
                foreach (var h in hashers) h.Update(chunk);
                foreach (var w in ws) w.Write(chunk);
                offset += n;
                Interlocked.Add(ref readBytes, n);
```

`windows/src/Offload.Core/Models.cs`, řádky 8–13 — najít:

```csharp
    public int BufferCount { get; set; } = 16;
    public int ReadRetries { get; set; } = 3;
    public bool Verify { get; set; } = true;
    /// Files at least this large are flushed individually; the rest are flushed once at the end.
    public long FullSyncThreshold { get; set; } = 256L << 20;
    public bool AllowSameDevice { get; set; }
```

nahradit:

```csharp
    public int BufferCount { get; set; } = 16;
    public int ReadRetries { get; set; } = 3;
    public bool Verify { get; set; } = true;
    /// The checksums of a full copy (spec 3.5): xxh64, or xxh64 + MD5 (R16 — never MD5 alone). An append uses the
    /// earlier backup's instead (OffloadPlan.Hashes).
    public HashKind Hashes { get; set; } = HashKind.Xxh64;
    /// Files at least this large are flushed individually; the rest are flushed once at the end.
    public long FullSyncThreshold { get; set; } = 256L << 20;
    public bool AllowSameDevice { get; set; }
```

`windows/src/Offload.Core/Models.cs`, řádky 59–64 — najít:

```csharp
    public SourceScan? FullScan { get; init; }
    /// Append: files of the earlier backup that were never read back.
    public int UnverifiedEarlier { get; init; }
    /// Spec 2.5: what each destination volume needs and has free (the CLI's --dry-run lists it).
    public IReadOnlyList<Space.SpaceLine> Space { get; init; } = [];
    /// "Na cíli … zbude po offloadu jen … — rezerva pod 5 %…" — never blocks (R11).
```

nahradit:

```csharp
    public SourceScan? FullScan { get; init; }
    /// Append: files of the earlier backup that were never read back.
    public int UnverifiedEarlier { get; init; }
    /// What this copy hashes and verifies with (xxh64 always): OffloadOptions.Hashes for a full copy, the earlier
    /// backup's for an append (spec 3.5).
    public HashKind Hashes { get; init; } = HashKind.Xxh64;
    /// Spec 2.5: what each destination volume needs and has free (the CLI's --dry-run lists it).
    public IReadOnlyList<Space.SpaceLine> Space { get; init; } = [];
    /// "Na cíli … zbude po offloadu jen … — rezerva pod 5 %…" — never blocks (R11).
```

`windows/src/Offload.Core/OffloadEngine.cs`, řádky 42–48 — najít:

```csharp

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots, Space = space, SpaceWarnings = warnings };
    }

    /// An append (spec 2.2): the same checks as Prepare (options, source, destinations, Windows names, FAT32, space),
```

nahradit:

```csharp

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan
        {
            Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots, Space = space, SpaceWarnings = warnings,
            Hashes = HashKinds.Normalize(Options.Hashes),
        };
    }

    /// An append (spec 2.2): the same checks as Prepare (options, source, destinations, Windows names, FAT32, space),
```

`windows/src/Offload.Core/OffloadEngine.cs`, řádky 78–83 — najít:

```csharp
            Source = a.Source, Scan = scan, Date = a.Date, CardName = a.CardName, DestRoots = a.DestRoots,
            Mode = PlanMode.Append, Generation = a.Generation, FullScan = a.FullScan, UnverifiedEarlier = a.UnverifiedEarlier,
            Space = space, SpaceWarnings = warnings,
        };
    }

```

nahradit:

```csharp
            Source = a.Source, Scan = scan, Date = a.Date, CardName = a.CardName, DestRoots = a.DestRoots,
            Mode = PlanMode.Append, Generation = a.Generation, FullScan = a.FullScan, UnverifiedEarlier = a.UnverifiedEarlier,
            Space = space, SpaceWarnings = warnings,
            Hashes = HashKinds.FromNames(a.HashAlgorithms),   // the earlier backup's, not the current setting (spec 3.5)
        };
    }

```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 69–75 — najít:

```csharp
    /// Every file is on every destination and was read back with a matching hash.
    [JsonIgnore] public bool SafeToEject => Success && Verified;

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<ulong?> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
        DateTime started, DateTime copyFinished, DateTime finished)
    {
```

nahradit:

```csharp
    /// Every file is on every destination and was read back with a matching hash.
    [JsonIgnore] public bool SafeToEject => Success && Verified;

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<FileHashes> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
        DateTime started, DateTime copyFinished, DateTime finished)
    {
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 79–85 — najít:

```csharp
            Path = f.RelPath,
            Size = f.Size,
            Modified = f.ModifiedUtc,
            Xxh64 = hashes[i] is ulong h ? Format.Hex64(h) : null,
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
```

nahradit:

```csharp
            Path = f.RelPath,
            Size = f.Size,
            Modified = f.ModifiedUtc,
            Xxh64 = hashes[i].Xxh64Hex,
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 14–19 — najít:

```csharp
    public string? Date { get; set; }
    public bool Verify { get; set; } = true;
    public int ChunkMB { get; set; } = 16;
    public int Buffers { get; set; } = 16;
    public bool AllowSameDevice { get; set; }
    /// Probe clips and put thumbnails + metadata into the report (stage 2).
```

nahradit:

```csharp
    public string? Date { get; set; }
    public bool Verify { get; set; } = true;
    public int ChunkMB { get; set; } = 16;
    /// xxh64, or xxh64 + MD5 (spec 3.5; settings `hash`, CLI --hash). An append keeps the earlier backup's.
    public HashKind Hashes { get; set; } = HashKind.Xxh64;
    public int Buffers { get; set; } = 16;
    public bool AllowSameDevice { get; set; }
    /// Probe clips and put thumbnails + metadata into the report (stage 2).
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 107–112 — najít:

```csharp
            BufferCount = config.Buffers,
            Verify = config.Verify,
            AllowSameDevice = config.AllowSameDevice,
        })
    {
    }
```

nahradit:

```csharp
            BufferCount = config.Buffers,
            Verify = config.Verify,
            AllowSameDevice = config.AllowSameDevice,
            Hashes = config.Hashes,
        })
    {
    }
```

- [ ] **Step 4: Spustit — projdou**

Run: stejný filtr jako v kroku 2.
Expected: PASS (`Both_TwoBlockPool_EveryFileVerified` skončí do 60 s; špatný počet konzumentů by kopii zasekl, nebo vrátil blok dřív a ověření by selhalo). Pak `dotnet build windows\Offload.sln -warnaserror` a celá sada.

- [ ] **Step 5: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 20–25 — najít:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 3 — proxy pro přelinkování: s platným TC navíc `reel_name` (muxer MOV ho zapíše do stopy timecodu `tmcd`, ffprobe ho tam ukáže; ověřeno s ffmpeg 8.1), všechny zvukové proudy zdroje (`-map 0:a?`; H.264/HEVC AAC 64 kb/s na kanál každého proudu, 128–512 kb/s; ProRes PCM), bez TC ani `tmcd`, ani cívka; proxy přejmenovaná kvůli shodě jmen má poznámku „Proxy přejmenována na … (shoda jmen) — přelinkujte ji ručně.“; odhad místa počítá 0,2 Mb/s na každou zvukovou stopu
- Úkol 2 — cívka klipu (`ReelResolver`): tag kamery z ffprobe (`reel_name`, `tape_name`, `reel` — u MOV na stopě timecodu, u MXF na každém proudu), jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty; v reportu `clips[].info.reel`
- Úkol 1 — `CardRecord` úplný: klipy všech generací (doplnění za dřívějšími, tatáž cesta = nejnovější záznam), součty přes generace (soubory, data, začátek–konec), selhané soubory podle nejnovějšího záznamu, „ověřeno“ jen když jsou ověřené všechny generace a žádný report doplnění není nečitelný; generace bez analýzy klipů dostane klipy nalezené v jejích souborech (bez metadat); snese report z Macu (bez `generation`, `lut`, `proxyEncoder`, `md5`) i `"clips": null`
````

nahradit:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 4 — jádro MD5 (R16: jen navíc k xxh64): `HashKind` (`Xxh64`, `Md5`, xxh64 vždy), `FileHashes` místo `ulong?[]`, jeden `SourceHasher` na algoritmus na vlastním vlákně (blok karty má o konzumenta víc, MD5 nebrzdí xxh64 ani čtení karty), zpětné čtení cíle hashuje všemi zvolenými v jednom průchodu („MD5 MISMATCH“ u nesouhlasu); celá kopie podle `OffloadOptions.Hashes`, doplnění podle dřívější zálohy (`OffloadPlan.Hashes`)
- Úkol 3 — proxy pro přelinkování: s platným TC navíc `reel_name` (muxer MOV ho zapíše do stopy timecodu `tmcd`, ffprobe ho tam ukáže; ověřeno s ffmpeg 8.1), všechny zvukové proudy zdroje (`-map 0:a?`; H.264/HEVC AAC 64 kb/s na kanál každého proudu, 128–512 kb/s; ProRes PCM), bez TC ani `tmcd`, ani cívka; proxy přejmenovaná kvůli shodě jmen má poznámku „Proxy přejmenována na … (shoda jmen) — přelinkujte ji ručně.“; odhad místa počítá 0,2 Mb/s na každou zvukovou stopu
- Úkol 2 — cívka klipu (`ReelResolver`): tag kamery z ffprobe (`reel_name`, `tape_name`, `reel` — u MOV na stopě timecodu, u MXF na každém proudu), jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty; v reportu `clips[].info.reel`
- Úkol 1 — `CardRecord` úplný: klipy všech generací (doplnění za dřívějšími, tatáž cesta = nejnovější záznam), součty přes generace (soubory, data, začátek–konec), selhané soubory podle nejnovějšího záznamu, „ověřeno“ jen když jsou ověřené všechny generace a žádný report doplnění není nečitelný; generace bez analýzy klipů dostane klipy nalezené v jejích souborech (bez metadat); snese report z Macu (bez `generation`, `lut`, `proxyEncoder`, `md5`) i `"clips": null`
````

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Windows: MD5 beside xxh64 in the copy engine (one hasher thread per algorithm)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.Core/DestWriter.cs windows/src/Offload.Core/Hashing.cs windows/src/Offload.Core/Models.cs windows/src/Offload.Core/OffloadEngine.Run.cs windows/src/Offload.Core/OffloadEngine.cs windows/src/Offload.Core/OffloadJob.cs windows/src/Offload.Core/OffloadReport.cs windows/src/Offload.Core/SourceHasher.cs windows/tests/Offload.Core.Tests/DestWriterTests.cs windows/tests/Offload.Core.Tests/EngineTests.cs windows/tests/Offload.Core.Tests/HashingTests.cs windows/tests/Offload.Core.Tests/ReportTests.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

---

### Task 5: Hash výstupy — report, `.md5`, MHL, HTML/TXT, nastavení, GUI, CLI

Spec §3.5 (UX, Data na disku, Výkon, okrajové případy), §5.1 `hash`, §6.2 úkol 5. Report: `hashAlgorithm` vždy `xxh64`, nový `hashAlgorithms` jen když není `["xxh64"]`, u souboru `md5`. `_REPORT\<karta>.md5` ve formátu `md5sum` (doplnění ho prodlouží jako `.xxh64`). MHL: `<md5>` vedle `<xxh64>` se stejným `action` a `hashdate`. TXT `Hash:        xxh64+md5`, HTML dlaždice „XXH64 + MD5“ a sloupec na algoritmus. Nastavení `hash` (`xxh64` | `both`), GUI „Kontrolní součet“, popisek ověření se jménem součtu a upozornění na pomalejší MD5, CLI `copy --hash xxh64|both`, `bench --hash md5`. Doplnění a historie podle algoritmů dřívější zálohy; JSON golden file pro `both`.

**Files:**
- Modify: `docs/VYVOJ-WINDOWS.md` (řádky 20–25)
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs` (řádky 205–210)
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs` (řádky 404–409)
- Modify: `windows/src/Offload.App/SetupPanel.xaml` (řádky 184–190)
- Modify: `windows/src/Offload.Cli/BenchCommand.cs` (řádky 10–26, 29–35)
- Modify: `windows/src/Offload.Cli/CopyCommand.cs` (řádky 22–32, 42–47)
- Modify: `windows/src/Offload.Core/Append/AppendPlanner.cs` (řádky 163–169)
- Modify: `windows/src/Offload.Core/Bench.cs` (řádky 1–5, 50–59, 76–83, 85–100)
- Modify: `windows/src/Offload.Core/Desktop/AppSettings.cs` (řádky 23–28, 53–59, 96–101)
- Modify: `windows/src/Offload.Core/Desktop/Texts.cs` (řádky 20–23)
- Modify: `windows/src/Offload.Core/Hashing.cs` (řádky 24–29)
- Modify: `windows/src/Offload.Core/HtmlReport.cs` (řádky 111–117, 146–157)
- Modify: `windows/src/Offload.Core/Messages.cs` (řádky 151–156)
- Modify: `windows/src/Offload.Core/Mhl.cs` (řádky 75–80)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 302–308)
- Modify: `windows/src/Offload.Core/OffloadReport.cs` (řádky 24–29, 33–39, 54–59, 80–85, 110–115, 121–127, 138–144, 214–246, 250–256)
- Modify: `windows/tests/Offload.Core.Tests/AppendEngineTests.cs` (řádky 587–592)
- Modify: `windows/tests/Offload.Core.Tests/BenchTests.cs` (řádky 12–17)
- Create: `windows/tests/Offload.Core.Tests/CliOutputsTests.cs`
- Modify: `windows/tests/Offload.Core.Tests/EngineTests.cs` (řádky 116–121)
- Modify: `windows/tests/Offload.Core.Tests/HashingTests.cs` (řádky 57–62)
- Modify: `windows/tests/Offload.Core.Tests/ReportTests.cs` (řádky 40–45)
- Modify: `windows/tests/Offload.Core.Tests/SettingsStoreTests.cs` (řádky 25–30)

**Interfaces:**
- Consumes: `HashKind`, `HashKinds`, `FileHashes`, `OffloadPlan.Hashes`, `JobConfig.Hashes` (úkol 4); `MhlReader` (čte `md5` už od plánu 6); `CopyCommand.JobFactory` (plán 6, seam testů CLI); `AppendFixtures.Options()`.
- Produces: `ReportFile.Md5` (`md5`); `OffloadReport.HashAlgorithms` (`IReadOnlyList<string>?`, `hashAlgorithms`), `OffloadReport.Algorithms` (`[JsonIgnore]`, `hashAlgorithms` nebo `[hashAlgorithm]`), `OffloadReport.HashListOf(string algorithm) → string`; `HashKinds.Wire(HashKind) → "xxh64"|"both"`, `HashKinds.TryParseWire(string?, out HashKind) → bool`, `HashKinds.FromSetting(string?) → HashKind`, `HashKinds.Label(HashKind)`; `AppSettings.Hash` (`string`, `hash`, výchozí `"xxh64"`); `Texts.VerifyLabel(HashKind)`, `Texts.Md5Slower`; `sealed record HashChoice(HashKind Kind, string Label)` s `All`, `Of(HashKind)`; `Msg.UnknownHash(string?)`; `Bench.Hash(int size, string algorithm = "xxh64")`, `Bench.Write(…, CancellationToken ct = default, HashKind kinds = HashKind.Xxh64)`; GUI `MainViewModel.HashChoices`, `Hash`, `VerifyLabel`, `Md5Note`; nový testovací soubor `CliOutputsTests` (kolekce „Console“) s pomocníky `Run(Command, params string[])` a `Card(TempDir)` — úkoly 6 a 8 do něj přidají testy.

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/ReportTests.cs`, řádky 40–45 — najít:

```csharp
        Assert.Equal("boom", root.GetProperty("files")[1].GetProperty("errors")[0].GetString());
    }

    [Fact]
    public void Json_UnbufferedOmittedWhenNormal()
    {
```

nahradit:

```csharp
        Assert.Equal("boom", root.GetProperty("files")[1].GetProperty("errors")[0].GetString());
    }

    // Spec 3.5: the JSON of a copy with xxh64 + MD5, byte for byte (golden) — "hashAlgorithm" stays "xxh64" for the
    // Mac, "hashAlgorithms" and "md5" are added keys.
    [Fact]
    public void Json_Both_Golden()
    {
        var t0 = new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
        var r = new OffloadReport
        {
            Version = "test", CardName = "A001", Date = "2026-10-07", Source = @"E:\", SourceFilesystem = "exFAT",
            Started = t0, CopyFinished = t0.AddSeconds(10), Finished = t0.AddSeconds(15), Verified = true, TotalFiles = 1, TotalBytes = 3,
            HashAlgorithms = ["xxh64", "md5"],
            Destinations = [new ReportDestination { Root = @"D:\Zaloha", CardPath = @"D:\Zaloha\2026-10-07\A001", Filesystem = "NTFS", FilesOK = 1 }],
            Files = [new ReportFile { Path = "CLIP/abc.mov", Size = 3, Modified = t0, Xxh64 = "44bc2cf5ad770999", Md5 = "900150983cd24fb0d6963f7d28e17f72", Destinations = ["verified"], Errors = [null] }],
            Skipped = [],
        };
        const string golden = """
            {
              "cardName": "A001",
              "clips": [],
              "copyFinished": "2026-10-07T08:00:10Z",
              "date": "2026-10-07",
              "destinations": [
                {
                  "cardPath": "D:\\Zaloha\\2026-10-07\\A001",
                  "filesFailed": 0,
                  "filesOK": 1,
                  "filesystem": "NTFS",
                  "root": "D:\\Zaloha"
                }
              ],
              "files": [
                {
                  "destinations": [
                    "verified"
                  ],
                  "errors": [
                    null
                  ],
                  "md5": "900150983cd24fb0d6963f7d28e17f72",
                  "modified": "2026-10-07T08:00:00Z",
                  "path": "CLIP/abc.mov",
                  "size": 3,
                  "xxh64": "44bc2cf5ad770999"
                }
              ],
              "finished": "2026-10-07T08:00:15Z",
              "hashAlgorithm": "xxh64",
              "hashAlgorithms": [
                "xxh64",
                "md5"
              ],
              "skipped": [],
              "source": "E:\\",
              "sourceFilesystem": "exFAT",
              "started": "2026-10-07T08:00:00Z",
              "tool": "offload",
              "totalBytes": 3,
              "totalFiles": 1,
              "verified": true,
              "version": "test"
            }
            """;
        Assert.Equal(golden.Replace("\r\n", "\n"), r.ToJson());
        Assert.Equal(["xxh64", "md5"], OffloadReport.FromJson(r.ToJson()).Algorithms);
        Assert.Equal(["xxh64"], Reports.Sample(Card).Algorithms);   // a report without the key (the Mac, xxh64 only)
    }

    [Fact]
    public void Json_UnbufferedOmittedWhenNormal()
    {
```

`windows/tests/Offload.Core.Tests/EngineTests.cs`, řádky 116–121 — najít:

```csharp
        Assert.False(report.Success);
    }

    [Fact]
    public void DefaultBlockSize_FileOneByteOverBlock()
    {
```

nahradit:

```csharp
        Assert.False(report.Success);
    }

    // Spec 3.5: xxh64 + MD5 — "md5" per file, `<card>.md5` (md5sum format), MHL <md5> beside <xxh64>, TXT and HTML say
    // so, and the verification of the backup reads both; xxh64 alone leaves no trace of MD5.
    [Fact]
    public void Both_ReportHashListMhlAndBackupVerification()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var files = MakeCard(card);
        var options = Options();
        options.Hashes = HashKind.Both;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");

        var report = engine.Run(plan);

        Assert.Equal(["xxh64", "md5"], report.HashAlgorithms);
        foreach (var f in report.Files)
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(files[f.Path])), f.Md5);
        var rep = Path.Combine(plan.CardPaths[0], "_REPORT");
        var md5List = File.ReadAllText(Path.Combine(rep, "A001.md5"));
        Assert.Equal(report.HashListOf("md5"), md5List);
        Assert.Contains($"{report.Files[0].Md5}  {report.Files[0].Path}\n", md5List);
        Assert.DoesNotContain("\r", md5List);
        Assert.NotEqual(0xEF, File.ReadAllBytes(Path.Combine(rep, "A001.md5"))[0]);   // no BOM
        Assert.Contains("Hash:        xxh64+md5", File.ReadAllText(Path.Combine(rep, "offload_report.txt")));
        Assert.Contains("XXH64 + MD5", File.ReadAllText(Path.Combine(rep, "A001_report.html")));
        var mhl = Verification.MhlReader.Read(plan.CardPaths[0])!;
        Assert.All(mhl.Files.Values, r => Assert.Equal(["xxh64", "md5"], r.Hashes.Select(h => h.Algorithm)));
        var verify = Verification.BackupVerifier.Verify(plan.CardPaths[0], chunkSize: Chunk);
        Assert.True(verify.Success);
        Assert.Equal(["xxh64", "md5"], verify.HashAlgorithms);

        // xxh64 alone (the default): no md5 key, no .md5 list, no <md5> in the MHL.
        var plain = new OffloadEngine(Options());
        var plainPlan = plain.Prepare(card, [dir.Sub("d2")], null, "2026-10-04");
        var plainReport = plain.Run(plainPlan);
        Assert.Null(plainReport.HashAlgorithms);
        Assert.All(plainReport.Files, f => Assert.Null(f.Md5));
        Assert.False(File.Exists(Path.Combine(plainPlan.CardPaths[0], "_REPORT", "A001.md5")));
        Assert.DoesNotContain("md5", plainReport.ToJson());
    }

    [Fact]
    public void DefaultBlockSize_FileOneByteOverBlock()
    {
```

`windows/tests/Offload.Core.Tests/AppendEngineTests.cs`, řádky 587–592 — najít:

```csharp
        }
    }

    // Spec 2.2: an existing proxy is never overwritten; a new clip of the same name gets "_2".
    [FfmpegFact]
    public void Proxies_ExistingNeverOverwritten()
```

nahradit:

```csharp
        }
    }

    // Spec 3.5: an append hashes with the earlier backup's algorithms, not the current setting — MD5 lines follow the
    // earlier ones in `<card>.md5`, generation 2 of the MHL has <md5> too.
    [Fact]
    public void Append_KeepsTheBackupsHashes()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var history = dir.Sub("appdata");
        var options = AppendFixtures.Options();
        options.Hashes = HashKind.Both;
        var full = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-06", AnalyzeClips = false, HistoryFolder = history }, options);
        full.Run();
        var paths = full.Plan!.CardPaths;
        var before = File.ReadAllText(Path.Combine(paths[0], "_REPORT", "A001.md5"));
        AppendFixtures.NewClip(card);

        var append = Job(card, [], history, appendInto: paths);   // the setting is xxh64 only
        var report = append.Run()!;

        Assert.Equal(HashKind.Both, append.Plan!.Hashes);
        Assert.Equal(["xxh64", "md5"], report.HashAlgorithms);
        var list = File.ReadAllText(Path.Combine(paths[0], "_REPORT", "A001.md5"));
        Assert.StartsWith(before, list);
        Assert.EndsWith($"{report.Files.Single().Md5}  CLIP/A001C003.mov\n", list);
        var gen2 = Verification.MhlReader.Read(paths[0])!.Generations[1];
        Assert.Equal(["xxh64", "md5"], gen2.Files.Single().Hashes.Select(h => h.Algorithm));
        Assert.Equal(["xxh64", "md5"], new HistoryStore(history).Load().Single().HashAlgorithms);
    }

    // Spec 2.2: an existing proxy is never overwritten; a new clip of the same name gets "_2".
    [FfmpegFact]
    public void Proxies_ExistingNeverOverwritten()
```

`windows/tests/Offload.Core.Tests/SettingsStoreTests.cs`, řádky 25–30 — najít:

```csharp
        Assert.Empty(s.PortNames);
    }

    [Fact]
    public void Corrupt_Defaults()
    {
```

nahradit:

```csharp
        Assert.Empty(s.PortNames);
    }

    // Spec 5.1: "hash" — "xxh64" by default, "both" round-trips; a value a newer version wrote is kept but read as xxh64.
    [Fact]
    public void Hash_DefaultBothAndUnknownValue()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        Assert.Equal("xxh64", SettingsStore.Load(path).Hash);
        var s = LoadJson(dir, """{"hash":"both"}""");
        Assert.Equal(HashKind.Both, HashKinds.FromSetting(s.Hash));
        Assert.Null(s.Extra);
        SettingsStore.Save(s, path);
        Assert.Contains("\"hash\": \"both\"", File.ReadAllText(path));
        var future = LoadJson(dir, """{"hash":"xxh128"}""");
        Assert.Equal(HashKind.Xxh64, HashKinds.FromSetting(future.Hash));
        SettingsStore.Save(future, path);
        Assert.Contains("\"hash\": \"xxh128\"", File.ReadAllText(path));
    }

    [Fact]
    public void Corrupt_Defaults()
    {
```

`windows/tests/Offload.Core.Tests/BenchTests.cs`, řádky 12–17 — najít:

```csharp
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Read_ReadsUpToLimitPerBlockSize()
    {
```

nahradit:

```csharp
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    // Spec 3.5: `bench --hash md5` — MD5 in RAM, and the read-back through both hashes as a copy verifies.
    [Fact]
    public void Md5_HashLine_AndReadBackThroughBoth()
    {
        Assert.Equal("md5 in RAM", Bench.Hash(1 << 20, "md5").Label);
        Assert.Equal("xxh64 in RAM", Bench.Hash(1 << 20).Label);
        using var dir = new TempDir();
        var lines = Bench.Write(dir.Path, 1 << 20, 64 * 1024, kinds: HashKind.Both);
        Assert.Equal(["write 64 KB", "read  64 KB + xxh64 + md5"], lines.Select(l => l.Label));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Read_ReadsUpToLimitPerBlockSize()
    {
```

`windows/tests/Offload.Core.Tests/HashingTests.cs`, řádky 57–62 — najít:

```csharp
        Assert.Equal(XxHash64.HashToUInt64(data), hb.Xxh64);
    }

    [Fact]
    public void HashKinds_Xxh64Always()
    {
```

nahradit:

```csharp
        Assert.Equal(XxHash64.HashToUInt64(data), hb.Xxh64);
    }

    // Spec 3.5: the choice in the GUI and its texts; settings/CLI values "xxh64" and "both" only.
    [Fact]
    public void Choices_Labels_Wire()
    {
        Assert.Equal(["xxHash64 (rychlý)", "xxHash64 + MD5"], Desktop.HashChoice.All.Select(c => c.Label));
        Assert.Equal(HashKind.Both, Desktop.HashChoice.Of(HashKind.Md5).Kind);
        Assert.Equal("Ověřit kopie zpětným čtením (xxHash64 + MD5)", Desktop.Texts.VerifyLabel(HashKind.Both));
        Assert.Equal("Ověřit kopie zpětným čtením (xxHash64)", Desktop.Texts.VerifyLabel(HashKind.Xxh64));
        Assert.Equal(("xxh64", "both"), (HashKinds.Wire(HashKind.Xxh64), HashKinds.Wire(HashKind.Md5)));
        Assert.True(HashKinds.TryParseWire("both", out var both) && both == HashKind.Both);
        Assert.False(HashKinds.TryParseWire("md5", out _));   // R16: never MD5 alone
        Assert.Equal(HashKind.Xxh64, HashKinds.FromSetting(null));
    }

    [Fact]
    public void HashKinds_Xxh64Always()
    {
```

Nový soubor `windows/tests/Offload.Core.Tests/CliOutputsTests.cs`:

```csharp
using Offload.Cli;

namespace Offload.Core.Tests;

/// Plan 7: the CLI options of the outputs — `copy --hash`, `bench --hash`.
[Collection("Console")]
public class CliOutputsTests
{
    static (int Exit, string Out, string Err) Run(System.CommandLine.Command command, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var old = CopyCommand.JobFactory;
        Console.SetOut(so);
        Console.SetError(se);
        CopyCommand.JobFactory = c =>
        {
            var options = AppendFixtures.Options();   // card and destinations share a volume in a test
            options.Hashes = c.Hashes;
            return new OffloadJob(c, options);
        };
        try { return (command.Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = old;
        }
    }

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        return card;
    }

    // Spec 3.5: `--hash both` = xxh64 + MD5 (the .md5 list is there); anything else is a Czech error, exit 1.
    [Fact]
    public void CopyHash_BothWritesMd5_UnknownRefused()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var d1 = dir.Sub("d1");

        var ok = Run(CopyCommand.Create(), card, "-d", d1, "--date", "2026-10-07", "--no-history", "--no-analyze", "--hash", "both");

        Assert.Equal(0, ok.Exit);
        Assert.True(File.Exists(Path.Combine(d1, "2026-10-07", "A001", "_REPORT", "A001.md5")));
        Assert.Contains("Hash:        xxh64+md5", ok.Out);

        var bad = Run(CopyCommand.Create(), card, "-d", dir.Sub("d2"), "--no-history", "--hash", "md5");
        Assert.Equal(1, bad.Exit);
        Assert.Contains("Chyba: Neznámý kontrolní součet „md5“ — použijte xxh64 nebo both (xxh64 + MD5).", bad.Err);
    }

    // Spec 3.5: `bench --hash md5` adds the MD5 line; an unknown value is refused in Czech.
    [Fact]
    public void BenchHash_Md5Line_UnknownRefused()
    {
        var md5 = Run(BenchCommand.Create(), "--hash", "md5");
        Assert.Equal(0, md5.Exit);
        Assert.Contains("md5 in RAM", md5.Out);
        Assert.Contains("xxh64 in RAM", md5.Out);
        var bad = Run(BenchCommand.Create(), "--hash", "sha1");
        Assert.Equal(1, bad.Exit);
        Assert.Contains("Neznámý kontrolní součet „sha1“", bad.Err);
    }
}
```

- [ ] **Step 2: Spustit — selžou**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ReportTests|FullyQualifiedName~EngineTests|FullyQualifiedName~Append_KeepsTheBackupsHashes|FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~BenchTests|FullyQualifiedName~HashingTests|FullyQualifiedName~CliOutputsTests"`
Expected: build FAIL — `CS0117: 'OffloadReport' neobsahuje definici pro 'HashAlgorithms'`, `'ReportFile' … 'Md5'`, `'HashChoice' se nenašel`, `'AppSettings' … 'Hash'`.

- [ ] **Step 3: Implementace — jádro**

`windows/src/Offload.Core/OffloadReport.cs`, řádky 24–29 — najít:

```csharp
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("modified")] public DateTime Modified { get; init; }
    [JsonPropertyName("xxh64")] public string? Xxh64 { get; init; }
    [JsonPropertyName("sourceError")] public string? SourceError { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<string> Destinations { get; init; }
    [JsonPropertyName("errors")] public required IReadOnlyList<string?> Errors { get; init; }
```

nahradit:

```csharp
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("modified")] public DateTime Modified { get; init; }
    [JsonPropertyName("xxh64")] public string? Xxh64 { get; init; }
    /// Only when the copy computed MD5 too (spec 3.5): 32 lower-case hex digits.
    [JsonPropertyName("md5")] public string? Md5 { get; init; }
    [JsonPropertyName("sourceError")] public string? SourceError { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<string> Destinations { get; init; }
    [JsonPropertyName("errors")] public required IReadOnlyList<string?> Errors { get; init; }
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 33–39 — najít:

```csharp
{
    [JsonPropertyName("tool")] public string Tool { get; init; } = "offload";
    [JsonPropertyName("version")] public string Version { get; init; } = OffloadVersion.String;
    [JsonPropertyName("hashAlgorithm")] public string HashAlgorithm { get; init; } = "xxh64";
    [JsonPropertyName("cardName")] public required string CardName { get; init; }
    [JsonPropertyName("date")] public required string Date { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
```

nahradit:

```csharp
{
    [JsonPropertyName("tool")] public string Tool { get; init; } = "offload";
    [JsonPropertyName("version")] public string Version { get; init; } = OffloadVersion.String;
    /// Always "xxh64" (the Mac only shows it); `hashAlgorithms` says when there is more.
    [JsonPropertyName("hashAlgorithm")] public string HashAlgorithm { get; init; } = "xxh64";
    /// Only when it is not just xxh64 (spec 3.5): ["xxh64", "md5"].
    [JsonPropertyName("hashAlgorithms")] public IReadOnlyList<string>? HashAlgorithms { get; init; }
    [JsonPropertyName("cardName")] public required string CardName { get; init; }
    [JsonPropertyName("date")] public required string Date { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 54–59 — najít:

```csharp
    /// The ASC MHL generation this report belongs to: absent (= 1) for the full copy, 2, 3 … for appends (spec 2.2).
    [JsonPropertyName("generation")] public int? Generation { get; set; }

    [JsonIgnore] public double CopySeconds => (CopyFinished - Started).TotalSeconds;
    [JsonIgnore] public double VerifySeconds => (Finished - CopyFinished).TotalSeconds;

```

nahradit:

```csharp
    /// The ASC MHL generation this report belongs to: absent (= 1) for the full copy, 2, 3 … for appends (spec 2.2).
    [JsonPropertyName("generation")] public int? Generation { get; set; }

    /// The hashes of this copy: `hashAlgorithms`, else the one `hashAlgorithm` (reports without the key, the Mac).
    [JsonIgnore] public IReadOnlyList<string> Algorithms => HashAlgorithms is { Count: > 0 } all ? all : [HashAlgorithm];

    [JsonIgnore] public double CopySeconds => (CopyFinished - Started).TotalSeconds;
    [JsonIgnore] public double VerifySeconds => (Finished - CopyFinished).TotalSeconds;

```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 80–85 — najít:

```csharp
            Size = f.Size,
            Modified = f.ModifiedUtc,
            Xxh64 = hashes[i].Xxh64Hex,
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
```

nahradit:

```csharp
            Size = f.Size,
            Modified = f.ModifiedUtc,
            Xxh64 = hashes[i].Xxh64Hex,
            Md5 = hashes[i].Md5Hex,
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 110–115 — najít:

```csharp
            Files = files,
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
        };
    }

```

nahradit:

```csharp
            Files = files,
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
        };
    }

```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 121–127 — najít:

```csharp

    /// Plain hash list, one "<xxh64>  <path>" per line — readable by humans and by `offload verify`.
    [JsonIgnore]
    public string HashList => string.Join("\n", Files.Where(f => f.Xxh64 != null).Select(f => $"{f.Xxh64}  {f.Path}")) + "\n";

    [JsonIgnore]
    public string Summary
```

nahradit:

```csharp

    /// Plain hash list, one "<xxh64>  <path>" per line — readable by humans and by `offload verify`.
    [JsonIgnore]
    public string HashList => HashListOf("xxh64");

    /// "<hash>  <path>" per file that has hash `algorithm` ("xxh64" or "md5", the md5sum format), "\n" after each.
    public string HashListOf(string algorithm) =>
        string.Concat(Files.Select(f => (Hash: algorithm == "md5" ? f.Md5 : f.Xxh64, f.Path)).Where(x => x.Hash != null).Select(x => $"{x.Hash}  {x.Path}\n"));

    /// `_REPORT\<card>.xxh64` or `.md5`.
    static string HashListPath(string cardPath, string cardName, string algorithm) =>
        System.IO.Path.Combine(cardPath, Layout.ReportDirName, $"{cardName}.{algorithm}");

    [JsonIgnore]
    public string Summary
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 138–144 — najít:

```csharp
            s.Append($"Files:       {TotalFiles}  ({Format.Bytes(TotalBytes)})\n");
            s.Append($"Copy:        {CopySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, CopySeconds)}\n");
            s.Append("Verify:      " + (Verified ? $"{VerifySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, VerifySeconds)}" : "NOT VERIFIED") + "\n");
            s.Append($"Hash:        {HashAlgorithm}\n");
            foreach (var d in Destinations)
                s.Append($"Destination: {d.CardPath} [{d.Filesystem}]  ok {d.FilesOK}  failed {d.FilesFailed}{(d.Unbuffered == false ? "  (buffered I/O)" : "")}\n");
            if (Skipped.Count > 0) s.Append($"\nSkipped (system files): {string.Join(", ", Skipped)}\n");
```

nahradit:

```csharp
            s.Append($"Files:       {TotalFiles}  ({Format.Bytes(TotalBytes)})\n");
            s.Append($"Copy:        {CopySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, CopySeconds)}\n");
            s.Append("Verify:      " + (Verified ? $"{VerifySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, VerifySeconds)}" : "NOT VERIFIED") + "\n");
            s.Append($"Hash:        {string.Join("+", Algorithms)}\n");
            foreach (var d in Destinations)
                s.Append($"Destination: {d.CardPath} [{d.Filesystem}]  ok {d.FilesOK}  failed {d.FilesFailed}{(d.Unbuffered == false ? "  (buffered I/O)" : "")}\n");
            if (Skipped.Count > 0) s.Append($"\nSkipped (system files): {string.Join(", ", Skipped)}\n");
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 214–246 — najít:

```csharp
        }
        if (!includeHashRecords) return;
        Mhl.Write(this, cardPath);
        // An append: the earlier lines byte for byte, then the missed ones, then this generation's.
        var hashList = System.IO.Path.Combine(dir, $"{CardName}.xxh64");
        var lines = Encoding.UTF8.GetBytes(HashList);
        if (generation == 1)
        {
            NativeIO.WriteFileReplacing(hashList, lines);
            return;
        }
        var (earlier, missed) = HashListAndMissed(hashList, cardPath, Files.Select(f => f.Path));
        NativeIO.WriteFileReplacing(hashList, [.. earlier, .. missed, .. lines]);
    }

    /// `<card>/_REPORT/<card name>.xxh64` gets the lines of every path the ASC MHL history records but the list lacks
    /// (a hash list whose write failed, step 3 of Write) — the one record file that may be rewritten. Used by an append
    /// with nothing new (fix round 2, item 6b); an append with new files does the same in Write. False: nothing missed.
    public static bool HealHashList(string cardPath, string cardName)
    {
        var hashList = System.IO.Path.Combine(cardPath, Layout.ReportDirName, $"{cardName}.xxh64");
        var (earlier, missed) = HashListAndMissed(hashList, cardPath, []);
        if (missed.Length == 0) return false;
        NativeIO.WriteFileReplacing(hashList, [.. earlier, .. missed]);
        return true;
    }

    /// The card's hash list as the earlier generations left it (exact bytes, then a line break if it had none), and
    /// the xxh64 lines of every path the ASC MHL history records but neither the list nor `alsoListed` has.
    static (byte[] Earlier, byte[] Missed) HashListAndMissed(string path, string cardPath, IEnumerable<string> alsoListed)
    {
        var earlier = File.Exists(path) ? File.ReadAllBytes(path) : [];
        if (earlier.Length > 0 && earlier[^1] != (byte)'\n') earlier = [.. earlier, (byte)'\n'];
```

nahradit:

```csharp
        }
        if (!includeHashRecords) return;
        Mhl.Write(this, cardPath);
        // `<card>.xxh64` always, `<card>.md5` with MD5 (spec 3.5). An append: the earlier lines byte for byte, then the
        // missed ones, then this generation's.
        foreach (var algorithm in Algorithms.Where(a => a is "xxh64" or "md5"))
        {
            var hashList = HashListPath(cardPath, CardName, algorithm);
            var lines = Encoding.UTF8.GetBytes(HashListOf(algorithm));
            if (generation == 1)
            {
                NativeIO.WriteFileReplacing(hashList, lines);
                continue;
            }
            var (earlier, missed) = HashListAndMissed(hashList, cardPath, Files.Select(f => f.Path), algorithm);
            NativeIO.WriteFileReplacing(hashList, [.. earlier, .. missed, .. lines]);
        }
    }

    /// `<card>/_REPORT/<card name>.xxh64` (and `.md5` when the ASC MHL history has MD5) gets the lines of every path the
    /// history records but the list lacks (a hash list whose write failed, step 3 of Write) — the record files that may
    /// be rewritten. Used by an append with nothing new (fix round 2, item 6b); an append with new files does the same
    /// in Write. False: nothing missed.
    public static bool HealHashList(string cardPath, string cardName)
    {
        bool healed = false;
        foreach (var algorithm in new[] { "xxh64", "md5" })
        {
            var hashList = HashListPath(cardPath, cardName, algorithm);
            var (earlier, missed) = HashListAndMissed(hashList, cardPath, [], algorithm);
            if (missed.Length == 0) continue;
            NativeIO.WriteFileReplacing(hashList, [.. earlier, .. missed]);
            healed = true;
        }
        return healed;
    }

    /// The card's hash list as the earlier generations left it (exact bytes, then a line break if it had none), and
    /// the `algorithm` lines of every path the ASC MHL history records but neither the list nor `alsoListed` has.
    static (byte[] Earlier, byte[] Missed) HashListAndMissed(string path, string cardPath, IEnumerable<string> alsoListed, string algorithm)
    {
        var earlier = File.Exists(path) ? File.ReadAllBytes(path) : [];
        if (earlier.Length > 0 && earlier[^1] != (byte)'\n') earlier = [.. earlier, (byte)'\n'];
```

`windows/src/Offload.Core/OffloadReport.cs`, řádky 250–256 — najít:

```csharp
        var missed = new StringBuilder();
        if (Verification.MhlReader.Read(cardPath) is { } mhl)
            foreach (var r in mhl.LastGood.Values.OrderBy(r => r.Path, StringComparer.Ordinal))
                if (!listed.Contains(r.Path) && r.Hashes.FirstOrDefault(h => h.Algorithm == "xxh64") is { } h)
                    missed.Append($"{h.Value}  {r.Path}\n");
        return (earlier, Encoding.UTF8.GetBytes(missed.ToString()));
    }
```

nahradit:

```csharp
        var missed = new StringBuilder();
        if (Verification.MhlReader.Read(cardPath) is { } mhl)
            foreach (var r in mhl.LastGood.Values.OrderBy(r => r.Path, StringComparer.Ordinal))
                if (!listed.Contains(r.Path) && r.Hashes.FirstOrDefault(h => h.Algorithm == algorithm) is { } h)
                    missed.Append($"{h.Value}  {r.Path}\n");
        return (earlier, Encoding.UTF8.GetBytes(missed.ToString()));
    }
```

`windows/src/Offload.Core/Mhl.cs`, řádky 75–80 — najít:

```csharp
            Line("    <hash>");
            Line($"      <path size=\"{f.Size}\" lastmodificationdate=\"{IsoDate(f.Modified)}\">{Esc(f.Path)}</path>");
            Line($"      <xxh64 action=\"{action}\" hashdate=\"{stamp}\">{f.Xxh64}</xxh64>");
            Line("    </hash>");
        }
        Line("  </hashes>");
```

nahradit:

```csharp
            Line("    <hash>");
            Line($"      <path size=\"{f.Size}\" lastmodificationdate=\"{IsoDate(f.Modified)}\">{Esc(f.Path)}</path>");
            Line($"      <xxh64 action=\"{action}\" hashdate=\"{stamp}\">{f.Xxh64}</xxh64>");
            if (f.Md5 != null) Line($"      <md5 action=\"{action}\" hashdate=\"{stamp}\">{f.Md5}</md5>");   // spec 3.5
            Line("    </hash>");
        }
        Line("  </hashes>");
```

`windows/src/Offload.Core/HtmlReport.cs`, řádky 111–117 — najít:

```csharp
        Stat(h, videoClips.Count.ToString(Inv), "klipů" + (totalDuration > 0 ? " · " + Duration(totalDuration) : ""));
        Stat(h, Duration(r.CopySeconds), "kopírování · " + Format.Rate(r.TotalBytes, r.CopySeconds));
        Stat(h, r.Verified ? Duration(r.VerifySeconds) : "—", "ověření · " + (r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "neprovedeno"));
        Stat(h, Esc(r.HashAlgorithm.ToUpperInvariant()), "kontrolní součet");
        h.Append("</div>\n\n");

        h.Append("<h2>Cíle</h2><div class=\"panel\"><table>\n");
```

nahradit:

```csharp
        Stat(h, videoClips.Count.ToString(Inv), "klipů" + (totalDuration > 0 ? " · " + Duration(totalDuration) : ""));
        Stat(h, Duration(r.CopySeconds), "kopírování · " + Format.Rate(r.TotalBytes, r.CopySeconds));
        Stat(h, r.Verified ? Duration(r.VerifySeconds) : "—", "ověření · " + (r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "neprovedeno"));
        Stat(h, Esc(string.Join(" + ", r.Algorithms.Select(a => a.ToUpperInvariant()))), "kontrolní součet");
        h.Append("</div>\n\n");

        h.Append("<h2>Cíle</h2><div class=\"panel\"><table>\n");
```

`windows/src/Offload.Core/HtmlReport.cs`, řádky 146–157 — najít:

```csharp
        if (r.Clips.Count > 0) AppendClips(h, r);

        h.Append($"<details{(forPrint ? " open" : "")}><summary>Všechny soubory ({r.Files.Count})</summary><div class=\"panel\"><table>\n");
        h.Append($"<tr><th>Soubor</th><th class=\"num\">Velikost</th><th>{Esc(r.HashAlgorithm)}</th>");
        for (int i = 0; i < r.Destinations.Count; i++) h.Append($"<th>Cíl {i + 1}</th>");
        h.Append("</tr>\n");
        foreach (var f in r.Files)
        {
            h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"num\">{Format.Bytes(f.Size)}</td><td class=\"mono\">{Esc(f.Xxh64 ?? "—")}</td>");
            foreach (var s in f.Destinations)
            {
                var cls = s == "verified" ? "ok" : (s == "failed" ? "bad" : "warn");
```

nahradit:

```csharp
        if (r.Clips.Count > 0) AppendClips(h, r);

        h.Append($"<details{(forPrint ? " open" : "")}><summary>Všechny soubory ({r.Files.Count})</summary><div class=\"panel\"><table>\n");
        bool md5 = r.Algorithms.Contains("md5");
        h.Append($"<tr><th>Soubor</th><th class=\"num\">Velikost</th><th>xxh64</th>{(md5 ? "<th>md5</th>" : "")}");
        for (int i = 0; i < r.Destinations.Count; i++) h.Append($"<th>Cíl {i + 1}</th>");
        h.Append("</tr>\n");
        foreach (var f in r.Files)
        {
            h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"num\">{Format.Bytes(f.Size)}</td><td class=\"mono\">{Esc(f.Xxh64 ?? "—")}</td>");
            if (md5) h.Append($"<td class=\"mono\">{Esc(f.Md5 ?? "—")}</td>");
            foreach (var s in f.Destinations)
            {
                var cls = s == "verified" ? "ok" : (s == "failed" ? "bad" : "warn");
```

`windows/src/Offload.Core/Hashing.cs`, řádky 24–29 — najít:

```csharp
    /// From a report's hash names ("md5" anywhere → both); anything else is xxh64.
    public static HashKind FromNames(IEnumerable<string>? names) =>
        names != null && names.Contains("md5", StringComparer.OrdinalIgnoreCase) ? HashKind.Both : HashKind.Xxh64;
}

/// The checksums of one file; null = not computed (the file failed, or MD5 was not asked for).
```

nahradit:

```csharp
    /// From a report's hash names ("md5" anywhere → both); anything else is xxh64.
    public static HashKind FromNames(IEnumerable<string>? names) =>
        names != null && names.Contains("md5", StringComparer.OrdinalIgnoreCase) ? HashKind.Both : HashKind.Xxh64;

    /// settings.json `hash` and CLI --hash: "xxh64" | "both".
    public static string Wire(HashKind kinds) => Normalize(kinds).HasFlag(HashKind.Md5) ? "both" : "xxh64";

    public static bool TryParseWire(string? s, out HashKind kinds)
    {
        kinds = s switch { "xxh64" => HashKind.Xxh64, "both" => HashKind.Both, _ => default };
        return kinds != default;
    }

    /// The setting as the copy uses it: anything this version does not know (a newer value) is xxh64.
    public static HashKind FromSetting(string? s) => TryParseWire(s, out var k) ? k : HashKind.Xxh64;

    /// For people: "xxHash64", "xxHash64 + MD5".
    public static string Label(HashKind kinds) => Normalize(kinds).HasFlag(HashKind.Md5) ? "xxHash64 + MD5" : "xxHash64";
}

/// The checksums of one file; null = not computed (the file failed, or MD5 was not asked for).
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 23–28 — najít:

```csharp
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("tools")] public ExternalTools Tools { get; set; } = new();
    [JsonPropertyName("naming")] public NamingSettings Naming { get; set; } = new();
    [JsonPropertyName("portNames")]
    public Dictionary<string, string> PortNames
    {
```

nahradit:

```csharp
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("tools")] public ExternalTools Tools { get; set; } = new();
    [JsonPropertyName("naming")] public NamingSettings Naming { get; set; } = new();
    /// "xxh64" (default) or "both" = xxh64 + MD5 (spec 3.5, R16). A value a newer version wrote is kept as it is and
    /// read as xxh64 (HashKinds.FromSetting).
    [JsonPropertyName("hash")] public string Hash { get; set; } = "xxh64";
    [JsonPropertyName("portNames")]
    public Dictionary<string, string> PortNames
    {
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 53–59 — najít:

```csharp
    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The top-level keys Load reads into properties; every other key goes to AppSettings.Extra.
    static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "destRoots", "verify", "analyzeClips", "proxy", "tools", "naming", "portNames" };

    static readonly JsonSerializerOptions Json = new()
    {
```

nahradit:

```csharp
    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The top-level keys Load reads into properties; every other key goes to AppSettings.Extra.
    static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "destRoots", "verify", "analyzeClips", "proxy", "tools", "naming", "portNames", "hash" };

    static readonly JsonSerializerOptions Json = new()
    {
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 96–101 — najít:

```csharp
                .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList());
            Section("verify", e => s.Verify = e.GetBoolean());
            Section("analyzeClips", e => s.AnalyzeClips = e.GetBoolean());
            void Kept<T>(string key, Func<T, T> set, Func<T> fresh) where T : class
            {
                if (!root.TryGetValue(key, out var e) || e.ValueKind == JsonValueKind.Null) return;
```

nahradit:

```csharp
                .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList());
            Section("verify", e => s.Verify = e.GetBoolean());
            Section("analyzeClips", e => s.AnalyzeClips = e.GetBoolean());
            Section("hash", e => s.Hash = e.GetString() is { Length: > 0 } h ? h : "xxh64");
            void Kept<T>(string key, Func<T, T> set, Func<T> fresh) where T : class
            {
                if (!root.TryGetValue(key, out var e) || e.ValueKind == JsonValueKind.Null) return;
```

`windows/src/Offload.Core/Desktop/Texts.cs`, řádky 20–23 — najít:

```csharp
        $"{n} {Plural(n, "soubor selhal", "soubory selhaly", "souborů selhalo")} — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.";

    public static string Warnings(int n) => n == 0 ? "bez varování" : $"{n} varování";
}
```

nahradit:

```csharp
        $"{n} {Plural(n, "soubor selhal", "soubory selhaly", "souborů selhalo")} — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.";

    public static string Warnings(int n) => n == 0 ? "bez varování" : $"{n} varování";

    /// The verify checkbox names the checksum it reads back with (spec 3.5).
    public static string VerifyLabel(HashKind kinds) => $"Ověřit kopie zpětným čtením ({HashKinds.Label(kinds)})";

    public const string Md5Slower = "MD5 je pomalejší — u rychlých karet (CFexpress) může zpomalit kopii.";
}

/// One choice of "Kontrolní součet" (spec 3.5): xxHash64, or xxHash64 + MD5 — never MD5 alone (R16).
public sealed record HashChoice(HashKind Kind, string Label)
{
    public static readonly IReadOnlyList<HashChoice> All = [new(HashKind.Xxh64, "xxHash64 (rychlý)"), new(HashKind.Both, "xxHash64 + MD5")];

    public static HashChoice Of(HashKind kinds) => All.First(c => c.Kind == (HashKinds.Normalize(kinds).HasFlag(HashKind.Md5) ? HashKind.Both : HashKind.Xxh64));
}
```

`windows/src/Offload.Core/Append/AppendPlanner.cs`, řádky 163–169 — najít:

```csharp
            NewFiles = newFiles,
            NewDirs = newDirs,
            UnverifiedEarlier = unverified,
            HashAlgorithms = [first.HashAlgorithm],
        };
    }

```

nahradit:

```csharp
            NewFiles = newFiles,
            NewDirs = newDirs,
            UnverifiedEarlier = unverified,
            HashAlgorithms = first.Algorithms,
        };
    }

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 302–308 — najít:

```csharp
                    Date = plan.Date,
                    Success = true,
                    Verified = true,
                    HashAlgorithms = [report.HashAlgorithm],
                    Destinations = plan.DestRoots.Select((root, i) => new HistoryDestination { Root = root, CardPath = cardPaths[i] }).ToList(),
                };
                entry.Finished = report.Finished;
```

nahradit:

```csharp
                    Date = plan.Date,
                    Success = true,
                    Verified = true,
                    HashAlgorithms = [.. report.Algorithms],
                    Destinations = plan.DestRoots.Select((root, i) => new HistoryDestination { Root = root, CardPath = cardPaths[i] }).ToList(),
                };
                entry.Finished = report.Finished;
```

`windows/src/Offload.Core/Messages.cs`, řádky 151–156 — najít:

```csharp
    public static string UnknownProxyCodec(string? x) =>
        $"Neznámý kodek proxy „{x}“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.";
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string ProxyFolderNotFound(string path) => $"Složka pro proxy nenalezena: {path}";
    public const string ProxyFolderNoEffect = "Poznámka: --proxy-folder bez --proxy nic nedělá.";
    public const string Cancelling = "Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…";
```

nahradit:

```csharp
    public static string UnknownProxyCodec(string? x) =>
        $"Neznámý kodek proxy „{x}“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.";
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string UnknownHash(string? x) => $"Neznámý kontrolní součet „{x}“ — použijte xxh64 nebo both (xxh64 + MD5).";
    public static string ProxyFolderNotFound(string path) => $"Složka pro proxy nenalezena: {path}";
    public const string ProxyFolderNoEffect = "Poznámka: --proxy-folder bez --proxy nic nedělá.";
    public const string Cancelling = "Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…";
```

`windows/src/Offload.Core/Bench.cs`, řádky 1–5 — najít:

```csharp
using System.Diagnostics;
using System.IO.Hashing;

namespace Offload.Core;

```

nahradit:

```csharp
using System.Diagnostics;

namespace Offload.Core;

```

`windows/src/Offload.Core/Bench.cs`, řádky 50–59 — najít:

```csharp
        return lines;
    }

    /// Writes a temporary file (unbuffered, flushed to the media), reads it back, deletes it.
    /// ct: checked before every block of the write and the read-back — a cancelled test throws
    /// OperationCanceledException at once and the temporary file is still deleted.
    public static IReadOnlyList<Line> Write(string dir, long size, int blockSize, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(dir, $".offload-bench-{Environment.ProcessId}");
```

nahradit:

```csharp
        return lines;
    }

    /// Writes a temporary file (unbuffered, flushed to the media), reads it back through `kinds` (as the copy verifies),
    /// deletes it. ct: checked before every block of the write and the read-back — a cancelled test throws
    /// OperationCanceledException at once and the temporary file is still deleted.
    public static IReadOnlyList<Line> Write(string dir, long size, int blockSize, CancellationToken ct = default, HashKind kinds = HashKind.Xxh64)
    {
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(dir, $".offload-bench-{Environment.ProcessId}");
```

`windows/src/Offload.Core/Bench.cs`, řádky 76–83 — najít:

```csharp
            }
            var write = new Line($"write {BlockLabel(blockSize)}", size, sw.Elapsed.TotalSeconds);
            sw.Restart();
            var (_, read) = Hashing.HashFile(path, buf, ct: ct);
            return [write, new Line($"read  {BlockLabel(blockSize)} + xxh64", read, sw.Elapsed.TotalSeconds)];
        }
        finally
        {
```

nahradit:

```csharp
            }
            var write = new Line($"write {BlockLabel(blockSize)}", size, sw.Elapsed.TotalSeconds);
            sw.Restart();
            var (_, read) = Hashing.HashFile(path, buf, kinds, ct: ct);
            return [write, new Line($"read  {BlockLabel(blockSize)} + {string.Join(" + ", HashKinds.Names(kinds))}", read, sw.Elapsed.TotalSeconds)];
        }
        finally
        {
```

`windows/src/Offload.Core/Bench.cs`, řádky 85–100 — najít:

```csharp
        }
    }

    /// Raw hashing throughput from memory — must be far above any disk, otherwise it is the bottleneck.
    public static Line Hash(int size)
    {
        using var buf = new AlignedBuffer(size);
        Random.Shared.NextBytes(buf.Span);
        var sw = Stopwatch.StartNew();
        var xx = new XxHash64();
        for (int i = 0; i < 8; i++) xx.Append(buf.Span);
        xx.GetCurrentHashAsUInt64();
        return new Line("xxh64 in RAM", (long)size * 8, sw.Elapsed.TotalSeconds);
    }

    internal static string BlockLabel(int bs) => bs >= 1 << 20 ? $"{bs >> 20} MB" : $"{bs >> 10} KB";
```

nahradit:

```csharp
        }
    }

    /// Raw hashing throughput from memory — must be far above any disk, otherwise it is the bottleneck. `algorithm`:
    /// "xxh64" or "md5" (spec 3.5: MD5 runs on its own thread during a copy, so this is its ceiling per card).
    public static Line Hash(int size, string algorithm = "xxh64")
    {
        using var buf = new AlignedBuffer(size);
        Random.Shared.NextBytes(buf.Span);
        var sw = Stopwatch.StartNew();
        var h = Verification.HashAlgo.Create(algorithm);
        for (int i = 0; i < 8; i++) h.Append(buf.Span);
        h.Finish();
        return new Line($"{algorithm} in RAM", (long)size * 8, sw.Elapsed.TotalSeconds);
    }

    internal static string BlockLabel(int bs) => bs >= 1 << 20 ? $"{bs >> 20} MB" : $"{bs >> 10} KB";
```

- [ ] **Step 4: Implementace — CLI**

`windows/src/Offload.Cli/BenchCommand.cs`, řádky 10–26 — najít:

```csharp
        var read = new Option<string?>("--read") { Description = "Card or file to read from." };
        var write = new Option<string[]>("--write") { Description = "Destination folder to test write speed (repeatable).", DefaultValueFactory = _ => [] };
        var gb = new Option<double>("--gb") { Description = "Amount of data per test in GB.", DefaultValueFactory = _ => 2, CustomParser = a => CliParse.Parse(a) ?? 2 };
        var cmd = new Command("bench", "Measure card read speed and disk write/read speed.");
        cmd.Options.Add(read);
        cmd.Options.Add(write);
        cmd.Options.Add(gb);
        cmd.SetAction(r =>
        {
            long bytes = (long)(r.GetValue(gb) * 1_000_000_000);
            try
            {
                Console.WriteLine("CPU");
                Print(Bench.Hash(256 << 20));
                if (r.GetValue(read) is string rd)
                {
                    Console.WriteLine($"Read {rd}");
```

nahradit:

```csharp
        var read = new Option<string?>("--read") { Description = "Card or file to read from." };
        var write = new Option<string[]>("--write") { Description = "Destination folder to test write speed (repeatable).", DefaultValueFactory = _ => [] };
        var gb = new Option<double>("--gb") { Description = "Amount of data per test in GB.", DefaultValueFactory = _ => 2, CustomParser = a => CliParse.Parse(a) ?? 2 };
        var hash = new Option<string>("--hash") { Description = "Checksums to measure: xxh64 (default) or md5 (xxh64 + MD5, as a copy with --hash both).", DefaultValueFactory = _ => "xxh64" };
        var cmd = new Command("bench", "Measure card read speed and disk write/read speed.");
        cmd.Options.Add(read);
        cmd.Options.Add(write);
        cmd.Options.Add(gb);
        cmd.Options.Add(hash);
        cmd.SetAction(r =>
        {
            long bytes = (long)(r.GetValue(gb) * 1_000_000_000);
            var kinds = r.GetValue(hash) switch
            {
                "xxh64" => HashKind.Xxh64,
                "md5" or "both" => HashKind.Both,
                _ => (HashKind?)null,
            };
            if (kinds is not HashKind chosen)
            {
                Console.Error.WriteLine(Msg.CliError + Msg.UnknownHash(r.GetValue(hash)));
                return 1;
            }
            try
            {
                Console.WriteLine("CPU");
                Print(Bench.Hash(256 << 20));
                if (chosen.HasFlag(HashKind.Md5)) Print(Bench.Hash(256 << 20, "md5"));
                if (r.GetValue(read) is string rd)
                {
                    Console.WriteLine($"Read {rd}");
```

`windows/src/Offload.Cli/BenchCommand.cs`, řádky 29–35 — najít:

```csharp
                foreach (var w in r.GetValue(write) ?? [])
                {
                    Console.WriteLine($"Write {w}");
                    foreach (var l in Bench.Write(CliPath.Full(w), bytes, 16 << 20)) Print(l);
                }
                return 0;
            }
```

nahradit:

```csharp
                foreach (var w in r.GetValue(write) ?? [])
                {
                    Console.WriteLine($"Write {w}");
                    foreach (var l in Bench.Write(CliPath.Full(w), bytes, 16 << 20, kinds: chosen)) Print(l);
                }
                return 0;
            }
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 22–32 — najít:

```csharp
        var allowSame = new Option<bool>("--allow-same-device") { Description = "Allow two destinations on the same volume." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy." };
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (%APPDATA%\\Offload\\history.json)." };
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
        {
```

nahradit:

```csharp
        var allowSame = new Option<bool>("--allow-same-device") { Description = "Allow two destinations on the same volume." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy." };
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (%APPDATA%\\Offload\\history.json)." };
        var hash = new Option<string>("--hash") { Description = "Checksums: xxh64 (default) or both (xxh64 + MD5; MD5 is slower on fast cards).", DefaultValueFactory = _ => "xxh64" };
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, hash }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
        {
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 42–47 — najít:

```csharp
                    Verify = !r.GetValue(noVerify),
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                };
                stageTwo.Apply(r, config);
            }
```

nahradit:

```csharp
                    Verify = !r.GetValue(noVerify),
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                    Hashes = HashKinds.TryParseWire(r.GetValue(hash), out var kinds) ? kinds : throw new ArgumentException(Msg.UnknownHash(r.GetValue(hash))),
                };
                stageTwo.Apply(r, config);
            }
```

`offload append` volbu `--hash` nedostane: doplnění počítá součty dřívější zálohy (§3.5; viz „Rozhodnutí“).

- [ ] **Step 5: Implementace — GUI**

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 404–409 — najít:

```csharp
        set { Settings.Verify = value; SettingsChanged(); Raise(); }
    }

    public bool AnalyzeClips
    {
        get => Settings.AnalyzeClips;
```

nahradit:

```csharp
        set { Settings.Verify = value; SettingsChanged(); Raise(); }
    }

    /// "Kontrolní součet" (spec 3.5): xxHash64 or xxHash64 + MD5 — `hash` in settings.json.
    public IReadOnlyList<HashChoice> HashChoices => HashChoice.All;

    public HashChoice Hash
    {
        get => HashChoice.Of(HashKinds.FromSetting(Settings.Hash));
        set
        {
            if (value == null) return;
            Settings.Hash = HashKinds.Wire(value.Kind);
            SettingsChanged();
            RaiseMany(nameof(Hash), nameof(VerifyLabel), nameof(Md5Note));
        }
    }

    public string VerifyLabel => Texts.VerifyLabel(HashKinds.FromSetting(Settings.Hash));

    /// Shown under the choice while MD5 is on.
    public string? Md5Note => HashKinds.FromSetting(Settings.Hash).HasFlag(HashKind.Md5) ? Texts.Md5Slower : null;

    public bool AnalyzeClips
    {
        get => Settings.AnalyzeClips;
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 205–210 — najít:

```csharp
            {
                CardName = name,
                Date = DateString,
                Verify = Settings.Verify,
                AnalyzeClips = Settings.AnalyzeClips,
                Proxy = SettingsStore.Clone(Settings.Proxy),
```

nahradit:

```csharp
            {
                CardName = name,
                Date = DateString,
                Hashes = HashKinds.FromSetting(Settings.Hash),   // an append (above) keeps the earlier backup's (spec 3.5)
                Verify = Settings.Verify,
                AnalyzeClips = Settings.AnalyzeClips,
                Proxy = SettingsStore.Clone(Settings.Proxy),
```

`windows/src/Offload.App/SetupPanel.xaml`, řádky 184–190 — najít:

```xml
                                Visibility="{Binding UseCustomDate, Converter={StaticResource BoolToVisible}}" />
                </StackPanel>
                <TextBlock Text="{Binding PathPreview}" FontFamily="Consolas" Style="{StaticResource Caption}" Margin="0,6,0,0" />
                <CheckBox Content="Ověřit kopie zpětným čtením (xxHash64)" IsChecked="{Binding Verify}" />
                <TextBlock Text="Bez ověření není jisté, že data na disku jsou v pořádku." Style="{StaticResource WarningText}"
                           Visibility="{Binding Verify, Converter={StaticResource BoolToCollapsed}}" />
                <CheckBox Content="Analýza klipů a náhledy v reportu" IsChecked="{Binding AnalyzeClips}" />
```

nahradit:

```xml
                                Visibility="{Binding UseCustomDate, Converter={StaticResource BoolToVisible}}" />
                </StackPanel>
                <TextBlock Text="{Binding PathPreview}" FontFamily="Consolas" Style="{StaticResource Caption}" Margin="0,6,0,0" />
                <DockPanel Margin="0,6,0,0">
                    <TextBlock Text="Kontrolní součet" Style="{StaticResource Caption}" VerticalAlignment="Center" Width="110" />
                    <ComboBox ItemsSource="{Binding HashChoices}" SelectedItem="{Binding Hash}" DisplayMemberPath="Label"
                              AutomationProperties.AutomationId="HashChoice" />
                </DockPanel>
                <TextBlock Text="{Binding Md5Note}" Style="{StaticResource WarningText}" TextWrapping="Wrap"
                           Visibility="{Binding Md5Note, Converter={StaticResource NullToCollapsed}}" />
                <CheckBox Content="{Binding VerifyLabel}" IsChecked="{Binding Verify}" />
                <TextBlock Text="Bez ověření není jisté, že data na disku jsou v pořádku." Style="{StaticResource WarningText}"
                           Visibility="{Binding Verify, Converter={StaticResource BoolToCollapsed}}" />
                <CheckBox Content="Analýza klipů a náhledy v reportu" IsChecked="{Binding AnalyzeClips}" />
```

- [ ] **Step 6: Spustit — projdou**

Run: stejný filtr jako v kroku 2.
Expected: PASS. Pak `dotnet build windows\Offload.sln -warnaserror` (XAML se zkompiluje, 0 varování) a celá sada.

- [ ] **Step 7: VYVOJ + commit**

`docs/VYVOJ-WINDOWS.md`, řádky 20–25 — najít:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 4 — jádro MD5 (R16: jen navíc k xxh64): `HashKind` (`Xxh64`, `Md5`, xxh64 vždy), `FileHashes` místo `ulong?[]`, jeden `SourceHasher` na algoritmus na vlastním vlákně (blok karty má o konzumenta víc, MD5 nebrzdí xxh64 ani čtení karty), zpětné čtení cíle hashuje všemi zvolenými v jednom průchodu („MD5 MISMATCH“ u nesouhlasu); celá kopie podle `OffloadOptions.Hashes`, doplnění podle dřívější zálohy (`OffloadPlan.Hashes`)
- Úkol 3 — proxy pro přelinkování: s platným TC navíc `reel_name` (muxer MOV ho zapíše do stopy timecodu `tmcd`, ffprobe ho tam ukáže; ověřeno s ffmpeg 8.1), všechny zvukové proudy zdroje (`-map 0:a?`; H.264/HEVC AAC 64 kb/s na kanál každého proudu, 128–512 kb/s; ProRes PCM), bez TC ani `tmcd`, ani cívka; proxy přejmenovaná kvůli shodě jmen má poznámku „Proxy přejmenována na … (shoda jmen) — přelinkujte ji ručně.“; odhad místa počítá 0,2 Mb/s na každou zvukovou stopu
- Úkol 2 — cívka klipu (`ReelResolver`): tag kamery z ffprobe (`reel_name`, `tape_name`, `reel` — u MOV na stopě timecodu, u MXF na každém proudu), jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty; v reportu `clips[].info.reel`
````

nahradit:

````markdown

## 2026-10-07 — plán 7: výstupy pro produkci a střih

- Úkol 5 — výstupy MD5: v reportu `hashAlgorithms` (jen když není `["xxh64"]`) a `files[].md5`, `hashAlgorithm` zůstává `xxh64`; `_REPORT\<karta>.md5` (formát md5sum, při doplnění se prodlouží jako `.xxh64`); v ASC MHL `<md5>` vedle `<xxh64>`; TXT `Hash: xxh64+md5`, HTML dlaždice „XXH64 + MD5“ a sloupec md5; nastavení `hash` (`xxh64` | `both`, neznámá hodnota se zachová a čte jako xxh64), v GUI „Kontrolní součet“ + „Ověřit kopie zpětným čtením (…)“ + upozornění na pomalejší MD5; CLI `copy --hash xxh64|both`, `bench --hash md5`; doplnění a historie podle algoritmů dřívější zálohy
- Úkol 4 — jádro MD5 (R16: jen navíc k xxh64): `HashKind` (`Xxh64`, `Md5`, xxh64 vždy), `FileHashes` místo `ulong?[]`, jeden `SourceHasher` na algoritmus na vlastním vlákně (blok karty má o konzumenta víc, MD5 nebrzdí xxh64 ani čtení karty), zpětné čtení cíle hashuje všemi zvolenými v jednom průchodu („MD5 MISMATCH“ u nesouhlasu); celá kopie podle `OffloadOptions.Hashes`, doplnění podle dřívější zálohy (`OffloadPlan.Hashes`)
- Úkol 3 — proxy pro přelinkování: s platným TC navíc `reel_name` (muxer MOV ho zapíše do stopy timecodu `tmcd`, ffprobe ho tam ukáže; ověřeno s ffmpeg 8.1), všechny zvukové proudy zdroje (`-map 0:a?`; H.264/HEVC AAC 64 kb/s na kanál každého proudu, 128–512 kb/s; ProRes PCM), bez TC ani `tmcd`, ani cívka; proxy přejmenovaná kvůli shodě jmen má poznámku „Proxy přejmenována na … (shoda jmen) — přelinkujte ji ručně.“; odhad místa počítá 0,2 Mb/s na každou zvukovou stopu
- Úkol 2 — cívka klipu (`ReelResolver`): tag kamery z ffprobe (`reel_name`, `tape_name`, `reel` — u MOV na stopě timecodu, u MXF na každém proudu), jinak cívka z názvu klipu (`A001C003…` → `A001`), jinak název složky karty; v reportu `clips[].info.reel`
````

Zpráva do `.git/COMMIT_MSG_PLAN7`:

```text
Windows: MD5 outputs (report keys, .md5 list, MHL md5, hash setting, GUI, --hash)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```bash
git add docs/VYVOJ-WINDOWS.md windows/src/Offload.App/MainViewModel.Queue.cs windows/src/Offload.App/MainViewModel.Setup.cs windows/src/Offload.App/SetupPanel.xaml windows/src/Offload.Cli/BenchCommand.cs windows/src/Offload.Cli/CopyCommand.cs windows/src/Offload.Core/Append/AppendPlanner.cs windows/src/Offload.Core/Bench.cs windows/src/Offload.Core/Desktop/AppSettings.cs windows/src/Offload.Core/Desktop/Texts.cs windows/src/Offload.Core/Hashing.cs windows/src/Offload.Core/HtmlReport.cs windows/src/Offload.Core/Messages.cs windows/src/Offload.Core/Mhl.cs windows/src/Offload.Core/OffloadJob.cs windows/src/Offload.Core/OffloadReport.cs windows/tests/Offload.Core.Tests/AppendEngineTests.cs windows/tests/Offload.Core.Tests/BenchTests.cs windows/tests/Offload.Core.Tests/CliOutputsTests.cs windows/tests/Offload.Core.Tests/EngineTests.cs windows/tests/Offload.Core.Tests/HashingTests.cs windows/tests/Offload.Core.Tests/ReportTests.cs windows/tests/Offload.Core.Tests/SettingsStoreTests.cs
git commit -F .git/COMMIT_MSG_PLAN7
```

Pokračování: úkoly 6–10 v `windows/docs/superpowers/plans/2026-10-07-offload-windows-7-vystupy-2.md`.
