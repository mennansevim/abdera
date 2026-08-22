"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import {
  useCreateAndLinkGuardian,
  useCreateEnrollment,
  useEnrollments,
  useInstruments,
  useStudentGuardians,
  useTeachers,
} from "@/lib/people";

const ENROLLMENT_STATUS_LABEL: Record<string, string> = { Active: "aktif", Paused: "durduruldu", Ended: "sona erdi" };

// isAdmin=false (Teacher) iken veli bilgisi hiç istenmez - /api/students/{id}/guardians
// Admin-only olduğu için Teacher'a 403 dönerdi (docs/04-permissions.md).
export function StudentDetail({ studentId, isAdmin }: { studentId: string; isAdmin: boolean }) {
  const [showGuardianForm, setShowGuardianForm] = useState(false);
  const { data: guardians } = useStudentGuardians(isAdmin ? studentId : "");
  const { data: enrollments } = useEnrollments(studentId);
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();

  return (
    <div className="grid gap-4 border-t border-[var(--line)] bg-[var(--surface-muted)] p-4 sm:grid-cols-2">
      {isAdmin && (
        <section>
          <div className="mb-2 flex items-center justify-between gap-3">
            <h3 className="text-micro">Veliler</h3>
            <button
              type="button"
              onClick={() => setShowGuardianForm(true)}
              className="pressable inline-flex min-h-9 items-center gap-1.5 rounded-lg border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand)] hover:border-[var(--brand)] hover:bg-[var(--brand-soft)]"
            >
              <span className="text-base leading-none">+</span> Veli ekle
            </button>
          </div>
          <ul className="space-y-1.5">
            {guardians?.map((g) => (
              <li key={g.id} className="rounded-xl border border-[var(--line)] bg-white px-3 py-2 text-sm">
                <span className="font-semibold">{g.firstName} {g.lastName}</span>
                <span className="text-meta"> · {g.phoneNumber}{g.relationship && ` · ${g.relationship}`}{g.isPrimary && " · birincil"}</span>
              </li>
            ))}
            {guardians?.length === 0 && (
              <li className="rounded-xl border border-dashed border-[var(--line)] bg-white/60 px-3 py-4 text-center text-sm text-[var(--muted)]">
                Henüz veli eklenmemiş. Eklemek için yukarıdaki butonu kullanın.
              </li>
            )}
          </ul>
          <AddGuardianForm studentId={studentId} open={showGuardianForm} onClose={() => setShowGuardianForm(false)} />
        </section>
      )}

      <section>
        <h3 className="text-micro mb-2">Kayıtlar (Enrollment)</h3>
        <ul className="mb-3 space-y-1.5">
          {enrollments?.map((e) => {
            const teacher = teachers?.find((t) => t.id === e.teacherId);
            const instrument = instruments?.find((i) => i.id === e.instrumentId);
            return (
              <li key={e.id} className="flex items-center justify-between gap-2 rounded-xl border border-[var(--line)] bg-white px-3 py-2 text-sm">
                <span><span className="font-semibold">{instrument?.name ?? "?"}</span><span className="text-meta"> · {teacher ? `${teacher.firstName} ${teacher.lastName}` : "?"}</span></span>
                <span className={`shrink-0 rounded-full px-2 py-0.5 text-[.62rem] font-bold ${e.status === "Active" ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{ENROLLMENT_STATUS_LABEL[e.status] ?? e.status}</span>
              </li>
            );
          })}
          {enrollments?.length === 0 && <li className="text-meta px-1">Henüz kayıt yok.</li>}
        </ul>
        {isAdmin && (
          <AddEnrollmentForm studentId={studentId} teachers={teachers ?? []} instruments={instruments ?? []} />
        )}
      </section>
    </div>
  );
}

function AddGuardianForm({ studentId, open, onClose }: { studentId: string; open: boolean; onClose: () => void }) {
  const createAndLink = useCreateAndLinkGuardian(studentId);
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [relationship, setRelationship] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent): Promise<boolean> {
    event.preventDefault();
    setError(null);
    try {
      await createAndLink.mutateAsync({ firstName, lastName, phoneNumber, relationship, isPrimary: true });
      setFirstName("");
      setLastName("");
      setPhoneNumber("");
      setRelationship("");
      return true;
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Veli eklenemedi.");
      return false;
    }
  }

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 grid place-items-center p-4" role="dialog" aria-modal="true" aria-label="Veli ekle">
      <button type="button" onClick={onClose} aria-label="Veli ekleme penceresini kapat" className="absolute inset-0 bg-[#2a1c14]/35 backdrop-blur-[2px]" />
      <form onSubmit={async (event) => { if (await handleSubmit(event)) onClose(); }} className="app-card relative z-10 w-full max-w-lg space-y-4 p-5 sm:p-6">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-micro text-[var(--brand-strong)]">Yeni kayıt</p>
            <h4 className="mt-1 font-serif text-xl font-bold italic">Veli ekle</h4>
            <p className="text-meta mt-1">Veli bilgileri yalnızca bu pencerede gösterilir.</p>
          </div>
          <button type="button" onClick={onClose} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white text-[var(--muted)]" aria-label="Kapat">
            <Icon name="close" className="h-4 w-4" />
          </button>
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="space-y-1.5 text-xs font-semibold text-[var(--muted)]">Ad<input placeholder="Ad" value={firstName} onChange={(e) => setFirstName(e.target.value)} required className="field text-sm" /></label>
          <label className="space-y-1.5 text-xs font-semibold text-[var(--muted)]">Soyad<input placeholder="Soyad" value={lastName} onChange={(e) => setLastName(e.target.value)} required className="field text-sm" /></label>
          <label className="space-y-1.5 text-xs font-semibold text-[var(--muted)]">Telefon<input placeholder="0555 111 22 33" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} required className="field text-sm" /></label>
          <label className="space-y-1.5 text-xs font-semibold text-[var(--muted)]">Yakınlık<input placeholder="Anne / baba" value={relationship} onChange={(e) => setRelationship(e.target.value)} className="field text-sm" /></label>
        </div>
        {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
        <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-4">
          <button type="button" onClick={onClose} className="pressable min-h-11 rounded-xl border border-[var(--line)] bg-white px-4 text-sm font-bold text-[var(--muted)]">Vazgeç</button>
          <button type="submit" disabled={createAndLink.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-5 text-sm font-bold text-white disabled:opacity-50">{createAndLink.isPending ? "Ekleniyor…" : "Veli ekle"}</button>
        </div>
      </form>
    </div>
  );
}

function AddEnrollmentForm({
  studentId,
  teachers,
  instruments,
}: {
  studentId: string;
  teachers: { id: string; firstName: string; lastName: string; instrumentIds: string[] }[];
  instruments: { id: string; name: string }[];
}) {
  const createEnrollment = useCreateEnrollment(studentId);
  const [teacherId, setTeacherId] = useState("");
  const [instrumentId, setInstrumentId] = useState("");
  const [startedAt, setStartedAt] = useState(() => new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<string | null>(null);

  const availableInstruments = teacherId
    ? instruments.filter((i) => teachers.find((t) => t.id === teacherId)?.instrumentIds.includes(i.id))
    : instruments;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createEnrollment.mutateAsync({ teacherId, instrumentId, startedAt });
      setTeacherId("");
      setInstrumentId("");
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Kayıt oluşturulamadı.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-wrap gap-2">
      <select value={teacherId} onChange={(e) => setTeacherId(e.target.value)} required className="field min-h-10 w-auto text-xs">
        <option value="">Öğretmen seç</option>
        {teachers.map((t) => (
          <option key={t.id} value={t.id}>{t.firstName} {t.lastName}</option>
        ))}
      </select>
      <select value={instrumentId} onChange={(e) => setInstrumentId(e.target.value)} required className="field min-h-10 w-auto text-xs">
        <option value="">Enstrüman seç</option>
        {availableInstruments.map((i) => (
          <option key={i.id} value={i.id}>{i.name}</option>
        ))}
      </select>
      <input type="date" value={startedAt} onChange={(e) => setStartedAt(e.target.value)} required className="field min-h-10 w-auto text-xs" />
      <button type="submit" disabled={createEnrollment.isPending} className="pressable min-h-10 rounded-xl bg-[var(--brand)] px-3 text-xs font-bold text-white disabled:opacity-50">Kaydet</button>
      {error && <p role="alert" className="w-full text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
    </form>
  );
}
