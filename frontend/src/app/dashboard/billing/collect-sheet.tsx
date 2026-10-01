"use client";

import { useMemo, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { FormMessage, Modal } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  COURSE_KIND_LABEL,
  useBillingBoard,
  useBillingPolicy,
  useCollect,
  useCollectionPreview,
  useCorrectPayment,
  useStudentBilling,
  useUpdateEnrollmentBilling,
  type CourseKind,
  type BoardRow,
  type DiscountChoice,
  type PaymentMethod,
  type PaymentRecord,
} from "@/lib/billing";
import { formatMoney, formatPeriod, todayInput } from "@/lib/billing-format";
import { MONTHS_SHORT, cellState, CELL_LABEL, isSelectable, type CellState } from "./board-model";

// Aidatlar ekranının TEK ödeme penceresi (docs/10-decisions.md H17). Liste ve Çizelge aynı
// pencereyi açar: aylar → isteğe bağlı indirimler → tahsil et; altında öğrencinin geçmişi.
// Toplu ödeme ayrı bir akış değil, birden fazla ay seçmek. Tutarı sunucu hesaplar
// (collection-preview); düğme yalnızca sunucunun gösterdiği toplamı teyit ederek tahsil eder.
export function CollectSheet({ row, year, initialPeriods, onClose, onCollected }: {
  row: BoardRow;
  year: number;
  initialPeriods: string[];
  onClose: () => void;
  onCollected: (message: string) => void;
}) {
  const [sheetYear, setSheetYear] = useState(year);
  const [selected, setSelected] = useState<string[]>(initialPeriods);
  const [choice, setChoice] = useState<DiscountChoice>({
    sibling: row.siblingDiscount,
    multiCourse: row.multiCourse,
    prepay: true,
    manualPercent: row.manualDiscountPercent,
  });
  const [manualText, setManualText] = useState(row.manualDiscountPercent ? String(row.manualDiscountPercent) : "");
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [paymentDate, setPaymentDate] = useState(todayInput);
  // "Farklı tutar alındı": velinin verdiği tutar hesaplanandan azsa iki anlamı var - kalanı
  // borç kalır (kısmi ödeme, yalnızca tek ay) ya da aylar ödendi sayılır (küsürat, M2).
  const [agreedText, setAgreedText] = useState("");
  const [showAgreed, setShowAgreed] = useState(false);
  const [keepRestOwed, setKeepRestOwed] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Pencere başka bir yıla geçince o yılın satırı gerekir (Aralık + Ocak peşin gibi).
  const { data: otherYear } = useBillingBoard(sheetYear);
  const yearRow = sheetYear === year ? row : otherYear?.find((item) => item.enrollmentId === row.enrollmentId);
  const { data: policy } = useBillingPolicy();
  const periods = useMemo(() => [...selected].sort(), [selected]);
  const preview = useCollectionPreview(row.enrollmentId, periods, choice);
  const quote = periods.length ? preview.data : undefined;
  const collect = useCollect();

  const firstTier = policy?.prepayTiers.slice().sort((a, b) => a.minMonths - b.minMonths)[0];
  const discountTotal = quote ? quote.rows.filter((r) => !r.blockedReason).reduce((sum, r) => sum + (r.baseAmount - r.amount), 0) : 0;
  const typed = showAgreed && agreedText.trim() ? Number(agreedText.replace(",", ".")) : null;
  const agreed = quote && typed !== null && Number.isFinite(typed) && typed !== quote.total ? typed : null;
  const partialPossible = periods.length === 1 && !!quote && agreed !== null && agreed > 0 && agreed < quote.total;
  const partial = partialPossible && keepRestOwed;
  const busy = collect.isPending || preview.isFetching;
  const canSubmit = !!quote && quote.rows.length > 0 && quote.blockers.length === 0 && quote.total > 0 && !busy
    && (typed === null || (Number.isFinite(typed) && typed > 0 && typed <= quote.total));

  function toggle(period: string) {
    setError(null);
    setSelected((current) => current.includes(period) ? current.filter((item) => item !== period) : [...current, period]);
  }

  function setManual(value: string) {
    setManualText(value);
    const parsed = Number(value.replace(",", "."));
    setChoice((current) => ({ ...current, manualPercent: value.trim() && Number.isFinite(parsed) && parsed > 0 ? Math.min(parsed, 100) : null }));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!quote) return;
    setError(null);
    try {
      const result = await collect.mutateAsync({
        enrollmentId: row.enrollmentId,
        periods,
        discounts: choice,
        paymentDate,
        method,
        expectedTotal: quote.total,
        agreedTotal: agreed !== null && !partial ? agreed : undefined,
        partialAmount: partial ? agreed! : undefined,
      });
      onCollected(`${row.studentName}: ${formatMoney(result.total, result.currency)} tahsil edildi`);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Tahsilat kaydedilemedi.");
    }
  }

  return <Modal open title={row.studentName} description={`${row.instrumentName} · ${row.teacherName} · ${COURSE_KIND_LABEL[row.courseKind]}`} onClose={onClose}>
    <form onSubmit={submit} className="space-y-4">
      <section className="space-y-2" aria-label="Aylar">
        <div className="flex items-center justify-between gap-2">
          <p className="text-micro text-[var(--muted)]">Hangi aylar?</p>
          <div className="flex items-center gap-1" role="group" aria-label="Yıl">
            <button type="button" onClick={() => setSheetYear((y) => y - 1)} className="icon-btn icon-btn-quiet !h-8 !w-8" aria-label="Önceki yıl"><Icon name="arrow-left" className="h-3.5 w-3.5" /></button>
            <strong className="min-w-12 text-center text-sm tabular-nums">{sheetYear}</strong>
            <button type="button" onClick={() => setSheetYear((y) => y + 1)} className="icon-btn icon-btn-quiet !h-8 !w-8" aria-label="Sonraki yıl"><Icon name="arrow-right" className="h-3.5 w-3.5" /></button>
          </div>
        </div>
        <div className="grid grid-cols-4 gap-1.5 sm:grid-cols-6">
          {MONTHS_SHORT.map((label, index) => {
            const period = `${sheetYear}-${String(index + 1).padStart(2, "0")}`;
            const state: CellState = yearRow ? cellState(yearRow, period) : "none";
            const on = selected.includes(period);
            const disabled = !isSelectable(state);
            return <button
              key={period}
              type="button"
              disabled={disabled}
              aria-pressed={on}
              aria-label={`${formatPeriod(period)}: ${CELL_LABEL[state]}`}
              onClick={() => toggle(period)}
              className={`pressable flex min-h-12 flex-col items-center justify-center rounded-xl border-[1.5px] px-1 py-1.5 ${chipClass(state, on)}`}
            >
              <span className="text-sm font-bold">{label}</span>
              <span className={`text-[.68rem] font-bold ${on ? "" : chipTextClass(state)}`}>{CELL_LABEL[state]}</span>
            </button>;
          })}
        </div>
      </section>

      <section className="space-y-2" aria-label="İndirimler">
        <p className="text-micro text-[var(--muted)]">İndirimler <span className="font-semibold normal-case tracking-normal">· isteğe bağlı</span></p>
        <div className="divide-y divide-[var(--line)] overflow-hidden rounded-xl border border-[var(--line)] bg-white">
          <Toggle
            label={`Kardeş indirimi %${trimPercent(quote?.defaults.siblingPercent ?? 0)}`}
            hint={row.siblingDiscount ? "Öğrenci kaydında kardeş işaretli" : "Öğrenci kaydında kardeş işaretli değil"}
            on={choice.sibling}
            onChange={(sibling) => setChoice((current) => ({ ...current, sibling }))}
          />
          <Toggle
            label={`Çoklu kurs indirimi %${trimPercent(quote?.defaults.multiCoursePercent ?? 0)}`}
            hint={row.multiCourse ? "Birden fazla kursa gidiyor · kardeşle birlikteyse yüksek olan" : "Tek kursa gidiyor"}
            on={choice.multiCourse}
            onChange={(multiCourse) => setChoice((current) => ({ ...current, multiCourse }))}
          />
          <Toggle
            label={quote?.prepayPercent ? `Peşin ödeme %${trimPercent(quote.prepayPercent)}` : "Peşin ödeme"}
            hint={quote?.prepayPercent ? "Diğer indirimin üstüne eklenir" : firstTier ? `${firstTier.minMonths} veya daha fazla ay seçince %${trimPercent(firstTier.percent)}` : "Fiyat politikasında kademe yok"}
            on={choice.prepay}
            onChange={(prepay) => setChoice((current) => ({ ...current, prepay }))}
          />
          <label className="flex items-center gap-3 px-3 py-2.5">
            <span className="min-w-0 flex-1"><span className="block text-sm font-semibold">Özel indirim</span><span className="text-meta block">Doluysa kardeş ve çoklu kurs yerine geçer</span></span>
            <span className="flex items-center gap-1 text-sm font-bold text-[var(--muted)]">%<input inputMode="decimal" value={manualText} onChange={(event) => setManual(event.target.value)} placeholder="0" aria-label="Özel indirim yüzdesi" className="field !min-h-9 w-16 text-right text-sm" /></span>
          </label>
        </div>
      </section>

      <div className="grid gap-2 sm:grid-cols-[1fr_auto]">
        <div role="group" aria-label="Ödeme şekli" className="grid grid-cols-3 gap-1 rounded-xl bg-[var(--surface-muted)] p-1">
          {([["Cash", "Nakit"], ["Transfer", "Havale"], ["Card", "Kart"]] as const).map(([value, label]) =>
            <button key={value} type="button" aria-pressed={method === value} onClick={() => setMethod(value)} className={`pressable min-h-9 rounded-lg text-sm font-bold ${method === value ? "bg-white text-[var(--foreground)] shadow-sm" : "text-[var(--muted)]"}`}>{label}</button>)}
        </div>
        <label className="flex items-center gap-2 text-sm font-semibold text-[var(--muted)]">Tarih
          <input type="date" value={paymentDate} onChange={(event) => setPaymentDate(event.target.value)} required className="field !min-h-10 text-sm" />
        </label>
      </div>

      <section className="space-y-2 rounded-xl border border-[var(--line)] bg-white p-3" aria-live="polite">
        {!periods.length && <p className="text-meta">Tahsil edilecek ayları yukarıdan seç.</p>}
        {!!periods.length && !quote && <p className="text-meta">Hesaplanıyor…</p>}
        {quote && <>
          <ul className="space-y-1 text-sm tabular-nums">
            {quote.rows.map((item) => <li key={item.period} className="flex items-baseline justify-between gap-3">
              <span className="min-w-0">
                <span className="font-semibold">{formatPeriod(item.period)}</span>
                {item.blockedReason && <span className="ml-1.5 text-xs font-bold text-[var(--danger-strong)]">{item.blockedReason}</span>}
                {!item.blockedReason && item.alreadyPaid > 0 && <span className="text-meta ml-1.5">kalan</span>}
                {!item.blockedReason && item.discountReason && <span className="text-meta block truncate text-xs">{item.discountReason}</span>}
              </span>
              <span className="shrink-0 font-semibold">{formatMoney(item.due, quote.currency)}</span>
            </li>)}
          </ul>
          <div className="space-y-0.5 border-t border-dashed border-[var(--line)] pt-2 text-sm tabular-nums">
            {discountTotal > 0 && <div className="flex justify-between text-[var(--success-strong)]"><span>İndirim</span><span>−{formatMoney(discountTotal, quote.currency)}</span></div>}
            <div className="flex justify-between text-base font-extrabold"><span>Toplam</span><span>{formatMoney(agreed ?? quote.total, quote.currency)}</span></div>
            {agreed !== null && <p className="text-meta text-xs">Hesaplanan {formatMoney(quote.total, quote.currency)}{partial ? `; ${formatMoney(quote.total - agreed, quote.currency)} borç olarak kalır.` : "; aylar ödendi sayılır, fark indirim olarak yazılır."}</p>}
          </div>
          {!showAgreed && quote.blockers.length === 0 && <button type="button" onClick={() => { setShowAgreed(true); setAgreedText(String(quote.total)); }} className="text-xs font-bold text-[var(--brand-strong)] underline underline-offset-2">Farklı tutar alındı</button>}
          {showAgreed && <div className="space-y-2">
            <label className="form-label">Alınan tutar
              <input inputMode="decimal" value={agreedText} onChange={(event) => setAgreedText(event.target.value)} className="field text-sm" />
            </label>
            {agreed !== null && agreed < quote.total && <div role="radiogroup" aria-label="Eksik kalan tutar" className="grid gap-1.5 text-sm">
              <label className={`flex items-center gap-2 ${periods.length === 1 ? "" : "opacity-50"}`}>
                <input type="radio" name="rest" checked={partialPossible && keepRestOwed} disabled={periods.length !== 1} onChange={() => setKeepRestOwed(true)} />
                <span>Kalanı borç kalsın <span className="text-meta">(kısmi ödeme{periods.length === 1 ? "" : ", yalnızca tek ayda"})</span></span>
              </label>
              <label className="flex items-center gap-2">
                <input type="radio" name="rest" checked={!partial} onChange={() => setKeepRestOwed(false)} />
                <span>Ödendi sayılsın <span className="text-meta">(küsürat indirimi)</span></span>
              </label>
            </div>}
            {agreed !== null && agreed > quote.total && <p className="text-xs font-semibold text-[var(--danger-strong)]">Hesaplanandan fazla alınamaz; tarife toplamını aşamaz.</p>}
          </div>}
        </>}
      </section>

      {error && <FormMessage tone="error">{error}</FormMessage>}

      <button type="submit" disabled={!canSubmit} className="btn btn-primary w-full !min-h-12 text-base tabular-nums">
        {collect.isPending ? "Kaydediliyor…" : quote && periods.length ? `${formatMoney(agreed ?? quote.total, quote.currency)} tahsil et` : "Önce ay seç"}
      </button>

    </form>

    <div className="mt-4 space-y-4">
      <PaymentHistory row={row} />

      <details className="rounded-xl border border-[var(--line)]">
        <summary className="cursor-pointer px-3 py-2.5 text-sm font-bold">Kayıt ayarları <span className="text-meta font-medium">· ders türü, kalıcı özel indirim</span></summary>
        <div className="px-3 pb-3">
          <EnrollmentDiscountBlock
            studentId={row.studentId}
            enrollmentId={row.enrollmentId}
            label={`${row.instrumentName} · ${row.teacherName}`}
            courseKind={row.courseKind}
            manualDiscountPercent={row.manualDiscountPercent}
            manualDiscountReason={row.manualDiscountReason}
          />
        </div>
      </details>
    </div>
  </Modal>;
}

