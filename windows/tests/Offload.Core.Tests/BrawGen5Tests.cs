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

    const string Film = "Blackmagic Design Film";

    /// A stand-in IBlackmagicRawClipProcessingAttributes: slots 1/2 count references, slot 3 reads the colour science
    /// generation and the gamma, slot 4 sets the generation. And a stand-in IBlackmagicRawClip whose slot 12
    /// (CloneClipProcessingAttributes) hands that object out with one reference, as the SDK's clone does.
    static unsafe class FakeGen
    {
        public static int Gen;
        public static string? Gamma;
        public static int SetResult;      // what SetClipAttribute returns
        public static bool Ignore;        // the SDK says OK but keeps the old value
        public static int RefCount;
        public static int CloneResult;    // what CloneClipProcessingAttributes returns
        static nint attributes;           // what the fake clip's clone hands out

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static uint AddRef(nint self) => (uint)Interlocked.Increment(ref RefCount);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static uint Release(nint self) => (uint)Interlocked.Decrement(ref RefCount);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int Get(nint self, uint attribute, ComVariant* value)
        {
            if (attribute == FourCC.ClipAttributeColorScienceGen && Gen >= 0)
            {
                *value = ComVariant.Create((ushort)Gen);
                return Com.S_OK;
            }
            if (attribute == FourCC.ClipAttributeGamma && Gamma != null)
            {
                *value = ComVariant.Create(Gamma);
                return Com.S_OK;
            }
            return Com.E_FAIL;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int Set(nint self, uint attribute, ComVariant* value)
        {
            if (SetResult != Com.S_OK) return SetResult;
            if (attribute == FourCC.ClipAttributeColorScienceGen && value->VarType == VarEnum.VT_UI2 && !Ignore) Gen = value->GetRawDataRef<ushort>();
            return Com.S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int Clone(nint self, nint* result)
        {
            RefCount = 1;
            *result = attributes;
            return CloneResult;
        }

        static nint Object(int slots, params (int Slot, nint Fn)[] fns)
        {
            var vtbl = (nint*)NativeMemory.AllocZeroed((nuint)slots, (nuint)sizeof(nint));
            foreach (var (slot, fn) in fns) vtbl[slot] = fn;
            var obj = (nint*)NativeMemory.Alloc((nuint)sizeof(nint));
            *obj = (nint)vtbl;
            return (nint)obj;
        }

        public static nint Create(int gen, int setResult = Com.S_OK, bool ignore = false, string? gamma = Film)
        {
            (Gen, SetResult, Ignore, Gamma, RefCount) = (gen, setResult, ignore, gamma, 1);
            return Object(5,
                (1, (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef),
                (2, (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release),
                (3, (nint)(delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)&Get),
                (4, (nint)(delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)&Set));
        }

        /// A clip whose clone is `attrs` (RefCount 0 until cloned).
        public static nint CreateClip(nint attrs, int cloneResult)
        {
            (attributes, CloneResult, RefCount) = (attrs, cloneResult, 0);
            return Object(13, (12, (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&Clone));
        }

        public static void Free(nint obj)
        {
            NativeMemory.Free(*(void**)obj);
            NativeMemory.Free((void*)obj);
        }
    }

    static (bool Ok, int? Original, int Now, string Stderr) ToGen5(int gen, int setResult = Com.S_OK, bool ignore = false, string? gamma = Film)
    {
        nint obj = FakeGen.Create(gen, setResult, ignore, gamma);
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

    // Fix round 1: only Blackmagic Design Film is converted (what the spec and the checkbox promise). Video / Extended
    // Video Gen 4 are display-ready: left alone, set never called, no warning. A gamma that cannot be read is a warning.
    [Fact]
    public void ToGen5_OnlyFilmGamma()
    {
        Assert.Equal((false, (int?)4, 4, ""), ToGen5(4, gamma: "Blackmagic Design Video"));
        Assert.Equal((false, (int?)4, 4, ""), ToGen5(4, gamma: "Blackmagic Design Extended Video"));
        Assert.Equal((true, (int?)4, 5, ""), ToGen5(4, gamma: "blackmagic design film"));
        Assert.Equal((false, (int?)4, 4, "braw-decode: --gen5: gamma unknown — decoding as recorded" + Environment.NewLine), ToGen5(4, gamma: null));
    }

    /// Decoder.Gen5Attributes on the fake clip: whether the clone is handed back for the jobs, the recorded generation,
    /// the clone's reference count after the caller (Run's finally) released a used one, and stderr.
    static (bool Used, int? Original, int RefCount, string Stderr) Gen5Attributes(int gen, int setResult = Com.S_OK, int cloneResult = Com.S_OK, string? gamma = Film)
    {
        nint attrs = FakeGen.Create(gen, setResult, gamma: gamma);
        nint clip = FakeGen.CreateClip(attrs, cloneResult);
        try
        {
            using var stderr = new StringWriter();
            var a = Decoder.Gen5Attributes(new BrawClip(clip), stderr, out var original);
            bool used = a.Ptr != 0;
            if (used)
            {
                Assert.Equal(attrs, a.Ptr);
                Assert.Equal(1, FakeGen.RefCount);   // still alive for the decode jobs
                Com.Release(a.Ptr);                  // what Run's finally does
            }
            return (used, original, FakeGen.RefCount, stderr.ToString());
        }
        finally
        {
            FakeGen.Free(clip);
            FakeGen.Free(attrs);
        }
    }

    // Fix round 1: every path gives the clone's reference back — the used one through the caller, every other one at once.
    [Fact]
    public void Gen5Attributes_EveryPath_ReleasesTheClone()
    {
        Assert.Equal((true, (int?)4, 0, ""), Gen5Attributes(4));
        var refused = Gen5Attributes(4, setResult: Com.E_FAIL);
        Assert.Equal((false, (int?)4, 0), (refused.Used, refused.Original, refused.RefCount));
        Assert.Contains("not accepted", refused.Stderr);
        Assert.Equal((false, (int?)5, 0, ""), Gen5Attributes(5));
        var unknown = Gen5Attributes(-1);
        Assert.Equal((false, (int?)null, 0), (unknown.Used, unknown.Original, unknown.RefCount));
        Assert.Contains("generation unknown", unknown.Stderr);
        Assert.Equal((false, (int?)4, 0, ""), Gen5Attributes(4, gamma: "Blackmagic Design Video"));
        // A clone call that fails but still hands out an object: released, a warning, decoded as recorded.
        Assert.Equal((false, (int?)null, 0, "braw-decode: --gen5: the clip's processing attributes cannot be read — decoding as recorded" + Environment.NewLine),
            Gen5Attributes(4, cloneResult: Com.E_FAIL));
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
        Assert.Equal(("auto", "bmd-film-gen5", "Blackmagic RAW: Film Gen 4 → Gen 5 (SDK conversion)"), (converted.Source, converted.Id, converted.Reason));
        var plain = LutResolver.Resolve(clip, new ClipInfo { Gamma = "Blackmagic Design Film", ColorScienceGen = 4 }, @"X:\A001.braw", s, cat);
        Assert.Equal(("none", "Blackmagic RAW: Blackmagic Design Film Gen 4 (no built-in LUT)"), (plain.Source, plain.Reason));
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
        Assert.Equal(("none", "Blackmagic RAW: Blackmagic Design Film Gen 4 (no built-in LUT) — the conversion to Gen 5 failed"), (failed.Source, failed.Reason));
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
        Assert.Equal(["Blackmagic RAW: braw-decode does not know --gen5 (an older version) — Film Gen 4 is decoded without conversion to Gen 5"], logs);
    }

    // Fix round 1: only a usage error (exit 2) means "older decoder". A real failure under --gen5 (exit 4: the clip
    // cannot be opened) is not retried and not blamed on the decoder version — the clip gets its Czech note.
    [Fact]
    public void Backend_RealFailureWithGen5_IsNotRetried()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "braw-decode.cmd");
        File.WriteAllText(path, """
            @echo off
            >>"%~dp0args.txt" echo %*
            echo %* | findstr /c:"--gen5" >nul && (echo braw-decode: cannot open the clip 1>&2& exit /b 4)
            echo {"width":320,"height":180,"sourceWidth":320,"sourceHeight":180,"fps":25,"frames":1,"timecode":"","camera":""}
            exit /b 0

            """.ReplaceLineEndings("\r\n"));
        File.WriteAllText(Path.Combine(dir.Path, "BlackmagicRawAPI.dll"), "x");
        var tools = new ExternalTools { BrawDecoder = path, NoSearch = true };
        var logs = new List<string>();
        var b = new BrawBackend(_ => "x", gen5: true) { Log = logs.Add };

        var e = Assert.Throws<RawDecoderException>(() => b.Probe(Path.Combine(dir.Sub("card"), "A001.braw"), tools, dir.Path));
        Assert.StartsWith("Blackmagic RAW: cannot open the clip — ", e.Message);
        Assert.Equal(4, e.DecoderError.Status);
        var lines = File.ReadAllLines(Path.Combine(dir.Path, "args.txt"));
        Assert.Single(lines);
        Assert.StartsWith("--info --gen5 ", lines[0]);
        Assert.DoesNotContain(logs, l => l.Contains("does not know --gen5", StringComparison.Ordinal));
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
        Assert.Single(logs, l => l.Contains("does not know --gen5", StringComparison.Ordinal));
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
