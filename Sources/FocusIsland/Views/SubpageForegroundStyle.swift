import SwiftUI

private struct WhiteSubpageForegroundKey: EnvironmentKey {
    static let defaultValue = false
}
private struct DarkGlassForegroundKey: EnvironmentKey {
    static let defaultValue = false
}

extension EnvironmentValues {
    var darkGlassForeground: Bool {
        get { self[DarkGlassForegroundKey.self] }
        set { self[DarkGlassForegroundKey.self] = newValue }
    }
    var whiteSubpageForeground: Bool {
        get { self[WhiteSubpageForegroundKey.self] }
        set { self[WhiteSubpageForegroundKey.self] = newValue }
    }
}

/// Neutral labels/icons only. Semantic theme, success and warning colors keep
/// their own styles; Black retains its existing hierarchy and press feedback.
struct SubpageForegroundStyle: ShapeStyle {
    let fallback: Color
    var glassOpacity: Double = 1

    func resolve(in environment: EnvironmentValues) -> Color {
        if environment.darkGlassForeground { return .black.opacity(glassOpacity) }
        return environment.whiteSubpageForeground ? .white.opacity(glassOpacity) : fallback
    }

    static let primary = Self(fallback: FocusDesign.ColorToken.textPrimary)
    static let secondary = Self(fallback: FocusDesign.ColorToken.textSecondary)
    static let muted = Self(fallback: FocusDesign.ColorToken.textMuted)
}
