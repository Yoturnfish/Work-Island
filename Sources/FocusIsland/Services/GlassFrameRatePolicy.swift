import Foundation

/// One rate drives capture, crop requests and GPU submission. No display link
/// or persistent polling: transitions follow native pointer/screen events.
enum GlassFrameRatePolicy {
    static func validated(_ rate: Int) -> Int {
        (1...1000).contains(rate) ? rate : 60
    }
    static func target(displayRate: Int, dragging: Bool, reduceMotion: Bool) -> Int {
        let maximum = validated(displayRate)
        return reduceMotion ? min(maximum, 30) : (dragging ? maximum : min(maximum, 60))
    }
    static func limited(_ rate: Int, reduceMotion: Bool) -> Int {
        let rate = validated(rate)
        return reduceMotion ? min(rate, 30) : rate
    }
}

/// Keep a fixed cadence budget despite interleaved pointer/capture callbacks.
/// Small arrival jitter is tolerated without turning 144 into a 160 fps loop.
struct GlassRenderCadence {
    private var deadline: TimeInterval?
    private var previousInterval: TimeInterval?
    func isDue(at now: TimeInterval, interval: TimeInterval) -> Bool {
        guard now.isFinite, interval.isFinite, interval > 0 else { return false }
        guard previousInterval == interval, let deadline else { return true }
        return now + interval * 0.1 >= deadline
    }
    mutating func submitted(at now: TimeInterval, interval: TimeInterval) {
        guard now.isFinite, interval.isFinite, interval > 0 else { return }
        let prior = previousInterval == interval ? (deadline ?? now) : now
        // Advance the fixed phase, not the latest callback's arrival time.
        // Reanchoring after a slightly late callback makes the next on-time
        // callback too early and can halve 144 Hz under alternating jitter.
        var next = prior + interval
        let tolerance = interval * 0.1
        if next <= now + tolerance {
            // Skip missed slots instead of emitting a catch-up burst.
            next += (floor((now + tolerance - next) / interval) + 1) * interval
        }
        deadline = next
        previousInterval = interval
    }
    mutating func reset() { deadline = nil; previousInterval = nil }
}
