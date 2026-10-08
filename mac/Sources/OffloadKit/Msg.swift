import Foundation

/// Which check before the copy stopped a job (port of Windows `PreflightCode`). Tests and the GUI tell the cases apart
/// by the code, never by the text.
public enum PreflightCode: String, Equatable {
    case other
    case chunkSize
    case bufferCount
    case date
    case sourceNotFound
    case noDestination
    case duplicateDestination
    case sourceIsOffload
    case sourceUnreadable
    case destinationNotFound
    case destinationNotWritable
    case destinationUnreadable
    case sameVolumeAsSource
    case sharedVolume
    case emptySource
    case noSpace
    case noHashRecord
    case noFilesToRead
    case appendUnavailable
    case appendNoReport
    case appendNoMhl
    case appendDiverged
    case appendConflict
    case appendMissingFile
    case appendFailedFile
    case appendOtherCard
    case appendMhlDamaged
    case appendBusy
    /// Not an error: the backup already has everything (the job ends "Hotovo").
    case nothingNew
}

/// Every message a user sees from checks and validation (GUI job card, CLI) — Czech, one function per message, word for
/// word as Windows `Msg` (spec 2.6). Paths are written the Mac way ("/"). Tool logs, the TXT report (shared format) and
/// `--help` stay English.
public enum Msg {
    public static func chunkSize(_ n: Int) -> String { "Velikost bloku musí být násobek 4096 B a nejvýš 1 GiB (zadáno \(n))." }
    public static func bufferCount(_ n: Int) -> String { "Počet bufferů musí být aspoň 2 (zadáno \(n))." }
    public static func date(_ d: String) -> String { "Datum musí být ve tvaru RRRR-MM-DD (zadáno „\(d)“)." }
    public static func sourceNotFound(_ path: String) -> String { "Zdroj nenalezen: \(path)" }
    public static let noDestination = "Není zadaný žádný cíl."
    public static func duplicateDestination(_ root: String) -> String { "Cíl je zadaný dvakrát: \(root)" }
    public static let sourceIsOffload =
        "Zdroj vypadá jako dřívější offload (má _REPORT, _PROXY nebo ascmhl) — kopírujte původní kartu, nebo tyto složky odeberte."
    public static func sourceUnreadable(_ path: String) -> String { "Zdroj nejde přečíst: \(path)" }
    public static func destinationNotFound(_ root: String) -> String { "Cíl nenalezen: \(root) — je disk připojený?" }
    public static func destinationNotWritable(_ root: String) -> String { "Na cíl nejde zapisovat: \(root)" }
    public static func destinationUnreadable(_ root: String) -> String { "Cíl nejde přečíst: \(root)" }
    public static func sameVolumeAsSource(_ root: String) -> String { "Cíl \(root) je na stejném disku jako zdroj." }
    public static func sharedVolume(_ root: String) -> String {
        "Dva cíle jsou na stejném disku (\(root)) — to není druhá kopie. Povolit jde volbou --allow-same-device."
    }
    public static let emptySource = "Na zdroji nejsou žádné soubory."

    /// "Na cíli Zaloha (RAID_A) není dost místa: potřeba 412 GB (karty 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB."
    public static func noSpace(_ place: String, _ need: SpaceNeed, _ free: Int64) -> String {
        "Na cíli \(place) není dost místa: potřeba \(bytes(need.total)) (\(parts(need, "karty"))), volno \(bytes(free))."
    }

    /// CLI --dry-run: "Místo: /Volumes/RAID_A potřeba 412 GB (karta 380 GB, proxy ~30 GB, report 2 GB), volno 398 GB"
    public static func spaceLine(_ place: String, _ need: SpaceNeed, _ free: Int64) -> String {
        "Místo: \(place) potřeba \(bytes(need.total)) (\(parts(need, "karta"))), volno \(bytes(free))"
    }

    static func parts(_ need: SpaceNeed, _ cards: String) -> String {
        var p: [String] = []
        if need.card > 0 { p.append("\(cards) \(bytes(need.card))") }
        if need.proxy > 0 { p.append("proxy ~\(bytes(need.proxy))") }
        if need.report > 0 { p.append("report \(bytes(need.report))") }
        return p.joined(separator: ", ")
    }

