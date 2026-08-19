// Attendance + ders değişikliği + telafi API'leri - docs/07-api.md.
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export type AttendanceStatus = "Present" | "Absent" | "Excused";

export interface Attendance {
  id: string;
  lessonId: string;
  status: AttendanceStatus;
  markedByTeacherId: string;
  markedAt: string;
  note: string | null;
}

export function useMarkAttendance(lessonId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { status: AttendanceStatus; note?: string }) =>
      api.post<Attendance>(`/api/lessons/${lessonId}/attendance`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["calendar"] }),
  });
}

export function useCreateLessonNote(lessonId: string) {
  return useMutation({
    mutationFn: (body: { practiced?: string; note?: string; homework?: string; nextGoal?: string }) =>
      api.post(`/api/lessons/${lessonId}/notes`, body),
  });
}

export type ChangeRequestStatus =
  | "Pending" | "Approved" | "Rejected"
  | "AlternativeProposed" | "ParentConfirmationPending" | "ParentAccepted" | "ParentRejected";

export interface ChangeRequest {
  id: string;
  lessonId: string;
  requestedBy: string;
  reason: string | null;
  proposedStartAt: string;
  proposedEndAt: string;
  status: ChangeRequestStatus;
  createdAt: string;
  resolvedAt: string | null;
}

export function useCreateChangeRequest(lessonId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { reason?: string; proposedStartAt: string; proposedEndAt: string }) =>
      api.post<ChangeRequest>(`/api/lessons/${lessonId}/change-requests`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["change-requests"] }),
  });
}

export function usePendingChangeRequests() {
  return useQuery({
    queryKey: ["change-requests", "Pending"],
    queryFn: () => api.get<ChangeRequest[]>("/api/change-requests?status=Pending"),
  });
}

export function useApproveChangeRequest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (requestId: string) => api.post(`/api/change-requests/${requestId}/approve`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["change-requests"] });
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
    },
  });
}

export function useRejectChangeRequest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (requestId: string) => api.post(`/api/change-requests/${requestId}/reject`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["change-requests"] }),
  });
}

export function useCancelLesson() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ lessonId, cancelledBy, reason }: { lessonId: string; cancelledBy: "Guardian" | "School"; reason?: string }) =>
      api.post<{ lessonId: string; makeupCreditEarned: boolean }>(`/api/lessons/${lessonId}/cancel`, { cancelledBy, reason }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["calendar"] }),
  });
}
