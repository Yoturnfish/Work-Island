import AppKit
import QuartzCore
import SwiftUI

/// A compositor-driven replacement for the continuously redrawn SwiftUI
/// Canvas. Static geometry is rebuilt only when size/progress/style changes;
/// scanning and breathing remain display-rate Core Animation effects.
struct LayerAnimatedProgressView: NSViewRepresentable {
    let progress: Double
    let isActive: Bool
    let animationsEnabled: Bool
    let isAway: Bool
    let columns: Int
    let accentColor: Color
    let style: IslandProgressStyle

    func makeNSView(context: Context) -> LayerAnimatedProgressNSView {
        LayerAnimatedProgressNSView()
    }

    func updateNSView(_ view: LayerAnimatedProgressNSView, context: Context) {
        view.update(
            progress: progress,
            isActive: isActive,
            animationsEnabled: animationsEnabled,
            isAway: isAway,
            columns: columns,
            accentColor: NSColor(accentColor),
            style: style
        )
    }
}

final class LayerAnimatedProgressNSView: NSView {
    static let scanAnimationKey = "WorkIsland.progressScan"
    static let pulseAnimationKey = "WorkIsland.progressPulse"

    private let unfilledLayer = CAShapeLayer()
    private let filledLayer = CAShapeLayer()
    private let unfilledScanContainer = CALayer()
    private let filledScanContainer = CALayer()
    private let unfilledScanMask = CAShapeLayer()
    private let filledScanMask = CAShapeLayer()
    private let unfilledScanGradient = CAGradientLayer()
    private let filledScanGradient = CAGradientLayer()

