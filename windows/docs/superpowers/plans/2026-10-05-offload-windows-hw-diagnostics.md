# Offload pro Windows — plán HW: USB diagnostika a propustnost článků

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aplikace zjistí, kde a jak rychle jsou připojené karta a cílové disky (řadič, hub, port, rychlost linky, co zařízení umí), dá doporučení k zapojení, volitelně změří skutečnou rychlost, a během kopie ukáže, který článek řetězu brzdí. Vše bez administrátorských práv.

**Architecture:** Nová složka `Offload.Core/Hardware/`. Tenká vrstva P/Invoke (cfgmgr32 + `DeviceIoControl` na huby a svazky) ověřená spike-em 2026-10-05 na tomto PC; nad ní čisté, testovatelné funkce: rozhodnutí o rychlosti linky, sestavení „konektorů“ (USB 2 port + jeho USB 3 dvojče), doporučení, řetěz článků během kopie. CLI `offload ports [--measure]` a řetěz na začátku `offload copy`. Dotaz při startu a sekce Hardware v GUI jsou v plánu 4.

**Tech Stack:** C# / .NET 10, `LibraryImport` (kernel32, cfgmgr32), xUnit.

**Spec:** `windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md` — oddíl 8a (+ zjištění ze spike-u níže).

## Ověřeno spike-em na tomto PC (2026-10-05, bez admina)

- Svazek → disk: `\\.\E:` otevřít s access 0, share R|W; `IOCTL_STORAGE_QUERY_PROPERTY` (0x2D1400; BusType na offsetu 28: 7 USB, 11 SATA, 17 NVMe…), `IOCTL_STORAGE_GET_DEVICE_NUMBER` (0x2D1080).
- Disk → USB zařízení: rozhraní `GUID_DEVINTERFACE_DISK`, shoda čísla disku, `DEVPKEY_Device_InstanceId` → `CM_Locate_DevNodeW` → `CM_Get_Parent` až k uzlu, jehož rodič má rozhraní `GUID_DEVINTERFACE_USB_HUB`; port = `DEVPKEY_Device_Address`; řadič = předek s `GUID_DEVINTERFACE_USB_HOST_CONTROLLER`.
- Hub: otevřít rozhraní hubu s access 0; IOCTL `GET_HUB_INFORMATION_EX` (0x220454, počet portů), `GET_NODE_CONNECTION_INFORMATION_EX` (0x220448), `..._EX_V2` (0x22045C, `Length=16`, `SupportedUsbProtocols=7` jinak chyba 87), `GET_PORT_CONNECTOR_PROPERTIES` (0x220458), `GET_NODE_CONNECTION_DRIVERKEY_NAME` (0x220420; = `DEVPKEY_Device_Driver` dítěte).
- **Pozor:** `CONNECTION_INFORMATION_EX.Speed` hlásí u USB 3 zařízení na USB 3 portu `High` (480 Mb/s). Skutečnou linku dávají příznaky V2: bit0 běží ≥ SuperSpeed, bit2 běží ≥ SuperSpeedPlus; bit1/bit3 = zařízení to umí. Ověřeno: WD Elements na portu 9 (jen USB 2.0) → V2 opSS=False ssCap=True; po přepojení na port 19 (USB 3) → opSS=True.
- xHCI root hub ukazuje každý fyzický konektor dvakrát: USB 2 port (1–16) a USB 3 port (17–26), propojené `CompanionPortNumber`. USB 2 port bez dvojčete = konektor jen USB 2.0.
- Nejde zjistit: 10 vs 20 Gb/s (IOCTL SuperSpeedPlus neověřen → „10 Gb/s a víc“), zda USB 3 port umí 5 nebo 10 Gb/s, fyzické umístění konektoru (_PLD nespolehlivé → uživatel pojmenuje porty), kvalita kabelu přímo (jen nepřímo: umí víc, běží méně).
- Názvy zařízení jsou lokalizované (čeština) — pro logiku používat VID/PID, službu, `BusReportedDeviceDesc`.

## Global Constraints

- Bez administrátorských práv, bez nových NuGet balíčků, `net10.0-windows`.
- Nic se nezapisuje mimo dočasné soubory měření (`.offload-bench-*` na cílových discích, mazané) a `%APPDATA%\Offload\hwtest.json`. Na kartu se nikdy nezapisuje (u karty se měří jen čtení).
- Diagnostika nikdy neshodí offload: každá chyba P/Invoke → „neznámé“ (null / Unknown), žádná výjimka ven z veřejných funkcí `DriveLinks.Resolve` a `UsbScanner.Scan`.
- Texty pro uživatele česky; rychlosti linek: „USB 2.0 (480 Mb/s)“, „USB 3 (5 Gb/s)“, „USB 3 (10 Gb/s a víc)“.
- Mac (`mac/`) se nemění. Každý commit: řádek nahoře v nové sekci „plán HW“ v `docs/VYVOJ-WINDOWS.md` (bez SHA); poslední řádek zprávy přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Pojmenování portů: jádro je přijímá jako slovník `UsbConnector.Id → název` (`Advisor.Advise`, `HwTest.Run`); ukládání do nastavení a editor názvů jsou v plánu 4 (GUI). CLI používá výchozí popisky.
- Testy závislé na hardwaru: `[UsbFact]` (přeskočí bez USB řadiče), `[UsbDriveFact]` (přeskočí bez USB disku). Na tomto PC je E: (WD Elements) na USB.

## Review Focus

1. **USB 3 zařízení v USB 3 portu** → musí ukázat 5 Gb/s, ne 480 Mb/s (past `Speed=High`). Test: Task 2 `Link_V2FlagWinsOverSpeedField`; ruční ověření Task 8.
2. **USB 3 disk v konektoru, který USB 3 umí, ale linka běží jako USB 2.0** → hláška „kabel nebo kontakt“, ne „port je jen USB 2.0“. Test: Task 5 `Advice_CapableDeviceSlowLinkOnUsb3Connector`.
3. **Síťový disk, mount-point nebo virtuální disk jako cíl** → žádný pád, „síťový/neznámý“. Test: Task 4 `Resolve_UncAndMissingPath_DoNotThrow`.
4. **Odpojení zařízení během skenu** (uzel zmizí mezi voláními) → sken doběhne, chybějící data = neznámé. Test: Task 3 `Connectors_TolerateMissingCompanionAndDevice` + všechny P/Invoke cesty vrací null.
5. **Karta a oba cíle na jednom řadiči** → upozornění na sdílenou kapacitu a správné úzké hrdlo. Test: Task 5 `Advice_SharedController`, Task 7 `Bottleneck_*`.

---

## Struktura souborů

```
windows/src/Offload.Core/Hardware/
  UsbNative.cs      P/Invoke, struktury, IOCTL a GUID konstanty, DevPropKey
  DevNodes.cs       pomocník pro uzly zařízení (rozhraní, rodič/děti, vlastnosti)
  UsbHubIo.cs       otevření hubu, informace o hubu a portu (RawPort)
  UsbSpeeds.cs      UsbLinkSpeed, rozhodnutí linka/schopnost, popisky, praktické stropy MB/s
  UsbTopology.cs    UsbController/UsbHub/UsbPort/UsbDevice/UsbConnector, UsbScanner.Scan, Connectors.Build
  DriveLinks.cs     StorageBus, UsbLink, DriveLink, DriveLinks.Resolve
  Advisor.cs        Advice, Advisor.Advise
  ChainView.cs      ChainElement, ChainView (řetěz článků, úzké hrdlo)
  HwTest.cs         DriveMeasurement, HwTestResult, HwTest.Run/Save/Load
windows/src/Offload.Cli/
  PortsCommand.cs   offload ports [--measure] [--card …] [--dest …]
  Program.cs, CopyCommand.cs, ProgressDisplay.cs (úprava)
windows/tests/Offload.Core.Tests/
  HardwareFacts.cs  [UsbFact], [UsbDriveFact]
  UsbNativeTests.cs, UsbSpeedsTests.cs, UsbTopologyTests.cs, DriveLinksTests.cs, AdvisorTests.cs, ChainViewTests.cs, HwTestTests.cs
```

---

### Task 1: P/Invoke vrstva a uzly zařízení

**Files:**
- Create: `windows/src/Offload.Core/Hardware/UsbNative.cs`, `DevNodes.cs`, `UsbHubIo.cs`
- Create: `windows/tests/Offload.Core.Tests/HardwareFacts.cs`, `UsbNativeTests.cs`

**Interfaces:**
- Produces (všechno `internal`): `DevPropKey`; `DevKeys` (DeviceDesc, Service, Driver, FriendlyName, Address, LocationPaths, BusReportedDesc, InstanceId); `UsbNative` (konstanty, struktury `UsbDeviceDescriptor`, `UsbNodeConnectionInformationEx`, `UsbNodeConnectionInformationExV2`, `UsbPortConnectorProperties`, `UsbHubInformationEx`, `UsbNodeConnectionName`, `StoragePropertyQuery`, `StorageDeviceNumber`, funkce CreateFile/DeviceIoControl/CM_*); `DevNodes.Interfaces(Guid, string?) -> List<string>`, `Id(uint)`, `Locate(string) -> uint?`, `Parent(uint) -> uint?`, `Children(uint) -> IEnumerable<uint>`, `Prop(uint, DevPropKey) -> object?`, `Str(uint, DevPropKey) -> string?`, `UInt(uint, DevPropKey) -> uint?`, `InterfaceStr(string, DevPropKey) -> string?`, `Name(uint)`, `BusName(uint)`; `RawPort` record; `UsbHubIo.Open(string) -> SafeFileHandle?`, `HubPortCount(SafeFileHandle) -> int?`, `QueryPort(SafeFileHandle, int) -> RawPort`. Testy: `UsbFactAttribute`, `UsbDriveFactAttribute`.

