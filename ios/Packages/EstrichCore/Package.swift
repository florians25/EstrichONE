// swift-tools-version: 6.0
import PackageDescription

/// Plattformneutrale Kernlogik der EstrichONE-App (ohne UIKit/SwiftUI), auf macOS und Linux testbar.
/// Kryptografie: CryptoKit auf Apple-Plattformen, unter Linux (nur Tests) das API-gleiche swift-crypto.
let package = Package(
    name: "EstrichCore",
    defaultLocalization: "de",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [
        .library(name: "EstrichCore", targets: ["EstrichCore"]),
    ],
    dependencies: [
        .package(url: "https://github.com/apple/swift-crypto.git", "3.0.0"..<"6.0.0"),
    ],
    targets: [
        .target(
            name: "EstrichCore",
            dependencies: [
                .product(name: "Crypto", package: "swift-crypto", condition: .when(platforms: [.linux])),
            ]),
        .testTarget(name: "EstrichCoreTests", dependencies: ["EstrichCore"]),
    ]
)
