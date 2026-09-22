import { Component, FormEvent, useEffect, useState, type ErrorInfo, type ReactNode } from "react";
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

function asList<T>(value: unknown): T[] {
  if (Array.isArray(value)) return value as T[];
  if (value && typeof value === "object") {
    const record = value as { items?: unknown; rows?: unknown; sessions?: unknown };
    if (Array.isArray(record.items)) return record.items as T[];
    if (Array.isArray(record.rows)) return record.rows as T[];
    if (Array.isArray(record.sessions)) return record.sessions as T[];
  }
  return [];
}

class ReportsErrorBoundary extends Component<
  { children: ReactNode; onBack: () => void; onRetry: () => void },
  { error: Error | null }
> {
  state = { error: null as Error | null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("reports.render", error, info);
  }

  componentDidUpdate(prevProps: { children: ReactNode }) {
    if (prevProps.children !== this.props.children && this.state.error) {
      this.setState({ error: null });
    }
  }

  render() {
    if (this.state.error) {
      return (
        <ReportCrashCard
          message={this.state.error.message}
          onBack={this.props.onBack}
          onRetry={() => {
            this.setState({ error: null });
            this.props.onRetry();
          }}
        />
      );
    }
    return this.props.children;
  }
}

function ReportCrashCard({
  message,
  onBack,
  onRetry
}: {
  message: string;
  onBack: () => void;
  onRetry: () => void;
}) {
  const { t } = useTranslation();
  return (
    <section className="card-block" role="alert">
      <h2>{t("reports.error")}</h2>
      <p className="login-form__error">{message || t("reports.crash")}</p>
      <div className="kyc-slot__actions">
        <button type="button" className="btn-ghost" onClick={onBack}>
          {t("reports.back")}
        </button>
        <button type="button" className="btn-primary" onClick={onRetry}>
          {t("reports.retry")}
        </button>
      </div>
    </section>
  );
}

