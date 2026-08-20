"use client";

import { useState } from "react";
import { ApiError } from "@/lib/api";
import { useEnrollments, useInstruments, useStudents } from "@/lib/people";
import {
  useCreateFeePlan,
  useCreateReceivable,
  useFeePlan,
  usePriceLists,
  useRecordPayment,
  useStudentBilling,
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
      <h2 className="text-lg font-semibold">Öğrenci Aidatları</h2>

      <select value={studentId} onChange={(e) => setStudentId(e.target.value)}
        className="rounded-md border border-neutral-300 px-2 py-1.5 text-sm">
        <option value="">Öğrenci seç</option>
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
  const [error, setError] = useState<string | null>(null);

  return (
    <div className="rounded-lg border border-neutral-200 bg-white p-4">
      <h3 className="mb-2 font-medium">{instrumentName}</h3>

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
          <p className="mb-2 text-sm text-neutral-500">
            {feePlan.billingType === "Monthly" ? "Aylık" : "Paket"} · {feePlan.amount.toLocaleString("tr-TR")} {feePlan.currency}
            {feePlan.dueDay && ` · her ayın ${feePlan.dueDay}. günü`}
          </p>

          <div className="mb-3 overflow-x-auto rounded-lg border border-neutral-100">
            <table className="w-full text-sm">
              <tbody>
                {receivables.map((r) => (
                  <ReceivableRow key={r.id} studentId={studentId} receivable={r} />
                ))}
              </tbody>
            </table>
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
        </>
      )}
      {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
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
    return <p className="text-sm text-neutral-400">Bu enstrüman için fiyat listesi kalemi yok.</p>;
  }

  return (
    <form onSubmit={(e) => { e.preventDefault(); onSubmit(itemId, dueDay); }} className="flex flex-wrap items-end gap-2 text-sm">
      <select value={itemId} onChange={(e) => setItemId(e.target.value)} required
        className="rounded-md border border-neutral-300 px-2 py-1">
        <option value="">Fiyat kalemi seç</option>
        {priceListItems.map((i) => (
          <option key={i.id} value={i.id}>
            {i.durationMinutes} dk · {i.billingType === "Monthly" ? "Aylık" : "Paket"} · {i.amount.toLocaleString("tr-TR")} {i.currency}
          </option>
        ))}
      </select>
      <input type="number" min={1} max={28} value={dueDay} onChange={(e) => setDueDay(Number(e.target.value))}
        className="w-20 rounded-md border border-neutral-300 px-2 py-1" title="Vade günü" />
      <button type="submit" className="rounded-md bg-neutral-900 px-3 py-1 text-white">Ücret planı oluştur</button>
    </form>
  );
}

function CreateReceivableForm({ onSubmit }: { onSubmit: (period: string) => Promise<void> }) {
  const [period, setPeriod] = useState(() => new Date().toISOString().slice(0, 7));

  return (
    <form onSubmit={(e) => { e.preventDefault(); onSubmit(period); }} className="flex items-end gap-2 text-sm">
      <input type="month" value={period} onChange={(e) => setPeriod(e.target.value)}
        className="rounded-md border border-neutral-300 px-2 py-1" />
      <button type="submit" className="rounded-md border border-neutral-300 px-3 py-1 hover:bg-neutral-100">
        Aidat oluştur
      </button>
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
  const statusColor: Record<Receivable["status"], string> = {
    Unpaid: "text-neutral-500", Partial: "text-amber-600", Paid: "text-green-700", Overdue: "text-red-600", Cancelled: "text-neutral-400",
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
    <tr className="border-t border-neutral-100 align-top">
      <td className="px-3 py-1.5">{receivable.period}</td>
      <td className="px-3 py-1.5 text-neutral-500">vade: {receivable.dueDate}</td>
      <td className="px-3 py-1.5">{receivable.amount.toLocaleString("tr-TR")} {receivable.currency}</td>
      <td className={`px-3 py-1.5 font-medium ${statusColor[receivable.status]}`}>{statusLabel[receivable.status]}</td>
      <td className="px-3 py-1.5">
        {receivable.status !== "Paid" && receivable.status !== "Cancelled" && (
          <>
            <button onClick={() => setShowForm((v) => !v)}
              className="inline-flex min-h-11 items-center text-blue-600 underline">
              Ödeme al
            </button>
            {showForm && (
              <form onSubmit={handleSubmit} className="mt-1 flex flex-wrap items-center gap-1">
                <input type="number" step={0.01} value={amount} onChange={(e) => setAmount(Number(e.target.value))}
                  className="w-24 rounded border border-neutral-300 px-1 py-0.5" />
                <select value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}
                  className="rounded border border-neutral-300 px-1 py-0.5">
                  <option value="Cash">Nakit</option>
                  <option value="Transfer">Havale</option>
                  <option value="Card">Kart</option>
                  <option value="Other">Diğer</option>
                </select>
                <button type="submit" className="min-h-11 rounded bg-neutral-900 px-2 text-white">Kaydet</button>
              </form>
            )}
            {error && <p className="text-red-600">{error}</p>}
          </>
        )}
      </td>
    </tr>
  );
}
