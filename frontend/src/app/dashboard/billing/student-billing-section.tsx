"use client";

import { useMemo, useState, type FormEvent } from "react";
import { useQueries } from "@tanstack/react-query";
import { Icon } from "@/components/icons";
import { api, ApiError } from "@/lib/api";
import { useEnrollments, useInstruments, useStudents, useTeachers } from "@/lib/people";
import {
  useCreateFeePlan,
  useCreateReceivable,
  useBulkPayment,
  useCorrectPayment,
  usePriceLists,
  useRecordPayment,
  useStudentBilling,
  type FeePlan,
  type PaymentMethod,
  type PaymentRecord,
  type Receivable,
} from "@/lib/billing";

// Bu panel bilinçli olarak TEK bir şey yapar: bir öğrencinin aidat geçmişini göstermek ve
// yeni bir dönem aidatı eklemek. Önceki sürümde aynı bilginin İKİ farklı temsili vardı -
// üstte salt-okunur bir "Dönem takibi" özeti, altta her kurs için ayrı, kendi "Ödeme al"
// formunu taşıyan ham bir liste - ve ana aidat listesindeki "Tahsilat" butonuyla buradaki
// "Ödeme al" butonu aynı işi iki farklı görünümde yapıyordu. Kullanıcı geri bildirimi:
// "Tahsilat ve Hesap kısımlarının kullanımları mantıklı değil."
//
// Çözüm: TEK birleşik dönem listesi (tüm kurslar birleştirilmiş, ana listedeki Tahsilat
// butonuyla aynı görünüm), TEK "+ Yeni aidat ekle" eylemi. Ücret planı kurulumu ve toplu
// ödeme hâlâ gerekli ama birincil akışın önüne geçmesin diye ikincil/katlanır durumda.
export function StudentBillingSection({ initialStudentId = "", showStudentPicker = true, onClose }: { initialStudentId?: string; showStudentPicker?: boolean; onClose?: () => void }) {
  const { data: students } = useStudents();
  const [studentId, setStudentId] = useState(initialStudentId);
  const { data: enrollments } = useEnrollments(studentId);
  const { data: teachers } = useTeachers();
  const { data: billing } = useStudentBilling(studentId);
  const { data: instruments } = useInstruments();
  const { data: priceLists } = usePriceLists();
  const activeEnrollments = useMemo(() => enrollments?.filter((enrollment) => enrollment.status === "Active") ?? [], [enrollments]);

  // Her aktif kursun ücret planı olup olmadığı BURADA, tek seferde bilinir - hem "+ Yeni
  // aidat ekle" formunun yalnızca planı olan kursları önermesi hem de plansız kalan
  // kursların altta ayrıca uyarılması bu bilgiye ihtiyaç duyar.
  const feePlanQueries = useQueries({
    queries: activeEnrollments.map((enrollment) => ({
      queryKey: ["fee-plan", enrollment.id],
      queryFn: async () => {
        try {
          return await api.get<FeePlan>(`/api/enrollments/${enrollment.id}/fee-plan`);
        } catch {
          return null;
        }
      },
    })),
  });
  const feePlanLoading = feePlanQueries.some((query) => query.isLoading);
  const feePlanByEnrollment = new Map(activeEnrollments.map((enrollment, index) => [enrollment.id, feePlanQueries[index]?.data ?? null]));
  const enrollmentsWithPlan = activeEnrollments.filter((enrollment) => feePlanByEnrollment.get(enrollment.id));
  // Yüklenirken hiçbir kursu "plansız" diye göstermeyelim - aksi halde her açılışta bir an
  // için gerçek olmayan bir uyarı yanıp söner.
  const enrollmentsWithoutPlan = feePlanLoading ? [] : activeEnrollments.filter((enrollment) => !feePlanByEnrollment.get(enrollment.id));

  function enrollmentLabel(enrollmentId: string) {
    const enrollment = activeEnrollments.find((item) => item.id === enrollmentId);
    if (!enrollment) return "Kurs";
    const instrumentName = instruments?.find((i) => i.id === enrollment.instrumentId)?.name ?? "?";
    const teacher = teachers?.find((t) => t.id === enrollment.teacherId);
    return `${instrumentName} · ${teacher ? `${teacher.firstName} ${teacher.lastName}` : "Öğretmen atanmadı"}`;
  }

  return (
    <section className="app-card overflow-hidden">
      <div className="flex flex-wrap items-end justify-between gap-4 border-b border-[var(--line)] bg-[var(--surface-muted)]/45 p-4 sm:p-5">
        <div><p className="text-micro text-[var(--brand-strong)]">Öğrenci hesabı</p><h2 className="mt-1 text-title">Aidat geçmişi</h2><p className="text-meta mt-1">Geçmiş dönemleri gör, yeni aidat ekle, ödeme al.</p></div>
        <div className="flex w-full items-end gap-2 sm:w-auto">{showStudentPicker ? <label className="min-w-0 flex-1 space-y-1.5 sm:w-72"><span className="text-[.68rem] font-bold text-[var(--muted)]">Öğrenci</span><select value={studentId} onChange={(e) => setStudentId(e.target.value)} className="field min-h-11 text-sm">
          <option value="">Öğrenci seçin…</option>
          {students?.map((s) => (<option key={s.id} value={s.id}>{s.firstName} {s.lastName}</option>))}
        </select></label> : <p className="text-meta">Seçilen öğrencinin aidat geçmişi</p>}{onClose && <button type="button" onClick={onClose} className="pressable grid h-11 w-11 shrink-0 place-items-center rounded-xl border border-[var(--line)] bg-white text-[var(--muted)]" aria-label="Hesap ayrıntısını kapat"><Icon name="close" className="h-4 w-4" /></button>}</div>
      </div>

      {!studentId && <div className="grid min-h-56 place-items-center p-8 text-center"><div><span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-[var(--brand-soft)] text-xl" aria-hidden="true">₺</span><p className="mt-4 text-sm font-bold">Öğrenci hesabı seçilmedi</p><p className="text-meta mt-1">Borç, tahsilat ve ödeme geçmişi burada gösterilecek.</p></div></div>}

      {studentId && (
        <div className="space-y-4 p-4 sm:p-5">
          <UnifiedPeriodsList
            studentId={studentId}
            billing={billing}
            enrollmentLabel={enrollmentLabel}
            enrollmentsWithPlan={enrollmentsWithPlan}
          />

          {activeEnrollments.length === 0 && <p className="rounded-xl bg-[var(--surface-muted)] p-4 text-sm text-[var(--muted)]">Bu öğrencinin aktif kaydı bulunmuyor.</p>}

          {enrollmentsWithoutPlan.length > 0 && (
            <div className="space-y-3">
              <p className="text-micro text-[var(--warning-strong)]">Ücret planı eksik</p>
              {enrollmentsWithoutPlan.map((enrollment) => (
                <MissingFeePlanCard
                  key={enrollment.id}
                  enrollmentId={enrollment.id}
                  label={enrollmentLabel(enrollment.id)}
                  priceListItems={(priceLists ?? []).flatMap((l) => l.items).filter((i) => i.instrumentId === enrollment.instrumentId)}
                />
              ))}
            </div>
          )}

          {enrollmentsWithPlan.length > 0 && (
            <details className="group rounded-xl border border-[var(--line)] bg-white">
              <summary className="pressable flex min-h-11 cursor-pointer list-none items-center justify-between px-4 text-xs font-bold text-[var(--muted)]">
                Toplu ödeme al <Icon name="chevron" className="h-4 w-4 shrink-0 transition-transform group-open:rotate-90" />
              </summary>
              <div className="space-y-3 border-t border-[var(--line)] p-4">
                {enrollmentsWithPlan.map((enrollment) => (
                  <BulkPaymentBlock key={enrollment.id} studentId={studentId} enrollmentId={enrollment.id} label={enrollmentLabel(enrollment.id)} feePlan={feePlanByEnrollment.get(enrollment.id)!} />
                ))}
              </div>
            </details>
          )}
        </div>
      )}
    </section>
  );
}

function UnifiedPeriodsList({
  studentId,
  billing,
  enrollmentLabel,
  enrollmentsWithPlan,
}: {
  studentId: string;
  billing: { enrollmentId: string; instrumentId: string; receivables: Receivable[] }[] | undefined;
  enrollmentLabel: (enrollmentId: string) => string;
  enrollmentsWithPlan: { id: string }[];
}) {
  const [showAddForm, setShowAddForm] = useState(false);
  const periods = (billing ?? []).flatMap((row) => row.receivables.map((receivable) => ({
    ...receivable,
    label: enrollmentLabel(row.enrollmentId),
  }))).sort((a, b) => b.period.localeCompare(a.period) || a.label.localeCompare(b.label, "tr-TR"));

  return <article className="overflow-hidden rounded-2xl border border-[var(--line)] bg-white">
    <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--line)] bg-[var(--brand-soft)]/35 p-4">
      <div><p className="text-micro text-[var(--brand-strong)]">Dönem takibi</p><h3 className="mt-1 text-title">Dönem aidatları</h3></div>
      <div className="flex items-center gap-2">
        <span className="rounded-full bg-white px-2.5 py-1 text-[.65rem] font-bold text-[var(--brand-strong)]">{periods.length} dönem</span>
        {enrollmentsWithPlan.length > 0 && <button type="button" onClick={() => setShowAddForm((value) => !value)} className="pressable inline-flex min-h-9 items-center gap-1 rounded-lg bg-[var(--brand)] px-3 text-[.68rem] font-bold text-white"><Icon name={showAddForm ? "close" : "plus"} className="h-3.5 w-3.5" />{showAddForm ? "Kapat" : "Yeni aidat ekle"}</button>}
      </div>
    </div>

    {showAddForm && <AddTuitionForm studentId={studentId} enrollments={enrollmentsWithPlan} enrollmentLabel={enrollmentLabel} onCreated={() => setShowAddForm(false)} />}

    <div className="divide-y divide-[var(--line)]">
      {periods.map((receivable) => <PeriodRow key={receivable.id} studentId={studentId} receivable={receivable} instrumentLabel={receivable.label} />)}
      {!periods.length && <div className="grid min-h-40 place-items-center p-6 text-center"><div><span className="mx-auto grid h-10 w-10 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="wallet" className="h-4 w-4" /></span><p className="mt-3 text-xs font-bold">Tanımlı dönem bulunmuyor</p><p className="text-meta mt-1">{enrollmentsWithPlan.length ? "Yukarıdaki \"Yeni aidat ekle\" ile ilk dönemi oluştur." : "Önce aşağıdan bir ücret planı oluştur."}</p></div></div>}
    </div>
  </article>;
}

