import { parseError, getToken } from "./client";

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
  status?: string;
  kycStatus?: string;
  legalStatus?: string;
  qualificationShareCount?: number;
  votingRights?: boolean;
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
  const response = await fetch(`/api/v1/members?${params.toString()}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as { items: MemberSummary[]; total: number };
}

export async function fetchMember360(id: string): Promise<Member360> {
  const response = await fetch(`/api/v1/members/${id}/360`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function createMember(body: MemberWrite): Promise<Member360> {
  const response = await fetch("/api/v1/members", {
    method: "POST",
    headers: headers(),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function convertToSocietaire(id: string): Promise<Member360> {
  const response = await fetch(`/api/v1/members/${id}/convert-to-societaire`, {
    method: "POST",
    headers: { ...headers(), "Idempotency-Key": crypto.randomUUID() }
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function subscribePermanentShares(id: string, quantity: number): Promise<Member360> {
  const response = await fetch(`/api/v1/members/${id}/permanent-shares`, {
    method: "POST",
    headers: { ...headers(), "Idempotency-Key": crypto.randomUUID() },
    body: JSON.stringify({ quantity })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function updateMember(id: string, body: MemberWrite): Promise<Member360> {
  const response = await fetch(`/api/v1/members/${id}`, {
    method: "PUT",
    headers: headers(),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as Member360;
}

export async function fetchStaff(): Promise<StaffSummary[]> {
  const response = await fetch("/api/v1/staff/directory", { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as StaffSummary[];
}

export async function openTicket(memberId: string, subject: string, body: string, assignToUserId?: string): Promise<MemberTicket> {
  const response = await fetch(`/api/v1/members/${memberId}/tickets`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ subject, body, assignToUserId: assignToUserId || null })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as MemberTicket;
}

export async function assignTicket(memberId: string, ticketId: string, assignedToUserId: string): Promise<MemberTicket> {
  const response = await fetch(`/api/v1/members/${memberId}/tickets/${ticketId}/assign`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ assignedToUserId })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as MemberTicket;
}

export async function closeTicket(memberId: string, ticketId: string): Promise<MemberTicket> {
  const response = await fetch(`/api/v1/members/${memberId}/tickets/${ticketId}/close`, {
    method: "POST",
    headers: headers()
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as MemberTicket;
}
