# Offload pro Windows — plán 10: Verify-in-Proxy (úkoly 1–9) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ověřit primární cílový disk čtením, které stejně dělá tvorba proxy (ffmpeg přes vlastní HTTP tap Offloadu, RAW SDK z cache naplněné jedním ověřujícím čtením), ostatní disky souběžně na pozadí, a rozdělit verdikt karty na **„Lze vyjmout“** (vše zapsané) a **„Lze formátovat“** (vše ověřené) — s blokovým manifestem, navázáním po pádu, XXH128 v ASC MHL a obsahovou kontrolou dekodérem, jako verze 0.6.0.

**Architecture:** Nové díly jsou v `windows/src/Offload.Core/VerifyInProxy/` a napojují se na stávající engine (R1, žádný druhý engine): `SourceHasher` při čtení karty počítá i XXH3-128 souboru a XXH3-64 každého 16MiB bloku, `DestWriter` zapisuje `NO_BUFFERING | WRITE_THROUGH` a vlákno `offload.manifest` připisuje do `_REPORT/offload-manifest.jsonl` záznamy souborů a kontrolní body. S proxy kopie neověřuje (`DeferVerify`, reporty `verifyPending`, bez generace MHL); 2. fáze spustí `VerifySession`: primární cíl (zvolený, jinak nejrychlejší při kopii) čte ffmpeg přes `RangeServer` + `TapFile` (režim A) nebo `CacheWarm` (režim B) a každý blok jde do `BlockVerifier`, ostatní cíle čte vlákno na pozadí s nízkou prioritou I/O, po proxy `BlockReads` dočte, co nikdo nečetl, a `OffloadReport.Commit` zapíše generaci ASC MHL pod zámkem složek. `ResumePlanner` dokončí nepotvrzenou generaci přes doplnění (R6). UI, oznámení, historie a denní report dostanou dva verdikty (R5).

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, xUnit, BCL + `System.IO.Hashing` 10.0.12 (už v `Offload.Core.csproj`: `XxHash64`, `XxHash3`, `XxHash128`), `System.Net.Sockets.TcpListener` (tap), Win32 `CreateFile` s `FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH`, `SetThreadPriority(THREAD_MODE_BACKGROUND_BEGIN)`, `GlobalMemoryStatusEx`, ffmpeg/ffprobe 8.1 (CLI, jako dosud).

**Spec:** `windows/docs/superpowers/specs/2026-10-08-offload-verify-in-proxy-design.md` (specifikace uživatele + rozhodnutí R1–R12; kde se liší, platí R). Plán má dva soubory: tento (úkoly 1–9: hashe, ověřovač, manifest, zápis, kontrola cache, odložené ověření, tap, režim B) a `windows/docs/superpowers/plans/2026-10-08-offload-windows-10-verify-in-proxy-2.md` (úkoly 10–18: obsahová kontrola, relace ověření, úloha, navázání po pádu, R12, UI, nastavení, reporty, dokumentace a verze; na konci kontrola proti spec).

## Global Constraints

- **Základ:** `main` na ef25348 (0.5.2; obsahuje `windows-proxy-tree` 29f555f, na kterém spec vznikl, a jeho opravy 779dce5 a c25165e). Pracovní větev `windows-verify-in-proxy` z ef25348. Všechny příkazy git a dotnet se spouštějí z kořene repa (`D:\My\Projekt\offload` nebo jeho worktree).
- Žádný nový NuGet balíček. `System.IO.Hashing` 10.0.12 už projekt odkazuje (XxHash128/XxHash3 **nejsou** ve sdíleném frameworku `Microsoft.NETCore.App` 10.0.12 — jsou v tomto balíčku).
- Texty UI česky („Lze vyjmout“, „Lze formátovat“, „Dokončení ověření“, „Ověřit primární disk během proxy“ — doslova); identifikátory anglicky; logy, TXT report a chyby v JSON reportu anglicky (HTML je ukáže česky).
- UI vlákno nikdy nečeká na disk ani na síť: kontrola cache, ověření, commit a otevření náhledu vady běží mimo UI vlákno (`Task.Run`, vlákna úlohy); view-modely jen čtou `JobStatus`.
- Na kartu se nikdy nic nezapisuje. Karta se čte znovu jen u bloku, který nesouhlasí stejně na víc cílech (spec 10), a jen když je to ta samá karta (`CardStillThere`).
- Nikdy nepřepsat existující soubor na cíli (R7): nesoulad = `failed` + „Zkopírovat znovu“ (oprava odloží vadnou kopii do `neshoda_NNNN`); manifest se jen připisuje (testy plánů 6 a 9 ho hlídají jako soubor, do kterého se přidává na konec); reporty a MHL podle pravidel plánu 6 (nová generace = nové soubory, `Commit` nahradí jen reporty té generace s `verifyPending`).
- `mac/` se nemění; co má Mac převzít, jde do `docs/VYVOJ-MAC.md`, rozdíly do `docs/ROZDILY.md` (úkol 18).
- GUI se při práci na úkolech **nikdy nespouští** a nikdy se nespouští žádný nalezený `offload.exe` / `Offload.exe` (ani `--demo`). Ruční testy 50–58 (úkol 18 je zapíše do `windows/README.md`) dělá uživatel.
- Testy nikdy nesahají na skutečné `%APPDATA%` (historie přes `TestEnvironment` / `HistoryFolder` v `TempDir`), na kořeny skutečných svazků (sonda cache jde do `_REPORT` cíle v `TempDir`) ani na síť mimo `127.0.0.1` (tap naslouchá jen na `IPAddress.Loopback`; testy se připojují jen tam).
- Blok = 16 MiB = `OffloadOptions.ChunkSize` (poslední kratší); blokový hash XXH3-64 (jen v manifestu); souborový hash XXH3-128 **a** xxh64 (R2: `.xxh64` a `hashAlgorithms` beze změny, MD5 režim dál navíc); manifest `_REPORT/offload-manifest.jsonl` (R4); kontrolní bod každých 64 bloků (1 GiB).
- Formáty čitelné Macem: jen **přidané** klíče — report `files[].xxh128`, `verifyPending`, `safeToFormatSeconds`, `verifyNotes`, `destinations[].verifyMethod` / `verifyNote` / `volumeSerial`, `clips[].content`; ASC MHL `<xxh128>` vedle `<xxh64>`, metoda v `<creatorinfo><comment>`; `history.json` `unfinished`. Popis do `docs/FORMATY.md` v úkolu 18.
- Výchozí stav (R11): verify-in-proxy zapnuté, když jsou zapnuté proxy a ověření a je ffmpeg; mimo to (bez proxy, `--classic-verify`, vypnutá volba, oprava) zpětné čtení hned po kopii jako v 0.5.x. Síťový cíl a cíl, který neprojde kontrolou čtení mimo cache, se ověří zpětným čtením vždy (důvod v reportu).
- `TreatWarningsAsErrors`: každý úkol končí `dotnet build windows\Offload.sln -warnaserror` bez varování a celou zelenou sadou `dotnet test windows\Offload.sln` (start na ef25348: 1494 testů, z toho 8 přeskočených; po plánu 1645 testů, z toho 8 přeskočených). Časově citlivé (ffmpeg a vlákna pod zátěží celé sady): `ProxyWatchdogTests`, `ProxyRendererTests.Feeder_*`, `EnginePauseTests`, `JobPauseTests`, `NtfyClientTests`, nově `TapRenderTests` a `VerifyInProxyJobTests.PausedInStageTwo_*` — při jednom selhání je pusťte znovu samostatně (`--filter "FullyQualifiedName~<třída>"`) a selhání nahlaste. Testy s ffmpeg jsou `[FfmpegFact]` (bez ffmpeg se přeskočí).
- Verze: plán 10 → **0.6.0** (`0.6.0-win`) — nastaví úkol 18 v `windows\Directory.Build.props` a v testech s verzí (`FormatTests`, `ReportTests`, `HtmlReportTests`).
- Každý úkol přidá řádek do `docs/VYVOJ-WINDOWS.md` do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (sekci založí úkol 1 nad sekcí `## 2026-10-08 — Po zkopírování: klikací volby`; nejnovější řádek nahoře, bez SHA).
- Commit: soubory výslovně (`git add <cesty>`, nikdy `-A` ani `.`), zpráva se zapíše nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10` a commituje `git commit -F .git/COMMIT_MSG_PLAN10`; poslední řádek zprávy přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Bloky „Najít / Nahradit“ uvádějí text podle stavu souboru ve chvíli, kdy se blok nahrazuje: výchozí stav = ef25348, pak předchozí úkoly a předchozí bloky téhož úkolu (pořadí bloků = pořadí nahrazování; čísla řádků platí v tu chvíli). Každý blok „Najít“ je v souboru právě jednou. „(nový soubor)“ = soubor se vytvoří celý. Kód plánu byl postavený a otestovaný v kopii repa mimo strom repa (`git worktree` z ef25348): každý úkol se zkompiloval s `-warnaserror`, jeho testy prošly a celá sada byla po plánu zelená. Pracovní kopie má konce řádků CRLF (`core.autocrlf=true`), bloky v plánu LF — nástroj Edit je sjednotí; kdo upravuje skriptem, porovnává bez ohledu na `\r`.

## Review Focus

Vstupy a poruchy, které spec implikuje a které by uživatele kously nejdřív; každá má test v úkolu, který vlastní kód (nejpravděpodobnější nahoře):

1. **Falešné „ověřeno“ z cache** — čtení, které jde z RAM místo z disku, by kopii „ověřilo“ bez důkazu. Zápis jde mimo cache (`NO_BUFFERING | WRITE_THROUGH`), cíl, který `NO_BUFFERING` nedodržuje nebo je síťový, se ověří zpětným čtením, a bajt změněný na disku **po** zápisu (druhým handlem s `NO_BUFFERING`, cache ho nevidí) ověření najde v režimu A, B i při čtení na pozadí. Testy: úkol 5 `UnbufferedCheckTests.BufferedWrites_Classic`, `ProbeCannotBeWritten_Classic_WithTheReason`, `IsRemote_UncYes_LocalNo`; úkol 8 `TapRenderTests.FlippedByteOnTheDisk_TheTapFindsIt`; úkol 9 `CacheWarmTests.Warm_FlippedByteOnTheDisk_Mismatch`; úkol 11 `VerifySessionTests.FlippedByteOnTheSecondary_Failed_WithTheBlock`; úkol 12 `VerifyInProxyJobTests.FlippedByteOnThePrimary_FoundByTheProxyRead_Failed_EjectYesFormatNo`, `CacheCheckFails_ThatDiskReadBack_WithTheReason`. Zápis přímo na zařízení (`\\.\PhysicalDriveN` pod NTFS) je ruční test 53.
2. **Částečné a opakované bloky** — ffmpeg čte blok po kusech, z více spojení najednou, některé dvakrát; poslední blok je kratší. Výsledek musí být stejný jako sekvenční čtení; opakované čtení s jinými daty = chyba I/O, nikdy „ověřeno“; přetečení rozpracovaných bloků jen nechá blok k dočtení. Testy: úkol 2 `BlockVerifierTests.RandomOrder_Pieces_SameAsSequential`, `ShortLastBlock_FedInPieces_IsVerified`, `ConcurrentFeeds_FromSeveralConnections_Verified`, `RepeatedRead_Same_Ignored_Different_MismatchAndUnstable`, `TooManyBlocksInPieces_TheOldestIsDropped_AndStaysMissing`; úkol 13 `ResumeTests.Resume_PartDiffersFromItsCheckpoint_FileFails_NeverVerified`.
3. **Vzory skoků kontejnerů** — MOV s `moov` na konci, MP4 faststart, MXF s patičkou a indexem: tap nesmí číst soubor dvakrát (bez `-short_seek_size` ffmpeg 8.1 četl 200 %), co ffmpeg nepřečte (index, hlavička), dorovná fill-in. Testy: úkol 8 `TapRenderTests.Mov_MoovAtEnd_ReadOnce_FillInVerifiesTheRest`, `Mp4_Faststart_Verified`, `Mxf_FooterAndIndex_Verified`; úkol 2 `BlockVerifierTests.Unread_HeaderAndTail_AreTheMissingRanges`.
4. **Bezpečnost tapu** — jiný proces nebo webová stránka na tomtéž PC nesmí přes server číst jiné soubory: jen `127.0.0.1`, token, id místo cesty (traversal nemá co otevřít), `Host` jen `127.0.0.1:<port>` (DNS rebinding), jen GET/HEAD, limity spojení a hlaviček, žádné čtení disku pro odmítnutý požadavek. Testy: úkol 7 `RangeServerTests.ListensOnLoopbackOnly`, `WrongToken_UnknownId_OrTraversal_404_NothingRead`, `ForeignHost_403`, `OtherMethods_405`, `OversizedHead_ConnectionClosed_ServerStillServes`, `ConnectionsOverTheLimit_Refused_NotQueued`, `UnsatisfiableOrSeveralRanges_416`.
5. **Souhra s pauzou, zrušením, doplněním a opravou** — pauza ve 2. fázi zastaví i čtení ověření; zrušení zastaví proxy i ověření a zapsané kopie zůstanou zapsané, neověřené („Lze vyjmout“, nikdy „Lze formátovat“); doplnění ověřené během proxy zapíše generaci 2; oprava zůstává zpětným čtením a nedokončenou zálohu odmítne; pád → doplnění dokončí. Testy: úkol 12 `VerifyInProxyJobTests.PausedInStageTwo_HoldsThenFinishesVerified`, `CancelledBeforeTheProxies_CommittedButUnverified`, `Append_VerifiedInProxy_CommitsGenerationTwo`, `WithoutProxies_OrSwitchedOff_ReadBackInStageOne`, `RepairJobTests.RepairCancelledDuringVerify_EjectYes_FormatNo_Warning`; úkol 11 `VerifySessionTests.Cancelled_NothingRead_CopiesStayWritten`; úkol 13 `ResumeTests.CrashMidCopy_AppendResumes_CompleteFileNotRead_PartGoesOn`, `PendingVerification_AppendFinishesIt_WithoutTheCard`, `Repair_OfAnUnfinishedBackup_Refused`.
6. **Odpojený disk nebo zmizelý / zkrácený soubor během ověření** — soubor selže jen na tom cíli, ostatní pokračují, karta „Neformátovat“, report řekne proč; zkrácený soubor je nesoulad i se souhlasnými bloky. Testy: úkol 12 `VerifyInProxyJobTests.FileGoneOnTheSecondary_FailedThere_FormatNo`; úkol 9 `CacheWarmTests.Warm_TruncatedFile_SizeMismatch`; úkol 2 `BlockVerifierTests.OtherLengthOnDisk_IsAMismatch_EvenWithMatchingBlocks`; úkol 7 `RangeServerTests.ReadFailsMidBody_ConnectionCut_Counted`; úkol 4 `ManifestRecordingTests.FailedOnADestination_NoRecordThere`.
7. **Stejný blok špatně na více cílech** — podezření na zdroj: karta se přečte znovu (jen ta samá karta) a poznámka řekne, zda vada vznikla při zápisu, při čtení karty, nebo je karta vadná; vytažená karta = poznámka „vložte ji a spusťte Zkopírovat znovu“. Testy: úkol 11 `VerifySessionTests.SameBlockOnBothDisks_TheCardIsReadAgain`, `CardGone_TheNoteSaysSo`; úkol 12 `VerifyInProxyJobTests.SameBlockWrongOnBothDisks_TheCardIsAsked_NoteInTheReport`.
8. **Prohozená písmena disků (R12)** — disk pod jiným písmenem nikdy nevezme sloupec jiného disku a denní report nezezelená. Test: úkol 14 `ReadBackSerialTests.ReadBack_BySerial_LettersSwapped_NeverAnotherDisksColumn`, `DayReport_PendingCard_SaysVerifying_NotGreen`.

## Ověřeno před plánem (2026-10-08, ffmpeg 8.1, .NET 10.0.12, Windows 11)

Měřeno zkušebním programem v kopii repa mimo strom (nejde do repa); stejné vlastnosti hlídají testy plánu (`TapRenderTests`, `UnbufferedCheckTests`, `CacheWarmTests`, `Xxh128Tests`). Média: `big_prores.mov` 735 MB ProRes s `moov` na konci, `prores_moovend.mov`, `h264_faststart.mp4` (`-movflags +faststart`), `sony.mxf` (80 MB XDCAM), vyrobené `ffmpeg -f lavfi -i testsrc2…`.

**ffmpeg přes HTTP s rozsahy (R3).** Server `TcpListener(IPAddress.Loopback, 0)`, odpověď `206` s `Content-Range`, soubor čtený bez cache; před každým během `drop` (otevřít a zavřít handle s `NO_BUFFERING`):

```powershell
proto drop big_prores.mov; proto serve big_prores.mov ffmpeg -hide_banner -loglevel error -nostdin -i "{in}" -map 0:v:0 -map "0:a?" -f null -
proto drop big_prores.mov; proto direct ffmpeg -hide_banner -loglevel error -nostdin -i big_prores.mov -map 0:v:0 -map "0:a?" -f null -
proto serve h264_faststart.mp4 ffmpeg -hide_banner -loglevel error -nostdin -short_seek_size 65536 -i "{in}" -map 0 -c copy -f null -
```

- Požadavky: MOV s `moov` na konci = 3 (`bytes=0-`, `moov` na konci, `mdat` od bajtu 36); MP4 faststart = 1 sekvenční; MXF = 5 (`0-`, poslední 4 bajty, `0-`, patička, tělo od 9216). Seek funguje přes `Range`.
- Přečteno z disku bez `-short_seek_size`: 200 % souboru (ffmpeg přeskočil `mdat` čtením přes HTTP); s `-short_seek_size 65536` ~100 %. S bloky 16 MiB a pamětí 4 bloků v tapu: velký MOV 101,8 %, ProRes 101,1 %, MP4 100 %, MXF 116,7 % (80 MB soubor, ~1 blok navíc na každé zahozené spojení; s výsečemi 256 KiB a odesílacím bufferem 64 KiB — s 1 MiB / výchozím bufferem bylo 220 %); ověřeno tapem 100 % bloků, dorovnání 0 s.
- Rychlost (NVMe, studená cache): dekódování přes tap 1,63 s proti 1,55 s přímo (+5 %); proxy (enkódování) 2,34 s proti 2,27 s (+3 %); kopie streamu 0,41 s proti 0,37 s; ~1,7–1,9 GB/s přes server.

**`NO_BUFFERING | WRITE_THROUGH` z .NET (R8, spec 8).** `File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None, (FileOptions)0x20000000 | FileOptions.WriteThrough, prealloc)`, buffery `NativeMemory.AlignedAlloc` 4 KiB:

```powershell
proto writespeed D:\p10 1024; proto writespeed E:\p10 1024   # zápis s/bez WRITE_THROUGH
proto nobuf D:\p10                                           # zarovnání, poslední blok, odmítnutí
proto readspeed D:\p10ile.bin; proto evict D:\p10 512      # odkud jde čtení po zápisu a po vyhození
```

- `WRITE_THROUGH` nezpomalí: NVMe 3527 MB/s s ním proti 1504 MB/s bez něj (bez něj se čeká na flush), USB HDD 57 proti 56, SATA stejně.
- Poslední blok zapsaný zarovnaný (výplň) a pak `RandomAccess.SetLength` (= `SetFileInformationByHandle` EndOfFile) ořízne přesně.
- Nezarovnaný offset čtení/zápisu s `NO_BUFFERING` svazek odmítne chybou 87 (`ERROR_INVALID_PARAMETER`) na NTFS i exFAT; nezarovnaný **buffer** odmítne NTFS, exFAT ho přijme → kontrola cache (úkol 5) zkouší nezarovnaný **offset**.
- Po zápisu bez cache jde běžné (buffered) čtení z disku (USB HDD 64 MB/s, z cache by bylo ~6 GB/s). Otevření a zavření handlu s `NO_BUFFERING` soubor vyhodí z cache (NTFS HDD 81 MB/s, exFAT 57 MB/s, NVMe 2527 MB/s proti ~5,7 GB/s z cache) — režim B (úkol 9).
- Bajt převrácený druhým handlem s `NO_BUFFERING` najde čtení bez cache i běžné čtení (NTFS drží koherenci cache) → test cache ze spec 11 se dá automatizovat na úrovni souboru (úkoly 8, 9, 11, 12); zápis přímo na zařízení (obchází i souborový systém) vyžaduje správce → ruční test 53.

**XxHash128 / XxHash3.** Ve sdíleném frameworku `Microsoft.NETCore.App` 10.0.12 (ani v ref packu) `System.IO.Hashing.dll` **není**; je v NuGet `System.IO.Hashing` 10.0.12, který `Offload.Core.csproj` už odkazuje (`HashAlgo` z něj bere `XxHash64`) → žádný nový NuGet. Hex výstup C# (`XxHash128.GetCurrentHash`, `XxHash3.HashToUInt64`, `XxHash64`) se shoduje s Python `xxhash` (`xxh3_128`, `xxh3_64`, `xxh64`) na 0 B, 1 B, 16 MiB a 16 MiB + 1 (`proto hashcmp`).

## Rozhodnutí tam, kde spec nestačil (platí pro oba soubory plánu)

- **`<process>` v ASC MHL (spec 9):** schéma ASC MHL v2 dovoluje v `<process>` jen `in-place` / `transfer` / `flatten` — zůstává `transfer` a metoda jde do `<creatorinfo><comment>` (`· ověření: verify-in-proxy` / `· ověření: read-back`) a do reportu `destinations[].verifyMethod`.
- **`failed` v MHL:** spec 9 „`failed` se nezapíše“ proti pravidlu plánu 6 (ruling V1: selhání je v MHL s `action="failed"`, „Ověřit zálohu“ ho hlásí). Platí plán 6 — MHL zůstává čitelný starším Offloadem a Macem; nově se do ověřené generace nezapíše stav `written` (jen ověřené a selhané soubory).
- **Kontrola čtení bez cache (spec 8):** místo měření rychlosti (NVMe 2,5 GB/s z disku proti 5,7 GB/s z RAM — příliš blízko pro spolehlivý práh) se zkouší přesná vlastnost: svazek, který `NO_BUFFERING` dodržuje, odmítne nezarovnané čtení. Sonda 1 MiB v `_REPORT` cíle, pak se smaže.
- **Režim B s okny po 4 GiB (spec 5):** ne v v1 — Offload nezná tempo SDK. Klip do poloviny volné RAM se ohřeje celý; větší se ověří dočtením po proxy (čte se pak dvakrát, report to neskrývá).
- **Proxy fronta až po celé kopii (spec 4 „proxy začne hned po `copied`“, „video nejdřív“):** 2. fáze začíná po kopii jako dosud — karta jde vyjmout hned po kopii a čtení ffmpeg z primárního disku by během kopie soupeřilo se zápisem na týž disk. Pořadí souborů zůstává podle karty. Úspora spec 1 (odpadá samostatné čtení primárního cíle) platí celá.
- **„Uživatel zruší proxy“ (spec 10):** Offload nemá zvlášť „zrušit proxy“; Zrušit zastaví proxy i ověření, generace se zapíše se zapsanými, neověřenými kopiemi („Lze vyjmout“, „Neformátovat“) a ověření dokončí doplnění / „Zkopírovat znovu“.
- **Soubory bez proxy „v pauzách mezi proxy“ (spec 6):** vlákno na pozadí pro primární svazek čte jen tehdy, když tap nečte (`tapIdle`); „fyzický disk“ = svazek (jedno vlákno na svazek).
- **Nesoulad (spec 10) a R7:** nikdy se nepřepisuje — blok se přečte znovu bez cache (souhlas = poznámka „nestabilní čtení“, soubor ověřen), jinak `failed` a oprava.
- **Potvrzení `content_warn` (spec 7):** Offload kartu neformátuje; potvrzení je tlačítko na kartě úlohy, které odemkne zelenou „Lze formátovat“ (a náhled místa vady jako JPEG blízko vadného snímku).
- **Řádek souboru se třemi tečkami (spec 9):** karta úlohy nemá seznam všech souborů (jen problémové); stav každého souboru a cíle (kopie, ověření, obsah) je v HTML/PDF reportu. Pruh proxy a ověření primárního cíle = fáze Proxy, po ní „Dokončení ověření“ s ověřenými bajty.
- **Tap URL (R3 `…/<token>/<cesta>`):** místo cesty náhodné id souboru a jméno jen jako přípona pro demuxer — URL nenese cestu, takže traversal nemá co otevřít.
- **Pád aplikace (spec 10):** „při startu se načte manifest“ = při vložení karty nabídka „Dokončit přerušenou zálohu“ (doplnění, R6), historie má záznam „nedokončeno“ od startu kopie.
- **Nic nového / nic k opravě:** oba verdikty = bez varování (konzervativně jako dosud).
- **Denní report:** zelený text „ověřeno“ zůstává; karta, jejíž ověření neskončilo, je „OVĚŘUJE SE“ a mezi problémy.
- **Test plánu 9 `StageTwoTests.ProxyCopyFails_RenderedProxyKept`** blokuje kopii proxy na druhém cíli; proxy se teď renderuje na primárním (nejrychlejším) cíli — test si primární zvolí (`PrimaryDestination = 0`), jinak by výsledek závisel na tom, který disk byl rychlejší (úkol 12).

---

### Task 1: XXH3-128 a blokové hashe při čtení karty (R2)

Každý soubor dostane vedle xxh64 i XXH3-128 (do reportu `files[].xxh128` a do ASC MHL jako `<xxh128>` vedle
`<xxh64>`, R2) a hasher xxh64 si při čtení karty zapamatuje XXH3-64 každého bloku (blok = chunk kopie, 16 MiB) — z nich
staví blokový manifest (úkoly 3–4) a ověřovač (úkol 2). `.xxh64` ani `hashAlgorithms` se nemění, takže Mac a starší
Offload zálohu ověří dál. `System.IO.Hashing` 10.0.12 (XxHash128, XxHash3) projekt už odkazuje — žádný nový NuGet.
Testy, které čekaly v MHL jen xxh64 (a md5), teď čekají i xxh128.

**Files:**
- Modify: `windows/src/Offload.Core/Hashing.cs:44-46`
- Modify: `windows/src/Offload.Core/SourceHasher.cs:1, 8-15, 24-35, 49-52, 62-73, 80-107`
- Modify: `windows/src/Offload.Core/OffloadReport.cs:27-28, 121-122`
- Modify: `windows/src/Offload.Core/Mhl.cs:89`
- Modify: `windows/src/Offload.Core/OffloadEngine.Run.cs:37, 178-179`
- Modify: `windows/src/Offload.Core/Append/RepairPlanner.cs:193`
- Modify: `docs/VYVOJ-WINDOWS.md:25`
- Test: `windows/tests/Offload.Core.Tests/Xxh128Tests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/BackupVerifierTests.cs:32-33`
- Test: `windows/tests/Offload.Core.Tests/EngineTests.cs:198, 214, 224`
- Test: `windows/tests/Offload.Core.Tests/AppendEngineTests.cs:75, 622, 670-671`
- Test: `windows/tests/Offload.Core.Tests/RepairEngineTests.cs:67-68`

**Interfaces:**
- Consumes: `System.IO.Hashing.XxHash128`, `XxHash3` (balíček už v `Offload.Core.csproj`).
- Produces:
  - `FileHashes(ulong? Xxh64, byte[]? Md5, string? Xxh128 = null)` (record struct, 3. parametr nový, výchozí `null`).
  - `SourceHasher(HashKind kind, int fileCount, Func<string, IFileHash>? create = null, int blockSize = 0)`; `string?[] Xxh128`, `ulong[]?[] Blocks` (po `Finish(i)`); `SourceHasher.For(kinds, fileCount, create = null, blockSize = 0)`.
  - `ReportFile.Xxh128` (`"xxh128"`), MHL `<xxh128 action=… hashdate=…>` hned za `<xxh64>`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/Xxh128Tests.cs` (nový soubor)

