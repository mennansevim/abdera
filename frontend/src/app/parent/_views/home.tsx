"use client";

import { useState } from "react";
import { Icon, type IconName } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useRespondRsvp, type GuardianBilling, type GuardianLesson, type GuardianMessage, type GuardianRsvpResponse } from "@/lib/guardian";
import {
  EmptyCard,
  LoadError,
  MessageCard,
  Pill,
  SectionTitle,
  SkeletonStack,
  TextButton,
  formatDate,
  formatDayLabel,
  formatInstantDate,
  formatMoney,
  formatTimeRange,
  isOpenReceivable,
  remainingOf,
  rsvpPill,
} from "./shared";

type ParentTarget = "calendar" | "billing" | "messages";

function upcoming(lessons: GuardianLesson[] | undefined, today: Date) {
  return (lessons ?? [])
    // Rescheduled dersin ESKİ saatidir (yeni saat ayrı bir Normal ders olarak oluşur); iptal gibi
    // gerçekleşmeyecek, "sıradaki ders" sayılmaz.
    .filter((lesson) => lesson.status !== "Cancelled" && lesson.status !== "Rescheduled" && new Date(lesson.endAt) > today)
    .sort((a, b) => a.startAt.localeCompare(b.startAt));
}

export function HomeView({ lessons, lessonsLoading, lessonsError, refetchLessons, messages, messagesError, billing, studentId, today, onNavigate }: {
  lessons: GuardianLesson[] | undefined;
  lessonsLoading: boolean;
  lessonsError: boolean;
  refetchLessons: () => unknown;
  messages: GuardianMessage[] | undefined;
  messagesError: boolean;
  billing: GuardianBilling | undefined;
  studentId: string | undefined;
  today: Date;
  onNavigate: (target: ParentTarget) => void;
}) {
  const next = upcoming(lessons, today);
  // Katılım yanıtı yalnızca Normal derse verilebildiği için telafi dersinde düğmeler yerine not çıkar.
  const nextLesson = next[0];
  const laterLessons = next.filter((lesson) => lesson.id !== nextLesson?.id).slice(0, 2);

  const receivables = (billing?.enrollments ?? []).filter((item) => item.studentId === studentId).flatMap((item) => item.receivables);
  const open = receivables.filter(isOpenReceivable).sort((a, b) => a.dueDate.localeCompare(b.dueDate));
  const openTotal = open.reduce((sum, item) => sum + remainingOf(item), 0);
  const overdue = open.some((item) => item.status === "Overdue");
  const makeups = (billing?.makeupCredits ?? []).filter((credit) => credit.studentId === studentId)
    .sort((a, b) => a.expiresAt.localeCompare(b.expiresAt));

  return (
    <div>
      <h1 className="sr-only">Ana Sayfa</h1>

      {lessonsLoading ? (
        <SkeletonStack heights={["h-56"]} />
      ) : lessonsError ? (
        <LoadError message="Ders bilgisi yüklenemedi" onRetry={refetchLessons} />
      ) : nextLesson ? (
        <NextLessonCard lesson={nextLesson} today={today} />
      ) : (
        <EmptyCard icon="calendar">Şu an planlanmış yaklaşan bir ders yok.</EmptyCard>
      )}

      <div className="mt-3 grid grid-cols-2 gap-3">
        <StatusTile
          icon="wallet"
          label="Aidat"
          ariaLabel="Ödeme ayrıntılarını aç"
          onClick={() => onNavigate("billing")}
          value={open.length ? formatMoney(openTotal, open[0]!.currency) : receivables.length ? "Borç yok" : "—"}
          tone={open.length ? (overdue ? "red" : "amber") : receivables.length ? "green" : "neutral"}
          detail={open.length ? (overdue ? "Ödeme gecikti" : `Son gün ${formatDate(open[0]!.dueDate)}`) : receivables.length ? "Ödemeler güncel" : "Henüz aidat yok"}
        />
        <StatusTile
          icon="swap"
          label="Telafi hakkı"
          value={makeups.length ? `${makeups.length} ders` : "Yok"}
          tone={makeups.length ? "brand" : "neutral"}
          detail={makeups[0] ? `${formatInstantDate(makeups[0].expiresAt)} tarihine kadar` : "Kullanılabilir hak yok"}
        />
      </div>

      {laterLessons.length > 0 && (
        <>
          <SectionTitle action={<TextButton onClick={() => onNavigate("calendar")}>Tüm dersler</TextButton>}>Sonraki dersler</SectionTitle>
          <div className="app-card divide-y divide-[var(--line)] overflow-hidden">
            {laterLessons.map((lesson) => (
              <div key={lesson.id} className="flex items-center gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-bold first-letter:uppercase">{formatDayLabel(lesson.startAt, today)}</p>
                  <p className="text-[.8125rem] text-[var(--muted)]">{formatTimeRange(lesson.startAt, lesson.endAt)} · {lesson.instrumentName}</p>
                </div>
                {(() => { const pill = rsvpPill(lesson); return pill && <Pill tone={pill.tone} icon={pill.icon}>{pill.label}</Pill>; })()}
              </div>
            ))}
          </div>
        </>
      )}

      <SectionTitle action={messages && messages.length > 2 ? <TextButton onClick={() => onNavigate("messages")}>Tümü</TextButton> : undefined}>Son bildirimler</SectionTitle>
      <div className="space-y-2">
        {messagesError
          ? <p role="alert" className="app-card px-4 py-3 text-sm text-[var(--danger-strong)]">Bildirimler yüklenemedi.</p>
          : messages?.length
            ? messages.slice(0, 2).map((message) => <MessageCard key={message.id} message={message} compact />)
            : <EmptyCard icon="bell">Henüz bir bildirim yok.</EmptyCard>}
      </div>
    </div>
  );
}

