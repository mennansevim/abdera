"use client";

import Link from "next/link";
import { useState } from "react";
import { Badge, EmptyState, FormMessage, LoadError, PageHeader, Panel, Segmented, Skeleton } from "@/components/ui";
import { alertTone, useAcknowledgeAlert, useAlerts } from "@/lib/alerts";
import { errorMessage } from "@/lib/api";
import { formatDateTime } from "@/lib/format";

// Okullardan gelen alarmlar. Onaylanan alarm kapanır; okul kutucuğundaki kırmızı işaret kalkar.
export default function AlertsPage() {
  const [view, setView] = useState<"open" | "all">("open");
  const alerts = useAlerts(view === "open");
  const acknowledge = useAcknowledgeAlert();

  return (
    <div className="grid gap-5">
      <PageHeader
        title="Alarmlar"
        description="Okulların imzalı olarak bildirdiği sorunlar."
        actions={<Segmented label="Görünüm" value={view} onChange={setView} options={[{ value: "open", label: "Açık" }, { value: "all", label: "Tümü (son 200)" }]} />}
      />
      {acknowledge.error && <FormMessage tone="error">{errorMessage(acknowledge.error)}</FormMessage>}
      {alerts.isLoading ? <Skeleton className="h-48" /> : alerts.error ? <LoadError message={errorMessage(alerts.error)} onRetry={() => alerts.refetch()} /> : (
        <Panel flush>
          {(alerts.data ?? []).length === 0 ? (
            <EmptyState icon="bell" title={view === "open" ? "Açık alarm yok" : "Alarm yok"} />
          ) : (
            <ul className="divide-y divide-[var(--line)]">
              {(alerts.data ?? []).map((alert) => (
                <li key={alert.id} className="flex flex-wrap items-start gap-3 px-4 py-3">
                  <Badge tone={alertTone(alert.level)}>{alert.level}</Badge>
                  <div className="min-w-0 flex-1">
                    <p className="flex flex-wrap items-center gap-2 text-sm font-bold">
                      <Link href={`/schools/${alert.schoolId}`} className="font-mono text-[var(--brand-strong)] hover:underline">{alert.slug}</Link>
                      <span>{alert.kind}</span>
                    </p>
                    <p className="text-meta break-words">{alert.detail}</p>
                    <p className="text-[.72rem] text-[var(--muted)]">
                      Olay {formatDateTime(alert.occurredAt)} · alındı {formatDateTime(alert.receivedAt)}
                      {alert.acknowledgedAt && ` · onaylandı ${formatDateTime(alert.acknowledgedAt)}`}
                    </p>
                  </div>
                  {!alert.acknowledgedAt && (
                    <button type="button" className="btn btn-quiet" disabled={acknowledge.isPending && acknowledge.variables === alert.id} onClick={() => acknowledge.mutate(alert.id)}>Onayla</button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </Panel>
      )}
    </div>
  );
}
