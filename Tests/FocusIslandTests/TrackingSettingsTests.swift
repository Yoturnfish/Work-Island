import Foundation
import Testing
@testable import FocusIsland

@MainActor
struct TrackingSettingsTests {
    @Test func liquidGlassBlurPersistsZeroAndClampsWithoutChangingLegacyFrosting() {
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }
        defaults.set(37.0, forKey: "simulatedGlassFrosting")
        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.glassBlurRadius == 1.5)
        for (value, expected) in [(0.0, 0.0), (3, 3), (-1, 0), (99, 6), (.nan, 0), (.infinity, 0)] {
            settings.glassBlurRadius = value
            #expect(settings.glassBlurRadius == expected)
            #expect(TrackingSettings(defaults: defaults).glassBlurRadius == expected)
            #expect(settings.glassFrosting == 37)
            defaults.set(value, forKey: "liquidGlassBlurRadius")
            #expect(TrackingSettings(defaults: defaults).glassBlurRadius == expected)
        }
        defaults.set("invalid", forKey: "liquidGlassBlurRadius")
        #expect(TrackingSettings(defaults: defaults).glassBlurRadius == 1.5)
    }
    @Test func glassStylesMigrateWithoutChangingOtherPreferences() {
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }
        for oldValue in ["liquidGlass", "stableLiquidGlass", "glass"] {
            defaults.set(oldValue, forKey: "backgroundStyle")
            defaults.set("soft", forKey: "glassStyle")
            defaults.set("unchanged", forKey: "windowPositionTest")
            let settings = TrackingSettings(defaults: defaults)
            #expect(settings.backgroundStyle == .glass)
            #expect(defaults.string(forKey: "backgroundStyle") == "glass")
            #expect(defaults.object(forKey: "glassStyle") == nil)
            #expect(defaults.string(forKey: "windowPositionTest") == "unchanged")
        }
        defaults.set("standard", forKey: "backgroundStyle")
        #expect(TrackingSettings(defaults: defaults).backgroundStyle == .standard)
        #expect(IslandBackgroundStyle.allCases == [.standard, .glass, .liquidGlass])
        #expect(IslandBackgroundStyle.standard.usesWindowShadow)
        #expect(!IslandBackgroundStyle.glass.usesWindowShadow)
        #expect(L10n.text("模拟玻璃", language: .english) == "Simulated Glass")
    }

    @Test func glassDropdownMigrationPreservesBothOldSwitchPositionsAndLaterChoices() {
        for legacy in [false, true] {
            let (defaults, suite) = isolatedDefaults()
            defer { defaults.removePersistentDomain(forName: suite) }
            defaults.set("glass", forKey: "backgroundStyle")
            defaults.set(true, forKey: GlassCapturePermission.requestKey)
            defaults.set(legacy, forKey: GlassCapturePermission.legacyKey)
            let settings = TrackingSettings(defaults: defaults)
            #expect(settings.backgroundStyle == (legacy ? .glass : .liquidGlass))
            #expect(defaults.object(forKey: GlassCapturePermission.legacyKey) == nil)
            settings.backgroundStyle = .glass
            #expect(TrackingSettings(defaults: defaults).backgroundStyle == .glass)
            settings.backgroundStyle = .liquidGlass
            #expect(TrackingSettings(defaults: defaults).backgroundStyle == .liquidGlass)
            #expect(defaults.string(forKey: "backgroundStyle") == "capturedGlass")
        }
        #expect(IslandBackgroundStyle.restored("capturedGlass") == .liquidGlass)
        #expect(IslandBackgroundStyle.restored("invalid") == .standard)
        #expect(!IslandBackgroundStyle.liquidGlass.usesWindowShadow)
    }

    @Test func languagePersistsWithoutChangingTrackingConfiguration() {
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.language == AppLanguage.preferredSystemLanguage())
        settings.language = .english
        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.language == .english)
        #expect(restored.goalSeconds == settings.goalSeconds)
        #expect(restored.trackedApplications == settings.trackedApplications)
        restored.language = .chinese
        #expect(TrackingSettings(defaults: defaults).language == .chinese)
        defaults.set("unknown", forKey: AppLanguage.defaultsKey)
        #expect(TrackingSettings(defaults: defaults).language == AppLanguage.preferredSystemLanguage())
    }

    @Test func systemLanguageDefaultRecognizesChineseLocalesAndFallsBackToEnglish() {
        for identifier in ["zh-Hans", "zh-Hant-TW", "zh_CN"] {
            #expect(AppLanguage.preferredSystemLanguage(from: [identifier]) == .chinese)
        }
        for languages in [["en-US"], ["ja-JP"], ["fr-FR"], []] {
            #expect(AppLanguage.preferredSystemLanguage(from: languages) == .english)
        }
    }

    @Test func simulatedFrostingUsesIndependentDefaultAndPreservesZero() {
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }
        defaults.set(0.0, forKey: "glassBlurStrength")
        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.glassFrosting == 0)
        #expect(defaults.double(forKey: "simulatedGlassFrosting") == 0)
        #expect(defaults.object(forKey: "glassBlurStrength") == nil)
        settings.glassFrosting = 0
        #expect(TrackingSettings(defaults: defaults).glassFrosting == 0)
        settings.glassFrosting = 100
        #expect(TrackingSettings(defaults: defaults).glassFrosting == 100)
        #expect(L10n.text("磨砂度", language: .english) == "Frosting")
    }

    @Test func simulatedFrostingClampsInvalidRuntimeAndPersistedValues() {
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        for (value, expected) in [(-20.0, 0.0), (120, 100), (.nan, 0), (.infinity, 0), (-.infinity, 0)] {
            settings.glassFrosting = value
            #expect(settings.glassFrosting == expected)
            #expect(defaults.double(forKey: "simulatedGlassFrosting") == expected)
            #expect(TrackingSettings(defaults: defaults).glassFrosting == expected)

            defaults.set(value, forKey: "simulatedGlassFrosting")
            #expect(TrackingSettings(defaults: defaults).glassFrosting == expected)
            #expect(defaults.double(forKey: "simulatedGlassFrosting") == expected)
        }
        defaults.set("invalid", forKey: "simulatedGlassFrosting")
        #expect(TrackingSettings(defaults: defaults).glassFrosting == 0)
    }

    @Test func translationsAndNewRuleUseSelectedLanguage() {
        #expect(L10n.text("设置", language: .english) == "Settings")
        #expect(L10n.text("设置", language: .chinese) == "设置")
        #expect(L10n.text("Custom app", language: .english) == "Custom app")
        #expect(L10n.english.values.allSatisfy { !$0.isEmpty })
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        settings.language = .english
        settings.addWebsiteRuleAtBeginning()
        #expect(settings.trackedWebsites.first?.displayName == L10n.text("新路径", language: settings.language))
    }

    @Test func defaultsMatchTheProductSpecification() {
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }

        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.language == AppLanguage.preferredSystemLanguage())
        #expect(settings.goalSeconds == TrackingSettings.defaultGoalSeconds)
        #expect(settings.awayThreshold == TrackingSettings.defaultAwayThreshold)
        #expect(!settings.awayTimerEnabled)
        #expect(!settings.superReminderEnabled)
        #expect(settings.trackedApplications.count == 5)
        #expect(settings.displayMode == .floating)
        #expect(settings.islandScale == 0.82)
        #expect(settings.islandOpacity == 1)
        #expect(settings.glassFrosting == 0)
        #expect(settings.horizontalPlacement == .center)
        #expect(settings.topOffset == 0)
        #expect(settings.durationDisplayStyle == .hoursMinutes)
        #expect(settings.backgroundStyle == .standard)
        #expect(settings.dayResetMinutes == 0)
        #expect(TrackedApplication.defaults.first?.category == .editing)
        #expect(settings.launchAtLogin)
    }

    @Test func valuesAndApplicationListPersist() {
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }

        var first: TrackingSettings? = TrackingSettings(defaults: defaults)
        first?.goalSeconds = 21_600
        first?.awayThreshold = 120
        first?.awayTimerEnabled = true
        first?.superReminderEnabled = true
        first?.trackedApplications = [
            .init(bundleIdentifier: "com.example.creator", displayName: "Creator")
        ]
        first?.displayMode = .menuBar
        first?.backgroundStyle = .glass
        first?.islandOpacity = 0.68
        first?.horizontalPlacement = .trailing
        first?.topOffset = 42
        first?.durationDisplayStyle = .minutesSeconds
        first?.dayResetMinutes = 180
        first?.launchAtLogin = false
        first = nil

        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.goalSeconds == 21_600)
        #expect(restored.awayThreshold == 120)
        #expect(restored.awayTimerEnabled)
        #expect(restored.superReminderEnabled)
        #expect(restored.trackedApplications.first?.bundleIdentifier == "com.example.creator")
        #expect(restored.displayMode == .menuBar)
        #expect(restored.backgroundStyle == .glass)
        #expect(restored.islandScale == 0.82)
        #expect(restored.islandOpacity == 0.68)
        #expect(restored.horizontalPlacement == .trailing)
        #expect(restored.topOffset == 42)
        #expect(restored.durationDisplayStyle == .minutesSeconds)
        #expect(restored.dayResetMinutes == 180)
        #expect(!restored.launchAtLogin)
    }

    @Test func migratesLegacyIdleThresholdWhenAwayThresholdIsMissing() {
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }

        defaults.set(45.0, forKey: "idleThreshold")
        let settings = TrackingSettings(defaults: defaults)

        #expect(settings.awayThreshold == 45)
        #expect(defaults.double(forKey: "awayThreshold") == 45)
    }

    @Test func newWebsiteRuleIsInsertedAtTheBeginning() {
        let (defaults, suite) = isolatedDefaults()
        defer { defaults.removePersistentDomain(forName: suite) }

        let settings = TrackingSettings(defaults: defaults)
        settings.trackedWebsites = [.init(displayName: "旧路径", pathContains: "old.example")]
        settings.addWebsiteRuleAtBeginning()

        #expect(settings.trackedWebsites.count == 2)
        #expect(settings.trackedWebsites.first?.displayName == L10n.text("新路径", language: settings.language))
        #expect(settings.trackedWebsites.last?.pathContains == "old.example")
    }

    private func isolatedDefaults() -> (UserDefaults, String) {
        let suite = "FocusIslandSettingsTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defaults.removePersistentDomain(forName: suite)
        return (defaults, suite)
    }
}
