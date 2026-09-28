"use client";

// Pazartesi-Cumartesi hafta seçici - öğretmen listesi ve benchmark ekranı ortak kullanır
// (backend Benchmark.cs haftayı Pazartesi'ye normalize eder). Öğretmen ÖDEME haftası
// (pazar → cumartesi, TeacherPayWeek) bu değildir; onun seçicisi
// Giderler > Gider ekle > Haftalık sekmesinde (teacher-payout-form.tsx).
import { Icon } from "@/components/icons";

export function mondayOf(date: Date): Date {
  const d = new Date(date);
  d.setDate(d.getDate() + (d.getDay() === 0 ? -6 : 1 - d.getDay()));
  d.setHours(0, 0, 0, 0);
  return d;
}

export function addDays(date: Date, days: number): Date {
  const d = new Date(date);
  d.setDate(d.getDate() + days);
  return d;
}

export function toIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function WeekPicker({ monday, onChange }: { monday: Date; onChange: (monday: Date) => void }) {
  const isCurrentWeek = monday.getTime() === mondayOf(new Date()).getTime();
  const fmt = (d: Date) => d.toLocaleDateString("tr-TR", { day: "numeric", month: "short" });
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <div className="flex items-center gap-1.5 rounded-[.9rem] bg-[var(--surface-muted)] p-1" aria-label="Hafta değiştir">
        <button type="button" onClick={() => onChange(addDays(monday, -7))} className="icon-btn icon-btn-quiet" aria-label="Önceki hafta"><Icon name="arrow-left" className="h-4 w-4" /></button>
        <button type="button" onClick={() => onChange(addDays(monday, 7))} className="icon-btn icon-btn-quiet" aria-label="Sonraki hafta"><Icon name="arrow-right" className="h-4 w-4" /></button>
      </div>
      <button type="button" onClick={() => onChange(mondayOf(new Date()))} disabled={isCurrentWeek} aria-pressed={isCurrentWeek} className="btn btn-quiet px-3 text-[.75rem] font-semibold disabled:cursor-default disabled:bg-[var(--brand-soft)] disabled:text-[var(--brand-strong)] disabled:opacity-70">Bu hafta</button>
      <span aria-live="polite" className="inline-flex min-h-11 items-center rounded-xl bg-[var(--surface-muted)] px-3 text-xs font-bold tabular-nums text-[#5c4d3f]">
        {fmt(monday)} – {fmt(addDays(monday, 5))} (Pzt–Cmt)
      </span>
    </div>
  );
}
