import { FormEvent, useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  downloadReport,
  fetchReport,
  type DepositListing,
  type Financials,
  type Liquidity,
  type ParCt90,
  type RenewalRegister,
  type ReportKind,
  type ReportPayload,
  type TellerCashProof,
  type TrialBalance
} from "../api/reports";
import { formatMoney, formatMoneyNumber } from "../money";

function today() {
  return new Date().toISOString().slice(0, 10);
}

function yearStart() {
  return `${new Date().getFullYear()}-01-01`;
}

const KINDS: ReportKind[] = [
  "teller-cash-proof",
  "trial-balance",
  "financials",
  "deposits",
  "liquidity",
  "par-ct90",
  "renewal-register"
];

export function ReportsPage() {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  const kindParam = searchParams.get("kind");
  const kind: ReportKind = KINDS.includes(kindParam as ReportKind) ? (kindParam as ReportKind) : "trial-balance";
  const [asOf, setAsOf] = useState(today);
  const [from, setFrom] = useState(yearStart);
  const [currency, setCurrency] = useState("HTG");
  const [report, setReport] = useState<ReportPayload | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const query = {
    currency,
    asOf,
    date: asOf,
    from,
    to: asOf
  };

  async function load(nextKind = kind) {
    setBusy(true);
    setError(null);
    try {
      setReport(await fetchReport(nextKind, query));
    } catch (err) {
      setReport(null);
      setError(err instanceof Error ? err.message : t("reports.error"));
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    void load(kind);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [kind]);

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    void load();
  }

  async function exportFile(format: "pdf" | "csv") {
    setError(null);
    try {
      await downloadReport(kind, format, query);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("reports.error"));
    }
  }

  return (
    <main className="page">
      <h1>{t("reports.title")}</h1>
      <form className="search-bar" onSubmit={onSubmit}>
        <label>
          {t("reports.kind")}
          <select
            value={kind}
            onChange={(e) => {
              const next = e.target.value as ReportKind;
              setSearchParams({ kind: next });
            }}
          >
            {KINDS.map((item) => (
              <option key={item} value={item}>
                {t(`reports.kind.${item}`)}
              </option>
            ))}
          </select>
        </label>
        {kind === "financials" || kind === "deposits" || kind === "renewal-register" ? (
          <label>
            {t("reports.from")}
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
        ) : null}
        <label>
          {kind === "teller-cash-proof" ? t("reports.date") : t("reports.asOf")}
          <input type="date" value={asOf} onChange={(e) => setAsOf(e.target.value)} />
        </label>
        <label>
          {t("reports.currency")}
          <select value={currency} onChange={(e) => setCurrency(e.target.value)}>
            <option value="HTG">HTG</option>
            <option value="USD">USD</option>
          </select>
        </label>
        <button type="submit" disabled={busy}>
          {busy ? t("reports.loading") : t("reports.run")}
        </button>
        <button type="button" className="btn-ghost" onClick={() => void exportFile("pdf")}>
          PDF
        </button>
        <button type="button" className="btn-ghost" onClick={() => void exportFile("csv")}>
          CSV
        </button>
      </form>
      {error ? <p className="login-form__error">{error}</p> : null}
      {report ? <ReportBody kind={kind} report={report} /> : null}
    </main>
  );
}

