import XCTest
@testable import OffloadKit

final class HashingTests: XCTestCase {
    private func run(_ data: Data, kinds: HashKinds, blockSize: Int = 4) -> (hashes: FileHashes, blocks: [UInt64]) {
        let h = MultiHasher(kinds: kinds, blockSize: blockSize)
        data.withUnsafeBytes { if data.count > 0 { h.update($0.baseAddress!, data.count) } }
        return h.finish()
    }

    // Review focus 6: the values Windows and ascmhl write. If one differs, check with `brew install xxhash`:
    // printf '' | xxhsum -H1 (xxh64), -H3 (xxh3), -H2 (xxh128); printf '' | md5.
    func testVectors() {
        let empty = run(Data(), kinds: .both)
        XCTAssertEqual(empty.hashes.xxh64Hex, "ef46db3751d8e999")
        XCTAssertEqual(empty.hashes.xxh128, "99aa06d3014798d86001c324468d497f")
        XCTAssertEqual(empty.hashes.md5Hex, "d41d8cd98f00b204e9800998ecf8427e")
        XCTAssertEqual(empty.blocks, [], "an empty file has no block")
        let abc = run(Data("abc".utf8), kinds: .both)
        XCTAssertEqual(abc.hashes.xxh64Hex, "44bc2cf5ad770999")
        XCTAssertEqual(abc.hashes.md5Hex, "900150983cd24fb0d6963f7d28e17f72")
        XCTAssertEqual(BlockHash.hex(BlockHash.xxh3(UnsafeRawPointer(bitPattern: 1)!, 0)), "2d06800538d394c2")
        let sha1 = HashAlgo.make("sha1")!
        XCTAssertEqual(sha1.value(), "da39a3ee5e6b4b0d3255bfef95601890afd80709")
    }

    func testXxh64OnlyHasNoMd5ButAlwaysXxh128() {
        let r = run(Data("abc".utf8), kinds: .xxh64)
        XCTAssertNil(r.hashes.md5)
        XCTAssertNotNil(r.hashes.xxh128, "xxh128 is computed for every file (FORMATY, Windows 0.6.0)")
    }

    func testBlocksSplitAtBlockSizeAndFeedOrderDoesNotMatter() {
        let data = Data((0..<10).map { UInt8($0) })
        let whole = run(data, kinds: .xxh64, blockSize: 4)
        XCTAssertEqual(whole.blocks.count, 3, "4 + 4 + 2 bytes")
        // The same bytes fed in odd pieces give the same blocks and hashes.
        let h = MultiHasher(kinds: .xxh64, blockSize: 4)
        data.withUnsafeBytes { p in
            h.update(p.baseAddress!, 3); h.update(p.baseAddress! + 3, 6); h.update(p.baseAddress! + 9, 1)
        }
        let pieces = h.finish()
        XCTAssertEqual(pieces.blocks, whole.blocks)
        XCTAssertEqual(pieces.hashes, whole.hashes)
        let third = data.subdata(in: 8..<10)
        XCTAssertEqual(third.withUnsafeBytes { BlockHash.xxh3($0.baseAddress!, 2) }, whole.blocks[2])
    }

    func testKindsNamesAndSettings() {
        XCTAssertEqual(HashKinds.xxh64.names, ["xxh64"])
        XCTAssertEqual(HashKinds.both.names, ["xxh64", "md5"])
        XCTAssertEqual(HashKinds.from(names: nil), .xxh64)
        XCTAssertEqual(HashKinds.from(names: ["xxh64", "md5"]), .both)
        XCTAssertEqual(HashKinds.from(names: ["md5"]), .both, "MD5 is never alone")
        XCTAssertEqual(HashKinds.fromSetting("both"), .both)
        XCTAssertEqual(HashKinds.fromSetting("sha256"), .xxh64, "a value from a newer version reads as xxh64")
    }

    func testHashAlgoRegistry() {
        XCTAssertEqual(Set(HashAlgo.known), ["xxh64", "md5", "sha1", "xxh3", "xxh128", "c4"])
        XCTAssertNil(HashAlgo.make("sha256"))
        XCTAssertTrue(HashAlgo.equal("xxh64", "EF46DB3751D8E999", "ef46db3751d8e999"))
        XCTAssertFalse(HashAlgo.equal("c4", "c4ABC", "c4abc"), "base58 is case sensitive")
        let c4 = HashAlgo.make("c4")!
        XCTAssertEqual(c4.value(), MHL.c4(Data()))
        XCTAssertEqual(c4.value().count, 90)
        XCTAssertEqual(HashAlgo.label("xxh128"), "xxHash128")
    }

    func testHashFileGivesAllKinds() throws {
        let dir = tempDir()
        let path = dir.file("a.bin")   // the plan's dir.sub("a.bin") would make a folder of that name (TempDir.sub)
        let data = TestFS.writeRandom(path, size: 3 << 20, seed: 7)
        let buf = IO.allocateAligned(1 << 20)
        defer { free(buf) }
        let r = try hashFile(path, chunkSize: 1 << 20, buffer: buf, kinds: .both)
        XCTAssertEqual(r.size, Int64(data.count))
        XCTAssertEqual(r.hashes, run(data, kinds: .both).hashes)
    }
}