    public static func lowSpace(_ place: String, _ left: Int64) -> String {
        "Na cíli \(place) zbude po offloadu jen \(bytes(left)) — rezerva pod 5 %, odhad proxy je přibližný."
    }
    public static func proxiesSkippedForSpace(_ place: String, _ need: Int64, _ free: Int64) -> String {
        "Proxy se nevyrenderovaly: na \(place) chybí místo (potřeba ~\(bytes(need)), volno \(bytes(free)))."
    }
    public static func reportNotWritten(_ details: String) -> String { "Report se nepodařilo zapsat: \(details)" }
    public static func noHashRecord(_ dir: String) -> String { "Ve složce \(dir) není ASC MHL ani seznam součtů — není s čím porovnat." }
    public static func noFilesToRead(_ path: String) -> String { "V \(path) nejsou soubory ke čtení." }
    public static let cardVolumeUnreadable = "Karta se nedá přečíst."
    public static let cardInspectFailed = "Kartu se nepodařilo prověřit."
    public static let cardSwapped = "Karta se během čtení vyměnila."
    public static let cardNameAutoNone = "Návrh názvu nenalezen, použije se jmenovka svazku."
    public static func cardNameAuto(_ name: String) -> String { "Název karty: \(name) (podle klipů)" }
    public static func cardUnreadable(_ detail: String) -> String { "Kartu nejde přečíst: \(detail)" }

    /// Where a backup is checked on the Mac: the Nástroje menu of the window, or the CLI (Windows names its top bar).
    public static let verifyHint = "Nástroje → Ověřit zálohu… (v příkazové řádce offload verify)"

    // MARK: Append (spec 2.2)

    public static func appendUnavailable(_ cardPath: String) -> String {
        "Dřívější záloha není dostupná: \(cardPath) — připojte disk, nebo udělejte celou kopii."
    }
    public static func appendNoReport(_ cardPath: String) -> String {
        "V \(cardPath) chybí report dřívější zálohy (_REPORT/offload_report.json) — doplnit nejde."
    }
    public static func appendBadReport(_ cardPath: String, _ detail: String) -> String {
        "Report dřívější zálohy v \(cardPath) nejde přečíst (\(detail)) — doplnit nejde."
    }
    public static func appendNoMhl(_ cardPath: String) -> String {
        "V \(cardPath) chybí ASC MHL (ascmhl/ascmhl_chain.xml) — doplnit nejde."
    }
    public static func appendDiverged(_ details: String) -> String {
        "Zálohy na cílech se liší (\(details)) — zkontrolujte je přes \(verifyHint); doplnit nejde."
    }
    /// The numbered reports and the ASC MHL chain of one card folder disagree (an append stopped half way).
    public static func appendReportsMismatch(_ cardPath: String, _ report: Int, _ mhl: Int) -> String {
        "V \(cardPath) nesouhlasí reporty s ASC MHL (poslední report je generace \(report), poslední generace MHL \(mhl)) — dřívější doplnění se nedokončilo. "
            + "Zkontrolujte zálohu přes \(verifyHint) a složku opravte ručně; doplnit nejde."
    }
    public static func appendNotACardFolder(_ path: String) -> String {
        "\(path) není složka karty v záloze (<cíl>/<datum>/<karta>) — doplnit nejde."
    }
    public static func appendDifferentFolders(_ cardPaths: [String]) -> String {
        "Složky dřívější zálohy se jmenují různě (\(cardPaths.joined(separator: ", "))) — doplnit jde jen do složek se stejným datem a názvem karty."
    }
    public static func appendConflict(_ lines: [String], _ fullCopyFolder: String) -> String {
        "Doplnění zastaveno — nic se nezkopírovalo. \(lines.count) "
            + Texts.plural(lines.count, "soubor se neshoduje", "soubory se neshodují", "souborů se neshoduje")
            + " se zálohou:\n  " + lines.prefix(10).joined(separator: "\n  ") + (lines.count > 10 ? "\n  …" : "")
            + "\nUdělejte celou kopii (vznikne \(fullCopyFolder))."
    }
    public static func appendMissingFile(_ disk: String, _ path: String) -> String {
        "Na \(disk) chybí soubor z dřívější zálohy: \(path) — zkontrolujte zálohu přes \(verifyHint)"
    }
    public static func appendFailedFile(_ path: String, _ disk: String) -> String {
        "\(path) se při dřívější záloze na \(disk) nezkopíroval správně — doplnit nejde, udělejte celou kopii."
    }
    public static let appendOtherCard = "Na kartě není žádný soubor z dřívější zálohy — jde o jinou kartu? Udělejte celou kopii."
    public static func appendMhlDamaged(_ cardPath: String) -> String {
        "Historie ASC MHL v \(cardPath) je poškozená — zkontrolujte ji přes \(verifyHint); doplnit nejde."
    }
    /// One line of the appendConflict list: a file on a destination that no report records (a crashed append leaves one).
    public static func appendUnknownFile(_ disk: String, _ path: String) -> String {
        "na \(disk) je soubor \(path), který žádný report nezná (nedokončené doplnění?)"
    }
    /// One line of the appendConflict list: the temporary copy name of a new file is taken.
    public static func appendPartNameTaken(_ path: String, _ part: String) -> String {
        "\(path) (dočasné jméno kopie \(part) je jiný soubor zálohy nebo karty)"
    }
    public static func hashListNotHealed(_ cardPath: String, _ error: String) -> String {
        "Seznam kontrolních součtů (.xxh64) v \(cardPath) nejde doplnit o chybějící řádky: \(error)"
    }
    public static func appendBusy(_ cardPath: String) -> String {
        "Do této zálohy právě doplňuje jiná úloha (\(cardPath)) — počkejte, až skončí."
    }
    public static let nothingNew = "Nic nového — záloha je úplná."
    public static func unverifiedEarlier(_ n: Int) -> String {
        "\(n) \(Texts.plural(n, "soubor", "soubory", "souborů")) z dřívější zálohy "
            + "\(Texts.plural(n, "nebyl ověřen", "nebyly ověřeny", "nebylo ověřeno")) — doporučeno Ověřit zálohu."
    }
    public static let cardNotInHistory = "Karta není v historii — zadejte složky zálohy volbou --into."
    /// The comment of an append's ASC MHL generation (the MHL is shared with Windows, so the text is too).
    public static func mhlAppendComment(_ files: Int, _ source: String) -> String {
        "Offload doplnění: \(files) \(Texts.plural(files, "nový soubor", "nové soubory", "nových souborů")) z \(source)"
    }