```csharp
using System.IO.Hashing;
using System.Xml.Linq;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Plan 10, R2: every copy hashes XXH3-128 next to xxh64 — into the report JSON and the ASC MHL — and keeps the XXH3-64
/// of every block for the block manifest; xxh64 and its hash list stay for older Offload and the Mac.
public class Xxh128Tests
{
    const int Chunk = 64 * 1024;

    static OffloadOptions Options() => new() { ChunkSize = Chunk, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    static string Hex128(byte[] data)
    {
        var h = new XxHash128();
        h.Append(data);
        return Convert.ToHexStringLower(h.GetCurrentHash());
    }

    [Fact]
    public void Copy_RecordsXxh128_InReportAndMhl_NextToXxh64()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var a = TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 3 * Chunk + 5, 1);
        var b = TestFs.WriteRandom(Path.Combine(card, "b.wav"), 0, 2);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");

        var report = engine.Run(plan);

        Assert.True(report.Success && report.Verified);
        var fa = report.Files.Single(f => f.Path == "CLIP/a.mov");
        var fb = report.Files.Single(f => f.Path == "b.wav");
        Assert.Equal(Hex128(a), fa.Xxh128);
        Assert.Equal(Hex128(b), fb.Xxh128);
        Assert.Equal(Format.Hex64(XxHash64.HashToUInt64(a)), fa.Xxh64);
        Assert.Contains("\"xxh128\"", File.ReadAllText(Path.Combine(plan.CardPaths[0], "_REPORT", "offload_report.json")));

        var mhl = MhlReader.Read(plan.CardPaths[0])!;
        var rec = mhl.Files["CLIP/a.mov"];
        Assert.Equal(["xxh64", "xxh128"], rec.Hashes.Select(h => h.Algorithm));
        Assert.Equal(Hex128(a), rec.Hashes.Single(h => h.Algorithm == "xxh128").Value);
        Assert.All(rec.Hashes, h => Assert.Equal("original", h.Action));
        // The hash list stays xxh64 only (older Offload, the Mac).
        Assert.DoesNotContain(Hex128(a), File.ReadAllText(Path.Combine(plan.CardPaths[0], "_REPORT", "A001.xxh64")));
        // "Ověřit zálohu" checks both hashes of the record.
        var verify = BackupVerifier.Verify(plan.CardPaths[0], chunkSize: 4096);
        Assert.True(verify.Success);
        Assert.Equal(["xxh64", "xxh128"], verify.HashAlgorithms);
    }

    [Fact]
    public void Mhl_Xxh128Element_IsInTheAscNamespace_AfterXxh64()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 1000, 3);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");
        engine.Run(plan);

        var file = Directory.GetFiles(Path.Combine(plan.CardPaths[0], "ascmhl"), "0001_*.mhl").Single();
        XNamespace ns = "urn:ASC:MHL:v2.0";
        var hash = XDocument.Load(file).Descendants(ns + "hash").Single();
        Assert.Equal(["path", "xxh64", "xxh128"], hash.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void SourceHasher_KeepsOneXxh3PerBlock_AndTheWholeFileXxh128()
    {
        var data = new byte[3 * Chunk + 7];
        new Random(4).NextBytes(data);
        using var pool = new BufferPool(4, Chunk);
        using var h = new SourceHasher(HashKind.Xxh64, 1, blockSize: Chunk);
        h.Begin();
        for (int off = 0; off < data.Length; off += Chunk)
        {
            int n = Math.Min(Chunk, data.Length - off);
            var buf = pool.Take();
            data.AsSpan(off, n).CopyTo(buf.Span);
            h.Update(new Chunk(buf, n, off, 1, pool));
        }
        h.Finish(0);
        h.Wait();

        var want = Enumerable.Range(0, 4).Select(i => XxHash3.HashToUInt64(data.AsSpan(i * Chunk, Math.Min(Chunk, data.Length - i * Chunk)))).ToArray();
        Assert.Equal(want, h.Blocks[0]);
        Assert.Equal(Hex128(data), h.Xxh128[0]);
        Assert.Equal(Format.Hex64(XxHash64.HashToUInt64(data)), h.Values[0]);
    }

    [Fact]
    public void SourceHasher_ABlockOutOfOrder_FailsTheFile_NeverAWrongList()
    {
        using var pool = new BufferPool(2, Chunk);
        using var h = new SourceHasher(HashKind.Xxh64, 1, blockSize: Chunk);
        h.Begin();
        var buf = pool.Take();
        h.Update(new Chunk(buf, Chunk, Chunk, 1, pool));   // block 1 before block 0
        h.Finish(0);
        h.Wait();

        Assert.Null(h.Blocks[0]);
        Assert.Null(h.Xxh128[0]);
        Assert.Null(h.Values[0]);
        Assert.Contains("out of order", h.Errors[0]);
    }
}
```

#### `windows/tests/Offload.Core.Tests/BackupVerifierTests.cs`

Najít (1/1, řádky 32–33):

```csharp
        Assert.Equal(["xxh64"], r.HashAlgorithms);
        Assert.Equal((3, 3, 151_000L, 151_000L), (r.Ok, r.TotalFiles, r.TotalBytes, r.BytesRead));
```

Nahradit:

```csharp
        Assert.Equal(["xxh64", "xxh128"], r.HashAlgorithms);   // plan 10 (R2): every record has xxh128 too — one read for both
        Assert.Equal((3, 3, 151_000L, 151_000L), (r.Ok, r.TotalFiles, r.TotalBytes, r.BytesRead));
```

#### `windows/tests/Offload.Core.Tests/EngineTests.cs`

Najít (1/3, řádek 198):

```csharp
        Assert.All(mhl.Files.Values, r => Assert.Equal(["xxh64", "md5"], r.Hashes.Select(h => h.Algorithm)));
```

Nahradit:

```csharp
        Assert.All(mhl.Files.Values, r => Assert.Equal(["xxh64", "xxh128", "md5"], r.Hashes.Select(h => h.Algorithm)));
```

Najít (2/3, řádek 214):

```csharp
        Assert.Equal(["xxh64", "md5"], verify.HashAlgorithms);
```

Nahradit:

```csharp
        Assert.Equal(["xxh64", "md5", "xxh128"], verify.HashAlgorithms);
```

Najít (3/3, řádek 224):

```csharp
        Assert.All(Verification.MhlReader.Read(plainPlan.CardPaths[0])!.Files.Values, r => Assert.Equal(["xxh64"], r.Hashes.Select(h => h.Algorithm)));
```

Nahradit:

```csharp
        Assert.All(Verification.MhlReader.Read(plainPlan.CardPaths[0])!.Files.Values, r => Assert.Equal(["xxh64", "xxh128"], r.Hashes.Select(h => h.Algorithm)));
```

#### `windows/tests/Offload.Core.Tests/AppendEngineTests.cs`

Najít (1/3, řádek 75):

```csharp
            Assert.Equal("original", Assert.Single(mhl.Generations[1].Files[0].Hashes).Action);
```

Nahradit:

```csharp
            Assert.All(mhl.Generations[1].Files[0].Hashes, h => Assert.Equal("original", h.Action));
            Assert.Equal(["xxh64", "xxh128"], mhl.Generations[1].Files[0].Hashes.Select(h => h.Algorithm));
```

Najít (2/3, řádek 622):

```csharp
        Assert.Equal(["xxh64", "md5"], gen2.Files.Single().Hashes.Select(h => h.Algorithm));
```

Nahradit:

```csharp
        Assert.Equal(["xxh64", "xxh128", "md5"], gen2.Files.Single().Hashes.Select(h => h.Algorithm));
```

Najít (3/3, řádky 670–671):

```csharp
            Assert.Equal(3, verify.Ok);
            Assert.Equal(["xxh64", "md5"], verify.HashAlgorithms);
```

Nahradit:

```csharp
            Assert.Equal(3, verify.Ok);
            Assert.Equal(["xxh64", "md5", "xxh128"], verify.HashAlgorithms);
```

#### `windows/tests/Offload.Core.Tests/RepairEngineTests.cs`

Najít (1/1, řádky 67–68):

```csharp
            Assert.Equal(d == 0 ? "verified" : "original", Assert.Single(gen2.Hashes).Action);
            Assert.Equal(f.Xxh64, gen2.Hashes[0].Value);
```

Nahradit:

