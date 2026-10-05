using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Offload.BrawDecode;

namespace Offload.Core.Tests;

public class BrawDecodeTests
{
    // --- command line (same as the Mac tool) ---

    [Fact]
    public void Options_MacContract()
    {
        Assert.Equal(new Options(true, Scale.Eighth, 0, null, @"E:\A001.braw"), Options.Parse(["--info", "--scale", "eighth", @"E:\A001.braw"]));
        Assert.Equal(new Options(false, Scale.Quarter, 120, 1, "a.braw"), Options.Parse(["--scale", "quarter", "--start", "120", "--count", "1", "a.braw"]));
        Assert.Equal(new Options(false, Scale.Full, 0, null, "a.braw"), Options.Parse(["a.braw"]));
        Assert.Equal(Scale.Half, Options.Parse(["--scale", "half", "a.braw"])!.Scale);
    }

    [Theory]
    [InlineData]
    [InlineData("--info")]
    [InlineData("--scale", "huge", "a.braw")]
    [InlineData("--start", "-1", "a.braw")]
    [InlineData("--count", "x", "a.braw")]
    [InlineData("--scale")]
    [InlineData("--bogus", "a.braw")]
    public void Options_Bad_IsNull(params string[] args) => Assert.Null(Options.Parse(args));

    [Fact]
    public void Options_Range_ClampedToClip()
    {
        Assert.Equal((0UL, 240UL), Options.Parse(["a"])!.Range(240));
        Assert.Equal((239UL, 240UL), Options.Parse(["--start", "239", "--count", "5", "a"])!.Range(240));
        Assert.Equal((120UL, 121UL), Options.Parse(["--start", "120", "--count", "1", "a"])!.Range(240));
        Assert.Equal((300UL, 240UL), Options.Parse(["--start", "300", "a"])!.Range(240));   // nothing to do
        Assert.Equal((5UL, 240UL), Options.Parse(["--start", "5", "--count", "18446744073709551615", "a"])!.Range(240));
    }

    // --- --info JSON ---

    [Fact]
    public void InfoJson_MacKeysThenColour()
    {
        var f = new ClipFacts(1440, 810, 11520, 6480, 23.976f, 240, "01:02:03:04", "URSA Mini Pro 12K", "Blackmagic Design Film", "Blackmagic Design", 5);
        Assert.Equal("""{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976000,"frames":240,"timecode":"01:02:03:04","camera":"URSA Mini Pro 12K","gamma":"Blackmagic Design Film","gamut":"Blackmagic Design","colorScienceGen":5}""" + "\n",
            InfoJson.Format(f));
        var bare = new ClipFacts(1, 2, 3, 4, 25, 0, "", "", null, null, null);
        Assert.Equal("""{"width":1,"height":2,"sourceWidth":3,"sourceHeight":4,"fps":25.000000,"frames":0,"timecode":"","camera":""}""" + "\n",
            InfoJson.Format(bare));
    }

    [Fact]
    public void InfoJson_RoundTripsThroughBrawBackend()
    {
        var f = new ClipFacts(720, 405, 5760, 3240, 29.97f, 10, "10:00:00:00", "Pocket \"6K\" \\ Pro\u0001", "Blackmagic Design Video", null, 5);
        var i = BrawBackend.ParseInfo(InfoJson.Format(f));
        Assert.Equal((720, 405, 5760, 3240, 10), (i.Width, i.Height, i.SourceWidth, i.SourceHeight, i.Frames));
        Assert.Equal(29.97, i.Fps, 3);
        Assert.Equal("Pocket \"6K\" \\ Pro", i.Camera);
        Assert.Equal(("Blackmagic Design Video", null, 5), (i.Gamma, i.Gamut, i.ColorScienceGen));
    }

    // --- frame bytes ---

