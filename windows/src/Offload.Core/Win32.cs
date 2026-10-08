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
