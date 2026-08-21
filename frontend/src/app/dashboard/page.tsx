"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { Icon, type IconName } from "@/components/icons";
import { useApproveChangeRequest, usePendingChangeRequests, useRejectChangeRequest } from "@/lib/attendance";
import { useBankTransactions } from "@/lib/banking";
import { useReceivables } from "@/lib/billing";
import { useDashboardToday } from "@/lib/dashboard";
import { buildInstrumentColorMap, INSTRUMENT_TONES, type InstrumentTone } from "@/lib/lesson-colors";
import { useNotifications } from "@/lib/messaging";
import { useStudents, useTeachers } from "@/lib/people";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import { ChangePasswordForm } from "./change-password-form";
import { TeacherTodayLessons } from "./teacher-today-lessons";

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

function formatMoney(value: number) {
  return new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 0 }).format(value);
}

export default function DashboardPage() {
  const { data: me, refetch } = useMe();
  if (!me) return null;

  return (
    <div className="space-y-5">
      {me.mustChangePassword && (
        <section className="app-card border-[#e6c46e] bg-[#fff9e8] p-4">
          <p className="text-sm font-bold text-[#7f5d0d]">Güvenliğin için önce kalıcı bir şifre belirle.</p>
          <ChangePasswordForm onDone={() => refetch()} />
        </section>
      )}
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

      <section className="grid grid-cols-2 gap-3 xl:grid-cols-4" aria-label="Günün özeti">
        <StatCard icon="calendar" value={today?.todayLessons} label="Bugünkü Ders" loading={statsLoading} tone="purple" />
        <StatCard icon="swap" value={today?.pendingChangeRequests} label="Bekleyen Değişiklik Talebi" loading={statsLoading} tone="amber" href="/dashboard/change-requests" />
        <StatCard icon="wallet" value={`${overdueReceivables.length} kayıt · ₺${formatMoney(overdueTotal)}`} label="Vadesi Geçen Aidat" loading={statsLoading} tone="red" href="/dashboard/billing" />
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
        <h1 className="text-[1.45rem] font-bold tracking-[-0.035em] sm:text-[1.7rem]">Merhaba{userName(email) ? `, ${userName(email)}` : ""}</h1>
        <p className="mt-1 text-xs text-[var(--muted)]">
          {new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", weekday: "long" }).format(new Date())} · Okulun bugünkü akışı burada
        </p>
      </div>
      <div className="flex items-center gap-2">
        <div className="relative min-w-0 flex-1 xl:w-[19rem] xl:flex-none">
          <Icon name="search" className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#a59fab]" />
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
        <Link href="/dashboard/notifications" className="pressable relative grid h-11 w-11 shrink-0 place-items-center rounded-xl border border-[var(--line)] bg-white text-[#756f7a] shadow-sm" aria-label="Bildirimleri aç">
          <Icon name="bell" className="h-[1.1rem] w-[1.1rem]" />
          {!!failedNotifications?.totalCount && <span className="absolute right-2.5 top-2.5 h-1.5 w-1.5 rounded-full bg-[#e55955] ring-2 ring-white" />}
        </Link>
      </div>
    </header>
  );
}

type StatTone = "purple" | "amber" | "red" | "rose";
const STAT_TONES: Record<StatTone, { icon: string; iconBg: string; value: string }> = {
  purple: { icon: "#5e4caf", iconBg: "#eeebff", value: "#2d2934" },
  amber: { icon: "#b1760b", iconBg: "#f9ecd4", value: "#8b5b05" },
  red: { icon: "#c94848", iconBg: "#ffe5e2", value: "#ad3434" },
  rose: { icon: "#ca5b61", iconBg: "#ffe8e7", value: "#a63c43" },
};

function StatCard({ icon, value, label, detail, loading, tone, href }: { icon: IconName; value?: number | string; label: string; detail?: string; loading: boolean; tone: StatTone; href?: string }) {
  const palette = STAT_TONES[tone];
  const content = (
    <div className="flex h-full items-start gap-3 p-4">
      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl" style={{ color: palette.icon, background: palette.iconBg }}><Icon name={icon} className="h-[1.05rem] w-[1.05rem]" /></span>
      <span className="min-w-0 pt-0.5">
        {loading ? <span className="skeleton mb-2 block h-7 w-16 rounded-md" /> : <span className="block text-[1.4rem] font-bold leading-none tracking-[-0.04em]" style={{ color: palette.value }}>{value ?? 0}</span>}
        <span className="mt-2 block text-[.68rem] font-medium leading-snug text-[#6f6874]">{label}</span>
        {detail && <span className="mt-1 block text-[.6rem] text-[var(--muted)]">{detail}</span>}
      </span>
      {href && <Icon name="chevron" className="ml-auto mt-2 h-3.5 w-3.5 text-[#b5afb8]" />}
    </div>
  );
  return href ? <Link href={href} className="app-card pressable min-h-[6.8rem] overflow-hidden hover:-translate-y-0.5 hover:shadow-[0_12px_32px_rgba(38,31,24,.08)]">{content}</Link> : <article className="app-card min-h-[6.8rem] overflow-hidden">{content}</article>;
}

function WeeklySchedule({ weekStart, lessons, loading, onWeekChange }: { weekStart: Date; lessons: CalendarLesson[]; loading: boolean; onWeekChange: (offset: number) => void }) {
  const weekdays = Array.from({ length: 5 }, (_, index) => addDays(weekStart, index));
  const lessonColors = useMemo(() => buildInstrumentColorMap(lessons.map((lesson) => lesson.instrumentName)), [lessons]);

  return (
    <section className="app-card min-w-0 overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-4 sm:px-5">
        <div>
          <h2 className="text-sm font-bold">Bu Hafta</h2>
          <p className="mt-0.5 text-[.65rem] text-[var(--muted)]">{weekdays[0].toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} – {weekdays[4].toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}</p>
        </div>
        <div className="ml-auto hidden flex-wrap items-center justify-end gap-2 lg:flex">
          {[...lessonColors.entries()].slice(0, 5).map(([name, tone]) => <span key={name} className="inline-flex items-center gap-1 text-[.53rem] text-[var(--muted)]"><span className="h-1.5 w-1.5 rounded-full" style={{ background: tone.border }} />{name}</span>)}
        </div>
        <div className="flex items-center gap-1.5">
          <button onClick={() => onWeekChange(-1)} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Önceki hafta"><Icon name="arrow-left" className="h-4 w-4" /></button>
          <button onClick={() => onWeekChange(0)} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-[.68rem] font-semibold hover:bg-[var(--surface-muted)]">Bu hafta</button>
          <button onClick={() => onWeekChange(1)} className="pressable grid h-10 w-10 place-items-center rounded-xl border border-[var(--line)] bg-white hover:bg-[var(--surface-muted)]" aria-label="Sonraki hafta"><Icon name="arrow-right" className="h-4 w-4" /></button>
        </div>
      </div>

      {loading ? <ScheduleSkeleton /> : (
        <>
          <div className="hidden grid-cols-[3.2rem_repeat(5,minmax(0,1fr))] border-t border-[var(--line)] xl:grid">
            <div className="border-r border-[var(--line)]" />
            {weekdays.map((day, index) => <div key={day.toISOString()} className={`border-r border-[var(--line)] px-2 py-2.5 text-center last:border-r-0 ${day.toDateString() === new Date().toDateString() ? "bg-[#f0efff]" : ""}`}><span className="block text-[.66rem] font-semibold text-[#746d79]">{WEEKDAYS[index]}</span><span className="mt-1 block text-[.6rem] text-[var(--muted)]">{day.getDate()}</span></div>)}
            <TimeLabels />
            {weekdays.map((day) => <DayColumn key={day.toISOString()} day={day} lessons={lessons} colors={lessonColors} />)}
          </div>
          <div className="space-y-4 border-t border-[var(--line)] p-4 xl:hidden">
            {weekdays.map((day, index) => {
              const dayLessons = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString()).sort((a,b) => a.startAt.localeCompare(b.startAt));
              return (
                <div key={day.toISOString()}>
                  <h3 className="mb-2 flex items-center gap-2 text-xs font-bold"><span className={`grid h-7 w-7 place-items-center rounded-lg ${day.toDateString() === new Date().toDateString() ? "bg-[var(--brand)] text-white" : "bg-[var(--surface-muted)] text-[#625b68]"}`}>{day.getDate()}</span>{WEEKDAYS[index]}</h3>
                  <div className="space-y-2 pl-9">
                    {dayLessons.map((lesson) => <AgendaLesson key={lesson.id} lesson={lesson} tone={lessonColors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0]} />)}
                    {!dayLessons.length && <p className="py-2 text-xs text-[#aaa3ad]">Planlanmış ders yok.</p>}
                  </div>
                </div>
              );
            })}
          </div>
        </>
      )}
    </section>
  );
}

