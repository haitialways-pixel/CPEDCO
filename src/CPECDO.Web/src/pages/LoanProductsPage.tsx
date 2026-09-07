import { FormEvent, useEffect, useMemo, useState } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  createLoanProduct,
  deactivateLoanProduct,
  fetchLoanProducts,
  updateLoanProduct,
  type LoanProduct,
  type SaveLoanProduct
} from "../api/loans";
const emptyForm = {
  code: "",
  legalName: "",
  commercialName: "",
  smsName: "",
  termDays: "90",
  installmentCount: "12",
  repaymentFrequency: "Weekly",
  defaultRatePercent: "20",
  maxRenewals: "",
  compulsorySavingsPercent: "10",
  minPrincipal: "",
  maxPrincipal: "",
  officerMaxApproval: "",
  earlyPayoffChargesFullFlatInterest: "false",
  latePenaltyPercentPerDay: "0",
  isActive: "true"
};

function optionalNumber(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const n = Number(trimmed);
  return Number.isFinite(n) ? n : null;
}

function safeReturnTo(value: string | null): string | null {
  if (!value) return null;
  if (!value.startsWith("/credit/")) return null;
  if (value.includes("//") || value.includes("\\")) return null;
  return value;
}

export function LoanProductsPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const returnTo = safeReturnTo(params.get("returnTo"));
  const canManage = session?.roles.some((r) => r.name === "Admin" || r.name === "Gerant") ?? false;
  const [products, setProducts] = useState<LoanProduct[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState(emptyForm);

  const editing = useMemo(
    () => products.find((p) => p.id === editingId) ?? null,
    [products, editingId]
  );

  async function refresh() {
    setProducts(await fetchLoanProducts(false));
  }

  useEffect(() => {
    void refresh().catch((err: unknown) => setError(err instanceof Error ? err.message : t("loans.error")));
  }, [t]);

  if (!canManage) return <Navigate to="/credit/prets" replace />;

  function set(name: string, value: string) {
    setForm((current) => ({ ...current, [name]: value }));
  }

  function startEdit(product: LoanProduct) {
    setEditingId(product.id);
    setForm({
      code: product.code,
      legalName: product.legalName,
      commercialName: product.commercialName ?? "",
      smsName: product.smsName ?? "",
      termDays: String(product.termDays),
      installmentCount: String(product.installmentCount),
      repaymentFrequency: product.repaymentFrequency,
      defaultRatePercent: String(product.defaultRatePercent),
      maxRenewals: product.maxRenewals == null ? "" : String(product.maxRenewals),
      compulsorySavingsPercent: String(product.compulsorySavingsPercent),
      minPrincipal: product.minPrincipal == null ? "" : String(product.minPrincipal),
      maxPrincipal: product.maxPrincipal == null ? "" : String(product.maxPrincipal),
      officerMaxApproval: product.officerMaxApproval == null ? "" : String(product.officerMaxApproval),
      earlyPayoffChargesFullFlatInterest: product.earlyPayoffChargesFullFlatInterest ? "true" : "false",
      latePenaltyPercentPerDay: String(product.latePenaltyPercentPerDay ?? 0),
      isActive: product.isActive ? "true" : "false"
    });
  }

  function resetForm() {
    setEditingId(null);
    setForm(emptyForm);
  }

  function body(): SaveLoanProduct {
    return {
      code: form.code,
      legalName: form.legalName,
      commercialName: form.commercialName.trim() || null,
      smsName: form.smsName.trim() || null,
      termDays: Number(form.termDays),
      installmentCount: Number(form.installmentCount),
      repaymentFrequency: form.repaymentFrequency,
      defaultRatePercent: Number(form.defaultRatePercent),
      maxRenewals: optionalNumber(form.maxRenewals) === null ? null : Number(form.maxRenewals),
      compulsorySavingsPercent: Number(form.compulsorySavingsPercent),
      minPrincipal: optionalNumber(form.minPrincipal),
      maxPrincipal: optionalNumber(form.maxPrincipal),
      officerMaxApproval: optionalNumber(form.officerMaxApproval),
      earlyPayoffChargesFullFlatInterest: form.earlyPayoffChargesFullFlatInterest === "true",
      latePenaltyPercentPerDay: Number(form.latePenaltyPercentPerDay || 0),
      isActive: form.isActive === "true"
    };
  }

  async function onSave(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (editingId) {
        await updateLoanProduct(editingId, body());
        resetForm();
        await refresh();
      } else {
        const created = await createLoanProduct(body());
        resetForm();
        await refresh();
        if (returnTo) {
          navigate(`${returnTo}?productId=${created.id}`);
          return;
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : t("loans.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onDeactivate(id: string) {
    setBusy(true);
    setError(null);
    try {
      await deactivateLoanProduct(id);
      if (editingId === id) resetForm();
      await refresh();
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
      <h1>{t("loans.productsTitle")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}

      <form className="stack-form" onSubmit={(event) => void onSave(event)}>
        <h2>{editing ? t("loans.editProduct") : t("loans.createProduct")}</h2>
        <div className="form-grid">
          <label>
            {t("loans.code")}
            <input
              value={form.code}
              onChange={(e) => set("code", e.target.value)}
              required={!editing}
              disabled={Boolean(editing)}
              maxLength={32}
            />
          </label>
          <label>
            {t("loans.legalName")}
            <input value={form.legalName} onChange={(e) => set("legalName", e.target.value)} required maxLength={128} />
          </label>
          <label>
            {t("loans.commercialName")}
            <input value={form.commercialName} onChange={(e) => set("commercialName", e.target.value)} maxLength={128} />
          </label>
          <label>
            {t("loans.smsName")}
            <input value={form.smsName} onChange={(e) => set("smsName", e.target.value)} maxLength={20} />
          </label>
          <label>
            {t("loans.termDays")}
            <input type="number" min={1} value={form.termDays} onChange={(e) => set("termDays", e.target.value)} required />
          </label>
          <label>
            {t("loans.installmentCount")}
            <input
              type="number"
              min={1}
              value={form.installmentCount}
              onChange={(e) => set("installmentCount", e.target.value)}
              required
            />
          </label>
          <label>
            {t("loans.frequency")}
            <select value={form.repaymentFrequency} onChange={(e) => set("repaymentFrequency", e.target.value)}>
              <option value="Weekly">{t("loans.frequency.Weekly")}</option>
            </select>
          </label>
          <label>
            {t("loans.defaultRate")}
            <input
              type="number"
              min={0}
              step="0.01"
              value={form.defaultRatePercent}
              onChange={(e) => set("defaultRatePercent", e.target.value)}
              required
            />
          </label>
          <label>
            {t("loans.maxRenewals")}
            <input
              type="number"
              min={0}
              value={form.maxRenewals}
              onChange={(e) => set("maxRenewals", e.target.value)}
              placeholder={t("loans.unlimited")}
            />
          </label>
          <label>
            {t("loans.compulsorySavings")}
            <input
              type="number"
              min={0}
              step="0.01"
              value={form.compulsorySavingsPercent}
              onChange={(e) => set("compulsorySavingsPercent", e.target.value)}
              required
            />
          </label>
          <label>
            {t("loans.minPrincipal")}
            <input type="number" min={0} step="0.01" value={form.minPrincipal} onChange={(e) => set("minPrincipal", e.target.value)} />
          </label>
          <label>
            {t("loans.maxPrincipal")}
            <input type="number" min={0} step="0.01" value={form.maxPrincipal} onChange={(e) => set("maxPrincipal", e.target.value)} />
          </label>
          <label>
            {t("loans.officerMax")}
            <input
              type="number"
              min={0}
              step="0.01"
              value={form.officerMaxApproval}
              onChange={(e) => set("officerMaxApproval", e.target.value)}
            />
          </label>
          <label>
            {t("loans.latePenalty")}
            <input
              type="number"
              min={0}
              step="0.01"
              value={form.latePenaltyPercentPerDay}
              onChange={(e) => set("latePenaltyPercentPerDay", e.target.value)}
            />
          </label>
          <label>
            {t("loans.earlyPayoffFull")}
            <select
              value={form.earlyPayoffChargesFullFlatInterest}
              onChange={(e) => set("earlyPayoffChargesFullFlatInterest", e.target.value)}
            >
              <option value="false">{t("loans.no")}</option>
              <option value="true">{t("loans.yes")}</option>
            </select>
          </label>
          <label>
            {t("loans.status")}
            <select value={form.isActive} onChange={(e) => set("isActive", e.target.value)}>
              <option value="true">{t("loans.active")}</option>
              <option value="false">{t("loans.inactive")}</option>
            </select>
          </label>
        </div>
        <p className="muted">{t("loans.rateHint")}</p>
        <p className="muted">{t("loans.interestMethod.FlatOnOriginalPrincipalForTerm")}</p>
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
        <h2>{t("loans.productsTitle")}</h2>
        <div className="table-wrap">
          <table className="data-table data-table--static">
            <thead>
              <tr>
                <th>{t("loans.code")}</th>
                <th>{t("loans.product")}</th>
                <th>{t("loans.termDays")}</th>
                <th>{t("loans.installmentCount")}</th>
                <th>{t("loans.defaultRate")}</th>
                <th>{t("loans.status")}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {products.length === 0 ? (
                <tr>
                  <td colSpan={7}>{t("loans.productsEmpty")}</td>
                </tr>
              ) : (
                products.map((product) => (
                  <tr key={product.id}>
                    <td>{product.code}</td>
                    <td>{product.displayName}</td>
                    <td>{product.termDays}</td>
                    <td>{product.installmentCount}</td>
                    <td>{product.defaultRatePercent}</td>
                    <td>{product.isActive ? t("loans.active") : t("loans.inactive")}</td>
                    <td>
                      <div className="row-actions">
                        <button type="button" className="btn-ghost" onClick={() => startEdit(product)}>
                          {t("loans.editProduct")}
                        </button>
                        {product.isActive ? (
                          <button type="button" className="btn-ghost" onClick={() => void onDeactivate(product.id)}>
                            {t("loans.deactivate")}
                          </button>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>
      <p className="muted">{t("loans.moneyNote")}</p>
    </main>
  );
}
