import { FormEvent, useEffect, useMemo, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { fetchMember360, searchMembers, type KycDocument, type MemberSummary } from "../api/members";
import { KycPieces } from "../components/KycPieces";
import { IdTrigger, useIdOpen } from "../components/IdTrigger";
import { fetchMemberSavings, type SavingsAccount } from "../api/savings";
import { fetchCurrentTill, type TillSession } from "../api/teller";
import {
  disburseLoan,
  fetchLoans,
  isTillCashShortfall,
  repayLoan,
  type Loan,
  type LoanReceipt,
  type TillCashShortfall
} from "../api/loans";
import { DisburseShortageDialog } from "../components/DisburseShortageDialog";
import { formatMoney } from "../money";

function todayIso() {
  const now = new Date();
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, "0");
  const d = String(now.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

function balances(loan: Loan) {
  const today = todayIso();
  let dueToday = 0;
  let arrears = 0;
  let remaining = 0;
  for (const line of loan.schedule.installments) {
    const rem = line.remaining ?? 0;
    remaining += rem;
    if (line.dueDate < today) arrears += rem;
    else if (line.dueDate === today) dueToday += rem;
  }
  return { dueToday, arrears, remaining };
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
  <p style="margin-top:1.4rem;font-size:10px;letter-spacing:.02em">CPCREDO — Caisse Populaire Épargne et de Crédit pour le Développement de l’Ouest — Pétion-Ville, Haïti</p>
  </body></html>`;
  const w = window.open("", "_blank", "width=480,height=640");
  if (!w) return;
  w.document.write(html);
  w.document.close();
  w.focus();
  w.print();
}

export function TellerCreditPage() {
  const { t } = useTranslation();
  const location = useLocation();
  const { session } = useAuth();
  const roles = session?.roles.map((r) => r.name) ?? [];
  const canCollect = roles.some((r) => r === "Caissier" || r === "Gerant");
  const canDisburse = roles.includes("Caissier");
  const isDisburse = location.pathname.includes("decaissement");

  const [till, setTill] = useState<TillSession | null>(null);
  const [query, setQuery] = useState("");
  const [members, setMembers] = useState<MemberSummary[]>([]);
  const [member, setMember] = useState<MemberSummary | null>(null);
  const [kycDocuments, setKycDocuments] = useState<KycDocument[]>([]);
  const [loans, setLoans] = useState<Loan[]>([]);
  const [loanId, setLoanId] = useState("");
  const ids = useIdOpen();
  const [accounts, setAccounts] = useState<SavingsAccount[]>([]);
  const [savingsAccountId, setSavingsAccountId] = useState("");
  const [amount, setAmount] = useState("");
  const [confirmPay, setConfirmPay] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [shortage, setShortage] = useState<TillCashShortfall | null>(null);

  const selected = useMemo(() => loans.find((l) => l.id === loanId) ?? null, [loans, loanId]);
  const figures = selected ? balances(selected) : null;
  const actionable = loans.filter((l) =>
    isDisburse ? l.status === "Approved" : l.status === "Active"
  );

  useEffect(() => {
    void fetchCurrentTill("HTG")
      .then(setTill)
      .catch(() => setTill(null));
  }, []);

  async function onSearch(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await searchMembers(query);
      setMembers(result.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  async function selectMember(item: MemberSummary) {
    setMember(item);
    setKycDocuments([]);
    setLoanId("");
    setAmount("");
    setConfirmPay(false);
    setBusy(true);
    setError(null);
    try {
      const list = await fetchLoans(undefined, item.id);
      const next = list.filter((l) => (isDisburse ? l.status === "Approved" : l.status === "Active"));
      setLoans(next);
      setLoanId(next[0]?.id ?? "");
      const savings = await fetchMemberSavings(item.id);
      setAccounts(savings);
      setSavingsAccountId(savings[0]?.id ?? "");
      try {
        const profile = await fetchMember360(item.id);
        setKycDocuments(profile.kycDocuments ?? []);
      } catch {
        setKycDocuments([]);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  async function attemptDisburse() {
    if (!selected || selected.status !== "Approved") return;
    setBusy(true);
    setError(null);
    try {
      const next = await disburseLoan(selected.id, savingsAccountId || undefined);
      setLoans((current) => current.map((l) => (l.id === next.id ? next : l)));
      setShortage(null);
      const tillNext = await fetchCurrentTill(selected.currencyCode);
      setTill(tillNext);
    } catch (err) {
      if (isTillCashShortfall(err)) {
        setShortage(err.shortfall);
      } else {
        setError(err instanceof Error ? err.message : t("teller.error"));
      }
    } finally {
      setBusy(false);
    }
  }

  async function onDisburse(event: FormEvent) {
    event.preventDefault();
    await attemptDisburse();
  }

  async function onCollect(event: FormEvent) {
    event.preventDefault();
    if (!selected || selected.status !== "Active") return;
    if (!confirmPay) {
      setConfirmPay(true);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await repayLoan(selected.id, Number(amount), selected.currencyCode);
      printLoanReceipt(result.receipt);
      setLoans((current) => current.map((l) => (l.id === result.loan.id ? result.loan : l)));
      setAmount("");
      setConfirmPay(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  if (!canCollect && !canDisburse) {
    return (
      <main className="page">
        <p>
          <Link to="/teller">{t("teller.backTill")}</Link>
        </p>
        <p className="login-form__error">{t("teller.creditForbidden")}</p>
      </main>
    );
  }

  return (
    <main className="page">
      <p>
        <Link to="/teller">{t("teller.backTill")}</Link>
      </p>
      <h1>{isDisburse ? t("nav.disburse") : t("teller.creditTitle")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}

      {!till ? (
        <p className="muted">
          {t("teller.creditNeedTill")}{" "}
          <Link to="/teller">{t("teller.open")}</Link>
        </p>
      ) : (
        <p className="muted">
          {t("teller.expected")}: {formatMoney(till.expectedCash, till.currencyCode)}
        </p>
      )}

      <form className="search-bar" onSubmit={(event) => void onSearch(event)}>
        <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t("members.searchPlaceholder")} />
        <button type="submit" disabled={busy}>
          {t("members.search")}
        </button>
      </form>
      {members.length > 0 ? (
        <ul className="coming-soon ul-reset">
          {members.map((item) => (
            <li key={item.id}>
              <button type="button" className="btn-ghost" onClick={() => void selectMember(item)}>
                {item.memberNo} — {item.fullName}
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {member ? (
        <>
          <p>
            <strong>{member.fullName}</strong>
            <IdTrigger
              open={ids.open}
              onToggle={ids.toggle}
              lines={[{ value: member.memberNo }, { value: selected?.loanNo }]}
            />
          </p>
          <KycPieces
            memberId={member.id}
            documents={kycDocuments}
            onChanged={async () => {
              const profile = await fetchMember360(member.id);
              setKycDocuments(profile.kycDocuments ?? []);
            }}
          />
          {actionable.length === 0 ? (
            <p>{t("teller.noCreditToCollect")}</p>
          ) : (
            <>
              <label>
                {t("loans.loanNo")}
                <select
                  value={loanId}
                  onChange={(e) => {
                    setLoanId(e.target.value);
                    setConfirmPay(false);
                    setAmount("");
                  }}
                >
                  {actionable.map((loan) => (
                    <option key={loan.id} value={loan.id}>
                      {ids.open ? `${loan.loanNo} — ` : ""}
                      {loan.productDisplayName} ({t(`loans.status.${loan.status}`)})
                    </option>
                  ))}
                </select>
              </label>

              {selected && figures ? (
                <section className="facts">
                  <article>
                    <span>{t("loans.status")}</span>
                    <strong>{t(`loans.status.${selected.status}`)}</strong>
                  </article>
                  <article>
                    <span>{t("loans.remainingBalanceDue")}</span>
                    <strong className="tabular-nums">
                      {formatMoney(
                        selected.status === "PaidOff" || selected.status === "WrittenOff"
                          ? 0
                          : selected.remainingBalanceDue ?? figures.remaining,
                        selected.currencyCode
                      )}
                    </strong>
                  </article>
                  <article>
                    <span>{t("loans.originalAmount")}</span>
                    <strong className="tabular-nums">{formatMoney(selected.principal, selected.currencyCode)}</strong>
                  </article>
                  <article>
                    <span>{t("loans.totalDisbursed")}</span>
                    <strong className="tabular-nums">
                      {formatMoney(
                        (selected.cashDisbursedAmount ?? 0) + (selected.savingsDisbursedAmount ?? 0),
                        selected.currencyCode
                      )}
                    </strong>
                  </article>
                  <article>
                    <span>{t("loans.totalRepaid")}</span>
                    <strong className="tabular-nums">{formatMoney(selected.totalRepaid ?? 0, selected.currencyCode)}</strong>
                  </article>
                  <article>
                    <span>{t("loans.nextPayment")}</span>
                    <strong className="tabular-nums">
                      {selected.nextPaymentAmount == null
                        ? "—"
                        : formatMoney(selected.nextPaymentAmount, selected.currencyCode)}
                    </strong>
                  </article>
                  <article>
                    <span>{t("loans.nextDueDate")}</span>
                    <strong>{selected.nextPaymentDueDate ?? "—"}</strong>
                  </article>
                  <article>
                    <span>{t("teller.dueToday")}</span>
                    <strong>{formatMoney(figures.dueToday, selected.currencyCode)}</strong>
                  </article>
                  <article>
                    <span>{t("teller.arrears")}</span>
                    <strong>{formatMoney(figures.arrears, selected.currencyCode)}</strong>
                  </article>
                </section>
              ) : null}

              {isDisburse && selected?.status === "Approved" && canDisburse ? (
                <form className="stack-form" onSubmit={(event) => void onDisburse(event)}>
                  <h2>{t("loans.disburse")}</h2>
                  {selected.compulsorySavingsPercent > 0 ? (
                    <label>
                      {t("loans.savingsAccount")}
                      <select value={savingsAccountId} onChange={(e) => setSavingsAccountId(e.target.value)} required>
                        <option value="">{t("loans.selectSavings")}</option>
                        {accounts.map((account) => (
                          <option key={account.id} value={account.id}>
                            {ids.open ? `${account.accountNo} — ` : ""}
                            {account.productName}
                          </option>
                        ))}
                      </select>
                    </label>
                  ) : null}
                  <button type="submit" disabled={busy || !till}>
                    {busy ? t("loans.saving") : t("loans.disburse")}
                  </button>
                </form>
              ) : null}
              {shortage ? (
                <DisburseShortageDialog
                  shortfall={shortage}
                  onCancel={() => setShortage(null)}
                  onFunded={async () => {
                    const tillNext = await fetchCurrentTill(shortage.currencyCode);
                    setTill(tillNext);
                    await attemptDisburse();
                  }}
                />
              ) : null}

              {!isDisburse && selected?.status === "Active" && canCollect ? (
                <form className="stack-form" onSubmit={(event) => void onCollect(event)}>
                  <h2>{t("teller.creditPay")}</h2>
                  <p className="muted">{t("loans.repayHint")}</p>
                  <label>
                    {t("loans.repayAmount")}
                    <input
                      type="number"
                      min="0.01"
                      step="0.01"
                      value={amount}
                      onChange={(e) => {
                        setAmount(e.target.value);
                        setConfirmPay(false);
                      }}
                      required
                    />
                  </label>
                  {confirmPay ? (
                    <p>
                      {t("teller.confirmPay", { amount: formatMoney(Number(amount), selected.currencyCode) })}
                    </p>
                  ) : null}
                  <button type="submit" disabled={busy || !till}>
                    {busy ? t("loans.saving") : confirmPay ? t("teller.confirm") : t("teller.creditPay")}
                  </button>
                </form>
              ) : null}

              {isDisburse && selected?.status === "Approved" && !canDisburse ? (
                <p className="muted">{t("teller.disburseCashierOnly")}</p>
              ) : null}
            </>
          )}
        </>
      ) : null}
    </main>
  );
}
