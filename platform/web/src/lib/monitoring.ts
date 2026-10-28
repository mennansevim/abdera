"use client";

// Gözlem paneli (platform/api Features/Monitoring.cs): bütün okullar ve sunucular tek ekranda. Sorun
// kuralları sunucudadır (Domain/FleetHealth.cs); panel yalnızca gösterir.
import { useQuery } from "@tanstack/react-query";
import { api } from "./api";
import type { Alert } from "./alerts";
import type { HostStatus } from "./hosts";
import type { LicenseState, SchoolStatus } from "./schools";

export type Severity = "ok" | "warning" | "critical";

export interface FleetIssue {
  severity: "Warning" | "Critical";
  kind: string;
  subject: string;
  schoolId: string | null;
  hostId: string | null;
  detail: string;
}

export interface HostRow {
  id: string;
  name: string;
  status: HostStatus;
  takenAt: string | null;
  reachable: boolean;
  load1: number | null;
  load5: number | null;
  cpus: number | null;
  memTotalMb: number | null;
  memAvailableMb: number | null;
  diskTotalMb: number | null;
  diskFreeMb: number | null;
  uptimeSeconds: number | null;
  schools: number;
  containers: number;
  restarts: number;
  error: string | null;
}

export interface SchoolRow {
  id: string;
  slug: string;
  displayName: string;
  status: SchoolStatus;
  hostName: string | null;
  appVersion: string | null;
  takenAt: string | null;
  reachable: boolean | null;
  health: string | null;
  errors24h: number | null;
  warnings24h: number | null;
  lastErrorAt: string | null;
  dbConnections: number | null;
  dbWaitingOnLocks: number | null;
  databaseSizeBytes: number | null;
  lastMigration: string | null;
  lastBackupAt: string | null;
  failedNotifications: number | null;
  memMb: number | null;
  restarts: number;
  licenseState: LicenseState;
  severity: Severity;
}

export interface Overview {
  generatedAt: string;
  counts: { schools: number; active: number; withProblems: number; provisioning: number; suspended: number; hosts: number; hostsWithProblems: number };
  totals: { students: number; staffUsers: number; enrollments: number; databaseBytes: number; errors24h: number; warnings24h: number; yearlyTotal: number };
  issues: FleetIssue[];
  hosts: HostRow[];
  schools: SchoolRow[];
  recentAlerts: Alert[];
}

export interface ContainerStat {
  name: string;
  school: string | null;
  status: string;
  health: string | null;
  restarts: number;
  memMb: number | null;
  memLimitMb: number | null;
  cpuPercent: number | null;
}

export interface HostStats {
  host: HostRow;
  points: { takenAt: string; reachable: boolean; load1: number | null; memUsedMb: number | null; diskUsedMb: number | null }[];
  containers: ContainerStat[];
}

export interface BackupItem {
  name: string;
  location: string;
  size: number;
  modifiedAt: string;
}

export type LogService = "api" | "web" | "db" | "backup-gw";
export type LogWindow = "15m" | "1h" | "6h" | "24h";

// Genel bakış dakikada bir tazelenir; ölçümler zaten 5 dakikada bir toplanıyor.
export function useOverview() {
  return useQuery({ queryKey: ["overview"], queryFn: () => api.get<Overview>("/api/overview"), refetchInterval: 60_000 });
}

export function useHostStats(id: string, hours: number) {
  return useQuery({ queryKey: ["hosts", id, "stats", hours], queryFn: () => api.get<HostStats>(`/api/hosts/${id}/stats?hours=${hours}`), refetchInterval: 60_000 });
}

export function useSchoolBackups(id: string) {
  return useQuery({
    queryKey: ["schools", id, "backups"],
    queryFn: () => api.get<{ items: BackupItem[]; lastSucceededAt: string | null }>(`/api/schools/${id}/backups`),
  });
}

// Log bilerek otomatik çekilmez: her okuma denetim kaydına yazılır, operatör "Getir"e basınca gider.
export function fetchSchoolLogs(id: string, service: LogService, since: LogWindow, tail: number) {
  return api.get<string>(`/api/schools/${id}/logs?service=${service}&since=${since}&tail=${tail}`);
}

export function percent(part: number | null | undefined, total: number | null | undefined): number | null {
  return part === null || part === undefined || !total ? null : Math.round((1000 * part) / total) / 10;
}
