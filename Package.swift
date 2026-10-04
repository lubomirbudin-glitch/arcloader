// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Offload",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "offload", targets: ["offload"]),
        .library(name: "OffloadKit", targets: ["OffloadKit"]),
    ],
    dependencies: [
        .package(url: "https://github.com/apple/swift-argument-parser", from: "1.5.0"),
    ],
    targets: [
        .target(name: "CXXHash", path: "Sources/CXXHash"),
        .target(name: "OffloadKit", dependencies: ["CXXHash"]),
        .executableTarget(name: "OffloadApp", dependencies: ["OffloadKit"]),
        .executableTarget(
            name: "offload",
            dependencies: [
                "OffloadKit",
                .product(name: "ArgumentParser", package: "swift-argument-parser"),
            ]
        ),
    ]
)
