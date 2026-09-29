"use client";

import { useMemo, useRef, useState } from "react";
import { AdminGate, EmptyState, PageHeader, Pager, Panel, Segmented } from "@/components/ui";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import {
  useAutomationSettings,
  useMessageTemplates,
  useNotifications,
  useRetryNotification,
  useUpdateAutomationSettings,
  useUpdateMessageTemplate,
  useWebhookEvents,
  type MessageTemplate,
  type NotificationJobStatus,
  type NotificationJobType,
  type WebhookEventStatus,
} from "@/lib/messaging";

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

const PLACEHOLDERS = [
  { key: "guardian_name", label: "Veli adı" },
  { key: "student_name", label: "Öğrenci adı" },
  { key: "instrument", label: "Ders türü" },
  { key: "lesson_time", label: "Ders saati" },
  { key: "new_lesson_time", label: "Yeni ders saati" },
  { key: "teacher_name", label: "Öğretmen adı" },
  { key: "due_date", label: "Son ödeme tarihi" },
  { key: "amount", label: "Tutar" },
  { key: "period", label: "Dönem" },
  { key: "currency", label: "Para birimi" },
  { key: "maintenance_type", label: "Bakım türü" },
];

const AUTOMATIC_PREVIEW_VALUES: Record<string, string> = {
  guardian_name: "Ayşe Hanım",
  student_name: "Deniz Kaya",
  instrument: "Piyano",
  lesson_time: "23 Ağustos 2026 13:00",
  new_lesson_time: "28 Ağustos 2026 17:30",
  teacher_name: "Can Öğretmen",
  due_date: "1 Eylül 2026",
  amount: "2.400 TL",
  period: "Eylül 2026",
  currency: "TRY",
  maintenance_type: "genel bakım",
};

const TEMPLATE_PLACEHOLDERS: Record<string, string[]> = {
  lesson_reminder_rsvp: ["guardian_name", "student_name", "instrument", "lesson_time", "teacher_name"],
  lesson_rescheduled: ["guardian_name", "student_name", "instrument", "new_lesson_time", "teacher_name"],
  makeup_approved: ["guardian_name", "student_name", "instrument", "lesson_time", "teacher_name"],
  payment_reminder: ["guardian_name", "student_name", "period", "amount", "currency", "due_date"],
  "instrument maintenance reminder": ["guardian_name", "instrument", "maintenance_type"],
};

const PLACEHOLDER_LABELS = Object.fromEntries(PLACEHOLDERS.map((placeholder) => [placeholder.key, placeholder.label]));

function placeholdersIn(body: string) {
  return Array.from(new Set(Array.from(body.matchAll(/{{\s*([^}]+)\s*}}/g), (match) => match[1]!.trim())));
}

function normalizeTemplateName(name: string) {
  return name.trim().toLowerCase().replaceAll(/\s+/g, " ");
}

const TEMPLATE_LABELS: Record<string, string> = {
  lesson_reminder_rsvp: "Ders hatırlatması ve katılım yanıtı",
  lesson_rescheduled: "Ders saati değişikliği",
  makeup_approved: "Telafi dersi onayı",
  payment_reminder: "Aidat ödeme hatırlatması",
  "instrument maintenance reminder": "Enstrüman bakım hatırlatması",
};

function templateLabel(name: string) {
  const normalizedName = normalizeTemplateName(name);
  if (normalizedName.includes("instrument") && normalizedName.includes("maintenance")) {
    return "Enstrüman bakım hatırlatması";
  }
  return TEMPLATE_LABELS[normalizedName] ?? normalizedName.replaceAll("_", " ");
}

export default function NotificationsPage() {
  return <AdminGate><NotificationsPageContent /></AdminGate>;
}

