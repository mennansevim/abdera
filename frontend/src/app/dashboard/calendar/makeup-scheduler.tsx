"use client";

import { useMemo, useState } from "react";
import { useQueries } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api";
import { useUseMakeupCredit, type MakeupCredit } from "@/lib/billing";
import { useEnrollments, useInstruments, useStudents, useTeachers } from "@/lib/people";
import { findOpenSlots, type SuggestedSlot } from "@/lib/smart-scheduling";
import { useCalendar, useTeacherAvailability } from "@/lib/scheduling";

function formatSlot(slot: SuggestedSlot) {
  return {
    day: slot.start.toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "short" }),
    time: `${slot.start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}–${slot.end.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}`,
  };
}

export function MakeupScheduler({ onPlaced, onCancel }: { onPlaced?: (summary: string) => void; onCancel?: () => void }) {
  const [studentId, setStudentId] = useState("");
  const [enrollmentId, setEnrollmentId] = useState("");
  const [creditId, setCreditId] = useState("");
  const [durationMinutes, setDurationMinutes] = useState(45);
  const [selectedSlot, setSelectedSlot] = useState<SuggestedSlot | null>(null);
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const { data: students } = useStudents();
  const creditQueries = useQueries({
    queries: (students ?? []).map((student) => ({
      queryKey: ["makeup-credits", student.id],
      queryFn: () => api.get<MakeupCredit[]>(`/api/students/${student.id}/makeup-credits`),
    })),
  });
  const creditsByStudent = new Map((students ?? []).map((student, index) => [student.id, creditQueries[index]?.data ?? []]));
  const eligibleStudents = (students ?? []).filter((student) => (creditsByStudent.get(student.id) ?? []).some((credit) => credit.status === "Available" && new Date(credit.expiresAt) >= new Date()));
  const activeStudentId = eligibleStudents.some((student) => student.id === studentId) ? studentId : "";
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();
  const { data: enrollments } = useEnrollments(activeStudentId);
  const activeCredits = (creditsByStudent.get(activeStudentId) ?? []).filter((item) => item.status === "Available" && new Date(item.expiresAt) >= new Date());
  const activeEnrollments = enrollments?.filter((item) => item.status === "Active") ?? [];
  const enrollment = activeEnrollments.find((item) => item.id === enrollmentId) ?? activeEnrollments[0];
  const activeCreditId = creditId || activeCredits[0]?.id || "";
  const { data: availability, isLoading } = useTeacherAvailability(enrollment?.teacherId ?? "");
  const range = useMemo(() => { const start = new Date(); const from = new Date(start); from.setDate(from.getDate() - ((from.getDay() + 6) % 7)); from.setHours(0, 0, 0, 0); const to = new Date(); to.setDate(to.getDate() + 21); return { from: from.toISOString(), to: to.toISOString(), start }; }, []);
  const { data: lessons } = useCalendar(range.from, range.to);
  const useCredit = useUseMakeupCredit(activeStudentId);
  const suggestions = enrollment ? findOpenSlots({ from: range.start, days: 21, durationMinutes, teacherId: enrollment.teacherId, studentId: activeStudentId, availability: availability ?? [], lessons: lessons ?? [], limit: 9 }) : [];

  async function place() {
    const slot = selectedSlot ?? suggestions[0];
    if (!enrollment || !activeCreditId || !slot) return;
    setMessage(null);
    try {
      await useCredit.mutateAsync({ creditId: activeCreditId, teacherId: enrollment.teacherId, instrumentId: enrollment.instrumentId, startAt: slot.start.toISOString(), durationMinutes });
      const text = `Telafi dersi ${formatSlot(slot).day} ${formatSlot(slot).time} saatine yerleştirildi.`;
      setMessage({ tone: "success", text });
      setSelectedSlot(null);
      onPlaced?.(text);
    } catch (error) {
      setMessage({ tone: "error", text: error instanceof ApiError ? error.detail ?? error.title : "Telafi dersi yerleştirilemedi." });
    }
  }

  return <section className="space-y-4">
    <p className="text-meta">Önümüzdeki 21 gün taranır; yalnızca kullanılabilir telafi hakkı olan öğrenciler listelenir.</p>
    <div className="grid gap-3 lg:grid-cols-3">
      <label className="form-label">Öğrenci<select value={activeStudentId} onChange={(event) => { setStudentId(event.target.value); setEnrollmentId(""); setCreditId(""); setSelectedSlot(null); }} className="field text-sm"><option value="">Telafi hakkı olan öğrenciyi seç</option>{eligibleStudents.map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}</select>{creditQueries.some((query) => query.isLoading) && <span className="block text-[.62rem] font-medium">Telafi hakları kontrol ediliyor…</span>}{!creditQueries.some((query) => query.isLoading) && !eligibleStudents.length && <span className="block text-[.62rem] font-medium text-[var(--warning-strong)]">Kullanılabilir telafi hakkı olan öğrenci yok.</span>}</label>
      <label className="form-label">Ders<select value={enrollment?.id ?? ""} onChange={(event) => { setEnrollmentId(event.target.value); setSelectedSlot(null); }} disabled={!activeStudentId} className="field text-sm"><option value="">Aktif kayıt seç</option>{activeEnrollments.map((item) => { const teacher = teachers?.find((row) => row.id === item.teacherId); const instrument = instruments?.find((row) => row.id === item.instrumentId); return <option key={item.id} value={item.id}>{instrument?.name ?? "Ders"} · {teacher?.firstName} {teacher?.lastName}</option>; })}</select></label>
      <label className="form-label">Telafi hakkı<select value={activeCreditId} onChange={(event) => setCreditId(event.target.value)} disabled={!activeStudentId} className="field text-sm"><option value="">Telafi hakkı seç</option>{activeCredits.map((credit) => <option key={credit.id} value={credit.id}>Son kullanım {new Date(credit.expiresAt).toLocaleDateString("tr-TR")}</option>)}</select></label>
    </div>
    {enrollment && activeCreditId && <div className="space-y-3"><div className="flex flex-wrap items-center justify-between gap-2"><p className="text-xs font-bold">En uygun boşluklar</p><label className="inline-flex items-center gap-2 text-[.65rem] font-bold text-[var(--muted)]">Süre<select value={durationMinutes} onChange={(event) => { setDurationMinutes(Number(event.target.value)); setSelectedSlot(null); }} className="rounded-lg border border-[var(--line)] bg-white px-2 py-1.5"><option value={30}>30 dk</option><option value={45}>45 dk</option><option value={60}>60 dk</option></select></label></div>{isLoading ? <div className="grid gap-2 sm:grid-cols-3">{[1,2,3].map((item) => <span key={item} className="skeleton h-16 rounded-xl" />)}</div> : suggestions.length ? <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{suggestions.map((slot, index) => { const active = (selectedSlot ?? suggestions[0])?.start.getTime() === slot.start.getTime(); const formatted = formatSlot(slot); return <button type="button" key={slot.start.toISOString()} onClick={() => setSelectedSlot(slot)} className={`pressable flex min-h-16 items-center gap-3 rounded-xl border p-3 text-left ${active ? "border-[var(--brand)] bg-[var(--brand-soft)]" : "border-[var(--line)] bg-white hover:border-[var(--brand)]"}`}><span className={`grid h-8 w-8 shrink-0 place-items-center rounded-lg text-xs font-bold ${active ? "bg-[var(--brand)] text-white" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{index + 1}</span><span><span className="block text-xs font-bold capitalize">{formatted.day}</span><span className="mt-1 block text-[.65rem] font-semibold text-[var(--muted)]">{formatted.time}</span></span></button>; })}</div> : <p className="rounded-xl bg-[var(--danger-soft)] px-3 py-3 text-xs font-semibold text-[var(--danger-strong)]">Önümüzdeki 21 günde uygun ortak boşluk bulunamadı.</p>}</div>}
    {message && <p role="status" className={`rounded-xl px-3 py-2.5 text-xs font-semibold ${message.tone === "success" ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--danger-soft)] text-[var(--danger-strong)]"}`}>{message.text}</p>}
    <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-3.5">
      {onCancel && <button type="button" onClick={onCancel} className="btn btn-quiet">Vazgeç</button>}
      <button type="button" onClick={place} disabled={useCredit.isPending || !activeCreditId || !enrollment || (!selectedSlot && !suggestions.length)} className="btn btn-primary">{useCredit.isPending ? "Yerleştiriliyor…" : "Seçilen slota yerleştir"}</button>
    </div>
  </section>;
}
