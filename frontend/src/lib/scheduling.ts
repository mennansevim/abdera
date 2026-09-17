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
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
      queryClient.invalidateQueries({ queryKey: ["student-lesson-series"] });
    },
  });
}

// Öğrenci künyesindeki "Ders programı" satırı - hangi kurs/öğretmen olduğu da aynı yanıtta
// geldiği için istemci ayrıca öğretmen/enstrüman listesi eşlemek zorunda kalmaz.
export interface StudentLessonSeries {
  id: string;
  enrollmentId: string;
  teacherId: string;
  teacherName: string;
  instrumentId: string;
  instrumentName: string;
  dayOfWeek: string;
  // Backend TimeOnly'yi "18:00:00" olarak serialize eder.
  startTime: string;
  durationMinutes: number;
  effectiveFrom: string;
  effectiveUntil: string | null;
  status: LessonSeriesStatus;
}

export function useStudentLessonSeries(studentId: string) {
  return useQuery({
    queryKey: ["student-lesson-series", studentId],
    queryFn: () => api.get<StudentLessonSeries[]>(`/api/students/${studentId}/lesson-series`),
    enabled: !!studentId,
  });
}

// Taşıma sunucuda "eskisini kapat + yenisini aç" olarak işlenir, bu yüzden dönen seri
// yeni bir id taşır; istemci listeyi tazeleyip yeni satırı gösterir.
export function useRescheduleLessonSeries(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ seriesId, ...body }: {
      seriesId: string;
      dayOfWeek: string;
      startTime: string;
      durationMinutes: number;
      effectiveFrom?: string | null;
    }) => api.post<CreateLessonSeriesResponse>(`/api/lesson-series/${seriesId}/reschedule`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-lesson-series", studentId] });
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
    },
  });
}

// "Her hafta Pazartesi 18:00 · 45 dk" - künyedeki tek satırlık özet.
export function formatWeeklySchedule(series: { dayOfWeek: string; startTime: string; durationMinutes: number }) {
  return `Her hafta ${DAY_NAMES_TR[series.dayOfWeek] ?? series.dayOfWeek} ${series.startTime.slice(0, 5)} · ${series.durationMinutes} dk`;
}

export interface CalendarLesson {
  id: string;
  lessonSeriesId: string | null;
  startAt: string;
  endAt: string;
  status: LessonStatus;
  studentId: string;
  studentName: string;
  teacherId: string;
  teacherName: string;
  instrumentId: string;
  instrumentName: string;
  rsvpResponse?: "Unknown" | "Attending" | "AttendingLate" | "NotAttending" | null;
}

export function useCalendar(from: string, to: string) {
  return useQuery({
    queryKey: ["calendar", from, to],
    queryFn: () => api.get<CalendarLesson[]>(`/api/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
    // Veli WhatsApp hızlı yanıtını uygulama kapalıyken verebilir. Webhook cevabı
    // veritabanına yazdıktan sonra açık öğretmen/yönetici takvimi sayfa yenilemeden
    // kısa sürede Geliyor / Geç kalacak / Gelmiyor durumuna geçsin.
    refetchInterval: 15_000,
    refetchOnWindowFocus: true,
  });
}

export function useUpdateLesson() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ lessonId, ...body }: {
      lessonId: string;
      studentId: string;
      teacherId: string;
      startAt: string;
      durationMinutes: number;
      status: "Normal" | "Cancelled";
    }) => api.patch<{ lessonId: string; replacedLessonId: string | null; status: LessonStatus }>(`/api/lessons/${lessonId}`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
      queryClient.invalidateQueries({ queryKey: ["change-requests"] });
      queryClient.invalidateQueries({ queryKey: ["makeup-credits"] });
    },
  });
}

export interface TeacherAvailability {
  id: string;
  dayOfWeek: string;
  startTime: string;
  endTime: string;
}

export function useTeacherAvailability(teacherId: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["teacher-availability", teacherId],
    queryFn: () => api.get<TeacherAvailability[]>(`/api/teachers/${teacherId}/availability`),
    enabled: !!teacherId && (options?.enabled ?? true),
  });
}

// Öğretmenler ekranındaki "uygun günler" tek-tık aç/kapa arayüzü bu ikisini kullanır: bir
// günü açmak POST, kapatmak DELETE'tir - ayrı bir "toggle" ucu yok, backend zaten
// create/delete olarak modelliyor (TeacherAvailabilities.cs).
export function useCreateTeacherAvailability(teacherId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { dayOfWeek: string; startTime: string; endTime: string }) =>
      api.post<TeacherAvailability>(`/api/teachers/${teacherId}/availability`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["teacher-availability", teacherId] }),
  });
}

export function useDeleteTeacherAvailability(teacherId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (availabilityId: string) => api.delete(`/api/teachers/${teacherId}/availability/${availabilityId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["teacher-availability", teacherId] }),
  });
}
