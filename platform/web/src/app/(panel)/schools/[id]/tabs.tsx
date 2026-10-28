"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { IntegrationFields } from "@/components/integration-fields";
import { ModulePicker } from "@/components/module-picker";
import { AuditTable } from "@/components/audit-table";
import { Badge, ConfirmDialog, CopyField, EmptyState, FormMessage, LoadError, Panel, ProgressBar, Skeleton, type Tone } from "@/components/ui";
import { alertKindLabel, alertTone, useAcknowledgeAlert } from "@/lib/alerts";
import { errorMessage } from "@/lib/api";
import { useAudit } from "@/lib/audit";
import { formatAge, formatBytes, formatDateTime, formatDuration, formatNumber, hoursSince } from "@/lib/format";
import { optionalOnly } from "@/lib/modules";
import { useReleaseModules } from "@/lib/releases";
import {
  BACKUP_STALE_HOURS,
  RUN_KIND_LABEL,
  RUN_STATUS_LABEL,
  isRunOpen,
  useRevealAdminPassword,
  useSchoolModules,
  type Run,
  type RunStatus,
  type SchoolDetail,
} from "@/lib/schools";
import { useNow } from "@/lib/use-now";

const RUN_TONE: Record<RunStatus, Tone> = {
  Pending: "muted",
  Running: "info",
  Succeeded: "success",
  Failed: "danger",
  Cancelled: "muted",
};

// --- Genel ---