- [ ] **Step 1: Testovací atributy a padající testy**

`windows/tests/Offload.Core.Tests/HardwareFacts.cs`:

```csharp
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
```

(`DriveLinks`/`StorageBus` vzniknou v Task 4 — do té doby `UsbDriveFactAttribute` vynechat a přidat v Task 4.)

`windows/tests/Offload.Core.Tests/UsbNativeTests.cs`:

```csharp
using System.Runtime.InteropServices;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public unsafe class UsbNativeTests
{
    [Fact]
    public void StructLayouts_MatchUsbioctlH()
    {
        Assert.Equal(20, sizeof(DevPropKey));
        Assert.Equal(18, sizeof(UsbDeviceDescriptor));
        Assert.Equal(23, (int)Marshal.OffsetOf<UsbNodeConnectionInformationEx>(nameof(UsbNodeConnectionInformationEx.Speed)));
        Assert.Equal(31, (int)Marshal.OffsetOf<UsbNodeConnectionInformationEx>(nameof(UsbNodeConnectionInformationEx.ConnectionStatus)));
        Assert.Equal(16, sizeof(UsbNodeConnectionInformationExV2));
        Assert.Equal(16 + 512, sizeof(UsbPortConnectorProperties));
        Assert.Equal(77, sizeof(UsbHubInformationEx));
        Assert.Equal(12, sizeof(StoragePropertyQuery));
        Assert.Equal(12, sizeof(StorageDeviceNumber));
    }

    [Fact]
    public void IoctlCodes() 
    {
        Assert.Equal(0x220448u, UsbNative.IoctlConnectionInfoEx);
        Assert.Equal(0x22045Cu, UsbNative.IoctlConnectionInfoExV2);
        Assert.Equal(0x220458u, UsbNative.IoctlPortConnectorProperties);
        Assert.Equal(0x220454u, UsbNative.IoctlHubInformationEx);
        Assert.Equal(0x220420u, UsbNative.IoctlDriverKeyName);
    }

    [Fact]
    public void EveryPcHasADisk_WhoseParentChainReachesTheRoot()
    {
        var disks = DevNodes.Interfaces(UsbNative.DiskInterface, null);
        Assert.NotEmpty(disks);
        var id = DevNodes.InterfaceStr(disks[0], DevKeys.InstanceId);
        Assert.False(string.IsNullOrEmpty(id));
        var dn = DevNodes.Locate(id!);
        Assert.NotNull(dn);
        Assert.False(string.IsNullOrEmpty(DevNodes.Name(dn!.Value)));
        string last = "";
        for (uint? cur = dn; cur is uint c; cur = DevNodes.Parent(c)) last = DevNodes.Id(c);
        Assert.StartsWith("HTREE", last, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingNodeAndInterface_ReturnNothing()
    {
        Assert.Null(DevNodes.Locate(@"USB\VID_FFFF&PID_FFFF\nope"));
        Assert.Empty(DevNodes.Interfaces(Guid.NewGuid(), null));
        Assert.Null(UsbHubIo.Open(@"\\?\usb#nope#{f18a0e88-c30c-11d0-8815-00a0c906bed8}"));
    }

    [UsbFact]
    public void RootHub_HasPorts_AndConnectedPortsReportVendor()
    {
        var hc = DevNodes.Interfaces(UsbNative.HostControllerInterface, null)[0];
        var hcDn = DevNodes.Locate(DevNodes.InterfaceStr(hc, DevKeys.InstanceId)!)!.Value;
        var rootHub = DevNodes.Children(hcDn).First(c => DevNodes.Interfaces(UsbNative.HubInterface, DevNodes.Id(c)).Count > 0);
        using var h = UsbHubIo.Open(DevNodes.Interfaces(UsbNative.HubInterface, DevNodes.Id(rootHub))[0]);
        Assert.NotNull(h);
        var ports = UsbHubIo.HubPortCount(h!);
        Assert.True(ports > 0);
        var raws = Enumerable.Range(1, ports!.Value).Select(p => UsbHubIo.QueryPort(h!, p)).ToList();
        Assert.All(raws.Where(r => r.Status == 1), r => Assert.NotEqual(0, r.Vid));
        Assert.All(raws, r => Assert.NotNull(r.Protocols));
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~UsbNativeTests"`
Expected: build FAIL — `Offload.Core.Hardware` neexistuje.

- [ ] **Step 3: UsbNative**

`windows/src/Offload.Core/Hardware/UsbNative.cs`:

```csharp
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
```

- [ ] **Step 4: DevNodes a UsbHubIo**

`windows/src/Offload.Core/Hardware/DevNodes.cs`:

```csharp
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
```

`windows/src/Offload.Core/Hardware/UsbHubIo.cs`:

```csharp
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
```

- [ ] **Step 5: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~UsbNativeTests"`
Expected: PASS (5 testů; `UsbFact` na tomto PC běží).

- [ ] **Step 6: Commit**

Řádek do `docs/VYVOJ-WINDOWS.md` — nová sekce `## 2026-10-05 — plán HW (USB diagnostika), průběh` nad sekcí plánu 2, první položka: `- Úkol 1 — přístup ke stromu zařízení a USB hubům bez admin práv (cfgmgr32, IOCTL)`.

```powershell
git add windows docs
git commit -m "Windows: USB/device-tree native layer (no admin)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Rychlost linky a schopnosti

**Files:**
- Create: `windows/src/Offload.Core/Hardware/UsbSpeeds.cs`
- Test: `windows/tests/Offload.Core.Tests/UsbSpeedsTests.cs`

**Interfaces:**
- Produces: `enum UsbLinkSpeed { Unknown, Low, Full, High, Super, SuperPlus }`; `UsbSpeeds.Link(byte connectionSpeed, uint? v2Flags) -> UsbLinkSpeed`, `UsbSpeeds.Capability(uint? v2Flags, ushort bcdUsb, UsbLinkSpeed link) -> UsbLinkSpeed`, `UsbSpeeds.Label(UsbLinkSpeed) -> string`, `UsbSpeeds.PracticalMBps(UsbLinkSpeed) -> double`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/UsbSpeedsTests.cs`:

```csharp
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class UsbSpeedsTests
{
    // Review focus 1: a USB 3 device on a USB 3 port reports Speed=High; the V2 flag is the truth.
    [Fact]
    public void Link_V2FlagWinsOverSpeedField()
    {
        Assert.Equal(UsbLinkSpeed.Super, UsbSpeeds.Link(2, 0b0011));
        Assert.Equal(UsbLinkSpeed.SuperPlus, UsbSpeeds.Link(2, 0b1111));
        Assert.Equal(UsbLinkSpeed.High, UsbSpeeds.Link(2, 0b0010));
        Assert.Equal(UsbLinkSpeed.Full, UsbSpeeds.Link(1, 0));
        Assert.Equal(UsbLinkSpeed.Low, UsbSpeeds.Link(0, null));
        Assert.Equal(UsbLinkSpeed.Super, UsbSpeeds.Link(3, null));
        Assert.Equal(UsbLinkSpeed.Unknown, UsbSpeeds.Link(9, null));
    }

    [Fact]
    public void Capability_FromFlagsElseDescriptor()
    {
        Assert.Equal(UsbLinkSpeed.Super, UsbSpeeds.Capability(0b0010, 0x0210, UsbLinkSpeed.High));
        Assert.Equal(UsbLinkSpeed.SuperPlus, UsbSpeeds.Capability(0b1010, 0x0320, UsbLinkSpeed.Super));
        Assert.Equal(UsbLinkSpeed.High, UsbSpeeds.Capability(0, 0x0200, UsbLinkSpeed.High));
        Assert.Equal(UsbLinkSpeed.Full, UsbSpeeds.Capability(null, 0x0110, UsbLinkSpeed.Full));
        // Never below what the link actually runs at.
        Assert.Equal(UsbLinkSpeed.Super, UsbSpeeds.Capability(null, 0x0200, UsbLinkSpeed.Super));
    }

    [Fact]
    public void LabelsAndCeilings()
    {
        Assert.Equal("USB 2.0 (480 Mb/s)", UsbSpeeds.Label(UsbLinkSpeed.High));
        Assert.Equal("USB 3 (5 Gb/s)", UsbSpeeds.Label(UsbLinkSpeed.Super));
        Assert.Equal("USB 3 (10 Gb/s a víc)", UsbSpeeds.Label(UsbLinkSpeed.SuperPlus));
        Assert.Equal("neznámá", UsbSpeeds.Label(UsbLinkSpeed.Unknown));
        Assert.Equal(40, UsbSpeeds.PracticalMBps(UsbLinkSpeed.High));
        Assert.Equal(450, UsbSpeeds.PracticalMBps(UsbLinkSpeed.Super));
        Assert.Equal(1000, UsbSpeeds.PracticalMBps(UsbLinkSpeed.SuperPlus));
        Assert.Equal(0, UsbSpeeds.PracticalMBps(UsbLinkSpeed.Unknown));
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~UsbSpeedsTests"`
Expected: build FAIL — `UsbSpeeds` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Hardware/UsbSpeeds.cs`:

```csharp
namespace Offload.Core.Hardware;

