import Foundation
import SwiftData

@Model
final class WorkSession {
    @Attribute(.unique) var id: UUID
    var bundleIdentifier: String
    var applicationName: String
    var startedAt: Date
    var endedAt: Date
    var effectiveSeconds: TimeInterval
    var dayKey: String
    var isManuallyCreated: Bool = false
    var isManuallyEdited: Bool = false
    var isAggregateCorrection: Bool = false
    var modifiedAt: Date?
    var categoryRawValue: String?
    var sourceKindRaw: String = "application"
    var sourceID: String?

    init(
        id: UUID = UUID(),
        bundleIdentifier: String,
        applicationName: String,
        startedAt: Date,
        endedAt: Date,
        effectiveSeconds: TimeInterval,
        dayKey: String,
        isManuallyCreated: Bool = false,
        isManuallyEdited: Bool = false,
        isAggregateCorrection: Bool = false,
        modifiedAt: Date? = nil,
        categoryRawValue: String? = nil
        , sourceKindRaw: String = "application", sourceID: String? = nil
    ) {
        self.id = id
        self.bundleIdentifier = bundleIdentifier
        self.applicationName = applicationName
        self.startedAt = startedAt
        self.endedAt = endedAt
        self.effectiveSeconds = effectiveSeconds
        self.dayKey = dayKey
        self.isManuallyCreated = isManuallyCreated
        self.isManuallyEdited = isManuallyEdited
        self.isAggregateCorrection = isAggregateCorrection
        self.modifiedAt = modifiedAt
        self.categoryRawValue = categoryRawValue
        self.sourceKindRaw = sourceKindRaw
        self.sourceID = sourceID
    }
}