export function GeneralTab({ detail, release }: { detail: SchoolDetail; release?: React.ReactNode }) {
  const now = useNow();
  const telemetry = detail.school.telemetry;
  const backupAge = hoursSince(telemetry?.lastBackupAt, now);
  const reveal = useRevealAdminPassword(detail.school.id);
  const [confirmReveal, setConfirmReveal] = useState(false);
  const integrationSecrets = detail.secretNames.filter((name) => name.startsWith("integration:")).map((name) => name.slice("integration:".length));
  const unhealthy = !!telemetry && (!telemetry.reachable || (!!telemetry.health && telemetry.health !== "Healthy"));

  return (
    <div className="grid gap-5">
      {telemetry && (telemetry.error || telemetry.healthDetail) && (
        <FormMessage tone="error">{telemetry.error ?? telemetry.healthDetail}</FormMessage>
      )}
      {!telemetry && <Panel><p className="text-meta">Henüz telemetri yok. Okul kurulduktan sonra platform düzenli olarak sağlık ve sayıları okur.</p></Panel>}

      <div className="grid gap-5 lg:grid-cols-3">
        <Panel title="Okul" flush>
          <dl className="px-4 py-1 text-sm">
            <Row label="Adres"><a className="font-mono text-[var(--brand-strong)] hover:underline" href={`https://${detail.school.publicDomain ?? detail.school.primaryDomain}`} target="_blank" rel="noreferrer">https://{detail.school.publicDomain ?? detail.school.primaryDomain}</a></Row>
            <Row label="Kısaltma"><span className="font-mono">{detail.school.slug}</span></Row>
            <Row label="Kısa ad">{detail.school.shortName}</Row>
            <Row label="Oluşturulma">{formatDateTime(detail.createdAt)}</Row>
            <Row label="Uygulama sürümü"><span className="font-mono">{telemetry?.appVersion ?? detail.school.appVersion ?? "—"}</span></Row>
            <Row label="Entegrasyonlar">{integrationSecrets.length === 0 ? "Girilmedi" : <span className="font-mono text-xs">{integrationSecrets.join(", ")}</span>}</Row>
          </dl>
        </Panel>
        {telemetry && (
          <>
            <Panel title="Kullanım" flush>
              <dl className="px-4 py-1 text-sm">
                <Row label="Sağlık"><span className={`font-bold ${unhealthy ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>{telemetry.reachable ? telemetry.health ?? "—" : "Ulaşılamıyor"} · {formatAge(telemetry.takenAt, now)}</span></Row>
                <Row label="Aktif öğrenci">{formatNumber(telemetry.activeStudents)}</Row>
                <Row label="Aktif öğretmen">{formatNumber(telemetry.activeTeachers)}</Row>
                <Row label="Kullanıcı">{detail.school.package?.staffSeats ? `${formatNumber(telemetry.activeStaffUsers)} / ${formatNumber(detail.school.package.staffSeats)}` : formatNumber(telemetry.activeStaffUsers)}</Row>
                <Row label="Kurs kaydı">{formatNumber(telemetry.activeEnrollments)}</Row>
              </dl>
            </Panel>
            <Panel title="Yedek ve veritabanı" flush>
              <dl className="px-4 py-1 text-sm">
                <Row label="Son yedek"><span className={backupAge === null || backupAge > BACKUP_STALE_HOURS ? "font-bold text-[var(--danger-strong)]" : ""}>{formatAge(telemetry.lastBackupAt, now)}</span></Row>
                <Row label="Yedek zamanı">{formatDateTime(telemetry.lastBackupAt)}</Row>
                <Row label="Veritabanı">{formatBytes(telemetry.databaseSizeBytes)}</Row>
                <Row label="Bekleyen bildirim">{formatNumber(telemetry.pendingNotifications)}</Row>
                <Row label="Hatalı bildirim">{formatNumber(telemetry.failedNotifications)}</Row>
              </dl>
            </Panel>
          </>
        )}
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <Panel title="Okul yöneticisi" flush>
          <dl className="px-4 py-1 text-sm">
            <Row label="E-posta">{detail.adminEmail ?? "—"}</Row>
          </dl>
          <div className="grid gap-2 px-4 pb-4 pt-2">
            {reveal.data ? (
              <>
                <CopyField label="Geçici parola" value={reveal.data.temporaryPassword} />
                <p className="text-meta">Yöneticiye güvenli bir kanaldan ilet; ilk girişte değiştirmesi zorunlu. Kayıt denetim günlüğüne yazıldı.</p>
              </>
            ) : (
              <button type="button" className="btn btn-quiet justify-self-start" onClick={() => setConfirmReveal(true)}>
                <Icon name="eye" className="h-4 w-4" />Geçici parolayı göster
              </button>
            )}
            {reveal.error && !confirmReveal && <FormMessage tone="error">{errorMessage(reveal.error)}</FormMessage>}
          </div>
        </Panel>
        {release}
      </div>

      <ConfirmDialog
        open={confirmReveal}
        title="Geçici parolayı göster"
        description="Bu işlem denetim günlüğüne kaydedilir."
        confirmLabel="Göster"
        pending={reveal.isPending}
        error={reveal.error ? errorMessage(reveal.error) : null}
        onConfirm={() => reveal.mutate(undefined, { onSuccess: () => setConfirmReveal(false) })}
        onClose={() => setConfirmReveal(false)}
      >
        <p className="text-body">Okul yöneticisinin geçici parolası ekranda açık metin olarak görünecek. Etrafında kimse olmadığından emin ol.</p>
      </ConfirmDialog>
    </div>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 border-b border-[var(--line)] py-2.5 last:border-b-0">
      <dt className="text-[var(--muted)]">{label}</dt>
      <dd className="min-w-0 break-words text-right font-medium">{children}</dd>
    </div>
  );
}

// --- İşlemler ---

export function RunsTab({ detail }: { detail: SchoolDetail }) {
  if (detail.runs.length === 0) return <Panel><EmptyState icon="activity" title="Henüz işlem yok" /></Panel>;
  return (
    <div className="grid gap-3">
      {detail.runs.map((run, index) => <RunCard key={run.id} run={run} defaultOpen={index === 0 || isRunOpen(run.status) || run.status === "Failed"} />)}
    </div>
  );
}

function RunCard({ run, defaultOpen }: { run: Run; defaultOpen: boolean }) {
  const now = useNow(1_000);
  const [open, setOpen] = useState(defaultOpen);
  const runOpen = isRunOpen(run.status);
  const done = run.status === "Succeeded" ? run.plan.length : run.currentStep;

  return (
    <section className="app-card overflow-hidden">
      <button type="button" onClick={() => setOpen((value) => !value)} aria-expanded={open} className="flex w-full flex-wrap items-center justify-between gap-2 px-4 py-3 text-left hover:bg-[var(--surface-muted)]">
        <span className="flex min-w-0 flex-wrap items-center gap-2">
          <span className="text-sm font-bold">{RUN_KIND_LABEL[run.kind] ?? run.kind}</span>
          <Badge tone={RUN_TONE[run.status]}>{RUN_STATUS_LABEL[run.status]}</Badge>
          {run.targetVersion && <span className="font-mono text-xs text-[var(--muted)]">v{run.targetVersion}</span>}
        </span>
        <span className="text-meta">{formatDateTime(run.createdAt)} · {formatDuration(run.createdAt, run.finishedAt, now)}</span>
      </button>
      <div className="px-4 pb-3">
        <ProgressBar value={done} max={run.plan.length} tone={run.status === "Failed" ? "danger" : run.status === "Succeeded" ? "success" : "brand"} label="İşlem ilerlemesi" />
      </div>
      {open && (
        <div className="grid gap-2 border-t border-[var(--line)] px-4 py-3">
          {run.error && <FormMessage tone="error">{run.error}</FormMessage>}
          <ol className="grid gap-1.5">
            {run.plan.map((name, index) => {
              const attempts = run.steps.filter((step) => step.step === index);
              const last = attempts[attempts.length - 1];
              const state = last?.status === "Succeeded" ? "done" : last?.status === "Failed" ? "failed" : last?.status === "Running" ? "running" : index === run.currentStep && runOpen ? "next" : "todo";
              return (
                <li key={`${index}-${name}`} className="rounded-xl border border-[var(--line)]">
                  <div className="flex flex-wrap items-center gap-2 px-3 py-2">
                    <StepDot state={state} />
                    <span className={`min-w-0 flex-1 text-sm font-semibold ${state === "todo" ? "text-[var(--muted)]" : ""}`}>{index + 1}. {name}</span>
                    {attempts.length > 1 && <span className="text-[.72rem] text-[var(--muted)]">{attempts.length}. deneme</span>}
                    {last && <span className="text-[.72rem] tabular-nums text-[var(--muted)]">{formatDuration(last.startedAt, last.finishedAt, now)}</span>}
                  </div>
                  {last?.log && (
                    <details className="border-t border-[var(--line)] px-3 py-1.5" open={state === "failed"}>
                      <summary className="text-xs font-bold text-[var(--muted)]">Log</summary>
                      <pre className="mt-1 max-h-64 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-[var(--surface-muted)] p-2 font-mono text-[.72rem]">{last.log}</pre>
                    </details>
                  )}
                </li>
              );
            })}
          </ol>
        </div>
      )}
    </section>
  );
}

function StepDot({ state }: { state: "done" | "failed" | "running" | "next" | "todo" }) {
  if (state === "done") return <span className="grid h-5 w-5 place-items-center rounded-full bg-[var(--success)] text-white"><Icon name="check" className="h-3 w-3" /></span>;
  if (state === "failed") return <span className="grid h-5 w-5 place-items-center rounded-full bg-[var(--danger)] text-white"><Icon name="x" className="h-3 w-3" /></span>;
  if (state === "running" || state === "next") return <span className="pulse-dot grid h-5 w-5 place-items-center rounded-full bg-[var(--info)]" aria-label="çalışıyor" />;
  return <span className="h-5 w-5 rounded-full border-2 border-[var(--line-strong)]" />;
}

// --- Modüller ---

export function ModulesTab({ detail, onQueued }: { detail: SchoolDetail; onQueued: () => void }) {
  const version = detail.school.appVersion;
  const catalog = useReleaseModules(version);
  const save = useSchoolModules(detail.school.id);
  const [selected, setSelected] = useState<Set<string>>(() => new Set(detail.modules));
  const [integrations, setIntegrations] = useState<Record<string, string>>({});
  const [confirm, setConfirm] = useState(false);
  const editable = detail.school.status === "Active" && !detail.runs.some((run) => isRunOpen(run.status));
  const stored = new Set(detail.secretNames.filter((name) => name.startsWith("integration:")).map((name) => name.slice("integration:".length)));

  if (!version) return <Panel><p className="text-meta">Okul henüz kurulmadı; modüller kurulum bitince değiştirilebilir. Seçili: {detail.modules.join(", ") || "yalnızca çekirdek"}.</p></Panel>;
  if (catalog.isLoading) return <Skeleton className="h-64" />;
  if (catalog.error || !catalog.data) return <LoadError message={catalog.error ? errorMessage(catalog.error) : "Katalog okunamadı."} onRetry={() => catalog.refetch()} />;

  const nextModules = optionalOnly(selected, catalog.data).sort();
  const current = [...detail.modules].sort();
  const filled = Object.fromEntries(Object.entries(integrations).map(([key, value]) => [key, value.trim()]).filter(([, value]) => value));
  const changed = nextModules.join(",") !== current.join(",") || Object.keys(filled).length > 0;

  return (
    <div className="grid gap-4">
      {!editable && <FormMessage tone="info">Modüller yalnızca çalışan ve süren işlemi olmayan okulda değiştirilebilir.</FormMessage>}
      <Panel title="Modüller" meta={`v${version} kataloğu`}>
        <ModulePicker catalog={catalog.data} selected={selected} onChange={setSelected} disabled={!editable} />
      </Panel>
      <Panel title="Entegrasyonlar" meta="Yalnızca değiştirmek istediğin alanları doldur">
        <IntegrationFields modules={new Set([...selected, ...(catalog.data ?? []).filter((m) => m.isCore).map((m) => m.key)])} values={integrations} stored={stored} onChange={(key, value) => setIntegrations((currentValues) => ({ ...currentValues, [key]: value }))} />
      </Panel>
      <div className="flex flex-wrap items-center justify-end gap-2">
        <span className="text-meta">Kaydetmek okulun yapılandırmasını yeniden yazar; api/web ~30 sn yeniden başlar. Kapatılan modülün verisi silinmez.</span>
        <button type="button" className="btn btn-primary" disabled={!editable || !changed} onClick={() => { save.reset(); setConfirm(true); }}>Değişiklikleri uygula</button>
      </div>
      <ConfirmDialog
        open={confirm}
        title="Yapılandırmayı uygula"
        description={detail.school.displayName}
        confirmLabel="Uygula"
        pending={save.isPending}
        error={save.error ? errorMessage(save.error) : null}
        onClose={() => setConfirm(false)}
        onConfirm={() => save.mutate(
          { modules: nextModules, integrations: Object.keys(filled).length > 0 ? filled : null },
          { onSuccess: () => { setConfirm(false); setIntegrations({}); onQueued(); } },
        )}
      >
        <p className="text-body">Okul yaklaşık 30 saniye kesinti yaşayacak. Sağlık kontrolü düşerse ajan önceki yapılandırmaya döner.</p>
      </ConfirmDialog>
    </div>
  );
}

// --- Telemetri ---

export function TelemetryTab({ detail }: { detail: SchoolDetail }) {
  if (detail.telemetryHistory.length === 0) return <Panel><EmptyState icon="activity" title="Telemetri geçmişi yok" /></Panel>;
  return (
    <Panel title="Telemetri geçmişi" meta={`son ${detail.telemetryHistory.length} ölçüm`} flush>
      <div className="overflow-x-auto">
        <table className="data-table">
          <thead>
            <tr><th>Zaman</th><th>Sağlık</th><th>Sürüm</th><th>Öğrenci</th><th>Öğretmen</th><th>Kayıt</th><th>Son yedek</th><th>DB</th><th>Bildirim (bekl./hata)</th></tr>
          </thead>
          <tbody>
            {detail.telemetryHistory.map((item) => (
              <tr key={item.takenAt}>
                <td className="whitespace-nowrap">{formatDateTime(item.takenAt)}</td>
                <td>{item.reachable ? <Badge tone={item.health === "Healthy" ? "success" : "warning"}>{item.health ?? "—"}</Badge> : <Badge tone="danger" className="max-w-[16rem] truncate">{item.error ? `Ulaşılamadı: ${item.error}` : "Ulaşılamadı"}</Badge>}</td>
                <td className="font-mono text-xs">{item.appVersion ?? "—"}</td>
                <td className="tabular-nums">{formatNumber(item.activeStudents)}</td>
                <td className="tabular-nums">{formatNumber(item.activeTeachers)}</td>
                <td className="tabular-nums">{formatNumber(item.activeEnrollments)}</td>
                <td className="whitespace-nowrap">{formatDateTime(item.lastBackupAt)}</td>
                <td className="whitespace-nowrap">{formatBytes(item.databaseSizeBytes)}</td>
                <td className="tabular-nums">{formatNumber(item.pendingNotifications)} / {formatNumber(item.failedNotifications)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}

// --- Alarmlar ---

export function AlertsTab({ detail }: { detail: SchoolDetail }) {
  const acknowledge = useAcknowledgeAlert();
  if (detail.alerts.length === 0) return <Panel><EmptyState icon="bell" title="Alarm yok" description="Okul bir sorun bildirdiğinde burada görünür." /></Panel>;
  return (
    <Panel title="Alarmlar" meta="son 20" flush>
      {acknowledge.error && <div className="p-3"><FormMessage tone="error">{errorMessage(acknowledge.error)}</FormMessage></div>}
      <ul className="divide-y divide-[var(--line)]">
        {detail.alerts.map((alert) => (
          <li key={alert.id} className="flex flex-wrap items-start gap-3 px-4 py-3">
            <Badge tone={alertTone(alert.level)}>{alert.level}</Badge>
            <div className="min-w-0 flex-1">
              <p className="text-sm font-bold">{alertKindLabel(alert.kind)}</p>
              <p className="text-meta break-words">{alert.detail}</p>
              <p className="text-[.72rem] text-[var(--muted)]">{formatDateTime(alert.occurredAt)}{alert.acknowledgedAt && ` · onaylandı ${formatDateTime(alert.acknowledgedAt)}`}</p>
            </div>
            {!alert.acknowledgedAt && (
              <button type="button" className="btn btn-quiet" disabled={acknowledge.isPending} onClick={() => acknowledge.mutate(alert.id)}>Onayla</button>
            )}
          </li>
        ))}
      </ul>
    </Panel>
  );
}

// --- Denetim ---

export function AuditTab({ schoolId }: { schoolId: string }) {
  const audit = useAudit({ schoolId, take: 100 });
  if (audit.isLoading) return <Skeleton className="h-48" />;
  if (audit.error) return <LoadError message={errorMessage(audit.error)} onRetry={() => audit.refetch()} />;
  return <AuditTable entries={audit.data ?? []} showSchool={false} />;
}
