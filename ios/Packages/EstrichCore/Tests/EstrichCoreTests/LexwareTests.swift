import Foundation
import Testing
@testable import EstrichCore

@Suite("Lexware Office in der App")
struct LexwareTests {
    private let response = """
    {"bauakteId":"b1","configured":true,"canAssign":false,"syncError":null,"neuesFeld":1,
     "customer":{"id":"k1","name":"Huber","lexwareContactId":"l1","syncEnabled":true},
     "contact":{"lexwareContactId":"l1","name":"HUBER BAU GMBH","number":"10001","url":"https://app.lexware.de/permalink/contacts/view/l1",
       "street":"Postfach 7","postalCode":"83001","city":"Rosenheim","email":"rechnung@huber.example","differingFields":["name","email"],
       "sync":{"state":"deviations","syncedAt":"2026-11-26T10:00:00Z","error":null,
         "deviations":[{"field":"name","label":"Name","estrichOne":"Huber Bau GmbH","lexware":"HUBER BAU GMBH"},
                       {"field":"email","label":"E-Mail-Adresse","estrichOne":null,"lexware":"rechnung@huber.example"}]}},
     "summary":"1 Rechnung: offen 1.190,00 €",
     "vouchers":[{"id":"v1","lexwareId":"x1","type":"invoice","number":"RE1042","title":null,"voucherDate":"2026-10-01","dueDate":"2026-10-15",
       "netAmount":1000.0,"grossAmount":1190.0,"currency":"EUR","status":"overdue","contactName":"Huber","url":"https://app.lexware.de/x","syncedAt":null,"syncError":null},
       {"id":"v2","lexwareId":"x2","type":"quotation","number":null,"voucherDate":null,"grossAmount":null,"status":"draft","url":""}]}
    """

    @Test("liest Kontakt, Abgleichstand und Belege der Bauakte")
    func decodes() throws {
        let data = try JSONCoding.decoder.decode(BauakteLexware.self, from: Data(response.utf8))
        let contact = try #require(data.contact)
        #expect(contact.name == "HUBER BAU GMBH")
        #expect(Lexware.address(contact) == "Postfach 7, 83001 Rosenheim")
        #expect(Lexware.differs(contact, "email"))
        #expect(!Lexware.differs(contact, "street", "postalCode", "city"))
        #expect(Lexware.syncText(contact.sync) == "2 Abweichungen offen")
        #expect(Lexware.syncTone(contact.sync) == .warning)
        #expect(Lexware.syncedAt(contact.sync) == "Zuletzt abgeglichen: 26.11.2026")
        #expect(data.summary == "1 Rechnung: offen 1.190,00 €")
        #expect(data.vouchers.count == 2)
    }

    @Test("Belegzeile mit Art, Nummer, Datum, Betrag und Status; für Entwürfe kein PDF")
    func vouchers() throws {
        let data = try JSONCoding.decoder.decode(BauakteLexware.self, from: Data(response.utf8))
        let invoice = data.vouchers[0]
        let draft = data.vouchers[1]
        #expect(Lexware.title(invoice) == "Rechnung RE1042")
        #expect(Lexware.detail(invoice) == "01.10.2026, 1.190,00 €")
        #expect(Lexware.statusText(invoice.status) == "Überfällig")
        #expect(Lexware.statusTone(invoice.status) == .danger)
        #expect(Lexware.fileName(invoice) == "Rechnung-RE1042.pdf")
        #expect(Lexware.pdfAvailable(invoice))
        #expect(Lexware.title(draft) == "Angebot wird automatisch vergeben")
        #expect(!Lexware.pdfAvailable(draft))
        #expect(Lexware.draftHint == "Für Entwürfe stellt Lexware Office kein PDF bereit.")
        #expect(Lexware.statusText("paidoff") == "Bezahlt")
        #expect(Lexware.statusTone("accepted") == .success)
        #expect(Lexware.typeText("creditnote") == "Gutschrift")
        // Auftragsbestätigung und Lieferschein: festgeschrieben heißt „Erstellt“, nie „Offen“ mit Zahlungsbedeutung.
        #expect(Lexware.statusText("open", type: "orderconfirmation") == "Erstellt")
        #expect(Lexware.statusTone("open", type: "orderconfirmation") == .neutral)
        #expect(Lexware.statusText("open", type: "deliverynote") == "Erstellt")
        #expect(Lexware.typeText("deliverynote") == "Lieferschein")
        #expect(Lexware.statusText("voided", type: "deliverynote") == "Storniert")
        #expect(Lexware.statusText("open", type: "invoice") == "Offen")
        #expect(Lexware.statusText("open", type: "quotation") == "Offen")
        #expect(Lexware.statusText("irgendwas", type: "invoice") == "Unbekannt")
        // Rechnungsplan (Verkauf): Anteil berechnet für den schlichten Balken.
        #expect(SalesPlan(orderNet: 10000, invoicedNet: 6500, text: "", progressPercent: 40).billedFraction == 0.65)
        #expect(SalesPlan(orderNet: nil, invoicedNet: 0, text: "", progressPercent: nil).billedFraction == nil)
        #expect(SalesPlan(orderNet: 1000, invoicedNet: 1500, text: "", progressPercent: nil).billedFraction == 1)
        #expect(Lexware.euro(15767.5) == "15.767,50 €")
        #expect(Lexware.euro(1234567.05) == "1.234.567,05 €")
        #expect(Lexware.euro(nil) == "")
    }

