@preconcurrency import AppKit
import SwiftUI

struct HistoryNavigationPage: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @ObservedObject var engine: TrackingEngine
    @ObservedObject var presentation: IslandPresentationModel

    var body: some View {
        CompactPageShell(presentation: presentation) {
            PersistentVerticalScrollView {
                LazyVStack(spacing: 3) {
                    Button { presentation.openHistoryStats() } label: {
                        HStack { Image(systemName: "chart.bar.fill"); Text(L10n.text("历史统计")); Spacer(); Image(systemName: "chevron.right") }
                            .font(.system(size: FocusDesign.Typography.detailBody, weight: .semibold))
                            .frame(height: FocusDesign.Size.iconHitArea)
                            .padding(.horizontal, FocusDesign.Spacing.small)
                    }
                    .buttonStyle(CompactTextButtonStyle())
                    .frame(maxWidth: .infinity, alignment: .leading)
                    ForEach(engine.recentHistoryDays()) { day in
                        if day.isSummaryOnly {
                            HStack {
                                MiniMatrix(progress: day.totalSeconds / max(engine.settings.goalSeconds, 1))
                                Text(day.dayKey).font(.system(size: FocusDesign.Typography.detailBody, weight: .semibold, design: .monospaced))
                                Spacer()
                                Text(TimeFormatting.compact(day.totalSeconds, style: engine.settings.durationDisplayStyle)).foregroundStyle(FocusDesign.ColorToken.actionText)
                            }
                            .compactRow(height: 24)
                            .frame(maxWidth: .infinity)
                        } else {
                            Button { presentation.openHistoryDetail(day) } label: {
                                HStack {
                                    MiniMatrix(progress: day.totalSeconds / max(engine.settings.goalSeconds, 1))
                                    Text(day.dayKey).font(.system(size: FocusDesign.Typography.detailBody, weight: .semibold, design: .monospaced))
                                    Spacer()
                                    Text(TimeFormatting.compact(day.totalSeconds, style: engine.settings.durationDisplayStyle)).foregroundStyle(FocusDesign.ColorToken.actionText)
                                }
                                .compactRow(height: 24)
                                .frame(maxWidth: .infinity)
                            }
                            .buttonStyle(FilledRectangleButtonStyle())
                            .frame(maxWidth: .infinity)
                        }
                    }
                }.padding(.horizontal, FocusDesign.Spacing.small).padding(.vertical, FocusDesign.Spacing.xSmall)
            }
        }
    }
}

struct HistoryStatisticsPage: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    @ObservedObject var engine: TrackingEngine
    @ObservedObject var presentation: IslandPresentationModel
    var body: some View {
        CompactPageShell(presentation: presentation) {
            HistoryTimelineChart(
                days: engine.completeHistoryDays(),
                durationStyle: engine.settings.durationDisplayStyle,
                goalSeconds: engine.settings.goalSeconds
            )
        }
    }
}

private struct HistoryTimelineChart: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    let days: [HistoryDay]
    let durationStyle: DurationDisplayStyle
    let goalSeconds: TimeInterval
    @State private var hoveredDayKey: String?
    @State private var positionedAtLatest = false

    var body: some View {
        VStack(spacing: FocusDesign.Spacing.xxSmall) {
            HStack(spacing: FocusDesign.Spacing.small) {
                Text(hoveredDay?.dayKey ?? L10n.text("历史统计"))
                Spacer()
                if let hoveredDay {
                    Text(TimeFormatting.compact(hoveredDay.totalSeconds, style: durationStyle))
                        .foregroundStyle(FocusDesign.ColorToken.actionText)
                }
            }
            .font(.system(size: FocusDesign.Typography.detailCaption, weight: .semibold, design: .monospaced))
            .frame(height: FocusDesign.Size.iconHitArea)
            .padding(.horizontal, FocusDesign.Spacing.small)

            GeometryReader { geometry in
                ScrollViewReader { proxy in
                    ScrollView(.horizontal) {
                        LazyHStack(alignment: .bottom, spacing: FocusDesign.Matrix.statisticsColumnSpacing) {
                            ForEach(days) { day in
                                VStack(spacing: FocusDesign.Spacing.xxSmall) {
                                    StatisticsPixelMatrix(
                                        progress: StatisticsPixelMatrix.goalProgress(
                                            seconds: day.totalSeconds,
                                            goalSeconds: goalSeconds
                                        )
                                    )
                                    Text(String(day.dayKey.suffix(2)))
                                        .font(.system(size: FocusDesign.Typography.statisticsDate, weight: .medium))
                                }
                                .offset(y: FocusDesign.Matrix.statisticsContentVerticalOffset)
                                .id(day.id)
                                .onHover { inside in
                                    if inside {
                                        hoveredDayKey = day.dayKey
                                    } else if hoveredDayKey == day.dayKey {
                                        hoveredDayKey = nil
                                    }
                                }
                                .accessibilityElement(children: .ignore)
                                .accessibilityLabel(day.dayKey)
                                .accessibilityValue(TimeFormatting.readable(day.totalSeconds, style: durationStyle))
                            }
                            Color.clear
                                .frame(width: max((geometry.size.width - FocusDesign.Matrix.statisticsMatrixWidth) / 2, 0))
                        }
                        .padding(.leading, FocusDesign.Spacing.small)
                        .background(HorizontalWheelCapture())
                    }
                    .scrollIndicators(.never)
                    .onAppear {
                        guard !positionedAtLatest, let latest = days.last else { return }
                        positionedAtLatest = true
                        Task { @MainActor in
                            await Task.yield()
                            proxy.scrollTo(latest.id, anchor: .center)
                        }
                    }
                }
            }
        }
    }

    private var hoveredDay: HistoryDay? {
        guard let hoveredDayKey else { return nil }
        return days.first { $0.dayKey == hoveredDayKey }
    }
}

