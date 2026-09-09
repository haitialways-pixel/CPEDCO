import { useEffect, useRef, useState, type PointerEvent } from "react";
import { useTranslation } from "react-i18next";

type Props = {
  src: string;
  aspect: number;
  title: string;
  output: { width: number; height: number; mime: "image/jpeg" | "image/png"; quality?: number; fileName: string };
  onCancel: () => void;
  onConfirm: (file: File) => void;
};

export function KycCropper({ src, aspect, title, output, onCancel, onConfirm }: Props) {
  const { t } = useTranslation();
  const viewRef = useRef<HTMLDivElement>(null);
  const imgRef = useRef<HTMLImageElement>(null);
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [nat, setNat] = useState({ w: 0, h: 0 });
  const [view, setView] = useState({ w: 320, h: 320 });
  const drag = useRef<{ x: number; y: number; panX: number; panY: number } | null>(null);

  useEffect(() => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
    setNat({ w: 0, h: 0 });
  }, [src]);

  useEffect(() => {
    const node = viewRef.current;
    if (!node) return;
    const measure = () => setView({ w: node.clientWidth, h: node.clientHeight });
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(node);
    return () => observer.disconnect();
  }, [src, aspect]);

  const cover = nat.w > 0 ? Math.max(view.w / nat.w, view.h / nat.h) : 1;
  const scale = cover * zoom;
  const left = (view.w - nat.w * scale) / 2 + pan.x;
  const top = (view.h - nat.h * scale) / 2 + pan.y;

  function onPointerDown(event: PointerEvent<HTMLDivElement>) {
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { x: event.clientX, y: event.clientY, panX: pan.x, panY: pan.y };
  }

  function onPointerMove(event: PointerEvent<HTMLDivElement>) {
    if (!drag.current) return;
    setPan({
      x: drag.current.panX + (event.clientX - drag.current.x),
      y: drag.current.panY + (event.clientY - drag.current.y)
    });
  }

  function onPointerUp() {
    drag.current = null;
  }

  function confirm() {
    const img = imgRef.current;
    if (!img || nat.w === 0) return;
    const sx = Math.max(0, -left / scale);
    const sy = Math.max(0, -top / scale);
    const sw = Math.min(nat.w - sx, view.w / scale);
    const sh = Math.min(nat.h - sy, view.h / scale);
    const canvas = window.document.createElement("canvas");
    canvas.width = output.width;
    canvas.height = output.height;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.fillStyle = "#ffffff";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(img, sx, sy, sw, sh, 0, 0, canvas.width, canvas.height);
    canvas.toBlob(
      (blob) => {
        if (!blob) return;
        onConfirm(new File([blob], output.fileName, { type: output.mime }));
      },
      output.mime,
      output.quality ?? 0.9
    );
  }

  return (
    <div className="kyc-modal" role="dialog" aria-modal="true" aria-label={title}>
      <div className="kyc-modal__card kyc-modal__card--wide">
        <h3>{title}</h3>
        <p className="muted">{t("kyc.cropHint")}</p>
        <div
          ref={viewRef}
          className="kyc-crop"
          style={{ aspectRatio: `${aspect}` }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
        >
          <img
            ref={imgRef}
            src={src}
            alt=""
            draggable={false}
            className="kyc-crop__img"
            style={{
              width: nat.w * scale,
              height: nat.h * scale,
              left,
              top
            }}
            onLoad={(e) => setNat({ w: e.currentTarget.naturalWidth, h: e.currentTarget.naturalHeight })}
          />
        </div>
        <label className="kyc-crop__zoom">
          {t("kyc.zoom")}
          <input
            type="range"
            min={1}
            max={3}
            step={0.01}
            value={zoom}
            onChange={(e) => setZoom(Number(e.target.value))}
          />
        </label>
        <div className="kyc-slot__actions">
          <button type="button" className="btn-primary" disabled={nat.w === 0} onClick={confirm}>
            {t("kyc.cropConfirm")}
          </button>
          <button type="button" className="btn-ghost" onClick={onCancel}>
            {t("kyc.overrideCancel")}
          </button>
        </div>
      </div>
    </div>
  );
}
