import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { TeacherStatus } from "./people";

// Öğretmenlere haftalık ders ödemesi (docs/10-decisions.md N1). Ödeme haftası PAZAR başlar,
// CUMARTESİ kapanır ve ödeme o cumartesi yapılır - kullanıcı kuralı: "her cumartesi
// tamamlanan derslerin ödemesini yapıyorum". Haftanın sınırını sunucu belirler; istemci
// yalnızca dönüp gelen `weekStart`'ı ±7 gün kaydırır, böylece tarayıcının saat dilimi
// okulunkinden farklı olduğunda bile yanlış haftaya bakılmaz.

export interface TeacherPayout {
  id: string;
  teacherId: string;
  weekStart: string;
  weekEnd: string;
  lessonCount: number;
  ratePerLesson: number;
  computedAmount: number;
  amount: number;
  currency: string;
  paidOn: string;
  note: string | null;
  expenseId: string;
  createdAt: string;
}

export interface TeacherPayoutWeekRow {
  teacherId: string;
  firstName: string;
  lastName: string;
  status: TeacherStatus;
  completedLessons: number;
  ratePerLesson: number | null;
  computedAmount: number | null;
  currency: string;
  payout: TeacherPayout | null;
}

export interface TeacherPayoutWeek {
  weekStart: string;
  weekEnd: string;
  payDay: string;
  isCurrentWeek: boolean;
  isClosed: boolean;
  totalCompletedLessons: number;
  paidTotal: number;
  payableTotal: number;
  currency: string;
  teachers: TeacherPayoutWeekRow[];
}

// `weekStart` verilmezse sunucu bugünü içeren ödeme haftasını döner.
export function useTeacherPayoutWeek(weekStart: string | null, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["teacher-payout-week", weekStart],
    queryFn: () => api.get<TeacherPayoutWeek>(
      weekStart ? `/api/teacher-payouts/week?weekStart=${weekStart}` : "/api/teacher-payouts/week"),
    enabled: options?.enabled ?? true,
  });
}

export function useSetTeacherPayRate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ teacherId, amountPerLesson, currency }: { teacherId: string; amountPerLesson: number; currency?: string }) =>
      api.put(`/api/teacher-payouts/rates/${teacherId}`, { amountPerLesson, currency }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["teacher-payout-week"] }),
  });
}

export function useCreateTeacherPayout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      teacherId: string;
      weekStart: string;
      expectedLessonCount: number;
      expectedAmount: number;
      agreedAmount?: number;
      paidOn?: string;
      note?: string;
    }) => api.post<TeacherPayout>("/api/teacher-payouts", body),
    // Ödeme gider defterine de düştüğü için Giderler ekranının sorguları da tazelenir.
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["teacher-payout-week"] });
      queryClient.invalidateQueries({ queryKey: ["expenses"] });
    },
  });
}

const MONTHS_SHORT = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];

// Tarihler sunucudan "YYYY-MM-DD" gelir. Date'e çevirirken saat dilimine takılmamak için
// bileşenler ayrı ayrı okunur; kaydırma UTC üzerinde yapılır (yaz saati sıçraması yok).
function parts(iso: string) {
  const [year, month, day] = iso.split("-").map(Number);
  return { year, month, day };
}

export function shiftWeekStart(weekStart: string, weeks: number): string {
  const { year, month, day } = parts(weekStart);
  return new Date(Date.UTC(year, month - 1, day + weeks * 7)).toISOString().slice(0, 10);
}

export function formatDayMonth(iso: string): string {
  const { month, day } = parts(iso);
  return `${day} ${MONTHS_SHORT[month - 1]}`;
}

export function formatPayWeek(weekStart: string, weekEnd: string): string {
  return `${formatDayMonth(weekStart)} – ${formatDayMonth(weekEnd)} ${parts(weekEnd).year}`;
}

export const payoutMoney = (value: number, currency = "TRY") =>
  currency === "TRY"
    ? `₺${value.toLocaleString("tr-TR", { maximumFractionDigits: 2 })}`
    : `${value.toLocaleString("tr-TR", { maximumFractionDigits: 2 })} ${currency}`;
