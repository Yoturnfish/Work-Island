import AppKit
import SwiftUI
import Testing
@testable import FocusIsland

@MainActor
private final class ScrollWheelRecordingScrollView: NSScrollView {
    private(set) var receivedScrollWheelEvent: NSEvent?

    override func scrollWheel(with event: NSEvent) {
        receivedScrollWheelEvent = event
    }
}

@MainActor
struct DisplayBehaviorTests {
    @Test func allGlassSubpagesShareThirtyPercentShadeButTimerDoesNot() {
        let presentation = IslandPresentationModel()
        #expect(GlassAppearanceMetrics.subpageShade(isExpanded: presentation.isExpanded, isOnboarding: false) == 0)
        presentation.showNavigation()
        for page in IslandNavigationPage.allCases {
            presentation.open(page)
            #expect(GlassAppearanceMetrics.subpageShade(isExpanded: presentation.isExpanded, isOnboarding: false) == 0.3)
        }
        #expect(GlassAppearanceMetrics.subpageShade(isExpanded: true, isOnboarding: true) == 0)
        presentation.collapse()
        #expect(GlassAppearanceMetrics.subpageShade(isExpanded: presentation.isExpanded, isOnboarding: false) == 0)
    }

    @Test func statusCapsuleUsesFixedArtworkForEveryBackgroundStyle() {
        let standard = StatusItemArtwork.make(
            time: "1H 17M", progress: 0.5, color: .systemGreen,
            mode: .staticDisplay, timestamp: 0,
            backgroundStyle: .standard, visualState: .tracking
        )
        let glass = StatusItemArtwork.make(
            time: "1H 17M", progress: 0.5, color: .systemGreen,
            mode: .staticDisplay, timestamp: 0,
            backgroundStyle: .glass, visualState: .tracking,
            reduceTransparency: false
        )
        let reducedTransparency = StatusItemArtwork.make(
            time: "1H 17M", progress: 0.5, color: .systemGreen,
            mode: .staticDisplay, timestamp: 0,
            backgroundStyle: .glass, visualState: .tracking,
            reduceTransparency: true
        )
        let fixedSize = NSSize(
            width: FocusDesign.AppKitToken.statusArtworkWidth,
            height: FocusDesign.AppKitToken.statusArtworkHeight
        )
        #expect(standard.size == fixedSize)
        #expect(glass.size == fixedSize)
        #expect(reducedTransparency.size == fixedSize)
        #expect(!standard.isTemplate && !glass.isTemplate && !reducedTransparency.isTemplate)
        #expect(standard.tiffRepresentation != glass.tiffRepresentation)
        #expect(glass.tiffRepresentation != reducedTransparency.tiffRepresentation)
    }

