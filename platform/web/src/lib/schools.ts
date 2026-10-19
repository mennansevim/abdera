"use client";

// Okullar: kutucuk listesi, sihirbaz, detay ve okul işlemleri (platform/api Features/Schools.cs).
// Kayıt tipleri backend record'larının camelCase JSON karşılığıdır; enum'lar metin olarak gelir.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export type SchoolStatus = "Provisioning" | "Active" | "Failed" | "Suspended" | "PendingDestroy" | "Destroyed";
export type RunKind = "Create" | "Upgrade" | "Suspend" | "Resume" | "Destroy" | "Undestroy" | "Purge" | "Cancel" | "Reconfigure" | "Drill" | "Adopt" | "DomainAttach" | "DomainDetach";
export type RunStatus = "Pending" | "Running" | "Succeeded" | "Failed" | "Cancelled";
export type StepStatus = "Running" | "Succeeded" | "Failed";

export interface Telemetry {
  takenAt: string;
  reachable: boolean;
  health: string | null;
  healthDetail: string | null;
  appVersion: string | null;
  activeStudents: number | null;
  activeTeachers: number | null;
  activeEnrollments: number | null;
  lastBackupAt: string | null;
  databaseSizeBytes: number | null;
  pendingNotifications: number | null;
  failedNotifications: number | null;
  error: string | null;
}

export interface RunSummary {
  id: string;
  kind: RunKind;
  status: RunStatus;
  currentStep: number;
  totalSteps: number;
  currentStepName: string | null;
  error: string | null;
  createdAt: string;
}

export interface SchoolTile {
  id: string;
  slug: string;
  displayName: string;
  shortName: string;
  primaryColor: string | null;
  status: SchoolStatus;
  primaryDomain: string;
  appVersion: string | null;
  targetVersion: string | null;
  hostName: string | null;
  isCanary: boolean;
  isVersionPinned: boolean;
  telemetry: Telemetry | null;
  openRun: RunSummary | null;
  openAlerts: number;
  // Okulun öğrettiği alan kategorileri (music, dance, sport...). Eski kayıtlarda boş.
  categories?: string[];
  // Okulun kendi açtığı alanların adları (katalog alanlarının adı sürüm kataloğundan gelir).
  customCategories?: { key: string; name: string }[];
  // Okula girilen adres: bağlı özel alan adı varsa o, yoksa platform alt alanı.
  publicDomain?: string;
}

export interface Step {
  step: number;
  name: string;
  status: StepStatus;
  attempt: number;
  log: string | null;
  startedAt: string;
  finishedAt: string | null;
}

export interface Run {
  id: string;
  kind: RunKind;
  status: RunStatus;
  currentStep: number;
  plan: string[];
  targetVersion: string | null;
  error: string | null;
  createdAt: string;
  finishedAt: string | null;
  steps: Step[];
}

export interface SchoolAlert {
  id: string;
  kind: string;
  level: string;
  detail: string;
  occurredAt: string;
  acknowledgedAt: string | null;
}

export interface SchoolDetail {
  school: SchoolTile;
  modules: string[];
  adminEmail: string | null;
  secretNames: string[];
  runs: Run[];
  telemetryHistory: Telemetry[];
  alerts: SchoolAlert[];
  createdAt: string;
  domain?: SchoolDomain;
}

// Okulun kendi alan adı (platform/api Features/SchoolDomains.cs). target: CNAME kaydının hedefi.
export interface SchoolDomain {
  customDomain: string | null;
  redirect: boolean;
  active: boolean;
  target: string;
  hostIp: string | null;
  lastCheck: string | null;
  checkedAt: string | null;
}

export interface DomainCheck {
  ready: boolean;
  found: string[];
  expected: string | null;
  message: string;
}

export interface SlugCheck {
  available: boolean;
  reason: string | null;
  domain: string;
}

// Sihirbaz isteği - backend Schools.CreateRequest. TimeOnly "HH:mm:ss", DateOnly "yyyy-MM-dd".
export interface CreateSchoolInput {
  slug: string;
  displayName: string;
  shortName: string;
  primaryColor: string | null;
  logoBase64: string | null;
  legalName: string | null;
  contactEmail: string | null;
  customDomain: string | null;
  version: string;
  // Yalnızca OPSİYONEL modüller: çekirdek modül gönderilirse backend reddeder.
  modules: string[];
  admin: { email: string; displayName: string | null };
  policy: {
    groupLessonSharePercent: number;
    maxWeeklyLessons: number;
    makeupNoticeHours: number;
    makeupCreditValidDays: number;
    excusedMakeupWindowDays: number;
    schoolDayStart: string;
    schoolDayEnd: string;
  } | null;
  billing: { multiCourseDiscountPercent: number; siblingDiscountPercent: number; dueDayOfMonth: number } | null;
  tariffs: { courseKind: "Individual" | "Group"; lessonsPerMonth: number; monthlyAmount: number; currency: string; effectiveFrom: string }[] | null;
  integrations: Record<string, string> | null;
  isCanary: boolean;
  // Okulun alt birimleri (branş): kategori sürümün kataloğundan, ad serbest.
  branches: { category: string; name: string }[];
  customCategories: { key: string; name: string; unitLabel: string; unitLabelPlural: string | null }[];
}

