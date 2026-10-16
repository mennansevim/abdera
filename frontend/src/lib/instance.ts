// Bu okul kurulumunun kimliği ve açık modülleri (docs/multi-tenant/ARCHITECTURE.md §5).
// Backend: GET /api/instance/config (anonim; giriş ekranı da okul adını gösterir).
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, apiBaseUrl } from "./api";

// Backend Shared/Modules/ModuleCatalog.cs ModuleKeys ile birebir.
export type ModuleKey =
  | "core.people"
  | "core.scheduling"
  | "core.attendance"
  | "core.billing"
  | "core.notifications"
  | "core.ops"
  | "progress"
  | "library"
  | "show"
  | "banking"
  | "profitability"
  | "parent-portal"
  | "groups"
  | "finance"
  | "whatsapp"
  | "email"
  | "ai";

export interface InstanceModule {
  key: ModuleKey;
  name: string;
  isCore: boolean;
  enabled: boolean;
  requires: ModuleKey[];
}

export interface SchoolProfile {
  name: string;
  shortName: string;
  primaryColor: string | null;
  logoUrl: string | null;
  // Gizlilik metninin veri sorumlusu unvanı ve KVKK başvuru adresi; boşsa okulun adı kullanılır.
  legalName?: string | null;
  contactEmail?: string | null;
}

// Backend TimeOnly "HH:mm:ss" olarak gelir.
export interface SchoolDay {
  start: string;
  end: string;
}

// Alt birim (branş) terimleri ve kategori kataloğu (backend Shared/Branches/BranchCatalog.cs).
// Tek kategorili okulda kategorinin terimi ("Enstrüman", "Dans türü"), karışık okulda "Branş".
export interface BranchTerms {
  branch: string;
  branchPlural: string;
}

// Cümle içi kullanım ("Ad veya enstrüman ara"): Türkçe küçük harf (İ -> i).
export interface BranchWords extends BranchTerms {
  branchLower: string;
  branchPluralLower: string;
}

export interface BranchCategory {
  key: string;
  name: string;
  unitLabel: string;
  unitLabelPlural: string;
  suggestions: string[];
  inUse: boolean;
  // Okulun kendi açtığı alan ("c-..." anahtarlı); katalog alanları düzenlenemez.
  isCustom?: boolean;
}

export interface InstanceConfig {
  profile: SchoolProfile;
  modules: InstanceModule[];
  schoolDay?: SchoolDay;
  demo?: boolean;
  terms?: BranchTerms;
  categories?: BranchCategory[];
}

// Yapılandırma okunamazsa (yükleniyor, ağ hatası) nötr kimlik ve "her şey açık" varsayılır: hiçbir
// ekran kaybolmaz; okulun adı yapılandırma gelince görünür. Ürünün adı hiçbir okulda görünmez.
export const DEFAULT_PROFILE: SchoolProfile = {
  name: "Okul",
  shortName: "Okul",
  primaryColor: null,
  logoUrl: null,
};

export function useInstanceConfig() {
  return useQuery({
    queryKey: ["instance-config"],
    queryFn: () => api.get<InstanceConfig>("/api/instance/config"),
    // Modül listesi kurulum anında belirlenir; oturum boyunca değişmesi beklenmez.
    staleTime: 5 * 60_000,
    retry: 1,
  });
}

// Yapılandırma gelmeden (ya da eski API'de) bugünkü müzik okulunun terimi kullanılır.
export const DEFAULT_TERMS: BranchTerms = { branch: "Enstrüman", branchPlural: "Enstrümanlar" };

export function useBranchTerms(): BranchWords {
  const { data } = useInstanceConfig();
  const terms = data?.terms ?? DEFAULT_TERMS;
  return {
    ...terms,
    branchLower: terms.branch.toLocaleLowerCase("tr-TR"),
    branchPluralLower: terms.branchPlural.toLocaleLowerCase("tr-TR"),
  };
}

export function useBranchCategories(): BranchCategory[] {
  const { data } = useInstanceConfig();
  return data?.categories ?? [];
}

// Müziğe özel ekranlar (enstrüman bakımı, nota kütüphanesi dili) yalnızca müzik öğreten okulda.
export function useTeachesMusic(): boolean {
  const { data } = useInstanceConfig();
  return !data?.categories || data.categories.some((category) => category.key === "music" && category.inUse);
}

export function useSchoolProfile(): SchoolProfile {
  const { data } = useInstanceConfig();
  return data?.profile ?? DEFAULT_PROFILE;
}

// Bilinmeyen/yüklenmemiş yapılandırmada modül açık sayılır (bkz. DEFAULT_PROFILE notu).
export function isModuleEnabled(config: InstanceConfig | undefined, key: ModuleKey | undefined): boolean {
  if (!key || !config) return true;
  return config.modules.find((module) => module.key === key)?.enabled ?? true;
}

export function useModuleEnabled(key: ModuleKey): boolean {
  const { data } = useInstanceConfig();
  return isModuleEnabled(data, key);
}

