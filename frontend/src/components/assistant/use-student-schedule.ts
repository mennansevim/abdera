"use client";

import { useMemo } from "react";
import { addDays, localDateKey, type AssistantStudent } from "@/lib/assistant";
import { useCalendar, useSchoolCalendarDays, useStudentLessonSeries, type CalendarLesson, type StudentLessonSeries } from "@/lib/scheduling";

// Ders kartı ve ders saati akışının ortak verisi: öğrencinin aktif haftalık serileri, önümüzdeki
// 5 haftadaki dersleri ve aynı aralıktaki okul tatilleri. Öğretmen oturumunda yalnızca kendi
// kurs kayıtlarının serileri kalır (sunucu courses'u zaten kapsamla döndürür).
export const SCHEDULE_DAYS = 35;

export interface StudentSchedule {
  series: StudentLessonSeries[];
  // Öğrencinin gelecekteki planlı dersleri (iptal/taşınmış hariç), tarih sırasıyla.
  upcoming: CalendarLesson[];
  // Çakışma denetimi için aralıktaki bütün dersler (öğretmenin diğer öğrencileri dahil).
  allLessons: CalendarLesson[];
  holidays: Map<string, string>;
  rangeStart: Date;
  // Kartın açıldığı an; "geçmiş ders" ve "geçmiş saat" denetimleri buna göre.
  now: Date;
  isLoading: boolean;
  isError: boolean;
}

export function useStudentSchedule(student: AssistantStudent): StudentSchedule {
  const range = useMemo(() => {
    const now = new Date();
    const start = new Date(now);
    start.setHours(0, 0, 0, 0);
    return { now, start, from: start.toISOString(), to: addDays(start, SCHEDULE_DAYS).toISOString(), fromKey: localDateKey(start), toKey: localDateKey(addDays(start, SCHEDULE_DAYS)) };
  }, []);

  const seriesQuery = useStudentLessonSeries(student.studentId);
  const calendar = useCalendar(range.from, range.to, { refetchInterval: false });
  const holidaysQuery = useSchoolCalendarDays(range.fromKey, range.toKey);

  return useMemo(() => {
    const visibleEnrollments = new Set(student.courses.map((course) => course.enrollmentId));
    const series = (seriesQuery.data ?? []).filter((item) => item.status === "Active" && visibleEnrollments.has(item.enrollmentId));
    const allLessons = (calendar.data ?? []).filter((lesson) => lesson.status !== "Cancelled" && lesson.status !== "Rescheduled");
    const upcoming = allLessons
      .filter((lesson) => lesson.studentId === student.studentId && new Date(lesson.endAt) > range.now)
      .sort((a, b) => a.startAt.localeCompare(b.startAt));
    const holidays = new Map((holidaysQuery.data ?? []).filter((day) => day.type === "Holiday").map((day) => [day.date, day.label]));
    return {
      series,
      upcoming,
      allLessons,
      holidays,
      rangeStart: range.start,
      now: range.now,
      isLoading: seriesQuery.isLoading || calendar.isLoading || holidaysQuery.isLoading,
      isError: seriesQuery.isError || calendar.isError,
    };
  }, [student, seriesQuery.data, seriesQuery.isLoading, seriesQuery.isError, calendar.data, calendar.isLoading, calendar.isError, holidaysQuery.data, holidaysQuery.isLoading, range.now, range.start]);
}

// Bir seriye ait dersler: aynı öğretmen ve enstrüman (tek seferlik taşınmış dersler seri
// kimliğini kaybedebildiği için kimlikle değil kurs eşlemesiyle).
export function lessonsOfSeries(schedule: StudentSchedule, series: StudentLessonSeries) {
  return schedule.upcoming.filter((lesson) => lesson.teacherId === series.teacherId && lesson.instrumentId === series.instrumentId);
}
