"use client";

import { useState, type FormEvent } from "react";
import { addDays, dayLabel, hhmm, localDateKey, type AssistantMove, type AssistantStudent } from "@/lib/assistant";
import { useRescheduleLesson } from "@/lib/attendance";
import { errorMessage } from "@/lib/library";
import { useRescheduleLessonSeries, useTeacherAvailability, useUpdateLesson, type CalendarLesson, type StudentLessonSeries, type TeacherAvailability } from "@/lib/scheduling";
import { checkManualSlot, findOpenSlots, findRecurringSlots, type SuggestedSlot } from "@/lib/smart-scheduling";
import { useMe } from "@/lib/use-auth";
import { Actions, AssistantCard, Choice, ConfirmBox, DoneLine, ErrorLine, GoLink, Loading, StudentHeading, WarningLine } from "./shared";
import { lessonsOfSeries, useStudentSchedule } from "./use-student-schedule";

// "Perşembe dersini 17:00'ye al". Adımlar: hangi ders (tatile denk gelen gün söylenir) →
// bir kerelik mi her hafta mı → yeni saat (öğretmen müsaitliği ve çakışma denetlenir; uygun
// değilse boş saat önerilir) → onay → takvimdeki taşımayla AYNI uç (AE2): her hafta için seri
// taşıma, tek ders için yöneticide talep+onay, öğretmende ders güncellemesi.
type Scope = "Once" | "Recurring";
const DAY_KEYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const RECURRING_CHECK_WEEKS = 4;

interface Source {
  lesson: CalendarLesson;
  notice: string | null;
}

