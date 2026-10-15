"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { Badge, ConfirmDialog, EmptyState, FormMessage, LoadError, PageHeader, Panel, ProgressBar, Skeleton } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { formatDateTime } from "@/lib/format";
import { useReleases } from "@/lib/releases";
import { ROLLOUT_STATUS_LABEL, WAVE_LABEL, useHaltRollout, useRollouts, useStartRollout, type Rollout } from "@/lib/rollouts";

const STATUS_TONE: Record<string, "info" | "warning" | "success" | "danger"> = { Running: "info", Paused: "warning", Completed: "success", Halted: "danger" };

// Filo güncellemesi (§7): kanarya → ilk %10 → kalanlar. Bir dalgada hata olursa dağıtım durur.
// Sürümü sabitlenmiş okullar dağıtıma katılmaz.
export default function RolloutsPage() {
  const rollouts = useRollouts();
  const releases = useReleases();
  const start = useStartRollout();
  const halt = useHaltRollout();
  const [version, setVersion] = useState("");
  const [halting, setHalting] = useState<Rollout | null>(null);
  const available = (releases.data ?? []).filter((release) => !release.isWithdrawn);
  const selected = version || available[0]?.version || "";
  const running = (rollouts.data ?? []).some((rollout) => rollout.status === "Running" || rollout.status === "Paused");

  function submit(event: FormEvent) {
    event.preventDefault();
    if (selected) start.mutate(selected);
  }

  return (
    <div className="grid gap-5">
      <PageHeader title="Dağıtımlar" description="Yeni sürümü dalga dalga bütün okullara yayar." />

      <Panel title="Yeni dağıtım">
        <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
          <label className="form-label min-w-56 flex-1 sm:flex-none">
            Sürüm
            <select className="field font-mono" value={selected} onChange={(event) => setVersion(event.target.value)} disabled={available.length === 0}>
              {available.map((release) => <option key={release.version} value={release.version}>v{release.version} ({release.schoolCount} okulda)</option>)}
            </select>
          </label>
          <button type="submit" className="btn btn-primary" disabled={!selected || running || start.isPending}>
            <Icon name="rocket" className="h-4 w-4" />{start.isPending ? "Başlatılıyor…" : "Dağıtımı başlat"}
          </button>
          {running && <span className="text-meta">Süren bir dağıtım var; aynı anda tek dağıtım çalışır.</span>}
        </form>
        {start.error && <div className="mt-3"><FormMessage tone="error">{errorMessage(start.error)}</FormMessage></div>}
      </Panel>

      {rollouts.isLoading ? <Skeleton className="h-48" /> : rollouts.error ? <LoadError message={errorMessage(rollouts.error)} onRetry={() => rollouts.refetch()} /> : (rollouts.data ?? []).length === 0 ? (
        <Panel><EmptyState icon="rocket" title="Henüz dağıtım yok" /></Panel>
      ) : (
        <div className="grid gap-3">
          {(rollouts.data ?? []).map((rollout) => (
            <section key={rollout.id} className="app-card grid gap-3 p-4">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="flex flex-wrap items-center gap-2">
                  <span className="font-mono text-sm font-bold">v{rollout.version}</span>
                  <Badge tone={STATUS_TONE[rollout.status] ?? "info"}>{ROLLOUT_STATUS_LABEL[rollout.status] ?? rollout.status}</Badge>
                  <span className="text-meta">{formatDateTime(rollout.startedAt)}{rollout.finishedAt && ` → ${formatDateTime(rollout.finishedAt)}`}</span>
                </span>
                {rollout.status === "Running" && (
                  <button type="button" className="btn btn-quiet !text-[var(--danger-strong)]" onClick={() => { halt.reset(); setHalting(rollout); }}>
                    <Icon name="pause" className="h-4 w-4" />Durdur
                  </button>
                )}
              </div>
              {rollout.haltReason && <FormMessage tone="error">{rollout.haltReason}</FormMessage>}
              <div className="grid gap-3 sm:grid-cols-3">
                {rollout.waves.map((wave) => {
                  const current = rollout.status === "Running" && wave.wave === rollout.wave;
                  return (
                    <div key={wave.wave} className={`rounded-2xl border p-3 ${current ? "border-[var(--brand)]" : "border-[var(--line)]"}`}>
                      <p className="flex items-center justify-between gap-2 text-xs font-bold">
                        <span>{wave.wave + 1}. {WAVE_LABEL[wave.wave] ?? "Dalga"}</span>
                        {current && <span className="pulse-dot text-[var(--info-strong)]">sürüyor</span>}
                      </p>
                      <div className="mt-2"><ProgressBar value={wave.done} max={wave.schools} tone={wave.schools > 0 && wave.done === wave.schools ? "success" : "brand"} label={`Dalga ${wave.wave + 1}`} /></div>
                      <p className="text-meta mt-1">{wave.done} / {wave.schools} okul bu sürümde</p>
                    </div>
                  );
                })}
              </div>
              {rollout.waveCompletedAt && rollout.status === "Running" && <p className="text-meta">Dalga {formatDateTime(rollout.waveCompletedAt)} tamamlandı; bekleme süresi dolunca sonraki dalga başlar.</p>}
            </section>
          ))}
        </div>
      )}

      <ConfirmDialog
        open={halting !== null}
        title="Dağıtımı durdur"
        description={halting ? `v${halting.version}` : undefined}
        confirmLabel="Durdur"
        tone="danger"
        pending={halt.isPending}
        error={halt.error ? errorMessage(halt.error) : null}
        onClose={() => setHalting(null)}
        onConfirm={() => halting && halt.mutate(halting.id, { onSuccess: () => setHalting(null) })}
      >
        <p className="text-body">Yeni okul güncellemesi başlatılmaz; güncellenmiş okullar yeni sürümde kalır. Süren tekil güncellemeler kendi işlemlerinde biter.</p>
      </ConfirmDialog>
    </div>
  );
}
