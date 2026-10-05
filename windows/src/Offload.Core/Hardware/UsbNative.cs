using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core.Hardware;

[StructLayout(LayoutKind.Sequential)]
internal struct DevPropKey(Guid fmtId, uint pid)
{
    public Guid FmtId = fmtId;
    public uint Pid = pid;
}

internal static class DevKeys
{
    static readonly Guid General = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    public static readonly DevPropKey DeviceDesc = new(General, 2);
    public static readonly DevPropKey Service = new(General, 6);
    public static readonly DevPropKey Driver = new(General, 11);
    public static readonly DevPropKey FriendlyName = new(General, 14);
    public static readonly DevPropKey Address = new(General, 30);
    public static readonly DevPropKey LocationPaths = new(General, 37);
    public static readonly DevPropKey BusReportedDesc = new(new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);
    public static readonly DevPropKey InstanceId = new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);
}

[StructLayout(LayoutKind.Sequential)]
internal struct StoragePropertyQuery { public int PropertyId; public int QueryType; public int AdditionalParameters; }

[StructLayout(LayoutKind.Sequential)]
internal struct StorageDeviceNumber { public uint DeviceType; public uint DeviceNumber; public uint PartitionNumber; }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UsbDeviceDescriptor
{
    public byte bLength, bDescriptorType;
    public ushort bcdUSB;
    public byte bDeviceClass, bDeviceSubClass, bDeviceProtocol, bMaxPacketSize0;
    public ushort idVendor, idProduct, bcdDevice;
    public byte iManufacturer, iProduct, iSerialNumber, bNumConfigurations;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct UsbNodeConnectionInformationEx
{
    public uint ConnectionIndex;
    public UsbDeviceDescriptor DeviceDescriptor;
    public byte CurrentConfigurationValue;
    /// 0 Low, 1 Full, 2 High, 3 Super — but USB 3 devices often still report High; use the V2 flags.
    public byte Speed;
    public byte DeviceIsHub;
    public ushort DeviceAddress;
    public uint NumberOfOpenPipes;
    /// 0 NoDevice, 1 Connected, …
    public uint ConnectionStatus;
    public fixed byte PipeList[32 * 11];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UsbNodeConnectionInformationExV2
{
    public uint ConnectionIndex;
    /// Must be 16.
    public uint Length;
    /// In: 7 (USB 1.1 | 2.0 | 3.x). Out: what the port supports.
    public uint SupportedUsbProtocols;
    /// bit0 operating at SuperSpeed or higher, bit1 SuperSpeed capable, bit2 operating at SuperSpeedPlus, bit3 SuperSpeedPlus capable.
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct UsbPortConnectorProperties
{
    public uint ConnectionIndex;
    public uint ActualLength;
    /// bit0 user connectable, bit1 debug capable, bit2 multiple companions, bit3 Type-C.
    public uint UsbPortProperties;
    public ushort CompanionIndex;
    public ushort CompanionPortNumber;
    public fixed char CompanionHubSymbolicLinkName[256];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct UsbHubInformationEx
{
    public uint HubType;
    public ushort HighestPortNumber;
    public fixed byte HubDescriptorUnion[71];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct UsbNodeConnectionName
{
    public uint ConnectionIndex;
    public uint ActualLength;
    public fixed char Name[512];
}

internal static unsafe partial class UsbNative
{
    public static readonly Guid DiskInterface = new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");
    public static readonly Guid HubInterface = new("F18A0E88-C30C-11D0-8815-00A0C906BED8");
    public static readonly Guid HostControllerInterface = new("3ABF6F2D-71C4-462A-8A92-1E6861E6AF27");

    public const uint IoctlStorageQueryProperty = 0x002D1400;
    public const uint IoctlStorageGetDeviceNumber = 0x002D1080;
    public const uint IoctlDriverKeyName = 0x220420;
    public const uint IoctlConnectionInfoEx = 0x220448;
    public const uint IoctlHubInformationEx = 0x220454;
    public const uint IoctlPortConnectorProperties = 0x220458;
    public const uint IoctlConnectionInfoExV2 = 0x22045C;

    public const uint CrSuccess = 0;
    public const uint CrBufferSmall = 0x1A;
    const uint ShareReadWrite = 3, OpenExisting = 3;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint securityAttributes, uint disposition, uint flags, nint template);

    /// Opens a device path for IOCTLs only (access 0) — enough for every query here, no admin needed.
    public static SafeFileHandle? OpenForQuery(string path)
    {
        var h = CreateFileW(path, 0, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (!h.IsInvalid) return h;
        h.Dispose();
        return null;
    }

    public const uint DriveRemote = 4;

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetDriveType(string rootPathName);

    /// UNC path of a mapped network drive ("Z:" → "\\server\share"); 0 = success.
    [LibraryImport("mpr.dll", EntryPoint = "WNetGetConnectionW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WNetGetConnection(string localName, char* remoteName, ref int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* inBuffer, int inSize, void* outBuffer, int outSize, out int returned, nint overlapped);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_Interface_List_SizeW(out uint length, Guid* interfaceClass, string? deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_Interface_ListW(Guid* interfaceClass, string? deviceId, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_Interface_PropertyW(string deviceInterface, DevPropKey* key, out uint type, byte* buffer, ref uint size, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Child(out uint child, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Sibling(out uint sibling, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Device_ID_Size(out uint length, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Device_IDW(uint devInst, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_DevNode_PropertyW(uint devInst, DevPropKey* key, out uint type, byte* buffer, ref uint size, uint flags);
}
