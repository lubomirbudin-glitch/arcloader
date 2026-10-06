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

/// The last 4000 characters a child process wrote to stderr. The pipe is read as data arrives (a chatty tool never
/// blocks on a full pipe); `drain()` after the process ended picks up what the handler has not delivered yet —
/// non-blocking, so a grandchild still holding the pipe cannot hang the caller. The handler reads under the same lock
/// and never after `drain()`: a read racing drain's non-blocking one could hit EAGAIN, which `availableData` raises as
/// an Objective-C exception (a crash, not a Swift error). Bytes are kept and decoded only in `text`, so a UTF-8
/// character split between two reads (a path with diacritics) comes out whole.
final class StderrTail {
    private let pipe = Pipe()
    private let lock = NSLock()
    private var bytes = Data()
    private var drained = false

    init(_ process: Process) {
        process.standardError = pipe
        pipe.fileHandleForReading.readabilityHandler = { [weak self] h in
            guard let self else { return }
            self.lock.lock()
            defer { self.lock.unlock() }
            guard !self.drained else { return }
            let data = h.availableData
            if data.isEmpty {
                h.readabilityHandler = nil   // EOF: otherwise the handler keeps firing with empty data
            } else {
                self.appendLocked(data)
            }
        }
    }

    var text: String {
        lock.lock()
        defer { lock.unlock() }
        return String(String(decoding: bytes, as: UTF8.self).suffix(4000))
    }

    func drain() -> String {
        let handle = pipe.fileHandleForReading
        handle.readabilityHandler = nil   // outside the lock: a handler still waiting for it then sees `drained`
        lock.lock()
        if !drained {
            drained = true
            let fd = handle.fileDescriptor
            _ = fcntl(fd, F_SETFL, fcntl(fd, F_GETFL) | O_NONBLOCK)
            var buffer = [UInt8](repeating: 0, count: 65536)
            while true {
                let n = buffer.withUnsafeMutableBytes { Darwin.read(fd, $0.baseAddress, $0.count) }
                if n < 0 && errno == EINTR { continue }
                if n <= 0 { break }   // EOF, or nothing more right now (EAGAIN)
                appendLocked(Data(buffer[0..<n]))
            }
        }
        lock.unlock()
        return text
    }

    /// Caller holds `lock`. At most 16 000 bytes are kept (≥ 4000 characters of UTF-8).
    private func appendLocked(_ data: Data) {
        bytes.append(data)
        if bytes.count > 16_000 { bytes = Data(bytes.suffix(16_000)) }
    }
}

/// Runs a child process, streams stdout lines to a handler, captures the stderr tail for error messages.
final class ToolProcess {
    let process = Process()
    private var stderr: StderrTail?

    init(_ executable: String, _ args: [String]) {
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = args
    }

    var stderrText: String { stderr?.text ?? "" }

    func captureStderr() {
        stderr = StderrTail(process)
    }

    /// After the process ended: the complete stderr tail.
    func drainStderr() -> String { stderr?.drain() ?? "" }

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

    private final class Output {
        var data = Data()
        let done = DispatchSemaphore(value: 0)
    }

    /// Runs to completion and returns stdout (only when no line handler is installed). With `timeout`, a tool still
    /// running after it is killed (SIGKILL) and a ToolError says so — a hung tool must never hang the job (Windows
    /// `ToolProcess.Run(…, timeout)`; Windows kills the whole process tree, here the tool itself).
    @discardableResult
    static func run(_ executable: String, _ args: [String], cwd: String? = nil, timeout: TimeInterval? = nil) throws -> String {
        let p = ToolProcess(executable, args)
        if let cwd { p.process.currentDirectoryURL = URL(fileURLWithPath: cwd) }
        let name = (executable as NSString).lastPathComponent
        let out = Pipe()
        p.process.standardOutput = out
        p.captureStderr()
        try p.process.run()
        // stdout on its own thread, so the wait below can give up on a tool that never ends.
        let output = Output()
        Thread {
            output.data = out.fileHandleForReading.readDataToEndOfFile()
            output.done.signal()
        }.start()
        if let timeout {
            let deadline = Date().addingTimeInterval(timeout)
            while p.process.isRunning && Date() < deadline { usleep(20_000) }
            if p.process.isRunning {
                kill(p.process.processIdentifier, SIGKILL)
                throw ToolError(tool: name, status: -1, message: "neodpověděl do \(String(format: "%.0f", timeout)) s — ukončen")
            }
        }
        p.process.waitUntilExit()
        output.done.wait()
        guard p.process.terminationStatus == 0 else {
            throw ToolError(tool: name, status: p.process.terminationStatus, message: p.drainStderr())
        }
        return String(decoding: output.data, as: UTF8.self)
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
