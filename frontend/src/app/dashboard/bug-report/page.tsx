"use client";

import { Suspense, useMemo, useState, type FormEvent } from "react";
import { useSearchParams } from "next/navigation";
import { Icon } from "@/components/icons";
import { Badge, EmptyState, FormMessage, Notice, PageHeader, Pager, Panel, Segmented, type Tone } from "@/components/ui";
import { ApiError, type Me } from "@/lib/api";
import {
  useAwaitingMe, useBugReports, useConfirmBugReport, useCreateBugReport, useProvideBugReportDetails, useTriageBugReport,
  type BugReport, type BugReportKind, type BugReportStatus,
} from "@/lib/bug-reports";
import { isModuleEnabled, useInstanceConfig } from "@/lib/instance";
import { CURRENT_VERSION } from "@/lib/releases";
import { useMe } from "@/lib/use-auth";
import { navPagesFor } from "../app-header";

// Hata bildir (docs/10-decisions.md W). Öğretmen formu doldurur, teşekkür mesajını görür.
// Gelen bildirimleri yalnızca yönetici görür; her biri doğrulanıp GitHub'da task'a dönüşür ya
// da gerekçesiyle reddedilir. Görev canlıya çıkınca bildirim "Çözüldü" olur ve bildirimi yapan
// bu sayfada "Onaylandı / Hâlâ hatalı" diye teyit eder (W6). Ne istendiği anlaşılamayan
// bildirim "Beklemede" olur; bildirimi yapan sorulan soruya bu sayfadan cevap yazar (W7).
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
  const newCount = useBugReports(["New"], 1, isAdmin).data?.counts.new ?? 0;

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
      {tab === "inbox" && isAdmin ? <Inbox isAdmin={isAdmin} /> : (
        <>
          <AwaitingMe />
          <ReportForm key={fromPath ?? ""} me={me} fromPath={fromPath} />
        </>
      )}
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
  const { data: instance } = useInstanceConfig();
  const pages = useMemo(() => navPagesFor(me.role === "Admin", (key) => isModuleEnabled(instance, key)), [me.role, instance]);
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

type InboxTab = "new" | "waiting" | "open" | "resolved" | "rejected";

// Sekme birden çok durumu bir arada gösterir: "Görevde" yeniden açılanları, "Çözüldü" teyit
// bekleyen ve onaylananları da içerir.
const INBOX_TABS: Record<InboxTab, { statuses: BugReportStatus[]; label: string; empty: string }> = {
  new: { statuses: ["New"], label: "Yeni", empty: "Bekleyen bildirim yok" },
  waiting: { statuses: ["NeedsInfo"], label: "Beklemede", empty: "Detay beklenen bildirim yok" },
  open: { statuses: ["Triaged", "Reopened"], label: "Görevde", empty: "Görevde bildirim yok" },
  resolved: { statuses: ["Resolved", "Verified"], label: "Çözüldü", empty: "Çözülen bildirim yok" },
  rejected: { statuses: ["Rejected"], label: "Reddedildi", empty: "Reddedilen bildirim yok" },
};

function Inbox({ isAdmin }: { isAdmin: boolean }) {
  const [tab, setTab] = useState<InboxTab>("new");
  const [page, setPage] = useState(1);
  const { data, isLoading } = useBugReports(INBOX_TABS[tab].statuses, page, isAdmin);
  const pages = useMemo(() => navPagesFor(true), []);
  const counts = data?.counts;
  const countOf: Record<InboxTab, number> | undefined = counts && {
    new: counts.new,
    waiting: counts.needsInfo,
    open: counts.triaged + counts.reopened,
    resolved: counts.resolved + counts.verified,
    rejected: counts.rejected,
  };
  const totalPages = data ? Math.max(1, Math.ceil(data.page.totalCount / data.page.pageSize)) : 1;

  return (
    <Panel
      flush
      title="Gelen bildirimler"
      actions={(
        <Segmented<InboxTab>
          label="Durum"
          value={tab}
          onChange={(value) => { setTab(value); setPage(1); }}
          options={(Object.keys(INBOX_TABS) as InboxTab[]).map((value) => ({
            value,
            label: `${INBOX_TABS[value].label}${countOf ? ` · ${countOf[value]}` : ""}`,
          }))}
        />
      )}
      footer={totalPages > 1 ? <Pager page={page} totalPages={totalPages} onChange={setPage} /> : undefined}
    >
      {isLoading ? (
        <p className="text-meta p-4">Yükleniyor…</p>
      ) : !data || data.page.items.length === 0 ? (
        <EmptyState title={INBOX_TABS[tab].empty} />
      ) : (
        <ul className="divide-y divide-[var(--line)]">
          {data.page.items.map((report) => <InboxRow key={report.id} report={report} pageLabel={matchPage(pages, report.pagePath)?.label} />)}
        </ul>
      )}
    </Panel>
  );
}

