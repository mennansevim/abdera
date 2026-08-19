"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useEnrollments, useInstruments, useStudents, useTeachers } from "@/lib/people";
import { DAY_NAMES_TR, useCreateLessonSeries } from "@/lib/scheduling";

const DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

// docs/00-master-prompt.md: ders serisi bir Enrollment üzerine kurulur - bu yüzden önce
// öğrenci, sonra o öğrencinin (aktif) kaydı seçilir.
export function CreateSeriesForm() {
  const { data: students } = useStudents();
  const [studentId, setStudentId] = useState("");
  const { data: enrollments } = useEnrollments(studentId);
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();

  const [enrollmentId, setEnrollmentId] = useState("");
  const [dayOfWeek, setDayOfWeek] = useState("Tuesday");
  const [startTime, setStartTime] = useState("18:00");
  const [durationMinutes, setDurationMinutes] = useState(45);
  const [effectiveFrom, setEffectiveFrom] = useState(() => new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<string | null>(null);
  const [summary, setSummary] = useState<string | null>(null);

  const createSeries = useCreateLessonSeries();
  const activeEnrollments = enrollments?.filter((e) => e.status === "Active") ?? [];

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSummary(null);
    try {
      const result = await createSeries.mutateAsync({
        enrollmentId,
        dayOfWeek,
        startTime: `${startTime}:00`,
        durationMinutes,
        effectiveFrom,
      });
      const skipped = result.generation.skippedHolidays.length + result.generation.skippedTeacherTimeOff.length;
      setSummary(
        `${result.generation.created} ders üretildi` + (skipped > 0 ? `, ${skipped} tarih tatil/izin nedeniyle atlandı.` : "."),
      );
      setEnrollmentId("");
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ders serisi oluşturulamadı.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3 rounded-lg border border-neutral-200 bg-white p-4">
      <h2 className="text-sm font-semibold text-neutral-700">Yeni ders serisi</h2>
      <div className="flex flex-wrap items-end gap-2">
        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Öğrenci</label>
          <select value={studentId} onChange={(e) => { setStudentId(e.target.value); setEnrollmentId(""); }} required
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm">
            <option value="">Seç</option>
            {students?.map((s) => (
              <option key={s.id} value={s.id}>{s.firstName} {s.lastName}</option>
            ))}
          </select>
        </div>

        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Kayıt (öğretmen · enstrüman)</label>
          <select value={enrollmentId} onChange={(e) => setEnrollmentId(e.target.value)} required disabled={!studentId}
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm">
            <option value="">Seç</option>
            {activeEnrollments.map((e) => {
              const teacher = teachers?.find((t) => t.id === e.teacherId);
              const instrument = instruments?.find((i) => i.id === e.instrumentId);
              return (
                <option key={e.id} value={e.id}>
                  {teacher ? `${teacher.firstName} ${teacher.lastName}` : "?"} · {instrument?.name ?? "?"}
                </option>
              );
            })}
          </select>
        </div>

        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Gün</label>
          <select value={dayOfWeek} onChange={(e) => setDayOfWeek(e.target.value)}
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm">
            {DAYS.map((d) => (
              <option key={d} value={d}>{DAY_NAMES_TR[d]}</option>
            ))}
          </select>
        </div>

        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Saat</label>
          <input type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} required
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm" />
        </div>

        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Süre (dk)</label>
          <input type="number" min={15} step={15} value={durationMinutes}
            onChange={(e) => setDurationMinutes(Number(e.target.value))} required
            className="block w-20 rounded-md border border-neutral-300 px-2 py-1 text-sm" />
        </div>

        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Başlangıç tarihi</label>
          <input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} required
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm" />
        </div>

        <button type="submit" disabled={createSeries.isPending || !enrollmentId}
          className="rounded-md bg-neutral-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
          {createSeries.isPending ? "Oluşturuluyor…" : "Seriyi oluştur"}
        </button>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}
      {summary && <p className="text-sm text-green-700">{summary}</p>}
    </form>
  );
}
