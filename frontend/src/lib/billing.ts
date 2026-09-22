// Pricing + Billing API'leri - docs/07-api.md. Yalnızca Admin erişebilir (docs/04-permissions.md).
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef } from "react";
import { api } from "./api";

export type CourseKind = "Individual" | "Group";
export type PaymentMethod = "Cash" | "Transfer" | "Card" | "Other";
export type ReceivableStatus = "Unpaid" | "Partial" | "Paid" | "Overdue" | "Cancelled";

export const COURSE_KIND_LABEL: Record<CourseKind, string> = { Individual: "Birebir", Group: "Grup" };

// Ücret tarifesi - eski PriceList + PriceListItem + FeePlan üçlüsünün yerini alır.
// Fiyat yalnızca ders türüne (birebir/grup) bağlı; enstrüman ve ders süresi tutarı
// değiştirmiyor (docs/10-decisions.md H1).
export interface TuitionRate {
  id: string;
  courseKind: CourseKind;
  lessonsPerMonth: number;
  monthlyAmount: number;
  currency: string;
  effectiveFrom: string;
  effectiveUntil: string | null;
  isCurrent: boolean;
}

export function useTuitionRates() {
  return useQuery({ queryKey: ["tuition-rates"], queryFn: () => api.get<TuitionRate[]>("/api/tuition-rates") });
}

// Zam ayrı bir "toplu güncelleme" işlemi değil: yeni yürürlük tarihiyle yeni satır açılır,
// öncekisi sunucuda otomatik kapanır. Geçmiş aidatlar tutarını kendi satırında taşıdığı
// için değişmez.
export function useCreateTuitionRate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { courseKind: CourseKind; lessonsPerMonth: number; monthlyAmount: number; effectiveFrom: string; currency?: string }) =>
      api.post<TuitionRate>("/api/tuition-rates", body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tuition-rates"] });
      queryClient.invalidateQueries({ queryKey: ["monthly-due-run"] });
    },
  });
}

export interface PrepayTier {
  id?: string;
  minMonths: number;
  percent: number;
}

export interface BillingPolicy {
  multiCourseDiscountPercent: number;
  siblingDiscountPercent: number;
  dueDayOfMonth: number;
  prepayTiers: PrepayTier[];
}

export function useBillingPolicy() {
  return useQuery({ queryKey: ["billing-policy"], queryFn: () => api.get<BillingPolicy>("/api/billing-policy") });
}

export function useUpdateBillingPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: BillingPolicy) => api.put<BillingPolicy>("/api/billing-policy", body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["billing-policy"] });
      queryClient.invalidateQueries({ queryKey: ["monthly-due-run"] });
      queryClient.invalidateQueries({ queryKey: ["prepay-preview"] });
    },
  });
}

// Kurs kaydının aidatı etkileyen iki alanı: ders türü ve o kayda özel elle indirim.
export function useUpdateEnrollmentBilling(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ enrollmentId, ...body }: { enrollmentId: string; courseKind?: CourseKind; manualDiscountPercent?: number | null; manualDiscountReason?: string | null }) =>
      api.patch(`/api/students/${studentId}/enrollments/${enrollmentId}`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] });
      queryClient.invalidateQueries({ queryKey: ["enrollments", studentId] });
      queryClient.invalidateQueries({ queryKey: ["monthly-due-run"] });
      queryClient.invalidateQueries({ queryKey: ["prepay-preview"] });
    },
  });
}

export interface StudentBillingRow {
  enrollmentId: string;
  instrumentId: string;
  courseKind: CourseKind;
  manualDiscountPercent: number | null;
  manualDiscountReason: string | null;
  receivables: Receivable[];
}

export interface Receivable {
  id: string;
  enrollmentId: string;
  period: string;
  amount: number;
  currency: string;
  dueDate: string;
  status: ReceivableStatus;
  totalPaid: number;
  payments: PaymentRecord[];
  // İndirimin nereden geldiğini satırın kendisi açıklar - başka tabloya gitmeye gerek yok.
  baseAmount: number;
  discountPercent: number;
  discountReason: string | null;
  prepayPlanId: string | null;
}

export interface BillingDue extends Receivable {
  studentId: string;
  studentName: string;
  teacherId: string;
  teacherName: string;
  instrumentId: string;
  instrumentName: string;
  courseKind: CourseKind;
}

export interface PaymentRecord {
  id: string;
  amount: number;
  paymentDate: string;
  method: PaymentMethod;
  reference: string | null;
  note: string | null;
  kind: "Payment" | "Correction";
  correctsPaymentId: string | null;
  previousAmount: number | null;
  recordedAt: string | null;
  prepayPlanId: string | null;
  prepayPlanMonths: number | null;
}

