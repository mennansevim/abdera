"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Icon } from "@/components/icons";
import { SearchInput, Segmented } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useBillingBoard, useCollect, useCorrectPayment, type BoardRow } from "@/lib/billing";
import { currentPeriod, formatMoney, formatPeriod, todayInput } from "@/lib/billing-format";
import { useSessionState } from "@/lib/use-session-state";
import {
  MONTHS_SHORT,
  byStudentName,
  cellState,
  hasDebt,
  initials,
  isOwed,
  isSelectable,
  lateCount,
  matchesQuery,
  periodOf,
  remainingOf,
  type CellState,
} from "./board-model";
import { CollectSheet } from "./collect-sheet";

// Aidatlar ekranı (docs/10-decisions.md H17): iki görünüm, bir ödeme penceresi.
//   Liste   - seçili ayın bekleyenleri; "Ödeme al" tek dokunuşla ayı kapatır.
//   Çizelge - öğrenci × ay; yılın resmi ve geçmiş.
// Görünüm seçimi sayfada durur ve hatırlanır (ilk açılışta telefonda Liste, geniş ekranda
// Çizelge). Arama iki görünümde ortak: öğrenci, öğretmen, veli veya enstrüman adı.
type View = "list" | "grid";
type ListFilter = "wait" | "paid" | "all";
type SheetState = { row: BoardRow; periods: string[] } | null;
type Toast = { message: string; undo?: () => Promise<void> } | null;

const VIEW_KEY = "abdera:billing:view";

export function DuesBoard() {
  const today = currentPeriod();
  const [view, setView] = useState<View>("list");
  const [month, setMonth] = useState(today);
  const year = Number(month.slice(0, 4));
  const [query, setQuery] = useSessionState("abdera:billing:board-search", "");
  const [listFilter, setListFilter] = useState<ListFilter>("wait");
  const [debtOnly, setDebtOnly] = useState(false);
  const [sheet, setSheet] = useState<SheetState>(null);
  const [toast, setToast] = useState<Toast>(null);
  const { data: board, isLoading, isError, refetch } = useBillingBoard(year);

  useEffect(() => {
    let initial: View = window.matchMedia("(min-width: 1024px)").matches ? "grid" : "list";
    try {
      const stored = window.localStorage.getItem(VIEW_KEY);
      if (stored === "list" || stored === "grid") initial = stored;
    } catch {
      // Depolama kapalıysa ekran genişliğine göre başlar.
    }
    // Tarayıcı deposunu ilk açılışta bir kez React'e eşitliyoruz (useSessionState ile aynı desen).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setView(initial);
  }, []);

  function changeView(next: View) {
    setView(next);
    try {
      window.localStorage.setItem(VIEW_KEY, next);
    } catch {
      // Hatırlanamazsa yalnızca bu oturum için geçerli olur.
    }
  }

  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(() => setToast(null), 6000);
    return () => window.clearTimeout(timer);
  }, [toast]);

  const rows = useMemo(() => (board ?? []).filter((row) => matchesQuery(row, query)), [board, query]);
  const teacherHit = useMemo(() => {
    const q = query.trim().toLocaleLowerCase("tr-TR");
    if (!q) return null;
    const names = [...new Set((board ?? []).map((row) => row.teacherName))].filter((name) => name.toLocaleLowerCase("tr-TR").includes(q));
    return names.length === 1 ? names[0] : null;
  }, [board, query]);
  const studentCount = useMemo(() => new Set(rows.map((row) => row.studentId)).size, [rows]);

  return <div className="space-y-3">
    <section className="app-card overflow-hidden">
      <div className="flex flex-wrap items-center gap-2 border-b border-[var(--line)] px-3 py-2.5">
        <Segmented
          label="Görünüm"
          options={[{ value: "list", label: "Liste" }, { value: "grid", label: "Çizelge" }]}
          value={view}
          onChange={changeView}
        />
        <SearchInput value={query} onChange={setQuery} label="Öğrenci, öğretmen veya veli ara" placeholder="Öğrenci, öğretmen veya veli ara…" />
      </div>
      {query.trim() && <p className="text-meta border-b border-[var(--line)] px-3 py-1.5 text-xs">
        {teacherHit && <><strong className="text-[var(--foreground)]">{teacherHit}</strong> öğrencileri · </>}{studentCount} öğrenci
      </p>}

      {isLoading && <p className="text-meta px-3 py-6 text-center">Aidatlar yükleniyor…</p>}
      {isError && <div className="px-3 py-6 text-center"><p className="text-meta">Aidatlar yüklenemedi.</p><button type="button" onClick={() => refetch()} className="btn btn-quiet mt-2">Tekrar dene</button></div>}
      {board && (view === "list"
        ? <ListView rows={rows} month={month} today={today} onMonth={setMonth} filter={listFilter} onFilter={setListFilter} onOpen={(row, periods) => setSheet({ row, periods })} onToast={setToast} />
        : <GridView rows={rows} year={year} today={today} onYear={(next) => setMonth(next === Number(today.slice(0, 4)) ? today : `${next}-12`)} debtOnly={debtOnly} onDebtOnly={setDebtOnly} onOpen={(row, periods) => setSheet({ row, periods })} />)}
    </section>

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
    const all = rows
      .map((row) => ({ row, state: cellState(row, month, today) }))
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
      waitingAmount: waitingRows.reduce((sum, item) => sum + (remainingOf(item.row, month) ?? 0), 0),
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
      {shown.map(({ row, state }) => <ListRow key={row.enrollmentId} row={row} state={state} month={month} onOpen={onOpen} onToast={onToast} />)}
    </ul>
    <p className="text-meta px-3 py-3 text-xs">&quot;Ödeme al&quot; ayın aidatını kayıttaki indirimle nakit olarak kaydeder. Birkaç ay almak, indirimi değiştirmek ya da geçmişi görmek için isme dokun.</p>
  </div>;
}

