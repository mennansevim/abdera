"use client";

// Öğrenci ve öğretmen analizi ekranlarının ortak yapı taşları: koyu özet kartı, halka
// göstergeler ve ayları sütun sütun hizalayan "şerit". Her satır aynı sütun ızgarasını
// kullanır ki aynı ayın devamı, seviyesi ve aidatı alt alta okunabilsin.
import { useEffect, useRef, type CSSProperties, type ReactNode } from "react";
import { Segmented } from "@/components/ui";
import { PERIOD_OPTIONS, levelName, longMonth, shortMonth, type AnalysisLessonState, type PeriodOption } from "@/lib/analysis";

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
// Ayları sütun sütun hizalayan tablo. İlk canlı sürümün dersleri:
// - Kayıttan önceki boş aylar gösterilmez (çağıran trimLeadingMonths ile kırpar).
// - Hiç verisi olmayan satır çizilmez; boş hücrede metin değil, sessiz bir çizgi durur.
// - Hücre içerikleri ortalanır; seçili ay bütün sütun boyunca hafif bir zeminle işaretlenir.

const ROW = "grid grid-cols-[6rem_minmax(0,1fr)] sm:grid-cols-[7.5rem_minmax(0,1fr)]";
const TINT = "bg-[#fbf3ea]";
// Şerit yatay kaydığında satır adları yerinde kalsın.
export const STICKY_LABEL = "sticky left-0 z-20 bg-white";

// Sütunlar en fazla 10rem: iki aylık yeni bir kayıtta sütunlar kartın tamamına yayılıp
// kopuk görünüyordu. Şerit sola hizalanır, uzun dönemde yine ekranı doldurur.
export function monthColumns(count: number): CSSProperties {
  return { gridTemplateColumns: `repeat(${count}, minmax(0, 10rem))` };
}

export function Strip({ periods, selected, onSelect, children, caption }: {
  periods: string[]; selected: number; onSelect: (index: number) => void; children: ReactNode; caption: string;
}) {
  // Dar ekranda şerit yatay kayar; en güncel ay görünür olsun diye açılışta sağa kaydırılır.
  const scroller = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const element = scroller.current;
    if (element) element.scrollLeft = element.scrollWidth;
  }, [periods.length]);
  return (
    <div ref={scroller} className="overflow-x-auto">
      <div role="group" aria-label={caption} className="flex flex-col" style={{ minWidth: `${7.5 * 16 + periods.length * 84}px` }}>
        <div className={ROW}>
          <span className={STICKY_LABEL} />
          <div className="grid" style={monthColumns(periods.length)}>
            {periods.map((period, index) => {
              const active = index === selected;
              const showYear = index === 0 || period.endsWith("-01");
              return (
                <div key={period} className={`rounded-t-2xl px-1 pt-1.5 pb-1 ${active ? TINT : ""}`}>
                  <button
                    type="button"
                    onClick={() => onSelect(index)}
                    aria-pressed={active}
                    aria-label={longMonth(period)}
                    className={`mx-auto flex min-h-11 w-full flex-col items-center justify-center rounded-xl transition-colors ${active ? "text-[var(--brand-strong)]" : "text-[var(--foreground)] hover:bg-[var(--surface-muted)]/70"}`}
                  >
                    <span className="text-[.85rem] font-extrabold capitalize">{shortMonth(period)}</span>
                    <span className={`text-[.65rem] font-bold tabular-nums ${showYear ? "text-[var(--muted)]" : "invisible"}`}>{period.slice(0, 4)}</span>
                  </button>
                </div>
              );
            })}
          </div>
        </div>
        {children}
        <div className={ROW} aria-hidden>
          <span className={STICKY_LABEL} />
          <div className="grid" style={monthColumns(periods.length)}>
            {periods.map((period, index) => <span key={period} className={`h-2 rounded-b-2xl ${index === selected ? TINT : ""}`} />)}
          </div>
        </div>
      </div>
    </div>
  );
}

function RowLabel({ label, hint }: { label: string; hint?: ReactNode }) {
  return (
    <div className={`flex flex-col justify-center gap-0.5 py-2.5 pr-3 ${STICKY_LABEL}`}>
      <span className="text-[.78rem] font-bold">{label}</span>
      {hint && <span className="text-[.68rem] font-semibold leading-tight text-[var(--muted)]">{hint}</span>}
    </div>
  );
}

