import { useTranslation } from "react-i18next";

type Props = {
  compact?: boolean;
};

export function InstitutionHeader({ compact = false }: Props) {
  const { t } = useTranslation();

  return (
    <header className={compact ? "letterhead letterhead--compact" : "letterhead"}>
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
