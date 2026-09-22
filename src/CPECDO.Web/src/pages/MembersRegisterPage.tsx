import { FormEvent, useEffect, useState } from "react";
import { Link, Navigate, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  authorizeFicheOverride,
  convertToSocietaire,
  fetchMember360,
  searchMembers,
  subscribePermanentShares,
  updateMember,
  type Member360,
  type MemberSummary,
  type MemberWrite
} from "../api/members";
import { MemberAccountsPanel } from "../components/MemberAccountsPanel";
import { KycPieces } from "../components/KycPieces";
import { IdTrigger } from "../components/IdTrigger";
import { formatMoney } from "../money";

function canAccessMembers(roles: string[]) {
  return roles.some((r) => ["Admin", "Gerant", "OfficierCredit"].includes(r));
}

export function MembersRegisterPage() {
  const { id } = useParams();
  const { session } = useAuth();
  const roles = session?.roles.map((r) => r.name) ?? [];
  if (!canAccessMembers(roles)) return <Navigate to="/" replace />;
  return id ? <RegisterDetail id={id} /> : <RegisterList />;
}

function RegisterList() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<MemberSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function runSearch(q = query) {
    setBusy(true);
    setError(null);
    try {
      const result = await searchMembers(q);
      setItems(result.items);
      setTotal(result.total);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("members.searchError"));
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    void runSearch("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <main className="page">
      <div className="page__head">
        <h1>{t("members.title")}</h1>
        <Link className="btn-primary" to="/membres/nouveau">
          {t("members.createSocietaire")}
        </Link>
      </div>
      <form
        className="search-bar"
        onSubmit={(event: FormEvent) => {
          event.preventDefault();
          void runSearch();
        }}
      >
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={t("members.searchPlaceholder")}
        />
        <button type="submit" disabled={busy}>
          {busy ? t("members.searching") : t("members.search")}
        </button>
      </form>
      {error ? <p className="login-form__error">{error}</p> : null}
      <p className="muted">{t("members.count", { count: total })}</p>
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th>{t("members.no")}</th>
              <th>{t("members.name")}</th>
              <th>{t("members.legalStatus")}</th>
              <th>{t("members.founder")}</th>
              <th>{t("members.votes")}</th>
            </tr>
          </thead>
          <tbody>
            {items.length === 0 ? (
              <tr>
                <td colSpan={5}>{t("members.empty")}</td>
              </tr>
            ) : (
              items.map((m) => (
                <tr key={m.id} onClick={() => navigate(`/membres/${m.id}`)}>
                  <td>{m.memberNo}</td>
                  <td>{m.fullName}</td>
                  <td>{t(`members.legal.${m.legalStatus}`)}</td>
                  <td>{m.isFounder ? t("members.voteYes") : t("members.voteNo")}</td>
                  <td>{m.votingRights ? t("members.voteYes") : t("members.voteNo")}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </main>
  );
}

function RegisterDetail({ id }: { id: string }) {
  const { t } = useTranslation();
  const { session } = useAuth();
  const roles = session?.roles.map((r) => r.name) ?? [];
  const canManage = roles.some((r) => r === "Admin" || r === "Gerant");
  const canOverride = roles.some((r) => ["ServiceClient", "OfficierCredit", "Caissier"].includes(r));
  const [member, setMember] = useState<Member360 | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState(false);
  const [legalStatus, setLegalStatus] = useState("Usager");
  const [qualificationShareCount, setQualificationShareCount] = useState("0");
  const [isFounder, setIsFounder] = useState(false);
  const [overrideOpen, setOverrideOpen] = useState(false);
  const [overrideUser, setOverrideUser] = useState("");
  const [overridePassword, setOverridePassword] = useState("");
  const [grantId, setGrantId] = useState<string | null>(null);
  const canOpen = roles.some((r) => ["Admin", "Gerant", "OfficierCredit", "ServiceClient"].includes(r));

  useEffect(() => {
    void fetchMember360(id)
      .then((data) => {
        setMember(data);
        setLegalStatus(data.legalStatus);
        setQualificationShareCount(String(data.shares.qualificationShareCount));
        setIsFounder(data.isFounder);
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t("members.loadError")));
  }, [id, t]);

  if (!member && !error) return <main className="page">{t("members.loading")}</main>;
  if (!member) {
    return (
      <main className="page">
        <p>
          <Link to="/membres">{t("members.back")}</Link>
        </p>
        <p className="login-form__error">{error}</p>
      </main>
    );
  }

  function bodyFromForm(): MemberWrite {
    if (!member) throw new Error("missing");
    return {
      firstName: member.firstName,
      lastName: member.lastName,
      cin: member.cin ?? undefined,
      nif: member.nif ?? undefined,
      phone: member.phone,
      alternatePhone: member.alternatePhone ?? undefined,
      addressLine: member.addressLine,
      city: member.city,
      commune: member.commune ?? undefined,
      dateOfBirth: member.dateOfBirth,
      placeOfBirth: member.placeOfBirth ?? undefined,
      occupation: member.occupation ?? undefined,
      status: member.status,
      kycStatus: member.kycStatus,
      legalStatus,
      qualificationShareCount: Number(qualificationShareCount || 0),
      isFounder,
      founderGroup: isFounder ? member.founderGroup ?? "QualifyingFounder" : null
    };
  }

  async function onSave(event: FormEvent) {
    event.preventDefault();
    if (!member) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await updateMember(id, bodyFromForm(), grantId);
      setMember(updated);
      setIsFounder(updated.isFounder);
      setLegalStatus(updated.legalStatus);
      setEditing(false);
      setGrantId(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("members.saveError"));
    } finally {
      setBusy(false);
    }
  }

  async function submitOverride(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const granted = await authorizeFicheOverride(id, {
        username: overrideUser,
        password: overridePassword,
        action: "edit-fiche"
      });
      setGrantId(granted.grantId);
      setOverrideOpen(false);
      setOverridePassword("");
      setEditing(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("kyc.overrideError"));
    } finally {
      setBusy(false);
    }
  }

  const locked = !editing;

  return (
    <main className="page page--narrow">
      <p>
        <Link to="/membres">{t("members.back")}</Link>
      </p>
      <h1>
        {member.fullName}
        <IdTrigger lines={[{ value: member.memberNo }]} />
      </h1>
      {error ? <p className="login-form__error">{error}</p> : null}
      <MemberAccountsPanel
        member={member}
        canOpen={canOpen}
        canPay={canOpen}
        onMemberUpdated={(next) => {
          setMember(next);
          setLegalStatus(next.legalStatus);
          setQualificationShareCount(String(next.shares.qualificationShareCount));
        }}
      />
      {locked ? (
        <p className="kyc-slot__lock">
          <span aria-hidden="true">🔒</span> {t("members.ficheLocked")}
        </p>
      ) : null}
      <div className="actions" style={{ marginBottom: "0.8rem" }}>
        {canManage && !editing ? (
          <button type="button" className="btn-primary" onClick={() => setEditing(true)}>
            {t("members.editFiche")}
          </button>
        ) : null}
        {canOverride && !editing ? (
          <button type="button" className="btn-ghost" onClick={() => setOverrideOpen(true)}>
            {t("members.editFiche")}
          </button>
        ) : null}
      </div>
      <form className="stack-form" onSubmit={(e) => void onSave(e)}>
        <div className="form-grid">
          <label>
            {t("members.firstName")}
            <input
              required
              disabled={locked}
              value={member.firstName}
              onChange={(e) => setMember({ ...member, firstName: e.target.value })}
            />
          </label>
          <label>
            {t("members.lastName")}
            <input
              required
              disabled={locked}
              value={member.lastName}
              onChange={(e) => setMember({ ...member, lastName: e.target.value })}
            />
          </label>
          <label>
            {t("members.cin")}
            <input disabled={locked} value={member.cin ?? ""} onChange={(e) => setMember({ ...member, cin: e.target.value })} />
          </label>
          <label>
            {t("members.nif")}
            <input disabled={locked} value={member.nif ?? ""} onChange={(e) => setMember({ ...member, nif: e.target.value })} />
          </label>
          <label>
            {t("members.phone")}
            <input
              required
              disabled={locked}
              value={member.phone}
              onChange={(e) => setMember({ ...member, phone: e.target.value })}
            />
          </label>
          <label>
            {t("members.altPhone")}
            <input
              disabled={locked}
              value={member.alternatePhone ?? ""}
              onChange={(e) => setMember({ ...member, alternatePhone: e.target.value })}
            />
          </label>
          <label className="span-2">
            {t("members.address")}
            <input
              required
              disabled={locked}
              value={member.addressLine}
              onChange={(e) => setMember({ ...member, addressLine: e.target.value })}
            />
          </label>
          <label>
            {t("members.city")}
            <input disabled={locked} value={member.city} onChange={(e) => setMember({ ...member, city: e.target.value })} />
          </label>
          <label>
            {t("members.commune")}
            <input
              disabled={locked}
              value={member.commune ?? ""}
              onChange={(e) => setMember({ ...member, commune: e.target.value })}
            />
          </label>
          <label>
            {t("members.dateOfBirth")}
            <input
              type="date"
              disabled={locked}
              value={member.dateOfBirth ?? ""}
              onChange={(e) => setMember({ ...member, dateOfBirth: e.target.value || null })}
            />
          </label>
          <label>
            {t("members.placeOfBirth")}
            <input
              disabled={locked}
              value={member.placeOfBirth ?? ""}
              onChange={(e) => setMember({ ...member, placeOfBirth: e.target.value })}
            />
          </label>
          <label className="span-2">
            {t("members.occupation")}
            <input
              disabled={locked}
              value={member.occupation ?? ""}
              onChange={(e) => setMember({ ...member, occupation: e.target.value })}
            />
          </label>
          <label>
            {t("members.statusLabel")}
            <select
              disabled={locked}
              value={member.status}
              onChange={(e) => setMember({ ...member, status: e.target.value })}
            >
              <option value="Pending">{t("members.status.Pending")}</option>
              <option value="Active">{t("members.status.Active")}</option>
              <option value="Suspended">{t("members.status.Suspended")}</option>
              <option value="Closed">{t("members.status.Closed")}</option>
            </select>
          </label>
          <label>
            {t("members.kyc")}
            <select
              disabled={locked}
              value={member.kycStatus}
              onChange={(e) => setMember({ ...member, kycStatus: e.target.value })}
            >
              <option value="Incomplete">{t("members.kycStatus.Incomplete")}</option>
              <option value="Pending">{t("members.kycStatus.Pending")}</option>
              <option value="Verified">{t("members.kycStatus.Verified")}</option>
              <option value="Rejected">{t("members.kycStatus.Rejected")}</option>
            </select>
          </label>
        </div>
        <label>
          {t("members.legalStatus")}
          <select disabled={locked} value={legalStatus} onChange={(e) => setLegalStatus(e.target.value)}>
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
            disabled={locked}
            value={qualificationShareCount}
            onChange={(e) => setQualificationShareCount(e.target.value)}
          />
        </label>
        <label className="checkbox-row">
          <input
            type="checkbox"
            disabled={locked}
            checked={isFounder}
            onChange={(e) => setIsFounder(e.target.checked)}
          />
          {t("members.founder")}
        </label>
        <p className="muted">
          {t("members.permanentShares")}: {member.shares.permanentShareCount} ·{" "}
          {formatMoney(member.shares.qualificationBookValue, member.shares.currencyCode)}
        </p>
        <div className="actions">
          {editing ? (
            <>
              <button className="btn-primary" type="submit" disabled={busy}>
                {busy ? t("members.saving") : t("members.save")}
              </button>
              <button
                type="button"
                className="btn-ghost"
                onClick={() => {
                  setEditing(false);
                  setGrantId(null);
                }}
              >
                {t("members.cancel")}
              </button>
            </>
          ) : null}
          {member.legalStatus === "Societaire" ? (
            <button
              type="button"
              className="btn-ghost"
              disabled={busy}
              onClick={() => {
                const raw = window.prompt(t("members.permanentPrompt"), "1");
                const quantity = Number(raw);
                if (!raw || !Number.isInteger(quantity) || quantity < 1) return;
                setBusy(true);
                void subscribePermanentShares(id, quantity)
                  .then(setMember)
                  .catch((err: unknown) => setError(err instanceof Error ? err.message : t("members.saveError")))
                  .finally(() => setBusy(false));
              }}
            >
              {t("members.subscribePermanent")}
            </button>
          ) : null}
          {member.legalStatus !== "Societaire" ? (
            <button
              type="button"
              className="btn-primary"
              disabled={busy}
              onClick={() => {
                setBusy(true);
                void convertToSocietaire(id)
                  .then((updated) => {
                    setMember(updated);
                    setLegalStatus(updated.legalStatus);
                  })
                  .catch((err: unknown) => setError(err instanceof Error ? err.message : t("members.saveError")))
                  .finally(() => setBusy(false));
              }}
            >
              {t("members.convertSocietaire")}
            </button>
          ) : null}
        </div>
      </form>
      {overrideOpen ? (
        <div className="kyc-modal" role="dialog" aria-modal="true">
          <form className="kyc-modal__card stack-form" onSubmit={(e) => void submitOverride(e)}>
            <h3>{t("kyc.overrideTitle")}</h3>
            <label>
              {t("kyc.overrideUser")}
              <input value={overrideUser} onChange={(e) => setOverrideUser(e.target.value)} required />
            </label>
            <label>
              {t("kyc.overridePassword")}
              <input
                type="password"
                value={overridePassword}
                onChange={(e) => setOverridePassword(e.target.value)}
                required
              />
            </label>
            <div className="actions">
              <button className="btn-primary" type="submit" disabled={busy}>
                {t("kyc.overrideSubmit")}
              </button>
              <button type="button" className="btn-ghost" onClick={() => setOverrideOpen(false)}>
                {t("kyc.overrideCancel")}
              </button>
            </div>
          </form>
        </div>
      ) : null}
      <KycPieces
        memberId={id}
        documents={member.kycDocuments ?? []}
        onChanged={async () => {
          const next = await fetchMember360(id);
          setMember(next);
        }}
      />
    </main>
  );
}
