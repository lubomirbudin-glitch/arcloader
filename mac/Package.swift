// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Offload",
    platforms: [.macOS(.v13)],
    products: [
        // The CLI as users type it (mmMover, user decision 2026-10-11); the target and its folder keep the old internal name.
        .executable(name: "mmmover", targets: ["offload"]),
        .library(name: "OffloadKit", targets: ["OffloadKit"]),
    ],
    dependencies: [
        .package(url: "https://github.com/apple/swift-argument-parser", from: "1.5.0"),
    ],
    targets: [
        .target(name: "CXXHash", path: "Sources/CXXHash"),
        .target(name: "OffloadKit", dependencies: ["CXXHash"],
                linkerSettings: [.linkedFramework("IOKit"), .linkedFramework("DiskArbitration")]),
        .executableTarget(name: "OffloadApp", dependencies: ["OffloadKit"]),
        .executableTarget(
            name: "offload",
            dependencies: [
                "OffloadKit",
                .product(name: "ArgumentParser", package: "swift-argument-parser"),
            ]
        ),
        .testTarget(name: "OffloadKitTests", dependencies: ["OffloadKit"], path: "Tests/OffloadKitTests",
                    exclude: ["Fixtures"]),   // read by path (#filePath), not bundled
    ]
)
