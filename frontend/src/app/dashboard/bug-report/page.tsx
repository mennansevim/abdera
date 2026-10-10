"use client";

import { Suspense, useMemo, useState, type FormEvent } from "react";
import { useSearchParams } from "next/navigation";
import { Icon } from "@/components/icons";
import { Badge, EmptyState, FormMessage, Notice, PageHeader, Pager, Panel, Segmented, type Tone } from "@/components/ui";
import { ApiError, type Me } from "@/lib/api";
import {
  BUG_REPORT_ISSUE_URL, useBugReports, useCreateBugReport, useTriageBugReport,
  type BugReport, type BugReportKind, type BugReportStatus,
} from "@/lib/bug-reports";
import { CURRENT_VERSION } from "@/lib/releases";
import { useMe } from "@/lib/use-auth";
import { navPagesFor } from "../app-header";

// Hata bildir (docs/10-decisions.md W). Öğretmen için gönder-unut: formu doldurur, teşekkür
// mesajını görür, o kadar. Gelen bildirimleri yalnızca yönetici görür; her biri doğrulanıp
// GitHub'da task'a dönüşür ya da gerekçesiyle reddedilir.
const OTHER = "__other__";
const MIN_LENGTH = 10;
const MAX_LENGTH = 4000;

type Tab = "report" | "inbox";

// useSearchParams App Router'da bir Suspense sınırı ister.
export default function BugReportPage() {
  return <Suspense><BugReportScreen /></Suspense>;
}

function BugReportScreen() {
  const fromPath = useSearchParams().get("from");
  const { data: me } = useMe();
  const [tab, setTab] = useState<Tab>("report");
  const isAdmin = me?.role === "Admin";
  const newCount = useBugReports("New", 1, isAdmin).data?.counts.new ?? 0;

  if (!me) return null;

  return (
    <div className="mx-auto max-w-3xl space-y-3">
      <PageHeader
        title="Hata bildir"
        description="Bir şey çalışmıyorsa ya da aklına bir öneri geldiyse yaz; gerisini biz takip ederiz."
      />
      {isAdmin && (
        <Segmented<Tab>
          label="Hata bildir sekmeleri"
          value={tab}
          onChange={setTab}
          options={[
            { value: "report", label: "Hata bildir", icon: "bug" },
            { value: "inbox", label: newCount > 0 ? `Gelen bildirimler · ${newCount}` : "Gelen bildirimler", icon: "bell" },
          ]}
        />
      )}
      {tab === "inbox" && isAdmin ? <Inbox isAdmin={isAdmin} /> : <ReportForm key={fromPath ?? ""} me={me} fromPath={fromPath} />}
    </div>
  );
}

// "/dashboard/students/123" gibi alt sayfalar menüdeki en uzun eşleşen sayfaya düşer.
function matchPage(pages: { href: string; label: string }[], path: string | null) {
  if (!path) return undefined;
  return pages
    .filter((page) => (page.href === "/dashboard" ? path === page.href : path.startsWith(page.href)))
    .sort((a, b) => b.href.length - a.href.length)[0];
}

