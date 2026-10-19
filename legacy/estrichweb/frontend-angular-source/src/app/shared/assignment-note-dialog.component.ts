import { Component, Inject, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatChipsModule } from '@angular/material/chips';

export interface AssignmentNoteData {
  title: string;
  subtitle?: string;
  notes: string;
  readonly?: boolean;
}

/**
 * Notiz zu einer Einplanung.
 *
 * Bewusst ein kleiner, eigener Dialog statt des vollen Einplanungsdialogs:
 * Im Alltag will man unterwegs schnell eine Bemerkung hinterlassen
 * ("Schlüssel beim Nachbarn", "Zufahrt gesperrt"), ohne Zeiten und
 * Ressourcen erneut bestätigen zu müssen.
 */
@Component({
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatChipsModule],
  template: `
    <h2 mat-dialog-title><mat-icon>sticky_note_2</mat-icon>Notiz zur Einplanung</h2>
    <mat-dialog-content class="anote-content">
      <p class="anote-ref">
        <b>{{ data.title }}</b>
        @if (data.subtitle) { <small>{{ data.subtitle }}</small> }
      </p>

      @if (!data.readonly) {
        <div class="anote-quick">
          @for (q of quick; track q) {
            <button type="button" class="anote-chip" (click)="append(q)">{{ q }}</button>
          }
        </div>
      }

      <mat-form-field appearance="outline" class="anote-field">
        <mat-label>Bemerkung</mat-label>
        <textarea matInput rows="6" [(ngModel)]="text" [readonly]="data.readonly"
                  placeholder="z. B. Zufahrt über Hofseite, Schlüssel beim Bauleiter"></textarea>
        <mat-hint>{{ text.length }} / 1000 Zeichen</mat-hint>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-stroked-button mat-dialog-close>Schließen</button>
      @if (!data.readonly) {
        @if (data.notes) { <button mat-stroked-button class="anote-clear" (click)="clear()"><mat-icon>backspace</mat-icon>Notiz löschen</button> }
        <button mat-flat-button color="primary" (click)="save()" [disabled]="text.length > 1000"><mat-icon>save</mat-icon>Speichern</button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    .anote-content { padding-top: 6px !important; min-width: min(430px, 86vw); }
    .anote-ref { margin: 0 0 12px; padding: 10px 12px; background: #f6f8fa; border-radius: 8px; }
    .anote-ref b { display: block; font-size: .94rem; color: #101828; }
    .anote-ref small { color: #667085; font-size: .8rem; }
    .anote-quick { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 12px; }
    .anote-chip {
      border: 1px solid #cfd6e0; border-radius: 999px; background: #fff;
      padding: 5px 11px; font: inherit; font-size: .78rem; color: #344054; cursor: pointer;
    }
    .anote-chip:hover { border-color: #14396f; background: #f2f6fc; color: #14396f; }
    .anote-field { width: 100%; }
    .anote-clear .mat-icon, .anote-clear .mdc-button__label { color: #b42318 !important; }
  `]
})
export class AssignmentNoteDialogComponent {
  private readonly ref = inject(MatDialogRef<AssignmentNoteDialogComponent>);
  text = '';
  /** Häufige Bemerkungen aus dem Baustellenalltag – ein Tipp genügt. */
  readonly quick = ['Zufahrt beachten', 'Schlüssel beim Bauleiter', 'Material vor Ort', 'Strom bauseits', 'Anfahrt früher', 'Rückruf Kunde'];

  constructor(@Inject(MAT_DIALOG_DATA) public data: AssignmentNoteData) {
    this.text = String(data.notes || '');
  }
  append(value: string): void {
    this.text = this.text.trim() ? `${this.text.trim()}\n${value}` : value;
  }
  clear(): void { this.ref.close({ notes: '' }); }
  save(): void { this.ref.close({ notes: this.text.trim() }); }
}
