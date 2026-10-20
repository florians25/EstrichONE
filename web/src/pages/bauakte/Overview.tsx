import {
  AimOutlined, CalendarOutlined, CheckOutlined, CloseOutlined, EditOutlined, EnvironmentOutlined, ExperimentOutlined, NumberOutlined,
  PictureOutlined, PushpinFilled, UserOutlined, WarningOutlined,
} from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Card, Col, Descriptions, Empty, Image, List, Row, Space, Tag, Typography } from 'antd';
import { useState } from 'react';
import { get } from '../../api/client';
import type {
  BauakteDetail, BauakteMember, ContactResponse, FileResponse, Inspection, NoteDto, PlanningRange, RoomOverview, WorkItemDto,
} from '../../api/models';
import { useAuth } from '../../auth/AuthContext';
import { addDays, isoDay, parseDay } from '../../shared/days';
import { SiteMap, statusMarkerColors } from '../../shared/SiteMap';
import { address, BauakteStatusTag, formatDateTime } from '../../shared/status';
import { cmNumber } from './Records';

export const area = (v: number) => `${v.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} m²`;
const day = (v: string | null) => (v ? parseDay(v).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }) : '–');
const content = (f: FileResponse, variant: 'thumb' | 'display') => `/api/v1/files/${f.id}/content?variant=${f.variants === 'ready' ? variant : 'original'}`;

/** Ein Feld im Kopfstreifen der Baustellenübersicht. */
function Fact({ icon, label, children }: { icon?: React.ReactNode; label: string; children: React.ReactNode }) {
  return (
    <div className="fact">
      {icon && <span className="fact-icon">{icon}</span>}
      <div>
        <div className="fact-label">{label}</div>
        <div className="fact-value">{children}</div>
      </div>
    </div>
  );
}

/**
 * Baustellenübersicht wie in Estrich Web: Kopfstreifen (Bauakte, Kunde, Baustelle, Aufbau, Fläche, Zugewiesen),
 * Objektbilder, Standort mit Karte, Kennzahlen, Baustellendaten. Standort im Büro per Klick in die Karte.
 */
