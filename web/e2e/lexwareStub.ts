import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { randomUUID } from 'node:crypto';

/**
 * Nachgebildete Public API von Lexware Office für die Ende-zu-Ende-Tests (kein echter Aufruf, kein echter Schlüssel):
 * Profil, Kontakte (Liste mit Filtern, Abruf, Anlage, Ändern mit `version`, sonst 409), Belege (Rechnungen, Angebote,
 * Gutschriften, Auftragsbestätigungen: nur Abruf), Belegliste und das Beleg-PDF (`/file` sowie `/document` mit `/v1/files`;
 * für Entwürfe 406), Artikel (Liste, Abruf, Anlage, Ändern mit `version`), Zahlungsbedingungen und das Anlegen eines
 * Angebotsentwurfs (`POST /v1/quotations`, mit Abfrage wie `finalize` abgelehnt). Andere schreibende Aufrufe für Belege
 * gibt es nicht (404). Der Zustand
 * liegt im Speicher und lässt sich im Test ändern (z. B. eine Rechnung in Lexware „abschließen“, einen Kontakt bearbeiten).
 */
export const STUB_KEY = 'e2e-schluessel-nicht-echt';

type Json = Record<string, unknown>;

export interface StubContact {
  name: string; company?: boolean; email?: string; phone?: string; street?: string; zip?: string; city?: string;
}

/** Kleines, gültiges PDF mit einer Seite und den angegebenen Zeilen (für die Anzeige im Test). */
export function simplePdf(lines: string[]): Buffer {
  const text = (value: string) => value.replace(/[\\()]/g, (c) => `\\${c}`).replace(/[^\x20-\x7e]/g, '?');
  const content = ['BT', '/F1 18 Tf', '72 770 Td', ...lines.flatMap((line, index) => [index === 1 ? '/F1 11 Tf' : '', index > 0 ? '0 -24 Td' : '', `(${text(line)}) Tj`]), 'ET']
    .filter(Boolean).join('\n');
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    `<< /Length ${content.length} >>\nstream\n${content}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ];
  let pdf = '%PDF-1.4\n';
  const offsets: number[] = [];
  objects.forEach((object, index) => {
    offsets.push(pdf.length);
    pdf += `${index + 1} 0 obj\n${object}\nendobj\n`;
  });
  const xref = pdf.length;
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.map((o) => `${String(o).padStart(10, '0')} 00000 n \n`).join('')}`;
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(pdf, 'latin1');
}

export class LexwareStub {
  readonly contacts = new Map<string, Json>();
  readonly vouchers = new Map<string, Json>();
  readonly articles = new Map<string, Json>();
  /** Gesendete Angebotsentwürfe (Inhalt von `POST /v1/quotations`). */
  readonly quotations: Json[] = [];
  paymentConditions: Json[] = [
    { id: 'pc-14', organizationDefault: true, paymentTermLabelTemplate: 'Zahlbar innerhalb von 14 Tagen ohne Abzug.', paymentTermDuration: 14 },
    { id: 'pc-30', organizationDefault: false, paymentTermLabelTemplate: 'Zahlbar innerhalb von 30 Tagen, bei Zahlung innerhalb von 8 Tagen 2 % Skonto.', paymentTermDuration: 30,
      paymentDiscountConditions: { discountPercentage: 2, discountRange: 8 } },
  ];
  readonly calls: { method: string; path: string }[] = [];
  companyName = 'Estrichbau E2E GmbH';
  /** Kennt die nachgebildete API den Pfad `/file`? Sonst nur den älteren Weg über `/document`. */
  fileRoute = true;
  /** Lexware Office nicht erreichbar: jede Anfrage antwortet mit 503. */
  down = false;
  /** Steuerarten, die diese Organisation ablehnt (Antwort wie bei einem echten Konto: 400 „Tax type '…' not allowed for this organization“). */
  readonly rejectedTaxTypes = new Set<string>();
  /** Angaben im Profil (`smallBusiness`, `taxType`); `undefined` = nicht geliefert. */
  smallBusiness: boolean | undefined = undefined;
  profileTaxType: string | undefined = undefined;
  /** Verzögerung jeder Antwort in Millisekunden (um Zwischenzustände der Oberfläche zu prüfen). */
  delayMs = 0;
  private server: Server | null = null;
  private customerNumber = 10000;

