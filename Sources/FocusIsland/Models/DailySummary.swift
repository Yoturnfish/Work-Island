import Foundation
import SwiftData

@Model
final class DailySummary {
    @Attribute(.unique) var dayKey: String
    var totalSeconds: TimeInterval

    init(dayKey: String, totalSeconds: TimeInterval) {
        self.dayKey = dayKey
        self.totalSeconds = totalSeconds
    }
}