export function RescheduleFlow({ student, move }: { student: AssistantStudent; move: AssistantMove | null }) {
  const { data: me } = useMe();
  const schedule = useStudentSchedule(student);
  const [chosenSeriesId, setChosenSeriesId] = useState<string | null>(null);
  const [scope, setScope] = useState<Scope | null>(move && move.scope !== "Unspecified" ? move.scope : null);
  const [picked, setPicked] = useState<Date | null>(null);
  const [manualDate, setManualDate] = useState("");
  const [manualTime, setManualTime] = useState("");
  const [cancelled, setCancelled] = useState(false);
  const [done, setDone] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const series = schedule.series.length === 1 ? schedule.series[0] : schedule.series.find((item) => item.id === chosenSeriesId);
  const availability = useTeacherAvailability(series?.teacherId ?? "");
  const rescheduleLesson = useRescheduleLesson();
  const updateLesson = useUpdateLesson();
  const rescheduleSeries = useRescheduleLessonSeries(student.studentId);
  const pending = rescheduleLesson.isPending || updateLesson.isPending || rescheduleSeries.isPending;

  if (schedule.isLoading) return <AssistantCard><StudentHeading student={student} /><Loading>Dersleri getiriyorum…</Loading></AssistantCard>;
  if (schedule.isError) return <AssistantCard><StudentHeading student={student} /><ErrorLine>Dersler şu an getirilemedi.</ErrorLine></AssistantCard>;

  if (schedule.series.length === 0) {
    return (
      <AssistantCard>
        <StudentHeading student={student} />
        <p className="text-sm">Haftalık ders programı yok. Tek seferlik bir dersi takvimden sürükleyerek taşıyabilirsin.</p>
        <Actions><GoLink href="/dashboard/calendar">Takvim</GoLink></Actions>
      </AssistantCard>
    );
  }

  if (!series) {
    return (
      <AssistantCard>
        <StudentHeading student={student} />
        <p className="text-sm">Hangi dersi?</p>
        <div className="flex flex-wrap gap-2">
          {schedule.series.map((item) => <Choice key={item.id} onClick={() => setChosenSeriesId(item.id)}>{item.instrumentName} · {item.teacherName}</Choice>)}
        </div>
      </AssistantCard>
    );
  }

  const source = findSource(lessonsOfSeries(schedule, series), move, schedule.holidays);
  if (!source) {
    // Kullanıcı bir gün söylediyse cevap o günü anmalı; genel "ders yok" yanıltıcı.
    const asked = move?.sourceDate ? dayLabel(new Date(`${move.sourceDate}T00:00:00`)) : null;
    return (
      <AssistantCard>
        <StudentHeading student={student} />
        <p className="text-sm">
          {asked
            ? `${asked} günü ${series.instrumentName} dersi yok; sonrasında da planlı ders görünmüyor.`
            : `${series.instrumentName} için önümüzdeki 5 haftada planlı ders görünmüyor.`}
        </p>
        <Actions><GoLink href="/dashboard/calendar">Takvim</GoLink></Actions>
      </AssistantCard>
    );
  }

  const lessonStart = new Date(source.lesson.startAt);
  const durationMinutes = Math.round((new Date(source.lesson.endAt).getTime() - lessonStart.getTime()) / 60000);
  const desired = picked ?? desiredFromMove(move, scope, lessonStart);
  const otherLessons = scope === "Recurring"
    ? schedule.allLessons.filter((lesson) => !(lesson.studentId === student.studentId && lesson.teacherId === series.teacherId && lesson.instrumentId === series.instrumentId && lesson.startAt >= source.lesson.startAt))
    : schedule.allLessons.filter((lesson) => lesson.id !== source.lesson.id);
  const problem = desired && scope
    ? checkTarget({ start: desired, scope, durationMinutes, lessonStart, series, studentId: student.studentId, availability: availability.data ?? [], lessons: otherLessons, holidays: schedule.holidays })
    : null;
  const suggestions = scope && (!desired || problem) && !availability.isLoading
    ? suggest({ scope, lessonStart, durationMinutes, series, studentId: student.studentId, availability: availability.data ?? [], lessons: otherLessons, holidays: schedule.holidays })
    : [];

  async function confirm(start: Date) {
    if (!series || !scope || !source) return;
    setError(null);
    const end = new Date(start.getTime() + durationMinutes * 60000);
    try {
      if (scope === "Recurring") {
        await rescheduleSeries.mutateAsync({
          seriesId: series.id,
          dayOfWeek: DAY_KEYS[start.getDay()]!,
          startTime: `${hhmm(start)}:00`,
          durationMinutes: series.durationMinutes,
          effectiveFrom: localDateKey(lessonStart),
        });
        setDone(`${student.fullName} için ${series.instrumentName} programı ${dayLabel(lessonStart)} dersinden itibaren her ${weekdayName(start)} ${hhmm(start)} oldu.`);
      } else if (me?.role === "Admin") {
        await rescheduleLesson.mutateAsync({ lessonId: source.lesson.id, proposedStartAt: start.toISOString(), proposedEndAt: end.toISOString() });
        setDone(`${student.fullName} dersi ${dayLabel(start)} ${hhmm(start)} saatine taşındı.`);
      } else {
        await updateLesson.mutateAsync({ lessonId: source.lesson.id, studentId: student.studentId, teacherId: source.lesson.teacherId, startAt: start.toISOString(), durationMinutes, status: "Normal" });
        setDone(`${student.fullName} dersi ${dayLabel(start)} ${hhmm(start)} saatine taşındı.`);
      }
    } catch (err) {
      setError(errorMessage(err, "Ders taşınamadı."));
    }
  }

  function submitManual(event: FormEvent) {
    event.preventDefault();
    if (!manualDate || !manualTime) return;
    setPicked(new Date(`${manualDate}T${manualTime}:00`));
  }

  return (
    <AssistantCard>
      <StudentHeading student={student} />
      {source.notice && !done && <WarningLine>{source.notice}</WarningLine>}
      {/* İşlemden sonra takvim tazelenir ve "sıradaki ders" yeni saat olur; satır gizlenir. */}
      {!done && <p className="text-sm">
        Taşınacak ders: <b>{dayLabel(lessonStart)} {hhmm(lessonStart)}</b> · {series.instrumentName} · {series.teacherName}
        <span className="text-meta block text-xs">Program: her {weekdayName(lessonStart)} {series.startTime.slice(0, 5)} · {series.durationMinutes} dk</span>
      </p>}

      {done ? (
        <>
          <DoneLine>{done}</DoneLine>
          <p className="text-meta text-xs">Takvimden taşımakla aynı işlem yapıldı; bildirimler de aynı şekilde gider.</p>
          <Actions><GoLink href="/dashboard/calendar">Takvimde gör</GoLink></Actions>
        </>
      ) : cancelled ? (
        <p className="text-meta text-sm">Vazgeçildi; hiçbir şey değişmedi.</p>
      ) : (
        <>
          <div className="space-y-2">
            <p className="text-sm">Bir kerelik mi, yoksa bundan sonraki her hafta mı?</p>
            <div className="flex flex-wrap gap-2">
              <Choice selected={scope === "Once"} onClick={() => { setScope("Once"); setPicked(null); }}>Yalnız bu ders</Choice>
              <Choice selected={scope === "Recurring"} onClick={() => { setScope("Recurring"); setPicked(null); }}>Bu dersten itibaren her hafta</Choice>
            </div>
          </div>

          {scope && desired && problem && <WarningLine>{dayLabel(desired)} {hhmm(desired)}: {problem}</WarningLine>}

          {scope && (!desired || problem) && (
            <div className="space-y-2">
              <p className="text-sm">{availability.isLoading ? "Boş saatlere bakıyorum…" : suggestions.length ? "Uygun saatler:" : "Önerecek boş saat bulamadım; saati kendin yazabilirsin."}</p>
              {suggestions.length > 0 && (
                <div className="flex flex-wrap gap-2">
                  {suggestions.map((slot) => (
                    <Choice key={slot.start.toISOString()} onClick={() => setPicked(slot.start)}>
                      {scope === "Recurring" ? `Her ${weekdayName(slot.start)} ${hhmm(slot.start)}` : `${dayLabel(slot.start)} ${hhmm(slot.start)}`}
                    </Choice>
                  ))}
                </div>
              )}
              <form onSubmit={submitManual} className="flex flex-wrap items-end gap-2">
                <label className="text-xs font-semibold">{scope === "Recurring" ? "İlk ders günü" : "Gün"}<input type="date" className="field mt-1 text-sm" value={manualDate} min={localDateKey(new Date())} onChange={(event) => setManualDate(event.target.value)} /></label>
                <label className="text-xs font-semibold">Saat<input type="time" step={900} className="field mt-1 text-sm" value={manualTime} onChange={(event) => setManualTime(event.target.value)} /></label>
                <button type="submit" disabled={!manualDate || !manualTime} className="btn btn-quiet text-xs">Bu saati dene</button>
              </form>
            </div>
          )}

          {scope && desired && !problem && (
            <ConfirmBox
              title={scope === "Recurring" ? "Haftalık program değişecek" : "Bu ders taşınacak"}
              rows={[
                { label: "Öğrenci", value: `${student.fullName} · ${series.instrumentName}` },
                { label: "Kapsam", value: scope === "Recurring" ? `${dayLabel(lessonStart)} dersinden itibaren her hafta` : "Yalnız bu ders" },
                {
                  label: "Saat",
                  value: scope === "Recurring"
                    ? <><s className="font-normal text-[var(--muted)]">Her {weekdayName(lessonStart)} {hhmm(lessonStart)}</s> → Her {weekdayName(desired)} {hhmm(desired)}</>
                    : <><s className="font-normal text-[var(--muted)]">{dayLabel(lessonStart)} {hhmm(lessonStart)}</s> → {dayLabel(desired)} {hhmm(desired)}</>,
                },
                { label: "Öğretmen", value: `${series.teacherName} (değişmiyor)` },
              ]}
              confirmLabel="Onayla ve taşı"
              pending={pending}
              onConfirm={() => void confirm(desired)}
              onCancel={() => setCancelled(true)}
            />
          )}
          {error && <ErrorLine>{error}</ErrorLine>}
        </>
      )}
    </AssistantCard>
  );
}

