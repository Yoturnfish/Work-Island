import Foundation
import SwiftData

enum FocusIslandDataStore {
    static let storeFileName = "FocusIsland.store"

    static var applicationSupportDirectory: URL {
        get throws {
            guard let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else {
                throw CocoaError(.fileNoSuchFile)
            }
            let directory = base.appendingPathComponent("com.focusisland.app", isDirectory: true)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            return directory
        }
    }

    static var storeURL: URL {
        get throws {
            try applicationSupportDirectory.appendingPathComponent(storeFileName, isDirectory: false)
        }
    }

    static var legacyStoreURL: URL {
        get throws {
            guard let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else {
                throw CocoaError(.fileNoSuchFile)
            }
            return base.appendingPathComponent("work-island/work-island.sqlite", isDirectory: false)
        }
    }

    static func makeModelContainer() throws -> ModelContainer {
        let schema = Schema([WorkSession.self, DailySummary.self])
        let configuration = ModelConfiguration("FocusIsland", schema: schema, url: try storeURL)
        return try ModelContainer(for: schema, configurations: [configuration])
    }
}
