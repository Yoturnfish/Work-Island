import Foundation
import WorkIslandCore

enum WorkCategory: String, CaseIterable, Identifiable, Codable, Sendable {
    case programming, editing, design, threeD, other
    var id: String { rawValue }
    var title: String {
        switch self { case .programming: "编程"; case .editing: "剪辑"; case .design: "设计"; case .threeD: "3D 创作"; case .other: "其他" }
    }
}

struct TrackedApplication: Codable, Identifiable, Hashable, Sendable {
    let id: UUID
    var bundleIdentifier: String
    var displayName: String
    var isEnabled: Bool
    var category: WorkCategory

    init(
        id: UUID = UUID(),
        bundleIdentifier: String,
        displayName: String,
        isEnabled: Bool = true,
        category: WorkCategory = .other
    ) {
        self.id = id
        self.bundleIdentifier = bundleIdentifier
        self.displayName = displayName
        self.isEnabled = isEnabled
        self.category = category
    }

    static let defaults: [TrackedApplication] = [
        .init(bundleIdentifier: "com.blackmagic-design.DaVinciResolve", displayName: "DaVinci Resolve", category: .editing),
        .init(bundleIdentifier: "com.apple.FinalCut", displayName: "Final Cut Pro", category: .editing),
        .init(bundleIdentifier: "com.adobe.Photoshop", displayName: "Adobe Photoshop", category: .design),
        .init(bundleIdentifier: "org.blenderfoundation.blender", displayName: "Blender", category: .threeD),
        .init(bundleIdentifier: "com.autodesk.fusion360", displayName: "Fusion 360", category: .threeD)
    ]

    static func builtInCategory(for bundleIdentifier: String) -> WorkCategory? {
        defaults.first { $0.bundleIdentifier == bundleIdentifier }?.category
    }

    private enum CodingKeys: String, CodingKey { case id, bundleIdentifier, displayName, isEnabled, category }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        bundleIdentifier = try c.decode(String.self, forKey: .bundleIdentifier)
        displayName = try c.decode(String.self, forKey: .displayName)
        isEnabled = try c.decodeIfPresent(Bool.self, forKey: .isEnabled) ?? true
        category = try c.decodeIfPresent(WorkCategory.self, forKey: .category)
            ?? Self.builtInCategory(for: bundleIdentifier)
            ?? .other
    }
}

extension TrackedApplication: ApplicationRuleProviding {
    var activityIdentifier: String { bundleIdentifier }
    var activityDisplayName: String { displayName }
    var activityIsEnabled: Bool { isEnabled }
    var activityCategoryRawValue: String { category.rawValue }
}
