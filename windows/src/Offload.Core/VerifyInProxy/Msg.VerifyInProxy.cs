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
    /// Ruling I3: a destination where reads past the file cache are impossible was read back through it (as 0.5.x did).
    public static string VerifiedThroughCache(string disk, int files) =>
        $"Ověřeno čtením přes cache Windows: {disk}, {files} {Desktop.Texts.Plural(files, "soubor", "soubory", "souborů")} — svazek nebo soubor nedovolí čtení mimo cache, data mohla přijít z paměti místo z disku.";
}
