import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { fetchTransfers, type TreasuryTransfer } from "../api/treasury";
import { formatMoney } from "../money";

export function TreasuryMovementsPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const canCreate = session?.roles.some((r) => r.name === "Admin" || r.name === "Gerant") ?? false;
  const [transfers, setTransfers] = useState<TreasuryTransfer[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void fetchTransfers()
      .then(setTransfers)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("treasury.error")));
  }, [t]);

  return (
    <main className="page">
      <h1>{t("treasury.transfersTitle")}</h1>
      {canCreate ? (
        <p>
          <Link className="btn-primary" to="/tresorerie/mouvements/nouveau">
            {t("treasury.create")}
          </Link>
        </p>
      ) : null}
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th>{t("treasury.date")}</th>
              <th>{t("treasury.direction")}</th>
              <th>{t("treasury.amount")}</th>
              <th>{t("treasury.status")}</th>
              <th>{t("treasury.initiator")}</th>
              <th>{t("treasury.slip")}</th>
            </tr>
          </thead>
          <tbody>
            {transfers.length === 0 ? (
              <tr>
                <td colSpan={6}>{t("treasury.empty")}</td>
              </tr>
            ) : (
              transfers.map((item) => (
                <tr key={item.id}>
                  <td>
                    <Link to={`/tresorerie/mouvements/${item.id}`}>
                      {(item.executedAtUtc ?? item.createdAtUtc).slice(0, 10)}
                    </Link>
                  </td>
                  <td>{t(`treasury.direction.${item.direction}`)}</td>
                  <td>{formatMoney(item.amount, item.currencyCode)}</td>
                  <td>
                    <span className={`status-badge status-badge--${item.status.toLowerCase()}`}>
                      {t(`treasury.status.${item.status}`)}
                    </span>
                  </td>
                  <td>{item.initiatedByName}</td>
                  <td>{item.bankSlipRef ?? ""}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </main>
  );
}
