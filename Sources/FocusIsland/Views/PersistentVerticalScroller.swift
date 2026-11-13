import AppKit
import SwiftUI

/// The exact visual measurements used by Work Island 0.8.1.
///
/// Scrolling itself intentionally stays in AppKit. The 0.8.1 implementation
/// fed every geometry change back through SwiftUI `ScrollPosition`, which could
/// lock the settings page during rapid wheel or thumb input. Keeping the old
/// artwork separate from the native scroll event cycle restores the appearance
/// without restoring that feedback loop.
enum PersistentScrollbarMetrics {
    static let contentSpacing: CGFloat = 3
    static let visualWidth: CGFloat = 10
    static let verticalInset: CGFloat = 14
    static let trailingInset: CGFloat = 10
    static let minimumKnobLength: CGFloat = 18
    static let slotOpacity: CGFloat = 0.08
    static let knobOpacity: CGFloat = 0.50

    static func knobLength(trackHeight: CGFloat, contentHeight: CGFloat, viewportHeight: CGFloat) -> CGFloat {
        guard trackHeight > 0 else { return 0 }
        let safeContentHeight = max(1, contentHeight)
        return min(trackHeight, max(minimumKnobLength, trackHeight * viewportHeight / safeContentHeight))
    }

    static func knobOffset(
        trackHeight: CGFloat,
        knobLength: CGFloat,
        contentOffset: CGFloat,
        contentHeight: CGFloat,
        viewportHeight: CGFloat
    ) -> CGFloat {
        let travel = max(0, trackHeight - knobLength)
        let maximumOffset = max(0, contentHeight - viewportHeight)
        guard travel > 0, maximumOffset > 0 else { return 0 }
        let fraction = min(max(contentOffset / maximumOffset, 0), 1)
        return travel * fraction
    }
}

/// Always-visible 0.8.1 scrollbar artwork backed by AppKit's scroll machinery.
struct PersistentVerticalScrollView<Content: View>: View {
    @ViewBuilder let content: () -> Content
    @StateObject private var bridge = PersistentScrollBridge()

    var body: some View {
        HStack(spacing: PersistentScrollbarMetrics.contentSpacing) {
            ScrollView(.vertical) { content() }
                .scrollIndicators(.never)
                .background(PersistentScrollViewLocator(bridge: bridge))

            PersistentCapsuleScrollbar(bridge: bridge)
                .frame(width: PersistentScrollbarMetrics.visualWidth)
                .padding(.vertical, PersistentScrollbarMetrics.verticalInset)
                .padding(.trailing, PersistentScrollbarMetrics.trailingInset)
        }
        .scrollIndicators(.never)
    }
}

@MainActor
final class PersistentScrollBridge: NSObject, ObservableObject {
    private(set) weak var scrollView: NSScrollView?
    private weak var observedDocumentView: NSView?
    private weak var scrollbarView: PersistentCapsuleScrollbarView?

    deinit {
        NotificationCenter.default.removeObserver(self)
    }

    func attach(scrollView: NSScrollView) {
        if self.scrollView === scrollView,
           observedDocumentView === scrollView.documentView {
            enforceVerticalScrolling()
            refreshScrollbar()
            return
        }

        NotificationCenter.default.removeObserver(self)
        self.scrollView = scrollView
        observedDocumentView = scrollView.documentView

        // SwiftUI owns the document and AppKit owns wheel/rubber-band behavior.
        // The system scroller is hidden because the adjacent custom view draws
        // the exact 0.8.1 track and receives direct thumb interaction.
        scrollView.hasVerticalScroller = false
        scrollView.autohidesScrollers = true
        scrollView.verticalScrollElasticity = .automatic
        enforceVerticalScrolling()

        let clipView = scrollView.contentView
        clipView.postsBoundsChangedNotifications = true
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(scrollGeometryDidChange(_:)),
            name: NSView.boundsDidChangeNotification,
            object: clipView
        )

