import { FormEvent, useEffect, useMemo, useState } from "react";
import { Link, Navigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  createSavingsProduct,
  deactivateSavingsProduct,
  fetchSavingsProductCatalog,
  updateSavingsProduct,
  type SaveSavingsProduct,
  type SavingsProduct
} from "../api/savings";

const emptyForm = {
  code: "",
  legalName: "",
  commercialName: "",
  currencyCode: "HTG",
  productKind: "AVue",
  termDays: "",
  interestRatePercent: "0",
  interestMethod: "None",
  minOpeningAmount: "0",
  minimumBalance: "0",
  allowWithdrawBeforeTerm: "false",
  isActive: "true"
};

export function SavingsProductsPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const canManage = session?.roles.some((r) => r.name === "Admin" || r.name === "Gerant") ?? false;
  const [products, setProducts] = useState<SavingsProduct[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState(emptyForm);
  const editing = useMemo(() => products.find((p) => p.id === editingId) ?? null, [products, editingId]);

  async function refresh() {
    setProducts(await fetchSavingsProductCatalog(false));
  }

  useEffect(() => {
    void refresh().catch((err: unknown) => setError(err instanceof Error ? err.message : t("savings.productError")));
  }, [t]);

  if (!canManage) return <Navigate to="/membres" replace />;

  function set(name: string, value: string) {
    setForm((current) => ({ ...current, [name]: value }));
  }

  function startEdit(product: SavingsProduct) {
    setEditingId(product.id);
    setForm({
      code: product.code,
      legalName: product.legalName,
      commercialName: product.commercialName ?? "",
      currencyCode: product.currencyCode,
      productKind: product.productKind,
      termDays: product.termDays == null ? "" : String(product.termDays),
      interestRatePercent: String(product.interestRatePercent ?? 0),
      interestMethod: product.interestMethod || "None",
      minOpeningAmount: String(product.minOpeningAmount ?? 0),
      minimumBalance: String(product.minimumBalance ?? 0),
      allowWithdrawBeforeTerm: product.allowWithdrawBeforeTerm ? "true" : "false",
      isActive: product.isActive ? "true" : "false"
    });
  }

  function resetForm() {
    setEditingId(null);
    setForm(emptyForm);
  }

  function body(): SaveSavingsProduct {
    const kind = form.productKind;
    return {
      code: form.code,
      legalName: form.legalName,
      commercialName: form.commercialName.trim() || null,
      currencyCode: form.currencyCode,
      productKind: kind,
      termDays: kind === "AVue" || !form.termDays ? null : Number(form.termDays),
      interestRatePercent: Number(form.interestRatePercent || 0),
      interestMethod: Number(form.interestRatePercent || 0) === 0 ? "None" : form.interestMethod,
      minOpeningAmount: Number(form.minOpeningAmount || 0),
      minimumBalance: Number(form.minimumBalance || 0),
      allowWithdrawBeforeTerm: kind === "Terme" && form.allowWithdrawBeforeTerm === "true",
      isActive: form.isActive === "true"
    };
  }

  async function onSave(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (editingId) await updateSavingsProduct(editingId, body());
      else await createSavingsProduct(body());
      resetForm();
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("savings.productError"));
    } finally {
      setBusy(false);
    }
  }

  async function onDeactivate(id: string) {
    setBusy(true);
    setError(null);
    try {
      await deactivateSavingsProduct(id);
      if (editingId === id) resetForm();
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("savings.productError"));
    } finally {
      setBusy(false);
    }
  }

  const termVisible = form.productKind === "Terme" || form.productKind === "Autre";

  return (
    <main className="page">
      <p>
        <Link to="/membres">{t("members.back")}</Link>
      </p>
      <h1>{t("savings.productsTitle")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}

      <form className="stack-form" onSubmit={(event) => void onSave(event)}>
        <h2>{editing ? t("savings.editProduct") : t("savings.createProduct")}</h2>
        <div className="form-grid">
          <label>
            {t("savings.code")}
            <input value={form.code} onChange={(e) => set("code", e.target.value)} required={!editing} disabled={Boolean(editing)} maxLength={32} />
          </label>
          <label>
            {t("savings.legalName")}
            <input value={form.legalName} onChange={(e) => set("legalName", e.target.value)} required maxLength={128} />
          </label>
          <label>
            {t("savings.commercialName")}
            <input value={form.commercialName} onChange={(e) => set("commercialName", e.target.value)} maxLength={128} />
          </label>
          <label>
            {t("savings.currency")}
            <select value={form.currencyCode} onChange={(e) => set("currencyCode", e.target.value)}>
              <option value="HTG">HTG</option>
              <option value="USD">USD</option>
            </select>
          </label>
          <label>
            {t("savings.kind")}
            <select value={form.productKind} onChange={(e) => set("productKind", e.target.value)}>
              <option value="AVue">{t("savings.kind.AVue")}</option>
              <option value="Terme">{t("savings.kind.Terme")}</option>
              <option value="Autre">{t("savings.kind.Autre")}</option>
            </select>
          </label>
          {termVisible ? (
            <label>
              {t("savings.termDays")}
              <input type="number" min={1} value={form.termDays} onChange={(e) => set("termDays", e.target.value)} required={form.productKind === "Terme"} />
            </label>
          ) : null}
          {termVisible ? (
            <label>
              {t("savings.allowEarlyWithdraw")}
              <select value={form.allowWithdrawBeforeTerm} onChange={(e) => set("allowWithdrawBeforeTerm", e.target.value)}>
                <option value="false">{t("loans.no")}</option>
                <option value="true">{t("loans.yes")}</option>
              </select>
            </label>
          ) : null}
          <label>
            {t("savings.rate")}
            <input type="number" min={0} step="0.01" value={form.interestRatePercent} onChange={(e) => set("interestRatePercent", e.target.value)} />
          </label>
          <label>
            {t("savings.interestMethod")}
            <select value={form.interestMethod} onChange={(e) => set("interestMethod", e.target.value)}>
              <option value="None">{t("savings.interest.None")}</option>
              <option value="SimpleAtMaturity">{t("savings.interest.SimpleAtMaturity")}</option>
            </select>
          </label>
          <label>
            {t("savings.minOpening")}
            <input type="number" min={0} step="0.01" value={form.minOpeningAmount} onChange={(e) => set("minOpeningAmount", e.target.value)} />
          </label>
          <label>
            {t("savings.minBalance")}
            <input type="number" min={0} step="0.01" value={form.minimumBalance} onChange={(e) => set("minimumBalance", e.target.value)} />
          </label>
          <label>
            {t("loans.status")}
            <select value={form.isActive} onChange={(e) => set("isActive", e.target.value)}>
              <option value="true">{t("loans.active")}</option>
              <option value="false">{t("loans.inactive")}</option>
            </select>
          </label>
        </div>
        <p className="muted">{t("savings.rateHint")}</p>
        <div className="row-actions">
          <button type="submit" disabled={busy}>
            {busy ? t("loans.saving") : t("loans.save")}
          </button>
          {editing ? (
            <button type="button" className="btn-ghost" onClick={resetForm}>
              {t("loans.cancel")}
            </button>
          ) : null}
        </div>
      </form>

      <section>
        <h2>{t("savings.productsList")}</h2>
        {products.length === 0 ? <p className="muted">{t("savings.productsEmpty")}</p> : null}
        {products.map((product) => (
          <article key={product.id} className="staff-row">
            <div>
              <strong>{product.displayName}</strong>
              <span>
                {product.code} · {product.productKind} · {product.currencyCode} ·{" "}
                {product.isActive ? t("loans.active") : t("loans.inactive")}
              </span>
            </div>
            <div>
              <button type="button" className="btn-ghost" disabled={busy} onClick={() => startEdit(product)}>
                {t("loans.editProduct")}
              </button>
              {product.isActive ? (
                <button type="button" className="btn-ghost" disabled={busy} onClick={() => void onDeactivate(product.id)}>
                  {t("loans.deactivate")}
                </button>
              ) : null}
            </div>
          </article>
        ))}
      </section>
    </main>
  );
}
