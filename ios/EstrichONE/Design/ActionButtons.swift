import SwiftUI

/// Beschriftung für große Aktionsschaltflächen: volle Breite, 56 pt hoch.
struct ActionLabel: View {
    let title: LocalizedStringKey
    let systemImage: String?
    var isBusy = false

    init(_ title: LocalizedStringKey, systemImage: String? = nil, isBusy: Bool = false) {
        self.title = title
        self.systemImage = systemImage
        self.isBusy = isBusy
    }

    var body: some View {
        HStack(spacing: 10) {
            if isBusy {
                ProgressView()
            } else if let systemImage {
                Image(systemName: systemImage)
                    .accessibilityHidden(true)
            }
            Text(title)
        }
        .font(.body.weight(.semibold))
        .frame(maxWidth: .infinity, minHeight: Theme.primaryActionHeight - 16)
    }
}

extension View {
    /// Hauptaktion: ab iOS 26 Liquid Glass mit Markentönung, davor die klassische gefüllte Schaltfläche.
    /// Glas liegt nur auf Bedienelementen, nie auf Inhalten (07, Abschnitt 1).
    @ViewBuilder
    func primaryActionStyle() -> some View {
        #if compiler(>=6.2)
        if #available(iOS 26, *) {
            buttonStyle(.glassProminent).tint(Theme.navy).controlSize(.large)
        } else {
            buttonStyle(.borderedProminent).tint(Theme.actionPrimary).controlSize(.large)
        }
        #else
        buttonStyle(.borderedProminent).tint(Theme.actionPrimary).controlSize(.large)
        #endif
    }

    /// Nebenaktion neben einer Hauptaktion.
    @ViewBuilder
    func secondaryActionStyle() -> some View {
        #if compiler(>=6.2)
        if #available(iOS 26, *) {
            buttonStyle(.glass).tint(Theme.actionPrimary).controlSize(.large)
        } else {
            buttonStyle(.bordered).tint(Theme.actionPrimary).controlSize(.large)
        }
        #else
        buttonStyle(.bordered).tint(Theme.actionPrimary).controlSize(.large)
        #endif
    }
}
