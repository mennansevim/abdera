// Attendance + ders değişikliği + telafi API'leri - docs/07-api.md.
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { PagedResponse } from "./messaging";

export type AttendanceStatus = "Present" | "Absent" | "Excused";

export const ATTENDANCE_LABELS: Record<AttendanceStatus, string> = { Present: "Geldi", Absent: "Gelmedi", Excused: "Mazeretli" };

// Sunucudaki Lesson.AttendanceOpensBeforeStart ile aynı: öğrenci erken gelirse yoklama
// dersten en fazla bu kadar önce alınabilir, daha önce sunucu 409 döner.
export const ATTENDANCE_OPENS_BEFORE_MINUTES = 15;

export function isAttendanceOpen(startAt: string, now: Date = new Date()) {
  return new Date(startAt).getTime() - ATTENDANCE_OPENS_BEFORE_MINUTES * 60_000 <= now.getTime();
}

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
      // "Mazeretli" telafi hakkı doğurur, ondan çıkan düzeltme hakkı geri alır.
      queryClient.invalidateQueries({ queryKey: ["makeup-credits"] });
      // Birebir derste yoklama kartın satırını doldurur; dönemin ilk dersi aidatı açar.
      queryClient.invalidateQueries({ queryKey: ["attendance-cards"] });
      queryClient.invalidateQueries({ queryKey: ["billing-board"] });
    },
  });
}

// Yönetici yoklama ekranı (AttendanceCards.cs, docs/10-decisions.md AD): birebir kayıt başına
// okulun kâğıt "Yoklama Listesi"nin dijital hali. 4 satır bir dönemdir; Geldi ve Mazeretli satır
// doldurur, Gelmedi kartın altına not düşer. Kartı ve durumu sunucu kurar, ekran yalnızca çizer.
export type CardState = "NotStarted" | "InProgress" | "LastLesson" | "AwaitingPayment" | "NextPrepaid" | "Unpaid";

export interface CardLesson {
  // Sistemdeki yoklamalı ders; karta elle yazılmış geçmiş derste null, entryId dolu.
  lessonId: string | null;
  entryId: string | null;
  startAt: string;
  date: string;
  status: AttendanceStatus;
}

export interface CycleReceivable {
  id: string;
  status: "Unpaid" | "Partial" | "Paid" | "Overdue" | "Cancelled";
  amount: number;
  paid: number;
  currency: string;
  dueDate: string;
  accruedOn: string | null;
}

export interface CardCycle {
  number: number;
  filled: CardLesson[];
  misses: CardLesson[];
  receivable: CycleReceivable | null;
}

export interface AttendanceCard {
  enrollmentId: string;
  studentId: string;
  studentName: string;
  teacherId: string;
  teacherName: string;
  instrumentName: string;
  startedAt: string;
  // Dönem sayacının başladığı gün; null: kayda başlangıç girilmedi ve okul geneli geçiş kapalı.
  countingFrom: string | null;
  currentCycle: number;
  filledInCurrent: number;
  state: CardState;
  unpaidAmount: number;
  currency: string;
  lastLessonDate: string | null;
  cycles: CardCycle[];
  // Geçiş gününden önce işlenmiş dersler: yalnızca bilgi, karta sayılmaz ve aidat açmaz.
  earlierLessons: CardLesson[];
  // Sayaç başlangıcı kayda özel girilmiş mi (öğrencinin ilk giriş günü).
  hasOwnCycleStart: boolean;
}

export interface AttendanceCardsResponse {
  cycleBillingStartsOn: string | null;
  cards: AttendanceCard[];
  // Karta elle ders yazılabilecek en eski gün (geçen ayın 1'i).
  entriesFrom: string;
}

// Karta elle yazılan geçmiş ders (kâğıttaki satır): eklenir, tarihi değişir ya da kaldırılır.
// Her değişiklikte sunucu kartı yeniden sayar ve dönem aidatlarını eşitler.
function useCardEntryMutation<T>(fn: (body: T) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["attendance-cards"] });
      queryClient.invalidateQueries({ queryKey: ["billing-board"] });
    },
  });
}

export function useAddCardEntry() {
  return useCardEntryMutation(({ enrollmentId, date }: { enrollmentId: string; date: string }) =>
    api.post(`/api/attendance/cards/${enrollmentId}/entries`, { date }));
}

export function useChangeCardEntry() {
  return useCardEntryMutation(({ entryId, date }: { entryId: string; date: string }) =>
    api.put(`/api/attendance/cards/entries/${entryId}`, { date }));
}

export function useRemoveCardEntry() {
  return useCardEntryMutation((entryId: string) => api.delete(`/api/attendance/cards/entries/${entryId}`));
}

export function useAttendanceCards(enabled = true) {
  return useQuery({
    queryKey: ["attendance-cards"],
    queryFn: () => api.get<AttendanceCardsResponse>("/api/attendance/cards"),
    enabled,
  });
}

export type PieceStatus = "Learning" | "Polishing" | "PerformanceReady" | "Archived";

export function useCreateLessonNote(lessonId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { practiced?: string; note?: string; homework?: string; nextGoal?: string; pieceTitle?: string; pieceDifficulty?: number; pieceComposer?: string; pieceStatus?: PieceStatus }) =>
      api.post(`/api/lessons/${lessonId}/notes`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["pending-lesson-notes"] });
      // Bu not, sonraki derslerin "Ders devam ediyor" önerisi olur.
      queryClient.invalidateQueries({ queryKey: ["previous-lesson-note"] });
      // Son eksik not yazılınca sunucu zildeki hatırlatmayı kapatır; yoklaması girilmemiş derse
      // yazılan not yoklamayı "geldi" olarak işler ve "öğrenci geldi mi?" sorusunu da kapatır.
      queryClient.invalidateQueries({ queryKey: ["staff-notifications"] });
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
      queryClient.invalidateQueries({ queryKey: ["lesson-notes", lessonId] });
    },
  });
}

export interface LessonNoteSummary {
  id: string;
  lessonId: string;
  practiced: string | null;
  note: string | null;
  homework: string | null;
  nextGoal: string | null;
  pieceTitle: string | null;
  createdAt: string;
}

// Bir derse yazılmış notlar (LessonNotes.ListAsync, en yeni önce). Bugün ekranındaki ders
// kartı notu girilmiş dersi "Not girildi" diye ayırt etsin diye: kart her derste "Not Ekle"
// yazınca öğretmen notu yazdığı dersi tanıyamıyor, aynı derse ikinci kez not giriyordu.
export function useLessonNotes(lessonId: string) {
  return useQuery({
    queryKey: ["lesson-notes", lessonId],
    queryFn: () => api.get<LessonNoteSummary[]>(`/api/lessons/${lessonId}/notes`),
    enabled: Boolean(lessonId),
  });
}

// Not formundaki "Ders devam ediyor" kısayolu: aynı öğrencinin aynı enstrümandaki bir önceki
// dersinden taşınacak alanlar (LessonNotes.PreviousAsync). Önceki not yoksa previous null.
export interface PreviousLessonNote {
  lessonStartAt: string;
  practiced: string | null;
  homework: string | null;
  nextGoal: string | null;
  pieceTitle: string | null;
  pieceComposer: string | null;
  pieceDifficulty: number | null;
  pieceStatus: PieceStatus | null;
}

export function usePreviousLessonNote(lessonId: string) {
  return useQuery({
    queryKey: ["previous-lesson-note", lessonId],
    queryFn: () => api.get<{ previous: PreviousLessonNote | null }>(`/api/lessons/${lessonId}/notes/previous`),
    enabled: Boolean(lessonId),
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
