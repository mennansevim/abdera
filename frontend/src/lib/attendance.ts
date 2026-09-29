// Attendance + ders değişikliği + telafi API'leri - docs/07-api.md.
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { PagedResponse } from "./messaging";

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
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
      // Yoklama dersi Tamamlandı'ya çeker: Öğretmenler listesindeki haftalık "Tamamlanan"
      // sütunu (benchmark) ve ödeme durumu (ödeme haftası) bu sayıdan beslenir. Yenilenmezse
      // liste önbellekteki eski 0'ı göstermeye devam ediyordu.
      queryClient.invalidateQueries({ queryKey: ["benchmark"] });
      queryClient.invalidateQueries({ queryKey: ["teacher-payout-week"] });
      // Geçmiş bir derse "geldi" girilirse ders yorum bekleyenler listesine düşer.
      queryClient.invalidateQueries({ queryKey: ["pending-lesson-notes"] });
      // Yoklama girilince zildeki "öğrenci geldi mi?" sorusu sunucuda kapanır.
      queryClient.invalidateQueries({ queryKey: ["staff-notifications"] });
    },
  });
}

export function useCreateLessonNote(lessonId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { practiced?: string; note?: string; homework?: string; nextGoal?: string; pieceTitle?: string; pieceDifficulty?: number }) =>
      api.post(`/api/lessons/${lessonId}/notes`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["pending-lesson-notes"] });
      // Son eksik not yazılınca sunucu zildeki hatırlatmayı kapatır; yoklaması girilmemiş derse
      // yazılan not yoklamayı "geldi" olarak işler ve "öğrenci geldi mi?" sorusunu da kapatır.
      queryClient.invalidateQueries({ queryKey: ["staff-notifications"] });
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
    },
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

export function usePendingChangeRequests(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["change-requests", "Pending"],
    queryFn: () => api.get<ChangeRequest[]>("/api/change-requests?status=Pending"),
    enabled: options?.enabled ?? true,
  });
}

export function useApproveChangeRequest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (requestId: string) => api.post(`/api/change-requests/${requestId}/approve`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["change-requests"] });
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
      queryClient.invalidateQueries({ queryKey: ["benchmark"] });
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

// Sürükle-bırak takvim (dashboard/calendar/page.tsx): admin bir dersi yeni gün/saate
// taşıdığında ayrı bir "hızlı taşıma" endpoint'i yok - var olan LessonChangeRequest
// akışını (create + approve) arka arkaya çağırıyoruz. Böylece çakışma kontrolü, eski
// bildirim job'unun iptali ve yeni hatırlatmanın kurulması ChangeRequests.cs'teki
// ApproveAsync'ten aynen miras alınır; ayrı bir backend değişikliği gerekmez.
export function useRescheduleLesson() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ lessonId, proposedStartAt, proposedEndAt }: { lessonId: string; proposedStartAt: string; proposedEndAt: string }) => {
      const request = await api.post<ChangeRequest>(`/api/lessons/${lessonId}/change-requests`, { proposedStartAt, proposedEndAt });
      try {
        await api.post(`/api/change-requests/${request.id}/approve`);
      } catch (err) {
        // Onay başarısız oldu (örn. çakışma) - yarım kalan talebi reddederek "Bekleyen
        // Değişiklik Talepleri" listesinde asılı bırakmıyoruz.
        await api.post(`/api/change-requests/${request.id}/reject`).catch(() => {});
        throw err;
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
      queryClient.invalidateQueries({ queryKey: ["change-requests"] });
      queryClient.invalidateQueries({ queryKey: ["benchmark"] });
    },
  });
}

export function useCancelLesson() {
  const queryClient = useQueryClient();
  return useMutation({
    // grantMakeupCredit: açık telafi kararı. Belirtilmezse sunucudaki politika (okul
    // kaynaklı iptal -> kredi, veli kaynaklı -> 24 saat kuralı) çalışır; false gönderildiğinde
    // ders telafi hakkı doğurmadan iptal edilir.
    mutationFn: ({ lessonId, cancelledBy, reason, grantMakeupCredit }: { lessonId: string; cancelledBy: "Guardian" | "School"; reason?: string; grantMakeupCredit?: boolean }) =>
      api.post<{ lessonId: string; makeupCreditEarned: boolean }>(`/api/lessons/${lessonId}/cancel`, { cancelledBy, reason, grantMakeupCredit }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
      queryClient.invalidateQueries({ queryKey: ["makeup-credits"] });
      queryClient.invalidateQueries({ queryKey: ["benchmark"] });
    },
  });
}

// "Yoklama" ekranı (/dashboard/attendance): geçmişe dönük yoklama dökümü + öğretmen kırılımı.
// Backend: Modules/Attendance/Features/AttendanceHistory.cs.
export type AttendanceFilter = AttendanceStatus | "NotMarked";

export interface AttendanceHistoryItem {
  lessonId: string;
  startAt: string;
  endAt: string;
  lessonStatus: "Normal" | "Rescheduled" | "Cancelled" | "Completed" | "Makeup";
  studentId: string;
  studentName: string;
  teacherId: string;
  teacherName: string;
  instrumentId: string;
  instrumentName: string;
  // Yoklaması hiç girilmemiş geçmiş dersler de listeye girer - o satırlarda null.
  attendanceStatus: AttendanceStatus | null;
  markedAt: string | null;
  note: string | null;
}

export interface AttendanceTeacherBreakdown {
  teacherId: string;
  teacherName: string;
  lessonCount: number;
  presentCount: number;
  absentCount: number;
  excusedCount: number;
  notMarkedCount: number;
  lastLessonAt: string | null;
}

export interface AttendanceHistory {
  lessons: PagedResponse<AttendanceHistoryItem>;
  teachers: AttendanceTeacherBreakdown[];
  totalLessonCount: number;
  presentCount: number;
  absentCount: number;
  excusedCount: number;
  notMarkedCount: number;
}

export interface AttendanceHistoryQuery {
  from: string;
  to: string;
  teacherId?: string;
  studentId?: string;
  status?: AttendanceFilter;
  page?: number;
  pageSize?: number;
}

export function useAttendanceHistory(query: AttendanceHistoryQuery) {
  const params = new URLSearchParams({ from: query.from, to: query.to });
  if (query.teacherId) params.set("teacherId", query.teacherId);
  if (query.studentId) params.set("studentId", query.studentId);
  if (query.status) params.set("status", query.status);
  params.set("page", String(query.page ?? 1));
  params.set("pageSize", String(query.pageSize ?? 50));

  return useQuery({
    queryKey: ["attendance-history", params.toString()],
    queryFn: () => api.get<AttendanceHistory>(`/api/attendance/history?${params.toString()}`),
    placeholderData: (previous) => previous,
  });
}
