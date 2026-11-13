import Foundation
import Testing
@testable import FocusIsland

struct HistoryDurationInputTests {
    @Test func acceptsExplicitZeroWhitespaceAndExistingMinuteNormalization() throws {
        #expect(try HistoryDurationInput.seconds(hours: "0", minutes: "0") == 0)
        #expect(try HistoryDurationInput.seconds(hours: " 2 ", minutes: " 05\n") == 7500)
        #expect(try HistoryDurationInput.seconds(hours: "0", minutes: "90") == 5400)
        #expect(try HistoryDurationInput.seconds(hours: "0001", minutes: "01") == 3660)
    }

    @Test func rejectsInvalidOrIncompleteInputInsteadOfResettingTotals() {
        for value in ["abc", "", " ", "-1", "1.5", "+2", "1e3", "1 2"] {
            #expect(throws: HistoryDurationInput.InputError.self) {
                try HistoryDurationInput.seconds(hours: value, minutes: "0")
            }
            #expect(throws: HistoryDurationInput.InputError.self) {
                try HistoryDurationInput.seconds(hours: "0", minutes: value)
            }
        }
    }

    @Test func rejectsMultiplicationAdditionParsingAndDoubleConversionOverflow() {
        let cases = [
            ("2562047788015216", "0"),
            ("0", String(Int.max)),
            (String(Int.max / 3600), "60"),
            ("999999999999999999999999999999", "0"),
            // Integer sum fits but Double rounds it to the unsafe Int boundary.
            (String(Int.max / 3600), "30")
        ]
        for (hours, minutes) in cases {
            #expect(throws: HistoryDurationInput.InputError.self) {
                try HistoryDurationInput.seconds(hours: hours, minutes: minutes)
            }
        }
    }

    @Test func largeAcceptedValuesRemainSafeForTimeFormatting() throws {
        let value = try HistoryDurationInput.seconds(hours: "1000000000", minutes: "59")
        #expect(value == 3_600_000_003_540)
        #expect(Int(value) == 3_600_000_003_540)
        #expect(!TimeFormatting.compact(value, style: .hoursMinutes).isEmpty)
    }
}
