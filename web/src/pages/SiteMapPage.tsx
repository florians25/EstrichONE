import { UnorderedListOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { Badge, Button, Card, Checkbox, Col, Empty, Input, List, Row, Space, Switch, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { get } from '../api/client';
import type { BauakteMapPoint, BauakteStatus, DirectoryUser } from '../api/models';
import { SiteMap, statusMarkerColors } from '../shared/SiteMap';
import { BauakteStatusTag, bauakteStatusLabels } from '../shared/status';

const activeStatuses: BauakteStatus[] = ['inquiry', 'planned', 'inProgress', 'drying'];

/** Baustellenkarte: alle sichtbaren Bauakten mit Standort, nach Status filterbar, mit Liste daneben. */
export default function SiteMapPage() {
  const navigate = useNavigate();
  const [all, setAll] = useState(false);
  const [statuses, setStatuses] = useState<BauakteStatus[]>(activeStatuses);
  const [search, setSearch] = useState('');
  const points = useQuery({ queryKey: ['bauakten-map', all], queryFn: () => get<BauakteMapPoint[]>('bauakten/map', { all }) });
  const people = useQuery({ queryKey: ['directory'], queryFn: () => get<DirectoryUser[]>('directory/users'), staleTime: 60_000 });
  const offered: BauakteStatus[] = all ? [...activeStatuses, 'completed', 'cancelled'] : activeStatuses;

  const visible = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (points.data ?? []).filter((p) => statuses.includes(p.status)
      && (!q || [p.number, p.title, p.street, p.city].some((v) => v?.toLowerCase().includes(q))));
  }, [points.data, statuses, search]);
  const name = (id: string | null) => (id && people.data?.find((p) => p.id === id)?.displayName) || null;

  return (
    <PageContainer
      title="Baustellenkarte"
      subTitle={points.data ? `${visible.length} von ${points.data.length} Baustellen mit Standort` : undefined}
      extra={<Link to="/bauakten"><Button icon={<UnorderedListOutlined />}>Als Liste</Button></Link>}
    >
      <Card size="small" style={{ marginBottom: 16 }}>
        <Space wrap size={16}>
          <Input.Search allowClear placeholder="Nummer, Bauvorhaben, Ort" style={{ width: 280 }} aria-label="Baustellen filtern"
            onChange={(e) => setSearch(e.target.value)} />
          <Checkbox.Group value={statuses} onChange={(v) => setStatuses(v as BauakteStatus[])}
            options={offered.map((s) => ({
              value: s,
              label: <Space size={6}><Badge color={statusMarkerColors[s]} />{bauakteStatusLabels[s]}</Space>,
            }))} />
          <Space><Switch size="small" checked={all} onChange={(v) => { setAll(v); setStatuses(v ? [...activeStatuses, 'completed'] : activeStatuses); }} aria-label="Auch abgeschlossene" />Auch abgeschlossene</Space>
        </Space>
      </Card>
      <Row gutter={16}>
        <Col xs={24} xl={17}>
          <Card size="small" styles={{ body: { padding: 0 } }}>
            <SiteMap height="calc(100vh - 290px)" zoom={13}
              markers={visible.map((p) => ({
                id: p.id, latitude: p.latitude, longitude: p.longitude, color: statusMarkerColors[p.status],
                label: `${p.number ?? 'neu'} · ${p.title}`,
                popup: (
                  <Space direction="vertical" size={4} style={{ minWidth: 200 }}>
                    <Typography.Text type="secondary" className="tabular">{p.number ?? 'noch ohne Nummer'}</Typography.Text>
                    <Link to={`/bauakten/${p.id}`}><strong>{p.title}</strong></Link>
                    <span>{[p.street, p.city].filter(Boolean).join(', ') || '–'}</span>
                    <BauakteStatusTag status={p.status} />
                    {name(p.responsibleUserId) && <Typography.Text type="secondary">Zuständig: {name(p.responsibleUserId)}</Typography.Text>}
                  </Space>
                ),
              }))} />
          </Card>
        </Col>
        <Col xs={24} xl={7}>
          <Card size="small" title="Baustellen" styles={{ body: { padding: 0, maxHeight: 'calc(100vh - 330px)', overflow: 'auto' } }}>
            <List
              size="small"
              loading={points.isLoading}
              dataSource={visible}
              locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Keine Baustelle mit Standort in dieser Auswahl." /> }}
              renderItem={(p) => (
                <List.Item className="clickable" onClick={() => navigate(`/bauakten/${p.id}`)} style={{ paddingInline: 12 }}>
                  <List.Item.Meta
                    avatar={<Badge color={statusMarkerColors[p.status]} />}
                    title={<span>{p.title}</span>}
                    description={<span className="tabular">{p.number ?? 'neu'} · {p.city ?? '–'}</span>}
                  />
                </List.Item>
              )}
            />
          </Card>
        </Col>
      </Row>
    </PageContainer>
  );
}
