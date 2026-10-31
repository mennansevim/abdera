"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { FormMessage, PageHeader, SectionHeader } from "@/components/ui";
import { Icon } from "@/components/icons";
import { formatBytes } from "@/lib/audio-clips";
import {
  dataExportFileUrl, importTemplateUrl, isExportInProgress, useDataExports, useRequestDataExport, useSpreadsheetImport,
  type DataExport, type ImportKind, type ImportReport, type ImportRowStatus,
} from "@/lib/imports";
import { errorMessage } from "@/lib/library";
import { useMe } from "@/lib/use-auth";

// Excel'den içe aktarma (docs/10-decisions.md AF), yalnızca yönetici. Akış: boş şablonu indir,
// doldur, yükle → önizleme (hiçbir şey kaydedilmez) → onayla. Yalnızca "Hazır" satırlar yazılır;
// sistemde zaten olan kayıt atlanır, hatalı satır nedeniyle birlikte listelenir.
const KINDS: Array<{ value: ImportKind; label: string; description: string }> = [
  {
    value: "students",
    label: "Öğrenci + veli",
    description: "Her satır bir öğrenci ve (isteğe bağlı) bir kurs kaydı. İki enstrüman çalan öğrenciyi iki satıra yaz. Sistemde aynı ad soyad ve doğum tarihiyle ya da aynı veli telefonuyla kayıtlı öğrenci atlanır. Veliye mesaj gönderilmez.",
  },
  {
    value: "payments",
    label: "Ödeme geçmişi",
    description: "Her satır bir öğrencinin ödenmiş bir ayı. O ayın aidatı açılır ve ödendi işaretlenir; tutar tarifeden azsa fark indirim olarak yazılır, tarifeyi aşamaz. Önce öğrencileri ve kurs kayıtlarını aktar.",
  },
];

const STATUS: Record<ImportRowStatus, { label: string; className: string }> = {
  Ready: { label: "Hazır", className: "bg-[var(--brand-soft)] text-[var(--brand)]" },
  Imported: { label: "Aktarıldı", className: "bg-[var(--success-soft)] text-[var(--success-strong)]" },
  Skipped: { label: "Atlandı", className: "bg-[var(--surface-muted)] text-[var(--muted)]" },
  Error: { label: "Hatalı", className: "bg-[var(--danger-soft)] text-[var(--danger-strong)]" },
};

export default function ImportPage() {
  const { data: me } = useMe();
  const [kind, setKind] = useState<ImportKind>("students");

  if (me && me.role !== "Admin") {
    return <FormMessage tone="error">Excel ile aktarım yalnızca yöneticiye açık.</FormMessage>;
  }

  const current = KINDS.find((item) => item.value === kind)!;
  return (
    <div className="mx-auto max-w-4xl space-y-3">
      <PageHeader
        title="Excel ile aktar"
        description="Öğrencileri, velileri ve geçmiş ödemeleri toplu olarak al ya da tüm veriyi Excel'e indir."
        actions={<Link href="/dashboard/settings" className="btn btn-quiet"><Icon name="arrow-left" className="h-4 w-4" />Ayarlar</Link>}
      />

      <div className="grid gap-2 sm:grid-cols-2" role="tablist" aria-label="Ne içe aktarılacak">
        {KINDS.map((item) => (
          <button
            key={item.value}
            type="button"
            role="tab"
            aria-selected={kind === item.value}
            onClick={() => setKind(item.value)}
            className={`pressable rounded-xl border-2 px-3 py-3 text-left text-sm font-bold ${kind === item.value ? "border-[var(--brand)] bg-[var(--brand-soft)]" : "border-[var(--line)] bg-white hover:border-[var(--brand)]/50"}`}
          >
            {item.label}
          </button>
        ))}
      </div>

      <ImportPanel key={kind} kind={kind} title={current.label} description={current.description} />

      <ExportPanel />
    </div>
  );
}

