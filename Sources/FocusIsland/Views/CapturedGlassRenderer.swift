import AppKit
@preconcurrency import MetalKit
import CoreVideo
@preconcurrency import MetalPerformanceShaders

/// Same optical edge profile as the approved lab, now sampling the bounded
/// desktop texture instead of a generated scene. No continuous MTKView loop.
@MainActor
final class CapturedGlassRenderer: NSObject, MTKViewDelegate {
    struct Uniforms {
        var viewport: SIMD4<Float> // logical width, height, backing scale, radius
        var capture: SIMD4<Float> // offset x/y, source width/height in points
    }
    let device: MTLDevice
    private let queue: MTLCommandQueue
    private let pipeline: MTLRenderPipelineState
    private let luminancePipeline: MTLComputePipelineState
    private let cache: CVMetalTextureCache
    private let gaussianBlur: GlassGaussianBlur
    private var frame: GlassPixelFrame?
    private var texture: CVMetalTexture?
    private var region: GlassCaptureRegion?
    private var onPresentation: (@MainActor @Sendable () -> Void)?
    var radius: CGFloat = 0
    var blurRadius = GlassBlurMetrics.defaultRadius
    var onLuminance: (@MainActor @Sendable (Double) -> Void)?
    private var lastLuminanceSample: TimeInterval = -.infinity
    var frameInterval: TimeInterval = 1 / 60
    private var renderCadence = GlassRenderCadence()
    private let inFlight = DispatchSemaphore(value: 3)
    private(set) var submittedFrames = 0
    let statistics = GlassGPUStatistics()

    init?(material: Void = ()) {
        guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else { return nil }
        self.device = device
        self.queue = queue
        gaussianBlur = GlassGaussianBlur(device: device)
        var cache: CVMetalTextureCache?
        guard CVMetalTextureCacheCreate(nil, nil, device, nil, &cache) == kCVReturnSuccess,
              let cache else { return nil }
        self.cache = cache
        do {
            let library = try device.makeLibrary(source: Self.shader, options: nil)
            let descriptor = MTLRenderPipelineDescriptor()
            descriptor.vertexFunction = library.makeFunction(name: "glassVertex")
            descriptor.fragmentFunction = library.makeFunction(name: "glassFragment")
            descriptor.colorAttachments[0].pixelFormat = .bgra8Unorm
            pipeline = try device.makeRenderPipelineState(descriptor: descriptor)
            guard let luminanceFunction = library.makeFunction(name: "glassLuminance") else { return nil }
            luminancePipeline = try device.makeComputePipelineState(function: luminanceFunction)
        } catch { return nil }
        super.init()
    }

    func accept(_ pixels: CVPixelBuffer, region: GlassCaptureRegion, in view: MTKView,
                onPresentation: (@MainActor @Sendable () -> Void)? = nil) -> Bool {
        var texture: CVMetalTexture?
        guard CVMetalTextureCacheCreateTextureFromImage(nil, cache, pixels, nil, .bgra8Unorm,
                    CVPixelBufferGetWidth(pixels), CVPixelBufferGetHeight(pixels), 0, &texture) == kCVReturnSuccess,
              let texture else { return false }
        self.texture = texture
        frame = GlassPixelFrame(pixels)
        self.region = region
        self.onPresentation = onPresentation
        view.draw()
        return true
    }

    func clear() {
        frame = nil
        texture = nil
        region = nil
        onPresentation = nil
        onLuminance = nil
        lastLuminanceSample = -.infinity
        renderCadence.reset()
        CVMetalTextureCacheFlush(cache, 0)
        gaussianBlur.clear()
    }

    func reframe(to desired: GlassCaptureRegion, in view: MTKView) -> Bool {
        guard let region else { return false }
        let mapped = region.reframed(to: desired)
        guard mapped.containsVisibleViewport(view.bounds.size) else {
            statistics.recordCoverageMiss(); return false
        }
        self.region = mapped
        view.draw()
        return true
    }

