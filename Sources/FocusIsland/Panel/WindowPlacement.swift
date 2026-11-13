import AppKit

enum WindowPlacement {
    struct Display {
        let frame: CGRect
        let visibleFrame: CGRect
    }

    static func placed(_ frame: CGRect, displays: [Display], pointer: CGPoint? = nil) -> CGRect {
        guard !displays.isEmpty else { return frame }
        // A fully visible window stays exactly where the user released it,
        // including the menu-bar area. Proximity alone must not cause snapping.
        if displays.contains(where: { $0.frame.contains(frame) }) { return frame }
        let target: Display
        if let pointer, let underPointer = displays.first(where: { $0.frame.contains(pointer) }) {
            target = underPointer
        } else {
            target = displays.max { lhs, rhs in
                func score(_ display: Display) -> CGFloat {
                    let overlap = frame.intersection(display.frame)
                    if !overlap.isNull { return overlap.width * overlap.height }
                    let point = pointer ?? CGPoint(x: frame.midX, y: frame.midY)
                    let dx = max(display.frame.minX - point.x, 0, point.x - display.frame.maxX)
                    let dy = max(display.frame.minY - point.y, 0, point.y - display.frame.maxY)
                    return -(dx * dx + dy * dy)
                }
                return score(lhs) < score(rhs)
            }!
        }
        let visible = target.frame
        var result = frame
        result.origin.x = min(max(frame.minX, visible.minX), max(visible.minX, visible.maxX - frame.width))
        result.origin.y = min(max(frame.minY, visible.minY), max(visible.minY, visible.maxY - frame.height))
        return result
    }

    static func centeredFrame(size: CGSize, at pointer: CGPoint, displays: [Display]) -> CGRect {
        let proposed = CGRect(
            x: pointer.x - size.width / 2,
            y: pointer.y - size.height / 2,
            width: size.width,
            height: size.height
        )
        return placed(proposed, displays: displays, pointer: pointer)
    }

    static func isMenuBarDrop(
        _ point: CGPoint,
        displays: [Display],
        statusBarHeight: CGFloat
    ) -> Bool {
        let height = max(statusBarHeight, 20)
        return displays.contains { display in
            point.x >= display.frame.minX && point.x <= display.frame.maxX
                && point.y >= display.frame.maxY - height && point.y <= display.frame.maxY
        }
    }

    @MainActor static var displays: [Display] {
        NSScreen.screens.map { Display(frame: $0.frame, visibleFrame: $0.visibleFrame) }
    }
}

/// Pointer tracking is state, not a nested event loop. The normal application
/// loop continues delivering capture frames and asynchronous crop updates.
struct PanelPointerDrag {
    let initialFrame: CGRect
    let initialPointer: CGPoint

    func frame(at pointer: CGPoint) -> CGRect {
        initialFrame.offsetBy(dx: pointer.x - initialPointer.x, dy: pointer.y - initialPointer.y)
    }
}

final class FocusPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
    var onCommandDropInMenuBar: ((NSPoint) -> Bool)?
    var onPointerDragChanged: ((Bool) -> Void)?
    private var pointerDrag: PanelPointerDrag?

    override func performDrag(with event: NSEvent) {
        guard pointerDrag == nil, event.type == .leftMouseDown || event.type == .leftMouseDragged else { return }
        pointerDrag = PanelPointerDrag(initialFrame: frame, initialPointer: NSEvent.mouseLocation)
        onPointerDragChanged?(true)
    }

    override func sendEvent(_ event: NSEvent) {
        if let drag = pointerDrag, event.type == .leftMouseDragged || event.type == .leftMouseUp {
            let pointer = NSEvent.mouseLocation
            let released = event.type == .leftMouseUp
            if released { pointerDrag = nil }
            defer { if released { onPointerDragChanged?(false) } }
            if released, event.modifierFlags.contains(.command), onCommandDropInMenuBar?(pointer) == true {
                super.sendEvent(event)
                return
            }
            let candidate = drag.frame(at: pointer)
            let placed = released
                ? WindowPlacement.placed(candidate, displays: WindowPlacement.displays, pointer: pointer)
                : candidate
            setFrameOrigin(placed.origin)
        }
        // SwiftUI receives normal drag/release events to clear gesture state.
        super.sendEvent(event)
    }

    override func orderOut(_ sender: Any?) {
        if pointerDrag != nil {
            pointerDrag = nil
            onPointerDragChanged?(false)
        }
        super.orderOut(sender)
    }
}
