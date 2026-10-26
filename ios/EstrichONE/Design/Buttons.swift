import SwiftUI

/// Rangstufe einer Schaltfläche. Je Bildschirmbereich höchstens eine `primary`.
enum ButtonRank {
    /// Gefüllt in Markenblau, weiße Schrift – die eine Hauptaktion.
    case primary
    /// Früher eigene Akzentfarbe; heute gleich `primary` (eine Akzentfarbe).
    case accent
    /// Zart getönt, Schrift in Markenblau – Nebenaktionen.
    case secondary
    /// Nur Schrift; beim Drücken eine getönte Fläche.
    case tertiary
    /// Löschen, Verwerfen, Stornieren.
    case destructive
}

enum ButtonSize {
    /// 52 pt – gut mit Handschuhen.
    case regular
    /// 44 pt (Mindest-Tippfläche), in Zeilen und Leisten.
    case compact
}

/// Schaltflächen von EstrichONE: Kapselform, klare Rangfolge, deutlicher Druckzustand mit leichter Haptik.
/// Dynamic Type: Die Höhe ist eine Mindesthöhe; Beschriftungen dürfen in zwei Zeilen umbrechen.
struct EstrichButtonStyle: ButtonStyle {
    var rank: ButtonRank = .primary
    var size: ButtonSize = .regular
    var fullWidth = false

    func makeBody(configuration: Configuration) -> some View {
        EstrichButtonBody(configuration: configuration, rank: rank, size: size, fullWidth: fullWidth)
    }
}

private struct EstrichButtonBody: View {
    let configuration: ButtonStyleConfiguration
    let rank: ButtonRank
    let size: ButtonSize
    let fullWidth: Bool

    @Environment(\.isEnabled) private var isEnabled

    var body: some View {
        let pressed = configuration.isPressed
        configuration.label
            .font(size == .regular ? .body.weight(.semibold) : .subheadline.weight(.semibold))
            .labelStyle(TightLabelStyle())
            .multilineTextAlignment(.center)
            .lineLimit(2)
            .foregroundStyle(foreground)
            .tint(foreground)
            .padding(.horizontal, size == .regular ? 20 : 14)
            .padding(.vertical, 6)
            .frame(maxWidth: fullWidth ? .infinity : nil, minHeight: size == .regular ? Size.button : Size.minTap)
            .background { Capsule().fill(fill(pressed: pressed)) }
            .contentShape(Capsule())
            .hoverEffect(.highlight)
            .scaleEffect(pressed ? 0.97 : 1)
            .opacity(isEnabled ? 1 : 0.4)
            .animation(.spring(response: 0.25, dampingFraction: 0.75), value: pressed)
            .sensoryFeedback(.impact(weight: .light, intensity: 0.6), trigger: pressed) { _, isPressed in
                isPressed && rank != .tertiary
            }
    }

    private var foreground: Color {
        switch rank {
        case .primary, .accent: Theme.onAccent
        case .secondary, .tertiary: Theme.accent
        case .destructive: Theme.danger
        }
    }

    private func fill(pressed: Bool) -> Color {
        switch rank {
        case .primary, .accent: pressed ? Theme.accentPressed : Theme.accentFill
        case .secondary: pressed ? Theme.fillStrong : Theme.accentSoft
        case .tertiary: pressed ? Theme.fill : .clear
        case .destructive: pressed ? Theme.fillStrong : Theme.dangerSoft
        }
    }
}

/// Symbol und Text dicht beieinander (wie in Systemschaltflächen).
struct TightLabelStyle: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 7) {
            configuration.icon
                .imageScale(.medium)
                .accessibilityHidden(true)
            configuration.title
        }
    }
}

/// Aktion als Kachel: Symbol über kurzer Beschriftung. Für Leisten mit 3–5 gleichrangigen Aktionen (Foto, Tagebuch …);
/// 64 pt hoch, auf Karten wie auf dem Seitenhintergrund lesbar.
struct ActionTileStyle: ButtonStyle {
    var rank: ButtonRank = .secondary

    func makeBody(configuration: Configuration) -> some View {
        ActionTileBody(configuration: configuration, rank: rank)
    }
}

private struct ActionTileBody: View {
    let configuration: ButtonStyleConfiguration
    let rank: ButtonRank
    @Environment(\.isEnabled) private var isEnabled

