import AppKit
import Combine
import MetalKit
import OSLog
@preconcurrency import ScreenCaptureKit

enum IslandBackgroundStyle: String, CaseIterable, Identifiable {
    case standard
    case glass
    case liquidGlass = "capturedGlass"
    var id: String { rawValue }
    var isGlass: Bool { self != .standard }
    var usesCapturedGlass: Bool { self == .liquidGlass }
    // Keep the system shadow off to avoid a dark silhouette hairline.
    var usesWindowShadow: Bool { self == .standard }
    var title: String {
        switch self {
        case .standard: L10n.text("黑色")
        case .glass: L10n.text("透明")
        case .liquidGlass: L10n.text("液态玻璃")
        }
    }
    static func restored(_ value: String?) -> Self {
        switch value {
        case "liquidGlass", "stableLiquidGlass", "glass": .glass
        case "capturedGlass": .liquidGlass
        default: .standard
        }
    }
}

/// The previous fixed system backdrop remains the permission/accessibility
/// fallback and the explicit rollback renderer.
enum GlassAppearanceMetrics {
    static let backdropOpacity: CGFloat = 0.25
    static func subpageShade(isExpanded: Bool, isOnboarding: Bool) -> Double {
        isExpanded && !isOnboarding ? 0.30 : 0
    }
    static let interactionBackingOpacity: CGFloat = 1 / 255
    static let edgeHighlightWidth: CGFloat = 1.1
    static let edgeShoulderWidth: CGFloat = 3.2
}

final class LiquidGlassContentView: NSView {
    let foreground: NSView
    private(set) var effect: NSVisualEffectView?
    private(set) var backgroundStyle: IslandBackgroundStyle = .standard
    var isGlassEnabled: Bool { backgroundStyle.isGlass }
    private let content = ContentView()
    private let highlights = EdgeHighlightView()
    private let capsuleGeometry: Bool
    private var renderer: CapturedGlassRenderer?
    private var capturedView: CapturedGlassView?
    private var capture: GlassBackgroundCapture?
    private var captureTask: Task<Void, Never>?
    private var configurationTask: Task<Void, Never>?
    private var cropCadence = GlassCropUpdateCadence()
    private var captureDisplayID: CGDirectDisplayID?
    private var captureGeneration = UUID()
    private var captureSignature: CaptureSignature?
    private var windowSubscriptions = Set<AnyCancellable>()
    private var subscriptions = Set<AnyCancellable>()
    private var suspended = false
    private var reducedTransparency = false
    private var failedSignature: CaptureSignature?
    private static let captureLog = Logger(subsystem: "com.focusisland.app", category: "LiquidGlass")
    private var receivedFrames = 0
    private var capturedFrameShown = false
    private var runtimeMetrics: GlassRuntimeMetrics?
    private var dragMetrics: GlassRuntimeMetrics?
    private var dragPresentationStart: GlassPresentationSnapshot?
    private var dragRate = 60
    private var configurationUpdates = 0
    private var dragConfigurationStart = 0
    private(set) var isDragging = false
    private var blurRadius = GlassBlurMetrics.defaultRadius

    private struct CaptureSignature: Equatable {
        let region: GlassCaptureRegion
        let displayID: CGDirectDisplayID
        let reduceMotion: Bool
        let framesPerSecond: Int
    }

