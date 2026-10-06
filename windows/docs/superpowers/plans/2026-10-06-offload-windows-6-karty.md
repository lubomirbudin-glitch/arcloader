# Offload pro Windows — plán 6: karty a bezpečnost dat (úkoly 1–9) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Skupina A návrhu 15 vylepšení — Offload pozná už zálohovanou kartu, umí do zálohy doplnit jen nové klipy, navrhne název karty z klipů, ověří zálohu kdykoli později proti ASC MHL, zkontroluje místo před startem a všechny hlášky kontrol jsou česky; výsledek je verze 0.2.0.

**Architecture:** Jádro dostane nové jmenné prostory `Offload.Core.History` (otisk karty, `history.json`, shoda), `Offload.Core.Verification` (čtení ASC MHL, ověření, report ověření), `Offload.Core.Append` (plán doplnění) a `Offload.Core.Space` (odhad a součet místa); engine umí doplnění (`PrepareAppend`, generace MHL N) a kontrolu místa po svazcích. Rozhodování GUI (životní cyklus zjišťování karty, texty, okno „Karty už jsou zálohované“, ověření ve frontě, kontrola místa) je v `Offload.Core.Desktop` a testuje se bez okna; WPF jen váže. CLI dostane `append`, nový `verify` a české hlášky.

**Tech Stack:** C# / .NET 10, WPF, xUnit, `System.IO.Hashing` (už v projektu), BCL (`IncrementalHash`, `GZipStream`, `XDocument`, pojmenovaný `Mutex`), ffmpeg jen v testech `[FfmpegFact]`, Microsoft Edge jen v testech `[EdgeFact]`.

**Spec:** `windows/docs/superpowers/specs/2026-10-06-offload-vylepseni-design.md` — skupina A (§2.1–2.6), průřezové věci (§5), plán 6 (§6.1). Plán má dvě části: tento soubor (úkoly 1–9) a `windows/docs/superpowers/plans/2026-10-06-offload-windows-6-karty-2.md` (úkoly 10–19).

## Global Constraints

- Žádný nový NuGet balíček — jen BCL .NET 10 (`System.Security.Cryptography`, `System.IO.Compression.GZipStream`, `System.Xml.Linq`), `System.IO.Hashing` už je, P/Invoke.
- Texty UI česky, doslova podle návrhu; identifikátory v kódu anglicky. Logy nástrojů (`app.log`, stderr ffmpeg/braw-decode), TXT report (sdílený s Macem) a `--help` zůstávají anglicky (§2.6).
- UI vlákno nikdy nečeká na disk ani síť — historie, otisk karty, kontrola místa, ověření: vše `Task.Run` + výsledek přes dispatcher, jako dnes `RefreshLut` / `DestinationViewModel.Probe`.
- Na kartu se nikdy nezapisuje — otisk karty, návrh názvu i doplnění kartu jen čtou (výčet složek, `FileReader`).
- Formáty na disku čitelné Macem: nové soubory a klíče jsou jen **přidané** (Swift `Codable` neznámé klíče ignoruje); popis do `docs/FORMATY.md` (úkol 18).
- Testovatelné bez kamer: dočasné složky, syntetické klipy z ffmpeg `testsrc2`, ručně psané MHL/JSON, falešné volné místo (`OffloadOptions.FreeSpace`, úkol 16).
- Mac později: `mac/` se nemění; co má Mac převzít, jde do `docs/VYVOJ-MAC.md` „Naplánováno“ (úkol 18).
- GUI se v testech nikdy nespouští (`Offload.exe` ne). Logika oken je v `Offload.Core.Desktop` a testuje se tam; WPF části se ověřují `dotnet build` (XAML se kompiluje) a ručním testem (úkol 19).
- R15: burn-in beze změny (výchozí zapnuto). R16: MD5 jen navíc k xxh64 — to je plán 7; v plánu 6 se MD5 jen čte při ověření zálohy (R9).
- Verze: plán 6 → 0.2.0 (`0.2.0-win`, R22), nastaví úkol 18.
- `TreatWarningsAsErrors`: každý úkol končí `dotnet build` bez varování a celou zelenou sadou (`cd windows` → `dotnet test`; dnes 558 testů, po plánu ~760). `ProxyWatchdogTests` je časově citlivý — když pod zátěží celé sady jednou selže, spusťte ho znovu samostatně (`dotnet test --filter "FullyQualifiedName~ProxyWatchdogTests"`).
- Testy nikdy nezapíšou do skutečného `%APPDATA%\Offload`: od úkolu 5 `TestEnvironment` (module initializer) přesměruje `HistoryStore.Default` do `%TEMP%\offload-tests\history-<pid>`.
- Každý commit: řádek nahoře v sekci `## 2026-10-06 — plán 6: karty a bezpečnost dat` v `docs/VYVOJ-WINDOWS.md` (sekci založí úkol 1 nad sekcí `## 2026-10-06 — balíček ZIP`; nejnovější řádek nahoře, bez SHA); poslední řádek zprávy commitu přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Bloky „najít / nahradit“ uvádějí řádky podle stavu souboru **před** daným úkolem (u prvního zásahu do souboru = dnešní kód, commit `9e4026d`). Kód plánu byl postavený a otestovaný v kopii repa (všech 17 kódových úkolů se zkompiluje, celá sada zelená). Pracovní kopie má konce řádků CRLF (`core.autocrlf=true`), bloky v plánu LF — nástroj Edit je sjednotí; kdo upravuje skriptem, porovnává bez ohledu na `\r`.

## Review Focus

1. **Karta s 10 000+ soubory a hlubokým stromem, vytažená uprostřed skenu** → zjišťování nikdy nespadne ani nezablokuje UI, výsledek staré karty se zahodí. Testy: úkol 6 `CardInspectorTests.TenThousandFiles_UnderBudget`, `Unreadable_ErrorInTheInsight`, `Cancelled_Throws`; úkol 7 `CardInspectionsTests.SwappedCard_CancelsAndDiscards_ThenLooksAgain`.
2. **FAT32 karta po změně letního/zimního času** → pořád „stejná karta“, doplnění nehlásí konflikt; na exFAT je hodinový posun změna. Testy: úkol 3 `FileTimes_WholeHoursOnlyOnLocalTimeFilesystems`, `FileTimes_Close`; úkol 6 `Fat32_HourShift_IsTheSameCard`, `ExFat_HourShift_IsAChange`; úkol 13 `FatHourShift_IsNoConflict_ButOnExFatItIs`.
3. **Doplnění do složky, jejíž MHL vytvořil Mac / jiný nástroj, má víc generací nebo se cíle liší** → funguje pro cizí platné MHL, rozdílné cíle nic nezkopírují. Testy: úkol 10 `TwoGenerations_NewestWins_EveryHashType`; úkol 13 `MacBackup_Fixture`, `DifferentGenerations_Diverged`; úkol 14 `Generation2_OnBothDestinations_EarlierFilesUntouched`, `CancelledAppend_RecordsWhatIsDone_NoHistory_NextAppendCopiesTheRest`.
4. **`history.json` poškozený, ze zrušené úlohy nebo zapisovaný zároveň GUI a CLI** → odloží se, začne se prázdný, nikdy neblokuje; zrušená úloha nic nezapíše; souběh nic neztratí. Testy: úkol 4 `Damaged_SetAside_StartsEmpty_NextRecordWorks`, `TwoWriters_InTwoThreads_KeepEveryEntry`, `Busy_SkipsTheEntry_AfterTheTimeout`, `NewerVersion_IsReadOnly`; úkol 5 `Cancelled_IsNotRecorded`, `BrokenHistoryFolder_NeverFailsTheJob`.
5. **Místo: dvě karty do fronty na tentýž disk + běžící úloha + složka pro proxy na stejném svazku** → požadavky se sčítají po svazcích, ne po cestách. Testy: úkol 16 `SameVolume_DestinationsAndProxyFolder_AddUp`, `TwoCardsOneDestination_AddUp`, `NotEnoughForTheProxies_StopsBeforeCopying`; úkol 17 `TwoCards_RunningJob_ProxyFolder_OnOneVolume`.

## Rozhodnutí tam, kde návrh nestačil (platí pro oba soubory plánu)

- **Chyby parseru `System.CommandLine` česky (§2.6, „ověřit při implementaci“):** balíček 2.0.12 satelit `cs` má, ale novější hlášky (např. „Option '--dest' is required.“) přeložené nejsou. CLI proto nastaví `cs-CZ`, povolí satelit `cs` a `--dest` už není povinná volba parseru — chybějící cíl hlásí jádro česky („Není zadaný žádný cíl.“).
- **Volba „Uložit report ověření“ (§2.4):** `OpenFolderDialog` neumí vlastní zaškrtávátko; volba je zaškrtávací položka menu *Nástroje* → „Ukládat report ověření“ (výchozí zapnuto, platí do zavření aplikace, žádný nový klíč nastavení).
- **Doplnění po zrušeném doplnění (§2.2):** zrušené doplnění zapíše `failed` u nedokončených souborů; pravidlo „soubor v záloze selhal → doplnit nejde“ by pak blokovalo každé další doplnění. Proto: `failed` v reportu **a soubor není na žádném cíli** = zkopíruje se znovu; `failed` a soubor na cíli je = „doplnit nejde, celá kopie“.
- **Časy v reportu mají celé sekundy:** porovnání karta ↔ report ↔ soubor na cíli u doplnění snese 2 s (FAT ukládá po 2 s) a na FAT/FAT32 navíc posun o celé hodiny (`FileTimes.Close`); historie porovnává přesně (`FileTimes.Same`), protože ukládá ticky.
- **Parametr místa v enginu (§2.5):** místo `Func<string root, long>` je `Func<SourceScan, SpaceExtra>` — požadavek proxy se počítá z klipů skenu, který vzniká až v enginu, a nese i složku pro proxy a rozpis do hlášky.
- **Ručně přidaná složka se v historii páruje jen podle `shape` (§2.1 krok 1):** složka, do které přibyly soubory, se proto nepozná a doplnění se u ní v GUI nenabídne (u svazků ano); CLI `append --into` funguje vždy.
- **Okno „Karty už jsou zálohované“ pro jednu kartu** používá jednotné číslo („Karta už je zálohovaná“, „Zkopírovat ji znovu celou?“); karta „změněná od zálohy“ se v okně neuvádí — její řádek už říká, že jde jen celá kopie.

---

### Task 1: České hlášky + `PreflightCode`

Spec §2.6 (tabulka 35 hlášek), §6.1 úkol 1. První, ať další úkoly píšou česky. Zavádí i `Format.BytesCs` („3,2 GB“), který používají hlášky všech dalších úkolů.

**Files:**
- Modify: `windows/src/Offload.Cli/BenchCommand.cs` (řádky 36–40)
- Modify: `windows/src/Offload.Cli/CliParse.cs` (řádky 19–23)
- Modify: `windows/src/Offload.Cli/CopyCommand.cs` (řádky 11–15, 49–57, 72–82, 98–102, 131–135)
- Modify: `windows/src/Offload.Cli/LutsCommand.cs` (řádky 12–16)
- Modify: `windows/src/Offload.Cli/Offload.Cli.csproj` (řádky 5–8)
- Modify: `windows/src/Offload.Cli/PortsCommand.cs` (řádky 1–4, 65–69)
- Modify: `windows/src/Offload.Cli/Program.cs` (řádky 1–3, 11–14)
- Modify: `windows/src/Offload.Cli/VerifyCommand.cs` (řádky 22–26, 47–51)
- Modify: `windows/src/Offload.Core/Bench.cs` (řádky 23–27)
- Modify: `windows/src/Offload.Core/Format.cs` (řádky 23–26)
- Modify: `windows/src/Offload.Core/Layout.cs` (řádky 60–66)
- Modify: `windows/src/Offload.Core/Luts.cs` (řádky 214–218, 223–227)
- Create: `windows/src/Offload.Core/Messages.cs`
- Modify: `windows/src/Offload.Core/Models.cs` (řádky 57–64)
- Modify: `windows/src/Offload.Core/OffloadEngine.Run.cs` (řádky 82–86)
- Modify: `windows/src/Offload.Core/OffloadEngine.cs` (řádky 26–83, 87–91)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 228–232, 295–299)
- Modify: `windows/src/Offload.Core/ProxyRenderer.cs` (řádky 97–108, 113–117, 183–187, 236–240, 304–308)
- Modify: `windows/src/Offload.Core/Scanner.cs` (řádky 82–86)
- Modify: `windows/src/Offload.Core/Verifier.cs` (řádky 21–25)
- Modify: `windows/tests/Offload.Core.Tests/FormatTests.cs` (řádky 23–25)
- Modify: `windows/tests/Offload.Core.Tests/JobTests.cs` (řádky 33–37)
- Modify: `windows/tests/Offload.Core.Tests/LutOptionTests.cs` (řádky 16–21)
- Create: `windows/tests/Offload.Core.Tests/PreflightMessagesTests.cs`
- Modify: `windows/tests/Offload.Core.Tests/PreflightTests.cs` (řádky 34–38, 41–45, 48–52, 57–61, 66–69, 73–77, 100–104, 108–112, 124–128, 135–139, 145–156)
- Modify: `windows/tests/Offload.Core.Tests/ProxyRendererTests.cs` (řádky 41–45, 47–51)
- Modify: `windows/tests/Offload.Core.Tests/ScannerTests.cs` (řádky 56–60)
- Modify: `windows/tests/Offload.Core.Tests/StageTwoTests.cs` (řádky 207–213)
- Modify: `windows/tests/Offload.Core.Tests/VerifierTests.cs` (řádky 67–71)
- Modify: `docs/VYVOJ-WINDOWS.md` (nová sekce nad řádkem 20 `## 2026-10-06 — balíček ZIP`)

**Interfaces:**
- Produces: `enum PreflightCode` (Other, ChunkSize, BufferCount, Date, SourceNotFound, NoDestination, DuplicateDestination, SourceIsOffload, SourceUnreadable, DestinationNotFound, DestinationNotWritable, DestinationUnreadable, SameVolumeAsSource, SharedVolume, EmptySource, BadNames, Fat32, NoSpace, NoHashRecord, NoFilesToRead); `OffloadError.Code (PreflightCode?)`, `OffloadError.Preflight(PreflightCode code, string message)` (stará přetížená `Preflight(string)` zmizí); statická třída `Msg` (metoda / konstanta na každou hlášku); `Format.BytesCs(long) → string` („3,2 GB“, „412 GB“); `OffloadEngine` interně rozdělený na `CheckOptions`, `CheckSource`, `FullRoots`, `CheckNotAnOffload`, `CheckDestinations`, `ScanSource`, `CheckFilesFit` (úkoly 14 a 16 je znovu použijí).

- [ ] **Step 1: Padající testy**

Nový `windows/tests/Offload.Core.Tests/PreflightMessagesTests.cs`:

```csharp
namespace Offload.Core.Tests;

/// Spec 2.6: every message of the checks before a copy is Czech, word for word, and carries its PreflightCode.
public class PreflightMessagesTests
{
    static OffloadOptions SameVolume() => new() { AllowSameDevice = true, AllowSourceVolume = true };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 1000, 1);
        return card;
    }

    static OffloadError Fail(Action<OffloadEngine> act, OffloadOptions? options = null) =>
        Assert.Throws<OffloadError>(() => act(new OffloadEngine(options ?? SameVolume())));

    /// Every scenario PreflightTests covers, with the exact Czech text it must produce.
    static List<(PreflightCode Code, string Expected, OffloadError Error)> Scenarios(TempDir dir)
    {
        var card = Card(dir);
        var d1 = dir.Sub("d1");
        var nope = Path.Combine(dir.Path, "nope");
        var missing = Path.Combine(dir.Path, "no-such-card");
        var empty = dir.Sub("empty");
        var offload = dir.Sub("old");
        TestFs.WriteRandom(Path.Combine(offload, "a.mov"), 10, 1);
        Directory.CreateDirectory(Path.Combine(offload, "_REPORT"));
        return
        [
            (PreflightCode.ChunkSize, "Velikost bloku musí být násobek 4096 B a nejvýš 1 GiB (zadáno 1000).",
                Fail(e => e.Prepare(card, [d1], null, null), new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true, ChunkSize = 1000 })),
            (PreflightCode.BufferCount, "Počet bufferů musí být aspoň 2 (zadáno 1).",
                Fail(e => e.Prepare(card, [d1], null, null), new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true, BufferCount = 1 })),
            (PreflightCode.Date, "Datum musí být ve tvaru RRRR-MM-DD (zadáno „2026-1-4“).", Fail(e => e.Prepare(card, [d1], null, "2026-1-4"))),
            (PreflightCode.SourceNotFound, $"Zdroj nenalezen: {missing}", Fail(e => e.Prepare(missing, [d1], null, null))),
            (PreflightCode.NoDestination, "Není zadaný žádný cíl.", Fail(e => e.Prepare(card, [], null, null))),
            (PreflightCode.DuplicateDestination, $"Cíl je zadaný dvakrát: {d1.ToUpperInvariant()}\\",
                Fail(e => e.Prepare(card, [d1, d1.ToUpperInvariant() + @"\"], null, null))),
            (PreflightCode.SourceIsOffload,
                "Zdroj vypadá jako dřívější offload (má _REPORT, _PROXY nebo ascmhl) — kopírujte původní kartu, nebo tyto složky odeberte.",
                Fail(e => e.Prepare(offload, [d1], null, null))),
            (PreflightCode.DestinationNotFound, $"Cíl nenalezen: {nope} — je disk připojený?", Fail(e => e.Prepare(card, [nope], null, null))),
            (PreflightCode.SameVolumeAsSource, $"Cíl {d1} je na stejném disku jako zdroj.",
                Fail(e => e.Prepare(card, [d1], null, null), new OffloadOptions { AllowSameDevice = true })),
            (PreflightCode.SharedVolume,
                $"Dva cíle jsou na stejném disku ({dir.Sub("d2")}) — to není druhá kopie. Povolit jde volbou --allow-same-device.",
                Fail(e => e.Prepare(card, [d1, dir.Sub("d2")], null, null), new OffloadOptions { AllowSourceVolume = true })),
            (PreflightCode.EmptySource, "Na zdroji nejsou žádné soubory.", Fail(e => e.Prepare(empty, [d1], null, null))),
        ];
    }

    [Fact]
    public void EveryScenario_ExactCzechText_AndCode()
    {
        using var dir = new TempDir();
        foreach (var (code, expected, error) in Scenarios(dir))
        {
            Assert.True(error.IsPreflight, expected);
            Assert.Equal(code, error.Code);
            Assert.Equal(expected, error.Message);
        }
    }

    [Fact]
    public void NoEnglishWords()
    {
        using var dir = new TempDir();
        string[] english = ["not found", "cannot", "must", "Destination", "Source"];
        foreach (var (_, _, error) in Scenarios(dir))
            foreach (var word in english)
                Assert.DoesNotContain(word, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextsWithoutAnEasyScenario()
    {
        Assert.Equal(@"Na cíli D:\Zaloha není dost místa: potřeba 412 GB, volno 398 GB.",
            Msg.NoSpace(@"D:\Zaloha", 412_000_000_000, 398_000_000_000));
        Assert.Equal(@"Na cíl nejde zapisovat: D:\Zaloha", Msg.DestinationNotWritable(@"D:\Zaloha"));
        Assert.Equal(@"Cíl nejde přečíst: D:\Zaloha", Msg.DestinationUnreadable(@"D:\Zaloha"));
        Assert.Equal(@"Zdroj nejde přečíst: E:\", Msg.SourceUnreadable(@"E:\"));
        Assert.Equal("FAT32 neumí uložit soubory od 4 GB (CLIP/A001C002.mov, 4.29 GB)",
            OffloadEngine.Fat32Problem("FAT32", [new("CLIP/A001C002.mov", 4L << 30, DateTime.UtcNow)]));
        Assert.Equal("Report se nepodařilo zapsat: disk full", Msg.ReportNotWritten("disk full"));
        Assert.Equal(@"Ve složce D:\A001 není ASC MHL ani seznam součtů — není s čím porovnat.", Msg.NoHashRecord(@"D:\A001"));
        Assert.Equal(@"V E:\ nejsou soubory ke čtení.", Msg.NoFilesToRead(@"E:\"));
    }

    [Fact]
    public void BadNames_ListAndCount()
    {
        var problems = Enumerable.Range(1, 22).Select(i => $"f{i}: {Msg.InvalidChar}").ToList();
        var text = Msg.BadNames(problems);
        Assert.StartsWith("Některé názvy Windows neumí uložit:\n  f1: obsahuje znak, který Windows nepovolí\n", text);
        Assert.EndsWith("\n  … a dalších 2", text);
        var scan = new SourceScan
        {
            Root = @"X:\",
            Files = [new("CLIP/A.MOV", 1, DateTime.UtcNow), new("CLIP/a.mov", 1, DateTime.UtcNow), new("CON.mov", 1, DateTime.UtcNow), new("b. ", 1, DateTime.UtcNow)],
            Dirs = ["CLIP"],
            Skipped = [],
        };
        Assert.Equal(
            ["CLIP/a.mov: stejný název jako CLIP/A.MOV, liší se jen velikostí písmen", "CON.mov: rezervovaný název zařízení ve Windows", "b. : končí tečkou nebo mezerou"],
            Scanner.WindowsProblems(scan));
    }

    [Fact]
    public void CliAndClipTexts()
    {
        Assert.Equal("Neznámý kodek proxy „av1“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.", Msg.UnknownProxyCodec("av1"));
        Assert.Equal("„x“ není kladné číslo.", Msg.NotPositiveNumber("x"));
        Assert.Equal("Blackmagic RAW: chybí dekodér", Msg.NoDecoder(ClipFormat.Braw.Label()));
        Assert.Equal("žádná ověřená kopie", Msg.NoVerifiedCopy);
    }
}
```

Úpravy existujících testů (anglické texty → kódy / české texty; `BytesCs` do `FormatTests`):

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 34–38 — najít:

```csharp

    [Fact]
    public void SourceMissing() => AssertPreflight("Source not found",
        e => e.Prepare(@"C:\offload-no-such-card", [Path.GetTempPath()], null, null));

```

nahradit:

```csharp

    [Fact]
    public void SourceMissing() => AssertPreflight(PreflightCode.SourceNotFound,
        e => e.Prepare(@"C:\offload-no-such-card", [Path.GetTempPath()], null, null));

```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 41–45 — najít:

```csharp
    {
        using var dir = new TempDir();
        AssertPreflight("No destination given", e => e.Prepare(Card(dir), [], null, null));
    }

```

nahradit:

```csharp
    {
        using var dir = new TempDir();
        AssertPreflight(PreflightCode.NoDestination, e => e.Prepare(Card(dir), [], null, null));
    }

```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 48–52 — najít:

```csharp
    {
        using var dir = new TempDir();
        AssertPreflight("Destination not found", e => e.Prepare(Card(dir), [Path.Combine(dir.Path, "nope")], null, null));
    }

```

nahradit:

```csharp
    {
        using var dir = new TempDir();
        AssertPreflight(PreflightCode.DestinationNotFound, e => e.Prepare(Card(dir), [Path.Combine(dir.Path, "nope")], null, null));
    }

```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 57–61 — najít:

```csharp
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSameDevice = true }).Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.Contains("same volume as the source", e.Message);
    }

```

nahradit:

```csharp
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSameDevice = true }).Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.Equal(PreflightCode.SameVolumeAsSource, e.Code);
    }

```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 66–69 — najít:

```csharp
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSourceVolume = true }).Prepare(Card(dir), [dir.Sub("d1"), dir.Sub("d2")], null, null));
        Assert.Contains("--allow-same-device", e.Message);
    }
```

nahradit:

```csharp
        var e = Assert.Throws<OffloadError>(() =>
            new OffloadEngine(new OffloadOptions { AllowSourceVolume = true }).Prepare(Card(dir), [dir.Sub("d1"), dir.Sub("d2")], null, null));
        Assert.Equal(PreflightCode.SharedVolume, e.Code);
        Assert.Contains("--allow-same-device", e.Message);
    }
```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 73–77 — najít:

```csharp
    {
        using var dir = new TempDir();
        AssertPreflight("Source contains no files", e => e.Prepare(dir.Sub("empty"), [dir.Sub("d1")], null, null));
    }

```

nahradit:

```csharp
    {
        using var dir = new TempDir();
        AssertPreflight(PreflightCode.EmptySource, e => e.Prepare(dir.Sub("empty"), [dir.Sub("d1")], null, null));
    }

```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 100–104 — najít:

```csharp
        var card = Card(dir);
        Directory.CreateDirectory(Path.Combine(card, folder));
        AssertPreflight("Source looks like an earlier offload", e => e.Prepare(card, [dir.Sub("d1")], null, null));
    }

```

nahradit:

```csharp
        var card = Card(dir);
        Directory.CreateDirectory(Path.Combine(card, folder));
        AssertPreflight(PreflightCode.SourceIsOffload, e => e.Prepare(card, [dir.Sub("d1")], null, null));
    }

```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 108–112 — najít:

```csharp
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        AssertPreflight("Destination given twice", e => e.Prepare(Card(dir), [d1, d1.ToUpperInvariant() + @"\"], null, null));
    }

```

nahradit:

```csharp
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        AssertPreflight(PreflightCode.DuplicateDestination, e => e.Prepare(Card(dir), [d1, d1.ToUpperInvariant() + @"\"], null, null));
    }

```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 124–128 — najít:

```csharp
                .Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.True(e.IsPreflight);
        Assert.Contains("ChunkSize", e.Message);
    }

```

nahradit:

```csharp
                .Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.True(e.IsPreflight);
        Assert.Equal(PreflightCode.ChunkSize, e.Code);
    }

```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 135–139 — najít:

```csharp
                .Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.True(e.IsPreflight);
        Assert.Contains("BufferCount", e.Message);
    }

```

nahradit:

```csharp
                .Prepare(Card(dir), [dir.Sub("d1")], null, null));
        Assert.True(e.IsPreflight);
        Assert.Equal(PreflightCode.BufferCount, e.Code);
    }

```

`windows/tests/Offload.Core.Tests/PreflightTests.cs`, řádky 145–156 — najít:

```csharp
    {
        using var dir = new TempDir();
        AssertPreflight("Date must be", e => e.Prepare(Card(dir), [dir.Sub("d1")], null, date));
    }

    static void AssertPreflight(string message, Action<OffloadEngine> act)
    {
        var e = Assert.Throws<OffloadError>(() => act(new OffloadEngine(SameVolume())));
        Assert.True(e.IsPreflight);
        Assert.Contains(message, e.Message);
    }
}
```

nahradit:

