"use client";

import { useState } from "react";
import { AuditTable } from "@/components/audit-table";
import { LoadError, PageHeader, Segmented, Skeleton } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { useAudit } from "@/lib/audit";

// Platform denetim kaydı (salt okunur; kayıtlar silinmez).
export default function AuditPage() {
  const [take, setTake] = useState<"100" | "250" | "500">("100");
  const audit = useAudit({ take: Number(take) });
  return (
    <div className="grid gap-5">
      <PageHeader
        title="Denetim"
        description="Operatör ve sistem eylemlerinin kaydı. Sır değerleri hiçbir zaman yazılmaz."
        actions={<Segmented label="Kayıt sayısı" value={take} onChange={setTake} options={[{ value: "100", label: "Son 100" }, { value: "250", label: "250" }, { value: "500", label: "500" }]} />}
      />
      {audit.isLoading ? <Skeleton className="h-64" /> : audit.error ? <LoadError message={errorMessage(audit.error)} onRetry={() => audit.refetch()} /> : <AuditTable entries={audit.data ?? []} />}
    </div>
  );
}
