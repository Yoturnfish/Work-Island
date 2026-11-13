import AppKit
import Foundation
import SwiftUI
import Testing
@testable import FocusIsland

@MainActor
struct TrackingEngineTests {
    private let targetID = "com.example.creator"

    // Run independently: creating 56 native hosts competes with the suite's 10–40 ms timing checks.
    @Test(.enabled(if: ProcessInfo.processInfo.environment["WORK_ISLAND_UI_SMOKE"] == "1"))
    func allDashboardPagesLayoutAtEverySupportedScaleAndBackground() async throws {
        let fixture = makeFixture()
        let day = HistoryDay(dayKey: "2026-08-22", totalSeconds: 600,
                             applications: [.init(bundleIdentifier: targetID, applicationName: "Creator", seconds: 600)])
        for background in IslandBackgroundStyle.allCases {
            fixture.engine.settings.backgroundStyle = background
            for scale in [0.5, 0.65, 0.82, 1.0] {
                fixture.engine.settings.islandScale = scale
                let size = FocusDesign.Window.scaledSize(for: scale)
                for page in IslandNavigationPage.allCases {
                    let presentation = IslandPresentationModel()
                    presentation.showNavigation()
                    if page == .historyDetail { presentation.openHistoryDetail(day) }
                    else { presentation.open(page) }
                    let host = NSHostingView(rootView: IslandRootView(
                        engine: fixture.engine, presentation: presentation, onHoverChanged: { _ in }
                    ).transaction { $0.disablesAnimations = true })
                    host.sizingOptions = []
                    let window = NSWindow(contentRect: NSRect(origin: .zero, size: size),
                                          styleMask: .borderless, backing: .buffered, defer: false)
                    window.isReleasedWhenClosed = false
                    window.contentView = host
                    host.layoutSubtreeIfNeeded()
                    // Give other main-actor tests and deferred SwiftUI layout work a fair turn.
                    try await Task.sleep(for: .milliseconds(5))
                    host.layoutSubtreeIfNeeded()
                    // sizingOptions=[] deliberately disables the intrinsic fitting size;
                    // the native viewport is the actual boundary used by our panel.
                    // NSWindow rounds fractional logical-point content dimensions.
                    #expect(abs(host.bounds.width - size.width) <= 1.001)
                    #expect(abs(host.bounds.height - size.height) <= 1.001)
                    #expect(!host.subviews.isEmpty)
                    window.close()
                }
            }
        }
    }