    [Fact]
    public unsafe void FrameSlot_CopiesExactlyWidthHeightRgb48()
    {
        var padded = new byte[4 * 2 * 6 + 100];   // the SDK pads its buffer at the end
        for (int i = 0; i < padded.Length; i++) padded[i] = (byte)i;
        var slot = new FrameSlot();
        fixed (byte* p = padded) Assert.True(slot.Fill(4, 2, p, (uint)padded.Length));
        Assert.True(slot.Ok);
        Assert.Equal(48, slot.Length);
        Assert.Equal(padded[..48], slot.Data[..slot.Length]);
        fixed (byte* p = padded) Assert.False(new FrameSlot().Fill(4, 2, p, 47));   // short buffer = failure
        Assert.False(new FrameSlot().Fill(4, 2, null, 48));
        Assert.Equal(12288L * 6480 * 6, FrameSlot.ExpectedBytes(12288, 6480));
    }

    // --- native callback object (no SDK needed) ---

    sealed class RecordingSink : IBrawCallbackSink
    {
        public readonly List<string> Calls = [];
        public bool Throw;
        public void ReadComplete(nint job, int result, nint frame)
        {
            Calls.Add($"read {job} {result} {frame}");
            if (Throw) throw new InvalidOperationException("boom");
        }
        public void ProcessComplete(nint job, int result, nint image) => Calls.Add($"process {job} {result} {image}");
    }

    [Fact]
    public unsafe void Callback_IsAComObjectTheSdkCanCall()
    {
        var sink = new RecordingSink();
        var reports = new List<string>();
        using var cb = new BrawCallback(sink, reports.Add);
        nint p = cb.Pointer;

        Assert.Equal(Com.S_OK, Com.QueryInterface(p, new Guid("E9F98FAC-33DB-4A65-BB94-8A82B027AED0"), out var q));
        Assert.Equal(p, q);
        Assert.Equal(2, cb.RefCount);
        Assert.Equal(1u, Com.Release(p));
        Assert.Equal(Com.S_OK, Com.QueryInterface(p, Com.IID_IUnknown, out q));
        Com.Release(q);
        Assert.Equal(Com.E_NOINTERFACE, Com.QueryInterface(p, Guid.NewGuid(), out q));
        Assert.Equal(0, q);
        var iid = Com.IID_IUnknown;
        Assert.Equal(Com.E_POINTER, ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Com.Slot(p, 0))(p, &iid, null));
        Assert.Equal(2u, Com.AddRef(p));
        Assert.Equal(1u, Com.Release(p));

