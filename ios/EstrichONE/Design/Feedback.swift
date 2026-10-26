import SwiftUI

// MARK: Platzhalter beim Laden

/// Grauer Balken als Platzhalter (statt springender Inhalte beim ersten Laden).
struct SkeletonBar: View {
    var width: CGFloat?
    var height: CGFloat = 14

    var body: some View {
        RoundedRectangle(cornerRadius: height / 2.4, style: .continuous)
            .fill(Theme.fillStrong)
            .frame(width: width, height: height)
            .frame(maxWidth: width == nil ? .infinity : nil, alignment: .leading)
    }
}

/// Sanftes Pulsieren für Platzhalter; bei „Bewegung reduzieren“ ruhig.
struct Pulsing: ViewModifier {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var dim = false

    func body(content: Content) -> some View {
        content
            .opacity(dim ? 0.45 : 1)
            .onAppear {
                guard !reduceMotion else { return }
                withAnimation(.easeInOut(duration: 0.9).repeatForever(autoreverses: true)) { dim = true }
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("Wird geladen")
    }
}

extension View {
    func pulsing() -> some View { modifier(Pulsing()) }
}

/// Karte als Platzhalter mit einigen Zeilen.
struct SkeletonCard: View {
    var lines = 3
    var showsHeader = true

    var body: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            if showsHeader {
                SkeletonBar(width: 120, height: 18)
            }
            ForEach(0..<lines, id: \.self) { index in
                SkeletonBar(width: index == lines - 1 ? 180 : nil, height: 14)
            }
        }
        .padding(Space.l)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
        .pulsing()
    }
}

/// Liste als Platzhalter (z. B. Bauakte beim ersten Öffnen).
struct SkeletonList: View {
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Space.l) {
                VStack(alignment: .leading, spacing: Space.s) {
                    SkeletonBar(width: 90, height: 14)
                    SkeletonBar(width: 240, height: 26)
                    SkeletonBar(width: 160, height: 14)
                }
                .padding(.horizontal, Space.xs)
                SkeletonCard(lines: 3, showsHeader: false)
                HStack(spacing: Space.s) {
                    ForEach(0..<4, id: \.self) { _ in
                        RoundedRectangle(cornerRadius: Radius.l, style: .continuous).fill(Theme.fill).frame(height: 64)
                    }
                }
                SkeletonCard(lines: 4)
                SkeletonCard(lines: 5)
            }
            .padding(Space.l)
        }
        .background(Theme.canvas)
        .pulsing()
    }
}

// MARK: Fortschritt

/// Fortschrittsbalken (0…1).
struct ProgressBar: View {
    let value: Double
    var tint: Color = Theme.accent
    var height: CGFloat = 6

    var body: some View {
        let clamped = min(1, max(0, value))
        Capsule()
            .fill(Theme.fillStrong)
            .frame(height: height)
            .overlay(alignment: .leading) {
                GeometryReader { proxy in
                    Capsule()
                        .fill(tint)
                        .frame(width: max(height, proxy.size.width * clamped))
                        .opacity(clamped > 0 ? 1 : 0)
                }
            }
            .animation(.easeOut(duration: 0.3), value: clamped)
            .accessibilityElement()
            .accessibilityLabel("Fortschritt")
            .accessibilityValue("\(Int((clamped * 100).rounded())) Prozent")
    }
}

/// Schritte eines Assistenten: „Schritt 2 von 4 · Baustelle“ und Segmente.
struct StepProgress: View {
    let steps: [String]
    let current: Int

    var body: some View {
        let index = min(max(current, 0), max(steps.count - 1, 0))
        VStack(alignment: .leading, spacing: Space.s) {
            HStack(alignment: .firstTextBaseline) {
                Text("Schritt \(index + 1) von \(steps.count)")
                    .font(Typo.metaStrong)
                    .foregroundStyle(Theme.accent)
                Spacer()
                if steps.indices.contains(index) {
                    Text(steps[index]).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                }
            }
            HStack(spacing: 4) {
                ForEach(steps.indices, id: \.self) { step in
                    Capsule()
                        .fill(step <= index ? Theme.accent : Theme.fillStrong)
                        .frame(height: 4)
                }
            }
        }
        .animation(.easeOut(duration: 0.25), value: index)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Schritt \(index + 1) von \(steps.count)\(steps.indices.contains(index) ? ": \(steps[index])" : "")")
        .accessibilityIdentifier("wizardProgress")
    }
}

// MARK: Rückmeldung

/// Kurze Bestätigung am unteren Rand („Foto gespeichert …“), verschwindet nach 3 Sekunden; mit Haptik.
struct ToastModifier: ViewModifier {
    @Binding var message: String?

    func body(content: Content) -> some View {
        content
            .overlay(alignment: .bottom) {
                if let message {
                    HStack(spacing: Space.s) {
                        Image(systemName: "checkmark.circle.fill")
                            .foregroundStyle(Theme.success)
                            .accessibilityHidden(true)
                        Text(message)
                            .font(Typo.subheadStrong)
                            .foregroundStyle(Theme.textPrimary)
                            .multilineTextAlignment(.leading)
                    }
                    .padding(.horizontal, Space.l)
                    .padding(.vertical, Space.m)
                    .background(.regularMaterial, in: Capsule())
                    .overlay(Capsule().strokeBorder(Theme.separator))
                    .shadow(color: .black.opacity(0.12), radius: 12, y: 4)
                    .padding(.horizontal, Space.l)
                    .padding(.bottom, Space.l)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                    .accessibilityIdentifier("bauakteToast")
                    .task(id: message) {
                        try? await Task.sleep(for: .seconds(3))
                        if self.message == message { self.message = nil }
                    }
                }
            }
            .animation(.spring(response: 0.35, dampingFraction: 0.85), value: message)
            .sensoryFeedback(.success, trigger: message) { _, new in new != nil }
    }
}

extension View {
    func toast(_ message: Binding<String?>) -> some View { modifier(ToastModifier(message: message)) }
}
