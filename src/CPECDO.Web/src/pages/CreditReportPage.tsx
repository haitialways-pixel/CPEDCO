import { FormEvent, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { fetchCreditReport, type CreditReport } from "../api/reports";
import { fetchLoanProducts, type LoanProduct } from "../api/loans";
import { formatMoney } from "../money";

function today() {
  return new Date().toISOString().slice(0, 10);
}

function monthAgo() {
  const d = new Date();
  d.setDate(d.getDate() - 30);
  return d.toISOString().slice(0, 10);
}

export function CreditReportPage() {
  const { t } = useTranslation();
  const [from, setFrom] = useState(monthAgo);
  const [to, setTo] = useState(today);
  const [productId, setProductId] = useState("");
  const [products, setProducts] = useState<LoanProduct[]>([]);
  const [report, setReport] = useState<CreditReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void fetchLoanProducts(true)
      .then(setProducts)
      .catch(() => setProducts([]));
  }, []);

  async function load(event?: FormEvent) {
    event?.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setReport(await fetchCreditReport({ from, to, productId: productId || undefined }));
    } catch (err) {
      setReport(null);
      setError(err instanceof Error ? err.message : t("reports.error"));
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const href = (status?: string) => {
    const q = new URLSearchParams({ from, to });
    if (productId) q.set("productId", productId);
    if (status) q.set("status", status);
    return `/credit/prets?${q.toString()}`;
  };

  return (
    <main className="page">
      <h1>{t("nav.creditReports")}</h1>
      <form className="search-bar" onSubmit={(e) => void load(e)}>
        <label>
          {t("reports.from")}
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} required />
        </label>
        <label>
          {t("reports.asOf")}
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} required />
        </label>
        <label>
          {t("loans.product")}
          <select value={productId} onChange={(e) => setProductId(e.target.value)}>
            <option value="">{t("loans.all")}</option>
            {products.map((p) => (
              <option key={p.id} value={p.id}>
                {p.displayName}
              </option>
            ))}
          </select>
        </label>
        <button type="submit" disabled={busy}>
          {busy ? t("reports.loading") : t("reports.run")}
        </button>
      </form>
      {error ? <p className="login-form__error">{error}</p> : null}
      {report ? (
        <>
          <section className="facts">
            <Kpi to={href()} label={t("creditReport.received")} value={String(report.received)} />
            <Kpi to={href("PendingApproval")} label={t("creditReport.pending")} value={String(report.pending)} />
            <Kpi to={href("Approved")} label={t("creditReport.approved")} value={String(report.approved)} />
            <Kpi to={href("Rejected")} label={t("creditReport.rejected")} value={String(report.rejected)} />
            <Kpi to={href()} label={t("creditReport.cancelled")} value={String(report.cancelled)} />
            <Kpi to={href("Active")} label={t("creditReport.disbursed")} value={String(report.disbursed)} />
            <Kpi to={href()} label={t("creditReport.requested")} value={formatMoney(report.requestedAmount, report.currencyCode)} />
            <Kpi to={href("Approved")} label={t("creditReport.approvedAmt")} value={formatMoney(report.approvedAmount, report.currencyCode)} />
            <Kpi to={href("Active")} label={t("creditReport.disbursedAmt")} value={formatMoney(report.disbursedAmount, report.currencyCode)} />
            <Kpi to={href("Active")} label={t("creditReport.outstanding")} value={formatMoney(report.outstandingPrincipal, report.currencyCode)} />
            <Kpi to="/credit/fonds" label={t("creditReport.poolOpening")} value={formatMoney(report.poolOpening, report.currencyCode)} />
            <Kpi to="/credit/fonds" label={t("creditReport.poolFunded")} value={formatMoney(report.poolFunded, report.currencyCode)} />
            <Kpi to="/credit/fonds" label={t("creditReport.poolDisbursed")} value={formatMoney(report.poolDisbursed, report.currencyCode)} />
            <Kpi to="/credit/fonds" label={t("creditReport.poolAvailable")} value={formatMoney(report.poolAvailable, report.currencyCode)} />
            <Kpi to={href("Active")} label={t("creditReport.interestReceived")} value={formatMoney(report.interestReceived, report.currencyCode)} />
            <Kpi to={href("Active")} label={t("creditReport.interestAccrued")} value={formatMoney(report.interestAccrued, report.currencyCode)} />
            <Kpi to={href("Active")} label={t("creditReport.fees")} value={formatMoney(report.feesPenaltiesReceived, report.currencyCode)} />
            <Kpi to={href("Approved")} label={t("creditReport.approvalRate")} value={report.approvalRate == null ? "—" : `${Math.round(report.approvalRate * 100)} %`} />
            <Kpi to={href()} label={t("creditReport.avgDecision")} value={fmtDays(report.avgDaysApplyToDecision)} />
            <Kpi to={href("Active")} label={t("creditReport.avgDisburse")} value={fmtDays(report.avgDaysDecisionToDisburse)} />
            <Kpi to="/reports?kind=par-ct90" label={t("creditReport.par30")} value={formatMoney(report.par30, report.currencyCode)} />
            <Kpi to="/reports?kind=par-ct90" label={t("creditReport.par90")} value={formatMoney(report.par90, report.currencyCode)} />
          </section>
          {report.rejectReasons.length > 0 ? (
            <section className="card-block">
              <h2>{t("creditReport.rejectReasons")}</h2>
              <ul>
                {report.rejectReasons.map((row) => (
                  <li key={row.reason}>
                    {row.reason} — {row.count}
                  </li>
                ))}
              </ul>
            </section>
          ) : null}
          <Breakdown title={t("creditReport.byProduct")} rows={report.byProduct} currency={report.currencyCode} />
          <Breakdown title={t("creditReport.byOfficer")} rows={report.byOfficer} currency={report.currencyCode} />
        </>
      ) : null}
    </main>
  );
}

function fmtDays(value: number | null) {
  if (value == null) return "—";
  return `${value.toFixed(1)} j`;
}

function Kpi({ to, label, value }: { to: string; label: string; value: string }) {
  return (
    <article>
      <span>{label}</span>
      <strong>
        <Link to={to}>{value}</Link>
      </strong>
    </article>
  );
}

function Breakdown({
  title,
  rows,
  currency
}: {
  title: string;
  rows: { id: string; label: string; count: number; requested: number; disbursed: number; href: string }[];
  currency: string;
}) {
  const { t } = useTranslation();
  if (rows.length === 0) return null;
  return (
    <section className="card-block">
      <h2>{title}</h2>
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th>{title}</th>
              <th>{t("creditReport.received")}</th>
              <th>{t("creditReport.requested")}</th>
              <th>{t("creditReport.disbursedAmt")}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <td>
                  <Link to={row.href}>{row.label}</Link>
                </td>
                <td>{row.count}</td>
                <td>{formatMoney(row.requested, currency)}</td>
                <td>{formatMoney(row.disbursed, currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