    // MARK: CLI

    public static let cliError = "Chyba: "
    public static func unknownProxyCodec(_ x: String?) -> String {
        "Neznámý kodek proxy „\(x ?? "")“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ."
    }
    public static func lutNotFound(_ path: String) -> String { "LUT nenalezen: \(path)" }
    public static func unknownHash(_ x: String?) -> String {
        "Neznámý kontrolní součet „\(x ?? "")“ — použijte xxh64 nebo both (xxh64 + MD5)."
    }
    public static func proxyFolderNotFound(_ path: String) -> String { "Složka pro proxy nenalezena: \(path)" }
    public static let proxyFolderNoEffect = "Poznámka: --proxy-folder bez --proxy nic nedělá."
    public static let cancelling = "Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…"
    public static func notPositiveNumber(_ s: String?) -> String { "„\(s ?? "")“ není kladné číslo." }
    public static let noBuiltinLuts = "Vestavěné LUTy nenalezeny (chybí složka luts)."

    // MARK: Clip notes (report, job card)

    public static let noVerifiedCopy = "žádná ověřená kopie"

    // MARK: Numbers in Czech sentences

    private static let units = ["B", "KB", "MB", "GB", "TB"]

    /// A size inside a Czech sentence (Windows `Format.BytesCs`): "3,2 GB", "2 GB", "412 GB", "850 MB", "12 B" — decimal
    /// units, at most one decimal place below 10, none above.
    public static func bytes(_ b: Int64) -> String {
        var v = Double(b)
        var i = 0
        while v >= 1000 && i < units.count - 1 { v /= 1000; i += 1 }
        if i == 0 { return "\(b) B" }
        if v < 10 {
            let r = (v * 10).rounded(.toNearestOrAwayFromZero) / 10
            let text = r == r.rounded(.down) ? String(format: "%.0f", r) : String(format: "%.1f", r)
            return text.replacingOccurrences(of: ".", with: ",") + " " + units[i]
        }
        return String(format: "%.0f", v.rounded(.toNearestOrAwayFromZero)) + " " + units[i]
    }

    /// "1 234 567" — thousands separated by a non-breaking space, as .NET "N0" in cs-CZ.
    public static func count(_ n: Int) -> String {
        let digits = String(abs(n))
        var out = ""
        for (i, c) in digits.enumerated() {
            if i > 0 && (digits.count - i) % 3 == 0 { out.append("\u{00A0}") }
            out.append(c)
        }
        return (n < 0 ? "-" : "") + out
    }
}
