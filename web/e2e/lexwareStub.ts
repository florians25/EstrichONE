import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { randomUUID } from 'node:crypto';

/**
 * Nachgebildete Public API von Lexware Office für die Ende-zu-Ende-Tests (kein echter Aufruf, kein echter Schlüssel):
 * Profil, Kontakte (Liste mit Filtern, Abruf, Anlage), Belege (Rechnungen, Angebote, Gutschriften, Auftragsbestätigungen:
 * nur Abruf) und Belegliste. Schreibende Aufrufe für Belege gibt es nicht (404), denn EstrichONE legt keine Belege an. Der
 * Zustand liegt im Speicher und lässt sich im Test ändern (z. B. eine Rechnung in Lexware „abschließen“).
 */
export const STUB_KEY = 'e2e-schluessel-nicht-echt';

type Json = Record<string, unknown>;

export interface StubContact {
  name: string; company?: boolean; email?: string; phone?: string; street?: string; zip?: string; city?: string;
}

export class LexwareStub {
  readonly contacts = new Map<string, Json>();
  readonly vouchers = new Map<string, Json>();
  readonly calls: { method: string; path: string }[] = [];
  companyName = 'Estrichbau E2E GmbH';
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
    const contact: Json = { id, version: 1, archived: false, roles: { customer: { number: ++this.customerNumber } } };
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
    type: 'invoice' | 'quotation' | 'creditnote' | 'orderconfirmation'; status: string; number: string | null; date: string; dueDate?: string; net: number;
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

  /** Wie eine Änderung in Lexware Office (abschließen, Zahlung verbuchen, Angebot annehmen). */
  change(id: string, values: { status?: string; number?: string; dueDate?: string }): void {
    const voucher = this.vouchers.get(id);
    if (!voucher) return;
    if (values.status) voucher.voucherStatus = values.status;
    if (values.number) voucher.voucherNumber = values.number;
    if (values.dueDate) voucher[voucher.voucherType === 'quotation' ? 'expirationDate' : 'dueDate'] = `${values.dueDate}T00:00:00.000+01:00`;
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

    if (request.headers.authorization !== `Bearer ${STUB_KEY}`) return send(401, { status: 401, error: 'Unauthorized' });
    if (method === 'GET' && path === '/v1/profile') return send(200, { organizationId: 'aa93e8a8-2aa3-470b-b914-caad8a255dd8', companyName: this.companyName });

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
      this.contacts.set(id, { ...body, id, roles: { customer: { number: ++this.customerNumber } } });
      return send(200, { id, resourceUri: `${this.url}/v1/contacts/${id}`, version: 1 });
    }
    if (method === 'GET' && path.startsWith('/v1/contacts/')) {
      const contact = this.contacts.get(path.slice('/v1/contacts/'.length));
      return contact ? send(200, contact) : send(404, { status: 404, error: 'Not Found' });
    }

    const segments: Record<string, string> = { invoices: 'invoice', quotations: 'quotation', 'credit-notes': 'creditnote', 'order-confirmations': 'orderconfirmation' };
    const parts = path.split('/').filter(Boolean);
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
