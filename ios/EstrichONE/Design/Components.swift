import EstrichCore
import SwiftUI

// MARK: Karten und Abschnitte

extension View {
    /// Hintergrund einer Karte (ohne Innenabstand).
    func card(radius: CGFloat = Radius.xl) -> some View {
        background(Theme.surface, in: RoundedRectangle(cornerRadius: radius, style: .continuous))
    }

    /// Breite begrenzen und zentrieren (iPad quer, große Bildschirme).
    func readableWidth(_ width: CGFloat = Size.readableWidth) -> some View {
        frame(maxWidth: width).frame(maxWidth: .infinity)
    }
}

/// Karte mit Innenabstand; Inhalt linksbündig untereinander.
struct Card<Content: View>: View {
    var padding: CGFloat
    var spacing: CGFloat
    let content: Content

    init(padding: CGFloat = Space.l, spacing: CGFloat = Space.m, @ViewBuilder content: () -> Content) {
        self.padding = padding
        self.spacing = spacing
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: spacing) { content }
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card()
    }
}

/// Überschrift eines Abschnitts auf frei gestalteten Seiten, rechts optional eine Aktion („Alle“).
struct SectionHeading<Trailing: View>: View {
    let title: String
    let trailing: Trailing

    init(_ title: String, @ViewBuilder trailing: () -> Trailing) {
        self.title = title
        self.trailing = trailing()
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.s) {
            Text(title)
                .font(Typo.section)
                .foregroundStyle(Theme.textPrimary)
                .accessibilityAddTraits(.isHeader)
            Spacer(minLength: Space.s)
            trailing
                .font(Typo.subheadStrong)
                .tint(Theme.accent)
        }
        .padding(.horizontal, Space.xs)
    }
}

extension SectionHeading where Trailing == EmptyView {
    init(_ title: String) {
        self.init(title) { EmptyView() }
    }
}

/// „Alle (3)“ mit Pfeil – rechts neben einer Abschnittsüberschrift.
struct MoreLink: View {
    let title: String
    let action: () -> Void

    init(_ title: String = "Alle", action: @escaping () -> Void) {
        self.title = title
        self.action = action
    }

    var body: some View {
        Button(action: action) {
            HStack(spacing: 3) {
                Text(title)
                Image(systemName: "chevron.right").font(.caption.weight(.bold))
            }
            .frame(minHeight: Size.minTap)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(Theme.accent)
    }
}

/// Pfeil rechts in Zeilen, die eine Seite öffnen.
struct Chevron: View {
    var body: some View {
        Image(systemName: "chevron.right")
            .font(.footnote.weight(.semibold))
            .foregroundStyle(Theme.textTertiary)
            .accessibilityHidden(true)
    }
}

/// Trennlinie in Karten, eingerückt bis zum Text.
struct InsetDivider: View {
    var leading: CGFloat = 0
    @Environment(\.displayScale) private var displayScale

    var body: some View {
        Rectangle()
            .fill(Theme.separator)
            .frame(height: 1 / max(displayScale, 1))
            .padding(.leading, leading)
    }
}

// MARK: Symbole, Avatare, Chips

/// SF Symbol in einer zart getönten, abgerundeten Fläche – einheitlich in Markenblau (keine bunten Kacheln).
struct IconBadge: View {
    let symbol: String
    var tint: Color = Theme.accent
    var background: Color = Theme.accentSoft
    var size: CGFloat = Size.iconBadge
    @ScaledMetric(relativeTo: .body) private var scale: CGFloat = 1

    var body: some View {
        let side = size * min(scale, 1.6)
        Image(systemName: symbol)
            .font(.system(size: side * 0.47, weight: SymbolWeight.control))
            .foregroundStyle(tint)
            .frame(width: side, height: side)
            .background(background, in: RoundedRectangle(cornerRadius: side * 0.3, style: .continuous))
            .accessibilityHidden(true)
    }
}

/// Kreis mit Initialen bzw. Firmensymbol, ruhig in Markenblau.
struct Avatar: View {
    let name: String
    var isCompany = false
    var size: CGFloat = 40

