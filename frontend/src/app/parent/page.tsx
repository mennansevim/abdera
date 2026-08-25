"use client";

import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Icon, type IconName } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useRequireGuardianAuth } from "@/lib/guardian-auth";
import {
  useGuardianBilling,
  useGuardianCalendar,
  useGuardianMessages,
  useGuardianPracticeJournal,
  useCreateGuardianPracticeEntry,
  useGuardianProgress,
  useGuardianStudents,
  useRespondRsvp,
  type GuardianBilling,
  type GuardianLesson,
  type GuardianMessage,
  type GuardianProgress,
  type PracticeJournal,
  type GuardianStudent,
} from "@/lib/guardian";
import { useLogout } from "@/lib/use-auth";

type ParentTab = "home" | "calendar" | "progress" | "billing" | "messages";

const WEEKDAY_SHORT_FALLBACK = ["Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz"];

function addDays(date: Date, days: number) {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}

export default function ParentPage() {
  const router = useRouter();
  const logout = useLogout();
  const { guardian, isLoading: guardianLoading } = useRequireGuardianAuth();
  const { data: students, isLoading: studentsLoading } = useGuardianStudents();
  const { data: billing, isLoading: billingLoading } = useGuardianBilling();
  const { data: messages, isLoading: messagesLoading } = useGuardianMessages();
  const [tab, setTab] = useState<ParentTab>("home");
  const [selectedDay, setSelectedDay] = useState(1);
  const [studentIndex, setStudentIndex] = useState(0);
  const [today] = useState(() => new Date());

  const selectedStudent = students?.[studentIndex % Math.max(students.length, 1)];
  const from = useMemo(() => today.toISOString(), [today]);
  const to = useMemo(() => addDays(today, 60).toISOString(), [today]);
  const { data: lessons } = useGuardianCalendar(selectedStudent?.studentId, from, to);
  const { data: progress, isLoading: progressLoading } = useGuardianProgress(selectedStudent?.studentId);
  const { data: practiceJournal, isLoading: practiceJournalLoading } = useGuardianPracticeJournal(selectedStudent?.studentId);

  function handleLogout() {
    logout.mutate(undefined, { onSuccess: () => router.push("/parent/login") });
  }

  if (guardianLoading || studentsLoading || !guardian) {
    return <main className="grid min-h-dvh place-items-center bg-[#efede6]"><div className="skeleton h-12 w-12 rounded-2xl" /></main>;
  }

  const initials = selectedStudent ? `${selectedStudent.firstName.charAt(0)}${selectedStudent.lastName.charAt(0)}`.toLocaleUpperCase("tr-TR") : "?";

  return (
    <main className="min-h-dvh bg-[#efede6] sm:grid sm:place-items-center sm:p-6">
      <section className="relative mx-auto min-h-dvh w-full max-w-[390px] overflow-hidden border-[#dfd9d0] bg-[#fbf9f5] shadow-[0_12px_40px_rgba(44,35,28,.1)] sm:min-h-[760px] sm:rounded-[1.4rem] sm:border">
        <div className="h-full overflow-y-auto px-4 pb-24 pt-4">
          <header className="mb-4 flex items-center gap-3">
            <span className="grid h-10 w-10 place-items-center rounded-xl bg-[linear-gradient(145deg,#d99a22,#a96606)] text-xs font-bold text-white">{initials}</span>
            {selectedStudent ? (
              <span className="min-w-0 flex-1">
                <span className="block text-sm font-bold">{selectedStudent.firstName} {selectedStudent.lastName}</span>
                <span className="mt-0.5 block text-[.62rem] text-[var(--muted)]">
                  {selectedStudent.instrumentName ?? "Aktif kayıt yok"}{selectedStudent.teacherName ? ` · ${selectedStudent.teacherName}` : ""}
                </span>
              </span>
            ) : (
              <span className="min-w-0 flex-1 text-xs text-[var(--muted)]">Bağlı öğrenci bulunamadı</span>
            )}
            <HeaderMenu
              students={students}
              studentIndex={studentIndex}
              onSelectStudent={setStudentIndex}
              onLogout={handleLogout}
              loggingOut={logout.isPending}
            />
          </header>

          {tab === "home" && <HomeView lessons={lessons} today={today} studentId={selectedStudent?.studentId} billing={billing} messages={messages} />}
          {tab === "calendar" && <CalendarView lessons={lessons} today={today} selectedDay={selectedDay} setSelectedDay={setSelectedDay} />}
          {tab === "progress" && <ProgressView studentId={selectedStudent?.studentId} progress={progress} practiceJournal={practiceJournal} loading={progressLoading || practiceJournalLoading} />}
          {tab === "billing" && <BillingView billing={billing} loading={billingLoading} studentId={selectedStudent?.studentId} />}
          {tab === "messages" && <MessagesView messages={messages} loading={messagesLoading} />}
        </div>
        <ParentNavigation tab={tab} setTab={setTab} />
      </section>
    </main>
  );
}

