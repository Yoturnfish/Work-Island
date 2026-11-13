import SwiftUI
struct DailyGoalPicker: View {
    @Binding var seconds: TimeInterval
    var width: CGFloat = FocusDesign.Size.settingsPickerWidth
    var body: some View {
        GlassSettingsPicker(title: L10n.text("每日目标"),
            labels: TrackingSettings.goalOptions.map { TimeFormatting.goalHours($0) },
            selectedIndex: TrackingSettings.goalOptions.firstIndex(of: seconds) ?? 0,
            onSelect: { index in
                guard TrackingSettings.goalOptions.indices.contains(index) else { return }
                seconds = TrackingSettings.goalOptions[index]
            })
            .frame(width: width, height: FocusDesign.Size.settingsPickerHeight)
    }
}
