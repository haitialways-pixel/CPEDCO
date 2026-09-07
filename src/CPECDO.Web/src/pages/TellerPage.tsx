import { FormEvent, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { searchMembers, type MemberSummary } from "../api/members";
import { fetchMemberSavings, type SavingsAccount } from "../api/savings";
import {
  closeTill,
  fetchCurrentTill,
  openTill,
  postCash,
  type CashReceipt,
  type TillSession
} from "../api/teller";

import { formatMoney } from "../money";

const HTG_DENOMS = [1000, 500, 250, 100, 50, 25, 10, 5, 1];

function money(value: number, currency: string) {
  return formatMoney(value, currency);
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
  const [till, setTill] = useState<TillSession | null>(null);
  const [floatAmt, setFloatAmt] = useState("0");
  const [counts, setCounts] = useState<Record<number, string>>({});
  const [query, setQuery] = useState("");
  const [members, setMembers] = useState<MemberSummary[]>([]);
  const [member, setMember] = useState<MemberSummary | null>(null);
  const [accounts, setAccounts] = useState<SavingsAccount[]>([]);
  const [accountId, setAccountId] = useState("");
  const [amount, setAmount] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function refreshTill() {
    setTill(await fetchCurrentTill("HTG"));
  }

  useEffect(() => {
    void refreshTill().catch((err: unknown) => setError(err instanceof Error ? err.message : t("teller.error")));
  }, [t]);

  async function onOpen(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setTill(await openTill(Number(floatAmt || 0)));
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  async function onClose(event: FormEvent) {
    event.preventDefault();
    if (!till) return;
    setBusy(true);
    setError(null);
    try {
      const denominations = HTG_DENOMS
        .map((face) => ({ faceValue: face, quantity: Number(counts[face] || 0) }))
        .filter((d) => d.quantity > 0);
      const closed = await closeTill(till.id, denominations);
      setTill(null);
      alert(`${t("teller.overShort")}: ${formatMoney(closed.overShortAmount ?? 0, closed.currencyCode)}`);
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
    const list = await fetchMemberSavings(item.id);
    setAccounts(list);
    setAccountId(list[0]?.id ?? "");
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
    } catch (err) {
      setError(err instanceof Error ? err.message : t("teller.error"));
    } finally {
      setBusy(false);
    }
  }

  const selected = accounts.find((a) => a.id === accountId);

  return (
    <main className="page">
      <h1>{t("teller.title")}</h1>
      <p>
        <Link to="/caisse/paiement-credit">{t("nav.tellerCredit")}</Link>
      </p>
      {error ? <p className="login-form__error">{error}</p> : null}

      <section className="card-block">
        <h2>{t("teller.till")}</h2>
        {till ? (
          <>
            <p>
              {t("teller.expected")}: <strong>{money(till.expectedCash, till.currencyCode)}</strong>
              {" · "}
              {t("teller.float")}: {money(till.openingFloat, till.currencyCode)}
            </p>
            <form className="stack-form" onSubmit={(e) => void onClose(e)}>
              <div className="form-grid">
                {HTG_DENOMS.map((face) => (
                  <label key={face}>
                    {face} HTG
                    <input
                      type="number"
                      min={0}
                      value={counts[face] ?? ""}
                      onChange={(e) => setCounts((c) => ({ ...c, [face]: e.target.value }))}
                    />
                  </label>
                ))}
              </div>
              <button className="btn-primary" type="submit" disabled={busy}>
                {t("teller.close")}
              </button>
            </form>
          </>
        ) : (
          <form className="search-bar" onSubmit={(e) => void onOpen(e)}>
            <input
              type="number"
              min={0}
              step="0.0001"
              value={floatAmt}
              onChange={(e) => setFloatAmt(e.target.value)}
              placeholder={t("teller.float")}
            />
            <button type="submit" disabled={busy}>
              {t("teller.open")}
            </button>
          </form>
        )}
      </section>

      <section className="card-block">
        <h2>{t("teller.pad")}</h2>
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
                {t("savings.available")}: {money(selected.availableBalance, selected.currencyCode)}
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
              <button type="button" disabled={busy || !till || !accountId} onClick={() => void cash("deposit")}>
                {t("teller.deposit")}
              </button>
              <button type="button" disabled={busy || !till || !accountId} onClick={() => void cash("withdraw")}>
                {t("teller.withdraw")}
              </button>
            </div>
          </>
        ) : null}
      </section>
    </main>
  );
}
