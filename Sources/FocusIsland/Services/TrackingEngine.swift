import Combine
import Foundation
import WorkIslandCore

@MainActor
final class TrackingEngine: ObservableObject {
    private static let supportedBrowserIdentifiers = Set(BrowserAutomationService.browsers.map(\.id))

    enum SessionEditError: LocalizedError {
        case invalidRange
        case crossesDayBoundary
        case historyArchived

        var errorDescription: String? {
            switch self {
            case .invalidRange: L10n.text("结束时间必须晚于开始时间。")
            case .crossesDayBoundary: L10n.text("一次记录不能跨越两个统计日。")
            case .historyArchived: L10n.text("超过七天的记录已归档为日总时长，不能再修改。")
            }
        }
    }
    enum State: Equatable {
        case tracking(applicationName: String)
        case away
        case paused
    }

    private struct ActiveSession {
        let bundleIdentifier: String
        let applicationName: String
        let startedAt: Date
        let dayKey: String
        let category: WorkCategory
    }

    /// Plain values used by the one-second aggregate update. Keeping SwiftData
    /// model objects out of this hot path avoids repeatedly faulting managed
    /// properties while the timer and progress animation are running.
    private struct CachedUsageTotal {
        let applicationName: String
        var seconds: TimeInterval
    }

    @Published private(set) var state: State = .away
    @Published private(set) var awayStartedAt: Date?
    @Published private(set) var reminderPreviewStartedAt: Date?
    @Published private var reminderResetAt: Date?
    private var reminderPreviewTask: Task<Void, Never>?
    var superReminderStartedAt: Date? {
        guard let awayStartedAt else { return nil }
        return max(awayStartedAt, reminderResetAt ?? awayStartedAt)
    }

    private func resetReminder(preview: Bool) {
        reminderPreviewTask?.cancel()
        reminderPreviewStartedAt = nil
        let now = clock.now
        reminderResetAt = now
        guard preview, !reminderSuspendedForOnboarding else { return }
        reminderPreviewStartedAt = now
        reminderPreviewTask = Task { @MainActor [weak self] in
            do { try await Task.sleep(for: .seconds(1.5)) } catch { return }
            self?.reminderPreviewStartedAt = nil
        }
    }
    private var reminderSuspendedForOnboarding = false

    func setReminderOnboardingActive(_ active: Bool) {
        reminderPreviewTask?.cancel()
        reminderPreviewStartedAt = nil
        reminderResetAt = nil
        reminderSuspendedForOnboarding = active
        awayStartedAt = !active && isAway ? clock.now : nil
    }
    @Published private(set) var todaySeconds: TimeInterval = 0
    @Published private(set) var appUsage: [AppUsage] = []
    @Published private(set) var currentApplicationName: String?
    private(set) var todaySessions: [WorkSession] = []
    @Published private(set) var historyDays: [HistoryDay] = []
    @Published private(set) var persistenceError: String?
    @Published var isManuallyPaused = false

    let settings: TrackingSettings

    private let detector: any ActivityDetecting
    private let store: any SessionStoring
    private let clock: any ClockProviding
    private let calendar: Calendar
    private var activeSession: ActiveSession?
    private var timer: Timer?
    private var lastEvaluation: Date?
    private var cancellables: Set<AnyCancellable> = []
    private var cachedDetailSessions: [WorkSession] = []
    private var cachedDailySummaries: [DailySummary] = []
    private var cachedTodayDayKey: String?
    private var cachedTodaySessions: [WorkSession] = []
    private var cachedTodayUsage: [String: CachedUsageTotal] = [:]
    private var aggregateDayKey: String?
    private var historyNeedsReload = true
    private var isSuspended = false
    private let checkpointInterval: TimeInterval = 60
    private let detailRetentionDays = 7

