import XCTest
@testable import OffloadKit

final class BrakeMeterTests: XCTestCase {
    private let chunk: Int64 = 16 << 20

    /// A copy that moves `rate` bytes/s through a lockstep engine with a fixed backlog per writer,
    /// quantised by whole chunks like `DestWriter` (it reports only finished blocks).
    private func run(_ m: BrakeMeter, seconds: Double, rate: Int64, lags: [Int64]) -> Brake? {
        var last: Brake?
        var t = 0.0
        while t <= seconds + 1e-9 {
            let read = Int64(Double(rate) * t) + lags.max()! * chunk
            last = m.add(seconds: t, readBytes: read, writtenBytes: lags.map { (read - $0 * chunk) / chunk * chunk })
            t += 0.5
        }
        return last
    }

    func testLockstepEqualRatesLaggingWriterBrakes() {
        let b = run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 57_000_000, lags: [0, 15])
        XCTAssertEqual(b?.element, 2)
        XCTAssertTrue((50...65).contains(b!.mbps), "\(b!.mbps)")
    }

    func testAllLagsSmallCardBrakes() {
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 300_000_000, lags: [1, 0, 1])?.element, 0)
    }

    func testFirstOfEqualLagsAndNeverTheCardOnATie() {
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 100_000_000, lags: [14, 14])?.element, 1)
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 100_000_000, lags: [3, 0])?.element, 1)
    }

    // At the start the pool is still filling: the slow writer's backlog grows before it is large on average.
    func testGrowingBacklogWriterBrakesBeforeThePoolIsFull() {
        let m = BrakeMeter(chunkBytes: chunk, bufferCount: 16)
        var b: Brake?
        var t = 0.0
        while t <= 2.0 + 1e-9 {
            b = m.add(seconds: t, readBytes: Int64(100_000_000 * t), writtenBytes: [Int64(60_000_000 * t)])   // card 100, disk 60 MB/s
            t += 0.5
        }
        XCTAssertEqual(b?.element, 1)
    }

    func testSmallPoolThresholdIsHalfThePool() {
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 2), seconds: 6, rate: 50_000_000, lags: [1])?.element, 1)
        XCTAssertEqual(run(BrakeMeter(chunkBytes: chunk, bufferCount: 16), seconds: 6, rate: 50_000_000, lags: [1])?.element, 0)
    }

    func testLinkFullFromTheBrakingElementsLink() {
        var b = run(BrakeMeter(chunkBytes: chunk, bufferCount: 16, linkMBps: [450, 40]), seconds: 6, rate: 38_000_000, lags: [15])
        XCTAssertEqual(b?.element, 1)
        XCTAssertEqual(b?.linkFull, true)
        b = run(BrakeMeter(chunkBytes: chunk, bufferCount: 16, linkMBps: [450, nil]), seconds: 6, rate: 38_000_000, lags: [15])
        XCTAssertEqual(b?.linkFull, false)
        XCTAssertNil(b?.utilization)
    }

    func testNoVerdictUntilHalfTheWindowAndSmoothedOverIt() {
        let m = BrakeMeter(chunkBytes: chunk, bufferCount: 16, windowSeconds: 4)
        XCTAssertNil(m.add(seconds: 0, readBytes: 0, writtenBytes: [0]))
        XCTAssertNil(m.add(seconds: 1, readBytes: 100 * chunk, writtenBytes: [100 * chunk - 15 * chunk]))
        XCTAssertEqual(m.add(seconds: 2, readBytes: 200 * chunk, writtenBytes: [200 * chunk - 15 * chunk])?.element, 1)
        // One momentary catch-up of the writer does not flip the verdict.
        XCTAssertEqual(m.add(seconds: 2.5, readBytes: 210 * chunk, writtenBytes: [210 * chunk])?.element, 1)
    }
}
