import { PageContainer, ProForm, ProFormText } from '@ant-design/pro-components';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Card, Space, Typography } from 'antd';
import { get, problemMessage, put } from '../api/client';
import type { CompanyResponse } from '../api/models';

/** Firmendaten für den Briefkopf (Bestellschein, Bauaktenblatt). */
export default function CompanyPage() {
  const { message } = App.useApp();
  const queries = useQueryClient();
  const company = useQuery({ queryKey: ['company'], queryFn: () => get<CompanyResponse>('company') });

  return (
    <PageContainer title={<Space direction="vertical" size={0}><Typography.Text type="secondary" className="overline">System</Typography.Text>Firmendaten</Space>}>
      <Card loading={company.isLoading} style={{ maxWidth: 720 }}>
        {company.data && (
          <ProForm<Omit<CompanyResponse, 'version'>>
            initialValues={company.data}
            submitter={{ searchConfig: { submitText: 'Speichern' }, resetButtonProps: false }}
            onFinish={async (v) => {
              try {
                const saved = await put<CompanyResponse>('company', { ...v, email: v.email || null });
                queries.setQueryData(['company'], saved);
                message.success('Firmendaten gespeichert');
                return true;
              } catch (e) {
                message.error(problemMessage(e));
                return false;
              }
            }}
          >
            <Typography.Paragraph type="secondary">Erscheinen im Briefkopf von Bestellscheinen und Ausdrucken.</Typography.Paragraph>
            <ProFormText name="name" label="Firmenname" rules={[{ required: true, whitespace: true, message: 'Bitte den Firmennamen angeben.' }]} />
            <ProFormText name="street" label="Straße" />
            <Space.Compact block>
              <ProFormText name="postalCode" label="PLZ" width="xs" />
              <ProFormText name="city" label="Ort" width="md" />
            </Space.Compact>
            <Space.Compact block>
              <ProFormText name="phone" label="Telefon" width="sm" />
              <ProFormText name="email" label="E-Mail" width="md" rules={[{ type: 'email', message: 'Bitte eine gültige E-Mail-Adresse angeben.' }]} />
            </Space.Compact>
            <ProFormText name="website" label="Website" />
          </ProForm>
        )}
      </Card>
    </PageContainer>
  );
}