// Önceden ayrı bir "+N öğrenci" butonu ve ayrı bir çıkış butonu vardı; mockup'ta ikisi tek bir
// "…" menüsünde toplanıyor (docs/14-ui-design-prompt.md D).
function HeaderMenu({ students, studentIndex, onSelectStudent, onLogout, loggingOut }: {
  students: GuardianStudent[] | undefined;
  studentIndex: number;
  onSelectStudent: (index: number) => void;
  onLogout: () => void;
  loggingOut: boolean;
}) {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") setOpen(false); };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [open]);

  return (
    <div className="relative shrink-0">
      <button onClick={() => setOpen((value) => !value)} className="pressable grid h-9 w-9 place-items-center rounded-xl border border-[var(--line)] bg-white text-[#756f7a]" aria-label="Menüyü aç" aria-expanded={open}>
        <Icon name="more" className="h-4 w-4" />
      </button>
      {open && (
        <>
          <button aria-label="Menüyü kapat" onClick={() => setOpen(false)} className="fixed inset-0 z-40 cursor-default" />
          <div role="menu" className="app-card absolute right-0 top-[calc(100%+.4rem)] z-50 w-56 overflow-hidden p-1.5">
            {students && students.length > 1 && (
              <>
                <p className="px-3 pb-1 pt-2 text-[.58rem] font-bold uppercase tracking-[.06em] text-[var(--muted)]">Öğrenciler</p>
                {students.map((student, index) => (
                  <button
                    key={student.studentId}
                    role="menuitemradio"
                    aria-checked={index === studentIndex % students.length}
                    onClick={() => { onSelectStudent(index); setOpen(false); }}
                    className={`pressable flex min-h-11 w-full items-center justify-between rounded-xl px-3 text-left text-xs font-semibold ${index === studentIndex % students.length ? "bg-[var(--brand-soft)] text-[var(--brand)]" : "text-[#514b59] hover:bg-black/[.035]"}`}
                  >
                    {student.firstName} {student.lastName}
                    {index === studentIndex % students.length && <Icon name="check" className="h-3.5 w-3.5" />}
                  </button>
                ))}
                <div className="my-1 border-t border-[var(--line)]" />
              </>
            )}
            <button onClick={onLogout} disabled={loggingOut} className="pressable flex min-h-11 w-full items-center gap-2 rounded-xl px-3 text-left text-xs font-semibold text-[#756f7a] hover:bg-black/[.035] disabled:opacity-50">
              <Icon name="logout" className="h-4 w-4" /> Çıkış yap
            </button>
          </div>
        </>
      )}
    </div>
  );
}

function nextRsvpableLesson(lessons: GuardianLesson[] | undefined, today: Date) {
  return lessons
    ?.filter((lesson) => lesson.status === "Normal" && new Date(lesson.endAt) > today)
    .sort((a, b) => a.startAt.localeCompare(b.startAt))[0];
}

