import Foundation

/// Which check stopped a job. Tests and the GUI tell the cases apart by the code, never by the text.
public enum PreflightCode: String {
    case other, chunkSize, bufferCount, date, sourceNotFound, noDestination, duplicateDestination, sourceIsOffload
    case sourceUnreadable, destinationNotFound, destinationNotWritable, destinationUnreadable, sameVolumeAsSource
    case sharedVolume, emptySource, noSpace, noHashRecord, noFilesToRead
    case appendUnavailable, appendNoReport, appendNoMhl, appendDiverged, appendConflict, appendMissingFile, appendFailedFile
    case appendOtherCard, appendMhlDamaged, appendBusy
    /// Not an error: the backup already has everything (the job ends "Hotovo").
    case nothingNew
    /// Not an error: a repair found every file good on every destination.
    case repairNothing
    case repairConflict
}

/// Czech text with the right plural: 1 / 2–4 / other.
func plural(_ n: Int, _ one: String, _ few: String, _ many: String) -> String { Texts.plural(n, one, few, many) }

/// Every message a user sees from checks and validation — Czech, word for word as Windows (Messages.cs), paths with "/".
public enum Msg {
    public static func chunkSize(_ n: Int) -> String { "Velikost bloku musí být násobek 4096 B a nejvýš 1 GiB (zadáno \(n))." }
    public static func bufferCount(_ n: Int) -> String { "Počet bufferů musí být aspoň 2 (zadáno \(n))." }
    public static func date(_ d: String) -> String { "Datum musí být ve tvaru RRRR-MM-DD (zadáno „\(d)“)." }
    public static func sourceNotFound(_ p: String) -> String { "Zdroj nenalezen: \(p)" }
    public static let noDestination = "Není zadaný žádný cíl."
    public static func duplicateDestination(_ r: String) -> String { "Cíl je zadaný dvakrát: \(r)" }
    public static let sourceIsOffload =
        "Zdroj vypadá jako dřívější offload (má _REPORT, _PROXY nebo ascmhl) — kopírujte původní kartu, nebo tyto složky odeberte."
    public static func sourceUnreadable(_ p: String) -> String { "Zdroj nejde přečíst: \(p)" }
    public static func destinationNotFound(_ r: String) -> String { "Cíl nenalezen: \(r) — je disk připojený?" }
    public static func destinationNotWritable(_ r: String) -> String { "Na cíl nejde zapisovat: \(r)" }
    public static func destinationUnreadable(_ r: String) -> String { "Cíl nejde přečíst: \(r)" }
    public static func sameVolumeAsSource(_ r: String) -> String { "Cíl \(r) je na stejném disku jako zdroj." }
    public static func sharedVolume(_ r: String) -> String {
        "Dva cíle jsou na stejném disku (\(r)) — to není druhá kopie. Povolit jde volbou --allow-same-device."
    }
    public static let emptySource = "Na zdroji nejsou žádné soubory."
    public static func reportNotWritten(_ d: String) -> String { "Report se nepodařilo zapsat: \(d)" }
    public static func noHashRecord(_ dir: String) -> String { "Ve složce \(dir) není ASC MHL ani seznam součtů — není s čím porovnat." }
    public static func noFilesToRead(_ p: String) -> String { "V \(p) nejsou soubory ke čtení." }
    public static let cardVolumeUnreadable = "Karta se nedá přečíst."
    public static let cardInspectFailed = "Kartu se nepodařilo prověřit."
    public static let cardSwapped = "Karta se během čtení vyměnila."
    public static let cardNameAutoNone = "Návrh názvu nenalezen, použije se jmenovka svazku."
    public static func cardNameAuto(_ n: String) -> String { "Název karty: \(n) (podle klipů)" }
    public static func cardUnreadable(_ d: String) -> String { "Kartu nejde přečíst: \(d)" }
    /// Where a backup is checked (the new look: top bar; before it: menu Nástroje).
    public static let verifyHint = "Ověřit zálohu (horní lišta okna; v příkazové řádce offload verify)"

    // Space (úkol 48 fills the parts)
    public static func noSpace(_ whereTo: String, total: Int64, parts: String, free: Int64) -> String {
        "Na cíli \(whereTo) není dost místa: potřeba \(formatBytesCs(total)) (\(parts)), volno \(formatBytesCs(free))."
    }
    public static func spaceLine(_ whereTo: String, total: Int64, parts: String, free: Int64) -> String {
        "Místo: \(whereTo) potřeba \(formatBytesCs(total)) (\(parts)), volno \(formatBytesCs(free))"
    }
    public static func lowSpace(_ whereTo: String, left: Int64) -> String {
        "Na cíli \(whereTo) zbude po offloadu jen \(formatBytesCs(left)) — rezerva pod 5 %, odhad proxy je přibližný."
    }
    public static func proxiesSkippedForSpace(_ whereTo: String, need: Int64, free: Int64) -> String {
        "Proxy se nevyrenderovaly: na \(whereTo) chybí místo (potřeba ~\(formatBytesCs(need)), volno \(formatBytesCs(free)))."
    }