// Mesaj şablonları/otomasyon ayarları tamamen Admin'e özel (docs/04-permissions.md).
function NotificationsPageContent() {
  const [activeTab, setActiveTab] = useState<"activity" | "inbound" | "templates">("activity");

  return (
    <div className="space-y-3">
      <PageHeader
        title="Mesaj Merkezi"
        description="Hazır WhatsApp mesajlarını düzenle, önizle ve gönderimleri takip et."
        actions={
          <Segmented
            label="Mesaj Merkezi görünümü"
            options={[{ value: "activity", label: "Gönderim kayıtları" }, { value: "inbound", label: "Gelen mesajlar" }, { value: "templates", label: "Şablonlar ve otomasyon" }]}
            value={activeTab}
            onChange={setActiveTab}
          />
        }
      />

      {activeTab === "activity" ? <ActivityPanel /> : activeTab === "inbound" ? <InboundPanel /> : <TemplatesPanel />}
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

      {!isLoading && !!jobs?.length && (
        <div className="overflow-x-auto">
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
        <div className="overflow-x-auto">
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

function TemplatesPanel() {
  const { data: templates, isLoading } = useMessageTemplates();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = templates?.find((template) => template.id === selectedId) ?? templates?.[0];

  return (
    <section className="space-y-3">
      <div className="grid gap-3 xl:grid-cols-[15rem_minmax(0,1fr)]">
        <div className="app-card h-fit p-1.5">
          <p className="text-micro px-2.5 py-1.5 text-[var(--muted)]">Hazır şablonlar</p>
          {isLoading && <div className="space-y-2 p-2">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-12 rounded-xl" />)}</div>}
          {templates?.map((template) => <button key={template.id} type="button" onClick={() => setSelectedId(template.id)} className={`pressable flex min-h-10 w-full items-center justify-between gap-2 rounded-lg px-2.5 text-left text-sm font-semibold ${selected?.id === template.id ? "bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "hover:bg-[var(--surface-muted)]"}`}><span className="truncate">{templateLabel(template.name)}</span><span aria-hidden className={`h-2 w-2 shrink-0 rounded-full ${template.isActive ? "bg-[var(--success)]" : "bg-[var(--muted)]"}`} /><span className="sr-only">{template.isActive ? "açık" : "kapalı"}</span></button>)}
        </div>

        {selected && <TemplateEditor key={selected.id} template={selected} />}
      </div>

      <AutomationSettings />
    </section>
  );
}

function TemplateEditor({ template }: { template: MessageTemplate }) {
  // Şablon anahtarı sabit - NotificationMessageBuilder'ın switch'i buna göre eşleşiyor,
  // formda salt-okunur gösteriliyor, bu yüzden bir setter'a ihtiyaç yok.
  const name = template.name;
  const [body, setBody] = useState(template.body);
  const [isActive, setIsActive] = useState(template.isActive);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [draggingKey, setDraggingKey] = useState<string | null>(null);
  const [dropActive, setDropActive] = useState(false);
  const update = useUpdateMessageTemplate();
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const availablePlaceholders = useMemo(() => {
    const fieldsAlreadyInTemplate = placeholdersIn(template.body);
    const allowed = TEMPLATE_PLACEHOLDERS[normalizeTemplateName(name)] ?? fieldsAlreadyInTemplate;
    return PLACEHOLDERS.filter((item) => allowed.includes(item.key));
  }, [name, template.body]);
  const usedPlaceholders = useMemo(() => new Set(placeholdersIn(body)), [body]);
  const preview = useMemo(() => body.replace(/{{\s*([^}]+)\s*}}/g, (_match, rawKey: string) => {
    const key = rawKey.trim();
    return AUTOMATIC_PREVIEW_VALUES[key] ?? `[${PLACEHOLDER_LABELS[key] ?? key}]`;
  }), [body]);
  const dirty = body !== template.body || isActive !== template.isActive;

  function insertPlaceholder(key: string, position?: number | null) {
    if (!key) return;
    const textarea = textareaRef.current;
    const token = `{{${key}}}`;
    const start = position ?? textarea?.selectionStart ?? body.length;
    const end = textarea?.selectionEnd ?? start;
    const nextBody = `${body.slice(0, start)}${token}${body.slice(end)}`;
    setBody(nextBody);
    setSaved(false);
    window.requestAnimationFrame(() => { textarea?.focus(); textarea?.setSelectionRange(start + token.length, start + token.length); });
  }

  function resetTemplate() {
    setBody(template.body);
    setIsActive(template.isActive);
    setSaved(false);
    setError(null);
  }

  async function saveTemplate(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);
    try {
      await update.mutateAsync({ id: template.id, name, body, isActive });
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Şablon kaydedilemedi.");
    }
  }

  return (
    <form onSubmit={saveTemplate} className="grid items-start gap-3 xl:grid-cols-[minmax(0,1.08fr)_minmax(20rem,.92fr)]">
      <div className="app-card space-y-3 p-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div><h2 className="text-title">{templateLabel(name)}</h2><p className="text-meta mt-1">Metni düzenle; öğrenci bilgilerini aşağıdaki kartlarla yerleştir.</p></div>
          <label className="pressable inline-flex min-h-10 items-center gap-2 rounded-full border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--muted)]">
            <input type="checkbox" className="h-5 w-5 shrink-0 accent-[var(--brand)]" checked={isActive} onChange={(event) => { setIsActive(event.target.checked); setSaved(false); }} />
            {isActive ? "Gönderime açık" : "Gönderim kapalı"}
          </label>
        </div>

        <section aria-labelledby={`fields-${template.id}`} className="rounded-2xl border border-[var(--line)] bg-[var(--surface-muted)]/55 p-3">
          <div className="flex items-start gap-3">
            <span className="grid h-8 w-8 shrink-0 place-items-center rounded-xl bg-white text-[var(--brand-strong)] shadow-sm"><Icon name="plus" className="h-4 w-4" /></span>
            <div><h3 id={`fields-${template.id}`} className="text-xs font-bold">Otomatik bilgi ekle</h3><p className="text-meta mt-0.5">Kartı mesaja sürükle. Telefonda veya klavyeyle dokunman yeterli.</p></div>
          </div>
          <div className="mt-2 grid gap-1.5 sm:grid-cols-2">
            {availablePlaceholders.map((placeholder) => {
              const used = usedPlaceholders.has(placeholder.key);
              return (
                <button
                  key={placeholder.key}
                  type="button"
                  draggable
                  onDragStart={(event) => {
                    event.dataTransfer.effectAllowed = "copy";
                    event.dataTransfer.setData("text/plain", placeholder.key);
                    setDraggingKey(placeholder.key);
                  }}
                  onDragEnd={() => { setDraggingKey(null); setDropActive(false); }}
                  onClick={() => insertPlaceholder(placeholder.key)}
                  className={`pressable flex min-h-10 cursor-grab items-center gap-2 rounded-lg border bg-white px-3 text-left transition-[transform,border-color,box-shadow,opacity] duration-150 active:cursor-grabbing motion-reduce:transition-none ${draggingKey === placeholder.key ? "scale-[.98] border-[var(--brand)] opacity-60 shadow-inner" : "border-[var(--line)] hover:-translate-y-0.5 hover:border-[var(--brand)] hover:shadow-sm"}`}
                  aria-label={`${placeholder.label} alanını mesaja ekle${used ? " · mesajda kullanılıyor" : ""}`}
                >
                  <Icon name="more" className="h-4 w-4 shrink-0 rotate-90 text-[var(--muted)]" />
                  <span className="min-w-0 flex-1 text-xs font-bold">{placeholder.label}</span>
                  {used && <span className="inline-flex items-center gap-1 text-[.75rem] font-bold text-[var(--success-strong)]"><Icon name="check" className="h-3.5 w-3.5" /> Eklendi</span>}
                </button>
              );
            })}
          </div>
        </section>

        <label className="block space-y-1.5 text-xs font-semibold text-[var(--muted)]">
          Mesaj metni
          <span
            className={`relative block rounded-2xl border-2 border-dashed transition-[border-color,background-color,transform] duration-150 motion-reduce:transition-none ${dropActive ? "scale-[1.01] border-[var(--brand)] bg-[var(--brand-soft)]/55" : "border-transparent"}`}
            onDragEnter={(event) => { event.preventDefault(); setDropActive(true); }}
            onDragOver={(event) => { event.preventDefault(); event.dataTransfer.dropEffect = "copy"; setDropActive(true); }}
            onDragLeave={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDropActive(false); }}
            onDrop={(event) => {
              event.preventDefault();
              setDropActive(false);
              setDraggingKey(null);
              insertPlaceholder(event.dataTransfer.getData("text/plain"), textareaRef.current?.selectionStart ?? body.length);
            }}
          >
            <textarea ref={textareaRef} value={body} onChange={(event) => { setBody(event.target.value); setSaved(false); }} rows={10} className="field min-h-56 resize-y rounded-xl bg-white px-4 py-3 font-sans text-sm leading-relaxed" aria-describedby={`drop-help-${template.id}`} />
            {dropActive && <span className="pointer-events-none absolute inset-x-3 bottom-3 rounded-xl bg-[var(--brand)] px-3 py-2 text-center text-xs font-bold text-white shadow-lg">Bilgiyi buraya bırak</span>}
          </span>
        </label>
        <p id={`drop-help-${template.id}`} className="text-meta -mt-3">Bir kartı tıklarsan imlecin olduğu yere, sürüklersen mesaj alanına eklenir.</p>
        {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
        {saved && <p role="status" className="rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-xs font-medium text-[var(--success-strong)]">Şablon kaydedildi.</p>}
        <div className="flex flex-wrap items-center justify-between gap-2 border-t border-[var(--line)] pt-4">
          <span className="text-meta">{dirty ? "Kaydedilmemiş değişiklik var" : "Tüm değişiklikler kaydedildi"}</span>
          <div className="flex gap-2">
            {dirty && <button type="button" onClick={resetTemplate} className="btn btn-quiet">Geri al</button>}
            <button type="submit" disabled={update.isPending || !dirty} className="btn btn-primary">{update.isPending ? "Kaydediliyor…" : "Değişiklikleri kaydet"}</button>
          </div>
        </div>
      </div>
      <div className="app-card h-fit overflow-hidden p-4 sm:sticky sm:top-5">
        <div className="mb-4 flex items-center justify-between"><div><h2 className="text-title">Veli ne görecek?</h2><p className="text-meta mt-0.5">Yazdıkların anında burada görünür.</p></div><span className="rounded-full bg-[var(--success-soft)] px-2.5 py-1 text-[.75rem] font-bold text-[var(--success-strong)]">WhatsApp</span></div>
        <div className="rounded-[1.6rem] bg-[#e7e1d7] p-3 shadow-inner">
          <div className="ml-auto max-w-[95%] rounded-2xl rounded-tr-md bg-[#dcf8c6] p-4 text-sm leading-relaxed text-[#243522] shadow-sm">
            <p className="mb-2 text-[.75rem] font-bold uppercase tracking-[.06em] text-[#5f7a59]">Abdera Müzik Okulu</p>
            <p className="whitespace-pre-wrap">{preview || "Mesaj metni burada görünecek."}</p>
            <p className="mt-2 text-right text-[.75rem] text-[#6e806a]">şimdi ✓✓</p>
          </div>
          {name === "lesson_reminder_rsvp" && (
            <div className="mt-2 grid gap-1.5">
              {["✓ Geliyorum", "◷ Geç kalacağım", "× Gelemiyorum"].map((label) => <span key={label} className="rounded-xl bg-white/90 px-3 py-2.5 text-center text-xs font-bold text-[#276f62] shadow-sm">{label}</span>)}
            </div>
          )}
        </div>
        <p className="text-meta mt-4">Veli, öğrenci ve ders bilgileri gönderim anında otomatik doldurulur.</p>
      </div>
    </form>
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
