// Yeni okul sihirbazının durumu, adım doğrulamaları ve istek gövdesi. Durum yalnızca bellekte
// tutulur (sayfa yenilenirse kaybolur - sırlar bilerek tarayıcı deposuna yazılmaz).
//
// Doğrulama sınırları okul uygulamasının kurulum ucuyla birebir (SchoolProfile, SchoolPolicy,
// BillingSettings, InstanceProvisioning.Validate): o uç kurulumun SONUNDA çalışır, burada
// yakalanmayan bir değer kurulumu "Okulu hazırla" adımında düşürür.
import { optionalOnly } from "@/lib/modules";
import type { ModuleEntry } from "@/lib/releases";
import type { CreateSchoolInput, SlugCheck } from "@/lib/schools";

export interface TariffDraft {
  lessonsPerMonth: string;
  monthlyAmount: string;
  effectiveFrom: string;
}

export interface WizardState {
  displayName: string;
  shortName: string;
  slug: string;
  slugTouched: boolean;
  customDomain: string;
  primaryColor: string;
  logoBase64: string | null;
  logoPreview: string | null;
  logoName: string | null;
  legalName: string;
  contactEmail: string;
  version: string;
  isCanary: boolean;
  modules: Set<string>;
  adminEmail: string;
  adminDisplayName: string;
  groupLessonSharePercent: string;
  maxWeeklyLessons: string;
  makeupNoticeHours: string;
  makeupCreditValidDays: string;
  excusedMakeupWindowDays: string;
  schoolDayStart: string;
  schoolDayEnd: string;
  multiCourseDiscountPercent: string;
  siblingDiscountPercent: string;
  dueDayOfMonth: string;
  individual: TariffDraft;
  group: TariffDraft;
  integrations: Record<string, string>;
}

export const STEPS = [
  { key: "identity", title: "Kimlik" },
  { key: "version", title: "Sürüm" },
  { key: "modules", title: "Modüller" },
  { key: "admin", title: "Yönetici" },
  { key: "policy", title: "Okul kuralları" },
  { key: "billing", title: "Aidat" },
  { key: "integrations", title: "Entegrasyonlar" },
  { key: "host", title: "Sunucu" },
  { key: "summary", title: "Özet ve kur" },
] as const;

export type StepKey = (typeof STEPS)[number]["key"];

function firstDayOfThisMonth(): string {
  const today = new Date();
  return `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-01`;
}

// Varsayılanlar bugünkü Abdera değerleri (ARCHITECTURE.md §4.2 adım 6).
export function initialState(): WizardState {
  const effectiveFrom = firstDayOfThisMonth();
  return {
    displayName: "",
    shortName: "",
    slug: "",
    slugTouched: false,
    customDomain: "",
    primaryColor: "#a84e1f",
    logoBase64: null,
    logoPreview: null,
    logoName: null,
    legalName: "",
    contactEmail: "",
    version: "",
    isCanary: false,
    modules: new Set(),
    adminEmail: "",
    adminDisplayName: "",
    groupLessonSharePercent: "40",
    maxWeeklyLessons: "4",
    makeupNoticeHours: "24",
    makeupCreditValidDays: "60",
    excusedMakeupWindowDays: "21",
    schoolDayStart: "09:00",
    schoolDayEnd: "21:00",
    multiCourseDiscountPercent: "5",
    siblingDiscountPercent: "5",
    dueDayOfMonth: "1",
    individual: { lessonsPerMonth: "4", monthlyAmount: "", effectiveFrom },
    group: { lessonsPerMonth: "4", monthlyAmount: "", effectiveFrom },
    integrations: {},
  };
}

// Okul adından kısaltma önerisi: Türkçe harfler sadeleştirilir, boşluk tireye döner.
export function suggestSlug(name: string): string {
  const map: Record<string, string> = { ç: "c", ğ: "g", ı: "i", ö: "o", ş: "s", ü: "u", â: "a", î: "i", û: "u" };
  return name
    .toLocaleLowerCase("tr-TR")
    .replace(/[çğıöşüâîû]/g, (ch) => map[ch] ?? ch)
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .replace(/^[^a-z]+/, "")
    .slice(0, 31)
    .replace(/-+$/, "");
}

// Kısaltma kuralı - platform Domain/Slug.cs (ajanın valid_slug'ıyla aynı).
export const SLUG_PATTERN = /^[a-z][a-z0-9-]{2,30}$/;
export function slugFormatError(slug: string): string | null {
  if (!SLUG_PATTERN.test(slug) || slug.endsWith("-") || slug.includes("--")) {
    return "3-31 karakter; küçük harfle başlar, yalnızca küçük harf, rakam ve tek tire.";
  }
  return null;
}

