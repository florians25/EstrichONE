import EstrichCore
import SwiftUI

/// Update-Ablauf (deploy/README.md): Leiste mit Countdown in der Ankündigung (wie im Web).
struct UpdateAnnounceBanner: View {
    let sync: SyncController

    var body: some View {
        if let state = sync.update {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                let screen = UpdatePresentation.screen(state, now: context.date)
                if screen.banner {
                    HStack(alignment: .top, spacing: Space.s) {
                        Image(systemName: "icloud.and.arrow.up")
                            .foregroundStyle(Theme.warning)
                            .accessibilityHidden(true)
                        Text(screen.bannerText)
                            .font(.subheadline.weight(.semibold))
                            .monospacedDigit()
                            .foregroundStyle(Theme.textPrimary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .padding(.horizontal, Space.l)
                    .padding(.vertical, Space.m)
                    .background(Theme.warningSoft)
                    .accessibilityElement(children: .combine)
                    .accessibilityIdentifier("updateAnnounce")
                }
            }
        }
    }
}

/// Wartungsbildschirm über der ganzen App: Phase, Zielversion, Schritte, Countdown, Fortschritt (wie Web und Android).
struct UpdateMaintenanceScreen: View {
    let sync: SyncController

    var body: some View {
        if let state = sync.update {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                let screen = UpdatePresentation.screen(state, now: context.date)
                if screen.page {
                    content(screen)
                }
            }
        }
    }

    private func content(_ screen: UpdateScreen) -> some View {
        ZStack {
            Theme.canvas.ignoresSafeArea()
            VStack(alignment: .leading, spacing: Space.s) {
                Image(systemName: "icloud.and.arrow.up")
                    .font(.system(size: 34))
                    .foregroundStyle(Theme.accent)
                    .accessibilityHidden(true)
                Text(screen.title)
                    .font(.title2.bold())
                    .accessibilityIdentifier("updateTitle")
                if !screen.version.isEmpty {
                    Text("Zielversion \(screen.version)").foregroundStyle(Theme.textSecondary)
                }
                HStack(spacing: Space.s) {
                    ForEach(screen.steps) { step in
                        VStack(alignment: .leading, spacing: 6) {
                            Capsule()
                                .fill(step.state == .done ? Theme.success : step.state == .now ? Theme.accent : Theme.fillStrong)
                                .frame(height: 4)
                            Text(step.label)
                                .font(.caption.weight(step.state == .now ? .bold : .regular))
                                .foregroundStyle(step.state == .now ? Theme.textPrimary : Theme.textSecondary)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(.top, Space.m)
                Text(screen.countdownLabel)
                    .foregroundStyle(Theme.textSecondary)
                    .padding(.top, Space.s)
                if !screen.countdown.isEmpty {
                    Text(screen.countdown)
                        .font(.system(size: 40, weight: .bold))
                        .monospacedDigit()
                        .accessibilityIdentifier("updateCountdown")
                }
                ProgressView(value: screen.progress)
                    .tint(screen.failed ? Theme.danger : Theme.accent)
                Text(screen.text)
                    .font(.body)
                    .padding(.top, Space.xs)
                Text(sync.updateOffline
                     ? "Server wird gerade getauscht – die App wartet automatisch."
                     : "Die App prüft alle 5 Sekunden und geht nach der Freigabe zur Anmeldung.")
                    .font(.footnote)
                    .foregroundStyle(Theme.textSecondary)
            }
            .padding(Space.xl)
            .frame(maxWidth: 480)
            .background(Theme.surface, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
            .shadow(color: .black.opacity(0.08), radius: 16, y: 6)
            .padding(Space.l)
        }
        .accessibilityIdentifier("updateScreen")
    }
}
