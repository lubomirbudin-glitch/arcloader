import DiskArbitration
import Foundation

extension DriveLinks {
    static let networkFilesystems: Set<String> = ["smbfs", "afpfs", "nfs", "webdav", "ftp", "cifs"]

    /// Where the volume holding `path` is connected. Never throws: anything unreadable stays unknown/nil.
    public static func resolve(_ path: String) -> DriveLink {
        var fs = statfs()
        guard statfs(path, &fs) == 0 else { return DriveLink(root: path, bus: .unknown, product: "", usb: nil) }
        let root = cString(fs.f_mntonname), from = cString(fs.f_mntfromname), type = cString(fs.f_fstypename)
        if networkFilesystems.contains(type) { return network(root, from) }
        guard from.hasPrefix("/dev/"), let session = DASessionCreate(kCFAllocatorDefault),
              let disk = DADiskCreateFromBSDName(kCFAllocatorDefault, session, String(from.dropFirst(5))) else {
            return DriveLink(root: root, bus: .unknown, product: "", usb: nil)
        }
        let desc = (DADiskCopyDescription(disk) as? [String: Any]) ?? [:]
        func str(_ key: CFString) -> String? { (desc[key as String] as? String)?.trimmingCharacters(in: .whitespaces) }
        let product = [str(kDADiskDescriptionDeviceVendorKey), str(kDADiskDescriptionDeviceModelKey)]
            .compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " ")
        // Up from the media: through APFS (volume → container → physical store → disk → driver → device).
        let chain = IOEntry(DADiskCopyIOMedia(disk))?.ancestors ?? []
        let bus = busFrom(protocol: str(kDADiskDescriptionDeviceProtocolKey), classes: chain.map(\.className),
                          hasUsbDevice: chain.contains(where: UsbScanner.isDevice))
        guard bus == .usb, let usb = usbLink(chain) else { return DriveLink(root: root, bus: bus, product: product, usb: nil) }
        return DriveLink(root: root, bus: bus, product: product, usb: usb)
    }

    /// From the DiskArbitration protocol and the registry classes between the media and the root.
    static func busFrom(protocol p: String?, classes: [String], hasUsbDevice: Bool) -> StorageBus {
        if hasUsbDevice { return .usb }
        if classes.contains(where: { $0.contains("Thunderbolt") }) { return .thunderbolt }
        switch (p ?? "").lowercased() {
        case "usb": return .usb
        case "sata", "serial ata", "ata": return .sata
        case "secure digital": return .sd
        case "pci-express", "pci", "apple fabric", "nvme":
            return classes.contains { $0.contains("NVMe") } ? .nvme : .other
        case "virtual interface", "disk image": return .virtual
        case "sas": return .sas
        case "scsi", "parallel scsi": return .scsi
        case "": return .unknown
        default: return .other
        }
    }

    /// The USB device above the media, its port, hubs on the way and the controller.
    static func usbLink(_ chain: [IOEntry]) -> UsbLink? {
        guard let di = chain.firstIndex(where: UsbScanner.isDevice), di + 2 < chain.count else { return nil }
        let device = chain[di]
        let port = chain[di + 1]
        guard UsbScanner.isPort(port) else { return nil }
        // The port's owner (for its id and its sibling ports): the first device or controller above the port —
        // a hub's ports may hang under a hub driver (AppleUSB30Hub…) between the port and the hub device (spec 9).
        guard let oi = chain[(di + 2)...].firstIndex(where: { UsbScanner.isDevice($0) || UsbScanner.isController($0) }) else {
            return nil
        }
        let owner = chain[oi]
        let uasp = chain[..<di].contains { $0.className.contains(UsbScanner.Keys.uasClassFragment) }
        var hubs: [UsbHubRef] = []
        var controller: IOEntry?
        for e in chain[oi...] {
            if UsbScanner.isController(e) {
                controller = e
                break
            }
            if UsbScanner.isDevice(e) { hubs.append(UsbHubRef(instanceId: UsbScanner.deviceId(e), name: UsbScanner.productName(e))) }
        }
        guard let controller else { return nil }
        // Listed where the scan lists it: a USB 2 root port whose USB 3 twin is on another controller belongs to that
        // controller's root hub (the traffic still goes through `controller`, the one above the device).
        let listed = UsbScanner.listedOwner(of: port) ?? owner
        let hubId = UsbScanner.isController(listed) ? listed.path : UsbScanner.deviceId(listed)
        let raws = UsbScanner.rawPorts(of: listed)
        // The number as the scan made it (unique per owner), found by the port's registry entry.
        let portId = port.entryId
        let number = raws.first { $0.entry.entryId == portId }?.raw.number ?? port.int(UsbScanner.Keys.portNumber) ?? 0
        let paired = UsbScanner.pair(raws.map(\.raw)).first { $0.number == number }
        let kind = UsbScanner.portKind(port)
        let companion = paired?.companion ?? 0
        let link = UsbScanner.linkSpeed(device)
        // The same pairing as the port list: a paired or USB 3 port is a USB 3 socket, named by its USB 2 twin — also
        // a port on the USB 2 half of an external USB 3 hub (its twin is on the USB 3 half).
        let paired3 = kind == true || companion > 0 || UsbScanner.twinPort(of: port) != nil
        let connectorUsb3: Bool? = paired3 || link >= .superSpeed ? true : (paired?.usb3 ?? kind)
        let connectorPort = kind == true && companion > 0 ? companion : number
        return UsbLink(controllerId: controller.path, controllerName: UsbScanner.controllerName(controller),
                       connectorId: "\(hubId)#\(number)", connectorPort: connectorPort, port: number, link: link,
                       capable: UsbSpeeds.capability(bcdUSB: device.int(UsbScanner.Keys.bcdUSB), link: link),
                       connectorUsb3: connectorUsb3, uasp: uasp, hubs: hubs)
    }

    static func cString<T>(_ tuple: T) -> String {
        withUnsafeBytes(of: tuple) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
    }
}