```csharp
    {
        using var dir = new TempDir();
        AssertPreflight(PreflightCode.Date, e => e.Prepare(Card(dir), [dir.Sub("d1")], null, date));
    }

    static void AssertPreflight(PreflightCode code, Action<OffloadEngine> act)
    {
        var e = Assert.Throws<OffloadError>(() => act(new OffloadEngine(SameVolume())));
        Assert.True(e.IsPreflight);
        Assert.Equal(code, e.Code);
    }
}
```
`windows/tests/Offload.Core.Tests/JobTests.cs`, řádky 33–37 — najít:

```csharp
        var s = job.Status();
        Assert.Equal(JobPhase.Failed, s.Phase);
        Assert.Contains("Source not found", s.Error);
        Assert.True(s.Phase.IsFinished());
    }
```

nahradit:

```csharp
        var s = job.Status();
        Assert.Equal(JobPhase.Failed, s.Phase);
        Assert.Contains("Zdroj nenalezen", s.Error);
        Assert.True(s.Phase.IsFinished());
    }
```
`windows/tests/Offload.Core.Tests/VerifierTests.cs`, řádky 67–71 — najít:

```csharp
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() => Verifier.Verify(dir.Sub("card")));
        Assert.Contains("No .xxh64 hash list", e.Message);
    }

```

nahradit:

```csharp
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() => Verifier.Verify(dir.Sub("card")));
        Assert.Equal(PreflightCode.NoHashRecord, e.Code);
    }

```
`windows/tests/Offload.Core.Tests/StageTwoTests.cs`, řádky 207–213 — najít:

```csharp
        Assert.True(s.SafeToEject);
        var clip = Assert.Single(report!.Clips);
        Assert.Contains("ffprobe not found", clip.ProbeError);
        Assert.Equal(ProxyStatus.Unsupported, clip.ProxyStatus);
        Assert.Equal("ffmpeg not found", clip.ProxyNote);
    }

```

nahradit:

```csharp
        Assert.True(s.SafeToEject);
        var clip = Assert.Single(report!.Clips);
        Assert.Contains("ffprobe nenalezen", clip.ProbeError);
        Assert.Equal(ProxyStatus.Unsupported, clip.ProxyStatus);
        Assert.Equal("ffmpeg nenalezen", clip.ProxyNote);
    }

```
`windows/tests/Offload.Core.Tests/ProxyRendererTests.cs`, řádky 41–45 — najít:

```csharp
    {
        using var none = new ProxyRenderer(new ProxySettings(), new ExternalTools { NoSearch = true }.Resolved());
        Assert.Equal("ffmpeg not found", none.UnsupportedReason(VideoClip("a.mov")));
        var ff = new ExternalTools { Ffmpeg = @"C:\Windows\System32\cmd.exe", NoSearch = true }.Resolved();
        using var some = new ProxyRenderer(new ProxySettings(), ff);
```

nahradit:

```csharp
    {
        using var none = new ProxyRenderer(new ProxySettings(), new ExternalTools { NoSearch = true }.Resolved());
        Assert.Equal("ffmpeg nenalezen", none.UnsupportedReason(VideoClip("a.mov")));
        var ff = new ExternalTools { Ffmpeg = @"C:\Windows\System32\cmd.exe", NoSearch = true }.Resolved();
        using var some = new ProxyRenderer(new ProxySettings(), ff);
```

`windows/tests/Offload.Core.Tests/ProxyRendererTests.cs`, řádky 47–51 — najít:

```csharp
        Assert.StartsWith("RED R3D", some.UnsupportedReason(new Clip { Name = "r", Path = "r.R3D", Format = ClipFormat.R3d, Segments = ["r.R3D"] }));
        Assert.StartsWith("Canon", some.UnsupportedReason(new Clip { Name = "c", Path = "c.CRM", Format = ClipFormat.CanonRaw, Segments = ["c.CRM"] }));
        Assert.Equal("unknown format", some.UnsupportedReason(new Clip { Name = "u", Path = "u", Format = ClipFormat.Unknown, Segments = ["u"] }));
    }

```

nahradit:

```csharp
        Assert.StartsWith("RED R3D", some.UnsupportedReason(new Clip { Name = "r", Path = "r.R3D", Format = ClipFormat.R3d, Segments = ["r.R3D"] }));
        Assert.StartsWith("Canon", some.UnsupportedReason(new Clip { Name = "c", Path = "c.CRM", Format = ClipFormat.CanonRaw, Segments = ["c.CRM"] }));
        Assert.Equal("neznámý formát", some.UnsupportedReason(new Clip { Name = "u", Path = "u", Format = ClipFormat.Unknown, Segments = ["u"] }));
    }

```
`windows/tests/Offload.Core.Tests/ScannerTests.cs`, řádky 56–60 — najít:

```csharp
        var problems = Scanner.WindowsProblems(scan);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("CLIP/a001.mov") && p.Contains("upper/lower case"));
        Assert.Contains(problems, p => p.StartsWith("CLIP/bad:name.mov"));
    }
```

nahradit:

```csharp
        var problems = Scanner.WindowsProblems(scan);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("CLIP/a001.mov") && p.Contains("liší se jen velikostí písmen"));
        Assert.Contains(problems, p => p.StartsWith("CLIP/bad:name.mov"));
    }
```
`windows/tests/Offload.Core.Tests/LutOptionTests.cs`, řádky 16–21 — najít:

```csharp
        Assert.Equal(LutMode.Custom, LutOption.Parse(@"C:\luts\show.cube", null, Cat).Mode);
        Assert.Equal(@"C:\luts\show.cube", LutOption.Parse(@"C:\luts\show.cube", null, Cat).Path);
        Assert.StartsWith("Unknown LUT", LutOption.Parse("nope", null, Cat).Error);
        Assert.StartsWith("Unknown fallback LUT", LutOption.Parse("auto", "nope", Cat).Error);
        Assert.Null(LutOption.Parse("auto", "none", Cat).Fallback);
    }
```

nahradit:

```csharp
        Assert.Equal(LutMode.Custom, LutOption.Parse(@"C:\luts\show.cube", null, Cat).Mode);
        Assert.Equal(@"C:\luts\show.cube", LutOption.Parse(@"C:\luts\show.cube", null, Cat).Path);
        Assert.StartsWith("Neznámý LUT „nope“", LutOption.Parse("nope", null, Cat).Error);
        Assert.StartsWith("Neznámý záložní LUT „nope“", LutOption.Parse("auto", "nope", Cat).Error);
        Assert.Null(LutOption.Parse("auto", "none", Cat).Fallback);
    }
```
`windows/tests/Offload.Core.Tests/FormatTests.cs`, řádky 23–25 — najít:

```csharp
    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.1.0-win", OffloadVersion.String);
}
```

nahradit:

```csharp
    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.1.0-win", OffloadVersion.String);

    // Spec 2.1/2.5: sizes inside Czech sentences.
    [Theory]
    [InlineData(12L, "12 B")]
    [InlineData(3_200_000_000L, "3,2 GB")]
    [InlineData(2_000_000_000L, "2 GB")]
    [InlineData(412_400_000_000L, "412 GB")]
    [InlineData(850_000_000L, "850 MB")]
    public void BytesCs_CzechDecimalComma(long bytes, string expected) => Assert.Equal(expected, Format.BytesCs(bytes));
}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `cd windows` → `dotnet test --filter "FullyQualifiedName~PreflightMessagesTests|FullyQualifiedName~PreflightTests|FullyQualifiedName~FormatTests"`
Expected: build FAIL — `PreflightCode`, `Msg`, `OffloadError.Code`, `Format.BytesCs` neexistují.

- [ ] **Step 3: Implementace**

Nový `windows/src/Offload.Core/Messages.cs`:

```csharp
namespace Offload.Core;

/// Which check before the copy stopped a job. Tests and the GUI tell the cases apart by the code, never by the text.
public enum PreflightCode
{
    Other,
    ChunkSize,
    BufferCount,
    Date,
    SourceNotFound,
    NoDestination,
    DuplicateDestination,
    SourceIsOffload,
    SourceUnreadable,
    DestinationNotFound,
    DestinationNotWritable,
    DestinationUnreadable,
    SameVolumeAsSource,
    SharedVolume,
    EmptySource,
    BadNames,
    Fat32,
    NoSpace,
    NoHashRecord,
    NoFilesToRead,
}

/// Every message a user sees from checks and validation (GUI job card, CLI) — Czech, one method per message, no resx.
/// Tool logs (app.log, ffmpeg/braw-decode stderr), the TXT report (shared with the Mac) and --help stay English.
public static class Msg
{
    public static string ChunkSize(int n) => $"Velikost bloku musí být násobek 4096 B a nejvýš 1 GiB (zadáno {n}).";
    public static string BufferCount(int n) => $"Počet bufferů musí být aspoň 2 (zadáno {n}).";
    public static string Date(string d) => $"Datum musí být ve tvaru RRRR-MM-DD (zadáno „{d}“).";
    public static string SourceNotFound(string path) => $"Zdroj nenalezen: {path}";
    public const string NoDestination = "Není zadaný žádný cíl.";
    public static string DuplicateDestination(string root) => $"Cíl je zadaný dvakrát: {root}";
    public const string SourceIsOffload =
        "Zdroj vypadá jako dřívější offload (má _REPORT, _PROXY nebo ascmhl) — kopírujte původní kartu, nebo tyto složky odeberte.";
    public static string SourceUnreadable(string path) => $"Zdroj nejde přečíst: {path}";
    public static string DestinationNotFound(string root) => $"Cíl nenalezen: {root} — je disk připojený?";
    public static string DestinationNotWritable(string root) => $"Na cíl nejde zapisovat: {root}";
    public static string DestinationUnreadable(string root) => $"Cíl nejde přečíst: {root}";
    public static string SameVolumeAsSource(string root) => $"Cíl {root} je na stejném disku jako zdroj.";
    public static string SharedVolume(string root) =>
        $"Dva cíle jsou na stejném disku ({root}) — to není druhá kopie. Povolit jde volbou --allow-same-device.";
    public const string EmptySource = "Na zdroji nejsou žádné soubory.";

    /// The first 20 problems, one per line, then how many more.
    public static string BadNames(IReadOnlyList<string> problems)
    {
        var more = problems.Count > 20 ? $"\n  … a dalších {problems.Count - 20}" : "";
        return "Některé názvy Windows neumí uložit:\n  " + string.Join("\n  ", problems.Take(20)) + more;
    }

    public static string SameNameCase(string path, string other) => $"{path}: stejný název jako {other}, liší se jen velikostí písmen";
    public const string InvalidChar = "obsahuje znak, který Windows nepovolí";
    public const string EndsWithDotOrSpace = "končí tečkou nebo mezerou";
    public const string ReservedName = "rezervovaný název zařízení ve Windows";
    public static string Fat32(string filesystem, string path, long size) =>
        $"{filesystem} neumí uložit soubory od 4 GB ({path}, {Format.Bytes(size)})";
    public static string NoSpace(string root, long need, long free) =>
        $"Na cíli {root} není dost místa: potřeba {Format.BytesCs(need)}, volno {Format.BytesCs(free)}.";
    public static string ReportNotWritten(string details) => $"Report se nepodařilo zapsat: {details}";
    public static string NoHashRecord(string dir) => $"Ve složce {dir} není ASC MHL ani seznam součtů — není s čím porovnat.";
    public static string NoFilesToRead(string path) => $"V {path} nejsou soubory ke čtení.";

    // CLI
    public const string CliError = "Chyba: ";
    public static string UnknownProxyCodec(string? x) =>
        $"Neznámý kodek proxy „{x}“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.";
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string ProxyFolderNotFound(string path) => $"Složka pro proxy nenalezena: {path}";
    public const string ProxyFolderNoEffect = "Poznámka: --proxy-folder bez --proxy nic nedělá.";
    public const string Cancelling = "Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…";
    public static string NotPositiveNumber(string? s) => $"„{s}“ není kladné číslo.";
    public static string UnknownFallbackLut(string x, string ids) => $"Neznámý záložní LUT „{x}“. Vestavěné: {ids}";
    public static string UnknownLut(string x, string ids) => $"Neznámý LUT „{x}“. Použijte auto, none, cestu k .cube nebo jedno z: {ids}";
    public const string NoBuiltinLuts = "Vestavěné LUTy nenalezeny (chybí složka luts).";

    // Clip notes (report, job card)
    public const string NoVerifiedCopy = "žádná ověřená kopie";
    public const string FfmpegNotFound = "ffmpeg nenalezen";
    public const string FfprobeNotFound = "ffprobe nenalezen";
    public const string UnknownFormat = "neznámý formát";
    public const string NoDecoderShort = "chybí dekodér";
    public static string NoDecoder(string format) => $"{format}: chybí dekodér";
}
```

`OffloadError` s kódem:

`windows/src/Offload.Core/Models.cs`, řádky 57–64 — najít:

```csharp
{
    public bool IsPreflight { get; }

    OffloadError(string message, bool preflight) : base(message) => IsPreflight = preflight;

    public static OffloadError Preflight(string message) => new(message, true);
    public static OffloadError Failed(string message) => new(message, false);
}
```

nahradit:

```csharp
{
    public bool IsPreflight { get; }
    /// Which check failed (preflight errors only); tests and the GUI tell the cases apart by it, not by the text.
    public PreflightCode? Code { get; }

    OffloadError(string message, bool preflight, PreflightCode? code) : base(message)
    {
        IsPreflight = preflight;
        Code = code;
    }

    public static OffloadError Preflight(PreflightCode code, string message) => new(message, true, code);
    public static OffloadError Failed(string message) => new(message, false, null);
}
```

`Format.BytesCs`:

`windows/src/Offload.Core/Format.cs`, řádky 23–26 — najít:

```csharp
    }

    public static string Rate(long bytes, double seconds) =>
        seconds > 0 ? (bytes / seconds / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "-";
```

nahradit:

```csharp
    }

    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// A size inside a Czech sentence: "3,2 GB", "2 GB", "412 GB", "850 MB", "12 B" — decimal units like Bytes, at most
    /// one decimal place below 10, none above.
    public static string BytesCs(long b)
    {
        double v = b;
        int i = 0;
        while (v >= 1000 && i < Units.Length - 1) { v /= 1000; i++; }
        if (i == 0) return $"{b} B";
        return v.ToString(v < 10 ? "0.#" : "0", Cs) + " " + Units[i];
    }

    public static string Rate(long bytes, double seconds) =>
        seconds > 0 ? (bytes / seconds / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "-";
```

`OffloadEngine.Prepare` rozdělený na kontroly s českými hláškami (pořadí kontrol beze změny: volby, datum, zdroj, cíle zadané, zdroj není offload, cíle, sken, místo):

`windows/src/Offload.Core/OffloadEngine.cs`, řádky 26–83 — najít:

```csharp
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date)
    {
        if (Options.ChunkSize <= 0 || Options.ChunkSize % 4096 != 0 || Options.ChunkSize > 1 << 30)
            throw OffloadError.Preflight($"ChunkSize must be a positive multiple of 4096 and at most 1 GiB (got {Options.ChunkSize})");
        if (Options.BufferCount < 2)
            throw OffloadError.Preflight($"BufferCount must be at least 2 (got {Options.BufferCount})");
        if (date != null && !DateFormat.IsMatch(date))
            throw OffloadError.Preflight($"Date must be YYYY-MM-DD (got \"{date}\")");
        if (!Directory.Exists(source)) throw OffloadError.Preflight($"Source not found: {source}");
        source = Path.GetFullPath(source);
        var roots = destRoots.Select(Path.GetFullPath).ToList();
        if (roots.Count == 0) throw OffloadError.Preflight("No destination given");
        for (int i = 0; i < roots.Count; i++)
            for (int j = 0; j < i; j++)
                if (string.Equals(roots[i].TrimEnd(Path.DirectorySeparatorChar), roots[j].TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw OffloadError.Preflight($"Destination given twice: {roots[i]}");
        foreach (var marker in new[] { Layout.ReportDirName, "_PROXY", "ascmhl" })
            if (Directory.EnumerateDirectories(source).Any(d => string.Equals(Path.GetFileName(d), marker, StringComparison.OrdinalIgnoreCase)))
                throw OffloadError.Preflight("Source looks like an earlier offload (has _REPORT/_PROXY/ascmhl) — copy its parent card instead, or remove these folders");
        var sourceVolume = NativeIO.VolumeId(source) ?? throw OffloadError.Preflight($"Cannot stat source {source}");

        var volumes = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) throw OffloadError.Preflight($"Destination not found: {root}");
            if (!IsWritable(root)) throw OffloadError.Preflight($"Destination not writable: {root}");
            var volume = NativeIO.VolumeId(root) ?? throw OffloadError.Preflight($"Cannot stat destination {root}");
            if (volume == sourceVolume && !Options.AllowSourceVolume)
                throw OffloadError.Preflight($"Destination {root} is on the same volume as the source");
            if (volumes.Contains(volume) && !Options.AllowSameDevice)
                throw OffloadError.Preflight($"Destinations share a volume ({root}) — that is not a second copy. Use --allow-same-device to override.");
            volumes.Add(volume);
        }

        var scan = Scanner.Scan(source);
        if (scan.Files.Count == 0) throw OffloadError.Preflight("Source contains no files");
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0)
        {
            var more = problems.Count > 20 ? $"\n  … and {problems.Count - 20} more" : "";
            throw OffloadError.Preflight("Names Windows cannot store:\n  " + string.Join("\n  ", problems.Take(20)) + more);
        }

        long needed = scan.TotalBytes + scan.Files.Count * 64L * 1024 + (64L << 20);
        foreach (var root in roots)
        {
            if (Fat32Problem(NativeIO.FilesystemType(root), scan.Files) is string fat)
                throw OffloadError.Preflight($"{root}: {fat}");
            long free = NativeIO.AvailableBytes(root) ?? 0;
            if (free < needed)
                throw OffloadError.Preflight($"Not enough space on {root}: need {Format.Bytes(needed)}, free {Format.Bytes(free)}");
        }

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots };
    }

```

nahradit:

```csharp
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date)
    {
        CheckOptions();
        if (date != null && !DateFormat.IsMatch(date)) throw OffloadError.Preflight(PreflightCode.Date, Msg.Date(date));
        source = CheckSource(source);
        var roots = FullRoots(destRoots);
        CheckNotAnOffload(source);
        CheckDestinations(source, roots);
        var scan = ScanSource(source);
        CheckFilesFit(roots, scan.Files, scan.TotalBytes);

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots };
    }

    void CheckOptions()
    {
        if (Options.ChunkSize <= 0 || Options.ChunkSize % 4096 != 0 || Options.ChunkSize > 1 << 30)
            throw OffloadError.Preflight(PreflightCode.ChunkSize, Msg.ChunkSize(Options.ChunkSize));
        if (Options.BufferCount < 2)
            throw OffloadError.Preflight(PreflightCode.BufferCount, Msg.BufferCount(Options.BufferCount));
    }

    /// The source exists; returns its full path.
    static string CheckSource(string source)
    {
        if (!Directory.Exists(source)) throw OffloadError.Preflight(PreflightCode.SourceNotFound, Msg.SourceNotFound(source));
        return Path.GetFullPath(source);
    }

    /// Full paths of the destination roots: at least one, none twice.
    static List<string> FullRoots(IReadOnlyList<string> destRoots)
    {
        var roots = destRoots.Select(Path.GetFullPath).ToList();
        if (roots.Count == 0) throw OffloadError.Preflight(PreflightCode.NoDestination, Msg.NoDestination);
        for (int i = 0; i < roots.Count; i++)
            for (int j = 0; j < i; j++)
                if (string.Equals(roots[i].TrimEnd(Path.DirectorySeparatorChar), roots[j].TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw OffloadError.Preflight(PreflightCode.DuplicateDestination, Msg.DuplicateDestination(roots[i]));
        return roots;
    }

    /// A folder with _REPORT, _PROXY or ascmhl is a destination of an earlier offload, not a card.
    static void CheckNotAnOffload(string source)
    {
        foreach (var marker in new[] { Layout.ReportDirName, Layout.ProxyDirName, Mhl.DirName })
            if (Directory.EnumerateDirectories(source).Any(d => string.Equals(Path.GetFileName(d), marker, StringComparison.OrdinalIgnoreCase)))
                throw OffloadError.Preflight(PreflightCode.SourceIsOffload, Msg.SourceIsOffload);
    }

    /// Every destination exists, is writable, is not on the source's volume and (unless allowed) not on another
    /// destination's volume.
    void CheckDestinations(string source, IReadOnlyList<string> roots)
    {
        var sourceVolume = NativeIO.VolumeId(source) ?? throw OffloadError.Preflight(PreflightCode.SourceUnreadable, Msg.SourceUnreadable(source));
        var volumes = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) throw OffloadError.Preflight(PreflightCode.DestinationNotFound, Msg.DestinationNotFound(root));
            if (!IsWritable(root)) throw OffloadError.Preflight(PreflightCode.DestinationNotWritable, Msg.DestinationNotWritable(root));
            var volume = NativeIO.VolumeId(root) ?? throw OffloadError.Preflight(PreflightCode.DestinationUnreadable, Msg.DestinationUnreadable(root));
            if (volume == sourceVolume && !Options.AllowSourceVolume)
                throw OffloadError.Preflight(PreflightCode.SameVolumeAsSource, Msg.SameVolumeAsSource(root));
            if (volumes.Contains(volume) && !Options.AllowSameDevice)
                throw OffloadError.Preflight(PreflightCode.SharedVolume, Msg.SharedVolume(root));
            volumes.Add(volume);
        }
    }

    /// The card's files; none at all, or names Windows cannot store, stop the job.
    static SourceScan ScanSource(string source)
    {
        var scan = Scanner.Scan(source);
        if (scan.Files.Count == 0) throw OffloadError.Preflight(PreflightCode.EmptySource, Msg.EmptySource);
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0) throw OffloadError.Preflight(PreflightCode.BadNames, Msg.BadNames(problems));
        return scan;
    }

    /// The FAT32 limit and free space on every destination for `files` (all of them, or only the new ones of an append).
    void CheckFilesFit(IReadOnlyList<string> roots, IReadOnlyList<SourceFile> files, long totalBytes)
    {
        long needed = totalBytes + files.Count * 64L * 1024 + (64L << 20);
        foreach (var root in roots)
        {
            if (Fat32Problem(NativeIO.FilesystemType(root), files) is string fat)
                throw OffloadError.Preflight(PreflightCode.Fat32, $"{root}: {fat}");
            long free = NativeIO.AvailableBytes(root) ?? 0;
            if (free < needed) throw OffloadError.Preflight(PreflightCode.NoSpace, Msg.NoSpace(root, needed, free));
        }
    }

```

`windows/src/Offload.Core/OffloadEngine.cs`, řádky 87–91 — najít:

```csharp
        if (!filesystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase)) return null;
        var big = files.FirstOrDefault(f => f.Size > uint.MaxValue);
        return big == null ? null : $"{filesystem} cannot store files of 4 GB or more ({big.RelPath}, {Format.Bytes(big.Size)})";
    }

```

nahradit:

```csharp
        if (!filesystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase)) return null;
        var big = files.FirstOrDefault(f => f.Size > uint.MaxValue);
        return big == null ? null : Msg.Fat32(filesystem, big.RelPath, big.Size);
    }

```

Ostatní hlášky jádra (§2.6 řádky 16–21, 22, 33–35):

`windows/src/Offload.Core/OffloadEngine.Run.cs`, řádky 82–86 — najít:

```csharp
            }
            SetPhase(CopyPhase.Done);
            if (reportErrors.Count > 0) throw OffloadError.Failed("Report could not be written: " + string.Join("; ", reportErrors));
            return report;
        }
```

nahradit:

```csharp
            }
            SetPhase(CopyPhase.Done);
            if (reportErrors.Count > 0) throw OffloadError.Failed(Msg.ReportNotWritten(string.Join("; ", reportErrors)));
            return report;
        }