function ReportForm({ me, fromPath }: { me: Me; fromPath: string | null }) {
  const pages = useMemo(() => navPagesFor(me.role === "Admin"), [me.role]);
  const createReport = useCreateBugReport();
  const [kind, setKind] = useState<BugReportKind>("Bug");
  const [page, setPage] = useState(() => matchPage(pages, fromPath)?.href ?? "");
  const [otherPage, setOtherPage] = useState("");
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    const selected = page === OTHER ? otherPage.trim() : page;
    if (!selected) {
      setError("Hangi sayfada olduğunu seç.");
      return;
    }
    if (description.trim().length < MIN_LENGTH) {
      setError(`Açıklama en az ${MIN_LENGTH} karakter olmalı.`);
      return;
    }
    // Menüdeki sayfa seçildiyse ve kullanıcı o sayfanın bir alt sayfasından geldiyse (öğrenci
    // künyesi gibi) tam adres saklanır - hatayı yeniden üretirken işe yarar.
    const pagePath = page !== OTHER && fromPath && matchPage(pages, fromPath)?.href === page ? fromPath : selected;
    try {
      await createReport.mutateAsync({ kind, pagePath, description: description.trim(), appVersion: CURRENT_VERSION });
      setSent(true);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Bildirim gönderilemedi. Bağlantını kontrol edip tekrar dene.");
    }
  }

  function reset() {
    setSent(false);
    setDescription("");
    setKind("Bug");
  }

  if (sent) {
    return (
      <Panel>
        <div className="grid justify-items-start gap-3">
          <Notice>Teşekkürler, bildirimin ulaştı. İnceleyip gereğini yapacağız.</Notice>
          <button type="button" onClick={reset} className="btn btn-quiet">
            <Icon name="plus" className="h-4 w-4" /> Yeni bildirim
          </button>
        </div>
      </Panel>
    );
  }

  return (
    <Panel>
      <form onSubmit={handleSubmit} className="grid gap-4">
        <div className="grid gap-1.5">
          <span className="form-label">Tür</span>
          <Segmented<BugReportKind>
            label="Bildirim türü"
            value={kind}
            onChange={setKind}
            options={[
              { value: "Bug", label: "Bir şey çalışmıyor", icon: "bug" },
              { value: "Suggestion", label: "Önerim var", icon: "sparkles" },
            ]}
          />
        </div>

        <label className="form-label">Hangi sayfada?
          <select value={page} onChange={(e) => setPage(e.target.value)} className="field text-sm">
            <option value="" disabled>Sayfa seç</option>
            {pages.map((item) => <option key={item.href} value={item.href}>{item.label}</option>)}
            <option value={OTHER}>Diğer</option>
          </select>
        </label>
        {page === OTHER && (
          <label className="form-label">Sayfanın adı
            <input value={otherPage} onChange={(e) => setOtherPage(e.target.value)} maxLength={200} className="field text-sm" placeholder="Giriş ekranı" />
          </label>
        )}

        <label className="form-label">Açıklama
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={6}
            maxLength={MAX_LENGTH}
            className="field min-h-36 py-2 text-sm"
            placeholder={kind === "Bug"
              ? "Ne yapmaya çalışıyordun, ne oldu, ne bekliyordun?"
              : "Neyi, neden değiştirelim? Sana ne kazandırır?"}
          />
          <span className="text-meta block text-right">{description.trim().length} / {MAX_LENGTH}</span>
        </label>

        {error && <FormMessage tone="error">{error}</FormMessage>}

        <div className="flex flex-wrap items-center justify-between gap-3">
          <span className="text-meta">Sürüm {CURRENT_VERSION} ve tarayıcı bilgisi otomatik eklenir.</span>
          <button type="submit" disabled={createReport.isPending} className="btn btn-primary">
            {createReport.isPending ? "Gönderiliyor…" : "Gönder"}
          </button>
        </div>
      </form>
    </Panel>
  );
}

const STATUS_LABEL: Record<BugReportStatus, string> = { New: "Yeni", Triaged: "Göreve dönüştü", Rejected: "Reddedildi" };

function Inbox({ isAdmin }: { isAdmin: boolean }) {
  const [status, setStatus] = useState<BugReportStatus>("New");
  const [page, setPage] = useState(1);
  const { data, isLoading } = useBugReports(status, page, isAdmin);
  const pages = useMemo(() => navPagesFor(true), []);
  const counts = data?.counts;
  const totalPages = data ? Math.max(1, Math.ceil(data.page.totalCount / data.page.pageSize)) : 1;

  return (
    <Panel
      flush
      title="Gelen bildirimler"
      actions={(
        <Segmented<BugReportStatus>
          label="Durum"
          value={status}
          onChange={(value) => { setStatus(value); setPage(1); }}
          options={[
            { value: "New", label: `Yeni${counts ? ` · ${counts.new}` : ""}` },
            { value: "Triaged", label: `Göreve dönüştü${counts ? ` · ${counts.triaged}` : ""}` },
            { value: "Rejected", label: `Reddedildi${counts ? ` · ${counts.rejected}` : ""}` },
          ]}
        />
      )}
      footer={totalPages > 1 ? <Pager page={page} totalPages={totalPages} onChange={setPage} /> : undefined}
    >
      {isLoading ? (
        <p className="text-meta p-4">Yükleniyor…</p>
      ) : !data || data.page.items.length === 0 ? (
        <EmptyState title={status === "New" ? "Bekleyen bildirim yok" : `${STATUS_LABEL[status]} bildirim yok`} />
      ) : (
        <ul className="divide-y divide-[var(--line)]">
          {data.page.items.map((report) => <InboxRow key={report.id} report={report} pageLabel={matchPage(pages, report.pagePath)?.label} />)}
        </ul>
      )}
    </Panel>
  );
}

