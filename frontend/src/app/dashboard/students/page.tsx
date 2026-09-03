"use client";

import { useState, type FormEvent } from "react";
import { Icon, instrumentBadgeStyle } from "@/components/icons";
import { AddButton, FormActions, FormMessage, Modal, PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/use-auth";
import { useCreateStudent, useStudentOverviews, type StudentInstrumentSummary } from "@/lib/people";
import { StudentDetail } from "./student-detail";

// docs/04-permissions.md: öğrenci oluşturma/veli/kayıt yönetimi yalnızca Admin - Teacher
// yalnızca kendisine atanmış öğrencileri görür, formlar 403 vermesin diye gizlenir.
export default function StudentsPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  // "İçine girmeden anlayabilelim" - liste artık her satırda enstrüman rozetlerini de
  // taşıyan tek bir toplu istekten (overview) besleniyor, N+1 sorgu açmadan.
  const { data: overviews, isLoading } = useStudentOverviews();
  const students = overviews?.map((row) => row.student);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Öğrenciler"
        description="Öğrenciler, aldıkları dersler ve kayıt bilgileri."
        actions={isAdmin && <AddButton label="Öğrenci ekle" onClick={() => setShowCreate(true)} />}
      />

      <div className="app-card overflow-hidden">
        {isLoading && <div className="space-y-3 p-4">{Array.from({ length: 4 }, (_, index) => <div key={index} className="skeleton h-12 rounded-xl" />)}</div>}
        {students?.length === 0 && <p className="p-6 text-center text-sm text-[var(--muted)]">Henüz öğrenci yok.</p>}
        <ul className="divide-y divide-[var(--line)]">
          {overviews?.map(({ student, instruments }) => (
            <li id={`student-${student.id}`} key={student.id} className="scroll-mt-24 target:bg-[var(--brand-soft)]">
              <button
                onClick={() => setExpandedId(expandedId === student.id ? null : student.id)}
                className="pressable flex min-h-14 w-full items-center justify-between gap-3 px-4 py-3 text-left hover:bg-[var(--surface-muted)]"
                aria-expanded={expandedId === student.id}
              >
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-bold">{student.firstName} {student.lastName}</span>
                  <span className="text-meta mt-0.5 block">{student.birthDate}</span>
                </span>
                <InstrumentBadgeRow instruments={instruments} />
                <Icon name="chevron" className={`h-4 w-4 shrink-0 text-[var(--muted)] transition-transform ${expandedId === student.id ? "rotate-90" : ""}`} />
              </button>
              {expandedId === student.id && <StudentDetail studentId={student.id} isAdmin={isAdmin} />}
            </li>
          ))}
        </ul>
      </div>

      {isAdmin && (
        <Modal open={showCreate} title="Öğrenci ekle" onClose={() => setShowCreate(false)} size="sm">
          <CreateStudentForm onClose={() => setShowCreate(false)} onCreated={() => setShowCreate(false)} />
        </Modal>
      )}
    </div>
  );
}

// "Keman piyano bateri gitar tasarımları ile yatay barlar süslenebilir" - her enstrüman
// kendi ikonu ve renk kimliğiyle küçük bir rozet olur, satır tıklanmadan görünür.
function InstrumentBadgeRow({ instruments }: { instruments: StudentInstrumentSummary[] }) {
  if (!instruments.length) return null;
  return (
    <span className="flex shrink-0 items-center gap-1" aria-label={`Enstrümanlar: ${instruments.map((i) => i.instrumentName).join(", ")}`}>
      {instruments.map((instrument) => {
        const style = instrumentBadgeStyle(instrument.instrumentName);
        return (
          <span key={instrument.instrumentId} title={instrument.instrumentName} className={`grid h-7 w-7 place-items-center rounded-lg ${style.className}`}>
            <Icon name={style.icon} className="h-4 w-4" />
          </span>
        );
      })}
    </span>
  );
}

function CreateStudentForm({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
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
      onCreated();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Ad<input value={firstName} onChange={(e) => setFirstName(e.target.value)} required className="field text-sm" /></label>
        <label className="form-label">Soyad<input value={lastName} onChange={(e) => setLastName(e.target.value)} required className="field text-sm" /></label>
      </div>
      <label className="form-label">Doğum tarihi<input type="date" value={birthDate} onChange={(e) => setBirthDate(e.target.value)} required className="field text-sm" /></label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Öğrenci ekle" pending={createStudent.isPending} pendingLabel="Ekleniyor…" />
    </form>
  );
}