        scrollView.postsFrameChangedNotifications = true
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(scrollGeometryDidChange(_:)),
            name: NSView.frameDidChangeNotification,
            object: scrollView
        )

        if let documentView = scrollView.documentView {
            documentView.postsFrameChangedNotifications = true
            documentView.postsBoundsChangedNotifications = true
            NotificationCenter.default.addObserver(
                self,
                selector: #selector(scrollGeometryDidChange(_:)),
                name: NSView.frameDidChangeNotification,
                object: documentView
            )
            NotificationCenter.default.addObserver(
                self,
                selector: #selector(scrollGeometryDidChange(_:)),
                name: NSView.boundsDidChangeNotification,
                object: documentView
            )
        }

        refreshScrollbar()
    }

    func detach(scrollView: NSScrollView?) {
        guard scrollView == nil || self.scrollView === scrollView else { return }
        NotificationCenter.default.removeObserver(self)
        self.scrollView = nil
        observedDocumentView = nil
        refreshScrollbar()
    }

    func register(scrollbarView: PersistentCapsuleScrollbarView) {
        self.scrollbarView = scrollbarView
        refreshScrollbar()
    }

    func unregister(scrollbarView: PersistentCapsuleScrollbarView) {
        if self.scrollbarView === scrollbarView {
            self.scrollbarView = nil
        }
    }

    var geometry: (contentHeight: CGFloat, viewportHeight: CGFloat, contentOffset: CGFloat) {
        guard let scrollView,
              let documentView = scrollView.documentView else {
            return (1, 1, 0)
        }
        let viewportHeight = max(1, scrollView.contentView.bounds.height)
        let contentHeight = max(1, documentView.bounds.height, documentView.frame.height)
        let maximumOffset = max(0, contentHeight - viewportHeight)
        let visibleRect = scrollView.documentVisibleRect
        let rawOffset: CGFloat
        if documentView.isFlipped {
            rawOffset = visibleRect.minY - documentView.bounds.minY
        } else {
            // AppKit's ordinary document coordinate system starts at the
            // bottom. Convert it to the same top-to-bottom fraction used by
            // SwiftUI and by the original 0.8.1 scrollbar.
            rawOffset = documentView.bounds.maxY - visibleRect.maxY
        }
        return (contentHeight, viewportHeight, min(max(rawOffset, 0), maximumOffset))
    }

    func scroll(toFraction fraction: CGFloat) {
        guard let scrollView,
              scrollView.documentView != nil else { return }
        let metrics = geometry
        let maximumOffset = max(0, metrics.contentHeight - metrics.viewportHeight)
        let target = min(max(fraction, 0), 1) * maximumOffset
        let clipView = scrollView.contentView
        let targetY: CGFloat
        if let documentView = scrollView.documentView, documentView.isFlipped {
            targetY = documentView.bounds.minY + target
        } else if let documentView = scrollView.documentView {
            targetY = documentView.bounds.maxY - metrics.viewportHeight - target
        } else {
            targetY = target
        }
        clipView.scroll(to: NSPoint(x: 0, y: targetY))
        scrollView.reflectScrolledClipView(clipView)
        refreshScrollbar()
    }

    func scrollByPage(direction: CGFloat) -> Bool {
        guard scrollView != nil else { return false }
        let metrics = geometry
        let maximumOffset = max(0, metrics.contentHeight - metrics.viewportHeight)
        guard maximumOffset > 0 else { return false }
        let nextOffset = min(max(
            metrics.contentOffset + direction * max(24, metrics.viewportHeight * 0.75),
            0
        ), maximumOffset)
        scroll(toFraction: nextOffset / maximumOffset)
        return true
    }

    func forwardScrollWheel(_ event: NSEvent) -> Bool {
        guard let scrollView else { return false }
        // The custom track is a sibling of SwiftUI's NSScrollView, so events
        // received over the track do not naturally enter its responder chain.
        // Forward the original event intact to preserve precise deltas,
        // momentum phases, and the user's natural-scrolling preference.
        scrollView.scrollWheel(with: event)
        return true
    }

    @objc private func scrollGeometryDidChange(_ notification: Notification) {
        // SwiftUI can replace its document hosting view as disclosure content
        // changes. Reattach from the locator on its next layout; meanwhile the
        // existing track remains stable and never changes the page width.
        if observedDocumentView !== scrollView?.documentView,
           let scrollView {
            attach(scrollView: scrollView)
            return
        }
        enforceVerticalScrolling()
        refreshScrollbar()
    }

    private func enforceVerticalScrolling() {
        guard let scrollView else { return }
        // Hiding an indicator does not disable an axis. A diagonal wheel event
        // can still rubber-band horizontally, or pan a temporarily wider host.
        // Keep AppKit's original vertical event/momentum handling and constrain
        // only X, synchronously before the next draw. Do not feed bounds into
        // SwiftUI or install a per-frame/event monitor for every scroll page.
        if scrollView.hasHorizontalScroller { scrollView.hasHorizontalScroller = false }
        if scrollView.horizontalScrollElasticity != .none { scrollView.horizontalScrollElasticity = .none }
        if !scrollView.usesPredominantAxisScrolling { scrollView.usesPredominantAxisScrolling = true }
        let clipView = scrollView.contentView
        guard clipView.bounds.minX != 0 else { return }
        clipView.scroll(to: NSPoint(x: 0, y: clipView.bounds.minY))
        scrollView.reflectScrolledClipView(clipView)
    }

    private func refreshScrollbar() {
        scrollbarView?.refresh()
    }
}

