@preconcurrency import AppKit
import SwiftUI

struct ExpandedDashboardView: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @ObservedObject var engine: TrackingEngine
    @ObservedObject var presentation: IslandPresentationModel
    var onCloseNavigation: (() -> Void)? = nil
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ZStack {
            page
                .id(presentation.page)
                .transition(pageTransition)
        }
        .clipped()
        .foregroundStyle(SubpageForegroundStyle.primary)
        .animation(reduceMotion ? nil : FocusDesign.Motion.navigation, value: presentation.page)
    }

    @ViewBuilder
    private var page: some View {
        switch presentation.page {
        case .home:
            NavigationHomeView(
                engine: engine,
                presentation: presentation,
                onCloseNavigation: onCloseNavigation
            )
        case .settings:
            SettingsNavigationPage(engine: engine, presentation: presentation)
        case .history:
            HistoryNavigationPage(engine: engine, presentation: presentation)
        case .recognition:
            RecognitionNavigationPage(engine: engine, presentation: presentation)
        case .browserPermissions:
            BrowserPermissionsPage(presentation: presentation)
        case .historyStats:
            HistoryStatisticsPage(engine: engine, presentation: presentation)
        case .historyDetail:
            if let day = presentation.selectedHistoryDay { HistoryDetailPage(day: day, engine: engine, presentation: presentation) }
        }
    }

    private var pageTransition: AnyTransition {
        let forward = presentation.transitionDirection > 0
        return .asymmetric(
            insertion: .move(edge: forward ? .trailing : .leading).combined(with: .opacity),
            removal: .move(edge: forward ? .leading : .trailing).combined(with: .opacity)
        )
    }
}

private struct NavigationHomeView: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @ObservedObject var engine: TrackingEngine
    @ObservedObject var presentation: IslandPresentationModel
    let onCloseNavigation: (() -> Void)?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        HStack(spacing: FocusDesign.Spacing.xSmall) {
            Button { navigateBack() } label: {
                Image(systemName: "backward.end.fill")
                    .font(.system(size: FocusDesign.Typography.icon, weight: .bold))
                    .frame(width: FocusDesign.Size.navigationBackHitArea, height: FocusDesign.Size.navigationBackHitArea)
            }
            .buttonStyle(NavigationItemStyle())
            .frame(width: FocusDesign.Size.navigationBackWidth, height: FocusDesign.Size.navigationItemHeight)
            .offset(x: FocusDesign.Navigation.homeBackOffset)
            .padding(.trailing, FocusDesign.Navigation.homeBackTrailingCompensation)
            .help(L10n.text("返回计时页面"))

            NavigationItem(title: L10n.text("设置"), systemImage: "gearshape.fill") { navigate(to: .settings) }
                .offset(x: -6)
            NavigationItem(title: L10n.text("历史"), systemImage: "clock.arrow.circlepath") { navigate(to: .history) }
                .offset(x: -6)
            NavigationItem(title: L10n.text("识别"), systemImage: "square.grid.2x2") { navigate(to: .recognition) }
                .offset(x: -6)

            Button { engine.toggleManualPause() } label: {
                VStack(spacing: FocusDesign.Spacing.xSmall) {
                    Image(systemName: engine.isManuallyPaused ? "play.fill" : "pause.fill")
                        .font(.system(size: FocusDesign.Typography.navigationIcon, weight: .bold))
                    Text(engine.isManuallyPaused ? L10n.text("继续计时") : L10n.text("暂停计时"))
                        .font(.system(size: FocusDesign.Typography.body, weight: .bold))
                        .lineLimit(1)
                        .offset(y: 2)
                }
                .foregroundStyle(.black)
                .frame(width: FocusDesign.Size.pauseButtonWidth, height: FocusDesign.Size.pauseButtonHeight)
                .background(engine.settings.activeTheme?.accent.color ?? FocusDesign.ColorToken.accent, in: RoundedRectangle(cornerRadius: FocusDesign.Radius.control))
            }
            .buttonStyle(PauseButtonStyle())
        }
        .padding(.leading, FocusDesign.Navigation.homeLeadingPadding)
        .padding(.trailing, FocusDesign.Spacing.medium)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private func navigate(to page: IslandNavigationPage) {
        withAnimation(reduceMotion ? nil : FocusDesign.Motion.navigation) { presentation.open(page) }
    }

    private func navigateBack() {
        if let onCloseNavigation {
            onCloseNavigation()
        } else {
            withAnimation(reduceMotion ? nil : FocusDesign.Motion.navigation) { presentation.goBack() }
        }
    }
}

private struct NavigationItem: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    let title: String
    let systemImage: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: FocusDesign.Spacing.xSmall) {
                Image(systemName: systemImage)
                    .font(.system(size: FocusDesign.Typography.navigationIcon, weight: .semibold))
                    .frame(height: 24)
                Text(title).font(.system(size: L10n.language == .english ? FocusDesign.Typography.englishNavigationLabel : FocusDesign.Typography.label, weight: .semibold))
                    .lineLimit(1)
                    .minimumScaleFactor(FocusDesign.Typography.navigationMinimumScale)
                    .frame(height: 15)
            }
            .frame(width: FocusDesign.Size.navigationItemWidth, height: FocusDesign.Size.navigationItemHeight)
        }
        .buttonStyle(NavigationItemStyle())
    }
}

private struct NavigationItemStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(SubpageForegroundStyle(
                fallback: FocusDesign.ColorToken.textPrimary.opacity(configuration.isPressed ? FocusDesign.Opacity.disabledText : FocusDesign.Opacity.navigationText),
                glassOpacity: configuration.isPressed ? FocusDesign.Opacity.disabledText : 1))
            .background(configuration.isPressed ? FocusDesign.ColorToken.pressedSurface : .clear, in: RoundedRectangle(cornerRadius: FocusDesign.Radius.control))
            .contentShape(RoundedRectangle(cornerRadius: FocusDesign.Radius.control))
    }
}

private struct PauseButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .opacity(configuration.isPressed ? FocusDesign.Opacity.disabledText : 1)
            .contentShape(RoundedRectangle(cornerRadius: FocusDesign.Radius.control))
    }
}

struct CompactPageShell<Content: View>: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @ObservedObject var presentation: IslandPresentationModel
    @ViewBuilder let content: Content
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        HStack(spacing: FocusDesign.Spacing.small) {
            Button { withAnimation(reduceMotion ? nil : FocusDesign.Motion.navigation) { presentation.goBack() } } label: {
                Image(systemName: "chevron.left")
                    .font(.system(size: FocusDesign.Typography.icon, weight: .bold))
                    .frame(width: FocusDesign.Size.detailBackHitArea, height: FocusDesign.Size.detailBackHitArea)
            }
            .buttonStyle(RailButtonStyle())
            .foregroundStyle(SubpageForegroundStyle.secondary)
            .help(L10n.text("返回导航"))

            content.frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .padding(.leading, FocusDesign.Navigation.detailLeadingPadding)
    }
}

private struct RailButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(configuration.isPressed ? FocusDesign.ColorToken.pressedSurface : .clear, in: RoundedRectangle(cornerRadius: FocusDesign.Radius.control))
            .contentShape(RoundedRectangle(cornerRadius: FocusDesign.Radius.control))
    }
}
