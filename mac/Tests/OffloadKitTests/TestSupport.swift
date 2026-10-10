import Foundation
import XCTest
@testable import OffloadKit

/// Unique folder under $TMPDIR/offload-tests. Use `tempDir()` in a test: it is removed after the test
/// (ARC may free a local object before the end of its scope, so the folder is not tied to `deinit`).
final class TempDir {
    let path: String

    init() {
        path = NSTemporaryDirectory() + "offload-tests/" + String(UUID().uuidString.prefix(8))
        try! FileManager.default.createDirectory(atPath: path, withIntermediateDirectories: true)
    }

    /// Creates (if needed) and returns `path/parts…`.
    @discardableResult
    func sub(_ parts: String...) -> String {
        let p = ([path] + parts).joined(separator: "/")
        try! FileManager.default.createDirectory(atPath: p, withIntermediateDirectories: true)
        return p
    }

    /// `path/parts…` for a file: the parent folders are created, the file is not (`sub` would make it a folder).
    func file(_ parts: String...) -> String {
        let p = ([path] + parts).joined(separator: "/")
        try! FileManager.default.createDirectory(atPath: (p as NSString).deletingLastPathComponent,
                                                 withIntermediateDirectories: true)
        return p
    }
}

extension XCTestCase {
    /// A fresh folder, removed after the test.
    func tempDir() -> TempDir {
        let dir = TempDir()
        addTeardownBlock { try? FileManager.default.removeItem(atPath: dir.path) }
        return dir
    }
}

enum TestFS {
    /// Deterministic pseudo-random bytes (SplitMix64) written to `path`; parent folders are created.
    @discardableResult
    static func writeRandom(_ path: String, size: Int, seed: UInt64) -> Data {
        try! FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent,
                                                 withIntermediateDirectories: true)
        var state = seed
        var data = Data(count: size)
        data.withUnsafeMutableBytes { (buf: UnsafeMutableRawBufferPointer) in
            var i = 0
            while i < size {
                state &+= 0x9E37_79B9_7F4A_7C15
                var z = state
                z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
                z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
                z ^= z >> 31
                var b = 0
                while b < 8 && i < size {
                    buf[i] = UInt8(truncatingIfNeeded: z >> UInt64(8 * b))
                    b += 1
                    i += 1
                }
            }
        }
        try! data.write(to: URL(fileURLWithPath: path))
        return data
    }

    /// Every regular file under `dir` (hidden ones too), relative paths, sorted.
    static func files(in dir: String) -> [String] {
        guard let en = FileManager.default.enumerator(atPath: dir) else { return [] }
        var out: [String] = []
        while let rel = en.nextObject() as? String {
            var isDir: ObjCBool = false
            if FileManager.default.fileExists(atPath: dir + "/" + rel, isDirectory: &isDir), !isDir.boolValue {
                out.append(rel)
            }
        }
        return out.sorted()
    }
}

enum TestMedia {
    /// ffmpeg / ffprobe / braw-decode: mac/Tools/bin first (like the app build), then the usual search
    /// (~/Library/Application Support/Offload/bin, /opt/homebrew/bin, /usr/local/bin, /usr/bin).
    static let tools: ExternalTools = {
        let mac = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // OffloadKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // mac
        let bin = mac.appendingPathComponent("Tools/bin").path
        func local(_ name: String) -> String? {
            let p = bin + "/" + name
            return FileManager.default.isExecutableFile(atPath: p) ? p : nil
        }
        return ExternalTools(ffmpeg: local("ffmpeg"), ffprobe: local("ffprobe"), brawDecoder: local("braw-decode")).resolved()
    }()

    static func requireFFmpeg() throws -> (ffmpeg: String, ffprobe: String) {
        guard let f = tools.ffmpeg, let p = tools.ffprobe else { throw XCTSkip("ffmpeg/ffprobe not found") }
        return (f, p)
    }

    /// Test pattern with a TC track and optional sine audio, H.264 (libx264) in MOV/MP4 by extension.
    static func makeClip(_ path: String, seconds: Double = 2, fps: Int = 25, timecode: String = "01:00:00:00",
                         audio: Bool = true, width: Int = 1280, height: Int = 720) throws {
        let (ffmpeg, _) = try requireFFmpeg()
        try FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent,
                                                withIntermediateDirectories: true)
        let d = String(seconds)
        var args = ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                    "-f", "lavfi", "-i", "testsrc2=s=\(width)x\(height):r=\(fps):d=\(d)"]
        if audio { args += ["-f", "lavfi", "-i", "sine=d=\(d)"] }
        args += ["-timecode", timecode, "-c:v", "libx264", "-pix_fmt", "yuv420p"]
        if audio { args += ["-c:a", "aac", "-shortest"] }
        args.append(path)
        try ToolProcess.run(ffmpeg, args)
    }

    /// 2×2×2 identity 3D LUT.
    static func identityLUT(_ path: String) {
        try! "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n"
            .write(toFile: path, atomically: true, encoding: .utf8)
    }
}
