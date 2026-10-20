import { PrinterOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, Descriptions, Empty, List, Space, Table, Tag, Timeline, Typography } from 'antd';
import { get } from '../../api/client';
import type { BauakteDetail, DiaryEntry, DirectoryUser, Inspection, RoomOverview } from '../../api/models';
import { parseDay } from '../../shared/days';
import { formatDateTime } from '../../shared/status';

const checkTitles: Record<string, string> = {
  substrate: 'Untergrund tragfähig, eben und sauber',
  edgeInsulation: 'Randdämmstreifen vorhanden',
  heatingPressureTest: 'Fußbodenheizung abgedrückt',
  access: 'Zufahrt und Pumpenstandort',
  utilities: 'Wasser und Strom vorhanden',
};
const results: Record<string, { text: string; color: string }> = {
  ok: { text: 'In Ordnung', color: 'success' },
  notOk: { text: 'Mangel', color: 'warning' },
  notApplicable: { text: 'Entfällt', color: 'default' },
};
const weatherNames: Record<string, string> = { sunny: 'sonnig', cloudy: 'bewölkt', rain: 'Regen', snow: 'Schnee', frost: 'Frost', wind: 'Wind' };
const binderNames: Record<string, string> = { cement: 'Zementestrich (CT)', calciumSulfate: 'Calciumsulfatestrich (CA)' };

