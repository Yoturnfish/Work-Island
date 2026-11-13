import SwiftUI

/// A small glyph-local backing, not a shadow on controls or the whole window.
/// Does not change layout, foreground opacity or hit testing.
struct GlassTextRenderer: TextRenderer {
    let enabled: Bool
    var darkForeground = false
    static let opacity = 0.22
    static let radius: CGFloat = 6

    static func shouldEnable(isGlass: Bool, isExpanded: Bool, isOnboarding: Bool) -> Bool {
        isGlass && !isExpanded && !isOnboarding
    }

    func draw(layout: Text.Layout, in context: inout GraphicsContext) {
        var drawing = context
        if enabled {
            drawing.addFilter(.shadow(color: (darkForeground ? Color.white : Color.black).opacity(Self.opacity),
                                      radius: Self.radius, x: 0, y: 0))
        }
        for line in layout { drawing.draw(line) }
    }
}
