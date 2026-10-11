import Darwin
import Foundation

/// What the tap serves: the file's length and a way to read it — from the destination's disk, through the verifier.
protocol TapSource: AnyObject {
    var length: Int64 { get }
    /// The bytes at `offset` into `buffer`; how many were copied — fewer only at the end of the file. Throws on a disk error.
    func read(_ offset: Int64, _ buffer: UnsafeMutableRawPointer, _ count: Int) throws -> Int
    /// Rises while the source works on a read (TapFile: bytes read from the disk, piece by piece) — the proxy's stall
    /// watchdog takes it as a sign of life of a slow destination.
    var activity: Int64 { get }
}

/// Mode A (Windows R3, spec D.23.7): Offload's own HTTP server, so ffmpeg reads the clip through Offload and every byte it
/// reads passes the block verifier. AVFoundation's resource loader is not used — the proxy is ffmpeg's (spec D.24).
/// Security: bound to 127.0.0.1 only (never another interface) and a peer that is not loopback is dropped unread; a random
/// port; a one-time 128-bit token as the first path segment (compared in constant time) and a random id per file — the URL
/// names no path on the disk, so nothing in it can reach any other file (the rest of the path is never used). Only GET and
/// HEAD, one byte range, a Host header of exactly `127.0.0.1:<port>` (DNS rebinding), at most `maxConnections` at once, a
/// request head up to `maxHeaderBytes` arriving whole within `headerTimeout` (slowloris). Small slices and a small send
/// buffer: when ffmpeg drops a connection to seek (moov at the end, an MXF footer) the server stops reading soon.
/// BSD sockets, one thread per connection. `close()` stops listening and cuts every connection.
public final class RangeServer {
    public static let maxConnections = 8
    public static let maxHeaderBytes = 8192
    static let sliceBytes = 256 << 10
    static let sendBufferBytes = 64 << 10

    public let port: UInt16
    private let listener: Int32
    private let token: [UInt8]
    private let lock = NSLock()
    private var files: [String: Entry] = [:]
    private var clients = Set<Int32>()
    private var closed = false
    /// How long a request head may take to arrive whole (also an idle kept-alive connection). Tests shorten it.
    var headerTimeout: TimeInterval = 30
    private var acceptThread: Thread?
    private let stopped = DispatchSemaphore(value: 0)

    final class Entry {
        let source: TapSource
        var sent: Int64 = 0
        init(_ s: TapSource) { source = s }
    }

    /// Body bytes sent and requests answered (tests, the log).
    private(set) var bytesSent: Int64 = 0
    private(set) var requests: Int64 = 0

    private var tokenHex: String { token.map { String(format: "%02x", $0) }.joined() }

