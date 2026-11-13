import AppKit
import QuartzCore

@MainActor
final class StatusItemProgressOverlayView: NSView {
    static let scanAnimationKey = "WorkIsland.statusProgressScan"
    static let pulseAnimationKey = "WorkIsland.statusProgressPulse"

    private let scanContainer = CALayer()
    private let scanMask = CAShapeLayer()
    private let scanGradient = CAGradientLayer()
    private let awayLayer = CAShapeLayer()
    private var litCount = -1
    private var currentColor = NSColor.clear
    private var mode = ProgressAnimationMetrics.AnimationMode.staticDisplay
    private var lastBoundsSize = CGSize.zero
    private var awayElapsed: TimeInterval?

    static func overlayFrame(in buttonBounds: NSRect, flipped: Bool) -> NSRect {
        let totalWidth = CGFloat(FocusDesign.Matrix.menuBarColumns) * FocusDesign.Matrix.menuBarPixelWidth
            + CGFloat(FocusDesign.Matrix.menuBarColumns - 1) * FocusDesign.Matrix.menuBarPixelGap
        let progressY: CGFloat
        if flipped {
            progressY = buttonBounds.midY + FocusDesign.AppKitToken.statusArtworkHeight / 2
                - FocusDesign.AppKitToken.statusProgressBottom
                - FocusDesign.Matrix.menuBarPixelHeight
        } else {
            progressY = buttonBounds.midY - FocusDesign.AppKitToken.statusArtworkHeight / 2
                + FocusDesign.AppKitToken.statusProgressBottom
        }
        return NSRect(
            x: buttonBounds.midX - totalWidth / 2 + FocusDesign.AppKitToken.statusContentOffsetX,
            y: progressY,
            width: totalWidth,
            height: FocusDesign.Matrix.menuBarPixelHeight
        )
    }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.masksToBounds = false
        scanContainer.mask = scanMask
        scanContainer.addSublayer(scanGradient)
        layer?.addSublayer(scanContainer)
        layer?.addSublayer(awayLayer)
        setAccessibilityElement(false)
    }

    required init?(coder: NSCoder) { nil }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func layout() {
        super.layout()
        guard bounds.size != lastBoundsSize else { return }
        lastBoundsSize = bounds.size
        rebuildGeometry()
        updateAnimations(force: true)
    }

    func update(
        progress: Double,
        color: NSColor,
        mode: ProgressAnimationMetrics.AnimationMode,
        awayElapsed: TimeInterval?
    ) {
        let nextLitCount = StatusItemArtwork.litCount(progress: progress)
        let geometryChanged = nextLitCount != litCount
        let colorChanged = !currentColor.isEqual(color)
        let animationChanged = self.mode != mode
        litCount = nextLitCount
        currentColor = color
        self.mode = mode
        self.awayElapsed = awayElapsed
        if geometryChanged { rebuildGeometry() }
        if colorChanged { updateColors() }
        if animationChanged { updateAnimations(force: false) }
    }

    private func rebuildGeometry() {
        guard bounds.width > 0, bounds.height > 0 else { return }
        let all = CGMutablePath()
        let unfilled = CGMutablePath()
        for column in 0..<FocusDesign.Matrix.menuBarColumns {
            let rect = CGRect(
                x: CGFloat(column) * (FocusDesign.Matrix.menuBarPixelWidth + FocusDesign.Matrix.menuBarPixelGap),
                y: 0,
                width: FocusDesign.Matrix.menuBarPixelWidth,
                height: FocusDesign.Matrix.menuBarPixelHeight
            )
            let cell = CGPath(
                roundedRect: rect,
                cornerWidth: FocusDesign.Radius.pixel,
                cornerHeight: FocusDesign.Radius.pixel,
                transform: nil
            )
            all.addPath(cell)
            if column >= litCount { unfilled.addPath(cell) }
        }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        scanContainer.frame = bounds
        scanMask.frame = bounds
        scanMask.path = all
        awayLayer.frame = bounds
        awayLayer.path = unfilled
        CATransaction.commit()
        updateColors()
    }

    private func updateColors() {
        let strengths: [CGFloat] = [0, 0.10, 0.20, 0.30, 0.20, 0.10, 0]
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        scanGradient.colors = strengths.map { currentColor.withAlphaComponent($0).cgColor }
        scanGradient.locations = [0, 0.17, 0.33, 0.5, 0.67, 0.83, 1]
        scanGradient.startPoint = CGPoint(x: 0, y: 0.5)
        scanGradient.endPoint = CGPoint(x: 1, y: 0.5)
        awayLayer.fillColor = currentColor.cgColor
        CATransaction.commit()
    }

    private func updateAnimations(force: Bool) {
        if force {
            scanGradient.removeAnimation(forKey: Self.scanAnimationKey)
            awayLayer.removeAnimation(forKey: Self.pulseAnimationKey)
        }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        scanContainer.isHidden = mode != .scanning
        awayLayer.isHidden = mode != .awayPulse
        awayLayer.opacity = 0
        CATransaction.commit()

        switch mode {
        case .staticDisplay:
            scanGradient.removeAnimation(forKey: Self.scanAnimationKey)
            awayLayer.removeAnimation(forKey: Self.pulseAnimationKey)
        case .scanning:
            awayLayer.removeAnimation(forKey: Self.pulseAnimationKey)
            guard scanGradient.animation(forKey: Self.scanAnimationKey) == nil else { return }
            let bandWidth = max(14, bounds.width * 0.26)
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            scanGradient.frame = CGRect(x: -bandWidth, y: 0, width: bandWidth, height: bounds.height)
            CATransaction.commit()
            let scan = CABasicAnimation(keyPath: "position.x")
            scan.fromValue = -bandWidth / 2
            scan.toValue = bounds.width + bandWidth / 2
            scan.duration = FocusDesign.Motion.scanDuration
            scan.repeatCount = .infinity
            scan.timingFunction = CAMediaTimingFunction(name: .linear)
            scanGradient.add(scan, forKey: Self.scanAnimationKey)
        case .awayPulse:
            scanGradient.removeAnimation(forKey: Self.scanAnimationKey)
            guard awayLayer.animation(forKey: Self.pulseAnimationKey) == nil else { return }
            let pulse = CABasicAnimation(keyPath: "opacity")
            pulse.fromValue = 0
            pulse.toValue = FocusDesign.Opacity.awayInactivePixel
            pulse.duration = FocusDesign.Motion.awayPulseDuration / 2
            pulse.autoreverses = true
            pulse.repeatCount = .infinity
            pulse.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            if let awayElapsed, awayElapsed > 0 {
                pulse.beginTime = CACurrentMediaTime()
                    - awayElapsed.truncatingRemainder(dividingBy: FocusDesign.Motion.awayPulseDuration)
            }
            awayLayer.add(pulse, forKey: Self.pulseAnimationKey)
        }
    }

    var scanAnimation: CABasicAnimation? {
        scanGradient.animation(forKey: Self.scanAnimationKey) as? CABasicAnimation
    }

    var pulseAnimation: CABasicAnimation? {
        awayLayer.animation(forKey: Self.pulseAnimationKey) as? CABasicAnimation
    }
}

