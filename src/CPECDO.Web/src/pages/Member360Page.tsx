import { FormEvent, useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  assignTicket,
  closeTicket,
  convertToSocietaire,
  fetchMember360,
  fetchStaff,
  openTicket,
  subscribePermanentShares,
  updateMember,
  type Member360,
  type StaffSummary
} from "../api/members";
import {
  blockAccount,
  downloadStatementPdf,
  fetchSavingsProducts,
  openSavingsAccount,
  placeHold,
  releaseHold,
  unblockAccount,
  type SavingsAccount,
  type SavingsProduct
} from "../api/savings";

import { formatMoney } from "../money";

function money(value: number, currency: string) {
  return formatMoney(value, currency);
}

export function Member360Page() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { t } = useTranslation();
  const { session } = useAuth();
  const canWrite = session?.roles.some((r) => !r.isReadOnly) ?? false;
  const canServiceClient =
    session?.roles.some((r) => ["Admin", "Gerant", "ServiceClient"].includes(r.name)) ?? false;
  const [member, setMember] = useState<Member360 | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState(false);
  const [accounts, setAccounts] = useState<SavingsAccount[]>([]);
  const [products, setProducts] = useState<SavingsProduct[]>([]);
  const [productId, setProductId] = useState("");
  const [opening, setOpening] = useState(false);
  const [staff, setStaff] = useState<StaffSummary[]>([]);
  const [holdAmt, setHoldAmt] = useState("");
  const [holdReason, setHoldReason] = useState("");
  const [blockReason, setBlockReason] = useState("");
  const [pdfFrom, setPdfFrom] = useState("2026-01-01");
  const [pdfTo, setPdfTo] = useState(new Date().toISOString().slice(0, 10));
  const [ticketSubject, setTicketSubject] = useState("");
  const [ticketBody, setTicketBody] = useState("");
  const [ticketAssignee, setTicketAssignee] = useState("");
  const [form, setForm] = useState({
    firstName: "",
    lastName: "",
    cin: "",
    nif: "",
    phone: "",
    alternatePhone: "",
    addressLine: "",
    city: "",
    commune: "",
    status: "Active",
    kycStatus: "Verified",
    legalStatus: "Usager",
    qualificationShareCount: "0"
  });

  useEffect(() => {
    if (!id) return;
    void Promise.all([fetchMember360(id), fetchSavingsProducts(), fetchStaff()])
      .then(([data, prods, staffList]) => {
        setMember(data);
        setAccounts(data.savingsAccounts ?? []);
        setStaff(staffList);
        setProducts(prods);
        setProductId(prods[0]?.id ?? "");
        setForm({
          firstName: data.firstName,
          lastName: data.lastName,
          cin: data.cin ?? "",
          nif: data.nif ?? "",
          phone: data.phone,
          alternatePhone: data.alternatePhone ?? "",
          addressLine: data.addressLine,
          city: data.city,
          commune: data.commune ?? "",
          status: data.status,
          kycStatus: data.kycStatus,
          legalStatus: data.legalStatus,
          qualificationShareCount: String(data.shares.qualificationShareCount)
        });
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("members.loadError")));
  }, [id, t]);

  function set(name: string, value: string) {
    setForm((current) => ({ ...current, [name]: value }));
  }

  async function onSave(event: FormEvent) {
    event.preventDefault();
    if (!id) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await updateMember(id, {
        firstName: form.firstName,
        lastName: form.lastName,
        cin: form.cin || undefined,
        nif: form.nif || undefined,
        phone: form.phone,
        alternatePhone: form.alternatePhone || undefined,
        addressLine: form.addressLine,
        city: form.city,
        commune: form.commune || undefined,
        status: form.status,
        kycStatus: form.kycStatus,
        legalStatus: form.legalStatus,
        qualificationShareCount: Number(form.qualificationShareCount || 0)
      });
      setMember(updated);
      setEditing(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("members.saveError"));
    } finally {
      setBusy(false);
    }
  }

  if (!member && !error) return <main className="page">{t("members.loading")}</main>;
  if (!member) {
    return (
      <main className="page">
        <p>
          <Link to="/members">{t("members.back")}</Link>
        </p>
        <p className="login-form__error">{error}</p>
      </main>
    );
  }

  return (
    <main className="page">
      <p>
        <Link to="/members">{t("members.back")}</Link>
      </p>
      <div className="page__head">
        <div>
          <p className="eyebrow">{member.memberNo}</p>
          <h1>{member.fullName}</h1>
        </div>
        {canWrite && !editing ? (
          <button type="button" className="btn-ghost" onClick={() => setEditing(true)}>
            {t("members.edit")}
          </button>
        ) : null}
      </div>
      {error ? <p className="login-form__error">{error}</p> : null}

      <p className="vote-banner">{t("members.voteRule")}</p>

      {editing ? (
        <form className="stack-form" onSubmit={(e) => void onSave(e)}>
          <div className="form-grid">
            <label>
              {t("members.firstName")}
              <input required value={form.firstName} onChange={(e) => set("firstName", e.target.value)} />
            </label>
            <label>
              {t("members.lastName")}
              <input required value={form.lastName} onChange={(e) => set("lastName", e.target.value)} />
            </label>
            <label>
              {t("members.cin")}
              <input value={form.cin} onChange={(e) => set("cin", e.target.value)} />
            </label>
            <label>
              {t("members.nif")}
              <input value={form.nif} onChange={(e) => set("nif", e.target.value)} />
            </label>
            <label>
              {t("members.phone")}
              <input required value={form.phone} onChange={(e) => set("phone", e.target.value)} />
            </label>
            <label>
              {t("members.altPhone")}
              <input value={form.alternatePhone} onChange={(e) => set("alternatePhone", e.target.value)} />
            </label>
            <label className="span-2">
              {t("members.address")}
              <input required value={form.addressLine} onChange={(e) => set("addressLine", e.target.value)} />
            </label>
            <label>
              {t("members.city")}
              <input required value={form.city} onChange={(e) => set("city", e.target.value)} />
            </label>
            <label>
              {t("members.commune")}
              <input value={form.commune} onChange={(e) => set("commune", e.target.value)} />
            </label>
            <label>
              {t("members.statusLabel")}
              <select value={form.status} onChange={(e) => set("status", e.target.value)}>
                <option value="Pending">{t("members.status.Pending")}</option>
                <option value="Active">{t("members.status.Active")}</option>
                <option value="Suspended">{t("members.status.Suspended")}</option>
                <option value="Closed">{t("members.status.Closed")}</option>
              </select>
            </label>
            <label>
              {t("members.kyc")}
              <select value={form.kycStatus} onChange={(e) => set("kycStatus", e.target.value)}>
                <option value="Incomplete">{t("members.kycStatus.Incomplete")}</option>
                <option value="Pending">{t("members.kycStatus.Pending")}</option>
                <option value="Verified">{t("members.kycStatus.Verified")}</option>
                <option value="Rejected">{t("members.kycStatus.Rejected")}</option>
              </select>
            </label>
            <label>
              {t("members.legalStatus")}
              <select value={form.legalStatus} onChange={(e) => set("legalStatus", e.target.value)}>
                <option value="Usager">{t("members.legal.Usager")}</option>
                <option value="Societaire">{t("members.legal.Societaire")}</option>
                <option value="Auxiliaire">{t("members.legal.Auxiliaire")}</option>
              </select>
            </label>
            <label>
              {t("members.qualificationShares")}
              <input
                type="number"
                min={0}
                step={1}
                value={form.qualificationShareCount}
                onChange={(e) => set("qualificationShareCount", e.target.value)}
              />
            </label>
          </div>
          <div className="actions">
            <button className="btn-primary" type="submit" disabled={busy}>
              {busy ? t("members.saving") : t("members.save")}
            </button>
            <button type="button" className="btn-ghost" onClick={() => setEditing(false)}>
              {t("members.cancel")}
            </button>
          </div>
        </form>
      ) : (
        <>
          <section className="facts">
            <article>
              <span>{t("members.statusLabel")}</span>
              <strong>{t(`members.status.${member.status}`)}</strong>
            </article>
            <article>
              <span>{t("members.kyc")}</span>
              <strong>{t(`members.kycStatus.${member.kycStatus}`)}</strong>
            </article>
            <article>
              <span>{t("members.legalStatus")}</span>
              <strong>{t(`members.legal.${member.legalStatus}`)}</strong>
            </article>
            <article>
              <span>{t("members.founder")}</span>
              <strong>
                {member.isFounder
                  ? t(`members.founderGroup.${member.founderGroup ?? "CapitalFounder"}`)
                  : t("members.founderNone")}
              </strong>
            </article>
            <article>
              <span>{t("members.votes")}</span>
              <strong>{member.votingRights ? t("members.voteYes") : t("members.voteNo")}</strong>
            </article>
            {member.legalStatus === "Usager" ? (
              <article>
                <span>{t("members.usagerDaysLeft")}</span>
                <strong>
                  {member.servicesBlocked
                    ? t("members.usagerExpired")
                    : t("members.daysCount", { count: member.usagerDaysLeft ?? 0 })}
                </strong>
              </article>
            ) : null}
            <article>
              <span>{t("dashboard.branch")}</span>
              <strong>{member.branchName}</strong>
            </article>
          </section>
          {canWrite && member.legalStatus !== "Societaire" ? (
            <p>
              <button
                type="button"
                className="btn-primary"
                disabled={busy}
                onClick={() => {
                  if (!id) return;
                  setBusy(true);
                  setError(null);
                  void convertToSocietaire(id)
                    .then((updated) => setMember(updated))
                    .catch((err: unknown) => setError(err instanceof Error ? err.message : t("members.saveError")))
                    .finally(() => setBusy(false));
                }}
              >
                {t("members.convertSocietaire")}
              </button>
            </p>
          ) : null}
          <section className="card-block">
            <h2>{t("members.profile")}</h2>
            <dl className="deflist">
              <div>
                <dt>{t("members.cin")}</dt>
                <dd>{member.cin ?? "—"}</dd>
              </div>
              <div>
                <dt>{t("members.nif")}</dt>
                <dd>{member.nif ?? "—"}</dd>
              </div>
              <div>
                <dt>{t("members.phone")}</dt>
                <dd>{member.phone}</dd>
              </div>
              <div>
                <dt>{t("members.altPhone")}</dt>
                <dd>{member.alternatePhone ?? "—"}</dd>
              </div>
              <div>
                <dt>{t("members.address")}</dt>
                <dd>
                  {member.addressLine}, {member.city}
                  {member.commune ? ` — ${member.commune}` : ""}
                </dd>
              </div>
            </dl>
          </section>
          <section className="card-block">
            <h2>{t("members.sharesTitle")}</h2>
            <dl className="deflist">
              <div>
                <dt>{t("members.qualificationShares")}</dt>
                <dd>
                  {member.shares.qualificationShareCount}
                  {member.shares.qualificationAccountNo ? ` · ${member.shares.qualificationAccountNo}` : ""}
                </dd>
              </div>
              <div>
                <dt>{t("members.permanentShares")}</dt>
                <dd>
                  {member.shares.permanentShareCount}
                  {member.shares.permanentAccountNo ? ` · ${member.shares.permanentAccountNo}` : ""}
                </dd>
              </div>
              <div>
                <dt>{t("members.parValue")}</dt>
                <dd>{money(member.shares.parValue, member.shares.currencyCode)}</dd>
              </div>
              <div>
                <dt>{t("members.qualificationBook")}</dt>
                <dd>{money(member.shares.qualificationBookValue, member.shares.currencyCode)}</dd>
              </div>
              <div>
                <dt>{t("members.permanentBook")}</dt>
                <dd>{money(member.shares.permanentBookValue, member.shares.currencyCode)}</dd>
              </div>
              <div>
                <dt>{t("members.votes")}</dt>
                <dd>{member.shares.votingRights ? t("members.voteYes") : t("members.voteNo")}</dd>
              </div>
            </dl>
            {canWrite && member.legalStatus === "Societaire" ? (
              <p>
                <button
                  type="button"
                  className="btn-ghost"
                  disabled={busy}
                  onClick={() => {
                    if (!id) return;
                    const raw = window.prompt(t("members.permanentPrompt"), "1");
                    const quantity = Number(raw);
                    if (!raw || !Number.isInteger(quantity) || quantity < 1) return;
                    setBusy(true);
                    setError(null);
                    void subscribePermanentShares(id, quantity)
                      .then((updated) => setMember(updated))
                      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("members.saveError")))
                      .finally(() => setBusy(false));
                  }}
                >
                  {t("members.subscribePermanent")}
                </button>
              </p>
            ) : null}
          </section>
          <section className="card-block">
            <h2>{t("savings.title")}</h2>
            {accounts.length === 0 ? <p className="muted">{t("savings.empty")}</p> : null}
            {accounts.length > 0 ? (
              <div className="table-wrap">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>{t("savings.accountNo")}</th>
                      <th>{t("savings.product")}</th>
                      <th>{t("savings.ledger")}</th>
                      <th>{t("savings.available")}</th>
                      <th>{t("service.status")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {accounts.map((account) => (
                      <tr key={account.id} onClick={() => navigate(`/savings-accounts/${account.id}`)}>
                        <td>{account.accountNo}</td>
                        <td>{account.productName}</td>
                        <td>{money(account.ledgerBalance, account.currencyCode)}</td>
                        <td>{money(account.availableBalance, account.currencyCode)}</td>
                        <td>
                          {account.isBlocked
                            ? `${t("service.blocked")} — ${account.blockedReason ?? ""}`
                            : t("service.openAccount")}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
            {canWrite && products.length > 0 ? (
              <form
                className="search-bar"
                style={{ marginTop: "1rem" }}
                onSubmit={(e) => {
                  e.preventDefault();
                  if (!id || !productId) return;
                  setOpening(true);
                  setError(null);
                  void openSavingsAccount(id, productId)
                    .then((created) => {
                      setAccounts((current) => [...current, created]);
                      setMember((current) =>
                        current
                          ? { ...current, savingsAccounts: [...(current.savingsAccounts ?? []), created] }
                          : current
                      );
                    })
                    .catch((err: unknown) => setError(err instanceof Error ? err.message : t("savings.openError")))
                    .finally(() => setOpening(false));
                }}
              >
                <select value={productId} onChange={(e) => setProductId(e.target.value)}>
                  {products.map((product) => (
                    <option key={product.id} value={product.id}>
                      {product.name} ({product.currencyCode})
                    </option>
                  ))}
                </select>
                <button type="submit" disabled={opening}>
                  {opening ? t("savings.opening") : t("savings.open")}
                </button>
              </form>
            ) : null}
          </section>

          <section className="card-block">
            <h2>{t("service.transactions")}</h2>
            {(member.recentTransactions ?? []).length === 0 ? (
              <p className="muted">{t("service.noTransactions")}</p>
            ) : (
              <div className="table-wrap">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>{t("savings.date")}</th>
                      <th>{t("savings.accountNo")}</th>
                      <th>{t("savings.description")}</th>
                      <th>{t("savings.type")}</th>
                      <th>{t("savings.amount")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {member.recentTransactions.map((tx) => (
                      <tr key={`${tx.savingsAccountId}-${tx.postedAtUtc}-${tx.description}`}>
                        <td>{tx.valueDateUtc.slice(0, 10)}</td>
                        <td>{tx.accountNo}</td>
                        <td>{tx.description}</td>
                        <td>{tx.entryType}</td>
                        <td>{money(tx.amount, tx.currencyCode)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="card-block">
            <h2>{t("service.loans")}</h2>
            <p className="muted">{member.loansPlaceholder ?? t("service.loansNone")}</p>
          </section>

          {canServiceClient ? (
            <>
              <section className="card-block">
                <h2>{t("service.holds")}</h2>
                {accounts.map((account) => (
                  <div key={account.id} className="stack-form" style={{ marginBottom: "1rem" }}>
                    <p>
                      <strong>{account.accountNo}</strong> · {account.productName}
                      {account.isBlocked ? ` · ${t("service.blocked")}` : ""}
                    </p>
                    {(account.holds ?? []).length === 0 ? (
                      <p className="muted">{t("service.noHolds")}</p>
                    ) : (
                      <ul className="ul-reset">
                        {account.holds.map((hold) => (
                          <li key={hold.id}>
                            {money(hold.amount, account.currencyCode)} — {hold.reason}{" "}
                            <button
                              type="button"
                              className="btn-ghost"
                              onClick={() => {
                                setBusy(true);
                                void releaseHold(account.id, hold.id)
                                  .then((updated) => {
                                    setAccounts((current) => current.map((a) => (a.id === updated.id ? updated : a)));
                                  })
                                  .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
                                  .finally(() => setBusy(false));
                              }}
                            >
                              {t("service.release")}
                            </button>
                          </li>
                        ))}
                      </ul>
                    )}
                    <div className="search-bar">
                      <input
                        type="number"
                        min={0}
                        step="0.0001"
                        value={holdAmt}
                        onChange={(e) => setHoldAmt(e.target.value)}
                        placeholder={t("service.holdAmount")}
                      />
                      <input
                        value={holdReason}
                        onChange={(e) => setHoldReason(e.target.value)}
                        placeholder={t("service.reason")}
                      />
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => {
                          setBusy(true);
                          setError(null);
                          void placeHold(account.id, Number(holdAmt), holdReason)
                            .then((updated) => {
                              setAccounts((current) => current.map((a) => (a.id === updated.id ? updated : a)));
                              setHoldAmt("");
                              setHoldReason("");
                            })
                            .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
                            .finally(() => setBusy(false));
                        }}
                      >
                        {t("service.hold")}
                      </button>
                    </div>
                    {account.isBlocked ? (
                      <button
                        type="button"
                        className="btn-ghost"
                        disabled={busy}
                        onClick={() => {
                          setBusy(true);
                          void unblockAccount(account.id)
                            .then((updated) => setAccounts((current) => current.map((a) => (a.id === updated.id ? updated : a))))
                            .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
                            .finally(() => setBusy(false));
                        }}
                      >
                        {t("service.unblock")}
                      </button>
                    ) : (
                      <div className="search-bar">
                        <input
                          value={blockReason}
                          onChange={(e) => setBlockReason(e.target.value)}
                          placeholder={t("service.blockReason")}
                        />
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => {
                            setBusy(true);
                            void blockAccount(account.id, blockReason)
                              .then((updated) => {
                                setAccounts((current) => current.map((a) => (a.id === updated.id ? updated : a)));
                                setBlockReason("");
                              })
                              .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
                              .finally(() => setBusy(false));
                          }}
                        >
                          {t("service.block")}
                        </button>
                      </div>
                    )}
                    <div className="search-bar">
                      <input type="date" value={pdfFrom} onChange={(e) => setPdfFrom(e.target.value)} />
                      <input type="date" value={pdfTo} onChange={(e) => setPdfTo(e.target.value)} />
                      <button
                        type="button"
                        onClick={() => {
                          void downloadStatementPdf(account.id, pdfFrom, pdfTo).catch((err: unknown) =>
                            setError(err instanceof Error ? err.message : t("service.error"))
                          );
                        }}
                      >
                        {t("service.pdf")}
                      </button>
                    </div>
                  </div>
                ))}
              </section>

              <section className="card-block">
                <h2>{t("service.tickets")}</h2>
                <form
                  className="stack-form"
                  onSubmit={(e) => {
                    e.preventDefault();
                    if (!id) return;
                    setBusy(true);
                    setError(null);
                    void openTicket(id, ticketSubject, ticketBody, ticketAssignee || undefined)
                      .then((created) => {
                        setMember((current) =>
                          current ? { ...current, tickets: [created, ...(current.tickets ?? [])] } : current
                        );
                        setTicketSubject("");
                        setTicketBody("");
                      })
                      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")))
                      .finally(() => setBusy(false));
                  }}
                >
                  <label>
                    {t("service.subject")}
                    <input required value={ticketSubject} onChange={(e) => setTicketSubject(e.target.value)} />
                  </label>
                  <label>
                    {t("service.body")}
                    <input value={ticketBody} onChange={(e) => setTicketBody(e.target.value)} />
                  </label>
                  <label>
                    {t("service.assign")}
                    <select value={ticketAssignee} onChange={(e) => setTicketAssignee(e.target.value)}>
                      <option value="">{t("service.unassigned")}</option>
                      {staff.map((person) => (
                        <option key={person.id} value={person.id}>
                          {person.fullName}
                        </option>
                      ))}
                    </select>
                  </label>
                  <button className="btn-primary" type="submit" disabled={busy}>
                    {t("service.openTicket")}
                  </button>
                </form>
                {(member.tickets ?? []).length === 0 ? (
                  <p className="muted">{t("service.noTickets")}</p>
                ) : (
                  <ul className="ul-reset" style={{ marginTop: "1rem" }}>
                    {member.tickets.map((ticket) => (
                      <li key={ticket.id} className="card-block">
                        <strong>
                          {ticket.ticketNo} · {ticket.subject}
                        </strong>
                        <p className="muted">
                          {t(`service.ticketStatus.${ticket.status}`)} · {ticket.createdByName}
                          {ticket.assignedToName ? ` → ${ticket.assignedToName}` : ""}
                        </p>
                        {ticket.body ? <p>{ticket.body}</p> : null}
                        {ticket.status !== "Closed" ? (
                          <div className="actions">
                            <select
                              value={ticket.assignedToUserId ?? ""}
                              onChange={(e) => {
                                const userId = e.target.value;
                                if (!userId || !id) return;
                                void assignTicket(id, ticket.id, userId)
                                  .then((updated) =>
                                    setMember((current) =>
                                      current
                                        ? {
                                            ...current,
                                            tickets: current.tickets.map((item) => (item.id === updated.id ? updated : item))
                                          }
                                        : current
                                    )
                                  )
                                  .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")));
                              }}
                            >
                              <option value="">{t("service.assign")}</option>
                              {staff.map((person) => (
                                <option key={person.id} value={person.id}>
                                  {person.fullName}
                                </option>
                              ))}
                            </select>
                            <button
                              type="button"
                              className="btn-ghost"
                              onClick={() => {
                                if (!id) return;
                                void closeTicket(id, ticket.id)
                                  .then((updated) =>
                                    setMember((current) =>
                                      current
                                        ? {
                                            ...current,
                                            tickets: current.tickets.map((item) => (item.id === updated.id ? updated : item))
                                          }
                                        : current
                                    )
                                  )
                                  .catch((err: unknown) => setError(err instanceof Error ? err.message : t("service.error")));
                              }}
                            >
                              {t("service.closeTicket")}
                            </button>
                          </div>
                        ) : null}
                      </li>
                    ))}
                  </ul>
                )}
              </section>
            </>
          ) : null}
        </>
      )}
    </main>
  );
}
