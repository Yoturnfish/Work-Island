import Foundation

enum TimeFormatting {
    static func goalHours(_ interval: TimeInterval, language: AppLanguage = L10n.language) -> String {
        let hours = max(0, Int(interval / 3600))
        return language == .english ? "\(hours) h" : "\(hours) 小时"
    }

    static func compact(_ interval: TimeInterval, style: DurationDisplayStyle = .hoursMinutes) -> String {
        let seconds = max(0, Int(interval))
        let hours = seconds / 3600
        let minutes = (seconds % 3600) / 60
        switch style {
        case .hoursMinutes:
            return String(format: "%dH %02dM", hours, minutes)
        case .minutesSeconds:
            return String(format: "%02dM %02dS", seconds / 60, seconds % 60)
        }
    }

    static func readable(_ interval: TimeInterval, style: DurationDisplayStyle = .hoursMinutes) -> String {
        let seconds = max(0, Int(interval))
        let hours = seconds / 3600
        let minutes = (seconds % 3600) / 60
        switch style {
        case .hoursMinutes:
            return "\(hours) \(L10n.text("小时")) \(minutes) \(L10n.text("分钟"))"
        case .minutesSeconds:
            return "\(seconds / 60) \(L10n.text("分钟")) \(seconds % 60) \(L10n.text("秒"))"
        }
    }
}
