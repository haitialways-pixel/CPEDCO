import { parseError, getToken, apiFetch } from "./client";

export type MemberSummary = {
  id: string;
  memberNo: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phone: string;
  city: string;
  status: string;
  kycStatus: string;
  legalStatus: string;
  isFounder: boolean;
  votingRights: boolean;
};

export type ShareHoldings = {
  qualificationAccountNo: string | null;
  permanentAccountNo: string | null;
  qualificationShareCount: number;
  permanentShareCount: number;
  parValue: number;
  currencyCode: string;
  qualificationBookValue: number;
  permanentBookValue: number;
  votingRights: boolean;
  voteRule: string;
};

export type Member360 = {
  id: string;
  memberNo: string;
  firstName: string;
  lastName: string;
  fullName: string;
  cin: string | null;
  nif: string | null;
  phone: string;
  alternatePhone: string | null;
  addressLine: string;
  city: string;
  commune: string | null;
  dateOfBirth: string | null;
  placeOfBirth: string | null;
  occupation: string | null;
  branchId: string;
  branchName: string;
  status: string;
  kycStatus: string;
  legalStatus: string;
  isFounder: boolean;
  founderGroup: string | null;
  usagerSinceUtc: string | null;
  probationDays: number;
  usagerDaysElapsed: number | null;
  usagerDaysLeft: number | null;
  servicesBlocked: boolean;
  votingRights: boolean;
  voteRule: string;
  createdAtUtc: string;
  createdAtPortAuPrince: string;
  updatedAtUtc: string | null;
  shares: ShareHoldings;
  savingsAccounts: import("./savings").SavingsAccount[];
  recentTransactions: MemberTransaction[];
  tickets: MemberTicket[];
  loansPlaceholder: string;
  kycDocuments: KycDocument[];
  loans: {
    id: string;
    loanNo: string;
    status: string;
    cycleNumber: number;
    maxRenewals: number | null;
    remainingRenewals: number | null;
    isEvergreen: boolean;
    principal: number;
    currencyCode: string;
  }[];
};

export type MemberTransaction = {
  savingsAccountId: string;
  accountNo: string;
  valueDateUtc: string;
  postedAtUtc: string;
  postedAtPortAuPrince: string;
  entryType: string;
  amount: number;
  currencyCode: string;
  description: string;
};

export type MemberTicket = {
  id: string;
  ticketNo: string;
  subject: string;
  body: string;
  status: string;
  createdByUserId: string;
  createdByName: string;
  assignedToUserId: string | null;
  assignedToName: string | null;
  createdAtUtc: string;
  closedAtUtc: string | null;
};

export type KycDocumentType = "Photo" | "IdFront" | "IdBack" | "Signature";

export type KycDocument = {
  id: string;
  type: KycDocumentType;
  contentType: string;
  fileName: string;
  uploadedAtUtc: string;
  uploadedBy: string;
  uploadedByName: string;
};

export type StaffSummary = {
  id: string;
  username: string;
  fullName: string;
};

export type MemberWrite = {
  firstName: string;
  lastName: string;
  cin?: string;
  nif?: string;
  phone: string;
  alternatePhone?: string;
  addressLine: string;
  city: string;
  commune?: string;
  dateOfBirth?: string | null;
  placeOfBirth?: string;
  occupation?: string;
  status?: string;
  kycStatus?: string;
  legalStatus?: string;
  qualificationShareCount?: number;
  votingRights?: boolean;
  isFounder?: boolean;
  founderGroup?: string | null;
};

function headers(): HeadersInit {
  const token = getToken();
  return {
    "Content-Type": "application/json",
    ...(token ? { Authorization: `Bearer ${token}` } : {})
  };
}

