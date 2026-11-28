import { describe, expect, it } from 'vitest';
import { waitForImages } from '../webapp/util/printImages';

const image = (state: 'loaded' | 'pending' | 'broken') => {
  const img = document.createElement('img');
  Object.defineProperty(img, 'complete', { configurable: true, get: () => state === 'loaded' });
  Object.defineProperty(img, 'naturalWidth', { configurable: true, get: () => (state === 'broken' ? 0 : 100) });
  img.decode = () => Promise.resolve();
  return img;
};

describe('Druck der Bauakte wartet auf Logo und Fotos', () => {
  it('druckt erst nach dem Laden aller Bilder und zählt fehlende', async () => {
    const late = image('pending');
    const broken = image('pending');
    const waiting = waitForImages([image('loaded'), late, broken]);
    late.dispatchEvent(new Event('load'));
    broken.dispatchEvent(new Event('error'));
    expect(await waiting).toBe(1);
  });

  it('bricht nach der Wartezeit ab statt endlos zu hängen', async () => {
    expect(await waitForImages([image('pending')], 20)).toBe(1);
  });
});