  get url(): string {
    const address = this.server?.address();
    return address && typeof address === 'object' ? `http://127.0.0.1:${address.port}` : '';
  }

  async start(port = 0): Promise<string> {
    this.server = createServer((request, response) => void this.handle(request, response));
    await new Promise<void>((resolve) => this.server!.listen(port, '127.0.0.1', resolve));
    return this.url;
  }

  async stop(): Promise<void> {
    await new Promise<void>((resolve) => (this.server ? this.server.close(() => resolve()) : resolve()));
    this.server = null;
  }

  addContact(c: StubContact): string {
    const id = randomUUID();
    const contact: Json = {
      id, organizationId: 'aa93e8a8-2aa3-470b-b914-caad8a255dd8', version: 1, archived: false, createdDate: '2026-01-10T09:00:00.000+01:00',
      updatedDate: '2026-01-10T09:00:00.000+01:00', roles: { customer: { number: ++this.customerNumber } }, note: 'Stammkunde',
    };
    if (c.company === false) {
      const space = c.name.lastIndexOf(' ');
      contact.person = { salutation: 'Herr', firstName: space > 0 ? c.name.slice(0, space) : null, lastName: space > 0 ? c.name.slice(space + 1) : c.name };
    } else {
      contact.company = { name: c.name };
    }
    if (c.street || c.zip) contact.addresses = { billing: [{ street: c.street, zip: c.zip, city: c.city, countryCode: 'DE' }] };
    if (c.email) contact.emailAddresses = { business: [c.email] };
    if (c.phone) contact.phoneNumbers = { business: [c.phone] };
    this.contacts.set(id, contact);
    return id;
  }

  addVoucher(v: {
    type: 'invoice' | 'quotation' | 'creditnote' | 'orderconfirmation' | 'deliverynote'; status: string; number: string | null; date: string; dueDate?: string; net: number;
    gross: number; contactId?: string; contactName?: string; title?: string; introduction?: string;
  }): string {
    const id = randomUUID();
    const contact = v.contactId ? this.contacts.get(v.contactId) : undefined;
    this.vouchers.set(id, {
      id, voucherType: v.type, voucherStatus: v.status, voucherNumber: v.number, voucherDate: `${v.date}T00:00:00.000+01:00`,
      [v.type === 'quotation' ? 'expirationDate' : 'dueDate']: v.dueDate ? `${v.dueDate}T00:00:00.000+01:00` : null,
      title: v.title ?? (v.type === 'quotation' ? 'Angebot' : 'Rechnung'), introduction: v.introduction ?? null,
      address: { contactId: v.contactId ?? null, name: v.contactName ?? (contact ? LexwareStub.name(contact) : null) },
      totalPrice: { currency: 'EUR', totalNetAmount: v.net, totalGrossAmount: v.gross },
    });
    return id;
  }

  addArticle(a: { title: string; type?: 'PRODUCT' | 'SERVICE'; number?: string; net: number; tax?: number; unit?: string; description?: string }): string {
    const id = randomUUID();
    const tax = a.tax ?? 19;
    this.articles.set(id, {
      id, organizationId: 'aa93e8a8-2aa3-470b-b914-caad8a255dd8', archived: false, title: a.title, description: a.description ?? null, type: a.type ?? 'SERVICE',
      articleNumber: a.number ?? null, unitName: a.unit ?? 'm²', price: { netPrice: a.net, grossPrice: Math.round(a.net * (100 + tax)) / 100, leadingPrice: 'NET', taxRate: tax },
      version: 1,
    });
    return id;
  }

