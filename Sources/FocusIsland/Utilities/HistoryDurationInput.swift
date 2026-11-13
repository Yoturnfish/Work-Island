import Foundation

/// Validate before converting or touching records. Empty/invalid fields never mean zero.
enum HistoryDurationInput {
    enum InputError: LocalizedError {
        case invalidNumber
        case outOfRange

        var errorDescription: String? {
            switch self {
            case .invalidNumber: L10n.text("请输入非负整数；需要清零时，请明确输入 0。")
            case .outOfRange: L10n.text("时长数值过大，请输入较小的数值。")
            }
        }
    }

    static func seconds(hours: String, minutes: String) throws -> TimeInterval {
        func component(_ text: String) throws -> Int {
            let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !value.isEmpty, value.allSatisfy({ $0 >= "0" && $0 <= "9" }) else {
                throw InputError.invalidNumber
            }
            guard let number = Int(value) else { throw InputError.outOfRange }
            return number
        }
        let (hourSeconds, hourOverflow) = try component(hours).multipliedReportingOverflow(by: 3600)
        let (minuteSeconds, minuteOverflow) = try component(minutes).multipliedReportingOverflow(by: 60)
        guard !hourOverflow, !minuteOverflow else { throw InputError.outOfRange }
        let (total, totalOverflow) = hourSeconds.addingReportingOverflow(minuteSeconds)
        // Time formatting converts Double back to Int. Exclude the upper boundary
        // that Double rounds to Int.max + 1, even when integer arithmetic itself fits.
        let seconds = TimeInterval(total)
        guard !totalOverflow, seconds < TimeInterval(Int.max) else { throw InputError.outOfRange }
        return seconds
    }
}
