"use client";

import Link from "next/link";
import { useState } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useModuleEnabled } from "@/lib/instance";
import { ATTENDANCE_LABELS, useMarkAttendance, type AttendanceStatus } from "@/lib/attendance";
import { usePendingLessonNotes } from "@/lib/progress";
import { useMyTeacherWeek, type MyTeacherWeek, type MyWeekLesson } from "@/lib/teacher-payouts";

// Öğretmenin kendi ödeme haftası (issue #16, docs/10-decisions.md Y): Pzt–Cmt tamamlanan ders
// sayısı, yoklaması eksik dersler ve Ödendi/Ödenmedi. Tutar bilinçli olarak gösterilmez
// (kullanıcı kararı). Kart açılınca haftanın başlamış dersleri yoklama durumlarıyla listelenir;
// öğretmen yanlış ya da eksik yoklamayı buradan düzeltir (kullanıcı isteği: "geldi gelmedi
// durumunu gösterirken aslında gelmedi ya da mazeretli yapılabilsin").
export function TeacherWeekCard() {
  // Ödendi/Ödenmedi satırı gelir-gider modülüne bağlı; ders sayıları her okulda görünür.
  const financeEnabled = useModuleEnabled("finance");
  const [weekStart, setWeekStart] = useState<string | null>(null);
  const { data: week, isLoading, isError } = useMyTeacherWeek(weekStart);
  const { data: pendingNotes } = usePendingLessonNotes();
  const [open, setOpen] = useState(false);
  const pendingNoteCount = pendingNotes?.items.length ?? 0;

  if (isLoading) return <div className="skeleton mb-3 h-24 rounded-2xl" aria-label="Haftalık özet yükleniyor" />;
  if (isError || !week) return null;

  const missing = week.lessons.filter((lesson) => lesson.status !== "Completed");
  const listed = open ? week.lessons : missing;

  return (
    <section aria-labelledby="haftalik-ozet-baslik" className="app-card mb-3 overflow-clip">
      <header className="flex items-center gap-2 border-b border-[var(--line)] px-3 py-2.5">
        <button type="button" onClick={() => setWeekStart(shiftWeek(week.weekStart, -7))} aria-label="Önceki hafta" className="icon-btn icon-btn-quiet shrink-0"><Icon name="chevron" className="h-4 w-4 rotate-180" /></button>
        <div className="min-w-0 flex-1 text-center">
          <h2 id="haftalik-ozet-baslik" className="text-sm font-bold">{week.isCurrentWeek ? "Bu hafta" : financeEnabled ? "Ödeme haftası" : "Hafta"} · {rangeLabel(week)}</h2>
          {financeEnabled && <p className="text-meta mt-0.5">{payLabel(week)}</p>}
        </div>
        <button type="button" onClick={() => setWeekStart(shiftWeek(week.weekStart, 7))} disabled={week.isCurrentWeek} aria-label="Sonraki hafta" className="icon-btn icon-btn-quiet shrink-0 disabled:opacity-30"><Icon name="chevron" className="h-4 w-4" /></button>
      </header>

      <div className="grid grid-cols-2 gap-2 p-3">
        <div className="rounded-xl bg-[var(--surface-muted)] px-3 py-2">
          <p className="text-micro text-[var(--muted)]">Tamamlanan ders</p>
          <p className="text-title mt-0.5 tabular-nums">{week.completedLessons}</p>
        </div>
        <div className={`rounded-xl px-3 py-2 ${week.missingAttendanceLessons ? "bg-[var(--warning-soft)]" : "bg-[var(--surface-muted)]"}`}>
          <p className="text-micro text-[var(--muted)]">Yoklaması eksik</p>
          <p className={`text-title mt-0.5 tabular-nums ${week.missingAttendanceLessons ? "text-[var(--warning-strong)]" : ""}`}>{week.missingAttendanceLessons}</p>
        </div>
      </div>

      {pendingNoteCount > 0 && (
        <Link href="/dashboard/progress#yorum-bekleyen-dersler" className="pressable mx-3 mb-3 flex items-center gap-2 rounded-xl border border-[var(--line)] bg-white px-3 py-2 text-xs font-bold hover:bg-[var(--surface-muted)]">
          <Icon name="note" className="h-4 w-4 text-[var(--warning-strong)]" />
          <span className="flex-1">{pendingNoteCount} dersin yorumu bekliyor</span>
          <Icon name="chevron" className="h-4 w-4 text-[var(--muted)]" />
        </Link>
      )}

      {listed.length > 0 && (
        <ul className="divide-y divide-[var(--line)] border-t border-[var(--line)]">
          {listed.map((lesson) => <WeekLessonRow key={lesson.lessonId} lesson={lesson} paid={week.isPaid} />)}
        </ul>
      )}
      {week.isPaid && open && week.lessons.length > 0 && (
        <p className="text-meta border-t border-[var(--line)] px-3 py-2">Bu haftanın ödemesi yapıldı; buradaki düzeltmeler yapılmış ödemeyi değiştirmez, gerekirse yöneticiye haber ver.</p>
      )}
      {week.lessons.length > missing.length && (
        <button type="button" onClick={() => setOpen((value) => !value)} aria-expanded={open} className="pressable flex w-full items-center justify-center gap-1.5 border-t border-[var(--line)] px-3 py-2.5 text-xs font-bold text-[var(--brand-strong)] hover:bg-[var(--surface-muted)]">
          {open ? "Yalnız eksikleri göster" : `Haftanın derslerini göster ve düzelt (${week.lessons.length})`}
          <Icon name="chevron" className={`h-3.5 w-3.5 ${open ? "-rotate-90" : "rotate-90"}`} />
        </button>
      )}
    </section>
  );
}

