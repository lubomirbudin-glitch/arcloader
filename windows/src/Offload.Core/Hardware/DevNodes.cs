using System.Text;

namespace Offload.Core.Hardware;

/// Device tree helpers over cfgmgr32. Every failure (device unplugged meanwhile, missing property) gives null/empty.
internal static unsafe class DevNodes
{
    public static List<string> Interfaces(Guid interfaceClass, string? deviceId)
    {
        for (int tries = 0; tries < 5; tries++)
        {
            if (UsbNative.CM_Get_Device_Interface_List_SizeW(out uint len, &interfaceClass, deviceId, 0) != UsbNative.CrSuccess || len <= 1) return [];
            var buf = new char[len];
            uint cr;
            fixed (char* p = buf) cr = UsbNative.CM_Get_Device_Interface_ListW(&interfaceClass, deviceId, p, len, 0);
            if (cr == UsbNative.CrBufferSmall) continue;  // the list changed between the two calls
            if (cr != UsbNative.CrSuccess) return [];
            return new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return [];
    }

    public static string Id(uint devInst)
    {
        if (UsbNative.CM_Get_Device_ID_Size(out uint len, devInst, 0) != UsbNative.CrSuccess) return "";
        var buf = new char[len + 1];
        fixed (char* p = buf)
            if (UsbNative.CM_Get_Device_IDW(devInst, p, len + 1, 0) != UsbNative.CrSuccess) return "";
        return new string(buf, 0, (int)len);
    }

    public static uint? Locate(string deviceId) =>
        UsbNative.CM_Locate_DevNodeW(out uint dn, deviceId, 0) == UsbNative.CrSuccess ? dn : null;

    public static uint? Parent(uint devInst) =>
        UsbNative.CM_Get_Parent(out uint p, devInst, 0) == UsbNative.CrSuccess ? p : null;

    public static IEnumerable<uint> Children(uint devInst)
    {
        if (UsbNative.CM_Get_Child(out uint c, devInst, 0) != UsbNative.CrSuccess) yield break;
        yield return c;
        while (UsbNative.CM_Get_Sibling(out c, c, 0) == UsbNative.CrSuccess) yield return c;
    }

    static object? Decode(uint type, byte[] b, uint size) => type switch
    {
        0x12 => Encoding.Unicode.GetString(b, 0, (int)size).TrimEnd('\0'),
        0x2012 => Encoding.Unicode.GetString(b, 0, (int)size).Split('\0', StringSplitOptions.RemoveEmptyEntries),
        0x7 when size >= 4 => BitConverter.ToUInt32(b, 0),
        0x11 when size >= 1 => b[0] != 0,
        _ => null,
    };

    public static object? Prop(uint devInst, DevPropKey key)
    {
        uint size = 0;
        if (UsbNative.CM_Get_DevNode_PropertyW(devInst, &key, out _, null, ref size, 0) != UsbNative.CrBufferSmall || size == 0) return null;
        var b = new byte[size];
        uint type;
        fixed (byte* p = b)
            if (UsbNative.CM_Get_DevNode_PropertyW(devInst, &key, out type, p, ref size, 0) != UsbNative.CrSuccess) return null;
        return Decode(type, b, size);
    }

    public static string? Str(uint devInst, DevPropKey key) => Prop(devInst, key) switch
    {
        string s => s,
        string[] a => string.Join(" | ", a),
        _ => null,
    };

    public static uint? UInt(uint devInst, DevPropKey key) => Prop(devInst, key) as uint?;

    public static string? InterfaceStr(string deviceInterface, DevPropKey key)
    {
        uint size = 0;
        if (UsbNative.CM_Get_Device_Interface_PropertyW(deviceInterface, &key, out _, null, ref size, 0) != UsbNative.CrBufferSmall || size == 0) return null;
        var b = new byte[size];
        uint type;
        fixed (byte* p = b)
            if (UsbNative.CM_Get_Device_Interface_PropertyW(deviceInterface, &key, out type, p, ref size, 0) != UsbNative.CrSuccess) return null;
        return Decode(type, b, size) as string;
    }

    /// Localized name from Device Manager ("Velkokapacitní paměťové zařízení USB").
    public static string Name(uint devInst) => Str(devInst, DevKeys.FriendlyName) ?? Str(devInst, DevKeys.DeviceDesc) ?? "?";

    /// The product's own name as the device reports it ("Elements 25A2"), else the Device Manager name.
    public static string BusName(uint devInst) => Str(devInst, DevKeys.BusReportedDesc) ?? Name(devInst);

    public static bool IsHub(uint devInst) => Interfaces(UsbNative.HubInterface, Id(devInst)).Count > 0;

    public static bool IsHostController(uint devInst) => Interfaces(UsbNative.HostControllerInterface, Id(devInst)).Count > 0;
}