    init(
        detector: any ActivityDetecting,
        store: any SessionStoring,
        clock: any ClockProviding,
        settings: TrackingSettings = TrackingSettings(),
        calendar: Calendar = .current
    ) {
        self.detector = detector
        self.store = store
        self.clock = clock
        self.settings = settings
        self.calendar = calendar
        do {
            try archiveExpiredSessions(at: clock.now)
        } catch {
            reportPersistenceError(error)
        }
        reloadToday(at: clock.now)
        settings.$superReminderEnabled.dropFirst().removeDuplicates().sink { [weak self] enabled in
            // Selecting Flash previews one complete pulse. Selecting Off only
            // resets the schedule and immediately removes any active preview.
            self?.resetReminder(preview: enabled)
        }.store(in: &cancellables)
        // Deduplicate against the publisher's initial value before dropping it;
        // `dropFirst().removeDuplicates()` would still let the first redundant
        // assignment through because the deduplicator had no baseline value.
        settings.$dayResetMinutes.removeDuplicates().dropFirst().sink { [weak self] minutes in
            self?.applyDayReset(minutes: minutes)
        }.store(in: &cancellables)
        settings.$trackedApplications.dropFirst().sink { [weak self] _ in
            self?.historyNeedsReload = true
            self?.rebuildHistoryFromCache(at: self?.clock.now ?? Date())
        }.store(in: &cancellables)
        Publishers.Merge3(settings.$activeThemeID.map { _ in () }, settings.$themes.map { _ in () }, settings.$trackedWebsites.map { _ in () })
            .dropFirst(3).sink { [weak self] _ in
                // Published values emit before storage changes; evaluate on the next main-actor turn.
                Task { @MainActor [weak self] in
                    guard let self else { return }
                    // Website statistics can be regrouped by a theme/rule change,
                    // even though the persisted sessions themselves did not change.
                    self.historyNeedsReload = true
                    guard self.timer != nil else { return }
                    self.evaluate()
                }
            }.store(in: &cancellables)
    }

    var progress: Double {
        guard settings.goalSeconds > 0 else { return 0 }
        return min(max(todaySeconds / settings.goalSeconds, 0), 1)
    }

    var isAway: Bool {
        if case .away = state { return true }
        return false
    }

    var isShowingAwayTimer: Bool {
        settings.awayTimerEnabled && isAway && awayStartedAt != nil
    }

    func currentAwaySeconds(at date: Date? = nil) -> TimeInterval {
        guard isAway, let awayStartedAt else { return 0 }
        return max(0, (date ?? clock.now).timeIntervalSince(awayStartedAt))
    }

    var displayedTimerSeconds: TimeInterval {
        isShowingAwayTimer ? currentAwaySeconds() : todaySeconds
    }

    var displayedTimerStyle: DurationDisplayStyle {
        isShowingAwayTimer ? .minutesSeconds : settings.durationDisplayStyle
    }

    var isAwayBreathing: Bool {
        guard let started = awayBreathingStartedAt else { return false }
        return clock.now >= started
    }

    var awayBreathingStartedAt: Date? {
        guard isAway, let started = awayStartedAt else { return nil }
        return started.addingTimeInterval(10)
    }

    func recentHistoryDays(count: Int = 7, at date: Date? = nil) -> [HistoryDay] {
        guard count > 0 else { return [] }
        let now = date ?? clock.now
        let shiftedNow = calendar.date(byAdding: .minute, value: -settings.dayResetMinutes, to: now) ?? now
        let logicalToday = calendar.startOfDay(for: shiftedNow)
        let recorded = Dictionary(uniqueKeysWithValues: historySnapshotWithLiveToday().map { ($0.dayKey, $0) })

        return (0..<count).compactMap { offset in
            guard let day = calendar.date(byAdding: .day, value: -offset, to: logicalToday) else { return nil }
            let components = calendar.dateComponents([.year, .month, .day], from: day)
            let key = String(format: "%04d-%02d-%02d", components.year ?? 0, components.month ?? 0, components.day ?? 0)
            return recorded[key] ?? HistoryDay(dayKey: key, totalSeconds: 0, applications: [])
        }
    }

    func recentRecordedHistoryDays(count: Int = 7) -> [HistoryDay] {
        guard count > 0 else { return [] }
        return Array(historySnapshotWithLiveToday().prefix(count))
    }

