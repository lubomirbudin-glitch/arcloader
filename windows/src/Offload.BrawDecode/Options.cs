using System.Globalization;

namespace Offload.BrawDecode;

/// Decode scale, as the SDK's BlackmagicRawResolutionScale four-character codes.
internal enum Scale : uint
{
    Full = 0x66756C6C,    // 'full'
    Half = 0x68616C66,    // 'half'
    Quarter = 0x71727472, // 'qrtr'
    Eighth = 0x65697468,  // 'eith'
}

/// Command line, the same as the Mac braw-decode:
///   braw-decode --info [--gen5] [--scale S] clip.braw
///   braw-decode [--gen5] [--scale S] [--start N] [--count N] clip.braw
/// `--gen5` (spec 4.2): a Blackmagic Design Film Gen 4 clip is decoded with colour science Gen 5 (and --info reports the
/// effective values plus "colorScienceGenOriginal").
internal sealed record Options(bool Info, Scale Scale, ulong Start, ulong? Count, string Path)
{
    public const string Usage = "usage: braw-decode [--info] [--gen5] [--scale full|half|quarter|eighth] [--start N] [--count N] clip.braw";

    public bool Gen5 { get; init; }

    /// null = bad command line (print Usage, exit 2). Stricter than the Mac (which falls back to "full" / 0):
    /// Offload never passes anything else, so a typo should fail loudly.
    public static Options? Parse(IReadOnlyList<string> args)
    {
        bool info = false, gen5 = false;
        var scale = Scale.Full;
        ulong start = 0;
        ulong? count = null;
        string? path = null;
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            string? Value() => i + 1 < args.Count ? args[++i] : null;
            switch (a)
            {
                case "--info":
                    info = true;
                    break;
                case "--gen5":
                    gen5 = true;
                    break;
                case "--scale":
                    var s = Value();
                    if (s == null || !TryScale(s, out scale)) return null;
                    break;
                case "--start":
                    if (!ulong.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out start)) return null;
                    break;
                case "--count":
                    if (!ulong.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
                    count = n;
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal)) return null;
                    path = a;
                    break;
            }
        }
        return path == null ? null : new Options(info, scale, start, count, path) { Gen5 = gen5 };
    }

    static bool TryScale(string s, out Scale scale)
    {
        scale = s switch { "full" => Scale.Full, "half" => Scale.Half, "quarter" => Scale.Quarter, "eighth" => Scale.Eighth, _ => 0 };
        return scale != 0;
    }

    /// Frames [first, end) to decode, clamped to the clip.
    public (ulong First, ulong End) Range(ulong frames) =>
        (Start, Count is ulong c ? Math.Min(frames, Start + Math.Min(c, ulong.MaxValue - Start)) : frames);
}