@MainActor
enum StatusItemArtwork {
    static func backgroundPath(in rect: NSRect) -> NSBezierPath {
        let radius = min(rect.width, rect.height) * FocusDesign.AppKitToken.statusCapsuleRadiusRatio
        return NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
    }

    static func gradientColors(for state: FocusDesign.Status.VisualState) -> [NSColor] {
        [
            FocusDesign.AppKitToken.statusGradientStart,
            FocusDesign.AppKitToken.statusGradientEnd(for: state)
        ]
    }

    private static func gradientColors(
        for state: FocusDesign.Status.VisualState,
        accent: NSColor
    ) -> [NSColor] {
        let fraction: CGFloat
        switch state {
        case .tracking: fraction = 0.18
        case .away: fraction = 0.24
        case .paused: fraction = 0.16
        }
        return [
            FocusDesign.AppKitToken.statusGradientStart,
            NSColor.black.blended(withFraction: fraction, of: accent) ?? NSColor.black
        ]
    }

    static func drawBackground(
        in rect: NSRect,
        style: IslandBackgroundStyle,
        visualState: FocusDesign.Status.VisualState,
        accent: NSColor,
        reduceTransparency: Bool
    ) {
        let path = backgroundPath(in: rect)
        switch style {
        case .standard:
            NSGradient(colors: gradientColors(for: visualState, accent: accent))?
                .draw(in: path, angle: -45)
        case .glass, .liquidGlass:
            if reduceTransparency {
                NSColor(calibratedWhite: 0.12, alpha: 1).setFill()
                path.fill()
            } else {
                // Keep the complete surface in the fixed NSImage so an
                // auto-hidden menu bar cannot collapse its geometry. The menu
                // bar already supplies the system backdrop; this translucent
                // neutral wash and the two localized reflection bands reproduce
                // the accepted small-capsule glass treatment without a child
                // material view or a uniform white/black outline.
                NSGradient(colors: [
                    NSColor.white.withAlphaComponent(0.16),
                    NSColor.white.withAlphaComponent(0.07),
                    NSColor.black.withAlphaComponent(0.12)
                ])?.draw(in: path, angle: -45)
                drawGlassHighlights(in: rect)
            }
        }
    }

