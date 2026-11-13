import SwiftUI

struct StyledProgressView: View {
    static let waveformPatternCount = 7

    let progress: Double
    let isActive: Bool
    let animationsEnabled: Bool
    var isAway = false
    var columns = FocusDesign.Matrix.fullColumns
    var accentColor: Color = .focusGreen
    let style: IslandProgressStyle
    var awayBreathingStartedAt: Date? = nil
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    static func clampedProgress(_ value: Double) -> Double { value.isFinite ? min(max(value, 0), 1) : 0 }
    static func waveformHeight(index: Int, count: Int) -> Double {
        let mirroredIndex = min(index, count - 1 - index)
        return 0.32 + 0.68 * abs(sin(
            Double(mirroredIndex * waveformPatternCount) * .pi / Double(max(1, count - 1))
        ))
    }
    static func filledCells(progress: Double, count: Int) -> Int {
        let value = clampedProgress(progress)
        guard value > 0, count > 0 else { return 0 }
        return value >= 1 ? count : min(count - 1, max(1, Int(value * Double(count))))
    }
    static func shimmerCenter(width: CGFloat, phase: Double) -> CGFloat {
        // Travel fully outside both edges before the next cycle starts.
        let radius = max(24, width * 0.16)
        return -radius + (width + 2 * radius) * phase
    }
    static func shouldAnimate(progress: Double, active: Bool, enabled: Bool, reduceMotion: Bool) -> Bool {
        active && enabled && !reduceMotion
    }

    static func waveformCellOpacity(isFilled: Bool, progress: Double, moving: Bool, pulsing: Bool, phase: Double, awayElapsed: TimeInterval? = nil) -> Double {
        if isFilled {
            return 1
        }
        return 0.18 + (pulsing ? ProgressAnimationMetrics.awayBreathingStrength(elapsed: awayElapsed ?? phase * FocusDesign.Motion.awayPulseDuration) * (0.35 - 0.18) : 0)
    }

    var body: some View {
        let value = Self.clampedProgress(progress)
        let moving = Self.shouldAnimate(progress: value, active: isActive && !isAway, enabled: animationsEnabled, reduceMotion: reduceMotion)
        let pulsing = isAway && value < 1 && animationsEnabled && !reduceMotion
        LayerAnimatedProgressView(
            progress: value,
            isActive: moving,
            animationsEnabled: animationsEnabled && !reduceMotion,
            isAway: pulsing,
            columns: columns,
            accentColor: accentColor,
            style: style
        )
        .accessibilityLabel(L10n.text("每日目标"))
        .accessibilityValue("\(Int(value * 100))%")
    }
}
