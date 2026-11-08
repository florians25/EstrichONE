import { describe, expect, it } from 'vitest';
import { attachmentMedia, fileMedia, fileVariantUrl, indexOf, mediaKind, position, stageHtml, step, thumbInnerHtml, viewable } from '../webapp/util/media';

describe('Medien: Vorschaukacheln und Detailfenster', () => {
  it('erkennt Bild, Video und Sonstiges aus Inhaltstyp oder Endung', () => {
    expect(mediaKind('image/jpeg')).toBe('image');
    expect(mediaKind('video/mp4')).toBe('video');
    expect(mediaKind('application/octet-stream', 'Baustelle.MOV')).toBe('video');
    expect(mediaKind('', 'IMG_0001.HEIC')).toBe('image');
    expect(mediaKind('application/pdf', 'Angebot.pdf')).toBe('other');
    expect(mediaKind(null)).toBe('other');
  });

  it('nutzt für Fotos kleine bzw. große Varianten und für Videos die Datei selbst', () => {
    const photo = fileMedia({ id: 'p1', originalName: 'Bad.jpg', contentType: 'image/jpeg', variants: 'ready', caption: 'Randdämmung' });
    expect(photo).toMatchObject({ kind: 'image', thumb: '/api/v1/files/p1/content?variant=thumb', src: '/api/v1/files/p1/content?variant=large',
      fallback: '/api/v1/files/p1/content', download: '/api/v1/files/p1/content', caption: 'Randdämmung' });
    const pending = fileMedia({ id: 'p2', originalName: 'x.jpg', contentType: 'image/jpeg', variants: 'pending' });
    expect(pending.thumb).toBe('/api/v1/files/p2/content?variant=original');
    const video = fileMedia({ id: 'v1', originalName: 'Rundgang.mp4', contentType: 'video/mp4', variants: 'notApplicable' }, '3 MB');
    expect(video).toMatchObject({ kind: 'video', thumb: '/api/v1/files/v1/content', src: '/api/v1/files/v1/content', meta: '3 MB' });
    expect(fileVariantUrl({ id: 'a', variants: 'ready' }, 'display')).toBe('/api/v1/files/a/content?variant=display');
  });

  it('Anhänge aus Meldungen und Helpdesk', () => {
    expect(attachmentMedia({ id: 'a1', name: 'bildschirm.png', contentType: 'image/png' }).thumb).toBe('/api/v1/files/a1/content?variant=thumb');
    expect(attachmentMedia({ id: 'a2', name: 'log.txt', contentType: 'text/plain' }).kind).toBe('other');
    expect(viewable([{ kind: 'image' as const }, { kind: 'other' as const }, { kind: 'video' as const }])).toHaveLength(2);
  });

  it('blättert im Kreis und zählt mit', () => {
    expect(step(0, -1, 3)).toBe(2);
    expect(step(2, 1, 3)).toBe(0);
    expect(step(1, 1, 3)).toBe(2);
    expect(step(0, 1, 0)).toBe(0);
    expect(indexOf([{ id: 'a' }, { id: 'b' }], 'b')).toBe(1);
    expect(indexOf([{ id: 'a' }], 'x')).toBe(0);
    expect(position(1, 7)).toBe('2 von 7');
    expect(position(0, 1)).toBe('');
  });

  it('erzeugt Kachel und Bühne mit maskierten Namen', () => {
    const tile = thumbInnerHtml({ kind: 'video', thumb: '/v.mp4', name: '<Rundgang>' });
    expect(tile).toContain('<video');
    expect(tile).toContain('#t=0.1');
    expect(tile).toContain('eoMediaThumbPlay');
    expect(thumbInnerHtml({ kind: 'image', thumb: '/t.webp', name: 'a"b' })).toContain('alt="a&#34;b"');
    const stage = stageHtml({ kind: 'video', src: '/v.mp4', name: 'Rundgang' });
    expect(stage).toContain('controls');
    expect(stageHtml({ kind: 'image', src: '/l.webp', name: 'Bad' })).toContain('<img');
    expect(stageHtml({ kind: 'other', src: '/x', name: '<x>' })).toContain('&#60;x&#62;');
  });
});
