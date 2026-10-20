import { describe, expect, it } from 'vitest';
import { fileSize } from './bauakte/Files';
import { cmNumber, weatherLine } from './bauakte/Records';
import { describeEvent } from './TicketPage';

describe('Anzeigeformate', () => {
  it('Dateigrößen in KB und MB', () => {
    expect(fileSize(200)).toBe('1 KB');
    expect(fileSize(50 * 1024)).toBe('50 KB');
    expect(fileSize(2.5 * 1024 * 1024)).toBe('2,5 MB');
  });

  it('CM-Werte und Wetterzeile im Bautagebuch', () => {
    expect(cmNumber(1.6)).toBe('1,6');
    expect(cmNumber(null)).toBe('–');
    expect(weatherLine({ weather: 'rain', temperatureCelsius: 9, workersPresent: null })).toBe('Regen · 9 °C');
    expect(weatherLine({ weather: null, temperatureCelsius: null, workersPresent: 3 })).toBe('3 anwesend');
  });

  it('Ticket-Ereignisse mit C#-Namen der Werte', () => {
    expect(describeEvent('status', 'InProgress')).toBe('hat den Status geändert: In Bearbeitung');
    expect(describeEvent('priority', 'Urgent')).toBe('hat die Priorität geändert: Dringend');
    expect(describeEvent('assignee', null)).toBe('hat die Zuständigkeit geändert');
  });
});
