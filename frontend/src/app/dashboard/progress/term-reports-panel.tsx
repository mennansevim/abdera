"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { TermReportFactsView } from "@/components/term-report-facts";
import { FormMessage, Modal } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  TERM_REPORT_COMMENT_MAX_LENGTH,
  usePublishTermReport,
  useSaveTermReport,
  useTermReport,
  useTermReports,
  useUnpublishTermReport,
  type TermReportRow,
} from "@/lib/term-reports";

// Dönem karnesi (issue #15, docs/10-decisions.md Z). Öğretmen kendi kurs kayıtlarının karnesini
// buradan yazar ve yayınlar; yönetici tüm okulun ilerlemesini görür. Yorumu öğretmen yazar -
// yapay zekâ taslağı yok (kullanıcı kararı). Yayınlanan karne yalnızca veli portalında görünür.
export function TermReportsPanel({ isAdmin }: { isAdmin: boolean }) {
  const [term, setTerm] = useState<string | null>(null);
  const { data, isLoading, isError } = useTermReports(term);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<TermReportRow | null>(null);

  if (isLoading) return <div className="skeleton h-16 rounded-2xl" aria-label="Dönem karneleri yükleniyor" />;
  if (isError || !data) return null;
  if (!data.rows.length && !term) return null;

  const total = data.rows.length;
  const viewingPrevious = term !== null;

  return (
    <section aria-labelledby="donem-karnesi-baslik" className="app-card overflow-clip">
      <header className="flex flex-wrap items-center gap-3 p-3 sm:p-4">
        <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name="note" className="h-4 w-4" /></span>
        <div className="min-w-0 flex-1">
          <h2 id="donem-karnesi-baslik" className="text-sm font-bold">Dönem karnesi · {data.term.label}</h2>
          <p className="text-meta mt-0.5">{data.publishedCount}/{total} yayınlandı{data.draftCount ? ` · ${data.draftCount} taslak` : ""} · veli portalında görünür</p>
        </div>
        <div className="flex items-center gap-2">
          <button type="button" onClick={() => setTerm(viewingPrevious ? null : data.previousTerm.key)} className="btn btn-quiet text-xs">
            {viewingPrevious ? "Bu döneme dön" : "Önceki dönem"}
          </button>
          <button type="button" onClick={() => setOpen((value) => !value)} aria-expanded={open} className="btn btn-quiet text-xs">
            {open ? "Gizle" : "Karneleri aç"}
          </button>
        </div>
      </header>
      {open && (
        <ul className="divide-y divide-[var(--line)] border-t border-[var(--line)]">
          {data.rows.map((row) => (
            <li key={row.enrollmentId}>
              <button type="button" onClick={() => setEditing(row)} className="pressable flex w-full items-center gap-3 px-3 py-2.5 text-left hover:bg-[var(--surface-muted)] sm:px-4">
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-xs font-bold">{row.studentName} <span className="font-medium text-[var(--muted)]">· {row.instrumentName}</span></span>
                  {isAdmin && <span className="text-meta block truncate">{row.teacherName}</span>}
                </span>
                <StatusPill status={row.status} />
              </button>
            </li>
          ))}
          {!data.rows.length && <li className="text-meta px-4 py-6 text-center">Bu dönemde karne yazılacak kurs kaydı yok.</li>}
        </ul>
      )}
      {editing && (
        <Modal open title={`${editing.studentName} · ${editing.instrumentName}`} onClose={() => setEditing(null)}>
          <TermReportEditor enrollmentId={editing.enrollmentId} term={data.term.key} />
        </Modal>
      )}
    </section>
  );
}

function StatusPill({ status }: { status: TermReportRow["status"] }) {
  const config = status === "Published"
    ? { label: "Yayında", className: "bg-[var(--success-soft)] text-[var(--success-strong)]" }
    : status === "Draft"
      ? { label: "Taslak", className: "bg-[var(--warning-soft)] text-[var(--warning-strong)]" }
      : { label: "Yazılmadı", className: "bg-[var(--surface-muted)] text-[var(--muted)]" };
  return <span className={`shrink-0 rounded-full px-2 py-1 text-[.72rem] font-bold ${config.className}`}>{config.label}</span>;
}

