"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/use-auth";
import { useCreateStudent, useStudents } from "@/lib/people";
import { StudentDetail } from "./student-detail";

// docs/04-permissions.md: öğrenci oluşturma/veli/kayıt yönetimi yalnızca Admin - Teacher
// yalnızca kendisine atanmış öğrencileri görür, formlar 403 vermesin diye gizlenir.
export default function StudentsPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const { data: students, isLoading } = useStudents();
  const [expandedId, setExpandedId] = useState<string | null>(null);
  // Önceden bu form sayfanın üstünde her zaman açık duruyordu - ayrı bir "+" eylemi
  // olmadığı için "yeni öğrenci nasıl eklenir" belirsizdi. Diğer ekranlardaki
  // "+ X ekle" deseniyle (takvim, aidat) tutarlı hale getirildi: istek üzerine açılır,
  // başarılı eklemeden sonra kendiliğinden kapanır.
  const [showCreateForm, setShowCreateForm] = useState(false);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-display font-serif italic">Öğrenciler</h1>
        {isAdmin && <button type="button" onClick={() => setShowCreateForm((value) => !value)} aria-pressed={showCreateForm} className="pressable inline-flex min-h-11 items-center gap-1.5 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)]"><Icon name={showCreateForm ? "close" : "plus"} className="h-4 w-4" />{showCreateForm ? "Kapat" : "Yeni öğrenci"}</button>}
      </div>

      {isAdmin && showCreateForm && <CreateStudentForm onCreated={() => setShowCreateForm(false)} />}

      <div className="app-card overflow-hidden">
        {isLoading && <div className="space-y-3 p-4">{Array.from({ length: 4 }, (_, index) => <div key={index} className="skeleton h-12 rounded-xl" />)}</div>}
        {students?.length === 0 && <p className="p-6 text-center text-sm text-[var(--muted)]">Henüz öğrenci yok.</p>}
        <ul className="divide-y divide-[var(--line)]">
          {students?.map((student) => (
            <li id={`student-${student.id}`} key={student.id} className="scroll-mt-24 target:bg-[var(--brand-soft)]">
              <button
                onClick={() => setExpandedId(expandedId === student.id ? null : student.id)}
                className="pressable flex min-h-14 w-full items-center justify-between gap-3 px-4 py-3 text-left hover:bg-[var(--surface-muted)]"
                aria-expanded={expandedId === student.id}
              >
                <span className="min-w-0">
                  <span className="block text-sm font-bold">{student.firstName} {student.lastName}</span>
                  <span className="text-meta mt-0.5 block">{student.birthDate}</span>
                </span>
                <Icon name="chevron" className={`h-4 w-4 shrink-0 text-[var(--muted)] transition-transform ${expandedId === student.id ? "rotate-90" : ""}`} />
              </button>
              {expandedId === student.id && <StudentDetail studentId={student.id} isAdmin={isAdmin} />}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

function CreateStudentForm({ onCreated }: { onCreated: () => void }) {
  const createStudent = useCreateStudent();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [birthDate, setBirthDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createStudent.mutateAsync({ firstName, lastName, birthDate });
      setFirstName("");
      setLastName("");
      setBirthDate("");
      onCreated();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="app-card flex flex-wrap items-end gap-3 p-4">
      <div className="space-y-1.5">
        <label className="text-[.7rem] font-semibold text-[var(--muted)]">Ad</label>
        <input value={firstName} onChange={(e) => setFirstName(e.target.value)} required className="field min-h-11 w-32 text-sm" />
      </div>
      <div className="space-y-1.5">
        <label className="text-[.7rem] font-semibold text-[var(--muted)]">Soyad</label>
        <input value={lastName} onChange={(e) => setLastName(e.target.value)} required className="field min-h-11 w-32 text-sm" />
      </div>
      <div className="space-y-1.5">
        <label className="text-[.7rem] font-semibold text-[var(--muted)]">Doğum tarihi</label>
        <input type="date" value={birthDate} onChange={(e) => setBirthDate(e.target.value)} required className="field min-h-11 text-sm" />
      </div>
      <button type="submit" disabled={createStudent.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)] disabled:opacity-50">
        {createStudent.isPending ? "Ekleniyor…" : "Öğrenci ekle"}
      </button>
      {error && <p role="alert" className="w-full rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
    </form>
  );
}
