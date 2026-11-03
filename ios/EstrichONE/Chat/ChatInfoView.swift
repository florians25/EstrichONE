import EstrichCore
import PhotosUI
import SwiftUI
import UIKit

/// Info zur Unterhaltung wie im Messenger: Bild, Name, Beschreibung, Bauakte bzw. Kolonne, Medien, Suche,
/// Stummschalten, Mitglieder mit Admins; Gruppenadmins ändern Bild, Name, Beschreibung und Mitglieder.
struct ChatInfoView: View {
    let conversationId: UUID
    let fallbackTitle: String
    let kind: ChatKind
    let reveal: (UUID) -> Void
    let changed: () -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var detail: ChatConversationDetail?
    @State private var failure: String?
    @State private var search = ""
    @State private var hits: [ChatSearchHit] = []
    @State private var editing = false
    @State private var addingMembers = false
    @State private var confirmsLeave = false
    @State private var avatarItem: PhotosPickerItem?
    @State private var busy = false
    @State private var toast: String?

    private var userId: UUID? { model.active?.user.id }
    private var canManage: Bool { detail?.canManage == true && model.active?.sync?.isReadOnly != true }
    private var chatKind: ChatKind { detail?.chatKind ?? kind }

    var body: some View {
        infoList
        .listStyle(.insetGrouped)
        .navigationTitle(chatKind == .direct ? "Kontaktinfo" : "Gruppeninfo")
        .navigationBarTitleDisplayMode(.inline)
        .overlay { if detail == nil, failure == nil { ProgressView() } }
        .task { await load() }
        .onChange(of: search) { _, text in if text.isEmpty { hits = [] } }
        .onChange(of: avatarItem) { _, item in
            guard let item else { return }
            Task { await setAvatar(item) }
        }
        .navigationDestination(isPresented: $editing) {
            if let detail {
                ChatGroupEditor(mode: .edit(detail)) { _ in
                    editing = false
                    Task {
                        await load()
                        changed()
                    }
                }
            }
        }
        .sheet(isPresented: $addingMembers) {
            ChatMemberPicker(title: "Mitglieder hinzufügen", excluded: Set(detail?.members.map(\.id) ?? [])) { added in
                addingMembers = false
                Task { await update(ChatGroupChange(memberIds: (detail?.members.map(\.id) ?? []) + added.map(\.id)), message: "Hinzugefügt") }
            }
        }
        .confirmationDialog("Gruppe verlassen?", isPresented: $confirmsLeave, titleVisibility: .visible) {
            Button("Verlassen", role: .destructive) { Task { await leave() } }
            Button("Abbrechen", role: .cancel) {}
        } message: {
            Text(chatKind == .crew || chatKind == .site
                 ? "Sie erhalten keine Nachrichten mehr. Solange Sie zur Kolonne bzw. Bauakte gehören, kann die Verwaltung Sie wieder hinzufügen."
                 : "Sie erhalten keine Nachrichten mehr aus dieser Gruppe.")
        }
        .toast($toast)
    }

