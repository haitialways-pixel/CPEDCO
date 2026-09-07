import { FormEvent, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  downloadStatementPdf,
  fetchSavingsAccount,
  fetchStatement,
  type SavingsAccount,
  type SavingsStatement
} from "../api/savings";

import { formatMoney } from "../money";

function money(value: number, currency: string) {
  return formatMoney(value, currency);
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

export function SavingsStatementPage() {
  const { id } = useParams();
  const { t } = useTranslation();
  const [account, setAccount] = useState<SavingsAccount | null>(null);
  const [statement, setStatement] = useState<SavingsStatement | null>(null);
  const [from, setFrom] = useState("2026-01-01");
  const [to, setTo] = useState(today());
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!id) return;
    void fetchSavingsAccount(id)
      .then(setAccount)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("savings.loadError")));
  }, [id, t]);

  async function loadStatement(event?: FormEvent) {
    event?.preventDefault();
    if (!id) return;
    setBusy(true);
    setError(null);
    try {
      setStatement(await fetchStatement(id, from, to));
    } catch (err) {
      setError(err instanceof Error ? err.message : t("savings.loadError"));
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    void loadStatement();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  if (!account && !error) return <main className="page">{t("members.loading")}</main>;

  return (
    <main className="page">
      <p>
        {account ? <Link to={`/members/${account.memberId}`}>{t("members.back")}</Link> : <Link to="/members">{t("members.back")}</Link>}
      </p>
      {error ? <p className="login-form__error">{error}</p> : null}
      {account ? (
        <>
          <div className="page__head">
            <div>
              <p className="eyebrow">{account.accountNo}</p>
              <h1>{account.productName}</h1>
            </div>
          </div>
          <section className="facts">
            <article>
              <span>{t("savings.ledger")}</span>
              <strong>{money(account.ledgerBalance, account.currencyCode)}</strong>
            </article>
            <article>
              <span>{t("savings.available")}</span>
              <strong>{money(account.availableBalance, account.currencyCode)}</strong>
            </article>
            <article>
              <span>{t("savings.currency")}</span>
              <strong>{account.currencyCode}</strong>
            </article>
          </section>
          <p className="muted">{t("savings.noMovements")}</p>
          <form className="search-bar" onSubmit={(e) => void loadStatement(e)}>
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            <button type="submit" disabled={busy}>
              {busy ? t("savings.loading") : t("savings.statement")}
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                if (!id) return;
                void downloadStatementPdf(id, from, to).catch((err: unknown) =>
                  setError(err instanceof Error ? err.message : t("savings.loadError"))
                );
              }}
            >
              {t("service.pdf")}
            </button>
          </form>
          {statement ? (
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>{t("savings.date")}</th>
                    <th>{t("savings.description")}</th>
                    <th>{t("savings.type")}</th>
                    <th>{t("savings.amount")}</th>
                    <th>{t("savings.running")}</th>
                  </tr>
                </thead>
                <tbody>
                  {statement.entries.length === 0 ? (
                    <tr>
                      <td colSpan={5}>{t("savings.statementEmpty")}</td>
                    </tr>
                  ) : (
                    statement.entries.map((entry, index) => (
                      <tr key={`${entry.postedAtUtc}-${index}`}>
                        <td>{entry.valueDateUtc.slice(0, 10)}</td>
                        <td>{entry.description}</td>
                        <td>{entry.entryType}</td>
                        <td>{money(entry.amount, statement.currencyCode)}</td>
                        <td>{money(entry.runningBalance, statement.currencyCode)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          ) : null}
        </>
      ) : null}
    </main>
  );
}
