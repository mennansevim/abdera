"use client";

import { useMemo, useRef, useState, type DragEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useRescheduleLesson } from "@/lib/attendance";
import { buildInstrumentColorMap, INSTRUMENT_TONES, type InstrumentTone } from "@/lib/lesson-colors";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import { computeHourWindow, layoutDayLessons, type HourWindow } from "@/lib/week-grid-layout";
import { CreateSeriesForm } from "./create-series-form";

// Saat penceresi ve çakışma yerleşimi artık dashboard önizlemesiyle (dashboard/page.tsx) aynı
// paylaşılan modülden (lib/week-grid-layout.ts) geliyor - sabit 09:00-19:00 önceden iki ekranda
// da ayrı ayrı kopyalanmıştı ve pencere dışı/çakışan dersleri yanlış konumlandırıyordu
// (docs/14-ui-design-prompt.md B3).
const GRID_HEIGHT_REM_PER_HOUR = 3.8;
const WEEK_DAYS_TR = ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"];
const INSTRUMENT_FILTERS = ["Hepsi", "Piyano", "Gitar", "Keman", "Bateri"] as const;

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

function studentInitials(name: string) {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part.charAt(0).toLocaleUpperCase("tr-TR"))
    .join("");
}

// Gece yarısından itibaren dakika -> "HH:MM". Sürükleme sırasında bırakılacak saat aralığını
// göstermek için (docs/14-ui-design-prompt.md sonrası kullanıcı geri bildirimi: "hangi saat
// aralığına bıraktığımı göremiyorum").
function formatMinutesOfDay(totalMinutes: number) {
  const hours = Math.floor(totalMinutes / 60) % 24;
  const minutes = totalMinutes % 60;
  return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}`;
}

export default function CalendarPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const [weekStart, setWeekStart] = useState(() => startOfWeek(new Date()));
  const [showSeriesForm, setShowSeriesForm] = useState(false);
  const [instrumentFilter, setInstrumentFilter] = useState<(typeof INSTRUMENT_FILTERS)[number]>("Hepsi");

  const weekEnd = useMemo(() => addDays(weekStart, 7), [weekStart]);
  const { data: rawLessons, isLoading } = useCalendar(weekStart.toISOString(), weekEnd.toISOString());
  // Bir ders ertelendiğinde backend eski kaydı SİLMEZ, `Rescheduled` durumuna çevirip yeni saat
  // için ayrı bir satır açar (denetim izi - CLAUDE.md). Eski kaydı ızgarada göstermek aynı dersin
  // iki yerde birden görünmesine yol açıyordu - değişiklik geçmişi `/dashboard/change-requests`'te
  // zaten var, canlı takvimde tekrar göstermeye gerek yok.
  const lessons = useMemo(() => (rawLessons ?? []).filter((lesson) => lesson.status !== "Rescheduled"), [rawLessons]);
  const visibleLessons = useMemo(
    () => instrumentFilter === "Hepsi" ? lessons : lessons.filter((lesson) => lesson.instrumentName.toLocaleLowerCase("tr-TR") === instrumentFilter.toLocaleLowerCase("tr-TR")),
    [instrumentFilter, lessons],
  );
  const weekDays = useMemo(() => Array.from({ length: 7 }, (_, index) => addDays(weekStart, index)), [weekStart]);
  const colors = useMemo(() => buildInstrumentColorMap(lessons.map((lesson) => lesson.instrumentName)), [lessons]);
  const hourWindow = useMemo(() => computeHourWindow(visibleLessons), [visibleLessons]);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="font-serif text-[1.45rem] font-bold italic tracking-[-0.02em] sm:text-[1.7rem]">Takvim</h1>
          <p className="mt-1 text-xs text-[var(--muted)]">{formatDateOnly(weekStart)} – {formatDateOnly(addDays(weekEnd, -1))}</p>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <button onClick={() => setWeekStart((d) => addDays(d, -7))} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Önceki hafta"><Icon name="arrow-left" className="h-4 w-4" /></button>
          <button onClick={() => setWeekStart(startOfWeek(new Date()))} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-[.68rem] font-semibold hover:bg-[var(--surface-muted)]">Bu hafta</button>
          <button onClick={() => setWeekStart((d) => addDays(d, 7))} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Sonraki hafta"><Icon name="arrow-right" className="h-4 w-4" /></button>
          {isAdmin && (
            <button onClick={() => setShowSeriesForm((value) => !value)} className="pressable ml-1 min-h-10 rounded-xl bg-[var(--brand)] px-3.5 text-[.68rem] font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)]">
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

      <div className="app-card flex flex-wrap items-center gap-2 p-3 sm:p-4">
        <span className="mr-1 text-xs font-bold text-[var(--muted)]">Ders türü</span>
        {INSTRUMENT_FILTERS.map((filter) => (
          <button
            key={filter}
            type="button"
            onClick={() => setInstrumentFilter(filter)}
            aria-pressed={instrumentFilter === filter}
            className={`pressable min-h-9 rounded-full border px-3 text-xs font-bold ${instrumentFilter === filter ? "border-[var(--brand)] bg-[var(--brand)] text-white" : "border-[var(--line)] bg-white text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]"}`}
          >
            {filter}
          </button>
        ))}
      </div>

      <WeeklyGrid weekDays={weekDays} lessons={visibleLessons} loading={isLoading} colors={colors} isAdmin={isAdmin} hourWindow={hourWindow} />
    </div>
  );
}

function WeeklyGrid({
  weekDays,
  lessons,
  loading,
  colors,
  isAdmin,
  hourWindow,
}: {
  weekDays: Date[];
  lessons: CalendarLesson[];
  loading: boolean;
  colors: Map<string, InstrumentTone>;
  isAdmin: boolean;
  hourWindow: HourWindow;
}) {
  const windowMinutes = (hourWindow.endHour - hourWindow.startHour) * 60;
  const reschedule = useRescheduleLesson();
  const draggingRef = useRef<CalendarLesson | null>(null);
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [movingId, setMovingId] = useState<string | null>(null);
  const [hoverSlot, setHoverSlot] = useState<{ day: string; minutes: number; label: string; heightPercent: number } | null>(null);
  const [toast, setToast] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const [openLesson, setOpenLesson] = useState<CalendarLesson | null>(null);

  function showToast(tone: "success" | "error", text: string) {
    setToast({ tone, text });
    window.setTimeout(() => setToast((current) => (current?.text === text ? null : current)), 4000);
  }

  function handleDragStart(event: DragEvent<HTMLElement>, lesson: CalendarLesson) {
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
    return Math.round((ratio * windowMinutes) / 15) * 15;
  }

  function handleDragOver(event: DragEvent<HTMLDivElement>, day: Date) {
    if (!draggingRef.current) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    const minutes = minutesFromEvent(event);
    const lesson = draggingRef.current;
    const durationMinutes = Math.max(15, (new Date(lesson.endAt).getTime() - new Date(lesson.startAt).getTime()) / 60000);
    const startOfDayMinutes = hourWindow.startHour * 60 + minutes;
    setHoverSlot({
      day: day.toDateString(),
      minutes,
      label: `${formatMinutesOfDay(startOfDayMinutes)}–${formatMinutesOfDay(startOfDayMinutes + durationMinutes)}`,
      heightPercent: Math.min(100, (durationMinutes / windowMinutes) * 100),
    });
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
    newStart.setMinutes(hourWindow.startHour * 60 + minutes);
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
        <div role="status" className={`flex items-center gap-2 border-b px-4 py-2.5 text-[.68rem] font-semibold ${toast.tone === "success" ? "border-[color:var(--success-soft)] bg-[var(--success-soft)] text-[var(--success-strong)]" : "border-[color:var(--danger-soft)] bg-[var(--danger-soft)] text-[var(--danger-strong)]"}`}>
          <Icon name={toast.tone === "success" ? "check" : "x"} className="h-3.5 w-3.5 shrink-0" />
          {toast.text}
        </div>
      )}

      {/* Masaüstü ızgara görünümü (sürükle-bırak burada aktif) */}
      <div className="hidden overflow-x-auto xl:block">
        <div className="grid min-w-[64rem] grid-cols-[3.4rem_repeat(7,minmax(8rem,1fr))] border-t border-[var(--line)]">
          <div className="border-r border-[var(--line)]" />
          {weekDays.map((day, index) => (
            <div key={day.toISOString()} className={`border-r border-[var(--line)] px-2 py-2.5 text-center last:border-r-0 ${day.toDateString() === new Date().toDateString() ? "bg-[var(--today-tint)]" : ""}`}>
              <span className="block text-[.66rem] font-semibold text-[var(--muted)]">{WEEK_DAYS_TR[index]}</span>
              <span className="mt-1 block text-[.6rem] text-[var(--muted)]">{day.getDate()} {day.toLocaleDateString("tr-TR", { month: "short" })}</span>
            </div>
          ))}
          <GridTimeLabels hourWindow={hourWindow} />
          {weekDays.map((day) => (
            <GridDayColumn
              key={day.toISOString()}
              day={day}
              lessons={lessons}
              colors={colors}
              isAdmin={isAdmin}
              hourWindow={hourWindow}
              draggingId={draggingId}
              movingId={movingId}
              hoverSlot={hoverSlot?.day === day.toDateString() ? hoverSlot : null}
              onDragStartLesson={handleDragStart}
              onDragEndLesson={handleDragEnd}
              onDragOverColumn={(event) => handleDragOver(event, day)}
              onDropColumn={(event) => handleDrop(event, day)}
              onOpenLesson={setOpenLesson}
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
                <span className={`grid h-7 w-7 place-items-center rounded-lg ${day.toDateString() === new Date().toDateString() ? "bg-[var(--brand)] text-white" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{day.getDate()}</span>
                {WEEK_DAYS_TR[index]}
              </h3>
              <div className="space-y-2 pl-9">
                {dayLessons.map((lesson) => <AgendaLessonCard key={lesson.id} lesson={lesson} tone={colors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0]} showTeacher={isAdmin} onOpen={() => setOpenLesson(lesson)} />)}
                {!dayLessons.length && <p className="py-2 text-xs text-[var(--muted)]">Planlanmış ders yok.</p>}
              </div>
            </div>
          );
        })}
      </div>
      {openLesson && <LessonDetailsDialog lesson={openLesson} onClose={() => setOpenLesson(null)} />}
    </section>
  );
}

