using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Offload.BrawDecode;

// Blackmagic RAW API for Windows, called through COM vtables with function pointers.
//
// Each wrapper is a raw interface pointer; method slots follow the SDK's IDL order (0–2 = IUnknown). The Mac header
// in mac/Tools/braw-decode/BlackmagicRawAPI.h has the same interfaces, IIDs and method order — on Windows strings
// are BSTR (caller frees with SysFreeString), bool is BOOL (4 bytes), Variant is the OLE VARIANT. Only the methods
// braw-decode needs are declared. References are counted by hand exactly like the C++ tool (no RCWs, no GC timing):
// whoever receives an interface from a Create*/Clone*/QueryInterface call releases it.
//
// The calling convention is __stdcall (STDMETHODCALLTYPE) everywhere; on x64 it is the one and only convention.

internal static unsafe class Com
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_POINTER = unchecked((int)0x80004003);

    public static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");

    public static nint Slot(nint obj, int index) => (*(nint**)obj)[index];

    /// IUnknown slot 0.
    public static int QueryInterface(nint obj, Guid iid, out nint result)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(obj, 0))(obj, &iid, &p);
        result = p;
        return hr;
    }

    /// IUnknown slot 1.
    public static uint AddRef(nint obj) => ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 1))(obj);

    /// IUnknown slot 2; a null pointer is a no-op (0).
    public static uint Release(nint obj) => obj == 0 ? 0 : ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj);

    /// The one getter for `HRESULT Get…(T* value)` methods (uint32, uint64, float, pointers). Like the C++ tool the
    /// HRESULT is not checked: on failure the value stays 0.
    public static T Get<T>(nint obj, int slot) where T : unmanaged
    {
        T v = default;
        ((delegate* unmanaged[Stdcall]<nint, T*, int>)Slot(obj, slot))(obj, &v);
        return v;
    }

    /// `HRESULT Get…(BSTR* value)`: the string, or null when the call fails.
    public static string? GetBstr(nint obj, int slot)
    {
        nint bstr = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(obj, slot))(obj, &bstr);
        var s = TakeBstr(bstr);
        return hr == S_OK ? s : null;
    }

    /// Takes ownership of a BSTR the API returned.
    public static string? TakeBstr(nint bstr)
    {
        if (bstr == 0) return null;
        try { return Marshal.PtrToStringBSTR(bstr); }
        finally { Marshal.FreeBSTR(bstr); }
    }

    public static string Hex(int hr) => "0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
}

internal static class Iid
{
    public static readonly Guid Factory = new("78DEEB84-98C9-434A-B7E5-7AACC2988399");
    public static readonly Guid Configuration = new("267E9866-FB40-4BFB-8BF8-96EA3F7DA36E");
    public static readonly Guid Callback = new("E9F98FAC-33DB-4A65-BB94-8A82B027AED0");
}

/// Four-character codes from the SDK enums.
internal static class FourCC
{
    public const uint PipelineCpu = 0x63707562;        // 'cpub'
    public const uint PipelineCuda = 0x63756461;       // 'cuda'
    public const uint PipelineOpenCL = 0x6F70636C;     // 'opcl'
    public const uint ResourceFormatRgbU16 = 0x3136696C; // '16il' — interleaved RGB 16-bit = ffmpeg rgb48le
    public const uint ClipAttributeColorScienceGen = 0x6373676E; // 'csgn' u16
    public const uint ClipAttributeGamma = 0x67616D61;           // 'gama' string
    public const uint ClipAttributeGamut = 0x67616D74;           // 'gamt' string

    /// The code as the SDK writes it ('gama'), for messages.
    public static string Text(uint code) =>
        string.Create(4, code, static (s, c) =>
        {
            for (int i = 0; i < 4; i++) s[i] = (char)(byte)(c >> (24 - 8 * i));
        });
}