public enum UsbLinkSpeed { Unknown, Low, Full, High, Super, SuperPlus }

public static class UsbSpeeds
{
    /// The speed the link really runs at. The V2 flags win: a USB 3 device on a USB 3 port still reports
    /// `Speed = High` in CONNECTION_INFORMATION_EX (verified on this PC, WD Elements on port 19).
    public static UsbLinkSpeed Link(byte connectionSpeed, uint? v2Flags)
    {
        uint f = v2Flags ?? 0;
        if ((f & 0b0100) != 0) return UsbLinkSpeed.SuperPlus;
        if ((f & 0b0001) != 0) return UsbLinkSpeed.Super;
        return connectionSpeed switch
        {
            0 => UsbLinkSpeed.Low,
            1 => UsbLinkSpeed.Full,
            2 => UsbLinkSpeed.High,
            3 => UsbLinkSpeed.Super,
            _ => UsbLinkSpeed.Unknown,
        };
    }

    /// What the device could do on a good port and cable — never less than what it runs at.
    public static UsbLinkSpeed Capability(uint? v2Flags, ushort bcdUsb, UsbLinkSpeed link)
    {
        uint f = v2Flags ?? 0;
        var cap = (f & 0b1000) != 0 ? UsbLinkSpeed.SuperPlus
            : (f & 0b0010) != 0 ? UsbLinkSpeed.Super
            : bcdUsb >= 0x0200 ? UsbLinkSpeed.High
            : UsbLinkSpeed.Full;
        return cap > link ? cap : link;
    }

    public static string Label(UsbLinkSpeed s) => s switch
    {
        UsbLinkSpeed.Low => "USB 1.0 (1,5 Mb/s)",
        UsbLinkSpeed.Full => "USB 1.1 (12 Mb/s)",
        UsbLinkSpeed.High => "USB 2.0 (480 Mb/s)",
        UsbLinkSpeed.Super => "USB 3 (5 Gb/s)",
        UsbLinkSpeed.SuperPlus => "USB 3 (10 Gb/s a víc)",
        _ => "neznámá",
    };

    /// Realistic ceiling for a disk or card reader over this link, MB/s (protocol overhead included).
    public static double PracticalMBps(UsbLinkSpeed s) => s switch
    {
        UsbLinkSpeed.Low => 0.15,
        UsbLinkSpeed.Full => 1,
        UsbLinkSpeed.High => 40,
        UsbLinkSpeed.Super => 450,
        UsbLinkSpeed.SuperPlus => 1000,
        _ => 0,
    };
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~UsbSpeedsTests"`
Expected: PASS (3 testy).

- [ ] **Step 5: Commit**

Řádek do VYVOJ (plán HW): `- Úkol 2 — rychlost linky a schopnost zařízení (USB 2.0 / USB 3 5 Gb/s / 10 Gb/s+), oprava pasti „Speed=High“ u USB 3`.

```powershell
git add windows docs
git commit -m "Windows: USB link speed vs device capability" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Topologie USB a fyzické konektory

**Files:**
- Create: `windows/src/Offload.Core/Hardware/UsbTopology.cs`
- Test: `windows/tests/Offload.Core.Tests/UsbTopologyTests.cs`

**Interfaces:**
- Consumes: Task 1 (`DevNodes`, `UsbHubIo`, `RawPort`), Task 2 (`UsbSpeeds`).
- Produces: records `UsbDevice(string InstanceId, ushort Vid, ushort Pid, string Name, UsbLinkSpeed Link, UsbLinkSpeed Capable, UsbHub? Hub)`, `UsbPort(int Number, bool SupportsUsb3, bool UserVisible, bool TypeC, int CompanionPort, UsbDevice? Device)`, `UsbHub(string InstanceId, string Name, IReadOnlyList<UsbPort> Ports)`, `UsbController(string InstanceId, string Name, IReadOnlyList<UsbHub> RootHubs)`, `UsbConnector(string Id, string HubInstanceId, string ControllerName, int Usb2Port, int Usb3Port, bool TypeC, bool UserVisible, UsbDevice? Device)` (+ `SupportsUsb3`, `DefaultLabel`), `UsbTopology(IReadOnlyList<UsbController> Controllers)` (+ `Connectors`); `UsbScanner.Scan() -> UsbTopology`; `Connectors.Build(UsbHub hub, string controllerName) -> List<UsbConnector>` (rekurzivně i do hubů zařízení).

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/UsbTopologyTests.cs`:

```csharp
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class UsbTopologyTests
{
    static UsbDevice Dev(string name, UsbLinkSpeed link, UsbLinkSpeed cap, UsbHub? hub = null) => new($"USB\\{name}", 1, 2, name, link, cap, hub);

    // Root hub like this PC: 2.0 ports 1..3, 3.x ports 17..18 as companions of 1 and 2.
    static UsbHub Root(UsbDevice? onPort1 = null, UsbDevice? onPort17 = null, UsbDevice? onPort3 = null) => new("ROOT", "Root hub",
    [
        new UsbPort(1, false, true, false, 17, onPort1),
        new UsbPort(2, false, true, true, 18, null),
        new UsbPort(3, false, true, false, 0, onPort3),
        new UsbPort(17, true, true, false, 1, onPort17),
        new UsbPort(18, true, true, true, 2, null),
    ]);

    [Fact]
    public void Connectors_PairUsb2AndUsb3Ports()
    {
        var c = Connectors.Build(Root(onPort17: Dev("disk", UsbLinkSpeed.Super, UsbLinkSpeed.Super)), "Intel xHCI");
        Assert.Equal(3, c.Count);
        var a = c.Single(x => x.Usb2Port == 1);
        Assert.Equal(17, a.Usb3Port);
        Assert.True(a.SupportsUsb3);
        Assert.Equal("disk", a.Device!.Name);
        Assert.Equal("ROOT#1", a.Id);
        Assert.Equal("port 1 USB 3", a.DefaultLabel);
        var typeC = c.Single(x => x.Usb2Port == 2);
        Assert.True(typeC.TypeC);
        Assert.Equal("port 2 USB-C", typeC.DefaultLabel);
        var usb2Only = c.Single(x => x.Usb2Port == 3);
        Assert.False(usb2Only.SupportsUsb3);
        Assert.Equal("port 3 jen USB 2.0", usb2Only.DefaultLabel);
    }

    [Fact]
    public void Connectors_DeviceOnUsb2HalfOfUsb3Connector()
    {
        var c = Connectors.Build(Root(onPort1: Dev("disk", UsbLinkSpeed.High, UsbLinkSpeed.Super)), "x");
        var a = c.Single(x => x.Usb2Port == 1);
        Assert.True(a.SupportsUsb3);
        Assert.Equal(UsbLinkSpeed.High, a.Device!.Link);
    }

    // Review focus 4
    [Fact]
    public void Connectors_TolerateMissingCompanionAndDevice()
    {
        var hub = new UsbHub("H", "hub", [new UsbPort(1, false, true, false, 99, null), new UsbPort(20, true, false, false, 0, null)]);
        var c = Connectors.Build(hub, "x");
        Assert.Equal(2, c.Count);
        Assert.Equal(0, c.Single(x => x.Usb2Port == 1).Usb3Port);
        Assert.Equal("port 20 USB 3 (interní)", c.Single(x => x.Usb3Port == 20).DefaultLabel);
    }

    [Fact]
    public void Connectors_RecurseIntoExternalHubs()
    {
        var ext = new UsbHub("EXT", "USB2.0 Hub", [new UsbPort(1, false, true, false, 0, Dev("reader", UsbLinkSpeed.High, UsbLinkSpeed.High))]);
        var c = Connectors.Build(Root(onPort3: Dev("hub", UsbLinkSpeed.High, UsbLinkSpeed.High, ext)), "x");
        Assert.Contains(c, x => x.HubInstanceId == "EXT" && x.Device!.Name == "reader");
    }

    [UsbFact]
    public void Scan_FindsControllersPortsAndDevices()
    {
        var t = UsbScanner.Scan();
        Assert.NotEmpty(t.Controllers);
        Assert.All(t.Controllers, c => Assert.False(string.IsNullOrEmpty(c.Name)));
        Assert.NotEmpty(t.Controllers.SelectMany(c => c.RootHubs).SelectMany(h => h.Ports));
        Assert.NotEmpty(t.Connectors);
        var again = UsbScanner.Scan();
        Assert.Equal(t.Connectors.Count, again.Connectors.Count);
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~UsbTopologyTests"`
Expected: build FAIL — typy neexistují.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Hardware/UsbTopology.cs`:

```csharp
namespace Offload.Core.Hardware;

public sealed record UsbDevice(string InstanceId, ushort Vid, ushort Pid, string Name, UsbLinkSpeed Link, UsbLinkSpeed Capable, UsbHub? Hub);

public sealed record UsbPort(int Number, bool SupportsUsb3, bool UserVisible, bool TypeC, int CompanionPort, UsbDevice? Device);

public sealed record UsbHub(string InstanceId, string Name, IReadOnlyList<UsbPort> Ports);

public sealed record UsbController(string InstanceId, string Name, IReadOnlyList<UsbHub> RootHubs);

/// One physical socket: an xHCI root hub lists it twice — as a USB 2 port and as its USB 3 twin ("companion").
public sealed record UsbConnector(string Id, string HubInstanceId, string ControllerName, int Usb2Port, int Usb3Port,
    bool TypeC, bool UserVisible, UsbDevice? Device)
{
    public bool SupportsUsb3 => Usb3Port > 0;

    public string DefaultLabel =>
        $"port {(Usb2Port > 0 ? Usb2Port : Usb3Port)} {(TypeC ? "USB-C" : SupportsUsb3 ? "USB 3" : "jen USB 2.0")}{(UserVisible ? "" : " (interní)")}";
}

public sealed record UsbTopology(IReadOnlyList<UsbController> Controllers)
{
    public IReadOnlyList<UsbConnector> Connectors =>
        Controllers.SelectMany(c => c.RootHubs.SelectMany(h => Hardware.Connectors.Build(h, c.Name))).ToList();
}

public static class Connectors
{
    public static List<UsbConnector> Build(UsbHub hub, string controllerName)
    {
        var result = new List<UsbConnector>();
        var done = new HashSet<int>();
        foreach (var port in hub.Ports.OrderBy(p => p.SupportsUsb3).ThenBy(p => p.Number))
        {
            if (!done.Add(port.Number)) continue;
            var twin = port.CompanionPort > 0 ? hub.Ports.FirstOrDefault(p => p.Number == port.CompanionPort && p.SupportsUsb3 != port.SupportsUsb3) : null;
            if (twin != null) done.Add(twin.Number);
            var usb2 = port.SupportsUsb3 ? twin : port;
            var usb3 = port.SupportsUsb3 ? port : twin;
            var device = usb3?.Device ?? usb2?.Device;
            int idPort = usb2?.Number ?? usb3!.Number;
            result.Add(new UsbConnector($"{hub.InstanceId}#{idPort}", hub.InstanceId, controllerName, usb2?.Number ?? 0, usb3?.Number ?? 0,
                port.TypeC || (twin?.TypeC ?? false), port.UserVisible || (twin?.UserVisible ?? false), device));
            if (device?.Hub is UsbHub child) result.AddRange(Build(child, controllerName));
        }
        return result;
    }
}

public static class UsbScanner
{
    /// Every USB host controller with its hubs and ports. Never throws; what cannot be read is left out.
    public static UsbTopology Scan()
    {
        var controllers = new List<UsbController>();
        try
        {
            foreach (var iface in DevNodes.Interfaces(UsbNative.HostControllerInterface, null))
            {
                if (DevNodes.InterfaceStr(iface, DevKeys.InstanceId) is not string id || DevNodes.Locate(id) is not uint hc) continue;
                var hubs = DevNodes.Children(hc).Where(DevNodes.IsHub).Select(dn => ReadHub(dn, 0)).OfType<UsbHub>().ToList();
                controllers.Add(new UsbController(id, DevNodes.Name(hc), hubs));
            }
        }
        catch (Exception)
        {
            // Diagnostics are best effort: a device unplugged mid-scan must never take the app down.
        }
        return new UsbTopology(controllers);
    }