private struct PersistentScrollViewLocator: NSViewRepresentable {
    let bridge: PersistentScrollBridge

    func makeNSView(context: Context) -> PersistentScrollViewLocatorView {
        PersistentScrollViewLocatorView(bridge: bridge)
    }

    func updateNSView(_ view: PersistentScrollViewLocatorView, context: Context) {
        view.bridge = bridge
        view.installWhenReady()
    }

    static func dismantleNSView(_ view: PersistentScrollViewLocatorView, coordinator: Void) {
        view.bridge.detach(scrollView: view.configuredScrollView)
    }
}

@MainActor
private final class PersistentScrollViewLocatorView: NSView {
    var bridge: PersistentScrollBridge
    private(set) weak var configuredScrollView: NSScrollView?
    private var pendingInstall = false
    private var installAttempts = 0
    private let maximumInstallAttempts = 8

    init(bridge: PersistentScrollBridge) {
        self.bridge = bridge
        super.init(frame: .zero)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        installAttempts = 0
        installWhenReady()
    }

    override func viewDidMoveToSuperview() {
        super.viewDidMoveToSuperview()
        installAttempts = 0
        installWhenReady()
    }

    override func layout() {
        super.layout()
        installWhenReady()
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    func installWhenReady() {
        if let scrollView = targetScrollView() {
            configuredScrollView = scrollView
            bridge.attach(scrollView: scrollView)
            return
        }
        guard !pendingInstall, installAttempts < maximumInstallAttempts else { return }
        installAttempts += 1
        pendingInstall = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.03) { [weak self] in
            guard let self else { return }
            self.pendingInstall = false
            self.installWhenReady()
        }
    }

    private func targetScrollView() -> NSScrollView? {
        if let enclosingScrollView { return enclosingScrollView }

        // A SwiftUI background is a sibling of the AppKit NSScrollView on
        // current macOS releases, so `enclosingScrollView` is legitimately nil.
        // Search only the nearest common ancestor and choose the geometrically
        // matching scroll view; this avoids coupling to private SwiftUI class
        // names and still distinguishes a nested horizontal history scroller.
        var ancestor = superview
        for _ in 0..<8 {
            guard let currentAncestor = ancestor else { break }
            let candidates = descendantScrollViews(in: currentAncestor)
            if !candidates.isEmpty {
                let locatorRect = convert(bounds, to: currentAncestor)
                return candidates.min { lhs, rhs in
                    geometryDistance(lhs, in: currentAncestor, from: locatorRect)
                        < geometryDistance(rhs, in: currentAncestor, from: locatorRect)
                }
            }
            ancestor = currentAncestor.superview
        }
        return nil
    }

    private func descendantScrollViews(in root: NSView) -> [NSScrollView] {
        root.subviews.flatMap { view -> [NSScrollView] in
            var matches = view is NSScrollView ? [view as! NSScrollView] : []
            matches.append(contentsOf: descendantScrollViews(in: view))
            return matches
        }
    }

