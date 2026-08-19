"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import {
  useCreateAndLinkGuardian,
  useCreateEnrollment,
  useEnrollments,
  useInstruments,
  useStudentGuardians,
  useTeachers,
} from "@/lib/people";

// isAdmin=false (Teacher) iken veli bilgisi hiç istenmez - /api/students/{id}/guardians
// Admin-only olduğu için Teacher'a 403 dönerdi (docs/04-permissions.md).
export function StudentDetail({ studentId, isAdmin }: { studentId: string; isAdmin: boolean }) {
  const { data: guardians } = useStudentGuardians(isAdmin ? studentId : "");
  const { data: enrollments } = useEnrollments(studentId);
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();

  return (
    <div className="grid gap-6 border-t border-neutral-200 bg-neutral-50 p-4 sm:grid-cols-2">
      {isAdmin && (
        <section>
          <h3 className="mb-2 text-sm font-semibold text-neutral-700">Veliler</h3>
          <ul className="mb-3 space-y-1 text-sm">
            {guardians?.map((g) => (
              <li key={g.id} className="rounded border border-neutral-200 bg-white px-2 py-1">
                {g.firstName} {g.lastName} · {g.phoneNumber}
                {g.relationship && ` · ${g.relationship}`}
                {g.isPrimary && " · birincil"}
              </li>
            ))}
            {guardians?.length === 0 && <li className="text-neutral-400">Henüz veli eklenmemiş.</li>}
          </ul>
          <AddGuardianForm studentId={studentId} />
        </section>
      )}

      <section>
        <h3 className="mb-2 text-sm font-semibold text-neutral-700">Kayıtlar (Enrollment)</h3>
        <ul className="mb-3 space-y-1 text-sm">
          {enrollments?.map((e) => {
            const teacher = teachers?.find((t) => t.id === e.teacherId);
            const instrument = instruments?.find((i) => i.id === e.instrumentId);
            return (
              <li key={e.id} className="rounded border border-neutral-200 bg-white px-2 py-1">
                {instrument?.name ?? "?"} · {teacher ? `${teacher.firstName} ${teacher.lastName}` : "?"} ·{" "}
                {e.status === "Active" ? "aktif" : e.status === "Paused" ? "durduruldu" : "sona erdi"}
              </li>
            );
          })}
          {enrollments?.length === 0 && <li className="text-neutral-400">Henüz kayıt yok.</li>}
        </ul>
        {isAdmin && (
          <AddEnrollmentForm studentId={studentId} teachers={teachers ?? []} instruments={instruments ?? []} />
        )}
      </section>
    </div>
  );
}

function AddGuardianForm({ studentId }: { studentId: string }) {
  const createAndLink = useCreateAndLinkGuardian(studentId);
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [relationship, setRelationship] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createAndLink.mutateAsync({ firstName, lastName, phoneNumber, relationship, isPrimary: true });
      setFirstName("");
      setLastName("");
      setPhoneNumber("");
      setRelationship("");
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Veli eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-wrap gap-2 text-sm">
      <input placeholder="Ad" value={firstName} onChange={(e) => setFirstName(e.target.value)} required
        className="w-24 rounded border border-neutral-300 px-2 py-1" />
      <input placeholder="Soyad" value={lastName} onChange={(e) => setLastName(e.target.value)} required
        className="w-24 rounded border border-neutral-300 px-2 py-1" />
      <input placeholder="0555 111 22 33" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} required
        className="w-32 rounded border border-neutral-300 px-2 py-1" />
      <input placeholder="Yakınlık (anne/baba)" value={relationship} onChange={(e) => setRelationship(e.target.value)}
        className="w-36 rounded border border-neutral-300 px-2 py-1" />
      <button type="submit" disabled={createAndLink.isPending}
        className="rounded bg-neutral-900 px-3 py-1 text-white disabled:opacity-50">
        Ekle
      </button>
      {error && <p className="w-full text-red-600">{error}</p>}
    </form>
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
    <form onSubmit={handleSubmit} className="flex flex-wrap gap-2 text-sm">
      <select value={teacherId} onChange={(e) => setTeacherId(e.target.value)} required
        className="rounded border border-neutral-300 px-2 py-1">
        <option value="">Öğretmen seç</option>
        {teachers.map((t) => (
          <option key={t.id} value={t.id}>{t.firstName} {t.lastName}</option>
        ))}
      </select>
      <select value={instrumentId} onChange={(e) => setInstrumentId(e.target.value)} required
        className="rounded border border-neutral-300 px-2 py-1">
        <option value="">Enstrüman seç</option>
        {availableInstruments.map((i) => (
          <option key={i.id} value={i.id}>{i.name}</option>
        ))}
      </select>
      <input type="date" value={startedAt} onChange={(e) => setStartedAt(e.target.value)} required
        className="rounded border border-neutral-300 px-2 py-1" />
      <button type="submit" disabled={createEnrollment.isPending}
        className="rounded bg-neutral-900 px-3 py-1 text-white disabled:opacity-50">
        Kaydet
      </button>
      {error && <p className="w-full text-red-600">{error}</p>}
    </form>
  );
}
