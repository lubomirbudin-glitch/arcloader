import AppKit
import WebKit

/// Prints the HTML report into a paginated A4 landscape PDF with WebKit: the same content as the HTML,
/// as one file that archives well and goes to post-production as is.
public enum PDFReport {
    static let pageSize = NSSize(width: 842, height: 595)  // A4 landscape in points
    static let margin: CGFloat = 28

    /// Safe to call from any thread. Rendering happens on the main thread, which must be running
    /// its run loop (the app always does; the CLI keeps it spinning while a job runs).
    public static func write(_ report: OffloadReport, to path: String, timeout: TimeInterval = 60) throws {
        try writeHTML(HTMLReport.render(report, forPrint: true), to: path, timeout: timeout)
    }

    /// Any HTML page (the verification report) as the same A4 landscape PDF.
    public static func writeHTML(_ html: String, to path: String, timeout: TimeInterval = 60) throws {
        var failure: Error?
        let work = {
            do { try MainActor.assumeIsolated { try render(html: html, to: URL(fileURLWithPath: path), timeout: timeout) } }
            catch { failure = error }
        }
        if Thread.isMainThread {
            work()
        } else {
            // Not DispatchQueue.main.sync: WebKit needs the main queue itself while the page loads,
            // so the work runs as a plain run-loop block and this thread waits on a semaphore.
            let done = DispatchSemaphore(value: 0)
            CFRunLoopPerformBlock(CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue) {
                work()
                done.signal()
            }
            CFRunLoopWakeUp(CFRunLoopGetMain())
            done.wait()
        }
        if let failure { throw failure }
    }

    private final class LoadWaiter: NSObject, WKNavigationDelegate {
        var done = false
        var error: Error?
        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) { done = true }
        func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { self.error = error; done = true }
        func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
            self.error = error
            done = true
        }
    }

    @MainActor
    private static func render(html: String, to url: URL, timeout: TimeInterval) throws {
        _ = NSApplication.shared
        let width = pageSize.width - 2 * margin
        // WebKit paints only into a view that sits in a window; an off-screen one is enough.
        let window = NSWindow(contentRect: NSRect(x: -20_000, y: -20_000, width: width, height: 800),
                              styleMask: [.borderless], backing: .buffered, defer: false)
        let web = WKWebView(frame: NSRect(x: 0, y: 0, width: width, height: 800))
        web.appearance = NSAppearance(named: .aqua)
        window.contentView = web
        let waiter = LoadWaiter()
        web.navigationDelegate = waiter
        web.loadHTMLString(html, baseURL: nil)
        let deadline = Date().addingTimeInterval(timeout)
        while !waiter.done && Date() < deadline {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
        }
        if let e = waiter.error { throw e }
        guard waiter.done else { throw OffloadError.failed("PDF: report did not load in \(Int(timeout)) s") }

        // Safety net: if anything is still wider than the page (long file names, many columns), zoom the whole page
        // out so nothing is cut off at the right edge.
        var zoomed = false
        web.evaluateJavaScript("""
            (function(){var d=document.documentElement;var w=d.clientWidth;var s=Math.max(d.scrollWidth,document.body.scrollWidth);
            if(s>w+1){document.body.style.zoom=String(0.98*w/s);}return s;})()
            """) { _, _ in zoomed = true }
        while !zoomed && Date() < deadline {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
        }

        let tmp = url.deletingLastPathComponent().appendingPathComponent("." + url.lastPathComponent + ".tmp")
        try? FileManager.default.removeItem(at: tmp)
        let info = NSPrintInfo(dictionary: [.jobDisposition: NSPrintInfo.JobDisposition.save, .jobSavingURL: tmp])
        info.paperSize = pageSize
        info.orientation = .landscape
        info.topMargin = margin
        info.bottomMargin = margin
        info.leftMargin = margin
        info.rightMargin = margin
        info.horizontalPagination = .fit
        info.verticalPagination = .automatic
        info.isHorizontallyCentered = false
        info.isVerticallyCentered = false

        let op = web.printOperation(with: info)
        op.showsPrintPanel = false
        op.showsProgressPanel = false
        op.view?.frame = web.bounds
        var finished = false
        let delegate = PrintDelegate { finished = true }
        op.runModal(for: window, delegate: delegate, didRun: #selector(PrintDelegate.printOperationDidRun(_:success:contextInfo:)),
                    contextInfo: nil)
        while !finished && Date() < deadline {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
        }
        window.orderOut(nil)
        guard finished, FileManager.default.fileExists(atPath: tmp.path) else {
            throw OffloadError.failed("PDF: printing did not finish")
        }
        try? FileManager.default.removeItem(at: url)
        try FileManager.default.moveItem(at: tmp, to: url)
    }

    private final class PrintDelegate: NSObject {
        let done: () -> Void
        init(_ done: @escaping () -> Void) { self.done = done }
        @objc func printOperationDidRun(_ op: NSPrintOperation, success: Bool, contextInfo: UnsafeMutableRawPointer?) { done() }
    }
}
