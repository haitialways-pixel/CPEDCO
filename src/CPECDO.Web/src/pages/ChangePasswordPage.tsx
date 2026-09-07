import { FormEvent, useState } from "react";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { InstitutionHeader } from "../components/InstitutionHeader";
import { LanguageSwitch } from "../components/LanguageSwitch";

export function ChangePasswordPage() {
  const { t } = useTranslation();
  const { session, changePassword, logout } = useAuth();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (!session) return null;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await changePassword(currentPassword, newPassword);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("password.error"));
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
        <form className="login-form" onSubmit={(event) => void submit(event)}>
          <h1>{t("password.title")}</h1>
          <p className="login-form__subtitle">{t("password.subtitle")}</p>
          {error ? (
            <p className="login-form__error" role="alert">
              {error}
            </p>
          ) : null}
          <label>
            {t("password.current")}
            <input
              type="password"
              autoComplete="current-password"
              value={currentPassword}
              onChange={(event) => setCurrentPassword(event.target.value)}
              required
            />
          </label>
          <label>
            {t("password.new")}
            <input
              type="password"
              autoComplete="new-password"
              minLength={8}
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              required
            />
          </label>
          <button type="submit" disabled={busy}>
            {busy ? t("password.busy") : t("password.submit")}
          </button>
          <button type="button" className="btn-ghost" onClick={logout}>
            {t("password.signOut")}
          </button>
        </form>
      </div>
    </div>
  );
}