```csharp
            // Plan 10 (R2): xxh64 and xxh128, both with the same action.
            Assert.Equal(["xxh64", "xxh128"], gen2.Hashes.Select(h => h.Algorithm));
            Assert.All(gen2.Hashes, h => Assert.Equal(d == 0 ? "verified" : "original", h.Action));
            Assert.Equal(f.Xxh64, gen2.Hashes[0].Value);
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~Xxh128Tests|FullyQualifiedName~BackupVerifierTests|FullyQualifiedName~EngineTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~RepairEngineTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS1061: 'ReportFile' does not contain a definition for 'Xxh128' and no accessible extension method 'Xxh128' accepting a first argument of type 'ReportFile' could be found (are you missing a using directive or an assembly reference?)`; `CS1739: The best overload for 'SourceHasher' does not have a parameter named 'blockSize'`; `CS1061: 'SourceHasher' does not contain a definition for 'Blocks' and no accessible extension method 'Blocks' accepting a first argument of type 'SourceHasher' could be found (are you missing a using directive or an assembly reference?)` a další chyby téhož druhu (všechny jen v testech tohoto úkolu).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/Hashing.cs`

Najít (1/1, řádky 44–46):

```csharp
/// The checksums of one file; null = not computed (the file failed, or MD5 was not asked for).
public readonly record struct FileHashes(ulong? Xxh64, byte[]? Md5)
{
```

Nahradit:

```csharp
/// The checksums of one file; null = not computed (the file failed, or MD5 was not asked for). `Xxh128` (plan 10, R2):
/// XXH3-128 as ASC MHL writes it (32 lower-case hex digits) — every copy computes it next to xxh64; null in older reports.
public readonly record struct FileHashes(ulong? Xxh64, byte[]? Md5, string? Xxh128 = null)
{
```

#### `windows/src/Offload.Core/SourceHasher.cs`

Najít (1/6, řádek 1):

```csharp
using Offload.Core.Verification;
```

Nahradit:

```csharp
using System.IO.Hashing;
using Offload.Core.Verification;
```

Najít (2/6, řádky 8–15):

```csharp
/// when it falls a whole pool behind.
internal sealed class SourceHasher : IDisposable
{
    readonly SerialWorker worker;
    readonly string algorithm;
    readonly Func<string, IFileHash> create;
    IFileHash? stream;
    string? error;
```

Nahradit:

```csharp
/// when it falls a whole pool behind. Plan 10 (R2, spec 4.2): the xxh64 hasher also keeps XXH3-128 of the whole file
/// and XXH3-64 of every block (`blockSize` bytes, the last one shorter) — the block manifest and the MHL's xxh128.
internal sealed class SourceHasher : IDisposable
{
    readonly SerialWorker worker;
    readonly string algorithm;
    readonly Func<string, IFileHash> create;
    readonly int blockSize;
    IFileHash? stream;
    XxHash128? whole;
    List<ulong>? blocks;
    string? error;
```

Najít (3/6, řádky 24–35):

```csharp
    /// Per file: why this hash could not be computed (the hash itself threw), else null.
    public string?[] Errors { get; }

    public SourceHasher(HashKind kind, int fileCount, Func<string, IFileHash>? create = null)
    {
        Kind = kind;
        this.create = create ?? HashAlgo.Create;
        algorithm = kind == HashKind.Md5 ? "md5" : "xxh64";
        worker = new SerialWorker("offload.hash." + algorithm);
        Values = new string?[fileCount];
        Errors = new string?[fileCount];
    }
```

Nahradit:

```csharp
    /// Per file: why this hash could not be computed (the hash itself threw), else null.
    public string?[] Errors { get; }
    /// Per file, with a block size: XXH3-128 of the whole file (lower-case hex), null when it was not hashed to the end.
    public string?[] Xxh128 { get; }
    /// Per file, with a block size: XXH3-64 of every block in order, null when it was not hashed to the end.
    public ulong[]?[] Blocks { get; }

    /// `blockSize` > 0: also the block hashes and XXH3-128 (the engine gives its chunk size: one block per chunk).
    public SourceHasher(HashKind kind, int fileCount, Func<string, IFileHash>? create = null, int blockSize = 0)
    {
        Kind = kind;
        this.create = create ?? HashAlgo.Create;
        this.blockSize = blockSize;
        algorithm = kind == HashKind.Md5 ? "md5" : "xxh64";
        worker = new SerialWorker("offload.hash." + algorithm);
        Values = new string?[fileCount];
        Errors = new string?[fileCount];
        Xxh128 = new string?[fileCount];
        Blocks = new ulong[]?[fileCount];
    }
```

Najít (4/6, řádky 49–52):

```csharp
    /// One hasher per kind in `kinds` (xxh64 always first).
    public static List<SourceHasher> For(HashKind kinds, int fileCount, Func<string, IFileHash>? create = null)
    {
        var all = new List<SourceHasher> { new(HashKind.Xxh64, fileCount, create) };
```

Nahradit:

```csharp
    /// One hasher per kind in `kinds` (xxh64 always first; it also keeps the blocks and XXH3-128 when `blockSize` > 0).
    public static List<SourceHasher> For(HashKind kinds, int fileCount, Func<string, IFileHash>? create = null, int blockSize = 0)
    {
        var all = new List<SourceHasher> { new(HashKind.Xxh64, fileCount, create, blockSize) };
```

Najít (5/6, řádky 62–73):

```csharp
        return Enumerable.Range(0, fileCount).Select(i => new FileHashes(
            xx?.Values[i] is string x ? Convert.ToUInt64(x, 16) : null,
            md5?.Values[i] is string m ? Convert.FromHexString(m) : null)).ToArray();
    }

    public void Begin() => worker.Post(() =>
    {
        error = null;
        stream = null;
        try { stream = create(algorithm); }
        catch (Exception e) { error = e.Message; }
    });
```

Nahradit:

```csharp
        return Enumerable.Range(0, fileCount).Select(i => new FileHashes(
            xx?.Values[i] is string x ? Convert.ToUInt64(x, 16) : null,
            md5?.Values[i] is string m ? Convert.FromHexString(m) : null,
            xx?.Xxh128[i])).ToArray();
    }

    public void Begin() => worker.Post(() =>
    {
        error = null;
        stream = null;
        whole = blockSize > 0 ? new XxHash128() : null;
        blocks = blockSize > 0 ? [] : null;
        try { stream = create(algorithm); }
        catch (Exception e) { error = e.Message; }
    });
```

Najít (6/6, řádky 80–107):

```csharp
    public void Update(Chunk c) => worker.Post(() =>
    {
        try { stream?.Append(c.Buffer.Span[..c.Length]); }
        catch (Exception e)
        {
            error ??= e.Message;
            stream = null;
        }
        finally { c.Release(); }
    });

    public void Finish(int index) => worker.Post(() =>
    {
        try
        {
            if (error == null) Values[index] = stream?.Finish();
        }
        catch (Exception e) { error = e.Message; }
        Errors[index] = error;
        stream = null;
        error = null;
    });

    public void Abort() => worker.Post(() =>
    {
        stream = null;
        error = null;
    });
```

Nahradit:

```csharp
    public void Update(Chunk c) => worker.Post(() =>
    {
        try
        {
            var data = c.Buffer.Span[..c.Length];
            stream?.Append(data);
            if (blocks != null && error == null)
            {
                // One chunk is one block: a chunk that is not the next block would make every later block hash wrong.
                if (c.Offset != (long)blocks.Count * blockSize) throw new InvalidOperationException($"block at {c.Offset} out of order");
                blocks.Add(XxHash3.HashToUInt64(data));
                whole!.Append(data);
            }
        }
        catch (Exception e)
        {
            error ??= e.Message;
            stream = null;
        }
        finally { c.Release(); }
    });

    public void Finish(int index) => worker.Post(() =>
    {
        try
        {
            if (error == null)
            {
                Values[index] = stream?.Finish();
                if (blocks != null)
                {
                    Xxh128[index] = Convert.ToHexStringLower(whole!.GetCurrentHash());
                    Blocks[index] = [.. blocks];
                }
            }
        }
        catch (Exception e) { error = e.Message; }
        Errors[index] = error;
        stream = null;
        whole = null;
        blocks = null;
        error = null;
    });

    public void Abort() => worker.Post(() =>
    {
        stream = null;
        whole = null;
        blocks = null;
        error = null;
    });
```

#### `windows/src/Offload.Core/OffloadReport.cs`

Najít (1/2, řádky 27–28):

```csharp
    /// Only when the copy computed MD5 too (spec 3.5): 32 lower-case hex digits.
    [JsonPropertyName("md5")] public string? Md5 { get; init; }
```

Nahradit:

```csharp
    /// Only when the copy computed MD5 too (spec 3.5): 32 lower-case hex digits.
    [JsonPropertyName("md5")] public string? Md5 { get; init; }
    /// XXH3-128 of the card's file (plan 10, R2: into the ASC MHL next to xxh64), 32 lower-case hex digits; absent in
    /// older reports and for a file the card could not give. The Mac ignores it.
    [JsonPropertyName("xxh128")] public string? Xxh128 { get; init; }
```

Najít (2/2, řádky 121–122):

```csharp
            Xxh64 = hashes[i].Xxh64Hex,
            Md5 = hashes[i].Md5Hex,
```

Nahradit:

```csharp
            Xxh64 = hashes[i].Xxh64Hex,
            Md5 = hashes[i].Md5Hex,
            Xxh128 = hashes[i].Xxh128,
```

#### `windows/src/Offload.Core/Mhl.cs`

Najít (1/1, řádek 89):

```csharp
            Line($"      <xxh64 action=\"{action}\" hashdate=\"{stamp}\">{f.Xxh64}</xxh64>");
```

Nahradit:

```csharp
            Line($"      <xxh64 action=\"{action}\" hashdate=\"{stamp}\">{f.Xxh64}</xxh64>");
            if (f.Xxh128 != null) Line($"      <xxh128 action=\"{action}\" hashdate=\"{stamp}\">{f.Xxh128}</xxh128>");   // plan 10, R2
```

#### `windows/src/Offload.Core/OffloadEngine.Run.cs`

Najít (1/2, řádek 37):

```csharp
            hashers = SourceHasher.For(plan.Hashes, files.Count, Options.HashFactory);   // xxh64, and MD5 on its own thread (spec 3.5)
```

Nahradit:

```csharp
            // xxh64 (with XXH3-128 and the block hashes, plan 10), and MD5 on its own thread (spec 3.5).
            hashers = SourceHasher.For(plan.Hashes, files.Count, Options.HashFactory, Options.ChunkSize);
```

Najít (2/2, řádky 178–179):

```csharp
            recorded[i] = expected.Xxh64 == null ? hashes[i]
                : new FileHashes(expected.Xxh64, expected.Md5 ?? (hashes[i].Xxh64 == expected.Xxh64 ? hashes[i].Md5 : null));
```

Nahradit:

```csharp
            bool same = hashes[i].Xxh64 == expected.Xxh64;
            recorded[i] = expected.Xxh64 == null ? hashes[i]
                : new FileHashes(expected.Xxh64, expected.Md5 ?? (same ? hashes[i].Md5 : null), expected.Xxh128 ?? (same ? hashes[i].Xxh128 : null));
```

#### `windows/src/Offload.Core/Append/RepairPlanner.cs`

Najít (1/1, řádek 193):

```csharp
        return f == null ? new FileHashes(null, null) : new FileHashes(Convert.ToUInt64(f.Xxh64, 16), f.Md5 is string m ? Convert.FromHexString(m) : null);
```

Nahradit:

```csharp
        return f == null ? new FileHashes(null, null)
            : new FileHashes(Convert.ToUInt64(f.Xxh64, 16), f.Md5 is string m ? Convert.FromHexString(m) : null, f.Xxh128);
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~Xxh128Tests|FullyQualifiedName~BackupVerifierTests|FullyQualifiedName~EngineTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~RepairEngineTests"`

Expected: PASS — `Failed: 0, Passed: 77, Skipped: 0, Total: 77` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1498` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Úkol 1 zakládá sekci plánu 10 nad sekcí `## 2026-10-08 — Po zkopírování: klikací volby`; další úkoly přidávají řádek nahoru do ní (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádek 25):

```markdown
## 2026-10-08 — Po zkopírování: klikací volby
```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 1: XXH3-128 ke každému souboru (report `files[].xxh128`, ASC MHL `<xxh128>` vedle `<xxh64>`, R2) a XXH3-64 každého bloku při čtení karty; `.xxh64` a `hashAlgorithms` beze změny.

## 2026-10-08 — Po zkopírování: klikací volby
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: XXH3-128 and per-block XXH3-64 while reading the card (plan 10, task 1)

The xxh64 hasher also keeps XXH3-128 of each file and XXH3-64 of every 16 MiB block while the
card is read. The report gets files[].xxh128 and the ASC MHL an <xxh128> next to <xxh64>; the
.xxh64 list and hashAlgorithms do not change, so the Mac and older builds still verify.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Append\RepairPlanner.cs windows\src\Offload.Core\Hashing.cs windows\src\Offload.Core\Mhl.cs windows\src\Offload.Core\OffloadEngine.Run.cs windows\src\Offload.Core\OffloadReport.cs windows\src\Offload.Core\SourceHasher.cs windows\tests\Offload.Core.Tests\AppendEngineTests.cs windows\tests\Offload.Core.Tests\BackupVerifierTests.cs windows\tests\Offload.Core.Tests\EngineTests.cs windows\tests\Offload.Core.Tests\RepairEngineTests.cs windows\tests\Offload.Core.Tests\Xxh128Tests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 2: BlockVerifier — ověření po blocích v libovolném pořadí (spec 5)

Ověřovač jednoho souboru na jednom cíli podle rozhraní ze spec 5: `Feed(offset, data)` přijme cokoli, co se
z cíle přečetlo (celé bloky, kusy, opakovaně, v libovolném pořadí, i souběžně z více spojení tapu), `MissingRanges()`
řekne, co dočíst, `Finish()` dá `Verified`, `Mismatch(blok)` nebo `Incomplete`. `ObserveLength` hlídá délku souboru na
disku (zkrácený soubor, spec 11). Kusy bloku se skládají podle offsetu, nejvýš `MaxPartials` bloků najednou.

**Files:**
- Create: `windows/src/Offload.Core/VerifyInProxy/BlockVerifier.cs`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/BlockVerifierTests.cs` (nový)

**Interfaces:**
- Consumes: —
- Produces:
  - `Offload.Core.VerifyInProxy.BlockManifest(long size, int blockSize, IReadOnlyList<ulong> blocks)`: `Count`, `Offset(b)`, `Length(b)`, `static CountFor(size, blockSize)`.
  - `BlockOutcomeKind { Verified, Mismatch, Incomplete }`, `BlockOutcome(Kind, int Block = -1, long? SizeOnDisk = null)`, `ByteRange(long Offset, long Length)`.
  - `BlockVerifier(BlockManifest)`: `Feed(long, ReadOnlySpan<byte>)`, `ObserveLength(long)`, `LengthObserved`, `MissingRanges()`, `Finish()`, `VerifiedBytes`, `FedBytes`, `UnstableBlocks`, `DiskHash(int)`, `const MaxPartials = 8`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/BlockVerifierTests.cs` (nový soubor)

```csharp
using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 5 and 11: the block verifier — any order, pieces, seeks, repeats; a flipped byte, a short or long file and a
/// missing block are never "verified".
public class BlockVerifierTests
{
    const int Block = 4096;

    static (byte[] Data, BlockManifest Manifest) File(long size, int seed = 1)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        var blocks = Enumerable.Range(0, BlockManifest.CountFor(size, Block))
            .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, (int)Math.Min(Block, size - (long)b * Block)))).ToList();
        return (data, new BlockManifest(size, Block, blocks));
    }

    static void FeedBlock(BlockVerifier v, byte[] data, int b) =>
        v.Feed((long)b * Block, data.AsSpan(b * Block, (int)Math.Min(Block, data.Length - (long)b * Block)));

    [Fact]
    public void Sequential_WholeBlocks_Verified()
    {
        var (data, m) = File(3 * Block + 100);
        var v = new BlockVerifier(m);
        for (int b = 0; b < m.Count; b++) FeedBlock(v, data, b);

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Empty(v.MissingRanges());
        Assert.Equal(data.Length, v.VerifiedBytes);
    }

    // Spec 11: a random order of reads gives the same result as a sequential one.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomOrder_Pieces_SameAsSequential(int seed)
    {
        var (data, m) = File(5 * Block + 1234, seed);
        var v = new BlockVerifier(m);
        var rnd = new Random(seed);
        // Pieces of random length at random offsets, overlapping, in random order, until every byte was given.
        var cuts = new List<(int Off, int Len)>();
        for (int off = 0; off < data.Length;)
        {
            int len = Math.Min(rnd.Next(1, Block + 700), data.Length - off);
            cuts.Add((off, len));
            off += len;
        }
        foreach (var (off, len) in cuts.OrderBy(_ => rnd.Next())) v.Feed(off, data.AsSpan(off, len));

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
    }

    [Fact]
    public void FlippedByte_IsAMismatch_AtItsBlock_AndStaysMissing()
    {
        var (data, m) = File(4 * Block);
        var bad = (byte[])data.Clone();
        bad[2 * Block + 17] ^= 0x01;
        var v = new BlockVerifier(m);
        for (int b = 0; b < m.Count; b++) FeedBlock(v, bad, b);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, 2), v.Finish());
        Assert.Equal([new ByteRange(2 * Block, Block)], v.MissingRanges());
        Assert.Equal(XxHash3.HashToUInt64(bad.AsSpan(2 * Block, Block)), v.DiskHash(2));
        Assert.Null(v.DiskHash(1));
    }

    // Spec 6: what the tap never read (the header, the index at the end) is what the fill-in reads — nothing else.
    [Fact]
    public void Unread_HeaderAndTail_AreTheMissingRanges()
    {
        var (data, m) = File(6 * Block + 10);
        var v = new BlockVerifier(m);
        for (int b = 1; b <= 4; b++) FeedBlock(v, data, b);

        Assert.Equal(BlockOutcomeKind.Incomplete, v.Finish().Kind);
        Assert.Equal([new ByteRange(0, Block), new ByteRange(5 * Block, Block + 10)], v.MissingRanges());
    }

    [Fact]
    public void ShortLastBlock_FedInPieces_IsVerified()
    {
        var (data, m) = File(Block + 10);
        var v = new BlockVerifier(m);
        FeedBlock(v, data, 0);
        v.Feed(Block + 5, data.AsSpan(Block + 5, 5));
        Assert.Equal(BlockOutcomeKind.Incomplete, v.Finish().Kind);
        v.Feed(Block, data.AsSpan(Block, 5));

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
    }

    [Fact]
    public void EmptyFile_NothingToRead_Verified()
    {
        var v = new BlockVerifier(new BlockManifest(0, Block, []));
        v.ObserveLength(0);
        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Empty(v.MissingRanges());
    }

    // Spec 11: a truncated file is found — also when every block the reader got matched.
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void OtherLengthOnDisk_IsAMismatch_EvenWithMatchingBlocks(int delta)
    {
        var (data, m) = File(2 * Block);
        var v = new BlockVerifier(m);
        FeedBlock(v, data, 0);
        FeedBlock(v, data, 1);
        v.ObserveLength(2 * Block + delta);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, -1, 2 * Block + delta), v.Finish());
    }

    [Fact]
    public void BytesPastTheCardsSize_AreIgnored()
    {
        var (data, m) = File(Block + 1);
        var v = new BlockVerifier(m);
        var longer = data.Concat(new byte[100]).ToArray();
        v.Feed(0, longer);

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Equal(longer.Length, v.FedBytes);
    }

    // Spec 5: reading a verified block again changes nothing — unless a whole read gives another answer (an I/O error).
    [Fact]
    public void RepeatedRead_Same_Ignored_Different_MismatchAndUnstable()
    {
        var (data, m) = File(2 * Block);
        var v = new BlockVerifier(m);
        FeedBlock(v, data, 0);
        FeedBlock(v, data, 1);
        FeedBlock(v, data, 0);
        v.Feed(10, new byte[20]);   // a piece of a verified block: ignored, whatever it holds
        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Empty(v.UnstableBlocks);

        var other = (byte[])data.Clone();
        other[5] ^= 0xFF;
        FeedBlock(v, other, 0);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, 0), v.Finish());
        Assert.Equal([0], v.UnstableBlocks);
        Assert.Equal(Block, v.VerifiedBytes);
    }

    // Spec 10: a mismatching block read again without the cache and matching now is verified, with a warning.
    [Fact]
    public void Mismatch_ReadAgainAndMatching_VerifiedButUnstable()
    {
        var (data, m) = File(Block);
        var bad = (byte[])data.Clone();
        bad[0] ^= 1;
        var v = new BlockVerifier(m);
        v.Feed(0, bad);
        Assert.Equal(BlockOutcomeKind.Mismatch, v.Finish().Kind);

        v.Feed(0, data);

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Equal([0], v.UnstableBlocks);
    }

    [Fact]
    public void TooManyBlocksInPieces_TheOldestIsDropped_AndStaysMissing()
    {
        var (data, m) = File((BlockVerifier.MaxPartials + 1) * Block);
        var v = new BlockVerifier(m);
        for (int b = 0; b <= BlockVerifier.MaxPartials; b++) v.Feed((long)b * Block, data.AsSpan(b * Block, 10));
        // Block 0 was the oldest piece: its first bytes are gone, the rest alone never completes it.
        for (int b = 1; b <= BlockVerifier.MaxPartials; b++) v.Feed((long)b * Block + 10, data.AsSpan(b * Block + 10, Block - 10));
        v.Feed(10, data.AsSpan(10, Block - 10));

        Assert.Equal(BlockOutcomeKind.Incomplete, v.Finish().Kind);
        Assert.Equal([new ByteRange(0, Block)], v.MissingRanges());
    }

    [Fact]
    public void Manifest_WrongBlockCount_Refused()
    {
        Assert.Throws<ArgumentException>(() => new BlockManifest(Block + 1, Block, [1UL]));
        Assert.Throws<ArgumentException>(() => new BlockManifest(0, Block, [1UL]));
    }

    [Fact]
    public void ConcurrentFeeds_FromSeveralConnections_Verified()
    {
        var (data, m) = File(64 * Block + 3);
        var v = new BlockVerifier(m);
        Parallel.For(0, m.Count, b => FeedBlock(v, data, b));
        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~BlockVerifierTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0234: The type or namespace name 'VerifyInProxy' does not exist in the namespace 'Offload.Core' (are you missing an assembly reference?)`; `CS0246: The type or namespace name 'BlockManifest' could not be found (are you missing a using directive or an assembly reference?)`; `CS0246: The type or namespace name 'BlockVerifier' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/VerifyInProxy/BlockVerifier.cs` (nový soubor)

```csharp
using System.IO.Hashing;

namespace Offload.Core.VerifyInProxy;

/// Spec 2, 4.5: what the card gave for one file — its size and the XXH3-64 of every block (`BlockSize` bytes, the last one
/// shorter; none for an empty file). The block manifest's record and what a BlockVerifier checks a destination against.
public sealed record BlockManifest
{
    public long Size { get; }
    public int BlockSize { get; }
    public IReadOnlyList<ulong> Blocks { get; }

    public BlockManifest(long size, int blockSize, IReadOnlyList<ulong> blocks)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
        if (blocks.Count != CountFor(size, blockSize))
            throw new ArgumentException($"{blocks.Count} block hashes for {size} bytes in blocks of {blockSize}", nameof(blocks));
        Size = size;
        BlockSize = blockSize;
        Blocks = blocks;
    }

    /// How many blocks `size` bytes make (0 for an empty file).
    public static int CountFor(long size, int blockSize) => (int)((size + blockSize - 1) / blockSize);

    public int Count => Blocks.Count;
    public long Offset(int block) => (long)block * BlockSize;
    public int Length(int block) => (int)Math.Min(BlockSize, Size - Offset(block));
}

public enum BlockOutcomeKind { Verified, Mismatch, Incomplete }

/// The verdict on one file × destination. `Block`: the first block that did not match (Mismatch), or -1 when the file on
/// the disk has another size (`SizeOnDisk`).
public readonly record struct BlockOutcome(BlockOutcomeKind Kind, int Block = -1, long? SizeOnDisk = null);

/// A byte range of the file, `Length` bytes from `Offset`.
public readonly record struct ByteRange(long Offset, long Length);

/// Spec 5 (BlockVerifier): every byte read from a destination — by the proxy tap in any order and with seeks, by the
/// fill-in, by a background read — goes in through Feed; a block is checked as soon as all its bytes are in. Whole-block
/// feeds are hashed straight away; pieces of a block are put together by their offset (at most MaxPartials blocks at a
/// time — the oldest is dropped and simply stays missing). A verified block read again: a whole read is hashed again and
/// a different answer makes the block a mismatch (spec 5: "nesoulad při opakování = chyba I/O"); a piece is ignored. A
/// mismatching block fed whole again and matching now is verified, but counted as an unstable read (spec 10). Bytes past
/// the manifest's size are ignored here — ObserveLength tells the verifier how long the file on the disk is.
/// Thread-safe: the tap feeds from several connections at once.
public sealed class BlockVerifier
{
    public const int MaxPartials = 8;

    enum State : byte { Missing, Partial, Verified, Mismatch }

    sealed class Partial(int length)
    {
        public readonly byte[] Data = new byte[length];
        public readonly List<(long Start, long End)> Covered = [];
        public long LastUse;
    }

    readonly Lock gate = new();
    readonly State[] state;
    readonly ulong?[] diskHash;
    readonly Dictionary<int, Partial> partials = [];
    readonly HashSet<int> unstable = [];
    long tick, verifiedBytes, fedBytes;
    long? sizeOnDisk;
    bool lengthObserved;

    public BlockManifest Manifest { get; }

    public BlockVerifier(BlockManifest manifest)
    {
        Manifest = manifest;
        state = new State[manifest.Count];
        diskHash = new ulong?[manifest.Count];
    }

    /// Bytes of verified blocks so far.
    public long VerifiedBytes
    {
        get { lock (gate) return verifiedBytes; }
    }

    /// Every byte handed in so far, read again or not (spec 11: mode B reads at most 1.05 × the file).
    public long FedBytes
    {
        get { lock (gate) return fedBytes; }
    }

    /// Blocks that gave two different answers on two reads (spec 10: "varování o nestabilním čtení"), in order.
    public IReadOnlyList<int> UnstableBlocks
    {
        get { lock (gate) return unstable.Order().ToList(); }
    }

    /// The XXH3-64 the disk gave for a mismatching block (spec 10: the same block wrong on several destinations), else null.
    public ulong? DiskHash(int block)
    {
        lock (gate) return state[block] == State.Mismatch ? diskHash[block] : null;
    }

    /// The length of the file on the disk, as a reader found it: another length than the card's is a mismatch.
    public void ObserveLength(long length)
    {
        lock (gate)
        {
            lengthObserved = true;
            if (length != Manifest.Size) sizeOnDisk = length;
        }
    }

    /// A reader has told the file's length on the disk (an empty file is verified only by that).
    public bool LengthObserved
    {
        get { lock (gate) return lengthObserved; }
    }

    public void Feed(long offset, ReadOnlySpan<byte> data)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        lock (gate)
        {
            fedBytes += data.Length;
            long end = Math.Min(offset + data.Length, Manifest.Size);
            for (long at = offset; at < end;)
            {
                int b = (int)(at / Manifest.BlockSize);
                long blockStart = Manifest.Offset(b), blockEnd = blockStart + Manifest.Length(b);
                long upTo = Math.Min(end, blockEnd);
                var piece = data.Slice((int)(at - offset), (int)(upTo - at));
                if (at == blockStart && upTo == blockEnd) Whole(b, piece);
                else Piece(b, at - blockStart, piece);
                at = upTo;
            }
        }
    }

    void Whole(int b, ReadOnlySpan<byte> data)
    {
        partials.Remove(b);
        ulong got = XxHash3.HashToUInt64(data);
        bool same = got == Manifest.Blocks[b];
        switch (state[b])
        {
            case State.Verified when !same:
                // Verified before, another answer now: the disk (or the way to it) does not read the same twice.
                unstable.Add(b);
                Set(b, State.Mismatch, got);
                break;
            case State.Verified:
                break;
            case State.Mismatch when same:
                unstable.Add(b);
                Set(b, State.Verified, null);
                break;
            default:
                Set(b, same ? State.Verified : State.Mismatch, same ? null : got);
                break;
        }
    }

    void Piece(int b, long at, ReadOnlySpan<byte> data)
    {
        if (state[b] is State.Verified or State.Mismatch) return;   // a whole read decides those
        if (!partials.TryGetValue(b, out var p))
        {
            if (partials.Count >= MaxPartials)
            {
                var oldest = partials.MinBy(kv => kv.Value.LastUse).Key;
                partials.Remove(oldest);
                state[oldest] = State.Missing;
            }
            p = partials[b] = new Partial(Manifest.Length(b));
            state[b] = State.Partial;
        }
        p.LastUse = ++tick;
        data.CopyTo(p.Data.AsSpan((int)at));
        Add(p.Covered, at, at + data.Length);
        if (p.Covered is [(0, var e)] && e == p.Data.Length) Whole(b, p.Data);
    }

    static void Add(List<(long Start, long End)> covered, long start, long end)
    {
        covered.Add((start, end));
        covered.Sort();
        var merged = new List<(long Start, long End)>();
        foreach (var (s, e) in covered)
        {
            if (merged.Count > 0 && s <= merged[^1].End) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, e));
            else merged.Add((s, e));
        }
        covered.Clear();
        covered.AddRange(merged);
    }

    void Set(int b, State s, ulong? hash)
    {
        if (state[b] == State.Verified) verifiedBytes -= Manifest.Length(b);
        state[b] = s;
        diskHash[b] = hash;
        if (s == State.Verified) verifiedBytes += Manifest.Length(b);
    }

    /// The blocks not verified yet (missing, put together only in part, or mismatching), adjacent ones joined — what the
    /// fill-in reads (spec 6: "nic se nečte celé znovu").
    public IReadOnlyList<ByteRange> MissingRanges()
    {
        lock (gate)
        {
            var ranges = new List<ByteRange>();
            for (int b = 0; b < state.Length; b++)
            {
                if (state[b] == State.Verified) continue;
                long off = Manifest.Offset(b);
                if (ranges.Count > 0 && ranges[^1].Offset + ranges[^1].Length == off)
                    ranges[^1] = ranges[^1] with { Length = ranges[^1].Length + Manifest.Length(b) };
                else ranges.Add(new ByteRange(off, Manifest.Length(b)));
            }
            return ranges;
        }
    }

    /// Verified: every block matched (and the file has the card's size, when a reader told). Mismatch: the size, or the
    /// first block that does not match. Incomplete: blocks are still missing.
    public BlockOutcome Finish()
    {
        lock (gate)
        {
            if (sizeOnDisk is long other) return new BlockOutcome(BlockOutcomeKind.Mismatch, -1, other);
            for (int b = 0; b < state.Length; b++)
                if (state[b] == State.Mismatch) return new BlockOutcome(BlockOutcomeKind.Mismatch, b);
            return state.All(s => s == State.Verified) ? new BlockOutcome(BlockOutcomeKind.Verified) : new BlockOutcome(BlockOutcomeKind.Incomplete);
        }
    }
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~BlockVerifierTests"`

Expected: PASS — `Failed: 0, Passed: 16, Skipped: 0, Total: 16` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1514` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 2: `BlockVerifier` — ověření po blocích v libovolném pořadí, kusy bloků, opakované čtení (nesoulad = chyba I/O), chybějící rozsahy, délka souboru na disku.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: block verifier for any read order (plan 10, task 2)

BlockVerifier checks one file on one destination from reads in any order: whole blocks or
pieces, repeated or concurrent; a repeated read with other data is an I/O error. It reports the
missing ranges and the length on the disk.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\VerifyInProxy\BlockVerifier.cs windows\tests\Offload.Core.Tests\BlockVerifierTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 3: Blokový manifest `_REPORT/offload-manifest.jsonl` (R4, R6)

Manifest na každém cíli: záznam `file` (soubor je na svém místě: velikost, čas, xxh64, xxh128, MD5, bloky) a
kontrolní body `part` rozepisovaného souboru (bloky od `first`). Soubor se jen připisuje — řádek se zapíše s
`WRITE_THROUGH` a flush, nic se nepřepisuje (pravidlo plánů 6–9); řádek přerušený pádem se při čtení přeskočí a další
zápis začne na novém řádku. Do `_REPORT` (R4) — MHL i „Ověřit zálohu“ ho ignorují.

**Files:**
- Create: `windows/src/Offload.Core/VerifyInProxy/ManifestFile.cs`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/ManifestFileTests.cs` (nový)

**Interfaces:**
- Consumes: `BlockManifest` (úkol 2), `OffloadReport.IsoSecondsDateConverter`, `Format.Hex64`.
- Produces:
  - `ManifestRecord` (`FileKind`/`PartKind`, `Version`, `Kind`, `Generation`, `Path`, `Size`, `Modified`, `Xxh64`, `Md5`, `Xxh128`, `BlockSize`, `First`, `Blocks`), `static Hex(IEnumerable<ulong>)`, `BlockValues()`, `ToManifest()`, `Hashes()`.
  - `PartCheckpoint(string Path, long Size, DateTime Modified, int BlockSize, ulong[] Blocks)`.
  - `ManifestFile`: `FileName`, `PathIn(cardPath)`, `Append(cardPath, ManifestRecord)`, `Append(cardPath, IReadOnlyList<ManifestRecord>)`, `Read(cardPath)`, `Files(records, generation)`, `Parts(records, generation)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/ManifestFileTests.cs` (nový soubor)

```csharp
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// R4, R6: `_REPORT\offload-manifest.jsonl` — append-only, a torn line never spoils the next, checkpoints chain up.
public class ManifestFileTests
{
    static readonly DateTime T = new(2026, 10, 8, 9, 30, 15, DateTimeKind.Utc);

    static string Card(TempDir dir)
    {
        var card = dir.Sub("2026-10-08", "A001");
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        return card;
    }

    static ManifestRecord FileRecord(string path, params ulong[] blocks) => new()
    {
        Path = path, Size = blocks.Length * 4096L, Modified = T, Xxh64 = "0123456789abcdef", Xxh128 = new string('a', 32),
        BlockSize = 4096, Blocks = ManifestRecord.Hex(blocks),
    };

    [Fact]
    public void Append_ThenRead_RoundTrip_OneLineEach()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        ManifestFile.Append(card, FileRecord("CLIP/a.mov", 1, 2));
        ManifestFile.Append(card, FileRecord("b.wav", 0xFFFFFFFFFFFFFFFF));

        var text = File.ReadAllText(ManifestFile.PathIn(card));
        Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.EndsWith("\n", text);
        Assert.Contains("\"blocks\":[\"0000000000000001\",\"0000000000000002\"]", text);
        Assert.Contains("\"modified\":\"2026-10-08T09:30:15Z\"", text);
        var all = ManifestFile.Read(card);
        Assert.Equal(["CLIP/a.mov", "b.wav"], all.Select(r => r.Path));
        var m = all[0].ToManifest()!;
        Assert.Equal([1UL, 2UL], m.Blocks);
        Assert.Equal(8192, m.Size);
        Assert.Equal(0x0123456789abcdefUL, all[0].Hashes()!.Value.Xxh64);
        Assert.Equal(new string('a', 32), all[0].Hashes()!.Value.Xxh128);
    }

    // A crash in the middle of a line: that line is skipped, the next append starts on its own line.
    [Fact]
    public void TornLine_IsSkipped_AndTheNextRecordIsWhole()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        ManifestFile.Append(card, FileRecord("a.mov", 1));
        File.AppendAllText(ManifestFile.PathIn(card), "{\"v\":1,\"kind\":\"file\",\"path\":\"b.m");
        ManifestFile.Append(card, FileRecord("c.mov", 3));

        Assert.Equal(["a.mov", "c.mov"], ManifestFile.Read(card).Select(r => r.Path));
    }

    // Never rewritten: what was there stays byte for byte, the new line goes after it.
    [Fact]
    public void Append_KeepsEarlierBytes()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        ManifestFile.Append(card, FileRecord("a.mov", 1));
        var before = File.ReadAllBytes(ManifestFile.PathIn(card));
        ManifestFile.Append(card, FileRecord("b.mov", 2));

        var after = File.ReadAllBytes(ManifestFile.PathIn(card));
        Assert.Equal(before, after[..before.Length]);
    }

    [Fact]
    public void Read_NoManifest_Empty()
    {
        using var dir = new TempDir();
        Assert.Empty(ManifestFile.Read(Card(dir)));
    }

    [Fact]
    public void Files_NewestOfItsGeneration_AnyCase()
    {
        var records = new[]
        {
            FileRecord("A.mov", 1),
            FileRecord("a.mov", 2),
            new ManifestRecord { Path = "a.mov", Generation = 2, BlockSize = 4096, Size = 4096, Blocks = ManifestRecord.Hex([9UL]) },
        };
        var files = ManifestFile.Files(records, 1);
        Assert.Equal([2UL], files["A.MOV"].ToManifest()!.Blocks);
        Assert.Equal([9UL], ManifestFile.Files(records, 2)["a.mov"].ToManifest()!.Blocks);
    }

    static ManifestRecord Part(string path, int first, params ulong[] blocks) => new()
    {
        Kind = ManifestRecord.PartKind, Path = path, Size = 10 * 4096, Modified = T, BlockSize = 4096, First = first, Blocks = ManifestRecord.Hex(blocks),
    };

    [Fact]
    public void Parts_ChainUp_GapIgnored_RestartFromZero()
    {
        var chained = ManifestFile.Parts([Part("a.mov", 0, 1, 2), Part("a.mov", 2, 3), Part("a.mov", 5, 6)], 1);
        Assert.Equal([1UL, 2UL, 3UL], chained["a.mov"].Blocks);

        var restarted = ManifestFile.Parts([Part("a.mov", 0, 1, 2), Part("a.mov", 0, 7)], 1);
        Assert.Equal([7UL], restarted["a.mov"].Blocks);
    }

    [Fact]
    public void ToManifest_WrongCountOrBadHex_Null()
    {
        Assert.Null(new ManifestRecord { Path = "a", Size = 8192, BlockSize = 4096, Blocks = ["0000000000000001"] }.ToManifest());
        Assert.Null(new ManifestRecord { Path = "a", Size = 4096, BlockSize = 4096, Blocks = ["zz"] }.ToManifest());
        Assert.Null(Part("a", 0, 1).ToManifest());
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ManifestFileTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0246: The type or namespace name 'ManifestRecord' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/VerifyInProxy/ManifestFile.cs` (nový soubor)

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core.VerifyInProxy;

/// One line of `<card>\_REPORT\offload-manifest.jsonl` (spec 4.5, R4). `file`: a file in its place on this destination,
/// with the card's hashes and the XXH3-64 of every block. `part`: blocks `first` … of a file still being written under its
/// `.offload-part` name — on the disk, written through — the checkpoint a later append resumes from (R6). Generation
/// `gen` is the ASC MHL generation the copy wrote. The Mac ignores the file (FORMATY).
public sealed class ManifestRecord
{
    public const string FileKind = "file";
    public const string PartKind = "part";

    [JsonPropertyName("v")] public int Version { get; init; } = 1;
    [JsonPropertyName("kind")] public string Kind { get; init; } = FileKind;
    [JsonPropertyName("gen")] public int Generation { get; init; } = 1;
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("modified")] public DateTime Modified { get; init; }
    [JsonPropertyName("xxh64"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Xxh64 { get; init; }
    [JsonPropertyName("md5"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Md5 { get; init; }
    [JsonPropertyName("xxh128"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Xxh128 { get; init; }
    [JsonPropertyName("blockSize")] public int BlockSize { get; init; }
    /// `part` only: the index of the first block in `blocks`.
    [JsonPropertyName("first"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int First { get; init; }
    /// XXH3-64 of each block, 16 lower-case hex digits.
    [JsonPropertyName("blocks")] public IReadOnlyList<string> Blocks { get; init; } = [];

    public static IReadOnlyList<string> Hex(IEnumerable<ulong> blocks) => blocks.Select(Format.Hex64).ToList();

    /// The blocks as numbers; null when one is not 16 hex digits.
    public ulong[]? BlockValues()
    {
        var values = new ulong[Blocks.Count];
        for (int i = 0; i < values.Length; i++)
            if (Blocks[i] is not { Length: 16 } h || !ulong.TryParse(h, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out values[i])) return null;
        return values;
    }

    /// A `file` record as what the destination's copy is checked against; null when it does not hold together.
    public BlockManifest? ToManifest()
    {
        if (Kind != FileKind || BlockSize <= 0 || BlockValues() is not { } values || values.Length != BlockManifest.CountFor(Size, BlockSize)) return null;
        return new BlockManifest(Size, BlockSize, values);
    }

    /// The hashes the card gave, as the copy records them; null when xxh64 is not readable.
    public FileHashes? Hashes()
    {
        if (Xxh64 is not { Length: 16 } x || !ulong.TryParse(x, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var xx)) return null;
        byte[]? md5 = null;
        if (Md5 != null)
        {
            try { md5 = Convert.FromHexString(Md5); }
            catch (FormatException) { return null; }
        }
        return new FileHashes(xx, md5, Xxh128);
    }
}

/// A checkpoint put together from a file's `part` records: the first `Blocks.Length` blocks are on the disk.
public sealed record PartCheckpoint(string Path, long Size, DateTime Modified, int BlockSize, ulong[] Blocks);

/// `<card>\_REPORT\offload-manifest.jsonl` — only ever appended to: a line is written through and flushed, nothing in the
/// file is rewritten (plan 6–9 rule "nikdy nepřepsat"; the manifest is an append-only file of its own, FORMATY). A line
/// left half-written by a crash is skipped when read, and the next append starts on a new line.
public static class ManifestFile
{
    public const string FileName = "offload-manifest.jsonl";

    static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new OffloadReport.IsoSecondsDateConverter() },
    };

    static readonly Dictionary<string, Lock> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static string PathIn(string cardPath) => System.IO.Path.Combine(cardPath, Layout.ReportDirName, FileName);

    static Lock LockFor(string path)
    {
        lock (Locks)
        {
            if (!Locks.TryGetValue(path, out var l)) Locks[path] = l = new Lock();
            return l;
        }
    }

    /// Appends one record (the recorder and a writer's checkpoints share the file: one at a time). IOError on failure.
    public static void Append(string cardPath, ManifestRecord record) => Append(cardPath, [record]);

    /// Appends records, one line each, with one flush (many small files finish together).
    public static void Append(string cardPath, IReadOnlyList<ManifestRecord> records)
    {
        if (records.Count == 0) return;
        var path = PathIn(cardPath);
        var line = Encoding.UTF8.GetBytes(string.Concat(records.Select(r => JsonSerializer.Serialize(r, Json) + "\n")));
        lock (LockFor(System.IO.Path.GetFullPath(path)))
        {
            try
            {
                using var f = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.WriteThrough);
                if (f.Length > 0)
                {
                    f.Seek(-1, SeekOrigin.End);
                    if (f.ReadByte() != '\n') f.WriteByte((byte)'\n');   // a torn last line stays alone on its line
                }
                f.Seek(0, SeekOrigin.End);
                f.Write(line);
                f.Flush(flushToDisk: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new IOError("write", path, e);
            }
        }
    }

    /// Every record that can be read, in the order written; none when there is no manifest.
    public static IReadOnlyList<ManifestRecord> Read(string cardPath)
    {
        var path = PathIn(cardPath);
        if (!File.Exists(path)) return [];
        string text;
        using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var r = new StreamReader(s, Encoding.UTF8))
            text = r.ReadToEnd();
        var records = new List<ManifestRecord>();
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Length == 0) continue;
            try
            {
                if (JsonSerializer.Deserialize<ManifestRecord>(line, Json) is { Version: 1, Path.Length: > 0 } r) records.Add(r);
            }
            catch (JsonException) { /* torn by a crash, or not ours */ }
        }
        return records;
    }

    /// The newest `file` record of every path of generation `generation` (paths compared in any case).
    public static IReadOnlyDictionary<string, ManifestRecord> Files(IEnumerable<ManifestRecord> records, int generation)
    {
        var files = new Dictionary<string, ManifestRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
            if (r.Kind == ManifestRecord.FileKind && r.Generation == generation) files[r.Path] = r;
        return files;
    }

    /// The checkpoint of every path of generation `generation` from its `part` records: a record with `first` 0 starts a
    /// new attempt, one that continues the blocks so far extends it, anything else (a gap) is ignored. A path whose
    /// size, time or block size changes between records starts again with the record that changed it.
    public static IReadOnlyDictionary<string, PartCheckpoint> Parts(IEnumerable<ManifestRecord> records, int generation)
    {
        var parts = new Dictionary<string, PartCheckpoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
        {
            if (r.Kind != ManifestRecord.PartKind || r.Generation != generation || r.BlockSize <= 0 || r.BlockValues() is not { } values) continue;
            parts.TryGetValue(r.Path, out var had);
            bool same = had != null && had.Size == r.Size && had.Modified == r.Modified && had.BlockSize == r.BlockSize;
            if (r.First == 0) parts[r.Path] = new PartCheckpoint(r.Path, r.Size, r.Modified, r.BlockSize, values);
            else if (same && r.First == had!.Blocks.Length) parts[r.Path] = had with { Blocks = [.. had.Blocks, .. values] };
        }
        return parts;
    }
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ManifestFileTests"`

Expected: PASS — `Failed: 0, Passed: 7, Skipped: 0, Total: 7` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1521` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 3: `_REPORT/offload-manifest.jsonl` — jen připisovaný manifest (záznam souboru s bloky, kontrolní body rozepsaného souboru), řádek přerušený pádem se přeskočí.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: append-only block manifest (plan 10, task 3)

_REPORT/offload-manifest.jsonl: file records (size, time, xxh64, xxh128, md5, block hashes) and
checkpoints of a file being written. Append-only with write-through; a torn last line is skipped.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\VerifyInProxy\ManifestFile.cs windows\tests\Offload.Core.Tests\ManifestFileTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 4: Zápis NO_BUFFERING | WRITE_THROUGH, rychlost cílů, kontrolní body a záznamy manifestu (R8, R6, spec 4.5)

Zápis na cíl dostane k `FILE_FLAG_NO_BUFFERING` i `FILE_FLAG_WRITE_THROUGH` (R8): zápis je na disku, když se
vrátí, a nikdy v cache — co se pak čte, jde z disku (naměřeno: zápis není pomalejší; NVMe 3,5 GB/s, USB HDD 57 MB/s).
`DestWriter` měří svou rychlost zápisu (spec 6: nejrychlejší cíl bude primární), počítá XXH3-64 každého zapsaného bloku
a po každých `CheckpointBlocks` blocích (64 = 1 GiB) zapíše do manifestu kontrolní bod (jen mimo cache — buffered zápis
může být ještě v RAM). Vlákno `offload.manifest` zapisuje záznam `file` každého souboru, jakmile je na svém místě
(spec 4.5 „po každém souboru řádek“; víc malých souborů jedním zápisem); oprava zapíše své soubory až po
`PlaceRepairCopies`. Manifest se jen připisuje — testy plánu 6/9, které hlídají „nic se nepřepsalo“, ho teď kontrolují
jako soubor, do kterého se jen přidává na konec.

**Files:**
- Modify: `windows/src/Offload.Core/NativeIO.cs:17-20`
- Modify: `windows/src/Offload.Core/DestWriter.cs:1, 11-20, 38-39, 56-60, 76-79, 123-134, 152-153`
- Modify: `windows/src/Offload.Core/Models.cs:14-15`
- Modify: `windows/src/Offload.Core/OffloadEngine.cs:15-16, 35-36, 274`
- Modify: `windows/src/Offload.Core/OffloadEngine.Run.cs:1, 25, 34-38, 97-104, 119, 168-174`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/ManifestRecordingTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/AppendEngineTests.cs:30, 64-67, 197-200, 667, 675-676`
- Test: `windows/tests/Offload.Core.Tests/RepairPlannerTests.cs:40-47`

**Interfaces:**
- Consumes: `ManifestFile`, `ManifestRecord` (úkol 3), `SourceHasher.Blocks`/`Xxh128` (úkol 1).
- Produces:
  - `NativeIO.OpenWrite(path, unbuffered: true, …)` = `NO_BUFFERING | WriteThrough`.
  - `DestWriter(int index, string cardPath, int fileCount, OffloadOptions options, int generation = 1)`, `double? WriteRate`.
  - `OffloadOptions.CheckpointBlocks` (internal, výchozí 64).
  - `OffloadEngine.Blocks` (`ulong[]?[]`), `WriteRates` (`IReadOnlyList<double?>`), `WrittenUnbuffered` (`IReadOnlyList<bool>`) — platné po `Run`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/ManifestRecordingTests.cs` (nový soubor)

```csharp
using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 4.5, R4, R6: while copying, every destination's `_REPORT\offload-manifest.jsonl` gets a checkpoint of the file
/// being written (every CheckpointBlocks blocks) and the file's record once it is in its place — with the card's hashes
/// and block hashes; a file that failed there gets none. The copy measures each destination's write speed (spec 6).
public class ManifestRecordingTests
{
    const int Block = 64 * 1024;

    static OffloadOptions Options() => new() { ChunkSize = Block, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, CheckpointBlocks = 1 };

    static ulong[] Blocks(byte[] data) => Enumerable.Range(0, BlockManifest.CountFor(data.Length, Block))
        .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, Math.Min(Block, data.Length - b * Block)))).ToArray();

    [Fact]
    public void Copy_WritesCheckpointsAndRecords()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var big = TestFs.WriteRandom(Path.Combine(card, "CLIP", "A.mov"), 3 * Block + 5, 1);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");

        engine.Run(plan);

        var records = ManifestFile.Read(plan.CardPaths[0]);
        Assert.Equal(Blocks(big), ManifestFile.Parts(records, 1)["CLIP/A.mov"].Blocks);
        var file = ManifestFile.Files(records, 1)["CLIP/A.mov"];
        Assert.Equal(Blocks(big), file.ToManifest()!.Blocks);
        Assert.Equal(Format.Hex64(XxHash64.HashToUInt64(big)), file.Xxh64);
        Assert.Equal(engine.Blocks[0], file.ToManifest()!.Blocks);
    }

    [Fact]
    public void EveryDestination_ItsOwnManifest_WithMd5WhenTheCopyHashesIt()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = TestFs.WriteRandom(Path.Combine(card, "a.mov"), 2 * Block, 1);
        TestFs.WriteRandom(Path.Combine(card, "b.wav"), 100, 2);
        var options = Options();
        options.Hashes = HashKind.Both;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-08");

        engine.Run(plan);

        foreach (var p in plan.CardPaths)
        {
            var files = ManifestFile.Files(ManifestFile.Read(p), 1);
            Assert.Equal(["a.mov", "b.wav"], files.Keys.Order());
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(a)), files["a.mov"].Md5);
            Assert.Equal(32, files["a.mov"].Xxh128!.Length);
        }
        Assert.All(engine.WriteRates, r => Assert.True(r > 0));
        Assert.All(engine.WrittenUnbuffered, Assert.True);
    }

    // A disk that failed while writing a file has no record of it — a resume never trusts that copy.
    [Fact]
    public void FailedOnADestination_NoRecordThere()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 2 * Block, 1);
        var options = Options();
        options.BeforeDestWrite = d => { if (d == 1) throw new IOException("disk gone"); };
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-08");

        engine.Run(plan);

        Assert.Single(ManifestFile.Files(ManifestFile.Read(plan.CardPaths[0]), 1));
        Assert.Empty(ManifestFile.Files(ManifestFile.Read(plan.CardPaths[1]), 1));
    }

    // A buffered destination writes no checkpoints (its data may still be in RAM) — its records are written as usual.
    [Fact]
    public void BufferedDestination_NoCheckpoints_ButRecords()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 3 * Block, 1);
        var options = Options();
        options.ForceBuffered = true;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");

        engine.Run(plan);

        var records = ManifestFile.Read(plan.CardPaths[0]);
        Assert.Empty(ManifestFile.Parts(records, 1));
        Assert.Single(ManifestFile.Files(records, 1));
        Assert.Equal([false], engine.WrittenUnbuffered);
    }
}
```

#### `windows/tests/Offload.Core.Tests/AppendEngineTests.cs`

Najít (1/5, řádek 30):

```csharp
    static readonly string[] Rewritten = [@"_REPORT\A001.xxh64", @"ascmhl\ascmhl_chain.xml", @"_REPORT\A001_clips.csv"];
