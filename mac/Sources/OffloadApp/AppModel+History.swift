import AppKit
import OffloadKit
import SwiftUI

/// The right column's tabs.
enum QueueTab: Hashable { case queue, history }

/// "Today" under the queue, the "Card history" tab with ticks for the summary report, the camera label of a card and
/// the time estimate of a start — as Windows (UI návrh, nové věci 6, 7, 10).
extension AppModel {
    /// The cards finished today (from history.json as loaded); nil hides the section.
    var today: HistoryDay? { history.flatMap { HistoryView.today($0, now: Date()) } }

    /// Every day with a card, newest first.
    var historyDays: [HistoryDay] { history.map { HistoryView.days($0, now: Date()) } ?? [] }

    func isInSummary(_ id: String) -> Bool { summarySelection.contains(id) }

    func setInSummary(_ id: String, _ on: Bool) {
        if on { summarySelection.insert(id) } else { summarySelection.remove(id) }
    }

    func clearSummarySelection() { summarySelection.removeAll() }

    /// "Summary report (3)…", "Summary report…" with nothing ticked, "Making summary report…" while one is made.
    var summaryButtonText: String {
        makingSummary ? "Making summary report…"
            : summarySelection.isEmpty ? "Summary report…" : "Summary report (\(summarySelection.count))…"
    }

    /// One clip of the card names the camera — off the main thread, reading only, ffprobe for at most 5 s. The label is
    /// kept only while the card is still listed with this look.
    func probeCamera(_ path: String, _ insight: CardInsight) {
        guard !isPreview else { return }
        let tools = self.tools
        let identity = insight.volume?.identity
        inspectQueue.async { [weak self] in
            let label = CameraHints.probe(insight, tools: tools, catalog: LutCatalog.shared)?.text
            DispatchQueue.main.async {
                guard let self, self.allSources.contains(path), self.insights[path]?.volume?.identity == identity else { return }
                self.cameraLabels[path] = label
            }
        }
    }

    // MARK: Day report

    /// After a job (also an append, a cancelled or failed one): the day report on each of its destinations, off the main
    /// thread; a busy day is skipped after 5 s, a failure only logged (FORMATY „Denní report“).
    func writeDayReports(_ item: JobItem) {
        guard !isPreview, item.job.config.dayReport, item.job.plan != nil else { return }
        dayReportsRunning += 1
        dayQueue.async { [weak self] in
            item.job.writeDayReports(log: { NSLog("%@", $0) })
            DispatchQueue.main.async {
                guard let self else { return }
                self.dayReportsRunning -= 1
                self.checkTerminateReady()
            }
        }
    }

    /// "Day report…": a day folder (or a card folder in it) on a destination — written now and opened.
    func dayReportOnRequest() {
        guard !isPreview, !terminating else { return }
        let panel = NSOpenPanel()
        panel.title = "Choose a day folder on a destination (or a card folder in it)"
        panel.prompt = "Day report"
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let path = url.path
        dayReportsRunning += 1
        dayQueue.async { [weak self] in
            let result = Result { try DayReportService.regenerate(path, log: { NSLog("%@", $0) }) }
            DispatchQueue.main.async {
                guard let self else { return }
                self.dayReportsRunning -= 1
                switch result {
                case .success(let html): NSWorkspace.shared.open(URL(fileURLWithPath: html))
                case .failure(let e): self.message = Msg.dayReportFailed("\(e)")
                }
                self.checkTerminateReady()
            }
        }
    }

    /// "estimate ~8:00 incl. proxies" — only from what this session measured (RateMemory); nil shows only the size.
    var startEstimateText: String? {
        guard let bytes = selectedBytes, bytes > 0 else { return nil }
        let clips = selectedInOrder.flatMap { insights[$0]?.clips ?? [] }
        let s = settings
        return RateMemory.startText(rates.estimate(bytes: bytes, clips: clips, verify: s.verify, analyze: s.analyzeClips, proxy: s.proxy),
                                    proxies: s.proxy.enabled)
    }
}
