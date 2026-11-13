import Foundation
import Testing
@testable import FocusIsland

@MainActor struct TrackingThemeTests {
    @Test func backgroundStyleAndFrostingPersistAndValidate() {
        let suite = "GlassSettings.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.backgroundStyle == .standard)
        #expect(settings.glassFrosting == 0)
        settings.islandOpacity = 0.6
        settings.backgroundStyle = .glass
        settings.glassFrosting = 72
        defaults.set(0.8, forKey: "glassBlurStrength")
        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.backgroundStyle == .glass)
        #expect(restored.glassFrosting == 72)
        #expect(defaults.object(forKey: "glassBlurStrength") == nil)
        #expect(restored.islandOpacity == 0.6)
        restored.backgroundStyle = .standard
        #expect(restored.glassFrosting == 72)
        #expect(restored.islandOpacity == 0.6)
        defaults.set("invalid", forKey: "backgroundStyle")
        #expect(TrackingSettings(defaults: defaults).glassFrosting == 72)
        #expect(TrackingSettings(defaults: defaults).backgroundStyle == .standard)
    }
    @Test func automaticSelectionsToggleAndRemainUniqueAcrossRestart() {
        let suite = "AutoToggle.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        settings.selectColor(.blue)
        #expect(settings.toggleRecognizedApplication(identifier: "test.app", name: "Test"))
        settings.addRecognizedApplication(identifier: "test.app", name: "Test")
        #expect(settings.ruleApplications.count == 1)
        #expect(!settings.toggleRecognizedApplication(identifier: "test.app", name: "Test"))
        #expect(settings.ruleApplications.isEmpty)
        _ = settings.toggleCurrentWebsite(address: "https://t.bilibili.com/?tab=video")
        _ = settings.addCurrentWebsite(address: "https://www.bilibili.com/video/one")
        #expect(settings.ruleWebsites.count == 1)
        _ = settings.toggleCurrentWebsite(address: "https://www.bilibili.com/")
        #expect(settings.ruleWebsites.isEmpty)
        _ = settings.toggleCurrentWebsite(address: "https://www.bilibili.com/")
        settings.selectColor(.green)
        #expect(!settings.hasWebsite(keyword: "bilibili"))
        settings.selectColor(.blue)
        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.hasWebsite(keyword: " BILIBILI "))
        #expect(restored.toggleCurrentWebsite(address: "about:blank") == nil)
        #expect(restored.ruleWebsites.count == 1)
    }
    @Test func legacyDuplicatesAreRemovedWithinEachGroupOnly() {
        let suite = "LegacyDuplicates.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        settings.createTheme()
        settings.themes[0].applications = [.init(bundleIdentifier: "app", displayName: "Original"), .init(bundleIdentifier: "app", displayName: "Duplicate")]
        settings.themes[0].websites = [.init(displayName: "Original", pathContains: "example"), .init(displayName: "Duplicate", pathContains: " EXAMPLE ")]
        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.ruleApplications.count == 1)
        #expect(restored.ruleApplications[0].displayName == "Original")
        #expect(restored.ruleWebsites.count == 1)
    }
    @Test func reminderPulsesIncreaseToThreePerTwoSeconds() {
        #expect(SuperReminderTiming.intensity(elapsed: 30.75, threshold: 30) > 0.99)
        #expect(SuperReminderTiming.intensity(elapsed: 31.5, threshold: 30) < 0.00001)
        #expect(SuperReminderTiming.intensity(elapsed: 40, threshold: 30) == 0)
        #expect(SuperReminderTiming.intensity(elapsed: 45.75, threshold: 30) > 0.99)
        #expect(SuperReminderTiming.frequency(elapsed: 29, threshold: 30) == 0)
        #expect(SuperReminderTiming.frequency(elapsed: 30, threshold: 30) == 1 / 15.0)
        #expect(SuperReminderTiming.frequency(elapsed: 60, threshold: 30) == 1 / 7.5)
        #expect(SuperReminderTiming.frequency(elapsed: 90, threshold: 30) == 1 / 3.75)
        #expect(SuperReminderTiming.frequency(elapsed: 120, threshold: 30) == 1 / 1.875)
        #expect(SuperReminderTiming.frequency(elapsed: 150, threshold: 30) == 1 / 0.9375)
        #expect(SuperReminderTiming.frequency(elapsed: 120, threshold: 120) == 1 / 60.0)
        #expect(SuperReminderTiming.frequency(elapsed: 180, threshold: 30) == 1.5)
        #expect(SuperReminderTiming.frequency(elapsed: 10000, threshold: 30) == 1.5)
        #expect(SuperReminderTiming.frequency(elapsed: .nan, threshold: 30) == 0)
        #expect(SuperReminderTiming.frequency(elapsed: 100, threshold: 0) == 0)
        for boundary in [60.0, 90, 120, 150, 180, 210] {
            #expect(abs(SuperReminderTiming.intensity(elapsed: boundary - 0.00001, threshold: 30) - SuperReminderTiming.intensity(elapsed: boundary + 0.00001, threshold: 30)) < 0.001)
        }
        var peaks = 0
        for i in 1..<1200 {
            let t = 240.1 + Double(i) / 600
            let value = SuperReminderTiming.intensity(elapsed: t, threshold: 30)
            if value > SuperReminderTiming.intensity(elapsed: t - 1 / 600.0, threshold: 30), value > SuperReminderTiming.intensity(elapsed: t + 1 / 600.0, threshold: 30) { peaks += 1 }
        }
        #expect(peaks == 3)
        let suite = "Reminder.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        #expect(!settings.superReminderEnabled)
        #expect(!settings.awayTimerEnabled)
        settings.superReminderEnabled = true
        settings.awayTimerEnabled = true
        #expect(TrackingSettings(defaults: defaults).superReminderEnabled)
        #expect(TrackingSettings(defaults: defaults).awayTimerEnabled)
    }
    @Test func progressStylesMigrateAndPersistPerColor() {
        let suite = "ColorStyles.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        defaults.set("waveform", forKey: "progressStyle")
        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.progressStyle == .waveform)
        settings.progressStyle = .matrix
        settings.selectColor(.blue)
        #expect(settings.progressStyle == .waveform)
        settings.progressStyle = .waveform
        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.progressStyle == .waveform)
        restored.selectColor(.green)
        #expect(restored.progressStyle == .matrix)
        restored.selectColor(.purple)
        #expect(restored.progressStyle == .waveform)
    }
    @Test func removedStylesFallBackAndWaveformIsSymmetric() {
        for progress in [0.00001, 0.01, 0.99999] {
            #expect(StyledProgressView.filledCells(progress: progress, count: 37) >= 1)
            #expect(StyledProgressView.filledCells(progress: progress, count: 37) < 37)
            #expect(StatisticsPixelMatrix.litCount(progress: progress) >= 1)
            #expect(StatisticsPixelMatrix.litCount(progress: progress) < 16)
        }
        #expect(StyledProgressView.filledCells(progress: 1, count: 37) == 37)
        #expect(StyledProgressView.filledCells(progress: 0, count: 37) == 0)
        #expect(StyledProgressView.waveformPatternCount == 7)
        let waveform = (0..<37).map { StyledProgressView.waveformHeight(index: $0, count: 37) }
        let waveformPeaks = (1..<36).filter {
            waveform[$0] > waveform[$0 - 1] && waveform[$0] >= waveform[$0 + 1]
        }
        #expect(waveformPeaks.count == 7)
        #expect(IslandProgressStyle.allCases == [.matrix, .waveform])
        let suite = "RemovedStyles.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        for removed in ["continuous", "segments", "thinLine", "capsules", "dots", "checkerboard",
                        "steppedBlocks", "lightStreaks", "unknown-style"] {
            defaults.set(removed, forKey: "progressStyle")
            defaults.set(["green": removed], forKey: "colorProgressStyles")
            #expect(TrackingSettings(defaults: defaults).progressStyle == .matrix)
        }
        for index in 0..<38 {
            #expect(StyledProgressView.waveformHeight(index: index, count: 38) == StyledProgressView.waveformHeight(index: 37 - index, count: 38))
        }
        #expect(StyledProgressView.shimmerCenter(width: 200, phase: 0) < -24)
        #expect(StyledProgressView.shimmerCenter(width: 200, phase: 1) > 224)
    }
    @Test func progressAnimationHonorsPauseReductionAndValueLimits() {
        for value in [0.001, 0.5, 1.0, 2.0] {
            #expect(StyledProgressView.shouldAnimate(progress: value, active: true, enabled: true, reduceMotion: false))
            #expect(!StyledProgressView.shouldAnimate(progress: value, active: false, enabled: true, reduceMotion: false))
            #expect(!StyledProgressView.shouldAnimate(progress: value, active: true, enabled: true, reduceMotion: true))
            #expect(!StyledProgressView.shouldAnimate(progress: value, active: true, enabled: false, reduceMotion: false))
        }
        #expect(StyledProgressView.shouldAnimate(progress: 0, active: true, enabled: true, reduceMotion: false))
        #expect(StyledProgressView.clampedProgress(.nan) == 0)
        #expect(StyledProgressView.clampedProgress(-1) == 0)
        #expect(StyledProgressView.clampedProgress(2) == 1)
        let state = OnboardingState()
        state.next()
        #expect(state.step == .goal && state.direction == 1)
        state.back()
        #expect(state.step == .language && state.direction == -1)
    }
    @Test func manualWebsiteOnlyAddsConfirmedNonemptyInputToCurrentColor() {
        let suite = "ManualWebsite.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        settings.selectColor(.blue)
        #expect(!settings.addManualWebsite("  \n "))
        #expect(settings.ruleWebsites.isEmpty)
        #expect(settings.addManualWebsite("  chatgpt  "))
        #expect(settings.ruleWebsites.first?.pathContains == "chatgpt")
        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.ruleWebsites.first?.displayName == "chatgpt")
        restored.selectColor(.purple)
        #expect(restored.ruleWebsites.isEmpty)
    }
    @Test func selectedEmptyColorsCanBeNamedAndNamesDriveTheMainTitle() {
        let suite = "ColorNames.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.titleEditingDraft.isEmpty)
        settings.selectColor(.blue)
        settings.renameColor(.blue, to: "Empty")
        #expect(settings.colorNames["blue"] == "Empty")
        #expect(settings.displayTitle == "Empty")
        #expect(settings.titleEditingDraft == "Empty")
        settings.addRecognizedApplication(identifier: "test", name: "Test")
        settings.renameColor(.blue, to: "Design")
        settings.renameColor(.green, to: "Renamed")
        #expect(settings.colorNames["green"] == "Renamed")
        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.displayTitle == "Design")
        restored.selectColor(.green)
        #expect(restored.displayTitle == "Renamed")
        #expect(restored.titleEditingDraft == "Renamed")
        restored.selectColor(.blue)
        restored.saveTitle("Writing")
        #expect(restored.colorNames["blue"] == "Writing")
        #expect(ThemeAccent.allCases.first == .green)
        #expect(ThemeAccent(rawValue: "orange") == .orange)
    }
    @Test func titleAndColorNamesShareOneValueAndEmptyNamesReset() {
        let suite = "UnifiedTitle.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        defaults.set("Legacy title", forKey: "customTitle")
        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.colorNames["green"] == "Legacy title")
        settings.saveTitle("Default work")
        #expect(settings.colorNames["green"] == "Default work")
        settings.renameColor(.green, to: "")
        #expect(settings.displayTitle == L10n.text("屏幕工作时间", language: settings.language))
        settings.selectColor(.purple)
        settings.saveTitle("Project work")
        #expect(settings.colorNames["purple"] == "Project work")
        settings.saveTitle("")
        #expect(settings.displayTitle == L10n.text("屏幕工作时间", language: settings.language))
        let restored = TrackingSettings(defaults: defaults)
        restored.selectColor(.green)
        #expect(restored.displayTitle == L10n.text("屏幕工作时间", language: restored.language))
    }
    @Test func legacyGroupsKeepTheirRulesWhenAssignedColors() {
        let suite = "LegacyColors.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        settings.createTheme()
        settings.addRecognizedApplication(identifier: "one", name: "One")
        settings.createTheme()
        settings.addRecognizedApplication(identifier: "two", name: "Two")
        let selected = settings.activeThemeID
        defaults.removeObject(forKey: "colorGroupIDs")
        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.activeThemeID == selected)
        #expect(restored.ruleApplications.map(\.bundleIdentifier) == ["two"])
        let colors = restored.themes.map(\.accent)
        #expect(Set(colors).count == 2)
        restored.selectColor(restored.themes[0].accent)
        #expect(restored.ruleApplications.map(\.bundleIdentifier) == ["one"])
    }
    @Test func sevenColorsHaveIndependentPersistentScopes() {
        let suite = "ColorGroups.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        let original = settings.trackedApplications
        #expect(ThemeAccent.allCases.count == 7)
        for color in ThemeAccent.allCases where color != .green {
            settings.selectColor(color)
            #expect(settings.ruleApplications.isEmpty)
            settings.addRecognizedApplication(identifier: color.rawValue, name: color.rawValue)
        }
        let restored = TrackingSettings(defaults: defaults)
        for color in ThemeAccent.allCases where color != .green {
            restored.selectColor(color)
            #expect(restored.selectedColor == color)
            #expect(restored.ruleApplications.map(\.bundleIdentifier) == [color.rawValue])
        }
        restored.selectColor(.green)
        #expect(restored.ruleApplications == original)
    }
    @Test func themesPersistIndependentlyAndRejectDuplicateRules() {
        let suite = "TrackingThemeTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        let original = settings.trackedApplications
        #expect(settings.activeThemeID == nil)
        #expect(settings.progressStyle == .matrix)
        settings.createTheme()
        let first = settings.activeThemeID
        #expect(settings.activeTheme?.accent == .blue)
        settings.themes[0].name = "Writing"
        settings.themes[0].accent = .purple
        settings.addRecognizedApplication(identifier: "app.test", name: "Test")
        settings.addRecognizedApplication(identifier: "app.test", name: "Test")
        settings.addWebsiteRuleAtBeginning()
        settings.ruleWebsites[0].pathContains = "test.example"
        #expect(Set(settings.ruleApplications.map(\.id)).count == 1)
        settings.createTheme()
        #expect(settings.ruleApplications.isEmpty)
        settings.addRecognizedApplication(identifier: "app.test", name: "Test")
        settings.activeThemeID = first
        settings.progressStyle = .waveform
        let restored = TrackingSettings(defaults: defaults)
        #expect(restored.ruleApplications.count == 1)
        #expect(restored.ruleWebsites[0].pathContains == "test.example")
        #expect(restored.activeTheme?.name == "Writing")
        #expect(restored.activeTheme?.accent == .purple)
        #expect(restored.progressStyle == .waveform)
        #expect(restored.trackedApplications == original)
        restored.themes.removeAll()
        #expect(restored.ruleApplications.isEmpty)
        #expect(restored.ruleWebsites.isEmpty)
        restored.activeThemeID = nil
        #expect(restored.ruleApplications == original)
    }
}
