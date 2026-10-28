"use client";

import { useMemo } from "react";
import type { GuardianLesson } from "@/lib/guardian";
import { EmptyCard, LoadError, Pill, SkeletonStack, ViewHeader, formatDayLabel, formatTime, rsvpPill } from "./shared";

// Eski ekran yalnızca bugünden başlayan 7 günü gün gün seçtiriyordu; veli "bir sonraki ders ne
// zaman?" sorusu için gün gün dokunmak zorundaydı. Artık yüklenen 60 günün tamamı gün başlıklı
// tek bir ajanda listesi - kaydırmak yeterli.
export function CalendarView({ lessons, loading, error, refetch, today }: {
  lessons: GuardianLesson[] | undefined;
  loading: boolean;
  error: boolean;
  refetch: () => unknown;
  today: Date;
}) {
  const days = useMemo(() => {
    const groups = new Map<string, GuardianLesson[]>();
    for (const lesson of [...(lessons ?? [])].sort((a, b) => a.startAt.localeCompare(b.startAt))) {
      const key = new Date(lesson.startAt).toDateString();
      groups.set(key, [...(groups.get(key) ?? []), lesson]);
    }
    return [...groups.values()];
  }, [lessons]);

  return (
    <div>
      <ViewHeader title="Takvim" subtitle="Önümüzdeki iki ayın dersleri" />
      {loading ? (
        <SkeletonStack heights={["h-24", "h-24", "h-24"]} />
      ) : error ? (
        <LoadError message="Takvim yüklenemedi" onRetry={refetch} />
      ) : days.length === 0 ? (
        <EmptyCard icon="calendar">Önümüzdeki günlerde planlanmış ders yok.</EmptyCard>
      ) : (
        <div className="space-y-5">
          {days.map((dayLessons) => {
            const first = dayLessons[0]!;
            const label = formatDayLabel(first.startAt, today);
            const isToday = label === "Bugün";
            return (
              <section key={first.id}>
                <h2 className={`mb-2 text-sm font-bold first-letter:uppercase ${isToday ? "text-[var(--brand-strong)]" : "text-[var(--muted)]"}`}>
                  {label === "Bugün" || label === "Yarın"
                    ? `${label} · ${new Date(first.startAt).toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "long" })}`
                    : label}
                </h2>
                <div className="space-y-2">
                  {dayLessons.map((lesson) => <LessonRow key={lesson.id} lesson={lesson} highlight={isToday} />)}
                </div>
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}

function LessonRow({ lesson, highlight }: { lesson: GuardianLesson; highlight: boolean }) {
  const pill = rsvpPill(lesson);
  const cancelled = lesson.status === "Cancelled" || lesson.status === "Rescheduled";
  return (
    <article className={`app-card flex items-center gap-3 px-4 py-3 ${highlight ? "ring-2 ring-[var(--brand-soft)]" : ""} ${cancelled ? "opacity-70" : ""}`}>
      <div className="w-14 shrink-0 text-center">
        <p className={`text-base font-bold tabular-nums ${cancelled ? "line-through" : ""}`}>{formatTime(lesson.startAt)}</p>
        <p className="text-[.8125rem] tabular-nums text-[var(--muted)]">{formatTime(lesson.endAt)}</p>
      </div>
      <div className="h-10 w-px shrink-0 bg-[var(--line)]" />
      <div className="min-w-0 flex-1">
        <p className={`truncate text-[.9375rem] font-bold ${cancelled ? "line-through" : ""}`}>{lesson.instrumentName}</p>
        <p className="truncate text-[.8125rem] text-[var(--muted)]">{lesson.teacherName}</p>
      </div>
      {pill && <Pill tone={pill.tone} icon={pill.icon}>{pill.label}</Pill>}
    </article>
  );
}