```

Nahradit:

```csharp
    /// The record files an append may change: the hash list and the manifest only by adding at the end (checked by
    /// AssertAppendedOnly), the chain and the clip list replaced.
    static readonly string[] Rewritten = [@"_REPORT\A001.xxh64", @"ascmhl\ascmhl_chain.xml", @"_REPORT\A001_clips.csv", @"_REPORT\offload-manifest.jsonl"];

    /// Plan 10: the manifest is append-only — every earlier byte is still there, new lines only after them.
    static void AssertAppendedOnly(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after)
    {
        foreach (var name in new[] { @"_REPORT\A001.xxh64", @"_REPORT\offload-manifest.jsonl" })
            if (before.TryGetValue(name, out var was)) Assert.Equal(was, after[name][..was.Length]);
    }
```

Najít (2/5, řádky 64–67):

```csharp
            // Every earlier file byte for byte — the data, the reports, the PDF, generation 0001 — but the two rewritten ones.
            var after = AppendFixtures.Tree(paths[d]);
            foreach (var (name, bytes) in trees[d].Where(kv => !Rewritten.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
                Assert.Equal(bytes, after[name]);
```

Nahradit:

```csharp
            // Every earlier file byte for byte — the data, the reports, the PDF, generation 0001 — but the record files.
            var after = AppendFixtures.Tree(paths[d]);
            foreach (var (name, bytes) in trees[d].Where(kv => !Rewritten.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
                Assert.Equal(bytes, after[name]);
            AssertAppendedOnly(trees[d], after);
```

Najít (3/5, řádky 197–200):

```csharp
            var after = AppendFixtures.Tree(paths[d]);
            foreach (var (name, bytes) in trees[d].Where(kv => !Rewritten.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
                Assert.Equal(bytes, after[name]);
            Assert.True(File.Exists(Path.Combine(paths[d], "_REPORT", "offload_report_0003.json")));
```

Nahradit:

```csharp
            var after = AppendFixtures.Tree(paths[d]);
            foreach (var (name, bytes) in trees[d].Where(kv => !Rewritten.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
                Assert.Equal(bytes, after[name]);
            AssertAppendedOnly(trees[d], after);
            Assert.True(File.Exists(Path.Combine(paths[d], "_REPORT", "offload_report_0003.json")));
```

Najít (4/5, řádek 667):

```csharp
        string[] rewritten = [.. Rewritten, @"_REPORT\A001.md5"];
```

Nahradit:

```csharp
        string[] rewritten = [.. Rewritten, @"_REPORT\A001.md5"];
        Assert.All(trees, t => Assert.True(t.ContainsKey(@"_REPORT\offload-manifest.jsonl")));
```

Najít (5/5, řádky 675–676):

```csharp
            foreach (var name in new[] { @"_REPORT\A001.xxh64", @"_REPORT\A001.md5" })
                Assert.Equal(trees[d][name], after[name][..trees[d][name].Length]);   // earlier lines byte for byte
```

Nahradit:

```csharp
            foreach (var name in new[] { @"_REPORT\A001.xxh64", @"_REPORT\A001.md5", @"_REPORT\offload-manifest.jsonl" })
                Assert.Equal(trees[d][name], after[name][..trees[d][name].Length]);   // earlier lines byte for byte
```

#### `windows/tests/Offload.Core.Tests/RepairPlannerTests.cs`

Najít (1/1, řádky 40–47):

```csharp
    /// Every file of `before` is still there byte for byte with its time, except the paths in `mayChange` (the record
    /// files a generation may rewrite); returns the files that are new.
    public static List<string> AssertUntouchedExcept(Dictionary<string, (byte[] Bytes, DateTime Time)> before, string dir, params string[] mayChange)
    {
        var after = Snapshot(dir);
        foreach (var (path, (bytes, time)) in before)
        {
            if (mayChange.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
```

Nahradit:

```csharp
    /// Every file of `before` is still there byte for byte with its time, except the paths in `mayChange` (the record
    /// files a generation may rewrite) and the block manifest (plan 10), which may only grow at its end; returns the files
    /// that are new.
    public static List<string> AssertUntouchedExcept(Dictionary<string, (byte[] Bytes, DateTime Time)> before, string dir, params string[] mayChange)
    {
        var after = Snapshot(dir);
        foreach (var (path, (bytes, time)) in before)
        {
            if (mayChange.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            if (path.EndsWith("_REPORT/offload-manifest.jsonl", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(after.ContainsKey(path), $"{path} is gone from {dir}");
                Assert.True(bytes.AsSpan().SequenceEqual(after[path].Bytes.AsSpan(0, Math.Min(bytes.Length, after[path].Bytes.Length))) && after[path].Bytes.Length >= bytes.Length,
                    $"{path} was changed, not appended to, in {dir}");
                continue;
            }
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ManifestRecordingTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~RepairEngineTests|FullyQualifiedName~RepairJobTests|FullyQualifiedName~EnginePauseTests|FullyQualifiedName~DestWriterTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0117: 'OffloadOptions' does not contain a definition for 'CheckpointBlocks'`; `CS1061: 'OffloadEngine' does not contain a definition for 'Blocks' and no accessible extension method 'Blocks' accepting a first argument of type 'OffloadEngine' could be found (are you missing a using directive or an assembly reference?)`; `CS1061: 'OffloadEngine' does not contain a definition for 'WriteRates' and no accessible extension method 'WriteRates' accepting a first argument of type 'OffloadEngine' could be found (are you missing a using directive or an assembly reference?)` a další chyby téhož druhu (všechny jen v testech tohoto úkolu).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/NativeIO.cs`

Najít (1/1, řádky 17–20):

```csharp
    /// Creates or truncates a file. `preallocate` reserves the space up front, so a full disk fails here, not halfway.
    public static SafeFileHandle OpenWrite(string path, bool unbuffered, long preallocate = 0) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None,
            unbuffered ? NoBuffering : FileOptions.None, preallocate));
```

Nahradit:

```csharp
    /// Creates or truncates a file. `preallocate` reserves the space up front, so a full disk fails here, not halfway.
    /// Unbuffered = FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH (plan 10, R8): a write is on the disk when it returns
    /// and never in the file cache — what is read later comes from the disk (measured: no slower than without it).
    public static SafeFileHandle OpenWrite(string path, bool unbuffered, long preallocate = 0) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None,
            unbuffered ? NoBuffering | FileOptions.WriteThrough : FileOptions.None, preallocate));
```

#### `windows/src/Offload.Core/DestWriter.cs`

Najít (1/7, řádek 1):

```csharp
using Microsoft.Win32.SafeHandles;
```

Nahradit:

```csharp
using System.Diagnostics;
using System.IO.Hashing;
using Microsoft.Win32.SafeHandles;
using Offload.Core.VerifyInProxy;
```

Najít (2/7, řádky 11–20):

```csharp
    readonly SerialWorker worker;
    readonly OffloadOptions options;
    readonly int index;
    SafeFileHandle? handle;
    string partPath = "";
    string? currentError;
    int align = AlignedBuffer.Alignment;
    long written;
    long verified;
    volatile bool reading;
```

Nahradit:

```csharp
    readonly SerialWorker worker;
    readonly OffloadOptions options;
    readonly int index;
    readonly int generation;
    SafeFileHandle? handle;
    string partPath = "";
    string? currentError;
    int align = AlignedBuffer.Alignment;
    long written;
    long verified;
    volatile bool reading;
    /// The file being written, the XXH3-64 of its blocks written so far, and how many of them a checkpoint holds (R6).
    SourceFile? current;
    readonly List<ulong> blockHashes = [];
    int checkpointed;
    long writeTicks, writeBytes;
```

Najít (3/7, řádky 38–39):

```csharp
    public long Written => Interlocked.Read(ref written);
    public long Verified => Interlocked.Read(ref verified);
```

Nahradit:

```csharp
    public long Written => Interlocked.Read(ref written);
    public long Verified => Interlocked.Read(ref verified);
    /// Bytes per second while writing (the time inside the writes only); null before the first write. Plan 10: the
    /// fastest destination becomes the primary one (spec 6).
    public double? WriteRate
    {
        get
        {
            long ticks = Interlocked.Read(ref writeTicks);
            return ticks > 0 ? Interlocked.Read(ref writeBytes) / ((double)ticks / Stopwatch.Frequency) : null;
        }
    }
```

Najít (4/7, řádky 56–60):

```csharp
    public DestWriter(int index, string cardPath, int fileCount, OffloadOptions options)
    {
        CardPath = cardPath;
        this.index = index;
        this.options = options;
```

Nahradit:

```csharp
    /// `generation`: the ASC MHL generation this copy writes — the manifest's checkpoints say it (R6).
    public DestWriter(int index, string cardPath, int fileCount, OffloadOptions options, int generation = 1)
    {
        CardPath = cardPath;
        this.index = index;
        this.options = options;
        this.generation = generation;
```

Najít (5/7, řádky 76–79):

```csharp
    public void Begin(SourceFile file) => worker.Post(() =>
    {
        currentError = null;
        partPath = Layout.Join(CardPath, PartName(file.RelPath));
```

Nahradit:

```csharp
    public void Begin(SourceFile file) => worker.Post(() =>
    {
        currentError = null;
        current = file;
        blockHashes.Clear();
        checkpointed = 0;
        partPath = Layout.Join(CardPath, PartName(file.RelPath));
```

Najít (6/7, řádky 123–134):

```csharp
            // Unbuffered writes must be whole sectors: the last block is written padded, Finish trims the file.
            int len = Unbuffered ? (c.Length + align - 1) / align * align : c.Length;
            try
            {
                NativeIO.Write(handle, c.Buffer.Span[..len], c.Offset, partPath);
            }
            catch (IOError e) when (Unbuffered && e.Code == NativeIO.ErrorInvalidParameter)
            {
                SwitchToBuffered();
                NativeIO.Write(handle!, c.Buffer.Span[..c.Length], c.Offset, partPath);
            }
            Interlocked.Add(ref written, c.Length);
```

Nahradit:

```csharp
            // Unbuffered writes must be whole sectors: the last block is written padded, Finish trims the file.
            int len = Unbuffered ? (c.Length + align - 1) / align * align : c.Length;
            long started = Stopwatch.GetTimestamp();
            try
            {
                NativeIO.Write(handle, c.Buffer.Span[..len], c.Offset, partPath);
            }
            catch (IOError e) when (Unbuffered && e.Code == NativeIO.ErrorInvalidParameter)
            {
                SwitchToBuffered();
                NativeIO.Write(handle!, c.Buffer.Span[..c.Length], c.Offset, partPath);
            }
            Interlocked.Add(ref writeTicks, Stopwatch.GetTimestamp() - started);
            Interlocked.Add(ref writeBytes, c.Length);
            Interlocked.Add(ref written, c.Length);
            blockHashes.Add(XxHash3.HashToUInt64(c.Buffer.Span[..c.Length]));
            if (options.CheckpointBlocks > 0 && blockHashes.Count - checkpointed >= options.CheckpointBlocks) Checkpoint();
```

Najít (7/7, řádky 152–153):

```csharp
    void SwitchToBuffered()
    {
```

Nahradit:

```csharp
    /// R6: the blocks written since the last checkpoint go into the manifest — on the disk already (written through), so a
    /// later append resumes after them. Only past the file cache: a buffered write may still be in RAM. Best effort: a
    /// checkpoint that cannot be written only means a resume starts earlier.
    void Checkpoint()
    {
        if (!Unbuffered || current is not { } f) return;
        try
        {
            ManifestFile.Append(CardPath, new ManifestRecord
            {
                Kind = ManifestRecord.PartKind, Generation = generation, Path = f.RelPath, Size = f.Size, Modified = f.ModifiedUtc,
                BlockSize = options.ChunkSize, First = checkpointed, Blocks = ManifestRecord.Hex(blockHashes.Skip(checkpointed)),
            });
            checkpointed = blockHashes.Count;
        }
        catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { }
    }

    void SwitchToBuffered()
    {
```

#### `windows/src/Offload.Core/Models.cs`

Najít (1/1, řádky 14–15):

```csharp
    /// Files at least this large are flushed individually; the rest are flushed once at the end.
    public long FullSyncThreshold { get; set; } = 256L << 20;
```

Nahradit:

```csharp
    /// Files at least this large are flushed individually; the rest are flushed once at the end.
    public long FullSyncThreshold { get; set; } = 256L << 20;
    /// Plan 10, R6: every this many blocks of a file being written, its block hashes go into the manifest as a checkpoint
    /// (64 × 16 MiB = 1 GiB); 0 = none.
    internal int CheckpointBlocks { get; set; } = 64;
```

#### `windows/src/Offload.Core/OffloadEngine.cs`

Najít (1/3, řádky 15–16):

```csharp
    long readBytes;
    DestWriter[] writers = [];
```

Nahradit:

```csharp
    long readBytes;
    DestWriter[] writers = [];
    /// Plan 10: appends the manifest's `file` records on its own thread (spec 4.5: "po každém souboru řádek").
    SerialWorker? recorder;
```

Najít (2/3, řádky 35–36):

```csharp
    /// Waits while paused; false when the job was cancelled meanwhile (stage 2 uses it before every clip).
    public bool WaitWhilePaused() => Pauses.WaitWhilePaused(cts.Token);
```

Nahradit:

```csharp
    /// Waits while paused; false when the job was cancelled meanwhile (stage 2 uses it before every clip).
    public bool WaitWhilePaused() => Pauses.WaitWhilePaused(cts.Token);

    /// After Run (plan 10): per file, the XXH3-64 of its blocks as the card gave them (null when it was not read to the
    /// end); per destination, its write speed and whether it was written past the file cache.
    public ulong[]?[] Blocks { get; private set; } = [];
    public IReadOnlyList<double?> WriteRates { get; private set; } = [];
    public IReadOnlyList<bool> WrittenUnbuffered { get; private set; } = [];
```

Najít (3/3, řádek 274):

```csharp
                Pauses.IsPaused, Pauses.IsHolding && writers.All(w => w.IsIdle), planned, writers.Select(w => w.VerifyPlanned).ToArray());
```

Nahradit:

```csharp
                Pauses.IsPaused, Pauses.IsHolding && writers.All(w => w.IsIdle) && (recorder?.IsIdle ?? true), planned,
                writers.Select(w => w.VerifyPlanned).ToArray());
```

#### `windows/src/Offload.Core/OffloadEngine.Run.cs`

Najít (1/6, řádek 1):

```csharp
namespace Offload.Core;
```

Nahradit:

```csharp
using Offload.Core.VerifyInProxy;

namespace Offload.Core;
```

Najít (2/6, řádek 25):

```csharp
        var ws = cardPaths.Select((p, i) => new DestWriter(i, p, files.Count, Options)).ToArray();
```

Nahradit:

```csharp
        var ws = cardPaths.Select((p, i) => new DestWriter(i, p, files.Count, Options, plan.Generation)).ToArray();
```

Najít (3/6, řádky 34–38):

```csharp
        BufferPool? pool = null;
        var hashers = new List<SourceHasher>();
        try
        {
            pool = new BufferPool(Options.BufferCount, Options.ChunkSize);
```

Nahradit:

```csharp
        BufferPool? pool = null;
        var hashers = new List<SourceHasher>();
        var unrecorded = new System.Collections.Concurrent.ConcurrentQueue<(int File, DestWriter[] Targets)>();
        try
        {
            pool = new BufferPool(Options.BufferCount, Options.ChunkSize);
            // A repair's copies take their place only after the copy (PlaceRepairCopies): it records them there.
            if (plan.Expected == null) lock (gate) recorder = new SerialWorker("offload.manifest");
```

Najít (4/6, řádky 97–104):

```csharp
                    foreach (var h in hashers) h.Finish(i);
                    // A repair's new copy waits under its temporary name until the card's hash is known (below).
                    foreach (var w in targets) w.Finish(i, f, hold: plan.Expected != null);
                }
            }
            foreach (var h in hashers) h.Wait();
            foreach (var w in ws) w.Wait();
            var hashes = SourceHasher.Combine(hashers, files.Count);
```

Nahradit:

```csharp
                    foreach (var h in hashers) h.Finish(i);
                    // A repair's new copy waits under its temporary name until the card's hash is known (below).
                    foreach (var w in targets) w.Finish(i, f, hold: plan.Expected != null);
                    if (recorder != null && targets.Length > 0)
                    {
                        unrecorded.Enqueue((i, targets));
                        recorder.Post(() => Record(plan, unrecorded, hashers));
                    }
                }
            }
            foreach (var h in hashers) h.Wait();
            foreach (var w in ws) w.Wait();
            recorder?.Wait();
            var hashes = SourceHasher.Combine(hashers, files.Count);
            var blocks = (ulong[]?[])hashers[0].Blocks.Clone();
```

Najít (5/6, řádek 119):

```csharp
            var recorded = plan.Expected == null ? hashes : PlaceRepairCopies(plan, ws, hashes, sourceErrors);
```

Nahradit:

```csharp
            var recorded = plan.Expected == null ? hashes : PlaceRepairCopies(plan, ws, hashes, sourceErrors);
            if (plan.Expected != null)
            {
                foreach (var i in Enumerable.Range(0, files.Count).Where(i => ws.Any(w => plan.Writes(i, w.Index))))
                    unrecorded.Enqueue((i, ws.Where(w => plan.Writes(i, w.Index)).ToArray()));
                RecordNow(plan, unrecorded, recorded, blocks);
            }
            Blocks = blocks;
            WriteRates = ws.Select(w => w.WriteRate).ToList();
            WrittenUnbuffered = ws.Select(w => w.Unbuffered).ToList();
```

Najít (6/6, řádky 168–174):

```csharp
        finally
        {
            foreach (var w in ws) w.Dispose();
            foreach (var h in hashers) h.Dispose();
            pool?.Dispose();
        }
    }
```

Nahradit:

```csharp
        finally
        {
            SerialWorker? r;
            lock (gate)
            {
                r = recorder;
                recorder = null;
            }
            r?.Dispose();   // runs what it still has first — before the writers it waits for are gone
            foreach (var w in ws) w.Dispose();
            foreach (var h in hashers) h.Dispose();
            pool?.Dispose();
        }
    }

    /// The recorder's step (spec 4.5): every file finished so far goes into the manifest of each destination where it is
    /// in its place — after the hashers and the writers are done with it; many small files in one write.
    void Record(OffloadPlan plan, System.Collections.Concurrent.ConcurrentQueue<(int File, DestWriter[] Targets)> unrecorded, List<SourceHasher> hashers)
    {
        if (unrecorded.IsEmpty) return;
        foreach (var h in hashers) h.Wait();
        var xx = hashers[0];
        var md5 = hashers.FirstOrDefault(h => h.Kind == HashKind.Md5);
        var batch = new List<(int File, DestWriter[] Targets)>();
        while (unrecorded.TryDequeue(out var item)) batch.Add(item);
        foreach (var w in batch.SelectMany(b => b.Targets).Distinct()) w.Wait();
        var hashes = batch.ToDictionary(b => b.File, b => new FileHashes(
            xx.Values[b.File] is string x ? Convert.ToUInt64(x, 16) : null,
            md5?.Values[b.File] is string m ? Convert.FromHexString(m) : null, xx.Xxh128[b.File]));
        Append(plan, batch, hashes, i => xx.Blocks[i]);
    }

    /// A repair's files, recorded after they took their place, with the hashes the report records.
    void RecordNow(OffloadPlan plan, System.Collections.Concurrent.ConcurrentQueue<(int File, DestWriter[] Targets)> unrecorded,
        FileHashes[] recorded, ulong[]?[] blocks)
    {
        var batch = new List<(int File, DestWriter[] Targets)>();
        while (unrecorded.TryDequeue(out var item)) batch.Add(item);
        Append(plan, batch, batch.ToDictionary(b => b.File, b => recorded[b.File]), i => blocks[i]);
    }

    void Append(OffloadPlan plan, List<(int File, DestWriter[] Targets)> batch, Dictionary<int, FileHashes> hashes, Func<int, ulong[]?> blocksOf)
    {
        foreach (var w in batch.SelectMany(b => b.Targets).Distinct())
        {
            var records = new List<ManifestRecord>();
            foreach (var (i, targets) in batch)
            {
                if (!targets.Contains(w) || w.Results[i].State != FileState.Written) continue;
                if (hashes[i] is not { Xxh64: ulong x } h || blocksOf(i) is not { } blocks) continue;
                var f = plan.Scan.Files[i];
                records.Add(new ManifestRecord
                {
                    Generation = plan.Generation, Path = f.RelPath, Size = f.Size, Modified = f.ModifiedUtc, Xxh64 = Format.Hex64(x),
                    Md5 = h.Md5Hex, Xxh128 = h.Xxh128, BlockSize = Options.ChunkSize, Blocks = ManifestRecord.Hex(blocks),
                });
            }
            // Best effort: without its record a file is only copied again by a resume, never trusted blindly.
            try { ManifestFile.Append(w.CardPath, records); }
            catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { }
        }
    }
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ManifestRecordingTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~RepairEngineTests|FullyQualifiedName~RepairJobTests|FullyQualifiedName~EnginePauseTests|FullyQualifiedName~DestWriterTests"`

Expected: PASS — `Failed: 0, Passed: 96, Skipped: 0, Total: 96` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1525` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 4: zápis na cíle `NO_BUFFERING | WRITE_THROUGH` (R8), rychlost zápisu každého cíle, kontrolní body a záznamy `file` v manifestu během kopie (spec 4.5, R6); manifest se jen připisuje.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: write-through unbuffered writes, write rates, manifest records and checkpoints (plan 10, task 4)

Destination writes use NO_BUFFERING | WRITE_THROUGH. Each writer measures its write rate and
checkpoints its block hashes every 64 blocks; a recorder thread appends a file record per placed
file (a repair records after placing its copies). Append/repair tests treat the manifest as
append-only.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\DestWriter.cs windows\src\Offload.Core\Models.cs windows\src\Offload.Core\NativeIO.cs windows\src\Offload.Core\OffloadEngine.Run.cs windows\src\Offload.Core\OffloadEngine.cs windows\tests\Offload.Core.Tests\AppendEngineTests.cs windows\tests\Offload.Core.Tests\ManifestRecordingTests.cs windows\tests\Offload.Core.Tests\RepairPlannerTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 5: Kontrola čtení mimo cache pro každý cíl (spec 8, R8)

Ověření během proxy smí věřit čtení jen tehdy, když prokazatelně jde z disku: kopie se zapsala mimo cache
(`DestWriter.Unbuffered`) a svazek `FILE_FLAG_NO_BUFFERING` opravdu dodržuje. Kontrola zapíše 1 MiB sondu do `_REPORT`,
zkusí nezarovnané čtení s `NO_BUFFERING` (svazek, který příznak dodržuje, ho odmítne `ERROR_INVALID_PARAMETER` —
naměřeno na NTFS i exFAT) a přečte sondu zpět. Síťový cíl (UNC, namapovaný disk) se nikdy nepoužije (R8). Spec navrhuje
měřit rychlost (disk proti RAM) — na NVMe jsou si příliš blízko (2,5 proti 5,7 GB/s naměřeno), odmítnutí je přesné.
Hlášky dostanou vlastní soubor (`Msg` je teď `partial`). R12: `NativeIO.VolumeSerialText` dá sériové číslo svazku jako
`vol`.

**Files:**
- Create: `windows/src/Offload.Core/VerifyInProxy/UnbufferedCheck.cs`
- Create: `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs`
- Modify: `windows/src/Offload.Core/Messages.cs:47`
- Modify: `windows/src/Offload.Core/NativeIO.cs:172`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/UnbufferedCheckTests.cs` (nový)

**Interfaces:**
- Consumes: `NativeIO.OpenWrite/OpenRead/Read/Write`, `Hardware.UsbNative.GetDriveType`.
- Produces:
  - `UnbufferedCheck.Problem(string cardPath, bool writtenUnbuffered)` → `null` = smí se ověřit během proxy, jinak česky proč ne.
  - `NativeIO.IsRemote(path)`, `NativeIO.VolumeSerialText(path)` (`"1A2B-3C4D"` / `null`).
  - `Msg.ClassicBufferedWrites`, `ClassicNetwork`, `ClassicCacheCheck`, `ClassicCacheCheckFailed(detail)`, `ClassicSetting`, `ClassicSecondary`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/UnbufferedCheckTests.cs` (nový soubor)

```csharp
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 8 "Kontrola za běhu", R8: a destination is verified from the proxy's reads only when its copy was written past
/// the file cache and its volume honours FILE_FLAG_NO_BUFFERING; network volumes never. R12: the volume serial.
public class UnbufferedCheckTests
{
    [Fact]
    public void LocalNtfsTemp_Passes_ProbeRemoved()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-08", "A001");
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));

        Assert.Null(UnbufferedCheck.Problem(card, writtenUnbuffered: true));
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_REPORT")));
    }

    [Fact]
    public void BufferedWrites_Classic()
    {
        using var dir = new TempDir();
        Assert.Equal(Msg.ClassicBufferedWrites, UnbufferedCheck.Problem(dir.Path, writtenUnbuffered: false));
    }

    [Fact]
    public void ProbeCannotBeWritten_Classic_WithTheReason()
    {
        using var dir = new TempDir();
        Assert.StartsWith("kontrolu čtení mimo cache nejde provést", UnbufferedCheck.Problem(Path.Combine(dir.Path, "gone"), writtenUnbuffered: true));
    }

    [Fact]
    public void IsRemote_UncYes_LocalNo()
    {
        Assert.True(NativeIO.IsRemote(@"\\server\share\Zaloha"));
        Assert.False(NativeIO.IsRemote(Path.GetTempPath()));
    }

    [Fact]
    public void VolumeSerialText_FormattedLikeVol()
    {
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}$", NativeIO.VolumeSerialText(Path.GetTempPath()));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~UnbufferedCheckTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0103: The name 'UnbufferedCheck' does not exist in the current context`; `CS0117: 'Msg' does not contain a definition for 'ClassicBufferedWrites'`; `CS0117: 'NativeIO' does not contain a definition for 'IsRemote'` a další chyby téhož druhu (všechny jen v testech tohoto úkolu).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/VerifyInProxy/UnbufferedCheck.cs` (nový soubor)

```csharp
namespace Offload.Core.VerifyInProxy;

/// Spec 8 "Kontrola za běhu" and R8: may this destination be verified from the reads of the proxy (and of the fill-in),
/// or only the classic way? Verify-in-proxy trusts a read only when it provably came from the disk: the copy was written
/// past the file cache (DestWriter.Unbuffered) and the volume honours FILE_FLAG_NO_BUFFERING. Measured on this PC (plan 10
/// prototype, NTFS and exFAT): an unaligned read on a NO_BUFFERING handle is refused with ERROR_INVALID_PARAMETER; a volume
/// that accepts it ignores the flag. Network volumes are never trusted (R8). A speed test (disk speed vs RAM speed, as the
/// spec suggests) is not used: on NVMe the two are too close to tell (2.5 vs 5.7 GB/s measured) — the refusal is exact.
public static class UnbufferedCheck
{
    const int ProbeBytes = 1 << 20;

    /// Null when the destination of `cardPath` passes; else the Czech reason it is verified the classic way (the report
    /// says it per destination).
    public static string? Problem(string cardPath, bool writtenUnbuffered)
    {
        if (!writtenUnbuffered) return Msg.ClassicBufferedWrites;
        if (NativeIO.IsRemote(cardPath)) return Msg.ClassicNetwork;
        var probe = Path.Combine(cardPath, Layout.ReportDirName, $".offload-cachecheck-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            using var buffer = new AlignedBuffer(ProbeBytes);
            new Random(ProbeBytes).NextBytes(buffer.Span);
            using (var w = NativeIO.OpenWrite(probe, unbuffered: true, ProbeBytes))
                NativeIO.Write(w, buffer.Span, 0, probe);
            using var r = NativeIO.OpenRead(probe, unbuffered: true);
            using var back = new AlignedBuffer(ProbeBytes);
            try
            {
                NativeIO.Read(r, back.Span[..4096], 1, probe);   // unaligned: a volume honouring the flag refuses it
                return Msg.ClassicCacheCheck;
            }
            catch (IOError e) when (e.Code == NativeIO.ErrorInvalidParameter) { }
            int n = NativeIO.Read(r, back.Span, 0, probe);
            return n == ProbeBytes && back.Span.SequenceEqual(buffer.Span) ? null : Msg.ClassicCacheCheck;
        }
        catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException)
        {
            return Msg.ClassicCacheCheckFailed(e.Message);
        }
        finally
        {
            NativeIO.Delete(probe);
        }
    }
}
```

#### `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs` (nový soubor)

```csharp
namespace Offload.Core;

/// Plan 10 (verify-in-proxy): the Czech texts of the report, the job card and the log.
public static partial class Msg
{
    // Why a destination is verified the classic way (report, per destination).
    public const string ClassicBufferedWrites = "svazek nepřijal zápis mimo cache — ověřeno zpětným čtením";
    public const string ClassicNetwork = "síťový cíl — ověřeno zpětným čtením";
    public const string ClassicCacheCheck = "kontrola čtení mimo cache neprošla — ověřeno zpětným čtením";
    public static string ClassicCacheCheckFailed(string detail) => $"kontrolu čtení mimo cache nejde provést ({detail}) — ověřeno zpětným čtením";
    public const string ClassicSetting = "ověření během proxy je vypnuté — ověřeno zpětným čtením";
    public const string ClassicSecondary = "další cíl — ověřeno čtením na pozadí";
}
```

#### `windows/src/Offload.Core/Messages.cs`

Najít (1/1, řádek 47):

```csharp
public static class Msg
```

Nahradit:

```csharp
public static partial class Msg
```

#### `windows/src/Offload.Core/NativeIO.cs`

Najít (1/1, řádek 172):

```csharp
    public static long? AvailableBytes(string path) =>
```

Nahradit:

```csharp
    /// A network volume — a UNC path or a mapped drive (R8: never verified from the proxy's reads).
    public static bool IsRemote(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        return VolumeRoot(full) is string root && Hardware.UsbNative.GetDriveType(root) == Hardware.UsbNative.DriveRemote;
    }

    /// The volume serial as the report keeps it (R12): "1A2B-3C4D", the way `vol` prints it; null when unknown.
    public static string? VolumeSerialText(string path) =>
        VolumeSerial(path) is uint s ? $"{s >> 16:X4}-{s & 0xFFFF:X4}" : null;

    public static long? AvailableBytes(string path) =>
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~UnbufferedCheckTests"`

Expected: PASS — `Failed: 0, Passed: 5, Skipped: 0, Total: 5` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1530` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 5: kontrola čtení mimo cache pro každý cíl (nezarovnané čtení s `NO_BUFFERING` musí svazek odmítnout, sonda v `_REPORT`); síťové cíle a cíle zapsané přes cache se ověřují zpětným čtením (R8); sériové číslo svazku (R12).
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: per-destination check that reads bypass the cache (plan 10, task 5)

UnbufferedCheck writes a 1 MiB probe into _REPORT and requires the volume to refuse an unaligned
NO_BUFFERING read; buffered writes, network volumes and failing checks fall back to read-back with
the reason. NativeIO gets IsRemote and VolumeSerialText (R12).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Messages.cs windows\src\Offload.Core\NativeIO.cs windows\src\Offload.Core\VerifyInProxy\Msg.VerifyInProxy.cs windows\src\Offload.Core\VerifyInProxy\UnbufferedCheck.cs windows\tests\Offload.Core.Tests\UnbufferedCheckTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 6: Odložené ověření: reporty „verifyPending“, MHL až po ověření, pole reportu (R5, R7, spec 9)

Úloha s ověřením během proxy nesmí po kopii hned číst zpět: kopie zapíše reporty s `verifyPending: true` a bez
generace ASC MHL a bez `.xxh64`/`.md5` — generaci zapíše až `OffloadReport.Commit` po ověření (úkol 12). Report nikdy
netvrdí „ověřeno“ dřív, než ověření skončí. Zrušená kopie, změněná karta a oprava se zapisují postaru (hned, jako
dosud). Report dostane pole ze spec 9: `verifyMethod`/`verifyNote` a `volumeSerial` (R12) u každého cíle,
`safeToFormatSeconds`, `verifyNotes`. MHL nese metodu v komentáři generace (`<process>` smí podle schématu ASC MHL v2
být jen `in-place`/`transfer`/`flatten`, proto zůstává `transfer`) a do ověřené generace nejde stav `written`.
`OffloadReport.SafeToEject` se přejmenuje na `SafeToFormat` (R5: „lze vyjmout“ a „lze formátovat“ jsou dvě různé věci;
reportová vlastnost vždy znamenala „ověřeno“).

**Files:**
- Modify: `windows/src/Offload.Core/OffloadReport.cs:12-21, 40-47, 67-74, 86-91, 153-158, 176-181, 272-278, 285-291, 311-316, 125-126`
- Modify: `windows/src/Offload.Core/Models.cs:16-21`
- Modify: `windows/src/Offload.Core/OffloadEngine.Run.cs:133-139, 152-158, 165`
- Modify: `windows/src/Offload.Core/Mhl.cs:47-52, 86-91`
- Modify: `windows/src/Offload.Core/OffloadJob.cs:319`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/DeferredVerifyTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/EngineTests.cs:41-47, 81-87, 233-239, 245-251, 261-267, 275-281, 292-298, 314-320, 332-338, 357-363, 375-381`
- Test: `windows/tests/Offload.Core.Tests/EnginePauseTests.cs:58-64, 84-90, 103-109, 247-253, 278-284, 298-304`
- Test: `windows/tests/Offload.Core.Tests/RepairEngineTests.cs:38-44, 165-171, 213-219`
- Test: `windows/tests/Offload.Core.Tests/ReportTests.cs:126-135, 151-157, 174-180`
- Test: `windows/tests/Offload.Core.Tests/RepairJobTests.cs:215-221, 409`
- Test: `windows/tests/Offload.Core.Tests/AppendEngineTests.cs:319`

**Interfaces:**
- Consumes: `OffloadReport.Write(cardPath, includeHashRecords)`, `MhlWriter`, `NativeIO.VolumeSerialText` (úkol 5).
- Produces:
  - `OffloadOptions.DeferVerify` (internal): kopie vynechá zpětné čtení, report `VerifyPending = true`, bez MHL a seznamů hashů.
  - `OffloadReport`: `VerifyPending`, `SafeToFormatSeconds`, `VerifyNotes`, `SafeToFormat` (dřív `SafeToEject`), `Write(cardPath, includeHashRecords, bool? createNew = null)`, `Commit(cardPath)`; `Verified`, `Finished`, `Destinations`, `Errors` mají `set`.
  - `ReportDestination`: `VerifyMethod` (`InProxy = "verify-in-proxy"`, `ReadBack = "read-back"`), `VerifyNote`, `VolumeSerial`; `FilesOK`/`FilesFailed` mají `set`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/DeferredVerifyTests.cs` (nový soubor)

```csharp
using System.Text.Json;
using Offload.Core.Append;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Plan 10: a copy whose verification runs with the proxies leaves the read-back out, writes its reports with
/// verifyPending and no ASC MHL generation (only verified files go into the MHL, spec 9); OffloadReport.Commit writes the
/// generation once verified. The classic report gets its method, the destination's serial (R12) and the time to "Lze
/// formátovat" (spec 9).
public class DeferredVerifyTests
{
    const int Block = 64 * 1024;

    static OffloadOptions Options(bool defer) => new()
    {
        ChunkSize = Block, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, DeferVerify = defer,
    };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 3 * Block + 7, 1);
        TestFs.WriteRandom(Path.Combine(card, "b.wav"), 1000, 2);
        return card;
    }

    static JsonElement Json(string cardPath, string name = "offload_report.json") =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(cardPath, "_REPORT", name))).RootElement.Clone();

    [Fact]
    public void Deferred_ReportsPending_NoMhlNoHashList_NeitherGreenNorRed()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: true));
        var plan = engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08");

        var report = engine.Run(plan);

        Assert.True(report.VerifyPending);
        Assert.False(report.Verified);
        Assert.True(report.Success);   // written counts as good while not verified — never red
        Assert.False(report.SafeToFormat);
        Assert.All(report.Files, f => Assert.Equal(["written"], f.Destinations));
        var p = plan.CardPaths[0];
        Assert.True(Json(p).GetProperty("verifyPending").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(p, "ascmhl")) && Directory.GetFiles(Path.Combine(p, "ascmhl")).Length > 0);
        Assert.False(File.Exists(Path.Combine(p, "_REPORT", "A001.xxh64")));
        Assert.All(engine.Snapshot().VerifiedBytes, v => Assert.Equal(0, v));   // nothing read back
        var record = CardRecord.Load(p);
        Assert.False(record.Verified);
        Assert.Empty(record.FailedFiles);
        Assert.Equal(new BackupState(0, 2), BackupState.Of([p]));
    }

    [Fact]
    public void Commit_WritesTheGeneration_ReplacesTheReports()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: true));
        var plan = engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08");
        var report = engine.Run(plan);
        foreach (var f in report.Files) f.Destinations = ["verified"];
        report.Verified = true;
        report.VerifyPending = null;

        report.Commit(plan.CardPaths[0]);

        var p = plan.CardPaths[0];
        Assert.True(Json(p).GetProperty("verified").GetBoolean());
        Assert.False(Json(p).TryGetProperty("verifyPending", out _));
        Assert.Single(Directory.GetFiles(Path.Combine(p, "ascmhl"), "0001_*.mhl"));
        Assert.True(File.Exists(Path.Combine(p, "_REPORT", "A001.xxh64")));
        Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
        Assert.True(CardRecord.Load(p).Verified);
    }

    // An append's reports are created new at the end of its copy (never over an earlier generation's, ruling I1) and
    // replaced by its own commit — the MHL generation file only then.
    [Fact]
    public void Commit_Append_Generation2()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var full = new OffloadEngine(Options(defer: false));
        var paths = full.Run(full.Prepare(card, [dir.Sub("d1")], null, "2026-10-08")).Destinations.Select(d => d.CardPath).ToList();
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), Block, 3);
        var engine = new OffloadEngine(Options(defer: true));
        var plan = engine.PrepareAppend(card, paths);

        var report = engine.Run(plan);

        Assert.Equal(2, plan.Generation);
        Assert.True(Json(paths[0], "offload_report_0002.json").GetProperty("verifyPending").GetBoolean());
        Assert.Empty(Directory.GetFiles(Path.Combine(paths[0], "ascmhl"), "0002_*.mhl"));
        foreach (var f in report.Files) f.Destinations = ["verified"];
        report.Verified = true;
        report.VerifyPending = null;
        report.Commit(paths[0]);
        Assert.Single(Directory.GetFiles(Path.Combine(paths[0], "ascmhl"), "0002_*.mhl"));
        Assert.True(BackupVerifier.Verify(paths[0], chunkSize: 4096).Success);
    }

    // Cancelled before the read-back: nothing to defer — the generation is written now, unverified (as before plan 10).
    [Fact]
    public void Cancelled_NotDeferred_WrittenAsBefore()
    {
        using var dir = new TempDir();
        var options = Options(defer: true);
        var engine = new OffloadEngine(options);
        options.BeforeVerify = engine.Cancel;
        var plan = engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08");

        var report = engine.Run(plan);

        Assert.Null(report.VerifyPending);
        Assert.False(report.Verified);
        Assert.Single(Directory.GetFiles(Path.Combine(plan.CardPaths[0], "ascmhl"), "*.mhl"));
    }

    [Fact]
    public void Classic_ReportFields_MethodSerialTimeAndMhlComment()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: false));
        var plan = engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08");
        var report = engine.Run(plan);

        var dest = Json(plan.CardPaths[0]).GetProperty("destinations")[0];
        Assert.Equal("read-back", dest.GetProperty("verifyMethod").GetString());
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}$", dest.GetProperty("volumeSerial").GetString());
        Assert.True(report.SafeToFormat);
        Assert.True(report.SafeToFormatSeconds >= 0);
        var mhl = File.ReadAllText(Directory.GetFiles(Path.Combine(plan.CardPaths[0], "ascmhl"), "*.mhl").Single());
        Assert.Contains("· ověření: read-back</comment>", mhl);
        Assert.Contains("<process>transfer</process>", mhl);
    }
}
```

#### `windows/tests/Offload.Core.Tests/EngineTests.cs`

Najít (1/11, řádky 41–47):

```csharp

        var report = engine.Run(plan);

        Assert.True(report.SafeToEject);
        Assert.Equal(Sizes.Length, report.TotalFiles);
        foreach (var cardPath in plan.CardPaths)
        {
```

Nahradit:

```csharp

        var report = engine.Run(plan);

        Assert.True(report.SafeToFormat);
        Assert.Equal(Sizes.Length, report.TotalFiles);
        foreach (var cardPath in plan.CardPaths)
        {
```

Najít (2/11, řádky 81–87):

```csharp
        // A block that never comes back to the pool hangs the copy: TimeoutException instead of a hung test run.
        var report = await Task.Run(() => engine.Run(plan)).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToEject);
        foreach (var cardPath in plan.CardPaths)
            foreach (var (rel, data) in files)
                Assert.Equal(data, File.ReadAllBytes(Layout.Join(cardPath, rel)));
```

Nahradit:

```csharp
        // A block that never comes back to the pool hangs the copy: TimeoutException instead of a hung test run.
        var report = await Task.Run(() => engine.Run(plan)).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToFormat);
        foreach (var cardPath in plan.CardPaths)
            foreach (var (rel, data) in files)
                Assert.Equal(data, File.ReadAllBytes(Layout.Join(cardPath, rel)));