function ImportPanel({ kind, title, description }: { kind: ImportKind; title: string; description: string }) {
  const input = useRef<HTMLInputElement>(null);
  const run = useSpreadsheetImport(kind);
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<ImportReport | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function send(selected: File, commit: boolean) {
    setError(null);
    try {
      setReport(await run.mutateAsync({ file: selected, commit }));
    } catch (err) {
      setReport(null);
      setError(errorMessage(err, "Dosya işlenemedi."));
    }
  }

  function choose(selected: File | undefined) {
    if (!selected) return;
    setFile(selected);
    void send(selected, false);
  }

  function reset() {
    setFile(null);
    setReport(null);
    setError(null);
    if (input.current) input.current.value = "";
  }

  return (
    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] px-4 py-2.5">
        <SectionHeader
          title={title}
          description={description}
          actions={<a href={importTemplateUrl(kind)} download className="btn btn-quiet"><Icon name="download" className="h-4 w-4" />Boş şablonu indir</a>}
        />
      </div>

      <div className="space-y-3 p-4">
        <label className="flex cursor-pointer flex-col items-center justify-center gap-1 rounded-xl border-2 border-dashed border-[var(--line)] px-4 py-6 text-center hover:border-[var(--brand)]/60">
          <span className="text-sm font-bold">{file ? file.name : "Excel dosyasını seç (.xlsx)"}</span>
          <span className="text-meta">Şablondaki ya da tüm veri dökümündeki başlık satırı korunmalı. En fazla 5000 satır, 10 MB.</span>
          <input
            ref={input}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            className="sr-only"
            onChange={(event) => choose(event.target.files?.[0])}
          />
        </label>

        {run.isPending && <p role="status" className="text-meta">Dosya okunuyor…</p>}
        {error && <FormMessage tone="error">{error}</FormMessage>}

        {report && (
          <>
            <div className="flex flex-wrap gap-2 text-xs font-bold" aria-label="Özet">
              {report.committed
                ? <Chip status="Imported" count={report.imported} />
                : <Chip status="Ready" count={report.ready} />}
              <Chip status="Skipped" count={report.skipped} />
              <Chip status="Error" count={report.errors} />
            </div>

            {report.committed ? (
              <FormMessage tone="success">
                {report.imported} satır içe aktarıldı.{report.skipped + report.errors > 0 && ` ${report.skipped + report.errors} satır atlandı; nedenleri aşağıda.`}
              </FormMessage>
            ) : (
              <p className="text-meta">Bu bir önizleme, henüz hiçbir şey kaydedilmedi. Hatalı satırları Excel&apos;de düzeltip dosyayı yeniden seçebilirsin.</p>
            )}

            <div className="max-h-[28rem] overflow-y-auto rounded-xl border border-[var(--line)]">
              <ul className="divide-y divide-[var(--line)]">
                {report.rows.map((row) => (
                  <li key={row.row} className="flex items-start gap-3 px-3 py-2">
                    <span className="text-meta w-12 shrink-0 pt-0.5 tabular-nums">Satır {row.row}</span>
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-semibold">{row.label}</p>
                      {row.message && <p className="text-meta mt-0.5">{row.message}</p>}
                    </div>
                    <span className={`shrink-0 rounded-full px-2 py-1 text-[.75rem] font-bold ${STATUS[row.status].className}`}>{STATUS[row.status].label}</span>
                  </li>
                ))}
              </ul>
            </div>

            <div className="flex flex-wrap justify-end gap-2">
              <button type="button" onClick={reset} className="btn btn-quiet">{report.committed ? "Yeni dosya" : "Vazgeç"}</button>
              {!report.committed && (
                <button
                  type="button"
                  onClick={() => file && void send(file, true)}
                  disabled={report.ready === 0 || run.isPending}
                  className="btn btn-primary"
                >
                  {run.isPending ? "Aktarılıyor…" : `${report.ready} satırı içe aktar`}
                </button>
              )}
            </div>
          </>
        )}
      </div>
    </section>
  );
}

