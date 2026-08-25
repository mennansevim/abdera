"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./api";

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
  aiTransformationAvailable: boolean;
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

// Faz 10: ham notu veliye uygun yapıcı bir metne çevirir. Yalnızca ÖNERİ döner - hiçbir şey
// kaydedilmez. Öğretmen öneriyi düzenleyip useSetParentComment ile kaydeder ya da yok sayar.
export function useSuggestParentComment() {
  return useMutation<{ suggestion: string }, ApiError, string>({
    mutationFn: (noteId) => api.post<{ suggestion: string }>(`/api/lesson-notes/${noteId}/parent-comment/suggest`),
  });
}

export function useRevokeParentComment(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (noteId: string) => api.post<ProgressEntry>(`/api/lesson-notes/${noteId}/parent-comment/revoke`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["student-progress", studentId] }),
  });
}
