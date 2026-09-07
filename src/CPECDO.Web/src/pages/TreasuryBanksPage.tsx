import { FormEvent, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  createBank,
  deactivateBank,
  fetchBankNames,
  fetchBanks,
  fetchVaults,
  updateBank,
  type BankAccount,
  type VaultBalance
} from "../api/treasury";
import { formatMoney } from "../money";

const emptyForm = {
  bankName: "Sogebank",
  customBankName: "",
  accountNumber: "",
  currencyCode: "HTG",
  label: "",
  glCode: "1110",
  notes: ""
};

export function TreasuryBanksPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const roles = session?.roles.map((r) => r.name) ?? [];
  const canManage = roles.some((r) => r === "Admin" || r === "Gerant");
  const [vaults, setVaults] = useState<VaultBalance[]>([]);
  const [banks, setBanks] = useState<BankAccount[]>([]);
  const [names, setNames] = useState<string[]>(["Sogebank", "Unibank", "BNC", "BUH", "Capital Bank", "Autre"]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState(emptyForm);

  async function refresh() {
    const [nextVaults, nextBanks, nextNames] = await Promise.all([fetchVaults(), fetchBanks(), fetchBankNames()]);
    setVaults(nextVaults);
    setBanks(nextBanks);
    setNames(nextNames);
  }

  useEffect(() => {
    void refresh().catch((err: unknown) => setError(err instanceof Error ? err.message : t("treasury.error")));
  }, [t]);

  function onCurrency(currencyCode: string) {
    setForm((current) => ({
      ...current,
      currencyCode,
      glCode: currencyCode === "USD" ? "1120" : current.glCode === "1120" ? "1110" : current.glCode
    }));
  }

  function startEdit(bank: BankAccount) {
    setEditingId(bank.id);
    setForm({
      bankName: bank.bankName,
      customBankName: bank.customBankName ?? "",
      accountNumber: bank.accountNumber,
      currencyCode: bank.currencyCode,
      label: bank.label ?? "",
      glCode: bank.glCode,
      notes: bank.notes ?? ""
    });
  }

  function resetForm() {
    setEditingId(null);
    setForm(emptyForm);
  }

  async function onSave(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const body = {
      bankName: form.bankName,
      customBankName: form.bankName === "Autre" ? form.customBankName : undefined,
      accountNumber: form.accountNumber,
      currencyCode: form.currencyCode,
      label: form.label || undefined,
      glCode: form.glCode || undefined,
      notes: form.notes || undefined,
      isActive: true
    };
    try {
      if (editingId) await updateBank(editingId, body);
      else await createBank(body);
      resetForm();
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("treasury.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onDeactivate(id: string) {
    setBusy(true);
    setError(null);
    try {
      await deactivateBank(id);
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("treasury.error"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="page">
      <h1>{t("treasury.banksTitle")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}

      <section>
        <h2>{t("treasury.vaults")}</h2>
        <div className="table-wrap">
          <table className="data-table data-table--static">
            <thead>
              <tr>
                <th>{t("treasury.gl")}</th>
                <th>{t("treasury.name")}</th>
                <th>{t("treasury.currency")}</th>
                <th>{t("treasury.balance")}</th>
              </tr>
            </thead>
            <tbody>
              {vaults.length === 0 ? (
                <tr>
                  <td colSpan={4}>{t("treasury.emptyVaults")}</td>
                </tr>
              ) : (
                vaults.map((vault) => (
                  <tr key={vault.glCode}>
                    <td>{vault.glCode}</td>
                    <td>{vault.name}</td>
                    <td>{vault.currencyCode}</td>
                    <td>{formatMoney(vault.balance, vault.currencyCode)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>

      {canManage ? (
        <form className="stack-form" onSubmit={(event) => void onSave(event)}>
          <h2>{editingId ? t("treasury.editBank") : t("treasury.createBank")}</h2>
          <div className="form-grid">
            <label>
              {t("treasury.bank")}
              <select value={form.bankName} onChange={(e) => setForm({ ...form, bankName: e.target.value })}>
                {names.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            </label>
            {form.bankName === "Autre" ? (
              <label>
                {t("treasury.customBank")}
                <input
                  value={form.customBankName}
                  onChange={(e) => setForm({ ...form, customBankName: e.target.value })}
                  required
                />
              </label>
            ) : (
              <label>
                {t("treasury.label")}
                <input
                  value={form.label}
                  onChange={(e) => setForm({ ...form, label: e.target.value })}
                  placeholder={t("treasury.labelPlaceholder")}
                />
              </label>
            )}
            {form.bankName === "Autre" ? (
              <label>
                {t("treasury.label")}
                <input
                  value={form.label}
                  onChange={(e) => setForm({ ...form, label: e.target.value })}
                  placeholder={t("treasury.labelPlaceholder")}
                />
              </label>
            ) : null}
            <label>
              {t("treasury.number")}
              <input
                value={form.accountNumber}
                onChange={(e) => setForm({ ...form, accountNumber: e.target.value })}
                required
              />
            </label>
            <label>
              {t("treasury.currency")}
              <select value={form.currencyCode} onChange={(e) => onCurrency(e.target.value)}>
                <option value="HTG">HTG</option>
                <option value="USD">USD</option>
              </select>
            </label>
            <label>
              {t("treasury.gl")}
              <input value={form.glCode} onChange={(e) => setForm({ ...form, glCode: e.target.value })} />
            </label>
            <label className="span-2">
              {t("treasury.notes")}
              <input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
            </label>
          </div>
          <div className="row-actions">
            <button type="submit" disabled={busy}>
              {busy ? t("treasury.saving") : editingId ? t("treasury.saveBank") : t("treasury.createBank")}
            </button>
            {editingId ? (
              <button type="button" className="btn-ghost" onClick={resetForm}>
                {t("treasury.cancel")}
              </button>
            ) : null}
          </div>
        </form>
      ) : null}

      <section>
        <h2>{t("treasury.banks")}</h2>
        <div className="table-wrap">
          <table className="data-table data-table--static">
            <thead>
              <tr>
                <th>{t("treasury.label")}</th>
                <th>{t("treasury.bank")}</th>
                <th>{t("treasury.number")}</th>
                <th>{t("treasury.currency")}</th>
                <th>{t("treasury.gl")}</th>
                <th>{t("treasury.balance")}</th>
                <th>{t("treasury.status")}</th>
                {canManage ? <th /> : null}
              </tr>
            </thead>
            <tbody>
              {banks.length === 0 ? (
                <tr>
                  <td colSpan={canManage ? 8 : 7}>{t("treasury.noBanks")}</td>
                </tr>
              ) : (
                banks.map((bank) => (
                  <tr key={bank.id}>
                    <td>{bank.label ?? "—"}</td>
                    <td>{bank.displayBankName}</td>
                    <td>{canManage ? bank.accountNumber : bank.accountNumberMasked}</td>
                    <td>{bank.currencyCode}</td>
                    <td>{bank.glCode}</td>
                    <td>{formatMoney(bank.balance, bank.currencyCode)}</td>
                    <td>{bank.isActive ? t("treasury.active") : t("treasury.inactive")}</td>
                    {canManage ? (
                      <td>
                        <div className="row-actions">
                          <button type="button" disabled={busy} onClick={() => startEdit(bank)}>
                            {t("treasury.editBank")}
                          </button>
                          {bank.isActive ? (
                            <button type="button" className="btn-ghost" disabled={busy} onClick={() => void onDeactivate(bank.id)}>
                              {t("treasury.deactivate")}
                            </button>
                          ) : null}
                        </div>
                      </td>
                    ) : null}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>
    </main>
  );
}
