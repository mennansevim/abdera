"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export interface ProgressEntry {
  id: string;
  lessonId: string;
  teacherId: string;
  instrumentId: string;
  lessonStartAt: string;
  createdAt: string;
  teacherName: string;
  instrumentName: string;
  practiced: string | null;
  note: string | null;
  homework: string | null;
  nextGoal: string | null;
  pieceTitle: string | null;
  pieceDifficulty: number | null;
  pieceComposer: string | null;
  pieceStatus: "Learning" | "Polishing" | "PerformanceReady" | "Archived" | null;
  pieceTargetDate: string | null;
  pieceResourceUrl: string | null;
  pieceResourceVisibleToGuardian: boolean;
  parentComment: string | null;
  parentCommentApprovedAt: string | null;
}

export interface StudentProgress {
  studentId: string;
  studentName: string;
  entryCount: number;
  lastEntryAt: string | null;
  entries: ProgressEntry[];
  skillAssessments: SkillAssessmentEntry[];
}

export interface SkillAssessmentEntry {
  id: string;
  skillDefinitionId: string;
  skillCode: string;
  skillLabel: string;
  teacherId: string;
  teacherName: string;
  lessonId: string | null;
  score: number;
  note: string | null;
  assessedAt: string;
}

export function useStudentProgress(studentId: string) {
  return useQuery({
    queryKey: ["student-progress", studentId],
    queryFn: () => api.get<StudentProgress>(`/api/students/${studentId}/progress`),
    enabled: !!studentId,
  });
}

// Gelişim ekranındaki "Genel gelişim" yorumu: öğretmen notlarından AI ile üretilir. Sunucu
// yorumu önbellekte tutar ve yalnızca yeni not girildiğinde yeniden üretir; bu yüzden istek
// ilk açılışta birkaç saniye sürebilir, sonrakiler anında döner.
export interface ProgressSummary {
  status: "Ready" | "NoNotes" | "Unavailable" | "Failed";
  summary: string | null;
  generatedAt: string | null;
  sourceNoteCount: number;
  // Sağlayıcı geçici hata verdiyse son üretilen yorum döner; yeni notları henüz kapsamıyor.
  isStale: boolean;
}

export function useProgressSummary(studentId: string) {
  return useQuery({
    queryKey: ["student-progress-summary", studentId],
    queryFn: () => api.get<ProgressSummary>(`/api/students/${studentId}/progress-summary`),
    enabled: !!studentId,
    staleTime: 5 * 60_000,
  });
}

export function useCreateProgressNote(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      lessonId: string;
      practiced?: string;
      note?: string;
      homework?: string;
      nextGoal?: string;
      pieceTitle?: string;
      pieceDifficulty?: number;
      pieceComposer?: string;
      pieceStatus?: "Learning" | "Polishing" | "PerformanceReady" | "Archived";
      pieceTargetDate?: string;
      pieceResourceUrl?: string;
      pieceResourceVisibleToGuardian?: boolean;
    }) => {
      const { lessonId, ...note } = body;
      return api.post(`/api/lessons/${lessonId}/notes`, note);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-progress", studentId] });
      queryClient.invalidateQueries({ queryKey: ["student-progress-summary", studentId] });
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
    },
  });
}

export function useSetParentComment(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ noteId, parentComment, approve }: { noteId: string; parentComment: string; approve: boolean }) =>
      api.put<ProgressEntry>(`/api/lesson-notes/${noteId}/parent-comment`, { parentComment, approve }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["student-progress", studentId] }),
  });
}

export function useRevokeParentComment(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (noteId: string) => api.post<ProgressEntry>(`/api/lesson-notes/${noteId}/parent-comment/revoke`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["student-progress", studentId] }),
  });
}