    var body: some View {
        ZStack {
            Circle().fill(Theme.accentSoft)
            if isCompany {
                Image(systemName: "building.2.fill").font(.system(size: size * 0.4, weight: .semibold))
            } else {
                Text(LocalUser.initials(of: name)).font(.system(size: size * 0.38, weight: .semibold, design: .rounded))
            }
        }
        .foregroundStyle(Theme.accent)
        .frame(width: size, height: size)
        .accessibilityHidden(true)
    }
}

/// Kleiner Statuspunkt.
struct StatusDot: View {
    let color: Color
    var size: CGFloat = 8

    var body: some View {
        Circle().fill(color).frame(width: size, height: size).accessibilityHidden(true)
    }
}

/// Kurze Kennzeichnung als Kapsel: neutral, in Markenblau oder als Status.
struct Chip: View {
    enum Tone {
        case neutral, accent, success, warning, danger
    }

    let text: String
    var symbol: String?
    var dot: Color?
    var tone: Tone = .neutral

    var body: some View {
        HStack(spacing: 5) {
            if let dot { StatusDot(color: dot, size: 7) }
            if let symbol { Image(systemName: symbol).imageScale(.small).accessibilityHidden(true) }
            Text(text).lineLimit(1)
        }
        .font(Typo.labelStrong)
        .foregroundStyle(foreground)
        .padding(.horizontal, 9)
        .frame(minHeight: 26)
        .background(background, in: Capsule())
    }

    private var foreground: Color {
        switch tone {
        case .neutral: Theme.textSecondary
        case .accent: Theme.accent
        case .success: Theme.success
        case .warning: Theme.warning
        case .danger: Theme.danger
        }
    }

    private var background: Color {
        switch tone {
        // Auch auf dem grauen Seitenhintergrund sichtbar (nicht nur auf weißen Karten).
        case .neutral: Theme.fillStrong
        case .accent: Theme.accentSoft
        case .success: Theme.successSoft
        case .warning: Theme.warningSoft
        case .danger: Theme.dangerSoft
        }
    }
}

// MARK: Zeilen und Werte

/// Zeile mit Symbol, Titel, Untertitel und rechtem Zubehör (Wert, Pfeil, Knopf). Mindesthöhe 56 pt.
struct DetailRow<Trailing: View>: View {
    let symbol: String?
    let title: String
    var subtitle: String?
    var tint: Color = Theme.accent
    var titleColor: Color = Theme.textPrimary
    let trailing: Trailing

    init(_ title: String, subtitle: String? = nil, symbol: String? = nil, tint: Color = Theme.accent,
         titleColor: Color = Theme.textPrimary, @ViewBuilder trailing: () -> Trailing) {
        self.title = title
        self.subtitle = subtitle
        self.symbol = symbol
        self.tint = tint
        self.titleColor = titleColor
        self.trailing = trailing()
    }

    var body: some View {
        HStack(spacing: Space.m) {
            if let symbol {
                IconBadge(symbol: symbol, tint: tint, background: tint.opacity(0.12))
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(Typo.body)
                    .foregroundStyle(titleColor)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
                if let subtitle {
                    Text(subtitle)
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .multilineTextAlignment(.leading)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            trailing
        }
        .frame(minHeight: Size.row - Space.m, alignment: .leading)
        .contentShape(Rectangle())
    }
}

extension DetailRow where Trailing == EmptyView {
    init(_ title: String, subtitle: String? = nil, symbol: String? = nil, tint: Color = Theme.accent, titleColor: Color = Theme.textPrimary) {
        self.init(title, subtitle: subtitle, symbol: symbol, tint: tint, titleColor: titleColor) { EmptyView() }
    }
}

/// Beschriftung über Wert – für Kenndaten in Spalten.
struct InfoItem: Identifiable, Equatable {
    let label: String
    let value: String
    var symbol: String?
    var id: String { label }
}

struct InfoGrid: View {
    let items: [InfoItem]
    var columns = 2

    var body: some View {
        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: Space.l, alignment: .topLeading), count: max(1, columns)),
                  alignment: .leading, spacing: Space.l) {
            ForEach(items) { item in
                VStack(alignment: .leading, spacing: 3) {
                    Text(item.label)
                        .font(Typo.label)
                        .foregroundStyle(Theme.textSecondary)
                        .lineLimit(1)
                    Text(item.value)
                        .font(.callout.weight(.semibold))
                        .foregroundStyle(Theme.textPrimary)
                        .multilineTextAlignment(.leading)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityElement(children: .combine)
            }
        }
    }
}

/// Kennzahl als Kachel: Symbol, große Zahl, Beschriftung. Antippbar über einen umgebenden Button.
struct MetricTile: View {
    let value: String
    let label: String
    let symbol: String
    var tint: Color = Theme.accent

