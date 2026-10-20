import { BgColorsOutlined, HistoryOutlined, PushpinFilled, PushpinOutlined, SendOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Avatar, Button, Card, Checkbox, Dropdown, Empty, Input, Segmented, Space, Tag, Tooltip, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { get, patch, post, problemMessage } from '../../api/client';
import type { BauakteHistoryEntry, DirectoryUser, NoteDto } from '../../api/models';
import { uuidv7 } from '../../shared/ids';
import { formatDateTime } from '../../shared/status';

const colors = [
  { key: 'yellow', label: 'Gelb', value: '#FFF7D6' },
  { key: 'red', label: 'Rot', value: '#FFE4E1' },
  { key: 'green', label: 'Grün', value: '#E3F6E5' },
  { key: 'blue', label: 'Blau', value: '#E3EEFB' },
];

type Item =
  | { kind: 'note'; at: string; note: NoteDto }
  | { kind: 'event'; at: string; entry: BauakteHistoryEntry };

/**
 * Notizen und Verlauf wie in Estrich Web: ein Chat-Verlauf mit Notizen und Systemänderungen, angeheftete oben,
 * Notizen anheften und einfärben.
 */
export function BauakteNotes({ bauakteId, canWrite, people, currentUserId }: {
  bauakteId: string; canWrite: boolean; people: DirectoryUser[]; currentUserId: string | null;
}) {
  const { message } = App.useApp();
  const queries = useQueryClient();
  const notes = useQuery({ queryKey: ['notes', bauakteId], queryFn: () => get<NoteDto[]>(`bauakten/${bauakteId}/notes`) });
  const history = useQuery({ queryKey: ['history', bauakteId], queryFn: () => get<BauakteHistoryEntry[]>(`bauakten/${bauakteId}/history`) });
  const [text, setText] = useState('');
  const [pin, setPin] = useState(false);
  const [filter, setFilter] = useState<'all' | 'notes'>('all');
  const name = (id: string) => people.find((p) => p.id === id)?.displayName ?? '–';

  const items = useMemo<Item[]>(() => {
    const noteItems: Item[] = (notes.data ?? []).map((n) => ({ kind: 'note', at: n.createdAt, note: n }));
    const events: Item[] = filter === 'notes' ? [] : (history.data ?? [])
      .filter((h) => h.action !== 'bauakte.note_added')
      .map((h) => ({ kind: 'event', at: h.at, entry: h }));
    return [...noteItems, ...events].sort((a, b) => a.at.localeCompare(b.at));
  }, [notes.data, history.data, filter]);
  const pinned = (notes.data ?? []).filter((n) => n.isPinned);

  const refresh = () => {
    void queries.invalidateQueries({ queryKey: ['notes', bauakteId] });
    void queries.invalidateQueries({ queryKey: ['history', bauakteId] });
  };

  const send = async () => {
    try {
      const id = uuidv7();
      await post(`bauakten/${bauakteId}/notes`, { id, text: text.trim() });
      if (pin) {
        await patch(`bauakten/${bauakteId}/notes/${id}`, { isPinned: true });
      }
      setText('');
      setPin(false);
      refresh();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const mark = async (note: NoteDto, change: { isPinned?: boolean; color?: string }) => {
    try {
      await patch(`bauakten/${bauakteId}/notes/${note.id}`, change);
      refresh();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const noteActions = (n: NoteDto) => canWrite && (
    <Space size={0} className="note-actions">
      <Tooltip title={n.isPinned ? 'Lösen' : 'Anheften'}>
        <Button type="text" size="small" aria-label={n.isPinned ? 'Notiz lösen' : 'Notiz anheften'}
          icon={n.isPinned ? <PushpinFilled style={{ color: '#FA8C16' }} /> : <PushpinOutlined />} onClick={() => void mark(n, { isPinned: !n.isPinned })} />
      </Tooltip>
      <Dropdown trigger={['click']} menu={{
        items: colors.map((c) => ({ key: c.key, label: <Space><span className="swatch" style={{ background: c.value, border: '1px solid #d9d9d9' }} />{c.label}</Space> })),
        onClick: ({ key }) => void mark(n, { color: key }),
      }}>
        <Button type="text" size="small" aria-label="Notiz einfärben" icon={<BgColorsOutlined />} />
      </Dropdown>
    </Space>
  );

  return (
    <Card size="small" title="Notizen & Verlauf"
      extra={<Segmented size="small" value={filter} onChange={(v) => setFilter(v as 'all' | 'notes')}
        options={[{ value: 'all', label: 'Mit Systemänderungen' }, { value: 'notes', label: 'Nur Notizen' }]} />}>
      {pinned.length > 0 && (
        <div className="pinned-notes">
          {pinned.map((n) => (
            <div key={n.id} className="pinned-note" style={{ background: colors.find((c) => c.key === n.color)?.value ?? '#FFF7D6' }}>
              <PushpinFilled style={{ color: '#FA8C16' }} />
              <Typography.Text className="pre" style={{ flex: 1 }}>{n.text}</Typography.Text>
              {noteActions(n)}
            </div>
          ))}
        </div>
      )}
      <div className="chat notes" role="log" aria-label="Notizen und Verlauf">
        {items.length === 0 && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch keine Notizen." />}
        {items.map((item) => item.kind === 'event'
          ? (
            <div key={`e-${item.at}-${item.entry.text}`} className="chat-event">
              <HistoryOutlined /> <span>{item.entry.text}</span> <Typography.Text type="secondary" className="tabular">{formatDateTime(item.at)}</Typography.Text>
            </div>
          )
          : (
            <div key={item.note.id} className={`chat-note${item.note.authorUserId === currentUserId ? ' own' : ''}`}>
              <Avatar size="small" style={{ background: '#0B2D5B', flex: 'none' }}>{name(item.note.authorUserId).split(' ').map((p) => p[0]).slice(0, 2).join('')}</Avatar>
              <div className="chat-bubble note" style={{ background: colors.find((c) => c.key === item.note.color)?.value }}>
                <div className="chat-meta">
                  <strong>{name(item.note.authorUserId)}</strong>
                  <Typography.Text type="secondary" className="tabular">{formatDateTime(item.note.createdAt)}</Typography.Text>
                  {item.note.isPinned && <Tag color="gold" bordered={false}>Angeheftet</Tag>}
                  {item.note.editedAt && <Typography.Text type="secondary">· bearbeitet</Typography.Text>}
                  {noteActions(item.note)}
                </div>
                <Typography.Paragraph className="pre" style={{ margin: 0 }}>{item.note.text}</Typography.Paragraph>
              </div>
            </div>
          ))}
      </div>
      {canWrite && (
        <div className="composer">
          <Input.TextArea aria-label="Neue Notiz" rows={3} placeholder="Notiz schreiben …" value={text} onChange={(e) => setText(e.target.value)}
            onPressEnter={(e) => { if (e.ctrlKey && text.trim()) { e.preventDefault(); void send(); } }} />
          <Space style={{ justifyContent: 'space-between', width: '100%', marginTop: 8 }}>
            <Checkbox checked={pin} onChange={(e) => setPin(e.target.checked)}><PushpinOutlined /> Anheften</Checkbox>
            <Space>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>Strg + Enter sendet</Typography.Text>
              <Button type="primary" icon={<SendOutlined />} disabled={!text.trim()} onClick={() => void send()}>Notiz speichern</Button>
            </Space>
          </Space>
        </div>
      )}
    </Card>
  );
}
