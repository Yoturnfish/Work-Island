import AppKit
@preconcurrency import ScreenCaptureKit
import CoreMedia
import CoreVideo
import OSLog

/// AppKit frames are bottom-up global points; sourceRect is top-down display
/// local points. Clipped padding preserves alignment at screen edges.
struct GlassCaptureRegion: Equatable, Sendable {
    let source: CGRect
    let offset: CGPoint
    let scale: CGFloat
    let display: CGRect
    var pixelWidth: Int { max(1, Int(ceil(source.width * scale))) }
    var pixelHeight: Int { max(1, Int(ceil(source.height * scale))) }

    private init(source: CGRect, offset: CGPoint, scale: CGFloat, display: CGRect) {
        self.source = source; self.offset = offset; self.scale = scale; self.display = display
    }

    /// Reuse the retained texture during small moves while the compositor
    /// applies the next crop. Its physical source stays fixed; UVs follow HUD.
    func reframed(to desired: Self) -> Self {
        Self(source: source,
             offset: CGPoint(x: desired.source.minX + desired.offset.x - source.minX,
                             y: desired.source.minY + desired.offset.y - source.minY), scale: scale, display: display)
    }

    func hasSamePixels(as other: Self) -> Bool {
        source == other.source && scale == other.scale && display == other.display
    }

    /// Frame metadata describes the actual captured content, not the most
    /// recently requested crop. Accept only an unambiguous known crop/size.
    static func identified(in candidates: [Self], screenRect: CGRect?, pixelWidth: Int, pixelHeight: Int) -> Self? {
        guard let rect = screenRect, [rect.minX, rect.minY, rect.width, rect.height].allSatisfy(\.isFinite),
              rect.width > 0, rect.height > 0 else { return nil }
        let matches = candidates.filter { candidate in
            guard candidate.pixelWidth == pixelWidth, candidate.pixelHeight == pixelHeight else { return false }
            let source = candidate.source, display = candidate.display
            let globalBottomUp = CGRect(x: display.minX + source.minX, y: display.maxY - source.maxY,
                                        width: source.width, height: source.height)
            let localBottomUp = globalBottomUp.offsetBy(dx: -display.minX, dy: -display.minY)
            let globalTopDown = source.offsetBy(dx: display.minX, dy: display.minY)
            return [source, globalBottomUp, localBottomUp, globalTopDown].contains { expected in
                zip([rect.minX, rect.minY, rect.width, rect.height],
                    [expected.minX, expected.minY, expected.width, expected.height])
                    .allSatisfy { abs($0 - $1) < 0.01 }
            }
        }
        guard let first = matches.first, matches.allSatisfy({ $0.hasSamePixels(as: first) }) else { return nil }
        return first
    }

    func containsViewport(_ size: CGSize) -> Bool {
        offset.x >= 0 && offset.y >= 0
            && offset.x + size.width <= source.width && offset.y + size.height <= source.height
    }

    /// Offscreen portions of a freely dragged panel have no desktop pixels.
    /// Validate only the visible portion, without disabling release clamping.
    func containsVisibleViewport(_ size: CGSize) -> Bool {
        guard size.width.isFinite, size.height.isFinite, size.width > 0, size.height > 0 else { return false }
        let viewport = CGRect(x: source.minX + offset.x, y: source.minY + offset.y,
                              width: size.width, height: size.height)
        let visible = viewport.intersection(CGRect(origin: .zero, size: display.size))
        return !visible.isNull && !visible.isEmpty && source.insetBy(dx: -0.01, dy: -0.01).contains(visible)
    }

    /// Keep the physical crop fixed for small moves, but only while it covers
    /// the viewport AND remains inside the original current-window +28pt scope.
    /// Production uses 18pt padding, leaving 10pt of legal spatial headroom.
    func canReuse(for desired: Self, viewportSize: CGSize) -> Bool {
        guard display == desired.display, scale == desired.scale,
              reframed(to: desired).containsVisibleViewport(viewportSize) else { return false }
        let viewport = CGRect(x: desired.source.minX + desired.offset.x,
                              y: desired.source.minY + desired.offset.y,
                              width: viewportSize.width, height: viewportSize.height)
        let allowed = viewport.insetBy(dx: -28, dy: -28)
            .intersection(CGRect(origin: .zero, size: display.size))
        return allowed.contains(source)
    }

    init?(window: CGRect, display: CGRect, scale: CGFloat, padding: CGFloat = 28) {
        guard [window.minX, window.minY, window.width, window.height,
               display.minX, display.minY, display.width, display.height, scale, padding]
            .allSatisfy(\.isFinite), window.width > 0, window.height > 0,
              display.width > 0, display.height > 0, scale > 0, padding >= 0 else { return nil }
        let clipped = window.insetBy(dx: -padding, dy: -padding).intersection(display)
        guard !clipped.isNull, !clipped.isEmpty else { return nil }
        self.scale = min(2, scale)
        self.display = display
        let raw = CGRect(x: clipped.minX - display.minX, y: display.maxY - clipped.maxY,
                         width: clipped.width, height: clipped.height)
        // Inward alignment stays inside the existing capture permission scope
        // and removes fractional crop resampling that shimmers under text.
        let left = ceil(raw.minX * self.scale) / self.scale
        let top = ceil(raw.minY * self.scale) / self.scale
        let right = floor(raw.maxX * self.scale) / self.scale
        let bottom = floor(raw.maxY * self.scale) / self.scale
        guard right > left, bottom > top else { return nil }
        source = CGRect(x: left, y: top, width: right - left, height: bottom - top)
        offset = CGPoint(x: window.minX - display.minX - left, y: display.maxY - window.maxY - top)
    }

