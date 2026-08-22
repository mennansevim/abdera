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
  Pending: "text-[var(--muted)]",
  Processing: "text-[var(--warning)]",
  Sent: "text-[var(--success-strong)]",
  Failed: "text-[var(--danger)]",
  Cancelled: "text-[var(--muted)]",
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

const PAGE_SIZE = 50;

export default function NotificationsPage() {
  const [filter, setFilter] = useState<NotificationJobStatus | "all">("all");
  const [page, setPage] = useState(1);
  const { data, isLoading } = useNotifications(filter === "all" ? undefined : filter, page, PAGE_SIZE);
  const jobs = data?.items;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  const retry = useRetryNotification();
  const [retryError, setRetryError] = useState<string | null>(null);

  function handleFilterChange(next: NotificationJobStatus | "all") {
    setFilter(next);
    setPage(1); // filtre değişince sayfa sıfırlanır - aksi halde boş bir sayfada kalınabilir.
  }

  async function handleRetry(jobId: string) {
    setRetryError(null);
    try {
      await retry.mutateAsync(jobId);
    } catch (err) {
      setRetryError(err instanceof ApiError ? (err.detail ?? err.title) : "Yeniden denenemedi.");
    }
  }

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-display font-serif italic">Bildirimler</h1>
        <p className="text-meta mt-1">
          WhatsApp üzerinden gönderilen/gönderilecek bildirimlerin durumu. Başarısız olanlar en fazla deneme
          sayısına ulaştıktan sonra burada kalır - elle yeniden denenebilir.
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        {FILTERS.map((f) => (
          <button
            key={f.value}
            onClick={() => handleFilterChange(f.value)}
            className={`pressable min-h-10 rounded-full px-3.5 text-xs font-bold ${
              filter === f.value
                ? "bg-[var(--brand)] text-white"
                : "border border-[var(--line)] bg-white text-[var(--muted)] hover:border-[#e0c39d]"
            }`}
          >
            {f.label}
          </button>
        ))}
      </div>

      {retryError && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{retryError}</p>}
      {isLoading && <div className="space-y-2">{Array.from({ length: 5 }, (_, index) => <div key={index} className="skeleton h-11 rounded-xl" />)}</div>}

      <div className="app-card overflow-x-auto">
        <table className="w-full min-w-[46rem] text-sm">
          <thead>
            <tr className="text-micro border-b border-[var(--line)] text-left">
              <th className="px-3 py-3">Tip</th>
              <th className="px-3 py-3">Alıcı</th>
              <th className="px-3 py-3">Planlanan zaman</th>
              <th className="px-3 py-3">Durum</th>
              <th className="px-3 py-3">Deneme</th>
              <th className="px-3 py-3">Hata</th>
              <th className="px-3 py-3" />
            </tr>
          </thead>
          <tbody>
            {jobs?.map((job) => (
              <tr key={job.id} className="border-b border-[var(--line)] last:border-0">
                <td className="px-3 py-3 font-medium">{TYPE_LABELS[job.type] ?? job.type}</td>
                <td className="text-meta px-3 py-3">{job.recipientPhoneNumber}</td>
                <td className="text-meta px-3 py-3">
                  {new Date(job.scheduledAt).toLocaleString("tr-TR")}
                </td>
                <td className={`px-3 py-3 font-bold ${STATUS_COLORS[job.status]}`}>
                  {STATUS_LABELS[job.status]}
                </td>
                <td className="text-meta px-3 py-3">{job.attemptCount}</td>
                <td className="text-meta max-w-xs truncate px-3 py-3" title={job.lastError ?? undefined}>
                  {job.lastError ?? "—"}
                </td>
                <td className="px-3 py-3">
                  {job.status === "Failed" && (
                    <button
                      onClick={() => handleRetry(job.id)}
                      disabled={retry.isPending}
                      className="pressable min-h-9 rounded-lg border border-[var(--line)] bg-white px-2.5 text-xs font-bold text-[var(--brand)] hover:bg-[var(--surface-muted)] disabled:opacity-50"
                    >
                      Yeniden dene
                    </button>
                  )}
                </td>
              </tr>
            ))}
            {jobs?.length === 0 && !isLoading && (
              <tr>
                <td colSpan={7} className="px-3 py-8 text-center text-sm text-[var(--muted)]">
                  Bu filtrede bildirim yok.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {data && data.totalCount > 0 && (
        <div className="flex items-center justify-between text-sm">
          <span className="text-meta">
            Toplam {data.totalCount} kayıt - sayfa {data.page} / {totalPages}
          </span>
          <div className="flex gap-2">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold hover:bg-[var(--surface-muted)] disabled:opacity-50"
            >
              Önceki
            </button>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold hover:bg-[var(--surface-muted)] disabled:opacity-50"
            >
              Sonraki
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
