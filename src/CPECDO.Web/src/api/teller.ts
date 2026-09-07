import { getToken, parseError } from "./client";

export type TillSession = {
  id: string;
  currencyCode: string;
  status: string;
  openingFloat: number;
  expectedCash: number;
  countedCash: number | null;
  overShortAmount: number | null;
  openedAtUtc: string;
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
  const response = await fetch(`/api/v1/tills/current?currency=${currency}`, { headers: headers() });
  if (response.status === 409) return null;
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as TillSession;
}

export async function openTill(openingFloat: number, currency = "HTG"): Promise<TillSession> {
  const response = await fetch("/api/v1/tills/open", {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ currencyCode: currency, openingFloat })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as TillSession;
}

export async function closeTill(
  tillId: string,
  denominations: { faceValue: number; quantity: number }[]
): Promise<TillSession> {
  const response = await fetch(`/api/v1/tills/${tillId}/close`, {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify({ denominations })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as TillSession;
}

export async function postCash(
  kind: "deposit" | "withdraw",
  savingsAccountId: string,
  amount: number
): Promise<CashPostResult> {
  const response = await fetch(`/api/v1/tills/${kind}`, {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify({ savingsAccountId, amount })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as CashPostResult;
}
