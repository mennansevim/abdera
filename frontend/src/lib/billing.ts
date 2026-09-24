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
  startedAt: string | null;
  endedAt: string | null;
  status: "Active" | "Ended" | null;
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

// Her ay kendiliğinden sayılan gider kalemi (kira, elektrik/su ortalaması, sabit maaş) -
// docs/10-decisions.md M9. Tutar değişince eski sürüm silinmez, seçilen aydan itibaren yeni
// sürüm geçerli olur. Aylar "YYYY-MM".
export interface RecurringExpenseAmount {
  id: string;
  monthlyAmount: number;
  currency: string;
  effectiveFrom: string;
  effectiveUntil: string | null;
}
export interface RecurringExpense {
  id: string;
  category: ExpenseCategory;
  name: string;
  note: string | null;
  isEnded: boolean;
  currentAmount: number;
  amounts: RecurringExpenseAmount[];
}

// Bir kalemin verilen aya ("YYYY-MM") düşen tutarı; o ayda yürürlükte değilse 0. Aylar aynı
// biçimde olduğu için metin karşılaştırması sıralamayla aynıdır.
export function recurringAmountFor(item: RecurringExpense, month: string) {
  return item.amounts.find((amount) => amount.effectiveFrom <= month && (amount.effectiveUntil === null || month <= amount.effectiveUntil))?.monthlyAmount ?? 0;
}

export function useRecurringExpenses() {
  return useQuery({ queryKey: ["recurring-expenses"], queryFn: () => api.get<RecurringExpense[]>("/api/recurring-expenses") });
}

export function useCreateRecurringExpense() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { category: ExpenseCategory; name: string; monthlyAmount: number; effectiveFrom: string; note?: string }) =>
      api.post<RecurringExpense>("/api/recurring-expenses", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["recurring-expenses"] }),
  });
}

export function useChangeRecurringExpenseAmount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string; monthlyAmount: number; effectiveFrom: string }) =>
      api.post<RecurringExpense>(`/api/recurring-expenses/${id}/amounts`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["recurring-expenses"] }),
  });
}

export function useEndRecurringExpense() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, lastMonth }: { id: string; lastMonth: string }) => api.post<RecurringExpense>(`/api/recurring-expenses/${id}/end`, { lastMonth }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["recurring-expenses"] }),
  });
}

export type MakeupCreditStatus ="Available" | "Used" | "Expired";
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

// Ay başı üretimi artık admin'in elle tetiklediği bir akış değil - her kayıt zaten en
// az 1 yıllık sabit haftalık ders taahhüdü olduğu için hangi aidatın açılacağı önceden
// belli. MonthlyReceivableGenerator (backend, BackgroundService) bunu periyodik olarak
// kendisi yapar. `POST /api/receivables/monthly-run` yalnızca bir kaçış kapısı olarak
// (gecikmiş/atlanmış bir dönemi elle telafi etmek için) API'de duruyor; rutin arayüz
// yüzeyi kaldırıldığı için burada bir istemci sarmalayıcısı tutulmuyor.

// Tek bir ayın aidatını elle açar (Admin). Kurs kaydı açılırken o ayın aidatı zaten sunucuda
// otomatik açılıyor; bu yalnızca bir sebeple açılamamış ayı (örn. o ayı kapsayan tarife yoktu)
// Aylık aidatlar ekranından açmak için. Tutarı sunucu tarifeden hesaplar; tarife yoksa 409
// ders türü + dönemi söyleyen bir mesajla döner.
export function useCreateReceivable(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { enrollmentId: string; period: string }) => api.post<Receivable>("/api/receivables", body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] });
      queryClient.invalidateQueries({ queryKey: ["billing-dues"] });
      queryClient.invalidateQueries({ queryKey: ["receivables"] });
      queryClient.invalidateQueries({ queryKey: ["prepay-preview"] });
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
    mutationFn: (body: { startPeriod: string; months: number; paymentDate: string; method: PaymentMethod; reference?: string; note?: string; expectedTotal?: number; agreedTotal?: number }) =>
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
