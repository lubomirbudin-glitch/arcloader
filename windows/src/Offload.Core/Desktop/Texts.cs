namespace Offload.Core.Desktop;

/// English texts with the right plural (1 card, 3 cards).
public static class Texts
{
    /// English plural: `one` for 1, `other` for everything else (0 files, 2 files).
    public static string Plural(long n, string one, string other) => n == 1 ? one : other;

    /// Kept for callers written for the old three-form (Czech) plural: `one` for 1, `few` for 2–4, `many` otherwise.
    public static string Plural(long n, string one, string few, string many) => n == 1 ? one : n is >= 2 and <= 4 ? few : many;

    /// "1 file", "3 files"; `other` defaults to `one` + "s".
    public static string N(long n, string one, string? other = null) => $"{n} {(n == 1 ? one : other ?? one + "s")}";

    public static string Cards(int n) => n == 0 ? "Select a card" : N(n, "card");

    public static string Destinations(int n) => N(n, "destination");

    public static string Files(int n, long bytes) => $"{N(n, "file")} · {Format.Bytes(bytes)}";

    public static string Clips(int n) => N(n, "clip");

    /// R5 (plan 10) "Safe to eject": copied everywhere, the verification may still run.
    public static string SafeToEject(int disks) =>
        $"All files are on {N(disks, "disk")} — the card is safe to eject. Format it only after verification.";

    /// R5 (plan 10) "Safe to format": verified everywhere.
    public static string SafeToFormat(int disks) =>
        $"All files are on {N(disks, "disk")} and verified — the card is safe to eject and format.";

    public static string Warnings(int n) => n == 0 ? "no warnings" : N(n, "warning");

    /// The verify checkbox names the checksum it reads back with (spec 3.5).
    public static string VerifyLabel(HashKind kinds) => $"Verify copies by reading back ({HashKinds.Label(kinds)})";

    public const string Md5Slower = "MD5 is slower — with fast cards (CFexpress) it may slow the copy down.";
}

/// One choice of "Checksum" (spec 3.5): xxHash64, or xxHash64 + MD5 — never MD5 alone (R16).
public sealed record HashChoice(HashKind Kind, string Label)
{
    public static readonly IReadOnlyList<HashChoice> All = [new(HashKind.Xxh64, "xxHash64 (fast)"), new(HashKind.Both, "xxHash64 + MD5")];

    public static HashChoice Of(HashKind kinds) => All.First(c => c.Kind == (HashKinds.Normalize(kinds).HasFlag(HashKind.Md5) ? HashKind.Both : HashKind.Xxh64));
}
