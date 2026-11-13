import AppKit
import Combine
import QuartzCore
import SwiftUI

@MainActor
final class FocusLossReturnCoordinator {
    private var task: Task<Void, Never>?
    private(set) var isPending = false

    func schedule(delay: TimeInterval, action: @escaping @MainActor () -> Void) {
        cancel()
        isPending = true
        task = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000))
            guard !Task.isCancelled, let self else { return }
            self.isPending = false
            action()
            self.task = nil
        }
    }

    func cancel() {
        task?.cancel()
        task = nil
        isPending = false
    }
}

@MainActor
final class IslandPanelController: NSObject, NSWindowDelegate {
    static let statusItemAutosaveName = "WorkIsland.StatusItem.System.v2"

    private enum PositionKey {
        static let x = "windowOriginX"
        static let y = "windowOriginY"
        static let saved = "windowOriginSaved"
    }

    private let panel: FocusPanel
    private let engine: TrackingEngine
    private let presentation = IslandPresentationModel()
    private var cancellables = Set<AnyCancellable>()
    private var observers: [NSObjectProtocol] = []
    private var eventMonitors: [Any] = []
    private var statusItem: NSStatusItem?
    private var statusVisibilityObservation: NSKeyValueObservation?
    private var statusCommandDragOrigin: NSPoint?
    private var statusCommandDragHasMoved = false
    private weak var statusProgressOverlay: StatusItemProgressOverlayView?
    private var statusArtworkTargetColor: StatusColorComponents?
    private var statusArtworkSnapshot: StatusItemArtworkSnapshot?
    private var statusRefreshScheduled = false
    private var isMenuPanelShown = false
    private var floatingFrameBeforeMenu: NSRect?
    private var menuStatusAnchor: NSRect?
    private var isPointerInside = false
    private var holdOnboardingOpacity = false
    private var isApplyingFrame = false
    private let focusLossReturn = FocusLossReturnCoordinator()
    private var onboardingOrigin: NSRect?
    private var onboardingMode: IslandDisplayMode?

