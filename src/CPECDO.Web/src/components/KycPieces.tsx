import { FormEvent, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthContext";
import {
  authorizeKycOverride,
  deleteKycDocument,
  fetchKycFile,
  uploadKycDocument,
  type KycDocument,
  type KycDocumentType,
  type KycOverrideAction
} from "../api/members";
import { KycCropper } from "./KycCropper";

const MAX_BYTES = 5 * 1024 * 1024;
const MANAGE_ROLES = ["Admin", "Gerant"];
const FILL_ROLES = ["Admin", "Gerant", "ServiceClient", "OfficierCredit", "Caissier"];
const ID_ASPECT = 85.6 / 54;
const PHOTO_ASPECT = 1;
const SIGN_ASPECT = 3;

type Slot = {
  type: KycDocumentType;
  labelKey: string;
  hintKey?: string;
  accept: string;
  camera?: boolean;
  frame: "photo" | "id" | "sig";
  aspect: number;
  output: { width: number; height: number; mime: "image/jpeg" | "image/png"; quality?: number; fileName: string };
};

const PHOTO: Slot = {
  type: "Photo",
  labelKey: "kyc.photo",
  hintKey: "kyc.photoHint",
  accept: "image/jpeg,image/png,.jpg,.jpeg,.png",
  camera: true,
  frame: "photo",
  aspect: PHOTO_ASPECT,
  output: { width: 600, height: 600, mime: "image/jpeg", quality: 0.88, fileName: "photo.jpg" }
};
const ID_FRONT: Slot = {
  type: "IdFront",
  labelKey: "kyc.idFront",
  accept: "image/jpeg,image/png,.jpg,.jpeg,.png",
  frame: "id",
  aspect: ID_ASPECT,
  output: { width: 856, height: 540, mime: "image/jpeg", quality: 0.88, fileName: "id-front.jpg" }
};
const ID_BACK: Slot = {
  type: "IdBack",
  labelKey: "kyc.idBack",
  accept: "image/jpeg,image/png,.jpg,.jpeg,.png",
  frame: "id",
  aspect: ID_ASPECT,
  output: { width: 856, height: 540, mime: "image/jpeg", quality: 0.88, fileName: "id-back.jpg" }
};
const SIGNATURE: Slot = {
  type: "Signature",
  labelKey: "kyc.signature",
  accept: "image/jpeg,image/png,.jpg,.jpeg,.png",
  frame: "sig",
  aspect: SIGN_ASPECT,
  output: { width: 900, height: 300, mime: "image/jpeg", quality: 0.88, fileName: "signature.jpg" }
};

type Props = {
  memberId: string;
  documents: KycDocument[];
  onChanged: () => Promise<void> | void;
};

export function KycPieces({ memberId, documents, onChanged }: Props) {
  const { t, i18n } = useTranslation();
  const { session } = useAuth();
  const roles = session?.roles.map((r) => r.name) ?? [];
  const canFill = roles.some((r) => FILL_ROLES.includes(r));
  const canManage = roles.some((r) => MANAGE_ROLES.includes(r));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<KycDocumentType | null>(null);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!open) return;
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open]);

  return (
    <section className="card-block kyc-pieces kyc-pieces--trigger">
      <button type="button" className="btn-ghost" onClick={() => setOpen(true)}>
        {t("kyc.identityPieces")}
      </button>
      {open ? (
        <div
          className="kyc-modal kyc-identity-modal"
          role="dialog"
          aria-modal="true"
          aria-labelledby="kyc-identity-title"
          onClick={() => setOpen(false)}
        >
          <div className="kyc-modal__card kyc-modal__card--identity" onClick={(e) => e.stopPropagation()}>
            <div className="kyc-identity-head">
              <h2 id="kyc-identity-title">{t("kyc.identityPieces")}</h2>
              <button type="button" className="btn-ghost" onClick={() => setOpen(false)}>
                {t("kyc.close")}
              </button>
            </div>
            {error ? (
              <p className="login-form__error" role="alert">
                {error}
              </p>
            ) : null}
            {!canFill ? <p className="muted">{t("kyc.viewOnly")}</p> : null}
            <div className="kyc-slots kyc-identity-grid">
              <KycSlotBox
                slot={PHOTO}
                memberId={memberId}
                document={documents.find((d) => d.type === "Photo")}
                canFill={canFill}
                canManage={canManage}
                busy={busy}
                locale={i18n.language}
                onBusy={setBusy}
                onError={setError}
                onChanged={onChanged}
              />
              <div className="kyc-id-group">
                <h3>{t("kyc.id")}</h3>
                <div className="kyc-id-row">
                  <KycSlotBox
                    slot={ID_FRONT}
                    memberId={memberId}
                    document={documents.find((d) => d.type === "IdFront")}
                    canFill={canFill}
                    canManage={canManage}
                    busy={busy}
                    locale={i18n.language}
                    onBusy={setBusy}
                    onError={setError}
                    onChanged={onChanged}
                  />
                  <KycSlotBox
                    slot={ID_BACK}
                    memberId={memberId}
                    document={documents.find((d) => d.type === "IdBack")}
                    canFill={canFill}
                    canManage={canManage}
                    busy={busy}
                    locale={i18n.language}
                    onBusy={setBusy}
                    onError={setError}
                    onChanged={onChanged}
                  />
                </div>
              </div>
              <KycSlotBox
                slot={SIGNATURE}
                memberId={memberId}
                document={documents.find((d) => d.type === "Signature")}
                canFill={canFill}
                canManage={canManage}
                busy={busy}
                locale={i18n.language}
                onBusy={setBusy}
                onError={setError}
                onChanged={onChanged}
              />
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}

function KycSlotBox({
  slot,
  memberId,
  document,
  canFill,
  canManage,
  busy,
  locale,
  onBusy,
  onError,
  onChanged
}: {
  slot: Slot;
  memberId: string;
  document?: KycDocument;
  canFill: boolean;
  canManage: boolean;
  busy: KycDocumentType | null;
  locale: string;
  onBusy: (type: KycDocumentType | null) => void;
  onError: (message: string | null) => void;
  onChanged: () => Promise<void> | void;
}) {
  const { t } = useTranslation();
  const fileRef = useRef<HTMLInputElement>(null);
  const scanRef = useRef<HTMLInputElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [lightbox, setLightbox] = useState(false);
  const [cropSrc, setCropSrc] = useState<string | null>(null);
  const [hasCamera, setHasCamera] = useState(
    () => typeof navigator !== "undefined" && Boolean(navigator.mediaDevices?.getUserMedia)
  );
  const [cameraOn, setCameraOn] = useState(false);
  const [stream, setStream] = useState<MediaStream | null>(null);
  const [overrideOpen, setOverrideOpen] = useState<KycOverrideAction | null>(null);
  const [overrideUser, setOverrideUser] = useState("");
  const [overridePassword, setOverridePassword] = useState("");
  const [overrideError, setOverrideError] = useState<string | null>(null);
  const [grantId, setGrantId] = useState<string | null>(null);
  const lockedBusy = busy !== null;
  const isPdf = document?.contentType === "application/pdf";
  const filledLocked = Boolean(document) && canFill && !canManage;

  useEffect(() => {
    let revoked: string | null = null;
    let cancelled = false;
    if (!document) {
      setPreviewUrl(null);
      return;
    }
    void fetchKycFile(memberId, document.type)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        revoked = url;
        setPreviewUrl(url);
      })
      .catch(() => {
        if (!cancelled) setPreviewUrl(null);
      });
    return () => {
      cancelled = true;
      if (revoked) URL.revokeObjectURL(revoked);
    };
  }, [memberId, document?.id, document?.type, document?.uploadedAtUtc]);

  useEffect(() => {
    const node = videoRef.current;
    if (node && stream) node.srcObject = stream;
    return () => {
      if (node) node.srcObject = null;
    };
  }, [stream]);

  useEffect(() => {
    return () => stopCamera(stream);
  }, [stream]);

  function stopCamera(current: MediaStream | null) {
    current?.getTracks().forEach((track) => track.stop());
  }

  function beginCrop(file: File | undefined) {
    if (!file || !canFill) return;
    onError(null);
    if (file.size > MAX_BYTES) {
      onError(t("kyc.tooLarge"));
      return;
    }
    if (!/image\/(jpeg|jpg|pjpeg|png)/i.test(file.type) && !/\.(jpe?g|png)$/i.test(file.name)) {
      onError(t("kyc.badType"));
      return;
    }
    const url = URL.createObjectURL(file);
    setCropSrc(url);
  }

  async function attach(file: File, grant?: string | null) {
    if (!canFill) return;
    onError(null);
    onBusy(slot.type);
    try {
      await uploadKycDocument(memberId, slot.type, file, document ? grant ?? grantId : undefined);
      setGrantId(null);
      await onChanged();
    } catch (err) {
      onError(err instanceof Error ? err.message : t("kyc.error"));
    } finally {
      onBusy(null);
      if (fileRef.current) fileRef.current.value = "";
      if (scanRef.current) scanRef.current.value = "";
    }
  }

  async function remove(grant?: string | null) {
    if (!canFill || !document) return;
    onError(null);
    onBusy(slot.type);
    try {
      await deleteKycDocument(memberId, slot.type, grant ?? grantId);
      setGrantId(null);
      await onChanged();
    } catch (err) {
      onError(err instanceof Error ? err.message : t("kyc.error"));
    } finally {
      onBusy(null);
    }
  }

  function requestReplace() {
    if (canManage) {
      fileRef.current?.click();
      return;
    }
    setOverrideOpen("replace");
    setOverrideError(null);
  }

  function requestDelete() {
    if (canManage) {
      if (!window.confirm(t("kyc.deleteConfirm"))) return;
      void remove();
      return;
    }
    setOverrideOpen("delete");
    setOverrideError(null);
  }

  async function submitOverride(event: FormEvent) {
    event.preventDefault();
    if (!document || !overrideOpen) return;
    setOverrideError(null);
    onBusy(slot.type);
    try {
      const granted = await authorizeKycOverride(document.id, {
        username: overrideUser,
        password: overridePassword,
        action: overrideOpen
      });
      setGrantId(granted.grantId);
      setOverridePassword("");
      setOverrideOpen(null);
      if (overrideOpen === "replace") {
        fileRef.current?.click();
      } else {
        await remove(granted.grantId);
      }
    } catch (err) {
      setOverrideError(err instanceof Error ? err.message : t("kyc.overrideError"));
    } finally {
      onBusy(null);
    }
  }

  async function startCamera() {
    onError(null);
    try {
      const next = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "user" }, audio: false });
      setStream(next);
      setCameraOn(true);
    } catch {
      setHasCamera(false);
      stopCamera(stream);
      setStream(null);
      setCameraOn(false);
    }
  }

  function closeCamera() {
    stopCamera(stream);
    setStream(null);
    setCameraOn(false);
  }

  function takePhoto() {
    const video = videoRef.current;
    if (!video) return;
    const canvas = window.document.createElement("canvas");
    canvas.width = video.videoWidth || 640;
    canvas.height = video.videoHeight || 480;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
    canvas.toBlob(
      (blob: Blob | null) => {
        if (!blob) return;
        closeCamera();
        beginCrop(new File([blob], "photo.jpg", { type: "image/jpeg" }));
      },
      "image/jpeg",
      0.92
    );
  }

  const uploadedLabel = document
    ? t("kyc.uploaded", {
        date: new Date(document.uploadedAtUtc).toLocaleString(locale.startsWith("ht") ? "fr-HT" : locale),
        name: document.uploadedByName || "—"
      })
    : null;

  return (
    <div className={`kyc-slot kyc-slot--${slot.frame}${document ? " kyc-slot--filled" : ""}`}>
      <p className="kyc-slot__label">{t(slot.labelKey)}</p>
      {slot.hintKey ? <p className="muted kyc-slot__hint">{t(slot.hintKey)}</p> : null}
      <input
        ref={fileRef}
        type="file"
        accept={slot.accept}
        hidden
        disabled={!canFill || lockedBusy}
        onChange={(e) => beginCrop(e.target.files?.[0])}
      />
      <input
        ref={scanRef}
        type="file"
        accept={slot.accept}
        capture="environment"
        hidden
        disabled={!canFill || lockedBusy}
        onChange={(e) => beginCrop(e.target.files?.[0])}
      />
      <button
        type="button"
        className={`kyc-frame kyc-frame--${slot.frame}${previewUrl && !isPdf ? " kyc-frame--clickable" : ""}`}
        onClick={() => {
          /* contained preview only — never full-bleed */
        }}
        disabled
        aria-label={t("kyc.preview")}
      >
        {previewUrl && !isPdf ? (
          <img src={previewUrl} alt={t(slot.labelKey)} />
        ) : isPdf && previewUrl ? (
          <span className="kyc-slot__pdf">{t("kyc.pdf")}</span>
        ) : (
          <span className="kyc-frame__placeholder" />
        )}
      </button>
      {uploadedLabel ? <p className="muted kyc-slot__meta">{uploadedLabel}</p> : null}
      {filledLocked ? (
        <p className="kyc-slot__lock">
          <span aria-hidden="true">🔒</span> {t("kyc.locked")}
          <span className="muted"> — {t("kyc.lockedGerant")}</span>
        </p>
      ) : null}
      <div className="kyc-slot__actions">
        {canFill && !document ? (
          <>
            <button type="button" className="btn-primary" disabled={lockedBusy} onClick={() => fileRef.current?.click()}>
              {t("kyc.joinFile")}
            </button>
            <button type="button" className="btn-ghost" disabled={lockedBusy} onClick={() => scanRef.current?.click()}>
              {t("kyc.scan")}
            </button>
            {slot.camera && hasCamera ? (
              <button type="button" className="btn-ghost" disabled={lockedBusy} onClick={() => void startCamera()}>
                {t("kyc.camera")}
              </button>
            ) : null}
          </>
        ) : null}
        {canFill && document && canManage ? (
          <>
            <button type="button" className="btn-ghost" disabled={lockedBusy} onClick={requestReplace}>
              {t("kyc.replace")}
            </button>
            <button type="button" className="btn-ghost" disabled={lockedBusy} onClick={requestDelete}>
              {t("kyc.delete")}
            </button>
          </>
        ) : null}
        {filledLocked ? (
          <>
            <button type="button" className="btn-ghost" disabled={lockedBusy} onClick={requestReplace}>
              {t("kyc.replace")}
            </button>
            <button type="button" className="btn-ghost" disabled={lockedBusy} onClick={requestDelete}>
              {t("kyc.delete")}
            </button>
          </>
        ) : null}
      </div>
      {canFill && !document ? <p className="muted kyc-slot__hint">{t("kyc.scanHint")}</p> : null}

      {cropSrc ? (
        <KycCropper
          src={cropSrc}
          aspect={slot.aspect}
          title={t("kyc.crop")}
          output={slot.output}
          onCancel={() => {
            URL.revokeObjectURL(cropSrc);
            setCropSrc(null);
          }}
          onConfirm={(file) => {
            URL.revokeObjectURL(cropSrc);
            setCropSrc(null);
            void attach(file);
          }}
        />
      ) : null}

      {lightbox && previewUrl ? (
        <div className="kyc-modal" role="dialog" aria-modal="true" aria-label={t("kyc.preview")}>
          <button type="button" className="kyc-lightbox" onClick={() => setLightbox(false)}>
            <span className={`kyc-frame kyc-frame--${slot.frame} kyc-frame--lightbox`}>
              <img src={previewUrl} alt={t(slot.labelKey)} />
            </span>
          </button>
        </div>
      ) : null}

      {cameraOn ? (
        <div className="kyc-modal" role="dialog" aria-modal="true" aria-label={t("kyc.camera")}>
          <div className="kyc-modal__card">
            <h3>{t("kyc.camera")}</h3>
            <div className="kyc-camera-wrap">
              <video ref={videoRef} autoPlay playsInline muted className="kyc-camera" />
              <span className="kyc-camera__square" />
            </div>
            <div className="kyc-slot__actions">
              <button type="button" className="btn-primary" onClick={takePhoto}>
                {t("kyc.takePhoto")}
              </button>
              <button type="button" className="btn-ghost" onClick={closeCamera}>
                {t("kyc.overrideCancel")}
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {overrideOpen ? (
        <div className="kyc-modal" role="dialog" aria-modal="true" aria-labelledby="kyc-override-title">
          <form className="kyc-modal__card stack-form" onSubmit={(e) => void submitOverride(e)}>
            <h3 id="kyc-override-title">{t("kyc.overrideTitle")}</h3>
            {overrideError ? (
              <p className="login-form__error" role="alert">
                {overrideError}
              </p>
            ) : null}
            <label>
              {t("kyc.overrideUser")}
              <input
                autoComplete="username"
                value={overrideUser}
                onChange={(e) => setOverrideUser(e.target.value)}
                required
              />
            </label>
            <label>
              {t("kyc.overridePassword")}
              <input
                type="password"
                autoComplete="current-password"
                value={overridePassword}
                onChange={(e) => setOverridePassword(e.target.value)}
                required
              />
            </label>
            <div className="kyc-slot__actions">
              <button type="submit" className="btn-primary" disabled={lockedBusy}>
                {t("kyc.overrideSubmit")}
              </button>
              <button
                type="button"
                className="btn-ghost"
                onClick={() => {
                  setOverrideOpen(null);
                  setOverridePassword("");
                }}
              >
                {t("kyc.overrideCancel")}
              </button>
            </div>
          </form>
        </div>
      ) : null}
    </div>
  );
}
