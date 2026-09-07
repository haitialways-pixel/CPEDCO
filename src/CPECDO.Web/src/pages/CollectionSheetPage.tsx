import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { fetchCollectionSheet, runLoanAccrual, type CollectionSheet } from "../api/loans";
import { formatMoney } from "../money";

export function CollectionSheetPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const canAccrue = session?.roles.some((r) => r.name === "Admin" || r.name === "Gerant") ?? false;
  const [period, setPeriod] = useState<"today" | "week">("today");
  const [sheet, setSheet] = useState<CollectionSheet | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function load(nextPeriod: "today" | "week") {
    setSheet(await fetchCollectionSheet(nextPeriod));
  }

  useEffect(() => {
    void load(period).catch((err: unknown) => setError(err instanceof Error ? err.message : t("loans.error")));
  }, [period, t]);

  async function onAccrue() {
    setBusy(true);
    setError(null);
    try {
      await runLoanAccrual();
      await load(period);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("loans.error"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="page">
      <p>
        <Link to="/credit/prets">{t("loans.back")}</Link>
      </p>
      <h1>{t("loans.collection")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}
      <p className="row-actions">
        <button type="button" className={period === "today" ? "btn-primary" : "btn-ghost"} onClick={() => setPeriod("today")}>
          {t("loans.duesToday")}
        </button>
        <button type="button" className={period === "week" ? "btn-primary" : "btn-ghost"} onClick={() => setPeriod("week")}>
          {t("loans.duesWeek")}
        </button>
        {canAccrue ? (
          <button type="button" className="btn-ghost" disabled={busy} onClick={() => void onAccrue()}>
            {t("loans.runAccrual")}
          </button>
        ) : null}
      </p>
      <div className="table-wrap">
        <table className="data-table data-table--static">
          <thead>
            <tr>
              <th>{t("loans.loanNo")}</th>
              <th>{t("loans.member")}</th>
              <th>{t("loans.product")}</th>
              <th>{t("loans.dueDate")}</th>
              <th>{t("loans.principalDue")}</th>
              <th>{t("loans.interestDue")}</th>
              <th>{t("loans.penalty")}</th>
              <th>{t("loans.remaining")}</th>
              <th>{t("loans.dpd")}</th>
            </tr>
          </thead>
          <tbody>
            {!sheet || sheet.rows.length === 0 ? (
              <tr>
                <td colSpan={9}>{t("loans.collectionEmpty")}</td>
              </tr>
            ) : (
              sheet.rows.map((row) => (
                <tr key={`${row.loanId}-${row.lineNo}`}>
                  <td>
                    <Link to={`/credit/prets/${row.loanId}`}>{row.loanNo}</Link>
                  </td>
                  <td>
                    {row.memberNo} — {row.memberName}
                  </td>
                  <td>{row.productName}</td>
                  <td>{row.dueDate}</td>
                  <td>{formatMoney(row.principalRemaining, "HTG")}</td>
                  <td>{formatMoney(row.interestRemaining, "HTG")}</td>
                  <td>{formatMoney(row.penaltyRemaining, "HTG")}</td>
                  <td>{formatMoney(row.totalRemaining, "HTG")}</td>
                  <td>{row.daysPastDue}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </main>
  );
}
