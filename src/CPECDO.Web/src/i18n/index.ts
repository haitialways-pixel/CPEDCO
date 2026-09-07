import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import { resources } from "./locales";

const stored = localStorage.getItem("cpcredo.lang");
const lng = stored === "ht" || stored === "en" || stored === "fr" ? stored : "fr";

void i18n.use(initReactI18next).init({
  resources,
  lng,
  fallbackLng: "fr",
  keySeparator: false,
  nsSeparator: false,
  interpolation: { escapeValue: false }
});

export default i18n;