function HomeView({ lessons, today, studentId, billing, messages }: {
  lessons: GuardianLesson[] | undefined;
  today: Date;
  studentId: string | undefined;
  billing: GuardianBilling | undefined;
  messages: GuardianMessage[] | undefined;
}) {
  const respondRsvp = useRespondRsvp();
  const [forceEditing, setForceEditing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nextLesson = nextRsvpableLesson(lessons, today);

  async function respond(response: "Attending" | "AttendingLate" | "NotAttending") {
    if (!nextLesson) return;
    setError(null);
    try {
      await respondRsvp.mutateAsync({ lessonId: nextLesson.id, response });
      setForceEditing(false);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Katılım yanıtı kaydedilemedi.");
    }
  }

  const rsvp = nextLesson?.rsvpResponse;
  const showButtons = forceEditing || !rsvp || rsvp === "Unknown";
  const studentBilling = billing?.enrollments.filter((item) => item.studentId === studentId);
  const outstanding = (studentBilling ?? [])
    .flatMap((item) => item.receivables)
    .filter((item) => item.status !== "Paid" && item.status !== "Cancelled");
  const outstandingTotal = outstanding.reduce((sum, item) => sum + Math.max(0, item.amount - item.totalPaid), 0);
  const availableMakeups = billing?.makeupCredits.filter((credit) => credit.studentId === studentId) ?? [];

  return (
    <div className="space-y-3">
      <section className="rounded-2xl bg-[linear-gradient(145deg,#fff0d9,#fff8ed)] p-4 shadow-[0_8px_20px_rgba(113,76,28,.08)]">
        <p className="text-[.57rem] font-bold uppercase tracking-[.08em] text-[#b07816]">Sıradaki Ders</p>
        {nextLesson ? (
          <>
            <h1 className="mt-1 font-serif text-[1.4rem] font-bold italic leading-tight text-[#403529]">{nextLesson.instrumentName} Dersi</h1>
            <p className="mt-1 text-[.65rem] text-[#776c60]">{formatLessonWhen(nextLesson.startAt, nextLesson.endAt, today)}</p>
            <p className="mt-0.5 text-[.62rem] text-[#9a8d7e]">{nextLesson.teacherName} ile</p>
            {showButtons ? (
              <div className="mt-4 grid grid-cols-3 gap-1.5">
                <button onClick={() => respond("Attending")} disabled={respondRsvp.isPending} className="pressable flex min-h-11 flex-col items-center justify-center gap-1 rounded-xl bg-[#36a561] px-1 text-[.62rem] font-bold text-white disabled:opacity-60"><Icon name="check" className="h-4 w-4" /> Geliyorum</button>
                <button onClick={() => respond("AttendingLate")} disabled={respondRsvp.isPending} className="pressable flex min-h-11 flex-col items-center justify-center gap-1 rounded-xl bg-[#d99a2b] px-1 text-[.62rem] font-bold text-white disabled:opacity-60"><Icon name="clock" className="h-4 w-4" /> Geç kalacağım</button>
                <button onClick={() => respond("NotAttending")} disabled={respondRsvp.isPending} className="pressable flex min-h-11 flex-col items-center justify-center gap-1 rounded-xl border border-[var(--line)] bg-white px-1 text-[.62rem] font-bold text-[#b84c4c] disabled:opacity-60"><Icon name="x" className="h-4 w-4" /> Gelemiyorum</button>
              </div>
            ) : (
              <>
                <p className={`mt-4 flex min-h-11 items-center justify-center gap-2 rounded-xl text-xs font-bold ${rsvp === "Attending" ? "bg-[#dcf3e4] text-[#227a49]" : rsvp === "AttendingLate" ? "bg-[#fbead0] text-[#9a6a1a]" : "bg-[#ffe2df] text-[#b3403c]"}`}>
                  <Icon name={rsvp === "Attending" ? "check" : rsvp === "AttendingLate" ? "clock" : "x"} className="h-4 w-4" />
                  {rsvp === "Attending" ? "Geliyorum olarak işaretlendi" : rsvp === "AttendingLate" ? "Geç kalacağım olarak işaretlendi" : "Gelemiyorum olarak işaretlendi"}
                </p>
                <button onClick={() => setForceEditing(true)} className="pressable mt-2 w-full text-center text-[.62rem] font-semibold text-[var(--muted)] underline">Yanıtını değiştir</button>
              </>
            )}
          </>
        ) : (
          <p className="mt-2 text-xs text-[#776c60]">Şu an planlanmış yaklaşan bir ders yok.</p>
        )}
        {error && <p role="alert" className="mt-3 rounded-xl bg-[#ffe8e5] p-3 text-xs font-semibold text-[#af4545]">{error}</p>}
      </section>
      <div className="grid grid-cols-2 gap-3">
        <InfoCard
          icon="wallet"
          label="Açık Aidat"
          value={outstanding.length ? formatMoney(outstandingTotal, outstanding[0]!.currency) : "—"}
          badge={outstanding.length ? "Ödenmedi" : "Güncel"}
          badgeTone={outstanding.length ? "red" : "green"}
          detail={outstanding[0] ? `Son vade: ${formatDate(outstanding[0].dueDate)}` : "Açık aidat bulunmuyor"}
        />
        <InfoCard
          icon="swap"
          label="Telafi Hakkı"
          value={`${availableMakeups.length} ders`}
          badge={availableMakeups.length ? "Kullanılabilir" : "Yok"}
          badgeTone={availableMakeups.length ? "green" : "red"}
          detail={availableMakeups[0] ? `Son kullanım: ${formatDateTime(availableMakeups[0].expiresAt)}` : "Kullanılabilir telafi yok"}
        />
      </div>
      <section>
        <h2 className="mb-2 text-xs font-bold">Son Bildirimler</h2>
        <div className="space-y-2">
          {messages?.slice(0, 3).map((message) => <MessageCard key={message.id} message={message} compact />)}
          {!messages?.length && <p className="app-card p-4 text-xs text-[var(--muted)]">Henüz bir bildirim yok.</p>}
        </div>
      </section>
    </div>
  );
}

function formatLessonWhen(startAt: string, endAt: string, today: Date) {
  const start = new Date(startAt);
  const end = new Date(endAt);
  const fullDate = start.toLocaleDateString("tr-TR", { day: "numeric", month: "long", weekday: "long" });
  const dateLabel = start.toDateString() === today.toDateString()
    ? `Bugün, ${fullDate}`
    : start.toDateString() === addDays(today, 1).toDateString()
      ? `Yarın, ${fullDate}`
      : fullDate;
  const time = `${start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}–${end.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}`;
  return `${dateLabel} · ${time}`;
}

// Takvim haftası değil, bugünden başlayan kayan 7 gün - bir sonraki takvim haftasına düşen bir
// telafi dersi, Pazartesi-başlangıçlı sabit bir haftada görünmez kalırdı.
function CalendarView({ lessons, today, selectedDay, setSelectedDay }: {
  lessons: GuardianLesson[] | undefined; today: Date; selectedDay: number; setSelectedDay: (index: number) => void;
}) {
  const weekDays = useMemo(() => Array.from({ length: 7 }, (_, index) => addDays(today, index)), [today]);
  const lessonsByDate = useMemo(() => {
    const map = new Map<string, GuardianLesson[]>();
    for (const lesson of lessons ?? []) {
      const key = new Date(lesson.startAt).toDateString();
      map.set(key, [...(map.get(key) ?? []), lesson]);
    }
    return map;
  }, [lessons]);

  const selected = weekDays[selectedDay];
  const dayLessons = (lessonsByDate.get(selected.toDateString()) ?? []).sort((a, b) => a.startAt.localeCompare(b.startAt));

  return (
    <div>
      <h1 className="text-xl font-bold">Takvim</h1>
      <p className="mt-1 text-xs text-[var(--muted)]">Yaklaşan derslerin</p>
      <div className="my-4 grid grid-cols-7 gap-1">
        {weekDays.map((day, index) => (
          <button key={day.toISOString()} onClick={() => setSelectedDay(index)} className={`pressable flex min-h-12 flex-col items-center justify-center rounded-xl text-[.55rem] font-semibold ${selectedDay === index ? "bg-[var(--brand)] text-white" : "border border-[var(--line)] bg-white text-[#746d79]"}`}>
            <span>{day.toLocaleDateString("tr-TR", { weekday: "short" }).replace(".", "") || WEEKDAY_SHORT_FALLBACK[index]}</span>
            <span className="mt-0.5 text-[.62rem] font-bold">{day.getDate()}</span>
          </button>
        ))}
      </div>
      <div className="space-y-3">
        {dayLessons.length ? dayLessons.map((lesson) => (
          <article key={lesson.id} className="app-card p-4">
            <p className="text-[.62rem] font-bold text-[var(--brand)]">{selected.toLocaleDateString("tr-TR", { day: "numeric", month: "long", weekday: "long" })}</p>
            <h2 className="mt-1 text-sm font-bold">{lesson.instrumentName} Dersi</h2>
            <p className="mt-1 text-xs text-[var(--muted)]">
              {new Date(lesson.startAt).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}–{new Date(lesson.endAt).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })} · {lesson.teacherName}
            </p>
          </article>
        )) : (
          <p className="app-card p-6 text-center text-xs text-[var(--muted)]">Bu gün için planlanmış ders yok.</p>
        )}
      </div>
    </div>
  );
}

