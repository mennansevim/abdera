"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useEnrollments, useInstruments, useStudents, useTeachers } from "@/lib/people";
import {
  useCreateFeePlan,
  useCreateReceivable,
  useFeePlan,
  useBulkPayment,
  useCorrectPayment,
  usePriceLists,
  useRecordPayment,
  useStudentBilling,
  type PaymentMethod,
  type PaymentRecord,
  type Receivable,
  type StudentBillingRow,
} from "@/lib/billing";

export function StudentBillingSection({ initialStudentId = "", showStudentPicker = true, onClose }: { initialStudentId?: string; showStudentPicker?: boolean; onClose?: () => void }) {
  const { data: students } = useStudents();
  const [studentId, setStudentId] = useState(initialStudentId);
  const { data: enrollments } = useEnrollments(studentId);
  const { data: teachers } = useTeachers();
  const { data: billing } = useStudentBilling(studentId);
  const { data: instruments } = useInstruments();
  const { data: priceLists } = usePriceLists();
  const activeEnrollments = enrollments?.filter((enrollment) => enrollment.status === "Active") ?? [];

  return (
    <section className="app-card overflow-hidden">
      <div className="flex flex-wrap items-end justify-between gap-4 border-b border-[var(--line)] bg-[var(--surface-muted)]/45 p-4 sm:p-5">
        <div><p className="text-micro text-[var(--brand-strong)]">Öğrenci hesabı</p><h2 className="mt-1 text-title">Hesap ayrıntısı</h2><p className="text-meta mt-1">Dönem aidatları, ödemeler ve ücret planı.</p></div>
        <div className="flex w-full items-end gap-2 sm:w-auto">{showStudentPicker ? <label className="min-w-0 flex-1 space-y-1.5 sm:w-72"><span className="text-[.68rem] font-bold text-[var(--muted)]">Öğrenci</span><select value={studentId} onChange={(e) => setStudentId(e.target.value)} className="field min-h-11 text-sm">
          <option value="">Öğrenci seçin…</option>
          {students?.map((s) => (<option key={s.id} value={s.id}>{s.firstName} {s.lastName}</option>))}
        </select></label> : <p className="text-meta">Seçilen öğrencinin ücret planı ve dönem aidatları</p>}{onClose && <button type="button" onClick={onClose} className="pressable grid h-11 w-11 shrink-0 place-items-center rounded-xl border border-[var(--line)] bg-white text-[var(--muted)]" aria-label="Hesap ayrıntısını kapat"><Icon name="close" className="h-4 w-4" /></button>}</div>
      </div>

      {!studentId && <div className="grid min-h-56 place-items-center p-8 text-center"><div><span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-[var(--brand-soft)] text-xl" aria-hidden="true">₺</span><p className="mt-4 text-sm font-bold">Öğrenci hesabı seçilmedi</p><p className="text-meta mt-1">Borç, tahsilat ve ödeme geçmişi burada gösterilecek.</p></div></div>}

      <div className="space-y-4 p-4 sm:p-5">
      {studentId && <StudentAccountHistory billing={billing} instruments={instruments ?? []} />}
      {studentId && activeEnrollments.map((enrollment) => (
        <EnrollmentBillingCard
          key={enrollment.id}
          studentId={studentId}
          enrollmentId={enrollment.id}
          instrumentName={instruments?.find((i) => i.id === enrollment.instrumentId)?.name ?? "?"}
          teacherName={teachers?.find((teacher) => teacher.id === enrollment.teacherId) ? `${teachers.find((teacher) => teacher.id === enrollment.teacherId)!.firstName} ${teachers.find((teacher) => teacher.id === enrollment.teacherId)!.lastName}` : "Öğretmen atanmadı"}
          receivables={billing?.find((b) => b.enrollmentId === enrollment.id)?.receivables ?? []}
          priceListItems={(priceLists ?? []).flatMap((l) => l.items).filter((i) => i.instrumentId === enrollment.instrumentId)}
        />
      ))}
      {studentId && activeEnrollments.length === 0 && <p className="rounded-xl bg-[var(--surface-muted)] p-4 text-sm text-[var(--muted)]">Bu öğrencinin aktif kaydı bulunmuyor.</p>}
      </div>
    </section>
  );
}

function StudentAccountHistory({ billing, instruments }: { billing: StudentBillingRow[] | undefined; instruments: { id: string; name: string }[] }) {
  const periods = (billing ?? []).flatMap((row) => row.receivables.map((receivable) => ({
    ...receivable,
    instrumentName: instruments.find((instrument) => instrument.id === row.instrumentId)?.name ?? "Ders",
  }))).sort((a, b) => b.period.localeCompare(a.period) || a.instrumentName.localeCompare(b.instrumentName, "tr-TR"));

  const statusLabel: Record<Receivable["status"], string> = {
    Unpaid: "Ödenmedi", Partial: "Kısmi ödendi", Paid: "Ödendi", Overdue: "Vadesi geçti", Cancelled: "İptal",
  };
  const statusTone: Record<Receivable["status"], string> = {
    Unpaid: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
    Partial: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
    Paid: "bg-[var(--success-soft)] text-[var(--success-strong)]",
    Overdue: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
    Cancelled: "bg-[var(--surface-muted)] text-[var(--muted)]",
  };

  return <article className="overflow-hidden rounded-2xl border border-[var(--line)] bg-white">
    <div className="flex items-center justify-between gap-3 border-b border-[var(--line)] bg-[var(--brand-soft)]/35 p-4"><div><p className="text-micro text-[var(--brand-strong)]">Dönem takibi</p><h3 className="mt-1 text-title">Dönem aidatları</h3><p className="text-meta mt-1">Tanımlı aylar ve ödeme durumları.</p></div><span className="rounded-full bg-white px-2.5 py-1 text-[.65rem] font-bold text-[var(--brand-strong)]">{periods.length} dönem</span></div>
    <div className="divide-y divide-[var(--line)]">
      {periods.map((receivable) => {
        const remaining = Math.max(0, receivable.amount - receivable.totalPaid);
        const latestPayment = [...receivable.payments].sort((a, b) => b.paymentDate.localeCompare(a.paymentDate))[0];
        return <div key={receivable.id} className="flex flex-wrap items-center gap-3 p-3.5 sm:px-4"><span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--brand-strong)]"><Icon name="calendar" className="h-4 w-4" /></span><span className="min-w-0 flex-1"><span className="block text-xs font-bold capitalize">{new Date(`${receivable.period}-01T00:00:00`).toLocaleDateString("tr-TR", { month: "long", year: "numeric" })}</span><span className="mt-0.5 block truncate text-[.62rem] text-[var(--muted)]">{receivable.instrumentName} · Vade {receivable.dueDate}{latestPayment ? ` · ${latestPayment.paymentDate} tarihinde ödendi` : ""}</span></span><span className="text-right"><strong className="block text-xs tabular-nums">{receivable.amount.toLocaleString("tr-TR")} {receivable.currency}</strong><span className="mt-0.5 block text-[.62rem] tabular-nums text-[var(--muted)]">{remaining ? `${remaining.toLocaleString("tr-TR")} kaldı` : "Tamamı ödendi"}</span></span><span className={`rounded-full px-2 py-1 text-[.58rem] font-bold ${statusTone[receivable.status]}`}>{statusLabel[receivable.status]}</span></div>;
      })}
      {!periods.length && <div className="grid min-h-40 place-items-center p-6 text-center"><div><span className="mx-auto grid h-10 w-10 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="wallet" className="h-4 w-4" /></span><p className="mt-3 text-xs font-bold">Tanımlı dönem bulunmuyor</p><p className="text-meta mt-1">Aidat oluşturulduğunda aylar burada listelenecek.</p></div></div>}
    </div>
  </article>;
}

