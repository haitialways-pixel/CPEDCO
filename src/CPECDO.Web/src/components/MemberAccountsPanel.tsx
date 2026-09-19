import { FormEvent, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { fetchMember360, payShares, type Member360 } from "../api/members";
import {
  fetchMemberSavings,
  fetchSavingsProducts,
  openMemberAccount,
  type SavingsAccount,
  type SavingsProduct
} from "../api/savings";
import { formatMoney } from "../money";

type Props = {
  member: Member360;
  canOpen: boolean;
  canPay?: boolean;
  onMemberUpdated?: (member: Member360) => void;
};

export function MemberAccountsPanel({ member, canOpen, canPay = false, onMemberUpdated }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [products, setProducts] = useState<SavingsProduct[]>([]);
  const [accounts, setAccounts] = useState<SavingsAccount[]>(member.savingsAccounts ?? []);
  const [openKind, setOpenKind] = useState("Epargne");
  const [productId, setProductId] = useState("");
  const [payType, setPayType] = useState(
    member.legalStatus === "Auxiliaire" ? "Permanent" : "Qualification"
  );
  const [payUnits, setPayUnits] = useState("1");
  const [payAmount, setPayAmount] = useState("");
  const [paySource, setPaySource] = useState("Till");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [productError, setProductError] = useState<string | null>(null);

  const filteredProducts = products.filter(
    (p) => member.legalStatus !== "Usager" || p.productKind === "AVue"
  );

  useEffect(() => {
    setAccounts(member.savingsAccounts ?? []);
  }, [member]);

  useEffect(() => {
    void fetchSavingsProducts()
      .then((list) => {
        setProducts(list);
        setProductId((current) => current || list.find((p) => p.productKind === "AVue")?.id || list[0]?.id || "");
        setProductError(null);
      })
      .catch((err: unknown) =>
        setProductError(err instanceof Error ? err.message : t("savings.productError"))
      );
    void fetchMemberSavings(member.id)
      .then(setAccounts)
      .catch(() => undefined);
  }, [member.id, t]);

  async function refreshMember() {
    const next = await fetchMember360(member.id);
    onMemberUpdated?.(next);
    setAccounts(next.savingsAccounts ?? (await fetchMemberSavings(member.id)));
    return next;
  }

  async function onOpen(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const opened = await openMemberAccount(
        member.id,
        openKind,
        openKind === "Epargne" ? productId : undefined
      );
      await refreshMember();
      setInfo(t("savings.opened", { no: opened.accountNo }));
    } catch (err) {
      setError(err instanceof Error ? err.message : t("savings.openError"));
    } finally {
      setBusy(false);
    }
  }

  async function onPay(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const units = Number(payUnits);
      const amount = Number(payAmount);
      await payShares(member.id, {
        shareType: payType,
        units: Number.isFinite(units) && units > 0 ? units : undefined,
        amount: Number.isFinite(amount) && amount > 0 ? amount : undefined,
        source: paySource
      });
      await refreshMember();
      setInfo(t("members.payOk"));
    } catch (err) {
      setError(err instanceof Error ? err.message : t("members.saveError"));
    } finally {
      setBusy(false);
    }
  }

  const shares = member.shares;
  const canPayThis = canPay && member.legalStatus !== "Usager";

  return (
    <section className="card-block accounts-panel" id="comptes">
      <h2>{t("savings.accountsTitle")}</h2>
      {error ? (
        <p className="login-form__error" role="alert">
          {error}
        </p>
      ) : null}
      {info ? (
        <p className="backup-info" role="status">
          {info}
        </p>
      ) : null}

      {accounts.length === 0 && !shares.qualificationAccountNo && !shares.permanentAccountNo ? (
        <p className="muted">{t("savings.noneYet")}</p>
      ) : (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>{t("savings.accountNo")}</th>
                <th>{t("savings.product")}</th>
                <th>{t("savings.available")}</th>
                <th>{t("service.status")}</th>
              </tr>
            </thead>
            <tbody>
              {accounts.map((account) => (
                <tr
                  key={account.id}
                  onClick={() => navigate(`/savings-accounts/${account.id}`)}
                  style={{ cursor: "pointer" }}
                >
                  <td>
                    <Link to={`/savings-accounts/${account.id}`}>{account.accountNo}</Link>
                  </td>
                  <td>
                    {account.productName} ({account.productKind})
                  </td>
                  <td>{formatMoney(account.availableBalance, account.currencyCode)}</td>
                  <td>{account.isBlocked ? t("service.blocked") : t("service.openAccount")}</td>
                </tr>
              ))}
              {shares.qualificationAccountNo ? (
                <tr>
                  <td>{shares.qualificationAccountNo}</td>
                  <td>{t("savings.kindQual")}</td>
                  <td>{formatMoney(shares.qualificationBookValue, shares.currencyCode)}</td>
                  <td>
                    {shares.qualificationShareCount} {t("members.qualificationShares")}
                  </td>
                </tr>
              ) : null}
              {shares.permanentAccountNo ? (
                <tr>
                  <td>{shares.permanentAccountNo}</td>
                  <td>{t("savings.kindPerm")}</td>
                  <td>{formatMoney(shares.permanentBookValue, shares.currencyCode)}</td>
                  <td>
                    {shares.permanentShareCount} {t("members.permanentShares")}
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      )}

      {canOpen ? (
        <form className="stack-form" style={{ marginTop: "1rem" }} onSubmit={(e) => void onOpen(e)}>
          <h3>{t("savings.open")}</h3>
          {productError ? <p className="login-form__error">{productError}</p> : null}
          <div className="search-bar">
            <label className="accounts-panel__field">
              {t("savings.openKind")}
              <select value={openKind} onChange={(e) => setOpenKind(e.target.value)}>
                <option value="Epargne">{t("savings.kindEpargne")}</option>
                {member.legalStatus === "Societaire" ? (
                  <option value="Qualification">{t("savings.kindQual")}</option>
                ) : null}
                {member.legalStatus === "Societaire" || member.legalStatus === "Auxiliaire" ? (
                  <option value="Permanent">{t("savings.kindPerm")}</option>
                ) : null}
              </select>
            </label>
            {openKind === "Epargne" ? (
              <label className="accounts-panel__field">
                {t("savings.product")}
                <select value={productId} onChange={(e) => setProductId(e.target.value)} required>
                  {filteredProducts.length === 0 ? (
                    <option value="">{t("savings.productsEmpty")}</option>
                  ) : (
                    filteredProducts.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.displayName || p.name} ({p.currencyCode})
                      </option>
                    ))
                  )}
                </select>
              </label>
            ) : null}
            <button className="btn-primary" type="submit" disabled={busy || (openKind === "Epargne" && !productId)}>
              {busy ? t("savings.opening") : t("savings.open")}
            </button>
          </div>
          {openKind === "Epargne" && filteredProducts.length === 0 ? (
            <p className="muted">
              {t("savings.needProduct")}{" "}
              <Link to="/epargne/produits">{t("nav.savingsProducts")}</Link>
            </p>
          ) : null}
        </form>
      ) : (
        <p className="muted">{t("savings.openHint")}</p>
      )}

      {canPayThis ? (
        <form className="stack-form" style={{ marginTop: "1rem" }} onSubmit={(e) => void onPay(e)}>
          <h3>{t("members.payShares")}</h3>
          <div className="search-bar">
            <select value={payType} onChange={(e) => setPayType(e.target.value)}>
              {member.legalStatus === "Societaire" ? (
                <option value="Qualification">{t("savings.kindQual")}</option>
              ) : null}
              {member.legalStatus === "Societaire" || member.legalStatus === "Auxiliaire" ? (
                <option value="Permanent">{t("savings.kindPerm")}</option>
              ) : null}
            </select>
            <input
              type="number"
              min={1}
              step={1}
              value={payUnits}
              onChange={(e) => setPayUnits(e.target.value)}
              placeholder={t("members.payUnits")}
            />
            <input
              type="number"
              min={0}
              step="0.01"
              value={payAmount}
              onChange={(e) => setPayAmount(e.target.value)}
              placeholder={t("members.payAmount")}
            />
            <select value={paySource} onChange={(e) => setPaySource(e.target.value)}>
              <option value="Till">{t("members.payTill")}</option>
              <option value="Vault">{t("members.payVault")}</option>
            </select>
            <button type="submit" disabled={busy}>
              {t("members.payShares")}
            </button>
          </div>
        </form>
      ) : null}
    </section>
  );
}
