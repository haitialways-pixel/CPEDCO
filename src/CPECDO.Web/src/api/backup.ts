import { parseError, apiFetch, getToken } from "./client";

export type BackupSettings = {
  folder: string;
  pgDumpPath: string;
};

export type BackupFile = {
  dumpFileName: string;
  createdAtUtc: string;
  dumpBytes: number;
  kycZipFileName: string | null;
  kycZipBytes: number | null;
};

export type BackupRunResult = {
  dumpFileName: string;
  kycZipFileName: string | null;
  folder: string;
};

export type BackupStatus = {
  folder: string;
  pgDumpPath: string;
  lastStatus: string;
  lastDumpFileName: string | null;
  lastError: string | null;
  lastRunAtUtc: string | null;
  nextRunAtLocal: string;
  files: BackupFile[];
};

function headers(): HeadersInit {
  const token = getToken();
  return {
    "Content-Type": "application/json",
    ...(token ? { Authorization: `Bearer ${token}` } : {})
  };
}

export async function fetchBackupSettings(): Promise<BackupSettings> {
  const response = await apiFetch("/api/v1/admin/backup/settings", { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as BackupSettings;
}

export async function saveBackupSettings(settings: BackupSettings): Promise<BackupSettings> {
  const response = await apiFetch("/api/v1/admin/backup/settings", {
    method: "PUT",
    headers: headers(),
    body: JSON.stringify(settings)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as BackupSettings;
}

export async function fetchBackupFiles(): Promise<BackupFile[]> {
  const response = await apiFetch("/api/v1/admin/backup/files", { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as BackupFile[];
}

export async function runBackup(): Promise<BackupRunResult> {
  const response = await apiFetch("/api/v1/admin/backup", { method: "POST", headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as BackupRunResult;
}

export async function fetchBackupStatus(): Promise<BackupStatus> {
  const response = await apiFetch("/api/v1/admin/backup/status", { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as BackupStatus;
}

export async function restoreBackup(dumpFileName: string, password: string, confirmation: string): Promise<void> {
  const response = await apiFetch("/api/v1/admin/backup/restore", {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ dumpFileName, password, confirmation })
  });
  if (!response.ok) throw new Error(await parseError(response));
}