    func configuration(reduceMotion: Bool, framesPerSecond: Int = 60) -> SCStreamConfiguration {
        let config = SCStreamConfiguration()
        config.sourceRect = source
        config.width = pixelWidth
        config.height = pixelHeight
        config.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(
            GlassFrameRatePolicy.limited(framesPerSecond, reduceMotion: reduceMotion)))
        // Five surfaces avoid starving the producer while up to three GPU
        // commands and one latest-wins delivery retain captured input frames.
        config.queueDepth = GlassFrameRatePolicy.limited(framesPerSecond, reduceMotion: reduceMotion) > 60 ? 5 : 3
        config.pixelFormat = kCVPixelFormatType_32BGRA
        config.colorSpaceName = CGColorSpace.sRGB
        config.showsCursor = false
        config.showMouseClicks = false
        config.capturesAudio = false
        config.captureMicrophone = false
        config.streamName = "Work Island local glass background"
        return config
    }
}

/// Budget requests from their start, not completion + another frame interval.
/// The latter doubles crop latency when the compositor itself takes a frame.
struct GlassCropUpdateCadence {
    private var lastRequest: TimeInterval?
    func delay(at now: TimeInterval, reduceMotion: Bool, framesPerSecond: Int = 60) -> TimeInterval {
        guard let lastRequest else { return 0 }
        return max(0, 1 / Double(GlassFrameRatePolicy.limited(framesPerSecond, reduceMotion: reduceMotion)) - (now - lastRequest))
    }
    mutating func requested(at now: TimeInterval) { lastRequest = now }
}

/// At most one delivery is queued for the main actor. Idle/non-complete frames
/// are discarded; there is no timer, pixel readback, image encoding, or storage.
final class GlassCaptureOutput: NSObject, SCStreamOutput, SCStreamDelegate, @unchecked Sendable {
    private let lock = NSLock()
    private var pending = false
    private var active = true
    private var updating = false
    private var region: GlassCaptureRegion
    private var knownRegions: [GlassCaptureRegion]
    private var acceptedRegion: GlassCaptureRegion?
    private var latestDisplayTime: UInt64 = 0
    private var loggedGeometry = false
    private static let log = Logger(subsystem: "com.focusisland.app", category: "LiquidGlass")
    private var epoch: UInt64 = 0
    private var cutoff: UInt64 = 0
    private var latest: Delivery?
    private let receive: @MainActor @Sendable (CVPixelBuffer, GlassCaptureRegion) -> Void
    private let failure: @MainActor @Sendable () -> Void
    private struct Delivery {
        let frame: GlassPixelFrame
        let region: GlassCaptureRegion
        let epoch: UInt64
        let geometryVerified: Bool
    }

    init(region: GlassCaptureRegion, receive: @escaping @MainActor @Sendable (CVPixelBuffer, GlassCaptureRegion) -> Void,
         failure: @escaping @MainActor @Sendable () -> Void) {
        self.region = region
        knownRegions = [region]
        self.receive = receive
        self.failure = failure
    }

    func invalidate() { lock.lock(); active = false; latest = nil; lock.unlock() }

    func beginUpdate(region requested: GlassCaptureRegion? = nil) {
        lock.lock(); defer { lock.unlock() }
        updating = true; epoch += 1
        if latest?.geometryVerified != true { latest = nil }
        if let requested {
            knownRegions.removeAll { $0.hasSamePixels(as: requested) }
            knownRegions.insert(requested, at: 0)
            knownRegions = Array(knownRegions.prefix(6))
        }
    }

    func noteAccepted(_ region: GlassCaptureRegion) {
        lock.lock(); acceptedRegion = region; lock.unlock()
    }

    func finishUpdate(region: GlassCaptureRegion) {
        lock.lock()
        self.region = region
        cutoff = mach_absolute_time()
        updating = false
        lock.unlock()
    }

    private func finishDelivery() -> Delivery? {
        lock.lock(); defer { lock.unlock() }
        pending = false
        let value = latest
        latest = nil
        return active && (value?.geometryVerified == true || (!updating && value?.epoch == epoch)) ? value : nil
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen else { return }
        consume(sampleBuffer)
    }

