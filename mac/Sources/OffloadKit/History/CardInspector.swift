import Foundation

/// What one look at a card found: size, clips, fingerprint and the history entry it matches. One scan serves the history
/// line, the card name suggestion and the space check. `error`: the card could not be read.
public struct CardInsight {
    public var source: String
    /// Nil for a folder that is not a volume root.
    public var volume: CardVolume?
    public var filesystem: String?
    public var totalBytes: Int64 = 0
    public var fileCount = 0
    public var files: [SourceFile] = []
    public var clips: [Clip] = []
    public var fingerprint = ""
    public var shape = ""
    public var match: HistoryMatch?
    /// New files found (or an unfinished backup) and every card folder of the earlier backup exists now: the append is
    /// offered only then. Looked up with the scan, off the main thread.
    public var appendReachable = false
    /// The folder name the clips suggest, or nil.
    public var nameSuggestion: NameSuggestion?
    /// "The card has more than one reel: A001, A002" when the clips disagree.
    public var nameNote: String?
    public var error: String?

    public init(source: String) { self.source = source }

    /// A card emptied in the camera after the backup: history still knows it (identical, everything removed).
    public var isEmptied: Bool { fileCount == 0 && (match?.removed ?? 0) > 0 }
}

public enum CardInspector {
    /// Reads only (a folder listing — nothing is ever written to the card). Never on the main thread. `history` nil =
    /// not loaded (or unreadable): no history match. Throws only CancellationError (`isCancelled`, e.g. the card was
    /// swapped); a card that cannot be read comes back with `error`.
    public static func inspect(_ source: String, store: HistoryStore?, history: [HistoryEntry]?,
                               isCancelled: () -> Bool = { false }) throws -> CardInsight {
        try inspect(source, store: store, history: history, isCancelled: isCancelled,
                    volumeOf: CardVolume.of, identify: CardIdentity.of, isVolumeRoot: isVolumeRoot)
    }

    /// The disk reads replaceable in tests.
    static func inspect(_ source: String, store: HistoryStore?, history: [HistoryEntry]?, isCancelled: () -> Bool,
                        volumeOf: (String) -> CardVolume?, identify: (String) -> String?, isVolumeRoot: (String) -> Bool,
                        directoryExists: (String) -> Bool = { ProxyPreflight.isDirectory($0) }) throws -> CardInsight {
        var r = CardInsight(source: source)
        if isCancelled() { throw CancellationError() }
        let root = isVolumeRoot(source)
        let volume = root ? volumeOf(source) : nil
        // A volume root whose volume cannot be read is a card that cannot be read — not a folder to match by shape.
        if root && volume == nil {
            r.error = Msg.cardVolumeUnreadable
            return r
        }
        let filesystem = volume?.filesystem ?? IO.filesystemType(source)
        let scan: SourceScan
        do {
            scan = try Scanner.scan(source, isCancelled: isCancelled)
        } catch is CancellationError {
            throw CancellationError()
        } catch OffloadError.preflight(let message, code: .noReadAccess) {
            r.error = message   // how to allow it, not "Cannot read the card: …"
            return r
        } catch {
            r.error = Msg.cardUnreadable("\(error)")
            return r
        }
        let (fingerprint, shape) = CardFingerprint.compute(scan.files)
        if isCancelled() { throw CancellationError() }
        // Another card in the same reader while it was scanned: what was read belongs to neither.
        if let volume, let id = volume.identity, identify(source) != id {
            r.error = Msg.cardSwapped
            return r
        }
        var match: HistoryMatch?
        if let history, let candidate = HistoryStore.findForCard(history, volume?.key, shape: shape) {
            let list = candidate.fingerprint == fingerprint ? nil : store?.readFileList(candidate)
            match = HistoryMatcher.match(scan, fingerprint: fingerprint, candidate: candidate, candidateFiles: list, filesystem: filesystem)
        }
        if let m = match, volume != nil, m.kind == .newFiles || (m.kind == .identical && m.entry.unfinished),
           !m.entry.destinations.isEmpty {
            r.appendReachable = m.entry.destinations.allSatisfy { !$0.cardPath.isEmpty && directoryExists($0.cardPath) }
        }
        if isCancelled() { throw CancellationError() }
        let clips = ClipDetector.detect(scan.files)
        let (name, note, _) = ReelSuggestion.fromClips(clips)
        r.volume = volume
        r.filesystem = filesystem
        r.totalBytes = scan.totalBytes
        r.fileCount = scan.files.count
        r.files = scan.files
        r.clips = clips
        r.fingerprint = fingerprint
        r.shape = shape
        r.match = match
        r.nameSuggestion = name
        r.nameNote = note
        return r
    }

    /// The source is the root of a mounted volume (a card), not a folder on it.
    public static func isVolumeRoot(_ source: String) -> Bool {
        let real = VolumeFacts.realPath(source).trimmingTrailingSlash()
        guard let m = VolumeFacts.mount(of: real) else { return false }
        return m.mountPoint.trimmingTrailingSlash() == real && real != "/"
    }
}
