"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useCreateChangeRequest, useCreateLessonNote, useMarkAttendance, type AttendanceStatus } from "@/lib/attendance";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";

const LESSON_TONES = [
  { bg: "#d9f0ee", text: "#277a76" },
  { bg: "#f8e7c8", text: "#99610a" },
  { bg: "#ffe0d7", text: "#ad5137" },
  { bg: "#f5d9e8", text: "#a24474" },
];

function toneFor(value: string) {
  const sum = Array.from(value).reduce((total, letter) => total + letter.charCodeAt(0), 0);
  return LESSON_TONES[sum % LESSON_TONES.length];
}

export function TeacherTodayLessons({ date = new Date() }: { date?: Date }) {
  const todayStart = new Date(date);
  todayStart.setHours(0, 0, 0, 0);
  const todayEnd = new Date(todayStart);
  todayEnd.setDate(todayEnd.getDate() + 1);
  const { data: lessons, isLoading } = useCalendar(todayStart.toISOString(), todayEnd.toISOString());
  const [expanded, setExpanded] = useState<{ id: string; mode: "attendance" | "note" } | null>(null);

  if (isLoading) return <div className="space-y-3">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-36 rounded-2xl" />)}</div>;

  if (!lessons?.length) {
    return <div className="app-card grid min-h-52 place-items-center border-dashed p-8 text-center"><div><span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="music" className="h-6 w-6" /></span><p className="mt-4 text-sm font-bold">Bugün dersin yok</p><p className="mt-1 text-xs text-[var(--muted)]">Takvimde planlanan yeni dersler burada görünür.</p></div></div>;
  }

  return (
    <div className="space-y-3">
      {lessons.slice().sort((a,b) => a.startAt.localeCompare(b.startAt)).map((lesson) => {
        const tone = toneFor(lesson.instrumentName);
        const start = new Date(lesson.startAt);
        const end = new Date(lesson.endAt);
        return (
          <article key={lesson.id} className="app-card overflow-hidden">
            <div className="flex items-center gap-3 p-3 sm:p-4">
              <span className="flex h-14 w-14 shrink-0 flex-col items-center justify-center rounded-xl text-[.63rem] font-bold tabular-nums" style={{ background: tone.bg, color: tone.text }}>
                <span>{start.toLocaleTimeString("tr-TR", { hour:"2-digit", minute:"2-digit" })}</span>
                <span className="mt-0.5 text-[.5rem] opacity-65">{end.toLocaleTimeString("tr-TR", { hour:"2-digit", minute:"2-digit" })}</span>
              </span>
              <div className="min-w-0 flex-1">
                <div className="flex items-start justify-between gap-2"><h2 className="truncate text-sm font-bold">{lesson.studentName}</h2><StatusBadge lesson={lesson} /></div>
                <span className="mt-1 inline-flex rounded-full px-2 py-0.5 text-[.58rem] font-bold" style={{ background: tone.bg, color: tone.text }}>{lesson.instrumentName}</span>
              </div>
            </div>
            <div className="grid grid-cols-2 gap-2 border-t border-[var(--line)] p-3">
              <button onClick={() => setExpanded(expanded?.id === lesson.id && expanded.mode === "attendance" ? null : { id: lesson.id, mode: "attendance" })} className="pressable flex min-h-11 items-center justify-center gap-2 rounded-xl border border-[var(--line)] bg-white text-xs font-bold hover:bg-[var(--surface-muted)]"><Icon name="calendar" className="h-4 w-4" /> Yoklama Al</button>
              <button onClick={() => setExpanded(expanded?.id === lesson.id && expanded.mode === "note" ? null : { id: lesson.id, mode: "note" })} className="pressable flex min-h-11 items-center justify-center gap-2 rounded-xl border border-[var(--line)] bg-white text-xs font-bold hover:bg-[var(--surface-muted)]"><Icon name="note" className="h-4 w-4" /> Not Ekle</button>
            </div>
            {expanded?.id === lesson.id && <LessonActions lesson={lesson} initialMode={expanded.mode} onDone={() => setExpanded(null)} />}
          </article>
        );
      })}
    </div>
  );
}

