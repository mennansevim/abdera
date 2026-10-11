"use client";

import { PIECE_STATUS_LABELS, type TermReportFacts } from "@/lib/term-reports";

// Karnenin veri kısmı - öğretmenin düzenleme penceresi ve veli portalı aynı görünümü kullanır,
// böylece öğretmen yayınlamadan önce velinin göreceğini birebir görür.
export function TermReportFactsView({ facts }: { facts: TermReportFacts }) {
  const total = facts.lessonsPresent + facts.lessonsAbsent + facts.lessonsExcused;
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-3 gap-2">
        <Metric label="Geldi" value={`${facts.lessonsPresent}${total ? ` / ${total}` : ""}`} />
        <Metric label="Mazeretli" value={String(facts.lessonsExcused)} />
        <Metric label="Gelmedi" value={String(facts.lessonsAbsent)} />
      </div>
      {facts.practiceMinutes > 0 && (
        <p className="rounded-xl bg-[var(--surface-muted)] px-3 py-2 text-xs">
          <strong>Evde çalışma:</strong> {facts.practiceDays} gün, toplam {formatMinutes(facts.practiceMinutes)}
        </p>
      )}
      {facts.pieces.length > 0 && (
        <section>
          <p className="text-micro text-[var(--muted)]">Çalışılan eserler</p>
          <ul className="mt-1.5 space-y-1">
            {facts.pieces.map((piece) => (
              <li key={piece.title} className="flex items-baseline justify-between gap-2 rounded-lg border border-[var(--line)] bg-white px-3 py-1.5 text-xs">
                <span className="min-w-0"><strong>{piece.title}</strong>{piece.composer && <span className="text-[var(--muted)]"> · {piece.composer}</span>}</span>
                {piece.status && <span className="shrink-0 text-[.72rem] font-bold text-[var(--brand-strong)]">{PIECE_STATUS_LABELS[piece.status]}</span>}
              </li>
            ))}
          </ul>
        </section>
      )}
      {facts.skills.length > 0 && (
        <section>
          <p className="text-micro text-[var(--muted)]">Beceriler (1–5)</p>
          <ul className="mt-1.5 grid gap-1 sm:grid-cols-2">
            {facts.skills.map((skill) => (
              <li key={skill.label} className="flex items-center justify-between rounded-lg bg-[var(--surface-muted)] px-3 py-1.5 text-xs">
                <span className="font-semibold">{skill.label}</span>
                <span className="tabular-nums">{skill.firstScore === skill.lastScore ? skill.lastScore : `${skill.firstScore} → ${skill.lastScore}`}</span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl bg-[var(--surface-muted)] px-3 py-2">
      <p className="text-micro text-[var(--muted)]">{label}</p>
      <p className="mt-0.5 text-sm font-bold tabular-nums">{value}</p>
    </div>
  );
}

function formatMinutes(minutes: number) {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours ? `${hours} sa${rest ? ` ${rest} dk` : ""}` : `${rest} dk`;
}
