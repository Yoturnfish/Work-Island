import Foundation
import Testing
@testable import WorkIslandCore

struct WebsiteRulesTests {
    @Test func matchingPreservesHostAndPathSemantics() {
        #expect(WebsiteRules.matches(
            url: "https://docs.example.com/project/ABC",
            titleContains: "",
            pathContains: "project/abc",
            isEnabled: true,
            matchesHostOnly: false
        ))
        #expect(WebsiteRules.matches(
            url: "https://docs.example.com/project",
            titleContains: "example",
            pathContains: "",
            isEnabled: true,
            matchesHostOnly: true
        ))
        #expect(!WebsiteRules.matches(
            url: "https://notexample.com/project",
            titleContains: "example",
            pathContains: "",
            isEnabled: true,
            matchesHostOnly: true
        ))
        #expect(!WebsiteRules.matches(
            url: "https://example.com/project",
            titleContains: "example",
            pathContains: "",
            isEnabled: false,
            matchesHostOnly: true
        ))
    }

    @Test func previewsRemoveCredentialsQueriesAndFragments() {
        #expect(WebsiteRules.privacySafePreview(
            of: "https://name:password@example.com/private?q=secret#fragment"
        ) == "https://example.com/private")
        #expect(WebsiteRules.privacySafePreview(of: "file:///tmp/private") == nil)
    }

    @Test func keywordExtractionHandlesCommonCountrySuffixes() {
        #expect(WebsiteRules.registrableKeyword(from: "https://sub.example.co.uk/path") == "example")
        #expect(WebsiteRules.registrableKeyword(from: "https://www.example.com.cn/path") == "example")
        #expect(WebsiteRules.registrableKeyword(from: "https://127.0.0.1/path") == nil)
    }

    @Test func statisticsIdentifierNormalizesCaseAndWhitespace() {
        #expect(WebsiteRules.statisticsIdentifier(keyword: " Example/Path ")
            == WebsiteRules.statisticsIdentifier(keyword: "example/path"))
        #expect(WebsiteRules.statisticsIdentifier(keyword: "example/path").hasPrefix("web.keyword."))
    }
}
