"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { Icon } from "@/components/icons";
import { SchoolAvatar, StatusBadge } from "@/components/school-bits";
import { LoadError, PageHeader, ProgressBar, SearchInput, Segmented, Skeleton } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { formatAge, formatNumber, hoursSince } from "@/lib/format";
import { BACKUP_STALE_HOURS, RUN_KIND_LABEL, isRunOpen, useSchools, type SchoolTile } from "@/lib/schools";
import { useNow } from "@/lib/use-now";

type Filter = "all" | "Active" | "problem" | "Provisioning" | "Suspended" | "PendingDestroy";

// Okul "sorunlu" mu: kurulum hatası, telemetriye ulaşılamıyor, açık alarm var ya da son yedek
// 36 saatten eski (ARCHITECTURE.md §4.1). Kırmızı kenarlık ve "Sorunlu" filtresi bunu kullanır.
function problemsOf(school: SchoolTile, now: number): string[] {
  const problems: string[] = [];
  if (school.status === "Failed") problems.push("Kurulum/işlem hatası");
  if (school.status === "Active") {
    if (school.telemetry && !school.telemetry.reachable) problems.push("Telemetriye ulaşılamıyor");
    const age = hoursSince(school.telemetry?.lastBackupAt, now);
    if (school.telemetry?.reachable && (age === null || age > BACKUP_STALE_HOURS)) problems.push("Yedek eski");
  }
  if (school.openAlerts > 0) problems.push(`${school.openAlerts} açık alarm`);
  return problems;
}

// Ana ekran: okul kutucukları + her zaman en sonda "+" kutucuğu (filtrelense bile).
export default function SchoolsHomePage() {
  const { data, isLoading, error, refetch } = useSchools();
  const now = useNow();
  const [filter, setFilter] = useState<Filter>("all");
  const [search, setSearch] = useState("");

  const schools = useMemo(() => data ?? [], [data]);
  const counts = useMemo(() => ({
    all: schools.length,
    Active: schools.filter((s) => s.status === "Active").length,
    problem: schools.filter((s) => problemsOf(s, now).length > 0).length,
    Provisioning: schools.filter((s) => s.status === "Provisioning").length,
    Suspended: schools.filter((s) => s.status === "Suspended").length,
    PendingDestroy: schools.filter((s) => s.status === "PendingDestroy").length,
  }), [schools, now]);

  const visible = useMemo(() => {
    const query = search.trim().toLocaleLowerCase("tr-TR");
    return schools.filter((school) => {
      if (filter === "problem" ? problemsOf(school, now).length === 0 : filter !== "all" && school.status !== filter) return false;
      if (!query) return true;
      return [school.displayName, school.shortName, school.slug, school.primaryDomain, school.hostName ?? ""]
        .some((value) => value.toLocaleLowerCase("tr-TR").includes(query));
    });
  }, [schools, filter, search, now]);

  return (
    <div className="grid gap-5">
      <PageHeader
        title="Okullar"
        description="Kurulu okullar, durumları ve süren işlemler."
        actions={<Link href="/schools/new" className="btn btn-primary"><Icon name="plus" className="h-4 w-4" />Yeni okul</Link>}
      />

      <div className="flex flex-wrap items-center gap-2">
        <Segmented<Filter>
          label="Duruma göre süz"
          value={filter}
          onChange={setFilter}
          options={[
            { value: "all", label: `Tümü ${counts.all}` },
            { value: "Active", label: `Çalışıyor ${counts.Active}` },
            { value: "problem", label: `Sorunlu ${counts.problem}` },
            { value: "Provisioning", label: `Kuruluyor ${counts.Provisioning}` },
            { value: "Suspended", label: `Askıda ${counts.Suspended}` },
            { value: "PendingDestroy", label: `Silinecek ${counts.PendingDestroy}` },
          ]}
        />
        <SearchInput value={search} onChange={setSearch} label="Okul ara" placeholder="Ad, kısaltma, alan adı…" />
      </div>

      {error && <LoadError message={errorMessage(error)} onRetry={() => refetch()} />}

      <div className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(15rem,1fr))]">
        {isLoading && Array.from({ length: 3 }, (_, index) => <Skeleton key={index} className="h-48" />)}
        {visible.map((school) => <SchoolTileCard key={school.id} school={school} now={now} />)}
        <Link
          href="/schools/new"
          className="pressable grid min-h-48 place-items-center rounded-[1.1rem] border-2 border-dashed border-[var(--line-strong)] text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]"
        >
          <span className="grid justify-items-center gap-2">
            <Icon name="plus" className="h-8 w-8" />
            <span className="text-sm font-bold">Yeni okul</span>
          </span>
        </Link>
        <Link
          href="/schools/adopt"
          className="pressable grid min-h-48 place-items-center rounded-[1.1rem] border border-dashed border-[var(--line)] px-4 text-center text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]"
        >
          <span className="grid justify-items-center gap-2">
            <Icon name="upload" className="h-6 w-6" />
            <span className="text-sm font-bold">Var olan okulu taşı</span>
            <span className="text-[.72rem]">Çalışan bir okulu dökümüyle platforma al</span>
          </span>
        </Link>
      </div>

      {!isLoading && schools.length > 0 && visible.length === 0 && (
        <p className="text-meta">Bu süzgece uyan okul yok.</p>
      )}
    </div>
  );
}

