"use client";

import { useMemo, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useCreateChangeRequest, useCreateLessonNote, useMarkAttendance, type AttendanceStatus } from "@/lib/attendance";
import { buildInstrumentColorMap, INSTRUMENT_TONES } from "@/lib/lesson-colors";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";

export function TeacherTodayLessons({ date = new Date() }: { date?: Date }) {
  const todayStart = new Date(date);
  todayStart.setHours(0, 0, 0, 0);
  const todayEnd = new Date(todayStart);
  todayEnd.setDate(todayEnd.getDate() + 1);
  const { data: rawLessons, isLoading } = useCalendar(todayStart.toISOString(), todayEnd.toISOString());
  // Bir ders ertelendiğinde backend eski kaydı SİLMEZ, `Rescheduled` durumuna çevirip yeni saat
  // için ayrı bir satır açar (denetim izi - CLAUDE.md). Bugünden başka bir güne taşınan bir ders,
  // bu filtre olmadan hâlâ "bugün" listesinde normal bir ders gibi görünüp yoklama/not almaya
  // açık kalırdı.
  const lessons = useMemo(() => rawLessons?.filter((lesson) => lesson.status !== "Rescheduled"), [rawLessons]);
  const [expanded, setExpanded] = useState<{ id: string; mode: "attendance" | "note" } | null>(null);
  // Enstrüman renkleri artık dashboard'daki haftalık ızgara ile aynı paletten (lesson-colors.ts) -
  // önceden burada ayrı, karakter-hash'ine dayalı bir renk kümesi kullanılıyordu (docs/14-ui-design-prompt.md C).
  const colors = useMemo(() => buildInstrumentColorMap((lessons ?? []).map((lesson) => lesson.instrumentName)), [lessons]);

  if (isLoading) return <div className="space-y-3">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-36 rounded-2xl" />)}</div>;

  if (!lessons?.length) {
    return <div className="app-card grid min-h-52 place-items-center border-dashed p-8 text-center"><div><span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="music" className="h-6 w-6" /></span><p className="mt-4 text-sm font-bold">Bugün dersin yok</p><p className="mt-1 text-xs text-[var(--muted)]">Takvimde planlanan yeni dersler burada görünür.</p></div></div>;
  }

  return (
    <div className="grid gap-3 xl:grid-cols-2">
      {lessons.slice().sort((a,b) => a.startAt.localeCompare(b.startAt)).map((lesson) => {
        const tone = colors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0];
        const start = new Date(lesson.startAt);
        const end = new Date(lesson.endAt);
        return (
          <article key={lesson.id} className="app-card relative overflow-hidden xl:self-start">
            <span className="absolute inset-y-0 left-0 w-[3px]" style={{ background: tone.border }} aria-hidden="true" />
            <div className="flex items-center gap-3 p-3 sm:p-4">
              <span className="flex h-14 w-16 shrink-0 flex-col items-center justify-center rounded-xl bg-[var(--surface-muted)] text-center">
                <span className="text-title tabular-nums leading-none">{start.toLocaleTimeString("tr-TR", { hour:"2-digit", minute:"2-digit" })}</span>
                <span className="mt-1 text-[.6rem] font-semibold" style={{ color: tone.text }}>{lesson.instrumentName}</span>
              </span>
              <div className="min-w-0 flex-1">
                <div className="flex items-start justify-between gap-2"><h2 className="truncate text-sm font-bold">{lesson.studentName}</h2><StatusBadge lesson={lesson} /></div>
                <span className="text-meta mt-1 block">{start.toLocaleTimeString("tr-TR", { hour:"2-digit", minute:"2-digit" })}–{end.toLocaleTimeString("tr-TR", { hour:"2-digit", minute:"2-digit" })}</span>
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
    Normal: { label: "Planlandı", className: "bg-[var(--success-soft)] text-[var(--success-strong)]" },
    Rescheduled: { label: "Ertelendi", className: "bg-[var(--warning-soft)] text-[var(--warning-strong)]" },
    Cancelled: { label: "İptal", className: "bg-[var(--danger-soft)] text-[var(--danger-strong)]" },
    Completed: { label: "Tamamlandı", className: "bg-[#e6dcf6] text-[#4b3777]" },
    Makeup: { label: "Telafi", className: "bg-[#e0dbc4] text-[#48521f]" },
  };
  const rsvp = lesson.status === "Normal" && lesson.rsvpResponse === "Attending"
    ? { label: "Geliyor", className: "bg-[var(--success-soft)] text-[var(--success-strong)]" }
    : lesson.status === "Normal" && lesson.rsvpResponse === "NotAttending"
      ? { label: "Gelmiyor", className: "bg-[var(--danger-soft)] text-[var(--danger-strong)]" }
      : lesson.status === "Normal"
        ? { label: "Cevap yok", className: "bg-[var(--warning-soft)] text-[var(--warning-strong)]" }
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
    <div className="space-y-4 border-t border-[var(--line)] bg-[var(--surface-muted)] p-4 sm:p-5">
      {saved ? <p className="flex items-center gap-2 rounded-xl bg-[var(--success-soft)] p-3 text-xs font-bold text-[var(--success-strong)]"><Icon name="check" className="h-4 w-4" /> Ders bilgileri kaydedildi.</p> : (
        <>
          <div>
            <p className="mb-2 text-[.68rem] font-bold text-[var(--muted)]">Yoklama</p>
            <div className="grid grid-cols-3 gap-2">
              {(["Present","Absent","Excused"] as const).map((item) => {
                const labels = { Present:"Geldi", Absent:"Gelmedi", Excused:"Mazeretli" };
                const active = status === item;
                return <button key={item} onClick={() => setStatus(item)} className={`pressable min-h-11 rounded-xl border px-2 text-[.68rem] font-bold ${active ? item === "Present" ? "border-[color:var(--success)] bg-[var(--success-soft)] text-[var(--success-strong)]" : item === "Absent" ? "border-[color:var(--danger)] bg-[var(--danger-soft)] text-[var(--danger-strong)]" : "border-[color:var(--warning)] bg-[var(--warning-soft)] text-[var(--warning-strong)]" : "border-[var(--line)] bg-white text-[var(--muted)]"}`}>{labels[item]}</button>;
              })}
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="sm:col-span-2"><span className="mb-1.5 block text-[.68rem] font-bold text-[var(--muted)]">Kısa ders notu</span><textarea value={note} onChange={(event) => setNote(event.target.value)} rows={2} placeholder="Bugünkü ilerleme, dikkat edilmesi gerekenler…" className="field resize-y text-xs" /></label>
            <label><span className="mb-1.5 block text-[.68rem] font-bold text-[var(--muted)]">Ne çalışıldı?</span><input value={practiced} onChange={(event) => setPracticed(event.target.value)} className="field text-xs" placeholder="Örn. Gam ve etüt" /></label>
            <label><span className="mb-1.5 block text-[.68rem] font-bold text-[var(--muted)]">Ödev</span><input value={homework} onChange={(event) => setHomework(event.target.value)} className="field text-xs" placeholder="Bir sonraki derse kadar" /></label>
            <label className="sm:col-span-2"><span className="mb-1.5 block text-[.68rem] font-bold text-[var(--muted)]">Sonraki hedef</span><input value={nextGoal} onChange={(event) => setNextGoal(event.target.value)} className="field text-xs" placeholder="Bir sonraki dersin odağı" /></label>
          </div>
          {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] p-3 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
          <div className="flex flex-wrap items-center gap-2">
            <button onClick={handleSave} disabled={markAttendance.isPending || createNote.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-5 text-xs font-bold text-white shadow-[0_8px_20px_rgba(217,102,42,.22)] disabled:opacity-50">{markAttendance.isPending || createNote.isPending ? "Kaydediliyor…" : "Kaydet"}</button>
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

  if (sent) return <p className="rounded-xl bg-[var(--success-soft)] p-3 text-xs font-bold text-[var(--success-strong)]">Talep gönderildi; yönetici onayı bekleniyor.</p>;
  return (
    <form onSubmit={handleSubmit} className="grid gap-2 rounded-2xl border border-[var(--line)] bg-white p-3 sm:grid-cols-2">
      <input type="date" value={date} onChange={(event) => setDate(event.target.value)} required className="field text-xs" aria-label="Önerilen gün" />
      <input type="time" value={time} onChange={(event) => setTime(event.target.value)} required className="field text-xs" aria-label="Önerilen saat" />
      <input type="number" min={15} step={15} value={durationMinutes} onChange={(event) => setDurationMinutes(Number(event.target.value))} className="field text-xs" aria-label="Ders süresi, dakika" />
      <input value={reason} onChange={(event) => setReason(event.target.value)} className="field text-xs" placeholder="Sebep (opsiyonel)" />
      <button type="submit" className="pressable min-h-11 rounded-xl bg-[var(--brand-strong)] px-4 text-xs font-bold text-white sm:col-span-2">Talebi gönder</button>
      {error && <p className="text-xs text-[var(--danger-strong)] sm:col-span-2">{error}</p>}
    </form>
  );
}
