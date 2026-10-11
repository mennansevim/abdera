// Dönem karnesi (issue #15, docs/10-decisions.md Z) - öğretmen/yönetici uçları ve veli görünümü.
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { PieceStatus } from "./attendance";

export type TermReportStatus = "Draft" | "Published";

export interface TermInfo {
  key: string;
  label: string;
  start: string;
  end: string;
}

export interface TermReportFacts {
  studentName: string;
  instrumentName: string;
  teacherName: string;
  lessonsPresent: number;
  lessonsAbsent: number;
  lessonsExcused: number;
  practiceMinutes: number;
  practiceDays: number;
  pieces: { title: string; composer: string | null; status: PieceStatus | null }[];
  skills: { label: string; firstScore: number; lastScore: number }[];
}

export interface TermReport {
  id: string | null;
  enrollmentId: string;
  studentId: string;
  term: TermInfo;
  status: TermReportStatus | null;
  comment: string | null;
  publishedAt: string | null;
  updatedAt: string | null;
  facts: TermReportFacts;
}

export interface TermReportRow {
  enrollmentId: string;
  studentId: string;
  studentName: string;
  instrumentName: string;
  teacherId: string;
  teacherName: string;
  status: TermReportStatus | null;
  updatedAt: string | null;
}

export interface TermReportList {
  term: TermInfo;
  previousTerm: TermInfo;
  publishedCount: number;
  draftCount: number;
  rows: TermReportRow[];
}

export interface GuardianTermReport {
  id: string;
  term: TermInfo;
  comment: string;
  publishedAt: string;
  facts: TermReportFacts;
}

export const PIECE_STATUS_LABELS: Record<PieceStatus, string> = {
  Learning: "Öğreniyor",
  Polishing: "Pekiştiriyor",
  PerformanceReady: "Sahneye hazır",
  Archived: "Tamamlandı",
};

export const TERM_REPORT_COMMENT_MAX_LENGTH = 2000;

// `term` verilmezse sunucu bugünün dönemini döner.
export function useTermReports(term: string | null) {
  return useQuery({
    queryKey: ["term-reports", term],
    queryFn: () => api.get<TermReportList>(term ? `/api/term-reports?term=${encodeURIComponent(term)}` : "/api/term-reports"),
  });
}

export function useTermReport(enrollmentId: string, term: string) {
  return useQuery({
    queryKey: ["term-report", enrollmentId, term],
    queryFn: () => api.get<TermReport>(`/api/enrollments/${enrollmentId}/term-reports/${term}`),
    enabled: Boolean(enrollmentId && term),
  });
}

function useTermReportMutation<TVariables>(enrollmentId: string, term: string, request: (variables: TVariables) => Promise<TermReport>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: request,
    onSuccess: (report) => {
      queryClient.setQueryData(["term-report", enrollmentId, term], report);
      queryClient.invalidateQueries({ queryKey: ["term-reports"] });
    },
  });
}

export function useSaveTermReport(enrollmentId: string, term: string) {
  return useTermReportMutation<{ comment: string }>(enrollmentId, term, ({ comment }) =>
    api.put<TermReport>(`/api/enrollments/${enrollmentId}/term-reports/${term}`, { comment }));
}

export function usePublishTermReport(enrollmentId: string, term: string) {
  return useTermReportMutation<void>(enrollmentId, term, () =>
    api.post<TermReport>(`/api/enrollments/${enrollmentId}/term-reports/${term}/publish`));
}

export function useUnpublishTermReport(enrollmentId: string, term: string) {
  return useTermReportMutation<void>(enrollmentId, term, () =>
    api.post<TermReport>(`/api/enrollments/${enrollmentId}/term-reports/${term}/unpublish`));
}

export function useGuardianTermReports(studentId: string | undefined) {
  return useQuery({
    queryKey: ["guardian", "term-reports", studentId],
    queryFn: () => api.get<GuardianTermReport[]>(`/api/guardian/me/students/${studentId}/term-reports`),
    enabled: !!studentId,
  });
}