    private var infoList: some View {
        List {
            Section {
                VStack(spacing: Space.s) {
                    ZStack(alignment: .bottomTrailing) {
                        ChatAvatar(title: detail?.title ?? fallbackTitle, kind: chatKind, avatarFileId: detail?.avatarFileId, size: 96)
                        if canManage, chatKind != .direct {
                            PhotosPicker(selection: $avatarItem, matching: .images) {
                                Image(systemName: "camera.fill")
                                    .font(.system(size: 13, weight: .semibold))
                                    .foregroundStyle(.white)
                                    .frame(width: 30, height: 30)
                                    .background(Circle().fill(Theme.accentFill))
                            }
                            .accessibilityLabel("Gruppenbild ändern")
                        }
                    }
                    Text(detail?.title ?? fallbackTitle).font(.title2.weight(.semibold)).multilineTextAlignment(.center)
                    Text(subtitle).font(Typo.subhead).foregroundStyle(Theme.textSecondary)
                    if let description = detail?.description, !description.isEmpty {
                        Text(description)
                            .font(Typo.body)
                            .multilineTextAlignment(.center)
                            .padding(.top, Space.xs)
                    }
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, Space.s)
                .listRowBackground(Color.clear)
            }
            if let failure {
                NoticeLine(tone: .danger, text: failure).listRowInsets(EdgeInsets()).listRowBackground(Color.clear)
            }
            if let bauakteId = detail?.bauakteId {
                Section {
                    NavigationLink { BauakteDetailView(bauakteId: bauakteId) } label: {
                        Label(detail?.bauakteTitle ?? "Bauakte öffnen", systemImage: "folder")
                    }
                } footer: {
                    Text("Baustellengruppe: Team und Bauleitung der Bauakte sind automatisch Mitglied. Fotos lassen sich mit langem Drücken in die Bauakte übernehmen.")
                }
            } else if let crew = detail?.crewName {
                Section {
                    Label(crew, systemImage: "truck.box")
                } footer: {
                    Text("Kolonnengruppe: Wer in der Kolonne ist, gehört automatisch dazu.")
                }
            }
            Section {
                NavigationLink { ChatMediaView(conversationId: conversationId) } label: {
                    Label("Medien und Dokumente", systemImage: "photo.on.rectangle")
                }
                .accessibilityIdentifier("chatMediaLink")
                muteRow
            }
            Section("In diesem Chat suchen") {
                TextField("Suchbegriff", text: $search)
                    .submitLabel(.search)
                    .onSubmit { Task { await runSearch() } }
                ForEach(hits) { hit in
                    Button { reveal(hit.message.id) } label: {
                        VStack(alignment: .leading, spacing: 2) {
                            Text("\(hit.message.senderName ?? "") · \(ChatTime.short(hit.message.createdAt))").font(Typo.label).foregroundStyle(Theme.textSecondary)
                            Text(hit.message.summary).font(Typo.subhead).foregroundStyle(Theme.textPrimary).lineLimit(2)
                        }
                    }
                }
            }
            if chatKind != .direct || (detail?.members.count ?? 0) > 2 {
                membersSection
            }
            if chatKind != .direct {
                Section {
                    if canManage {
                        Button("Gruppe bearbeiten", systemImage: "pencil") { editing = true }
                            .accessibilityIdentifier("editGroupButton")
                    }
                    Button("Gruppe verlassen", systemImage: "rectangle.portrait.and.arrow.right", role: .destructive) { confirmsLeave = true }
                        .foregroundStyle(Theme.danger)
                }
            }
        }
    }

    private var subtitle: String {
        guard let detail else { return chatKind.title }
        if chatKind == .direct { return "Einzelchat" }
        return "\(chatKind.title) · \(detail.members.count) Mitglieder"
    }

    @ViewBuilder
    private var muteRow: some View {
        if let until = detail?.mutedUntil, until > Date() {
            Button { Task { await mute(nil, on: false) } } label: {
                LabeledContent {
                    Text(until == .distantFuture || until.timeIntervalSinceNow > 365 * 86400 ? "immer" : "bis \(until.formatted(date: .abbreviated, time: .shortened))")
                } label: {
                    Label("Stummschaltung aufheben", systemImage: "bell.slash")
                }
            }
        } else {
            Menu {
                ForEach(ChatMuteOption.allCases) { option in
                    Button(option.title) { Task { await mute(option, on: true) } }
                }
            } label: {
                Label("Stummschalten", systemImage: "bell")
            }
            .accessibilityIdentifier("chatMuteMenu")
        }
    }