    public init() throws {
        var t = [UInt8](repeating: 0, count: 16)
        arc4random_buf(&t, 16)
        token = t
        let s = socket(AF_INET, SOCK_STREAM, 0)
        guard s >= 0 else { throw IOError("socket", "127.0.0.1") }
        var one: Int32 = 1
        setsockopt(s, SOL_SOCKET, SO_NOSIGPIPE, &one, socklen_t(MemoryLayout<Int32>.size))
        var addr = sockaddr_in()
        addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = 0
        addr.sin_addr = in_addr(s_addr: in_addr_t(INADDR_LOOPBACK).bigEndian)
        let bound = withUnsafePointer(to: &addr) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(s, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
        }
        guard bound == 0, listen(s, 16) == 0 else {
            let e = IOError("bind", "127.0.0.1")
            Darwin.close(s)
            throw e
        }
        var got = sockaddr_in()
        var len = socklen_t(MemoryLayout<sockaddr_in>.size)
        _ = withUnsafeMutablePointer(to: &got) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { getsockname(s, $0, &len) } }
        listener = s
        port = UInt16(bigEndian: got.sin_port)
        let t2 = Thread { [weak self] in self?.acceptLoop() }
        t2.name = "offload.tap.accept"
        acceptThread = t2
        t2.start()
    }

    deinit { close() }

    /// Serves `source` until `unregister`; returns its URL — `name` only gives ffmpeg the extension (the demuxer's hint).
    func register(_ source: TapSource, name: String) -> String {
        var raw = [UInt8](repeating: 0, count: 8)
        arc4random_buf(&raw, 8)
        let id = raw.map { String(format: "%02x", $0) }.joined()
        lock.lock(); files[id] = Entry(source); lock.unlock()
        let base = (name as NSString).lastPathComponent
        let safe = String(base.unicodeScalars.map { s -> Character in
            (s.isASCII && (CharacterSet.alphanumerics.contains(s) || s == "." || s == "_" || s == "-")) ? Character(s) : "_"
        })
        return "http://127.0.0.1:\(port)/\(tokenHex)/\(id)/\(safe.isEmpty ? "clip" : safe)"
    }

    /// Stops serving the file of `url` (a request for it is then 404).
    public func unregister(_ url: String) {
        guard let id = RangeServer.idOf(url) else { return }
        lock.lock(); files[id] = nil; lock.unlock()
    }

    /// For the proxy's stall watchdog: the body bytes sent for `url` plus its source's own activity; nil for another URL.
    public func activity(_ url: String) -> Int64? {
        guard let id = RangeServer.idOf(url) else { return nil }
        lock.lock(); defer { lock.unlock() }
        guard let e = files[id] else { return nil }
        return e.sent + e.source.activity
    }

    static func idOf(_ url: String) -> String? {
        guard let path = URL(string: url)?.path else { return nil }
        let parts = path.split(separator: "/", omittingEmptySubsequences: false)
        return parts.count >= 4 ? String(parts[2]) : nil
    }

    public func close() {
        lock.lock()
        if closed { lock.unlock(); return }
        closed = true
        let all = clients
        files = [:]
        lock.unlock()
        shutdown(listener, SHUT_RDWR)
        Darwin.close(listener)
        for c in all { shutdown(c, SHUT_RDWR) }
    }

    private var isClosed: Bool { lock.lock(); defer { lock.unlock() }; return closed }

    private func acceptLoop() {
        while !isClosed {
            var peer = sockaddr_in()
            var len = socklen_t(MemoryLayout<sockaddr_in>.size)
            let c = withUnsafeMutablePointer(to: &peer) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { accept(listener, $0, &len) } }
            if c < 0 {
                if errno == EINTR || errno == ECONNABORTED { continue }
                return   // closed
            }
            var one: Int32 = 1
            setsockopt(c, SOL_SOCKET, SO_NOSIGPIPE, &one, socklen_t(MemoryLayout<Int32>.size))
            // Only this machine's loopback; at most maxConnections at once — refused, never queued.
            let loopback = peer.sin_family == sa_family_t(AF_INET) && UInt32(bigEndian: peer.sin_addr.s_addr) >> 24 == 127
            lock.lock()
            let admit = loopback && !closed && clients.count < RangeServer.maxConnections
            if admit { clients.insert(c) }
            lock.unlock()
            guard admit else { Darwin.close(c); continue }
            let t = Thread { [weak self] in
                self?.serve(c)
                self?.drop(c)
            }
            t.name = "offload.tap.conn"
            t.start()
        }
    }

    private func drop(_ c: Int32) {
        lock.lock(); clients.remove(c); lock.unlock()
        Darwin.close(c)
    }

    private func serve(_ c: Int32) {
        var size = Int32(RangeServer.sendBufferBytes)
        setsockopt(c, SOL_SOCKET, SO_SNDBUF, &size, socklen_t(MemoryLayout<Int32>.size))
        var noDelay: Int32 = 1
        setsockopt(c, IPPROTO_TCP, TCP_NODELAY, &noDelay, socklen_t(MemoryLayout<Int32>.size))
        var send = timeval(tv_sec: 120, tv_usec: 0)
        setsockopt(c, SOL_SOCKET, SO_SNDTIMEO, &send, socklen_t(MemoryLayout<timeval>.size))
        let head = HeadReader(c)
        let buffer = UnsafeMutableRawPointer.allocate(byteCount: RangeServer.sliceBytes, alignment: 16)
        defer { buffer.deallocate() }
        while !isClosed {
            guard let lines = head.next(timeout: headerTimeout) else { return }
            lock.lock(); requests += 1; lock.unlock()
            if !answer(c, lines, buffer) { return }
        }
    }

    /// Reads request heads off one connection in chunks, keeping what follows a head for the next request.
    final class HeadReader {
        let socket: Int32
        var data = [UInt8]()

        init(_ s: Int32) { socket = s }

        /// The request line and headers (ASCII, up to the empty line); nil when the client closed, the head is longer than
        /// maxHeaderBytes, is not ASCII, or did not arrive whole within `timeout`.
        func next(timeout: TimeInterval) -> [String]? {
            let deadline = Date().addingTimeInterval(timeout)
            var chunk = [UInt8](repeating: 0, count: 4096)
            while true {
                if let end = RangeServer.headEnd(data) {
                    let head = Array(data[0..<end])
                    data.removeFirst(end + 4)
                    guard end + 4 <= RangeServer.maxHeaderBytes, !head.contains(where: { $0 >= 128 }) else { return nil }
                    return String(decoding: head, as: UTF8.self).components(separatedBy: "\r\n").filter { !$0.isEmpty }
                }
                if data.count > RangeServer.maxHeaderBytes || data.contains(where: { $0 >= 128 }) { return nil }
                let left = deadline.timeIntervalSinceNow
                if left <= 0 { return nil }
                var tv = timeval(tv_sec: Int(left), tv_usec: Int32((left - floor(left)) * 1_000_000))
                if tv.tv_sec == 0 && tv.tv_usec == 0 { tv.tv_usec = 1000 }
                setsockopt(socket, SOL_SOCKET, SO_RCVTIMEO, &tv, socklen_t(MemoryLayout<timeval>.size))
                let n = chunk.withUnsafeMutableBytes { recv(socket, $0.baseAddress, 4096, 0) }
                if n < 0 && errno == EINTR { continue }
                if n <= 0 { return nil }   // closed, reset or timed out
                data += chunk[0..<n]
            }
        }
    }

    static func headEnd(_ d: [UInt8]) -> Int? {
        guard d.count >= 4 else { return nil }
        for i in 0...(d.count - 4) where d[i] == 13 && d[i + 1] == 10 && d[i + 2] == 13 && d[i + 3] == 10 { return i }
        return nil
    }

    /// One request. False = close the connection.
    private func answer(_ c: Int32, _ head: [String], _ buffer: UnsafeMutableRawPointer) -> Bool {
        let line = head.first?.split(separator: " ", omittingEmptySubsequences: false).map(String.init) ?? []
        guard line.count == 3, line[2].hasPrefix("HTTP/1.") else { return status(c, 400, "Bad Request", close: true) }
        let method = line[0], target = line[1]
        var headers: [String: String] = [:]
        for h in head.dropFirst() {
            guard let colon = h.firstIndex(of: ":"), colon != h.startIndex else { return status(c, 400, "Bad Request", close: true) }
            headers[h[..<colon].trimmingCharacters(in: .whitespaces).lowercased()] = h[h.index(after: colon)...].trimmingCharacters(in: .whitespaces)
        }
        let close = line[2] == "HTTP/1.0" || headers["connection"]?.lowercased() == "close"
        guard method == "GET" || method == "HEAD" else {
            return status(c, 405, "Method Not Allowed", close: true, extra: "Allow: GET, HEAD\r\n")
        }
        // Exactly 127.0.0.1:<port> (a web page reaching it through DNS rebinding sends its own name).
        guard headers["host"]?.lowercased() == "127.0.0.1:\(port)" else { return status(c, 400, "Bad Request", close: true) }
        guard let parsed = RangeServer.parseTarget(target, token: tokenHex) else { return status(c, 404, "Not Found", close: close) }
        lock.lock(); let entry = files[parsed.id]; lock.unlock()
        guard let entry else { return status(c, 404, "Not Found", close: close) }
        let length = entry.source.length
        var start: Int64 = 0, end = length - 1, partial = false
        if let range = headers["range"] {
            guard let r = RangeServer.parseRange(range, length: length) else {
                return status(c, 416, "Range Not Satisfiable", close: close, extra: "Content-Range: bytes */\(length)\r\n")
            }
            (start, end, partial) = (r.0, r.1, true)
        }
        let count = max(0, end - start + 1)
        var h = partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n"
        h += "Content-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\nContent-Length: \(count)\r\n"
        if partial { h += "Content-Range: bytes \(start)-\(end)/\(length)\r\n" }
        h += close ? "Connection: close\r\n\r\n" : "Connection: keep-alive\r\n\r\n"
        guard sendAll(c, Array(h.utf8)) else { return false }
        if method == "HEAD" { return !close }
        var at = start
        while at <= end {
            if isClosed { return false }
            let want = Int(min(Int64(RangeServer.sliceBytes), end - at + 1))
            let n: Int
            do { n = try entry.source.read(at, buffer, want) } catch { return false }   // headers are out: only a cut says it
            if n <= 0 { return false }   // the file got shorter than it said
            guard sendAll(c, UnsafeRawBufferPointer(start: buffer, count: n)) else { return false }
            lock.lock(); bytesSent += Int64(n); entry.sent += Int64(n); lock.unlock()
            at += Int64(n)
        }
        return !close
    }

    private func sendAll(_ c: Int32, _ bytes: [UInt8]) -> Bool {
        bytes.withUnsafeBytes { sendAll(c, $0) }
    }

    private func sendAll(_ c: Int32, _ bytes: UnsafeRawBufferPointer) -> Bool {
        var done = 0
        while done < bytes.count {
            let n = Darwin.send(c, bytes.baseAddress! + done, bytes.count - done, 0)
            if n < 0 && errno == EINTR { continue }
            if n <= 0 { return false }
            done += n
        }
        return true
    }

    private func status(_ c: Int32, _ code: Int, _ text: String, close: Bool, extra: String = "") -> Bool {
        _ = sendAll(c, Array("HTTP/1.1 \(code) \(text)\r\n\(extra)Content-Length: 0\r\n\(close ? "Connection: close\r\n" : "")\r\n".utf8))
        return !close
    }

    /// The file a target names: "/<token>/<id>/<name>" with this server's token (constant-time compare); else nil. The
    /// name is never used for anything but the extension ffmpeg sees.
    public static func parseTarget(_ target: String, token: String) -> (id: String, name: String)? {
        let parts = target.split(separator: "/", omittingEmptySubsequences: false).map(String.init)
        guard parts.count == 4, parts[0].isEmpty, parts[1].count == token.count, !parts[2].isEmpty else { return nil }
        let a = Array(parts[1].utf8), b = Array(token.utf8)
        var diff: UInt8 = 0
        for i in 0..<a.count { diff |= a[i] ^ b[i] }
        guard diff == 0 else { return nil }
        guard parts[2].allSatisfy({ $0.isHexDigit }) else { return nil }
        return (parts[2], parts[3])
    }

    /// One range: "bytes=a-b", "bytes=a-", "bytes=-n" within `length`; nil for anything else (several ranges included).
    public static func parseRange(_ value: String, length: Int64) -> (Int64, Int64)? {
        guard value.lowercased().hasPrefix("bytes=") else { return nil }
        let spec = value.dropFirst(6).trimmingCharacters(in: .whitespaces)
        guard !spec.contains(","), let dash = spec.firstIndex(of: "-") else { return nil }
        let a = spec[..<dash].trimmingCharacters(in: .whitespaces), b = spec[spec.index(after: dash)...].trimmingCharacters(in: .whitespaces)
        func num(_ s: String) -> Int64? { s.isEmpty || !s.allSatisfy(\.isASCIIDigit) ? nil : Int64(s) }
        if a.isEmpty {
            guard let suffix = num(b), suffix > 0, length > 0 else { return nil }
            return (max(0, length - suffix), length - 1)
        }
        guard let start = num(a), start < length else { return nil }
        if b.isEmpty { return (start, length - 1) }
        guard let end = num(b), end >= start else { return nil }
        return (start, min(end, length - 1))
    }
}

private extension Character {
    var isASCIIDigit: Bool { ("0"..."9").contains(self) }
}
