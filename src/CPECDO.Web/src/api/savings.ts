import { getToken, parseError } from "./client";

export type SavingsProduct = {
  id: string;
  name: string;
  currencyCode: string;
  minimumBalance: number;
  liabilityGlAccountId: string;
  cashGlAccountId: string;
  isActive: boolean;
};

export type SavingsAccount = {
  id: string;
  memberId: string;
  productId: string;
  accountNo: string;
  productName: string;
  currencyCode: string;
  ledgerBalance: number;
  availableBalance: number;
  isActive: boolean;
  isBlocked: boolean;
  blockedReason: string | null;
  openedAtUtc: string;
  holds: SavingsHold[];
};

export type SavingsHold = {
  id: string;
  amount: number;
  reason: string;
  createdAtUtc: string;
};

export type StatementEntry = {
  valueDateUtc: string;
  postedAtUtc: string;
  entryType: string;
  amount: number;
  description: string;
  runningBalance: number;
};

export type SavingsStatement = {
  accountId: string;
  accountNo: string;
  productName: string;
  currencyCode: string;
  ledgerBalance: number;
  availableBalance: number;
  from: string;
  to: string;
  entries: StatementEntry[];
};

function headers(): HeadersInit {
  const token = getToken();
  return {
    "Content-Type": "application/json",
    ...(token ? { Authorization: `Bearer ${token}` } : {})
  };
}

export async function fetchSavingsProducts(): Promise<SavingsProduct[]> {
  const response = await fetch("/api/v1/savings/products", { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsProduct[];
}

export async function fetchMemberSavings(memberId: string): Promise<SavingsAccount[]> {
  const response = await fetch(`/api/v1/savings/members/${memberId}/accounts`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount[];
}

export async function openSavingsAccount(memberId: string, productId: string): Promise<SavingsAccount> {
  const response = await fetch("/api/v1/savings/accounts", {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ memberId, productId })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}

export async function fetchSavingsAccount(accountId: string): Promise<SavingsAccount> {
  const response = await fetch(`/api/v1/savings/accounts/${accountId}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}

export async function fetchStatement(accountId: string, from: string, to: string): Promise<SavingsStatement> {
  const params = new URLSearchParams({ from, to });
  const response = await fetch(`/api/v1/savings/accounts/${accountId}/statement?${params}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsStatement;
}

export async function downloadStatementPdf(accountId: string, from: string, to: string): Promise<void> {
  const params = new URLSearchParams({ from, to });
  const response = await fetch(`/api/v1/savings/accounts/${accountId}/statement.pdf?${params}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `releve-${accountId}.pdf`;
  link.click();
  URL.revokeObjectURL(url);
}

export async function placeHold(accountId: string, amount: number, reason: string): Promise<SavingsAccount> {
  const response = await fetch(`/api/v1/savings/accounts/${accountId}/holds`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ amount, reason })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}

export async function releaseHold(accountId: string, holdId: string): Promise<SavingsAccount> {
  const response = await fetch(`/api/v1/savings/accounts/${accountId}/holds/${holdId}/release`, {
    method: "POST",
    headers: headers()
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}

export async function blockAccount(accountId: string, reason: string): Promise<SavingsAccount> {
  const response = await fetch(`/api/v1/savings/accounts/${accountId}/block`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ reason })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}

export async function unblockAccount(accountId: string): Promise<SavingsAccount> {
  const response = await fetch(`/api/v1/savings/accounts/${accountId}/unblock`, {
    method: "POST",
    headers: headers()
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}
