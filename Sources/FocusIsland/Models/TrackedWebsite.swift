import Foundation
import WorkIslandCore

struct TrackedWebsite: Codable, Identifiable, Hashable, Sendable {
    let id: UUID
    var displayName: String
    var titleContains: String
    var pathContains: String
    var isEnabled: Bool
    var matchesHostOnly: Bool?

    init(id: UUID = UUID(), displayName: String, titleContains: String = "", pathContains: String = "", isEnabled: Bool = true) {
        self.id = id; self.displayName = displayName; self.titleContains = titleContains; self.pathContains = pathContains; self.isEnabled = isEnabled
    }

    func matches(url: String?) -> Bool {
        WebsiteRules.matches(
            url: url,
            titleContains: titleContains,
            pathContains: pathContains,
            isEnabled: isEnabled,
            matchesHostOnly: matchesHostOnly
        )
    }
}

extension TrackedWebsite: WebsiteActivityRuleProviding {
    var activityIdentifier: String {
        let keyword = pathContains.isEmpty ? titleContains : pathContains
        return WebsiteRules.statisticsIdentifier(keyword: keyword)
    }
    var activityDisplayName: String { displayName }
    var activityTitleContains: String { titleContains }
    var activityPathContains: String { pathContains }
    var activityIsEnabled: Bool { isEnabled }
    var activityMatchesHostOnly: Bool? { matchesHostOnly }
    var activityCategoryRawValue: String { WorkCategory.other.rawValue }
}
