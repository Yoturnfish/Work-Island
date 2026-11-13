import AppKit
import CoreMedia
import CoreVideo
import MetalKit
import ScreenCaptureKit
import SwiftUI
import Testing
@testable import FocusIsland

@Suite(.serialized) @MainActor
struct GlassCaptureTests {
    @Test func cropReuseNeverExpandsOriginalPrivacyScopeOrCrossesDisplayAndScale() throws {
        let display = CGRect(x: -1440,y: 300,width: 1440,height: 900)
        let size = CGSize(width: 344,height: 92)
        let original = try #require(GlassCaptureRegion(window:CGRect(x: -1200,y: 500,width: 344,height: 92),
            display:display,scale:1,padding:18))
        for dx in [-10.0,0,10] {
            let moved = try #require(GlassCaptureRegion(window:CGRect(x: -1200+dx,y: 505,width: 344,height: 92),
                display:display,scale:1,padding:18))
            #expect(original.canReuse(for:moved,viewportSize:size))
            #expect(original.reframed(to:moved).source == original.source)
        }
        let outside = try #require(GlassCaptureRegion(window:CGRect(x: -1189,y: 500,width: 344,height: 92),
            display:display,scale:1,padding:18))
        #expect(original.reframed(to:outside).containsVisibleViewport(size))
        #expect(!original.canReuse(for:outside,viewportSize:size)) // still drawable, outside +28 privacy bound
        let otherScale = try #require(GlassCaptureRegion(window:CGRect(x: -1200,y: 500,width: 344,height: 92),
            display:display,scale:2,padding:18))
        #expect(!original.canReuse(for:otherScale,viewportSize:size))
        let otherScreen = try #require(GlassCaptureRegion(window:CGRect(x: 200,y: 200,width: 344,height: 92),
            display:CGRect(x: 0,y: 0,width:1440,height:900),scale:1,padding:18))
        #expect(!original.canReuse(for:otherScreen,viewportSize:size))
    }

    @Test func brightForegroundUsesHysteresisAndIgnoresInvalidReadings() {
        #expect(GlassBlurMetrics.foregroundTransitionDuration == 0.25)
        var decision = GlassTextContrastDecision()
        let bright = decision.update(luminance: 1, timestamp: 1)
        #expect(bright)
        #expect(decision.usesDarkText)
        let tooSoon = decision.update(luminance: 0, timestamp: 1.1)
        let middleBright = decision.update(luminance: 0.55, timestamp: 2)
        let dark = decision.update(luminance: 0.3, timestamp: 3)
        #expect(!tooSoon && !middleBright && dark)
        #expect(!decision.usesDarkText)
        let middleDark = decision.update(luminance: 0.6, timestamp: 4)
        let invalidLuminance = decision.update(luminance: .nan, timestamp: 5)
        let invalidTime = decision.update(luminance: 1, timestamp: .infinity)
        #expect(!middleDark && !invalidLuminance && !invalidTime)
    }

    @Test func blurScalesOnlyCapturedBackgroundAndBrightSubpageKeepsLayout() {
        #expect(GlassBlurMetrics.captureScale(radius: 0, displayScale: 2) == 2)
        #expect(GlassBlurMetrics.captureScale(radius: 1.5, displayScale: 2) == 1)
        #expect(GlassBlurMetrics.captureScale(radius: 6, displayScale: 1) == 1)
        var environment = EnvironmentValues()
        environment.whiteSubpageForeground = true
        environment.darkGlassForeground = true
        #expect(NSColor(SubpageForegroundStyle.primary.resolve(in: environment)).usingColorSpace(.sRGB)?.redComponent == 0)
        environment.darkGlassForeground = false
        #expect(NSColor(SubpageForegroundStyle.primary.resolve(in: environment)).usingColorSpace(.sRGB)?.redComponent == 1)
    }

    @Test func partialViewportAtEveryScreenEdgeStillAcceptsLivePixels() throws {
        let display = CGRect(x: -1440, y: 300, width: 1440, height: 900)
        let size = CGSize(width: 344, height: 92)
        for origin in [CGPoint(x: -1470, y: 600), CGPoint(x: -320, y: 600),
                       CGPoint(x: -800, y: 270), CGPoint(x: -800, y: 1130)] {
            let region = try #require(GlassCaptureRegion(window: CGRect(origin: origin, size: size), display: display, scale: 1))
            #expect(!region.containsViewport(size))
            #expect(region.containsVisibleViewport(size))
            let renderer = try #require(CapturedGlassRenderer())
            let view = CapturedGlassView(frame: CGRect(origin: .zero, size: size), device: renderer.device)
            #expect(renderer.accept(try #require(sample(width: region.pixelWidth, height: region.pixelHeight).imageBuffer), region: region, in: view))
            #expect(renderer.reframe(to: region, in: view))
        }
        let offscreen = try #require(GlassCaptureRegion(window: CGRect(x: -1800, y: 600, width: 344, height: 92), display: display, scale: 1))
        #expect(!offscreen.containsVisibleViewport(size))
    }
    @Test func permissionRequestsOnlyOnFirstExplicitSelectionAndRemembersDenial() throws {
        let name = "GlassPermissionTests.\(UUID())"
        let defaults = try #require(UserDefaults(suiteName: name))
        defer { defaults.removePersistentDomain(forName: name) }
        let state = PermissionFixture()
        let permission = GlassCapturePermission(defaults: defaults, checkAccess: { state.allowed }, requestAccess: {
            state.requests += 1
            return state.allowed
        })
        permission.refresh()
        #expect(state.requests == 0 && !permission.granted)
        permission.requestForUserSelection()
        #expect(state.requests == 1 && !permission.granted && !permission.requesting)
        permission.requestForUserSelection()
        permission.refresh()
        #expect(state.requests == 1)
        let restored = GlassCapturePermission(defaults: defaults, checkAccess: { state.allowed }, requestAccess: {
            state.requests += 1
            return state.allowed
        })
        restored.requestForUserSelection()
        #expect(state.requests == 1 && restored.requestAttempted)
        state.allowed = true
        permission.refresh()
        #expect(permission.granted)
        state.allowed = false
        permission.refresh()
        #expect(!permission.granted) // revocation is observable without a dialog
    }

    @Test func grantedAccessDoesNotRequestAgainAndNewGrantIsReadBack() throws {
        let name = "GlassGrantTests.\(UUID())"
        let defaults = try #require(UserDefaults(suiteName: name))
        defer { defaults.removePersistentDomain(forName: name) }
        let state = PermissionFixture()
        let permission = GlassCapturePermission(defaults: defaults, checkAccess: { state.allowed }, requestAccess: {
            state.requests += 1
            state.allowed = true
            return true
        })
        permission.requestForUserSelection()
        #expect(state.requests == 1 && permission.granted)
        permission.requestForUserSelection()
        #expect(state.requests == 1)
    }

    @Test func captureSelectionIsSeparateFromTheOriginalTransparentStyle() {
        #expect(!IslandBackgroundStyle.standard.usesCapturedGlass)
        #expect(!IslandBackgroundStyle.glass.usesCapturedGlass)
        #expect(IslandBackgroundStyle.liquidGlass.usesCapturedGlass)
        #expect(IslandBackgroundStyle.liquidGlass.isGlass)
        let view = LiquidGlassContentView(foreground: NSView(frame: CGRect(x: 0, y: 0, width: 344, height: 92)))
        view.setBackgroundStyle(.liquidGlass)
        view.setBackgroundStyle(.glass)
        #expect(view.backgroundStyle == .glass)
        // Exercise both accessibility states instead of inheriting the runner's preference.
        view.refreshMaterial(reduceTransparency: false)
        #expect(view.effect?.isHidden == false)
        #expect(!view.subviews.contains { $0 is MTKView })
        view.refreshMaterial(reduceTransparency: true)
        #expect(view.effect == nil)
    }

    @Test func regionUsesTopLeftDisplayPointsWithBoundedPadding() throws {
        let region = try #require(GlassCaptureRegion(
            window: CGRect(x: 100, y: 200, width: 344, height: 92),
            display: CGRect(x: 0, y: 0, width: 1440, height: 900), scale: 2))
        #expect(region.source == CGRect(x: 72, y: 580, width: 400, height: 148))
        #expect(region.offset == CGPoint(x: 28, y: 28))
        #expect(region.pixelWidth == 800 && region.pixelHeight == 296)
    }

    @Test func clippingAndNegativeOrVerticallyOffsetDisplaysKeepTextureAlignment() throws {
        let display = CGRect(x: -1280, y: 300, width: 1280, height: 800)
        let region = try #require(GlassCaptureRegion(
            window: CGRect(x: -1280, y: 1008, width: 344, height: 92), display: display, scale: 1))
        #expect(region.source == CGRect(x: 0, y: 0, width: 372, height: 120))
        #expect(region.offset == .zero)
        #expect(region.pixelWidth == 372 && region.pixelHeight == 120)
        let bottom = try #require(GlassCaptureRegion(
            window: CGRect(x: -344, y: 300, width: 344, height: 92), display: display, scale: 2))
        #expect(bottom.source == CGRect(x: 908, y: 680, width: 372, height: 120))
        #expect(bottom.offset == CGPoint(x: 28, y: 28))
    }

    @Test func allSupportedHUDScalesRemainBoundedAndInvalidGeometryIsRejected() throws {
        let display = CGRect(x: 0, y: 0, width: 1920, height: 1080)
        for scale in [0.5, 0.65, 0.82, 1.0] {
            let size = FocusDesign.Window.scaledSize(for: scale)
            let region = try #require(GlassCaptureRegion(window: CGRect(origin: CGPoint(x: 200, y: 300), size: size), display: display, scale: 2))
            #expect(region.source.width < 410 && region.source.height < 160)
            #expect(region.offset.x <= 28 && region.offset.x > 27.5)
            #expect(region.offset.y <= 28 && region.offset.y > 27.5)
        }
        for bad in [0.0, -1, .nan, .infinity] {
            #expect(GlassCaptureRegion(window: CGRect(x: 100, y: 100, width: 344, height: 92), display: display, scale: bad) == nil)
        }
        #expect(GlassCaptureRegion(window: .zero, display: display, scale: 2) == nil)
        #expect(GlassCaptureRegion(window: CGRect(x: -500, y: -500, width: 100, height: 100), display: display, scale: 2) == nil)
    }

    @Test func configurationHasNoAudioCursorOrFullDisplayBuffer() throws {
        let region = try #require(GlassCaptureRegion(window: CGRect(x: 100, y: 100, width: 344, height: 92),
                                                    display: CGRect(x: 0, y: 0, width: 1440, height: 900), scale: 2))
        let config = region.configuration(reduceMotion: false)
        #expect(config.sourceRect == region.source)
        #expect(config.width == 800 && config.height == 296)
        #expect(config.queueDepth == 3)
        #expect(!config.showsCursor && !config.showMouseClicks)
        #expect(!config.capturesAudio && !config.captureMicrophone)
        #expect(abs(CMTimeGetSeconds(config.minimumFrameInterval) - 1 / 60) < 0.000001)
        #expect(abs(CMTimeGetSeconds(region.configuration(reduceMotion: true).minimumFrameInterval) - 1 / 30) < 0.000001)
    }

    @Test func unattachedSurfaceNeverRequestsOrStartsCaptureAndKeepsFallbackHitTargets() {
        let foreground = NSView(frame: CGRect(x: 0, y: 0, width: 344, height: 92))
        let button = NSButton(frame: CGRect(x: 200, y: 30, width: 60, height: 24))
        foreground.addSubview(button)
        let view = LiquidGlassContentView(foreground: foreground)
        view.setBackgroundStyle(.glass)
        view.refreshMaterial(reduceTransparency: false)
        view.layoutSubtreeIfNeeded()
        view.reconcileCapture()
        #expect(view.subviews.count == 3)
        #expect(view.subviews.allSatisfy { !($0 is MTKView) })
        #expect(view.effect?.alphaValue == 0.25)
        #expect(view.hitTest(CGPoint(x: 230, y: 42)) === button)
        view.suspendCapture(true)
        #expect(view.effect?.isHidden == false)
        view.setBackgroundStyle(.standard)
        #expect(view.effect == nil)
    }

    @Test func productionMetalShaderCompilesAndRetainsOnDemandRendering() throws {
        let renderer = try #require(CapturedGlassRenderer())
        #expect(MemoryLayout<CapturedGlassRenderer.Uniforms>.stride == 32)
        let view = CapturedGlassView(frame: CGRect(x: 0, y: 0, width: 344, height: 92), device: renderer.device)
        view.isPaused = true
        view.enableSetNeedsDisplay = false
        #expect(view.isPaused)
        #expect(!view.enableSetNeedsDisplay)
        #expect(view.presentsWithTransaction)
        #expect(view.hitTest(CGPoint(x: 100, y: 40)) == nil)
        #expect(renderer.submittedFrames == 0)
        renderer.clear()
        #expect(renderer.submittedFrames == 0)
    }

    @Test func cachedCropFollowsMovementWithoutMovingItsPhysicalSource() throws {
        let display = CGRect(x: -1440, y: 0, width: 1440, height: 900)
        let size = CGSize(width: 344, height: 92)
        let original = try #require(GlassCaptureRegion(window: CGRect(origin: CGPoint(x: -1200, y: 400), size: size), display: display, scale: 2))
        for dx in [-20.0, 0, 20] {
            let moved = try #require(GlassCaptureRegion(window: CGRect(origin: CGPoint(x: -1200 + dx, y: 410), size: size), display: display, scale: 2))
            let mapped = original.reframed(to: moved)
            #expect(mapped.source == original.source)
            #expect(mapped.offset == CGPoint(x: 28 + dx, y: 18))
            #expect(mapped.containsViewport(size))
        }
        let distant = try #require(GlassCaptureRegion(window: CGRect(origin: CGPoint(x: -1000, y: 400), size: size), display: display, scale: 2))
        #expect(!original.reframed(to: distant).containsViewport(size))
    }

    @Test func fractionalCropIsPixelAlignedWithoutExpandingCaptureOrMovingTheViewport() throws {
        let display = CGRect(x: -1440, y: 300, width: 1440, height: 900)
        for scale in [1.0, 2] {
            for hudScale in [0.5, 0.65, 0.82, 1.0] {
                let window = CGRect(origin: CGPoint(x: -1200.23, y: 400.37), size: FocusDesign.Window.scaledSize(for: hudScale))
                let region = try #require(GlassCaptureRegion(window: window, display: display, scale: scale))
                let approved = window.insetBy(dx: -28, dy: -28).intersection(display)
                let actual = CGRect(x: display.minX + region.source.minX, y: display.maxY - region.source.maxY,
                                    width: region.source.width, height: region.source.height)
                #expect(approved.contains(actual))
                for value in [region.source.minX, region.source.minY, region.source.maxX, region.source.maxY] {
                    #expect(abs(value * scale - (value * scale).rounded()) < 0.000001)
                }
                #expect(abs(region.source.minX + region.offset.x - (window.minX - display.minX)) < 0.000001)
                #expect(abs(region.source.minY + region.offset.y - (display.maxY - window.maxY)) < 0.000001)
                #expect(region.pixelWidth == Int((region.source.width * scale).rounded()))
            }
        }
    }

    @Test func actualFrameMetadataIdentifiesKnownCropsAndRejectsAmbiguousOrUnknownGeometry() throws {
        let display = CGRect(x: -1440, y: 300, width: 1440, height: 900)
        let original = try #require(GlassCaptureRegion(window: CGRect(x: -1200, y: 500, width: 344, height: 92), display: display, scale: 2))
        let moved = try #require(GlassCaptureRegion(window: CGRect(x: -1190, y: 500, width: 344, height: 92), display: display, scale: 2))
        let candidates = [moved, original]
        #expect(GlassCaptureRegion.identified(in: candidates, screenRect: original.source,
            pixelWidth: original.pixelWidth, pixelHeight: original.pixelHeight) == original)
        let global = CGRect(x: display.minX + moved.source.minX, y: display.maxY - moved.source.maxY,
                            width: moved.source.width, height: moved.source.height)
        #expect(GlassCaptureRegion.identified(in: candidates, screenRect: global,
            pixelWidth: moved.pixelWidth, pixelHeight: moved.pixelHeight) == moved)
        for invalid in [nil, .zero, display, CGRect(x: 9, y: 9, width: 4, height: 4)] as [CGRect?] {
            #expect(GlassCaptureRegion.identified(in: candidates, screenRect: invalid,
                pixelWidth: moved.pixelWidth, pixelHeight: moved.pixelHeight) == nil)
        }
        #expect(GlassCaptureRegion.identified(in: candidates, screenRect: moved.source,
            pixelWidth: 10, pixelHeight: 10) == nil)
        // Mirrored local top/bottom candidates cannot safely identify a frame.
        let mirrored = try #require(GlassCaptureRegion(window: CGRect(x: -1190, y: 908, width: 344, height: 92), display: display, scale: 2))
        #expect(GlassCaptureRegion.identified(in: [moved, mirrored], screenRect: moved.source,
            pixelWidth: moved.pixelWidth, pixelHeight: moved.pixelHeight) == nil)
    }

    @Test func cropCadenceDoesNotAddAnotherIntervalAfterCompositorCompletion() {
        var cadence = GlassCropUpdateCadence()
        #expect(cadence.delay(at: 100, reduceMotion: false) == 0)
        cadence.requested(at: 100)
        #expect(abs(cadence.delay(at: 100.010, reduceMotion: false) - (1 / 60 - 0.010)) < 0.000001)
        #expect(cadence.delay(at: 100.020, reduceMotion: false) == 0)
        #expect(abs(cadence.delay(at: 100.020, reduceMotion: true) - (1 / 30 - 0.020)) < 0.000001)
    }

    @Test func verifiedFramesKeepTheirPhysicalCropDuringConfigurationAndStopImmediately() async throws {
        let display = CGRect(x: 0, y: 0, width: 1920, height: 1080)
        let original = try #require(GlassCaptureRegion(window: CGRect(x: 100, y: 300, width: 344, height: 92), display: display, scale: 2))
        let moved = try #require(GlassCaptureRegion(window: CGRect(x: 120, y: 300, width: 344, height: 92), display: display, scale: 2))
        let fixture = FrameFixture()
        let output = GlassCaptureOutput(region: original, receive: { _, region in fixture.regions.append(region) }, failure: {})
        output.beginUpdate(region: moved)
        output.consume(try sample(width: original.pixelWidth, height: original.pixelHeight, screenRect: original.source))
        try await waitFor { fixture.regions.count == 1 }
        #expect(fixture.regions == [original])
        output.consume(try sample(width: moved.pixelWidth, height: moved.pixelHeight, screenRect: moved.source))
        try await waitFor { fixture.regions.count == 2 }
        #expect(fixture.regions.last == moved)
        output.finishUpdate(region: moved)
        output.consume(try sample(width: original.pixelWidth, height: original.pixelHeight, screenRect: original.source))
        try await waitFor { fixture.regions.count == 3 }
        #expect(fixture.regions.last == original) // never relabel an old crop as the new position
        output.invalidate()
        output.consume(try sample(width: moved.pixelWidth, height: moved.pixelHeight, screenRect: moved.source))
        #expect(fixture.regions.count == 3)
    }

    @Test func emptyDamageIsSkippedOnlyAfterTheRendererAcceptedTheSamePhysicalCrop() async throws {
        let display = CGRect(x: 0, y: 0, width: 1920, height: 1080)
        let original = try #require(GlassCaptureRegion(window: CGRect(x: 100, y: 300, width: 344, height: 92), display: display, scale: 2))
        let moved = try #require(GlassCaptureRegion(window: CGRect(x: 120, y: 300, width: 344, height: 92), display: display, scale: 2))
        let fixture = FrameFixture()
        let output = GlassCaptureOutput(region: original, receive: { _, region in fixture.regions.append(region) }, failure: {})
        output.consume(try sample(width: original.pixelWidth, height: original.pixelHeight, screenRect: original.source, emptyDamage: true))
        try await waitFor { fixture.regions.count == 1 }
        output.noteAccepted(original)
        output.consume(try sample(width: original.pixelWidth, height: original.pixelHeight, screenRect: original.source, emptyDamage: true))
        output.beginUpdate(region: moved)
        output.consume(try sample(width: moved.pixelWidth, height: moved.pixelHeight, screenRect: moved.source, emptyDamage: true))
        try await waitFor { fixture.regions.count == 2 }
        #expect(fixture.regions == [original, moved])
    }

    @Test func deliveryUsesLatestFrameAndRejectsIdleOldCropAndStoppedFrames() async throws {
        let original = try #require(GlassCaptureRegion(window: CGRect(x: 100, y: 100, width: 344, height: 92),
            display: CGRect(x: 0, y: 0, width: 1440, height: 900), scale: 2))
        let moved = try #require(GlassCaptureRegion(window: CGRect(x: 110, y: 100, width: 344, height: 92),
            display: CGRect(x: 0, y: 0, width: 1440, height: 900), scale: 2))
        let fixture = FrameFixture()
        let output = GlassCaptureOutput(region: original, receive: { pixels, region in
            fixture.widths.append(CVPixelBufferGetWidth(pixels))
            fixture.regions.append(region)
        }, failure: {})
        output.consume(try sample(width: 16, status: .idle))
        output.consume(try sample(width: 24))
        output.consume(try sample(width: 32))
        try await waitFor { fixture.widths.count == 1 }
        #expect(fixture.widths == [32])
        #expect(fixture.regions == [original])
        let old = try sample(width: 40)
        output.consume(old) // queued, then discarded by configuration epoch
        output.beginUpdate()
        output.consume(try sample(width: 48)) // while reconfiguring
        output.finishUpdate(region: moved)
        output.consume(old) // late packet from previous compositor crop
        output.consume(try sample(width: 56))
        try await waitFor { fixture.widths.count == 2 }
        #expect(fixture.widths == [32, 56])
        #expect(fixture.regions.last == moved)
        output.invalidate()
        output.consume(try sample(width: 64))
        #expect(fixture.widths.count == 2)
    }

    @Test func fastMovementKeepsTheLastTextureUntilAValidCropReplacement() throws {
        let display = CGRect(x: 0, y: 0, width: 1920, height: 1080)
        let size = CGSize(width: 344, height: 92)
        let original = try #require(GlassCaptureRegion(window: CGRect(origin: CGPoint(x: 100, y: 500), size: size), display: display, scale: 2))
        let distant = try #require(GlassCaptureRegion(window: CGRect(origin: CGPoint(x: 700, y: 200), size: size), display: display, scale: 2))
        let renderer = try #require(CapturedGlassRenderer())
        let view = CapturedGlassView(frame: CGRect(origin: .zero, size: size), device: renderer.device)
        view.isPaused = true
        view.enableSetNeedsDisplay = false
        #expect(renderer.accept(try #require(sample(width: original.pixelWidth, height: original.pixelHeight).imageBuffer), region: original, in: view))
        #expect(!renderer.reframe(to: distant, in: view))
        // A failed reframe must retain the original texture, not clear it.
        #expect(renderer.reframe(to: original, in: view))
        #expect(renderer.accept(try #require(sample(width: distant.pixelWidth, height: distant.pixelHeight).imageBuffer), region: distant, in: view))
        #expect(renderer.reframe(to: distant, in: view))
        renderer.clear()
        #expect(!renderer.reframe(to: distant, in: view))
    }

    private func waitFor(_ predicate: @MainActor () -> Bool) async throws {
        let deadline = Date().addingTimeInterval(3)
        while !predicate(), Date() < deadline { try await Task.sleep(for: .milliseconds(5)) }
        #expect(predicate())
    }

    private func sample(width: Int, height: Int = 16, status: SCFrameStatus = .complete,
                        screenRect: CGRect? = nil, emptyDamage: Bool = false) throws -> CMSampleBuffer {
        var pixels: CVPixelBuffer?
        let attributes = [kCVPixelBufferMetalCompatibilityKey: true,
                          kCVPixelBufferIOSurfacePropertiesKey: [:]] as CFDictionary
        #expect(CVPixelBufferCreate(nil, width, height, kCVPixelFormatType_32BGRA, attributes, &pixels) == kCVReturnSuccess)
        let image = try #require(pixels)
        var description: CMVideoFormatDescription?
        #expect(CMVideoFormatDescriptionCreateForImageBuffer(allocator: nil, imageBuffer: image,
            formatDescriptionOut: &description) == noErr)
        var timing = CMSampleTimingInfo(duration: .invalid, presentationTimeStamp: CMClockGetTime(CMClockGetHostTimeClock()), decodeTimeStamp: .invalid)
        var sample: CMSampleBuffer?
        #expect(CMSampleBufferCreateReadyWithImageBuffer(allocator: nil, imageBuffer: image,
            formatDescription: try #require(description), sampleTiming: &timing, sampleBufferOut: &sample) == noErr)
        let buffer = try #require(sample)
        let array = try #require(CMSampleBufferGetSampleAttachmentsArray(buffer, createIfNecessary: true))
        let dictionary = unsafeBitCast(CFArrayGetValueAtIndex(array, 0), to: NSMutableDictionary.self)
        dictionary[SCStreamFrameInfo.status.rawValue] = NSNumber(value: status.rawValue)
        dictionary[SCStreamFrameInfo.displayTime.rawValue] = NSNumber(value: mach_absolute_time())
        if let screenRect { dictionary[SCStreamFrameInfo.screenRect.rawValue] = screenRect.dictionaryRepresentation }
        if emptyDamage { dictionary[SCStreamFrameInfo.dirtyRects.rawValue] = [] as [NSValue] }
        return buffer
    }

    @Test func newPermissionAndRollbackLabelsHaveEnglishTranslations() {
        for key in ["液态玻璃", "授权屏幕录制 · 启用玻璃", "玻璃暂不可用 · 检查录制权限",
                    "液态玻璃仅在本机采集窗口背后的小块画面，不保存、不上传；选择透明可恢复原材质。"] {
            #expect(L10n.text(key, language: .english) != key)
        }
    }

    @Test func backgroundDropdownOffersAllThreeStylesWithUnchangedEdgeHitTargets() {
        let labels = ["黑色", "透明", "液态玻璃"]
        for language in AppLanguage.allCases {
            let host = NSHostingView(rootView: GlassSettingsPicker(title: L10n.text("背景样式", language: language),
                labels: labels.map { L10n.text($0, language: language) }, selectedIndex: 2, onSelect: { _ in })
                .frame(width: 96, height: 24))
            host.frame = CGRect(x: 0, y: 0, width: 96, height: 24)
            let window = NSWindow(contentRect: host.frame, styleMask: .borderless, backing: .buffered, defer: false)
            window.isReleasedWhenClosed = false
            window.contentView = host
            defer { window.close() }
            host.layoutSubtreeIfNeeded()
            func popup(_ view: NSView) -> NSPopUpButton? {
                if let button = view as? NSPopUpButton { return button }
                return view.subviews.lazy.compactMap { popup($0) }.first
            }
            let button = popup(host)
            #expect(button?.numberOfItems == 3)
            #expect(button?.indexOfSelectedItem == 2)
            #expect(button?.frame.size == CGSize(width: 96, height: 24))
            if let button {
                for x in [CGFloat(1), 95] {
                    #expect(host.hitTest(button.convert(CGPoint(x: x, y: 12), to: host)) === button)
                }
            }
        }
    }

    @Test func productionShaderRefractsGeneratedPixelsWithoutChangingCenterOrAlpha() throws {
        // Pure GPU fixture: generated local pixels only, never desktop capture.
        let renderer = try #require(CapturedGlassRenderer())
        let device = renderer.device
        let queue = try #require(device.makeCommandQueue())
        let library = try device.makeLibrary(source: CapturedGlassRenderer.shader, options: nil)
        let descriptor = MTLRenderPipelineDescriptor()
        descriptor.vertexFunction = library.makeFunction(name: "glassVertex")
        descriptor.fragmentFunction = library.makeFunction(name: "glassFragment")
        descriptor.colorAttachments[0].pixelFormat = .bgra8Unorm
        let pipeline = try device.makeRenderPipelineState(descriptor: descriptor)
        for hudScale in [0.5, 0.65, 0.82, 1.0] {
            let size = FocusDesign.Window.scaledSize(for: hudScale)
            let region = try #require(GlassCaptureRegion(window: CGRect(origin: CGPoint(x: 100, y: 100), size: size),
                display: CGRect(x: 0, y: 0, width: 1440, height: 900), scale: 2))
            let sourceDesc = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm,
                width: region.pixelWidth, height: region.pixelHeight, mipmapped: false)
            sourceDesc.storageMode = .shared
            sourceDesc.usage = .shaderRead
            let source = try #require(device.makeTexture(descriptor: sourceDesc))
            var pattern = [UInt8](repeating: 255, count: region.pixelWidth * region.pixelHeight * 4)
            for y in 0..<region.pixelHeight { for x in 0..<region.pixelWidth {
                let index = (y * region.pixelWidth + x) * 4
                pattern[index] = 64
                pattern[index + 1] = 128
                pattern[index + 2] = ((x / 8) % 2 == 0) ? 32 : 224
            }}
            pattern.withUnsafeBytes { source.replace(region: MTLRegionMake2D(0, 0, region.pixelWidth, region.pixelHeight),
                mipmapLevel: 0, withBytes: $0.baseAddress!, bytesPerRow: region.pixelWidth * 4) }
            let width = Int((size.width * 2).rounded()), height = Int((size.height * 2).rounded())
            let outputDesc = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm, width: width, height: height, mipmapped: false)
            outputDesc.storageMode = .shared
            outputDesc.usage = .renderTarget
            let target = try #require(device.makeTexture(descriptor: outputDesc))
            let pass = MTLRenderPassDescriptor()
            pass.colorAttachments[0].texture = target
            pass.colorAttachments[0].loadAction = .clear
            pass.colorAttachments[0].storeAction = .store
            let command = try #require(queue.makeCommandBuffer())
            let encoder = try #require(command.makeRenderCommandEncoder(descriptor: pass))
            var uniforms = CapturedGlassRenderer.Uniforms(
                viewport: SIMD4(Float(size.width), Float(size.height), 2, Float(FocusDesign.Window.cornerRadius * hudScale)),
                capture: SIMD4(28, 28, Float(region.source.width), Float(region.source.height)))
            encoder.setRenderPipelineState(pipeline)
            encoder.setFragmentBytes(&uniforms, length: MemoryLayout<CapturedGlassRenderer.Uniforms>.stride, index: 0)
            encoder.setFragmentTexture(source, index: 0)
            encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
            encoder.endEncoding()
            command.commit()
            command.waitUntilCompleted()
            #expect(command.status == .completed)
            var rendered = [UInt8](repeating: 0, count: width * height * 4)
            rendered.withUnsafeMutableBytes { target.getBytes($0.baseAddress!, bytesPerRow: width * 4,
                from: MTLRegionMake2D(0, 0, width, height), mipmapLevel: 0) }
            #expect(rendered[3] == 0) // rounded top-left is transparent
            let center = ((height / 2) * width + width / 2) * 4
            #expect(rendered[center + 3] == 255)
            #expect(abs(Int(rendered[center]) - 64) < 8)
            #expect(abs(Int(rendered[center + 1]) - 128) < 8)
            // Near the right edge, the stripe must differ from undisplaced UV.
            var differences = 0
            let row = height / 2
            for x in (width - Int(24 * hudScale * 2))..<(width - 2) {
                let sourceX = x + 56
                let expected = (sourceX / 8) % 2 == 0 ? 32 : 224
                if abs(Int(rendered[(row * width + x) * 4 + 2]) - expected) > 50 { differences += 1 }
            }
            #expect(differences > 4)
        }
    }

    @Test func changingPhysicalCropDoesNotMoveTheSameWorldBackgroundPixels() throws {
        let renderer = try #require(CapturedGlassRenderer())
        let device = renderer.device
        let queue = try #require(device.makeCommandQueue())
        let library = try device.makeLibrary(source: CapturedGlassRenderer.shader, options: nil)
        let descriptor = MTLRenderPipelineDescriptor()
        descriptor.vertexFunction = library.makeFunction(name: "glassVertex")
        descriptor.fragmentFunction = library.makeFunction(name: "glassFragment")
        descriptor.colorAttachments[0].pixelFormat = .bgra8Unorm
        let pipeline = try device.makeRenderPipelineState(descriptor: descriptor)
        let display = CGRect(x: 0, y: 0, width: 1920, height: 1080)
        for hudScale in [0.5, 0.65, 0.82, 1.0] {
            let size = FocusDesign.Window.scaledSize(for: hudScale)
            let original = try #require(GlassCaptureRegion(window: CGRect(origin: CGPoint(x: 100.23, y: 300.37), size: size), display: display, scale: 2))
            let moved = try #require(GlassCaptureRegion(window: CGRect(origin: CGPoint(x: 110.98, y: 296.12), size: size), display: display, scale: 2))
            func render(_ physical: GlassCaptureRegion) throws -> [UInt8] {
                let sourceDesc = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm,
                    width: physical.pixelWidth, height: physical.pixelHeight, mipmapped: false)
                sourceDesc.storageMode = .shared
                sourceDesc.usage = .shaderRead
                let source = try #require(device.makeTexture(descriptor: sourceDesc))
                var pixels = [UInt8](repeating: 255, count: physical.pixelWidth * physical.pixelHeight * 4)
                for y in 0..<physical.pixelHeight { for x in 0..<physical.pixelWidth {
                    let worldX = Int((physical.source.minX * 2).rounded()) + x
                    let worldY = Int((physical.source.minY * 2).rounded()) + y
                    let index = (y * physical.pixelWidth + x) * 4
                    pixels[index] = UInt8((worldY / 7 % 2) * 128 + 32)
                    pixels[index + 1] = UInt8((worldX / 9 % 2) * 128 + 32)
                    pixels[index + 2] = UInt8((worldX / 5 % 2) * 128 + 32)
                }}
                pixels.withUnsafeBytes { source.replace(region: MTLRegionMake2D(0, 0, physical.pixelWidth, physical.pixelHeight),
                    mipmapLevel: 0, withBytes: $0.baseAddress!, bytesPerRow: physical.pixelWidth * 4) }
                let width = Int(ceil(size.width * 2)), height = Int(ceil(size.height * 2))
                let targetDesc = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm, width: width, height: height, mipmapped: false)
                targetDesc.storageMode = .shared
                targetDesc.usage = .renderTarget
                let target = try #require(device.makeTexture(descriptor: targetDesc))
                let pass = MTLRenderPassDescriptor()
                pass.colorAttachments[0].texture = target
                pass.colorAttachments[0].loadAction = .clear
                pass.colorAttachments[0].storeAction = .store
                let command = try #require(queue.makeCommandBuffer())
                let encoder = try #require(command.makeRenderCommandEncoder(descriptor: pass))
                let mapped = physical.reframed(to: moved)
                #expect(mapped.containsViewport(size))
                var uniforms = CapturedGlassRenderer.Uniforms(
                    viewport: SIMD4(Float(size.width), Float(size.height), 2, Float(FocusDesign.Window.cornerRadius * hudScale)),
                    capture: SIMD4(Float(mapped.offset.x), Float(mapped.offset.y), Float(physical.source.width), Float(physical.source.height)))
                encoder.setRenderPipelineState(pipeline)
                encoder.setFragmentBytes(&uniforms, length: MemoryLayout<CapturedGlassRenderer.Uniforms>.stride, index: 0)
                encoder.setFragmentTexture(source, index: 0)
                encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
                encoder.endEncoding()
                command.commit()
                command.waitUntilCompleted()
                #expect(command.status == .completed)
                var result = [UInt8](repeating: 0, count: width * height * 4)
                result.withUnsafeMutableBytes { target.getBytes($0.baseAddress!, bytesPerRow: width * 4,
                    from: MTLRegionMake2D(0, 0, width, height), mipmapLevel: 0) }
                return result
            }
            let cached = try render(original), refreshed = try render(moved)
            #expect(zip(cached, refreshed).allSatisfy { abs(Int($0) - Int($1)) <= 1 })
        }
    }
}

@MainActor private final class PermissionFixture {
    var allowed = false
    var requests = 0
}

@MainActor private final class FrameFixture {
    var widths: [Int] = []
    var regions: [GlassCaptureRegion] = []
}

