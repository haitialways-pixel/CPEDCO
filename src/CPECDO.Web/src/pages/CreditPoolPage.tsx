import { FormEvent, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { fetchCreditPool, fundCreditPool, type CreditPool } from "../api/creditPool";
import { fetchBanks, type BankAccount } from "../api/treasury";
import { formatMoney } from "../money";

export function CreditPoolPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const canFund = (session?.roles.map((r) => r.name) ?? []).some((r) => r === "Admin" || r === "Gerant");
  const [pool, setPool] = useState<CreditPool | null>(null);
  const [banks, setBanks] = useState<BankAccount[]>([]);
  const [source, setSource] = useState("Vault");
  const [bankId, setBankId] = useState("");
  const [amount, setAmount] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function load() {
    const next = await fetchCreditPool("HTG");
    setPool(next);
  }

  useEffect(() => {
    void load().catch((err: unknown) => setError(err instanceof Error ? err.message : t("loans.error")));
    if (canFund) {
      void fetchBanks()
        .then(setBanks)
        .catch(() => setBanks([]));
    }
  }, [canFund, t]);

  async function onFund(event: FormEvent) {
    event.preventDefault();
    const value = Number(amount);
    if (!Number.isFinite(value) || value <= 0) return;
    setBusy(true);
    setError(null);
    try {
      const next = await fundCreditPool({
        sourceKind: source,
        bankAccountId: source === "Bank" ? bankId : undefined,
        amount: value,
        currencyCode: "HTG"
      });
      setPool(next);
      setAmount("");
    } catch (err) {
      setError(err instanceof Error ? err.message : t("loans.error"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="page">
      <h1>{t("nav.creditPool")}</h1>
      <p className="muted">{t("creditPool.hint")}</p>
      {error ? <p className="login-form__error">{error}</p> : null}
      {pool ? (
        <section className="facts">
          <article>
            <span>{t("creditPool.funded")}</span>
            <strong className="tabular-nums">{formatMoney(pool.fundedTotal, pool.currencyCode)}</strong>
          </article>
          <article>
            <span>{t("creditPool.disbursed")}</span>
            <strong className="tabular-nums">{formatMoney(pool.disbursedTotal, pool.currencyCode)}</strong>
          </article>
          <article>
            <span>{t("creditPool.reserved")}</span>
            <strong className="tabular-nums">{formatMoney(pool.reservedApprovals, pool.currencyCode)}</strong>
          </article>
          <article>
            <span>{t("creditPool.available")}</span>
            <strong className="tabular-nums">{formatMoney(pool.availableToLend, pool.currencyCode)}</strong>
          </article>
        </section>
      ) : null}
      {canFund ? (
        <form className="stack-form" onSubmit={(e) => void onFund(e)}>
          <h2>{t("creditPool.fund")}</h2>
          <label>
            {t("creditPool.source")}
            <select value={source} onChange={(e) => setSource(e.target.value)}>
              <option value="Vault">{t("loans.tillShortVault")}</option>
              <option value="Bank">{t("nav.treasuryBanksNav")}</option>
            </select>
          </label>
          {source === "Bank" ? (
            <label>
              {t("nav.treasuryBanksNav")}
              <select value={bankId} onChange={(e) => setBankId(e.target.value)} required>
                <option value="">{t("loans.selectSavings")}</option>
                {banks.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.pickerLabel}
                  </option>
                ))}
              </select>
            </label>
          ) : null}
          <label>
            {t("loans.repayAmount")}
            <input type="number" min="0.01" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required />
          </label>
          <button type="submit" disabled={busy}>
            {busy ? t("loans.saving") : t("creditPool.fund")}
          </button>
        </form>
      ) : (
        <p className="muted">{t("creditPool.tellerDenied")}</p>
      )}
      {pool && pool.movements.length > 0 ? (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>{t("reports.date")}</th>
                <th>{t("reports.type")}</th>
                <th>{t("reports.amount")}</th>
              </tr>
            </thead>
            <tbody>
              {pool.movements.map((m) => (
                <tr key={m.id}>
                  <td>{m.createdAtUtc.slice(0, 10)}</td>
                  <td>
                    {m.kind} · {m.sourceKind}
                  </td>
                  <td>{formatMoney(m.amount, m.currencyCode)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </main>
  );
}
