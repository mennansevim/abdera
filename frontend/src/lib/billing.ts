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

export interface PaymentRecord {
  id: string;
  amount: number;
  paymentDate: string;
  method: PaymentMethod;
  reference: string | null;
  note: string | null;
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

export function useReceivables(status?: ReceivableStatus) {
  const params = status ? `?status=${encodeURIComponent(status)}` : "";
  return useQuery({
    queryKey: ["receivables", status ?? "all"],
    queryFn: () => api.get<Receivable[]>(`/api/receivables${params}`),
  });
}

export function useStudentBilling(studentId: string) {
  return useQuery({
    queryKey: ["student-billing", studentId],
    queryFn: () => api.get<StudentBillingRow[]>(`/api/students/${studentId}/billing`),
    enabled: !!studentId,
  });
}

export function useCreateReceivable(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { enrollmentId: string; period: string }) => api.post<Receivable>("/api/receivables", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] }),
  });
}

export function useRecordPayment(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ receivableId, ...body }: { receivableId: string; amount: number; paymentDate: string; method: PaymentMethod; reference?: string; note?: string }) =>
      api.post(`/api/receivables/${receivableId}/payments`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] }),
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
    },
  });
}