```

Najít (3/11, řádky 233–239):

```csharp
        var data = TestFs.WriteRandom(Path.Combine(card, "big.mov"), (16 << 20) + 1, 42);
        var engine = new OffloadEngine(new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true });
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        Assert.True(engine.Run(plan).SafeToEject);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(plan.CardPaths[0], "big.mov")));
    }

```

Nahradit:

```csharp
        var data = TestFs.WriteRandom(Path.Combine(card, "big.mov"), (16 << 20) + 1, 42);
        var engine = new OffloadEngine(new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true });
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        Assert.True(engine.Run(plan).SafeToFormat);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(plan.CardPaths[0], "big.mov")));
    }

```

Najít (4/11, řádky 245–251):

```csharp
        MakeCard(card);
        var engine = new OffloadEngine(Options(forceBuffered: true));
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04"));
        Assert.True(report.SafeToEject);
        Assert.False(report.Destinations[0].Unbuffered);
        Assert.Contains("(buffered I/O)", report.Summary);
    }
```

Nahradit:

```csharp
        MakeCard(card);
        var engine = new OffloadEngine(Options(forceBuffered: true));
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04"));
        Assert.True(report.SafeToFormat);
        Assert.False(report.Destinations[0].Unbuffered);
        Assert.Contains("(buffered I/O)", report.Summary);
    }
```

Najít (5/11, řádky 261–267):

```csharp
        var engine = new OffloadEngine(options);
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04"));
        Assert.True(report.Success);
        Assert.False(report.SafeToEject);
        Assert.All(report.Files, f => Assert.Equal(["written"], f.Destinations));
    }

```

Nahradit:

```csharp
        var engine = new OffloadEngine(options);
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04"));
        Assert.True(report.Success);
        Assert.False(report.SafeToFormat);
        Assert.All(report.Files, f => Assert.Equal(["written"], f.Destinations));
    }

```

Najít (6/11, řádky 275–281):

```csharp
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        engine.Cancel();
        var report = engine.Run(plan);
        Assert.False(report.SafeToEject);
        Assert.All(report.Files, f => Assert.Equal("cancelled", f.SourceError));
        Assert.Empty(Directory.GetFiles(Path.Combine(plan.CardPaths[0], "CLIP")));
    }
```

Nahradit:

```csharp
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        engine.Cancel();
        var report = engine.Run(plan);
        Assert.False(report.SafeToFormat);
        Assert.All(report.Files, f => Assert.Equal("cancelled", f.SourceError));
        Assert.Empty(Directory.GetFiles(Path.Combine(plan.CardPaths[0], "CLIP")));
    }
```

Najít (7/11, řádky 292–298):

```csharp
        while (engine.Snapshot().ReadBytes == 0 && !run.IsCompleted) await Task.Delay(1);
        engine.Cancel();
        var report = await run;
        Assert.False(report.SafeToEject);
        Assert.False(report.Verified);
        AssertNoPartFiles(dir.Path);
    }
```

Nahradit:

```csharp
        while (engine.Snapshot().ReadBytes == 0 && !run.IsCompleted) await Task.Delay(1);
        engine.Cancel();
        var report = await run;
        Assert.False(report.SafeToFormat);
        Assert.False(report.Verified);
        AssertNoPartFiles(dir.Path);
    }
```

Najít (8/11, řádky 314–320):

```csharp
        Assert.StartsWith("source size changed during copy", bad.SourceError);
        Assert.Equal(["failed"], bad.Destinations);
        Assert.All(report.Files.Where(f => f.Path != "CLIP/F08.mov"), f => Assert.Equal(["verified"], f.Destinations));
        Assert.False(report.SafeToEject);
        Assert.False(File.Exists(Path.Combine(plan.CardPaths[0], "CLIP", "F08.mov")));
        AssertNoPartFiles(dir.Path);
    }
```

Nahradit:

```csharp
        Assert.StartsWith("source size changed during copy", bad.SourceError);
        Assert.Equal(["failed"], bad.Destinations);
        Assert.All(report.Files.Where(f => f.Path != "CLIP/F08.mov"), f => Assert.Equal(["verified"], f.Destinations));
        Assert.False(report.SafeToFormat);
        Assert.False(File.Exists(Path.Combine(plan.CardPaths[0], "CLIP", "F08.mov")));
        AssertNoPartFiles(dir.Path);
    }
```

Najít (9/11, řádky 332–338):

```csharp

        var report = engine.Run(plan);

        Assert.False(report.SafeToEject);
        Assert.Equal(0, report.Destinations[0].FilesFailed);
        Assert.Equal(1, report.Destinations[1].FilesFailed);
        var f = report.Files.Single(x => x.Path == "CLIP/F03.mov");
```

Nahradit:

```csharp

        var report = engine.Run(plan);

        Assert.False(report.SafeToFormat);
        Assert.Equal(0, report.Destinations[0].FilesFailed);
        Assert.Equal(1, report.Destinations[1].FilesFailed);
        var f = report.Files.Single(x => x.Path == "CLIP/F03.mov");
```

Najít (10/11, řádky 357–363):

```csharp
        var second = new OffloadEngine(Options());
        var plan2 = second.Prepare(card, [d1], null, "2026-10-04");
        Assert.Equal("A001_2", plan2.CardName);
        Assert.True(second.Run(plan2).SafeToEject);
        Assert.Equal(firstJson, File.ReadAllText(Path.Combine(plan1.CardPaths[0], "_REPORT", "offload_report.json")));
    }

```

Nahradit:

```csharp
        var second = new OffloadEngine(Options());
        var plan2 = second.Prepare(card, [d1], null, "2026-10-04");
        Assert.Equal("A001_2", plan2.CardName);
        Assert.True(second.Run(plan2).SafeToFormat);
        Assert.Equal(firstJson, File.ReadAllText(Path.Combine(plan1.CardPaths[0], "_REPORT", "offload_report.json")));
    }

```

Najít (11/11, řádky 375–381):

```csharp

        var report = engine.Run(plan);

        Assert.True(report.SafeToEject);
        var cardPath = plan.CardPaths[0];
        Assert.Equal(a, File.ReadAllBytes(Path.Combine(cardPath, "Záběr 01", "klip č.1.mov")));
        var list = File.ReadAllText(Path.Combine(cardPath, "_REPORT", "Natáčení den 1.xxh64"), Encoding.UTF8);
```

Nahradit:

```csharp

        var report = engine.Run(plan);

        Assert.True(report.SafeToFormat);
        var cardPath = plan.CardPaths[0];
        Assert.Equal(a, File.ReadAllBytes(Path.Combine(cardPath, "Záběr 01", "klip č.1.mov")));
        var list = File.ReadAllText(Path.Combine(cardPath, "_REPORT", "Natáčení den 1.xxh64"), Encoding.UTF8);
```

#### `windows/tests/Offload.Core.Tests/EnginePauseTests.cs`

Najít (1/6, řádky 58–64):

```csharp
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToEject);
        Assert.False(engine.Snapshot().Paused);
        Assert.True(report.CopyPausedSeconds >= 0.6, $"{report.CopyPausedSeconds}");
        Assert.Null(report.VerifyPausedSeconds);
```

Nahradit:

```csharp
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToFormat);
        Assert.False(engine.Snapshot().Paused);
        Assert.True(report.CopyPausedSeconds >= 0.6, $"{report.CopyPausedSeconds}");
        Assert.Null(report.VerifyPausedSeconds);
```

Najít (2/6, řádky 84–90):

```csharp
        Assert.InRange(held.ReadBytes, 2L * Chunk, (2L + options.BufferCount + 1) * Chunk);   // mid-file, one pool ahead at most
        engine.Pauses.Resume();

        Assert.True((await run.WaitAsync(TimeSpan.FromSeconds(60))).SafeToEject);
    }

    [Fact]
