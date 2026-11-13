import Foundation

/// Shared animation math; the live views are rendered by Core Animation.
enum ProgressAnimationMetrics {
    enum AnimationMode: Equatable {
        case staticDisplay
        case scanning
        case awayPulse
    }

    static func columnCount(forScale scale: Double) -> Int {
        if scale < FocusDesign.Window.compactThreshold { return FocusDesign.Matrix.compactColumns }
        if scale < FocusDesign.Window.adaptiveThreshold { return FocusDesign.Matrix.mediumColumns }
        return FocusDesign.Matrix.fullColumns
    }

    static func animationMode(
        progress: Double,
        isActive: Bool,
        isAway: Bool,
        animationsEnabled: Bool
    ) -> AnimationMode {
        guard animationsEnabled else { return .staticDisplay }
        if isAway { return progress >= 1 ? .staticDisplay : .awayPulse }
        if isActive { return .scanning }
        return .staticDisplay
    }

    static func scanEnabled(for mode: AnimationMode) -> Bool {
        mode == .scanning
    }

    /// Shared by the floating matrix and the menu-bar matrix. Away breathing
    /// belongs only to the unfinished cells, never to the illuminated portion.
    static func awayBreathingStrength(elapsed: TimeInterval) -> Double {
        guard elapsed.isFinite, elapsed > 0 else { return 0 }
        let fade = min(elapsed / 0.8, 1)
        let envelope = fade * fade * (3 - 2 * fade)
        let phase = elapsed.truncatingRemainder(dividingBy: FocusDesign.Motion.awayPulseDuration) / FocusDesign.Motion.awayPulseDuration
        return (1 - cos(phase * .pi * 2)) / 2 * envelope
    }

    static func cellOpacity(isFilled: Bool, column: Int, columns: Int, mode: AnimationMode, timestamp: TimeInterval, awayElapsed: TimeInterval? = nil) -> Double {
        let scanPhase = timestamp.truncatingRemainder(dividingBy: FocusDesign.Motion.scanDuration) / FocusDesign.Motion.scanDuration
        let scanColumn = Int(scanPhase * Double(columns + FocusDesign.Matrix.scanPadding)) - FocusDesign.Matrix.scanLeadingOffset
        let base: Double
        if isFilled {
            base = FocusDesign.Opacity.activePixel
        } else {
            base = FocusDesign.Opacity.inactivePixel + (mode == .awayPulse
                ? awayBreathingStrength(elapsed: awayElapsed ?? timestamp) * FocusDesign.Opacity.awayInactivePixel : 0)
        }
        let distance = abs(column - scanColumn)
        let scan = scanEnabled(for: mode) && distance < FocusDesign.Matrix.scanRadius
            ? Double(FocusDesign.Matrix.scanRadius - distance) * FocusDesign.Opacity.scanStep : 0
        return min(base + scan, 1)
    }

}
