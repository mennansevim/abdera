"use client";

import { useState, type FormEvent } from "react";
import { FormMessage, SectionHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useModuleEnabled, useSchoolPolicy, useUpdateSchoolPolicy, type SchoolPolicy } from "@/lib/instance";

// Okulun kuralları (docs/multi-tenant/ARCHITECTURE.md §5.3). Yalnızca yönetici görür; sunucu
// doğrular ve audit_log'a yazar. Değişiklik geçmiş aidat/ödeme satırlarını etkilemez.
export function SchoolPolicyPanel() {
  const { data: policy } = useSchoolPolicy();

  return (
    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] px-4 py-2.5">
        <SectionHeader title="Okul kuralları" description="Telafi hakları, haftalık ders sınırı ve okul günü saatleri. Değişiklik geçmiş aidat ve ödemeleri etkilemez." />
      </div>
      <div className="p-4">
        {policy ? <PolicyForm policy={policy} /> : <div className="skeleton h-40 rounded-xl" aria-busy="true" />}
      </div>
    </section>
  );
}

function PolicyForm({ policy }: { policy: SchoolPolicy }) {
  const update = useUpdateSchoolPolicy();
  const groupsEnabled = useModuleEnabled("groups");
  const [form, setForm] = useState(() => ({
    ...policy,
    schoolDayStart: policy.schoolDayStart.slice(0, 5),
    schoolDayEnd: policy.schoolDayEnd.slice(0, 5),
  }));
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);

  function set<K extends keyof SchoolPolicy>(key: K, value: SchoolPolicy[K]) {
    setForm((current) => ({ ...current, [key]: value }));
    setMessage(null);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    try {
      await update.mutateAsync({ ...form, schoolDayStart: `${form.schoolDayStart}:00`, schoolDayEnd: `${form.schoolDayEnd}:00` });
      setMessage({ tone: "success", text: "Okul kuralları kaydedildi." });
    } catch (err) {
      const fieldError = err instanceof ApiError && err.errors ? Object.values(err.errors)[0]?.[0] : undefined;
      setMessage({ tone: "error", text: fieldError ?? (err instanceof ApiError ? err.detail ?? err.title : "Kurallar kaydedilemedi.") });
    }
  }

  const number = (key: "maxWeeklyLessons" | "makeupNoticeHours" | "makeupCreditValidDays" | "excusedMakeupWindowDays" | "groupLessonSharePercent") =>
    (event: React.ChangeEvent<HTMLInputElement>) => set(key, Number(event.target.value));

  return (
    <form onSubmit={submit} className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Haftalık ders sınırı (öğrenci başına)
          <input type="number" min={1} max={14} required value={form.maxWeeklyLessons} onChange={number("maxWeeklyLessons")} className="field text-sm" />
        </label>
        <label className="form-label">Telafi için iptal bildirimi (saat)
          <input type="number" min={0} max={168} required value={form.makeupNoticeHours} onChange={number("makeupNoticeHours")} className="field text-sm" />
          <span className="text-meta mt-1 block font-normal">Veli dersten en az bu kadar önce iptal ederse telafi hakkı doğar.</span>
        </label>
        <label className="form-label">Telafi hakkının geçerliliği (gün)
          <input type="number" min={1} max={365} required value={form.makeupCreditValidDays} onChange={number("makeupCreditValidDays")} className="field text-sm" />
        </label>
        <label className="form-label">Mazeretli devamsızlıkta telafi süresi (gün)
          <input type="number" min={1} max={365} required value={form.excusedMakeupWindowDays} onChange={number("excusedMakeupWindowDays")} className="field text-sm" />
        </label>
        <label className="form-label">Okul günü başlangıcı
          <input type="time" step={900} required value={form.schoolDayStart} onChange={(event) => set("schoolDayStart", event.target.value)} className="field text-sm" />
        </label>
        <label className="form-label">Okul günü bitişi
          <input type="time" step={900} required value={form.schoolDayEnd} onChange={(event) => set("schoolDayEnd", event.target.value)} className="field text-sm" />
        </label>
        {groupsEnabled && (
          <label className="form-label">Grup dersi öğretmen payı (%)
            <input type="number" min={0} max={100} step={0.01} required value={form.groupLessonSharePercent} onChange={number("groupLessonSharePercent")} className="field text-sm" />
            <span className="text-meta mt-1 block font-normal">Grup aidatlarının öğretmene aylık ödenen yüzdesi.</span>
          </label>
        )}
      </div>
      {message && <FormMessage tone={message.tone}>{message.text}</FormMessage>}
      <button type="submit" className="btn btn-primary" disabled={update.isPending}>{update.isPending ? "Kaydediliyor…" : "Kuralları kaydet"}</button>
    </form>
  );
}
