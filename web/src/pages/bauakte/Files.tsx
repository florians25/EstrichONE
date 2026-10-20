import { DeleteOutlined, DownloadOutlined, FileOutlined, FilePdfOutlined, InboxOutlined, PlayCircleOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Empty, Image, Popconfirm, Select, Space, Table, Tag, Typography, Upload } from 'antd';
import { useState } from 'react';
import { api, del, get, patch, post, problemMessage } from '../../api/client';
import type { DirectoryUser, DocumentCategory, FileResponse } from '../../api/models';
import { uuidv7 } from '../../shared/ids';
import { formatDateTime } from '../../shared/status';

export function fileSize(bytes: number): string {
  return bytes < 1024 * 1024
    ? `${Math.max(1, Math.round(bytes / 1024))} KB`
    : `${(bytes / 1024 / 1024).toLocaleString('de-DE', { maximumFractionDigits: 1 })} MB`;
}

export const documentCategoryLabels: Record<DocumentCategory, string> = {
  offer: 'Angebot', order: 'Auftrag', invoice: 'Rechnung', plan: 'Vordruck / Plan', delivery: 'Lieferung', acceptance: 'Abnahme',
  customer: 'Kundendokument', office: 'Büro-Ordner', other: 'Sonstiges',
};
const categoryColors: Partial<Record<DocumentCategory, string>> = {
  offer: 'blue', order: 'geekblue', invoice: 'purple', plan: 'cyan', delivery: 'orange', acceptance: 'green',
};

const variantUrl = (file: FileResponse, variant: 'thumb' | 'display') =>
  `/api/v1/files/${file.id}/content?variant=${file.variants === 'ready' ? variant : 'original'}`;

/** Ankündigen (mit SHA-256), dann Inhalt senden – der Server prüft Typ und Prüfsumme. Gleicher Ablauf wie die Apps. */
async function upload(bauakteId: string, file: File, category: DocumentCategory | null): Promise<void> {
  const bytes = await file.arrayBuffer();
  const sha256 = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), (b) => b.toString(16).padStart(2, '0')).join('');
  const contentType = file.type || 'application/octet-stream';
  const kind = contentType.startsWith('image/') ? 'photo' : contentType.startsWith('video/') ? 'video' : 'document';
  const id = uuidv7();
  await post('files', {
    id, bauakteId, kind, originalName: file.name, contentType, sizeBytes: file.size, sha256,
    capturedAt: kind === 'photo' ? new Date(file.lastModified).toISOString() : null,
    category: kind === 'document' ? category : null,
  });
  await api(`files/${id}/content`, { method: 'PUT', raw: bytes });
}

/** Gemeinsame Dateiliste und Hochladen mit Fortschritt. */
function useFiles(bauakteId: string) {
  const queries = useQueryClient();
  const files = useQuery({ queryKey: ['files', bauakteId], queryFn: () => get<FileResponse[]>(`bauakten/${bauakteId}/files`) });
  const [status, setStatus] = useState<{ type: 'info' | 'success' | 'error'; text: string } | null>(null);
  const refresh = () => {
    void queries.invalidateQueries({ queryKey: ['files', bauakteId] });
    void queries.invalidateQueries({ queryKey: ['history', bauakteId] });
  };
  const uploadAll = async (list: File[], category: DocumentCategory | null = null) => {
    let done = 0;
    const errors: string[] = [];
    for (const file of list) {
      setStatus({ type: 'info', text: `Lade ${done + errors.length + 1} von ${list.length} hoch …` });
      try {
        await upload(bauakteId, file, category);
        done++;
      } catch (e) {
        errors.push(`${file.name}: ${problemMessage(e)}`);
      }
    }
    setStatus(errors.length > 0 ? { type: 'error', text: errors.join(' ') } : { type: 'success', text: `${done} ${done === 1 ? 'Datei' : 'Dateien'} hochgeladen.` });
    refresh();
  };
  return { files, status, setStatus, uploadAll, refresh };
}

function Dropzone({ accept, hint, onFiles }: { accept?: string; hint: string; onFiles: (files: File[]) => void }) {
  return (
    <Upload.Dragger multiple accept={accept} showUploadList={false}
      beforeUpload={(file, list) => {
        // antd ruft beforeUpload je Datei auf; die ganze Auswahl wird einmal verarbeitet.
        if (file === list[0]) {
          onFiles(list);
        }
        return Upload.LIST_IGNORE;
      }}>
      <p className="ant-upload-drag-icon"><InboxOutlined /></p>
      <p className="ant-upload-text">Dateien hochladen</p>
      <p className="ant-upload-hint">{hint}</p>
    </Upload.Dragger>
  );
}

