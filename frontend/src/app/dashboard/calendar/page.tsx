"use client";

import { useMemo, useRef, useState, type DragEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useRescheduleLesson } from "@/lib/attendance";
import { buildInstrumentColorMap, INSTRUMENT_TONES, type InstrumentTone } from "@/lib/lesson-colors";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import { CreateSeriesForm } from "./create-series-form";

// Dashboard önizlemesindeki (dashboard/page.tsx) TimeLabels/DayColumn ile aynı 09:00-19:00
// penceresi - iki ekranda da aynı saat matematiği kullanılıyor.
const GRID_START_HOUR = 9;
const GRID_END_HOUR = 19;
const GRID_WINDOW_MINUTES = (GRID_END_HOUR - GRID_START_HOUR) * 60;
const GRID_HEIGHT_REM = (GRID_END_HOUR - GRID_START_HOUR) * 3.4;
const WEEK_DAYS_TR = ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"];

// Haftanın Pazartesi'sini bulur - takvim her zaman Pazartesi'den başlar.
function startOfWeek(date: Date): Date {
  const d = new Date(date);
  const day = d.getDay();
  const diff = (day === 0 ? -6 : 1) - day;
  d.setDate(d.getDate() + diff);
  d.setHours(0, 0, 0, 0);
  return d;
}

function addDays(date: Date, days: number) {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}

function formatDateOnly(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function formatTime(date: Date) {
  return date.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
}

export default function CalendarPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const [weekStart, setWeekStart] = useState(() => startOfWeek(new Date()));
  const [showSeriesForm, setShowSeriesForm] = useState(false);

  const weekEnd = useMemo(() => addDays(weekStart, 7), [weekStart]);
  const { data: lessons, isLoading } = useCalendar(weekStart.toISOString(), weekEnd.toISOString());
  const weekDays = useMemo(() => Array.from({ length: 7 }, (_, index) => addDays(weekStart, index)), [weekStart]);
  const colors = useMemo(() => buildInstrumentColorMap((lessons ?? []).map((lesson) => lesson.instrumentName)), [lessons]);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-[1.45rem] font-bold tracking-[-0.035em] sm:text-[1.7rem]">Takvim</h1>
          <p className="mt-1 text-xs text-[var(--muted)]">{formatDateOnly(weekStart)} – {formatDateOnly(addDays(weekEnd, -1))}</p>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <button onClick={() => setWeekStart((d) => addDays(d, -7))} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Önceki hafta"><Icon name="arrow-left" className="h-4 w-4" /></button>
          <button onClick={() => setWeekStart(startOfWeek(new Date()))} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-[.68rem] font-semibold hover:bg-[var(--surface-muted)]">Bu hafta</button>
          <button onClick={() => setWeekStart((d) => addDays(d, 7))} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Sonraki hafta"><Icon name="arrow-right" className="h-4 w-4" /></button>
          {isAdmin && (
            <button onClick={() => setShowSeriesForm((value) => !value)} className="pressable ml-1 min-h-10 rounded-xl bg-[var(--brand)] px-3.5 text-[.68rem] font-bold text-white shadow-[0_6px_14px_rgba(74,55,143,.16)] hover:bg-[var(--brand-strong)]">
              {showSeriesForm ? "Formu kapat" : "+ Yeni ders serisi"}
            </button>
          )}
        </div>
      </div>

      {isAdmin && showSeriesForm && (
        <section className="app-card p-4">
          <CreateSeriesForm />
        </section>
      )}

      {isAdmin && (
        <p className="hidden items-center gap-1.5 text-[.65rem] text-[var(--muted)] xl:flex">
          <Icon name="swap" className="h-3.5 w-3.5" /> İpucu: bir ders kartını sürükleyip başka bir gün veya saate bırakarak taşıyabilirsin.
        </p>
      )}

      <WeeklyGrid weekDays={weekDays} lessons={lessons ?? []} loading={isLoading} colors={colors} isAdmin={isAdmin} />
    </div>
  );
}