```
`windows/src/Offload.Core/Scanner.cs`, řádky 82–86 — najít:

```csharp
        foreach (var p in scan.Dirs.Concat(scan.Files.Select(f => f.RelPath)))
        {
            if (seen.TryGetValue(p, out var other)) problems.Add($"{p}: same name as {other} except for upper/lower case");
            else seen[p] = p;
            var reason = p.Split('/').Select(Layout.InvalidNameReason).FirstOrDefault(r => r != null);
```

nahradit:

```csharp
        foreach (var p in scan.Dirs.Concat(scan.Files.Select(f => f.RelPath)))
        {
            if (seen.TryGetValue(p, out var other)) problems.Add(Msg.SameNameCase(p, other));
            else seen[p] = p;
            var reason = p.Split('/').Select(Layout.InvalidNameReason).FirstOrDefault(r => r != null);
```
`windows/src/Offload.Core/Layout.cs`, řádky 60–66 — najít:

```csharp
    public static string? InvalidNameReason(string name)
    {
        if (name.Any(c => c < 0x20 || InvalidChars.Contains(c))) return "contains a character Windows does not allow";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "ends with a dot or space";
        if (ReservedNames.Contains(BaseName(name))) return "reserved device name on Windows";
        return null;
    }
```

nahradit:

```csharp
    public static string? InvalidNameReason(string name)
    {
        if (name.Any(c => c < 0x20 || InvalidChars.Contains(c))) return Msg.InvalidChar;
        if (name.EndsWith('.') || name.EndsWith(' ')) return Msg.EndsWithDotOrSpace;
        if (ReservedNames.Contains(BaseName(name))) return Msg.ReservedName;
        return null;
    }
```
`windows/src/Offload.Core/Verifier.cs`, řádky 21–25 — najít:

```csharp
        var reportDir = Path.Combine(cardPath, Layout.ReportDirName);
        var list = Directory.Exists(reportDir) ? Directory.GetFiles(reportDir, "*.xxh64").FirstOrDefault() : null;
        if (list == null) throw OffloadError.Preflight($"No .xxh64 hash list in {reportDir}");

        var expected = new List<(string Hash, string Path)>();
```

nahradit:

```csharp
        var reportDir = Path.Combine(cardPath, Layout.ReportDirName);
        var list = Directory.Exists(reportDir) ? Directory.GetFiles(reportDir, "*.xxh64").FirstOrDefault() : null;
        if (list == null) throw OffloadError.Preflight(PreflightCode.NoHashRecord, Msg.NoHashRecord(cardPath));

        var expected = new List<(string Hash, string Path)>();
```
`windows/src/Offload.Core/Bench.cs`, řádky 23–27 — najít:

```csharp
        else
            throw new IOError("open", path, 2);
        if (targets.Count == 0) throw OffloadError.Preflight($"No files to read in {path}");

        var lines = new List<Line>();
```

nahradit:

```csharp
        else
            throw new IOError("open", path, 2);
        if (targets.Count == 0) throw OffloadError.Preflight(PreflightCode.NoFilesToRead, Msg.NoFilesToRead(path));

        var lines = new List<Line>();
```
`windows/src/Offload.Core/OffloadJob.cs`, řádky 228–232 — najít:

```csharp
            if (GoodDest(clip) is not int d)
            {
                entries[i].ProbeError = "no verified copy";
                continue;
            }
```

nahradit:

```csharp
            if (GoodDest(clip) is not int d)
            {
                entries[i].ProbeError = Msg.NoVerifiedCopy;
                continue;
            }
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 295–299 — najít:

```csharp
            {
                entries[i].ProxyStatus = ProxyStatus.Failed;
                entries[i].ProxyNote = entries[i].ProbeError ?? "no verified copy";
                Update(s => s with { ProxiesFailed = s.ProxiesFailed + 1 });
                continue;
```

nahradit:

```csharp
            {
                entries[i].ProxyStatus = ProxyStatus.Failed;
                entries[i].ProxyNote = entries[i].ProbeError ?? Msg.NoVerifiedCopy;
                Update(s => s with { ProxiesFailed = s.ProxiesFailed + 1 });
                continue;
```
`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 97–108 — najít:

```csharp
    public string? UnsupportedReason(Clip clip)
    {
        if (Tools.Ffmpeg == null) return "ffmpeg not found";
        return clip.Format switch
        {
            ClipFormat.Video => null,
            ClipFormat.Unknown => "unknown format",
            _ => backends.TryGetValue(clip.Format, out var b)
                ? (b.IsAvailable(Tools) ? null : b.MissingHint)
                : $"{clip.Format.Label()}: no decoder available",
        };
    }
```

nahradit:

```csharp
    public string? UnsupportedReason(Clip clip)
    {
        if (Tools.Ffmpeg == null) return Msg.FfmpegNotFound;
        return clip.Format switch
        {
            ClipFormat.Video => null,
            ClipFormat.Unknown => Msg.UnknownFormat,
            _ => backends.TryGetValue(clip.Format, out var b)
                ? (b.IsAvailable(Tools) ? null : b.MissingHint)
                : Msg.NoDecoder(clip.Format.Label()),
        };
    }
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 113–117 — najít:

```csharp
        if (clip.Format == ClipFormat.Video)
        {
            var ffprobe = Tools.Ffprobe ?? throw new ToolError("ffprobe", -1, "ffprobe not found");
            return FFProbe.Probe(ffprobe, path);
        }
```

nahradit:

```csharp
        if (clip.Format == ClipFormat.Video)
        {
            var ffprobe = Tools.Ffprobe ?? throw new ToolError("ffprobe", -1, Msg.FfprobeNotFound);
            return FFProbe.Probe(ffprobe, path);
        }
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 183–187 — najít:

```csharp
        if (clip.Format == ClipFormat.Video)
            return new FrameSource { InputArgs = hwaccel ? ["-hwaccel", "auto", "-i", path] : ["-i", path] };
        if (!backends.TryGetValue(clip.Format, out var b)) throw new ToolError(clip.Format.Label(), -1, "no decoder");
        return Logged(() => b.Source(path, info, Settings, look, Tools, workDir, singleFrameAt));
    }
```

nahradit:

```csharp
        if (clip.Format == ClipFormat.Video)
            return new FrameSource { InputArgs = hwaccel ? ["-hwaccel", "auto", "-i", path] : ["-i", path] };
        if (!backends.TryGetValue(clip.Format, out var b)) throw new ToolError(clip.Format.Label(), -1, Msg.NoDecoderShort);
        return Logged(() => b.Source(path, info, Settings, look, Tools, workDir, singleFrameAt));
    }
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 236–240 — najít:

```csharp
    public List<Thumbnail> Thumbnails(Clip clip, string cardPath, ClipInfo info, LutUse? lut, Func<bool>? isCancelled = null)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, "ffmpeg not found");
        isCancelled ??= () => false;
        var lutFile = PrepareLut(lut);
```

nahradit:

```csharp
    public List<Thumbnail> Thumbnails(Clip clip, string cardPath, ClipInfo info, LutUse? lut, Func<bool>? isCancelled = null)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, Msg.FfmpegNotFound);
        isCancelled ??= () => false;
        var lutFile = PrepareLut(lut);
```

`windows/src/Offload.Core/ProxyRenderer.cs`, řádky 304–308 — najít:

```csharp
    public void Render(Clip clip, string cardPath, ClipInfo info, LutUse? lut, string output, Action<double> progress, Func<bool> isCancelled)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, "ffmpeg not found");
        var lutFile = PrepareLut(lut);
        bool hwaccel = clip.Format == ClipFormat.Video && UseHardwareDecoding(info.Codec);
```

nahradit:

```csharp
    public void Render(Clip clip, string cardPath, ClipInfo info, LutUse? lut, string output, Action<double> progress, Func<bool> isCancelled)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, Msg.FfmpegNotFound);
        var lutFile = PrepareLut(lut);
        bool hwaccel = clip.Format == ClipFormat.Video && UseHardwareDecoding(info.Codec);
```
`windows/src/Offload.Core/Luts.cs`, řádky 214–218 — najít:

```csharp
        if (fallback != null && !fallback.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (cat.Find(fallback) == null) return (LutMode.Auto, null, null, null, $"Unknown fallback LUT '{fallback}'. Built-in: {Ids(cat)}");
            fb = fallback;
        }
```

nahradit:

```csharp
        if (fallback != null && !fallback.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (cat.Find(fallback) == null) return (LutMode.Auto, null, null, null, Msg.UnknownFallbackLut(fallback, Ids(cat)));
            fb = fallback;
        }
```

`windows/src/Offload.Core/Luts.cs`, řádky 223–227 — najít:

```csharp
        return cat.Find(value) != null
            ? (LutMode.Builtin, value, null, fb, null)
            : (LutMode.Auto, null, null, fb, $"Unknown LUT '{value}'. Use auto, none, a .cube path or one of: {Ids(cat)}");
    }

```

nahradit:

```csharp
        return cat.Find(value) != null
            ? (LutMode.Builtin, value, null, fb, null)
            : (LutMode.Auto, null, null, fb, Msg.UnknownLut(value, Ids(cat)));
    }

```

CLI (§2.6 řádky 23–32): „Chyba: “, české hlášky voleb, `cs-CZ` pro chyby parseru, `--dest` nepovinné pro parser:

`windows/src/Offload.Cli/Program.cs`, řádky 1–3 — najít:

```csharp
using System.CommandLine;
using System.Text;

```

nahradit:

```csharp
using System.CommandLine;
using System.Globalization;
using System.Text;

```

`windows/src/Offload.Cli/Program.cs`, řádky 11–14 — najít:

```csharp
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Like the Mac CLI, `copy` is the default command: `offload E:\ -d D:\ -d F:\` works.
        if (args.Length > 0 && !Commands.Contains(args[0]) && !args[0].StartsWith('-')) args = ["copy", .. args];
```

nahradit:

```csharp
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Parser errors of System.CommandLine (unknown option …) in Czech: the package ships a "cs" satellite.
        var cs = CultureInfo.GetCultureInfo("cs-CZ");
        CultureInfo.DefaultThreadCurrentUICulture = cs;
        CultureInfo.CurrentUICulture = cs;
        // Like the Mac CLI, `copy` is the default command: `offload E:\ -d D:\ -d F:\` works.
        if (args.Length > 0 && !Commands.Contains(args[0]) && !args[0].StartsWith('-')) args = ["copy", .. args];
```
`windows/src/Offload.Cli/Offload.Cli.csproj`, řádky 5–8 — najít:

```xml
    <RootNamespace>Offload.Cli</RootNamespace>
    <ApplicationIcon>..\..\..\shared\icon\Offload.ico</ApplicationIcon>
  </PropertyGroup>
  <ItemGroup>
```

nahradit:

```xml
    <RootNamespace>Offload.Cli</RootNamespace>
    <ApplicationIcon>..\..\..\shared\icon\Offload.ico</ApplicationIcon>
    <!-- Czech parser errors from System.CommandLine (its "cs" satellite); Directory.Build.props keeps only "en". -->
    <SatelliteResourceLanguages>en;cs</SatelliteResourceLanguages>
  </PropertyGroup>
  <ItemGroup>
```
`windows/src/Offload.Cli/CopyCommand.cs`, řádky 11–15 — najít:

```csharp
    {
        var source = new Argument<string>("source") { Description = "Card drive or source folder." };
        var dest = new Option<string[]>("--dest", "-d") { Description = "Destination root (repeat for each disk).", Required = true };
        var name = new Option<string?>("--name") { Description = "Card folder name (default: volume label)." };
        var date = new Option<string?>("--date") { Description = "Date folder (default: today, YYYY-MM-DD)." };
```

nahradit:

```csharp
    {
        var source = new Argument<string>("source") { Description = "Card drive or source folder." };
        // Not Required: the parser's "Option '--dest' is required." has no Czech translation — the engine says
        // "Není zadaný žádný cíl." instead.
        var dest = new Option<string[]>("--dest", "-d") { Description = "Destination root (repeat for each disk).", DefaultValueFactory = _ => [] };
        var name = new Option<string?>("--name") { Description = "Card folder name (default: volume label)." };
        var date = new Option<string?>("--date") { Description = "Date folder (default: today, YYYY-MM-DD)." };
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 49–57 — najít:

```csharp
                };
                if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
                    throw new ArgumentException($"Unknown proxy codec {r.GetValue(proxyCodec)}");
                var (lutMode, lutBuiltin, lutRaw, lutFb, lutErr) = LutOption.Parse(r.GetValue(lut), r.GetValue(lutFallback), LutCatalog.Default);
                if (lutErr != null) throw new ArgumentException(lutErr);
                var lutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
                if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException($"LUT not found: {lutPath}");
                config.AnalyzeClips = !r.GetValue(noAnalyze);
                config.Proxy = new ProxySettings
```

nahradit:

```csharp
                };
                if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
                    throw new ArgumentException(Msg.UnknownProxyCodec(r.GetValue(proxyCodec)));
                var (lutMode, lutBuiltin, lutRaw, lutFb, lutErr) = LutOption.Parse(r.GetValue(lut), r.GetValue(lutFallback), LutCatalog.Default);
                if (lutErr != null) throw new ArgumentException(lutErr);
                var lutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
                if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException(Msg.LutNotFound(lutPath));
                config.AnalyzeClips = !r.GetValue(noAnalyze);
                config.Proxy = new ProxySettings
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 72–82 — najít:

```csharp
                };
                if (config.Proxy.ExtraFolder != null && !config.Proxy.Enabled)
                    Console.Error.WriteLine("Note: --proxy-folder has no effect without --proxy.");
                if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string folder && !Directory.Exists(folder))
                    throw new ArgumentException($"Proxy folder not found: {folder}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
```

nahradit:

```csharp
                };
                if (config.Proxy.ExtraFolder != null && !config.Proxy.Enabled)
                    Console.Error.WriteLine(Msg.ProxyFolderNoEffect);
                if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string folder && !Directory.Exists(folder))
                    throw new ArgumentException(Msg.ProxyFolderNotFound(folder));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 98–102 — najít:

```csharp
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error: {e.Message}");
            return 1;
        }
```

nahradit:

```csharp
        catch (Exception e)
        {
            Console.Error.WriteLine(Msg.CliError + e.Message);
            return 1;
        }
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 131–135 — najít:

```csharp
        {
            e.Cancel = true;
            Console.Error.WriteLine("\nCancelling — finishing current block, partial files will be removed…");
            job.Cancel();
        };
```

nahradit:

```csharp
        {
            e.Cancel = true;
            Console.Error.WriteLine("\n" + Msg.Cancelling);
            job.Cancel();
        };
```
`windows/src/Offload.Cli/VerifyCommand.cs`, řádky 22–26 — najít:

```csharp
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
```

nahradit:

```csharp
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
```

`windows/src/Offload.Cli/VerifyCommand.cs`, řádky 47–51 — najít:

```csharp
        {
            ConsoleLine.Clear();
            Console.Error.WriteLine($"Error: {e.Message}");
            return 1;
        }
```

nahradit:

```csharp
        {
            ConsoleLine.Clear();
            Console.Error.WriteLine(Msg.CliError + e.Message);
            return 1;
        }
```
`windows/src/Offload.Cli/BenchCommand.cs`, řádky 36–40 — najít:

```csharp
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
```

nahradit:

```csharp
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
```
`windows/src/Offload.Cli/PortsCommand.cs`, řádky 1–4 — najít:

```csharp
using System.CommandLine;
using System.Globalization;
using Offload.Core.Hardware;

```

nahradit:

```csharp
using System.CommandLine;
using System.Globalization;
using Offload.Core;
using Offload.Core.Hardware;

```

`windows/src/Offload.Cli/PortsCommand.cs`, řádky 65–69 — najít:

```csharp
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
```

nahradit:

```csharp
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
```
`windows/src/Offload.Cli/CliParse.cs`, řádky 19–23 — najít:

```csharp
        var s = a.Tokens.Count > 0 ? a.Tokens[0].Value : null;
        var v = Double(s);
        if (v == null) a.AddError($"Cannot parse '{s}' as a positive number.");
        return v;
    }
```

nahradit:

```csharp
        var s = a.Tokens.Count > 0 ? a.Tokens[0].Value : null;
        var v = Double(s);
        if (v == null) a.AddError(Offload.Core.Msg.NotPositiveNumber(s));
        return v;
    }
```
`windows/src/Offload.Cli/LutsCommand.cs`, řádky 12–16 — najít:

```csharp
        {
            var cat = LutCatalog.Default;
            if (cat.Luts.Count == 0) Console.Error.WriteLine("No built-in LUTs found (luts folder missing).");
            foreach (var l in cat.Luts) Console.WriteLine($"{l.Id} — {l.Name}");
            return 0;
```

nahradit:

```csharp
        {
            var cat = LutCatalog.Default;
            if (cat.Luts.Count == 0) Console.Error.WriteLine(Msg.NoBuiltinLuts);
            foreach (var l in cat.Luts) Console.WriteLine($"{l.Id} — {l.Name}");
            return 0;
```

- [ ] **Step 4: Testy, celá sada a CLI**

Run: `dotnet test --filter "FullyQualifiedName~PreflightMessagesTests|FullyQualifiedName~PreflightTests|FullyQualifiedName~FormatTests|FullyQualifiedName~JobTests|FullyQualifiedName~VerifierTests|FullyQualifiedName~ScannerTests|FullyQualifiedName~LutOptionTests|FullyQualifiedName~ProxyRendererTests.UnsupportedReason"` → PASS. Pak `dotnet build` (bez varování) a celá sada `dotnet test` → zelená.

Ruční kontrola CLI (jen konzole, ne GUI): `src\Offload.Cli\bin\Debug\net10.0-windows\offload.exe copy C:\Windows\Temp` → `Chyba: Není zadaný žádný cíl.` (kód 1); `offload.exe copy E:\ -d X:\ --bogus` → první řádek česky („Nerozpoznaný příkaz nebo argument …“).

- [ ] **Step 5: VYVOJ + commit**

V `docs/VYVOJ-WINDOWS.md` založit nad řádkem `## 2026-10-06 — balíček ZIP` sekci (s prázdným řádkem za ní):

```markdown
## 2026-10-06 — plán 6: karty a bezpečnost dat

- Úkol 1 — české hlášky kontrol před kopií (`Msg`, `PreflightCode` u každé chyby), CLI „Chyba: “, chyby parseru System.CommandLine česky, kde je balíček přeložené (satelit cs; chybějící `--dest` hlásí jádro: „Není zadaný žádný cíl.“), velikosti v českých větách „3,2 GB“

```

```powershell
git add windows docs
git commit -m "Windows: Czech preflight messages with PreflightCode" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Zachování neznámých klíčů v nastavení

Spec §5.4 (R24), §6.1 úkol 2. Verze 0.2.0+ nesmaže klíče, které zapíše 0.3.0/0.4.0 (projekty, pravidla LUTů …).

**Files:**
- Modify: `windows/src/Offload.Core/Desktop/AppSettings.cs` (řádky 26–29, 33–36, 84–87)
- Modify: `windows/src/Offload.Core/Media.cs` (řádky 1–3, 191–194)
- Modify: `windows/tests/Offload.Core.Tests/SettingsStoreTests.cs` (řádky 1–2, 51–54)

**Interfaces:**
- Produces: `AppSettings.Extra` a `ProxySettings.Extra` (`Dictionary<string, JsonElement>?`, `[JsonExtensionData]`); `SettingsStore.KnownKeys` (soukromé; úkol 9 přidá `naming`).

- [ ] **Step 1: Padající testy**

`windows/tests/Offload.Core.Tests/SettingsStoreTests.cs`, řádky 1–2 — najít:

```csharp
using Offload.Core.Desktop;

```

nahradit:

```csharp
using System.Text.Json;
using Offload.Core.Desktop;

```

`windows/tests/Offload.Core.Tests/SettingsStoreTests.cs`, řádky 51–54 — najít:

```csharp
        Assert.True(s.Proxy.BurnTimecode);
        Assert.Equal(@"C:\x\ffmpeg.exe", s.Tools.Ffmpeg);
    }

```

nahradit:

```csharp
        Assert.True(s.Proxy.BurnTimecode);
        Assert.Equal(@"C:\x\ffmpeg.exe", s.Tools.Ffmpeg);
    }

    // R24: keys of a newer version survive load + save by this one — top level and inside "proxy".
    [Fact]
    public void UnknownKeys_SurviveLoadAndSave()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """
            {"verify":false,"projects":[{"name":"Film X","destRoots":["D:\\X"]}],"activeProject":"Film X","hash":"both",
             "proxy":{"enabled":true,"lutRules":[{"match":"camera","value":"FX6","lut":"sony-slog3-sgamut3cine"}],"brawGen4ToGen5":true}}
            """);
        var s = SettingsStore.Load(path);
        s.Verify = true;
        SettingsStore.Save(s, path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.True(root.GetProperty("verify").GetBoolean());
        Assert.Equal("Film X", root.GetProperty("projects")[0].GetProperty("name").GetString());
        Assert.Equal(@"D:\X", root.GetProperty("projects")[0].GetProperty("destRoots")[0].GetString());
        Assert.Equal("Film X", root.GetProperty("activeProject").GetString());
        Assert.Equal("both", root.GetProperty("hash").GetString());
        var proxy = root.GetProperty("proxy");
        Assert.True(proxy.GetProperty("enabled").GetBoolean());
        Assert.Equal("FX6", proxy.GetProperty("lutRules")[0].GetProperty("value").GetString());
        Assert.True(proxy.GetProperty("brawGen4ToGen5").GetBoolean());
        // every key once: a known key never also lands among the unknown ones
        var names = root.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.DoesNotContain("Extra", names);
    }

    [Fact]
    public void UnknownKeys_KeptByClone_NoneForFreshSettings()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """{"proxy":{"lutRules":[]},"updates":{"manifest":null}}""");
        Assert.True(SettingsStore.Clone(s.Proxy).Extra!.ContainsKey("lutRules"));
        Assert.True(SettingsStore.Clone(s).Extra!.ContainsKey("updates"));
        var fresh = Path.Combine(dir.Path, "fresh.json");
        SettingsStore.Save(new AppSettings(), fresh);
        Assert.DoesNotContain("Extra", File.ReadAllText(fresh));
        Assert.DoesNotContain("lutRules", File.ReadAllText(fresh));
    }

```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~SettingsStoreTests"` → build FAIL (`Extra` neexistuje).

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 26–29 — najít:

```csharp
    }
    private Dictionary<string, string> _portNames = new(StringComparer.OrdinalIgnoreCase);
}

```

nahradit:

```csharp
    }
    private Dictionary<string, string> _portNames = new(StringComparer.OrdinalIgnoreCase);

    /// Top-level keys this version does not know (a newer version wrote them): kept as they are and written back,
    /// so going back to an older version never loses projects or rules (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 33–36 — najít:

```csharp

    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    static readonly JsonSerializerOptions Json = new()
```

nahradit:

```csharp

    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The top-level keys Load reads into properties; every other key goes to AppSettings.Extra.
    static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "destRoots", "verify", "analyzeClips", "proxy", "tools", "portNames" };

    static readonly JsonSerializerOptions Json = new()
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 84–87 — najít:

```csharp
                s.PortNames = names;
            });
            return Normalize(s);
        }
```

nahradit:

```csharp
                s.PortNames = names;
            });
            foreach (var (key, value) in root)
                if (!KnownKeys.Contains(key)) (s.Extra ??= [])[key] = value.Clone();   // the document is disposed below
            return Normalize(s);
        }
```
`windows/src/Offload.Core/Media.cs`, řádky 1–3 — najít:

```csharp
using System.Globalization;
using System.Text.Json.Serialization;

```

nahradit:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

```

`windows/src/Offload.Core/Media.cs`, řádky 191–194 — najít:

```csharp
    /// How much of the image the frame covers (0.9 = 90 %, i.e. 10 % overscan around it).
    [JsonPropertyName("frameScale")] public double FrameScale { get; set; } = 1.0;

    /// Centered crop rectangle (x, y, w, h) inside a W×H image, even-sized, or null when no crop is set.
```

nahradit:

```csharp
    /// How much of the image the frame covers (0.9 = 90 %, i.e. 10 % overscan around it).
    [JsonPropertyName("frameScale")] public double FrameScale { get; set; } = 1.0;
    /// Keys a newer version added inside "proxy" (settings.json, report): kept and written back unchanged (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// Centered crop rectangle (x, y, w, h) inside a W×H image, even-sized, or null when no crop is set.
```

(`ProxySettings` se serializuje i do `offload_report.json` — neznámé klíče `proxy` tam projdou také; jsou jen přidané, Mac je ignoruje, §5.1.)

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~ReportTests"` → PASS; `dotnet test` → zelená.

- [ ] **Step 5: VYVOJ + commit**

Řádek nahoru do sekce plánu 6:

```markdown
- Úkol 2 — nastavení zachová klíče, které tahle verze nezná (horní úroveň i `proxy`), takže návrat na starší verzi od 0.2.0 nesmaže projekty ani pravidla
```

```powershell
git add windows docs
git commit -m "Windows: keep unknown settings keys" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Otisk karty, seznam souborů, tolerance času FAT

Spec §2.1 „Jádro“ (R2, R3), §6.1 úkol 3.

**Files:**
- Create: `windows/src/Offload.Core/History/CardFingerprint.cs`
- Create: `windows/src/Offload.Core/History/FileListStore.cs`
- Create: `windows/tests/Offload.Core.Tests/CardFingerprintTests.cs`

**Interfaces:**
- Produces (`namespace Offload.Core.History`): `CardFingerprint.Compute(IEnumerable<SourceFile>) → (string Fingerprint, string Shape)` a přetížení pro `SourceScan`; `FileTimes.StoresLocalTime(string? filesystem) → bool` (FAT, FAT32), `FileTimes.Same(DateTime a, DateTime b, bool localTimeFilesystem) → bool` (přesně, nebo celé hodiny ≤ 14 h), `FileTimes.Close(DateTime, DateTime, bool) → bool` (±2 s, na FAT i celé hodiny); `FileListStore.Write(string path, IEnumerable<SourceFile>)`, `FileListStore.Read(string path) → List<SourceFile>?` (null = chybí/poškozený).

- [ ] **Step 1: Padající testy**

```csharp
using System.Security.Cryptography;
using System.Text;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class CardFingerprintTests
{
    static readonly DateTime T0 = new(2026, 10, 6, 8, 15, 0, DateTimeKind.Utc);

    static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void Format_IsSortedLinesOfPathSizeTicks()
    {
        SourceFile[] files = [new("CLIP/b.mov", 20, T0.AddSeconds(2)), new("CLIP/a.mov", 10, T0)];
        var (fingerprint, shape) = CardFingerprint.Compute(files);
        Assert.Equal(Sha($"CLIP/a.mov\t10\t{T0.Ticks}\nCLIP/b.mov\t20\t{T0.AddSeconds(2).Ticks}\n"), fingerprint);
        Assert.Equal(Sha("CLIP/a.mov\t10\nCLIP/b.mov\t20\n"), shape);
    }

    [Fact]
    public void Order_DoesNotMatter()
    {
        SourceFile[] a = [new("A/1.mov", 1, T0), new("B/2.mov", 2, T0), new("a/3.mov", 3, T0)];
        Assert.Equal(CardFingerprint.Compute(a), CardFingerprint.Compute(a.Reverse()));
    }

    [Fact]
    public void Mtime_ChangesFingerprintOnly_SizeChangesBoth()
    {
        var before = CardFingerprint.Compute([new SourceFile("a.mov", 10, T0)]);
        var touched = CardFingerprint.Compute([new SourceFile("a.mov", 10, T0.AddHours(1))]);
        var grown = CardFingerprint.Compute([new SourceFile("a.mov", 11, T0)]);
        Assert.NotEqual(before.Fingerprint, touched.Fingerprint);
        Assert.Equal(before.Shape, touched.Shape);
        Assert.NotEqual(before.Fingerprint, grown.Fingerprint);
        Assert.NotEqual(before.Shape, grown.Shape);
    }

    [Fact]
    public void EmptyCard_IsTheHashOfNothing()
    {
        const string empty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        Assert.Equal((empty, empty), CardFingerprint.Compute(Array.Empty<SourceFile>()));
    }

    // Review focus 2: FAT32 card after a daylight-saving change.
    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    [InlineData(-1, true, true)]
    [InlineData(14, true, true)]
    [InlineData(15, true, false)]
    [InlineData(1, false, false)]   // exFAT/NTFS store UTC: an hour off is a changed file
    public void FileTimes_WholeHoursOnlyOnLocalTimeFilesystems(int hours, bool fat, bool same) =>
        Assert.Equal(same, FileTimes.Same(T0, T0.AddHours(hours), fat));

    [Fact]
    public void FileTimes_HourAndASecond_IsChanged()
    {
        Assert.False(FileTimes.Same(T0, T0.AddHours(1).AddSeconds(2), localTimeFilesystem: true));
        Assert.True(FileTimes.StoresLocalTime("FAT32"));
        Assert.True(FileTimes.StoresLocalTime("FAT"));
        Assert.False(FileTimes.StoresLocalTime("exFAT"));
        Assert.False(FileTimes.StoresLocalTime("NTFS"));
        Assert.False(FileTimes.StoresLocalTime(null));
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1_900, false, true)]        // a report keeps whole seconds, FAT 2-second steps
    [InlineData(2_500, false, false)]
    [InlineData(3_600_000, true, true)]     // FAT32 after the change to winter time
    [InlineData(3_601_500, true, true)]
    [InlineData(3_605_000, true, false)]
    [InlineData(3_600_000, false, false)]   // exFAT: an hour off is another file
    public void FileTimes_Close(int millis, bool fat, bool close) =>
        Assert.Equal(close, FileTimes.Close(T0, T0.AddMilliseconds(millis), fat));

    [Fact]
    public void FileList_RoundTrip_MissingAndDamaged()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "history", "x.files.gz");
        SourceFile[] files = [new("CLIP/Příliš žluťoučký.mov", 123_456_789_012, T0), new("a b/c.mxf", 0, T0.AddTicks(1))];
        FileListStore.Write(path, files);
        Assert.Equal(files, FileListStore.Read(path));
        Assert.Null(FileListStore.Read(Path.Combine(dir.Path, "missing.files.gz")));
        File.WriteAllText(path, "not gzip");
        Assert.Null(FileListStore.Read(path));
    }

    [Fact]
    public void FileList_TenThousandFiles_IsSmall()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "big.files.gz");
        var files = Enumerable.Range(0, 10_000).Select(i => new SourceFile($"DCIM/100CANON/IMG_{i:0000}.CR3", 30_000_000 + i, T0.AddSeconds(i))).ToList();
        FileListStore.Write(path, files);
        Assert.True(new FileInfo(path).Length < 400_000);
        Assert.Equal(10_000, FileListStore.Read(path)!.Count);
    }
}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~CardFingerprintTests"` → build FAIL (`Offload.Core.History` neexistuje).

