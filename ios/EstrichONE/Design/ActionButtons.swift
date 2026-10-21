import SwiftUI

/// Rangstufe einer Schaltfläche. Je Bildschirm höchstens eine `primary`; `accent` nur für Aufnehmen (Foto, Scan).
enum ButtonRank {
    /// Gefüllt in Markenfarbe mit Verlauf und leichtem Schatten.
    case primary
    /// Gefüllt im Akzentgrün, dunkle Schrift.
    case accent
    /// Getönte Fläche, Schrift in Markenfarbe.
    case secondary
    /// Nur Schrift; beim Drücken eine getönte Fläche.
    case tertiary
    /// Löschen, Verwerfen.
    case destructive
}

enum ButtonSize {
    /// 48 pt, Schrift `body`.
    case regular
    /// 44 pt (Mindest-Tippfläche), Schrift `subheadline`.
    case compact
}

/// Schaltflächen von EstrichONE: prägnant, kompakt, mit deutlichem Druckzustand und leichter Haptik.
/// Dynamic Type: Die Höhe ist eine Mindesthöhe, Beschriftungen dürfen umbrechen.
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

    private var shape: RoundedRectangle {
        RoundedRectangle(cornerRadius: size == .regular ? Theme.cornerRadius : 11, style: .continuous)
    }

    var body: some View {
        let pressed = configuration.isPressed
        let hapticOnPress = rank != .tertiary
        configuration.label
            .font(size == .regular ? .body.weight(.semibold) : .subheadline.weight(.semibold))
            .labelStyle(TightLabelStyle())
            .multilineTextAlignment(.center)
            .lineLimit(2)
            .foregroundStyle(foreground)
            .tint(foreground)
            .padding(.horizontal, size == .regular ? 18 : 14)
            .padding(.vertical, 6)
            .frame(maxWidth: fullWidth ? .infinity : nil,
                   minHeight: size == .regular ? Theme.controlHeight : Theme.compactControlHeight)
            .background { background(pressed: pressed) }
            .contentShape(shape)
            .hoverEffect(.highlight)
            .scaleEffect(pressed ? 0.97 : 1)
            .opacity(isEnabled ? 1 : 0.45)
            .animation(.spring(response: 0.25, dampingFraction: 0.7), value: pressed)
            .sensoryFeedback(.impact(weight: .light, intensity: 0.7), trigger: pressed) { _, isPressed in
                isPressed && hapticOnPress
            }
    }

    private var foreground: Color {
        switch rank {
        case .primary: .white
        case .accent: Theme.onAccent
        case .secondary, .tertiary: Theme.actionPrimary
        case .destructive: Theme.error
        }
    }

    @ViewBuilder
    private func background(pressed: Bool) -> some View {
        switch rank {
        case .primary:
            shape
                .fill(LinearGradient(colors: [Theme.brandFillTop, Theme.brandFillBottom], startPoint: .top, endPoint: .bottom))
                .overlay(shape.fill(.black.opacity(pressed ? 0.18 : 0)))
                .overlay(shape.strokeBorder(.white.opacity(0.14), lineWidth: 1))
                .shadow(color: Theme.navy.opacity(pressed ? 0.1 : 0.28), radius: pressed ? 2 : 6, y: pressed ? 1 : 3)
        case .accent:
            shape
                .fill(pressed ? Theme.accentPressed : Theme.accent)
                .overlay(shape.strokeBorder(.white.opacity(0.25), lineWidth: 1))
                .shadow(color: Theme.accent.opacity(pressed ? 0.1 : 0.35), radius: pressed ? 2 : 6, y: pressed ? 1 : 3)
        case .secondary:
            shape.fill(pressed ? Theme.secondaryFillPressed : Theme.secondaryFill)
        case .tertiary:
            shape.fill(pressed ? Theme.secondaryFill : .clear)
        case .destructive:
            shape.fill(Theme.errorBackground.opacity(pressed ? 1 : 0.8))
        }
    }
}

/// Symbol und Text dicht beieinander, Symbol etwas kleiner (wie in iOS-Systemschaltflächen).
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

