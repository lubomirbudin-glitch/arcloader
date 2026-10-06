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
        $"{filesystem} neumí uložit soubory od 4 GB ({path}, {Format.BytesCs(size)})";
    public static string NoSpace(string root, long need, long free) =>
        $"Na cíli {root} není dost místa: potřeba {Format.BytesCs(need)}, volno {Format.BytesCs(free)}.";
    public static string ReportNotWritten(string details) => $"Report se nepodařilo zapsat: {details}";
    public static string NoHashRecord(string dir) => $"Ve složce {dir} není ASC MHL ani seznam součtů — není s čím porovnat.";
    public static string NoFilesToRead(string path) => $"V {path} nejsou soubory ke čtení.";
    public static string CardUnreadable(string detail) => $"Kartu nejde přečíst: {detail}";

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
