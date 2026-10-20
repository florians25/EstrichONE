import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Form, Input } from 'antd';
import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { get, problemMessage } from '../api/client';
import type { ServerInfo } from '../api/models';
import { useAuth } from '../auth/AuthContext';
import { AuthCard } from './AuthCard';

export default function LoginPage() {
  const { signIn } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const info = useQuery({ queryKey: ['server-info'], queryFn: () => get<ServerInfo>('server-info') });
  const reason = params.get('grund');

  const submit = async (values: { login: string; password: string }) => {
    setBusy(true);
    setError(null);
    try {
      await signIn(values.login, values.password);
      navigate(params.get('ziel') ?? '/', { replace: true });
    } catch (e) {
      setError(problemMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthCard title="Anmelden" subtitle={info.data?.serverName}>
      {reason === 'abgelaufen' && <Alert type="info" showIcon message="Die Sitzung ist abgelaufen. Bitte erneut anmelden." style={{ marginBottom: 16 }} />}
      {reason === 'gesperrt' && <Alert type="error" showIcon message="Das Benutzerkonto ist gesperrt. Bitte an die Administration wenden." style={{ marginBottom: 16 }} />}
      {info.data?.setupRequired && (
        <Alert type="warning" showIcon style={{ marginBottom: 16 }}
          message={<>EstrichONE ist noch nicht eingerichtet. <Link to="/einrichtung">Jetzt einrichten</Link></>} />
      )}
      <Form layout="vertical" onFinish={submit} requiredMark={false}>
        <Form.Item label="Anmeldename oder E-Mail" name="login" rules={[{ required: true, message: 'Bitte den Anmeldenamen eingeben.' }]}>
          <Input autoComplete="username" size="large" autoFocus />
        </Form.Item>
        <Form.Item label="Passwort" name="password" rules={[{ required: true, message: 'Bitte das Passwort eingeben.' }]}>
          <Input.Password autoComplete="current-password" size="large" />
        </Form.Item>
        {error && <Alert type="error" showIcon message={error} role="alert" style={{ marginBottom: 16 }} />}
        <Button type="primary" htmlType="submit" size="large" block loading={busy}>Anmelden</Button>
      </Form>
    </AuthCard>
  );
}
