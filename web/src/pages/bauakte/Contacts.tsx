import { EditOutlined, MailOutlined, PhoneOutlined, UserAddOutlined } from '@ant-design/icons';
import { ProForm, ProFormText, ProFormTextArea } from '@ant-design/pro-components';
import { Avatar, Button, Card, Col, Descriptions, Empty, List, Row, Space, Tag, Typography } from 'antd';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import type { BauakteDetail, BauakteMember, ContactResponse } from '../../api/models';
import type { BauakteChanges } from './BauaktePage';

const dash = (value: string | null | undefined) => value || '–';
const phoneLink = (value: string | null | undefined) => value ? <a href={`tel:${value}`}><PhoneOutlined /> {value}</a> : '–';
const mailLink = (value: string | null | undefined) => value ? <a href={`mailto:${value}`}><MailOutlined /> {value}</a> : '–';

/** Ansprechpartner und Kontakte: Kunde (Stammdaten), Baustellenkontakt vor Ort, zugewiesene Mitarbeiter. */
export function BauakteContacts({ bauakte: b, customer, members, canEdit, onSave, onAssign }: {
  bauakte: BauakteDetail;
  customer: ContactResponse | null;
  members: BauakteMember[];
  canEdit: boolean;
  onSave: (changes: BauakteChanges) => Promise<boolean>;
  onAssign: () => void;
}) {
  const [editing, setEditing] = useState(false);

  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} xl={12}>
        <Card size="small" title="Kunde" extra={customer && <Link to="/kunden">Stammdaten</Link>} style={{ height: '100%' }}>
          {customer
            ? (
              <Descriptions column={{ xs: 1, md: 2 }} size="small" layout="vertical" colon={false} className="field-grid" items={[
                { key: 'name', label: 'Name', children: customer.name, span: 2 },
                { key: 'nr', label: 'Kundennummer', children: dash(customer.customerNumber) },
                { key: 'kind', label: 'Art', children: customer.kind === 'company' ? 'Firma' : 'Person' },
                { key: 'phone', label: 'Telefon', children: phoneLink(customer.phone) },
                { key: 'mail', label: 'E-Mail', children: mailLink(customer.email) },
                { key: 'street', label: 'Adresse', children: dash(customer.street), span: 2 },
                { key: 'zip', label: 'PLZ', children: dash(customer.postalCode) },
                { key: 'city', label: 'Ort', children: dash(customer.city) },
              ]} />
            )
            : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={b.customerId ? 'Kunde wird geladen …' : 'Noch kein Kunde zugeordnet. Über „Bearbeiten“ zuordnen oder neu anlegen.'} />}
        </Card>
      </Col>
      <Col xs={24} xl={12}>
        <Card size="small" title="Baustellenkontakt" style={{ height: '100%' }}
          extra={canEdit && !editing && <Button size="small" icon={<EditOutlined />} onClick={() => setEditing(true)}>Bearbeiten</Button>}>
          {editing
            ? (
              <ProForm
                layout="vertical"
                initialValues={{ siteContactName: b.siteContactName ?? '', siteContactPhone: b.siteContactPhone ?? '', siteContactEmail: b.siteContactEmail ?? '', siteNotes: b.siteNotes ?? '' }}
                submitter={{ searchConfig: { submitText: 'Speichern', resetText: 'Abbrechen' }, onReset: () => setEditing(false) }}
                onFinish={async (v: { siteContactName?: string; siteContactPhone?: string; siteContactEmail?: string; siteNotes?: string }) => {
                  const ok = await onSave({
                    siteContactName: v.siteContactName || null, siteContactPhone: v.siteContactPhone || null,
                    siteContactEmail: v.siteContactEmail || null, siteNotes: v.siteNotes || null,
                  });
                  if (ok) setEditing(false);
                  return ok;
                }}
              >
                <ProFormText name="siteContactName" label="Ansprechpartner vor Ort" placeholder="z. B. Herr Bauer (Bauleitung GU)" />
                <Space.Compact block>
                  <ProFormText name="siteContactPhone" label="Telefon" width="sm" />
                  <ProFormText name="siteContactEmail" label="E-Mail Baustellenkontakt" width="md" rules={[{ type: 'email', message: 'Bitte eine gültige E-Mail-Adresse angeben.' }]} />
                </Space.Compact>
                <ProFormTextArea name="siteNotes" label="Hinweise / Behinderungen" fieldProps={{ rows: 3, maxLength: 2000, showCount: true }} />
              </ProForm>
            )
            : (
              <Descriptions column={{ xs: 1, md: 2 }} size="small" layout="vertical" colon={false} className="field-grid" items={[
                { key: 'name', label: 'Ansprechpartner vor Ort', children: dash(b.siteContactName) },
                { key: 'phone', label: 'Telefon', children: phoneLink(b.siteContactPhone) },
                { key: 'mail', label: 'E-Mail Baustellenkontakt', children: mailLink(b.siteContactEmail), span: 2 },
                { key: 'street', label: 'Adresse', children: dash(b.street), span: 2 },
                { key: 'zip', label: 'PLZ', children: dash(b.postalCode) },
                { key: 'city', label: 'Ort', children: dash(b.city) },
                { key: 'notes', label: 'Hinweise', span: 2, children: b.siteNotes ? <Typography.Text className="pre">{b.siteNotes}</Typography.Text> : '–' },
              ]} />
            )}
        </Card>
      </Col>
      <Col xs={24} xl={12}>
        <Card size="small" title="Zugewiesene Mitarbeiter" extra={canEdit && <Button size="small" icon={<UserAddOutlined />} onClick={onAssign}>Mitarbeiter zuweisen</Button>}>
          <List
            dataSource={members}
            locale={{ emptyText: 'Noch niemand zugewiesen.' }}
            renderItem={(m) => (
              <List.Item>
                <List.Item.Meta
                  avatar={<Avatar style={{ background: '#0B2D5B' }}>{m.displayName.split(' ').map((p) => p[0]).slice(0, 2).join('')}</Avatar>}
                  title={<Space>{m.displayName}{m.role === 'responsible' && <Tag color="blue" bordered={false}>Zuständig</Tag>}{!m.isActive && <Tag bordered={false}>Gesperrt</Tag>}</Space>}
                  description={m.email ? mailLink(m.email) : 'Mitarbeiter'}
                />
              </List.Item>
            )}
          />
        </Card>
      </Col>
    </Row>
  );
}
