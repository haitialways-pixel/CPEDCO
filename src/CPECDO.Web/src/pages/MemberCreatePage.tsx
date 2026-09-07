import { FormEvent, useState } from "react";
import { Link, Navigate, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import { createMember } from "../api/members";

export function MemberCreatePage() {
  const { t } = useTranslation();
  const { session } = useAuth();
  const navigate = useNavigate();
  const canWrite = session?.roles.some((r) => !r.isReadOnly) ?? false;
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({
    firstName: "",
    lastName: "",
    cin: "",
    nif: "",
    phone: "",
    alternatePhone: "",
    addressLine: "",
    city: "Pétion-Ville",
    commune: "Pétion-Ville",
    status: "Pending",
    kycStatus: "Incomplete",
    legalStatus: "Usager",
    qualificationShareCount: "0"
  });

  if (!canWrite) return <Navigate to="/members" replace />;

  function set(name: string, value: string) {
    setForm((current) => ({ ...current, [name]: value }));
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const created = await createMember({
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
      navigate(`/members/${created.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : t("members.saveError"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="page page--narrow">
      <p>
        <Link to="/members">{t("members.back")}</Link>
      </p>
      <h1>{t("members.create")}</h1>
      {error ? <p className="login-form__error">{error}</p> : null}
      <form className="stack-form" onSubmit={(e) => void onSubmit(e)}>
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
        <p className="muted">{t("members.voteRule")}</p>
        <button className="btn-primary" type="submit" disabled={busy}>
          {busy ? t("members.saving") : t("members.save")}
        </button>
      </form>
    </main>
  );
}