function EnrollmentBillingCard({
  studentId,
  enrollmentId,
  instrumentName,
  teacherName,
  receivables,
  priceListItems,
}: {
  studentId: string;
  enrollmentId: string;
  instrumentName: string;
  teacherName: string;
  receivables: Receivable[];
  priceListItems: { id: string; durationMinutes: number; billingType: string; amount: number; currency: string }[];
}) {
  const { data: feePlan, isLoading: feePlanLoading } = useFeePlan(enrollmentId);
  const createFeePlan = useCreateFeePlan(enrollmentId);
  const createReceivable = useCreateReceivable(studentId);
  const bulkPayment = useBulkPayment(studentId, enrollmentId);
  const [error, setError] = useState<string | null>(null);

  return (
    <div className="rounded-2xl border border-[var(--line)] bg-white p-4 sm:p-5">
      <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2"><h3 className="font-serif text-base font-bold italic">{instrumentName}</h3><span className="text-meta">{teacherName}</span></div>

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
            {receivable.payments.map((payment) => <PaymentHistoryRow key={payment.id} studentId={studentId} payment={payment} currency={receivable.currency} />)}
          </div>
        </details>
      )}
    </div>
  );
}

function PaymentHistoryRow({ studentId, payment, currency }: { studentId: string; payment: PaymentRecord; currency: string }) {
  const correctPayment = useCorrectPayment(studentId);
  const [editing, setEditing] = useState(false);
  const [correctedAmount, setCorrectedAmount] = useState(payment.amount);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await correctPayment.mutateAsync({ paymentId: payment.id, correctedAmount, reason });
      setEditing(false);
      setReason("");
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme düzeltilemedi.");
    }
  }

  if (payment.kind === "Correction") {
    return <div className="rounded-lg border border-[var(--warning-soft)] bg-white px-2.5 py-2"><div className="flex flex-wrap justify-between gap-2"><span><strong>Düzeltme</strong> · {payment.paymentDate}{payment.note ? ` · ${payment.note}` : ""}</span><strong>{payment.previousAmount?.toLocaleString("tr-TR")} → {payment.amount.toLocaleString("tr-TR")} {currency}</strong></div></div>;
  }

  return <div className="rounded-lg bg-white px-2.5 py-2">
    <div className="flex flex-wrap items-center justify-between gap-2"><span>{payment.paymentDate} · {payment.method === "Transfer" ? "Havale" : payment.method === "Cash" ? "Nakit" : payment.method === "Card" ? "Kart" : "Diğer"}</span><span className="flex items-center gap-2"><strong>{payment.amount.toLocaleString("tr-TR")} {currency}</strong><button type="button" onClick={() => setEditing((value) => !value)} className="font-bold text-[var(--brand)]">Düzelt</button></span></div>
    {editing && <form onSubmit={submit} className="mt-2 grid gap-2 rounded-lg bg-[var(--surface-muted)] p-2 sm:grid-cols-[7rem_1fr_auto]"><input type="number" min={0} step={0.01} value={correctedAmount} onChange={(event) => setCorrectedAmount(Number(event.target.value))} aria-label="Düzeltilen ödeme tutarı" className="field min-h-9 text-xs" /><input value={reason} onChange={(event) => setReason(event.target.value)} required placeholder="Düzeltme nedeni" className="field min-h-9 text-xs" /><button disabled={correctPayment.isPending} className="pressable min-h-9 rounded-lg bg-[var(--brand)] px-3 text-xs font-bold text-white disabled:opacity-50">{correctPayment.isPending ? "Kaydediliyor…" : "Düzeltmeyi kaydet"}</button>{error && <p role="alert" className="text-[var(--danger-strong)] sm:col-span-3">{error}</p>}</form>}
  </div>;
}
