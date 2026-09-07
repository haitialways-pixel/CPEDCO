import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { fetchLoans, type Loan } from "../api/loans";
import { formatMoney } from "../money";

const PIPELINE = ["Draft", "PendingApproval", "Approved", "Active"] as const;

export function LoansPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const roles = session?.roles.map((r) => r.name) ?? [];
  const canWrite = roles.some((r) => ["Admin", "Gerant", "OfficierCredit"].includes(r));
  const canManageProducts = roles.some((r) => r === "Admin" || r === "Gerant");
  const defaultTab = roles.includes("Caissier")
    ? "Approved"
    : roles.includes("Gerant")
      ? "PendingApproval"
      : roles.includes("OfficierCredit")
        ? "Draft"
        : "";
  const [loans, setLoans] = useState<Loan[]>([]);
  const [tab, setTab] = useState<string>(defaultTab);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void fetchLoans()
      .then(setLoans)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("loans.error")));
  }, [t]);

  const visible = useMemo(
    () => (tab ? loans.filter((l) => l.status === tab) : loans),
    [loans, tab]
  );

  function count(status: string) {
    return loans.filter((l) => l.status === status).length;
  }

  return (
    <main className="page">
      <h1>{t("loans.pipeline")}</h1>
      <p className="row-actions">
        {canWrite ? (
          <Link className="btn-primary" to="/credit/prets/nouveau">
            {t("loans.new")}
          </Link>
        ) : null}
        {canManageProducts ? (
          <Link className="btn-primary" to="/credit/produits">
            {t("nav.loanProducts")}
          </Link>
        ) : null}
        <Link className="btn-primary" to="/credit/recouvrement">
          {t("nav.collection")}
        </Link>
      </p>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}
      <p className="row-actions">
        <button type="button" className={tab === "" ? "btn-primary" : "btn-ghost"} onClick={() => setTab("")}>
          {t("loans.all")} ({loans.length})
        </button>
        {PIPELINE.map((status) => (
          <button
            key={status}
            type="button"
            className={tab === status ? "btn-primary" : "btn-ghost"}
            onClick={() => setTab(status)}
          >
            {t(`loans.status.${status}`)} ({count(status)})
          </button>
        ))}
      </p>
      <div className="table-wrap">
        <table className="data-table data-table--static">
          <thead>
            <tr>
              <th>{t("loans.loanNo")}</th>
              <th>{t("loans.member")}</th>
              <th>{t("loans.product")}</th>
              <th>{t("loans.principal")}</th>
              <th>{t("loans.totalDue")}</th>
              <th>{t("loans.status")}</th>
            </tr>
          </thead>
          <tbody>
            {visible.length === 0 ? (
              <tr>
                <td colSpan={6}>{t("loans.empty")}</td>
              </tr>
            ) : (
              visible.map((loan) => (
                <tr key={loan.id}>
                  <td>
                    <Link to={`/credit/prets/${loan.id}`}>{loan.loanNo}</Link>
                  </td>
                  <td>
                    {loan.memberNo} — {loan.memberName}
                  </td>
                  <td>{loan.productDisplayName}</td>
                  <td>{formatMoney(loan.principal, loan.currencyCode)}</td>
                  <td>{formatMoney(loan.totalDue, loan.currencyCode)}</td>
                  <td>{t(`loans.status.${loan.status}`, { defaultValue: loan.status })}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </main>
  );
}