    func completeHistoryDays(at date: Date? = nil) -> [HistoryDay] {
        let snapshot = historySnapshotWithLiveToday()
        guard let earliestKey = snapshot.last?.dayKey,
              let earliestDay = Self.date(fromDayKey: earliestKey, calendar: calendar)
        else { return [] }

        let now = date ?? clock.now
        let latestKey = Self.dayKey(for: now, calendar: calendar, resetMinutes: settings.dayResetMinutes)
        guard let latestDay = Self.date(fromDayKey: latestKey, calendar: calendar), earliestDay <= latestDay else { return [] }
        let recorded = Dictionary(uniqueKeysWithValues: snapshot.map { ($0.dayKey, $0) })
        var result: [HistoryDay] = []
        var day = earliestDay
        while day <= latestDay {
            let components = calendar.dateComponents([.year, .month, .day], from: day)
            let key = String(format: "%04d-%02d-%02d", components.year ?? 0, components.month ?? 0, components.day ?? 0)
            result.append(recorded[key] ?? HistoryDay(dayKey: key, totalSeconds: 0, applications: []))
            guard let next = calendar.date(byAdding: .day, value: 1, to: day) else { break }
            day = next
        }
        return result
    }

    private static func date(fromDayKey key: String, calendar: Calendar) -> Date? {
        let parts = key.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        return calendar.date(from: DateComponents(year: parts[0], month: parts[1], day: parts[2]))
    }

    func start() {
        guard timer == nil else { return }
        evaluate()
        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.evaluate() }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func reloadPersistedSessions() {
        historyNeedsReload = true
        do {
            try archiveExpiredSessions(at: clock.now)
        } catch {
            reportPersistenceError(error)
        }
        rebuildAggregates(at: clock.now)
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        do {
            let now = clock.now
            try checkpointActiveSession(at: now)
            try finalizeActiveSession(at: now)
        } catch {
            reportPersistenceError(error)
        }
    }

    func toggleManualPause() {
        isManuallyPaused.toggle()
        evaluate()
    }

    func evaluate() {
        guard !isSuspended else { return }
        process(snapshot: detector.snapshot(), at: clock.now)
    }

    func prepareForSleep(at date: Date? = nil) {
        let now = date ?? clock.now
        isSuspended = true
        do {
            try checkpointActiveSession(at: now)
            try finalizeActiveSession(at: now)
        } catch {
            reportPersistenceError(error)
        }
        lastEvaluation = now
        state = .away
        awayStartedAt = nil
        rebuildAggregates(at: now)
    }

    func resumeAfterWake(at date: Date? = nil, snapshot: ActivitySnapshot? = nil) {
        isSuspended = false
        lastEvaluation = nil
        process(snapshot: snapshot ?? detector.snapshot(), at: date ?? clock.now)
    }

