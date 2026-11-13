import AppKit
import SwiftUI

enum FocusDesign {
    enum ColorToken {
        static let accent = Color(red: 0.25, green: 1, blue: 0.38)
        static let actionText = Color(nsColor: NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
                ? NSColor(red: 0.25, green: 1, blue: 0.38, alpha: 1) : .black
        })
        static let warning = Color.orange
        static let backgroundStart = Color.black.opacity(0.96)
        static let backgroundEnd = Color(red: 0.025, green: 0.055, blue: 0.04).opacity(0.96)
        static let border = Color.white.opacity(0.10)
        static let textPrimary = Color.primary
        // Constant opaque white: never dims or changes hue while away.
        static let glassTimerTitle = Color.white
        static let textSecondary = Color.primary.opacity(0.68)
        static let textMuted = Color.primary.opacity(0.38)
        static let surface = Color.white.opacity(0.055)
        static let pressedSurface = Color.white.opacity(0.08)
        static let pausedSurface = Color.white.opacity(0.12)
        static let pausedWashStart = Color.white.opacity(0.025)
        static let pausedWashEnd = Color.white.opacity(0.10)
        static let warningWashStart = Color.orange.opacity(0.04)
        static let warningWashEnd = Color.orange.opacity(0.20)
    }

    enum Window {
        static let baseWidth: CGFloat = 344
        static let baseHeight: CGFloat = 92
        static let cornerRadius: CGFloat = 26
        static let borderWidth: CGFloat = 1
        static let minimumScale = 0.5
        static let maximumScale = 1.0
        static let adaptiveThreshold = 0.82
        static let compactThreshold = 0.65
        static let menuBarScale = 0.82

        static func scaledSize(for scale: Double) -> CGSize {
            let value = min(max(scale, minimumScale), maximumScale)
            return CGSize(width: baseWidth * value, height: baseHeight * value)
        }
    }

    enum Spacing {
        static let list: CGFloat = 0
        static let xxSmall: CGFloat = 2
        static let xSmall: CGFloat = 4
        static let small: CGFloat = 6
        static let medium: CGFloat = 9
        static let large: CGFloat = 12
        static let xLarge: CGFloat = 14
        static let mainHorizontal: CGFloat = 18
    }

    enum Radius {
        static let small: CGFloat = 5
        static let control: CGFloat = 8
        static let surface: CGFloat = 10
        static let pixel: CGFloat = 1.5
    }

    enum Size {
        static let statusOrb: CGFloat = 34
        static let smallStatusOrb: CGFloat = 26
        static let compactProgressHeight: CGFloat = 14
        static let progressHeight: CGFloat = 24
        static let navigationBackWidth: CGFloat = 66
        static let navigationBackLayoutWidth: CGFloat = 28
        static let navigationBackHitArea: CGFloat = 38
        static let navigationItemWidth: CGFloat = 50
        static let navigationItemHeight: CGFloat = 66
        static let pauseButtonWidth: CGFloat = 78
        static let pauseButtonHeight: CGFloat = 60
        static let detailBackHitArea: CGFloat = 34
        static let detailRowHeight: CGFloat = 32
        static let settingsRowHeight: CGFloat = 34
        static let settingsControlWidth: CGFloat = 116
        static let settingsPickerWidth: CGFloat = 96
        static let settingsPickerHeight: CGFloat = 24
        static let settingsPickerCornerRadius: CGFloat = 4
        static let settingsPickerTextInset: CGFloat = 7
        static let settingsPickerTextTrailingClearance: CGFloat = 18
        static let settingsSliderWidth: CGFloat = 72
        static let settingsPercentageWidth: CGFloat = 38
        static let miniPixel: CGFloat = 4
        static let iconHitArea: CGFloat = 24
        static let historyDurationFieldWidth: CGFloat = 28
        static let browserPermissionDot: CGFloat = 6
        static let settingsSliderHeight: CGFloat = 14
        static let settingsSliderTrackHeight: CGFloat = 4
        static let settingsSliderThumb: CGFloat = 10
        static let settingsToggleWidth: CGFloat = 30
        static let settingsToggleHeight: CGFloat = 16
        static let settingsToggleThumb: CGFloat = 12
        static let exitRowHeight: CGFloat = 32
    }

    enum Typography {
        static let navigationMinimumScale: CGFloat = 0.8
        static let englishNavigationLabel: CGFloat = 10
        static let small: CGFloat = 10
        static let body: CGFloat = 11
        static let label: CGFloat = 12
        static let title: CGFloat = 13
        static let icon: CGFloat = 18
        static let navigationIcon: CGFloat = 20
        static let statisticsDate: CGFloat = 7
        static let settingsLabel: CGFloat = 13
        static let settingsValue: CGFloat = 12
        static let detailBody: CGFloat = 13
        static let detailCaption: CGFloat = 12
    }

    enum Opacity {
        static let hoverReduction = 0.25
        static let minimumWindow = 0.25
        static let activePixel = 0.72
        static let inactivePixel = 0.10
        static let scanStep = 0.10
        static let glow = 0.42
        static let activeOrbGlow = 0.60
        static let disabledText = 0.55
        static let navigationText = 0.82
        static let settingMinimum = 0.45
        static let awayTitle = 0.46
        static func mainTitle(isAway: Bool, isGlass: Bool) -> Double {
            isAway && !isGlass ? awayTitle : 1
        }
        static let awayInactivePixel = 0.25
        static let sliderTrack = 0.24
    }

    enum Motion {
        static let navigationDuration = 0.24
        static let controlDuration = 0.18
        static let statusTransitionDuration = 0.35
        static let focusLossDelay = 1.0
        static let pausePulseDuration = 0.9
        static let pausePulseScale = 0.86
        static let awayPulseDuration = 2.8
        static let awayIconScale = 1.08
        static let scanDuration = 2.8
        static let frameRate = 24.0
        static var navigation: Animation { .smooth(duration: navigationDuration) }
        static var control: Animation { .easeInOut(duration: controlDuration) }
        static var statusTransition: Animation { .easeInOut(duration: statusTransitionDuration) }
    }

    enum Navigation {
        static let primaryControlCenter = Spacing.mainHorizontal + Size.statusOrb / 2
        static let homeLeadingPadding = primaryControlCenter - Size.navigationBackLayoutWidth / 2
        static let homeBackOffset = -14 - (Size.navigationBackWidth - Size.navigationBackLayoutWidth) / 2
        static let homeBackTrailingCompensation = Size.navigationBackLayoutWidth - Size.navigationBackWidth
        static let detailLeadingPadding = primaryControlCenter - Size.detailBackHitArea / 2
    }

    enum Control {
        static let compactScroller: ControlSize = .mini
    }

    enum Matrix {
        static let rows = 3
        static let compactColumns = 10
        static let mediumColumns = 16
        static let fullColumns = 30
        static let gap: CGFloat = 2
        static let scanPadding = 8
        static let scanLeadingOffset = 4
        static let scanRadius = 3
        static let glowRadius: CGFloat = 5
        static let menuBarColumns = 10
        static let menuBarPixelWidth: CGFloat = 3.5
        static let menuBarPixelHeight: CGFloat = 3.5
        static let menuBarPixelGap: CGFloat = 1
        static let statisticsRows = 8
        static let statisticsColumns = 2
        static let statisticsPixelWidth: CGFloat = 5.25
        static let statisticsPixelHeight: CGFloat = 3
        static let statisticsGap: CGFloat = 1.5
        static let statisticsColumnSpacing: CGFloat = 6
        static let statisticsContentVerticalOffset: CGFloat = -5
        static let statisticsMatrixWidth = statisticsPixelWidth * CGFloat(statisticsColumns)
            + statisticsGap * CGFloat(statisticsColumns - 1)
    }

    enum AppKitToken {
        static let statusActive = NSColor.systemGreen
        static let statusAway = NSColor.systemOrange
        static let statusPaused = NSColor.secondaryLabelColor
        static let statusCapsuleRadiusRatio: CGFloat = 0.5
        static let statusGlowRadius: CGFloat = 2
        static let statusItemWidth: CGFloat = 78
        static let statusArtworkWidth: CGFloat = 74
        static let statusArtworkHeight: CGFloat = 22
        static let statusProgressBottom: CGFloat = 2
        static let statusTextBottom: CGFloat = 8
        static let statusTextSize: CGFloat = 9
        static let statusTextKerning: CGFloat = 0
        static let statusContentOffsetX: CGFloat = 2
        static let statusTextColor = NSColor.white
        static var statusTextFont: NSFont { NSFont.systemFont(ofSize: statusTextSize, weight: .medium) }
        static let statusGradientStart = NSColor.black.withAlphaComponent(0.96)

        static func statusGradientEnd(for state: Status.VisualState) -> NSColor {
            let color: NSColor
            let fraction: CGFloat
            switch state {
            case .tracking:
                color = statusActive
                fraction = 0.18
            case .away:
                color = statusAway
                fraction = 0.24
            case .paused:
                color = statusPaused
                fraction = 0.16
            }
            return NSColor.black.blended(withFraction: fraction, of: color) ?? NSColor.black
        }
    }

    enum Status {
        enum VisualState: Equatable {
            case tracking
            case away
            case paused
        }

        static func visualState(for state: TrackingEngine.State) -> VisualState {
            switch state {
            case .tracking: .tracking
            case .away: .away
            case .paused: .paused
            }
        }

        static func icon(for state: TrackingEngine.State) -> String {
            switch state {
            case .tracking: "clock.fill"
            case .away: "cup.and.saucer.fill"
            case .paused: "pause.fill"
            }
        }

        static func color(for state: TrackingEngine.State) -> Color {
            switch state {
            case .tracking: ColorToken.accent
            case .away: ColorToken.warning
            case .paused: ColorToken.textSecondary
            }
        }

        static func color(for state: TrackingEngine.State, trackingAccent: Color?) -> Color {
            if case .tracking = state, let trackingAccent { return trackingAccent }
            return color(for: state)
        }

        static func appKitColor(for state: TrackingEngine.State) -> NSColor {
            switch state {
            case .tracking: AppKitToken.statusActive
            case .away: AppKitToken.statusAway
            case .paused: AppKitToken.statusPaused
            }
        }

        static func isManuallyPaused(_ state: TrackingEngine.State) -> Bool {
            if case .paused = state { return true }
            return false
        }
    }
}

enum SettingsInteraction {
    static let onboardingReplayTapCount = 3
}

extension Color {
    static let focusGreen = FocusDesign.ColorToken.accent
}
