@preconcurrency import AppKit
import SwiftUI

struct RecognitionNavigationPage: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @ObservedObject private var settings: TrackingSettings
    @ObservedObject var presentation: IslandPresentationModel
    @State private var addingWebsite = false
    @State private var websiteDraft = ""
    @FocusState private var websiteFocused: Bool

    init(engine: TrackingEngine, presentation: IslandPresentationModel) {
        settings = engine.settings
        self.presentation = presentation
    }

    var body: some View {
        CompactPageShell(presentation: presentation) {
            PersistentVerticalScrollView {
                VStack(spacing: 3) {
                    RecognitionToolbar(settings: settings,
                        onApplicationAdded: { presentation.isRecognitionApplicationsExpanded = true },
                        onWebsiteAdded: { presentation.prepareForWebsiteAddition() },
                        onPickerChanged: { presentation.isShowingRecognitionPicker = $0 })
                    HStack(alignment: .top, spacing: 5) {
                        VStack(alignment: .leading, spacing: 4) {
                            HStack(spacing: 3) {
                                columnHeader(L10n.text("应用程序"), expanded: $presentation.isRecognitionApplicationsExpanded)
                                manualButton(action: chooseApplication)
                                    .accessibilityLabel(L10n.text("手动添加") + " · " + L10n.text("应用程序"))
                            }
                            if presentation.isRecognitionApplicationsExpanded {
                                ForEach($settings.ruleApplications) { $application in
                                    HStack(spacing: 3) {
                                        Toggle("", isOn: $application.isEnabled).labelsHidden().fixedSize()
                                        Text(application.displayName).lineLimit(1).truncationMode(.tail)
                                            .frame(maxWidth: .infinity, alignment: .leading).help(application.displayName)
                                        Button(role: .destructive) { settings.ruleApplications.removeAll { $0.id == application.id } } label: { Image(systemName: "trash").frame(width: 18, height: 24) }
                                            .buttonStyle(FilledRectangleButtonStyle(fill: .clear))
                                    }.padding(3)
                                }
                            }
                        }.frame(maxWidth: .infinity, alignment: .topLeading)
                        VStack(alignment: .leading, spacing: 4) {
                            HStack(spacing: 3) {
                                columnHeader(L10n.text("网站"), expanded: $presentation.isRecognitionWebsitesExpanded)
                                manualButton { websiteDraft = ""; addingWebsite = true }
                                    .accessibilityLabel(L10n.text("手动添加") + " · " + L10n.text("网站"))
                                    .popover(isPresented: $addingWebsite, arrowEdge: .bottom) {
                                        VStack(alignment: .leading, spacing: 10) {
                                            Text(L10n.text("手动添加") + " · " + L10n.text("网站")).font(.system(size: 12, weight: .semibold))
                                            TextField(L10n.text("填写路径中的关键词，即可自动识别"), text: $websiteDraft)
                                                .font(.system(size: 10))
                                                .textFieldStyle(.roundedBorder).focused($websiteFocused)
                                                .onAppear { websiteFocused = true }.onSubmit(saveWebsite)
                                            Button(action: saveWebsite) {
                                                Text(L10n.text("添加")).frame(maxWidth: .infinity).frame(height: 26)
                                            }.buttonStyle(FilledRectangleButtonStyle())
                                                .disabled(websiteDraft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                                        }.padding(12).frame(width: 260).preferredColorScheme(.dark)
                                    }
                            }
                            if presentation.isRecognitionWebsitesExpanded {
                                ForEach($settings.ruleWebsites) { $website in
                                    HStack(spacing: 3) {
                                        Toggle("", isOn: $website.isEnabled).labelsHidden().fixedSize()
                                        RuleKeywordField(placeholder: L10n.text("网站"), text: $website.pathContains)
                                            .lineLimit(1).truncationMode(.tail).frame(minWidth: 0, maxWidth: .infinity)
                                            .onChange(of: website.pathContains) { _, value in
                                                website.matchesHostOnly = false
                                                website.displayName = value
                                            }
                                        Button { settings.ruleWebsites.removeAll { $0.id == website.id } } label: { Image(systemName: "trash").frame(width: 18, height: 24) }
                                            .buttonStyle(FilledRectangleButtonStyle(fill: .clear))
                                    }.padding(3)
                                }
                                Button { presentation.open(.browserPermissions) } label: {
                                    Label(L10n.text("浏览器访问权限"), systemImage: "lock.shield")
                                        .font(.system(size: 10)).lineLimit(1).minimumScaleFactor(0.75)
                                        .frame(maxWidth: .infinity).frame(height: 24)
                                }.buttonStyle(FilledRectangleButtonStyle())
                            }
                        }.frame(maxWidth: .infinity, alignment: .topLeading)
                    }
                }.font(.system(size: 11)).controlSize(.mini)
                    .padding(.horizontal, 3).padding(.vertical, 3)
            }
            .onChange(of: settings.activeThemeID) { _, _ in addingWebsite = false; presentation.resetRecognitionDisclosure() }
            .onChange(of: addingWebsite) { _, value in presentation.isShowingRecognitionPicker = value }
        }
    }

    private func columnHeader(_ title: String, expanded: Binding<Bool>) -> some View {
        Button { expanded.wrappedValue.toggle() } label: {
            HStack(spacing: 3) {
                Text(title).lineLimit(1).minimumScaleFactor(0.75)
                Spacer(minLength: 0)
                Image(systemName: expanded.wrappedValue ? "chevron.up" : "chevron.down").font(.system(size: 8))
            }.font(.system(size: 12, weight: .semibold)).padding(.horizontal, 6).frame(height: 25)
        }.buttonStyle(FilledRectangleButtonStyle())
    }

    private func manualButton(action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: "plus").foregroundStyle(FocusDesign.ColorToken.actionText)
                .font(.system(size: 13, weight: .semibold)).frame(width: 20, height: 25)
        }.buttonStyle(FilledRectangleButtonStyle(fill: .clear))
    }

    private func saveWebsite() {
        guard settings.addManualWebsite(websiteDraft) else { return }
        presentation.isRecognitionWebsitesExpanded = true
        addingWebsite = false
    }

    private func chooseApplication() {
        let panel = NSOpenPanel()
        panel.title = L10n.text("选择需要识别的应用")
        panel.prompt = L10n.text("添加")
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.allowedContentTypes = [.application]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        if panel.runModal() == .OK, let url = panel.url {
            settings.addApplication(url: url)
            presentation.isRecognitionApplicationsExpanded = true
        }
    }
}

