import Foundation
import IOKit

/// An IOKit registry object owned by Swift: released when this goes away. Reads only the IOService plane.
final class IOEntry {
    let raw: io_object_t

    init?(_ raw: io_object_t) {
        guard raw != 0 else { return nil }
        self.raw = raw
    }

    deinit { IOObjectRelease(raw) }

    func property(_ key: String) -> Any? {
        IORegistryEntryCreateCFProperty(raw, key as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue()
    }

    /// A number, or little-endian Data such as "port" = <01000000>.
    func int(_ key: String) -> Int? {
        switch property(key) {
        case let n as NSNumber:
            return n.intValue
        case let d as Data where !d.isEmpty:
            var v = 0
            for (i, b) in d.prefix(8).enumerated() { v |= Int(b) << (8 * i) }
            return v
        default:
            return nil
        }
    }

    func uint64(_ key: String) -> UInt64? { (property(key) as? NSNumber)?.uint64Value }

    func string(_ key: String) -> String? {
        switch property(key) {
        case let s as String:
            return s
        case let d as Data:
            return String(data: d, encoding: .utf8)?.trimmingCharacters(in: CharacterSet.controlCharacters.union(.whitespaces))
        default:
            return nil
        }
    }

    var className: String { IOObjectCopyClass(raw).map { $0.takeRetainedValue() as String } ?? "" }

    func conforms(to cls: String) -> Bool { IOObjectConformsTo(raw, cls) != 0 }

    var name: String {
        var buf = [CChar](repeating: 0, count: 128)
        return IORegistryEntryGetName(raw, &buf) == KERN_SUCCESS ? String(cString: buf) : ""
    }

    /// Stable across restarts (unlike the registry entry id) — used as a controller's identity.
    var path: String { IORegistryEntryCopyPath(raw, kIOServicePlane).map { $0.takeRetainedValue() as String } ?? "" }

    /// Unique while the entry exists — compares two IOEntry objects for the same registry entry.
    var entryId: UInt64 {
        var id: UInt64 = 0
        return IORegistryEntryGetRegistryEntryID(raw, &id) == KERN_SUCCESS ? id : 0
    }

    /// The entry at a registry path such as "IOService:/AppleACPIPlatformExpert/…/HS03@14300000"; nil if gone.
    static func fromPath(_ path: String) -> IOEntry? {
        IOEntry(IORegistryEntryFromPath(kIOMainPortDefault, path))
    }

    var parent: IOEntry? {
        var p: io_registry_entry_t = 0
        return IORegistryEntryGetParentEntry(raw, kIOServicePlane, &p) == KERN_SUCCESS ? IOEntry(p) : nil
    }

    var children: [IOEntry] {
        var it: io_iterator_t = 0
        guard IORegistryEntryGetChildIterator(raw, kIOServicePlane, &it) == KERN_SUCCESS else { return [] }
        defer { IOObjectRelease(it) }
        var out: [IOEntry] = []
        while let e = IOEntry(IOIteratorNext(it)) { out.append(e) }
        return out
    }

    /// This entry, then each parent up to the root of the service plane.
    var ancestors: [IOEntry] {
        var out = [self]
        var cur = parent
        while let c = cur, out.count < 64 {
            out.append(c)
            cur = c.parent
        }
        return out
    }

    static func matching(_ className: String) -> [IOEntry] {
        var it: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching(className), &it) == KERN_SUCCESS else { return [] }
        defer { IOObjectRelease(it) }
        var out: [IOEntry] = []
        while let e = IOEntry(IOIteratorNext(it)) { out.append(e) }
        return out
    }
}
