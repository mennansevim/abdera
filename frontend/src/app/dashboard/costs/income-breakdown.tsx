"use client";

import { useMemo } from "react";
import { Icon } from "@/components/icons";
import { Modal } from "@/components/ui";
import { formatPeriod } from "@/lib/billing-format";
import { COURSE_KIND_LABEL, type BillingDue, type CourseKind, type PaymentMethod } from "@/lib/billing";
import { methodLabel } from "../billing/collect-sheet";
import { useBranchTerms } from "@/lib/instance";

// Gelir dökümü: Gelir ve gider ekranındaki her gelir rakamına (Özet'teki Gelir kartı, nakit
// akışı tablosunun aidat hücreleri, Gelirler sekmesi) dokununca açılır ve rakamın hangi
// ödemelerden oluştuğunu gösterir; aynı satırlar CSV olarak indirilir.
//
// Gelir AİDAT DÖNEMİNE göre sayılır: Ekim dökümü yalnızca Ekim aidatlarına yapılan ödemeleri
// listeler. Eylül aidatı Ekim'de ödense de Eylül'de görünür. Eskiden ödeme tarihine göre
// sayılıp "kendi ayı / gecikmiş / peşin" diye ayrılıyordu; geçmiş ayın parası bu ayın gelirine
// karıştığı için kullanıcı kafa karıştırıcı buldu ve kaldırıldı.

export interface IncomeEntry {
  id: string;
  date: string;
  amount: number;
  method: PaymentMethod;
  reference: string | null;
  studentName: string;
  teacherName: string;
  instrumentName: string;
  courseKind: CourseKind;
  period: string;
  prepayPlanMonths: number | null;
  corrected: boolean;
  // Tarihi düzeltilmiş ödemede ilk girilen tarih (date etkin tarihtir).
  originalDate: string | null;
}

// Aidatlara düşen tahsilatları ödeme tarihine göre verir. Düzeltilmiş bir tahsilatın geçerli
// tutarı en son düzeltmedir (sunucudaki ComputeEffectivePaymentAmountsAsync ile aynı kural);
// düzeltme satırları ayrıca sayılmaz, yoksa para iki kez gelir yazılırdı. Sıfıra düzeltilmiş
// (geri alınmış) tahsilat gelir değildir, listeden düşer.
export function collectedPayments(dues: BillingDue[]): IncomeEntry[] {
  return dues.flatMap((due) => {
    const corrections = due.payments.filter((payment) => payment.kind === "Correction");
    return due.payments
      .filter((payment) => payment.kind === "Payment")
      .map((payment) => {
        const latest = corrections
          .filter((correction) => correction.correctsPaymentId === payment.id)
          .sort((a, b) => (b.recordedAt ?? "").localeCompare(a.recordedAt ?? ""))[0];
        return {
          id: payment.id, date: payment.paymentDate, amount: latest ? latest.amount : payment.amount, method: payment.method,
          reference: payment.reference, studentName: due.studentName, teacherName: due.teacherName, instrumentName: due.instrumentName,
          courseKind: due.courseKind, period: due.period, prepayPlanMonths: payment.prepayPlanMonths, corrected: Boolean(latest),
          originalDate: payment.originalPaymentDate ?? null,
        };
      })
      .filter((entry) => entry.amount !== 0);
  }).sort((a, b) => b.date.localeCompare(a.date) || a.studentName.localeCompare(b.studentName, "tr-TR"));
}

// Ödemenin aidat dönemi ile ödendiği ay karşılaştırılır: aynı ay = o ayın aidatı, önce =
// gecikmiş tahsilat, sonra = ileri bir ayın peşin ödemesi. Gelir toplamını bölmez; yalnızca
// tahsilat defterinin filtresi, tarih düzeltme önerisi ve CSV'nin "Gelir türü" sütunu kullanır.
export type IncomeBucket = "current" | "late" | "advance";
export const BUCKET_LABEL: Record<IncomeBucket, string> = { current: "Kendi ayının aidatı", late: "Gecikmiş aidat", advance: "Peşin (ileri aylar)" };
export const BUCKET_COLOR: Record<IncomeBucket, string> = { current: "var(--success)", late: "#b7791f", advance: "#3f6fb0" };

