import Foundation

/// Task 70 (Windows R11): the verify-in-proxy choices of the CLI and the settings.
public enum VerifyOptions {
    /// Settings `verifyPrimary`: "fastest" (default) or "first"; any other value reads as "fastest".
    public static let fastest = "fastest"
    public static let first = "first"

    public static func primarySetting(_ s: String?) -> String { s == first ? first : fastest }

    /// CLI `--primary N` (1 … the number of `--dest`) as a 0-based index; nil without it. Out of range: the message (exit 1),
    /// never a silent fall back to the fastest destination.
    public static func primaryIndex(_ n: Int?, destinations: Int) throws -> Int? {
        guard let n else { return nil }
        if n < 1 { throw OffloadError.preflight(VIPMsg.primaryNotPositive(n)) }
        if n > destinations { throw OffloadError.preflight(VIPMsg.primaryTooHigh(n, destinations)) }
        return n - 1
    }

    /// The CLI's exit code after a copy or an append (spec D.22.7): 0 only when the card may be formatted — every copy
    /// verified everywhere and the result written (the whole backup for an append); "Nothing new" ends Done without a
    /// report: 0 when the backup is complete. With --no-verify nothing can be verified: every file copied is success.
    public static func exitCode(verify: Bool, _ status: JobStatus, _ report: OffloadReport?) -> Int32 {
        guard status.phase == .done else { return 1 }
        guard let report else { return status.note != nil && status.safeToFormat ? 0 : 1 }
        return report.success && (status.safeToFormat || !verify) ? 0 : 1
    }
}
