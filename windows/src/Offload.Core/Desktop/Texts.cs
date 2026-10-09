namespace Offload.Core.Desktop;

/// Czech texts with the right plural (1 karta, 3 karty, 5 karet).
public static class Texts
{
    public static string Plural(long n, string one, string few, string many) => n == 1 ? one : n is >= 2 and <= 4 ? few : many;

    public static string Cards(int n) => n == 0 ? "Vyberte kartu" : $"{n} {Plural(n, "karta", "karty", "karet")}";

    public static string Destinations(int n) => $"{n} {Plural(n, "cíl", "cíle", "cílů")}";

    public static string Files(int n, long bytes) => $"{n} {Plural(n, "soubor", "soubory", "souborů")} · {Format.Bytes(bytes)}";

    public static string Clips(int n) => $"{n} {Plural(n, "klip", "klipy", "klipů")}";

    /// R5 (plan 10) "Lze vyjmout": copied everywhere, the verification may still run.
    public static string SafeToEject(int disks) =>
        $"Všechny soubory jsou na {disks} {Plural(disks, "disku", "discích", "discích")} — kartu lze vyjmout. Formátovat až po ověření.";

    /// R5 (plan 10) "Lze formátovat": verified everywhere.
    public static string SafeToFormat(int disks) =>
        $"Všechny soubory jsou na {disks} {Plural(disks, "disku", "discích", "discích")} a ověřené — kartu lze vyjmout i formátovat.";

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
