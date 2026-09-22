import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

export type IdLine = { value?: string | null };

export function useIdOpen(initial = false) {
  const [open, setOpen] = useState(initial);
  return {
    open,
    toggle: () => setOpen((value) => !value),
    setOpen
  };
}

export function IdTrigger({
  lines,
  open,
  onToggle
}: {
  lines: IdLine[];
  open?: boolean;
  onToggle?: () => void;
}) {
  const { t } = useTranslation();
  const [innerOpen, setInnerOpen] = useState(false);
  const isOpen = open ?? innerOpen;
  const values = lines.map((line) => (line.value ?? "").trim()).filter(Boolean);

  useEffect(() => {
    if (!isOpen) return;
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") {
        if (onToggle) onToggle();
        else setInnerOpen(false);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [isOpen, onToggle]);

  if (values.length === 0) return null;

  return (
    <span className="id-trigger">
      <button
        type="button"
        className="btn-ghost"
        aria-expanded={isOpen}
        aria-label={t("members.no")}
        onClick={() => {
          if (onToggle) onToggle();
          else setInnerOpen((value) => !value);
        }}
      >
        {t("members.no")}
      </button>
      {isOpen ? <span className="id-trigger__values">{values.join(" · ")}</span> : null}
    </span>
  );
}
