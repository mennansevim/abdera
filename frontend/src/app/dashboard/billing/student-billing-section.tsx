"use client";

import { useState } from "react";
import { ApiError } from "@/lib/api";
import { useEnrollments, useInstruments, useStudents } from "@/lib/people";
import {
  useCreateFeePlan,
  useCreateReceivable,
  useFeePlan,
  useBulkPayment,
  usePriceLists,
  useRecordPayment,
  useStudentBilling,
  useMakeupCredits,
  type PaymentMethod,
  type Receivable,
} from "@/lib/billing";

export function StudentBillingSection() {
  const { data: students } = useStudents();
  const [studentId, setStudentId] = useState("");
  const { data: enrollments } = useEnrollments(studentId);
  const { data: billing } = useStudentBilling(studentId);
  const { data: instruments } = useInstruments();
  const { data: priceLists } = usePriceLists();

  return (
    <section className="space-y-4">
      <h2 className="text-micro text-[var(--brand-strong)]">Öğrenci Aidatları</h2>

      <select value={studentId} onChange={(e) => setStudentId(e.target.value)} className="field min-h-11 w-full max-w-xs text-sm">
        <option value="">Öğrenci seçin…</option>
        {students?.map((s) => (
          <option key={s.id} value={s.id}>{s.firstName} {s.lastName}</option>
        ))}
      </select>

      {studentId && enrollments?.map((enrollment) => (
        <EnrollmentBillingCard
          key={enrollment.id}
          studentId={studentId}
          enrollmentId={enrollment.id}
          instrumentName={instruments?.find((i) => i.id === enrollment.instrumentId)?.name ?? "?"}
          receivables={billing?.find((b) => b.enrollmentId === enrollment.id)?.receivables ?? []}
          priceListItems={(priceLists ?? []).flatMap((l) => l.items).filter((i) => i.instrumentId === enrollment.instrumentId)}
        />
      ))}
      {studentId && <MakeupCreditsCard studentId={studentId} />}
    </section>
  );
}