    func process(snapshot: ActivitySnapshot, at now: Date) {
        let currentDay = Self.dayKey(for: now, calendar: calendar, resetMinutes: settings.dayResetMinutes)
        var nextSessionStart = now
        if let lastEvaluation,
           Self.dayKey(for: lastEvaluation, calendar: calendar, resetMinutes: settings.dayResetMinutes) != currentDay {
            let boundary = logicalDayStart(for: now)
            do {
                try checkpointActiveSession(at: boundary)
                if activeSession != nil { nextSessionStart = boundary }
                try finalizeActiveSession(at: boundary)
            } catch {
                reportPersistenceError(error)
                return
            }
            do {
                try archiveExpiredSessions(at: now)
            } catch {
                reportPersistenceError(error)
                return
            }
            reloadToday(at: now)
        }

        if currentApplicationName != snapshot.applicationName {
            currentApplicationName = snapshot.applicationName
        }
        let decision = ActivityDecision.evaluate(
            input: .init(
                frontmostApplicationIdentifier: snapshot.bundleIdentifier,
                frontmostApplicationName: snapshot.applicationName,
                pointerApplicationIdentifier: snapshot.pointerApplicationBundleIdentifier,
                pointerApplicationName: snapshot.pointerApplicationName,
                idleSeconds: snapshot.idleSeconds,
                browserURL: snapshot.browserURL
            ),
            applicationRules: settings.ruleApplications,
            websiteRules: settings.ruleWebsites,
            supportedBrowserIdentifiers: Self.supportedBrowserIdentifiers,
            awayThreshold: settings.awayThreshold,
            isManuallyPaused: isManuallyPaused
        )

        let nextState: State
        let target: TrackingTarget?
        switch decision {
        case .paused:
            nextState = .paused
            target = nil
        case .away:
            nextState = .away
            target = nil
        case let .tracking(match):
            nextState = .tracking(applicationName: match.displayName)
            target = match
        }

        if case let .tracking(applicationName) = nextState,
           let target {
            let bundleIdentifier = target.identifier
            if activeSession?.bundleIdentifier != bundleIdentifier {
                do {
                    try checkpointActiveSession(at: now)
                    try finalizeActiveSession(at: now)
                } catch {
                    reportPersistenceError(error)
                    return
                }
                activeSession = ActiveSession(
                    bundleIdentifier: bundleIdentifier,
                    applicationName: applicationName,
                    startedAt: nextSessionStart,
                    dayKey: currentDay,
                    category: WorkCategory(rawValue: target.categoryRawValue) ?? .other
                )
            } else {
                do {
                    try checkpointActiveSession(at: now)
                } catch {
                    reportPersistenceError(error)
                    return
                }
            }
        } else {
            do {
                try checkpointActiveSession(at: lastEvaluation ?? now)
                try finalizeActiveSession(at: lastEvaluation ?? now)
            } catch {
                reportPersistenceError(error)
                return
            }
        }

        if nextState == .away && !reminderSuspendedForOnboarding {
            if awayStartedAt == nil { awayStartedAt = now }
        } else if awayStartedAt != nil {
            awayStartedAt = nil
        }
        if state != nextState { state = nextState }
        lastEvaluation = now
        rebuildAggregates(at: now)
        if nextState == .away,
           settings.awayTimerEnabled || (awayStartedAt.map { now <= $0.addingTimeInterval(10) } ?? false) {
            // Keep the optional away clock live and trigger the one-time
            // transition into compositor-driven breathing. After that point,
            // an idle timer with the away clock disabled needs no redraws.
            objectWillChange.send()
        }
    }

    func createManualSession(application: TrackedApplication, startedAt: Date, endedAt: Date) throws {
        try validateSessionRange(startedAt: startedAt, endedAt: endedAt)
        let dayKey = Self.dayKey(for: startedAt, calendar: calendar, resetMinutes: settings.dayResetMinutes)
        try validateDetailRetention(for: dayKey)
        try checkpointActiveSession(at: lastEvaluation ?? clock.now)
        try finalizeActiveSession(at: lastEvaluation ?? clock.now)
        let session = WorkSession(
            bundleIdentifier: application.bundleIdentifier,
            applicationName: application.displayName,
            startedAt: startedAt,
            endedAt: endedAt,
            effectiveSeconds: endedAt.timeIntervalSince(startedAt),
            dayKey: dayKey,
            isManuallyCreated: true,
            modifiedAt: clock.now,
            categoryRawValue: application.category.rawValue
        )
        try store.insert(session)
        historyNeedsReload = true
        rebuildAggregates(at: clock.now)
    }

    func updateSession(
        _ session: WorkSession,
        application: TrackedApplication,
        startedAt: Date,
        endedAt: Date
    ) throws {
        try validateSessionRange(startedAt: startedAt, endedAt: endedAt)
        let dayKey = Self.dayKey(for: startedAt, calendar: calendar, resetMinutes: settings.dayResetMinutes)
        try validateDetailRetention(for: dayKey)
        try checkpointActiveSession(at: lastEvaluation ?? clock.now)
        try finalizeActiveSession(at: lastEvaluation ?? clock.now)
        session.bundleIdentifier = application.bundleIdentifier
        session.applicationName = application.displayName
        session.startedAt = startedAt
        session.endedAt = endedAt
        session.effectiveSeconds = endedAt.timeIntervalSince(startedAt)
        session.dayKey = dayKey
        session.categoryRawValue = application.category.rawValue
        session.isManuallyEdited = true
        session.modifiedAt = clock.now
        try store.update(session)
        historyNeedsReload = true
        rebuildAggregates(at: clock.now)
    }

