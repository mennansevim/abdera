import type { BoardRow } from "@/lib/billing";
import { currentPeriod } from "@/lib/billing-format";

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

// Kayıt ayından itibaren her ay borçtur; kayıttan önceki ve biten kaydın sonrası değildir
// (lib/billing-format isObligatedPeriod ile aynı kural).
export function cellState(row: BoardRow, period: string, today = currentPeriod()): CellState {
  if (period < row.startedAt.slice(0, 7)) return "none";
  if (row.endedAt && period > row.endedAt.slice(0, 7)) return "none";
  const cell = row.cells.find((item) => item.period === period);
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

export function lateCount(row: BoardRow, year: number, today = currentPeriod()) {
  let count = 0;
  for (let index = 0; index < 12; index++) {
    const state = cellState(row, periodOf(year, index), today);
    if (state === "late" || state === "partial") count++;
  }
  return count;
}

export function hasDebt(row: BoardRow, year: number, today = currentPeriod()) {
  for (let index = 0; index < 12; index++) {
    if (isOwed(cellState(row, periodOf(year, index), today))) return true;
  }
  return false;
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
