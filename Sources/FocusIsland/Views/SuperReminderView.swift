import SwiftUI

enum SuperReminderTiming {
    static func frequency(elapsed: TimeInterval, threshold: TimeInterval) -> Double {
        guard elapsed.isFinite, threshold.isFinite, threshold > 0, elapsed >= threshold else { return 0 }
        return min(1.5, pow(2, min(1023, floor(elapsed / threshold))) / threshold)
    }
    static func intensity(elapsed: TimeInterval, threshold: TimeInterval) -> Double {
        let frequency = frequency(elapsed: elapsed, threshold: threshold)
        guard frequency > 0 else { return 0 }
        let round = floor(elapsed / threshold)
        // Integrate each completed round; preserve phase when the interval halves.
        var completed = 1.0
        var cycles = 0.0
        while completed < round {
            let rate = Self.frequency(elapsed: completed * threshold, threshold: threshold)
            if rate >= 1.5 { break }
            cycles += threshold * rate
            completed += 1
        }
        cycles += max(0, round - completed) * threshold * 1.5
        cycles += elapsed.truncatingRemainder(dividingBy: threshold) * frequency
        let timeInPulse = cycles.truncatingRemainder(dividingBy: 1) / frequency
        let pulseDuration = min(1.5, 0.9 / frequency)
        guard timeInPulse < pulseDuration else { return 0 }
        return (1 - cos(timeInPulse / pulseDuration * .pi * 2)) / 2
    }
}

struct SuperReminderView: View {
    let startedAt: Date
    let threshold: TimeInterval
    var preview = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        if !reduceMotion {
            TimelineView(.periodic(from: .now, by: 1)) { tick in
                let ready = preview || tick.date.timeIntervalSince(startedAt) >= threshold
                TimelineView(.animation(minimumInterval: 1 / 30.0, paused: !ready)) { frame in
                    Canvas { context, size in
                        let elapsed = frame.date.timeIntervalSince(startedAt)
                        let strength = preview
                            ? (elapsed >= 0 && elapsed < 1.5 ? (1 - cos(elapsed / 1.5 * .pi * 2)) / 2 : 0)
                            : SuperReminderTiming.intensity(elapsed: elapsed, threshold: threshold)
                        guard strength > 0 else { return }
                        context.scaleBy(x: size.width / 2, y: size.height / 2)
                        context.fill(Path(CGRect(x: 0, y: 0, width: 2, height: 2)), with: .radialGradient(
                            Gradient(stops: [.init(color: .white.opacity(0.75 * strength), location: 0),
                                             .init(color: .white.opacity(0.16 * strength), location: 0.55),
                                             .init(color: .clear, location: 1)]),
                            center: CGPoint(x: 1, y: 1), startRadius: 0, endRadius: 0.3 + strength * 1.3))
                    }
                }
            }.allowsHitTesting(false).accessibilityHidden(true)
        }
    }
}
