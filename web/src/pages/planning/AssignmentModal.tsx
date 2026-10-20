import { DeleteOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, DatePicker, Form, Input, Modal, Select, Space, TimePicker } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { del, get, problemMessage, put } from '../../api/client';
import type { AssignmentKind, AssignmentView, BauaktePage, CrewResponse, DirectoryUser, ResourceResponse } from '../../api/models';
import { uuidv7 } from '../../shared/ids';
import { kindActivities, kindLabels } from './shared';

export interface AssignmentForm {
  bauakteId: string | null;
  kind: AssignmentKind;
  day: Dayjs;
  startTime: Dayjs | null;
  endTime: Dayjs | null;
  activity: string;
  crewId: string | null;
  userIds: string[];
  resourceIds: string[];
  notes?: string;
}

export interface Editing {
  id: string;
  existing: boolean;
  initial: AssignmentForm;
  bauakteLabel: string | null;
  warnings: string[];
}

/** Neuer Einsatz, z. B. aus dem Vorrat oder per „+“ in einer Zelle. */
export function newEditing(day: string, crewId: string | null, bauakte?: { id: string; label: string }): Editing {
  return {
    id: uuidv7(), existing: false, bauakteLabel: bauakte?.label ?? null, warnings: [],
    initial: {
      bauakteId: bauakte?.id ?? null, kind: 'screed', day: dayjs(day), startTime: dayjs(`${day}T07:00`), endTime: null,
      activity: bauakte ? kindActivities.screed : '', crewId, userIds: [], resourceIds: [],
    },
  };
}

/** Vorhandenen Einsatz bearbeiten; zusätzliche Mitarbeiter = alle außer der Kolonne. */
export function editingOf(a: AssignmentView, crews: CrewResponse[]): Editing {
  const crewMembers = new Set(crews.find((c) => c.id === a.crewId)?.memberUserIds ?? []);
  return {
    id: a.id, existing: true, bauakteLabel: `${a.bauakteNumber ?? 'neu'} · ${a.bauakteTitle}`, warnings: a.warnings,
    initial: {
      bauakteId: a.bauakteId, kind: a.kind, day: dayjs(a.day), startTime: a.startTime ? dayjs(`${a.day}T${a.startTime}`) : null,
      endTime: a.endTime ? dayjs(`${a.day}T${a.endTime}`) : null, activity: a.activity, crewId: a.crewId,
      userIds: a.people.map((p) => p.id).filter((id) => !crewMembers.has(id)), resourceIds: a.resources.map((r) => r.id), notes: a.notes ?? '',
    },
  };
}

/** Bauakte suchen (Nummer, Bauvorhaben, Ort). */
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
export function AssignmentModal({ editing: initial, canPlan, onClose, crews, resources, people }: {
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
        bauakteId: v.bauakteId, kind: v.kind, day: v.day.format('YYYY-MM-DD'), startTime: v.startTime ? v.startTime.format('HH:mm:00') : null,
        endTime: v.endTime ? v.endTime.format('HH:mm:00') : null, activity: v.activity.trim(), crewId: v.crewId ?? null,
        userIds: v.userIds ?? [], resourceIds: v.resourceIds ?? [], notes: v.notes || null,
      });
      void queries.invalidateQueries({ queryKey: ['planning'] });
      void queries.invalidateQueries({ queryKey: ['backlog'] });
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
        void queries.invalidateQueries({ queryKey: ['backlog'] });
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
      width={640}
      onCancel={onClose}
      footer={[
        editing.existing && canPlan && <Button key="del" danger icon={<DeleteOutlined />} onClick={remove} style={{ float: 'left' }}>Löschen</Button>,
        <Button key="close" onClick={onClose}>Schließen</Button>,
        canPlan && <Button key="save" type="primary" loading={saving} onClick={() => void save()}>Speichern</Button>,
      ]}
    >
      <Form<AssignmentForm> form={form} layout="vertical" initialValues={editing.initial} disabled={!canPlan} requiredMark={false}
        onValuesChange={(changed: Partial<AssignmentForm>) => {
          // Tätigkeit aus der Terminart vorschlagen, solange sie leer oder noch der alte Vorschlag ist.
          if (changed.kind) {
            const current = form.getFieldValue('activity') as string;
            if (!current || Object.values(kindActivities).includes(current)) {
              form.setFieldValue('activity', kindActivities[changed.kind]);
            }
          }
        }}>
        <Form.Item name="bauakteId" label="Bauakte" rules={[{ required: true, message: 'Bitte eine Bauakte wählen.' }]}
          extra={editing.initial.bauakteId && <Link to={`/bauakten/${editing.initial.bauakteId}`} target="_blank">Bauakte öffnen</Link>}>
          <BauakteSelect initialLabel={editing.bauakteLabel} />
        </Form.Item>
        <Space.Compact block>
          <Form.Item name="kind" label="Terminart" style={{ flex: 1 }}>
            <Select options={(Object.keys(kindLabels) as AssignmentKind[]).map((k) => ({ value: k, label: kindLabels[k] }))} />
          </Form.Item>
          <Form.Item name="day" label="Tag" rules={[{ required: true }]} style={{ width: 200 }}>
            <DatePicker format="dd, DD.MM.YYYY" style={{ width: '100%' }} allowClear={false} />
          </Form.Item>
        </Space.Compact>
        <Space.Compact block>
          <Form.Item name="startTime" label="Beginn" style={{ width: 140 }}><TimePicker format="HH:mm" minuteStep={15} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="endTime" label="Ende (optional)" style={{ width: 140 }} dependencies={['startTime']}
            rules={[({ getFieldValue }) => ({
              validator: (_, v: Dayjs | null) => !v || !getFieldValue('startTime') || v.isAfter(getFieldValue('startTime'))
                ? Promise.resolve() : Promise.reject(new Error('Ende nach Beginn')),
            })]}>
            <TimePicker format="HH:mm" minuteStep={15} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="activity" label="Tätigkeit" style={{ flex: 1 }} rules={[{ required: true, whitespace: true, message: 'Bitte die Tätigkeit angeben.' }]}>
            <Input placeholder="z. B. Estrich einbringen EG" />
          </Form.Item>
        </Space.Compact>
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