    @Test("Prüfung beim Anlegen eines Kunden: Anfrage, Warnung und Wege")
    func check() throws {
        #expect(Lexware.checkRequest(name: "Hu", company: true) == nil)
        let request = try #require(Lexware.checkRequest(name: " Huber Bau ", company: true, postalCode: "83022"))
        #expect(request.name == "Huber Bau")
        #expect(request.kind == "company")
        #expect(request.postalCode == "83022")
        #expect(request.street == nil)
        #expect(Lexware.checkRequest(name: "Anna Kraus", company: false)?.kind == "person")
        #expect(Lexware.existsWarning == "Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden.")

        let json = """
        {"checked":true,"message":null,"canImport":true,"candidates":[
          {"contact":{"id":"l1","name":"HUBER BAU GMBH","isCompany":true,"customerNumber":"10001","street":"Am Anger 7","postalCode":"83022","city":"Rosenheim",
            "email":null,"phone":null,"url":"https://app.lexware.de/x","linkedContactId":null,"linkedContactName":null},"score":80,"reasons":["gleicher Name und PLZ"],"strong":false},
          {"contact":{"id":"l2","name":"Huber Verwaltung","isCompany":true,"customerNumber":null,"street":null,"postalCode":null,"city":null,
            "email":null,"phone":null,"url":"","linkedContactId":"k9","linkedContactName":"Huber"},"score":60,"reasons":["ähnlicher Name"],"strong":false}]}
        """
        let result = try JSONCoding.decoder.decode(LexwareContactCheck.self, from: Data(json.utf8))
        #expect(result.candidates.count == 2)
        #expect(Lexware.candidateLine(result.candidates[0]) == "Kundennummer 10001, Am Anger 7, 83022 Rosenheim, gleicher Name und PLZ")
        #expect(Lexware.candidateLine(result.candidates[1]) == "ohne Kundennummer, ähnlicher Name")
        #expect(Lexware.canImport(result, result.candidates[0]))
        #expect(!Lexware.canImport(result, result.candidates[1]), "schon mit einem Kunden verknüpft")
    }

    @Test("Eingangsbeleg hochladen: Modul nur mit Lexware, Größe geprüft, Meldung nach dem Hochladen")
    func purchaseUpload() throws {
        #expect(!AppModules(states: ["purchases": true]).purchasesOn, "ohne Lexware Office aus")
        #expect(AppModules(states: ["lexware": true, "purchases": true]).purchasesOn)
        #expect(!AppModules(states: ["lexware": true]).purchasesOn, "ältere Server kennen das Modul nicht")
        #expect(Lexware.purchaseUploadProblem(byteCount: 0) == "Die Datei ist leer.")
        #expect(Lexware.purchaseUploadProblem(byteCount: 6 * 1024 * 1024)?.contains("5 MB") == true)
        #expect(Lexware.purchaseUploadProblem(byteCount: 1000) == nil)

        let json = """
        {"document":{"id":"0193a1b2-0000-7000-8000-000000000001","fileName":"foto.jpg","formatName":"Datei ohne E-Rechnung","problemCount":0,
          "state":"uploaded"},"note":null,"data":{"format":"none","problems":[]},"canEdit":true,"duplicate":false}
        """
        let result = try JSONCoding.decoder.decode(PurchaseUploadResult.self, from: Data(json.utf8))
        #expect(Lexware.purchaseUploadText(result) == "Beleg hochgeladen (Datei ohne E-Rechnung). Das Büro gibt ihn an Lexware Office weiter.")
        let doc = PurchaseUploadResult.Document(id: UUID(), fileName: "r.xml", formatName: "XRechnung (UBL)", problemCount: 2)
        #expect(Lexware.purchaseUploadText(PurchaseUploadResult(document: doc, duplicate: false)).hasSuffix("das Büro prüft sie."))
        #expect(Lexware.purchaseUploadText(PurchaseUploadResult(document: doc, duplicate: true)).hasPrefix("Diesen Beleg gibt es schon."))
    }
}
