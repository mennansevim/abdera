// Banking (sanal IBAN / gelen havale eşleştirme) API'leri - docs/07-api.md,
// docs/12-bank-integration.md. Yalnızca Admin erişebilir (docs/04-permissions.md).
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export type BankTransactionStatus = "Received" | "Matched" | "NeedsReview" | "Ignored";

export interface VirtualIban {
  id: string;
  guardianId: string;
  iban: string;
  provider: string;
  status: "Active" | "Inactive";
}

export interface BankTransaction {
  id: string;
  virtualIbanId: string;
  guardianId: string;
  amount: number;
  currency: string;
  senderName: string | null;
  description: string | null;
  receivedAt: string;
  status: BankTransactionStatus;
  matchedReceivableId: string | null;
}

export function useGuardianVirtualIban(guardianId: string) {
  return useQuery({
    queryKey: ["virtual-iban", guardianId],
    queryFn: async () => {
      try {
        return await api.get<VirtualIban>(`/api/guardians/${guardianId}/virtual-iban`);
      } catch {
        return null;
      }
    },
    enabled: !!guardianId,
  });
}

export function useAssignVirtualIban() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (guardianId: string) => api.post<VirtualIban>(`/api/guardians/${guardianId}/virtual-iban`),
    onSuccess: (_data, guardianId) => queryClient.invalidateQueries({ queryKey: ["virtual-iban", guardianId] }),
  });
}

// ARC-3 (docs/13-audit-fix-prompt.md): liste artık Take(200) ile sessizce kesilmiyor,
// backend { items, totalCount, page, pageSize } zarfı dönüyor.
export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export function useBankTransactions(status?: BankTransactionStatus, page: number = 1, pageSize: number = 50) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (status) params.set("status", status);
  return useQuery({
    queryKey: ["bank-transactions", status ?? "all", page, pageSize],
    queryFn: () => api.get<PagedResponse<BankTransaction>>(`/api/bank-transactions?${params.toString()}`),
  });
}

export function useResolveBankTransaction() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ transactionId, receivableId }: { transactionId: string; receivableId: string | null }) =>
      api.post<BankTransaction>(`/api/bank-transactions/${transactionId}/resolve`, { receivableId }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["bank-transactions"] }),
  });
}