function ReportBody({ kind, report }: { kind: ReportKind; report: ReportPayload }) {
  const { t } = useTranslation();
  if (kind === "trial-balance") {
    const data = report as TrialBalance;
    return (
      <MoneyTable
        caption={`${t("reports.kind.trial-balance")} · ${data.asOf} · ${data.currencyCode}`}
        headers={[t("reports.code"), t("reports.account"), t("reports.type"), t("reports.debit"), t("reports.credit")]}
        rows={data.rows.map((row) => [
          row.accountCode,
          row.accountName,
          row.accountType,
          formatMoney(row.debit, data.currencyCode),
          formatMoney(row.credit, data.currencyCode)
        ])}
        footer={["", t("reports.totals"), "", formatMoney(data.totalDebit, data.currencyCode), formatMoney(data.totalCredit, data.currencyCode)]}
      />
    );
  }

  if (kind === "teller-cash-proof") {
    const data = report as TellerCashProof;
    return (
      <MoneyTable
        caption={`${t("reports.kind.teller-cash-proof")} · ${data.date} · ${data.currencyCode}`}
        headers={[
          t("reports.cashier"),
          t("reports.branch"),
          t("reports.status"),
          t("reports.float"),
          t("reports.expected"),
          t("reports.counted"),
          t("reports.overShort"),
          t("reports.deposits"),
          t("reports.withdrawals"),
          t("reports.internal")
        ]}
        rows={data.sessions.map((s) => [
          s.cashierName,
          s.branchName,
          s.status,
          formatMoney(s.openingFloat, data.currencyCode),
          formatMoney(s.expectedCash, data.currencyCode),
          s.countedCash == null ? "—" : formatMoney(s.countedCash, data.currencyCode),
          s.overShortAmount == null ? "—" : formatMoney(s.overShortAmount, data.currencyCode),
          formatMoney(s.deposits, data.currencyCode),
          formatMoney(s.withdrawals, data.currencyCode),
          (s.internalMovements ?? [])
            .map((m) => `${m.direction} ${formatMoney(m.amount, data.currencyCode)} (${m.status})`)
            .join(" ; ") || "—"
        ])}
      />
    );
  }

  if (kind === "financials") {
    const data = report as Financials;
    const section = (title: string, lines: Financials["assets"], totalLabel: string, total: number) =>
      lines.map((line) => [title, line.code, line.label, formatMoney(line.amount, data.currencyCode)]).concat([
        [title, "", totalLabel, formatMoney(total, data.currencyCode)]
      ]);
    return (
      <MoneyTable
        caption={`${t("reports.kind.financials")} · ${data.from} → ${data.asOf} · ${data.currencyCode}`}
        headers={[t("reports.section"), t("reports.code"), t("reports.account"), t("reports.amount")]}
        rows={[
          ...section(t("reports.assets"), data.assets, t("reports.totalAssets"), data.totalAssets),
          ...section(t("reports.liabilities"), data.liabilities, t("reports.totalLiabilities"), data.totalLiabilities),
          ...section(t("reports.equity"), data.equity, t("reports.totalEquity"), data.totalEquity),
          [t("reports.equity"), "", t("reports.liabilitiesAndEquity"), formatMoney(data.totalLiabilitiesAndEquity, data.currencyCode)],
          ...section(t("reports.income"), data.income, t("reports.totalIncome"), data.totalIncome),
          ...section(t("reports.expenses"), data.expenses, t("reports.totalExpenses"), data.totalExpenses),
          [t("reports.result"), "", t("reports.netIncome"), formatMoney(data.netIncome, data.currencyCode)]
        ]}
      />
    );
  }

  if (kind === "deposits") {
    const data = report as DepositListing;
    return (
      <MoneyTable
        caption={`${t("reports.kind.deposits")} · ${data.from} → ${data.to} · ${data.currencyCode}`}
        headers={[
          t("reports.posted"),
          t("reports.memberNo"),
          t("reports.memberName"),
          t("reports.accountNo"),
          t("reports.product"),
          t("reports.amount"),
          t("reports.cashier")
        ]}
        rows={data.rows.map((row) => [
          row.postedAtPortAuPrince.replace("T", " ").slice(0, 16),
          row.memberNo,
          row.memberName,
          row.accountNo,
          row.productName,
          formatMoney(row.amount, data.currencyCode),
          row.cashierName ?? "—"
        ])}
        footer={["", "", "", "", t("reports.totals"), formatMoney(data.total, data.currencyCode), ""]}
      />
    );
  }

  if (kind === "par-ct90") {
    const data = report as ParCt90;
    const par = (b: ParCt90["par1"]) => [
      `PAR ${b.days}`,
      formatMoney(b.outstanding, data.currencyCode),
      b.ratio == null ? t("reports.ratioNa") : `${formatMoneyNumber(b.ratio * 100)} %`
    ];
    return (
      <MoneyTable
        caption={`${t("reports.kind.par-ct90")} · ${data.asOf} · ${data.currencyCode}`}
        headers={[t("reports.indicator"), t("reports.outstanding"), t("reports.ratio")]}
        rows={[
          [t("reports.ct90Portfolio"), formatMoney(data.portfolioOutstanding, data.currencyCode), `${data.loanCount}`],
          par(data.par1),
          par(data.par7),
          par(data.par30)
        ]}
      />
    );
  }

  if (kind === "renewal-register") {
    const data = report as RenewalRegister;
    return (
      <MoneyTable
        caption={`${t("reports.kind.renewal-register")} · ${data.from} → ${data.to}`}
        headers={[
          t("reports.date"),
          t("reports.memberNo"),
          t("reports.memberName"),
          t("loans.loanNo"),
          t("reports.newLoanNo"),
          t("loans.cycle"),
          t("reports.previousPrincipal"),
          t("loans.principal"),
          t("loans.evergreen")
        ]}
        rows={data.rows.map((row) => [
          row.renewedAtUtc.slice(0, 10),
          row.memberNo,
          row.memberName,
          row.oldLoanNo,
          row.newLoanNo,
          String(row.newCycle),
          formatMoney(row.previousPrincipal, row.currencyCode),
          formatMoney(row.newPrincipal, row.currencyCode),
          row.isEvergreen ? t("loans.yes") : t("loans.no")
        ])}
      />
    );
  }

  const data = report as Liquidity;
  return (
    <MoneyTable
      caption={`${t("reports.kind.liquidity")} · ${data.asOf} · ${data.currencyCode}`}
      headers={[t("reports.section"), t("reports.code"), t("reports.account"), t("reports.amount")]}
      rows={[
        ...data.liquidAssets.map((line) => [t("reports.liquid"), line.code, line.label, formatMoney(line.amount, data.currencyCode)]),
        [t("reports.liquid"), "", t("reports.totals"), formatMoney(data.totalLiquidAssets, data.currencyCode)],
        ...data.memberDeposits.map((line) => [t("reports.memberDeposits"), line.code, line.label, formatMoney(line.amount, data.currencyCode)]),
        [t("reports.memberDeposits"), "", t("reports.totals"), formatMoney(data.totalMemberDeposits, data.currencyCode)],
        [t("reports.ratio"), "", t("reports.ratioFormula"), data.ratio == null ? t("reports.ratioNa") : data.ratio.toFixed(2)]
      ]}
    />
  );
}

function MoneyTable({
  caption,
  headers,
  rows,
  footer
}: {
  caption: string;
  headers: string[];
  rows: string[][];
  footer?: string[];
}) {
  return (
    <>
      <p className="muted">{caption}</p>
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              {headers.map((header) => (
                <th key={header}>{header}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={headers.length}>—</td>
              </tr>
            ) : (
              rows.map((row, index) => (
                <tr key={index}>
                  {row.map((cell, cellIndex) => (
                    <td key={cellIndex}>{cell}</td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
          {footer ? (
            <tfoot>
              <tr>
                {footer.map((cell, index) => (
                  <td key={index}>{cell}</td>
                ))}
              </tr>
            </tfoot>
          ) : null}
        </table>
      </div>
    </>
  );
}
