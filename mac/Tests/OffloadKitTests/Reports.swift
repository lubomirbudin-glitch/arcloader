import Foundation
@testable import OffloadKit

enum Reports {
    static let t0 = ISO8601DateFormatter().date(from: "2026-10-04T08:00:00Z")!

    /// Two-file report for one destination; `destError` makes the second file fail there; `states` overrides both.
    static func sample(verified: Bool = true, destError: String? = nil, states: [String]? = nil) -> OffloadReport {
        let ok = verified ? "verified" : "written"
        let s = states ?? [ok, destError == nil ? ok : "failed"]
        let card = "/Volumes/RAID/2026-10-04/A001"
        return OffloadReport(
            cardName: "A001", date: "2026-10-04", source: "/Volumes/A001", sourceFilesystem: "exfat",
            started: t0, copyFinished: t0.addingTimeInterval(10), finished: t0.addingTimeInterval(15),
            verified: verified, totalFiles: 2, totalBytes: 3000,
            destinations: [OffloadReport.Destination(root: "/Volumes/RAID", cardPath: card, filesystem: "apfs",
                                                     filesOK: destError == nil ? 2 : 1, filesFailed: destError == nil ? 0 : 1)],
            files: [
                OffloadReport.FileEntry(path: "CLIP/A&B.mov", size: 1000, modified: t0, xxh64: "0123456789abcdef",
                                        sourceError: nil, destinations: [s[0]], errors: [nil]),
                OffloadReport.FileEntry(path: "CLIP/c 02.mov", size: 2000, modified: t0, xxh64: "fedcba9876543210",
                                        sourceError: nil, destinations: [s[1]], errors: [destError]),
            ],
            skipped: [".DS_Store"])
    }
}
