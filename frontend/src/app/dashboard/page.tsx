"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { Icon, type IconName } from "@/components/icons";
import { useApproveChangeRequest, usePendingChangeRequests, useRejectChangeRequest } from "@/lib/attendance";
import { useBankTransactions } from "@/lib/banking";
import { useReceivables } from "@/lib/billing";
import { useDashboardToday } from "@/lib/dashboard";
import { buildInstrumentColorMap, INSTRUMENT_TONES, type InstrumentTone } from "@/lib/lesson-colors";
import { useNotifications } from "@/lib/messaging";
import { useSystemHealth } from "@/lib/ops";
import { useAttentionNeededStudents, useStudents, useTeachers } from "@/lib/people";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import { computeHourWindow, layoutDayLessons } from "@/lib/week-grid-layout";
import { TeacherTodayLessons } from "./teacher-today-lessons";

const HOUR_HEIGHT_REM = 3.6;

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
    <div className="space-y-5">
      {me.role === "Teacher" ? <TeacherDashboard email={me.email} /> : <AdminDashboard email={me.email} />}
    </div>
  );
}

function AdminDashboard({ email }: { email: string }) {
  const [weekStart, setWeekStart] = useState(() => weekStartFor(new Date()));
  const weekEnd = useMemo(() => addDays(weekStart, 7), [weekStart]);
  const { data: lessons, isLoading: lessonsLoading } = useCalendar(weekStart.toISOString(), weekEnd.toISOString());
  const { data: today, isLoading: statsLoading } = useDashboardToday();
  const { data: receivables } = useReceivables();
  const { data: failedNotifications } = useNotifications("Failed", 1, 1);
  const overdueReceivables = (receivables ?? []).filter((item) => item.status === "Overdue" || (item.status !== "Paid" && item.status !== "Cancelled" && new Date(`${item.dueDate}T23:59:59`) < new Date()));
  const overdueTotal = overdueReceivables.reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0);

  return (
    <>
      <DashboardTopbar email={email} />
      <SystemHealthBanner />

      <section className="grid grid-cols-2 gap-3 xl:grid-cols-4" aria-label="Günün özeti">
        <StatCard icon="calendar" value={today?.todayLessons} label="Bugünkü Ders" loading={statsLoading} tone="purple" />
        <StatCard icon="swap" value={today?.pendingChangeRequests} label="Bekleyen Değişiklik Talebi" loading={statsLoading} tone="amber" href="/dashboard/change-requests" />
        <StatCard icon="wallet" value={`${overdueReceivables.length} kayıt`} secondaryValue={`₺${formatMoney(overdueTotal)}`} label="Vadesi Geçen Aidat" loading={statsLoading} tone="red" href="/dashboard/billing" />
        <StatCard icon="bell" value={failedNotifications?.totalCount ?? 0} label="Gönderilemeyen Bildirim" loading={statsLoading} tone="rose" href="/dashboard/notifications" />
      </section>

      <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_18rem]">
        <WeeklySchedule weekStart={weekStart} lessons={lessons ?? []} loading={lessonsLoading} onWeekChange={(offset) => setWeekStart(offset === 0 ? weekStartFor(new Date()) : addDays(weekStart, offset * 7))} />
        <AdminAttentionRail lessons={lessons ?? []} />
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
    <header className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
      <div>
        <h1 className="font-serif text-[1.45rem] font-bold italic tracking-[-0.02em] sm:text-[1.7rem]">Merhaba{userName(email) ? `, ${userName(email)}` : ""}</h1>
        <p className="mt-1 text-xs text-[var(--muted)]">
          {new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", weekday: "long" }).format(new Date())} · Okulun bugünkü akışı burada
        </p>
      </div>
      <div className="flex items-center gap-2">
        <div className="relative min-w-0 flex-1 xl:w-[19rem] xl:flex-none">
          <Icon name="search" className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--muted)]" />
          <input value={query} onChange={(event) => setQuery(event.target.value)} className="field min-h-11 pl-10 pr-4 text-xs" placeholder="Öğrenci veya öğretmen ara…" aria-label="Öğrenci veya öğretmen ara" />
          {normalized && (
            <div className="app-card absolute right-0 top-[calc(100%+.45rem)] z-20 w-full min-w-[17rem] overflow-hidden p-1.5">
              {results.length ? results.map((result) => (
                <Link key={`${result.kind}-${result.id}`} href={result.href} onClick={() => setQuery("")} className="pressable flex min-h-11 items-center justify-between rounded-xl px-3 text-sm hover:bg-[var(--surface-muted)]">
                  <span className="font-medium">{result.label}</span><span className="text-[.65rem] text-[var(--muted)]">{result.kind}</span>
                </Link>
              )) : <p className="px-3 py-4 text-center text-xs text-[var(--muted)]">Eşleşen kayıt bulunamadı.</p>}
            </div>
          )}
        </div>
        <Link href="/dashboard/notifications" className="pressable relative grid h-11 w-11 shrink-0 place-items-center rounded-xl border-2 border-[var(--line)] bg-white text-[var(--brand-strong)] shadow-sm" aria-label="Bildirimleri aç">
          <Icon name="bell" className="h-[1.1rem] w-[1.1rem]" />
          {!!failedNotifications?.totalCount && <span className="absolute right-2.5 top-2.5 h-1.5 w-1.5 rounded-full bg-[var(--danger)] ring-2 ring-white" />}
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
    <section role="alert" className={`app-card flex flex-wrap items-center gap-3 p-4 ${tone.bg}`}>
      <span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-white/60 ${tone.text}`}><Icon name="shield" className="h-5 w-5" /></span>
      <div className="min-w-0 flex-1">
        <p className={`text-sm font-bold ${tone.text}`}>{tone.label}{health.detail ? `: ${health.detail}` : ""}</p>
        <p className="text-meta mt-0.5">Son başarılı yedekleme: {lastBackup}</p>
      </div>
    </section>
  );
}

type StatTone = "purple" | "amber" | "red" | "rose";
const STAT_TONES: Record<StatTone, { icon: string; iconBg: string; value: string }> = {
  purple: { icon: "var(--brand-strong)", iconBg: "var(--brand-soft)", value: "#3a2a1f" },
  amber: { icon: "var(--warning-strong)", iconBg: "var(--warning-soft)", value: "var(--warning-strong)" },
  red: { icon: "var(--danger-strong)", iconBg: "var(--danger-soft)", value: "var(--danger-strong)" },
  rose: { icon: "#a13c2f", iconBg: "#fbe3da", value: "#8a3423" },
};

function StatCard({ icon, value, secondaryValue, label, loading, tone, href }: { icon: IconName; value?: number | string; secondaryValue?: string; label: string; loading: boolean; tone: StatTone; href?: string }) {
  const palette = STAT_TONES[tone];
  const content = (
    <div className="flex h-full items-start gap-3 p-4">
      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl" style={{ color: palette.icon, background: palette.iconBg }}><Icon name={icon} className="h-[1.05rem] w-[1.05rem]" /></span>
      <span className="min-w-0 flex-1 pt-0.5">
        {loading ? (
          <span className="skeleton mb-2 block h-7 w-16 rounded-md" />
        ) : (
          <span className="block">
            {/* İkincil değeri olan kartlar (örn. "3 kayıt" + "₺8.400") sözcük içerir ve tutar
                sınırsız büyüyebilir (çok sayıda vadesi geçen aidat) - display ölçeği dar kartta
                taşar/kırpılır, bu yüzden bu kartlarda her iki satır da başlık ölçeğinde kalır. */}
            <span className={`block truncate ${secondaryValue ? "text-title" : "text-display"}`} style={{ color: palette.value }}>{value ?? 0}</span>
            {secondaryValue && <span className="text-title mt-0.5 block truncate" style={{ color: palette.value }}>{secondaryValue}</span>}
          </span>
        )}
        <span className="text-meta mt-2 block leading-snug">{label}</span>
      </span>
      {href && <Icon name="chevron" className="ml-auto mt-2 h-3.5 w-3.5 shrink-0 text-[var(--muted)]" />}
    </div>
  );
  return href ? <Link href={href} className="app-card pressable min-h-[6.8rem] overflow-hidden hover:-translate-y-0.5 hover:shadow-[0_12px_32px_rgba(38,31,24,.08)]">{content}</Link> : <article className="app-card min-h-[6.8rem] overflow-hidden">{content}</article>;
}

const RSVP_LEGEND: { color: string; label: string }[] = [
  { color: "var(--success)", label: "Geliyor" },
  { color: "var(--warning)", label: "Cevap yok" },
  { color: "var(--danger)", label: "Gelmiyor" },
];

function WeeklySchedule({ weekStart, lessons: allLessons, loading, onWeekChange }: { weekStart: Date; lessons: CalendarLesson[]; loading: boolean; onWeekChange: (offset: number) => void }) {
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
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-4 sm:px-5">
        <div>
          <div className="flex items-center gap-2.5">
            <h2 className="text-title">Bu Hafta</h2>
            <Link href="/dashboard/calendar" className="text-[.62rem] font-bold text-[var(--brand)] hover:underline">Takvimi aç</Link>
          </div>
          <p className="text-meta mt-0.5">{weekdays[0].toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} – {weekdays[4].toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}</p>
        </div>
        <div className="ml-auto hidden flex-wrap items-center justify-end gap-3 lg:flex">
          {RSVP_LEGEND.map((item) => <span key={item.label} className="inline-flex items-center gap-1.5 text-[.62rem] text-[var(--muted)]"><span className="h-1.5 w-1.5 rounded-full" style={{ background: item.color }} />{item.label}</span>)}
        </div>
        <div className="flex items-center gap-1.5">
          <button onClick={() => onWeekChange(-1)} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Önceki hafta"><Icon name="arrow-left" className="h-4 w-4" /></button>
          <button onClick={() => onWeekChange(0)} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-[.68rem] font-semibold hover:bg-[var(--surface-muted)]">Bu hafta</button>
          <button onClick={() => onWeekChange(1)} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Sonraki hafta"><Icon name="arrow-right" className="h-4 w-4" /></button>
        </div>
      </div>

      {loading ? <ScheduleSkeleton /> : (
        <>
          {/* Izgara görünümü ≥768px'te (docs/14-ui-design-prompt.md B3.1) - önceden yalnızca ≥1280px'te
              açılıyordu, 768-1279 arasında istenmeyen bir ajanda görünümüne düşüyordu. */}
          <div className="hidden grid-cols-[3.2rem_repeat(5,minmax(0,1fr))] border-t border-[var(--line)] md:grid">
            <div className="border-r border-[var(--line)]" />
            {weekdays.map((day, index) => <div key={day.toISOString()} className={`border-r border-[var(--line)] px-2 py-2.5 text-center last:border-r-0 ${day.toDateString() === new Date().toDateString() ? "bg-[var(--today-tint)]" : ""}`}><span className="block text-[.66rem] font-semibold text-[var(--muted)]">{WEEKDAYS[index]}</span><span className="mt-1 block text-[.6rem] text-[var(--muted)]">{day.getDate()}</span></div>)}
            <TimeLabels hourWindow={hourWindow} />
            {weekdays.map((day) => <DayColumn key={day.toISOString()} day={day} lessons={lessons} colors={lessonColors} hourWindow={hourWindow} onOpen={setOpenLesson} />)}
          </div>
          <div className="space-y-4 border-t border-[var(--line)] p-4 md:hidden">
            {weekdays.map((day, index) => {
              const dayLessons = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString()).sort((a,b) => a.startAt.localeCompare(b.startAt));
              return (
                <div key={day.toISOString()}>
                  <h3 className="mb-2 flex items-center gap-2 text-xs font-bold"><span className={`grid h-7 w-7 place-items-center rounded-lg ${day.toDateString() === new Date().toDateString() ? "bg-[var(--brand)] text-white" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{day.getDate()}</span>{WEEKDAYS[index]}</h3>
                  <div className="space-y-2 pl-9">
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
        <span key={index} className="absolute right-2 -translate-y-1/2 text-[.53rem] tabular-nums text-[var(--muted)]" style={{ top: `${(index / totalHours) * 100}%` }}>
          {String(hourWindow.startHour + index).padStart(2, "0")}:00
        </span>
      ))}
    </div>
  );
}

