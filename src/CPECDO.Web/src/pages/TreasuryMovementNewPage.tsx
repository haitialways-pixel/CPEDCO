import { FormEvent, useEffect, useMemo, useState } from "react";
import { Link, Navigate, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { createTransfer, fetchBanks, type BankAccount } from "../api/treasury";

export function TreasuryMovementNewPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const navigate = useNavigate();
  const canCreate = session?.roles.some((r) => r.name === "Admin" || r.name === "Gerant") ?? false;
  const [banks, setBanks] = useState<BankAccount[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({
    direction: "VaultToBank",
    currencyCode: "HTG",
    amount: "",
    bankAccountId: "",
    notes: ""
  });

  const selectable = useMemo(
    () => banks.filter((b) => b.isActive && b.currencyCode === form.currencyCode),
    [banks, form.currencyCode]
  );

  useEffect(() => {
    void fetchBanks()
      .then(setBanks)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("treasury.error")));
  }, [t]);

  useEffect(() => {
    setForm((current) => {
      const match = selectable.find((b) => b.id === current.bankAccountId);
      if (match) return current;
      return { ...current, bankAccountId: selectable[0]?.id ?? "" };
    });
  }, [selectable]);

  if (!canCreate) return <Navigate to="/tresorerie/mouvements" replace />;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const created = await createTransfer({
        direction: form.direction,
        amount: Number(form.amount),
        currencyCode: form.currencyCode,
        bankAccountId: form.bankAccountId,
        notes: form.notes.trim() || undefined
      });
      navigate(`/tresorerie/mouvements/${created.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("treasury.error"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="page">
      <p>
        <Link to="/tresorerie/mouvements">{t("treasury.backToList")}</Link>
      </p>
      <h1>{t("treasury.create")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}
      <form className="stack-form" onSubmit={(event) => void onSubmit(event)}>
        <div className="form-grid">
          <label>
            {t("treasury.direction")}
            <select value={form.direction} onChange={(e) => setForm({ ...form, direction: e.target.value })}>
              <option value="VaultToBank">{t("treasury.direction.VaultToBank")}</option>
              <option value="BankToVault">{t("treasury.direction.BankToVault")}</option>
            </select>
          </label>
          <label>
            {t("treasury.currency")}
            <select value={form.currencyCode} onChange={(e) => setForm({ ...form, currencyCode: e.target.value })}>
              <option value="HTG">HTG</option>
              <option value="USD">USD</option>
            </select>
          </label>
          <label>
            {t("treasury.amount")}
            <input
              type="number"
              min="0.01"
              step="0.01"
              value={form.amount}
              onChange={(e) => setForm({ ...form, amount: e.target.value })}
              required
            />
          </label>
          <label>
            {t("treasury.bankAccount")}
            <select
              value={form.bankAccountId}
              onChange={(e) => setForm({ ...form, bankAccountId: e.target.value })}
              required
            >
              {selectable.length === 0 ? (
                <option value="">{t("treasury.noSelectableBanks")}</option>
              ) : (
                selectable.map((bank) => (
                  <option key={bank.id} value={bank.id}>
                    {bank.pickerLabel}
                  </option>
                ))
              )}
            </select>
          </label>
          <label className="span-2">
            {t("treasury.notes")}
            <input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </label>
        </div>
        <button type="submit" disabled={busy || !form.amount || !form.bankAccountId}>
          {busy ? t("treasury.saving") : t("treasury.saveDraft")}
        </button>
      </form>
    </main>
  );
}
