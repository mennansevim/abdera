// Messaging (WhatsApp bildirim) API'leri - docs/07-api.md. Yalnızca Admin erişebilir
// (docs/04-permissions.md - para/rıza/takvim'e dokunan her şey gibi).
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export type NotificationJobType =
  | "LessonReminder"
  | "LessonRescheduled"
  | "MakeupApproved"
  | "PaymentReminder"
  | "Birthday"
  | "PackageEnding";

export type NotificationJobStatus = "Pending" | "Processing" | "Sent" | "Failed" | "Cancelled";

export interface NotificationJob {
  id: string;
  type: NotificationJobType;
  recipientPhoneNumber: string;
  referenceType: string;
  referenceId: string;
  scheduledAt: string;
  status: NotificationJobStatus;
  attemptCount: number;
  lastError: string | null;
  sentAt: string | null;
}

// ARC-3 (docs/13-audit-fix-prompt.md): liste artık Take(200) ile sessizce kesilmiyor,
// backend { items, totalCount, page, pageSize } zarfı dönüyor.
export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export function useNotifications(status?: NotificationJobStatus, page: number = 1, pageSize: number = 50) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (status) params.set("status", status);
  return useQuery({
    queryKey: ["notifications", status ?? "all", page, pageSize],
    queryFn: () => api.get<PagedResponse<NotificationJob>>(`/api/notifications?${params.toString()}`),
  });
}

export function useRetryNotification() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (jobId: string) => api.post<NotificationJob>(`/api/notifications/${jobId}/retry`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["notifications"] }),
  });
}
