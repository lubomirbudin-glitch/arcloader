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
    AppendUnavailable,
    AppendNoReport,
    AppendNoMhl,
    AppendDiverged,
    AppendConflict,
    AppendMissingFile,
    AppendFailedFile,
    AppendOtherCard,
    AppendMhlDamaged,
    AppendBusy,
    /// Not an error: the backup already has everything (the job ends "Hotovo").
    NothingNew,
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
        $"{filesystem} neumí uložit soubory od 4 GB ({path}, {Format.BytesCs(size)})";
    /// "Na cíli Zaloha (D:) není dost místa: potřeba 412 GB (karty 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB."
    public static string NoSpace(string where, Space.SpaceNeed need, long free) =>
        $"Na cíli {where} není dost místa: potřeba {Format.BytesCs(need.Total)} ({Parts(need, "karty")}), volno {Format.BytesCs(free)}.";
    /// CLI --dry-run: "Místo: D:\Zaloha potřeba 412 GB (karta 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB"
    public static string SpaceLine(string where, Space.SpaceNeed need, long free) =>
        $"Místo: {where} potřeba {Format.BytesCs(need.Total)} ({Parts(need, "karta")}), volno {Format.BytesCs(free)}";

    static string Parts(Space.SpaceNeed need, string cards)
    {
        var parts = new List<string>();
        if (need.Card > 0) parts.Add($"{cards} {Format.BytesCs(need.Card)}");
        if (need.Proxy > 0) parts.Add($"proxy ~{Format.BytesCs(need.Proxy)}");
        if (need.Report > 0) parts.Add($"report {Format.BytesCs(need.Report)}");
        return string.Join(", ", parts);
    }
    public static string LowSpace(string where, long left) =>
        $"Na cíli {where} zbude po offloadu jen {Format.BytesCs(left)} — rezerva pod 5 %, odhad proxy je přibližný.";
    public static string ProxiesSkippedForSpace(string where, long need, long free) =>
        $"Proxy se nevyrenderovaly: na {where} chybí místo (potřeba ~{Format.BytesCs(need)}, volno {Format.BytesCs(free)}).";
    public static string ReportNotWritten(string details) => $"Report se nepodařilo zapsat: {details}";
    public static string NoHashRecord(string dir) => $"Ve složce {dir} není ASC MHL ani seznam součtů — není s čím porovnat.";
    public static string NoFilesToRead(string path) => $"V {path} nejsou soubory ke čtení.";
    public const string CardVolumeUnreadable = "Karta se nedá přečíst.";
    public const string CardInspectFailed = "Kartu se nepodařilo prověřit.";
    public const string CardSwapped = "Karta se během čtení vyměnila.";
    public const string CardNameAutoNone = "Návrh názvu nenalezen, použije se jmenovka svazku.";
    public static string CardNameAuto(string name) => $"Název karty: {name} (podle klipů)";
    public static string CliProject(string name) => $"Projekt: {name} (výchozí hodnoty z nastavení aplikace, volby z příkazové řádky mají přednost)";
    public static string CliAppendProject(string name) => $"Nastavení z projektu: {name} (projekt zálohy zůstane podle dřívějšího reportu)";
    public static string CardUnreadable(string detail) => $"Kartu nejde přečíst: {detail}";

    // Append (spec 2.2)
    public static string AppendUnavailable(string cardPath) =>
        $"Dřívější záloha není dostupná: {cardPath} — připojte disk, nebo udělejte celou kopii.";
    public static string AppendNoReport(string cardPath) =>
        $"V {cardPath} chybí report dřívější zálohy (_REPORT\\offload_report.json) — doplnit nejde.";
    public static string AppendBadReport(string cardPath, string detail) =>
        $"Report dřívější zálohy v {cardPath} nejde přečíst ({detail}) — doplnit nejde.";
    public static string AppendNoMhl(string cardPath) =>
        $"V {cardPath} chybí ASC MHL (ascmhl\\ascmhl_chain.xml) — doplnit nejde.";
    public static string AppendDiverged(string details) =>
        $"Zálohy na cílech se liší ({details}) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.";
    /// Ruling I1: the numbered reports and the ASC MHL chain of one card folder disagree (an append stopped half way).
    public static string AppendReportsMismatch(string cardPath, int report, int mhl) =>
        $"V {cardPath} nesouhlasí reporty s ASC MHL (poslední report je generace {report}, poslední generace MHL {mhl}) — dřívější doplnění se nedokončilo. "
        + "Zkontrolujte zálohu přes Nástroje → Ověřit zálohu… a složku opravte ručně; doplnit nejde.";
    public static string AppendNotACardFolder(string path) =>
        $"{path} není složka karty v záloze (<cíl>\\<datum>\\<karta>) — doplnit nejde.";
    public static string AppendDifferentFolders(IEnumerable<string> cardPaths) =>
        $"Složky dřívější zálohy se jmenují různě ({string.Join(", ", cardPaths)}) — doplnit jde jen do složek se stejným datem a názvem karty.";
    public static string AppendConflict(IReadOnlyList<string> lines, string fullCopyFolder) =>
        $"Doplnění zastaveno — nic se nezkopírovalo. {lines.Count} {Desktop.Texts.Plural(lines.Count, "soubor se neshoduje", "soubory se neshodují", "souborů se neshoduje")} "
        + "se zálohou:\n  "
        + string.Join("\n  ", lines.Take(10)) + (lines.Count > 10 ? "\n  …" : "")
        + $"\nUdělejte celou kopii (vznikne {fullCopyFolder}).";
    public static string AppendMissingFile(string disk, string path) =>
        $"Na {disk} chybí soubor z dřívější zálohy: {path} — zkontrolujte zálohu přes Nástroje → Ověřit zálohu…";
    public static string AppendFailedFile(string path, string disk) =>
        $"{path} se při dřívější záloze na {disk} nezkopíroval správně — doplnit nejde, udělejte celou kopii.";
    public const string AppendOtherCard = "Na kartě není žádný soubor z dřívější zálohy — jde o jinou kartu? Udělejte celou kopii.";
    public static string AppendMhlDamaged(string cardPath) =>
        $"Historie ASC MHL v {cardPath} je poškozená — zkontrolujte ji přes Nástroje → Ověřit zálohu…; doplnit nejde.";
    /// One line of the AppendConflict list: a file on a destination that no report records (a crashed append leaves one).
    public static string AppendUnknownFile(string disk, string path) =>
        $"na {disk} je soubor {path}, který žádný report nezná (nedokončené doplnění?)";
    /// One line of the AppendConflict list: the temporary copy name of a new file is taken.
    public static string AppendPartNameTaken(string path, string part) =>
        $"{path} (dočasné jméno kopie {part} je jiný soubor zálohy nebo karty)";
    public static string HashListNotHealed(string cardPath, string error) =>
        $"Seznam kontrolních součtů (.xxh64) v {cardPath} nejde doplnit o chybějící řádky: {error}";
    public static string AppendBusy(string cardPath) =>
        $"Do této zálohy právě doplňuje jiná úloha ({cardPath}) — počkejte, až skončí.";
    public const string NothingNew = "Nic nového — záloha je úplná.";
    public static string UnverifiedEarlier(int n) =>
        $"{n} {Desktop.Texts.Plural(n, "soubor", "soubory", "souborů")} z dřívější zálohy {Desktop.Texts.Plural(n, "nebyl ověřen", "nebyly ověřeny", "nebylo ověřeno")} — doporučeno Ověřit zálohu.";
    public const string CardNotInHistory = "Karta není v historii — zadejte složky zálohy volbou --into.";

    // CLI
    public const string CliError = "Chyba: ";
    public static string UnknownProxyCodec(string? x) =>
        $"Neznámý kodek proxy „{x}“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ.";
    public static string LutNotFound(string path) => $"LUT nenalezen: {path}";
    public static string RuleLutNotFound(int rule, string name) => $"LUT z pravidla {rule} nenalezen: {name}";
    public static string BadLutRule(string spec) =>
        $"Pravidlo LUTu „{spec}“ nejde přečíst — použijte <podle>=<hodnota>:<lut>, např. camera=FX6:sony-slog3-sgamut3cine (podle: camera, serial, reel, folder; lut: vestavěný, none nebo cesta k .cube).";
    /// A proxy rendered without its custom LUT (the file was gone when stage 2 began).
    public static string ProxyWithoutLut(string file) => $"LUT nenalezen: {file} — proxy bez LUTu";
    public static string UnknownHash(string? x) => $"Neznámý kontrolní součet „{x}“ — použijte xxh64 nebo both (xxh64 + MD5).";

    // Day report (spec 3.2)
    public static string DayNotFound(string path) => $"Složka dne nenalezena: {path}";
    public static string NotADayFolder(string path) => $"Ve složce {path} není žádná karta s reportem — vyberte složku dne na cíli (<cíl>\\<datum>).";
    public static string DayReportFolderIsCard(string path) =>
        $"Ve složce dne {path} je karta „{Layout.ReportDirName}“ — denní report by se zapsal do ní, proto se nevytvoří.";
    public static string DayIsVolumeRoot(string path) =>
        $"{path} je kořen disku, ne složka dne — vyberte složku dne na cíli (<cíl>\\<datum>) nebo kartu v ní.";
    public static string DayReportBusy(string path) => $"Denní report ve složce {path} právě vytváří jiná úloha — zkuste to za chvíli.";
    public static string DayReportFailed(string detail) => $"Denní report se nepodařilo vytvořit: {detail}";
    public static string DayReportWritten(string path) => $"Denní report: {path}";
    public static string ProxyFolderNotFound(string path) => $"Složka pro proxy nenalezena: {path}";
    public const string ProxyFolderNoEffect = "Poznámka: --proxy-folder bez --proxy nic nedělá.";
    public const string Cancelling = "Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…";
    public static string NotPositiveNumber(string? s) => $"„{s}“ není kladné číslo.";
    public static string UnknownFallbackLut(string x, string ids) => $"Neznámý záložní LUT „{x}“. Vestavěné: {ids}";
    public static string UnknownLut(string x, string ids) => $"Neznámý LUT „{x}“. Použijte auto, none, cestu k .cube nebo jedno z: {ids}";
    public const string NoBuiltinLuts = "Vestavěné LUTy nenalezeny (chybí složka luts).";

    // Clip notes (report, job card)
    public const string NoVerifiedCopy = "žádná ověřená kopie";
    public static string ProxyRenamed(string name) => $"Proxy přejmenována na {name} (shoda jmen) — přelinkujte ji ručně.";
    public const string FfmpegNotFound = "ffmpeg nenalezen";
    public const string FfprobeNotFound = "ffprobe nenalezen";
    public const string UnknownFormat = "neznámý formát";
    public const string NoDecoderShort = "chybí dekodér";
    public static string NoDecoder(string format) => $"{format}: chybí dekodér";
}
