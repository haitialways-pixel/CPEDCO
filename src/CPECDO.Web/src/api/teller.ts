import { getToken, parseError, apiFetch } from "./client";

export type TillSession = {
  id: string;
  currencyCode: string;
  status: string;
  openingFloat: number;
  expectedCash: number;
  countedCash: number | null;
  overShortAmount: number | null;
  openedAtUtc: string;
  notes?: string | null;
};

export type CloseTillRequest = {
  countedBalance: number;
  notes?: string;
  denominations: { faceValue: number; quantity: number }[];
};

export type OpenTillPeer = {
  id: string;
  userId: string;
  cashierName: string;
  currencyCode: string;
  expectedCash: number;
};

export type InternalCashMovement = {
  id: string;
  movementNo: string;
  direction: string;
  status: string;
  currencyCode: string;
  amount: number;
  sourceTillSessionId: string | null;
  sourceCashierName: string | null;
  destinationTillSessionId: string | null;
  destinationCashierName: string | null;
  note: string | null;
  createdAtUtc: string;
  acceptedAtUtc: string | null;
  journalEntryId: string | null;
  canAccept: boolean;
  reason?: string;
  sourceType?: string;
  destinationType?: string;
  tellerUserId?: string | null;
  bagId?: string | null;
  receivedAmount?: number | null;
  canReject?: boolean;
};

export type ReceiptLetterhead = {
  sigle: string;
  line2: string;
  line3: string;
  line4: string;
};

export type CashReceipt = {
  letterhead: ReceiptLetterhead;
  type: string;
  title: string;
  receiptNo: string;
  journalNo: string;
  memberNo: string;
  memberName: string;
  accountNo: string;
  productName: string;
  amount: number;
  currencyCode: string;
  ledgerBalance: number;
  availableBalance: number;
  cashierName: string;
  branchName: string;
  postedAtPortAuPrince: string;
  allocations?: { kind: string; accountNo: string; label: string; amount: number }[] | null;
};

export type CashPostResult = {
  receipt: CashReceipt;
  journalId: string;
  journalLineCount: number;
  ledgerBalance: number;
  availableBalance: number;
};

function headers(idempotency?: string): HeadersInit {
  const token = getToken();
  return {
    "Content-Type": "application/json",
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(idempotency ? { "Idempotency-Key": idempotency } : {})
  };
}

export async function fetchCurrentTill(currency = "HTG"): Promise<TillSession | null> {
  const response = await apiFetch(`/api/v1/tills/current?currency=${currency}`, { headers: headers() });
  if (response.status === 409) return null;
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as TillSession;
}

export async function openTill(openingFloat: number, currency = "HTG"): Promise<TillSession> {
  const response = await apiFetch("/api/v1/tills/open", {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify({ currencyCode: currency, openingFloat })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as TillSession;
}

export async function closeTill(tillId: string, request: CloseTillRequest): Promise<TillSession> {
  const response = await apiFetch(`/api/v1/tills/${tillId}/close`, {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify({
      countedBalance: request.countedBalance,
      notes: request.notes,
      denominations: request.denominations
    })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as TillSession;
}

export async function fetchOpenTills(currency = "HTG"): Promise<OpenTillPeer[]> {
  const response = await apiFetch(`/api/v1/tills/open?currency=${currency}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as OpenTillPeer[];
}

export async function fetchInternalMovements(currency = "HTG"): Promise<InternalCashMovement[]> {
  const response = await apiFetch(`/api/v1/tills/internal-movements?currency=${currency}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as InternalCashMovement[];
}

export async function fetchPendingMovements(currency = "HTG"): Promise<InternalCashMovement[]> {
  const response = await apiFetch(`/api/v1/tills/pending-movements?currency=${currency}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as InternalCashMovement[];
}

export type CashSource = {
  kind: string;
  tillSessionId: string | null;
  label: string;
  available: number;
  currencyCode: string;
};

export async function fetchCashSources(currency = "HTG"): Promise<CashSource[]> {
  const response = await apiFetch(`/api/v1/tills/cash-sources?currency=${currency}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  const data: unknown = await response.json();
  return Array.isArray(data) ? (data as CashSource[]) : [];
}

export async function fundDrawer(body: {
  sourceKind: string;
  sourceTillSessionId?: string;
  amount: number;
  currencyCode: string;
  note?: string;
  loanId?: string;
}): Promise<InternalCashMovement> {
  const response = await apiFetch("/api/v1/tills/fund-drawer", {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as InternalCashMovement;
}

export async function createInternalMovement(body: {
  direction: string;
  amount: number;
  currencyCode: string;
  sourceTillSessionId?: string;
  destinationTillSessionId?: string;
  destinationTellerUserId?: string;
  reason?: string;
  note?: string;
  bagId?: string;
}): Promise<InternalCashMovement> {
  const response = await apiFetch("/api/v1/tills/internal-movements", {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as InternalCashMovement;
}

export async function acceptInternalMovement(
  id: string,
  countedAmount?: number
): Promise<InternalCashMovement> {
  const response = await apiFetch(`/api/v1/tills/internal-movements/${id}/accept`, {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify({ countedAmount: countedAmount ?? null })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as InternalCashMovement;
}

export async function rejectInternalMovement(id: string, reason: string): Promise<InternalCashMovement> {
  const response = await apiFetch(`/api/v1/tills/internal-movements/${id}/reject`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ reason })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as InternalCashMovement;
}

export async function postCash(
  kind: "deposit" | "withdraw",
  savingsAccountId: string,
  amount: number,
  gerantOverrideNote?: string
): Promise<CashPostResult> {
  const response = await apiFetch(`/api/v1/tills/${kind}`, {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify({ savingsAccountId, amount, gerantOverrideNote: gerantOverrideNote || null })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as CashPostResult;
}

export async function collectMixed(body: {
  memberId: string;
  cashReceived: number;
  currencyCode: string;
  lines: { kind: string; savingsAccountId?: string | null; amount: number }[];
}): Promise<CashPostResult> {
  const response = await apiFetch("/api/v1/tills/collect", {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as CashPostResult;
}
