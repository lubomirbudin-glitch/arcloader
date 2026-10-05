import Darwin
import Foundation

/// Safe copies of finished files (proxies, the PDF report): a temporary `.<name>.part` next to the target, then a rename.
/// Port of Windows `ProxyCopy.cs`.
public enum ProxyCopy {
    /// The target name is taken (`copyFile(…, replace: false)` found something there).
    struct Taken: Error {}

    /// Copies `src` to `dst` via `.<name>.part`; a failure leaves no temporary file behind.
    /// `replace: false` never overwrites: an existing `dst` throws `Taken`.
    static func copyFile(_ src: String, to dst: String, replace: Bool = true) throws {
        let dir = (dst as NSString).deletingLastPathComponent
        try IO.mkdirs(dir)
        let part = dir + "/." + (dst as NSString).lastPathComponent + ".part"
        unlink(part)
        do {
            try FileManager.default.copyItem(atPath: src, toPath: part)
            if replace {
                guard rename(part, dst) == 0 else { throw IOError("rename", dst) }
            } else {
                try renameNoReplace(part, dst)
            }
        } catch {
            unlink(part)
            throw error
        }
    }

    /// rename(2) that never replaces: `renamex_np(RENAME_EXCL)` where the filesystem supports it (APFS, HFS+);
    /// where it does not (ENOTSUP/EINVAL — possibly exFAT, FAT32, SMB), a check right before a plain rename.
    static func renameNoReplace(_ from: String, _ to: String) throws {
        if renamex_np(from, to, UInt32(RENAME_EXCL)) == 0 { return }
        let e = errno
        if e == EEXIST { throw Taken() }
        guard e == ENOTSUP || e == EINVAL else { throw IOError("rename", to, e) }
        if FileManager.default.fileExists(atPath: to) { throw Taken() }
        guard rename(from, to) == 0 else { throw IOError("rename", to) }
    }

    /// The extra copy of a proxy into the user's proxy folder: `<extraFolder>/<date>/<cardName>/<fileName>`, or
    /// `<name>_2.mov`, `_3` … when the name is taken there — never overwrites. Never throws: a missing folder
    /// (disconnected disk) or a failed copy comes back as the error text. At most `maxAttempts` names
    /// (`_2` … `_1000`) are tried; then it gives up with an error.
    public static func toExtraFolder(_ source: String, extraFolder: String, date: String, cardName: String,
                                     fileName: String, maxAttempts: Int = 1000) -> (path: String?, error: String?) {
        guard ProxyPreflight.isDirectory(extraFolder) else { return (nil, "složka není dostupná") }
        let dir = URL(fileURLWithPath: extraFolder).appendingPathComponent(date).appendingPathComponent(cardName).path
        let stem = (fileName as NSString).deletingPathExtension
        let ext = (fileName as NSString).pathExtension
        func name(_ n: Int) -> String { n == 1 ? fileName : "\(stem)_\(n)" + (ext.isEmpty ? "" : "." + ext) }
        do {
            for n in 1...max(1, maxAttempts) {
                let target = dir + "/" + name(n)
                if FileManager.default.fileExists(atPath: target) { continue }
                do {
                    try copyFile(source, to: target, replace: false)
                    return (target, nil)
                } catch is Taken {
                    // Taken between the check and the rename: try the next number.
                }
            }
            return (nil, "žádné volné jméno (\(name(1)) … \(name(max(1, maxAttempts))))")
        } catch let e as IOError {
            return (nil, e.description)
        } catch {
            return (nil, error.localizedDescription)
        }
    }
}
