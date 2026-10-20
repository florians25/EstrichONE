import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Empty, Input, List, Space, Tag, Typography } from 'antd';
import { useState } from 'react';
import { get, post, problemMessage } from '../../api/client';
import type { DirectoryUser, NoteDto } from '../../api/models';
import { uuidv7 } from '../../shared/ids';
import { formatDateTime } from '../../shared/status';

/** Notizen im Verlaufsstil: neueste zuerst, angeheftete oben. */
export function BauakteNotes({ bauakteId, canWrite, people }: { bauakteId: string; canWrite: boolean; people: DirectoryUser[] }) {
  const { message } = App.useApp();
  const queries = useQueryClient();
  const notes = useQuery({ queryKey: ['notes', bauakteId], queryFn: () => get<NoteDto[]>(`bauakten/${bauakteId}/notes`) });
  const [text, setText] = useState('');

  const create = async () => {
    try {
      await post(`bauakten/${bauakteId}/notes`, { id: uuidv7(), text: text.trim() });
      setText('');
      void queries.invalidateQueries({ queryKey: ['notes', bauakteId] });
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <Card size="small">
      {canWrite && (
        <Space direction="vertical" style={{ width: '100%', marginBottom: 12 }}>
          <Input.TextArea aria-label="Neue Notiz" rows={3} placeholder="Notiz schreiben …" value={text} onChange={(e) => setText(e.target.value)} />
          <Button type="primary" disabled={!text.trim()} onClick={create}>Notiz speichern</Button>
        </Space>
      )}
      <List
        className="notes"
        dataSource={notes.data ?? []}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch keine Notizen." /> }}
        renderItem={(n) => (
          <List.Item>
            <List.Item.Meta
              title={<Space size={8}>{n.isPinned && <Tag color="gold">Angeheftet</Tag>}<Typography.Text type="secondary">{people.find((p) => p.id === n.authorUserId)?.displayName ?? '–'} · {formatDateTime(n.createdAt)}</Typography.Text></Space>}
              description={<Typography.Paragraph className="pre" style={{ margin: 0, color: 'inherit' }}>{n.text}</Typography.Paragraph>}
            />
          </List.Item>
        )}
      />
    </Card>
  );
}
