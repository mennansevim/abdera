"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { TermReportFactsView } from "@/components/term-report-facts";
import { ApiError } from "@/lib/api";
import { useCreateGuardianPracticeEntry, type GuardianProgress, type GuardianProgressEntry, type PracticeJournal } from "@/lib/guardian";
import { useGuardianTermReports } from "@/lib/term-reports";
import { EmptyCard, SectionTitle, SkeletonStack, ViewHeader, formatInstantDate, localIsoDate } from "./shared";

const NOTES_PREVIEW = 3;
const DURATION_PRESETS = [15, 30, 45, 60];

export function ProgressView({ studentId, progress, practiceJournal, loading }: {
  studentId: string | undefined;
  progress: GuardianProgress | undefined;
  practiceJournal: PracticeJournal | undefined;
  loading: boolean;
}) {
  const [showAllNotes, setShowAllNotes] = useState(false);

  if (loading) return <><ViewHeader title="Gelişim" /><SkeletonStack heights={["h-24", "h-48", "h-64"]} /></>;

  const present = progress?.presentCount ?? 0;
  const absent = progress?.absentCount ?? 0;
  const total = present + absent + (progress?.excusedCount ?? 0);
  const entries = progress?.entries ?? [];
  const pieces = Array.from(new Set(entries.map((entry) => entry.pieceTitle).filter((piece): piece is string => !!piece)));
  const visibleEntries = showAllNotes ? entries : entries.slice(0, NOTES_PREVIEW);

  return (
    <div>
      <ViewHeader title="Gelişim" />

      <div className="grid grid-cols-3 gap-2">
        <Metric label="Derse katılım" value={total ? `%${Math.round((present / total) * 100)}` : "—"} />
        <Metric label="Gelmediği ders" value={String(absent)} warn={absent > 1} />
        <Metric label="Çalıştığı eser" value={String(pieces.length)} />
      </div>

      <SectionTitle>Öğretmenden notlar</SectionTitle>
      {entries.length ? (
        <div className="space-y-2">
          {visibleEntries.map((entry) => <TeacherNote key={entry.id} entry={entry} />)}
          {entries.length > NOTES_PREVIEW && (
            <button type="button" onClick={() => setShowAllNotes((value) => !value)} className="pressable min-h-11 w-full rounded-xl text-sm font-bold text-[var(--brand-strong)]">
              {showAllNotes ? "Daha az göster" : `Tüm notları göster (${entries.length})`}
            </button>
          )}
        </div>
      ) : (
        <EmptyCard icon="note">Öğretmen henüz bir not paylaşmadı.</EmptyCard>
      )}

      <PracticeJournalPanel studentId={studentId} journal={practiceJournal} />
      <TermReports studentId={studentId} />
    </div>
  );
}

function Metric({ label, value, warn }: { label: string; value: string; warn?: boolean }) {
  return (
    <div className="app-card px-2 py-3 text-center">
      <p className={`text-[1.35rem] font-bold tabular-nums ${warn ? "text-[var(--danger-strong)]" : "text-[var(--brand-strong)]"}`}>{value}</p>
      <p className="mt-0.5 text-[.8125rem] font-semibold leading-tight text-[var(--muted)]">{label}</p>
    </div>
  );
}

