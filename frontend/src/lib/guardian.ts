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

export type GuardianRsvpResponse = "Unknown" | "Attending" | "NotAttending";

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
    mutationFn: ({ lessonId, response }: { lessonId: string; response: "Attending" | "NotAttending" }) =>
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
