import AppKit
import MetalKit
import Testing
@testable import FocusIsland

/// Generated pixels only: these tests never capture the desktop or request access.
@Suite(.serialized) @MainActor
struct GlassMaterialTests {
    @Test func smallerReusedCropPreservesOpticalPixelsIncludingMaximumBlur() throws {
        for blur: Float in [0,1.5,6] {
            for origin in [SIMD2<Int>(0,0), SIMD2(10,0), SIMD2(-10,-10)] {
                let reference = try render(color:SIMD3(128,128,128),blur:blur,padding:28,
                    physicalOrigin:origin,viewportOrigin:origin,worldPattern:true)
                let cached = try render(color:SIMD3(128,128,128),blur:blur,padding:18,
                    physicalOrigin:.zero,viewportOrigin:origin,worldPattern:true)
                #expect(zip(reference,cached).allSatisfy { abs(Int($0)-Int($1)) <= 1 })
            }
        }
    }

    @Test func diagonalRimPreservesBlurConstantColorAndMaskOnWhiteBlackAndNavy() throws {
        for color in [SIMD3<UInt8>(255,255,255), SIMD3(0,0,0), SIMD3(44,28,15)] {
            let plain = try render(color: color, blur: 0)
            let blurred = try render(color: color, blur: 6)
            let original = try render(color: color, blur: 0, shader: originalRimShader)
            #expect(plain == blurred) // blur preserves constant backgrounds
            #expect(plain[3] == 0)
            let center = pixel(plain, x: 344, y: 92)
            #expect(center[3] == 255)
            // Only additive top/bottom lighting changes. Alpha, center, side
            // shadow and refraction remain those of the restored build 83.
            #expect(pixel(plain,x:344,y:92) == pixel(original,x:344,y:92))
            #expect(pixel(plain,x:1,y:92) == pixel(plain,x:686,y:92))
            #expect(pixel(plain,x:1,y:92) == pixel(original,x:1,y:92))
            #expect(stride(from:3,to:plain.count,by:4).allSatisfy { plain[$0] == original[$0] })
            #expect(pixel(plain,x:120,y:1)[2] > pixel(plain,x:566,y:1)[2])
            #expect(pixel(plain,x:566,y:182)[2] > pixel(plain,x:120,y:182)[2])
        }
    }

    @Test func highlightPlacementDifferenceIsVisibleEvenOnWhiteWithoutMovingSideOrCenter() throws {
        for (name,color) in [("white",SIMD3<UInt8>(255,255,255)),("navy",SIMD3(44,28,15)),("black",SIMD3(0,0,0))] {
            let current = try render(color:color,blur:6)
            let previous = try render(color:color,blur:6,shader:previousRimShader)
            if name == "white" {
                #expect(Int(pixel(previous,x:566,y:1)[2])-Int(pixel(current,x:566,y:1)[2]) >= 12)
                #expect(Int(pixel(previous,x:120,y:182)[2])-Int(pixel(current,x:120,y:182)[2]) >= 12)
            }
            #expect(pixel(current,x:344,y:92) == pixel(previous,x:344,y:92))
            #expect(pixel(current,x:1,y:92) == pixel(previous,x:1,y:92))
            try exportComparison(previous:previous,current:current,color:color,name:name)
        }
    }

