import Darwin
import XCTest
@testable import OffloadKit

/// An in-memory tap source.
final class MemorySource: TapSource {
    let data: Data
    init(_ d: Data) { data = d }
    var length: Int64 { Int64(data.count) }
    var activity: Int64 { 0 }
    func read(_ offset: Int64, _ buffer: UnsafeMutableRawPointer, _ count: Int) throws -> Int {
        let n = max(0, min(count, data.count - Int(offset)))
        data.withUnsafeBytes { _ = memcpy(buffer, $0.baseAddress! + Int(offset), n) }
        return n
    }
}

/// A raw HTTP/1.1 exchange over a socket (URLSession does not let a test send its own Host header).
enum RawHTTP {
    struct Response {
        let status: Int
        let headers: [String: String]
        let body: Data
    }

    static func send(port: UInt16, _ request: String) throws -> Response {
        let s = socket(AF_INET, SOCK_STREAM, 0)
        guard s >= 0 else { throw IOError("socket", "") }
        defer { close(s) }
        var addr = sockaddr_in()
        addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = port.bigEndian
        addr.sin_addr = in_addr(s_addr: in_addr_t(INADDR_LOOPBACK).bigEndian)
        let ok = withUnsafePointer(to: &addr) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(s, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
        guard ok == 0 else { throw IOError("connect", "127.0.0.1") }
        var tv = timeval(tv_sec: 10, tv_usec: 0)
        setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, &tv, socklen_t(MemoryLayout<timeval>.size))
        let bytes = Array(request.utf8)
        _ = bytes.withUnsafeBytes { Darwin.send(s, $0.baseAddress, $0.count, 0) }
        var all = Data()
        var chunk = [UInt8](repeating: 0, count: 65536)
        while true {
            let n = chunk.withUnsafeMutableBytes { recv(s, $0.baseAddress, 65536, 0) }
            if n <= 0 { break }
            all.append(contentsOf: chunk[0..<n])
        }
        guard let end = all.range(of: Data("\r\n\r\n".utf8)) else { throw IOError(message: "no head: \(all.count) bytes") }
        let head = String(decoding: all[..<end.lowerBound], as: UTF8.self).components(separatedBy: "\r\n")
        let status = Int(head[0].split(separator: " ")[1]) ?? 0
        var headers: [String: String] = [:]
        for h in head.dropFirst() {
            if let c = h.firstIndex(of: ":") { headers[h[..<c].lowercased()] = h[h.index(after: c)...].trimmingCharacters(in: .whitespaces) }
        }
        return Response(status: status, headers: headers, body: all[end.upperBound...])
    }

    static func get(_ url: String, range: String? = nil, host: String? = nil, method: String = "GET") throws -> Response {
        let u = URL(string: url)!
        let port = UInt16(u.port!)
        var r = "\(method) \(u.path) HTTP/1.1\r\nHost: \(host ?? "127.0.0.1:\(port)")\r\nConnection: close\r\n"
        if let range { r += "Range: \(range)\r\n" }
        return try send(port: port, r + "\r\n")
    }
}

final class RangeServerTests: XCTestCase {
    func testParseRange() {
        XCTAssertEqual(RangeServer.parseRange("bytes=0-9", length: 100).map { [$0.0, $0.1] }, [0, 9])
        XCTAssertEqual(RangeServer.parseRange("bytes=5-", length: 100).map { [$0.0, $0.1] }, [5, 99])
        XCTAssertEqual(RangeServer.parseRange("bytes=-3", length: 100).map { [$0.0, $0.1] }, [97, 99])
        XCTAssertEqual(RangeServer.parseRange("bytes=90-200", length: 100).map { [$0.0, $0.1] }, [90, 99])
        XCTAssertEqual(RangeServer.parseRange("bytes=-300", length: 100).map { [$0.0, $0.1] }, [0, 99])
        XCTAssertNil(RangeServer.parseRange("bytes=0-1,5-6", length: 100), "several ranges")
        XCTAssertNil(RangeServer.parseRange("bytes=100-", length: 100), "past the end")
        XCTAssertNil(RangeServer.parseRange("bytes=9-5", length: 100))
        XCTAssertNil(RangeServer.parseRange("bytes=-0", length: 100))
        XCTAssertNil(RangeServer.parseRange("items=0-5", length: 100))
        XCTAssertNil(RangeServer.parseRange("bytes=+1-5", length: 100))
        XCTAssertNil(RangeServer.parseRange("bytes=0-5", length: 0))
    }