function StatusBadge({ lesson }: { lesson: CalendarLesson }) {
  const config: Record<CalendarLesson["status"], { label: string; className: string }> = {
    Normal: { label: "Planlandı", className: "bg-[#e5f6e9] text-[#348351]" },
    Rescheduled: { label: "Ertelendi", className: "bg-[#fbefd7] text-[#98630b]" },
    Cancelled: { label: "İptal", className: "bg-[#ffe4e1] text-[#bf4949]" },
    Completed: { label: "Tamamlandı", className: "bg-[#ece9f8] text-[#625298]" },
    Makeup: { label: "Telafi", className: "bg-[#e3f2f4] text-[#357a83]" },
  };
  const rsvp = lesson.status === "Normal" && lesson.rsvpResponse === "Attending"
    ? { label: "Geliyor", className: "bg-[#def4e3] text-[#2d8750]" }
    : lesson.status === "Normal" && lesson.rsvpResponse === "NotAttending"
      ? { label: "Gelmiyor", className: "bg-[#ffe1df] text-[#c54949]" }
      : lesson.status === "Normal"
        ? { label: "Cevap yok", className: "bg-[#f9ecd0] text-[#9b6810]" }
        : config[lesson.status];
  return <span className={`shrink-0 rounded-full px-2 py-1 text-[.56rem] font-bold ${rsvp.className}`}>{rsvp.label}</span>;
}