const STATUS_BADGE: Partial<Record<BugReportStatus, { tone: Tone; label: string }>> = {
  Resolved: { tone: "warning", label: "Teyit bekliyor" },
  Verified: { tone: "success", label: "Onaylandı" },
  Reopened: { tone: "danger", label: "Hâlâ hatalı" },
  NeedsInfo: { tone: "muted", label: "Detay bekleniyor" },
};

// Detay istenen bildirime cevap: açıklamanın sonuna eklenir, bildirim yeniden incelemeye düşer.
function DetailsForm({ report }: { report: BugReport }) {
  const provide = useProvideBugReportDetails();
  const [details, setDetails] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function send(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (details.trim().length < MIN_LENGTH) {
      setError(`Açıklama en az ${MIN_LENGTH} karakter olmalı.`);
      return;
    }
    try {
      await provide.mutateAsync({ id: report.id, details: details.trim() });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Gönderilemedi.");
    }
  }

  return (
    <form onSubmit={send} className="grid gap-2">
      <label className="form-label">Cevabın
        <textarea value={details} onChange={(e) => setDetails(e.target.value)} rows={3} maxLength={MAX_LENGTH} className="field py-2 text-sm" placeholder="Ne olmasını istiyorsun? Hangi öğrenci/ders, hangi adımda?" />
      </label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <button type="submit" disabled={provide.isPending} className="btn btn-primary justify-self-start">{provide.isPending ? "Gönderiliyor…" : "Detayı gönder"}</button>
    </form>
  );
}

// Bildirimi yapanın teyidi: "Onaylandı" süreci kapatır, "Hâlâ hatalı" görevi yeniden açtırır.
function ConfirmButtons({ report }: { report: BugReport }) {
  const confirm = useConfirmBugReport();
  const [broken, setBroken] = useState(false);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function send(works: boolean) {
    setError(null);
    try {
      await confirm.mutateAsync({ id: report.id, works, note: works ? null : note.trim() || null });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Gönderilemedi.");
    }
  }

  return (
    <div className="grid gap-2">
      {broken ? (
        <div className="flex flex-wrap items-end gap-2">
          <label className="form-label min-w-0 flex-1">Ne hâlâ çalışmıyor? (isteğe bağlı)
            <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} className="field text-sm" placeholder="Hangi adımda, ne oldu?" />
          </label>
          <button type="button" onClick={() => setBroken(false)} disabled={confirm.isPending} className="btn btn-quiet">Vazgeç</button>
          <button type="button" onClick={() => send(false)} disabled={confirm.isPending} className="btn btn-primary">Gönder</button>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => send(true)} disabled={confirm.isPending} className="btn btn-primary">
            <Icon name="check" className="h-4 w-4" /> Onaylandı
          </button>
          <button type="button" onClick={() => setBroken(true)} disabled={confirm.isPending} className="btn btn-quiet">Hâlâ hatalı</button>
        </div>
      )}
      {error && <FormMessage tone="error">{error}</FormMessage>}
    </div>
  );
}