// "Yeni aidat gelince ekleyebileceğim basit bir ekran" - tek form: hangi kurs, hangi ay.
// Yalnızca zaten bir ücret planı olan kurslar listelenir (backend bir plan olmadan aidat
// oluşturmayı zaten reddediyor - Receivables.cs CreateAsync).
function AddTuitionForm({
  studentId,
  enrollments,
  enrollmentLabel,
  onCreated,
}: {
  studentId: string;
  enrollments: { id: string }[];
  enrollmentLabel: (enrollmentId: string) => string;
  onCreated: () => void;
}) {
  const createReceivable = useCreateReceivable(studentId);
  const [enrollmentId, setEnrollmentId] = useState(enrollments[0]?.id ?? "");
  const [period, setPeriod] = useState(() => new Date().toISOString().slice(0, 7));
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createReceivable.mutateAsync({ enrollmentId, period });
      onCreated();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Aidat oluşturulamadı.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-wrap items-end gap-2 border-b border-[var(--line)] bg-[var(--surface-muted)]/60 p-4">
      {enrollments.length > 1 && <label className="form-label">Kurs<select value={enrollmentId} onChange={(event) => setEnrollmentId(event.target.value)} required className="field min-h-10 text-sm">{enrollments.map((enrollment) => <option key={enrollment.id} value={enrollment.id}>{enrollmentLabel(enrollment.id)}</option>)}</select></label>}
      <label className="form-label">Dönem<input type="month" value={period} onChange={(event) => setPeriod(event.target.value)} required className="field min-h-10 text-sm" /></label>
      <button type="submit" disabled={createReceivable.isPending || !enrollmentId} className="btn btn-primary">{createReceivable.isPending ? "Ekleniyor…" : "Aidatı oluştur"}</button>
      {error && <p role="alert" className="w-full text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
    </form>
  );
}