function weekdayName(date: Date) {
  return date.toLocaleDateString("tr-TR", { weekday: "long" }).toLocaleLowerCase("tr-TR");
}

// Hangi ders: cümlede gün varsa o günkü ders, yoksa sıradaki. O gün ders yoksa nedeni
// (resmi tatil) söylenir ve sonraki derse geçilir.
function findSource(lessons: CalendarLesson[], move: AssistantMove | null, holidays: Map<string, string>): Source | null {
  const sourceDate = move?.sourceDate;
  if (!sourceDate) return lessons[0] ? { lesson: lessons[0], notice: null } : null;

  const sameDay = lessons.filter((lesson) => localDateKey(new Date(lesson.startAt)) === sourceDate);
  const sourceTime = move?.sourceTime?.slice(0, 5);
  const exact = sameDay.find((lesson) => !sourceTime || hhmm(new Date(lesson.startAt)) === sourceTime) ?? sameDay[0];
  if (exact) {
    const holidayOfLesson = holidays.get(sourceDate);
    return { lesson: exact, notice: holidayOfLesson ? `${dayLabel(new Date(`${sourceDate}T00:00:00`))} resmi tatil (${holidayOfLesson}) ama bu ders planlı görünüyor.` : null };
  }

  const next = lessons.find((lesson) => localDateKey(new Date(lesson.startAt)) > sourceDate);
  if (!next) return null;
  const day = dayLabel(new Date(`${sourceDate}T00:00:00`));
  const holiday = holidays.get(sourceDate);
  return {
    lesson: next,
    notice: holiday
      ? `${day} resmi tatil (${holiday}); o gün ders yok. Sıradaki ders: ${dayLabel(new Date(next.startAt))}.`
      : `${day} günü bu kursun dersi görünmüyor. Sıradaki ders: ${dayLabel(new Date(next.startAt))}.`,
  };
}

