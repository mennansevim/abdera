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
  | "PackageEnding"
  | "InstrumentMaintenance"
  | "LessonScheduleChanged";

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

// ARC-3 (docs/13-audit-fix-prompt.md): liste artık Take(200) ile sessizce kesilmiyor,
// backend { items, totalCount, page, pageSize } zarfı dönüyor.
export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// Ekran içi personel bildirimi (staff_notifications) - WhatsApp job'larından ayrı bir akış:
// bunlar dışarı gönderilmez, oturumdaki kullanıcının kendi zilinde görünür.
// AttendanceMissing: "öğrenci derse geldi mi?" sorusu - referenceId dersin id'si. Okundu
// işaretlemekle kapanmaz, zildeki Geldi/Gelmedi ile yoklama girilince kapanır.
// GuardianRsvp: veli derse gelemeyeceğini ya da gecikeceğini bildirdi - dersin öğretmenine düşer.
export type StaffNotificationType = "LessonMoved" | "LessonCancelled" | "MakeupScheduled" | "StudentDeletionRequested" | "LessonNoteMissing" | "AttendanceMissing" | "GuardianRsvp";

export type StaffNotification = {
  id: string;
  type: StaffNotificationType;
  title: string;
  body: string;
  referenceType: string;
  referenceId: string;
  readAt: string | null;
  createdAt: string;
};

export type StaffNotificationList = { items: StaffNotification[]; unreadCount: number };

// Bildirim sayfa yenilenmeden de düşmeli (ekrandaki açılır kart bu yoklamayla tetiklenir);
// okul ölçeğinde (6-8 personel) 30 saniyede bir küçük bir istek yeterli - websocket/push
// altyapısı kurmaya değmez (CLAUDE.md: gereksiz bağımlılık ekleme).
export function useStaffNotifications(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["staff-notifications"],
    queryFn: () => api.get<StaffNotificationList>("/api/me/notifications"),
    enabled: options?.enabled ?? true,
    refetchInterval: 30_000,
    refetchOnWindowFocus: true,
  });
}

export function useMarkStaffNotificationRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (notificationId: string) => api.post(`/api/me/notifications/${notificationId}/read`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["staff-notifications"] }),
  });
}

export function useMarkAllStaffNotificationsRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post("/api/me/notifications/read-all"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["staff-notifications"] }),
  });
}

export function useNotifications(status?: NotificationJobStatus, page: number = 1, pageSize: number = 50, options?: { enabled?: boolean }) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (status) params.set("status", status);
  return useQuery({
    queryKey: ["notifications", status ?? "all", page, pageSize],
    queryFn: () => api.get<PagedResponse<NotificationJob>>(`/api/notifications?${params.toString()}`),
    enabled: options?.enabled ?? true,
  });
}

// Gelen WhatsApp olayları (whatsapp_webhook_events): veliden gelen mesaj + bot cevabının
// neden gitmediği, ve Meta'nın giden mesaj için bildirdiği teslim hatası.
export type WebhookEventStatus = "Received" | "Processed" | "Failed";

export interface WebhookEvent {
  id: string;
  receivedAt: string;
  eventType: string;
  status: WebhookEventStatus;
  processingError: string | null;
  fromPhoneNumber: string | null;
  guardianName: string | null;
  text: string | null;
  deliveryStatus: string | null;
  deliveryError: string | null;
}

export function useWebhookEvents(status?: WebhookEventStatus, page: number = 1, pageSize: number = 50) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (status) params.set("status", status);
  return useQuery({
    queryKey: ["webhook-events", status ?? "all", page, pageSize],
    queryFn: () => api.get<PagedResponse<WebhookEvent>>(`/api/notifications/webhook-events?${params.toString()}`),
  });
}

export function useRetryNotification() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (jobId: string) => api.post<NotificationJob>(`/api/notifications/${jobId}/retry`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["notifications"] }),
  });
}

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