    private static func drawGlassHighlights(in rect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        let scale: CGFloat = 0.65
        let radius = min(rect.width, rect.height) * FocusDesign.AppKitToken.statusCapsuleRadiusRatio
        for (width, strength) in [
            (GlassAppearanceMetrics.edgeShoulderWidth, CGFloat(0.18)),
            (GlassAppearanceMetrics.edgeHighlightWidth, CGFloat(0.72))
        ] {
            let lineWidth = width * scale
            let inset = lineWidth / 2 + 0.35 * scale
            let highlightRect = rect.insetBy(dx: inset, dy: inset)
            let highlightPath = CGPath(
                roundedRect: highlightRect,
                cornerWidth: max(0, radius - inset),
                cornerHeight: max(0, radius - inset),
                transform: nil
            )
            let stroke = highlightPath.copy(
                strokingWithWidth: lineWidth,
                lineCap: .round,
                lineJoin: .round,
                miterLimit: 10
            )
            let colors = [strength, strength * 0.7, 0, 0, strength * 0.5].map {
                NSColor.white.withAlphaComponent($0).cgColor
            } as CFArray
            guard let gradient = CGGradient(
                colorsSpace: CGColorSpaceCreateDeviceRGB(),
                colors: colors,
                locations: [0, 0.18, 0.42, 0.68, 1]
            ) else { continue }
            context.saveGState()
            context.addPath(stroke)
            context.clip()
            context.drawLinearGradient(
                gradient,
                start: CGPoint(x: rect.minX, y: rect.maxY),
                end: CGPoint(x: rect.maxX, y: rect.minY),
                options: []
            )
            context.restoreGState()
        }
    }

    static func litCount(progress: Double) -> Int {
        Int((Double(FocusDesign.Matrix.menuBarColumns) * min(max(progress, 0), 1)).rounded(.down))
    }

    static func animationMode(
        progress: Double,
        state: TrackingEngine.State,
        animationsEnabled: Bool
    ) -> ProgressAnimationMetrics.AnimationMode {
        let isTracking: Bool
        if case .tracking = state { isTracking = true } else { isTracking = false }
        return ProgressAnimationMetrics.animationMode(
            progress: progress,
            isActive: isTracking,
            isAway: state == .away,
            animationsEnabled: animationsEnabled
        )
    }