function ListRow({ row, state, month, onOpen, onToast }: {
  row: BoardRow;
  state: CellState;
  month: string;
  onOpen: (row: BoardRow, periods: string[]) => void;
  onToast: (toast: Toast) => void;
}) {
  const collect = useCollect();
  const correct = useCorrectPayment(row.studentId);
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const remaining = remainingOf(row, month);
  const cell = row.cells.find((item) => item.period === month);
  const tags = [
    row.siblingDiscount && "kardeş",
    row.multiCourse && "çoklu kurs",
    row.manualDiscountPercent ? `özel %${row.manualDiscountPercent}` : null,
    state === "partial" && "kısmi ödendi",
  ].filter(Boolean);

  async function quickPay() {
    if (remaining === null) return;
    setError(null);
    try {
      const result = await collect.mutateAsync({
        enrollmentId: row.enrollmentId,
        periods: [month],
        discounts: null,
        paymentDate: todayInput(),
        method: "Cash",
        expectedTotal: remaining,
      });
      const paymentIds = result.receivables.flatMap((receivable) => {
        const latest = receivable.payments
          .filter((payment) => payment.kind === "Payment")
          .sort((a, b) => (b.recordedAt ?? "").localeCompare(a.recordedAt ?? ""))[0];
        return latest ? [latest.id] : [];
      });
      onToast({
        message: `${row.studentName} · ${formatPeriod(month)}: ${formatMoney(result.total, result.currency)} alındı`,
        undo: paymentIds.length ? async () => {
          for (const paymentId of paymentIds)
            await correct.mutateAsync({ paymentId, correctedAmount: 0, reason: "Ödeme al'a yanlışlıkla basıldı, hemen geri alındı" });
          await queryClient.invalidateQueries({ queryKey: ["billing-board"] });
          onToast({ message: "Ödeme geri alındı" });
        } : undefined,
      });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme kaydedilemedi.");
    }
  }

  return <li className="px-3 py-2.5">
    <div className="flex items-center gap-3">
      <button type="button" onClick={() => onOpen(row, isSelectable(state) ? [month] : [])} className="flex min-w-0 flex-1 items-center gap-3 text-left">
        <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-[var(--brand-soft)] text-xs font-extrabold text-[var(--brand-strong)]">{initials(row.studentName)}</span>
        <span className="min-w-0">
          <span className="block truncate text-sm font-bold">{row.studentName}</span>
          <span className="text-meta block truncate text-xs">{row.instrumentName} · {row.teacherName}{tags.length ? ` · ${tags.join(" · ")}` : ""}</span>
        </span>
      </button>
      {state === "paid"
        ? <span className="shrink-0 rounded-full bg-[var(--success-soft)] px-2.5 py-1 text-xs font-extrabold text-[var(--success-strong)]">✓ Ödendi{cell?.lastPaymentDate ? ` · ${new Date(`${cell.lastPaymentDate}T00:00:00`).toLocaleDateString("tr-TR", { day: "2-digit", month: "2-digit" })}` : ""}</span>
        : <>
          {remaining !== null && <span className={`shrink-0 text-right text-sm font-bold tabular-nums ${state === "late" || state === "partial" ? "text-[var(--danger-strong)]" : ""}`}>{formatMoney(remaining, cell?.currency ?? "TRY")}</span>}
          {remaining !== null && remaining > 0
            ? <button type="button" onClick={quickPay} disabled={collect.isPending} className="btn shrink-0 border-[1.5px] border-[var(--brand-strong)] bg-white text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">{collect.isPending ? "Kaydediliyor…" : "Ödeme al"}</button>
            : <button type="button" onClick={() => onOpen(row, [month])} className="btn shrink-0 border-[1.5px] border-[var(--brand-strong)] bg-white text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">Ödeme al</button>}
        </>}
    </div>
    {error && <p role="alert" className="mt-1.5 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
  </li>;
}

// ---- Çizelge --------------------------------------------------------------------

const GRID_SYMBOL: Record<CellState, string> = { paid: "✓", late: "!", current: "•", partial: "½", open: "", future: "", none: "–", cancelled: "×" };
const GRID_CLASS: Record<CellState, string> = {
  paid: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  late: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  current: "border-[1.5px] border-[var(--brand)] text-[var(--brand-strong)]",
  partial: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  open: "border border-dashed border-[var(--line)]",
  future: "border border-dashed border-[var(--line)]",
  none: "text-[var(--line)]",
  cancelled: "text-[var(--muted)]",
};

function GridView({ rows, year, today, onYear, debtOnly, onDebtOnly, onOpen }: {
  rows: BoardRow[];
  year: number;
  today: string;
  onYear: (year: number) => void;
  debtOnly: boolean;
  onDebtOnly: (value: boolean) => void;
  onOpen: (row: BoardRow, periods: string[]) => void;
}) {
  const scroller = useRef<HTMLDivElement>(null);
  const visible = useMemo(() => {
    const sorted = [...rows].sort(byStudentName);
    return debtOnly ? sorted.filter((row) => hasDebt(row, year, today)) : sorted;
  }, [rows, debtOnly, year, today]);
  const currentIndex = today.startsWith(`${year}-`) ? Number(today.slice(5, 7)) - 1 : -1;
  const paidNow = currentIndex >= 0 ? visible.filter((row) => cellState(row, today, today) === "paid").length : 0;
  const owedNow = currentIndex >= 0 ? visible.filter((row) => { const s = cellState(row, today, today); return s !== "none" && s !== "cancelled"; }).length : 0;

  // Telefonda 12 ay sığmaz: çizelge bu ayın çevresinden açılır, önceki aylar sola kaydırınca.
  useEffect(() => {
    const element = scroller.current;
    if (!element || currentIndex < 0) return;
    const column = element.querySelector<HTMLElement>(`[data-col="${currentIndex}"]`);
    if (column && column.offsetLeft + column.offsetWidth > element.clientWidth)
      element.scrollLeft = column.offsetLeft + column.offsetWidth - element.clientWidth + 48;
  }, [currentIndex, year]);

  return <div>
    <div className="flex flex-wrap items-center justify-between gap-2 px-3 py-3">
      <div className="flex items-center gap-1" role="group" aria-label="Yıl">
        <button type="button" onClick={() => onYear(year - 1)} className="icon-btn icon-btn-quiet !h-9 !w-9" aria-label="Önceki yıl"><Icon name="arrow-left" className="h-3.5 w-3.5" /></button>
        <strong className="min-w-12 text-center text-sm tabular-nums">{year}</strong>
        <button type="button" onClick={() => onYear(year + 1)} disabled={year >= Number(today.slice(0, 4))} className="icon-btn icon-btn-quiet !h-9 !w-9 disabled:opacity-35" aria-label="Sonraki yıl"><Icon name="arrow-right" className="h-3.5 w-3.5" /></button>
      </div>
      <button type="button" aria-pressed={debtOnly} onClick={() => onDebtOnly(!debtOnly)} className={`pressable rounded-full border px-3 py-1.5 text-xs font-bold ${debtOnly ? "border-[var(--foreground)] bg-[var(--foreground)] text-white" : "border-[var(--line)] bg-white"}`}>Yalnızca borcu olanlar</button>
      {currentIndex >= 0 && <span className="text-meta text-xs tabular-nums">{MONTHS_SHORT[currentIndex]}: {paidNow}/{owedNow} ödedi</span>}
    </div>
    <div className="flex flex-wrap gap-1.5 px-3 pb-2 text-[.7rem] font-bold">
      <span className="rounded-full bg-[var(--success-soft)] px-2 py-0.5 text-[var(--success-strong)]">✓ ödendi</span>
      <span className="rounded-full bg-[var(--danger-soft)] px-2 py-0.5 text-[var(--danger-strong)]">! gecikti</span>
      <span className="rounded-full border border-[var(--brand)] px-2 py-0.5 text-[var(--brand-strong)]">• bu ay</span>
      <span className="rounded-full bg-[var(--warning-soft)] px-2 py-0.5 text-[var(--warning-strong)]">½ kısmi</span>
    </div>

    {!visible.length && <p className="text-meta border-t border-[var(--line)] px-3 py-6 text-center">{rows.length ? "Borcu olan kimse yok." : "Bu yıl için kurs kaydı yok."}</p>}
    {!!visible.length && <div ref={scroller} className="max-h-[calc(100dvh-15rem)] overflow-auto border-t border-[var(--line)]">
      <table className="border-separate border-spacing-0 text-sm">
        <thead>
          <tr>
            <th scope="col" className="sticky left-0 top-0 z-30 min-w-36 border-b border-r border-[var(--line)] bg-[var(--surface)] px-3 py-2 text-left text-xs font-bold text-[var(--muted)] sm:min-w-52">Öğrenci</th>
            {MONTHS_SHORT.map((label, index) => <th key={label} data-col={index} scope="col" className={`sticky top-0 z-20 border-b border-[var(--line)] bg-[var(--surface)] px-0.5 py-2 text-center text-xs font-bold ${index === currentIndex ? "text-[var(--brand-strong)]" : "text-[var(--muted)]"}`}>{label}</th>)}
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
            {MONTHS_SHORT.map((label, index) => {
              const period = periodOf(year, index);
              const state = cellState(row, period, today);
              const clickable = state !== "none";
              return <td key={label} className="border-b border-[var(--line)] p-0.5 text-center">
                <button
                  type="button"
                  disabled={!clickable}
                  onClick={() => onOpen(row, isSelectable(state) ? [period] : [])}
                  aria-label={`${row.studentName} ${formatPeriod(period)}: ${state === "paid" ? "ödendi" : isOwed(state) ? "ödenmedi" : state === "none" ? "kayıt dışında" : "açık"}`}
                  className={`mx-auto grid h-8 w-9 place-items-center rounded-lg text-sm font-extrabold sm:w-11 ${GRID_CLASS[state]} ${clickable ? "pressable" : "cursor-default"}`}
                >{GRID_SYMBOL[state]}</button>
              </td>;
            })}
          </tr>)}
        </tbody>
      </table>
    </div>}
    <p className="text-meta px-3 py-3 text-xs">Bir hücreye dokununca o ay seçili ödeme penceresi açılır; isme dokununca öğrencinin tamamı ve geçmişi.</p>
  </div>;
}
