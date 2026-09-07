import { useTranslation } from "react-i18next";

const LANGS = [
  { code: "fr", key: "lang.fr" },
  { code: "ht", key: "lang.ht" },
  { code: "en", key: "lang.en" }
] as const;

export function LanguageSwitch() {
  const { i18n, t } = useTranslation();

  return (
    <div className="lang-switch" role="group" aria-label={t("lang.label")}>
      {LANGS.map((lang) => (
        <button
          key={lang.code}
          type="button"
          className={i18n.language === lang.code ? "is-active" : undefined}
          onClick={() => {
            void i18n.changeLanguage(lang.code);
            localStorage.setItem("cpcredo.lang", lang.code);
            document.documentElement.lang = lang.code === "ht" ? "ht" : lang.code;
          }}
        >
          {t(lang.key)}
        </button>
      ))}
    </div>
  );
}
