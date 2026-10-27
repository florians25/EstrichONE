import EstrichCore
import SwiftUI

/// Leiste oben auf allen Hauptseiten: rot bei Ausfall des Backup-Servers, gelb beim Archivserver (nur wenn eingerichtet),
/// darunter „Nur Ansicht“ nach 5 Minuten ohne Server. Ruhig, aber nicht zu übersehen; ohne Störung unsichtbar.
struct SystemStatusStrip: View {
    let sync: SyncController
    @State private var confirmsOverride = false

    var body: some View {
        let banners = sync.healthBanners
        let longOffline = sync.connection.isLongOffline(at: .now)
        if !banners.isEmpty || sync.isReadOnly || longOffline {
            VStack(spacing: 0) {
                ForEach(banners) { banner in
                    StripRow(tone: banner.level == .critical ? .danger : .warning,
                             symbol: banner.level == .critical ? "exclamationmark.octagon.fill" : "exclamationmark.triangle.fill",
                             text: banner.text)
                        .accessibilityIdentifier(banner.level == .critical ? "backupBanner" : "archiveBanner")
                }
                if sync.isReadOnly || longOffline {
                    TimelineView(.periodic(from: .now, by: 30)) { context in
                        readOnlyRow(now: context.date)
                    }
                }
            }
            // Farbe der ersten Zeile hinter der Statusleiste.
            .background(alignment: .top) {
                (banners.first.map { $0.level == .critical ? Theme.dangerSoft : Theme.warningSoft } ?? Theme.fill)
                    .ignoresSafeArea(edges: .top)
            }
            .readOnlyOverrideDialog(isPresented: $confirmsOverride, sync: sync)
        }
    }

    @ViewBuilder
    private func readOnlyRow(now: Date) -> some View {
        let since = OfflineDuration.text(since: sync.connection.lastContact, now: now)
        if sync.isReadOnly {
            StripRow(tone: .neutral, symbol: "eye", text: "Nur Ansicht – seit \(since) keine Verbindung zum Server. Erfassen und Ändern sind gesperrt.",
                     actionTitle: "Trotzdem arbeiten") { confirmsOverride = true }
                .accessibilityIdentifier("readOnlyBanner")
        } else {
            StripRow(tone: .neutral, symbol: "wifi.exclamationmark", text: "Offline auf eigene Gefahr – seit \(since) keine Verbindung.",
                     actionTitle: "Nur Ansicht") { sync.endReadOnlyOverride() }
                .accessibilityIdentifier("readOnlyOverriddenBanner")
        }
    }
}

/// „Trotzdem offline arbeiten (auf eigene Gefahr)“ mit Bestätigung.
extension View {
    func readOnlyOverrideDialog(isPresented: Binding<Bool>, sync: SyncController?) -> some View {
        confirmationDialog("Trotzdem offline arbeiten?", isPresented: isPresented, titleVisibility: .visible) {
            Button("Auf eigene Gefahr offline arbeiten", role: .destructive) { sync?.overrideReadOnly() }
                .accessibilityIdentifier("confirmReadOnlyOverride")
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text("Der Server ist seit über 5 Minuten nicht erreichbar. Neue Fotos und Änderungen bleiben nur auf diesem Gerät, bis die Verbindung wieder steht – geht das Gerät verloren oder wird die App gelöscht, sind sie weg. Andere sehen Ihre Änderungen erst danach.")
        }
    }
}

/// „12 Min.“, „2 Std. 5 Min.“
enum OfflineDuration {
    static func text(since: Date?, now: Date) -> String {
        guard let since else { return "einigen Minuten" }
        let minutes = max(1, Int(now.timeIntervalSince(since) / 60))
        if minutes < 60 { return "\(minutes) Min." }
        let hours = minutes / 60
        if hours >= 48 { return "\(hours / 24) Tagen" }
        return minutes % 60 == 0 ? "\(hours) Std." : "\(hours) Std. \(minutes % 60) Min."
    }
}

/// Eine Zeile der Statusleiste: volle Breite, getönte Fläche, feine Trennlinie unten.
private struct StripRow: View {
    let tone: Callout.Tone
    let symbol: String
    let text: String
    var actionTitle: String?
    var action: (() -> Void)?

    var body: some View {
        HStack(alignment: .center, spacing: Space.s) {
            Image(systemName: symbol)
                .font(.footnote.weight(.semibold))
                .foregroundStyle(tone.foreground)
                .accessibilityHidden(true)
            Text(text)
                .font(.footnote.weight(tone == .danger ? .semibold : .regular))
                .foregroundStyle(Theme.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
            if let actionTitle, let action {
                Button(actionTitle, action: action)
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(Theme.accent)
                    .padding(.horizontal, Space.s + 2)
                    .padding(.vertical, 5)
                    .background(Theme.surface, in: Capsule())
                    .fixedSize()
                    .contentShape(Capsule())
            }
        }
        .padding(.horizontal, Space.l)
        .padding(.vertical, Space.s)
        .frame(maxWidth: .infinity)
        .background(tone == .neutral ? Theme.fill : tone.background)
        .overlay(alignment: .bottom) { Rectangle().fill(Theme.separator).frame(height: 0.5) }
        .accessibilityElement(children: .combine)
    }
}

/// Wartung sperrt die App für alle außer Administratoren: Titel, Text, geplantes Ende und der Stand auf dem Gerät.
/// Nichts geht verloren – die Outbox bleibt, und nach dem Ende gibt sich die App selbst wieder frei.
struct MaintenanceLockView: View {
    let sync: SyncController
    @Environment(AppModel.self) private var model
    @State private var checking = false
    @State private var checkedAt = Date.now

