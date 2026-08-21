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
