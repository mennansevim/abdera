"use client";

import { useMemo, useState } from "react";
import { useQueries } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api";
import { useUseMakeupCredit, type MakeupCredit } from "@/lib/billing";
import { useEnrollments, useInstruments, useStudents, useTeachers } from "@/lib/people";
import { findOpenSlots, type SuggestedSlot } from "@/lib/smart-scheduling";
import { useCalendar, useTeacherAvailability } from "@/lib/scheduling";

export interface MakeupSchedulerContext {
  studentId: string;
  studentName: string;
  teacherId: string;
  teacherName: string;
  instrumentId: string;
  instrumentName: string;
  sourceLessonId: string;
  sourceLessonStartAt: string;
  durationMinutes: number;
}

function formatSlot(slot: SuggestedSlot) {
  return {
    day: slot.start.toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "short" }),
    time: `${slot.start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}–${slot.end.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}`,
  };
}

function isAvailable(credit: MakeupCredit) {
  return credit.status === "Available" && new Date(credit.expiresAt).getTime() >= Date.now();
}

function makeupSearchRange(sourceLessonStartAt?: string) {
  const now = new Date();
  const firstEligibleDay = sourceLessonStartAt ? new Date(sourceLessonStartAt) : new Date(now);
  firstEligibleDay.setHours(0, 0, 0, 0);
  if (sourceLessonStartAt) firstEligibleDay.setDate(firstEligibleDay.getDate() + 1);
  const today = new Date(now);
  today.setHours(0, 0, 0, 0);
  const start = firstEligibleDay > today ? firstEligibleDay : today;
  const from = new Date(start);
  from.setDate(from.getDate() - ((from.getDay() + 6) % 7));
  from.setHours(0, 0, 0, 0);
  const to = new Date(start);
  to.setDate(to.getDate() + 21);
  return { from: from.toISOString(), to: to.toISOString(), start };
}

