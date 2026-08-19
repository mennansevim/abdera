"use client";

import { useMemo, useState } from "react";
import { useMe } from "@/lib/use-auth";
import { DAY_NAMES_TR, useCalendar, type CalendarLesson } from "@/lib/scheduling";
import { CreateSeriesForm } from "./create-series-form";

// Haftanın Pazartesi'sini bulur - takvim her zaman Pazartesi'den başlar.
function startOfWeek(date: Date): Date {
  const d = new Date(date);
  const day = d.getDay();
  const diff = (day === 0 ? -6 : 1) - day;
  d.setDate(d.getDate() + diff);
  d.setHours(0, 0, 0, 0);
  return d;
}

function formatDateOnly(date: Date): string {
  return date.toISOString().slice(0, 10);
}

const WEEKDAY_ORDER = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

export default function CalendarPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const [weekStart, setWeekStart] = useState(() => startOfWeek(new Date()));

  const weekEnd = useMemo(() => {
    const d = new Date(weekStart);
    d.setDate(d.getDate() + 7);
    return d;
  }, [weekStart]);

  const { data: lessons, isLoading } = useCalendar(weekStart.toISOString(), weekEnd.toISOString());

  const lessonsByDay = useMemo(() => {
    const map = new Map<string, CalendarLesson[]>();
    for (const lesson of lessons ?? []) {
      const localDate = new Date(lesson.startAt);
      const key = WEEKDAY_ORDER[(localDate.getDay() + 6) % 7];
      map.set(key, [...(map.get(key) ?? []), lesson]);
    }
    for (const day of map.values()) {
      day.sort((a, b) => a.startAt.localeCompare(b.startAt));
    }
    return map;
  }, [lessons]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Takvim</h1>
        <div className="flex items-center gap-2 text-sm">
          <button
            onClick={() => setWeekStart((d) => { const n = new Date(d); n.setDate(n.getDate() - 7); return n; })}
            className="rounded-md border border-neutral-300 px-2 py-1 hover:bg-neutral-100"
          >
            ← Önceki hafta
          </button>
          <span className="text-neutral-500">
            {formatDateOnly(weekStart)} – {formatDateOnly(new Date(weekEnd.getTime() - 86400000))}
          </span>
          <button
            onClick={() => setWeekStart((d) => { const n = new Date(d); n.setDate(n.getDate() + 7); return n; })}
            className="rounded-md border border-neutral-300 px-2 py-1 hover:bg-neutral-100"
          >
            Sonraki hafta →
          </button>
        </div>
      </div>

      {isAdmin && <CreateSeriesForm />}

      {isLoading && <p className="text-sm text-neutral-500">Yükleniyor…</p>}

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {WEEKDAY_ORDER.map((day) => (
          <div key={day} className="rounded-lg border border-neutral-200 bg-white p-3">
            <h2 className="mb-2 text-sm font-semibold text-neutral-700">{DAY_NAMES_TR[day]}</h2>
            <ul className="space-y-2">
              {(lessonsByDay.get(day) ?? []).map((lesson) => (
                <li key={lesson.id} className="rounded border border-neutral-200 p-2 text-xs">
                  <div className="font-medium">
                    {new Date(lesson.startAt).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}
                  </div>
                  <div>{lesson.studentName}</div>
                  <div className="text-neutral-500">{lesson.instrumentName} · {lesson.teacherName}</div>
                </li>
              ))}
              {(lessonsByDay.get(day) ?? []).length === 0 && (
                <li className="text-xs text-neutral-400">Ders yok</li>
              )}
            </ul>
          </div>
        ))}
      </div>
    </div>
  );
}
