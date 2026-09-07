import { getToken, parseError } from "./client";

export type LoanProduct = {
  id: string;
  code: string;
  legalName: string;
  commercialName: string | null;
  displayName: string;
  smsName: string | null;
  termDays: number;
  installmentCount: number;
  repaymentFrequency: string;
  interestMethod: string;
  defaultRatePercent: number;
  maxRenewals: number | null;
  compulsorySavingsPercent: number;
  minPrincipal: number | null;
  maxPrincipal: number | null;
  officerMaxApproval: number | null;
  earlyPayoffChargesFullFlatInterest: boolean;
  latePenaltyPercentPerDay: number;
  isActive: boolean;
};

export type SaveLoanProduct = {
  code: string;
  legalName: string;
  commercialName?: string | null;
  smsName?: string | null;
  termDays: number;
  installmentCount: number;
  repaymentFrequency: string;
  defaultRatePercent: number;
  maxRenewals?: number | null;
  compulsorySavingsPercent: number;
  minPrincipal?: number | null;
  maxPrincipal?: number | null;
  officerMaxApproval?: number | null;
  earlyPayoffChargesFullFlatInterest: boolean;
  latePenaltyPercentPerDay?: number;
  isActive: boolean;
};

export type LoanInstallment = {
  lineNo: number;
  dueDate: string;
  principalDue: number;
  interestDue: number;
  totalDue: number;
  principalPaid: number;
  interestPaid: number;
  penaltyDue: number;
  penaltyPaid: number;
  remaining: number;
};

export type LoanSchedule = {
  principal: number;
  agreedRatePercent: number;
  rateAppliesToTermDays: number;
  totalInterest: number;
  totalDue: number;
  installmentCount: number;
  installments: LoanInstallment[];
};

export type Loan = {
  id: string;
  loanNo: string;
  memberId: string;
  memberNo: string;
  memberName: string;
  productId: string;
  productDisplayName: string;
  principal: number;
  agreedRatePercent: number;
  rateAppliesToTermDays: number;
  totalInterest: number;
  totalDue: number;
  currencyCode: string;
  status: string;
  cycleNumber: number;
  renewedFromLoanId: string | null;
  renewedToLoanId: string | null;
  originationDate: string;
  compulsorySavingsPercent: number;
  compulsorySavingsAmount: number;
  cashDisbursedAmount: number;
  savingsDisbursedAmount: number;
  officerMaxApproval: number | null;
  requiresSecondApproval: boolean;
  submittedByUserId: string | null;
  submittedAtUtc: string | null;
  approver1Id: string | null;
  approver1Name: string | null;
  approved1AtUtc: string | null;
  approver2Id: string | null;
  approver2Name: string | null;
  approved2AtUtc: string | null;
  disbursedByUserId: string | null;
  disbursedByName: string | null;
  disbursedAtUtc: string | null;
  postedJournalId: string | null;
  savingsAccountId: string | null;
  lienId: string | null;
  rejectReason: string | null;
  daysPastDue: number;
  schedule: LoanSchedule;
};

export type LoanReceipt = {
  letterheadSigle: string;
  letterheadLine2: string;
  letterheadLine3: string;
  letterheadLine4: string;
  type: string;
  title: string;
  receiptNo: string;
  journalNo: string;
  loanNo: string;
  memberNo: string;
  memberName: string;
  productName: string;
  amount: number;
  penalty: number;
  interest: number;
  principal: number;
  currencyCode: string;
  remainingBalance: number;
  cashierName: string;
  branchName: string;
  postedAtUtc: string;
  postedAtPortAuPrince: string;
};

export type CollectionSheetRow = {
  loanId: string;
  loanNo: string;
  memberNo: string;
  memberName: string;
  productName: string;
  lineNo: number;
  dueDate: string;
  principalRemaining: number;
  interestRemaining: number;
  penaltyRemaining: number;
  totalRemaining: number;
  daysPastDue: number;
  isArrears: boolean;
};

export type CollectionSheet = {
  period: string;
  from: string;
  to: string;
  rows: CollectionSheetRow[];
};

export type PayoffQuote = {
  principal: number;
  interestDue: number;
  totalDue: number;
  daysElapsed: number;
  termDays: number;
  chargesFullFlatInterest: boolean;
};