```

Nahradit:

```csharp
        Assert.InRange(held.ReadBytes, 2L * Chunk, (2L + options.BufferCount + 1) * Chunk);   // mid-file, one pool ahead at most
        engine.Pauses.Resume();

        Assert.True((await run.WaitAsync(TimeSpan.FromSeconds(60))).SafeToFormat);
    }

    [Fact]
```

Najít (3/6, řádky 103–109):

```csharp
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToEject);
        Assert.True(report.VerifyPausedSeconds >= 0.3, $"{report.VerifyPausedSeconds}");
        Assert.Null(report.CopyPausedSeconds);
    }
```

Nahradit:

```csharp
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToFormat);
        Assert.True(report.VerifyPausedSeconds >= 0.3, $"{report.VerifyPausedSeconds}");
        Assert.Null(report.CopyPausedSeconds);
    }
```

Najít (4/6, řádky 247–253):

```csharp
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(job.Report!.SafeToEject);
        Assert.Equal(3, Volatile.Read(ref reads));   // at the start, the failed read after the pause, the retry
    }

```

Nahradit:

```csharp
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(job.Report!.SafeToFormat);
        Assert.Equal(3, Volatile.Read(ref reads));   // at the start, the failed read after the pause, the retry
    }

```

Najít (5/6, řádky 278–284):

```csharp
        Assert.Equal([held.ReadBytes, held.ReadBytes], held.WrittenBytes);
        engine.Pauses.Resume();

        Assert.True((await run.WaitAsync(TimeSpan.FromSeconds(60))).SafeToEject);
    }

    // A folder source has no card identity (null when the copy started): nothing to compare, the copy goes on.
```

Nahradit:

```csharp
        Assert.Equal([held.ReadBytes, held.ReadBytes], held.WrittenBytes);
        engine.Pauses.Resume();

        Assert.True((await run.WaitAsync(TimeSpan.FromSeconds(60))).SafeToFormat);
    }

    // A folder source has no card identity (null when the copy started): nothing to compare, the copy goes on.
```

Najít (6/6, řádky 298–304):

```csharp
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(job.Report!.SafeToEject);
        Assert.Equal(1, Volatile.Read(ref reads));
    }

```

Nahradit:

```csharp
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(job.Report!.SafeToFormat);
        Assert.Equal(1, Volatile.Read(ref reads));
    }

```

#### `windows/tests/Offload.Core.Tests/RepairEngineTests.cs`

Najít (1/3, řádky 38–44):

```csharp

        var report = engine.Run(plan);

        Assert.True(report.SafeToEject);
        Assert.Equal((PlanMode.Repair, 2, "repair"), (plan.Mode, report.Generation!.Value, report.Kind));
        var f = Assert.Single(report.Files);
        Assert.Equal(RepairFixtures.Bad, f.Path);
```

Nahradit:

```csharp

        var report = engine.Run(plan);

        Assert.True(report.SafeToFormat);
        Assert.Equal((PlanMode.Repair, 2, "repair"), (plan.Mode, report.Generation!.Value, report.Kind));
        var f = Assert.Single(report.Files);
        Assert.Equal(RepairFixtures.Bad, f.Path);
```

Najít (2/3, řádky 165–171):

```csharp
        File.WriteAllBytes(cardFile, original);
        File.SetLastWriteTimeUtc(cardFile, time);
        var (last, lastPlan) = Prepare(card, paths);
        Assert.True(last.Run(lastPlan).SafeToEject);
        Assert.Equal(before[0][RepairFixtures.Bad].Bytes, File.ReadAllBytes(Layout.Join(paths[0], RepairFixtures.Bad)));
        Assert.Equal(before[1][RepairFixtures.Bad].Bytes, File.ReadAllBytes(Layout.Join(paths[1], "_REPORT/neshoda_0005/CLIP/A001C002.mov")));
        foreach (var p in paths) Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
```

Nahradit:

```csharp
        File.WriteAllBytes(cardFile, original);
        File.SetLastWriteTimeUtc(cardFile, time);
        var (last, lastPlan) = Prepare(card, paths);
        Assert.True(last.Run(lastPlan).SafeToFormat);
        Assert.Equal(before[0][RepairFixtures.Bad].Bytes, File.ReadAllBytes(Layout.Join(paths[0], RepairFixtures.Bad)));
        Assert.Equal(before[1][RepairFixtures.Bad].Bytes, File.ReadAllBytes(Layout.Join(paths[1], "_REPORT/neshoda_0005/CLIP/A001C002.mov")));
        foreach (var p in paths) Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
```

Najít (3/3, řádky 213–219):

```csharp
        Assert.Equal([false, true], next.Write[0]);
        Assert.Equal("_REPORT/neshoda_0003/CLIP/A001C002.mov", next.SetAside[0][1]);
        var (engine2, plan2) = Prepare(card, paths);
        Assert.True(engine2.Run(plan2).SafeToEject);
        Assert.Equal(goodTime, File.GetLastWriteTimeUtc(good));
        foreach (var p in paths) Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
    }
```

Nahradit:

```csharp
        Assert.Equal([false, true], next.Write[0]);
        Assert.Equal("_REPORT/neshoda_0003/CLIP/A001C002.mov", next.SetAside[0][1]);
        var (engine2, plan2) = Prepare(card, paths);
        Assert.True(engine2.Run(plan2).SafeToFormat);
        Assert.Equal(goodTime, File.GetLastWriteTimeUtc(good));
        foreach (var p in paths) Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
    }
```

#### `windows/tests/Offload.Core.Tests/ReportTests.cs`

Najít (1/3, řádky 126–135):

```csharp
    [Fact]
    public void SuccessAndEject()
    {
        Assert.True(Reports.Sample(Card).SafeToEject);
        var notVerified = Reports.Sample(Card, verified: false);
        Assert.True(notVerified.Success);
        Assert.False(notVerified.SafeToEject);
        var failed = Reports.Sample(Card, destError: "boom");
        Assert.False(failed.Success);
        Assert.Equal(["CLIP/c 02.mov"], failed.FailedFiles.Select(f => f.Path));
```

Nahradit:

```csharp
    [Fact]
    public void SuccessAndEject()
    {
        Assert.True(Reports.Sample(Card).SafeToFormat);
        var notVerified = Reports.Sample(Card, verified: false);
        Assert.True(notVerified.Success);
        Assert.False(notVerified.SafeToFormat);
        var failed = Reports.Sample(Card, destError: "boom");
        Assert.False(failed.Success);
        Assert.Equal(["CLIP/c 02.mov"], failed.FailedFiles.Select(f => f.Path));
```

Najít (2/3, řádky 151–157):

```csharp
            Verified = false, Destinations = r.Destinations, Files = files, Skipped = [],
        };
        Assert.True(cancelled.Success);
        Assert.False(cancelled.SafeToEject);
        Assert.Empty(cancelled.FailedFiles);
    }

```

Nahradit:

```csharp
            Verified = false, Destinations = r.Destinations, Files = files, Skipped = [],
        };
        Assert.True(cancelled.Success);
        Assert.False(cancelled.SafeToFormat);
        Assert.Empty(cancelled.FailedFiles);
    }

```

Najít (3/3, řádky 174–180):

```csharp
        Assert.Equal(2, r.Destinations[0].FilesOK);
        Assert.Equal(0, r.Destinations[0].FilesFailed);
        Assert.True(r.Success);
        Assert.False(r.SafeToEject);
    }

    [Fact]
```

Nahradit:

```csharp
        Assert.Equal(2, r.Destinations[0].FilesOK);
        Assert.Equal(0, r.Destinations[0].FilesFailed);
        Assert.True(r.Success);
        Assert.False(r.SafeToFormat);
    }

    [Fact]
```

#### `windows/tests/Offload.Core.Tests/RepairJobTests.cs`

Najít (1/2, řádky 215–221):

```csharp

        var s = repair.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(repair.Report!.SafeToEject);
        Assert.False(s.SafeToEject);
        Assert.Equal("Na kartě je 1 soubor, který záloha nemá — kartu nevysouvejte, zálohu doplňte.", s.Warning);
        Assert.Equal("Na kartě jsou 3 soubory, které záloha nemá — kartu nevysouvejte, zálohu doplňte.", Msg.CardFilesNotInBackup(3));
```

Nahradit:

```csharp

        var s = repair.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(repair.Report!.SafeToFormat);
        Assert.False(s.SafeToEject);
        Assert.Equal("Na kartě je 1 soubor, který záloha nemá — kartu nevysouvejte, zálohu doplňte.", s.Warning);
        Assert.Equal("Na kartě jsou 3 soubory, které záloha nemá — kartu nevysouvejte, zálohu doplňte.", Msg.CardFilesNotInBackup(3));
```

Najít (2/2, řádek 409):

```csharp
        Assert.True(append.Report!.SafeToEject);
```

Nahradit:

```csharp
        Assert.True(append.Report!.SafeToFormat);
```

#### `windows/tests/Offload.Core.Tests/AppendEngineTests.cs`

Najít (1/1, řádek 319):

```csharp
        Assert.True(append.Report!.SafeToEject);
```

Nahradit:

```csharp
        Assert.True(append.Report!.SafeToFormat);
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~DeferredVerifyTests|FullyQualifiedName~EngineTests|FullyQualifiedName~EnginePauseTests|FullyQualifiedName~RepairEngineTests|FullyQualifiedName~RepairJobTests|FullyQualifiedName~ReportTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~MhlTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0117: 'OffloadOptions' does not contain a definition for 'DeferVerify'`; `CS1061: 'OffloadReport' does not contain a definition for 'VerifyPending' and no accessible extension method 'VerifyPending' accepting a first argument of type 'OffloadReport' could be found (are you missing a using directive or an assembly reference?)`; `CS1061: 'OffloadReport' does not contain a definition for 'SafeToFormat' and no accessible extension method 'SafeToFormat' accepting a first argument of type 'OffloadReport' could be found (are you missing a using directive or an assembly reference?)` a další chyby téhož druhu (všechny jen v testech tohoto úkolu).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/OffloadReport.cs`

Najít (1/10, řádky 12–21):

```csharp
    [JsonPropertyName("root")] public required string Root { get; init; }
    [JsonPropertyName("cardPath")] public required string CardPath { get; init; }
    [JsonPropertyName("filesystem")] public required string Filesystem { get; init; }
    [JsonPropertyName("filesOK")] public int FilesOK { get; init; }
    [JsonPropertyName("filesFailed")] public int FilesFailed { get; init; }
    /// Present (false) only when the volume refused unbuffered writes and the file cache was used.
    [JsonPropertyName("unbuffered")] public bool? Unbuffered { get; init; }
}

public sealed class ReportFile
```

Nahradit:

```csharp
    [JsonPropertyName("root")] public required string Root { get; init; }
    [JsonPropertyName("cardPath")] public required string CardPath { get; init; }
    [JsonPropertyName("filesystem")] public required string Filesystem { get; init; }
    [JsonPropertyName("filesOK")] public int FilesOK { get; set; }
    [JsonPropertyName("filesFailed")] public int FilesFailed { get; set; }
    /// Present (false) only when the volume refused unbuffered writes and the file cache was used.
    [JsonPropertyName("unbuffered")] public bool? Unbuffered { get; init; }
    /// Plan 10 (spec 9): how this destination was verified — "verify-in-proxy" (the proxy's reads and the fill-in) or
    /// "read-back" (every file read again); absent while not verified (yet). The Mac ignores it.
    [JsonPropertyName("verifyMethod")] public string? VerifyMethod { get; set; }
    /// Why it was verified by read-back although verify-in-proxy ran (Czech, e.g. a network volume); absent otherwise.
    [JsonPropertyName("verifyNote")] public string? VerifyNote { get; set; }
    /// R12: the volume's serial number at the copy ("1A2B-3C4D") — which disk it really was, whatever its letter is now.
    [JsonPropertyName("volumeSerial")] public string? VolumeSerial { get; init; }

    public const string InProxy = "verify-in-proxy";
    public const string ReadBack = "read-back";
}

public sealed class ReportFile
```

Najít (2/10, řádky 40–47):

```csharp
    /// older reports and for a file the card could not give. The Mac ignores it.
    [JsonPropertyName("xxh128")] public string? Xxh128 { get; init; }
    [JsonPropertyName("sourceError")] public string? SourceError { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<string> Destinations { get; init; }
    [JsonPropertyName("errors")] public required IReadOnlyList<string?> Errors { get; init; }
    /// Repair only (Windows from 0.5.0): per destination, true = a new copy was written there, false = the good copy there
    /// was only read back (its state carries on the earlier one). The Mac ignores it.
    [JsonPropertyName("rewritten")] public IReadOnlyList<bool>? Rewritten { get; init; }
```

Nahradit:

```csharp
    /// older reports and for a file the card could not give. The Mac ignores it.
    [JsonPropertyName("xxh128")] public string? Xxh128 { get; init; }
    [JsonPropertyName("sourceError")] public string? SourceError { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<string> Destinations { get; set; }
    [JsonPropertyName("errors")] public required IReadOnlyList<string?> Errors { get; set; }
    /// Repair only (Windows from 0.5.0): per destination, true = a new copy was written there, false = the good copy there
    /// was only read back (its state carries on the earlier one). The Mac ignores it.
    [JsonPropertyName("rewritten")] public IReadOnlyList<bool>? Rewritten { get; init; }
```

Najít (3/10, řádky 67–74):

```csharp
    [JsonPropertyName("sourceFilesystem")] public required string SourceFilesystem { get; init; }
    [JsonPropertyName("started")] public DateTime Started { get; init; }
    [JsonPropertyName("copyFinished")] public DateTime CopyFinished { get; init; }
    [JsonPropertyName("finished")] public DateTime Finished { get; init; }
    [JsonPropertyName("verified")] public bool Verified { get; init; }
    [JsonPropertyName("totalFiles")] public int TotalFiles { get; init; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<ReportDestination> Destinations { get; init; }
```

Nahradit:

```csharp
    [JsonPropertyName("sourceFilesystem")] public required string SourceFilesystem { get; init; }
    [JsonPropertyName("started")] public DateTime Started { get; init; }
    [JsonPropertyName("copyFinished")] public DateTime CopyFinished { get; init; }
    [JsonPropertyName("finished")] public DateTime Finished { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    [JsonPropertyName("totalFiles")] public int TotalFiles { get; init; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<ReportDestination> Destinations { get; init; }
```

Najít (4/10, řádky 86–91):

```csharp
    /// leave the pause out; the Mac ignores the keys (its speeds then include it).
    [JsonPropertyName("copyPausedSeconds")] public double? CopyPausedSeconds { get; set; }
    [JsonPropertyName("verifyPausedSeconds")] public double? VerifyPausedSeconds { get; set; }
    /// "repair" for "Zkopírovat znovu" (Windows from 0.5.0); absent for a full copy and an append. The Mac ignores it.
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    /// Repair: the non-matching copies moved aside (`_REPORT/neshoda_NNNN/…`); absent when none was.
```

Nahradit:

```csharp
    /// leave the pause out; the Mac ignores the keys (its speeds then include it).
    [JsonPropertyName("copyPausedSeconds")] public double? CopyPausedSeconds { get; set; }
    [JsonPropertyName("verifyPausedSeconds")] public double? VerifyPausedSeconds { get; set; }
    /// Plan 10: the copy is done and its verification runs with the proxies (stage 2) — written at the end of the copy,
    /// gone once verified. A report saying so has no ASC MHL generation yet (OffloadReport.Commit). Absent otherwise.
    [JsonPropertyName("verifyPending")] public bool? VerifyPending { get; set; }
    /// Spec 9: seconds from the start until every file was verified on every destination (pauses left out); absent when
    /// that was never reached. The Mac ignores it.
    [JsonPropertyName("safeToFormatSeconds")] public double? SafeToFormatSeconds { get; set; }
    /// Spec 10 (Czech): unstable reads, a block wrong on several destinations, what the card gave for it again.
    [JsonPropertyName("verifyNotes")] public List<string>? VerifyNotes { get; set; }
    /// "repair" for "Zkopírovat znovu" (Windows from 0.5.0); absent for a full copy and an append. The Mac ignores it.
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    /// Repair: the non-matching copies moved aside (`_REPORT/neshoda_NNNN/…`); absent when none was.
```

Najít (5/10, řádky 153–158):

```csharp
            FilesOK = destResults[d].Count(r => IsOk(r.State)),
            FilesFailed = destResults[d].Count(r => !IsOk(r.State)),
            Unbuffered = unbuffered[d] ? null : false,
        }).ToList();
        return new OffloadReport
        {
```

Nahradit:

```csharp
            FilesOK = destResults[d].Count(r => IsOk(r.State)),
            FilesFailed = destResults[d].Count(r => !IsOk(r.State)),
            Unbuffered = unbuffered[d] ? null : false,
            VerifyMethod = verified ? ReportDestination.ReadBack : null,
            VolumeSerial = NativeIO.VolumeSerialText(root),
        }).ToList();
        return new OffloadReport
        {
```

Najít (6/10, řádky 176–181):

```csharp
            Kind = plan.Mode == PlanMode.Repair ? RepairKind : null,
            CopyPausedSeconds = Seconds(copyPaused),
            VerifyPausedSeconds = Seconds(verifyPaused),
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
        };
    }
```

Nahradit:

```csharp
            Kind = plan.Mode == PlanMode.Repair ? RepairKind : null,
            CopyPausedSeconds = Seconds(copyPaused),
            VerifyPausedSeconds = Seconds(verifyPaused),
            SafeToFormatSeconds = verified && files.All(f => f.SourceError == null && f.Destinations.All(s => s == "verified"))
                ? Math.Round(Math.Max(0, (finished - started - copyPaused - verifyPaused).TotalSeconds), 1) : null,
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
        };
    }
```

Najít (7/10, řádky 272–278):

```csharp
    ///      lines that were missed (any MHL-recorded path not in the list).
    /// The hash list and the chain are swapped with ReplaceFile (NativeIO.WriteFileReplacing), which works while a
    /// reader holds them with delete sharing.
    public void Write(string cardPath, bool includeHashRecords)
    {
        int generation = Generation ?? 1;
        var dir = System.IO.Path.Combine(cardPath, Layout.ReportDirName);
```

Nahradit:

```csharp
    ///      lines that were missed (any MHL-recorded path not in the list).
    /// The hash list and the chain are swapped with ReplaceFile (NativeIO.WriteFileReplacing), which works while a
    /// reader holds them with delete sharing.
    /// `createNew` (plan 10): whether the reports must not exist yet — by default an append's first write (with the hash
    /// records); the deferred first write of a verify-in-proxy append says so itself, its Commit replaces them.
    public void Write(string cardPath, bool includeHashRecords, bool? createNew = null)
    {
        int generation = Generation ?? 1;
        var dir = System.IO.Path.Combine(cardPath, Layout.ReportDirName);
```

Najít (8/10, řádky 285–291):

```csharp
            (TextName(generation), Summary),
            (HtmlName(CardName, generation), HtmlReport.Render(this)),
        };
        bool create = generation > 1 && includeHashRecords;
        foreach (var (name, text) in outputs)
        {
            var path = System.IO.Path.Combine(dir, name);
```

Nahradit:

```csharp
            (TextName(generation), Summary),
            (HtmlName(CardName, generation), HtmlReport.Render(this)),
        };
        bool create = createNew ?? (generation > 1 && includeHashRecords);
        foreach (var (name, text) in outputs)
        {
            var path = System.IO.Path.Combine(dir, name);
```

Najít (9/10, řádky 311–316):

```csharp
            NativeIO.WriteFileReplacing(hashList, [.. earlier, .. added]);
        }
    }

    /// The lines of `lines` the list does not have word for word yet: a repair's file is listed already with the hash the
    /// card gave then (a repair records only that one — ruling C1), so the list keeps one line per path and hash.
```

Nahradit:

```csharp
            NativeIO.WriteFileReplacing(hashList, [.. earlier, .. added]);
        }
    }

    /// Plan 10: the generation's record once its verification ended in stage 2 — the reports written at the end of the
    /// copy (with verifyPending) replaced, then the ASC MHL generation and the chain (the commit point), then the hash
    /// lists: the order of Write. The MHL generation file is new (no stage wrote it before).
    public void Commit(string cardPath) => Write(cardPath, includeHashRecords: true, createNew: false);

    /// The lines of `lines` the list does not have word for word yet: a repair's file is listed already with the hash the
    /// card gave then (a repair records only that one — ruling C1), so the list keeps one line per path and hash.
```

Najít (10/10, řádky 125–126):

```csharp
    /// Every file is on every destination and was read back with a matching hash.
    [JsonIgnore] public bool SafeToEject => Success && Verified;
```

Nahradit:

```csharp
    /// Every file is on every destination and was verified there (spec 2 safe_to_format for this generation).
    [JsonIgnore] public bool SafeToFormat => Success && Verified;
```

#### `windows/src/Offload.Core/Models.cs`

Najít (1/1, řádky 16–21):

```csharp
    /// Plan 10, R6: every this many blocks of a file being written, its block hashes go into the manifest as a checkpoint
    /// (64 × 16 MiB = 1 GiB); 0 = none.
    internal int CheckpointBlocks { get; set; } = 64;
    public bool AllowSameDevice { get; set; }
    /// Tests only: source and destinations in temp folders share a volume.
    internal bool AllowSourceVolume { get; set; }
```

Nahradit:

```csharp
    /// Plan 10, R6: every this many blocks of a file being written, its block hashes go into the manifest as a checkpoint
    /// (64 × 16 MiB = 1 GiB); 0 = none.
    internal int CheckpointBlocks { get; set; } = 64;
    /// Plan 10 (verify-in-proxy): the job verifies in stage 2 — the copy leaves out the read-back, writes its reports with
    /// "verifyPending" and no ASC MHL / hash list yet (OffloadReport.Commit writes them once verified).
    internal bool DeferVerify { get; set; }
    public bool AllowSameDevice { get; set; }
    /// Tests only: source and destinations in temp folders share a volume.
    internal bool AllowSourceVolume { get; set; }
```

#### `windows/src/Offload.Core/OffloadEngine.Run.cs`

Najít (1/3, řádky 133–139):

```csharp
            var copyPaused = Pauses.PausedTotal - pausedBefore;

            Options.BeforeVerify?.Invoke();
            if (Options.Verify && !IsCancelled)
            {
                SetPhase(CopyPhase.Verify);
                // A repair compares every copy — kept or new — with what the backup recorded (ruling C1).
```

Nahradit:

```csharp
            var copyPaused = Pauses.PausedTotal - pausedBefore;

            Options.BeforeVerify?.Invoke();
            // Plan 10: the read-back is left to stage 2 (the proxies read the copies) — not when the copy was cancelled or
            // the card changed (then the generation is written now, as without verify-in-proxy), never for a repair.
            bool defer = Options.DeferVerify && Options.Verify && !IsCancelled && !cardChanged && plan.Mode != PlanMode.Repair;
            if (Options.Verify && !IsCancelled && !defer)
            {
                SetPhase(CopyPhase.Verify);
                // A repair compares every copy — kept or new — with what the backup recorded (ruling C1).
```

Najít (2/3, řádky 152–158):

```csharp

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, recorded, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled, started, copyFinished, finished, copyPaused, verifyPaused);
            if (plan.Mode == PlanMode.Repair)
            {
                var moved = ws.SelectMany(w => w.MovedAside.Select(m => new SetAsideCopy(w.Index, files[m.File].RelPath, m.MovedTo))).ToList();
```

Nahradit:

```csharp

            SetPhase(CopyPhase.Report);
            var report = OffloadReport.Build(plan, recorded, sourceErrors, ws.Select(w => w.Results).ToList(),
                ws.Select(w => w.Unbuffered).ToList(), Options.Verify && !IsCancelled && !defer, started, copyFinished, finished, copyPaused, verifyPaused);
            if (defer) report.VerifyPending = true;
            if (plan.Mode == PlanMode.Repair)
            {
                var moved = ws.SelectMany(w => w.MovedAside.Select(m => new SetAsideCopy(w.Index, files[m.File].RelPath, m.MovedTo))).ToList();
```

Najít (3/3, řádek 165):

```csharp
                try { report.Write(path, includeHashRecords: true); }
```

Nahradit:

```csharp
                // Deferred (plan 10): the reports now, the ASC MHL and the hash lists once verified (Commit, stage 2).
                try { report.Write(path, includeHashRecords: !defer, createNew: defer ? plan.Generation > 1 : null); }
```

#### `windows/src/Offload.Core/Mhl.cs`

Najít (1/2, řádky 47–52):

```csharp
            : generation > 1
            ? $"Offload doplnění: {listed} {Texts.Plural(listed, "nový soubor", "nové soubory", "nových souborů")} z {report.Source}"
            : $"Offload of {report.CardName} from {report.Source}";
        IEnumerable<string> ignore = earlier?.Generations.FirstOrDefault() is { } first
            ? first.Ignore
            : IgnorePatterns.Concat(report.Skipped.Select(s => s.Split('/')[^1]));
```

Nahradit:

```csharp
            : generation > 1
            ? $"Offload doplnění: {listed} {Texts.Plural(listed, "nový soubor", "nové soubory", "nových souborů")} z {report.Source}"
            : $"Offload of {report.CardName} from {report.Source}";
        // Plan 10 (spec 9): how this destination was verified. `<process>` stays "transfer" — the ASC MHL v2 schema allows
        // only in-place / transfer / flatten there — so the method goes into the generation's comment.
        if (report.Destinations[d].VerifyMethod is string method) comment += $" · ověření: {method}";
        IEnumerable<string> ignore = earlier?.Generations.FirstOrDefault() is { } first
            ? first.Ignore
            : IgnorePatterns.Concat(report.Skipped.Select(s => s.Split('/')[^1]));
```

Najít (2/2, řádky 86–91):

```csharp
                if (state == "pending" || (!rewritten[d] && (state == "written" || !report.Verified))) continue;
                action = state == "failed" ? "failed" : rewritten[d] ? "original" : "verified";
            }
            else action = state is "verified" or "written" ? "original" : "failed";
            Line("    <hash>");
            Line($"      <path size=\"{f.Size}\" lastmodificationdate=\"{IsoDate(f.Modified)}\">{Esc(f.Path)}</path>");
```

Nahradit:

```csharp
                if (state == "pending" || (!rewritten[d] && (state == "written" || !report.Verified))) continue;
                action = state == "failed" ? "failed" : rewritten[d] ? "original" : "verified";
            }
            // Spec 9: only a verified copy goes in — "written" only into a generation made without (or cancelled before)
            // its verification, as before plan 10.
            else if (state == "written" && report.Verified) continue;
            else action = state is "verified" or "written" ? "original" : "failed";
            Line("    <hash>");
            Line($"      <path size=\"{f.Size}\" lastmodificationdate=\"{IsoDate(f.Modified)}\">{Esc(f.Path)}</path>");
```

#### `windows/src/Offload.Core/OffloadJob.cs`

Najít (1/1, řádek 319):

```csharp
            bool safe = report.SafeToEject;
```

Nahradit:

```csharp
            bool safe = report.SafeToFormat;
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~DeferredVerifyTests|FullyQualifiedName~EngineTests|FullyQualifiedName~EnginePauseTests|FullyQualifiedName~RepairEngineTests|FullyQualifiedName~RepairJobTests|FullyQualifiedName~ReportTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~MhlTests"`

