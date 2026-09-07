import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { changePassword as changePasswordApi, fetchMe, getToken, login as loginApi, setToken } from "../api/client";
import type { Session } from "../api/types";

type AuthState = {
  session: Session | null;
  ready: boolean;
  login: (username: string, password: string) => Promise<void>;
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
  logout: () => void;
};

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      setReady(true);
      return;
    }
    void fetchMe(token)
      .then((me) => {
        setSession({
          token,
          user: me.user,
          institution: me.institution,
          branch: me.branch,
          roles: me.roles
        });
      })
      .catch(() => setToken(null))
      .finally(() => setReady(true));
  }, []);

  const login = useCallback(async (username: string, password: string) => {
    const response = await loginApi(username, password);
    setToken(response.accessToken);
    setSession({
      token: response.accessToken,
      user: response.user,
      institution: response.institution,
      branch: response.branch,
      roles: response.roles
    });
  }, []);

  const logout = useCallback(() => {
    setToken(null);
    setSession(null);
  }, []);

  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    if (!session) return;
    await changePasswordApi(session.token, currentPassword, newPassword);
    setToken(null);
    setSession(null);
  }, [session]);

  const value = useMemo(() => ({ session, ready, login, changePassword, logout }), [session, ready, login, changePassword, logout]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
