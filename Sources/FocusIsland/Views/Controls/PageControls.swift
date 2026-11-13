@preconcurrency import AppKit
import SwiftUI

struct CompactTextButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(height: FocusDesign.Size.iconHitArea)
            .padding(.horizontal, FocusDesign.Spacing.small)
            .background(configuration.isPressed ? FocusDesign.ColorToken.pressedSurface : .clear, in: RoundedRectangle(cornerRadius: FocusDesign.Radius.small))
            .contentShape(RoundedRectangle(cornerRadius: FocusDesign.Radius.small))
    }
}

private struct GlassSettingsControlsKey: EnvironmentKey {
    static let defaultValue = false
}

extension EnvironmentValues {
    var glassSettingsControls: Bool {
        get { self[GlassSettingsControlsKey.self] }
        set { self[GlassSettingsControlsKey.self] = newValue }
    }
}

enum SettingsControlPalette {
    static func thumb(isGlass: Bool, isOn: Bool, isBrightGlass: Bool = false) -> Color {
        if isBrightGlass { return .black.opacity(0.82) }
        return isGlass ? .white : (isOn ? .black.opacity(0.82) : FocusDesign.ColorToken.textSecondary)
    }
}

struct CompactToggleRow: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    let title: String
    @Binding var isOn: Bool
    var body: some View {
        Toggle(title, isOn: $isOn)
            .toggleStyle(AlwaysActiveSwitchStyle())
            .settingsRow(emphasized: true)
    }
}

private struct AlwaysActiveSwitchStyle: ToggleStyle {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.glassSettingsControls) private var isGlass
    @Environment(\.darkGlassForeground) private var isBrightGlass

    func makeBody(configuration: Configuration) -> some View {
        Button {
            withAnimation(reduceMotion ? nil : FocusDesign.Motion.control) {
                configuration.isOn.toggle()
            }
        } label: {
            HStack(spacing: FocusDesign.Spacing.medium) {
                configuration.label
                ZStack(alignment: configuration.isOn ? .trailing : .leading) {
                    Capsule()
                        .fill(configuration.isOn ? FocusDesign.ColorToken.accent : FocusDesign.ColorToken.pressedSurface)
                    Circle()
                        .fill(SettingsControlPalette.thumb(isGlass: isGlass, isOn: configuration.isOn, isBrightGlass: isBrightGlass))
                        .frame(width: FocusDesign.Size.settingsToggleThumb, height: FocusDesign.Size.settingsToggleThumb)
                        .padding(2)
                }
                .frame(width: FocusDesign.Size.settingsToggleWidth, height: FocusDesign.Size.settingsToggleHeight)
            }
        }
        .buttonStyle(.plain)
        .accessibilityValue(configuration.isOn ? L10n.text("已开启") : L10n.text("已关闭"))
    }
}

struct CompactPickerRow<Value: Hashable, Values: RandomAccessCollection>: View where Values.Element == Value {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    let title: String
    @Binding var selection: Value
    let values: Values
    var onTitleTripleTap: (() -> Void)? = nil
    let label: (Value) -> String
    var body: some View {
        HStack {
            Text(title)
                .font(.system(size: FocusDesign.Typography.settingsLabel))
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
                .onTapGesture(count: SettingsInteraction.onboardingReplayTapCount) {
                    onTitleTripleTap?()
                }
            Spacer()
            GlassSettingsPicker(title: title, labels: Array(values).map(label),
                selectedIndex: Array(values).firstIndex(of: selection) ?? 0) { index in
                    let options = Array(values)
                    guard options.indices.contains(index) else { return }
                    selection = options[index]
                }
                .frame(width: FocusDesign.Size.settingsPickerWidth,
                       height: FocusDesign.Size.settingsPickerHeight)
        }
        .settingsRow()
    }
}

