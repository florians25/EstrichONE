// swift-tools-version: 6.0
import PackageDescription

/// Plattformneutrale Kernlogik der EstrichONE-App (ohne UIKit/SwiftUI), auf macOS und Linux testbar.
let package = Package(
    name: "EstrichCore",
    defaultLocalization: "de",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [
        .library(name: "EstrichCore", targets: ["EstrichCore"]),
    ],
    targets: [
        .target(name: "EstrichCore"),
        .testTarget(name: "EstrichCoreTests", dependencies: ["EstrichCore"]),
    ]
)