    // Append
    public static func appendUnavailable(_ p: String) -> String { "Dřívější záloha není dostupná: \(p) — připojte disk, nebo udělejte celou kopii." }
    public static func appendNoReport(_ p: String) -> String { "V \(p) chybí report dřívější zálohy (_REPORT/offload_report.json) — doplnit nejde." }
    public static func appendBadReport(_ p: String, _ d: String) -> String { "Report dřívější zálohy v \(p) nejde přečíst (\(d)) — doplnit nejde." }
    public static func appendNoMhl(_ p: String) -> String { "V \(p) chybí ASC MHL (ascmhl/ascmhl_chain.xml) — doplnit nejde." }
    public static func appendDiverged(_ details: String) -> String {
        "Zálohy na cílech se liší (\(details)) — zkontrolujte je přes \(verifyHint); doplnit nejde."
    }
    public static func appendReportsMismatch(_ p: String, report: Int, mhl: Int) -> String {
        "V \(p) nesouhlasí reporty s ASC MHL (poslední report je generace \(report), poslední generace MHL \(mhl)) — dřívější doplnění se nedokončilo. "
            + "Zkontrolujte zálohu přes \(verifyHint) a složku opravte ručně; doplnit nejde."
    }
    public static func appendNotACardFolder(_ p: String) -> String { "\(p) není složka karty v záloze (<cíl>/<datum>/<karta>) — doplnit nejde." }
    public static func appendDifferentFolders(_ paths: [String]) -> String {
        "Složky dřívější zálohy se jmenují různě (\(paths.joined(separator: ", "))) — doplnit jde jen do složek se stejným datem a názvem karty."
    }
    public static func appendConflict(_ lines: [String], fullCopyFolder: String) -> String {
        "Doplnění zastaveno — nic se nezkopírovalo. \(lines.count) \(plural(lines.count, "soubor se neshoduje", "soubory se neshodují", "souborů se neshoduje")) "
            + "se zálohou:\n  " + lines.prefix(10).joined(separator: "\n  ") + (lines.count > 10 ? "\n  …" : "")
            + "\nUdělejte celou kopii (vznikne \(fullCopyFolder))."
    }
    public static func appendMissingFile(_ disk: String, _ path: String) -> String {
        "Na \(disk) chybí soubor z dřívější zálohy: \(path) — zkontrolujte zálohu přes \(verifyHint)"
    }
    public static func appendFailedFile(_ path: String, _ disk: String) -> String {
        "\(path) se při dřívější záloze na \(disk) nezkopíroval správně — doplnit nejde, udělejte celou kopii."
    }
    public static let appendOtherCard = "Na kartě není žádný soubor z dřívější zálohy — jde o jinou kartu? Udělejte celou kopii."
    public static func appendMhlDamaged(_ p: String) -> String { "Historie ASC MHL v \(p) je poškozená — zkontrolujte ji přes \(verifyHint); doplnit nejde." }
    public static func appendUnknownFile(_ disk: String, _ path: String) -> String { "na \(disk) je soubor \(path), který žádný report nezná (nedokončené doplnění?)" }
    public static func appendPartNameTaken(_ path: String, _ part: String) -> String { "\(path) (dočasné jméno kopie \(part) je jiný soubor zálohy nebo karty)" }
    public static func hashListNotHealed(_ p: String, _ e: String) -> String { "Seznam kontrolních součtů (.xxh64) v \(p) nejde doplnit o chybějící řádky: \(e)" }
    public static func appendBusy(_ p: String) -> String {
        "S touto zálohou právě pracuje jiná úloha — doplnění, oprava nebo ověření s proxy (\(p)) — počkejte, až skončí."
    }
    public static let nothingNew = "Nic nového — záloha je úplná."
    public static func unverifiedEarlier(_ n: Int) -> String {
        "\(n) \(plural(n, "soubor", "soubory", "souborů")) z dřívější zálohy \(plural(n, "nebyl ověřen", "nebyly ověřeny", "nebylo ověřeno")) — doporučeno Ověřit zálohu."
    }
    public static let cardNotInHistory = "Karta není v historii — zadejte složky zálohy volbou --into."

