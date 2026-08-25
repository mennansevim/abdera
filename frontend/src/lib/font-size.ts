export type FontSizePreference = "small" | "standard" | "large";

export const FONT_SIZE_STORAGE_KEY = "abdera-font-size";
export const FONT_SIZE_CHANGE_EVENT = "abdera-font-size-change";
export const DEFAULT_FONT_SIZE: FontSizePreference = "standard";

export function isFontSizePreference(value: string | null): value is FontSizePreference {
  return value === "small" || value === "standard" || value === "large";
}

export function readFontSizePreference(): FontSizePreference {
  if (typeof window === "undefined") return DEFAULT_FONT_SIZE;
  const stored = window.localStorage.getItem(FONT_SIZE_STORAGE_KEY);
  return isFontSizePreference(stored) ? stored : DEFAULT_FONT_SIZE;
}

export function applyFontSizePreference(preference: FontSizePreference) {
  document.documentElement.dataset.fontSize = preference;
  window.localStorage.setItem(FONT_SIZE_STORAGE_KEY, preference);
  window.dispatchEvent(new Event(FONT_SIZE_CHANGE_EVENT));
}