// Tek dokunuş kaydeder: yoklaması eksik ders işaretlenir, girilmiş olan düzeltilir. Sunucu
// düzeltmeyi audit'e yazar ve Mazeretli'ye giriş/çıkışta telafi hakkını açar/geri alır.
function WeekLessonRow({ lesson, paid }: { lesson: MyWeekLesson; paid: boolean }) {
  const markAttendance = useMarkAttendance(lesson.lessonId);
  const [error, setError] = useState<string | null>(null);
  const start = new Date(lesson.startAt);
  const missing = lesson.attendanceStatus === null;

  function choose(status: AttendanceStatus) {
    if (status === lesson.attendanceStatus) return;
    setError(null);
    markAttendance.mutate({ status }, {
      onError: (err) => setError(err instanceof ApiError ? (err.detail ?? err.title) : "Yoklama kaydedilemedi."),
    });
  }

  return (
    <li className={`px-3 py-2.5 ${missing ? "bg-[var(--warning-soft)]/40" : ""}`}>
      <div className="flex items-baseline justify-between gap-2">
        <p className="min-w-0 truncate text-xs font-bold">{lesson.studentName} <span className="font-medium text-[var(--muted)]">· {lesson.instrumentName}</span></p>
        <p className="text-meta shrink-0">{start.toLocaleDateString("tr-TR", { weekday: "short", day: "numeric", month: "short" })} {start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}</p>
      </div>
      <div className="mt-2 grid grid-cols-3 gap-1.5" role="group" aria-label={`${lesson.studentName} yoklaması`}>
        {(["Present", "Absent", "Excused"] as const).map((item) => {
          const active = lesson.attendanceStatus === item;
          const pending = markAttendance.isPending && markAttendance.variables?.status === item;
          return (
            <button key={item} type="button" aria-pressed={active} disabled={markAttendance.isPending} onClick={() => choose(item)}
              className={`pressable min-h-10 rounded-lg border px-2 text-[.75rem] font-bold disabled:opacity-60 ${active ? item === "Present" ? "border-[color:var(--success)] bg-[var(--success-soft)] text-[var(--success-strong)]" : item === "Absent" ? "border-[color:var(--danger)] bg-[var(--danger-soft)] text-[var(--danger-strong)]" : "border-[color:var(--warning)] bg-[var(--warning-soft)] text-[var(--warning-strong)]" : "border-[var(--line)] bg-white text-[var(--muted)]"}`}>
              {pending ? "Kaydediliyor…" : ATTENDANCE_LABELS[item]}
            </button>
          );
        })}
      </div>
      {missing && !paid && <p className="text-meta mt-1.5">Yoklama girilmedi; girilmeden bu ders ödemeye sayılmaz.</p>}
      {error && <p role="alert" className="mt-1.5 text-[.75rem] font-semibold text-[var(--danger-strong)]">{error}</p>}
    </li>
  );
}

function parseDay(value: string) {
  return new Date(`${value}T00:00:00`);
}

function shiftWeek(weekStart: string, days: number) {
  const date = parseDay(weekStart);
  date.setDate(date.getDate() + days);
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function rangeLabel(week: MyTeacherWeek) {
  const start = parseDay(week.weekStart);
  const end = parseDay(week.weekEnd);
  const sameMonth = start.getMonth() === end.getMonth();
  return sameMonth
    ? `${start.getDate()}–${end.toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}`
    : `${start.toLocaleDateString("tr-TR", { day: "numeric", month: "short" })} – ${end.toLocaleDateString("tr-TR", { day: "numeric", month: "short" })}`;
}

function payLabel(week: MyTeacherWeek) {
  if (week.isPaid && week.paidOn) return `Ödendi · ${parseDay(week.paidOn).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}`;
  return `Ödenmedi · ödeme günü ${parseDay(week.payDay).toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "long" })}`;
}
