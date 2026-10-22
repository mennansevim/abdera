"use client";

import { useMemo } from "react";
import type { BoardRow } from "@/lib/billing";
import { cycleKey, cycleNumber, formatPeriod } from "@/lib/billing-format";
import { CYCLE_LABEL, GRID_CLASS, GRID_SYMBOL, byStudentName, cyclePeriods, cycleCellState, isOwed, isSelectable } from "./board-model";

// Çizelgenin dönem sütunları (docs/10-decisions.md AD): birebir kayıtta aidat ay yerine 4 derslik
// dönemle açılır, satır öğrencinin 1., 2., 3. dönemlerini gösterir. Sıradaki dönem kesik çizgili
// "peşin" hücredir - dokununca o dönem seçili ödeme penceresi açılır.
export function CycleGrid({ rows, debtOnly, onDebtOnly, onOpen }: {
  rows: BoardRow[];
  debtOnly: boolean;
  onDebtOnly: (value: boolean) => void;
  onOpen: (row: BoardRow, periods: string[]) => void;
}) {
  const visible = useMemo(() => {
    const sorted = [...rows].sort(byStudentName);
    return debtOnly ? sorted.filter((row) => cyclePeriods(row).some((period) => isOwed(cycleCellState(row, period)))) : sorted;
  }, [rows, debtOnly]);

  // Sütunlar: görünen satırların en küçük açılmış döneminden en büyük "sıradaki" dönemine.
  const columns = useMemo(() => {
    const numbers = visible.flatMap((row) => [...cyclePeriods(row).map(cycleNumber), row.nextCycleNumber]);
    if (!numbers.length) return [];
    const first = Math.min(...numbers);
    const last = Math.max(...numbers);
    return Array.from({ length: last - first + 1 }, (_, index) => first + index);
  }, [visible]);

  return <div>
    <div className="flex flex-wrap items-center justify-between gap-2 px-3 py-3">
      <button type="button" aria-pressed={debtOnly} onClick={() => onDebtOnly(!debtOnly)} className={`pressable rounded-full border px-3 py-1.5 text-xs font-bold ${debtOnly ? "border-[var(--foreground)] bg-[var(--foreground)] text-white" : "border-[var(--line)] bg-white"}`}>Yalnızca borcu olanlar</button>
      <span className="text-meta text-xs">Her dönem 4 ders · dönemin ilk dersinde aidatı açılır</span>
    </div>
    <div className="flex flex-wrap gap-1.5 px-3 pb-2 text-[.7rem] font-bold">
      <span className="rounded-full bg-[var(--success-soft)] px-2 py-0.5 text-[var(--success-strong)]">✓ ödendi</span>
      <span className="rounded-full border border-[var(--brand)] px-2 py-0.5 text-[var(--brand-strong)]">• ödenmedi</span>
      <span className="rounded-full bg-[var(--danger-soft)] px-2 py-0.5 text-[var(--danger-strong)]">! gecikti</span>
      <span className="rounded-full bg-[var(--warning-soft)] px-2 py-0.5 text-[var(--warning-strong)]">½ kısmi</span>
      <span className="rounded-full border border-dashed border-[var(--muted)] px-2 py-0.5 text-[var(--muted)]">peşin alınabilir</span>
    </div>

    {!visible.length && <p className="text-meta border-t border-[var(--line)] px-3 py-6 text-center">{rows.length ? "Borcu olan kimse yok." : "Dönemle faturalanan birebir kayıt yok."}</p>}
    {!!visible.length && <div className="max-h-[calc(100dvh-17rem)] overflow-auto border-t border-[var(--line)]">
      <table className="border-separate border-spacing-0 text-sm">
        <thead>
          <tr>
            <th scope="col" className="sticky left-0 top-0 z-30 min-w-36 border-b border-r border-[var(--line)] bg-[var(--surface)] px-3 py-2 text-left text-xs font-bold text-[var(--muted)] sm:min-w-52">Öğrenci</th>
            {columns.map((number) => <th key={number} scope="col" className="sticky top-0 z-20 whitespace-nowrap border-b border-[var(--line)] bg-[var(--surface)] px-0.5 py-2 text-center text-xs font-bold text-[var(--muted)]">{number}.</th>)}
          </tr>
        </thead>
        <tbody>
          {visible.map((row) => <tr key={row.enrollmentId}>
            <th scope="row" className="sticky left-0 z-10 max-w-36 border-b border-r border-[var(--line)] bg-[var(--surface)] px-3 py-1 text-left font-normal sm:max-w-52">
              <button type="button" onClick={() => onOpen(row, [])} className="block w-full min-w-0 text-left">
                <span className="block truncate text-sm font-bold underline decoration-[var(--line)] decoration-dotted underline-offset-4">{row.studentName}</span>
                <span className="text-meta block truncate text-[.7rem]">{row.instrumentName}</span>
              </button>
            </th>
            {columns.map((number) => {
              const period = cycleKey(number);
              const state = cycleCellState(row, period);
              // Peşin alınabilecek yalnızca sıradaki dönem; daha ilerisi pencerede seçilir.
              const shown = state === "future" && number !== row.nextCycleNumber ? "none" : state;
              const cell = row.cells.find((item) => item.period === period);
              const started = cell?.accruedOn ? ` · ${new Date(`${cell.accruedOn}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} başladı` : "";
              const clickable = shown !== "none";
              return <td key={number} className="border-b border-[var(--line)] p-0.5 text-center">
                <button
                  type="button"
                  disabled={!clickable}
                  onClick={() => onOpen(row, isSelectable(shown) ? [period] : [])}
                  title={`${formatPeriod(period)}: ${CYCLE_LABEL[shown]}${started}`}
                  aria-label={`${row.studentName} ${formatPeriod(period)}: ${CYCLE_LABEL[shown]}${started}`}
                  className={`mx-auto grid h-8 w-9 place-items-center rounded-lg text-sm font-extrabold sm:w-11 ${GRID_CLASS[shown]} ${clickable ? "pressable" : "cursor-default"}`}
                >{GRID_SYMBOL[shown]}</button>
              </td>;
            })}
          </tr>)}
        </tbody>
      </table>
    </div>}
    <p className="text-meta px-3 py-3 text-xs">Dönemler yoklama kartından açılır; geçiş öncesi aylık aidatlar &quot;Aylar&quot;da kalır. Bir hücreye dokununca o dönem seçili ödeme penceresi açılır.</p>
  </div>;
}
