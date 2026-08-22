"use client";

import { PriceListsSection } from "./price-lists-section";
import { StudentBillingSection } from "./student-billing-section";

// docs/04-permissions.md: aidat/tahsilat tamamen Admin - bu sayfa layout içindeki
// AppHeader'dan Teacher'a hiç gösterilmiyor (bkz. app-header.tsx ADMIN_ONLY_LINKS).
export default function BillingPage() {
  return (
    <div className="space-y-8">
      <h1 className="text-display font-serif italic">Aidat ve Fiyatlandırma</h1>
      <PriceListsSection />
      <StudentBillingSection />
    </div>
  );
}
