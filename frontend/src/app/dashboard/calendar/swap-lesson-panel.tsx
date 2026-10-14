"use client";

import { useMemo, useState } from "react";
import { ApiError } from "@/lib/api";
import { useCalendar, useSwapLessons, type CalendarLesson, type LessonSwapScope } from "@/lib/scheduling";

// Ders detayındaki "Yer değiştir": aynı öğretmenin aynı haftadaki başka bir birebir dersiyle
// saatleri takas eder (kullanıcı isteği: "Perşembe 15 Ferit'i Cuma 15'e, Cuma 15 Emir Ali'yi
// Perşembe 15'e"). Dersleri tek tek taşımak karşı ders hâlâ orada durduğu için çakışmaya
// takılıyordu. Doğrulama ve yazma sunucuda (SwapLessons.cs); burası yalnızca seçim ekranı.

function startOfWeek(date: Date) {
  const d = new Date(date);
  const day = d.getDay();
  d.setDate(d.getDate() + ((day === 0 ? -6 : 1) - day));
  d.setHours(0, 0, 0, 0);
  return d;
}

function slotLabel(startAt: string) {
  const start = new Date(startAt);
  return `${start.toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "short" })} ${start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}`;
}

function weeklyLabel(startAt: string) {
  const start = new Date(startAt);
  return `her ${start.toLocaleDateString("tr-TR", { weekday: "long" })} ${start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}`;
}

// Takasa girebilen ders: planlı (telafi değil), grup üyesi değil. Öğretmen başlamış dersi
// takas edemez - sunucu da aynı kuralı uygular.
function isSwappable(lesson: CalendarLesson, isAdmin: boolean, now: Date) {
  return lesson.status === "Normal" && !lesson.lessonGroupId && (isAdmin || new Date(lesson.startAt).getTime() > now.getTime());
}

export function canSwapLesson(lesson: CalendarLesson, canManage: boolean, isAdmin: boolean, now: Date) {
  return canManage && isSwappable(lesson, isAdmin, now);
}

export function SwapLessonPanel({ lesson, isAdmin, now, onSwapped, onCancel }: {
  lesson: CalendarLesson;
  isAdmin: boolean;
  now: Date;
  onSwapped: (message: string) => void;
  onCancel: () => void;
}) {
  const weekStart = useMemo(() => startOfWeek(new Date(lesson.startAt)), [lesson.startAt]);
  const weekEnd = useMemo(() => { const end = new Date(weekStart); end.setDate(end.getDate() + 7); return end; }, [weekStart]);
  const { data: weekLessons, isLoading } = useCalendar(weekStart.toISOString(), weekEnd.toISOString(), { refetchInterval: false });
  const swap = useSwapLessons();
  const [otherId, setOtherId] = useState("");
  const [error, setError] = useState<string | null>(null);

  const candidates = useMemo(() => (weekLessons ?? [])
    .filter((item) => item.id !== lesson.id && item.teacherId === lesson.teacherId && item.studentId !== lesson.studentId)
    .filter((item) => isSwappable(item, isAdmin, now))
    .sort((a, b) => a.startAt.localeCompare(b.startAt)), [weekLessons, lesson, isAdmin, now]);
  const other = candidates.find((item) => item.id === otherId) ?? null;
  const bothRecurring = Boolean(lesson.lessonSeriesId && other?.lessonSeriesId);
  // Karşı ders seçilmeden de görünür (seçince ne olacağı belli olsun), ama yalnızca iki ders
  // de haftalık programa bağlıysa basılabilir.
  const offersRecurring = Boolean(lesson.lessonSeriesId) && (!other || bothRecurring);

  async function handleSwap(scope: LessonSwapScope) {
    if (!other) return;
    setError(null);
    try {
      await swap.mutateAsync({ lessonId: lesson.id, otherLessonId: other.id, scope });
      onSwapped(scope === "Recurring"
        ? `${lesson.studentName} ile ${other.studentName} haftalık programda yer değiştirdi.`
        : `${lesson.studentName} ile ${other.studentName} bu hafta yer değiştirdi.`);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Dersler yer değiştiremedi.");
    }
  }

  return (
    <div className="border-t border-[var(--line)] bg-[var(--surface-muted)] p-4">
      <p className="text-sm font-bold">Yer değiştir</p>
      <p className="text-meta mt-1">{lesson.studentName}, aynı öğretmenin bu haftaki başka bir dersiyle saatini takas eder. Her öğrenci kendi ders süresini korur.</p>
      <label className="mt-3 block text-micro text-[var(--muted)]">
        Karşı ders
        <select value={otherId} onChange={(event) => { setOtherId(event.target.value); setError(null); }} disabled={isLoading || swap.isPending} className="field mt-1 min-h-11 bg-white text-sm font-semibold">
          <option value="">{isLoading ? "Dersler yükleniyor…" : candidates.length ? "Ders seç" : "Bu hafta takas edilebilecek ders yok"}</option>
          {candidates.map((item) => <option key={item.id} value={item.id}>{slotLabel(item.startAt)} · {item.studentName}</option>)}
        </select>
      </label>
      {other && (
        <ul className="mt-3 grid gap-1.5 rounded-xl border border-[var(--line)] bg-white px-3 py-2.5 text-sm">
          <li><span className="font-bold">{lesson.studentName}</span>: {slotLabel(lesson.startAt)} → {slotLabel(other.startAt)}</li>
          <li><span className="font-bold">{other.studentName}</span>: {slotLabel(other.startAt)} → {slotLabel(lesson.startAt)}</li>
        </ul>
      )}
      {other && !bothRecurring && <p className="text-meta mt-2">Derslerden biri haftalık programa bağlı değil; yalnızca bu hafta takas edilebilir.</p>}
      {other && bothRecurring && <p className="text-meta mt-2">&quot;Bundan sonra hep&quot; seçilirse {lesson.studentName} {weeklyLabel(other.startAt)}, {other.studentName} {weeklyLabel(lesson.startAt)} olur; önceki haftalar olduğu gibi kalır.</p>}
      {error && <p role="alert" className="mt-3 rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
      <div className="mt-3 flex flex-wrap justify-end gap-2" aria-busy={swap.isPending}>
        <button type="button" onClick={onCancel} disabled={swap.isPending} className="btn btn-quiet">Vazgeç</button>
        {offersRecurring && <button type="button" onClick={() => void handleSwap("Recurring")} disabled={!bothRecurring || swap.isPending} className="btn border border-[var(--line)] bg-white text-[var(--brand-strong)] disabled:opacity-60">{swap.isPending && swap.variables?.scope === "Recurring" ? "Kaydediliyor…" : "Bundan sonra hep"}</button>}
        <button type="button" onClick={() => void handleSwap("Once")} disabled={!other || swap.isPending} className="btn btn-primary disabled:opacity-60">{swap.isPending && swap.variables?.scope === "Once" ? "Kaydediliyor…" : "Yalnız bu hafta"}</button>
      </div>
    </div>
  );
}
