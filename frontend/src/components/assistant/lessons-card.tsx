"use client";

import { dayLabel, hhmm, localDateKey, type AssistantStudent } from "@/lib/assistant";
import { formatWeeklySchedule } from "@/lib/scheduling";
import { Actions, AskButton, AssistantCard, ErrorLine, GoLink, Loading, StudentHeading, WarningLine } from "./shared";
import { useStudentSchedule } from "./use-student-schedule";

// "Mennan'ın dersleri": haftalık program, sıradaki dersler ve araya giren resmi tatiller.
const SHOWN_LESSONS = 4;
const DAY_INDEX: Record<string, number> = { Sunday: 0, Monday: 1, Tuesday: 2, Wednesday: 3, Thursday: 4, Friday: 5, Saturday: 6 };

export function LessonsCard({ student }: { student: AssistantStudent }) {
  const schedule = useStudentSchedule(student);

  if (schedule.isLoading) return <AssistantCard><StudentHeading student={student} /><Loading>Dersler getiriliyor…</Loading></AssistantCard>;
  if (schedule.isError) return <AssistantCard><StudentHeading student={student} /><ErrorLine>Dersler şu an getirilemedi.</ErrorLine></AssistantCard>;

  const lessons = schedule.upcoming.slice(0, SHOWN_LESSONS);
  // Programın düştüğü gün resmi tatilse o hafta ders yoktur; öğrenciyi etkileyen tatiller.
  const weekdays = new Set(schedule.series.map((item) => DAY_INDEX[item.dayOfWeek]));
  const missedHolidays = Array.from(schedule.holidays)
    .filter(([date]) => weekdays.has(new Date(`${date}T00:00:00`).getDay()) && date >= localDateKey(new Date()))
    .slice(0, 2);

  return (
    <AssistantCard>
      <StudentHeading student={student} />

      {schedule.series.length > 0
        ? <ul className="space-y-1 text-sm">{schedule.series.map((item) => <li key={item.id}><b>{formatWeeklySchedule(item)}</b> <span className="text-meta">· {item.instrumentName} · {item.teacherName}</span></li>)}</ul>
        : <p className="text-meta text-sm">Haftalık ders programı yok.</p>}

      {missedHolidays.map(([date, label]) => (
        <WarningLine key={date}>{dayLabel(new Date(`${date}T00:00:00`))} resmi tatil ({label}); o gün ders yok.</WarningLine>
      ))}

      {lessons.length > 0
        ? (
          <ul className="divide-y divide-[var(--line)]">
            {lessons.map((lesson, index) => (
              <li key={lesson.id} className="flex items-center justify-between gap-3 py-2">
                <span className="min-w-0">
                  <span className="block text-sm font-bold">{dayLabel(new Date(lesson.startAt))} · {hhmm(new Date(lesson.startAt))}–{hhmm(new Date(lesson.endAt))}</span>
                  <span className="text-meta text-xs">{lesson.instrumentName} · {lesson.teacherName}</span>
                </span>
                {(lesson.status === "Makeup" || index === 0) && (
                  <span className={`shrink-0 rounded-full px-2 py-0.5 text-[.7rem] font-bold ${lesson.status === "Makeup" ? "bg-[var(--warning-soft)] text-[var(--warning-strong)]" : "bg-[var(--brand-soft)] text-[var(--brand-strong)]"}`}>
                    {lesson.status === "Makeup" ? "Telafi" : "Sıradaki"}
                  </span>
                )}
              </li>
            ))}
          </ul>
        )
        : <p className="text-meta text-sm">Önümüzdeki 5 haftada planlı dersi yok.</p>}

      <Actions>
        {schedule.series.length > 0 && <AskButton text="ders saatini değiştir" studentId={student.studentId}>Saatini değiştir</AskButton>}
        <AskButton text="telafi planla" studentId={student.studentId}>Telafi planla</AskButton>
        <GoLink href="/dashboard/calendar">Takvim</GoLink>
      </Actions>
    </AssistantCard>
  );
}
