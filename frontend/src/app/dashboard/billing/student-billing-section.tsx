"use client";

import { useMemo, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useEnrollments, useInstruments, useStudents, useTeachers } from "@/lib/people";
import {
  COURSE_KIND_LABEL,
  useCreatePrepayPlan,
  useCreateReceivable,
  useCorrectPayment,
  usePrepayPreview,
  useRecordPayment,
  useStudentBilling,
  useUpdateEnrollmentBilling,
  type CourseKind,
  type PaymentMethod,
  type PaymentRecord,
  type Receivable,
} from "@/lib/billing";

function isValidPeriod(period: string | null | undefined): period is string {
  return !!period && /^\d{4}-(0[1-9]|1[0-2])$/.test(period);
}

function isValidDateInput(value: string | null | undefined): value is string {
  return !!value && /^\d{4}-(0[1-9]|1[0-2])-([0-2]\d|3[01])$/.test(value) && Number.isFinite(new Date(`${value}T00:00:00`).getTime());
}

// Bu panel bilinçli olarak TEK bir şey yapar: bir öğrencinin aidat geçmişini göstermek ve
// yeni bir dönem aidatı eklemek. Önceki sürümde aynı bilginin İKİ farklı temsili vardı -
// üstte salt-okunur bir "Dönem takibi" özeti, altta her kurs için ayrı, kendi "Ödeme al"
// formunu taşıyan ham bir liste - ve ana aidat listesindeki "Tahsilat" butonuyla buradaki
// "Ödeme al" butonu aynı işi iki farklı görünümde yapıyordu. Kullanıcı geri bildirimi:
// "Tahsilat ve Hesap kısımlarının kullanımları mantıklı değil."
//
// Çözüm: TEK birleşik dönem listesi (tüm kurslar birleştirilmiş, ana listedeki Tahsilat
// butonuyla aynı görünüm), TEK "+ Yeni aidat ekle" eylemi.
//
// Aidat modeli yeniden tasarlandıktan sonra buradan "ücret planı kurulumu" tamamen kalktı
// (docs/10-decisions.md H1): bir kurs kaydının aidat açabilmesi için ön koşul kalmadı.
// Yerine kursa özel İNDİRİM ayarı geldi - ders türü (birebir/grup) ve o kayda özel elle
// indirim, ki bunlar gerçekten öğrenci bazında verilen kararlar.
export function StudentBillingSection({ initialStudentId = "", showStudentPicker = true, onClose }: { initialStudentId?: string; showStudentPicker?: boolean; onClose?: () => void }) {
  const { data: students } = useStudents();
  const [studentId, setStudentId] = useState(initialStudentId);
  const { data: enrollments } = useEnrollments(studentId);
  const { data: teachers } = useTeachers();
  const { data: billing } = useStudentBilling(studentId);
  const { data: instruments } = useInstruments();
  const activeEnrollments = useMemo(() => enrollments?.filter((enrollment) => enrollment.status === "Active") ?? [], [enrollments]);

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
        <div className="flex w-full items-end gap-2 sm:w-auto">{showStudentPicker ? <label className="min-w-0 flex-1 space-y-1.5 sm:w-72"><span className="text-[.75rem] font-bold text-[var(--muted)]">Öğrenci</span><select value={studentId} onChange={(e) => setStudentId(e.target.value)} className="field min-h-11 text-sm">
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
            enrollments={activeEnrollments}
          />

          {activeEnrollments.length === 0 && <p className="rounded-xl bg-[var(--surface-muted)] p-4 text-sm text-[var(--muted)]">Bu öğrencinin aktif kaydı bulunmuyor.</p>}

          {activeEnrollments.length > 0 && (
            <details className="group rounded-xl border border-[var(--line)] bg-white">
              <summary className="pressable flex min-h-11 cursor-pointer list-none items-center justify-between px-4 text-xs font-bold text-[var(--muted)]">
                Kurs indirimleri <Icon name="chevron" className="h-4 w-4 shrink-0 transition-transform group-open:rotate-90" />
              </summary>
              <div className="space-y-3 border-t border-[var(--line)] p-4">
                {activeEnrollments.map((enrollment) => {
                  const row = billing?.find((item) => item.enrollmentId === enrollment.id);
                  return <EnrollmentDiscountBlock
                    key={enrollment.id}
                    studentId={studentId}
                    enrollmentId={enrollment.id}
                    label={enrollmentLabel(enrollment.id)}
                    courseKind={row?.courseKind ?? "Individual"}
                    manualDiscountPercent={row?.manualDiscountPercent ?? null}
                    manualDiscountReason={row?.manualDiscountReason ?? null}
                  />;
                })}
              </div>
            </details>
          )}

          {activeEnrollments.length > 0 && (
            <details className="group rounded-xl border border-[var(--line)] bg-white">
              <summary className="pressable flex min-h-11 cursor-pointer list-none items-center justify-between px-4 text-xs font-bold text-[var(--muted)]">
                Peşin ödeme al <Icon name="chevron" className="h-4 w-4 shrink-0 transition-transform group-open:rotate-90" />
              </summary>
              <div className="space-y-3 border-t border-[var(--line)] p-4">
                {activeEnrollments.map((enrollment) => (
                  <PrepayBlock key={enrollment.id} studentId={studentId} enrollmentId={enrollment.id} label={enrollmentLabel(enrollment.id)} />
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
  enrollments,
}: {
  studentId: string;
  billing: { enrollmentId: string; instrumentId: string; receivables: Receivable[] }[] | undefined;
  enrollmentLabel: (enrollmentId: string) => string;
  enrollments: { id: string }[];
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
        <span className="rounded-full bg-white px-2.5 py-1 text-[.75rem] font-bold text-[var(--brand-strong)]">{periods.length} dönem</span>
        {enrollments.length > 0 && <button type="button" onClick={() => setShowAddForm((value) => !value)} className="pressable inline-flex min-h-9 items-center gap-1 rounded-lg bg-[var(--brand)] px-3 text-[.75rem] font-bold text-white"><Icon name={showAddForm ? "close" : "plus"} className="h-3.5 w-3.5" />{showAddForm ? "Kapat" : "Yeni aidat ekle"}</button>}
      </div>
    </div>

    {showAddForm && <AddTuitionForm studentId={studentId} enrollments={enrollments} enrollmentLabel={enrollmentLabel} onCreated={() => setShowAddForm(false)} />}

    <div className="divide-y divide-[var(--line)]">
      {periods.map((receivable) => <PeriodRow key={receivable.id} studentId={studentId} receivable={receivable} instrumentLabel={receivable.label} />)}
      {!periods.length && <div className="grid min-h-40 place-items-center p-6 text-center"><div><span className="mx-auto grid h-10 w-10 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="wallet" className="h-4 w-4" /></span><p className="mt-3 text-xs font-bold">Tanımlı dönem bulunmuyor</p><p className="text-meta mt-1">{enrollments.length ? "Yukarıdaki \"Yeni aidat ekle\" ile ilk dönemi oluştur." : "Bu öğrencinin aktif kursu yok."}</p></div></div>}
    </div>
  </article>;
}

// "Yeni aidat gelince ekleyebileceğim basit bir ekran" - tek form: hangi kurs, hangi ay.
// Tutar sorulmaz; tarife ve indirimlerden sunucuda hesaplanır.
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
    if (!enrollmentId || !isValidPeriod(period)) {
      setError("Aidat oluşturmak için geçerli bir dönem seçin.");
      return;
    }
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
      <button type="submit" disabled={createReceivable.isPending || !enrollmentId || !isValidPeriod(period)} className="btn btn-primary">{createReceivable.isPending ? "Ekleniyor…" : "Aidatı oluştur"}</button>
      {error && <p role="alert" className="w-full text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
    </form>
  );
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
          <input type="number" min={0} max={100} step={0.5} value={percent} onChange={(event) => { setPercent(event.target.value === "" ? "" : Number(event.target.value)); setSaved(false); }} placeholder="Yok" className="field min-h-10 text-xs" />
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

// Peşin ödeme: tutarı SUNUCU hesaplar (tarife × ay − öğrenci indirimi − kademe indirimi),
// ekran yalnızca gösterip onaylar. Eski "toplam tutar" alanı salt-okunur bir çarpımdı ve
// kampanya indirimini ifade edemiyordu.
function PrepayBlock({ studentId, enrollmentId, label }: { studentId: string; enrollmentId: string; label: string }) {
  const [startPeriod, setStartPeriod] = useState(() => new Date().toISOString().slice(0, 7));
  const [months, setMonths] = useState(10);
  const [paymentDate, setPaymentDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [method, setMethod] = useState<PaymentMethod>("Transfer");
  const [error, setError] = useState<string | null>(null);

  const createPrepayPlan = useCreatePrepayPlan(studentId, enrollmentId);
  const { data: preview, isLoading } = usePrepayPreview(enrollmentId, startPeriod, months, {
    enabled: isValidPeriod(startPeriod),
  });

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (!isValidPeriod(startPeriod) || !isValidDateInput(paymentDate)) {
      setError("Başlangıç dönemi ve ödeme tarihini seçin.");
      return;
    }
    try {
      await createPrepayPlan.mutateAsync({ startPeriod, months, paymentDate, method, expectedTotal: preview?.total });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Peşin ödeme kaydedilemedi.");
    }
  }

  const blocked = (preview?.blockers.length ?? 0) > 0;

  return (
    <form onSubmit={handleSubmit} className="rounded-xl border border-[var(--line)] p-4">
      <p className="mb-2 text-xs font-bold">{label}</p>
      <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
        <label className="form-label">Başlangıç<input type="month" value={startPeriod} onChange={(event) => { setStartPeriod(event.target.value); setError(null); }} required className="field min-h-10 text-xs" /></label>
        <label className="form-label">Kaç ay?<input type="number" min={1} max={24} value={months} onChange={(event) => { setMonths(Math.max(1, Math.min(24, Number(event.target.value) || 1))); setError(null); }} className="field min-h-10 text-xs" /></label>
        <label className="form-label">Ödeme tarihi<input type="date" value={paymentDate} onChange={(event) => { setPaymentDate(event.target.value); setError(null); }} required className="field min-h-10 text-xs" /></label>
        <label className="form-label">Yöntem<select value={method} onChange={(event) => setMethod(event.target.value as PaymentMethod)} className="field min-h-10 text-xs"><option value="Transfer">Havale</option><option value="Cash">Nakit</option><option value="Card">Kart</option><option value="Other">Diğer</option></select></label>
      </div>

      {isLoading && <div className="skeleton mt-3 h-16 rounded-xl" />}

      {preview && !isLoading && <dl className="mt-3 space-y-1 rounded-xl bg-[var(--surface-muted)]/60 p-3 text-[.75rem]">
        <div className="flex justify-between gap-2"><dt className="text-[var(--muted)]">Tarife toplamı ({preview.months} ay)</dt><dd className="tabular-nums">{preview.baseTotal.toLocaleString("tr-TR")} {preview.currency}</dd></div>
        {preview.studentDiscountPercent > 0 && <div className="flex justify-between gap-2"><dt className="text-[var(--muted)]">{preview.studentDiscountReason}</dt><dd className="tabular-nums text-[var(--success-strong)]">−%{preview.studentDiscountPercent}</dd></div>}
        {preview.prepayPercent > 0 && <div className="flex justify-between gap-2"><dt className="text-[var(--muted)]">Peşin ödeme indirimi</dt><dd className="tabular-nums text-[var(--success-strong)]">−%{preview.prepayPercent}</dd></div>}
        <div className="flex justify-between gap-2 border-t border-[var(--line)] pt-1.5"><dt className="font-bold">Ödenecek</dt><dd className="font-bold tabular-nums text-[var(--brand-strong)]">{preview.total.toLocaleString("tr-TR")} {preview.currency}</dd></div>
      </dl>}

      {blocked && <p role="alert" className="mt-2 rounded-lg bg-[var(--warning-soft)] px-3 py-2 text-xs font-semibold text-[var(--warning-strong)]">{preview!.blockers.join(" ")} Başlangıç dönemini değiştirin.</p>}

      <button type="submit" disabled={createPrepayPlan.isPending || blocked || !preview?.total} className="btn btn-primary mt-3">{createPrepayPlan.isPending ? "Kaydediliyor…" : "Peşin ödemeyi kaydet"}</button>
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

  return <div className="p-4">
    <div className="flex flex-wrap items-center gap-3">
      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--brand-strong)]"><Icon name="calendar" className="h-4 w-4" /></span>
      <span className="min-w-0 flex-1">
        <span className="block text-xs font-bold capitalize">{new Date(`${receivable.period}-01T00:00:00`).toLocaleDateString("tr-TR", { month: "long", year: "numeric" })}</span>
        <span className="mt-0.5 block truncate text-[.75rem] text-[var(--muted)]">{instrumentLabel} · Vade {receivable.dueDate}</span>
      </span>
      <span className="text-right"><strong className="block text-xs tabular-nums">{receivable.amount.toLocaleString("tr-TR")} {receivable.currency}</strong><span className="mt-0.5 block text-[.75rem] tabular-nums text-[var(--muted)]">{remaining ? `${remaining.toLocaleString("tr-TR")} kaldı` : "Tamamı ödendi"}</span></span>
      <span className={`rounded-full px-2 py-1 text-[.75rem] font-bold ${statusTone[receivable.status]}`}>{statusLabel[receivable.status]}</span>
      <span className="flex items-center gap-1.5">
        {canCollect && <button type="button" onClick={() => setShowForm((v) => !v)} className="btn btn-primary">Tahsilat</button>}
        {receivable.payments.length > 0 && <button type="button" onClick={() => setShowHistory((v) => !v)} className="pressable min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-[.75rem] font-bold text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]">Geçmiş · {receivable.payments.length}</button>}
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
    <div className="flex flex-wrap items-center justify-between gap-2"><span className="flex flex-wrap items-center gap-1.5">{payment.paymentDate} · {payment.method === "Transfer" ? "Havale" : payment.method === "Cash" ? "Nakit" : payment.method === "Card" ? "Kart" : "Diğer"}{payment.prepayPlanId && <span className="rounded-full bg-[var(--brand-soft)] px-1.5 py-0.5 text-[.75rem] font-bold text-[var(--brand-strong)]">Peşin ödeme · {payment.prepayPlanMonths} ay</span>}</span><span className="flex items-center gap-2"><strong>{payment.amount.toLocaleString("tr-TR")} {currency}</strong><button type="button" onClick={() => setEditing((value) => !value)} className="font-bold text-[var(--brand)]">Düzelt</button></span></div>
    {editing && <form onSubmit={submit} className="mt-2 grid gap-2 rounded-lg bg-[var(--surface-muted)] p-2 sm:grid-cols-[7rem_1fr_auto]"><input type="number" min={0} step={0.01} value={correctedAmount} onChange={(event) => setCorrectedAmount(Number(event.target.value))} aria-label="Düzeltilen ödeme tutarı" className="field min-h-9 text-xs" /><input value={reason} onChange={(event) => setReason(event.target.value)} required placeholder="Düzeltme nedeni" className="field min-h-9 text-xs" /><button disabled={correctPayment.isPending} className="btn btn-primary">{correctPayment.isPending ? "Kaydediliyor…" : "Düzeltmeyi kaydet"}</button>{error && <p role="alert" className="text-[var(--danger-strong)] sm:col-span-3">{error}</p>}</form>}
  </div>;
}
