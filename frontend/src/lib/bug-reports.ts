// "Hata bildir" (docs/10-decisions.md W, backend Modules/Ops/Features/BugReports.cs).
// Gönderim öğretmen + yönetici; liste ve durum güncellemesi yalnızca yönetici.
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { PagedResponse } from "./messaging";

export type BugReportKind = "Bug" | "Suggestion";
export type BugReportStatus = "New" | "Triaged" | "Rejected";

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
}

export interface BugReportList {
  page: PagedResponse<BugReport>;
  counts: Record<"new" | "triaged" | "rejected", number>;
}

export const BUG_REPORT_ISSUE_URL = "https://github.com/mennansevim/abdera/issues";

export function useCreateBugReport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { kind: BugReportKind; pagePath: string; description: string; appVersion: string }) =>
      api.post<{ id: string }>("/api/bug-reports", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["bug-reports"] }),
  });
}

export function useBugReports(status: BugReportStatus, page: number, enabled: boolean) {
  return useQuery({
    queryKey: ["bug-reports", status, page],
    queryFn: () => api.get<BugReportList>(`/api/bug-reports?status=${status}&page=${page}&pageSize=20`),
    enabled,
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