    var body: some View {
        VStack(alignment: .leading, spacing: Space.s) {
            Image(systemName: symbol)
                .font(.subheadline.weight(SymbolWeight.control))
                .foregroundStyle(tint)
                .accessibilityHidden(true)
            Text(value)
                .font(Typo.metric)
                .foregroundStyle(Theme.textPrimary)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
            Text(label)
                .font(Typo.meta)
                .foregroundStyle(Theme.textSecondary)
                .lineLimit(2, reservesSpace: true)
                .multilineTextAlignment(.leading)
        }
        .padding(Space.m + 2)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: Radius.l)
        .contentShape(RoundedRectangle(cornerRadius: Radius.l, style: .continuous))
        .accessibilityElement(children: .combine)
    }
}

// MARK: Hinweise und Leerzustände

/// Hinweis mit Symbol, Titel und Text; Ton bestimmt die (gedämpfte) Farbe.
struct Callout: View {
    enum Tone {
        case info, warning, danger, success, neutral

        var symbol: String {
            switch self {
            case .info: "info.circle.fill"
            case .warning: "exclamationmark.triangle.fill"
            case .danger: "exclamationmark.octagon.fill"
            case .success: "checkmark.circle.fill"
            case .neutral: "text.bubble.fill"
            }
        }

        var foreground: Color {
            switch self {
            case .info: Theme.accent
            case .warning: Theme.warning
            case .danger: Theme.danger
            case .success: Theme.success
            case .neutral: Theme.textSecondary
            }
        }

        var background: Color {
            switch self {
            case .info: Theme.accentSoft
            case .warning: Theme.warningSoft
            case .danger: Theme.dangerSoft
            case .success: Theme.successSoft
            case .neutral: Theme.fill
            }
        }
    }

    let tone: Tone
    var title: String?
    let message: String
    var symbol: String?
    var actionTitle: String?
    var action: (() -> Void)?

    var body: some View {
        HStack(alignment: .top, spacing: Space.m) {
            Image(systemName: symbol ?? tone.symbol)
                .font(.body.weight(SymbolWeight.control))
                .foregroundStyle(tone.foreground)
                .frame(width: 22)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3) {
                if let title {
                    Text(title).font(Typo.subheadStrong).foregroundStyle(tone == .neutral ? Theme.textPrimary : tone.foreground)
                }
                Text(message)
                    .font(Typo.subhead)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
                if let actionTitle, let action {
                    Button(actionTitle, action: action)
                        .font(Typo.subheadStrong)
                        .foregroundStyle(Theme.accent)
                        .frame(minHeight: 32)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(Space.m + 2)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(tone.background, in: RoundedRectangle(cornerRadius: Radius.l, style: .continuous))
        .accessibilityElement(children: .combine)
    }
}

/// Meldet, ob eine Seite über ihren Kopf hinaus gescrollt ist – dann zeigt die Navigationsleiste den Titel
/// (wie Kontakte). Unter iOS 17 steht der Titel immer in der Leiste.
struct ScrolledPastHeader: ViewModifier {
    @Binding var isScrolled: Bool
    var threshold: CGFloat = 72

    func body(content: Content) -> some View {
        if #available(iOS 18.0, *) {
            content.onScrollGeometryChange(for: Bool.self) { geometry in
                geometry.contentOffset.y + geometry.contentInsets.top > threshold
            } action: { _, scrolled in
                isScrolled = scrolled
            }
        } else {
            content.onAppear { isScrolled = true }
        }
    }
}

extension View {
    func scrolledPastHeader(_ isScrolled: Binding<Bool>, threshold: CGFloat = 72) -> some View {
        modifier(ScrolledPastHeader(isScrolled: isScrolled, threshold: threshold))
    }
}

/// Eingabefeld in Formularen mit kleiner Beschriftung darüber – bleibt lesbar, wenn das Feld schon ausgefüllt ist.
struct LabeledField<Field: View>: View {
    let title: String
    @ViewBuilder let field: () -> Field