export function ReportsPage() {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  const kindParam = searchParams.get("kind");
  const known = KINDS.includes(kindParam as ReportKind);
  const kind = known ? (kindParam as ReportKind) : null;
  const unknown = Boolean(kindParam) && !known;
  const [asOf, setAsOf] = useState(today);
  const [from, setFrom] = useState(yearStart);
  const [currency, setCurrency] = useState("HTG");
  const [report, setReport] = useState<ReportPayload | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [retryTick, setRetryTick] = useState(0);

  const query = {
    currency,
    asOf,
    date: asOf,
    from,
    to: asOf
  };

  function goList() {
    setSearchParams({});
    setReport(null);
    setError(null);
  }

  async function load(nextKind: ReportKind) {
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
    if (!kind) {
      setReport(null);
      return;
    }
    void load(kind);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [kind, retryTick]);

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (kind) void load(kind);
  }

  async function exportFile(format: "pdf" | "csv") {
    if (!kind) return;
    setError(null);
    try {
      await downloadReport(kind, format, query);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("reports.error"));
    }
  }

  return (
    <ReportsErrorBoundary onBack={goList} onRetry={() => setRetryTick((n) => n + 1)}>
    <main className="page">
      <h1>{t("reports.title")}</h1>
      {kind || unknown ? (
        <p>
          <button type="button" className="btn-ghost" onClick={goList}>
            {t("reports.back")}
          </button>
        </p>
      ) : null}

      {!kind && !unknown ? (
        <ul className="coming-soon ul-reset">
          {KINDS.map((item) => (
            <li key={item}>
              <button type="button" className="btn-ghost" onClick={() => setSearchParams({ kind: item })}>
                {t(`reports.kind.${item}`)}
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {unknown ? (
        <section className="card-block">
          <p>{t("reports.unavailable")}</p>
        </section>
      ) : null}

      {kind ? (
        <>
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
          {error ? (
            <section className="card-block" role="alert">
              <p className="login-form__error">{error}</p>
              <p className="muted">{t("reports.empty")}</p>
              <div className="kyc-slot__actions">
                <button type="button" className="btn-ghost" onClick={goList}>
                  {t("reports.back")}
                </button>
                <button type="button" className="btn-primary" onClick={() => setRetryTick((n) => n + 1)}>
                  {t("reports.retry")}
                </button>
              </div>
            </section>
          ) : null}
          {report && !error ? <ReportBody kind={kind} report={report} /> : null}
          {!busy && !error && !report ? <p className="muted">{t("reports.empty")}</p> : null}
        </>
      ) : null}
    </main>
    </ReportsErrorBoundary>
  );
}

function ReportBody({ kind, report }: { kind: ReportKind; report: ReportPayload }) {
  const { t } = useTranslation();
  if (kind === "trial-balance") {
    const data = report as TrialBalance;
    const rows = asList<TrialBalance["rows"][number]>(data?.rows);
    if (rows.length === 0) return <p className="muted">{t("reports.empty")}</p>;
    return (
      <MoneyTable
        caption={`${t("reports.kind.trial-balance")} · ${data.asOf ?? ""} · ${data.currencyCode ?? ""}`}
        headers={[t("reports.code"), t("reports.account"), t("reports.type"), t("reports.debit"), t("reports.credit")]}
        rows={rows.map((row) => [
          row.accountCode,
          row.accountName,
          row.accountType,
          formatMoney(row.debit, data.currencyCode),
          formatMoney(row.credit, data.currencyCode)
        ])}
        footer={[
          "",
          t("reports.totals"),
          "",
          formatMoney(data.totalDebit ?? 0, data.currencyCode),
          formatMoney(data.totalCredit ?? 0, data.currencyCode)
        ]}
      />
    );
  }

  if (kind === "teller-cash-proof") {
    const data = report as TellerCashProof;
    const sessions = asList<TellerCashProof["sessions"][number]>(data?.sessions);
    if (sessions.length === 0) return <p className="muted">{t("reports.empty")}</p>;
    return (
      <MoneyTable
        caption={`${t("reports.kind.teller-cash-proof")} · ${data.date ?? ""} · ${data.currencyCode ?? ""}`}
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
        rows={sessions.map((s) => [
          s.cashierName,
          s.branchName,
          s.status,
          formatMoney(s.openingFloat, data.currencyCode),
          formatMoney(s.expectedCash, data.currencyCode),
          s.countedCash == null ? "—" : formatMoney(s.countedCash, data.currencyCode),
          s.overShortAmount == null ? "—" : formatMoney(s.overShortAmount, data.currencyCode),
          formatMoney(s.deposits, data.currencyCode),
          formatMoney(s.withdrawals, data.currencyCode),
          asList<{ direction: string; amount: number; status: string }>(s.internalMovements)
            .map((m) => `${m.direction} ${formatMoney(m.amount, data.currencyCode)} (${m.status})`)
            .join(" ; ") || "—"
        ])}
      />
    );
  }

  if (kind === "financials") {
    const data = report as Financials;
    const section = (title: string, lines: unknown, totalLabel: string, total: number) =>
      asList<Financials["assets"][number]>(lines)
        .map((line) => [title, line.code, line.label, formatMoney(line.amount, data.currencyCode)])
        .concat([[title, "", totalLabel, formatMoney(total ?? 0, data.currencyCode)]]);
    const rows = [
      ...section(t("reports.assets"), data.assets, t("reports.totalAssets"), data.totalAssets),
      ...section(t("reports.liabilities"), data.liabilities, t("reports.totalLiabilities"), data.totalLiabilities),
      ...section(t("reports.equity"), data.equity, t("reports.totalEquity"), data.totalEquity),
      [t("reports.equity"), "", t("reports.liabilitiesAndEquity"), formatMoney(data.totalLiabilitiesAndEquity ?? 0, data.currencyCode)],
      ...section(t("reports.income"), data.income, t("reports.totalIncome"), data.totalIncome),
      ...section(t("reports.expenses"), data.expenses, t("reports.totalExpenses"), data.totalExpenses),
      [t("reports.result"), "", t("reports.netIncome"), formatMoney(data.netIncome ?? 0, data.currencyCode)]
    ];
    return (
      <MoneyTable
        caption={`${t("reports.kind.financials")} · ${data.from ?? ""} → ${data.asOf ?? ""} · ${data.currencyCode ?? ""}`}
        headers={[t("reports.section"), t("reports.code"), t("reports.account"), t("reports.amount")]}
        rows={rows}
      />
    );
  }

  if (kind === "deposits") {
    const data = report as DepositListing;
    const rows = asList<DepositListing["rows"][number]>(data?.rows);
    if (rows.length === 0) return <p className="muted">{t("reports.empty")}</p>;
    return (
      <MoneyTable
        caption={`${t("reports.kind.deposits")} · ${data.from ?? ""} → ${data.to ?? ""} · ${data.currencyCode ?? ""}`}
        headers={[
          t("reports.posted"),
          t("reports.memberNo"),
          t("reports.memberName"),
          t("reports.accountNo"),
          t("reports.product"),
          t("reports.amount"),
          t("reports.cashier")
        ]}
        rows={rows.map((row) => [
          (row.postedAtPortAuPrince ?? "").replace("T", " ").slice(0, 16),
          row.memberNo,
          row.memberName,
          row.accountNo,
          row.productName,
          formatMoney(row.amount, data.currencyCode),
          row.cashierName ?? "—"
        ])}
        footer={["", "", "", "", t("reports.totals"), formatMoney(data.total ?? 0, data.currencyCode), ""]}
      />
    );
  }

  if (kind === "par-ct90") {
    const data = report as ParCt90;
    const par = (b: ParCt90["par1"] | undefined) => [
      `PAR ${b?.days ?? "—"}`,
      formatMoney(b?.outstanding ?? 0, data.currencyCode),
      b?.ratio == null ? t("reports.ratioNa") : `${formatMoneyNumber(b.ratio * 100)} %`
    ];
    return (
      <MoneyTable
        caption={`${t("reports.kind.par-ct90")} · ${data.asOf ?? ""} · ${data.currencyCode ?? ""}`}
        headers={[t("reports.indicator"), t("reports.outstanding"), t("reports.ratio")]}
        rows={[
          [t("reports.ct90Portfolio"), formatMoney(data.portfolioOutstanding ?? 0, data.currencyCode), `${data.loanCount ?? 0}`],
          par(data.par1),
          par(data.par7),
          par(data.par30)
        ]}
      />
    );
  }

  if (kind === "renewal-register") {
    const data = report as RenewalRegister;
    const rows = asList<RenewalRegister["rows"][number]>(data?.rows);
    if (rows.length === 0) return <p className="muted">{t("reports.empty")}</p>;
    return (
      <MoneyTable
        caption={`${t("reports.kind.renewal-register")} · ${data.from ?? ""} → ${data.to ?? ""}`}
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
        rows={rows.map((row) => [
          (row.renewedAtUtc ?? "").slice(0, 10),
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

  if (kind === "liquidity") {
    const data = report as Liquidity;
    const liquid = asList<Liquidity["liquidAssets"][number]>(data?.liquidAssets);
    const deposits = asList<Liquidity["memberDeposits"][number]>(data?.memberDeposits);
    return (
      <MoneyTable
        caption={`${t("reports.kind.liquidity")} · ${data.asOf ?? ""} · ${data.currencyCode ?? ""}`}
        headers={[t("reports.section"), t("reports.code"), t("reports.account"), t("reports.amount")]}
        rows={[
          ...liquid.map((line) => [t("reports.liquid"), line.code, line.label, formatMoney(line.amount, data.currencyCode)]),
          [t("reports.liquid"), "", t("reports.totals"), formatMoney(data.totalLiquidAssets ?? 0, data.currencyCode)],
          ...deposits.map((line) => [t("reports.memberDeposits"), line.code, line.label, formatMoney(line.amount, data.currencyCode)]),
          [t("reports.memberDeposits"), "", t("reports.totals"), formatMoney(data.totalMemberDeposits ?? 0, data.currencyCode)],
          [t("reports.ratio"), "", t("reports.ratioFormula"), data.ratio == null ? t("reports.ratioNa") : data.ratio.toFixed(2)]
        ]}
      />
    );
  }

  return (
    <section className="card-block">
      <p>{t("reports.unavailable")}</p>
    </section>
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
  const safeHeaders = Array.isArray(headers) ? headers : [];
  const safeRows = Array.isArray(rows) ? rows : [];
  const { t } = useTranslation();
  if (safeRows.length === 0) {
    return <p className="muted">{t("reports.empty")}</p>;
  }
  return (
    <>
      <p className="muted">{caption}</p>
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              {safeHeaders.map((header) => (
                <th key={header}>{header}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {safeRows.map((row, index) => (
              <tr key={index}>
                {(Array.isArray(row) ? row : []).map((cell, cellIndex) => (
                  <td key={cellIndex}>{cell}</td>
                ))}
              </tr>
            ))}
          </tbody>
          {footer ? (
            <tfoot>
              <tr>
                {(Array.isArray(footer) ? footer : []).map((cell, index) => (
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