function TimeLabels() {
  return <div className="relative h-[21.5rem] border-r border-t border-[var(--line)] bg-[#fbfaf7]">{Array.from({ length: 11 }, (_, index) => <span key={index} className="absolute right-2 -translate-y-1/2 text-[.53rem] tabular-nums text-[#aaa3ad]" style={{ top: `${index * 10}%` }}>{String(index + 9).padStart(2,"0")}:00</span>)}</div>;
}

function DayColumn({ day, lessons, colors }: { day: Date; lessons: CalendarLesson[]; colors: Map<string, InstrumentTone> }) {
  const entries = lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === day.toDateString());
  const isToday = day.toDateString() === new Date().toDateString();
  return (
    <div className={`relative h-[21.5rem] border-r border-t border-[var(--line)] last:border-r-0 ${isToday ? "bg-[#f2f1ff]" : "bg-[#fbfaf7]"}`}>
      {Array.from({ length: 10 }, (_, index) => <span key={index} className="absolute inset-x-0 border-t border-dashed border-[#ebe7e1]" style={{ top: `${(index + 1) * 10}%` }} />)}
      {entries.map((lesson) => {
        const start = new Date(lesson.startAt);
        const end = new Date(lesson.endAt);
        const startMinutes = start.getHours() * 60 + start.getMinutes() - 9 * 60;
        const duration = Math.max(30, (end.getTime() - start.getTime()) / 60000);
        const top = Math.max(0, Math.min(96, startMinutes / 600 * 100));
        const height = Math.max(6.5, Math.min(20, duration / 600 * 100));
        const tone = colors.get(lesson.instrumentName) ?? INSTRUMENT_TONES[0];
        return <Link key={lesson.id} href="/dashboard/calendar" title={`${lesson.studentName} · ${lesson.instrumentName} · ${lesson.teacherName}`} className="pressable absolute left-1.5 right-1.5 z-10 overflow-hidden rounded-md border-l-[3px] px-2 py-1 shadow-sm hover:z-20 hover:shadow-md" style={{ top: `${top}%`, height: `${height}%`, minHeight: "1.85rem", background: tone.bg, borderLeftColor: tone.border, color: tone.text }}><span className="block text-[.52rem] font-bold tabular-nums">{start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}–{end.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}</span><span className="mt-0.5 block truncate text-[.57rem] font-bold">{lesson.studentName}</span><span className="block truncate text-[.46rem] opacity-75">{lesson.instrumentName}</span></Link>;
      })}
    </div>
  );
}

