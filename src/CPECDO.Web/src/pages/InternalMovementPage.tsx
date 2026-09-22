import { FormEvent, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { fetchStaff, type StaffSummary } from "../api/members";
import {
  acceptInternalMovement,
  createInternalMovement,
  fetchCurrentTill,
  fetchInternalMovements,
  fetchOpenTills,
  type InternalCashMovement,
  type OpenTillPeer,
  type TillSession
} from "../api/teller";
import { formatMoney } from "../money";

function parseAmount(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;
  const n = Number(trimmed);
  return Number.isFinite(n) ? n : null;
}

export function InternalMovementPage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const userId = session?.user.id;
  const [till, setTill] = useState<TillSession | null>(null);
  const [peers, setPeers] = useState<OpenTillPeer[]>([]);
  const [items, setItems] = useState<InternalCashMovement[]>([]);
  const [direction, setDirection] = useState("VaultToTill");
  const [amount, setAmount] = useState("");
  const [currency, setCurrency] = useState("HTG");
  const [sourceId, setSourceId] = useState("vault");
  const [destId, setDestId] = useState("");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [staff, setStaff] = useState<StaffSummary[]>([]);

  async function refresh() {
    const [current, open, list] = await Promise.all([
      fetchCurrentTill(currency),
      fetchOpenTills(currency),
      fetchInternalMovements(currency)
    ]);
    setTill(current);
    setPeers(open);
    setItems(list);
    const directory = await fetchStaff().catch(() => [] as StaffSummary[]);
    setStaff(directory);
    if (direction === "TillToVault" || direction === "TillToTill") {
      setSourceId(current?.id ?? "");
    } else {
      setSourceId("vault");
    }
  }

  useEffect(() => {
    void refresh().catch((err: unknown) => setError(err instanceof Error ? err.message : t("teller.error")));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currency, t]);

  function onDirection(next: string) {
    setDirection(next);
    if (next === "VaultToTill") {
      setSourceId("vault");
      setDestId(till?.id ?? "");
    } else if (next === "TillToVault") {
      setSourceId(till?.id ?? "");
      setDestId("");
    } else {
      setSourceId(till?.id ?? "");
    }
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const parsed = parseAmount(amount);
    if (parsed === null) {
      setError(t("internal.amountRequired"));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const destIsOpenTill = peers.some((p) => p.id === destId);
      await createInternalMovement({
        direction,
        amount: parsed,
        currencyCode: currency,
        reason: direction === "VaultToTill" && !destIsOpenTill ? "OpeningFloat" : undefined,
        sourceTillSessionId: sourceId && sourceId !== "vault" ? sourceId : undefined,
        destinationTillSessionId: destIsOpenTill ? destId : undefined,
        destinationTellerUserId: direction === "VaultToTill" && !destIsOpenTill ? destId : undefined,
        note: note.trim() || undefined
      });
      setAmount("");
      setNote("");
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onAccept(id: string) {
    setBusy(true);
    setError(null);
    try {
      await acceptInternalMovement(id);
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  const others = peers.filter((p) => p.userId !== userId);
  const needDest = direction === "VaultToTill" || direction === "TillToTill";
  const needSourceTill = direction === "TillToVault" || direction === "TillToTill";

  return (
    <main className="page">
      <p>
        <Link to="/teller">{t("teller.backTill")}</Link>
      </p>
      <h1>{t("internal.title")}</h1>
      {error ? <p className="login-form__error">{error}</p> : null}

      <section className="card-block">
        <form className="stack-form" onSubmit={(e) => void onSubmit(e)}>
          <label>
            {t("internal.direction")}
            <select value={direction} onChange={(e) => onDirection(e.target.value)}>
              <option value="VaultToTill">{t("internal.direction.VaultToTill")}</option>
              <option value="TillToVault">{t("internal.direction.TillToVault")}</option>
              <option value="TillToTill">{t("internal.direction.TillToTill")}</option>
            </select>
          </label>
          <div className="form-grid">
            <label>
              {t("internal.amount")}
              <input
                name="amount"
                type="number"
                min={0}
                step="0.01"
                required
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
              />
            </label>
            <label>
              {t("internal.currency")}
              <select value={currency} onChange={(e) => setCurrency(e.target.value)}>
                <option value="HTG">HTG</option>
                <option value="USD">USD</option>
              </select>
            </label>
          </div>
          {needSourceTill ? (
            <label>
              {t("internal.source")}
              <select value={sourceId} onChange={(e) => setSourceId(e.target.value)} required>
                <option value="">{t("internal.chooseTill")}</option>
                {peers.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.cashierName} ({formatMoney(p.expectedCash, p.currencyCode)})
                  </option>
                ))}
              </select>
            </label>
          ) : (
            <label>
              {t("internal.source")}
              <input readOnly value={t("internal.vault")} />
            </label>
          )}
          {needDest ? (
            <label>
              {t("internal.destination")}
              <select value={destId} onChange={(e) => setDestId(e.target.value)} required>
                <option value="">{t("internal.chooseTill")}</option>
                {direction === "TillToTill"
                  ? others.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.cashierName} ({formatMoney(p.expectedCash, p.currencyCode)})
                      </option>
                    ))
                  : (
                      <>
                        {staff.map((s) => (
                          <option key={s.id} value={s.id}>
                            {s.fullName}
                          </option>
                        ))}
                        {peers.map((p) => (
                          <option key={p.id} value={p.id}>
                            {p.cashierName} ({formatMoney(p.expectedCash, p.currencyCode)})
                          </option>
                        ))}
                      </>
                    )}
              </select>
            </label>
          ) : (
            <label>
              {t("internal.destination")}
              <input readOnly value={t("internal.vault")} />
            </label>
          )}
          <label>
            {t("internal.note")}
            <textarea maxLength={512} value={note} onChange={(e) => setNote(e.target.value)} />
          </label>
          <button className="btn-primary" type="submit" disabled={busy || parseAmount(amount) === null}>
            {t("internal.submit")}
          </button>
        </form>
      </section>

      <section className="card-block">
        <h2>{t("internal.today")}</h2>
        {items.length === 0 ? (
          <p className="muted">{t("internal.empty")}</p>
        ) : (
          <div className="table-wrap">
            <table className="data-table data-table--static">
              <thead>
                <tr>
                  <th>{t("internal.no")}</th>
                  <th>{t("internal.direction")}</th>
                  <th>{t("internal.amount")}</th>
                  <th>{t("reports.status")}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id}>
                    <td>{item.movementNo}</td>
                    <td>{t(`internal.direction.${item.direction}`)}</td>
                    <td>{formatMoney(item.amount, item.currencyCode)}</td>
                    <td>{item.status}</td>
                    <td>
                      {item.canAccept ? (
                        <button type="button" className="btn-primary" disabled={busy} onClick={() => void onAccept(item.id)}>
                          {t("internal.accept")}
                        </button>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </main>
  );
}
