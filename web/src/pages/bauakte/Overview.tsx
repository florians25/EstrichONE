import { AimOutlined, CalendarOutlined, CheckOutlined, CloseOutlined, ExperimentOutlined, PictureOutlined, PushpinOutlined, WarningOutlined } from '@ant-design/icons';
import { StatisticCard } from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Card, Col, Empty, Image, List, Row, Space, Tag, Typography } from 'antd';
import { useState } from 'react';
import { get } from '../../api/client';
import type { BauakteDetail, FileResponse, Inspection, NoteDto, PlanningRange, RoomOverview, WorkItemDto } from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { addDays, isoDay, parseDay } from '../../shared/days';
import { SiteMap, statusMarkerColors } from '../../shared/SiteMap';
import { address } from '../../shared/status';
import { cmNumber } from './Records';

const area = (v: number) => `${v.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} m²`;

/**
 * Übersicht einer Bauakte: Kennzahlen aus Räumen, Aufgaben, Dateien, Prüfungen und Planung,
 * Standort auf der Karte (im Büro per Klick festlegbar), neueste Fotos und angeheftete Notizen.
 */
export function BauakteOverview({ bauakte: b, canEdit, onLocation, onTab }: {
  bauakte: BauakteDetail;
  canEdit: boolean;
  onLocation: (latitude: number | null, longitude: number | null) => Promise<boolean>;
  onTab: (tab: string) => void;
}) {
  const { has } = useAuth();
  const today = isoDay(new Date());
  const rooms = useQuery({ queryKey: ['rooms', b.id], queryFn: () => get<RoomOverview>(`bauakten/${b.id}/rooms`) });
  const work = useQuery({ queryKey: ['work', b.id], enabled: has('work.read'), queryFn: () => get<WorkItemDto[]>(`bauakten/${b.id}/work-items`) });
  const files = useQuery({ queryKey: ['files', b.id], queryFn: () => get<FileResponse[]>(`bauakten/${b.id}/files`) });
  const inspections = useQuery({ queryKey: ['inspections', b.id], queryFn: () => get<Inspection[]>(`bauakten/${b.id}/inspections`) });
  const notes = useQuery({ queryKey: ['notes', b.id], queryFn: () => get<NoteDto[]>(`bauakten/${b.id}/notes`) });
  const planning = useQuery({
    queryKey: ['planning', 'bauakte', b.id, today], enabled: has('planning.read'),
    queryFn: () => get<PlanningRange>('planning/range', { from: today, to: addDays(today, 60) }),
  });
  const [picking, setPicking] = useState(false);
  const [picked, setPicked] = useState<[number, number] | null>(null);

  const openWork = (work.data ?? []).filter((w) => w.status !== 'done');
  const openDefects = openWork.filter((w) => w.kind === 'defect');
  const overdue = openWork.filter((w) => w.dueDate && w.dueDate < today);
  const photos = (files.data ?? []).filter((f) => f.kind === 'photo' && f.state === 'stored').slice(0, 8);
  const lastCm = (inspections.data ?? []).filter((i) => i.kind === 'cmMeasurement').sort((x, y) => y.performedAt.localeCompare(x.performedAt))[0];
  const next = (planning.data?.assignments ?? []).filter((a) => a.bauakteId === b.id).sort((x, y) => (x.day + (x.startTime ?? '')).localeCompare(y.day + (y.startTime ?? '')));
  const pinned = (notes.data ?? []).filter((n) => n.isPinned);
  const position: [number, number] | null = picked ?? (b.latitude !== null && b.longitude !== null ? [b.latitude, b.longitude] : null);

  const finishPick = async (save: boolean) => {
    if (save && picked && !(await onLocation(picked[0], picked[1]))) {
      return;
    }
    setPicking(false);
    setPicked(null);
  };

  return (
    <Row gutter={[16, 16]}>
      <Col span={24}>
        <StatisticCard.Group direction="row" className="overview-kpis">
          <StatisticCard statistic={{ title: 'Fläche', value: rooms.data ? area(rooms.data.totalAreaSquareMeters) : '–', description: rooms.data ? `${rooms.data.rooms.length} Räume, ${area(rooms.data.heatedAreaSquareMeters)} beheizt` : undefined }}
            onClick={() => onTab('rooms')} className="clickable" />
          {has('work.read') && (
            <StatisticCard statistic={{
              title: 'Offene Aufgaben', value: openWork.length,
              description: openDefects.length || overdue.length
                ? <Space size={4}>{openDefects.length > 0 && <Tag color="orange" bordered={false}>{openDefects.length} Mängel</Tag>}{overdue.length > 0 && <Tag color="red" bordered={false}>{overdue.length} überfällig</Tag>}</Space>
                : 'nichts überfällig',
            }} onClick={() => onTab('work')} className="clickable" />
          )}
          <StatisticCard statistic={{ title: 'Fotos und Dokumente', value: files.data?.length ?? '–', icon: <PictureOutlined style={{ fontSize: 24, color: '#0B2D5B' }} /> }}
            onClick={() => onTab('files')} className="clickable" />
          <StatisticCard statistic={{
            title: 'Letzte CM-Messung',
            value: lastCm ? `${cmNumber(lastCm.measuredCmPercent)} CM-%` : '–',
            description: lastCm
              ? <Tag color={lastCm.isReadyForCovering ? 'success' : 'warning'} bordered={false}>{lastCm.isReadyForCovering ? 'Belegreif' : 'Noch nicht belegreif'} · {lastCm.location}</Tag>
              : 'noch keine Messung',
            icon: <ExperimentOutlined style={{ fontSize: 24, color: '#0B2D5B' }} />,
          }} onClick={() => onTab('records')} className="clickable" />
          {has('planning.read') && (
            <StatisticCard statistic={{
              title: 'Nächster Einsatz',
              value: next[0] ? parseDay(next[0].day).toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit' }) : '–',
              description: next[0] ? `${next[0].startTime?.slice(0, 5) ?? ''} ${next[0].activity}${next[0].crewName ? ` · ${next[0].crewName}` : ''}` : 'nicht eingeplant',
              icon: <CalendarOutlined style={{ fontSize: 24, color: '#0B2D5B' }} />,
            }} />
          )}
        </StatisticCard.Group>
      </Col>

      <Col xs={24} xl={14}>
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Card size="small" title="Beschreibung">
            <Typography.Paragraph className="pre" style={{ margin: 0 }}>{b.description ?? 'Keine Beschreibung.'}</Typography.Paragraph>
          </Card>
          {pinned.length > 0 && (
            <Card size="small" title={<Space><PushpinOutlined />Angeheftete Notizen</Space>}>
              <List size="small" dataSource={pinned} renderItem={(n) => <List.Item><Typography.Text className="pre">{n.text}</Typography.Text></List.Item>} />
            </Card>
          )}
          {openWork.length > 0 && (
            <Card size="small" title="Offene Aufgaben und Mängel" extra={<a onClick={() => onTab('work')}>Alle anzeigen</a>}>
              <List size="small" dataSource={openWork.slice(0, 6)} renderItem={(w) => (
                <List.Item extra={w.dueDate && <Typography.Text type={w.dueDate < today ? 'danger' : 'secondary'} className="tabular">{parseDay(w.dueDate).toLocaleDateString('de-DE')}</Typography.Text>}>
                  <Space>{w.kind === 'defect' ? <WarningOutlined style={{ color: '#FA8C16' }} /> : <CheckOutlined style={{ color: '#8C8C8C' }} />}{w.title}</Space>
                </List.Item>
              )} />
            </Card>
          )}
          <Card size="small" title="Neueste Fotos" extra={photos.length > 0 && <a onClick={() => onTab('files')}>Alle Fotos</a>}>
            {photos.length === 0
              ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch keine Fotos." />
              : (
                <Image.PreviewGroup>
                  <div className="photo-grid photo-grid-small">
                    {photos.map((p) => (
                      <Image key={p.id} alt="Foto" src={`/api/v1/files/${p.id}/content?variant=${p.variants === 'ready' ? 'thumb' : 'original'}`}
                        preview={{ src: `/api/v1/files/${p.id}/content?variant=${p.variants === 'ready' ? 'display' : 'original'}` }} />
                    ))}
                  </div>
                </Image.PreviewGroup>
              )}
          </Card>
        </Space>
      </Col>

      <Col xs={24} xl={10}>
        <Card size="small" title="Standort"
          extra={canEdit && (picking
            ? <Space>
                <Button size="small" icon={<CloseOutlined />} onClick={() => void finishPick(false)}>Abbrechen</Button>
                <Button size="small" type="primary" icon={<CheckOutlined />} disabled={!picked} onClick={() => void finishPick(true)}>Standort speichern</Button>
              </Space>
            : <Button size="small" icon={<AimOutlined />} onClick={() => setPicking(true)}>{position ? 'Standort ändern' : 'Standort festlegen'}</Button>)}>
          {picking && <Alert type="info" showIcon style={{ marginBottom: 8 }} message="In die Karte klicken, um den Standort der Baustelle zu setzen." />}
          {position || picking
            ? <SiteMap height={340} onPick={picking ? (lat, lng) => setPicked([lat, lng]) : undefined}
                markers={position ? [{ id: b.id, latitude: position[0], longitude: position[1], color: statusMarkerColors[b.status], label: b.title }] : []} />
            : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch kein Standort. Die Apps übernehmen ihn beim Anlegen vor Ort, im Büro per „Standort festlegen“." />}
          <Typography.Paragraph type="secondary" style={{ margin: '8px 0 0' }}>
            {address(b)}{position && <span className="tabular"> · {position[0].toLocaleString('de-DE', { maximumFractionDigits: 5 })}, {position[1].toLocaleString('de-DE', { maximumFractionDigits: 5 })}</span>}
          </Typography.Paragraph>
        </Card>
      </Col>
    </Row>
  );
}
