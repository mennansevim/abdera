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
  guardianName?: string | null;
  studentName?: string | null;
  lessonType?: string | null;
}

export interface MessageTemplate {
  id: string;
  name: string;
  language: string;
  body: string;
  isActive: boolean;
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

export function useMessageTemplates() {
  return useQuery({
    queryKey: ["message-templates"],
    queryFn: () => api.get<MessageTemplate[]>("/api/message-templates"),
  });
}

export function useUpdateMessageTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string; name: string; body: string; language?: string; isActive: boolean }) =>
      api.patch<MessageTemplate>(`/api/message-templates/${id}`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["message-templates"] }),
  });
}

// Faz 3: ders hatırlatmasının otomatik gönderim ayarı - kalıcı, admin panelden değiştirilebilir
// (bkz. backend Modules/Messaging/Features/AutomationSettings.cs).
export interface NotificationAutomationSettings {
  lessonReminderMinutesBefore: 15 | 30 | 45 | 60;
  isEnabled: boolean;
  allowAttendingLateResponse: boolean;
  updatedAt: string;
}

export function useAutomationSettings() {
  return useQuery({
    queryKey: ["notification-automation-settings"],
    queryFn: () => api.get<NotificationAutomationSettings>("/api/notification-automation-settings"),
  });
}

export function useUpdateAutomationSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { lessonReminderMinutesBefore: number; isEnabled: boolean; allowAttendingLateResponse: boolean }) =>
      api.put<NotificationAutomationSettings>("/api/notification-automation-settings", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["notification-automation-settings"] }),
  });
}
