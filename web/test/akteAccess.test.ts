import { describe, expect, it } from 'vitest';
import { akteAccess } from '../webapp/util/akteAccess';

const me = 'u-me';
const akte = (over: Partial<{ status: 'planned' | 'cancelled'; responsibleUserId: string | null; isAssigned: boolean | null; canEdit: boolean | null }> = {}) =>
  ({ status: 'planned' as const, responsibleUserId: 'u-lead', isAssigned: false, canEdit: false, ...over });

describe('Bauakte: nur ansehen oder bearbeiten', () => {
  it('nicht zugeteilt: nur ansehen, Eintragen angeboten', () => {
    const a = akteAccess(akte(), [], me, true);
    expect(a).toEqual({ writable: false, readOnly: true, canEdit: false, canJoin: true, canLeave: false });
  });

  it('als Beteiligter eingetragen: bearbeiten und austragen', () => {
    const a = akteAccess(akte({ isAssigned: true, canEdit: true }), [{ userId: me, role: 'member' }], me, true);
    expect(a).toEqual({ writable: true, readOnly: false, canEdit: true, canJoin: false, canLeave: true });
  });

  it('zuständige Person kann sich nicht austragen', () => {
    const a = akteAccess(akte({ responsibleUserId: me, isAssigned: true, canEdit: true }), [{ userId: me, role: 'responsible' }], me, true);
    expect(a.canLeave).toBe(false);
    expect(a.canJoin).toBe(false);
  });

  it('„Alle Bauakten bearbeiten“ ohne Zuteilung: bearbeitbar, Eintragen bleibt möglich', () => {
    const a = akteAccess(akte({ canEdit: true }), [], me, true);
    expect(a.readOnly).toBe(false);
    expect(a.canEdit).toBe(true);
    expect(a.canJoin).toBe(true);
  });

  it('eingeplant (Kolonne) gilt als zugeteilt: kein Eintragen nötig', () => {
    expect(akteAccess(akte({ isAssigned: true, canEdit: true }), [], me, true).canJoin).toBe(false);
  });

  it('ohne Recht „Bauakten bearbeiten“ keine Bearbeiten-Knöpfe, auch wenn zugeteilt', () => {
    expect(akteAccess(akte({ isAssigned: true, canEdit: true }), [], me, false).canEdit).toBe(false);
  });

  it('stornierte Bauakte: kein Eintragen', () => {
    expect(akteAccess(akte({ status: 'cancelled' }), [], me, true).canJoin).toBe(false);
  });

  it('älterer Server ohne canEdit: bearbeitbar wie bisher', () => {
    expect(akteAccess(akte({ canEdit: null, isAssigned: null }), [], me, true).writable).toBe(true);
  });
});
