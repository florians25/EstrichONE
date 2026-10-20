import { CompassOutlined, PrinterOutlined, ReloadOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, List, Segmented, Select, Space, Switch, Tag, Typography } from 'antd';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { get } from '../../api/client';
import type { AssignmentView, CrewResponse, PlanningRange } from '../../api/models';
import { addDays, isoDay, parseDay } from '../../shared/days';
import { absenceNames, kindColors, kindIcons, kindLabels, timeRange } from './shared';

const route = (a: AssignmentView) => a.bauakteAddress ? `https://www.openstreetmap.org/search?query=${encodeURIComponent(a.bauakteAddress)}` : null;

/** Arbeitsplan (Agenda): wer ist wann wo – für die eigenen Einsätze oder alle Kolonnen, mit Navigation und Druck. */
export default function WorkPlanPage() {
  const today = isoDay(new Date());
  const [length, setLength] = useState<7 | 14>(7);
  const [mine, setMine] = useState(false);
  const [crew, setCrew] = useState<string | null>(null);
  const to = addDays(today, length - 1);
  const range = useQuery({
    queryKey: ['planning', mine ? 'mine' : 'all', today, to],
    queryFn: () => get<PlanningRange>(mine ? 'planning/mine' : 'planning/range', { from: today, to }),
  });
  const crews = useQuery({ queryKey: ['crews'], queryFn: () => get<CrewResponse[]>('planning/crews') });
  const days = Array.from({ length }, (_, i) => addDays(today, i));
  const items = (range.data?.assignments ?? []).filter((a) => !crew || (crew === 'none' ? !a.crewId : a.crewId === crew));

  return (
    <PageContainer
      title={<Space direction="vertical" size={0}><Typography.Text type="secondary" className="overline">Planung</Typography.Text>Arbeitsplan</Space>}
      extra={[
        <Button key="reload" icon={<ReloadOutlined />} onClick={() => void range.refetch()}>Aktualisieren</Button>,
        <Button key="print" icon={<PrinterOutlined />} onClick={() => window.print()}>Drucken</Button>,
      ]}
    >
      <Card size="small" style={{ marginBottom: 12 }}>
        <Space wrap size={16}>
          <Segmented value={length} onChange={(v) => setLength(v as 7 | 14)} options={[{ value: 7, label: '7 Tage' }, { value: 14, label: '14 Tage' }]} />
          <Space><Switch size="small" checked={mine} onChange={setMine} aria-label="Nur meine Einsätze" />Nur meine Einsätze</Space>
          <Select allowClear placeholder="Alle Kolonnen" style={{ width: 220 }} aria-label="Kolonne" value={crew} onChange={(v) => setCrew(v ?? null)}
            options={[...(crews.data ?? []).filter((c) => c.isActive).map((c) => ({ value: c.id, label: c.name })), { value: 'none', label: 'Ohne Kolonne' }]} />
        </Space>
      </Card>
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        {days.map((d) => {
          const own = items.filter((a) => a.day === d);
          const absent = (range.data?.absences ?? []).filter((a) => a.from <= d && d <= a.to);
          return (
            <Card key={d} size="small" className="agenda-day"
              title={<Space>
                <span>{parseDay(d).toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' })}</span>
                {d === today && <Tag color="blue" bordered={false}>Heute</Tag>}
              </Space>}
              extra={<Typography.Text type="secondary">{own.length} {own.length === 1 ? 'Einsatz' : 'Einsätze'}</Typography.Text>}>
              {absent.length > 0 && (
                <div style={{ marginBottom: 8 }}>{absent.map((a) => <Tag key={a.id} color="orange" bordered={false}>{a.userName} · {absenceNames[a.kind] ?? a.kind}</Tag>)}</div>
              )}
              {own.length === 0
                ? <Typography.Text type="secondary">Keine Einsätze.</Typography.Text>
                : (
                  <List dataSource={own} renderItem={(a) => (
                    <List.Item actions={[
                      route(a) && <a key="nav" href={route(a)!} target="_blank" rel="noreferrer"><CompassOutlined /> Navigation starten</a>,
                      <Link key="open" to={`/bauakten/${a.bauakteId}`}>Bauakte öffnen</Link>,
                    ].filter(Boolean)}>
                      <List.Item.Meta
                        avatar={<div className="agenda-time tabular"><span style={{ color: kindColors[a.kind], fontSize: 16 }}>{kindIcons[a.kind]}</span><span>{timeRange(a) || 'ganztags'}</span></div>}
                        title={<Space wrap size={6}>
                          <span>{a.bauakteNumber ?? ''} {a.bauakteTitle}</span>
                          <Tag bordered={false} style={{ color: kindColors[a.kind] }}>{kindLabels[a.kind]}</Tag>
                          {a.crewName && <Tag bordered={false}><span className="swatch" style={{ background: a.crewColor ?? '#d9d9d9', marginRight: 4 }} />{a.crewName}</Tag>}
                          {a.warnings.length > 0 && <Tag color="warning" bordered={false}>{a.warnings.length} Hinweise</Tag>}
                        </Space>}
                        description={<Space direction="vertical" size={0}>
                          <span>{a.activity}{a.bauakteAddress && <Typography.Text type="secondary"> · {a.bauakteAddress}</Typography.Text>}</span>
                          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                            {a.people.map((p) => p.name).join(', ') || '–'}{a.resources.length > 0 && ` · ${a.resources.map((r) => r.name).join(', ')}`}
                          </Typography.Text>
                          {a.notes && <Typography.Text style={{ fontSize: 12 }}>Notiz: {a.notes}</Typography.Text>}
                        </Space>}
                      />
                    </List.Item>
                  )} />
                )}
            </Card>
          );
        })}
      </Space>
    </PageContainer>
  );
}
