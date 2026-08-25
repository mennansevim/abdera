"use client";

import { useState, useSyncExternalStore, type FormEvent } from "react";
import { Icon } from "@/components/icons";
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
    <div className="mx-auto max-w-4xl space-y-5">
      <div>
        <p className="text-micro text-[var(--brand-strong)]">Hesap ve güvenlik</p>
        <h1 className="text-display mt-1 font-serif italic">Ayarlar</h1>
        <p className="text-meta mt-2 max-w-2xl">Hesap güvenliği, bildirim tercihleri ve uygulama davranışları burada yönetilir.</p>
      </div>

      <section className="app-card overflow-hidden">
        <div className="flex items-start gap-3 border-b border-[var(--line)] p-5 sm:p-6">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name="settings" className="h-5 w-5" /></span>
          <div>
            <h2 className="text-title">Şifre değiştir</h2>
            <p className="text-meta mt-1">Kalıcı şifreni güncellemek için mevcut şifreni ve en az 8 karakterli yeni şifreni gir.</p>
          </div>
        </div>
        <div className="p-5 sm:p-6">
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
        <div className="flex items-start gap-3 border-b border-[var(--line)] p-5 sm:p-6">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-[var(--surface-muted)] text-[var(--brand-strong)]" aria-hidden="true">
            <span className="text-lg font-bold">Aa</span>
          </span>
          <div>
            <h2 className="text-title">Yazı boyutu</h2>
            <p className="text-meta mt-1">Responsive yerleşim korunur; uygulamadaki yazıları ve rem tabanlı aralıkları birlikte ayarlar.</p>
          </div>
        </div>
        <div className="p-5 sm:p-6">
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
          <p className="mt-3 text-[.68rem] text-[var(--muted)]" role="status">Seçimin bu tarayıcıda otomatik olarak kaydedilir.</p>
        </div>
      </section>

      <section className="app-card grid gap-4 p-5 sm:grid-cols-[auto_1fr] sm:p-6">
        <span className="grid h-11 w-11 place-items-center rounded-2xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="bell" className="h-5 w-5" /></span>
        <div>
          <h2 className="text-title">Mesaj Merkezi</h2>
          <p className="text-meta mt-1">Ders hatırlatmaları, hazır WhatsApp şablonları ve gönderim tercihleri için Mesaj Merkezi’ni kullan.</p>
        </div>
      </section>
      {me?.role === "Admin" && <MaintenanceSettingsPanel />}
    </div>
  );
}

function MaintenanceSettingsPanel() {
  const { data: instruments } = useInstruments();
  const { data: settings } = useInstrumentMaintenanceSettings();
  const save = useSaveInstrumentMaintenanceSetting();
  const runDue = useRunDueMaintenanceReminders();
  const [instrumentId, setInstrumentId] = useState("");
  const [maintenanceType, setMaintenanceType] = useState("");
  const [periodDays, setPeriodDays] = useState("180");
  const [nextReminderAt, setNextReminderAt] = useState(() => new Date().toISOString().slice(0, 16));
  const [enabled, setEnabled] = useState(true);
  const [preference, setPreference] = useState<"None" | "WhatsApp">("WhatsApp");
  const [message, setMessage] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    try {
      await save.mutateAsync({ instrumentId: instrumentId || instruments?.[0]?.id || "", maintenanceType, periodDays: Number(periodDays), isEnabled: enabled, notificationPreference: preference, nextReminderAt: new Date(nextReminderAt).toISOString() });
      setMessage("Bakım ayarı kaydedildi.");
    } catch (error) {
      setMessage(error instanceof ApiError ? error.detail ?? error.title : "Bakım ayarı kaydedilemedi.");
    }
  }

  async function runReminders() {
    const result = await runDue.mutateAsync();
    setMessage(`${result.dueSettingCount} zamanı gelen ayar işlendi; rızası açık veliler için ${result.scheduledCount} bildirim sıraya alındı.`);
  }

  return <section className="app-card overflow-hidden"><div className="flex flex-wrap items-start justify-between gap-3 border-b border-[var(--line)] p-5 sm:p-6"><div><h2 className="text-title">Enstrüman bakım hatırlatmaları</h2><p className="text-meta mt-1">Bakım türü, dönem ve kanal enstrüman bazında yönetilir. WhatsApp kuyruğuna yalnız rızası açık veliler eklenir.</p></div><button type="button" onClick={() => void runReminders()} disabled={runDue.isPending} className="pressable min-h-10 rounded-xl border border-[var(--line)] px-3 text-xs font-bold disabled:opacity-50">Zamanı gelenleri sırala</button></div><div className="grid gap-5 p-5 sm:p-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]"><form onSubmit={submit} className="space-y-3"><label className="block text-xs font-bold text-[var(--muted)]">Enstrüman<select value={instrumentId || instruments?.[0]?.id || ""} onChange={(event) => setInstrumentId(event.target.value)} className="field mt-1 text-sm">{instruments?.map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}</select></label><label className="block text-xs font-bold text-[var(--muted)]">Bakım türü<input value={maintenanceType} onChange={(event) => setMaintenanceType(event.target.value)} required maxLength={200} placeholder="Örn. piyano akordu" className="field mt-1 text-sm" /></label><div className="grid grid-cols-2 gap-2"><label className="text-xs font-bold text-[var(--muted)]">Dönem (gün)<input type="number" min="1" max="3650" value={periodDays} onChange={(event) => setPeriodDays(event.target.value)} required className="field mt-1 text-sm" /></label><label className="text-xs font-bold text-[var(--muted)]">Sonraki tarih<input type="datetime-local" value={nextReminderAt} onChange={(event) => setNextReminderAt(event.target.value)} required className="field mt-1 text-sm" /></label></div><label className="block text-xs font-bold text-[var(--muted)]">Bildirim<select value={preference} onChange={(event) => setPreference(event.target.value as "None" | "WhatsApp")} className="field mt-1 text-sm"><option value="WhatsApp">WhatsApp</option><option value="None">Bildirim yok</option></select></label><label className="flex items-center gap-2 text-xs font-semibold"><input type="checkbox" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} /> Etkin</label><button disabled={save.isPending || !instruments?.length} className="pressable min-h-11 w-full rounded-xl bg-[var(--brand)] text-xs font-bold text-white disabled:opacity-50">Ayarı kaydet</button>{message && <p role="status" className="rounded-xl bg-[var(--surface-muted)] p-3 text-xs">{message}</p>}</form><div className="space-y-2">{settings?.map((setting) => <article key={setting.id} className="rounded-xl border border-[var(--line)] p-3"><div className="flex items-start justify-between gap-3"><div><p className="text-sm font-bold">{setting.instrumentName} · {setting.maintenanceType}</p><p className="mt-1 text-xs text-[var(--muted)]">Her {setting.periodDays} gün · {setting.notificationPreference === "WhatsApp" ? "WhatsApp" : "Bildirim yok"} · {setting.consentingGuardianCount} rızası açık veli</p></div><span className={`rounded-full px-2 py-1 text-[.6rem] font-bold ${setting.isEnabled ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{setting.isEnabled ? "Etkin" : "Kapalı"}</span></div><p className="mt-2 text-[.62rem] text-[var(--muted)]">Sonraki: {new Date(setting.nextReminderAt).toLocaleString("tr-TR")}</p></article>)}{!settings?.length && <p className="rounded-xl bg-[var(--surface-muted)] p-4 text-center text-xs text-[var(--muted)]">Henüz bakım ayarı yok.</p>}</div></div></section>;
}