    @Test func blurReducesBackgroundStripeContrastWithoutChangingMask() throws {
        let clear = try render(color: SIMD3(128,128,128), blur: 0, stripes: true)
        let blurred = try render(color: SIMD3(128,128,128), blur: 1.5, stripes: true)
        func variation(_ image: [UInt8]) -> Int {
            (200..<480).reduce(0) { sum, x in
                sum + abs(Int(pixel(image,x:x,y:92)[2])-Int(pixel(image,x:x+1,y:92)[2]))
            }
        }
        #expect(variation(blurred) < variation(clear) / 2)
        for index in stride(from: 3, to: clear.count, by: 4) { #expect(clear[index] == blurred[index]) }
    }

    @Test func luminanceKernelReadsOnlyOneScalarAndClassifiesGeneratedWhiteAndNavy() throws {
        let renderer = try #require(CapturedGlassRenderer())
        let device = renderer.device
        let library = try device.makeLibrary(source: CapturedGlassRenderer.shader, options: nil)
        let pipeline = try device.makeComputePipelineState(function: try #require(library.makeFunction(name:"glassLuminance")))
        let queue = try #require(device.makeCommandQueue())
        for (value, expected) in [(UInt8(255),1.0), (UInt8(32),32.0/255)] {
            let desc = MTLTextureDescriptor.texture2DDescriptor(pixelFormat:.bgra8Unorm, width:16,height:16,mipmapped:false)
            desc.storageMode = .shared
            let texture = try #require(device.makeTexture(descriptor:desc))
            let pixels = [UInt8](repeating:value,count:16*16*4)
            pixels.withUnsafeBytes { texture.replace(region:MTLRegionMake2D(0,0,16,16),mipmapLevel:0,withBytes:$0.baseAddress!,bytesPerRow:64) }
            let buffer = try #require(device.makeBuffer(length:4,options:.storageModeShared))
            let command = try #require(queue.makeCommandBuffer())
            let encoder = try #require(command.makeComputeCommandEncoder())
            var uniforms = CapturedGlassRenderer.Uniforms(viewport:SIMD4(344,92,2,20),capture:SIMD4(-10,-8,16,16))
            encoder.setComputePipelineState(pipeline)
            encoder.setTexture(texture,index:0)
            encoder.setBytes(&uniforms,length:MemoryLayout<CapturedGlassRenderer.Uniforms>.stride,index:0)
            encoder.setBuffer(buffer,offset:0,index:1)
            encoder.dispatchThreads(MTLSize(width:1,height:1,depth:1),threadsPerThreadgroup:MTLSize(width:1,height:1,depth:1))
            encoder.endEncoding(); command.commit(); command.waitUntilCompleted()
            #expect(command.status == .completed)
            #expect(abs(Double(buffer.contents().load(as:Float.self))-expected) < 0.001)
        }
    }

    private func pixel(_ image:[UInt8],x:Int,y:Int)->[UInt8] {
        Array(image[((y*688+x)*4)..<((y*688+x)*4+4)])
    }
    private var originalRimShader: String {
        CapturedGlassRenderer.shader.replacingOccurrences(of: "light *= glassHighlightFactor(p,u.viewport.xy);", with: "")
            .replacingOccurrences(of:"glassHighlightFacing(n)",with:"abs(n.y)")
    }
    private var previousRimShader: String {
        CapturedGlassRenderer.shader.replacingOccurrences(of:"glassHighlightFacing(n)",with:"abs(n.y)")
            .replacingOccurrences(of:"return weight;",with:"return mix(1.0,weight,0.35);")
    }
    /// Optional generated-color fixture, not a desktop screenshot.
    private func exportComparison(previous:[UInt8],current:[UInt8],color:SIMD3<UInt8>,name:String) throws {
        guard let path = ProcessInfo.processInfo.environment["WORK_ISLAND_RIM_PREVIEW_DIR"] else { return }
        let directory = URL(fileURLWithPath:path,isDirectory:true)
        try FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
        let width = 1392, height = 184
        var pixels = [UInt8](repeating:255,count:width*height*4)
        for y in 0..<height { for x in 0..<width {
            let index = (y*width+x)*4
            pixels[index] = color.x;pixels[index+1] = color.y;pixels[index+2] = color.z
            let source: [UInt8], column: Int
            if x < 688 { source=previous;column=x }
            else if x >= 704 { source=current;column=x-704 }
            else { continue }
            let at = (y*688+column)*4
            let alpha = Double(source[at+3])/255
            for channel in 0..<3 {
                pixels[index+channel] = UInt8(min(255,Double(source[at+channel])+Double(pixels[index+channel])*(1-alpha)).rounded())
            }
        }}
        let provider = CGDataProvider(data:Data(pixels) as CFData)!
        let image = CGImage(width:width,height:height,bitsPerComponent:8,bitsPerPixel:32,bytesPerRow:width*4,
            space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGBitmapInfo(rawValue:CGImageAlphaInfo.premultipliedFirst.rawValue)
                .union(.byteOrder32Little),provider:provider,decode:nil,shouldInterpolate:false,intent:.defaultIntent)!
        let bitmap = NSBitmapImageRep(cgImage:image)
        try bitmap.representation(using:.png,properties:[:])!.write(to:directory.appendingPathComponent("\(name)-85-left-86-right.png"))
    }
    private func render(color:SIMD3<UInt8>,blur:Float,stripes:Bool=false,padding:Int=28,
                        physicalOrigin:SIMD2<Int> = .zero, viewportOrigin:SIMD2<Int> = .zero,
                        worldPattern:Bool=false, shader: String? = nil)throws->[UInt8] {
        let renderer = try #require(CapturedGlassRenderer())
        let device = renderer.device
        let library = try device.makeLibrary(source:shader ?? CapturedGlassRenderer.shader,options:nil)
        let desc = MTLRenderPipelineDescriptor()
        desc.vertexFunction = library.makeFunction(name:"glassVertex")
        desc.fragmentFunction = library.makeFunction(name:"glassFragment")
        desc.colorAttachments[0].pixelFormat = .bgra8Unorm
        let pipeline = try device.makeRenderPipelineState(descriptor:desc)
        let sourceWidth = (344+padding*2)*2, sourceHeight = (92+padding*2)*2
        let sourceDesc = MTLTextureDescriptor.texture2DDescriptor(pixelFormat:.bgra8Unorm,width:sourceWidth,height:sourceHeight,mipmapped:false)
        sourceDesc.storageMode = .shared
        let source = try #require(device.makeTexture(descriptor:sourceDesc))
        var pixels = [UInt8](repeating:255,count:sourceWidth*sourceHeight*4)
        for y in 0..<sourceHeight { for x in 0..<sourceWidth {
            let i = (y*sourceWidth+x)*4
            pixels[i] = color.x; pixels[i+1] = color.y
            pixels[i+2] = stripes ? (x%4<2 ? 0:255) : color.z
            if worldPattern {
                let worldX = x+(physicalOrigin.x-padding)*2
                let worldY = y+(physicalOrigin.y-padding)*2
                pixels[i] = (worldY & 15)<8 ? 40:210
                pixels[i+1] = (worldX & 15)<8 ? 30:220
                pixels[i+2] = (worldX & 3)<2 ? 0:255
            }
        }}
        pixels.withUnsafeBytes { source.replace(region:MTLRegionMake2D(0,0,sourceWidth,sourceHeight),mipmapLevel:0,withBytes:$0.baseAddress!,bytesPerRow:sourceWidth*4) }
        let targetDesc = MTLTextureDescriptor.texture2DDescriptor(pixelFormat:.bgra8Unorm,width:688,height:184,mipmapped:false)
        targetDesc.storageMode = .shared; targetDesc.usage = .renderTarget
        let target = try #require(device.makeTexture(descriptor:targetDesc))
        let pass = MTLRenderPassDescriptor()
        pass.colorAttachments[0].texture = target
        pass.colorAttachments[0].loadAction = .clear; pass.colorAttachments[0].storeAction = .store
        let queue = try #require(device.makeCommandQueue())
        let command = try #require(queue.makeCommandBuffer())
        let gaussian = GlassGaussianBlur(device:device)
        let background = try #require(gaussian.encode(source:source,command:command,radius:Double(blur),scale:2))
        let encoder = try #require(command.makeRenderCommandEncoder(descriptor:pass))
        var uniforms = CapturedGlassRenderer.Uniforms(viewport:SIMD4(344,92,2,20),
            capture:SIMD4(Float(padding+viewportOrigin.x-physicalOrigin.x),Float(padding+viewportOrigin.y-physicalOrigin.y),
                          Float(sourceWidth)/2,Float(sourceHeight)/2))
        encoder.setRenderPipelineState(pipeline)
        encoder.setFragmentBytes(&uniforms,length:MemoryLayout<CapturedGlassRenderer.Uniforms>.stride,index:0)
        encoder.setFragmentTexture(background,index:0)
        encoder.drawPrimitives(type:.triangle,vertexStart:0,vertexCount:3)
        encoder.endEncoding(); command.commit(); command.waitUntilCompleted()
        #expect(command.status == .completed)
        var output = [UInt8](repeating:0,count:688*184*4)
        output.withUnsafeMutableBytes { target.getBytes($0.baseAddress!,bytesPerRow:688*4,from:MTLRegionMake2D(0,0,688,184),mipmapLevel:0) }
        return output
    }
}
