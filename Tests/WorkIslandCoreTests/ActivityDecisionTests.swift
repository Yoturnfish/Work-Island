import Foundation
import Testing
@testable import WorkIslandCore

struct ActivityDecisionTests {
    private struct FixtureCase: Decodable {
        struct Expected: Decodable {
            let state: String
            let identifier: String?
            let displayName: String?
            let categoryRawValue: String?
            let source: TrackingTarget.Source?
        }

        let name: String
        let input: ActivityInput
        let applicationRules: [ApplicationRule]
        let websiteRules: [WebsiteActivityRule]
        let supportedBrowserIdentifiers: [String]
        let awayThreshold: TimeInterval
        let isManuallyPaused: Bool
        let expected: Expected
    }

    @Test func sharedJSONAcceptanceVectors() throws {
        let testDirectory = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        let fixtureURL = testDirectory
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .appendingPathComponent("Specs/activity-decision-cases.json")
        let cases = try JSONDecoder().decode([FixtureCase].self, from: Data(contentsOf: fixtureURL))

        #expect(cases.count >= 6)
        for item in cases {
            let decision = ActivityDecision.evaluate(
                input: item.input,
                applicationRules: item.applicationRules,
                websiteRules: item.websiteRules,
                supportedBrowserIdentifiers: Set(item.supportedBrowserIdentifiers),
                awayThreshold: item.awayThreshold,
                isManuallyPaused: item.isManuallyPaused
            )
            switch (decision, item.expected.state) {
            case (.paused, "paused"), (.away, "away"):
                break
            case let (.tracking(target), "tracking"):
                #expect(target.identifier == item.expected.identifier, "\(item.name): identifier")
                #expect(target.displayName == item.expected.displayName, "\(item.name): display name")
                #expect(target.categoryRawValue == item.expected.categoryRawValue, "\(item.name): category")
                #expect(target.source == item.expected.source, "\(item.name): source")
            default:
                Issue.record("\(item.name): expected \(item.expected.state), got \(decision)")
            }
        }
    }

    @Test func sessionDTOUsesStableCrossPlatformJSONFields() throws {
        let record = WorkSessionRecord(
            id: "session-1",
            applicationIdentifier: "com.example.editor",
            applicationName: "Editor",
            startedAt: Date(timeIntervalSince1970: 1_700_000_000),
            endedAt: Date(timeIntervalSince1970: 1_700_000_120),
            effectiveSeconds: 120,
            dayKey: "2026-09-21",
            categoryRawValue: "editing",
            isManuallyEdited: true
        )
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let roundTrip = try decoder.decode(WorkSessionRecord.self, from: encoder.encode(record))
        #expect(roundTrip == record)
    }
}
