import AppKit
import SwiftUI

/// Label geometry defines both the painted rectangle and its complete hit region.
struct FilledRectangleButtonStyle: ButtonStyle {
    var fill: Color = .white.opacity(0.08)
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(fill.opacity(configuration.isPressed ? 0.65 : 1), in: RoundedRectangle(cornerRadius: 7))
            .contentShape(RoundedRectangle(cornerRadius: 7))
    }
}

struct RecognitionToolbar: View {
    @Environment(\.colorScheme) private var colorScheme
    @ObservedObject var settings: TrackingSettings
    var onApplicationAdded: () -> Void
    var onWebsiteAdded: () -> Void
    var onPickerChanged: (Bool) -> Void = { _ in }
    var showsColorPicker = true
    @State private var autoPicker = false
    @State private var colorPicker = false
    @State private var captureError = false
    @State private var browserPreviews: [String: String] = [:]
    @State private var browserKeywords: [String: String] = [:]
    @State private var editingColor: ThemeAccent?
    @FocusState private var nameFocused: Bool

    private var applications: [NSRunningApplication] {
        var seen = Set<String>()
        return NSWorkspace.shared.runningApplications.filter {
            $0.activationPolicy == .regular && $0.bundleIdentifier != Bundle.main.bundleIdentifier && $0.bundleIdentifier != nil
        }.filter { seen.insert(TrackingSettings.ruleKey($0.bundleIdentifier!)).inserted }
            .sorted { ($0.localizedName ?? "") < ($1.localizedName ?? "") }
    }
    private var browsers: [BrowserAutomationService.Browser] {
        BrowserAutomationService.browsers.filter { browser in
            NSRunningApplication.runningApplications(withBundleIdentifier: browser.id).contains { !$0.isTerminated }
        }
    }

