import AppKit
import SwiftUI
import Combine

extension Notification.Name {
    static let showWorkIslandOnboarding = Notification.Name("showWorkIslandOnboarding")
    static let finishWorkIslandOnboarding = Notification.Name("finishWorkIslandOnboarding")
}

enum OnboardingStep: Int, CaseIterable { case language, goal, recognition, tutorial, complete }

@MainActor final class OnboardingState: ObservableObject {
    @Published var step: OnboardingStep = .language
    @Published var direction = 1
    func next() { direction = 1; step = OnboardingStep(rawValue: min(step.rawValue + 1, 4))! }
    func back() { direction = -1; step = OnboardingStep(rawValue: max(step.rawValue - 1, 0))! }
}

/// Uses the same logical 344 × 92 canvas as every island page.
struct OnboardingView: View {
    @ObservedObject var settings: TrackingSettings
    @ObservedObject var state: OnboardingState
    let finish: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private func text(_ zh: String, _ en: String) -> String { settings.language == .english ? en : zh }

    var body: some View {
        HStack(spacing: 8) {
            Button {
                state.back()
            } label: {
                Image(systemName: "backward.end.fill")
                    .font(.system(size: 16, weight: .bold)).frame(width: 22, height: 36)
            }
            .buttonStyle(FilledRectangleButtonStyle(fill: .clear))
            .opacity(state.step == .language ? 0 : 1)
            .disabled(state.step == .language)
            .accessibilityLabel(text("上一步", "Back"))

            ZStack {
                content.id(state.step).transition(reduceMotion ? .identity : .asymmetric(
                    insertion: .move(edge: state.direction > 0 ? .trailing : .leading).combined(with: .opacity),
                    removal: .move(edge: state.direction > 0 ? .leading : .trailing).combined(with: .opacity)))
            }.frame(maxWidth: .infinity, maxHeight: .infinity).clipped()
                .animation(reduceMotion ? nil : FocusDesign.Motion.navigation, value: state.step)

            Button(action: advance) {
                Image(systemName: state.step == .complete ? "checkmark" : "forward.end.fill")
                    .font(.system(size: 16, weight: .bold)).frame(width: 22, height: 36)
            }.buttonStyle(FilledRectangleButtonStyle(fill: .clear)).accessibilityLabel(primaryTitle)
        }
        .font(.system(size: 12))
        .padding(.horizontal, 14).padding(.vertical, 18)
        .frame(width: FocusDesign.Window.baseWidth, height: FocusDesign.Window.baseHeight)
        .overlay(alignment: .topLeading) {
            HStack(spacing: 3) {
                ForEach(0..<OnboardingStep.allCases.count, id: \.self) { index in
                    RoundedRectangle(cornerRadius: 1)
                        .fill(FocusDesign.ColorToken.accent.opacity(index <= state.step.rawValue ? 1 : 0.22))
                        .frame(width: 9, height: 3)
                }
            }.padding(.leading, 40).padding(.top, 12)
                .accessibilityLabel(title)
        }
        .preferredColorScheme(.dark)
    }