Expected: PASS — `Failed: 0, Passed: 187, Skipped: 0, Total: 187` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1535` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 6: odložené ověření — reporty s `verifyPending`, generace ASC MHL a seznamy hashů až po ověření (`OffloadReport.Commit`); pole `verifyMethod`, `verifyNote`, `volumeSerial`, `safeToFormatSeconds`, `verifyNotes`; metoda v komentáři generace MHL; `SafeToEject` reportu je teď `SafeToFormat`.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: deferred verification, report fields and MHL commit after verify (plan 10, task 6)

With DeferVerify the copy skips the read-back and writes its reports with verifyPending and no
ASC MHL generation or hash list; OffloadReport.Commit writes them once verified. New report keys:
verifyMethod, verifyNote, volumeSerial, safeToFormatSeconds, verifyNotes; the MHL comment names the
method and a verified generation leaves out "written". Report.SafeToEject is now SafeToFormat.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Mhl.cs windows\src\Offload.Core\Models.cs windows\src\Offload.Core\OffloadEngine.Run.cs windows\src\Offload.Core\OffloadJob.cs windows\src\Offload.Core\OffloadReport.cs windows\tests\Offload.Core.Tests\AppendEngineTests.cs windows\tests\Offload.Core.Tests\DeferredVerifyTests.cs windows\tests\Offload.Core.Tests\EnginePauseTests.cs windows\tests\Offload.Core.Tests\EngineTests.cs windows\tests\Offload.Core.Tests\RepairEngineTests.cs windows\tests\Offload.Core.Tests\RepairJobTests.cs windows\tests\Offload.Core.Tests\ReportTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 7: Tap: HTTP server s rozsahy jen na 127.0.0.1 (R3, režim A)

ffmpeg čte klip v režimu A přes vlastní HTTP server Offloadu, takže každý přečtený bajt projde ověřovačem (R3).
Bezpečnost: `TcpListener` jen na `IPAddress.Loopback`, náhodný port, jednorázový 128bitový token jako první segment cesty
(porovnaný v konstantním čase), náhodné id souboru — URL nenese žádnou cestu na disku, takže traversal nemá co otevřít
(zbytek cesty se nikdy nedekóduje). Jen GET/HEAD, jeden rozsah, hlavička `Host` přesně `127.0.0.1:<port>` (proti DNS
rebinding), nejvýš 16 spojení, hlavičky do 8 KiB, 30 s na hlavičky. Malé výseče (256 KiB) a malý odesílací buffer
(64 KiB): když ffmpeg spojení zahodí kvůli skoku (moov na konci, patička MXF), server přestane číst brzy (naměřeno:
MXF 116,7 % souboru z disku místo 220 %).

**Files:**
- Create: `windows/src/Offload.Core/VerifyInProxy/RangeServer.cs`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/RangeServerTests.cs` (nový)

**Interfaces:**
- Consumes: —
- Produces:
  - `ITapSource { long Length; int Read(long offset, Span<byte> buffer); }`.
  - `RangeServer()`: `Port`, `EndPoint`, `Register(ITapSource, string name)` → URL `http://127.0.0.1:<port>/<token>/<id>/<name>`, `Unregister(url)`, `BytesSent`, `Requests`, `ReadErrors`, `MaxConnections = 16`, `MaxHeaderBytes = 8192`, `internal static ParseRange(string, long)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/RangeServerTests.cs` (nový soubor)

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// R3 tap server: Range semantics ffmpeg relies on, and its security — 127.0.0.1 only, token, no path in the URL, Host
/// header, methods, header size, connection limit, a client or a disk going away mid-body. Network: 127.0.0.1 only.
public class RangeServerTests
{
    sealed class Bytes(byte[] data) : ITapSource
    {
        public int Reads;
        public Exception? FailAt;
        public long Length => data.Length;

        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref Reads);
            if (FailAt != null && offset > 0) throw FailAt;
            int n = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
    }

    static byte[] Data(int size)
    {
        var d = new byte[size];
        new Random(size).NextBytes(d);
        return d;
    }

    /// Sends `request` as written (CRLF line ends) and returns the status line, the headers and the body.
    static (string Status, Dictionary<string, string> Headers, byte[] Body) Raw(int port, string request)
    {
        using var c = new TcpClient();
        c.Connect(IPAddress.Loopback, port);
        var s = c.GetStream();
        s.Write(Encoding.ASCII.GetBytes(request));
        using var all = new MemoryStream();
        try { s.CopyTo(all); } catch (IOException) { }
        var bytes = all.ToArray();
        int end = IndexOf(bytes, "\r\n\r\n"u8);
        if (end < 0) return ("", [], []);
        var head = Encoding.ASCII.GetString(bytes, 0, end).Split("\r\n");
        var headers = head.Skip(1).Select(h => h.Split(':', 2)).ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
        return (head[0], headers, bytes[(end + 4)..]);
    }

    static int IndexOf(byte[] a, ReadOnlySpan<byte> what) => a.AsSpan().IndexOf(what);

    static string Get(RangeServer server, string url, string? range = null, string method = "GET", string? host = null)
    {
        var path = new Uri(url).AbsolutePath;
        return $"{method} {path} HTTP/1.1\r\nHost: {host ?? $"127.0.0.1:{server.Port}"}\r\n{(range != null ? $"Range: {range}\r\n" : "")}Connection: close\r\n\r\n";
    }

    [Fact]
    public void ListensOnLoopbackOnly()
    {
        using var server = new RangeServer();
        Assert.Equal(IPAddress.Loopback, server.EndPoint.Address);
    }

    [Fact]
    public async Task FullGet_200_ExactBytes_AndAcceptsRanges()
    {
        var data = Data(3_000_001);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "A001C003.mov");
        Assert.EndsWith("/A001C003.mov", url);

        using var http = new HttpClient();
        var r = await http.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("bytes", Assert.Single(r.Headers.AcceptRanges));
        Assert.Equal(data, await r.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("bytes=10-19", 10, 19)]
    [InlineData("bytes=999990-", 999990, 999999)]
    [InlineData("bytes=-5", 999995, 999999)]
    [InlineData("bytes=5-5000000", 5, 999999)]
    public void Range_206_ContentRange_AndTheBytes(string range, int from, int to)
    {
        var data = Data(1_000_000);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "a.mov");

        var (status, headers, body) = Raw(server.Port, Get(server, url, range));

        Assert.Equal("HTTP/1.1 206 Partial Content", status);
        Assert.Equal($"bytes {from}-{to}/1000000", headers["Content-Range"]);
        Assert.Equal((to - from + 1).ToString(), headers["Content-Length"]);
        Assert.Equal(data[from..(to + 1)], body);
    }

    [Theory]
    [InlineData("bytes=1000000-")]
    [InlineData("bytes=0-1,5-6")]
    [InlineData("bytes=9-3")]
    [InlineData("items=0-1")]
    [InlineData("bytes=-0")]
    public void UnsatisfiableOrSeveralRanges_416(string range)
    {
        using var server = new RangeServer();
        var url = server.Register(new Bytes(Data(1_000_000)), "a.mov");
        var (status, headers, body) = Raw(server.Port, Get(server, url, range));
        Assert.Equal("HTTP/1.1 416 Range Not Satisfiable", status);
        Assert.Equal("bytes */1000000", headers["Content-Range"]);
        Assert.Empty(body);
    }

    [Fact]
    public void Head_HeadersOnly_NoRead()
    {
        var src = new Bytes(Data(1000));
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");
        var (status, headers, body) = Raw(server.Port, Get(server, url, method: "HEAD"));
        Assert.Equal("HTTP/1.1 200 OK", status);
        Assert.Equal("1000", headers["Content-Length"]);
        Assert.Empty(body);
        Assert.Equal(0, src.Reads);
    }

    // Security: without the exact token and a registered id nothing is served — and the URL's path never reaches the disk.
    [Theory]
    [InlineData("/{token}/{id}/../../Windows/win.ini")]
    [InlineData("/{token}/../{id}/a.mov")]
    [InlineData("/{token}/%2e%2e/{id}/a.mov")]
    [InlineData("/{token}/{id}")]
    [InlineData("/{token}/{id}/a.mov/extra")]
    [InlineData("/{wrongtoken}/{id}/a.mov")]
    [InlineData("/{token}/0000000000000000/a.mov")]
    [InlineData("/{token}x/{id}/a.mov")]
    [InlineData("//{token}/{id}/a.mov")]
    [InlineData("http://127.0.0.1/{token}/{id}/a.mov")]
    public void WrongToken_UnknownId_OrTraversal_404_NothingRead(string pattern)
    {
        var src = new Bytes(Data(1000));
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");
        var parts = new Uri(url).AbsolutePath.Split('/');
        var wrong = new string(parts[1].Reverse().ToArray());
        var target = pattern.Replace("{token}", parts[1]).Replace("{id}", parts[2]).Replace("{wrongtoken}", wrong);

        var (status, _, body) = Raw(server.Port, $"GET {target} HTTP/1.1\r\nHost: 127.0.0.1:{server.Port}\r\nConnection: close\r\n\r\n");

        Assert.Equal("HTTP/1.1 404 Not Found", status);
        Assert.Empty(body);
        Assert.Equal(0, src.Reads);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public void OtherMethods_405(string method)
    {
        var src = new Bytes(Data(10));
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");
        Assert.Equal("HTTP/1.1 405 Method Not Allowed", Raw(server.Port, Get(server, url, method: method)).Status);
        Assert.Equal(0, src.Reads);
    }

    // A web page that rebinds a name to 127.0.0.1 sends its own Host: refused.
    [Theory]
    [InlineData("evil.example:80")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void ForeignHost_403(string host)
    {
        var src = new Bytes(Data(10));
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");
        Assert.Equal("HTTP/1.1 403 Forbidden", Raw(server.Port, Get(server, url, host: host)).Status);
        Assert.Equal(0, src.Reads);
    }

    [Fact]
    public void OversizedHead_ConnectionClosed_ServerStillServes()
    {
        var data = Data(100);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "a.mov");
        var huge = $"GET {new Uri(url).AbsolutePath} HTTP/1.1\r\nHost: 127.0.0.1:{server.Port}\r\nX-Pad: {new string('a', RangeServer.MaxHeaderBytes)}\r\n\r\n";
        Assert.Equal("", Raw(server.Port, huge).Status);

        Assert.Equal(data, Raw(server.Port, Get(server, url)).Body);
    }

    [Fact]
    public void Unregistered_404()
    {
        using var server = new RangeServer();
        var url = server.Register(new Bytes(Data(10)), "a.mov");
        server.Unregister(url);
        Assert.Equal("HTTP/1.1 404 Not Found", Raw(server.Port, Get(server, url)).Status);
    }

    // ffmpeg closes a connection when it seeks: the server goes on with the next one.
    [Fact]
    public void ClientGoesAwayMidBody_NextRequestServed()
    {
        var data = Data(20_000_000);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "a.mov");
        using (var c = new TcpClient())
        {
            c.Connect(IPAddress.Loopback, server.Port);
            c.GetStream().Write(Encoding.ASCII.GetBytes(Get(server, url)));
            var some = new byte[1000];
            c.GetStream().ReadExactly(some);
        }
        var (status, _, body) = Raw(server.Port, Get(server, url, "bytes=19999990-"));
        Assert.Equal("HTTP/1.1 206 Partial Content", status);
        Assert.Equal(data[19999990..], body);
    }

    // A disk that fails mid-body: the connection is cut (ffmpeg sees a short read), the server stays up.
    [Fact]
    public void ReadFailsMidBody_ConnectionCut_Counted()
    {
        var data = Data(5_000_000);
        var src = new Bytes(data) { FailAt = new IOError("read", "x", 21) };
        using var server = new RangeServer();
        var url = server.Register(src, "a.mov");

        var (status, _, body) = Raw(server.Port, Get(server, url));

        Assert.Equal("HTTP/1.1 200 OK", status);
        Assert.True(body.Length < data.Length);
        Assert.Equal(1, server.ReadErrors);
        src.FailAt = null;
        Assert.Equal(data, Raw(server.Port, Get(server, url)).Body);
    }

    [Fact]
    public async Task KeepAlive_TwoRequestsOnOneConnection()
    {
        var data = Data(100);
        using var server = new RangeServer();
        var url = server.Register(new Bytes(data), "a.mov");
        using var http = new HttpClient();
        var a = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9) } });
        var b = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new System.Net.Http.Headers.RangeHeaderValue(90, 99) } });
        Assert.Equal(data[..10], await a.Content.ReadAsByteArrayAsync());
        Assert.Equal(data[90..], await b.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, server.Requests);
    }

    [Fact]
    public void ConnectionsOverTheLimit_Refused_NotQueued()
    {
        using var server = new RangeServer();
        var url = server.Register(new Bytes(Data(10)), "a.mov");
        var idle = Enumerable.Range(0, RangeServer.MaxConnections).Select(_ =>
        {
            var c = new TcpClient();
            c.Connect(IPAddress.Loopback, server.Port);
            return c;
        }).ToList();
        try
        {
            Thread.Sleep(300);   // every idle one holds a slot now
            Assert.Equal("", Raw(server.Port, Get(server, url)).Status);
        }
        finally
        {
            idle.ForEach(c => c.Dispose());
        }
        Assert.True(SpinWait.SpinUntil(() => Raw(server.Port, Get(server, url)).Status == "HTTP/1.1 200 OK", TimeSpan.FromSeconds(10)));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~RangeServerTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0246: The type or namespace name 'RangeServer' could not be found (are you missing a using directive or an assembly reference?)`; `CS0246: The type or namespace name 'ITapSource' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/VerifyInProxy/RangeServer.cs` (nový soubor)

```csharp
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core.VerifyInProxy;

/// What the tap serves: the file's length and a way to read it — from the destination's disk, through the verifier.
public interface ITapSource
{
    long Length { get; }
    /// The bytes at `offset` into `buffer`; how many were copied — fewer only at the end of the file. Throws on a disk error.
    int Read(long offset, Span<byte> buffer);
}

/// R3 (mode A): Offload's own HTTP server, so the ffmpeg process reads the clip through Offload and every byte it reads
/// passes the block verifier. Security: bound to 127.0.0.1 only (never another interface), a random port, a one-time
/// 128-bit token as the first path segment (compared in constant time) and a random id per file — the URL names no path
/// on the disk, so nothing in it can reach any other file (no path traversal: the rest of the path is never decoded or
/// used). Only GET and HEAD, one byte range at a time, a Host header of exactly 127.0.0.1:<port> (a web page cannot reach
/// it through DNS rebinding), at most MaxConnections at once, headers up to MaxHeaderBytes.
public sealed class RangeServer : IDisposable
{
    public const int MaxConnections = 16;
    public const int MaxHeaderBytes = 8192;
    /// Small slices and a small socket send buffer: when ffmpeg drops a connection to seek (moov at the end, an MXF
    /// footer) the server notices after little read-ahead — every block read is still verified, but a block read ahead
    /// and needed again later may fall out of the tap's cache and be read twice.
    const int SliceBytes = 256 << 10;
    const int SendBufferBytes = 64 << 10;
    static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);

    readonly TcpListener listener;
    readonly byte[] token = RandomNumberGenerator.GetBytes(16);
    readonly ConcurrentDictionary<string, ITapSource> files = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<TcpClient, byte> clients = new();
    readonly SemaphoreSlim slots = new(MaxConnections, MaxConnections);
    readonly CancellationTokenSource stop = new();
    readonly Task accepting;
    long bytesSent, requests, readErrors;

    public int Port { get; }
    public IPEndPoint EndPoint => (IPEndPoint)listener.LocalEndpoint;
    /// Body bytes sent, requests answered and disk reads that failed under a request (tests, the log).
    public long BytesSent => Interlocked.Read(ref bytesSent);
    public long Requests => Interlocked.Read(ref requests);
    public long ReadErrors => Interlocked.Read(ref readErrors);
    string Token => Convert.ToHexStringLower(token);

    public RangeServer()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = Task.Run(AcceptLoop);
    }

    /// Serves `source` until Unregister; returns its URL — `name` only gives ffmpeg the extension (the demuxer's hint).
    public string Register(ITapSource source, string name)
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        files[id] = source;
        var safe = new string(Path.GetFileName(name).Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
        return $"http://127.0.0.1:{Port}/{Token}/{id}/{(safe.Length > 0 ? safe : "clip")}";
    }

    /// Stops serving the file of `url` (a request for it is then 404).
    public void Unregister(string url)
    {
        if (IdOf(url) is string id) files.TryRemove(id, out _);
    }

    /// The file id of a URL this server made, else null.
    static string? IdOf(string url)
    {
        var parts = new Uri(url).AbsolutePath.Split('/');
        return parts.Length >= 4 ? parts[2] : null;
    }

    async Task AcceptLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false); }
            catch (Exception) { return; }   // stopped
            if (!slots.Wait(0))
            {
                client.Dispose();   // too many at once: refused, never queued
                continue;
            }
            clients[client] = 0;
            _ = Task.Run(() =>
            {
                try { Serve(client); }
                catch (Exception) { /* the client went away, or the server stopped */ }
                finally
                {
                    clients.TryRemove(client, out _);
                    client.Dispose();
                    slots.Release();
                }
            });
        }
    }

    void Serve(TcpClient client)
    {
        client.NoDelay = true;
        client.SendBufferSize = SendBufferBytes;
        client.ReceiveTimeout = (int)HeaderTimeout.TotalMilliseconds;
        client.SendTimeout = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;
        using var stream = client.GetStream();
        var buffer = new byte[SliceBytes];
        while (!stop.IsCancellationRequested)
        {
            var head = ReadHead(stream);
            if (head == null) return;   // closed, too long or timed out
            Interlocked.Increment(ref requests);
            if (!Answer(stream, head, buffer)) return;
        }
    }

    /// The request line and headers (ASCII, up to the empty line); null when the client closed, the head is longer than
    /// MaxHeaderBytes or is not ASCII.
    static List<string>? ReadHead(NetworkStream stream)
    {
        var bytes = new List<byte>(512);
        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0 || b > 127) return null;
            bytes.Add((byte)b);
            if (bytes.Count > MaxHeaderBytes) return null;
            int n = bytes.Count;
            if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n') break;
        }
        return Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n").Where(l => l.Length > 0).ToList();
    }

    /// One request. False = close the connection.
    bool Answer(NetworkStream stream, List<string> head, byte[] buffer)
    {
        var line = head[0].Split(' ');
        if (line.Length != 3 || !line[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return Status(stream, 400, "Bad Request", close: true);
        string method = line[0], target = line[1];
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in head.Skip(1))
        {
            int colon = h.IndexOf(':');
            if (colon <= 0) return Status(stream, 400, "Bad Request", close: true);
            headers[h[..colon].Trim()] = h[(colon + 1)..].Trim();
        }
        bool close = line[2] == "HTTP/1.0" || headers.TryGetValue("Connection", out var c) && c.Equals("close", StringComparison.OrdinalIgnoreCase);
        if (method is not ("GET" or "HEAD")) return Status(stream, 405, "Method Not Allowed", close: true, "Allow: GET, HEAD\r\n");
        if (!headers.TryGetValue("Host", out var host) || !host.Equals($"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase))
            return Status(stream, 403, "Forbidden", close: true);
        if (Find(target) is not { } source) return Status(stream, 404, "Not Found", close);

        long length = source.Length;
        long start = 0, end = length - 1;
        bool partial = false;
        if (headers.TryGetValue("Range", out var range))
        {
            if (ParseRange(range, length) is not { } r) return Status(stream, 416, "Range Not Satisfiable", close, $"Content-Range: bytes */{length}\r\n");
            (start, end, partial) = (r.Start, r.End, true);
        }
        long count = Math.Max(0, end - start + 1);
        var sb = new StringBuilder();
        sb.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
        sb.Append("Content-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"Content-Length: {count}\r\n");
        if (partial) sb.Append(CultureInfo.InvariantCulture, $"Content-Range: bytes {start}-{end}/{length}\r\n");
        sb.Append(close ? "Connection: close\r\n\r\n" : "Connection: keep-alive\r\n\r\n");
        stream.Write(Encoding.ASCII.GetBytes(sb.ToString()));
        if (method == "HEAD") return !close;

        for (long at = start; at <= end;)
        {
            int want = (int)Math.Min(buffer.Length, end - at + 1);
            int n;
            try { n = source.Read(at, buffer.AsSpan(0, want)); }
            catch (Exception)
            {
                Interlocked.Increment(ref readErrors);
                return false;   // the headers are out: only a cut connection can say it failed
            }
            if (n <= 0) return false;   // the file got shorter than it said: cut, ffmpeg sees a short read
            stream.Write(buffer, 0, n);
            Interlocked.Add(ref bytesSent, n);
            at += n;
        }
        return !close;
    }

    /// The source a target names: "/<token>/<id>/<name>" with this server's token and a registered id; else null.
    ITapSource? Find(string target)
    {
        var parts = target.Split('/');
        if (parts.Length != 4 || parts[0].Length != 0 || parts[1].Length != token.Length * 2) return null;
        byte[] given;
        try { given = Convert.FromHexString(parts[1]); }
        catch (FormatException) { return null; }
        if (!CryptographicOperations.FixedTimeEquals(given, token)) return null;
        return files.TryGetValue(parts[2], out var s) ? s : null;
    }

    /// One range: "bytes=a-b", "bytes=a-", "bytes=-n" within `length`; null for anything else (several ranges included).
    internal static (long Start, long End)? ParseRange(string value, long length)
    {
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
        var spec = value[6..].Trim();
        if (spec.Contains(',')) return null;
        int dash = spec.IndexOf('-');
        if (dash < 0) return null;
        string a = spec[..dash].Trim(), b = spec[(dash + 1)..].Trim();
        static bool Num(string s, out long v) => long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);
        if (a.Length == 0)
        {
            if (!Num(b, out var suffix) || suffix == 0 || length == 0) return null;
            return (Math.Max(0, length - suffix), length - 1);
        }
        if (!Num(a, out var start) || start >= length) return null;
        if (b.Length == 0) return (start, length - 1);
        if (!Num(b, out var end) || end < start) return null;
        return (start, Math.Min(end, length - 1));
    }

    static bool Status(NetworkStream stream, int code, string text, bool close, string extra = "")
    {
        stream.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {text}\r\n{extra}Content-Length: 0\r\n{(close ? "Connection: close\r\n" : "")}\r\n"));
        return !close;
    }

    public void Dispose()
    {
        stop.Cancel();
        try { listener.Stop(); } catch (Exception) { }
        foreach (var c in clients.Keys) c.Dispose();
        try { accepting.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        stop.Dispose();
    }
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~RangeServerTests"`

Expected: PASS — `Failed: 0, Passed: 35, Skipped: 0, Total: 35` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1570` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 7: `RangeServer` — HTTP s rozsahy jen na 127.0.0.1, token a id místo cesty, kontrola `Host`, limity spojení a hlaviček.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: loopback HTTP range server for the tap (plan 10, task 7)

RangeServer serves registered sources over HTTP with byte ranges on 127.0.0.1 only: random port,
one-time token, random file ids (no path in the URL), Host check, GET/HEAD only, connection and
header limits, small slices so a dropped connection stops reading early.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\VerifyInProxy\RangeServer.cs windows\tests\Offload.Core.Tests\RangeServerTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 8: Tap souboru, dočtení chybějících bloků a ffmpeg přes tap (spec 5, 6)

`TapFile` čte cíl po celých blocích bez cache (`DiskBlocks`, `NO_BUFFERING`), každý blok pošle do ověřovače a drží
poslední 4 bloky (ffmpeg čte hlavičku a pak skáče). `BlockReads.Complete` dočte, co tap nepřečetl, a blok, který
nesouhlasil, přečte ještě jednou (spec 10: souhlas napodruhé = nestabilní čtení, ne chyba); `Result` z verdiktu udělá
stav souboru (`HASH MISMATCH: block …`). `ProxyRenderer.Render` dostane `inputUrl` a čte klip z tapu s
`-short_seek_size 65536` (bez něj ffmpeg 8.1 přeskakoval mdat čtením: 200 % souboru přes HTTP). Testy běží na skutečném
ffmpeg: MOV s moov na konci, MP4 faststart, MXF — kolik bajtů tap přečetl z disku (≤ 1,2 × u MXF, jinak ≤ 1,05 × + 2 MiB),
že ověřeno 100 % bloků a že bajt převrácený na disku (druhým handlem s `NO_BUFFERING`) ověření najde.

**Files:**
- Create: `windows/src/Offload.Core/VerifyInProxy/TapFile.cs`
- Create: `windows/src/Offload.Core/VerifyInProxy/BlockReads.cs`
- Modify: `windows/src/Offload.Core/ProxyRenderer.cs:201-211, 352, 362, 377, 397, 399`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/TapRenderTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/TestMedia.cs:34-39`

**Interfaces:**
- Consumes: `BlockVerifier` (úkol 2), `RangeServer`/`ITapSource` (úkol 7), `AlignedBuffer`, `NativeIO.OpenRead`.
- Produces:
  - `DiskBlocks(string path)`: `Length`, `Read(long offset, AlignedBuffer, int blockSize)` (celý blok bez cache).
  - `TapFile(string path, BlockVerifier verifier) : ITapSource`: `CacheBlocks = 4`, `ReadFromDisk`.
  - `BlockReads.Complete(path, verifier, Func<bool>? beforeBlock = null, Action<int>? progress = null, CancellationToken ct = default)`, `BlockReads.Result(BlockOutcome, BlockVerifier)` → `DestFileResult`.
  - `ProxyRenderer.TapInputArgs`; `Render(…, Func<bool> isCancelled, string? inputUrl = null)`.
  - `TestMedia.MakeFaststart(…)` (testy).

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/TapRenderTests.cs` (nový soubor)

```csharp
using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// R3 mode A with ffmpeg 8.1: the proxy reads the clip through the tap — moov at the end (MOV), faststart (MP4), MXF —
/// every block it reads is verified, the fill-in reads only the rest, a flipped byte on the disk is found.
public class TapRenderTests
{
    const int Block = 64 * 1024;
    /// What the tap may read ahead on the connections ffmpeg drops to seek (socket buffers; measured ~0.6 MB each with ffmpeg
    /// 8.1 — with 16 MiB blocks at most one block: MOV 101.8 %, MXF 116.7 % of an 80 MB file, plan 10 measurements).
    const long Slack = 2 << 20;

    static BlockManifest ManifestOf(byte[] data) => new(data.Length, Block,
        Enumerable.Range(0, BlockManifest.CountFor(data.Length, Block))
            .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, Math.Min(Block, data.Length - b * Block)))).ToList());

    static ProxyRenderer Renderer() => new(new ProxySettings { Enabled = true, Height = 240, BurnFilename = false, BurnTimecode = false }, new ExternalTools().Resolved())
    {
        ForcedEncoder = "libx264",
    };

    /// Renders `rel` from `card` through the tap; returns the verifier, the bytes the tap read from the disk and the proxy.
    static (BlockVerifier Verifier, long ReadFromDisk, string Proxy) RenderThroughTap(string card, string rel, byte[] cardBytes)
    {
        var verifier = new BlockVerifier(ManifestOf(cardBytes));
        using var renderer = Renderer();
        var clip = new Clip { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = cardBytes.Length };
        var info = renderer.Probe(clip, card);
        var proxy = Path.Combine(card, "_PROXY", clip.Name + ".mov");
        using var server = new RangeServer();
        using var tap = new TapFile(Layout.Join(card, rel), verifier);
        var url = server.Register(tap, Path.GetFileName(rel));
        renderer.Render(clip, card, info, null, proxy, _ => { }, () => false, url);
        return (verifier, tap.ReadFromDisk, proxy);
    }

    [FfmpegFact]
    public void Mov_MoovAtEnd_ReadOnce_FillInVerifiesTheRest()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C001.mov"), seconds: 2, width: 640, height: 360);
        var bytes = File.ReadAllBytes(Path.Combine(card, "CLIP", "A001C001.mov"));

        var (v, read, proxy) = RenderThroughTap(card, "CLIP/A001C001.mov", bytes);

        Assert.True(File.Exists(proxy));
        // Measured with ffmpeg 8.1: the header, the moov at the end, then the mdat in order — each block read once.
        Assert.True(read <= bytes.Length + Slack, $"{read} of {bytes.Length}");
        Assert.True(v.VerifiedBytes >= bytes.Length - 2 * Block, $"verified {v.VerifiedBytes} of {bytes.Length}");
        Assert.NotEqual(BlockOutcomeKind.Mismatch, v.Finish().Kind);
        Assert.Equal(BlockOutcomeKind.Verified, BlockReads.Complete(Path.Combine(card, "CLIP", "A001C001.mov"), v).Kind);
    }

    [FfmpegFact]
    public void Mp4_Faststart_Verified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "M4ROOT", "CLIP", "C0001.MP4");
        TestMedia.MakeFaststart(path);
        var bytes = File.ReadAllBytes(path);

        var (v, read, _) = RenderThroughTap(card, "M4ROOT/CLIP/C0001.MP4", bytes);

        Assert.True(read <= bytes.Length + Slack, $"{read} of {bytes.Length}");
        Assert.Equal(BlockOutcomeKind.Verified, BlockReads.Complete(path, v).Kind);
    }

    [FfmpegFact]
    public void Mxf_FooterAndIndex_Verified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "XDROOT", "Clip", "C0001.MXF");
        TestMedia.MakeMxf(path, monoTracks: 2, seconds: 1);
        var bytes = File.ReadAllBytes(path);

        var (v, read, proxy) = RenderThroughTap(card, "XDROOT/Clip/C0001.MXF", bytes);

        Assert.True(File.Exists(proxy));
        Assert.True(read <= bytes.Length + Slack, $"{read} of {bytes.Length}");
        Assert.Equal(BlockOutcomeKind.Verified, BlockReads.Complete(path, v).Kind);
    }

    // Spec 11: a bit flipped on the destination (outside Offload, past the file cache) is found by the tap's reads.
    [FfmpegFact]
    public void FlippedByteOnTheDisk_TheTapFindsIt()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "A001C002.mov");
        TestMedia.MakeClip(path, seconds: 2, width: 640, height: 360);
        var bytes = File.ReadAllBytes(path);
        long at = bytes.Length / 2;
        DiskFlip.Flip(path, at);

        var (v, _, _) = RenderThroughTap(card, "A001C002.mov", bytes);

        var outcome = v.Finish();
        Assert.Equal(BlockOutcomeKind.Mismatch, outcome.Kind);
        Assert.Equal((int)(at / Block), outcome.Block);
        Assert.Equal(BlockOutcomeKind.Mismatch, BlockReads.Complete(path, v).Kind);   // read again: still wrong → failed
        Assert.Empty(v.UnstableBlocks);
    }
}