function MakeupCreditsCard({ studentId }: { studentId: string }) {
  const { data: credits } = useMakeupCredits(studentId);
  const available = credits?.filter((credit) => credit.status === "Available") ?? [];
  return (
    <section className="app-card p-4 sm:p-5">
      <div className="flex flex-wrap items-center justify-between gap-2"><div><p className="text-micro text-[var(--brand-strong)]">Telafi hakkı</p><h3 className="mt-1 text-title">Öğrencinin telafi dersleri</h3></div><span className="rounded-full bg-[var(--success-soft)] px-2.5 py-1 text-xs font-bold text-[var(--success-strong)]">{available.length} kullanılabilir</span></div>
      <div className="mt-3 space-y-2">
        {available.map((credit) => <div key={credit.id} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-[var(--line)] bg-white px-3 py-2 text-sm"><span>{credit.earnedReason === "SchoolCancelled" ? "Okul iptali" : "24 saatten önce veli iptali"}</span><span className="text-meta">Son kullanım: {new Date(credit.expiresAt).toLocaleDateString("tr-TR")}</span></div>)}
        {!available.length && <p className="text-meta">Kullanılabilir telafi hakkı yok.</p>}
      </div>
    </section>
  );
}

function EnrollmentBillingCard({
  studentId,
  enrollmentId,
  instrumentName,
  receivables,
  priceListItems,
}: {
  studentId: string;
  enrollmentId: string;
  instrumentName: string;
  receivables: Receivable[];
  priceListItems: { id: string; durationMinutes: number; billingType: string; amount: number; currency: string }[];
}) {
  const { data: feePlan, isLoading: feePlanLoading } = useFeePlan(enrollmentId);
  const createFeePlan = useCreateFeePlan(enrollmentId);
  const createReceivable = useCreateReceivable(studentId);
  const bulkPayment = useBulkPayment(studentId, enrollmentId);
  const [error, setError] = useState<string | null>(null);

  return (
    <div className="app-card p-4 sm:p-5">
      <h3 className="mb-2 font-serif text-base font-bold italic">{instrumentName}</h3>

      {!feePlanLoading && !feePlan && (
        <CreateFeePlanForm
          priceListItems={priceListItems}
          onSubmit={async (priceListItemId, dueDay) => {
            setError(null);
            try {
              await createFeePlan.mutateAsync({ priceListItemId, dueDay, activeFrom: new Date().toISOString().slice(0, 10) });
            } catch (err) {
              setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ücret planı oluşturulamadı.");
            }
          }}
        />
      )}

      {feePlan && (
        <>
          <p className="text-meta mb-3">
            {feePlan.billingType === "Monthly" ? "Aylık" : "Paket"} · {feePlan.amount.toLocaleString("tr-TR")} {feePlan.currency}
            {feePlan.dueDay && ` · her ayın ${feePlan.dueDay}. günü`}
          </p>

          <div className="mb-3 divide-y divide-[var(--line)] overflow-hidden rounded-xl border-2 border-[var(--line)]">
            {receivables.map((r) => (
              <ReceivableRow key={r.id} studentId={studentId} receivable={r} />
            ))}
            {receivables.length === 0 && <p className="text-meta px-3 py-3">Henüz aidat kaydı yok.</p>}
          </div>

          <CreateReceivableForm
            onSubmit={async (period) => {
              setError(null);
              try {
                await createReceivable.mutateAsync({ enrollmentId, period });
              } catch (err) {
                setError(err instanceof ApiError ? (err.detail ?? err.title) : "Aidat oluşturulamadı.");
              }
            }}
          />

          {feePlan.billingType === "Monthly" && (
            <BulkPaymentForm
              monthlyAmount={feePlan.amount}
              onSubmit={async (body) => {
                setError(null);
                try {
                  await bulkPayment.mutateAsync(body);
                } catch (err) {
                  setError(err instanceof ApiError ? (err.detail ?? err.title) : "Toplu ödeme kaydedilemedi.");
                }
              }}
            />
          )}
        </>
      )}
      {error && <p className="mt-2 text-sm font-medium text-[var(--danger-strong)]">{error}</p>}
    </div>
  );
}

function CreateFeePlanForm({
  priceListItems,
  onSubmit,
}: {
  priceListItems: { id: string; durationMinutes: number; billingType: string; amount: number; currency: string }[];
  onSubmit: (priceListItemId: string, dueDay?: number) => Promise<void>;
}) {
  const [itemId, setItemId] = useState("");
  const [dueDay, setDueDay] = useState(5);

  if (priceListItems.length === 0) {
    return <p className="text-meta">Bu enstrüman için fiyat listesi kalemi yok.</p>;
  }

  return (
    <form onSubmit={(e) => { e.preventDefault(); onSubmit(itemId, dueDay); }} className="flex flex-wrap items-end gap-2">
      <select value={itemId} onChange={(e) => setItemId(e.target.value)} required
        className="field min-h-10 w-auto text-sm">
        <option value="">Fiyat kalemi seç</option>
        {priceListItems.map((i) => (
          <option key={i.id} value={i.id}>
            {i.durationMinutes} dk · {i.billingType === "Monthly" ? "Aylık" : "Paket"} · {i.amount.toLocaleString("tr-TR")} {i.currency}
          </option>
        ))}
      </select>
      <input type="number" min={1} max={28} value={dueDay} onChange={(e) => setDueDay(Number(e.target.value))}
        className="field min-h-10 w-20 text-sm" title="Vade günü" />
      <button type="submit" className="pressable min-h-10 rounded-lg bg-[var(--brand)] px-3 text-sm font-bold text-white hover:bg-[var(--brand-strong)]">Ücret planı oluştur</button>
    </form>
  );
}

function CreateReceivableForm({ onSubmit }: { onSubmit: (period: string) => Promise<void> }) {
  const [period, setPeriod] = useState(() => new Date().toISOString().slice(0, 7));

  return (
    <form onSubmit={(e) => { e.preventDefault(); onSubmit(period); }} className="flex items-end gap-2">
      <input type="month" value={period} onChange={(e) => setPeriod(e.target.value)} className="field min-h-10 w-auto text-sm" />
      <button type="submit" className="pressable min-h-10 rounded-lg border-2 border-[var(--line)] bg-white px-3 text-sm font-bold hover:bg-[var(--surface-muted)]">
        Aidat oluştur
      </button>
    </form>
  );
}

function BulkPaymentForm({ monthlyAmount, onSubmit }: { monthlyAmount: number; onSubmit: (body: { startPeriod: string; months: number; amount: number; paymentDate: string; method: PaymentMethod }) => Promise<void> }) {
  const [startPeriod, setStartPeriod] = useState(() => new Date().toISOString().slice(0, 7));
  const [months, setMonths] = useState(1);
  const [amount, setAmount] = useState(monthlyAmount);
  const [paymentDate, setPaymentDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [method, setMethod] = useState<PaymentMethod>("Transfer");

  function changeMonths(value: number) {
    setMonths(value);
    setAmount(monthlyAmount * value);
  }

  return (
    <form onSubmit={(event) => { event.preventDefault(); onSubmit({ startPeriod, months, amount, paymentDate, method }); }} className="mt-4 rounded-2xl border border-[var(--brand)]/25 bg-[var(--brand-soft)]/45 p-3.5">
      <div className="mb-2"><p className="text-xs font-bold text-[var(--brand-strong)]">Toplu ödeme al</p><p className="text-meta mt-0.5">10 aylık veya 1 yıllık tahsilat, seçilen aydan başlayarak aidatlara otomatik dağıtılır.</p></div>
      <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-5">
        <label className="space-y-1 text-[.68rem] font-semibold text-[var(--muted)]">Başlangıç<input type="month" value={startPeriod} onChange={(event) => setStartPeriod(event.target.value)} className="field min-h-10 text-xs" /></label>
        <label className="space-y-1 text-[.68rem] font-semibold text-[var(--muted)]">Kaç ay?<select value={months} onChange={(event) => changeMonths(Number(event.target.value))} className="field min-h-10 text-xs"><option value={1}>1 ay</option><option value={3}>3 ay</option><option value={6}>6 ay</option><option value={10}>10 ay</option><option value={12}>12 ay</option></select></label>
        <label className="space-y-1 text-[.68rem] font-semibold text-[var(--muted)]">Toplam tutar<input type="number" min={0.01} step={0.01} value={amount} onChange={(event) => setAmount(Number(event.target.value))} className="field min-h-10 text-xs" /></label>
        <label className="space-y-1 text-[.68rem] font-semibold text-[var(--muted)]">Ödeme tarihi<input type="date" value={paymentDate} onChange={(event) => setPaymentDate(event.target.value)} className="field min-h-10 text-xs" /></label>
        <label className="space-y-1 text-[.68rem] font-semibold text-[var(--muted)]">Yöntem<select value={method} onChange={(event) => setMethod(event.target.value as PaymentMethod)} className="field min-h-10 text-xs"><option value="Transfer">Havale</option><option value="Cash">Nakit</option><option value="Card">Kart</option><option value="Other">Diğer</option></select></label>
      </div>
      <button type="submit" className="pressable mt-3 min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white">Toplu ödemeyi kaydet</button>
    </form>
  );
}

function ReceivableRow({ studentId, receivable }: { studentId: string; receivable: Receivable }) {
  const recordPayment = useRecordPayment(studentId);
  const [showForm, setShowForm] = useState(false);
  const [amount, setAmount] = useState(receivable.amount - receivable.totalPaid);
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [error, setError] = useState<string | null>(null);

  const statusLabel: Record<Receivable["status"], string> = {
    Unpaid: "ödenmedi", Partial: "kısmi", Paid: "ödendi", Overdue: "vadesi geçti", Cancelled: "iptal",
  };
  const statusClass: Record<Receivable["status"], string> = {
    Unpaid: "text-[var(--muted)]",
    Partial: "text-[var(--warning-strong)]",
    Paid: "text-[var(--success-strong)]",
    Overdue: "text-[var(--danger-strong)]",
    Cancelled: "text-[var(--muted)] line-through",
  };

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await recordPayment.mutateAsync({
        receivableId: receivable.id, amount, paymentDate: new Date().toISOString().slice(0, 10), method,
      });
      setShowForm(false);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme kaydedilemedi.");
    }
  }

  return (
    <div className="flex flex-wrap items-center gap-3 bg-white px-3 py-2.5 text-sm">
      <span className="w-24 shrink-0 font-semibold">{receivable.period}</span>
      <span className="text-meta w-32 shrink-0">vade: {receivable.dueDate}</span>
      <span className="w-28 shrink-0 font-semibold">{receivable.amount.toLocaleString("tr-TR")} {receivable.currency}</span>
      <span className={`w-24 shrink-0 font-bold ${statusClass[receivable.status]}`}>{statusLabel[receivable.status]}</span>
      <span className="flex-1">
        {receivable.status !== "Paid" && receivable.status !== "Cancelled" && (
          <>
            <button onClick={() => setShowForm((v) => !v)}
              className="pressable inline-flex min-h-9 items-center text-sm font-bold text-[var(--brand)] hover:text-[var(--brand-strong)]">
              Ödeme al
            </button>
            {showForm && (
              <form onSubmit={handleSubmit} className="mt-1.5 flex flex-wrap items-center gap-1.5">
                <input type="number" step={0.01} value={amount} onChange={(e) => setAmount(Number(e.target.value))}
                  className="field min-h-9 w-24 text-xs" />
                <select value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}
                  className="field min-h-9 w-auto text-xs">
                  <option value="Cash">Nakit</option>
                  <option value="Transfer">Havale</option>
                  <option value="Card">Kart</option>
                  <option value="Other">Diğer</option>
                </select>
                <button type="submit" className="pressable min-h-9 rounded-lg bg-[var(--brand)] px-2.5 text-xs font-bold text-white">Kaydet</button>
              </form>
            )}
            {error && <p className="mt-1 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
          </>
        )}
      </span>
      {receivable.payments.length > 0 && (
        <details className="w-full rounded-lg bg-[var(--surface-muted)] px-3 py-2 text-xs">
          <summary className="cursor-pointer font-bold text-[var(--brand-strong)]">Ödeme geçmişi · {receivable.payments.length} kayıt</summary>
          <div className="mt-2 space-y-1.5">
            {receivable.payments.map((payment) => <div key={payment.id} className="flex flex-wrap justify-between gap-2"><span>{payment.paymentDate} · {payment.method === "Transfer" ? "Havale" : payment.method === "Cash" ? "Nakit" : payment.method === "Card" ? "Kart" : "Diğer"}</span><strong>{payment.amount.toLocaleString("tr-TR")} {receivable.currency}</strong></div>)}
          </div>
        </details>
      )}
    </div>
  );
}