function headers(): HeadersInit {
  const token = getToken();
  return {
    "Content-Type": "application/json",
    ...(token ? { Authorization: `Bearer ${token}` } : {})
  };
}

async function read<T>(response: Response): Promise<T> {
  if (!response.ok) throw new Error(await parseError(response));
  return (await response.json()) as T;
}

export async function fetchLoanProducts(activeOnly = false): Promise<LoanProduct[]> {
  const qs = activeOnly ? "?activeOnly=true" : "";
  return read(await fetch(`/api/v1/loan-products${qs}`, { headers: headers() }));
}

export async function createLoanProduct(body: SaveLoanProduct): Promise<LoanProduct> {
  return read(await fetch("/api/v1/loan-products", { method: "POST", headers: headers(), body: JSON.stringify(body) }));
}

export async function updateLoanProduct(id: string, body: SaveLoanProduct): Promise<LoanProduct> {
  return read(
    await fetch(`/api/v1/loan-products/${id}`, { method: "PUT", headers: headers(), body: JSON.stringify(body) })
  );
}

export async function deactivateLoanProduct(id: string): Promise<LoanProduct> {
  return read(await fetch(`/api/v1/loan-products/${id}/deactivate`, { method: "POST", headers: headers() }));
}

export async function fetchLoans(status?: string, memberId?: string): Promise<Loan[]> {
  const params = new URLSearchParams();
  if (status) params.set("status", status);
  if (memberId) params.set("memberId", memberId);
  const qs = params.toString();
  return read(await fetch(`/api/v1/loans${qs ? `?${qs}` : ""}`, { headers: headers() }));
}

export async function fetchLoan(id: string): Promise<Loan> {
  return read(await fetch(`/api/v1/loans/${id}`, { headers: headers() }));
}

export async function previewLoanSchedule(body: {
  productId: string;
  principal: number;
  agreedRatePercent: number;
  startDate?: string;
}): Promise<LoanSchedule> {
  return read(await fetch("/api/v1/loans/preview", { method: "POST", headers: headers(), body: JSON.stringify(body) }));
}

export async function previewPayoffQuote(
  body: { productId: string; principal: number; agreedRatePercent: number },
  daysElapsed: number
): Promise<PayoffQuote> {
  return read(
    await fetch(`/api/v1/loans/payoff-quote?daysElapsed=${daysElapsed}`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify(body)
    })
  );
}

export async function createLoanDraft(body: {
  memberId: string;
  productId: string;
  principal: number;
  agreedRatePercent: number;
}): Promise<Loan> {
  return read(await fetch("/api/v1/loans", { method: "POST", headers: headers(), body: JSON.stringify(body) }));
}

export async function submitLoan(id: string): Promise<Loan> {
  return read(await fetch(`/api/v1/loans/${id}/submit`, { method: "POST", headers: headers() }));
}

export async function approveLoan(id: string): Promise<Loan> {
  return read(await fetch(`/api/v1/loans/${id}/approve`, { method: "POST", headers: headers() }));
}

export async function rejectLoan(id: string, reason?: string): Promise<Loan> {
  return read(
    await fetch(`/api/v1/loans/${id}/reject`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify({ reason })
    })
  );
}

export async function repayLoan(
  id: string,
  amount: number,
  currencyCode = "HTG"
): Promise<{ loan: Loan; receipt: LoanReceipt }> {
  const token = getToken();
  return read(
    await fetch(`/api/v1/loans/${id}/repayments`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        "Idempotency-Key": crypto.randomUUID()
      },
      body: JSON.stringify({ amount, currencyCode })
    })
  );
}

export async function runLoanAccrual(): Promise<{ loansUpdated: number; asOf: string }> {
  return read(await fetch("/api/v1/loans/run-accrual", { method: "POST", headers: headers() }));
}

export async function fetchCollectionSheet(period: "today" | "week"): Promise<CollectionSheet> {
  return read(await fetch(`/api/v1/loans/collection-sheet?period=${period}`, { headers: headers() }));
}

export async function disburseLoan(id: string, savingsAccountId?: string): Promise<Loan> {
  const token = getToken();
  return read(
    await fetch(`/api/v1/loans/${id}/disburse`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        "Idempotency-Key": crypto.randomUUID()
      },
      body: JSON.stringify({ savingsAccountId })
    })
  );
}
