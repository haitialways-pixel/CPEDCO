import { getToken, parseError, apiFetch } from "./client";

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

export type CreditReport = {
  from: string;
  to: string;
  currencyCode: string;
  received: number;
  pending: number;
  approved: number;
  rejected: number;
  cancelled: number;
  disbursed: number;
  requestedAmount: number;
  approvedAmount: number;
  disbursedAmount: number;
  outstandingPrincipal: number;
  poolOpening: number;
  poolFunded: number;
  poolDisbursed: number;
  poolAvailable: number;
  interestReceived: number;
  interestAccrued: number;
  feesPenaltiesReceived: number;
  approvalRate: number | null;
  avgDaysApplyToDecision: number | null;
  avgDaysDecisionToDisburse: number | null;
  par30: number;
  par90: number;
  rejectReasons: { reason: string; count: number }[];
  byProduct: { id: string; label: string; count: number; requested: number; disbursed: number; href: string }[];
  byOfficer: { id: string; label: string; count: number; requested: number; disbursed: number; href: string }[];
};

export async function fetchCreditReport(query: {
  from: string;
  to: string;
  productId?: string;
  officerId?: string;
}): Promise<CreditReport> {
  const response = await apiFetch(`/api/v1/reports/credit?${params(query)}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as CreditReport;
}

export type ReportKind =
  | "teller-cash-proof"
  | "trial-balance"
  | "financials"
  | "deposits"
  | "liquidity"
  | "par-ct90"
  | "renewal-register";

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
    internalMovements: { direction: string; status: string; amount: number; counterparty: string | null }[];
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

export type ParCt90 = {
  asOf: string;
  currencyCode: string;
  portfolioOutstanding: number;
  loanCount: number;
  par1: { days: number; outstanding: number; ratio: number | null };
  par7: { days: number; outstanding: number; ratio: number | null };
  par30: { days: number; outstanding: number; ratio: number | null };
};

export type RenewalRegister = {
  from: string;
  to: string;
  rows: {
    renewedAtUtc: string;
    memberNo: string;
    memberName: string;
    oldLoanNo: string;
    newLoanNo: string;
    newCycle: number;
    previousPrincipal: number;
    newPrincipal: number;
    isEvergreen: boolean;
    currencyCode: string;
  }[];
};

export type ReportPayload =
  | TrialBalance
  | TellerCashProof
  | Financials
  | DepositListing
  | Liquidity
  | ParCt90
  | RenewalRegister;

export async function fetchReport(
  kind: ReportKind,
  query: Record<string, string | undefined>
): Promise<ReportPayload> {
  const response = await apiFetch(`/api/v1/reports/${kind}?${params(query)}`, { headers: headers() });
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as ReportPayload;
}

export async function downloadReport(
  kind: ReportKind,
  format: "pdf" | "csv",
  query: Record<string, string | undefined>
): Promise<void> {
  const response = await apiFetch(`/api/v1/reports/${kind}?${params({ ...query, format })}`, { headers: headers() });
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
