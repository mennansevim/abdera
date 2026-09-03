"use client";

import { useEffect, useMemo, useRef, useState, type DragEvent, type FormEvent, type MouseEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useRescheduleLesson } from "@/lib/attendance";
import { useBillingDues } from "@/lib/billing";
import { buildInstrumentColorMap, INSTRUMENT_TONES, type InstrumentTone } from "@/lib/lesson-colors";
import { useEnrollments, useStudents, useTeachers } from "@/lib/people";
import { useCalendar, useUpdateLesson, type CalendarLesson } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import { computeHourWindow, layoutDayLessons, type HourWindow } from "@/lib/week-grid-layout";
import { CreateSeriesForm } from "./create-series-form";
import { MakeupScheduler } from "./makeup-scheduler";

// Saat penceresi ve çakışma yerleşimi artık dashboard önizlemesiyle (dashboard/page.tsx) aynı
// paylaşılan modülden (lib/week-grid-layout.ts) geliyor - sabit 09:00-19:00 önceden iki ekranda
// da ayrı ayrı kopyalanmıştı ve pencere dışı/çakışan dersleri yanlış konumlandırıyordu
// (docs/14-ui-design-prompt.md B3).
const GRID_HEIGHT_REM_PER_HOUR = 3.8;
const WEEK_DAYS_TR = ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"];
const DAY_KEYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const INSTRUMENT_FILTERS = ["Hepsi", "Piyano", "Gitar", "Keman", "Bateri"] as const;
type QuickAddSlot = { date: string; day: string; time: string; x: number; y: number };

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

function formatTime(date: Date) {
  return date.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
}

