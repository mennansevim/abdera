// Scheduling modülü tipleri ve React Query hook'ları - docs/07-api.md.
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export type LessonSeriesStatus = "Active" | "Ended";
export type LessonStatus = "Normal" | "Rescheduled" | "Cancelled" | "Completed" | "Makeup";

// .NET DayOfWeek: Pazar=0 ... Cumartesi=6 (backend'in JSON'da gönderdiği string ile eşleşir)
export const DAY_NAMES_TR: Record<string, string> = {
  Sunday: "Pazar",
  Monday: "Pazartesi",
  Tuesday: "Salı",
  Wednesday: "Çarşamba",
  Thursday: "Perşembe",
  Friday: "Cuma",
  Saturday: "Cumartesi",
};

export interface LessonSeries {
  id: string;
  enrollmentId: string;
  dayOfWeek: string;
  startTime: string;
  durationMinutes: number;
  effectiveFrom: string;
  effectiveUntil: string | null;
  status: LessonSeriesStatus;
}

export interface GenerationSummary {
  created: number;
  skippedHolidays: string[];
  skippedTeacherTimeOff: string[];
}

export interface CreateLessonSeriesResponse {
  series: LessonSeries;
  generation: GenerationSummary;
}

export function useCreateLessonSeries() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      enrollmentId: string;
      dayOfWeek: string;
      startTime: string;
      durationMinutes: number;
      effectiveFrom: string;
      effectiveUntil?: string | null;
    }) => api.post<CreateLessonSeriesResponse>("/api/lesson-series", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["calendar"] }),
  });
}

export interface CalendarLesson {
  id: string;
  startAt: string;
  endAt: string;
  status: LessonStatus;
  studentId: string;
  studentName: string;
  teacherId: string;
  teacherName: string;
  instrumentId: string;
  instrumentName: string;
}

export function useCalendar(from: string, to: string) {
  return useQuery({
    queryKey: ["calendar", from, to],
    queryFn: () => api.get<CalendarLesson[]>(`/api/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
  });
}