struct ActiveAccentSlider: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @Binding var value: Double
    let range: ClosedRange<Double>
    let label: String
    let valueText: String
    var step: Double? = nil
    var onEditingChanged: (Bool) -> Void = { _ in }
    @State private var isEditing = false

    var body: some View {
        GeometryReader { proxy in
            let width = max(proxy.size.width - FocusDesign.Size.settingsSliderThumb, 1)
            let progress = min(max((value - range.lowerBound) / (range.upperBound - range.lowerBound), 0), 1)
            ZStack(alignment: .leading) {
                Capsule()
                    .fill(FocusDesign.ColorToken.accent.opacity(FocusDesign.Opacity.sliderTrack))
                    .frame(height: FocusDesign.Size.settingsSliderTrackHeight)
                Capsule()
                    .fill(FocusDesign.ColorToken.accent)
                    .frame(width: max(FocusDesign.Size.settingsSliderTrackHeight, width * progress), height: FocusDesign.Size.settingsSliderTrackHeight)
                Circle()
                    .fill(FocusDesign.ColorToken.accent)
                    .frame(width: FocusDesign.Size.settingsSliderThumb, height: FocusDesign.Size.settingsSliderThumb)
                    .shadow(color: FocusDesign.ColorToken.accent.opacity(FocusDesign.Opacity.glow), radius: 3)
                    .offset(x: width * progress)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 0)
                    .onChanged { gesture in
                        if !isEditing {
                            isEditing = true
                            onEditingChanged(true)
                        }
                        let progress = min(max((gesture.location.x - FocusDesign.Size.settingsSliderThumb / 2) / width, 0), 1)
                        value = normalized(range.lowerBound + progress * (range.upperBound - range.lowerBound))
                    }
                    .onEnded { _ in
                        isEditing = false
                        onEditingChanged(false)
                    }
            )
        }
        .frame(height: FocusDesign.Size.settingsSliderHeight)
        .accessibilityElement()
        .accessibilityLabel(label)
        .accessibilityValue(valueText)
        .accessibilityAdjustableAction { direction in
            let step = step ?? (range.upperBound - range.lowerBound) / 20
            switch direction {
            case .increment: value = normalized(value + step)
            case .decrement: value = normalized(value - step)
            @unknown default: break
            }
        }
    }

    private func normalized(_ value: Double) -> Double {
        let stepped = step.map { range.lowerBound + ((value - range.lowerBound) / $0).rounded() * $0 } ?? value
        return min(max(stepped, range.lowerBound), range.upperBound)
    }
}

struct MiniMatrix: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    let progress: Double
    var body: some View {
        HStack(spacing: FocusDesign.Spacing.xxSmall) {
            ForEach(0..<FocusDesign.Matrix.compactColumns, id: \.self) { index in
                RoundedRectangle(cornerRadius: FocusDesign.Radius.pixel)
                    .fill(index < StyledProgressView.filledCells(progress: progress, count: FocusDesign.Matrix.compactColumns) ? FocusDesign.ColorToken.accent : FocusDesign.ColorToken.accent.opacity(FocusDesign.Opacity.inactivePixel))
                    .frame(width: FocusDesign.Size.miniPixel, height: FocusDesign.Size.miniPixel)
            }
        }
    }
}

extension View {
    func compactRow(emphasized: Bool = false, height: CGFloat = FocusDesign.Size.detailRowHeight) -> some View {
        self
            .font(.system(size: FocusDesign.Typography.detailBody))
            .padding(.horizontal, FocusDesign.Spacing.small)
            .frame(height: height)
            .background {
                if emphasized {
                    RoundedRectangle(cornerRadius: FocusDesign.Radius.small).fill(FocusDesign.ColorToken.surface)
                }
            }
    }

    func settingsRow(emphasized: Bool = false) -> some View {
        self
            .font(.system(size: FocusDesign.Typography.settingsLabel))
            .padding(.horizontal, FocusDesign.Spacing.small)
            .frame(height: FocusDesign.Size.settingsRowHeight)
            .background {
                if emphasized {
                    RoundedRectangle(cornerRadius: FocusDesign.Radius.small).fill(FocusDesign.ColorToken.surface)
                }
            }
    }

    func compactScroller(showsIndicators: Bool = true) -> some View {
        self
            .controlSize(FocusDesign.Control.compactScroller)
            .scrollIndicators(showsIndicators ? .automatic : .never)
    }
}