function Chip({ status, count }: { status: ImportRowStatus; count: number }) {
  return <span className={`rounded-full px-2.5 py-1 ${STATUS[status].className}`}>{STATUS[status].label}: {count}</span>;
}

// Tüm verinin Excel dökümü (docs/10-decisions.md AJ). Sunucu arka planda üretir; bu ekranda
// istenen döküm hazır olunca kendiliğinden iner. Son üç döküm listede kalır.
function ExportPanel() {
  const exports = useDataExports();
  const request = useRequestDataExport();
  const [requested, setRequested] = useState<string | null>(null);
  const downloaded = useRef<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const items = exports.data;
  const inProgress = items?.some(isExportInProgress) ?? false;
  const waiting = items?.some((item) => item.id === requested && isExportInProgress(item)) ?? false;

  useEffect(() => {
    const ready = items?.find((item) => item.id === requested);
    if (!ready || ready.status !== "Completed" || downloaded.current === ready.id) return;
    downloaded.current = ready.id;
    const link = document.createElement("a");
    link.href = dataExportFileUrl(ready.id);
    link.download = ready.fileName ?? "";
    link.click();
  }, [items, requested]);

  async function start() {
    setError(null);
    try {
      setRequested((await request.mutateAsync()).id);
    } catch (err) {
      setError(errorMessage(err, "Döküm başlatılamadı."));
    }
  }

  return (
    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] px-4 py-2.5">
        <SectionHeader
          title="Tüm veriyi dışa aktar"
          description="Öğrenciler, veliler, öğretmenler, aidatlar, tahsilatlar, dersler ve giderler tek Excel dosyasında. Öğrenciler ve Ödemeler sayfaları yukarıdaki şablonlarla aynı; dosyayı olduğu gibi içe aktarmaya verebilirsin."
          actions={
            <button type="button" onClick={() => void start()} disabled={inProgress || request.isPending} className="btn btn-primary">
              <Icon name="download" className="h-4 w-4" />
              {inProgress ? "Hazırlanıyor…" : "Excel'e aktar"}
            </button>
          }
        />
      </div>
      <div className="space-y-2 p-4">
        {error && <FormMessage tone="error">{error}</FormMessage>}
        {waiting && <p role="status" className="text-meta">Dosya hazırlanıyor; bitince kendiliğinden inecek. Bu sayfadan ayrılırsan aşağıdaki listeden indirebilirsin.</p>}
        {!items?.length ? (
          <p className="text-meta">{exports.isLoading ? "Yükleniyor…" : "Henüz döküm alınmadı."}</p>
        ) : (
          <ul className="divide-y divide-[var(--line)] rounded-xl border border-[var(--line)]">
            {items.map((item) => <ExportRow key={item.id} item={item} />)}
          </ul>
        )}
        <p className="text-meta">Yer kaplamasın diye yalnızca son üç döküm saklanır.</p>
      </div>
    </section>
  );
}

function ExportRow({ item }: { item: DataExport }) {
  const created = new Date(item.createdAt).toLocaleString("tr-TR", { dateStyle: "medium", timeStyle: "short" });
  return (
    <li className="flex items-center gap-3 px-3 py-2">
      <div className="min-w-0 flex-1">
        <p className="text-sm font-semibold">{created}</p>
        <p className="text-meta mt-0.5">
          {isExportInProgress(item) && "Hazırlanıyor…"}
          {item.status === "Failed" && (item.errorMessage ? `Üretilemedi: ${item.errorMessage}` : "Üretilemedi.")}
          {item.status === "Completed" && `${item.fileName}${item.sizeBytes ? ` · ${formatBytes(item.sizeBytes)}` : ""}`}
        </p>
      </div>
      {item.status === "Completed" && (
        <a href={dataExportFileUrl(item.id)} download={item.fileName ?? ""} className="btn btn-quiet shrink-0">
          <Icon name="download" className="h-4 w-4" />İndir
        </a>
      )}
    </li>
  );
}