export function BauakteOverview({ bauakte: b, customer, members, canEdit, onLocation, onTab, onEdit }: {
  bauakte: BauakteDetail;
  customer: ContactResponse | null;
  members: BauakteMember[];
  canEdit: boolean;
  onLocation: (latitude: number | null, longitude: number | null) => Promise<boolean>;
  onTab: (tab: string) => void;
  onEdit: () => void;
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
  const [gallery, setGallery] = useState(false);

  const openWork = (work.data ?? []).filter((w) => w.status !== 'done');
  const photos = (files.data ?? []).filter((f) => f.kind === 'photo' && f.state === 'stored');
  const lastCm = (inspections.data ?? []).filter((i) => i.kind === 'cmMeasurement').sort((x, y) => y.performedAt.localeCompare(x.performedAt))[0];
  const initial = (inspections.data ?? []).filter((i) => i.kind === 'initialInspection').sort((x, y) => y.performedAt.localeCompare(x.performedAt))[0];
  const next = (planning.data?.assignments ?? []).filter((a) => a.bauakteId === b.id)
    .sort((x, y) => (x.day + (x.startTime ?? '')).localeCompare(y.day + (y.startTime ?? '')))[0];
  const pinned = (notes.data ?? []).filter((n) => n.isPinned);
  const screeds = [...new Set((rooms.data?.rooms ?? []).map((r) => r.screedTypeName).filter(Boolean))];
  const position: [number, number] | null = picked ?? (b.latitude !== null && b.longitude !== null ? [b.latitude, b.longitude] : null);
  const responsible = members.find((m) => m.role === 'responsible');

  const finishPick = async (save: boolean) => {
    if (save && picked && !(await onLocation(picked[0], picked[1]))) {
      return;
    }
    setPicking(false);
    setPicked(null);
  };

  return (
    <Row gutter={[16, 16]} className="overview">
      <Col span={24}>
        <Card size="small" className="fact-strip" styles={{ body: { padding: 0 } }}>
          <div className="fact-strip-grid">
            <div className="fact-strip-main">
              <Fact icon={<NumberOutlined />} label="Bauakte"><span className="tabular">{b.number ?? '–'}</span></Fact>
              <Fact icon={<UserOutlined />} label="Kunde">{customer?.name ?? '–'}</Fact>
              <Fact icon={<EnvironmentOutlined />} label="Baustelle">{address(b)}</Fact>
            </div>
            <Fact label="Aufbau">{screeds.length ? screeds.join(', ') : '–'}</Fact>
            <Fact label="Fläche">{rooms.data ? area(rooms.data.totalAreaSquareMeters) : '–'}</Fact>
            <Fact label="Zugewiesen">{responsible?.displayName ?? '–'}{members.length > 1 && <Typography.Text type="secondary"> +{members.length - 1}</Typography.Text>}</Fact>
            <Fact label="Ausführung">{b.plannedStart || b.plannedEnd ? `${day(b.plannedStart)} – ${day(b.plannedEnd)}` : '–'}</Fact>
          </div>
        </Card>
      </Col>

      <Col xs={24} xl={12}>
        <Card size="small" title="Objektbilder" extra={<Typography.Text type="secondary">{photos.length} {photos.length === 1 ? 'Foto' : 'Fotos'}</Typography.Text>} style={{ height: '100%' }}>
          {photos.length === 0
            ? (
              <div className="hero hero-empty">
                <PictureOutlined style={{ fontSize: 28 }} />
                <span>Noch kein Bild hochgeladen</span>
                <Button onClick={() => onTab('fotos')}>Erstes Foto hinzufügen</Button>
              </div>
            )
            : (
              <div className="hero" style={{ backgroundImage: `url(${content(photos[0], 'display')})` }}>
                <div className="hero-shade">
                  <PictureOutlined style={{ fontSize: 22 }} />
                  <strong>{photos.length} {photos.length === 1 ? 'Foto hinterlegt' : 'Fotos hinterlegt'}</strong>
                  <Button type="primary" icon={<PictureOutlined />} onClick={() => setGallery(true)}>Foto-Galerie öffnen</Button>
                </div>
                <Image.PreviewGroup preview={{ visible: gallery, onVisibleChange: setGallery }}
                  items={photos.map((p) => content(p, 'display'))} />
              </div>
            )}
        </Card>
      </Col>

      <Col xs={24} xl={12}>
        <Card size="small" style={{ height: '100%' }}
          title={<Space>Standort<Typography.Text type="secondary" style={{ fontWeight: 400 }}>{[b.street, b.city].filter(Boolean).join(' ')}</Typography.Text></Space>}
          extra={canEdit && (picking
            ? <Space>
                <Button size="small" icon={<CloseOutlined />} onClick={() => void finishPick(false)}>Abbrechen</Button>
                <Button size="small" type="primary" icon={<CheckOutlined />} disabled={!picked} onClick={() => void finishPick(true)}>Standort speichern</Button>
              </Space>
            : <Button size="small" icon={<AimOutlined />} onClick={() => setPicking(true)}>{position ? 'Standort ändern' : 'Standort festlegen'}</Button>)}>
          {picking && <Alert type="info" showIcon style={{ marginBottom: 8 }} message="In die Karte klicken, um den Standort der Baustelle zu setzen." />}
          {position || picking
            ? <SiteMap height={250} onPick={picking ? (lat, lng) => setPicked([lat, lng]) : undefined}
                markers={position ? [{ id: b.id, latitude: position[0], longitude: position[1], color: statusMarkerColors[b.status], label: b.title }] : []} />
            : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch kein Standort. Die Apps übernehmen ihn beim Anlegen vor Ort, im Büro per „Standort festlegen“." />}
          {position && (
            <Typography.Paragraph type="secondary" style={{ margin: '8px 0 0' }}>
              <span className="tabular">{position[0].toLocaleString('de-DE', { maximumFractionDigits: 5 })}, {position[1].toLocaleString('de-DE', { maximumFractionDigits: 5 })}</span>
              {' · '}<a href={`https://www.openstreetmap.org/directions?to=${position[0]}%2C${position[1]}`} target="_blank" rel="noreferrer">Route</a>
            </Typography.Paragraph>
          )}
        </Card>
      </Col>

      <Col xs={24} md={8}>
        <Card size="small" className="kpi clickable" onClick={() => onTab('aufgaben')}>
          <Space align="start">
            <WarningOutlined className="kpi-icon" />
            <div>
              <div className="fact-label">Offene Aufgaben und Mängel</div>
              <div className="kpi-value">{has('work.read') ? openWork.length : '–'}</div>
              <Space size={4}>
                {openWork.filter((w) => w.kind === 'defect').length > 0 && <Tag color="orange" bordered={false}>{openWork.filter((w) => w.kind === 'defect').length} Mängel</Tag>}
                {openWork.filter((w) => w.dueDate && w.dueDate < today).length > 0 && <Tag color="red" bordered={false}>{openWork.filter((w) => w.dueDate && w.dueDate < today).length} überfällig</Tag>}
              </Space>
            </div>
          </Space>
        </Card>
      </Col>
      <Col xs={24} md={8}>
        <Card size="small" className="kpi clickable" onClick={() => onTab('erstbegehung')}>
          <Space align="start">
            <ExperimentOutlined className="kpi-icon" />
            <div>
              <div className="fact-label">Erstbegehung / CM-Messung</div>
              <div className="kpi-value">{lastCm ? `${cmNumber(lastCm.measuredCmPercent)} CM-%` : initial ? 'Erstbegehung erfolgt' : '–'}</div>
              {lastCm
                ? <Tag color={lastCm.isReadyForCovering ? 'success' : 'warning'} bordered={false}>{lastCm.isReadyForCovering ? 'Belegreif' : 'Noch nicht belegreif'} · {lastCm.location}</Tag>
                : <Typography.Text type="secondary">{initial ? formatDateTime(initial.performedAt) : 'Erstbegehung noch nicht ausgeführt'}</Typography.Text>}
            </div>
          </Space>
        </Card>
      </Col>
      <Col xs={24} md={8}>
        <Card size="small" className="kpi">
          <Space align="start">
            <CalendarOutlined className="kpi-icon" />
            <div>
              <div className="fact-label">Nächster Einsatz</div>
              <div className="kpi-value">{next ? parseDay(next.day).toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit' }) : '–'}</div>
              <Typography.Text type="secondary">{next ? `${next.startTime?.slice(0, 5) ?? ''} ${next.activity}${next.crewName ? ` · ${next.crewName}` : ''}` : 'nicht eingeplant'}</Typography.Text>
            </div>
          </Space>
        </Card>
      </Col>

      {(pinned.length > 0 || openWork.length > 0) && (
        <Col span={24}>
          <Row gutter={[16, 16]}>
            {pinned.length > 0 && (
              <Col xs={24} xl={12}>
                <Card size="small" title={<Space><PushpinFilled style={{ color: '#FA8C16' }} />Angeheftete Notizen</Space>} extra={<a onClick={() => onTab('notizen')}>Alle Notizen</a>}>
                  <List size="small" dataSource={pinned} renderItem={(n) => <List.Item className={n.color ? `note-${n.color}` : undefined}><Typography.Text className="pre">{n.text}</Typography.Text></List.Item>} />
                </Card>
              </Col>
            )}
            {openWork.length > 0 && (
              <Col xs={24} xl={12}>
                <Card size="small" title="Offene Aufgaben und Mängel" extra={<a onClick={() => onTab('aufgaben')}>Alle anzeigen</a>}>
                  <List size="small" dataSource={openWork.slice(0, 6)} renderItem={(w) => (
                    <List.Item extra={w.dueDate && <Typography.Text type={w.dueDate < today ? 'danger' : 'secondary'} className="tabular">{day(w.dueDate)}</Typography.Text>}>
                      <Space>{w.kind === 'defect' ? <WarningOutlined style={{ color: '#FA8C16' }} /> : <CheckOutlined style={{ color: '#8C8C8C' }} />}{w.title}</Space>
                    </List.Item>
                  )} />
                </Card>
              </Col>
            )}
          </Row>
        </Col>
      )}

      <Col span={24}>
        <Card size="small" title="Baustellendaten" extra={canEdit && <Button size="small" icon={<EditOutlined />} onClick={onEdit}>Bearbeiten</Button>}>
          <div className="section-title">Basisdaten</div>
          <Descriptions column={{ xs: 1, md: 2, xl: 4 }} size="small" layout="vertical" colon={false} className="field-grid" items={[
            { key: 'nr', label: 'Bauakte', children: <span className="tabular">{b.number ?? '–'}</span> },
            { key: 'customer', label: 'Kunde', children: customer?.name ?? '–' },
            { key: 'start', label: 'Beginn', children: day(b.plannedStart) },
            { key: 'end', label: 'Ende', children: day(b.plannedEnd) },
            { key: 'status', label: 'Status', children: <BauakteStatusTag status={b.status} /> },
            { key: 'resp', label: 'Zuständig', children: responsible?.displayName ?? '–' },
            { key: 'created', label: 'Angelegt', children: formatDateTime(b.createdAt) },
            { key: 'updated', label: 'Geändert', children: formatDateTime(b.updatedAt) },
          ]} />
          <div className="section-title">Standort</div>
          <Descriptions column={{ xs: 1, md: 2, xl: 4 }} size="small" layout="vertical" colon={false} className="field-grid" items={[
            { key: 'street', label: 'Straße', children: b.street ?? '–' },
            { key: 'zip', label: 'PLZ', children: b.postalCode ?? '–' },
            { key: 'city', label: 'Ort', children: b.city ?? '–' },
            { key: 'geo', label: 'Koordinaten', children: position ? <span className="tabular">{position[0].toFixed(5)}, {position[1].toFixed(5)}</span> : '–' },
          ]} />
          <div className="section-title">Beschreibung</div>
          <Typography.Paragraph className="pre" style={{ margin: '4px 12px 0' }}>{b.description ?? 'Keine Beschreibung.'}</Typography.Paragraph>
        </Card>
      </Col>
    </Row>
  );
}
