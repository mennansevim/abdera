// Pricing + Billing API'leri - docs/07-api.md. Yalnızca Admin erişebilir (docs/04-permissions.md).
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export type BillingType = "Monthly" | "Package";
export type PaymentMethod = "Cash" | "Transfer" | "Card" | "Other";
export type ReceivableStatus = "Unpaid" | "Partial" | "Paid" | "Overdue" | "Cancelled";

export interface PriceListItem {
  id: string;
  instrumentId: string;
  durationMinutes: number;
  billingType: BillingType;
  amount: number;
  currency: string;
  packageLessonCount: number | null;
}

export interface PriceList {
  id: string;
  name: string;
  effectiveFrom: string;
  effectiveUntil: string | null;
  items: PriceListItem[];
}

export function usePriceLists() {
  return useQuery({ queryKey: ["price-lists"], queryFn: () => api.get<PriceList[]>("/api/price-lists") });
}

export interface CreatePriceListItemInput {
  instrumentId: string;
  durationMinutes: number;
  billingType: BillingType;
  amount: number;
  currency?: string;
  packageLessonCount?: number | null;
}

export function useCreatePriceList() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; effectiveFrom: string; effectiveUntil?: string | null; items: CreatePriceListItemInput[] }) =>
      api.post<PriceList>("/api/price-lists", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["price-lists"] }),
  });
}

export interface BulkUpdatePreviewItem {
  itemId: string;
  instrumentName: string;
  durationMinutes: number;
  billingType: string;
  oldAmount: number;
  newAmount: number;
  activeFeePlanCount: number;
}

export function usePreviewBulkUpdate() {
  return useMutation({
    mutationFn: ({ priceListId, percentageChange }: { priceListId: string; percentageChange: number }) =>
      api.post<BulkUpdatePreviewItem[]>(`/api/price-lists/${priceListId}/preview-bulk-update`, { percentageChange }),
  });
}

export function useApplyBulkUpdate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ priceListId, percentageChange }: { priceListId: string; percentageChange: number }) =>
      api.post<BulkUpdatePreviewItem[]>(`/api/price-lists/${priceListId}/apply`, { percentageChange }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["price-lists"] }),
  });
}

export interface FeePlan {
  id: string;
  enrollmentId: string;
  billingType: BillingType;
  amount: number;
  currency: string;
  dueDay: number | null;
  packageLessonCount: number | null;
  activeFrom: string;
  activeUntil: string | null;
}

export function useFeePlan(enrollmentId: string) {
  return useQuery({
    queryKey: ["fee-plan", enrollmentId],
    queryFn: async () => {
      try {
        return await api.get<FeePlan>(`/api/enrollments/${enrollmentId}/fee-plan`);
      } catch {
        return null;
      }
    },
    enabled: !!enrollmentId,
  });
}

export function useCreateFeePlan(enrollmentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { priceListItemId: string; dueDay?: number; activeFrom: string }) =>
      api.post<FeePlan>(`/api/enrollments/${enrollmentId}/fee-plan`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["fee-plan", enrollmentId] }),
  });
}

export interface StudentBillingRow {
  enrollmentId: string;
  instrumentId: string;
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
}

export interface BillingDue extends Receivable {
  studentId: string;
  studentName: string;
  teacherId: string;
  teacherName: string;
  instrumentId: string;
  instrumentName: string;
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
  earnedReason: "GuardianCancelled24H" | "SchoolCancelled";
  earnedAt: string;
  expiresAt: string;
  status: MakeupCreditStatus;
  usedLessonId: string | null;
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

// Toplu aidat (BulkReceivables.cs): bir dönemin aidatlarını tüm aktif kayıtlar için tek
// çağrıda açar. Önizleme ayrı bir uçtan gelir; ekran "kaç aidat açılacak, hangileri zaten
// var, hangi kayıtta ücret planı eksik" bilgisini işlemden ÖNCE gösterebilsin diye.
export type BulkReceivableTarget = {
  enrollmentId: string;
  studentId: string;
  studentName: string;
  instrumentName: string;
  teacherName: string;
  amount: number;
  currency: string;
};

export type BulkReceivableMissing = {
  enrollmentId: string;
  studentId: string;
  studentName: string;
  instrumentName: string;
  teacherName: string;
  reason: string;
};

export type BulkReceivablePlan = {
  period: string;
  ready: BulkReceivableTarget[];
  alreadyExists: BulkReceivableTarget[];
  missing: BulkReceivableMissing[];
  readyTotal: number;
  currency: string;
};

export type BulkReceivableResult = {
  period: string;
  createdCount: number;
  createdTotal: number;
  currency: string;
  alreadyExistsCount: number;
  missing: BulkReceivableMissing[];
};

export function useBulkReceivablePlan(period: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["bulk-receivable-plan", period],
    queryFn: () => api.get<BulkReceivablePlan>(`/api/receivables/bulk-preview?period=${encodeURIComponent(period)}`),
    enabled: !!period && (options?.enabled ?? true),
  });
}

export function useCreateBulkReceivables() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (period: string) => api.post<BulkReceivableResult>("/api/receivables/bulk", { period }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["receivables"] });
      queryClient.invalidateQueries({ queryKey: ["billing-dues"] });
      queryClient.invalidateQueries({ queryKey: ["bulk-receivable-plan"] });
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
  return useMutation({
    mutationFn: ({ receivableId, ...body }: { receivableId: string; amount: number; paymentDate: string; method: PaymentMethod; reference?: string; note?: string }) =>
      api.post(`/api/receivables/${receivableId}/payments`, body),
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

export function useBulkPayment(studentId: string, enrollmentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { startPeriod: string; months: number; amount: number; paymentDate: string; method: PaymentMethod; reference?: string; note?: string }) =>
      api.post<Receivable[]>(`/api/enrollments/${enrollmentId}/bulk-payments`, { enrollmentId, ...body }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] });
      queryClient.invalidateQueries({ queryKey: ["receivables"] });
      queryClient.invalidateQueries({ queryKey: ["billing-dues"] });
    },
  });
}