    private func geometryDistance(
        _ scrollView: NSScrollView,
        in ancestor: NSView,
        from locatorRect: NSRect
    ) -> CGFloat {
        let candidateRect = scrollView.convert(scrollView.bounds, to: ancestor)
        return abs(candidateRect.midX - locatorRect.midX)
            + abs(candidateRect.midY - locatorRect.midY)
            + abs(candidateRect.width - locatorRect.width)
            + abs(candidateRect.height - locatorRect.height)
    }
}

private struct PersistentCapsuleScrollbar: NSViewRepresentable {
    let bridge: PersistentScrollBridge

    func makeNSView(context: Context) -> PersistentCapsuleScrollbarView {
        PersistentCapsuleScrollbarView(bridge: bridge)
    }

    func updateNSView(_ view: PersistentCapsuleScrollbarView, context: Context) {
        view.bridge = bridge
        bridge.register(scrollbarView: view)
    }

    static func dismantleNSView(_ view: PersistentCapsuleScrollbarView, coordinator: Void) {
        view.bridge.unregister(scrollbarView: view)
    }
}

@MainActor
final class PersistentCapsuleScrollbarView: NSView {
    var bridge: PersistentScrollBridge {
        didSet {
            oldValue.unregister(scrollbarView: self)
            bridge.register(scrollbarView: self)
        }
    }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { false }

    init(bridge: PersistentScrollBridge) {
        self.bridge = bridge
        super.init(frame: .zero)
        setAccessibilityElement(true)
        setAccessibilityRole(.scrollBar)
        setAccessibilityOrientation(.vertical)
        setAccessibilityLabel(L10n.text("滚动设置"))
        bridge.register(scrollbarView: self)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        let slot = bounds
        guard slot.width > 0, slot.height > 0 else { return }

        NSColor.white.withAlphaComponent(PersistentScrollbarMetrics.slotOpacity).setFill()
        NSBezierPath(
            roundedRect: slot,
            xRadius: slot.width / 2,
            yRadius: slot.width / 2
        ).fill()

        let knob = knobRect
        guard !knob.isEmpty else { return }
        NSColor.white.withAlphaComponent(PersistentScrollbarMetrics.knobOpacity).setFill()
        NSBezierPath(
            roundedRect: knob,
            xRadius: knob.width / 2,
            yRadius: knob.width / 2
        ).fill()
    }

    override func mouseDown(with event: NSEvent) {
        scroll(to: event)
    }

    override func mouseDragged(with event: NSEvent) {
        scroll(to: event)
    }

    override func scrollWheel(with event: NSEvent) {
        if !bridge.forwardScrollWheel(event) {
            super.scrollWheel(with: event)
        }
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func accessibilityPerformIncrement() -> Bool {
        bridge.scrollByPage(direction: 1)
    }

    override func accessibilityPerformDecrement() -> Bool {
        bridge.scrollByPage(direction: -1)
    }

    func refresh() {
        needsDisplay = true
        let metrics = bridge.geometry
        let maximumOffset = max(0, metrics.contentHeight - metrics.viewportHeight)
        let fraction = maximumOffset > 0 ? metrics.contentOffset / maximumOffset : 0
        setAccessibilityValue(fraction as NSNumber)
        setAccessibilityMinValue(0 as NSNumber)
        setAccessibilityMaxValue(1 as NSNumber)
    }

    var knobRect: NSRect {
        let metrics = bridge.geometry
        let length = PersistentScrollbarMetrics.knobLength(
            trackHeight: bounds.height,
            contentHeight: metrics.contentHeight,
            viewportHeight: metrics.viewportHeight
        )
        let offset = PersistentScrollbarMetrics.knobOffset(
            trackHeight: bounds.height,
            knobLength: length,
            contentOffset: metrics.contentOffset,
            contentHeight: metrics.contentHeight,
            viewportHeight: metrics.viewportHeight
        )
        return NSRect(x: 0, y: offset, width: bounds.width, height: length)
    }

    private func scroll(to event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        let metrics = bridge.geometry
        let length = PersistentScrollbarMetrics.knobLength(
            trackHeight: bounds.height,
            contentHeight: metrics.contentHeight,
            viewportHeight: metrics.viewportHeight
        )
        let travel = max(0, bounds.height - length)
        guard travel > 0 else { return }
        bridge.scroll(toFraction: (point.y - length / 2) / travel)
    }
}