    var body: some View {
        ZStack {
            Theme.canvas.ignoresSafeArea()
            ScrollView {
                VStack(spacing: Space.l) {
                    IconBadge(symbol: "wrench.and.screwdriver.fill", tint: Theme.warning, background: Theme.warningSoft, size: 56)
                        .padding(.top, Space.xxl)
                    VStack(spacing: Space.s) {
                        Text("Wartungsarbeiten")
                            .font(Typo.title)
                            .foregroundStyle(Theme.textPrimary)
                        Text(sync.maintenanceMessage ?? "EstrichONE wird gerade gewartet.")
                            .font(Typo.body)
                            .foregroundStyle(Theme.textSecondary)
                            .multilineTextAlignment(.center)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    VStack(spacing: 0) {
                        if sync.maintenanceEnd != nil {
                            row("Voraussichtlich bis", value: endText, symbol: "clock")
                            InsetDivider()
                        }
                        row("Auf diesem Gerät", value: pendingText, symbol: "lock.shield")
                        InsetDivider()
                        row("Zuletzt geprüft", value: checkedAt.formatted(date: .omitted, time: .shortened) + " Uhr", symbol: "arrow.clockwise")
                    }
                    .background(Theme.surface, in: RoundedRectangle(cornerRadius: Radius.l, style: .continuous))

                    Button {
                        Task { await check() }
                    } label: {
                        if checking { ProgressView() } else { Label("Erneut prüfen", systemImage: "arrow.clockwise") }
                    }
                    .estrichButton(.secondary)
                    .disabled(checking)
                    .accessibilityIdentifier("maintenanceRetry")

                    Text("Die App gibt sich nach der Wartung automatisch wieder frei. Ihre Daten bleiben verschlüsselt auf dem Gerät und werden danach übertragen.")
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                        .multilineTextAlignment(.center)
                        .fixedSize(horizontal: false, vertical: true)
                    if model.isDemo {
                        Button("Wartung beenden (Demo)") {
                            DemoSwitches.shared.maintenance = false
                            Task { await check() }
                        }
                        .font(Typo.subheadStrong)
                        .foregroundStyle(Theme.accent)
                    }
                }
                .padding(.horizontal, Space.l)
                .padding(.bottom, Space.xxl)
                .frame(maxWidth: 440)
                .frame(maxWidth: .infinity)
            }
        }
        .accessibilityIdentifier("maintenanceLock")
        // Kurz nach dem geplanten Ende selbst nachfragen (zusätzlich zum Abgleich alle 30 Sekunden).
        .task(id: sync.maintenanceEnd) {
            guard let end = sync.maintenanceEnd, end > .now else { return }
            try? await Task.sleep(for: .seconds(end.timeIntervalSinceNow + 5))
            await check()
        }
        .onChange(of: sync.revision) { _, _ in checkedAt = .now }
    }

    private var endText: String {
        guard let end = sync.maintenanceEnd else { return "unbekannt" }
        let time = end.formatted(date: .omitted, time: .shortened) + " Uhr"
        return Calendar.current.isDateInToday(end) ? time : end.formatted(.dateTime.weekday(.abbreviated).day().month(.abbreviated)) + ", " + time
    }

    private var pendingText: String {
        guard let summary = sync.summary else { return "Alles gesichert" }
        let count = summary.pendingOperations + summary.pendingUploads
        switch count {
        case 0: return "Alles übertragen"
        case 1: return "1 Änderung wartet sicher"
        default: return "\(count) Änderungen warten sicher"
        }
    }

    private func row(_ title: String, value: String, symbol: String) -> some View {
        HStack(spacing: Space.m) {
            Image(systemName: symbol)
                .font(.subheadline.weight(.medium))
                .foregroundStyle(Theme.textSecondary)
                .frame(width: 20)
                .accessibilityHidden(true)
            Text(title)
                .font(Typo.subhead)
                .foregroundStyle(Theme.textSecondary)
            Spacer(minLength: Space.s)
            Text(value)
                .font(Typo.subheadStrong)
                .foregroundStyle(Theme.textPrimary)
                .multilineTextAlignment(.trailing)
        }
        .padding(.horizontal, Space.l)
        .frame(minHeight: 46)
        .accessibilityElement(children: .combine)
    }

    private func check() async {
        checking = true
        await sync.run()
        checkedAt = .now
        checking = false
    }
}