/// IBlackmagicRawFactory: 3 CreateCodec, 4 CreatePipelineIterator, 5 CreatePipelineDeviceIterator, 6 CreateClipGeometry.
internal readonly unsafe struct BrawFactory(nint ptr)
{
    public nint Ptr => ptr;

    /// Slot 3.
    public int CreateCodec(out BrawCodec codec)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(ptr, 3))(ptr, &p);
        codec = new BrawCodec(p);
        return hr;
    }
}

/// IBlackmagicRaw: 3 OpenClip, 4 OpenClipWithGeometry, 5 SetCallback, 6 PreparePipeline, 7 PreparePipelineForDevice, 8 FlushJobs.
internal readonly unsafe struct BrawCodec(nint ptr)
{
    public nint Ptr => ptr;

    /// Slot 3.
    public int OpenClip(string fileName, out BrawClip clip)
    {
        nint bstr = Marshal.StringToBSTR(fileName);
        nint p = 0;
        try
        {
            int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Com.Slot(ptr, 3))(ptr, bstr, &p);
            clip = new BrawClip(p);
            return hr;
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    /// Slot 5. The SDK AddRefs the callback; SetCallback(0) gives the reference back.
    public int SetCallback(nint callback) => ((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(ptr, 5))(ptr, callback);

    /// Slot 8. Blocks until every submitted job (and its callbacks) has finished.
    public int FlushJobs() => ((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(ptr, 8))(ptr);

    /// IUnknown slot 0 with IID_IBlackmagicRawConfiguration.
    public int QueryConfiguration(out BrawConfiguration config)
    {
        int hr = Com.QueryInterface(ptr, Iid.Configuration, out var p);
        config = new BrawConfiguration(p);
        return hr;
    }
}

/// IBlackmagicRawConfiguration (read on the first OpenClip): 3 SetPipeline, 4 GetPipeline, 5 IsPipelineSupported,
/// 6 SetCPUThreads, 7 GetCPUThreads, 8 GetMaxCPUThreadCount, 9 SetWriteMetadataPerFrame, 10 GetWriteMetadataPerFrame,
/// 11 SetFromDevice, 12 GetVersion, 13 GetCameraSupportVersion.
internal readonly unsafe struct BrawConfiguration(nint ptr)
{
    public nint Ptr => ptr;

    /// Slot 3 (no pipeline context / command queue: CPU).
    public int SetPipeline(uint pipeline) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Com.Slot(ptr, 3))(ptr, pipeline, 0, 0);

    /// Slot 4 (context and command queue are optional outputs, not asked for).
    public int GetPipeline(out uint pipeline)
    {
        uint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint*, nint*, nint*, int>)Com.Slot(ptr, 4))(ptr, &p, null, null);
        pipeline = p;
        return hr;
    }

    /// Slot 5.
    public int IsPipelineSupported(uint pipeline, out bool supported)
    {
        int b = 0; // BOOL
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, int*, int>)Com.Slot(ptr, 5))(ptr, pipeline, &b);
        supported = b != 0;
        return hr;
    }

    /// Slot 12.
    public string? GetVersion() => Com.GetBstr(ptr, 12);

    /// Slot 13.
    public string? GetCameraSupportVersion() => Com.GetBstr(ptr, 13);
}

