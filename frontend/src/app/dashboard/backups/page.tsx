"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { AdminGate, PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useBackupRuns, useSystemHealth, useTriggerBackup, type BackgroundJobStatus, type BackupRunStatus } from "@/lib/ops";

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
  return <AdminGate><BackupsPageContent /></AdminGate>;
}

// Yedekleme kontrolü tamamen Admin'e özel (docs/04-permissions.md).
function BackupsPageContent() {
  const [page, setPage] = useState(1);
  const { data: health } = useSystemHealth();
  const { data: runs, isLoading } = useBackupRuns(page, 20);
  const trigger = useTriggerBackup();
  const [error, setError] = useState<string | null>(null);
  const [triggered, setTriggered] = useState(false);
  const totalPages = runs ? Math.max(1, Math.ceil(runs.totalCount / runs.pageSize)) : 1;
  const backupDisabled = health?.providers.backup === "Disabled";

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
    <div className="space-y-3">
      <PageHeader
        title="Yedekleme"
        description="Günlük şifreli veritabanı yedeklemesi ve sistem sağlık durumu."
        actions={health && <span className={`rounded-full px-3 py-1.5 text-xs font-bold ${HEALTH_CLASS[health.level]}`}>{HEALTH_LABELS[health.level]}</span>}
      />

      {health && health.level !== "Healthy" && (
        <section role="alert" className="app-card flex items-start gap-3 border-[var(--danger)]/30 bg-[var(--danger-soft)] px-4 py-3">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-white/60 text-[var(--danger-strong)]"><Icon name="shield" className="h-5 w-5" /></span>
          <div>
            <p className="text-sm font-bold text-[var(--danger-strong)]">{health.detail ?? "Sistemde bir sorun var."}</p>
            <p className="text-meta mt-1">Son kontrol: {new Date(health.lastCheckedAt).toLocaleString("tr-TR")}</p>
          </div>
        </section>
      )}

      <section className="app-card flex flex-wrap items-center justify-between gap-3 px-4 py-3">
        <div>
          <h2 className="text-title">Manuel yedekleme</h2>
          <p className="text-meta mt-1">{backupDisabled ? "Harici yedekleme sağlayıcısı henüz yapılandırılmadı." : "Otomatik günlük yedeklemeyi beklemeden şimdi bir yedek al."}</p>
        </div>
        <button type="button" onClick={triggerNow} disabled={trigger.isPending || backupDisabled} className="btn btn-primary">
          {backupDisabled ? "Yedekleme kapalı" : trigger.isPending ? "Başlatılıyor…" : "Şimdi yedek al"}
        </button>
      </section>
      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-sm font-medium text-[var(--danger-strong)]">{error}</p>}
      {triggered && <p role="status" className="rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-sm font-medium text-[var(--success-strong)]">Yedekleme başlatıldı, birkaç dakika içinde aşağıdaki listede görünecek.</p>}

      {health && health.jobs.length > 0 && <BackgroundJobsCard jobs={health.jobs} />}

      <section className="app-card overflow-x-auto">
        <table className="w-full min-w-[46rem] text-sm">
          <thead>
            <tr className="text-micro border-b border-[var(--line)] text-left">
              <th className="px-3 py-2">Başlangıç</th>
              <th className="px-3 py-2">Tür</th>
              <th className="px-3 py-2">Durum</th>
              <th className="px-3 py-2">Boyut</th>
              <th className="px-3 py-2">Hata</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-3 py-8 text-center text-sm text-[var(--muted)]">Yükleniyor…</td></tr>}
            {runs?.items.map((run) => (
              <tr key={run.id} className="border-b border-[var(--line)] last:border-0">
                <td className="text-meta px-3 py-2">{new Date(run.startedAt).toLocaleString("tr-TR")}</td>
                <td className="px-3 py-2">{run.triggeredManually ? "Manuel" : "Otomatik"}</td>
                <td className={`px-3 py-2 font-bold ${STATUS_CLASS[run.status]}`}>{STATUS_LABELS[run.status]}</td>
                <td className="text-meta px-3 py-2">{formatSize(run.sizeBytes)}</td>
                <td className="text-meta max-w-xs px-3 py-2">
                  {run.errorMessage
                    ? <details className="group"><summary className="line-clamp-2 cursor-pointer break-words group-open:line-clamp-none">{run.errorMessage}</summary></details>
                    : "—"}
                </td>
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

function formatInterval(minutes: number) {
  if (minutes >= 60 * 24) return "günde bir";
  if (minutes >= 60) return minutes === 60 ? "saatte bir" : `${minutes / 60} saatte bir`;
  return minutes === 1 ? "dakikada bir" : `${minutes} dakikada bir`;
}

// Aylık aidat üretimi gibi işler sessizce durursa burada görünür; eşik aşılınca sunucu ayrıca
// sağlık durumunu "Dikkat gerekiyor"a çekip e-posta atar.
function BackgroundJobsCard({ jobs }: { jobs: BackgroundJobStatus[] }) {
  return (
    <section className="app-card px-4 py-3">
      <h2 className="text-title">Arka plan işleri</h2>
      <p className="text-meta mt-1">Otomatik çalışan işlerin son turu. Bir iş üç turunu üst üste kaçırırsa uyarı e-postası gider.</p>
      <ul className="mt-2 divide-y divide-[var(--line)]">
        {jobs.map((job) => {
          const failed = job.lastFailedAt !== null && (job.lastSucceededAt === null || job.lastFailedAt > job.lastSucceededAt);
          return (
            <li key={job.name} className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5 py-2 text-sm">
              <span>
                <strong>{job.label}</strong>
                <span className="text-meta ml-1.5">{formatInterval(job.intervalMinutes)}</span>
              </span>
              {failed ? (
                <span className="font-bold text-[var(--danger-strong)]" title={job.lastError ?? undefined}>
                  Son tur hata verdi · {new Date(job.lastFailedAt!).toLocaleString("tr-TR")}
                </span>
              ) : (
                <span className="text-meta">
                  {job.lastSucceededAt ? `Son başarılı: ${new Date(job.lastSucceededAt).toLocaleString("tr-TR")}` : "Henüz çalışmadı"}
                </span>
              )}
              {failed && job.lastError && <p className="text-meta w-full break-words">{job.lastError}</p>}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