    private var membersSection: some View {
        Section {
            if canManage {
                Button("Mitglieder hinzufügen", systemImage: "person.badge.plus") { addingMembers = true }
            }
            ForEach(sortedMembers) { member in
                HStack(spacing: Space.m) {
                    Avatar(name: member.displayName, size: 34)
                    VStack(alignment: .leading, spacing: 0) {
                        Text(member.id == userId ? "\(member.displayName) (Ich)" : member.displayName).font(Typo.body)
                        if let note = memberNote(member) {
                            Text(note).font(Typo.meta).foregroundStyle(Theme.textSecondary)
                        }
                    }
                    Spacer()
                    if member.isAdmin {
                        Text("Admin")
                            .font(Typo.labelStrong)
                            .foregroundStyle(Theme.accent)
                            .padding(.horizontal, Space.s)
                            .padding(.vertical, 3)
                            .background(Theme.accentSoft, in: Capsule())
                    }
                }
                .swipeActions {
                    if canManage, member.id != userId {
                        if !member.automatic {
                            Button("Entfernen", role: .destructive) {
                                Task { await update(ChatGroupChange(memberIds: (detail?.members.map(\.id) ?? []).filter { $0 != member.id }), message: "Entfernt") }
                            }
                        }
                        Button(member.isAdmin ? "Kein Admin" : "Zum Admin") {
                            let admins = (detail?.members.filter(\.isAdmin).map(\.id) ?? [])
                            let next = member.isAdmin ? admins.filter { $0 != member.id } : admins + [member.id]
                            Task { await update(ChatGroupChange(adminIds: next), message: member.isAdmin ? "Admin-Recht entzogen" : "Zum Admin gemacht") }
                        }
                        .tint(Theme.accentFill)
                    }
                }
            }
        } header: {
            Text("\(detail?.members.count ?? 0) Mitglieder")
        } footer: {
            if canManage { Text("Zum Entfernen oder für Admin-Rechte nach links wischen.") }
        }
    }

    private var sortedMembers: [ChatMember] {
        (detail?.members ?? []).sorted { lhs, rhs in
            if lhs.isAdmin != rhs.isAdmin { return lhs.isAdmin }
            return lhs.displayName.localizedCompare(rhs.displayName) == .orderedAscending
        }
    }

    private func memberNote(_ member: ChatMember) -> String? {
        let parts = [member.role, member.automatic ? (chatKind == .site ? "über die Bauakte" : "über die Kolonne") : nil].compactMap { $0 }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }

    private func load() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            detail = try await client.chatConversation(conversationId)
            failure = nil
        } catch {
            failure = error.serverMessage ?? error.message
        }
    }

    private func runSearch() async {
        let term = search.trimmingCharacters(in: .whitespaces)
        guard term.count >= 2, let client = model.active?.client else { return }
        hits = (try? await client.searchChat(term, conversationId: conversationId)) ?? []
        if hits.isEmpty { toast = "Keine Treffer" }
    }

    private func mute(_ option: ChatMuteOption?, on: Bool) async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            try await client.muteChat(conversationId, muted: on, until: option?.until())
            detail?.mutedUntil = on ? (option?.until() ?? .distantFuture) : nil
            toast = on ? "Stummgeschaltet – @Erwähnungen kommen trotzdem an" : "Mitteilungen wieder an"
            changed()
        } catch {
            failure = error.serverMessage ?? error.message
        }
    }

    private func update(_ change: ChatGroupChange, message: String) async {
        guard let client = model.active?.client else { return }
        busy = true
        defer { busy = false }
        do throws(APIError) {
            detail = try await client.updateChatGroup(conversationId, change)
            toast = message
            changed()
        } catch {
            failure = error.serverMessage ?? error.message
        }
    }

    private func setAvatar(_ item: PhotosPickerItem) async {
        avatarItem = nil
        guard let client = model.active?.client, let raw = try? await item.loadTransferable(type: Data.self) else { return }
        let jpeg = await Task.detached(priority: .userInitiated) { ChatImages.square(raw, side: 512) }.value
        guard let jpeg else { return }
        let id = UUID.v7()
        do throws(APIError) {
            try await client.uploadChatAttachment(id: id, conversationId: conversationId, kind: "avatar", name: "Gruppenbild.jpg",
                                                  contentType: "image/jpeg", data: jpeg)
            detail = try await client.updateChatGroup(conversationId, ChatGroupChange(avatarFileId: id))
            toast = "Gruppenbild geändert"
            changed()
        } catch {
            failure = error.serverMessage ?? error.message
        }
    }

    private func leave() async {
        guard let client = model.active?.client else { return }
        do throws(APIError) {
            try await client.leaveChat(conversationId)
            changed()
            dismiss()
        } catch {
            failure = error.serverMessage ?? error.message
        }
    }
}

/// Gruppe anlegen bzw. bearbeiten: Art, Name, Beschreibung, Kolonne bzw. Bauakte, Mitglieder, „nur Admins schreiben“.
struct ChatGroupEditor: View {
    enum Mode {
        case create
        case edit(ChatConversationDetail)
    }

    let mode: Mode
    let done: (ChatConversation?) -> Void