    init(engine: TrackingEngine) {
        self.engine = engine
        panel = FocusPanel(
            contentRect: NSRect(origin: .zero, size: Self.panelSize(forScale: engine.settings.islandScale)),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        super.init()
        configurePanel()
        observeChanges()
        observeWorkspace()
        observeInteractionFocus()
        observeStatusItemCommandDrag()
        observers.append(NotificationCenter.default.addObserver(forName: .showWorkIslandOnboarding, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.showOnboarding() }
        })
        observers.append(NotificationCenter.default.addObserver(forName: .finishWorkIslandOnboarding, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.finishOnboarding() }
        })
        for name in [NSWorkspace.willSleepNotification, NSWorkspace.screensDidSleepNotification] {
            observers.append(NSWorkspace.shared.notificationCenter.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                Task { @MainActor in (self?.panel.contentView as? LiquidGlassContentView)?.suspendCapture(true) }
            })
        }
        for name in [NSWorkspace.didWakeNotification, NSWorkspace.screensDidWakeNotification] {
            observers.append(NSWorkspace.shared.notificationCenter.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                Task { @MainActor in (self?.panel.contentView as? LiquidGlassContentView)?.suspendCapture(false) }
            })
        }
        observers.append(NotificationCenter.default.addObserver(forName: NSApplication.willTerminateNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in (self?.panel.contentView as? LiquidGlassContentView)?.suspendCapture(true) }
        })
    }

    func show() {
        applyDisplayMode(engine.settings.displayMode)
        if !engine.settings.hasCompletedOnboarding { showOnboarding() }
    }

    func stopGlassCapture() {
        (panel.contentView as? LiquidGlassContentView)?.suspendCapture(true)
    }

    private func showOnboarding() {
        engine.setReminderOnboardingActive(true)
        closeMenuPanel()
        if onboardingMode == nil {
            onboardingOrigin = panel.frame
            onboardingMode = engine.settings.displayMode
        }
        focusLossReturn.cancel()
        presentation.collapse()
        presentation.onboardingState.step = .language
        presentation.isOnboardingPresented = true
        applyDisplayMode(.floating)
        panel.makeKeyAndOrderFront(nil)
    }

    private func finishOnboarding() {
        engine.setReminderOnboardingActive(false)
        holdOnboardingOpacity = true
        engine.settings.hasCompletedOnboarding = true
        presentation.isOnboardingPresented = false
        presentation.collapse()
        applyDisplayMode(onboardingMode ?? engine.settings.displayMode)
        if let frame = onboardingOrigin {
            isApplyingFrame = true
            panel.setFrame(frame, display: true)
            isApplyingFrame = false
        }
        onboardingOrigin = nil
        onboardingMode = nil
    }

    private func configurePanel() {
        panel.delegate = self
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.appearance = NSAppearance(named: .darkAqua)
        panel.hasShadow = (presentation.isOnboardingPresented ? IslandBackgroundStyle.standard : engine.settings.backgroundStyle).usesWindowShadow
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .canJoinAllApplications, .fullScreenDisallowsTiling]
        panel.hidesOnDeactivate = false
        panel.becomesKeyOnlyIfNeeded = true
        panel.isMovable = false // Disable server-side edge tiling; FocusPanel tracks drags.
        panel.isMovableByWindowBackground = false
        panel.isReleasedWhenClosed = false
        panel.onPointerDragChanged = { [weak self] dragging in
            (self?.panel.contentView as? LiquidGlassContentView)?.setDragging(dragging)
        }
        panel.onCommandDropInMenuBar = { [weak self] pointer in
            guard let self,
                  self.engine.settings.displayMode == .floating,
                  !self.presentation.isOnboardingPresented,
                  WindowPlacement.isMenuBarDrop(
                    pointer,
                    displays: WindowPlacement.displays,
                    statusBarHeight: NSStatusBar.system.thickness
                  ) else { return false }
            self.engine.settings.displayMode = .menuBar
            return true
        }

        let hostingView = NSHostingView(
            rootView: IslandRootView(
                engine: engine,
                presentation: presentation,
                onHoverChanged: { [weak self] inside in self?.setPointerInside(inside) }
            )
        )
        hostingView.wantsLayer = true
        // Window geometry is owned by panelSize/IslandRootView, not hosting
        // intrinsic-size constraints from changing scrollable content.
        hostingView.sizingOptions = []
        hostingView.layer?.backgroundColor = NSColor.clear.cgColor
        let content = LiquidGlassContentView(foreground: hostingView)
        content.setBlurRadius(engine.settings.glassBlurRadius)
        content.setBackgroundStyle(presentation.isOnboardingPresented ? .standard : engine.settings.backgroundStyle)
        panel.contentView = content
    }

    private func applyBackgroundStyle(_ style: IslandBackgroundStyle) {
        panel.hasShadow = style.usesWindowShadow
        panel.invalidateShadow()
        (panel.contentView as? LiquidGlassContentView)?.setBackgroundStyle(style)
        if statusItem != nil {
            updateStatusItem(
                seconds: engine.displayedTimerSeconds,
                style: engine.displayedTimerStyle,
                state: engine.state
            )
        }
    }

    private func observeChanges() {
        let settings = engine.settings
        presentation.$isOnboardingPresented.dropFirst().sink { [weak self] showing in
            guard let self else { return }
            self.applyBackgroundStyle(showing ? .standard : settings.backgroundStyle)
        }.store(in: &cancellables)
        presentation.$isShowingRecognitionPicker.sink { [weak self] showing in
            if showing { self?.cancelFocusLossReturn() }
        }.store(in: &cancellables)

        presentation.$page.removeDuplicates().dropFirst().receive(on: RunLoop.main)
            .sink { [weak self] page in
                guard let self, page == .settings, self.presentation.page == .settings,
                      self.presentation.isExpanded, !self.presentation.isOnboardingPresented else { return }
                // A single activation for an explicit settings-page entry.
                // Never use focus changes/ticks to maintain a glass appearance.
                self.cancelFocusLossReturn()
                // Settings entry is an explicit user action. The cooperative
                // activate() request can be declined while another app is front.
                NSApp.activate(ignoringOtherApps: true)
                self.panel.makeKeyAndOrderFront(nil)
                self.panel.makeFirstResponder(nil)
            }.store(in: &cancellables)

        settings.$displayMode.dropFirst().sink { [weak self] mode in
            self?.applyDisplayMode(mode)
        }.store(in: &cancellables)

        settings.$islandOpacity.dropFirst().sink { [weak self] opacity in
            self?.applyOpacity(opacity: opacity, previewing: self?.engine.settings.isPreviewingOpacity ?? false)
        }.store(in: &cancellables)
        settings.$backgroundStyle.dropFirst().sink { [weak self] _ in
            Task { @MainActor [weak self] in
                guard let self else { return }
                self.engine.settings.isPreviewingOpacity = false
                self.applyBackgroundStyle(self.presentation.isOnboardingPresented ? .standard : self.engine.settings.backgroundStyle)
                self.applyOpacity(opacity: self.engine.settings.islandOpacity, previewing: false)
            }
        }.store(in: &cancellables)
        settings.$glassBlurRadius.removeDuplicates().dropFirst().receive(on: RunLoop.main).sink { [weak self] radius in
            (self?.panel.contentView as? LiquidGlassContentView)?.setBlurRadius(radius)
        }.store(in: &cancellables)

        settings.$isPreviewingOpacity.dropFirst().sink { [weak self] previewing in
            guard let self else { return }
            self.applyOpacity(opacity: self.engine.settings.islandOpacity, previewing: previewing)
        }.store(in: &cancellables)

        presentation.$isExpanded.dropFirst().sink { [weak self] expanded in
            guard let self else { return }
            if !expanded && self.isMenuPanelShown {
                self.closeMenuPanel(resetNavigation: false)
            }
            // A queued resize animation would restore the hidden panel's old
            // bottom-left origin after it has been anchored under the menu bar.
            self.updatePanelSize(scale: 0.82, animate: !self.isMenuPanelShown)
            self.applyOpacity(opacity: self.engine.settings.islandOpacity, previewing: false, expanded: expanded)
        }.store(in: &cancellables)

        Publishers.CombineLatest3(engine.$todaySeconds, settings.$durationDisplayStyle, engine.$state)
            .sink { [weak self] _, _, state in
                guard let self else { return }
                self.updateStatusItem(seconds: self.engine.displayedTimerSeconds,
                                      style: self.engine.displayedTimerStyle,
                                      state: state)
            }
            .store(in: &cancellables)

        // Away time and the delayed breathing onset intentionally do not
        // republish today's work total. Coalesce those engine updates into one
        // lightweight layer refresh on the next main-loop turn.
        engine.objectWillChange.sink { [weak self] _ in
            Task { @MainActor [weak self] in self?.scheduleStatusItemRefresh() }
        }.store(in: &cancellables)

        settings.$activeThemeID.dropFirst().sink { [weak self] _ in
            guard let self else { return }
            self.updateStatusItem(seconds: self.engine.displayedTimerSeconds,
                                  style: self.engine.displayedTimerStyle,
                                  state: self.engine.state)
        }.store(in: &cancellables)

        settings.$awayTimerEnabled.dropFirst().sink { [weak self] _ in
            guard let self else { return }
            self.updateStatusItem(seconds: self.engine.displayedTimerSeconds,
                                  style: self.engine.displayedTimerStyle,
                                  state: self.engine.state)
        }.store(in: &cancellables)
    }

    private func observeWorkspace() {
        observers.append(NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in Task { @MainActor in self?.restoreOrClampPosition() } })

        observers.append(NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in Task { @MainActor in self?.accessibilityDisplayOptionsDidChange() } })

        observers.append(NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification,
            object: nil,
            queue: .main
        ) { [weak self] notification in
            let app = notification.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
            Task { @MainActor in
                if app?.bundleIdentifier == Bundle.main.bundleIdentifier {
                    self?.cancelFocusLossReturn()
                } else {
                    self?.scheduleFocusLossReturn()
                }
            }
        })
    }

    private func observeInteractionFocus() {
        let events: NSEvent.EventTypeMask = [.leftMouseDown, .rightMouseDown, .otherMouseDown, .scrollWheel]

        if let globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: events, handler: { [weak self] event in
            Task { @MainActor in
                guard let self else { return }
                if self.closeMenuPopoverIfNeeded(for: event) { return }
                if self.panel.frame.contains(NSEvent.mouseLocation) {
                    self.cancelFocusLossReturn()
                } else {
                    self.scheduleFocusLossReturn()
                }
            }
        }) {
            eventMonitors.append(globalMonitor)
        }

        if let localMonitor = NSEvent.addLocalMonitorForEvents(matching: events, handler: { [weak self] event in
            if self?.closeMenuPopoverIfNeeded(for: event) == true { return event }
            if event.window === self?.panel {
                Task { @MainActor in self?.cancelFocusLossReturn() }
            }
            return event
        }) {
            eventMonitors.append(localMonitor)
        }
    }

    private func observeStatusItemCommandDrag() {
        let events: NSEvent.EventTypeMask = [.leftMouseDown, .leftMouseDragged, .leftMouseUp]
        guard let monitor = NSEvent.addLocalMonitorForEvents(matching: events, handler: { [weak self] event in
            guard let self else { return event }
            return self.handleStatusItemCommandDrag(event)
        }) else { return }
        eventMonitors.append(monitor)
    }

    private func handleStatusItemCommandDrag(_ event: NSEvent) -> NSEvent? {
        switch event.type {
        case .leftMouseDown:
            resetStatusItemCommandDrag()
            guard engine.settings.displayMode == .menuBar,
                  event.modifierFlags.contains(.command),
                  eventTargetsStatusItem(event) else { return event }
            statusCommandDragOrigin = NSEvent.mouseLocation
            return event

        case .leftMouseDragged:
            guard let origin = statusCommandDragOrigin else { return event }
            let pointer = NSEvent.mouseLocation
            if hypot(pointer.x - origin.x, pointer.y - origin.y) >= Self.statusItemDragThreshold {
                statusCommandDragHasMoved = true
                // Keep macOS from reordering the item while Work Island owns
                // this Command-drag gesture.
                return nil
            }
            return event

        case .leftMouseUp:
            guard statusCommandDragOrigin != nil else { return event }
            let didMove = statusCommandDragHasMoved
            let pointer = NSEvent.mouseLocation
            resetStatusItemCommandDrag()
            guard Self.shouldConvertStatusCommandDrag(
                didMove: didMove,
                pointer: pointer,
                displays: WindowPlacement.displays,
                statusBarHeight: NSStatusBar.system.thickness
            ) else { return event }
            // Defer removal of the status item until AppKit finishes handling
            // this event. The floating window is placed at the release point.
            DispatchQueue.main.async { [weak self] in
                self?.convertStatusItemToFloating(at: pointer)
            }
            return nil

        default:
            return event
        }
    }

    private func eventTargetsStatusItem(_ event: NSEvent) -> Bool {
        guard let button = statusItem?.button,
              event.window === button.window else { return false }
        return button.bounds.contains(button.convert(event.locationInWindow, from: nil))
    }

    private func resetStatusItemCommandDrag() {
        statusCommandDragOrigin = nil
        statusCommandDragHasMoved = false
    }

    nonisolated static let statusItemDragThreshold: CGFloat = 4

    nonisolated static func shouldConvertStatusCommandDrag(
        didMove: Bool,
        pointer: NSPoint,
        displays: [WindowPlacement.Display],
        statusBarHeight: CGFloat
    ) -> Bool {
        didMove && !WindowPlacement.isMenuBarDrop(
            pointer,
            displays: displays,
            statusBarHeight: statusBarHeight
        )
    }

    private func applyDisplayMode(_ mode: IslandDisplayMode) {
        cancelFocusLossReturn()
        switch mode {
        case .floating:
            closeMenuPanel()
            removeStatusItem()
            updatePanelSize(scale: 0.82, animate: false)
            restoreOrClampPosition()
            applyOpacity(opacity: engine.settings.islandOpacity, previewing: false)
            panel.orderFrontRegardless()
        case .menuBar:
            closeMenuPanel()
            presentation.isMenuBarPresentation = false
            presentation.collapse()
            panel.orderOut(nil)
            installStatusItem()
        }
        (panel.contentView as? LiquidGlassContentView)?.reconcileCapture()
    }

    static func shouldCloseMenuPopover(
        for eventType: NSEvent.EventType,
        mouseLocation: NSPoint,
        popoverFrame: NSRect?,
        statusItemFrame: NSRect?
    ) -> Bool {
        let isMouseDown = eventType == .leftMouseDown || eventType == .rightMouseDown || eventType == .otherMouseDown
        guard isMouseDown, let popoverFrame else { return false }
        return !popoverFrame.contains(mouseLocation) && !(statusItemFrame?.contains(mouseLocation) ?? false)
    }

    private func closeMenuPopoverIfNeeded(for event: NSEvent) -> Bool {
        guard isMenuPanelShown, !presentation.isShowingRecognitionPicker else { return false }
        // Menus, color/name popovers and file dialogs belong to this same app;
        // interacting with them must not dismiss their parent panel.
        if let window = event.window, window !== panel { return false }
        let shouldClose = Self.shouldCloseMenuPopover(
            for: event.type,
            mouseLocation: NSEvent.mouseLocation,
            popoverFrame: panel.frame,
            statusItemFrame: statusItemScreenFrame()
        )
        if shouldClose { closeMenuPanel() }
        return shouldClose
    }

    private func statusItemScreenFrame() -> NSRect? {
        guard let button = statusItem?.button, let window = button.window else { return nil }
        let rect = window.convertToScreen(button.convert(button.bounds, to: nil))
        if NSScreen.screens.contains(where: { Self.isStatusAnchor(rect, on: $0.frame) }) { return rect }
        // Some system-hosted status items expose a local zero-origin window.
        return menuStatusAnchor
    }

    private func hasUsableStatusItemFrame() -> Bool {
        guard let button = statusItem?.button, let window = button.window else { return false }
        let rect = window.convertToScreen(button.convert(button.bounds, to: nil))
        // An auto-hidden menu bar can temporarily collapse or park its native
        // button window away from the visible top band. Positive, status-item-
        // sized geometry still proves that SystemUIServer hosts the item; using
        // top-edge placement as the availability test incorrectly ejects a
        // valid item back to floating mode during launch.
        return Self.isNativeStatusItemGeometryUsable(rect)
    }

    nonisolated static func isNativeStatusItemGeometryUsable(_ rect: NSRect) -> Bool {
        rect.width >= FocusDesign.AppKitToken.statusItemWidth / 2 && rect.height > 0
    }

    private func setPointerInside(_ inside: Bool) {
        isPointerInside = inside
        if !inside && !panel.frame.contains(NSEvent.mouseLocation) && !presentation.isOnboardingPresented {
            holdOnboardingOpacity = false
        }
        if inside { cancelFocusLossReturn() }
        applyOpacity(opacity: engine.settings.islandOpacity, previewing: engine.settings.isPreviewingOpacity)
    }

    private func applyOpacity(
        opacity: Double,
        previewing: Bool,
        expanded: Bool? = nil
    ) {
        if engine.settings.backgroundStyle.isGlass || presentation.isOnboardingPresented || holdOnboardingOpacity {
            panel.alphaValue = 1
            return
        }
        let isExpanded = expanded ?? presentation.isExpanded
        let target: Double
        if isExpanded {
            target = previewing ? opacity : 1
        } else if isPointerInside {
            target = max(FocusDesign.Opacity.minimumWindow, opacity - FocusDesign.Opacity.hoverReduction)
        } else {
            target = opacity
        }
        panel.animator().alphaValue = target
    }

    private func updatePanelSize(scale: Double, animate: Bool) {
        let size = Self.panelSize(forScale: scale)
        guard abs(panel.frame.width - size.width) > 0.001 || abs(panel.frame.height - size.height) > 0.001 else { return }
        var frame = panel.frame
        frame.origin.y += frame.height - size.height
        frame.size = size
        if animate {
            isApplyingFrame = true
            NSAnimationContext.runAnimationGroup { context in
                context.duration = FocusDesign.Motion.navigationDuration
                context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                panel.animator().setFrame(frame, display: true)
            } completionHandler: { [weak self] in
                Task { @MainActor in
                    self?.isApplyingFrame = false
                    self?.clampPosition()
                }
            }
        } else {
            isApplyingFrame = true
            panel.setFrame(frame, display: true)
            isApplyingFrame = false
            clampPosition()
        }
    }

    static func panelSize(forScale scale: Double) -> NSSize {
        FocusDesign.Window.scaledSize(for: scale)
    }

    static func shouldScheduleFocusLoss(displayMode: IslandDisplayMode, isExpanded: Bool) -> Bool {
        displayMode == .floating && isExpanded
    }

    private var pendingPositionSave: DispatchWorkItem?

    func windowDidMove(_ notification: Notification) {
        guard onboardingMode == nil, !isApplyingFrame, engine.settings.displayMode == .floating else { return }
        let origin = panel.frame.origin
        pendingPositionSave?.cancel()
        let save = DispatchWorkItem {
            UserDefaults.standard.set(origin.x, forKey: PositionKey.x)
            UserDefaults.standard.set(origin.y, forKey: PositionKey.y)
            UserDefaults.standard.set(true, forKey: PositionKey.saved)
        }
        pendingPositionSave = save
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2, execute: save)
    }

    func windowDidResignKey(_ notification: Notification) {
        scheduleFocusLossReturn()
    }

    func windowDidBecomeKey(_ notification: Notification) {
        cancelFocusLossReturn()
    }

    private func scheduleFocusLossReturn() {
        guard !presentation.isShowingRecognitionPicker else { return }
        guard onboardingMode == nil else { return }
        guard Self.shouldScheduleFocusLoss(
            displayMode: engine.settings.displayMode,
            isExpanded: presentation.isExpanded
        ) else { return }
        guard !focusLossReturn.isPending else { return }
        focusLossReturn.schedule(delay: FocusDesign.Motion.focusLossDelay) { [weak self] in
            guard let self else { return }
            withAnimation(FocusDesign.Motion.navigation) { self.presentation.collapse() }
        }
    }

    private func cancelFocusLossReturn() {
        focusLossReturn.cancel()
    }

    private func restoreOrClampPosition() {
        if UserDefaults.standard.bool(forKey: PositionKey.saved) {
            panel.setFrameOrigin(NSPoint(
                x: UserDefaults.standard.double(forKey: PositionKey.x),
                y: UserDefaults.standard.double(forKey: PositionKey.y)
            ))
            clampPosition()
        } else {
            guard let screen = NSScreen.main else { return }
            let visible = screen.visibleFrame
            panel.setFrameOrigin(NSPoint(x: visible.midX - panel.frame.width / 2, y: visible.maxY - panel.frame.height))
        }
    }

    private func clampPosition() {
        let placed = WindowPlacement.placed(panel.frame, displays: WindowPlacement.displays)
        if placed.origin != panel.frame.origin {
            isApplyingFrame = true
            panel.setFrameOrigin(placed.origin)
            isApplyingFrame = false
        }
    }

    private func installStatusItem() {
        guard statusItem == nil else { return }
        let item = NSStatusBar.system.statusItem(withLength: FocusDesign.AppKitToken.statusItemWidth)
        item.autosaveName = nil
        item.autosaveName = Self.statusItemAutosaveName
        item.behavior = [.removalAllowed]
        item.isVisible = true
        if let button = item.button {
            configureStatusButton(button, action: #selector(togglePopover(_:)))
            installStatusProgressOverlay(in: button)
        }
        statusItem = item
        let itemIdentity = ObjectIdentifier(item)
        observeStatusItemVisibility(item, identity: itemIdentity)
        updateStatusItem(seconds: engine.displayedTimerSeconds, style: engine.displayedTimerStyle, state: engine.state)
        // macOS 27 can keep isVisible true while the separate System Settings
        // menu-bar switch prevents actual placement. Wait for native layout,
        // then recover to the full floating window instead of drawing an
        // overlay that only resembles a status item.
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in
            guard let self, let item = self.statusItem,
                  ObjectIdentifier(item) == itemIdentity else { return }
            let hasUsableFrame = self.hasUsableStatusItemFrame()
            guard Self.shouldRecoverUnavailableStatusItem(
                displayMode: self.engine.settings.displayMode,
                hasUsableFrame: hasUsableFrame
            ) else { return }
            self.engine.settings.displayMode = .floating
        }
    }

    private func observeStatusItemVisibility(_ item: NSStatusItem, identity: ObjectIdentifier) {
        statusVisibilityObservation?.invalidate()
        statusVisibilityObservation = item.observe(\.isVisible, options: [.new]) { [weak self] _, change in
            guard let isVisible = change.newValue else { return }
            let pointer = NSEvent.mouseLocation
            Task { @MainActor [weak self] in
                self?.handleStatusItemVisibilityChange(
                    isVisible: isVisible,
                    itemIdentity: identity,
                    pointer: pointer
                )
            }
        }
    }

    nonisolated static func shouldRecoverHiddenStatusItem(
        displayMode: IslandDisplayMode,
        isVisible: Bool
    ) -> Bool {
        displayMode == .menuBar && !isVisible
    }

    nonisolated static func shouldRecoverUnavailableStatusItem(
        displayMode: IslandDisplayMode,
        hasUsableFrame: Bool
    ) -> Bool {
        displayMode == .menuBar && !hasUsableFrame
    }

    private func handleStatusItemVisibilityChange(
        isVisible: Bool,
        itemIdentity: ObjectIdentifier,
        pointer: NSPoint
    ) {
        guard statusItem.map(ObjectIdentifier.init) == itemIdentity,
              Self.shouldRecoverHiddenStatusItem(
                displayMode: engine.settings.displayMode,
                isVisible: isVisible
              ) else { return }
        convertStatusItemToFloating(at: pointer)
    }

    private func configureStatusButton(_ button: NSButton, action: Selector) {
        button.target = self
        button.action = action
        button.toolTip = "Work Island"
        button.isBordered = false
        button.focusRingType = .none
        button.setButtonType(.momentaryChange)
        button.state = .off
        if let cell = button.cell as? NSButtonCell {
            cell.highlightsBy = []
            cell.showsStateBy = []
        }
        button.wantsLayer = true
        button.layer?.backgroundColor = NSColor.clear.cgColor
        button.layer?.cornerRadius = 0
        button.layer?.masksToBounds = false
        button.imagePosition = .imageOnly
        button.imageScaling = .scaleNone
        button.attributedTitle = NSAttributedString(string: "")
    }

    private func installStatusProgressOverlay(in button: NSButton) {
        let overlay = StatusItemProgressOverlayView(frame: StatusItemProgressOverlayView.overlayFrame(
            in: button.bounds,
            flipped: button.isFlipped
        ))
        // Keep the compositor layer centered on the fixed 74×22 artwork when
        // an auto-hidden menu bar temporarily reports a roughly three-point
        // button height. It never participates in hit testing.
        overlay.autoresizingMask = [.minXMargin, .maxXMargin, .minYMargin, .maxYMargin]
        button.addSubview(overlay)
        statusProgressOverlay = overlay
    }

    private func removeStatusItem() {
        resetStatusItemCommandDrag()
        statusVisibilityObservation?.invalidate()
        statusVisibilityObservation = nil
        guard let statusItem else { return }
        NSStatusBar.system.removeStatusItem(statusItem)
        self.statusItem = nil
        statusProgressOverlay = nil
        statusArtworkTargetColor = nil
        statusArtworkSnapshot = nil
    }

    private func convertStatusItemToFloating(at pointer: NSPoint) {
        guard engine.settings.displayMode == .menuBar else { return }
        engine.settings.displayMode = .floating
        placeFloatingPanel(at: pointer)
    }

    private func placeFloatingPanel(at pointer: NSPoint) {
        let target = WindowPlacement.centeredFrame(
            size: panel.frame.size,
            at: pointer,
            displays: WindowPlacement.displays
        )
        pendingPositionSave?.cancel()
        isApplyingFrame = true
        panel.setFrame(target, display: true)
        isApplyingFrame = false
        UserDefaults.standard.set(target.origin.x, forKey: PositionKey.x)
        UserDefaults.standard.set(target.origin.y, forKey: PositionKey.y)
        UserDefaults.standard.set(true, forKey: PositionKey.saved)
        panel.orderFrontRegardless()
    }

    private func updateStatusItem(seconds: TimeInterval, style: DurationDisplayStyle, state: TrackingEngine.State) {
        guard let button = statusItem?.button else {
            statusArtworkTargetColor = nil
            return
        }
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        let target = StatusColorComponents(statusColor(for: state))
        let animateColorChange = !reduceMotion
            && statusArtworkTargetColor != nil
            && statusArtworkTargetColor != target
        statusArtworkTargetColor = target
        renderStatusItem(
            button: button,
            seconds: seconds,
            style: style,
            state: state,
            color: target.appKitColor,
            animateColorChange: animateColorChange
        )
    }

    private func renderStatusItem(
        button: NSStatusBarButton,
        seconds: TimeInterval,
        style: DurationDisplayStyle,
        state: TrackingEngine.State,
        color: NSColor,
        animateColorChange: Bool
    ) {
        let mode = StatusItemArtwork.animationMode(
            progress: engine.progress,
            state: state,
            animationsEnabled: !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
                && (!engine.isAway || engine.isAwayBreathing)
        )
        let awayElapsed = engine.awayBreathingStartedAt.map {
            max(0, Date().timeIntervalSince($0))
        }
        // Keep the system-owned NSStatusBarButton's fixed-size image path.
        // An automatically hidden menu bar can temporarily report a three-point
        // button height; custom child views inherit that invalid geometry and
        // become clipped. The button cell continues to place this 74×22 image
        // correctly in both collapsed and revealed states.
        let visualState = FocusDesign.Status.visualState(for: state)
        let reduceTransparency = NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency
        let snapshot = StatusItemArtworkSnapshot(
            time: TimeFormatting.compact(seconds, style: style),
            litCount: StatusItemArtwork.litCount(progress: engine.progress),
            color: StatusColorComponents(color),
            backgroundStyle: engine.settings.backgroundStyle,
            visualState: visualState,
            reduceTransparency: reduceTransparency
        )
        if snapshot != statusArtworkSnapshot {
            Self.applyStatusColorTransition(to: button, animated: animateColorChange)
            button.image = StatusItemArtwork.make(
                time: snapshot.time,
                progress: engine.progress,
                color: color,
                mode: .staticDisplay,
                timestamp: 0,
                backgroundStyle: snapshot.backgroundStyle,
                visualState: snapshot.visualState,
                reduceTransparency: snapshot.reduceTransparency
            )
            statusArtworkSnapshot = snapshot
        }
        statusProgressOverlay?.update(
            progress: engine.progress,
            color: color,
            mode: mode,
            awayElapsed: awayElapsed
        )
    }

    nonisolated static let statusColorTransitionKey = "WorkIsland.statusColorTransition"

    static func statusColorTransition(animated: Bool) -> CATransition? {
        guard animated else { return nil }
        let transition = CATransition()
        transition.type = .fade
        transition.duration = FocusDesign.Motion.statusTransitionDuration
        transition.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        return transition
    }

    static func applyStatusColorTransition(to button: NSStatusBarButton, animated: Bool) {
        guard let transition = statusColorTransition(animated: animated),
              let layer = button.layer else {
            button.layer?.removeAnimation(forKey: statusColorTransitionKey)
            return
        }
        layer.add(transition, forKey: statusColorTransitionKey)
    }

    static func shouldAnimateStatusItem(
        displayMode: IslandDisplayMode,
        state: TrackingEngine.State,
        reduceMotion: Bool,
        statusItemInstalled: Bool
    ) -> Bool {
        displayMode == .menuBar && statusItemInstalled && !reduceMotion && state != .paused
    }

    private func scheduleStatusItemRefresh() {
        guard !statusRefreshScheduled else { return }
        statusRefreshScheduled = true
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.statusRefreshScheduled = false
            self.updateStatusItem(
                seconds: self.engine.displayedTimerSeconds,
                style: self.engine.displayedTimerStyle,
                state: self.engine.state
            )
        }
    }

    private func accessibilityDisplayOptionsDidChange() {
        (panel.contentView as? LiquidGlassContentView)?.refreshMaterial()
        updateStatusItem(
            seconds: engine.displayedTimerSeconds,
            style: engine.displayedTimerStyle,
            state: engine.state
        )
    }

    private func statusColor(for state: TrackingEngine.State) -> NSColor {
        NSColor(FocusDesign.Status.color(
            for: state,
            trackingAccent: engine.settings.activeTheme?.accent.color
        ))
    }

    @objc private func togglePopover(_ sender: NSStatusBarButton) {
        let pointer = NSEvent.mouseLocation
        guard let screen = NSScreen.screens.first(where: { $0.frame.contains(pointer) }) ?? sender.window?.screen else { return }
        let candidate = statusItemScreenFrame()
        let anchor = candidate.flatMap { $0.contains(pointer) ? $0 : nil }
            ?? NSRect(x: pointer.x - sender.bounds.width / 2,
                      y: screen.frame.maxY - NSStatusBar.system.thickness,
                      width: sender.bounds.width, height: NSStatusBar.system.thickness)
        toggleMenuPanel(anchor: anchor, screen: screen)
    }

    private func toggleMenuPanel(anchor: NSRect, screen: NSScreen) {
        if isMenuPanelShown {
            closeMenuPanel()
        } else {
            menuStatusAnchor = anchor
            cancelFocusLossReturn()
            floatingFrameBeforeMenu = panel.frame
            isMenuPanelShown = true
            presentation.isMenuBarPresentation = true
            presentation.showNavigation()
            isApplyingFrame = true
            panel.setFrame(Self.menuPanelFrame(size: Self.panelSize(forScale: 0.82), anchor: anchor, screen: screen.frame), display: true)
            isApplyingFrame = false
            // Keep the previously cached compact timer invisible until SwiftUI
            // has committed the navigation page. This is a one-frame handoff,
            // not a fade or a compact-to-expanded transition.
            panel.alphaValue = 0
            panel.orderFrontRegardless()
            (panel.contentView as? LiquidGlassContentView)?.reconcileCapture()
            DispatchQueue.main.async { [weak self] in
                guard let self, self.isMenuPanelShown else { return }
                self.panel.contentView?.layoutSubtreeIfNeeded()
                self.panel.displayIfNeeded()
                self.panel.alphaValue = 1
            }
        }
    }

    static func menuPanelFrame(size: NSSize, anchor: NSRect, screen: NSRect) -> NSRect {
        NSRect(x: min(max(anchor.midX - size.width / 2, screen.minX), max(screen.minX, screen.maxX - size.width)),
               y: max(screen.minY, anchor.minY - size.height - 4), width: size.width, height: size.height)
    }

    static func isStatusAnchor(_ rect: NSRect, on screen: NSRect) -> Bool {
        rect.width > 0 && rect.height > 0 && rect.midX >= screen.minX && rect.midX <= screen.maxX
            && rect.maxY <= screen.maxY + 2 && rect.maxY >= screen.maxY - 40
            && rect.minY > screen.midY
    }

    private func closeMenuPanel(resetNavigation: Bool = true) {
        guard isMenuPanelShown else { return }
        isMenuPanelShown = false
        cancelFocusLossReturn()
        panel.orderOut(nil)
        (panel.contentView as? LiquidGlassContentView)?.reconcileCapture()
        if let frame = floatingFrameBeforeMenu {
            isApplyingFrame = true
            panel.setFrame(frame, display: false)
            isApplyingFrame = false
        }
        floatingFrameBeforeMenu = nil
        if resetNavigation { presentation.collapse() }
        presentation.isMenuBarPresentation = false
    }
}

private struct StatusItemArtworkSnapshot: Equatable {
    let time: String
    let litCount: Int
    let color: StatusColorComponents
    let backgroundStyle: IslandBackgroundStyle
    let visualState: FocusDesign.Status.VisualState
    let reduceTransparency: Bool
}

/// The fixed NSImage always contains a complete static capsule. This small,
/// noninteractive overlay contributes only the scan/breathing delta so the
/// WindowServer can animate it without redrawing an NSImage on every frame.
