"use client";

import { useState } from "react";
import { AdminGate, PageHeader, Segmented } from "@/components/ui";
import { DuesBoard } from "./dues-board";
import { TuitionPolicySection } from "./tuition-policy-section";

// İki iş, iki sekme (docs/10-decisions.md H17). "Aidatlar" günlük iş: kim ödedi, kim
// bekliyor, tahsilat. "Fiyat politikası" nadiren açılan ayar. Eski üçüncü sekme (Toplu ödeme)
// kalktı: ödeme penceresinde birden fazla ay seçmek zaten toplu ödeme.
type BillingView = "dues" | "pricing";

export default function BillingPage() {
  return <AdminGate><BillingPageContent /></AdminGate>;
}

// Aidatlar tamamen Admin'e özel (docs/04-permissions.md) - AdminGate sayfayı bir
// öğretmen doğrudan adres yazsa bile açmaz, /dashboard'a geri yönlendirir.
function BillingPageContent() {
  const [view, setView] = useState<BillingView>("dues");

  return (
    <div className="space-y-3">
      <PageHeader
        title="Aidatlar"
        actions={
          <Segmented
            label="Aidat görünümü"
            options={[
              { value: "dues", label: "Aidatlar", icon: "wallet" },
              { value: "pricing", label: "Fiyat politikası", icon: "settings" },
            ]}
            value={view}
            onChange={setView}
          />
        }
      />
      {view === "dues" && <DuesBoard />}
      {view === "pricing" && <TuitionPolicySection />}
    </div>
  );
}
