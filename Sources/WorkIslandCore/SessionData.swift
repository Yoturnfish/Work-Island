import Foundation

/// Storage-neutral session representation shared by platform adapters.
public struct WorkSessionRecord: Codable, Equatable, Sendable {
    public let id: String
    public let applicationIdentifier: String
    public let applicationName: String
    public let startedAt: Date
    public let endedAt: Date
    public let effectiveSeconds: TimeInterval
    public let dayKey: String
    public let categoryRawValue: String
    public let isManuallyCreated: Bool
    public let isManuallyEdited: Bool

    public init(
        id: String,
        applicationIdentifier: String,
        applicationName: String,
        startedAt: Date,
        endedAt: Date,
        effectiveSeconds: TimeInterval,
        dayKey: String,
        categoryRawValue: String,
        isManuallyCreated: Bool = false,
        isManuallyEdited: Bool = false
    ) {
        self.id = id
        self.applicationIdentifier = applicationIdentifier
        self.applicationName = applicationName
        self.startedAt = startedAt
        self.endedAt = endedAt
        self.effectiveSeconds = effectiveSeconds
        self.dayKey = dayKey
        self.categoryRawValue = categoryRawValue
        self.isManuallyCreated = isManuallyCreated
        self.isManuallyEdited = isManuallyEdited
    }
}

public struct DailySummaryRecord: Codable, Equatable, Sendable {
    public let dayKey: String
    public let totalSeconds: TimeInterval

    public init(dayKey: String, totalSeconds: TimeInterval) {
        self.dayKey = dayKey
        self.totalSeconds = totalSeconds
    }
}

/// The behavioral storage boundary. SwiftData and SQLite adapters can conform
/// without exposing either framework to the shared decision and statistics code.
public protocol SessionRepository: AnyObject {
    func insert(_ session: WorkSessionRecord) throws
    func sessions(dayKey: String) throws -> [WorkSessionRecord]
    func sessions(startingWith dayKey: String) throws -> [WorkSessionRecord]
    func update(_ session: WorkSessionRecord) throws
    func delete(sessionID: String) throws
    func archive(_ sessions: [WorkSessionRecord], summary: DailySummaryRecord) throws
    func allDailySummaries() throws -> [DailySummaryRecord]
}