export type ExpenseCategory = "Salary" | "Utilities" | "Rent" | "Other";
export interface Expense {
  id: string;
  category: ExpenseCategory;
  description: string;
  amount: number;
  currency: string;
  expenseDate: string;
  note: string | null;
}

export function useExpenses() {
  return useQuery({ queryKey: ["expenses"], queryFn: () => api.get<Expense[]>("/api/expenses") });
}

export function useCreateExpense() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { category: ExpenseCategory; description: string; amount: number; currency?: string; expenseDate: string; note?: string }) => api.post<Expense>("/api/expenses", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["expenses"] }),
  });
}

export type MakeupCreditStatus = "Available" | "Used" | "Expired";
export interface MakeupCredit {
  id: string;
  studentId: string;
  sourceLessonId: string;
  earnedReason: "GuardianCancelled24H" | "SchoolCancelled";
  earnedAt: string;
  expiresAt: string;
  status: MakeupCreditStatus;
  usedLessonId: string | null;
  sourceLessonStartAt: string;
}

export function useMakeupCredits(studentId: string) {
  return useQuery({
    queryKey: ["makeup-credits", studentId],
    queryFn: () => api.get<MakeupCredit[]>(`/api/students/${studentId}/makeup-credits`),
    enabled: !!studentId,
  });
}

export function useUseMakeupCredit(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ creditId, ...body }: { creditId: string; teacherId: string; instrumentId: string; startAt: string; durationMinutes: number }) =>
      api.post<{ creditId: string; newLessonId: string }>(`/api/makeup-credits/${creditId}/use`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["makeup-credits", studentId] });
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
    },
  });
}

export function useReceivables(status?: ReceivableStatus) {
  const params = status ? `?status=${encodeURIComponent(status)}` : "";
  return useQuery({
    queryKey: ["receivables", status ?? "all"],
    queryFn: () => api.get<Receivable[]>(`/api/receivables${params}`),
  });
}

// `enabled` opsiyonel: aidat listesindeki her satır "Geçmiş" collapse'ını AÇILANA kadar bu
// isteği göndermemeli - aksi halde ekrandaki her satır için görünmeyen bir istek atılırdı.
export function useStudentBilling(studentId: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["student-billing", studentId],
    queryFn: () => api.get<StudentBillingRow[]>(`/api/students/${studentId}/billing`),
    enabled: !!studentId && (options?.enabled ?? true),
  });
}

// docs/04-permissions.md: aidat verisi tamamen Admin - bu uç 403 verir. `enabled: false`
// ile çağıranlar (örn. Ders Programı'nın gecikmiş-aidat uyarısı Teacher oturumunda) isteği
// hiç göndermeden bunu atlayabilir.
export function useBillingDues(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["billing-dues"],
    queryFn: () => api.get<BillingDue[]>("/api/billing/dues"),
    enabled: options?.enabled,
  });
}

// Ay başı üretimi (MonthlyDueRun.cs): bir dönemin aidatlarını tüm aktif kurs kayıtları
// için tek çağrıda açar. Önizleme ayrı bir uçtan gelir; ekran "kim ne kadar ödeyecek,
// hangi indirimle, hangileri zaten var, hangisinde tarife eksik" bilgisini işlemden
// ÖNCE gösterebilsin diye.
export type MonthlyDueTarget = {
  enrollmentId: string;
  studentId: string;
  studentName: string;
  instrumentName: string;
  teacherName: string;
  courseKind: CourseKind;
  baseAmount: number;
  discountPercent: number;
  discountReason: string | null;
  amount: number;
  currency: string;
};

export type MonthlyDueMissing = {
  enrollmentId: string;
  studentId: string;
  studentName: string;
  instrumentName: string;
  teacherName: string;
  courseKind: CourseKind;
  reason: string;
};

export type MonthlyDuePlan = {
  period: string;
  dueDate: string;
  ready: MonthlyDueTarget[];
  alreadyExists: MonthlyDueTarget[];
  missing: MonthlyDueMissing[];
  readyBaseTotal: number;
  readyTotal: number;
  readyDiscountTotal: number;
  currency: string;
};

export type MonthlyDueResult = {
  period: string;
  createdCount: number;
  createdTotal: number;
  createdDiscountTotal: number;
  currency: string;
  alreadyExistsCount: number;
  missing: MonthlyDueMissing[];
};

export function useMonthlyDuePlan(period: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["monthly-due-run", period],
    queryFn: () => api.get<MonthlyDuePlan>(`/api/receivables/monthly-run?period=${encodeURIComponent(period)}`),
    enabled: !!period && (options?.enabled ?? true),
  });
}