  /** Wie eine Änderung in Lexware Office (abschließen, Zahlung verbuchen, Angebot annehmen). */
  change(id: string, values: { status?: string; number?: string; dueDate?: string }): void {
    const voucher = this.vouchers.get(id);
    if (!voucher) return;
    if (values.status) voucher.voucherStatus = values.status;
    if (values.number) voucher.voucherNumber = values.number;
    if (values.dueDate) voucher[voucher.voucherType === 'quotation' ? 'expirationDate' : 'dueDate'] = `${values.dueDate}T00:00:00.000+01:00`;
  }

  /** Wie eine Änderung am Kontakt in Lexware Office: Version und Änderungszeitpunkt steigen. */
  edit(id: string, change: (contact: Json) => void): void {
    const contact = this.contacts.get(id);
    if (!contact) return;
    change(contact);
    contact.version = Number(contact.version ?? 0) + 1;
    contact.updatedDate = new Date().toISOString();
  }

  /** Rechnungsanschrift eines Kontakts (zum Prüfen und Ändern im Test). */
  billing(id: string): Json {
    const contact = this.contacts.get(id)!;
    const addresses = (contact.addresses ??= {}) as Json;
    const billing = (addresses.billing ??= [{ countryCode: 'DE' }]) as Json[];
    return billing[0];
  }

  private static name(contact: Json): string {
    const company = contact.company as { name?: string } | undefined;
    const person = contact.person as { firstName?: string; lastName?: string } | undefined;
    return company?.name ?? `${person?.firstName ?? ''} ${person?.lastName ?? ''}`.trim();
  }