function Toggle({ label, hint, on, onChange }: { label: string; hint: string; on: boolean; onChange: (value: boolean) => void }) {
  return <button type="button" role="switch" aria-checked={on} onClick={() => onChange(!on)} className="flex w-full items-center gap-3 px-3 py-2.5 text-left">
    <span className="min-w-0 flex-1"><span className="block text-sm font-semibold">{label}</span><span className="text-meta block">{hint}</span></span>
    <span className={`relative h-6 w-10 shrink-0 rounded-full transition-colors ${on ? "bg-[var(--success)]" : "bg-[var(--line)]"}`}>
      <span className={`absolute top-[3px] h-[18px] w-[18px] rounded-full bg-white shadow transition-transform ${on ? "translate-x-[19px]" : "translate-x-[3px]"}`} />
    </span>
  </button>;
}

// Seçili olmayan kutuda renkli çerçeve yok - tek dolu turuncu kutu "seçili" demek. Durum alt
// yazının renginde (kullanıcı geri bildirimi: turuncu çerçeveli "bu ay" seçili sanılıyordu).
function chipClass(state: CellState, on: boolean) {
  if (on) return "border-[var(--brand-strong)] bg-[var(--brand-strong)] text-white";
  if (state === "paid") return "border-transparent bg-[var(--success-soft)] text-[var(--foreground)]";
  if (state === "none" || state === "cancelled") return "border-dashed border-[var(--line)] bg-transparent text-[var(--muted)] opacity-60";
  return "border-[var(--line)] bg-white text-[var(--foreground)]";
}