export function bucketOf(entry: Pick<IncomeEntry, "date" | "period">): IncomeBucket {
  const paidMonth = entry.date.slice(0, 7);
  return entry.period === paidMonth ? "current" : entry.period < paidMonth ? "late" : "advance";
}

const money = (value: number) => `₺${value.toLocaleString("tr-TR", { maximumFractionDigits: 2 })}`;
const dateTr = (iso: string) => `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}`;

// Türkçe Excel: ayraç `;`, ondalık virgül; BOM olmadan ğ/ş bozuk açılır.
function csvCell(value: string | number) {
  const text = typeof value === "number" ? value.toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: false }) : value;
  return /[";\n\r]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

export function downloadIncomeCsv(entries: IncomeEntry[], fileName: string, branchLabel = "Enstrüman") {
  const header = ["Ödeme tarihi", "Öğrenci", branchLabel, "Öğretmen", "Ders türü", "Aidat dönemi", "Gelir türü", "Yöntem", "Peşin plan (ay)", "Referans", "Düzeltildi", "Tutar (TRY)"];
  const rows = [...entries].sort((a, b) => a.date.localeCompare(b.date) || a.studentName.localeCompare(b.studentName, "tr-TR")).map((entry) => [
    dateTr(entry.date), entry.studentName, entry.instrumentName, entry.teacherName, COURSE_KIND_LABEL[entry.courseKind], formatPeriod(entry.period),
    BUCKET_LABEL[bucketOf(entry)], methodLabel(entry.method), entry.prepayPlanMonths ? String(entry.prepayPlanMonths) : "", entry.reference ?? "", entry.corrected ? "Evet" : "",
    entry.amount,
  ]);
  const total = entries.reduce((sum, entry) => sum + entry.amount, 0);
  const lines = [header, ...rows, [], ["Toplam", "", "", "", "", "", "", "", "", "", "", total]]
    .map((row) => row.map((cell) => csvCell(cell as string | number)).join(";"));
  const blob = new Blob([`﻿${lines.join("\r\n")}`], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function fileSlug(text: string) {
  return text.toLocaleLowerCase("tr-TR")
    .replace(/[çğıöşü]/g, (char) => ({ ç: "c", ğ: "g", ı: "i", ö: "o", ş: "s", ü: "u" })[char] ?? char)
    .replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

function groupSum<K extends string>(entries: IncomeEntry[], key: (entry: IncomeEntry) => K) {
  const map = new Map<K, { amount: number; count: number }>();
  for (const entry of entries) {
    const current = map.get(key(entry)) ?? { amount: 0, count: 0 };
    map.set(key(entry), { amount: current.amount + entry.amount, count: current.count + 1 });
  }
  return Array.from(map.entries()).sort((a, b) => b[1].amount - a[1].amount);
}

export function IncomeBreakdownModal({ open, title, entries, onClose }: { open: boolean; title: string; entries: IncomeEntry[]; onClose: () => void }) {
  const terms = useBranchTerms();
  const total = entries.reduce((sum, entry) => sum + entry.amount, 0);
  const students = useMemo(() => new Set(entries.map((entry) => entry.studentName)).size, [entries]);
  const byPeriod = useMemo(() => groupSum(entries, (entry) => entry.period).sort((a, b) => a[0].localeCompare(b[0])), [entries]);
  const byInstrument = useMemo(() => groupSum(entries, (entry) => `${entry.instrumentName} · ${COURSE_KIND_LABEL[entry.courseKind]}`), [entries]);
  const byMethod = useMemo(() => groupSum(entries, (entry) => methodLabel(entry.method)), [entries]);

  return (
    <Modal open={open} title={title} description="Bu dönemin aidatlarına yapılan ödemeler. Geçmiş ayların geç ödenen aidatı kendi ayında görünür." onClose={onClose} size="lg">
      <div className="space-y-4">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <p className="text-micro text-[var(--muted)]">Toplam</p>
            <p className="text-2xl font-bold tabular-nums text-[var(--success-strong)]">{money(total)}</p>
            <p className="text-meta">{entries.length} ödeme · {students} öğrenci</p>
          </div>
          <button type="button" disabled={!entries.length} onClick={() => downloadIncomeCsv(entries, `gelir-dokumu-${fileSlug(title)}.csv`, terms.branch)} className="btn btn-primary">
            <Icon name="download" className="h-4 w-4" />Detaylı dökümü indir (Excel/CSV)
          </button>
        </div>

        <div className={`grid gap-3 ${byPeriod.length > 1 ? "md:grid-cols-3" : "md:grid-cols-2"}`}>
          {byPeriod.length > 1 && <BreakdownTable title="Aidat dönemine göre" rows={byPeriod.map(([period, value]) => ({ label: formatPeriod(period), ...value }))} />}
          <BreakdownTable title={`${terms.branch} ve ders türü`} rows={byInstrument.map(([label, value]) => ({ label, ...value }))} />
          <BreakdownTable title="Ödeme yöntemi" rows={byMethod.map(([label, value]) => ({ label, ...value }))} />
        </div>

        <section aria-label="Ödemeler" className="overflow-hidden rounded-xl border border-[var(--line)]">
          <div className="max-h-80 overflow-auto">
            <table className="w-full min-w-[30rem] text-[.8125rem]">
              <thead className="sticky top-0 bg-[var(--surface-muted)] text-[.6875rem] font-bold text-[var(--muted)]">
                <tr>
                  <th scope="col" className="px-3 py-2 text-left">Ödeme tarihi</th>
                  <th scope="col" className="px-3 py-2 text-left">Öğrenci</th>
                  <th scope="col" className="px-3 py-2 text-left">Aidat</th>
                  <th scope="col" className="px-3 py-2 text-right">Tutar</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => (
                  <tr key={entry.id} className="border-t border-[var(--line)]">
                    <td className="px-3 py-1.5 tabular-nums whitespace-nowrap">{dateTr(entry.date)}</td>
                    <td className="px-3 py-1.5"><span className="font-semibold">{entry.studentName}</span><span className="text-meta block text-[.72rem]">{entry.instrumentName} · {COURSE_KIND_LABEL[entry.courseKind]} · {methodLabel(entry.method)}</span></td>
                    <td className="px-3 py-1.5 whitespace-nowrap">{formatPeriod(entry.period)}</td>
                    <td className="px-3 py-1.5 text-right font-semibold tabular-nums whitespace-nowrap">{money(entry.amount)}</td>
                  </tr>
                ))}
                {!entries.length && <tr><td colSpan={4} className="text-meta px-3 py-6 text-center">Bu kalemde tahsilat yok.</td></tr>}
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </Modal>
  );
}

function BreakdownTable({ title, rows }: { title: string; rows: { label: string; amount: number; count: number }[] }) {
  return (
    <section className="min-w-0 rounded-xl border border-[var(--line)]">
      <p className="border-b border-[var(--line)] px-3 py-2 text-xs font-bold">{title}</p>
      <ul className="max-h-48 overflow-auto">
        {rows.map((row) => (
          <li key={row.label} className="flex items-baseline justify-between gap-2 px-3 py-1.5 text-[.8125rem]">
            <span className="min-w-0 truncate">{row.label} <span className="text-[var(--muted)]">· {row.count}</span></span>
            <span className="shrink-0 font-semibold tabular-nums">{money(row.amount)}</span>
          </li>
        ))}
        {!rows.length && <li className="text-meta px-3 py-2">—</li>}
      </ul>
    </section>
  );
}
