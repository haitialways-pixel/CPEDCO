import { getToken, parseError } from "./client";

function headers(): HeadersInit {
  const token = getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

function params(values: Record<string, string | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    if (value) search.set(key, value);
  }
  return search.toString();
}

export type ReportKind = "teller-cash-proof" | "trial-balance" | "financials" | "deposits" | "liquidity";

export type TrialBalanceRow = {
  glAccountId: string;
  accountCode: string;
  accountName: string;
  accountType: string;
  debit: number;
  credit: number;
};

export type TrialBalance = {
  asOf: string;
  currencyCode: string;
  rows: TrialBalanceRow[];
  totalDebit: number;
  totalCredit: number;
  net: number;
};

export type ReportLine = { code: string; label: string; amount: number };

export type TellerCashProof = {
  date: string;
  currencyCode: string;
  sessions: {
    tillId: string;
    cashierName: string;
    branchName: string;
    status: string;
    openingFloat: number;
    expectedCash: number;
    countedCash: number | null;
    overShortAmount: number | null;
    deposits: number;
    withdrawals: number;
    counts: { faceValue: number; quantity: number; subtotal: number }[];
  }[];
};

export type Financials = {
  from: string;
  asOf: string;
  currencyCode: string;
  assets: ReportLine[];
  totalAssets: number;
  liabilities: ReportLine[];
  totalLiabilities: number;
  equity: ReportLine[];
  netIncome: number;
  totalEquity: number;
  totalLiabilitiesAndEquity: number;
  income: ReportLine[];
  totalIncome: number;
  expenses: ReportLine[];
  totalExpenses: number;
};

export type DepositListing = {
  from: string;
  to: string;
  currencyCode: string;
  rows: {
    postedAtPortAuPrince: string;
    memberNo: string;
    memberName: string;
    accountNo: string;
    productName: string;
    amount: number;
    cashierName: string | null;
    description: string;
  }[];
  total: number;
};

export type Liquidity = {
  asOf: string;
  currencyCode: string;
  liquidAssets: ReportLine[];
  totalLiquidAssets: number;
  memberDeposits: ReportLine[];
  totalMemberDeposits: number;
  ratio: number | null;
};

export type ReportPayload = TrialBalance | TellerCashProof | Financials | DepositListing | Liquidity;

export async function fetchReport(
  kind: ReportKind,
  query: Record<string, string | undefined>
): Promise<ReportPayload> {
  const response = await fetch(`/api/v1/reports/${kind}?${params(query)}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as ReportPayload;
}

export async function downloadReport(
  kind: ReportKind,
  format: "pdf" | "csv",
  query: Record<string, string | undefined>
): Promise<void> {
  const response = await fetch(`/api/v1/reports/${kind}?${params({ ...query, format })}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  const blob = await response.blob();
  const disposition = response.headers.get("content-disposition") ?? "";
  const match = /filename\*?=(?:UTF-8'')?["']?([^";]+)/i.exec(disposition);
  const filename = match ? decodeURIComponent(match[1]) : `${kind}.${format}`;
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}

export async function fetchTrialBalance(asOf: string, currency: string): Promise<TrialBalance> {
  return (await fetchReport("trial-balance", { asOf, currency })) as TrialBalance;
}
