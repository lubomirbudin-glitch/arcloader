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
