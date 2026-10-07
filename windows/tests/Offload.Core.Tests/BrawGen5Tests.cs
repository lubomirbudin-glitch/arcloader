using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Offload.BrawDecode;
using Offload.Cli;

namespace Offload.Core.Tests;

/// Spec 4.2, R26: Blackmagic RAW Film Gen 4 decoded as Gen 5 (braw-decode --gen5) so the Gen 5 LUT fits — off by
/// default. The real conversion is checked only with a Gen 4 sample ([BrawSampleFact], manual test) — ověří až vzorek
/// .braw Gen 4.
[Collection("Console")]
public class BrawGen5Tests
{
    [Fact]
    public void Options_Gen5_ForInfoAndFrames()
    {
        Assert.True(Options.Parse(["--info", "--gen5", "--scale", "eighth", "a.braw"])!.Gen5);
        Assert.True(Options.Parse(["--gen5", "--start", "3", "--count", "1", "a.braw"])!.Gen5);
        Assert.False(Options.Parse(["a.braw"])!.Gen5);
        Assert.Contains("[--gen5]", Options.Usage);
    }

    [Fact]
    public void InfoJson_OriginalGenerationAfterTheColour()
    {
        var f = new ClipFacts(1, 2, 3, 4, 25, 1, "", "", "Blackmagic Design Film", "Blackmagic Design", 5, 4);
        Assert.EndsWith(""","gamma":"Blackmagic Design Film","gamut":"Blackmagic Design","colorScienceGen":5,"colorScienceGenOriginal":4}""" + "\n", InfoJson.Format(f));
        var info = BrawBackend.ToClipInfo(BrawBackend.ParseInfo(InfoJson.Format(f)));
        Assert.Equal((5, 4), (info.ColorScienceGen, info.ColorScienceGenOriginal));
    }

    /// A stand-in IBlackmagicRawClipProcessingAttributes: slot 3 reads the colour science generation, slot 4 sets it.
    static unsafe class FakeGen
    {
        public static int Gen;
        public static int SetResult;      // what SetClipAttribute returns
        public static bool Ignore;        // the SDK says OK but keeps the old value

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int Get(nint self, uint attribute, ComVariant* value)
        {
            if (attribute != FourCC.ClipAttributeColorScienceGen || Gen < 0) return Com.E_FAIL;
            *value = ComVariant.Create((ushort)Gen);
            return Com.S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int Set(nint self, uint attribute, ComVariant* value)
        {
            if (SetResult != Com.S_OK) return SetResult;
            if (attribute == FourCC.ClipAttributeColorScienceGen && value->VarType == VarEnum.VT_UI2 && !Ignore) Gen = value->GetRawDataRef<ushort>();
            return Com.S_OK;
        }

        public static nint Create(int gen, int setResult = Com.S_OK, bool ignore = false)
        {
            (Gen, SetResult, Ignore) = (gen, setResult, ignore);
            var vtbl = (nint*)NativeMemory.AllocZeroed(5, (nuint)sizeof(nint));
            vtbl[3] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)&Get;
            vtbl[4] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)&Set;
            var obj = (nint*)NativeMemory.Alloc((nuint)sizeof(nint));
            *obj = (nint)vtbl;
            return (nint)obj;
        }