    private var progress = -1.0
    private var isActive = false
    private var animationsEnabled = false
    private var isAway = false
    private var columns = 0
    private var rows = 0
    private var matrixGap = -1.0
    private var glowRadius = -1.0
    private var filledElementCount = -1
    private var accentColor = NSColor.clear
    private var style = IslandProgressStyle.matrix
    private var lastBoundsSize = CGSize.zero
    private var mode = ProgressAnimationMetrics.AnimationMode.staticDisplay
    private(set) var scanConfigurationCount = 0

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.masksToBounds = false
        configureScanContainer(unfilledScanContainer, mask: unfilledScanMask, gradient: unfilledScanGradient)
        configureScanContainer(filledScanContainer, mask: filledScanMask, gradient: filledScanGradient)
        layer?.addSublayer(unfilledLayer)
        layer?.addSublayer(filledLayer)
        layer?.addSublayer(unfilledScanContainer)
        layer?.addSublayer(filledScanContainer)
    }

    required init?(coder: NSCoder) { nil }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func layout() {
        super.layout()
        guard bounds.size != lastBoundsSize else { return }
        lastBoundsSize = bounds.size
        rebuildGeometryAndAnimations(restartAnimation: true)
    }

    func update(
        progress: Double,
        isActive: Bool,
        animationsEnabled: Bool,
        isAway: Bool,
        columns: Int,
        accentColor: NSColor,
        style: IslandProgressStyle,
        rows: Int = FocusDesign.Matrix.rows,
        matrixGap: CGFloat = FocusDesign.Matrix.gap,
        glowRadius: CGFloat = FocusDesign.Matrix.glowRadius
    ) {
        let clamped = StyledProgressView.clampedProgress(progress)
        let normalizedColumns = max(1, columns)
        let normalizedRows = max(1, rows)
        let nextFilledElementCount = Self.filledElementCount(
            progress: clamped,
            columns: normalizedColumns,
            style: style,
            rows: normalizedRows
        )
        let visibleGeometryChanged = self.filledElementCount != nextFilledElementCount
        let structuralGeometryChanged = self.columns != normalizedColumns
            || self.rows != normalizedRows
            || self.matrixGap != matrixGap
            || self.glowRadius != glowRadius
            || self.style != style
        let animationChanged = self.isActive != isActive
            || self.animationsEnabled != animationsEnabled
            || self.isAway != isAway
        let colorChanged = !self.accentColor.isEqual(accentColor)

        self.progress = clamped
        self.isActive = isActive
        self.animationsEnabled = animationsEnabled
        self.isAway = isAway
        self.columns = normalizedColumns
        self.rows = normalizedRows
        self.matrixGap = matrixGap
        self.glowRadius = glowRadius
        self.filledElementCount = nextFilledElementCount
        self.accentColor = accentColor
        self.style = style

        if visibleGeometryChanged || structuralGeometryChanged {
            // The live timer advances progress every second. Updating the mask
            // must not restart the 2.8-second scan, otherwise it can only reach
            // the midpoint before the next tick sends it back to the left.
            rebuildGeometryAndAnimations(restartAnimation: structuralGeometryChanged)
        } else {
            if colorChanged { updateColors() }
            if animationChanged { updateAnimations(force: false) }
        }
    }

    private func configureScanContainer(_ container: CALayer, mask: CAShapeLayer, gradient: CAGradientLayer) {
        container.mask = mask
        container.addSublayer(gradient)
    }

    private func rebuildGeometryAndAnimations(restartAnimation: Bool) {
        guard bounds.width > 0, bounds.height > 0 else { return }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        let paths: (filled: CGPath, unfilled: CGPath)
        switch style {
        case .matrix: paths = matrixPaths()
        case .waveform: paths = waveformPaths()
        }
        unfilledLayer.frame = bounds
        filledLayer.frame = bounds
        unfilledLayer.path = paths.unfilled
        filledLayer.path = paths.filled
        unfilledScanContainer.frame = bounds
        filledScanContainer.frame = bounds
        unfilledScanMask.frame = bounds
        filledScanMask.frame = bounds
        unfilledScanMask.path = paths.unfilled
        filledScanMask.path = paths.filled
        CATransaction.commit()
        updateColors()
        updateAnimations(force: restartAnimation)
    }

    private func matrixPaths() -> (filled: CGPath, unfilled: CGPath) {
        let filled = CGMutablePath()
        let unfilled = CGMutablePath()
        let rowCount = rows
        let gap = matrixGap
        let cellWidth = max(0, (bounds.width - CGFloat(columns - 1) * gap) / CGFloat(columns))
        let cellHeight = max(0, (bounds.height - CGFloat(rowCount - 1) * gap) / CGFloat(rowCount))
        let illuminated = filledElementCount
        for column in 0..<columns {
            for row in 0..<rowCount {
                // AppKit paths use a bottom-left origin. Keep the established
                // left-to-right column order, but count each column from its
                // visually top cell down to the bottom cell.
                let index = Self.matrixElementIndex(column: column, row: row, rows: rowCount)
                let rect = CGRect(
                    x: CGFloat(column) * (cellWidth + gap),
                    y: CGFloat(row) * (cellHeight + gap),
                    width: cellWidth,
                    height: cellHeight
                )
                let path = CGPath(
                    roundedRect: rect,
                    cornerWidth: FocusDesign.Radius.pixel,
                    cornerHeight: FocusDesign.Radius.pixel,
                    transform: nil
                )
                (index < illuminated ? filled : unfilled).addPath(path)
            }
        }
        return (filled, unfilled)
    }

    private func waveformPaths() -> (filled: CGPath, unfilled: CGPath) {
        let filled = CGMutablePath()
        let unfilled = CGMutablePath()
        let count = 37
        let illuminated = filledElementCount
        let gap = min(3.0, bounds.width / CGFloat(count * 2))
        let width = max(0, (bounds.width - CGFloat(count - 1) * gap) / CGFloat(count))
        for index in 0..<count {
            let height = bounds.height * StyledProgressView.waveformHeight(index: index, count: count)
            let rect = CGRect(
                x: CGFloat(index) * (width + gap),
                y: (bounds.height - height) / 2,
                width: width,
                height: height
            )
            let radius = min(width / 2, 3)
            let path = CGPath(roundedRect: rect, cornerWidth: radius, cornerHeight: radius, transform: nil)
            (index < illuminated ? filled : unfilled).addPath(path)
        }
        return (filled, unfilled)
    }

    private func updateColors() {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        let color = accentColor.cgColor
        filledLayer.fillColor = color
        unfilledLayer.fillColor = color
        // Status colors interpolate without changing the animation mode. Keep
        // the glow on the same interpolated color so an away-state orange
        // shadow cannot remain under working-state green cells.
        filledLayer.shadowColor = color
        if style.usesPixelProgressTreatment {
            unfilledScanGradient.colors = matrixGradient(color: accentColor, filled: false)
            filledScanGradient.colors = matrixGradient(color: accentColor, filled: true)
            let locations: [NSNumber] = [0, 0.17, 0.33, 0.5, 0.67, 0.83, 1]
            unfilledScanGradient.locations = locations
            filledScanGradient.locations = locations
        } else {
            let colors = [
                NSColor.clear.cgColor,
                NSColor.white.withAlphaComponent(0.08).cgColor,
                NSColor.white.withAlphaComponent(0.35).cgColor,
                NSColor.white.withAlphaComponent(0.08).cgColor,
                NSColor.clear.cgColor
            ]
            unfilledScanGradient.colors = colors
            filledScanGradient.colors = colors
            let locations: [NSNumber] = [0, 0.28, 0.5, 0.72, 1]
            unfilledScanGradient.locations = locations
            filledScanGradient.locations = locations
        }
        CATransaction.commit()
    }

    private func matrixGradient(color: NSColor, filled: Bool) -> [CGColor] {
        let strengths: [CGFloat] = filled
            ? [0, 0.36, 0.72, 1, 0.72, 0.36, 0]
            : [0, 0.11, 0.22, 0.33, 0.22, 0.11, 0]
        return strengths.map { color.withAlphaComponent($0).cgColor }
    }

    private func updateAnimations(force: Bool) {
        let nextMode = ProgressAnimationMetrics.animationMode(
            progress: progress,
            isActive: isActive,
            isAway: isAway,
            animationsEnabled: animationsEnabled
        )
        guard force || nextMode != mode else { return }
        mode = nextMode
        unfilledLayer.removeAnimation(forKey: Self.pulseAnimationKey)
        unfilledScanGradient.removeAnimation(forKey: Self.scanAnimationKey)
        filledScanGradient.removeAnimation(forKey: Self.scanAnimationKey)

        CATransaction.begin()
        CATransaction.setDisableActions(true)
        filledLayer.opacity = style.usesPixelProgressTreatment ? Float(FocusDesign.Opacity.activePixel) : 1
        unfilledLayer.opacity = style.usesPixelProgressTreatment ? Float(FocusDesign.Opacity.inactivePixel) : 0.18
        unfilledScanContainer.isHidden = nextMode != .scanning
        filledScanContainer.isHidden = nextMode != .scanning
        filledLayer.shadowOpacity = isActive ? Float(FocusDesign.Opacity.glow) : 0
        filledLayer.shadowColor = accentColor.cgColor
        filledLayer.shadowRadius = isActive ? glowRadius : 0
        filledLayer.shadowOffset = .zero
        CATransaction.commit()

        switch nextMode {
        case .staticDisplay:
            break
        case .awayPulse:
            let base = style.usesPixelProgressTreatment ? FocusDesign.Opacity.inactivePixel : 0.18
            let peak = style.usesPixelProgressTreatment
                ? base + FocusDesign.Opacity.awayInactivePixel
                : 0.35
            let pulse = CABasicAnimation(keyPath: "opacity")
            pulse.fromValue = base
            pulse.toValue = peak
            pulse.duration = FocusDesign.Motion.awayPulseDuration / 2
            pulse.autoreverses = true
            pulse.repeatCount = .infinity
            pulse.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            unfilledLayer.add(pulse, forKey: Self.pulseAnimationKey)
        case .scanning:
            configureScanAnimation()
        }
    }

    private func configureScanAnimation() {
        scanConfigurationCount += 1
        let bandWidth = max(24, bounds.width * (style.usesPixelProgressTreatment ? 0.18 : 0.16))
        let start = -bandWidth / 2
        let end = bounds.width + bandWidth / 2
        for gradient in [unfilledScanGradient, filledScanGradient] {
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            gradient.startPoint = CGPoint(x: 0, y: 0.5)
            gradient.endPoint = CGPoint(x: 1, y: 0.5)
            gradient.frame = CGRect(x: -bandWidth, y: 0, width: bandWidth, height: bounds.height)
            CATransaction.commit()
            let scan = CABasicAnimation(keyPath: "position.x")
            scan.fromValue = start
            scan.toValue = end
            scan.duration = FocusDesign.Motion.scanDuration
            scan.repeatCount = .infinity
            scan.timingFunction = CAMediaTimingFunction(name: .linear)
            gradient.add(scan, forKey: Self.scanAnimationKey)
        }
    }

    var scanAnimation: CABasicAnimation? {
        unfilledScanGradient.animation(forKey: Self.scanAnimationKey) as? CABasicAnimation
    }

    var pulseAnimation: CABasicAnimation? {
        unfilledLayer.animation(forKey: Self.pulseAnimationKey) as? CABasicAnimation
    }

    var hasFilledGeometry: Bool { !(filledLayer.path?.isEmpty ?? true) }
    var hasUnfilledGeometry: Bool { !(unfilledLayer.path?.isEmpty ?? true) }
    var filledShadowColor: CGColor? { filledLayer.shadowColor }

    static func filledElementCount(
        progress: Double,
        columns: Int,
        style: IslandProgressStyle,
        rows: Int = FocusDesign.Matrix.rows
    ) -> Int {
        let progress = StyledProgressView.clampedProgress(progress)
        switch style {
        case .matrix:
            return Int((Double(max(1, columns) * max(1, rows)) * progress).rounded(.down))
        case .waveform:
            return StyledProgressView.filledCells(progress: progress, count: 37)
        }
    }

    static func matrixElementIndex(column: Int, row: Int, rows: Int) -> Int {
        column * rows + (rows - 1 - row)
    }
}

private extension IslandProgressStyle {
    var usesPixelProgressTreatment: Bool {
        switch self {
        case .matrix: return true
        case .waveform: return false
        }
    }
}
