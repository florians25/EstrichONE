import {
  AuditOutlined, CarryOutOutlined, CheckSquareOutlined, ColumnHeightOutlined, ExperimentOutlined, FileSearchOutlined, MoreOutlined,
  SearchOutlined, TeamOutlined, ToolOutlined,
} from '@ant-design/icons';
import type { ReactNode } from 'react';
import type { AssignmentKind, AssignmentView } from '../../api/models';
import { parseDay } from '../../shared/days';

export const kindLabels: Record<AssignmentKind, string> = {
  screed: 'Estrich / Ausführung', initialInspection: 'Erstbegehung', secondInspection: 'Zweitbegehung', cmMeasurement: 'CM-Messung',
  acceptance: 'Abnahme', survey: 'Aufmaß', check: 'Kontrolle', customerAppointment: 'Kundentermin', delivery: 'Lieferung', other: 'Sonstiges',
};

export const kindColors: Record<AssignmentKind, string> = {
  screed: '#0B2D5B', initialInspection: '#7C3AED', secondInspection: '#9F67FF', cmMeasurement: '#0891B2', acceptance: '#16A34A',
  survey: '#CA8A04', check: '#EA580C', customerAppointment: '#DB2777', delivery: '#64748B', other: '#94A3B8',
};

export const kindIcons: Record<AssignmentKind, ReactNode> = {
  screed: <ColumnHeightOutlined />, initialInspection: <FileSearchOutlined />, secondInspection: <SearchOutlined />, cmMeasurement: <ExperimentOutlined />,
  acceptance: <CheckSquareOutlined />, survey: <AuditOutlined />, check: <ToolOutlined />, customerAppointment: <TeamOutlined />,
  delivery: <CarryOutOutlined />, other: <MoreOutlined />,
};

/** Vorschlag für die Tätigkeit je Terminart. */
export const kindActivities: Record<AssignmentKind, string> = {
  screed: 'Estrich einbringen', initialInspection: 'Erstbegehung', secondInspection: 'Zweitbegehung', cmMeasurement: 'CM-Messung',
  acceptance: 'Abnahme', survey: 'Aufmaß', check: 'Kontrolle', customerAppointment: 'Kundentermin', delivery: 'Lieferung', other: '',
};

export const absenceNames: Record<string, string> = { vacation: 'Urlaub', sick: 'krank', training: 'Schulung', other: 'abwesend' };

export const shortDay = (d: string) => parseDay(d).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' });
export const weekdayName = (d: string) => parseDay(d).toLocaleDateString('de-DE', { weekday: 'long' });
export const time = (t: string | null) => t?.slice(0, 5) ?? '';
export const timeRange = (a: Pick<AssignmentView, 'startTime' | 'endTime'>) =>
  a.startTime ? `${time(a.startTime)}${a.endTime ? `–${time(a.endTime)}` : ''}` : '';

/** Ganz kompakte Einsatzkarte für Wochen-, Tages- und Monatsansicht. */
export function AssignmentCard({ a, showPeople, draggable, onOpen, compact }: {
  a: AssignmentView; showPeople?: boolean; draggable?: boolean; onOpen: (a: AssignmentView) => void; compact?: boolean;
}) {
  const color = a.warnings.length ? undefined : a.crewColor ?? kindColors[a.kind];
  return (
    <button type="button" className={`board-item${a.warnings.length ? ' board-warn' : ''}${compact ? ' board-compact' : ''}`}
      style={{ borderLeftColor: color }} draggable={draggable}
      onDragStart={draggable ? (e) => { e.dataTransfer.setData('text/assignment', a.id); e.dataTransfer.effectAllowed = 'move'; } : undefined}
      onClick={() => onOpen(a)}
      aria-label={`${a.activity}, ${a.bauakteTitle}${a.warnings.length ? `, ${a.warnings.length} Hinweise` : ''}`}>
      <span className="board-time tabular">
        <span style={{ color: kindColors[a.kind] }}>{kindIcons[a.kind]}</span> {timeRange(a)}{!compact && <> · {kindLabels[a.kind]}</>}
      </span>
      <strong>{a.bauakteNumber ?? ''} {a.bauakteTitle}</strong>
      {!compact && <span>{a.activity}</span>}
      {!compact && a.resources.length > 0 && <small>{a.resources.map((r) => r.name).join(', ')}</small>}
      {!compact && showPeople && <small>{a.people.map((p) => p.name).join(', ')}</small>}
      {!compact && a.warnings.length > 0 && (
        <small className="board-warning" title={a.warnings.join(' ')}>⚠ {a.warnings[0]}{a.warnings.length > 1 && ` (+${a.warnings.length - 1})`}</small>
      )}
    </button>
  );
}
