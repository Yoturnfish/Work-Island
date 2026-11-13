import Foundation
import SwiftData

@MainActor
final class SwiftDataSessionStore: SessionStoring {
    private let context: ModelContext

    init(context: ModelContext) {
        self.context = context
    }

    func insert(_ session: WorkSession) throws {
        context.insert(session)
        try context.save()
    }

    func sessions(dayKey: String) throws -> [WorkSession] {
        let key = dayKey
        let descriptor = FetchDescriptor<WorkSession>(
            predicate: #Predicate { $0.dayKey == key },
            sortBy: [SortDescriptor(\.startedAt)]
        )
        return try context.fetch(descriptor)
    }

    func sessions(startingWith dayKey: String) throws -> [WorkSession] {
        let key = dayKey
        let descriptor = FetchDescriptor<WorkSession>(
            predicate: #Predicate { $0.dayKey >= key },
            sortBy: [SortDescriptor(\.startedAt, order: .reverse)]
        )
        return try context.fetch(descriptor)
    }

    func oldestSessionDayKey(before dayKey: String) throws -> String? {
        let cutoff = dayKey
        var descriptor = FetchDescriptor<WorkSession>(
            predicate: #Predicate { $0.dayKey < cutoff },
            sortBy: [SortDescriptor(\.dayKey)]
        )
        descriptor.fetchLimit = 1
        return try context.fetch(descriptor).first?.dayKey
    }

    func session(sourceID: String) throws -> WorkSession? {
        let sourceID = sourceID
        var descriptor = FetchDescriptor<WorkSession>(predicate: #Predicate { $0.sourceID == sourceID })
        descriptor.fetchLimit = 1
        return try context.fetch(descriptor).first
    }

    func update(_ session: WorkSession) throws {
        try context.save()
    }

    func delete(_ session: WorkSession) throws {
        context.delete(session)
        try context.save()
    }

    func archive(_ sessions: [WorkSession], dayKey: String, totalSeconds: TimeInterval) throws {
        let key = dayKey
        var descriptor = FetchDescriptor<DailySummary>(predicate: #Predicate { $0.dayKey == key })
        descriptor.fetchLimit = 1
        if let summary = try context.fetch(descriptor).first {
            summary.totalSeconds = totalSeconds
        } else {
            context.insert(DailySummary(dayKey: dayKey, totalSeconds: totalSeconds))
        }
        sessions.forEach(context.delete)
        try context.save()
    }

    func allDailySummaries() throws -> [DailySummary] {
        try context.fetch(FetchDescriptor<DailySummary>(sortBy: [SortDescriptor(\.dayKey, order: .reverse)]))
    }
}
