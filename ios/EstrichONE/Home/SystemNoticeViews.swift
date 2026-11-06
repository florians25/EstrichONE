import EstrichCore
import SwiftUI

// Systemhinweise des Betreibers bzw. der Verwaltung (geplante Wartung, Störungen, Informationen) als Karten:
// Symbol und Farbe je Art, Titel mit Countdown („in 2 Tagen“, „läuft seit 10 Min.“), Zeitraum, Kurztext;
// „Details“ öffnet ein Blatt mit allem. Ausblenden merkt sich die App je Benutzer – Störungen und laufende Wartung nicht.

/// Ausgeblendete Hinweise je Benutzer (höchstens 100 Schlüssel). Ein geänderter Hinweis hat einen neuen Schlüssel.
@MainActor
@Observable
final class NoticeDismissals {
    static let shared = NoticeDismissals()

    private(set) var keys: Set<String> = []
    private var user: String?

    private var storageKey: String { "systemNotices.dismissed.\(user ?? "anon")" }

    func load(user: String) {
        guard user != self.user else { return }
        self.user = user
        keys = Set(UserDefaults.standard.stringArray(forKey: storageKey) ?? [])
    }

    func dismiss(_ id: String) {
        var list = (UserDefaults.standard.stringArray(forKey: storageKey) ?? []).filter { $0 != id }
        list.append(id)
        list = Array(list.suffix(100))
        UserDefaults.standard.set(list, forKey: storageKey)
        keys = Set(list)
    }
}

extension NoticeItem.Tone {
    var tint: Color {
        switch self {
        case .information: Theme.accent
        case .warning: Theme.warning
        case .error: Theme.danger
        }
    }

    var soft: Color {
        switch self {
        case .information: Theme.accentSoft
        case .warning: Theme.warningSoft
        case .error: Theme.dangerSoft
        }
    }
}

/// Startseite („Heute“): alle aktuellen Hinweise, mehrere zusammengefasst („2 weitere Hinweise“).
struct SystemNoticesBanner: View {
    let sync: SyncController
    @Environment(AppModel.self) private var model
    @Environment(\.shotScene) private var shotScene
    @State private var dismissals = NoticeDismissals.shared
    @State private var expanded = false
    @State private var detail: NoticeItem?

    var body: some View {
        TimelineView(.periodic(from: .now, by: 60)) { context in
            content(now: context.date)
        }
        .sheet(item: $detail) { item in
            NoticeDetailSheet(item: item) { dismissals.dismiss(item.id) }
        }
        .onAppear { dismissals.load(user: model.active.map { "\($0.user.id)" } ?? "anon") }
        .task(id: shotScene) {
            // Screenshot „hinweis-detail“: Details der geplanten Wartung.
            guard shotScene == "hinweis-detail" else {
                if shotScene != nil { detail = nil }
                return
            }
            try? await Task.sleep(for: .milliseconds(400))
            detail = NoticePresentation.items(sync.notices, now: .now).first { $0.kind == .maintenance || $0.kind == .maintenanceSoon }
        }
    }

    @ViewBuilder
    private func content(now: Date) -> some View {
        // Laufende Wartung meldet die Startseite schon oben („Wartung des Servers“) – hier nicht doppelt.
        let items = NoticePresentation.items(sync.notices, now: now)
            .filter { !($0.kind == .maintenanceRunning && sync.maintenanceMessage != nil) }
        let selection = NoticePresentation.selection(items, dismissed: dismissals.keys, expanded: expanded || shotScene == "hinweise")
        if !selection.shown.isEmpty {
            VStack(spacing: Space.s) {
                ForEach(selection.shown) { item in
                    SystemNoticeCard(item: item, onDetails: { detail = item },
                                     onDismiss: item.isDismissible ? { withAnimation(.snappy) { dismissals.dismiss(item.id) } } : nil)
                        .transition(.opacity.combined(with: .move(edge: .top)))
                }
                if selection.hidden > 0 || (expanded && selection.shown.count > 1) {
                    Button {
                        withAnimation(.snappy) { expanded.toggle() }
                    } label: {
                        Label(selection.hidden > 0 ? NoticePresentation.moreText(selection.hidden) : "Weniger anzeigen",
                              systemImage: selection.hidden > 0 ? "chevron.down" : "chevron.up")
                            .font(Typo.metaStrong)
                            .foregroundStyle(Theme.accent)
                            .frame(minHeight: Size.minTap)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.leading, Space.xs)
                    .accessibilityIdentifier("systemNoticesMore")
                }
            }
            .accessibilityIdentifier("systemNotices")
        }
    }
}