- [ ] **Step 3: Implementace**

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core.History;

/// What a card looked like (spec 2.1, R2): two SHA-256 hashes (lower-case hex) over its file list sorted ordinally —
/// `Fingerprint` over "relpath\tsize\tmtimeTicksUtc\n" lines (the card is exactly as it was), `Shape` over
/// "relpath\tsize\n" (the same files whatever their times — how a manually added folder is found again).
public static class CardFingerprint
{
    public static (string Fingerprint, string Shape) Compute(IEnumerable<SourceFile> files)
    {
        using var full = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var shape = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var f in files.OrderBy(f => f.RelPath, StringComparer.Ordinal))
        {
            var size = f.Size.ToString(CultureInfo.InvariantCulture);
            shape.AppendData(Encoding.UTF8.GetBytes($"{f.RelPath}\t{size}\n"));
            full.AppendData(Encoding.UTF8.GetBytes($"{f.RelPath}\t{size}\t{f.ModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture)}\n"));
        }
        return (Convert.ToHexStringLower(full.GetHashAndReset()), Convert.ToHexStringLower(shape.GetHashAndReset()));
    }

    public static (string Fingerprint, string Shape) Compute(SourceScan scan) => Compute(scan.Files);
}

/// File times compared over a card's life (R3). FAT and FAT32 store local time: after a daylight-saving change Windows
/// reports every file shifted by whole hours, so there two times are the same when they differ by whole hours — at
/// most 14 — with equal minutes, seconds and fractions. exFAT and NTFS store UTC: there only an exact match counts.
public static class FileTimes
{
    public static bool StoresLocalTime(string? filesystem) =>
        filesystem != null && filesystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase);

    public static bool Same(DateTime a, DateTime b, bool localTimeFilesystem)
    {
        if (a.Ticks == b.Ticks) return true;
        if (!localTimeFilesystem) return false;
        long diff = Math.Abs(a.Ticks - b.Ticks);
        return diff % TimeSpan.TicksPerHour == 0 && diff <= 14 * TimeSpan.TicksPerHour;
    }

    /// Times as a report keeps them (whole seconds) or a FAT disk (2-second steps): the same within 2 s — on a
    /// local-time file system also after a shift by whole hours (at most 14).
    public static bool Close(DateTime a, DateTime b, bool localTimeFilesystem)
    {
        const long slack = 2 * TimeSpan.TicksPerSecond;
        long diff = Math.Abs(a.Ticks - b.Ticks);
        if (diff <= slack) return true;
        if (!localTimeFilesystem || diff > 14 * TimeSpan.TicksPerHour + slack) return false;
        long off = diff % TimeSpan.TicksPerHour;
        return off <= slack || TimeSpan.TicksPerHour - off <= slack;
    }
}
```

```csharp
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Offload.Core.History;

/// The file list of one history entry (`history\<id>.files.gz`): gzip of UTF-8 lines "relpath\tsize\tmtimeTicksUtc\n".
/// Read only when a card's fingerprint differs from the entry's (spec 2.1, 5.5: ~200 KB for 10 000 files).
public static class FileListStore
{
    /// Written via a temporary name and a rename, like every other file Offload keeps.
    public static void Write(string path, IEnumerable<SourceFile> files)
    {
        using var buffer = new MemoryStream();
        using (var gz = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new StreamWriter(gz, new UTF8Encoding(false)))
        {
            foreach (var f in files)
            {
                w.Write(f.RelPath);
                w.Write('\t');
                w.Write(f.Size.ToString(CultureInfo.InvariantCulture));
                w.Write('\t');
                w.Write(f.ModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture));
                w.Write('\n');
            }
        }
        NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
        NativeIO.WriteFileDurably(path, buffer.ToArray());
    }

    /// Null when the list is missing or damaged — that only costs the "new files" count, never the history line.
    public static List<SourceFile>? Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var gz = new GZipStream(file, CompressionMode.Decompress);
            using var r = new StreamReader(gz, Encoding.UTF8);
            var list = new List<SourceFile>();
            while (r.ReadLine() is string line)
            {
                if (line.Length == 0) continue;
                var parts = line.Split('\t');
                if (parts.Length != 3
                    || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                    || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                    || ticks > DateTime.MaxValue.Ticks) return null;
                list.Add(new SourceFile(parts[0], size, new DateTime(ticks, DateTimeKind.Utc)));
            }
            return list;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~CardFingerprintTests"` → PASS (20); `dotnet test` → zelená.

- [ ] **Step 5: VYVOJ + commit**

Řádek nahoru do sekce plánu 6:

```markdown
- Úkol 3 — otisk karty (SHA-256 `fingerprint` s časy a `shape` bez nich), seznam souborů jako gz, porovnání časů s tolerancí celých hodin na FAT/FAT32 (posun letního času)
```

```powershell
git add windows docs
git commit -m "Windows: card fingerprint, file list and FAT time tolerance" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `HistoryStore`

Spec §2.1 „Data“, „Okrajové případy“, §5.2, §6.1 úkol 4.

**Files:**
- Create: `windows/src/Offload.Core/History/HistoryEntry.cs`
- Create: `windows/src/Offload.Core/History/HistoryStore.cs`
- Modify: `windows/src/Offload.Core/OffloadReport.cs` (řádky 199–203)
- Create: `windows/tests/Offload.Core.Tests/HistoryStoreTests.cs`

**Interfaces:**
- Consumes: `FileListStore` (úkol 3).
- Produces: `HistoryEntry` (+ `HistoryCard`, `HistoryDestination`, `HistoryGeneration` s konstantami `Full`/`Append`), `record CardKey(string FsSerial, long Capacity)`; `HistoryStore(string folder, Action<string>? log = null)` s `Folder`, `FilePath`, `Load() → IReadOnlyList<HistoryEntry>` (nejnovější `finished` první), `Record(HistoryEntry entry, IReadOnlyList<SourceFile>? files) → bool` (přidá nebo nahradí podle `Id`), `ReadFileList(HistoryEntry) → IReadOnlyList<SourceFile>?`, `static FindForCard(IEnumerable<HistoryEntry>, CardKey?, string shape) → HistoryEntry?`, `static NewId(DateTime finishedUtc) → string`, `static Default(Action<string>? log = null)`, interní `MaxEntries` (5000) a `LockTimeout` (5 s); `OffloadReport.IsoSecondsDateConverter` je nově `internal`.

- [ ] **Step 1: Padající testy**

```csharp
using System.Text.Json;
using Offload.Core.History;

namespace Offload.Core.Tests;

public static class HistoryFixtures
{
    public static readonly DateTime T0 = new(2026, 10, 6, 8, 15, 0, DateTimeKind.Utc);

    /// A finished full copy of a card volume (serial 1A2B3C4D, 256 GB) onto D:\Zaloha and F:\Zaloha.
    public static HistoryEntry Entry(DateTime finished, string cardName = "A001", string? serial = "1A2B3C4D", long capacity = 256_060_514_304,
        string fingerprint = "f", string shape = "s", bool success = true, bool verified = true)
    {
        var date = finished.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return new HistoryEntry
        {
            Id = HistoryStore.NewId(finished),
            Card = serial == null ? null : new HistoryCard { Identity = @"\\?\Volume{1}\|" + serial, FsSerial = serial, Capacity = capacity, Label = cardName, Filesystem = "exFAT" },
            Source = @"E:\",
            CardName = cardName,
            Date = date,
            Finished = finished,
            Success = success,
            Verified = verified,
            FailedFiles = success ? null : 3,
            Files = 2,
            Bytes = 3000,
            Fingerprint = fingerprint,
            Shape = shape,
            Destinations =
            [
                new HistoryDestination { Root = @"D:\Zaloha", CardPath = $@"D:\Zaloha\{date}\{cardName}" },
                new HistoryDestination { Root = @"F:\Zaloha", CardPath = $@"F:\Zaloha\{date}\{cardName}" },
            ],
            Generations = [new HistoryGeneration { Seq = 1, Kind = HistoryGeneration.Full, Finished = finished, Files = 2, Bytes = 3000, Verified = verified }],
            Reels = [cardName],
        };
    }
}

public class HistoryStoreTests
{
    static readonly DateTime T0 = HistoryFixtures.T0;

    [Fact]
    public void RoundTrip_FormatOfSpec52()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var entry = HistoryFixtures.Entry(T0);
        SourceFile[] files = [new("CLIP/a.mov", 1000, T0), new("CLIP/b.mov", 2000, T0)];

        Assert.True(store.Record(entry, files));

        var back = Assert.Single(store.Load());
        Assert.Equal(entry.Id, back.Id);
        Assert.Equal("1A2B3C4D", back.Card!.FsSerial);
        Assert.Equal(256_060_514_304, back.Card.Capacity);
        Assert.Equal(T0, back.Finished);
        Assert.Equal(DateTimeKind.Utc, back.Finished.Kind);
        Assert.Equal(["xxh64"], back.HashAlgorithms);
        Assert.Equal(@"F:\Zaloha", back.Destinations[1].Root);
        Assert.Equal($"history/{entry.Id}.files.gz", back.FileList);
        Assert.Equal(files, store.ReadFileList(back));

        var json = File.ReadAllText(store.FilePath);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"finished\": \"2026-10-06T08:15:00Z\"", json);
        Assert.Contains("\"project\": null", json);
        Assert.DoesNotContain("failedFiles", json);   // only when success is false
        Assert.DoesNotContain("\r", json);
        Assert.True(File.Exists(Path.Combine(dir.Path, "history", entry.Id + ".files.gz")));
    }

    [Fact]
    public void Missing_IsEmpty()
    {
        using var dir = new TempDir();
        Assert.Empty(new HistoryStore(Path.Combine(dir.Path, "nope")).Load());
    }

    // Review focus 4: a damaged history.json is set aside, never blocks.
    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("{\"entries\":[]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"id\":\"x\",\"finished\":\"yesterday\"}]}")]
    public void Damaged_SetAside_StartsEmpty_NextRecordWorks(string content)
    {
        using var dir = new TempDir();
        var log = new List<string>();
        var store = new HistoryStore(dir.Path, log.Add);
        File.WriteAllText(store.FilePath, content);

        Assert.Empty(store.Load());

        Assert.False(File.Exists(store.FilePath));
        var aside = Assert.Single(Directory.GetFiles(dir.Path, "history.json.corrupt-*"));
        Assert.Equal(content, File.ReadAllText(aside));
        Assert.Contains(log, l => l.Contains("damaged"));
        Assert.True(store.Record(HistoryFixtures.Entry(T0), null));
        Assert.Single(store.Load());
    }

    // Review focus 4: GUI and CLI writing at once — the mutex keeps every entry.
    [Fact]
    public void TwoWriters_InTwoThreads_KeepEveryEntry()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        void Write(int offset)
        {
            for (int i = 0; i < 25; i++) Assert.True(store.Record(HistoryFixtures.Entry(T0.AddMinutes(offset + i)), null));
        }
        var a = new Thread(() => Write(0));
        var b = new Thread(() => Write(100));
        a.Start();
        b.Start();
        a.Join();
        b.Join();
        Assert.Equal(50, store.Load().Count);
    }

    [Fact]
    public void Busy_SkipsTheEntry_AfterTheTimeout()
    {
        using var dir = new TempDir();
        var log = new List<string>();
        var store = new HistoryStore(dir.Path, log.Add) { LockTimeout = TimeSpan.FromMilliseconds(200) };
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var m = new Mutex(false, @"Local\Offload-history");
            m.WaitOne();
            held.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            m.ReleaseMutex();
        });
        holder.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(30)));
        try
        {
            Assert.False(store.Record(HistoryFixtures.Entry(T0), null));
        }
        finally
        {
            release.Set();
            holder.Join();
        }
        Assert.Contains(log, l => l.Contains("busy"));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Prune_KeepsTheNewest_AndDeletesOldFileLists()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path) { MaxEntries = 3 };
        var entries = Enumerable.Range(0, 5).Select(i => HistoryFixtures.Entry(T0.AddDays(i))).ToList();
        foreach (var e in entries.AsEnumerable().Reverse()) store.Record(e, [new SourceFile("a.mov", 1, T0)]);

        Assert.Equal(entries.Skip(2).Reverse().Select(e => e.Id), store.Load().Select(e => e.Id));
        Assert.False(File.Exists(Path.Combine(dir.Path, "history", entries[0].Id + ".files.gz")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "history", entries[1].Id + ".files.gz")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "history", entries[4].Id + ".files.gz")));
    }

    [Fact]
    public void SameId_ReplacesTheEntry()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var entry = HistoryFixtures.Entry(T0);
        store.Record(entry, null);
        entry.Files = 14;
        entry.Generations.Add(new HistoryGeneration { Seq = 2, Kind = HistoryGeneration.Append, Finished = T0.AddHours(1), Files = 12 });
        store.Record(entry, null);
        var back = Assert.Single(store.Load());
        Assert.Equal(14, back.Files);
        Assert.Equal([1, 2], back.Generations.Select(g => g.Seq));
    }

    [Fact]
    public void NewerVersion_IsReadOnly()
    {
        using var dir = new TempDir();
        var log = new List<string>();
        var store = new HistoryStore(dir.Path, log.Add);
        var newer = JsonSerializer.Serialize(new { version = 2, entries = new[] { new { id = "a", cardName = "A001", finished = "2026-10-06T08:15:00Z" } } });
        File.WriteAllText(store.FilePath, newer);

        Assert.Equal("A001", Assert.Single(store.Load()).CardName);
        Assert.False(store.Record(HistoryFixtures.Entry(T0), null));
        Assert.Equal(newer, File.ReadAllText(store.FilePath));
        Assert.Contains(log, l => l.Contains("newer"));
    }

    [Fact]
    public void InvalidId_IsNeverWritten()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var entry = HistoryFixtures.Entry(T0);
        entry.Id = @"..\..\evil";
        Assert.False(store.Record(entry, [new SourceFile("a", 1, T0)]));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "history")));
    }

    // R1: serial + capacity, not the volume GUID; a clone with the same serial but another capacity is another card.
    [Fact]
    public void FindForCard_BySerialAndCapacity_OrByShape_NewestWins()
    {
        var old = HistoryFixtures.Entry(T0);
        var newer = HistoryFixtures.Entry(T0.AddHours(2));
        var clone = HistoryFixtures.Entry(T0.AddHours(3), capacity: 128_000_000_000);
        var folder = HistoryFixtures.Entry(T0.AddHours(4), serial: null, shape: "folder-shape");
        HistoryEntry[] all = [old, newer, clone, folder];

        Assert.Same(newer, HistoryStore.FindForCard(all, new CardKey("1a2b3c4d", 256_060_514_304), "x"));
        Assert.Same(clone, HistoryStore.FindForCard(all, new CardKey("1A2B3C4D", 128_000_000_000), "x"));
        Assert.Null(HistoryStore.FindForCard(all, new CardKey("99999999", 256_060_514_304), "folder-shape"));
        Assert.Same(folder, HistoryStore.FindForCard(all, null, "folder-shape"));
        Assert.Null(HistoryStore.FindForCard(all, null, "other"));
    }

    [Fact]
    public void FiveThousandEntries_LoadQuickly()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var many = Enumerable.Range(0, 5000).Select(i => HistoryFixtures.Entry(T0.AddMinutes(i))).ToList();
        // written in one go through the private format: Record 5 000 times would take minutes
        File.WriteAllText(store.FilePath, JsonSerializer.Serialize(new { version = 1, entries = many }));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(5000, store.Load().Count);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms");   // spec 5.5: 300 ms; generous under a loaded suite
    }
}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~HistoryStoreTests"` → build FAIL.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/OffloadReport.cs`, řádky 199–203 — najít:

```csharp
    };

    sealed class IsoSecondsDateConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
```

nahradit:

```csharp
    };

    internal sealed class IsoSecondsDateConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
```

```csharp
using System.Text.Json.Serialization;

namespace Offload.Core.History;

/// The card volume of an entry (spec 5.2); null for a manually added folder.
public sealed class HistoryCard
{
    /// `CardIdentity` at the time ("\\?\Volume{…}\|1A2B3C4D") — stored, never required to match (R1).
    [JsonPropertyName("identity")] public string? Identity { get; set; }
    [JsonPropertyName("fsSerial")] public string FsSerial { get; set; } = "";
    [JsonPropertyName("capacity")] public long Capacity { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("filesystem")] public string? Filesystem { get; set; }
}

public sealed class HistoryDestination
{
    [JsonPropertyName("root")] public string Root { get; set; } = "";
    [JsonPropertyName("cardPath")] public string CardPath { get; set; } = "";
}

/// One copy into the card folder: the full copy (seq 1) or an append (seq = its ASC MHL generation).
public sealed class HistoryGeneration
{
    public const string Full = "full";
    public const string Append = "append";

    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = Full;
    [JsonPropertyName("finished")] public DateTime Finished { get; set; }
    [JsonPropertyName("files")] public int Files { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
}

/// One card offload in %APPDATA%\Offload\history.json (spec 5.2). An append updates its entry instead of adding one.
public sealed class HistoryEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("card")] public HistoryCard? Card { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("cardName")] public string CardName { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    /// Filled from plan 8 (projects); null until then.
    [JsonPropertyName("project")] public string? Project { get; set; }
    [JsonPropertyName("finished")] public DateTime Finished { get; set; }
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    /// Only when `success` is false.
    [JsonPropertyName("failedFiles"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? FailedFiles { get; set; }
    [JsonPropertyName("hashAlgorithms")] public List<string> HashAlgorithms { get; set; } = ["xxh64"];
    [JsonPropertyName("files")] public int Files { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("shape")] public string Shape { get; set; } = "";
    /// "history/<id>.files.gz" — informational; the store derives the path from `id`.
    [JsonPropertyName("fileList")] public string? FileList { get; set; }
    [JsonPropertyName("destinations")] public List<HistoryDestination> Destinations { get; set; } = [];
    [JsonPropertyName("generations")] public List<HistoryGeneration> Generations { get; set; } = [];
    [JsonPropertyName("reels")] public List<string> Reels { get; set; } = [];
}

/// How a card volume is recognised again (R1): the file system's serial number plus the capacity. The volume GUID is
/// not part of it — Windows gives the same card another GUID in another reader or slot.
public sealed record CardKey(string FsSerial, long Capacity);
```

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Offload.Core.Desktop;

namespace Offload.Core.History;

/// %APPDATA%\Offload\history.json + history\<id>.files.gz (spec 2.1, 5.2). Local to this PC, never sent anywhere.
/// The GUI and the CLI may run at once: every read-modify-write holds the named mutex `Local\Offload-history`
/// (at most 5 s, then the entry is skipped with a log line), and the file is replaced via a temporary name.
/// Never throws — a broken history must never stop an offload.
public sealed partial class HistoryStore(string folder, Action<string>? log = null)
{
    public const int CurrentVersion = 1;
    const string MutexName = @"Local\Offload-history";

    public static HistoryStore Default(Action<string>? log = null) => new(AppData.Folder, log);

    public string Folder { get; } = folder;
    public string FilePath => Path.Combine(Folder, "history.json");
    /// Spec 5.2: at most 5 000 entries; the oldest by `finished` go, with their file lists.
    internal int MaxEntries { get; init; } = 5000;
    internal TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(5);

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new OffloadReport.IsoSecondsDateConverter() },
    };

    sealed class HistoryFile
    {
        [JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;
        [JsonPropertyName("entries")] public List<HistoryEntry> Entries { get; set; } = [];
    }

    [GeneratedRegex(@"^[0-9A-Za-z-]{1,64}\z")]
    private static partial Regex SafeId();

    /// "20261006T081500Z-3f2a9c1b": the finish time in UTC and 8 random hex digits.
    public static string NewId(DateTime finishedUtc) =>
        finishedUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-"
        + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    /// Every entry, newest `finished` first. Missing → empty; damaged → set aside as history.json.corrupt-<stamp>
    /// (only while holding the lock), logged, empty.
    public IReadOnlyList<HistoryEntry> Load()
    {
        try
        {
            using var held = Lock();
            return ReadFile(repair: held != null).Entries.OrderByDescending(e => e.Finished).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"history: {e.Message}");
            return [];
        }
    }

    /// Adds `entry`, or replaces the entry with the same id (an append), writes its file list and prunes. False when
    /// nothing was written: the lock stayed busy, the file is from a newer version, or the disk failed — logged.
    public bool Record(HistoryEntry entry, IReadOnlyList<SourceFile>? files)
    {
        if (!SafeId().IsMatch(entry.Id))
        {
            log?.Invoke($"history: invalid id \"{entry.Id}\" — not written");
            return false;
        }
        try
        {
            using var held = Lock();
            if (held == null)
            {
                log?.Invoke($"history: busy for {LockTimeout.TotalSeconds:0} s — the entry for {entry.CardName} is skipped");
                return false;
            }
            var (entries, version) = ReadFile(repair: true);
            if (version > CurrentVersion)
            {
                log?.Invoke($"history: version {version} is newer than this app — read only, the entry for {entry.CardName} is skipped");
                return false;
            }
            if (files != null)
            {
                FileListStore.Write(FileListPath(entry.Id)!, files);
                entry.FileList = $"history/{entry.Id}.files.gz";
            }
            entries.RemoveAll(e => e.Id == entry.Id);
            entries.Add(entry);
            var keep = entries.OrderByDescending(e => e.Finished).Take(MaxEntries).ToList();
            foreach (var gone in entries.Except(keep))
                if (FileListPath(gone.Id) is string list) NativeIO.Delete(list);
            NativeIO.Mkdirs(Folder);
            NativeIO.WriteFileDurably(FilePath, JsonSerializer.SerializeToUtf8Bytes(new HistoryFile { Entries = keep }, Json));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or IOError)
        {
            log?.Invoke($"history: {e.Message}");
            return false;
        }
    }

    /// The entry's file list, or null when it is missing or damaged.
    public IReadOnlyList<SourceFile>? ReadFileList(HistoryEntry entry) =>
        FileListPath(entry.Id) is string path ? FileListStore.Read(path) : null;

    /// The newest entry this card may be (spec 2.1, steps 1–2): for a volume root the same file-system serial and
    /// capacity (R1), for a folder the same shape. Null when there is none.
    public static HistoryEntry? FindForCard(IEnumerable<HistoryEntry> entries, CardKey? key, string shape) =>
        entries.Where(e => key != null
                ? e.Card is { } c && string.Equals(c.FsSerial, key.FsSerial, StringComparison.OrdinalIgnoreCase) && c.Capacity == key.Capacity
                : e.Shape == shape)
            .OrderByDescending(e => e.Finished)
            .FirstOrDefault();

    /// Derived from the id (never from the stored "fileList"), so a hand-edited file cannot point anywhere else.
    string? FileListPath(string id) => SafeId().IsMatch(id) ? Path.Combine(Folder, "history", id + ".files.gz") : null;

    (List<HistoryEntry> Entries, int Version) ReadFile(bool repair)
    {
        if (!File.Exists(FilePath)) return ([], CurrentVersion);
        var bytes = File.ReadAllBytes(FilePath);
        int version;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            version = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("version", out var v)
                      && v.TryGetInt32(out var n) ? n : throw new JsonException("no version");
        }
        catch (JsonException e)
        {
            if (repair) SetAside(e.Message);
            return ([], CurrentVersion);
        }
        try
        {
            var file = JsonSerializer.Deserialize<HistoryFile>(bytes, Json);
            return ((file?.Entries ?? []).Where(e => e != null && e.Id.Length > 0).ToList(), version);
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            // A newer version may have changed the entries: never set its file aside, just do not use it.
            if (version > CurrentVersion) return ([], version);
            if (repair) SetAside(e.Message);
            return ([], CurrentVersion);
        }
    }

    void SetAside(string why)
    {
        var target = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        try
        {
            File.Move(FilePath, target, overwrite: true);
            log?.Invoke($"history.json is damaged ({why}) — set aside as {Path.GetFileName(target)}, starting empty");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"history.json is damaged ({why}) and could not be set aside: {e.Message}");
        }
    }

    sealed class Held(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    /// The cross-process lock, or null when another writer held it for LockTimeout. Released on the same thread.
    Held? Lock()
    {
        var mutex = new Mutex(false, MutexName);
        try
        {
            if (!mutex.WaitOne(LockTimeout))
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // A process died holding it: we own it now, and the file was replaced atomically, so it is consistent.
        }
        return new Held(mutex);
    }
}
```

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~HistoryStoreTests"` → PASS (14); `dotnet test` → zelená.

- [ ] **Step 5: VYVOJ + commit**

Řádek nahoru do sekce plánu 6:

```markdown
- Úkol 4 — `history.json` v %APPDATA%\Offload (formát návrhu §5.2) pod pojmenovaným mutexem `Local\Offload-history` (GUI i CLI), poškozený soubor odložen jako `history.json.corrupt-…`, novější verze jen ke čtení, nejvýš 5 000 záznamů
```

```powershell
git add windows docs
git commit -m "Windows: offload history store" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Zápis historie z úlohy

Spec §2.1 „Zápis historie“, R27, §6.1 úkol 5. Identitu karty zjišťuje nově `OffloadJob` sám na vlákně kopie (dnes `JobQueue.Runner.RunCopy`) — `Runner` ji jen čte.

**Files:**
- Modify: `windows/src/Offload.Cli/CopyCommand.cs` (řádky 32–39, 49–52)
- Modify: `windows/src/Offload.Core/Desktop/JobQueue.cs` (řádky 102–128)
- Create: `windows/src/Offload.Core/History/CardVolume.cs`
- Modify: `windows/src/Offload.Core/History/HistoryStore.cs` (řádky 18–22)
- Modify: `windows/src/Offload.Core/NativeIO.cs` (řádky 127–130)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 1–3, 17–20, 67–74, 136–139, 157–162, 177–180)
- Create: `windows/tests/Offload.Core.Tests/HistoryJobTests.cs`
- Modify: `windows/tests/Offload.Core.Tests/QueueRulesTests.cs` (řádky 75–81)
- Create: `windows/tests/Offload.Core.Tests/TestEnvironment.cs`

**Interfaces:**
- Consumes: `CardFingerprint`, `HistoryStore`, `HistoryEntry` (úkoly 3–4).
- Produces: `JobConfig.RecordHistory` (bool, výchozí true), `JobConfig.HistoryFolder` (string?, null = `HistoryStore.Default`); `OffloadJob.SourceIdentity` (string?), interní seamy `OffloadJob.Identify` (`Func<string, string?>`, výchozí `CardIdentity.Of`) a `OffloadJob.VolumeOf` (`Func<string, CardVolume?>`); `record CardVolume(string? Identity, string FsSerial, long Capacity, string? Label, string Filesystem)` s `Key`, `ToHistoryCard()`, `static Of(string source)`; `NativeIO.TotalBytes(string) → long?`; `HistoryStore.DefaultFolderOverride` (interní, jen testy); `JobQueue.Runner(OffloadJob, Action<string>?)` bez parametru `identify`; CLI `--no-history`.

- [ ] **Step 1: Padající testy**

Nejdřív izolace testů od skutečné historie — nový `windows/tests/Offload.Core.Tests/TestEnvironment.cs`:

```csharp
using System.Runtime.CompilerServices;
using Offload.Core.History;