    init(_ title: String, @ViewBuilder field: @escaping () -> Field) {
        self.title = title
        self.field = field
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title)
                .font(Typo.label)
                .foregroundStyle(Theme.textSecondary)
            field()
                .font(Typo.body)
        }
        .padding(.vertical, 2)
    }
}

/// Knapper Hinweis in einer Zeile (z. B. Demomodus): Symbol, Text, optional eine Aktion rechts – nimmt kaum Platz weg.
struct NoticeLine: View {
    let tone: Callout.Tone
    let text: String
    var symbol: String?
    var actionTitle: String?
    var action: (() -> Void)?

    var body: some View {
        HStack(spacing: Space.s) {
            Image(systemName: symbol ?? tone.symbol)
                .font(.subheadline.weight(SymbolWeight.control))
                .foregroundStyle(tone.foreground)
                .accessibilityHidden(true)
            Text(text)
                .font(Typo.subhead)
                .foregroundStyle(Theme.textPrimary)
                .lineLimit(2)
                .frame(maxWidth: .infinity, alignment: .leading)
            if let actionTitle, let action {
                Button(actionTitle, action: action)
                    .font(Typo.subheadStrong)
                    .foregroundStyle(Theme.accent)
                    .frame(minHeight: Size.minTap)
                    .contentShape(Rectangle())
            }
        }
        .padding(.leading, Space.m)
        .padding(.trailing, actionTitle == nil ? Space.m : Space.s)
        .padding(.vertical, actionTitle == nil ? Space.s + 2 : 0)
        .frame(minHeight: Size.minTap)
        .background(tone.background, in: RoundedRectangle(cornerRadius: Radius.m, style: .continuous))
        .accessibilityElement(children: .combine)
    }
}

/// Leerzustand in einer Karte oder Liste: Symbol, Titel, kurzer Text, optional eine Aktion.
struct EmptyStateView: View {
    let symbol: String
    let title: String
    var message: String?
    var actionTitle: String?
    var actionSymbol = "plus"
    var action: (() -> Void)?

    var body: some View {
        VStack(spacing: Space.m) {
            IconBadge(symbol: symbol, size: 48)
            VStack(spacing: Space.xs) {
                Text(title)
                    .font(Typo.headline)
                    .foregroundStyle(Theme.textPrimary)
                    .multilineTextAlignment(.center)
                if let message {
                    Text(message)
                        .font(Typo.subhead)
                        .foregroundStyle(Theme.textSecondary)
                        .multilineTextAlignment(.center)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            if let actionTitle, let action {
                Button(action: action) { Label(actionTitle, systemImage: actionSymbol) }
                    .estrichButton(.secondary)
                    .padding(.top, Space.xs)
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, Space.xl)
        .padding(.horizontal, Space.l)
    }
}

// MARK: Fluss-Layout

/// Einfacher Fließumbruch (Chips, Kenndaten im Kopf).
struct ChipFlow<Content: View>: View {
    var spacing: CGFloat = 6
    @ViewBuilder let content: () -> Content

    var body: some View {
        FlowLayout(spacing: spacing) { content() }
    }
}

struct FlowLayout: Layout {
    var spacing: CGFloat

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 320
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowHeight: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > 0, x + size.width > width {
                x = 0
                y += rowHeight + spacing
                rowHeight = 0
            }
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
        return CGSize(width: width, height: y + rowHeight)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        var y = bounds.minY
        var rowHeight: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > bounds.minX, x + size.width > bounds.maxX {
                x = bounds.minX
                y += rowHeight + spacing
                rowHeight = 0
            }
            subview.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(size))
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
    }
}