// Kullanıcıdan bir şey bekleyen kendi bildirimleri - formun üstünde, cevaplanana kadar durur:
// detay istenenler (soru + cevap kutusu) ve çözülüp teyit bekleyenler.
function AwaitingMe() {
  const { data } = useAwaitingMe();
  const needsInfo = data?.filter((report) => report.status === "NeedsInfo") ?? [];
  const resolved = data?.filter((report) => report.status === "Resolved") ?? [];
  return (
    <>
      {needsInfo.length > 0 && (
        <Panel title="Bildirimini anlayamadık, biraz detay verir misin?">
          <ul className="grid gap-4">
            {needsInfo.map((report) => (
              <li key={report.id} className="grid gap-2">
                <p className="text-meta">{formatDate(report.createdAt)} tarihli bildirimin:</p>
                <p className="line-clamp-3 whitespace-pre-wrap break-words text-sm">{report.description}</p>
                {report.triageNote && <p className="rounded-xl bg-[var(--warning-soft)] px-3 py-2 text-sm font-semibold text-[var(--warning-strong)]">Soru: {report.triageNote}</p>}
                <DetailsForm report={report} />
              </li>
            ))}
          </ul>
        </Panel>
      )}
      {resolved.length > 0 && (
        <Panel title="Çözüldü, teyidini bekliyoruz">
          <ul className="grid gap-4">
            {resolved.map((report) => (
              <li key={report.id} className="grid gap-2">
                <p className="text-meta">{formatDate(report.createdAt)} tarihli bildirimin{report.resolvedAt ? `, ${formatDate(report.resolvedAt)} tarihinde çözüldü` : " çözüldü"}. Şimdi doğru çalışıyor mu?</p>
                <p className="line-clamp-3 whitespace-pre-wrap break-words text-sm">{report.description}</p>
                <ConfirmButtons report={report} />
              </li>
            ))}
          </ul>
        </Panel>
      )}
    </>
  );
}

function InboxRow({ report, pageLabel }: { report: BugReport; pageLabel?: string }) {
  const triage = useTriageBugReport();
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const reporter = report.reporterName ?? report.reporterEmail ?? "Silinmiş kullanıcı";
  const kindTone: Tone = report.kind === "Bug" ? "danger" : "warning";
  const statusBadge = STATUS_BADGE[report.status];

  async function update(status: BugReportStatus, triageNote = status === "Rejected" || status === "NeedsInfo" ? note.trim() || null : null) {
    setError(null);
    try {
      await triage.mutateAsync({ id: report.id, status, githubIssueNumber: null, triageNote });
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
            {statusBadge && <Badge tone={statusBadge.tone}>{statusBadge.label}</Badge>}
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
          {report.githubIssueNumber && report.status !== "New" && report.status !== "Rejected" && (
            // Kayıt numarası gösterilir; ürün ekibinin iç görev takibine bağlantı okula açılmaz.
            <Badge tone="success" className="justify-self-start">Görev #{report.githubIssueNumber}</Badge>
          )}
          {report.status === "Rejected" && report.triageNote && <p className="text-meta">Ret gerekçesi: {report.triageNote}</p>}
          {report.status === "NeedsInfo" && (
            <>
              {report.triageNote && <p className="text-meta">Sorulan: {report.triageNote}</p>}
              <DetailsForm report={report} />
              {/* Detay gelmeyecekse bildirim kapatılır; Reddedildi sekmesinden "Yeniden aç" ile geri alınır. */}
              <button type="button" onClick={() => update("Rejected", "Detay beklerken iptal edildi.")} disabled={triage.isPending} className="btn btn-quiet justify-self-start">İptal et</button>
            </>
          )}
          {report.status === "Reopened" && <p className="text-meta">Hâlâ hatalı{report.feedbackNote ? `: ${report.feedbackNote}` : " (açıklama yok)"}</p>}
          {(report.status === "Triaged" || report.status === "Reopened") && (
            <button type="button" onClick={() => update("Resolved")} disabled={triage.isPending} className="btn btn-quiet justify-self-start">
              <Icon name="check" className="h-4 w-4" /> Çözüldü olarak işaretle
            </button>
          )}
          {report.status === "Resolved" && <ConfirmButtons report={report} />}
          {report.status === "New" && (
            <div className="flex flex-wrap items-end gap-2">
              <label className="form-label min-w-0 flex-1">Not: ret gerekçesi ya da bildirimi yapana soru
                <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} className="field text-sm" placeholder="Hangi öğrenciler, ne olmasını istiyorsun?" />
              </label>
              <button type="button" onClick={() => update("NeedsInfo")} disabled={triage.isPending || !note.trim()} className="btn btn-quiet">Detay iste</button>
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