// Bir sorguyu modüle bağlarken kullanılır: yapılandırma gelene kadar false döner ki kapalı modülün
// ucuna açılışta bir kez 404 isteği gitmesin. Yapılandırma okunamazsa (hata) açık sayılır.
export function useModuleQueryEnabled(key: ModuleKey): boolean {
  const { data, isPending, isError } = useInstanceConfig();
  if (isPending && !isError) return false;
  return isModuleEnabled(data, key);
}

// Logo yüklenmemişse null: çağıran baş harfleri gösterir (ürünün logosu hiçbir okulda görünmez).
export function schoolLogoSrc(profile: SchoolProfile): string | null {
  return profile.logoUrl ? `${apiBaseUrl()}${profile.logoUrl}` : null;
}

// Backend SchoolProfile.MaxLogoBytes ile aynı; sunucu yine kendisi denetler (tür dosya içeriğinden).
export const SCHOOL_LOGO_MAX_BYTES = 512 * 1024;

export interface UpdateSchoolProfileRequest {
  name: string;
  shortName: string;
  primaryColor: string | null;
  legalName?: string;
  contactEmail?: string;
}

// Profil/logo değişince menü, giriş ekranı ve diğer sekmeler bir sonraki config okumasında yenilenir.
function useInvalidateInstanceConfig() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ["instance-config"] });
}

export function useUpdateSchoolProfile() {
  const invalidate = useInvalidateInstanceConfig();
  return useMutation<SchoolProfile, ApiError, UpdateSchoolProfileRequest>({
    mutationFn: (request) => api.put<SchoolProfile>("/api/instance/profile", request),
    onSuccess: invalidate,
  });
}

export function useUploadSchoolLogo() {
  const invalidate = useInvalidateInstanceConfig();
  return useMutation<SchoolProfile, ApiError, File>({
    mutationFn: async (file) => {
      const body = new FormData();
      body.append("file", file);
      const response = await fetch(`${apiBaseUrl()}/api/instance/logo`, { method: "PUT", credentials: "include", body });
      if (!response.ok) {
        // FormData yüklemesi api.ts'teki JSON istemcisini kullanamaz; hata gövdesi aynı biçime çevrilir.
        const problem = await response.json().catch(() => null);
        const fieldError = problem?.errors ? Object.values(problem.errors as Record<string, string[]>)[0]?.[0] : undefined;
        throw new ApiError(response.status, problem?.title ?? "Logo yüklenemedi.", fieldError ?? problem?.detail ?? "Logo yüklenemedi.");
      }
      return (await response.json()) as SchoolProfile;
    },
    onSuccess: invalidate,
  });
}

export function useRemoveSchoolLogo() {
  const invalidate = useInvalidateInstanceConfig();
  return useMutation<void, ApiError, void>({
    mutationFn: () => api.delete<void>("/api/instance/logo"),
    onSuccess: invalidate,
  });
}

// Okul günü saatleri (school_policy). Yapılandırma gelmeden bugünkü 09:00–21:00 geçerlidir.
export const DEFAULT_SCHOOL_DAY: SchoolDay = { start: "09:00", end: "21:00" };

export function useSchoolDay(): SchoolDay {
  const { data } = useInstanceConfig();
  return data?.schoolDay ? { start: data.schoolDay.start.slice(0, 5), end: data.schoolDay.end.slice(0, 5) } : DEFAULT_SCHOOL_DAY;
}

export interface SchoolPolicy {
  groupLessonSharePercent: number;
  maxWeeklyLessons: number;
  makeupNoticeHours: number;
  makeupCreditValidDays: number;
  excusedMakeupWindowDays: number;
  schoolDayStart: string;
  schoolDayEnd: string;
}

export function useSchoolPolicy(enabled = true) {
  return useQuery({ queryKey: ["school-policy"], queryFn: () => api.get<SchoolPolicy>("/api/instance/policy"), enabled });
}

export function useUpdateSchoolPolicy() {
  const queryClient = useQueryClient();
  return useMutation<SchoolPolicy, ApiError, SchoolPolicy>({
    mutationFn: (policy) => api.put<SchoolPolicy>("/api/instance/policy", policy),
    onSuccess: (policy) => {
      queryClient.setQueryData(["school-policy"], policy);
      return queryClient.invalidateQueries({ queryKey: ["instance-config"] });
    },
  });
}

// Akıllı zamanlama önerilerinin uyacağı okul kuralları (lib/smart-scheduling.ts SchedulingRules).
export function useSchedulingRules(): { schoolDay: SchoolDay; maxWeeklyLessons?: number } {
  const schoolDay = useSchoolDay();
  const { data: policy } = useSchoolPolicy();
  return { schoolDay, maxWeeklyLessons: policy?.maxWeeklyLessons };
}

// Tanıtım (demo) yayını: sunucunun Demo:Enabled ayarından okunur. Eski derleme anı bayrağı
// (NEXT_PUBLIC_DEMO_ENABLED) geriye dönük uyumluluk için hâlâ açabilir.
const BUILD_TIME_DEMO = process.env.NEXT_PUBLIC_DEMO_ENABLED === "true";

export function useDemoEnabled(): boolean {
  const { data } = useInstanceConfig();
  return BUILD_TIME_DEMO || Boolean(data?.demo);
}
