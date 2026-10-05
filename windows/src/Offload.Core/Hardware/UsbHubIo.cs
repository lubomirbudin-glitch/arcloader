using Microsoft.Win32.SafeHandles;

namespace Offload.Core.Hardware;

/// One port as the hub reports it. Null fields: that query failed (device gone, old driver …).
internal sealed record RawPort(
    int Number, uint Status, byte Speed, bool IsHub, ushort Vid, ushort Pid, ushort BcdUsb,
    uint? Protocols, uint? V2Flags, uint? Properties, int CompanionPort, string? DriverKey);

internal static unsafe class UsbHubIo
{
    public static SafeFileHandle? Open(string hubInterfacePath) => UsbNative.OpenForQuery(hubInterfacePath);

    public static int? HubPortCount(SafeFileHandle hub)
    {
        UsbHubInformationEx info = default;
        return UsbNative.DeviceIoControl(hub, UsbNative.IoctlHubInformationEx, null, 0, &info, sizeof(UsbHubInformationEx), out _, 0)
            ? info.HighestPortNumber
            : null;
    }

    public static RawPort QueryPort(SafeFileHandle hub, int port)
    {
        UsbNodeConnectionInformationEx ci = default;
        ci.ConnectionIndex = (uint)port;
        bool haveConn = UsbNative.DeviceIoControl(hub, UsbNative.IoctlConnectionInfoEx, &ci, sizeof(UsbNodeConnectionInformationEx),
            &ci, sizeof(UsbNodeConnectionInformationEx), out _, 0);

        var v2 = new UsbNodeConnectionInformationExV2
        {
            ConnectionIndex = (uint)port, Length = (uint)sizeof(UsbNodeConnectionInformationExV2), SupportedUsbProtocols = 0b111,
        };
        bool haveV2 = UsbNative.DeviceIoControl(hub, UsbNative.IoctlConnectionInfoExV2, &v2, sizeof(UsbNodeConnectionInformationExV2),
            &v2, sizeof(UsbNodeConnectionInformationExV2), out _, 0);

        UsbPortConnectorProperties pc = default;
        pc.ConnectionIndex = (uint)port;
        bool havePc = UsbNative.DeviceIoControl(hub, UsbNative.IoctlPortConnectorProperties, &pc, sizeof(UsbPortConnectorProperties),
            &pc, sizeof(UsbPortConnectorProperties), out _, 0);

        string? driverKey = null;
        uint status = haveConn ? ci.ConnectionStatus : 0;
        if (status == 1)
        {
            UsbNodeConnectionName dk = default;
            dk.ConnectionIndex = (uint)port;
            if (UsbNative.DeviceIoControl(hub, UsbNative.IoctlDriverKeyName, &dk, sizeof(UsbNodeConnectionName), &dk, sizeof(UsbNodeConnectionName), out _, 0))
                driverKey = new string(dk.Name).TrimEnd('\0');
        }
        return new RawPort(port, status, ci.Speed, ci.DeviceIsHub != 0, ci.DeviceDescriptor.idVendor, ci.DeviceDescriptor.idProduct,
            ci.DeviceDescriptor.bcdUSB, haveV2 ? v2.SupportedUsbProtocols : null, haveV2 ? v2.Flags : null,
            havePc ? pc.UsbPortProperties : null, havePc ? pc.CompanionPortNumber : 0, driverKey);
    }
}