function formatMoney(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
}

function formatDate(value: string) {
  return new Date(`${value}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long" });
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleDateString("tr-TR", { day: "numeric", month: "long" });
}

function BillingView({ billing, loading, studentId }: {
  billing: GuardianBilling | undefined;
  loading: boolean;
  studentId: string | undefined;
}) {
  const [copyState, setCopyState] = useState<"idle" | "copied" | "error">("idle");
  const enrollments = billing?.enrollments.filter((item) => item.studentId === studentId) ?? [];
  const availableMakeups = billing?.makeupCredits.filter((credit) => credit.studentId === studentId) ?? [];

  async function copyIban() {
    const iban = billing?.virtualIban?.iban;
    if (!iban) return;
    try {
      if (navigator.clipboard) {
        await navigator.clipboard.writeText(iban);
      } else {
        const textarea = document.createElement("textarea");
        textarea.value = iban;
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.select();
        const copied = document.execCommand("copy");
        textarea.remove();
        if (!copied) throw new Error("copy failed");
      }
      setCopyState("copied");
      window.setTimeout(() => setCopyState("idle"), 1800);
    } catch {
      setCopyState("error");
    }
  }

  if (loading) {
    return <div className="space-y-3"><div className="skeleton h-8 w-32 rounded-lg" /><div className="skeleton h-28 rounded-2xl" /><div className="skeleton h-40 rounded-2xl" /></div>;
  }

  return (
    <div>
      <h1 className="text-xl font-bold">Aidat</h1>
      <p className="mt-1 text-xs text-[var(--muted)]">Ödemeler ve dönem bilgisi</p>

      <div className="mt-4 space-y-3">
        {enrollments.map((enrollment) => (
          <section key={enrollment.enrollmentId} className="app-card p-4">
            <div className="flex items-start justify-between gap-3">
              <div><h2 className="text-sm font-bold">{enrollment.instrumentName}</h2><p className="mt-1 text-[.62rem] text-[var(--muted)]">{enrollment.teacherName}</p></div>
              <span className="rounded-full bg-[var(--brand-soft)] px-2 py-1 text-[.55rem] font-bold text-[var(--brand)]">{enrollment.studentName}</span>
            </div>
            <div className="mt-3 space-y-2">
              {enrollment.receivables.map((receivable) => {
                const remaining = Math.max(0, receivable.amount - receivable.totalPaid);
                const paid = receivable.status === "Paid";
                return <div key={receivable.id} className="flex items-center gap-2 rounded-xl border border-[var(--line)] bg-white p-3">
                  <span className={`grid h-8 w-8 shrink-0 place-items-center rounded-lg ${paid ? "bg-[#ddf2e2] text-[#2e7d49]" : "bg-[#ffe0de] text-[#c94b4b]"}`}><Icon name={paid ? "check" : "wallet"} className="h-3.5 w-3.5" /></span>
                  <span className="min-w-0 flex-1"><span className="block text-xs font-bold">{receivable.period}</span><span className="block text-[.58rem] text-[var(--muted)]">Vade: {formatDate(receivable.dueDate)} · {paid ? "Ödendi" : receivable.status === "Partial" ? "Kısmi ödeme" : "Açık"}</span></span>
                  <span className={`shrink-0 text-xs font-bold ${paid ? "text-[#297a45]" : "text-[#b3403c]"}`}>{formatMoney(remaining, receivable.currency)}</span>
                </div>;
              })}
              {!enrollment.receivables.length && <p className="rounded-xl bg-[var(--surface-muted)] p-3 text-xs text-[var(--muted)]">Henüz bu kayıt için aidat oluşturulmadı.</p>}
            </div>
          </section>
        ))}

        {!enrollments.length && <p className="app-card p-5 text-center text-xs text-[var(--muted)]">Bu öğrenci için henüz bir aidat kaydı yok.</p>}

        <article className="app-card p-4">
          <p className="text-xs font-bold">Ödeme bilgisi</p>
          {billing?.virtualIban ? (
            <>
              <p className="mt-2 text-xs leading-relaxed text-[var(--muted)]">Havale yaparken açıklama alanına öğrenci adını ve dönem bilgisini ekle.</p>
              <p className="mt-3 break-all rounded-xl bg-[var(--surface-muted)] px-3 py-2 text-xs font-bold tracking-wide">{billing.virtualIban.iban}</p>
              <p className="mt-1 text-[.55rem] text-[var(--muted)]">Sağlayıcı: {billing.virtualIban.provider}</p>
              <button className="pressable mt-3 min-h-11 w-full rounded-xl bg-[var(--brand)] text-xs font-bold text-white disabled:opacity-60" onClick={copyIban} disabled={copyState === "copied"}>
                {copyState === "copied" ? "IBAN kopyalandı" : "IBAN’ı kopyala"}
              </button>
              {copyState === "error" && <p role="alert" className="mt-2 text-center text-[.62rem] text-[#b3403c]">IBAN kopyalanamadı; yukarıdaki numarayı seçip kopyalayabilirsin.</p>}
            </>
          ) : <p className="mt-2 text-xs leading-relaxed text-[var(--muted)]">Okul henüz sana özel bir sanal IBAN tanımlamamış.</p>}
        </article>

        <article className="app-card p-4">
          <p className="text-xs font-bold">Telafi hakları</p>
          <p className="mt-1 text-[.62rem] text-[var(--muted)]">Kullanılabilir telafi: {availableMakeups.length}</p>
          {availableMakeups.map((credit) => <p key={credit.id} className="mt-2 rounded-xl bg-[#eaf8ed] px-3 py-2 text-[.62rem] font-semibold text-[#287747]">Son kullanım: {formatDateTime(credit.expiresAt)}</p>)}
        </article>
      </div>
    </div>
  );
}

function MessagesView({ messages, loading }: { messages: GuardianMessage[] | undefined; loading: boolean }) {
  return <div><h1 className="text-xl font-bold">Mesajlar</h1><p className="mt-1 text-xs text-[var(--muted)]">Okuldan gelen son bildirimler</p><div className="mt-4 space-y-2">{loading ? <div className="skeleton h-24 rounded-2xl" /> : messages?.length ? messages.map((message) => <MessageCard key={message.id} message={message} />) : <p className="app-card p-5 text-center text-xs text-[var(--muted)]">Henüz bir bildirim yok.</p>}</div></div>;
}

function ProgressView({ studentId, progress, practiceJournal, loading }: { studentId: string | undefined; progress: GuardianProgress | undefined; practiceJournal: PracticeJournal | undefined; loading: boolean }) {
  const total = (progress?.presentCount ?? 0) + (progress?.absentCount ?? 0) + (progress?.excusedCount ?? 0);
  const attendanceRate = total ? Math.round(((progress?.presentCount ?? 0) / total) * 100) : 0;
  const pieces = Array.from(new Set(progress?.entries.map((entry) => entry.pieceTitle).filter((piece): piece is string => !!piece) ?? []));
  if (loading) return <div className="space-y-3"><div className="skeleton h-8 w-32 rounded-lg" /><div className="skeleton h-28 rounded-2xl" /><div className="skeleton h-44 rounded-2xl" /></div>;
  return <div><h1 className="text-xl font-bold">Gelişim</h1><p className="mt-1 text-xs text-[var(--muted)]">Öğretmen notları, devamlılık, repertuvar ve çalışma günlüğü</p>
    <div className="mt-4 grid grid-cols-3 gap-2"><ParentMetric label="Katılım" value={`%${attendanceRate}`} tone="green" /><ParentMetric label="Devamsız" value={String(progress?.absentCount ?? 0)} tone={(progress?.absentCount ?? 0) > 1 ? "red" : "neutral"} /><ParentMetric label="Repertuvar" value={String(pieces.length)} tone="brand" /></div>
    {pieces.length > 0 && <section className="app-card mt-3 p-4"><p className="text-[.58rem] font-bold uppercase tracking-[.07em] text-[var(--muted)]">Çalışılan eserler</p><div className="mt-2 flex flex-wrap gap-1.5">{pieces.map((piece) => <span key={piece} className="rounded-full bg-[var(--brand-soft)] px-2.5 py-1.5 text-[.62rem] font-bold text-[var(--brand-strong)]">{piece}</span>)}</div></section>}
    <PracticeJournalPanel studentId={studentId} journal={practiceJournal} />
    <section className="mt-3 space-y-2"><h2 className="text-xs font-bold">Öğretmen değerlendirmeleri</h2>{progress?.entries.map((entry) => <article key={entry.id} className="app-card p-4"><div className="flex items-start justify-between gap-2"><div><p className="text-xs font-bold">{entry.instrumentName}</p><p className="mt-0.5 text-[.58rem] text-[var(--muted)]">{entry.teacherName} · {new Date(entry.lessonStartAt).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}</p></div>{entry.pieceDifficulty && <span className="rounded-full bg-[var(--surface-muted)] px-2 py-1 text-[.55rem] font-bold text-[var(--muted)]">Seviye {entry.pieceDifficulty}/5</span>}</div>{entry.parentComment && <p className="mt-3 text-xs leading-relaxed text-[#554e59]">{entry.parentComment}</p>}{entry.pieceTitle && <div className="mt-3 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] px-3 py-2 text-[.62rem]"><strong>{entry.pieceTitle}</strong>{entry.pieceComposer ? ` · ${entry.pieceComposer}` : ""}{entry.pieceResourceUrl && <a href={entry.pieceResourceUrl} target="_blank" rel="noreferrer" className="ml-2 font-bold text-[var(--brand)] underline">Nota / bağlantı</a>}</div>}{entry.practiced && <p className="mt-2 rounded-xl bg-[var(--surface-muted)] px-3 py-2 text-[.62rem] text-[var(--muted)]"><strong className="text-[var(--foreground)]">Çalışıldı:</strong> {entry.practiced}</p>}{entry.homework && <p className="mt-2 rounded-xl bg-[#fff3dd] px-3 py-2 text-[.62rem] text-[#7b5b20]"><strong>Ev çalışması:</strong> {entry.homework}</p>}{entry.nextGoal && <p className="mt-2 text-[.62rem] text-[var(--brand-strong)]"><strong>Sonraki hedef:</strong> {entry.nextGoal}</p>}</article>)}{!progress?.entries.length && <p className="app-card p-5 text-center text-xs text-[var(--muted)]">Henüz paylaşılmış gelişim değerlendirmesi yok.</p>}</section>
  </div>;
}

function PracticeJournalPanel({ studentId, journal }: { studentId: string | undefined; journal: PracticeJournal | undefined }) {
  const createEntry = useCreateGuardianPracticeEntry(studentId);
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [durationMinutes, setDurationMinutes] = useState("30");
  const [goal, setGoal] = useState("");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createEntry.mutateAsync({ date, durationMinutes: Number(durationMinutes), goal, note: note || undefined });
      setGoal("");
      setNote("");
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Çalışma kaydı eklenemedi.");
    }
  }

  return <section className="app-card mt-3 p-4"><div className="flex items-start justify-between gap-3"><div><p className="text-[.58rem] font-bold uppercase tracking-[.07em] text-[var(--muted)]">Çalışma günlüğü</p><p className="mt-1 text-xs font-bold">Toplam {journal?.totalMinutes ?? 0} dakika</p></div><div className="flex flex-wrap justify-end gap-1">{journal?.badges.map((badge) => <span key={badge} className="rounded-full bg-[#fff3dd] px-2 py-1 text-[.52rem] font-bold text-[#8a651d]">{badge}</span>)}</div></div>
    <form onSubmit={submit} className="mt-3 space-y-2"><div className="grid grid-cols-2 gap-2"><label className="text-[.56rem] font-bold text-[var(--muted)]">Tarih<input type="date" max={new Date().toISOString().slice(0, 10)} value={date} onChange={(event) => setDate(event.target.value)} required className="field mt-1 text-xs" /></label><label className="text-[.56rem] font-bold text-[var(--muted)]">Süre (dk)<input type="number" min="1" max="600" value={durationMinutes} onChange={(event) => setDurationMinutes(event.target.value)} required className="field mt-1 text-xs" /></label></div><input value={goal} onChange={(event) => setGoal(event.target.value)} required maxLength={500} placeholder="Bugünkü hedef" className="field text-xs" /><textarea value={note} onChange={(event) => setNote(event.target.value)} maxLength={2000} rows={2} placeholder="İsteğe bağlı not" className="field resize-y text-xs" />{error && <p role="alert" className="text-[.62rem] font-semibold text-[var(--danger-strong)]">{error}</p>}<button disabled={createEntry.isPending || !studentId} className="pressable min-h-10 w-full rounded-xl bg-[var(--brand)] text-xs font-bold text-white disabled:opacity-50">{createEntry.isPending ? "Kaydediliyor…" : "Çalışmayı kaydet ve onayla"}</button></form>
    {!!journal?.entries.length && <div className="mt-3 space-y-2 border-t border-[var(--line)] pt-3">{journal.entries.slice(0, 5).map((entry) => <article key={entry.id} className="rounded-xl bg-[var(--surface-muted)] px-3 py-2"><div className="flex justify-between gap-2 text-[.58rem] text-[var(--muted)]"><span>{new Date(`${entry.date}T00:00:00`).toLocaleDateString("tr-TR")}</span><span>{entry.durationMinutes} dk · {entry.parentApproved ? "Veli onaylı" : "Onay bekliyor"}</span></div><p className="mt-1 text-xs font-bold">{entry.goal}</p>{entry.note && <p className="mt-1 text-[.62rem] text-[var(--muted)]">{entry.note}</p>}</article>)}</div>}
  </section>;
}

function ParentMetric({ label, value, tone }: { label: string; value: string; tone: "green" | "red" | "brand" | "neutral" }) {
  const color = { green: "text-[#287747] bg-[#eaf8ed]", red: "text-[#b3403c] bg-[#ffe8e5]", brand: "text-[var(--brand-strong)] bg-[var(--brand-soft)]", neutral: "text-[var(--muted)] bg-[var(--surface-muted)]" }[tone];
  return <article className={`rounded-2xl p-3 text-center ${color}`}><p className="text-lg font-bold tabular-nums">{value}</p><p className="mt-1 text-[.55rem] font-bold">{label}</p></article>;
}

function InfoCard({ icon, label, value, badge, badgeTone, detail }: { icon: IconName; label: string; value: string; badge: string; badgeTone: "red"|"green"; detail: string }) {
  return <article className="app-card min-h-[7.4rem] p-3"><p className="flex items-center gap-1.5 text-[.58rem] text-[var(--muted)]"><Icon name={icon} className="h-3.5 w-3.5" />{label}</p><p className={`mt-2 text-base font-bold ${badgeTone === "green" ? "text-[#297a45]" : "text-[#302b35]"}`}>{value}</p><span className={`mt-2 inline-flex rounded-full px-2 py-1 text-[.5rem] font-bold ${badgeTone === "green" ? "bg-[#ddf2e2] text-[#2e7d49]" : "bg-[#ffe0de] text-[#c94b4b]"}`}>{badge}</span><p className="mt-1.5 text-[.5rem] text-[#a29ba5]">{detail}</p></article>;
}

// "2 saat önce" gibi göreli zaman - Son Bildirimler'de (Ana Sayfa) taramayı hızlandırır;
// Mesajlar sekmesindeki tam geçmişte mutlak tarih daha faydalı, o yüzden orada değişmedi.
function formatRelativeTime(value: string): string {
  const diffMs = Date.now() - new Date(value).getTime();
  const minutes = Math.round(diffMs / 60000);
  if (minutes < 1) return "az önce";
  if (minutes < 60) return `${minutes} dakika önce`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} saat önce`;
  const days = Math.round(hours / 24);
  if (days < 7) return `${days} gün önce`;
  return new Date(value).toLocaleDateString("tr-TR", { day: "numeric", month: "long" });
}

function MessageCard({ message, compact }: { message: GuardianMessage; compact?: boolean }) {
  return (
    <article className="flex gap-2.5 rounded-xl border border-[var(--line)] bg-white p-3 shadow-sm">
      <span className="grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="note" className="h-3.5 w-3.5" /></span>
      <span className="min-w-0">
        <span className={`block text-[.64rem] leading-relaxed text-[#554e59] ${compact ? "line-clamp-2" : ""}`}>{message.body}</span>
        <span className="mt-1 block text-[.52rem] text-[#a19aa5]">
          {compact ? formatRelativeTime(message.createdAt) : new Date(message.createdAt).toLocaleString("tr-TR", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" })}
        </span>
      </span>
    </article>
  );
}

function ParentNavigation({ tab, setTab }: { tab: ParentTab; setTab: (tab: ParentTab) => void }) {
  const items: { id: ParentTab; label: string; icon: IconName }[] = [{id:"home",label:"Ana Sayfa",icon:"home"},{id:"calendar",label:"Takvim",icon:"calendar"},{id:"progress",label:"Gelişim",icon:"activity"},{id:"billing",label:"Aidat",icon:"wallet"},{id:"messages",label:"Mesajlar",icon:"note"}];
  return <nav className="absolute inset-x-0 bottom-0 grid grid-cols-5 border-t border-[var(--line)] bg-white/95 px-1 pb-[max(.3rem,env(safe-area-inset-bottom))] pt-1 backdrop-blur-xl">{items.map(item=><button key={item.id} onClick={()=>setTab(item.id)} className={`pressable flex min-h-14 flex-col items-center justify-center gap-1 text-[.52rem] font-semibold ${tab===item.id?"text-[var(--brand)]":"text-[#9a949e]"}`}><Icon name={item.icon} className="h-4 w-4" />{item.label}</button>)}</nav>;
}
