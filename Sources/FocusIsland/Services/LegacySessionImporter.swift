import Foundation
import SQLite3

@MainActor
final class LegacySessionImporter {
    struct Report: Equatable {
        let sessions: Int
        let corrections: Int
    }

    private static let migrationKey = "legacySessionMigrationV1"
    private let fileManager: FileManager
    private let defaults: UserDefaults
    private let legacyURL: URL
    private let backupDirectory: URL?

    init(
        fileManager: FileManager = .default,
        defaults: UserDefaults = .standard,
        legacyURL: URL? = nil,
        backupDirectory: URL? = nil
    ) throws {
        self.fileManager = fileManager
        self.defaults = defaults
        self.legacyURL = try legacyURL ?? FocusIslandDataStore.legacyStoreURL
        self.backupDirectory = backupDirectory
    }

    func migrateIfNeeded(into store: any SessionStoring) throws -> Report? {
        guard !defaults.bool(forKey: Self.migrationKey), fileManager.fileExists(atPath: legacyURL.path) else {
            return nil
        }

        try backupLegacyDatabase()
        let database = try LegacyDatabase(url: legacyURL)
        defer { database.close() }

        let targets = try database.targets()
        var sessionsImported = 0
        for session in try database.sessions() {
            let sourceID = "legacy-session:\(session.id)"
            guard try store.session(sourceID: sourceID) == nil else { continue }
            let target = targets[session.targetID]
            try store.insert(WorkSession(
                bundleIdentifier: target?.bundleIdentifier ?? "legacy.unknown.\(session.targetID)",
                applicationName: target?.name ?? "已删除目标",
                startedAt: Self.date(fromEpoch: session.startedAt),
                endedAt: Self.date(fromEpoch: session.endedAt),
                effectiveSeconds: session.effectiveSeconds,
                dayKey: session.dayKey,
                categoryRawValue: nil,
                sourceKindRaw: "legacySession",
                sourceID: sourceID
            ))
            sessionsImported += 1
        }

        var correctionsImported = 0
        for correction in try database.corrections() {
            let sourceID = "legacy-correction:\(correction.id)"
            guard try store.session(sourceID: sourceID) == nil else { continue }
            let target = targets[correction.targetID]
            let createdAt = Self.date(fromEpoch: correction.createdAt)
            try store.insert(WorkSession(
                bundleIdentifier: target?.bundleIdentifier ?? "legacy.unknown.\(correction.targetID)",
                applicationName: target?.name ?? "已删除目标",
                startedAt: createdAt,
                endedAt: createdAt,
                effectiveSeconds: correction.deltaSeconds,
                dayKey: correction.dayKey,
                isManuallyCreated: true,
                isManuallyEdited: true,
                isAggregateCorrection: true,
                modifiedAt: createdAt,
                categoryRawValue: nil,
                sourceKindRaw: "legacyCorrection",
                sourceID: sourceID
            ))
            correctionsImported += 1
        }

        defaults.set(true, forKey: Self.migrationKey)
        return Report(sessions: sessionsImported, corrections: correctionsImported)
    }

    private func backupLegacyDatabase() throws {
        let destinationDirectory = try self.backupDirectory ?? FocusIslandDataStore.applicationSupportDirectory
            .appendingPathComponent("LegacyBackups", isDirectory: true)
        try fileManager.createDirectory(at: destinationDirectory, withIntermediateDirectories: true)

        let stamp = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: ":", with: "-")
        let backupBase = destinationDirectory.appendingPathComponent("work-island-\(stamp)", isDirectory: false)
        for suffix in ["", "-wal", "-shm"] {
            let source = URL(fileURLWithPath: legacyURL.path + suffix)
            guard fileManager.fileExists(atPath: source.path) else { continue }
            let destination = URL(fileURLWithPath: backupBase.path + ".sqlite" + suffix)
            guard !fileManager.fileExists(atPath: destination.path) else { continue }
            try fileManager.copyItem(at: source, to: destination)
        }
    }

    private static func date(fromEpoch value: Int64) -> Date {
        let seconds = abs(value) > 10_000_000_000 ? Double(value) / 1_000 : Double(value)
        return Date(timeIntervalSince1970: seconds)
    }
}

private final class LegacyDatabase {
    fileprivate struct Target {
        let id: Int64
        let kind: String
        let name: String
        let matchValue: String

        var bundleIdentifier: String {
            kind == "app" ? matchValue : "legacy.website.\(id)"
        }
    }

