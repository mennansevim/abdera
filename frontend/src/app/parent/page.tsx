"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { Icon, type IconName } from "@/components/icons";
import { useRequireGuardianAuth } from "@/lib/guardian-auth";
import { useGuardianCalendar, useGuardianStudents, useRespondRsvp, type GuardianLesson } from "@/lib/guardian";
import { useLogout } from "@/lib/use-auth";

type ParentTab = "home" | "calendar" | "billing" | "messages";

// docs/10-decisions.md Karar F reversal - kapsam bilinçli olarak dar: yalnızca kendi
// öğrencisi/takvimi/RSVP'si gerçek veriden geliyor. Aidat ve bildirimler hâlâ mock - ayrı bir iş.
const messages = [
  { id: 1, text: "Ders hatırlatması — piyano dersin yarın 15:00’te. Katılım durumunuzu bildirir misiniz?", time: "Bugün, 14:02" },
  { id: 2, text: "Eylül ayı aidat hatırlatması: ₺2.500, son ödeme 5 Eylül.", time: "Dün, 10:15" },
  { id: 3, text: "Telafi dersi onaylandı — Ayşe Yılmaz ile.", time: "18 Ağustos" },
];

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
  const [tab, setTab] = useState<ParentTab>("home");
  const [selectedDay, setSelectedDay] = useState(1);
  const [studentIndex, setStudentIndex] = useState(0);
  const [today] = useState(() => new Date());

  const selectedStudent = students?.[studentIndex % Math.max(students.length, 1)];
  const from = useMemo(() => today.toISOString(), [today]);
  const to = useMemo(() => addDays(today, 60).toISOString(), [today]);
  const { data: lessons } = useGuardianCalendar(selectedStudent?.studentId, from, to);

  function handleLogout() {
    logout.mutate(undefined, { onSuccess: () => router.push("/login") });
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
            {(students?.length ?? 0) > 1 && (
              <button onClick={() => setStudentIndex((index) => index + 1)} className="pressable grid h-9 w-9 place-items-center rounded-xl border border-[var(--line)] bg-white text-[.62rem] text-[var(--muted)]" aria-label="Diğer öğrenciyi göster">+{(students!.length - 1)}</button>
            )}
            <button onClick={handleLogout} disabled={logout.isPending} className="pressable grid h-9 w-9 shrink-0 place-items-center rounded-xl border border-[var(--line)] bg-white text-[#756f7a] disabled:opacity-50" aria-label="Çıkış yap"><Icon name="logout" className="h-4 w-4" /></button>
          </header>

          {tab === "home" && <HomeView lessons={lessons} today={today} />}
          {tab === "calendar" && <CalendarView lessons={lessons} today={today} selectedDay={selectedDay} setSelectedDay={setSelectedDay} />}
          {tab === "billing" && <BillingView />}
          {tab === "messages" && <MessagesView />}
        </div>
        <ParentNavigation tab={tab} setTab={setTab} />
      </section>
    </main>
  );
}

function nextRsvpableLesson(lessons: GuardianLesson[] | undefined, today: Date) {
  return lessons
    ?.filter((lesson) => lesson.status === "Normal" && new Date(lesson.endAt) > today)
    .sort((a, b) => a.startAt.localeCompare(b.startAt))[0];
}

function HomeView({ lessons, today }: { lessons: GuardianLesson[] | undefined; today: Date }) {
  const respondRsvp = useRespondRsvp();
  const [forceEditing, setForceEditing] = useState(false);
  const nextLesson = nextRsvpableLesson(lessons, today);

  async function respond(response: "Attending" | "NotAttending") {
    if (!nextLesson) return;
    setForceEditing(false);
    await respondRsvp.mutateAsync({ lessonId: nextLesson.id, response });
  }

  const rsvp = nextLesson?.rsvpResponse;
  const showButtons = forceEditing || !rsvp || rsvp === "Unknown";

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
              <div className="mt-4 grid grid-cols-2 gap-2">
                <button onClick={() => respond("Attending")} disabled={respondRsvp.isPending} className="pressable flex min-h-11 items-center justify-center gap-2 rounded-xl bg-[#36a561] text-xs font-bold text-white disabled:opacity-60"><Icon name="check" className="h-4 w-4" /> Geliyorum</button>
                <button onClick={() => respond("NotAttending")} disabled={respondRsvp.isPending} className="pressable flex min-h-11 items-center justify-center gap-2 rounded-xl border border-[var(--line)] bg-white text-xs font-bold text-[#b84c4c] disabled:opacity-60"><Icon name="x" className="h-4 w-4" /> Gelemiyorum</button>
              </div>
            ) : (
              <>
                <p className={`mt-4 flex min-h-11 items-center justify-center gap-2 rounded-xl text-xs font-bold ${rsvp === "Attending" ? "bg-[#dcf3e4] text-[#227a49]" : "bg-[#ffe2df] text-[#b3403c]"}`}>
                  <Icon name={rsvp === "Attending" ? "check" : "x"} className="h-4 w-4" />
                  {rsvp === "Attending" ? "Geliyorum olarak işaretlendi" : "Gelemiyorum olarak işaretlendi"}
                </p>
                <button onClick={() => setForceEditing(true)} className="pressable mt-2 w-full text-center text-[.62rem] font-semibold text-[var(--muted)] underline">Yanıtını değiştir</button>
              </>
            )}
          </>
        ) : (
          <p className="mt-2 text-xs text-[#776c60]">Şu an planlanmış yaklaşan bir ders yok.</p>
        )}
      </section>
      <div className="grid grid-cols-2 gap-3">
        {/* Aidat ve telafi hakkı kartları henüz mock - docs/10-decisions.md Karar F reversal
            kapsamı yalnızca RSVP + takvim, aidat/bildirim ayrı bir iş. */}
        <InfoCard icon="wallet" label="Eylül Aidatı" value="₺2.500" badge="Ödenmedi" badgeTone="red" detail="Son ödeme: 5 Eylül" />
        <InfoCard icon="swap" label="Telafi Hakkı" value="2 ders" badge="Kullanılabilir" badgeTone="green" detail="Son kullanma: 15 Ekim" />
      </div>
      <section>
        <h2 className="mb-2 text-xs font-bold">Son Bildirimler</h2>
        <div className="space-y-2">{messages.map((message) => <MessageCard key={message.id} message={message} />)}</div>
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

