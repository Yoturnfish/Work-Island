import Combine
import Foundation

enum IslandDisplayMode: String, CaseIterable, Identifiable, Codable {
    case floating
    case menuBar

    var id: String { rawValue }
    var title: String { self == .floating ? L10n.text("悬浮窗模式") : L10n.text("菜单栏模式") }
}

enum DurationDisplayStyle: String, CaseIterable, Identifiable, Codable {
    case hoursMinutes
    case minutesSeconds

    var id: String { rawValue }
    /// Compact, language-independent labels keep the settings popup aligned.
    var title: String { self == .hoursMinutes ? "H + M" : "M + S" }
}

enum IslandHorizontalPlacement: String, CaseIterable, Identifiable, Codable {
    case leading
    case center
    case trailing

    var id: String { rawValue }
    var title: String {
        switch self {
        case .leading: L10n.text("左侧")
        case .center: L10n.text("居中")
        case .trailing: L10n.text("右侧")
        }
    }
}

@MainActor
final class TrackingSettings: ObservableObject {
    private enum Key {
        static let goalSeconds = "goalSeconds"
        static let legacyIdleThreshold = "idleThreshold"
        static let awayThreshold = "awayThreshold"
        static let awayTimerEnabled = "awayTimerEnabled"
        static let trackedApplications = "trackedApplications"
        static let trackedWebsites = "trackedWebsites"
        static let displayMode = "displayMode"
        static let islandScale = "islandScale"
        static let islandOpacity = "islandOpacity"
        static let simulatedGlassFrosting = "simulatedGlassFrosting"
        static let liquidGlassBlurRadius = "liquidGlassBlurRadius"
        static let horizontalPlacement = "horizontalPlacement"
        static let topOffset = "topOffset"
        static let durationDisplayStyle = "durationDisplayStyle"
        static let dayResetMinutes = "dayResetMinutes"
        static let launchAtLogin = "launchAtLogin"
        static let applicationCategoryMigration = "applicationCategoryMigrationV1"
    }

    static let goalOptions: [TimeInterval] = [1, 2, 3, 4, 6, 8].map { Double($0) * 3600 }
    static let defaultGoalSeconds: TimeInterval = 2 * 3600
    static let defaultAwayThreshold: TimeInterval = 30
    @Published var language: AppLanguage { didSet { defaults.set(language.rawValue, forKey: AppLanguage.defaultsKey) } }
    @Published var customTitle: String { didSet { defaults.set(customTitle, forKey: "customTitle") } }
    var displayTitle: String {
        if let name = colorNames[selectedColor.rawValue]?.trimmingCharacters(in: .whitespacesAndNewlines), !name.isEmpty { return name }
        return L10n.text("屏幕工作时间", language: language)
    }
    /// The title editor starts with only an explicitly saved name. The
    /// localized fallback ("屏幕工作时间" / "Work time") is never used as editable text.
    var titleEditingDraft: String {
        guard let name = colorNames[selectedColor.rawValue]?
            .trimmingCharacters(in: .whitespacesAndNewlines), !name.isEmpty else { return "" }
        return name
    }
    func saveTitle(_ value: String) {
        let title = String(value.trimmingCharacters(in: .whitespacesAndNewlines).prefix(60))
        if selectedColor == .green { customTitle = title }
        renameColor(selectedColor, to: title)
    }