function InboxRow({ report, pageLabel }: { report: BugReport; pageLabel?: string }) {
  const triage = useTriageBugReport();
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const reporter = report.reporterName ?? report.reporterEmail ?? "Silinmiş kullanıcı";
  const kindTone: Tone = report.kind === "Bug" ? "danger" : "warning";

  async function update(status: BugReportStatus) {
    setError(null);
    try {
      await triage.mutateAsync({ id: report.id, status, githubIssueNumber: null, triageNote: status === "Rejected" ? note.trim() || null : null });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Güncellenemedi.");
    }
  }

  return (
    <li>
      <details className="group">
        <summary className="pressable flex cursor-pointer list-none flex-col gap-1 px-4 py-3 hover:bg-black/[.02]">
          <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            <Badge tone={kindTone}>{report.kind === "Bug" ? "Hata" : "Öneri"}</Badge>
            <span className="min-w-0 truncate font-bold">{pageLabel ?? report.pagePath}</span>
            <span className="text-meta truncate">· {reporter}</span>
            <span className="text-meta ml-auto whitespace-nowrap">{formatDate(report.createdAt)}</span>
          </div>
          <p className="line-clamp-2 text-sm text-[var(--muted)] group-open:hidden">{report.description}</p>
        </summary>
        <div className="grid gap-3 px-4 pb-4">
          <p className="whitespace-pre-wrap break-words text-sm">{report.description}</p>
          <dl className="text-meta grid gap-0.5 break-all">
            <div><dt className="inline font-semibold">Adres: </dt><dd className="inline">{report.pagePath}</dd></div>
            {report.appVersion && <div><dt className="inline font-semibold">Sürüm: </dt><dd className="inline">{report.appVersion}</dd></div>}
            {report.userAgent && <div><dt className="inline font-semibold">Tarayıcı: </dt><dd className="inline">{report.userAgent}</dd></div>}
            {report.reporterEmail && <div><dt className="inline font-semibold">Gönderen: </dt><dd className="inline">{report.reporterEmail}</dd></div>}
          </dl>
          {report.status === "Triaged" && report.githubIssueNumber && (
            <a href={`${BUG_REPORT_ISSUE_URL}/${report.githubIssueNumber}`} target="_blank" rel="noreferrer" className="justify-self-start">
              <Badge tone="success">Görev #{report.githubIssueNumber}</Badge>
            </a>
          )}
          {report.status === "Rejected" && report.triageNote && <p className="text-meta">Ret gerekçesi: {report.triageNote}</p>}
          {report.status === "New" && (
            <div className="flex flex-wrap items-end gap-2">
              <label className="form-label min-w-0 flex-1">Ret gerekçesi (isteğe bağlı)
                <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} className="field text-sm" placeholder="Zaten böyle çalışıyor" />
              </label>
              <button type="button" onClick={() => update("Rejected")} disabled={triage.isPending} className="btn btn-quiet">Reddet</button>
            </div>
          )}
          {report.status === "Rejected" && (
            <button type="button" onClick={() => update("New")} disabled={triage.isPending} className="btn btn-quiet justify-self-start">Yeniden aç</button>
          )}
          {error && <FormMessage tone="error">{error}</FormMessage>}
        </div>
      </details>
    </li>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString("tr-TR", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
}
