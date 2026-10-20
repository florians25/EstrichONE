import { useQuery } from '@tanstack/react-query';
import { Card, Empty, List, Space, Table, Tag, Typography } from 'antd';
import type { DiaryEntry, DirectoryUser, Inspection } from '../../api/models';
import { get } from '../../api/client';
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

export const cmNumber = (value: number | null) =>
  value === null ? '–' : value.toLocaleString('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 2 });

export function weatherLine(entry: Pick<DiaryEntry, 'weather' | 'temperatureCelsius' | 'workersPresent'>): string {
  return [
    entry.weather ? weatherNames[entry.weather] : null,
    entry.temperatureCelsius !== null ? `${entry.temperatureCelsius} °C` : null,
    entry.workersPresent !== null ? `${entry.workersPresent} anwesend` : null,
  ].filter(Boolean).join(' · ');
}

/** Prüfungen (Erstbegehung, CM-Messung) und Bautagebuch – erfasst in den Apps, im Büro zur Ansicht. */
export function BauakteRecords({ bauakteId, people }: { bauakteId: string; people: DirectoryUser[] }) {
  const inspections = useQuery({ queryKey: ['inspections', bauakteId], queryFn: () => get<Inspection[]>(`bauakten/${bauakteId}/inspections`) });
  const diary = useQuery({ queryKey: ['diary', bauakteId], queryFn: () => get<DiaryEntry[]>(`bauakten/${bauakteId}/diary`) });
  const name = (id: string) => people.find((p) => p.id === id)?.displayName ?? '–';

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Card size="small" title={`Prüfungen (${inspections.data?.length ?? 0})`}>
        <Table<Inspection> size="small" rowKey="id" pagination={false} dataSource={inspections.data ?? []} loading={inspections.isLoading}
          locale={{ emptyText: 'Noch keine Erstbegehung oder CM-Messung.' }}
          columns={[
            {
              title: 'Art', key: 'kind', width: 170, render: (_, i) => (
                <>{i.kind === 'cmMeasurement' ? 'CM-Messung' : 'Erstbegehung'}{i.correctsInspectionId && <Typography.Text type="secondary"> (Korrektur)</Typography.Text>}</>
              ),
            },
            {
              title: 'Ergebnis', key: 'result', render: (_, i) => {
                if (i.kind === 'cmMeasurement') {
                  return (
                    <Space wrap size={8}>
                      <span className="tabular">{i.location}: {cmNumber(i.measuredCmPercent)} CM-% (Grenzwert {cmNumber(i.limitCmPercent)})</span>
                      <Tag color={i.isReadyForCovering ? 'success' : 'warning'} bordered={false}>{i.isReadyForCovering ? 'Belegreif' : 'Noch nicht belegreif'}</Tag>
                    </Space>
                  );
                }
                if (i.notRequired) {
                  return 'Nicht notwendig';
                }
                return (
                  <Space direction="vertical" size={2}>
                    {Object.keys(checkTitles).filter((k) => i.checks[k]).map((k) => (
                      <span key={k}><Tag color={results[i.checks[k]]?.color} bordered={false}>{results[i.checks[k]]?.text ?? i.checks[k]}</Tag>{checkTitles[k]}</span>
                    ))}
                  </Space>
                );
              },
            },
            { title: 'Bemerkung', dataIndex: 'remarks', render: (v: string | null) => v ?? '–' },
            { title: 'Durch', dataIndex: 'performedByUserId', width: 160, render: name },
            { title: 'Zeitpunkt', dataIndex: 'performedAt', width: 160, render: (v: string) => <span className="tabular">{formatDateTime(v)}</span> },
          ]} />
      </Card>
      <Card size="small" title={`Bautagebuch (${diary.data?.length ?? 0})`} loading={diary.isLoading}>
        <List
          dataSource={diary.data ?? []}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Noch keine Einträge." /> }}
          renderItem={(e) => (
            <List.Item>
              <List.Item.Meta
                title={
                  <Space split="·" wrap>
                    <span>{parseDay(e.day).toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' })}</span>
                    {weatherLine(e) && <Typography.Text type="secondary">{weatherLine(e)}</Typography.Text>}
                  </Space>
                }
                description={
                  <>
                    <Typography.Paragraph className="pre" style={{ margin: '4px 0', color: 'inherit' }}>{e.text}</Typography.Paragraph>
                    <Typography.Text type="secondary">{name(e.authorUserId)}{e.editedAt && ' · bearbeitet'}</Typography.Text>
                  </>
                }
              />
            </List.Item>
          )}
        />
      </Card>
    </Space>
  );
}