/// IBlackmagicRawClip: 3 GetWidth, 4 GetHeight, 5 GetFrameRate, 6 GetFrameCount, 7 GetTimecodeForFrame,
/// 8 GetMetadataIterator, 9 GetMetadata, 10 SetMetadata, 11 GetCameraType, 12 CloneClipProcessingAttributes,
/// 13 GetMulticardFileCount, 14 IsMulticardFilePresent, 15 GetSidecarFileAttached, 16 SaveSidecarFile,
/// 17 ReloadSidecarFile, 18 CreateJobReadFrame, 19 CreateJobTrim, 20 CloneWithGeometry.
internal readonly unsafe struct BrawClip(nint ptr)
{
    public nint Ptr => ptr;

    /// Slot 3.
    public uint Width => Com.Get<uint>(ptr, 3);

    /// Slot 4.
    public uint Height => Com.Get<uint>(ptr, 4);

    /// Slot 5.
    public float FrameRate => Com.Get<float>(ptr, 5);

    /// Slot 6.
    public ulong FrameCount => Com.Get<ulong>(ptr, 6);

    /// Slot 7.
    public string? TimecodeForFrame(ulong frame)
    {
        nint bstr = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, ulong, nint*, int>)Com.Slot(ptr, 7))(ptr, frame, &bstr);
        var s = Com.TakeBstr(bstr);
        return hr == Com.S_OK ? s : null;
    }

    /// Slot 11.
    public string? CameraType => Com.GetBstr(ptr, 11);

    /// Slot 12.
    public int CloneClipProcessingAttributes(out BrawClipProcessingAttributes attributes)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(ptr, 12))(ptr, &p);
        attributes = new BrawClipProcessingAttributes(p);
        return hr;
    }

    /// Slot 18.
    public int CreateJobReadFrame(ulong frame, out BrawJob job)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, ulong, nint*, int>)Com.Slot(ptr, 18))(ptr, frame, &p);
        job = new BrawJob(p);
        return hr;
    }
}

/// IBlackmagicRawClipProcessingAttributes: 3 GetClipAttribute, 4 SetClipAttribute, 5 GetClipAttributeRange,
/// 6 GetClipAttributeList, 7 GetISOList, 8 GetPost3DLUT.
internal readonly unsafe struct BrawClipProcessingAttributes(nint ptr)
{
    public nint Ptr => ptr;

    /// String attribute (gamma, gamut). null when the clip has none; `error` when it has one that is not a string
    /// or cannot be read — a decoder error to report, never an exception.
    public string? Text(uint attribute, out string? error)
    {
        var value = Fetch(attribute, out error);
        if (value is null or string) return (string?)value;
        error = $"clip attribute '{FourCC.Text(attribute)}' is a number, expected a string";
        return null;
    }

    /// Integer attribute (colour science gen is a u16). null when the clip has none; `error` as for Text.
    public int? Number(uint attribute, out string? error)
    {
        var value = Fetch(attribute, out error);
        if (value is null or int) return (int?)value;
        error = $"clip attribute '{FourCC.Text(attribute)}' is a string, expected a number";
        return null;
    }

    /// The one VARIANT read: slot 3 GetClipAttribute. A failed call (attribute not available) is null without an
    /// error; anything thrown while reading or clearing the VARIANT becomes `error`.
    object? Fetch(uint attribute, out string? error)
    {
        error = null;
        try
        {
            ComVariant v = default;
            try
            {
                int hr = ((delegate* unmanaged[Stdcall]<nint, uint, ComVariant*, int>)Com.Slot(ptr, 3))(ptr, attribute, &v);
                return hr == Com.S_OK ? Value(v) : null;
            }
            finally
            {
                v.Dispose();   // VariantClear (frees a BSTR)
            }
        }
        catch (Exception e)
        {
            error = $"clip attribute '{FourCC.Text(attribute)}': {e.Message}";
            return null;
        }
    }

    /// string for VT_BSTR (a null BSTR is the empty string, as in COM), int for the integer types, null for
    /// VT_EMPTY / VT_NULL. Integers are read straight from
    /// the VARIANT's data (no As<T> type mapping to get wrong); any other type throws NotSupportedException and a
    /// value outside int throws OverflowException — both end up as `error` in Fetch.
    internal static object? Value(ComVariant v) => v.VarType switch
    {
        VarEnum.VT_EMPTY or VarEnum.VT_NULL => null,
        VarEnum.VT_BSTR => v.As<string>() ?? "",
        VarEnum.VT_UI1 => (int)v.GetRawDataRef<byte>(),
        VarEnum.VT_I1 => (int)v.GetRawDataRef<sbyte>(),
        VarEnum.VT_UI2 => (int)v.GetRawDataRef<ushort>(),
        VarEnum.VT_I2 => (int)v.GetRawDataRef<short>(),
        VarEnum.VT_I4 or VarEnum.VT_INT => v.GetRawDataRef<int>(),
        VarEnum.VT_UI4 or VarEnum.VT_UINT => checked((int)v.GetRawDataRef<uint>()),
        var t => throw new NotSupportedException($"VARIANT type {t} is not supported"),
    };
}

