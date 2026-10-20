import { Spin } from 'antd';
import { lazy, Suspense, type ReactNode } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import { AppLayout } from './layout/AppLayout';

const LoginPage = lazy(() => import('./pages/LoginPage'));
const SetupPage = lazy(() => import('./pages/SetupPage'));
const ChangePasswordPage = lazy(() => import('./pages/ChangePasswordPage'));
const StartPage = lazy(() => import('./pages/StartPage'));
const BauaktenPage = lazy(() => import('./pages/BauaktenPage'));
const BauaktePage = lazy(() => import('./pages/bauakte/BauaktePage'));
const CustomersPage = lazy(() => import('./pages/CustomersPage'));
const PlanningPage = lazy(() => import('./pages/PlanningPage'));
const ResourcesPage = lazy(() => import('./pages/ResourcesPage'));
const UsersPage = lazy(() => import('./pages/UsersPage'));
const DevicesPage = lazy(() => import('./pages/DevicesPage'));
const HelpdeskPage = lazy(() => import('./pages/HelpdeskPage'));
const TicketPage = lazy(() => import('./pages/TicketPage'));
const BackupPage = lazy(() => import('./pages/BackupPage'));

const loading = <div style={{ display: 'grid', placeItems: 'center', height: '100vh' }}><Spin size="large" /></div>;

/** Nur angemeldet; nach Einmalpasswort zuerst das eigene Passwort. */
function SignedIn({ children }: { children: ReactNode }) {
  const { user, ready, mustChangePassword } = useAuth();
  const location = useLocation();
  if (!ready) {
    return loading;
  }
  if (!user) {
    return <Navigate to={`/anmelden?ziel=${encodeURIComponent(location.pathname + location.search)}`} replace />;
  }
  if (mustChangePassword && location.pathname !== '/konto/passwort') {
    return <Navigate to="/konto/passwort" replace />;
  }
  return <>{children}</>;
}

function Permitted({ permission, children }: { permission: string; children: ReactNode }) {
  const { has } = useAuth();
  return has(permission) ? <>{children}</> : <Navigate to="/start" replace />;
}

export function App() {
  return (
    <Suspense fallback={loading}>
      <Routes>
        <Route path="/anmelden" element={<LoginPage />} />
        <Route path="/einrichtung" element={<SetupPage />} />
        <Route element={<SignedIn><AppLayout /></SignedIn>}>
          <Route index element={<Navigate to="/start" replace />} />
          <Route path="start" element={<StartPage />} />
          <Route path="konto/passwort" element={<ChangePasswordPage />} />
          <Route path="bauakten" element={<Permitted permission="bauakten.read"><BauaktenPage /></Permitted>} />
          <Route path="bauakten/:id" element={<Permitted permission="bauakten.read"><BauaktePage /></Permitted>} />
          <Route path="kunden" element={<Permitted permission="contacts.read"><CustomersPage /></Permitted>} />
          <Route path="planung" element={<Permitted permission="planning.read"><PlanningPage /></Permitted>} />
          <Route path="planung/ressourcen" element={<Permitted permission="planning.read"><ResourcesPage /></Permitted>} />
          <Route path="verwaltung/benutzer" element={<Permitted permission="users.manage"><UsersPage /></Permitted>} />
          <Route path="it-center/geraete" element={<Permitted permission="devices.read"><DevicesPage /></Permitted>} />
          <Route path="it-center/helpdesk" element={<Permitted permission="helpdesk.agent"><HelpdeskPage /></Permitted>} />
          <Route path="it-center/helpdesk/:id" element={<Permitted permission="helpdesk.agent"><TicketPage /></Permitted>} />
          <Route path="system/sicherung" element={<Permitted permission="backup.manage"><BackupPage /></Permitted>} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Suspense>
  );
}
