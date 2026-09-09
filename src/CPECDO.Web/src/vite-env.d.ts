/// <reference types="vite/client" />

/** Production uses relative /api paths. Do not add VITE_API_URL. */
interface ImportMetaEnv {
  readonly VITE_API_URL?: never;
}