    @Test func editingTodayFromHistorySettlesActiveWorkAndContinuesFromSaveBoundary() throws {
        let fixture = makeFixture()
        let now = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: now.addingTimeInterval(-20))
        fixture.engine.process(snapshot: activeSnapshot(), at: now)
        #expect(fixture.engine.todaySeconds == 20)
        try fixture.engine.setDayTotal(dayKey: "2026-08-22", bundleIdentifier: targetID, applicationName: "Creator", seconds: 120)
        #expect(fixture.engine.todaySeconds == 120)
        #expect(fixture.store.items.filter { !$0.isAggregateCorrection }.map(\.effectiveSeconds) == [20])
        #expect(fixture.engine.state == .tracking(applicationName: "Creator"))
        fixture.engine.process(snapshot: activeSnapshot(), at: now.addingTimeInterval(5))
        #expect(fixture.engine.todaySeconds == 125)
    }

    @Test func historySaveIncludesTypingTimeOnlyOnceAndSupportsZeroAndRepeatedEdits() throws {
        let start = date(2026, 8, 22, 9, 0, 0)
        let clock = MutableClock(value: start)
        let fixture = makeFixture(clock: clock)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(20))
        // The editor is open, and the save occurs 0.7 seconds after the last timer tick.
        clock.now = start.addingTimeInterval(20.7)
        try fixture.engine.setDayTotal(dayKey: "2026-08-22", bundleIdentifier: targetID, applicationName: "Creator", seconds: 120)
        #expect(abs(fixture.engine.todaySeconds - 120) < 0.0001)
        let automatic = try #require(fixture.store.items.first)
        #expect(abs(automatic.effectiveSeconds - 20.7) < 0.0001)
        clock.now = start.addingTimeInterval(25.7)
        fixture.engine.process(snapshot: activeSnapshot(), at: clock.now)
        #expect(abs(fixture.engine.todaySeconds - 125) < 0.0001)
        try fixture.engine.setDayTotal(dayKey: "2026-08-22", bundleIdentifier: targetID, applicationName: "Creator", seconds: 0)
        #expect(abs(fixture.engine.todaySeconds) < 0.0001)
        #expect(fixture.store.items.filter(\.isAggregateCorrection).count == 1)
        clock.now = start.addingTimeInterval(30.7)
        fixture.engine.process(snapshot: activeSnapshot(), at: clock.now)
        #expect(abs(fixture.engine.todaySeconds - 5) < 0.0001)
        try fixture.engine.setDayTotal(dayKey: "2026-08-22", bundleIdentifier: targetID, applicationName: "Creator", seconds: 60)
        #expect(abs(fixture.engine.todaySeconds - 60) < 0.0001)
        #expect(fixture.store.items.filter(\.isAggregateCorrection).count == 1)
    }

    @Test func editingEarlierDayDoesNotInterruptTodaysActiveSession() throws {
        let start = date(2026, 8, 22, 9, 0, 0)
        let clock = MutableClock(value: start)
        let fixture = makeFixture(clock: clock)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        clock.now = start.addingTimeInterval(20)
        fixture.engine.process(snapshot: activeSnapshot(), at: clock.now)
        try fixture.engine.setDayTotal(dayKey: "2026-08-21", bundleIdentifier: targetID, applicationName: "Creator", seconds: 120)
        #expect(fixture.engine.todaySeconds == 20)
        let onlyCorrections = fixture.store.items.allSatisfy { $0.isAggregateCorrection }
        #expect(onlyCorrections)
        clock.now = start.addingTimeInterval(25)
        fixture.engine.process(snapshot: activeSnapshot(), at: clock.now)
        #expect(fixture.engine.todaySeconds == 25)
    }

    @Test func historySaveNearMinuteBoundaryDoesNotDiscardFractionalCorrection() throws {
        let start = date(2026, 8, 22, 9, 0, 0)
        let clock = MutableClock(value: start)
        let fixture = makeFixture(clock: clock)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        clock.now = start.addingTimeInterval(59.75)
        try fixture.engine.setDayTotal(dayKey: "2026-08-22", bundleIdentifier: targetID, applicationName: "Creator", seconds: 60)
        #expect(fixture.engine.todaySeconds == 60)
        #expect(fixture.store.items.first(where: \.isAggregateCorrection)?.effectiveSeconds == 0.25)
        clock.now = start.addingTimeInterval(60.75)
        fixture.engine.process(snapshot: activeSnapshot(), at: clock.now)
        #expect(fixture.engine.todaySeconds == 61)
    }

    @Test func historySaveAtDayBoundaryKeepsNewDayWorkSeparate() throws {
        let start = date(2026, 8, 21, 23, 59, 50)
        let clock = MutableClock(value: start)
        let fixture = makeFixture(clock: clock)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        clock.now = date(2026, 8, 22, 0, 0, 5)
        try fixture.engine.setDayTotal(dayKey: "2026-08-21", bundleIdentifier: targetID, applicationName: "Creator", seconds: 120)
        #expect(fixture.engine.todaySeconds == 5)
        #expect(fixture.store.items.filter { $0.dayKey == "2026-08-21" }.reduce(0) { $0 + $1.effectiveSeconds } == 120)
        clock.now = date(2026, 8, 22, 0, 0, 10)
        fixture.engine.process(snapshot: activeSnapshot(), at: clock.now)
        #expect(fixture.engine.todaySeconds == 10)
    }

    @Test func invalidDayTotalsFailBeforeWritingOrInterruptingTracking() throws {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(-20))
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        for seconds in [Double.nan, .infinity, -.infinity, Double(Int.max)] {
            #expect(throws: HistoryDurationInput.InputError.self) {
                try fixture.engine.setDayTotal(dayKey: "2026-08-22", bundleIdentifier: targetID, applicationName: "Creator", seconds: seconds)
            }
            #expect(fixture.store.items.isEmpty)
            #expect(fixture.engine.todaySeconds == 20)
        }
    }

    @Test func reminderTogglePreviewsFlashSelectionButNeverOffSelection() {
        let fixture = makeFixture()
        let now = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: .init(bundleIdentifier: nil, applicationName: nil, idleSeconds: 0), at: now.addingTimeInterval(-300))
        fixture.engine.settings.superReminderEnabled = true
        #expect(fixture.engine.superReminderStartedAt == now)
        #expect(fixture.engine.reminderPreviewStartedAt == now)
        fixture.engine.settings.superReminderEnabled = false
        #expect(fixture.engine.reminderPreviewStartedAt == nil)
        #expect(fixture.engine.superReminderStartedAt == now)
        #expect(fixture.engine.awayStartedAt == now.addingTimeInterval(-300))
        fixture.engine.setReminderOnboardingActive(true)
        fixture.engine.settings.superReminderEnabled = true
        #expect(fixture.engine.reminderPreviewStartedAt == nil)
        #expect(fixture.engine.superReminderStartedAt == nil)
    }

    @Test func websiteKeywordStatisticsMergeAndRemainEditable() throws {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 8, 0, 0)
        for (name, seconds) in [(" Bilibili ", 120.0), ("bilibili", 180.0)] {
            try fixture.store.insert(WorkSession(bundleIdentifier: "web.\(UUID().uuidString)", applicationName: name, startedAt: start, endedAt: start.addingTimeInterval(seconds), effectiveSeconds: seconds, dayKey: "2026-08-22"))
        }
        fixture.engine.reloadPersistedSessions()
        #expect(fixture.engine.appUsage.count == 1)
        #expect(fixture.engine.appUsage.first?.seconds == 300)
        #expect(fixture.engine.historyDays.first?.applications.count == 1)
        let id = TrackingEngine.websiteStatisticsID(keyword: "bilibili")
        try fixture.engine.setDayTotal(dayKey: "2026-08-22", bundleIdentifier: id, applicationName: "bilibili", seconds: 200)
        #expect(fixture.engine.historyDays.first?.totalSeconds == 200)
        try fixture.engine.setDayTotal(dayKey: "2026-08-22", bundleIdentifier: id, applicationName: "bilibili", seconds: 400)
        #expect(fixture.engine.historyDays.first?.totalSeconds == 400)
        #expect(fixture.engine.historyDays.first?.applications.count == 1)
        #expect(TrackingEngine.websiteStatisticsID(keyword: "bilibili") != TrackingEngine.websiteStatisticsID(keyword: "bilibili/video"))
    }

    @Test func onboardingDoesNotAccumulateReminderTime() {
        let fixture = makeFixture()
        let away = ActivitySnapshot(bundleIdentifier: nil, applicationName: nil, idleSeconds: 0)
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.setReminderOnboardingActive(true)
        for seconds in [0.0, 30, 300, 1800] {
            fixture.engine.process(snapshot: away, at: start.addingTimeInterval(seconds))
            #expect(fixture.engine.awayStartedAt == nil)
        }
        fixture.engine.setReminderOnboardingActive(false)
        #expect(fixture.engine.awayStartedAt != nil)
        fixture.engine.setReminderOnboardingActive(true)
        #expect(fixture.engine.awayStartedAt == nil)
    }

    @Test func awayReminderClockResetsOnWorkPauseAndSleep() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        let away = ActivitySnapshot(bundleIdentifier: nil, applicationName: nil, idleSeconds: 0)
        fixture.engine.process(snapshot: away, at: start)
        #expect(fixture.engine.awayStartedAt == start)
        fixture.engine.process(snapshot: away, at: start.addingTimeInterval(10))
        #expect(fixture.engine.awayStartedAt == start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(11))
        #expect(fixture.engine.awayStartedAt == nil)
        fixture.engine.process(snapshot: away, at: start.addingTimeInterval(12))
        #expect(fixture.engine.awayStartedAt == start.addingTimeInterval(12))
        fixture.engine.isManuallyPaused = true
        fixture.engine.process(snapshot: away, at: start.addingTimeInterval(13))
        #expect(fixture.engine.awayStartedAt == nil)
        fixture.engine.isManuallyPaused = false
        fixture.engine.process(snapshot: away, at: start.addingTimeInterval(14))
        fixture.engine.prepareForSleep(at: start.addingTimeInterval(15))
        #expect(fixture.engine.awayStartedAt == nil)
    }

    @Test func awayTimerMeasuresOnlyTheCurrentSessionAndNeverAddsToWork() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        let away = ActivitySnapshot(bundleIdentifier: nil, applicationName: nil, idleSeconds: 0)
        fixture.engine.settings.awayTimerEnabled = true

        fixture.engine.process(snapshot: away, at: start)
        #expect(fixture.engine.isShowingAwayTimer)
        #expect(fixture.engine.currentAwaySeconds(at: start.addingTimeInterval(17)) == 17)
        #expect(fixture.engine.displayedTimerStyle == .minutesSeconds)
        #expect(fixture.engine.todaySeconds == 0)
        #expect(fixture.store.items.isEmpty)

        fixture.engine.isManuallyPaused = true
        fixture.engine.process(snapshot: away, at: start.addingTimeInterval(20))
        #expect(!fixture.engine.isShowingAwayTimer)
        #expect(fixture.engine.currentAwaySeconds(at: start.addingTimeInterval(25)) == 0)

        fixture.engine.isManuallyPaused = false
        fixture.engine.process(snapshot: away, at: start.addingTimeInterval(30))
        #expect(fixture.engine.currentAwaySeconds(at: start.addingTimeInterval(35)) == 5)
        #expect(fixture.engine.todaySeconds == 0)
        #expect(fixture.store.items.isEmpty)
    }

    @Test func themesRestrictBothAppsAndWebsitesAndRestoreDefaultRules() {
        let fixture = makeFixture()
        let settings = fixture.engine.settings
        let start = date(2026, 8, 22, 9, 0, 0)
        settings.trackedWebsites = [.init(displayName: "Default web", pathContains: "default.test")]
        settings.createTheme()
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        #expect(fixture.engine.state == .away)
        settings.addRecognizedApplication(identifier: "com.theme.app", name: "Theme app")
        settings.addRecognizedApplication(identifier: "com.theme.app", name: "Theme app")
        fixture.engine.process(snapshot: .init(bundleIdentifier: "com.theme.app", applicationName: "Theme app", idleSeconds: 0), at: start.addingTimeInterval(1))
        #expect(fixture.engine.state == .tracking(applicationName: "Theme app"))
        fixture.engine.process(snapshot: .init(bundleIdentifier: "com.theme.app", applicationName: "Theme app", idleSeconds: 0), at: start.addingTimeInterval(11))
        #expect(fixture.engine.todaySeconds == 10)
        settings.ruleWebsites = [.init(displayName: "Theme web", pathContains: "theme.test")]
        fixture.engine.process(snapshot: .init(bundleIdentifier: "com.apple.Safari", applicationName: "Safari", idleSeconds: 0, browserURL: "https://default.test"), at: start.addingTimeInterval(12))
        #expect(fixture.engine.state == .away)
        fixture.engine.process(snapshot: .init(bundleIdentifier: "com.apple.Safari", applicationName: "Safari", idleSeconds: 0, browserURL: "https://theme.test"), at: start.addingTimeInterval(13))
        #expect(fixture.engine.state == .tracking(applicationName: "Theme web"))
        settings.createTheme()
        fixture.engine.process(snapshot: .init(bundleIdentifier: "com.apple.Safari", applicationName: "Safari", idleSeconds: 0, browserURL: "https://theme.test"), at: start.addingTimeInterval(14))
        #expect(fixture.engine.state == .away)
        settings.activeThemeID = nil
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(15))
        #expect(fixture.engine.state == .tracking(applicationName: "Creator"))
    }

    @Test func tracksOnlyEnabledFrontmostApplication() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)

        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(10))

        #expect(fixture.engine.state == .tracking(applicationName: "Creator"))
        #expect(fixture.engine.todaySeconds == 10)
        #expect(fixture.engine.appUsage.first?.bundleIdentifier == targetID)
    }

    @Test func nonTargetApplicationImmediatelyEntersAwayState() {
        let fixture = makeFixture()
        fixture.engine.process(
            snapshot: .init(bundleIdentifier: "com.apple.finder", applicationName: "Finder", idleSeconds: 0),
            at: date(2026, 8, 22, 9, 0, 0)
        )

        #expect(fixture.engine.state == .away)
        #expect(fixture.engine.isAway)
        #expect(fixture.engine.todaySeconds == 0)
    }

    @Test func idleAtAwayThresholdStopsAndUserActivityResumes() {
        let fixture = makeFixture()
        fixture.engine.settings.awayThreshold = 30
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(10))
        fixture.engine.process(snapshot: activeSnapshot(idle: 30), at: start.addingTimeInterval(11))

        #expect(fixture.engine.state == .away)
        #expect(fixture.store.items.count == 1)
        #expect(fixture.store.items[0].effectiveSeconds == 10)

        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(20))
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(25))
        #expect(fixture.engine.todaySeconds == 15)
    }

    @Test func manualPauseHasPriorityUntilExplicitResume() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(5))
        fixture.engine.toggleManualPause()
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(15))

        #expect(fixture.engine.state == .paused)
        #expect(!fixture.engine.isAway)
        #expect(fixture.engine.todaySeconds == 5)

        fixture.engine.toggleManualPause()
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(20))
        #expect(fixture.engine.state == .tracking(applicationName: "Creator"))
    }

    @Test func appSwitchFinalizesPreviousSession() {
        let secondID = "com.example.second"
        let fixture = makeFixture(extraApplications: [
            .init(bundleIdentifier: secondID, displayName: "Second")
        ])
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(
            snapshot: .init(bundleIdentifier: secondID, applicationName: "Second", idleSeconds: 0),
            at: start.addingTimeInterval(8)
        )

        #expect(fixture.store.items.count == 1)
        #expect(fixture.store.items[0].bundleIdentifier == targetID)
        #expect(fixture.store.items[0].effectiveSeconds == 8)
    }

    @Test func activeSessionCheckpointsEveryMinute() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)

        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(61))

        #expect(fixture.store.items.count == 1)
        #expect(fixture.store.items[0].effectiveSeconds == 60)
        #expect(fixture.engine.todaySeconds == 61)
    }

    @Test func storageFailureKeepsActiveDurationVisible() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(10))
        fixture.store.error = TestStoreError.unavailable

        fixture.engine.process(snapshot: activeSnapshot(idle: 30), at: start.addingTimeInterval(11))

        #expect(fixture.engine.persistenceError != nil)
        #expect(fixture.engine.todaySeconds == 10)
        #expect(fixture.store.items.isEmpty)
    }

    @Test func crossingMidnightSplitsTheSession() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 23, 59, 55)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: date(2026, 8, 23, 0, 0, 1))

        #expect(fixture.store.items.count == 1)
        #expect(fixture.store.items[0].dayKey == "2026-08-22")
        #expect(fixture.store.items[0].effectiveSeconds == 5)
        #expect(fixture.engine.todaySeconds == 1)
    }

    @Test func customResetTimeSplitsAtThreeAM() {
        let fixture = makeFixture()
        fixture.engine.settings.dayResetMinutes = 180
        let start = date(2026, 8, 23, 2, 59, 55)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: date(2026, 8, 23, 3, 0, 1))

        #expect(fixture.store.items.count == 1)
        #expect(fixture.store.items[0].dayKey == "2026-08-22")
        #expect(fixture.store.items[0].effectiveSeconds == 5)
        #expect(fixture.engine.todaySeconds == 1)
    }

    @Test func unchangedDayResetNeverReprocessesStoredHistory() {
        let fixture = makeFixture()
        let initialFetchCount = fixture.store.historyFetchCount

        fixture.engine.settings.dayResetMinutes = 0
        #expect(fixture.store.historyFetchCount == initialFetchCount)

        fixture.engine.settings.dayResetMinutes = 180
        #expect(fixture.store.historyFetchCount > initialFetchCount)
        let changedFetchCount = fixture.store.historyFetchCount

        fixture.engine.settings.dayResetMinutes = 180
        #expect(fixture.store.historyFetchCount == changedFetchCount)
    }

    @Test func historyUsesDateWithoutCategoryEvaluation() throws {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(120))
        fixture.engine.process(snapshot: .init(bundleIdentifier: "com.apple.finder", applicationName: "Finder", idleSeconds: 0), at: start.addingTimeInterval(121))

        #expect(fixture.engine.historyDays.first?.dayKey == "2026-08-22")
        #expect(fixture.engine.historyDays.first?.totalSeconds == 120)
    }

    @Test func sevenDayHistoryIncludesDatesWithoutSessions() {
        let fixture = makeFixture()
        let recordedDay = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: recordedDay)
        fixture.engine.process(snapshot: activeSnapshot(), at: recordedDay.addingTimeInterval(120))
        fixture.engine.process(
            snapshot: .init(bundleIdentifier: "com.apple.finder", applicationName: "Finder", idleSeconds: 0),
            at: recordedDay.addingTimeInterval(121)
        )

        let days = fixture.engine.recentHistoryDays(count: 7, at: date(2026, 8, 24, 12, 0, 0))
        #expect(days.map(\.dayKey) == ["2026-08-24", "2026-08-23", "2026-08-22", "2026-08-21", "2026-08-20", "2026-08-19", "2026-08-18"])
        #expect(days.first?.totalSeconds == 0)
        #expect(days.first { $0.dayKey == "2026-08-22" }?.totalSeconds == 120)
    }

    @Test func recentRecordedHistoryIsLimitedToSevenEntries() {
        let fixture = makeFixture()
        for day in 1...9 {
            let start = date(2026, 8, day, 9, 0, 0)
            fixture.engine.process(snapshot: activeSnapshot(), at: start)
            fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(60))
            fixture.engine.process(
                snapshot: .init(bundleIdentifier: "com.apple.finder", applicationName: "Finder", idleSeconds: 0),
                at: start.addingTimeInterval(61)
            )
        }

        #expect(fixture.engine.recentRecordedHistoryDays().map(\.dayKey) == [
            "2026-08-09", "2026-08-08", "2026-08-07", "2026-08-06", "2026-08-05", "2026-08-04", "2026-08-03"
        ])
    }

    @Test func completeHistoryFillsDatesFromFirstRecordThroughToday() {
        let fixture = makeFixture()
        for day in [20, 22] {
            let start = date(2026, 8, day, 9, 0, 0)
            fixture.engine.process(snapshot: activeSnapshot(), at: start)
            fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(120))
            fixture.engine.process(
                snapshot: .init(bundleIdentifier: "com.apple.finder", applicationName: "Finder", idleSeconds: 0),
                at: start.addingTimeInterval(121)
            )
        }

        let days = fixture.engine.completeHistoryDays(at: date(2026, 8, 24, 12, 0, 0))
        #expect(days.map(\.dayKey) == ["2026-08-20", "2026-08-21", "2026-08-22", "2026-08-23", "2026-08-24"])
        #expect(days.first { $0.dayKey == "2026-08-21" }?.totalSeconds == 0)
        #expect(days.first { $0.dayKey == "2026-08-22" }?.totalSeconds == 120)
    }

    @Test func archivesDetailsOlderThanSevenLogicalDaysIntoDailySummary() throws {
        let fixture = makeFixture()
        let oldDay = "2026-08-15"
        let retainedDay = "2026-08-16"
        try fixture.store.insert(WorkSession(
            bundleIdentifier: targetID,
            applicationName: "Creator",
            startedAt: date(2026, 8, 15, 9, 0, 0),
            endedAt: date(2026, 8, 15, 9, 10, 0),
            effectiveSeconds: 600,
            dayKey: oldDay
        ))
        try fixture.store.insert(WorkSession(
            bundleIdentifier: targetID,
            applicationName: "Creator",
            startedAt: date(2026, 8, 15, 9, 10, 0),
            endedAt: date(2026, 8, 15, 9, 10, 0),
            effectiveSeconds: -120,
            dayKey: oldDay,
            isAggregateCorrection: true
        ))
        try fixture.store.insert(WorkSession(
            bundleIdentifier: targetID,
            applicationName: "Creator",
            startedAt: date(2026, 8, 16, 9, 0, 0),
            endedAt: date(2026, 8, 16, 9, 10, 0),
            effectiveSeconds: 600,
            dayKey: retainedDay
        ))

        fixture.engine.reloadPersistedSessions()

        #expect(fixture.store.items.map(\.dayKey) == [retainedDay])
        #expect(fixture.store.summaries.first?.dayKey == oldDay)
        #expect(fixture.store.summaries.first?.totalSeconds == 480)
        #expect(fixture.engine.historyDays.first { $0.dayKey == oldDay }?.isSummaryOnly == true)
        #expect(fixture.engine.historyDays.first { $0.dayKey == oldDay }?.applications.isEmpty == true)
        #expect(fixture.engine.historyDays.first { $0.dayKey == retainedDay }?.isSummaryOnly == false)
    }

    @Test func pointerInteractionInTrackedWindowStartsTrackingWithoutFocus() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        let snapshot = ActivitySnapshot(
            bundleIdentifier: "com.apple.finder",
            applicationName: "Finder",
            idleSeconds: 0,
            pointerApplicationBundleIdentifier: targetID,
            pointerApplicationName: "Creator"
        )

        fixture.engine.process(snapshot: snapshot, at: start)
        fixture.engine.process(snapshot: snapshot, at: start.addingTimeInterval(8))

        if case .tracking = fixture.engine.state {
            #expect(Bool(true))
        } else {
            Issue.record("指针在工作窗口中操作时应进入计时状态")
        }
        #expect(fixture.engine.todaySeconds == 8)
    }

    @Test func keyboardInputOverridesOlderPointerActivity() {
        let selection = SystemActivityDetector.selectInputSource(
            pointerIdle: 45,
            keyboardIdle: 0.2,
            systemIdle: 0.2
        )

        #expect(selection.source == .frontmost)
        #expect(selection.idleSeconds == 0.2)
    }

    @Test func recentPointerActivityStillSupportsSplitScreenTracking() {
        let selection = SystemActivityDetector.selectInputSource(
            pointerIdle: 0.1,
            keyboardIdle: 12,
            systemIdle: 0.1
        )

        #expect(selection.source == .pointer)
        #expect(selection.idleSeconds == 0.1)
    }

    @Test func pointerWindowLookupIsCoalescedUntilTheOneSecondEvaluation() {
        #expect(SystemActivityDetector.shouldResolvePointerWindow(
            needsRefresh: true,
            usesPointer: true
        ))
        #expect(!SystemActivityDetector.shouldResolvePointerWindow(
            needsRefresh: false,
            usesPointer: true
        ))
        #expect(!SystemActivityDetector.shouldResolvePointerWindow(
            needsRefresh: true,
            usesPointer: false
        ))
    }

    @Test func websitePathMatchTakesPriorityOverTrackedBrowser() {
        let browserID = "com.google.Chrome"
        let fixture = makeFixture(extraApplications: [.init(bundleIdentifier: browserID, displayName: "Chrome")])
        fixture.engine.settings.trackedWebsites = [
            .init(displayName: "Bilibili 工作区", pathContains: "bilibili.com/video")
        ]
        let start = date(2026, 8, 22, 9, 0, 0)
        let snapshot = ActivitySnapshot(
            bundleIdentifier: "com.apple.finder",
            applicationName: "Finder",
            idleSeconds: 0,
            pointerApplicationBundleIdentifier: browserID,
            pointerApplicationName: "Chrome",
            browserURL: "https://www.bilibili.com/video/BV1"
        )

        fixture.engine.process(snapshot: snapshot, at: start)
        fixture.engine.process(snapshot: snapshot, at: start.addingTimeInterval(5))

        #expect(fixture.engine.state == .tracking(applicationName: "Bilibili 工作区"))
        #expect(fixture.engine.appUsage.first?.applicationName == "Bilibili 工作区")
        #expect(fixture.engine.appUsage.first?.bundleIdentifier.hasPrefix("web.") == true)
    }

    @Test func safariUsesFirstEnabledMatchingWebsiteRule() {
        let fixture = makeFixture()
        fixture.engine.settings.trackedWebsites = [
            .init(displayName: "第一条", pathContains: "/workspace"),
            .init(displayName: "第二条", pathContains: "example.com")
        ]
        let snapshot = ActivitySnapshot(
            bundleIdentifier: "com.apple.Safari",
            applicationName: "Safari",
            idleSeconds: 0,
            browserURL: "https://example.com/workspace/project"
        )

        fixture.engine.process(snapshot: snapshot, at: date(2026, 8, 22, 9, 0, 0))
        #expect(fixture.engine.state == .tracking(applicationName: "第一条"))
    }

    @Test func staleBrowserURLIsIgnoredForNonBrowserWindow() {
        let fixture = makeFixture()
        fixture.engine.settings.trackedWebsites = [.init(displayName: "工作站点", pathContains: "work.example")]
        fixture.engine.process(
            snapshot: .init(
                bundleIdentifier: "com.apple.finder",
                applicationName: "Finder",
                idleSeconds: 0,
                browserURL: "https://work.example/project"
            ),
            at: date(2026, 8, 22, 9, 0, 0)
        )

        #expect(fixture.engine.state == .away)
        #expect(fixture.engine.todaySeconds == 0)
    }

    @Test func browserFallsBackToOrdinaryApplicationWhenURLIsUnavailable() {
        let browserID = "com.microsoft.edgemac"
        let fixture = makeFixture(extraApplications: [.init(bundleIdentifier: browserID, displayName: "Edge")])
        fixture.engine.settings.trackedWebsites = [.init(displayName: "工作站点", pathContains: "work.example")]
        fixture.engine.process(
            snapshot: .init(bundleIdentifier: browserID, applicationName: "Edge", idleSeconds: 0),
            at: date(2026, 8, 22, 9, 0, 0)
        )

        #expect(fixture.engine.state == .tracking(applicationName: "Edge"))
    }

    @Test func historyStoreIsNotFetchedOnEveryTick() {
        let fixture = makeFixture()
        let initialFetches = fixture.store.historyFetchCount
        let initialDayFetches = fixture.store.dayFetchCount
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(1))
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(2))
        #expect(fixture.store.historyFetchCount == initialFetches)
        #expect(fixture.store.dayFetchCount == initialDayFetches)
        #expect(fixture.engine.recentHistoryDays(count: 1, at: start.addingTimeInterval(2)).first?.totalSeconds == 2)
    }

    @Test func sleepFinalizesAndWakeStartsFreshSession() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.prepareForSleep(at: start.addingTimeInterval(10))
        #expect(fixture.store.items.count == 1)
        #expect(fixture.store.items[0].effectiveSeconds == 10)
        #expect(fixture.engine.state == .away)

        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(1_000))
        #expect(fixture.engine.todaySeconds == 10)
        fixture.engine.resumeAfterWake(at: start.addingTimeInterval(1_000), snapshot: activeSnapshot())
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(1_005))
        #expect(fixture.engine.todaySeconds == 15)
    }

    @Test func progressIsCappedAtOne() {
        let fixture = makeFixture(goal: 10)
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(20))
        #expect(fixture.engine.progress == 1)
    }

    @Test func aggregatesStoredAndActiveSessionsByApplication() throws {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        try fixture.store.insert(WorkSession(
            bundleIdentifier: targetID,
            applicationName: "Creator",
            startedAt: start.addingTimeInterval(-30),
            endedAt: start,
            effectiveSeconds: 30,
            dayKey: "2026-08-22"
        ))
        // Out-of-engine imports explicitly notify the engine after persisting;
        // production's legacy importer follows the same contract.
        fixture.engine.reloadPersistedSessions()
        fixture.engine.process(snapshot: activeSnapshot(), at: start)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(10))

        #expect(fixture.engine.todaySeconds == 40)
        #expect(fixture.engine.appUsage.count == 1)
    }

    @Test func awayStateRecoversWhenReturningToTrackedApplication() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        let other = ActivitySnapshot(bundleIdentifier: "com.apple.finder", applicationName: "Finder", idleSeconds: 0)
        fixture.engine.process(snapshot: other, at: start)
        #expect(fixture.engine.isAway)
        fixture.engine.process(snapshot: activeSnapshot(), at: start.addingTimeInterval(1))
        #expect(fixture.engine.state == .tracking(applicationName: "Creator"))
        #expect(!fixture.engine.isAway)
    }

    @Test func manualPauseTakesPriorityOverAwayState() {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        fixture.engine.isManuallyPaused = true
        fixture.engine.process(
            snapshot: .init(bundleIdentifier: "com.apple.finder", applicationName: "Finder", idleSeconds: 600),
            at: start
        )
        #expect(fixture.engine.state == .paused)
        #expect(!fixture.engine.isAway)
    }

    @Test func manuallyCreatesUpdatesAndDeletesSession() throws {
        let fixture = makeFixture()
        let start = date(2026, 8, 22, 9, 0, 0)
        let application = fixture.engine.settings.trackedApplications[0]
        try fixture.engine.createManualSession(
            application: application,
            startedAt: start,
            endedAt: start.addingTimeInterval(1_800)
        )
        #expect(fixture.store.items.count == 1)
        #expect(fixture.store.items[0].isManuallyCreated)
        #expect(fixture.engine.todaySeconds == 1_800)

        let session = fixture.store.items[0]
        try fixture.engine.updateSession(
            session,
            application: application,
            startedAt: start,
            endedAt: start.addingTimeInterval(3_600)
        )
        #expect(session.isManuallyEdited)
        #expect(fixture.engine.todaySeconds == 3_600)

        try fixture.engine.deleteSession(session)
        #expect(fixture.store.items.isEmpty)
        #expect(fixture.engine.todaySeconds == 0)
    }

    @Test func manualSessionRejectsInvalidAndCrossDayRanges() {
        let fixture = makeFixture()
        let application = fixture.engine.settings.trackedApplications[0]
        let start = date(2026, 8, 22, 23, 30, 0)
        #expect(throws: TrackingEngine.SessionEditError.invalidRange) {
            try fixture.engine.createManualSession(application: application, startedAt: start, endedAt: start)
        }
        #expect(throws: TrackingEngine.SessionEditError.crossesDayBoundary) {
            try fixture.engine.createManualSession(
                application: application,
                startedAt: start,
                endedAt: date(2026, 8, 23, 0, 30, 0)
            )
        }
    }

    @Test func settingAppTotalPreservesAutomaticSessionsAndReplacesCorrection() throws {
        let fixture = makeFixture()
        let application = fixture.engine.settings.trackedApplications[0]
        let start = date(2026, 8, 22, 8, 0, 0)
        try fixture.store.insert(WorkSession(
            bundleIdentifier: application.bundleIdentifier,
            applicationName: application.displayName,
            startedAt: start,
            endedAt: start.addingTimeInterval(1_800),
            effectiveSeconds: 1_800,
            dayKey: "2026-08-22"
        ))

        try fixture.engine.setTodayTotal(for: application, seconds: 3_600)
        #expect(fixture.engine.todaySeconds == 3_600)
        #expect(fixture.store.items.filter(\.isAggregateCorrection).count == 1)
        #expect(fixture.store.items.first { $0.isAggregateCorrection }?.effectiveSeconds == 1_800)

        try fixture.engine.setTodayTotal(for: application, seconds: 900)
        #expect(fixture.engine.todaySeconds == 900)
        #expect(fixture.store.items.filter(\.isAggregateCorrection).count == 1)
        #expect(fixture.store.items.first { $0.isAggregateCorrection }?.effectiveSeconds == -900)

        try fixture.engine.setTodayTotal(for: application, seconds: 0)
        #expect(fixture.engine.todaySeconds == 0)
        #expect(fixture.store.items.filter(\.isAggregateCorrection).count == 1)
        #expect(fixture.store.items.contains { !$0.isAggregateCorrection && $0.effectiveSeconds == 1_800 })
    }

    private func makeFixture(
        extraApplications: [TrackedApplication] = [],
        goal: TimeInterval = 4 * 60 * 60,
        clock: (any ClockProviding)? = nil
    ) -> (engine: TrackingEngine, store: MemorySessionStore) {
        let suite = "FocusIslandTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defaults.removePersistentDomain(forName: suite)
        let settings = TrackingSettings(defaults: defaults)
        settings.goalSeconds = goal
        settings.trackedApplications = [
            .init(bundleIdentifier: targetID, displayName: "Creator", category: .programming)
        ] + extraApplications
        let store = MemorySessionStore()
        let fixtureClock = clock ?? FixedClock(value: date(2026, 8, 22, 9, 0, 0))
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        let engine = TrackingEngine(
            detector: FixedDetector(value: activeSnapshot()),
            store: store,
            clock: fixtureClock,
            settings: settings,
            calendar: calendar
        )
        return (engine, store)
    }

    private func activeSnapshot(idle: TimeInterval = 0) -> ActivitySnapshot {
        .init(bundleIdentifier: targetID, applicationName: "Creator", idleSeconds: idle)
    }

    private func date(_ year: Int, _ month: Int, _ day: Int, _ hour: Int, _ minute: Int, _ second: Int) -> Date {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        return calendar.date(from: DateComponents(
            year: year, month: month, day: day, hour: hour, minute: minute, second: second
        ))!
    }
}

