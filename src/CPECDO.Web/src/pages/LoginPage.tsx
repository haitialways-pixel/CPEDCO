import { FormEvent, useState } from "react";
import { useTranslation } from "react-i18next";
import { Navigate } from "react-router-dom";
import { InstitutionHeader } from "../components/InstitutionHeader";
import { LanguageSwitch } from "../components/LanguageSwitch";
import { useAuth } from "../auth/AuthContext";

export function LoginPage() {
  const { t } = useTranslation();
  const { session, login } = useAuth();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (session) return <Navigate to="/" replace />;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await login(username.trim(), password);
    } catch (err) {
      const message = err instanceof Error ? err.message : t("login.error");
      setError(message.includes("Failed to fetch") ? t("login.network") : message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login-page">
      <div className="login-page__lang">
        <LanguageSwitch />
      </div>
      <div className="login-sheet">
        <InstitutionHeader />
        <form className="login-form" onSubmit={(e) => void onSubmit(e)}>
          <h1>{t("login.title")}</h1>
          <p className="login-form__subtitle">{t("login.subtitle")}</p>
          {error ? (
            <p className="login-form__error" role="alert">
              {error}
            </p>
          ) : null}
          <label>
            {t("login.username")}
            <input
              autoComplete="username"
              autoFocus
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              required
            />
          </label>
          <label>
            {t("login.password")}
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </label>
          <button type="submit" disabled={busy}>
            {busy ? t("login.busy") : t("login.submit")}
          </button>
          <p className="login-form__demo">
            {t("login.demo")}
            <br />
            <code>admin</code> / <code>Admin@Cpcredo2026</code>
            <br />
            <code>gerant</code> / <code>Gerant@Cpcredo2026</code>
            <br />
            <code>caissier</code> / <code>Caissier@Cpcredo2026</code>
          </p>
        </form>
      </div>
    </div>
  );
}
