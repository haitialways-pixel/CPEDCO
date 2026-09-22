import { useTranslation } from "react-i18next";

type Props = {
  compact?: boolean;
};

export function InstitutionHeader({ compact = false }: Props) {
  const { t } = useTranslation();

  if (compact) {
    return (
      <div className="letterhead letterhead--plaque">
        <span className="letterhead__flag letterhead__flag--edge" aria-hidden="true">
          <span className="flag-blue" />
          <span className="flag-red" />
        </span>
        <p className="letterhead__sigle">{t("letterhead.sigle")}</p>
        <p className="letterhead__legal">
          <span className="letterhead__line2">{t("letterhead.line2")}</span>
          <span className="letterhead__line3">
            {t("letterhead.line3")}
            {" · "}
            {t("letterhead.line4")}
          </span>
        </p>
      </div>
    );
  }

  return (
    <header className="letterhead">
      <div className="letterhead__flag" aria-hidden="true">
        <span className="flag-blue" />
        <span className="flag-red" />
      </div>
      <p className="letterhead__sigle">{t("letterhead.sigle")}</p>
      <p className="letterhead__line2">{t("letterhead.line2")}</p>
      <p className="letterhead__line3">{t("letterhead.line3")}</p>
      <p className="letterhead__line4">{t("letterhead.line4")}</p>
    </header>
  );
}