    static UsbHub? ReadHub(uint hubNode, int depth)
    {
        if (depth > 6) return null;
        var id = DevNodes.Id(hubNode);
        var paths = DevNodes.Interfaces(UsbNative.HubInterface, id);
        if (paths.Count == 0) return null;
        using var h = UsbHubIo.Open(paths[0]);
        if (h == null || UsbHubIo.HubPortCount(h) is not int count) return new UsbHub(id, DevNodes.Name(hubNode), []);
        var children = DevNodes.Children(hubNode).ToList();
        var ports = new List<UsbPort>();
        for (int n = 1; n <= count; n++)
        {
            var raw = UsbHubIo.QueryPort(h, n);
            UsbDevice? device = null;
            if (raw.Status == 1)
            {
                var link = UsbSpeeds.Link(raw.Speed, raw.V2Flags);
                var cap = UsbSpeeds.Capability(raw.V2Flags, raw.BcdUsb, link);
                uint? child = raw.DriverKey == null ? null
                    : children.Cast<uint?>().FirstOrDefault(c => string.Equals(DevNodes.Str(c!.Value, DevKeys.Driver), raw.DriverKey, StringComparison.OrdinalIgnoreCase));
                if (child is uint c)
                    device = new UsbDevice(DevNodes.Id(c), raw.Vid, raw.Pid, DevNodes.BusName(c), link, cap, raw.IsHub ? ReadHub(c, depth + 1) : null);
                else
                    device = new UsbDevice("", raw.Vid, raw.Pid, $"VID_{raw.Vid:X4}&PID_{raw.Pid:X4}", link, cap, null);
            }
            uint props = raw.Properties ?? 0;
            ports.Add(new UsbPort(n, ((raw.Protocols ?? 0) & 0b100) != 0, (props & 1) != 0, (props & 8) != 0, raw.CompanionPort, device));
        }
        return new UsbHub(id, DevNodes.Name(hubNode), ports);
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~UsbTopologyTests"`
Expected: PASS (5 testů).

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 3 — topologie USB: řadiče, huby, porty, fyzické konektory (USB 2 + USB 3 dvojče), externí huby`.

```powershell
git add windows docs
git commit -m "Windows: USB topology scan and physical connectors" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Kde sedí disk (svazek → USB port)

**Files:**
- Create: `windows/src/Offload.Core/Hardware/DriveLinks.cs`
- Modify: `windows/tests/Offload.Core.Tests/HardwareFacts.cs` (přidat `UsbDriveFactAttribute` z Task 1 Step 1)
- Test: `windows/tests/Offload.Core.Tests/DriveLinksTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3, `NativeIO.VolumeRoot`, `NativeIO.VolumeId`.
- Produces: `enum StorageBus { Unknown, Usb, Sata, Nvme, Sd, Mmc, Scsi, Sas, Raid, Virtual, Network, Other }`; `UsbLink(string ControllerName, string HubName, string ConnectorId, int Port, UsbLinkSpeed Link, UsbLinkSpeed Capable, bool ConnectorSupportsUsb3, bool Uasp)`; `DriveLink(string Root, StorageBus Bus, string Product, UsbLink? Usb)` (+ `Description`); `DriveLinks.Resolve(string path) -> DriveLink` (nikdy nevyhodí); internal `DriveLinks.BusFrom(int) -> StorageBus`.

- [ ] **Step 1: Napsat padající testy**

Do `HardwareFacts.cs` přidat `UsbDriveFactAttribute` (Task 1 Step 1).

`windows/tests/Offload.Core.Tests/DriveLinksTests.cs`:

```csharp
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class DriveLinksTests
{
    [Theory]
    [InlineData(7, StorageBus.Usb)]
    [InlineData(11, StorageBus.Sata)]
    [InlineData(17, StorageBus.Nvme)]
    [InlineData(12, StorageBus.Sd)]
    [InlineData(14, StorageBus.Virtual)]
    [InlineData(0, StorageBus.Unknown)]
    [InlineData(99, StorageBus.Other)]
    public void BusFrom(int raw, StorageBus bus) => Assert.Equal(bus, DriveLinks.BusFrom(raw));

    [Fact]
    public void SystemDrive_IsInternalWithAProductName()
    {
        var link = DriveLinks.Resolve(Path.GetPathRoot(Environment.SystemDirectory)!);
        Assert.NotEqual(StorageBus.Unknown, link.Bus);
        Assert.False(string.IsNullOrWhiteSpace(link.Product));
        Assert.Contains(link.Product, link.Description);
    }

    // Review focus 3
    [Fact]
    public void Resolve_UncAndMissingPath_DoNotThrow()
    {
        Assert.Equal(StorageBus.Network, DriveLinks.Resolve(@"\\server\share\x").Bus);
        var missing = DriveLinks.Resolve(@"Q:\nope");
        Assert.Null(missing.Usb);
    }

    [UsbDriveFact]
    public void UsbDrive_HasPortControllerAndLink()
    {
        var link = DriveLinks.Resolve(UsbDriveFactAttribute.FirstUsbDrive!);
        Assert.Equal(StorageBus.Usb, link.Bus);
        Assert.NotNull(link.Usb);
        Assert.True(link.Usb!.Port > 0);
        Assert.False(string.IsNullOrEmpty(link.Usb.ControllerName));
        Assert.NotEqual(UsbLinkSpeed.Unknown, link.Usb.Link);
        Assert.True(link.Usb.Capable >= link.Usb.Link);
        Assert.Contains(UsbScanner.Scan().Connectors, c => c.Id == link.Usb.ConnectorId);
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~DriveLinksTests"`
Expected: build FAIL — `DriveLinks` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Hardware/DriveLinks.cs`:

```csharp
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core.Hardware;

public enum StorageBus { Unknown, Usb, Sata, Nvme, Sd, Mmc, Scsi, Sas, Raid, Virtual, Network, Other }

public sealed record UsbLink(string ControllerName, string HubName, string ConnectorId, int Port,
    UsbLinkSpeed Link, UsbLinkSpeed Capable, bool ConnectorSupportsUsb3, bool Uasp);

public sealed record DriveLink(string Root, StorageBus Bus, string Product, UsbLink? Usb)
{
    public string Description => Usb is { } u
        ? $"{Product} — {UsbSpeeds.Label(u.Link)}, port {u.Port} ({u.ControllerName})"
        : $"{Product} — {Bus switch { StorageBus.Nvme => "NVMe", StorageBus.Sata => "SATA", StorageBus.Network => "síťový disk", StorageBus.Unknown => "neznámé připojení", _ => Bus.ToString() }}";
}

public static unsafe class DriveLinks
{
    internal static StorageBus BusFrom(int raw) => raw switch
    {
        0 => StorageBus.Unknown,
        1 => StorageBus.Scsi,
        7 => StorageBus.Usb,
        8 => StorageBus.Raid,
        10 => StorageBus.Sas,
        11 => StorageBus.Sata,
        12 => StorageBus.Sd,
        13 => StorageBus.Mmc,
        14 or 15 => StorageBus.Virtual,
        17 => StorageBus.Nvme,
        _ => StorageBus.Other,
    };

    /// Where the volume holding `path` is connected. Never throws: anything unreadable stays Unknown/null.
    public static DriveLink Resolve(string path)
    {
        string root = path;
        try
        {
            root = NativeIO.VolumeRoot(path) ?? Path.GetPathRoot(path) ?? path;
            if (root.StartsWith(@"\\", StringComparison.Ordinal) && !root.StartsWith(@"\\?\", StringComparison.Ordinal))
                return new DriveLink(root, StorageBus.Network, root.TrimEnd('\\'), null);
            var device = root.Length <= 3 ? @"\\.\" + root.TrimEnd('\\') : NativeIO.VolumeId(path)?.TrimEnd('\\');
            if (device == null) return new DriveLink(root, StorageBus.Unknown, "", null);
            using var volume = UsbNative.OpenForQuery(device);
            if (volume == null) return new DriveLink(root, StorageBus.Unknown, "", null);
            var (busRaw, product) = Descriptor(volume);
            var bus = BusFrom(busRaw);
            if (bus != StorageBus.Usb || DiskNumber(volume) is not uint disk) return new DriveLink(root, bus, product, null);
            return new DriveLink(root, bus, product, UsbLinkOf(disk));
        }
        catch (Exception)
        {
            return new DriveLink(root, StorageBus.Unknown, "", null);
        }
    }

    static (int Bus, string Product) Descriptor(SafeFileHandle h)
    {
        var q = new StoragePropertyQuery();  // StorageDeviceProperty, PropertyStandardQuery
        var buf = new byte[1024];
        fixed (byte* p = buf)
            if (!UsbNative.DeviceIoControl(h, UsbNative.IoctlStorageQueryProperty, &q, sizeof(StoragePropertyQuery), p, buf.Length, out _, 0))
                return (0, "");
        string Ascii(int offsetField)
        {
            int o = BitConverter.ToInt32(buf, offsetField);
            if (o <= 0 || o >= buf.Length) return "";
            int e = o;
            while (e < buf.Length && buf[e] != 0) e++;
            return Encoding.ASCII.GetString(buf, o, e - o).Trim();
        }
        var product = string.Join(" ", new[] { Ascii(12), Ascii(16) }.Where(s => s.Length > 0));
        return (BitConverter.ToInt32(buf, 28), product);
    }

    static uint? DiskNumber(SafeFileHandle h)
    {
        StorageDeviceNumber n;
        return UsbNative.DeviceIoControl(h, UsbNative.IoctlStorageGetDeviceNumber, null, 0, &n, sizeof(StorageDeviceNumber), out _, 0)
            ? n.DeviceNumber : null;
    }

    static UsbLink? UsbLinkOf(uint diskNumber)
    {
        string? instance = null;
        foreach (var iface in DevNodes.Interfaces(UsbNative.DiskInterface, null))
        {
            using var h = UsbNative.OpenForQuery(iface);
            if (h != null && DiskNumber(h) == diskNumber) { instance = DevNodes.InterfaceStr(iface, DevKeys.InstanceId); break; }
        }
        if (instance == null || DevNodes.Locate(instance) is not uint diskNode) return null;

        // Walk up to the USB device: the first ancestor whose parent is a hub (BOT: USBSTOR\…, UASP: SCSI\…, composite: &MI_xx).
        uint? usbDevice = null, hub = null;
        for (uint? cur = diskNode; cur is uint c; cur = DevNodes.Parent(c))
        {
            if (DevNodes.Parent(c) is uint parent && DevNodes.IsHub(parent)) { usbDevice = c; hub = parent; break; }
        }
        if (usbDevice is not uint dev || hub is not uint hubNode) return null;
        int port = (int)(DevNodes.UInt(dev, DevKeys.Address) ?? 0);
        string controller = "?";
        for (uint? cur = hubNode; cur is uint c; cur = DevNodes.Parent(c))
            if (DevNodes.IsHostController(c)) { controller = DevNodes.Name(c); break; }
        var hubId = DevNodes.Id(hubNode);
        var paths = DevNodes.Interfaces(UsbNative.HubInterface, hubId);
        if (paths.Count == 0 || port <= 0) return null;
        using var hubHandle = UsbHubIo.Open(paths[0]);
        if (hubHandle == null) return null;
        var raw = UsbHubIo.QueryPort(hubHandle, port);
        var link = UsbSpeeds.Link(raw.Speed, raw.V2Flags);
        bool onUsb3Port = ((raw.Protocols ?? 0) & 0b100) != 0;
        bool connectorUsb3 = onUsb3Port || (raw.CompanionPort > 0 && ((UsbHubIo.QueryPort(hubHandle, raw.CompanionPort).Protocols ?? 0) & 0b100) != 0);
        int connectorPort = onUsb3Port && raw.CompanionPort > 0 ? raw.CompanionPort : port;
        bool uasp = string.Equals(DevNodes.Str(dev, DevKeys.Service), "UASPStor", StringComparison.OrdinalIgnoreCase);
        return new UsbLink(controller, DevNodes.Name(hubNode), $"{hubId}#{connectorPort}", port, link,
            UsbSpeeds.Capability(raw.V2Flags, raw.BcdUsb, link), connectorUsb3, uasp);
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~DriveLinksTests"`
Expected: PASS (10 vč. theory; `UsbDrive_…` běží — E: je na USB).

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 4 — kde sedí disk: sběrnice (USB/SATA/NVMe…), řadič, port, rychlost linky, UASP`.

```powershell
git add windows docs
git commit -m "Windows: map a volume to its bus, USB port and link speed" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Doporučení

**Files:**
- Create: `windows/src/Offload.Core/Hardware/Advisor.cs`
- Test: `windows/tests/Offload.Core.Tests/AdvisorTests.cs`

**Interfaces:**
- Consumes: `DriveLink`, `UsbLink`, `UsbConnector`, `UsbSpeeds`.
- Produces: `enum AdviceKind { Warning, Info, Tip }`; `record Advice(AdviceKind Kind, string Text)`; `record RoleLink(string Role, DriveLink Link)`; `Advisor.Advise(IReadOnlyList<RoleLink> drives, IReadOnlyList<UsbConnector> connectors, IReadOnlyDictionary<string, double>? measuredMBps = null, IReadOnlyDictionary<string, string>? portNames = null) -> List<Advice>` (měření klíčovaná rolí; jména portů klíčovaná `UsbConnector.Id`); `Advisor.PortName(UsbConnector, IReadOnlyDictionary<string,string>?) -> string`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/AdvisorTests.cs`:

```csharp
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class AdvisorTests
{
    const string Hc = "Intel(R) USB 3.20 eXtensible Host Controller";

    static RoleLink Usb(string role, string product, UsbLinkSpeed link, UsbLinkSpeed cap, bool connectorUsb3, int port, string controller = Hc) =>
        new(role, new DriveLink($"{role}:\\", StorageBus.Usb, product, new UsbLink(controller, "Root", $"ROOT#{port}", port, link, cap, connectorUsb3, false)));

    static readonly UsbConnector[] Ports =
    [
        new("ROOT#9", "ROOT", Hc, 9, 0, false, true, null),
        new("ROOT#5", "ROOT", Hc, 5, 20, false, true, null),
        new("ROOT#11", "ROOT", Hc, 11, 26, true, true, null),
        new("ROOT#1", "ROOT", Hc, 1, 17, false, true, new UsbDevice("x", 1, 1, "Keyboard", UsbLinkSpeed.Full, UsbLinkSpeed.Full, null)),
        new("ROOT#14", "ROOT", Hc, 14, 0, false, false, null),
    ];

    [Fact]
    public void Advice_CapableDeviceOnUsb2OnlyPort()
    {
        var a = Advisor.Advise([Usb("Cíl 1", "WD Elements", UsbLinkSpeed.High, UsbLinkSpeed.Super, false, 9)], Ports);
        var w = Assert.Single(a, x => x.Kind == AdviceKind.Warning);
        Assert.Contains("Cíl 1 WD Elements", w.Text);
        Assert.Contains("umí USB 3 (5 Gb/s)", w.Text);
        Assert.Contains("jen USB 2.0", w.Text);
        Assert.Contains("port 5 USB 3", w.Text);
        Assert.Contains("port 11 USB-C", w.Text);
    }

    // Review focus 2
    [Fact]
    public void Advice_CapableDeviceSlowLinkOnUsb3Connector()
    {
        var a = Advisor.Advise([Usb("Karta", "SanDisk reader", UsbLinkSpeed.High, UsbLinkSpeed.Super, true, 5)], Ports);
        var w = Assert.Single(a, x => x.Kind == AdviceKind.Warning);
        Assert.Contains("kabel", w.Text);
        Assert.DoesNotContain("jen USB 2.0", w.Text);
    }

    // Review focus 5
    [Fact]
    public void Advice_SharedController()
    {
        var a = Advisor.Advise(
        [
            Usb("Karta", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5),
            Usb("Cíl 1", "SSD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
        ], Ports);
        Assert.Contains(a, x => x.Kind == AdviceKind.Info && x.Text.Contains("Karta, Cíl 1") && x.Text.Contains("jeden řadič"));
    }

    [Fact]
    public void Advice_PortListsAndNames()
    {
        var names = new Dictionary<string, string> { ["ROOT#5"] = "přední levý" };
        var a = Advisor.Advise([], Ports, portNames: names);
        Assert.Contains(a, x => x.Kind == AdviceKind.Tip && x.Text.StartsWith("Volné rychlé porty: ") && x.Text.Contains("přední levý") && x.Text.Contains("port 11 USB-C"));
        Assert.Contains(a, x => x.Kind == AdviceKind.Tip && x.Text.Contains("port 9 jen USB 2.0") && !x.Text.Contains("port 14"));
    }

    [Fact]
    public void Advice_MeasuredEstimateAndSlowDisk()
    {
        var a = Advisor.Advise(
        [
            Usb("Karta", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5),
            Usb("Cíl 1", "WD Elements", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
            new RoleLink("Cíl 2", new DriveLink("D:\\", StorageBus.Sata, "ST1000LM035", null)),
        ], Ports, new Dictionary<string, double> { ["Karta"] = 300, ["Cíl 1"] = 26, ["Cíl 2"] = 98 });
        Assert.Contains(a, x => x.Kind == AdviceKind.Info && x.Text == "Odhad rychlosti offloadu s tímto zapojením: 26 MB/s (brzdí Cíl 1 WD Elements).");
        Assert.Contains(a, x => x.Text.Contains("Cíl 1 WD Elements") && x.Text.Contains("brzdí disk, ne port"));
    }

    [Fact]
    public void Advice_OrderWarningsFirst()
    {
        var a = Advisor.Advise([Usb("Cíl 1", "WD", UsbLinkSpeed.High, UsbLinkSpeed.Super, false, 9)], Ports);
        Assert.Equal(AdviceKind.Warning, a[0].Kind);
        Assert.Equal(a.OrderBy(x => x.Kind).Select(x => x.Kind), a.Select(x => x.Kind));
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~AdvisorTests"`
Expected: build FAIL — `Advisor` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Hardware/Advisor.cs`:

```csharp
using System.Globalization;

namespace Offload.Core.Hardware;

public enum AdviceKind { Warning, Info, Tip }

public sealed record Advice(AdviceKind Kind, string Text);

/// A drive in its role for this offload ("Karta", "Cíl 1" …).
public sealed record RoleLink(string Role, DriveLink Link);

public static class Advisor
{
    public static string PortName(UsbConnector c, IReadOnlyDictionary<string, string>? names) =>
        names != null && names.TryGetValue(c.Id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : c.DefaultLabel;

    public static List<Advice> Advise(IReadOnlyList<RoleLink> drives, IReadOnlyList<UsbConnector> connectors,
        IReadOnlyDictionary<string, double>? measuredMBps = null, IReadOnlyDictionary<string, string>? portNames = null)
    {
        var advice = new List<Advice>();
        string Who(RoleLink r) => $"{r.Role} {r.Link.Product}".Trim();
        var freeFast = connectors.Where(c => c.UserVisible && c.SupportsUsb3 && c.Device == null).Select(c => PortName(c, portNames)).ToList();
        var slowOnly = connectors.Where(c => c.UserVisible && !c.SupportsUsb3).Select(c => PortName(c, portNames)).ToList();

        foreach (var r in drives)
        {
            if (r.Link.Usb is not { } u || u.Capable <= u.Link) continue;
            if (!u.ConnectorSupportsUsb3)
            {
                var where = freeFast.Count > 0 ? $" — přepoj do USB 3 portu: {string.Join(", ", freeFast)}" : " — přepoj do USB 3 portu";
                advice.Add(new Advice(AdviceKind.Warning, $"{Who(r)}: umí {UsbSpeeds.Label(u.Capable)}, ale je v portu jen USB 2.0 (port {u.Port}){where}."));
            }
            else
            {
                advice.Add(new Advice(AdviceKind.Warning, $"{Who(r)}: umí {UsbSpeeds.Label(u.Capable)}, ale linka běží jako {UsbSpeeds.Label(u.Link)} — pravděpodobně kabel jen pro USB 2.0 nebo špatný kontakt; zkus jiný kabel nebo port."));
            }
        }

        foreach (var group in drives.Where(r => r.Link.Usb != null).GroupBy(r => r.Link.Usb!.ControllerName).Where(g => g.Count() >= 2))
            advice.Add(new Advice(AdviceKind.Info, $"{string.Join(", ", group.Select(r => r.Role))} jdou přes jeden řadič ({group.Key}) — dělí se o jeho kapacitu."));

        if (measuredMBps != null && measuredMBps.Count > 0)
        {
            var measured = drives.Where(r => measuredMBps.ContainsKey(r.Role)).ToList();
            if (measured.Count > 0)
            {
                var slowest = measured.MinBy(r => measuredMBps[r.Role])!;
                advice.Add(new Advice(AdviceKind.Info,
                    $"Odhad rychlosti offloadu s tímto zapojením: {Mb(measuredMBps[slowest.Role])} MB/s (brzdí {Who(slowest)})."));
            }
            foreach (var r in measured)
            {
                if (r.Link.Usb is not { } u || u.Link < UsbLinkSpeed.Super) continue;
                double ceiling = UsbSpeeds.PracticalMBps(u.Link);
                if (measuredMBps[r.Role] < ceiling / 2)
                    advice.Add(new Advice(AdviceKind.Info,
                        $"{Who(r)}: linka zvládne ~{Mb(ceiling)} MB/s, disk sám dává {Mb(measuredMBps[r.Role])} MB/s — brzdí disk, ne port."));
            }
        }

        if (freeFast.Count > 0) advice.Add(new Advice(AdviceKind.Tip, $"Volné rychlé porty: {string.Join(", ", freeFast)}."));
        if (slowOnly.Count > 0) advice.Add(new Advice(AdviceKind.Tip, $"Porty jen USB 2.0 — na offload je nepoužívej (hodí se pro klávesnici, myš): {string.Join(", ", slowOnly)}."));
        return advice.OrderBy(a => a.Kind).ToList();
    }

    static string Mb(double v) => v.ToString("0", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~AdvisorTests"`
Expected: PASS (6 testů).

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 5 — doporučení: zařízení pod svými možnostmi (port vs. kabel), sdílený řadič, volné rychlé a pomalé porty, odhad rychlosti offloadu`.

```powershell
git add windows docs
git commit -m "Windows: hardware advice (ports, cables, shared controller, speed estimate)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Řetěz článků během kopie

**Files:**
- Create: `windows/src/Offload.Core/Hardware/ChainView.cs`
- Test: `windows/tests/Offload.Core.Tests/ChainViewTests.cs`

**Interfaces:**
- Consumes: `RoleLink`, `DriveLink`, `UsbSpeeds`.
- Produces: `record ChainElement(string Role, string Product, string Connection, double? LinkMBps, double MBps, string? Controller)` (+ `Utilization` = MBps/LinkMBps); `ChainView.Build(IReadOnlyList<RoleLink> links, IReadOnlyList<double> mbps) -> List<ChainElement>`; `ChainView.Bottleneck(IReadOnlyList<ChainElement>) -> string?`; `ChainView.Lines(IReadOnlyList<ChainElement>) -> List<string>`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/ChainViewTests.cs`:

```csharp
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class ChainViewTests
{
    const string Hc = "Intel xHCI";

    static RoleLink Usb(string role, string product, UsbLinkSpeed link) =>
        new(role, new DriveLink("x:\\", StorageBus.Usb, product, new UsbLink(Hc, "Root", "R#1", 5, link, link, true, false)));

    static RoleLink Sata(string role) => new(role, new DriveLink("D:\\", StorageBus.Sata, "ST1000", null));

    [Fact]
    public void Build_UtilizationAndConnection()
    {
        var c = ChainView.Build([Usb("Karta", "reader", UsbLinkSpeed.Super), Sata("Cíl 1")], [225, 98]);
        Assert.Equal(0.5, c[0].Utilization!.Value, 2);
        Assert.Equal("USB 3 (5 Gb/s), port 5", c[0].Connection);
        Assert.Null(c[1].Utilization);
        Assert.Equal("SATA", c[1].Connection);
    }

    // Review focus 5
    [Fact]
    public void Bottleneck_SlowestElement_FullLinkSaysSo()
    {
        Assert.Equal("Cíl 1 ST1000", ChainView.Bottleneck(ChainView.Build([Usb("Karta", "reader", UsbLinkSpeed.Super), Sata("Cíl 1")], [300, 98])));
        Assert.Equal("Karta reader — linka je plná (USB 2.0 (480 Mb/s))",
            ChainView.Bottleneck(ChainView.Build([Usb("Karta", "reader", UsbLinkSpeed.High), Sata("Cíl 1")], [38, 98])));
        Assert.Null(ChainView.Bottleneck(ChainView.Build([Sata("Cíl 1")], [0])));
    }

    [Fact]
    public void Lines_IncludeControllerSum()
    {
        var lines = ChainView.Lines(ChainView.Build([Usb("Karta", "reader", UsbLinkSpeed.Super), Usb("Cíl 1", "SSD", UsbLinkSpeed.Super)], [300, 290]));
        Assert.Contains(lines, l => l.StartsWith("Karta") && l.Contains("300 MB/s") && l.Contains("67 % linky"));
        Assert.Contains(lines, l => l == "Řadič Intel xHCI: 590 MB/s");
        Assert.Contains(lines, l => l.StartsWith("Úzké hrdlo: "));
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~ChainViewTests"`
Expected: build FAIL — `ChainView` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Hardware/ChainView.cs`:

```csharp
using System.Globalization;

namespace Offload.Core.Hardware;

public sealed record ChainElement(string Role, string Product, string Connection, double? LinkMBps, double MBps, string? Controller)
{
    public double? Utilization => LinkMBps is double l && l > 0 ? MBps / l : null;
}

/// The chain card → reader/link → controller → disks with live throughput, and what is holding it back.
public static class ChainView
{
    public static List<ChainElement> Build(IReadOnlyList<RoleLink> links, IReadOnlyList<double> mbps) =>
        links.Select((r, i) =>
        {
            var u = r.Link.Usb;
            var connection = u != null
                ? $"{UsbSpeeds.Label(u.Link)}, port {u.Port}"
                : r.Link.Bus switch { StorageBus.Sata => "SATA", StorageBus.Nvme => "NVMe", StorageBus.Network => "síť", _ => r.Link.Bus.ToString() };
            double? ceiling = u != null ? UsbSpeeds.PracticalMBps(u.Link) : null;
            return new ChainElement(r.Role, r.Link.Product, connection, ceiling, i < mbps.Count ? mbps[i] : 0, u?.ControllerName);
        }).ToList();

    /// The element that sets the pace: the slowest one moving data; "linka je plná" when it runs at > 85 % of its link.
    public static string? Bottleneck(IReadOnlyList<ChainElement> chain)
    {
        var active = chain.Where(e => e.MBps > 0).ToList();
        if (active.Count == 0) return null;
        var slowest = active.MinBy(e => e.MBps)!;
        var who = $"{slowest.Role} {slowest.Product}".Trim();
        return slowest.Utilization > 0.85 ? $"{who} — linka je plná ({slowest.Connection.Split(", port")[0]})" : who;
    }

    public static List<string> Lines(IReadOnlyList<ChainElement> chain)
    {
        var lines = chain.Select(e =>
        {
            var util = e.Utilization is double u ? $"  {(u * 100).ToString("0", CultureInfo.InvariantCulture)} % linky" : "";
            return $"{e.Role} {e.Product} — {e.Connection}  {e.MBps.ToString("0", CultureInfo.InvariantCulture)} MB/s{util}";
        }).ToList();
        foreach (var g in chain.Where(e => e.Controller != null).GroupBy(e => e.Controller!).Where(g => g.Count() >= 2))
            lines.Add($"Řadič {g.Key}: {g.Sum(e => e.MBps).ToString("0", CultureInfo.InvariantCulture)} MB/s");
        if (Bottleneck(chain) is string b) lines.Add($"Úzké hrdlo: {b}");
        return lines;
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~ChainViewTests"`
Expected: PASS (3 testy).

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 6 — řetěz článků během kopie: rychlost, % linky, součet na řadiči, úzké hrdlo`.

```powershell
git add windows docs
git commit -m "Windows: live hardware chain with bottleneck" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Test HW (rychlý + měření) a uložení výsledku

**Files:**
- Create: `windows/src/Offload.Core/Hardware/HwTest.cs`
- Test: `windows/tests/Offload.Core.Tests/HwTestTests.cs`

**Interfaces:**
- Consumes: `UsbScanner`, `DriveLinks`, `Advisor`, `Bench.Read`, `Bench.Write`.
- Produces: `record DriveMeasurement(string Role, string Root, double? ReadMBps, double? WriteMBps)`; `record HwTestResult(DateTime Time, IReadOnlyList<RoleLink> Drives, IReadOnlyList<UsbConnector> Connectors, IReadOnlyList<DriveMeasurement> Measurements, IReadOnlyList<Advice> Advice)`; `HwTest.Run(IReadOnlyList<string> cards, IReadOnlyList<string> destinations, bool measure, long bytesPerDevice = 1_000_000_000, IReadOnlyDictionary<string,string>? portNames = null) -> HwTestResult`; `HwTest.DefaultPath`; `HwTest.Save(HwTestResult, string path)`; `HwTest.Load(string path) -> HwTestResult?`; internal `HwTest.Roles(cards, destinations) -> List<(string Role, string Root)>`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/HwTestTests.cs`:

```csharp
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class HwTestTests
{
    [Fact]
    public void Roles_CardsThenDestinations_OtherDrivesNotIncluded()
    {
        Assert.Equal([("Karta", @"E:\"), ("Cíl 1", @"D:\a"), ("Cíl 2", @"F:\")], HwTest.Roles([@"E:\"], [@"D:\a", @"F:\"]));
        Assert.Equal([("Karta 1", @"E:\"), ("Karta 2", @"G:\")], HwTest.Roles([@"E:\", @"G:\"], []));
    }

    [Fact]
    public void QuickTest_NoMeasurement_NothingWritten()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var dest = dir.Sub("dest");
        var r = HwTest.Run([card], [dest], measure: false);
        Assert.Equal(2, r.Drives.Count);
        Assert.Empty(r.Measurements);
        Assert.Empty(Directory.GetFiles(dest));
    }

    [Fact]
    public void Measure_ReadsCardWritesDestinations_AndCleansUp()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestFs.WriteRandom(Path.Combine(card, "clip.bin"), 4 << 20, 1);
        var dest = dir.Sub("dest");
        var r = HwTest.Run([card], [dest], measure: true, bytesPerDevice: 8 << 20);
        var m = r.Measurements.ToDictionary(x => x.Role);
        Assert.True(m["Karta"].ReadMBps > 0);
        Assert.Null(m["Karta"].WriteMBps);
        Assert.True(m["Cíl 1"].WriteMBps > 0);
        Assert.Empty(Directory.GetFiles(dest, "*", SearchOption.AllDirectories));
        Assert.Contains(r.Advice, a => a.Text.StartsWith("Odhad rychlosti offloadu"));
    }

    [Fact]
    public void SaveLoad_RoundTrip()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "hwtest.json");
        var r = HwTest.Run([dir.Sub("card")], [dir.Sub("dest")], measure: false);
        HwTest.Save(r, path);
        var back = HwTest.Load(path);
        Assert.NotNull(back);
        Assert.Equal(r.Drives.Count, back!.Drives.Count);
        Assert.Equal(r.Connectors.Count, back.Connectors.Count);
        Assert.Null(HwTest.Load(Path.Combine(dir.Path, "missing.json")));
    }
}
```

- [ ] **Step 2: Spustit, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~HwTestTests"`
Expected: build FAIL — `HwTest` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Hardware/HwTest.cs`:

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core.Hardware;

public sealed record DriveMeasurement(string Role, string Root, double? ReadMBps, double? WriteMBps);

public sealed record HwTestResult(DateTime Time, IReadOnlyList<RoleLink> Drives, IReadOnlyList<UsbConnector> Connectors,
    IReadOnlyList<DriveMeasurement> Measurements, IReadOnlyList<Advice> Advice);

/// The start-up hardware check: topology and links in a moment; with `measure`, a short read of each card and
/// write + read of each destination (a temporary file, deleted). Nothing is ever written to a card.
public static class HwTest
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Offload", "hwtest.json");

    internal static List<(string Role, string Root)> Roles(IReadOnlyList<string> cards, IReadOnlyList<string> destinations)
    {
        var roles = cards.Select((c, i) => (cards.Count == 1 ? "Karta" : $"Karta {i + 1}", c)).ToList();
        roles.AddRange(destinations.Select((d, i) => ($"Cíl {i + 1}", d)));
        return roles;
    }

    public static HwTestResult Run(IReadOnlyList<string> cards, IReadOnlyList<string> destinations, bool measure,
        long bytesPerDevice = 1_000_000_000, IReadOnlyDictionary<string, string>? portNames = null)
    {
        var roles = Roles(cards, destinations);
        var drives = roles.Select(r => new RoleLink(r.Role, DriveLinks.Resolve(r.Root))).ToList();
        var connectors = UsbScanner.Scan().Connectors;
        var measurements = new List<DriveMeasurement>();
        if (measure)
        {
            foreach (var (role, root) in roles)
            {
                bool isCard = role.StartsWith("Karta", StringComparison.Ordinal);
                try
                {
                    if (isCard)
                    {
                        var line = Bench.Read(root, bytesPerDevice, [16 << 20])[0];
                        measurements.Add(new DriveMeasurement(role, root, line.Bytes / line.Seconds / 1e6, null));
                    }
                    else
                    {
                        var lines = Bench.Write(root, bytesPerDevice, 16 << 20);
                        measurements.Add(new DriveMeasurement(role, root, lines[1].Bytes / lines[1].Seconds / 1e6, lines[0].Bytes / lines[0].Seconds / 1e6));
                    }
                }
                catch (Exception)
                {
                    measurements.Add(new DriveMeasurement(role, root, null, null));
                }
            }
        }
        // The offload pace: cards are read, destinations written.
        var pace = measurements
            .Select(m => (m.Role, Value: m.Role.StartsWith("Karta", StringComparison.Ordinal) ? m.ReadMBps : m.WriteMBps))
            .Where(x => x.Value is > 0)
            .ToDictionary(x => x.Role, x => x.Value!.Value);
        var advice = Advisor.Advise(drives, connectors, pace.Count > 0 ? pace : null, portNames);
        return new HwTestResult(DateTime.UtcNow, drives, connectors, measurements, advice);
    }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Save(HwTestResult result, string path)
    {
        NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
        NativeIO.WriteFileDurably(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, Json)));
    }

    public static HwTestResult? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<HwTestResult>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~HwTestTests"`
Expected: PASS (4 testy).

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 7 — test HW: rychlý (topologie, linky) a s měřením (čtení karty, zápis + čtení cílů), uložení výsledku`.

```powershell
git add windows docs
git commit -m "Windows: hardware test with optional measurement and saved result" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: CLI `offload ports` a řetěz v `offload copy`

**Files:**
- Create: `windows/src/Offload.Cli/PortsCommand.cs`
- Modify: `windows/src/Offload.Cli/Program.cs`, `CopyCommand.cs`, `ProgressDisplay.cs`
- Modify: `windows/README.md`, `docs/ROZDILY.md`, `docs/VYVOJ-WINDOWS.md`

**Interfaces:**
- Consumes: `HwTest`, `DriveLinks`, `UsbScanner`, `Advisor`, `ChainView`, `CliPath.Full`.
- Produces: `offload ports [--measure] [--card <path> …] [--dest <path> …] [--gb <n>]`; v `offload copy` po výpisu cílů blok „Připojení:“ (řetěz); stavový řádek kopie končí `| brzdí: <článek>`.

- [ ] **Step 1: PortsCommand**

`windows/src/Offload.Cli/PortsCommand.cs`:

```csharp
using System.CommandLine;
using System.Globalization;
using Offload.Core.Hardware;

namespace Offload.Cli;

static class PortsCommand
{
    public static Command Create()
    {
        var measure = new Option<bool>("--measure") { Description = "Also measure card read and destination write/read speed (temporary file, deleted)." };
        var card = new Option<string[]>("--card") { Description = "Card or reader to check (repeatable).", DefaultValueFactory = _ => [] };
        var dest = new Option<string[]>("--dest") { Description = "Destination to check (repeatable).", DefaultValueFactory = _ => [] };
        var gb = new Option<double>("--gb") { Description = "Amount of data per device when measuring, in GB.", DefaultValueFactory = _ => 1, CustomParser = a => CliParse.Parse(a) ?? 1 };
        var cmd = new Command("ports", "Show USB ports, where cards and disks are connected and how fast, with advice.");
        foreach (var o in new Option[] { measure, card, dest, gb }) cmd.Options.Add(o);
        cmd.SetAction(r =>
        {
            try
            {
                var cards = (r.GetValue(card) ?? []).Select(CliPath.Full).ToList();
                var dests = (r.GetValue(dest) ?? []).Select(CliPath.Full).ToList();
                if (cards.Count == 0 && dests.Count == 0)
                {
                    // No roles given: every ready drive, as "Disk X:".
                    dests = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
                        .Select(d => d.RootDirectory.FullName).ToList();
                    if (r.GetValue(measure)) throw new ArgumentException("--measure needs --card and/or --dest");
                }
                var result = HwTest.Run(cards, dests, r.GetValue(measure), (long)(r.GetValue(gb) * 1_000_000_000));
                Print(result);
                HwTest.Save(result, HwTest.DefaultPath);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
        });
        return cmd;
    }

    static void Print(HwTestResult r)
    {
        Console.WriteLine("Připojení:");
        foreach (var d in r.Drives) Console.WriteLine($"  {d.Role,-8} {d.Link.Root,-6} {d.Link.Description}");
        Console.WriteLine();
        Console.WriteLine("USB porty:");
        foreach (var c in r.Connectors.Where(c => c.UserVisible))
            Console.WriteLine($"  {c.DefaultLabel,-22} {(c.Device is { } dev ? $"{dev.Name} — {UsbSpeeds.Label(dev.Link)}" : "volný")}");
        if (r.Measurements.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Měření:");
            foreach (var m in r.Measurements)
                Console.WriteLine($"  {m.Role,-8} {m.Root,-6} čtení {Mb(m.ReadMBps)}  zápis {Mb(m.WriteMBps)}");
        }
        Console.WriteLine();
        Console.WriteLine("Doporučení:");
        foreach (var a in r.Advice) Console.WriteLine($"  {(a.Kind == AdviceKind.Warning ? "!" : a.Kind == AdviceKind.Info ? "i" : "·")} {a.Text}");
    }

    static string Mb(double? v) => v is double x ? x.ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "—";
}
```

V `Program.cs`: do `Commands` přidat `"ports"` a `root.Subcommands.Add(PortsCommand.Create());`.

- [ ] **Step 2: Řetěz v `offload copy`**

V `CopyCommand.Run` po výpisu cílů (`→ …`) a před `if (dryRun) return 0;`:

```csharp
        var chainLinks = new List<RoleLink> { new("Karta", DriveLinks.Resolve(plan.Source)) };
        chainLinks.AddRange(plan.DestRoots.Select((d, i) => new RoleLink($"Cíl {i + 1}", DriveLinks.Resolve(d))));
        Console.WriteLine("  Připojení:");
        foreach (var l in chainLinks) Console.WriteLine($"    {l.Role}: {l.Link.Description}");
        foreach (var a in Advisor.Advise(chainLinks, UsbScanner.Scan().Connectors).Where(a => a.Kind == AdviceKind.Warning))
            Console.WriteLine($"  ! {a.Text}");
```

(`using Offload.Core.Hardware;` nahoru.) `ProgressDisplay` vytvářet s `chainLinks`: `using (new ProgressDisplay(job, chainLinks))`.

V `ProgressDisplay`: konstruktor `public ProgressDisplay(OffloadJob job, IReadOnlyList<RoleLink>? chain = null)` (uložit do pole `chain`); ve větvi `case JobPhase.Copying:` po sestavení `line` (před `break`):

```csharp
                        if (chain != null && chain.Count == 1 + s.WrittenBytes.Length)
                        {
                            var rates = new List<double> { (s.ReadBytes - lastRead) / dt / 1_000_000 };
                            rates.AddRange(s.WrittenBytes.Select((b, i) => (b - lastWritten[i]) / dt / 1_000_000));
                            if (ChainView.Bottleneck(ChainView.Build(chain, rates)) is string slow) line += $"  | brzdí: {slow}";
                        }
```

- [ ] **Step 3: Sestavit a ručně ověřit na tomto PC**

```powershell
dotnet build
dotnet test
$exe = "src\Offload.Cli\bin\Debug\net10.0-windows\offload.exe"
& $exe ports; "exit $LASTEXITCODE"
& $exe ports --measure --dest E:\ --dest D:\ --gb 0.5; "exit $LASTEXITCODE"
& $exe ports --measure; "exit $LASTEXITCODE"
```

Expected:
- `ports`: blok „Připojení:“ s `E:\ … Elements 25A2 — USB 3 (5 Gb/s), port 19 (Intel(R) USB 3.20 …)` (pokud uživatel nechal disk v USB 3 portu; v portu 9 → `USB 2.0 (480 Mb/s), port 9` a varování „je v portu jen USB 2.0“), `C:\` NVMe, `D:\` SATA; seznam USB portů (volné / co v nich je); doporučení s volnými rychlými porty a porty jen USB 2.0; exit 0; vznikne `%APPDATA%\Offload\hwtest.json`.
- `--measure --dest E:\ --dest D:\`: měření zápis/čtení obou, odhad „Odhad rychlosti offloadu … (brzdí Cíl 1 … )“, u E: případně „brzdí disk, ne port“; na E:\ ani D:\ nezůstane `.offload-bench-*`; exit 0.
- `--measure` bez rolí: `Error: --measure needs --card and/or --dest`, exit 1.

Výstupy vložit do reportu.

- [ ] **Step 4: Dokumentace**

- `windows/README.md`: Stav — řádek HW diagnostiky `hotovo (CLI \`offload ports\`); dotaz při startu a sekce v GUI v plánu 4`; do Použití: `offload ports                                 # porty, kde a jak rychle jsou disky připojené, doporučení` a `offload ports --measure --card E:\ --dest D:\   # + změřit skutečnou rychlost`.
- `docs/ROZDILY.md`: řádky „Test USB portů…“ a „Propustnost článků…“ ve sloupci Windows → `✅ CLI (GUI v plánu 4)`; do „Záměrné rozdíly“: „10 vs 20 Gb/s se nerozlišuje (Windows to bez neověřeného IOCTL nehlásí) — zobrazuje se „10 Gb/s a víc“; fyzickou polohu portu Windows neznají spolehlivě, porty pojmenuje uživatel.“
- `docs/VYVOJ-WINDOWS.md`: tabulka plánů — HW `hotovo (CLI)`; nahoru do seznamu plánu HW: `- Úkol 8 — CLI \`offload ports [--measure]\`, řetěz připojení a úzké hrdlo v \`offload copy\``.

- [ ] **Step 5: Commit**

```powershell
git add windows docs
git commit -m "Windows: offload ports command, connection chain and bottleneck in copy" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