// Var olan okulu taşıma - backend Schools.AdoptRequest. Kurulum ucu çağrılmaz; okulun verisi
// sunucuya elle konan dökümden (/srv/abdera/<slug>/dumps/) geri yüklenir.
export interface AdoptSchoolInput {
  slug: string;
  displayName: string;
  shortName: string;
  primaryColor: string | null;
  customDomain: string | null;
  version: string;
  modules: string[];
  dumpName: string;
  integrations: Record<string, string> | null;
  isCanary: boolean;
}

export const DUMP_NAME_PATTERN = /^[A-Za-z0-9._-]{1,120}\.sql\.gz(\.enc)?$/;

export type SchoolAction = "retry" | "cancel" | "suspend" | "resume" | "destroy" | "undestroy" | "purge" | "drill";

export const STATUS_LABEL: Record<SchoolStatus, string> = {
  Provisioning: "Kuruluyor",
  Active: "Çalışıyor",
  Failed: "Hata",
  Suspended: "Askıda",
  PendingDestroy: "Silinmeyi bekliyor",
  Destroyed: "Silindi",
};

export const RUN_KIND_LABEL: Record<RunKind, string> = {
  Create: "Kurulum",
  Upgrade: "Güncelleme",
  Suspend: "Askıya alma",
  Resume: "Devam ettirme",
  Destroy: "Silme bekleyişi",
  Undestroy: "Silmeyi geri alma",
  Purge: "Kalıcı silme",
  Cancel: "İptal",
  Reconfigure: "Yapılandırma",
  Drill: "Prova geri yükleme",
  Adopt: "Sahiplenme",
  DomainAttach: "Alan adını bağlama",
  DomainDetach: "Alan adını kaldırma",
};

export const RUN_STATUS_LABEL: Record<RunStatus, string> = {
  Pending: "Sırada",
  Running: "Çalışıyor",
  Succeeded: "Tamamlandı",
  Failed: "Başarısız",
  Cancelled: "İptal edildi",
};

export function isRunOpen(status: RunStatus) {
  return status === "Pending" || status === "Running";
}

// Yedek 36 saatten eskiyse okul "sorunlu" sayılır (ARCHITECTURE.md §4.1).
export const BACKUP_STALE_HOURS = 36;

export function useSchools(options?: { poll?: boolean }) {
  return useQuery({
    queryKey: ["schools"],
    queryFn: () => api.get<SchoolTile[]>("/api/schools"),
    // Kurulum/işlem sürerken kutucuklar 5 sn'de bir tazelenir.
    refetchInterval: (query) => {
      if (options?.poll === false) return false;
      const data = query.state.data;
      return data?.some((s) => s.status === "Provisioning" || (s.openRun && isRunOpen(s.openRun.status))) ? 5_000 : false;
    },
  });
}

export function useSchool(id: string) {
  return useQuery({
    queryKey: ["schools", id],
    queryFn: () => api.get<SchoolDetail>(`/api/schools/${id}`),
    // Açık işlem varsa adım adım canlı ilerleme için 3 sn'de bir.
    refetchInterval: (query) => {
      const data = query.state.data;
      return data?.runs.some((r) => isRunOpen(r.status)) || data?.school.status === "Provisioning" ? 3_000 : false;
    },
  });
}

export async function checkSlug(slug: string): Promise<SlugCheck> {
  return api.get<SlugCheck>(`/api/schools/check-slug?slug=${encodeURIComponent(slug)}`);
}

export function useCreateSchool() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateSchoolInput) => api.post<{ id: string }>("/api/schools", input),
    onSuccess: () => client.invalidateQueries({ queryKey: ["schools"] }),
  });
}

export function useAdoptSchool() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: AdoptSchoolInput) => api.post<{ id: string }>("/api/schools/adopt", input),
    onSuccess: () => client.invalidateQueries({ queryKey: ["schools"] }),
  });
}

function useSchoolMutation<TVariables, TResult = unknown>(call: (variables: TVariables) => Promise<TResult>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: call,
    onSettled: () => {
      client.invalidateQueries({ queryKey: ["schools"] });
      client.invalidateQueries({ queryKey: ["audit"] });
    },
  });
}

export function useSchoolAction(id: string) {
  return useSchoolMutation((action: SchoolAction) => api.post<{ runId: string }>(`/api/schools/${id}/${action}`));
}

export function useUpgradeSchool(id: string) {
  return useSchoolMutation((version: string) => api.post<{ runId: string }>(`/api/schools/${id}/upgrade`, { version }));
}

export function useSchoolFlags(id: string) {
  return useSchoolMutation((flags: { isCanary: boolean; isVersionPinned: boolean }) => api.put<void>(`/api/schools/${id}/flags`, flags));
}

export function useSchoolModules(id: string) {
  return useSchoolMutation((input: { modules: string[]; integrations: Record<string, string> | null }) =>
    api.put<{ runId: string }>(`/api/schools/${id}/modules`, input));
}

export function useSetSchoolDomain(id: string) {
  return useSchoolMutation((input: { domain: string; redirect: boolean }) => api.put<SchoolDomain>(`/api/schools/${id}/domain`, input));
}

export function useCheckSchoolDomain(id: string) {
  return useSchoolMutation(() => api.post<DomainCheck>(`/api/schools/${id}/domain/check`));
}

export function useRemoveSchoolDomain(id: string) {
  return useSchoolMutation(() => api.delete<void>(`/api/schools/${id}/domain`));
}

export function useRevealAdminPassword(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<{ temporaryPassword: string }>(`/api/schools/${id}/reveal-admin-password`),
    onSettled: () => client.invalidateQueries({ queryKey: ["audit"] }),
  });
}
