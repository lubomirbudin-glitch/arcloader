import Foundation
import XCTest
@testable import OffloadKit

/// braw-decode tests: the decoder built into mac/Tools/bin, the Blackmagic RAW library where it looks, and a sample clip
/// (OFFLOAD_BRAW_SAMPLE=<full path to a .braw>, see README-START „Vzorek BRAW“). A missing requirement skips the test.
enum BrawTest {
    static let decoder: String? = {
        let p = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // OffloadKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // mac
            .appendingPathComponent("Tools/bin/braw-decode").path
        return FileManager.default.isExecutableFile(atPath: p) ? p : nil
    }()

    static func requireDecoder() throws -> String {
        guard let d = decoder else { throw XCTSkip("mac/Tools/bin/braw-decode missing — run Tools/braw-decode/build.sh") }
        return d
    }

    static func requireLibrary() throws -> String {
        let exe = try requireDecoder()
        guard let lib = BrawLibrary.find(decoderDir: (exe as NSString).deletingLastPathComponent) else {
            throw XCTSkip("BlackmagicRawAPI.framework not found (Blackmagic RAW Player, SDK or DaVinci Resolve)")
        }
        return lib
    }

    static func requireSample() throws -> String {
        _ = try requireLibrary()
        guard let p = ProcessInfo.processInfo.environment["OFFLOAD_BRAW_SAMPLE"], FileManager.default.fileExists(atPath: p) else {
            throw XCTSkip("OFFLOAD_BRAW_SAMPLE not set or the file does not exist")
        }
        return p
    }

    private final class Box {
        var data = Data()
    }

    /// Runs `exe` to the end: exit code, stdout and stderr (read in parallel, so neither pipe fills up).
    static func run(_ exe: String, _ args: [String]) throws -> (code: Int32, stdout: Data, stderr: String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: exe)
        p.arguments = args
        let out = Pipe(), err = Pipe()
        p.standardOutput = out
        p.standardError = err
        try p.run()
        let errBox = Box()
        let group = DispatchGroup()
        group.enter()
        DispatchQueue.global().async {
            errBox.data = err.fileHandleForReading.readDataToEndOfFile()
            group.leave()
        }
        let outData = out.fileHandleForReading.readDataToEndOfFile()
        group.wait()
        p.waitUntilExit()
        return (p.terminationStatus, outData, String(decoding: errBox.data, as: UTF8.self))
    }

    static func json(_ data: Data) throws -> [String: Any] {
        guard let o = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw ToolError(tool: "braw-decode", status: 0, message: "not a JSON object: \(String(decoding: data, as: UTF8.self))")
        }
        return o
    }
}

extension BrawTest {
    /// One-frame 320×180 clip as braw-decode --info prints it (no gamma — like an older decoder).
    static let oneFrameInfo = #"{"width":320,"height":180,"sourceWidth":320,"sourceHeight":180,"fps":25,"frames":1,"timecode":"","camera":""}"#

    static let brawClip = Clip(name: "A001", path: "A001.braw", format: .braw, segments: ["A001.braw"], bytes: 1, isCameraProxy: false)

    static func clipInfo(frames: Int) -> ClipInfo {
        var i = ClipInfo()
        i.codec = "Blackmagic RAW"
        i.width = 320
        i.height = 180
        i.frameRate = "25/1"
        i.frames = frames
        i.duration = Double(frames) / 25
        return i
    }

    static func script(_ dir: TempDir, _ name: String, _ body: String) -> String { script(in: dir.path, name, body) }

    /// An executable sh script `<folder>/<name>`.
    static func script(in folder: String, _ name: String, _ body: String) -> String {
        let path = folder + "/" + name
        try! ("#!/bin/sh\n" + body + "\n").write(toFile: path, atomically: true, encoding: .utf8)
        try! FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: path)
        return path
    }

    /// A stand-in braw-decode: a sh script named braw-decode (errors carry that tool name) in `<dir>/fake-braw/`. `info`
    /// runs for `--info`, `frames` for frame decoding; every run first appends its first argument to `calls`.
    static func fakeDecoder(_ dir: TempDir, info: String, frames: String) -> (path: String, calls: String) {
        let sub = dir.sub("fake-braw")
        let calls = sub + "/calls.txt"
        let body = """
        echo "$1" >> '\(calls)'
        if [ "$1" = "--info" ]; then
        \(info)
        else
        \(frames)
        fi
        """
        return (script(in: sub, "braw-decode", body), calls)
    }

    static func printInfo(_ json: String = oneFrameInfo) -> String { "echo '\(json)'\nexit 0" }
    static func fail(_ code: Int, _ line: String) -> String { "echo '\(line)' >&2\nexit \(code)" }
    static let hang = "exec sleep 30"

    /// The first argument of every run of a fake decoder ("--info" or "--scale").
    static func calls(_ file: String) -> [String] {
        ((try? String(contentsOfFile: file, encoding: .utf8)) ?? "").split(separator: "\n").map(String.init)
    }
}

/// Log lines collected from another thread.
final class LogLines {
    private let lock = NSLock()
    private var all: [String] = []
    func add(_ s: String) { lock.lock(); all.append(s); lock.unlock() }
    var lines: [String] { lock.lock(); defer { lock.unlock() }; return all }
}