function StatusLine({ status, onClose }: { status: { type: 'info' | 'success' | 'error'; text: string } | null; onClose: () => void }) {
  return status && <Alert role="status" type={status.type} showIcon message={status.text} closable onClose={onClose} />;
}

function DeleteButton({ file, onDeleted }: { file: FileResponse; onDeleted: () => void }) {
  const { message } = App.useApp();
  return (
    <Popconfirm title={`„${file.originalName}“ löschen?`} okText="Löschen" okButtonProps={{ danger: true }} cancelText="Abbrechen"
      onConfirm={async () => {
        try {
          await del(`files/${file.id}`);
          onDeleted();
        } catch (e) {
          message.error(problemMessage(e));
        }
      }}>
      <Button type="text" size="small" danger icon={<DeleteOutlined />} aria-label={`${file.originalName} löschen`} />
    </Popconfirm>
  );
}

/** Fotos der Bauakte als Galerie (Vorschaubilder, Großansicht mit Blättern). */
export function BauaktePhotos({ bauakteId, canUpload, canDelete }: { bauakteId: string; canUpload: boolean; canDelete: boolean }) {
  const { files, status, setStatus, uploadAll, refresh } = useFiles(bauakteId);
  const photos = (files.data ?? []).filter((f) => f.kind === 'photo');
  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      {canUpload && <Dropzone accept="image/*" hint="Klicken oder Fotos hierher ziehen (JPEG, PNG, HEIC)." onFiles={(l) => void uploadAll(l)} />}
      <StatusLine status={status} onClose={() => setStatus(null)} />
      <Card size="small" title={`Fotos (${photos.length})`} loading={files.isLoading}>
        {photos.length === 0
          ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch keine Fotos." />
          : (
            <Image.PreviewGroup>
              <div className="photo-grid">
                {photos.map((p) => (
                  <figure key={p.id} className="photo-tile">
                    {p.state === 'stored'
                      ? <Image src={variantUrl(p, 'thumb')} preview={{ src: variantUrl(p, 'display') }} alt={`Foto vom ${formatDateTime(p.capturedAt ?? p.createdAt)}`} />
                      : <div className="photo-pending">Upload ausstehend</div>}
                    <figcaption>
                      <span className="tabular">{formatDateTime(p.capturedAt ?? p.createdAt)}</span>
                      {canDelete && <DeleteButton file={p} onDeleted={refresh} />}
                    </figcaption>
                  </figure>
                ))}
              </div>
            </Image.PreviewGroup>
          )}
      </Card>
    </Space>
  );
}

/** Videos mit Wiedergabe im Browser (Range-Anfragen, kein vollständiger Download nötig). */
export function BauakteVideos({ bauakteId, canUpload, canDelete }: { bauakteId: string; canUpload: boolean; canDelete: boolean }) {
  const { files, status, setStatus, uploadAll, refresh } = useFiles(bauakteId);
  const videos = (files.data ?? []).filter((f) => f.kind === 'video');
  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      {canUpload && <Dropzone accept="video/*" hint="Klicken oder Videos hierher ziehen (MP4, MOV)." onFiles={(l) => void uploadAll(l)} />}
      <StatusLine status={status} onClose={() => setStatus(null)} />
      <Card size="small" title={`Videos (${videos.length})`} loading={files.isLoading}>
        {videos.length === 0
          ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch keine Videos." />
          : (
            <div className="video-grid">
              {videos.map((v) => (
                <Card key={v.id} size="small" styles={{ body: { padding: 8 } }}>
                  {v.state === 'stored'
                    ? <video controls preload="metadata" src={`/api/v1/files/${v.id}/content`} style={{ width: '100%', borderRadius: 4, background: '#000' }} />
                    : <div className="photo-pending" style={{ aspectRatio: '16/9' }}><PlayCircleOutlined /> Upload ausstehend</div>}
                  <Space style={{ justifyContent: 'space-between', width: '100%', marginTop: 6 }}>
                    <Typography.Text ellipsis style={{ maxWidth: 220 }}>{v.originalName}</Typography.Text>
                    <Space size={4}>
                      <Typography.Text type="secondary" className="tabular">{fileSize(v.sizeBytes)}</Typography.Text>
                      {canDelete && <DeleteButton file={v} onDeleted={refresh} />}
                    </Space>
                  </Space>
                </Card>
              ))}
            </div>
          )}
      </Card>
    </Space>
  );
}

