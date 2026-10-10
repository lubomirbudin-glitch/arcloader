import XCTest
@testable import OffloadKit

/// Hash types ASC MHL may carry (R9), vectors as Windows `HashAlgoTests`; MD5 in one read with xxh64 (spec 3.5).
/// (Stream A's registry tests, kept on the plan's API — task 32: `HashAlgo.make/equal`, `HashKinds`, `hashFile(kinds:)`.)
final class HashAlgoTests: XCTestCase {
    /// The whole of `data` through one running hash of the registry.
    private func of(_ name: String, _ data: Data) -> String? {
        guard let h = HashAlgo.make(name) else { return nil }
        data.withUnsafeBytes { if let p = $0.baseAddress { h.update(p, data.count) } }
        return h.value()
    }

    func testVectors() {
        let cases: [(String, String, String)] = [
            ("xxh64", "", "ef46db3751d8e999"),
            ("xxh64", "abc", "44bc2cf5ad770999"),
            ("xxh3", "", "2d06800538d394c2"),
            ("xxh3", "abc", "78af5f94892f3950"),
            ("xxh128", "", "99aa06d3014798d86001c324468d497f"),
            ("xxh128", "abc", "06b05ab6733a618578af5f94892f3950"),
            ("md5", "", "d41d8cd98f00b204e9800998ecf8427e"),
            ("md5", "abc", "900150983cd24fb0d6963f7d28e17f72"),
            ("sha1", "abc", "a9993e364706816aba3e25717850c26c9cd0d89d"),
            ("c4", "", "c459dsjfscH38cYeXXYogktxf4Cd9ibshE3BHUo6a58hBXmRQdZrAkZzsWcbWtDg5oQstpDuni4Hirj75GEmTc1sFT"),
            ("c4", "abc", "c45S4rnaTNWonxss1u8LzsaJdEph1AJhWUF4sh2waXKMsutyfAxg4ybUeuXVWS9HdNcEypmeXn8FZGonD4w1rj9DZp"),
        ]
        for (algo, input, expected) in cases {
            XCTAssertEqual(of(algo, Data(input.utf8)), expected, "\(algo)(\(input))")
        }
        XCTAssertNil(HashAlgo.make("sha256"))
    }

    func testBlockByBlockEqualsAtOnce() {
        var data = Data(count: 100_000)
        for i in data.indices { data[i] = UInt8(truncatingIfNeeded: i &* 31 &+ 7) }
        for algo in HashAlgo.known {
            let h = HashAlgo.make(algo)!
            var i = 0
            while i < data.count {
                let n = min(4096, data.count - i)
                data.withUnsafeBytes { h.update($0.baseAddress! + i, n) }
                i += n
            }
            XCTAssertEqual(h.value(), of(algo, data), algo)
        }
    }

    func testEqualHexAnyCaseC4Exact() {
        XCTAssertTrue(HashAlgo.equal("xxh64", "44BC2CF5AD770999", "44bc2cf5ad770999"))
        XCTAssertFalse(HashAlgo.equal("c4", "c45S4", "C45s4"))
        XCTAssertEqual(HashAlgo.label("xxh64"), "xxHash64")
        XCTAssertEqual(HashAlgo.label("md5"), "MD5")
    }

    func testHashKindsXxh64Always() {
        XCTAssertEqual(HashKinds.md5, .both)
        XCTAssertEqual(HashKinds.md5.names, ["xxh64", "md5"])
        XCTAssertEqual(HashKinds.xxh64.names, ["xxh64"])
        XCTAssertEqual(HashKinds(rawValue: 0), .xxh64, "R16: xxh64 is never left out")
        XCTAssertEqual(HashKinds.from(names: ["xxh64", "md5"]), .both)
        XCTAssertEqual(HashKinds.from(names: nil), .xxh64)
        XCTAssertEqual(HashKinds.fromSetting("future"), .xxh64)
        XCTAssertEqual(HashKinds.fromSetting("md5"), .xxh64, "R16: the setting has no MD5 alone")
    }