function MissingFeePlanCard({ enrollmentId, label, priceListItems }: { enrollmentId: string; label: string; priceListItems: { id: string; durationMinutes: number; billingType: string; amount: number; currency: string }[] }) {
  const createFeePlan = useCreateFeePlan(enrollmentId);
  const [itemId, setItemId] = useState("");
  const [dueDay, setDueDay] = useState(5);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createFeePlan.mutateAsync({ priceListItemId: itemId, dueDay, activeFrom: new Date().toISOString().slice(0, 10) });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ücret planı oluşturulamadı.");
    }
  }

  return (
    <div className="rounded-xl border border-[var(--warning-soft)] bg-[var(--warning-soft)]/40 p-3.5">
      <p className="text-xs font-bold">{label}</p>
      <p className="text-meta mt-0.5">Bu kurs için aidat oluşturulabilmesi için önce bir ücret planı gerekiyor.</p>
      {priceListItems.length === 0
        ? <p className="text-meta mt-2 font-semibold text-[var(--danger-strong)]">Bu enstrüman için fiyat listesi kalemi yok - önce Fiyat politikası ekranından ekle.</p>
        : <form onSubmit={handleSubmit} className="mt-2 flex flex-wrap items-end gap-2">
            <select value={itemId} onChange={(e) => setItemId(e.target.value)} required className="field min-h-10 w-auto text-sm">
              <option value="">Fiyat kalemi seç</option>
              {priceListItems.map((i) => <option key={i.id} value={i.id}>{i.durationMinutes} dk · {i.billingType === "Monthly" ? "Aylık" : "Paket"} · {i.amount.toLocaleString("tr-TR")} {i.currency}</option>)}
            </select>
            <label className="form-label">Vade günü<input type="number" min={1} max={28} value={dueDay} onChange={(e) => setDueDay(Number(e.target.value))} className="field min-h-10 w-20 text-sm" /></label>
            <button type="submit" disabled={createFeePlan.isPending || !itemId} className="btn btn-primary">{createFeePlan.isPending ? "Oluşturuluyor…" : "Ücret planı oluştur"}</button>
          </form>}
      {error && <p className="mt-2 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
    </div>
  );
}

