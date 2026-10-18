"use client";

// Öğrenci ve öğretmen analizi ekranlarının ortak yapı taşları: koyu özet kartı, halka
// göstergeler ve ayları sütun sütun hizalayan "şerit". Her satır aynı sütun ızgarasını
// kullanır ki aynı ayın devamı, seviyesi ve aidatı alt alta okunabilsin.
import type { CSSProperties, ReactNode } from "react";
import { Segmented } from "@/components/ui";
import { PERIOD_OPTIONS, shortMonth, type AnalysisLessonState, type PeriodOption } from "@/lib/analysis";

export const STATE_STYLE: Record<AnalysisLessonState, { label: string; dot: CSSProperties }> = {
  Present: { label: "Geldi", dot: { background: "var(--success)" } },
  Makeup: { label: "Telafi", dot: { background: "var(--success)", boxShadow: "0 0 0 2px var(--surface), 0 0 0 3.5px var(--success)" } },
  Excused: { label: "Mazeretli", dot: { background: "#d69a2d" } },
  Absent: { label: "Gelmedi", dot: { background: "var(--danger-strong)" } },
  Cancelled: { label: "İptal", dot: { background: "#ddd0bf" } },
  Unmarked: { label: "Yoklama yok", dot: { background: "transparent", boxShadow: "inset 0 0 0 2px var(--muted)" } },
  Upcoming: { label: "Planlı", dot: { background: "transparent", boxShadow: "inset 0 0 0 1.5px var(--line)" } },
};

export function StateLegend({ states }: { states: AnalysisLessonState[] }) {
  return (
    <ul className="flex flex-wrap gap-x-3.5 gap-y-1 text-[.75rem] font-semibold text-[var(--muted)]">
      {states.map((state) => (
        <li key={state} className="inline-flex items-center gap-1.5">
          <span aria-hidden className="h-2.5 w-2.5 rounded-full" style={STATE_STYLE[state].dot} />
          {STATE_STYLE[state].label}
        </li>
      ))}
    </ul>
  );
}

export function PeriodPicker({ value, onChange }: { value: PeriodOption; onChange: (value: PeriodOption) => void }) {
  return <Segmented label="Dönem" options={PERIOD_OPTIONS.map((option) => ({ ...option }))} value={value} onChange={onChange} />;
}

// Koyu zeminli üst kart. Renkler token değil sabit: kart her temada koyu kalır.
export function AnalysisHero({ children, summary }: { children: ReactNode; summary?: ReactNode }) {
  return (
    <header className="flex flex-col gap-5 rounded-3xl bg-[#2b1f18] px-5 py-6 text-[#f7efe4] sm:px-7">
      {children}
      {summary && <p className="border-t border-[#45362c] pt-4 font-serif text-[1.05rem] italic leading-snug text-[#f3e6d6] sm:text-lg">{summary}</p>}
    </header>
  );
}

export function HeroChip({ children, tone = "line" }: { children: ReactNode; tone?: "line" | "good" | "warn" }) {
  const style = tone === "good" ? "bg-[#3d3027] text-[#9fd6a5]" : tone === "warn" ? "bg-[#3d3027] text-[#f2c14e]" : "border border-[#6b5444] text-[#e9d8c4]";
  return <span className={`rounded-full px-2.5 py-0.5 text-xs font-semibold ${style}`}>{children}</span>;
}

const CIRCUMFERENCE = 2 * Math.PI * 30;

export function Gauge({ label, value, hint, percent, color }: { label: string; value: string; hint: string; percent: number | null; color: string }) {
  const filled = percent === null ? 0 : (CIRCUMFERENCE * Math.max(0, Math.min(100, percent))) / 100;
  return (
    <div className="flex min-w-0 items-center gap-3">
      <div className="relative h-[72px] w-[72px] shrink-0">
        <svg width="72" height="72" viewBox="0 0 72 72" aria-hidden>
          <circle cx="36" cy="36" r="30" fill="none" stroke="#3d3027" strokeWidth="6" />
          <circle cx="36" cy="36" r="30" fill="none" stroke={color} strokeWidth="6" strokeLinecap="round"
            strokeDasharray={`${filled} ${CIRCUMFERENCE}`} transform="rotate(-90 36 36)" />
        </svg>
        <span className="absolute inset-0 grid place-items-center text-[1.05rem] font-extrabold tabular-nums">{value}</span>
      </div>
      <div className="min-w-0">
        <p className="text-[.7rem] font-bold uppercase tracking-[.08em] text-[#c9b6a3]">{label}</p>
        <p className="text-[.82rem] font-semibold leading-snug">{hint}</p>
      </div>
    </div>
  );
}

// --- Şerit -----------------------------------------------------------------

export function Strip({ periods, selected, onSelect, children, caption }: {
  periods: string[]; selected: number; onSelect: (index: number) => void; children: ReactNode; caption: string;
}) {
  return (
    <div className="-mx-1 overflow-x-auto px-1">
      <div role="group" aria-label={caption} className="flex flex-col" style={{ minWidth: `${9 * 16 + periods.length * 76}px` }}>
        <div className="grid grid-cols-[9rem_minmax(0,1fr)]">
          <span />
          <StripCells periods={periods}>
            {(index) => (
              <button
                type="button"
                onClick={() => onSelect(index)}
                aria-pressed={index === selected}
                className={`min-h-11 w-full rounded-t-xl text-[.82rem] font-extrabold capitalize transition-colors ${index === selected ? "bg-[#fbf1e6] text-[var(--brand-strong)] shadow-[inset_0_-3px_0_var(--brand)]" : "hover:bg-[var(--surface-muted)]/60"}`}
              >
                {shortMonth(periods[index]!)}
              </button>
            )}
          </StripCells>
        </div>
        {children}
      </div>
    </div>
  );
}

