"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { FormMessage, SectionHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  useBillingPolicy,
  useCreatePrepayPlan,
  usePrepayPreview,
  type PaymentMethod,
  type PrepayResult,
  type PrepayTier,
} from "@/lib/billing";
import { currentPeriod, formatMoney, formatPeriod, isValidDateInput, isValidPeriod, todayInput } from "@/lib/billing-format";
import { useStudentAutocomplete, type StudentSearchResult } from "@/lib/people";

// TOPLU ÖDEME (veliye giden mesajdaki adıyla; kodda PrepayPlan) - birkaç ayın aidatı tek
// seferde, kademeli indirimle tahsil edilir.
//
// Bu ekran daha önce öğrenci künyesinin içinde, açılır bir "Peşin ödeme al" bölümünün
// altında, her kurs kaydı için ayrı bir form olarak duruyordu: kullanıcı onu bulamıyor,
// "normal aidat" ile "toplu ödeme"nin nerede ayrıştığını göremiyordu. Artık tek giriş
// noktası burası ve akış üç adım: ÖĞRENCİYİ BUL -> KAÇ AY -> KAYDET.
//
// Tutarın hiçbir parçası burada hesaplanmaz. Ekran yalnızca sunucunun önizlemesini
// gösterir ve kaydederken gördüğü toplamı (expectedTotal) teyit eder; ayrışma varsa
// sunucu işlemi durdurur (CLAUDE.md "peşin ödeme tutarını sunucu hesaplar").
export function BulkPaymentSection() {
  const [query, setQuery] = useState("");
  const [selected, setSelected] = useState<StudentSearchResult | null>(null);
  const [startPeriod, setStartPeriod] = useState(currentPeriod);
  const [months, setMonths] = useState(3);
  const [paymentDate, setPaymentDate] = useState(todayInput);
  const [method, setMethod] = useState<PaymentMethod>("Transfer");
  const [reference, setReference] = useState("");
  const [result, setResult] = useState<PrepayResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const { data: policy } = useBillingPolicy();
  const search = useStudentAutocomplete(query);
  const { data: preview, isLoading: previewLoading } = usePrepayPreview(
    selected?.enrollmentId ?? "",
    startPeriod,
    months,
    { enabled: !!selected && isValidPeriod(startPeriod) },
  );
  const createPlan = useCreatePrepayPlan(selected?.studentId ?? "", selected?.enrollmentId ?? "");

  const tiers = policy?.prepayTiers ?? [];
  const blocked = (preview?.blockers.length ?? 0) > 0;

  function pick(row: StudentSearchResult) {
    setSelected(row);
    setQuery("");
    setResult(null);
    setError(null);
  }

  function reset() {
    setSelected(null);
    setResult(null);
    setError(null);
    setQuery("");
    setStartPeriod(currentPeriod());
    setPaymentDate(todayInput());
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (!selected) return;
    if (!isValidPeriod(startPeriod) || !isValidDateInput(paymentDate)) {
      setError("Başlangıç ayını ve ödeme tarihini seç.");
      return;
    }
    try {
      setResult(await createPlan.mutateAsync({
        startPeriod,
        months,
        paymentDate,
        method,
        reference: reference.trim() || undefined,
        expectedTotal: preview?.total,
      }));
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Toplu ödeme kaydedilemedi.");
    }
  }

  return (
    <section className="app-card">
      <SectionHeader
        title="Toplu ödeme al"
        description="Birkaç ayın aidatını tek seferde tahsil et. İndirimi ve her ayın tutarını sunucu hesaplar."
      />

      <div className="space-y-4 border-t border-[var(--line)] p-4">
        <RulesStrip tiers={tiers} multiCoursePercent={policy?.multiCourseDiscountPercent ?? 0} siblingPercent={policy?.siblingDiscountPercent ?? 0} />

        {/* 1. adım - öğrenciyi bul. Arama satırları kurs kaydı başına döner, çünkü aidat
            da toplu ödeme de bir kurs kaydına yazılır: aynı öğrencinin piyanosu ve resmi
            ayrı satırlar ve ayrı fiyatlardır. */}
        <div>
          <p className="text-micro text-[var(--muted)]">1 · Öğrenci ve kurs</p>
          {selected ? (
            <div className="mt-1.5 flex items-center justify-between gap-3 rounded-xl border border-[var(--brand)] bg-[var(--brand-soft)] px-3 py-2.5">
              <span className="min-w-0">
                <strong className="block truncate text-sm">{selected.studentName}</strong>
                <span className="text-meta block truncate">{selected.instrumentName} · {selected.courseKind === "Group" ? "Grup" : "Birebir"} · {selected.teacherName}</span>
              </span>
              <button type="button" onClick={reset} className="pressable shrink-0 rounded-lg px-2 py-1 text-xs font-bold text-[var(--brand-strong)]">Değiştir</button>
            </div>
          ) : (
            <>
              <label className="relative mt-1.5 block">
                <span className="sr-only">Öğrenci ara</span>
                <Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--muted)]" />
                <input
                  type="search"
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  placeholder="Öğrenci, öğretmen veya enstrüman adı yaz"
                  className="field min-h-11 pl-9 text-sm"
                />
              </label>
              {query.trim().length >= 2 && (
                <ul className="mt-1.5 max-h-56 divide-y divide-[var(--line)] overflow-y-auto rounded-xl border border-[var(--line)]">
                  {search.isLoading && <li className="px-3 py-2.5 text-xs text-[var(--muted)]">Aranıyor…</li>}
                  {!search.isLoading && !search.data?.length && <li className="px-3 py-2.5 text-xs text-[var(--muted)]">Eşleşen aktif kurs kaydı yok.</li>}
                  {search.data?.map((row) => (
                    <li key={row.enrollmentId}>
                      <button type="button" onClick={() => pick(row)} className="pressable flex w-full items-center justify-between gap-3 px-3 py-2.5 text-left hover:bg-[var(--brand-soft)]">
                        <span className="min-w-0">
                          <strong className="block truncate text-xs">{row.studentName}</strong>
                          <span className="text-meta block truncate">{row.instrumentName} · {row.courseKind === "Group" ? "Grup" : "Birebir"} · {row.teacherName}</span>
                        </span>
                        <Icon name="chevron" className="h-4 w-4 shrink-0 text-[var(--muted)]" />
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              <p className="text-meta mt-1.5">En az 2 harf yaz. Yalnızca aktif kurs kayıtları listelenir.</p>
            </>
          )}
        </div>

        {selected && !result && (
          <form onSubmit={handleSubmit} className="space-y-4">
            {/* 2. adım - kaç ay. Kademeler politikadan gelir; oranlar burada hesaplanmaz. */}
            <div>
              <p className="text-micro text-[var(--muted)]">2 · Kaç ay</p>
              <div className="mt-1.5 flex flex-wrap gap-2">
                {/* minMonths benzersizdir - politika ucu aynı ay sayısı için ikinci kademeyi reddeder. */}
                {tiers.map((tier) => (
                  <button
                    key={tier.minMonths}
                    type="button"
                    onClick={() => { setMonths(tier.minMonths); setError(null); }}
                    aria-pressed={months === tier.minMonths}
                    className={`pressable min-h-11 rounded-xl border px-3 text-sm font-bold ${months === tier.minMonths ? "border-[1.5px] border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "border-[var(--line)] bg-white"}`}
                  >
                    {tier.minMonths} ay <span className="ml-1 text-[.75rem] font-bold text-[var(--success-strong)]">%{tier.percent}</span>
                  </button>
                ))}
                <label className="flex min-h-11 items-center gap-2 rounded-xl border border-[var(--line)] bg-white px-3 text-[.75rem] font-semibold text-[var(--muted)]">
                  Diğer
                  <input
                    type="number"
                    min={1}
                    max={24}
                    value={months}
                    onChange={(event) => { setMonths(Math.max(1, Math.min(24, Math.trunc(Number(event.target.value)) || 1))); setError(null); }}
                    className="w-14 bg-transparent text-sm font-bold tabular-nums text-[var(--foreground)] outline-none"
                    aria-label="Ay sayısı"
                  />
                </label>
              </div>
              {tiers.length === 0 && <p className="text-meta mt-1.5">Kademe tanımlı değil - toplu ödemeye indirim uygulanmaz. Fiyat politikası ekranından ekleyebilirsin.</p>}
            </div>

            {/* 3. adım - tahsilat bilgisi. */}
            <div>
              <p className="text-micro text-[var(--muted)]">3 · Tahsilat</p>
              <div className="mt-1.5 grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
                <label className="form-label">Başlangıç ayı
                  <input type="month" value={startPeriod} onChange={(event) => { setStartPeriod(event.target.value); setError(null); }} required className="field min-h-11 text-sm" />
                </label>
                <label className="form-label">Ödeme tarihi
                  <input type="date" value={paymentDate} onChange={(event) => { setPaymentDate(event.target.value); setError(null); }} required className="field min-h-11 text-sm" />
                </label>
                <label className="form-label">Yöntem
                  <select value={method} onChange={(event) => setMethod(event.target.value as PaymentMethod)} className="field min-h-11 text-sm">
                    <option value="Transfer">Havale</option>
                    <option value="Cash">Nakit</option>
                    <option value="Card">Kart</option>
                    <option value="Other">Diğer</option>
                  </select>
                </label>
                <label className="form-label">Dekont / açıklama
                  <input type="text" value={reference} onChange={(event) => setReference(event.target.value)} placeholder="İsteğe bağlı" className="field min-h-11 text-sm" />
                </label>
              </div>
            </div>

            {previewLoading && <div className="skeleton h-28 rounded-xl" />}

            {preview && !previewLoading && (
              <>
                <dl className="space-y-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/60 p-3.5 text-xs">
                  <div className="flex justify-between gap-2">
                    <dt className="text-[var(--muted)]">Tarife toplamı · {preview.months} ay</dt>
                    <dd className="tabular-nums">{formatMoney(preview.baseTotal, preview.currency)}</dd>
                  </div>
                  {preview.studentDiscountPercent > 0 && (
                    <div className="flex justify-between gap-2">
                      <dt className="text-[var(--muted)]">{preview.studentDiscountReason ?? "Öğrenci indirimi"}</dt>
                      <dd className="tabular-nums text-[var(--success-strong)]">−%{preview.studentDiscountPercent}</dd>
                    </div>
                  )}
                  <div className="flex justify-between gap-2">
                    <dt className="text-[var(--muted)]">Toplu ödeme indirimi</dt>
                    <dd className={`tabular-nums ${preview.prepayPercent > 0 ? "text-[var(--success-strong)]" : "text-[var(--muted)]"}`}>{preview.prepayPercent > 0 ? `−%${preview.prepayPercent}` : "—"}</dd>
                  </div>
                  <div className="flex justify-between gap-2 border-t border-[var(--line)] pt-2">
                    <dt className="text-sm font-bold">Tahsil edilecek</dt>
                    <dd className="text-sm font-bold tabular-nums text-[var(--brand-strong)]">{formatMoney(preview.total, preview.currency)}</dd>
                  </div>
                  {preview.savingTotal > 0 && (
                    <div className="flex justify-between gap-2">
                      <dt className="text-[var(--muted)]">Veli kazancı</dt>
                      <dd className="tabular-nums text-[var(--success-strong)]">{formatMoney(preview.savingTotal, preview.currency)}</dd>
                    </div>
                  )}
                </dl>

                <section className="rounded-xl border border-[var(--line)]">
                  <p className="border-b border-[var(--line)] bg-[var(--surface-muted)]/60 px-3 py-2 text-[.75rem] font-bold text-[var(--muted)]">Kapsanan aylar</p>
                  <ul className="max-h-48 divide-y divide-[var(--line)] overflow-y-auto">
                    {preview.monthRows.map((row) => (
                      <li key={row.period} className="flex items-center justify-between gap-3 px-3 py-2 text-xs">
                        <span className="min-w-0 truncate capitalize">{formatPeriod(row.period)}</span>
                        <span className="flex shrink-0 items-center gap-2">
                          {row.blockedReason && <span className="rounded-full bg-[var(--warning-soft)] px-1.5 py-0.5 text-[.75rem] font-bold text-[var(--warning-strong)]">{row.blockedReason}</span>}
                          {!row.blockedReason && row.alreadyExists && <span className="text-meta">açık aidat</span>}
                          <strong className="tabular-nums">{formatMoney(row.amount, preview.currency)}</strong>
                        </span>
                      </li>
                    ))}
                  </ul>
                </section>
              </>
            )}

            {blocked && (
              <p role="alert" className="rounded-xl bg-[var(--warning-soft)] px-3 py-2.5 text-xs font-semibold text-[var(--warning-strong)]">
                {preview!.blockers.join(" ")} Ödeme görmüş bir ay yeniden fiyatlanamaz - başlangıç ayını ileri al.
              </p>
            )}

            {error && <FormMessage tone="error">{error}</FormMessage>}

            <button type="submit" disabled={createPlan.isPending || blocked || !preview?.total} className="btn btn-primary min-h-12 w-full sm:w-auto">
              {createPlan.isPending ? "Kaydediliyor…" : preview ? `${formatMoney(preview.total, preview.currency)} tahsilatı kaydet` : "Toplu ödemeyi kaydet"}
            </button>
          </form>
        )}

        {result && (
          <div className="space-y-3">
            <FormMessage tone="success">
              {result.months} aylık toplu ödeme kaydedildi · {formatMoney(result.total, result.currency)}
              {result.prepayPercent > 0 && ` · %${result.prepayPercent} toplu ödeme indirimi`}
              {result.savingTotal > 0 && ` · ${formatMoney(result.savingTotal, result.currency)} kazanç`}
            </FormMessage>
            <p className="text-meta">
              {formatPeriod(result.startPeriod)} ayından başlayarak {result.receivables.length} aidat ödenmiş olarak işaretlendi. Tahsilatlar sekmesinde
              bu aylar artık &quot;Ödendi&quot; görünür.
            </p>
            <button type="button" onClick={reset} className="btn btn-quiet">Yeni toplu ödeme</button>
          </div>
        )}
      </div>
    </section>
  );
}

// Kuralları ekranın üstünde, veliye giden mesajdaki dille tekrar eder - "hangi indirim
// nereden geliyor" sorusu için Fiyat politikası sekmesine gitmek gerekmesin.
function RulesStrip({ tiers, multiCoursePercent, siblingPercent }: { tiers: PrepayTier[]; multiCoursePercent: number; siblingPercent: number }) {
  return (
    <div className="rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/60 p-3 text-xs">
      <p className="font-bold">Nasıl hesaplanır</p>
      <ul className="text-meta mt-1 space-y-0.5">
        <li>Aylık tarife ders türüne göredir: Birebir ve Grup için ayrı tutar (enstrüman ve ders süresi fiyatı değiştirmez).</li>
        <li>2 kursa katılan veya kardeşi olan öğrenciye %{Math.max(multiCoursePercent, siblingPercent)} indirim - ikisi birden varsa yüksek olan uygulanır, toplanmaz.</li>
        <li>
          Toplu ödeme indirimi bunun üstüne biner:{" "}
          {tiers.length ? tiers.map((tier) => `${tier.minMonths} ay %${tier.percent}`).join(" · ") : "kademe tanımlı değil"}.
        </li>
      </ul>
    </div>
  );
}
