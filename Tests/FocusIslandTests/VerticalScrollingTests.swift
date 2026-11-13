import AppKit
import SwiftUI
import Testing
@testable import FocusIsland

@MainActor
struct VerticalScrollingTests {
    @Test func swiftUIHostedVerticalPageLocksItsActualNativeViewport() async throws {
        let host = NSHostingView(rootView: PersistentVerticalScrollView {
            VStack { ForEach(0..<20) { Text("Row \($0)").frame(width: 500, height: 24) } }
        }.frame(width: 240, height: 100))
        host.sizingOptions = []
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 240, height: 100),
                              styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.contentView = host
        defer { window.close() }
        func scrollViews(in view: NSView) -> [NSScrollView] {
            view.subviews.flatMap { child in
                (child as? NSScrollView).map { [$0] } ?? []
            } + view.subviews.flatMap { scrollViews(in: $0) }
        }
        var native: NSScrollView?
        for _ in 0..<20 {
            host.layoutSubtreeIfNeeded()
            native = scrollViews(in: host).first
            if native?.horizontalScrollElasticity == NSScrollView.Elasticity.none { break }
            try await Task.sleep(for: .milliseconds(20))
        }
        let viewport = try #require(native)
        #expect(viewport.horizontalScrollElasticity == .none)
        #expect(viewport.usesPredominantAxisScrolling)
        viewport.contentView.setBoundsOrigin(NSPoint(x: 30, y: 60))
        #expect(viewport.contentView.bounds.minX == 0)
        #expect(viewport.contentView.bounds.minY == 60)
    }

    @Test func horizontalDriftIsClampedWithoutChangingVerticalOffsetAtEveryScale() {
        for scale in [0.5, 0.65, 0.82, 1.0] {
            let scrollView = NSScrollView(frame: NSRect(x: 0, y: 0, width: 240 * scale, height: 100 * scale))
            // Exercise overflowing content too, not only a document that fits.
            scrollView.documentView = NSView(frame: NSRect(x: 0, y: 0, width: 500, height: 500))
            scrollView.layoutSubtreeIfNeeded()
            let bridge = PersistentScrollBridge()
            bridge.attach(scrollView: scrollView)
            #expect(!scrollView.hasHorizontalScroller)
            #expect(scrollView.horizontalScrollElasticity == .none)
            #expect(scrollView.verticalScrollElasticity == .automatic)
            #expect(scrollView.usesPredominantAxisScrolling)

            let clip = scrollView.contentView
            for x: CGFloat in [-24, 24, 180] {
                clip.setBoundsOrigin(NSPoint(x: x, y: 80))
                #expect(clip.bounds.minX == 0)
                #expect(clip.bounds.minY == 80)
            }
            bridge.scroll(toFraction: 1)
            #expect(clip.bounds.minX == 0)
            #expect(bridge.geometry.contentOffset > 0)
            #expect(bridge.scrollByPage(direction: -1))
            #expect(clip.bounds.minX == 0)
            bridge.detach(scrollView: scrollView)
        }
    }

    @Test func repeatedLayoutAndDocumentReplacementKeepHorizontalLock() {
        let scrollView = NSScrollView(frame: NSRect(x: 0, y: 0, width: 240, height: 100))
        scrollView.documentView = NSView(frame: NSRect(x: 0, y: 0, width: 500, height: 500))
        let bridge = PersistentScrollBridge()
        bridge.attach(scrollView: scrollView)
        scrollView.horizontalScrollElasticity = .automatic
        scrollView.usesPredominantAxisScrolling = false
        bridge.attach(scrollView: scrollView)
        #expect(scrollView.horizontalScrollElasticity == .none)
        #expect(scrollView.usesPredominantAxisScrolling)

        scrollView.documentView = NSView(frame: NSRect(x: 0, y: 0, width: 600, height: 600))
        scrollView.contentView.setBoundsOrigin(NSPoint(x: 45, y: 90))
        #expect(scrollView.contentView.bounds.minX == 0)
        #expect(scrollView.contentView.bounds.minY == 90)
        bridge.detach(scrollView: scrollView)
        scrollView.contentView.setBoundsOrigin(NSPoint(x: 45, y: 90))
        #expect(scrollView.contentView.bounds.minX == 45)
    }
}
