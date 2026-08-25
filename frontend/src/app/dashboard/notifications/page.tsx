"use client";

import { useMemo, useRef, useState } from "react";
import { ApiError } from "@/lib/api";
import {
  useAutomationSettings,
  useMessageTemplates,
  useNotifications,
  useRetryNotification,
  useUpdateAutomationSettings,
  useUpdateMessageTemplate,
  type MessageTemplate,
  type NotificationJobStatus,
  type NotificationJobType,
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
];

const AUTOMATIC_PREVIEW_VALUES: Record<string, string> = {
  guardian_name: "Ayşe Hanım",
  student_name: "Deniz Kaya",
  instrument: "Piyano",
  lesson_time: "23 Ağustos 2026 13:00",
  teacher_name: "Can Öğretmen",
  due_date: "1 Eylül 2026",
  amount: "2.400 TL",
  period: "Eylül 2026",
  currency: "TRY",
};

const CUSTOM_PREVIEW_DEFAULTS: Record<string, string> = {
  new_lesson_time: "28 Ağustos 2026 17:30",
};

const PLACEHOLDER_LABELS = Object.fromEntries(PLACEHOLDERS.map((placeholder) => [placeholder.key, placeholder.label]));

function placeholdersIn(body: string) {
  return Array.from(new Set(Array.from(body.matchAll(/{{\s*([^}]+)\s*}}/g), (match) => match[1]!.trim())));
}

const TEMPLATE_LABELS: Record<string, string> = {
  lesson_reminder_rsvp: "Ders hatırlatması ve katılım yanıtı",
  lesson_rescheduled: "Ders saati değişikliği",
  makeup_approved: "Telafi dersi onayı",
  payment_reminder: "Aidat ödeme hatırlatması",
};

function templateLabel(name: string) {
  return TEMPLATE_LABELS[name] ?? name.replaceAll("_", " ");
}

