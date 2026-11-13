import Foundation
import SQLite3
import Testing
@testable import FocusIsland

@MainActor
struct LegacySessionImporterTests {
    @Test func migratesLegacySessionsAndCorrectionsIdempotently() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("FocusIslandImport-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }

        let legacyURL = directory.appendingPathComponent("work-island.sqlite")
        try makeLegacyDatabase(at: legacyURL)
        let suite = "FocusIslandLegacyImportTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = ImportMemoryStore()
        let backupDirectory = directory.appendingPathComponent("backups", isDirectory: true)

        let importer = try LegacySessionImporter(defaults: defaults, legacyURL: legacyURL, backupDirectory: backupDirectory)
        let imported = try importer.migrateIfNeeded(into: store)
        let report = try #require(imported)

        #expect(report == .init(sessions: 1, corrections: 1))
        #expect(store.items.count == 2)
        #expect(store.items.first { $0.sourceID == "legacy-session:10" }?.bundleIdentifier == "com.example.creator")
        #expect(store.items.first { $0.sourceID == "legacy-correction:20" }?.effectiveSeconds == -300)
        let backups = try FileManager.default.contentsOfDirectory(atPath: backupDirectory.path)
        #expect(backups.contains { $0.hasSuffix(".sqlite") })

        defaults.set(false, forKey: "legacySessionMigrationV1")
        let retried = try importer.migrateIfNeeded(into: store)
        let retry = try #require(retried)
        #expect(retry == .init(sessions: 0, corrections: 0))
        #expect(store.items.count == 2)
    }

    private func makeLegacyDatabase(at url: URL) throws {
        var database: OpaquePointer?
        guard sqlite3_open(url.path, &database) == SQLITE_OK, let database else {
            throw LegacyTestError.databaseCreationFailed
        }
        defer { sqlite3_close(database) }
        for sql in [
            "CREATE TABLE targets (id INTEGER PRIMARY KEY, kind TEXT NOT NULL, name TEXT NOT NULL, match_value TEXT NOT NULL)",
            "CREATE TABLE sessions (id INTEGER PRIMARY KEY, target_id INTEGER NOT NULL, started_at INTEGER NOT NULL, ended_at INTEGER NOT NULL, effective_seconds REAL NOT NULL, day_key TEXT NOT NULL)",
            "CREATE TABLE corrections (id INTEGER PRIMARY KEY, target_id INTEGER NOT NULL, day_key TEXT NOT NULL, delta_seconds REAL NOT NULL, created_at INTEGER NOT NULL)",
            "INSERT INTO targets VALUES (1, 'app', 'Creator', 'com.example.creator')",
            "INSERT INTO sessions VALUES (10, 1, 1724317200, 1724317800, 600, '2024-08-22')",
            "INSERT INTO corrections VALUES (20, 1, '2024-08-22', -300, 1724317900)"
        ] {
            guard sqlite3_exec(database, sql, nil, nil, nil) == SQLITE_OK else {
                throw LegacyTestError.databaseCreationFailed
            }
        }
    }
}

@MainActor
private final class ImportMemoryStore: SessionStoring {
    var items: [WorkSession] = []

    func insert(_ session: WorkSession) throws { items.append(session) }
    func sessions(dayKey: String) throws -> [WorkSession] { items.filter { $0.dayKey == dayKey } }
    func sessions(startingWith dayKey: String) throws -> [WorkSession] { items.filter { $0.dayKey >= dayKey } }
    func oldestSessionDayKey(before dayKey: String) throws -> String? { items.filter { $0.dayKey < dayKey }.map(\.dayKey).min() }
    func session(sourceID: String) throws -> WorkSession? { items.first { $0.sourceID == sourceID } }
    func update(_ session: WorkSession) throws {}
    func delete(_ session: WorkSession) throws { items.removeAll { $0.id == session.id } }
    func archive(_ sessions: [WorkSession], dayKey: String, totalSeconds: TimeInterval) throws {
        let ids = Set(sessions.map(\.id))
        items.removeAll { ids.contains($0.id) }
    }
    func allDailySummaries() throws -> [DailySummary] { [] }
}

private enum LegacyTestError: Error {
    case databaseCreationFailed
}