    func testFileBothHashesInOneRead() throws {
        let dir = tempDir()
        let path = dir.path + "/abc"
        try Data("abc".utf8).write(to: URL(fileURLWithPath: path))
        let buf = IO.allocateAligned(4096)
        defer { free(buf) }
        let (h, size) = try hashFile(path, chunkSize: 4096, buffer: buf, kinds: .both)
        XCTAssertEqual(h.md5Hex, "900150983cd24fb0d6963f7d28e17f72")
        XCTAssertEqual(h.xxh64, 0x44bc2cf5ad770999)
        XCTAssertEqual(h.xxh128, "06b05ab6733a618578af5f94892f3950")
        XCTAssertEqual(size, 3)
        XCTAssertNil(try hashFile(path, chunkSize: 4096, buffer: buf, kinds: .xxh64).hashes.md5)

        let big = dir.path + "/big"
        let data = TestFS.writeRandom(big, size: 3 * 4096 + 1, seed: 7)
        var progress = 0
        let (hb, read) = try hashFile(big, chunkSize: 4096, buffer: buf, kinds: .md5, progress: { progress += $0 })
        XCTAssertEqual(hb.md5Hex, of("md5", data))
        XCTAssertEqual(hb.xxh64Hex, of("xxh64", data))   // R16: MD5 alone still brings xxh64
        XCTAssertEqual(hb.xxh128, of("xxh128", data))
        XCTAssertEqual(read, Int64(data.count))
        XCTAssertEqual(progress, data.count)
    }

    func testBytesAndNoSpace() {
        XCTAssertEqual(formatBytesShort(3_200_000_000), "3.2 GB")
        XCTAssertEqual(formatBytesShort(2_000_000_000), "2 GB")
        XCTAssertEqual(formatBytesShort(412_000_000_000), "412 GB")
        XCTAssertEqual(formatBytesShort(12), "12 B")
        let need = SpaceNeed(card: 380_000_000_000, proxy: 30_000_000_000, report: 2_000_000_000)
        XCTAssertEqual(Msg.noSpace("Zaloha", total: need.total, parts: need.partsText("cards"), free: 398_000_000_000),
                       "Not enough space on Zaloha: needs 412 GB (cards 380 GB, proxy ~30 GB, report 2 GB), free 398 GB.")
    }
}

/// ASC MHL generations read back (Windows `MhlReaderTests`): ours, two generations with every hash type, damaged chains.
final class MHLReaderTests: XCTestCase {
    private func generation(_ card: String, _ seq: Int, _ files: [(String, Int, [(String, String)], String)]) -> String {
        var x = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<hashlist version=\"2.0\" xmlns=\"urn:ASC:MHL:v2.0\">\n"
        x += "  <processinfo><process>transfer</process><ignore><pattern>.DS_Store</pattern><pattern>ascmhl</pattern></ignore></processinfo>\n  <hashes>\n"
        for (path, size, hashes, action) in files {
            x += "    <hash><path size=\"\(size)\" lastmodificationdate=\"2026-10-06T09:00:00+02:00\">\(path)</path>"
            for (a, v) in hashes { x += "<\(a) action=\"\(action)\" hashdate=\"2026-10-06T10:00:00+02:00\">\(v)</\(a)>" }
            x += "</hash>\n"
        }
        x += "  </hashes>\n</hashlist>\n"
        try! FileManager.default.createDirectory(atPath: card + "/ascmhl", withIntermediateDirectories: true)
        let name = String(format: "%04d_%@_2026-10-06_080000Z.mhl", seq, (card as NSString).lastPathComponent)
        try! Data(x.utf8).write(to: URL(fileURLWithPath: card + "/ascmhl/" + name))
        return name
    }

    private func chain(_ card: String, _ names: [String]) {
        var x = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ascmhldirectory xmlns=\"urn:ASC:MHL:DIRECTORY:v2.0\">\n"
        for (i, n) in names.enumerated() {
            let c4 = MHL.c4(FileManager.default.contents(atPath: card + "/ascmhl/" + n)!)
            x += "  <hashlist sequencenr=\"\(i + 1)\">\n    <path>\(n)</path>\n    <c4>\(c4)</c4>\n  </hashlist>\n"
        }
        x += "</ascmhldirectory>\n"
        try! Data(x.utf8).write(to: URL(fileURLWithPath: card + "/ascmhl/ascmhl_chain.xml"))
    }

