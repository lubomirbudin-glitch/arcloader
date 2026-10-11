import CryptoKit
import Darwin
import Foundation

/// An exclusive advisory lock on a local file (flock). Released by `release()`, by deinit, or by the kernel when the
/// process dies. Spec D.23.4.
public final class FileLock {
    private let lock = NSLock()
    private var fd: Int32

    private init(fd: Int32) { self.fd = fd }
    deinit { release() }

    /// The lock now, or nil when another descriptor (this process or another) holds it. Never waits.
    public static func tryLock(_ path: String) -> FileLock? {
        try? FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent,
                                                 withIntermediateDirectories: true)
        let fd = open(path, O_RDWR | O_CREAT | O_CLOEXEC, 0o600)
        guard fd >= 0 else { return nil }
        if flock(fd, LOCK_EX | LOCK_NB) == 0 { return FileLock(fd: fd) }
        close(fd)
        return nil
    }

    /// Tries every 50 ms until `timeout`; nil when the lock stayed held.
    public static func lock(_ path: String, timeout: TimeInterval) -> FileLock? {
        let end = Date().addingTimeInterval(timeout)
        while true {
            if let l = tryLock(path) { return l }
            if Date() >= end { return nil }
            usleep(50_000)
        }
    }

    /// Any thread, any number of times (flock is not tied to a thread, unlike a Windows mutex).
    public func release() {
        lock.lock()
        defer { lock.unlock() }
        guard fd >= 0 else { return }
        flock(fd, LOCK_UN)
        close(fd)
        fd = -1
    }
}

/// Names of the local lock files (spec D.23.4). Never a lock file on a destination: that is the FORMATY contract and
/// may be a network drive.
public enum Locks {
    /// `AppSupport.lockFolder` — after the rename to mmMover still `~/Library/Application Support/Offload/locks`, so an
    /// older Offload and mmMover keep excluding each other (a lock is its file's path).
    public static var folder: String { AppSupport.lockFolder }

    static func hex<H: HashFunction>(_ h: H.Type, _ s: String) -> String {
        H.hash(data: Data(s.utf8)).map { String(format: "%02x", $0) }.joined()
    }

    /// `history-<sha256(full path of the history folder, upper-cased)>.lock`.
    public static func history(_ historyDir: String) -> String {
        let full = URL(fileURLWithPath: historyDir).standardizedFileURL.path.trimmingTrailingSlash().uppercased()
        return folder + "/history-" + hex(SHA256.self, full) + ".lock"
    }

    /// `append-<sha256(folderKey)>.lock` — one job at a time per card folder.
    public static func cardFolder(_ cardPath: String) -> String {
        folder + "/append-" + hex(SHA256.self, VolumeFacts.folderKey(cardPath)) + ".lock"
    }

    /// `day-<sha1(folderKey of the day folder)>.lock` — the day report and day clip lists.
    public static func day(_ dayFolder: String) -> String {
        folder + "/day-" + hex(Insecure.SHA1.self, VolumeFacts.folderKey(dayFolder)) + ".lock"
    }
}

/// One job at a time per card folder (append, repair, a copy verifying with its proxies). Never waits: a busy folder
/// refuses at once and nothing stays held. Locks are taken in a fixed order (by lock name).
public final class FolderLocks {
    private let lock = NSLock()
    private var held: [FileLock]

    private init(_ held: [FileLock]) { self.held = held }
    deinit { release() }

    public static func acquire(_ cardPaths: [String]) throws -> FolderLocks {
        var held: [FileLock] = []
        var seen = Set<String>()
        let ordered = cardPaths.map { (Locks.cardFolder($0), $0) }
            .filter { seen.insert($0.0).inserted }
            .sorted { $0.0 < $1.0 }
        for (name, path) in ordered {
            guard let l = FileLock.tryLock(name) else {
                held.reversed().forEach { $0.release() }
                throw OffloadError.preflight(Msg.appendBusy(path), code: .appendBusy)
            }
            held.append(l)
        }
        return FolderLocks(held)
    }

    /// Any thread, any number of times (a lock taken in phase 1 may be released by phase 2 on another thread).
    public func release() {
        lock.lock()
        let all = held
        held = []
        lock.unlock()
        all.reversed().forEach { $0.release() }
    }
}
