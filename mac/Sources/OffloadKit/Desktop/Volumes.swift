import Foundation

/// A mounted volume offered in the Karty section. `identity` (`CardIdentity`) tells two cards in the same slot
/// apart; `ejectProblem` (`CardEject.problem`) is nil when "Vysunout kartu" may eject it.
public struct VolumeItem: Identifiable, Hashable {
    public var id: String { path }
    public let name: String
    public let path: String
    public let total: Int64
    public let free: Int64
    public let format: String
    public let looksLikeCard: Bool
    public let isDestination: Bool
    public let identity: String?
    public let ejectProblem: String?

    public init(name: String, path: String, total: Int64, free: Int64, format: String, looksLikeCard: Bool,
                isDestination: Bool, identity: String? = nil, ejectProblem: String? = nil) {
        self.name = name
        self.path = path
        self.total = total
        self.free = free
        self.format = format
        self.looksLikeCard = looksLikeCard
        self.isDestination = isDestination
        self.identity = identity
        self.ejectProblem = ejectProblem
    }
}
