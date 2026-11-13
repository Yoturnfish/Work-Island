import Foundation

/// Platform-neutral activity data. Platform adapters are responsible for
/// translating AppKit/Win32 observations into these plain values.
public struct ActivityInput: Codable, Equatable, Sendable {
    public let frontmostApplicationIdentifier: String?
    public let frontmostApplicationName: String?
    public let pointerApplicationIdentifier: String?
    public let pointerApplicationName: String?
    public let idleSeconds: TimeInterval
    public let browserURL: String?

    public init(
        frontmostApplicationIdentifier: String?,
        frontmostApplicationName: String?,
        pointerApplicationIdentifier: String? = nil,
        pointerApplicationName: String? = nil,
        idleSeconds: TimeInterval,
        browserURL: String? = nil
    ) {
        self.frontmostApplicationIdentifier = frontmostApplicationIdentifier
        self.frontmostApplicationName = frontmostApplicationName
        self.pointerApplicationIdentifier = pointerApplicationIdentifier
        self.pointerApplicationName = pointerApplicationName
        self.idleSeconds = idleSeconds
        self.browserURL = browserURL
    }
}

public protocol ApplicationRuleProviding {
    var activityIdentifier: String { get }
    var activityDisplayName: String { get }
    var activityIsEnabled: Bool { get }
    var activityCategoryRawValue: String { get }
}

public struct ApplicationRule: Codable, Equatable, Sendable, ApplicationRuleProviding {
    public let identifier: String
    public let displayName: String
    public let isEnabled: Bool
    public let categoryRawValue: String

    public init(identifier: String, displayName: String, isEnabled: Bool, categoryRawValue: String) {
        self.identifier = identifier
        self.displayName = displayName
        self.isEnabled = isEnabled
        self.categoryRawValue = categoryRawValue
    }

    public var activityIdentifier: String { identifier }
    public var activityDisplayName: String { displayName }
    public var activityIsEnabled: Bool { isEnabled }
    public var activityCategoryRawValue: String { categoryRawValue }
}

public protocol WebsiteActivityRuleProviding {
    var activityIdentifier: String { get }
    var activityDisplayName: String { get }
    var activityTitleContains: String { get }
    var activityPathContains: String { get }
    var activityIsEnabled: Bool { get }
    var activityMatchesHostOnly: Bool? { get }
    var activityCategoryRawValue: String { get }
}

public struct WebsiteActivityRule: Codable, Equatable, Sendable, WebsiteActivityRuleProviding {
    public let identifier: String
    public let displayName: String
    public let titleContains: String
    public let pathContains: String
    public let isEnabled: Bool
    public let matchesHostOnly: Bool?
    public let categoryRawValue: String

    public init(
        identifier: String,
        displayName: String,
        titleContains: String,
        pathContains: String,
        isEnabled: Bool,
        matchesHostOnly: Bool?,
        categoryRawValue: String
    ) {
        self.identifier = identifier
        self.displayName = displayName
        self.titleContains = titleContains
        self.pathContains = pathContains
        self.isEnabled = isEnabled
        self.matchesHostOnly = matchesHostOnly
        self.categoryRawValue = categoryRawValue
    }

    public var activityIdentifier: String { identifier }
    public var activityDisplayName: String { displayName }
    public var activityTitleContains: String { titleContains }
    public var activityPathContains: String { pathContains }
    public var activityIsEnabled: Bool { isEnabled }
    public var activityMatchesHostOnly: Bool? { matchesHostOnly }
    public var activityCategoryRawValue: String { categoryRawValue }
}

public struct TrackingTarget: Codable, Equatable, Sendable {
    public enum Source: String, Codable, Equatable, Sendable {
        case application
        case website
    }

    public let identifier: String
    public let displayName: String
    public let categoryRawValue: String
    public let source: Source

    public init(identifier: String, displayName: String, categoryRawValue: String, source: Source) {
        self.identifier = identifier
        self.displayName = displayName
        self.categoryRawValue = categoryRawValue
        self.source = source
    }
}

public enum ActivityStateDecision: Codable, Equatable, Sendable {
    case paused
    case away
    case tracking(TrackingTarget)
}

public enum ActivityDecision {
    public static func evaluate<ApplicationRuleType, WebsiteRuleType>(
        input: ActivityInput,
        applicationRules: [ApplicationRuleType],
        websiteRules: [WebsiteRuleType],
        supportedBrowserIdentifiers: Set<String>,
        awayThreshold: TimeInterval,
        isManuallyPaused: Bool
    ) -> ActivityStateDecision
    where ApplicationRuleType: ApplicationRuleProviding,
          WebsiteRuleType: WebsiteActivityRuleProviding {
        if isManuallyPaused { return .paused }
        if input.idleSeconds >= awayThreshold { return .away }

        let effectiveIdentifier = input.pointerApplicationIdentifier
            ?? input.frontmostApplicationIdentifier

        if let effectiveIdentifier,
           supportedBrowserIdentifiers.contains(effectiveIdentifier),
           let rule = websiteRules.first(where: {
               WebsiteRules.matches(
                   url: input.browserURL,
                   titleContains: $0.activityTitleContains,
                   pathContains: $0.activityPathContains,
                   isEnabled: $0.activityIsEnabled,
                   matchesHostOnly: $0.activityMatchesHostOnly
               )
           }) {
            return .tracking(.init(
                identifier: rule.activityIdentifier,
                displayName: rule.activityDisplayName,
                categoryRawValue: rule.activityCategoryRawValue,
                source: .website
            ))
        }

        let selectedRule: ApplicationRuleType?
        if let pointerIdentifier = input.pointerApplicationIdentifier {
            selectedRule = applicationRules.first {
                $0.activityIsEnabled && $0.activityIdentifier == pointerIdentifier
            }
        } else {
            selectedRule = applicationRules.first {
                $0.activityIsEnabled && $0.activityIdentifier == input.frontmostApplicationIdentifier
            }
        }

        guard let selectedRule else { return .away }
        return .tracking(.init(
            identifier: selectedRule.activityIdentifier,
            displayName: selectedRule.activityDisplayName,
            categoryRawValue: selectedRule.activityCategoryRawValue,
            source: .application
        ))
    }
}
