"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useBackupRuns, useSystemHealth, useTriggerBackup, type BackupRunStatus } from "@/lib/ops";

const STATUS_LABELS: Record<BackupRunStatus, string> = { Running: "sürüyor", Succeeded: "başarılı", Failed: "başarısız" };
const STATUS_CLASS: Record<BackupRunStatus, string> = {
  Running: "text-[var(--warning-strong)]",
  Succeeded: "text-[var(--success-strong)]",
  Failed: "text-[var(--danger-strong)]",
};
const HEALTH_LABELS = { Healthy: "Sağlıklı", Degraded: "Dikkat gerekiyor", Unhealthy: "Sorunlu" };
const HEALTH_CLASS = {
  Healthy: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  Degraded: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  Unhealthy: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
};

function formatSize(bytes: number | null) {
  if (bytes === null) return "—";
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export default function BackupsPage() {
  const [page, setPage] = useState(1);
  const { data: health } = useSystemHealth();
  const { data: runs, isLoading } = useBackupRuns(page, 20);
  const trigger = useTriggerBackup();
  const [error, setError] = useState<string | null>(null);
  const [triggered, setTriggered] = useState(false);
  const totalPages = runs ? Math.max(1, Math.ceil(runs.totalCount / runs.pageSize)) : 1;

  async function triggerNow() {
    setError(null);
    setTriggered(false);
    try {
      await trigger.mutateAsync();
      setTriggered(true);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Yedekleme tetiklenemedi.");
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Yedekleme"
        description="Günlük şifreli veritabanı yedeklemesi ve sistem sağlık durumu."
        actions={health && <span className={`rounded-full px-3 py-1.5 text-xs font-bold ${HEALTH_CLASS[health.level]}`}>{HEALTH_LABELS[health.level]}</span>}
      />

      {health && health.level !== "Healthy" && (
        <section role="alert" className="app-card flex items-start gap-3 border-[var(--danger)]/30 bg-[var(--danger-soft)] p-4">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-white/60 text-[var(--danger-strong)]"><Icon name="shield" className="h-5 w-5" /></span>
          <div>
            <p className="text-sm font-bold text-[var(--danger-strong)]">{health.detail ?? "Sistemde bir sorun var."}</p>
            <p className="text-meta mt-1">Son kontrol: {new Date(health.lastCheckedAt).toLocaleString("tr-TR")}</p>
          </div>
        </section>
      )}

      <section className="app-card flex flex-wrap items-center justify-between gap-3 p-5">
        <div>
          <h2 className="text-title">Manuel yedekleme</h2>
          <p className="text-meta mt-1">Otomatik günlük yedeklemeyi beklemeden şimdi bir yedek al.</p>
        </div>
        <button type="button" onClick={triggerNow} disabled={trigger.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white disabled:opacity-50">
          {trigger.isPending ? "Başlatılıyor…" : "Şimdi yedek al"}
        </button>
      </section>
      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-sm font-medium text-[var(--danger-strong)]">{error}</p>}
      {triggered && <p role="status" className="rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-sm font-medium text-[var(--success-strong)]">Yedekleme başlatıldı, birkaç dakika içinde aşağıdaki listede görünecek.</p>}

      <section className="app-card overflow-x-auto">
        <table className="w-full min-w-[46rem] text-sm">
          <thead>
            <tr className="text-micro border-b border-[var(--line)] text-left">
              <th className="px-3 py-3">Başlangıç</th>
              <th className="px-3 py-3">Tür</th>
              <th className="px-3 py-3">Durum</th>
              <th className="px-3 py-3">Boyut</th>
              <th className="px-3 py-3">Hata</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-3 py-8 text-center text-sm text-[var(--muted)]">Yükleniyor…</td></tr>}
            {runs?.items.map((run) => (
              <tr key={run.id} className="border-b border-[var(--line)] last:border-0">
                <td className="text-meta px-3 py-3">{new Date(run.startedAt).toLocaleString("tr-TR")}</td>
                <td className="px-3 py-3">{run.triggeredManually ? "Manuel" : "Otomatik"}</td>
                <td className={`px-3 py-3 font-bold ${STATUS_CLASS[run.status]}`}>{STATUS_LABELS[run.status]}</td>
                <td className="text-meta px-3 py-3">{formatSize(run.sizeBytes)}</td>
                <td className="text-meta max-w-xs truncate px-3 py-3" title={run.errorMessage ?? undefined}>{run.errorMessage ?? "—"}</td>
              </tr>
            ))}
            {runs?.items.length === 0 && !isLoading && <tr><td colSpan={5} className="px-3 py-8 text-center text-sm text-[var(--muted)]">Henüz yedekleme kaydı yok.</td></tr>}
          </tbody>
        </table>
      </section>

      {runs && runs.totalCount > 0 && (
        <div className="flex items-center justify-between text-sm">
          <span className="text-meta">Toplam {runs.totalCount} kayıt · sayfa {runs.page} / {totalPages}</span>
          <div className="flex gap-2">
            <button type="button" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page <= 1} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold disabled:opacity-50">Önceki</button>
            <button type="button" onClick={() => setPage((current) => Math.min(totalPages, current + 1))} disabled={page >= totalPages} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold disabled:opacity-50">Sonraki</button>
          </div>
        </div>
      )}
    </div>
  );
}
