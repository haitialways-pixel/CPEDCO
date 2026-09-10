import { FormEvent, useEffect, useState } from "react";
import { Navigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  fetchBackupFiles,
  fetchBackupSettings,
  restoreBackup,
  runBackup,
  saveBackupSettings,
  type BackupFile,
  type BackupSettings
} from "../api/backup";

export function BackupPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const [settings, setSettings] = useState<BackupSettings>({ folder: "", pgDumpPath: "" });
  const [files, setFiles] = useState<BackupFile[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [restoreName, setRestoreName] = useState<string | null>(null);
  const [restorePassword, setRestorePassword] = useState("");

  const isAdmin = session?.roles.some((r) => r.name === "Admin") ?? false;
  const canBackup = session?.roles.some((r) => r.name === "Admin" || r.name === "Gerant") ?? false;

  async function reload() {
    const [nextSettings, nextFiles] = await Promise.all([fetchBackupSettings(), fetchBackupFiles()]);
    setSettings(nextSettings);
    setFiles(nextFiles);
  }

  useEffect(() => {
    if (!session || !canBackup) return;
    void reload().catch((err) => setError(err instanceof Error ? err.message : t("backup.loadError")));
  }, [session, canBackup, t]);

  if (!session) return null;
  if (!canBackup) return <Navigate to="/" replace />;

  async function save(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setInfo(null);
    setBusy(true);
    try {
      const saved = await saveBackupSettings(settings);
      setSettings(saved);
      setInfo(t("backup.settingsSaved"));
    } catch (err) {
      setError(err instanceof Error ? err.message : t("backup.saveError"));
    } finally {
      setBusy(false);
    }
  }

  async function backupNow() {
    setError(null);
    setInfo(null);
    setBusy(true);
    try {
      const result = await runBackup();
      setInfo(t("backup.done", { dump: result.dumpFileName }));
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("backup.runError"));
    } finally {
      setBusy(false);
    }
  }

  async function confirmRestore(event: FormEvent) {
    event.preventDefault();
    if (!restoreName) return;
    setError(null);
    setInfo(null);
    setBusy(true);
    try {
      await restoreBackup(restoreName, restorePassword);
      setRestoreName(null);
      setRestorePassword("");
      setInfo(t("backup.restored"));
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("backup.restoreError"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="dashboard">
      <h1>{t("backup.title")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}
      {info ? <p className="backup-info">{info}</p> : null}

      <section className="coming-soon">
        <h2>{t("backup.settings")}</h2>
        <form className="login-form" onSubmit={(event) => void save(event)}>
          <label>
            {t("backup.folder")}
            <input
              value={settings.folder}
              onChange={(event) => setSettings({ ...settings, folder: event.target.value })}
              required
            />
          </label>
          <label>
            {t("backup.pgDumpPath")}
            <input
              value={settings.pgDumpPath}
              onChange={(event) => setSettings({ ...settings, pgDumpPath: event.target.value })}
              placeholder={t("backup.pgDumpPlaceholder")}
            />
          </label>
          <button type="submit" disabled={busy}>
            {t("backup.saveSettings")}
          </button>
        </form>
      </section>

      <section className="coming-soon">
        <h2>{t("backup.run")}</h2>
        <p>{t("backup.runHint")}</p>
        <button type="button" className="btn-primary" disabled={busy} onClick={() => void backupNow()}>
          {t("backup.runNow")}
        </button>
      </section>

      <section className="coming-soon">
        <h2>{t("backup.files")}</h2>
        {files.length === 0 ? <p>{t("backup.empty")}</p> : null}
        {files.map((file) => (
          <article key={file.dumpFileName} className="staff-row">
            <div>
              <strong>{file.dumpFileName}</strong>
              <span>
                {(file.dumpBytes / 1024).toFixed(1)} Ko
                {file.kycZipFileName ? ` · ${file.kycZipFileName}` : ""}
              </span>
            </div>
            {isAdmin ? (
              <div>
                <button
                  type="button"
                  className="btn-ghost"
                  disabled={busy}
                  onClick={() => {
                    setRestoreName(file.dumpFileName);
                    setRestorePassword("");
                  }}
                >
                  {t("backup.restore")}
                </button>
              </div>
            ) : null}
          </article>
        ))}
      </section>

      {isAdmin && restoreName ? (
        <section className="coming-soon">
          <h2>{t("backup.restoreTitle")}</h2>
          <p>{t("backup.restoreHint", { dump: restoreName })}</p>
          <form className="login-form" onSubmit={(event) => void confirmRestore(event)}>
            <label>
              {t("backup.password")}
              <input
                type="password"
                autoComplete="current-password"
                value={restorePassword}
                onChange={(event) => setRestorePassword(event.target.value)}
                required
                minLength={1}
              />
            </label>
            <button type="submit" disabled={busy}>
              {t("backup.restoreConfirm")}
            </button>
            <button type="button" className="btn-ghost" onClick={() => setRestoreName(null)}>
              {t("backup.cancel")}
            </button>
          </form>
        </section>
      ) : null}
    </main>
  );
}
