import { FormEvent, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  attachSlip,
  cancelTransfer,
  executeTransfer,
  fetchSlipObjectUrl,
  fetchTransfer,
  involvesBank,
  type TreasuryTransfer
} from "../api/treasury";
import { formatMoney } from "../money";

function stampTime(iso: string | null | undefined) {
  if (!iso) return "";
  return new Date(iso).toLocaleString("fr-HT", {
    timeZone: "America/Port-au-Prince",
    dateStyle: "short",
    timeStyle: "short"
  });
}

export function TreasuryMovementDetailPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const { session } = useAuth();
  const userId = session?.user.id ?? "";
  const roles = session?.roles.map((r) => r.name) ?? [];
  const isGerant = roles.includes("Gerant");
  const isAdmin = roles.includes("Admin");

  const [item, setItem] = useState<TreasuryTransfer | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [slipRef, setSlipRef] = useState("");
  const [slipFile, setSlipFile] = useState<File | null>(null);
  const [slipPreview, setSlipPreview] = useState<string | null>(null);
  const [detailSlipUrl, setDetailSlipUrl] = useState<string | null>(null);
  const [executeOpen, setExecuteOpen] = useState(false);
  const [password, setPassword] = useState("");
  const [cancelReason, setCancelReason] = useState("");

  async function load() {
    if (!id) return;
    const found = await fetchTransfer(id);
    setItem(found);
    if (found?.bankSlipRef) setSlipRef(found.bankSlipRef);
  }

  useEffect(() => {
    void load().catch((err: unknown) => setError(err instanceof Error ? err.message : t("treasury.error")));
  }, [id, t]);

  useEffect(() => {
    if (slipFile && slipFile.type.startsWith("image/")) {
      const url = URL.createObjectURL(slipFile);
      setSlipPreview(url);
      return () => URL.revokeObjectURL(url);
    }
    setSlipPreview(null);
    return undefined;
  }, [slipFile]);

  useEffect(() => {
    if (!item?.hasSlip) {
      setDetailSlipUrl(null);
      return;
    }
    let cancelled = false;
    void fetchSlipObjectUrl(item.id)
      .then((url) => {
        if (!cancelled) setDetailSlipUrl(url);
      })
      .catch(() => {
        if (!cancelled) setDetailSlipUrl(null);
      });
    return () => {
      cancelled = true;
    };
  }, [item?.id, item?.hasSlip]);

  const canManage = isAdmin || isGerant;
  const canExecute =
    item?.status === "Draft" &&
    involvesBank(item.direction) &&
    canManage &&
    item.initiatedById !== userId;
  const canAttach = item?.status === "Draft" && involvesBank(item.direction) && canManage;
  const canCancel = item?.status === "Draft" && (isAdmin || item.initiatedById === userId);

  async function onAttach(event: FormEvent) {
    event.preventDefault();
    if (!item || !slipFile) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await attachSlip(item.id, {
        slip: slipFile,
        slipRef: slipRef.trim(),
        slipType: item.direction === "BankToVault" ? "WithdrawalSlip" : "DepositSlip"
      });
      setItem(updated);
      setSlipFile(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("treasury.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onExecute() {
    if (!item) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await executeTransfer(item.id, password);
      setItem(updated);
      setExecuteOpen(false);
      setPassword("");
    } catch (err) {
      setError(err instanceof Error ? err.message : t("treasury.error"));
    } finally {
      setBusy(false);
    }
  }

  if (!item) {
    return (
      <main className="page">
        <p>
          <Link to="/tresorerie/mouvements">{t("treasury.backToList")}</Link>
        </p>
        {error ? <p className="login-form__error">{error}</p> : <p>{t("treasury.loading")}</p>}
      </main>
    );
  }

  return (
    <main className="page">
      <p>
        <Link to="/tresorerie/mouvements">{t("treasury.backToList")}</Link>
      </p>
      <h1>
        {item.transferNo} · {t(`treasury.direction.${item.direction}`)}
      </h1>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}

      <section className="treasury-detail">
        <p>
          <strong>{t("treasury.amount")}:</strong> {formatMoney(item.amount, item.currencyCode)}
        </p>
        {item.bankAccountLabel ? <p>{item.bankAccountLabel}</p> : null}
        {item.notes ? <p>{item.notes}</p> : null}
        <p>
          <span className={`status-badge status-badge--${item.status.toLowerCase()}`}>
            {t(`treasury.status.${item.status}`)}
          </span>
        </p>

        <ol className="approval-stamps">
          <li>
            <strong>{t("treasury.stamp.initiator")}</strong>
            <span>
              {item.initiatedByName}
              {item.initiatedByRole ? ` · ${t(`roles.${item.initiatedByRole}`)}` : ""} · {stampTime(item.createdAtUtc)}
            </span>
          </li>
          <li>
            <strong>{t("treasury.stamp.verifier")}</strong>
            <span>
              {item.executedByName
                ? `${item.executedByName}${item.executedByRole ? ` · ${t(`roles.${item.executedByRole}`)}` : ""} · ${stampTime(item.executedAtUtc)}`
                : t("treasury.stamp.pending")}
            </span>
          </li>
        </ol>

        {item.hasSlip ? (
          <div className="slip-preview">
            <p>
              {t(`treasury.slipType.${item.slipType ?? "DepositSlip"}`)} · {item.bankSlipRef}
            </p>
            {detailSlipUrl && item.slipContentType?.startsWith("image/") ? (
              <img src={detailSlipUrl} alt={item.slipFileName ?? t("treasury.slip")} />
            ) : (
              <a href={detailSlipUrl ?? "#"} target="_blank" rel="noreferrer">
                {item.slipFileName ?? t("treasury.slip")}
              </a>
            )}
          </div>
        ) : null}

        {canAttach ? (
          <form className="stack-form" onSubmit={(event) => void onAttach(event)}>
            <h2>{t("treasury.attachSlip")}</h2>
            <label>
              {t("treasury.slip")}
              <input value={slipRef} onChange={(e) => setSlipRef(e.target.value)} required />
            </label>
            <label>
              {t("treasury.slipFile")}
              <input
                type="file"
                accept="image/jpeg,image/png,application/pdf"
                onChange={(e) => setSlipFile(e.target.files?.[0] ?? null)}
                required
              />
            </label>
            {slipPreview ? <img className="slip-thumb" src={slipPreview} alt="" /> : null}
            <button type="submit" disabled={busy || !slipFile || !slipRef.trim()}>
              {t("treasury.attachSlip")}
            </button>
          </form>
        ) : null}

        <div className="row-actions">
          {canExecute ? (
            <button type="button" disabled={busy} onClick={() => setExecuteOpen(true)}>
              {t("treasury.approveAndExecute")}
            </button>
          ) : null}
          {canCancel ? (
            <>
              <input
                placeholder={t("treasury.cancelReason")}
                value={cancelReason}
                onChange={(e) => setCancelReason(e.target.value)}
              />
              <button
                type="button"
                className="btn-ghost"
                disabled={busy || !cancelReason.trim()}
                onClick={() =>
                  void cancelTransfer(item.id, cancelReason.trim())
                    .then(setItem)
                    .catch((err: unknown) => setError(err instanceof Error ? err.message : t("treasury.error")))
                }
              >
                {t("treasury.cancel")}
              </button>
            </>
          ) : null}
        </div>
      </section>

      {executeOpen ? (
        <div className="modal-backdrop" role="presentation" onClick={() => setExecuteOpen(false)}>
          <div className="modal" role="dialog" onClick={(event) => event.stopPropagation()}>
            <h2>{t("treasury.approveAndExecute")}</h2>
            <p>
              {formatMoney(item.amount, item.currencyCode)} · {item.bankName} · {item.accountNumberMasked}
            </p>
            {detailSlipUrl && item.slipContentType?.startsWith("image/") ? (
              <img className="slip-thumb" src={detailSlipUrl} alt="" />
            ) : (
              <p>{item.slipFileName ?? t("treasury.stamp.pending")}</p>
            )}
            <label>
              {t("treasury.verifierPassword")}
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
              />
            </label>
            <div className="row-actions">
              <button type="button" disabled={busy || !password || !item.hasSlip} onClick={() => void onExecute()}>
                {t("treasury.confirm")}
              </button>
              <button type="button" className="btn-ghost" onClick={() => setExecuteOpen(false)}>
                {t("treasury.cancel")}
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </main>
  );
}
