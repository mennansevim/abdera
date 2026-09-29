"use client";

import Link from "next/link";
import { useEffect, useMemo, useRef, useState } from "react";
import { Icon } from "@/components/icons";
import { useApproveChangeRequest, usePendingChangeRequests, useRejectChangeRequest } from "@/lib/attendance";
import { useBankTransactions } from "@/lib/banking";
import { useReceivables } from "@/lib/billing";
import { useDashboardToday, type UpcomingBirthday } from "@/lib/dashboard";
import { buildInstrumentColorMap, INSTRUMENT_TONES, type InstrumentTone } from "@/lib/lesson-colors";
import { useNotifications } from "@/lib/messaging";
import { useSystemHealth } from "@/lib/ops";
import { useAttentionNeededStudents, useStudents, useTeachers } from "@/lib/people";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import { Panel, StatStrip } from "@/components/ui";
import { computeHourWindow, layoutDayLessons } from "@/lib/week-grid-layout";
import { PendingLessonNotes } from "./pending-lesson-notes";
import { TeacherTodayLessons } from "./teacher-today-lessons";

const HOUR_HEIGHT_REM = 3.6;
// Ders kartının CSS minimum yüksekliği ve dakika karşılığı - çakışma yerleşimi kısa dersi bu
// süre kadar uzun sayar, yoksa kart bir sonraki dersin üstüne biner (lib/week-grid-layout.ts).
const LESSON_CARD_MIN_HEIGHT_REM = 1.85;
const LESSON_CARD_MIN_MINUTES = Math.ceil((LESSON_CARD_MIN_HEIGHT_REM / HOUR_HEIGHT_REM) * 60);

// Ders bloklarındaki katılım noktası ve haftalık ızgara başlığındaki gösterge için ortak sözlük -
// teacher-today-lessons.tsx'teki StatusBadge ile aynı terimler (Geliyor/Cevap yok/Gelmiyor).
function rsvpDotTone(lesson: CalendarLesson): { color: string; label: string } {
  if (lesson.status !== "Normal") return { color: "transparent", label: "" };
  if (lesson.rsvpResponse === "Attending") return { color: "var(--success)", label: "Geliyor" };
  if (lesson.rsvpResponse === "AttendingLate") return { color: "var(--warning)", label: "Geç kalacak" };
  if (lesson.rsvpResponse === "NotAttending") return { color: "var(--danger)", label: "Gelmiyor" };
  return { color: "var(--warning)", label: "Cevap yok" };
}

const WEEKDAYS = ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma"];

function weekStartFor(date: Date) {
  const result = new Date(date);
  const day = result.getDay();
  result.setDate(result.getDate() + (day === 0 ? -6 : 1 - day));
  result.setHours(0, 0, 0, 0);
  return result;
}

function addDays(date: Date, days: number) {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}

function userName(email: string) {
  const first = email.split("@")[0].split(/[._-]/)[0];
  return first ? first.charAt(0).toLocaleUpperCase("tr-TR") + first.slice(1) : "";
}

function studentInitials(name: string) {
  return name.split(" ").filter(Boolean).slice(0, 2).map((part) => part.charAt(0).toLocaleUpperCase("tr-TR")).join("");
}

function formatMoney(value: number) {
  return new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 0 }).format(value);
}

export default function DashboardPage() {
  const { data: me } = useMe();
  if (!me) return null;

  return (
    <div className="space-y-3">
      {me.role === "Teacher" ? <TeacherDashboard email={me.email} /> : <AdminDashboard email={me.email} />}
    </div>
  );
}

