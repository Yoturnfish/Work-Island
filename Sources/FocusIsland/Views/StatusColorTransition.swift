import AppKit
import SwiftUI

/// A color-space-stable representation shared by SwiftUI's floating window
/// and the AppKit-rendered menu-bar artwork.
struct StatusColorComponents: Equatable {
    var red: Double
    var green: Double
    var blue: Double
    var alpha: Double

    init(red: Double, green: Double, blue: Double, alpha: Double = 1) {
        self.red = red
        self.green = green
        self.blue = blue
        self.alpha = alpha
    }

    init(_ color: Color) {
        self.init(NSColor(color))
    }

    init(_ color: NSColor) {
        var converted: NSColor?
        if let appearance = NSAppearance(named: .darkAqua) {
            appearance.performAsCurrentDrawingAppearance {
                converted = color.usingColorSpace(.sRGB)
            }
        }
        let rgb = converted ?? color.usingColorSpace(.sRGB) ?? .white
        red = Double(rgb.redComponent)
        green = Double(rgb.greenComponent)
        blue = Double(rgb.blueComponent)
        alpha = Double(rgb.alphaComponent)
    }

    var swiftUIColor: Color {
        Color(red: red, green: green, blue: blue, opacity: alpha)
    }

    var appKitColor: NSColor {
        NSColor(srgbRed: CGFloat(red), green: CGFloat(green), blue: CGFloat(blue), alpha: CGFloat(alpha))
    }

    func interpolated(to target: Self, fraction: Double) -> Self {
        let amount = min(max(fraction, 0), 1)
        return Self(
            red: red + (target.red - red) * amount,
            green: green + (target.green - green) * amount,
            blue: blue + (target.blue - blue) * amount,
            alpha: alpha + (target.alpha - alpha) * amount
        )
    }
}

/// The explicit blend used by the menu-bar image renderer. Canvas and NSImage
/// drawing do not inherit implicit SwiftUI color animation reliably.
struct StatusColorBlend: Equatable {
    let from: StatusColorComponents
    let to: StatusColorComponents
    let startedAt: TimeInterval
    let duration: TimeInterval

    func linearFraction(at timestamp: TimeInterval) -> Double {
        guard duration > 0 else { return 1 }
        if timestamp >= startedAt + duration { return 1 }
        return min(max((timestamp - startedAt) / duration, 0), 1)
    }

    func components(at timestamp: TimeInterval) -> StatusColorComponents {
        let linear = linearFraction(at: timestamp)
        // Smoothstep provides a deterministic ease-in/ease-out for AppKit.
        let eased = linear * linear * (3 - 2 * linear)
        return from.interpolated(to: to, fraction: eased)
    }

    func isComplete(at timestamp: TimeInterval) -> Bool {
        linearFraction(at: timestamp) >= 1
    }
}

/// Makes the full floating timer consume one explicitly interpolated accent
/// color. This includes Canvas progress drawing, which otherwise jumps.
struct StatusColorTransitionView<Content: View>: View {
    let components: StatusColorComponents
    private let content: (Color) -> Content
    @State private var settled: StatusColorComponents
    @State private var blend: StatusColorBlend?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(components: StatusColorComponents, @ViewBuilder content: @escaping (Color) -> Content) {
        self.components = components
        self.content = content
        _settled = State(initialValue: components)
    }

    var body: some View {
        TimelineView(.animation(
            minimumInterval: 1 / FocusDesign.Motion.frameRate,
            paused: blend == nil
        )) { timeline in
            let timestamp = timeline.date.timeIntervalSinceReferenceDate
            let current = blend?.components(at: timestamp) ?? settled
            content(current.swiftUIColor)
        }
        .onChange(of: components) { _, target in
            let timestamp = Date().timeIntervalSinceReferenceDate
            let current = blend?.components(at: timestamp) ?? settled
            settled = target
            if reduceMotion || current == target {
                blend = nil
            } else {
                blend = StatusColorBlend(
                    from: current,
                    to: target,
                    startedAt: timestamp,
                    duration: FocusDesign.Motion.statusTransitionDuration
                )
            }
        }
        .onChange(of: reduceMotion) { _, reduced in
            if reduced { blend = nil }
        }
        .task(id: blend) {
            guard let scheduled = blend else { return }
            let deadline = scheduled.startedAt + scheduled.duration
            let remaining = max(0, deadline - Date().timeIntervalSinceReferenceDate)
            try? await Task.sleep(for: .seconds(remaining))
            guard blend == scheduled else { return }
            blend = nil
        }
    }
}