export function MakeupScheduler({
  context,
  onPlaced,
  onCancel,
}: {
  context?: MakeupSchedulerContext;
  onPlaced?: (summary: string) => void;
  onCancel?: () => void;
}) {
  const [studentId, setStudentId] = useState(context?.studentId ?? "");
  const [enrollmentId, setEnrollmentId] = useState("");
  const [creditId, setCreditId] = useState("");
  const [durationMinutes, setDurationMinutes] = useState(Math.round(context?.durationMinutes ?? 45));
  const [selectedSlot, setSelectedSlot] = useState<SuggestedSlot | null>(null);
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const { data: students } = useStudents();
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();

  const creditStudentIds = useMemo(
    () => context ? [context.studentId] : (students ?? []).map((student) => student.id),
    [context, students],
  );
  const creditQueries = useQueries({
    queries: creditStudentIds.map((id) => ({
      queryKey: ["makeup-credits", id],
      queryFn: () => api.get<MakeupCredit[]>(`/api/students/${id}/makeup-credits`),
    })),
  });
  const creditsByStudent = new Map(creditStudentIds.map((id, index) => [id, creditQueries[index]?.data ?? []]));
  const eligibleStudents = (students ?? []).filter((student) =>
    (creditsByStudent.get(student.id) ?? []).some(isAvailable));
  const requestedStudentId = context?.studentId ?? studentId;
  const activeCredits = (creditsByStudent.get(requestedStudentId) ?? [])
    .filter(isAvailable)
    .filter((credit) => !context || credit.sourceLessonId === context.sourceLessonId);
  const activeStudentId = activeCredits.length ? requestedStudentId : "";
  const creditsLoading = creditQueries.some((query) => query.isLoading);
  const creditsError = creditQueries.some((query) => query.isError);

  const { data: enrollments, isLoading: enrollmentsLoading } = useEnrollments(activeStudentId);
  const allActiveEnrollments = enrollments?.filter((item) => item.status === "Active") ?? [];
  const activeEnrollments = context
    ? allActiveEnrollments.filter((item) => item.teacherId === context.teacherId && item.instrumentId === context.instrumentId)
    : allActiveEnrollments;
  const contextualEnrollment = context ? activeEnrollments[0] : undefined;
  const enrollment = activeEnrollments.find((item) => item.id === enrollmentId)
    ?? contextualEnrollment
    ?? activeEnrollments[0];
  const activeCredit = activeCredits.find((item) => item.id === creditId) ?? activeCredits[0];
  const sourceLessonStartAt = activeCredit?.sourceLessonStartAt ?? context?.sourceLessonStartAt;
  const { data: availability, isLoading: availabilityLoading } = useTeacherAvailability(enrollment?.teacherId ?? "");
  const range = makeupSearchRange(sourceLessonStartAt);
  const { data: lessons, isLoading: lessonsLoading } = useCalendar(range.from, range.to);
  const useCredit = useUseMakeupCredit(activeStudentId);
  const suggestions = enrollment
    ? findOpenSlots({
        from: range.start,
        days: 21,
        durationMinutes,
        teacherId: enrollment.teacherId,
        studentId: activeStudentId,
        availability: availability ?? [],
        lessons: lessons ?? [],
        limit: 9,
      })
    : [];
  const slotsLoading = availabilityLoading || lessonsLoading;

  function enrollmentLabel(item: (typeof activeEnrollments)[number]) {
    const teacher = teachers?.find((row) => row.id === item.teacherId);
    const instrument = instruments?.find((row) => row.id === item.instrumentId);
    const teacherName = context?.teacherId === item.teacherId ? context.teacherName : `${teacher?.firstName ?? ""} ${teacher?.lastName ?? ""}`.trim();
    const instrumentName = context?.instrumentId === item.instrumentId ? context.instrumentName : instrument?.name;
    return `${instrumentName ?? "Ders"} · ${teacherName || "Öğretmen"}`;
  }

  async function place() {
    const slot = selectedSlot ?? suggestions[0];
    if (!enrollment || !activeCredit || !slot) return;
    setMessage(null);
    try {
      await useCredit.mutateAsync({
        creditId: activeCredit.id,
        teacherId: enrollment.teacherId,
        instrumentId: enrollment.instrumentId,
        startAt: slot.start.toISOString(),
        durationMinutes,
      });
      const formatted = formatSlot(slot);
      const text = `Telafi dersi ${formatted.day} ${formatted.time} saatine yerleştirildi.`;
      setMessage({ tone: "success", text });
      setSelectedSlot(null);
      onPlaced?.(text);
    } catch (error) {
      setMessage({ tone: "error", text: error instanceof ApiError ? error.detail ?? error.title : "Telafi dersi yerleştirilemedi." });
    }
  }

  return (
    <section className="space-y-4">
      {context ? (
        <div className="rounded-xl border border-[var(--brand)]/25 bg-[var(--brand-soft)] px-4 py-3">
          <p className="text-sm font-bold">{context.studentName}</p>
          <p className="mt-1 text-[.75rem] font-semibold text-[var(--brand-strong)]">{context.instrumentName} · {context.teacherName} · Tek derslik telafi</p>
          <p className="mt-1 text-[.75rem] text-[var(--muted)]">Yalnızca iptal edilen ders gününden sonraki tarihler gösterilir.</p>
        </div>
      ) : (
        <p className="text-meta">İptal edilen ders gününden sonraki 21 gün taranır; yalnızca kullanılabilir telafi hakkı olan öğrenciler listelenir.</p>
      )}

      {creditsLoading && <div className="grid gap-2 sm:grid-cols-3" aria-label="Telafi hakları yükleniyor">{[1, 2, 3].map((item) => <span key={item} className="skeleton h-14 rounded-xl" />)}</div>}
      {!creditsLoading && creditsError && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-3 text-xs font-semibold text-[var(--danger-strong)]">Telafi hakları yüklenemedi. Bağlantıyı kontrol edip yeniden deneyin.</p>}
      {!creditsLoading && context && !activeCredits.length && <p role="status" className="rounded-xl bg-[var(--warning-soft)] px-3 py-3 text-xs font-semibold text-[var(--warning-strong)]">Bu iptal edilen derse ait kullanılabilir telafi hakkı bulunamadı veya hakkın süresi doldu.</p>}

      {!creditsLoading && !creditsError && !context && eligibleStudents.length > 0 && (
        <div className="grid gap-3 lg:grid-cols-3">
          <label className="form-label">Öğrenci
            <select value={activeStudentId} onChange={(event) => { setStudentId(event.target.value); setEnrollmentId(""); setCreditId(""); setSelectedSlot(null); }} className="field text-sm">
              <option value="">Telafi hakkı olan öğrenciyi seç</option>
              {eligibleStudents.map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}
            </select>
          </label>
          <label className="form-label">Ders
            <select value={enrollment?.id ?? ""} onChange={(event) => { setEnrollmentId(event.target.value); setSelectedSlot(null); }} disabled={!activeStudentId} className="field text-sm">
              <option value="">Aktif kayıt seç</option>
              {activeEnrollments.map((item) => <option key={item.id} value={item.id}>{enrollmentLabel(item)}</option>)}
            </select>
          </label>
          <label className="form-label">Telafi hakkı
            <select value={activeCredit?.id ?? ""} onChange={(event) => setCreditId(event.target.value)} disabled={!activeStudentId} className="field text-sm">
              <option value="">Telafi hakkı seç</option>
              {activeCredits.map((credit) => <option key={credit.id} value={credit.id}>Son kullanım {new Date(credit.expiresAt).toLocaleDateString("tr-TR")}</option>)}
            </select>
          </label>
        </div>
      )}

      {!creditsLoading && !context && !eligibleStudents.length && !creditsError && <p className="rounded-xl bg-[var(--warning-soft)] px-3 py-3 text-xs font-semibold text-[var(--warning-strong)]">Kullanılabilir telafi hakkı olan öğrenci yok.</p>}
      {context && activeStudentId && enrollmentsLoading && <div className="grid gap-2 sm:grid-cols-3" aria-label="Ders kaydı yükleniyor">{[1, 2, 3].map((item) => <span key={item} className="skeleton h-14 rounded-xl" />)}</div>}
      {context && activeStudentId && !enrollmentsLoading && !enrollment && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-3 text-xs font-semibold text-[var(--danger-strong)]">Bu öğrenci, öğretmen ve enstrüman için aktif ders kaydı bulunamadı.</p>}

      {enrollment && activeCredit && (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div><p className="text-xs font-bold">Müsait saatler</p><p className="mt-1 text-[.75rem] text-[var(--muted)]">Öğrenci ve öğretmenin ortak boşlukları</p></div>
            <label className="inline-flex items-center gap-2 text-[.75rem] font-bold text-[var(--muted)]">Süre
              <select value={durationMinutes} onChange={(event) => { setDurationMinutes(Number(event.target.value)); setSelectedSlot(null); }} className="rounded-lg border border-[var(--line)] bg-white px-2 py-1.5">
                <option value={30}>30 dk</option><option value={45}>45 dk</option><option value={60}>60 dk</option>
              </select>
            </label>
          </div>
          {slotsLoading ? (
            <div className="grid gap-2 sm:grid-cols-3" aria-label="Müsait saatler yükleniyor">{[1, 2, 3].map((item) => <span key={item} className="skeleton h-16 rounded-xl" />)}</div>
          ) : suggestions.length ? (
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
              {suggestions.map((slot, index) => {
                const active = (selectedSlot ?? suggestions[0])?.start.getTime() === slot.start.getTime();
                const formatted = formatSlot(slot);
                return (
                  <button type="button" key={slot.start.toISOString()} onClick={() => setSelectedSlot(slot)} aria-pressed={active} className={`pressable flex min-h-16 items-center gap-3 rounded-xl border p-3 text-left ${active ? "border-[var(--brand)] bg-[var(--brand-soft)]" : "border-[var(--line)] bg-white hover:border-[var(--brand)]"}`}>
                    <span className={`grid h-8 w-8 shrink-0 place-items-center rounded-lg text-xs font-bold ${active ? "bg-[var(--brand)] text-white" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{index + 1}</span>
                    <span><span className="block text-xs font-bold capitalize">{formatted.day}</span><span className="mt-1 block text-[.75rem] font-semibold text-[var(--muted)]">{formatted.time}</span></span>
                  </button>
                );
              })}
            </div>
          ) : (
            <p className="rounded-xl bg-[var(--danger-soft)] px-3 py-3 text-xs font-semibold text-[var(--danger-strong)]">Önümüzdeki 21 günde uygun ortak boşluk bulunamadı.</p>
          )}
        </div>
      )}

      {message && <p role="status" className={`rounded-xl px-3 py-2.5 text-xs font-semibold ${message.tone === "success" ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--danger-soft)] text-[var(--danger-strong)]"}`}>{message.text}</p>}
      <div className="sticky bottom-[-1rem] z-10 -mx-4 flex justify-end gap-2 border-t border-[var(--line)] bg-[var(--surface)] px-4 pb-[max(1rem,env(safe-area-inset-bottom))] pt-4 sm:-mx-5 sm:px-5">
        {onCancel && <button type="button" onClick={onCancel} className="btn btn-quiet">Vazgeç</button>}
        <button type="button" onClick={place} disabled={useCredit.isPending || !activeCredit || !enrollment || (!selectedSlot && !suggestions.length)} className="btn btn-primary">{useCredit.isPending ? "Yerleştiriliyor…" : "Telafi dersini yerleştir"}</button>
      </div>
    </section>
  );
}
