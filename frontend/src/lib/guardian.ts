// Veli kendi kendine hizmet uçları - docs/10-decisions.md Karar F reversal. Kapsam bilinçli
// olarak dar: yalnızca kendi öğrencisi/takvimi/RSVP'si ve salt-okunur aidat/mesaj görünümü.
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { LessonStatus } from "./scheduling";

export interface GuardianStudent {
  studentId: string;
  firstName: string;
  lastName: string;
  instrumentName: string | null;
  teacherName: string | null;
}

export function useGuardianStudents() {
  return useQuery({
    queryKey: ["guardian", "students"],
    queryFn: () => api.get<GuardianStudent[]>("/api/guardian/me/students"),
  });
}

export type GuardianRsvpResponse = "Unknown" | "Attending" | "AttendingLate" | "NotAttending";

export interface GuardianLesson {
  id: string;
  startAt: string;
  endAt: string;
  status: LessonStatus;
  instrumentName: string;
  teacherName: string;
  rsvpResponse: GuardianRsvpResponse;
}

export function useGuardianCalendar(studentId: string | undefined, from: string, to: string) {
  return useQuery({
    queryKey: ["guardian", "calendar", studentId, from, to],
    queryFn: () => api.get<GuardianLesson[]>(
      `/api/guardian/me/students/${studentId}/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
    ),
    enabled: !!studentId,
  });
}

export function useRespondRsvp() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ lessonId, response }: { lessonId: string; response: "Attending" | "AttendingLate" | "NotAttending" }) =>
      api.post<{ lessonId: string; response: GuardianRsvpResponse; respondedAt: string }>(
        `/api/guardian/me/lessons/${lessonId}/rsvp`, { response },
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["guardian", "calendar"] }),
  });
}

export type GuardianReceivableStatus = "Unpaid" | "Partial" | "Paid" | "Overdue" | "Cancelled";

export interface GuardianReceivable {
  id: string;
  period: string;
  amount: number;
  currency: string;
  dueDate: string;
  status: GuardianReceivableStatus;
  totalPaid: number;
}

export interface GuardianBillingEnrollment {
  enrollmentId: string;
  studentId: string;
  studentName: string;
  instrumentName: string;
  teacherName: string;
  receivables: GuardianReceivable[];
}

export interface GuardianMakeupCredit {
  id: string;
  studentId: string;
  earnedReason: "GuardianCancelled24H" | "SchoolCancelled";
  earnedAt: string;
  expiresAt: string;
}

export interface GuardianBilling {
  enrollments: GuardianBillingEnrollment[];
  makeupCredits: GuardianMakeupCredit[];
  virtualIban: { iban: string; provider: string } | null;
}

export interface GuardianMessage {
  id: string;
  body: string;
  direction: "Outbound" | "Inbound";
  createdAt: string;
  sentAt: string | null;
}

export function useGuardianBilling() {
  return useQuery({
    queryKey: ["guardian", "billing"],
    queryFn: () => api.get<GuardianBilling>("/api/guardian/me/billing"),
  });
}

export function useGuardianMessages() {
  return useQuery({
    queryKey: ["guardian", "messages"],
    queryFn: () => api.get<GuardianMessage[]>("/api/guardian/me/messages"),
  });
}

export interface GuardianProgressEntry {
  id: string;
  lessonStartAt: string;
  teacherName: string;
  instrumentName: string;
  practiced: string | null;
  parentComment: string | null;
  homework: string | null;
  nextGoal: string | null;
  pieceTitle: string | null;
  pieceDifficulty: number | null;
  pieceComposer: string | null;
  pieceStatus: "Learning" | "Polishing" | "PerformanceReady" | "Archived" | null;
  pieceTargetDate: string | null;
  pieceResourceUrl: string | null;
  createdAt: string;
}

export interface GuardianProgress {
  studentId: string;
  presentCount: number;
  absentCount: number;
  excusedCount: number;
  entries: GuardianProgressEntry[];
}

export function useGuardianProgress(studentId: string | undefined) {
  return useQuery({
    queryKey: ["guardian", "progress", studentId],
    queryFn: () => api.get<GuardianProgress>(`/api/guardian/me/students/${studentId}/progress`),
    enabled: !!studentId,
  });
}

export interface PracticeJournalEntry {
  id: string;
  studentId: string;
  date: string;
  durationMinutes: number;
  goal: string;
  note: string | null;
  parentApproved: boolean;
  createdAt: string;
}

export interface PracticeJournal {
  entries: PracticeJournalEntry[];
  totalMinutes: number;
  badges: string[];
}

export function useGuardianPracticeJournal(studentId: string | undefined) {
  return useQuery({
    queryKey: ["guardian", "practice-journal", studentId],
    queryFn: () => api.get<PracticeJournal>(`/api/guardian/me/students/${studentId}/practice-journal`),
    enabled: !!studentId,
  });
}

export function useCreateGuardianPracticeEntry(studentId: string | undefined) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { date: string; durationMinutes: number; goal: string; note?: string }) =>
      api.post<PracticeJournalEntry>(`/api/guardian/me/students/${studentId}/practice-journal`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["guardian", "practice-journal", studentId] }),
  });
}