const DOMAIN_PATTERN = /^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const COLOR_PATTERN = /^#[0-9a-f]{6}$/i;

export function parseNumber(value: string): number {
  const normalized = value.trim().replace(",", ".");
  return normalized === "" ? Number.NaN : Number(normalized);
}

function intIn(value: string, min: number, max: number): boolean {
  const n = parseNumber(value);
  return Number.isInteger(n) && n >= min && n <= max;
}

function decimalIn(value: string, min: number, max: number, decimals = 2): boolean {
  const n = parseNumber(value);
  return Number.isFinite(n) && n >= min && n <= max && Math.round(n * 10 ** decimals) === n * 10 ** decimals;
}

export type Errors = Record<string, string>;

export interface ValidationContext {
  slugCheck: SlugCheck | undefined;
  slugChecking: boolean;
  catalog: ModuleEntry[] | undefined;
}

export function validateStep(step: StepKey, state: WizardState, context: ValidationContext): Errors {
  const errors: Errors = {};
  const groups = state.modules.has("groups");
  switch (step) {
    case "identity": {
      const name = state.displayName.trim();
      if (name.length === 0 || name.length > 120) errors.displayName = "Okul adı 1-120 karakter olmalı.";
      const shortName = state.shortName.trim();
      if (shortName.length === 0 || shortName.length > 40) errors.shortName = "Kısa ad 1-40 karakter olmalı.";
      const slugError = slugFormatError(state.slug);
      if (slugError) errors.slug = slugError;
      else if (context.slugChecking || !context.slugCheck) errors.slug = "Kısaltma denetleniyor…";
      else if (!context.slugCheck.available) errors.slug = context.slugCheck.reason ?? "Bu kısaltma kullanılamaz.";
      const domain = state.customDomain.trim().toLowerCase();
      if (domain && !DOMAIN_PATTERN.test(domain)) errors.customDomain = "Geçerli bir alan adı gir (örn. panel.okulum.com).";
      if (!COLOR_PATTERN.test(state.primaryColor)) errors.primaryColor = "Renk #RRGGBB biçiminde olmalı.";
      if (state.legalName.trim().length > 200) errors.legalName = "Unvan en fazla 200 karakter olabilir.";
      const contact = state.contactEmail.trim();
      if (contact && (contact.length > 254 || !EMAIL_PATTERN.test(contact))) errors.contactEmail = "Geçerli bir e-posta gir.";
      break;
    }
    case "version":
      if (!state.version) errors.version = "Bir sürüm seç.";
      break;
    case "modules":
      if (!context.catalog) errors.modules = "Modül listesi yükleniyor…";
      break;
    case "admin": {
      const email = state.adminEmail.trim();
      if (!email || email.length > 254 || !EMAIL_PATTERN.test(email)) errors.adminEmail = "Geçerli bir yönetici e-postası gir.";
      if (state.adminDisplayName.trim().length > 100) errors.adminDisplayName = "Ad en fazla 100 karakter olabilir.";
      break;
    }
    case "policy":
      if (groups && !decimalIn(state.groupLessonSharePercent, 0, 100)) errors.groupLessonSharePercent = "0 ile 100 arasında, en fazla iki ondalık.";
      if (!intIn(state.maxWeeklyLessons, 1, 14)) errors.maxWeeklyLessons = "1 ile 14 arasında bir tam sayı.";
      if (!intIn(state.makeupNoticeHours, 0, 168)) errors.makeupNoticeHours = "0 ile 168 saat arasında.";
      if (!intIn(state.makeupCreditValidDays, 1, 365)) errors.makeupCreditValidDays = "1 ile 365 gün arasında.";
      if (!intIn(state.excusedMakeupWindowDays, 1, 365)) errors.excusedMakeupWindowDays = "1 ile 365 gün arasında.";
      if (!state.schoolDayStart) errors.schoolDayStart = "Başlangıç saati gir.";
      if (!state.schoolDayEnd) errors.schoolDayEnd = "Bitiş saati gir.";
      else if (state.schoolDayStart && state.schoolDayEnd <= state.schoolDayStart) errors.schoolDayEnd = "Bitiş başlangıçtan sonra olmalı.";
      break;
    case "billing": {
      if (!decimalIn(state.multiCourseDiscountPercent, 0, 100)) errors.multiCourseDiscountPercent = "0 ile 100 arasında.";
      if (!decimalIn(state.siblingDiscountPercent, 0, 100)) errors.siblingDiscountPercent = "0 ile 100 arasında.";
      if (!intIn(state.dueDayOfMonth, 1, 28)) errors.dueDayOfMonth = "1 ile 28 arasında.";
      const tariffs: [string, TariffDraft][] = [["individual", state.individual]];
      if (groups) tariffs.push(["group", state.group]);
      for (const [prefix, tariff] of tariffs) {
        if (!intIn(tariff.lessonsPerMonth, 1, 31)) errors[`${prefix}.lessonsPerMonth`] = "1 ile 31 arasında.";
        if (!decimalIn(tariff.monthlyAmount, 0, 10_000_000)) errors[`${prefix}.monthlyAmount`] = "Aylık tutarı gir (en fazla iki ondalık).";
        if (!/^\d{4}-\d{2}-\d{2}$/.test(tariff.effectiveFrom)) errors[`${prefix}.effectiveFrom`] = "Başlangıç tarihini gir.";
      }
      break;
    }
    case "integrations":
      for (const [key, value] of Object.entries(state.integrations)) {
        if (value.includes("\n") || value.length > 2000) errors[key] = "Tek satır, en fazla 2000 karakter.";
      }
      if (state.integrations.Email__Smtp__Port?.trim() && !intIn(state.integrations.Email__Smtp__Port, 1, 65535)) {
        errors.Email__Smtp__Port = "Port 1-65535 olmalı.";
      }
      break;
    default:
      break;
  }
  return errors;
}