function DayColumn({ day, lessons, colors, hourWindow, onOpen }: { day: Date; lessons: CalendarLesson[]; colors: Map<string, InstrumentTone>; hourWindow: { startHour: number; endHour: number }; onOpen: (lesson: CalendarLesson) => void }) {
  const entries = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString());
  const layout = useMemo(() => layoutDayLessons(entries, hourWindow), [entries, hourWindow]);
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
            style={{ top: `${position.top * 100}%`, height: `${position.height * 100}%`, left, width, minHeight: "1.85rem", background: tone.bg, borderLeftColor: tone.border, color: tone.text }}
          >
            {dot.label && <span className="absolute right-1 top-1 h-1.5 w-1.5 rounded-full" style={{ background: dot.color }} aria-label={dot.label} />}
            <span className={`block text-[.52rem] font-bold tabular-nums ${isCancelled ? "line-through" : ""}`}>{start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}–{end.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}</span>
            <span className={`mt-0.5 block truncate text-[.57rem] font-bold ${isCancelled ? "line-through" : ""}`}>{position.columns > 2 ? studentInitials(lesson.studentName) : lesson.studentName}</span>
            <span className="block truncate text-[.46rem] opacity-75">{lesson.instrumentName}</span>
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
    <button type="button" onClick={() => onOpen(lesson)} className={`pressable flex min-h-14 w-full items-center gap-3 rounded-xl border border-[var(--line)] bg-white p-2.5 text-left shadow-sm ${isCancelled ? "opacity-60" : ""}`}>
      <span className="h-9 w-1 shrink-0 rounded-full" style={{ background: tone.border }} />
      <span className={`w-20 shrink-0 text-[.65rem] font-bold tabular-nums ${isCancelled ? "line-through" : ""}`} style={{ color: tone.text }}>{start.toLocaleTimeString("tr-TR", {hour:"2-digit",minute:"2-digit"})}–{end.toLocaleTimeString("tr-TR", {hour:"2-digit",minute:"2-digit"})}</span>
      <span className="min-w-0 flex-1">
        <span className={`block truncate text-xs font-bold ${isCancelled ? "line-through" : ""}`}>{lesson.studentName}</span>
        <span className="block truncate text-[.62rem] text-[var(--muted)]">{lesson.instrumentName} · {lesson.teacherName}</span>
      </span>
      {dot.label && <span className="shrink-0 h-1.5 w-1.5 rounded-full" style={{ background: dot.color }} aria-label={dot.label} />}
    </button>
  );
}

