import Foundation
import QuartzCore
import Darwin

/// Scalar-only presentation diagnostics. GPU completion and actual display
/// are different events; keep both counts without storing any screen pixels.
struct GlassPresentationSnapshot {
    private(set) var submitted = 0
    private(set) var displayed = 0
    private(set) var dropped = 0
    private(set) var cadenceSkipped = 0
    private(set) var coverageMisses = 0
    private(set) var displayLagSeconds = 0.0
    private(set) var drawableWaitSeconds = 0.0
    private(set) var schedulingWaitSeconds = 0.0

    mutating func recordSubmission(drawableWait: Double, schedulingWait: Double) {
        submitted += 1
        if drawableWait.isFinite, drawableWait >= 0 { drawableWaitSeconds += drawableWait }
        if schedulingWait.isFinite, schedulingWait >= 0 { schedulingWaitSeconds += schedulingWait }
    }
    mutating func recordDisplay(at time: Double, submittedAt: Double) {
        guard time.isFinite, time > 0, submittedAt.isFinite, time >= submittedAt else {
            dropped += 1; return
        }
        displayed += 1
        displayLagSeconds += time - submittedAt
    }
    mutating func recordCadenceSkip() { cadenceSkipped += 1 }
    mutating func recordCoverageMiss() { coverageMisses += 1 }

    struct Report {
        let displayedFPS: Double
        let displayLagMilliseconds: Double
        let drawableWaitMilliseconds: Double
        let schedulingWaitMilliseconds: Double
        let dropped: Int
        let cadenceSkipped: Int
        let coverageMisses: Int
    }

    func report(since start: Self, duration: Double) -> Report? {
        guard duration.isFinite, duration > 0 else { return nil }
        let draws = submitted - start.submitted, displays = displayed - start.displayed
        guard draws >= 0, displays >= 0 else { return nil }
        return Report(displayedFPS: Double(displays) / duration,
            displayLagMilliseconds: displays > 0 ? (displayLagSeconds-start.displayLagSeconds) / Double(displays) * 1000 : 0,
            drawableWaitMilliseconds: draws > 0 ? (drawableWaitSeconds-start.drawableWaitSeconds) / Double(draws) * 1000 : 0,
            schedulingWaitMilliseconds: draws > 0 ? (schedulingWaitSeconds-start.schedulingWaitSeconds) / Double(draws) * 1000 : 0,
            dropped: dropped-start.dropped, cadenceSkipped: cadenceSkipped-start.cadenceSkipped,
            coverageMisses: coverageMisses-start.coverageMisses)
    }
}

/// Resource-only counters, no screen contents. Sampled from frame callbacks at
/// ten-second intervals, so no new timer or steady idle polling is introduced.
struct GlassRuntimeMetrics {
    private var start: Double = CACurrentMediaTime()
    private var cpu = Self.cpuSeconds()
    private var firstGPU: (frames: Int, seconds: Double)
    private var received = 0

    struct Report {
        let duration: Double
        let cpuPercent: Double
        let captureFPS: Double
        let renderedFPS: Double
        let gpuMilliseconds: Double
        let residentMiB: Double
    }

    init(gpu: (frames: Int, seconds: Double)) { firstGPU = gpu }

    mutating func recordFrame() { received += 1 }

    mutating func receive(gpu: (frames: Int, seconds: Double)) -> Report? {
        recordFrame()
        let now = CACurrentMediaTime(), elapsed = now - start
        guard elapsed >= 10 else { return nil }
        return finish(gpu: gpu, at: now)
    }

    /// Also used for actual pointer-drag spans; no timer or position polling.
    mutating func finish(gpu: (frames: Int, seconds: Double), at now: Double = CACurrentMediaTime()) -> Report? {
        let elapsed = now - start
        guard elapsed.isFinite, elapsed >= 0.001 else { return nil }
        let currentCPU = Self.cpuSeconds()
        let frames = gpu.frames - firstGPU.frames
        let report = Report(duration: elapsed, cpuPercent: (currentCPU - cpu) / elapsed * 100,
            captureFPS: Double(received) / elapsed, renderedFPS: Double(frames) / elapsed,
            gpuMilliseconds: frames > 0 ? (gpu.seconds - firstGPU.seconds) / Double(frames) * 1000 : 0,
            residentMiB: Self.residentMiB())
        start = now; cpu = currentCPU; firstGPU = gpu; received = 0
        return report
    }

    private static func cpuSeconds() -> Double {
        var usage = rusage()
        getrusage(RUSAGE_SELF, &usage)
        return Double(usage.ru_utime.tv_sec + usage.ru_stime.tv_sec)
            + Double(usage.ru_utime.tv_usec + usage.ru_stime.tv_usec) / 1_000_000
    }

    private static func residentMiB() -> Double {
        var info = mach_task_basic_info()
        var count = mach_msg_type_number_t(MemoryLayout<mach_task_basic_info>.size / MemoryLayout<natural_t>.size)
        let status = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                task_info(mach_task_self_, task_flavor_t(MACH_TASK_BASIC_INFO), $0, &count)
            }
        }
        return status == KERN_SUCCESS ? Double(info.resident_size) / 1_048_576 : 0
    }
}
