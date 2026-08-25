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
    whatsApp: "Configured" | "DevelopmentOnly" | "Misconfigured";
    banking: "Configured" | "DevelopmentOnly" | "Misconfigured";
    backup: "Configured" | "DevelopmentOnly" | "Misconfigured";
  };
}

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
  });
}

export function useTriggerBackup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<void>("/api/backup-runs/trigger"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["backup-runs"] }),
  });
}
