import { getToken, parseError } from "./client";

export type BankAccount = {
  id: string;
  bankName: string;
  customBankName: string | null;
  displayBankName: string;
  label: string | null;
  accountNumber: string;
  accountNumberMasked: string;
  pickerLabel: string;
  currencyCode: string;
  glCode: string;
  balance: number;
  isActive: boolean;
  notes: string | null;
};

export type SaveBankAccount = {
  bankName: string;
  customBankName?: string;
  accountNumber: string;
  currencyCode: string;
  label?: string;
  glCode?: string;
  notes?: string;
  isActive: boolean;
};

export type VaultBalance = {
  glCode: string;
  name: string;
  currencyCode: string;
  balance: number;
};

export type TreasuryTransfer = {
  id: string;
  transferNo: string;
  direction: string;
  bankAccountId: string | null;
  bankName: string;
  bank: string;
  bankAccountLabel: string;
  accountNumberMasked: string;
  amount: number;
  feeAmount: number;
  currencyCode: string;
  status: string;
  createdByUserId: string;
  createdByName: string;
  createdByRole: string | null;
  initiatedById: string;
  initiatedByName: string;
  initiatedByRole: string | null;
  approver1Id: string | null;
  approver1Name: string | null;
  approver1Role: string | null;
  approved1AtUtc: string | null;
  approver2Id: string | null;
  approver2Name: string | null;
  approver2Role: string | null;
  approved2AtUtc: string | null;
  executedById: string | null;
  executedByName: string | null;
  executedByRole: string | null;
  bankSlipRef: string | null;
  slipType: string | null;
  slipFileName: string | null;
  slipContentType: string | null;
  hasSlip: boolean;
  notes: string | null;
  cancelReason: string | null;
  postedJournalId: string | null;
  createdAtUtc: string;
  executedAtUtc: string | null;
};

export const BANK_DIRECTIONS = ["VaultToBank", "BankToVault"] as const;

export function involvesBank(direction: string) {
  return BANK_DIRECTIONS.includes(direction as (typeof BANK_DIRECTIONS)[number]);
}

function headers(idempotency?: string, json = true): HeadersInit {
  const token = getToken();
  return {
    ...(json ? { "Content-Type": "application/json" } : {}),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(idempotency ? { "Idempotency-Key": idempotency } : {})
  };
}

async function read<T>(response: Response): Promise<T> {
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as T;
}

export async function fetchBankNames(): Promise<string[]> {
  return read(await fetch("/api/v1/treasury/bank-names", { headers: headers() }));
}

export async function fetchBanks(): Promise<BankAccount[]> {
  return read(await fetch("/api/v1/treasury/banks", { headers: headers() }));
}

export async function createBank(body: SaveBankAccount): Promise<BankAccount> {
  return read(await fetch("/api/v1/treasury/banks", { method: "POST", headers: headers(), body: JSON.stringify(body) }));
}

export async function updateBank(id: string, body: SaveBankAccount): Promise<BankAccount> {
  return read(await fetch(`/api/v1/treasury/banks/${id}`, { method: "PUT", headers: headers(), body: JSON.stringify(body) }));
}

export async function deactivateBank(id: string): Promise<BankAccount> {
  return read(await fetch(`/api/v1/treasury/banks/${id}/deactivate`, { method: "POST", headers: headers() }));
}

export async function fetchVaults(): Promise<VaultBalance[]> {
  return read(await fetch("/api/v1/treasury/vaults", { headers: headers() }));
}

export async function fetchTransfers(): Promise<TreasuryTransfer[]> {
  return read(await fetch("/api/v1/treasury/transfers", { headers: headers() }));
}

export async function fetchTransfer(id: string): Promise<TreasuryTransfer> {
  return read(await fetch(`/api/v1/treasury/transfers/${id}`, { headers: headers() }));
}

export async function createTransfer(body: {
  direction: string;
  amount: number;
  currencyCode: string;
  bankAccountId?: string;
  notes?: string;
}): Promise<TreasuryTransfer> {
  return read(
    await fetch("/api/v1/treasury/transfers", {
      method: "POST",
      headers: headers(),
      body: JSON.stringify(body)
    })
  );
}

export async function approveTransfer(id: string): Promise<TreasuryTransfer> {
  return read(await fetch(`/api/v1/treasury/transfers/${id}/approve`, { method: "POST", headers: headers() }));
}

export async function attachSlip(
  id: string,
  body: { slip: File; slipRef: string; slipType?: string }
): Promise<TreasuryTransfer> {
  const form = new FormData();
  form.append("slip", body.slip);
  form.append("slipRef", body.slipRef);
  if (body.slipType) form.append("slipType", body.slipType);
  return read(
    await fetch(`/api/v1/treasury/transfers/${id}/slip`, {
      method: "POST",
      headers: headers(undefined, false),
      body: form
    })
  );
}

export async function executeTransfer(id: string, password?: string): Promise<TreasuryTransfer> {
  return read(
    await fetch(`/api/v1/treasury/transfers/${id}/execute`, {
      method: "POST",
      headers: headers(crypto.randomUUID()),
      body: JSON.stringify({ password })
    })
  );
}

export async function cancelTransfer(id: string, reason: string): Promise<TreasuryTransfer> {
  return read(
    await fetch(`/api/v1/treasury/transfers/${id}/cancel`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify({ reason })
    })
  );
}

export async function fetchSlipObjectUrl(id: string): Promise<string> {
  const response = await fetch(`/api/v1/treasury/transfers/${id}/slip`, { headers: headers(undefined, false) });
  if (!response.ok) throw new Error(await parseError(response));
  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

export async function downloadTreasuryJournal(format: "pdf" | "csv", from?: string, to?: string): Promise<void> {
  const search = new URLSearchParams({ format });
  if (from) search.set("from", from);
  if (to) search.set("to", to);
  const response = await fetch(`/api/v1/treasury/journal?${search}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  const blob = await response.blob();
  const disposition = response.headers.get("content-disposition") ?? "";
  const match = /filename\*?=(?:UTF-8'')?["']?([^";]+)/i.exec(disposition);
  const filename = match ? decodeURIComponent(match[1]) : `journal-tresorerie.${format}`;
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}
