"use client";

import Link from "next/link";
import { auditActionLabel, type AuditEntry } from "@/lib/audit";
import { formatDateTime } from "@/lib/format";
import { EmptyState, Panel } from "./ui";

// JSON değeri tek satır özet: {"from":"1","to":"2"} → from: 1 · to: 2. Okunamazsa ham metin.
function summarize(json: string | null): string | null {
  if (!json) return null;
  try {
    const value = JSON.parse(json) as unknown;
    if (value && typeof value === "object" && !Array.isArray(value)) {
      return Object.entries(value as Record<string, unknown>)
        .map(([key, item]) => `${key}: ${Array.isArray(item) ? item.join(", ") : typeof item === "object" && item !== null ? JSON.stringify(item) : String(item)}`)
        .join(" · ");
    }
    return JSON.stringify(value);
  } catch {
    return json;
  }
}

// Denetim kaydı tablosu - hem genel Denetim ekranında hem okul detayında.
export function AuditTable({ entries, showSchool = true }: { entries: AuditEntry[]; showSchool?: boolean }) {
  if (entries.length === 0) return <Panel><EmptyState icon="history" title="Kayıt yok" /></Panel>;
  return (
    <Panel flush>
      <div className="overflow-x-auto">
        <table className="data-table">
          <thead>
            <tr>
              <th>Zaman</th>
              <th>Kim</th>
              <th>Eylem</th>
              {showSchool && <th>Okul</th>}
              <th>Ayrıntı</th>
            </tr>
          </thead>
          <tbody>
            {entries.map((entry) => {
              const before = summarize(entry.beforeJson);
              const after = summarize(entry.afterJson);
              return (
                <tr key={entry.id}>
                  <td className="whitespace-nowrap">{formatDateTime(entry.createdAt)}</td>
                  <td className="max-w-[12rem] truncate">{entry.actor ?? "—"}</td>
                  <td className="whitespace-nowrap font-semibold" title={entry.action}>{auditActionLabel(entry.action)}</td>
                  {showSchool && (
                    <td className="font-mono text-xs">
                      {entry.schoolId ? <Link href={`/schools/${entry.schoolId}`} className="text-[var(--brand-strong)] hover:underline">{entry.slug ?? "?"}</Link> : "—"}
                    </td>
                  )}
                  <td className="min-w-[16rem] text-xs text-[var(--muted)]">
                    {before && <span className="block break-words"><b>Önce:</b> {before}</span>}
                    {after && <span className="block break-words">{before && <b>Sonra: </b>}{after}</span>}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
