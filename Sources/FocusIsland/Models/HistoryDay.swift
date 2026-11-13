import Foundation

struct HistoryDay: Identifiable, Sendable {
    let dayKey: String
    let totalSeconds: TimeInterval
    let applications: [AppUsage]
    let isSummaryOnly: Bool
    var id: String { dayKey }

    init(dayKey: String, totalSeconds: TimeInterval, applications: [AppUsage], isSummaryOnly: Bool = false) {
        self.dayKey = dayKey
        self.totalSeconds = totalSeconds
        self.applications = applications
        self.isSummaryOnly = isSummaryOnly
    }
}