function TermReportEditor({ enrollmentId, term }: { enrollmentId: string; term: string }) {
  const { data: report, isLoading, isError } = useTermReport(enrollmentId, term);
  const save = useSaveTermReport(enrollmentId, term);
  const publish = usePublishTermReport(enrollmentId, term);
  const unpublish = useUnpublishTermReport(enrollmentId, term);
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const pending = save.isPending || publish.isPending || unpublish.isPending;

  if (isLoading) return <div className="space-y-2"><div className="skeleton h-16 rounded-xl" /><div className="skeleton h-28 rounded-xl" /></div>;
  if (isError || !report) return <FormMessage tone="error">Karne yüklenemedi.</FormMessage>;

  const published = report.status === "Published";

  async function run(action: () => Promise<unknown>, success: string) {
    setMessage(null);
    try {
      await action();
      setMessage({ tone: "success", text: success });
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? (err.detail ?? err.title) : "İşlem tamamlanamadı." });
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-meta">{report.term.label} · {report.facts.teacherName}</p>
        {published && report.publishedAt && (
          <span className="rounded-full bg-[var(--success-soft)] px-2 py-1 text-[.72rem] font-bold text-[var(--success-strong)]">
            Yayında · {new Date(report.publishedAt).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}
          </span>
        )}
      </div>
      <TermReportFactsView facts={report.facts} />
      {published ? (
        <div className="space-y-3">
          <p className="whitespace-pre-line rounded-xl border border-[var(--line)] bg-white p-3 text-sm">{report.comment}</p>
          <p className="text-meta">Yayınlanmış karnenin verileri yayın anındaki haliyle sabittir. Düzeltmek için yayından kaldırıp yeniden yayınla.</p>
          {message && <FormMessage tone={message.tone}>{message.text}</FormMessage>}
          <div className="flex justify-end">
            <button type="button" onClick={() => void run(() => unpublish.mutateAsync(), "Karne yayından kaldırıldı; veli artık görmüyor.")} disabled={pending} className="btn btn-quiet">
              {unpublish.isPending ? "Kaldırılıyor…" : "Yayından kaldır"}
            </button>
          </div>
        </div>
      ) : (
        // Taslak formu kayıtlı yorumla bir kez açılır; sonraki yenilemeler yazılanı ezmez.
        <DraftForm
          initialComment={report.comment ?? ""}
          pending={pending}
          saving={save.isPending && !publish.isPending}
          publishing={publish.isPending}
          message={message}
          onSave={(comment) => void run(() => save.mutateAsync({ comment }), "Taslak kaydedildi; veli henüz görmüyor.")}
          // Önce yazılanı kaydet, sonra yayınla: yayınlanan yorum her zaman ekrandaki metindir.
          onPublish={(comment) => void run(async () => {
            await save.mutateAsync({ comment });
            await publish.mutateAsync();
          }, "Karne yayınlandı; veli portalında görünüyor.")}
        />
      )}
    </div>
  );
}

function DraftForm({ initialComment, pending, saving, publishing, message, onSave, onPublish }: {
  initialComment: string;
  pending: boolean;
  saving: boolean;
  publishing: boolean;
  message: { tone: "success" | "error"; text: string } | null;
  onSave: (comment: string) => void;
  onPublish: (comment: string) => void;
}) {
  const [comment, setComment] = useState(initialComment);

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    onSave(comment);
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <label className="block">
        <span className="text-meta mb-1.5 block font-bold">Öğretmen yorumu <span className="font-medium">· veli bu metni görür</span></span>
        <textarea value={comment} onChange={(event) => setComment(event.target.value)} rows={6} maxLength={TERM_REPORT_COMMENT_MAX_LENGTH}
          placeholder="Bu dönem neler başardı, nerede zorlandı, gelecek dönem neye odaklanacağız…" className="field resize-y text-sm" />
        <span className="text-meta mt-1 block text-right tabular-nums">{comment.length}/{TERM_REPORT_COMMENT_MAX_LENGTH}</span>
      </label>
      {message && <FormMessage tone={message.tone}>{message.text}</FormMessage>}
      <div className="flex flex-wrap justify-end gap-2">
        <button type="submit" disabled={pending} className="btn btn-quiet">{saving ? "Kaydediliyor…" : "Taslak kaydet"}</button>
        <button type="button" onClick={() => onPublish(comment)} disabled={pending || !comment.trim()} className="btn btn-primary">{publishing ? "Yayınlanıyor…" : "Yayınla"}</button>
      </div>
    </form>
  );
}