export default function NotificationsPage() {
  const [activeTab, setActiveTab] = useState<"activity" | "templates">("activity");

  return (
    <div className="space-y-5">
      <div>
        <p className="text-micro text-[var(--brand-strong)]">WhatsApp ve otomasyon</p>
        <h1 className="text-display mt-1 font-serif italic">Mesaj Merkezi</h1>
        <p className="text-meta mt-2 max-w-3xl">Ders türü, veli ve öğrenci bilgilerini görünür tut; hazır mesajlarını düzenle, önizle ve zamanlanmış gönderimleri buradan takip et.</p>
      </div>

      <div className="flex flex-wrap gap-2 border-b border-[var(--line)] pb-1">
        <button type="button" onClick={() => setActiveTab("activity")} className={`pressable min-h-11 rounded-t-xl px-4 text-sm font-bold ${activeTab === "activity" ? "border-b-2 border-[var(--brand)] text-[var(--brand-strong)]" : "text-[var(--muted)] hover:bg-[var(--surface-muted)]"}`}>
          Gönderim kayıtları
        </button>
        <button type="button" onClick={() => setActiveTab("templates")} className={`pressable min-h-11 rounded-t-xl px-4 text-sm font-bold ${activeTab === "templates" ? "border-b-2 border-[var(--brand)] text-[var(--brand-strong)]" : "text-[var(--muted)] hover:bg-[var(--surface-muted)]"}`}>
          Şablonlar ve otomasyon
        </button>
      </div>

      {activeTab === "activity" ? <ActivityPanel /> : <TemplatesPanel />}
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
    <section className="space-y-4">
      <div className="flex flex-wrap gap-2">
        {(["all", "Pending", "Sent", "Failed", "Cancelled"] as const).map((status) => (
          <button key={status} type="button" onClick={() => { setFilter(status); setPage(1); }} className={`pressable min-h-10 rounded-full px-3.5 text-xs font-bold ${filter === status ? "bg-[var(--brand)] text-white" : "border border-[var(--line)] bg-white text-[var(--muted)] hover:border-[#e0c39d]"}`}>
            {status === "all" ? "Tümü" : STATUS_LABELS[status]}
          </button>
        ))}
      </div>

      {retryError && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{retryError}</p>}
      {isLoading && <div className="space-y-2">{Array.from({ length: 5 }, (_, index) => <div key={index} className="skeleton h-14 rounded-xl" />)}</div>}

      <div className="app-card overflow-x-auto">
        <table className="w-full min-w-[68rem] text-sm">
          <thead>
            <tr className="text-micro border-b border-[var(--line)] text-left">
              <th className="px-3 py-3">Mesaj tipi</th>
              <th className="px-3 py-3">Ders türü</th>
              <th className="px-3 py-3">Veli</th>
              <th className="px-3 py-3">Öğrenci</th>
              <th className="px-3 py-3">Planlanan zaman</th>
              <th className="px-3 py-3">Durum</th>
              <th className="px-3 py-3">Hata</th>
              <th className="px-3 py-3" />
            </tr>
          </thead>
          <tbody>
            {jobs?.map((job) => (
              <tr key={job.id} className="border-b border-[var(--line)] last:border-0">
                <td className="px-3 py-3 font-semibold">{TYPE_LABELS[job.type] ?? job.type}</td>
                <td className="px-3 py-3">{job.lessonType ?? (job.referenceType === "receivable" ? "Aidat" : "—")}</td>
                <td className="px-3 py-3">{job.guardianName ?? job.recipientPhoneNumber}</td>
                <td className="px-3 py-3">{job.studentName ?? "—"}</td>
                <td className="text-meta px-3 py-3">{new Date(job.scheduledAt).toLocaleString("tr-TR")}</td>
                <td className={`px-3 py-3 font-bold ${STATUS_COLORS[job.status]}`}>{STATUS_LABELS[job.status]}</td>
                <td className="text-meta max-w-xs truncate px-3 py-3" title={job.lastError ?? undefined}>{job.lastError ?? "—"}</td>
                <td className="px-3 py-3">{job.status === "Failed" && <button type="button" onClick={() => handleRetry(job.id)} disabled={retry.isPending} className="pressable min-h-11 rounded-lg border border-[var(--line)] bg-white px-2.5 text-xs font-bold text-[var(--brand)] disabled:opacity-50">Yeniden dene</button>}</td>
              </tr>
            ))}
            {jobs?.length === 0 && !isLoading && <tr><td colSpan={8} className="px-3 py-8 text-center text-sm text-[var(--muted)]">Bu filtrede gönderim yok.</td></tr>}
          </tbody>
        </table>
      </div>

      {data && data.totalCount > 0 && (
        <div className="flex items-center justify-between text-sm">
          <span className="text-meta">Toplam {data.totalCount} kayıt · sayfa {data.page} / {totalPages}</span>
          <div className="flex gap-2">
            <button type="button" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page <= 1} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold disabled:opacity-50">Önceki</button>
            <button type="button" onClick={() => setPage((current) => Math.min(totalPages, current + 1))} disabled={page >= totalPages} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold disabled:opacity-50">Sonraki</button>
          </div>
        </div>
      )}
    </section>
  );
}

