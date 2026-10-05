import Foundation

/// External tools the proxy/report stage can use. Every one is optional: a missing tool only disables
/// the formats that need it, never the offload itself.
public struct ExternalTools: Codable, Equatable {
    public var ffmpeg: String?
    public var ffprobe: String?
    public var redline: String?
    public var artCmd: String?
    public var brawDecoder: String?

    public init(ffmpeg: String? = nil, ffprobe: String? = nil, redline: String? = nil, artCmd: String? = nil, brawDecoder: String? = nil) {
        self.ffmpeg = ffmpeg
        self.ffprobe = ffprobe
        self.redline = redline
        self.artCmd = artCmd
        self.brawDecoder = brawDecoder
    }

    /// Folders searched in order. The app bundle and ~/Library/Application Support/Offload/bin come first,
    /// so a bundled ffmpeg wins over whatever happens to be on the system.
    public static var searchDirs: [String] {
        var dirs: [String] = []
        if let res = Bundle.main.resourcePath { dirs.append(res + "/bin") }
        var dir = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath().deletingLastPathComponent()
        dirs.append(dir.path)
        // Development build (.build/<triple>/release/…): use the repo's Tools/bin.
        for _ in 0..<5 {
            dir = dir.deletingLastPathComponent()
            dirs.append(dir.appendingPathComponent("Tools/bin").path)
        }
        dirs.append(supportDir + "/bin")
        dirs += ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"]
        return dirs
    }

    public static var supportDir: String {
        (NSHomeDirectory() as NSString).appendingPathComponent("Library/Application Support/Offload")
    }

    static let knownPaths: [String: [String]] = [
        "REDline": [
            "/Applications/REDCINE-X Professional/REDCINE-X PRO.app/Contents/MacOS/REDline",
            "/Applications/REDCINE-X Professional/REDline",
            "/usr/local/bin/REDline",
        ],
        "art-cmd": [
            "/Applications/ARRI Reference Tool.app/Contents/MacOS/art-cmd",
            "/Applications/ARRI Reference Tool/art-cmd",
            "/usr/local/bin/art-cmd",
        ],
    ]

    static func find(_ name: String) -> String? {
        let fm = FileManager.default
        for d in searchDirs where fm.isExecutableFile(atPath: d + "/" + name) { return d + "/" + name }
        return knownPaths[name]?.first { fm.isExecutableFile(atPath: $0) }
    }

    /// Fills every tool that is not set explicitly (or whose explicit path no longer exists).
    public func resolved() -> ExternalTools {
        let fm = FileManager.default
        func pick(_ explicit: String?, _ name: String) -> String? {
            if let e = explicit, !e.isEmpty, fm.isExecutableFile(atPath: e) { return e }
            return ExternalTools.find(name)
        }
        return ExternalTools(ffmpeg: pick(ffmpeg, "ffmpeg"), ffprobe: pick(ffprobe, "ffprobe"),
                             redline: pick(redline, "REDline"), artCmd: pick(artCmd, "art-cmd"),
                             brawDecoder: pick(brawDecoder, "braw-decode"))
    }
}

/// Runs a child process, streams stdout lines to a handler, captures the stderr tail for error messages.
final class ToolProcess {
    let process = Process()
    private var stderrTail = ""
    private let lock = NSLock()

    init(_ executable: String, _ args: [String]) {
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = args
    }

    var stderrText: String { lock.lock(); defer { lock.unlock() }; return stderrTail }

    func captureStderr() {
        let pipe = Pipe()
        process.standardError = pipe
        pipe.fileHandleForReading.readabilityHandler = { [weak self] h in
            let data = h.availableData
            if data.isEmpty { h.readabilityHandler = nil }   // EOF: otherwise the handler keeps firing with empty data
            guard let self, !data.isEmpty else { return }
            self.lock.lock()
            self.stderrTail = String((self.stderrTail + String(decoding: data, as: UTF8.self)).suffix(4000))
            self.lock.unlock()
        }
    }

    func onStdoutLine(_ handler: @escaping (String) -> Void) {
        let pipe = Pipe()
        process.standardOutput = pipe
        var pending = ""
        pipe.fileHandleForReading.readabilityHandler = { h in
            let data = h.availableData
            guard !data.isEmpty else {
                h.readabilityHandler = nil   // EOF: otherwise the handler keeps firing with empty data
                return
            }
            pending += String(decoding: data, as: UTF8.self)
            while let nl = pending.firstIndex(of: "\n") {
                handler(String(pending[..<nl]))
                pending = String(pending[pending.index(after: nl)...])
            }
        }
    }

    /// Runs to completion and returns stdout (only when no line handler is installed).
    @discardableResult
    static func run(_ executable: String, _ args: [String], cwd: String? = nil) throws -> String {
        let p = ToolProcess(executable, args)
        if let cwd { p.process.currentDirectoryURL = URL(fileURLWithPath: cwd) }
        let out = Pipe()
        p.process.standardOutput = out
        p.captureStderr()
        try p.process.run()
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.process.waitUntilExit()
        guard p.process.terminationStatus == 0 else {
            throw ToolError(tool: (executable as NSString).lastPathComponent, status: p.process.terminationStatus, message: p.stderrText)
        }
        return String(decoding: data, as: UTF8.self)
    }

    /// For tools that log to stdout and report failures there (art-cmd): stdout and stderr together, and the exit
    /// status — the caller decides what counts as failure (art-cmd may exit 0 without writing its output).
    static func runCombined(_ executable: String, _ args: [String], cwd: String? = nil) throws -> (status: Int32, output: String) {
        let p = ToolProcess(executable, args)
        if let cwd { p.process.currentDirectoryURL = URL(fileURLWithPath: cwd) }
        let out = Pipe()
        p.process.standardOutput = out
        p.process.standardError = out
        p.process.standardInput = FileHandle.nullDevice
        try p.process.run()
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.process.waitUntilExit()
        return (p.process.terminationStatus, String(decoding: data, as: UTF8.self))
    }
}

public struct ToolError: Error, CustomStringConvertible {
    public let tool: String
    public let status: Int32
    public let message: String

    public var description: String {
        let lastLines = message.split(separator: "\n").suffix(3).joined(separator: " | ")
        return "\(tool) failed (exit \(status)): \(lastLines)"
    }
}