function dateInputValue(date: Date) {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function timeInputValue(date: Date) {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function dateTimeFromInputs(dateValue: string, timeValue: string) {
  const [year, month, day] = dateValue.split("-").map(Number);
  const [hours, minutes] = timeValue.split(":").map(Number);
  return new Date(year, month - 1, day, hours, minutes, 0, 0);
}

function formatWeekRange(firstDay: Date, lastDay: Date) {
  const firstMonth = firstDay.toLocaleDateString("tr-TR", { month: "long" });
  const lastMonth = lastDay.toLocaleDateString("tr-TR", { month: "long" });
  const year = lastDay.getFullYear();
  return firstMonth === lastMonth
    ? `${firstDay.getDate()}–${lastDay.getDate()} ${lastMonth} ${year}`
    : `${firstDay.getDate()} ${firstMonth}–${lastDay.getDate()} ${lastMonth} ${year}`;
}

function lessonDurationMinutes(lesson: CalendarLesson) {
  return Math.max(0, (new Date(lesson.endAt).getTime() - new Date(lesson.startAt).getTime()) / 60000);
}

function formatLessonTotal(minutes: number) {
  const hours = minutes / 60;
  return `${new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 1 }).format(hours)} saat`;
}

function isLessonActive(lesson: CalendarLesson, now: Date) {
  return (lesson.status === "Normal" || lesson.status === "Makeup")
    && new Date(lesson.startAt).getTime() <= now.getTime()
    && now.getTime() < new Date(lesson.endAt).getTime();
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
  const [now, setNow] = useState(() => new Date());
  const [timelineRange] = useState(() => {
    const from = new Date();
    from.setHours(0, 0, 0, 0);
    return { from, to: addDays(from, 14) };
  });
  const [showSeriesForm, setShowSeriesForm] = useState(false);
  const [quickAddSlot, setQuickAddSlot] = useState<QuickAddSlot | null>(null);
  const [showMakeupScheduler, setShowMakeupScheduler] = useState(false);
  const [instrumentFilter, setInstrumentFilter] = useState<(typeof INSTRUMENT_FILTERS)[number]>("Hepsi");
  const [teacherFilter, setTeacherFilter] = useState("all");
  const [studentFilter, setStudentFilter] = useState("all");

  const weekEnd = useMemo(() => addDays(weekStart, 7), [weekStart]);
  const { data: teachers } = useTeachers();
  const { data: students } = useStudents();
  // Aidat/tahsilat verisi tamamen Admin'e ait (docs/04-permissions.md) - Teacher oturumunda
  // bu istek hiç gönderilmez, yalnızca sonucu (gecikmiş aidat rozeti) Admin görür.
  const { data: dues } = useBillingDues({ enabled: isAdmin });
  const overdueStudentIds = useMemo(
    () => new Set((dues ?? []).filter((due) => due.status === "Overdue").map((due) => due.studentId)),
    [dues],
  );
  const { data: rawLessons, isLoading } = useCalendar(weekStart.toISOString(), weekEnd.toISOString());
  const { data: rawTimelineLessons, isLoading: timelineLoading } = useCalendar(timelineRange.from.toISOString(), timelineRange.to.toISOString());
  // Bir ders ertelendiğinde backend eski kaydı SİLMEZ, `Rescheduled` durumuna çevirip yeni saat
  // için ayrı bir satır açar (denetim izi - CLAUDE.md). Eski kaydı ızgarada göstermek aynı dersin
  // iki yerde birden görünmesine yol açıyordu - değişiklik geçmişi `/dashboard/change-requests`'te
  // zaten var, canlı takvimde tekrar göstermeye gerek yok.
  const lessons = useMemo(() => (rawLessons ?? []).filter((lesson) => lesson.status !== "Rescheduled"), [rawLessons]);
  const timelineLessons = useMemo(() => (rawTimelineLessons ?? []).filter((lesson) => lesson.status !== "Rescheduled"), [rawTimelineLessons]);
  const visibleLessons = useMemo(() => lessons.filter((lesson) => {
    const matchesInstrument = instrumentFilter === "Hepsi" || lesson.instrumentName.toLocaleLowerCase("tr-TR") === instrumentFilter.toLocaleLowerCase("tr-TR");
    const matchesTeacher = teacherFilter === "all" || lesson.teacherId === teacherFilter;
    const matchesStudent = studentFilter === "all" || lesson.studentId === studentFilter;
    return matchesInstrument && matchesTeacher && matchesStudent;
  }), [instrumentFilter, lessons, studentFilter, teacherFilter]);
  const visibleTimelineLessons = useMemo(() => timelineLessons.filter((lesson) => {
    const matchesInstrument = instrumentFilter === "Hepsi" || lesson.instrumentName.toLocaleLowerCase("tr-TR") === instrumentFilter.toLocaleLowerCase("tr-TR");
    const matchesTeacher = teacherFilter === "all" || lesson.teacherId === teacherFilter;
    const matchesStudent = studentFilter === "all" || lesson.studentId === studentFilter;
    return matchesInstrument && matchesTeacher && matchesStudent;
  }), [instrumentFilter, studentFilter, teacherFilter, timelineLessons]);
  const weekDays = useMemo(() => Array.from({ length: 7 }, (_, index) => addDays(weekStart, index)), [weekStart]);
  const colors = useMemo(() => buildInstrumentColorMap([...lessons, ...timelineLessons].map((lesson) => lesson.instrumentName)), [lessons, timelineLessons]);
  const hourWindow = useMemo(() => computeHourWindow(visibleLessons), [visibleLessons]);
  const totalMinutes = useMemo(() => visibleLessons.reduce((total, lesson) => total + lessonDurationMinutes(lesson), 0), [visibleLessons]);

  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 30_000);
    return () => window.clearInterval(timer);
  }, []);

  return (
    <div className="space-y-4">
      <header className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
        <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
          <h1 className="font-serif text-[1.45rem] font-bold italic tracking-[-0.02em] sm:text-[1.7rem]">Ders Programı</h1>
          <p className="text-xs font-semibold text-[var(--muted)]">{visibleLessons.length} ders · {formatLessonTotal(totalMinutes)}</p>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <button onClick={() => setWeekStart((d) => addDays(d, -7))} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Önceki hafta"><Icon name="arrow-left" className="h-4 w-4" /></button>
          <button onClick={() => setWeekStart((d) => addDays(d, 7))} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Sonraki hafta"><Icon name="arrow-right" className="h-4 w-4" /></button>
          <button onClick={() => setWeekStart(startOfWeek(new Date()))} className="pressable ml-1 min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-[.68rem] font-semibold hover:bg-[var(--surface-muted)]">Bugün</button>
          <span className="ml-1 inline-flex min-h-10 items-center gap-2 rounded-xl bg-white/55 px-2.5 text-xs font-bold tabular-nums text-[#5c4d3f]">
            <Icon name="calendar" className="h-4 w-4 text-[var(--brand)]" />
            {formatWeekRange(weekStart, addDays(weekEnd, -1))}
          </span>
          {isAdmin && (
            <><button onClick={() => { setShowMakeupScheduler((value) => !value); setShowSeriesForm(false); setQuickAddSlot(null); }} className={`pressable ml-1 min-h-10 rounded-xl border px-3.5 text-[.68rem] font-bold ${showMakeupScheduler ? "border-[#6d559b] bg-[#6d559b] text-white" : "border-[#cfc2e4] bg-white text-[#5c477f] hover:bg-[#eee8f8]"}`}>
              {showMakeupScheduler ? "Telafi asistanını kapat" : "Telafi planla"}
            </button><button onClick={() => { setShowSeriesForm((value) => !value); setShowMakeupScheduler(false); setQuickAddSlot(null); }} className="pressable min-h-10 rounded-xl bg-[var(--brand)] px-3.5 text-[.68rem] font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)]">
              {showSeriesForm ? "Zamanlayıcıyı kapat" : "+ Yeni ders"}
            </button></>
          )}
        </div>
      </header>

      {isAdmin && showSeriesForm && !quickAddSlot && (
        <section className="app-card p-4">
          <CreateSeriesForm
            key="manual"
            onCreated={() => { setShowSeriesForm(false); setQuickAddSlot(null); }}
          />
        </section>
      )}

      {isAdmin && quickAddSlot && (
        <QuickAddLessonPopover
          slot={quickAddSlot}
          onClose={() => { setQuickAddSlot(null); setShowSeriesForm(false); }}
        />
      )}

      {isAdmin && showMakeupScheduler && <section className="app-card p-4 sm:p-5"><MakeupScheduler /></section>}

      {isAdmin && (
        <p className="hidden items-center gap-1.5 text-[.65rem] text-[var(--muted)] xl:flex">
          <Icon name="swap" className="h-3.5 w-3.5" /> İpucu: boş bir alana çift tıklayarak o gün ve saati hazır gelen yeni ders formunu açabilirsin; ders kartını sürükleyerek de taşıyabilirsin.
        </p>
      )}

      <div className="app-card flex flex-wrap items-center justify-between gap-3 p-3 sm:px-4">
        <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
          {/* Öğrenci listesinden seçip yalnızca o öğrencinin derslerini görmek için - diğer
              filtrelerle AND mantığıyla birlikte çalışır (aynı desen: öğretmen + enstrüman). */}
          <label className="relative min-w-48"><span className="sr-only">Öğrenciye göre filtrele</span><Icon name="students" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--brand)]" /><select value={studentFilter} onChange={(event) => setStudentFilter(event.target.value)} className="field min-h-9 bg-white py-1 pl-9 pr-8 text-xs font-bold"><option value="all">Tüm öğrenciler</option>{students?.filter((student) => student.status === "Active").map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}</select></label>
          <label className="relative min-w-48"><span className="sr-only">Öğretmene göre filtrele</span><Icon name="teachers" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--brand)]" /><select value={teacherFilter} onChange={(event) => setTeacherFilter(event.target.value)} className="field min-h-9 bg-white py-1 pl-9 pr-8 text-xs font-bold"><option value="all">Tüm öğretmenler</option>{teachers?.filter((teacher) => teacher.status === "Active").map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>)}</select></label>
          <div className="h-6 w-px bg-[var(--line)] max-sm:hidden" aria-hidden="true" />
          <div className="flex gap-1.5 overflow-x-auto" aria-label="Enstrümana göre filtrele">{INSTRUMENT_FILTERS.map((filter) => (
              <button
                key={filter}
                type="button"
                onClick={() => setInstrumentFilter(filter)}
                aria-pressed={instrumentFilter === filter}
                className={`pressable min-h-9 shrink-0 rounded-xl border px-3.5 text-xs font-bold ${instrumentFilter === filter ? "border-[var(--brand)] bg-[var(--brand)] text-white shadow-[0_5px_12px_rgba(217,102,42,.2)]" : "border-[var(--line)] bg-white text-[#5c4d3f] hover:border-[var(--brand)] hover:text-[var(--brand)]"}`}
              >
                {filter}
              </button>
            ))}</div>
        </div>
        <div className="hidden flex-wrap items-center justify-end gap-3 lg:flex" aria-label="Enstrüman renkleri">
          {[...colors.entries()].slice(0, 5).map(([instrument, tone]) => (
            <span key={instrument} className="inline-flex items-center gap-1.5 text-[.65rem] font-semibold text-[var(--muted)]">
              <span className="h-2.5 w-2.5 rounded-full" style={{ background: tone.border }} />{instrument}
            </span>
          ))}
        </div>
      </div>

      <div className="grid items-start gap-4 2xl:grid-cols-[minmax(0,1fr)_17.5rem]">
        <WeeklyGrid weekDays={weekDays} lessons={visibleLessons} loading={isLoading} colors={colors} isAdmin={isAdmin} hourWindow={hourWindow} now={now} overdueStudentIds={overdueStudentIds} onDoubleClickSlot={(slot) => { setQuickAddSlot(slot); setShowMakeupScheduler(false); setShowSeriesForm(false); }} />
        <UpcomingLessonsRail lessons={visibleTimelineLessons} colors={colors} now={now} loading={timelineLoading} onOpenWeek={() => setWeekStart(startOfWeek(new Date()))} />
      </div>
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
  now,
  overdueStudentIds,
  onDoubleClickSlot,
}: {
  weekDays: Date[];
  lessons: CalendarLesson[];
  loading: boolean;
  colors: Map<string, InstrumentTone>;
  isAdmin: boolean;
  hourWindow: HourWindow;
  now: Date;
  overdueStudentIds: Set<string>;
  onDoubleClickSlot: (slot: QuickAddSlot) => void;
}) {
  const windowMinutes = (hourWindow.endHour - hourWindow.startHour) * 60;
  const reschedule = useRescheduleLesson();
  const draggingRef = useRef<CalendarLesson | null>(null);
  const transparentDragImageRef = useRef<HTMLCanvasElement | null>(null);
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [dragPreview, setDragPreview] = useState<{ x: number; y: number; lesson: CalendarLesson; label: string } | null>(null);
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
    // Tarayıcının sabit kalan yerel hayalet görüntüsü yerine hedef saati anlık güncellenebilen
    // React önizlemesi kullanılır. 1x1 şeffaf tuval yalnızca yerel hayaleti gizler.
    const transparentImage = document.createElement("canvas");
    transparentImage.width = 1;
    transparentImage.height = 1;
    transparentImage.style.position = "fixed";
    transparentImage.style.left = "-2px";
    transparentImage.style.top = "-2px";
    document.body.appendChild(transparentImage);
    transparentDragImageRef.current = transparentImage;
    event.dataTransfer.setDragImage(transparentImage, 0, 0);
    setDragPreview({
      x: event.clientX,
      y: event.clientY,
      lesson,
      label: `${formatTime(new Date(lesson.startAt))}–${formatTime(new Date(lesson.endAt))}`,
    });
  }

  function handleDragEnd() {
    transparentDragImageRef.current?.remove();
    transparentDragImageRef.current = null;
    draggingRef.current = null;
    setDraggingId(null);
    setHoverSlot(null);
    setDragPreview(null);
  }

  function minutesFromEvent(event: DragEvent<HTMLDivElement>) {
    const rect = event.currentTarget.getBoundingClientRect();
    const ratio = Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height));
    return Math.round((ratio * windowMinutes) / 15) * 15;
  }

  function handleDoubleClick(event: MouseEvent<HTMLDivElement>, day: Date) {
    if (!isAdmin) return;
    const rect = event.currentTarget.getBoundingClientRect();
    const ratio = Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height));
    const minutes = Math.round((ratio * windowMinutes) / 15) * 15;
    const start = new Date(day);
    start.setHours(0, 0, 0, 0);
    start.setMinutes(hourWindow.startHour * 60 + minutes);
    if (start.getTime() < now.getTime()) {
      showToast("error", "Geçmiş bir tarih veya saate ders eklenemez.");
      return;
    }
    onDoubleClickSlot({
      date: dateInputValue(start),
      day: DAY_KEYS[start.getDay()]!,
      time: formatMinutesOfDay(start.getHours() * 60 + start.getMinutes()),
      x: event.clientX,
      y: event.clientY,
    });
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
    setDragPreview({
      x: event.clientX,
      y: event.clientY,
      lesson,
      label: `${formatMinutesOfDay(startOfDayMinutes)}–${formatMinutesOfDay(startOfDayMinutes + durationMinutes)}`,
    });
  }

  async function handleDrop(event: DragEvent<HTMLDivElement>, day: Date) {
    event.preventDefault();
    const lesson = draggingRef.current;
    transparentDragImageRef.current?.remove();
    transparentDragImageRef.current = null;
    draggingRef.current = null;
    setDraggingId(null);
    setHoverSlot(null);
    setDragPreview(null);
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

      {dragPreview && <FloatingDragPreview preview={dragPreview} tone={colors.get(dragPreview.lesson.instrumentName) ?? INSTRUMENT_TONES[0]} />}

      {/* Masaüstü ızgara görünümü (sürükle-bırak burada aktif) */}
      <div className="hidden overflow-x-auto xl:block">
        <div className="relative grid min-w-[56rem] grid-cols-[3.4rem_repeat(7,minmax(7.5rem,1fr))] grid-rows-[3.9rem_auto]">
          <div className="border-r border-[var(--line)]" />
          {weekDays.map((day, index) => (
            <div key={day.toISOString()} className={`border-r border-[var(--line)] px-2 py-2.5 text-center last:border-r-0 ${day.toDateString() === new Date().toDateString() ? "bg-[var(--today-tint)]" : ""}`}>
              <span className="block text-[.66rem] font-semibold text-[var(--muted)]">{WEEK_DAYS_TR[index]}</span>
              <span className="mt-1 block text-[.6rem] text-[var(--muted)]">{day.getDate()} {day.toLocaleDateString("tr-TR", { month: "short" })}</span>
            </div>
          ))}
          <GridTimeLabels hourWindow={hourWindow} dragging={!!draggingId} />
          {weekDays.map((day) => (
            <GridDayColumn
              key={day.toISOString()}
              day={day}
              lessons={lessons}
              colors={colors}
              isAdmin={isAdmin}
              hourWindow={hourWindow}
              overdueStudentIds={overdueStudentIds}
              draggingId={draggingId}
              movingId={movingId}
              hoverSlot={hoverSlot?.day === day.toDateString() ? hoverSlot : null}
              onDragStartLesson={handleDragStart}
              onDragEndLesson={handleDragEnd}
              onDragOverColumn={(event) => handleDragOver(event, day)}
              onDropColumn={(event) => handleDrop(event, day)}
              onDoubleClickColumn={(event) => handleDoubleClick(event, day)}
              onOpenLesson={setOpenLesson}
              now={now}
            />
          ))}
          <CurrentTimeLine weekDays={weekDays} hourWindow={hourWindow} now={now} />
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
                {dayLessons.map((lesson) => <AgendaLessonCard key={lesson.id} lesson={lesson} tone={colors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0]} showTeacher={isAdmin} active={isLessonActive(lesson, now)} overdue={overdueStudentIds.has(lesson.studentId)} onOpen={() => setOpenLesson(lesson)} />)}
                {!dayLessons.length && <p className="py-2 text-xs text-[var(--muted)]">Planlanmış ders yok.</p>}
              </div>
            </div>
          );
        })}
      </div>
      {openLesson && <LessonDetailsDialog lesson={openLesson} isAdmin={isAdmin} now={now} onUpdated={() => showToast("success", "Ders ayrıntıları güncellendi.")} onClose={() => setOpenLesson(null)} />}
    </section>
  );
}

