import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { jsPDF } from 'jspdf';
import QRCode from 'qrcode';
import { ApiService, GenericRecord } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

@Component({
  standalone: true,
  imports: [FormsModule, DatePipe, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSnackBarModule],
  template: `
    <section class="page-head compact-head">
      <div><p class="eyebrow">Kundenportal</p><h1>Kundenzugänge</h1></div>
      <div class="head-actions"><button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon>Aktualisieren</button></div>
    </section>

    <mat-card class="square-card portal-admin-card">
      <mat-form-field appearance="outline" class="portal-search"><mat-label>Kunde suchen</mat-label><input matInput [(ngModel)]="search" placeholder="Name, Ort"><mat-icon matPrefix>search</mat-icon></mat-form-field>
      <p class="portal-admin-hint">Erzeugen Sie je Kunde einen sicheren Zugangslink und drucken Sie den Zugangsbrief mit QR-Code zum Aushändigen. Der Link zeigt dem Kunden seine Bauvorhaben und freigegebenen Dokumente – ganz ohne Passwort.</p>

      <div class="portal-customer-list">
        @for (c of filtered(); track c['id']) {
          <article class="portal-customer-row">
            <div class="portal-customer-info">
              <b>{{ c['name'] }}</b>
              <small>{{ cityLine(c) }}</small>
            </div>
            <div class="portal-customer-actions">
              <button mat-flat-button color="primary" [disabled]="busyId() === c['id']" (click)="generateAndDownload(c)"><mat-icon>qr_code_2</mat-icon>Zugang + Brief erstellen</button>
            </div>
          </article>
        } @empty { <p class="portal-empty">Keine Kunden gefunden.</p> }
      </div>
    </mat-card>
  `
})
export class CustomerPortalAdminComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly snack = inject(MatSnackBar);
  readonly customers = signal<GenericRecord[]>([]);
  readonly company = signal<GenericRecord>({});
  readonly busyId = signal('');
  search = '';
  readonly filtered = computed(() => {
    const q = this.search.trim().toLowerCase();
    const list = this.customers();
    if (!q) return list;
    return list.filter((c) => [c['name'], c['city'], c['zip']].join(' ').toLowerCase().includes(q));
  });

  constructor() { this.load(); this.api.publicAppInfo().subscribe({ next: (info) => { const c = info?.['company']; if (c && typeof c === 'object') this.company.set(c as GenericRecord); }, error: () => {} }); }
  cityLine(c: GenericRecord): string { return [c['zip'], c['city']].filter((v) => !!v).join(' '); }
  load(): void {
    this.api.list('customers', '', 500).subscribe({
      next: (res) => this.customers.set(res.items || []),
      error: () => this.snack.open('Kunden konnten nicht geladen werden.', 'OK', { duration: 3000 })
    });
  }

  generateAndDownload(customer: GenericRecord): void {
    this.busyId.set(String(customer['id']));
    // Bauaktennummer des Kunden ermitteln (erste/aktuellste Bauakte).
    this.api.list('projects', '', 200).subscribe({
      next: (proj) => {
        const own = (proj.items || []).filter((p) => String(p['customer_id'] || '') === String(customer['id']));
        const bauakte = own.map((p) => String(p['project_no'] || p['name'] || '')).filter(Boolean).slice(0, 3).join(', ');
        this.api.customerPortalGenerate(String(customer['id'])).subscribe({
          next: async (res) => {
            const token = String(res['token'] || '');
            const pin = String(res['pin'] || '');
            const url = `${location.origin}/kundenportal/${token}`;
            try { await this.buildLetter(customer, url, pin, bauakte); this.snack.open('Zugang erstellt – Brief wird heruntergeladen.', 'OK', { duration: 3000 }); }
            catch { this.snack.open('Zugang erstellt, aber der Brief konnte nicht erzeugt werden.', 'OK', { duration: 4000 }); }
            this.busyId.set('');
          },
          error: () => { this.busyId.set(''); this.snack.open('Zugang konnte nicht erstellt werden.', 'OK', { duration: 3500 }); }
        });
      },
      error: () => {
        // Auch ohne Projektliste den Zugang erstellen.
        this.api.customerPortalGenerate(String(customer['id'])).subscribe({
          next: async (res) => {
            const token = String(res['token'] || ''); const pin = String(res['pin'] || '');
            const url = `${location.origin}/kundenportal/${token}`;
            try { await this.buildLetter(customer, url, pin, ''); } catch {}
            this.busyId.set('');
          },
          error: () => { this.busyId.set(''); this.snack.open('Zugang konnte nicht erstellt werden.', 'OK', { duration: 3500 }); }
        });
      }
    });
  }

  private async buildLetter(customer: GenericRecord, url: string, pin: string, bauakte: string): Promise<void> {
    const doc = new jsPDF({ unit: 'mm', format: 'a4' });
    const PW = 210, PH = 297;          // A4 in mm
    const left = 25, right = 20;        // DIN 5008 Ränder
    const contentW = PW - left - right;
    const co = this.company();
    const companyName = String(co['name'] || 'EstrichWeb');
    const companyStreet = String(co['street'] || co['address'] || '');
    const companyCity = [String(co['zip'] || ''), String(co['city'] || '')].filter(Boolean).join(' ');
    const companyPhone = String(co['phone'] || '');
    const companyEmail = String(co['email'] || '');
    const companyMgr = String(co['managing_director'] || co['ceo'] || '');
    const companyReg = String(co['register'] || co['hrb'] || '');
    const companyVat = String(co['vat_id'] || co['ustid'] || '');
    const today = new Date().toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
    const aktenzeichen = `KP-${String(customer['customer_no'] || customer['id'] || '').toString().slice(-8).toUpperCase() || today.replace(/\./g, '')}`;

    const black = () => doc.setTextColor(0, 0, 0);
    black();

    // ---- Briefkopf (Firmenname, dünne Linie darunter) ----
    doc.setFont('times', 'bold'); doc.setFontSize(18);
    doc.text(companyName.toUpperCase(), left, 22);
    doc.setFont('times', 'normal'); doc.setFontSize(9);
    const headRight: string[] = [];
    if (companyStreet) headRight.push(companyStreet);
    if (companyCity) headRight.push(companyCity);
    if (companyPhone) headRight.push('Tel. ' + companyPhone);
    if (companyEmail) headRight.push(companyEmail);
    const hrStartY = 23 - Math.max(0, headRight.length - 1) * 4.0;
    headRight.forEach((line, i) => doc.text(line, PW - right, hrStartY + i * 4.0, { align: 'right' }));
    doc.setLineWidth(0.6); doc.setDrawColor(0); doc.line(left, 26, PW - right, 26);

    // ---- Rücksendeangabe (klein) über dem Anschriftenfeld (DIN 5008) ----
    doc.setFont('times', 'normal'); doc.setFontSize(7);
    const senderLine = [companyName, companyStreet, companyCity].filter(Boolean).join(' · ');
    doc.text(senderLine, left, 45);
    doc.setLineWidth(0.2); doc.line(left, 46.5, left + 85, 46.5);

    // ---- Anschriftenfeld (Empfänger) ----
    doc.setFont('times', 'normal'); doc.setFontSize(11);
    const addr: string[] = [];
    addr.push(String(customer['name'] || 'Kundin/Kunde'));
    if (customer['contact_person'] && String(customer['contact_person']) !== String(customer['name'])) addr.push('z. Hd. ' + String(customer['contact_person']));
    if (customer['address']) addr.push(String(customer['address']));
    const custCity = [String(customer['zip'] || ''), String(customer['city'] || '')].filter(Boolean).join(' ');
    if (custCity) addr.push(custCity);
    addr.forEach((line, i) => doc.text(line, left, 52 + i * 5));

    // ---- Informationsblock rechts (Datum / Aktenzeichen / Bauakte / Bearbeiter) ----
    doc.setFont('times', 'normal'); doc.setFontSize(10);
    const infoX = 125; let iy = 52;
    const infoRow = (k: string, v: string) => { if (!v) return; doc.text(k, infoX, iy); const vLines = doc.splitTextToSize(v, PW - right - infoX - 22) as string[]; doc.text(vLines.slice(0,2), PW - right, iy, { align: 'right' }); iy += 5.5 * Math.max(1, Math.min(2, vLines.length)); };
    infoRow('Datum:', today);
    infoRow('Aktenzeichen:', aktenzeichen);
    infoRow('Bauakte-Nr.:', bauakte || '—');
    infoRow('Bearbeiter:', (this.auth.userName || '').trim() || '—');

    // ---- Betreff ----
    let y = 98;
    doc.setFont('times', 'bold'); doc.setFontSize(11);
    doc.text('Zugang zu Ihrem persönlichen Kundenportal (digitale Bauakte)', left, y);
    y += 10;

    // ---- Anrede + Fließtext ----
    doc.setFont('times', 'normal'); doc.setFontSize(11);
    const salutation = customer['contact_person'] ? `Sehr geehrte Damen und Herren,` : `Sehr geehrte Damen und Herren,`;
    doc.text(salutation, left, y); y += 7;
    const body1 = `hiermit stellen wir Ihnen den persönlichen Zugang zu Ihrem Kundenportal bereit. Über dieses Portal können Sie jederzeit den aktuellen Stand Ihrer Bauvorhaben sowie die für Sie freigegebenen Dokumente einsehen. Der Zugang erfolgt über den unten abgebildeten Zugangscode in Verbindung mit Ihrer persönlichen PIN.`;
    const body1Lines = doc.splitTextToSize(body1, contentW) as string[];
    doc.text(body1Lines, left, y); y += body1Lines.length * 5.4 + 4;

    // ---- QR-Code im Rahmen (schwarz-weiß) ----
    const qrDataUrl = await QRCode.toDataURL(url, { width: 600, margin: 0, color: { dark: '#000000', light: '#ffffff' } });
    const qrSize = 48; const qrX = left;
    doc.setDrawColor(0); doc.setLineWidth(0.4);
    doc.rect(qrX - 2, y - 2, qrSize + 4, qrSize + 4);
    doc.addImage(qrDataUrl, 'PNG', qrX, y, qrSize, qrSize);
    // Anleitung rechts neben dem QR
    const insX = qrX + qrSize + 8; let insY = y + 4;
    doc.setFont('times', 'bold'); doc.setFontSize(10); doc.text('So gelangen Sie zu Ihrem Portal:', insX, insY); insY += 6;
    doc.setFont('times', 'normal'); doc.setFontSize(10);
    ['1. QR-Code mit der Kamera Ihres Smartphones scannen.',
     '2. Alternativ den unten stehenden Link im Browser eingeben.',
     '3. Auf Nachfrage Ihre 5-stellige PIN eingeben.'].forEach((line) => {
      const wrapped = doc.splitTextToSize(line, contentW - qrSize - 8) as string[];
      doc.text(wrapped, insX, insY); insY += wrapped.length * 5;
    });
    y += qrSize + 10;

    // ---- PIN-Kasten (hervorgehoben) ----
    const pinBoxH = 16;
    doc.setDrawColor(0); doc.setLineWidth(0.8); doc.rect(left, y, contentW, pinBoxH);
    doc.setFont('times', 'bold'); doc.setFontSize(11); doc.text('Ihre persönliche PIN:', left + 4, y + 10);
    doc.setFont('courier', 'bold'); doc.setFontSize(20);
    doc.text(pin.split('').join('  '), PW - right - 4, y + 11.5, { align: 'right' });
    y += pinBoxH + 8;

    // ---- Zugangslink ----
    doc.setFont('times', 'bold'); doc.setFontSize(10); doc.text('Ihr persönlicher Zugangslink:', left, y); y += 5.5;
    doc.setFont('courier', 'normal'); doc.setFontSize(9);
    const urlLines = doc.splitTextToSize(url, contentW) as string[];
    doc.text(urlLines, left, y); y += urlLines.length * 4.8 + 4;

    // ---- Hinweis ----
    doc.setFont('times', 'normal'); doc.setFontSize(10);
    const note = 'Bitte behandeln Sie Zugangscode und PIN vertraulich und geben Sie diese nicht an Dritte weiter. Bei Verlust kann jederzeit ein neuer Zugang ausgestellt werden; der bisherige Zugang verliert dann automatisch seine Gültigkeit.';
    const noteLines = doc.splitTextToSize(note, contentW) as string[];
    doc.text(noteLines, left, y); y += noteLines.length * 5.4 + 6;

    // ---- Grußformel ----
    doc.text('Mit freundlichen Grüßen', left, y); y += 14;
    doc.text(companyName, left, y);

    // ---- Fußzeile (Firmendaten, behördlich) ----
    const footY = PH - 22;
    doc.setLineWidth(0.3); doc.setDrawColor(0); doc.line(left, footY - 4, PW - right, footY - 4);
    doc.setFont('times', 'normal'); doc.setFontSize(7.5); doc.setTextColor(0);
    const col = contentW / 3;
    const c1 = [companyName, companyStreet, companyCity].filter(Boolean);
    const c2 = [companyPhone ? 'Tel. ' + companyPhone : '', companyEmail].filter(Boolean);
    const c3 = [companyMgr ? 'Geschäftsführung: ' + companyMgr : '', companyReg ? 'Register: ' + companyReg : '', companyVat ? 'USt-IdNr.: ' + companyVat : ''].filter(Boolean);
    c1.forEach((l, i) => doc.text(l, left, footY + i * 3.6));
    c2.forEach((l, i) => doc.text(l, left + col, footY + i * 3.6));
    c3.forEach((l, i) => doc.text(l, left + col * 2, footY + i * 3.6));

    const safeName = String(customer['name'] || 'Kunde').replace(/[^a-z0-9]+/gi, '_');
    doc.save(`Kundenzugang_${safeName}.pdf`);
  }
}
