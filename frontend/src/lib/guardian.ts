// Veli kendi kendine hizmet uçları - docs/10-decisions.md Karar F reversal. Kapsam bilinçli
// olarak dar: yalnızca kendi öğrencisi/takvimi/RSVP'si. Aidat ve bildirim listesi burada YOK,
// ayrı bir iş (bkz. GuardianPortal.cs üstündeki yorum).
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