namespace Offload.Core.Tests;

static class TestEnvironment
{
    /// Every OffloadJob a test runs records history (R27). Without this it would land in the developer's own
    /// %APPDATA%\Offload\history.json — set once, before any test, independent of OFFLOAD_SETTINGS_DIR (which
    /// SettingsStoreTests.AppDataFolder_Override changes for a moment).
#pragma warning disable CA2255 // a test assembly: the initializer only isolates the tests' own app data
    [ModuleInitializer]
    internal static void IsolateHistory() =>
        HistoryStore.DefaultFolderOverride = Path.Combine(Path.GetTempPath(), "offload-tests", "history-" + Environment.ProcessId);
#pragma warning restore CA2255
}
```

```csharp
using Offload.Core.History;

namespace Offload.Core.Tests;

/// Spec 2.1: a finished stage 1 is recorded in history.json (also with failed files), a cancelled one never.
public class HistoryJobTests
{
    static OffloadOptions Options(Action<int>? beforeFile = null) => new()
    {
        ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, BeforeFileCopy = beforeFile,
    };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 50_000, 2);
        return card;
    }

    [Fact]
    public void Success_IsRecorded_WithFingerprintAndDestinations()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var history = dir.Sub("appdata");
        var config = new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")]) { Date = "2026-10-06", AnalyzeClips = false, HistoryFolder = history };
        var job = new OffloadJob(config, Options());

        job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        var store = new HistoryStore(history);
        var e = Assert.Single(store.Load());
        Assert.Null(e.Card);   // a folder, not a drive root
        Assert.Equal("A001", e.CardName);
        Assert.Equal("2026-10-06", e.Date);
        Assert.True(e.Success);
        Assert.True(e.Verified);
        Assert.Equal(2, e.Files);
        Assert.Equal(150_000, e.Bytes);
        Assert.Equal(CardFingerprint.Compute(Scanner.Scan(card)), (e.Fingerprint, e.Shape));
        Assert.Equal(job.Plan!.CardPaths, e.Destinations.Select(d => d.CardPath));
        Assert.Equal(1, Assert.Single(e.Generations).Seq);
        Assert.Equal(2, store.ReadFileList(e)!.Count);
    }

    [Fact]
    public void CardVolume_IsStored()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var config = new JobConfig(Card(dir), [dir.Sub("d1")]) { AnalyzeClips = false, HistoryFolder = history };
        var job = new OffloadJob(config, Options())
        {
            Identify = _ => @"\\?\Volume{x}\|1A2B3C4D",
            VolumeOf = _ => new CardVolume(@"\\?\Volume{x}\|1A2B3C4D", "1A2B3C4D", 256_060_514_304, "A001", "exFAT"),
        };

        job.Run();

        Assert.Equal(@"\\?\Volume{x}\|1A2B3C4D", job.SourceIdentity);
        var card = Assert.Single(new HistoryStore(history).Load()).Card!;
        Assert.Equal(("1A2B3C4D", 256_060_514_304L, "exFAT"), (card.FsSerial, card.Capacity, card.Filesystem));
    }

    // Review focus 4: a cancelled job leaves no history entry.
    [Fact]
    public void Cancelled_IsNotRecorded()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        OffloadJob? job = null;
        var config = new JobConfig(Card(dir), [dir.Sub("d1")]) { AnalyzeClips = false, HistoryFolder = history };
        job = new OffloadJob(config, Options(i => { if (i == 1) job!.Cancel(); }));

        job.Run();

        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
        Assert.Empty(new HistoryStore(history).Load());
    }

    [Fact]
    public void RecordHistoryOff_WritesNothing()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var config = new JobConfig(Card(dir), [dir.Sub("d1")]) { AnalyzeClips = false, HistoryFolder = history, RecordHistory = false };
        new OffloadJob(config, Options()).Run();
        Assert.False(File.Exists(Path.Combine(history, "history.json")));
    }

    [Fact]
    public void BrokenHistoryFolder_NeverFailsTheJob()
    {
        using var dir = new TempDir();
        var blocker = Path.Combine(dir.Path, "file-not-folder");
        File.WriteAllText(blocker, "x");
        var log = new List<string>();
        var config = new JobConfig(Card(dir), [dir.Sub("d1")]) { AnalyzeClips = false, HistoryFolder = blocker };
        var job = new OffloadJob(config, Options());
        job.Run(log.Add);
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Contains(log, l => l.StartsWith("history", StringComparison.Ordinal));
    }
}
```

`QueueRulesTests.Runner_RecordsTheCardOnTheCopyThread` — identitu teď čte úloha:

`windows/tests/Offload.Core.Tests/QueueRulesTests.cs`, řádky 75–81 — najít:

```csharp
    {
        using var dir = new TempDir();
        var job = new OffloadJob(new JobConfig(dir.Sub("card"), [dir.Sub("d1")]));
        var seen = new List<string>();
        var runner = new JobQueue.Runner(job, null, src => { seen.Add(src); return "ID-" + src.Length; });
        Assert.Null(runner.SourceIdentity);
        job.Cancel();
```

nahradit:

```csharp
    {
        using var dir = new TempDir();
        var seen = new List<string>();
        var job = new OffloadJob(new JobConfig(dir.Sub("card"), [dir.Sub("d1")])) { Identify = src => { seen.Add(src); return "ID-" + src.Length; } };
        var runner = new JobQueue.Runner(job, null);
        Assert.Null(runner.SourceIdentity);
        job.Cancel();
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~HistoryJobTests|FullyQualifiedName~QueueRulesTests"` → build FAIL.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/NativeIO.cs`, řádky 127–130 — najít:

```csharp
        Win32.GetDiskFreeSpaceEx(Path.GetFullPath(path), out var free, out _, out _) ? (long)free : null;

    /// Write size unit for unbuffered I/O on this file's volume (at least 4096).
    public static int SectorSize(SafeFileHandle h)
```

nahradit:

```csharp
        Win32.GetDiskFreeSpaceEx(Path.GetFullPath(path), out var free, out _, out _) ? (long)free : null;

    /// The size of the volume (a card's capacity — part of how history recognises a card, R1).
    public static long? TotalBytes(string path) =>
        Win32.GetDiskFreeSpaceEx(Path.GetFullPath(path), out _, out var total, out _) ? (long)total : null;

    /// Write size unit for unbuffered I/O on this file's volume (at least 4096).
    public static int SectorSize(SafeFileHandle h)
```

```csharp
using Offload.Core.Desktop;

namespace Offload.Core.History;

/// The card volume behind a source that is a drive root, as history stores it (spec 5.2 "card"). Touches the drive —
/// only on a worker thread (the copy thread, a card inspection, the CLI).
public sealed record CardVolume(string? Identity, string FsSerial, long Capacity, string? Label, string Filesystem)
{
    public CardKey Key => new(FsSerial, Capacity);

    public HistoryCard ToHistoryCard() =>
        new() { Identity = Identity, FsSerial = FsSerial, Capacity = Capacity, Label = Label, Filesystem = Filesystem };

    /// Null for a folder (not a drive root) or a volume that cannot be read.
    public static CardVolume? Of(string source)
    {
        try
        {
            if (Layout.DriveRootOf(source) is not string root) return null;
            if (NativeIO.VolumeSerial(root) is not uint serial || NativeIO.TotalBytes(root) is not long capacity) return null;
            return new CardVolume(CardIdentity.Of(root), serial.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), capacity,
                NativeIO.VolumeLabel(root), NativeIO.FilesystemType(root));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
```

`windows/src/Offload.Core/History/HistoryStore.cs`, řádky 18–22 — najít:

```csharp
    const string MutexName = @"Local\Offload-history";

    public static HistoryStore Default(Action<string>? log = null) => new(AppData.Folder, log);

    public string Folder { get; } = folder;
```

nahradit:

```csharp
    const string MutexName = @"Local\Offload-history";

    /// %APPDATA%\Offload (or OFFLOAD_SETTINGS_DIR). Tests set DefaultFolderOverride once, so no test ever writes into
    /// the developer's own history — not even while another test changes OFFLOAD_SETTINGS_DIR.
    public static HistoryStore Default(Action<string>? log = null) => new(DefaultFolderOverride ?? AppData.Folder, log);

    internal static string? DefaultFolderOverride { get; set; }

    public string Folder { get; } = folder;
```

`OffloadJob` — identita karty na vlákně kopie, zápis po dokončené stage 1 (i s chybami; zrušená úloha nic):

`windows/src/Offload.Core/OffloadJob.cs`, řádky 1–3 — najít:

```csharp
using System.Diagnostics;

namespace Offload.Core;
```

nahradit:

```csharp
using System.Diagnostics;
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core;
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 17–20 — najít:

```csharp
    public ProxySettings Proxy { get; set; } = new();
    public ExternalTools Tools { get; set; } = new();
}

```

nahradit:

```csharp
    public ProxySettings Proxy { get; set; } = new();
    public ExternalTools Tools { get; set; } = new();
    /// Write the finished copy into history.json (R27; CLI --no-history turns it off).
    public bool RecordHistory { get; set; } = true;
    /// Where history.json lives; null = HistoryStore.Default (%APPDATA%\Offload).
    public string? HistoryFolder { get; set; }
}

```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 67–74 — najít:

```csharp
    volatile OffloadPlan? planValue;
    volatile OffloadReport? reportValue;

    /// Set on a worker thread, read by the app's UI thread.
    public OffloadPlan? Plan { get => planValue; private set => planValue = value; }
    public OffloadReport? Report { get => reportValue; private set => reportValue = value; }

    public OffloadJob(JobConfig config)
```

nahradit:

```csharp
    volatile OffloadPlan? planValue;
    volatile OffloadReport? reportValue;
    volatile string? sourceIdentity;
    CardVolume? cardVolume;

    /// Set on a worker thread, read by the app's UI thread.
    public OffloadPlan? Plan { get => planValue; private set => planValue = value; }
    public OffloadReport? Report { get => reportValue; private set => reportValue = value; }
    /// The card in the source's drive when the copy started (`CardIdentity`), read on the copy thread; null before
    /// that or when unknown. "Vysunout kartu" ejects only this card.
    public string? SourceIdentity => sourceIdentity;

    /// Tests: what CardIdentity.Of / CardVolume.Of would read from the drive.
    internal Func<string, string?> Identify { get; init; } = CardIdentity.Of;
    internal Func<string, CardVolume?> VolumeOf { get; init; } = CardVolume.Of;

    public OffloadJob(JobConfig config)
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 136–139 — najít:

```csharp
    public bool RunCopy(Action<string>? log = null)
    {
        lock (gate)
        {
```

nahradit:

```csharp
    public bool RunCopy(Action<string>? log = null)
    {
        IdentifyCard(log);
        lock (gate)
        {
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 157–162 — najít:

```csharp
            {
                Update(s => s with { Phase = JobPhase.Cancelled });
                return false;
            }
            if (!Config.AnalyzeClips && !Config.Proxy.Enabled)
            {
```

nahradit:

```csharp
            {
                Update(s => s with { Phase = JobPhase.Cancelled });
                return false;   // a cancelled copy is not recorded in history
            }
            RecordHistory(plan, report, log);
            if (!Config.AnalyzeClips && !Config.Proxy.Enabled)
            {
```

`windows/src/Offload.Core/OffloadJob.cs`, řádky 177–180 — najít:

```csharp
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
            return false;
        }
    }
```

nahradit:

```csharp
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
            return false;
        }
    }

    /// Which card is in the drive — read on the copy thread before anything else (the UI never waits on a card).
    void IdentifyCard(Action<string>? log)
    {
        try
        {
            sourceIdentity = Identify(Config.Source);
            cardVolume = VolumeOf(Config.Source);
        }
        catch (Exception e)
        {
            log?.Invoke($"card identity: {e.Message}");
        }
    }

    /// The finished stage 1 (with failed files too) goes into history.json; this never fails the job.
    void RecordHistory(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        try
        {
            var (fingerprint, shape) = CardFingerprint.Compute(plan.Scan);
            var cardPaths = plan.CardPaths;
            var entry = new HistoryEntry
            {
                Id = HistoryStore.NewId(report.Finished),
                Card = cardVolume?.ToHistoryCard(),
                Source = plan.Source,
                CardName = plan.CardName,
                Date = plan.Date,
                Finished = report.Finished,
                Success = report.Success,
                Verified = report.Verified,
                FailedFiles = report.Success ? null : report.FailedFiles.Count,
                HashAlgorithms = [report.HashAlgorithm],
                Files = plan.Scan.Files.Count,
                Bytes = plan.Scan.TotalBytes,
                Fingerprint = fingerprint,
                Shape = shape,
                Destinations = plan.DestRoots.Select((root, i) => new HistoryDestination { Root = root, CardPath = cardPaths[i] }).ToList(),
                Generations =
                [
                    new HistoryGeneration
                    {
                        Seq = 1, Kind = HistoryGeneration.Full, Finished = report.Finished,
                        Files = plan.Scan.Files.Count, Bytes = plan.Scan.TotalBytes, Verified = report.Verified,
                    },
                ],
            };
            var store = Config.HistoryFolder is string folder ? new HistoryStore(folder, log) : HistoryStore.Default(log);
            store.Record(entry, plan.Scan.Files);
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
        }
    }
