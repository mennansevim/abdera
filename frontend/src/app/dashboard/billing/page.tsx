"use client";

import { useMemo, useState } from "react";
import { Icon, type IconName } from "@/components/icons";
import { AdminGate, PageHeader } from "@/components/ui";
import { useReceivables } from "@/lib/billing";
import { BulkPaymentSection } from "./bulk-payment-section";
import { TuitionPolicySection } from "./tuition-policy-section";
import { DuesListSection, type BillingFilterSummary } from "./dues-list-section";

// Üç ayrı iş, üç ayrı sekme. Karışıklığın kaynağı bunların tek ekranda iç içe olmasıydı:
// "normal aidat" (aylık borç + tahsilat), "toplu ödeme" (birkaç ayın peşin tahsilatı) ve
// "fiyat politikası" (tarife + indirim kuralları) birbirinin yerine geçmiyor.
type BillingView = "collections" | "bulk" | "pricing";

function money(value: number) {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", maximumFractionDigits: 0 }).format(value);
}

export default function BillingPage() {
  return <AdminGate><BillingPageContent /></AdminGate>;
}

// Aidatlar tamamen Admin'e özel (docs/04-permissions.md) - AdminGate sayfayı bir
// öğretmen doğrudan adres yazsa bile açmaz, /dashboard'a geri yönlendirir.
function BillingPageContent() {
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
    <div className="space-y-4">
      <PageHeader
        title="Aidat yönetimi"
        description="Aylık aidatları takip et, birkaç ayı tek seferde tahsil et, fiyat ve indirim kurallarını yönet."
        actions={
          <div className="grid w-full grid-cols-3 gap-1 rounded-xl border border-[var(--line)] bg-white p-1 sm:inline-flex sm:w-auto" role="group" aria-label="Aidat görünümü">
            <ViewButton active={view === "collections"} onClick={() => setView("collections")} icon="wallet">Aylık aidatlar</ViewButton>
            <ViewButton active={view === "bulk"} onClick={() => setView("bulk")} icon="check">Toplu ödeme</ViewButton>
            <ViewButton active={view === "pricing"} onClick={() => setView("pricing")} icon="settings">Fiyat politikası</ViewButton>
          </div>
        }
      />

      {view === "collections" && (
        <>
          <section className="grid gap-3 sm:grid-cols-3" aria-label="Tahsilat özeti">
            <SummaryCard icon="wallet" label="Açık bakiye" value={money(summary.outstanding)} detail={`${summary.openCount} aidat bekliyor`} loading={isLoading} />
            <SummaryCard icon="bell" label="Vadesi geçen" value={money(summary.overdue)} detail={`${summary.overdueCount} gecikmiş aidat`} loading={isLoading} tone="danger" />
            <SummaryCard icon="check" label="Tahsil edilen" value={money(summary.collected)} detail="Kaydedilen toplam ödeme" loading={isLoading} tone="success" />
          </section>
          <DuesListSection onSummaryChange={setFilteredSummary} />
        </>
      )}
      {view === "bulk" && <BulkPaymentSection />}
      {view === "pricing" && <TuitionPolicySection />}
    </div>
  );
}

// Üç sekme telefonda tek satıra sığmıyordu (360px'te sayfa 421px'e genişliyordu): mobilde
// eşit üç sütun, ikon üstte ve metin satır kırabilir; sm'den itibaren eski yatay şerit.
function ViewButton({ active, onClick, icon, children }: { active: boolean; onClick: () => void; icon: IconName; children: React.ReactNode }) {
  return <button type="button" onClick={onClick} aria-pressed={active} className={`btn min-w-0 flex-col gap-1 whitespace-normal px-1.5 py-1.5 text-center leading-tight sm:flex-row sm:gap-[.4rem] sm:whitespace-nowrap sm:px-[.9rem] sm:py-0 ${active ? "btn-primary" : "text-[var(--muted)] hover:bg-[var(--surface-muted)]"}`}><Icon name={icon} className="h-4 w-4 shrink-0" />{children}</button>;
}

function SummaryCard({ icon, label, value, detail, loading, tone = "brand" }: { icon: IconName; label: string; value: string; detail: string; loading: boolean; tone?: "brand" | "success" | "danger" | "warning" }) {
  const colors = { brand: "bg-[var(--brand-soft)] text-[var(--brand-strong)]", success: "bg-[var(--success-soft)] text-[var(--success-strong)]", danger: "bg-[var(--danger-soft)] text-[var(--danger-strong)]", warning: "bg-[var(--warning-soft)] text-[var(--warning-strong)]" }[tone];
  return <article className="app-card min-w-0 p-4"><div className="flex items-start gap-3"><span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl ${colors}`}><Icon name={icon} className="h-4 w-4" /></span><div className="min-w-0"><p className="text-[.75rem] font-bold text-[var(--muted)]">{label}</p>{loading ? <span className="skeleton mt-2 block h-6 w-24 rounded-md" /> : <p className="mt-1 truncate text-lg font-bold tabular-nums tracking-[-.02em]">{value}</p>}<p className="mt-1 truncate text-[.75rem] text-[var(--muted)]">{detail}</p></div></div></article>;
}