    init(foreground: NSView, capsuleGeometry: Bool = false) {
        self.foreground = foreground
        self.capsuleGeometry = capsuleGeometry
        super.init(frame: foreground.frame)
        wantsLayer = true
        content.wantsLayer = true
        content.addSubview(foreground)
        addSubview(highlights)
        addSubview(content)
        highlights.isHidden = true
        GlassCapturePermission.shared.$granted.removeDuplicates().dropFirst().receive(on: RunLoop.main).sink { [weak self] _ in
            self?.reconcileCapture()
        }.store(in: &subscriptions)
        NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification).sink { [weak self] _ in
            GlassCapturePermission.shared.refresh()
            self?.failedSignature = nil
            self?.reconcileCapture()
        }.store(in: &subscriptions)
        NotificationCenter.default.publisher(for: NSApplication.didChangeScreenParametersNotification).sink { [weak self] _ in
            self?.reconcileCapture()
        }.store(in: &subscriptions)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is unsupported") }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window == nil { isDragging = false }
        windowSubscriptions.removeAll()
        if let window {
            for name in [NSWindow.didMoveNotification, NSWindow.didResizeNotification,
                         NSWindow.didChangeScreenNotification, NSWindow.didChangeOcclusionStateNotification,
                         NSWindow.didMiniaturizeNotification, NSWindow.didDeminiaturizeNotification] {
                NotificationCenter.default.publisher(for: name, object: window).sink { [weak self] _ in
                    self?.reconcileCapture()
                }.store(in: &windowSubscriptions)
            }
        }
        reconcileCapture()
    }

    /// The controller calls this immediately after ordering the shared panel
    /// in/out, as the occlusion notification can arrive one run-loop later.
    func reconcileCapture() {
        guard backgroundStyle.usesCapturedGlass, !reducedTransparency, !suspended,
              let window, window.isVisible,
              !window.isMiniaturized, window.occlusionState.contains(.visible),
              let screen = window.screen,
              let id = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber,
              GlassCapturePermission.shared.granted,
              (capture != nil || captureTask != nil || CGPreflightScreenCaptureAccess()),
              var region = GlassCaptureRegion(window: window.convertToScreen(convert(bounds, to: nil)),
                                              display: screen.frame, scale: GlassBlurMetrics.captureScale(
                                                radius: blurRadius, displayScale: screen.backingScaleFactor), padding: 18) else {
            stopCapture()
            return
        }
        if let previous = captureSignature?.region, previous.canReuse(for: region, viewportSize: bounds.size) {
            region = previous.reframed(to: region)
        }
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        let signature = CaptureSignature(region: region, displayID: id.uint32Value, reduceMotion: reduceMotion,
            framesPerSecond: GlassFrameRatePolicy.target(displayRate: screen.maximumFramesPerSecond,
                dragging: isDragging, reduceMotion: reduceMotion))
        guard signature != failedSignature else { return }
        guard signature != captureSignature else { return }
        if captureSignature?.framesPerSecond != signature.framesPerSecond {
            Self.captureLog.notice("Glass frame target \(signature.framesPerSecond) fps; dragging \(self.isDragging)")
        }
        // Move/resize within this display updates the running compositor stream.
        // Do not clear it or fetch shareable content again for every mouse event.
        if captureDisplayID == signature.displayID, capture != nil || captureTask != nil {
            captureSignature = signature
            synchronizeRendererRate(signature.framesPerSecond)
            // Reframe only when the cached texture covers the new viewport.
            // Otherwise keep the last glass drawable until the new crop arrives;
            // switching to the system material here flashes during fast drags.
            if let view = capturedView { _ = renderer?.reframe(to: region, in: view) }
            scheduleConfigurationUpdate()
            return
        }
        stopCapture()
        captureSignature = signature
        captureDisplayID = signature.displayID
        let generation = captureGeneration
        captureTask = Task { [weak self] in
            guard !Task.isCancelled, let self, generation == self.captureGeneration else { return }
            var session: GlassBackgroundCapture?
            do {
                let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
                guard !Task.isCancelled, generation == self.captureGeneration,
                      let display = content.displays.first(where: { $0.displayID == signature.displayID }) else { return }
                if self.renderer == nil { self.renderer = CapturedGlassRenderer() }
                guard let renderer = self.renderer else { throw GlassBackgroundCapture.CaptureError.applicationMissing }
                renderer.blurRadius = self.blurRadius
                renderer.onLuminance = { [weak self] luminance in
                    guard let self, generation == self.captureGeneration else { return }
                    GlassBackdropAppearance.shared.receive(luminance: luminance)
                }
                if self.capturedView == nil {
                    let view = CapturedGlassView(frame: self.bounds, device: renderer.device)
                    view.isPaused = true
                    view.enableSetNeedsDisplay = false
                    view.presentsWithTransaction = true
                    view.colorPixelFormat = .bgra8Unorm
                    view.clearColor = MTLClearColorMake(0, 0, 0, 0)
                    view.layer?.isOpaque = false
                    view.delegate = renderer
                    view.isHidden = true
                    view.alphaValue = 0
                    self.addSubview(view, positioned: .below, relativeTo: self.highlights)
                    self.capturedView = view
                }
                let target = self.captureSignature ?? signature
                self.synchronizeRendererRate(target.framesPerSecond)
                self.runtimeMetrics = GlassRuntimeMetrics(gpu: renderer.statistics.snapshot())
                session = try GlassBackgroundCapture(display: display, content: content, region: target.region,
                    reduceMotion: target.reduceMotion, framesPerSecond: target.framesPerSecond,
                    receive: { [weak self] pixels, source in
                        self?.receiveGlassPixels(pixels, source: source, generation: generation)
                    }, failure: { [weak self] in
                        guard let self, generation == self.captureGeneration else { return }
                        self.captureFailed(signature: signature)
                    })
                try await session?.start()
                guard !Task.isCancelled, generation == self.captureGeneration else {
                    await session?.stop(); return
                }
                self.capture = session
                self.captureTask = nil
                if self.isDragging { self.beginDragMetrics() }
                Self.captureLog.notice("Local glass capture started")
                self.scheduleConfigurationUpdate()
            } catch {
                await session?.stop()
                if !Task.isCancelled, generation == self.captureGeneration { self.captureFailed(signature: signature) }
            }
        }
    }

    private func scheduleConfigurationUpdate() {
        guard configurationTask == nil, let capture else { return }
        let generation = captureGeneration
        configurationTask = Task { [weak self] in
            guard let self else { return }
            while !Task.isCancelled, generation == self.captureGeneration,
                  let desired = self.captureSignature,
                  !capture.region.hasSamePixels(as: desired.region) || capture.reduceMotion != desired.reduceMotion
                    || capture.framesPerSecond != desired.framesPerSecond {
                do {
                    let delay = self.cropCadence.delay(at: CACurrentMediaTime(), reduceMotion: desired.reduceMotion,
                        framesPerSecond: desired.framesPerSecond)
                    if delay > 0 { try await Task.sleep(for: .seconds(delay)) }
                    guard !Task.isCancelled, generation == self.captureGeneration,
                          let newest = self.captureSignature else { return }
                    if !capture.region.hasSamePixels(as: newest.region) || capture.reduceMotion != newest.reduceMotion
                        || capture.framesPerSecond != newest.framesPerSecond {
                        self.cropCadence.requested(at: CACurrentMediaTime())
                        try await capture.update(region: newest.region, reduceMotion: newest.reduceMotion,
                            framesPerSecond: newest.framesPerSecond)
                        if generation == self.captureGeneration { self.configurationUpdates += 1 }
                    }
                    guard !Task.isCancelled, generation == self.captureGeneration else { return }
                } catch {
                    if generation == self.captureGeneration {
                        let error = error as NSError
                        Self.captureLog.error("Glass configuration failed; domain \(error.domain, privacy: .public), code \(error.code)")
                        self.captureFailed(signature: self.captureSignature ?? desired)
                    }
                    return
                }
            }
            if generation == self.captureGeneration { self.configurationTask = nil }
        }
    }

    private func receiveGlassPixels(_ pixels: CVPixelBuffer, source: GlassCaptureRegion, generation: UUID) {
        guard generation == captureGeneration, let signature = captureSignature,
              let view = capturedView, let renderer else { return }
        let mapped = source.reframed(to: signature.region)
        guard mapped.containsVisibleViewport(view.bounds.size) else {
            renderer.statistics.recordCoverageMiss(); return
        }
        // First activation stays transparent until its first GPU output. Crop
        // changes retain the visible glass drawable until a valid replacement.
        view.isHidden = false
        var presented: (@MainActor @Sendable () -> Void)?
        if !capturedFrameShown {
            presented = { [weak self] in
                guard let self, generation == self.captureGeneration,
                      self.captureSignature == signature else { return }
                self.capturedFrameShown = true
                self.capturedView?.alphaValue = 1
                self.effect?.isHidden = true
                self.highlights.isHidden = true
            }
        }
        if renderer.accept(pixels, region: mapped, in: view, onPresentation: presented) {
            capture?.noteAccepted(source)
            receivedFrames += 1
            dragMetrics?.recordFrame()
            if receivedFrames == 1 {
                let fps = signature.framesPerSecond
                Self.captureLog.notice("First local glass frame; buffer \(signature.region.pixelWidth)x\(signature.region.pixelHeight), cap \(fps) fps, audio off")
            }
            GlassCapturePermission.shared.setCaptureFailed(false)
            if let report = runtimeMetrics?.receive(gpu: renderer.statistics.snapshot()) {
                Self.captureLog.notice("Glass runtime: seconds \(report.duration), CPU \(report.cpuPercent)%, capture FPS \(report.captureFPS), render FPS \(report.renderedFPS), GPU ms/frame \(report.gpuMilliseconds), RSS MiB \(report.residentMiB)")
            }
        } else {
            captureFailed(signature: signature)
        }
    }

    private func captureFailed(signature: CaptureSignature) {
        failedSignature = signature
        Self.captureLog.error("Local glass unavailable; original backdrop restored")
        GlassCapturePermission.shared.setCaptureFailed(true)
        GlassCapturePermission.shared.refresh()
        stopCapture()
    }

    private func stopCapture() {
        finishDragMetrics(interrupted: true)
        if receivedFrames > 0 {
            Self.captureLog.notice("Local glass stopped; accepted frames \(self.receivedFrames), GPU submissions \(self.renderer?.submittedFrames ?? 0)")
        }
        receivedFrames = 0
        runtimeMetrics = nil
        capturedFrameShown = false
        captureGeneration = UUID()
        captureTask?.cancel()
        captureTask = nil
        configurationTask?.cancel()
        configurationTask = nil
        cropCadence = GlassCropUpdateCadence()
        captureSignature = nil
        captureDisplayID = nil
        if let capture {
            self.capture = nil
            capture.invalidate()
            Task { await capture.stop() }
        }
        hideCapturedFrame()
        renderer?.clear()
        GlassBackdropAppearance.shared.reset()
    }

    private func hideCapturedFrame() {
        capturedFrameShown = false
        capturedView?.isHidden = true
        capturedView?.alphaValue = 0
        effect?.isHidden = false
        highlights.isHidden = effect == nil
    }

    func suspendCapture(_ suspend: Bool) {
        suspended = suspend
        reconcileCapture()
    }

    func setDragging(_ dragging: Bool) {
        guard dragging != isDragging else { return }
        isDragging = dragging
        if dragging {
            beginDragMetrics()
        } else {
            finishDragMetrics(interrupted: false)
        }
        reconcileCapture()
    }

    private func synchronizeRendererRate(_ rate: Int) {
        renderer?.frameInterval = 1 / Double(rate)
        capturedView?.preferredFramesPerSecond = rate
    }

    private func beginDragMetrics() {
        guard dragMetrics == nil, capture != nil, let renderer else { return }
        dragRate = GlassFrameRatePolicy.target(displayRate: window?.screen?.maximumFramesPerSecond ?? 60,
            dragging: true, reduceMotion: NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
        dragMetrics = GlassRuntimeMetrics(gpu: renderer.statistics.snapshot())
        dragPresentationStart = renderer.statistics.presentationSnapshot()
        dragConfigurationStart = configurationUpdates
    }

    private func finishDragMetrics(interrupted: Bool) {
        guard var metrics = dragMetrics, let renderer else { return }
        dragMetrics = nil
        let presentationStart = dragPresentationStart
        dragPresentationStart = nil
        guard let report = metrics.finish(gpu: renderer.statistics.snapshot()), report.duration >= 0.1 else { return }
        let updates = configurationUpdates - dragConfigurationStart
        Self.captureLog.notice("Glass drag runtime: seconds \(report.duration), target \(self.dragRate) fps, CPU \(report.cpuPercent)%, capture FPS \(report.captureFPS), render FPS \(report.renderedFPS), GPU ms/frame \(report.gpuMilliseconds), RSS MiB \(report.residentMiB), config updates/s \(Double(updates) / report.duration), interrupted \(interrupted)")
        if let presentationStart,
           let display = renderer.statistics.presentationSnapshot().report(since: presentationStart, duration: report.duration) {
            Self.captureLog.notice("Glass drag presentation: displayed FPS \(display.displayedFPS), submission-to-display ms \(display.displayLagMilliseconds), drawable wait ms \(display.drawableWaitMilliseconds), schedule wait ms \(display.schedulingWaitMilliseconds), dropped callbacks \(display.dropped), cadence skips \(display.cadenceSkipped), uncovered crops \(display.coverageMisses)")
        }
    }

    func setBackgroundStyle(_ style: IslandBackgroundStyle) {
        guard style != backgroundStyle else { return }
        backgroundStyle = style
        refreshMaterial()
    }

    // Existing preferences and historical diagnostic probes may supply this
    // value. It intentionally has no visual effect in the fixed-blur preview.
    func setFrosting(_ value: Double) {}

    func setBlurRadius(_ value: Double) {
        let normalized = GlassBlurMetrics.normalized(value)
        guard normalized != blurRadius else { return }
        blurRadius = normalized
        renderer?.blurRadius = normalized
        if let view = capturedView { renderer?.redraw(in: view) }
        reconcileCapture()
    }

    /// Rebuild only for explicit style/accessibility changes, not focus or ticks.
    func refreshMaterial(reduceTransparency: Bool? = nil) {
        stopCapture()
        failedSignature = nil
        effect?.removeFromSuperview()
        effect = nil
        let reduced = reduceTransparency ?? NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency
        reducedTransparency = reduced
        if isGlassEnabled && !reduced {
            let backdrop = BackdropView()
            backdrop.material = .hudWindow
            backdrop.blendingMode = .behindWindow
            backdrop.state = .active
            backdrop.appearance = NSAppearance(named: .aqua)
            backdrop.alphaValue = GlassAppearanceMetrics.backdropOpacity
            backdrop.isEmphasized = false
            addSubview(backdrop, positioned: .below, relativeTo: highlights)
            effect = backdrop
        }
        if isGlassEnabled {
            layer?.backgroundColor = effect == nil
                ? NSColor(white: 0.12, alpha: 1).cgColor
                : NSColor.black.withAlphaComponent(GlassAppearanceMetrics.interactionBackingOpacity).cgColor
        } else {
            layer?.backgroundColor = NSColor.clear.cgColor
        }
        highlights.isHidden = effect == nil
        needsLayout = true
        reconcileCapture()
    }

    override func layout() {
        super.layout()
        let radius = capsuleGeometry ? bounds.height / 2
            : bounds.height * FocusDesign.Window.cornerRadius / FocusDesign.Window.baseHeight
        clipsToBounds = isGlassEnabled
        layer?.cornerRadius = isGlassEnabled ? radius : 0
        layer?.cornerCurve = .continuous
        layer?.masksToBounds = isGlassEnabled
        content.clipsToBounds = isGlassEnabled
        content.layer?.cornerRadius = isGlassEnabled ? radius : 0
        content.layer?.cornerCurve = .continuous
        content.layer?.masksToBounds = isGlassEnabled
        foreground.clipsToBounds = isGlassEnabled
        effect?.frame = bounds
        capturedView?.frame = bounds
        renderer?.radius = radius
        highlights.frame = bounds
        highlights.updateGeometry(radius: radius, scale: capsuleGeometry ? 0.65 : bounds.height / FocusDesign.Window.baseHeight)
        content.frame = bounds
        content.needsLayout = true
        content.layoutSubtreeIfNeeded()
        reconcileCapture()
    }

    private final class ContentView: NSView {
        override func layout() {
            super.layout()
            subviews.first?.frame = bounds
        }
    }
    private final class BackdropView: NSVisualEffectView {
        override func hitTest(_ point: NSPoint) -> NSView? { nil }
    }

    /// Two localized reflection bands, fading completely between the lobes.
    /// Below the glyphs, no hit testing, no dark stroke and no blur-radius claim.
    private final class EdgeHighlightView: NSView {
        private let crest = CAGradientLayer()
        private let shoulder = CAGradientLayer()
        private let crestMask = CAShapeLayer()
        private let shoulderMask = CAShapeLayer()

        override init(frame: NSRect) {
            super.init(frame: frame)
            wantsLayer = true
            layer?.name = "systemBlurEdgeHighlights"
            for (gradient, mask, strength) in [(shoulder, shoulderMask, 0.18), (crest, crestMask, 0.72)] {
                gradient.colors = GlassHighlightGradient.weights.map { weight in
                    NSColor.white.withAlphaComponent(weight * strength).cgColor
                }
                gradient.locations = GlassHighlightGradient.locations
                gradient.startPoint = GlassHighlightGradient.start
                gradient.endPoint = GlassHighlightGradient.end
                mask.fillColor = NSColor.clear.cgColor
                mask.strokeColor = NSColor.white.cgColor
                gradient.mask = mask
                layer?.addSublayer(gradient)
            }
        }
        required init?(coder: NSCoder) { fatalError("init(coder:) is unsupported") }
        override func hitTest(_ point: NSPoint) -> NSView? { nil }

        func updateGeometry(radius: CGFloat, scale: CGFloat) {
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            for (gradient, mask, width) in [
                (shoulder, shoulderMask, GlassAppearanceMetrics.edgeShoulderWidth),
                (crest, crestMask, GlassAppearanceMetrics.edgeHighlightWidth)
            ] {
                let lineWidth = width * scale
                let inset = lineWidth / 2 + 0.35 * scale
                gradient.frame = bounds
                mask.frame = bounds
                mask.lineWidth = lineWidth
                mask.path = CGPath(roundedRect: bounds.insetBy(dx: inset, dy: inset),
                    cornerWidth: max(0, radius - inset), cornerHeight: max(0, radius - inset), transform: nil)
            }
            CATransaction.commit()
        }
    }
}