function FloatingDragPreview({ preview, tone }: { preview: { x: number; y: number; lesson: CalendarLesson; label: string }; tone: InstrumentTone }) {
  return <div aria-hidden="true" className="pointer-events-none fixed z-[70] w-44 -translate-x-1/2 -translate-y-[calc(100%+.8rem)] overflow-hidden rounded-xl border-l-4 px-3 py-2.5 text-left shadow-[0_16px_40px_rgba(58,42,31,.25)]" style={{ left: preview.x, top: preview.y, background: tone.bg, borderLeftColor: tone.border, color: tone.text }}><span className="block rounded-lg bg-white/85 px-2 py-1 text-center text-xs font-extrabold tabular-nums shadow-sm">{preview.label}</span><span className="mt-2 block truncate text-xs font-bold">{preview.lesson.studentName}</span><span className="mt-0.5 block truncate text-[.62rem] opacity-75">{preview.lesson.instrumentName} · {preview.lesson.teacherName}</span></div>;
}

function QuickAddLessonPopover({ slot, onClose }: { slot: QuickAddSlot; onClose: () => void }) {
  // Popup, çift tıklanan hücrenin yakınında açılır; ekran kenarına taşarsa içeri doğru kayar.
  // Bu bileşen yalnızca kullanıcı etkileşiminden sonra oluşturulduğu için viewport ölçüsü
  // burada güvenle okunabilir (ilk SSR çıktısında popup yoktur).
  const viewportWidth = typeof window === "undefined" ? 1200 : window.innerWidth;
  const viewportHeight = typeof window === "undefined" ? 800 : window.innerHeight;
  const panelWidth = Math.min(520, viewportWidth - 24);
  const panelHeight = Math.min(700, viewportHeight - 24);
  const position = {
    left: Math.min(Math.max(12, slot.x - panelWidth / 2), viewportWidth - panelWidth - 12),
    top: Math.min(Math.max(12, slot.y + 14), viewportHeight - panelHeight - 12),
  };
  const slotDate = new Date(`${slot.date}T12:00:00`);
  const slotLabel = `${slotDate.toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric" })} · ${WEEK_DAYS_TR[(slotDate.getDay() + 6) % 7]} · ${slot.time}`;

  return (
    <div className="fixed inset-0 z-[60]" role="presentation" onMouseDown={onClose}>
      <button type="button" onClick={onClose} className="absolute inset-0 bg-[#2a1c14]/20 backdrop-blur-[1px]" aria-label="Yeni ders penceresini kapat" />
      <section
        role="dialog"
        aria-modal="true"
        aria-label="Yeni ders oluştur"
        onMouseDown={(event) => event.stopPropagation()}
        className="absolute w-[min(32.5rem,calc(100vw-1.5rem))] max-h-[calc(100vh-1.5rem)] overflow-hidden rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-[0_24px_70px_rgba(52,35,24,.28)]"
        style={{ left: position.left, top: position.top }}
      >
        <div className="flex items-start justify-between gap-3 border-b border-[var(--line)] bg-[var(--surface-muted)] px-4 py-3">
          <div>
            <p className="text-micro text-[var(--brand-strong)]">Takvimden hızlı ekle</p>
            <h2 className="mt-1 text-base font-extrabold">Yeni ders oluştur</h2>
            <p className="mt-1 text-[.68rem] font-semibold text-[var(--muted)]">{slotLabel}</p>
          </div>
          <button type="button" onClick={onClose} className="pressable grid h-9 w-9 shrink-0 place-items-center rounded-lg border border-[var(--line)] bg-white text-[var(--muted)]" aria-label="Kapat"><Icon name="close" className="h-4 w-4" /></button>
        </div>
        <div className="max-h-[calc(100vh-7rem)] overflow-y-auto p-4">
          <CreateSeriesForm
            initialDate={slot.date}
            initialDay={slot.day}
            initialTime={slot.time}
            onCreated={onClose}
          />
        </div>
      </section>
    </div>
  );
}

