import { getToken, parseError, apiFetch } from "./client";

function headers(idem?: string): HeadersInit {
  const token = getToken();
  return {
    "Content-Type": "application/json",
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(idem ? { "Idempotency-Key": idem } : {})
  };
}

export type CreditPoolMovement = {
  id: string;
  kind: string;
  sourceKind: string;
  amount: number;
  currencyCode: string;
  note: string | null;
  loanId: string | null;
  createdAtUtc: string;
};

export type CreditPool = {
  id: string;
  currencyCode: string;
  fundedTotal: number;
  disbursedTotal: number;
  reservedApprovals: number;
  availableToLend: number;
  movements: CreditPoolMovement[];
};

export async function fetchCreditPool(currency = "HTG"): Promise<CreditPool> {
  const response = await apiFetch(`/api/v1/credit-pool?currency=${currency}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as CreditPool;
}

export async function fundCreditPool(body: {
  sourceKind: string;
  bankAccountId?: string;
  amount: number;
  currencyCode: string;
  note?: string;
}): Promise<CreditPool> {
  const response = await apiFetch("/api/v1/credit-pool/funding", {
    method: "POST",
    headers: headers(crypto.randomUUID()),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as CreditPool;
}
