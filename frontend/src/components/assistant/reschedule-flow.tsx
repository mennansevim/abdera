"use client";

import { useState, type FormEvent } from "react";
import { addDays, dayLabel, hasPassed, hhmm, localDateKey, type AssistantMove, type AssistantStudent } from "@/lib/assistant";
import { useRescheduleLesson } from "@/lib/attendance";
import { errorMessage } from "@/lib/library";
import { useRescheduleLessonSeries, useTeacherAvailability, useUpdateLesson, type CalendarLesson, type StudentLessonSeries, type TeacherAvailability } from "@/lib/scheduling";
import { checkManualSlot, findOpenSlots, findRecurringSlots, type SuggestedSlot } from "@/lib/smart-scheduling";
import { useMe } from "@/lib/use-auth";
import { Actions, AssistantCard, Choice, ConfirmBox, DoneLine, ErrorLine, GoLink, Loading, StudentHeading, WarningLine } from "./shared";
import { lessonsOfSeries, useStudentSchedule } from "./use-student-schedule";

// "Perşembe dersini 17:00'ye al". Adımlar: hangi ders (modelin seçtiği ders görünür ve
// değiştirilebilir; tatile denk gelen gün söylenir) → bir kerelik mi her hafta mı → yeni saat
// (öğretmen müsaitliği ve çakışma denetlenir; uygun değilse boş saat önerilir) → onay →
// takvimdeki taşımayla AYNI uç (AE2): her hafta için seri taşıma, tek ders için yöneticide
// talep+onay, öğretmende ders güncellemesi. Onay kutusu yapılacak işin aynısını gösterir.
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
  const [chosenLessonId, setChosenLessonId] = useState<string | null>(null);
  const [scope, setScope] = useState<Scope | null>(move && move.scope !== "Unspecified" ? move.scope : null);
  const [picked, setPicked] = useState<Date | null>(null);
  const [startSameWeek, setStartSameWeek] = useState(false);
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

  // Başlamış ders taşınmaz; seçenekler yalnız henüz başlamamış dersler.
  const candidates = lessonsOfSeries(schedule, series).filter((lesson) => new Date(lesson.startAt) > schedule.now);
  const chosenLesson = candidates.find((lesson) => lesson.id === chosenLessonId);
  const source = chosenLesson ? { lesson: chosenLesson, notice: null } : findSource(candidates, move, schedule.holidays);
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
  const requested = picked ?? desiredFromMove(move, scope, lessonStart);
  // Her hafta: sunucu yeni programı taşınan dersin gününden başlatır; ilk ders o günden sonraki
  // ilk hedef gündür. Karttaki tarih de buna çekilir ki onaylanan ile yapılan aynı olsun.
  const firstWeekly = requested && scope === "Recurring" ? firstOnOrAfter(lessonStart, requested) : null;
  const sameWeek = firstWeekly ? sameWeekStart(lessonStart, firstWeekly) : null;
  const desired = scope === "Recurring" ? (startSameWeek && sameWeek ? sameWeek : firstWeekly) : requested;
  const effectiveFrom = desired && scope === "Recurring" && desired < lessonStart ? desired : lessonStart;
  const skipsWeek = scope === "Recurring" && desired !== null && !startSameWeek && weekKey(desired) !== weekKey(lessonStart);

  const otherLessons = scope === "Recurring"
    ? schedule.allLessons.filter((lesson) => !(lesson.studentId === student.studentId && lesson.teacherId === series.teacherId && lesson.instrumentId === series.instrumentId && new Date(lesson.startAt) >= startOfDay(effectiveFrom)))
    : schedule.allLessons.filter((lesson) => lesson.id !== source.lesson.id);
  // Müsaitlik gelmeden denetim yapılmaz: boş liste "her gün açık" sayıldığı için erken bir onay
  // müsait olmayan saate ders koyabilirdi (tek ders ve telafide sunucu müsaitliği denetlemiyor).
  const ready = availability.isSuccess;
  const problem = desired && scope && ready
    ? checkTarget({ start: desired, scope, durationMinutes, lessonStart, series, studentId: student.studentId, availability: availability.data, lessons: otherLessons, holidays: schedule.holidays })
    : null;
  const suggestions = scope && (!desired || problem) && ready
    ? suggest({ scope, lessonStart, durationMinutes, series, studentId: student.studentId, availability: availability.data, lessons: otherLessons, holidays: schedule.holidays })
    : [];

  async function confirm(start: Date) {
    if (!series || !scope || !source) return;
    setError(null);
    // Kart uzun süre açık kaldıysa seçilen saat ya da taşınacak ders geçmiş olabilir.
    if (hasPassed(start) || hasPassed(lessonStart)) {
      setError("Bu saat ya da taşınacak ders geçti; kartı yeniden açıp başka saat seç.");
      return;
    }
    const end = new Date(start.getTime() + durationMinutes * 60000);
    try {
      if (scope === "Recurring") {
        await rescheduleSeries.mutateAsync({
          seriesId: series.id,
          dayOfWeek: DAY_KEYS[start.getDay()] ?? "Monday",
          startTime: `${hhmm(start)}:00`,
          durationMinutes: series.durationMinutes,
          effectiveFrom: localDateKey(effectiveFrom),
        });
        setDone(`${student.fullName} için ${series.instrumentName} programı her ${weekdayName(start)} ${hhmm(start)} oldu. İlk ders ${dayLabel(start)}.`);
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
    setStartSameWeek(false);
    setPicked(new Date(`${manualDate}T${manualTime}:00`));
  }

  function chooseScope(next: Scope) {
    setScope(next);
    setPicked(null);
    setStartSameWeek(false);
  }

  const options = candidates.slice(0, 8);
  if (!options.includes(source.lesson)) options.push(source.lesson);
  const checks = [
    "Öğretmenin müsait olduğu saat",
    "Öğretmenin ve öğrencinin o saatte başka dersi yok",
    ...(scope === "Recurring" ? [`Sonraki ${RECURRING_CHECK_WEEKS} haftada da çakışma yok`] : ["Resmi tatil değil"]),
  ];

  return (
    <AssistantCard>
      <StudentHeading student={student} />
      {source.notice && !done && <WarningLine>{source.notice}</WarningLine>}
      {/* İşlemden sonra takvim tazelenir ve "sıradaki ders" yeni saat olur; seçim gizlenir. */}
      {!done && !cancelled && (
        <div className="space-y-1">
          <label className="block text-xs font-semibold" htmlFor={`source-${source.lesson.id}`}>Taşınacak ders</label>
          <select
            id={`source-${source.lesson.id}`}
            className="field text-sm"
            value={source.lesson.id}
            onChange={(event) => { setChosenLessonId(event.target.value); setPicked(null); setStartSameWeek(false); }}
          >
            {options.map((lesson) => {
              const start = new Date(lesson.startAt);
              const holiday = schedule.holidays.get(localDateKey(start));
              return <option key={lesson.id} value={lesson.id}>{dayLabel(start)} {hhmm(start)}{holiday ? ` · tatil (${holiday})` : ""}</option>;
            })}
          </select>
          <p className="text-meta text-xs">{series.instrumentName} · {series.teacherName} · program: her {weekdayName(lessonStart)} {series.startTime.slice(0, 5)}, {series.durationMinutes} dk</p>
        </div>
      )}

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
              <Choice selected={scope === "Once"} onClick={() => chooseScope("Once")}>Yalnız bu ders</Choice>
              <Choice selected={scope === "Recurring"} onClick={() => chooseScope("Recurring")}>Bu dersten itibaren her hafta</Choice>
            </div>
          </div>

          {scope && desired && !ready && (availability.isError
            ? <ErrorLine>Öğretmenin müsaitliği getirilemedi; taşımayı takvimden yap.</ErrorLine>
            : <Loading>Öğretmenin müsaitliğine bakıyorum…</Loading>)}

          {scope && desired && problem && <WarningLine>{dayLabel(desired)} {hhmm(desired)}: {problem}</WarningLine>}

          {scope && (!desired || problem) && ready && (
            <div className="space-y-2">
              <p className="text-sm">{suggestions.length ? "Uygun saatler:" : "Önerecek boş saat bulamadım; saati kendin yazabilirsin."}</p>
              {suggestions.length > 0 && (
                <div className="flex flex-wrap gap-2">
                  {suggestions.map((slot) => (
                    <Choice key={slot.start.toISOString()} onClick={() => { setStartSameWeek(false); setPicked(slot.start); }}>
                      {scope === "Recurring" ? `Her ${weekdayName(slot.start)} ${hhmm(slot.start)}` : `${dayLabel(slot.start)} ${hhmm(slot.start)}`}
                    </Choice>
                  ))}
                </div>
              )}
              <form onSubmit={submitManual} className="flex flex-wrap items-end gap-2">
                <label className="text-xs font-semibold">{scope === "Recurring" ? "Yeni gün (herhangi bir hafta)" : "Gün"}<input type="date" className="field mt-1 text-sm" value={manualDate} min={localDateKey(new Date())} onChange={(event) => setManualDate(event.target.value)} /></label>
                <label className="text-xs font-semibold">Saat<input type="time" step={900} className="field mt-1 text-sm" value={manualTime} onChange={(event) => setManualTime(event.target.value)} /></label>
                <button type="submit" disabled={!manualDate || !manualTime} className="btn btn-quiet text-xs">Bu saati dene</button>
              </form>
            </div>
          )}

          {scope && desired && ready && !problem && (
            <ConfirmBox
              title={scope === "Recurring" ? "Haftalık program değişecek" : "Bu ders taşınacak"}
              rows={[
                { label: "Öğrenci", value: `${student.fullName} · ${series.instrumentName}` },
                { label: "Kapsam", value: scope === "Recurring" ? "Bundan sonraki her hafta" : "Yalnız bu ders" },
                {
                  label: scope === "Recurring" ? "Program" : "Saat",
                  value: scope === "Recurring"
                    ? <><s className="font-normal text-[var(--muted)]">Her {weekdayName(lessonStart)} {hhmm(lessonStart)}</s> → Her {weekdayName(desired)} {hhmm(desired)}</>
                    : <><s className="font-normal text-[var(--muted)]">{dayLabel(lessonStart)} {hhmm(lessonStart)}</s> → {dayLabel(desired)} {hhmm(desired)}</>,
                },
                ...(scope === "Recurring" ? [{ label: "İlk ders", value: `${dayLabel(desired)} ${hhmm(desired)}` }] : []),
                { label: "Süre", value: `${durationMinutes} dk · ${series.teacherName} (değişmiyor)` },
              ]}
              checks={checks}
              warning={skipsWeek ? (
                <span className="block space-y-2">
                  <span className="block">{dayLabel(lessonStart)} dersi kalkıyor ve yeni program {dayLabel(desired)} başlıyor: o hafta ders olmayacak.</span>
                  {sameWeek && <button type="button" onClick={() => setStartSameWeek(true)} className="btn btn-quiet text-xs">Bu hafta {dayLabel(sameWeek)} başlasın</button>}
                </span>
              ) : undefined}
              confirmLabel={scope === "Recurring" ? "Onayla, programı değiştir" : "Onayla ve taşı"}
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

function startOfDay(date: Date) {
  const day = new Date(date);
  day.setHours(0, 0, 0, 0);
  return day;
}

// Pazartesi başlangıçlı haftanın anahtarı.
function weekKey(date: Date) {
  return localDateKey(addDays(startOfDay(date), -((date.getDay() + 6) % 7)));
}

// Taşınan dersin gününden itibaren hedefin haftanın günü ve saatine denk gelen ilk an.
function firstOnOrAfter(lessonStart: Date, target: Date) {
  const date = addDays(startOfDay(lessonStart), (target.getDay() - lessonStart.getDay() + 7) % 7);
  date.setHours(target.getHours(), target.getMinutes(), 0, 0);
  return date;
}

// Yeni gün taşınan dersin haftasında daha önceyse ve henüz geçmediyse, programın bu hafta
// başlaması seçeneği. Yoksa null.
function sameWeekStart(lessonStart: Date, firstWeekly: Date) {
  if (weekKey(firstWeekly) === weekKey(lessonStart)) return null;
  const candidate = addDays(firstWeekly, -7);
  return candidate.getTime() > Date.now() && weekKey(candidate) === weekKey(lessonStart) ? candidate : null;
}

function weekdayName(date: Date) {
  return date.toLocaleDateString("tr-TR", { weekday: "long" }).toLocaleLowerCase("tr-TR");
}

// Hangi ders: cümlede gün varsa o günkü ders, yoksa sıradaki. O gün ders yoksa nedeni
// (resmi tatil) söylenir ve sonraki derse geçilir.
function findSource(lessons: CalendarLesson[], move: AssistantMove | null, holidays: Map<string, string>): Source | null {
  const sourceDate = move?.sourceDate;
  // Ders söylenmediyse sıradaki ders alınır ve bu açıkça yazılır; kullanıcı aşağıdan değiştirebilir.
  if (!sourceDate) return lessons[0] ? { lesson: lessons[0], notice: lessons.length > 1 ? `Hangi ders olduğu söylenmedi; sıradaki dersi (${dayLabel(new Date(lessons[0].startAt))}) seçtim. Başka bir dersse aşağıdan değiştir.` : null } : null;

  const sameDay = lessons.filter((lesson) => localDateKey(new Date(lesson.startAt)) === sourceDate);
  const sourceTime = move?.sourceTime?.slice(0, 5);
  const timed = sameDay.find((lesson) => !sourceTime || hhmm(new Date(lesson.startAt)) === sourceTime);
  const exact = timed ?? sameDay[0];
  if (exact) {
    const holidayOfLesson = holidays.get(sourceDate);
    const day = dayLabel(new Date(`${sourceDate}T00:00:00`));
    // Söylenen saatte ders yoksa o günün dersi seçilir ama bu açıkça söylenir.
    const timeNotice = timed ? null : `${day} ${sourceTime} dersi yok; o günkü ${hhmm(new Date(exact.startAt))} dersini seçtim.`;
    const holidayNotice = holidayOfLesson ? `${day} resmi tatil (${holidayOfLesson}) ama bu ders planlı görünüyor.` : null;
    return { lesson: exact, notice: [timeNotice, holidayNotice].filter(Boolean).join(" ") || null };
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