    private let defaults: UserDefaults
    private var colorGroupIDs: [String: String]
    @Published private(set) var colorNames: [String: String] {
        didSet { defaults.set(colorNames, forKey: "colorGroupNames") }
    }
    func renameColor(_ color: ThemeAccent, to name: String) {
        colorNames[color.rawValue] = String(name.prefix(60))
    }
    var selectedColor: ThemeAccent {
        guard let id = activeThemeID else { return .green }
        return colorGroupIDs.first(where: { $0.value == id.uuidString }).flatMap { ThemeAccent(rawValue: $0.key) } ?? activeTheme?.accent ?? .blue
    }
    func selectColor(_ color: ThemeAccent) {
        if let saved = colorGroupIDs[color.rawValue] {
            activeThemeID = saved == "default" ? nil : UUID(uuidString: saved)
        } else {
            var theme = TrackingTheme(name: color.title)
            theme.accent = color
            themes.append(theme)
            colorGroupIDs[color.rawValue] = theme.id.uuidString
            defaults.set(colorGroupIDs, forKey: "colorGroupIDs")
            activeThemeID = theme.id
        }
    }
    @Published var themes: [TrackingTheme] {
        didSet { defaults.set(try? JSONEncoder().encode(themes), forKey: "trackingThemes") }
    }
    @Published var activeThemeID: UUID? {
        didSet { defaults.set(activeThemeID?.uuidString, forKey: "activeTrackingTheme") }
    }
    @Published private var colorProgressStyles: [String: String] {
        didSet { defaults.set(colorProgressStyles, forKey: "colorProgressStyles") }
    }
    private let legacyProgressStyle: IslandProgressStyle
    var progressStyle: IslandProgressStyle {
        get {
            guard let stored = colorProgressStyles[selectedColor.rawValue] else { return legacyProgressStyle }
            return IslandProgressStyle(rawValue: stored) ?? .matrix
        }
        set { colorProgressStyles[selectedColor.rawValue] = newValue.rawValue }
    }
    var activeTheme: TrackingTheme? { themes.first { $0.id == activeThemeID } }
    // A missing/deleted active theme is deliberately empty, never a fallback to broad rules.
    var ruleApplications: [TrackedApplication] {
        get { activeThemeID == nil ? trackedApplications : activeTheme?.applications ?? [] }
        set {
            if activeThemeID == nil { trackedApplications = Self.uniqueApplications(newValue) }
            else if let index = themes.firstIndex(where: { $0.id == activeThemeID }) { themes[index].applications = Self.uniqueApplications(newValue) }
        }
    }
    var ruleWebsites: [TrackedWebsite] {
        get { activeThemeID == nil ? trackedWebsites : activeTheme?.websites ?? [] }
        set {
            if activeThemeID == nil { trackedWebsites = Self.uniqueWebsites(newValue) }
            else if let index = themes.firstIndex(where: { $0.id == activeThemeID }) { themes[index].websites = Self.uniqueWebsites(newValue) }
        }
    }
    func createTheme() {
        let theme = TrackingTheme(name: L10n.text("新组合", language: language))
        themes.append(theme)
        activeThemeID = theme.id
    }
    func addRecognizedApplication(identifier: String, name: String) {
        if let index = ruleApplications.firstIndex(where: { Self.ruleKey($0.bundleIdentifier) == Self.ruleKey(identifier) }) {
            ruleApplications[index].isEnabled = true
        } else {
            ruleApplications.append(.init(bundleIdentifier: identifier, displayName: name))
        }
    }
    @Published var hasCompletedOnboarding: Bool {
        didSet { defaults.set(hasCompletedOnboarding, forKey: "hasCompletedOnboarding") }
    }