export function StripRow({ label, hint, periods, selected, children }: {
  label: string; hint?: ReactNode; periods: string[]; selected: number; children: (index: number) => ReactNode;
}) {
  return (
    <div className={`${ROW} border-t border-[#f3ebe0]`}>
      <RowLabel label={label} hint={hint} />
      <div className="grid" style={monthColumns(periods.length)}>
        {periods.map((period, index) => (
          <div key={period} className={`flex min-h-14 min-w-0 flex-col items-center justify-center gap-1 px-1.5 py-2.5 text-center ${index === selected ? TINT : ""}`}>
            {children(index)}
          </div>
        ))}
      </div>
    </div>
  );
}

// Boş hücre: "ders yok / not yok" metinleri sütunları gürültüyle dolduruyordu.
export function EmptyCell({ label = "Veri yok" }: { label?: string }) {
  return <span role="img" aria-label={label} title={label} className="h-[3px] w-3.5 rounded-full bg-[#ebe1d4]" />;
}

// Aylık ortalama zorluk çizgisi (1–5). Ekseni yazıyla göstermek yerine her noktanın üstünde
// değeri durur; seviye adı ipucunda. Değeri olmayan ay çizgiyi böler.
export function LevelRow({ label, hint, values, periods, selected, format }: {
  label: string; hint?: ReactNode; values: (number | null)[]; periods: string[]; selected: number; format: (value: number) => string;
}) {
  const y = (value: number) => 82 - ((value - 1) / 4) * 52;
  const segments: string[][] = [[]];
  values.forEach((value, index) => {
    if (value === null) { if (segments.at(-1)!.length) segments.push([]); return; }
    segments.at(-1)!.push(`${index * 100 + 50},${y(value)}`);
  });
  return (
    <div className={`${ROW} border-t border-[#f3ebe0]`}>
      <RowLabel label={label} hint={hint} />
      <div className="relative h-24">
        <div className="absolute inset-0 grid" style={monthColumns(periods.length)}>
          {values.map((value, index) => (
            <div key={periods[index]} className={`relative ${index === selected ? TINT : ""}`}>
              {value === null
                ? <span className="absolute inset-0 grid place-items-center"><EmptyCell label="Ders notunda zorluk yok" /></span>
                : <span title={`${format(value)} · ${levelName(value)}`}>
                    <span className="absolute left-1/2 z-10 h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-[2.5px] border-[var(--brand)] bg-white" style={{ top: `${y(value)}%` }} />
                    <span className="absolute left-1/2 -translate-x-1/2 text-[.7rem] font-extrabold tabular-nums text-[var(--brand-strong)]" style={{ top: `calc(${y(value)}% - 22px)` }}>{format(value)}</span>
                  </span>}
            </div>
          ))}
        </div>
        <svg viewBox={`0 0 ${periods.length * 100} 100`} preserveAspectRatio="none" aria-hidden className="pointer-events-none absolute inset-0 h-full w-full">
          {segments.filter((points) => points.length > 1).map((points, index) => (
            <polyline key={index} points={points.join(" ")} fill="none" stroke="var(--brand)" strokeOpacity="0.55" strokeWidth="2" strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
          ))}
        </svg>
      </div>
    </div>
  );
}

// Şeritte çizilmeyen satırları tek satırda açıklar - kullanıcı "seviye nereye gitti" diye sormasın.
export function StripFootnote({ children }: { children: ReactNode }) {
  return <p className="text-meta flex items-start gap-1.5 pt-1"><span aria-hidden className="mt-[.45rem] h-1 w-1 shrink-0 rounded-full bg-[var(--muted)]" />{children}</p>;
}

export function MiniBar({ percent, label, tone = "muted" }: { percent: number | null; label: string; tone?: "muted" | "brand" }) {
  return (
    <div className="flex w-full max-w-[4.5rem] flex-col items-center gap-1">
      <span className="text-xs font-extrabold tabular-nums">{label}</span>
      <span className="block h-1.5 w-full overflow-hidden rounded-full bg-[#f1e7da]">
        <span className={`block h-full rounded-full ${tone === "brand" ? "bg-[var(--brand)]" : "bg-[var(--muted)]"}`} style={{ width: `${percent ?? 0}%` }} />
      </span>
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
