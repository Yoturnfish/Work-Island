import AppKit
import Testing
@testable import FocusIsland

struct WindowPlacementTests {
    let main = WindowPlacement.Display(frame: CGRect(x: 0, y: 0, width: 1920, height: 1080), visibleFrame: CGRect(x: 0, y: 0, width: 1920, height: 1050))
    let left = WindowPlacement.Display(frame: CGRect(x: -1920, y: 0, width: 1920, height: 1080), visibleFrame: CGRect(x: -1920, y: 0, width: 1920, height: 1080))

    @Test func pointerDragUsesAbsoluteDeltaWithoutAccumulationAndClampsOnlyOnRelease() {
        let frame = CGRect(x: 100, y: 500, width: 283, height: 76)
        let drag = PanelPointerDrag(initialFrame: frame, initialPointer: CGPoint(x: 200, y: 530))
        for point in [CGPoint(x: 350, y: 480), CGPoint(x: 1900, y: 530), CGPoint(x: 200, y: 530)] {
            let candidate = drag.frame(at: point)
            #expect(candidate.origin == CGPoint(x: 100 + point.x - 200, y: 500 + point.y - 530))
            #expect(candidate.size == frame.size)
        }
        let outside = drag.frame(at: CGPoint(x: 1900, y: 530))
        #expect(outside.maxX > main.frame.maxX)
        #expect(main.frame.contains(WindowPlacement.placed(outside, displays: [main])))
        #expect(drag.frame(at: drag.initialPointer) == frame)
    }

    @Test @MainActor func panelDragReturnsToTheApplicationLoopBeforeMouseRelease() async throws {
        let panel = FocusPanel(contentRect: CGRect(x: 100, y: 500, width: 283, height: 76),
                               styleMask: .borderless, backing: .buffered, defer: false)
        panel.isReleasedWhenClosed = false
        defer { panel.close() }
        var transitions: [Bool] = []
        panel.onPointerDragChanged = { transitions.append($0) }
        let event = try #require(NSEvent.mouseEvent(with: .leftMouseDragged, location: CGPoint(x: 100, y: 40),
            modifierFlags: [], timestamp: 0, windowNumber: panel.windowNumber, context: nil,
            eventNumber: 1, clickCount: 1, pressure: 1))
        panel.performDrag(with: event)
        panel.performDrag(with: event) // repeated callbacks must not restart a drag
        #expect(transitions == [true])
        // An asynchronous main-actor delivery must run while the drag is active.
        let delivery = Task { @MainActor in true }
        #expect(await delivery.value)
        let release = try #require(NSEvent.mouseEvent(with: .leftMouseUp, location: event.locationInWindow,
            modifierFlags: [], timestamp: 1, windowNumber: panel.windowNumber, context: nil,
            eventNumber: 2, clickCount: 1, pressure: 0))
        panel.sendEvent(release)
        #expect(transitions == [true, false])
    }

    @Test @MainActor func hidingAnActivePanelEndsHighRefreshDragExactlyOnce() throws {
        let panel = FocusPanel(contentRect: CGRect(x: 100, y: 500, width: 283, height: 76),
                               styleMask: .borderless, backing: .buffered, defer: false)
        panel.isReleasedWhenClosed = false
        defer { panel.close() }
        var transitions: [Bool] = []
        panel.onPointerDragChanged = { transitions.append($0) }
        let event = try #require(NSEvent.mouseEvent(with: .leftMouseDragged, location: CGPoint(x: 100, y: 40),
            modifierFlags: [], timestamp: 0, windowNumber: panel.windowNumber, context: nil,
            eventNumber: 1, clickCount: 1, pressure: 1))
        panel.performDrag(with: event)
        panel.orderOut(nil)
        panel.orderOut(nil)
        #expect(transitions == [true, false])
    }

    @Test func releaseAtSeamKeepsWholeWindowOnPointerDisplay() {
        for x in stride(from: -400, through: 400, by: 5) {
            let pointer = CGPoint(x: x, y: 500)
            let raw = CGRect(x: x - 140, y: 465, width: 283, height: 76)
            let result = WindowPlacement.placed(raw, displays: [main, left], pointer: pointer)
            #expect((x < 0 ? left : main).frame.contains(result))
            #expect(result.size == raw.size)
        }
    }
    @Test func releaseOutsidePhysicalEdgesNeverHidesContent() {
        for point in [CGPoint(x: -100, y: 500), CGPoint(x: 2000, y: 500), CGPoint(x: 800, y: 1100), CGPoint(x: 800, y: -100)] {
            let result = WindowPlacement.placed(CGRect(origin: point, size: CGSize(width: 283, height: 76)), displays: [main], pointer: point)
            #expect(main.frame.contains(result))
        }
        let near = CGRect(x: 7, y: 967, width: 283, height: 76)
        #expect(WindowPlacement.placed(near, displays: [main]) == near)
        let coveringMenuBar = CGRect(x: 700, y: 1004, width: 283, height: 76)
        #expect(WindowPlacement.placed(coveringMenuBar, displays: [main]) == coveringMenuBar)
    }
    @Test func disconnectAndVerticallyOffsetScreensClampToAvailableArea() {
        let old = CGRect(x: -1700, y: 500, width: 283, height: 76)
        #expect(main.frame.contains(WindowPlacement.placed(old, displays: [main])))
        let upper = WindowPlacement.Display(frame: CGRect(x: 600, y: 1080, width: 1200, height: 900), visibleFrame: CGRect(x: 600, y: 1080, width: 1200, height: 875))
        #expect(upper.frame.contains(WindowPlacement.placed(CGRect(x: 700, y: 1050, width: 283, height: 76), displays: [main, upper], pointer: CGPoint(x: 800, y: 1100))))
    }

    @Test func commandDropTargetIsOnlyThePhysicalMenuBarBand() {
        #expect(WindowPlacement.isMenuBarDrop(CGPoint(x: 900, y: 1070), displays: [main, left], statusBarHeight: 24))
        #expect(WindowPlacement.isMenuBarDrop(CGPoint(x: -900, y: 1070), displays: [main, left], statusBarHeight: 24))
        #expect(!WindowPlacement.isMenuBarDrop(CGPoint(x: 900, y: 1040), displays: [main, left], statusBarHeight: 24))
        #expect(!WindowPlacement.isMenuBarDrop(CGPoint(x: 2000, y: 1070), displays: [main, left], statusBarHeight: 24))
    }

    @Test func removedStatusItemReturnsAWholeWindowAtTheReleasePoint() {
        let size = CGSize(width: 283, height: 76)
        for point in [CGPoint(x: 960, y: 540), CGPoint(x: 1918, y: 1078), CGPoint(x: -1918, y: 2)] {
            let result = WindowPlacement.centeredFrame(size: size, at: point, displays: [main, left])
            #expect((point.x < 0 ? left : main).frame.contains(result))
            #expect(result.size == size)
        }
    }

    @Test func menuBarCommandDragConvertsOnlyAfterLeavingTheBar() {
        #expect(!IslandPanelController.shouldConvertStatusCommandDrag(
            didMove: false, pointer: CGPoint(x: 900, y: 500), displays: [main], statusBarHeight: 24
        ))
        #expect(!IslandPanelController.shouldConvertStatusCommandDrag(
            didMove: true, pointer: CGPoint(x: 900, y: 1070), displays: [main], statusBarHeight: 24
        ))
        #expect(IslandPanelController.shouldConvertStatusCommandDrag(
            didMove: true, pointer: CGPoint(x: 900, y: 900), displays: [main], statusBarHeight: 24
        ))
    }
}