export const cmNumber = (value: number | null) =>
  value === null ? '–' : value.toLocaleString('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 2 });

export function weatherLine(entry: Pick<DiaryEntry, 'weather' | 'temperatureCelsius' | 'workersPresent'>): string {
  return [
    entry.weather ? weatherNames[entry.weather] : null,
    entry.temperatureCelsius !== null ? `${entry.temperatureCelsius} °C` : null,
    entry.workersPresent !== null ? `${entry.workersPresent} anwesend` : null,
  ].filter(Boolean).join(' · ');
}

/** Ergebnis der Erstbegehung in einem Wort, wie im Protokoll. */
export function inspectionResult(i: Pick<Inspection, 'notRequired' | 'checks'>): { text: string; color: string } {
  if (i.notRequired) {
    return { text: 'Nicht notwendig', color: 'default' };
  }
  return Object.values(i.checks).includes('notOk')
    ? { text: 'Mit Mängeln – Klärung nötig', color: 'warning' }
    : { text: 'Bestanden / freigegeben', color: 'success' };
}

/** Tagesübersicht (Bautagebuch): je Tag, was auf der Baustelle passiert ist – erfasst in den Apps. */
export function BauakteDiary({ bauakteId, people }: { bauakteId: string; people: DirectoryUser[] }) {
  const diary = useQuery({ queryKey: ['diary', bauakteId], queryFn: () => get<DiaryEntry[]>(`bauakten/${bauakteId}/diary`) });
  const name = (id: string) => people.find((p) => p.id === id)?.displayName ?? '–';
  const entries = [...(diary.data ?? [])].sort((a, b) => b.day.localeCompare(a.day) || b.createdAt.localeCompare(a.createdAt));
  return (
    <Card size="small" title={`Tageschronik (${entries.length})`} loading={diary.isLoading}
      extra={<Typography.Text type="secondary">Tagesberichte werden in den Apps auf der Baustelle erfasst.</Typography.Text>}>
      {entries.length === 0
        ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch keine Tagesberichte." />
        : (
          <Timeline items={entries.map((e) => ({
            children: (
              <div className="diary-entry">
                <Space split="·" wrap>
                  <strong>{parseDay(e.day).toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' })}</strong>
                  {weatherLine(e) && <Typography.Text type="secondary">{weatherLine(e)}</Typography.Text>}
                </Space>
                <Typography.Paragraph className="pre" style={{ margin: '4px 0' }}>{e.text}</Typography.Paragraph>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>{name(e.authorUserId)} · {formatDateTime(e.createdAt)}{e.editedAt && ' · bearbeitet'}</Typography.Text>
              </div>
            ),
          }))} />
        )}
    </Card>
  );
}

/**
 * Erstbegehung und CM-Messungen als Protokoll (wie Estrich Web): wesentliche Angaben, Untergrund und Aufbau,
 * Messwerte, Bemerkungen. Drucken bzw. als PDF speichern über den Browser.
 */
export function BauakteInspections({ bauakte, people }: { bauakte: BauakteDetail; people: DirectoryUser[] }) {
  const inspections = useQuery({ queryKey: ['inspections', bauakte.id], queryFn: () => get<Inspection[]>(`bauakten/${bauakte.id}/inspections`) });
  const rooms = useQuery({ queryKey: ['rooms', bauakte.id], queryFn: () => get<RoomOverview>(`bauakten/${bauakte.id}/rooms`) });
  const name = (id: string) => people.find((p) => p.id === id)?.displayName ?? '–';
  const all = [...(inspections.data ?? [])].sort((a, b) => b.performedAt.localeCompare(a.performedAt));
  const initial = all.filter((i) => i.kind === 'initialInspection');
  const cm = all.filter((i) => i.kind === 'cmMeasurement');
  const latest = initial[0];
  const screeds = [...new Set((rooms.data?.rooms ?? []).map((r) => r.screedTypeName).filter(Boolean))];
  const heated = (rooms.data?.rooms ?? []).some((r) => r.underfloorHeating);

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Card size="small" loading={inspections.isLoading} className="protocol"
        title={<Space direction="vertical" size={0}>
          <span>Erstbegehungsprotokoll</span>
          {latest && <Typography.Text type="secondary" style={{ fontSize: 12, fontWeight: 400 }}>Erfasst am {formatDateTime(latest.performedAt)}</Typography.Text>}
        </Space>}
        extra={latest && <Button icon={<PrinterOutlined />} onClick={() => window.print()}>Drucken / PDF</Button>}>
        {!latest
          ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Erstbegehung noch nicht ausgeführt. Sie wird in der App auf der Baustelle erfasst." />
          : (
            <>
              <div className="section-title">Wesentliche Angaben</div>
              <Descriptions column={{ xs: 1, md: 2, xl: 4 }} size="small" layout="vertical" colon={false} className="field-grid" items={[
                { key: 'result', label: 'Ergebnis', children: <Tag color={inspectionResult(latest).color} bordered={false}>{inspectionResult(latest).text}</Tag> },
                { key: 'status', label: 'Status', children: latest.correctsInspectionId ? 'Korrigiert' : 'Abgeschlossen' },
                { key: 'by', label: 'Prüfer', children: name(latest.performedByUserId) },
                { key: 'date', label: 'Datum', children: new Date(latest.performedAt).toLocaleDateString('de-DE') },
              ]} />
              <div className="section-title">Untergrund & Aufbau</div>
              <Descriptions column={{ xs: 1, md: 2, xl: 4 }} size="small" layout="vertical" colon={false} className="field-grid" items={[
                { key: 'screed', label: 'Estrichart', children: screeds.length ? screeds.join(', ') : '–' },
                { key: 'area', label: 'Fläche', children: rooms.data ? `${rooms.data.totalAreaSquareMeters.toLocaleString('de-DE', { minimumFractionDigits: 2 })} m²` : '–' },
                { key: 'heating', label: 'Fußbodenheizung', children: rooms.data?.rooms.length ? (heated ? 'ja' : 'nein') : 'unbekannt' },
                ...Object.keys(checkTitles).map((k) => ({
                  key: k, label: checkTitles[k],
                  children: latest.notRequired ? '–' : latest.checks[k] ? <Tag color={results[latest.checks[k]].color} bordered={false}>{results[latest.checks[k]].text}</Tag> : '–',
                })),
              ]} />
              <div className="section-title">Bemerkungen</div>
              <div className="remark-box">
                <div className="fact-label">Bemerkungen / Mängel</div>
                <Typography.Paragraph className="pre" style={{ margin: 0 }}>{latest.remarks ?? '–'}</Typography.Paragraph>
              </div>
              {initial.length > 1 && (
                <>
                  <div className="section-title">Frühere Begehungen</div>
                  <List size="small" dataSource={initial.slice(1)} renderItem={(i) => (
                    <List.Item>
                      <Space><span className="tabular">{formatDateTime(i.performedAt)}</span>{name(i.performedByUserId)}<Tag bordered={false} color={inspectionResult(i).color}>{inspectionResult(i).text}</Tag></Space>
                    </List.Item>
                  )} />
                </>
              )}
            </>
          )}
      </Card>

      <Card size="small" title={`Messwerte – CM-Messungen (${cm.length})`} className="protocol">
        <Table<Inspection> size="small" rowKey="id" pagination={false} dataSource={cm} loading={inspections.isLoading}
          locale={{ emptyText: 'Noch keine CM-Messung.' }}
          columns={[
            { title: 'Datum', dataIndex: 'performedAt', width: 160, render: (v: string) => <span className="tabular">{formatDateTime(v)}</span> },
            { title: 'Messstelle', dataIndex: 'location', render: (v: string | null) => v ?? '–' },
            { title: 'Estrich', dataIndex: 'binder', width: 210, render: (v: string | null, i) => `${v ? binderNames[v] ?? v : '–'}${i.heated ? ', beheizt' : ''}` },
            { title: 'Dicke', dataIndex: 'thicknessMm', width: 80, align: 'right', render: (v: number | null) => (v === null ? '–' : `${v} mm`) },
            { title: 'CM / Feuchte', key: 'value', width: 190, align: 'right', render: (_, i) => <span className="tabular">{cmNumber(i.measuredCmPercent)} CM-% (Grenzwert {cmNumber(i.limitCmPercent)})</span> },
            { title: 'Ergebnis', dataIndex: 'isReadyForCovering', width: 170, render: (v: boolean | null) => <Tag color={v ? 'success' : 'warning'} bordered={false}>{v ? 'Belegreif' : 'Noch nicht belegreif'}</Tag> },
            { title: 'Prüfer', dataIndex: 'performedByUserId', width: 150, render: (v: string) => name(v) },
            { title: 'Bemerkung', dataIndex: 'remarks', render: (v: string | null, i) => <>{v ?? '–'}{i.correctsInspectionId && <Typography.Text type="secondary"> (Korrektur)</Typography.Text>}</> },
          ]} />
      </Card>
    </Space>
  );
}
