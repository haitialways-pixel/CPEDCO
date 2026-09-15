import { FormEvent, useState } from "react";
import { Navigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { InstitutionHeader } from "../components/InstitutionHeader";
import { LanguageSwitch } from "../components/LanguageSwitch";
import { useAuth } from "../auth/AuthContext";

export function MfaPage() {
  const { t } = useTranslation();
  const { session, mfaChallenge, verifyMfa, logout } = useAuth();
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (session) return <Navigate to="/" replace />;
  if (!mfaChallenge) return <Navigate to="/login" replace />;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await verifyMfa(code.trim());
    } catch (err) {
      setError(err instanceof Error ? err.message : t("mfa.error"));
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
        <form className="login-form" onSubmit={(e) => void submit(e)}>
          <h1>{t("mfa.title")}</h1>
          <p className="login-form__subtitle">
            {mfaChallenge.setupRequired ? t("mfa.setupHint") : t("mfa.codeHint")}
          </p>
          {mfaChallenge.setupRequired && mfaChallenge.setup ? (
            <div className="mfa-setup">
              <img src={mfaChallenge.setup.qrPngDataUrl} alt={t("mfa.qrAlt")} width={192} height={192} />
              <p className="mfa-setup__key">
                {t("mfa.manualKey")}
                <br />
                <code>{mfaChallenge.setup.manualKey}</code>
              </p>
            </div>
          ) : null}
          {error ? (
            <p className="login-form__error" role="alert">
              {error}
            </p>
          ) : null}
          <label>
            {t("mfa.code")}
            <input
              inputMode="numeric"
              autoComplete="one-time-code"
              pattern="[0-9]{6}"
              maxLength={6}
              value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
              required
              autoFocus
            />
          </label>
          <button type="submit" disabled={busy || code.length !== 6}>
            {busy ? t("mfa.busy") : t("mfa.submit")}
          </button>
          <button type="button" className="btn-ghost" onClick={logout}>
            {t("password.signOut")}
          </button>
        </form>
      </div>
    </div>
  );
}
