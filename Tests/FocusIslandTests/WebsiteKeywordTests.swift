import Foundation
import Testing
@testable import FocusIsland

@MainActor struct WebsiteKeywordTests {
    @Test func urlPreviewHidesCredentialsAndQuery() {
        #expect(WebsiteKeyword.preview(of: "https://user:secret@t.bilibili.com/video?token=abc#x") == "https://t.bilibili.com/video")
        #expect(WebsiteKeyword.preview(of: "about:blank") == nil)
        #expect(WebsiteKeyword.preview(of: "https://example.com/" + String(repeating: "a", count: 100))!.count == 53)
    }
    @Test func extractsKeywordsWithoutQueriesOrSubdomains() {
        #expect(WebsiteKeyword.extract(from: "https://t.bilibili.com/?tab=video") == "bilibili")
        #expect(WebsiteKeyword.extract(from: "https://WWW.Example.co.uk/path?q=x") == "example")
        #expect(WebsiteKeyword.extract(from: "https://docs.example.com.cn/a") == "example")
        #expect(WebsiteKeyword.extract(from: "chrome://settings") == nil)
        #expect(WebsiteKeyword.extract(from: "file:///tmp/bilibili.html") == nil)
        #expect(WebsiteKeyword.extract(from: "https://127.0.0.1/path") == nil)
    }
    @Test func automaticWebsiteUsesCurrentScopeAndMatchesOnlyHostLabels() {
        let suite = "WebsiteKeywordTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let settings = TrackingSettings(defaults: defaults)
        settings.createTheme()
        #expect(settings.addCurrentWebsite(address: "https://t.bilibili.com/?tab=video") == "bilibili")
        #expect(settings.trackedWebsites.isEmpty)
        let restored = TrackingSettings(defaults: defaults)
        let rule = restored.ruleWebsites[0]
        #expect(rule.pathContains == "bilibili")
        #expect(rule.displayName == "bilibili")
        #expect(rule.matches(url: "https://www.bilibili.com/video/123"))
        #expect(!rule.matches(url: "https://example.com/?next=bilibili"))
        #expect(!rule.matches(url: "https://notbilibili.com"))
        #expect(settings.addCurrentWebsite(address: "about:blank") == nil)
        #expect(settings.ruleWebsites.count == 1)
        let legacy = TrackedWebsite(displayName: "Legacy", pathContains: "/work")
        #expect(legacy.matches(url: "https://example.com/work"))
    }
}