struct BrowserPermissionsPage: View {
    @Environment(\.whiteSubpageForeground) private var whiteForeground
    @Environment(\.darkGlassForeground) private var darkForeground
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @ObservedObject var presentation: IslandPresentationModel
    @ObservedObject private var automation = BrowserAutomationService.shared

    var body: some View {
        CompactPageShell(presentation: presentation) {
            PersistentVerticalScrollView {
                LazyVStack(spacing: FocusDesign.Spacing.list) {
                    HStack {
                        Text(L10n.text("读取当前网页路径"))
                        Spacer()
                        Button(L10n.text("授权")) { automation.requestAuthorization() }
                            .buttonStyle(CompactTextButtonStyle())
                            .foregroundStyle(FocusDesign.ColorToken.actionText)
                    }
                    .compactRow()

                    ForEach(BrowserAutomationService.browsers) { browser in
                        let status = automation.statuses[browser.id, default: .pending]
                        HStack(spacing: FocusDesign.Spacing.small) {
                            Circle()
                                .fill(browserStatusColor(status))
                                .frame(width: FocusDesign.Size.browserPermissionDot, height: FocusDesign.Size.browserPermissionDot)
                            Text(browser.name)
                            Spacer()
                            Text(L10n.text(status.rawValue)).foregroundStyle(browserStatusColor(status))
                        }
                        .compactRow()
                    }

                    if automation.statuses.values.contains(.denied) {
                        Button { automation.openAutomationSettings() } label: {
                            HStack {
                                Text(L10n.text("前往系统自动化设置"))
                                Spacer()
                                Image(systemName: "arrow.up.forward.app")
                            }
                        }
                        .buttonStyle(CompactTextButtonStyle())
                        .foregroundStyle(FocusDesign.ColorToken.warning)
                    }
                }
                .padding(.horizontal, FocusDesign.Spacing.small)
                .padding(.vertical, FocusDesign.Spacing.xSmall)
            }
            .onAppear { automation.refreshStatuses() }
        }
    }

    private func browserStatusColor(_ status: BrowserPermissionStatus) -> Color {
        switch status {
        case .authorized: FocusDesign.ColorToken.accent
        case .denied: FocusDesign.ColorToken.warning
        default: darkForeground ? .black : (whiteForeground ? .white : FocusDesign.ColorToken.textMuted)
        }
    }
}

private struct RuleKeywordField: View {
    let placeholder: String
    @Binding var text: String
    @State private var editing = false
    @FocusState private var focused: Bool

    var body: some View {
        Group {
            if editing {
                TextField(placeholder, text: $text)
                    .textFieldStyle(.roundedBorder).focused($focused)
                    .onAppear { focused = true }
                    .onSubmit { editing = false }
                    .onChange(of: focused) { _, value in if !value { editing = false } }
            } else {
                Button { editing = true } label: {
                    Text(text.isEmpty ? placeholder : text)
                        .foregroundStyle(text.isEmpty ? SubpageForegroundStyle.secondary : SubpageForegroundStyle.primary)
                        .lineLimit(1).truncationMode(.tail)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 4).frame(height: 22)
                }.buttonStyle(FilledRectangleButtonStyle())
                    .help(text).accessibilityLabel(placeholder).accessibilityValue(text)
            }
        }.frame(height: 24)
    }
}
