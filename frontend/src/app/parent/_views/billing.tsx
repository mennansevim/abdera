"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import type { GuardianBilling, GuardianReceivable } from "@/lib/guardian";
import {
  EmptyCard,
  LoadError,
  Pill,
  SectionTitle,
  SkeletonStack,
  ViewHeader,
  formatDate,
  formatInstantDate,
  formatMoney,
  formatPeriod,
  isOpenReceivable,
  remainingOf,
  type PillTone,
} from "./shared";

const PAID_PREVIEW = 4;

export function BillingView({ billing, loading, error, refetch, fetching, studentId }: {
  billing: GuardianBilling | undefined;
  loading: boolean;
  error: boolean;
  refetch: () => Promise<unknown>;
  fetching: boolean;
  studentId: string | undefined;
}) {
  const [showAllPaid, setShowAllPaid] = useState(false);

  if (loading) return <><ViewHeader title="Aidat" /><SkeletonStack heights={["h-36", "h-32", "h-48"]} /></>;
  if (error) return <><ViewHeader title="Aidat" /><LoadError message="Aidat bilgisi yüklenemedi" onRetry={refetch} pending={fetching} /></>;

  const enrollments = billing?.enrollments.filter((item) => item.studentId === studentId) ?? [];
  const makeups = billing?.makeupCredits.filter((credit) => credit.studentId === studentId)
    .sort((a, b) => a.expiresAt.localeCompare(b.expiresAt)) ?? [];
  // Tek öğrencinin birden fazla kursu olabilir; liste kurs kurs değil, velinin sorusuna göre
  // (ödenecek / ödenmiş) ayrılır. Kurs adı her satırda zaten yazıyor.
  const rows = enrollments.flatMap((enrollment) => enrollment.receivables
    .filter((receivable) => receivable.status !== "Cancelled")
    .map((receivable) => ({ receivable, instrumentName: enrollment.instrumentName })));
  const open = rows.filter((row) => isOpenReceivable(row.receivable)).sort((a, b) => a.receivable.dueDate.localeCompare(b.receivable.dueDate));
  const paid = rows.filter((row) => row.receivable.status === "Paid").sort((a, b) => b.receivable.dueDate.localeCompare(a.receivable.dueDate));
  const openTotal = open.reduce((sum, row) => sum + remainingOf(row.receivable), 0);
  const overdueCount = open.filter((row) => row.receivable.status === "Overdue").length;
  const currency = rows[0]?.receivable.currency ?? "TRY";
  const showCourse = enrollments.length > 1;
  const visiblePaid = showAllPaid ? paid : paid.slice(0, PAID_PREVIEW);

  return (
    <div>
      <ViewHeader title="Aidat" />

      {/* Durum kartı: velinin tek sorusu "borcum var mı, ne kadar, ne zamana kadar?" */}
      {open.length ? (
        <section className={`rounded-[1.4rem] px-5 py-5 ${overdueCount ? "bg-[var(--danger-soft)] text-[var(--danger-strong)]" : "bg-[var(--warning-soft)] text-[var(--warning-strong)]"}`}>
          <p className="text-sm font-bold">Ödenecek tutar</p>
          <p className="mt-1 text-[2rem] font-bold leading-none tabular-nums">{formatMoney(openTotal, currency)}</p>
          <p className="mt-2 text-sm font-semibold">
            {overdueCount
              ? `${overdueCount} ödemenin günü geçti`
              : `Son ödeme günü ${formatDate(open[0]!.receivable.dueDate)}`}
          </p>
        </section>
      ) : (
        <section className={`flex items-center gap-4 rounded-[1.4rem] px-5 py-5 ${rows.length ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--surface-muted)]"}`}>
          <span className={`grid h-12 w-12 shrink-0 place-items-center rounded-2xl ${rows.length ? "bg-white/70" : "bg-white text-[var(--muted)]"}`}>
            <Icon name={rows.length ? "check" : "wallet"} className="h-6 w-6" />
          </span>
          <div>
            <p className="text-lg font-bold">{rows.length ? "Borcunuz yok" : "Henüz aidat yok"}</p>
            <p className="text-sm opacity-80">{rows.length ? "Tüm ödemeler tamam." : "Okul aidat eklediğinde burada görünecek."}</p>
          </div>
        </section>
      )}

      {open.length > 0 && <PaymentInfo iban={billing?.virtualIban?.iban} />}

      {open.length > 0 && (
        <>
          <SectionTitle>Ödenecekler</SectionTitle>
          <div className="app-card divide-y divide-[var(--line)] overflow-hidden">
            {open.map((row) => <ReceivableRow key={row.receivable.id} receivable={row.receivable} course={showCourse ? row.instrumentName : undefined} />)}
          </div>
        </>
      )}

      {paid.length > 0 && (
        <>
          <SectionTitle>Ödenenler</SectionTitle>
          <div className="app-card divide-y divide-[var(--line)] overflow-hidden">
            {visiblePaid.map((row) => <ReceivableRow key={row.receivable.id} receivable={row.receivable} course={showCourse ? row.instrumentName : undefined} />)}
          </div>
          {paid.length > PAID_PREVIEW && (
            <button type="button" onClick={() => setShowAllPaid((value) => !value)} className="pressable mt-2 min-h-11 w-full rounded-xl text-sm font-bold text-[var(--brand-strong)]">
              {showAllPaid ? "Daha az göster" : `Tüm ödemeleri göster (${paid.length})`}
            </button>
          )}
        </>
      )}

      {open.length === 0 && billing?.virtualIban && <PaymentInfo iban={billing.virtualIban.iban} />}

      <SectionTitle>Telafi hakları</SectionTitle>
      {makeups.length ? (
        <div className="app-card divide-y divide-[var(--line)] overflow-hidden">
          {makeups.map((credit) => (
            <div key={credit.id} className="flex items-center gap-3 px-4 py-3">
              <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name="swap" className="h-4 w-4" /></span>
              <div className="min-w-0 flex-1">
                <p className="text-sm font-bold">1 telafi dersi</p>
                <p className="text-[.8125rem] text-[var(--muted)]">{formatInstantDate(credit.expiresAt)} tarihine kadar kullanılabilir</p>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <EmptyCard icon="swap">Şu an kullanılabilir telafi hakkı yok.</EmptyCard>
      )}
    </div>
  );
}

const STATUS: Record<GuardianReceivable["status"], { label: string; tone: PillTone }> = {
  Paid: { label: "Ödendi", tone: "green" },
  Partial: { label: "Kısmen ödendi", tone: "amber" },
  Overdue: { label: "Gecikti", tone: "red" },
  Unpaid: { label: "Bekliyor", tone: "amber" },
  Cancelled: { label: "İptal", tone: "neutral" },
};

function ReceivableRow({ receivable, course }: { receivable: GuardianReceivable; course?: string }) {
  const period = formatPeriod(receivable.period);
  const paid = receivable.status === "Paid";
  const status = STATUS[receivable.status];
  return (
    <div className="flex items-center gap-3 px-4 py-3">
      <div className="min-w-0 flex-1">
        <p className="text-[.9375rem] font-bold">
          {period.title}
          {period.detail && <span className="font-semibold text-[var(--muted)]"> · {period.detail}</span>}
        </p>
        <p className="text-[.8125rem] text-[var(--muted)]">
          {[course, `${paid ? "Vade" : "Son gün"} ${formatDate(receivable.dueDate)}`, receivable.prepayPlanMonths ? `Peşin ${receivable.prepayPlanMonths} ay` : null].filter(Boolean).join(" · ")}
        </p>
      </div>
      <div className="flex shrink-0 flex-col items-end gap-1">
        <span className="text-[.9375rem] font-bold tabular-nums">{formatMoney(paid ? receivable.totalPaid : remainingOf(receivable), receivable.currency)}</span>
        <Pill tone={status.tone}>{status.label}</Pill>
      </div>
    </div>
  );
}

function PaymentInfo({ iban }: { iban: string | undefined }) {
  const [copyState, setCopyState] = useState<"idle" | "copied" | "error">("idle");

  async function copyIban() {
    if (!iban) return;
    try {
      if (navigator.clipboard) {
        await navigator.clipboard.writeText(iban);
      } else {
        const textarea = document.createElement("textarea");
        textarea.value = iban;
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.select();
        const copied = document.execCommand("copy");
        textarea.remove();
        if (!copied) throw new Error("copy failed");
      }
      setCopyState("copied");
      window.setTimeout(() => setCopyState("idle"), 1800);
    } catch {
      setCopyState("error");
    }
  }

  return (
    <section className="app-card mt-3 p-4">
      <p className="flex items-center gap-2 text-sm font-bold"><Icon name="bank" className="h-4 w-4 text-[var(--brand-strong)]" /> Nasıl ödenir?</p>
      {iban ? (
        <>
          <p className="mt-1 text-sm text-[var(--muted)]">Bu IBAN&apos;a havale/EFT yapabilirsiniz. Ödemeniz otomatik olarak işlenir.</p>
          <p className="mt-3 select-all break-all rounded-xl bg-[var(--surface-muted)] px-3 py-3 font-mono text-[.9375rem] font-bold tracking-wide">{iban}</p>
          <button type="button" onClick={copyIban} className={`pressable mt-3 flex min-h-12 w-full items-center justify-center gap-2 rounded-xl text-sm font-bold ${copyState === "copied" ? "bg-[var(--success)] text-white" : "bg-[var(--brand-strong)] text-white"}`}>
            <Icon name={copyState === "copied" ? "check" : "note"} className="h-4 w-4" />
            {copyState === "copied" ? "IBAN kopyalandı" : "IBAN’ı kopyala"}
          </button>
          {copyState === "error" && <p role="alert" className="mt-2 text-center text-[.8125rem] text-[var(--danger-strong)]">Kopyalanamadı; numaraya basılı tutup kopyalayabilirsiniz.</p>}
        </>
      ) : (
        <p className="mt-1 text-sm text-[var(--muted)]">Ödeme için okul yönetimiyle görüşebilirsiniz.</p>
      )}
    </section>
  );
}