function AgendaLesson({ lesson, tone }: { lesson: CalendarLesson; tone: InstrumentTone }) {
  const start = new Date(lesson.startAt);
  const end = new Date(lesson.endAt);
  return <Link href="/dashboard/calendar" className="pressable flex min-h-14 items-center gap-3 rounded-xl border border-[var(--line)] bg-white p-2.5 shadow-sm"><span className="h-9 w-1 rounded-full" style={{ background: tone.border }} /><span className="w-20 shrink-0 text-[.65rem] font-bold tabular-nums" style={{ color: tone.text }}>{start.toLocaleTimeString("tr-TR", {hour:"2-digit",minute:"2-digit"})}–{end.toLocaleTimeString("tr-TR", {hour:"2-digit",minute:"2-digit"})}</span><span className="min-w-0"><span className="block truncate text-xs font-bold">{lesson.studentName}</span><span className="block truncate text-[.62rem] text-[var(--muted)]">{lesson.instrumentName} · {lesson.teacherName}</span></span></Link>;
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
            return <div key={request.id} className="flex items-center gap-2 py-3 first:pt-0 last:pb-0"><span className="min-w-0 flex-1"><span className="block truncate text-[.7rem] font-bold">{lesson?.studentName ?? "Ders değişikliği"}</span><span className="mt-0.5 block text-[.56rem] text-[var(--muted)]">{new Date(request.proposedStartAt).toLocaleString("tr-TR", { weekday:"short", hour:"2-digit", minute:"2-digit" })}</span></span><button disabled={busyId === request.id} onClick={() => act(request.id,"approve")} className="pressable grid h-8 w-8 place-items-center rounded-lg bg-[#d8f3df] text-[#23834b] disabled:opacity-50" aria-label="Talebi onayla"><Icon name="check" className="h-4 w-4" /></button><button disabled={busyId === request.id} onClick={() => act(request.id,"reject")} className="pressable grid h-8 w-8 place-items-center rounded-lg bg-[#ffe2df] text-[#c94848] disabled:opacity-50" aria-label="Talebi reddet"><Icon name="x" className="h-4 w-4" /></button></div>;
          })}
        </div>
      </section>

      <section className="app-card p-4">
        <div className="mb-3 flex items-center justify-between"><h2 className="text-xs font-bold">İncelenecek Banka İşlemleri</h2><Link href="/dashboard/banking" className="text-[.62rem] font-bold text-[var(--brand)]">Tümünü gör</Link></div>
        {!bankItems?.items.length && <EmptyRail text="İncelenecek işlem yok." />}
        <div className="divide-y divide-[var(--line)]">
          {bankItems?.items.map((item) => <Link key={item.id} href="/dashboard/banking" className="pressable flex items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"><span className="min-w-0"><span className="block truncate text-[.7rem] font-bold">{item.senderName ?? "İsimsiz gönderici"}</span><span className="mt-0.5 block truncate text-[.56rem] text-[var(--muted)]">{item.description ?? "Açıklama yok"}</span></span><span className="shrink-0 text-[.7rem] font-bold">{formatMoney(item.amount)} {item.currency}</span></Link>)}
        </div>
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
    <div className="mx-auto max-w-[32rem] lg:max-w-3xl">
      <header className="mb-3 flex items-start justify-between gap-3">
        <div><h1 className="text-[1.35rem] font-bold tracking-[-0.035em]">Bugün</h1><p className="mt-0.5 text-[.65rem] text-[var(--muted)]">{new Intl.DateTimeFormat("tr-TR", { day:"numeric", month:"long", weekday:"long" }).format(new Date())}</p></div>
        <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--brand-soft)] text-[.65rem] font-bold text-[var(--brand)]">{userName(email).slice(0,2).toLocaleUpperCase("tr-TR")}</span>
      </header>
      <div className="mb-3 grid grid-cols-7 gap-1.5">
        {weekDays.map((day) => {
          const active = day.toDateString() === selectedDate.toDateString();
          return <button key={day.toISOString()} onClick={() => setSelectedDate(day)} className={`pressable flex min-h-[3.2rem] flex-col items-center justify-center rounded-xl border text-[.55rem] ${active ? "border-[var(--brand)] bg-[var(--brand)] text-white shadow-[0_7px_16px_rgba(74,55,143,.18)]" : "border-[var(--line)] bg-white text-[#746d79]"}`}><span>{day.toLocaleDateString("tr-TR", { weekday:"short" }).replace(".","")}</span><span className="mt-1 text-[.7rem] font-bold">{day.getDate()}</span></button>;
        })}
      </div>
      <TeacherTodayLessons date={selectedDate} />
    </div>
  );
}
