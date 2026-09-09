import { FormEvent, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { fetchMember360, searchMembers, type KycDocument, type MemberSummary } from "../api/members";
import { KycPieces } from "../components/KycPieces";
import { downloadLivretPdf, fetchMemberSavings, type SavingsAccount } from "../api/savings";
import {
  acceptInternalMovement,
  closeTill,
  fetchCurrentTill,
  fetchInternalMovements,
  openTill,
  postCash,
  type CashReceipt,
  type InternalCashMovement,
  type TillSession
} from "../api/teller";

import { formatMoney } from "../money";

const HTG_DENOMS = [1000, 500, 250, 100, 50, 25, 10, 5, 1];

function money(value: number, currency: string) {
  return formatMoney(value, currency);
}

function parseMoneyInput(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;
  const n = Number(trimmed);
  if (!Number.isFinite(n)) return null;
  return n;
}

function roundMoney(value: number, decimals = 2): number {
  const factor = 10 ** decimals;
  return Math.round(value * factor) / factor;
}

function denomSum(counts: Record<number, string>): number {
  return HTG_DENOMS.reduce((sum, face) => {
    const raw = counts[face];
    if (raw === undefined || raw.trim() === "") return sum;
    const qty = Number(raw);
    if (!Number.isFinite(qty) || qty <= 0) return sum;
    return sum + face * qty;
  }, 0);
}

function hasDenomQty(counts: Record<number, string>): boolean {
  return HTG_DENOMS.some((face) => {
    const raw = counts[face];
    if (raw === undefined || raw.trim() === "") return false;
    const qty = Number(raw);
    return Number.isFinite(qty) && qty > 0;
  });
}

function printReceipt(receipt: CashReceipt) {
  const html = `<!doctype html><html lang="fr"><head><meta charset="utf-8"/><title>${receipt.receiptNo}</title>
  <style>
    body{font-family:Georgia,serif;padding:24px;color:#1a1714}
    .sigle{font-size:28px;font-weight:700;letter-spacing:.18em;margin:0}
    .l2{font-size:14px;font-weight:600;margin:.4rem 0 0}
    .l3{font-size:13px;font-weight:400;font-style:italic;margin:.15rem 0 0}
    .l4{font-size:11px;letter-spacing:.12em;text-transform:uppercase;margin:.5rem 0 1rem}
    h2{font-size:16px;margin:1rem 0}
    table{width:100%;border-collapse:collapse}
    td{padding:.25rem 0}
  </style></head><body>
  <p class="sigle">${receipt.letterhead.sigle}</p>
  <p class="l2">${receipt.letterhead.line2}</p>
  <p class="l3">${receipt.letterhead.line3}</p>
  <p class="l4">${receipt.letterhead.line4}</p>
  <h2>${receipt.title}</h2>
  <table>
    <tr><td>N° reçu</td><td>${receipt.receiptNo}</td></tr>
    <tr><td>Membre</td><td>${receipt.memberNo} — ${receipt.memberName}</td></tr>
    <tr><td>Compte</td><td>${receipt.accountNo} (${receipt.productName})</td></tr>
    <tr><td>Montant</td><td>${formatMoney(receipt.amount, receipt.currencyCode)}</td></tr>
    <tr><td>Nouveau solde</td><td>${formatMoney(receipt.ledgerBalance, receipt.currencyCode)}</td></tr>
    <tr><td>Disponible</td><td>${formatMoney(receipt.availableBalance, receipt.currencyCode)}</td></tr>
    <tr><td>Caissier</td><td>${receipt.cashierName}</td></tr>
    <tr><td>Agence</td><td>${receipt.branchName}</td></tr>
  </table>
  <p style="margin-top:1.4rem;font-size:10px;letter-spacing:.02em">CPCREDO — Caisse Populaire d’Épargne et de Crédit pour le Développement de l’Ouest — Pétion-Ville, Haïti</p>
  </body></html>`;
  const w = window.open("", "_blank", "width=480,height=640");
  if (!w) return;
  w.document.write(html);
  w.document.close();
  w.focus();
  w.print();
}

export function TellerPage() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const vue = params.get("vue");
  const [till, setTill] = useState<TillSession | null>(null);
  const [floatAmt, setFloatAmt] = useState("");
  const [countedBalance, setCountedBalance] = useState("");
  const [closeNotes, setCloseNotes] = useState("");
  const [counts, setCounts] = useState<Record<number, string>>({});
  const [query, setQuery] = useState("");
  const [members, setMembers] = useState<MemberSummary[]>([]);
  const [member, setMember] = useState<MemberSummary | null>(null);
  const [kycDocuments, setKycDocuments] = useState<KycDocument[]>([]);
  const [accounts, setAccounts] = useState<SavingsAccount[]>([]);
  const [accountId, setAccountId] = useState("");
  const [amount, setAmount] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [incoming, setIncoming] = useState<InternalCashMovement[]>([]);

  async function refreshTill() {
    const current = await fetchCurrentTill("HTG");
    setTill(current);
    if (!current) {
      setIncoming([]);
      return;
    }
    const list = await fetchInternalMovements(current.currencyCode);
    setIncoming(list.filter((m) => m.canAccept && m.destinationTillSessionId === current.id));
  }

  useEffect(() => {
    void refreshTill().catch((err: unknown) => setError(err instanceof Error ? err.message : t("teller.error")));
  }, [t]);

  useEffect(() => {
    const id = vue === "depot" || vue === "retrait" ? "teller-pad" : "teller-till";
    document.getElementById(id)?.scrollIntoView({ block: "start" });
  }, [vue]);

  async function onOpen(event: FormEvent) {
    event.preventDefault();
    const counted = parseMoneyInput(floatAmt);
    if (counted === null) {
      setError(t("teller.openRequired"));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      setTill(await openTill(counted));
      setFloatAmt("");
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  function onDenomChange(face: number, quantity: string) {
    const next = { ...counts, [face]: quantity };
    setCounts(next);
    if (hasDenomQty(next)) {
      setCountedBalance(roundMoney(denomSum(next)).toFixed(2));
    }
  }

  async function onClose(event: FormEvent) {
    event.preventDefault();
    if (!till) return;
    const counted = parseMoneyInput(countedBalance);
    if (counted === null) {
      setError(t("teller.countedRequired"));
      return;
    }
    const difference = roundMoney(counted - till.expectedCash);
    if (difference !== 0 && closeNotes.trim() === "") {
      setError(t("teller.notesRequired"));
      return;
    }
    const denominations = HTG_DENOMS
      .map((face) => ({ faceValue: face, quantity: Number(counts[face] || 0) }))
      .filter((d) => d.quantity > 0);
    const denomTotal = denominations.reduce((sum, line) => sum + line.faceValue * line.quantity, 0);
    if (denominations.length > 0 && roundMoney(denomTotal - counted, 4) !== 0) {
      setError(t("teller.countMismatch"));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const closed = await closeTill(till.id, {
        countedBalance: counted,
        notes: closeNotes.trim() || undefined,
        denominations
      });
      setTill(null);
      setCounts({});
      setCountedBalance("");
      setCloseNotes("");
      alert(`${t("teller.difference")}: ${formatMoney(closed.overShortAmount ?? 0, closed.currencyCode)}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onSearch(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await searchMembers(query);
      setMembers(result.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  async function selectMember(item: MemberSummary) {
    setMember(item);
    setKycDocuments([]);
    const list = await fetchMemberSavings(item.id);
    setAccounts(list);
    setAccountId(list[0]?.id ?? "");
    try {
      const profile = await fetchMember360(item.id);
      setKycDocuments(profile.kycDocuments ?? []);
    } catch {
      setKycDocuments([]);
    }
  }

  async function cash(kind: "deposit" | "withdraw") {
    if (!accountId) return;
    setBusy(true);
    setError(null);
    try {
      const result = await postCash(kind, accountId, Number(amount));
      setAccounts((current) =>
        current.map((a) =>
          a.id === accountId
            ? { ...a, ledgerBalance: result.ledgerBalance, availableBalance: result.availableBalance }
            : a
        )
      );
      printReceipt(result.receipt);
      setAmount("");
      await refreshTill();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  const selected = accounts.find((a) => a.id === accountId);
  const countedValue = parseMoneyInput(countedBalance);
  const closeDifference =
    till && countedValue !== null ? roundMoney(countedValue - till.expectedCash) : null;
  const notesRequired = closeDifference !== null && closeDifference !== 0;
  const canClose =
    !busy && countedValue !== null && countedValue >= 0 && (!notesRequired || closeNotes.trim() !== "");

  return (
    <main className="page">
      <h1>{t("teller.title")}</h1>
      <p>
        <Link to="/caisse/paiement-credit">{t("nav.tellerCredit")}</Link>
        {" · "}
        <Link to="/caisse/mouvement-interne">{t("nav.internal")}</Link>
      </p>
      {error ? <p className="login-form__error">{error}</p> : null}

      <section className="card-block" id="teller-till">
        <h2>{t("teller.till")}</h2>
        {till ? (
          <>
            <p>
              {t("teller.expected")}: <strong>{money(till.expectedCash, till.currencyCode)}</strong>
              {" · "}
              {t("teller.float")}: {money(till.openingFloat, till.currencyCode)}
            </p>
            {incoming.length > 0 ? (
              <div className="till-incoming">
                <h3>{t("internal.acceptIncoming")}</h3>
                <ul className="ul-reset">
                  {incoming.map((m) => (
                    <li key={m.id}>
                      {t(`internal.direction.${m.direction}`)} · {money(m.amount, m.currencyCode)}
                      <button
                        type="button"
                        className="btn-primary"
                        disabled={busy}
                        onClick={() =>
                          void (async () => {
                            setBusy(true);
                            setError(null);
                            try {
                              await acceptInternalMovement(m.id);
                              await refreshTill();
                            } catch (err) {
                              setError(err instanceof Error ? err.message : t("teller.error"));
                            } finally {
                              setBusy(false);
                            }
                          })()
                        }
                      >
                        {t("internal.accept")}
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
            <h3>{t("teller.closeTitle")}</h3>
            <p className="muted">{t("teller.closeCaption")}</p>
            <form className="stack-form" onSubmit={(e) => void onClose(e)}>
              <label>
                {t("teller.expectedBalance")}
                <input
                  name="expectedBalance"
                  readOnly
                  value={money(till.expectedCash, till.currencyCode)}
                />
              </label>
              <label>
                {t("teller.countedBalance")}
                <input
                  name="countedBalance"
                  type="number"
                  min={0}
                  step="0.01"
                  required
                  value={countedBalance}
                  onChange={(e) => setCountedBalance(e.target.value)}
                />
              </label>
              <fieldset className="till-denoms">
                <legend>{t("teller.denominations")}</legend>
                <div className="form-grid">
                  {HTG_DENOMS.map((face) => (
                    <label key={face}>
                      {face} HTG
                      <input
                        type="number"
                        min={0}
                        step={1}
                        value={counts[face] ?? ""}
                        onChange={(e) => onDenomChange(face, e.target.value)}
                      />
                    </label>
                  ))}
                </div>
              </fieldset>
              <label>
                {t("teller.difference")}
                <input
                  name="difference"
                  readOnly
                  className={notesRequired ? "till-difference is-nonzero" : "till-difference"}
                  value={
                    closeDifference === null ? "" : money(closeDifference, till.currencyCode)
                  }
                />
              </label>
              <label>
                {t("teller.notes")}
                <textarea
                  name="notes"
                  maxLength={512}
                  required={notesRequired}
                  value={closeNotes}
                  onChange={(e) => setCloseNotes(e.target.value)}
                />
              </label>
              <p className="muted">{t("teller.closeHelper")}</p>
              <button className="btn-primary" type="submit" disabled={!canClose}>
                {t("teller.closeSubmit")}
              </button>
            </form>
          </>
        ) : (
          <form className="stack-form" onSubmit={(e) => void onOpen(e)}>
            <h3>{t("teller.openTitle")}</h3>
            <p className="muted">{t("teller.openCaption")}</p>
            <label>
              {t("teller.openFloat")}
              <input
                name="openingFloat"
                type="number"
                min={0}
                step="0.01"
                required
                value={floatAmt}
                onChange={(e) => setFloatAmt(e.target.value)}
              />
            </label>
            <p className="muted">{t("teller.openHelper")}</p>
            <button className="btn-primary" type="submit" disabled={busy || parseMoneyInput(floatAmt) === null}>
              {t("teller.open")}
            </button>
          </form>
        )}
      </section>

      <section className="card-block" id="teller-pad">
        <h2>{vue === "depot" ? t("nav.deposit") : vue === "retrait" ? t("nav.withdraw") : t("teller.pad")}</h2>
        <form className="search-bar" onSubmit={(e) => void onSearch(e)}>
          <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t("members.searchPlaceholder")} />
          <button type="submit" disabled={busy}>
            {t("members.search")}
          </button>
        </form>
        {members.length > 0 ? (
          <ul className="coming-soon ul-reset">
            {members.map((item) => (
              <li key={item.id}>
                <button type="button" className="btn-ghost" onClick={() => void selectMember(item)}>
                  {item.memberNo} — {item.fullName}
                </button>
              </li>
            ))}
          </ul>
        ) : null}
        {member ? (
          <>
            <p>
              <strong>{member.fullName}</strong> ({member.memberNo})
            </p>
            <KycPieces
              memberId={member.id}
              documents={kycDocuments}
              onChanged={async () => {
                const profile = await fetchMember360(member.id);
                setKycDocuments(profile.kycDocuments ?? []);
              }}
            />
            <label>
              {t("savings.accountNo")}
              <select value={accountId} onChange={(e) => setAccountId(e.target.value)}>
                {accounts.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.accountNo} — {a.productName} ({money(a.availableBalance, a.currencyCode)})
                  </option>
                ))}
              </select>
            </label>
            {selected ? (
              <p className="muted">
                {t("savings.ledger")}: {money(selected.ledgerBalance, selected.currencyCode)} ·{" "}
                {t("savings.available")}: {money(selected.availableBalance, selected.currencyCode)}{" "}
                <button
                  type="button"
                  className="btn-ghost"
                  onClick={() =>
                    void downloadLivretPdf(selected.id).catch((err: unknown) =>
                      setError(err instanceof Error ? err.message : t("teller.error"))
                    )
                  }
                >
                  {t("savings.livret")}
                </button>
              </p>
            ) : (
              <p className="muted">{t("savings.empty")}</p>
            )}
            <div className="search-bar" style={{ marginTop: "0.8rem" }}>
              <input
                type="number"
                min={0}
                step="0.0001"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                placeholder={t("teller.amount")}
              />
              {vue !== "retrait" ? (
                <button type="button" disabled={busy || !till || !accountId} onClick={() => void cash("deposit")}>
                  {t("teller.deposit")}
                </button>
              ) : null}
              {vue !== "depot" ? (
                <button type="button" disabled={busy || !till || !accountId} onClick={() => void cash("withdraw")}>
                  {t("teller.withdraw")}
                </button>
              ) : null}
            </div>
          </>
        ) : null}
      </section>
    </main>
  );
}