        ((delegate* unmanaged[Stdcall]<nint, nint, int, nint, void>)Com.Slot(p, 3))(p, 11, 0, 22);
        ((delegate* unmanaged[Stdcall]<nint, nint, int, nint, void>)Com.Slot(p, 5))(p, 33, -1, 44);
        ((delegate* unmanaged[Stdcall]<nint, nint, int, void>)Com.Slot(p, 4))(p, 1, 0);          // DecodeComplete
        ((delegate* unmanaged[Stdcall]<nint, nint, float, void>)Com.Slot(p, 6))(p, 1, 0.5f);     // TrimProgress
        ((delegate* unmanaged[Stdcall]<nint, nint, int, void>)Com.Slot(p, 7))(p, 1, 0);          // TrimComplete
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, uint, nint, void>)Com.Slot(p, 8))(p, 0, 0, 1, 0);
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, uint, nint, void>)Com.Slot(p, 9))(p, 0, 0, 1, 0);
        ((delegate* unmanaged[Stdcall]<nint, nint, int, void>)Com.Slot(p, 10))(p, 0, 0);         // PreparePipelineComplete
        Assert.Equal(["read 11 0 22", "process 33 -1 44"], sink.Calls);
        Assert.Empty(reports);

        // An exception in the sink must never unwind into the SDK's thread: it goes to the injected reporter.
        sink.Throw = true;
        ((delegate* unmanaged[Stdcall]<nint, nint, int, nint, void>)Com.Slot(p, 3))(p, 1, 0, 2);
        Assert.Equal(3, sink.Calls.Count);
        Assert.Equal("braw-decode: boom", Assert.Single(reports));

        // ... and neither does an exception in the reporter itself.
        using var mute = new BrawCallback(sink, _ => throw new IOException("stderr closed"));
        nint m = mute.Pointer;
        ((delegate* unmanaged[Stdcall]<nint, nint, int, nint, void>)Com.Slot(m, 3))(m, 1, 0, 2);
        Assert.Equal(4, sink.Calls.Count);
    }

    [Fact]
    public unsafe void Callback_DisposedWhileTheSdkHoldsIt_LivesUntilTheLastRelease()
    {
        var sink = new RecordingSink();
        var reports = new List<string>();
        var cb = new BrawCallback(sink, reports.Add);
        nint p = cb.Pointer;
        Assert.Equal(2u, Com.AddRef(p));       // the SDK's reference (a missed SetCallback(null))

        cb.Dispose();
        cb.Dispose();                           // idempotent: drops our reference once
        Assert.Contains("still referenced", Assert.Single(reports));
        Assert.Throws<ObjectDisposedException>(() => cb.RefCount);

        // The SDK can still call in: the memory and the sink stay alive until its Release.
        ((delegate* unmanaged[Stdcall]<nint, nint, int, nint, void>)Com.Slot(p, 3))(p, 5, 0, 6);
        Assert.Equal(["read 5 0 6"], sink.Calls);
        Assert.Equal(0u, Com.Release(p));      // the last reference: freed now
    }

    // --- COM helpers ---

    [Fact]
    public void FourCC_Text_IsTheSdkCode()
    {
        Assert.Equal("gama", FourCC.Text(FourCC.ClipAttributeGamma));
        Assert.Equal("csgn", FourCC.Text(FourCC.ClipAttributeColorScienceGen));
        Assert.Equal("cpub", FourCC.Text(FourCC.PipelineCpu));
        Assert.Equal("16il", FourCC.Text(FourCC.ResourceFormatRgbU16));
        Assert.Equal("eith", FourCC.Text((uint)Scale.Eighth));
        Assert.Equal("0x80004005", Com.Hex(Com.E_FAIL));
    }

    [Fact]
    public void ClipAttribute_VariantValues()
    {
        static object? Value(ComVariant v)
        {
            try { return BrawClipProcessingAttributes.Value(v); }
            finally { v.Dispose(); }
        }
        Assert.Equal("Blackmagic Design Film", Value(ComVariant.Create("Blackmagic Design Film")));
        Assert.Equal(5, Value(ComVariant.Create<ushort>(5)));
        Assert.Equal(5, Value(ComVariant.Create<short>(5)));
        Assert.Equal(5, Value(ComVariant.Create<byte>(5)));
        Assert.Equal(5, Value(ComVariant.Create(5)));
        Assert.Equal(5, Value(ComVariant.Create(5u)));
        Assert.Equal(5, Value(ComVariant.CreateRaw(VarEnum.VT_INT, 5)));
        Assert.Equal(5, Value(ComVariant.CreateRaw(VarEnum.VT_UINT, 5u)));
        Assert.Null(Value(default));
        Assert.Null(Value(ComVariant.Null));
        // Anything else is a decoder error (reported, never a crash), not a silent guess.
        Assert.Throws<NotSupportedException>(() => Value(ComVariant.Create(1.5f)));
        Assert.Throws<OverflowException>(() => Value(ComVariant.Create(uint.MaxValue)));
    }

    /// A stand-in IBlackmagicRawClipProcessingAttributes whose GetClipAttribute (slot 3) hands out fixed VARIANTs.
    static unsafe class FakeAttributes
    {
        public const uint Float = 0x666C7420;   // 'flt '

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int GetClipAttribute(nint self, uint attribute, ComVariant* value)
        {
            *value = attribute switch
            {
                FourCC.ClipAttributeGamma => ComVariant.Create("Blackmagic Design Film"),
                FourCC.ClipAttributeColorScienceGen => ComVariant.Create<ushort>(5),
                FourCC.ClipAttributeGamut => ComVariant.Create(7),   // a number where a string belongs
                Float => ComVariant.Create(1.5f),
                _ => default,
            };
            return attribute is FourCC.ClipAttributeGamma or FourCC.ClipAttributeColorScienceGen
                or FourCC.ClipAttributeGamut or Float ? Com.S_OK : Com.E_FAIL;
        }

        /// The object: one pointer to a vtable whose slots 0–2 are never called here.
        public static nint Create()
        {
            var vtbl = (nint*)NativeMemory.AllocZeroed(4, (nuint)sizeof(nint));
            vtbl[3] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)&GetClipAttribute;
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

    [Fact]
    public void ClipAttribute_TextAndNumber_ErrorsAreReportedNotThrown()
    {
        nint obj = FakeAttributes.Create();
        try
        {
            var a = new BrawClipProcessingAttributes(obj);
            Assert.Equal("Blackmagic Design Film", a.Text(FourCC.ClipAttributeGamma, out var error));
            Assert.Null(error);
            Assert.Equal(5, a.Number(FourCC.ClipAttributeColorScienceGen, out error));
            Assert.Null(error);
            Assert.Null(a.Text(0x6162636B, out error));           // attribute not available: no value, no error
            Assert.Null(error);

            Assert.Null(a.Text(FourCC.ClipAttributeGamut, out error));
            Assert.Equal("clip attribute 'gamt' is a number, expected a string", error);
            Assert.Null(a.Number(FourCC.ClipAttributeGamma, out error));
            Assert.Equal("clip attribute 'gama' is a string, expected a number", error);
            Assert.Null(a.Number(FakeAttributes.Float, out error));
            Assert.Equal("clip attribute 'flt ': VARIANT type VT_R4 is not supported", error);
        }
        finally
        {
            FakeAttributes.Free(obj);
        }
    }

    // --- the library loader and the real library (DaVinci Resolve on this PC), still no clip ---

    [Fact]
    public void Runtime_NoLibraryOrNoFactory_IsAnErrorNotACrash()
    {
        Assert.Null(BrawRuntime.CreateFactory(@"C:\nonexistent\BlackmagicRawAPI.dll", out var error));
        Assert.Equal(@"cannot load C:\nonexistent\BlackmagicRawAPI.dll", error);
        var kernel32 = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
        Assert.Null(BrawRuntime.CreateFactory(kernel32, out error));
        Assert.Equal($"{kernel32} has no CreateBlackmagicRawFactoryInstance", error);
    }

    [BrawLibraryFact]
    public void Sdk_FactoryCodecConfigurationCallback()
    {
        var factory = BrawRuntime.CreateFactory(BrawLibraryFactAttribute.Library!, out var error);
        Assert.True(factory.HasValue, error);
        Assert.Null(error);
        var f = factory.Value;
        Assert.Equal(Com.S_OK, Com.QueryInterface(f.Ptr, Iid.Factory, out var same));   // the IID is the SDK's
        Assert.Equal(f.Ptr, same);
        Com.Release(same);
        Assert.Equal(Com.S_OK, f.CreateCodec(out var codec));
        Assert.Equal(Com.S_OK, codec.QueryConfiguration(out var config));
        Assert.Matches(@"^\d+\.\d+", config.GetCameraSupportVersion());
        Assert.NotNull(config.GetVersion());
        Assert.Equal(Com.S_OK, config.IsPipelineSupported(FourCC.PipelineCpu, out var cpu));
        Assert.True(cpu);
        Assert.Equal(Com.S_OK, config.SetPipeline(FourCC.PipelineCpu));
        Assert.Equal(Com.S_OK, config.GetPipeline(out var pipeline));
        Assert.Equal(FourCC.PipelineCpu, pipeline);

        using var cb = new BrawCallback(new RecordingSink(), _ => { });
        Assert.Equal(Com.S_OK, codec.SetCallback(cb.Pointer));
        Assert.True(cb.RefCount >= 2);   // the SDK keeps a reference
        Assert.Equal(Com.E_FAIL, codec.OpenClip(@"C:\nonexistent\A001.braw", out var clip));
        Assert.Equal(0, clip.Ptr);
        Assert.Equal(Com.S_OK, codec.FlushJobs());
        Assert.Equal(Com.S_OK, codec.SetCallback(0));
        Assert.Equal(1, cb.RefCount);

        Com.Release(config.Ptr);
        Assert.Equal(0u, Com.Release(codec.Ptr));
        Assert.Equal(0u, Com.Release(f.Ptr));
    }
}
