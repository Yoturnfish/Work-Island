import Foundation

/// Platform-neutral website matching used by both the macOS application and
/// the future Windows activity adapter. It intentionally has no UI, storage,
/// browser-automation or Apple-framework dependencies.
public enum WebsiteRules {
    public static func statisticsIdentifier(keyword: String) -> String {
        let normalized = keyword.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        return "web.keyword." + Data(normalized.utf8).base64EncodedString()
    }

    public static func matches(
        url: String?,
        titleContains: String,
        pathContains: String,
        isEnabled: Bool,
        matchesHostOnly: Bool?
    ) -> Bool {
        guard isEnabled, let normalizedURL = url?.lowercased() else { return false }
        let keyword = pathContains.isEmpty ? titleContains : pathContains
        let value = keyword.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !value.isEmpty else { return false }
        if matchesHostOnly == true {
            guard let host = URLComponents(string: normalizedURL)?.host?.lowercased() else { return false }
            return host.split(separator: ".").contains(Substring(value))
        }
        return normalizedURL.contains(value)
    }

    public static func privacySafePreview(of address: String, maximumLength: Int = 52) -> String? {
        guard var parts = URLComponents(string: address),
              ["http", "https"].contains(parts.scheme?.lowercased() ?? ""),
              parts.host != nil else { return nil }
        parts.user = nil
        parts.password = nil
        parts.query = nil
        parts.fragment = nil
        guard let value = parts.string else { return nil }
        let limit = max(0, maximumLength)
        return value.count > limit ? String(value.prefix(limit)) + "…" : value
    }

    public static func registrableKeyword(from address: String) -> String? {
        guard let url = URLComponents(string: address),
              ["http", "https"].contains(url.scheme?.lowercased() ?? ""),
              let host = url.host?.lowercased() else { return nil }
        let parts = host.split(separator: ".").map(String.init)
        guard parts.count >= 2, !parts.allSatisfy({ Int($0) != nil }), !host.contains(":") else { return nil }
        let commonSecondLevels = ["co", "com", "org", "net", "gov", "edu", "ac"]
        let suffixCount = parts.last?.count == 2 && commonSecondLevels.contains(parts[parts.count - 2]) ? 2 : 1
        guard parts.count > suffixCount else { return nil }
        return parts[parts.count - suffixCount - 1]
    }
}
