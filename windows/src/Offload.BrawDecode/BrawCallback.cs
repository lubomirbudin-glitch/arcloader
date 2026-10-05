using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Offload.BrawDecode;

/// What the SDK reports back. Called on SDK worker threads, possibly in parallel.
internal interface IBrawCallbackSink
{
    void ReadComplete(nint job, int result, nint frame);
    void ProcessComplete(nint job, int result, nint processedImage);
}

/// A native IBlackmagicRawCallback object for IBlackmagicRaw::SetCallback: a vtable of [UnmanagedCallersOnly]
/// functions plus a GCHandle to the managed sink. Vtable order (IDL): 0 QueryInterface, 1 AddRef, 2 Release,
/// 3 ReadComplete, 4 DecodeComplete, 5 ProcessComplete, 6 TrimProgress, 7 TrimComplete,
/// 8 SidecarMetadataParseWarning, 9 SidecarMetadataParseError, 10 PreparePipelineComplete.
///
/// Lifetime is plain COM reference counting: we hold one reference, the SDK takes its own in SetCallback and gives
/// it back in SetCallback(0). Dispose drops ours; the memory and the GCHandle are freed when the count reaches 0, so
/// an SDK that still holds the object can never call into freed memory. Call codec.SetCallback(0) before Dispose.
///
/// No exception ever unwinds into an SDK thread: a throwing sink is reported through `report` (default stderr).
internal sealed unsafe class BrawCallback : IDisposable
{
    struct Native
    {
        public nint* Vtbl;
        public nint Target;   // GCHandle of the Target below
        public int Refs;
    }

    sealed class Target(IBrawCallbackSink sink, Action<string> report)
    {
        public readonly IBrawCallbackSink Sink = sink;
        public readonly Action<string> Report = report;
    }

    static readonly nint* Vtbl = CreateVtbl();
    nint native;   // Native*, 0 once disposed

    /// `report` gets one line per problem (a sink exception, a Dispose while the SDK still holds the object).
    public BrawCallback(IBrawCallbackSink sink, Action<string>? report = null)
    {
        var n = (Native*)NativeMemory.AllocZeroed((nuint)sizeof(Native));
        n->Vtbl = Vtbl;
        n->Target = GCHandle.ToIntPtr(GCHandle.Alloc(new Target(sink, report ?? (m => Console.Error.WriteLine(m)))));
        n->Refs = 1;
        native = (nint)n;
    }

    /// The IBlackmagicRawCallback* to hand to SetCallback.
    public nint Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(native == 0, this);
            return native;
        }
    }

    /// References held by the SDK plus our own one (for tests and diagnostics).
    public int RefCount => Volatile.Read(ref ((Native*)Pointer)->Refs);

    /// Drops our reference; frees now if the SDK holds none, otherwise on the SDK's last Release.
    public void Dispose()
    {
        var self = (Native*)Interlocked.Exchange(ref native, 0);
        if (self == null) return;
        var target = TargetOf(self);   // before the decrement: afterwards an SDK Release may free self
        int left = Interlocked.Decrement(ref self->Refs);
        if (left == 0) Destroy(self);
        else Report(target, () => $"braw-decode: callback still referenced by the SDK ({left}); freed on its last Release");
    }

    static nint* CreateVtbl()
    {
        var v = (nint*)NativeMemory.Alloc(11, (nuint)sizeof(nint));  // lives as long as the process
        v[0] = (nint)(delegate* unmanaged[Stdcall]<Native*, Guid*, nint*, int>)&QueryInterface;
        v[1] = (nint)(delegate* unmanaged[Stdcall]<Native*, uint>)&AddRef;
        v[2] = (nint)(delegate* unmanaged[Stdcall]<Native*, uint>)&Release;
        v[3] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, nint, void>)&ReadComplete;
        v[4] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, void>)&JobDone;          // DecodeComplete
        v[5] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, nint, void>)&ProcessComplete;
        v[6] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, float, void>)&TrimProgress;
        v[7] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, void>)&JobDone;          // TrimComplete
        v[8] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, nint, uint, nint, void>)&SidecarMessage; // Warning
        v[9] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, nint, uint, nint, void>)&SidecarMessage; // Error
        v[10] = (nint)(delegate* unmanaged[Stdcall]<Native*, nint, int, void>)&JobDone;         // PreparePipelineComplete
        return v;
    }

    static Target TargetOf(Native* self) => (Target)GCHandle.FromIntPtr(self->Target).Target!;

    /// Runs exactly once per object (the count reaches 0 once). Cannot throw: the handle was allocated in the
    /// constructor and is non-zero, and nothing else frees it.
    static void Destroy(Native* self)
    {
        var handle = GCHandle.FromIntPtr(self->Target);
        NativeMemory.Free(self);
        handle.Free();
    }

    /// The message is built inside the try too: an exception's Message getter may itself throw.
    static void Report(Target? target, Func<string> message)
    {
        try { target?.Report(message()); }
        catch (Exception) { /* the reporter itself failed (stderr closed): nowhere left to report to */ }
    }

    static void Report(Target? target, Exception e) => Report(target, () => $"braw-decode: {e.Message}");

    // Slot 0.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static int QueryInterface(Native* self, Guid* iid, nint* result)
    {
        if (result == null) return Com.E_POINTER;
        if (*iid == Com.IID_IUnknown || *iid == Iid.Callback)
        {
            Interlocked.Increment(ref self->Refs);
            *result = (nint)self;
            return Com.S_OK;
        }
        *result = 0;
        return Com.E_NOINTERFACE;
    }

    // Slot 1.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint AddRef(Native* self) => (uint)Interlocked.Increment(ref self->Refs);

    // Slot 2. The last reference (ours or the SDK's, whichever goes last) frees the object.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint Release(Native* self)
    {
        int left = Interlocked.Decrement(ref self->Refs);
        if (left == 0) Destroy(self);
        return (uint)left;
    }

    // Slot 3.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void ReadComplete(Native* self, nint job, int result, nint frame)
    {
        Target? target = null;
        try
        {
            target = TargetOf(self);
            target.Sink.ReadComplete(job, result, frame);
        }
        catch (Exception e)
        {
            Report(target, e);
        }
    }

    // Slot 5.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void ProcessComplete(Native* self, nint job, int result, nint image)
    {
        Target? target = null;
        try
        {
            target = TargetOf(self);
            target.Sink.ProcessComplete(job, result, image);
        }
        catch (Exception e)
        {
            Report(target, e);
        }
    }

    // Slots 4, 7, 10 (DecodeComplete, TrimComplete, PreparePipelineComplete): nothing to do.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void JobDone(Native* self, nint job, int result) { }

    // Slot 6.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void TrimProgress(Native* self, nint job, float progress) { }

    // Slots 8, 9 (SidecarMetadataParseWarning / Error): braw-decode ignores sidecars like the Mac tool.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static void SidecarMessage(Native* self, nint clip, nint fileName, uint line, nint info) { }
}