private struct FixedDetector: ActivityDetecting {
    let value: ActivitySnapshot
    func snapshot() -> ActivitySnapshot { value }
}

private struct FixedClock: ClockProviding {
    let value: Date
    var now: Date { value }
}

@MainActor
private final class MutableClock: ClockProviding {
    var now: Date
    init(value: Date) { now = value }
}

@MainActor
private final class MemorySessionStore: SessionStoring {
    var items: [WorkSession] = []
    var summaries: [DailySummary] = []
    var historyFetchCount = 0
    var dayFetchCount = 0
    var error: Error?

    func insert(_ session: WorkSession) throws {
        if let error { throw error }
        items.append(session)
    }

    func sessions(dayKey: String) throws -> [WorkSession] {
        dayFetchCount += 1
        return items.filter { $0.dayKey == dayKey }
    }

    func sessions(startingWith dayKey: String) throws -> [WorkSession] {
        historyFetchCount += 1
        return items.filter { $0.dayKey >= dayKey }
    }

    func oldestSessionDayKey(before dayKey: String) throws -> String? {
        if let error { throw error }
        return items.filter { $0.dayKey < dayKey }.map(\.dayKey).min()
    }

    func session(sourceID: String) throws -> WorkSession? {
        if let error { throw error }
        return items.first { $0.sourceID == sourceID }
    }

    func update(_ session: WorkSession) throws {
        if let error { throw error }
    }

    func delete(_ session: WorkSession) throws {
        if let error { throw error }
        items.removeAll { $0.id == session.id }
    }

    func archive(_ sessions: [WorkSession], dayKey: String, totalSeconds: TimeInterval) throws {
        if let error { throw error }
        if let summary = summaries.first(where: { $0.dayKey == dayKey }) {
            summary.totalSeconds = totalSeconds
        } else {
            summaries.append(DailySummary(dayKey: dayKey, totalSeconds: totalSeconds))
        }
        let ids = Set(sessions.map(\.id))
        items.removeAll { ids.contains($0.id) }
    }

    func allDailySummaries() throws -> [DailySummary] {
        if let error { throw error }
        historyFetchCount += 1
        return summaries
    }
}

private enum TestStoreError: LocalizedError {
    case unavailable

    var errorDescription: String? { "测试存储不可用" }
}