    var body: some View {
        let pressed = configuration.isPressed
        let prominent = rank == .primary || rank == .accent
        let shape = RoundedRectangle(cornerRadius: Radius.l, style: .continuous)
        configuration.label
            .labelStyle(VerticalLabelStyle(iconTint: prominent ? Theme.onAccent : Theme.accent))
            .foregroundStyle(prominent ? Theme.onAccent : Theme.textPrimary)
            .frame(maxWidth: .infinity, minHeight: 64)
            .background {
                shape.fill(prominent ? (pressed ? Theme.accentPressed : Theme.accentFill) : (pressed ? Theme.fillStrong : Theme.fill))
            }
            .contentShape(shape)
            .hoverEffect(.highlight)
            .scaleEffect(pressed ? 0.96 : 1)
            .opacity(isEnabled ? 1 : 0.4)
            .animation(.spring(response: 0.25, dampingFraction: 0.75), value: pressed)
            .sensoryFeedback(.impact(weight: .light, intensity: 0.6), trigger: pressed) { _, isPressed in isPressed }
    }
}

private struct VerticalLabelStyle: LabelStyle {
    let iconTint: Color

    func makeBody(configuration: Configuration) -> some View {
        VStack(spacing: 5) {
            configuration.icon
                .font(.title3.weight(SymbolWeight.control))
                .foregroundStyle(iconTint)
                .frame(height: 24)
                .accessibilityHidden(true)
            configuration.title
                .font(.footnote.weight(.semibold))
                .lineLimit(1)
                .minimumScaleFactor(0.75)
        }
        .padding(.horizontal, 6)
        .padding(.vertical, 8)
    }
}

/// Runde Symbolschaltfläche (44 pt), z. B. Anrufen oder Route in einer Zeile.
struct IconButtonStyle: ButtonStyle {
    var rank: ButtonRank = .secondary

    func makeBody(configuration: Configuration) -> some View {
        let pressed = configuration.isPressed
        let prominent = rank == .primary || rank == .accent
        configuration.label
            .labelStyle(.iconOnly)
            .font(.body.weight(SymbolWeight.control))
            .foregroundStyle(prominent ? Theme.onAccent : (rank == .destructive ? Theme.danger : Theme.accent))
            .frame(width: Size.minTap, height: Size.minTap)
            .background {
                Circle().fill(prominent ? (pressed ? Theme.accentPressed : Theme.accentFill) : (pressed ? Theme.fillStrong : Theme.accentSoft))
            }
            .contentShape(Circle())
            .hoverEffect(.highlight)
            .scaleEffect(pressed ? 0.92 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.75), value: pressed)
            .sensoryFeedback(.impact(weight: .light, intensity: 0.6), trigger: pressed) { _, isPressed in isPressed }
    }
}

/// Beschriftung für Schaltflächen über die ganze Breite (Einrichtung, Anmeldung, Formulare), optional mit Ladeanzeige.
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
        HStack(spacing: 8) {
            if isBusy {
                ProgressView().controlSize(.small)
            } else if let systemImage {
                Image(systemName: systemImage)
                    .imageScale(.medium)
                    .accessibilityHidden(true)
            }
            Text(title)
        }
        .frame(maxWidth: .infinity)
    }
}

/// Zeilen in Karten: beim Drücken grau hinterlegt wie Listenzeilen.
struct RowButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(configuration.isPressed ? Theme.fill : .clear)
            .contentShape(Rectangle())
    }
}

extension View {
    /// Hauptaktion über die ganze Breite.
    func primaryActionStyle() -> some View {
        buttonStyle(EstrichButtonStyle(rank: .primary, size: .regular, fullWidth: true))
    }

    /// Nebenaktion neben oder unter einer Hauptaktion.
    func secondaryActionStyle() -> some View {
        buttonStyle(EstrichButtonStyle(rank: .secondary, size: .regular, fullWidth: true))
    }

    /// Kurzform für die übrigen Stufen und Größen.
    func estrichButton(_ rank: ButtonRank, size: ButtonSize = .compact, fullWidth: Bool = false) -> some View {
        buttonStyle(EstrichButtonStyle(rank: rank, size: size, fullWidth: fullWidth))
    }

    /// Bedienelement über Kamerabild oder Foto: ab iOS 26 Liquid Glass, davor eine dunkle, deckende Kapsel.
    @ViewBuilder
    func overlayControlStyle() -> some View {
        #if compiler(>=6.2)
        if #available(iOS 26, *) {
            self.buttonStyle(.glass).controlSize(.regular)
        } else {
            self.buttonStyle(OverlayControlStyle())
        }
        #else
        self.buttonStyle(OverlayControlStyle())
        #endif
    }
}

/// Dunkle Kapsel für Bedienelemente auf Fotos (vor iOS 26).
struct OverlayControlStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.subheadline.weight(.semibold))
            .labelStyle(TightLabelStyle())
            .foregroundStyle(.white)
            .padding(.horizontal, 14)
            .frame(minHeight: Size.minTap)
            .background(.black.opacity(configuration.isPressed ? 0.7 : 0.5), in: Capsule())
            .overlay(Capsule().strokeBorder(.white.opacity(0.2)))
            .contentShape(Capsule())
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.75), value: configuration.isPressed)
    }
}
