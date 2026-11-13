import AppKit
import Combine
import SwiftData

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var panelController: IslandPanelController?
    private var engine: TrackingEngine?
    private var modelContainer: ModelContainer?
    private var observers: [NSObjectProtocol] = []
    private var cancellables: Set<AnyCancellable> = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        do {
            let container = try FocusIslandDataStore.makeModelContainer()
            let store = SwiftDataSessionStore(context: container.mainContext)
            let settings = TrackingSettings()
            let engine = TrackingEngine(
                detector: SystemActivityDetector(
                    awayThreshold: { settings.awayThreshold },
                    websiteRulesEnabled: { settings.ruleWebsites.contains(where: \.isEnabled) }
                ),
                store: store,
                clock: SystemClock(),
                settings: settings
            )
            do {
                let importer = try LegacySessionImporter()
                _ = try importer.migrateIfNeeded(into: store)
                engine.reloadPersistedSessions()
            } catch {
                engine.reportPersistenceError(error)
            }
            let panelController = IslandPanelController(engine: engine)
            self.modelContainer = container
            self.engine = engine
            self.panelController = panelController
            panelController.show()
            engine.start()
            observeSystemLifecycle(engine: engine)
            configureLaunchAtLogin(settings: engine.settings)
        } catch {
            let alert = NSAlert(error: error)
            alert.messageText = L10n.text("Work Island 无法启动")
            alert.informativeText = L10n.text("本地数据存储初始化失败：") + error.localizedDescription
            alert.runModal()
            NSApp.terminate(nil)
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        panelController?.stopGlassCapture()
        engine?.stop()
        observers.forEach(NotificationCenter.default.removeObserver)
    }

    private func observeSystemLifecycle(engine: TrackingEngine) {
        let center = NSWorkspace.shared.notificationCenter
        observers.append(center.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak engine] _ in
            Task { @MainActor in engine?.prepareForSleep() }
        })
        observers.append(center.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak engine] _ in
            Task { @MainActor in engine?.resumeAfterWake() }
        })
        observers.append(NotificationCenter.default.addObserver(forName: NSNotification.Name.NSSystemClockDidChange, object: nil, queue: .main) { [weak engine] _ in
            Task { @MainActor in engine?.evaluate() }
        })
        observers.append(NotificationCenter.default.addObserver(forName: NSNotification.Name.NSCalendarDayChanged, object: nil, queue: .main) { [weak engine] _ in
            Task { @MainActor in engine?.evaluate() }
        })
    }

    private func configureLaunchAtLogin(settings: TrackingSettings) {
        LaunchAtLoginController.apply(enabled: settings.launchAtLogin, settings: settings)
        settings.$launchAtLogin.dropFirst().sink { [weak settings] enabled in
            guard let settings else { return }
            LaunchAtLoginController.apply(enabled: enabled, settings: settings)
        }.store(in: &cancellables)
    }
}
