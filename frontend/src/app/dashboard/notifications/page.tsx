"use client";

import { useState } from "react";
import { ApiError } from "@/lib/api";
import {
  useNotifications,
  useRetryNotification,
  type NotificationJobStatus,
  type NotificationJobType,
} from "@/lib/messaging";

// docs/04-permissions.md: WhatsApp bildirim durumu tamamen Admin - app-header.tsx
// ADMIN_ONLY_LINKS'e bak. abdera-notification skill madde 10: "FAILED durumuna düşerse
// yönetici panelinde görünüyor mu, 'yeniden dene' uç noktası çalışıyor mu?" - bu sayfa
// tam olarak o kontrolü sağlıyor.
const STATUS_LABELS: Record<NotificationJobStatus, string> = {
  Pending: "bekliyor",
  Processing: "işleniyor",
  Sent: "gönderildi",
  Failed: "başarısız",
  Cancelled: "iptal edildi",
};

const STATUS_COLORS: Record<NotificationJobStatus, string> = {
  Pending: "text-neutral-500",
  Processing: "text-amber-600",
  Sent: "text-green-700",
  Failed: "text-red-600",
  Cancelled: "text-neutral-400",
};

const TYPE_LABELS: Record<NotificationJobType, string> = {
  LessonReminder: "Ders hatırlatması",
  LessonRescheduled: "Ders saati değişti",
  MakeupApproved: "Telafi onaylandı",
  PaymentReminder: "Aidat hatırlatması",
  Birthday: "Doğum günü",
  PackageEnding: "Paket bitiyor",
};

const FILTERS: { value: NotificationJobStatus | "all"; label: string }[] = [
  { value: "all", label: "Tümü" },
  { value: "Pending", label: "Bekliyor" },
  { value: "Sent", label: "Gönderildi" },
  { value: "Failed", label: "Başarısız" },
  { value: "Cancelled", label: "İptal" },
];

export default function NotificationsPage() {
  const [filter, setFilter] = useState<NotificationJobStatus | "all">("all");
  const { data: jobs, isLoading } = useNotifications(filter === "all" ? undefined : filter);
  const retry = useRetryNotification();
  const [retryError, setRetryError] = useState<string | null>(null);

  async function handleRetry(jobId: string) {
    setRetryError(null);
    try {
      await retry.mutateAsync(jobId);
    } catch (err) {
      setRetryError(err instanceof ApiError ? (err.detail ?? err.title) : "Yeniden denenemedi.");
    }
  }

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold">Bildirimler</h1>
      <p className="text-sm text-neutral-500">
        WhatsApp üzerinden gönderilen/gönderilecek bildirimlerin durumu. Başarısız olanlar en fazla deneme
        sayısına ulaştıktan sonra burada kalır - elle yeniden denenebilir.
      </p>

      <div className="flex flex-wrap gap-2">
        {FILTERS.map((f) => (
          <button
            key={f.value}
            onClick={() => setFilter(f.value)}
            className={
              filter === f.value
                ? "rounded-md bg-neutral-900 px-3 py-1 text-sm text-white"
                : "rounded-md border border-neutral-300 px-3 py-1 text-sm text-neutral-700 hover:bg-neutral-100"
            }
          >
            {f.label}
          </button>
        ))}
      </div>

      {retryError && <p className="text-sm text-red-600">{retryError}</p>}
      {isLoading && <p className="text-sm text-neutral-500">Yükleniyor…</p>}

      <div className="overflow-x-auto rounded-lg border border-neutral-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-neutral-200 text-left text-xs text-neutral-500">
              <th className="px-3 py-2">Tip</th>
              <th className="px-3 py-2">Alıcı</th>
              <th className="px-3 py-2">Planlanan zaman</th>
              <th className="px-3 py-2">Durum</th>
              <th className="px-3 py-2">Deneme</th>
              <th className="px-3 py-2">Hata</th>
              <th className="px-3 py-2" />
            </tr>
          </thead>
          <tbody>
            {jobs?.map((job) => (
              <tr key={job.id} className="border-b border-neutral-100 last:border-0">
                <td className="px-3 py-2">{TYPE_LABELS[job.type] ?? job.type}</td>
                <td className="px-3 py-2 text-neutral-500">{job.recipientPhoneNumber}</td>
                <td className="px-3 py-2 text-neutral-500">
                  {new Date(job.scheduledAt).toLocaleString("tr-TR")}
                </td>
                <td className={`px-3 py-2 font-medium ${STATUS_COLORS[job.status]}`}>
                  {STATUS_LABELS[job.status]}
                </td>
                <td className="px-3 py-2 text-neutral-500">{job.attemptCount}</td>
                <td className="max-w-xs truncate px-3 py-2 text-neutral-500" title={job.lastError ?? undefined}>
                  {job.lastError ?? "—"}
                </td>
                <td className="px-3 py-2">
                  {job.status === "Failed" && (
                    <button
                      onClick={() => handleRetry(job.id)}
                      disabled={retry.isPending}
                      className="rounded-md border border-neutral-300 px-2 py-1 text-xs hover:bg-neutral-100 disabled:opacity-50"
                    >
                      Yeniden dene
                    </button>
                  )}
                </td>
              </tr>
            ))}
            {jobs?.length === 0 && !isLoading && (
              <tr>
                <td colSpan={7} className="px-3 py-6 text-center text-neutral-400">
                  Bu filtrede bildirim yok.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
