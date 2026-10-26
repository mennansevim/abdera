"use client";

import { openDueLines } from "@/lib/assistant";
import { formatMoney } from "@/lib/billing-format";
import { useBillingDues } from "@/lib/billing";
import { usePendingLessonNotes } from "@/lib/progress";
import { useAssistant } from "./assistant-provider";

// Panel açılınca bugünün kısa özeti. Yalnızca sayılar ve o işe giden kısayol; ayrıntı kartta.
// Yönetici: aidat günü gelenler. Öğretmen: notu yazılmamış dersler (zildeki sayıyla aynı liste).
export function WelcomeSummary({ isAdmin }: { isAdmin: boolean }) {
  const { ask } = useAssistant();
  const dues = useBillingDues({ enabled: isAdmin });
  const notes = usePendingLessonNotes({ enabled: !isAdmin });

  if (isAdmin) {
    if (!dues.data) return null;
    const lines = openDueLines(dues.data, new Date());
    const overdue = lines.filter((line) => line.bucket === "overdue");
    const upcoming = lines.length - overdue.length;
    if (lines.length === 0) return <p className="text-meta text-xs">Önümüzdeki 7 günde vadesi gelen aidat yok.</p>;
    const overdueTotal = overdue.reduce((total, line) => total + line.remaining, 0);
    return (
      <button type="button" onClick={() => ask("Aidat günü gelenleri listele")} className="pressable flex w-full items-center justify-between gap-3 rounded-xl border border-[var(--line)] bg-white px-3 py-2 text-left text-xs">
        <span>
          {overdue.length > 0 && <><b className="tabular-nums">{overdue.length}</b> aidatın vadesi geçti ({formatMoney(overdueTotal, overdue[0]!.due.currency)}){upcoming > 0 ? ", " : ""}</>}
          {upcoming > 0 && <><b className="tabular-nums">{upcoming}</b> aidat 7 gün içinde</>}
        </span>
        <span className="shrink-0 font-bold text-[var(--brand-strong)]">Listele</span>
      </button>
    );
  }

  const count = notes.data?.items.length ?? 0;
  if (count === 0) return null;
  return <p className="rounded-xl border border-[var(--line)] bg-white px-3 py-2 text-xs"><b className="tabular-nums">{count}</b> dersin notu henüz yazılmadı.</p>;
}
