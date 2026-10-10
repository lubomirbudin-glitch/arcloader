import Darwin
import Foundation

/// How a card volume is recognised again (Windows R1): the file system's serial number plus the capacity.
public struct CardKey: Hashable, Codable {
    public let fsSerial: String
    public let capacity: Int64
}

/// The card volume behind a source that is a volume root, as history stores it. Touches the disk — never on the main thread.
public struct CardVolume: Equatable {
    public let identity: String?
    public let fsSerial: String
    public let capacity: Int64
    public let label: String?
    public let filesystem: String
    public var key: CardKey { CardKey(fsSerial: fsSerial, capacity: capacity) }

    /// Nil for a folder (not a volume root) or a volume that cannot be read.
    public static func of(_ source: String) -> CardVolume? {
        // The real path, as statfs names the mount point ("/private/tmp/…" for "/tmp/…").
        let url = URL(fileURLWithPath: VolumeFacts.realPath(source))
        guard let m = VolumeFacts.mount(of: url.path), m.mountPoint == url.path.trimmingTrailingSlash(),
              let serial = VolumeFacts.fsSerial(m.mountPoint), let capacity = VolumeFacts.capacity(m.mountPoint) else { return nil }
        let label = (try? url.resourceValues(forKeys: [.volumeNameKey]))?.volumeName
        return CardVolume(identity: CardIdentity.of(source), fsSerial: serial, capacity: capacity, label: label,
                          filesystem: VolumeFacts.filesystemName(m.mountPoint))
    }
}

extension String {
    func trimmingTrailingSlash() -> String {
        var s = self
        while s.count > 1 && s.hasSuffix("/") { s.removeLast() }
        return s
    }
}

/// The first sector of a FAT/exFAT/NTFS volume: its serial number as Windows reports it.
enum BootSector {
    static func serial(_ s: [UInt8]) -> (serial: UInt32, kind: String)? {
        guard s.count >= 0x70 else { return nil }
        func text(_ at: Int, _ n: Int) -> String { String(decoding: s[at..<(at + n)], as: UTF8.self) }
        func le32(_ at: Int) -> UInt32 { (0..<4).reduce(UInt32(0)) { $0 | UInt32(s[at + $1]) << (8 * UInt32($1)) } }
        if text(3, 8) == "EXFAT   " { return (le32(0x64), "exFAT") }
        if text(3, 8) == "NTFS    " { return (le32(0x48), "NTFS") }
        if text(0x52, 8).hasPrefix("FAT32"), s[0x42] == 0x29 { return (le32(0x43), "FAT32") }
        if text(0x36, 8).hasPrefix("FAT"), s[0x26] == 0x29 { return (le32(0x27), "FAT") }
        return nil
    }

    /// 4096 bytes from the start of the raw device ("/dev/disk4s1" → "/dev/rdisk4s1"), read only. Nil when it cannot be
    /// opened (EACCES: the device node is root's — spec D.27) or read.
    static func read(device: String) -> [UInt8]? {
        let raw = device.hasPrefix("/dev/disk") ? "/dev/r" + device.dropFirst("/dev/".count) : device
        let fd = open(raw, O_RDONLY)
        guard fd >= 0 else { return nil }
        defer { close(fd) }
        _ = fcntl(fd, F_NOCACHE, 1)
        var buf = [UInt8](repeating: 0, count: 4096)
        let n = buf.withUnsafeMutableBytes { Darwin.pread(fd, $0.baseAddress, 4096, 0) }
        return n >= 512 ? Array(buf[0..<n]) : nil
    }
}

enum VolumeFacts {
    struct Mount { let mountPoint: String; let device: String; let fsType: String; let local: Bool }

