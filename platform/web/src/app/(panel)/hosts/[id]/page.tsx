"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { Icon } from "@/components/icons";
import { Sparkline } from "@/components/sparkline";
import { Badge, EmptyState, LoadError, PageHeader, Panel, Segmented, Skeleton, StatStrip } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { formatAge, formatNumber } from "@/lib/format";
import { percent, useHostStats } from "@/lib/monitoring";
import { useNow } from "@/lib/use-now";

type Range = "6" | "24" | "168";

// Sunucu gözlemi: yük, bellek ve disk eğrisi (5 dakikada bir ölçüm, 7 gün saklanır) ve konteynerler.
export default function HostDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [range, setRange] = useState<Range>("24");
  const stats = useHostStats(id, Number(range));
  const now = useNow();
  const host = stats.data?.host;

  return (
    <div className="grid gap-5">
      <Link href="/hosts" className="text-meta inline-flex items-center gap-1 hover:underline"><Icon name="arrow-left" className="h-4 w-4" />Sunucular</Link>
      {stats.isLoading ? <Skeleton className="h-64" /> : stats.error ? <LoadError message={errorMessage(stats.error)} onRetry={() => stats.refetch()} /> : host && stats.data && (
        <>
          <PageHeader
            title={host.name}
            description={`Ölçüm ${formatAge(host.takenAt, now)} · 5 dakikada bir toplanır, 7 gün saklanır.`}
            actions={<Segmented label="Aralık" value={range} onChange={setRange} options={[{ value: "6", label: "6 sa" }, { value: "24", label: "24 sa" }, { value: "168", label: "7 gün" }]} />}
          />
          <StatStrip
            label="Son ölçüm"
            items={[
              { key: "state", label: "Durum", value: host.reachable ? "Ulaşılıyor" : "Ulaşılamıyor", tone: host.reachable ? "success" : "danger", hint: host.reachable ? undefined : host.error ?? undefined },
              { key: "load", label: "Yük (1 / 5 dk)", value: `${host.load1 ?? "—"} / ${host.load5 ?? "—"}`, hint: `${host.cpus ?? "—"} çekirdek` },
              { key: "mem", label: "Bellek", value: `%${percent(host.memTotalMb !== null && host.memAvailableMb !== null ? host.memTotalMb - host.memAvailableMb : null, host.memTotalMb) ?? "—"}`, hint: `${formatNumber(host.memAvailableMb)} MB boş` },
              { key: "disk", label: "Disk", value: `%${percent(host.diskTotalMb !== null && host.diskFreeMb !== null ? host.diskTotalMb - host.diskFreeMb : null, host.diskTotalMb) ?? "—"}`, hint: `${formatNumber(host.diskFreeMb !== null ? Math.round(host.diskFreeMb / 1024) : null)} GB boş` },
              { key: "uptime", label: "Açık", value: host.uptimeSeconds ? `${Math.floor(host.uptimeSeconds / 86400)} gün` : "—" },
            ]}
          />
          <div className="grid gap-3 md:grid-cols-3">
            <Panel title="Yük (1 dk)" meta={`${host.cpus ?? "?"} çekirdek`}><div className="text-[var(--info-strong)]"><Sparkline values={stats.data.points.map((p) => p.load1)} max={host.cpus ?? undefined} label="Yük eğrisi" /></div></Panel>
            <Panel title="Kullanılan bellek" meta={`${formatNumber(host.memTotalMb)} MB`}><div className="text-[var(--brand)]"><Sparkline values={stats.data.points.map((p) => p.memUsedMb)} max={host.memTotalMb ?? undefined} label="Bellek eğrisi" /></div></Panel>
            <Panel title="Kullanılan disk" meta={`${formatNumber(host.diskTotalMb !== null ? Math.round(host.diskTotalMb / 1024) : null)} GB`}><div className="text-[var(--warning-strong)]"><Sparkline values={stats.data.points.map((p) => p.diskUsedMb)} max={host.diskTotalMb ?? undefined} label="Disk eğrisi" /></div></Panel>
          </div>
          <Panel title="Konteynerler" meta={`${stats.data.containers.length} konteyner`} flush>
            {stats.data.containers.length === 0 ? <div className="p-4"><EmptyState icon="server" title="Konteyner yok" /></div> : (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[40rem] text-left text-sm">
                  <thead className="text-[.72rem] uppercase tracking-wide text-[var(--muted)]">
                    <tr className="border-b border-[var(--line)]">{["Konteyner", "Okul", "Durum", "Bellek", "CPU", "Yeniden başlama"].map((label) => <th key={label} className="px-3 py-2 font-bold">{label}</th>)}</tr>
                  </thead>
                  <tbody>
                    {stats.data.containers.map((container) => (
                      <tr key={container.name} className="border-b border-[var(--line)] last:border-0">
                        <td className="px-3 py-2 font-mono text-xs">{container.name}</td>
                        <td className="px-3 py-2">{container.school ?? <span className="text-[var(--muted)]">altyapı</span>}</td>
                        <td className="px-3 py-2"><Badge tone={container.status !== "running" || container.health === "unhealthy" ? "danger" : "success"}>{container.health ?? container.status}</Badge></td>
                        <td className="px-3 py-2 tabular-nums">{container.memMb === null ? "—" : `${Math.round(container.memMb)} MB`}{/* Sınırı olmayan konteynerde docker sunucunun tüm belleğini yazar; o durumda sınır gösterilmez. */}
                          {container.memLimitMb && container.memLimitMb < (host.memTotalMb ?? Number.POSITIVE_INFINITY) * 0.95 ? <span className="text-[var(--muted)]"> / {Math.round(container.memLimitMb)}</span> : null}</td>
                        <td className="px-3 py-2 tabular-nums">{container.cpuPercent === null ? "—" : `%${container.cpuPercent}`}</td>
                        <td className={`px-3 py-2 tabular-nums ${container.restarts > 0 ? "font-bold text-[var(--warning-strong)]" : ""}`}>{container.restarts}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Panel>
        </>
      )}
    </div>
  );
}
