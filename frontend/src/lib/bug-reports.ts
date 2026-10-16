// "Hata bildir" (docs/10-decisions.md W, backend Modules/Ops/Features/BugReports.cs).
// Gönderim öğretmen + yönetici; liste ve durum güncellemesi yalnızca yönetici. Çözülen
// bildirimi, bildirimi yapan "çalışıyor / hâlâ hatalı" diye teyit eder (W6). Anlaşılamayan
// bildirim için bildirimi yapandan detay istenir (W7).
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { PagedResponse } from "./messaging";

export type BugReportKind = "Bug" | "Suggestion";
export type BugReportStatus = "New" | "Triaged" | "Rejected" | "Resolved" | "Verified" | "Reopened" | "NeedsInfo";

export interface BugReport {
  id: string;
  kind: BugReportKind;
  pagePath: string;
  description: string;
  userAgent: string | null;
  appVersion: string | null;
  reporterName: string | null;
  reporterEmail: string | null;
  status: BugReportStatus;
  githubIssueNumber: number | null;
  triageNote: string | null;
  createdAt: string;
  resolvedAt: string | null;
  feedbackNote: string | null;
}

export interface BugReportList {
  page: PagedResponse<BugReport>;
  counts: Record<"new" | "triaged" | "rejected" | "resolved" | "verified" | "reopened" | "needsInfo", number>;
}


export function useCreateBugReport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { kind: BugReportKind; pagePath: string; description: string; appVersion: string }) =>
      api.post<{ id: string }>("/api/bug-reports", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["bug-reports"] }),
  });
}

export function useBugReports(statuses: BugReportStatus[], page: number, enabled: boolean) {
  return useQuery({
    queryKey: ["bug-reports", statuses.join(","), page],
    queryFn: () => api.get<BugReportList>(`/api/bug-reports?${statuses.map((status) => `status=${status}`).join("&")}&page=${page}&pageSize=20`),
    enabled,
  });
}

// Oturumdaki kullanıcıdan bir şey bekleyen bildirimler: çözüldü teyidi ya da istenen detay.
export function useAwaitingMe() {
  return useQuery({
    queryKey: ["bug-reports", "awaiting-me"],
    queryFn: () => api.get<BugReport[]>("/api/bug-reports/awaiting-me"),
  });
}

export function useProvideBugReportDetails() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, details }: { id: string; details: string }) =>
      api.post<void>(`/api/bug-reports/${id}/details`, { details }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["bug-reports"] }),
  });
}

export function useConfirmBugReport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string; works: boolean; note: string | null }) =>
      api.post<void>(`/api/bug-reports/${id}/confirmation`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["bug-reports"] }),
  });
}

export function useTriageBugReport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string; status: BugReportStatus; githubIssueNumber: number | null; triageNote: string | null }) =>
      api.patch<void>(`/api/bug-reports/${id}`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["bug-reports"] }),
  });
}