    static func mount(of path: String) -> Mount? {
        var fs = statfs()
        guard statfs(path, &fs) == 0 else { return nil }
        func str<T>(_ t: T) -> String { withUnsafeBytes(of: t) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) } }
        return Mount(mountPoint: str(fs.f_mntonname), device: str(fs.f_mntfromname), fsType: str(fs.f_fstypename),
                     local: fs.f_flags & UInt32(MNT_LOCAL) != 0)
    }

    static func isLocal(_ path: String) -> Bool { mount(of: path)?.local ?? false }

    static func fsSerialText(_ v: UInt32) -> String { String(format: "%08X", v) }
    static func volumeSerialText(_ v: UInt32) -> String { String(format: "%04X-%04X", v >> 16, v & 0xFFFF) }

    static func bootSerial(_ path: String) -> (serial: UInt32, kind: String)? {
        guard let m = mount(of: path), ["msdos", "exfat", "ntfs"].contains(m.fsType), m.device.hasPrefix("/dev/") else { return nil }
        return BootSector.read(device: m.device).flatMap(BootSector.serial)
    }

    static func volumeUUID(_ path: String) -> String? {
        (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeUUIDStringKey]))?.volumeUUIDString
    }

    /// History key part: "1A2B3C4D", or "uuid:<volume UUID>" when the boot sector cannot be read (spec D.23.2).
    static func fsSerial(_ root: String) -> String? {
        if let b = bootSerial(root) { return fsSerialText(b.serial) }
        return volumeUUID(root).map { "uuid:" + $0 }
    }

    /// Report `destinations[].volumeSerial`: "1A2B-3C4D" (FAT/exFAT/NTFS, as Windows), else "uuid:<volume UUID>".
    static func volumeSerialText(_ path: String) -> String? {
        if let b = bootSerial(path) { return volumeSerialText(b.serial) }
        return volumeUUID(path).map { "uuid:" + $0 }
    }

    /// "exFAT", "FAT32", "FAT", "NTFS", "APFS", "HFS+", or what statfs says.
    static func filesystemName(_ path: String) -> String {
        if let b = bootSerial(path) { return b.kind }
        guard let m = mount(of: path) else { return "?" }
        return ["apfs": "APFS", "hfs": "HFS+", "exfat": "exFAT", "msdos": "FAT32", "ntfs": "NTFS", "smbfs": "SMB"][m.fsType] ?? m.fsType
    }

    static func capacity(_ root: String) -> Int64? {
        var fs = statfs()
        guard statfs(root, &fs) == 0 else { return nil }
        return Int64(fs.f_blocks) * Int64(fs.f_bsize)
    }

    /// Lock key of a folder: the volume's UUID and the path under its root, upper-cased (spec D.23.4).
    static func folderKey(_ path: String) -> String {
        let full = realPath(path).trimmingTrailingSlash()
        if let m = mount(of: full), let uuid = volumeUUID(full) {
            let rel = VolumeFacts.relative(full, to: m.mountPoint)
            return (uuid + "|" + (rel.isEmpty ? "/" : rel)).uppercased()
        }
        return ("|" + full).uppercased()
    }

    /// Symlinks resolved like statfs sees the path (realpath(3)); `URL.resolvingSymlinksInPath` is not used — it strips
    /// "/private" ("/private/tmp/x" → "/tmp/x"), which then no longer starts with the mount point statfs names.
    /// A path that does not exist: standardized only.
    static func realPath(_ path: String) -> String {
        if let r = Darwin.realpath(path, nil) {
            defer { free(r) }
            return String(cString: r)
        }
        return URL(fileURLWithPath: path).standardizedFileURL.path
    }

    /// `full` without the mount point `root` — only when it really starts with it: a path on the startup disk is under
    /// "/" by its firmlink ("/Users/…") although statfs names "/System/Volumes/Data" as its mount point.
    static func relative(_ full: String, to root: String) -> String {
        let r = root.trimmingTrailingSlash()
        guard r != "/", full == r || full.hasPrefix(r + "/") else { return full }
        return String(full.dropFirst(r.count))
    }

    /// The path without its volume root, "/" separated: for comparing a report's card path with a folder by disk (R12).
    static func underRoot(_ path: String) -> String {
        var p = path.replacingOccurrences(of: "\\", with: "/")
        if p.hasPrefix("//") {                                   // \\server\share\rest
            let parts = p.split(separator: "/", omittingEmptySubsequences: true)
            p = "/" + parts.dropFirst(2).joined(separator: "/")
        } else if p.count >= 2, p.dropFirst().first == ":" {      // D:\rest
            p = String(p.dropFirst(2))
        } else if let m = mount(of: p), FileManager.default.fileExists(atPath: p) {
            p = relative(realPath(p), to: m.mountPoint)
        } else if p.hasPrefix("/Volumes/") {                     // a Mac path of a disk not here
            let parts = p.split(separator: "/", omittingEmptySubsequences: true)
            p = "/" + parts.dropFirst(2).joined(separator: "/")
        }
        p = p.trimmingTrailingSlash()
        return p.hasPrefix("/") ? p : "/" + p
    }
}

/// File times compared over a card's life (Windows R3).
public enum FileTimes {
    public static func storesLocalTime(_ fs: String?) -> Bool {
        guard let fs = fs?.lowercased() else { return false }
        return fs == "msdos" || fs.hasPrefix("fat")
    }

    public static func same(_ a: Date, _ b: Date, localTime: Bool) -> Bool {
        let ta = ticks(a), tb = ticks(b)
        if ta == tb { return true }
        guard localTime else { return false }
        let diff = abs(ta - tb), hour: Int64 = 36_000_000_000
        return diff % hour == 0 && diff <= 14 * hour
    }

    public static func close(_ a: Date, _ b: Date, localTime: Bool) -> Bool {
        let slack: Int64 = 20_000_000, hour: Int64 = 36_000_000_000
        let diff = abs(ticks(a) - ticks(b))
        if diff <= slack { return true }
        guard localTime, diff <= 14 * hour + slack else { return false }
        let off = diff % hour
        return off <= slack || hour - off <= slack
    }

    /// .NET ticks (100 ns since 0001-01-01 UTC) — the `mtimeTicksUtc` of history file lists and fingerprints.
    public static func ticks(_ d: Date) -> Int64 {
        let ns = (d.timeIntervalSince1970 * 1e7).rounded()
        return Int64(ns) + 621_355_968_000_000_000
    }

    public static func ticks(_ t: timespec) -> Int64 { Int64(t.tv_sec) * 10_000_000 + Int64(t.tv_nsec) / 100 + 621_355_968_000_000_000 }

    public static func date(ticks: Int64) -> Date { Date(timeIntervalSince1970: Double(ticks - 621_355_968_000_000_000) / 1e7) }
}
