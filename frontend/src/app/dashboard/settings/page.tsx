"use client";

import { useState, useSyncExternalStore, type FormEvent } from "react";
import { AddButton, FormActions, FormMessage, Modal, PageHeader, SectionHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { applyFontSizePreference, FONT_SIZE_CHANGE_EVENT, readFontSizePreference, type FontSizePreference } from "@/lib/font-size";
import { useInstruments, useInstrumentMaintenanceSettings, useRunDueMaintenanceReminders, useSaveInstrumentMaintenanceSetting } from "@/lib/people";
import { useMe } from "@/lib/use-auth";
import { ChangePasswordForm } from "../change-password-form";

const FONT_SIZE_OPTIONS: Array<{ value: FontSizePreference; label: string; detail: string }> = [
  { value: "small", label: "Küçük", detail: "Daha kompakt" },
  { value: "standard", label: "Standart", detail: "Önerilen" },
  { value: "large", label: "Büyük", detail: "Daha rahat okuma" },
];

export default function SettingsPage() {
  const [saved, setSaved] = useState(false);
  const fontSize = useSyncExternalStore(
    (onStoreChange) => {
      window.addEventListener(FONT_SIZE_CHANGE_EVENT, onStoreChange);
      return () => window.removeEventListener(FONT_SIZE_CHANGE_EVENT, onStoreChange);
    },
    readFontSizePreference,
    () => "standard" as FontSizePreference,
  );
  const { data: me, refetch } = useMe();
  const passwordChangeRequired = me?.mustChangePassword ?? false;

  function changeFontSize(value: FontSizePreference) {
    applyFontSizePreference(value);
    setSaved(false);
  }

  return (
    <div className="mx-auto max-w-4xl space-y-4">
      <PageHeader title="Ayarlar" description="Hesap güvenliği, görünüm ve bildirim tercihleri." />

      <section className="app-card overflow-hidden">
        <div className="border-b border-[var(--line)] p-4 sm:p-5">
          <SectionHeader title="Şifre değiştir" description="Mevcut şifreni ve en az 8 karakterli yeni şifreni gir." />
        </div>
        <div className="p-4 sm:p-5">
          {passwordChangeRequired && (
            <p className="mb-4 rounded-xl bg-[var(--warning-soft)] px-3 py-2.5 text-sm font-semibold text-[var(--warning-strong)]">
              Güvenliğin için önce kalıcı bir şifre belirlemelisin.
            </p>
          )}
          <ChangePasswordForm onDone={() => { setSaved(true); void refetch(); }} />
          {saved && <p role="status" className="mt-4 rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-sm font-semibold text-[var(--success-strong)]">Şifren güncellendi.</p>}
        </div>
      </section>

      <section className="app-card overflow-hidden">
        <div className="border-b border-[var(--line)] p-4 sm:p-5">
          <SectionHeader title="Yazı boyutu" description="Uygulamadaki yazıları ve aralıkları birlikte ölçekler; seçim bu tarayıcıda saklanır." />
        </div>
        <div className="p-4 sm:p-5">
          <div className="grid gap-2 sm:grid-cols-3" role="group" aria-label="Yazı boyutu seçimi">
            {FONT_SIZE_OPTIONS.map((option) => (
              <button
                key={option.value}
                type="button"
                aria-pressed={fontSize === option.value}
                onClick={() => changeFontSize(option.value)}
                className={`pressable rounded-xl border-2 px-3 py-3 text-left ${fontSize === option.value ? "border-[var(--brand)] bg-[var(--brand-soft)]" : "border-[var(--line)] bg-white hover:border-[var(--brand)]/50"}`}
              >
                <span className="block text-sm font-bold">{option.label}</span>
                <span className="mt-0.5 block text-[.68rem] text-[var(--muted)]">{option.detail}</span>
              </button>
            ))}
          </div>
        </div>
      </section>

      <section className="app-card p-4 sm:p-5">
        <SectionHeader title="Mesaj Merkezi" description="Ders hatırlatmaları, WhatsApp şablonları ve gönderim tercihleri Mesaj Merkezi'nde yönetilir." />
      </section>
      {me?.role === "Admin" && <MaintenanceSettingsPanel />}
    </div>
  );
}

function MaintenanceSettingsPanel() {
  const { data: settings } = useInstrumentMaintenanceSettings();
  const runDue = useRunDueMaintenanceReminders();
  const [showForm, setShowForm] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  async function runReminders() {
    const result = await runDue.mutateAsync();
    setMessage(`${result.dueSettingCount} zamanı gelen ayar işlendi; rızası açık veliler için ${result.scheduledCount} bildirim sıraya alındı.`);
  }

  return (
    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] p-4 sm:p-5">
        <SectionHeader
          title="Enstrüman bakımı"
          description="Bakım türü, dönem ve kanal enstrüman bazında yönetilir; WhatsApp kuyruğuna yalnız rızası açık veliler eklenir."
          actions={
            <>
              <button type="button" onClick={() => void runReminders()} disabled={runDue.isPending} className="btn btn-quiet">Zamanı gelenleri sırala</button>
              <AddButton label="Bakım ayarı ekle" onClick={() => setShowForm(true)} />
            </>
          }
        />
        {message && <p role="status" className="text-meta mt-3">{message}</p>}
      </div>

      <div className="divide-y divide-[var(--line)]">
        {settings?.map((setting) => (
          <article key={setting.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
            <div className="min-w-0">
              <p className="text-sm font-bold">{setting.instrumentName} · {setting.maintenanceType}</p>
              <p className="text-meta mt-0.5">
                Her {setting.periodDays} gün · {setting.notificationPreference === "WhatsApp" ? "WhatsApp" : "Bildirim yok"} · {setting.consentingGuardianCount} rızası açık veli · sonraki {new Date(setting.nextReminderAt).toLocaleDateString("tr-TR")}
              </p>
            </div>
            <span className={`shrink-0 rounded-full px-2 py-1 text-[.62rem] font-bold ${setting.isEnabled ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{setting.isEnabled ? "Etkin" : "Kapalı"}</span>
          </article>
        ))}
        {!settings?.length && <p className="text-meta px-4 py-6 text-center">Henüz bakım ayarı yok.</p>}
      </div>

      <Modal open={showForm} title="Bakım ayarı ekle" onClose={() => setShowForm(false)} size="sm">
        <MaintenanceSettingForm onClose={() => setShowForm(false)} />
      </Modal>
    </section>
  );
}

function MaintenanceSettingForm({ onClose }: { onClose: () => void }) {
  const { data: instruments } = useInstruments();
  const save = useSaveInstrumentMaintenanceSetting();
  const [instrumentId, setInstrumentId] = useState("");
  const [maintenanceType, setMaintenanceType] = useState("");
  const [periodDays, setPeriodDays] = useState("180");
  const [nextReminderAt, setNextReminderAt] = useState(() => new Date().toISOString().slice(0, 16));
  const [enabled, setEnabled] = useState(true);
  const [preference, setPreference] = useState<"None" | "WhatsApp">("WhatsApp");
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await save.mutateAsync({
        instrumentId: instrumentId || instruments?.[0]?.id || "",
        maintenanceType,
        periodDays: Number(periodDays),
        isEnabled: enabled,
        notificationPreference: preference,
        nextReminderAt: new Date(nextReminderAt).toISOString(),
      });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Bakım ayarı kaydedilemedi.");
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Enstrüman
          <select value={instrumentId || instruments?.[0]?.id || ""} onChange={(event) => setInstrumentId(event.target.value)} className="field text-sm">
            {instruments?.map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}
          </select>
        </label>
        <label className="form-label">Bakım türü
          <input value={maintenanceType} onChange={(event) => setMaintenanceType(event.target.value)} required maxLength={200} placeholder="Örn. piyano akordu" className="field text-sm" />
        </label>
        <label className="form-label">Dönem (gün)
          <input type="number" min="1" max="3650" value={periodDays} onChange={(event) => setPeriodDays(event.target.value)} required className="field text-sm" />
        </label>
        <label className="form-label">Sonraki tarih
          <input type="datetime-local" value={nextReminderAt} onChange={(event) => setNextReminderAt(event.target.value)} required className="field text-sm" />
        </label>
        <label className="form-label">Bildirim
          <select value={preference} onChange={(event) => setPreference(event.target.value as "None" | "WhatsApp")} className="field text-sm">
            <option value="WhatsApp">WhatsApp</option>
            <option value="None">Bildirim yok</option>
          </select>
        </label>
        <label className="flex items-center gap-2 self-end pb-2.5 text-xs font-semibold">
          <input type="checkbox" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} /> Etkin
        </label>
      </div>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Ayarı kaydet" pending={save.isPending} disabled={!instruments?.length} />
    </form>
  );
}
