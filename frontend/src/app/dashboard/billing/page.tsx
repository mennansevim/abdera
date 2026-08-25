"use client";

import { useMemo, useState } from "react";
import { Icon, type IconName } from "@/components/icons";
import { useReceivables } from "@/lib/billing";
import { PriceListsSection } from "./price-lists-section";
import { DuesListSection, type BillingFilterSummary } from "./dues-list-section";

type BillingView = "collections" | "pricing";

function money(value: number) {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", maximumFractionDigits: 0 }).format(value);
}

export default function BillingPage() {
  const [view, setView] = useState<BillingView>("collections");
  const { data: receivables, isLoading } = useReceivables();
  const baseSummary = useMemo(() => {
    const rows = receivables ?? [];
    const collected = rows.reduce((total, item) => total + item.totalPaid, 0);
    const outstanding = rows.filter((item) => item.status === "Unpaid" || item.status === "Partial" || item.status === "Overdue").reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0);
    const overdue = rows.filter((item) => item.status === "Overdue").reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0);
    const openCount = rows.filter((item) => item.status === "Unpaid" || item.status === "Partial" || item.status === "Overdue").length;
    const overdueCount = rows.filter((item) => item.status === "Overdue").length;
    return { outstanding, collected, overdue, openCount, overdueCount };
  }, [receivables]);
  const [filteredSummary, setFilteredSummary] = useState<BillingFilterSummary | null>(null);
  const summary = filteredSummary ?? baseSummary;

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="text-micro text-[var(--brand-strong)]">Finans</p>
          <h1 className="text-display mt-1 font-serif italic">Aidat yönetimi</h1>
          <p className="text-meta mt-2 max-w-2xl">Tahsilat durumunu bir bakışta gör, öğrenci hesabına in ve ödemeyi aynı ekrandan kaydet.</p>
        </div>
        <div className="inline-flex rounded-2xl border border-[var(--line)] bg-white p-1 shadow-sm" aria-label="Aidat görünümü">
          <ViewButton active={view === "collections"} onClick={() => setView("collections")} icon="wallet">Tahsilatlar</ViewButton>
          <ViewButton active={view === "pricing"} onClick={() => setView("pricing")} icon="settings">Fiyat politikası</ViewButton>
        </div>
      </header>

      {view === "collections" ? (
        <>
          <section className="grid gap-3 sm:grid-cols-3" aria-label="Tahsilat özeti">
            <SummaryCard icon="wallet" label="Açık bakiye" value={money(summary.outstanding)} detail={`${summary.openCount} aidat bekliyor`} loading={isLoading} />
            <SummaryCard icon="bell" label="Vadesi geçen" value={money(summary.overdue)} detail={`${summary.overdueCount} gecikmiş aidat`} loading={isLoading} tone="danger" />
            <SummaryCard icon="check" label="Tahsil edilen" value={money(summary.collected)} detail="Kaydedilen toplam ödeme" loading={isLoading} tone="success" />
          </section>
          <DuesListSection onSummaryChange={setFilteredSummary} />
        </>
      ) : <PriceListsSection />}
    </div>
  );
}

function ViewButton({ active, onClick, icon, children }: { active: boolean; onClick: () => void; icon: IconName; children: React.ReactNode }) {
  return <button type="button" onClick={onClick} aria-pressed={active} className={`pressable inline-flex min-h-10 items-center gap-2 rounded-xl px-3 text-xs font-bold ${active ? "bg-[var(--brand)] text-white shadow-sm" : "text-[var(--muted)] hover:bg-[var(--surface-muted)]"}`}><Icon name={icon} className="h-4 w-4" />{children}</button>;
}

function SummaryCard({ icon, label, value, detail, loading, tone = "brand" }: { icon: IconName; label: string; value: string; detail: string; loading: boolean; tone?: "brand" | "success" | "danger" | "warning" }) {
  const colors = { brand: "bg-[var(--brand-soft)] text-[var(--brand-strong)]", success: "bg-[var(--success-soft)] text-[var(--success-strong)]", danger: "bg-[var(--danger-soft)] text-[var(--danger-strong)]", warning: "bg-[var(--warning-soft)] text-[var(--warning-strong)]" }[tone];
  return <article className="app-card min-w-0 p-3.5 sm:p-4"><div className="flex items-start gap-3"><span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl ${colors}`}><Icon name={icon} className="h-4 w-4" /></span><div className="min-w-0"><p className="text-[.68rem] font-bold text-[var(--muted)]">{label}</p>{loading ? <span className="skeleton mt-2 block h-6 w-24 rounded-md" /> : <p className="mt-1 truncate text-lg font-bold tabular-nums tracking-[-.02em]">{value}</p>}<p className="mt-1 truncate text-[.62rem] text-[var(--muted)]">{detail}</p></div></div></article>;
}