// Cümledeki hedef ("cumaya", "17:00'ye", "yarım saat ileri") ders tarihine uygulanır. Hedef
// hiç yoksa null: akış boş saat önerir.
function desiredFromMove(move: AssistantMove | null, scope: Scope | null, lessonStart: Date): Date | null {
  if (move?.shiftMinutes) return new Date(lessonStart.getTime() + move.shiftMinutes * 60_000);
  if (!move || (!move.targetDate && !move.targetTime)) return null;
  let date = new Date(lessonStart);
  if (move.targetDate) {
    const target = new Date(`${move.targetDate}T00:00:00`);
    if (scope === "Recurring") {
      // Her hafta: hedef günün ders haftasındaki (ya da sonraki) karşılığı.
      const offset = (target.getDay() - lessonStart.getDay() + 7) % 7;
      date = addDays(lessonStart, offset);
    } else if (move.targetWeekday && !move.targetNextWeek) {
      // Bir kerelik: taşınan dersin haftasındaki (pazartesi-pazar) o gün; geçmişte kaldıysa
      // sonraki hafta.
      const mondayOffset = (lessonStart.getDay() + 6) % 7;
      const targetOffset = (DAY_KEYS.indexOf(move.targetWeekday) + 6) % 7;
      date = addDays(lessonStart, targetOffset - mondayOffset);
      const [hour, minute] = (move.targetTime ?? `${hhmm(lessonStart)}:00`).split(":").map(Number);
      if (new Date(date).setHours(hour ?? 0, minute ?? 0, 0, 0) <= Date.now()) date = addDays(date, 7);
    } else {
      date = target;
    }
  }
  const [hour, minute] = (move.targetTime ?? `${hhmm(lessonStart)}:00`).split(":").map(Number);
  date.setHours(hour ?? 0, minute ?? 0, 0, 0);
  return date;
}

function overlaps(start: Date, end: Date, lesson: CalendarLesson) {
  return start < new Date(lesson.endAt) && new Date(lesson.startAt) < end;
}

function checkTarget({ start, scope, durationMinutes, lessonStart, series, studentId, availability, lessons, holidays }: {
  start: Date;
  scope: Scope;
  durationMinutes: number;
  lessonStart: Date;
  series: StudentLessonSeries;
  studentId: string;
  availability: TeacherAvailability[];
  lessons: CalendarLesson[];
  holidays: Map<string, string>;
}): string | null {
  if (Number.isNaN(start.getTime())) return "Geçerli bir gün ve saat seç.";
  if (start.getTime() === lessonStart.getTime()) return "Ders zaten bu saatte.";
  if (start.getTime() <= Date.now()) return "Geçmiş bir saate taşınamaz.";
  const holiday = holidays.get(localDateKey(start));
  if (scope === "Once" && holiday) return `resmi tatil (${holiday}).`;

  const first = checkManualSlot({ start, durationMinutes, teacherId: series.teacherId, studentId, availability, lessons });
  if (first) return first;
  if (scope === "Once") return null;

  // Her hafta: sonraki haftalarda da öğretmen ya da öğrenci dolu olmamalı (sunucu da denetler).
  for (let week = 1; week <= RECURRING_CHECK_WEEKS; week += 1) {
    const weekStart = addDays(start, week * 7);
    const weekEnd = new Date(weekStart.getTime() + durationMinutes * 60000);
    if (lessons.some((lesson) => (lesson.teacherId === series.teacherId || lesson.studentId === studentId) && overlaps(weekStart, weekEnd, lesson)))
      return `${dayLabel(weekStart)} haftasında öğretmenin ya da öğrencinin başka dersi var.`;
  }
  return null;
}

function suggest({ scope, lessonStart, durationMinutes, series, studentId, availability, lessons, holidays }: {
  scope: Scope;
  lessonStart: Date;
  durationMinutes: number;
  series: StudentLessonSeries;
  studentId: string;
  availability: TeacherAvailability[];
  lessons: CalendarLesson[];
  holidays: Map<string, string>;
}): SuggestedSlot[] {
  const common = { durationMinutes, teacherId: series.teacherId, studentId, availability, lessons };
  if (scope === "Recurring") return findRecurringSlots({ ...common, effectiveFrom: localDateKey(lessonStart), limit: 6 });

  // Bir kerelik: dersin haftası çevresinde, tatil olmayan günler.
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const from = addDays(lessonStart, -3) > today ? addDays(lessonStart, -3) : today;
  return findOpenSlots({ ...common, from, days: 10, limit: 12 })
    .filter((slot) => !holidays.has(localDateKey(slot.start)))
    .slice(0, 6);
}

