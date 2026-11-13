import SwiftUI

enum ThemeAccent: String, Codable, CaseIterable, Identifiable {
    case green, blue, yellow, cyan, pink, orange, purple
    var id: String { rawValue }
    var color: Color {
        switch self {
        case .blue: Color(red: 0.2, green: 0.55, blue: 1)
        case .purple: .purple
        case .cyan: .cyan
        case .pink: .pink
        case .orange: Color(red: 1, green: 0.60, blue: 0.76) // Keep persisted orange slot; display soft pink.
        case .green: FocusDesign.ColorToken.accent
        case .yellow: .yellow
        }
    }
    var title: String {
        L10n.text(["blue": "蓝色", "purple": "紫色", "cyan": "青色", "pink": "粉色", "orange": "浅粉色", "green": "绿色", "yellow": "黄色"][rawValue]!)
    }
}

struct TrackingTheme: Codable, Identifiable {
    var id = UUID()
    var name: String
    var accent: ThemeAccent = .blue
    var applications: [TrackedApplication] = []
    var websites: [TrackedWebsite] = []
}

enum IslandProgressStyle: String, Codable, CaseIterable, Identifiable {
    case matrix, waveform
    var id: String { rawValue }
    var title: String {
        switch self {
        case .matrix: L10n.text("矩形矩阵")
        case .waveform: L10n.text("波形进度条")
        }
    }
}
