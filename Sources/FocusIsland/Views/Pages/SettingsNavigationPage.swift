@preconcurrency import AppKit
import SwiftUI

struct SettingsNavigationPage: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @ObservedObject private var engine: TrackingEngine
    @ObservedObject private var settings: TrackingSettings
    @ObservedObject var presentation: IslandPresentationModel
    @ObservedObject private var glassPermission = GlassCapturePermission.shared

    init(engine: TrackingEngine, presentation: IslandPresentationModel) {
        self.engine = engine
        settings = engine.settings
        self.presentation = presentation
    }

    var body: some View {
        CompactPageShell(presentation: presentation) {
            PersistentVerticalScrollView {
                // The settings page is short and contains several AppKit-backed
                // controls. Keep those controls alive while scrolling instead
                // of repeatedly dismantling/recreating them at viewport edges.
                VStack(spacing: FocusDesign.Spacing.list) {
                    CompactToggleRow(title: L10n.text("登录后启动"), isOn: Binding(
                        get: { settings.launchAtLoginRegistered },
                        set: { enabled in
                            settings.launchAtLogin = enabled
                        }))
                        .onAppear { LaunchAtLoginController.refresh(settings: settings) }
                        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
                            LaunchAtLoginController.refresh(settings: settings)
                        }
                    if let error = settings.launchAtLoginError {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(error).font(.system(size: 10)).foregroundStyle(.orange)
                            Button(action: LaunchAtLoginController.openSettings) {
                                Text(L10n.text("系统登录项")).frame(maxWidth: .infinity).frame(height: 24)
                            }.buttonStyle(FilledRectangleButtonStyle())
                        }.padding(.horizontal, 6)
                    }
                    CompactPickerRow(title: L10n.text("显示模式"), selection: $settings.displayMode, values: IslandDisplayMode.allCases) { $0.title }
                    CompactPickerRow(title: L10n.text("背景样式"), selection: Binding(
                        get: { settings.backgroundStyle },
                        set: { style in
                            let switching = settings.backgroundStyle != style
                            settings.backgroundStyle = style
                            if switching && style.usesCapturedGlass { glassPermission.requestForUserSelection() }
                        }), values: IslandBackgroundStyle.allCases) { $0.title }
                        .help(L10n.text("液态玻璃仅在本机采集窗口背后的小块画面，不保存、不上传；选择透明可恢复原材质。"))
                    if settings.backgroundStyle.usesCapturedGlass {
                        blurRow
                        if !glassPermission.granted || glassPermission.captureFailed {
                            Button { glassPermission.requestOrOpenSettings() } label: {
                                Text(L10n.text(glassPermission.granted ? "玻璃暂不可用 · 检查录制权限" : "授权屏幕录制 · 启用玻璃"))
                                    .frame(maxWidth: .infinity).frame(height: 24)
                            }.buttonStyle(FilledRectangleButtonStyle())
                        }
                    }
                    if settings.displayMode == .floating && settings.backgroundStyle == .standard {
                        opacityRow
                    }
                    CompactPickerRow(title: L10n.text("时间格式"), selection: $settings.durationDisplayStyle, values: DurationDisplayStyle.allCases) { $0.title }
                    CompactPickerRow(title: L10n.text("每日目标"), selection: $settings.goalSeconds, values: TrackingSettings.goalOptions) { TimeFormatting.goalHours($0) }
                    CompactPickerRow(title: L10n.text("进入游离状态"), selection: $settings.awayThreshold, values: [30.0, 45, 60, 120, 180, 300, 600]) { formatAwayThreshold($0) }
                    CompactPickerRow(title: L10n.text("游离计时"), selection: $settings.awayTimerEnabled, values: [false, true]) { L10n.text($0 ? "开" : "关") }
                        .help(L10n.text("进入游离后显示本次时长，恢复工作即归零；不累计、不保存。"))
                    CompactPickerRow(title: L10n.text("超级提醒"), selection: $settings.superReminderEnabled, values: [false, true]) { L10n.text($0 ? "闪光" : "关") }
                        .help(L10n.text("游离一轮后中心渐变闪烁，逐轮加快，最快每两秒三次。"))
                    HStack {
                        Text(L10n.text("每日刷新"))
                        Spacer()
                        DayResetTimeInput(minutes: $settings.dayResetMinutes)
                    }
                        .settingsRow()
                    CompactPickerRow(title: L10n.text("语言"), selection: $settings.language,
                                     values: AppLanguage.allCases,
                                     onTitleTripleTap: {
                                         NotificationCenter.default.post(name: .showWorkIslandOnboarding, object: nil)
                                     }) { $0.title }
                    Button { engine.stop(); NSApp.terminate(nil) } label: {
                        HStack {
                            Image(systemName: "power")
                            Text(L10n.text("退出 Work Island"))
                            Spacer()
                            Text(currentVersion)
                                .font(.system(size: FocusDesign.Typography.settingsValue, weight: .medium, design: .monospaced))
                                .foregroundStyle(SubpageForegroundStyle.muted)
                        }
                            .font(.system(size: FocusDesign.Typography.settingsLabel, weight: .semibold))
                            .frame(height: FocusDesign.Size.exitRowHeight)
                            .padding(.horizontal, FocusDesign.Spacing.small)
                    }
                    .buttonStyle(CompactTextButtonStyle())
                    .foregroundStyle(.red)
                }
                .padding(.leading, 2)
                .padding(.trailing, 3)
                .padding(.vertical, FocusDesign.Spacing.xSmall)
            }
        }
        .environment(\.glassSettingsControls, settings.backgroundStyle.isGlass)
    }

    private func formatAwayThreshold(_ value: TimeInterval) -> String {
        value < 60 ? "\(Int(value)) \(L10n.text("秒"))" : "\(Int(value / 60)) \(L10n.text("分钟"))"
    }
    private var currentVersion: String {
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        return "v\(version ?? L10n.text("开发版"))"
    }
    private var opacityRow: some View {
        HStack {
            Text(L10n.text("透明度"))
            Spacer()
            HStack(spacing: FocusDesign.Spacing.small) {
                ActiveAccentSlider(value: $settings.islandOpacity,
                    range: FocusDesign.Opacity.settingMinimum...FocusDesign.Window.maximumScale,
                    label: L10n.text("透明度"),
                    valueText: "\(Int(settings.islandOpacity * 100))%",
                    onEditingChanged: { settings.isPreviewingOpacity = $0 })
                    .frame(width: FocusDesign.Size.settingsSliderWidth)
                Text("\(Int(settings.islandOpacity * 100))%")
                    .foregroundStyle(SubpageForegroundStyle.muted)
                    .frame(width: FocusDesign.Size.settingsPercentageWidth, alignment: .trailing)
            }.frame(width: FocusDesign.Size.settingsControlWidth, alignment: .trailing)
        }.settingsRow()
    }

    private var blurRow: some View {
        HStack {
            Text(L10n.text("背景高斯模糊"))
            Spacer()
            HStack(spacing: FocusDesign.Spacing.small) {
                ActiveAccentSlider(value: $settings.glassBlurRadius, range: 0...GlassBlurMetrics.maximumRadius,
                    label: L10n.text("背景高斯模糊"), valueText: blurPercentage, step: 0.15)
                    .frame(width: FocusDesign.Size.settingsSliderWidth)
                Text(blurPercentage).foregroundStyle(SubpageForegroundStyle.muted)
                    .frame(width: FocusDesign.Size.settingsPercentageWidth, alignment: .trailing)
            }.frame(width: FocusDesign.Size.settingsControlWidth, alignment: .trailing)
        }.settingsRow()
            .help(L10n.text("仅模糊液态玻璃背景，文字和进度条保持清晰；0 为关闭。"))
    }
    private var blurPercentage: String { "\(Int((settings.glassBlurRadius / GlassBlurMetrics.maximumRadius * 100).rounded()))%" }
}
