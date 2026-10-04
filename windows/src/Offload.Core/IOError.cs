using System.ComponentModel;

namespace Offload.Core;

/// I/O failure with the operation and the file, so a report line says exactly what went wrong where.
public sealed class IOError : Exception
{
    public string Op { get; }
    public string FilePath { get; }
    /// Win32 error code (e.g. 87 = invalid parameter, 112 = disk full).
    public int Code { get; }

    public IOError(string op, string path, int code)
        : base($"{op} {path}: {new Win32Exception(code).Message} (error {code})")
    {
        Op = op;
        FilePath = path;
        Code = code;
    }

    public IOError(string op, string path, Exception inner)
        : base($"{op} {path}: {inner.Message}", inner)
    {
        Op = op;
        FilePath = path;
        Code = inner.HResult & 0xFFFF;
    }
}
