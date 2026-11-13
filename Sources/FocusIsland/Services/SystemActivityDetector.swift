import AppKit
import CoreGraphics
import Foundation

@MainActor
final class SystemActivityDetector: ActivityDetecting {
    enum InputSource: Equatable {
        case frontmost
        case pointer
    }

    struct InputSelection: Equatable {
        let source: InputSource
        let idleSeconds: TimeInterval
    }

    private var lastPointerInteraction: Date?
    private var lastPointerBundleIdentifier: String?
    private var lastPointerApplicationName: String?
    private var lastPointerLocation: CGPoint?
    private var pointerWindowNeedsRefresh = false
    private var eventMonitors: [Any] = []
    private let browserURLProvider: any BrowserURLProviding
    private let awayThreshold: () -> TimeInterval
    private let websiteRulesEnabled: () -> Bool

    init(
        browserURLProvider: any BrowserURLProviding = BrowserAutomationService.shared,
        awayThreshold: @escaping () -> TimeInterval = { 30 },
        websiteRulesEnabled: @escaping () -> Bool = { false }
    ) {
        self.browserURLProvider = browserURLProvider
        self.awayThreshold = awayThreshold
        self.websiteRulesEnabled = websiteRulesEnabled
        let events: NSEvent.EventTypeMask = [.mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged, .scrollWheel, .leftMouseDown, .rightMouseDown, .otherMouseDown]
        eventMonitors.append(NSEvent.addGlobalMonitorForEvents(matching: events) { [weak self] _ in
            Task { @MainActor in self?.capturePointerActivity() }
        } as Any)
        eventMonitors.append(NSEvent.addLocalMonitorForEvents(matching: events) { [weak self] event in
            self?.capturePointerActivity()
            return event
        } as Any)
    }

    func snapshot() -> ActivitySnapshot {
        let app = NSWorkspace.shared.frontmostApplication
        let systemIdle = CGEventSource.secondsSinceLastEventType(
            .combinedSessionState,
            eventType: CGEventType(rawValue: UInt32.max)!
        )
        let keyboardIdle = min(
            CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: .keyDown),
            CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: .flagsChanged)
        )
        let pointerIdle = lastPointerInteraction.map { Date().timeIntervalSince($0) }
        let selection = Self.selectInputSource(
            pointerIdle: pointerIdle,
            keyboardIdle: keyboardIdle,
            systemIdle: systemIdle
        )
        let usesPointer = selection.source == .pointer
        if Self.shouldResolvePointerWindow(
            needsRefresh: pointerWindowNeedsRefresh,
            usesPointer: usesPointer
        ) {
            resolvePointerWindow()
        }
        let effectiveBundleIdentifier = usesPointer ? lastPointerBundleIdentifier : app?.bundleIdentifier
        let browserURL = readBrowserURL(bundleIdentifier: effectiveBundleIdentifier,
                                       idleSeconds: selection.idleSeconds,
                                       pointer: usesPointer ? lastPointerLocation : nil)
        return ActivitySnapshot(
            bundleIdentifier: app?.bundleIdentifier,
            applicationName: app?.localizedName,
            idleSeconds: selection.idleSeconds,
            pointerApplicationBundleIdentifier: usesPointer ? lastPointerBundleIdentifier : nil,
            pointerApplicationName: usesPointer ? lastPointerApplicationName : nil,
            browserURL: browserURL
        )
    }

    func readBrowserURL(bundleIdentifier: String?, idleSeconds: TimeInterval, pointer: CGPoint?) -> String? {
        guard websiteRulesEnabled(), idleSeconds < awayThreshold(), let bundleIdentifier,
              BrowserAutomationService.browsers.contains(where: { $0.id == bundleIdentifier }) else { return nil }
        return browserURLProvider.url(bundleIdentifier: bundleIdentifier, pointer: pointer)
    }

    static func selectInputSource(
        pointerIdle: TimeInterval?,
        keyboardIdle: TimeInterval,
        systemIdle: TimeInterval
    ) -> InputSelection {
        guard let pointerIdle, pointerIdle <= keyboardIdle else {
            return InputSelection(source: .frontmost, idleSeconds: min(keyboardIdle, systemIdle))
        }
        return InputSelection(source: .pointer, idleSeconds: min(pointerIdle, systemIdle))
    }

    nonisolated static func shouldResolvePointerWindow(
        needsRefresh: Bool,
        usesPointer: Bool
    ) -> Bool {
        needsRefresh && usesPointer
    }

    private func capturePointerActivity() {
        lastPointerInteraction = Date()
        lastPointerLocation = quartzPointerLocation()
        pointerWindowNeedsRefresh = true
    }

    private func resolvePointerWindow() {
        pointerWindowNeedsRefresh = false
        guard let point = lastPointerLocation,
              let windowInfo = windowUnderPointer(at: point),
              let pid = windowInfo[kCGWindowOwnerPID as String] as? pid_t,
              let app = NSRunningApplication(processIdentifier: pid) else {
            lastPointerBundleIdentifier = nil
            lastPointerApplicationName = nil
            return
        }
        lastPointerBundleIdentifier = app.bundleIdentifier
        lastPointerApplicationName = app.localizedName
    }

    private func windowUnderPointer(at quartzPoint: CGPoint) -> [String: Any]? {
        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        let windows = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]] ?? []
        return windows.first { info in
            guard let layer = info[kCGWindowLayer as String] as? Int, layer == 0,
                  let rawBounds = info[kCGWindowBounds as String],
                  let bounds = CGRect(dictionaryRepresentation: rawBounds as! CFDictionary)
            else { return false }
            return bounds.contains(quartzPoint)
        }
    }

    private func quartzPointerLocation() -> CGPoint {
        let pointer = NSEvent.mouseLocation
        let mainHeight = NSScreen.screens.first(where: { $0.frame.origin == .zero })?.frame.height
            ?? NSScreen.main?.frame.height
            ?? 0
        return CGPoint(x: pointer.x, y: mainHeight - pointer.y)
    }
}

struct SystemClock: ClockProviding {
    var now: Date { Date() }
}