    func deleteSession(_ session: WorkSession) throws {
        try validateDetailRetention(for: session.dayKey)
        try checkpointActiveSession(at: lastEvaluation ?? clock.now)
        try finalizeActiveSession(at: lastEvaluation ?? clock.now)
        try store.delete(session)
        historyNeedsReload = true
        rebuildAggregates(at: clock.now)
    }

    func setTodayTotal(for application: TrackedApplication, seconds targetSeconds: TimeInterval) throws {
        let target = max(0, targetSeconds)
        let now = clock.now
        try checkpointActiveSession(at: lastEvaluation ?? now)
        try finalizeActiveSession(at: lastEvaluation ?? now)
        let key = Self.dayKey(for: now, calendar: calendar, resetMinutes: settings.dayResetMinutes)
        let stored = try store.sessions(dayKey: key)
        let corrections = stored.filter {
            statisticsID($0.bundleIdentifier, name: $0.applicationName) == statisticsID(application.bundleIdentifier, name: application.displayName) && $0.isAggregateCorrection
        }
        for correction in corrections {
            try store.delete(correction)
        }
        let refreshed = try store.sessions(dayKey: key)
        let base = refreshed
            .filter { statisticsID($0.bundleIdentifier, name: $0.applicationName) == statisticsID(application.bundleIdentifier, name: application.displayName) && !$0.isAggregateCorrection }
            .reduce(0) { $0 + $1.effectiveSeconds }
        let difference = target - base
        if abs(difference) >= 0.5 {
            try store.insert(WorkSession(
                bundleIdentifier: application.bundleIdentifier,
                applicationName: application.displayName,
                startedAt: now,
                endedAt: now,
                effectiveSeconds: difference,
                dayKey: key,
                isManuallyCreated: true,
                isManuallyEdited: true,
                isAggregateCorrection: true,
                modifiedAt: now,
                categoryRawValue: application.category.rawValue
            ))
        }
        historyNeedsReload = true
        rebuildAggregates(at: now)
    }

    func setDayTotal(dayKey: String, bundleIdentifier: String, applicationName: String, seconds targetSeconds: TimeInterval) throws {
        guard targetSeconds.isFinite, targetSeconds < TimeInterval(Int.max) else {
            throw HistoryDurationInput.InputError.outOfRange
        }
        try validateDetailRetention(for: dayKey)
        let groupID = statisticsID(bundleIdentifier, name: applicationName)
        let now = clock.now
        if let active = activeSession, active.dayKey == dayKey,
           statisticsID(active.bundleIdentifier, name: active.applicationName) == groupID {
            // The requested value is the total at save time, not persisted total
            // plus an unfinished interval. Preserve automatic records, then resume
            // from this exact boundary so the next tick counts only new work.
            let nextDay = calendar.date(byAdding: .day, value: 1, to: logicalDayStart(for: active.startedAt)) ?? now
            let boundary = min(max(now, active.startedAt), nextDay)
            try checkpointActiveSession(at: boundary)
            try finalizeActiveSession(at: boundary)
            activeSession = ActiveSession(
                bundleIdentifier: active.bundleIdentifier,
                applicationName: active.applicationName,
                startedAt: boundary,
                dayKey: Self.dayKey(for: boundary, calendar: calendar, resetMinutes: settings.dayResetMinutes),
                category: active.category
            )
            lastEvaluation = boundary
        }
        let stored = try store.sessions(dayKey: dayKey)
        for item in stored where statisticsID(item.bundleIdentifier, name: item.applicationName) == groupID && item.isAggregateCorrection { try store.delete(item) }
        let base = try store.sessions(dayKey: dayKey).filter { statisticsID($0.bundleIdentifier, name: $0.applicationName) == groupID && !$0.isAggregateCorrection }.reduce(0) { $0 + $1.effectiveSeconds }
        let difference = max(0, targetSeconds) - base
        if difference != 0 {
            try store.insert(WorkSession(bundleIdentifier: bundleIdentifier, applicationName: applicationName, startedAt: now, endedAt: now, effectiveSeconds: difference, dayKey: dayKey, isManuallyCreated: true, isManuallyEdited: true, isAggregateCorrection: true, modifiedAt: now))
        }
        historyNeedsReload = true
        rebuildAggregates(at: now)
    }