    // Repair (soubor -3, úkol 62)
    public static let repairNothing = "Nic k opravě — všechny soubory jsou na všech cílech v pořádku."
    public static func repairConflict(_ lines: [String]) -> String {
        "Oprava zastavena — nic se nezkopírovalo. \(lines.count) \(plural(lines.count, "soubor nejde opravit", "soubory nejde opravit", "souborů nejde opravit")):\n  "
            + lines.prefix(10).joined(separator: "\n  ") + (lines.count > 10 ? "\n  …" : "")
            + "\nKarta se od zálohy změnila nebo ve složce zálohy chybí soubor — zkontrolujte ji přes Ověřit zálohu, nebo udělejte celou kopii."
    }
    public static func repairNotOnCard(_ p: String) -> String { "\(p): na kartě už není" }
    public static func repairCardDiffers(_ p: String, _ algorithm: String, now: String, recorded: String) -> String {
        "\(p): na kartě je jiný obsah než při záloze (\(algorithm) \(now), v záloze \(recorded))"
    }
    public static func repairCardUnreadable(_ p: String, _ d: String) -> String { "\(p): na kartě nejde přečíst (\(d))" }
    public static let repairNeedsVerify = "Oprava se vždy ověřuje zpětným čtením — bez ověření ji spustit nejde."
    public static func mhlRepairComment(copied: Int, readBack: Int, source: String) -> String {
        "Offload oprava: \(copied) \(plural(copied, "soubor", "soubory", "souborů")) znovu z \(source)"
            + (readBack > 0 ? "; \(readBack) \(plural(readBack, "soubor jen přečten", "soubory jen přečteny", "souborů jen přečteno")) zpět" : "")
    }
    public static let repairOtherCard =
        "Ve čtečce není karta, ze které se záloha dělala (jiná karta, nebo žádná) — nic se nezkopírovalo. Vložte původní kartu a spusťte Zkopírovat znovu."
    public static func cardFilesNotInBackup(_ n: Int) -> String {
        "Na kartě \(plural(n, "je", "jsou", "je")) \(n) \(plural(n, "soubor, který", "soubory, které", "souborů, které")) záloha nemá — kartu nevysouvejte, zálohu doplňte."
    }
    public static func backupStillFailed(_ n: Int) -> String {
        "V záloze \(n) \(plural(n, "soubor stále nesouhlasí", "soubory stále nesouhlasí", "souborů stále nesouhlasí")) — kartu nevysouvejte, spusťte Zkopírovat znovu."
    }
    public static func backupStateUnreadable(_ d: String) -> String { "Stav zálohy nejde přečíst (\(d)) — nic se nezkontrolovalo, kartu zatím nevysouvejte." }
    public static func forRepair(_ appendRefusal: String) -> String {
        appendRefusal.replacingOccurrences(of: "doplnit nejde", with: "opravit nejde").replacingOccurrences(of: "doplnit jde jen", with: "opravit jde jen")
    }
    public static let cardChangedWhilePaused =
        "Během pozastavení se karta ve čtečce vyměnila nebo vytáhla — zbylé soubory se nezkopírovaly. Vložte původní kartu a zálohu doplňte."

    // CLI
    public static let cliError = "Chyba: "
    public static func unknownProxyCodec(_ x: String?) -> String {
        "Neznámý kodek proxy „\(x ?? "")“ — použijte h264, hevc, proresProxy, proresLT, prores422 nebo prores422HQ."
    }
    public static func lutNotFound(_ p: String) -> String { "LUT nenalezen: \(p)" }
    public static func unknownHash(_ x: String?) -> String { "Neznámý kontrolní součet „\(x ?? "")“ — použijte xxh64 nebo both (xxh64 + MD5)." }
    public static func proxyFolderNotFound(_ p: String) -> String { "Složka pro proxy nenalezena: \(p)" }
    public static let proxyFolderNoEffect = "Poznámka: --proxy-folder bez --proxy nic nedělá."
    public static let cancelling = "Ruším — dokončuji aktuální blok, rozpracované soubory se smažou…"
    public static func notPositiveNumber(_ s: String?) -> String { "„\(s ?? "")“ není kladné číslo." }
    public static func unknownFallbackLut(_ x: String, ids: String) -> String { "Neznámý záložní LUT „\(x)“. Vestavěné: \(ids)" }
    public static func unknownLut(_ x: String, ids: String) -> String { "Neznámý LUT „\(x)“. Použijte auto, none, cestu k .cube nebo jedno z: \(ids)" }
    public static let noBuiltinLuts = "Vestavěné LUTy nenalezeny (chybí složka luts)."

    // Clip notes
    public static let noVerifiedCopy = "žádná ověřená kopie"
    public static func proxyRenamed(_ name: String) -> String { "Proxy přejmenována na \(name) (shoda jmen) — přelinkujte ji ručně." }
    public static let ffmpegNotFound = "ffmpeg nenalezen"
    public static let ffprobeNotFound = "ffprobe nenalezen"
    public static let unknownFormat = "neznámý formát"
    public static func noDecoder(_ f: String) -> String { "\(f): chybí dekodér" }
}
