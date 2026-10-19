import { Component, Inject, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { jsPDF } from 'jspdf';
import { GenericRecord } from '../../core/api.service';

export interface MaterialOrderDialogData {
  project?: GenericRecord | null;
  sites?: GenericRecord[];
  suppliers?: GenericRecord[];
  order?: GenericRecord | null;
}

export const MATERIAL_ORDER_STATUSES = [
  { value: 'to_order', label: 'Zu bestellen' },
  { value: 'ordered', label: 'Bestellt' },
  { value: 'done', label: 'Erledigt' },
  { value: 'cancelled', label: 'Abgesagt' }
];

export function materialOrderStatusLabel(status: unknown): string {
  return MATERIAL_ORDER_STATUSES.find((s) => s.value === String(status || ''))?.label || 'Zu bestellen';
}

function safeFilename(value: unknown): string { return String(value || 'Bestellung').replace(/[^a-z0-9äöüß._-]+/gi, '_').replace(/^_+|_+$/g, '') || 'Bestellung'; }
function moneylessQuantity(q: unknown): string { const n = Number(q); return Number.isFinite(n) && n !== 0 ? new Intl.NumberFormat('de-DE', { maximumFractionDigits: 3 }).format(n) : ''; }

function formatDateDe(value: unknown): string {
  if (!value) return '';
  const d = new Date(String(value));
  return Number.isNaN(d.getTime()) ? String(value) : new Intl.DateTimeFormat('de-DE').format(d);
}
function compactAddress(parts: unknown[]): string { return parts.map((v) => String(v || '').trim()).filter(Boolean).join(' '); }

export function downloadMaterialOrderPdf(order: GenericRecord, project: GenericRecord | null = null, company: GenericRecord | null = null): void {
  // Design analog zum Kundenportal-Zugangsbrief: DIN 5008, Times-Serifenschrift, mm.
  const doc = new jsPDF({ unit: 'mm', format: 'a4' });
  const PW = 210, PH = 297;
  const left = 25, right = 20;
  const contentW = PW - left - right;
  const co = company || {};
  const companyName = String(co['name'] || co['company_name'] || 'EstrichWeb');
  const companyStreet = String(co['street'] || co['address'] || '');
  const companyCity = [String(co['zip'] || ''), String(co['city'] || '')].filter(Boolean).join(' ');
  const companyPhone = String(co['phone'] || '');
  const companyEmail = String(co['email'] || '');
  const companyWeb = String(co['website'] || '');
  const companyMgr = String(co['ceo'] || co['managing_director'] || '');
  const companyVat = String(co['vat'] || co['vat_id'] || co['ustid'] || '');
  const orderNo = String(order['order_no'] || 'Materialbestellung').trim();
  const today = new Date().toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
  const statusLabel = materialOrderStatusLabel(order['status']);
  const projectLine = [project?.['project_no'] || order['project_no'], project?.['name'] || order['project_name']].filter(Boolean).join(' · ') || '—';
  const siteLine = [order['site_no'], order['site_name']].filter(Boolean).join(' · ') || '—';
  // Lieferadresse inkl. PLZ + Ort (aus Bestellung, sonst Baustelle/Bauakte)
  const deliveryBase = String(order['delivery_address'] || project?.['address'] || '').trim();
  const deliveryCity = [String(order['delivery_zip'] || order['site_zip'] || project?.['zip'] || ''), String(order['delivery_city'] || order['site_city'] || project?.['city'] || '')].filter(Boolean).join(' ').trim();
  const deliveryAddress = [deliveryBase, deliveryCity && !deliveryBase.includes(deliveryCity) ? deliveryCity : ''].filter(Boolean).join('\n');
  const items = Array.isArray(order['items']) ? order['items'] as GenericRecord[] : [];

  const supplier: string[] = [];
  supplier.push(String(order['supplier_name'] || 'Lieferant'));
  if (order['supplier_contact_person']) supplier.push('z. Hd. ' + String(order['supplier_contact_person']));
  if (order['supplier_address']) supplier.push(String(order['supplier_address']));
  const supCity = compactAddress([order['supplier_zip'], order['supplier_city']]);
  if (supCity) supplier.push(supCity);

  let page = 1;
  const black = () => doc.setTextColor(0, 0, 0);

  const header = (): void => {
    black();
    doc.setFont('times', 'bold'); doc.setFontSize(18);
    doc.text(companyName.toUpperCase(), left, 22);
    doc.setFont('times', 'normal'); doc.setFontSize(9);
    const hr: string[] = [];
    if (companyStreet) hr.push(companyStreet);
    if (companyCity) hr.push(companyCity);
    if (companyPhone) hr.push('Tel. ' + companyPhone);
    if (companyEmail) hr.push(companyEmail);
    const hrStartY = 23 - Math.max(0, hr.length - 1) * 4.0;
    hr.forEach((line, i) => doc.text(line, PW - right, hrStartY + i * 4.0, { align: 'right' }));
    doc.setLineWidth(0.6); doc.setDrawColor(0); doc.line(left, 26, PW - right, 26);
  };

  const footer = (): void => {
    doc.setLineWidth(0.2); doc.setDrawColor(150); doc.line(left, PH - 18, PW - right, PH - 18);
    doc.setFont('times', 'normal'); doc.setFontSize(7); doc.setTextColor(110, 110, 110);
    const f1 = [companyName, companyStreet, companyCity].filter(Boolean).join(' · ');
    const f2 = [companyPhone ? 'Tel. ' + companyPhone : '', companyEmail, companyWeb].filter(Boolean).join(' · ');
    const f3 = [companyMgr ? 'Geschäftsführung: ' + companyMgr : '', companyVat ? 'USt-IdNr.: ' + companyVat : ''].filter(Boolean).join(' · ');
    if (f1) doc.text(f1, PW / 2, PH - 14, { align: 'center' });
    if (f2) doc.text(f2, PW / 2, PH - 11, { align: 'center' });
    if (f3) doc.text(f3, PW / 2, PH - 8, { align: 'center' });
    doc.text('Materialbestellung ' + orderNo, left, PH - 14);
    doc.text('Seite ' + page, PW - right, PH - 14, { align: 'right' });
    black();
  };

  header();

  // ---- Schmaler Code39-Barcode der Bestellnummer ----
  const code39Map: Record<string, string> = {
    '0':'nnnwwnwnn','1':'wnnwnnnnw','2':'nnwwnnnnw','3':'wnwwnnnnn','4':'nnnwwnnnw',
    '5':'wnnwwnnnn','6':'nnwwwnnnn','7':'nnnwnnwnw','8':'wnnwnnwnn','9':'nnwwnnwnn',
    'A':'wnnnnwnnw','B':'nnwnnwnnw','C':'wnwnnwnnn','D':'nnnnwwnnw','E':'wnnnwwnnn',
    'F':'nnwnwwnnn','G':'nnnnnwwnw','H':'wnnnnwwnn','I':'nnwnnwwnn','J':'nnnnwwwnn',
    'K':'wnnnnnnww','L':'nnwnnnnww','M':'wnwnnnnwn','N':'nnnnwnnww','O':'wnnnwnnwn',
    'P':'nnwnwnnwn','Q':'nnnnnnwww','R':'wnnnnnwwn','S':'nnwnnnwwn','T':'nnnnwnwwn',
    'U':'wwnnnnnnw','V':'nwwnnnnnw','W':'wwwnnnnnn','X':'nwnnwnnnw','Y':'wwnnwnnnn',
    'Z':'nwwnwnnnn','-':'nwnnnnwnw','.':'wwnnnnwnn',' ':'nwwnnnwnn','*':'nwnnwnwnn'
  };
  const barElements = (text: string): Array<[number, boolean]> => {
    const chars = ('*' + text.toUpperCase().replace(/[^0-9A-Z. -]/g, '') + '*').split('');
    const out: Array<[number, boolean]> = [];
    chars.forEach((ch) => { const pat = code39Map[ch] || code39Map['*']; for (let i = 0; i < pat.length; i++) out.push([pat[i] === 'w' ? 3 : 1, i % 2 === 0]); out.push([1, false]); });
    return out;
  };
  const barWidth = (text: string, narrow: number): number => barElements(text).reduce((s, e) => s + e[0] * narrow, 0);
  const drawBarcode = (text: string, xRight: number, yTop: number, height: number, narrow = 0.3): void => {
    const els = barElements(text); let cx = xRight - barWidth(text, narrow); doc.setFillColor(0, 0, 0);
    els.forEach(([w, bar]) => { const ww = w * narrow; if (bar) doc.rect(cx, yTop, ww, height, 'F'); cx += ww; });
  };
  drawBarcode(orderNo, PW - right, 84, 9, 0.3);
  doc.setFont('times', 'normal'); doc.setFontSize(7); doc.setTextColor(90, 90, 90);
  doc.text(orderNo, PW - right, 96, { align: 'right' });
  black();

  // Rücksendeangabe + Empfänger (Lieferant)
  doc.setFont('times', 'normal'); doc.setFontSize(7);
  doc.text([companyName, companyStreet, companyCity].filter(Boolean).join(' · '), left, 45);
  doc.setLineWidth(0.2); doc.line(left, 46.5, left + 85, 46.5);
  doc.setFont('times', 'normal'); doc.setFontSize(11);
  supplier.forEach((line, i) => doc.text(line, left, 52 + i * 5));

  // Informationsblock rechts
  doc.setFont('times', 'normal'); doc.setFontSize(10);
  const infoX = 125; let iy = 52;
  const infoRow = (k: string, v: string): void => { if (!v) return; doc.text(k, infoX, iy); const vl = doc.splitTextToSize(v, PW - right - infoX - 22) as string[]; doc.text(vl.slice(0, 2), PW - right, iy, { align: 'right' }); iy += 5.5 * Math.max(1, Math.min(2, vl.length)); };
  infoRow('Bestell-Nr.:', orderNo);
  infoRow('Datum:', today);
  infoRow('Status:', statusLabel);
  infoRow('Bauakte:', projectLine);
  if (order['requested_delivery_date']) infoRow('Lieferdatum:', formatDateDe(order['requested_delivery_date']));

  // Betreff
  let y = 104;
  doc.setFont('times', 'bold'); doc.setFontSize(12);
  doc.text('Materialbestellung ' + orderNo, left, y); y += 9;

  // Anrede / Einleitung
  doc.setFont('times', 'normal'); doc.setFontSize(11);
  doc.text('Sehr geehrte Damen und Herren,', left, y); y += 7;
  const intro = 'hiermit bestellen wir die nachfolgend aufgeführten Materialien für das genannte Bauvorhaben. Wir bitten um Lieferung gemäß den angegebenen Daten.';
  const introLines = doc.splitTextToSize(intro, contentW) as string[];
  doc.text(introLines, left, y); y += introLines.length * 5.4 + 4;

  // Baustelle / Lieferadresse
  doc.setFont('times', 'bold'); doc.setFontSize(10); doc.text('Baustelle:', left, y);
  doc.setFont('times', 'normal'); doc.text(siteLine, left + 30, y); y += 5.5;
  if (deliveryAddress) {
    doc.setFont('times', 'bold'); doc.text('Lieferadresse:', left, y);
    doc.setFont('times', 'normal');
    const dl = doc.splitTextToSize(deliveryAddress, contentW - 32) as string[];
    doc.text(dl, left + 30, y); y += dl.length * 5;
  }
  y += 4;

  // Positionstabelle
  const cols = [16, contentW - 16 - 28 - 26, 28, 26];
  const drawTableHead = (): void => {
    doc.setFont('times', 'bold'); doc.setFontSize(10); black();
    doc.setDrawColor(0); doc.setLineWidth(0.3);
    let x = left;
    const heads = ['Pos.', 'Material / Beschreibung', 'Menge', 'Einheit'];
    doc.line(left, y, PW - right, y);
    heads.forEach((h, i) => { doc.text(h, x + 2, y + 5); x += cols[i]; });
    y += 7;
    doc.line(left, y, PW - right, y);
  };
  const pageBreak = (): void => { footer(); doc.addPage(); page += 1; y = 20; header(); y = 40; };
  drawTableHead();
  doc.setFont('times', 'normal'); doc.setFontSize(10);
  if (!items.length) { doc.text('Keine Positionen erfasst.', left + 2, y + 6); y += 9; }
  items.forEach((item, idx) => {
    const desc = String(item['description'] || 'Material').trim();
    const notes = String(item['notes'] || '').trim();
    const txt = notes ? desc + ' – ' + notes : desc;
    const lines = doc.splitTextToSize(txt, cols[1] - 4) as string[];
    const rowH = Math.max(7, lines.length * 5 + 2);
    if (y + rowH > PH - 40) { pageBreak(); drawTableHead(); doc.setFont('times', 'normal'); doc.setFontSize(10); }
    let x = left;
    doc.text(String(idx + 1), x + 2, y + 5); x += cols[0];
    doc.text(lines, x + 2, y + 5); x += cols[1];
    doc.text(moneylessQuantity(item['quantity']) || '—', x + 2, y + 5); x += cols[2];
    doc.text(String(item['unit'] || '—'), x + 2, y + 5);
    y += rowH;
    doc.setDrawColor(210); doc.setLineWidth(0.1); doc.line(left, y, PW - right, y);
  });
  doc.setDrawColor(0); doc.setLineWidth(0.3); doc.line(left, y, PW - right, y); y += 8;

  // Hinweise
  if (order['notes']) {
    if (y > PH - 60) pageBreak();
    doc.setFont('times', 'bold'); doc.setFontSize(10); doc.text('Hinweise:', left, y); y += 5.5;
    doc.setFont('times', 'normal');
    const nl = doc.splitTextToSize(String(order['notes']), contentW) as string[];
    doc.text(nl, left, y); y += nl.length * 5 + 4;
  }

  // Grußformel + Unterschrift
  if (y > PH - 55) pageBreak();
  y += 6;
  doc.setFont('times', 'normal'); doc.setFontSize(11);
  doc.text('Mit freundlichen Grüßen', left, y); y += 8;
  doc.text(companyName, left, y); y += 24;
  doc.setDrawColor(0); doc.setLineWidth(0.3);
  doc.line(left, y, left + 70, y);
  doc.setFontSize(8); doc.setTextColor(110, 110, 110);
  doc.text('Ort, Datum, Unterschrift', left, y + 5);

  footer();
  doc.save(safeFilename(orderNo) + '.pdf');
}


@Component({
  standalone: true,
  selector: 'ew-material-order-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title class="material-dialog-title"><mat-icon>inventory_2</mat-icon>{{ data.order ? 'Materialbestellung bearbeiten' : 'Materialbestellung erfassen' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="form-grid dialog-form material-order-form">
        <mat-form-field appearance="outline"><mat-label>Lieferant</mat-label><mat-select formControlName="supplier_id" (openedChange)="supplierSearch.set('')"><mat-option value="">Ohne Lieferant</mat-option><div class="select-search-box" (click)="$event.stopPropagation()"><input placeholder="Lieferant suchen…" [value]="supplierSearch()" (input)="supplierSearch.set($any($event.target).value)" (keydown)="$event.stopPropagation()"></div>@for (s of filteredSuppliers(); track s['id']) { <mat-option [value]="s['id']">{{ s['name'] }} @if (s['city']) { · {{ s['city'] }} }</mat-option> }</mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Status</mat-label><mat-select formControlName="status">@for (s of statuses; track s.value) { <mat-option [value]="s.value">{{ s.label }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Baustelle</mat-label><mat-select formControlName="site_id"><mat-option value="">Ohne Zuordnung</mat-option>@for (site of sites; track site['id']) { <mat-option [value]="site['id']">{{ site['site_no'] || 'Baustelle' }} · {{ site['name'] }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Wunsch-Lieferdatum</mat-label><input matInput type="date" formControlName="requested_delivery_date"></mat-form-field>
        <mat-form-field class="wide" appearance="outline"><mat-label>Lieferadresse</mat-label><input matInput formControlName="delivery_address"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Ansprechpartner</mat-label><input matInput formControlName="contact_name"></mat-form-field>
        <mat-form-field class="wide" appearance="outline"><mat-label>Notizen</mat-label><textarea matInput rows="2" formControlName="notes"></textarea></mat-form-field>

        <div class="wide material-items-head"><strong>Positionen</strong><button mat-stroked-button type="button" (click)="addItem()"><mat-icon>add</mat-icon>Position</button></div>
        <div formArrayName="items" class="wide material-items-list">
          @for (ctrl of items.controls; track $index; let i = $index) {
            <div class="material-item-row" [formGroupName]="i">
              <mat-form-field appearance="outline"><mat-label>Material *</mat-label><input matInput formControlName="description"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Menge</mat-label><input matInput type="number" step="0.01" formControlName="quantity"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Einheit</mat-label><input matInput formControlName="unit" placeholder="z. B. Sack, m²"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Notiz</mat-label><input matInput formControlName="notes"></mat-form-field>
              <button mat-icon-button type="button" (click)="removeItem(i)" [disabled]="items.length <= 1" aria-label="Position entfernen"><mat-icon>delete</mat-icon></button>
            </div>
          }
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Abbrechen</button><button mat-flat-button color="primary" [disabled]="form.invalid" (click)="save()"><mat-icon>save</mat-icon>Speichern</button></mat-dialog-actions>
  `
})
export class MaterialOrderDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly ref = inject(MatDialogRef<MaterialOrderDialogComponent>);
  readonly statuses = MATERIAL_ORDER_STATUSES;
  readonly suppliers: GenericRecord[];
  readonly supplierSearch = signal('');
  filteredSuppliers(): GenericRecord[] {
    const q = this.supplierSearch().trim().toLowerCase();
    if (!q) return this.suppliers;
    return this.suppliers.filter((s) => [s['name'], s['city'], s['supplier_no'], s['email']].map((v) => String(v || '').toLowerCase()).some((v) => v.includes(q)));
  }
  readonly sites: GenericRecord[];
  readonly form = this.fb.group({
    supplier_id: [''], site_id: [''], status: ['to_order'], requested_delivery_date: [''], delivery_address: [''], contact_name: [''], notes: [''],
    items: this.fb.array([])
  });

  constructor(@Inject(MAT_DIALOG_DATA) public data: MaterialOrderDialogData) {
    this.suppliers = data.suppliers || [];
    this.sites = data.sites || [];
    const order = data.order || {};
    this.form.patchValue({
      supplier_id: String(order['supplier_id'] || ''), site_id: String(order['site_id'] || ''), status: String(order['status'] || 'to_order'),
      requested_delivery_date: String(order['requested_delivery_date'] || '').slice(0, 10), delivery_address: String(order['delivery_address'] || ''),
      contact_name: String(order['contact_name'] || ''), notes: String(order['notes'] || '')
    });
    const orderItems = Array.isArray(order['items']) && order['items'].length ? order['items'] as GenericRecord[] : [{ description: '', quantity: 1, unit: '', notes: '' }];
    orderItems.forEach((item) => this.addItem(item));
  }

  get items(): FormArray { return this.form.get('items') as FormArray; }
  addItem(item: GenericRecord = {}): void { this.items.push(this.fb.group({ description: [String(item['description'] || ''), Validators.required], quantity: [Number(item['quantity'] || 1)], unit: [String(item['unit'] || '')], notes: [String(item['notes'] || '')] })); }
  removeItem(index: number): void { if (this.items.length > 1) this.items.removeAt(index); }
  save(): void { const raw = this.form.getRawValue(); this.ref.close({ ...raw, items: raw.items || [] }); }
}