    private func validateSessionRange(startedAt: Date, endedAt: Date) throws {
        guard endedAt > startedAt else { throw SessionEditError.invalidRange }
        guard Self.dayKey(for: startedAt, calendar: calendar, resetMinutes: settings.dayResetMinutes) == Self.dayKey(for: endedAt, calendar: calendar, resetMinutes: settings.dayResetMinutes) else {
            throw SessionEditError.crossesDayBoundary
        }
    }

    private func validateDetailRetention(for dayKey: String) throws {
        guard dayKey >= firstDetailedDayKey(at: clock.now) else {
            throw SessionEditError.historyArchived
        }
    }

    func clearPersistenceError() {
        persistenceError = nil
    }

    func reportPersistenceError(_ error: Error) {
        persistenceError = L10n.text("计时数据无法保存或读取：") + error.localizedDescription + L10n.text("。当前时段已保留在内存中，请检查磁盘空间和文件权限后重试。")
    }

    private func checkpointActiveSession(at now: Date) throws {
        while let activeSession,
              now.timeIntervalSince(activeSession.startedAt) >= checkpointInterval {
            let boundary = activeSession.startedAt.addingTimeInterval(checkpointInterval)
            try finalizeActiveSession(at: boundary)
            self.activeSession = ActiveSession(
                bundleIdentifier: activeSession.bundleIdentifier,
                applicationName: activeSession.applicationName,
                startedAt: boundary,
                dayKey: activeSession.dayKey,
                category: activeSession.category
            )
        }
    }

    private func finalizeActiveSession(at endDate: Date) throws {
        guard let activeSession else { return }
        let safeEnd = max(endDate, activeSession.startedAt)
        let seconds = safeEnd.timeIntervalSince(activeSession.startedAt)
        if seconds > 0 {
            let session = WorkSession(
                bundleIdentifier: activeSession.bundleIdentifier,
                applicationName: activeSession.applicationName,
                startedAt: activeSession.startedAt,
                endedAt: safeEnd,
                effectiveSeconds: seconds,
                dayKey: activeSession.dayKey,
                categoryRawValue: activeSession.category.rawValue
            )
            try store.insert(session)
            if historyNeedsReload {
                // A pending external/manual change requires a canonical reload;
                // the new session will be included by that fetch.
            } else {
                cachedDetailSessions.append(session)
                if cachedTodayDayKey == session.dayKey {
                    cachedTodaySessions.append(session)
                    cachedTodaySessions.sort { $0.startedAt > $1.startedAt }
                    todaySessions = cachedTodaySessions
                    addToCachedTodayUsage(session)
                }
                replaceDetailedHistoryDay(
                    dayKey: session.dayKey,
                    sessions: cachedDetailSessions.filter { $0.dayKey == session.dayKey }
                )
            }
        }
        self.activeSession = nil
    }

    private func reloadToday(at date: Date) {
        rebuildAggregates(at: date)
    }

    private func firstDetailedDayKey(at date: Date) -> String {
        let start = logicalDayStart(for: date)
        let earliest = calendar.date(byAdding: .day, value: -(detailRetentionDays - 1), to: start) ?? start
        return Self.dayKey(for: earliest, calendar: calendar, resetMinutes: settings.dayResetMinutes)
    }

    private func archiveExpiredSessions(at date: Date) throws {
        let cutoff = firstDetailedDayKey(at: date)
        var didArchive = false
        while let dayKey = try store.oldestSessionDayKey(before: cutoff) {
            let sessions = try store.sessions(dayKey: dayKey)
            guard !sessions.isEmpty else { break }
            let total = makeHistoryDay(dayKey: dayKey, sessions: sessions).totalSeconds
            try store.archive(sessions, dayKey: dayKey, totalSeconds: total)
            didArchive = true
        }
        if didArchive { historyNeedsReload = true }
    }

