import { FormEvent, useEffect, useMemo, useState } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { searchMembers, type MemberSummary } from "../api/members";
import {
  createLoanDraft,
  fetchLoanProducts,
  previewLoanSchedule,
  previewPayoffQuote,
  type LoanProduct,
  type LoanSchedule,
  type PayoffQuote
} from "../api/loans";
import { formatMoney } from "../money";

export function LoanNewPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const canWrite = session?.roles.some((r) => ["Admin", "Gerant", "OfficierCredit"].includes(r.name)) ?? false;
  const canManageProducts = session?.roles.some((r) => r.name === "Admin" || r.name === "Gerant") ?? false;
  const preselected = params.get("productId") ?? "";

  const [products, setProducts] = useState<LoanProduct[]>([]);
  const [productId, setProductId] = useState(preselected);
  const [principal, setPrincipal] = useState("");
  const [rate, setRate] = useState("20");
  const [query, setQuery] = useState("");
  const [members, setMembers] = useState<MemberSummary[]>([]);
  const [member, setMember] = useState<MemberSummary | null>(null);
  const [schedule, setSchedule] = useState<LoanSchedule | null>(null);
  const [quote, setQuote] = useState<PayoffQuote | null>(null);
  const [daysElapsed, setDaysElapsed] = useState("45");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const selectedProduct = useMemo(
    () => products.find((p) => p.id === productId) ?? null,
    [products, productId]
  );

  useEffect(() => {
    void fetchLoanProducts(true)
      .then((list) => {
        setProducts(list);
        setProductId((current) => {
          if (current && list.some((p) => p.id === current)) return current;
          if (preselected && list.some((p) => p.id === preselected)) return preselected;
          return list[0]?.id ?? "";
        });
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("loans.error")));
  }, [t, preselected]);

  useEffect(() => {
    if (!selectedProduct) return;
    setRate(String(selectedProduct.defaultRatePercent));
  }, [selectedProduct?.id]);

  if (!canWrite) return <Navigate to="/credit/prets" replace />;

  async function onSearch(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await searchMembers(query);
      setMembers(result.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("loans.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onPreview() {
    if (!productId) return;
    setBusy(true);
    setError(null);
    try {
      const next = await previewLoanSchedule({
        productId,
        principal: Number(principal),
        agreedRatePercent: Number(rate)
      });
      setSchedule(next);
    } catch (err) {
      setSchedule(null);
      setError(err instanceof Error ? err.message : t("loans.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onQuote() {
    if (!productId) return;
    setBusy(true);
    setError(null);
    try {
      const next = await previewPayoffQuote(
        { productId, principal: Number(principal), agreedRatePercent: Number(rate) },
        Number(daysElapsed)
      );
      setQuote(next);
    } catch (err) {
      setQuote(null);
      setError(err instanceof Error ? err.message : t("loans.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onSave(event: FormEvent) {
    event.preventDefault();
    if (!member || !productId) return;
    setBusy(true);
    setError(null);
    try {
      const created = await createLoanDraft({
        memberId: member.id,
        productId,
        principal: Number(principal),
        agreedRatePercent: Number(rate)
      });
      navigate(`/credit/prets/${created.id}`);
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
      <h1>{t("loans.new")}</h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}

      <form className="search-bar" onSubmit={(event) => void onSearch(event)}>
        <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t("members.searchPlaceholder")} />
        <button type="submit" disabled={busy}>
          {t("members.search")}
        </button>
      </form>
      {members.length > 0 ? (
        <ul className="coming-soon ul-reset">
          {members.map((item) => (
            <li key={item.id}>
              <button type="button" className="btn-ghost" onClick={() => setMember(item)}>
                {item.memberNo} — {item.fullName}
              </button>
            </li>
          ))}
        </ul>
      ) : null}
      {member ? (
        <p>
          <strong>{member.fullName}</strong> ({member.memberNo})
        </p>
      ) : (
        <p className="muted">{t("loans.memberSearch")}</p>
      )}

      <form className="stack-form" onSubmit={(event) => void onSave(event)}>
        <div className="form-grid">
          <label>
            {t("loans.product")}
            <select
              value={productId}
              onChange={(e) => setProductId(e.target.value)}
              required
              disabled={products.length === 0}
            >
              {products.length === 0 ? <option value="">{t("loans.noProducts")}</option> : null}
              {products.map((product) => (
                <option key={product.id} value={product.id}>
                  {product.displayName} ({product.code})
                </option>
              ))}
            </select>
          </label>
          {canManageProducts ? (
            <div>
              <button
                type="button"
                className="btn-ghost"
                onClick={() =>
                  navigate(`/credit/produits?returnTo=${encodeURIComponent("/credit/prets/nouveau")}`)
                }
              >
                {t("loans.createProduct")}
              </button>
            </div>
          ) : null}
          <label>
            {t("loans.principal")}
            <input
              type="number"
              min="0.01"
              step="0.01"
              value={principal}
              onChange={(e) => setPrincipal(e.target.value)}
              required
            />
          </label>
          <label>
            {t("loans.agreedRate")}
            <input
              type="number"
              min="0"
              step="0.01"
              value={rate}
              onChange={(e) => setRate(e.target.value)}
              required
            />
          </label>
        </div>
        <p className="muted">{t("loans.rateHint")}</p>
        {selectedProduct ? (
          <p className="muted">
            {t("loans.interestMethod.FlatOnOriginalPrincipalForTerm")} · {selectedProduct.termDays}{" "}
            {t("loans.days")} · {selectedProduct.installmentCount} × {t("loans.frequency.Weekly")}
          </p>
        ) : null}
        <div className="row-actions">
          <button type="button" className="btn-ghost" disabled={busy || !productId} onClick={() => void onPreview()}>
            {t("loans.preview")}
          </button>
          <button type="submit" disabled={busy || !member || !productId}>
            {busy ? t("loans.saving") : t("loans.saveDraft")}
          </button>
        </div>
      </form>

      {schedule ? (
        <section>
          <h2>{t("loans.schedule")}</h2>
          <p>
            {t("loans.totalInterest")}: <strong>{formatMoney(schedule.totalInterest, "HTG")}</strong>
            {" · "}
            {t("loans.totalDue")}: <strong>{formatMoney(schedule.totalDue, "HTG")}</strong>
          </p>
          <ScheduleTable schedule={schedule} />
        </section>
      ) : null}

      <section>
        <h2>{t("loans.payoffQuote")}</h2>
        <div className="search-bar">
          <label>
            {t("loans.daysElapsed")}
            <input type="number" min={0} value={daysElapsed} onChange={(e) => setDaysElapsed(e.target.value)} />
          </label>
          <button type="button" disabled={busy || !productId} onClick={() => void onQuote()}>
            {t("loans.payoffQuote")}
          </button>
        </div>
        {quote ? (
          <p>
            {t("loans.interestDueNow")}: <strong>{formatMoney(quote.interestDue, "HTG")}</strong>
            {" · "}
            {t("loans.payoffTotal")}: <strong>{formatMoney(quote.totalDue, "HTG")}</strong>
          </p>
        ) : null}
      </section>
    </main>
  );
}

function ScheduleTable({ schedule }: { schedule: LoanSchedule }) {
  const { t } = useTranslation();
  return (
    <div className="table-wrap">
      <table className="data-table data-table--static">
        <thead>
          <tr>
            <th>{t("loans.lineNo")}</th>
            <th>{t("loans.dueDate")}</th>
            <th>{t("loans.principalDue")}</th>
            <th>{t("loans.interestDue")}</th>
            <th>{t("loans.installmentTotal")}</th>
          </tr>
        </thead>
        <tbody>
          {schedule.installments.map((line) => (
            <tr key={line.lineNo}>
              <td>{line.lineNo}</td>
              <td>{line.dueDate}</td>
              <td>{formatMoney(line.principalDue, "HTG")}</td>
              <td>{formatMoney(line.interestDue, "HTG")}</td>
              <td>{formatMoney(line.totalDue, "HTG")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
