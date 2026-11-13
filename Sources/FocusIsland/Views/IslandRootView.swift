import AppKit
import SwiftUI

struct IslandRootView: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @ObservedObject var engine: TrackingEngine
    @ObservedObject private var settings: TrackingSettings
    @ObservedObject var presentation: IslandPresentationModel
    @ObservedObject private var glassAppearance = GlassBackdropAppearance.shared
    let onHoverChanged: (Bool) -> Void

    @State private var editingTitle = false
    @State private var titleDraft = ""
    @FocusState private var titleFocused: Bool
    @State private var orbBounds = CGRect.zero
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(
        engine: TrackingEngine,
        presentation: IslandPresentationModel,
        onHoverChanged: @escaping (Bool) -> Void
    ) {
        self.engine = engine
        settings = engine.settings
        self.presentation = presentation
        self.onHoverChanged = onHoverChanged
    }

    @State private var finishingOnboarding = false

    var body: some View {
        islandContent
            .frame(width: scaledWindowSize.width * presentation.reminderScale, height: scaledWindowSize.height * presentation.reminderScale)
    }

    private var islandContent: some View {
        ZStack {
            if presentation.isOnboardingPresented {
                OnboardingView(settings: settings, state: presentation.onboardingState,
                    finish: {
                        finishingOnboarding = true
                        NotificationCenter.default.post(name: .finishWorkIslandOnboarding, object: nil)
                    })
                    .scaleEffect(settings.islandScale * presentation.reminderScale, anchor: .topLeading)
                    .frame(width: scaledWindowSize.width * presentation.reminderScale, height: scaledWindowSize.height * presentation.reminderScale, alignment: .topLeading)
                    .transition(reduceMotion ? .identity : .asymmetric(insertion: .opacity, removal: .scale(scale: 1.12).combined(with: .opacity)))
            } else if presentation.isExpanded {
                ExpandedDashboardView(engine: engine, presentation: presentation)
                    .frame(width: FocusDesign.Window.baseWidth, height: FocusDesign.Window.baseHeight)
                    .scaleEffect(settings.islandScale * presentation.reminderScale, anchor: .topLeading)
                    .frame(
                        width: scaledWindowSize.width * presentation.reminderScale,
                        height: scaledWindowSize.height * presentation.reminderScale,
                        alignment: .topLeading
                    )
                    .transition(presentation.isMenuBarPresentation
                        ? .identity : .move(edge: .trailing).combined(with: .opacity))
            } else {
                StatusColorTransitionView(components: targetAccentComponents) { color in
                    compactHeader(accentColor: color)
                }
                    .textRenderer(GlassTextRenderer(enabled: GlassTextRenderer.shouldEnable(
                        isGlass: settings.backgroundStyle.isGlass, isExpanded: presentation.isExpanded,
                        isOnboarding: presentation.isOnboardingPresented), darkForeground: usesDarkGlassForeground))
                    .transition(presentation.isMenuBarPresentation ? .identity
                        : finishingOnboarding
                            ? (reduceMotion ? .identity : .scale(scale: 0.82).combined(with: .opacity))
                            : .move(edge: .leading).combined(with: .opacity))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .preferredColorScheme(.dark)
        .environment(\.colorScheme, .dark)
        .environment(\.whiteSubpageForeground, settings.backgroundStyle.isGlass && presentation.isExpanded && !presentation.isOnboardingPresented)
        .environment(\.darkGlassForeground, usesDarkGlassForeground)
        .background(background)
        .overlay {
            if let preview = engine.reminderPreviewStartedAt, !presentation.isOnboardingPresented {
                SuperReminderView(startedAt: preview, threshold: settings.awayThreshold, preview: true)
            } else if settings.superReminderEnabled, engine.progress < 1, engine.isAway, !presentation.isOnboardingPresented,
               let startedAt = engine.superReminderStartedAt {
                SuperReminderView(startedAt: startedAt, threshold: settings.awayThreshold)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: FocusDesign.Window.cornerRadius * settings.islandScale * presentation.reminderScale, style: .continuous))
        .overlay {
            if settings.backgroundStyle == .standard || presentation.isOnboardingPresented {
                RoundedRectangle(cornerRadius: FocusDesign.Window.cornerRadius * settings.islandScale * presentation.reminderScale, style: .continuous)
                    .stroke(FocusDesign.ColorToken.border, lineWidth: FocusDesign.Window.borderWidth)
            }
        }
        .animation(Self.shouldAnimateRootTransition(
            reduceMotion: reduceMotion,
            isMenuBarPresentation: presentation.isMenuBarPresentation
        ) ? FocusDesign.Motion.navigation : nil,
                   value: presentation.isExpanded)
        .animation(reduceMotion ? nil : .easeOut(duration: 0.38), value: presentation.isOnboardingPresented)
        .onChange(of: presentation.isExpanded) { _, value in if value { finishingOnboarding = false } }
        .onChange(of: presentation.isOnboardingPresented) { _, value in if value { finishingOnboarding = false } }
        .animation(reduceMotion ? nil : FocusDesign.Motion.statusTransition,
                   value: FocusDesign.Status.visualState(for: engine.state))
        .animation(reduceMotion ? nil : .easeInOut(duration: GlassBlurMetrics.foregroundTransitionDuration),
                   value: usesDarkGlassForeground)
        .onHover(perform: onHoverChanged)
        .contentShape(Rectangle())
        .coordinateSpace(name: "islandWindow")
        .gesture(windowDrag)
        .alert(L10n.text("计时记录未保存"), isPresented: Binding(
            get: { engine.persistenceError != nil },
            set: { if !$0 { engine.clearPersistenceError() } }
        )) {
            Button(L10n.text("知道了")) { engine.clearPersistenceError() }
        } message: {
            Text(engine.persistenceError ?? "")
        }
    }

    @ViewBuilder
    private func compactHeader(accentColor: Color) -> some View {
        if settings.islandScale < FocusDesign.Window.adaptiveThreshold && !presentation.isExpanded {
            HStack(spacing: FocusDesign.Spacing.medium * presentation.reminderScale) {
                statusOrb(accentColor: accentColor)
                    .frame(width: FocusDesign.Size.smallStatusOrb * presentation.reminderScale, height: FocusDesign.Size.smallStatusOrb * presentation.reminderScale)
                VStack(alignment: .leading, spacing: FocusDesign.Spacing.small * presentation.reminderScale) {
                    Text(TimeFormatting.compact(engine.displayedTimerSeconds, style: engine.displayedTimerStyle))
                        .font(.system(size: FocusDesign.Typography.label * presentation.reminderScale, weight: .bold, design: .monospaced)).foregroundStyle(accentColor)
                    StyledProgressView(progress: engine.progress, isActive: engine.isTracking, animationsEnabled: !reduceMotion, isAway: engine.isAwayBreathing, columns: ProgressAnimationMetrics.columnCount(forScale: settings.islandScale), accentColor: accentColor, style: settings.progressStyle, awayBreathingStartedAt: engine.awayBreathingStartedAt).frame(height: FocusDesign.Size.compactProgressHeight * presentation.reminderScale)
                }.offset(y: -2)
            }
            .padding(.horizontal, FocusDesign.Spacing.xLarge * presentation.reminderScale)
            .frame(maxHeight: .infinity)
        } else {
            HStack(spacing: FocusDesign.Spacing.large * presentation.reminderScale) {
                statusOrb(accentColor: accentColor)

                VStack(alignment: .leading, spacing: FocusDesign.Spacing.xxSmall * presentation.reminderScale) {
                    HStack {
                        titleView
                            .font(.system(size: FocusDesign.Typography.title * presentation.reminderScale, weight: .semibold, design: .rounded))
                            .foregroundStyle(usesDarkGlassForeground ? .black : (settings.backgroundStyle.isGlass ? FocusDesign.ColorToken.glassTimerTitle : FocusDesign.ColorToken.textPrimary))
                            .opacity(FocusDesign.Opacity.mainTitle(isAway: engine.isAway, isGlass: settings.backgroundStyle.isGlass))
                        Spacer()
                        Text(TimeFormatting.compact(engine.displayedTimerSeconds, style: engine.displayedTimerStyle))
                            .font(.system(size: FocusDesign.Typography.title * presentation.reminderScale, weight: .bold, design: .monospaced))
                            .foregroundStyle(accentColor)
                    }
                    StyledProgressView(
                        progress: engine.progress,
                        isActive: engine.isTracking,
                        animationsEnabled: !reduceMotion,
                        isAway: engine.isAwayBreathing,
                        columns: FocusDesign.Matrix.fullColumns,
                        accentColor: accentColor, style: settings.progressStyle,
                        awayBreathingStartedAt: engine.awayBreathingStartedAt
                    )
                    .frame(height: FocusDesign.Size.progressHeight * presentation.reminderScale)
                }.offset(y: -2)

            }
            .padding(.horizontal, FocusDesign.Spacing.mainHorizontal * presentation.reminderScale)
            .frame(maxHeight: .infinity)
        }
    }

    private var usesDarkGlassForeground: Bool {
        settings.backgroundStyle.usesCapturedGlass && glassAppearance.usesDarkText
            && !presentation.isOnboardingPresented && !presentation.isMenuBarPresentation
    }

    private func statusOrb(accentColor: Color) -> some View {
        Button { openNavigation() } label: {
            ZStack {
                Circle()
                    .fill(isManuallyPaused ? FocusDesign.ColorToken.pausedSurface : accentColor)
                    .shadow(color: isManuallyPaused ? .clear : accentColor.opacity(FocusDesign.Opacity.activeOrbGlow), radius: FocusDesign.Radius.control)
                LayerAnimatedStatusIcon(
                    symbolName: statusIcon,
                    pointSize: FocusDesign.Typography.label * presentation.reminderScale,
                    color: isManuallyPaused ? NSColor(FocusDesign.ColorToken.textSecondary) : .black,
                    pulse: reduceMotion ? .none : isManuallyPaused ? .paused : engine.isAway ? .away : .none
                )
            }
        }
        .buttonStyle(.plain)
        .contentShape(Circle())
        .help(L10n.text("打开导航"))
        .frame(width: FocusDesign.Size.statusOrb * presentation.reminderScale, height: FocusDesign.Size.statusOrb * presentation.reminderScale)
        .onGeometryChange(for: CGRect.self) { $0.frame(in: .named("islandWindow")) } action: { orbBounds = $0 }
    }

    @ViewBuilder private var titleView: some View {
        if editingTitle {
            TextField("", text: $titleDraft)
                .textFieldStyle(.plain).focused($titleFocused)
                .onSubmit { finishTitleEditing() }
                .onExitCommand { cancelTitleEditing() }
                .onAppear { titleFocused = true }
                .onChange(of: titleFocused) { _, focused in
                    if !focused && editingTitle { cancelTitleEditing() }
                }
        } else {
            Text(settings.displayTitle).lineLimit(1).minimumScaleFactor(0.65)
                .contentShape(Rectangle())
                .onTapGesture(count: 3) {
                    titleDraft = settings.titleEditingDraft
                    editingTitle = true
                }
                .help(settings.language == .english ? "Triple-click to rename" : "三击自定义标题")
        }
    }

    private var windowDrag: some Gesture {
        DragGesture(minimumDistance: 2).onChanged { value in
            guard !editingTitle, let event = NSApp.currentEvent, let window = event.window else { return }
            if !presentation.isExpanded && !presentation.isOnboardingPresented {
                let center = CGPoint(x: orbBounds.midX, y: orbBounds.midY)
                let radius = orbBounds.width / 2
                guard hypot(value.startLocation.x - center.x, value.startLocation.y - center.y) > radius else { return }
            }
            // FocusPanel tracks pointer events directly, without system edge tiling.
            window.performDrag(with: event)
        }
    }

    private var background: some View {
        ZStack {
            if presentation.isOnboardingPresented {
                Color(red: 0.025, green: 0.06, blue: 0.04)
            } else if settings.backgroundStyle.isGlass {
                // The AppKit simulated backdrop sits below this clear hosting view.
                Color.black.opacity(GlassAppearanceMetrics.subpageShade(
                    isExpanded: presentation.isExpanded, isOnboarding: presentation.isOnboardingPresented))
            } else {
                VisualEffectView(material: .hudWindow, blendingMode: .behindWindow)
                LinearGradient(
                    colors: [FocusDesign.ColorToken.backgroundStart, FocusDesign.ColorToken.backgroundEnd],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
                LinearGradient(
                    colors: [FocusDesign.ColorToken.warningWashStart, FocusDesign.ColorToken.warningWashEnd],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
                .opacity(engine.isAway ? 1 : 0)
                LinearGradient(
                    colors: [FocusDesign.ColorToken.pausedWashStart, FocusDesign.ColorToken.pausedWashEnd],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
                .opacity(isManuallyPaused ? 1 : 0)
            }
        }
    }



    private var targetAccentColor: Color {
        FocusDesign.Status.color(for: engine.state, trackingAccent: settings.activeTheme?.accent.color)
    }

    private var targetAccentComponents: StatusColorComponents {
        StatusColorComponents(targetAccentColor)
    }

    private var isManuallyPaused: Bool {
        FocusDesign.Status.isManuallyPaused(engine.state)
    }

    private var statusIcon: String {
        FocusDesign.Status.icon(for: engine.state)
    }

    private var scaledWindowSize: CGSize {
        FocusDesign.Window.scaledSize(for: settings.islandScale)
    }

    static func shouldAnimateRootTransition(reduceMotion: Bool, isMenuBarPresentation: Bool) -> Bool {
        !reduceMotion && !isMenuBarPresentation
    }

    private func openNavigation() {
        cancelTitleEditing()
        let animation: Animation? = reduceMotion ? nil : FocusDesign.Motion.navigation
        withAnimation(animation) {
            presentation.showNavigation()
        }
    }

    private func finishTitleEditing() {
        settings.saveTitle(titleDraft)
        titleFocused = false
        editingTitle = false
    }

    private func cancelTitleEditing() {
        editingTitle = false
        titleFocused = false
        titleDraft = settings.titleEditingDraft
    }

}

enum StatusIconPulse: Equatable {
    case none
    case paused
    case away

    var targetScale: CGFloat {
        switch self {
        case .none: 1
        case .paused: FocusDesign.Motion.pausePulseScale
        case .away: FocusDesign.Motion.awayIconScale
        }
    }

    var duration: TimeInterval {
        switch self {
        case .none: 0
        case .paused: FocusDesign.Motion.pausePulseDuration
        case .away: FocusDesign.Motion.awayPulseDuration
        }
    }
}

/// Runs the always-on orb pulse entirely in Core Animation. The compositor
/// keeps full display-rate motion without asking SwiftUI to lay out the whole
/// island on every frame.
struct LayerAnimatedStatusIcon: NSViewRepresentable {
    let symbolName: String
    let pointSize: CGFloat
    let color: NSColor
    let pulse: StatusIconPulse

    func makeNSView(context: Context) -> LayerAnimatedStatusIconView {
        LayerAnimatedStatusIconView()
    }

    func updateNSView(_ view: LayerAnimatedStatusIconView, context: Context) {
        view.update(symbolName: symbolName, pointSize: pointSize, color: color, pulse: pulse)
    }
}

final class LayerAnimatedStatusIconView: NSView {
    static let pulseAnimationKey = "WorkIsland.statusIconPulse"
    /// AppKit owns an NSView backing layer's geometry and can restore its
    /// anchor after layout. Keep the animated geometry on our own layer so the
    /// ring center remains the real scale origin throughout the pulse.
    private let pulseLayer = CALayer()
    private var symbolName = ""
    private var pointSize: CGFloat = 0
    private var symbolColor = NSColor.clear
    private var renderedSize = CGSize.zero
    private var pulse: StatusIconPulse = .none

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        pulseLayer.contentsGravity = .resize
        layer?.addSublayer(pulseLayer)
    }

    required init?(coder: NSCoder) { nil }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func layout() {
        super.layout()
        centerPulseAnchor()
        if renderedSize != bounds.size { renderSymbol() }
    }

    private func centerPulseAnchor() {
        let ringCenter = CGPoint(x: bounds.midX, y: bounds.midY)
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        pulseLayer.bounds = CGRect(origin: .zero, size: bounds.size)
        pulseLayer.anchorPoint = CGPoint(x: 0.5, y: 0.5)
        pulseLayer.position = ringCenter
        CATransaction.commit()
    }

    private func renderSymbol() {
        renderedSize = bounds.size
        guard bounds.width > 0, bounds.height > 0,
              let image = NSImage(systemSymbolName: symbolName, accessibilityDescription: nil)?
                .withSymbolConfiguration(.init(pointSize: pointSize, weight: .bold)) else {
            pulseLayer.contents = nil
            return
        }
        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        guard let bitmap = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: max(1, Int(ceil(bounds.width * scale))),
            pixelsHigh: max(1, Int(ceil(bounds.height * scale))),
            bitsPerSample: 8,
            samplesPerPixel: 4,
            hasAlpha: true,
            isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: 0,
            bitsPerPixel: 0
        ), let context = NSGraphicsContext(bitmapImageRep: bitmap) else { return }
        bitmap.size = bounds.size
        let offset = Self.opticalOffset(symbolName: symbolName, pointSize: pointSize)
        let imageRect = NSRect(
            x: (bounds.width - image.size.width) / 2 + offset.x,
            y: (bounds.height - image.size.height) / 2 + offset.y,
            width: image.size.width,
            height: image.size.height
        )
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = context
        context.cgContext.scaleBy(x: scale, y: scale)
        context.imageInterpolation = .high
        image.draw(in: imageRect)
        symbolColor.setFill()
        imageRect.fill(using: .sourceAtop)
        context.flushGraphics()
        NSGraphicsContext.restoreGraphicsState()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        pulseLayer.contents = bitmap.cgImage
        pulseLayer.contentsScale = scale
        CATransaction.commit()
    }

    static func opticalOffset(symbolName: String, pointSize: CGFloat) -> NSPoint {
        guard symbolName == "cup.and.saucer.fill" else { return .zero }
        let scale = pointSize / FocusDesign.Typography.label
        // The saucer and right-side handle make the symbol's visible mass sit
        // slightly low/right despite a geometrically centered image rect.
        return NSPoint(x: -0.5 * scale, y: 0.5 * scale)
    }

    func update(symbolName: String, pointSize: CGFloat, color: NSColor, pulse: StatusIconPulse) {
        if self.symbolName != symbolName || self.pointSize != pointSize || self.symbolColor != color {
            self.symbolName = symbolName
            self.pointSize = pointSize
            self.symbolColor = color
            renderedSize = .zero
            layoutSubtreeIfNeeded()
            renderSymbol()
        }
        guard self.pulse != pulse else { return }
        self.pulse = pulse
        layoutSubtreeIfNeeded()
        centerPulseAnchor()
        pulseLayer.removeAnimation(forKey: Self.pulseAnimationKey)
        pulseLayer.setAffineTransform(.identity)
        guard pulse != .none else { return }
        let animation = CABasicAnimation(keyPath: "transform.scale")
        animation.fromValue = 1
        animation.toValue = pulse.targetScale
        animation.duration = pulse.duration
        animation.autoreverses = true
        animation.repeatCount = .infinity
        animation.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        pulseLayer.add(animation, forKey: Self.pulseAnimationKey)
    }

    var pulseAnimation: CABasicAnimation? {
        pulseLayer.animation(forKey: Self.pulseAnimationKey) as? CABasicAnimation
    }

    var pulseContainerFrame: NSRect { pulseLayer.frame }
    var pulseAnchorPoint: CGPoint { pulseLayer.anchorPoint }
    var pulseAnchorInRoot: CGPoint { pulseLayer.position }
}

private extension TrackingEngine {
    var isTracking: Bool {
        if case .tracking = state { return true }
        return false
    }

}

struct VisualEffectView: NSViewRepresentable {
    let material: NSVisualEffectView.Material
    let blendingMode: NSVisualEffectView.BlendingMode

    func makeNSView(context: Context) -> NSVisualEffectView {
        let view = NSVisualEffectView()
        view.material = material
        view.blendingMode = blendingMode
        view.state = .active
        return view
    }

    func updateNSView(_ nsView: NSVisualEffectView, context: Context) {}
}
