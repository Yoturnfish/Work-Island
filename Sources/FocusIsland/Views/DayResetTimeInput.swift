import SwiftUI

enum DayResetTime {
    static func displayParts(for minutes: Int) -> (hour: String, minute: String) {
        let value = min(max(minutes, 0), 1_439)
        if value == 0 { return ("24", "00") }
        return (String(format: "%02d", value / 60), String(format: "%02d", value % 60))
    }

    static func parse(hour: String, minute: String) -> Int? {
        guard let hourValue = Int(hour), let minuteValue = Int(minute),
              (0...59).contains(minuteValue) else { return nil }
        if hourValue == 24 { return minuteValue == 0 ? 0 : nil }
        guard (0...23).contains(hourValue) else { return nil }
        return hourValue * 60 + minuteValue
    }

    static func sanitizedComponent(_ value: String) -> String {
        String(value.filter(\.isWholeNumber).prefix(2))
    }

    /// An entirely cleared control means midnight. Partially entered or
    /// invalid values keep the last committed value instead of changing the
    /// user's reset time unexpectedly.
    static func committedMinutes(hour: String, minute: String, fallback: Int) -> Int {
        if hour.isEmpty && minute.isEmpty { return 0 }
        return parse(hour: hour, minute: minute) ?? fallback
    }
}

/// Two editable number fields share one control while the colon remains fixed
/// at the exact center. Midnight is shown as 24:00 but stored as minute zero.
struct DayResetTimeInput: View {
    private enum Field: Hashable { case hour, minute }

    @Binding var minutes: Int
    @State private var hourText = ""
    @State private var minuteText = ""
    @FocusState private var focusedField: Field?

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: FocusDesign.Size.settingsPickerCornerRadius, style: .continuous)
                .fill(.white.opacity(0.16))

            HStack(spacing: 8) {
                componentField(
                    text: $hourText,
                    placeholder: "24",
                    alignment: .trailing,
                    field: .hour
                )
                componentField(
                    text: $minuteText,
                    placeholder: "00",
                    alignment: .leading,
                    field: .minute
                )
            }
            .padding(.horizontal, FocusDesign.Size.settingsPickerTextInset)

            Text(":")
                .font(.system(size: FocusDesign.Typography.settingsValue, weight: .medium, design: .monospaced))
                .foregroundStyle(SubpageForegroundStyle(fallback: FocusDesign.ColorToken.textSecondary, glassOpacity: 0.74))
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
        .frame(width: FocusDesign.Size.settingsPickerWidth,
               height: FocusDesign.Size.settingsPickerHeight)
        .contentShape(RoundedRectangle(
            cornerRadius: FocusDesign.Size.settingsPickerCornerRadius,
            style: .continuous))
        .onAppear(perform: loadStoredValue)
        .onChange(of: minutes) { _, _ in
            if focusedField == nil { loadStoredValue() }
        }
        .onChange(of: focusedField) { _, field in
            if field == nil { finishEditing() }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(L10n.text("每日刷新"))
        .accessibilityHint("24:00")
    }

    private func componentField(
        text: Binding<String>,
        placeholder: String,
        alignment: TextAlignment,
        field: Field
    ) -> some View {
        TextField("", text: text, prompt: Text(placeholder).foregroundStyle(FocusDesign.ColorToken.textMuted))
            .textFieldStyle(.plain)
            .focusEffectDisabled()
            .focused($focusedField, equals: field)
            .font(.system(size: FocusDesign.Typography.settingsValue, design: .monospaced))
            .foregroundStyle(SubpageForegroundStyle.primary)
            .multilineTextAlignment(alignment)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .accessibilityLabel(field == .hour ? L10n.text("小时") : L10n.text("分钟"))
            .onSubmit {
                if hourText.isEmpty && minuteText.isEmpty {
                    focusedField = nil
                } else if field == .hour {
                    focusedField = .minute
                } else {
                    focusedField = nil
                }
            }
            .onChange(of: text.wrappedValue) { oldValue, newValue in
                let sanitized = DayResetTime.sanitizedComponent(newValue)
                if sanitized != newValue { text.wrappedValue = sanitized; return }
                commitIfComplete()
                if field == .hour, focusedField == .hour,
                   oldValue.count < 2, sanitized.count == 2 {
                    focusedField = .minute
                }
            }
    }

    private func commitIfComplete() {
        guard hourText.count == 2, minuteText.count == 2,
              let value = DayResetTime.parse(hour: hourText, minute: minuteText) else { return }
        setMinutesIfChanged(value)
    }

    private func finishEditing() {
        let committed = DayResetTime.committedMinutes(
            hour: hourText,
            minute: minuteText,
            fallback: minutes
        )
        setMinutesIfChanged(committed)
        loadStoredValue()
    }

    private func loadStoredValue() {
        let parts = DayResetTime.displayParts(for: minutes)
        if hourText != parts.hour { hourText = parts.hour }
        if minuteText != parts.minute { minuteText = parts.minute }
    }

    /// SwiftUI may rebuild or re-synchronize both fields while the containing
    /// settings page scrolls. Assigning an unchanged value to `@Published`
    /// still emits, so guard here before the expensive logical-day migration.
    private func setMinutesIfChanged(_ value: Int) {
        guard minutes != value else { return }
        minutes = value
    }
}