// Seçili modüller için girilmiş (boş olmayan) entegrasyon değerleri. Kapalı modülün alanı gönderilmez.
export function integrationsFor(state: WizardState): Record<string, string> {
  const prefixes: Record<string, string> = { whatsapp: "WhatsApp__", email: "Email__", ai: "Ai__" };
  const result: Record<string, string> = {};
  for (const [key, raw] of Object.entries(state.integrations)) {
    const value = raw.trim();
    if (!value) continue;
    const owner = Object.entries(prefixes).find(([, prefix]) => key.startsWith(prefix))?.[0];
    if (owner && !state.modules.has(owner)) continue;
    result[key] = value;
  }
  return result;
}

const toTime = (value: string) => (value.length === 5 ? `${value}:00` : value);

export function buildRequest(state: WizardState, catalog: ModuleEntry[]): CreateSchoolInput {
  const groups = state.modules.has("groups");
  const tariff = (kind: "Individual" | "Group", draft: TariffDraft) => ({
    courseKind: kind,
    lessonsPerMonth: parseNumber(draft.lessonsPerMonth),
    monthlyAmount: parseNumber(draft.monthlyAmount),
    currency: "TRY",
    effectiveFrom: draft.effectiveFrom,
  });
  const integrations = integrationsFor(state);
  return {
    slug: state.slug,
    displayName: state.displayName.trim(),
    shortName: state.shortName.trim(),
    primaryColor: state.primaryColor.toLowerCase(),
    logoBase64: state.logoBase64,
    legalName: state.legalName.trim() || null,
    contactEmail: state.contactEmail.trim() || null,
    customDomain: state.customDomain.trim().toLowerCase() || null,
    version: state.version,
    modules: optionalOnly(state.modules, catalog),
    admin: { email: state.adminEmail.trim(), displayName: state.adminDisplayName.trim() || null },
    policy: {
      // Grup dersi modülü kapalıyken pay kullanılmaz; varsayılan değer yazılır.
      groupLessonSharePercent: groups ? parseNumber(state.groupLessonSharePercent) : 40,
      maxWeeklyLessons: parseNumber(state.maxWeeklyLessons),
      makeupNoticeHours: parseNumber(state.makeupNoticeHours),
      makeupCreditValidDays: parseNumber(state.makeupCreditValidDays),
      excusedMakeupWindowDays: parseNumber(state.excusedMakeupWindowDays),
      schoolDayStart: toTime(state.schoolDayStart),
      schoolDayEnd: toTime(state.schoolDayEnd),
    },
    billing: {
      multiCourseDiscountPercent: parseNumber(state.multiCourseDiscountPercent),
      siblingDiscountPercent: parseNumber(state.siblingDiscountPercent),
      dueDayOfMonth: parseNumber(state.dueDayOfMonth),
    },
    tariffs: [tariff("Individual", state.individual), ...(groups ? [tariff("Group", state.group)] : [])],
    integrations: Object.keys(integrations).length > 0 ? integrations : null,
    isCanary: state.isCanary,
  };
}

export const MAX_LOGO_BYTES = 512 * 1024;
export const LOGO_TYPES = ["image/png", "image/jpeg", "image/webp"];