function chipTextClass(state: CellState) {
  switch (state) {
    case "paid": return "text-[var(--success-strong)]";
    case "late": return "text-[var(--danger-strong)]";
    case "partial": return "text-[var(--warning-strong)]";
    case "current": return "text-[var(--foreground)]";
    default: return "text-[var(--muted)]";
  }
}

function trimPercent(value: number) {
  return value.toLocaleString("tr-TR", { maximumFractionDigits: 2 });
}

export function methodLabel(method: PaymentMethod) {
  return method === "Transfer" ? "Havale" : method === "Cash" ? "Nakit" : method === "Card" ? "Kart" : "Diğer";
}

// Öğrencinin bu kurstaki ödemeleri, yeniden eskiye. Yanlış kaydedilen ödeme buradan geri
// alınır: kayıt silinmez, gerekçeli düzeltmeyle 0'a çekilir (CLAUDE.md).
function PaymentHistory({ row }: { row: BoardRow }) {
  const { data: billing, isLoading } = useStudentBilling(row.studentId);
  const [revertTarget, setRevertTarget] = useState<{ payment: PaymentRecord; period: string; currency: string } | null>(null);

  const items = useMemo(() => {
    const course = billing?.find((item) => item.enrollmentId === row.enrollmentId);
    return (course?.receivables ?? [])
      .flatMap((receivable) => receivable.payments
        .filter((payment) => payment.kind === "Payment")
        .map((payment) => ({ payment, receivable, effective: effectiveAmount(receivable.payments, payment) })))
      .sort((a, b) => (b.payment.recordedAt ?? b.payment.paymentDate).localeCompare(a.payment.recordedAt ?? a.payment.paymentDate));
  }, [billing, row.enrollmentId]);

  return <section className="space-y-2 border-t border-[var(--line)] pt-4" aria-label="Geçmiş ödemeler">
    <p className="text-micro text-[var(--muted)]">Geçmiş ödemeler</p>
    {isLoading && <p className="text-meta">Yükleniyor…</p>}
    {!isLoading && !items.length && <p className="text-meta">Bu kursta henüz ödeme yok.</p>}
    <ul className="divide-y divide-[var(--line)]">
      {items.slice(0, 24).map(({ payment, receivable, effective }) => <li key={payment.id} className="flex items-center gap-3 py-2 text-sm">
        <span className="min-w-0 flex-1">
          <span className="block font-semibold">{formatPeriod(receivable.period)}{payment.prepayPlanMonths ? <span className="text-meta ml-1.5 text-xs">· {payment.prepayPlanMonths} aylık ödemenin parçası</span> : null}</span>
          <span className="text-meta block text-xs">{new Date(`${payment.paymentDate}T00:00:00`).toLocaleDateString("tr-TR")} · {methodLabel(payment.method)}{effective === 0 ? " · geri alındı" : ""}</span>
        </span>
        <strong className={`shrink-0 tabular-nums ${effective === 0 ? "text-[var(--muted)] line-through" : ""}`}>{formatMoney(payment.amount, receivable.currency)}</strong>
        {effective > 0 && receivable.status !== "Cancelled" && <button type="button" onClick={() => setRevertTarget({ payment, period: receivable.period, currency: receivable.currency })} className="icon-btn icon-btn-quiet !h-8 !w-8 shrink-0" aria-label={`${formatPeriod(receivable.period)} ödemesini geri al`} title="Ödemeyi geri al"><Icon name="swap" className="h-3.5 w-3.5" /></button>}
      </li>)}
    </ul>
    {revertTarget && <RevertPaymentDialog studentId={row.studentId} target={revertTarget} onClose={() => setRevertTarget(null)} />}
  </section>;
}