    func consume(_ sampleBuffer: CMSampleBuffer) {
        guard sampleBuffer.isValid,
              let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let value = attachments.first?[.status] as? Int,
              value == SCFrameStatus.complete.rawValue,
              let pixels = sampleBuffer.imageBuffer else { return }
        lock.lock()
        let displayTime = (attachments.first?[.displayTime] as? NSNumber)?.uint64Value ?? 0
        let screenRect = Self.rect(attachments.first?[.screenRect])
        let identified = GlassCaptureRegion.identified(in: knownRegions, screenRect: screenRect,
            pixelWidth: CVPixelBufferGetWidth(pixels), pixelHeight: CVPixelBufferGetHeight(pixels))
        if !loggedGeometry {
            loggedGeometry = true
            Self.log.notice("Frame geometry: screen \(String(describing: screenRect), privacy: .public), expected crop \(String(describing: self.region.source), privacy: .public), verified \(identified != nil)")
        }
        guard active, displayTime == 0 || displayTime > latestDisplayTime,
              identified != nil || (!updating && (cutoff == 0 || displayTime > cutoff)) else { lock.unlock(); return }
        let physical = identified ?? region
        // Empty damage is reusable only after the renderer accepted this exact
        // crop. Movement still redraws cached pixels with fresh window UVs.
        if let dirty = attachments.first?[.dirtyRects] as? [Any], dirty.isEmpty,
           acceptedRegion?.hasSamePixels(as: physical) == true { lock.unlock(); return }
        if displayTime > 0 { latestDisplayTime = displayTime }
        // Latest-wins delivery prevents latency accumulating under load.
        latest = Delivery(frame: GlassPixelFrame(pixels), region: physical, epoch: epoch, geometryVerified: identified != nil)
        guard !pending else { lock.unlock(); return }
        pending = true
        lock.unlock()
        Task { @MainActor [weak self] in
            guard let self else { return }
            if let value = self.finishDelivery() { self.receive(value.frame.pixels, value.region) }
        }
    }

    private static func rect(_ value: Any?) -> CGRect? {
        if let dictionary = value as? [String: Any] { return CGRect(dictionaryRepresentation: dictionary as CFDictionary) }
        if let rect = value as? NSValue,
           String(cString: rect.objCType) == String(cString: NSValue(rect: .zero).objCType) { return rect.rectValue }
        return nil
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        lock.lock(); let report = active; active = false; lock.unlock()
        if report {
            let error = error as NSError
            Self.log.error("Glass stream stopped; domain \(error.domain, privacy: .public), code \(error.code)")
            Task { @MainActor [failure] in failure() }
        }
    }
}

/// CVPixelBuffer is immutable after delivery and retained through GPU completion.
final class GlassPixelFrame: @unchecked Sendable {
    let pixels: CVPixelBuffer
    init(_ pixels: CVPixelBuffer) { self.pixels = pixels }
}

@MainActor
final class GlassBackgroundCapture {
    private let stream: SCStream
    private let output: GlassCaptureOutput
    private var stopped = false
    private(set) var region: GlassCaptureRegion
    private(set) var reduceMotion: Bool
    private(set) var framesPerSecond: Int

    init(display: SCDisplay, content: SCShareableContent, region: GlassCaptureRegion,
         reduceMotion: Bool, framesPerSecond: Int = 60,
         receive: @escaping @MainActor @Sendable (CVPixelBuffer, GlassCaptureRegion) -> Void,
         failure: @escaping @MainActor @Sendable () -> Void) throws {
        // Exclude every window belonging to this process, including controls
        // and popovers. Capturing our own glass would create recursive feedback.
        let ownApps = content.applications.filter { $0.processID == ProcessInfo.processInfo.processIdentifier }
        guard !ownApps.isEmpty else { throw CaptureError.applicationMissing }
        let filter = SCContentFilter(display: display, excludingApplications: ownApps, exceptingWindows: [])
        self.region = region
        self.reduceMotion = reduceMotion
        self.framesPerSecond = GlassFrameRatePolicy.limited(framesPerSecond, reduceMotion: reduceMotion)
        output = GlassCaptureOutput(region: region, receive: receive, failure: failure)
        stream = SCStream(filter: filter, configuration: region.configuration(reduceMotion: reduceMotion,
            framesPerSecond: self.framesPerSecond), delegate: output)
        try stream.addStreamOutput(output, type: .screen, sampleHandlerQueue: DispatchQueue(label: "workisland.glass.capture", qos: .utility))
    }

    func start() async throws { try await stream.startCapture() }
    func update(region: GlassCaptureRegion, reduceMotion: Bool, framesPerSecond: Int = 60) async throws {
        guard !stopped else { return }
        output.beginUpdate(region: region)
        let rate = GlassFrameRatePolicy.limited(framesPerSecond, reduceMotion: reduceMotion)
        try await stream.updateConfiguration(region.configuration(reduceMotion: reduceMotion, framesPerSecond: rate))
        guard !stopped else { return }
        self.region = region
        self.reduceMotion = reduceMotion
        self.framesPerSecond = rate
        output.finishUpdate(region: region)
    }

    func noteAccepted(_ region: GlassCaptureRegion) { output.noteAccepted(region) }

    func invalidate() { output.invalidate() }
    func stop() async {
        guard !stopped else { return }
        stopped = true
        output.invalidate()
        try? await stream.stopCapture()
    }

    enum CaptureError: Error { case applicationMissing }
}
