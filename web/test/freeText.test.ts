import { describe, expect, it } from 'vitest';
import { freeTextBlocks, freeTextHtml, isLong, telHref } from '../webapp/util/freeText';
import { viewGroups, type FormSchema } from '../webapp/util/formSchema';
import { chatTeamsUrl, filesViaTeams, FILES_VIA_TEAMS_TEXT } from '../webapp/util/teams';

const schema: FormSchema = {
  id: 's', key: 'bauakte', version: 1, lists: {},
  groups: [
    { key: 'site', label: 'Baustelle', fields: [
      { key: 'floors', label: 'Geschosse', type: 'text', path: 'project.floors', builtin: true },
    ] },
    { key: 'extra', label: 'Sonstiges', fields: [
      { key: 'remarks', label: 'Bemerkungen', type: 'textarea', path: 'project.custom.remarks' },
      { key: 'gate', label: 'Anlieferung', type: 'textarea', path: 'project.custom.gate' },
    ] },
  ],
};

describe('Freitext als HTML', () => {
  it('escapt HTML und erhält Zeilenumbrüche', () => {
    expect(freeTextHtml('Zufahrt <b>hinten</b>\nSchlüssel beim Hausmeister')).toBe('Zufahrt &lt;b&gt;hinten&lt;/b&gt;<br>Schlüssel beim Hausmeister');
    expect(freeTextHtml('a\r\nb')).toBe('a<br>b');
    expect(freeTextHtml(null)).toBe('');
  });

  it('erkennt Links, E-Mail-Adressen und Telefonnummern', () => {
    const html = freeTextHtml('Plan: https://example.com/plan.pdf. Mail bau@firma.de, Tel. 0871 123456 oder +49 (0)171 2233445');
    expect(html).toContain('<a href="https://example.com/plan.pdf" target="_blank" rel="noopener noreferrer">https://example.com/plan.pdf</a>.');
    expect(html).toContain('<a href="mailto:bau@firma.de">bau@firma.de</a>');
    expect(html).toContain('<a href="tel:0871123456">0871 123456</a>');
    expect(html).toContain('<a href="tel:+491712233445">+49 (0)171 2233445</a>');
    expect(freeTextHtml('www.estrich.de')).toContain('href="https://www.estrich.de"');
  });

  it('macht kurze Zahlen und Datumsangaben nicht zu Telefonnummern', () => {
    expect(freeTextHtml('Ab 08.12.2026, 45 mm, PLZ 84028')).not.toContain('<a');
  });

  it('kein javascript: als Link', () => {
    expect(freeTextHtml('javascript:alert(1)')).not.toContain('<a');
    expect(telHref('+49 171 / 22')).toBe('tel:+4917122');
  });

  it('kürzt lange Texte', () => {
    expect(isLong('kurz')).toBe(false);
    expect(isLong('x'.repeat(400))).toBe(true);
    expect(isLong('1\n2\n3\n4\n5\n6')).toBe(true);
  });
});

describe('Textblöcke der Bauakte', () => {
  const b = { siteNotes: 'Hund im Hof', description: null, project: { floors: 'EG', custom: { remarks: '', gate: 'Tor 3' } }, plannedStart: null, plannedEnd: null };

  it('Hinweise und Beschreibung immer, Schema-Freitexte nur mit Inhalt', () => {
    const blocks = freeTextBlocks(schema, b as never);
    expect(blocks.map((x) => x.key)).toEqual(['siteNotes', 'description', 'gate']);
    expect(blocks[0]).toMatchObject({ label: 'Hinweise und Zugang', important: true, source: 'bauakte', text: 'Hund im Hof', maxLength: 3000 });
    expect(blocks[1].text).toBe('');
    expect(blocks[2]).toMatchObject({ label: 'Anlieferung', source: 'schema', path: 'project.custom.gate', maxLength: 2000 });
  });

  it('leere Hinweise bleiben als ein Block „Hinweise und Zugang“ sichtbar', () => {
    const blocks = freeTextBlocks(schema, { siteNotes: '', description: 'Text', project: {} } as never);
    expect(blocks.map((x) => x.key)).toEqual(['siteNotes', 'description']);
    expect(blocks[0]).toMatchObject({ label: 'Hinweise und Zugang', text: '' });
  });

  it('ohne Schema nur die Felder der Bauakte', () => {
    expect(freeTextBlocks(null, b as never)).toHaveLength(2);
  });

  it('Spaltenansicht ohne mehrzeilige Felder', () => {
    const groups = viewGroups(schema, b as never, { skipTextareas: true });
    expect(groups).toEqual([{ label: 'Baustelle', rows: [{ label: 'Geschosse', value: 'EG' }] }]);
    expect(viewGroups(schema, b as never)).toHaveLength(2);
  });
});

describe('Dateien und Fotos über Microsoft Teams', () => {
  it('Hinweis aus dem Chat-Status (auch ältere Server)', () => {
    expect(filesViaTeams({ filesViaTeams: true, attachmentsEnabled: false })).toBe(true);
    expect(filesViaTeams({ attachmentsEnabled: false })).toBe(true);
    expect(filesViaTeams({ attachmentsEnabled: true })).toBe(false);
    expect(filesViaTeams(null)).toBe(false);
    expect(FILES_VIA_TEAMS_TEXT).toBe('Dateien und Fotos tauschen Sie über Microsoft Teams aus.');
  });

  it('In Teams öffnen: verknüpfte Unterhaltung vor allgemeinem Ziel, nichts ohne Teams', () => {
    const status = { teamsAvailable: true, teamsWebUrl: 'https://teams.microsoft.com' };
    expect(chatTeamsUrl(status)).toBe('https://teams.microsoft.com');
    expect(chatTeamsUrl(status, { teamsWebUrl: 'https://teams.microsoft.com/l/chat/19%3a1/0' })).toBe('https://teams.microsoft.com/l/chat/19%3a1/0');
    expect(chatTeamsUrl({ teamsAvailable: false, teamsWebUrl: null })).toBe('');
    expect(chatTeamsUrl(status, { teamsWebUrl: 'https://boese.example.com' })).toBe('');
  });
});
