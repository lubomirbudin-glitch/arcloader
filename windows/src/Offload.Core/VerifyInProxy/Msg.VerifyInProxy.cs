namespace Offload.Core;

/// Plan 10 (verify-in-proxy): the Czech texts of the report, the job card and the log.
public static partial class Msg
{
    // Why a destination is verified the classic way (report, per destination).
    public const string ClassicBufferedWrites = "svazek nepřijal zápis mimo cache — ověřeno zpětným čtením";
    public const string ClassicNetwork = "síťový cíl — ověřeno zpětným čtením";
    public const string ClassicCacheCheck = "kontrola čtení mimo cache neprošla — ověřeno zpětným čtením";
    public static string ClassicCacheCheckFailed(string detail) => $"kontrolu čtení mimo cache nejde provést ({detail}) — ověřeno zpětným čtením";
    public const string ClassicCompressedOrEncrypted = "složka cíle je komprimovaná nebo šifrovaná (NTFS) — ověřeno zpětným čtením";
    public const string ClassicSetting = "ověření během proxy je vypnuté — ověřeno zpětným čtením";
    public const string ClassicSecondary = "další cíl — ověřeno čtením na pozadí";

    // Spec 10 notes (report "verifyNotes", the job's warning).
    public static string UnstableRead(string path, string disk, IReadOnlyList<int> blocks) =>
        $"Nestabilní čtení: {path} na {disk} (blok {string.Join(", ", blocks)}) dal při jednom čtení jiná data — při dalším souhlasil s kartou.";
    public static string SameBlockWrong(string path, int block, string disks, string card) =>
        $"{path}: blok {block} nesouhlasí stejně na {disks} — {card}";
    public static string SameBlockWrongMore(string path, int blocks) =>
        $"{path}: dalších {blocks} {Desktop.Texts.Plural(blocks, "blok nesouhlasí", "bloky nesouhlasí", "bloků nesouhlasí")} stejně na více cílech (karta se pro ně znovu nečetla).";
    public const string CardNotReadAgain = "kartu se nepodařilo přečíst znovu (už není ve čtečce?) — vložte ji a spusťte Zkopírovat znovu.";
    public const string CardGivesRecorded = "karta teď dává zaznamenaná data: vada vznikla při zápisu na disky.";
    public const string CardGivesWhatDisksHave = "karta teď dává stejná data jako disky: podezření na chybné čtení karty při kopii.";
    public const string CardGivesOther = "karta teď dává jiná data než při kopii i než disky: podezření na vadnou kartu nebo čtečku.";

    /// The job's warning when the verified generation could not be written into a card folder (it stays uncommitted:
    /// "Dokončit přerušenou zálohu", an append, finishes it — R6, ruling I5; a repair refuses an unfinished backup).
    public static string VerifyNotCommitted(string detail) =>
        $"Výsledek ověření se nepodařilo zapsat ({detail}) — kartu zatím neformátujte; zálohu dokončí „Dokončit přerušenou zálohu“ (Doplnit, offload append).";

    // Fix round 1, item 2b: a record of the job that the history lock kept out.
    public const string HistoryDelayed = "Historie karet je právě zaneprázdněná — záznam této zálohy se zapíše, jakmile to půjde (aplikaci zatím nezavírejte).";
    public const string HistoryNotWritten = "Záznam této zálohy se do historie karet nepodařilo zapsat (historie byla dlouho zaneprázdněná) — záloha na discích je v pořádku, jen ji karta při dalším vložení nepozná.";

    // R6 (resume).
    public static string ResumeDiverged(string cardPath, int generation) =>
        $"{cardPath}: nedokončená záloha (generace {generation}) nesouhlasí s ostatními cíli — zkontrolujte ji ručně.";
    /// Ruling I5: the repair points to the tick the card gets on insert ("Dokončit přerušenou zálohu", even with no new
    /// file) and to its CLI form.
    public const string RepairUnfinished = "Záloha nebyla dokončena (aplikace skončila během kopie nebo ověření) — dokončete ji nejdřív volbou „Dokončit přerušenou zálohu“ u karty (offload append).";

    /// A card folder gone before the verification results were written into it (an unplugged disk) — never made anew.
    public const string CardFolderGone = "složka karty na cíli zmizela (odpojený disk?)";

    /// Spec 7: "2 klipy mají vadu obsahu — před formátováním je zkontrolujte (kopie souhlasí s kartou)."
    public static string ContentWarnings(int n) =>
        $"{n} {Desktop.Texts.Plural(n, "klip má", "klipy mají", "klipů má")} vadu obsahu — před formátováním {Desktop.Texts.Plural(n, "ho", "je", "je")} zkontrolujte (kopie souhlasí s kartou).";
    /// Ruling I3: a destination where reads past the file cache are impossible was read back through it (as 0.5.x did).
    public static string VerifiedThroughCache(string disk, int files) =>
        $"Ověřeno čtením přes cache Windows: {disk}, {files} {Desktop.Texts.Plural(files, "soubor", "soubory", "souborů")} — svazek nebo soubor nedovolí čtení mimo cache, data mohla přijít z paměti místo z disku.";
}