// Mock - kapsam dışı (docs/10-decisions.md Karar F reversal yalnızca RSVP + takvim'i kapsıyor).
function BillingView() {
  return <div><h1 className="text-xl font-bold">Aidat</h1><p className="mt-1 text-xs text-[var(--muted)]">Ödemeler ve dönem bilgisi</p><div className="mt-4 space-y-3"><InfoCard icon="wallet" label="Eylül 2026" value="₺2.500" badge="Ödenmedi" badgeTone="red" detail="Son ödeme: 5 Eylül" /><article className="app-card p-4"><p className="text-xs font-bold">Ödeme bilgisi</p><p className="mt-2 text-xs leading-relaxed text-[var(--muted)]">Sana özel sanal IBAN üzerinden yapılan havaleler aidatına otomatik işlenir.</p><button className="pressable mt-3 min-h-11 w-full rounded-xl bg-[var(--brand)] text-xs font-bold text-white" onClick={() => navigator.clipboard?.writeText("TR942036008341259")}>IBAN’ı kopyala</button></article></div></div>;
}

// Mock - kapsam dışı (docs/10-decisions.md Karar F reversal yalnızca RSVP + takvim'i kapsıyor).
function MessagesView() {
  return <div><h1 className="text-xl font-bold">Mesajlar</h1><p className="mt-1 text-xs text-[var(--muted)]">Okuldan gelen son bildirimler</p><div className="mt-4 space-y-2">{messages.map((message)=><MessageCard key={message.id} message={message}/>)}</div></div>;
}

function InfoCard({ icon, label, value, badge, badgeTone, detail }: { icon: IconName; label: string; value: string; badge: string; badgeTone: "red"|"green"; detail: string }) {
  return <article className="app-card min-h-[7.4rem] p-3"><p className="flex items-center gap-1.5 text-[.58rem] text-[var(--muted)]"><Icon name={icon} className="h-3.5 w-3.5" />{label}</p><p className={`mt-2 text-base font-bold ${badgeTone === "green" ? "text-[#297a45]" : "text-[#302b35]"}`}>{value}</p><span className={`mt-2 inline-flex rounded-full px-2 py-1 text-[.5rem] font-bold ${badgeTone === "green" ? "bg-[#ddf2e2] text-[#2e7d49]" : "bg-[#ffe0de] text-[#c94b4b]"}`}>{badge}</span><p className="mt-1.5 text-[.5rem] text-[#a29ba5]">{detail}</p></article>;
}

function MessageCard({ message }: { message: typeof messages[number] }) {
  return <article className="flex gap-2.5 rounded-xl border border-[var(--line)] bg-white p-3 shadow-sm"><span className="grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="note" className="h-3.5 w-3.5" /></span><span><span className="block text-[.64rem] leading-relaxed text-[#554e59]">{message.text}</span><span className="mt-1 block text-[.52rem] text-[#a19aa5]">{message.time}</span></span></article>;
}

function ParentNavigation({ tab, setTab }: { tab: ParentTab; setTab: (tab: ParentTab) => void }) {
  const items: { id: ParentTab; label: string; icon: IconName }[] = [{id:"home",label:"Ana Sayfa",icon:"home"},{id:"calendar",label:"Takvim",icon:"calendar"},{id:"billing",label:"Aidat",icon:"wallet"},{id:"messages",label:"Mesajlar",icon:"note"}];
  return <nav className="absolute inset-x-0 bottom-0 grid grid-cols-4 border-t border-[var(--line)] bg-white/95 px-2 pb-[max(.3rem,env(safe-area-inset-bottom))] pt-1 backdrop-blur-xl">{items.map(item=><button key={item.id} onClick={()=>setTab(item.id)} className={`pressable flex min-h-14 flex-col items-center justify-center gap-1 text-[.57rem] font-semibold ${tab===item.id?"text-[var(--brand)]":"text-[#9a949e]"}`}><Icon name={item.icon} className="h-4 w-4" />{item.label}</button>)}</nav>;
}
