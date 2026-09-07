import type { StaffUser } from "./types";
import { parseError } from "./client";

const headers = (token: string) => ({
  Authorization: `Bearer ${token}`,
  "Content-Type": "application/json"
});

export async function fetchStaff(token: string): Promise<StaffUser[]> {
  const response = await fetch("/api/v1/staff", { headers: headers(token) });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as StaffUser[];
}

export async function createStaff(token: string, request: {
  username: string;
  fullName: string;
  email: string;
  password: string;
  roleName: string;
}): Promise<StaffUser> {
  const response = await fetch("/api/v1/staff", {
    method: "POST",
    headers: headers(token),
    body: JSON.stringify(request)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as StaffUser;
}

export async function assignStaffRole(token: string, id: string, roleName: string): Promise<StaffUser> {
  const response = await fetch(`/api/v1/staff/${id}/role`, {
    method: "PUT",
    headers: headers(token),
    body: JSON.stringify({ roleName })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as StaffUser;
}

export async function disableStaff(token: string, id: string): Promise<StaffUser> {
  const response = await fetch(`/api/v1/staff/${id}/disable`, { method: "POST", headers: headers(token) });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as StaffUser;
}

export async function resetStaffPassword(token: string, id: string, newPassword: string): Promise<StaffUser> {
  const response = await fetch(`/api/v1/staff/${id}/reset-password`, {
    method: "POST",
    headers: headers(token),
    body: JSON.stringify({ newPassword })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as StaffUser;
}

export const staffRoleNames = [
  "Admin",
  "Gerant",
  "Caissier",
  "OfficierCredit",
  "ServiceClient",
  "Commissaire"
] as const;