    @Environment(AppModel.self) private var model
    @State private var kind: ChatKind = .group
    @State private var title = ""
    @State private var description = ""
    @State private var onlyAdminsWrite = false
    @State private var members: [ChatMember] = []
    @State private var crews: [ChatCrew] = []
    @State private var crewId: UUID?
    @State private var bauakte: Bauakte?
    @State private var choosesBauakte = false
    @State private var choosesMembers = false
    @State private var busy = false
    @State private var failure: String?

    private var isCreate: Bool { if case .create = mode { true } else { false } }

    var body: some View {
        Form {
            if let failure {
                NoticeLine(tone: .danger, text: failure).listRowInsets(EdgeInsets()).listRowBackground(Color.clear)
            }
            if isCreate {
                Section {
                    Picker("Art", selection: $kind) {
                        ForEach([ChatKind.group, .crew, .site, .announcement], id: \.self) { kind in
                            Label(kind.title, systemImage: kind.symbol).tag(kind)
                        }
                    }
                    .accessibilityIdentifier("groupKindPicker")
                } footer: {
                    Text(kindHint)
                }
            }
            Section("Name und Beschreibung") {
                TextField("Gruppenname", text: $title)
                    .accessibilityIdentifier("groupTitleField")
                TextField("Beschreibung (optional)", text: $description, axis: .vertical)
                    .lineLimit(2...5)
            }
            if isCreate, kind == .crew {
                Section("Kolonne") {
                    Picker("Kolonne", selection: $crewId) {
                        Text("Bitte wählen").tag(UUID?.none)
                        ForEach(crews) { crew in Text(crew.name).tag(Optional(crew.id)) }
                    }
                }
            }
            if isCreate, kind == .site {
                Section("Bauakte") {
                    Button { choosesBauakte = true } label: {
                        LabeledContent("Baustelle", value: bauakte?.title ?? "Wählen …")
                    }
                }
            }
            Section {
                Toggle("Nur Admins dürfen schreiben", isOn: $onlyAdminsWrite)
                    .disabled(kind == .announcement)
            } footer: {
                Text("Für Ankündigungen wie „Alle Mitarbeiter“: Alle lesen, nur Admins schreiben.")
            }
            if isCreate {
                Section {
                    Button("Mitglieder wählen", systemImage: "person.badge.plus") { choosesMembers = true }
                    ForEach(members) { member in
                        HStack {
                            Avatar(name: member.displayName, size: 28)
                            Text(member.displayName)
                            Spacer()
                            Button { members.removeAll { $0.id == member.id } } label: {
                                Image(systemName: "minus.circle.fill").foregroundStyle(Theme.danger)
                            }
                            .buttonStyle(.plain)
                            .accessibilityLabel("\(member.displayName) entfernen")
                        }
                    }
                } header: {
                    Text(kind == .crew || kind == .site ? "Weitere Mitglieder (optional)" : "Mitglieder")
                } footer: {
                    Text("Auswahl aus den Personen Ihres Betriebs – keine Telefonnummern nötig.")
                }
            }
        }
        .navigationTitle(isCreate ? "Neue Gruppe" : "Gruppe bearbeiten")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                if busy { ProgressView() } else {
                    Button(isCreate ? "Anlegen" : "Sichern") { Task { await save() } }
                        .disabled(!canSave)
                        .accessibilityIdentifier("saveGroupButton")
                }
            }
        }
        .onChange(of: kind) { _, kind in if kind == .announcement { onlyAdminsWrite = true } }
        .onChange(of: crewId) { _, id in
            if title.isEmpty, let crew = crews.first(where: { $0.id == id }) { title = crew.name }
        }
        .sheet(isPresented: $choosesBauakte) {
            BauaktePickerSheet(title: "Bauakte wählen") { picked in
                choosesBauakte = false
                bauakte = picked
                if title.isEmpty { title = picked.title }
            }
        }
        .sheet(isPresented: $choosesMembers) {
            ChatMemberPicker(title: "Mitglieder wählen", excluded: Set(members.map(\.id))) { added in
                choosesMembers = false
                members += added
            }
        }
        .task {
            if case let .edit(detail) = mode {
                kind = detail.chatKind
                title = detail.title
                description = detail.description ?? ""
                onlyAdminsWrite = detail.onlyAdminsWrite
            } else {
                crews = (try? await model.active?.client.chatCrews()) ?? []
            }
        }
    }

    private var kindHint: String {
        switch kind {
        case .crew: "Mitglieder folgen der Kolonne in der Planung."
        case .site: "Team und Bauleitung der Bauakte kommen automatisch dazu; in der Bauakte erscheint ein Link."
        case .announcement: "Alle Mitglieder lesen, nur Admins schreiben."
        default: "Freie Gruppe mit ausgewählten Personen."
        }
    }

    private var canSave: Bool {
        guard !title.trimmingCharacters(in: .whitespaces).isEmpty else { return false }
        if isCreate, kind == .crew { return crewId != nil }
        if isCreate, kind == .site { return bauakte != nil }
        return true
    }

    private func save() async {
        guard let client = model.active?.client else { return }
        busy = true
        defer { busy = false }
        let cleanTitle = title.trimmingCharacters(in: .whitespacesAndNewlines)
        let cleanDescription = description.trimmingCharacters(in: .whitespacesAndNewlines)
        do throws(APIError) {
            switch mode {
            case .create:
                let created = try await client.createChatGroup(ChatGroupChange(
                    title: cleanTitle, description: cleanDescription.isEmpty ? nil : cleanDescription, memberIds: members.map(\.id), kind: kind,
                    onlyAdminsWrite: onlyAdminsWrite, crewId: kind == .crew ? crewId : nil, bauakteId: kind == .site ? bauakte?.id : nil))
                done(created)
            case let .edit(detail):
                _ = try await client.updateChatGroup(detail.id, ChatGroupChange(title: cleanTitle, description: cleanDescription,
                                                                                 onlyAdminsWrite: onlyAdminsWrite))
                done(nil)
            }
        } catch {
            failure = error.serverMessage ?? error.message
        }
    }
}

