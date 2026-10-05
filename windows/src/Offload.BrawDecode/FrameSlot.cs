namespace Offload.BrawDecode;

/// One decoded frame waiting for its turn on stdout. Buffers are reused batch after batch; FrameSink.Begin clears
/// Ok before each batch (the Mac's `s.ok = false`), so a slot the SDK did not fill this time is never written.
internal sealed class FrameSlot
{
    public byte[] Data = [];
    public int Length;
    public uint Width, Height;
    public volatile bool Ok;

    /// stdout carries exactly width × height RGB48 pixels; the SDK may pad its buffer at the end.
    public static long ExpectedBytes(uint width, uint height) => (long)width * height * 6;

    /// Copies one processed image; false when the SDK buffer is shorter than a whole frame.
    public unsafe bool Fill(uint width, uint height, void* resource, uint sizeBytes)
    {
        long expected = ExpectedBytes(width, height);
        if (resource == null || expected == 0 || sizeBytes < expected || expected > Array.MaxLength) return false;
        if (Data.Length < expected) Data = new byte[expected];
        new ReadOnlySpan<byte>(resource, (int)expected).CopyTo(Data);
        Length = (int)expected;
        Width = width;
        Height = height;
        Ok = true;
        return true;
    }
}