    static func pixelOpacity(
        column: Int,
        litCount: Int,
        columns: Int,
        mode: ProgressAnimationMetrics.AnimationMode,
        timestamp: TimeInterval,
        awayElapsed: TimeInterval? = nil
    ) -> CGFloat {
        ProgressAnimationMetrics.cellOpacity(isFilled: column < litCount, column: column,
            columns: columns, mode: mode, timestamp: timestamp, awayElapsed: awayElapsed)
    }

    static func make(
        time: String,
        progress: Double,
        color: NSColor,
        mode: ProgressAnimationMetrics.AnimationMode,
        timestamp: TimeInterval,
        awayElapsed: TimeInterval? = nil,
        backgroundStyle: IslandBackgroundStyle = .standard,
        visualState: FocusDesign.Status.VisualState = .tracking,
        reduceTransparency: Bool = false
    ) -> NSImage {
        let size = NSSize(
            width: FocusDesign.AppKitToken.statusArtworkWidth,
            height: FocusDesign.AppKitToken.statusArtworkHeight
        )
        let image = NSImage(size: size, flipped: false) { rect in
            drawBackground(
                in: rect,
                style: backgroundStyle,
                visualState: visualState,
                accent: color,
                reduceTransparency: reduceTransparency
            )
            drawTime(time, in: rect)
            let columns = FocusDesign.Matrix.menuBarColumns
            let totalWidth = CGFloat(columns) * FocusDesign.Matrix.menuBarPixelWidth
                + CGFloat(columns - 1) * FocusDesign.Matrix.menuBarPixelGap
            let originX = rect.midX - totalWidth / 2 + FocusDesign.AppKitToken.statusContentOffsetX
            let lit = litCount(progress: progress)
            for column in 0..<columns {
                let dot = NSRect(
                    x: originX + CGFloat(column) * (FocusDesign.Matrix.menuBarPixelWidth + FocusDesign.Matrix.menuBarPixelGap),
                    y: FocusDesign.AppKitToken.statusProgressBottom,
                    width: FocusDesign.Matrix.menuBarPixelWidth,
                    height: FocusDesign.Matrix.menuBarPixelHeight
                )
                let opacity = pixelOpacity(
                    column: column,
                    litCount: lit,
                    columns: columns,
                    mode: mode,
                    timestamp: timestamp, awayElapsed: awayElapsed
                )
                NSGraphicsContext.saveGraphicsState()
                if opacity > FocusDesign.Opacity.inactivePixel {
                    let shadow = NSShadow()
                    shadow.shadowColor = color.withAlphaComponent(opacity * FocusDesign.Opacity.glow)
                    shadow.shadowBlurRadius = FocusDesign.AppKitToken.statusGlowRadius
                    shadow.shadowOffset = .zero
                    shadow.set()
                }
                color.withAlphaComponent(opacity).setFill()
                NSBezierPath(roundedRect: dot, xRadius: FocusDesign.Radius.pixel, yRadius: FocusDesign.Radius.pixel).fill()
                NSGraphicsContext.restoreGraphicsState()
            }
            return true
        }
        image.isTemplate = false
        return image
    }

    static func drawTime(_ text: String, in rect: NSRect) {
        let attributes: [NSAttributedString.Key: Any] = [
            .font: FocusDesign.AppKitToken.statusTextFont,
            .kern: FocusDesign.AppKitToken.statusTextKerning,
            .foregroundColor: FocusDesign.AppKitToken.statusTextColor
        ]
        let attributed = NSAttributedString(string: text, attributes: attributes)
        let textSize = attributed.size()
        attributed.draw(at: NSPoint(
            x: rect.midX - textSize.width / 2 + FocusDesign.AppKitToken.statusContentOffsetX,
            y: FocusDesign.AppKitToken.statusTextBottom
        ))
    }
}
