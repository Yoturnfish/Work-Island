import Foundation
import WorkIslandCore

enum WebsiteKeyword {
    static func preview(of address: String) -> String? {
        WebsiteRules.privacySafePreview(of: address)
    }
    static func extract(from address: String) -> String? {
        WebsiteRules.registrableKeyword(from: address)
    }
}