    private func rebuildAggregates(at now: Date) {
        let key = Self.dayKey(for: now, calendar: calendar, resetMinutes: settings.dayResetMinutes)
        if !reloadHistoryCacheIfNeeded(at: now) {
            return
        }
        if cachedTodayDayKey != key {
            cachedTodayDayKey = key
            cachedTodaySessions = cachedDetailSessions
                .filter { $0.dayKey == key }
                .sorted { $0.startedAt > $1.startedAt }
            todaySessions = cachedTodaySessions
            cachedTodayUsage = makeCachedUsageTotals(from: cachedTodaySessions)
        }
        aggregateDayKey = key
        var grouped = cachedTodayUsage
        if let activeSession, activeSession.dayKey == key {
            let seconds = max(0, now.timeIntervalSince(activeSession.startedAt))
            let id = statisticsID(activeSession.bundleIdentifier, name: activeSession.applicationName)
            let previous = grouped[id]?.seconds ?? 0
            grouped[id] = CachedUsageTotal(
                applicationName: activeSession.applicationName,
                seconds: previous + seconds
            )
        }
        let nextAppUsage = grouped.map {
            AppUsage(
                bundleIdentifier: $0.key,
                applicationName: $0.value.applicationName,
                seconds: $0.value.seconds
            )
        }.sorted { $0.seconds > $1.seconds }
        if appUsage != nextAppUsage { appUsage = nextAppUsage }
        let nextTodaySeconds = nextAppUsage.reduce(0) { $0 + $1.seconds }
        if todaySeconds != nextTodaySeconds { todaySeconds = nextTodaySeconds }
    }

    static func dayKey(for date: Date, calendar: Calendar, resetMinutes: Int = 0) -> String {
        let shifted = calendar.date(byAdding: .minute, value: -resetMinutes, to: date) ?? date
        let components = calendar.dateComponents([.year, .month, .day], from: shifted)
        return String(format: "%04d-%02d-%02d", components.year ?? 0, components.month ?? 0, components.day ?? 0)
    }

    private func logicalDayStart(for date: Date) -> Date {
        let shifted = calendar.date(byAdding: .minute, value: -settings.dayResetMinutes, to: date) ?? date
        return calendar.date(byAdding: .minute, value: settings.dayResetMinutes, to: calendar.startOfDay(for: shifted)) ?? date
    }

    private func applyDayReset(minutes: Int) {
        do {
            try checkpointActiveSession(at: lastEvaluation ?? clock.now)
            try finalizeActiveSession(at: lastEvaluation ?? clock.now)
            let sessions = try store.sessions(startingWith: firstDetailedDayKey(at: clock.now))
            for session in sessions {
                session.dayKey = Self.dayKey(for: session.startedAt, calendar: calendar, resetMinutes: minutes)
                try store.update(session)
            }
            try archiveExpiredSessions(at: clock.now)
        } catch {
            reportPersistenceError(error)
            return
        }
        historyNeedsReload = true
        reloadToday(at: clock.now)
    }

    @discardableResult
    private func reloadHistoryCacheIfNeeded(at now: Date) -> Bool {
        guard historyNeedsReload else { return true }
        do {
            cachedDetailSessions = try store.sessions(startingWith: firstDetailedDayKey(at: now))
            cachedDailySummaries = try store.allDailySummaries()
        } catch {
            reportPersistenceError(error)
            return false
        }
        historyNeedsReload = false
        cachedTodayDayKey = nil
        cachedTodayUsage = [:]
        rebuildHistory(detailSessions: cachedDetailSessions, summaries: cachedDailySummaries, now: now)
        return true
    }

    private func rebuildHistoryFromCache(at now: Date) {
        _ = reloadHistoryCacheIfNeeded(at: now)
    }

    private func rebuildHistory(detailSessions: [WorkSession], summaries: [DailySummary], now: Date) {
        var days = Dictionary(uniqueKeysWithValues: summaries.map {
            ($0.dayKey, HistoryDay(dayKey: $0.dayKey, totalSeconds: $0.totalSeconds, applications: [], isSummaryOnly: true))
        })
        var detailedDays: [String: [WorkSession]] = Dictionary(grouping: detailSessions, by: \.dayKey)
        if let activeSession {
            let seconds = max(0, now.timeIntervalSince(activeSession.startedAt))
            let transient = WorkSession(bundleIdentifier: activeSession.bundleIdentifier, applicationName: activeSession.applicationName, startedAt: activeSession.startedAt, endedAt: now, effectiveSeconds: seconds, dayKey: activeSession.dayKey, categoryRawValue: activeSession.category.rawValue)
            detailedDays[activeSession.dayKey, default: []].append(transient)
        }
        for (dayKey, sessions) in detailedDays {
            days[dayKey] = makeHistoryDay(dayKey: dayKey, sessions: sessions)
        }
        historyDays = days.values.sorted { $0.dayKey > $1.dayKey }
    }

