import Foundation

struct AppUsage: Identifiable, Equatable, Sendable {
    var id: String { bundleIdentifier }
    let bundleIdentifier: String
    let applicationName: String
    let seconds: TimeInterval
}
