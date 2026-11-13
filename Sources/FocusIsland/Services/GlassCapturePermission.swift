import AppKit
import Combine

/// Only explicit theme selection or the permission button may request access.
/// Launch/visibility checks are read-only, including restored glass preferences.
@MainActor
final class GlassCapturePermission: ObservableObject {
    static let shared = GlassCapturePermission()
    // Read only during the 0.8.24/25 preference migration. No runtime switch.
    static let legacyKey = "glassUseLegacyBackdrop"
    static let requestKey = "glassScreenCaptureRequested"
    @Published private(set) var granted: Bool
    @Published private(set) var requesting = false
    @Published private(set) var captureFailed = false
    private(set) var requestAttempted: Bool
    private let defaults: UserDefaults
    private let checkAccess: @MainActor () -> Bool
    private let requestAccess: @MainActor () -> Bool

    init(defaults: UserDefaults = .standard,
         checkAccess: @escaping @MainActor () -> Bool = { CGPreflightScreenCaptureAccess() },
         requestAccess: @escaping @MainActor () -> Bool = { CGRequestScreenCaptureAccess() }) {
        self.defaults = defaults
        self.checkAccess = checkAccess
        self.requestAccess = requestAccess
        granted = checkAccess()
        requestAttempted = defaults.bool(forKey: Self.requestKey)
    }

    func refresh() {
        let current = checkAccess()
        if current != granted { granted = current }
    }

    func requestForUserSelection() {
        guard !requesting else { return }
        refresh()
        guard !granted, !requestAttempted else { return }
        requestAttempted = true
        defaults.set(true, forKey: Self.requestKey)
        requesting = true
        // macOS owns this dialog. Never change TCC, dismiss it, or repeatedly
        // request access from lifecycle/stream callbacks.
        _ = requestAccess()
        refresh()
        requesting = false
    }

    func requestOrOpenSettings() {
        if !granted && !requestAttempted { requestForUserSelection() }
        else { openSystemSettings() }
    }

    func setCaptureFailed(_ failed: Bool) {
        if captureFailed != failed { captureFailed = failed }
    }

    func openSystemSettings() {
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") else { return }
        NSWorkspace.shared.open(url)
    }
}