/// Personen auswählen (Mehrfachauswahl) für Gruppen.
struct ChatMemberPicker: View {
    let title: String
    let excluded: Set<UUID>
    let picked: ([ChatMember]) -> Void

    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var contacts: [ChatMember] = []
    @State private var selected: Set<UUID> = []
    @State private var search = ""
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            List(filtered) { person in
                Button {
                    if selected.contains(person.id) { selected.remove(person.id) } else { selected.insert(person.id) }
                } label: {
                    HStack(spacing: Space.m) {
                        Avatar(name: person.displayName, size: 32)
                        Text(person.displayName).foregroundStyle(Theme.textPrimary)
                        Spacer()
                        Image(systemName: selected.contains(person.id) ? "checkmark.circle.fill" : "circle")
                            .font(.title3)
                            .foregroundStyle(selected.contains(person.id) ? Theme.accent : Theme.textTertiary)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            .overlay { if !loaded { ProgressView() } }
            .searchable(text: $search, prompt: "Person suchen")
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fertig") { picked(contacts.filter { selected.contains($0.id) }) }.disabled(selected.isEmpty)
                }
            }
            .task {
                contacts = ((try? await model.active?.client.chatContacts()) ?? [])
                    .sorted { $0.displayName.localizedCompare($1.displayName) == .orderedAscending }
                loaded = true
            }
        }
    }

    private var filtered: [ChatMember] {
        let list = contacts.filter { !excluded.contains($0.id) && $0.id != model.active?.user.id }
        let term = search.trimmingCharacters(in: .whitespaces)
        return term.isEmpty ? list : list.filter { $0.displayName.localizedCaseInsensitiveContains(term) }
    }
}

/// Gruppenbild: quadratisch zugeschnitten, verkleinert.
enum ChatImages {
    static func square(_ data: Data, side: CGFloat) -> Data? {
        guard let image = UIImage(data: data) else { return nil }
        let length = min(image.size.width, image.size.height)
        let origin = CGPoint(x: (image.size.width - length) / 2, y: (image.size.height - length) / 2)
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        let renderer = UIGraphicsImageRenderer(size: CGSize(width: side, height: side), format: format)
        let result = renderer.image { _ in
            image.draw(in: CGRect(x: -origin.x * side / length, y: -origin.y * side / length,
                                  width: image.size.width * side / length, height: image.size.height * side / length))
        }
        return result.jpegData(compressionQuality: 0.8)
    }
}