export function useRunMonthlyDues() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (period: string) => api.post<MonthlyDueResult>("/api/receivables/monthly-run", { period }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["receivables"] });
      queryClient.invalidateQueries({ queryKey: ["billing-dues"] });
      queryClient.invalidateQueries({ queryKey: ["monthly-due-run"] });
      queryClient.invalidateQueries({ queryKey: ["student-billing"] });
    },
  });
}

export function useCreateReceivable(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { enrollmentId: string; period: string }) => api.post<Receivable>("/api/receivables", body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] });
      queryClient.invalidateQueries({ queryKey: ["billing-dues"] });
      queryClient.invalidateQueries({ queryKey: ["receivables"] });
    },
  });
}

export function useRecordPayment(studentId: string) {
  const queryClient = useQueryClient();
  // Aynı kullanıcı niyeti ağ yanıtı kaybolduğu için yeniden denenirse aynı anahtar gider.
  // Başarılı yanıttan sonra anahtar temizlenir; alanlardan biri değişirse fingerprint de
  // değiştiği için yeni ve bağımsız bir tahsilat isteği oluşur.
  const attempts = useRef(new Map<string, string>());
  return useMutation({
    mutationFn: ({ receivableId, ...body }: { receivableId: string; amount: number; paymentDate: string; method: PaymentMethod; reference?: string; note?: string }) => {
      const fingerprint = JSON.stringify({ receivableId, ...body });
      const idempotencyKey = attempts.current.get(fingerprint) ?? crypto.randomUUID();
      attempts.current.set(fingerprint, idempotencyKey);
      return api.post(`/api/receivables/${receivableId}/payments`, body, {
        headers: { "Idempotency-Key": idempotencyKey },
      }).then((result) => {
        attempts.current.delete(fingerprint);
        return result;
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] });
      queryClient.invalidateQueries({ queryKey: ["billing-dues"] });
      queryClient.invalidateQueries({ queryKey: ["receivables"] });
    },
  });
}

export function useCorrectPayment(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ paymentId, correctedAmount, reason }: { paymentId: string; correctedAmount: number; reason: string }) =>
      api.post(`/api/payments/${paymentId}/corrections`, { correctedAmount, reason }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] });
      queryClient.invalidateQueries({ queryKey: ["billing-dues"] });
      queryClient.invalidateQueries({ queryKey: ["receivables"] });
    },
  });
}

// Yıl başı peşin ödeme kampanyası (PrepayPlans.cs). Tutarı SUNUCU hesaplar - ekran
// yalnızca gördüğü toplamı teyit eder (expectedTotal). Eski akış istemcinin gönderdiği
// tutarın indirimsiz toplama birebir eşit olmasını şart koştuğu için kampanya indirimi
// sisteme hiç girilemiyordu.
export type PrepayMonthRow = {
  period: string;
  dueDate: string;
  baseAmount: number;
  amount: number;
  alreadyExists: boolean;
  blockedReason: string | null;
};

export type PrepayPreview = {
  enrollmentId: string;
  studentId: string;
  studentName: string;
  instrumentName: string;
  courseKind: CourseKind;
  startPeriod: string;
  months: number;
  studentDiscountPercent: number;
  studentDiscountReason: string | null;
  prepayPercent: number;
  baseTotal: number;
  total: number;
  savingTotal: number;
  currency: string;
  monthRows: PrepayMonthRow[];
  blockers: string[];
};

export type PrepayResult = {
  prepayPlanId: string;
  startPeriod: string;
  months: number;
  prepayPercent: number;
  baseTotal: number;
  total: number;
  savingTotal: number;
  currency: string;
  receivables: Receivable[];
};

export function usePrepayPreview(enrollmentId: string, startPeriod: string, months: number, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["prepay-preview", enrollmentId, startPeriod, months],
    queryFn: () =>
      api.get<PrepayPreview>(
        `/api/enrollments/${enrollmentId}/prepay-preview?startPeriod=${encodeURIComponent(startPeriod)}&months=${months}`,
      ),
    enabled: !!enrollmentId && !!startPeriod && months >= 1 && (options?.enabled ?? true),
  });
}

export function useCreatePrepayPlan(studentId: string, enrollmentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { startPeriod: string; months: number; paymentDate: string; method: PaymentMethod; reference?: string; note?: string; expectedTotal?: number }) =>
      api.post<PrepayResult>(`/api/enrollments/${enrollmentId}/prepay-plans`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] });
      queryClient.invalidateQueries({ queryKey: ["receivables"] });
      queryClient.invalidateQueries({ queryKey: ["billing-dues"] });
      queryClient.invalidateQueries({ queryKey: ["prepay-preview"] });
      queryClient.invalidateQueries({ queryKey: ["monthly-due-run"] });
    },
  });
}
