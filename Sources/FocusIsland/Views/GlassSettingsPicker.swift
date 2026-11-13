import AppKit
import SwiftUI

/// One native popup owns its entire background, label, arrows and click area.
/// Only the cell drawing changes; menu selection and keyboard behavior stay native.
struct GlassSettingsPicker: NSViewRepresentable {
    @Environment(\.whiteSubpageForeground) private var whiteForeground
    @Environment(\.darkGlassForeground) private var darkForeground
    let title: String
    let labels: [String]
    let selectedIndex: Int
    let onSelect: (Int) -> Void

    func makeCoordinator() -> Coordinator { Coordinator(onSelect: onSelect) }
    func makeNSView(context: Context) -> GlassSettingsPickerHostView {
        let host = GlassSettingsPickerHostView()
        let button = host.button
        button.focusRingType = .none
        button.autoenablesItems = false
        button.target = context.coordinator
        button.action = #selector(Coordinator.select(_:))
        button.setContentHuggingPriority(.defaultLow, for: .horizontal)
        return host
    }
    func updateNSView(_ host: GlassSettingsPickerHostView, context: Context) {
        let button = host.button
        var appearanceChanged = false
        if let cell = button.cell as? GlassSettingsPickerCell,
           cell.whiteForeground != whiteForeground || cell.darkForeground != darkForeground {
            cell.whiteForeground = whiteForeground
            cell.darkForeground = darkForeground
            appearanceChanged = true
        }
        context.coordinator.onSelect = onSelect
        if button.itemTitles != labels {
            button.removeAllItems()
            button.addItems(withTitles: labels)
            appearanceChanged = true
        }
        if labels.indices.contains(selectedIndex),
           button.indexOfSelectedItem != selectedIndex {
            button.selectItem(at: selectedIndex)
            appearanceChanged = true
        }
        let desiredFont = NSFont.systemFont(ofSize: FocusDesign.Typography.settingsValue)
        if button.font?.isEqual(desiredFont) != true {
            button.font = desiredFont
            appearanceChanged = true
        }
        button.setAccessibilityLabel(title)
        if appearanceChanged { button.needsDisplay = true }
    }
    @MainActor final class Coordinator: NSObject {
        var onSelect: (Int) -> Void
        init(onSelect: @escaping (Int) -> Void) { self.onSelect = onSelect }
        @objc func select(_ sender: NSPopUpButton) { onSelect(sender.indexOfSelectedItem) }
    }
}

/// SwiftUI sizes this neutral host instead of the native popup's alignment
/// rect. The button frame is then pinned to the exact host bounds so every
/// label, appearance and language has identical visible and clickable edges.
final class GlassSettingsPickerHostView: NSView {
    let button = NSPopUpButton(frame: .zero, pullsDown: false)

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        button.cell = GlassSettingsPickerCell(textCell: "", pullsDown: false)
        button.autoresizingMask = [.width, .height]
        addSubview(button)
    }

    convenience init() { self.init(frame: .zero) }
    @available(*, unavailable) required init?(coder: NSCoder) { nil }

    override var intrinsicContentSize: NSSize {
        NSSize(width: FocusDesign.Size.settingsPickerWidth,
               height: FocusDesign.Size.settingsPickerHeight)
    }

    override func layout() {
        super.layout()
        button.frame = bounds
    }
}

final class GlassSettingsPickerCell: NSPopUpButtonCell {
    var whiteForeground = false
    var darkForeground = false

    static func textFrame(in frame: NSRect, textHeight: CGFloat) -> NSRect {
        NSRect(
            x: frame.minX + FocusDesign.Size.settingsPickerTextInset,
            y: frame.midY - textHeight / 2,
            width: max(0, frame.width - FocusDesign.Size.settingsPickerTextInset
                - FocusDesign.Size.settingsPickerTextTrailingClearance),
            height: textHeight
        )
    }

    override func draw(withFrame frame: NSRect, in controlView: NSView) {
        let foreground = darkForeground ? NSColor.black : NSColor.white
        foreground.withAlphaComponent(isHighlighted ? 0.26 : 0.16).setFill()
        NSBezierPath(roundedRect: frame,
                     xRadius: FocusDesign.Size.settingsPickerCornerRadius,
                     yRadius: FocusDesign.Size.settingsPickerCornerRadius).fill()
        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = .left
        paragraph.lineBreakMode = .byTruncatingTail
        let attributes: [NSAttributedString.Key: Any] = [
            .font: font ?? NSFont.systemFont(ofSize: FocusDesign.Typography.settingsValue),
            .foregroundColor: foreground.withAlphaComponent(isEnabled ? 1 : 0.5),
            .paragraphStyle: paragraph
        ]
        let title = (selectedItem?.title ?? "") as NSString
        let height = title.size(withAttributes: attributes).height
        title.draw(in: Self.textFrame(in: frame, textHeight: height), withAttributes: attributes)
        NSColor(white: darkForeground ? 0 : (whiteForeground ? 1 : 0.88), alpha: isEnabled ? 1 : 0.5).setStroke()
        let arrows = NSBezierPath()
        arrows.lineWidth = 1.2
        arrows.lineCapStyle = .round
        arrows.lineJoinStyle = .round
        let x = frame.maxX - 9, y = frame.midY
        for direction: CGFloat in [-1, 1] {
            arrows.move(to: NSPoint(x: x - 2.3, y: y + direction * 1.3))
            arrows.line(to: NSPoint(x: x, y: y + direction * 3.6))
            arrows.line(to: NSPoint(x: x + 2.3, y: y + direction * 1.3))
        }
        arrows.stroke()
    }
}
