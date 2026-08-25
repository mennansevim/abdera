"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import {
  useCreateAndLinkGuardian,
  useCreateEnrollment,
  useEndEnrollment,
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
  const [showEnrollmentForm, setShowEnrollmentForm] = useState(false);
  const { data: guardians } = useStudentGuardians(isAdmin ? studentId : "");
  const { data: enrollments } = useEnrollments(studentId);
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();
  const activeEnrollments = enrollments?.filter((enrollment) => enrollment.status === "Active") ?? [];

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
        <div className="mb-2 flex items-center justify-between gap-3">
          <div><h3 className="text-micro">Kurslar</h3><p className="text-meta mt-1">Öğrencinin öğretmen ve enstrüman eşleşmeleri</p></div>
          {isAdmin && <button type="button" onClick={() => setShowEnrollmentForm((visible) => !visible)} aria-expanded={showEnrollmentForm} className="pressable inline-flex min-h-9 items-center gap-1.5 rounded-lg border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand)] hover:border-[var(--brand)] hover:bg-[var(--brand-soft)]"><span className="text-base leading-none">{showEnrollmentForm ? "−" : "+"}</span>{showEnrollmentForm ? "Formu kapat" : "Başka kurs ekle"}</button>}
        </div>
        <ul className="space-y-2">
          {activeEnrollments.map((e) => {
            const teacher = teachers?.find((t) => t.id === e.teacherId);
            const instrument = instruments?.find((i) => i.id === e.instrumentId);
            return (
              <EnrollmentRow key={e.id} studentId={studentId} enrollmentId={e.id} instrumentName={instrument?.name ?? "Enstrüman"} teacherName={teacher ? `${teacher.firstName} ${teacher.lastName}` : "Öğretmen"} status={ENROLLMENT_STATUS_LABEL[e.status] ?? e.status} isAdmin={isAdmin} />
            );
          })}
          {!activeEnrollments.length && <li className="rounded-xl border border-dashed border-[var(--line)] bg-white/60 px-3 py-5 text-center text-sm text-[var(--muted)]">Henüz aktif kurs yok.</li>}
        </ul>
        {isAdmin && showEnrollmentForm && <AddEnrollmentForm studentId={studentId} teachers={teachers ?? []} instruments={instruments ?? []} onClose={() => setShowEnrollmentForm(false)} />}
      </section>
    </div>
  );
}

function EnrollmentRow({ studentId, enrollmentId, instrumentName, teacherName, status, isAdmin }: { studentId: string; enrollmentId: string; instrumentName: string; teacherName: string; status: string; isAdmin: boolean }) {
  const endEnrollment = useEndEnrollment(studentId);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function remove() {
    setError(null);
    try {
      await endEnrollment.mutateAsync(enrollmentId);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Kurs kaldırılamadı.");
      setConfirming(false);
    }
  }

  return <li className="rounded-xl border border-[var(--line)] bg-white px-3 py-2.5 text-sm">
    <div className="flex items-center gap-3">
      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="music" className="h-4 w-4" /></span>
      <span className="min-w-0 flex-1"><span className="block truncate font-bold">{instrumentName}</span><span className="text-meta mt-0.5 block truncate">{teacherName}</span></span>
      <span className="shrink-0 rounded-full bg-[var(--success-soft)] px-2 py-0.5 text-[.62rem] font-bold text-[var(--success-strong)]">{status}</span>
      {isAdmin && !confirming && <button type="button" onClick={() => setConfirming(true)} className="pressable grid h-9 w-9 shrink-0 place-items-center rounded-lg text-[var(--muted)] hover:bg-[var(--danger-soft)] hover:text-[var(--danger-strong)]" aria-label={`${instrumentName} kursunu kaldır`}><Icon name="x" className="h-4 w-4" /></button>}
    </div>
    {confirming && <div className="mt-2 flex flex-wrap items-center justify-between gap-2 rounded-lg bg-[var(--danger-soft)] px-3 py-2"><p className="text-[.66rem] font-semibold text-[var(--danger-strong)]">Kurs kaldırılsın mı? Gelecekteki dersler durdurulur.</p><span className="flex gap-1.5"><button type="button" onClick={() => setConfirming(false)} className="pressable min-h-8 rounded-lg bg-white px-2.5 text-[.64rem] font-bold">İptal</button><button type="button" onClick={remove} disabled={endEnrollment.isPending} className="pressable min-h-8 rounded-lg bg-[var(--danger)] px-2.5 text-[.64rem] font-bold text-white disabled:opacity-50">{endEnrollment.isPending ? "Kaldırılıyor…" : "Kaldır"}</button></span></div>}
    {error && <p role="alert" className="mt-2 text-[.66rem] font-semibold text-[var(--danger-strong)]">{error}</p>}
  </li>;
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
  onClose,
}: {
  studentId: string;
  teachers: { id: string; firstName: string; lastName: string; instrumentIds: string[] }[];
  instruments: { id: string; name: string }[];
  onClose: () => void;
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
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Kayıt oluşturulamadı.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="mt-3 space-y-3 rounded-xl border border-[var(--brand)]/25 bg-[var(--brand-soft)]/45 p-3">
      <div className="flex items-start justify-between gap-3"><div><p className="text-xs font-bold">Yeni kurs</p><p className="text-[.64rem] text-[var(--muted)]">Öğretmen ve enstrümanı seçerek bu öğrenciye bağla.</p></div><button type="button" onClick={onClose} aria-label="Kurs ekleme formunu kapat" className="pressable grid h-8 w-8 place-items-center rounded-lg bg-white text-[var(--muted)]"><Icon name="close" className="h-3.5 w-3.5" /></button></div>
      <div className="grid gap-2 sm:grid-cols-2">
      <label className="space-y-1 text-[.66rem] font-bold text-[var(--muted)]">Öğretmen<select value={teacherId} onChange={(e) => { setTeacherId(e.target.value); setInstrumentId(""); }} required className="field min-h-10 text-xs">
        <option value="">Öğretmen seç</option>
        {teachers.map((t) => (
          <option key={t.id} value={t.id}>{t.firstName} {t.lastName}</option>
        ))}
      </select></label>
      <label className="space-y-1 text-[.66rem] font-bold text-[var(--muted)]">Enstrüman<select value={instrumentId} onChange={(e) => setInstrumentId(e.target.value)} required disabled={!teacherId} className="field min-h-10 text-xs">
        <option value="">Enstrüman seç</option>
        {availableInstruments.map((i) => (
          <option key={i.id} value={i.id}>{i.name}</option>
        ))}
      </select></label>
      </div>
      <label className="block space-y-1 text-[.66rem] font-bold text-[var(--muted)]">Başlangıç tarihi<input type="date" value={startedAt} onChange={(e) => setStartedAt(e.target.value)} required className="field min-h-10 text-xs" /></label>
      {error && <p role="alert" className="w-full text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
      <div className="flex items-center justify-between gap-2 border-t border-[var(--line)] pt-3"><a href="/dashboard/teachers" className="text-[.66rem] font-bold text-[var(--brand)] hover:underline">Öğretmen sayfasından ekle</a><button type="submit" disabled={createEnrollment.isPending} className="pressable min-h-10 rounded-xl bg-[var(--brand)] px-4 text-xs font-bold text-white disabled:opacity-50">{createEnrollment.isPending ? "Ekleniyor…" : "Kursu ekle"}</button></div>
    </form>
  );
}
