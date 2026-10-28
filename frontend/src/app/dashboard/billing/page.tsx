"use client";

import { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { AdminGate, Notice, PageHeader, Segmented } from "@/components/ui";
import { QuickCollect } from "./collect-sheet";
import { DuesBoard } from "./dues-board";
import { TuitionPolicySection } from "./tuition-policy-section";

// İki iş, iki sekme (docs/10-decisions.md H17). "Aidatlar" günlük iş: kim ödedi, kim
// bekliyor, tahsilat. "Fiyat politikası" nadiren açılan ayar. Eski üçüncü sekme (Toplu ödeme)
// kalktı: ödeme penceresinde birden fazla ay seçmek zaten toplu ödeme.
type BillingView = "dues" | "pricing";

export default function BillingPage() {
  // useSearchParams App Router'da bir Suspense sınırı ister.
  return <AdminGate><Suspense><BillingPageContent /></Suspense></AdminGate>;
}

// Aidatlar tamamen Admin'e özel (docs/04-permissions.md) - AdminGate sayfayı bir
// öğretmen doğrudan adres yazsa bile açmaz, /dashboard'a geri yönlendirir.
//
// Asistandan gelen bağlantılar (docs/10-decisions.md AE2): `?view=pricing` Fiyat politikası
// sekmesini, `?collect={enrollmentId}&period=..&name=..` o kaydın sade ödeme penceresini
// (QuickCollect) açar. Asistan para almaz; tahsilat yine bu ekranın tek ödeme yolundan geçer.
function BillingPageContent() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const [view, setView] = useState<BillingView>(() => (searchParams.get("view") === "pricing" ? "pricing" : "dues"));
  const [collect, setCollect] = useState(() => {
    const enrollmentId = searchParams.get("collect");
    const period = searchParams.get("period");
    return enrollmentId && period ? { enrollmentId, period, name: searchParams.get("name") ?? "Öğrenci" } : null;
  });
  const [collected, setCollected] = useState<string | null>(null);

  function closeCollect(message?: string) {
    setCollect(null);
    if (message) setCollected(message);
    router.replace("/dashboard/billing", { scroll: false });
  }

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
      {collected && <Notice onDismiss={() => setCollected(null)}>{collected}</Notice>}
      {view === "dues" && <DuesBoard />}
      {view === "pricing" && <TuitionPolicySection />}
      {collect && <QuickCollect
        enrollmentId={collect.enrollmentId}
        studentName={collect.name}
        periods={[collect.period]}
        onClose={() => closeCollect()}
        onCollected={(message) => closeCollect(message)}
      />}
    </div>
  );
}
