using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core;

internal static unsafe partial class Win32
{
    internal const uint MOVEFILE_REPLACE_EXISTING = 0x1;
    internal const uint MOVEFILE_WRITE_THROUGH = 0x8;
    internal const uint ES_SYSTEM_REQUIRED = 0x1;
    internal const uint ES_CONTINUOUS = 0x80000000;
    internal const int FileStorageInfo = 16;

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_STORAGE_INFO
    {
        public uint LogicalBytesPerSector;
        public uint PhysicalBytesPerSectorForAtomicity;
        public uint PhysicalBytesPerSectorForPerformance;
        public uint FileSystemEffectivePhysicalBytesPerSectorForAtomicity;
        public uint Flags;
        public uint ByteOffsetForSectorAlignment;
        public uint ByteOffsetForPartitionAlignment;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FlushFileBuffers(SafeFileHandle file);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetVolumePathName(string fileName, char* volumePathName, uint bufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetVolumeNameForVolumeMountPoint(string volumeMountPoint, char* volumeName, uint bufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetVolumeInformation(string rootPathName, char* volumeNameBuffer, uint volumeNameSize,
        out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags,
        char* fileSystemNameBuffer, uint fileSystemNameSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int fileInformationClass, void* info, uint bufferSize);

    [LibraryImport("kernel32.dll")]
    internal static partial uint SetThreadExecutionState(uint flags);

    const int ThreadModeBackgroundBegin = 0x00010000;
    const int ThreadModeBackgroundEnd = 0x00020000;
    const int ThreadIoPriority = 22;

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadPriority(nint thread, int priority);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationThread(nint thread, int infoClass, out int info, uint length, out uint returned);

    /// Plan 10 (spec 6): the calling thread's disk reads at background priority (low I/O priority) until Dispose (on the
    /// same thread) — the background verification never slows the proxy's reads. Best effort.
    internal static IDisposable BackgroundIo()
    {
        bool on = SetThreadPriority(GetCurrentThread(), ThreadModeBackgroundBegin);
        return new BackgroundMode(on);
    }

    sealed class BackgroundMode(bool on) : IDisposable
    {
        bool ended = !on;

        public void Dispose()
        {
            if (ended) return;
            ended = true;
            SetThreadPriority(GetCurrentThread(), ThreadModeBackgroundEnd);
        }
    }

    /// The calling thread's I/O priority hint (0 very low, 1 low, 2 normal …); -1 when it cannot be told.
    internal static int IoPriority() =>
        NtQueryInformationThread(GetCurrentThread(), ThreadIoPriority, out int priority, sizeof(int), out _) == 0 ? priority : -1;

    const int ProcessIoPriorityClass = 33;   // PROCESSINFOCLASS ProcessIoPriority
    const int IoPriorityNormal = 2;
    const uint ProcessModeBackgroundBegin = 0x00100000;
    const uint ProcessModeBackgroundEnd = 0x00200000;

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(nint process, int infoClass, out int info, uint length, out uint returned);

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetInformationProcess(nint process, int infoClass, ref int info, uint length);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetPriorityClass(nint process, uint priorityClass);

    /// A process's I/O priority hint (0 very low, 1 low, 2 normal …); -1 when it cannot be told.
    internal static int ProcessIoPriority(System.Diagnostics.Process p) =>
        NtQueryInformationProcess(p.Handle, ProcessIoPriorityClass, out int priority, sizeof(int), out _) == 0 ? priority : -1;

    internal static bool SetProcessIoPriority(System.Diagnostics.Process p, int priority) =>
        NtSetInformationProcess(p.Handle, ProcessIoPriorityClass, ref priority, sizeof(int)) == 0;

    /// A tool Offload started (ffmpeg, art-cmd, REDline, braw-decode …) at least at normal CPU and I/O priority. Windows
    /// hands a child its parent's class when that is BelowNormal or Idle (Offload started by a scheduler or a launcher
    /// at low priority) and very low I/O priority when the parent is in background mode — a RAW decode reading 570 MB/s
    /// would crawl (on the Mac 1.4 instead of 11.6 fps). Never lowers anything; best effort (the process may be gone).
    internal static void RaiseToNormal(System.Diagnostics.Process p)
    {
        try
        {
            if (p.PriorityClass is System.Diagnostics.ProcessPriorityClass.Idle or System.Diagnostics.ProcessPriorityClass.BelowNormal)
                p.PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
            if (ProcessIoPriority(p) is >= 0 and < IoPriorityNormal) SetProcessIoPriority(p, IoPriorityNormal);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    /// Tests only: the whole current process in background mode (low CPU, I/O and memory priority) until Dispose.
    internal static IDisposable BackgroundProcess()
    {
        bool on = SetPriorityClass(GetCurrentProcess(), ProcessModeBackgroundBegin);
        return new ProcessBackgroundMode(on);
    }

    sealed class ProcessBackgroundMode(bool on) : IDisposable
    {
        bool ended = !on;

        public void Dispose()
        {
            if (ended) return;
            ended = true;
            SetPriorityClass(GetCurrentProcess(), ProcessModeBackgroundEnd);
        }
    }

    /// Explorer's natural order ("C2" before "C10") — the Windows counterpart of localizedStandardCompare.
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int StrCmpLogicalW(string a, string b);

    // Plan 10 fix round 1: the final path of a handle (junctions, symlinks) and the flags a handle really has.
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_SHARE_ALL = 0x7;
    internal const int FileModeInformation = 16;
    internal const uint FILE_WRITE_THROUGH = 0x2;
    internal const uint FILE_NO_INTERMEDIATE_BUFFERING = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_STATUS_BLOCK
    {
        public nint Status;
        public nuint Information;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    internal static partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* path, uint length, uint flags);

    [LibraryImport("ntdll.dll")]
    internal static partial int NtQueryInformationFile(SafeFileHandle file, out IO_STATUS_BLOCK ioStatus, out uint info, uint length, int infoClass);

}
