import { describe, expect, it } from 'vitest';
import { roomsToText, textToRooms } from '../webapp/util/catalog';

describe('Bauakten-Vorlagen: Räume als Text', () => {
  it('liest Zeilen mit deutschen Zahlen und Fußbodenheizung', () => {
    expect(textToRooms('Wohnen/Essen; EG; 42,5; 65; FBH\nBad; OG\n\n; leer')).toEqual([
      { name: 'Wohnen/Essen', floor: 'EG', areaSquareMeters: 42.5, thicknessMillimeters: 65, underfloorHeating: true },
      { name: 'Bad', floor: 'OG', areaSquareMeters: null, thicknessMillimeters: null, underfloorHeating: false },
    ]);
  });

  it('schreibt Räume zurück, ohne leere Spalten am Ende', () => {
    expect(roomsToText([{ name: 'Bad', floor: 'OG' }, { name: 'Flur', floor: 'EG', areaSquareMeters: 8, thicknessMillimeters: 50, underfloorHeating: true }]))
      .toBe('Bad; OG\nFlur; EG; 8; 50; FBH');
  });
});