function StripCells({ periods, children }: { periods: string[]; children: (index: number) => ReactNode }) {
  return (
    <div className="grid" style={{ gridTemplateColumns: `repeat(${periods.length}, minmax(0, 1fr))` }}>
      {periods.map((period, index) => <div key={period} className="min-w-0">{children(index)}</div>)}
    </div>
  );
}

export function StripRow({ label, hint, periods, selected, children }: {
  label: string; hint?: ReactNode; periods: string[]; selected: number; children: (index: number) => ReactNode;
}) {
  return (
    <div className="grid grid-cols-[9rem_minmax(0,1fr)] border-t border-[#f1e7da]">
      <div className="flex flex-col justify-center gap-0.5 py-3 pr-2">
        <span className="text-xs font-bold text-[var(--muted)]">{label}</span>
        {hint && <span className="text-[.7rem] font-semibold leading-tight text-[#9a8573]">{hint}</span>}
      </div>
      <StripCells periods={periods}>
        {(index) => <div className={`h-full px-2 py-3 ${index === selected ? "bg-[#fbf1e6]" : ""}`}>{children(index)}</div>}
      </StripCells>
    </div>
  );
}

// Aylık ortalama zorluk çizgisi: ölçek 1–5, değeri olmayan ay çizgiyi böler.
export function LevelRow({ label, values, periods, selected, format }: {
  label: string; values: (number | null)[]; periods: string[]; selected: number; format: (value: number) => string;
}) {
  const y = (value: number) => 88 - ((value - 1) / 4) * 76;
  const segments: string[][] = [];
  values.forEach((value, index) => {
    if (value === null) { segments.push([]); return; }
    if (!segments.length) segments.push([]);
    segments[segments.length - 1]!.push(`${index * 100 + 50},${y(value)}`);
  });
  return (
    <div className="grid grid-cols-[9rem_minmax(0,1fr)] border-t border-[#f1e7da]">
      <div className="flex flex-col justify-between py-3 pr-2">
        <span className="text-xs font-bold text-[var(--muted)]">{label}</span>
        <span aria-hidden className="text-[.7rem] font-semibold leading-[1.6] text-[#9a8573]">İleri<br />Orta<br />Başlangıç</span>
      </div>
      <div className="relative h-28">
        <div className="absolute inset-0 grid" style={{ gridTemplateColumns: `repeat(${periods.length}, minmax(0, 1fr))` }}>
          {values.map((value, index) => (
            <div key={periods[index]} className={`relative ${index === selected ? "bg-[#fbf1e6]" : ""}`}>
              {value === null
                ? <span className="absolute inset-x-0 top-1/2 -translate-y-1/2 text-center text-[.7rem] font-semibold text-[#b6a594]">not yok</span>
                : <>
                    <span className="absolute left-1/2 h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-[3px] border-[var(--brand)] bg-white" style={{ top: `${y(value)}%` }} />
                    <span className="absolute left-1/2 -translate-x-1/2 text-[.7rem] font-extrabold text-[var(--brand-strong)]" style={{ top: `calc(${y(value)}% - 24px)` }}>{format(value)}</span>
                  </>}
            </div>
          ))}
        </div>
        <svg viewBox={`0 0 ${periods.length * 100} 100`} preserveAspectRatio="none" aria-hidden className="pointer-events-none absolute inset-0 h-full w-full">
          <line x1="0" y1={y(2.5)} x2={periods.length * 100} y2={y(2.5)} stroke="#f1e7da" vectorEffect="non-scaling-stroke" />
          <line x1="0" y1={y(3.5)} x2={periods.length * 100} y2={y(3.5)} stroke="#f1e7da" vectorEffect="non-scaling-stroke" />
          {segments.filter((points) => points.length > 1).map((points, index) => (
            <polyline key={index} points={points.join(" ")} fill="none" stroke="var(--brand)" strokeWidth="2.5" strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
          ))}
        </svg>
      </div>
    </div>
  );
}

export function MiniBar({ percent, label, tone = "muted" }: { percent: number | null; label: string; tone?: "muted" | "brand" }) {
  return (
    <div className="flex items-center gap-2">
      <span className="h-1.5 flex-1 overflow-hidden rounded-full bg-[#f1e7da]">
        <span className={`block h-full rounded-full ${tone === "brand" ? "bg-[var(--brand)]" : "bg-[var(--muted)]"}`} style={{ width: `${percent ?? 0}%` }} />
      </span>
      <span className="text-[.7rem] font-bold tabular-nums text-[var(--muted)]">{label}</span>
    </div>
  );
}

export function rateTone(value: number | null) {
  if (value === null) return "text-[var(--muted)]";
  return value < 85 ? "text-[var(--warning-strong)]" : "text-[var(--success-strong)]";
}

export function AnalysisCard({ title, action, children, className = "" }: { title: string; action?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={`app-card flex flex-col gap-3.5 p-5 ${className}`}>
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <h2 className="shrink-0 text-[.95rem] font-extrabold">{title}</h2>
        {action}
      </div>
      {children}
    </section>
  );
}

export function StatBlock({ value, label, tone }: { value: ReactNode; label: string; tone?: string }) {
  return (
    <div className="min-w-0">
      <p className={`text-xl font-extrabold tabular-nums ${tone ?? ""}`}>{value}</p>
      <p className="text-meta">{label}</p>
    </div>
  );
}