```

`windows/src/Offload.Core/Desktop/JobQueue.cs`, řádky 102–128 — najít:

```csharp
    }

    /// `identify` (default `CardIdentity.Of`) records which card is in the drive when its copy starts — on the copy
    /// thread, so the UI never waits on the card; "Vysunout kartu" later ejects only that card.
    public sealed class Runner(OffloadJob job, Action<string>? log, Func<string, string?>? identify = null) : IQueuedJob
    {
        volatile string? sourceIdentity;

        public OffloadJob Job { get; } = job;
        public bool IsCancelled => Job.IsCancelled;
        /// The card that was copied (`CardIdentity`), once the copy has started; null before or when unknown.
        public string? SourceIdentity => sourceIdentity;

        public bool RunCopy()
        {
            try
            {
                sourceIdentity = (identify ?? CardIdentity.Of)(Job.Config.Source);
            }
            catch (Exception e)
            {
                log?.Invoke($"card identity: {e.Message}");
            }
            return Job.RunCopy(log);
        }

        public void RunPost() => Job.RunPost(log);
```

nahradit:

```csharp
    }

    /// The job records which card is in the drive when its copy starts (OffloadJob.SourceIdentity, on the copy
    /// thread — the CLI needs it for history too); "Vysunout kartu" later ejects only that card.
    public sealed class Runner(OffloadJob job, Action<string>? log) : IQueuedJob
    {
        public OffloadJob Job { get; } = job;
        public bool IsCancelled => Job.IsCancelled;
        /// The card that was copied (`CardIdentity`), once the copy has started; null before or when unknown.
        public string? SourceIdentity => Job.SourceIdentity;

        public bool RunCopy() => Job.RunCopy(log);

        public void RunPost() => Job.RunPost(log);
```

CLI `--no-history`:

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 32–39 — najít:

```csharp
        var noBurnIn = new Option<bool>("--no-burn-in") { Description = "No burn-in of clip name and timecode." };
        var proxyFolder = new Option<string?>("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder }) cmd.Options.Add(o);
        cmd.SetAction(r =>
        {
```

nahradit:

```csharp
        var noBurnIn = new Option<bool>("--no-burn-in") { Description = "No burn-in of clip name and timecode." };
        var proxyFolder = new Option<string?>("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (%APPDATA%\\Offload\\history.json)." };

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder, noHistory }) cmd.Options.Add(o);
        cmd.SetAction(r =>
        {
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 49–52 — najít:

```csharp
                    Verify = !r.GetValue(noVerify),
                    AllowSameDevice = r.GetValue(allowSame),
                };
                if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
```

nahradit:

```csharp
                    Verify = !r.GetValue(noVerify),
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                };
                if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
```

(`JobItemViewModel` vytváří `new JobQueue.Runner(job, m => …)` se dvěma parametry — zůstává beze změny.)

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~HistoryJobTests|FullyQualifiedName~QueueRulesTests|FullyQualifiedName~JobQueueTests|FullyQualifiedName~JobTests"` → PASS; `dotnet build` (i `Offload.App`) bez varování; `dotnet test` → zelená. Po běhu sady nesmí v `%APPDATA%\Offload` vzniknout `history.json` (kontrola: `Test-Path "$env:APPDATA\Offload\history.json"` před a po je stejné).

- [ ] **Step 5: VYVOJ + commit**

Řádek nahoru do sekce plánu 6:

```markdown
- Úkol 5 — každá dokončená kopie (i s chybami, zrušená ne) se zapíše do historie s otiskem a seznamem souborů; identitu karty zjišťuje úloha sama na vlákně kopie (i v CLI); CLI `--no-history`; testy píšou historii jen do %TEMP%
```

```powershell
git add windows docs
git commit -m "Windows: record finished copies in the history" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: `CardInspector` + `HistoryMatcher`

Spec §2.1 „Jádro“ (kroky 1–3, `CardInspector`), texty UX tabulky, „CLI“, §6.1 úkol 6. Jeden `Scanner.Scan` slouží řádku historie, návrhu názvu (úkol 9) i kontrole místa (úkol 17).

**Files:**
- Modify: `windows/src/Offload.Cli/CopyCommand.cs` (řádky 3–6, 94–97, 159–161)
- Create: `windows/src/Offload.Core/History/CardInspector.cs`
- Create: `windows/src/Offload.Core/History/HistoryMatcher.cs`
- Create: `windows/src/Offload.Core/History/HistoryText.cs`
- Modify: `windows/src/Offload.Core/Messages.cs` (řádky 65–68)
- Modify: `windows/src/Offload.Core/Scanner.cs` (řádky 36–40, 44–47)
- Create: `windows/tests/Offload.Core.Tests/CardInspectorTests.cs`
- Create: `windows/tests/Offload.Core.Tests/HistoryMatcherTests.cs`
- Create: `windows/tests/Offload.Core.Tests/HistoryTextTests.cs`

**Interfaces:**
- Consumes: `CardFingerprint`, `FileTimes`, `HistoryStore.FindForCard/ReadFileList`, `CardVolume.Of` (úkoly 3–5).
- Produces: `enum HistoryMatchKind { Identical, NewFiles, Changed, NotCompared }`; `record HistoryMatch(HistoryEntry Entry, HistoryMatchKind Kind, IReadOnlyList<SourceFile> New, long NewBytes, IReadOnlyList<string> Changed, int Removed)`; `HistoryMatcher.Match(SourceScan, string fingerprint, HistoryEntry, IReadOnlyList<SourceFile>?, string? filesystem)`; `record CardInsight` (`Source`, `Volume`, `Filesystem`, `TotalBytes`, `FileCount`, `Clips`, `Fingerprint`, `Shape`, `History`, `Error`); `CardInspector.Inspect(string source, IReadOnlyList<HistoryEntry>? history, HistoryStore? store, CancellationToken ct) → CardInsight`; `enum HistoryTone { Neutral, Ok, Warning, Error }`; `HistoryText.Inspecting`, `HistoryText.When(DateTime)`, `HistoryText.DiskName(string root)` („Zaloha (D:)“), `HistoryText.Line(HistoryMatch, bool fullPaths = false) → (string Text, HistoryTone Tone)`; `Scanner.Scan(string root, CancellationToken ct = default)`; `Msg.CardUnreadable(string)`; `CopyCommand.Inspect(string source)` (interní).

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core.History;

namespace Offload.Core.Tests;

public class HistoryMatcherTests
{
    static readonly DateTime T0 = HistoryFixtures.T0;

    static readonly SourceFile[] Backed = [new("CLIP/A001C001.mov", 1000, T0), new("CLIP/A001C002.mov", 2000, T0.AddMinutes(1))];

    static SourceScan Scan(params SourceFile[] files) =>
        new() { Root = @"E:\", Files = files.OrderBy(f => f.RelPath, StringComparer.Ordinal).ToList(), Dirs = ["CLIP"], Skipped = [] };

    static HistoryMatch Match(SourceScan scan, string? filesystem = "exFAT", bool withList = true)
    {
        var (fingerprint, shape) = CardFingerprint.Compute(Backed);
        var entry = HistoryFixtures.Entry(T0, fingerprint: fingerprint, shape: shape);
        return HistoryMatcher.Match(scan, CardFingerprint.Compute(scan).Fingerprint, entry, withList ? Backed : null, filesystem);
    }

    [Fact]
    public void SameCard_Identical()
    {
        var m = Match(Scan(Backed));
        Assert.Equal(HistoryMatchKind.Identical, m.Kind);
        Assert.Empty(m.New);
    }

    [Fact]
    public void MoreClips_NewFiles_WithCountAndBytes()
    {
        var m = Match(Scan([.. Backed, new("CLIP/A001C003.mov", 3000, T0.AddMinutes(5)), new("CLIP/A001C004.mov", 500, T0.AddMinutes(6))]));
        Assert.Equal(HistoryMatchKind.NewFiles, m.Kind);
        Assert.Equal(["CLIP/A001C003.mov", "CLIP/A001C004.mov"], m.New.Select(f => f.RelPath));
        Assert.Equal(3500, m.NewBytes);
    }

    [Fact]
    public void KnownPathWithOtherSize_Changed()
    {
        var m = Match(Scan(new("CLIP/A001C001.mov", 1001, T0), Backed[1], new("CLIP/A001C003.mov", 3000, T0)));
        Assert.Equal(HistoryMatchKind.Changed, m.Kind);
        Assert.Equal(["CLIP/A001C001.mov"], m.Changed);
    }

    [Fact]
    public void DeletedInCamera_IsOnlyInformation()
    {
        var m = Match(Scan(Backed[0]));
        Assert.Equal(HistoryMatchKind.Identical, m.Kind);
        Assert.Equal(1, m.Removed);
    }

    // Review focus 2: FAT32 card after the change to winter time — every time an hour off, nothing changed.
    [Fact]
    public void Fat32_HourShift_IsTheSameCard()
    {
        var shifted = Backed.Select(f => f with { ModifiedUtc = f.ModifiedUtc.AddHours(-1) }).ToArray();
        var m = Match(Scan([.. shifted, new("CLIP/A001C003.mov", 3000, T0)]), "FAT32");
        Assert.Equal(HistoryMatchKind.NewFiles, m.Kind);
        Assert.Single(m.New);
    }

    [Fact]
    public void ExFat_HourShift_IsAChange()
    {
        var shifted = Backed.Select(f => f with { ModifiedUtc = f.ModifiedUtc.AddHours(-1) }).ToArray();
        var m = Match(Scan(shifted), "exFAT");
        Assert.Equal(HistoryMatchKind.Changed, m.Kind);
        Assert.Equal(2, m.Changed.Count);
    }

    [Fact]
    public void FileListMissing_NotCompared()
    {
        var m = Match(Scan([.. Backed, new("CLIP/A001C003.mov", 3000, T0)]), withList: false);
        Assert.Equal(HistoryMatchKind.NotCompared, m.Kind);
    }
}
```

```csharp
using System.Diagnostics;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class CardInspectorTests
{
    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001_261006_R1AB.mov"), 1000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002_261006_R1AB.mov"), 2000, 2);
        return card;
    }

    [Fact]
    public void Folder_SizeClipsFingerprint_NoHistory()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var insight = CardInspector.Inspect(card, history: null, store: null, CancellationToken.None);
        Assert.Null(insight.Error);
        Assert.Null(insight.Volume);
        Assert.Equal(3000, insight.TotalBytes);
        Assert.Equal(2, insight.FileCount);
        Assert.Equal(["A001C001_261006_R1AB", "A001C002_261006_R1AB"], insight.Clips.Select(c => c.Name));
        Assert.Equal(CardFingerprint.Compute(Scanner.Scan(card)), (insight.Fingerprint, insight.Shape));
        Assert.Null(insight.History);
    }

    [Fact]
    public void Folder_FoundByShape_NewFilesFromTheStoredList()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var store = new HistoryStore(dir.Sub("appdata"));
        var before = Scanner.Scan(card);
        var (fp, shape) = CardFingerprint.Compute(before);
        store.Record(HistoryFixtures.Entry(HistoryFixtures.T0, serial: null, fingerprint: fp, shape: shape), before.Files);

        var same = CardInspector.Inspect(card, store.Load(), store, CancellationToken.None);
        Assert.Equal(HistoryMatchKind.Identical, same.History!.Kind);

        // A folder is found by its shape: new files change it, so a grown folder is no longer recognised (spec 2.1 step 1).
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C003_261006_R1AB.mov"), 500, 3);
        Assert.Null(CardInspector.Inspect(card, store.Load(), store, CancellationToken.None).History);
    }

    [Fact]
    public void Cancelled_Throws()
    {
        using var dir = new TempDir();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => CardInspector.Inspect(Card(dir), null, null, cts.Token));
    }

    // Review focus 1: a card pulled out — an error in the insight, never an exception.
    [Fact]
    public void Unreadable_ErrorInTheInsight()
    {
        using var dir = new TempDir();
        var gone = Path.Combine(dir.Path, "gone");
        var insight = CardInspector.Inspect(gone, null, null, CancellationToken.None);
        Assert.StartsWith("Kartu nejde přečíst: ", insight.Error);
    }

    // Spec 5.5: 10 000 files well within the budget (3 s on a USB 3 reader; here a local disk).
    [Fact]
    public void TenThousandFiles_UnderBudget()
    {
        using var dir = new TempDir();
        var card = dir.Sub("big");
        for (int d = 0; d < 100; d++)
        {
            var sub = Directory.CreateDirectory(Path.Combine(card, "DCIM", $"{100 + d}CANON")).FullName;
            for (int f = 0; f < 100; f++) File.WriteAllBytes(Path.Combine(sub, $"IMG_{f:0000}.CR3"), []);
        }
        var sw = Stopwatch.StartNew();
        var insight = CardInspector.Inspect(card, [], null, CancellationToken.None);
        Assert.Equal(10_000, insight.FileCount);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"{sw.Elapsed.TotalSeconds:0.0} s");
    }
}
```

```csharp
using Offload.Core.History;

namespace Offload.Core.Tests;

/// Spec 2.1 UX table, word for word.
public class HistoryTextTests
{
    static readonly DateTime Finished = new DateTime(2026, 10, 6, 8, 15, 0, DateTimeKind.Local).ToUniversalTime();

    static HistoryMatch Match(HistoryMatchKind kind, bool success = true, bool verified = true, int newFiles = 0, long newBytes = 0, int changed = 0)
    {
        var e = HistoryFixtures.Entry(Finished, success: success, verified: verified);
        var added = Enumerable.Range(0, newFiles).Select(i => new SourceFile($"n{i}", 1, Finished)).ToList();
        return new HistoryMatch(e, kind, added, newBytes, Enumerable.Range(0, changed).Select(i => $"c{i}").ToList(), 0);
    }

    [Fact]
    public void Verified() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno", HistoryTone.Ok),
        HistoryText.Line(Match(HistoryMatchKind.Identical)));

    [Fact]
    public void NewFiles() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno · nové soubory: 12 (3,2 GB)", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.NewFiles, newFiles: 12, newBytes: 3_200_000_000)));

    [Fact]
    public void NotVerified() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — NEOVĚŘENO", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.Identical, verified: false)));

    [Fact]
    public void WithErrors() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — s chybami (3 soubory)", HistoryTone.Error),
        HistoryText.Line(Match(HistoryMatchKind.Identical, success: false)));

    [Fact]
    public void Changed() => Assert.Equal(
        ("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:), ale karta se od té doby změnila (4 soubory jinak) — doplnit nejde, jen celá kopie", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.Changed, changed: 4)));

    [Fact]
    public void NotCompared_IsThePlainLine() => Assert.Equal("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno",
        HistoryText.Line(Match(HistoryMatchKind.NotCompared)).Text);

    [Fact]
    public void Cli_FullPaths() => Assert.Equal(@"Zálohováno 6. 10. 2026 8:15 na D:\Zaloha, F:\Zaloha — ověřeno",
        HistoryText.Line(Match(HistoryMatchKind.Identical), fullPaths: true).Text);

    [Theory]
    [InlineData(@"D:\Zaloha", "Zaloha (D:)")]
    [InlineData(@"d:\Backup\Zaloha\", "Zaloha (D:)")]
    [InlineData(@"D:\", "D:")]
    [InlineData(@"\\NAS\share\Zaloha", "Zaloha")]
    public void DiskName(string root, string name) => Assert.Equal(name, HistoryText.DiskName(root));

}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~HistoryMatcherTests|FullyQualifiedName~CardInspectorTests|FullyQualifiedName~HistoryTextTests"` → build FAIL.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Scanner.cs`, řádky 36–40 — najít:

```csharp
    };

    public static SourceScan Scan(string root)
    {
        var files = new List<SourceFile>();
```

nahradit:

```csharp
    };

    /// `ct` is checked before every folder: a card inspection is cancelled when the card is swapped or pulled out.
    public static SourceScan Scan(string root, CancellationToken ct = default)
    {
        var files = new List<SourceFile>();
```

`windows/src/Offload.Core/Scanner.cs`, řádky 44–47 — najít:

```csharp
        void Walk(string dir, string prefix)
        {
            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", OneLevel).ToList(); }
```

nahradit:

```csharp
        void Walk(string dir, string prefix)
        {
            ct.ThrowIfCancellationRequested();
            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", OneLevel).ToList(); }
```
`windows/src/Offload.Core/Messages.cs`, řádky 65–68 — najít:

```csharp
    public static string NoHashRecord(string dir) => $"Ve složce {dir} není ASC MHL ani seznam součtů — není s čím porovnat.";
    public static string NoFilesToRead(string path) => $"V {path} nejsou soubory ke čtení.";

    // CLI
```

nahradit:

```csharp
    public static string NoHashRecord(string dir) => $"Ve složce {dir} není ASC MHL ani seznam součtů — není s čím porovnat.";
    public static string NoFilesToRead(string path) => $"V {path} nejsou soubory ke čtení.";
    public static string CardUnreadable(string detail) => $"Kartu nejde přečíst: {detail}";

    // CLI
```

```csharp
namespace Offload.Core.History;

/// Identical: nothing on the card that the backup lacks (files may have been deleted in the camera — `Removed`).
/// NewFiles: only additions — they can be appended (spec 2.2). Changed: a known path has another size or time — only a
/// full copy. NotCompared: the fingerprint differs but the entry's file list is missing or damaged.
public enum HistoryMatchKind { Identical, NewFiles, Changed, NotCompared }

public sealed record HistoryMatch(HistoryEntry Entry, HistoryMatchKind Kind, IReadOnlyList<SourceFile> New, long NewBytes,
    IReadOnlyList<string> Changed, int Removed);

public static class HistoryMatcher
{
    /// Spec 2.1, steps 2–3. `candidateFiles`: the entry's file list (only needed when the fingerprints differ).
    /// `filesystem`: the card's — FAT/FAT32 times may differ by whole hours (R3).
    public static HistoryMatch Match(SourceScan scan, string fingerprint, HistoryEntry candidate,
        IReadOnlyList<SourceFile>? candidateFiles, string? filesystem)
    {
        if (candidate.Fingerprint == fingerprint) return new(candidate, HistoryMatchKind.Identical, [], 0, [], 0);
        if (candidateFiles == null) return new(candidate, HistoryMatchKind.NotCompared, [], 0, [], 0);
        bool localTime = FileTimes.StoresLocalTime(filesystem);
        var known = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        foreach (var f in candidateFiles) known[f.RelPath] = f;
        var added = new List<SourceFile>();
        var changed = new List<string>();
        foreach (var f in scan.Files)
        {
            if (!known.Remove(f.RelPath, out var k)) added.Add(f);
            else if (k.Size != f.Size || !FileTimes.Same(k.ModifiedUtc, f.ModifiedUtc, localTime)) changed.Add(f.RelPath);
        }
        var kind = changed.Count > 0 ? HistoryMatchKind.Changed : added.Count > 0 ? HistoryMatchKind.NewFiles : HistoryMatchKind.Identical;
        return new(candidate, kind, added, added.Sum(f => f.Size), changed, known.Count);
    }
}
```

```csharp
namespace Offload.Core.History;

/// What one look at a card found (spec 2.1): size, clips, fingerprint and the history entry it matches. One scan serves
/// the history line, the card name suggestion (2.3) and the space check (2.5). `Error`: the card could not be read.
public sealed record CardInsight
{
    public required string Source { get; init; }
    /// Null for a folder that is not a drive root.
    public CardVolume? Volume { get; init; }
    public string? Filesystem { get; init; }
    public long TotalBytes { get; init; }
    public int FileCount { get; init; }
    public IReadOnlyList<Clip> Clips { get; init; } = [];
    public string Fingerprint { get; init; } = "";
    public string Shape { get; init; } = "";
    public HistoryMatch? History { get; init; }
    public string? Error { get; init; }
}

public static class CardInspector
{
    /// Reads only (a folder listing — nothing is ever written to the card). Runs off the UI thread; `ct` cancels it when
    /// the card is swapped. `history` null = not loaded (or unreadable): no history match.
    public static CardInsight Inspect(string source, IReadOnlyList<HistoryEntry>? history, HistoryStore? store, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var volume = CardVolume.Of(source);
            var filesystem = volume?.Filesystem ?? NativeIO.FilesystemType(source);
            var scan = Scanner.Scan(source, ct);
            var (fingerprint, shape) = CardFingerprint.Compute(scan);
            ct.ThrowIfCancellationRequested();
            HistoryMatch? match = null;
            if (history != null && HistoryStore.FindForCard(history, volume?.Key, shape) is { } candidate)
            {
                var list = candidate.Fingerprint == fingerprint ? null : store?.ReadFileList(candidate);
                match = HistoryMatcher.Match(scan, fingerprint, candidate, list, filesystem);
            }
            return new CardInsight
            {
                Source = source,
                Volume = volume,
                Filesystem = filesystem,
                TotalBytes = scan.TotalBytes,
                FileCount = scan.Files.Count,
                Clips = ClipDetector.Detect(scan.Files),
                Fingerprint = fingerprint,
                Shape = shape,
                History = match,
            };
        }
        catch (IOError e)
        {
            // Pulled out mid-scan, or a folder that cannot be listed: no insight, never an exception for the caller.
            return new CardInsight { Source = source, Error = Msg.CardUnreadable(e.Message) };
        }
    }
}
```

```csharp
using System.Globalization;
using Offload.Core.Desktop;

namespace Offload.Core.History;

public enum HistoryTone { Neutral, Ok, Warning, Error }

/// The history line under a card (spec 2.1 table) and in the CLI.
public static class HistoryText
{
    public const string Inspecting = "Zjišťuji, jestli už je karta zálohovaná…";

    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// "6. 10. 2026 8:15" in local time.
    public static string When(DateTime finishedUtc) => finishedUtc.ToLocalTime().ToString("d. M. yyyy H:mm", Cs);

    /// "Zaloha (D:)": the destination folder's name and its drive letter; a drive root alone is "D:", a share folder
    /// only its name.
    public static string DiskName(string root)
    {
        var trimmed = root.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        if (root.StartsWith(@"\\", StringComparison.Ordinal)) return name.Length > 0 ? name : root;
        var letter = trimmed.Length >= 2 && trimmed[1] == ':' ? trimmed[..2].ToUpperInvariant() : null;
        if (name.Length == 0 || trimmed.Length <= 2) return letter ?? root;
        return letter != null ? $"{name} ({letter})" : name;
    }

    static string Files(int n) => $"{n} {Texts.Plural(n, "soubor", "soubory", "souborů")}";

    /// `fullPaths`: the CLI names the destinations by their full root ("D:\Zaloha"), the GUI by DiskName.
    public static (string Text, HistoryTone Tone) Line(HistoryMatch m, bool fullPaths = false)
    {
        var e = m.Entry;
        var disks = string.Join(", ", e.Destinations.Select(d => fullPaths ? d.Root : DiskName(d.Root)));
        var head = $"Zálohováno {When(e.Finished)} na {disks}";
        if (m.Kind == HistoryMatchKind.Changed)
            return ($"{head}, ale karta se od té doby změnila ({Files(m.Changed.Count)} jinak) — doplnit nejde, jen celá kopie", HistoryTone.Warning);
        var (state, tone) = !e.Success ? ($" — s chybami ({Files(e.FailedFiles ?? 0)})", HistoryTone.Error)
            : !e.Verified ? (" — NEOVĚŘENO", HistoryTone.Warning)
            : (" — ověřeno", HistoryTone.Ok);
        var text = head + state;
        if (m.Kind == HistoryMatchKind.NewFiles)
        {
            text += $" · nové soubory: {m.New.Count} ({Format.BytesCs(m.NewBytes)})";
            if (tone == HistoryTone.Ok) tone = HistoryTone.Warning;
        }
        return (text, tone);
    }
}
```

CLI vypíše před plánem `Zálohováno … — ověřeno` (bez dotazu — skript nemá kdo potvrdit):

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 3–6 — najít:

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Cli;
```

nahradit:

```csharp
using Offload.Core.Desktop;
using Offload.Core.Hardware;
using Offload.Core.History;

namespace Offload.Cli;
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 94–97 — najít:

```csharp
    static int Run(JobConfig config, bool dryRun)
    {
        var job = new OffloadJob(config);
        OffloadPlan plan;
```

nahradit:

```csharp
    static int Run(JobConfig config, bool dryRun)
    {
        if (config.RecordHistory && Inspect(config.Source)?.History is { } known)
            Console.WriteLine(HistoryText.Line(known, fullPaths: true).Text);   // no question: a script has nobody to ask
        var job = new OffloadJob(config);
        OffloadPlan plan;
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 159–161 — najít:

```csharp
        return status.Phase == JobPhase.Done && report?.Success == true ? 0 : 1;
    }
}
```

nahradit:

```csharp
        return status.Phase == JobPhase.Done && report?.Success == true ? 0 : 1;
    }

    /// The card against history (spec 2.1 CLI). Never fails the copy: any problem only means no history line.
    internal static CardInsight? Inspect(string source)
    {
        try
        {
            var store = HistoryStore.Default(m => Console.Error.WriteLine(m));
            return CardInspector.Inspect(source, store.Load(), store, CancellationToken.None);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"history: {e.Message}");
            return null;
        }
    }
}
```

- [ ] **Step 4: Testy + celá sada**

Run: `dotnet test --filter "FullyQualifiedName~HistoryMatcherTests|FullyQualifiedName~CardInspectorTests|FullyQualifiedName~HistoryTextTests|FullyQualifiedName~ScannerTests"` → PASS (`TenThousandFiles_UnderBudget` vytváří 10 000 souborů, trvá pár sekund); `dotnet test` → zelená.

- [ ] **Step 5: VYVOJ + commit**

Řádek nahoru do sekce plánu 6:

```markdown
- Úkol 6 — pohled na kartu: jeden sken (zrušitelný), otisk, shoda s historií (beze změny / nové soubory / změněná / bez seznamu), texty řádku historie; CLI vypíše před plánem „Zálohováno … — ověřeno“
```

```powershell
git add windows docs
git commit -m "Windows: card inspection against the history" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: GUI — řádek historie u karty

Spec §2.1 „UX“ (tabulka stavů) a „Vlákna“, §6.1 úkol 7. Životní cyklus zjišťování je v `CardInspections` (Core, testovaný); `MainViewModel.History.cs` ho jen řídí přes `Task.Run` a dispatcher.

**Files:**
- Create: `windows/src/Offload.App/MainViewModel.History.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs` (řádky 152–155, 206–209, 213–216)
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs` (řádky 76–81, 156–160, 172–175, 204–207)
- Modify: `windows/src/Offload.App/MainViewModel.cs` (řádky 23–26)
- Modify: `windows/src/Offload.App/SetupPanel.xaml` (řádky 74–77)
- Modify: `windows/src/Offload.App/SourceItemViewModel.cs` (řádky 1–3, 15–18)
- Create: `windows/src/Offload.Core/Desktop/CardInspections.cs`
- Create: `windows/tests/Offload.Core.Tests/CardInspectionsTests.cs`

**Interfaces:**
- Consumes: `CardInspector.Inspect`, `HistoryStore.Default/Load`, `HistoryText` (úkoly 4–6).
- Produces: `CardInspections` (`Start(path, identity) → CancellationToken?`, `Finished(path, insight, currentIdentity, stillListed) → bool again`, `Drop(path)`, `CancelAll()`, `IsRunning(path)`, `Result(path, identity) → CardInsight?`, `static RowText(bool historyLoaded, bool running, CardInsight?) → (string?, HistoryTone)`); v `MainViewModel`: `InitHistory()`, `ReloadHistory()`, `InspectAll()`, `InspectMissing()`, `Inspect(path)`, `internal CardInsight? Insight(string path)`, `internal (string? Text, HistoryTone Tone) HistoryLine(string path)`; `SourceItemViewModel.HistoryText`, `HistoryTone`, `InsightChanged()`.

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class CardInspectionsTests
{
    static CardInsight Insight(HistoryMatch? match = null) => new() { Source = @"E:\", History = match };

    [Fact]
    public void OneAtATime_RequestDuringARun_RunsOnceMore()
    {
        var c = new CardInspections();
        Assert.NotNull(c.Start(@"E:\", "card1"));
        Assert.Null(c.Start(@"e:\", "card1"));   // same path, any case
        Assert.Null(c.Start(@"E:\", "card1"));
        Assert.True(c.Finished(@"E:\", Insight(), "card1", stillListed: true));
        Assert.NotNull(c.Result(@"E:\", "card1"));
        Assert.NotNull(c.Start(@"E:\", "card1"));
        Assert.False(c.Finished(@"E:\", Insight(), "card1", stillListed: true));
    }

    // Review focus 1 and spec 2.1: a card swapped in the same reader mid-inspection — the old result is thrown away.
    [Fact]
    public void SwappedCard_CancelsAndDiscards_ThenLooksAgain()
    {
        var c = new CardInspections();
        var token = c.Start(@"E:\", "card1")!.Value;
        c.Drop(@"E:\");
        Assert.Null(c.Start(@"E:\", "card2"));
        Assert.True(token.IsCancellationRequested);
        Assert.True(c.Finished(@"E:\", Insight(), "card2", stillListed: true));   // a late result of card1
        Assert.Null(c.Result(@"E:\", "card2"));
        Assert.Null(c.Result(@"E:\", "card1"));
    }

    [Fact]
    public void Gone_ResultForgotten_NoRerun()
    {
        var c = new CardInspections();
        c.Start(@"E:\", "card1");
        Assert.True(c.Finished(@"E:\", Insight(), "card1", stillListed: true) is false);
        c.Start(@"E:\", "card1");
        c.Drop(@"E:\");
        Assert.False(c.Finished(@"E:\", null, null, stillListed: false));
        Assert.Null(c.Result(@"E:\", "card1"));
        Assert.False(c.IsRunning(@"E:\"));
    }

    [Fact]
    public void Result_OnlyForTheSameCard()
    {
        var c = new CardInspections();
        c.Start(@"E:\", "card1");
        c.Finished(@"E:\", Insight(), "card1", stillListed: true);
        Assert.Null(c.Result(@"E:\", "card2"));
    }

    [Fact]
    public void RowText_InspectingUntilKnown()
    {
        Assert.Equal((HistoryText.Inspecting, HistoryTone.Neutral), CardInspections.RowText(historyLoaded: false, running: false, null));
        Assert.Equal((HistoryText.Inspecting, HistoryTone.Neutral), CardInspections.RowText(true, true, null));
        Assert.Equal(((string?)null, HistoryTone.Neutral), CardInspections.RowText(true, false, null));
        Assert.Equal(((string?)null, HistoryTone.Neutral), CardInspections.RowText(true, false, Insight()));
        var m = new HistoryMatch(HistoryFixtures.Entry(HistoryFixtures.T0), HistoryMatchKind.Identical, [], 0, [], 0);
        Assert.Equal(HistoryTone.Ok, CardInspections.RowText(true, true, Insight(m)).Tone);   // a rerun keeps the last line
    }
}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~CardInspectionsTests"` → build FAIL.

- [ ] **Step 3: Implementace**

```csharp
using Offload.Core.History;

namespace Offload.Core.Desktop;

/// The life of the card inspections in the Karty list (spec 2.1 "Vlákna"), without a window: one inspection per
/// source at a time, a request while it runs makes it run once more afterwards, a source that disappears or now holds
/// another card (`Identity`, as `Volumes.NamesToDrop` decides) cancels it and its result is thrown away. UI thread only.
public sealed class CardInspections
{
    sealed class Run(string? identity)
    {
        public readonly string? Identity = identity;
        public readonly CancellationTokenSource Cts = new();
        public bool Again;
    }

    readonly Dictionary<string, Run> running = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (string? Identity, CardInsight Insight)> results = new(StringComparer.OrdinalIgnoreCase);

    static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// A look at `path` (holding the card `identity`) is wanted. The token to start it with, or null when one runs
    /// already — that one then runs once more (and is cancelled at once when it looks at another card).
    public CancellationToken? Start(string path, string? identity)
    {
        if (running.TryGetValue(path, out var r))
        {
            r.Again = true;
            if (!Same(r.Identity, identity)) r.Cts.Cancel();
            return null;
        }
        var run = new Run(identity);
        running[path] = run;
        return run.Cts.Token;
    }

    /// The run for `path` ended; `insight` is null when it was cancelled or failed. Its result is kept only while the
    /// path is still listed and holds the card it looked at (`currentIdentity`). True = start once more.
    public bool Finished(string path, CardInsight? insight, string? currentIdentity, bool stillListed)
    {
        if (!running.Remove(path, out var run)) return false;
        run.Cts.Dispose();
        if (!stillListed)
        {
            results.Remove(path);
            return false;
        }
        bool sameCard = Same(run.Identity, currentIdentity);
        if (!sameCard) results.Remove(path);
        else if (insight != null) results[path] = (run.Identity, insight);
        return run.Again || !sameCard;
    }

    /// The source left the list or holds another card now: cancel its run and forget its result.
    public void Drop(string path)
    {
        if (running.TryGetValue(path, out var r)) r.Cts.Cancel();
        results.Remove(path);
    }

    public void CancelAll()
    {
        foreach (var r in running.Values) r.Cts.Cancel();
    }

    public bool IsRunning(string path) => running.ContainsKey(path);

    /// The last result for the card that is at `path` now, or null.
    public CardInsight? Result(string path, string? identity) =>
        results.TryGetValue(path, out var r) && Same(r.Identity, identity) ? r.Insight : null;

    /// The history line of a card row (spec 2.1 table): "Zjišťuji…" until history is loaded and the first look is
    /// done, then the match (nothing for an unknown card or one that could not be read).
    public static (string? Text, HistoryTone Tone) RowText(bool historyLoaded, bool running, CardInsight? insight)
    {
        if (insight == null) return historyLoaded && !running ? (null, HistoryTone.Neutral) : (HistoryText.Inspecting, HistoryTone.Neutral);
        return insight.History is { } m ? HistoryText.Line(m) : (null, HistoryTone.Neutral);
    }
}
```

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.App;

/// Spec 2.1: under every card the line "Zálohováno …" from history.json. history.json is read off the UI thread at start
/// and after every finished copy; each source is looked at (scan + fingerprint + match) by its own Task.
public sealed partial class MainViewModel
{
    readonly CardInspections inspections = new();
    HistoryStore historyStore = null!;
    // Null until loaded: the rows say "Zjišťuji…" meanwhile.
    IReadOnlyList<HistoryEntry>? history;
    bool historyLoading, historyReload;

    void InitHistory()
    {
        historyStore = HistoryStore.Default(m => AppLog.Write(m));
        ReloadHistory();
    }

    /// Reads history.json off the UI thread, then looks at every card again; a request during a read runs once more.
    void ReloadHistory()
    {
        if (historyLoading)
        {
            historyReload = true;
            return;
        }
        historyLoading = true;
        var store = historyStore;
        Task.Run(() => store.Load()).ContinueWith(t =>
        {
            historyLoading = false;
            if (closing) return;
            if (t.IsCompletedSuccessfully) history = t.Result;
            else
            {
                AppLog.Write($"history load: {t.Exception?.GetBaseException().Message}");
                history ??= [];
            }
            InspectAll();
            if (historyReload)
            {
                historyReload = false;
                ReloadHistory();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void InspectAll()
    {
        foreach (var s in Sources) Inspect(s.Path);
    }

    /// Only the sources without a result for the card in them now (new cards, swapped cards).
    void InspectMissing()
    {
        foreach (var s in Sources)
            if (inspections.Result(s.Path, IdentityOf(s.Path)) == null && !inspections.IsRunning(s.Path)) Inspect(s.Path);
    }

    void Inspect(string path)
    {
        if (history is not { } entries || closing) return;   // InspectAll runs once history is loaded
        if (inspections.Start(path, IdentityOf(path)) is not CancellationToken ct) return;
        var store = historyStore;
        Task.Run(() => CardInspector.Inspect(path, entries, store, ct), ct).ContinueWith(t =>
        {
            if (t.IsFaulted) AppLog.Write($"card inspect {path}: {t.Exception?.GetBaseException().Message}");
            bool listed = Sources.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
            bool again = inspections.Finished(path, t.IsCompletedSuccessfully ? t.Result : null, IdentityOf(path), listed);
            if (closing) return;
            RowFor(path)?.InsightChanged();
            SetupChanged?.Invoke();
            if (again) Inspect(path);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// The card in a scanned volume (`CardIdentity` from the scan); null for a manual folder.
    string? IdentityOf(string path) =>
        volumes.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase))?.Identity;

    SourceItemViewModel? RowFor(string path) =>
        Sources.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

    /// The last look at the card that is at `path` now, or null.
    internal CardInsight? Insight(string path) => inspections.Result(path, IdentityOf(path));

    internal (string? Text, HistoryTone Tone) HistoryLine(string path) =>
        CardInspections.RowText(history != null, inspections.IsRunning(path), Insight(path));
}
```

Napojení: načtení historie při startu, `Drop` u karty, která odešla nebo se vyměnila (stejné pravidlo jako `NamesToDrop`), zjištění nových karet po každém skenu svazků, znovu po dokončené kopii (úloha právě kartu zapsala) a po ⟳; zrušení při zavírání:

`windows/src/Offload.App/MainViewModel.cs`, řádky 23–26 — najít:

```csharp
        InitQueue();
        InitHardware();
    }

```

nahradit:

```csharp
        InitQueue();
        InitHardware();
        InitHistory();
    }

```
`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 76–81 — najít:

```csharp
        foreach (var d in Destinations) d.Probe();
        RefreshLut();
        RefreshProxyFolder();
    }

    void HealthCheck()
```

nahradit:

```csharp
        foreach (var d in Destinations) d.Probe();
        RefreshLut();
        RefreshProxyFolder();
        ReloadHistory();   // and every card is looked at again
    }

    void HealthCheck()
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 156–160 — najít:

```csharp
        selected.Clear();
        selected.UnionWith(next);
        foreach (var p in drop) cardNames.Remove(p);
        var sig = string.Join("\n", found.Where(v => !v.IsDestination).Select(v => $"{v.Path}|{v.Identity}|{v.Name}|{v.Total - v.Free}|{v.Format}|{v.LooksLikeCard}|{selected.Contains(v.Path)}"))
            + "\n" + string.Join("\n", manualSources.Select(p => $"{p}|{selected.Contains(p)}"));
```

nahradit:

```csharp
        selected.Clear();
        selected.UnionWith(next);
        foreach (var p in drop)
        {
            cardNames.Remove(p);
            inspections.Drop(p);   // its history line belonged to the card that left
        }
        var sig = string.Join("\n", found.Where(v => !v.IsDestination).Select(v => $"{v.Path}|{v.Identity}|{v.Name}|{v.Total - v.Free}|{v.Format}|{v.LooksLikeCard}|{selected.Contains(v.Path)}"))
            + "\n" + string.Join("\n", manualSources.Select(p => $"{p}|{selected.Contains(p)}"));
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 172–175 — najít:

```csharp
            RebuildDestinations();
        }
        foreach (var j in Jobs) j.VolumesChanged();   // "Vysunout kartu" only while the copied card is in its drive
        RefreshLut();   // the LUT may sit on a disk that just came or went
```

nahradit:

```csharp
            RebuildDestinations();
        }
        InspectMissing();   // new or swapped cards
        foreach (var j in Jobs) j.VolumesChanged();   // "Vysunout kartu" only while the copied card is in its drive
        RefreshLut();   // the LUT may sit on a disk that just came or went
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 204–207 — najít:

```csharp
        RebuildSources();
        ReadManualLabel(path);
    }

```

nahradit:

```csharp
        RebuildSources();
        ReadManualLabel(path);
        Inspect(path);
    }

```
`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 152–155 — najít:

```csharp
        item.Refresh();
        if (!closing && item.SafeToEject) SystemSounds.Asterisk.Play();
    }

```

nahradit:

```csharp
        item.Refresh();
        if (!closing && item.SafeToEject) SystemSounds.Asterisk.Play();
        if (!closing) ReloadHistory();   // the job has just recorded the card: its line says "Zálohováno"
    }

```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 206–209 — najít:

```csharp
            if (Jobs.Any(j => !j.IsFinished) && !confirm()) return false;
            closing = true;
            foreach (var j in Jobs) j.Job.Cancel();
            foreach (var j in Jobs) j.Refresh();
```

nahradit:

```csharp
            if (Jobs.Any(j => !j.IsFinished) && !confirm()) return false;
            closing = true;
            inspections.CancelAll();
            foreach (var j in Jobs) j.Job.Cancel();
            foreach (var j in Jobs) j.Refresh();
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 213–216 — najít:

```csharp
        }
        closeRaised = true;
        ticker.Stop();
        healthTimer.Stop();
```

nahradit:

```csharp
        }
        closeRaised = true;
        inspections.CancelAll();
        ticker.Stop();
        healthTimer.Stop();
```
`windows/src/Offload.App/SourceItemViewModel.cs`, řádky 1–3 — najít:

```csharp
﻿using Offload.Core;

namespace Offload.App;
```

nahradit:

```csharp
﻿using Offload.Core;
using Offload.Core.History;

namespace Offload.App;
```

`windows/src/Offload.App/SourceItemViewModel.cs`, řádky 15–18 — najít:

```csharp
    public string CardNamePlaceholder { get; } = defaultName;

    public bool IsSelected
    {
```

nahradit:

```csharp
    public string CardNamePlaceholder { get; } = defaultName;

    /// "Zálohováno … — ověřeno" (spec 2.1), or null for a card history does not know.
    public string? HistoryText => owner.HistoryLine(path).Text;
    public HistoryTone HistoryTone => owner.HistoryLine(path).Tone;

    /// A card inspection finished: the history line (and what depends on it) may have changed.
    internal void InsightChanged() => RaiseMany(nameof(HistoryText), nameof(HistoryTone));

    public bool IsSelected
    {
```

Řádek pod `Detail` karty (šedá / zelená / oranžová / červená podle `HistoryTone`):

`windows/src/Offload.App/SetupPanel.xaml`, řádky 74–77 — najít:

```xml
                                        <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
                                        <TextBlock Text="{Binding Detail}" Style="{StaticResource Caption}" Margin="0,2,0,0" />
                                        <DockPanel Margin="0,6,0,0" Visibility="{Binding IsSelected, Converter={StaticResource BoolToVisible}}">
                                            <TextBlock Text="Název složky" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0" />
```

nahradit:

```xml
                                        <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
                                        <TextBlock Text="{Binding Detail}" Style="{StaticResource Caption}" Margin="0,2,0,0" />
                                        <TextBlock Text="{Binding HistoryText}" FontSize="12" Margin="0,2,0,0" TextWrapping="Wrap"
                                                   AutomationProperties.AutomationId="CardHistoryText"
                                                   Visibility="{Binding HistoryText, Converter={StaticResource NullToCollapsed}}">
                                            <TextBlock.Style>
                                                <Style TargetType="TextBlock">
                                                    <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding HistoryTone}" Value="Ok">
                                                            <Setter Property="Foreground" Value="{DynamicResource SystemFillColorSuccessBrush}" />
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding HistoryTone}" Value="Warning">
                                                            <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCautionBrush}" />
                                                        </DataTrigger>
                                                        <DataTrigger Binding="{Binding HistoryTone}" Value="Error">
                                                            <Setter Property="Foreground" Value="{DynamicResource SystemFillColorCriticalBrush}" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </TextBlock.Style>
                                        </TextBlock>
                                        <DockPanel Margin="0,6,0,0" Visibility="{Binding IsSelected, Converter={StaticResource BoolToVisible}}">
                                            <TextBlock Text="Název složky" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0" />
```

- [ ] **Step 4: Testy + build**

Run: `dotnet test --filter "FullyQualifiedName~CardInspectionsTests"` → PASS; `dotnet build` (XAML se zkompiluje) bez varování; `dotnet test` → zelená. GUI nespouštět — co ověřit ručně, je v seznamu úkolu 19 (body 1–3).

- [ ] **Step 5: VYVOJ + commit**

Řádek nahoru do sekce plánu 6:

```markdown
- Úkol 7 — GUI: pod kartou řádek „Zálohováno 6. 10. 2026 8:15 na Zaloha (D:) … — ověřeno“ (zeleně / oranžově / červeně, šedě „Zjišťuji…“), zjišťuje se mimo UI vlákno po jedné úloze na kartu; výměna karty ve čtečce běžící zjišťování zruší a výsledek zahodí; po dokončené kopii se historie načte znovu
```

```powershell
git add windows docs
git commit -m "Windows GUI: history line under each card" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: GUI — okno „Karty už jsou zálohované“

Spec §2.1 „Varování před celou kopií“, §6.1 úkol 8. Vlastní WPF okno (MessageBox neumí tři vlastní tlačítka), vlastník = hlavní okno; obsah počítá `BackedUpPrompt` v Core.

**Files:**
- Create: `windows/src/Offload.App/BackedUpPromptWindow.xaml`
- Create: `windows/src/Offload.App/BackedUpPromptWindow.xaml.cs`
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs` (řádky 24–27, 81–85, 95–103)
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs` (řádky 243–246)
- Modify: `windows/src/Offload.App/MainWindow.xaml.cs` (řádky 26–29)
- Create: `windows/src/Offload.Core/Desktop/BackedUpPrompt.cs`
- Create: `windows/tests/Offload.Core.Tests/BackedUpPromptTests.cs`

**Interfaces:**
- Consumes: `Insight(path)`, `HistoryMatch`, `HistoryText.When/DiskName` (úkoly 6–7).
- Produces: `enum BackedUpChoice { CopyAgain, SkipBackedUp, Cancel }`; `record BackedUpPromptModel(string Title, IReadOnlyList<string> Lines, string Question)`; `BackedUpPrompt.ShouldAsk(CardInsight?)`, `Line(HistoryMatch)`, `NewFolder(HistoryEntry, string date, string plannedName)`, `Build(IReadOnlyList<(HistoryMatch, string PlannedName)>, string date)`, konstanty textů tlačítek; `MainViewModel.AskBackedUp` (`Func<BackedUpPromptModel, BackedUpChoice>?`), `SelectedSources()`, `PlannedName(path)`, `StartJobs(IReadOnlyCollection<string> skip)`; `BackedUpPromptWindow.Ask(Window owner, BackedUpPromptModel)`.

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class BackedUpPromptTests
{
    static readonly DateTime Finished = new DateTime(2026, 10, 6, 8, 15, 0, DateTimeKind.Local).ToUniversalTime();

    static HistoryMatch Match(string card, HistoryMatchKind kind = HistoryMatchKind.Identical, int newFiles = 0, bool verified = true)
    {
        var e = HistoryFixtures.Entry(Finished, cardName: card, verified: verified);
        e.Date = "2026-10-06";
        return new HistoryMatch(e, kind, Enumerable.Range(0, newFiles).Select(i => new SourceFile($"n{i}", 1, Finished)).ToList(), newFiles, [], 0);
    }

    [Fact]
    public void TwoCards_SpecText()
    {
        var model = BackedUpPrompt.Build([(Match("A001"), "A001"), (Match("A002", HistoryMatchKind.NewFiles, 12), "A002")], "2026-10-06");
        Assert.Equal("Karty už jsou zálohované", model.Title);
        Assert.Equal(
            ["A001 — 6. 10. 2026 8:15, Zaloha (D:), Zaloha (F:), ověřeno", "A002 — 6. 10. 2026 8:15, Zaloha (D:), Zaloha (F:), ověřeno · nové soubory: 12"],
            model.Lines);
        Assert.Equal("Zkopírovat je znovu celé? Vzniknou nové složky (A001_2, A002_2).", model.Question);
    }

    [Fact]
    public void OneCard_Singular_OtherDay_NewDateFolder()
    {
        var model = BackedUpPrompt.Build([(Match("A001", verified: false), "A001")], "2026-10-07");
        Assert.Equal("Karta už je zálohovaná", model.Title);
        Assert.Equal(["A001 — 6. 10. 2026 8:15, Zaloha (D:), Zaloha (F:), NEOVĚŘENO"], model.Lines);
        Assert.Equal(@"Zkopírovat ji znovu celou? Vznikne nová složka (2026-10-07\A001).", model.Question);
    }

    [Fact]
    public void Asked_OnlyForKnownCards_NotForChangedOnes()
    {
        Assert.False(BackedUpPrompt.ShouldAsk(null));
        Assert.False(BackedUpPrompt.ShouldAsk(new CardInsight { Source = @"E:\" }));
        Assert.True(BackedUpPrompt.ShouldAsk(new CardInsight { Source = @"E:\", History = Match("A001") }));
        Assert.True(BackedUpPrompt.ShouldAsk(new CardInsight { Source = @"E:\", History = Match("A001", HistoryMatchKind.NotCompared) }));
        Assert.False(BackedUpPrompt.ShouldAsk(new CardInsight { Source = @"E:\", History = Match("A001", HistoryMatchKind.Changed) }));
    }
}
```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~BackedUpPromptTests"` → build FAIL.

- [ ] **Step 3: Implementace**

```csharp
using Offload.Core.History;

namespace Offload.Core.Desktop;

public enum BackedUpChoice { CopyAgain, SkipBackedUp, Cancel }

/// What the window "Karty už jsou zálohované" shows (spec 2.1 "Varování před celou kopií").
public sealed record BackedUpPromptModel(string Title, IReadOnlyList<string> Lines, string Question);

public static class BackedUpPrompt
{
    public const string CopyAgainText = "Kopírovat znovu celé";
    public const string SkipText = "Vynechat zálohované";
    public const string CancelText = "Zrušit";

    /// A selected card that history knows is asked about before a full copy — except one that changed since: its own
    /// line already says that only a full copy is possible.
    public static bool ShouldAsk(CardInsight? insight) => insight?.History is { Kind: not HistoryMatchKind.Changed };

    /// "A001 — 6. 10. 2026 8:15, Zaloha (D:), Zaloha2 (F:), ověřeno · nové soubory: 12"
    public static string Line(HistoryMatch m)
    {
        var e = m.Entry;
        var state = !e.Success ? $"s chybami ({e.FailedFiles ?? 0} {Texts.Plural(e.FailedFiles ?? 0, "soubor", "soubory", "souborů")})"
            : e.Verified ? "ověřeno" : "NEOVĚŘENO";
        var line = $"{e.CardName} — {HistoryText.When(e.Finished)}, {string.Join(", ", e.Destinations.Select(d => HistoryText.DiskName(d.Root)))}, {state}";
        return m.Kind == HistoryMatchKind.NewFiles ? $"{line} · nové soubory: {m.New.Count}" : line;
    }

    /// The folder a full copy would create: "A001_2" when the card was copied under the same name into today's date
    /// folder, otherwise the name in the date folder ("2026-10-07\A001").
    public static string NewFolder(HistoryEntry entry, string date, string plannedName) =>
        entry.Date == date && string.Equals(entry.CardName, plannedName, StringComparison.OrdinalIgnoreCase)
            ? $"{plannedName}_2"
            : $@"{date}\{plannedName}";

    /// `cards`: each backed-up card with the folder name Start would use for it.
    public static BackedUpPromptModel Build(IReadOnlyList<(HistoryMatch Match, string PlannedName)> cards, string date)
    {
        var folders = string.Join(", ", cards.Select(c => NewFolder(c.Match.Entry, date, c.PlannedName)));
        return cards.Count == 1
            ? new BackedUpPromptModel("Karta už je zálohovaná", [Line(cards[0].Match)], $"Zkopírovat ji znovu celou? Vznikne nová složka ({folders}).")
            : new BackedUpPromptModel("Karty už jsou zálohované", cards.Select(c => Line(c.Match)).ToList(),
                $"Zkopírovat je znovu celé? Vzniknou nové složky ({folders}).");
    }
}
```

```xml
<Window x:Class="Offload.App.BackedUpPromptWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Offload" SizeToContent="WidthAndHeight" MinWidth="420" MaxWidth="720" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Icon="pack://application:,,,/Offload.ico">
    <StackPanel Margin="22,18">
        <TextBlock Text="{Binding Title}" FontSize="17" FontWeight="SemiBold" />
        <ItemsControl ItemsSource="{Binding Lines}" Margin="0,12,0,0">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <TextBlock TextWrapping="Wrap" Margin="0,0,0,4">
                        <Run Text="•" />
                        <Run Text="{Binding Mode=OneWay}" />
                    </TextBlock>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <TextBlock Text="{Binding Question}" Margin="0,10,0,0" TextWrapping="Wrap" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
            <Button x:Name="CopyAgainButton" Click="CopyAgain_Click" Padding="12,5" AutomationProperties.AutomationId="BackedUpCopyAgain" />
            <Button x:Name="SkipButton" Click="Skip_Click" Padding="12,5" Margin="8,0,0,0" IsDefault="True"
                    Style="{DynamicResource AccentButtonStyle}" AutomationProperties.AutomationId="BackedUpSkip" />
            <Button x:Name="CancelButton" IsCancel="True" Padding="12,5" Margin="8,0,0,0" MinWidth="80" AutomationProperties.AutomationId="BackedUpCancel" />
        </StackPanel>
    </StackPanel>
</Window>
```

```csharp
using System.Windows;
using Offload.Core.Desktop;

namespace Offload.App;

/// "Karty už jsou zálohované" before a full copy (spec 2.1). Own window: MessageBox cannot have three own buttons.
public partial class BackedUpPromptWindow : Window
{
    BackedUpChoice choice = BackedUpChoice.Cancel;   // closed with the X or Esc = nothing starts

    BackedUpPromptWindow(BackedUpPromptModel model)
    {
        InitializeComponent();
        DataContext = model;
        CopyAgainButton.Content = BackedUpPrompt.CopyAgainText;
        SkipButton.Content = BackedUpPrompt.SkipText;
        CancelButton.Content = BackedUpPrompt.CancelText;
    }

    public static BackedUpChoice Ask(Window owner, BackedUpPromptModel model)
    {
        var w = new BackedUpPromptWindow(model) { Owner = owner };
        w.ShowDialog();
        return w.choice;
    }

    void CopyAgain_Click(object sender, RoutedEventArgs e)
    {
        choice = BackedUpChoice.CopyAgain;
        Close();
    }

    void Skip_Click(object sender, RoutedEventArgs e)
    {
        choice = BackedUpChoice.SkipBackedUp;
        Close();
    }
}
```

`Start` po kontrole LUTu a složky pro proxy: dotaz, „Vynechat zálohované“ karty odškrtne a nespustí, „Zrušit“ nespustí nic:

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 24–27 — najít:

```csharp
    /// Closing was requested while jobs ran; they are cancelled and finished now — close the window.
    public event Action? CloseReady;

    void InitQueue()
```

nahradit:

```csharp
    /// Closing was requested while jobs ran; they are cancelled and finished now — close the window.
    public event Action? CloseReady;
    /// Shows "Karty už jsou zálohované" (set by the window; modal, the user answers — no disk is waited on).
    public Func<BackedUpPromptModel, BackedUpChoice>? AskBackedUp { get; set; }

    void InitQueue()
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 81–85 — najít:

```csharp
                break;
            }
            if (ReadyToStart) StartJobs();   // anything may have changed while the LUT was checked
        }
        catch (Exception e)
```

nahradit:

```csharp
                break;
            }
            // Spec 2.1: cards history already knows are copied again in full only when the user says so.
            var skip = new List<string>();
            var backedUp = SelectedSources().Where(p => BackedUpPrompt.ShouldAsk(Insight(p))).ToList();
            if (backedUp.Count > 0 && AskBackedUp is { } ask)
            {
                var model = BackedUpPrompt.Build(backedUp.Select(p => (Insight(p)!.History!, PlannedName(p))).ToList(), DateString);
                switch (ask(model))
                {
                    case BackedUpChoice.Cancel:
                        return;
                    case BackedUpChoice.SkipBackedUp:
                        skip.AddRange(backedUp);
                        break;
                }
            }
            if (ReadyToStart) StartJobs(skip);   // anything may have changed while the LUT was checked
        }
        catch (Exception e)
```

`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 95–103 — najít:

```csharp
    }

    void StartJobs()
    {
        var sources = volumes.Select(v => v.Path).Concat(manualSources).Where(selected.Contains).ToList();
        foreach (var src in sources)
        {
            var name = cardNames.GetValueOrDefault(src) is { Length: > 0 } n && n.Trim().Length > 0 ? n.Trim() : null;
            var config = new JobConfig(src, Settings.DestRoots.ToList())
```

nahradit:

```csharp
    }

    /// `skip`: backed-up cards the user chose not to copy again — they are unticked, not started.
    void StartJobs(IReadOnlyCollection<string> skip)
    {
        foreach (var src in SelectedSources())
        {
            if (skip.Contains(src, StringComparer.OrdinalIgnoreCase))
            {
                selected.Remove(src);
                continue;
            }
            var name = cardNames.GetValueOrDefault(src) is { Length: > 0 } n && n.Trim().Length > 0 ? n.Trim() : null;
            var config = new JobConfig(src, Settings.DestRoots.ToList())
```
`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 243–246 — najít:

```csharp
    internal bool IsSelected(string path) => selected.Contains(path);

    internal void SetSelected(string path, bool on)
    {
```

nahradit:

```csharp
    internal bool IsSelected(string path) => selected.Contains(path);

    /// The ticked sources in the order of the list (volumes, then manual folders).
    List<string> SelectedSources() => volumes.Select(v => v.Path).Concat(manualSources).Where(selected.Contains).ToList();

    /// The card folder name a start would use now: the typed name, otherwise the placeholder.
    internal string PlannedName(string path) =>
        cardNames.GetValueOrDefault(path) is { } typed && typed.Trim().Length > 0 ? typed.Trim() : DefaultCardName(path);

    internal void SetSelected(string path, bool on)
    {
```
`windows/src/Offload.App/MainWindow.xaml.cs`, řádky 26–29 — najít:

```csharp
        });
        vm.AttentionRequested += _ => NativeMethods.Flash(this);
        // Spec 8a.2: the USB check is on demand only (menu Nástroje, Ctrl+U, the Hardware section) — no start-up question.
        vm.HardwareRequested += hw =>
```

nahradit:

```csharp
        });
        vm.AttentionRequested += _ => NativeMethods.Flash(this);
        vm.AskBackedUp = model => BackedUpPromptWindow.Ask(this, model);
        // Spec 8a.2: the USB check is on demand only (menu Nástroje, Ctrl+U, the Hardware section) — no start-up question.
        vm.HardwareRequested += hw =>
```

- [ ] **Step 4: Testy + build**

Run: `dotnet test --filter "FullyQualifiedName~BackedUpPromptTests"` → PASS; `dotnet build` bez varování; `dotnet test` → zelená. Ruční ověření okna: úkol 19, bod 4.

- [ ] **Step 5: VYVOJ + commit**

Řádek nahoru do sekce plánu 6:

```markdown
- Úkol 8 — GUI: před celou kopií karty, kterou historie zná, okno „Karty už jsou zálohované“ (Kopírovat znovu celé / Vynechat zálohované / Zrušit) se seznamem záloh a složkou, která vznikne (A001_2)
```

```powershell
git add windows docs
git commit -m "Windows GUI: ask before copying backed-up cards again" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Název karty z klipů

Spec §2.3, §5.1 (`naming.suggestFromClips`), §6.1 úkol 9. Návrh jen z názvů souborů (R10); napsaný název má vždy přednost; Start použije přesně to, co je vidět.

**Files:**
- Modify: `windows/src/Offload.App/MainViewModel.Queue.cs` (řádky 122–126)
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs` (řádky 192–198, 248–252, 315–319, 321–325)
- Modify: `windows/src/Offload.App/SetupPanel.xaml` (řádky 94–106, 173–176)
- Modify: `windows/src/Offload.App/SourceItemViewModel.cs` (řádky 7–12, 14–18, 21–25)
- Modify: `windows/src/Offload.Cli/CopyCommand.cs` (řádky 15–19, 45–49, 88–100, 163–173)
- Modify: `windows/src/Offload.Core/Desktop/AppSettings.cs` (řádky 3–6, 13–16, 39–43, 84–87, 110–113)
- Create: `windows/src/Offload.Core/Desktop/CardNaming.cs`
- Modify: `windows/src/Offload.Core/History/CardInspector.cs` (řádky 15–18, 38–41, 45–52)
- Modify: `windows/src/Offload.Core/OffloadJob.cs` (řádky 244–247)
- Create: `windows/src/Offload.Core/ReelNames.cs`
- Modify: `windows/tests/Offload.Core.Tests/HistoryJobTests.cs` (řádky 44–47)
- Create: `windows/tests/Offload.Core.Tests/ReelNamesTests.cs`

**Interfaces:**
- Consumes: `CardInsight` (úkol 6), `HistoryEntry.Project/Date/CardName` (úkol 4), `Insight(path)` a `history` v `MainViewModel` (úkol 7).
- Produces: `ReelNames.Match(string name) → (string Reel, string Prefix)?`, `ReelNames.FromClipName(string) → string?`; `record NameSuggestion(string Name, string Hint)`; `ReelSuggestion.FromClips(IReadOnlyList<Clip>) → (NameSuggestion? Suggestion, string? Note, IReadOnlyList<string> Reels)`, `ReelSuggestion.Next(IEnumerable<HistoryEntry>, string date, string? project, string today) → NameSuggestion?`; `CardInsight.Name`, `CardInsight.NameNote`; `CardNaming.Placeholder(bool suggestFromClips, CardInsight?, IReadOnlyList<HistoryEntry>?, string date, string today, string fallback) → (string Name, string? Hint)`; `NamingSettings { SuggestFromClips = true }`, `AppSettings.Naming` (`"naming"`); `MainViewModel.Placeholder(path)`, `SuggestCardNames`; `SourceItemViewModel.NameHint`; historie plní `reels`; CLI `--name auto`.

- [ ] **Step 1: Padající testy**

```csharp
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class ReelNamesTests
{
    [Theory]
    [InlineData("A001C003_210101_R1AB", "A001")]          // ARRI, Sony Venice, Canon Cinema EOS
    [InlineData("B012C0003", "B012")]                     // four-digit clip number
    [InlineData("A001_C003_0101AB_001", "A001")]          // RED
    [InlineData("A001_08251530_C001", "A001")]            // Blackmagic
    [InlineData("a001c003_210101_r1ab", "A001")]          // any case in, upper case out
    [InlineData("C0001", null)]                           // Sony XAVC
    [InlineData("A01C003", null)]
    [InlineData("AA001C003", null)]
    [InlineData("P1000123", null)]                        // Panasonic
    [InlineData("DJI_0001", null)]
    [InlineData("IMG_1234", null)]
    public void FromClipName(string name, string? reel) => Assert.Equal(reel, ReelNames.FromClipName(name));

    static Clip C(string name, bool cameraProxy = false) =>
        new() { Name = name, Path = name + ".mov", Format = ClipFormat.Video, Segments = [name + ".mov"], Bytes = 1, IsCameraProxy = cameraProxy };

    [Fact]
    public void OneReel_Suggested_WithHint()
    {
        var (s, note, reels) = ReelSuggestion.FromClips([C("A001C001_210101_R1AB"), C("A001C002_210101_R1AB"), C("README")]);
        Assert.Equal(new NameSuggestion("A001", "podle názvů klipů (A001C001…)"), s);
        Assert.Null(note);
        Assert.Equal(["A001"], reels);
    }

    [Fact]
    public void TwoReels_NoSuggestion_ButANote()
    {
        var (s, note, reels) = ReelSuggestion.FromClips([C("A002C001_x"), C("A001C001_x")]);
        Assert.Null(s);
        Assert.Equal("Na kartě je víc cívek: A001, A002", note);
        Assert.Equal(["A001", "A002"], reels);
    }

    [Fact]
    public void CameraProxies_Ignored()
    {
        var (s, _, _) = ReelSuggestion.FromClips([C("A001C001_x"), C("B007C001_x", cameraProxy: true)]);
        Assert.Equal("A001", s!.Name);
    }

    [Fact]
    public void NoReel_Nothing() => Assert.Equal((null, null), (ReelSuggestion.FromClips([C("C0001")]).Suggestion, ReelSuggestion.FromClips([C("C0001")]).Note));

    static HistoryEntry Copied(string cardName, string date, string? project = null)
    {
        var e = HistoryFixtures.Entry(HistoryFixtures.T0, cardName: cardName);
        e.Date = date;
        e.Project = project;
        return e;
    }

    [Fact]
    public void Next_OneCameraLetter()
    {
        HistoryEntry[] h = [Copied("A001", "2026-10-06"), Copied("A002", "2026-10-06"), Copied("A001_2", "2026-10-06"), Copied("A009", "2026-10-05")];
        Assert.Equal(new NameSuggestion("A003", "další v řadě po A002 (dnes)"), ReelSuggestion.Next(h, "2026-10-06", null, today: "2026-10-06"));
        Assert.Equal(new NameSuggestion("A010", "další v řadě po A009 (2026-10-05)"), ReelSuggestion.Next(h, "2026-10-05", null, today: "2026-10-06"));
    }

    [Fact]
    public void Next_TwoLetters_OtherDay_OtherProject_Nothing()
    {
        HistoryEntry[] h = [Copied("A001", "2026-10-06"), Copied("B001", "2026-10-06"), Copied("C005", "2026-10-07", project: "Film X")];
        Assert.Null(ReelSuggestion.Next(h, "2026-10-06", null, "2026-10-06"));
        Assert.Null(ReelSuggestion.Next(h, "2026-10-08", null, "2026-10-06"));
        Assert.Null(ReelSuggestion.Next(h, "2026-10-07", null, "2026-10-06"));
        Assert.Equal("C006", ReelSuggestion.Next(h, "2026-10-07", "Film X", "2026-10-06")!.Name);
    }

    [Fact]
    public void Placeholder_ClipsThenNextThenLabel()
    {
        var withReel = new CardInsight { Source = @"E:\", Name = new NameSuggestion("A001", "podle názvů klipů (A001C001…)") };
        var plain = new CardInsight { Source = @"E:\" };
        HistoryEntry[] h = [Copied("A004", "2026-10-06")];
        Assert.Equal(("A001", "podle názvů klipů (A001C001…)"), CardNaming.Placeholder(true, withReel, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
        Assert.Equal(("A005", "další v řadě po A004 (dnes)"), CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
        Assert.Equal(("EOS_DIGITAL", (string?)null), CardNaming.Placeholder(false, withReel, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
        Assert.Equal(("EOS_DIGITAL", (string?)null), CardNaming.Placeholder(true, null, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
        var known = plain with { History = new HistoryMatch(h[0], HistoryMatchKind.Identical, [], 0, [], 0) };
        Assert.Equal("EOS_DIGITAL", CardNaming.Placeholder(true, known, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL").Name);   // a known card is not "next"
        var twoReels = plain with { NameNote = "Na kartě je víc cívek: A001, A002" };
        Assert.Equal(("EOS_DIGITAL", "Na kartě je víc cívek: A001, A002"), CardNaming.Placeholder(true, twoReels, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
    }

    [Fact]
    public void Setting_DefaultOn_RoundTrip()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        Assert.True(SettingsStore.Load(path).Naming.SuggestFromClips);
        File.WriteAllText(path, """{"naming":{"suggestFromClips":false}}""");
        var s = SettingsStore.Load(path);
        Assert.False(s.Naming.SuggestFromClips);
        Assert.Null(s.Extra);   // "naming" is a known key now
        SettingsStore.Save(s, path);
        Assert.Contains("\"suggestFromClips\": false", File.ReadAllText(path));
    }
}
```

`windows/tests/Offload.Core.Tests/HistoryJobTests.cs`, řádky 44–47 — najít:

```csharp
        Assert.Equal(1, Assert.Single(e.Generations).Seq);
        Assert.Equal(2, store.ReadFileList(e)!.Count);
    }

```

nahradit:

```csharp
        Assert.Equal(1, Assert.Single(e.Generations).Seq);
        Assert.Equal(2, store.ReadFileList(e)!.Count);
        Assert.Equal(["A001"], e.Reels);   // spec 2.3: the reels of the clips
    }

```

- [ ] **Step 2: Spustit — musí selhat**

Run: `dotnet test --filter "FullyQualifiedName~ReelNamesTests|FullyQualifiedName~HistoryJobTests"` → build FAIL.

- [ ] **Step 3: Implementace**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Offload.Core.History;

namespace Offload.Core;

/// The reel ("A001") in a camera's clip name (spec 2.3) — from the name only, clips are never read before the copy (R10).
public static partial class ReelNames
{
    [GeneratedRegex(@"^(?<cam>[A-Z])(?<reel>\d{3})_\d{8}_C(?<clip>\d{3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Blackmagic();

    [GeneratedRegex(@"^(?<cam>[A-Z])(?<reel>\d{3})_C(?<clip>\d{3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Red();

    /// ARRI, Sony Venice, Canon Cinema EOS.
    [GeneratedRegex(@"^(?<cam>[A-Z])(?<reel>\d{3})C(?<clip>\d{3,4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArriSonyCanon();

    /// The reel in upper case and the part of the name that gave it ("A001C003"), or null (Sony XAVC "C0001", Panasonic,
    /// DJI, phones …). `name`: the clip name without extension.
    public static (string Reel, string Prefix)? Match(string name)
    {
        foreach (var r in new[] { Blackmagic(), Red(), ArriSonyCanon() })
            if (r.Match(name) is { Success: true } m)
                return ((m.Groups["cam"].Value + m.Groups["reel"].Value).ToUpperInvariant(), m.Value);
        return null;
    }

    public static string? FromClipName(string name) => Match(name)?.Reel;
}

/// A card folder name proposed instead of the volume label (spec 2.3). `Hint` is the grey line under the name field.
public sealed record NameSuggestion(string Name, string Hint);

public static partial class ReelSuggestion
{
    [GeneratedRegex(@"^[A-Z]\d{3}\z")]
    private static partial Regex ReelFolder();

    /// One reel among all recognised clips (camera proxies left out; an ARRIRAW sequence by its folder name, which is its
    /// clip name) → that reel. Several reels → no suggestion, but `Note` says so. `Reels`: every reel found, sorted.
    public static (NameSuggestion? Suggestion, string? Note, IReadOnlyList<string> Reels) FromClips(IReadOnlyList<Clip> clips)
    {
        var named = clips.Where(c => !c.IsCameraProxy)
            .Select(c => ReelNames.Match(c.Name))
            .Where(m => m != null)
            .Select(m => m!.Value)
            .ToList();
        var reels = named.Select(m => m.Reel).Distinct().Order(StringComparer.Ordinal).ToList();
        return reels.Count switch
        {
            0 => (null, null, reels),
            1 => (new NameSuggestion(reels[0], $"podle názvů klipů ({named[0].Prefix}…)"), null, reels),
            _ => (null, $"Na kartě je víc cívek: {string.Join(", ", reels)}", reels),
        };
    }

    /// "Next in line": history entries of the same date folder (and, from plan 8, project) named like "A001"; exactly
    /// one camera letter → the next number ("A001" → "A002"). Several letters → null (which camera is this card?).
    public static NameSuggestion? Next(IEnumerable<HistoryEntry> history, string date, string? project, string today)
    {
        var names = history.Where(e => e.Date == date && e.Project == project && ReelFolder().IsMatch(e.CardName))
            .Select(e => e.CardName).ToList();
        if (names.Count == 0 || names.Select(n => n[0]).Distinct().Count() != 1) return null;
        int max = names.Max(n => int.Parse(n[1..], NumberStyles.None, CultureInfo.InvariantCulture));
        if (max >= 999) return null;
        var letter = names[0][0];
        var when = date == today ? "dnes" : date;
        return new NameSuggestion($"{letter}{max + 1:000}", $"další v řadě po {letter}{max:000} ({when})");
    }
}
```

```csharp
using Offload.Core.History;

namespace Offload.Core.Desktop;

/// What the empty "Název složky" field shows (spec 2.3) — and what Start uses when nothing is typed.
public static class CardNaming
{
    /// The clips' reel first; a card history does not know gets "next in line" for the date folder; otherwise
    /// `fallback` (the volume label or folder name). `Hint`: the grey line under the field, or null.
    public static (string Name, string? Hint) Placeholder(bool suggestFromClips, CardInsight? insight,
        IReadOnlyList<HistoryEntry>? history, string date, string today, string fallback)
    {
        if (!suggestFromClips || insight == null) return (fallback, null);
        if (insight.Name is { } fromClips) return (fromClips.Name, fromClips.Hint);
        if (insight.NameNote is string note) return (fallback, note);
        if (insight.History == null && history != null && ReelSuggestion.Next(history, date, null, today) is { } next)
            return (next.Name, next.Hint);
        return (fallback, null);
    }
}
```

`windows/src/Offload.Core/History/CardInspector.cs`, řádky 15–18 — najít:

```csharp
    public string Shape { get; init; } = "";
    public HistoryMatch? History { get; init; }
    public string? Error { get; init; }
}
```

nahradit:

```csharp
    public string Shape { get; init; } = "";
    public HistoryMatch? History { get; init; }
    /// The folder name the clips suggest (spec 2.3), or null.
    public NameSuggestion? Name { get; init; }
    /// "Na kartě je víc cívek: A001, A002" when the clips disagree.
    public string? NameNote { get; init; }
    public string? Error { get; init; }
}
```

`windows/src/Offload.Core/History/CardInspector.cs`, řádky 38–41 — najít:

```csharp
                match = HistoryMatcher.Match(scan, fingerprint, candidate, list, filesystem);
            }
            return new CardInsight
            {
```

nahradit:

```csharp
                match = HistoryMatcher.Match(scan, fingerprint, candidate, list, filesystem);
            }
            var clips = ClipDetector.Detect(scan.Files);
            var (name, note, _) = ReelSuggestion.FromClips(clips);
            return new CardInsight
            {
```

`windows/src/Offload.Core/History/CardInspector.cs`, řádky 45–52 — najít:

```csharp
                TotalBytes = scan.TotalBytes,
                FileCount = scan.Files.Count,
                Clips = ClipDetector.Detect(scan.Files),
                Fingerprint = fingerprint,
                Shape = shape,
                History = match,
            };
        }
```

nahradit:

```csharp
                TotalBytes = scan.TotalBytes,
                FileCount = scan.Files.Count,
                Clips = clips,
                Fingerprint = fingerprint,
                Shape = shape,
                History = match,
                Name = name,
                NameNote = note,
            };
        }
```
`windows/src/Offload.Core/OffloadJob.cs`, řádky 244–247 — najít:

```csharp
                    },
                ],
            };
            var store = Config.HistoryFolder is string folder ? new HistoryStore(folder, log) : HistoryStore.Default(log);
```

nahradit:

```csharp
                    },
                ],
                Reels = ReelSuggestion.FromClips(ClipDetector.Detect(plan.Scan.Files)).Reels.ToList(),
            };
            var store = Config.HistoryFolder is string folder ? new HistoryStore(folder, log) : HistoryStore.Default(log);
```
`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 3–6 — najít:

```csharp

namespace Offload.Core.Desktop;

/// The app's settings — the Mac `AppSettings` keys (destRoots, verify, analyzeClips, proxy, tools);
```

nahradit:

```csharp

namespace Offload.Core.Desktop;

/// "naming" in settings.json (spec 5.1).
public sealed class NamingSettings
{
    /// Propose the card folder name from the clip names ("A001C003…" → "A001") or the next in line (spec 2.3).
    [JsonPropertyName("suggestFromClips")] public bool SuggestFromClips { get; set; } = true;
}

/// The app's settings — the Mac `AppSettings` keys (destRoots, verify, analyzeClips, proxy, tools);
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 13–16 — najít:

```csharp
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("tools")] public ExternalTools Tools { get; set; } = new();
    [JsonPropertyName("portNames")]
    public Dictionary<string, string> PortNames
```

nahradit:

```csharp
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("tools")] public ExternalTools Tools { get; set; } = new();
    [JsonPropertyName("naming")] public NamingSettings Naming { get; set; } = new();
    [JsonPropertyName("portNames")]
    public Dictionary<string, string> PortNames
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 39–43 — najít:

```csharp

    /// The top-level keys Load reads into properties; every other key goes to AppSettings.Extra.
    static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "destRoots", "verify", "analyzeClips", "proxy", "tools", "portNames" };

    static readonly JsonSerializerOptions Json = new()
```

nahradit:

```csharp

    /// The top-level keys Load reads into properties; every other key goes to AppSettings.Extra.
    static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "destRoots", "verify", "analyzeClips", "proxy", "tools", "naming", "portNames" };

    static readonly JsonSerializerOptions Json = new()
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 84–87 — najít:

```csharp
            Section("proxy", e => s.Proxy = e.Deserialize<ProxySettings>(Json) ?? new ProxySettings());
            Section("tools", e => s.Tools = e.Deserialize<ExternalTools>(Json) ?? new ExternalTools());
            Section("portNames", e =>
            {
```

nahradit:

```csharp
            Section("proxy", e => s.Proxy = e.Deserialize<ProxySettings>(Json) ?? new ProxySettings());
            Section("tools", e => s.Tools = e.Deserialize<ExternalTools>(Json) ?? new ExternalTools());
            Section("naming", e => s.Naming = e.Deserialize<NamingSettings>(Json) ?? new NamingSettings());
            Section("portNames", e =>
            {
```

`windows/src/Offload.Core/Desktop/AppSettings.cs`, řádky 110–113 — najít:

```csharp
        s.Proxy ??= new ProxySettings();
        s.Tools ??= new ExternalTools();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
```

nahradit:

```csharp
        s.Proxy ??= new ProxySettings();
        s.Tools ??= new ExternalTools();
        s.Naming ??= new NamingSettings();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
```

GUI — zástupný text a šedý řádek pod polem „Název složky“, volba v *Struktura a kontrola*, „další v řadě“ se přepočítá při změně data:

`windows/src/Offload.App/SourceItemViewModel.cs`, řádky 7–12 — najít:

```csharp

/// One row in Karty: a volume or a manual folder; the tick and the card name live in MainViewModel.
/// `defaultName`: the placeholder — the scanned volume label or the folder name (no disk access on the UI thread).
public sealed class SourceItemViewModel(MainViewModel owner, string path, string title, string detail, SourceKind kind, string defaultName) : ObservableObject
{
    public string Path => path;
```

nahradit:

```csharp

/// One row in Karty: a volume or a manual folder; the tick and the card name live in MainViewModel.
public sealed class SourceItemViewModel(MainViewModel owner, string path, string title, string detail, SourceKind kind) : ObservableObject
{
    public string Path => path;
```

`windows/src/Offload.App/SourceItemViewModel.cs`, řádky 14–18 — najít:

```csharp
    public string Detail => detail;
    public string Glyph => kind switch { SourceKind.Card => "\uE7F1", SourceKind.Drive => "\uEDA2", _ => "\uE8B7" };
    public string CardNamePlaceholder { get; } = defaultName;

    /// "Zálohováno … — ověřeno" (spec 2.1), or null for a card history does not know.
```

nahradit:

```csharp
    public string Detail => detail;
    public string Glyph => kind switch { SourceKind.Card => "\uE7F1", SourceKind.Drive => "\uEDA2", _ => "\uE8B7" };
    /// The name a start uses when none is typed: the clips' reel, the next in line, or the volume label / folder name.
    public string CardNamePlaceholder => owner.Placeholder(path).Name;
    /// "podle názvů klipů (A001C001…)", "další v řadě po A001 (dnes)", "Na kartě je víc cívek: …" — or null.
    public string? NameHint => owner.Placeholder(path).Hint;

    /// "Zálohováno … — ověřeno" (spec 2.1), or null for a card history does not know.
```

`windows/src/Offload.App/SourceItemViewModel.cs`, řádky 21–25 — najít:

```csharp

    /// A card inspection finished: the history line (and what depends on it) may have changed.
    internal void InsightChanged() => RaiseMany(nameof(HistoryText), nameof(HistoryTone));

    public bool IsSelected
```

nahradit:

```csharp

    /// A card inspection finished: the history line (and what depends on it) may have changed.
    internal void InsightChanged() => RaiseMany(nameof(HistoryText), nameof(HistoryTone), nameof(CardNamePlaceholder), nameof(NameHint));

    /// The date folder or the naming option changed: the suggested name may be another.
    internal void NamingChanged() => RaiseMany(nameof(CardNamePlaceholder), nameof(NameHint));

    public bool IsSelected
```
`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 192–198 — najít:

```csharp
            Sources.Add(new SourceItemViewModel(this, v.Path, v.Name,
                $"{Format.Bytes(v.Total - v.Free)} · {v.Format}{(v.LooksLikeCard ? "" : " · nevypadá jako karta")}",
                v.LooksLikeCard ? SourceKind.Card : SourceKind.Drive, DefaultCardName(v.Path)));
        foreach (var p in manualSources)
            Sources.Add(new SourceItemViewModel(this, p, Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p, p, SourceKind.Folder, DefaultCardName(p)));
        Raise(nameof(HasSources));
        SetupChanged?.Invoke();
```

nahradit:

```csharp
            Sources.Add(new SourceItemViewModel(this, v.Path, v.Name,
                $"{Format.Bytes(v.Total - v.Free)} · {v.Format}{(v.LooksLikeCard ? "" : " · nevypadá jako karta")}",
                v.LooksLikeCard ? SourceKind.Card : SourceKind.Drive));
        foreach (var p in manualSources)
            Sources.Add(new SourceItemViewModel(this, p, Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p, p, SourceKind.Folder));
        Raise(nameof(HasSources));
        SetupChanged?.Invoke();
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 248–252 — najít:

```csharp
    /// The card folder name a start would use now: the typed name, otherwise the placeholder.
    internal string PlannedName(string path) =>
        cardNames.GetValueOrDefault(path) is { } typed && typed.Trim().Length > 0 ? typed.Trim() : DefaultCardName(path);

    internal void SetSelected(string path, bool on)
```

nahradit:

```csharp
    /// The card folder name a start would use now: the typed name, otherwise the placeholder.
    internal string PlannedName(string path) =>
        cardNames.GetValueOrDefault(path) is { } typed && typed.Trim().Length > 0 ? typed.Trim() : Placeholder(path).Name;

    /// Spec 2.3: the suggestion from the clips or the next in line, else the volume label / folder name. Memory only.
    internal (string Name, string? Hint) Placeholder(string path) =>
        CardNaming.Placeholder(Settings.Naming.SuggestFromClips, Insight(path), history, DateString, Layout.Today(), DefaultCardName(path));

    void NamingChanged()
    {
        foreach (var s in Sources) s.NamingChanged();
    }

    internal void SetSelected(string path, bool on)
```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 315–319 — najít:

```csharp
    {
        get => useCustomDate;
        set { if (Set(ref useCustomDate, value)) Raise(nameof(PathPreview)); }
    }

```

nahradit:

```csharp
    {
        get => useCustomDate;
        set
        {
            if (!Set(ref useCustomDate, value)) return;
            Raise(nameof(PathPreview));
            NamingChanged();   // "next in line" counts within the date folder
        }
    }

```

`windows/src/Offload.App/MainViewModel.Setup.cs`, řádky 321–325 — najít:

```csharp
    {
        get => customDate;
        set { if (Set(ref customDate, value)) Raise(nameof(PathPreview)); }
    }

```

nahradit:

```csharp
    {
        get => customDate;
        set
        {
            if (!Set(ref customDate, value)) return;
            Raise(nameof(PathPreview));
            NamingChanged();
        }
    }

    public bool SuggestCardNames
    {
        get => Settings.Naming.SuggestFromClips;
        set
        {
            Settings.Naming.SuggestFromClips = value;
            SettingsChanged();
            Raise();
            NamingChanged();
        }
    }

```
`windows/src/Offload.App/MainViewModel.Queue.cs`, řádky 122–126 — najít:

```csharp
                continue;
            }
            var name = cardNames.GetValueOrDefault(src) is { Length: > 0 } n && n.Trim().Length > 0 ? n.Trim() : null;
            var config = new JobConfig(src, Settings.DestRoots.ToList())
            {
```

nahradit:

```csharp
                continue;
            }
            // Exactly what the row shows: the typed name, else a suggestion (spec 2.3); null = the volume label.
            var name = cardNames.GetValueOrDefault(src) is { Length: > 0 } n && n.Trim().Length > 0 ? n.Trim()
                : Placeholder(src).Name is var shown && shown != DefaultCardName(src) ? shown
                : null;
            var config = new JobConfig(src, Settings.DestRoots.ToList())
            {
```
`windows/src/Offload.App/SetupPanel.xaml`, řádky 94–106 — najít:

```xml
                                            </TextBlock.Style>
                                        </TextBlock>
                                        <DockPanel Margin="0,6,0,0" Visibility="{Binding IsSelected, Converter={StaticResource BoolToVisible}}">
                                            <TextBlock Text="Název složky" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0" />
                                            <Grid>
                                                <TextBox Text="{Binding CardName, UpdateSourceTrigger=PropertyChanged}" FontSize="12" />
                                                <TextBlock Text="{Binding CardNamePlaceholder}" Style="{StaticResource Caption}" Margin="12,0,0,0"
                                                           VerticalAlignment="Center" IsHitTestVisible="False"
                                                           Visibility="{Binding CardName, Converter={StaticResource EmptyToVisible}}" />
                                            </Grid>
                                        </DockPanel>
                                    </StackPanel>
                                </Grid>
```

nahradit:

```xml
                                            </TextBlock.Style>
                                        </TextBlock>
                                        <StackPanel Margin="0,6,0,0" Visibility="{Binding IsSelected, Converter={StaticResource BoolToVisible}}">
                                            <DockPanel>
                                                <TextBlock Text="Název složky" Style="{StaticResource Caption}" VerticalAlignment="Center" Margin="0,0,8,0" />
                                                <Grid>
                                                    <TextBox Text="{Binding CardName, UpdateSourceTrigger=PropertyChanged}" FontSize="12" />
                                                    <TextBlock Text="{Binding CardNamePlaceholder}" Style="{StaticResource Caption}" Margin="12,0,0,0"
                                                               VerticalAlignment="Center" IsHitTestVisible="False"
                                                               Visibility="{Binding CardName, Converter={StaticResource EmptyToVisible}}" />
                                                </Grid>
                                            </DockPanel>
                                            <TextBlock Text="{Binding NameHint}" Style="{StaticResource Caption}" FontSize="11" Margin="0,2,0,0"
                                                       AutomationProperties.AutomationId="CardNameHint"
                                                       Visibility="{Binding NameHint, Converter={StaticResource NullToCollapsed}}" />
                                        </StackPanel>
                                    </StackPanel>
                                </Grid>
```

`windows/src/Offload.App/SetupPanel.xaml`, řádky 173–176 — najít:

```xml
                           Visibility="{Binding Verify, Converter={StaticResource BoolToCollapsed}}" />
                <CheckBox Content="Analýza klipů a náhledy v reportu" IsChecked="{Binding AnalyzeClips}" />

                <!-- Proxy -->
```

nahradit:

```xml
                           Visibility="{Binding Verify, Converter={StaticResource BoolToCollapsed}}" />
                <CheckBox Content="Analýza klipů a náhledy v reportu" IsChecked="{Binding AnalyzeClips}" />
                <CheckBox Content="Navrhovat název karty podle klipů" IsChecked="{Binding SuggestCardNames}"
                          AutomationProperties.AutomationId="SuggestCardNames" />

                <!-- Proxy -->
```

CLI `--name auto` (návrh z klipů, jinak jmenovka; bez `--name` jako dnes):

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 15–19 — najít:

```csharp
        // "Není zadaný žádný cíl." instead.
        var dest = new Option<string[]>("--dest", "-d") { Description = "Destination root (repeat for each disk).", DefaultValueFactory = _ => [] };
        var name = new Option<string?>("--name") { Description = "Card folder name (default: volume label)." };
        var date = new Option<string?>("--date") { Description = "Date folder (default: today, YYYY-MM-DD)." };
        var chunkMb = new Option<int>("--chunk-mb") { Description = "Block size in MB.", DefaultValueFactory = _ => 16 };
```

nahradit:

```csharp
        // "Není zadaný žádný cíl." instead.
        var dest = new Option<string[]>("--dest", "-d") { Description = "Destination root (repeat for each disk).", DefaultValueFactory = _ => [] };
        var name = new Option<string?>("--name") { Description = "Card folder name (default: volume label); auto = the reel in the clip names (A001C003… → A001), else the volume label." };
        var date = new Option<string?>("--date") { Description = "Date folder (default: today, YYYY-MM-DD)." };
        var chunkMb = new Option<int>("--chunk-mb") { Description = "Block size in MB.", DefaultValueFactory = _ => 16 };
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 45–49 — najít:

```csharp
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), r.GetValue(dest)!.Select(CliPath.Full).ToList())
                {
                    CardName = r.GetValue(name),
                    Date = r.GetValue(date),
                    ChunkMB = r.GetValue(chunkMb),
```

nahradit:

```csharp
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), r.GetValue(dest)!.Select(CliPath.Full).ToList())
                {
                    CardName = r.GetValue(name) is string n && !n.Equals("auto", StringComparison.OrdinalIgnoreCase) ? n : null,
                    Date = r.GetValue(date),
                    ChunkMB = r.GetValue(chunkMb),
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 88–100 — najít:

```csharp
            // Only when proxies or clip analysis (thumbnails) are on — otherwise no LUT is used at all.
            if (LutOption.PlanLine(config.Proxy, config.AnalyzeClips) is string lutLine) Console.WriteLine(lutLine);
            return Run(config, r.GetValue(dryRun));
        });
        return cmd;
    }

    static int Run(JobConfig config, bool dryRun)
    {
        if (config.RecordHistory && Inspect(config.Source)?.History is { } known)
            Console.WriteLine(HistoryText.Line(known, fullPaths: true).Text);   // no question: a script has nobody to ask
        var job = new OffloadJob(config);
        OffloadPlan plan;
```

nahradit:

```csharp
            // Only when proxies or clip analysis (thumbnails) are on — otherwise no LUT is used at all.
            if (LutOption.PlanLine(config.Proxy, config.AnalyzeClips) is string lutLine) Console.WriteLine(lutLine);
            bool autoName = r.GetValue(name) is string a && a.Equals("auto", StringComparison.OrdinalIgnoreCase);
            return Run(config, r.GetValue(dryRun), autoName);
        });
        return cmd;
    }

    static int Run(JobConfig config, bool dryRun, bool autoName)
    {
        var insight = config.RecordHistory || autoName ? Inspect(config.Source, withHistory: config.RecordHistory) : null;
        if (insight?.History is { } known)
            Console.WriteLine(HistoryText.Line(known, fullPaths: true).Text);   // no question: a script has nobody to ask
        if (autoName)
        {
            // --name auto: the reel from the clip names, otherwise the volume label (as without --name).
            config.CardName = insight?.Name?.Name;
            if (insight?.NameNote is string note) Console.WriteLine(note);
        }
        var job = new OffloadJob(config);
        OffloadPlan plan;
```

`windows/src/Offload.Cli/CopyCommand.cs`, řádky 163–173 — najít:

```csharp
    }

    /// The card against history (spec 2.1 CLI). Never fails the copy: any problem only means no history line.
    internal static CardInsight? Inspect(string source)
    {
        try
        {
            var store = HistoryStore.Default(m => Console.Error.WriteLine(m));
            return CardInspector.Inspect(source, store.Load(), store, CancellationToken.None);
        }
        catch (Exception e)
```

nahradit:

```csharp
    }

    /// The card against history (spec 2.1 CLI) and its clip names (2.3). Never fails the copy: any problem only means
    /// no history line and no suggested name.
    internal static CardInsight? Inspect(string source, bool withHistory)
    {
        try
        {
            var store = withHistory ? HistoryStore.Default(m => Console.Error.WriteLine(m)) : null;
            return CardInspector.Inspect(source, store?.Load(), store, CancellationToken.None);
        }
        catch (Exception e)
```

- [ ] **Step 4: Testy + build**

Run: `dotnet test --filter "FullyQualifiedName~ReelNamesTests|FullyQualifiedName~CardInspectorTests|FullyQualifiedName~HistoryJobTests|FullyQualifiedName~SettingsStoreTests"` → PASS; `dotnet build` bez varování; `dotnet test` → zelená. Ruční ověření: úkol 19, bod 5.

- [ ] **Step 5: VYVOJ + commit**

Řádek nahoru do sekce plánu 6:

```markdown
- Úkol 9 — název karty z názvů klipů (ARRI/Sony Venice/Canon `A001C003…`, RED `A001_C003…`, Blackmagic `A001_08251530_C001`) nebo „další v řadě“ podle historie dne; šedý zástupný text a řádek pod polem, volba „Navrhovat název karty podle klipů“ (`naming.suggestFromClips`), CLI `--name auto`, cívky v historii (`reels`)
```

```powershell
git add windows docs
git commit -m "Windows: card name from clip names" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

Pokračování (úkoly 10–19): `windows/docs/superpowers/plans/2026-10-06-offload-windows-6-karty-2.md`.
