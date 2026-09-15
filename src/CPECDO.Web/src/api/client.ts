import type { Institution, LoginResponse, MeResponse } from "./types";

const TOKEN_KEY = "cpcredo.token";

/** Empty base = relative /api/... (same origin on LAN/phone). Dev uses the Vite proxy. Never localhost. */
export const API_BASE = "";

export function apiUrl(path: string): string {
  const normalized = path.startsWith("/") ? path : `/${path}`;
  return `${API_BASE}${normalized}`;
}

export async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const response = await fetch(apiUrl(path), init);
  if (response.status === 401) {
    try {
      const body = (await response.clone().json()) as { code?: string };
      if (body.code === "auth.session_expired") {
        window.dispatchEvent(new Event("cpcredo:session-expired"));
      }
    } catch {
      /* not JSON */
    }
  }
  return response;
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string | null): void {
  if (token) localStorage.setItem(TOKEN_KEY, token);
  else localStorage.removeItem(TOKEN_KEY);
}

export async function parseError(response: Response): Promise<string> {
  const text = await response.text();
  const looksLikeProxyFailure =
    response.status >= 500 &&
    (/ECONNREFUSED|ENOTFOUND|proxy error|Bad Gateway|Unable to connect/i.test(text) ||
      !text.trim().startsWith("{"));

  if (looksLikeProxyFailure) {
    return "Impossible de joindre le serveur. Démarrez l’API (dotnet run --project src/CPCREDO.WebApi) et PostgreSQL.";
  }

  try {
    const body = JSON.parse(text) as { error?: string; detail?: string };
    if (body.error && body.detail) return `${body.error} (${body.detail.split("\n")[0]})`;
    return body.error ?? `Erreur ${response.status}`;
  } catch {
    return `Erreur ${response.status}`;
  }
}

export async function fetchInstitution(): Promise<Institution> {
  const response = await apiFetch("/api/public/institution");
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Institution;
}

export async function login(username: string, password: string): Promise<LoginResponse> {
  const response = await apiFetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as LoginResponse;
}

export async function verifyMfa(ticket: string, code: string): Promise<LoginResponse> {
  const response = await apiFetch("/api/auth/mfa/verify", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ticket, code })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as LoginResponse;
}

export async function fetchMe(token: string): Promise<MeResponse> {
  const response = await apiFetch("/api/auth/me", {
    headers: { Authorization: `Bearer ${token}` }
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as MeResponse;
}

export async function changePassword(token: string, currentPassword: string, newPassword: string): Promise<void> {
  const response = await apiFetch("/api/auth/change-password", {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ currentPassword, newPassword })
  });
  if (!response.ok) throw new Error(await parseError(response));
}

export async function logout(token: string | null): Promise<void> {
  if (!token) return;
  try {
    await apiFetch("/api/auth/logout", {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` }
    });
  } catch {
    /* local logout still proceeds */
  }
}