/// Eine Hinweiskarte: farbiger Rand links, Symbol, Titel mit Countdown, Zeitraum, Kurztext, „Details“ und ggf. Ausblenden.
struct SystemNoticeCard: View {
    let item: NoticeItem
    var onDetails: (() -> Void)?
    var onDismiss: (() -> Void)?

    var body: some View {
        let tone = item.tone
        HStack(alignment: .top, spacing: Space.m) {
            IconBadge(symbol: item.symbol, tint: tone.tint, background: tone.soft, size: 36)
            VStack(alignment: .leading, spacing: Space.xs) {
                Text(item.title)
                    .font(Typo.subheadStrong)
                    .foregroundStyle(Theme.textPrimary)
                    .fixedSize(horizontal: false, vertical: true)
                let chips = [item.relative, item.remaining].filter { !$0.isEmpty }
                if !chips.isEmpty {
                    HStack(spacing: Space.xs) {
                        ForEach(chips, id: \.self) { NoticeChip(text: $0, tone: tone) }
                    }
                }
                if !item.period.isEmpty {
                    Label(item.period, systemImage: "calendar")
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textPrimary)
                        .labelStyle(NoticeMetaLabelStyle())
                }
                if !item.text.isEmpty {
                    Text(item.text)
                        .font(Typo.meta)
                        .foregroundStyle(Theme.textSecondary)
                        .lineLimit(3)
                        .fixedSize(horizontal: false, vertical: true)
                }
                if let onDetails {
                    Button(action: onDetails) {
                        HStack(spacing: 3) {
                            Text("Details")
                            Image(systemName: "chevron.right").font(.caption2.weight(.bold))
                        }
                        .font(Typo.metaStrong)
                        .foregroundStyle(Theme.accent)
                        .frame(minHeight: 30)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("Details: \(item.title)")
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            if let onDismiss {
                Button(action: onDismiss) {
                    Image(systemName: "xmark")
                        .font(.caption.weight(.bold))
                        .foregroundStyle(Theme.textSecondary)
                        .frame(width: Size.minTap, height: Size.minTap)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Hinweis ausblenden")
                .padding(.vertical, -10)
                .padding(.trailing, -10)
            }
        }
        .padding(.vertical, Space.m)
        .padding(.leading, Space.m + 4)
        .padding(.trailing, Space.m)
        .background(Theme.surface)
        .overlay(alignment: .leading) { Rectangle().fill(tone.tint).frame(width: 4) }
        .clipShape(RoundedRectangle(cornerRadius: Radius.m, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: Radius.m, style: .continuous).strokeBorder(tone.tint.opacity(0.22), lineWidth: 1)
        }
        .contentShape(RoundedRectangle(cornerRadius: Radius.m, style: .continuous))
        .onTapGesture { onDetails?() }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("\(item.label): \(item.title)")
        .accessibilityIdentifier("systemNotice-\(item.kind.rawValue)")
    }
}

/// Kleine Kapsel mit relativer Angabe („in 2 Tagen“, „noch 20 Min.“).
struct NoticeChip: View {
    let text: String
    let tone: NoticeItem.Tone

    var body: some View {
        Text(text)
            .font(Typo.labelStrong)
            .foregroundStyle(tone.tint)
            .padding(.horizontal, Space.s)
            .padding(.vertical, 3)
            .background(tone.soft, in: Capsule())
            .overlay { Capsule().strokeBorder(tone.tint.opacity(0.3), lineWidth: 0.5) }
            .fixedSize()
    }
}

private struct NoticeMetaLabelStyle: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 5) {
            configuration.icon.foregroundStyle(Theme.textSecondary).font(.caption)
            configuration.title.fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// Details eines Hinweises: großes Symbol, Art, Titel, Countdown, Zeitraum, Herkunft und der vollständige Text.
struct NoticeDetailSheet: View {
    let item: NoticeItem
    var onDismiss: (() -> Void)?
    @Environment(\.dismiss) private var close

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Space.l) {
                    HStack(alignment: .center, spacing: Space.m) {
                        IconBadge(symbol: item.symbol, tint: item.tone.tint, background: item.tone.soft, size: 52)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(item.label.uppercased())
                                .font(Typo.labelStrong)
                                .tracking(0.6)
                                .foregroundStyle(item.tone.tint)
                            Text(item.title)
                                .font(Typo.title)
                                .foregroundStyle(Theme.textPrimary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    .accessibilityElement(children: .combine)

                    if item.isTimed {
                        TimelineView(.periodic(from: .now, by: 30)) { context in
                            countdown(now: context.date)
                        }
                    }

                    VStack(spacing: 0) {
                        if !item.period.isEmpty {
                            row("Zeitraum", value: item.period, symbol: "calendar")
                        }
                        if !item.source.isEmpty {
                            if !item.period.isEmpty { InsetDivider() }
                            row("Hinweis von", value: item.source, symbol: "person.crop.circle")
                        }
                    }
                    .background(Theme.surface, in: RoundedRectangle(cornerRadius: Radius.l, style: .continuous))

                    if !item.text.isEmpty {
                        Text(item.text)
                            .font(Typo.body)
                            .foregroundStyle(Theme.textPrimary)
                            .fixedSize(horizontal: false, vertical: true)
                            .textSelection(.enabled)
                    }

                    if item.kind == .maintenance || item.kind == .maintenanceSoon || item.kind == .maintenanceRunning {
                        Callout(tone: .neutral, title: "Während der Wartung",
                                message: "Sie können in der App weiterarbeiten. Fotos und Einträge bleiben sicher auf dem Gerät und werden danach automatisch übertragen.",
                                symbol: "lock.shield")
                    }

                    if let onDismiss, item.isDismissible {
                        Button {
                            onDismiss()
                            close()
                        } label: {
                            Label("Hinweis ausblenden", systemImage: "eye.slash")
                        }
                        .estrichButton(.secondary, fullWidth: true)
                        .accessibilityIdentifier("noticeDismissButton")
                    }
                }
                .padding(Space.l)
                .frame(maxWidth: 560)
                .frame(maxWidth: .infinity)
            }
            .background(Theme.canvas)
            .navigationTitle("Systemhinweis")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fertig") { close() }
                }
            }
        }
        .presentationDetents([.medium, .large])
        .presentationDragIndicator(.visible)
        .accessibilityIdentifier("noticeDetail")
    }

    /// Großer Countdown: „Beginnt in 2 Tagen“ bzw. „Läuft seit 10 Min. · noch 20 Min.“.
    private func countdown(now: Date) -> some View {
        let relative = NoticePresentation.relative(start: item.start, end: item.end, now: now)
        let remaining = NoticePresentation.remaining(end: item.end, now: now)
        let upcoming = item.start.map { $0 > now } ?? false
        let headline = upcoming ? "Beginnt" : (relative.isEmpty ? "Gültig" : "Status")
        let value = relative.isEmpty ? remaining : relative
        return HStack(alignment: .center, spacing: Space.m) {
            Image(systemName: upcoming ? "hourglass" : "clock.badge.checkmark")
                .font(.title3.weight(.semibold))
                .foregroundStyle(item.tone.tint)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(headline)
                    .font(Typo.label)
                    .foregroundStyle(Theme.textSecondary)
                Text(value)
                    .font(Typo.time)
                    .foregroundStyle(Theme.textPrimary)
                    .contentTransition(.numericText())
            }
            Spacer(minLength: 0)
            if !relative.isEmpty, !remaining.isEmpty {
                NoticeChip(text: remaining, tone: item.tone)
            }
        }
        .padding(Space.l)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(item.tone.soft, in: RoundedRectangle(cornerRadius: Radius.l, style: .continuous))
        .accessibilityElement(children: .combine)
    }

    private func row(_ title: String, value: String, symbol: String) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: Space.m) {
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
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.horizontal, Space.l)
        .padding(.vertical, Space.m)
        .frame(minHeight: 46)
        .accessibilityElement(children: .combine)
    }
}