function GridTimeLabels({ hourWindow, dragging }: { hourWindow: HourWindow; dragging: boolean }) {
  const totalHours = hourWindow.endHour - hourWindow.startHour;
  return (
    <div className={`sticky left-0 border-r border-t border-[var(--line)] bg-[#fdf9f2] ${dragging ? "z-30 shadow-[8px_0_18px_rgba(80,48,24,.09)]" : "z-10"}`} style={{ height: `${totalHours * GRID_HEIGHT_REM_PER_HOUR}rem` }}>
      {Array.from({ length: totalHours + 1 }, (_, index) => (
        <span key={index} className={`absolute right-2 -translate-y-1/2 rounded px-1 text-[.53rem] tabular-nums ${dragging ? "bg-white font-bold text-[var(--foreground)]" : "text-[var(--muted)]"}`} style={{ top: `${(index / totalHours) * 100}%` }}>
          {String(hourWindow.startHour + index).padStart(2, "0")}:00
        </span>
      ))}
    </div>
  );
}

function CurrentTimeLine({ weekDays, hourWindow, now }: { weekDays: Date[]; hourWindow: HourWindow; now: Date }) {
  const todayInWeek = weekDays.some((day) => day.toDateString() === now.toDateString());
  const minutesFromWindowStart = now.getHours() * 60 + now.getMinutes() - hourWindow.startHour * 60;
  const windowMinutes = (hourWindow.endHour - hourWindow.startHour) * 60;
  if (!todayInWeek || minutesFromWindowStart < 0 || minutesFromWindowStart > windowMinutes) return null;

  const offsetRem = (minutesFromWindowStart / 60) * GRID_HEIGHT_REM_PER_HOUR;
  return (
    <div
      className="pointer-events-none absolute inset-x-0 z-30 flex -translate-y-1/2 items-center"
      style={{ top: `calc(3.9rem + ${offsetRem}rem)` }}
      aria-label={`Şu an saat ${formatTime(now)}`}
    >
      <span className="w-[3.4rem] shrink-0 rounded-r-md bg-[var(--brand)] py-1 pr-2 text-right text-[.6rem] font-extrabold tabular-nums text-white shadow-sm">{formatTime(now)}</span>
      <span className="h-2 w-2 -translate-x-1/2 rounded-full bg-[var(--brand)] ring-2 ring-white" />
      <span className="h-px flex-1 -translate-x-1 bg-[var(--brand)] shadow-[0_0_0_1px_rgba(217,102,42,.08)]" />
    </div>
  );
}

