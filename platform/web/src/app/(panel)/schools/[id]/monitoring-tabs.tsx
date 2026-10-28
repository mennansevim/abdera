"use client";

import { useMemo, useState } from "react";
import { Icon } from "@/components/icons";
import { EmptyState, FormMessage, LoadError, Panel, SearchInput, Segmented, Skeleton } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { formatAge, formatBytes, formatDateTime } from "@/lib/format";
import { fetchSchoolLogs, useSchoolBackups, type LogService, type LogWindow } from "@/lib/monitoring";
import type { SchoolDetail } from "@/lib/schools";
import { useNow } from "@/lib/use-now";

const SERVICES: { value: LogService; label: string }[] = [
  { value: "api", label: "API" },
  { value: "web", label: "Web" },
  { value: "db", label: "Veritabanı" },
  { value: "backup-gw", label: "Yedek" },
];
const WINDOWS: { value: LogWindow; label: string }[] = [
  { value: "15m", label: "15 dk" },
  { value: "1h", label: "1 sa" },
  { value: "6h", label: "6 sa" },
  { value: "24h", label: "24 sa" },
];

type LevelFilter = "all" | "warn" | "error";

// Serilog satırları "[10:00:00 ERR]" biçiminde; Postgres "ERROR:"/"FATAL:", ASP.NET "fail:".
function levelOf(line: string): "error" | "warn" | null {
  if (/\b(ERR|FTL)\]|\bERROR:|\bFATAL:|\bfail:|\bcrit:/.test(line)) return "error";
  if (/\bWRN\]|\bWARNING:|\bwarn:/.test(line)) return "warn";
  return null;
}

// Okulun konteyner logu: istek anında ajandan okunur, platformda saklanmaz. Kişisel veri içerebileceği
// için otomatik çekilmez; her "Getir" denetim kaydına yazılır.
export function LogsTab({ detail }: { detail: SchoolDetail }) {
  const [service, setService] = useState<LogService>("api");
  const [since, setSince] = useState<LogWindow>("1h");
  const [text, setText] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [query, setQuery] = useState("");
  const [level, setLevel] = useState<LevelFilter>("all");
  const installed = detail.school.status !== "Provisioning" || detail.school.appVersion !== null;

  async function load() {
    setLoading(true);
    setError(null);
    try { setText(await fetchSchoolLogs(detail.school.id, service, since, 1000)); }
    catch (cause) { setError(errorMessage(cause)); }
    finally { setLoading(false); }
  }

  const lines = useMemo(() => {
    const all = (text ?? "").split("\n").filter((line) => line.trim() !== "").reverse();
    const needle = query.trim().toLocaleLowerCase("tr");
    return all.filter((line) => {
      const lineLevel = levelOf(line);
      if (level === "error" && lineLevel !== "error") return false;
      if (level === "warn" && lineLevel === null) return false;
      return needle === "" || line.toLocaleLowerCase("tr").includes(needle);
    });
  }, [text, query, level]);

  return (
    <div className="grid gap-4">
      <FormMessage tone="info">Loglar okulun sunucusundan şu an okunur, platformda saklanmaz. Kişisel veri içerebilir: her görüntüleme denetim kaydına yazılır.</FormMessage>
      <Panel
        title="Konteyner logu"
        meta="Son 1000 satır, en yenisi üstte"
        actions={<button type="button" className="btn btn-primary" onClick={load} disabled={loading || !installed}><Icon name="refresh" className="h-4 w-4" />{text === null ? "Getir" : "Yenile"}</button>}
      >
        <div className="mb-3 flex flex-wrap items-center gap-3">
          <Segmented label="Servis" value={service} onChange={setService} options={SERVICES} />
          <Segmented label="Süre" value={since} onChange={setSince} options={WINDOWS} />
          <Segmented label="Seviye" value={level} onChange={setLevel} options={[{ value: "all", label: "Hepsi" }, { value: "warn", label: "Uyarı+" }, { value: "error", label: "Hata" }]} />
          <div className="min-w-[14rem] flex-1"><SearchInput value={query} onChange={setQuery} label="Logda ara" placeholder="Metin ara…" /></div>
        </div>
        {error && <FormMessage tone="error">{error}</FormMessage>}
        {loading ? <Skeleton className="h-64" /> : text === null ? (
          <EmptyState icon="history" title="Log henüz okunmadı" description="Servis ve süreyi seçip Getir'e bas." />
        ) : lines.length === 0 ? (
          <EmptyState icon="check" title="Eşleşen satır yok" />
        ) : (
          <pre className="max-h-[32rem] overflow-auto rounded-xl bg-[var(--surface-muted)] p-3 font-mono text-[.72rem] leading-relaxed">
            {lines.map((line, index) => {
              const lineLevel = levelOf(line);
              return <div key={index} className={lineLevel === "error" ? "text-[var(--danger-strong)]" : lineLevel === "warn" ? "text-[var(--warning-strong)]" : ""}>{line}</div>;
            })}
          </pre>
        )}
      </Panel>
    </div>
  );
}

// Okulun yedekleri: R2'deki günlük yedekler, askı/silme öncesi saklama dışı dökümler ve sunucudaki dökümler.
export function BackupsTab({ detail }: { detail: SchoolDetail }) {
  const backups = useSchoolBackups(detail.school.id);
  const now = useNow();
  if (backups.isLoading) return <Skeleton className="h-48" />;
  if (backups.error) return <LoadError message={errorMessage(backups.error)} onRetry={() => backups.refetch()} />;
  const items = backups.data?.items ?? [];
  const total = items.reduce((sum, item) => sum + item.size, 0);
  return (
    <Panel
      title="Yedekler"
      meta={`${items.length} dosya · ${formatBytes(total)} · son başarılı yedek ${formatAge(backups.data?.lastSucceededAt, now)}`}
      actions={<button type="button" className="btn" onClick={() => backups.refetch()} disabled={backups.isFetching}><Icon name="refresh" className="h-4 w-4" />Yenile</button>}
      flush
    >
      {items.length === 0 ? <div className="p-4"><EmptyState icon="database" title="Yedek yok" description="Okul henüz yedek almamış ya da R2 tanımlı değil." /></div> : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[36rem] text-left text-sm">
            <thead className="text-[.72rem] uppercase tracking-wide text-[var(--muted)]">
              <tr className="border-b border-[var(--line)]">{["Dosya", "Yer", "Boyut", "Tarih"].map((label) => <th key={label} className="px-3 py-2 font-bold">{label}</th>)}</tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={`${item.location}-${item.name}`} className="border-b border-[var(--line)] last:border-0">
                  <td className="px-3 py-2 font-mono text-xs break-all">{item.name}</td>
                  <td className="px-3 py-2">{item.location}</td>
                  <td className="px-3 py-2 tabular-nums">{formatBytes(item.size)}</td>
                  <td className="px-3 py-2">{formatDateTime(item.modifiedAt)} <span className="text-meta">· {formatAge(item.modifiedAt, now)}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}