    @Test func sharedMenuPanelAnchorsBelowStatusItemOnEitherScreenEdge() {
        #expect(!IslandPanelController.isStatusAnchor(NSRect(x: 0, y: 0, width: 80, height: 24),
                                                      on: NSRect(x: 0, y: 0, width: 1920, height: 1080)))
        #expect(!IslandPanelController.isStatusAnchor(NSRect(x: 7, y: 1068, width: 80, height: 24),
                                                      on: NSRect(x: 0, y: 0, width: 1920, height: 1080)))
        for screen in [NSRect(x: 0, y: 0, width: 1920, height: 1080), NSRect(x: -1920, y: 120, width: 1920, height: 1080)] {
            for x in [screen.minX, screen.midX, screen.maxX - 80] {
                let anchor = NSRect(x: x, y: screen.maxY - 24, width: 80, height: 24)
                #expect(IslandPanelController.isStatusAnchor(anchor, on: screen))
                let frame = IslandPanelController.menuPanelFrame(size: NSSize(width: 282, height: 75), anchor: anchor, screen: screen)
                #expect(screen.contains(frame))
                #expect(frame.maxY == anchor.minY - 4)
            }
        }
    }

    @Test func confirmedGlassAppearanceAndLegacyNamesStayCompatible() {
        #expect(GlassAppearanceMetrics.backdropOpacity == 0.25)
        #expect(GlassTextRenderer.opacity == 0.22)
        #expect(GlassTextRenderer.radius == 6)
        #expect(GlassTextRenderer(enabled: true).displayPadding == EdgeInsets())
        #expect(IslandBackgroundStyle.allCases == [.standard, .glass, .liquidGlass])
        for value in ["liquidGlass", "stableLiquidGlass", "glass"] {
            #expect(IslandBackgroundStyle.restored(value) == .glass)
        }
        #expect(IslandBackgroundStyle.restored("standard") == .standard)
        #expect(L10n.text("黑色", language: .english) == "Black")
        #expect(L10n.text("默认", language: .english) == "Default")
        #expect(L10n.text("透明", language: .english) == "Transparent")
        #expect(FocusDesign.ColorToken.glassTimerTitle == .white)
    }
    @Test func transparentSubpageNeutralElementsAreWhiteWithoutChangingBlackPalette() {
        var environment = EnvironmentValues()
        #expect(SubpageForegroundStyle.secondary.resolve(in: environment) == FocusDesign.ColorToken.textSecondary)
        #expect(SubpageForegroundStyle.muted.resolve(in: environment) == FocusDesign.ColorToken.textMuted)
        environment.whiteSubpageForeground = true
        for style in [SubpageForegroundStyle.primary, .secondary, .muted] {
            #expect(style.resolve(in: environment) == Color.white.opacity(1))
        }
        #expect(SubpageForegroundStyle(fallback: .black, glassOpacity: 0.55).resolve(in: environment) == Color.white.opacity(0.55))
    }
    @Test func textBackingIsExclusiveToTransparentTimer() {
        for isGlass in [false, true] {
            for isExpanded in [false, true] {
                for isOnboarding in [false, true] {
                    #expect(GlassTextRenderer.shouldEnable(isGlass: isGlass, isExpanded: isExpanded,
                        isOnboarding: isOnboarding) == (isGlass && !isExpanded && !isOnboarding))
                }
            }
        }
    }
    @Test func glassTextBackingPreservesTextLayoutAndActuallyChangesRendering() {
        func render(_ enabled: Bool) -> CGImage? {
            ImageRenderer(content: Text("Work time 工作时间")
                .font(.system(size: 20)).foregroundStyle(.white)
                .textRenderer(GlassTextRenderer(enabled: enabled))
                .padding(5)).cgImage
        }
        let plain = render(false), backed = render(true)
        #expect(plain != nil && backed != nil)
        #expect(plain?.width == backed?.width && plain?.height == backed?.height)
        #expect(plain?.dataProvider?.data != backed?.dataProvider?.data)
    }
    @Test func onboardingGoalUsesTheSharedNativePopupAndAllSixOptions() {
        let host = NSHostingView(rootView: DailyGoalPicker(seconds: .constant(14_400)))
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 96, height: 24),
                              styleMask: .borderless, backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.contentView = host
        host.layoutSubtreeIfNeeded()
        func popup(in view: NSView) -> NSPopUpButton? {
            if let button = view as? NSPopUpButton { return button }
            return view.subviews.lazy.compactMap { popup(in: $0) }.first
        }
        let button = popup(in: host)
        #expect(button != nil)
        #expect(button?.cell is GlassSettingsPickerCell)
        #expect(button?.numberOfItems == 6)
        #expect(button?.indexOfSelectedItem == 3)
        #expect(abs((button?.frame.width ?? 0) - FocusDesign.Size.settingsPickerWidth) < 0.01)
        if let button {
            for x in [CGFloat(1), button.bounds.width - 1] {
                let point = button.convert(CGPoint(x: x, y: button.bounds.midY), to: host)
                #expect(host.hitTest(point) === button)
            }
        }
        window.close()
    }

    @Test func settingsPopupGeometryAndCompactTimeLabelsStayUnified() {
        #expect(DurationDisplayStyle.hoursMinutes.title == "H + M")
        #expect(DurationDisplayStyle.minutesSeconds.title == "M + S")
        #expect(TimeFormatting.goalHours(3_600, language: .english) == "1 h")
        #expect(TimeFormatting.goalHours(7_200, language: .english) == "2 h")
        #expect(TimeFormatting.goalHours(7_200, language: .chinese) == "2 小时")
        #expect(FocusDesign.Size.settingsPickerWidth == 96)
        #expect(FocusDesign.Size.settingsPickerHeight == 24)
        #expect(FocusDesign.Size.settingsPickerCornerRadius == 4)
        #expect(FocusDesign.Size.settingsPickerTextInset == 7)
        #expect(SettingsInteraction.onboardingReplayTapCount == 3)
        #expect(L10n.text("24 小时制", language: .english) == "24-hour")

        let bounds = NSRect(x: 0, y: 0, width: FocusDesign.Size.settingsPickerWidth,
                            height: FocusDesign.Size.settingsPickerHeight)
        let textFrame = GlassSettingsPickerCell.textFrame(in: bounds, textHeight: 14)
        #expect(textFrame.minX == 7)
        #expect(textFrame.maxX == bounds.maxX - FocusDesign.Size.settingsPickerTextTrailingClearance)
        #expect(textFrame.midY == bounds.midY)

        func popup(in view: NSView) -> NSPopUpButton? {
            if let button = view as? NSPopUpButton { return button }
            return view.subviews.lazy.compactMap { popup(in: $0) }.first
        }
        for language in AppLanguage.allCases {
            for whiteForeground in [false, true] {
                let root = GlassSettingsPicker(
                    title: L10n.text("时间格式", language: language),
                    labels: DurationDisplayStyle.allCases.map(\.title),
                    selectedIndex: 0,
                    onSelect: { _ in }
                )
                .environment(\.whiteSubpageForeground, whiteForeground)
                .frame(width: FocusDesign.Size.settingsPickerWidth,
                       height: FocusDesign.Size.settingsPickerHeight)
                let host = NSHostingView(rootView: root)
                host.frame = bounds
                host.layoutSubtreeIfNeeded()
                let button = popup(in: host)
                #expect(button?.frame.size == bounds.size)
                #expect(button?.superview is GlassSettingsPickerHostView)
                #expect(button?.frame == button?.superview?.bounds)
                #expect(button?.itemTitles == ["H + M", "M + S"])
                #expect((button?.cell as? GlassSettingsPickerCell)?.whiteForeground == whiteForeground)
            }
        }
    }

    @Test func dayResetUsesFixedMidnightRepresentationAndStrict24HourParsing() {
        let midnight = DayResetTime.displayParts(for: 0)
        #expect(midnight.hour == "24")
        #expect(midnight.minute == "00")
        let afternoon = DayResetTime.displayParts(for: 13 * 60 + 7)
        #expect(afternoon.hour == "13")
        #expect(afternoon.minute == "07")
        #expect(DayResetTime.parse(hour: "24", minute: "00") == 0)
        #expect(DayResetTime.parse(hour: "00", minute: "00") == 0)
        #expect(DayResetTime.parse(hour: "23", minute: "59") == 1_439)
        #expect(DayResetTime.parse(hour: "24", minute: "01") == nil)
        #expect(DayResetTime.parse(hour: "25", minute: "00") == nil)
        #expect(DayResetTime.parse(hour: "12", minute: "60") == nil)
        #expect(DayResetTime.sanitizedComponent("1a2:3") == "12")
        #expect(DayResetTime.committedMinutes(hour: "", minute: "", fallback: 13 * 60 + 7) == 0)
        #expect(DayResetTime.committedMinutes(hour: "24", minute: "00", fallback: 13 * 60 + 7) == 0)
        #expect(DayResetTime.committedMinutes(hour: "08", minute: "30", fallback: 13 * 60 + 7) == 8 * 60 + 30)
        #expect(DayResetTime.committedMinutes(hour: "08", minute: "", fallback: 13 * 60 + 7) == 13 * 60 + 7)
        #expect(DayResetTime.committedMinutes(hour: "99", minute: "99", fallback: 13 * 60 + 7) == 13 * 60 + 7)
    }

    @Test func progressAnimationsKeepTheirFullCadenceAndOrbPulseUsesTheCompositor() {
        let icon = LayerAnimatedStatusIconView(frame: NSRect(x: 0, y: 0, width: 32, height: 32))
        icon.update(symbolName: "cup.and.saucer.fill", pointSize: 14, color: .black, pulse: .away)
        icon.layoutSubtreeIfNeeded()
        #expect(icon.hitTest(NSPoint(x: 16, y: 16)) == nil)
        #expect(icon.pulseAnimation?.duration == FocusDesign.Motion.awayPulseDuration)
        #expect(icon.pulseAnimation?.autoreverses == true)
        #expect(icon.pulseAnimation?.repeatCount == .infinity)
        #expect(icon.pulseContainerFrame == icon.bounds)
        #expect(icon.pulseAnchorPoint == CGPoint(x: 0.5, y: 0.5))
        #expect(icon.pulseAnchorInRoot == CGPoint(x: icon.bounds.midX, y: icon.bounds.midY))
        icon.frame = NSRect(x: 0, y: 0, width: 26, height: 26)
        icon.layoutSubtreeIfNeeded()
        #expect(icon.pulseContainerFrame == icon.bounds)
        #expect(icon.pulseAnchorInRoot == CGPoint(x: 13, y: 13))
        let cupOffset = LayerAnimatedStatusIconView.opticalOffset(
            symbolName: "cup.and.saucer.fill", pointSize: FocusDesign.Typography.label
        )
        #expect(cupOffset == NSPoint(x: -0.5, y: 0.5))
        #expect(LayerAnimatedStatusIconView.opticalOffset(
            symbolName: "clock.fill", pointSize: FocusDesign.Typography.label
        ) == .zero)
        icon.update(symbolName: "pause.fill", pointSize: 14, color: .white, pulse: .none)
        #expect(icon.pulseAnimation == nil)

        let progress = LayerAnimatedProgressNSView(frame: NSRect(x: 0, y: 0, width: 220, height: 16))
        progress.update(progress: 0.4, isActive: true, animationsEnabled: true, isAway: false,
                        columns: 30, accentColor: .systemGreen, style: .matrix)
        progress.layoutSubtreeIfNeeded()
        #expect(progress.hasFilledGeometry && progress.hasUnfilledGeometry)
        #expect(progress.scanAnimation?.duration == FocusDesign.Motion.scanDuration)
        #expect(FocusDesign.Motion.scanDuration == 2.8)
        #expect(FocusDesign.Motion.scanDuration == FocusDesign.Motion.awayPulseDuration)
        #expect(progress.scanAnimation?.repeatCount == .infinity)
        let scanConfigurations = progress.scanConfigurationCount
        progress.update(progress: 0.41, isActive: true, animationsEnabled: true, isAway: false,
                        columns: 30, accentColor: .systemGreen, style: .matrix)
        #expect(progress.scanConfigurationCount == scanConfigurations)
        #expect(progress.scanAnimation != nil)
        #expect(LayerAnimatedProgressNSView.filledElementCount(progress: 0.40, columns: 30, style: .matrix)
            == LayerAnimatedProgressNSView.filledElementCount(progress: 0.41, columns: 30, style: .matrix))
        progress.update(progress: 0.41, isActive: true, animationsEnabled: true, isAway: false,
                        columns: 30, accentColor: .systemOrange, style: .matrix)
        #expect(progress.filledShadowColor == NSColor.systemOrange.cgColor)
        progress.update(progress: 0.41, isActive: true, animationsEnabled: true, isAway: false,
                        columns: 30, accentColor: .systemGreen, style: .matrix)
        #expect(progress.filledShadowColor == NSColor.systemGreen.cgColor)
        progress.update(progress: 0.4, isActive: false, animationsEnabled: true, isAway: true,
                        columns: 30, accentColor: .systemOrange, style: .matrix)
        #expect(progress.scanAnimation == nil)
        #expect(progress.pulseAnimation?.duration == FocusDesign.Motion.awayPulseDuration / 2)
        #expect(progress.pulseAnimation?.autoreverses == true)
        progress.update(progress: 1, isActive: false, animationsEnabled: true, isAway: true,
                        columns: 30, accentColor: .systemOrange, style: .matrix)
        #expect(progress.pulseAnimation == nil)
    }
    @Test func awayBreathingFadesInFromTriggerRatherThanGlobalPhase() {
        for elapsed in [-1.0, 0, 0.001, 0.1, 0.4, 0.8, 1.4, 2.8] {
            let strength = ProgressAnimationMetrics.awayBreathingStrength(elapsed: elapsed)
            #expect((0...1).contains(strength))
            for timestamp in [0.0, 12345.67, 800000000.25] {
                let matrix = ProgressAnimationMetrics.cellOpacity(isFilled: false, column: 20, columns: 30,
                    mode: .awayPulse, timestamp: timestamp, awayElapsed: elapsed)
                #expect(abs(matrix - (0.1 + strength * 0.25)) < 0.000001)
                let menu = Double(StatusItemArtwork.pixelOpacity(column: 5, litCount: 1, columns: 10,
                    mode: .awayPulse, timestamp: timestamp, awayElapsed: elapsed))
                #expect(abs(menu - matrix) < 0.000001)
                let wave = StyledProgressView.waveformCellOpacity(isFilled: false, progress: 0.5,
                    moving: false, pulsing: true, phase: 0.25, awayElapsed: elapsed)
                #expect(abs(wave - (0.18 + strength * 0.17)) < 0.000001)
            }
        }
        #expect(ProgressAnimationMetrics.awayBreathingStrength(elapsed: 0) == 0)
        #expect(ProgressAnimationMetrics.awayBreathingStrength(elapsed: 0.001) < 0.00001)
        #expect(ProgressAnimationMetrics.awayBreathingStrength(elapsed: 1.4) == 1)
        for invalid in [Double.nan, .infinity, -.infinity] {
            #expect(ProgressAnimationMetrics.awayBreathingStrength(elapsed: invalid) == 0)
        }
    }

    @Test func progressStylesHaveStableGeometryAndSharedAnimations() {
        for style in IslandProgressStyle.allCases {
            let progress = LayerAnimatedProgressNSView(frame: NSRect(x: 0, y: 0, width: 220, height: 24))
            progress.update(progress: 0.43, isActive: true, animationsEnabled: true, isAway: false,
                            columns: 30, accentColor: .systemGreen, style: style)
            progress.layoutSubtreeIfNeeded()
            #expect(progress.hasFilledGeometry && progress.hasUnfilledGeometry)
            #expect(progress.scanAnimation?.duration == FocusDesign.Motion.scanDuration)
            let configurations = progress.scanConfigurationCount
            progress.update(progress: 0.44, isActive: true, animationsEnabled: true, isAway: false,
                            columns: 30, accentColor: .systemGreen, style: style)
            #expect(progress.scanConfigurationCount == configurations)
            progress.update(progress: 0.44, isActive: false, animationsEnabled: true, isAway: true,
                            columns: 30, accentColor: .systemOrange, style: style)
            #expect(progress.pulseAnimation?.duration == FocusDesign.Motion.awayPulseDuration / 2)
            progress.update(progress: 1, isActive: false, animationsEnabled: true, isAway: true,
                            columns: 30, accentColor: .systemOrange, style: style)
            #expect(progress.pulseAnimation == nil)
        }
    }
    @Test func glassSettingsUseWhiteThumbWithoutChangingDefaultControls() {
        #expect(SettingsControlPalette.thumb(isGlass: true, isOn: true) == .white)
        #expect(SettingsControlPalette.thumb(isGlass: true, isOn: false) == .white)
        #expect(SettingsControlPalette.thumb(isGlass: false, isOn: true) == .black.opacity(0.82))
        #expect(SettingsControlPalette.thumb(isGlass: false, isOn: false) == FocusDesign.ColorToken.textSecondary)
        #expect(EnvironmentValues().glassSettingsControls == false)
    }
    @Test func glassTitleNeverDimsWhileDefaultKeepsItsAwayAppearance() {
        #expect(FocusDesign.Opacity.mainTitle(isAway: true, isGlass: true) == 1)
        #expect(FocusDesign.Opacity.mainTitle(isAway: false, isGlass: true) == 1)
        #expect(FocusDesign.Opacity.mainTitle(isAway: true, isGlass: false) == 0.46)
        #expect(FocusDesign.Opacity.mainTitle(isAway: false, isGlass: false) == 1)
    }

    @Test func awayBreathingNeverChangesCompletedCellsInEitherStyleOrMenuBar() {
        for progress in [0.01, 0.5, 0.99, 1.0] {
            for sample in 0...56 {
                let time = Double(sample) / 20
                let phase = time / FocusDesign.Motion.awayPulseDuration
                #expect(ProgressAnimationMetrics.cellOpacity(isFilled: true, column: 0, columns: 30,
                    mode: .awayPulse, timestamp: time) == FocusDesign.Opacity.activePixel)
                let menuOpacity = Double(StatusItemArtwork.pixelOpacity(column: 0, litCount: 1, columns: 10,
                    mode: .awayPulse, timestamp: time))
                #expect(abs(menuOpacity - FocusDesign.Opacity.activePixel) < 0.000001)
                #expect(StyledProgressView.waveformCellOpacity(isFilled: true, progress: progress,
                    moving: false, pulsing: true, phase: phase) == 1)
                #expect(StyledProgressView.waveformCellOpacity(isFilled: true, progress: progress,
                    moving: true, pulsing: false, phase: phase) == 1)
            }
        }
        let duration = FocusDesign.Motion.awayPulseDuration
        let low = ProgressAnimationMetrics.cellOpacity(isFilled: false, column: 20, columns: 30,
            mode: .awayPulse, timestamp: duration)
        let high = ProgressAnimationMetrics.cellOpacity(isFilled: false, column: 20, columns: 30,
            mode: .awayPulse, timestamp: duration * 0.5)
        #expect(abs(low - 0.10) < 0.00001)
        #expect(abs(high - 0.35) < 0.00001)
        #expect(StyledProgressView.waveformCellOpacity(isFilled: false, progress: 0.5,
            moving: false, pulsing: true, phase: 0.5) == 0.35)
        #expect(StyledProgressView.waveformCellOpacity(isFilled: false, progress: 0.5,
            moving: false, pulsing: false, phase: 0.25) == 0.18)
    }

    @Test func fixedSystemBlurIgnoresLegacyFrostingWithoutBackgroundTint() {
        let foreground = NSView(frame: NSRect(x: 0, y: 0, width: 344, height: 92))
        let view = LiquidGlassContentView(foreground: foreground)
        view.setBackgroundStyle(.glass)
        view.refreshMaterial(reduceTransparency: false)
        view.layoutSubtreeIfNeeded()
        let effect = view.effect
        for value in [0.0, 25, 50, 75, 100, .nan, .infinity] {
            view.setFrosting(value)
            #expect(view.effect === effect)
            #expect(effect?.alphaValue == 0.25)
            #expect(effect?.isHidden == false)
            #expect(view.subviews.count == 3)
            #expect(view.subviews.first === effect)
            #expect(view.subviews.allSatisfy {
                $0.layer?.name != "glassMaterialShade" && $0.layer?.name != "simulatedGlassBevel"
            })
            #expect(foreground.alphaValue == 1)
        }
        view.refreshMaterial(reduceTransparency: true)
        #expect(view.effect == nil)
        #expect(view.layer?.backgroundColor?.alpha == 1)
        view.refreshMaterial(reduceTransparency: false)
        #expect(view.effect?.alphaValue == 0.25)
        view.setBackgroundStyle(.standard)
        #expect(view.effect == nil)
        #expect(view.layer?.backgroundColor?.alpha == 0)
    }

    @Test func edgeHighlightsStayBelowForegroundAndNeverInterceptInput() {
        let foreground = NSView(frame: NSRect(x: 0, y: 0, width: 344, height: 92))
        let view = LiquidGlassContentView(foreground: foreground)
        view.setBackgroundStyle(.glass)
        view.refreshMaterial(reduceTransparency: false)
        view.layoutSubtreeIfNeeded()
        let edge = view.subviews.first { $0.layer?.name == "systemBlurEdgeHighlights" }
        #expect(edge != nil)
        #expect(view.subviews[1] === edge)
        #expect(edge?.isHidden == false)
        #expect(edge?.frame == view.bounds)
        #expect(edge?.hitTest(NSPoint(x: 1, y: 46)) == nil)
        #expect(view.hitTest(NSPoint(x: 1, y: 46)) === foreground)
        let bands = edge?.layer?.sublayers?.compactMap { $0 as? CAGradientLayer } ?? []
        #expect(bands.count == 2)
        for band in bands {
            let colors = band.colors as? [CGColor] ?? []
            #expect(colors.count == 5)
            #expect(colors[2].alpha == 0 && colors[3].alpha == 0)
            #expect((band.mask as? CAShapeLayer)?.path != nil)
            #expect(band.animationKeys()?.isEmpty != false)
        }
        #expect(view.effect?.alphaValue == 0.25)
        view.refreshMaterial(reduceTransparency: true)
        #expect(edge?.isHidden == true)
        view.setBackgroundStyle(.standard)
        #expect(edge?.isHidden == true)
    }

    @Test func glassOwnsForegroundWithoutChangingHitGeometryOrRecreatingMaterial() {
        let foreground = NSView(frame: NSRect(x: 0, y: 0, width: 344, height: 92))
        let view = LiquidGlassContentView(foreground: foreground)
        view.setBackgroundStyle(.glass)
        view.layoutSubtreeIfNeeded()
        let effect = view.effect
        view.setBackgroundStyle(.glass)
        #expect(view.effect === effect)
        #expect(view.layer?.borderWidth == 0)
        #expect(foreground.convert(foreground.bounds, to: view) == view.bounds)
        if !NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency {
            let glass = view.effect
            #expect(glass != nil)
            #expect(glass?.material == .hudWindow)
            #expect(glass?.blendingMode == .behindWindow)
            #expect(glass?.state == .active)
            #expect(glass?.alphaValue == 0.25)
            #expect(view.layer?.cornerRadius == FocusDesign.Window.cornerRadius)
            #expect(view.effect?.frame == view.bounds)
            #expect(foreground.alphaValue == 1)
            #expect(foreground.superview !== glass)
            #expect(view.subviews.filter { $0 is NSVisualEffectView }.count == 1)
            #expect(view.effect?.maskImage == nil)
            #expect(view.subviews.allSatisfy { $0.layer?.borderWidth ?? 0 == 0 })
            #expect(!(view.layer?.sublayers?.contains { $0 is CAShapeLayer } ?? false))
            view.needsLayout = true
            view.layoutSubtreeIfNeeded()
            #expect(view.effect === effect)
        }
        #expect(foreground.convert(foreground.bounds, to: view) == view.bounds)
        for x in [CGFloat(1), 343] {
            #expect(view.hitTest(NSPoint(x: x, y: 46)) === foreground)
        }
        for scale in [CGFloat(0.5), 1, 2] {
            view.frame.size = NSSize(width: 344 * scale, height: 92 * scale)
            view.needsLayout = true
            view.layoutSubtreeIfNeeded()
            #expect(foreground.convert(foreground.bounds, to: view) == view.bounds)
            #expect(view.effect === effect)
            for x in [CGFloat(1), view.bounds.width - 1] {
                #expect(view.hitTest(NSPoint(x: x, y: view.bounds.midY)) === foreground)
            }
        }
        view.frame.size = NSSize(width: 344, height: 92)
        view.refreshMaterial(reduceTransparency: true)
        view.layoutSubtreeIfNeeded()
        #expect(view.effect == nil)
        #expect(view.layer?.backgroundColor?.alpha == 1)
        #expect(foreground.alphaValue == 1)
        #expect(view.subviews.first { $0.layer?.name == "simulatedGlassBevel" } == nil)
        view.refreshMaterial(reduceTransparency: false)
        view.layoutSubtreeIfNeeded()
        #expect(view.effect?.state == .active)
        #expect(view.layer?.backgroundColor?.alpha == GlassAppearanceMetrics.interactionBackingOpacity)
        view.setBackgroundStyle(.standard)
        view.layoutSubtreeIfNeeded()
        #expect(view.effect == nil)
        #expect(foreground.convert(foreground.bounds, to: view) == view.bounds)
        #expect(view.subviews.first { $0.layer?.name == "simulatedGlassBevel" } == nil)
        for x in [CGFloat(1), 343] {
            #expect(view.hitTest(NSPoint(x: x, y: 46)) === foreground)
        }
    }

    @Test func glassClipsHostedScrollingContentToTheSameViewportAtEveryScale() {
        // Exercise an NSHostingView, not only a plain NSView: settings pages
        // contain a hosted scroll viewport whose document exceeds the HUD.
        let foreground = NSHostingView(rootView:
            ScrollView {
                VStack { ForEach(0..<30) { Text("Row \($0)").frame(height: 34) } }
            }.frame(width: 344 * 0.82, height: 92 * 0.82)
        )
        foreground.sizingOptions = []
        let view = LiquidGlassContentView(foreground: foreground)
        view.setBackgroundStyle(.glass)
        view.refreshMaterial(reduceTransparency: false)
        for scale in [0.5, 0.82, 1.0] {
            view.frame.size = FocusDesign.Window.scaledSize(for: scale)
            view.needsLayout = true
            view.layoutSubtreeIfNeeded()
            #expect(view.clipsToBounds)
            #expect(foreground.clipsToBounds)
            #expect(foreground.superview?.clipsToBounds == true)
            #expect(view.layer?.masksToBounds == true)
            #expect(foreground.superview?.layer?.masksToBounds == true)
            #expect(view.layer?.cornerCurve == .continuous)
            #expect(foreground.convert(foreground.bounds, to: view) == view.bounds)
            #expect(view.effect?.frame == view.bounds)
            let actualRadius = view.layer?.cornerRadius ?? -1
            let expectedRadius = FocusDesign.Window.cornerRadius * scale
            #expect(abs(actualRadius - expectedRadius) < 0.0001, "frame=\(view.frame), bounds=\(view.bounds), foreground=\(foreground.frame)")
            #expect(foreground.superview?.layer?.cornerRadius == view.layer?.cornerRadius)
        }
        view.setBackgroundStyle(.standard)
        view.layoutSubtreeIfNeeded()
        #expect(!view.clipsToBounds)
        #expect(!foreground.clipsToBounds)
        #expect(foreground.superview?.clipsToBounds == false)
        #expect(view.layer?.backgroundColor?.alpha == 0)
    }

    @Test func fixedBlurKeepsAStableInteractionBackingAndControlsKeepTheirHitTargets() {
        let foreground = NSView(frame: NSRect(x: 0, y: 0, width: 344, height: 92))
        let control = NSButton(frame: NSRect(x: 200, y: 30, width: 60, height: 24))
        foreground.addSubview(control)
        let view = LiquidGlassContentView(foreground: foreground)
        view.setBackgroundStyle(.glass)
        view.refreshMaterial(reduceTransparency: false)
        view.layoutSubtreeIfNeeded()
        for frosting in [0.0, 50, 100] {
            view.setFrosting(frosting)
            #expect(view.layer?.backgroundColor?.alpha == GlassAppearanceMetrics.interactionBackingOpacity)
            #expect(view.hitTest(NSPoint(x: 80, y: 40)) === foreground)
            #expect(view.hitTest(NSPoint(x: 230, y: 42)) === control)
        }
    }

    @Test func pixelColumnsAdaptAtScaleBoundaries() {
        #expect(ProgressAnimationMetrics.columnCount(forScale: 0.5) == 10)
        #expect(ProgressAnimationMetrics.columnCount(forScale: 0.64) == 10)
        #expect(ProgressAnimationMetrics.columnCount(forScale: 0.65) == 16)
        #expect(ProgressAnimationMetrics.columnCount(forScale: 0.81) == 16)
        #expect(ProgressAnimationMetrics.columnCount(forScale: 0.82) == 30)
        #expect(ProgressAnimationMetrics.columnCount(forScale: 1) == 30)
    }

    @Test func completedProgressKeepsWorkingScanButNeverEnablesCompletionBreathing() {
        #expect(ProgressAnimationMetrics.animationMode(progress: 0.99, isActive: true, isAway: false, animationsEnabled: true) == .scanning)
        let completed = ProgressAnimationMetrics.animationMode(progress: 1, isActive: true, isAway: false, animationsEnabled: true)
        #expect(completed == .scanning)
        #expect(ProgressAnimationMetrics.animationMode(progress: 1.4, isActive: true, isAway: false, animationsEnabled: true) == .scanning)
        #expect(ProgressAnimationMetrics.scanEnabled(for: completed))
        #expect(ProgressAnimationMetrics.animationMode(progress: 1, isActive: true, isAway: false, animationsEnabled: false) == .staticDisplay)
        #expect(ProgressAnimationMetrics.animationMode(progress: 1, isActive: false, isAway: true, animationsEnabled: true) == .staticDisplay)
    }

    @Test func menuBarArtworkUsesSystemTimeAndSingleProgressRow() {
        #expect(IslandPanelController.statusItemAutosaveName == "WorkIsland.StatusItem.System.v2")
        #expect(IslandPanelController.shouldRecoverHiddenStatusItem(displayMode: .menuBar, isVisible: false))
        #expect(!IslandPanelController.shouldRecoverHiddenStatusItem(displayMode: .menuBar, isVisible: true))
        #expect(!IslandPanelController.shouldRecoverHiddenStatusItem(displayMode: .floating, isVisible: false))
        #expect(IslandPanelController.shouldRecoverUnavailableStatusItem(displayMode: .menuBar, hasUsableFrame: false))
        #expect(!IslandPanelController.shouldRecoverUnavailableStatusItem(displayMode: .menuBar, hasUsableFrame: true))
        #expect(!IslandPanelController.shouldRecoverUnavailableStatusItem(displayMode: .floating, hasUsableFrame: false))
        #expect(IslandPanelController.statusItemDragThreshold == 4)
        #expect(StatusItemArtwork.litCount(progress: 0) == 0)
        #expect(StatusItemArtwork.litCount(progress: 0.5) == 5)
        #expect(StatusItemArtwork.litCount(progress: 1) == 10)
        #expect(StatusItemArtwork.litCount(progress: 1.3) == FocusDesign.Matrix.menuBarColumns)
        #expect(FocusDesign.AppKitToken.statusTextColor == NSColor.white)
        #expect(FocusDesign.AppKitToken.statusTextFont.fontName == NSFont.systemFont(ofSize: FocusDesign.AppKitToken.statusTextSize, weight: .medium).fontName)
        #expect(FocusDesign.AppKitToken.statusTextKerning == 0)
        #expect(FocusDesign.AppKitToken.statusTextBottom == 8)
        #expect(FocusDesign.AppKitToken.statusContentOffsetX == 2)
        #expect(FocusDesign.Matrix.menuBarColumns == 10)
        #expect(FocusDesign.Window.menuBarScale == 0.82)

        let image = StatusItemArtwork.make(
            time: "1H 17M",
            progress: 0.5,
            color: .systemGreen,
            mode: .staticDisplay,
            timestamp: 0
        )
        let bitmap = image.tiffRepresentation.flatMap(NSBitmapImageRep.init(data:))
        var visiblePixels = 0
        if let bitmap {
            for y in 0..<bitmap.pixelsHigh {
                for x in 0..<bitmap.pixelsWide where (bitmap.colorAt(x: x, y: y)?.alphaComponent ?? 0) > 0.01 {
                    visiblePixels += 1
                }
            }
        }
        #expect(bitmap != nil)
        #expect(visiblePixels > 100)
    }

    @Test func menuBarBackgroundUsesAButtonSizedCapsule() {
        let rect = NSRect(x: 0, y: 0, width: 78, height: 24)
        let path = StatusItemArtwork.backgroundPath(in: rect)
        #expect(path.bounds == rect)
        #expect(path.contains(NSPoint(x: rect.midX, y: 0.5)))
        #expect(path.contains(NSPoint(x: rect.midX, y: 23.5)))
        #expect(!path.contains(NSPoint(x: 0.5, y: 0.5)))
        #expect(!path.contains(NSPoint(x: 0.5, y: 23.5)))
    }

    @Test func menuBarPopoverClosesOnlyForOutsideMouseClicks() {
        let popover = NSRect(x: 20, y: 20, width: 100, height: 80)
        let status = NSRect(x: 60, y: 100, width: 50, height: 24)
        #expect(!IslandPanelController.shouldCloseMenuPopover(for: .leftMouseDown, mouseLocation: NSPoint(x: 40, y: 40), popoverFrame: popover, statusItemFrame: status))
        #expect(!IslandPanelController.shouldCloseMenuPopover(for: .leftMouseDown, mouseLocation: NSPoint(x: 70, y: 110), popoverFrame: popover, statusItemFrame: status))
        #expect(IslandPanelController.shouldCloseMenuPopover(for: .leftMouseDown, mouseLocation: NSPoint(x: 200, y: 200), popoverFrame: popover, statusItemFrame: status))
        #expect(!IslandPanelController.shouldCloseMenuPopover(for: .scrollWheel, mouseLocation: NSPoint(x: 200, y: 200), popoverFrame: popover, statusItemFrame: status))
    }

    @Test func menuBarOpensDirectlyWithoutTheFloatingRootTransition() {
        #expect(IslandRootView.shouldAnimateRootTransition(reduceMotion: false, isMenuBarPresentation: false))
        #expect(!IslandRootView.shouldAnimateRootTransition(reduceMotion: true, isMenuBarPresentation: false))
        #expect(!IslandRootView.shouldAnimateRootTransition(reduceMotion: false, isMenuBarPresentation: true))
        #expect(!IslandRootView.shouldAnimateRootTransition(reduceMotion: true, isMenuBarPresentation: true))
    }

    @Test func autoHiddenMenuBarGeometryStillCountsAsANativeStatusItem() {
        #expect(IslandPanelController.isNativeStatusItemGeometryUsable(
            NSRect(x: 1092, y: 13.5, width: 80, height: 3)
        ))
        #expect(IslandPanelController.isNativeStatusItemGeometryUsable(
            NSRect(x: 1092, y: 3, width: 80, height: 24)
        ))
        #expect(!IslandPanelController.isNativeStatusItemGeometryUsable(.zero))
        #expect(!IslandPanelController.isNativeStatusItemGeometryUsable(
            NSRect(x: 0, y: 0, width: 2, height: 3)
        ))
    }

    @Test func menuBarAnimationMatchesTrackingState() {
        let scanning = StatusItemArtwork.animationMode(progress: 0.5, state: .tracking(applicationName: "Xcode"), animationsEnabled: true)
        let completed = StatusItemArtwork.animationMode(progress: 1, state: .tracking(applicationName: "Xcode"), animationsEnabled: true)
        #expect(scanning == .scanning)
        #expect(completed == .scanning)
        #expect(StatusItemArtwork.animationMode(progress: 0.5, state: .away, animationsEnabled: true) == .awayPulse)
        #expect(StatusItemArtwork.animationMode(progress: 0.5, state: .paused, animationsEnabled: true) == .staticDisplay)
        #expect(StatusItemArtwork.animationMode(progress: 0.5, state: .tracking(applicationName: "Xcode"), animationsEnabled: false) == .staticDisplay)

        let beforeScan = StatusItemArtwork.pixelOpacity(column: 0, litCount: 8, columns: 16, mode: scanning, timestamp: 0)
        let duringScan = StatusItemArtwork.pixelOpacity(column: 0, litCount: 8, columns: 16, mode: scanning, timestamp: 1.0 / 3.0)
        #expect(duringScan > beforeScan)
        let awayLow = StatusItemArtwork.pixelOpacity(column: 15, litCount: 8, columns: 16, mode: .awayPulse, timestamp: 0)
        let awayHigh = StatusItemArtwork.pixelOpacity(column: 15, litCount: 8, columns: 16, mode: .awayPulse, timestamp: 0.5)
        #expect(awayHigh > awayLow)

        #expect(IslandPanelController.shouldAnimateStatusItem(displayMode: .menuBar, state: .away, reduceMotion: false, statusItemInstalled: true))
        #expect(!IslandPanelController.shouldAnimateStatusItem(displayMode: .floating, state: .away, reduceMotion: false, statusItemInstalled: true))
        #expect(!IslandPanelController.shouldAnimateStatusItem(displayMode: .menuBar, state: .paused, reduceMotion: false, statusItemInstalled: true))
        #expect(!IslandPanelController.shouldAnimateStatusItem(displayMode: .menuBar, state: .away, reduceMotion: true, statusItemInstalled: true))

        #expect(FocusDesign.Motion.frameRate == 24)
        let staticFrame = StatusItemArtwork.make(
            time: "1H 17M", progress: 0.5, color: .systemGreen,
            mode: .staticDisplay, timestamp: 0
        )
        #expect(staticFrame.size == NSSize(
            width: FocusDesign.AppKitToken.statusArtworkWidth,
            height: FocusDesign.AppKitToken.statusArtworkHeight
        ))

        let totalWidth = CGFloat(FocusDesign.Matrix.menuBarColumns) * FocusDesign.Matrix.menuBarPixelWidth
            + CGFloat(FocusDesign.Matrix.menuBarColumns - 1) * FocusDesign.Matrix.menuBarPixelGap
        let buttonBounds = NSRect(x: 0, y: 0, width: 78, height: 24)
        let flippedFrame = StatusItemProgressOverlayView.overlayFrame(in: buttonBounds, flipped: true)
        let unflippedFrame = StatusItemProgressOverlayView.overlayFrame(in: buttonBounds, flipped: false)
        #expect(flippedFrame == NSRect(x: 19, y: 17.5, width: totalWidth, height: 3.5))
        #expect(unflippedFrame == NSRect(x: 19, y: 3, width: totalWidth, height: 3.5))
        let overlay = StatusItemProgressOverlayView(frame: NSRect(
            x: 0, y: 0, width: totalWidth, height: FocusDesign.Matrix.menuBarPixelHeight
        ))
        overlay.layoutSubtreeIfNeeded()
        overlay.update(progress: 0.5, color: .systemGreen, mode: .scanning, awayElapsed: nil)
        #expect(overlay.scanAnimation?.duration == FocusDesign.Motion.scanDuration)
        #expect(overlay.scanAnimation?.repeatCount == .infinity)
        #expect(overlay.hitTest(NSPoint(x: totalWidth / 2, y: 1)) == nil)
        overlay.update(progress: 0.5, color: .systemOrange, mode: .awayPulse, awayElapsed: 0)
        #expect(overlay.scanAnimation == nil)
        #expect(overlay.pulseAnimation?.duration == FocusDesign.Motion.awayPulseDuration / 2)
        #expect(overlay.pulseAnimation?.autoreverses == true)

        let transition = IslandPanelController.statusColorTransition(animated: true)
        #expect(transition?.type == .fade)
        #expect(transition?.duration == FocusDesign.Motion.statusTransitionDuration)
        #expect(IslandPanelController.statusColorTransition(animated: false) == nil)
    }

    @Test func persistentVerticalScrollerMatches081ArtworkWithoutSwiftUIPositionFeedback() {
        let scrollView = ScrollWheelRecordingScrollView(frame: NSRect(x: 0, y: 0, width: 240, height: 100))
        let document = NSView(frame: NSRect(x: 0, y: 0, width: 220, height: 400))
        scrollView.documentView = document
        scrollView.layoutSubtreeIfNeeded()

        let bridge = PersistentScrollBridge()
        bridge.attach(scrollView: scrollView)
        let scroller = PersistentCapsuleScrollbarView(bridge: bridge)
        scroller.frame = NSRect(x: 0, y: 0, width: 10, height: 72)
        bridge.register(scrollbarView: scroller)

        #expect(!scrollView.hasVerticalScroller)
        #expect(PersistentScrollbarMetrics.contentSpacing == 3)
        #expect(PersistentScrollbarMetrics.visualWidth == 10)
        #expect(PersistentScrollbarMetrics.verticalInset == 14)
        #expect(PersistentScrollbarMetrics.trailingInset == 10)
        #expect(PersistentScrollbarMetrics.minimumKnobLength == 18)
        #expect(PersistentScrollbarMetrics.slotOpacity == 0.08)
        #expect(PersistentScrollbarMetrics.knobOpacity == 0.5)
        #expect(PersistentScrollbarMetrics.knobLength(
            trackHeight: 72, contentHeight: 400, viewportHeight: 100
        ) == 18)
        #expect(PersistentScrollbarMetrics.knobOffset(
            trackHeight: 72, knobLength: 18, contentOffset: 150,
            contentHeight: 400, viewportHeight: 100
        ) == 27)
        #expect(PersistentScrollbarMetrics.knobLength(
            trackHeight: 72, contentHeight: 100, viewportHeight: 100
        ) == 72)
        #expect(scroller.isAccessibilityElement())
        #expect(scroller.accessibilityRole() == .scrollBar)
        #expect(scroller.accessibilityLabel() == L10n.text("滚动设置"))

        bridge.scroll(toFraction: 1)
        #expect(bridge.geometry.contentOffset > 0)
        #expect(scroller.accessibilityPerformDecrement())

        let cgEvent = CGEvent(
            scrollWheelEvent2Source: nil,
            units: .pixel,
            wheelCount: 1,
            wheel1: 24,
            wheel2: 0,
            wheel3: 0
        )!
        let wheelEvent = NSEvent(cgEvent: cgEvent)!
        scroller.scrollWheel(with: wheelEvent)
        #expect(scrollView.receivedScrollWheelEvent === wheelEvent)
    }

    @Test func matrixProgressCountsEachColumnFromTopToBottom() {
        let rows = FocusDesign.Matrix.rows
        #expect(rows == 3)
        #expect(LayerAnimatedProgressNSView.matrixElementIndex(column: 0, row: 2, rows: rows) == 0)
        #expect(LayerAnimatedProgressNSView.matrixElementIndex(column: 0, row: 1, rows: rows) == 1)
        #expect(LayerAnimatedProgressNSView.matrixElementIndex(column: 0, row: 0, rows: rows) == 2)
        #expect(LayerAnimatedProgressNSView.matrixElementIndex(column: 1, row: 2, rows: rows) == 3)
    }

    @Test func statusColorTransitionIsSharedByEveryThemeAndRenderer() {
        #expect(FocusDesign.Motion.statusTransitionDuration == 0.35)
        for accent in ThemeAccent.allCases {
            let components = StatusColorComponents(accent.color)
            #expect(StatusColorComponents(FocusDesign.Status.color(
                for: .tracking(applicationName: "Any"), trackingAccent: accent.color
            )) == components)
            for value in [components.red, components.green, components.blue, components.alpha] {
                #expect(value.isFinite)
                #expect((0...1).contains(value))
            }
        }

        let working = StatusColorComponents(ThemeAccent.blue.color)
        let away = StatusColorComponents(FocusDesign.ColorToken.warning)
        let blend = StatusColorBlend(
            from: working,
            to: away,
            startedAt: 10,
            duration: FocusDesign.Motion.statusTransitionDuration
        )
        #expect(blend.components(at: 9) == working)
        #expect(blend.components(at: 10) == working)
        #expect(blend.components(at: 10 + FocusDesign.Motion.statusTransitionDuration) == away)
        #expect(!blend.isComplete(at: 10.1))
        #expect(blend.isComplete(at: 10 + FocusDesign.Motion.statusTransitionDuration))

        let halfway = blend.components(at: 10 + FocusDesign.Motion.statusTransitionDuration / 2)
        #expect(abs(halfway.red - (working.red + away.red) / 2) < 0.000_001)
        #expect(abs(halfway.green - (working.green + away.green) / 2) < 0.000_001)
        #expect(abs(halfway.blue - (working.blue + away.blue) / 2) < 0.000_001)
    }

    @Test func durationFormatsSupportBothStyles() {
        #expect(TimeFormatting.compact(3_661, style: .hoursMinutes) == "1H 01M")
        #expect(TimeFormatting.compact(3_661, style: .minutesSeconds) == "61M 01S")
        // The test process has its own selected/system language; do not assume
        // Chinese or change the user's application/language preferences.
        let english = L10n.language == .english
        #expect(TimeFormatting.readable(3_661, style: .hoursMinutes) ==
            (english ? "1 hours 1 min" : "1 小时 1 分钟"))
        #expect(TimeFormatting.readable(3_661, style: .minutesSeconds) ==
            (english ? "61 min 1 sec" : "61 分钟 1 秒"))
    }

    @Test func navigationBackReturnsToHomeBeforeCollapsing() {
        let presentation = IslandPresentationModel()
        presentation.isExpanded = true
        presentation.open(.history)
        #expect(presentation.page == .history)
        #expect(presentation.isExpanded)

        presentation.goBack()
        #expect(presentation.page == .home)
        #expect(presentation.isExpanded)

        presentation.goBack()
        #expect(!presentation.isExpanded)
        #expect(presentation.page == .home)
    }

    @Test func navigationKeepsTheSamePanelSizeAtEveryScale() {
        #expect(abs(IslandPanelController.panelSize(forScale: 0.5).width - FocusDesign.Window.baseWidth * 0.5) < 0.001)
        #expect(abs(IslandPanelController.panelSize(forScale: 0.82).height - FocusDesign.Window.baseHeight * 0.82) < 0.001)
        #expect(IslandPanelController.panelSize(forScale: 1).width == FocusDesign.Window.baseWidth)
        #expect(IslandPanelController.panelSize(forScale: 0.83) == FocusDesign.Window.scaledSize(for: 0.83))
    }

    @Test func navigationDirectionFollowsHierarchy() {
        let presentation = IslandPresentationModel()
        presentation.showNavigation()
        #expect(presentation.transitionDirection == 1)
        presentation.open(.settings)
        #expect(presentation.transitionDirection == 1)
        presentation.goBack()
        #expect(presentation.transitionDirection == -1)
    }

    @Test func navigationStackReturnsToTheActualParentPage() {
        let presentation = IslandPresentationModel()
        presentation.showNavigation()
        presentation.open(.history)
        presentation.openHistoryStats()
        presentation.openHistoryDetail(.init(dayKey: "2026-08-24", totalSeconds: 10, applications: []))
        presentation.goBack()
        #expect(presentation.page == .historyStats)
        presentation.goBack()
        #expect(presentation.page == .history)

        presentation.resetToNavigationHome()
        presentation.open(.recognition)
        presentation.open(.browserPermissions)
        presentation.goBack()
        #expect(presentation.page == .recognition)
    }

    @Test func recognitionSectionsStartCollapsed() {
        let presentation = IslandPresentationModel()
        #expect(!presentation.isRecognitionApplicationsExpanded)
        #expect(!presentation.isRecognitionWebsitesExpanded)

        presentation.showNavigation()
        presentation.open(.recognition)
        presentation.isRecognitionWebsitesExpanded = true
        presentation.open(.browserPermissions)
        presentation.goBack()
        #expect(presentation.isRecognitionWebsitesExpanded)

        presentation.goBack()
        #expect(!presentation.isRecognitionApplicationsExpanded)
        #expect(!presentation.isRecognitionWebsitesExpanded)
    }

    @Test func navigationAndTypographyTokensMatchTheDesign() {
        let mainOrbCenter = FocusDesign.Spacing.mainHorizontal + FocusDesign.Size.statusOrb / 2
        let homeBackCenter = FocusDesign.Navigation.homeLeadingPadding
            + FocusDesign.Size.navigationBackWidth / 2
            + FocusDesign.Navigation.homeBackOffset
        let detailBackCenter = FocusDesign.Navigation.detailLeadingPadding + FocusDesign.Size.detailBackHitArea / 2
        #expect(mainOrbCenter == 35)
        #expect(homeBackCenter == mainOrbCenter - 14)
        #expect(FocusDesign.Size.navigationBackWidth == FocusDesign.Size.navigationItemHeight)
        #expect(detailBackCenter == mainOrbCenter)
        #expect(FocusDesign.Typography.settingsLabel == 13)
        #expect(FocusDesign.Typography.settingsValue == 12)
        #expect(FocusDesign.Typography.detailBody == 13)
        #expect(FocusDesign.Typography.detailCaption == 12)
        #expect(FocusDesign.Size.detailRowHeight == 32)
        #expect(FocusDesign.Size.settingsRowHeight == 34)
        #expect(FocusDesign.Size.exitRowHeight == 32)
        #expect(FocusDesign.Size.settingsToggleWidth == 30)
        #expect(FocusDesign.Size.pauseButtonWidth == 78)
        #expect(FocusDesign.Size.pauseButtonHeight == 60)
        #expect(FocusDesign.Size.settingsPickerWidth == 96)
        #expect(ThemeAccent.allCases == [.green, .blue, .yellow, .cyan, .pink, .orange, .purple])
        #expect(FocusDesign.Size.settingsPickerHeight == 24)
        #expect(FocusDesign.Spacing.list == 0)
        #expect(FocusDesign.Motion.statusTransitionDuration == 0.35)
        #expect(FocusDesign.Motion.focusLossDelay == 1)
    }

    @Test func menuBarGradientFollowsTrackingState() {
        let tracking = StatusItemArtwork.gradientColors(for: .tracking)
        let away = StatusItemArtwork.gradientColors(for: .away)
        let paused = StatusItemArtwork.gradientColors(for: .paused)
        #expect(tracking.count == 2)
        #expect(tracking[0] == FocusDesign.AppKitToken.statusGradientStart)
        #expect(tracking[1] != away[1])
        #expect(away[1] != paused[1])
        #expect(paused[1] != tracking[1])
    }

    @Test func historyMatrixFillsBottomUpAndLeftToRight() {
        #expect(FocusDesign.Matrix.statisticsContentVerticalOffset == -5)
        #expect(StatisticsPixelMatrix.goalProgress(seconds: 0, goalSeconds: 14_400) == 0)
        #expect(StatisticsPixelMatrix.goalProgress(seconds: 7_200, goalSeconds: 14_400) == 0.5)
        #expect(StatisticsPixelMatrix.goalProgress(seconds: 14_400, goalSeconds: 14_400) == 1)
        #expect(StatisticsPixelMatrix.goalProgress(seconds: 18_000, goalSeconds: 14_400) == 1)
        #expect(StatisticsPixelMatrix.litCount(progress: 0) == 0)
        #expect(StatisticsPixelMatrix.litCount(progress: 0.5) == 8)
        #expect(StatisticsPixelMatrix.litCount(progress: 1.4) == 16)
        #expect(StatisticsPixelMatrix.isLit(row: 0, column: 0, litCount: 3))
        #expect(StatisticsPixelMatrix.isLit(row: 0, column: 1, litCount: 3))
        #expect(StatisticsPixelMatrix.isLit(row: 1, column: 0, litCount: 3))
        #expect(!StatisticsPixelMatrix.isLit(row: 1, column: 1, litCount: 3))
    }

    @Test func addingWebsiteRevealsTheCollapsedSection() {
        let presentation = IslandPresentationModel()
        #expect(!presentation.isRecognitionWebsitesExpanded)
        presentation.prepareForWebsiteAddition()
        #expect(presentation.isRecognitionWebsitesExpanded)
    }

    @Test func verticalWheelDeltaMapsToClampedHorizontalOffset() {
        #expect(HorizontalWheelCapture.targetOffset(current: 100, verticalDelta: -2, maximum: 300, precise: true) == 102)
        #expect(HorizontalWheelCapture.targetOffset(current: 100, verticalDelta: -2, maximum: 300, precise: false) == 136)
        #expect(HorizontalWheelCapture.targetOffset(current: 5, verticalDelta: 20, maximum: 300, precise: false) == 0)
        #expect(HorizontalWheelCapture.targetOffset(current: 295, verticalDelta: -20, maximum: 300, precise: false) == 300)
    }

    @Test func focusLossAppliesToEveryExpandedPage() {
        for page in IslandNavigationPage.allCases {
            let presentation = IslandPresentationModel()
            presentation.showNavigation()
            if page != .home { presentation.open(page) }
            #expect(IslandPanelController.shouldScheduleFocusLoss(
                displayMode: .floating,
                isExpanded: presentation.isExpanded
            ))
        }

        #expect(!IslandPanelController.shouldScheduleFocusLoss(displayMode: .menuBar, isExpanded: true))
        #expect(!IslandPanelController.shouldScheduleFocusLoss(displayMode: .floating, isExpanded: false))
    }

    @Test func browserPermissionEntryHidesWhileWebsiteRulesAreExpanded() {
        let presentation = IslandPresentationModel()
        #expect(presentation.shouldShowBrowserPermissionEntry)
        presentation.isRecognitionWebsitesExpanded = true
        #expect(!presentation.shouldShowBrowserPermissionEntry)
    }

    @Test func focusLossReturnWaitsAndCanBeCancelled() async throws {
        let coordinator = FocusLossReturnCoordinator()
        var returns = 0
        coordinator.schedule(delay: 0.04) { returns += 1 }
        // Assert and cancel in one main-actor turn. A fixed sleep cannot prove
        // "not yet" when unrelated UI work can delay this test past the deadline.
        #expect(returns == 0)
        #expect(coordinator.isPending)
        coordinator.cancel()
        #expect(!coordinator.isPending)

        coordinator.schedule(delay: 0.01) { returns += 1 }
        let deadline = ContinuousClock.now.advanced(by: .seconds(10))
        while returns == 0, ContinuousClock.now < deadline {
            try await Task.sleep(for: .milliseconds(5))
        }
        #expect(returns == 1)
        #expect(!coordinator.isPending)
    }

    @Test func awayAndManualPauseHaveDistinctVisualMappings() {
        #expect(FocusDesign.Status.icon(for: .away) == "cup.and.saucer.fill")
        #expect(FocusDesign.Status.icon(for: .paused) == "pause.fill")
        #expect(!FocusDesign.Status.isManuallyPaused(.away))
        #expect(FocusDesign.Status.isManuallyPaused(.paused))
    }
}
