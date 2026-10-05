import XCTest
@testable import OffloadKit

final class JobProgressTests: XCTestCase {
    private let t0 = Date(timeIntervalSince1970: 1_791_201_600)

    private func snap(_ read: Int64, _ written: [Int64], _ verified: [Int64]? = nil, total: Int64 = 1_000_000_000) -> ProgressSnapshot {
        ProgressSnapshot(phase: .copy, totalBytes: total, totalFiles: 10, fileIndex: 1, currentFile: "DCIM/100CANON/A001.MOV",
                         readBytes: read, writtenBytes: written, verifiedBytes: verified ?? written.map { _ in 0 }, errors: 0)
    }

    private func st(_ phase: JobStatus.Phase, _ copy: ProgressSnapshot? = nil) -> JobStatus {
        var s = JobStatus()
        s.phase = phase
        s.copy = copy
        return s
    }

    private func copying() -> JobProgress {
        let p = JobProgress(now: t0)
        p.update(st(.copying, snap(0, [0, 0])), now: t0 + 0.5)
        p.update(st(.copying, snap(100_000_000, [100_000_000, 50_000_000])), now: t0 + 1.5)
        p.update(st(.copying, snap(150_000_000, [150_000_000, 100_000_000])), now: t0 + 2.5)
        return p
    }