    private var title: String {
        switch state.step {
        case .language: text("欢迎 · 选择语言", "Welcome · Language")
        case .goal: text("每日工作目标", "Daily work goal")
        case .recognition: text("工作识别", "Track your work")
        case .tutorial: text("拖动与导航", "Drag & navigate")
        case .complete: text("准备好了", "You're ready")
        }
    }
    private var primaryTitle: String {
        switch state.step {
        case .complete: text("完成", "Done")
        default: text("下一步", "Next")
        }
    }
    private func advance() {
        switch state.step {
        case .complete: finish()
        default: state.next()
        }
    }
    @ViewBuilder private var content: some View {
        switch state.step {
        case .language:
            HStack(spacing: 12) {
                LanguageGlobe().stroke(.white, lineWidth: 1.6)
                    .frame(width: 21, height: 21)
                    .accessibilityHidden(true)
                ForEach([AppLanguage.english, .chinese]) { language in
                    Button { settings.language = language } label: {
                        Text(language.title).font(.system(size: 15, weight: .semibold))
                            .frame(maxWidth: .infinity).frame(height: 32)
                    }.buttonStyle(FilledRectangleButtonStyle(fill: .white.opacity(settings.language == language ? 0.22 : 0.08)))
                }
            }.padding(.horizontal, 8)
        case .goal:
            HStack(spacing: 6) {
                Text(text("设定每日工作目标", "Daily work goal"))
                    .font(.system(size: 16, weight: .semibold)).lineLimit(1).minimumScaleFactor(0.75)
                DailyGoalPicker(seconds: $settings.goalSeconds)
                    .foregroundStyle(FocusDesign.ColorToken.accent)
            }
        case .recognition:
            RecognitionToolbar(settings: settings, onApplicationAdded: {}, onWebsiteAdded: {}, showsColorPicker: false)
                .overlay(alignment: .top) {
                Text(text("你也可以稍后手动添加", "You can also add items manually later"))
                    .font(.system(size: 9)).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.8)
                    .offset(y: 33)
                }
        case .tutorial:
            HStack(spacing: 10) {
                tutorialDiagram(isDrag: true)
                Rectangle().fill(.white.opacity(0.15)).frame(width: 1)
                tutorialDiagram(isDrag: false)
            }.accessibilityElement(children: .ignore)
                .accessibilityLabel(text("拖动圆形按钮以外的区域移动窗口；点击左侧圆形按钮进入导航。", "Drag outside the circle to move the window. Click the left circle to open navigation."))
        case .complete:
            HStack(spacing: 10) {
                Image(systemName: "checkmark.circle.fill").font(.system(size: 26))
                    .foregroundStyle(FocusDesign.ColorToken.accent)
                Text(text("开始你的工作", "Ready to work")).font(.system(size: 17, weight: .semibold))
            }
        }
    }
    private func tutorialDiagram(isDrag: Bool) -> some View {
        VStack(spacing: 2) {
            ZStack(alignment: .leading) {
                RoundedRectangle(cornerRadius: 8).fill(.black.opacity(0.6))
                    .overlay(RoundedRectangle(cornerRadius: 8).stroke(.white.opacity(0.16)))
                HStack(spacing: 7) {
                    Circle().fill(FocusDesign.ColorToken.accent).frame(width: 17, height: 17)
                        .overlay { if !isDrag { Circle().stroke(.white, lineWidth: 1).padding(4) } }
                    VStack(alignment: .leading, spacing: 3) {
                        Capsule().fill(.white.opacity(0.5)).frame(width: 32, height: 3)
                        Capsule().fill(FocusDesign.ColorToken.accent.opacity(0.5)).frame(width: 44, height: 4)
                    }
                }.padding(.leading, 8)
                Image(systemName: isDrag ? "arrow.left.and.right" : "cursorarrow")
                    .font(.system(size: 14, weight: .bold)).foregroundStyle(.white)
                    .offset(x: isDrag ? 59 : 19, y: 6)
            }.frame(height: 36)
            Text(isDrag ? text("窗口可拖动", "Drag the window") : text("点圆环进菜单", "Tap ring for menu"))
                .font(.system(size: 10, weight: .medium)).lineLimit(1).minimumScaleFactor(0.8)
        }.frame(maxWidth: .infinity)
    }

}

/// Simple latitude/equator globe matching the language reference icon.
private struct LanguageGlobe: Shape {
    func path(in rect: CGRect) -> Path {
        let bounds = rect.insetBy(dx: 1, dy: 1)
        var path = Path(ellipseIn: bounds)
        path.addEllipse(in: bounds.insetBy(dx: bounds.width * 0.28, dy: 0))
        path.move(to: CGPoint(x: bounds.minX, y: bounds.midY))
        path.addLine(to: CGPoint(x: bounds.maxX, y: bounds.midY))
        return path
    }
}
