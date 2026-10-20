import { FileOutlined, InboxOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Card, Empty, Image, Space, Table, Typography, Upload } from 'antd';
import { useState } from 'react';
import { api, get, post, problemMessage } from '../../api/client';
import type { FileResponse } from '../../api/models';
import { uuidv7 } from '../../shared/ids';
import { formatDateTime } from '../../shared/status';

export function fileSize(bytes: number): string {
  return bytes < 1024 * 1024
    ? `${Math.max(1, Math.round(bytes / 1024))} KB`
    : `${(bytes / 1024 / 1024).toLocaleString('de-DE', { maximumFractionDigits: 1 })} MB`;
}

const variantUrl = (file: FileResponse, variant: 'thumb' | 'display') =>
  `/api/v1/files/${file.id}/content?variant=${file.variants === 'ready' ? variant : 'original'}`;

/** Ankündigen (mit SHA-256), dann Inhalt senden – der Server prüft Typ und Prüfsumme. Gleicher Ablauf wie die Apps. */
async function upload(bauakteId: string, file: File): Promise<void> {
  const bytes = await file.arrayBuffer();
  const sha256 = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), (b) => b.toString(16).padStart(2, '0')).join('');
  const contentType = file.type || 'application/octet-stream';
  const kind = contentType.startsWith('image/') ? 'photo' : contentType.startsWith('video/') ? 'video' : 'document';
  const id = uuidv7();
  await post('files', {
    id, bauakteId, kind, originalName: file.name, contentType, sizeBytes: file.size, sha256,
    capturedAt: kind === 'photo' ? new Date(file.lastModified).toISOString() : null,
  });
  await api(`files/${id}/content`, { method: 'PUT', raw: bytes });
}

/** Fotos, Videos und Dokumente: ansehen, herunterladen, aus dem Browser hochladen. */
export function BauakteFiles({ bauakteId, canUpload }: { bauakteId: string; canUpload: boolean }) {
  const queries = useQueryClient();
  const files = useQuery({ queryKey: ['files', bauakteId], queryFn: () => get<FileResponse[]>(`bauakten/${bauakteId}/files`) });
  const [status, setStatus] = useState<{ type: 'info' | 'success' | 'error'; text: string } | null>(null);
  const photos = (files.data ?? []).filter((f) => f.kind === 'photo');
  const documents = (files.data ?? []).filter((f) => f.kind !== 'photo');

  const uploadAll = async (list: File[]) => {
    let done = 0;
    const errors: string[] = [];
    for (const file of list) {
      setStatus({ type: 'info', text: `Lade ${done + errors.length + 1} von ${list.length} hoch …` });
      try {
        await upload(bauakteId, file);
        done++;
      } catch (e) {
        errors.push(`${file.name}: ${problemMessage(e)}`);
      }
    }
    setStatus(errors.length > 0
      ? { type: 'error', text: errors.join(' ') }
      : { type: 'success', text: `${done} ${done === 1 ? 'Datei' : 'Dateien'} hochgeladen.` });
    void queries.invalidateQueries({ queryKey: ['files', bauakteId] });
    void queries.invalidateQueries({ queryKey: ['history', bauakteId] });
  };

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      {canUpload && (
        <Upload.Dragger
          multiple
          showUploadList={false}
          beforeUpload={(_, list) => {
            // Ein Aufruf für die ganze Auswahl: antd ruft beforeUpload je Datei auf, wir laden nur beim ersten.
            if (list[0] && _ === list[0]) {
              void uploadAll(list);
            }
            return Upload.LIST_IGNORE;
          }}
        >
          <p className="ant-upload-drag-icon"><InboxOutlined /></p>
          <p className="ant-upload-text">Dateien hochladen</p>
          <p className="ant-upload-hint">Klicken oder hierher ziehen. Fotos, PDF-Dokumente und Videos.</p>
        </Upload.Dragger>
      )}
      {status && <Alert role="status" type={status.type} showIcon message={status.text} closable onClose={() => setStatus(null)} />}
      <Card size="small" title={`Fotos (${photos.length})`} loading={files.isLoading}>
        {photos.length === 0
          ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch keine Fotos." />
          : (
            <Image.PreviewGroup>
              <div className="photo-grid">
                {photos.map((p) => p.state === 'stored'
                  ? <Image key={p.id} src={variantUrl(p, 'thumb')} preview={{ src: variantUrl(p, 'display') }}
                      alt={`Foto vom ${formatDateTime(p.capturedAt ?? p.createdAt)}`} />
                  : <div key={p.id} className="photo-pending">Upload ausstehend</div>)}
              </div>
            </Image.PreviewGroup>
          )}
      </Card>
      <Card size="small" title={`Dokumente und Videos (${documents.length})`}>
        <Table<FileResponse> size="small" rowKey="id" pagination={false} dataSource={documents} loading={files.isLoading}
          locale={{ emptyText: 'Noch keine Dokumente.' }}
          columns={[
            {
              title: 'Datei', dataIndex: 'originalName', render: (v: string, f) => f.state === 'stored'
                ? <a href={`/api/v1/files/${f.id}/content`} target="_blank" rel="noopener noreferrer"><FileOutlined /> {v}</a>
                : <Typography.Text type="secondary"><FileOutlined /> {v} (Upload ausstehend)</Typography.Text>,
            },
            { title: 'Größe', dataIndex: 'sizeBytes', width: 110, align: 'right', render: (v: number) => <span className="tabular">{fileSize(v)}</span> },
            { title: 'Hochgeladen', dataIndex: 'createdAt', width: 170, render: (v: string) => <span className="tabular">{formatDateTime(v)}</span> },
          ]} />
      </Card>
    </Space>
  );
}
