import { Component, computed, input } from '@angular/core';

/**
 * Linien-Symbole (24×24, eigene Zeichnungen im Stil der Fachanwendungen): dünne Striche, keine Füllung.
 * Rein dekorativ – die Bedeutung steht immer auch als Text daneben oder im aria-label des Bedienelements.
 */
const paths: Record<string, string[]> = {
  home: ['M3 10.5 12 3l9 7.5', 'M5 9.5V21h14V9.5', 'M10 21v-6h4v6'],
  overview: ['M4 4h7v7H4z', 'M13 4h7v7h-7z', 'M4 13h7v7H4z', 'M13 13h7v7h-7z'],
  bauakte: ['M4 21V5l8-3v19', 'M12 8h8v13', 'M2 21h20', 'M7 8h2M7 12h2M7 16h2M15 12h2M15 16h2'],
  inbox: ['M3 13h5l1.5 3h5L16 13h5', 'M5.5 5h13L21 13v6H3v-6z'],
  'folder-plus': ['M3 6.5A1.5 1.5 0 0 1 4.5 5H9l2 2h8.5A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z', 'M12 10.5v5M9.5 13h5'],
  customers: ['M9 11.5a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z', 'M2.5 20c.6-3.5 3.3-5.5 6.5-5.5s5.9 2 6.5 5.5', 'M16 4.6a3.5 3.5 0 0 1 0 6.8', 'M18 14.8c2 .7 3.3 2.5 3.6 5.2'],
  user: ['M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z', 'M4 21c.8-4 4-6 8-6s7.2 2 8 6'],
  calendar: ['M4 5h16v16H4z', 'M4 10h16', 'M8 3v4M16 3v4', 'M8 14h2M12 14h2M16 14h0M8 17h2M12 17h2'],
  crew: ['M4 17h16v2.5H4z', 'M6 17v-3a6 6 0 0 1 12 0v3', 'M10 9V6.5h4V9'],
  truck: ['M2.5 6h11v10h-11z', 'M13.5 9.5h4L21 13v3h-7.5', 'M7 18.5a2 2 0 1 0 0-4 2 2 0 0 0 0 4zM17 18.5a2 2 0 1 0 0-4 2 2 0 0 0 0 4z'],
  absence: ['M4 5h16v16H4z', 'M4 10h16', 'M8 3v4M16 3v4', 'M9.5 13.5l5 5M14.5 13.5l-5 5'],
  device: ['M7 2.5h10a1 1 0 0 1 1 1v17a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1v-17a1 1 0 0 1 1-1z', 'M11 18.5h2'],
  helpdesk: ['M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z', 'M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8z', 'M5.6 5.6l3.6 3.6M14.8 14.8l3.6 3.6M18.4 5.6l-3.6 3.6M9.2 14.8l-3.6 3.6'],
  users: ['M12 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z', 'M5 20c.7-3.5 3.5-5.5 7-5.5s6.3 2 7 5.5', 'M20.5 8.5l-2 2-1-1'],
  backup: ['M12 8c4.4 0 8-1.3 8-3s-3.6-3-8-3-8 1.3-8 3 3.6 3 8 3z', 'M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5', 'M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3'],
  search: ['M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z', 'M20 20l-4-4'],
  plus: ['M12 5v14M5 12h14'],
  edit: ['M4 20h4L19 9l-4-4L4 16z', 'M13.5 6.5l4 4'],
  trash: ['M4 7h16', 'M10 11v6M14 11v6', 'M6 7l1 13h10l1-13', 'M9 7V4h6v3'],
  refresh: ['M20 11a8 8 0 0 0-14.3-4.9L4 8', 'M4 3v5h5', 'M4 13a8 8 0 0 0 14.3 4.9L20 16', 'M20 21v-5h-5'],
  print: ['M6 9V3h12v6', 'M5 17H3V9h18v8h-2', 'M6 14h12v7H6z'],
  download: ['M12 4v12', 'M7 11l5 5 5-5', 'M4 20h16'],
  upload: ['M12 16V4', 'M7 9l5-5 5 5', 'M4 20h16'],
  'chevron-down': ['M6 9l6 6 6-6'],
  'chevron-right': ['M9 6l6 6-6 6'],
  'chevron-left': ['M15 6l-6 6 6 6'],
  rooms: ['M3 4h18v16H3z', 'M3 12h9V4', 'M12 12v8', 'M16 12h5'],
  tasks: ['M9 6h11M9 12h11M9 18h11', 'M4 5.5l1 1 2-2M4 11.5l1 1 2-2M4 17.5l1 1 2-2'],
  note: ['M5 3h10l4 4v14H5z', 'M15 3v4h4', 'M8 11h8M8 15h8M8 7h4'],
  photo: ['M3 6h18v14H3z', 'M8.5 12a2 2 0 1 0 0-4 2 2 0 0 0 0 4z', 'M21 16l-5-5-8 8'],
  inspection: ['M5 4h14v17H5z', 'M9 4V2.5h6V4', 'M8.5 13l2.5 2.5 4.5-4.5'],
  history: ['M3 12a9 9 0 1 0 2.6-6.4L3 8', 'M3 3v5h5', 'M12 7v5l3 2'],
  'log-out': ['M15 4h4v16h-4', 'M10 8l-4 4 4 4', 'M6 12h10'],
  key: ['M8 19a4 4 0 1 0 0-8 4 4 0 0 0 0 8z', 'M11 12l8.5-8.5M16.5 6.5l2 2M14.5 8.5l2 2'],
  help: ['M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z', 'M9.5 9.2a2.5 2.5 0 1 1 3.4 2.3c-.6.3-.9.9-.9 1.5v.5', 'M12 16.5v.5'],
  menu: ['M4 6h16M4 12h16M4 18h16'],
  warning: ['M12 3.5 2.5 20h19z', 'M12 10v4.5', 'M12 17v.5'],
  pin: ['M12 21s-7-6.2-7-12a7 7 0 0 1 14 0c0 5.8-7 12-7 12z', 'M12 11.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z'],
  phone: ['M5 3h4l2 5-2.5 1.5a11 11 0 0 0 6 6L16 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 5a2 2 0 0 1 2-2z'],
  close: ['M6 6l12 12M18 6 6 18'],
  check: ['M5 12.5l4.5 4.5L19 7.5'],
  pump: ['M4 18h10v3H4z', 'M6 18v-6h6v6', 'M12 13h4l3-5h2', 'M8 12V8h2v4'],
  settings: ['M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1', 'M15 8a2 2 0 1 0 0-4 2 2 0 0 0 0 4zM9 14a2 2 0 1 0 0-4 2 2 0 0 0 0 4zM17 20a2 2 0 1 0 0-4 2 2 0 0 0 0 4z'],
};

@Component({
  selector: 'eo-icon',
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 24 24" fill="none" stroke="currentColor"
      [attr.stroke-width]="stroke()" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">
      @for (d of shape(); track $index) { <path [attr.d]="d" /> }
    </svg>
  `,
  styles: `:host { display: inline-flex; flex: none; line-height: 0; }`,
})
export class IconComponent {
  readonly name = input.required<string>();
  readonly size = input(18);
  readonly stroke = input(1.6);
  protected readonly shape = computed(() => paths[this.name()] ?? paths['help']);
}

export const iconNames = Object.keys(paths);