function WeeklyGrid({
  weekDays,
  lessons,
  loading,
  colors,
  isAdmin,
}: {
  weekDays: Date[];
  lessons: CalendarLesson[];
  loading: boolean;
  colors: Map<string, InstrumentTone>;
  isAdmin: boolean;
}) {
  const reschedule = useRescheduleLesson();
  const draggingRef = useRef<CalendarLesson | null>(null);
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [movingId, setMovingId] = useState<string | null>(null);
  const [hoverSlot, setHoverSlot] = useState<{ day: string; minutes: number } | null>(null);
  const [toast, setToast] = useState<{ tone: "success" | "error"; text: string } | null>(null);

  function showToast(tone: "success" | "error", text: string) {
    setToast({ tone, text });
    window.setTimeout(() => setToast((current) => (current?.text === text ? null : current)), 4000);
  }

  function handleDragStart(event: DragEvent<HTMLDivElement>, lesson: CalendarLesson) {
    draggingRef.current = lesson;
    setDraggingId(lesson.id);
    event.dataTransfer.effectAllowed = "move";
    // Firefox sürüklemeyi başlatmak için setData çağrısı ister; içerik başka bir yerde okunmuyor.
    event.dataTransfer.setData("text/plain", lesson.id);
  }

  function handleDragEnd() {
    draggingRef.current = null;
    setDraggingId(null);
    setHoverSlot(null);
  }

  function minutesFromEvent(event: DragEvent<HTMLDivElement>) {
    const rect = event.currentTarget.getBoundingClientRect();
    const ratio = Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height));
    return Math.round((ratio * GRID_WINDOW_MINUTES) / 15) * 15;
  }

  function handleDragOver(event: DragEvent<HTMLDivElement>, day: Date) {
    if (!draggingRef.current) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    setHoverSlot({ day: day.toDateString(), minutes: minutesFromEvent(event) });
  }

  async function handleDrop(event: DragEvent<HTMLDivElement>, day: Date) {
    event.preventDefault();
    const lesson = draggingRef.current;
    draggingRef.current = null;
    setDraggingId(null);
    setHoverSlot(null);
    if (!lesson) return;

    const minutes = minutesFromEvent(event);
    const newStart = new Date(day);
    newStart.setHours(0, 0, 0, 0);
    newStart.setMinutes(GRID_START_HOUR * 60 + minutes);
    const durationMs = new Date(lesson.endAt).getTime() - new Date(lesson.startAt).getTime();
    const newEnd = new Date(newStart.getTime() + durationMs);

    if (newStart.getTime() === new Date(lesson.startAt).getTime()) return;
    if (newStart.getTime() < new Date().getTime()) {
      showToast("error", "Geçmiş bir tarihe/saate ders taşınamaz.");
      return;
    }

    setMovingId(lesson.id);
    try {
      await reschedule.mutateAsync({ lessonId: lesson.id, proposedStartAt: newStart.toISOString(), proposedEndAt: newEnd.toISOString() });
      showToast("success", `${lesson.studentName} dersi ${WEEK_DAYS_TR[(day.getDay() + 6) % 7]} ${formatTime(newStart)} olarak güncellendi.`);
    } catch (err) {
      showToast("error", err instanceof ApiError ? (err.detail ?? err.title) : "Ders taşınamadı.");
    } finally {
      setMovingId(null);
    }
  }

  if (loading) {
    return <div className="grid h-[26rem] grid-cols-1 gap-3 xl:grid-cols-7">{Array.from({ length: 7 }, (_, index) => <div key={index} className="skeleton rounded-xl" />)}</div>;
  }

  return (
    <section className="app-card min-w-0 overflow-hidden">
      {toast && (
        <div role="status" className={`flex items-center gap-2 border-b px-4 py-2.5 text-[.68rem] font-semibold ${toast.tone === "success" ? "border-[#cdeed8] bg-[#eafbf0] text-[#237247]" : "border-[#f5d4d0] bg-[#fff1ef] text-[#b8453f]"}`}>
          <Icon name={toast.tone === "success" ? "check" : "x"} className="h-3.5 w-3.5 shrink-0" />
          {toast.text}
        </div>
      )}

      {/* Masaüstü ızgara görünümü (sürükle-bırak burada aktif) */}
      <div className="hidden overflow-x-auto xl:block">
        <div className="grid min-w-[64rem] grid-cols-[3.4rem_repeat(7,minmax(8rem,1fr))] border-t border-[var(--line)]">
          <div className="border-r border-[var(--line)]" />
          {weekDays.map((day, index) => (
            <div key={day.toISOString()} className={`border-r border-[var(--line)] px-2 py-2.5 text-center last:border-r-0 ${day.toDateString() === new Date().toDateString() ? "bg-[#f0efff]" : ""}`}>
              <span className="block text-[.66rem] font-semibold text-[#746d79]">{WEEK_DAYS_TR[index]}</span>
              <span className="mt-1 block text-[.6rem] text-[var(--muted)]">{day.getDate()} {day.toLocaleDateString("tr-TR", { month: "short" })}</span>
            </div>
          ))}
          <GridTimeLabels />
          {weekDays.map((day) => (
            <GridDayColumn
              key={day.toISOString()}
              day={day}
              lessons={lessons}
              colors={colors}
              isAdmin={isAdmin}
              draggingId={draggingId}
              movingId={movingId}
              hoverMinutes={hoverSlot?.day === day.toDateString() ? hoverSlot.minutes : null}
              onDragStartLesson={handleDragStart}
              onDragEndLesson={handleDragEnd}
              onDragOverColumn={(event) => handleDragOver(event, day)}
              onDropColumn={(event) => handleDrop(event, day)}
            />
          ))}
        </div>
      </div>

      {/* Tablet/mobil ajanda görünümü - tasarım kuralı: dar ekranda ızgara yerine dikey liste. */}
      <div className="space-y-4 border-t border-[var(--line)] p-4 xl:hidden">
        {weekDays.map((day, index) => {
          const dayLessons = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString()).sort((a, b) => a.startAt.localeCompare(b.startAt));
          return (
            <div key={day.toISOString()}>
              <h3 className="mb-2 flex items-center gap-2 text-xs font-bold">
                <span className={`grid h-7 w-7 place-items-center rounded-lg ${day.toDateString() === new Date().toDateString() ? "bg-[var(--brand)] text-white" : "bg-[var(--surface-muted)] text-[#625b68]"}`}>{day.getDate()}</span>
                {WEEK_DAYS_TR[index]}
              </h3>
              <div className="space-y-2 pl-9">
                {dayLessons.map((lesson) => <AgendaLessonCard key={lesson.id} lesson={lesson} tone={colors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0]} showTeacher={isAdmin} />)}
                {!dayLessons.length && <p className="py-2 text-xs text-[#aaa3ad]">Planlanmış ders yok.</p>}
              </div>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function GridTimeLabels() {
  const totalHours = GRID_END_HOUR - GRID_START_HOUR;
  return (
    <div className="relative border-r border-t border-[var(--line)] bg-[#fbfaf7]" style={{ height: `${GRID_HEIGHT_REM}rem` }}>
      {Array.from({ length: totalHours + 1 }, (_, index) => (
        <span key={index} className="absolute right-2 -translate-y-1/2 text-[.53rem] tabular-nums text-[#aaa3ad]" style={{ top: `${(index / totalHours) * 100}%` }}>
          {String(GRID_START_HOUR + index).padStart(2, "0")}:00
        </span>
      ))}
    </div>
  );
}

function GridDayColumn({
  day,
  lessons,
  colors,
  isAdmin,
  draggingId,
  movingId,
  hoverMinutes,
  onDragStartLesson,
  onDragEndLesson,
  onDragOverColumn,
  onDropColumn,
}: {
  day: Date;
  lessons: CalendarLesson[];
  colors: Map<string, InstrumentTone>;
  isAdmin: boolean;
  draggingId: string | null;
  movingId: string | null;
  hoverMinutes: number | null;
  onDragStartLesson: (event: DragEvent<HTMLDivElement>, lesson: CalendarLesson) => void;
  onDragEndLesson: () => void;
  onDragOverColumn: (event: DragEvent<HTMLDivElement>) => void;
  onDropColumn: (event: DragEvent<HTMLDivElement>) => void;
}) {
  const entries = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString());
  const isToday = day.toDateString() === new Date().toDateString();
  const totalHours = GRID_END_HOUR - GRID_START_HOUR;
  const totalMinutes = totalHours * 60;

  return (
    <div
      onDragOver={onDragOverColumn}
      onDrop={onDropColumn}
      className={`relative border-r border-t border-[var(--line)] last:border-r-0 ${isToday ? "bg-[#f2f1ff]" : "bg-[#fbfaf7]"} ${hoverMinutes !== null ? "outline outline-2 -outline-offset-2 outline-[color:var(--brand)]" : ""}`}
      style={{ height: `${GRID_HEIGHT_REM}rem` }}
    >
      {Array.from({ length: totalHours - 1 }, (_, index) => (
        <span key={index} className="absolute inset-x-0 border-t border-dashed border-[#ebe7e1]" style={{ top: `${((index + 1) / totalHours) * 100}%` }} />
      ))}

      {hoverMinutes !== null && (
        <span className="pointer-events-none absolute inset-x-1 z-20 rounded-md border-2 border-dashed border-[var(--brand)] bg-[var(--brand)]/10" style={{ top: `${(hoverMinutes / totalMinutes) * 100}%`, height: "2.4rem" }} />
      )}

      {entries.map((lesson) => {
        const start = new Date(lesson.startAt);
        const end = new Date(lesson.endAt);
        const startMinutes = start.getHours() * 60 + start.getMinutes() - GRID_START_HOUR * 60;
        const duration = Math.max(30, (end.getTime() - start.getTime()) / 60000);
        const top = Math.max(0, Math.min(96, (startMinutes / totalMinutes) * 100));
        const height = Math.max(6.5, Math.min(100 - top, (duration / totalMinutes) * 100));
        const tone = colors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0];
        const draggable = isAdmin && lesson.status === "Normal";
        return (
          <div
            key={lesson.id}
            draggable={draggable}
            onDragStart={(event) => onDragStartLesson(event, lesson)}
            onDragEnd={onDragEndLesson}
            title={`${lesson.studentName} · ${lesson.instrumentName} · ${lesson.teacherName}`}
            className={`pressable absolute left-1.5 right-1.5 z-10 overflow-hidden rounded-md border-l-[3px] px-2 py-1 shadow-sm transition-opacity ${draggable ? "cursor-grab active:cursor-grabbing" : ""} ${draggingId === lesson.id ? "opacity-35" : "hover:z-20 hover:shadow-md"} ${movingId === lesson.id ? "animate-pulse" : ""}`}
            style={{ top: `${top}%`, height: `${height}%`, minHeight: "1.85rem", background: tone.bg, borderLeftColor: tone.border, color: tone.text }}
          >
            <span className="block text-[.52rem] font-bold tabular-nums">{formatTime(start)}–{formatTime(end)}</span>
            <span className="mt-0.5 block truncate text-[.57rem] font-bold">{lesson.studentName}</span>
            <span className="block truncate text-[.46rem] opacity-75">{lesson.instrumentName}{isAdmin ? ` · ${lesson.teacherName}` : ""}</span>
          </div>
        );
      })}
    </div>
  );
}

function AgendaLessonCard({ lesson, tone, showTeacher }: { lesson: CalendarLesson; tone: InstrumentTone; showTeacher: boolean }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  return (
    <article className="flex min-h-14 items-center gap-3 rounded-xl border border-[var(--line)] bg-white p-2.5 shadow-sm">
      <span className="h-9 w-1 rounded-full" style={{ background: tone.border }} />
      <span className="w-20 shrink-0 text-[.65rem] font-bold tabular-nums" style={{ color: tone.text }}>{formatTime(start)}–{formatTime(end)}</span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-xs font-bold">{lesson.studentName}</span>
        <span className="block truncate text-[.62rem] text-[var(--muted)]">{lesson.instrumentName}{showTeacher ? ` · ${lesson.teacherName}` : ""}</span>
      </span>
      <LessonStatusChip lesson={lesson} />
    </article>
  );
}

function LessonStatusChip({ lesson }: { lesson: CalendarLesson }) {
  const config: Record<CalendarLesson["status"], { label: string; className: string }> = {
    Normal: { label: "Planlandı", className: "bg-[#e5f6e9] text-[#348351]" },
    Rescheduled: { label: "Ertelendi", className: "bg-[#fbefd7] text-[#98630b]" },
    Cancelled: { label: "İptal", className: "bg-[#ffe4e1] text-[#bf4949]" },
    Completed: { label: "Tamamlandı", className: "bg-[#ece9f8] text-[#625298]" },
    Makeup: { label: "Telafi", className: "bg-[#e3f2f4] text-[#357a83]" },
  };
  const { label, className } = config[lesson.status];
  return <span className={`shrink-0 rounded-full px-2 py-1 text-[.56rem] font-bold ${className}`}>{label}</span>;
}
