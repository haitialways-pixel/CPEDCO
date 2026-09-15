import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import {
  changePassword as changePasswordApi,
  fetchMe,
  getToken,
  login as loginApi,
  logout as logoutApi,
  setToken,
  verifyMfa as verifyMfaApi
} from "../api/client";
import type { LoginResponse, MfaSetup, Session } from "../api/types";

export type MfaChallengeState = {
  ticket: string;
  setupRequired: boolean;
  setup: MfaSetup | null;
};

const IDLE_MS = 12 * 60 * 1000;

type AuthState = {
  session: Session | null;
  mfaChallenge: MfaChallengeState | null;
  ready: boolean;
  login: (username: string, password: string) => Promise<void>;
  verifyMfa: (code: string) => Promise<void>;
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
  logout: () => void;
};

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [mfaChallenge, setMfaChallenge] = useState<MfaChallengeState | null>(null);
  const [ready, setReady] = useState(false);
  const sessionRef = useRef<Session | null>(null);
  sessionRef.current = session;

  const clearSession = useCallback(() => {
    setToken(null);
    setSession(null);
    setMfaChallenge(null);
  }, []);

  const applyLogin = useCallback((response: LoginResponse) => {
    if (response.mfaRequired && response.mfaTicket) {
      setMfaChallenge({
        ticket: response.mfaTicket,
        setupRequired: Boolean(response.mfaSetupRequired),
        setup: response.mfaSetup ?? null
      });
      return;
    }
    if (!response.accessToken || !response.user || !response.institution || !response.branch || !response.roles) {
      throw new Error("Réponse de connexion incomplète.");
    }
    setMfaChallenge(null);
    setToken(response.accessToken);
    setSession({
      token: response.accessToken,
      user: response.user,
      institution: response.institution,
      branch: response.branch,
      roles: response.roles
    });
  }, []);

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

  useEffect(() => {
    const onExpired = () => clearSession();
    window.addEventListener("cpcredo:session-expired", onExpired);
    return () => window.removeEventListener("cpcredo:session-expired", onExpired);
  }, [clearSession]);

  useEffect(() => {
    if (!session) return;
    let timer = window.setTimeout(endIdle, IDLE_MS);

    function endIdle() {
      const current = sessionRef.current;
      if (!current) return;
      void logoutApi(current.token).finally(clearSession);
    }

    function bump() {
      window.clearTimeout(timer);
      timer = window.setTimeout(endIdle, IDLE_MS);
    }

    const opts: AddEventListenerOptions = { capture: true };
    window.addEventListener("keydown", bump, opts);
    window.addEventListener("pointerdown", bump, opts);
    window.addEventListener("touchstart", bump, opts);
    return () => {
      window.clearTimeout(timer);
      window.removeEventListener("keydown", bump, opts);
      window.removeEventListener("pointerdown", bump, opts);
      window.removeEventListener("touchstart", bump, opts);
    };
  }, [session, clearSession]);

  const login = useCallback(async (username: string, password: string) => {
    applyLogin(await loginApi(username, password));
  }, [applyLogin]);

  const verifyMfa = useCallback(async (code: string) => {
    if (!mfaChallenge) return;
    applyLogin(await verifyMfaApi(mfaChallenge.ticket, code));
  }, [applyLogin, mfaChallenge]);

  const logout = useCallback(() => {
    const token = sessionRef.current?.token ?? getToken();
    void logoutApi(token).finally(clearSession);
  }, [clearSession]);

  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    const current = sessionRef.current;
    if (!current) return;
    await changePasswordApi(current.token, currentPassword, newPassword);
    clearSession();
  }, [clearSession]);

  const value = useMemo(
    () => ({ session, mfaChallenge, ready, login, verifyMfa, changePassword, logout }),
    [session, mfaChallenge, ready, login, verifyMfa, changePassword, logout]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