function BulkPaymentBlock({ studentId, enrollmentId, label, feePlan }: { studentId: string; enrollmentId: string; label: string; feePlan: FeePlan }) {
  const bulkPayment = useBulkPayment(studentId, enrollmentId);
  const [error, setError] = useState<string | null>(null);
  const [startPeriod, setStartPeriod] = useState(() => new Date().toISOString().slice(0, 7));
  const [months, setMonths] = useState(1);
  const [amount, setAmount] = useState(feePlan.amount);
  const [paymentDate, setPaymentDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [method, setMethod] = useState<PaymentMethod>("Transfer");

  if (feePlan.billingType !== "Monthly") return null;

  function changeMonths(value: number) {
    setMonths(value);
    setAmount(feePlan.amount * value);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await bulkPayment.mutateAsync({ startPeriod, months, amount, paymentDate, method });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Toplu ödeme kaydedilemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="rounded-xl border border-[var(--line)] p-3.5">
      <p className="mb-2 text-xs font-bold">{label}</p>
      <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-5">
        <label className="form-label">Başlangıç<input type="month" value={startPeriod} onChange={(event) => setStartPeriod(event.target.value)} className="field min-h-10 text-xs" /></label>
        <label className="form-label">Kaç ay?<select value={months} onChange={(event) => changeMonths(Number(event.target.value))} className="field min-h-10 text-xs"><option value={1}>1 ay</option><option value={3}>3 ay</option><option value={6}>6 ay</option><option value={10}>10 ay</option><option value={12}>12 ay</option></select></label>
        <label className="form-label">Toplam tutar<input type="number" min={0.01} step={0.01} value={amount} onChange={(event) => setAmount(Number(event.target.value))} className="field min-h-10 text-xs" /></label>
        <label className="form-label">Ödeme tarihi<input type="date" value={paymentDate} onChange={(event) => setPaymentDate(event.target.value)} className="field min-h-10 text-xs" /></label>
        <label className="form-label">Yöntem<select value={method} onChange={(event) => setMethod(event.target.value as PaymentMethod)} className="field min-h-10 text-xs"><option value="Transfer">Havale</option><option value="Cash">Nakit</option><option value="Card">Kart</option><option value="Other">Diğer</option></select></label>
      </div>
      <button type="submit" disabled={bulkPayment.isPending} className="btn btn-primary mt-3">{bulkPayment.isPending ? "Kaydediliyor…" : "Toplu ödemeyi kaydet"}</button>
      {error && <p className="mt-2 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
    </form>
  );
}

// Ana aidat listesindeki DueRow ile GÖRSEL OLARAK AYNI desen (Tahsilat butonu + inline
// form): "Hesap" panelinin kendi farklı bir "Ödeme al" arayüzü olması, kullanıcının aynı
// eylemi iki farklı görünümde öğrenmesi gerektiği anlamına geliyordu.
function PeriodRow({ studentId, receivable, instrumentLabel }: { studentId: string; receivable: Receivable & { label: string }; instrumentLabel: string }) {
  const recordPayment = useRecordPayment(studentId);
  const [showForm, setShowForm] = useState(false);
  const [showHistory, setShowHistory] = useState(false);
  const [amount, setAmount] = useState(Math.max(0, receivable.amount - receivable.totalPaid));
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [error, setError] = useState<string | null>(null);
  const remaining = Math.max(0, receivable.amount - receivable.totalPaid);
  const canCollect = receivable.status !== "Paid" && receivable.status !== "Cancelled";

  const statusLabel: Record<Receivable["status"], string> = {
    Unpaid: "Ödenmedi", Partial: "Kısmi ödendi", Paid: "Ödendi", Overdue: "Vadesi geçti", Cancelled: "İptal",
  };
  const statusTone: Record<Receivable["status"], string> = {
    Unpaid: "bg-[var(--surface-muted)] text-[var(--muted)]",
    Partial: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
    Paid: "bg-[var(--success-soft)] text-[var(--success-strong)]",
    Overdue: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
    Cancelled: "bg-[var(--surface-muted)] text-[var(--muted)]",
  };

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await recordPayment.mutateAsync({ receivableId: receivable.id, amount, paymentDate: new Date().toISOString().slice(0, 10), method });
      setShowForm(false);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme kaydedilemedi.");
    }
  }

  return <div className="p-3.5 sm:px-4">
    <div className="flex flex-wrap items-center gap-3">
      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--brand-strong)]"><Icon name="calendar" className="h-4 w-4" /></span>
      <span className="min-w-0 flex-1">
        <span className="block text-xs font-bold capitalize">{new Date(`${receivable.period}-01T00:00:00`).toLocaleDateString("tr-TR", { month: "long", year: "numeric" })}</span>
        <span className="mt-0.5 block truncate text-[.62rem] text-[var(--muted)]">{instrumentLabel} · Vade {receivable.dueDate}</span>
      </span>
      <span className="text-right"><strong className="block text-xs tabular-nums">{receivable.amount.toLocaleString("tr-TR")} {receivable.currency}</strong><span className="mt-0.5 block text-[.62rem] tabular-nums text-[var(--muted)]">{remaining ? `${remaining.toLocaleString("tr-TR")} kaldı` : "Tamamı ödendi"}</span></span>
      <span className={`rounded-full px-2 py-1 text-[.58rem] font-bold ${statusTone[receivable.status]}`}>{statusLabel[receivable.status]}</span>
      <span className="flex items-center gap-1.5">
        {canCollect && <button type="button" onClick={() => setShowForm((v) => !v)} className="btn btn-primary">Tahsilat</button>}
        {receivable.payments.length > 0 && <button type="button" onClick={() => setShowHistory((v) => !v)} className="pressable min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-[.66rem] font-bold text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]">Geçmiş · {receivable.payments.length}</button>}
      </span>
    </div>

    {showForm && <form onSubmit={handleSubmit} className="mt-3 flex flex-wrap items-center gap-1.5 rounded-xl border border-[var(--brand)]/25 bg-[var(--brand-soft)]/45 p-3">
      <input type="number" step={0.01} min={0.01} max={remaining} value={amount} onChange={(e) => setAmount(Number(e.target.value))} className="field min-h-9 w-24 text-xs" />
      <select value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)} className="field min-h-9 w-auto text-xs">
        <option value="Cash">Nakit</option>
        <option value="Transfer">Havale</option>
        <option value="Card">Kart</option>
        <option value="Other">Diğer</option>
      </select>
      <button type="submit" disabled={recordPayment.isPending} className="btn btn-primary">{recordPayment.isPending ? "Kaydediliyor…" : "Kaydet"}</button>
      {error && <p className="w-full text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
    </form>}

    {showHistory && receivable.payments.length > 0 && (
      <div className="mt-3 space-y-1.5 rounded-lg bg-[var(--surface-muted)] p-2.5">
        {receivable.payments.map((payment) => <PaymentHistoryRow key={payment.id} studentId={studentId} payment={payment} currency={receivable.currency} />)}
      </div>
    )}
  </div>;
}

