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

export function useNotifications(status?: NotificationJobStatus) {
  const query = status ? `?status=${status}` : "";
  return useQuery({
    queryKey: ["notifications", status ?? "all"],
    queryFn: () => api.get<NotificationJob[]>(`/api/notifications${query}`),
  });
}

export function useRetryNotification() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (jobId: string) => api.post<NotificationJob>(`/api/notifications/${jobId}/retry`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["notifications"] }),
  });
}
