"use client";

import { useState } from "react";
import { AdminGate, EmptyState, PageHeader, Pager, Panel, Segmented } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  useAutomationSettings,
  useNotifications,
  useRetryNotification,
  useUpdateAutomationSettings,
  useWebhookEvents,
  type NotificationJobStatus,
  type NotificationJobType,
  type WebhookEventStatus,
} from "@/lib/messaging";

const STATUS_LABELS: Record<NotificationJobStatus, string> = {
  Pending: "bekliyor",
  Processing: "işleniyor",
  Sent: "gönderildi",
  Failed: "başarısız",
  // Kısa tutulur: durum filtresi beş seçeneği 375px'lik telefona tek satırda sığdırmak zorunda.
  Cancelled: "iptal",
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
  InstrumentMaintenance: "Enstrüman bakımı",
  LessonScheduleChanged: "Ders programı değişti",
};

export default function NotificationsPage() {
  return <AdminGate><NotificationsPageContent /></AdminGate>;
}

// Mesaj Merkezi tamamen Admin'e özel (docs/04-permissions.md). Şablon metinleri panelden
// düzenlenmez: Meta'da onaylı şablon sabit, gövde migration'la yönetilir (docs/10-decisions.md T1).
function NotificationsPageContent() {
  const [activeTab, setActiveTab] = useState<"activity" | "inbound" | "automation">("activity");

  return (
    <div className="space-y-3">
      <PageHeader
        title="Mesaj Merkezi"
        description="WhatsApp gönderimlerini, veliden gelen mesajları ve otomatik gönderim ayarlarını takip et."
        actions={
          <Segmented
            label="Mesaj Merkezi görünümü"
            options={[{ value: "activity", label: "Gönderim kayıtları" }, { value: "inbound", label: "Gelen mesajlar" }, { value: "automation", label: "Otomasyon" }]}
            value={activeTab}
            onChange={setActiveTab}
          />
        }
      />

      {activeTab === "activity" ? <ActivityPanel /> : activeTab === "inbound" ? <InboundPanel /> : <AutomationSettings />}
    </div>
  );
}

