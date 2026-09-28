"use client";

// Pazartesi-Cumartesi hafta seçici - öğretmen listesi ve benchmark ekranı ortak kullanır
// (backend Benchmark.cs haftayı Pazartesi'ye normalize eder). Öğretmen ÖDEME haftası
// (TeacherPayWeek) da aynı Pzt-Cmt penceresidir; ödeme formu teacher-payout-form.tsx.
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

// Yakın haftalar için tarih yerine ad (kullanıcı isteği: "Bu hafta, Geçen hafta gibi isimler
// kullan"); uzak haftalarda null döner, yalnızca tarih aralığı gösterilir.
export function relativeWeekName(monday: Date, today = new Date()): string | null {
  const weeks = Math.round((mondayOf(monday).getTime() - mondayOf(today).getTime()) / (7 * 24 * 60 * 60 * 1000));
  return ({ [-2]: "İki hafta önce", [-1]: "Geçen hafta", 0: "Bu hafta", 1: "Gelecek hafta" } as Record<number, string>)[weeks] ?? null;
}

// "YYYY-MM-DD" → yerel gece yarısı (saat dilimine takılmadan).
export function fromIsoDate(iso: string): Date {
  const [year, month, day] = iso.split("-").map(Number);
  return new Date(year, month - 1, day);
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
      {!isCurrentWeek && <button type="button" onClick={() => onChange(mondayOf(new Date()))} className="btn btn-quiet px-3 text-[.75rem] font-semibold">Bu haftaya dön</button>}
      <span aria-live="polite" className="inline-flex min-h-11 items-center gap-1.5 rounded-xl bg-[var(--surface-muted)] px-3 text-xs font-bold tabular-nums text-[#5c4d3f]">
        {relativeWeekName(monday) && <span className="text-[var(--foreground)]">{relativeWeekName(monday)} ·</span>}
        <span>{fmt(monday)} – {fmt(addDays(monday, 5))}</span>
      </span>
    </div>
  );
}
