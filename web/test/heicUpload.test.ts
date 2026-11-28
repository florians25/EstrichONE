import { afterEach, describe, expect, it, vi } from 'vitest';
import { heicAsJpeg } from '../webapp/util/upload';

describe('HEIC vom iPhone vor dem Hochladen', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lässt JPEG, PNG und Dokumente unverändert', async () => {
    const jpeg = new File([new Uint8Array([0xff, 0xd8, 0xff])], 'Bad.jpg', { type: 'image/jpeg' });
    expect(await heicAsJpeg(jpeg)).toBe(jpeg);
  });

  it('lädt HEIC unverändert hoch, wenn der Browser es nicht dekodieren kann', async () => {
    vi.stubGlobal('createImageBitmap', () => Promise.reject(new Error('nicht unterstützt')));
    const heic = new File([new Uint8Array([0, 0, 0, 24])], 'IMG_0001.HEIC', { type: '' });
    expect(await heicAsJpeg(heic)).toBe(heic);
  });

  it('wandelt HEIC in JPEG um, wenn der Browser es lesen kann (Safari)', async () => {
    vi.stubGlobal('createImageBitmap', () => Promise.resolve({ width: 30, height: 40, close: () => undefined }));
    const draw = vi.fn();
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ drawImage: draw } as unknown as CanvasRenderingContext2D);
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback: BlobCallback) => callback(new Blob([new Uint8Array([0xff, 0xd8])], { type: 'image/jpeg' })));
    const heic = new File([new Uint8Array([0, 0, 0, 24])], 'IMG_0002.heic', { type: 'image/heic', lastModified: 1_700_000_000_000 });
    const result = await heicAsJpeg(heic);
    expect(result.name).toBe('IMG_0002.jpg');
    expect(result.type).toBe('image/jpeg');
    expect(result.lastModified).toBe(1_700_000_000_000);
    expect(draw).toHaveBeenCalled();
  });
});
