import Foundation

struct ActivitySnapshot: Equatable, Sendable {
    let bundleIdentifier: String?
    let applicationName: String?
    let idleSeconds: TimeInterval

    /// 鼠标滚动、移动或拖动后的短暂窗口内，实际位于指针下方的应用。
    /// 它允许非焦点的工作窗口在用户直接操作时恢复计时。
    let pointerApplicationBundleIdentifier: String?
    let pointerApplicationName: String?
    let browserURL: String?

    init(
        bundleIdentifier: String?,
        applicationName: String?,
        idleSeconds: TimeInterval,
        pointerApplicationBundleIdentifier: String? = nil,
        pointerApplicationName: String? = nil,
        browserURL: String? = nil
    ) {
        self.bundleIdentifier = bundleIdentifier
        self.applicationName = applicationName
        self.idleSeconds = idleSeconds
        self.pointerApplicationBundleIdentifier = pointerApplicationBundleIdentifier
        self.pointerApplicationName = pointerApplicationName
        self.browserURL = browserURL
    }
}

@MainActor
protocol ActivityDetecting {
    func snapshot() -> ActivitySnapshot
}

@MainActor
protocol BrowserURLProviding: AnyObject {
    func url(bundleIdentifier: String, pointer: CGPoint?) -> String?
}

@MainActor
protocol ClockProviding {
    var now: Date { get }
}

@MainActor
protocol SessionStoring: AnyObject {
    func insert(_ session: WorkSession) throws
    func sessions(dayKey: String) throws -> [WorkSession]
    func sessions(startingWith dayKey: String) throws -> [WorkSession]
    func oldestSessionDayKey(before dayKey: String) throws -> String?
    func session(sourceID: String) throws -> WorkSession?
    func update(_ session: WorkSession) throws
    func delete(_ session: WorkSession) throws
    func archive(_ sessions: [WorkSession], dayKey: String, totalSeconds: TimeInterval) throws
    func allDailySummaries() throws -> [DailySummary]
}
