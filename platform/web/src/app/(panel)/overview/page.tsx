"use client";

import Link from "next/link";
import { Icon } from "@/components/icons";
import { Badge, EmptyState, LoadError, PageHeader, Panel, ProgressBar, Skeleton, StatStrip, type Tone } from "@/components/ui";
import { alertKindLabel, alertTone } from "@/lib/alerts";
import { errorMessage } from "@/lib/api";
import { formatAge, formatBytes, formatMoney, formatNumber, hoursSince } from "@/lib/format";
import { percent, useOverview, type FleetIssue, type HostRow, type SchoolRow, type Severity } from "@/lib/monitoring";
import { BACKUP_STALE_HOURS } from "@/lib/schools";
import { useNow } from "@/lib/use-now";

const SEVERITY_TONE: Record<Severity, Tone> = { ok: "success", warning: "warning", critical: "danger" };
const SEVERITY_LABEL: Record<Severity, string> = { ok: "Sorunsuz", warning: "Bakılmalı", critical: "Kritik" };

// Gözlem paneli: bütün okullar ve sunucular tek ekranda (platform/api Features/Monitoring.cs). Sorunun ne
// sayıldığı sunucudadır (FleetHealth); burada yalnızca sıralanıp gösterilir. Dakikada bir tazelenir.
export default function OverviewPage() {
  const overview = useOverview();
  const now = useNow();
  const data = overview.data;

  return (
    <div className="grid gap-5">
      <PageHeader
        title="Genel bakış"
        description={data ? `Bütün okullar ve sunucular · ${formatAge(data.generatedAt, now)} güncellendi, dakikada bir tazelenir.` : "Bütün okullar ve sunucular tek ekranda."}
        actions={<button type="button" className="btn" onClick={() => overview.refetch()} disabled={overview.isFetching}><Icon name="refresh" className="h-4 w-4" />Yenile</button>}
      />
      {overview.isLoading ? <Skeleton className="h-64" /> : overview.error ? <LoadError message={errorMessage(overview.error)} onRetry={() => overview.refetch()} /> : data && (
        <>
          <StatStrip
            label="Filo"
            items={[
              { key: "schools", label: "Okul", value: `${data.counts.active} / ${data.counts.schools}`, hint: "çalışan / toplam" },
              { key: "problems", label: "Sorunlu okul", value: formatNumber(data.counts.withProblems), tone: data.counts.withProblems > 0 ? "danger" : "success" },
              { key: "hosts", label: "Sunucu", value: `${data.counts.hosts - data.counts.hostsWithProblems} / ${data.counts.hosts}`, hint: "sorunsuz / toplam", tone: data.counts.hostsWithProblems > 0 ? "warning" : undefined },
              { key: "errors", label: "Hata (24 sa)", value: formatNumber(data.totals.errors24h), tone: data.totals.errors24h > 0 ? "warning" : undefined, hint: `${formatNumber(data.totals.warnings24h)} uyarı` },
              { key: "students", label: "Öğrenci", value: formatNumber(data.totals.students), hint: `${formatNumber(data.totals.staffUsers)} personel hesabı` },
              { key: "db", label: "Veritabanları", value: formatBytes(data.totals.databaseBytes) },
              { key: "yearly", label: "Yıllık lisans", value: formatMoney(data.totals.yearlyTotal) },
            ]}
          />

          <IssuesPanel issues={data.issues} />

          <Panel title="Sunucular" meta={`${data.hosts.length} sunucu`}>
            {data.hosts.length === 0 ? <EmptyState icon="server" title="Sunucu yok" /> : (
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">{data.hosts.map((host) => <HostCard key={host.id} host={host} now={now} />)}</div>
            )}
          </Panel>

          <Panel title="Okullar" meta="Son telemetri ve sunucu ölçümü" flush>
            {data.schools.length === 0 ? <div className="p-4"><EmptyState icon="home" title="Okul yok" /></div> : <SchoolTable schools={data.schools} now={now} />}
          </Panel>

          <Panel title="Onaylanmamış alarmlar" meta={<Link href="/alerts" className="underline">Tümü</Link>}>
            {data.recentAlerts.length === 0 ? <p className="text-meta">Açık alarm yok.</p> : (
              <ul className="grid gap-2">
                {data.recentAlerts.map((alert) => (
                  <li key={alert.id} className="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm">
                    <Badge tone={alertTone(alert.level)}>{alertKindLabel(alert.kind)}</Badge>
                    <Link href={`/schools/${alert.schoolId}`} className="font-bold hover:underline">{alert.slug}</Link>
                    <span className="min-w-0 flex-1 truncate text-[var(--muted)]">{alert.detail}</span>
                    <span className="text-meta">{formatAge(alert.receivedAt, now)}</span>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </>
      )}
    </div>
  );
}

function IssuesPanel({ issues }: { issues: FleetIssue[] }) {
  const critical = issues.filter((issue) => issue.severity === "Critical").length;
  return (
    <Panel title="Sorunlar" meta={issues.length === 0 ? "Yok" : `${critical} kritik · ${issues.length - critical} uyarı`}>
      {issues.length === 0 ? <EmptyState icon="check" title="Her şey yolunda" description="Bütün okullar ulaşılabilir, yedekler güncel, sunucularda yer var." /> : (
        <ul className="grid gap-2">
          {issues.map((issue, index) => (
            <li key={`${issue.kind}-${issue.schoolId ?? issue.hostId}-${index}`} className="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm">
              <Badge tone={issue.severity === "Critical" ? "danger" : "warning"}>{issue.severity === "Critical" ? "Kritik" : "Uyarı"}</Badge>
              <Link href={issue.schoolId ? `/schools/${issue.schoolId}` : `/hosts/${issue.hostId}`} className="font-bold hover:underline">{issue.subject}</Link>
              <span className="min-w-0 flex-1 text-[var(--muted)]">{issue.detail}</span>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

function HostCard({ host, now }: { host: HostRow; now: number }) {
  const memUsed = host.memTotalMb !== null && host.memAvailableMb !== null ? host.memTotalMb - host.memAvailableMb : null;
  const diskUsed = host.diskTotalMb !== null && host.diskFreeMb !== null ? host.diskTotalMb - host.diskFreeMb : null;
  const memPercent = percent(memUsed, host.memTotalMb);
  const diskPercent = percent(diskUsed, host.diskTotalMb);
  return (
    <Link href={`/hosts/${host.id}`} className="app-card pressable grid gap-3 p-4">
      <div className="flex items-start justify-between gap-2">
        <p className="flex items-center gap-2 text-sm font-bold"><Icon name="server" className="h-4 w-4" />{host.name}</p>
        <Badge tone={!host.reachable ? "danger" : "success"}>{host.reachable ? "Ulaşılıyor" : "Ulaşılamıyor"}</Badge>
      </div>
      {host.reachable ? (
        <>
          <Meter label="Bellek" value={memPercent} detail={`${formatNumber(memUsed)} / ${formatNumber(host.memTotalMb)} MB`} />
          <Meter label="Disk" value={diskPercent} detail={`${formatNumber(diskUsed !== null ? Math.round(diskUsed / 1024) : null)} / ${formatNumber(host.diskTotalMb !== null ? Math.round(host.diskTotalMb / 1024) : null)} GB`} />
          <dl className="grid grid-cols-3 gap-2 text-xs">
            <div><dt className="text-[var(--muted)]">Yük (1 dk)</dt><dd className="font-bold tabular-nums">{host.load1 ?? "—"} / {host.cpus ?? "—"} çekirdek</dd></div>
            <div><dt className="text-[var(--muted)]">Okul</dt><dd className="font-bold tabular-nums">{host.schools} · {host.containers} konteyner</dd></div>
            <div><dt className="text-[var(--muted)]">Yeniden başlama</dt><dd className={`font-bold tabular-nums ${host.restarts > 0 ? "text-[var(--warning-strong)]" : ""}`}>{host.restarts}</dd></div>
          </dl>
        </>
      ) : <p className="text-meta">{host.error ?? "Ölçüm yok."}</p>}
      <p className="text-meta">Ölçüm {formatAge(host.takenAt, now)}{host.uptimeSeconds ? ` · ${Math.floor(host.uptimeSeconds / 86400)} gündür açık` : ""}</p>
    </Link>
  );
}

function Meter({ label, value, detail }: { label: string; value: number | null; detail: string }) {
  return (
    <div>
      <div className="mb-1 flex justify-between text-xs"><span className="text-[var(--muted)]">{label}</span><span className="font-bold tabular-nums">{value === null ? "—" : `%${value}`} · {detail}</span></div>
      <ProgressBar value={value ?? 0} max={100} tone={value !== null && value > 90 ? "danger" : value !== null && value > 80 ? "warning" : "brand"} label={`${label} kullanımı`} />
    </div>
  );
}

function SchoolTable({ schools, now }: { schools: SchoolRow[]; now: number }) {
  const sorted = [...schools].sort((a, b) => rank(b.severity) - rank(a.severity) || a.displayName.localeCompare(b.displayName, "tr"));
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[56rem] text-left text-sm">
        <thead className="text-[.72rem] uppercase tracking-wide text-[var(--muted)]">
          <tr className="border-b border-[var(--line)]">
            {["Okul", "Durum", "Sağlık", "Hata / uyarı (24 sa)", "DB bağlantı", "DB boyutu", "Son yedek", "Bellek", "Yeniden başlama", "Sürüm"].map((label) => <th key={label} className="px-3 py-2 font-bold">{label}</th>)}
          </tr>
        </thead>
        <tbody>
          {sorted.map((school) => {
            const backupAge = hoursSince(school.lastBackupAt, now);
            return (
              <tr key={school.id} className="border-b border-[var(--line)] last:border-0">
                <td className="px-3 py-2">
                  <Link href={`/schools/${school.id}`} className="font-bold hover:underline">{school.displayName}</Link>
                  <p className="text-meta">{school.slug} · {school.hostName ?? "sunucu yok"}</p>
                </td>
                <td className="px-3 py-2"><Badge tone={SEVERITY_TONE[school.severity]}>{SEVERITY_LABEL[school.severity]}</Badge></td>
                <td className="px-3 py-2">{school.reachable === false ? <span className="font-bold text-[var(--danger-strong)]">Ulaşılamıyor</span> : school.health ?? "—"}</td>
                <td className="px-3 py-2 tabular-nums"><span className={school.errors24h ? "font-bold text-[var(--danger-strong)]" : ""}>{formatNumber(school.errors24h)}</span> / {formatNumber(school.warnings24h)}</td>
                <td className="px-3 py-2 tabular-nums">{formatNumber(school.dbConnections)}{school.dbWaitingOnLocks ? <span className="text-[var(--warning-strong)]"> · {school.dbWaitingOnLocks} kilit</span> : null}</td>
                <td className="px-3 py-2 tabular-nums">{formatBytes(school.databaseSizeBytes)}</td>
                <td className={`px-3 py-2 ${school.status === "Active" && (backupAge === null || backupAge > BACKUP_STALE_HOURS) ? "font-bold text-[var(--danger-strong)]" : ""}`}>{formatAge(school.lastBackupAt, now)}</td>
                <td className="px-3 py-2 tabular-nums">{school.memMb === null ? "—" : `${Math.round(school.memMb)} MB`}</td>
                <td className={`px-3 py-2 tabular-nums ${school.restarts > 0 ? "font-bold text-[var(--warning-strong)]" : ""}`}>{school.restarts}</td>
                <td className="px-3 py-2 font-mono text-xs">{school.appVersion ? `v${school.appVersion}` : "—"}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function rank(severity: Severity): number {
  return severity === "critical" ? 2 : severity === "warning" ? 1 : 0;
}
