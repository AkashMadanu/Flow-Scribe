// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "FlowScribe",
    platforms: [.macOS(.v14)],
    dependencies: [
        // On-device speech recognition on the Apple Neural Engine (Apache-2.0).
        .package(url: "https://github.com/FluidInference/FluidAudio.git", from: "0.18.0")
    ],
    targets: [
        // Pure logic with no AppKit or model code, so it can be unit tested quickly.
        .target(name: "FlowScribeCore"),
        .executableTarget(
            name: "FlowScribe",
            dependencies: [
                "FlowScribeCore",
                .product(name: "FluidAudio", package: "FluidAudio"),
            ]
        ),
        .testTarget(name: "FlowScribeCoreTests", dependencies: ["FlowScribeCore"]),
    ],
    swiftLanguageModes: [.v5]
)