function TemplatesPanel() {
  const { data: templates, isLoading } = useMessageTemplates();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = templates?.find((template) => template.id === selectedId) ?? templates?.[0];

  return (
    <section className="space-y-5">
      <div className="grid gap-4 xl:grid-cols-[15rem_minmax(0,1fr)]">
        <div className="app-card h-fit p-2">
          <p className="text-micro px-3 py-2 text-[var(--muted)]">Hazır şablonlar</p>
          {isLoading && <div className="space-y-2 p-2">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-12 rounded-xl" />)}</div>}
          {templates?.map((template) => <button key={template.id} type="button" onClick={() => setSelectedId(template.id)} className={`pressable flex min-h-12 w-full items-center justify-between gap-2 rounded-xl px-3 text-left text-sm font-semibold ${selected?.id === template.id ? "bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "hover:bg-[var(--surface-muted)]"}`}><span className="truncate">{templateLabel(template.name)}</span><span className={`h-2 w-2 shrink-0 rounded-full ${template.isActive ? "bg-[var(--success)]" : "bg-[var(--muted)]"}`} /></button>)}
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
  const [customValues, setCustomValues] = useState<Record<string, string>>(() => Object.fromEntries(
    placeholdersIn(template.body)
      .filter((key) => !(key in AUTOMATIC_PREVIEW_VALUES))
      .map((key) => [key, CUSTOM_PREVIEW_DEFAULTS[key] ?? ""]),
  ));
  const update = useUpdateMessageTemplate();
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const customPlaceholders = useMemo(() => placeholdersIn(body).filter((key) => !(key in AUTOMATIC_PREVIEW_VALUES)), [body]);
  const preview = useMemo(() => body.replace(/{{\s*([^}]+)\s*}}/g, (_match, rawKey: string) => {
    const key = rawKey.trim();
    return AUTOMATIC_PREVIEW_VALUES[key] ?? customValues[key] ?? `[${PLACEHOLDER_LABELS[key] ?? key} girilmedi]`;
  }), [body, customValues]);

  function insertPlaceholder(key: string, position?: number | null) {
    const textarea = textareaRef.current;
    const token = `{{${key}}}`;
    const start = position ?? textarea?.selectionStart ?? body.length;
    const end = textarea?.selectionEnd ?? start;
    const nextBody = `${body.slice(0, start)}${token}${body.slice(end)}`;
    setBody(nextBody);
    window.requestAnimationFrame(() => { textarea?.focus(); textarea?.setSelectionRange(start + token.length, start + token.length); });
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
    <form onSubmit={saveTemplate} className="grid gap-4 lg:grid-cols-2">
      <div className="app-card space-y-4 p-4 sm:p-5">
        <div className="flex items-start justify-between gap-3"><div><p className="text-micro text-[var(--brand-strong)]">Düzenleyici</p><h2 className="mt-1 text-title">{templateLabel(name)}</h2></div><label className="flex items-center gap-2 text-xs font-semibold text-[var(--muted)]"><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /> Aktif</label></div>
        <div className="space-y-2"><p className="text-xs font-semibold text-[var(--muted)]">Mesaja bilgi alanı ekle</p><div className="flex flex-wrap gap-1.5">{PLACEHOLDERS.map((placeholder) => <button key={placeholder.key} type="button" draggable onDragStart={(event) => event.dataTransfer.setData("text/plain", placeholder.key)} onClick={() => insertPlaceholder(placeholder.key)} className="pressable min-h-8 rounded-full border border-[var(--line)] bg-white px-2.5 text-[.68rem] font-bold text-[var(--brand)] hover:border-[var(--brand)]">{placeholder.label}</button>)}</div></div>
        {customPlaceholders.length > 0 && <section className="rounded-xl border border-[var(--brand)]/25 bg-[var(--brand-soft)]/45 p-3"><div><p className="text-xs font-bold">Özel değerler</p><p className="mt-0.5 text-[.64rem] leading-relaxed text-[var(--muted)]">Bu alanları doldurduğunda canlı örnek anında güncellenir.</p></div><div className="mt-3 grid gap-2 sm:grid-cols-2">{customPlaceholders.map((key) => <label key={key} className="space-y-1 text-[.66rem] font-bold text-[var(--muted)]">{PLACEHOLDER_LABELS[key] ?? key.replaceAll("_", " ")}<input value={customValues[key] ?? ""} onChange={(event) => setCustomValues((current) => ({ ...current, [key]: event.target.value }))} placeholder="Değeri gir" className="field min-h-10 bg-white text-xs" /></label>)}</div></section>}
        <label className="space-y-1.5 text-xs font-semibold text-[var(--muted)]">Mesaj gövdesi<textarea ref={textareaRef} value={body} onChange={(event) => setBody(event.target.value)} onDragOver={(event) => event.preventDefault()} onDrop={(event) => { event.preventDefault(); insertPlaceholder(event.dataTransfer.getData("text/plain"), textareaRef.current?.selectionStart); }} rows={13} className="field resize-y font-mono text-sm leading-relaxed" /></label>
        {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
        {saved && <p role="status" className="rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-xs font-medium text-[var(--success-strong)]">Şablon kaydedildi.</p>}
        <button type="submit" disabled={update.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white disabled:opacity-50">{update.isPending ? "Kaydediliyor…" : "Şablonu kaydet"}</button>
      </div>
      <div className="app-card h-fit overflow-hidden p-4 sm:p-5"><div className="mb-4 flex items-center justify-between"><div><p className="text-micro text-[var(--brand-strong)]">Canlı örnek</p><h2 className="mt-1 text-title">Önizleme</h2></div><span className="rounded-full bg-[var(--success-soft)] px-2.5 py-1 text-[.65rem] font-bold text-[var(--success-strong)]">WhatsApp</span></div><div className="rounded-2xl bg-[#e8f5df] p-3.5 text-sm leading-relaxed text-[#2d4c28] shadow-inner"><p className="mb-2 text-[.65rem] font-bold uppercase tracking-[.08em] text-[#6a8a5f]">Abdera Müzik Okulu</p><p className="whitespace-pre-wrap">{preview || "Mesaj gövdesi burada görünecek."}</p></div><p className="text-meta mt-4">Veli ve öğrenci bilgileri gönderim anında gerçek kayıtlarla değiştirilir.</p></div>
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
    <section className="app-card space-y-4 p-4 sm:p-5">
      <div className="flex flex-wrap items-start justify-between gap-3"><div><p className="text-micro text-[var(--brand-strong)]">Ders hatırlatması</p><h2 className="mt-1 text-title">Otomatik gönderim ayarları</h2><p className="text-meta mt-1">Ders saatinden önce veliye hangi mesajın ne zaman gideceğini belirle.</p></div><label className="flex items-center gap-2 text-xs font-semibold text-[var(--muted)]"><input type="checkbox" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} disabled={isLoading} /> Otomatik gönder</label></div>
      <div className="grid gap-4 lg:grid-cols-[12rem_1fr_auto] lg:items-end">
        <label className="space-y-1.5 text-xs font-semibold text-[var(--muted)]">Dersden ne kadar önce?<select value={minutes} onChange={(event) => setMinutes(event.target.value)} disabled={isLoading} className="field text-sm"><option value="15">15 dakika önce</option><option value="30">30 dakika önce</option><option value="45">45 dakika önce</option><option value="60">60 dakika önce</option></select></label>
        <div className="space-y-2">
          <p className="text-xs font-semibold text-[var(--muted)]">Çoktan seçmeli cevaplar</p>
          <div className="flex flex-wrap gap-2">
            <span className="inline-flex items-center gap-2 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] px-3 py-2 text-xs text-[var(--muted)]">Evet, katılıyorum.</span>
            <label className="inline-flex items-center gap-2 rounded-xl border border-[var(--line)] bg-white px-3 py-2 text-xs"><input type="checkbox" checked={allowLate} onChange={(event) => setAllowLate(event.target.checked)} disabled={isLoading} /> Evet ama biraz gecikeceğim</label>
            <span className="inline-flex items-center gap-2 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] px-3 py-2 text-xs text-[var(--muted)]">Hayır, katılamıyorum.</span>
          </div>
        </div>
        <button type="button" onClick={save} disabled={isLoading || update.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white disabled:cursor-not-allowed disabled:opacity-50">{update.isPending ? "Kaydediliyor…" : "Ayarları kaydet"}</button>
      </div>
      {error && <p role="alert" className="text-sm font-semibold text-[var(--danger-strong)]">{error}</p>}
      {saved && <p role="status" className="text-sm font-semibold text-[var(--success-strong)]">Otomasyon ayarı kaydedildi. {enabled ? "Bekleyen ders hatırlatmaları buna göre güncellendi." : "Bekleyen ders hatırlatmaları iptal edildi."}</p>}
    </section>
  );
}
