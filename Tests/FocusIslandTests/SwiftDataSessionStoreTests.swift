import Foundation
import SwiftData
import Testing
@testable import FocusIsland

@MainActor
struct SwiftDataSessionStoreTests {
    private func container(at url: URL? = nil) throws -> ModelContainer {
        let schema = Schema([WorkSession.self, DailySummary.self])
        let configuration: ModelConfiguration
        if let url {
            configuration = ModelConfiguration(schema: schema, url: url, cloudKitDatabase: .none)
        } else {
            configuration = ModelConfiguration(schema: schema, isStoredInMemoryOnly: true, cloudKitDatabase: .none)
        }
        return try ModelContainer(for: schema, configurations: [configuration])
    }

    private func session(day: String, offset: TimeInterval, sourceID: String? = nil) -> WorkSession {
        let start = Date(timeIntervalSince1970: 1_700_000_000 + offset)
        return WorkSession(bundleIdentifier: "com.example.editor", applicationName: "Editor",
                           startedAt: start, endedAt: start.addingTimeInterval(60),
                           effectiveSeconds: 60, dayKey: day, sourceID: sourceID)
    }

    @Test func queriesOrderAndFilterRealModels() throws {
        let container = try container()
        let store = SwiftDataSessionStore(context: container.mainContext)
        try store.insert(session(day: "2026-09-28", offset: 0, sourceID: "first"))
        try store.insert(session(day: "2026-09-29", offset: 120))
        try store.insert(session(day: "2026-09-29", offset: 60))
        #expect(try store.sessions(dayKey: "2026-09-29").map(\.effectiveSeconds) == [60, 60])
        let day = try store.sessions(dayKey: "2026-09-29")
        #expect(day[0].startedAt < day[1].startedAt)
        let recent = try store.sessions(startingWith: "2026-09-29")
        #expect(recent.count == 2)
        #expect(recent[0].startedAt > recent[1].startedAt)
        #expect(try store.oldestSessionDayKey(before: "2026-09-29") == "2026-09-28")
        #expect(try store.oldestSessionDayKey(before: "2026-09-28") == nil)
        #expect(try store.session(sourceID: "first")?.dayKey == "2026-09-28")
        #expect(try store.session(sourceID: "missing") == nil)
    }

    @Test func updatesAndDeletesSurviveReopeningDiskStore() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("WorkIslandStoreTests-\(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: false)
        defer { try? FileManager.default.removeItem(at: directory) }
        let url = directory.appendingPathComponent("sessions.store")
        // Separate scopes release contexts before reopening the same on-disk store.
        do {
            let container = try container(at: url)
            let store = SwiftDataSessionStore(context: container.mainContext)
            let item = session(day: "2026-09-29", offset: 0, sourceID: "persisted")
            try store.insert(item)
            item.effectiveSeconds = 42
            item.isManuallyEdited = true
            try store.update(item)
        }
        do {
            let container = try container(at: url)
            let store = SwiftDataSessionStore(context: container.mainContext)
            let item = try #require(try store.session(sourceID: "persisted"))
            #expect(item.effectiveSeconds == 42)
            #expect(item.isManuallyEdited)
            try store.delete(item)
        }
        do {
            let container = try container(at: url)
            let store = SwiftDataSessionStore(context: container.mainContext)
            #expect(try store.sessions(dayKey: "2026-09-29").isEmpty)
        }
    }

    @Test func archiveReplacesTotalWithoutDuplicatingOrDeletingOtherDays() throws {
        let container = try container()
        let store = SwiftDataSessionStore(context: container.mainContext)
        let old = session(day: "2026-09-28", offset: 0)
        let retained = session(day: "2026-09-29", offset: 60)
        try store.insert(old)
        try store.insert(retained)
        try store.archive([old], dayKey: "2026-09-28", totalSeconds: 60)
        try store.archive([], dayKey: "2026-09-28", totalSeconds: 75)
        try store.archive([], dayKey: "2026-09-27", totalSeconds: 10)
        let summaries = try store.allDailySummaries()
        #expect(summaries.map(\.dayKey) == ["2026-09-28", "2026-09-27"])
        #expect(summaries.map(\.totalSeconds) == [75, 10])
        #expect(try store.sessions(dayKey: "2026-09-28").isEmpty)
        #expect(try store.sessions(dayKey: "2026-09-29").map(\.id) == [retained.id])
    }
}
