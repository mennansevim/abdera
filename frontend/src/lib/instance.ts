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
}

export interface InstanceConfig {
  profile: SchoolProfile;
  modules: InstanceModule[];
}

// Yapılandırma okunamazsa (yükleniyor, ağ hatası, eski API) bugünkü kimlik ve "her şey açık"
// varsayılır: tek okullu kurulumda hiçbir ekran kaybolmamalı, marka yine "Abdera" görünmeli.
export const DEFAULT_PROFILE: SchoolProfile = {
  name: "Abdera Sanat Akademi",
  shortName: "Abdera",
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

export function schoolLogoSrc(profile: SchoolProfile): string {
  return profile.logoUrl ? `${apiBaseUrl()}${profile.logoUrl}` : "/abdera-logo.webp";
}

// Backend SchoolProfile.MaxLogoBytes ile aynı; sunucu yine kendisi denetler (tür dosya içeriğinden).
export const SCHOOL_LOGO_MAX_BYTES = 512 * 1024;

export interface UpdateSchoolProfileRequest {
  name: string;
  shortName: string;
  primaryColor: string | null;
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