    func testTwoGenerationsNewestWinsEveryHashType() {
        let card = tempDir().sub("A001")
        let g1 = generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "AAAAAAAAAAAAAAAA")], "original")])
        let g2 = generation(card, 2, [
            ("CLIP/a.mov", 11, [("xxh64", "bbbbbbbbbbbbbbbb")], "original"),
            ("CLIP\\b.mov", 20, [("md5", "900150983cd24fb0d6963f7d28e17f72"), ("sha1", "a9993e364706816aba3e25717850c26c9cd0d89d"),
                                 ("xxh128", "06b05ab6733a618578af5f94892f3950"), ("xxh3", "78af5f94892f3950")], "original"),
        ])
        chain(card, [g1, g2])
        let h = MHLReader.read(cardPath: card)!
        XCTAssertEqual(h.chainProblems, [])
        XCTAssertEqual(h.generations.map(\.seq), [1, 2])
        XCTAssertEqual(h.record("CLIP/a.mov")?.size, 11)
        XCTAssertEqual(h.record("CLIP/a.mov")?.generation, 2)
        XCTAssertEqual(h.record("clip/b.mov")?.hashes.map(\.algorithm), ["md5", "sha1", "xxh128", "xxh3"])
        XCTAssertEqual(h.record("CLIP/b.mov")?.modified, Date(timeIntervalSince1970: 1_791_270_000))   // 2026-10-06T07:00:00Z
        XCTAssertEqual(h.chain.map(\.seq), [1, 2])
    }

    func testChangedGenerationIsAChainProblem() {
        let card = tempDir().sub("A001")
        let g1 = generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "aaaaaaaaaaaaaaaa")], "original")])
        let g2 = generation(card, 2, [("CLIP/b.mov", 10, [("xxh64", "bbbbbbbbbbbbbbbb")], "failed")])
        chain(card, [g1, g2])
        let f = FileHandle(forWritingAtPath: card + "/ascmhl/" + g2)!
        f.seekToEndOfFile()
        f.write(Data("<!-- edited -->".utf8))
        f.closeFile()
        let h = MHLReader.read(cardPath: card)!
        XCTAssertEqual(h.chainProblems, ["ASC MHL generation 0002 does not match the chain (the MHL file was changed)."])
        XCTAssertEqual(h.generations.count, 2)
        XCTAssertTrue(h.record("CLIP/b.mov")!.failed)
        XCTAssertNil(h.lastGood["clip/b.mov"])
    }

    func testMissingChainReadsByNumber() {
        let card = tempDir().sub("A001")
        _ = generation(card, 2, [("CLIP/b.mov", 10, [("xxh64", "bbbbbbbbbbbbbbbb")], "original")])
        _ = generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "aaaaaaaaaaaaaaaa")], "original")])
        let h = MHLReader.read(cardPath: card)!
        XCTAssertEqual(h.chainProblems, ["ascmhl_chain.xml is missing — generations are read by the number in the file name."])
        XCTAssertEqual(h.generations.map(\.seq), [1, 2])
        XCTAssertTrue(h.chain.isEmpty)
    }

    func testNoAscmhlIsNil() {
        let dir = tempDir()
        XCTAssertNil(MHLReader.read(cardPath: dir.sub("A001")))
        dir.sub("A001", "ascmhl")
        XCTAssertNil(MHLReader.read(cardPath: dir.path + "/A001"))
    }

    /// Our generation 1 and an appended generation 2 (with MD5) read back with an intact two-entry chain.
    func testWrittenGenerationsChainUp() throws {
        let dir = tempDir()
        let card = dir.sub("d1", "2026-10-06", "A001")
        func report(_ files: [OffloadReport.FileEntry], generation: Int?) -> OffloadReport {
            var r = OffloadReport(cardName: "A001", date: "2026-10-06", source: "/Volumes/A001", sourceFilesystem: "exfat",
                                  started: Date(), copyFinished: Date(), finished: Date(), verified: true,
                                  totalFiles: files.count, totalBytes: 0,
                                  destinations: [.init(root: dir.path + "/d1", cardPath: card, filesystem: "apfs", filesOK: files.count, filesFailed: 0)],
                                  files: files, skipped: [])
            r.generation = generation
            return r
        }
        let a = OffloadReport.FileEntry(path: "CLIP/a.mov", size: 3, modified: Date(), xxh64: "44bc2cf5ad770999",
                                        sourceError: nil, destinations: ["verified"], errors: [nil])
        var b = OffloadReport.FileEntry(path: "CLIP/b.mov", size: 3, modified: Date(), xxh64: "44bc2cf5ad770999",
                                        sourceError: nil, destinations: ["verified"], errors: [nil])
        b.md5 = "900150983cd24fb0d6963f7d28e17f72"
        try MHL.write(report: report([a], generation: nil), cardPath: card, now: Date(timeIntervalSince1970: 1_791_270_000), host: "mac")
        try MHL.write(report: report([b], generation: 2), cardPath: card, now: Date(timeIntervalSince1970: 1_791_273_600), host: "mac")
        let h = MHLReader.read(cardPath: card)!
        XCTAssertEqual(h.chainProblems, [])
        XCTAssertEqual(h.chain.map(\.seq), [1, 2])
        XCTAssertEqual(h.record("CLIP/b.mov")?.hashes.map(\.algorithm), ["xxh64", "md5"])
        XCTAssertTrue(h.ignore.contains("_REPORT"))
        XCTAssertTrue(h.chain[1].path.hasPrefix("0002_A001_"))
        // A third write that claims generation 2 again is refused (the chain already has it).
        XCTAssertThrowsError(try MHL.write(report: report([b], generation: 2), cardPath: card, now: Date(), host: "mac"))
    }
}
