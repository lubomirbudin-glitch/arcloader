using System.Runtime.InteropServices;

namespace Offload.Core;

/// Native memory aligned to 4096 B: unbuffered I/O needs sector-aligned buffers and sizes.
public sealed unsafe class AlignedBuffer : IDisposable
{
    public const int Alignment = 4096;
    byte* pointer;

    public int Size { get; }

    public AlignedBuffer(int size)
    {
        if (size <= 0 || size % Alignment != 0)
            throw new ArgumentException($"Buffer size must be a positive multiple of {Alignment}", nameof(size));
        Size = size;
        pointer = (byte*)NativeMemory.AlignedAlloc((nuint)size, Alignment);
    }

    public Span<byte> Span => new(pointer, Size);

    public void Dispose()
    {
        if (pointer == null) return;
        NativeMemory.AlignedFree(pointer);
        pointer = null;
    }
}
