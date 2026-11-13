// swift-tools-version: 6.1
import PackageDescription

let package = Package(
    name: "FocusIsland",
    platforms: [.macOS(.v15)],
    products: [
        .library(name: "WorkIslandCore", targets: ["WorkIslandCore"]),
        .executable(name: "FocusIsland", targets: ["FocusIsland"])
    ],
    targets: [
        .target(
            name: "WorkIslandCore",
            path: "Sources/WorkIslandCore"
        ),
        .executableTarget(
            name: "FocusIsland",
            dependencies: ["WorkIslandCore"],
            path: "Sources/FocusIsland"
        ),
        .testTarget(
            name: "WorkIslandCoreTests",
            dependencies: ["WorkIslandCore"],
            path: "Tests/WorkIslandCoreTests"
        ),
        .testTarget(
            name: "FocusIslandTests",
            dependencies: ["FocusIsland"],
            path: "Tests/FocusIslandTests"
        )
    ]
)
