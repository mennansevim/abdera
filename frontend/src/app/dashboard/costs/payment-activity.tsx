"use client";

import { COURSE_KIND_LABEL, type PaymentActivityKind, type PaymentActivityRow } from "@/lib/billing";
import { formatPeriod } from "@/lib/billing-format";
import { methodLabel } from "../billing/collect-sheet";
import { incomeMonthOf } from "./income-breakdown";

// Ödeme hareketleri (Gelir ve gider > Hareketler): tahsilata dokunan her kayıt, ekrana GİRİLDİĞİ
// güne göre, en yeni önce. Tahsilat defteri parayı ödeme tarihine göre dizer; burası "ödeme al
// dedim, ne yazıldı, hangi ayın gelirine düştü, kim girdi" sorusunun yanıtıdır. Salt okunur.

const MONTHS = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];
const WEEKDAYS = ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"];
const KIND_LABEL: Record<PaymentActivityKind, string> = { Received: "Ödeme alındı", AmountCorrected: "Tutar düzeltildi", DateCorrected: "Tarih düzeltildi" };
const KIND_COLOR: Record<PaymentActivityKind, string> = { Received: "var(--success)", AmountCorrected: "#b7791f", DateCorrected: "#3f6fb0" };

const pad = (value: number) => String(value).padStart(2, "0");
const money = (value: number) => `₺${value.toLocaleString("tr-TR", { maximumFractionDigits: 2 })}`;
const dateTr = (iso: string) => `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}`;
const monthName = (key: string) => `${MONTHS[Number(key.slice(5, 7)) - 1]} ${key.slice(0, 4)}`;

// Kayıt anı tarayıcının yerel gününe çevrilir (okul ve kullanıcı aynı saat diliminde).
export function recordedDay(row: Pick<PaymentActivityRow, "recordedAt">) {
  const at = new Date(row.recordedAt);
  return `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}`;
}

function dayHeading(key: string) {
  const [year, month, day] = key.split("-").map(Number);
  return `${day} ${MONTHS[month - 1]} ${year} · ${WEEKDAYS[(new Date(year, month - 1, day).getDay() + 6) % 7]}`;
}

function detail(row: PaymentActivityRow) {
  if (row.kind === "AmountCorrected") {
    return row.amount === 0 ? `${money(row.previousAmount ?? 0)} geri alındı` : `${money(row.previousAmount ?? 0)} → ${money(row.amount)}`;
  }
  if (row.kind === "DateCorrected") return `Ödeme tarihi ${dateTr(row.previousDate ?? row.paymentDate)} → ${dateTr(row.paymentDate)}`;
  return `${methodLabel(row.method)} · ödeme tarihi ${dateTr(row.paymentDate)}`;
}

export function PaymentActivityLog({ rows, loading }: { rows: PaymentActivityRow[]; loading: boolean }) {
  if (loading) return <p className="text-meta px-4 py-6 text-center">Yükleniyor…</p>;
  if (!rows.length) return <p className="text-meta px-4 py-6 text-center">Bu dönemde ödeme hareketi yok.</p>;

  const days: { key: string; rows: PaymentActivityRow[] }[] = [];
  for (const row of rows) {
    const key = recordedDay(row);
    const last = days[days.length - 1];
    if (last?.key === key) last.rows.push(row);
    else days.push({ key, rows: [row] });
  }

  return (
    <div>
      {days.map((day) => (
        <section key={day.key} aria-label={dayHeading(day.key)}>
          <h3 className="text-micro border-b border-[var(--line)] bg-[var(--surface-muted)] px-4 py-1.5 font-bold text-[var(--muted)]">{dayHeading(day.key)}</h3>
          <ul>
            {day.rows.map((row) => {
              const at = new Date(row.recordedAt);
              const incomeMonth = incomeMonthOf(row.period, row.paymentDate);
              return (
                <li key={row.id} className="flex items-start gap-3 border-b border-[var(--line)] px-4 py-2 last:border-b-0">
                  <span className="text-meta w-11 shrink-0 pt-0.5 tabular-nums">{pad(at.getHours())}:{pad(at.getMinutes())}</span>
                  <span className="min-w-0 flex-1">
                    <span className="flex flex-wrap items-center gap-1.5">
                      <span className="rounded-full px-2 py-0.5 text-[.7rem] font-bold" style={{ color: KIND_COLOR[row.kind], background: `color-mix(in srgb, ${KIND_COLOR[row.kind]} 14%, transparent)` }}>{KIND_LABEL[row.kind]}</span>
                      <strong className="truncate text-sm">{row.studentName}</strong>
                    </span>
                    <span className="text-meta block text-[.75rem]">
                      {row.instrumentName} · {COURSE_KIND_LABEL[row.courseKind]} · {formatPeriod(row.period)} aidatı · {detail(row)}
                    </span>
                    <span className="text-meta block text-[.72rem]">
                      {row.kind !== "AmountCorrected" && <>{monthName(incomeMonth)} gelirine yazılır · </>}
                      {row.actorName ?? "Otomatik (banka)"}
                      {row.note && <> · “{row.note}”</>}
                    </span>
                  </span>
                  <span className={`shrink-0 text-sm font-bold tabular-nums ${row.kind === "Received" ? "text-[var(--success-strong)]" : ""}`}>
                    {row.kind === "DateCorrected" ? "" : money(row.amount)}
                  </span>
                </li>
              );
            })}
          </ul>
        </section>
      ))}
    </div>
  );
}