const RSVP_CHOICES: { value: Exclude<GuardianRsvpResponse, "Unknown">; label: string; icon: IconName; className: string }[] = [
  { value: "Attending", label: "Geliyorum", icon: "check", className: "bg-[var(--success)] text-white" },
  { value: "AttendingLate", label: "Geç kalacağım", icon: "clock", className: "bg-[var(--warning)] text-white" },
  { value: "NotAttending", label: "Gelemiyorum", icon: "x", className: "border-[1.5px] border-[var(--danger)] bg-white text-[var(--danger-strong)]" },
];

const RSVP_DONE: Record<Exclude<GuardianRsvpResponse, "Unknown">, { text: string; className: string; icon: IconName }> = {
  Attending: { text: "Geleceğinizi bildirdiniz", className: "bg-[var(--success-soft)] text-[var(--success-strong)]", icon: "check" },
  AttendingLate: { text: "Geç kalacağınızı bildirdiniz", className: "bg-[var(--warning-soft)] text-[var(--warning-strong)]", icon: "clock" },
  NotAttending: { text: "Gelemeyeceğinizi bildirdiniz", className: "bg-[var(--danger-soft)] text-[var(--danger-strong)]", icon: "x" },
};

function NextLessonCard({ lesson, today }: { lesson: GuardianLesson; today: Date }) {
  const respondRsvp = useRespondRsvp();
  const [editing, setEditing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const answer = lesson.rsvpResponse === "Unknown" ? null : lesson.rsvpResponse;
  const canAnswer = lesson.status === "Normal";

  async function respond(response: Exclude<GuardianRsvpResponse, "Unknown">) {
    setError(null);
    try {
      await respondRsvp.mutateAsync({ lessonId: lesson.id, response });
      setEditing(false);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Yanıtın kaydedilemedi. Tekrar dene.");
    }
  }

  const dayLabel = formatDayLabel(lesson.startAt, today);
  const exactDate = new Date(lesson.startAt).toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "long" });

  return (
    <section aria-labelledby="next-lesson-title" className="overflow-hidden rounded-[1.4rem] bg-white shadow-[var(--shadow-card)] ring-1 ring-[var(--line)]">
      <div className="bg-[linear-gradient(140deg,#ea8a4c,#a84e1f)] px-5 pb-5 pt-4 text-white">
        <p className="text-[.8125rem] font-bold uppercase tracking-[.08em] opacity-85">Sıradaki ders</p>
        <h2 id="next-lesson-title" className="mt-1 text-[1.75rem] font-bold leading-tight first-letter:uppercase">{dayLabel}</h2>
        {dayLabel !== exactDate && <p className="text-sm opacity-90">{exactDate}</p>}
        <p className="mt-3 text-[1.35rem] font-bold tabular-nums">{formatTimeRange(lesson.startAt, lesson.endAt)}</p>
        <p className="mt-0.5 text-sm opacity-90">{lesson.instrumentName} · {lesson.teacherName}</p>
      </div>

      <div className="px-4 py-4">
        {!canAnswer ? (
          <p className="flex min-h-12 items-center gap-2 rounded-xl bg-[var(--surface-muted)] px-3 text-sm font-semibold">
            <Icon name="swap" className="h-4 w-4 text-[var(--brand-strong)]" />
            Bu bir telafi dersi.
          </p>
        ) : answer && !editing ? (
          <div className="flex items-center gap-2">
            <p className={`flex min-h-12 flex-1 items-center gap-2 rounded-xl px-3 text-sm font-bold ${RSVP_DONE[answer].className}`}>
              <Icon name={RSVP_DONE[answer].icon} className="h-4 w-4 shrink-0" />
              {RSVP_DONE[answer].text}
            </p>
            <button type="button" onClick={() => setEditing(true)} className="pressable min-h-12 shrink-0 rounded-xl border border-[var(--line)] px-4 text-sm font-bold text-[var(--muted)]">Değiştir</button>
          </div>
        ) : (
          <>
            <p className="mb-2.5 text-sm font-bold">Derse katılacak mı?</p>
            <div className="grid grid-cols-3 gap-2" role="group" aria-label="Derse katılım yanıtı">
              {RSVP_CHOICES.map((choice) => (
                <button
                  key={choice.value}
                  type="button"
                  onClick={() => respond(choice.value)}
                  disabled={respondRsvp.isPending}
                  aria-pressed={answer === choice.value}
                  className={`pressable flex min-h-16 flex-col items-center justify-center gap-1 rounded-2xl px-1 text-[.8125rem] font-bold leading-tight disabled:opacity-60 ${choice.className}`}
                >
                  <Icon name={choice.icon} className="h-5 w-5" />
                  {choice.label}
                </button>
              ))}
            </div>
            {editing && (
              <button type="button" onClick={() => setEditing(false)} className="pressable mt-1 min-h-11 w-full text-sm font-semibold text-[var(--muted)]">Vazgeç</button>
            )}
          </>
        )}
        {error && <p role="alert" className="mt-3 rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-sm font-semibold text-[var(--danger-strong)]">{error}</p>}
      </div>
    </section>
  );
}