function AdminDashboard({ email }: { email: string }) {
  const [weekStart, setWeekStart] = useState(() => weekStartFor(new Date()));
  const weekEnd = useMemo(() => addDays(weekStart, 7), [weekStart]);
  const { data: lessons, isLoading: lessonsLoading, isError: lessonsError, isFetching: lessonsFetching, refetch: refetchLessons } = useCalendar(weekStart.toISOString(), weekEnd.toISOString());
  const { data: today, isLoading: statsLoading } = useDashboardToday();
  const { data: receivables } = useReceivables();
  const { data: failedNotifications } = useNotifications("Failed", 1, 1);
  const overdueReceivables = (receivables ?? []).filter((item) => item.status === "Overdue" || (item.status !== "Paid" && item.status !== "Cancelled" && new Date(`${item.dueDate}T23:59:59`) < new Date()));
  const overdueTotal = overdueReceivables.reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0);

  return (
    <>
      <DashboardTopbar email={email} />
      <SystemHealthBanner />

      <StatStrip
        label="Günün özeti"
        items={[
          { key: "today", label: "Bugünkü ders", value: today?.todayLessons ?? 0, loading: statsLoading, href: "/dashboard/calendar" },
          { key: "requests", label: "Bekleyen değişiklik talebi", value: today?.pendingChangeRequests ?? 0, tone: today?.pendingChangeRequests ? "warning" : undefined, loading: statsLoading, href: "/dashboard/change-requests" },
          { key: "overdue", label: "Vadesi geçen aidat", value: `₺${formatMoney(overdueTotal)}`, hint: `${overdueReceivables.length} kayıt`, tone: overdueReceivables.length ? "danger" : undefined, loading: statsLoading, href: "/dashboard/billing" },
          { key: "failed", label: "Gönderilemeyen bildirim", value: failedNotifications?.totalCount ?? 0, tone: failedNotifications?.totalCount ? "danger" : undefined, loading: statsLoading, href: "/dashboard/notifications" },
        ]}
      />

      <div className="grid items-start gap-3 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <WeeklySchedule weekStart={weekStart} lessons={lessons ?? []} loading={lessonsLoading} error={lessonsError} retrying={lessonsFetching} onRetry={() => void refetchLessons()} onWeekChange={(offset) => setWeekStart(offset === 0 ? weekStartFor(new Date()) : addDays(weekStart, offset * 7))} />
        <AdminAttentionRail lessons={lessons ?? []} birthdays={today?.upcomingBirthdays} />
      </div>
    </>
  );
}

function DashboardTopbar({ email }: { email: string }) {
  const [query, setQuery] = useState("");
  const { data: students } = useStudents();
  const { data: teachers } = useTeachers();
  const { data: failedNotifications } = useNotifications("Failed", 1, 1);
  const normalized = query.trim().toLocaleLowerCase("tr-TR");
  const results = normalized
    ? [
        ...(students ?? []).filter((item) => `${item.firstName} ${item.lastName}`.toLocaleLowerCase("tr-TR").includes(normalized)).slice(0, 4).map((item) => ({ id: item.id, label: `${item.firstName} ${item.lastName}`, kind: "Öğrenci", href: `/dashboard/students#student-${item.id}` })),
        ...(teachers ?? []).filter((item) => `${item.firstName} ${item.lastName}`.toLocaleLowerCase("tr-TR").includes(normalized)).slice(0, 4).map((item) => ({ id: item.id, label: `${item.firstName} ${item.lastName}`, kind: "Öğretmen", href: `/dashboard/teachers#teacher-${item.id}` })),
      ]
    : [];

  return (
    <header className="flex flex-col gap-2 lg:flex-row lg:items-center lg:justify-between">
      <div>
        <h1 className="text-display font-serif leading-tight">Merhaba{userName(email) ? `, ${userName(email)}` : ""}</h1>
        <p className="text-meta mt-0.5">
          {new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", weekday: "long" }).format(new Date())} · Okulun bugünkü akışı burada
        </p>
      </div>
      <div className="flex items-center gap-2">
        <div className="relative min-w-0 flex-1 lg:w-[19rem] lg:flex-none">
          <Icon name="search" className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--muted)]" />
          <input type="search" enterKeyHint="search" autoComplete="off" value={query} onChange={(event) => setQuery(event.target.value)} className="field pl-10 pr-4 text-sm" placeholder="Öğrenci veya öğretmen ara…" aria-label="Öğrenci veya öğretmen ara" />
          {normalized && (
            <div className="app-card absolute right-0 top-[calc(100%+.45rem)] z-20 w-full min-w-[17rem] overflow-hidden p-1.5">
              {results.length ? results.map((result) => (
                <Link key={`${result.kind}-${result.id}`} href={result.href} onClick={() => setQuery("")} className="pressable flex min-h-11 items-center justify-between rounded-xl px-3 text-sm hover:bg-[var(--surface-muted)]">
                  <span className="font-medium">{result.label}</span><span className="text-[.75rem] text-[var(--muted)]">{result.kind}</span>
                </Link>
              )) : <p className="px-3 py-4 text-center text-xs text-[var(--muted)]">Eşleşen kayıt bulunamadı.</p>}
            </div>
          )}
        </div>
        <Link href="/dashboard/notifications" className="icon-btn icon-btn-quiet relative shrink-0 text-[var(--brand-strong)]" aria-label={failedNotifications?.totalCount ? `Bildirimleri aç, ${failedNotifications.totalCount} gönderilemeyen bildirim` : "Bildirimleri aç"}>
          <Icon name="bell" className="h-[1.1rem] w-[1.1rem]" />
          {!!failedNotifications?.totalCount && <span className="absolute right-2.5 top-2.5 h-1.5 w-1.5 rounded-full bg-[var(--danger)] ring-2 ring-white" aria-hidden="true" />}
        </Link>
      </div>
    </header>
  );
}