function SchoolTileCard({ school, now }: { school: SchoolTile; now: number }) {
  const problems = problemsOf(school, now);
  const telemetry = school.telemetry;
  const backupAge = hoursSince(telemetry?.lastBackupAt, now);
  const backupStale = backupAge === null || backupAge > BACKUP_STALE_HOURS;
  const run = school.openRun;

  return (
    <Link
      href={`/schools/${school.id}`}
      className={`app-card pressable flex min-w-0 flex-col gap-3 p-4 hover:border-[var(--brand)] ${problems.length > 0 ? "!border-[var(--danger)] ring-1 ring-[var(--danger)]" : ""}`}
    >
      <div className="flex items-start justify-between gap-2">
        <SchoolAvatar name={school.shortName || school.displayName} color={school.primaryColor} />
        <div className="flex flex-wrap justify-end gap-1">
          {school.isCanary && <span className="rounded-full bg-[var(--warning-soft)] px-2 py-0.5 text-[.68rem] font-bold text-[var(--warning-strong)]">Kanarya</span>}
          <StatusBadge status={school.status} />
        </div>
      </div>

      <div className="min-w-0">
        <p className="truncate text-sm font-bold">{school.displayName}</p>
        <p className="text-meta truncate">{school.primaryDomain}</p>
      </div>

      {run ? (
        <div className="grid gap-1.5">
          <div className="flex items-center justify-between gap-2 text-xs font-bold">
            <span className={run.status === "Failed" ? "text-[var(--danger-strong)]" : "text-[var(--info-strong)]"}>
              {RUN_KIND_LABEL[run.kind]} · Adım {Math.min(run.currentStep + 1, run.totalSteps)}/{run.totalSteps}
            </span>
            {run.status === "Failed" && <span className="text-[var(--danger-strong)]">Başarısız</span>}
          </div>
          <ProgressBar
            value={run.currentStep}
            max={run.totalSteps}
            tone={run.status === "Failed" ? "danger" : "brand"}
            label={`${school.displayName} işlem ilerlemesi`}
          />
          <span className="text-meta truncate">{run.status === "Failed" ? run.error ?? run.currentStepName : isRunOpen(run.status) ? run.currentStepName ?? "Sırada" : ""}</span>
        </div>
      ) : (
        <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
          <dt className="text-[var(--muted)]">Öğrenci</dt>
          <dd className="text-right font-bold tabular-nums">{formatNumber(telemetry?.activeStudents)}</dd>
          <dt className="text-[var(--muted)]">Son yedek</dt>
          <dd className={`text-right font-bold ${school.status === "Active" && backupStale ? "text-[var(--danger-strong)]" : ""}`}>{formatAge(telemetry?.lastBackupAt, now)}</dd>
        </dl>
      )}

      <div className="mt-auto flex flex-wrap items-center justify-between gap-x-2 gap-y-1 border-t border-[var(--line)] pt-2 text-[.72rem] text-[var(--muted)]">
        <span className="truncate font-mono">{school.appVersion ? `v${school.appVersion}` : school.targetVersion ? `→ v${school.targetVersion}` : "—"}</span>
        <span className="flex items-center gap-1 truncate"><Icon name="server" className="h-3 w-3" />{school.hostName ?? "sunucu yok"}</span>
      </div>
      {problems.length > 0 && (
        <p className="flex items-center gap-1 text-[.72rem] font-bold text-[var(--danger-strong)]">
          <Icon name="alert-triangle" className="h-3.5 w-3.5 shrink-0" />
          <span className="truncate">{problems.join(" · ")}</span>
        </p>
      )}
    </Link>
  );
}