function GridDayColumn({
  day,
  lessons,
  colors,
  isAdmin,
  hourWindow,
  overdueStudentIds,
  draggingId,
  movingId,
  hoverSlot,
  onDragStartLesson,
  onDragEndLesson,
  onDragOverColumn,
  onDropColumn,
  onDoubleClickColumn,
  onOpenLesson,
  now,
}: {
  day: Date;
  lessons: CalendarLesson[];
  colors: Map<string, InstrumentTone>;
  isAdmin: boolean;
  hourWindow: HourWindow;
  overdueStudentIds: Set<string>;
  draggingId: string | null;
  movingId: string | null;
  hoverSlot: { minutes: number; label: string; heightPercent: number } | null;
  onDragStartLesson: (event: DragEvent<HTMLElement>, lesson: CalendarLesson) => void;
  onDragEndLesson: () => void;
  onDragOverColumn: (event: DragEvent<HTMLDivElement>) => void;
  onDropColumn: (event: DragEvent<HTMLDivElement>) => void;
  onDoubleClickColumn: (event: MouseEvent<HTMLDivElement>) => void;
  onOpenLesson: (lesson: CalendarLesson) => void;
  now: Date;
}) {
  const entries = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString());
  const layout = useMemo(() => layoutDayLessons(entries, hourWindow), [entries, hourWindow]);
  const isToday = day.toDateString() === new Date().toDateString();
  const totalHours = hourWindow.endHour - hourWindow.startHour;
  const totalMinutes = totalHours * 60;

  return (
    <div
      data-testid={`calendar-day-${dateInputValue(day)}`}
      data-start-hour={hourWindow.startHour}
      data-end-hour={hourWindow.endHour}
      onDragOver={onDragOverColumn}
      onDrop={onDropColumn}
      onDoubleClick={onDoubleClickColumn}
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
        const active = isLessonActive(lesson, now);
        const overdue = overdueStudentIds.has(lesson.studentId);
        const gapPct = 1.5;
        const width = `calc(${100 / position.columns}% - ${gapPct}px)`;
        const left = `calc(${(position.column / position.columns) * 100}% + ${gapPct / 2}px)`;
        return (
          <button
            type="button"
            key={lesson.id}
            data-testid={`calendar-lesson-${lesson.id}`}
            draggable={draggable}
            onDragStart={(event) => onDragStartLesson(event, lesson)}
            onDragEnd={onDragEndLesson}
            onClick={() => onOpenLesson(lesson)}
            onDoubleClick={(event) => event.stopPropagation()}
            title={`${lesson.studentName} · ${lesson.instrumentName} · ${lesson.teacherName}${overdue ? " · Aidat gecikmiş" : ""}`}
            aria-label={`${lesson.studentName}, ${lesson.instrumentName}, ${formatTime(start)} - ${formatTime(end)}${overdue ? ", aidat gecikmiş" : ""}. Detayları aç`}
            className={`pressable absolute z-10 overflow-hidden rounded-md border-l-[3px] px-2 py-1 text-left shadow-sm transition-opacity ${draggable ? "cursor-grab active:cursor-grabbing" : ""} ${draggingId === lesson.id ? "opacity-20" : "hover:z-20 hover:shadow-md"} ${movingId === lesson.id ? "animate-pulse" : ""} ${isCancelled ? "opacity-55" : ""} ${active ? "ring-2 ring-[var(--brand)] ring-offset-1" : ""}`}
            style={{ top: `${position.top * 100}%`, height: `${position.height * 100}%`, left, width, minHeight: "1.85rem", background: tone.bg, borderLeftColor: tone.border, color: tone.text }}
          >
            {/* Yalnızca Admin oturumunda dolu gelir (overdueStudentIds) - Teacher'a mali veri
                sızmaz, çünkü hook Teacher için hiç istek atmıyor (docs/04-permissions.md). */}
            {overdue && <span className="absolute right-1 top-1 z-10 grid h-3.5 w-3.5 place-items-center rounded-full bg-[var(--danger)] text-white shadow-sm" aria-hidden="true"><Icon name="alert-triangle" className="h-2.5 w-2.5" strokeWidth={2.6} /></span>}
            <span className="flex items-center justify-between gap-1">
              <span className={`block text-[.52rem] font-bold tabular-nums ${isCancelled ? "line-through" : ""}`}>{formatTime(start)}–{formatTime(end)}</span>
              {active && <span className="rounded-full bg-[var(--brand)] px-1.5 py-0.5 text-[.43rem] font-extrabold uppercase tracking-wide text-white">Şimdi</span>}
            </span>
            <span className={`mt-0.5 block truncate text-[.57rem] font-bold ${isCancelled ? "line-through" : ""}`}>{position.columns > 2 ? studentInitials(lesson.studentName) : lesson.studentName}</span>
            <span className="block truncate text-[.46rem] opacity-75">{lesson.instrumentName}{isAdmin ? ` · ${lesson.teacherName}` : ""}</span>
          </button>
        );
      })}
    </div>
  );
}