export async function searchMembers(q: string, status?: string): Promise<{ items: MemberSummary[]; total: number }> {
  const params = new URLSearchParams();
  if (q.trim()) params.set("q", q.trim());
  if (status) params.set("status", status);
  const response = await apiFetch(`/api/v1/members?${params.toString()}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as { items: MemberSummary[]; total: number };
}

export async function fetchMember360(id: string): Promise<Member360> {
  const response = await apiFetch(`/api/v1/members/${id}/360`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function createMember(body: MemberWrite): Promise<Member360> {
  const response = await apiFetch("/api/v1/members", {
    method: "POST",
    headers: headers(),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function convertToSocietaire(id: string): Promise<Member360> {
  const response = await apiFetch(`/api/v1/members/${id}/convert-to-societaire`, {
    method: "POST",
    headers: { ...headers(), "Idempotency-Key": crypto.randomUUID() }
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function subscribePermanentShares(id: string, quantity: number): Promise<Member360> {
  const response = await apiFetch(`/api/v1/members/${id}/permanent-shares`, {
    method: "POST",
    headers: { ...headers(), "Idempotency-Key": crypto.randomUUID() },
    body: JSON.stringify({ quantity })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function updateMember(id: string, body: MemberWrite, overrideGrantId?: string | null): Promise<Member360> {
  const extra: Record<string, string> = {};
  if (overrideGrantId) extra["X-Member-Override"] = overrideGrantId;
  const response = await apiFetch(`/api/v1/members/${id}`, {
    method: "PUT",
    headers: { ...headers(), ...extra },
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function authorizeFicheOverride(
  memberId: string,
  body: { username: string; password: string; action?: string }
): Promise<{ grantId: string; documentId: string; action: string; expiresAtUtc: string }> {
  const response = await apiFetch(`/api/v1/members/${memberId}/override-auth`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ ...body, action: body.action ?? "edit-fiche" })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as {
    grantId: string;
    documentId: string;
    action: string;
    expiresAtUtc: string;
  };
}

export async function fetchStaff(): Promise<StaffSummary[]> {
  const response = await apiFetch("/api/v1/staff/directory", { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as StaffSummary[];
}

export async function openTicket(memberId: string, subject: string, body: string, assignToUserId?: string): Promise<MemberTicket> {
  const response = await apiFetch(`/api/v1/members/${memberId}/tickets`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ subject, body, assignToUserId: assignToUserId || null })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as MemberTicket;
}

export async function assignTicket(memberId: string, ticketId: string, assignedToUserId: string): Promise<MemberTicket> {
  const response = await apiFetch(`/api/v1/members/${memberId}/tickets/${ticketId}/assign`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ assignedToUserId })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as MemberTicket;
}

export type KycOverrideAction = "replace" | "delete";

export async function uploadKycDocument(
  memberId: string,
  type: KycDocumentType,
  file: File,
  overrideGrantId?: string | null
): Promise<KycDocument> {
  const token = getToken();
  const body = new FormData();
  body.append("file", file);
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (overrideGrantId) headers["X-Kyc-Override"] = overrideGrantId;
  const response = await apiFetch(`/api/v1/members/${memberId}/kyc/${type}`, {
    method: "PUT",
    headers,
    body
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as KycDocument;
}

export async function deleteKycDocument(
  memberId: string,
  type: KycDocumentType,
  overrideGrantId?: string | null
): Promise<void> {
  const token = getToken();
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (overrideGrantId) headers["X-Kyc-Override"] = overrideGrantId;
  const response = await apiFetch(`/api/v1/members/${memberId}/kyc/${type}`, {
    method: "DELETE",
    headers
  });
  if (!response.ok) throw new Error(await parseError(response));
}

export async function authorizeKycOverride(
  documentId: string,
  body: { username: string; password: string; action: KycOverrideAction }
): Promise<{ grantId: string; documentId: string; action: string; expiresAtUtc: string }> {
  const response = await apiFetch(`/api/v1/kyc/${documentId}/override-auth`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as {
    grantId: string;
    documentId: string;
    action: string;
    expiresAtUtc: string;
  };
}

export async function fetchKycFile(memberId: string, type: KycDocumentType): Promise<string> {
  const token = getToken();
  const response = await apiFetch(`/api/v1/members/${memberId}/kyc/${type}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {}
  });
  if (!response.ok) throw new Error(await parseError(response));
  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

export async function closeTicket(memberId: string, ticketId: string): Promise<MemberTicket> {
  const response = await apiFetch(`/api/v1/members/${memberId}/tickets/${ticketId}/close`, {
    method: "POST",
    headers: headers()
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as MemberTicket;
}
