// Bu okul kurulumunun kimliği ve açık modülleri (docs/multi-tenant/ARCHITECTURE.md §5).
// Backend: GET /api/instance/config (anonim; giriş ekranı da okul adını gösterir).
"use client";

import { useQuery } from "@tanstack/react-query";
import { api, apiBaseUrl } from "./api";

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
  | "profitability";

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

export function schoolLogoSrc(profile: SchoolProfile): string {
  return profile.logoUrl ? `${apiBaseUrl()}${profile.logoUrl}` : "/abdera-logo.webp";
}
