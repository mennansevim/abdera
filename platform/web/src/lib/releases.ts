"use client";

// Sürümler ve sürüme bağlı modül kataloğu (platform/api Features/Releases.cs, §16.8).
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export interface ModuleEntry {
  key: string;
  name: string;
  isCore: boolean;
  requires: string[];
}

// Branş kategorileri kataloğu (okul uygulamasının BranchCatalog'u; sürüm kaydıyla gelir, §5.6).
export interface CategoryEntry {
  key: string;
  name: string;
  unitLabel: string;
  unitLabelPlural: string;
  suggestions: string[];
}

export interface Release {
  version: string;
  apiImage: string;
  webImage: string;
  revision: string | null;
  notes: string | null;
  isWithdrawn: boolean;
  releasedAt: string;
  schoolCount: number;
}

export interface ReleaseInput {
  version: string;
  apiImage: string;
  webImage: string;
  revision: string | null;
  modules: ModuleEntry[];
  notes: string | null;
  categories?: CategoryEntry[];
}

export function useReleases() {
  return useQuery({ queryKey: ["releases"], queryFn: () => api.get<Release[]>("/api/releases") });
}

export function useReleaseModules(version: string | null | undefined) {
  return useQuery({
    queryKey: ["releases", version, "modules"],
    queryFn: () => api.get<ModuleEntry[]>(`/api/releases/${encodeURIComponent(version!)}/modules`),
    enabled: Boolean(version),
    staleTime: 5 * 60_000,
  });
}

// Kataloğu olmayan eski sürümde boş liste döner; sihirbaz o durumda yalnızca serbest ad alır.
export function useReleaseCategories(version: string | null | undefined) {
  return useQuery({
    queryKey: ["releases", version, "categories"],
    queryFn: () => api.get<CategoryEntry[]>(`/api/releases/${encodeURIComponent(version!)}/categories`),
    enabled: Boolean(version),
    staleTime: 5 * 60_000,
  });
}

export function useRegisterRelease() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: ReleaseInput) => api.post<void>("/api/releases", input),
    onSuccess: () => client.invalidateQueries({ queryKey: ["releases"] }),
  });
}

export function useWithdrawRelease() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (version: string) => api.post<void>(`/api/releases/${encodeURIComponent(version)}/withdraw`),
    onSuccess: () => client.invalidateQueries({ queryKey: ["releases"] }),
  });
}

// Sürüm karşılaştırma: "2026.10.15.3" biçimi (yyyy.MM.dd.n) parça parça sayısal karşılaştırılır,
// ortak parçalar eşitse uzun olan büyüktür - backend Domain/Release.cs Compare ile birebir.
export function compareVersions(a: string, b: string): number {
  const left = a.split(".").map(Number);
  const right = b.split(".").map(Number);
  for (let i = 0; i < Math.min(left.length, right.length); i += 1) {
    if (left[i] !== right[i]) return left[i] < right[i] ? -1 : 1;
  }
  return Math.sign(left.length - right.length);
}

export const VERSION_PATTERN = /^\d{4}\.\d{2}\.\d{2}\.\d+$/;
export const IMAGE_PATTERN = /^[a-z0-9.-]+(:[0-9]+)?\/[a-z0-9._/-]+@sha256:[a-f0-9]{64}$/;