/// Spec 11 "test cache": changes a byte of a file through a second NO_BUFFERING | WRITE_THROUGH handle — the sector is
/// read, changed and written back past the file cache (on the disk now; NTFS and exFAT keep a cached copy coherent, so
/// any later read sees it). A change behind the file system's back (a raw device write) needs admin rights: manual test.
public static class DiskFlip
{
    public static void Flip(string path, long offset)
    {
        using var h = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, (FileOptions)0x20000000 | FileOptions.WriteThrough);
        using var sector = new AlignedBuffer(AlignedBuffer.Alignment);
        long at = offset / AlignedBuffer.Alignment * AlignedBuffer.Alignment;
        int n = RandomAccess.Read(h, sector.Span, at);
        sector.Span[(int)(offset - at)] ^= 0x01;
        RandomAccess.Write(h, sector.Span, at);
        if (n < AlignedBuffer.Alignment) RandomAccess.SetLength(h, at + n);   // the last sector: keep the file's length
    }
}
```

#### `windows/tests/Offload.Core.Tests/TestMedia.cs`

Najít (1/1, řádky 34–39):

```csharp
        if (reel != null) args.AddRange(["-metadata:s:v:0", $"reel_name={reel}"]);
        args.Add(path);
        ToolProcess.Run(Ffmpeg!, args);
    }

    /// A Sony-like MXF: MPEG-2 1080p25 with `monoTracks` 24-bit PCM mono streams and timecode `timecode`.
```

Nahradit:

```csharp
        if (reel != null) args.AddRange(["-metadata:s:v:0", $"reel_name={reel}"]);
        args.Add(path);
        ToolProcess.Run(Ffmpeg!, args);
    }

    /// H.264 MP4 with the moov atom first ("faststart"), 640×360 at 25 fps with timecode 01:00:00:00, no audio — a file
    /// whose damaged or cut-off tail still opens (plan 10: the tap's faststart case and the content check).
    public static void MakeFaststart(string path, double seconds = 2)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ToolProcess.Run(Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", $"testsrc2=s=640x360:r=25:d={d}",
            "-c:v", "libx264", "-g", "25", "-pix_fmt", "yuv420p", "-timecode", "01:00:00:00", "-movflags", "+faststart", path]);
    }

    /// A Sony-like MXF: MPEG-2 1080p25 with `monoTracks` 24-bit PCM mono streams and timecode `timecode`.
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~TapRenderTests|FullyQualifiedName~ProxyRendererTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0246: The type or namespace name 'TapFile' could not be found (are you missing a using directive or an assembly reference?)`; `CS1501: No overload for method 'Render' takes 8 arguments`; `CS0103: The name 'BlockReads' does not exist in the current context`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/VerifyInProxy/TapFile.cs` (nový soubor)

```csharp
using Microsoft.Win32.SafeHandles;

namespace Offload.Core.VerifyInProxy;

/// A file on a destination read in whole blocks straight from the disk (FILE_FLAG_NO_BUFFERING, spec 8) — never from the
/// file cache, so what it gives is what the disk holds. A volume that refuses unbuffered reads is not used (IOError with
/// ERROR_INVALID_PARAMETER from the constructor): its copies are verified the classic way instead.
internal sealed class DiskBlocks : IDisposable
{
    readonly SafeFileHandle handle;

    public string Path { get; }
    /// The file's length on the disk now.
    public long Length { get; }

    public DiskBlocks(string path)
    {
        Path = path;
        handle = NativeIO.OpenRead(path, unbuffered: true);
        try { Length = RandomAccess.GetLength(handle); }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// The block at `offset` (a multiple of the sector size — every block offset is) into `buffer`; the bytes read, fewer
    /// only at the end of the file. The whole buffer is asked for: an unbuffered read must be whole sectors.
    public int Read(long offset, AlignedBuffer buffer, int blockSize) =>
        NativeIO.Read(handle, buffer.Span[..blockSize], offset, Path);

    public void Dispose() => handle.Dispose();
}

/// Mode A's source (R3): the primary destination's copy of a clip, read in whole blocks straight from the disk; each
/// block read is handed to the verifier once, then served to ffmpeg from a small cache (a seek back to a block just
/// read — moov at the end, an MXF footer — costs no second read). Thread-safe (ffmpeg may open a second connection
/// before it closes the first).
public sealed class TapFile : ITapSource, IDisposable
{
    public const int CacheBlocks = 4;

    readonly DiskBlocks disk;
    readonly BlockVerifier verifier;
    readonly Lock gate = new();
    readonly List<(int Block, AlignedBuffer Buffer, int Length)> cache = [];
    long readFromDisk;

    public long Length => disk.Length;
    /// Bytes read from the disk so far (every block once, unless it fell out of the cache).
    public long ReadFromDisk => Interlocked.Read(ref readFromDisk);

    /// Opens `path` unbuffered (IOError when the volume refuses it) and tells the verifier its length.
    public TapFile(string path, BlockVerifier verifier)
    {
        this.verifier = verifier;
        disk = new DiskBlocks(path);
        verifier.ObserveLength(disk.Length);
    }

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset >= disk.Length || buffer.IsEmpty) return 0;
        int size = verifier.Manifest.BlockSize;
        int done = 0;
        lock (gate)
        {
            while (done < buffer.Length && offset + done < disk.Length)
            {
                long at = offset + done;
                int b = (int)(at / size);
                var (block, length) = Block(b, size);
                int from = (int)(at - (long)b * size);
                if (from >= length) break;   // the disk gave less than the file's length (it shrank meanwhile)
                int n = Math.Min(length - from, buffer.Length - done);
                block.Span.Slice(from, n).CopyTo(buffer[done..]);
                done += n;
            }
        }
        return done;
    }

    (AlignedBuffer Buffer, int Length) Block(int b, int size)
    {
        int hit = cache.FindIndex(c => c.Block == b);
        if (hit >= 0)
        {
            var c = cache[hit];
            cache.RemoveAt(hit);
            cache.Add(c);   // most recently used last
            return (c.Buffer, c.Length);
        }
        AlignedBuffer buffer;
        if (cache.Count >= CacheBlocks)
        {
            buffer = cache[0].Buffer;
            cache.RemoveAt(0);
        }
        else buffer = new AlignedBuffer(size);
        int n;
        try { n = disk.Read((long)b * size, buffer, size); }
        catch
        {
            buffer.Dispose();
            throw;
        }
        Interlocked.Add(ref readFromDisk, n);
        verifier.Feed((long)b * size, buffer.Span[..n]);
        cache.Add((b, buffer, n));
        return (buffer, n);
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var c in cache) c.Buffer.Dispose();
            cache.Clear();
        }
        disk.Dispose();
    }
}
```

#### `windows/src/Offload.Core/VerifyInProxy/BlockReads.cs` (nový soubor)

```csharp
namespace Offload.Core.VerifyInProxy;

/// Reading blocks of a destination's copy straight from the disk for the verifier: the fill-in after a proxy (spec 6 —
/// only what the tap did not read) and the classic background read (all of it, a fresh verifier).
public static class BlockReads
{
    /// Reads every block the verifier still lacks, then once more every block that did not match (spec 10: "blok znovu
    /// bez cache" — matching now is an unstable read, not a failure); returns the verdict. `beforeBlock`: false stops
    /// (a pause that ended in a cancel) — OperationCanceledException, as `ct`. `progress`: bytes read. IOError when the
    /// file cannot be opened or read (a disk unplugged).
    public static BlockOutcome Complete(string path, BlockVerifier verifier, Func<bool>? beforeBlock = null, Action<int>? progress = null,
        CancellationToken ct = default)
    {
        // Verified already by this session's reads (the tap, the cache read): nothing to open again.
        if (verifier.LengthObserved && verifier.Finish().Kind == BlockOutcomeKind.Verified) return verifier.Finish();
        int size = verifier.Manifest.BlockSize;
        using var buffer = new AlignedBuffer(size);
        using var disk = new DiskBlocks(path);
        verifier.ObserveLength(disk.Length);
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var range in verifier.MissingRanges())
            {
                for (long off = range.Offset; off < range.Offset + range.Length; off += size)
                {
                    ct.ThrowIfCancellationRequested();
                    if (beforeBlock?.Invoke() == false) throw new OperationCanceledException();
                    ct.ThrowIfCancellationRequested();
                    int n = disk.Read(off, buffer, size);
                    if (n <= 0) break;   // shorter on the disk: ObserveLength already made it a mismatch
                    verifier.Feed(off, buffer.Span[..n]);
                    progress?.Invoke(n);
                }
            }
            var now = verifier.Finish();
            if (now.Kind != BlockOutcomeKind.Mismatch || now.Block < 0) return now;
        }
        return verifier.Finish();
    }

    /// The destination's result from a verdict (English texts, as every error in the report JSON — spec 2.6): a mismatch
    /// names the block and both hashes, a size difference both sizes; Incomplete leaves the copy "written".
    public static DestFileResult Result(BlockOutcome outcome, BlockVerifier verifier) => outcome.Kind switch
    {
        BlockOutcomeKind.Verified => new DestFileResult(FileState.Verified, null),
        BlockOutcomeKind.Mismatch when outcome.Block < 0 => new DestFileResult(FileState.Failed, $"size mismatch: {outcome.SizeOnDisk} != {verifier.Manifest.Size}"),
        BlockOutcomeKind.Mismatch => new DestFileResult(FileState.Failed,
            $"HASH MISMATCH: block {outcome.Block} dest {Format.Hex64(verifier.DiskHash(outcome.Block) ?? 0)} != source {Format.Hex64(verifier.Manifest.Blocks[outcome.Block])}"),
        _ => new DestFileResult(FileState.Written, null),
    };
}
```

#### `windows/src/Offload.Core/ProxyRenderer.cs`

Najít (1/6, řádky 201–211):

```csharp
    internal static bool UseHardwareDecoding(string? codec) =>
        codec != null && (codec.StartsWith("h264", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("hevc", StringComparison.OrdinalIgnoreCase));

    FrameSource SourceFor(Clip clip, string cardPath, ClipInfo info, RawLook look, double? singleFrameAt, bool hwaccel)
    {
        var path = Layout.Join(cardPath, clip.Path);
        if (clip.Format == ClipFormat.Video)
            return new FrameSource { InputArgs = hwaccel ? ["-hwaccel", "auto", "-i", path] : ["-i", path] };
        if (!backends.TryGetValue(clip.Format, out var b)) throw new ToolError(clip.Format.Label(), -1, Msg.NoDecoderShort);
        return Logged(() => b.Source(path, info, Settings, look, Tools, workDir, singleFrameAt));
    }
```

Nahradit:

```csharp
    internal static bool UseHardwareDecoding(string? codec) =>
        codec != null && (codec.StartsWith("h264", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("hevc", StringComparison.OrdinalIgnoreCase));

    /// ffmpeg reads the tap (plan 10, mode A) with this input option: a forward jump over more than 64 KB is a new range
    /// request, not a read-through. Without it ffmpeg 8.1 read a moov-at-end MOV and an MXF twice over HTTP (it skipped
    /// the whole mdat by reading it: 200 % of the file); with it 100 % + the few header bytes (plan 10 measurements).
    internal static readonly string[] TapInputArgs = ["-short_seek_size", "65536"];

    FrameSource SourceFor(Clip clip, string cardPath, ClipInfo info, RawLook look, double? singleFrameAt, bool hwaccel, string? inputUrl = null)
    {
        var path = Layout.Join(cardPath, clip.Path);
        if (clip.Format == ClipFormat.Video)
        {
            string[] input = inputUrl != null ? [.. TapInputArgs, "-i", inputUrl] : ["-i", path];
            return new FrameSource { InputArgs = hwaccel ? ["-hwaccel", "auto", .. input] : input };
        }
        if (!backends.TryGetValue(clip.Format, out var b)) throw new ToolError(clip.Format.Label(), -1, Msg.NoDecoderShort);
        return Logged(() => b.Source(path, info, Settings, look, Tools, workDir, singleFrameAt));
    }
```

Najít (2/6, řádek 352):

```csharp
    public void Render(Clip clip, string cardPath, ClipInfo info, LutUse? lut, string output, Action<double> progress, Func<bool> isCancelled)
```

Nahradit:

```csharp
    /// `inputUrl` (plan 10, mode A): a video clip is read from the tap (RangeServer) instead of from its file.
    public void Render(Clip clip, string cardPath, ClipInfo info, LutUse? lut, string output, Action<double> progress, Func<bool> isCancelled,
        string? inputUrl = null)
```

Najít (3/6, řádek 362):

```csharp
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel);
```

Nahradit:

```csharp
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel, inputUrl);
```

Najít (4/6, řádek 377):

```csharp
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel: false);
```

Nahradit:

```csharp
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel: false, inputUrl);
```

Najít (5/6, řádek 397):

```csharp
        Action<double> progress, Func<bool> isCancelled, bool hwaccel)
```

Nahradit:

```csharp
        Action<double> progress, Func<bool> isCancelled, bool hwaccel, string? inputUrl)
```

Najít (6/6, řádek 399):

```csharp
        var src = SourceFor(clip, cardPath, info, LookFor(lutFile != null ? lut : null, info), null, hwaccel);
```

Nahradit:

```csharp
        var src = SourceFor(clip, cardPath, info, LookFor(lutFile != null ? lut : null, info), null, hwaccel, inputUrl);
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~TapRenderTests|FullyQualifiedName~ProxyRendererTests"`

Expected: PASS — `Failed: 0, Passed: 32, Skipped: 0, Total: 32` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1574` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 8: `TapFile` (celé bloky bez cache, 4 bloky v paměti, vše do ověřovače), `BlockReads` (dočtení chybějících bloků, druhé čtení nesouhlasného bloku); ffmpeg čte klip z tapu s `-short_seek_size 65536`; změřeno na MOV/MP4/MXF.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: tap file, fill-in reads and ffmpeg reading through the tap (plan 10, task 8)

TapFile reads whole blocks past the cache, keeps the last four and feeds every block to the
verifier; BlockReads fills in what the tap did not read and reads a mismatching block once more.
ProxyRenderer reads from the tap with -short_seek_size 65536. Tested with ffmpeg on MOV with moov
at the end, MP4 faststart and MXF.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\ProxyRenderer.cs windows\src\Offload.Core\VerifyInProxy\BlockReads.cs windows\src\Offload.Core\VerifyInProxy\TapFile.cs windows\tests\Offload.Core.Tests\TapRenderTests.cs windows\tests\Offload.Core.Tests\TestMedia.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 9: Režim B: vyhození z cache a ověřující čtení před SDK (spec 5, R10)

Klipy, které dekóduje SDK výrobce (BRAW, R3D, ARRIRAW), tapem číst nejde. `CacheWarm` soubor nejdřív vyhodí z
cache (otevře a zavře handle s `NO_BUFFERING` — naměřeno: další čtení přes cache jde z disku, NTFS i exFAT), pak ho jednou
přečte přes cache po blocích do ověřovače; SDK pak dekóduje z cache a disk se čte jednou. Klip větší než polovina
volné RAM se neohřívá (okna po 4 GiB napřed před SDK v1 nemá — tempo SDK neznáme): ověří se dočtením po proxy.
`TapRegistry` určí režim podle formátu klipu (MOV/MP4/MXF = A, BRAW/R3D/ARRIRAW = B, ostatní = dočtení).

**Files:**
- Create: `windows/src/Offload.Core/VerifyInProxy/CacheWarm.cs`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/CacheWarmTests.cs` (nový)

**Interfaces:**
- Consumes: `BlockVerifier`, `DiskBlocks` (úkol 8), `Clip.Format`.
- Produces:
  - `TapMode`, `TapRegistry.ModeFor(Clip)`.
  - `CacheWarm.AvailableMemory()`, `Fits(long bytes, long? available = null)`, `VerifyAndWarm(path, verifier, beforeBlock = null, progress = null, ct = default)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/CacheWarmTests.cs` (nový soubor)

```csharp
using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 5 mode B, spec 8, spec 11: the RAW clip's file is verified by one read through the cache (≤ 1.05 × its size),
/// a flipped byte is found; the codec registry decides mode A or B.
public class CacheWarmTests
{
    const int Block = 64 * 1024;

    static (byte[] Data, BlockManifest Manifest) WriteUnbuffered(string path, int size)
    {
        var data = new byte[size];
        new Random(size).NextBytes(data);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // As the copy writes it (R8): past the file cache, the last block padded and trimmed.
        using (var h = NativeIO.OpenWrite(path, unbuffered: true, size))
        using (var buf = new AlignedBuffer(Block))
        {
            for (int off = 0; off < size; off += Block)
            {
                int n = Math.Min(Block, size - off);
                buf.Span.Clear();
                data.AsSpan(off, n).CopyTo(buf.Span);
                NativeIO.Write(h, buf.Span[..((n + 4095) / 4096 * 4096)], off, path);
            }
            NativeIO.SetLength(h, size, path);
        }
        var blocks = Enumerable.Range(0, BlockManifest.CountFor(size, Block))
            .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, Math.Min(Block, size - b * Block)))).ToList();
        return (data, new BlockManifest(size, Block, blocks));
    }

    [Fact]
    public void Warm_VerifiesInOneRead_AtMostTheFileSize()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A001_0001.braw");
        var (data, m) = WriteUnbuffered(path, 5 * Block + 333);
        var v = new BlockVerifier(m);

        long read = CacheWarm.VerifyAndWarm(path, v);

        Assert.Equal(data.Length, read);
        Assert.Equal(data.Length, v.FedBytes);   // spec 11: mode B reads the file once (≤ 1.05 ×)
        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
    }

    [Fact]
    public void Warm_FlippedByteOnTheDisk_Mismatch()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A001_0001.braw");
        var (_, m) = WriteUnbuffered(path, 3 * Block);
        // Read through the cache first: the flip below must still be seen (it goes past the cache, NTFS keeps it coherent).
        _ = File.ReadAllBytes(path);
        DiskFlip.Flip(path, Block + 5);
        var v = new BlockVerifier(m);

        CacheWarm.VerifyAndWarm(path, v);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, 1), v.Finish());
    }

    [Fact]
    public void Warm_TruncatedFile_SizeMismatch()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.R3D");
        var (_, m) = WriteUnbuffered(path, 2 * Block);
        using (var f = new FileStream(path, FileMode.Open)) f.SetLength(Block);
        var v = new BlockVerifier(m);

        CacheWarm.VerifyAndWarm(path, v);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, -1, Block), v.Finish());
    }

    [Fact]
    public void Warm_CancelledBeforeABlock_Throws_NothingVerifiedAfter()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.braw");
        var (_, m) = WriteUnbuffered(path, 4 * Block);
        var v = new BlockVerifier(m);
        int blocks = 0;

        Assert.Throws<OperationCanceledException>(() => CacheWarm.VerifyAndWarm(path, v, () => ++blocks <= 2));
        Assert.Equal(2 * Block, v.VerifiedBytes);
    }

    [Fact]
    public void Fits_HalfTheFreeMemory()
    {
        Assert.True(CacheWarm.Fits(4L << 30, available: 8L << 30));
        Assert.False(CacheWarm.Fits((4L << 30) + 1, available: 8L << 30));
        Assert.True(CacheWarm.AvailableMemory() > 0);
    }

    [Theory]
    [InlineData(ClipFormat.Video, "CLIP/A001C001.mov", TapMode.Http)]
    [InlineData(ClipFormat.Video, "M4ROOT/CLIP/C0001.MP4", TapMode.Http)]
    [InlineData(ClipFormat.Video, "XDROOT/Clip/C0001.MXF", TapMode.Http)]
    [InlineData(ClipFormat.Video, "PRIVATE/AVCHD/00000.MTS", TapMode.None)]
    [InlineData(ClipFormat.Braw, "A001_0001.braw", TapMode.CacheWarm)]
    [InlineData(ClipFormat.R3d, "A001.RDC/A001_001.R3D", TapMode.CacheWarm)]
    [InlineData(ClipFormat.Arriraw, "A001C001.mxf", TapMode.CacheWarm)]
    [InlineData(ClipFormat.CanonRaw, "A001.CRM", TapMode.None)]
    public void Registry_ModeA_ForFfmpegContainers_ModeB_ForSdks(ClipFormat format, string path, TapMode want) =>
        Assert.Equal(want, TapRegistry.ModeFor(new Clip { Name = "x", Path = path, Format = format, Segments = [path] }));
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CacheWarmTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0246: The type or namespace name 'TapMode' could not be found (are you missing a using directive or an assembly reference?)`; `CS0103: The name 'TapMode' does not exist in the current context`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/VerifyInProxy/CacheWarm.cs` (nový soubor)

```csharp
using System.Runtime.InteropServices;

namespace Offload.Core.VerifyInProxy;

/// How the proxy's reads of a clip verify its copy (R3, R10, spec 5 "registr kodeků").
public enum TapMode
{
    /// Not through the proxy: the fill-in reads it the classic way (other containers, other segments, no proxy).
    None,
    /// Mode A: ffmpeg decodes it itself — it reads through the tap (RangeServer). MOV, MP4, MXF.
    Http,
    /// Mode B: a vendor SDK reads it (BRAW, R3D, ARRIRAW): Offload reads it first, through the cache, and verifies.
    CacheWarm,
}

public static class TapRegistry
{
    static readonly string[] HttpExtensions = [".mov", ".mp4", ".mxf"];

    public static TapMode ModeFor(Clip clip) => clip.Format switch
    {
        ClipFormat.Video when HttpExtensions.Contains(Path.GetExtension(clip.Path), StringComparer.OrdinalIgnoreCase) => TapMode.Http,
        ClipFormat.Braw or ClipFormat.R3d or ClipFormat.Arriraw => TapMode.CacheWarm,
        _ => TapMode.None,
    };
}

/// Mode B (spec 5, spec 8): the clip's files are evicted from the file cache (open and close a NO_BUFFERING handle —
/// measured in plan 10: the next cached read then comes from the disk, NTFS and exFAT), then read once in order through
/// the cache, every block into the verifier. Nothing of the file is in the cache before that read — it was written past
/// the cache (phase 1, R8) and just evicted — so the read comes from the disk and verifies it; the SDK then decodes from
/// the cache, the disk is read once (spec 11: ≤ 1.05 × the file). A clip larger than half the free RAM is not warmed (the
/// spec's windows of 4 GiB ahead of the SDK would need the SDK's own reading pace — not in v1): it is verified the
/// classic way after its proxy.
public static partial class CacheWarm
{
    /// The free physical memory now (GlobalMemoryStatusEx); 0 when unknown.
    public static long AvailableMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailPhys : 0;
    }

    /// Whether `bytes` of a clip can stay in the cache until the SDK reads them: at most half the free RAM (`available`:
    /// tests give it; null = AvailableMemory()).
    public static bool Fits(long bytes, long? available = null) => bytes <= (available ?? AvailableMemory()) / 2;

    /// Evicts `path` from the cache, then reads it once through the cache into `verifier`; returns the bytes read.
    /// `beforeBlock` false or `ct` = OperationCanceledException; IOError when the file cannot be read.
    public static long VerifyAndWarm(string path, BlockVerifier verifier, Func<bool>? beforeBlock = null, Action<int>? progress = null,
        CancellationToken ct = default)
    {
        using (NativeIO.OpenRead(path, unbuffered: true)) { }   // closing the last handle drops the cached pages
        using var h = NativeIO.OpenRead(path, unbuffered: false);
        verifier.ObserveLength(RandomAccess.GetLength(h));
        int size = verifier.Manifest.BlockSize;
        var buffer = new byte[size];
        long total = 0;
        for (long off = 0; ; off += size)
        {
            ct.ThrowIfCancellationRequested();
            if (beforeBlock?.Invoke() == false) throw new OperationCanceledException();
            int n = NativeIO.Read(h, buffer, off, path);
            if (n <= 0) break;
            verifier.Feed(off, buffer.AsSpan(0, n));
            total += n;
            progress?.Invoke(n);
            if (n < size) break;
        }
        return total;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~CacheWarmTests"`

Expected: PASS — `Failed: 0, Passed: 13, Skipped: 0, Total: 13` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1587` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 9: režim B — vyhození souboru z cache a jedno ověřující čtení přes cache před SDK; registr formátů (A = MOV/MP4/MXF, B = BRAW/R3D/ARRIRAW).
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: cache eviction and verifying cache warm-up for SDK formats (plan 10, task 9)

CacheWarm evicts a clip from the file cache, then reads it once through the cache into the
verifier, so the SDK decodes from RAM and the disk is read once. Clips larger than half the free
RAM are verified by the fill-in. TapRegistry picks mode A (ffmpeg) or B (SDK).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\VerifyInProxy\CacheWarm.cs windows\tests\Offload.Core.Tests\CacheWarmTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

