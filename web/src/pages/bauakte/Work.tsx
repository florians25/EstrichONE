import { PlusOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Checkbox, DatePicker, Input, Select, Space, Table, Tag, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import { useState } from 'react';
import { get, patch, post, problemMessage } from '../../api/client';
import type { DirectoryUser, WorkItemDto, WorkItemKind } from '../../api/models';
import { isoDay, parseDay } from '../../shared/days';
import { uuidv7 } from '../../shared/ids';

/** Aufgaben und Mängel einer Bauakte: anlegen, zuweisen, abhaken. */
export function BauakteWork({ bauakteId, canEdit, people }: { bauakteId: string; canEdit: boolean; people: DirectoryUser[] }) {
  const { message } = App.useApp();
  const queries = useQueryClient();
  const key = ['work', bauakteId];
  const items = useQuery({ queryKey: key, queryFn: () => get<WorkItemDto[]>(`bauakten/${bauakteId}/work-items`) });
  const [kind, setKind] = useState<WorkItemKind>('task');
  const [title, setTitle] = useState('');
  const [due, setDue] = useState<Dayjs | null>(null);
  const [assignee, setAssignee] = useState<string | null>(null);
  const today = isoDay(new Date());

  const refresh = () => {
    void queries.invalidateQueries({ queryKey: key });
    void queries.invalidateQueries({ queryKey: ['history', bauakteId] });
  };

  const create = async () => {
    try {
      await post('work-items', { id: uuidv7(), bauakteId, kind, title: title.trim(), dueDate: due ? due.format('YYYY-MM-DD') : null, assigneeUserId: assignee });
      setTitle('');
      setDue(null);
      refresh();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const toggle = async (item: WorkItemDto) => {
    try {
      await patch(`work-items/${item.id}`, { status: item.status === 'done' ? 'open' : 'done' });
      refresh();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <Card size="small">
      {canEdit && (
        <Space.Compact block style={{ marginBottom: 12 }}>
          <Select aria-label="Art" value={kind} onChange={setKind} style={{ width: 130 }}
            options={[{ value: 'task', label: 'Aufgabe' }, { value: 'defect', label: 'Mangel' }]} />
          <Input aria-label="Titel" placeholder={kind === 'defect' ? 'Was ist mangelhaft?' : 'Was ist zu tun?'} value={title}
            onChange={(e) => setTitle(e.target.value)} onPressEnter={() => title.trim() && void create()} />
          <DatePicker aria-label="Fällig am" value={due} onChange={setDue} format="DD.MM.YYYY" placeholder="Fällig am" />
          <Select aria-label="Zuständig" allowClear placeholder="Zuständig" value={assignee} onChange={(v) => setAssignee(v ?? null)} style={{ width: 200 }}
            options={people.filter((p) => p.isActive).map((p) => ({ value: p.id, label: p.displayName }))} />
          <Button type="primary" icon={<PlusOutlined />} disabled={!title.trim()} onClick={create}>Hinzufügen</Button>
        </Space.Compact>
      )}
      <Table<WorkItemDto> size="small" rowKey="id" pagination={false} dataSource={items.data ?? []} loading={items.isLoading}
        locale={{ emptyText: 'Keine Aufgaben oder Mängel.' }}
        columns={[
          {
            title: '', key: 'done', width: 44, render: (_, item) => (
              <Checkbox checked={item.status === 'done'} disabled={!canEdit} onChange={() => void toggle(item)}
                aria-label={`${item.status === 'done' ? 'Wieder öffnen' : 'Erledigt'}: ${item.title}`} />
            ),
          },
          {
            title: 'Aufgabe', dataIndex: 'title', render: (_, item) => (
              <Typography.Text delete={item.status === 'done'} type={item.status === 'done' ? 'secondary' : undefined}>{item.title}</Typography.Text>
            ),
          },
          { title: 'Art', dataIndex: 'kind', width: 110, render: (v: WorkItemKind) => (v === 'defect' ? <Tag color="orange" bordered={false}>Mangel</Tag> : 'Aufgabe') },
          {
            title: 'Fällig', dataIndex: 'dueDate', width: 120, render: (v: string | null, item) => v
              ? <Typography.Text type={item.status !== 'done' && v < today ? 'danger' : undefined} className="tabular">{parseDay(v).toLocaleDateString('de-DE')}</Typography.Text>
              : '–',
          },
          { title: 'Zuständig', dataIndex: 'assigneeUserId', width: 180, render: (v: string | null) => people.find((p) => p.id === v)?.displayName ?? '–' },
        ]} />
    </Card>
  );
}