/** Dokumente mit Dokumentart (Angebot, Plan, Lieferung …), Filter und Hochladen in eine Kategorie. */
export function BauakteDocuments({ bauakteId, canUpload, canDelete, people }: {
  bauakteId: string; canUpload: boolean; canDelete: boolean; people: DirectoryUser[];
}) {
  const { message } = App.useApp();
  const { files, status, setStatus, uploadAll, refresh } = useFiles(bauakteId);
  const [category, setCategory] = useState<DocumentCategory>('other');
  const [filter, setFilter] = useState<DocumentCategory | 'all'>('all');
  const documents = (files.data ?? []).filter((f) => f.kind === 'document');
  const shown = documents.filter((d) => filter === 'all' || (d.category ?? 'other') === filter);
  const counts = documents.reduce<Record<string, number>>((acc, d) => ({ ...acc, [d.category ?? 'other']: (acc[d.category ?? 'other'] ?? 0) + 1 }), {});

  const recategorize = async (file: FileResponse, value: DocumentCategory) => {
    try {
      await patch(`files/${file.id}`, { caption: file.caption, category: value });
      refresh();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      {canUpload && (
        <Card size="small">
          <Space direction="vertical" style={{ width: '100%' }}>
            <Space>
              <Typography.Text>Dokumentart für neue Dateien:</Typography.Text>
              <Select aria-label="Dokumentart" value={category} onChange={setCategory} style={{ width: 200 }}
                options={(Object.keys(documentCategoryLabels) as DocumentCategory[]).map((k) => ({ value: k, label: documentCategoryLabels[k] }))} />
            </Space>
            <Dropzone hint="Klicken oder Dateien hierher ziehen. PDF, Office-Dokumente, Pläne." onFiles={(l) => void uploadAll(l, category)} />
          </Space>
        </Card>
      )}
      <StatusLine status={status} onClose={() => setStatus(null)} />
      <Card size="small" title={`Dokumente (${documents.length})`}
        extra={<Select size="small" aria-label="Filter Dokumentart" value={filter} onChange={setFilter} style={{ width: 210 }}
          options={[{ value: 'all', label: `Alle Dokumentarten (${documents.length})` },
            ...(Object.keys(documentCategoryLabels) as DocumentCategory[]).filter((k) => counts[k]).map((k) => ({ value: k, label: `${documentCategoryLabels[k]} (${counts[k]})` }))]} />}>
        <Table<FileResponse> size="small" rowKey="id" pagination={false} dataSource={shown} loading={files.isLoading}
          locale={{ emptyText: 'Noch keine Dokumente.' }}
          columns={[
            {
              title: 'Datei', dataIndex: 'originalName', render: (v: string, f) => {
                const icon = f.contentType === 'application/pdf' ? <FilePdfOutlined style={{ color: '#CF1322' }} /> : <FileOutlined />;
                return f.state === 'stored'
                  ? <a href={`/api/v1/files/${f.id}/content`} target="_blank" rel="noopener noreferrer">{icon} {v}</a>
                  : <Typography.Text type="secondary">{icon} {v} (Upload ausstehend)</Typography.Text>;
              },
            },
            {
              title: 'Dokumentart', dataIndex: 'category', width: 190, render: (v: DocumentCategory | null, f) => canUpload
                ? <Select size="small" variant="borderless" value={v ?? 'other'} style={{ width: 170 }} aria-label={`Dokumentart von ${f.originalName}`}
                    onChange={(value) => void recategorize(f, value)}
                    options={(Object.keys(documentCategoryLabels) as DocumentCategory[]).map((k) => ({ value: k, label: documentCategoryLabels[k] }))} />
                : <Tag color={categoryColors[v ?? 'other']} bordered={false}>{documentCategoryLabels[v ?? 'other']}</Tag>,
            },
            { title: 'Größe', dataIndex: 'sizeBytes', width: 100, align: 'right', render: (v: number) => <span className="tabular">{fileSize(v)}</span> },
            {
              title: 'Hochgeladen', dataIndex: 'createdAt', width: 230, render: (v: string, f) => (
                <span><span className="tabular">{formatDateTime(v)}</span> · {people.find((p) => p.id === f.uploadedByUserId)?.displayName ?? '–'}</span>
              ),
            },
            {
              title: '', key: 'actions', width: 90, align: 'right', render: (_, f) => (
                <Space size={0}>
                  {f.state === 'stored' && <Button type="text" size="small" icon={<DownloadOutlined />} href={`/api/v1/files/${f.id}/content?download=1`} aria-label={`${f.originalName} herunterladen`} download={f.originalName} />}
                  {canDelete && <DeleteButton file={f} onDeleted={refresh} />}
                </Space>
              ),
            },
          ]} />
      </Card>
    </Space>
  );
}
