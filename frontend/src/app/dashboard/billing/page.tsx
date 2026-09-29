"use client";

import { useMemo, useState } from "react";
import { AdminGate, PageHeader, Segmented, StatStrip } from "@/components/ui";
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
    <div className="space-y-3">
      <PageHeader
        title="Aidat yönetimi"
        description="Aylık aidatları takip et, birkaç ayı tek seferde tahsil et, fiyat ve indirim kurallarını yönet."
        actions={
          <Segmented
            label="Aidat görünümü"
            options={[
              { value: "collections", label: "Aylık aidatlar", icon: "wallet" },
              { value: "bulk", label: "Toplu ödeme", icon: "check" },
              { value: "pricing", label: "Fiyat politikası", icon: "settings" },
            ]}
            value={view}
            onChange={setView}
          />
        }
      />

      {view === "collections" && (
        <>
          <StatStrip
            label="Tahsilat özeti"
            items={[
              { key: "outstanding", label: "Açık bakiye", value: money(summary.outstanding), hint: `${summary.openCount} aidat bekliyor`, loading: isLoading },
              { key: "overdue", label: "Vadesi geçen", value: money(summary.overdue), hint: `${summary.overdueCount} gecikmiş aidat`, tone: summary.overdueCount ? "danger" : undefined, loading: isLoading },
              { key: "collected", label: "Tahsil edilen", value: money(summary.collected), hint: "Kaydedilen toplam ödeme", tone: "success", loading: isLoading },
            ]}
          />
          <DuesListSection onSummaryChange={setFilteredSummary} />
        </>
      )}
      {view === "bulk" && <BulkPaymentSection />}
      {view === "pricing" && <TuitionPolicySection />}
    </div>
  );
}