    fileprivate struct Session {
        let id: Int64
        let targetID: Int64
        let startedAt: Int64
        let endedAt: Int64
        let effectiveSeconds: TimeInterval
        let dayKey: String
    }

    fileprivate struct Correction {
        let id: Int64
        let targetID: Int64
        let dayKey: String
        let deltaSeconds: TimeInterval
        let createdAt: Int64
    }

    private var database: OpaquePointer?

    init(url: URL) throws {
        let immutableURL = url.absoluteString + "?immutable=1"
        let result = sqlite3_open_v2(immutableURL, &database, SQLITE_OPEN_READONLY | SQLITE_OPEN_URI | SQLITE_OPEN_FULLMUTEX, nil)
        guard result == SQLITE_OK, database != nil else {
            defer { close() }
            throw LegacyDatabaseError.openFailed(message: Self.message(for: database))
        }
    }

    func close() {
        if let database { sqlite3_close_v2(database) }
        database = nil
    }

    fileprivate func targets() throws -> [Int64: Target] {
        let statement = try prepare("SELECT id, kind, name, match_value FROM targets")
        defer { sqlite3_finalize(statement) }
        var result: [Int64: Target] = [:]
        while sqlite3_step(statement) == SQLITE_ROW {
            let id = sqlite3_column_int64(statement, 0)
            result[id] = Target(
                id: id,
                kind: Self.string(statement, column: 1),
                name: Self.string(statement, column: 2),
                matchValue: Self.string(statement, column: 3)
            )
        }
        try throwIfLastStepFailed(statement)
        return result
    }

    fileprivate func sessions() throws -> [Session] {
        let statement = try prepare("SELECT id, target_id, started_at, ended_at, effective_seconds, day_key FROM sessions ORDER BY id")
        defer { sqlite3_finalize(statement) }
        var result: [Session] = []
        while sqlite3_step(statement) == SQLITE_ROW {
            result.append(Session(
                id: sqlite3_column_int64(statement, 0),
                targetID: sqlite3_column_int64(statement, 1),
                startedAt: sqlite3_column_int64(statement, 2),
                endedAt: sqlite3_column_int64(statement, 3),
                effectiveSeconds: sqlite3_column_double(statement, 4),
                dayKey: Self.string(statement, column: 5)
            ))
        }
        try throwIfLastStepFailed(statement)
        return result
    }

    fileprivate func corrections() throws -> [Correction] {
        let statement = try prepare("SELECT id, target_id, day_key, delta_seconds, created_at FROM corrections ORDER BY id")
        defer { sqlite3_finalize(statement) }
        var result: [Correction] = []
        while sqlite3_step(statement) == SQLITE_ROW {
            result.append(Correction(
                id: sqlite3_column_int64(statement, 0),
                targetID: sqlite3_column_int64(statement, 1),
                dayKey: Self.string(statement, column: 2),
                deltaSeconds: sqlite3_column_double(statement, 3),
                createdAt: sqlite3_column_int64(statement, 4)
            ))
        }
        try throwIfLastStepFailed(statement)
        return result
    }

    private func prepare(_ sql: String) throws -> OpaquePointer? {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(database, sql, -1, &statement, nil) == SQLITE_OK else {
            throw LegacyDatabaseError.queryFailed(message: Self.message(for: database))
        }
        return statement
    }

    private func throwIfLastStepFailed(_ statement: OpaquePointer?) throws {
        guard sqlite3_errcode(database) == SQLITE_OK || sqlite3_errcode(database) == SQLITE_DONE else {
            throw LegacyDatabaseError.queryFailed(message: Self.message(for: database))
        }
    }

    private static func string(_ statement: OpaquePointer?, column: Int32) -> String {
        guard let value = sqlite3_column_text(statement, column) else { return "" }
        return String(cString: value)
    }

    private static func message(for database: OpaquePointer?) -> String {
        guard let database, let message = sqlite3_errmsg(database) else { return "未知 SQLite 错误" }
        return String(cString: message)
    }
}

private enum LegacyDatabaseError: LocalizedError {
    case openFailed(message: String)
    case queryFailed(message: String)

    var errorDescription: String? {
        switch self {
        case let .openFailed(message): "无法读取旧版计时库：\(message)"
        case let .queryFailed(message): "读取旧版计时记录失败：\(message)"
        }
    }
}