struct StatisticsPixelMatrix: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    let progress: Double

    static func litCount(progress: Double) -> Int {
        let total = FocusDesign.Matrix.statisticsRows * FocusDesign.Matrix.statisticsColumns
        return StyledProgressView.filledCells(progress: progress, count: total)
    }

    static func goalProgress(seconds: TimeInterval, goalSeconds: TimeInterval) -> Double {
        guard goalSeconds > 0 else { return 0 }
        return min(max(seconds / goalSeconds, 0), 1)
    }

    static func isLit(row: Int, column: Int, litCount: Int) -> Bool {
        row * FocusDesign.Matrix.statisticsColumns + column < litCount
    }

    var body: some View {
        let lit = Self.litCount(progress: progress)
        VStack(spacing: FocusDesign.Matrix.statisticsGap) {
            ForEach((0..<FocusDesign.Matrix.statisticsRows).reversed(), id: \.self) { row in
                HStack(spacing: FocusDesign.Matrix.statisticsGap) {
                    ForEach(0..<FocusDesign.Matrix.statisticsColumns, id: \.self) { column in
                        let illuminated = Self.isLit(row: row, column: column, litCount: lit)
                        RoundedRectangle(cornerRadius: FocusDesign.Radius.pixel)
                            .fill(FocusDesign.ColorToken.accent.opacity(illuminated ? FocusDesign.Opacity.activePixel : FocusDesign.Opacity.inactivePixel))
                            .frame(width: FocusDesign.Matrix.statisticsPixelWidth, height: FocusDesign.Matrix.statisticsPixelHeight)
                            .shadow(color: illuminated ? FocusDesign.ColorToken.accent.opacity(FocusDesign.Opacity.glow) : .clear, radius: 2)
                    }
                }
            }
        }
    }
}

struct HorizontalWheelCapture: NSViewRepresentable {
    @MainActor
    final class Coordinator {
        weak var view: NSView?
        var monitor: Any?

        func install(for view: NSView) {
            self.view = view
            monitor = NSEvent.addLocalMonitorForEvents(matching: .scrollWheel) { [weak self] event in
                let windowNumber = event.windowNumber
                let location = event.locationInWindow
                let verticalDelta = event.scrollingDeltaY
                let horizontalDelta = event.scrollingDeltaX
                let precise = event.hasPreciseScrollingDeltas
                Task { @MainActor [weak self] in
                    self?.handle(
                        windowNumber: windowNumber,
                        location: location,
                        verticalDelta: verticalDelta,
                        horizontalDelta: horizontalDelta,
                        precise: precise
                    )
                }
                return event
            }
        }

        private func handle(
            windowNumber: Int,
            location: NSPoint,
            verticalDelta: CGFloat,
            horizontalDelta: CGFloat,
            precise: Bool
        ) {
            guard let view, view.window?.windowNumber == windowNumber else { return }
            let localLocation = view.convert(location, from: nil)
            guard view.bounds.contains(localLocation), abs(verticalDelta) > abs(horizontalDelta),
                  let scrollView = view.enclosingScrollView,
                  let documentView = scrollView.documentView
            else { return }

            let maximumX = max(0, documentView.frame.width - scrollView.contentView.bounds.width)
            let currentX = scrollView.contentView.bounds.origin.x
            let targetX = HorizontalWheelCapture.targetOffset(
                current: currentX,
                verticalDelta: verticalDelta,
                maximum: maximumX,
                precise: precise
            )
            guard abs(targetX - currentX) > 0.01 else { return }
            scrollView.contentView.scroll(to: NSPoint(x: targetX, y: scrollView.contentView.bounds.origin.y))
            scrollView.reflectScrolledClipView(scrollView.contentView)
        }

