import { describe, expect, it } from 'vitest';
import { dialogTitle, itemHtml, noteHtml, splitNotes, VISIBLE_VERSIONS, type ReleaseNote } from '../webapp/util/releaseNotes';

const note = (version: string, items: string[] = ['Punkt'], title = 'Neu'): ReleaseNote =>
  ({ version, title: `EstrichONE ${version}`, sections: [{ title, items }] });

describe('Was gibt’s Neues: sicheres HTML', () => {
  it('maskiert HTML, setzt nur **fett** um', () => {
    expect(itemHtml('**Kennwerte** bearbeiten <img src=x onerror=alert(1)> & "mehr"'))
      .toBe('<strong>Kennwerte</strong> bearbeiten &lt;img src=x onerror=alert(1)&gt; &amp; &quot;mehr&quot;');
    expect(itemHtml('**<b>x</b>**')).toBe('<strong>&lt;b&gt;x&lt;/b&gt;</strong>');
    // Übrig gebliebene Sternchen nicht anzeigen.
    expect(itemHtml('offen **ohne Ende')).toBe('offen ohne Ende');
  });

  it('Liste je Version; Zwischenüberschrift nur, wenn es nicht nur „Neu“ gibt', () => {
    expect(noteHtml(note('1.0.26', ['**A** eins', 'B']))).toBe('<ul><li><strong>A</strong> eins</li><li>B</li></ul>');
    expect(noteHtml(note('1.0.12', ['M'], 'Module <x>'))).toBe('<p><strong>Module &lt;x&gt;</strong></p><ul><li>M</li></ul>');
  });
});

describe('Was gibt’s Neues: Aufteilung und Titel', () => {
  it('die neuesten fünf offen, ältere eingeklappt', () => {
    const notes = ['1.0.26', '1.0.25', '1.0.24', '1.0.23', '1.0.22', '1.0.21', '1.0.20'].map((v) => note(v));
    const { shown, older } = splitNotes(notes);
    expect(shown.map((n) => n.version)).toEqual(['1.0.26', '1.0.25', '1.0.24', '1.0.23', '1.0.22']);
    expect(older.map((n) => n.version)).toEqual(['1.0.21', '1.0.20']);
    expect(shown).toHaveLength(VISIBLE_VERSIONS);
    expect(splitNotes(notes.slice(0, 2)).older).toEqual([]);
  });

  it('Titel nach der neuesten Version', () => {
    expect(dialogTitle([note('1.0.27'), note('1.0.26')], '1.0.27')).toBe('Was gibt’s Neues in Version 1.0.27');
    expect(dialogTitle([], '1.0.27')).toBe('Was gibt’s Neues in Version 1.0.27');
    expect(dialogTitle([])).toBe('Was gibt’s Neues');
  });
});
