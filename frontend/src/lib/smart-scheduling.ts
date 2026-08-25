import type { CalendarLesson, TeacherAvailability } from "./scheduling";

export interface SuggestedSlot {
  start: Date;
  end: Date;
  score: number;
  reason: string;
}

const DAY_INDEX: Record<string, number> = { Sunday: 0, Monday: 1, Tuesday: 2, Wednesday: 3, Thursday: 4, Friday: 5, Saturday: 6 };
const MAXIMUM_STUDENT_LESSONS_PER_WEEK = 4;

function timeParts(value: string) {
  const [hour = "0", minute = "0"] = value.split(":");
  return { hour: Number(hour), minute: Number(minute) };
}

function overlaps(start: Date, end: Date, lesson: CalendarLesson) {
  return start < new Date(lesson.endAt) && new Date(lesson.startAt) < end && lesson.status !== "Cancelled" && lesson.status !== "Rescheduled";
}

function studentHasWeeklyCapacity(start: Date, studentId: string, lessons: CalendarLesson[]) {
  const weekStart = new Date(start);
  const daysSinceMonday = (weekStart.getDay() + 6) % 7;
  weekStart.setDate(weekStart.getDate() - daysSinceMonday);
  weekStart.setHours(0, 0, 0, 0);
  const weekEnd = new Date(weekStart);
  weekEnd.setDate(weekEnd.getDate() + 7);

  const lessonCount = lessons.filter((lesson) => {
    if (lesson.studentId !== studentId || lesson.status === "Cancelled" || lesson.status === "Rescheduled") return false;
    const lessonStart = new Date(lesson.startAt);
    return lessonStart >= weekStart && lessonStart < weekEnd;
  }).length;

  return lessonCount < MAXIMUM_STUDENT_LESSONS_PER_WEEK;
}

function windowsForDay(day: Date, availability: TeacherAvailability[]) {
  const windows = availability.filter((item) => DAY_INDEX[item.dayOfWeek] === day.getDay());
  return windows.length ? windows : [{ id: "default", dayOfWeek: "", startTime: "10:00", endTime: "21:00" }];
}

export function findOpenSlots({
  from,
  days,
  durationMinutes,
  teacherId,
  studentId,
  availability,
  lessons,
  limit = 8,
}: {
  from: Date;
  days: number;
  durationMinutes: number;
  teacherId: string;
  studentId: string;
  availability: TeacherAvailability[];
  lessons: CalendarLesson[];
  limit?: number;
}) {
  const results: SuggestedSlot[] = [];
  const now = new Date();
  for (let offset = 0; offset < days; offset += 1) {
    const day = new Date(from);
    day.setHours(0, 0, 0, 0);
    day.setDate(day.getDate() + offset);
    for (const window of windowsForDay(day, availability)) {
      const startParts = timeParts(window.startTime);
      const endParts = timeParts(window.endTime);
      const cursor = new Date(day);
      cursor.setHours(startParts.hour, startParts.minute, 0, 0);
      const windowEnd = new Date(day);
      windowEnd.setHours(endParts.hour, endParts.minute, 0, 0);
      while (cursor.getTime() + durationMinutes * 60000 <= windowEnd.getTime()) {
        const start = new Date(cursor);
        const end = new Date(start.getTime() + durationMinutes * 60000);
        const busy = lessons.some((lesson) => (lesson.teacherId === teacherId || lesson.studentId === studentId) && overlaps(start, end, lesson));
        if (start > now && !busy && studentHasWeeklyCapacity(start, studentId, lessons)) {
          const preferredHour = start.getHours() >= 16 && start.getHours() <= 19;
          const soonness = Math.max(0, 30 - offset);
          results.push({ start, end, score: soonness + (preferredHour ? 12 : 0), reason: preferredHour ? "Tercih edilen saat" : "Öğretmen müsait" });
        }
        cursor.setMinutes(cursor.getMinutes() + 15);
      }
    }
  }
  return results.sort((a, b) => b.score - a.score || a.start.getTime() - b.start.getTime()).slice(0, limit);
}

export function findRecurringSlots({ effectiveFrom, durationMinutes, teacherId, studentId, availability, lessons, limit = 6 }: { effectiveFrom: string; durationMinutes: number; teacherId: string; studentId: string; availability: TeacherAvailability[]; lessons: CalendarLesson[]; limit?: number }) {
  const oneOff = findOpenSlots({ from: new Date(`${effectiveFrom}T00:00:00`), days: 14, durationMinutes, teacherId, studentId, availability, lessons, limit: 80 });
  const candidates = oneOff.filter((candidate) => {
    for (let week = 1; week < 8; week += 1) {
      const start = new Date(candidate.start);
      start.setDate(start.getDate() + week * 7);
      const end = new Date(start.getTime() + durationMinutes * 60000);
      if (!studentHasWeeklyCapacity(start, studentId, lessons)) return false;
      if (lessons.some((lesson) => (lesson.teacherId === teacherId || lesson.studentId === studentId) && overlaps(start, end, lesson))) return false;
    }
    return true;
  });
  const unique = new Map<string, SuggestedSlot>();
  for (const slot of candidates) {
    const key = `${slot.start.getDay()}-${slot.start.getHours()}-${slot.start.getMinutes()}`;
    if (!unique.has(key)) unique.set(key, { ...slot, reason: "8 hafta boyunca uygun" });
  }
  return Array.from(unique.values()).slice(0, limit);
}
