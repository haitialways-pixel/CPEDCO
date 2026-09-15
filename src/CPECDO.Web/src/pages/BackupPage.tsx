import { FormEvent, useEffect, useState } from "react";
import { Navigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  fetchBackupStatus,
  restoreBackup,
  runBackup,
  saveBackupSettings,
  setAutoBackup,
  type BackupFile,
  type BackupSettings,
  type BackupStatus
} from "../api/backup";

const emptySettings: BackupSettings = {
  folder: "",
  pgDumpPath: "",
  autoBackupEnabled: false,
  retentionDays: 14,
  keepFiles: 7
};

export function BackupPage() {
  const { t, i18n } = useTranslation();
  const { session } = useAuth();
  const [settings, setSettings] = useState<BackupSettings>(emptySettings);
  const [files, setFiles] = useState<BackupFile[]>([]);
  const [lastStatus, setLastStatus] = useState("");
  const [lastDump, setLastDump] = useState<string | null>(null);
  const [lastError, setLastError] = useState<string | null>(null);
  const [nextRun, setNextRun] = useState<string | null>(null);
  const [autoOn, setAutoOn] = useState(false);
  const [autoState, setAutoState] = useState("Désactivée");
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [restoreName, setRestoreName] = useState<string | null>(null);
  const [restorePassword, setRestorePassword] = useState("");
  const [restoreConfirm, setRestoreConfirm] = useState("");

  const isAdmin = session?.roles.some((r) => r.name === "Admin") ?? false;
  const canBackup = session?.roles.some((r) => r.name === "Admin" || r.name === "Gerant") ?? false;
  const locale = i18n.language === "en" ? "en" : "fr-HT";

  function applyStatus(status: BackupStatus) {
    setSettings({
      folder: status.folder,
      pgDumpPath: status.pgDumpPath,
      autoBackupEnabled: status.autoBackupEnabled,
      retentionDays: status.retentionDays > 0 ? status.retentionDays : 14,
      keepFiles: status.keepFiles > 0 ? status.keepFiles : 7
    });
    setFiles(status.files);
    setLastStatus(status.lastStatus);
    setLastDump(status.lastDumpFileName);
    setLastError(status.lastError);
    setNextRun(status.nextRunAtLocal);
    setAutoOn(status.autoBackupEnabled);
    setAutoState(status.autoBackupState);
  }

  async function reload() {
    applyStatus(await fetchBackupStatus());
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
      setError(err instanceof Error ? `Échec — ${err.message}` : t("backup.runError"));
      await reload().catch(() => undefined);
    } finally {
      setBusy(false);
    }
  }

  async function toggleAuto(enabled: boolean) {
    if (!isAdmin) return;
    setError(null);
    setInfo(null);
    setBusy(true);
    try {
      applyStatus(await setAutoBackup(enabled));
      setInfo(enabled ? t("backup.autoOn") : t("backup.autoOff"));
    } catch (err) {
      setError(err instanceof Error ? err.message : t("backup.toggleError"));
      await reload().catch(() => undefined);
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
      await restoreBackup(restoreName, restorePassword, restoreConfirm);
      setRestoreName(null);
      setRestorePassword("");
      setRestoreConfirm("");
      setInfo(t("backup.restored"));
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("backup.restoreError"));
    } finally {
      setBusy(false);
    }
  }

  const nextLabel =
    autoOn && nextRun
      ? new Date(nextRun).toLocaleString(locale, {
          dateStyle: "short",
          timeStyle: "short"
        })
      : "—";

  return (
    <main className="dashboard">
      <h1>{t("backup.title")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}
      {info ? (
        <p className="backup-info" role="status">
          {info}
        </p>
      ) : null}

      <section className="coming-soon">
        <h2>{t("backup.auto")}</h2>
        <label className="backup-toggle">
          <input
            type="checkbox"
            checked={autoOn}
            disabled={!isAdmin || busy}
            onChange={(event) => void toggleAuto(event.target.checked)}
          />
          <span>
            <strong className={autoOn ? "backup-state backup-state--on" : "backup-state backup-state--off"}>
              {autoState || (autoOn ? t("backup.autoOn") : t("backup.autoOff"))}
            </strong>
            {autoOn ? (
              <span>
                {" "}
                · {t("backup.nextRun")}: {nextLabel}
              </span>
            ) : null}
          </span>
        </label>
        {!isAdmin ? <p className="backup-hint">{t("backup.autoAdminOnly")}</p> : null}
      </section>

      <section className="facts">
        <article>
          <span>{t("backup.lastResult")}</span>
          <strong>
            {lastStatus === "OK" || lastStatus === "FAIL" ? lastStatus : "—"}
            {lastDump ? ` · ${lastDump}` : ""}
          </strong>
          {lastStatus === "FAIL" && lastError ? <span>{lastError}</span> : null}
        </article>
        <article>
          <span>{t("backup.nextRun")}</span>
          <strong>{autoOn ? nextLabel : t("backup.autoOff")}</strong>
        </article>
      </section>

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
          <label>
            {t("backup.retentionDays")}
            <input
              type="number"
              min={1}
              max={365}
              value={settings.retentionDays}
              onChange={(event) =>
                setSettings({ ...settings, retentionDays: Number(event.target.value) || 14 })
              }
              required
            />
          </label>
          <label>
            {t("backup.keepFiles")}
            <input
              type="number"
              min={1}
              max={100}
              value={settings.keepFiles}
              onChange={(event) => setSettings({ ...settings, keepFiles: Number(event.target.value) || 7 })}
              required
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
                {new Date(file.createdAtUtc).toLocaleString(locale, {
                  dateStyle: "short",
                  timeStyle: "short"
                })}
                {" · "}
                {(file.dumpBytes / 1024).toFixed(1)} Ko
                {" · "}
                <span
                  className={
                    file.status === "OK" ? "backup-file-status backup-file-status--ok" : "backup-file-status backup-file-status--fail"
                  }
                >
                  {file.status === "OK" ? t("backup.ok") : t("backup.fail")}
                </span>
              </span>
            </div>
            {isAdmin ? (
              <div>
                <button
                  type="button"
                  className="btn-ghost"
                  disabled={busy || file.status !== "OK"}
                  onClick={() => {
                    setRestoreName(file.dumpFileName);
                    setRestorePassword("");
                    setRestoreConfirm("");
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
            <label>
              {t("backup.confirmation")}
              <input
                value={restoreConfirm}
                onChange={(event) => setRestoreConfirm(event.target.value)}
                placeholder="SAUVEGARDE"
                required
                autoComplete="off"
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
