import { getToken, parseError, apiFetch } from "./client";

export type SavingsProduct = {
  id: string;
  code: string;
  legalName: string;
  commercialName: string | null;
  displayName: string;
  name: string;
  currencyCode: string;
  productKind: string;
  termDays: number | null;
  interestRatePercent: number;
  interestMethod: string;
  minOpeningAmount: number;
  minimumBalance: number;
  allowWithdrawBeforeTerm: boolean;
  liabilityGlAccountId: string;
  cashGlAccountId: string;
  isActive: boolean;
};

export type SaveSavingsProduct = {
  code: string;
  legalName: string;
  commercialName?: string | null;
  currencyCode: string;
  productKind: string;
  termDays?: number | null;
  termMonths?: number | null;
  interestRatePercent: number;
  interestMethod: string;
  minOpeningAmount: number;
  minimumBalance: number;
  allowWithdrawBeforeTerm: boolean;
  isActive: boolean;
};

export type OpenedAccount = {
  kind: string;
  id: string;
  accountNo: string;
  label: string;
  currencyCode: string;
  balance: number;
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
  lastPassbookPrintAtUtc: string | null;
  maturesOn: string | null;
  allowWithdrawBeforeTerm: boolean;
  productKind: string;
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
  const response = await apiFetch("/api/v1/savings/products", { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsProduct[];
}

export async function fetchMemberSavings(memberId: string): Promise<SavingsAccount[]> {
  const response = await apiFetch(`/api/v1/savings/members/${memberId}/accounts`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount[];
}

export async function openMemberAccount(
  memberId: string,
  kind: string,
  productId?: string
): Promise<OpenedAccount> {
  const response = await apiFetch("/api/v1/savings/accounts", {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ memberId, kind, productId: productId || null })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as OpenedAccount;
}

export async function openSavingsAccount(memberId: string, productId: string): Promise<OpenedAccount> {
  return openMemberAccount(memberId, "Epargne", productId);
}

export async function fetchSavingsProductCatalog(activeOnly = false): Promise<SavingsProduct[]> {
  const qs = activeOnly ? "?activeOnly=true" : "";
  const response = await apiFetch(`/api/v1/savings-products${qs}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsProduct[];
}

export async function createSavingsProduct(body: SaveSavingsProduct): Promise<SavingsProduct> {
  const response = await apiFetch("/api/v1/savings-products", {
    method: "POST",
    headers: headers(),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsProduct;
}

export async function updateSavingsProduct(id: string, body: SaveSavingsProduct): Promise<SavingsProduct> {
  const response = await apiFetch(`/api/v1/savings-products/${id}`, {
    method: "PUT",
    headers: headers(),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsProduct;
}

export async function deactivateSavingsProduct(id: string): Promise<SavingsProduct> {
  const response = await apiFetch(`/api/v1/savings-products/${id}/deactivate`, {
    method: "POST",
    headers: headers()
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsProduct;
}

export async function fetchSavingsAccount(accountId: string): Promise<SavingsAccount> {
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}

export async function fetchStatement(accountId: string, from: string, to: string): Promise<SavingsStatement> {
  const params = new URLSearchParams({ from, to });
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}/statement?${params}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsStatement;
}

export async function downloadLivretPdf(accountId: string, from?: string, to?: string): Promise<void> {
  const params = new URLSearchParams();
  if (from) params.set("from", from);
  if (to) params.set("to", to);
  const qs = params.toString();
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}/livret.pdf${qs ? `?${qs}` : ""}`, {
    method: "POST",
    headers: headers()
  });
  if (!response.ok) throw new Error(await parseError(response));
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `livret-${accountId}.pdf`;
  link.click();
  URL.revokeObjectURL(url);
}

export async function downloadStatementPdf(accountId: string, from: string, to: string): Promise<void> {
  const params = new URLSearchParams({ from, to });
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}/statement.pdf?${params}`, { headers: headers() });
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
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}/holds`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ amount, reason })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}

export async function releaseHold(accountId: string, holdId: string): Promise<SavingsAccount> {
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}/holds/${holdId}/release`, {
    method: "POST",
    headers: headers()
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}

export async function blockAccount(accountId: string, reason: string): Promise<SavingsAccount> {
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}/block`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ reason })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}

export async function unblockAccount(accountId: string): Promise<SavingsAccount> {
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}/unblock`, {
    method: "POST",
    headers: headers()
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as SavingsAccount;
}