// Bir ödemenin bugünkü geçerli tutarı: en son düzeltme (varsa) özgün tutarın yerine geçer.
function effectiveAmount(rows: PaymentRecord[], payment: PaymentRecord) {
  const corrections = rows
    .filter((row) => row.kind === "Correction" && row.correctsPaymentId === payment.id)
    .sort((a, b) => (a.recordedAt ?? "").localeCompare(b.recordedAt ?? ""));
  return corrections.at(-1)?.amount ?? payment.amount;
}

function RevertPaymentDialog({ studentId, target, onClose }: {
  studentId: string;
  target: { payment: PaymentRecord; period: string; currency: string };
  onClose: () => void;
}) {
  const correctPayment = useCorrectPayment(studentId);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const { payment } = target;

  async function submit(event: FormEvent) {
    event.preventDefault();
    event.stopPropagation();
    setError(null);
    try {
      await correctPayment.mutateAsync({ paymentId: payment.id, correctedAmount: 0, reason: reason.trim() });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme geri alınamadı.");
    }
  }

  return <Modal open title="Ödemeyi geri al" description={`${formatPeriod(target.period)} aidatı`} onClose={onClose} size="sm">
    <form onSubmit={submit} className="space-y-3">
      <p className="rounded-xl bg-[var(--surface-muted)] px-3 py-2.5 text-sm">
        <strong className="tabular-nums">{formatMoney(payment.amount, target.currency)}</strong> · {payment.paymentDate} · {methodLabel(payment.method)}
      </p>
      <p className="text-meta">Ödeme kaydı silinmez; tutarı sıfıra düzeltilir ve işlem kayıt altına alınır. Ay yeniden ödenmedi durumuna döner.</p>
      {payment.prepayPlanId && <FormMessage tone="error">Bu ödeme {payment.prepayPlanMonths} aylık ödemenin bir parçası. Yalnızca bu ayın payı geri alınır.</FormMessage>}
      <label className="form-label">Geri alma nedeni
        <textarea value={reason} onChange={(event) => setReason(event.target.value)} required maxLength={500} rows={2} autoFocus className="field resize-y" placeholder="Ör. yanlış öğrenciye kaydedildi" />
      </label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-3">
        <button type="button" onClick={onClose} className="btn btn-quiet">Vazgeç</button>
        <button type="submit" disabled={correctPayment.isPending || !reason.trim()} className="btn bg-[var(--danger)] text-white hover:bg-[var(--danger-strong)]">
          {correctPayment.isPending ? "Geri alınıyor…" : "Ödemeyi geri al"}
        </button>
      </div>
    </form>
  </Modal>;
}

