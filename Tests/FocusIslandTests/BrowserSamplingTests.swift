import AppKit
import Testing
@testable import FocusIsland

@MainActor
struct BrowserSamplingTests {
    @Test func automaticSamplingStopsWhenTheActiveThemeHasNoEnabledWebsiteRules() {
        let settingsName = "BrowserSamplingTests.\(UUID())"
        let defaults = UserDefaults(suiteName: settingsName)!
        defer { defaults.removePersistentDomain(forName: settingsName) }
        let settings = TrackingSettings(defaults: defaults)
        settings.ruleWebsites = []
        let provider = RecordingBrowserURLProvider()
        let detector = SystemActivityDetector(
            browserURLProvider: provider,
            awayThreshold: { 30 },
            websiteRulesEnabled: { settings.ruleWebsites.contains(where: \.isEnabled) }
        )
        func read() -> String? {
            detector.readBrowserURL(bundleIdentifier: "com.apple.Safari", idleSeconds: 0, pointer: nil)
        }
        #expect(read() == nil)
        #expect(provider.requests.isEmpty)
        settings.ruleWebsites = [.init(displayName: "Example", pathContains: "example.test", isEnabled: false)]
        #expect(read() == nil)
        #expect(provider.requests.isEmpty)
        settings.ruleWebsites[0].isEnabled = true
        #expect(read() == "https://example.test/work")
        #expect(provider.requests.count == 1)
        settings.createTheme()
        #expect(read() == nil)
        #expect(provider.requests.count == 1)
    }

    @Test func enabledSamplingHonorsIdleBrowserAndPointerBoundaries() {
        let provider = RecordingBrowserURLProvider()
        let detector = SystemActivityDetector(browserURLProvider: provider, websiteRulesEnabled: { true })
        for identifier in [nil, "com.example.editor"] {
            #expect(detector.readBrowserURL(bundleIdentifier: identifier, idleSeconds: 0, pointer: nil) == nil)
        }
        #expect(detector.readBrowserURL(bundleIdentifier: "com.apple.Safari", idleSeconds: 30, pointer: nil) == nil)
        #expect(provider.requests.isEmpty)
        let point = CGPoint(x: 100, y: 200)
        #expect(detector.readBrowserURL(bundleIdentifier: "com.apple.Safari", idleSeconds: 1, pointer: point) != nil)
        #expect(provider.requests.count == 1)
        #expect(provider.requests.first?.pointer == point)
    }
}

@MainActor
private final class RecordingBrowserURLProvider: BrowserURLProviding {
    var requests: [(bundleIdentifier: String, pointer: CGPoint?)] = []
    func url(bundleIdentifier: String, pointer: CGPoint?) -> String? {
        requests.append((bundleIdentifier, pointer))
        return "https://example.test/work"
    }
}
