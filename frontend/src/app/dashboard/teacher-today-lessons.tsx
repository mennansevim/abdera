"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useCreateChangeRequest, useCreateLessonNote, useMarkAttendance, type AttendanceStatus } from "@/lib/attendance";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";

// docs/00-master-prompt.md Teacher UX: "The first screen should be My Lessons Today...
// Open lesson -> Present/Absent/Excused -> Short lesson note -> Homework/next goal -> Save.
// Do not require teachers to fill twenty fields after every lesson."
export function TeacherTodayLessons() {
  const todayStart = new Date();
  todayStart.setHours(0, 0, 0, 0);
  const todayEnd = new Date(todayStart);
  todayEnd.setDate(todayEnd.getDate() + 1);

  const { data: lessons, isLoading } = useCalendar(todayStart.toISOString(), todayEnd.toISOString());
  const [expandedId, setExpandedId] = useState<string | null>(null);

  return (
    <div className="space-y-3">
      {isLoading && <p className="text-sm text-neutral-500">Yükleniyor…</p>}
      {lessons?.length === 0 && (
        <p className="rounded-lg border border-dashed border-neutral-300 p-6 text-sm text-neutral-500">
          Bugün dersin yok.
        </p>
      )}
      {lessons
        ?.slice()
        .sort((a, b) => a.startAt.localeCompare(b.startAt))
        .map((lesson) => (
          <div key={lesson.id} className="overflow-hidden rounded-lg border border-neutral-200 bg-white">
            <button
              onClick={() => setExpandedId(expandedId === lesson.id ? null : lesson.id)}
              className="flex w-full items-center justify-between px-4 py-3 text-left text-sm hover:bg-neutral-50"
            >
              <span>
                <span className="font-medium">
                  {new Date(lesson.startAt).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}
                </span>{" "}
                {lesson.studentName} · {lesson.instrumentName}
              </span>
              <StatusBadge status={lesson.status} />
            </button>
            {expandedId === lesson.id && <LessonActions lesson={lesson} onDone={() => setExpandedId(null)} />}
          </div>
        ))}
    </div>
  );
}

function StatusBadge({ status }: { status: CalendarLesson["status"] }) {
  const label: Record<CalendarLesson["status"], string> = {
    Normal: "bekliyor",
    Rescheduled: "ertelendi",
    Cancelled: "iptal",
    Completed: "tamamlandı",
    Makeup: "telafi",
  };
  return <span className="text-xs text-neutral-400">{label[status]}</span>;
}

function LessonActions({ lesson, onDone }: { lesson: CalendarLesson; onDone: () => void }) {
  const markAttendance = useMarkAttendance(lesson.id);
  const createNote = useCreateLessonNote(lesson.id);
  const createChangeRequest = useCreateChangeRequest(lesson.id);

  const [status, setStatus] = useState<AttendanceStatus | null>(null);
  const [practiced, setPracticed] = useState("");
  const [note, setNote] = useState("");
  const [homework, setHomework] = useState("");
  const [nextGoal, setNextGoal] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [showChangeForm, setShowChangeForm] = useState(false);

  async function handleSave() {
    if (!status) {
      setError("Önce yoklama durumu seç.");
      return;
    }
    setError(null);
    try {
      await markAttendance.mutateAsync({ status, note: note || undefined });
      if (practiced || note || homework || nextGoal) {
        await createNote.mutateAsync({
          practiced: practiced || undefined,
          note: note || undefined,
          homework: homework || undefined,
          nextGoal: nextGoal || undefined,
        });
      }
      onDone();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Kaydedilemedi.");
    }
  }

  const disabled = lesson.status === "Cancelled" || lesson.status === "Completed";

  return (
    <div className="space-y-3 border-t border-neutral-200 bg-neutral-50 p-4">
      {disabled ? (
        <p className="text-sm text-neutral-500">
          Bu ders zaten {lesson.status === "Completed" ? "tamamlandı" : "iptal edildi"}, işlem yapılamaz.
        </p>
      ) : (
        <>
          <div className="flex gap-2">
            {(["Present", "Absent", "Excused"] as const).map((s) => (
              <button
                key={s}
                onClick={() => setStatus(s)}
                className={`rounded-md border px-3 py-1.5 text-sm ${
                  status === s ? "border-neutral-900 bg-neutral-900 text-white" : "border-neutral-300 hover:bg-neutral-100"
                }`}
              >
                {s === "Present" ? "Geldi" : s === "Absent" ? "Gelmedi" : "Mazeretli"}
              </button>
            ))}
          </div>

          <textarea
            placeholder="Kısa not (opsiyonel)"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={2}
            className="w-full rounded-md border border-neutral-300 px-2 py-1 text-sm"
          />
          <div className="grid gap-2 sm:grid-cols-2">
            <input placeholder="Ne çalışıldı" value={practiced} onChange={(e) => setPracticed(e.target.value)}
              className="rounded-md border border-neutral-300 px-2 py-1 text-sm" />
            <input placeholder="Ödev" value={homework} onChange={(e) => setHomework(e.target.value)}
              className="rounded-md border border-neutral-300 px-2 py-1 text-sm" />
          </div>
          <input placeholder="Sonraki hedef" value={nextGoal} onChange={(e) => setNextGoal(e.target.value)}
            className="w-full rounded-md border border-neutral-300 px-2 py-1 text-sm" />

          <div className="flex items-center gap-2">
            <button onClick={handleSave} disabled={markAttendance.isPending}
              className="rounded-md bg-neutral-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
              Kaydet
            </button>
            <button onClick={() => setShowChangeForm((v) => !v)}
              className="text-sm text-neutral-500 underline">
              Ders değişikliği talep et
            </button>
          </div>

          {error && <p className="text-sm text-red-600">{error}</p>}
          {showChangeForm && (
            <ChangeRequestForm
              onSubmit={async (proposedStartAt, proposedEndAt, reason) => {
                await createChangeRequest.mutateAsync({ proposedStartAt, proposedEndAt, reason });
                setShowChangeForm(false);
              }}
            />
          )}
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
      const end = new Date(start.getTime() + durationMinutes * 60_000);
      await onSubmit(start.toISOString(), end.toISOString(), reason || undefined);
      setSent(true);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Talep gönderilemedi.");
    }
  }

  if (sent) {
    return <p className="text-sm text-green-700">Talep gönderildi, yönetici onayı bekleniyor.</p>;
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-wrap items-end gap-2 rounded-md border border-neutral-200 bg-white p-3">
      <input type="date" value={date} onChange={(e) => setDate(e.target.value)} required
        className="rounded-md border border-neutral-300 px-2 py-1 text-sm" />
      <input type="time" value={time} onChange={(e) => setTime(e.target.value)} required
        className="rounded-md border border-neutral-300 px-2 py-1 text-sm" />
      <input type="number" min={15} step={15} value={durationMinutes} onChange={(e) => setDurationMinutes(Number(e.target.value))}
        className="w-20 rounded-md border border-neutral-300 px-2 py-1 text-sm" />
      <input placeholder="Sebep (opsiyonel)" value={reason} onChange={(e) => setReason(e.target.value)}
        className="rounded-md border border-neutral-300 px-2 py-1 text-sm" />
      <button type="submit" className="rounded-md bg-neutral-900 px-3 py-1.5 text-sm text-white">
        Talep gönder
      </button>
      {error && <p className="w-full text-sm text-red-600">{error}</p>}
    </form>
  );
}
