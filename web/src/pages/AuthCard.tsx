import { Card, Typography } from 'antd';
import type { ReactNode } from 'react';
import { brand } from '../theme';

/** Rahmen für Anmeldung und Einrichtung: Markenfläche links, Formular rechts. */
export function AuthCard({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  return (
    <div style={{ minHeight: '100vh', display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(360px, 480px)', background: '#F0F2F5' }}>
      <div style={{ background: `linear-gradient(135deg, ${brand.navy}, ${brand.navyLight})`, color: '#fff', padding: '48px 56px', display: 'flex', flexDirection: 'column', justifyContent: 'space-between' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, fontSize: 22, fontWeight: 700 }}>
          <svg width="28" height="28" viewBox="0 0 22 22" aria-hidden="true">
            <rect x="2" y="4" width="18" height="3.5" rx="1" fill="#fff" />
            <rect x="2" y="9.5" width="13" height="3.5" rx="1" fill="#fff" />
            <rect x="2" y="15" width="18" height="3.5" rx="1" fill={brand.blue} />
          </svg>
          EstrichONE
        </div>
        <div>
          <Typography.Title level={2} style={{ color: '#fff', marginBottom: 8 }}>Die Software für Estrichbetriebe</Typography.Title>
          <Typography.Paragraph style={{ color: '#dbe5f3', fontSize: 16, maxWidth: 520 }}>
            Bauakten, Planung, Baustellendokumentation und IT – im Büro, auf der Baustelle und unterwegs.
          </Typography.Paragraph>
        </div>
        <Typography.Text style={{ color: '#9fb3cf' }}>Selbst betrieben. Ihre Daten bleiben auf Ihrem Server.</Typography.Text>
      </div>
      <div style={{ display: 'grid', placeItems: 'center', padding: 24 }}>
        <Card style={{ width: '100%', maxWidth: 420 }} variant="outlined">
          <Typography.Title level={3} style={{ marginTop: 0, color: brand.navy }}>{title}</Typography.Title>
          {subtitle && <Typography.Paragraph type="secondary">{subtitle}</Typography.Paragraph>}
          {children}
        </Card>
      </div>
    </div>
  );
}