        public static void Free(nint obj)
        {
            NativeMemory.Free(*(void**)obj);
            NativeMemory.Free((void*)obj);
        }
    }

    static (bool Ok, int? Original, int Now, string Stderr) ToGen5(int gen, int setResult = Com.S_OK, bool ignore = false)
    {
        nint obj = FakeGen.Create(gen, setResult, ignore);
        try
        {
            using var stderr = new StringWriter();
            bool ok = Decoder.ToGen5(new BrawClipProcessingAttributes(obj), stderr, out var original);
            return (ok, original, FakeGen.Gen, stderr.ToString());
        }
        finally
        {
            FakeGen.Free(obj);
        }
    }

    [Fact]
    public void ToGen5_Gen4_IsSetTo5_AndReadBack()
    {
        var r = ToGen5(4);
        Assert.Equal((true, (int?)4, 5, ""), r);
    }

    [Fact]
    public void ToGen5_AlreadyGen5_NothingToDo_NoWarning()
    {
        Assert.Equal((false, (int?)5, 5, ""), ToGen5(5));
    }

    // Spec 4.2: when the SDK does not take it — a warning on stderr, decoded as Gen 4 (no LUT, as before).
    [Fact]
    public void ToGen5_Refused_OrIgnored_IsAWarning()
    {
        var refused = ToGen5(4, setResult: Com.E_FAIL);
        Assert.False(refused.Ok);
        Assert.Equal("braw-decode: --gen5: colour science Gen 5 not accepted (0x80004005, reads 4) — decoding as Gen 4" + Environment.NewLine, refused.Stderr);
        var ignored = ToGen5(4, ignore: true);
        Assert.False(ignored.Ok);
        Assert.Contains("(0x00000000, reads 4) — decoding as Gen 4", ignored.Stderr);
        var unknown = ToGen5(-1);
        Assert.False(unknown.Ok);
        Assert.Equal("braw-decode: --gen5: colour science generation unknown — decoding as recorded" + Environment.NewLine, unknown.Stderr);
    }

    // The LUT rule: an effective Gen 5 that was Gen 4 gets the BMD Gen 5 LUT, with the reason; without the conversion
    // Gen 4 stays without a LUT (as before).
    [Fact]
    public void LutResolver_Gen4_WithAndWithoutConversion()
    {
        var cat = LutCatalog.Load(LutCatalog.DefaultFolder);
        var clip = new Clip { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"], Bytes = 1 };
        var s = new ProxySettings { Enabled = true, LutMode = LutMode.Auto };
        var converted = LutResolver.Resolve(clip, new ClipInfo { Gamma = "Blackmagic Design Film", ColorScienceGen = 5, ColorScienceGenOriginal = 4 }, @"X:\A001.braw", s, cat);
        Assert.Equal(("auto", "bmd-film-gen5", "Blackmagic RAW: Film Gen 4 → Gen 5 (převod SDK)"), (converted.Source, converted.Id, converted.Reason));
        var plain = LutResolver.Resolve(clip, new ClipInfo { Gamma = "Blackmagic Design Film", ColorScienceGen = 4 }, @"X:\A001.braw", s, cat);
        Assert.Equal(("none", "Blackmagic RAW: Blackmagic Design Film Gen 4 (bez vestavěného LUTu)"), (plain.Source, plain.Reason));
    }

    // M11 (a): the conversion was switched on but the clip still reads Gen 4 without the original (the SDK refused,
    // or an older braw-decode) — the reason says so, instead of looking as if nobody asked.
    [Fact]
    public void LutResolver_Gen4_ConversionOnButNotDone_SaysSo()
    {
        var cat = LutCatalog.Load(LutCatalog.DefaultFolder);
        var clip = new Clip { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"], Bytes = 1 };
        var on = new ProxySettings { Enabled = true, LutMode = LutMode.Auto, BrawGen4ToGen5 = true };
        var failed = LutResolver.Resolve(clip, new ClipInfo { Gamma = "Blackmagic Design Film", ColorScienceGen = 4 }, @"X:\A001.braw", on, cat);
        Assert.Equal(("none", "Blackmagic RAW: Blackmagic Design Film Gen 4 (bez vestavěného LUTu) — převod na Gen 5 se nepodařil"), (failed.Source, failed.Reason));
        // A Gen 5 clip with the switch on: nothing to convert, nothing to say.
        var gen5 = LutResolver.Resolve(clip, new ClipInfo { Gamma = "Blackmagic Design Film", ColorScienceGen = 5 }, @"X:\A001.braw", on, cat);
        Assert.Equal(("auto", "Blackmagic RAW: Blackmagic Design Film Gen 5"), (gen5.Source, gen5.Reason));
    }

    /// A stand-in braw-decode that writes its arguments to args.txt and answers --info with a converted Gen 4 clip.
    static string RecordingDecoder(string dir)
    {
        var path = Path.Combine(dir, "braw-decode.cmd");
        File.WriteAllText(path, """
            @echo off
            >>"%~dp0args.txt" echo %*
            echo {"width":320,"height":180,"sourceWidth":320,"sourceHeight":180,"fps":25,"frames":1,"timecode":"","camera":"","gamma":"Blackmagic Design Film","colorScienceGen":5,"colorScienceGenOriginal":4}
            exit /b 0

            """.ReplaceLineEndings("\r\n"));
        File.WriteAllText(Path.Combine(dir, "BlackmagicRawAPI.dll"), "x");   // the backend is available
        return path;
    }

    // proxy.brawGen4ToGen5 → the renderer's BRAW backend passes --gen5 (for --info and for the frames); off → it does not.
    [Fact]
    public void Renderer_PassesGen5_OnlyWhenSwitchedOn()
    {
        using var dir = new TempDir();
        var tools = new ExternalTools { BrawDecoder = RecordingDecoder(dir.Path), NoSearch = true };
        var clip = new Clip { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"], Bytes = 1 };
        var card = dir.Sub("card");

        using (var on = new ProxyRenderer(new ProxySettings { BrawGen4ToGen5 = true }, tools))
        {
            var info = on.Probe(clip, card);
            Assert.Equal((5, 4), (info.ColorScienceGen, info.ColorScienceGenOriginal));
        }
        using (var off = new ProxyRenderer(new ProxySettings(), tools)) off.Probe(clip, card);

        var lines = File.ReadAllLines(Path.Combine(dir.Path, "args.txt"));
        Assert.StartsWith("--info --gen5 --scale eighth ", lines[0]);
        Assert.StartsWith("--info --scale eighth ", lines[1]);
    }

    /// An older braw-decode (pinned by the user) without --gen5: usage, exit 2, whenever --gen5 is on the line.
    static string OldDecoder(string dir)
    {
        var path = Path.Combine(dir, "braw-decode.cmd");
        File.WriteAllText(path, """
            @echo off
            >>"%~dp0args.txt" echo %*
            echo %* | findstr /c:"--gen5" >nul && (echo usage: braw-decode [--info] clip.braw 1>&2& exit /b 2)
            echo {"width":320,"height":180,"sourceWidth":320,"sourceHeight":180,"fps":25,"frames":1,"timecode":"","camera":"","gamma":"Blackmagic Design Film","colorScienceGen":4}
            exit /b 0

            """.ReplaceLineEndings("\r\n"));
        File.WriteAllText(Path.Combine(dir, "BlackmagicRawAPI.dll"), "x");
        return path;
    }

    // M11 (b): a decoder that rejects --gen5 (exit 2) does not fail every BRAW clip — once, the backend warns in the
    // job's log and goes on without --gen5 (for this clip, the later ones and their frames).
    [Fact]
    public void Backend_OldDecoderWithoutGen5_RetriesWithoutIt_WarnsOnce()
    {
        using var dir = new TempDir();
        var tools = new ExternalTools { BrawDecoder = OldDecoder(dir.Path), NoSearch = true };
        var logs = new List<string>();
        var b = new BrawBackend(_ => "x", gen5: true) { Log = logs.Add };
        var path = Path.Combine(dir.Sub("card"), "A001.braw");

        var info = b.Probe(path, tools, dir.Path);
        Assert.Equal((4, (int?)null), (info.ColorScienceGen, info.ColorScienceGenOriginal));
        b.Probe(path, tools, dir.Path);
        var source = b.Source(path, info, new ProxySettings(), new RawLook(true, null), tools, dir.Path, null);
        Assert.DoesNotContain("--gen5", source.FeederArgs!);

        var lines = File.ReadAllLines(Path.Combine(dir.Path, "args.txt"));
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("--info --gen5 --scale eighth ", lines[0]);
        Assert.All(lines[1..], l => Assert.DoesNotContain("--gen5", l));
        Assert.Equal(["Blackmagic RAW: braw-decode nezná --gen5 (starší verze) — Film Gen 4 se dekóduje bez převodu na Gen 5"], logs);
    }

    // The renderer hands its log to the Gen 5 backend it makes (the warning above reaches the job's log).
    [Fact]
    public void Renderer_Gen5Backend_WarnsInTheJobLog()
    {
        using var dir = new TempDir();
        var tools = new ExternalTools { BrawDecoder = OldDecoder(dir.Path), NoSearch = true };
        var clip = new Clip { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"], Bytes = 1 };
        var logs = new List<string>();
        using var r = new ProxyRenderer(new ProxySettings { BrawGen4ToGen5 = true }, tools) { Log = logs.Add };
        Assert.Equal(4, r.Probe(clip, dir.Sub("card")).ColorScienceGen);
        Assert.Single(logs, l => l.Contains("nezná --gen5", StringComparison.Ordinal));
    }

    [Fact]
    public void Setting_OffByDefault_AbsentWhileOff_CliOption()
    {
        Assert.False(new ProxySettings().BrawGen4ToGen5);
        Assert.DoesNotContain("brawGen4ToGen5", System.Text.Json.JsonSerializer.Serialize(new ProxySettings()));
        Assert.Contains("\"brawGen4ToGen5\":true", System.Text.Json.JsonSerializer.Serialize(new ProxySettings { BrawGen4ToGen5 = true }));

        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "A001.braw"), 100, 1);
        JobConfig? seen = null;
        var old = CopyCommand.JobFactory;
        var (o, e) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        CopyCommand.JobFactory = c => new OffloadJob(seen = c, AppendFixtures.Options());
        try { Assert.Equal(0, CopyCommand.Create().Parse([card, "-d", dir.Sub("d1"), "--dry-run", "--no-history", "--braw-gen5"]).Invoke()); }
        finally
        {
            CopyCommand.JobFactory = old;
            Console.SetOut(o);
            Console.SetError(e);
        }
        Assert.True(seen!.Proxy.BrawGen4ToGen5);
    }

    // --- with the real library (DaVinci Resolve's BlackmagicRawAPI.dll on this PC), no clip needed ---

    // --gen5 changes nothing before the clip is open: a missing clip is still exit 4 naming the library, no warning.
    [BrawLibraryFact]
    public void Library_Gen5_MissingClip_StillExit4()
    {
        foreach (var args in new[] { new[] { "--info", "--gen5", @"C:\nonexistent\A001.braw" }, new[] { "--gen5", "--scale", "eighth", "--count", "1", @"C:\nonexistent\A001.braw" } })
        {
            var r = BrawDecodeTests.Run(args);
            Assert.Equal(4, r.Code);
            Assert.Equal($@"braw-decode: cannot open C:\nonexistent\A001.braw (0x80004005; library {BrawLibraryFactAttribute.Library})", r.Stderr.TrimEnd());
            Assert.Empty(r.Stdout);
        }
    }

    // The published exe takes --gen5 too (the trimmed build must keep the new path).
    [BrawLibraryFact]
    public void Library_Exe_Gen5_MissingClip_StillExit4()
    {
        var r = BrawDecodeTests.RunExe("--info", "--gen5", @"C:\nonexistent\A001.braw");
        Assert.Equal(4, r.Code);
        Assert.Equal($@"braw-decode: cannot open C:\nonexistent\A001.braw (0x80004005; library {BrawLibraryFactAttribute.Library})", r.Stderr.TrimEnd());
    }
}
