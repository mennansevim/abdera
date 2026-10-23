"use client";

import { useEffect, useMemo, useState } from "react";
import { Icon } from "@/components/icons";
import { SearchInput, Segmented } from "@/components/ui";
import { useBillingBoard, type BoardRow } from "@/lib/billing";
import { currentPeriod, formatMoney, formatPeriod, isCyclePeriod } from "@/lib/billing-format";
import { useSessionState } from "@/lib/use-session-state";
import {
  byStudentName,
  cellState,
  initials,
  isOwed,
  isSelectable,
  lateCount,
  matchesQuery,
  monthEntry,
  remainingOfAll,
  type CellState,
} from "./board-model";
import { AttendanceCards } from "./attendance-cards";
import { CollectSheet, QuickCollect } from "./collect-sheet";

// Aidatlar ekranı (docs/10-decisions.md H17, AD18): tek görünüm. Üstte birebir öğrencilerin delikli
// kartları (4 ders = 1 aidat), altında aylık faturalanan kayıtlar (grup dersleri ve dönem sayacı
// başlamamış birebir kayıt) - onların kartı yok, ay ay listelenir. Arama ikisinde ortak: öğrenci,
// öğretmen, veli veya enstrüman adı. Tahsilat her yerde aynı tek yoldan (Collections.cs).
type ListFilter = "wait" | "paid" | "all";
type SheetState = { row: BoardRow; periods: string[] } | null;
type Toast = { message: string; undo?: () => Promise<void> } | null;

export function DuesBoard() {
  const today = currentPeriod();
  const [month, setMonth] = useState(today);
  const year = Number(month.slice(0, 4));
  const [query, setQuery] = useSessionState("abdera:billing:board-search", "");
  const [listFilter, setListFilter] = useState<ListFilter>("wait");
  const [sheet, setSheet] = useState<SheetState>(null);
  const [toast, setToast] = useState<Toast>(null);
  const { data: board, isError, refetch } = useBillingBoard(year);

  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(() => setToast(null), 6000);
    return () => window.clearTimeout(timer);
  }, [toast]);

  const monthlyRows = useMemo(
    () => (board ?? []).filter((row) => (row.courseKind === "Group" || !row.cycleBillingStartsOn) && matchesQuery(row, query)),
    [board, query],
  );

  return <div className="space-y-3">
    <SearchInput value={query} onChange={setQuery} label="Öğrenci, öğretmen veya veli ara" placeholder="Öğrenci, öğretmen veya veli ara…" />

    <AttendanceCards query={query} onFullCollect={(enrollmentId) => {
      const row = board?.find((item) => item.enrollmentId === enrollmentId);
      if (row) setSheet({ row, periods: [] });
    }} />

    {isError && <div className="app-card px-3 py-6 text-center"><p className="text-meta">Aylık aidatlar yüklenemedi.</p><button type="button" onClick={() => refetch()} className="btn btn-quiet mt-2">Tekrar dene</button></div>}
    {board && monthlyRows.length > 0 && <section aria-labelledby="monthly-dues" className="app-card overflow-hidden">
      <h2 id="monthly-dues" className="border-b border-[var(--line)] px-3 py-2.5 text-sm font-bold">Aylık aidatlar · grup dersleri</h2>
      <ListView rows={monthlyRows} month={month} today={today} onMonth={setMonth} filter={listFilter} onFilter={setListFilter} onOpen={(row, periods) => setSheet({ row, periods })} onToast={setToast} />
    </section>}

    {sheet && <CollectSheet
      key={`${sheet.row.enrollmentId}-${sheet.periods.join(",")}`}
      row={sheet.row}
      year={year}
      initialPeriods={sheet.periods}
      onClose={() => setSheet(null)}
      onCollected={(message) => { setSheet(null); setToast({ message }); }}
    />}

    {toast && <div role="status" className="fixed inset-x-3 bottom-[calc(env(safe-area-inset-bottom,0px)+5rem)] z-40 mx-auto flex max-w-md items-center gap-3 rounded-2xl bg-[var(--foreground)] px-4 py-3 text-sm font-semibold text-white shadow-lg lg:bottom-6">
      <span className="min-w-0 flex-1">{toast.message}</span>
      {toast.undo && <button type="button" onClick={async () => { const undo = toast.undo!; setToast(null); await undo(); }} className="shrink-0 font-extrabold text-[#f3a172]">Geri al</button>}
      <button type="button" onClick={() => setToast(null)} aria-label="Kapat" className="shrink-0 opacity-70"><Icon name="close" className="h-4 w-4" /></button>
    </div>}
  </div>;
}

