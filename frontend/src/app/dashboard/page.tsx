"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { Icon, type IconName } from "@/components/icons";
import { useApproveChangeRequest, usePendingChangeRequests, useRejectChangeRequest } from "@/lib/attendance";
import { useBankTransactions } from "@/lib/banking";
import { useDashboardToday, type UpcomingBirthday } from "@/lib/dashboard";
import { buildInstrumentColorMap, INSTRUMENT_TONES } from "@/lib/lesson-colors";
import { useNotifications } from "@/lib/messaging";
import { useSystemHealth } from "@/lib/ops";
import { useAttentionNeededStudents, useStudents, useTeachers } from "@/lib/people";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import { Panel, StatStrip } from "@/components/ui";
import { TeacherTodayLessons } from "./teacher-today-lessons";

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
  const { data: failedNotifications } = useNotifications("Failed", 1, 1);
  // Yan panel (talepler, banka, doğum günleri...) her açılışta KAPALI başlar: ilk görünen ekran
  // geniş takvim olsun (kullanıcı isteği). Kapalıyken dar şeritteki rozetler bekleyen iş olduğunu
  // söyler; tercih bilerek saklanmıyor ki ekran her seferinde geniş takvimle açılsın.
  const [railOpen, setRailOpen] = useState(false);

  return (
    <>
      <DashboardTopbar email={email} />
      <SystemHealthBanner />

      <StatStrip
        label="Günün özeti"
        items={[
          { key: "today", label: "Bugünkü ders", value: today?.todayLessons ?? 0, loading: statsLoading, href: "/dashboard/calendar" },
          { key: "requests", label: "Bekleyen değişiklik talebi", value: today?.pendingChangeRequests ?? 0, tone: today?.pendingChangeRequests ? "warning" : undefined, loading: statsLoading, href: "/dashboard/change-requests" },
          { key: "failed", label: "Gönderilemeyen bildirim", value: failedNotifications?.totalCount ?? 0, tone: failedNotifications?.totalCount ? "danger" : undefined, loading: statsLoading, href: "/dashboard/notifications" },
        ]}
      />

      <div className={`grid items-start gap-3 ${railOpen ? "xl:grid-cols-[minmax(0,1fr)_20rem]" : "xl:grid-cols-[minmax(0,1fr)_3.5rem]"}`}>
        <WeeklySummary weekStart={weekStart} lessons={lessons ?? []} loading={lessonsLoading} error={lessonsError} retrying={lessonsFetching} onRetry={() => void refetchLessons()} onWeekChange={(offset) => setWeekStart(offset === 0 ? weekStartFor(new Date()) : addDays(weekStart, offset * 7))} />
        <AdminAttentionRail lessons={lessons ?? []} birthdays={today?.upcomingBirthdays} open={railOpen} onToggle={() => setRailOpen((value) => !value)} />
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

// Haftanın ÖZETİ: gün gün hangi enstrümandan kaç ders var, hangi öğretmenin kaç dersi var.
// Eskiden burada takvim ekranının küçük bir kopyası (saat ızgarası) duruyordu; kullanıcı geri
// bildirimi: "takvim ekranının aynısı olmamalı, özet olmalı". Ders ders ayrıntı ve her tür
// işlem takvim ekranındadır ("Takvimi aç"); burası yalnızca sayar.
const WEEKDAY_NAMES = ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"];

function countBy(lessons: CalendarLesson[], key: (lesson: CalendarLesson) => string) {
  const counts = new Map<string, number>();
  for (const lesson of lessons) counts.set(key(lesson), (counts.get(key(lesson)) ?? 0) + 1);
  return [...counts].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0], "tr-TR"));
}

