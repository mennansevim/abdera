import type { BoardRow } from "@/lib/billing";
import { currentPeriod, cycleKey, cycleNumber, isCyclePeriod } from "@/lib/billing-format";

// Liste, Çizelge ve ödeme penceresinin ay hücresini aynı biçimde okuması için tek yer.
export const MONTHS_SHORT = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];

// paid: ödendi · late: vadesi geçmiş açık ay · current: bu ayın açık aidatı · partial: üzerinde
// ödeme var, kalanı açık · open: erken açılmış ileri ay · future: henüz açılmamış ileri ay
// (peşin alınabilir) · none: kayıt dönemi dışında · cancelled: iptal edilmiş aidat.
export type CellState = "paid" | "late" | "current" | "partial" | "open" | "future" | "none" | "cancelled";

export const CELL_LABEL: Record<CellState, string> = {
  paid: "✓ ödendi",
  late: "gecikti",
  current: "bu ay",
  partial: "kısmi",
  open: "açık",
  future: "peşin",
  none: "—",
  cancelled: "iptal",
};

export function isSelectable(state: CellState) {
  return state === "late" || state === "current" || state === "partial" || state === "open" || state === "future";
}

export function isOwed(state: CellState) {
  return state === "late" || state === "current" || state === "partial";
}

export function periodOf(year: number, monthIndex: number) {
  return `${year}-${String(monthIndex + 1).padStart(2, "0")}`;
}

// Birebir kayıt geçiş gününden itibaren ay yerine 4 derslik dönemle faturalanır (docs/10-decisions.md
// AD): o aylarda aylık borç yoktur, aidat dönem hücrelerindedir ("D003").
export function cycleStartMonth(row: BoardRow) {
  return row.cycleBillingStartsOn?.slice(0, 7) ?? null;
}

// Kayıt ayından itibaren her ay borçtur; kayıttan önceki ve biten kaydın sonrası değildir
// (lib/billing-format isObligatedPeriod ile aynı kural).
export function cellState(row: BoardRow, period: string, today = currentPeriod()): CellState {
  if (isCyclePeriod(period)) return cycleCellState(row, period);
  if (period < row.startedAt.slice(0, 7)) return "none";
  if (row.endedAt && period > row.endedAt.slice(0, 7)) return "none";
  const cell = row.cells.find((item) => item.period === period);
  const cycleStart = cycleStartMonth(row);
  if (!cell && cycleStart && period >= cycleStart) return "none";
  if (cell) {
    if (cell.status === "Paid") return "paid";
    if (cell.status === "Cancelled") return "cancelled";
    if (cell.status === "Partial" || cell.totalPaid > 0) return "partial";
    if (period < today) return "late";
    return period === today ? "current" : "open";
  }
  if (period < today) return "late";
  return period === today ? "current" : "future";
}

// Açılmış ayın kalan tutarı; açılmamış ay için null (tutarı sunucu tahsilatta hesaplar).
export function remainingOf(row: BoardRow, period: string) {
  const cell = row.cells.find((item) => item.period === period);
  if (!cell || cell.status === "Cancelled") return null;
  return Math.max(0, cell.amount - cell.totalPaid);
}

// Dönem hücresi: açılmış dönem ödenmediyse "current" (vadesi geçince "late"); peşin ödenmiş ama
// başlamamış dönem "paid"; açılmamış dönemlerden sıradakiler "future" (peşin alınabilir).
export function cycleCellState(row: BoardRow, period: string): CellState {
  const cell = row.cells.find((item) => item.period === period);
  if (!cell) return cycleNumber(period) >= row.nextCycleNumber ? "future" : "none";
  if (cell.status === "Paid") return "paid";
  if (cell.status === "Cancelled") return "cancelled";
  if (cell.status === "Partial" || cell.totalPaid > 0) return "partial";
  if (cell.status === "Overdue") return "late";
  return cell.accruedOn ? "current" : "open";
}

export const CYCLE_LABEL: Record<CellState, string> = {
  paid: "✓ ödendi",
  late: "gecikti",
  current: "ödenmedi",
  partial: "kısmi",
  open: "açık",
  future: "peşin",
  none: "—",
  cancelled: "iptal",
};

export function cyclePeriods(row: BoardRow) {
  return row.cells.filter((cell) => isCyclePeriod(cell.period)).map((cell) => cell.period).sort();
}

// Ödeme penceresinin göstereceği dönemler: açılmış/ödenmiş olanlar + peşin alınabilecek sıradaki `ahead` dönem.
export function cycleChoices(row: BoardRow, ahead = 4) {
  const opened = cyclePeriods(row);
  const next = Array.from({ length: ahead }, (_, index) => cycleKey(row.nextCycleNumber + index));
  return [...new Set([...opened, ...next])].sort();
}

// Liste görünümünde bir ayın satırı: aylık kayıtta o ayın kendisi, dönemle faturalanan kayıtta o ay
// başlamış (tahakkuk etmiş) dönemler. Hiçbiri yoksa o ay için borç yoktur.
export function monthEntry(row: BoardRow, month: string, today = currentPeriod()): { periods: string[]; state: CellState } {
  const cycleStart = cycleStartMonth(row);
  if (!cycleStart || month < cycleStart || row.cells.some((cell) => cell.period === month))
    return { periods: [month], state: cellState(row, month, today) };

  const periods = row.cells
    .filter((cell) => isCyclePeriod(cell.period) && cell.accruedOn?.startsWith(month) && cell.status !== "Cancelled")
    .map((cell) => cell.period)
    .sort();
  if (!periods.length) return { periods: [], state: "none" };
  const states = periods.map((period) => cycleCellState(row, period));
  const worst = (["late", "partial", "current", "open", "paid"] as const).find((state) => states.includes(state)) ?? "paid";
  return { periods, state: worst };
}

export function remainingOfAll(row: BoardRow, periods: string[]) {
  const values = periods.map((period) => remainingOf(row, period));
  return values.every((value) => value === null) ? null : values.reduce<number>((sum, value) => sum + (value ?? 0), 0);
}

export function lateCount(row: BoardRow, year: number, today = currentPeriod()) {
  let count = 0;
  for (let index = 0; index < 12; index++) {
    const state = cellState(row, periodOf(year, index), today);
    if (state === "late" || state === "partial") count++;
  }
  for (const period of cyclePeriods(row)) {
    const state = cycleCellState(row, period);
    if (state === "late" || state === "partial") count++;
  }
  return count;
}

const normalize = (value: string) => value.toLocaleLowerCase("tr-TR");

export function matchesQuery(row: BoardRow, query: string) {
  const q = normalize(query.trim());
  if (!q) return true;
  return normalize(`${row.studentName} ${row.teacherName} ${row.guardianNames} ${row.instrumentName}`).includes(q);
}

export function byStudentName(a: BoardRow, b: BoardRow) {
  return a.studentName.localeCompare(b.studentName, "tr-TR") || a.instrumentName.localeCompare(b.instrumentName, "tr-TR");
}

export function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).map((part) => part[0]).join("").slice(0, 2).toLocaleUpperCase("tr-TR");
}
