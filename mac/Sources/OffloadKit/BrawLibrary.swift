import Foundation

/// Where the Blackmagic RAW library (BlackmagicRawAPI.framework) can be — the same folders, in the same order, as
/// braw-decode looks in (kLibrarySearchPaths in Tools/braw-decode/braw-decode.cpp). Offload never ships it: it comes with
/// the Blackmagic RAW SDK, the free Blackmagic RAW Player or DaVinci Resolve. Port of Windows `BrawLibrary`.
public enum BrawLibrary {
    public static let frameworkName = "BlackmagicRawAPI.framework"

    /// Next to braw-decode first, then the SDK, the Player and DaVinci Resolve.
    public static func searchDirs(decoderDir: String) -> [String] {
        [
            decoderDir,
            "/Applications/Blackmagic RAW/Blackmagic RAW SDK/Mac/Libraries",
            "/Applications/Blackmagic RAW/Blackmagic RAW Player.app/Contents/Frameworks",
            "/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Frameworks",
        ]
    }

    /// The first `<dir>/BlackmagicRawAPI.framework` that is a folder (a bundle), or nil.
    public static func find(in dirs: [String]) -> String? {
        for dir in dirs {
            let path = dir + "/" + frameworkName
            var isDir: ObjCBool = false
            if FileManager.default.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue { return path }
        }
        return nil
    }

    public static func find(decoderDir: String) -> String? { find(in: searchDirs(decoderDir: decoderDir)) }
}