function TeacherNote({ entry }: { entry: GuardianProgressEntry }) {
  return (
    <article className="app-card p-4">
      <p className="text-[.8125rem] font-semibold text-[var(--muted)]">
        {formatInstantDate(entry.lessonStartAt)} · {entry.instrumentName} · {entry.teacherName}
      </p>
      {entry.parentComment && <p className="mt-2 whitespace-pre-line text-[.9375rem] leading-relaxed">{entry.parentComment}</p>}
      {entry.homework && (
        <p className="mt-3 flex gap-2 rounded-xl bg-[var(--warning-soft)] px-3 py-2.5 text-sm text-[var(--warning-strong)]">
          <Icon name="pencil" className="mt-0.5 h-4 w-4 shrink-0" />
          <span><strong>Ev ödevi:</strong> {entry.homework}</span>
        </p>
      )}
      {entry.nextGoal && (
        <p className="mt-2 flex gap-2 text-sm text-[var(--brand-strong)]">
          <Icon name="target" className="mt-0.5 h-4 w-4 shrink-0" />
          <span><strong>Sonraki hedef:</strong> {entry.nextGoal}</span>
        </p>
      )}
      {entry.practiced && <p className="mt-2 text-sm text-[var(--muted)]"><strong className="text-[var(--foreground)]">Derste çalışılan:</strong> {entry.practiced}</p>}
      {entry.pieceTitle && (
        <div className="mt-3 flex flex-wrap items-center gap-x-2 gap-y-1 rounded-xl bg-[var(--surface-muted)] px-3 py-2.5 text-sm">
          <Icon name="music" className="h-4 w-4 text-[var(--brand-strong)]" />
          <strong>{entry.pieceTitle}</strong>
          {entry.pieceComposer && <span className="text-[var(--muted)]">{entry.pieceComposer}</span>}
          {entry.pieceDifficulty && <span className="text-[var(--muted)]">· Seviye {entry.pieceDifficulty}/5</span>}
          {entry.pieceResourceUrl && <a href={entry.pieceResourceUrl} target="_blank" rel="noreferrer" className="ml-auto inline-flex min-h-9 items-center font-bold text-[var(--brand-strong)] underline">Notayı aç</a>}
        </div>
      )}
    </article>
  );
}