// Faz 4 (docs/15-product-phases.md): "ana ekranda göster, sorun varsa kırmızı ile uyar".
// Sistem sağlıklıyken sessiz kalır (dikkat dağıtmaz), Degraded/Unhealthy'de belirgin bir
// şerit gösterir - aynı sorun için ilgililere zaten e-posta gitmiştir (SystemHealthMonitor),
// bu yalnızca panelde de görünür kılar.
function SystemHealthBanner() {
  const { data: health } = useSystemHealth();
  if (!health || health.level === "Healthy") return null;

  const tone = health.level === "Unhealthy"
    ? { bg: "bg-[var(--danger-soft)]", text: "text-[var(--danger-strong)]", label: "Sistem sorunlu" }
    : { bg: "bg-[var(--warning-soft)]", text: "text-[var(--warning-strong)]", label: "Dikkat gerekiyor" };
  const lastBackup = health.lastSuccessfulBackupAt
    ? new Date(health.lastSuccessfulBackupAt).toLocaleString("tr-TR")
    : "hiç";

  return (
    <section role="alert" className={`app-card flex flex-wrap items-center gap-3 px-4 py-2.5 ${tone.bg}`}>
      <span className={`grid h-8 w-8 shrink-0 place-items-center rounded-xl bg-white/60 ${tone.text}`}><Icon name="shield" className="h-5 w-5" /></span>
      <div className="min-w-0 flex-1">
        <p className={`text-sm font-bold ${tone.text}`}>{tone.label}{health.detail ? `: ${health.detail}` : ""}</p>
        <p className="text-meta mt-0.5">Son başarılı yedekleme: {lastBackup}</p>
      </div>
    </section>
  );
}

const RSVP_LEGEND: { color: string; label: string }[] = [
  { color: "var(--success)", label: "Geliyor" },
  { color: "var(--warning)", label: "Cevap yok" },
  { color: "var(--danger)", label: "Gelmiyor" },
];

