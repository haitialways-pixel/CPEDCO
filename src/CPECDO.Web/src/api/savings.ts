import { getToken, parseError, apiFetch, readJson } from "./client";

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
  return readJson<SavingsProduct[]>(response);
}

export async function createSavingsProduct(body: SaveSavingsProduct): Promise<SavingsProduct> {
  const response = await apiFetch("/api/v1/savings-products", {
    method: "POST",
    headers: headers(),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return readJson<SavingsProduct>(response);
}

export async function updateSavingsProduct(id: string, body: SaveSavingsProduct): Promise<SavingsProduct> {
  const response = await apiFetch(`/api/v1/savings-products/${id}`, {
    method: "PUT",
    headers: headers(),
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await parseError(response));
  return readJson<SavingsProduct>(response);
}

export async function deactivateSavingsProduct(id: string): Promise<SavingsProduct> {
  const response = await apiFetch(`/api/v1/savings-products/${id}/deactivate`, {
    method: "POST",
    headers: headers()
  });
  if (!response.ok) throw new Error(await parseError(response));
  return readJson<SavingsProduct>(response);
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

export type LivretLine = {
  valueDateUtc: string;
  description: string;
  debit: number;
  credit: number;
  runningBalance: number;
  cashierName: string;
  entryId?: string | null;
};

export type LivretPreview = {
  accountId: string;
  currencyCode: string;
  lines: LivretLine[];
};

export class LivretEmptyError extends Error {
  constructor(message = "Rien de nouveau à porter sur le livret.") {
    super(message);
    this.name = "LivretEmptyError";
  }
}

export async function fetchUnprintedLivret(accountId: string): Promise<LivretPreview> {
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}/livret`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return readJson<LivretPreview>(response);
}

export async function confirmLivretPrint(accountId: string, entryIds: string[]): Promise<LivretPreview> {
  const response = await apiFetch(`/api/v1/savings/accounts/${accountId}/livret/confirm`, {
    method: "POST",
    headers: headers(),
    body: JSON.stringify({ entryIds })
  });
  if (!response.ok) throw new Error(await parseError(response));
  return readJson<LivretPreview>(response);
}

function moneyCell(value: number, currency: string): string {
  if (!value) return "";
  return formatLivretAmount(value, currency);
}

function formatLivretAmount(value: number, currency: string): string {
  const rounded = Math.round(Math.abs(value) * 100) / 100;
  const [whole, frac = "00"] = rounded.toFixed(2).split(".");
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, "\u202F");
  const symbol = currency.toUpperCase() === "USD" ? "$US" : "G";
  return `${value < 0 ? "-" : ""}${grouped},${frac} ${symbol}`;
}

function livretPrintHtml(preview: LivretPreview): string {
  const rows = preview.lines
    .map((line) => {
      const date = (line.valueDateUtc ?? "").slice(0, 10);
      return `<tr><td>${date}</td><td class="num">${moneyCell(line.debit, preview.currencyCode)}</td><td class="num">${moneyCell(line.credit, preview.currencyCode)}</td><td class="num">${formatLivretAmount(line.runningBalance, preview.currencyCode)}</td></tr>`;
    })
    .join("");
  return `<!doctype html><html lang="fr"><head><meta charset="utf-8"/><title>Livret</title>
<style>
  @page { size: A6; margin: 8mm; }
  body { font-family: "Segoe UI", sans-serif; color: #111; font-size: 11px; margin: 0; }
  table { width: 100%; border-collapse: collapse; }
  th, td { padding: 3px 4px; border-bottom: 1px solid #ddd; }
  th { text-align: left; font-size: 10px; }
  td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
  nav, header, footer, .no-print { display: none !important; }
</style></head><body>
<table>
  <thead><tr><th>Date</th><th class="num">Débit</th><th class="num">Crédit</th><th class="num">Solde</th></tr></thead>
  <tbody>${rows}</tbody>
</table>
</body></html>`;
}

function openPrintDocument(html: string): Window | null {
  const w = window.open("", "_blank", "width=420,height=560");
  if (w) {
    w.document.write(html);
    w.document.close();
    return w;
  }
  const iframe = document.createElement("iframe");
  iframe.setAttribute("aria-hidden", "true");
  iframe.style.position = "fixed";
  iframe.style.right = "0";
  iframe.style.bottom = "0";
  iframe.style.width = "0";
  iframe.style.height = "0";
  iframe.style.border = "0";
  document.body.appendChild(iframe);
  const doc = iframe.contentDocument;
  if (!doc) {
    iframe.remove();
    return null;
  }
  doc.write(html);
  doc.close();
  return iframe.contentWindow;
}

export async function printUnprintedLivret(accountId: string): Promise<void> {
  const preview = await fetchUnprintedLivret(accountId);
  const lines = Array.isArray(preview.lines) ? preview.lines : [];
  if (lines.length === 0) throw new LivretEmptyError();
  const ids = lines.map((line) => line.entryId).filter((id): id is string => Boolean(id));
  const target = openPrintDocument(livretPrintHtml({ ...preview, lines }));
  if (!target) throw new Error("Fenêtre d’impression bloquée.");
  target.focus();
  target.print();
  await confirmLivretPrint(accountId, ids);
  try {
    target.close();
  } catch {
    /* ignore */
  }
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