function ActivityPanel() {
  const [filter, setFilter] = useState<NotificationJobStatus | "all">("all");
  const [page, setPage] = useState(1);
  const { data, isLoading } = useNotifications(filter === "all" ? undefined : filter, page, 50);
  const jobs = data?.items;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
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
    <Panel
      flush
      title="Gönderimler"
      meta={data ? `${data.totalCount} kayıt` : undefined}
      actions={
        <Segmented
          label="Duruma göre filtrele"
          options={(["all", "Pending", "Sent", "Failed", "Cancelled"] as const).map((status) => ({ value: status, label: status === "all" ? "Tümü" : STATUS_LABELS[status] }))}
          value={filter}
          onChange={(status) => { setFilter(status); setPage(1); }}
        />
      }
      footer={data && totalPages > 1 ? <Pager page={data.page} totalPages={totalPages} onChange={setPage} /> : undefined}
    >
      {retryError && <p role="alert" className="m-3 rounded-xl bg-[var(--danger-soft)] px-3 py-2 text-xs font-medium text-[var(--danger-strong)]">{retryError}</p>}
      {isLoading && <div className="space-y-2 p-4">{Array.from({ length: 5 }, (_, index) => <div key={index} className="skeleton h-10 rounded-lg" />)}</div>}
      {!isLoading && jobs?.length === 0 && <EmptyState icon="bell" title="Bu filtrede gönderim yok." />}

      {/* Telefonda 8 sütunluk tablo yana kayıyordu: aynı bilgi satır kartı olarak listelenir. */}
      {!isLoading && !!jobs?.length && (
        <ul className="divide-y divide-[var(--line)] md:hidden">
          {jobs.map((job) => (
            <li key={job.id} className="space-y-0.5 px-4 py-2.5 text-sm">
              <div className="flex items-baseline justify-between gap-3">
                <span className="min-w-0 truncate font-semibold">{TYPE_LABELS[job.type] ?? job.type}</span>
                <span className={`shrink-0 text-xs font-bold ${STATUS_COLORS[job.status]}`}>{STATUS_LABELS[job.status]}</span>
              </div>
              <p className="text-meta truncate">{[job.studentName, job.guardianName ?? job.recipientPhoneNumber].filter(Boolean).join(" · ")}</p>
              <p className="text-meta">{job.lessonType ?? (job.referenceType === "receivable" ? "Aidat" : "—")} · {new Date(job.scheduledAt).toLocaleString("tr-TR", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}</p>
              {job.lastError && <details className="group text-meta"><summary className="line-clamp-2 cursor-pointer break-words group-open:line-clamp-none">{job.lastError}</summary></details>}
              {job.status === "Failed" && <button type="button" onClick={() => handleRetry(job.id)} disabled={retry.isPending} className="btn btn-quiet mt-1 px-2.5 text-xs text-[var(--brand)]">Yeniden dene</button>}
            </li>
          ))}
        </ul>
      )}
      {!isLoading && !!jobs?.length && (
        <div className="hidden overflow-x-auto md:block">
          <table className="w-full min-w-[60rem] text-sm">
            <thead>
              <tr className="text-micro border-b border-[var(--line)] text-left text-[var(--muted)]">
                <th className="px-4 py-2">Mesaj tipi</th>
                <th className="px-3 py-2">Ders türü</th>
                <th className="px-3 py-2">Veli</th>
                <th className="px-3 py-2">Öğrenci</th>
                <th className="px-3 py-2">Planlanan</th>
                <th className="px-3 py-2">Durum</th>
                <th className="px-3 py-2">Hata</th>
                <th className="sticky right-0 bg-[var(--surface)] px-3 py-2 shadow-[-1px_0_0_var(--line)]"><span className="sr-only">İşlem</span></th>
              </tr>
            </thead>
            <tbody>
              {jobs.map((job) => (
                <tr key={job.id} className="border-b border-[var(--line)] last:border-0 hover:bg-[var(--surface-muted)]/50">
                  <td className="px-4 py-2 font-semibold whitespace-nowrap">{TYPE_LABELS[job.type] ?? job.type}</td>
                  <td className="px-3 py-2">{job.lessonType ?? (job.referenceType === "receivable" ? "Aidat" : "—")}</td>
                  <td className="px-3 py-2">{job.guardianName ?? job.recipientPhoneNumber}</td>
                  <td className="px-3 py-2">{job.studentName ?? "—"}</td>
                  <td className="text-meta px-3 py-2 whitespace-nowrap">{new Date(job.scheduledAt).toLocaleString("tr-TR", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}</td>
                  <td className={`px-3 py-2 font-bold whitespace-nowrap ${STATUS_COLORS[job.status]}`}>{STATUS_LABELS[job.status]}</td>
                  <td className="text-meta max-w-xs px-3 py-2">
                    {job.lastError
                      ? <details className="group"><summary className="line-clamp-1 cursor-pointer break-words group-open:line-clamp-none">{job.lastError}</summary></details>
                      : "—"}
                  </td>
                  <td className="sticky right-0 bg-[var(--surface)] px-3 py-2 shadow-[-1px_0_0_var(--line)]">{job.status === "Failed" && <button type="button" onClick={() => handleRetry(job.id)} disabled={retry.isPending} className="btn btn-quiet min-h-8 pointer-coarse:min-h-11 px-2.5 text-xs text-[var(--brand)]">Yeniden dene</button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}

const WEBHOOK_STATUS_LABELS: Record<WebhookEventStatus, string> = {
  Received: "alındı",
  Processed: "işlendi",
  Failed: "başarısız",
};

const WEBHOOK_STATUS_COLORS: Record<WebhookEventStatus, string> = {
  Received: "text-[var(--warning)]",
  Processed: "text-[var(--success-strong)]",
  Failed: "text-[var(--danger)]",
};

const DELIVERY_LABELS: Record<string, string> = {
  sent: "gönderildi",
  delivered: "teslim edildi",
  read: "okundu",
  failed: "teslim edilemedi",
};

// Veli yazdığında cevabın neden gitmediği (veli bulunamadı, onay kapalı, gönderim hatası) ve
// Meta'nın giden mesaj için bildirdiği teslim durumu burada görünür.
function InboundPanel() {
  const [filter, setFilter] = useState<WebhookEventStatus | "all">("all");
  const [page, setPage] = useState(1);
  const { data, isLoading } = useWebhookEvents(filter === "all" ? undefined : filter, page, 50);
  const events = data?.items;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <Panel
      flush
      title="Gelen WhatsApp olayları"
      meta={data ? `${data.totalCount} kayıt` : undefined}
      actions={
        <Segmented
          label="Duruma göre filtrele"
          options={(["all", "Processed", "Failed"] as const).map((status) => ({ value: status, label: status === "all" ? "Tümü" : WEBHOOK_STATUS_LABELS[status] }))}
          value={filter}
          onChange={(status) => { setFilter(status); setPage(1); }}
        />
      }
      footer={data && totalPages > 1 ? <Pager page={data.page} totalPages={totalPages} onChange={setPage} /> : undefined}
    >
      {isLoading && <div className="space-y-2 p-4">{Array.from({ length: 5 }, (_, index) => <div key={index} className="skeleton h-10 rounded-lg" />)}</div>}
      {!isLoading && events?.length === 0 && <EmptyState icon="bell" title="Bu filtrede olay yok." />}

      {!isLoading && !!events?.length && (
        <ul className="divide-y divide-[var(--line)] md:hidden">
          {events.map((event) => {
            const isDelivery = !!event.deliveryStatus;
            const error = event.processingError ?? event.deliveryError;
            return (
              <li key={event.id} className="space-y-0.5 px-4 py-2.5 text-sm">
                <div className="flex items-baseline justify-between gap-3">
                  <span className="min-w-0 truncate font-semibold">{event.guardianName ?? event.fromPhoneNumber ?? (isDelivery ? "Teslim bilgisi" : event.eventType)}</span>
                  <span className={`shrink-0 text-xs font-bold ${event.deliveryStatus === "failed" ? "text-[var(--danger)]" : WEBHOOK_STATUS_COLORS[event.status]}`}>
                    {event.deliveryStatus === "failed" ? "teslim edilemedi" : WEBHOOK_STATUS_LABELS[event.status]}
                  </span>
                </div>
                <p className="text-meta">{new Date(event.receivedAt).toLocaleString("tr-TR", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })} · {isDelivery ? "Teslim bilgisi" : event.fromPhoneNumber ? "Gelen mesaj" : event.eventType}</p>
                <p className="break-words">{event.text || (isDelivery ? DELIVERY_LABELS[event.deliveryStatus!] ?? event.deliveryStatus : "—")}</p>
                {error && <details className="group text-meta"><summary className="line-clamp-2 cursor-pointer break-words group-open:line-clamp-none">{error}</summary></details>}
              </li>
            );
          })}
        </ul>
      )}
      {!isLoading && !!events?.length && (
        <div className="hidden overflow-x-auto md:block">
          <table className="w-full min-w-[56rem] text-sm">
            <thead>
              <tr className="text-micro border-b border-[var(--line)] text-left text-[var(--muted)]">
                <th className="px-4 py-2">Zaman</th>
                <th className="px-3 py-2">Olay</th>
                <th className="px-3 py-2">Gönderen</th>
                <th className="px-3 py-2">Mesaj</th>
                <th className="px-3 py-2">Durum</th>
                <th className="px-3 py-2">Hata</th>
              </tr>
            </thead>
            <tbody>
              {events.map((event) => {
                const isDelivery = !!event.deliveryStatus;
                const error = event.processingError ?? event.deliveryError;
                return (
                  <tr key={event.id} className="border-b border-[var(--line)] last:border-0 hover:bg-[var(--surface-muted)]/50">
                    <td className="text-meta px-4 py-2 whitespace-nowrap">{new Date(event.receivedAt).toLocaleString("tr-TR", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", second: "2-digit" })}</td>
                    <td className="px-3 py-2 whitespace-nowrap">{isDelivery ? "Teslim bilgisi" : event.fromPhoneNumber ? "Gelen mesaj" : event.eventType}</td>
                    <td className="px-3 py-2">{event.guardianName ?? event.fromPhoneNumber ?? "—"}</td>
                    <td className="max-w-xs px-3 py-2 break-words">{event.text || (isDelivery ? DELIVERY_LABELS[event.deliveryStatus!] ?? event.deliveryStatus : "—")}</td>
                    <td className={`px-3 py-2 font-bold whitespace-nowrap ${event.deliveryStatus === "failed" ? "text-[var(--danger)]" : WEBHOOK_STATUS_COLORS[event.status]}`}>
                      {event.deliveryStatus === "failed" ? "teslim edilemedi" : WEBHOOK_STATUS_LABELS[event.status]}
                    </td>
                    <td className="text-meta max-w-xs px-3 py-2">
                      {error
                        ? <details className="group"><summary className="line-clamp-1 cursor-pointer break-words group-open:line-clamp-none">{error}</summary></details>
                        : "—"}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}

function AutomationSettings() {
  const { data: settings, isLoading } = useAutomationSettings();
  const update = useUpdateAutomationSettings();

  const [enabled, setEnabled] = useState(true);
  const [minutes, setMinutes] = useState("60");
  const [allowLate, setAllowLate] = useState(true);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loadedOnce, setLoadedOnce] = useState(false);

  // Sunucudan gelen değerler yerel state'e yalnızca bir kez (ilk yüklemede) yansıtılır -
  // sonrasında admin form üzerinde düzenlerken sunucu yeniden fetch olursa üzerine yazmasın.
  if (settings && !loadedOnce) {
    setEnabled(settings.isEnabled);
    setMinutes(String(settings.lessonReminderMinutesBefore));
    setAllowLate(settings.allowAttendingLateResponse);
    setLoadedOnce(true);
  }

  async function save() {
    setError(null);
    setSaved(false);
    try {
      await update.mutateAsync({ lessonReminderMinutesBefore: Number(minutes), isEnabled: enabled, allowAttendingLateResponse: allowLate });
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Otomasyon ayarı kaydedilemedi.");
    }
  }

  return (
    <section className="app-card space-y-3 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3"><div><h2 className="text-title">Otomatik gönderim ayarları</h2><p className="text-meta mt-1">Ders saatinden önce veliye hangi mesajın ne zaman gideceğini belirle.</p></div><label className="flex min-h-11 items-center gap-2 text-xs font-semibold text-[var(--muted)]"><input type="checkbox" className="h-5 w-5 shrink-0 accent-[var(--brand)]" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} disabled={isLoading} /> Otomatik gönder</label></div>
      <div className="grid gap-3 lg:grid-cols-[12rem_1fr_auto] lg:items-end">
        <label className="space-y-1.5 text-xs font-semibold text-[var(--muted)]">Dersten ne kadar önce?<select value={minutes} onChange={(event) => setMinutes(event.target.value)} disabled={isLoading} className="field text-sm"><option value="15">15 dakika önce</option><option value="30">30 dakika önce</option><option value="45">45 dakika önce</option><option value="60">60 dakika önce</option></select></label>
        <div className="space-y-2">
          <p className="text-xs font-semibold text-[var(--muted)]">Çoktan seçmeli cevaplar</p>
          <div className="flex flex-wrap gap-2">
            <span className="inline-flex items-center gap-2 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] px-3 py-2 text-xs text-[var(--muted)]">Geliyorum</span>
            <label className="inline-flex min-h-11 items-center gap-2 rounded-xl border border-[var(--line)] bg-white px-3 py-2 text-xs"><input type="checkbox" className="h-5 w-5 shrink-0 accent-[var(--brand)]" checked={allowLate} onChange={(event) => setAllowLate(event.target.checked)} disabled={isLoading} /> Geç kalacağım</label>
            <span className="inline-flex items-center gap-2 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] px-3 py-2 text-xs text-[var(--muted)]">Gelemiyorum</span>
          </div>
        </div>
        <button type="button" onClick={save} disabled={isLoading || update.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white disabled:cursor-not-allowed disabled:opacity-50">{update.isPending ? "Kaydediliyor…" : "Ayarları kaydet"}</button>
      </div>
      {error && <p role="alert" className="text-sm font-semibold text-[var(--danger-strong)]">{error}</p>}
      {saved && <p role="status" className="text-sm font-semibold text-[var(--success-strong)]">Otomasyon ayarı kaydedildi. {enabled ? "Bekleyen ders hatırlatmaları buna göre güncellendi." : "Bekleyen ders hatırlatmaları iptal edildi."}</p>}
    </section>
  );
}
