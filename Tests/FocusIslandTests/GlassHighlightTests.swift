import AppKit
import MetalKit
import Testing
@testable import FocusIsland

@Suite(.serialized) @MainActor
struct GlassHighlightTests {
    @Test func diagonalMatchesTransparentAndLocalizesBrightSegmentsAcrossSizes() {
        #expect(GlassHighlightGradient.start == CGPoint(x: 0, y: 1))
        #expect(GlassHighlightGradient.end == CGPoint(x: 1, y: 0))
        #expect(GlassHighlightGradient.weights == [1,0.7,0,0,0.5])
        for scale in [0.5,0.65,0.82,1.0] {
            let size = CGSize(width:344*scale,height:92*scale)
            for point in [CGPoint(x:0,y:0),CGPoint(x:0.2,y:0.7),CGPoint(x:0.5,y:0.5),CGPoint(x:1,y:1)] {
                let factor = GlassHighlightGradient.capturedFactor(
                    point:CGPoint(x:point.x*size.width,y:point.y*size.height),size:size)
                let reference = GlassHighlightGradient.capturedFactor(point:point,size:CGSize(width:1,height:1))
                #expect(abs(factor-reference) < 0.000001)
                #expect(factor >= 1-GlassHighlightGradient.capturedAdjustment && factor <= 1)
            }
        }
        #expect(GlassHighlightGradient.capturedFactor(point:.zero,size:.zero) == 1)
        #expect(GlassHighlightGradient.capturedFactor(point:CGPoint(x:CGFloat.nan,y:0),size:CGSize(width:344,height:92)) == 1)
    }

    @Test func rotatedLightingFavorsOppositeCornersWithoutLightingStraightSides() {
        let diagonal = sqrt(0.5)
        let upperLeft = GlassHighlightGradient.capturedFacing(normal:CGPoint(x:-diagonal,y:-diagonal))
        let upperRight = GlassHighlightGradient.capturedFacing(normal:CGPoint(x:diagonal,y:-diagonal))
        let lowerRight = GlassHighlightGradient.capturedFacing(normal:CGPoint(x:diagonal,y:diagonal))
        #expect(upperLeft > 0.9 && abs(upperLeft-lowerRight) < 0.000001)
        #expect(upperRight < 0.47)
        #expect(GlassHighlightGradient.capturedFacing(normal:CGPoint(x:1,y:0)) < 0.47)
        #expect(GlassHighlightGradient.capturedFacing(normal:CGPoint(x:-1,y:0)) < 0.47)
    }

    @Test func gpuGradientUsesTheSameAngleAndStopsAsActualNativeTransparentLayer() throws {
        let width = 344, height = 92
        let gradient = CAGradientLayer()
        gradient.frame = CGRect(x:0,y:0,width:width,height:height)
        gradient.startPoint = GlassHighlightGradient.start
        gradient.endPoint = GlassHighlightGradient.end
        gradient.locations = GlassHighlightGradient.locations
        gradient.colors = GlassHighlightGradient.weights.map { NSColor.white.withAlphaComponent($0).cgColor }
        let context = try #require(CGContext(data:nil,width:width,height:height,bitsPerComponent:8,
            bytesPerRow:width*4,space:CGColorSpaceCreateDeviceRGB(),
            bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue|CGBitmapInfo.byteOrder32Big.rawValue))
        gradient.render(in:context)
        let image = try #require(context.makeImage())
        let native = [UInt8](try #require(image.dataProvider?.data) as Data)
        let device = try #require(MTLCreateSystemDefaultDevice())
        let queue = try #require(device.makeCommandQueue())
        let probe = #"""
        kernel void testGradient(device float *output [[buffer(0)]],constant uint2 &size [[buffer(1)]],
                                uint2 id [[thread_position_in_grid]]) {
            if(any(id>=size)) return;
            output[id.y*size.x+id.x]=glassHighlightFactor(float2(id)+0.5,float2(size));
        }
        """#
        let library = try device.makeLibrary(source:CapturedGlassRenderer.shader+probe,options:nil)
        let pipeline = try device.makeComputePipelineState(function:try #require(library.makeFunction(name:"testGradient")))
        let buffer = try #require(device.makeBuffer(length:width*height*4,options:.storageModeShared))
        let command = try #require(queue.makeCommandBuffer())
        let encoder = try #require(command.makeComputeCommandEncoder())
        var size = SIMD2<UInt32>(UInt32(width),UInt32(height))
        encoder.setComputePipelineState(pipeline)
        encoder.setBuffer(buffer,offset:0,index:0)
        encoder.setBytes(&size,length:MemoryLayout<SIMD2<UInt32>>.stride,index:1)
        encoder.dispatchThreads(MTLSize(width:width,height:height,depth:1),threadsPerThreadgroup:MTLSize(width:8,height:8,depth:1))
        encoder.endEncoding();command.commit();command.waitUntilCompleted()
        #expect(command.status == .completed)
        let factors = UnsafeBufferPointer(start:buffer.contents().assumingMemoryBound(to:Float.self),count:width*height)
        let maximumError = factors.enumerated().map { i,factor in
            let expected = 1 + GlassHighlightGradient.capturedAdjustment * (Double(native[i*4+3])/255-1)
            return abs(Double(factor)-expected)
        }.max() ?? 0
        // CA's 8-bit raster/interpolation differs by up to two channel levels;
        // compare the unattenuated gradient within that quantization budget.
        #expect(maximumError <= 2.0/255)
        #expect(factors.enumerated().allSatisfy { i,factor in
            let expected = GlassHighlightGradient.capturedFactor(
                point:CGPoint(x:Double(i%width)+0.5,y:Double(i/width)+0.5),
                size:CGSize(width:width,height:height))
            return abs(Double(factor)-expected) < 0.000001
        })
    }
}