function PracticeJournalPanel({ studentId, journal }: { studentId: string | undefined; journal: PracticeJournal | undefined }) {
  const createEntry = useCreateGuardianPracticeEntry(studentId);
  const [date, setDate] = useState(localIsoDate);
  const [durationMinutes, setDurationMinutes] = useState("30");
  const [goal, setGoal] = useState("");
  const [note, setNote] = useState("");
  const [showNote, setShowNote] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);
    if (!(Number(durationMinutes) > 0)) {
      setError("Kaç dakika çalışıldığını seç.");
      return;
    }
    try {
      await createEntry.mutateAsync({ date, durationMinutes: Number(durationMinutes), goal, note: note || undefined });
      setGoal("");
      setNote("");
      setShowNote(false);
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Çalışma kaydı eklenemedi.");
    }
  }

  return (
    <>
      <SectionTitle>Evde çalışma</SectionTitle>
      <section className="app-card p-4">
        <div className="flex items-center gap-3">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name="clock" className="h-5 w-5" /></span>
          <div className="min-w-0 flex-1">
            <p className="text-lg font-bold tabular-nums">{journal?.totalMinutes ?? 0} dakika</p>
            <p className="text-[.8125rem] text-[var(--muted)]">Toplam evde çalışma</p>
          </div>
        </div>
        {!!journal?.badges.length && (
          <div className="mt-3 flex flex-wrap gap-1.5">
            {journal.badges.map((badge) => <span key={badge} className="rounded-full bg-[var(--warning-soft)] px-2.5 py-1 text-[.8125rem] font-bold text-[var(--warning-strong)]">{badge}</span>)}
          </div>
        )}

        <form onSubmit={submit} className="mt-4 space-y-3 border-t border-[var(--line)] pt-4">
          <p className="text-sm font-bold">Bugün çalıştık</p>
          <div>
            <p id="practice-duration" className="mb-1.5 text-[.8125rem] font-semibold text-[var(--muted)]">Kaç dakika?</p>
            {/* Hazır süreler tek dokunuş; listede olmayan süre son kutuya yazılır. */}
            <div className="grid grid-cols-5 gap-1.5" role="group" aria-labelledby="practice-duration">
              {DURATION_PRESETS.map((minutes) => (
                <button
                  key={minutes}
                  type="button"
                  onClick={() => setDurationMinutes(String(minutes))}
                  aria-pressed={durationMinutes === String(minutes)}
                  className={`pressable min-h-12 rounded-xl text-sm font-bold ${durationMinutes === String(minutes) ? "bg-[var(--brand-strong)] text-white" : "border border-[var(--line)] bg-white text-[var(--foreground)]"}`}
                >
                  {minutes} dk
                </button>
              ))}
              <input
                type="number"
                inputMode="numeric"
                min="1"
                max="600"
                aria-label="Süre (dk)"
                placeholder="Diğer"
                value={DURATION_PRESETS.includes(Number(durationMinutes)) ? "" : durationMinutes}
                onChange={(event) => setDurationMinutes(event.target.value)}
                className={`field min-h-12 px-1 text-center text-base ${DURATION_PRESETS.includes(Number(durationMinutes)) ? "" : "border-[var(--brand)]"}`}
              />
            </div>
          </div>
          <label className="block text-[.8125rem] font-semibold text-[var(--muted)]">Tarih
            <input type="date" max={localIsoDate()} value={date} onChange={(event) => setDate(event.target.value)} required className="field mt-1 text-base" />
          </label>
          <label className="block text-[.8125rem] font-semibold text-[var(--muted)]">Bugünkü hedef
            <input value={goal} onChange={(event) => setGoal(event.target.value)} required maxLength={500} placeholder="Ör. sol el gamı, yavaş tempo" className="field mt-1 text-base" />
          </label>
          {showNote ? (
            <label className="block text-[.8125rem] font-semibold text-[var(--muted)]">Not
              <textarea value={note} onChange={(event) => setNote(event.target.value)} maxLength={2000} rows={2} autoFocus className="field mt-1 resize-y text-base" />
            </label>
          ) : (
            <button type="button" onClick={() => setShowNote(true)} className="pressable flex min-h-11 items-center gap-1.5 text-sm font-semibold text-[var(--brand-strong)]">
              <Icon name="plus" className="h-4 w-4" /> Not ekle
            </button>
          )}
          {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-sm font-semibold text-[var(--danger-strong)]">{error}</p>}
          {saved && <p role="status" className="rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-sm font-semibold text-[var(--success-strong)]">Çalışma kaydedildi.</p>}
          <button disabled={createEntry.isPending || !studentId} className="pressable min-h-12 w-full rounded-xl bg-[var(--brand-strong)] text-sm font-bold text-white disabled:opacity-50">
            {createEntry.isPending ? "Kaydediliyor…" : "Çalışmayı kaydet ve onayla"}
          </button>
        </form>

        {!!journal?.entries.length && (
          <ul className="mt-4 divide-y divide-[var(--line)] border-t border-[var(--line)]">
            {journal.entries.slice(0, 5).map((entry) => (
              <li key={entry.id} className="py-3">
                <div className="flex items-baseline justify-between gap-2">
                  <p className="min-w-0 text-sm font-bold">{entry.goal}</p>
                  <p className="shrink-0 text-sm font-bold tabular-nums">{entry.durationMinutes} dk</p>
                </div>
                <p className="mt-0.5 text-[.8125rem] text-[var(--muted)]">
                  {new Date(`${entry.date}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} · {entry.parentApproved ? "Veli onaylı" : "Onay bekliyor"}
                </p>
                {entry.note && <p className="mt-1 text-[.8125rem] text-[var(--muted)]">{entry.note}</p>}
              </li>
            ))}
          </ul>
        )}
      </section>
    </>
  );
}

// Dönem karnesi (issue #15): yalnızca öğretmenin yayınladığı karneler, en yeni dönem önce.
// Veriler yayın anındaki haliyle sabittir; karne yoksa bölüm hiç görünmez.
function TermReports({ studentId }: { studentId: string | undefined }) {
  const { data: reports } = useGuardianTermReports(studentId);
  if (!reports?.length) return null;
  return (
    <>
      <SectionTitle>Dönem karnesi</SectionTitle>
      <div className="space-y-2">
        {reports.map((report) => (
          <details key={report.id} className="app-card group overflow-hidden" open={report === reports[0]}>
            <summary className="flex min-h-14 cursor-pointer list-none items-center gap-3 px-4 py-3">
              <span className="min-w-0 flex-1">
                <span className="block text-sm font-bold">{report.term.label} · {report.facts.instrumentName}</span>
                <span className="block text-[.8125rem] text-[var(--muted)]">{report.facts.teacherName} · {new Date(report.publishedAt).toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric" })}</span>
              </span>
              <Icon name="chevron" className="h-4 w-4 shrink-0 text-[var(--muted)] transition-transform group-open:rotate-90" />
            </summary>
            <div className="space-y-3 border-t border-[var(--line)] px-4 py-4">
              <p className="whitespace-pre-line text-[.9375rem] leading-relaxed">{report.comment}</p>
              <TermReportFactsView facts={report.facts} />
            </div>
          </details>
        ))}
      </div>
    </>
  );
}
