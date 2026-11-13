import AppKit
import Combine
import Foundation

enum BrowserPermissionStatus: String, Sendable {
    case notInstalled = "未安装"
    case pending = "待授权"
    case authorized = "已授权"
    case denied = "已拒绝"
}

@MainActor
final class BrowserAutomationService: ObservableObject, BrowserURLProviding {
    static let shared = BrowserAutomationService()

    struct Browser: Identifiable, Sendable {
        let id: String
        let name: String
        let applicationName: String
    }

    static let browsers = [
        Browser(id: "com.google.Chrome", name: "Chrome", applicationName: "Google Chrome"),
        Browser(id: "com.microsoft.edgemac", name: "Edge", applicationName: "Microsoft Edge"),
        Browser(id: "com.apple.Safari", name: "Safari", applicationName: "Safari")
    ]

    @Published private(set) var statuses: [String: BrowserPermissionStatus] = [:]
    private var authorized: Set<String>
    private let defaults: UserDefaults

    private init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        authorized = Set(defaults.stringArray(forKey: "automationAuthorizedBrowsers") ?? [])
        refreshStatuses()
    }

    func refreshStatuses() {
        for browser in Self.browsers {
            guard NSWorkspace.shared.urlForApplication(withBundleIdentifier: browser.id) != nil else { statuses[browser.id] = .notInstalled; continue }
            statuses[browser.id] = authorized.contains(browser.id) ? .authorized : (statuses[browser.id] == .denied ? .denied : .pending)
        }
    }

    func requestAuthorization() {
        for browser in Self.browsers where NSWorkspace.shared.urlForApplication(withBundleIdentifier: browser.id) != nil {
            var error: NSDictionary?
            let source = "tell application \"\(browser.applicationName)\" to count windows"
            _ = NSAppleScript(source: source)?.executeAndReturnError(&error)
            if error == nil {
                authorized.insert(browser.id)
                statuses[browser.id] = .authorized
            } else {
                authorized.remove(browser.id)
                statuses[browser.id] = Self.isPermissionDenied(error) ? .denied : .pending
            }
        }
        defaults.set(Array(authorized), forKey: "automationAuthorizedBrowsers")
    }

    func url(bundleIdentifier: String, pointer: CGPoint?) -> String? {
        guard authorized.contains(bundleIdentifier), let browser = Self.browsers.first(where: { $0.id == bundleIdentifier }) else { return nil }
        let tabExpression = bundleIdentifier == "com.apple.Safari" ? "URL of current tab of w" : "URL of active tab of w"
        let privateCheck = bundleIdentifier == "com.apple.Safari" ? "" : "if (mode of w as text) contains \"incognito\" then return \"\"\n"
        let selectWindow: String
        if let pointer {
            selectWindow = """
            repeat with w in windows
                set b to bounds of w
                if \(Int(pointer.x)) is greater than or equal to item 1 of b and \(Int(pointer.x)) is less than or equal to item 3 of b and \(Int(pointer.y)) is greater than or equal to item 2 of b and \(Int(pointer.y)) is less than or equal to item 4 of b then
                    \(privateCheck)                    return \(tabExpression)
                end if
            end repeat
            """
        } else {
            selectWindow = """
            if (count windows) > 0 then
                set w to front window
                \(privateCheck)                return \(tabExpression)
            end if
            """
        }
        let source = """
        tell application "\(browser.applicationName)"
            \(selectWindow)
        end tell
        return ""
        """
        var error: NSDictionary?
        let descriptor = NSAppleScript(source: source)?.executeAndReturnError(&error)
        if Self.isPermissionDenied(error) {
            authorized.remove(bundleIdentifier)
            statuses[bundleIdentifier] = .denied
            defaults.set(Array(authorized), forKey: "automationAuthorizedBrowsers")
        }
        guard error == nil, let value = descriptor?.stringValue, !value.isEmpty else { return nil }
        return value
    }

    func openAutomationSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation") { NSWorkspace.shared.open(url) }
    }

    private static func isPermissionDenied(_ error: NSDictionary?) -> Bool {
        (error?[NSAppleScript.errorNumber] as? Int) == -1743
    }
}
