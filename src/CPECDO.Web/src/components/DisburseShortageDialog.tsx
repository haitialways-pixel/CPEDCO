import { FormEvent, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { fetchCashSources, fundDrawer, type CashSource } from "../api/teller";
import type { TillCashShortfall } from "../api/loans";
import { formatMoney } from "../money";

type Props = {
  shortfall: TillCashShortfall;
  onCancel: () => void;
  onFunded: () => Promise<void> | void;
};

export function DisburseShortageDialog({ shortfall, onCancel, onFunded }: Props) {
  const { t } = useTranslation();
  const [step, setStep] = useState<"warn" | "transfer">("warn");
  const [sources, setSources] = useState<CashSource[]>([]);
  const [sourceKey, setSourceKey] = useState("");
  const [amount, setAmount] = useState(String(shortfall.missing));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") onCancel();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onCancel]);

  useEffect(() => {
    if (step !== "transfer") return;
    setBusy(true);
    void fetchCashSources(shortfall.currencyCode)
      .then((list) => {
        setSources(list);
        const first = list[0];
        setSourceKey(first ? sourceValue(first) : "");
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("loans.error")))
      .finally(() => setBusy(false));
  }, [step, shortfall.currencyCode, t]);

  const selected = sources.find((item) => sourceValue(item) === sourceKey);
  const maxAmount = selected?.available ?? 0;
  const memo = t("loans.tillShortMemoDefault", { id: shortfall.loanNo });

  async function onConfirmTransfer(event: FormEvent) {
    event.preventDefault();
    if (!selected) return;
    const value = Number(amount);
    if (!Number.isFinite(value) || value <= 0) {
      setError(t("loans.tillShortAmountInvalid"));
      return;
    }
    if (value > maxAmount) {
      setError(t("loans.tillShortExceedsSource"));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await fundDrawer({
        sourceKind: selected.kind,
        sourceTillSessionId: selected.tillSessionId ?? undefined,
        amount: value,
        currencyCode: shortfall.currencyCode,
        note: memo,
        loanId: shortfall.loanId
      });
      await onFunded();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("loans.error"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div
      className="kyc-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="till-short-title"
      onClick={onCancel}
    >
      <div className="kyc-modal__card kyc-modal__card--wide" onClick={(e) => e.stopPropagation()}>
        <h3 id="till-short-title">{t("loans.tillShortTitle")}</h3>
        {error ? (
          <p className="login-form__error" role="alert">
            {error}
          </p>
        ) : null}
        <p>{t("loans.tillShortBody")}</p>
        <p className="tabular-nums">
          {t("loans.tillShortAmount")}: {formatMoney(shortfall.disbursementAmount, shortfall.currencyCode)}
          <br />
          {t("loans.tillShortAvailable")}: {formatMoney(shortfall.drawerAvailable, shortfall.currencyCode)}
          <br />
          {t("loans.tillShortMissing")}: {formatMoney(shortfall.missing, shortfall.currencyCode)}
        </p>
        {step === "warn" ? (
          <>
            <p>{t("loans.tillShortAsk")}</p>
            <div className="kyc-slot__actions">
              <button type="button" className="btn-ghost" onClick={onCancel}>
                {t("loans.cancel")}
              </button>
              <button type="button" className="btn-primary" onClick={() => setStep("transfer")}>
                {t("loans.tillShortTransfer")}
              </button>
            </div>
          </>
        ) : (
          <form className="stack-form" onSubmit={(e) => void onConfirmTransfer(e)}>
            <label>
              {t("loans.tillShortFrom")}
              <select value={sourceKey} onChange={(e) => setSourceKey(e.target.value)} required>
                <option value="">{t("loans.tillShortPickSource")}</option>
                {sources.map((item) => (
                  <option key={sourceValue(item)} value={sourceValue(item)}>
                    {item.kind === "Vault" ? t("loans.tillShortVault") : item.label} —{" "}
                    {formatMoney(item.available, item.currencyCode)}
                  </option>
                ))}
              </select>
            </label>
            <p className="muted">
              {t("loans.tillShortTo")}: {t("loans.tillShortDrawer")}
            </p>
            <label>
              {t("loans.tillShortMissing")}
              <input
                type="number"
                min="0.01"
                step="0.01"
                max={maxAmount || undefined}
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                required
              />
            </label>
            <p className="muted">{t("loans.tillShortMemo")}: {memo}</p>
            <div className="kyc-slot__actions">
              <button type="button" className="btn-ghost" onClick={onCancel} disabled={busy}>
                {t("loans.cancel")}
              </button>
              <button type="submit" className="btn-primary" disabled={busy || !selected}>
                {busy ? t("loans.saving") : t("loans.tillShortConfirm")}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}

function sourceValue(item: CashSource): string {
  return item.kind === "Vault" ? "Vault" : `Till:${item.tillSessionId ?? ""}`;
}
