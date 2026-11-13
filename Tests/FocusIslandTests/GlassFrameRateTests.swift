import AppKit
import CoreMedia
import QuartzCore
import Testing
@testable import FocusIsland

@MainActor struct GlassFrameRateTests {
    @Test func presentationReportsDisplayedFramesAndWaitsSeparatelyFromGPUCompletion() throws {
        var snapshot = GlassPresentationSnapshot()
        snapshot.recordSubmission(drawableWait: 1, schedulingWait: 1)
        snapshot.recordDisplay(at: 1, submittedAt: 0)
        let start = snapshot
        snapshot.recordSubmission(drawableWait: 0.002, schedulingWait: 0.004)
        snapshot.recordSubmission(drawableWait: 0.004, schedulingWait: 0.002)
        snapshot.recordDisplay(at: 10.010, submittedAt: 10)
        snapshot.recordDisplay(at: 10.030, submittedAt: 10)
        snapshot.recordDisplay(at: 0, submittedAt: 10)
        snapshot.recordCadenceSkip(); snapshot.recordCoverageMiss()
        let report = try #require(snapshot.report(since: start, duration: 2))
        #expect(report.displayedFPS == 1)
        #expect(abs(report.displayLagMilliseconds - 20) < 0.000001)
        #expect(abs(report.drawableWaitMilliseconds - 3) < 0.000001)
        #expect(abs(report.schedulingWaitMilliseconds - 3) < 0.000001)
        #expect(report.dropped == 1 && report.cadenceSkipped == 1 && report.coverageMisses == 1)
        #expect(snapshot.report(since: start, duration: .nan) == nil)
        #expect(snapshot.report(since: start, duration: 0) == nil)
        let empty = try #require(start.report(since: start, duration: 1))
        #expect(empty.displayedFPS == 0 && empty.displayLagMilliseconds == 0)
    }

    @Test func alternatingLateCallbacksDoNotHalveDisplayCadence() {
        for rate in [60.0, 120, 144, 165, 240] {
            for delayFraction in [0.0, 0.12, 0.22, 0.4] {
                var cadence = GlassRenderCadence()
                var submitted = 0
                let interval = 1 / rate
                for tick in 0..<Int(rate * 10) {
                    let now = Double(tick) * interval + (tick.isMultiple(of: 2) ? 0 : delayFraction * interval)
                    if cadence.isDue(at: now, interval: interval) {
                        cadence.submitted(at: now, interval: interval)
                        submitted += 1
                    }
                }
                #expect(submitted == Int(rate * 10))
            }
        }
    }

    @Test func delayedRunLoopSkipsMissedSlotsWithoutBurstOrInvalidState() {
        var cadence = GlassRenderCadence()
        let interval = 1.0 / 144
        cadence.submitted(at: 0, interval: interval)
        #expect(cadence.isDue(at: 0.040, interval: interval))
        cadence.submitted(at: 0.040, interval: interval)
        #expect(!cadence.isDue(at: 0.040, interval: interval))
        #expect(cadence.isDue(at: 6 * interval, interval: interval))
        cadence.submitted(at: .nan, interval: interval)
        cadence.submitted(at: 0.040, interval: 0)
        #expect(cadence.isDue(at: 6 * interval, interval: interval))
        cadence.submitted(at: 6 * interval, interval: interval)
        #expect(!cadence.isDue(at: 6 * interval, interval: interval))
    }

    @Test func interleavedCallbacksRespectAverageTargetWithoutAccumulatingLatency() {
        for rate in [60.0,144] {
            var cadence = GlassRenderCadence()
            var submitted = 0
            for tick in 0..<20000 {
                let now = Double(tick)/2000
                if cadence.isDue(at: now, interval: 1/rate) {
                    cadence.submitted(at: now, interval: 1/rate)
                    submitted += 1
                }
            }
            #expect(abs(Double(submitted)-rate*10) <= 2)
            #expect(cadence.isDue(at: 100,interval: 1/rate))
            cadence.submitted(at: 100,interval: 1/rate)
            #expect(!cadence.isDue(at: 100.001,interval: 1/rate))
            cadence.reset()
            #expect(cadence.isDue(at: 100.001,interval: 1/rate))
        }
    }

    @Test func dragFollowsEachDisplayWhileIdleAndAccessibilityStayBounded() {
        for rate in [24, 60, 120, 144, 165, 240, 360] {
            #expect(GlassFrameRatePolicy.target(displayRate: rate, dragging: true, reduceMotion: false) == rate)
            #expect(GlassFrameRatePolicy.target(displayRate: rate, dragging: false, reduceMotion: false) == min(rate,60))
            #expect(GlassFrameRatePolicy.target(displayRate: rate, dragging: true, reduceMotion: true) == min(rate,30))
        }
        for invalid in [0,-1,Int.max] {
            #expect(GlassFrameRatePolicy.target(displayRate: invalid, dragging: true, reduceMotion: false) == 60)
        }
    }

    @Test func configurationAndCropCadenceShareTheExact144HzTarget() throws {
        let region = try #require(GlassCaptureRegion(window: CGRect(x: 100,y: 300,width: 344,height: 92),
            display: CGRect(x: 0,y: 0,width: 1920,height: 1080), scale: 2))
        let config = region.configuration(reduceMotion: false, framesPerSecond: 144)
        #expect(abs(CMTimeGetSeconds(config.minimumFrameInterval)-1.0/144) < 0.0000001)
        #expect(config.queueDepth == 5 && !config.capturesAudio && !config.captureMicrophone)
        #expect(config.sourceRect == region.source)
        #expect(abs(CMTimeGetSeconds(region.configuration(reduceMotion: true, framesPerSecond: 144).minimumFrameInterval)-1.0/30) < 0.0000001)
        var cadence = GlassCropUpdateCadence()
        cadence.requested(at: 100)
        #expect(abs(cadence.delay(at: 100.003, reduceMotion: false, framesPerSecond: 144) - (1.0/144-0.003)) < 0.0000001)
        #expect(cadence.delay(at: 100.010, reduceMotion: false, framesPerSecond: 144) == 0)
        #expect(cadence.delay(at: 100.010, reduceMotion: false, framesPerSecond: 60) > 0)
    }

    @Test func dragOnlyMetricsFlushWithoutWaitingForPeriodicLogAndRejectInvalidTime() {
        var metrics = GlassRuntimeMetrics(gpu: (frames: 10, seconds: 0.1))
        metrics.recordFrame(); metrics.recordFrame(); metrics.recordFrame()
        let report = metrics.finish(gpu: (frames: 130, seconds: 0.22), at: CACurrentMediaTime()+1)
        #expect(report != nil)
        #expect(abs((report?.captureFPS ?? 0)-3) < 0.01)
        #expect(abs((report?.renderedFPS ?? 0)-120) < 0.05)
        #expect(abs((report?.gpuMilliseconds ?? 0)-1) < 0.000001)
        let invalid = metrics.finish(gpu: (frames: 130,seconds:0.22),at:.infinity)
        #expect(invalid == nil)
    }

    @Test func unattachedAndFallbackDragStateDoesNotStartCaptureOrAlterMaterial() {
        let view = LiquidGlassContentView(foreground: NSView(frame: CGRect(x: 0,y: 0,width: 344,height: 92)))
        view.setBackgroundStyle(.glass)
        let effect = view.effect
        view.setDragging(true)
        view.setDragging(true)
        #expect(view.isDragging && view.effect === effect)
        view.setDragging(false)
        #expect(!view.isDragging && view.effect === effect)
        #expect(view.subviews.allSatisfy { !($0 is CapturedGlassView) })
    }
}