/// Aktion als kleine Kachel: Symbol über kurzer Beschriftung (wie die Aktionsleiste in Karten oder Kontakte).
/// Für Aktionsleisten mit 3–4 gleichrangigen Aktionen; 56 pt hoch.
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
        let shape = RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous)
        configuration.label
            .labelStyle(VerticalLabelStyle())
            .foregroundStyle(foreground)
            .frame(maxWidth: .infinity, minHeight: 56)
            .background {
                switch rank {
                case .primary:
                    shape.fill(LinearGradient(colors: [Theme.brandFillTop, Theme.brandFillBottom], startPoint: .top, endPoint: .bottom))
                        .overlay(shape.fill(.black.opacity(pressed ? 0.18 : 0)))
                        .shadow(color: Theme.navy.opacity(pressed ? 0.1 : 0.25), radius: pressed ? 2 : 5, y: pressed ? 1 : 2)
                case .accent:
                    shape.fill(pressed ? Theme.accentPressed : Theme.accent)
                        .shadow(color: Theme.accent.opacity(pressed ? 0.1 : 0.3), radius: pressed ? 2 : 5, y: pressed ? 1 : 2)
                default:
                    shape.fill(pressed ? Theme.secondaryFillPressed : Theme.secondaryFill)
                }
            }
            .contentShape(shape)
            .hoverEffect(.highlight)
            .scaleEffect(pressed ? 0.95 : 1)
            .opacity(isEnabled ? 1 : 0.45)
            .animation(.spring(response: 0.25, dampingFraction: 0.7), value: pressed)
            .sensoryFeedback(.impact(weight: .light, intensity: 0.7), trigger: pressed) { _, isPressed in isPressed }
    }

    private var foreground: Color {
        switch rank {
        case .primary: .white
        case .accent: Theme.onAccent
        case .destructive: Theme.error
        default: Theme.actionPrimary
        }
    }
}

private struct VerticalLabelStyle: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        VStack(spacing: 4) {
            configuration.icon
                .font(.system(.title3).weight(.semibold))
                .frame(height: 22)
                .accessibilityHidden(true)
            configuration.title
                .font(.caption.weight(.semibold))
                .lineLimit(1)
                .minimumScaleFactor(0.8)
        }
        .padding(.horizontal, 4)
        .padding(.vertical, 7)
    }
}

/// Runde Symbolschaltfläche (44 pt), z. B. Anrufen oder Route in einer Zeile.
struct IconButtonStyle: ButtonStyle {
    var rank: ButtonRank = .secondary

    func makeBody(configuration: Configuration) -> some View {
        let pressed = configuration.isPressed
        configuration.label
            .labelStyle(.iconOnly)
            .font(.body.weight(.semibold))
            .foregroundStyle(rank == .primary ? Color.white : (rank == .accent ? Theme.onAccent : Theme.actionPrimary))
            .frame(width: 44, height: 44)
            .background {
                Circle().fill(rank == .primary ? Theme.navyLight : (rank == .accent ? Theme.accent : (pressed ? Theme.secondaryFillPressed : Theme.secondaryFill)))
            }
            .contentShape(Circle())
            .hoverEffect(.highlight)
            .scaleEffect(pressed ? 0.92 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.7), value: pressed)
            .sensoryFeedback(.impact(weight: .light, intensity: 0.7), trigger: pressed) { _, isPressed in isPressed }
    }
}

/// Beschriftung für Aktionsschaltflächen über die ganze Breite (Einrichtung, Anmeldung, Formulare).
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

    /// Bedienelement über Kamerabild oder Navy-Kopf: ab iOS 26 Liquid Glass, davor eine dunkle, deckende Kapsel.
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

/// Dunkle Kapsel für Bedienelemente auf Fotos und farbigen Flächen (vor iOS 26).
struct OverlayControlStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.subheadline.weight(.semibold))
            .labelStyle(TightLabelStyle())
            .foregroundStyle(.white)
            .padding(.horizontal, 14)
            .frame(minHeight: 44)
            .background(.black.opacity(configuration.isPressed ? 0.7 : 0.5), in: Capsule())
            .overlay(Capsule().strokeBorder(.white.opacity(0.2)))
            .contentShape(Capsule())
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.7), value: configuration.isPressed)
    }
}
