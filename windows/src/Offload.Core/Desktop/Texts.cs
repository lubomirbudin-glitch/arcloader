namespace Offload.Core.Desktop;

/// Czech texts with the right plural (1 karta, 3 karty, 5 karet).
public static class Texts
{
    public static string Plural(long n, string one, string few, string many) => n == 1 ? one : n is >= 2 and <= 4 ? few : many;

    public static string Cards(int n) => n == 0 ? "Vyberte kartu" : $"{n} {Plural(n, "karta", "karty", "karet")}";

    public static string Destinations(int n) => $"{n} {Plural(n, "cíl", "cíle", "cílů")}";

    public static string Files(int n, long bytes) => $"{n} {Plural(n, "soubor", "soubory", "souborů")} · {Format.Bytes(bytes)}";

    public static string Clips(int n) => $"{n} {Plural(n, "klip", "klipy", "klipů")}";

    public static string SafeToEject(int disks) =>
        $"Všechny soubory jsou na {disks} {Plural(disks, "disku", "discích", "discích")} a ověřené — kartu lze vysunout.";

    public static string Failed(int n) =>
        $"{n} {Plural(n, "soubor selhal", "soubory selhaly", "souborů selhalo")} — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.";

    public static string Warnings(int n) => n == 0 ? "bez varování" : $"{n} varování";
}
