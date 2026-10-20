import {
  CalendarOutlined, InboxOutlined, LeftOutlined, PlusOutlined, PrinterOutlined, ReloadOutlined, RightOutlined, SettingOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Badge, Button, Card, Drawer, Empty, Input, List, Segmented, Select, Space, Switch, Tag, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { get, problemMessage, put } from '../../api/client';
import type {
  AssignmentKind, AssignmentView, BacklogItem, BauakteStatus, CrewResponse, DirectoryUser, PlanningRange, ResourceResponse,
} from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { addDays, isoDay, isoWeek, mondayOf, parseDay } from '../../shared/days';
import { bauakteStatusLabels } from '../../shared/status';
import { AssignmentModal, editingOf, newEditing, type Editing } from './AssignmentModal';
import { absenceNames, AssignmentCard, kindColors, kindLabels, shortDay, weekdayName } from './shared';

type View = 'tag' | 'woche' | 'monat';
interface Lane { id: string | null; name: string; color: string | null; members: string[] }

const titles: Record<View, string> = { tag: 'Tageseinteilung', woche: 'Kolonnenplanung', monat: 'Kalender' };
const monthName = (d: string) => parseDay(d).toLocaleDateString('de-DE', { month: 'long', year: 'numeric' });

/** Sichtbare Tage je Ansicht; Monat = volle Wochen (Mo–So) um den Monat. */
function visibleDays(view: View, anchor: string, workWeek: boolean): string[] {
  if (view === 'tag') {
    return [anchor];
  }
  if (view === 'woche') {
    const monday = mondayOf(anchor);
    return Array.from({ length: workWeek ? 5 : 7 }, (_, i) => addDays(monday, i));
  }
  const first = `${anchor.slice(0, 7)}-01`;
  const start = mondayOf(first);
  const last = addDays(`${isoDay(new Date(parseDay(first).getFullYear(), parseDay(first).getMonth() + 1, 1))}`, -1);
  const end = addDays(mondayOf(last), 6);
  const days: string[] = [];
  for (let d = start; d <= end; d = addDays(d, 1)) {
    days.push(d);
  }
  return days;
}

/**
 * Disposition wie in Estrich Web: Kolonnenplanung (Woche), Tageseinteilung (Tag), Kalender (Monat), Filter,
 * Baustellen-Vorrat zum Hineinziehen, Verschieben per Ziehen, Wochen-PDF über die Druckansicht.
 */
export default function PlanningPage({ defaultView = 'woche' }: { defaultView?: View }) {
  const { has } = useAuth();
  const { message } = App.useApp();
  const queries = useQueryClient();
  const today = isoDay(new Date());
  const [params, setParams] = useSearchParams();
  const view = (['tag', 'woche', 'monat'] as View[]).includes(params.get('ansicht') as View) ? params.get('ansicht') as View : defaultView;
  const anchor = /^\d{4}-\d{2}-\d{2}$/.test(params.get('datum') ?? '') ? params.get('datum')! : today;
  const [workWeek, setWorkWeek] = useState(true);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [dropTarget, setDropTarget] = useState<string | null>(null);
  const [backlogOpen, setBacklogOpen] = useState(false);
  const [filter, setFilter] = useState<{ bauakte?: string; crew?: string; status?: BauakteStatus; kind?: AssignmentKind; search: string }>({ search: '' });
  const canPlan = has('planning.manage');
  const days = visibleDays(view, anchor, workWeek);

  const set = (next: { ansicht?: View; datum?: string }) => setParams((p) => {
    if (next.ansicht) p.set('ansicht', next.ansicht);
    if (next.datum) p.set('datum', next.datum);
    return p;
  });
  const step = view === 'tag' ? 1 : view === 'woche' ? 7 : 0;
  const shift = (direction: number) => {
    if (view === 'monat') {
      const d = parseDay(anchor);
      set({ datum: isoDay(new Date(d.getFullYear(), d.getMonth() + direction, 1)) });
    } else {
      set({ datum: addDays(anchor, direction * step) });
    }
  };

  const range = useQuery({ queryKey: ['planning', days[0], days[days.length - 1]], queryFn: () => get<PlanningRange>('planning/range', { from: days[0], to: days[days.length - 1] }) });
  const crews = useQuery({ queryKey: ['crews'], queryFn: () => get<CrewResponse[]>('planning/crews') });
  const resources = useQuery({ queryKey: ['resources'], queryFn: () => get<ResourceResponse[]>('planning/resources') });
  const people = useQuery({ queryKey: ['directory'], queryFn: () => get<DirectoryUser[]>('directory/users'), staleTime: 60_000 });
  const backlog = useQuery({ queryKey: ['backlog'], queryFn: () => get<BacklogItem[]>('planning/backlog') });

  const activeCrews = (crews.data ?? []).filter((c) => c.isActive);
  const lanes: Lane[] = [
    ...activeCrews.filter((c) => !filter.crew || filter.crew === c.id).map((c) => ({ id: c.id as string | null, name: c.name, color: c.color, members: c.memberUserIds })),
    ...(!filter.crew || filter.crew === 'none' ? [{ id: null, name: 'Termine ohne Kolonne', color: null, members: [] }] : []),
  ];
  const all = range.data?.assignments ?? [];
  const assignments = useMemo(() => all.filter((a) => {
    const q = filter.search.trim().toLowerCase();
    return (!filter.bauakte || a.bauakteId === filter.bauakte)
      && (!filter.status || a.bauakteStatus === filter.status)
      && (!filter.kind || a.kind === filter.kind)
      && (!q || [a.bauakteNumber, a.bauakteTitle, a.bauakteAddress, a.activity, ...a.people.map((p) => p.name)].some((v) => v?.toLowerCase().includes(q)));
  }), [all, filter]);
  const absentOn = (d: string) => (range.data?.absences ?? []).filter((a) => a.from <= d && d <= a.to);
  const inCell = (laneId: string | null, d: string) => assignments.filter((a) => a.day === d && (a.crewId ?? null) === laneId);
  const warningCount = assignments.filter((a) => a.warnings.length > 0).length;
  const name = (id: string) => people.data?.find((p) => p.id === id)?.displayName ?? '?';

  /** Einsatz auf anderen Tag oder andere Kolonne ziehen; zusätzliche Mitarbeiter und Geräte bleiben erhalten. */
  const move = async (assignmentId: string, crewId: string | null, day: string) => {
    const a = all.find((x) => x.id === assignmentId);
    if (!a || (a.day === day && (a.crewId ?? null) === crewId)) {
      return;
    }
    const crewMembers = new Set(crews.data?.find((c) => c.id === a.crewId)?.memberUserIds ?? []);
    const extras = a.people.map((p) => p.id).filter((id) => !crewMembers.has(id));
    if (!crewId && extras.length === 0) {
      message.warning('Ohne Kolonne braucht der Einsatz mindestens einen Mitarbeiter. Bitte im Einsatz Mitarbeiter wählen.');
      return;
    }
    try {
      const saved = await put<AssignmentView>(`planning/assignments/${a.id}`, {
        bauakteId: a.bauakteId, kind: a.kind, day, startTime: a.startTime, endTime: a.endTime, activity: a.activity, crewId,
        userIds: extras, resourceIds: a.resources.map((r) => r.id), notes: a.notes,
      });
      void queries.invalidateQueries({ queryKey: ['planning'] });
      if (saved.warnings.length > 0) {
        message.warning(`Verschoben – ${saved.warnings.join(' ')}`);
      } else {
        message.success(`„${a.activity}“ auf ${weekdayName(day)}, ${shortDay(day)} verschoben`);
      }
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  /** Ziehen aus dem Plan (verschieben) oder aus dem Vorrat (neuer Einsatz an dieser Stelle). */
  const dropProps = (laneId: string | null, d: string) => canPlan ? {
    onDragOver: (e: React.DragEvent) => { e.preventDefault(); setDropTarget(`${laneId}|${d}`); },
    onDragLeave: () => setDropTarget((t) => (t === `${laneId}|${d}` ? null : t)),
    onDrop: (e: React.DragEvent) => {
      e.preventDefault();
      setDropTarget(null);
      const assignment = e.dataTransfer.getData('text/assignment');
      const fromBacklog = e.dataTransfer.getData('text/bauakte');
      if (assignment) {
        void move(assignment, laneId, d);
      } else if (fromBacklog) {
        setEditing(newEditing(d, laneId, JSON.parse(fromBacklog) as { id: string; label: string }));
      }
    },
  } : {};
  const dropClass = (laneId: string | null, d: string) => (dropTarget === `${laneId}|${d}` ? ' board-drop' : '');
  const open = (a: AssignmentView) => setEditing(editingOf(a, crews.data ?? []));

  const periodLabel = view === 'tag'
    ? parseDay(anchor).toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' })
    : view === 'woche' ? `KW ${isoWeek(days[0])}` : monthName(anchor);
  const periodSub = view === 'woche' ? `${shortDay(days[0])} – ${shortDay(days[days.length - 1])} ${parseDay(days[days.length - 1]).getFullYear()}` : null;
  const free = (backlog.data ?? []).filter((b) => b.upcomingAssignments === 0).length;

  return (
    <PageContainer
      className="planning-page"
      title={<Space direction="vertical" size={0}><Typography.Text type="secondary" className="overline">Disposition</Typography.Text>{titles[view]}</Space>}
      extra={[
        canPlan && <Button key="new" type="primary" icon={<PlusOutlined />} onClick={() => setEditing(newEditing(view === 'woche' ? (days.includes(today) ? today : days[0]) : anchor, null))}>Einplanung</Button>,
        <Button key="reload" icon={<ReloadOutlined />} onClick={() => { void range.refetch(); void backlog.refetch(); }}>Neu laden</Button>,
        <Button key="print" icon={<PrinterOutlined />} onClick={() => window.print()}>{view === 'woche' ? 'Wochen-PDF' : 'Drucken'}</Button>,
        <Badge key="backlog" count={free} size="small" offset={[-4, 2]}>
          <Button icon={<InboxOutlined />} onClick={() => setBacklogOpen(true)}>Baustellen-Vorrat</Button>
        </Badge>,
        <Link key="res" to="/planung/ressourcen"><Button icon={<SettingOutlined />} aria-label="Kolonnen und Geräte" /></Link>,
      ].filter(Boolean)}
    >
      <Card size="small" className="planning-toolbar" style={{ marginBottom: 12 }}>
        <div className="toolbar-row">
          <Segmented value={view} onChange={(v) => set({ ansicht: v as View })} options={[{ value: 'tag', label: 'Tag' }, { value: 'woche', label: 'Woche' }, { value: 'monat', label: 'Monat' }]} />
          <Space.Compact>
            <Button icon={<LeftOutlined />} aria-label="Zurück" onClick={() => shift(-1)} />
            <div className="period">
              <strong>{periodLabel}</strong>
              {periodSub && <span className="tabular">{periodSub}</span>}
            </div>
            <Button icon={<RightOutlined />} aria-label="Weiter" onClick={() => shift(1)} />
          </Space.Compact>
          <Button icon={<CalendarOutlined />} onClick={() => set({ datum: today })}>{view === 'tag' ? 'Heute' : view === 'woche' ? 'Diese Woche' : 'Dieser Monat'}</Button>
          {view === 'woche' && (
            <Space className="week-toggle"><span>Arbeitswoche</span><Switch size="small" checked={!workWeek} onChange={(v) => setWorkWeek(!v)} aria-label="Standardwoche (Mo–So)" /><span>Standardwoche</span></Space>
          )}
        </div>
        <div className="toolbar-row filters">
          <Select allowClear showSearch optionFilterProp="label" placeholder="Alle Bauakten" style={{ minWidth: 220, flex: 2 }} aria-label="Auftrag"
            value={filter.bauakte} onChange={(v) => setFilter({ ...filter, bauakte: v })}
            options={[...new Map(all.map((a) => [a.bauakteId, `${a.bauakteNumber ?? 'neu'} · ${a.bauakteTitle}`])).entries()].map(([value, label]) => ({ value, label }))} />
          <Select allowClear placeholder="Alle Kolonnen" style={{ minWidth: 170, flex: 1 }} aria-label="Kolonne" value={filter.crew} onChange={(v) => setFilter({ ...filter, crew: v })}
            options={[...activeCrews.map((c) => ({ value: c.id, label: c.name })), { value: 'none', label: 'Termine ohne Kolonne' }]} />
          <Select allowClear placeholder="Alle Status" style={{ minWidth: 150, flex: 1 }} aria-label="Status" value={filter.status} onChange={(v) => setFilter({ ...filter, status: v })}
            options={(['inquiry', 'planned', 'inProgress', 'drying', 'completed'] as BauakteStatus[]).map((s) => ({ value: s, label: bauakteStatusLabels[s] }))} />
          <Select allowClear placeholder="Alle Terminarten" style={{ minWidth: 170, flex: 1 }} aria-label="Terminart" value={filter.kind} onChange={(v) => setFilter({ ...filter, kind: v })}
            options={(Object.keys(kindLabels) as AssignmentKind[]).map((k) => ({ value: k, label: <Space><Badge color={kindColors[k]} />{kindLabels[k]}</Space> }))} />
          <Input.Search allowClear placeholder="Suche" style={{ minWidth: 180, flex: 2 }} aria-label="Suche im Plan" onChange={(e) => setFilter({ ...filter, search: e.target.value })} />
        </div>
      </Card>

      {range.error && <Alert type="error" showIcon message={problemMessage(range.error)} style={{ marginBottom: 12 }} />}
      {warningCount > 0 && (
        <Alert type="warning" showIcon style={{ marginBottom: 12 }}
          message={`${warningCount} ${warningCount === 1 ? 'Einsatz hat' : 'Einsätze haben'} Hinweise (z. B. Urlaub oder Doppelbelegung).`} />
      )}

      {view === 'woche' && (
        <Card size="small" styles={{ body: { padding: 0 } }}>
          <div className="board" role="table" aria-label="Wochenplan" style={{ ['--days' as string]: days.length }}>
            <div className="board-row board-head" role="row">
              <div className="board-cell board-lane" role="columnheader">Kolonne</div>
              {days.map((d) => (
                <div key={d} className={`board-cell${d === today ? ' board-today' : ''}`} role="columnheader">
                  <a onClick={() => set({ ansicht: 'tag', datum: d })}>{parseDay(d).toLocaleDateString('de-DE', { weekday: 'short' }).toUpperCase()}</a>
                  <Typography.Text type="secondary" className="tabular">{shortDay(d)}</Typography.Text>
                </div>
              ))}
            </div>
            <div className="board-row board-absences" role="row">
              <div className="board-cell board-lane" role="rowheader">Abwesend</div>
              {days.map((d) => (
                <div key={d} className="board-cell" role="cell">
                  {absentOn(d).map((a) => <Tag key={a.id} color="orange" bordered={false} style={{ marginInlineEnd: 0 }}>{a.userName} · {absenceNames[a.kind] ?? a.kind}</Tag>)}
                </div>
              ))}
            </div>
            {lanes.map((lane) => {
              const count = assignments.filter((a) => (a.crewId ?? null) === lane.id).length;
              return (
                <div key={lane.id ?? 'none'} className="board-row" role="row">
                  <div className="board-cell board-lane" role="rowheader">
                    <Space size={8} align="start">
                      <span className="swatch" style={{ background: lane.color ?? '#d9d9d9', marginTop: 5 }} aria-hidden="true" />
                      <span>
                        {lane.name}
                        <br /><Typography.Text type="secondary" style={{ fontWeight: 400, fontSize: 12 }}>
                          {count === 0 ? 'Kapazität offen' : `${count} ${count === 1 ? 'Einsatz' : 'Einsätze'}`}{lane.members.length > 0 && ` · ${lane.members.length} Pers.`}
                        </Typography.Text>
                      </span>
                    </Space>
                  </div>
                  {days.map((d) => (
                    <div key={d} className={`board-cell${d === today ? ' board-today' : ''}${dropClass(lane.id, d)}`} role="cell" {...dropProps(lane.id, d)}>
                      {inCell(lane.id, d).map((a) => <AssignmentCard key={a.id} a={a} showPeople={!lane.id} draggable={canPlan} onOpen={open} />)}
                      {canPlan && (
                        <Button type="dashed" size="small" icon={<PlusOutlined />} className="board-add"
                          aria-label={`Einsatz planen: ${lane.name}, ${weekdayName(d)} ${shortDay(d)}`} onClick={() => setEditing(newEditing(d, lane.id))} />
                      )}
                    </div>
                  ))}
                </div>
              );
            })}
          </div>
        </Card>
      )}

      {view === 'tag' && (
        <div className="day-grid">
          {absentOn(anchor).length > 0 && (
            <Card size="small" title="Abwesend" className="day-absent">
              <Space wrap>{absentOn(anchor).map((a) => <Tag key={a.id} color="orange" bordered={false}>{a.userName} · {absenceNames[a.kind] ?? a.kind}</Tag>)}</Space>
            </Card>
          )}
          {lanes.map((lane) => {
            const items = inCell(lane.id, anchor);
            return (
              <Card key={lane.id ?? 'none'} size="small" className={`day-lane${dropClass(lane.id, anchor)}`} {...dropProps(lane.id, anchor)}
                title={<Space><span className="swatch" style={{ background: lane.color ?? '#d9d9d9' }} />{lane.name}</Space>}
                extra={canPlan && <Button size="small" icon={<PlusOutlined />} onClick={() => setEditing(newEditing(anchor, lane.id))}>Termin planen</Button>}>
                {lane.members.length > 0 && (
                  <div style={{ marginBottom: 8 }}>
                    {lane.members.map((m) => {
                      const absent = absentOn(anchor).some((a) => a.userId === m);
                      return <Tag key={m} bordered={false} color={absent ? 'orange' : undefined}>{name(m)}{absent && ' (abwesend)'}</Tag>;
                    })}
                  </div>
                )}
                {items.length === 0
                  ? <Typography.Text type="secondary">Keine Einsätze – Kapazität offen.</Typography.Text>
                  : <div className="day-items">{items.map((a) => <AssignmentCard key={a.id} a={a} showPeople draggable={canPlan} onOpen={open} />)}</div>}
              </Card>
            );
          })}
        </div>
      )}

      {view === 'monat' && (
        <Card size="small" styles={{ body: { padding: 0 } }}>
          <div className="month" role="grid" aria-label={`Kalender ${monthName(anchor)}`}>
            {['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'].map((w) => <div key={w} className="month-head" role="columnheader">{w}</div>)}
            {days.map((d) => {
              const items = assignments.filter((a) => a.day === d);
              const absent = absentOn(d).length;
              const outside = d.slice(0, 7) !== anchor.slice(0, 7);
              return (
                <div key={d} role="gridcell" className={`month-cell${outside ? ' month-outside' : ''}${d === today ? ' board-today' : ''}${dropClass(null, d)}`}
                  {...dropProps(null, d)}>
                  <div className="month-day">
                    <a onClick={() => set({ ansicht: 'tag', datum: d })} aria-label={`Tagesansicht ${shortDay(d)}`}>{parseDay(d).getDate()}</a>
                    {absent > 0 && <Tag color="orange" bordered={false} style={{ marginInlineEnd: 0 }}>{absent} abw.</Tag>}
                  </div>
                  {items.slice(0, 4).map((a) => <AssignmentCard key={a.id} a={a} compact draggable={canPlan} onOpen={open} />)}
                  {items.length > 4 && <a className="month-more" onClick={() => set({ ansicht: 'tag', datum: d })}>+{items.length - 4} weitere</a>}
                </div>
              );
            })}
          </div>
        </Card>
      )}

      <Drawer title={<Space><InboxOutlined />Baustellen-Vorrat</Space>} open={backlogOpen} onClose={() => setBacklogOpen(false)} mask={false} width={380}
        className="backlog-drawer">
        <Backlog items={backlog.data ?? []} loading={backlog.isLoading} canPlan={canPlan}
          onPlan={(b) => setEditing(newEditing(days.includes(today) ? today : days[0], null, { id: b.bauakteId, label: `${b.number ?? 'neu'} · ${b.title}` }))} />
      </Drawer>

      {editing && (
        <AssignmentModal key={editing.id} editing={editing} canPlan={canPlan} onClose={() => setEditing(null)}
          crews={activeCrews} resources={(resources.data ?? []).filter((r) => r.isActive)} people={(people.data ?? []).filter((p) => p.isActive)} />
      )}
    </PageContainer>
  );
}

/** Baustellen-Vorrat: geplante und laufende Bauakten; „frei“ = ohne künftigen Einsatz. Zum Einplanen in den Plan ziehen. */
function Backlog({ items, loading, canPlan, onPlan }: { items: BacklogItem[]; loading: boolean; canPlan: boolean; onPlan: (b: BacklogItem) => void }) {
  const [mode, setMode] = useState<'free' | 'planned' | 'all'>('free');
  const [search, setSearch] = useState('');
  const counts = { free: items.filter((b) => b.upcomingAssignments === 0).length, planned: items.filter((b) => b.upcomingAssignments > 0).length, all: items.length };
  const q = search.trim().toLowerCase();
  const shown = items.filter((b) => (mode === 'all' || (mode === 'free') === (b.upcomingAssignments === 0))
    && (!q || [b.number, b.title, b.city, b.customerName].some((v) => v?.toLowerCase().includes(q))));
  return (
    <Space direction="vertical" size={12} style={{ width: '100%' }}>
      <Segmented block value={mode} onChange={(v) => setMode(v as typeof mode)} options={[
        { value: 'free', label: <div className="seg-count"><strong>{counts.free}</strong><span>frei</span></div> },
        { value: 'planned', label: <div className="seg-count"><strong>{counts.planned}</strong><span>verplant</span></div> },
        { value: 'all', label: <div className="seg-count"><strong>{counts.all}</strong><span>alle</span></div> },
      ]} />
      <Input.Search allowClear placeholder="Name, Ort, Akte, Kunde" aria-label="Vorrat durchsuchen" onChange={(e) => setSearch(e.target.value)} />
      {canPlan && <Typography.Text type="secondary" style={{ fontSize: 12 }}>Baustelle in den Plan ziehen oder „Einplanen“ wählen.</Typography.Text>}
      <List loading={loading} dataSource={shown} locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Keine Baustellen in dieser Auswahl." /> }}
        renderItem={(b) => (
          <div className="backlog-item" draggable={canPlan}
            onDragStart={(e) => { e.dataTransfer.setData('text/bauakte', JSON.stringify({ id: b.bauakteId, label: `${b.number ?? 'neu'} · ${b.title}` })); e.dataTransfer.effectAllowed = 'copy'; }}>
            <div style={{ flex: 1, minWidth: 0 }}>
              <Link to={`/bauakten/${b.bauakteId}`}><strong>{b.title}</strong></Link>
              <div className="backlog-meta">{[b.customerName, [b.postalCode, b.city].filter(Boolean).join(' ')].filter(Boolean).join(' · ') || '–'}</div>
              <div className="backlog-meta tabular">
                {b.number ?? 'neu'}
                {b.totalAreaSquareMeters > 0 && ` · ${b.totalAreaSquareMeters.toLocaleString('de-DE', { minimumFractionDigits: 2 })} m²`}
                {b.screedTypes.length > 0 && ` · ${b.screedTypes.join(', ')}`}
              </div>
              <Space size={4} style={{ marginTop: 4 }}>
                <Tag bordered={false}>{bauakteStatusLabels[b.status]}</Tag>
                {b.nextAssignment ? <Tag color="blue" bordered={false}>ab {shortDay(b.nextAssignment)}</Tag> : <Tag color="green" bordered={false}>frei</Tag>}
              </Space>
            </div>
            {canPlan && <Button size="small" icon={<CalendarOutlined />} onClick={() => onPlan(b)} aria-label={`${b.title} einplanen`}>Einplanen</Button>}
          </div>
        )} />
    </Space>
  );
}