function LessonActions({ lesson, initialMode, onDone }: { lesson: CalendarLesson; initialMode: "attendance" | "note"; onDone: () => void }) {
  const markAttendance = useMarkAttendance(lesson.id);
  const createNote = useCreateLessonNote(lesson.id);
  const createChangeRequest = useCreateChangeRequest(lesson.id);
  const [status, setStatus] = useState<AttendanceStatus | null>(null);
  const [practiced, setPracticed] = useState("");
  const [note, setNote] = useState("");
  const [homework, setHomework] = useState("");
  const [nextGoal, setNextGoal] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [showChangeForm, setShowChangeForm] = useState(false);
  const disabled = lesson.status === "Cancelled" || lesson.status === "Completed";

  async function handleSave() {
    if (!status && !practiced && !note && !homework && !nextGoal) {
      setError(initialMode === "attendance" ? "Yoklama durumu seçmelisin." : "Kaydetmek için kısa bir not eklemelisin.");
      return;
    }
    setError(null);
    try {
      if (status) await markAttendance.mutateAsync({ status, note: note || undefined });
      if (practiced || note || homework || nextGoal) await createNote.mutateAsync({ practiced: practiced || undefined, note: note || undefined, homework: homework || undefined, nextGoal: nextGoal || undefined });
      setSaved(true);
      window.setTimeout(onDone, 650);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Kaydedilemedi.");
    }
  }

  if (disabled) return <div className="border-t border-[var(--line)] bg-[var(--surface-muted)] p-4 text-xs text-[var(--muted)]">Bu ders {lesson.status === "Completed" ? "tamamlandı" : "iptal edildi"}; yeni işlem yapılamaz.</div>;

  return (
    <div className="space-y-4 border-t border-[var(--line)] bg-[#faf8f4] p-4 sm:p-5">
      {saved ? <p className="flex items-center gap-2 rounded-xl bg-[#e2f5e7] p-3 text-xs font-bold text-[#287747]"><Icon name="check" className="h-4 w-4" /> Ders bilgileri kaydedildi.</p> : (
        <>
          <div>
            <p className="mb-2 text-[.68rem] font-bold text-[#625b68]">Yoklama</p>
            <div className="grid grid-cols-3 gap-2">
              {(["Present","Absent","Excused"] as const).map((item) => {
                const labels = { Present:"Geldi", Absent:"Gelmedi", Excused:"Mazeretli" };
                const active = status === item;
                return <button key={item} onClick={() => setStatus(item)} className={`pressable min-h-11 rounded-xl border px-2 text-[.68rem] font-bold ${active ? item === "Present" ? "border-[#4ca76b] bg-[#ddf3e3] text-[#267844]" : item === "Absent" ? "border-[#dc7470] bg-[#ffe5e2] text-[#ad4141]" : "border-[#d2a94f] bg-[#fbefd5] text-[#865a09]" : "border-[var(--line)] bg-white text-[#77707b]"}`}>{labels[item]}</button>;
              })}
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="sm:col-span-2"><span className="mb-1.5 block text-[.68rem] font-bold text-[#625b68]">Kısa ders notu</span><textarea value={note} onChange={(event) => setNote(event.target.value)} rows={2} placeholder="Bugünkü ilerleme, dikkat edilmesi gerekenler…" className="field resize-y text-xs" /></label>
            <label><span className="mb-1.5 block text-[.68rem] font-bold text-[#625b68]">Ne çalışıldı?</span><input value={practiced} onChange={(event) => setPracticed(event.target.value)} className="field text-xs" placeholder="Örn. Gam ve etüt" /></label>
            <label><span className="mb-1.5 block text-[.68rem] font-bold text-[#625b68]">Ödev</span><input value={homework} onChange={(event) => setHomework(event.target.value)} className="field text-xs" placeholder="Bir sonraki derse kadar" /></label>
            <label className="sm:col-span-2"><span className="mb-1.5 block text-[.68rem] font-bold text-[#625b68]">Sonraki hedef</span><input value={nextGoal} onChange={(event) => setNextGoal(event.target.value)} className="field text-xs" placeholder="Bir sonraki dersin odağı" /></label>
          </div>
          {error && <p role="alert" className="rounded-xl bg-[#ffe8e5] p-3 text-xs font-semibold text-[#af4545]">{error}</p>}
          <div className="flex flex-wrap items-center gap-2">
            <button onClick={handleSave} disabled={markAttendance.isPending || createNote.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-5 text-xs font-bold text-white shadow-[0_8px_20px_rgba(74,55,143,.18)] disabled:opacity-50">{markAttendance.isPending || createNote.isPending ? "Kaydediliyor…" : "Kaydet"}</button>
            <button onClick={() => setShowChangeForm((value) => !value)} className="pressable min-h-11 rounded-xl px-3 text-xs font-bold text-[var(--brand)] hover:bg-[var(--brand-soft)]">Ders değişikliği iste</button>
          </div>
          {showChangeForm && <ChangeRequestForm onSubmit={async (proposedStartAt, proposedEndAt, reason) => { await createChangeRequest.mutateAsync({ proposedStartAt, proposedEndAt, reason }); setShowChangeForm(false); }} />}
        </>
      )}
    </div>
  );
}

function ChangeRequestForm({ onSubmit }: { onSubmit: (start: string, end: string, reason?: string) => Promise<void> }) {
  const [date, setDate] = useState("");
  const [time, setTime] = useState("18:00");
  const [durationMinutes, setDurationMinutes] = useState(45);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const start = new Date(`${date}T${time}:00`);
      await onSubmit(start.toISOString(), new Date(start.getTime() + durationMinutes * 60000).toISOString(), reason || undefined);
      setSent(true);
    } catch (err) { setError(err instanceof ApiError ? err.detail ?? err.title : "Talep gönderilemedi."); }
  }

  if (sent) return <p className="rounded-xl bg-[#e2f5e7] p-3 text-xs font-bold text-[#287747]">Talep gönderildi; yönetici onayı bekleniyor.</p>;
  return (
    <form onSubmit={handleSubmit} className="grid gap-2 rounded-2xl border border-[var(--line)] bg-white p-3 sm:grid-cols-2">
      <input type="date" value={date} onChange={(event) => setDate(event.target.value)} required className="field text-xs" aria-label="Önerilen gün" />
      <input type="time" value={time} onChange={(event) => setTime(event.target.value)} required className="field text-xs" aria-label="Önerilen saat" />
      <input type="number" min={15} step={15} value={durationMinutes} onChange={(event) => setDurationMinutes(Number(event.target.value))} className="field text-xs" aria-label="Ders süresi, dakika" />
      <input value={reason} onChange={(event) => setReason(event.target.value)} className="field text-xs" placeholder="Sebep (opsiyonel)" />
      <button type="submit" className="pressable min-h-11 rounded-xl bg-[var(--brand-strong)] px-4 text-xs font-bold text-white sm:col-span-2">Talebi gönder</button>
      {error && <p className="text-xs text-[#b84545] sm:col-span-2">{error}</p>}
    </form>
  );
}