// Aidatı etkileyen, gerçekten öğrenci bazında verilen iki karar: bu kurs birebir mi grup
// mu, ve bu kayda özel bir indirim var mı. Elle indirim girildiğinde otomatik kurallar
// (2 kurs / kardeş) devre dışı kalır - admin bilinçli bir karar vermiştir.
function EnrollmentDiscountBlock({
  studentId,
  enrollmentId,
  label,
  courseKind,
  manualDiscountPercent,
  manualDiscountReason,
}: {
  studentId: string;
  enrollmentId: string;
  label: string;
  courseKind: CourseKind;
  manualDiscountPercent: number | null;
  manualDiscountReason: string | null;
}) {
  const updateBilling = useUpdateEnrollmentBilling(studentId);
  const [kind, setKind] = useState<CourseKind>(courseKind);
  const [percent, setPercent] = useState<number | "">(manualDiscountPercent ?? "");
  const [reason, setReason] = useState(manualDiscountReason ?? "");
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);
    try {
      await updateBilling.mutateAsync({
        enrollmentId,
        courseKind: kind,
        manualDiscountPercent: percent === "" ? null : Number(percent),
        manualDiscountReason: percent === "" ? null : reason.trim() || null,
      });
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "İndirim kaydedilemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="rounded-xl border border-[var(--line)] p-4">
      <p className="mb-2 text-xs font-bold">{label}</p>
      <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
        <label className="form-label">Ders türü
          <select value={kind} onChange={(event) => { setKind(event.target.value as CourseKind); setSaved(false); }} className="field min-h-10 text-xs">
            <option value="Individual">{COURSE_KIND_LABEL.Individual}</option>
            <option value="Group">{COURSE_KIND_LABEL.Group}</option>
          </select>
        </label>
        <label className="form-label">Özel indirim (%)
          <input type="number" inputMode="decimal" min={0} max={100} step={0.5} value={percent} onChange={(event) => { setPercent(event.target.value === "" ? "" : Number(event.target.value)); setSaved(false); }} placeholder="Yok" className="field min-h-10 text-xs" />
        </label>
        <label className="form-label lg:col-span-2">Gerekçe
          <input value={reason} onChange={(event) => { setReason(event.target.value); setSaved(false); }} disabled={percent === ""} maxLength={200} placeholder="Örn. Burslu öğrenci" className="field min-h-10 text-xs disabled:opacity-60" />
        </label>
      </div>
      <p className="text-meta mt-2">{percent === "" ? "Otomatik kurallar geçerli (2 kurs / kardeş indirimi)." : `Otomatik kurallar yerine %${percent} uygulanacak.`}</p>
      <div className="mt-2 flex items-center gap-3">
        <button type="submit" disabled={updateBilling.isPending} className="btn btn-primary">{updateBilling.isPending ? "Kaydediliyor…" : "Kaydet"}</button>
        {saved && !error && <span className="text-[.75rem] font-bold text-[var(--success-strong)]">Kaydedildi</span>}
      </div>
      {error && <p className="mt-2 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
    </form>
  );
}