function GridTimeLabels({ hourWindow }: { hourWindow: HourWindow }) {
  const totalHours = hourWindow.endHour - hourWindow.startHour;
  return (
    <div className="relative border-r border-t border-[var(--line)] bg-[#fdf9f2]" style={{ height: `${totalHours * GRID_HEIGHT_REM_PER_HOUR}rem` }}>
      {Array.from({ length: totalHours + 1 }, (_, index) => (
        <span key={index} className="absolute right-2 -translate-y-1/2 text-[.53rem] tabular-nums text-[var(--muted)]" style={{ top: `${(index / totalHours) * 100}%` }}>
          {String(hourWindow.startHour + index).padStart(2, "0")}:00
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
  hourWindow,
  draggingId,
  movingId,
  hoverSlot,
  onDragStartLesson,
  onDragEndLesson,
  onDragOverColumn,
  onDropColumn,
  onOpenLesson,
}: {
  day: Date;
  lessons: CalendarLesson[];
  colors: Map<string, InstrumentTone>;
  isAdmin: boolean;
  hourWindow: HourWindow;
  draggingId: string | null;
  movingId: string | null;
  hoverSlot: { minutes: number; label: string; heightPercent: number } | null;
  onDragStartLesson: (event: DragEvent<HTMLElement>, lesson: CalendarLesson) => void;
  onDragEndLesson: () => void;
  onDragOverColumn: (event: DragEvent<HTMLDivElement>) => void;
  onDropColumn: (event: DragEvent<HTMLDivElement>) => void;
  onOpenLesson: (lesson: CalendarLesson) => void;
}) {
  const entries = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString());
  const layout = useMemo(() => layoutDayLessons(entries, hourWindow), [entries, hourWindow]);
  const isToday = day.toDateString() === new Date().toDateString();
  const totalHours = hourWindow.endHour - hourWindow.startHour;
  const totalMinutes = totalHours * 60;

  return (
    <div
      onDragOver={onDragOverColumn}
      onDrop={onDropColumn}
      className={`relative border-r border-t border-[var(--line)] last:border-r-0 ${isToday ? "bg-[var(--today-tint-strong)]" : "bg-[#fdf9f2]"} ${hoverSlot ? "outline outline-2 -outline-offset-2 outline-[color:var(--brand)]" : ""}`}
      style={{ height: `${totalHours * GRID_HEIGHT_REM_PER_HOUR}rem` }}
    >
      {Array.from({ length: totalHours - 1 }, (_, index) => (
        <span key={index} className="absolute inset-x-0 border-t border-dashed border-[#f3e4cd]" style={{ top: `${((index + 1) / totalHours) * 100}%` }} />
      ))}

      {/* Sürüklerken bırakılacak saat aralığını gösteren etiket - önceden yalnızca boş, saat
          bilgisi olmayan bir dikdörtgendi, hangi saate bırakılacağı tahmin edilemiyordu
          (kullanıcı geri bildirimi). Yükseklik artık dersin gerçek süresine göre ölçekleniyor. */}
      {hoverSlot && (
        <span
          className="pointer-events-none absolute inset-x-1 z-20 flex items-start justify-center overflow-hidden rounded-md border-2 border-dashed border-[var(--brand)] bg-[var(--brand)]/10 pt-0.5"
          style={{ top: `${(hoverSlot.minutes / totalMinutes) * 100}%`, height: `${hoverSlot.heightPercent}%`, minHeight: "1.6rem" }}
        >
          <span className="rounded-full bg-[var(--brand)] px-2 py-0.5 text-[.62rem] font-bold tabular-nums text-white shadow-sm">{hoverSlot.label}</span>
        </span>
      )}

      {entries.map((lesson) => {
        const start = new Date(lesson.startAt);
        const end = new Date(lesson.endAt);
        const position = layout.get(lesson.id);
        if (!position) return null;
        const tone = colors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0];
        const draggable = isAdmin && lesson.status === "Normal";
        const isCancelled = lesson.status === "Cancelled";
        const gapPct = 1.5;
        const width = `calc(${100 / position.columns}% - ${gapPct}px)`;
        const left = `calc(${(position.column / position.columns) * 100}% + ${gapPct / 2}px)`;
        return (
          <button
            type="button"
            key={lesson.id}
            draggable={draggable}
            onDragStart={(event) => onDragStartLesson(event, lesson)}
            onDragEnd={onDragEndLesson}
            onClick={() => onOpenLesson(lesson)}
            title={`${lesson.studentName} · ${lesson.instrumentName} · ${lesson.teacherName}`}
            aria-label={`${lesson.studentName}, ${lesson.instrumentName}, ${formatTime(start)} - ${formatTime(end)}. Detayları aç`}
            className={`pressable absolute z-10 overflow-hidden rounded-md border-l-[3px] px-2 py-1 text-left shadow-sm transition-opacity ${draggable ? "cursor-grab active:cursor-grabbing" : ""} ${draggingId === lesson.id ? "opacity-35" : "hover:z-20 hover:shadow-md"} ${movingId === lesson.id ? "animate-pulse" : ""} ${isCancelled ? "opacity-55" : ""}`}
            style={{ top: `${position.top * 100}%`, height: `${position.height * 100}%`, left, width, minHeight: "1.85rem", background: tone.bg, borderLeftColor: tone.border, color: tone.text }}
          >
            <span className={`block text-[.52rem] font-bold tabular-nums ${isCancelled ? "line-through" : ""}`}>{formatTime(start)}–{formatTime(end)}</span>
            <span className={`mt-0.5 block truncate text-[.57rem] font-bold ${isCancelled ? "line-through" : ""}`}>{position.columns > 2 ? studentInitials(lesson.studentName) : lesson.studentName}</span>
            <span className="block truncate text-[.46rem] opacity-75">{lesson.instrumentName}{isAdmin ? ` · ${lesson.teacherName}` : ""}</span>
          </button>
        );
      })}
    </div>
  );
}

function AgendaLessonCard({ lesson, tone, showTeacher, onOpen }: { lesson: CalendarLesson; tone: InstrumentTone; showTeacher: boolean; onOpen: () => void }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  return (
    <button type="button" onClick={onOpen} className="pressable flex min-h-14 w-full items-center gap-3 rounded-xl border border-[var(--line)] bg-white p-2.5 text-left shadow-sm hover:border-[var(--brand)]">
      <span className="h-9 w-1 rounded-full" style={{ background: tone.border }} />
      <span className="w-20 shrink-0 text-[.65rem] font-bold tabular-nums" style={{ color: tone.text }}>{formatTime(start)}–{formatTime(end)}</span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-xs font-bold">{lesson.studentName}</span>
        <span className="block truncate text-[.62rem] text-[var(--muted)]">{lesson.instrumentName}{showTeacher ? ` · ${lesson.teacherName}` : ""}</span>
      </span>
      <LessonStatusChip lesson={lesson} />
    </button>
  );
}

function LessonStatusChip({ lesson }: { lesson: CalendarLesson }) {
  const config: Record<CalendarLesson["status"], { label: string; className: string }> = {
    Normal: { label: "Planlandı", className: "bg-[var(--success-soft)] text-[var(--success-strong)]" },
    Rescheduled: { label: "Ertelendi", className: "bg-[var(--warning-soft)] text-[var(--warning-strong)]" },
    Cancelled: { label: "İptal", className: "bg-[var(--danger-soft)] text-[var(--danger-strong)]" },
    Completed: { label: "Tamamlandı", className: "bg-[#e6dcf6] text-[#4b3777]" },
    Makeup: { label: "Telafi", className: "bg-[#e0dbc4] text-[#48521f]" },
  };
  const { label, className } = config[lesson.status];
  return <span className={`shrink-0 rounded-full px-2 py-1 text-[.56rem] font-bold ${className}`}>{label}</span>;
}

function LessonDetailsDialog({ lesson, onClose }: { lesson: CalendarLesson; onClose: () => void }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  const duration = Math.round((end.getTime() - start.getTime()) / 60000);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center p-4" role="dialog" aria-modal="true" aria-label="Ders detayları">
      <button type="button" onClick={onClose} aria-label="Ders detay penceresini kapat" className="absolute inset-0 bg-[#2a1c14]/35 backdrop-blur-[2px]" />
      <section className="app-card relative z-10 w-full max-w-md overflow-hidden">
        <div className="flex items-start justify-between gap-3 border-b border-[var(--line)] bg-[var(--surface-muted)] p-5">
          <div>
            <p className="text-micro text-[var(--brand-strong)]">Ders ayrıntısı</p>
            <h2 className="mt-1 font-serif text-xl font-bold italic">{lesson.studentName}</h2>
            <p className="text-meta mt-1">{lesson.instrumentName} · {lesson.status === "Makeup" ? "Telafi dersi" : "Düzenli ders"}</p>
          </div>
          <button type="button" onClick={onClose} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white text-[var(--muted)]" aria-label="Kapat"><Icon name="close" className="h-4 w-4" /></button>
        </div>
        <dl className="grid gap-3 p-5 sm:grid-cols-2">
          <DetailItem label="Tarih" value={start.toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "long", year: "numeric" })} />
          <DetailItem label="Saat" value={`${formatTime(start)} – ${formatTime(end)}`} />
          <DetailItem label="Süre" value={`${duration} dakika`} />
          <DetailItem label="Öğretmen" value={lesson.teacherName} />
          <DetailItem label="Katılım" value={lesson.rsvpResponse === "Attending" ? "Geliyor" : lesson.rsvpResponse === "AttendingLate" ? "Geç kalacak" : lesson.rsvpResponse === "NotAttending" ? "Gelmiyor" : "Cevap bekleniyor"} />
          <DetailItem label="Durum" value={lesson.status === "Cancelled" ? "İptal edildi" : lesson.status === "Completed" ? "Tamamlandı" : lesson.status === "Makeup" ? "Telafi" : "Planlandı"} />
        </dl>
        <div className="flex justify-end border-t border-[var(--line)] p-4">
          <button type="button" onClick={onClose} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-5 text-sm font-bold text-white">Kapat</button>
        </div>
      </section>
    </div>
  );
}

function DetailItem({ label, value }: { label: string; value: string }) {
  return <div className="rounded-xl border border-[var(--line)] bg-white px-3 py-2.5"><dt className="text-micro text-[var(--muted)]">{label}</dt><dd className="mt-1 text-sm font-semibold">{value}</dd></div>;
}
