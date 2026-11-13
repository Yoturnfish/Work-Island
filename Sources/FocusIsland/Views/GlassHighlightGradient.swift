import AppKit

/// The explicit Transparent theme's existing diagonal, shared with a visible
/// brightness adjustment of the captured glass rim. Nothing moves over time.
enum GlassHighlightGradient {
    static let start = CGPoint(x: 0, y: 1)
    static let end = CGPoint(x: 1, y: 0)
    static let locations: [NSNumber] = [0, 0.18, 0.42, 0.68, 1]
    static let weights = [1.0, 0.7, 0, 0, 0.5]
    static let capturedAdjustment = 1.0
    // A fixed 20-degree optical axis favors upper-left / lower-right curves
    // but remains below the lighting threshold on the straight side walls.
    static func capturedFacing(normal: CGPoint) -> Double {
        abs(Double(normal.x) * 0.34202014 + Double(normal.y) * 0.93969262)
    }

    /// In the Metal viewport y is top-down, unlike the native layer's y axis.
    /// Core Animation projects this normalized diagonal before interpolation.
    static func capturedFactor(point: CGPoint, size: CGSize) -> Double {
        guard size.width.isFinite, size.height.isFinite, size.width > 0, size.height > 0,
              point.x.isFinite, point.y.isFinite else { return 1 }
        let t = min(max(Double(point.x / size.width + point.y / size.height) * 0.5, 0), 1)
        for i in 1..<locations.count where t <= locations[i].doubleValue {
            let lower = locations[i-1].doubleValue, upper = locations[i].doubleValue
            let value = weights[i-1] + (weights[i]-weights[i-1]) * (t-lower) / (upper-lower)
            return 1 + capturedAdjustment * (value - 1)
        }
        return 1 + capturedAdjustment * (weights.last! - 1)
    }

    static let metalFunction = #"""
    float glassHighlightFactor(float2 p, float2 size) {
        float t = clamp(dot(p/max(size,float2(0.000001)),float2(0.5)),0.0,1.0);
        float weight;
        if(t<0.18) weight=mix(1.0,0.7,t/0.18);
        else if(t<0.42) weight=mix(0.7,0.0,(t-0.18)/0.24);
        else if(t<0.68) weight=0.0;
        else weight=mix(0.0,0.5,(t-0.68)/0.32);
        // Let the diagonal's quiet segments fully fade instead of clipping
        // back to white when additive light meets a bright desktop.
        return weight;
    }
    float glassHighlightFacing(float2 normal) {
        return abs(dot(normal,float2(0.34202014,0.93969262)));
    }
    """#
}