    var body: some View {
        HStack(spacing: 6) {
            Button { autoPicker.toggle() } label: {
                Label(L10n.text("自动识别"), systemImage: "plus.viewfinder")
                    .font(.system(size: 12, weight: .medium))
                    .frame(maxWidth: .infinity).frame(height: 28)
            }
            .buttonStyle(FilledRectangleButtonStyle())
            .popover(isPresented: $autoPicker, arrowEdge: .bottom) {
                PersistentVerticalScrollView {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(L10n.text("当前网页")).foregroundStyle(SubpageForegroundStyle(fallback: .secondary))
                        if browsers.isEmpty { Text(L10n.text("没有运行的支持浏览器")).foregroundStyle(SubpageForegroundStyle(fallback: .secondary)) }
                        ForEach(browsers) { browser in
                            Button { capture(browser) } label: {
                                HStack(spacing: 6) {
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(browserPreviews[browser.id] ?? L10n.text("网址不可用 · 请检查授权"))
                                        .font(.system(size: 10)).foregroundStyle(FocusDesign.ColorToken.textPrimary).lineLimit(1).truncationMode(.tail)
                                }.frame(maxWidth: .infinity, alignment: .leading)
                                Image(systemName: "checkmark.circle.fill").foregroundStyle(FocusDesign.ColorToken.actionText)
                                    .frame(width: 16)
                                    .opacity(browserKeywords[browser.id].map { settings.hasWebsite(keyword: $0) } == true ? 1 : 0)
                                }.padding(7)
                            }.buttonStyle(FilledRectangleButtonStyle())
                                .accessibilityLabel(browser.name + " · " + (browserPreviews[browser.id] ?? L10n.text("网址不可用 · 请检查授权")))
                        }
                        Divider()
                        Text(L10n.text("当前应用程序")).foregroundStyle(SubpageForegroundStyle(fallback: .secondary))
                        ForEach(applications, id: \.processIdentifier) { app in
                            option(app.localizedName ?? app.bundleIdentifier!, added: settings.ruleApplications.contains { TrackingSettings.ruleKey($0.bundleIdentifier) == TrackingSettings.ruleKey(app.bundleIdentifier!) }) {
                                guard let identifier = app.bundleIdentifier else { return }
                                settings.toggleRecognizedApplication(identifier: identifier, name: app.localizedName ?? identifier)
                                onApplicationAdded()
                            }
                        }
                    }.font(.system(size: 12)).padding(10)
                }.frame(width: 275, height: 220).preferredColorScheme(colorScheme)
                    .task {
                        browserPreviews = [:]
                        browserKeywords = [:]
                        for browser in browsers {
                            if let address = BrowserAutomationService.shared.url(bundleIdentifier: browser.id, pointer: nil) {
                                browserPreviews[browser.id] = WebsiteKeyword.preview(of: address)
                                browserKeywords[browser.id] = WebsiteKeyword.extract(from: address)
                            }
                        }
                    }
            }
            if showsColorPicker {
            Button {
                editingColor = settings.selectedColor
                colorPicker.toggle()
            } label: {
                Rectangle().fill(.clear).frame(width: 38, height: 22)
                    .accessibilityLabel(L10n.text("切换颜色组合"))
                    .accessibilityValue(settings.selectedColor.title)
            }
            .buttonStyle(FilledRectangleButtonStyle(fill: settings.selectedColor.color))
            .popover(isPresented: $colorPicker, arrowEdge: .bottom) {
                VStack(alignment: .leading, spacing: 10) {
                    Text(L10n.text("切换颜色可重新配置识别组合，适应不同工作类型。"))
                        .font(.system(size: 11)).foregroundStyle(SubpageForegroundStyle(fallback: .secondary)).fixedSize(horizontal: false, vertical: true)
                        .multilineTextAlignment(.center).frame(maxWidth: .infinity)
                    HStack(alignment: .top, spacing: 5) {
                    ForEach(ThemeAccent.allCases) { color in
                        VStack(spacing: 6) {
                        Button {
                            settings.selectColor(color)
                            editingColor = color
                            nameFocused = true
                        } label: {
                            Image(systemName: "checkmark").opacity(settings.selectedColor == color ? 1 : 0)
                                .font(.system(size: 11, weight: .bold)).foregroundStyle(.black)
                                .frame(width: 26, height: 26)
                        }
                        .buttonStyle(FilledRectangleButtonStyle(fill: color.color))
                        .accessibilityLabel(color.title)
                        Text(color == .green ? L10n.text("默认") : (settings.colorNames[color.rawValue] ?? ""))
                            .font(.system(size: 10)).lineLimit(1).truncationMode(.tail)
                            .help(settings.colorNames[color.rawValue] ?? color.title)
                        }.frame(width: 38)
                    }
                    }
                    if let color = editingColor {
                        let colorName = Binding(
                            get: { settings.colorNames[color.rawValue] ?? "" },
                            set: { settings.renameColor(color, to: $0) })
                        HStack {
                            Text(L10n.text("命名")).font(.system(size: 11))
                            ZStack(alignment: .leading) {
                                if colorName.wrappedValue.isEmpty {
                                    Text(L10n.text("为颜色组合命名"))
                                        .foregroundStyle(.white.opacity(0.62))
                                        .allowsHitTesting(false)
                                }
                                TextField("", text: colorName)
                                    .textFieldStyle(.plain)
                                    .foregroundStyle(.white)
                                    .tint(.white)
                                    .focused($nameFocused)
                                    .onAppear { nameFocused = true }
                                    .onSubmit { nameFocused = false }
                            }
                                .padding(.horizontal, 8).padding(.vertical, 8)
                                .background(.white.opacity(0.08), in: RoundedRectangle(cornerRadius: 7))
                                .contentShape(RoundedRectangle(cornerRadius: 7))
                        }.padding(.vertical, 5)
                        Group {
                            VStack(spacing: 5) {
                                ForEach(IslandProgressStyle.allCases) { style in
                                    Button { settings.progressStyle = style } label: {
                                        HStack(spacing: 10) {
                                            StyledProgressView(progress: 0.62, isActive: true, animationsEnabled: true,
                                                columns: 24, accentColor: color.color, style: style)
                                                .frame(height: 12).allowsHitTesting(false).accessibilityHidden(true)
                                            Image(systemName: "checkmark").opacity(settings.progressStyle == style ? 1 : 0)
                                                .frame(width: 14).foregroundStyle(color.color)
                                        }.padding(.horizontal, 9).frame(height: 30)
                                    }.buttonStyle(FilledRectangleButtonStyle())
                                        .accessibilityLabel(style.title).help(style.title)
                                        .accessibilityAddTraits(settings.progressStyle == style ? .isSelected : [])
                                }
                            }.padding(.vertical, 3)
                        }
                    }
                }.padding(10).frame(width: 316).fixedSize(horizontal: false, vertical: true).preferredColorScheme(colorScheme)
            }
            }
        }.padding(.vertical, 3)
        .onChange(of: autoPicker) { _, _ in onPickerChanged(autoPicker || colorPicker) }
        .onChange(of: colorPicker) { _, opened in
            if !opened { editingColor = nil; nameFocused = false }
            onPickerChanged(autoPicker || colorPicker)
        }
        .onDisappear { onPickerChanged(false) }
        .alert(L10n.text("无法读取当前网页"), isPresented: $captureError) {
            Button(L10n.text("知道了"), role: .cancel) {}
        } message: {
            Text(L10n.text("请在目标浏览器最前方窗口打开普通网页，并在浏览器访问权限中授权。不会读取后台标签页。"))
        }
    }

    private func option(_ title: String, added: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack {
                Text(title).lineLimit(1).truncationMode(.tail).frame(maxWidth: .infinity, alignment: .leading)
                Image(systemName: "checkmark.circle.fill").foregroundStyle(FocusDesign.ColorToken.actionText)
                    .frame(width: 16).opacity(added ? 1 : 0)
            }.padding(7)
        }.buttonStyle(FilledRectangleButtonStyle())
    }

    private func capture(_ browser: BrowserAutomationService.Browser) {
        guard let address = BrowserAutomationService.shared.url(bundleIdentifier: browser.id, pointer: nil),
              settings.toggleCurrentWebsite(address: address) != nil else {
            captureError = true
            return
        }
        browserKeywords[browser.id] = WebsiteKeyword.extract(from: address)
        browserPreviews[browser.id] = WebsiteKeyword.preview(of: address)
        onWebsiteAdded()
    }
}