function PaymentHistoryRow({ studentId, payment, currency }: { studentId: string; payment: PaymentRecord; currency: string }) {
  const correctPayment = useCorrectPayment(studentId);
  const [editing, setEditing] = useState(false);
  const [correctedAmount, setCorrectedAmount] = useState(payment.amount);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
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
    return <div className="rounded-lg border border-[var(--warning-soft)] bg-white px-2.5 py-2 text-xs"><div className="flex flex-wrap justify-between gap-2"><span><strong>Düzeltme</strong> · {payment.paymentDate}{payment.note ? ` · ${payment.note}` : ""}</span><strong>{payment.previousAmount?.toLocaleString("tr-TR")} → {payment.amount.toLocaleString("tr-TR")} {currency}</strong></div></div>;
  }

  return <div className="rounded-lg bg-white px-2.5 py-2 text-xs">
    <div className="flex flex-wrap items-center justify-between gap-2"><span>{payment.paymentDate} · {payment.method === "Transfer" ? "Havale" : payment.method === "Cash" ? "Nakit" : payment.method === "Card" ? "Kart" : "Diğer"}</span><span className="flex items-center gap-2"><strong>{payment.amount.toLocaleString("tr-TR")} {currency}</strong><button type="button" onClick={() => setEditing((value) => !value)} className="font-bold text-[var(--brand)]">Düzelt</button></span></div>
    {editing && <form onSubmit={submit} className="mt-2 grid gap-2 rounded-lg bg-[var(--surface-muted)] p-2 sm:grid-cols-[7rem_1fr_auto]"><input type="number" min={0} step={0.01} value={correctedAmount} onChange={(event) => setCorrectedAmount(Number(event.target.value))} aria-label="Düzeltilen ödeme tutarı" className="field min-h-9 text-xs" /><input value={reason} onChange={(event) => setReason(event.target.value)} required placeholder="Düzeltme nedeni" className="field min-h-9 text-xs" /><button disabled={correctPayment.isPending} className="btn btn-primary">{correctPayment.isPending ? "Kaydediliyor…" : "Düzeltmeyi kaydet"}</button>{error && <p role="alert" className="text-[var(--danger-strong)] sm:col-span-3">{error}</p>}</form>}
  </div>;
}
