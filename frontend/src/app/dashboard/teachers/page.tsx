"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/use-auth";
import { useCreateTeacher, useInstruments, useTeachers } from "@/lib/people";

export default function TeachersPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const { data: teachers, isLoading } = useTeachers();
  const { data: instruments } = useInstruments();

  return (
    <div className="space-y-5">
      <h1 className="text-display font-serif italic">Öğretmenler</h1>

      {isAdmin && <CreateTeacherForm instruments={instruments ?? []} />}

      <div className="app-card overflow-hidden">
        {isLoading && <div className="space-y-3 p-4">{Array.from({ length: 4 }, (_, index) => <div key={index} className="skeleton h-12 rounded-xl" />)}</div>}
        {teachers?.length === 0 && <p className="p-6 text-center text-sm text-[var(--muted)]">Henüz öğretmen yok.</p>}
        <ul className="divide-y divide-[var(--line)]">
          {teachers?.map((teacher) => (
            <li id={`teacher-${teacher.id}`} key={teacher.id} className="scroll-mt-24 flex min-h-14 items-center justify-between gap-3 px-4 py-3 target:bg-[var(--brand-soft)]">
              <span className="min-w-0">
                <span className="block text-sm font-bold">{teacher.firstName} {teacher.lastName}</span>
                <span className="text-meta mt-0.5 block truncate">
                  {teacher.instrumentIds
                    .map((id) => instruments?.find((i) => i.id === id)?.name)
                    .filter(Boolean)
                    .join(", ")}
                </span>
              </span>
              <span className="shrink-0 text-right">
                <span className={`inline-flex rounded-full px-2 py-0.5 text-[.62rem] font-bold ${teacher.status === "Active" ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{teacher.status === "Active" ? "Aktif" : "Pasif"}</span>
                <span className="text-meta mt-0.5 block">{teacher.hasLoginAccount ? "Giriş hesabı var" : "Giriş hesabı yok"}</span>
              </span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

function CreateTeacherForm({ instruments }: { instruments: { id: string; name: string }[] }) {
  const createTeacher = useCreateTeacher();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [selectedInstruments, setSelectedInstruments] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [temporaryPassword, setTemporaryPassword] = useState<string | null>(null);

  function toggleInstrument(id: string) {
    setSelectedInstruments((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setTemporaryPassword(null);
    try {
      const result = await createTeacher.mutateAsync({
        firstName,
        lastName,
        instrumentIds: selectedInstruments,
        email: email || undefined,
      });
      setFirstName("");
      setLastName("");
      setEmail("");
      setSelectedInstruments([]);
      if (result.temporaryPassword) {
        setTemporaryPassword(result.temporaryPassword);
      }
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğretmen eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="app-card space-y-3 p-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1.5">
          <label className="text-[.7rem] font-semibold text-[var(--muted)]">Ad</label>
          <input value={firstName} onChange={(e) => setFirstName(e.target.value)} required className="field min-h-11 w-32 text-sm" />
        </div>
        <div className="space-y-1.5">
          <label className="text-[.7rem] font-semibold text-[var(--muted)]">Soyad</label>
          <input value={lastName} onChange={(e) => setLastName(e.target.value)} required className="field min-h-11 w-32 text-sm" />
        </div>
        <div className="space-y-1.5">
          <label className="text-[.7rem] font-semibold text-[var(--muted)]">E-posta (giriş hesabı için, opsiyonel)</label>
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className="field min-h-11 w-56 text-sm" />
        </div>
      </div>

      <div className="space-y-1.5">
        <label className="text-[.7rem] font-semibold text-[var(--muted)]">Enstrümanlar</label>
        <div className="flex flex-wrap gap-2">
          {instruments.map((i) => {
            const checked = selectedInstruments.includes(i.id);
            return (
              <button
                key={i.id}
                type="button"
                onClick={() => toggleInstrument(i.id)}
                aria-pressed={checked}
                className={`pressable min-h-9 rounded-full border px-3 text-xs font-semibold ${checked ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand)]" : "border-[var(--line)] bg-white text-[var(--muted)] hover:border-[#e0c39d]"}`}
              >
                {i.name}
              </button>
            );
          })}
        </div>
      </div>

      <button type="submit" disabled={createTeacher.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)] disabled:opacity-50">
        {createTeacher.isPending ? "Ekleniyor…" : "Öğretmen ekle"}
      </button>

      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}

      {temporaryPassword && (
        <p className="rounded-xl border border-[var(--warning)]/40 bg-[var(--warning-soft)] p-3 text-xs font-medium text-[var(--warning-strong)]">
          Geçici şifre: <code className="font-mono font-bold">{temporaryPassword}</code> — bunu öğretmene sözlü/WhatsApp ile
          ilet, bir daha gösterilmeyecek.
        </p>
      )}
    </form>
  );
}