function AgendaLessonCard({ lesson, tone, showTeacher, active = false, overdue = false, onOpen }: { lesson: CalendarLesson; tone: InstrumentTone; showTeacher: boolean; active?: boolean; overdue?: boolean; onOpen: () => void }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  return (
    <button type="button" onClick={onOpen} title={overdue ? "Aidat gecikmiş" : undefined} className={`pressable flex min-h-14 w-full items-center gap-3 rounded-xl border bg-white p-2.5 text-left shadow-sm hover:border-[var(--brand)] ${active ? "border-[var(--brand)] ring-2 ring-[var(--brand)]/15" : "border-[var(--line)]"}`}>
      <span className="h-9 w-1 rounded-full" style={{ background: tone.border }} />
      <span className="w-20 shrink-0 text-[.65rem] font-bold tabular-nums" style={{ color: tone.text }}>{formatTime(start)}–{formatTime(end)}</span>
      <span className="min-w-0 flex-1">
        <span className="flex items-center gap-2">
          <span className="block truncate text-xs font-bold">{lesson.studentName}</span>
          {overdue && <Icon name="alert-triangle" className="h-3.5 w-3.5 shrink-0 text-[var(--danger-strong)]" />}
          {active && <span className="shrink-0 rounded-full bg-[var(--brand)] px-2 py-0.5 text-[.5rem] font-extrabold uppercase text-white">Şimdi</span>}
        </span>
        <span className="block truncate text-[.62rem] text-[var(--muted)]">{lesson.instrumentName}{showTeacher ? ` · ${lesson.teacherName}` : ""}</span>
      </span>
      <LessonStatusChip lesson={lesson} />
    </button>
  );
}

