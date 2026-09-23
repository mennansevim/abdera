"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useEnrollments, useInstruments, useStudents, useTeachers } from "@/lib/people";
import { findRecurringSlots, type SuggestedSlot } from "@/lib/smart-scheduling";
import { DAY_NAMES_TR, useCalendar, useCreateLessonSeries, useTeacherAvailability } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";

const DAY_KEYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const WEEKDAY_ORDER = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
const DAY_SHORT_TR: Record<string, string> = { Monday: "Pzt", Tuesday: "Sal", Wednesday: "Çar", Thursday: "Per", Friday: "Cum", Saturday: "Cmt", Sunday: "Paz" };

function dateRange() {
  const from = new Date();
  from.setDate(from.getDate() - ((from.getDay() + 6) % 7));
  from.setHours(0, 0, 0, 0);
  const to = new Date();
  to.setDate(to.getDate() + 70);
  return { from: from.toISOString(), to: to.toISOString() };
}

function hhmm(date: Date) {
  return date.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
}

export function CreateSeriesForm({ onCreated, onCancel, initialDate, initialDay, initialTime }: { onCreated?: (summary: string) => void; onCancel?: () => void; initialDate?: string; initialDay?: string; initialTime?: string }) {
  const [range] = useState(() => dateRange());
  const { data: me } = useMe();
  const isTeacher = me?.role === "Teacher";
  const { data: students } = useStudents();
  const [studentId, setStudentId] = useState("");
  const { data: enrollments } = useEnrollments(studentId);
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();
  const { data: lessons, isLoading: lessonsLoading } = useCalendar(range.from, range.to);

  const [enrollmentId, setEnrollmentId] = useState("");
  const [durationMinutes, setDurationMinutes] = useState(45);
  const [effectiveFrom, setEffectiveFrom] = useState(() => initialDate ?? new Date().toISOString().slice(0, 10));
  const [selectedSlot, setSelectedSlot] = useState<SuggestedSlot | null>(null);
  const [showManual, setShowManual] = useState(() => Boolean(initialTime));
  const [manualDay, setManualDay] = useState(initialDay ?? "Tuesday");
  const [manualTime, setManualTime] = useState(initialTime ?? "18:00");
  const [error, setError] = useState<string | null>(null);
  const [summary, setSummary] = useState<string | null>(null);

  const createSeries = useCreateLessonSeries();
  const activeEnrollments = enrollments?.filter((item) => item.status === "Active") ?? [];
  const resolvedEnrollmentId = isTeacher && activeEnrollments.length === 1 ? activeEnrollments[0]!.id : enrollmentId;
  const enrollment = activeEnrollments.find((item) => item.id === resolvedEnrollmentId);
  const teacher = teachers?.find((item) => item.id === enrollment?.teacherId);
  const instrument = instruments?.find((item) => item.id === enrollment?.instrumentId);
  const { data: availability, isLoading: availabilityLoading } = useTeacherAvailability(enrollment?.teacherId ?? "");
  // Takvim henüz yüklenmemişken boş ders listesiyle öneri üretmek, dolu saatleri boş gibi
  // gösterirdi; tarama bitene kadar öneri listesi boş kalır.
  const scanning = availabilityLoading || lessonsLoading;
  const suggestions = enrollment && !scanning ? findRecurringSlots({ effectiveFrom, durationMinutes, teacherId: enrollment.teacherId, studentId, availability: availability ?? [], lessons: lessons ?? [] }) : [];
  const manualMode = isTeacher || showManual;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const slot = manualMode ? null : selectedSlot ?? suggestions[0] ?? null;
    if (!manualMode && !slot) { setError("Uygun bir saat seç veya özel saat belirle."); return; }
    setError(null);
    setSummary(null);
    try {
      const result = await createSeries.mutateAsync({
        enrollmentId: resolvedEnrollmentId,
        dayOfWeek: manualMode ? manualDay : DAY_KEYS[slot!.start.getDay()]!,
        startTime: `${manualMode ? manualTime : hhmm(slot!.start)}:00`,
        durationMinutes,
        effectiveFrom,
      });
      const skipped = result.generation.skippedHolidays.length + result.generation.skippedTeacherTimeOff.length;
      const text = `${result.generation.created} ders takvime yerleştirildi${skipped ? ` · ${skipped} uygun olmayan tarih atlandı` : ""}.`;
      setSummary(text);
      onCreated?.(text);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ders serisi oluşturulamadı.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="min-w-0 space-y-4">
      <div className={`grid min-w-0 gap-3 ${isTeacher && activeEnrollments.length <= 1 ? "" : "sm:grid-cols-2"}`}>
        <label className="form-label min-w-0">
          1 · Öğrenci
          <select value={studentId} onChange={(event) => { setStudentId(event.target.value); setEnrollmentId(""); setSelectedSlot(null); }} required className="field min-w-0 text-sm">
            <option value="">Öğrenci seç</option>
            {students?.map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}
          </select>
        </label>

        {isTeacher && activeEnrollments.length <= 1 ? (
          enrollment && (
            <div className="self-end rounded-xl bg-[var(--brand-soft)] px-3 py-2.5 text-sm font-bold text-[var(--brand-strong)]">
              {instrument?.name ?? "Ders"}
            </div>
          )
        ) : (
          <label className="form-label min-w-0">
            {isTeacher ? "2 · Ders" : "2 · Ders ve öğretmen"}
            <select value={enrollmentId} onChange={(event) => { setEnrollmentId(event.target.value); setSelectedSlot(null); }} required disabled={!studentId} className="field min-w-0 text-sm">
              <option value="">{isTeacher ? "Ders seç" : "Aktif kayıt seç"}</option>
              {activeEnrollments.map((item) => {
                const itemTeacher = teachers?.find((t) => t.id === item.teacherId);
                const itemInstrument = instruments?.find((i) => i.id === item.instrumentId);
                return (
                  <option key={item.id} value={item.id}>
                    {isTeacher
                      ? (itemInstrument?.name ?? "Ders")
                      : `${itemTeacher ? `${itemTeacher.firstName} ${itemTeacher.lastName}` : "Öğretmen"} · ${itemInstrument?.name ?? "Ders"}`}
                  </option>
                );
              })}
            </select>
          </label>
        )}
      </div>

      <div className="grid min-w-0 gap-3 sm:grid-cols-2">
        <label className="form-label min-w-0">
          Ders süresi
          <select value={durationMinutes} onChange={(event) => { setDurationMinutes(Number(event.target.value)); setSelectedSlot(null); }} className="field min-w-0 text-sm">
            <option value={30}>30 dakika</option>
            <option value={45}>45 dakika</option>
            <option value={60}>60 dakika</option>
          </select>
        </label>
        <label className="form-label min-w-0">
          Başlangıç tarihi
          <input type="date" value={effectiveFrom} onChange={(event) => { setEffectiveFrom(event.target.value); setSelectedSlot(null); }} required className="field min-w-0 text-sm" />
        </label>
        {!isTeacher && enrollment && <div className="min-w-0 rounded-2xl border border-[var(--line)] bg-[var(--surface-muted)] px-4 py-3 sm:col-span-2"><p className="text-[.75rem] font-bold text-[var(--muted)]">Planlanan kayıt</p><p className="mt-1 break-words text-sm font-bold">{instrument?.name} · {teacher?.firstName} {teacher?.lastName}</p><p className="mt-1 text-[.75rem] text-[var(--muted)]">{durationMinutes} dakika · haftalık seri</p></div>}
      </div>

      <p className="text-meta">Seçilen gün ve saat her hafta tekrarlanır; bir öğrenci haftada en fazla 4 düzenli ders alabilir. Çakışmalar otomatik kontrol edilir.</p>

      {enrollment && !manualMode && (
        <section className="rounded-2xl border border-[var(--line)] p-3 sm:p-4">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2"><p className="text-meta font-bold">Önerilen saatler</p>{scanning && <span className="text-meta">Takvim taranıyor…</span>}</div>
          {scanning ? <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-16 rounded-xl" />)}</div> : suggestions.length ? <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{suggestions.map((slot, index) => { const active = (selectedSlot ?? suggestions[0])?.start.getTime() === slot.start.getTime(); return <button key={slot.start.toISOString()} type="button" onClick={() => setSelectedSlot(slot)} aria-pressed={active} className={`pressable flex min-h-16 items-center gap-3 rounded-xl border p-3 text-left ${active ? "border-[var(--brand)] bg-[var(--brand-soft)] shadow-sm" : "border-[var(--line)] bg-white hover:border-[var(--brand)]"}`}><span className={`grid h-9 w-9 shrink-0 place-items-center rounded-xl text-xs font-bold ${active ? "bg-[var(--brand)] text-white" : "bg-[var(--surface-muted)] text-[var(--brand-strong)]"}`}>{index + 1}</span><span><span className="block text-xs font-bold">{DAY_NAMES_TR[DAY_KEYS[slot.start.getDay()]!]} · {hhmm(slot.start)}</span><span className="mt-0.5 block text-[.75rem] text-[var(--muted)]">{slot.reason}</span></span></button>; })}</div> : <div className="rounded-xl bg-[var(--warning-soft)] px-3 py-3 text-xs font-semibold text-[var(--warning-strong)]">Bu aralıkta düzenli boşluk bulunamadı. Özel saat belirleyebilir veya başlangıç tarihini değiştirebilirsin.</div>}
        </section>
      )}

      {enrollment && manualMode && (
        <div className="grid min-w-0 gap-3 rounded-2xl border border-[var(--line)] bg-[var(--surface-muted)] p-4 sm:grid-cols-2">
          <fieldset className="min-w-0">
            <legend className="form-label">Gün</legend>
            <div className="mt-1 grid grid-cols-4 gap-1.5 sm:grid-cols-7" role="radiogroup" aria-label="Ders günü">
              {WEEKDAY_ORDER.map((day) => {
                const active = manualDay === day;
                return <button key={day} type="button" role="radio" aria-checked={active} onClick={() => setManualDay(day)} className={`pressable grid min-h-11 place-items-center rounded-xl border px-1 text-xs font-bold ${active ? "border-[var(--brand)] bg-[var(--brand)] text-white shadow-sm" : "border-[var(--line)] bg-white text-[var(--foreground)] hover:border-[var(--brand)]"}`}>{DAY_SHORT_TR[day]}</button>;
              })}
            </div>
          </fieldset>
          <label className="form-label min-w-0">Saat<input type="time" value={manualTime} onChange={(event) => setManualTime(event.target.value)} className="field min-w-0 text-base tabular-nums" /></label>
        </div>
      )}

      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
      {summary && <p role="status" className="rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-xs font-semibold text-[var(--success-strong)]">{summary}</p>}

      {/* Uzun formda gönder butonu ekranın altında kaybolmasın: Modal gövdesinin (px-4 py-4
          sm:px-5) ve hızlı ekleme penceresinin aynı dolgusuna göre yapışkan alt şerit
          (FormActions ile aynı kalıp). */}
      <div className="sticky bottom-[-1rem] z-10 -mx-4 flex flex-wrap items-center justify-between gap-2 border-t border-[var(--line)] bg-[var(--surface)] px-4 pb-[max(1rem,env(safe-area-inset-bottom))] pt-3 sm:-mx-5 sm:px-5">
        {!isTeacher && (
          <button type="button" onClick={() => setShowManual((value) => !value)} className="btn btn-quiet">
            {showManual ? "Akıllı önerilere dön" : "Özel saat belirle"}
          </button>
        )}
        <span className="ml-auto flex gap-2">
          {onCancel && <button type="button" onClick={onCancel} className="btn btn-quiet">Vazgeç</button>}
          <button
            type="submit"
            disabled={createSeries.isPending || !resolvedEnrollmentId || (!manualMode && !selectedSlot && !suggestions.length)}
            className="btn btn-primary"
          >
            {createSeries.isPending ? "Yerleştiriliyor…" : "Seriyi takvime yerleştir"}
          </button>
        </span>
      </div>
    </form>
  );
}
