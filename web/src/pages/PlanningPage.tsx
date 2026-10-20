import { CalendarOutlined, DeleteOutlined, LeftOutlined, PlusOutlined, RightOutlined, SettingOutlined, WarningOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, DatePicker, Form, Input, Modal, Select, Space, Tag, TimePicker, Tooltip, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { del, get, problemMessage, put } from '../api/client';
import type { AssignmentView, BauaktePage, CrewResponse, DirectoryUser, PlanningRange, ResourceResponse } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { addDays, isoDay, isoWeek, mondayOf, parseDay } from '../shared/days';
import { uuidv7 } from '../shared/ids';

const absenceNames: Record<string, string> = { vacation: 'Urlaub', sick: 'krank', training: 'Schulung', other: 'abwesend' };
const label = (d: string) => parseDay(d).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' });
const weekday = (d: string) => parseDay(d).toLocaleDateString('de-DE', { weekday: 'long' });

interface AssignmentForm {
  bauakteId: string | null;
  day: Dayjs;
  startTime: Dayjs | null;
  activity: string;
  crewId: string | null;
  userIds: string[];
  resourceIds: string[];
  notes?: string;
}

interface Editing {
  id: string;
  existing: boolean;
  initial: AssignmentForm;
  bauakteLabel: string | null;
  warnings: string[];
}

/** Bauakte suchen (Nummer oder Bauvorhaben). */
function BauakteSelect({ value, onChange, initialLabel }: { value?: string | null; onChange?: (v: string | null) => void; initialLabel: string | null }) {
  const [options, setOptions] = useState<{ value: string; label: string }[]>(value && initialLabel ? [{ value, label: initialLabel }] : []);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const search = (text: string) => {
    clearTimeout(timer.current);
    timer.current = setTimeout(async () => {
      if (text.trim().length < 2) {
        return;
      }
      const page = await get<BauaktePage>('bauakten', { search: text.trim(), limit: 8 });
      setOptions(page.items.map((b) => ({ value: b.id, label: `${b.number ?? 'neu'} · ${b.title}${b.city ? ` (${b.city})` : ''}` })));
    }, 200);
  };
  return (
    <Select aria-label="Bauakte" showSearch allowClear filterOption={false} placeholder="Nummer oder Bauvorhaben eingeben" value={value}
      onChange={(v) => onChange?.(v ?? null)} onSearch={search} options={options} notFoundContent={null} />
  );
}

/** Einsatz planen oder bearbeiten; eigenes Formular je Einsatz. Hinweise bleiben nach dem Speichern sichtbar. */
function AssignmentModal({ editing: initial, canPlan, onClose, crews, resources, people }: {
  editing: Editing; canPlan: boolean; onClose: () => void; crews: CrewResponse[]; resources: ResourceResponse[]; people: DirectoryUser[];
}) {
  const { message, modal } = App.useApp();
  const queries = useQueryClient();
  const [form] = Form.useForm<AssignmentForm>();
  const [editing, setEditing] = useState(initial);
  const [saving, setSaving] = useState(false);

  const save = async () => {
    const v = await form.validateFields();
    setSaving(true);
    try {
      const saved = await put<AssignmentView>(`planning/assignments/${editing.id}`, {
        bauakteId: v.bauakteId, day: v.day.format('YYYY-MM-DD'), startTime: v.startTime ? v.startTime.format('HH:mm:00') : null, activity: v.activity.trim(),
        crewId: v.crewId ?? null, userIds: v.userIds ?? [], resourceIds: v.resourceIds ?? [], notes: v.notes || null,
      });
      void queries.invalidateQueries({ queryKey: ['planning'] });
      if (saved.warnings.length > 0) {
        // Hinweise sichtbar lassen; die Planung entscheidet, ob sie so bleibt.
        setEditing({ ...editing, existing: true, warnings: saved.warnings });
        message.warning('Gespeichert – bitte die Hinweise prüfen.');
      } else {
        message.success('Einsatz gespeichert');
        onClose();
      }
    } catch (e) {
      message.error(problemMessage(e));
    } finally {
      setSaving(false);
    }
  };

  const remove = () => modal.confirm({
    title: 'Einsatz löschen?', okText: 'Löschen', okButtonProps: { danger: true }, cancelText: 'Abbrechen',
    onOk: async () => {
      try {
        await del(`planning/assignments/${editing.id}`);
        void queries.invalidateQueries({ queryKey: ['planning'] });
        onClose();
      } catch (e) {
        message.error(problemMessage(e));
      }
    },
  });

  return (
    <Modal
      title={editing.existing ? 'Einsatz bearbeiten' : 'Einsatz planen'}
      open
      width={620}
      onCancel={onClose}
      footer={[
        editing.existing && canPlan && <Button key="del" danger icon={<DeleteOutlined />} onClick={remove} style={{ float: 'left' }}>Löschen</Button>,
        <Button key="close" onClick={onClose}>Schließen</Button>,
        canPlan && <Button key="save" type="primary" loading={saving} onClick={() => void save()}>Speichern</Button>,
      ]}
    >
      <Form<AssignmentForm> form={form} layout="vertical" initialValues={editing.initial} disabled={!canPlan} requiredMark={false}>
        <Form.Item name="bauakteId" label="Bauakte" rules={[{ required: true, message: 'Bitte eine Bauakte wählen.' }]}
          extra={editing.initial.bauakteId && <Link to={`/bauakten/${editing.initial.bauakteId}`} target="_blank">Bauakte öffnen</Link>}>
          <BauakteSelect initialLabel={editing.bauakteLabel} />
        </Form.Item>
        <Space.Compact block>
          <Form.Item name="day" label="Tag" rules={[{ required: true }]} style={{ flex: 1 }}><DatePicker format="dd, DD.MM.YYYY" style={{ width: '100%' }} allowClear={false} /></Form.Item>
          <Form.Item name="startTime" label="Beginn" style={{ width: 140 }}><TimePicker format="HH:mm" minuteStep={15} style={{ width: '100%' }} /></Form.Item>
        </Space.Compact>
        <Form.Item name="activity" label="Tätigkeit" rules={[{ required: true, whitespace: true, message: 'Bitte die Tätigkeit angeben.' }]}>
          <Input placeholder="z. B. Estrich einbringen EG" />
        </Form.Item>
        <Form.Item name="crewId" label="Kolonne"><Select allowClear placeholder="keine" options={crews.map((c) => ({ value: c.id, label: c.name }))} /></Form.Item>
        <Form.Item name="userIds" label="Weitere Mitarbeiter" dependencies={['crewId']}
          rules={[({ getFieldValue }) => ({ validator: (_, v: string[] | undefined) => getFieldValue('crewId') || (v && v.length) ? Promise.resolve() : Promise.reject(new Error('Bitte eine Kolonne oder Mitarbeiter einplanen.')) })]}>
          <Select mode="multiple" showSearch optionFilterProp="label" options={people.map((p) => ({ value: p.id, label: p.displayName }))} />
        </Form.Item>
        <Form.Item name="resourceIds" label="Fahrzeuge und Geräte">
          <Select mode="multiple" showSearch optionFilterProp="label" notFoundContent="Noch keine Geräte angelegt."
            options={resources.map((r) => ({ value: r.id, label: r.name }))} />
        </Form.Item>
        <Form.Item name="notes" label="Hinweise"><Input.TextArea rows={2} /></Form.Item>
        {editing.warnings.length > 0 && (
          <Alert role="status" type="warning" showIcon message="Hinweise" description={<ul style={{ margin: 0, paddingInlineStart: 18 }}>{editing.warnings.map((w) => <li key={w}>{w}</li>)}</ul>} />
        )}
      </Form>
    </Modal>
  );
}

/** Wochenplan: Zeilen = Kolonnen, Spalten = Tage; Konflikte werden angezeigt, nicht verhindert. */
export default function PlanningPage() {
  const { has } = useAuth();
  const today = isoDay(new Date());
  const [week, setWeek] = useState(mondayOf(today));
  const days = Array.from({ length: 6 }, (_, i) => addDays(week, i));
  const canPlan = has('planning.manage');
  const [editing, setEditing] = useState<Editing | null>(null);

  const range = useQuery({ queryKey: ['planning', week], queryFn: () => get<PlanningRange>('planning/range', { from: week, to: addDays(week, 6) }) });
  const crews = useQuery({ queryKey: ['crews'], queryFn: () => get<CrewResponse[]>('planning/crews') });
  const resources = useQuery({ queryKey: ['resources'], queryFn: () => get<ResourceResponse[]>('planning/resources') });
  const people = useQuery({ queryKey: ['directory'], queryFn: () => get<DirectoryUser[]>('directory/users'), staleTime: 60_000 });

  const activeCrews = (crews.data ?? []).filter((c) => c.isActive);
  const lanes = [...activeCrews.map((c) => ({ id: c.id as string | null, name: c.name, color: c.color })), { id: null, name: 'Einzeln eingeplant', color: null }];
  const assignments = range.data?.assignments ?? [];
  const absentOn = (d: string) => (range.data?.absences ?? []).filter((a) => a.from <= d && d <= a.to);
  const warningCount = assignments.filter((a) => a.warnings.length > 0).length;

  const open = (a: AssignmentView | null, crewId: string | null, d: string) => {
    if (a) {
      const crewMembers = new Set(crews.data?.find((c) => c.id === a.crewId)?.memberUserIds ?? []);
      setEditing({
        id: a.id, existing: true, bauakteLabel: `${a.bauakteNumber ?? 'neu'} · ${a.bauakteTitle}`, warnings: a.warnings,
        initial: {
          bauakteId: a.bauakteId, day: dayjs(a.day), startTime: a.startTime ? dayjs(`${a.day}T${a.startTime}`) : null, activity: a.activity, crewId: a.crewId,
          userIds: a.people.map((p) => p.id).filter((id) => !crewMembers.has(id)), resourceIds: a.resources.map((r) => r.id), notes: a.notes ?? '',
        },
      });
    } else {
      setEditing({
        id: uuidv7(), existing: false, bauakteLabel: null, warnings: [],
        initial: { bauakteId: null, day: dayjs(d), startTime: dayjs(`${d}T07:00`), activity: '', crewId, userIds: [], resourceIds: [] },
      });
    }
  };

  return (
    <PageContainer
      title="Plantafel"
      subTitle={`KW ${isoWeek(week)} · ${label(week)} – ${label(days[5])}`}
      extra={[
        <Space.Compact key="nav">
          <Button icon={<LeftOutlined />} aria-label="Vorherige Woche" onClick={() => setWeek(addDays(week, -7))} />
          <Button icon={<CalendarOutlined />} onClick={() => setWeek(mondayOf(today))}>Heute</Button>
          <Button icon={<RightOutlined />} aria-label="Nächste Woche" onClick={() => setWeek(addDays(week, 7))} />
        </Space.Compact>,
        <DatePicker key="pick" picker="week" allowClear={false} value={dayjs(week)} onChange={(d) => d && setWeek(mondayOf(d.format('YYYY-MM-DD')))} format="[KW] w, YYYY" aria-label="Woche wählen" />,
        <Link key="res" to="/planung/ressourcen"><Button icon={<SettingOutlined />}>Kolonnen und Geräte</Button></Link>,
      ]}
    >
      {range.error && <Alert type="error" showIcon message={problemMessage(range.error)} style={{ marginBottom: 16 }} />}
      {warningCount > 0 && (
        <Alert type="warning" showIcon style={{ marginBottom: 16 }}
          message={`${warningCount} ${warningCount === 1 ? 'Einsatz hat' : 'Einsätze haben'} Hinweise (z. B. Urlaub oder Doppelbelegung).`} />
      )}
      <Card size="small" styles={{ body: { padding: 0 } }}>
        <div className="board" role="table" aria-label="Wochenplan">
          <div className="board-row board-head" role="row">
            <div className="board-cell board-lane" role="columnheader">Kolonne</div>
            {days.map((d) => (
              <div key={d} className={`board-cell${d === today ? ' board-today' : ''}`} role="columnheader">
                {weekday(d)}<br /><Typography.Text type="secondary" className="tabular">{label(d)}</Typography.Text>
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
          {lanes.map((lane) => (
            <div key={lane.id ?? 'none'} className="board-row" role="row">
              <div className="board-cell board-lane" role="rowheader">
                <span className="swatch" style={{ background: lane.color ?? '#d9d9d9' }} aria-hidden="true" />{lane.name}
              </div>
              {days.map((d) => (
                <div key={d} className={`board-cell${d === today ? ' board-today' : ''}`} role="cell">
                  {assignments.filter((a) => a.day === d && (a.crewId ?? null) === lane.id).map((a) => (
                    <button key={a.id} type="button" className={`board-item${a.warnings.length ? ' board-warn' : ''}`}
                      style={{ borderLeftColor: a.warnings.length ? undefined : (lane.color ?? '#0B2D5B') }}
                      onClick={() => open(a, null, d)}
                      aria-label={`${a.activity}, ${a.bauakteTitle}${a.warnings.length ? `, ${a.warnings.length} Hinweise` : ''}`}>
                      <span className="board-time tabular">{a.startTime?.slice(0, 5) ?? ''}</span>
                      <strong>{a.bauakteNumber ?? ''} {a.bauakteTitle}</strong>
                      <span>{a.activity}</span>
                      {a.resources.length > 0 && <small>{a.resources.map((r) => r.name).join(', ')}</small>}
                      {!lane.id && <small>{a.people.map((p) => p.name).join(', ')}</small>}
                      {a.warnings.length > 0 && (
                        <Tooltip title={a.warnings.join(' ')}>
                          <small className="board-warning"><WarningOutlined /> {a.warnings[0]}{a.warnings.length > 1 && ` (+${a.warnings.length - 1})`}</small>
                        </Tooltip>
                      )}
                    </button>
                  ))}
                  {canPlan && (
                    <Button type="dashed" size="small" icon={<PlusOutlined />} className="board-add"
                      aria-label={`Einsatz planen: ${lane.name}, ${weekday(d)} ${label(d)}`} onClick={() => open(null, lane.id, d)} />
                  )}
                </div>
              ))}
            </div>
          ))}
        </div>
      </Card>

      {editing && (
        <AssignmentModal key={editing.id} editing={editing} canPlan={canPlan} onClose={() => setEditing(null)}
          crews={activeCrews} resources={(resources.data ?? []).filter((r) => r.isActive)} people={(people.data ?? []).filter((p) => p.isActive)} />
      )}
    </PageContainer>
  );
}