    func testRatesResetOnPhaseChangeThenSmoothed() {
        let p = copying()
        XCTAssertEqual(p.readRate, 80e6, accuracy: 1)     // 0.6 × 100 + 0.4 × 50 MB/s
        XCTAssertEqual(p.destRates[0], 80e6, accuracy: 1)
        XCTAssertEqual(p.destRates[1], 50e6, accuracy: 1)
        XCTAssertEqual(p.eta!, 850e6 / 80e6, accuracy: 0.001)
        p.update(st(.verifying, snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [0, 0])), now: t0 + 3.5)
        XCTAssertEqual(p.readRate, 0)
        XCTAssertEqual(p.destRates, [0, 0])
    }

    func testUpdatesCloserThan200msKeepRates() {
        let p = copying()
        p.update(st(.copying, snap(900_000_000, [900_000_000, 900_000_000])), now: t0 + 2.6)
        XCTAssertEqual(p.readRate, 80e6, accuracy: 1)
    }

    func testPhaseChangeInsideSkippedUpdateResetsRates() {
        let p = copying()
        p.update(st(.verifying, snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [0, 0])), now: t0 + 2.6)
        p.update(st(.verifying, snap(1_000_000_000, [1_000_000_000, 1_000_000_000], [100_000_000, 100_000_000])), now: t0 + 3.6)
        XCTAssertEqual(p.readRate, 0)
        XCTAssertTrue(p.destRates.allSatisfy { $0 >= 0 })
    }

    func testVerifyingEtaFromTheSlowestDestination() {
        let p = JobProgress(now: t0)
        p.update(st(.verifying, snap(0, [0, 0], [0, 0])), now: t0 + 0.5)
        p.update(st(.verifying, snap(0, [0, 0], [200_000_000, 100_000_000])), now: t0 + 1.5)
        XCTAssertEqual(p.readRate, 100e6, accuracy: 1)
        XCTAssertEqual(p.eta!, 9.0, accuracy: 0.001)
        XCTAssertEqual(p.detailText, "D1 200 MB/s · D2 100 MB/s · zbývá 0:09")
    }

    func testPhaseProgressEachPhase() {
        let p = JobProgress(now: t0)
        p.update(st(.copying, snap(250_000_000, [0])), now: t0)
        XCTAssertEqual(p.phaseProgress, 0.25, accuracy: 0.001)
        p.update(st(.verifying, snap(0, [0, 0], [500_000_000, 250_000_000])), now: t0)
        XCTAssertEqual(p.phaseProgress, 0.375, accuracy: 0.001)
        var a = JobStatus()
        a.phase = .analyzing
        a.clipIndex = 1
        a.clipCount = 4
        a.clipFraction = 0.5
        p.update(a, now: t0)
        XCTAssertEqual(p.phaseProgress, 0.375, accuracy: 0.001)
        p.update(st(.done), now: t0)
        XCTAssertEqual(p.phaseProgress, 1)
        p.update(JobStatus(), now: t0)
        XCTAssertEqual(p.phaseProgress, 0)
    }

    func testDetailTexts() {
        XCTAssertEqual(copying().detailText, "Karta 80 MB/s · D1 80 MB/s · D2 50 MB/s · zbývá 0:10 · A001.MOV")
        let p = JobProgress(now: t0)
        var s = JobStatus()
        s.phase = .proxies
        s.clipIndex = 2
        s.clipCount = 5
        s.clipName = "A003"
        s.clipFraction = 0.426
        p.update(s, now: t0)
        XCTAssertEqual(p.detailText, "Proxy 3/5 · A003 · 43 %")
        s.phase = .analyzing
        s.clipIndex = 0
        s.clipCount = 3
        s.clipName = "A001"
        p.update(s, now: t0)
        XCTAssertEqual(p.detailText, "Klip 1/3 · A001")
        p.update(st(.flushing), now: t0)
        XCTAssertEqual(p.detailText, "Zápis cache disků…")
    }

    func testEtaText() {
        XCTAssertEqual(JobProgress.etaText(59), "0:59")
        XCTAssertEqual(JobProgress.etaText(600), "10:00")
        XCTAssertEqual(JobProgress.etaText(3725), "1:02:05")
        XCTAssertEqual(JobProgress.etaText(-5), "0:00")
        XCTAssertEqual(JobProgress.etaText(.nan), "0:00")
    }

    func testStatusTextToneAndErrorNotForCancelled() {
        let cases: [(JobStatus.Phase, Bool?, String, CardTone, Bool)] = [
            (.done, false, "Chyba", .error, true),
            (.done, true, "Hotovo", .ok, false),
            (.failed, nil, "Chyba", .error, true),
            (.cancelled, nil, "Zrušeno", .warning, false),
            (.cancelled, false, "Zrušeno", .warning, false),
            (.copying, nil, "Kopírování", .active, false),
            (.waiting, nil, "Čeká", .waiting, false),
        ]
        for (phase, ok, text, tone, error) in cases {
            let p = JobProgress(now: t0)
            var s = JobStatus()
            s.phase = phase
            s.copySucceeded = ok
            p.update(s, now: t0)
            XCTAssertEqual(p.statusText, text, "\(phase)")
            XCTAssertEqual(p.tone, tone, "\(phase)")
            XCTAssertEqual(p.isError, error, "\(phase)")
        }
    }

    func testBrakeOnlyWhileCopying() {
        let mb: Int64 = 1_000_000
        let p = JobProgress(now: t0)
        p.attachBrakeMeter(BrakeMeter(chunkBytes: 16 << 20, bufferCount: 4))
        for i in 0...8 {
            let read = Int64(i) * 50 * mb
            p.update(st(.copying, snap(read, [read, max(0, read - 48 * mb)])), now: t0 + Double(i) * 0.5)
        }
        XCTAssertEqual(p.brake?.element, 2)
        p.update(st(.verifying, snap(400 * mb, [400 * mb, 400 * mb])), now: t0 + 5)
        XCTAssertNil(p.brake)
    }

    func testSummaryText() {
        let clip = Clip(name: "A001", path: "A001.MOV", format: .video, segments: ["A001.MOV"], bytes: 1, isCameraProxy: false)
        func entry(_ s: ProxyStatus) -> ClipEntry { var e = ClipEntry(clip: clip); e.proxyStatus = s; return e }
        var r = OffloadReport(cardName: "A001", date: "2026-10-05", source: "/Volumes/A001", sourceFilesystem: "exfat",
                              started: t0, copyFinished: t0 + 10, finished: t0 + 15, verified: true,
                              totalFiles: 3, totalBytes: 1_000_000_000, destinations: [], files: [], skipped: [])
        r.clips = [entry(.done), entry(.failed), entry(.unsupported)]
        var p = ProxySettings()
        p.enabled = true
        r.proxy = p
        XCTAssertEqual(JobProgress.summaryText(r), "kopie 100 MB/s · ověření 200 MB/s · 3 klipy · proxy 1, 1 chyba, 1 nepodporováno")
    }

    func testCzechPlurals() {
        XCTAssertEqual(Texts.cards(0), "Vyberte kartu")
        XCTAssertEqual(Texts.cards(1), "1 karta")
        XCTAssertEqual(Texts.cards(3), "3 karty")
        XCTAssertEqual(Texts.cards(5), "5 karet")
        XCTAssertEqual(Texts.destinations(0), "0 cílů")
        XCTAssertEqual(Texts.destinations(1), "1 cíl")
        XCTAssertEqual(Texts.destinations(2), "2 cíle")
        XCTAssertEqual(Texts.files(1, 1000), "1 soubor · 1.00 KB")
        XCTAssertEqual(Texts.files(12, 2_500_000_000), "12 souborů · 2.50 GB")
        XCTAssertEqual(Texts.clips(1), "1 klip")
        XCTAssertEqual(Texts.clips(4), "4 klipy")
        XCTAssertEqual(Texts.clips(7), "7 klipů")
        XCTAssertEqual(Texts.safeToEject(1), "Všechny soubory jsou na 1 disku a ověřené — kartu lze vysunout.")
        XCTAssertEqual(Texts.safeToEject(2), "Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.")
        XCTAssertEqual(Texts.failed(3), "3 soubory selhaly — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.")
        XCTAssertEqual(Texts.failed(1), "1 soubor selhal — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.")
        XCTAssertEqual(Texts.warnings(0), "bez varování")
        XCTAssertEqual(Texts.warnings(2), "2 varování")
        XCTAssertEqual(Texts.cancelled, "Zrušeno — karta není kompletně zálohovaná.")
    }
}