  private async handle(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const url = new URL(request.url ?? '/', 'http://stub');
    const path = url.pathname;
    const method = request.method ?? 'GET';
    this.calls.push({ method, path });
    const chunks: Buffer[] = [];
    for await (const chunk of request) chunks.push(chunk as Buffer);
    const body = chunks.length ? (JSON.parse(Buffer.concat(chunks).toString('utf8')) as Json) : {};
    const send = (status: number, json: unknown) => {
      response.writeHead(status, { 'Content-Type': 'application/json' });
      response.end(JSON.stringify(json));
    };

    if (this.delayMs > 0) await new Promise((resolve) => setTimeout(resolve, this.delayMs));
    if (this.down) return send(503, { status: 503, error: 'Service Unavailable' });
    if (request.headers.authorization !== `Bearer ${STUB_KEY}`) return send(401, { status: 401, error: 'Unauthorized' });
    if (method === 'GET' && path === '/v1/profile') {
      return send(200, { organizationId: 'aa93e8a8-2aa3-470b-b914-caad8a255dd8', companyName: this.companyName, smallBusiness: this.smallBusiness, taxType: this.profileTaxType });
    }

    if (method === 'GET' && path === '/v1/contacts') {
      let all = [...this.contacts.values()].filter((c) => (c.roles as Json).customer).sort((a, b) => LexwareStub.name(a).localeCompare(LexwareStub.name(b)));
      const name = url.searchParams.get('name');
      const email = url.searchParams.get('email');
      const number = url.searchParams.get('number');
      if (name) all = all.filter((c) => LexwareStub.name(c).toLowerCase().includes(name.toLowerCase()));
      if (email) all = all.filter((c) => JSON.stringify(c.emailAddresses ?? {}).toLowerCase().includes(email.toLowerCase()));
      if (number) all = all.filter((c) => String(((c.roles as Json).customer as Json).number) === number);
      const page = Number(url.searchParams.get('page') ?? 0);
      const size = Number(url.searchParams.get('size') ?? 25);
      const totalPages = Math.max(1, Math.ceil(all.length / size));
      return send(200, { content: all.slice(page * size, (page + 1) * size), first: page === 0, last: page + 1 >= totalPages, totalPages, totalElements: all.length, size, number: page });
    }
    if (method === 'POST' && path === '/v1/contacts') {
      const id = randomUUID();
      this.contacts.set(id, { ...body, id, version: 1, archived: false, roles: { customer: { number: ++this.customerNumber } } });
      return send(200, { id, resourceUri: `${this.url}/v1/contacts/${id}`, version: 1 });
    }
    if (method === 'GET' && path.startsWith('/v1/contacts/')) {
      const contact = this.contacts.get(path.slice('/v1/contacts/'.length));
      return contact ? send(200, contact) : send(404, { status: 404, error: 'Not Found' });
    }

    if (method === 'PUT' && path.startsWith('/v1/contacts/')) {
      const id = path.slice('/v1/contacts/'.length);
      const stored = this.contacts.get(id);
      if (!stored) return send(404, { status: 404, error: 'Not Found' });
      if (body.version !== stored.version) return send(409, { status: 409, message: 'Resource was modified' });
      // Wie die echte API: Der gesendete Kontakt ersetzt den gespeicherten; ID, Organisation und Zeitpunkte führt der Server.
      const next: Json = {
        ...body, id, organizationId: stored.organizationId, createdDate: stored.createdDate, archived: stored.archived, version: Number(stored.version) + 1,
        updatedDate: new Date().toISOString(),
      };
      this.contacts.set(id, next);
      return send(200, { id, resourceUri: `${this.url}${path}`, createdDate: next.createdDate, updatedDate: next.updatedDate, version: next.version });
    }

    if (method === 'GET' && path === '/v1/articles') {
      const all = [...this.articles.values()];
      const page = Number(url.searchParams.get('page') ?? 0);
      const size = Number(url.searchParams.get('size') ?? 25);
      const totalPages = Math.max(1, Math.ceil(all.length / size));
      return send(200, { content: all.slice(page * size, (page + 1) * size), first: page === 0, last: page + 1 >= totalPages, totalPages, totalElements: all.length });
    }
    if (method === 'POST' && path === '/v1/articles') {
      const id = randomUUID();
      this.articles.set(id, { ...body, id, version: 1, archived: false });
      return send(200, { id, resourceUri: `${this.url}/v1/articles/${id}`, version: 1 });
    }
    if (method === 'GET' && path.startsWith('/v1/articles/')) {
      const article = this.articles.get(path.slice('/v1/articles/'.length));
      return article ? send(200, article) : send(404, { status: 404, error: 'Not Found' });
    }
    if (method === 'PUT' && path.startsWith('/v1/articles/')) {
      const id = path.slice('/v1/articles/'.length);
      const stored = this.articles.get(id);
      if (!stored) return send(404, { status: 404, error: 'Not Found' });
      if (body.version !== stored.version) return send(409, { status: 409, message: 'Resource was modified' });
      this.articles.set(id, { ...body, id, archived: stored.archived, version: Number(stored.version) + 1 });
      return send(200, { id, version: Number(stored.version) + 1 });
    }
    if (method === 'GET' && path === '/v1/payment-conditions') return send(200, this.paymentConditions);
    if (method === 'POST' && path === '/v1/quotations') {
      if (url.search) return send(400, { status: 400, message: 'Unexpected query' });
      const taxType = String((body.taxConditions as Json | undefined)?.taxType ?? '');
      if (this.rejectedTaxTypes.has(taxType)) {
        return send(400, { requestId: randomUUID(), status: 400, error: 'Bad Request', path, message: `Tax type '${taxType}' not allowed for this organization` });
      }
      const id = randomUUID();
      let net = 0;
      let gross = 0;
      for (const line of ((body.lineItems as Json[]) ?? []).filter((l) => l.type !== 'text')) {
        const price = line.unitPrice as Json;
        const amount = Math.round(Number(line.quantity) * Number(price.netAmount) * (1 - Number(line.discountPercentage ?? 0) / 100) * 100) / 100;
        net += amount;
        gross += Math.round(amount * (100 + Number(price.taxRatePercentage))) / 100;
      }
      this.quotations.push(body);
      const contact = this.contacts.get(String((body.address as Json).contactId));
      this.vouchers.set(id, {
        ...body, id, voucherType: 'quotation', voucherStatus: 'draft', voucherNumber: null,
        address: { contactId: (body.address as Json).contactId, name: contact ? LexwareStub.name(contact) : null },
        totalPrice: { currency: 'EUR', totalNetAmount: Math.round(net * 100) / 100, totalGrossAmount: Math.round(gross * 100) / 100 },
      });
      return send(200, { id, resourceUri: `${this.url}/v1/quotations/${id}`, version: 1 });
    }

    const segments: Record<string, string> = { invoices: 'invoice', quotations: 'quotation', 'credit-notes': 'creditnote', 'order-confirmations': 'orderconfirmation', 'delivery-notes': 'deliverynote' };
    const parts = path.split('/').filter(Boolean);
    const pdf = (id: string) => {
      const voucher = this.vouchers.get(id)!;
      const kind: Record<string, string> = { invoice: 'Rechnung', quotation: 'Angebot', creditnote: 'Gutschrift', orderconfirmation: 'Auftragsbestaetigung' };
      const content = simplePdf([
        `${kind[String(voucher.voucherType)]} ${String(voucher.voucherNumber ?? '')}`.trim(), String((voucher.address as Json).name ?? ''),
        `Datum: ${String(voucher.voucherDate).slice(0, 10)}`, `Gesamtbetrag: ${String((voucher.totalPrice as Json).totalGrossAmount)} EUR`,
        'Nachgebildeter Beleg aus dem Test, kein echtes Dokument.',
      ]);
      response.writeHead(200, { 'Content-Type': 'application/pdf', 'Content-Length': content.length });
      response.end(content);
    };
    if (method === 'GET' && parts.length === 4 && segments[parts[1]] && (parts[3] === 'file' || parts[3] === 'document')) {
      const voucher = this.vouchers.get(parts[2]);
      if ((parts[3] === 'file' && !this.fileRoute) || !voucher || voucher.voucherType !== segments[parts[1]]) return send(404, { status: 404, error: 'Not Found' });
      if (voucher.voucherStatus === 'draft') return send(406, { status: 406, message: 'No document for drafts' });
      return parts[3] === 'file' ? pdf(parts[2]) : send(200, { documentFileId: `file-${parts[2]}` });
    }
    if (method === 'GET' && parts.length === 3 && parts[1] === 'files' && parts[2].startsWith('file-') && this.vouchers.has(parts[2].slice(5))) {
      return pdf(parts[2].slice(5));
    }
    if (method === 'GET' && parts.length === 3 && segments[parts[1]]) {
      const voucher = this.vouchers.get(parts[2]);
      return voucher && voucher.voucherType === segments[parts[1]] ? send(200, voucher) : send(404, { status: 404, error: 'Not Found' });
    }
    if (method === 'GET' && path === '/v1/voucherlist') {
      const types = (url.searchParams.get('voucherType') ?? '').split(',');
      const number = url.searchParams.get('voucherNumber');
      const contactId = url.searchParams.get('contactId');
      const content = [...this.vouchers.values()]
        .filter((v) => types.includes(String(v.voucherType)) && (!number || v.voucherNumber === number) && (!contactId || (v.address as Json).contactId === contactId))
        .sort((a, b) => String(b.voucherDate).localeCompare(String(a.voucherDate)))
        .map((v) => ({
          id: v.id, voucherType: v.voucherType, voucherStatus: v.voucherStatus, voucherNumber: v.voucherNumber, voucherDate: v.voucherDate,
          dueDate: v.dueDate ?? v.expirationDate ?? null, contactId: (v.address as Json).contactId, contactName: (v.address as Json).name,
          totalAmount: (v.totalPrice as Json).totalGrossAmount, currency: 'EUR',
        }));
      return send(200, { content, last: true, totalPages: 1, totalElements: content.length });
    }
    return send(404, { status: 404, error: 'Not Found' });
  }
}
