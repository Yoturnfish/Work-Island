import Combine
import Foundation
import QuartzCore
import OSLog

enum GlassBlurMetrics {
    static let foregroundTransitionDuration = 0.25
    static let defaultRadius = 1.5
    static let maximumRadius = 6.0
    static func normalized(_ radius: Double) -> Double {
        radius.isFinite ? min(max(radius, 0), maximumRadius) : 0
    }
    /// A blurred background can use fewer input pixels while the foreground,
    /// optical outline and drawable remain at the display's original scale.
    static func captureScale(radius: Double, displayScale: CGFloat) -> CGFloat {
        min(normalized(radius) >= 1.5 ? 1 : 2, displayScale)
    }
}

struct GlassTextContrastDecision {
    private(set) var usesDarkText = false
    private var lastChange: TimeInterval = -.infinity

    mutating func update(luminance: Double, timestamp: TimeInterval) -> Bool {
        guard luminance.isFinite, timestamp.isFinite, timestamp - lastChange >= 0.2 else { return false }
        let requested = usesDarkText ? luminance >= 0.48 : luminance > 0.66
        guard requested != usesDarkText else { return false }
        usesDarkText = requested
        lastChange = timestamp
        return true
    }
}

/// Only classification changes publish to SwiftUI. The GPU returns one scalar
/// at most four times per second; no CPU image scans, saved frames or timer.
@MainActor
final class GlassBackdropAppearance: ObservableObject {
    static let shared = GlassBackdropAppearance()
    @Published private(set) var usesDarkText = false
    private var decision = GlassTextContrastDecision()
    private static let log = Logger(subsystem: "com.focusisland.app", category: "LiquidGlass")

    func receive(luminance: Double) {
        if decision.update(luminance: luminance, timestamp: CACurrentMediaTime()) {
            usesDarkText = decision.usesDarkText
            Self.log.notice("Glass foreground uses dark text: \(self.usesDarkText)")
        }
    }
    func reset() {
        decision = GlassTextContrastDecision()
        if usesDarkText { usesDarkText = false }
    }
}
