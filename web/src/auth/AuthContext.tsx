import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, get, onSessionEnded, post } from '../api/client';
import type { UserSummary, WebSessionResponse } from '../api/models';

interface AuthState {
  user: UserSummary | null;
  permissions: ReadonlySet<string>;
  mustChangePassword: boolean;
  ready: boolean;
  has: (permission: string) => boolean;
  signIn: (login: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
  restore: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

/**
 * Anmeldung im Browser: Tokens liegen ausschließlich in HttpOnly-Cookies (docs/reengineering/17).
 * Hier stehen nur der Benutzer und seine Rechte.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const [user, setUser] = useState<UserSummary | null>(null);
  const [permissions, setPermissions] = useState<ReadonlySet<string>>(new Set());
  const [mustChangePassword, setMustChangePassword] = useState(false);
  const [ready, setReady] = useState(false);

  const clear = useCallback(() => {
    setUser(null);
    setPermissions(new Set());
    setMustChangePassword(false);
  }, []);

  const loadPermissions = useCallback(async () => {
    setPermissions(new Set(await get<string[]>('access/me/permissions')));
  }, []);

  const restore = useCallback(async () => {
    try {
      setUser(await api<UserSummary>('auth/me', { quiet: true }));
      await loadPermissions();
    } catch {
      clear();
    } finally {
      setReady(true);
    }
  }, [clear, loadPermissions]);

  useEffect(() => {
    onSessionEnded((reason) => {
      clear();
      navigate(`/anmelden?grund=${reason === 'disabled' ? 'gesperrt' : 'abgelaufen'}`);
    });
    void restore();
  }, [clear, navigate, restore]);

  const value = useMemo<AuthState>(
    () => ({
      user,
      permissions,
      mustChangePassword,
      ready,
      has: (permission) => permissions.has(permission),
      signIn: async (login, password) => {
        const session = await post<WebSessionResponse>('auth/web/login', { login, password });
        setUser(session.user);
        setMustChangePassword(session.mustChangePassword);
        await loadPermissions();
      },
      signOut: async () => {
        try {
          await post('auth/web/logout');
        } finally {
          clear();
        }
      },
      changePassword: async (currentPassword, newPassword) => {
        await post('auth/change-password', { currentPassword, newPassword });
        setMustChangePassword(false);
      },
      restore,
    }),
    [user, permissions, mustChangePassword, ready, loadPermissions, clear, restore],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth außerhalb von AuthProvider');
  }
  return context;
}