function WeeklySummary({ weekStart, lessons: allLessons, loading, error, retrying, onRetry, onWeekChange }: { weekStart: Date; lessons: CalendarLesson[]; loading: boolean; error: boolean; retrying: boolean; onRetry: () => void; onWeekChange: (offset: number) => void }) {
  // Ertelenen dersin eski satırı ve iptal edilen ders sayılmaz - takvim ızgarasıyla aynı kural.
  const lessons = useMemo(() => allLessons.filter((lesson) => lesson.status !== "Rescheduled" && lesson.status !== "Cancelled"), [allLessons]);
  const lessonColors = useMemo(() => buildInstrumentColorMap(lessons.map((lesson) => lesson.instrumentName)), [lessons]);
  const days = Array.from({ length: 7 }, (_, index) => {
    const date = addDays(weekStart, index);
    return { date, name: WEEKDAY_NAMES[index]!, lessons: lessons.filter((lesson) => new Date(lesson.startAt).toDateString() === date.toDateString()) };
  // Pazar yalnızca dersi varsa gösterilir; okulun olağan haftası Pazartesi-Cumartesi.
  }).filter((day, index) => index < 6 || day.lessons.length > 0);
  const todayKey = new Date().toDateString();

  return (
    <section className="app-card min-w-0 overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 px-4 py-2">
        <div className="flex min-w-0 flex-wrap items-baseline gap-x-2">
          <h2 className="text-sm font-bold">Haftanın özeti</h2>
          <span className="text-meta">{days[0]!.date.toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} – {days[days.length - 1]!.date.toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} · {lessons.length} ders</span>
          <Link href="/dashboard/calendar" className="text-[.75rem] font-bold text-[var(--brand)] hover:underline">Takvimi aç</Link>
        </div>
        <div className="flex items-center gap-1.5">
          <button type="button" onClick={() => onWeekChange(-1)} className="icon-btn icon-btn-quiet" aria-label="Önceki hafta"><Icon name="arrow-left" className="h-4 w-4" /></button>
          <button type="button" onClick={() => onWeekChange(0)} className="btn btn-quiet px-3 text-[.75rem] font-semibold">Bu hafta</button>
          <button type="button" onClick={() => onWeekChange(1)} className="icon-btn icon-btn-quiet" aria-label="Sonraki hafta"><Icon name="arrow-right" className="h-4 w-4" /></button>
        </div>
      </div>

      {/* Hata boş günler gibi görünmesin ("Ders yok" yanıltıcı olur). */}
      {loading ? (
        <div className="grid gap-2 border-t border-[var(--line)] p-3 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-6">{Array.from({ length: 6 }, (_, index) => <div key={index} className="skeleton h-40 rounded-xl" />)}</div>
      ) : error ? (
        <div className="grid min-h-48 place-items-center border-t border-[var(--line)] p-8 text-center"><div><p className="text-sm font-bold">Ders programı yüklenemedi</p><p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p><button type="button" onClick={onRetry} disabled={retrying} className="btn btn-quiet mt-3 disabled:opacity-50">{retrying ? "Yükleniyor…" : "Tekrar dene"}</button></div></div>
      ) : (
        <ol className={`grid gap-2 border-t border-[var(--line)] p-3 sm:grid-cols-2 md:grid-cols-3 ${days.length > 6 ? "xl:grid-cols-7" : "xl:grid-cols-6"}`}>
          {days.map((day) => {
            const isToday = day.date.toDateString() === todayKey;
            const byInstrument = countBy(day.lessons, (lesson) => lesson.instrumentName);
            const byTeacher = countBy(day.lessons, (lesson) => lesson.teacherName);
            const notComing = day.lessons.filter((lesson) => lesson.rsvpResponse === "NotAttending").length;
            const late = day.lessons.filter((lesson) => lesson.rsvpResponse === "AttendingLate").length;
            return (
              <li key={day.date.toISOString()} className={`min-w-0 rounded-xl border p-3 ${isToday ? "border-[var(--brand)] bg-[var(--today-tint)]" : "border-[var(--line)] bg-white"}`}>
                {/* Gün adı kesilmesin diye sayı alt satırda: yedi sütunda "Pazartesi" + "3 ders" yan yana sığmıyor. */}
                <h3 className="text-xs font-bold">{day.name} <span className="font-semibold text-[var(--muted)]">{day.date.getDate()}</span></h3>
                <p className={`mt-0.5 tabular-nums ${day.lessons.length ? "text-base font-extrabold text-[var(--foreground)]" : "text-[.75rem] font-semibold text-[var(--muted)]"}`}>{day.lessons.length ? `${day.lessons.length} ders` : "Ders yok"}</p>
                {day.lessons.length > 0 && (
                  <>
                    <ul className="mt-2 flex flex-wrap gap-1" aria-label="Enstrümana göre ders sayısı">
                      {byInstrument.map(([name, count]) => {
                        const tone = lessonColors.get(name) ?? INSTRUMENT_TONES[0];
                        return <li key={name} className="rounded-md border px-1.5 py-0.5 text-[.72rem] font-bold tabular-nums" style={{ background: tone.bg, borderColor: tone.border, color: tone.text }}>{count} {name}</li>;
                      })}
                    </ul>
                    <ul className="mt-2 space-y-0.5 border-t border-[var(--line)] pt-2" aria-label="Öğretmene göre ders sayısı">
                      {byTeacher.map(([name, count]) => (
                        <li key={name} className="flex items-baseline justify-between gap-2 text-[.75rem]">
                          <span className="min-w-0 truncate">{name}</span>
                          <span className="shrink-0 font-bold tabular-nums text-[var(--muted)]">{count}</span>
                        </li>
                      ))}
                    </ul>
                    {(notComing > 0 || late > 0) && (
                      <p className="mt-2 text-[.72rem] font-bold text-[var(--danger-strong)]">
                        {[notComing > 0 ? `${notComing} gelemiyor` : null, late > 0 ? `${late} gecikecek` : null].filter(Boolean).join(" · ")}
                      </p>
                    )}
                  </>
                )}
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}

function AdminAttentionRail({ lessons, birthdays, open, onToggle }: { lessons: CalendarLesson[]; birthdays?: UpcomingBirthday[]; open: boolean; onToggle: () => void }) {
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

  // Kapalı şerit: bölüm başına bir simge + sayı rozeti. Geniş ekranda sağda dikey, dar ekranda
  // takvimin altında yatay durur; herhangi birine basmak paneli açar.
  const birthdaysToday = birthdays?.filter((item) => item.daysUntil === 0).length ?? 0;
  const sections = ([
    { key: "requests", icon: "swap", label: "Değişiklik talepleri", count: requests?.length ?? 0, badge: "bg-[var(--warning-strong)]" },
    { key: "bank", icon: "bank", label: "İncelenecek banka işlemleri", count: bankItems?.items.length ?? 0, badge: "bg-[var(--danger)]" },
    { key: "students", icon: "alert-triangle", label: "İlgi gerektirebilecek öğrenciler", count: attentionStudents?.length ?? 0, badge: "bg-[var(--danger)]" },
    { key: "birthdays", icon: "cake", label: birthdaysToday ? `Yaklaşan doğum günleri, ${birthdaysToday} tanesi bugün` : "Yaklaşan doğum günleri", count: birthdays?.length ?? 0, badge: "bg-[var(--brand)]" },
  ] satisfies { key: string; icon: IconName; label: string; count: number; badge: string }[]).filter((section) => section.count > 0);

  if (!open) {
    return (
      <aside aria-label="Dikkat gerektirenler" className="app-card flex flex-row flex-wrap items-center gap-1.5 p-1.5 xl:flex-col">
        <button type="button" onClick={onToggle} aria-expanded={false} aria-label="Dikkat gerektirenler panelini aç" title="Paneli aç" className="icon-btn icon-btn-quiet">
          <Icon name="chevrons-left" className="h-4 w-4 max-xl:rotate-[-90deg]" />
        </button>
        {railLoading && <span className="skeleton h-10 w-10 rounded-xl" aria-label="Dikkat gerektiren işler yükleniyor" />}
        {allClear && <span className="grid h-10 w-10 place-items-center rounded-xl bg-[var(--success-soft)] text-[var(--success-strong)]" role="img" aria-label="Bugün için her şey yolunda" title="Bugün için her şey yolunda"><Icon name="check" className="h-4 w-4" /></span>}
        {!railLoading && sections.map((section) => (
          <button key={section.key} type="button" onClick={onToggle} aria-label={`${section.label}: ${section.count}. Paneli aç`} title={`${section.label} (${section.count})`} className="pressable relative grid h-10 w-10 place-items-center rounded-xl text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">
            <Icon name={section.icon} className="h-[1.15rem] w-[1.15rem]" />
            <span className={`absolute -right-0.5 -top-0.5 grid h-[1.1rem] min-w-[1.1rem] place-items-center rounded-full px-1 text-[.65rem] font-extrabold leading-none text-white ${section.badge}`} aria-hidden="true">{section.count > 9 ? "9+" : section.count}</span>
          </button>
        ))}
      </aside>
    );
  }

  return (
    <aside aria-label="Dikkat gerektirenler" className="grid gap-3 md:grid-cols-2 xl:grid-cols-1">
      <div className="flex items-center justify-between gap-2 px-1 md:col-span-2 xl:col-span-1">
        <h2 className="text-micro text-[var(--muted)]">Dikkat gerektirenler</h2>
        <button type="button" onClick={onToggle} aria-expanded aria-label="Dikkat gerektirenler panelini kapat" title="Paneli kapat" className="icon-btn icon-btn-quiet">
          <Icon name="chevrons-right" className="h-4 w-4 max-xl:rotate-[-90deg]" />
        </button>
      </div>
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