/// IBlackmagicRawJob: 3 Submit, 4 Abort, 5 SetUserData, 6 GetUserData.
internal readonly unsafe struct BrawJob(nint ptr)
{
    public nint Ptr => ptr;

    /// Slot 3.
    public int Submit() => ((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(ptr, 3))(ptr);

    /// Slot 5.
    public int SetUserData(nint data) => ((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(ptr, 5))(ptr, data);

    /// Slot 6.
    public nint GetUserData() => Com.Get<nint>(ptr, 6);
}

/// IBlackmagicRawFrame: 3 GetFrameIndex, 4 GetTimecode, 5 GetMetadataIterator, 6 GetMetadata, 7 SetMetadata,
/// 8 CloneFrameProcessingAttributes, 9 SetResolutionScale, 10 GetResolutionScale, 11 SetResourceFormat,
/// 12 GetResourceFormat, 13 GetSensorRate, 14 CreateJobDecodeAndProcessFrame.
internal readonly unsafe struct BrawFrame(nint ptr)
{
    public nint Ptr => ptr;

    /// Slot 9.
    public int SetResolutionScale(Scale scale) => ((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Slot(ptr, 9))(ptr, (uint)scale);

    /// Slot 11.
    public int SetResourceFormat(uint format) => ((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Slot(ptr, 11))(ptr, format);

    /// Slot 14, with the clip's own processing attributes (null overrides) — frames come out in the gamma they were
    /// shot in.
    public int CreateJobDecodeAndProcessFrame(out BrawJob job)
    {
        nint p = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)Com.Slot(ptr, 14))(ptr, 0, 0, &p);
        job = new BrawJob(p);
        return hr;
    }
}

/// IBlackmagicRawProcessedImage: 3 GetWidth, 4 GetHeight, 5 GetResource, 6 GetResourceType, 7 GetResourceFormat,
/// 8 GetResourceSizeBytes, 9 GetResourceContextAndCommandQueue.
internal readonly unsafe struct BrawProcessedImage(nint ptr)
{
    public nint Ptr => ptr;

    /// Slot 3.
    public uint Width => Com.Get<uint>(ptr, 3);

    /// Slot 4.
    public uint Height => Com.Get<uint>(ptr, 4);

    /// Slot 5.
    public void* Resource => (void*)Com.Get<nint>(ptr, 5);

    /// Slot 8.
    public uint SizeBytes => Com.Get<uint>(ptr, 8);
}

/// Loads BlackmagicRawAPI.dll once per process (never unloaded: SDK worker threads live inside it) and creates factories.
internal static unsafe class BrawRuntime
{
    static readonly Dictionary<string, nint> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// null + error text (naming the library) when the library cannot be loaded or has no factory export.
    public static BrawFactory? CreateFactory(string dllPath, out string? error)
    {
        error = null;
        nint create;
        lock (Loaded)
        {
            if (!Loaded.TryGetValue(dllPath, out var lib))
            {
                if (!NativeLibrary.TryLoad(dllPath, out lib))
                {
                    error = $"cannot load {dllPath}";
                    return null;
                }
                Loaded[dllPath] = lib;
            }
            if (!NativeLibrary.TryGetExport(lib, "CreateBlackmagicRawFactoryInstance", out create))
            {
                error = $"{dllPath} has no CreateBlackmagicRawFactoryInstance";
                return null;
            }
        }
        nint factory = ((delegate* unmanaged[Stdcall]<nint>)create)();
        if (factory == 0)
        {
            error = $"{dllPath}: CreateBlackmagicRawFactoryInstance returned null";
            return null;
        }
        return new BrawFactory(factory);
    }
}