const TILE_TONE = {
  red: "text-[var(--danger-strong)]",
  amber: "text-[var(--warning-strong)]",
  green: "text-[var(--success-strong)]",
  brand: "text-[var(--brand-strong)]",
  neutral: "text-[var(--foreground)]",
};

function StatusTile({ icon, label, value, detail, tone, onClick, ariaLabel }: {
  icon: IconName;
  label: string;
  value: string;
  detail: string;
  tone: keyof typeof TILE_TONE;
  onClick?: () => void;
  ariaLabel?: string;
}) {
  const body = (
    <>
      <span className="flex items-center justify-between text-[.8125rem] font-semibold text-[var(--muted)]">
        <span className="flex items-center gap-1.5"><Icon name={icon} className="h-4 w-4" />{label}</span>
        {onClick && <Icon name="chevron" className="h-4 w-4" />}
      </span>
      <span className={`mt-2 block text-[1.2rem] font-bold tabular-nums ${TILE_TONE[tone]}`}>{value}</span>
      <span className="mt-0.5 block text-[.8125rem] text-[var(--muted)]">{detail}</span>
    </>
  );
  // Butonun erişilebilir adı bilerek alt menü adlarını ("Aidat" gibi) içermez; aynı adı taşıyan
  // iki buton olursa hem ekran okuyucu hem e2e seçicileri (getByRole name) karışır.
  return onClick
    ? <button type="button" onClick={onClick} aria-label={ariaLabel} className="app-card pressable block p-4 text-left">{body}</button>
    : <div className="app-card p-4">{body}</div>;
}
