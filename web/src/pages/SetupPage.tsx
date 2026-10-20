import { Alert, Button, Divider, Form, Input } from 'antd';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { post, problemMessage } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { AuthCard } from './AuthCard';

interface SetupForm {
  setupCode: string;
  companyName: string;
  displayName: string;
  userName: string;
  email?: string;
  password: string;
}

/** Ersteinrichtung mit dem Einmalcode aus dem Server-Protokoll (docs/reengineering/17, „Erster Administrator“). */
export default function SetupPage() {
  const { signIn } = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (v: SetupForm) => {
    setBusy(true);
    setError(null);
    try {
      await post('setup', {
        setupCode: v.setupCode.trim(), companyName: v.companyName,
        administrator: { displayName: v.displayName, userName: v.userName, email: v.email || null, password: v.password },
      });
      await signIn(v.userName, v.password);
      navigate('/', { replace: true });
    } catch (e) {
      setError(problemMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthCard title="EstrichONE einrichten" subtitle="Den Einrichtungscode zeigt der Server beim ersten Start in seinem Protokoll an (docker compose logs api).">
      <Form layout="vertical" onFinish={submit} requiredMark={false}>
        <Form.Item label="Einrichtungscode" name="setupCode" rules={[{ required: true }]}><Input autoComplete="off" /></Form.Item>
        <Form.Item label="Firmenname" name="companyName" rules={[{ required: true }]}><Input /></Form.Item>
        <Divider orientation="left" plain>Erster Administrator</Divider>
        <Form.Item label="Name" name="displayName" rules={[{ required: true }]}><Input autoComplete="name" /></Form.Item>
        <Form.Item label="Anmeldename" name="userName" rules={[{ required: true }]}><Input autoComplete="username" /></Form.Item>
        <Form.Item label="E-Mail (optional)" name="email"><Input type="email" autoComplete="email" /></Form.Item>
        <Form.Item label="Passwort" name="password" extra="Mindestens 10 Zeichen." rules={[{ required: true }]}>
          <Input.Password autoComplete="new-password" />
        </Form.Item>
        {error && <Alert type="error" showIcon message={error} role="alert" style={{ marginBottom: 16 }} />}
        <Button type="primary" htmlType="submit" size="large" block loading={busy}>Einrichten und anmelden</Button>
      </Form>
    </AuthCard>
  );
}
