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
}
