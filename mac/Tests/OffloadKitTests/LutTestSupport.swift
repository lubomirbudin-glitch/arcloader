import Foundation
@testable import OffloadKit

extension TestMedia {
    /// The repo's shared/luts — tests never depend on where xctest runs from (its executable is Xcode's xctest).
    static let lutFolder: String = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent()   // OffloadKitTests
        .deletingLastPathComponent()   // Tests
        .deletingLastPathComponent()   // mac
        .deletingLastPathComponent()   // repo
        .appendingPathComponent("shared/luts").path

    static let luts = LutCatalog.load(lutFolder)

    /// A custom .cube as the resolver hands it over.
    static func customLut(_ path: String) -> LutUse {
        LutUse(source: "custom", id: nil, name: (path as NSString).lastPathComponent, reason: "vlastní LUT", file: path)
    }
}