    func testParseTarget() {
        let token = String(repeating: "ab", count: 16)
        XCTAssertEqual(RangeServer.parseTarget("/\(token)/0123abcd/A001.mov", token: token)?.id, "0123abcd")
        XCTAssertNil(RangeServer.parseTarget("/\(String(repeating: "cd", count: 16))/0123abcd/A001.mov", token: token), "a foreign token")
        XCTAssertNil(RangeServer.parseTarget("/\(token)/0123abcd/../../etc/passwd", token: token), "no path is ever followed")
        XCTAssertNil(RangeServer.parseTarget("/\(token)/../x", token: token))
        XCTAssertNil(RangeServer.parseTarget("/\(token)/0123abcd", token: token))
    }

    func testServerAnswersRangesAndRefusesTheRest() throws {
        let server = try RangeServer()
        defer { server.close() }
        let data = Data((0..<1000).map { UInt8(truncatingIfNeeded: $0 * 7) })
        let url = server.register(MemorySource(data), name: "A001C001.mov")
        XCTAssertTrue(url.hasPrefix("http://127.0.0.1:\(server.port)/"))
        XCTAssertTrue(url.hasSuffix("/A001C001.mov"))

        let part = try RawHTTP.get(url, range: "bytes=10-19")
        XCTAssertEqual(part.status, 206)
        XCTAssertEqual(part.body, data.subdata(in: 10..<20))
        XCTAssertEqual(part.headers["content-range"], "bytes 10-19/1000")

        let whole = try RawHTTP.get(url)
        XCTAssertEqual(whole.status, 200)
        XCTAssertEqual(whole.body, data)
        XCTAssertEqual(whole.headers["accept-ranges"], "bytes")

        let head = try RawHTTP.get(url, method: "HEAD")
        XCTAssertEqual(head.status, 200)
        XCTAssertEqual(head.headers["content-length"], "1000")
        XCTAssertTrue(head.body.isEmpty)

        XCTAssertEqual(try RawHTTP.get(url, host: "localhost:\(server.port)").status, 400, "DNS rebinding: only 127.0.0.1:<port>")
        XCTAssertEqual(try RawHTTP.get(url, method: "POST").status, 405)
        XCTAssertEqual(try RawHTTP.get(url, range: "bytes=2000-").status, 416)
        let other = url.replacingOccurrences(of: URL(string: url)!.pathComponents[1], with: "00000000000000000000000000000000")
        XCTAssertEqual(try RawHTTP.get(other).status, 404, "another token")
        server.unregister(url)
        XCTAssertEqual(try RawHTTP.get(url).status, 404, "unregistered")
        XCTAssertNil(server.activity(url))
    }

    func testHeadThatNeverEndsIsCut() throws {
        let server = try RangeServer()
        defer { server.close() }
        server.headerTimeout = 0.5
        let t0 = Date()
        let r = try? RawHTTP.send(port: server.port, "GET / HTTP/1.1\r\nHost: 127.0.0.1:\(server.port)\r\n")   // no empty line
        XCTAssertNil(r, "the connection is closed without an answer")
        XCTAssertLessThan(Date().timeIntervalSince(t0), 5)
    }

    func testListensOnLoopbackOnly() throws {
        let server = try RangeServer()
        defer { server.close() }
        // The listening socket's address is 127.0.0.1 (never INADDR_ANY): lsof-like check through getsockname is internal,
        // so connect to another local address of this Mac and expect a refusal.
        var ifaddr: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifaddr) == 0, let first = ifaddr else { throw XCTSkip("no interfaces") }
        defer { freeifaddrs(ifaddr) }
        var other: in_addr?
        var p: UnsafeMutablePointer<ifaddrs>? = first
        while let cur = p {
            if let a = cur.pointee.ifa_addr, a.pointee.sa_family == sa_family_t(AF_INET) {
                let sin = a.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee }
                if UInt32(bigEndian: sin.sin_addr.s_addr) >> 24 != 127 { other = sin.sin_addr; break }
            }
            p = cur.pointee.ifa_next
        }
        guard let addrIn = other else { throw XCTSkip("no non-loopback IPv4 address") }
        let s = socket(AF_INET, SOCK_STREAM, 0)
        defer { close(s) }
        var addr = sockaddr_in()
        addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = server.port.bigEndian
        addr.sin_addr = addrIn
        let ok = withUnsafePointer(to: &addr) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(s, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
        XCTAssertNotEqual(ok, 0, "the tap must not answer on another interface")
    }
}