    private func replaceDetailedHistoryDay(dayKey: String, sessions: [WorkSession]) {
        var updated = historyDays.filter { $0.dayKey != dayKey }
        if !sessions.isEmpty {
            updated.append(makeHistoryDay(dayKey: dayKey, sessions: sessions))
        }
        historyDays = updated.sorted { $0.dayKey > $1.dayKey }
    }

    /// Historical storage changes only when a session is finalized. While a
    /// session is active, views overlay the already-computed live aggregate for
    /// the current logical day instead of regrouping every retained session on
    /// every one-second timer tick.
    private func historySnapshotWithLiveToday() -> [HistoryDay] {
        guard let aggregateDayKey else { return historyDays }
        var snapshot = historyDays.filter { $0.dayKey != aggregateDayKey }
        let hasTodayData = !cachedTodayUsage.isEmpty || activeSession?.dayKey == aggregateDayKey
        if hasTodayData {
            let applications = appUsage.map {
                AppUsage(
                    bundleIdentifier: $0.bundleIdentifier,
                    applicationName: $0.applicationName,
                    seconds: max(0, $0.seconds)
                )
            }.filter { $0.seconds > 0 }
            snapshot.append(HistoryDay(
                dayKey: aggregateDayKey,
                totalSeconds: applications.reduce(0) { $0 + $1.seconds },
                applications: applications
            ))
        }
        return snapshot.sorted { $0.dayKey > $1.dayKey }
    }

    static func websiteStatisticsID(keyword: String) -> String {
        WebsiteRules.statisticsIdentifier(keyword: keyword)
    }

    private func statisticsID(_ identifier: String, name: String) -> String {
        guard identifier.hasPrefix("web."), !identifier.hasPrefix("web.keyword.") else { return identifier }
        let rules = settings.trackedWebsites + settings.themes.flatMap(\.websites)
        if let rule = rules.first(where: { "web.\($0.id.uuidString)" == identifier }) {
            return Self.websiteStatisticsID(keyword: rule.pathContains.isEmpty ? rule.titleContains : rule.pathContains)
        }
        // Old sessions retain the keyword as their display name after a rule is removed.
        return name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? identifier : Self.websiteStatisticsID(keyword: name)
    }

    private func makeCachedUsageTotals(from sessions: [WorkSession]) -> [String: CachedUsageTotal] {
        var totals: [String: CachedUsageTotal] = [:]
        for session in sessions {
            let id = statisticsID(session.bundleIdentifier, name: session.applicationName)
            let previous = totals[id]?.seconds ?? 0
            totals[id] = CachedUsageTotal(
                applicationName: session.applicationName,
                seconds: previous + session.effectiveSeconds
            )
        }
        return totals
    }

    private func addToCachedTodayUsage(_ session: WorkSession) {
        let id = statisticsID(session.bundleIdentifier, name: session.applicationName)
        let previous = cachedTodayUsage[id]?.seconds ?? 0
        cachedTodayUsage[id] = CachedUsageTotal(
            applicationName: session.applicationName,
            seconds: previous + session.effectiveSeconds
        )
    }

    private func makeHistoryDay(dayKey: String, sessions: [WorkSession]) -> HistoryDay {
        var apps: [String: (String, TimeInterval)] = [:]
        for session in sessions {
            let id = statisticsID(session.bundleIdentifier, name: session.applicationName)
            let value = apps[id]?.1 ?? 0
            apps[id] = (session.applicationName, value + session.effectiveSeconds)
        }
        let usages = apps.map {
            AppUsage(bundleIdentifier: $0.key, applicationName: $0.value.0, seconds: max(0, $0.value.1))
        }.filter { $0.seconds > 0 }.sorted { $0.seconds > $1.seconds }
        return HistoryDay(dayKey: dayKey, totalSeconds: usages.reduce(0) { $0 + $1.seconds }, applications: usages)
    }
}
