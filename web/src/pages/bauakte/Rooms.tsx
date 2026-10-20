import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { ModalForm, ProFormDigit, ProFormSelect, ProFormSwitch, ProFormText, ProFormTextArea, StatisticCard } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Input, Popconfirm, Space, Table, Tag } from 'antd';
import { useState } from 'react';
import { del, get, problemMessage, put } from '../../api/client';
import type { RoomOverview, RoomResponse, RoomStatus, ScreedTypeResponse } from '../../api/models';
import { uuidv7 } from '../../shared/ids';

const statusText: Record<RoomStatus, [string, string]> = { planned: ['blue', 'Geplant'], laid: ['orange', 'Eingebracht'], ready: ['green', 'Belegreif'] };
const area = (v: number) => `${v.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} m²`;

interface RoomForm {
  name: string;
  floor?: string;
  sectionId?: string | null;
  areaSquareMeters?: number | null;
  status: RoomStatus;
  screedTypeId?: string | null;
  thicknessMillimeters?: number | null;
  insulationMillimeters?: number | null;
  underfloorHeating?: boolean;
  notes?: string;
}

/** Räume und Flächen mit Estrichaufbau, gruppiert nach Bereich, mit Summen. */
export function BauakteRooms({ bauakteId, canEdit }: { bauakteId: string; canEdit: boolean }) {
  const { message } = App.useApp();
  const queries = useQueryClient();
  const key = ['rooms', bauakteId];
  const overview = useQuery({ queryKey: key, queryFn: () => get<RoomOverview>(`bauakten/${bauakteId}/rooms`) });
  const types = useQuery({ queryKey: ['screed-types'], queryFn: () => get<ScreedTypeResponse[]>('screed-types'), staleTime: Infinity });
  const [editing, setEditing] = useState<RoomResponse | 'new' | null>(null);
  const [section, setSection] = useState<{ id: string; name: string } | null>(null);

  const run = async (request: Promise<RoomOverview>) => {
    try {
      queries.setQueryData(key, await request);
      void queries.invalidateQueries({ queryKey: ['history', bauakteId] });
      return true;
    } catch (e) {
      message.error(problemMessage(e));
      return false;
    }
  };

  const o = overview.data;
  const groups = o
    ? [
        ...o.sections.map((s) => ({ id: s.id as string | null, name: s.name, rooms: o.rooms.filter((r) => r.sectionId === s.id) })),
        ...(o.rooms.some((r) => !r.sectionId) || o.sections.length === 0
          ? [{ id: null, name: o.sections.length === 0 ? 'Räume' : 'Ohne Bereich', rooms: o.rooms.filter((r) => !r.sectionId) }]
          : []),
      ]
    : [];
  const room = editing && editing !== 'new' ? editing : null;

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <StatisticCard.Group direction="row">
        <StatisticCard className="summary" statistic={{ title: 'Fläche gesamt', value: area(o?.totalAreaSquareMeters ?? 0) }} />
        <StatisticCard statistic={{ title: 'davon beheizt', value: area(o?.heatedAreaSquareMeters ?? 0) }} />
        <StatisticCard statistic={{ title: 'Räume', value: o?.rooms.length ?? 0 }} />
        {canEdit && (
          <StatisticCard>
            <Space>
              <Button onClick={() => setSection({ id: uuidv7(), name: '' })}>Bereich hinzufügen</Button>
              <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>Raum hinzufügen</Button>
            </Space>
          </StatisticCard>
        )}
      </StatisticCard.Group>
      {section && (
        <Card size="small">
          <Space.Compact style={{ width: 520 }}>
            <Input aria-label="Name des Bereichs" placeholder="z. B. Haus A, OG, Wohnung 3" value={section.name} autoFocus
              onChange={(e) => setSection({ ...section, name: e.target.value })} />
            <Button type="primary" disabled={!section.name.trim()} onClick={async () => {
              if (await run(put<RoomOverview>(`bauakten/${bauakteId}/sections/${section.id}`, { id: section.id, name: section.name.trim() }))) setSection(null);
            }}>Speichern</Button>
            <Button onClick={() => setSection(null)}>Abbrechen</Button>
          </Space.Compact>
        </Card>
      )}
      {groups.map((group) => (
        <Card key={group.id ?? 'none'} size="small" title={group.name}
          extra={group.id && canEdit && (
            <Space>
              <Button size="small" onClick={() => setSection({ id: group.id!, name: group.name })}>Umbenennen</Button>
              <Popconfirm title="Bereich entfernen? Die Räume bleiben erhalten." okText="Entfernen" cancelText="Abbrechen"
                onConfirm={() => run(del<RoomOverview>(`bauakten/${bauakteId}/sections/${group.id}`))}>
                <Button size="small" danger>Bereich entfernen</Button>
              </Popconfirm>
            </Space>
          )}>
          <Table<RoomResponse> size="small" rowKey="id" pagination={false} dataSource={group.rooms} locale={{ emptyText: 'Noch keine Räume.' }}
            columns={[
              { title: 'Raum', dataIndex: 'name' },
              { title: 'Geschoss', dataIndex: 'floor', render: (v) => v ?? '–' },
              { title: 'Fläche', dataIndex: 'areaSquareMeters', align: 'right', render: (v: number | null) => <span className="tabular">{v === null ? '–' : area(v)}</span> },
              { title: 'Estrich', dataIndex: 'screedTypeName', render: (v) => v ?? '–' },
              { title: 'Dicke', dataIndex: 'thicknessMillimeters', align: 'right', render: (v: number | null) => (v === null ? '–' : `${v} mm`) },
              { title: 'Heizung', dataIndex: 'underfloorHeating', render: (v: boolean) => (v ? 'Ja' : 'Nein') },
              { title: 'Status', dataIndex: 'status', render: (v: RoomStatus) => <Tag color={statusText[v][0]} bordered={false}>{statusText[v][1]}</Tag> },
              ...(canEdit ? [{
                title: '', key: 'actions', align: 'right' as const, width: 110, render: (_: unknown, r: RoomResponse) => (
                  <Space>
                    <Button size="small" icon={<EditOutlined />} aria-label={`Raum ${r.name} bearbeiten`} onClick={() => setEditing(r)} />
                    <Popconfirm title={`Raum „${r.name}“ entfernen?`} okText="Entfernen" cancelText="Abbrechen" onConfirm={() => run(del<RoomOverview>(`bauakten/${bauakteId}/rooms/${r.id}`))}>
                      <Button size="small" danger icon={<DeleteOutlined />} aria-label={`Raum ${r.name} entfernen`} />
                    </Popconfirm>
                  </Space>
                ),
              }] : []),
            ]} />
        </Card>
      ))}
      <ModalForm<RoomForm>
        title={room ? 'Raum bearbeiten' : 'Raum hinzufügen'}
        open={editing !== null}
        width={560}
        modalProps={{ destroyOnHidden: true, onCancel: () => setEditing(null) }}
        onOpenChange={(open) => { if (!open) setEditing(null); }}
        submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' } }}
        initialValues={room ? {
          name: room.name, floor: room.floor ?? '', sectionId: room.sectionId, areaSquareMeters: room.areaSquareMeters, status: room.status,
          screedTypeId: room.screedTypeId, thicknessMillimeters: room.thicknessMillimeters, insulationMillimeters: room.insulationMillimeters,
          underfloorHeating: room.underfloorHeating, notes: room.notes ?? '',
        } : { status: 'planned', underfloorHeating: false }}
        onFinish={async (v) => {
          const id = room?.id ?? uuidv7();
          const ok = await run(put<RoomOverview>(`bauakten/${bauakteId}/rooms/${id}`, {
            id, sectionId: v.sectionId ?? null, name: v.name.trim(), floor: v.floor || null, areaSquareMeters: v.areaSquareMeters ?? null,
            screedTypeId: v.screedTypeId ?? null, thicknessMillimeters: v.thicknessMillimeters ?? null, insulationMillimeters: v.insulationMillimeters ?? null,
            underfloorHeating: !!v.underfloorHeating, status: v.status, notes: v.notes || null,
          }));
          if (ok) setEditing(null);
          return ok;
        }}
      >
        <Space.Compact block>
          <ProFormText name="name" label="Raum" width="md" rules={[{ required: true }]} />
          <ProFormText name="floor" label="Geschoss" width={140} placeholder="EG" />
        </Space.Compact>
        {(o?.sections.length ?? 0) > 0 && (
          <ProFormSelect name="sectionId" label="Bereich" allowClear options={o!.sections.map((s) => ({ value: s.id, label: s.name }))} />
        )}
        <Space.Compact block>
          <ProFormDigit name="areaSquareMeters" label="Fläche (m²)" width="sm" min={0} fieldProps={{ precision: 2, decimalSeparator: ',' }} />
          <ProFormSelect name="status" label="Status" width="sm" options={Object.entries(statusText).map(([value, [, label]]) => ({ value, label }))} />
        </Space.Compact>
        <ProFormSelect name="screedTypeId" label="Estrichart" allowClear placeholder="noch offen"
          options={(types.data ?? []).filter((t) => t.isActive).map((t) => ({ value: t.id, label: t.designation ? `${t.name} (${t.designation})` : t.name }))} />
        <Space.Compact block>
          <ProFormDigit name="thicknessMillimeters" label="Dicke (mm)" width="sm" min={10} max={300} />
          <ProFormDigit name="insulationMillimeters" label="Dämmung (mm)" width="sm" min={0} max={500} />
        </Space.Compact>
        <ProFormSwitch name="underfloorHeating" label="Fußbodenheizung" />
        <ProFormTextArea name="notes" label="Hinweise" fieldProps={{ rows: 2 }} />
      </ModalForm>
    </Space>
  );
}