        func removeMonitor() {
            if let monitor { NSEvent.removeMonitor(monitor) }
            monitor = nil
        }

    }

    static func targetOffset(current: CGFloat, verticalDelta: CGFloat, maximum: CGFloat, precise: Bool) -> CGFloat {
        let multiplier: CGFloat = precise ? 1 : 18
        return min(max(current - verticalDelta * multiplier, 0), max(0, maximum))
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        context.coordinator.install(for: view)
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {}

    static func dismantleNSView(_ nsView: NSView, coordinator: Coordinator) {
        coordinator.removeMonitor()
    }
}

struct HistoryDetailPage: View {
    @AppStorage(AppLanguage.defaultsKey) private var interfaceLanguage = AppLanguage.preferredSystemLanguage().rawValue
    let day: HistoryDay
    @ObservedObject var engine: TrackingEngine
    @ObservedObject var presentation: IslandPresentationModel
    @State private var hours: [String: String] = [:]
    @State private var minutes: [String: String] = [:]
    var body: some View {
        CompactPageShell(presentation: presentation) {
            if day.isSummaryOnly {
                HStack {
                    Text(L10n.text("该日期仅保留总时长"))
                    Spacer()
                    Text(TimeFormatting.compact(day.totalSeconds, style: engine.settings.durationDisplayStyle))
                        .foregroundStyle(FocusDesign.ColorToken.actionText)
                }
                .font(.system(size: FocusDesign.Typography.detailBody))
                .compactRow()
                .padding(.horizontal, FocusDesign.Spacing.small)
                .padding(.vertical, FocusDesign.Spacing.xSmall)
            } else {
                PersistentVerticalScrollView {
                    LazyVStack(alignment: .leading, spacing: FocusDesign.Spacing.list) {
                        HStack(alignment: .firstTextBaseline) {
                            Text(day.dayKey)
                                .font(.system(size: FocusDesign.Typography.detailBody, weight: .bold, design: .monospaced))
                            Spacer(minLength: FocusDesign.Spacing.xSmall)
                            Text(L10n.text("修改后回车保存"))
                                .font(.system(size: FocusDesign.Typography.detailCaption))
                                .foregroundStyle(SubpageForegroundStyle.muted)
                        }
                        ForEach(day.applications) { usage in
                            HStack(spacing: FocusDesign.Spacing.xSmall) {
                                Text(usage.applicationName).lineLimit(1)
                                Spacer(minLength: FocusDesign.Spacing.xSmall)
                                TextField(L10n.text("时"), text: hourBinding(usage)).frame(width: FocusDesign.Size.historyDurationFieldWidth)
                                Text(L10n.text("时"))
                                TextField(L10n.text("分"), text: minuteBinding(usage)).frame(width: FocusDesign.Size.historyDurationFieldWidth)
                                Text(L10n.text("分"))
                            }
                            .font(.system(size: FocusDesign.Typography.detailBody))
                            .frame(height: FocusDesign.Size.detailRowHeight)
                            .onSubmit { save(usage) }
                        }
                    }
                    .padding(.horizontal, FocusDesign.Spacing.small)
                    .padding(.vertical, FocusDesign.Spacing.xSmall)
                }
                }
        }
        .onAppear {
            if !day.isSummaryOnly {
                for usage in day.applications {
                    hours[usage.id] = String(Int(usage.seconds) / 3600)
                    minutes[usage.id] = String((Int(usage.seconds) % 3600) / 60)
                }
            }
        }
    }
    private func hourBinding(_ usage: AppUsage) -> Binding<String> { Binding(get: { hours[usage.id] ?? "0" }, set: { hours[usage.id] = $0 }) }
    private func minuteBinding(_ usage: AppUsage) -> Binding<String> { Binding(get: { minutes[usage.id] ?? "0" }, set: { minutes[usage.id] = $0 }) }
    private func save(_ usage: AppUsage) {
        do {
            let seconds = try HistoryDurationInput.seconds(
                hours: hours[usage.id] ?? "", minutes: minutes[usage.id] ?? ""
            )
            try engine.setDayTotal(
                dayKey: day.dayKey,
                bundleIdentifier: usage.bundleIdentifier,
                applicationName: usage.applicationName,
                seconds: seconds
            )
        } catch is HistoryDurationInput.InputError {
            // No records have been touched. Restore the last displayed value and
            // use native feedback without adding any permanent UI decoration.
            hours[usage.id] = String(Int(usage.seconds) / 3600)
            minutes[usage.id] = String((Int(usage.seconds) % 3600) / 60)
            NSSound.beep()
        } catch {
            engine.reportPersistenceError(error)
        }
    }
}
