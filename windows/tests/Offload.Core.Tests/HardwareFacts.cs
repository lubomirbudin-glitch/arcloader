using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public sealed class UsbFactAttribute : FactAttribute
{
    public UsbFactAttribute()
    {
        if (DevNodes.Interfaces(UsbNative.HostControllerInterface, null).Count == 0) Skip = "no USB host controller";
    }
}
