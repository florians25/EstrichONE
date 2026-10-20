import { PageContainer } from '@ant-design/pro-components';
import { Alert, Button, Card, Form, Input } from 'antd';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { problemMessage } from '../api/client';
import { useAuth } from '../auth/AuthContext';

export default function ChangePasswordPage() {
  const { changePassword, mustChangePassword } = useAuth();
  const navigate = useNavigate();
  const [form] = Form.useForm();
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);
  const forced = mustChangePassword;

  const submit = async (v: { current: string; next: string }) => {
    setBusy(true);
    setError(null);
    try {
      await changePassword(v.current, v.next);
      form.resetFields();
      if (forced) {
        navigate('/', { replace: true });
      } else {
        setDone(true);
      }
    } catch (e) {
      setError(problemMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <PageContainer title="Passwort ändern">
      <Card style={{ maxWidth: 520 }}>
        {forced && <Alert type="info" showIcon style={{ marginBottom: 16 }} message="Sie haben ein Einmalpasswort erhalten. Bitte legen Sie jetzt ein eigenes Passwort fest." />}
        {done && <Alert type="success" showIcon style={{ marginBottom: 16 }} message="Das Passwort wurde geändert. Andere Sitzungen wurden beendet." />}
        <Form form={form} layout="vertical" onFinish={submit} requiredMark={false}>
          <Form.Item label="Bisheriges Passwort" name="current" rules={[{ required: true }]}><Input.Password autoComplete="current-password" /></Form.Item>
          <Form.Item label="Neues Passwort" name="next" extra="Mindestens 10 Zeichen; eine lange Passphrase ist am sichersten." rules={[{ required: true }]}>
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Form.Item label="Neues Passwort wiederholen" name="repeat" dependencies={['next']}
            rules={[{ required: true }, ({ getFieldValue }) => ({ validator: (_, value) => value === getFieldValue('next') ? Promise.resolve() : Promise.reject(new Error('Die Passwörter stimmen nicht überein.')) })]}>
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          {error && <Alert type="error" showIcon message={error} role="alert" style={{ marginBottom: 16 }} />}
          <Button type="primary" htmlType="submit" loading={busy}>Passwort ändern</Button>
        </Form>
      </Card>
    </PageContainer>
  );
}
