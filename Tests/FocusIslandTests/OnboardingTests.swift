import Foundation
import Testing
@testable import FocusIsland

@MainActor
struct OnboardingTests {
    @Test func tutorialAdvancesWithoutAnimationAndGoalsUseSharedChoices() {
        let state = OnboardingState()
        state.step = .tutorial
        state.next()
        #expect(state.step == .complete)
        #expect(TrackingSettings.goalOptions == [3600, 7200, 10800, 14400, 21600, 28800])
        #expect(TrackingSettings.goalOptions.contains(4 * 3600))
        #expect(Set(TrackingSettings.goalOptions).count == TrackingSettings.goalOptions.count)
    }
    @Test func customTitlePersistsAcrossLanguageChangesAndCanBeReset() {
        let suite = "OnboardingTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        defaults.set(AppLanguage.english.rawValue, forKey: AppLanguage.defaultsKey)
        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.displayTitle == L10n.text("屏幕工作时间", language: .english))
        settings.saveTitle("  My creative time  ")
        settings.language = .chinese
        #expect(settings.displayTitle == "My creative time")
        #expect(TrackingSettings(defaults: defaults).displayTitle == "My creative time")
        settings.saveTitle("  \n ")
        #expect(settings.displayTitle == "屏幕工作时间")
        settings.saveTitle(String(repeating: "a", count: 80))
        #expect(settings.customTitle.count == 60)
    }
    @Test func firstRunRemainsPendingUntilFinished() {
        let suite = "OnboardingTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let first = TrackingSettings(defaults: defaults)
        #expect(!first.hasCompletedOnboarding)
        #expect(first.language == AppLanguage.preferredSystemLanguage())
        first.language = .chinese
        let interrupted = TrackingSettings(defaults: defaults)
        #expect(!interrupted.hasCompletedOnboarding)
        #expect(interrupted.language == .chinese)
        interrupted.hasCompletedOnboarding = true
        #expect(TrackingSettings(defaults: defaults).hasCompletedOnboarding)
    }

    @Test func existingUsersKeepTheirLanguageAndSkipAutomaticOnboarding() {
        let suite = "OnboardingTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        defaults.set(30, forKey: "awayThreshold")
        defaults.set(AppLanguage.chinese.rawValue, forKey: AppLanguage.defaultsKey)
        let settings = TrackingSettings(defaults: defaults)
        #expect(settings.hasCompletedOnboarding)
        #expect(settings.language == .chinese)
        let state = OnboardingState()
        state.next()
        #expect(state.step == .goal)
        state.next()
        #expect(state.step == .recognition)
        state.next()
        #expect(state.step == .tutorial)
        // Replaying the guide does not reset the saved completion flag.
        #expect(TrackingSettings(defaults: defaults).hasCompletedOnboarding)
    }
}
