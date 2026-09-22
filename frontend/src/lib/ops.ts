// Faz 4: sistem sağlığı (DB + yedekleme tazeliği) ve yedekleme geçmişi - yalnızca Admin
// (docs/04-permissions.md, backend Modules/Ops).
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { PagedResponse } from "./messaging";

export type SystemHealthLevel = "Healthy" | "Degraded" | "Unhealthy";

export interface SystemHealthSummary {
  level: SystemHealthLevel;
  detail: string | null;
  lastCheckedAt: string;
  databaseReachable: boolean;
  lastSuccessfulBackupAt: string | null;
  lastBackupStatus: string | null;
  providers: {
    whatsApp: ProviderConfigurationState;
    banking: ProviderConfigurationState;
    backup: ProviderConfigurationState;
  };
}

export type ProviderConfigurationState = "Configured" | "DevelopmentOnly" | "ManualOnly" | "Disabled" | "Misconfigured";

export function useSystemHealth() {
  return useQuery({
    queryKey: ["system-health"],
    queryFn: () => api.get<SystemHealthSummary>("/api/system/health"),
    // Ana ekranda otomatik tazelensin diye - bkz. dashboard/page.tsx sağlık kartı.
    refetchInterval: 5 * 60 * 1000,
  });
}

export type BackupRunStatus = "Running" | "Succeeded" | "Failed";

export interface BackupRun {
  id: string;
  status: BackupRunStatus;
  triggeredManually: boolean;
  startedAt: string;
  completedAt: string | null;
  sizeBytes: number | null;
  remotePath: string | null;
  errorMessage: string | null;
}

export function useBackupRuns(page: number = 1, pageSize: number = 20) {
  return useQuery({
    queryKey: ["backup-runs", page, pageSize],
    queryFn: () => api.get<PagedResponse<BackupRun>>(`/api/backup-runs?page=${page}&pageSize=${pageSize}`),
    // Tetiklenen bir yedekleme "Running"de kalırken kısa aralıkla otomatik yenile - aksi halde
    // admin bitip bitmediğini görmek için sayfayı elle yenilemek zorunda kalıyordu.
    refetchInterval: (query) => query.state.data?.items.some((item) => item.status === "Running") ? 3000 : false,
  });
}

export function useTriggerBackup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<void>("/api/backup-runs/trigger"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["backup-runs"] }),
  });
}