function LessonPopover({ lesson, tone, onClose }: { lesson: CalendarLesson; tone: InstrumentTone; onClose: () => void }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  const dot = rsvpDotTone(lesson);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-[#2b1a10]/40 p-4 backdrop-blur-[2px]" onClick={onClose}>
      <div role="dialog" aria-modal="true" aria-label={`${lesson.studentName} ders detayı`} onClick={(event) => event.stopPropagation()} className="app-card w-full max-w-[22rem] overflow-hidden">
        <div className="flex items-start justify-between gap-2 border-l-4 p-4" style={{ borderLeftColor: tone.border, background: tone.bg }}>
          <div className="min-w-0">
            <p className="truncate text-sm font-bold" style={{ color: tone.text }}>{lesson.studentName}</p>
            <p className="mt-0.5 text-[.7rem] font-semibold" style={{ color: tone.text }}>{lesson.instrumentName}</p>
          </div>
          <button onClick={onClose} className="pressable grid h-8 w-8 shrink-0 place-items-center rounded-lg hover:bg-black/5" aria-label="Kapat"><Icon name="close" className="h-4 w-4" /></button>
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
  return <div className="grid h-[21.5rem] grid-cols-5 gap-3 border-t border-[var(--line)] p-4">{Array.from({ length: 5 }, (_, index) => <div key={index} className="skeleton rounded-xl" />)}</div>;
}

function AdminAttentionRail({ lessons }: { lessons: CalendarLesson[] }) {
  const { data: requests, isLoading } = usePendingChangeRequests();
  const { data: bankItems } = useBankTransactions("NeedsReview", 1, 3);
  const approve = useApproveChangeRequest();
  const reject = useRejectChangeRequest();
  const [busyId, setBusyId] = useState<string | null>(null);
  const { data: attentionStudents } = useAttentionNeededStudents();

  async function act(id: string, action: "approve" | "reject") {
    setBusyId(id);
    try { await (action === "approve" ? approve.mutateAsync(id) : reject.mutateAsync(id)); }
    finally { setBusyId(null); }
  }

  return (
    <aside className="grid gap-4 md:grid-cols-2 xl:grid-cols-1">
      <section className="app-card p-4">
        <div className="mb-3 flex items-center justify-between"><h2 className="text-xs font-bold">Bekleyen Değişiklik Talepleri</h2><Link href="/dashboard/change-requests" className="text-[.62rem] font-bold text-[var(--brand)]">Tümünü gör</Link></div>
        {isLoading && <div className="skeleton h-28 rounded-xl" />}
        {!isLoading && !requests?.length && <EmptyRail text="Bekleyen talep yok." />}
        <div className="divide-y divide-[var(--line)]">
          {requests?.slice(0, 3).map((request) => {
            const lesson = lessons.find((item) => item.id === request.lessonId);
            return <div key={request.id} className="flex items-center gap-2 py-3 first:pt-0 last:pb-0"><span className="min-w-0 flex-1"><span className="block truncate text-[.7rem] font-bold">{lesson?.studentName ?? "Ders değişikliği"}</span><span className="mt-0.5 block text-[.56rem] text-[var(--muted)]">{new Date(request.proposedStartAt).toLocaleString("tr-TR", { weekday:"short", hour:"2-digit", minute:"2-digit" })}</span></span><button disabled={busyId === request.id} onClick={() => act(request.id,"approve")} className="pressable grid h-8 w-8 place-items-center rounded-lg bg-[var(--success-soft)] text-[var(--success-strong)] disabled:opacity-50" aria-label="Talebi onayla"><Icon name="check" className="h-4 w-4" /></button><button disabled={busyId === request.id} onClick={() => act(request.id,"reject")} className="pressable grid h-8 w-8 place-items-center rounded-lg bg-[var(--danger-soft)] text-[var(--danger-strong)] disabled:opacity-50" aria-label="Talebi reddet"><Icon name="x" className="h-4 w-4" /></button></div>;
          })}
        </div>
      </section>

      <section className="app-card p-4">
        <div className="mb-3 flex items-center justify-between"><h2 className="text-xs font-bold">Gözden Geçirilmesi Gereken Banka İşlemleri</h2><Link href="/dashboard/banking" className="text-[.62rem] font-bold text-[var(--brand)]">Tümünü gör</Link></div>
        {!bankItems?.items.length && <EmptyRail text="İncelenecek işlem yok." />}
        <div className="divide-y divide-[var(--line)]">
          {bankItems?.items.map((item) => (
            <Link key={item.id} href="/dashboard/banking" className="pressable flex items-center justify-between gap-3 py-3 first:pt-0 last:pb-0">
              <span className="min-w-0">
                <span className="block truncate text-[.7rem] font-bold tabular-nums">{formatMoney(item.amount)} {item.currency}</span>
                <span className="mt-0.5 block truncate text-[.56rem] text-[var(--muted)]">{item.senderName ?? "İsimsiz gönderici"}{item.description ? ` · ${item.description}` : ""}</span>
              </span>
              <span className="pressable shrink-0 rounded-lg border border-[var(--line)] bg-white px-2.5 py-1.5 text-[.62rem] font-bold text-[var(--brand)]">İncele</span>
            </Link>
          ))}
        </div>
      </section>

      <section className="app-card p-4">
        <div className="mb-3 flex items-center justify-between"><h2 className="text-xs font-bold">İlgi Gerektirebilecek Öğrenciler</h2><span className="text-[.6rem] text-[var(--muted)]">Açıklanabilir sinyal</span></div>
        {!attentionStudents?.length && <EmptyRail text="Şu an uyarı üreten bir sinyal yok." />}
        <div className="divide-y divide-[var(--line)]">{attentionStudents?.slice(0, 4).map((student) => <Link key={student.studentId} href={`/dashboard/students#student-${student.studentId}`} className="pressable block py-3 first:pt-0 last:pb-0"><span className="block text-[.7rem] font-bold">{student.studentName}</span><span className="mt-1 block text-[.58rem] leading-relaxed text-[var(--danger-strong)]">İlgi gerektirebilir · {student.reasons.join(" · ")}</span></Link>)}</div>
      </section>
    </aside>
  );
}

function EmptyRail({ text }: { text: string }) { return <p className="rounded-xl bg-[var(--surface-muted)] px-3 py-5 text-center text-[.65rem] text-[var(--muted)]">{text}</p>; }

function TeacherDashboard({ email }: { email: string }) {
  const [selectedDate, setSelectedDate] = useState(() => new Date());
  const weekStart = weekStartFor(new Date());
  const weekDays = Array.from({ length: 7 }, (_, index) => addDays(weekStart, index));
  return (
    <div className="mx-auto max-w-[32rem] xl:max-w-5xl">
      <header className="mb-3 flex items-start justify-between gap-3">
        <div><h1 className="text-[1.35rem] font-bold tracking-[-0.035em]">Bugün</h1><p className="mt-0.5 text-[.65rem] text-[var(--muted)]">{new Intl.DateTimeFormat("tr-TR", { day:"numeric", month:"long", weekday:"long" }).format(new Date())}</p></div>
        <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--brand-soft)] text-[.65rem] font-bold text-[var(--brand)]">{userName(email).slice(0,2).toLocaleUpperCase("tr-TR")}</span>
      </header>
      {/* Gün şeridi 390px'te taşarsa yatay kaydırılabilir (docs/14-ui-design-prompt.md C) - 7 gün
          sabit grid-cols-7 ile önceden dar ekranda okunmaz hale sıkışıyordu. */}
      <div className="mb-3 flex gap-1.5 overflow-x-auto pb-1 [scrollbar-width:none] sm:grid sm:grid-cols-7 sm:overflow-visible" style={{ scrollSnapType: "x proximity" }}>
        {weekDays.map((day) => {
          const active = day.toDateString() === selectedDate.toDateString();
          const isToday = day.toDateString() === new Date().toDateString();
          return (
            <button key={day.toISOString()} onClick={() => setSelectedDate(day)} style={{ scrollSnapAlign: "start" }} className={`pressable relative flex min-h-[3.2rem] w-12 shrink-0 flex-col items-center justify-center rounded-xl border text-[.55rem] sm:w-auto ${active ? "border-[var(--brand)] bg-[var(--brand)] text-white shadow-[0_7px_16px_rgba(168,78,31,.2)]" : "border-[var(--line)] bg-white text-[var(--muted)]"}`}>
              <span>{day.toLocaleDateString("tr-TR", { weekday:"short" }).replace(".","")}</span>
              <span className="mt-1 text-[.7rem] font-bold">{day.getDate()}</span>
              {isToday && !active && <span className="absolute bottom-1.5 h-1 w-1 rounded-full bg-[var(--brand)]" />}
            </button>
          );
        })}
      </div>
      <TeacherTodayLessons date={selectedDate} />
    </div>
  );
}