function UpcomingLessonsRail({
  lessons,
  colors,
  now,
  loading,
  onOpenWeek,
}: {
  lessons: CalendarLesson[];
  colors: Map<string, InstrumentTone>;
  now: Date;
  loading: boolean;
  onOpenWeek: () => void;
}) {
  const activeLesson = lessons.find((lesson) => isLessonActive(lesson, now));
  const upcomingLessons = lessons
    .filter((lesson) => (lesson.status === "Normal" || lesson.status === "Makeup") && new Date(lesson.startAt).getTime() > now.getTime())
    .sort((a, b) => a.startAt.localeCompare(b.startAt))
    .slice(0, 5);

  return (
    <aside className="app-card overflow-hidden 2xl:sticky 2xl:top-5" aria-label="Canlı ders akışı">
      <div className="flex items-center justify-between border-b border-[var(--line)] px-4 py-4">
        <div>
          <p className="text-micro text-[var(--brand-strong)]">Canlı akış</p>
          <h2 className="text-title mt-1">Şimdi ve yaklaşanlar</h2>
        </div>
        <span className="relative flex h-3 w-3" aria-hidden="true">
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-[var(--brand)] opacity-30" />
          <span className="relative inline-flex h-3 w-3 rounded-full bg-[var(--brand)]" />
        </span>
      </div>

      <div className="p-3">
        {loading ? (
          <div className="space-y-3">
            <div className="skeleton h-32 rounded-xl" />
            {Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-16 rounded-xl" />)}
          </div>
        ) : (
          <>
            {activeLesson ? <ActiveLessonCard lesson={activeLesson} tone={colors.get(activeLesson.instrumentName) ?? INSTRUMENT_TONES[0]} now={now} /> : (
              <div className="rounded-xl border border-dashed border-[#e7cbaa] bg-[#fffaf4] px-3 py-4 text-center">
                <span className="mx-auto grid h-9 w-9 place-items-center rounded-full bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="clock" className="h-4 w-4" /></span>
                <p className="mt-2 text-xs font-bold">Şu anda ders yok</p>
                <p className="mt-1 text-[.62rem] leading-relaxed text-[var(--muted)]">Sıradaki ders başladığında burada canlı olarak vurgulanacak.</p>
              </div>
            )}

            <div className="mb-2 mt-4 flex items-center justify-between px-1">
              <h3 className="text-xs font-extrabold">Yaklaşan dersler</h3>
              <span className="rounded-full bg-[var(--surface-muted)] px-2 py-1 text-[.55rem] font-bold text-[var(--muted)]">{upcomingLessons.length}</span>
            </div>

            <div className="space-y-1.5">
              {upcomingLessons.map((lesson, index) => (
                <UpcomingLessonItem key={lesson.id} lesson={lesson} tone={colors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[index % INSTRUMENT_TONES.length]} now={now} first={index === 0} />
              ))}
              {!upcomingLessons.length && <p className="rounded-xl bg-[var(--surface-muted)] px-3 py-4 text-center text-xs text-[var(--muted)]">Önümüzdeki 14 günde planlanmış ders yok.</p>}
            </div>
          </>
        )}
      </div>

      <button type="button" onClick={onOpenWeek} className="pressable flex min-h-11 w-full items-center justify-center gap-2 border-t border-[var(--line)] bg-[#fffaf4] text-xs font-bold text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">
        <Icon name="calendar" className="h-3.5 w-3.5" /> Bugünün haftasına dön
      </button>
    </aside>
  );
}

function ActiveLessonCard({ lesson, tone, now }: { lesson: CalendarLesson; tone: InstrumentTone; now: Date }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  const duration = Math.max(1, end.getTime() - start.getTime());
  const progress = Math.min(100, Math.max(0, ((now.getTime() - start.getTime()) / duration) * 100));
  const remaining = Math.max(1, Math.ceil((end.getTime() - now.getTime()) / 60000));

  return (
    <article className="overflow-hidden rounded-xl border shadow-sm" style={{ borderColor: tone.border, background: tone.bg, color: tone.text }}>
      <div className="p-3.5">
        <div className="flex items-center justify-between gap-2">
          <span className="rounded-full bg-white/75 px-2 py-1 text-[.52rem] font-extrabold uppercase tracking-[.08em]">Şu an derste</span>
          <span className="text-[.62rem] font-bold tabular-nums">{formatTime(start)}–{formatTime(end)}</span>
        </div>
        <h3 className="mt-3 text-sm font-extrabold">{lesson.studentName}</h3>
        <p className="mt-1 text-[.65rem] font-semibold opacity-75">{lesson.instrumentName} · {lesson.teacherName}</p>
        <div className="mt-3 h-1.5 overflow-hidden rounded-full bg-white/65">
          <span className="block h-full rounded-full transition-[width] duration-500" style={{ width: `${progress}%`, background: tone.border }} />
        </div>
        <p className="mt-1.5 text-right text-[.55rem] font-bold opacity-70">Yaklaşık {remaining} dk kaldı</p>
      </div>
    </article>
  );
}

function UpcomingLessonItem({ lesson, tone, now, first }: { lesson: CalendarLesson; tone: InstrumentTone; now: Date; first: boolean }) {
  const start = new Date(lesson.startAt);
  const tomorrow = addDays(now, 1);
  const dayLabel = start.toDateString() === now.toDateString()
    ? "Bugün"
    : start.toDateString() === tomorrow.toDateString()
      ? "Yarın"
      : start.toLocaleDateString("tr-TR", { weekday: "short", day: "numeric", month: "short" });

  return (
    <article className={`flex items-center gap-2.5 rounded-xl border p-2.5 ${first ? "border-[var(--line)] bg-white shadow-sm" : "border-transparent bg-[#fffaf4]"}`}>
      <span className="h-10 w-1 shrink-0 rounded-full" style={{ background: tone.border }} />
      <span className="w-12 shrink-0">
        <span className="block text-[.53rem] font-bold text-[var(--muted)]">{dayLabel}</span>
        <span className="mt-0.5 block text-xs font-extrabold tabular-nums">{formatTime(start)}</span>
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-[.68rem] font-extrabold">{lesson.studentName}</span>
        <span className="mt-0.5 block truncate text-[.57rem] text-[var(--muted)]">{lesson.instrumentName} · {lesson.teacherName}</span>
      </span>
      {first && <Icon name="chevron" className="h-3.5 w-3.5 shrink-0 text-[var(--brand)]" />}
    </article>
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

function LessonDetailsDialog({ lesson, isAdmin, now, onUpdated, onClose }: { lesson: CalendarLesson; isAdmin: boolean; now: Date; onUpdated: () => void; onClose: () => void }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  const duration = Math.round((end.getTime() - start.getTime()) / 60000);
  const updateLesson = useUpdateLesson();
  const { data: students } = useStudents();
  const { data: teachers } = useTeachers();
  const [editing, setEditing] = useState(false);
  const [studentId, setStudentId] = useState(lesson.studentId);
  const [teacherId, setTeacherId] = useState(lesson.teacherId);
  const [statusValue, setStatusValue] = useState<"Normal" | "Cancelled">("Normal");
  const [dateValue, setDateValue] = useState(() => dateInputValue(start));
  const [timeValue, setTimeValue] = useState(() => timeInputValue(start));
  const [durationValue, setDurationValue] = useState(() => String(duration));
  const [error, setError] = useState<string | null>(null);
  const { data: enrollments } = useEnrollments(studentId);
  const eligibleEnrollments = enrollments?.filter((item) => item.status === "Active" && item.instrumentId === lesson.instrumentId) ?? [];
  const eligibleTeacherIds = new Set(eligibleEnrollments.map((item) => item.teacherId));
  const eligibleTeachers = teachers?.filter((teacher) => teacher.status === "Active" && eligibleTeacherIds.has(teacher.id)) ?? [];
  const canEdit = isAdmin && lesson.status === "Normal" && start.getTime() >= now.getTime();

  async function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    const minutes = Number(durationValue);
    const nextStart = dateTimeFromInputs(dateValue, timeValue);
    if (!Number.isFinite(nextStart.getTime()) || !Number.isInteger(minutes) || minutes < 15 || minutes > 180) {
      setError("Geçerli bir tarih, saat ve 15–180 dakika arasında bir süre girin.");
      return;
    }
    if (nextStart.getTime() < Date.now()) {
      setError("Geçmiş bir tarih veya saate ders planlanamaz.");
      return;
    }
    try {
      await updateLesson.mutateAsync({
        lessonId: lesson.id,
        studentId,
        teacherId,
        startAt: nextStart.toISOString(),
        durationMinutes: minutes,
        status: statusValue,
      });
      onUpdated();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ders güncellenemedi.");
    }
  }

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
        {editing ? (
          <form onSubmit={handleSave} className="border-t border-[var(--line)] bg-[var(--surface-muted)] p-5">
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="text-micro text-[var(--muted)]">Öğrenci<select value={studentId} onChange={(event) => { const nextStudentId = event.target.value; setStudentId(nextStudentId); setTeacherId(""); }} className="field mt-1 min-h-10 bg-white text-sm font-semibold" required><option value="">Öğrenci seç</option>{students?.filter((student) => student.status === "Active").map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}</select></label>
              <label className="text-micro text-[var(--muted)]">Öğretmen<select value={teacherId} onChange={(event) => setTeacherId(event.target.value)} className="field mt-1 min-h-10 bg-white text-sm font-semibold" required><option value="">Öğretmen seç</option>{eligibleTeachers.map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>)}</select></label>
              <label className="text-micro text-[var(--muted)]">Yeni tarih<input type="date" value={dateValue} onChange={(event) => setDateValue(event.target.value)} className="field mt-1 min-h-10 bg-white text-sm font-semibold" required /></label>
              <label className="text-micro text-[var(--muted)]">Yeni saat<input type="time" value={timeValue} onChange={(event) => setTimeValue(event.target.value)} className="field mt-1 min-h-10 bg-white text-sm font-semibold" required /></label>
              <label className="text-micro text-[var(--muted)]">Süre (dk)<input type="number" min={15} max={180} step={5} value={durationValue} onChange={(event) => setDurationValue(event.target.value)} className="field mt-1 min-h-10 bg-white text-sm font-semibold" required /></label>
              <label className="text-micro text-[var(--muted)]">Durum<select value={statusValue} onChange={(event) => setStatusValue(event.target.value as "Normal" | "Cancelled")} className="field mt-1 min-h-10 bg-white text-sm font-semibold"><option value="Normal">Planlandı</option><option value="Cancelled">İptal edildi</option></select></label>
            </div>
            <p className="mt-3 text-[.68rem] text-[var(--muted)]">Öğretmen seçenekleri öğrencinin bu enstrümandaki aktif kurs kayıtlarından gelir. Tüm değişiklikler çakışma ve yetki kontrolünden geçer.</p>
            {error && <p role="alert" className="mt-3 rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
            <div className="mt-4 flex flex-wrap justify-end gap-2">
              <button type="button" onClick={() => { setEditing(false); setError(null); }} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-4 text-sm font-bold">İptal</button>
              <button type="submit" disabled={updateLesson.isPending || !teacherId || !studentId} className="pressable min-h-10 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white disabled:cursor-wait disabled:opacity-60">{updateLesson.isPending ? "Kaydediliyor…" : "Değişiklikleri kaydet"}</button>
            </div>
          </form>
        ) : (
          <div className="flex flex-wrap justify-end gap-2 border-t border-[var(--line)] p-4">
            {canEdit && <button type="button" onClick={() => setEditing(true)} className="pressable min-h-11 rounded-xl border border-[var(--line)] bg-white px-5 text-sm font-bold text-[var(--brand-strong)]">Düzenle</button>}
            <button type="button" onClick={onClose} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-5 text-sm font-bold text-white">Kapat</button>
          </div>
        )}
      </section>
    </div>
  );
}

function DetailItem({ label, value }: { label: string; value: string }) {
  return <div className="rounded-xl border border-[var(--line)] bg-white px-3 py-2.5"><dt className="text-micro text-[var(--muted)]">{label}</dt><dd className="mt-1 text-sm font-semibold">{value}</dd></div>;
}