    @Published var goalSeconds: TimeInterval { didSet { defaults.set(goalSeconds, forKey: Key.goalSeconds) } }
    @Published var awayThreshold: TimeInterval { didSet { defaults.set(awayThreshold, forKey: Key.awayThreshold) } }
    @Published var awayTimerEnabled: Bool { didSet { defaults.set(awayTimerEnabled, forKey: Key.awayTimerEnabled) } }
    @Published var superReminderEnabled: Bool { didSet { defaults.set(superReminderEnabled, forKey: "superReminderEnabled") } }
    @Published var displayMode: IslandDisplayMode { didSet { defaults.set(displayMode.rawValue, forKey: Key.displayMode) } }
    @Published var islandScale: Double { didSet { defaults.set(islandScale, forKey: Key.islandScale) } }
    @Published var islandOpacity: Double { didSet { defaults.set(islandOpacity, forKey: Key.islandOpacity) } }
    @Published var backgroundStyle: IslandBackgroundStyle { didSet { defaults.set(backgroundStyle.rawValue, forKey: "backgroundStyle") } }
    @Published var glassBlurRadius: Double {
        didSet {
            let normalized = GlassBlurMetrics.normalized(glassBlurRadius)
            if glassBlurRadius != normalized { glassBlurRadius = normalized }
            defaults.set(normalized, forKey: Key.liquidGlassBlurRadius)
        }
    }
    /// Material blending strength, independent of the standard background's window opacity.
    @Published var glassFrosting: Double {
        didSet {
            let normalized = Self.normalizedGlassFrosting(glassFrosting)
            if glassFrosting != normalized { glassFrosting = normalized }
            defaults.set(normalized, forKey: Key.simulatedGlassFrosting)
        }
    }
    @Published var horizontalPlacement: IslandHorizontalPlacement { didSet { defaults.set(horizontalPlacement.rawValue, forKey: Key.horizontalPlacement) } }
    @Published var topOffset: Double { didSet { defaults.set(topOffset, forKey: Key.topOffset) } }
    @Published var durationDisplayStyle: DurationDisplayStyle { didSet { defaults.set(durationDisplayStyle.rawValue, forKey: Key.durationDisplayStyle) } }
    @Published var dayResetMinutes: Int { didSet { defaults.set(dayResetMinutes, forKey: Key.dayResetMinutes) } }
    @Published var launchAtLogin: Bool { didSet { defaults.set(launchAtLogin, forKey: Key.launchAtLogin) } }
    @Published var launchAtLoginError: String?
    @Published var launchAtLoginRegistered = false
    @Published var isPreviewingOpacity = false
    @Published var trackedApplications: [TrackedApplication] {
        didSet {
            if let data = try? JSONEncoder().encode(trackedApplications) {
                defaults.set(data, forKey: Key.trackedApplications)
            }
        }
    }
    @Published var trackedWebsites: [TrackedWebsite] { didSet { if let data = try? JSONEncoder().encode(trackedWebsites) { defaults.set(data, forKey: Key.trackedWebsites) } } }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        superReminderEnabled = defaults.bool(forKey: "superReminderEnabled")
        awayTimerEnabled = defaults.bool(forKey: Key.awayTimerEnabled)
        var savedNames = defaults.dictionary(forKey: "colorGroupNames") as? [String: String] ?? [:]
        if savedNames["green"] == nil, let legacyTitle = defaults.string(forKey: "customTitle"), !legacyTitle.isEmpty {
            savedNames["green"] = legacyTitle
        }
        colorNames = savedNames
        colorGroupIDs = defaults.dictionary(forKey: "colorGroupIDs") as? [String: String] ?? [:]
        themes = defaults.data(forKey: "trackingThemes").flatMap { try? JSONDecoder().decode([TrackingTheme].self, from: $0) } ?? []
        activeThemeID = defaults.string(forKey: "activeTrackingTheme").flatMap(UUID.init(uuidString:))
        legacyProgressStyle = IslandProgressStyle(rawValue: defaults.string(forKey: "progressStyle") ?? "") ?? .matrix
        colorProgressStyles = defaults.dictionary(forKey: "colorProgressStyles") as? [String: String] ?? [:]
        customTitle = defaults.string(forKey: "customTitle") ?? ""
        // Existing installations keep their language and do not get a surprise first-run flow.
        let existingInstallation = defaults.object(forKey: Key.awayThreshold) != nil
            || defaults.object(forKey: Key.trackedApplications) != nil
            || defaults.object(forKey: AppLanguage.defaultsKey) != nil
        let completed = defaults.object(forKey: "hasCompletedOnboarding") as? Bool ?? existingInstallation
        hasCompletedOnboarding = completed
        defaults.set(completed, forKey: "hasCompletedOnboarding")
        let initialLanguage = AppLanguage(rawValue: defaults.string(forKey: AppLanguage.defaultsKey) ?? "")
            ?? AppLanguage.preferredSystemLanguage()
        language = initialLanguage
        defaults.set(initialLanguage.rawValue, forKey: AppLanguage.defaultsKey)
        var restoredBackground = IslandBackgroundStyle.restored(defaults.string(forKey: "backgroundStyle"))
        // Preserve the previously enabled/disabled renderer once, then retire
        // the auxiliary switch. Older glass selections stay system-transparent.
        if !defaults.bool(forKey: "glassThemeDropdownMigrated"), restoredBackground == .glass,
           defaults.bool(forKey: GlassCapturePermission.requestKey),
           !defaults.bool(forKey: GlassCapturePermission.legacyKey) {
            restoredBackground = .liquidGlass
        }
        backgroundStyle = restoredBackground
        defaults.set(restoredBackground.rawValue, forKey: "backgroundStyle")
        defaults.removeObject(forKey: GlassCapturePermission.legacyKey)
        // Mark migration so choosing the old Transparent option stays old on
        // every subsequent launch, even with an existing recording permission.
        defaults.set(true, forKey: "glassThemeDropdownMigrated")
        defaults.removeObject(forKey: "glassStyle")
        // The retired implementation persisted zero on every launch. Do not interpret that
        // obsolete value as a user choice for the new 0–100 simulated-material control.
        defaults.removeObject(forKey: "glassBlurStrength")
        let savedFrosting = (defaults.object(forKey: Key.simulatedGlassFrosting) as? NSNumber)?.doubleValue
        let restoredFrosting = Self.normalizedGlassFrosting(savedFrosting ?? 0)
        glassFrosting = restoredFrosting
        defaults.set(restoredFrosting, forKey: Key.simulatedGlassFrosting)
        let savedBlur = (defaults.object(forKey: Key.liquidGlassBlurRadius) as? NSNumber)?.doubleValue
        let restoredBlur = GlassBlurMetrics.normalized(savedBlur ?? GlassBlurMetrics.defaultRadius)
        glassBlurRadius = restoredBlur
        defaults.set(restoredBlur, forKey: Key.liquidGlassBlurRadius)
        let storedGoal = defaults.double(forKey: Key.goalSeconds)
        let requestedGoal = storedGoal > 0 ? storedGoal : Self.defaultGoalSeconds
        let migratedGoal = Self.goalOptions.min(by: { abs($0 - requestedGoal) < abs($1 - requestedGoal) }) ?? Self.defaultGoalSeconds
        goalSeconds = migratedGoal
        defaults.set(migratedGoal, forKey: Key.goalSeconds)
        let storedAwayThreshold = defaults.double(forKey: Key.awayThreshold)
        let legacyIdleThreshold = defaults.double(forKey: Key.legacyIdleThreshold)
        let initialAwayThreshold = storedAwayThreshold > 0 ? storedAwayThreshold : (legacyIdleThreshold > 0 ? legacyIdleThreshold : Self.defaultAwayThreshold)
        awayThreshold = initialAwayThreshold
        if storedAwayThreshold <= 0 {
            defaults.set(initialAwayThreshold, forKey: Key.awayThreshold)
        }
        displayMode = IslandDisplayMode(rawValue: defaults.string(forKey: Key.displayMode) ?? "") ?? .floating
        let storedOpacity = defaults.double(forKey: Key.islandOpacity)
        islandScale = 0.82
        islandOpacity = storedOpacity > 0 ? storedOpacity : 1
        horizontalPlacement = IslandHorizontalPlacement(rawValue: defaults.string(forKey: Key.horizontalPlacement) ?? "") ?? .center
        topOffset = max(0, defaults.double(forKey: Key.topOffset))
        durationDisplayStyle = DurationDisplayStyle(rawValue: defaults.string(forKey: Key.durationDisplayStyle) ?? "") ?? .hoursMinutes
        dayResetMinutes = min(max(defaults.integer(forKey: Key.dayResetMinutes), 0), 1439)
        launchAtLogin = defaults.object(forKey: Key.launchAtLogin) as? Bool ?? true
        launchAtLoginError = nil
        if let data = defaults.data(forKey: Key.trackedApplications),
           let apps = try? JSONDecoder().decode([TrackedApplication].self, from: data) {
            trackedApplications = apps
        } else {
            trackedApplications = TrackedApplication.defaults
        }
        trackedWebsites = defaults.data(forKey: Key.trackedWebsites).flatMap { try? JSONDecoder().decode([TrackedWebsite].self, from: $0) } ?? []
        for index in trackedWebsites.indices where trackedWebsites[index].pathContains.isEmpty {
            trackedWebsites[index].pathContains = trackedWebsites[index].titleContains
        }
        if !defaults.bool(forKey: Key.applicationCategoryMigration) {
            for index in trackedApplications.indices where trackedApplications[index].category == .other {
                if let category = TrackedApplication.builtInCategory(for: trackedApplications[index].bundleIdentifier) {
                    trackedApplications[index].category = category
                }
            }
            defaults.set(true, forKey: Key.applicationCategoryMigration)
        }
        if colorGroupIDs.isEmpty {
            colorGroupIDs[ThemeAccent.green.rawValue] = "default"
            // Preserve separate legacy groups; prioritize the currently selected group.
            let ordered = themes.filter { $0.id == activeThemeID } + themes.filter { $0.id != activeThemeID }
            for theme in ordered {
                guard let color = colorGroupIDs[theme.accent.rawValue] == nil ? theme.accent : ThemeAccent.allCases.first(where: { colorGroupIDs[$0.rawValue] == nil }) else { continue }
                colorGroupIDs[color.rawValue] = theme.id.uuidString
                if let index = themes.firstIndex(where: { $0.id == theme.id }) { themes[index].accent = color }
            }
            defaults.set(colorGroupIDs, forKey: "colorGroupIDs")
        }
        removeDuplicateRules()
    }

    private static func normalizedGlassFrosting(_ value: Double) -> Double {
        value.isFinite ? min(max(value, 0), 100) : 0
    }

    @discardableResult func toggleRecognizedApplication(identifier: String, name: String) -> Bool {
        if ruleApplications.contains(where: { Self.ruleKey($0.bundleIdentifier) == Self.ruleKey(identifier) }) {
            ruleApplications.removeAll { Self.ruleKey($0.bundleIdentifier) == Self.ruleKey(identifier) }
            return false
        }
        addRecognizedApplication(identifier: identifier, name: name)
        return true
    }
    static func ruleKey(_ value: String) -> String { value.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() }
    private static func uniqueApplications(_ rules: [TrackedApplication]) -> [TrackedApplication] {
        var seen = Set<String>()
        return rules.filter { seen.insert(ruleKey($0.bundleIdentifier)).inserted }
    }
    private static func uniqueWebsites(_ rules: [TrackedWebsite]) -> [TrackedWebsite] {
        var seen = Set<String>()
        return rules.filter {
            let key = ruleKey($0.pathContains.isEmpty ? $0.titleContains : $0.pathContains)
            return key.isEmpty || seen.insert(key).inserted
        }
    }
    func hasWebsite(keyword: String) -> Bool {
        ruleWebsites.contains { Self.ruleKey($0.pathContains.isEmpty ? $0.titleContains : $0.pathContains) == Self.ruleKey(keyword) }
    }
    func removeDuplicateRules() {
        trackedApplications = Self.uniqueApplications(trackedApplications)
        trackedWebsites = Self.uniqueWebsites(trackedWebsites)
        for index in themes.indices {
            themes[index].applications = Self.uniqueApplications(themes[index].applications)
            themes[index].websites = Self.uniqueWebsites(themes[index].websites)
        }
    }

    func addApplication(url: URL) {
        guard let bundle = Bundle(url: url),
              let identifier = bundle.bundleIdentifier else { return }
        let name = (bundle.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String)
            ?? (bundle.object(forInfoDictionaryKey: "CFBundleName") as? String)
            ?? url.deletingPathExtension().lastPathComponent
        addRecognizedApplication(identifier: identifier, name: name)
    }

    func addWebsiteRuleAtBeginning() {
        ruleWebsites.insert(.init(displayName: L10n.text("新路径", language: language)), at: 0)
    }

    @discardableResult func addManualWebsite(_ value: String) -> Bool {
        let keyword = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !keyword.isEmpty else { return false }
        if hasWebsite(keyword: keyword) { return true }
        ruleWebsites.insert(.init(displayName: keyword, pathContains: keyword), at: 0)
        return true
    }

    @discardableResult func addCurrentWebsite(address: String) -> String? {
        guard let keyword = WebsiteKeyword.extract(from: address) else { return nil }
        if hasWebsite(keyword: keyword) { return keyword }
        var rule = TrackedWebsite(displayName: keyword, pathContains: keyword)
        rule.matchesHostOnly = true
        ruleWebsites.insert(rule, at: 0)
        return keyword
    }
    @discardableResult func toggleCurrentWebsite(address: String) -> String? {
        guard let keyword = WebsiteKeyword.extract(from: address) else { return nil }
        if hasWebsite(keyword: keyword) {
            ruleWebsites.removeAll { Self.ruleKey($0.pathContains.isEmpty ? $0.titleContains : $0.pathContains) == Self.ruleKey(keyword) }
        } else { _ = addCurrentWebsite(address: address) }
        return keyword
    }
}
