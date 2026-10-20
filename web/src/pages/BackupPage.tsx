import { CloudUploadOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useQuery } from '@tanstack/react-query';
import { Alert, App, Button, Card, Descriptions, Space, Tag, Typography } from 'antd';
import { get, post, problemMessage } from '../api/client';
import type { BackupRunInfo, BackupStatusResponse } from '../api/models';
import { formatDateTime } from '../shared/status';

function RunInfo({ info }: { info: BackupRunInfo | null }) {
  if (!info) {
    return <Tag bordered={false}>Noch nie</Tag>;
  }
  return (
    <Space wrap>
      <Tag color={info.status === 'succeeded' ? 'success' : 'error'} bordered={false}>{info.status === 'succeeded' ? 'Erfolgreich' : 'Fehlgeschlagen'}</Tag>
      <span className="tabular">{formatDateTime(info.at)}</span>
      <Typography.Text type="secondary">{info.message}</Typography.Text>
    </Space>
  );
}

/** Backup Center: Zustand der Sicherungen, Sicherung jetzt anstoßen. */
export default function BackupPage() {
  const { message } = App.useApp();
  const status = useQuery({ queryKey: ['backup'], queryFn: () => get<BackupStatusResponse>('backup/status'), refetchInterval: 15_000 });
  const s = status.data;

  const runNow = async () => {
    try {
      await post('backup/run');
      message.success('Sicherung angefordert');
      void status.refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <PageContainer
      title="Sicherung"
      subTitle="Datenbank und Dateien, verschlüsselt mit Prüfsummen"
      extra={s && <Button type="primary" icon={<CloudUploadOutlined />} onClick={runNow} disabled={s.runRequested || !s.configured}>
        {s.runRequested ? 'Sicherung angefordert …' : 'Jetzt sichern'}
      </Button>}
    >
      {status.error && <Alert type="error" showIcon message={problemMessage(status.error)} />}
      {s && (
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          <Alert role="status" type={s.healthy ? 'success' : 'error'} showIcon message={s.summary} />
          <Card size="small" title="Verlauf">
            <Descriptions column={1} bordered size="small" labelStyle={{ width: 240 }}
              items={[
                { key: 'run', label: 'Letzte Sicherung', children: <RunInfo info={s.lastRun} /> },
                { key: 'success', label: 'Letzte erfolgreiche', children: <RunInfo info={s.lastSuccess} /> },
                { key: 'check', label: 'Integritätsprüfung', children: <RunInfo info={s.lastCheck} /> },
                { key: 'restore', label: 'Wiederherstellungstest', children: <RunInfo info={s.lastRestoreTest} /> },
              ]} />
          </Card>
        </Space>
      )}
    </PageContainer>
  );
}
