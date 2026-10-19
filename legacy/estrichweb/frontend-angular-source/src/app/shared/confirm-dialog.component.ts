import { Component, Inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  icon?: string;
  tone?: 'warn' | 'primary';
}

@Component({
  selector: 'ew-confirm-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title class="confirm-title"><mat-icon>{{ data.icon || 'help' }}</mat-icon>{{ data.title }}</h2>
    <mat-dialog-content class="confirm-dialog-content">
      <p>{{ data.message }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end" class="confirm-actions">
      <!-- cancelText: '' blendet den Abbrechen-Knopf aus (reine Hinweismeldung). -->
      @if (data.cancelText !== '') {
        <button mat-stroked-button type="button" (click)="close(false)">{{ data.cancelText || 'Abbrechen' }}</button>
      }
      <button mat-flat-button [color]="data.tone === 'warn' ? 'warn' : 'primary'" type="button" (click)="close(true)">
        <mat-icon>{{ confirmIcon() }}</mat-icon>{{ data.confirmText || 'Bestätigen' }}
      </button>
    </mat-dialog-actions>
  `
})
export class ConfirmDialogComponent {
  /** Symbol des Bestätigen-Knopfs: bei Löschvorgängen der Papierkorb,
   *  sonst ein Haken. Frueher stand bei JEDER Warnung ein Papierkorb –
   *  auch bei reinen Hinweisen wie Planungskonflikten. */
  confirmIcon(): string {
    if (this.data.icon === 'delete' || this.data.icon === 'delete_forever') return 'delete';
    return 'check';
  }
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: ConfirmDialogData,
    private readonly ref: MatDialogRef<ConfirmDialogComponent>
  ) {}

  close(value: boolean): void { this.ref.close(value); }
}