    func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) {}
    func redraw(in view: MTKView) {
        renderCadence.reset()
        view.draw()
    }
    func draw(in view: MTKView) {
        let now = CACurrentMediaTime()
        guard renderCadence.isDue(at: now, interval: frameInterval) else {
            statistics.recordCadenceSkip(); return
        }
        guard let frame, let cvTexture = texture, let texture = CVMetalTextureGetTexture(cvTexture), let region,
              view.bounds.width > 0, view.bounds.height > 0,
              inFlight.wait(timeout: .now()) == .success else { return }
        let drawableRequested = CACurrentMediaTime()
        guard let pass = view.currentRenderPassDescriptor, let drawable = view.currentDrawable,
              let command = queue.makeCommandBuffer() else {
            inFlight.signal(); return
        }
        let drawableWait = CACurrentMediaTime() - drawableRequested
        guard
              let background = gaussianBlur.encode(source: texture, command: command, radius: blurRadius, scale: region.scale),
              let encoder = command.makeRenderCommandEncoder(descriptor: pass) else {
            inFlight.signal(); return
        }
        var uniforms = Uniforms(
            viewport: SIMD4(Float(view.bounds.width), Float(view.bounds.height), Float(view.drawableSize.width / view.bounds.width), Float(radius)),
            capture: SIMD4(Float(region.offset.x), Float(region.offset.y), Float(region.source.width), Float(region.source.height)))
        encoder.setRenderPipelineState(pipeline)
        encoder.setFragmentBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 0)
        encoder.setFragmentTexture(background, index: 0)
        encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
        encoder.endEncoding()
        // Read only one scalar at most four times per second. Pixel sampling
        // stays on the GPU; SwiftUI is notified only when contrast changes.
        var measurement: GlassLuminanceMeasurement?
        if onLuminance != nil, now - lastLuminanceSample >= 0.25,
           let buffer = device.makeBuffer(length: MemoryLayout<Float>.stride, options: .storageModeShared),
           let compute = command.makeComputeCommandEncoder() {
            compute.setComputePipelineState(luminancePipeline)
            compute.setTexture(texture, index: 0)
            compute.setBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 0)
            compute.setBuffer(buffer, offset: 0, index: 1)
            compute.dispatchThreads(MTLSize(width: 1, height: 1, depth: 1), threadsPerThreadgroup: MTLSize(width: 1, height: 1, depth: 1))
            compute.endEncoding()
            measurement = GlassLuminanceMeasurement(buffer: buffer)
            lastLuminanceSample = now
        }
        if !view.presentsWithTransaction { command.present(drawable) }
        let semaphore = inFlight
        // Keep both IOSurface and CVMetalTexture alive until sampling completes.
        let retained = GlassTextureLifetime(frame: frame, texture: cvTexture)
        let presented = onPresentation
        let statistics = statistics
        let measured = measurement
        let luminanceCallback = onLuminance
        onPresentation = nil
        drawable.addPresentedHandler { drawable in
            statistics.recordDisplay(at: drawable.presentedTime, submittedAt: now)
        }
        command.addCompletedHandler { command in
            statistics.record(command)
            withExtendedLifetime(retained) { _ = semaphore.signal() }
            if command.status == .completed, measured != nil || presented != nil {
                let luminance = measured?.value
                Task { @MainActor in
                    if let luminance { luminanceCallback?(luminance) }
                    presented?()
                }
            }
        }
        command.commit()
        var schedulingWait = 0.0
        if view.presentsWithTransaction {
            // Apple's transaction presentation sequence: schedule, then present
            // on the current CA transaction. Never wait for GPU completion.
            let schedulingStarted = CACurrentMediaTime()
            command.waitUntilScheduled()
            schedulingWait = CACurrentMediaTime() - schedulingStarted
            drawable.present()
        }
        statistics.recordSubmission(drawableWait: drawableWait, schedulingWait: schedulingWait)
        renderCadence.submitted(at: now, interval: frameInterval)
        submittedFrames += 1
    }

    // Adapted from Glass HQ liquid-glass 9f5bfc7, MIT. Full attribution/license
    // are shipped in THIRD_PARTY_NOTICES.md. All sampling remains on the GPU.
    static let shader = #"""
    #include <metal_stdlib>
    using namespace metal;
    struct Uniforms { float4 viewport; float4 capture; };
    \#(GlassHighlightGradient.metalFunction)
    kernel void glassLuminance(constant Uniforms &u [[buffer(0)]],
            device float &output [[buffer(1)]], texture2d<float> background [[texture(0)]]) {
        constexpr sampler s(coord::normalized, address::clamp_to_edge, filter::linear);
        float sum = 0;
        // Restrict the grid to visible captured pixels when partially offscreen.
        float2 lo = max(u.capture.xy, float2(0));
        float2 hi = min(u.capture.xy + u.viewport.xy, u.capture.zw);
        for (uint y=0; y<4; y++) for (uint x=0; x<4; x++) {
            float2 uv = mix(lo,hi,(float2(x,y)+0.5)/4.0)/u.capture.zw;
            sum += dot(background.sample(s,uv).rgb,float3(0.2126,0.7152,0.0722));
        }
        output = sum/16.0;
    }
    vertex float4 glassVertex(uint id [[vertex_id]]) {
        const float2 v[] = {float2(-1,-1),float2(3,-1),float2(-1,3)};
        return float4(v[id],0,1);
    }
    float distanceToGlass(float2 p, constant Uniforms &u) {
        float radius = min(u.viewport.w, min(u.viewport.x,u.viewport.y)*0.5);
        float2 q = abs(p-u.viewport.xy*0.5)-u.viewport.xy*0.5+radius;
        return length(max(q,0.0))+min(max(q.x,q.y),0.0)-radius;
    }
    fragment float4 glassFragment(float4 position [[position]],
                constant Uniforms &u [[buffer(0)]], texture2d<float> background [[texture(0)]]) {
        constexpr sampler sample(coord::normalized, address::clamp_to_edge, filter::linear);
        float2 p = position.xy/u.viewport.z;
        float d = distanceToGlass(p,u);
        if(d>0.7) return float4(0);
        float2 n = normalize(float2(
            distanceToGlass(p+float2(0.4,0),u)-distanceToGlass(p-float2(0.4,0),u),
            distanceToGlass(p+float2(0,0.4),u)-distanceToGlass(p-float2(0,0.4),u))+float2(0.000001));
        // Keep the approved lab's physical optical depth and 24pt displacement.
        // HUD layout/corner radius stays unchanged; only the glass is refracted.
        float depth = clamp(-d*0.05,0.0,1.0);
        float edge = 1.0-clamp(sqrt((2.0-depth)*depth),0.0,1.0);
        float2 displaced = p-n*edge*24.0;
        float2 uv = (displaced+u.capture.xy)/u.capture.zw;
        float3 glass = background.sample(sample,uv).rgb;
        glass = mix(glass,float3(0.10,0.19,0.22),0.035);
        float directional = pow(clamp((glassHighlightFacing(n)-0.47)/0.53,0.0,1.0),1.25);
        float rim = exp(-abs(d+0.65)*1.8);
        float light = directional*rim*0.69 + directional*0.13*exp(min(d,0.0)/5.0);
        light *= glassHighlightFactor(p,u.viewport.xy);
        float side = pow(abs(n.x),5.0)*rim*0.18;
        // A localized inner reflection keeps the curved edge legible on bright
        // desktops, where an additive white highlight alone disappears.
        float reflection = 0.14*exp(-abs(d+1.8)*0.45)*(0.35+0.65*abs(n.x));
        glass = glass*(1.0-side-reflection)+float3(light);
        float coverage = clamp(0.5-d*u.viewport.z,0.0,1.0);
        return float4(clamp(glass,0.0,1.0)*coverage,coverage);
    }
    """#
}

/// Apple's GPU Gaussian filter, encoded before optical sampling on the same
/// serial queue. No CPU readback; reuse the bounded scratch texture and kernel.
@MainActor final class GlassGaussianBlur {
    private let device: MTLDevice
    private var kernel: MPSImageGaussianBlur?
    private var sigma: Float = 0
    private var scratch: MTLTexture?
    init(device: MTLDevice) { self.device = device }
    func clear() { scratch = nil; kernel = nil; sigma = 0 }
    func encode(source: MTLTexture, command: MTLCommandBuffer, radius: Double, scale: CGFloat) -> MTLTexture? {
        let requested = Float(GlassBlurMetrics.normalized(radius) * scale)
        guard requested > 0 else { return source }
        if kernel == nil || requested != sigma {
            let filter = MPSImageGaussianBlur(device: device, sigma: requested)
            filter.edgeMode = .clamp
            kernel = filter; sigma = requested
        }
        if scratch?.width != source.width || scratch?.height != source.height {
            let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm,
                width: source.width, height: source.height, mipmapped: false)
            descriptor.storageMode = .private
            descriptor.usage = [.shaderRead, .shaderWrite]
            scratch = device.makeTexture(descriptor: descriptor)
        }
        guard let scratch, let kernel else { return nil }
        kernel.encode(commandBuffer: command, sourceTexture: source, destinationTexture: scratch)
        return scratch
    }
}

final class GlassGPUStatistics: @unchecked Sendable {
    private let lock = NSLock()
    private var frames = 0
    private var seconds = 0.0
    private var presentation = GlassPresentationSnapshot()
    func recordSubmission(drawableWait: Double, schedulingWait: Double) {
        lock.lock(); defer { lock.unlock() }
        presentation.recordSubmission(drawableWait: drawableWait, schedulingWait: schedulingWait)
    }
    func recordDisplay(at time: Double, submittedAt: Double) {
        lock.lock(); defer { lock.unlock() }
        presentation.recordDisplay(at: time, submittedAt: submittedAt)
    }
    func recordCadenceSkip() {
        lock.lock(); defer { lock.unlock() }
        presentation.recordCadenceSkip()
    }
    func recordCoverageMiss() {
        lock.lock(); defer { lock.unlock() }
        presentation.recordCoverageMiss()
    }
    func presentationSnapshot() -> GlassPresentationSnapshot {
        lock.lock(); defer { lock.unlock() }
        return presentation
    }
    func record(_ command: MTLCommandBuffer) {
        guard command.status == .completed else { return }
        let elapsed = command.gpuEndTime - command.gpuStartTime
        lock.lock(); defer { lock.unlock() }
        frames += 1
        if elapsed.isFinite, elapsed > 0 { seconds += elapsed }
    }
    func snapshot() -> (frames: Int, seconds: Double) {
        lock.lock(); defer { lock.unlock() }
        return (frames, seconds)
    }
}

private final class GlassLuminanceMeasurement: @unchecked Sendable {
    let buffer: MTLBuffer
    init(buffer: MTLBuffer) { self.buffer = buffer }
    var value: Double { Double(buffer.contents().load(as: Float.self)) }
}

private final class GlassTextureLifetime: @unchecked Sendable {
    let frame: GlassPixelFrame
    let texture: CVMetalTexture
    init(frame: GlassPixelFrame, texture: CVMetalTexture) { self.frame = frame; self.texture = texture }
}

final class CapturedGlassView: MTKView {
    override init(frame: CGRect, device: MTLDevice?) {
        super.init(frame: frame, device: device)
        isPaused = true
        enableSetNeedsDisplay = false
        presentsWithTransaction = true
    }
    required init(coder: NSCoder) { fatalError("init(coder:) is unsupported") }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}
