using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public sealed class UsbFactAttribute : FactAttribute
{
    public UsbFactAttribute()
    {
        if (DevNodes.Interfaces(UsbNative.HostControllerInterface, null).Count == 0) Skip = "no USB host controller";
    }
}

public sealed class UsbDriveFactAttribute : FactAttribute
{
    public static string? FirstUsbDrive => DriveInfo.GetDrives()
        .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
        .Select(d => d.RootDirectory.FullName)
        .FirstOrDefault(r => DriveLinks.Resolve(r).Bus == StorageBus.Usb);

    public UsbDriveFactAttribute()
    {
        if (FirstUsbDrive == null) Skip = "no USB drive attached";
    }
}