function WeeklySchedule({ weekStart, lessons: allLessons, loading, error, retrying, onRetry, onWeekChange }: { weekStart: Date; lessons: CalendarLesson[]; loading: boolean; error: boolean; retrying: boolean; onRetry: () => void; onWeekChange: (offset: number) => void }) {
  const weekdays = Array.from({ length: 5 }, (_, index) => addDays(weekStart, index));
  // Bir ders ertelendiğinde backend eski kaydı SİLMEZ, `Rescheduled` durumuna çevirip yeni saat
  // için ayrı bir satır açar (denetim izi - CLAUDE.md). Bu eski kaydı ızgarada göstermeye devam
  // etmek aynı dersin iki yerde birden görünmesine yol açıyordu ("taşıdığım ders eski yerinde de
  // kalıyor" bulgusu) - `Rescheduled` artık burada, kaynakta filtreleniyor.
  const lessons = useMemo(() => allLessons.filter((lesson) => lesson.status !== "Rescheduled"), [allLessons]);
  const lessonColors = useMemo(() => buildInstrumentColorMap(lessons.map((lesson) => lesson.instrumentName)), [lessons]);
  const hourWindow = useMemo(() => computeHourWindow(lessons.filter((lesson) => weekdays.some((day) => new Date(lesson.startAt).toDateString() === day.toDateString()))), [lessons, weekdays]);
  const [openLesson, setOpenLesson] = useState<CalendarLesson | null>(null);

  return (
    <section className="app-card min-w-0 overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 px-4 py-2">
        <div className="flex min-w-0 flex-wrap items-baseline gap-x-2">
          <h2 className="text-sm font-bold">Bu hafta</h2>
          <span className="text-meta">{weekdays[0].toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} – {weekdays[4].toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}</span>
          <Link href="/dashboard/calendar" className="text-[.75rem] font-bold text-[var(--brand)] hover:underline">Takvimi aç</Link>
        </div>
        <div className="ml-auto hidden flex-wrap items-center justify-end gap-3 md:flex">
          {RSVP_LEGEND.map((item) => <span key={item.label} className="inline-flex items-center gap-1.5 text-[.75rem] text-[var(--muted)]"><span className="h-1.5 w-1.5 rounded-full" style={{ background: item.color }} aria-hidden="true" />{item.label}</span>)}
        </div>
        <div className="flex items-center gap-1.5">
          <button type="button" onClick={() => onWeekChange(-1)} className="icon-btn icon-btn-quiet" aria-label="Önceki hafta"><Icon name="arrow-left" className="h-4 w-4" /></button>
          <button type="button" onClick={() => onWeekChange(0)} className="btn btn-quiet px-3 text-[.75rem] font-semibold">Bu hafta</button>
          <button type="button" onClick={() => onWeekChange(1)} className="icon-btn icon-btn-quiet" aria-label="Sonraki hafta"><Icon name="arrow-right" className="h-4 w-4" /></button>
        </div>
      </div>

      {/* Hata boş günler gibi görünmesin ("Planlanmış ders yok" yanıltıcı olur) - takvim
          sayfasındaki hata kutusuyla aynı dil. */}
      {loading ? <ScheduleSkeleton /> : error ? (
        <div className="grid min-h-48 place-items-center border-t border-[var(--line)] p-8 text-center"><div><p className="text-sm font-bold">Ders programı yüklenemedi</p><p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p><button type="button" onClick={onRetry} disabled={retrying} className="btn btn-quiet mt-3 disabled:opacity-50">{retrying ? "Yükleniyor…" : "Tekrar dene"}</button></div></div>
      ) : (
        <>
          {/* Izgara görünümü ≥768px'te (docs/14-ui-design-prompt.md B3.1) - önceden yalnızca ≥1280px'te
              açılıyordu, 768-1279 arasında istenmeyen bir ajanda görünümüne düşüyordu. */}
          <div className="hidden grid-cols-[3.2rem_repeat(5,minmax(0,1fr))] border-t border-[var(--line)] md:grid">
            <div className="border-r border-[var(--line)]" />
            {weekdays.map((day, index) => <div key={day.toISOString()} className={`border-r border-[var(--line)] px-2 py-1.5 text-center text-[.75rem] last:border-r-0 ${day.toDateString() === new Date().toDateString() ? "bg-[var(--today-tint)]" : ""}`}><span className="font-semibold text-[var(--muted)]">{WEEKDAYS[index]}</span> <span className="font-bold">{day.getDate()}</span></div>)}
            <TimeLabels hourWindow={hourWindow} />
            {weekdays.map((day) => <DayColumn key={day.toISOString()} day={day} lessons={lessons} colors={lessonColors} hourWindow={hourWindow} onOpen={setOpenLesson} />)}
          </div>
          <div className="space-y-3 border-t border-[var(--line)] p-3 md:hidden">
            {weekdays.map((day, index) => {
              const dayLessons = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString()).sort((a,b) => a.startAt.localeCompare(b.startAt));
              return (
                <div key={day.toISOString()}>
                  <h3 className="mb-2 flex items-center gap-2 text-xs font-bold"><span className={`grid h-7 w-7 place-items-center rounded-lg ${day.toDateString() === new Date().toDateString() ? "bg-[var(--brand)] text-white" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{day.getDate()}</span>{WEEKDAYS[index]}</h3>
                  <div className="space-y-1.5">
                    {dayLessons.map((lesson) => <AgendaLesson key={lesson.id} lesson={lesson} tone={lessonColors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0]} onOpen={setOpenLesson} />)}
                    {!dayLessons.length && <p className="py-2 text-xs text-[var(--muted)]">Planlanmış ders yok.</p>}
                  </div>
                </div>
              );
            })}
          </div>
        </>
      )}

      {openLesson && <LessonPopover lesson={openLesson} tone={lessonColors.get(openLesson.instrumentName) ?? INSTRUMENT_TONES[0]} onClose={() => setOpenLesson(null)} />}
    </section>
  );
}

function TimeLabels({ hourWindow }: { hourWindow: { startHour: number; endHour: number } }) {
  const totalHours = hourWindow.endHour - hourWindow.startHour;
  return (
    <div className="relative border-r border-t border-[var(--line)] bg-[#fdf9f2]" style={{ height: `${totalHours * HOUR_HEIGHT_REM}rem` }}>
      {Array.from({ length: totalHours + 1 }, (_, index) => (
        <span key={index} className={`absolute right-2 text-[.75rem] tabular-nums text-[var(--muted)] ${index === 0 ? "translate-y-0" : index === totalHours ? "-translate-y-full" : "-translate-y-1/2"}`} style={{ top: `${(index / totalHours) * 100}%` }}>
          {String(hourWindow.startHour + index).padStart(2, "0")}:00
        </span>
      ))}
    </div>
  );
}

function DayColumn({ day, lessons, colors, hourWindow, onOpen }: { day: Date; lessons: CalendarLesson[]; colors: Map<string, InstrumentTone>; hourWindow: { startHour: number; endHour: number }; onOpen: (lesson: CalendarLesson) => void }) {
  const entries = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString());
  const layout = useMemo(() => layoutDayLessons(entries, hourWindow, { minDurationMinutes: LESSON_CARD_MIN_MINUTES }), [entries, hourWindow]);
  const isToday = day.toDateString() === new Date().toDateString();
  const totalHours = hourWindow.endHour - hourWindow.startHour;
  return (
    <div className={`relative border-r border-t border-[var(--line)] last:border-r-0 ${isToday ? "bg-[var(--today-tint-strong)]" : "bg-[#fdf9f2]"}`} style={{ height: `${totalHours * HOUR_HEIGHT_REM}rem` }}>
      {Array.from({ length: totalHours - 1 }, (_, index) => <span key={index} className="absolute inset-x-0 border-t border-dashed border-[#f3e4cd]" style={{ top: `${((index + 1) / totalHours) * 100}%` }} />)}
      {entries.map((lesson) => {
        const start = new Date(lesson.startAt);
        const end = new Date(lesson.endAt);
        const position = layout.get(lesson.id);
        if (!position) return null;
        const tone = colors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0];
        const dot = rsvpDotTone(lesson);
        const isCancelled = lesson.status === "Cancelled";
        const gapPct = 1.5;
        const width = `calc(${100 / position.columns}% - ${gapPct}px)`;
        const left = `calc(${(position.column / position.columns) * 100}% + ${gapPct / 2}px)`;
        return (
          <button
            key={lesson.id}
            type="button"
            onClick={() => onOpen(lesson)}
            title={`${lesson.studentName} · ${lesson.instrumentName} · ${lesson.teacherName}`}
            className={`pressable absolute z-10 overflow-hidden rounded-md border-l-[3px] px-2 py-1 text-left shadow-sm hover:z-20 hover:shadow-md ${isCancelled ? "opacity-55" : ""}`}
            style={{ top: `${position.top * 100}%`, height: `${position.height * 100}%`, left, width, minHeight: `${LESSON_CARD_MIN_HEIGHT_REM}rem`, background: tone.bg, borderLeftColor: tone.border, color: tone.text }}
          >
            {dot.label && <><span className="absolute right-1 top-1 h-1.5 w-1.5 rounded-full" style={{ background: dot.color }} aria-hidden="true" /><span className="sr-only">Katılım: {dot.label}</span></>}
            <span className={`block text-[.75rem] font-bold tabular-nums ${isCancelled ? "line-through" : ""}`}>{start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}–{end.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}</span>
            <span className={`mt-0.5 block truncate text-[.75rem] font-bold ${isCancelled ? "line-through" : ""}`}>{position.columns > 2 ? studentInitials(lesson.studentName) : lesson.studentName}</span>
            <span className="block truncate text-[.75rem] opacity-75">{lesson.instrumentName}</span>
          </button>
        );
      })}
    </div>
  );
}

function AgendaLesson({ lesson, tone, onOpen }: { lesson: CalendarLesson; tone: InstrumentTone; onOpen: (lesson: CalendarLesson) => void }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  const dot = rsvpDotTone(lesson);
  const isCancelled = lesson.status === "Cancelled";
  return (
    <button type="button" onClick={() => onOpen(lesson)} className={`pressable flex min-h-12 w-full items-center gap-3 rounded-xl border border-[var(--line)] bg-white px-2.5 py-2 text-left ${isCancelled ? "opacity-60" : ""}`}>
      <span className="h-9 w-1 shrink-0 rounded-full" style={{ background: tone.border }} />
      <span className={`w-20 shrink-0 text-[.75rem] font-bold tabular-nums ${isCancelled ? "line-through" : ""}`} style={{ color: tone.text }}>{start.toLocaleTimeString("tr-TR", {hour:"2-digit",minute:"2-digit"})}–{end.toLocaleTimeString("tr-TR", {hour:"2-digit",minute:"2-digit"})}</span>
      <span className="min-w-0 flex-1">
        <span className={`block truncate text-xs font-bold ${isCancelled ? "line-through" : ""}`}>{lesson.studentName}</span>
        <span className="block truncate text-[.75rem] text-[var(--muted)]">{lesson.instrumentName} · {lesson.teacherName}</span>
      </span>
      {dot.label && <><span className="shrink-0 h-1.5 w-1.5 rounded-full" style={{ background: dot.color }} aria-hidden="true" /><span className="sr-only">Katılım: {dot.label}</span></>}
    </button>
  );
}

function LessonPopover({ lesson, tone, onClose }: { lesson: CalendarLesson; tone: InstrumentTone; onClose: () => void }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  const dot = rsvpDotTone(lesson);

  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);
  // Arka plan kaydırması kilitlenir, Escape kapatır, odak açılışta Kapat'a taşınıp kapanışta
  // önceki öğeye döner. onClose her render'da yeni fonksiyon geldiği için ref'ten okunur -
  // effect yalnızca açılış/kapanışta çalışır.
  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null;
    closeButtonRef.current?.focus();
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") onCloseRef.current(); };
    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      document.body.style.overflow = overflow;
      previouslyFocused?.focus();
    };
  }, []);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-[#2b1a10]/40 p-4 backdrop-blur-[2px]" onClick={onClose}>
      <div role="dialog" aria-modal="true" aria-label={`${lesson.studentName} ders detayı`} onClick={(event) => event.stopPropagation()} className="app-card w-full max-w-[22rem] overflow-hidden">
        <div className="flex items-start justify-between gap-2 border-l-4 p-4" style={{ borderLeftColor: tone.border, background: tone.bg }}>
          <div className="min-w-0">
            <p className="truncate text-sm font-bold" style={{ color: tone.text }}>{lesson.studentName}</p>
            <p className="mt-0.5 text-[.75rem] font-semibold" style={{ color: tone.text }}>{lesson.instrumentName}</p>
          </div>
          <button ref={closeButtonRef} type="button" onClick={onClose} className="icon-btn icon-btn-quiet shrink-0" aria-label="Kapat"><Icon name="close" className="h-4 w-4" /></button>
        </div>
        <div className="space-y-2 p-4 text-sm">
          <p className="flex items-center gap-2 text-[var(--foreground)]"><Icon name="clock" className="h-4 w-4 text-[var(--muted)]" />{start.toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "long" })} · {start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}–{end.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}</p>
          <p className="flex items-center gap-2 text-[var(--foreground)]"><Icon name="teachers" className="h-4 w-4 text-[var(--muted)]" />{lesson.teacherName}</p>
          {dot.label && <p className="flex items-center gap-2"><span className="h-2 w-2 rounded-full" style={{ background: dot.color }} />{dot.label}</p>}
        </div>
        <div className="border-t border-[var(--line)] p-3">
          <Link href="/dashboard/calendar" onClick={onClose} className="pressable flex min-h-11 items-center justify-center rounded-xl bg-[var(--brand)] text-xs font-bold text-white">Takvimde aç</Link>
        </div>
      </div>
    </div>
  );
}

function ScheduleSkeleton() {
  return <div className="grid gap-3 border-t border-[var(--line)] p-4 max-md:[&>*]:h-14 md:h-[21.5rem] md:grid-cols-5">{Array.from({ length: 5 }, (_, index) => <div key={index} className="skeleton rounded-xl" />)}</div>;
}

function AdminAttentionRail({ lessons, birthdays }: { lessons: CalendarLesson[]; birthdays?: UpcomingBirthday[] }) {
  const { data: requests, isLoading } = usePendingChangeRequests();
  const { data: bankItems, isLoading: bankLoading } = useBankTransactions("NeedsReview", 1, 3);
  const approve = useApproveChangeRequest();
  const reject = useRejectChangeRequest();
  const [busyId, setBusyId] = useState<string | null>(null);
  const { data: attentionStudents, isLoading: attentionLoading } = useAttentionNeededStudents();
  const hasRequests = Boolean(requests?.length);
  const hasBankItems = Boolean(bankItems?.items.length);
  const hasAttentionStudents = Boolean(attentionStudents?.length);
  const hasBirthdays = Boolean(birthdays?.length);
  const railLoading = isLoading || bankLoading || attentionLoading || birthdays === undefined;
  const allClear = !railLoading && !hasRequests && !hasBankItems && !hasAttentionStudents && !hasBirthdays;

  async function act(id: string, action: "approve" | "reject") {
    setBusyId(id);
    try { await (action === "approve" ? approve.mutateAsync(id) : reject.mutateAsync(id)); }
    finally { setBusyId(null); }
  }

  return (
    <aside className="grid gap-3 md:grid-cols-2 xl:grid-cols-1">
      {railLoading && <div className="skeleton min-h-28 rounded-2xl md:col-span-2 xl:col-span-1" aria-label="Dikkat gerektiren işler yükleniyor" />}

      {allClear && (
        <section className="app-card flex items-center gap-3 px-4 py-3 md:col-span-2 xl:col-span-1">
          <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--success-soft)] text-[var(--success-strong)]"><Icon name="check" className="h-4 w-4" /></span>
          <div><h2 className="text-sm font-bold">Bugün için her şey yolunda</h2><p className="text-meta">Bekleyen talep, banka işlemi veya öğrenci uyarısı yok.</p></div>
        </section>
      )}

      {hasRequests && (
        <Panel flush title="Değişiklik talepleri" meta={requests?.length} actions={<Link href="/dashboard/change-requests" className="text-[.75rem] font-bold text-[var(--brand)] hover:underline">Tümü</Link>}>
          <ul className="divide-y divide-[var(--line)]">
            {requests?.slice(0, 3).map((request) => {
              const lesson = lessons.find((item) => item.id === request.lessonId);
              return (
                <li key={request.id} className="flex items-center gap-2 px-4 py-2">
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-xs font-bold">{lesson?.studentName ?? "Ders değişikliği"}</span>
                    <span className="block text-[.72rem] text-[var(--muted)]">{new Date(request.proposedStartAt).toLocaleString("tr-TR", { weekday: "short", hour: "2-digit", minute: "2-digit" })}</span>
                  </span>
                  <button disabled={busyId === request.id} onClick={() => act(request.id, "approve")} className="icon-btn bg-[var(--success-soft)] text-[var(--success-strong)] disabled:opacity-50" aria-label="Talebi onayla"><Icon name="check" className="h-4 w-4" /></button>
                  <button disabled={busyId === request.id} onClick={() => act(request.id, "reject")} className="icon-btn bg-[var(--danger-soft)] text-[var(--danger-strong)] disabled:opacity-50" aria-label="Talebi reddet"><Icon name="x" className="h-4 w-4" /></button>
                </li>
              );
            })}
          </ul>
        </Panel>
      )}

      {hasBankItems && (
        <Panel flush title="İncelenecek banka işlemleri" actions={<Link href="/dashboard/banking" className="text-[.75rem] font-bold text-[var(--brand)] hover:underline">Tümü</Link>}>
          <ul className="divide-y divide-[var(--line)]">
            {bankItems?.items.map((item) => (
              <li key={item.id}>
                <Link href="/dashboard/banking" className="pressable flex items-center justify-between gap-3 px-4 py-2 hover:bg-[var(--surface-muted)]">
                  <span className="min-w-0">
                    <span className="block truncate text-xs font-bold tabular-nums">{formatMoney(item.amount)} {item.currency}</span>
                    <span className="block truncate text-[.72rem] text-[var(--muted)]">{item.senderName ?? "İsimsiz gönderici"}{item.description ? ` · ${item.description}` : ""}</span>
                  </span>
                  <span className="shrink-0 text-[.75rem] font-bold text-[var(--brand)]">İncele</span>
                </Link>
              </li>
            ))}
          </ul>
        </Panel>
      )}

      {hasAttentionStudents && (
        <Panel flush title="İlgi gerektirebilecek öğrenciler">
          <ul className="divide-y divide-[var(--line)]">
            {attentionStudents?.slice(0, 4).map((student) => (
              <li key={student.studentId}>
                <Link href={`/dashboard/students#student-${student.studentId}`} className="pressable block px-4 py-2 hover:bg-[var(--surface-muted)]">
                  <span className="block text-xs font-bold">{student.studentName}</span>
                  <span className="block text-[.72rem] leading-snug text-[var(--danger-strong)]">{student.reasons.join(" · ")}</span>
                </Link>
              </li>
            ))}
          </ul>
        </Panel>
      )}

      {hasBirthdays && <UpcomingBirthdaysRail birthdays={birthdays} />}
    </aside>
  );
}

// Öğrencilerin doğum günleri önceden yalnızca bir KPI sayısıydı, ana ekranda hiçbir yerde
// gösterilmiyordu - kullanıcı isteğiyle gerçek bir liste (isim + tarih + kaç gün kaldı)
// hâline getirildi (bkz. Dashboard.cs ListUpcomingBirthdaysAsync, 30 günlük pencere).
function UpcomingBirthdaysRail({ birthdays }: { birthdays?: UpcomingBirthday[] }) {
  function dueLabel(daysUntil: number) {
    if (daysUntil === 0) return "Bugün";
    if (daysUntil === 1) return "Yarın";
    return `${daysUntil} gün sonra`;
  }

  return (
    <Panel flush title="Yaklaşan doğum günleri" meta="30 gün içinde">
      {!birthdays?.length && <p className="text-meta px-4 py-4 text-center">Yaklaşan doğum günü yok.</p>}
      <ul className="divide-y divide-[var(--line)]">
        {birthdays?.slice(0, 5).map((item) => (
          <li key={item.studentId}>
            <Link href={`/dashboard/students#student-${item.studentId}`} className="pressable flex items-center gap-3 px-4 py-2 hover:bg-[var(--surface-muted)]">
              <Icon name="cake" className="h-4 w-4 shrink-0 text-[var(--brand)]" />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-xs font-bold">{item.studentName}</span>
                <span className="block text-[.72rem] text-[var(--muted)]">
                  {new Date(`${item.nextOccurrence}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} · {item.turningAge} yaşına giriyor
                </span>
              </span>
              <span className="shrink-0 text-[.75rem] font-bold text-[var(--brand)]">{dueLabel(item.daysUntil)}</span>
            </Link>
          </li>
        ))}
      </ul>
    </Panel>
  );
}

function TeacherDashboard({ email }: { email: string }) {
  const [selectedDate, setSelectedDate] = useState(() => new Date());
  const weekStart = weekStartFor(new Date());
  const weekDays = Array.from({ length: 7 }, (_, index) => addDays(weekStart, index));
  const selectedIsToday = selectedDate.toDateString() === new Date().toDateString();
  return (
    <div className="mx-auto max-w-[32rem] xl:max-w-5xl">
      <header className="mb-3 flex items-start justify-between gap-3">
        <div><h1 className="text-[1.35rem] font-bold tracking-[-0.035em]">{selectedIsToday ? "Bugün" : selectedDate.toLocaleDateString("tr-TR", { weekday: "long" })}</h1><p className="mt-0.5 text-[.75rem] text-[var(--muted)]">{new Intl.DateTimeFormat("tr-TR", { day:"numeric", month:"long", weekday:"long" }).format(selectedDate)}</p></div>
        <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--brand-soft)] text-[.75rem] font-bold text-[var(--brand)]">{userName(email).slice(0,2).toLocaleUpperCase("tr-TR")}</span>
      </header>
      <PendingLessonNotes />
      {/* Gün şeridi: gizli kaydırma çubuklu yatay şerit 360/390px'te son günleri ekran dışına
          itiyor ve kaydırılabildiği fark edilmiyordu. Hücreler min-w-0 ile daralabilen 7 sütunlu
          ızgarada ~328px'e sığar (hücre başı ~43px, kısa gün adı + tarih). */}
      <div className="mb-3 grid grid-cols-7 gap-1 sm:gap-1.5">
        {weekDays.map((day) => {
          const active = day.toDateString() === selectedDate.toDateString();
          const isToday = day.toDateString() === new Date().toDateString();
          return (
            <button key={day.toISOString()} type="button" onClick={() => setSelectedDate(day)} aria-pressed={active} className={`pressable relative flex min-h-[3.2rem] min-w-0 flex-col items-center justify-center rounded-xl border text-[.75rem] ${active ? "border-[var(--brand)] bg-[var(--brand)] text-white shadow-[0_7px_16px_rgba(168,78,31,.2)]" : "border-[var(--line)] bg-white text-[var(--muted)]"}`}>
              <span>{day.toLocaleDateString("tr-TR", { weekday:"short" }).replace(".","")}</span>
              <span className="mt-1 text-[.75rem] font-bold">{day.getDate()}</span>
              {isToday && !active && <span className="absolute bottom-1.5 h-1 w-1 rounded-full bg-[var(--brand)]" />}
            </button>
          );
        })}
      </div>
      <TeacherTodayLessons date={selectedDate} />
    </div>
  );
}