// ---- Liste ----------------------------------------------------------------------

function ListView({ rows, month, today, onMonth, filter, onFilter, onOpen, onToast }: {
  rows: BoardRow[];
  month: string;
  today: string;
  onMonth: (period: string) => void;
  filter: ListFilter;
  onFilter: (filter: ListFilter) => void;
  onOpen: (row: BoardRow, periods: string[]) => void;
  onToast: (toast: Toast) => void;
}) {
  const year = Number(month.slice(0, 4));
  const { inMonth, paid, waiting, waitingAmount, shown } = useMemo(() => {
    // Dönemle faturalanan birebir kayıtta ayın satırı, o ay başlamış dönemlerdir (monthEntry).
    const all = rows
      .map((row) => ({ row, ...monthEntry(row, month, today) }))
      .filter((item) => item.state !== "none" && item.state !== "cancelled");
    const paidRows = all.filter((item) => item.state === "paid");
    const waitingRows = all.filter((item) => item.state !== "paid");
    const visible = filter === "paid" ? [...paidRows].sort((a, b) => byStudentName(a.row, b.row))
      : filter === "all" ? [...all].sort((a, b) => byStudentName(a.row, b.row))
      // Gecikmesi en çok olan en üstte, sonra ad sırası.
      : [...waitingRows].sort((a, b) => lateCount(b.row, year, today) - lateCount(a.row, year, today) || byStudentName(a.row, b.row));
    return {
      inMonth: all,
      paid: paidRows,
      waiting: waitingRows,
      waitingAmount: waitingRows.reduce((sum, item) => sum + (remainingOfAll(item.row, item.periods) ?? 0), 0),
      shown: visible,
    };
  }, [rows, month, today, filter, year]);

  function shift(delta: number) {
    const [y, m] = month.split("-").map(Number);
    const date = new Date(y, m - 1 + delta, 1);
    const next = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
    if (next <= today) onMonth(next);
  }

  return <div>
    <div className="space-y-2.5 px-3 py-3">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-1" role="group" aria-label="Ay">
          <button type="button" onClick={() => shift(-1)} className="icon-btn icon-btn-quiet !h-9 !w-9" aria-label="Önceki ay"><Icon name="arrow-left" className="h-3.5 w-3.5" /></button>
          <strong className="min-w-28 text-center text-sm">{formatPeriod(month)}</strong>
          <button type="button" onClick={() => shift(1)} disabled={month >= today} className="icon-btn icon-btn-quiet !h-9 !w-9 disabled:opacity-35" aria-label="Sonraki ay"><Icon name="arrow-right" className="h-3.5 w-3.5" /></button>
        </div>
        <span className={`text-sm font-bold tabular-nums ${waiting.length ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>
          {waiting.length ? `${formatMoney(waitingAmount, "TRY")} bekliyor` : inMonth.length ? "Hepsi ödendi" : ""}
        </span>
      </div>
      <div>
        <div className="mb-1 flex justify-between text-xs font-bold"><span>{paid.length}/{inMonth.length} ödedi</span></div>
        <div className="h-2 overflow-hidden rounded-full bg-[var(--surface-muted)]" aria-hidden>
          <div className="h-full rounded-full bg-[var(--success)]" style={{ width: `${inMonth.length ? (paid.length / inMonth.length) * 100 : 0}%` }} />
        </div>
      </div>
      <Segmented
        label="Filtre"
        className="w-full [&>button]:flex-1 [&>button]:justify-center"
        options={[
          { value: "wait", label: `Bekleyen ${waiting.length}` },
          { value: "paid", label: `Ödenen ${paid.length}` },
          { value: "all", label: "Tümü" },
        ]}
        value={filter}
        onChange={onFilter}
      />
    </div>

    {!shown.length && <p className="text-meta border-t border-[var(--line)] px-3 py-6 text-center">{inMonth.length ? "Bu filtrede kimse yok." : "Bu ay için kurs kaydı yok."}</p>}
    <ul className="divide-y divide-[var(--line)] border-t border-[var(--line)]">
      {shown.map(({ row, state, periods }) => <ListRow key={row.enrollmentId} row={row} state={state} periods={periods} onOpen={onOpen} onToast={onToast} />)}
    </ul>
  </div>;
}

function ListRow({ row, state, periods, onOpen, onToast }: {
  row: BoardRow;
  state: CellState;
  // Aylık kayıtta [ay]; dönemle faturalanan kayıtta o ay başlamış dönemler ("D003").
  periods: string[];
  onOpen: (row: BoardRow, periods: string[]) => void;
  onToast: (toast: Toast) => void;
}) {
  // "Ödeme al" kartlardaki ile aynı sade pencereyi açar (QuickCollect: tarih + tutar).
  const [quick, setQuick] = useState(false);
  const remaining = remainingOfAll(row, periods);
  const cell = row.cells.filter((item) => periods.includes(item.period)).sort((a, b) => (b.lastPaymentDate ?? "").localeCompare(a.lastPaymentDate ?? ""))[0];
  const unpaid = periods.filter((period) => isOwed(cellState(row, period)) || cellState(row, period) === "open");
  const label = periods.map(formatPeriod).join(", ");
  const tags = [
    row.siblingDiscount && "kardeş",
    row.multiCourse && "çoklu kurs",
    row.manualDiscountPercent ? `özel %${row.manualDiscountPercent}` : null,
    state === "partial" && "kısmi ödendi",
  ].filter(Boolean);

  return <li className="px-3 py-2.5">
    <div className="flex items-center gap-3">
      <button type="button" onClick={() => onOpen(row, isSelectable(state) ? unpaid : [])} className="flex min-w-0 flex-1 items-center gap-3 text-left">
        <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-[var(--brand-soft)] text-xs font-extrabold text-[var(--brand-strong)]">{initials(row.studentName)}</span>
        <span className="min-w-0">
          <span className="block truncate text-sm font-bold">{row.studentName}</span>
          <span className="text-meta line-clamp-2 text-xs">{row.instrumentName} · {row.teacherName}{periods.some(isCyclePeriod) ? ` · ${label}` : ""}{tags.length ? ` · ${tags.join(" · ")}` : ""}</span>
        </span>
      </button>
      {state === "paid"
        ? <span className="shrink-0 rounded-full bg-[var(--success-soft)] px-2.5 py-1 text-xs font-extrabold text-[var(--success-strong)]">✓ Ödendi{cell?.lastPaymentDate ? ` · ${new Date(`${cell.lastPaymentDate}T00:00:00`).toLocaleDateString("tr-TR", { day: "2-digit", month: "2-digit" })}` : ""}</span>
        // Telefonda tutar düğmenin üstüne çıkar: yan yana durduklarında 320px'te öğrenci adına
        // ~55px kalıyordu.
        : <span className="flex shrink-0 flex-col items-end gap-1 min-[400px]:flex-row min-[400px]:items-center min-[400px]:gap-3">
          {remaining !== null && <span className={`shrink-0 text-right text-sm font-bold tabular-nums ${state === "late" || state === "partial" ? "text-[var(--danger-strong)]" : ""}`}>{formatMoney(remaining, cell?.currency ?? "TRY")}</span>}
          {unpaid.length > 0
            ? <button type="button" onClick={() => setQuick(true)} className="btn shrink-0 border-[1.5px] border-[var(--brand-strong)] bg-white text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">Ödeme al</button>
            : <button type="button" onClick={() => onOpen(row, unpaid)} className="btn shrink-0 border-[1.5px] border-[var(--brand-strong)] bg-white text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">Ödeme al</button>}
        </span>}
    </div>
    {quick && <QuickCollect
      enrollmentId={row.enrollmentId}
      studentName={row.studentName}
      periods={unpaid}
      onClose={() => setQuick(false)}
      onCollected={(message) => { setQuick(false); onToast({ message }); }}
    />}
  </li>;
}
