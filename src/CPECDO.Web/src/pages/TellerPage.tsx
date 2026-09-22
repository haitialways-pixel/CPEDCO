import { FormEvent, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { fetchMember360, searchMembers, type KycDocument, type Member360, type MemberSummary } from "../api/members";
import { KycPieces } from "../components/KycPieces";
import { IdTrigger, useIdOpen } from "../components/IdTrigger";
import { MemberAccountsPanel } from "../components/MemberAccountsPanel";
import {
  downloadLivretPdf,
  fetchMemberSavings,
  LivretEmptyError,
  openMemberAccount,
  printUnprintedLivret,
  type SavingsAccount
} from "../api/savings";
import {
  acceptInternalMovement,
  closeTill,
  collectMixed,
  fetchCurrentTill,
  fetchInternalMovements,
  fetchPendingMovements,
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
    ${(receipt.allocations ?? [])
      .map(
        (a) =>
          `<tr><td>${a.label}</td><td>${a.accountNo} — ${formatMoney(a.amount, receipt.currencyCode)}</td></tr>`
      )
      .join("")}
    <tr><td>Nouveau solde</td><td>${formatMoney(receipt.ledgerBalance, receipt.currencyCode)}</td></tr>
    <tr><td>Disponible</td><td>${formatMoney(receipt.availableBalance, receipt.currencyCode)}</td></tr>
    <tr><td>Caissier</td><td>${receipt.cashierName}</td></tr>
    <tr><td>Agence</td><td>${receipt.branchName}</td></tr>
  </table>
  <p style="margin-top:1.4rem;font-size:10px;letter-spacing:.02em">CPCREDO — Caisse Populaire Épargne et de Crédit pour le Développement de l’Ouest — Pétion-Ville, Haïti</p>
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
  const { session } = useAuth();
  const isGerant = session?.roles.some((r) => r.name === "Gerant" || r.name === "Admin") ?? false;
  const canOpenAccount = session?.roles.some((r) => ["Admin", "Gerant", "OfficierCredit", "ServiceClient"].includes(r.name)) ?? false;
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
  const [overrideNote, setOverrideNote] = useState("");
  const [profile, setProfile] = useState<Member360 | null>(null);
  const [cashReceived, setCashReceived] = useState("");
  const [lines, setLines] = useState<{ kind: string; savingsAccountId: string; amount: string }[]>([
    { kind: "Epargne", savingsAccountId: "", amount: "" }
  ]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [incoming, setIncoming] = useState<InternalCashMovement[]>([]);
  const [pendingOpen, setPendingOpen] = useState<InternalCashMovement[]>([]);
  const memberIds = useIdOpen();
  const openIds = useIdOpen();

  async function refreshTill() {
    const current = await fetchCurrentTill("HTG");
    setTill(current);
    if (!current) {
      setIncoming([]);
      setPendingOpen(await fetchPendingMovements("HTG"));
      return;
    }
    setPendingOpen([]);
    const list = await fetchInternalMovements(current.currencyCode);
    setIncoming(list.filter((m) => m.canAccept && m.destinationTillSessionId === current.id));
  }

  useEffect(() => {
    void refreshTill().catch((err: unknown) => setError(err instanceof Error ? err.message : t("teller.error")));
  }, [t]);

  useEffect(() => {
    const id = vue === "depot" || vue === "retrait" || vue === "encaisser" ? "teller-pad" : "teller-till";
    document.getElementById(id)?.scrollIntoView({ block: "start" });
  }, [vue]);

  async function onOpen(event: FormEvent) {
    event.preventDefault();
    const pending = pendingOpen[0];
    if (!pending) {
      setError(t("teller.openNone"));
      return;
    }
    const counted = parseMoneyInput(floatAmt);
    if (counted === null) {
      setError(t("teller.openRequired"));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await acceptInternalMovement(pending.id, counted);
      await refreshTill();
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
    setProfile(null);
    const list = await fetchMemberSavings(item.id);
    setAccounts(list);
    setAccountId(list[0]?.id ?? "");
    setLines([{ kind: "Epargne", savingsAccountId: list[0]?.id ?? "", amount: "" }]);
    try {
      const loaded = await fetchMember360(item.id);
      setProfile(loaded);
      setKycDocuments(loaded.kycDocuments ?? []);
    } catch {
      setKycDocuments([]);
    }
  }

  async function cash(kind: "deposit" | "withdraw") {
    if (!accountId) return;
    setBusy(true);
    setError(null);
    try {
      const result = await postCash(kind, accountId, Number(amount), overrideNote.trim() || undefined);
      setAccounts((current) =>
        current.map((a) =>
          a.id === accountId
            ? { ...a, ledgerBalance: result.ledgerBalance, availableBalance: result.availableBalance }
            : a
        )
      );
      printReceipt(result.receipt);
      setAmount("");
      setOverrideNote("");
      await refreshTill();
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  async function collect() {
    if (!member) return;
    const cash = Number(cashReceived);
    setBusy(true);
    setError(null);
    try {
      const result = await collectMixed({
        memberId: member.id,
        cashReceived: cash,
        currencyCode: "HTG",
        lines: lines.map((line) => ({
          kind: line.kind,
          savingsAccountId: line.kind === "Epargne" ? line.savingsAccountId || null : null,
          amount: Number(line.amount)
        }))
      });
      printReceipt(result.receipt);
      setAccounts(await fetchMemberSavings(member.id));
      setCashReceived("");
      setLines([{ kind: "Epargne", savingsAccountId: accounts[0]?.id ?? "", amount: "" }]);
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
            {pendingOpen.length === 0 ? <p className="muted">{t("teller.openNone")}</p> : null}
            {pendingOpen.map((m) => (
              <p key={m.id}>
                {t("teller.openIssued")}: {money(m.amount, m.currencyCode)}
                <IdTrigger
                  open={openIds.open}
                  onToggle={openIds.toggle}
                  lines={[{ value: m.movementNo }]}
                />
              </p>
            ))}
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
                disabled={pendingOpen.length === 0}
              />
            </label>
            <button
              className="btn-primary"
              type="submit"
              disabled={busy || pendingOpen.length === 0 || parseMoneyInput(floatAmt) === null}
            >
              {t("teller.openAccept")}
            </button>
          </form>
        )}
      </section>

      <section className="card-block" id="teller-pad">
        <h2>
          {vue === "depot"
            ? t("nav.deposit")
            : vue === "retrait"
              ? t("nav.withdraw")
              : vue === "encaisser"
                ? t("nav.collect")
                : t("teller.pad")}
        </h2>
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
              <strong>{member.fullName}</strong>
              <IdTrigger
                open={memberIds.open}
                onToggle={memberIds.toggle}
                lines={[{ value: member.memberNo }, { value: selected?.accountNo }]}
              />
            </p>
            {profile ? (
              <MemberAccountsPanel
                member={profile}
                canOpen={canOpenAccount}
                onMemberUpdated={(next) => {
                  setProfile(next);
                  setAccounts(next.savingsAccounts ?? []);
                  setAccountId((current) => current || next.savingsAccounts?.[0]?.id || "");
                }}
              />
            ) : null}
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
                    {memberIds.open ? `${a.accountNo} — ` : ""}
                    {a.productName} ({money(a.availableBalance, a.currencyCode)})
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
                    void printUnprintedLivret(selected.id).catch((err: unknown) =>
                      setError(
                        err instanceof LivretEmptyError
                          ? t("savings.livretEmpty")
                          : err instanceof Error
                            ? err.message
                            : t("teller.error")
                      )
                    )
                  }
                >
                  {t("savings.livret")}
                </button>
                <button
                  type="button"
                  className="btn-ghost"
                  onClick={() =>
                    void downloadLivretPdf(selected.id).catch((err: unknown) =>
                      setError(err instanceof Error ? err.message : t("teller.error"))
                    )
                  }
                >
                  {t("savings.exportPdf")}
                </button>
              </p>
            ) : (
              <p className="muted">{t("savings.empty")}</p>
            )}
            {vue === "encaisser" ? (
              <div className="stack-form" style={{ marginTop: "0.8rem" }}>
                <p className="muted">{t("teller.collectCaption")}</p>
                <label>
                  {t("teller.cashReceived")}
                  <input type="number" min={0} step="0.01" value={cashReceived} onChange={(e) => setCashReceived(e.target.value)} />
                </label>
                {lines.map((line, index) => (
                  <div key={index} className="search-bar">
                    <select
                      value={line.kind}
                      onChange={(e) => {
                        const next = [...lines];
                        next[index] = { ...line, kind: e.target.value };
                        setLines(next);
                      }}
                    >
                      <option value="Epargne">{t("savings.kindEpargne")}</option>
                      <option value="Qualification">{t("savings.kindQual")}</option>
                      <option value="Permanent">{t("savings.kindPerm")}</option>
                    </select>
                    {line.kind === "Epargne" ? (
                      <select
                        value={line.savingsAccountId}
                        onChange={(e) => {
                          const next = [...lines];
                          next[index] = { ...line, savingsAccountId: e.target.value };
                          setLines(next);
                        }}
                      >
                        <option value="">{t("savings.empty")}</option>
                        {accounts.map((a) => (
                          <option key={a.id} value={a.id}>
                            {memberIds.open ? `${a.accountNo} — ` : ""}
                            {a.productName}
                          </option>
                        ))}
                      </select>
                    ) : null}
                    {line.kind === "Epargne" && !line.savingsAccountId && canOpenAccount ? (
                      <button
                        type="button"
                        className="btn-ghost"
                        disabled={busy}
                        onClick={() => {
                          if (!member) return;
                          const productId = accounts[0]?.productId;
                          setBusy(true);
                          void openMemberAccount(member.id, "Epargne", productId)
                            .then(async () => {
                              const list = await fetchMemberSavings(member.id);
                              setAccounts(list);
                              const next = [...lines];
                              next[index] = { ...line, savingsAccountId: list[0]?.id ?? "" };
                              setLines(next);
                            })
                            .catch((err: unknown) => setError(err instanceof Error ? err.message : t("savings.openError")))
                            .finally(() => setBusy(false));
                        }}
                      >
                        {t("teller.openAccount")}
                      </button>
                    ) : null}
                    <input
                      type="number"
                      min={0}
                      step="0.01"
                      value={line.amount}
                      onChange={(e) => {
                        const next = [...lines];
                        next[index] = { ...line, amount: e.target.value };
                        setLines(next);
                      }}
                      placeholder={t("teller.amount")}
                    />
                  </div>
                ))}
                <button
                  type="button"
                  className="btn-ghost"
                  onClick={() => setLines([...lines, { kind: "Epargne", savingsAccountId: accountId, amount: "" }])}
                >
                  {t("teller.addLine")}
                </button>
                <button type="button" className="btn-primary" disabled={busy || !till} onClick={() => void collect()}>
                  {t("teller.collectConfirm")}
                </button>
              </div>
            ) : (
              <div className="search-bar" style={{ marginTop: "0.8rem" }}>
                <input
                  type="number"
                  min={0}
                  step="0.0001"
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  placeholder={t("teller.amount")}
                />
                {vue === "retrait" && isGerant ? (
                  <input
                    value={overrideNote}
                    onChange={(e) => setOverrideNote(e.target.value)}
                    placeholder={t("teller.overrideNote")}
                  />
                ) : null}
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
            )}
          </>
        ) : null}
      </section>
    </main>
  );
}
