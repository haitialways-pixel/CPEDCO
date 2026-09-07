import { FormEvent, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  approveLoan,
  disburseLoan,
  fetchLoan,
  previewPayoffQuote,
  rejectLoan,
  repayLoan,
  submitLoan,
  type Loan,
  type LoanReceipt,
  type PayoffQuote
} from "../api/loans";
import { fetchMemberSavings, type SavingsAccount } from "../api/savings";
import { formatMoney } from "../money";

export function LoanDetailPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const { session } = useAuth();
  const roles = session?.roles.map((r) => r.name) ?? [];
  const canSubmit = roles.some((r) => ["Admin", "Gerant", "OfficierCredit"].includes(r));
  const canApprove = roles.some((r) => r === "Admin" || r === "Gerant");
  const canDisburse = roles.includes("Caissier");

  const [loan, setLoan] = useState<Loan | null>(null);
  const [accounts, setAccounts] = useState<SavingsAccount[]>([]);
  const [savingsAccountId, setSavingsAccountId] = useState("");
  const [rejectReason, setRejectReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [daysElapsed, setDaysElapsed] = useState("45");
  const [quote, setQuote] = useState<PayoffQuote | null>(null);
  const [busy, setBusy] = useState(false);
  const [repayAmount, setRepayAmount] = useState("");

  async function load(loanId: string) {
    const next = await fetchLoan(loanId);
    setLoan(next);
    if (next.status === "Approved") {
      const list = await fetchMemberSavings(next.memberId);
      setAccounts(list);
      setSavingsAccountId((current) => current || list[0]?.id || "");
    }
  }

  useEffect(() => {
    if (!id) return;
    void load(id).catch((err: unknown) => setError(err instanceof Error ? err.message : t("loans.error")));
  }, [id, t]);

  async function run(action: () => Promise<Loan>) {
    setBusy(true);
    setError(null);
    try {
      const next = await action();
      setLoan(next);
      if (next.status === "Approved") {
        const list = await fetchMemberSavings(next.memberId);
        setAccounts(list);
        setSavingsAccountId(list[0]?.id || "");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : t("loans.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onDisburse(event: FormEvent) {
    event.preventDefault();
    if (!loan) return;
    await run(() => disburseLoan(loan.id, savingsAccountId || undefined));
  }

  async function onQuote() {
    if (!loan) return;
    setBusy(true);
    setError(null);
    try {
      setQuote(
        await previewPayoffQuote(
          {
            productId: loan.productId,
            principal: loan.principal,
            agreedRatePercent: loan.agreedRatePercent
          },
          Number(daysElapsed)
        )
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : t("loans.error"));
    } finally {
      setBusy(false);
    }
  }

  if (!loan && !error) {
    return (
      <main className="page">
        <p>{t("loans.loading")}</p>
      </main>
    );
  }

  const compulsoryPreview = loan
    ? Math.round(loan.principal * (loan.compulsorySavingsPercent / 100) * 10000) / 10000
    : 0;

  return (
    <main className="page">
      <p>
        <Link to="/credit/prets">{t("loans.back")}</Link>
      </p>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}
      {loan ? (
        <>
          <h1>
            {loan.loanNo} · {loan.productDisplayName}
          </h1>
          <p>
            {loan.memberNo} — {loan.memberName}
          </p>
          <section className="facts">
            <article>
              <span>{t("loans.status")}</span>
              <strong>{t(`loans.status.${loan.status}`, { defaultValue: loan.status })}</strong>
            </article>
            <article>
              <span>{t("loans.principal")}</span>
              <strong>{formatMoney(loan.principal, loan.currencyCode)}</strong>
            </article>
            <article>
              <span>{t("loans.agreedRate")}</span>
              <strong>{loan.agreedRatePercent}</strong>
            </article>
            <article>
              <span>{t("loans.compulsorySavings")}</span>
              <strong>
                {loan.compulsorySavingsPercent}% ·{" "}
                {formatMoney(loan.status === "Active" ? loan.compulsorySavingsAmount : compulsoryPreview, loan.currencyCode)}
              </strong>
            </article>
            <article>
              <span>{t("loans.totalDue")}</span>
              <strong>{formatMoney(loan.totalDue, loan.currencyCode)}</strong>
            </article>
            <article>
              <span>{t("loans.cycle")}</span>
              <strong>{loan.cycleNumber}</strong>
            </article>
            <article>
              <span>{t("loans.dpd")}</span>
              <strong>{loan.daysPastDue}</strong>
            </article>
          </section>

          {loan.requiresSecondApproval && loan.status === "PendingApproval" ? (
            <p className="muted">
              {t("loans.secondApproval")}
              {loan.approver1Name ? ` — 1: ${loan.approver1Name}` : ""}
            </p>
          ) : null}
          {loan.approver1Name ? (
            <p className="muted">
              {t("loans.approvedBy")}: {loan.approver1Name}
              {loan.approver2Name ? ` · ${loan.approver2Name}` : ""}
            </p>
          ) : null}
          {loan.postedJournalId ? (
            <p className="muted">
              {t("loans.journal")}: {loan.postedJournalId} · {t("loans.cashOut")}:{" "}
              {formatMoney(loan.cashDisbursedAmount, loan.currencyCode)}
            </p>
          ) : null}
          {loan.rejectReason ? <p className="muted">{loan.rejectReason}</p> : null}

          <div className="row-actions">
            {canSubmit && loan.status === "Draft" ? (
              <button type="button" disabled={busy} onClick={() => void run(() => submitLoan(loan.id))}>
                {t("loans.submit")}
              </button>
            ) : null}
            {canApprove && loan.status === "PendingApproval" ? (
              <>
                <button type="button" disabled={busy} onClick={() => void run(() => approveLoan(loan.id))}>
                  {t("loans.approve")}
                </button>
                <form
                  className="search-bar"
                  onSubmit={(event) => {
                    event.preventDefault();
                    void run(() => rejectLoan(loan.id, rejectReason));
                  }}
                >
                  <input
                    value={rejectReason}
                    onChange={(e) => setRejectReason(e.target.value)}
                    placeholder={t("loans.rejectReason")}
                  />
                  <button type="submit" className="btn-ghost" disabled={busy}>
                    {t("loans.reject")}
                  </button>
                </form>
              </>
            ) : null}
          </div>

          {canDisburse && loan.status === "Active" ? (
            <form
              className="stack-form"
              onSubmit={(event) => {
                event.preventDefault();
                void run(async () => {
                  const result = await repayLoan(loan.id, Number(repayAmount));
                  printLoanReceipt(result.receipt);
                  setRepayAmount("");
                  return result.loan;
                });
              }}
            >
              <h2>{t("loans.repay")}</h2>
              <p className="muted">{t("loans.repayHint")}</p>
              <label>
                {t("loans.repayAmount")}
                <input
                  type="number"
                  min="0.01"
                  step="0.01"
                  value={repayAmount}
                  onChange={(e) => setRepayAmount(e.target.value)}
                  required
                />
              </label>
              <button type="submit" disabled={busy}>
                {busy ? t("loans.saving") : t("loans.repay")}
              </button>
            </form>
          ) : null}

          {canDisburse && loan.status === "Approved" ? (
            <form className="stack-form" onSubmit={(event) => void onDisburse(event)}>
              <h2>{t("loans.disburse")}</h2>
              <p className="muted">{t("loans.disburseHint")}</p>
              {loan.compulsorySavingsPercent > 0 ? (
                <label>
                  {t("loans.savingsAccount")}
                  <select
                    value={savingsAccountId}
                    onChange={(e) => setSavingsAccountId(e.target.value)}
                    required
                  >
                    <option value="">{t("loans.selectSavings")}</option>
                    {accounts.map((account) => (
                      <option key={account.id} value={account.id}>
                        {account.accountNo} — {account.productName}
                      </option>
                    ))}
                  </select>
                </label>
              ) : null}
              <button type="submit" disabled={busy}>
                {busy ? t("loans.saving") : t("loans.disburse")}
              </button>
            </form>
          ) : null}

          <h2>{t("loans.schedule")}</h2>
          <div className="table-wrap">
            <table className="data-table data-table--static">
              <thead>
                <tr>
                  <th>{t("loans.lineNo")}</th>
                  <th>{t("loans.dueDate")}</th>
                  <th>{t("loans.principalDue")}</th>
                  <th>{t("loans.interestDue")}</th>
                  <th>{t("loans.penalty")}</th>
                  <th>{t("loans.remaining")}</th>
                </tr>
              </thead>
              <tbody>
                {loan.schedule.installments.map((line) => (
                  <tr key={line.lineNo}>
                    <td>{line.lineNo}</td>
                    <td>{line.dueDate}</td>
                    <td>{formatMoney(line.principalDue, loan.currencyCode)}</td>
                    <td>{formatMoney(line.interestDue, loan.currencyCode)}</td>
                    <td>{formatMoney(line.penaltyDue ?? 0, loan.currencyCode)}</td>
                    <td>{formatMoney(line.remaining ?? line.totalDue, loan.currencyCode)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <h2>{t("loans.payoffQuote")}</h2>
          <div className="search-bar">
            <label>
              {t("loans.daysElapsed")}
              <input type="number" min={0} value={daysElapsed} onChange={(e) => setDaysElapsed(e.target.value)} />
            </label>
            <button type="button" disabled={busy} onClick={() => void onQuote()}>
              {t("loans.payoffQuote")}
            </button>
          </div>
          {quote ? (
            <p>
              {t("loans.interestDueNow")}: <strong>{formatMoney(quote.interestDue, loan.currencyCode)}</strong>
              {" · "}
              {t("loans.payoffTotal")}: <strong>{formatMoney(quote.totalDue, loan.currencyCode)}</strong>
            </p>
          ) : null}
        </>
      ) : null}
    </main>
  );
}

function printLoanReceipt(receipt: LoanReceipt) {
  const html = `<!doctype html><html lang="fr"><head><meta charset="utf-8"/><title>${receipt.receiptNo}</title>
  <style>
    body{font-family:Georgia,serif;padding:24px;color:#1a1714}
    .sigle{font-size:28px;font-weight:700;letter-spacing:.18em;margin:0}
    .l2{font-size:14px;font-weight:600;margin:.4rem 0 0}
    .l3{font-size:13px;font-weight:400;font-style:italic;margin:.15rem 0 0}
    .l4{font-size:11px;letter-spacing:.12em;text-transform:uppercase;margin:.5rem 0 1rem}
    h2{font-size:16px;margin:1rem 0}
    table{width:100%;border-collapse:collapse}
    td{padding:.25rem 0}
  </style></head><body>
  <p class="sigle">${receipt.letterheadSigle}</p>
  <p class="l2">${receipt.letterheadLine2}</p>
  <p class="l3">${receipt.letterheadLine3}</p>
  <p class="l4">${receipt.letterheadLine4}</p>
  <h2>${receipt.title}</h2>
  <table>
    <tr><td>N° reçu</td><td>${receipt.receiptNo}</td></tr>
    <tr><td>Prêt</td><td>${receipt.loanNo}</td></tr>
    <tr><td>Produit</td><td>${receipt.productName}</td></tr>
    <tr><td>Membre</td><td>${receipt.memberNo} — ${receipt.memberName}</td></tr>
    <tr><td>Montant</td><td>${formatMoney(receipt.amount, receipt.currencyCode)}</td></tr>
    <tr><td>Pénalité</td><td>${formatMoney(receipt.penalty, receipt.currencyCode)}</td></tr>
    <tr><td>Intérêt</td><td>${formatMoney(receipt.interest, receipt.currencyCode)}</td></tr>
    <tr><td>Capital</td><td>${formatMoney(receipt.principal, receipt.currencyCode)}</td></tr>
    <tr><td>Solde restant</td><td>${formatMoney(receipt.remainingBalance ?? 0, receipt.currencyCode)}</td></tr>
    <tr><td>Journal</td><td>${receipt.journalNo}</td></tr>
    <tr><td>Caissier</td><td>${receipt.cashierName}</td></tr>
    <tr><td>Agence</td><td>${receipt.branchName}</td></tr>
  </table>
  </body></html>`;
  const w = window.open("", "_blank", "width=480,height=640");
  if (!w) return;
  w.document.write(html);
  w.document.close();
  w.focus();
  w.print();
}
