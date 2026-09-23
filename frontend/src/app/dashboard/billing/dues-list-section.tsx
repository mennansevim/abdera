"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { Modal, onInvalidTurkish, resetValidity } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  useBillingDues,
  useCreatePrepayPlan,
  usePrepayPreview,
  useRecordPayment,
  useStudentBilling,
  type BillingDue,
  type PaymentMethod,
  type Receivable,
} from "@/lib/billing";
import { currentPeriod, formatDay, formatMoney, formatPeriod, isValidPeriod } from "@/lib/billing-format";
import { useEnrollments, useInstruments, useStudentOverviews, useStudents, useTeachers, type StudentInstrumentSummary, type Student } from "@/lib/people";
import { useSessionState } from "@/lib/use-session-state";
import { StudentBillingSection } from "./student-billing-section";

// Ekran bir "öğrenci defteri": önce TÜM öğrenciler listelenir, her satırın altında o
// öğrencinin aidat takvimi (geçmiş dahil) açılır.
//
// Önceki sürüm "dönem defteri" idi (önce ay seçilir, o ayın borç satırları listelenir).
// Kullanıcı geri bildirimi bunu tersine çevirdi: "tüm öğrencileri listele, bir öğrenciye
// tıkladığımda altında ayların olduğu takvim açılsın, aya tıklayarak da tahsil edilsin."
// Dönem-önce yaklaşımın sorunu: o ay hiç borcu olmayan veya seçili durum sekmesine
// girmeyen bir öğrenci listede HİÇ görünmüyordu - "tüm öğrenciler" sorusuna cevap
// vermiyordu. Öğrenci-önce yaklaşımda herkes her zaman listede; geçmiş dahil tüm
// dönemlere tek bir "Detay" ile bakılır (aynı takvim, yalnızca ay artık salt-okunur değil,
// tahsilat da alınabiliyor - bkz. ReceivablePeriodCard).
//
// "Kim borçlu" triyajını kaybetmemek için üst özet kartları (page.tsx) hâlâ tüm okulun
// açık/gecikmiş/tahsil edilen toplamını gösteriyor - bu ekran yalnızca listeleme biçimini
// değiştirdi, toplamların kaynağını değil.

export interface BillingFilterSummary {
  outstanding: number;
  collected: number;
  overdue: number;
  openCount: number;
  overdueCount: number;
}

const MONTHS_TR = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];

const STATUS_TONES: Record<BillingDue["status"], string> = {
  Unpaid: "bg-[var(--surface-muted)] text-[var(--muted)]",
  Partial: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  Paid: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  Overdue: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  Cancelled: "bg-[var(--surface-muted)] text-[var(--muted)]",
};

function isOpen(status: BillingDue["status"]) {
  return status === "Unpaid" || status === "Partial" || status === "Overdue";
}

function normalizeMonthCount(value: string, max: number) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? Math.max(1, Math.min(max, Math.trunc(parsed))) : 1;
}

function paymentMethodLabel(method: PaymentMethod) {
  return method === "Transfer" ? "Havale" : method === "Cash" ? "Nakit" : method === "Card" ? "Kart" : "Diğer";
}

// "Vadesi geçti" yerine "14 gün gecikti": yöneticiye doğrudan aciliyet sırasını verir.
function daysOverdue(dueDate: string) {
  const due = new Date(`${dueDate}T00:00:00`);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return Math.floor((today.getTime() - due.getTime()) / 86_400_000);
}

export function DuesListSection({ onSummaryChange }: { onSummaryChange?: (summary: BillingFilterSummary) => void }) {
  const { data: overviews, isLoading, isError, isFetching, refetch } = useStudentOverviews();
  const { data: dues } = useBillingDues();
  const { data: teachers } = useTeachers();
  const [teacherFilter, setTeacherFilter] = useSessionState("abdera:billing:teacher", "all");
  const [studentSearch, setStudentSearch] = useSessionState("abdera:billing:student-search", "");
  // selectedStudentId yalnızca "tam hesabı aç" kaçış kapısı için tutuluyor - hızlı
  // tahsilat panelinin (QuickCollectPanel) kendi öğrenci seçimi ayrıdır.
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null);
  const [showCreatePanel, setShowCreatePanel] = useState(false);

  const studentPickerRef = useRef<HTMLSelectElement>(null);
  const startAddingDue = useCallback(() => {
    setShowCreatePanel(true);
    // Pencere açıldıktan sonra odaklan - aksi halde eleman henüz DOM'da olmuyor.
    window.setTimeout(() => studentPickerRef.current?.focus(), 0);
  }, []);

  const clearFilters = useCallback(() => {
    setTeacherFilter("all");
    setStudentSearch("");
  }, [setStudentSearch, setTeacherFilter]);

  // Öğretmen filtresi öğrenci kaydında tutulmuyor (bir kurs kaydına bağlı) - aidat
  // verisinden (dues) türetiliyor, ayrı bir uç nokta açmaya gerek yok.
  const studentTeacherIds = useMemo(() => {
    const map = new Map<string, Set<string>>();
    for (const due of dues ?? []) {
      const set = map.get(due.studentId) ?? new Set<string>();
      set.add(due.teacherId);
      map.set(due.studentId, set);
    }
    return map;
  }, [dues]);

  const thisPeriod = useMemo(() => currentPeriod(), []);
  const duesByStudentThisPeriod = useMemo(() => {
    const map = new Map<string, BillingDue[]>();
    for (const due of dues ?? []) {
      if (due.period !== thisPeriod || due.status === "Cancelled") continue;
      const rows = map.get(due.studentId) ?? [];
      rows.push(due);
      map.set(due.studentId, rows);
    }
    return map;
  }, [dues, thisPeriod]);

  const visibleStudents = useMemo(() => {
    const query = studentSearch.trim().toLocaleLowerCase("tr-TR");
    return (overviews ?? [])
      .filter(({ student, instruments }) => {
        if (teacherFilter !== "all" && !studentTeacherIds.get(student.id)?.has(teacherFilter)) return false;
        if (!query) return true;
        const haystack = [`${student.firstName} ${student.lastName}`, ...instruments.map((item) => item.instrumentName)]
          .join(" ").toLocaleLowerCase("tr-TR");
        return haystack.includes(query);
      })
      .sort((a, b) => `${a.student.firstName} ${a.student.lastName}`.localeCompare(`${b.student.firstName} ${b.student.lastName}`, "tr-TR"));
  }, [overviews, studentSearch, teacherFilter, studentTeacherIds]);

  // Üstteki özet kartları arama/öğretmen daraltmasını takip etsin - dönem artık bir
  // daraltma boyutu olmadığı için tüm geçmiş bu kapsamda toplanır (aynı page.tsx'teki
  // tüm-okul toplamının daraltılmış hâli).
  const visibleStudentIds = useMemo(() => new Set(visibleStudents.map(({ student }) => student.id)), [visibleStudents]);
  const scopedDues = useMemo(() => (dues ?? []).filter((due) => visibleStudentIds.has(due.studentId)), [dues, visibleStudentIds]);
  const filterSummary = useMemo<BillingFilterSummary>(() => ({
    outstanding: scopedDues.filter((item) => isOpen(item.status)).reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0),
    collected: scopedDues.reduce((total, item) => total + item.totalPaid, 0),
    overdue: scopedDues.filter((item) => item.status === "Overdue").reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0),
    openCount: scopedDues.filter((item) => isOpen(item.status)).length,
    overdueCount: scopedDues.filter((item) => item.status === "Overdue").length,
  }), [scopedDues]);

  useEffect(() => onSummaryChange?.(filterSummary), [filterSummary, onSummaryChange]);

  const hasActiveFilters = teacherFilter !== "all" || studentSearch.trim() !== "";

  return <div className="space-y-4">
    <Modal open={showCreatePanel} title="Tahsilat kaydet" description="Öğrenciyi, ilk dönemi ve kaç aylık ödeme yaptığını seç; aylar otomatik olarak ödendi işaretlensin." onClose={() => setShowCreatePanel(false)}>
      <QuickCollectPanel
        pickerRef={studentPickerRef}
        onOpenFullAccount={(studentId) => { setShowCreatePanel(false); setSelectedStudentId(studentId); }}
        onCollected={() => setShowCreatePanel(false)}
      />
    </Modal>

    {selectedStudentId && <StudentBillingSection key={selectedStudentId} initialStudentId={selectedStudentId} showStudentPicker={false} onClose={() => setSelectedStudentId(null)} />}

    <section className="app-card overflow-hidden">
      <div className="flex flex-wrap items-center gap-2 border-b border-[var(--line)] bg-[var(--surface-muted)]/30 px-4 py-3">
        <p className="text-meta mr-auto"><strong className="text-[var(--foreground)]">{visibleStudents.length}</strong> öğrenci gösteriliyor</p>
        <label className="min-w-0 flex-1 basis-40"><span className="sr-only">Öğretmene göre filtrele</span><select value={teacherFilter} onChange={(event) => setTeacherFilter(event.target.value)} className="field min-h-11 text-xs font-semibold"><option value="all">Tüm öğretmenler</option>{teachers?.filter((teacher) => teacher.status === "Active").map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>)}</select></label>
        <label className="relative min-w-0 flex-1 basis-40"><span className="sr-only">Öğrenci adına göre ara</span><Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--muted)]" /><input type="search" value={studentSearch} onChange={(event) => setStudentSearch(event.target.value)} placeholder="Öğrenci ara…" className="field min-h-11 pl-9 text-xs font-semibold" /></label>
        <button type="button" onClick={startAddingDue} className="btn btn-primary"><Icon name="plus" className="h-4 w-4" />Tahsilat kaydet</button>
      </div>

      <div className="hidden grid-cols-[minmax(12rem,1.4fr)_minmax(10rem,.9fr)_minmax(9rem,.8fr)_auto] gap-3 border-b border-t border-[var(--line)] bg-[var(--surface-muted)]/55 px-4 py-2.5 text-[.75rem] font-bold uppercase tracking-[.08em] text-[var(--muted)] md:grid"><span>Öğrenci</span><span>Kurslar</span><span>Bu ay</span><span className="text-right">İşlem</span></div>
      {isLoading && <div className="space-y-2 p-4">{[1, 2, 3, 4].map((item) => <div key={item} className="skeleton h-16 rounded-xl" />)}</div>}
      {!isLoading && isError && <div className="grid min-h-52 place-items-center p-8 text-center"><div><span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--danger-soft)] text-[var(--danger-strong)]"><Icon name="x" className="h-5 w-5" /></span><p className="mt-3 text-sm font-bold">Öğrenci listesi yüklenemedi</p><p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p><button type="button" onClick={() => void refetch()} disabled={isFetching} className="btn btn-quiet mt-3 disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button></div></div>}
      {!isLoading && !isError && visibleStudents.length > 0 && <ul className="divide-y divide-[var(--line)]">{visibleStudents.map(({ student, instruments }) => <StudentRow key={student.id} student={student} instruments={instruments} thisMonthDues={duesByStudentThisPeriod.get(student.id) ?? []} />)}</ul>}
      {!isLoading && !isError && !visibleStudents.length && <div className="grid min-h-52 place-items-center p-8 text-center"><div>
        <span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="wallet" className="h-5 w-5" /></span>
        {!overviews?.length
          ? <p className="mt-3 text-sm font-bold">Henüz öğrenci yok</p>
          : <>
              <p className="mt-3 text-sm font-bold">Eşleşen öğrenci yok</p>
              <p className="text-meta mt-1">{hasActiveFilters ? "Seçili filtrelerle eşleşen öğrenci bulunamadı." : "Öğrenci listesi boş."}</p>
              {hasActiveFilters && <button type="button" onClick={clearFilters} className="btn btn-quiet mt-3">Filtreleri temizle</button>}
            </>}
      </div></div>}
    </section>

  </div>;
}

// Bir öğrenci satırı: ad, aktif kursları, bu ayki aidat durumu (triyaj için - hangi
// öğrenciyi araması gerektiğini görmek için tek tek "Detay" açmasına gerek kalmasın) ve
// geçmiş dahil tüm dönemleri açan "Detay" butonu.
function StudentRow({ student, instruments, thisMonthDues }: { student: Student; instruments: StudentInstrumentSummary[]; thisMonthDues: BillingDue[] }) {
  const [showDetail, setShowDetail] = useState(false);
  const totalDue = thisMonthDues.reduce((total, due) => total + due.amount, 0);
  const totalPaid = thisMonthDues.reduce((total, due) => total + due.totalPaid, 0);
  const hasRecord = thisMonthDues.length > 0;
  const isPaid = totalDue > 0 && totalPaid >= totalDue;
  const isPartial = totalPaid > 0 && !isPaid;
  const overdueDues = thisMonthDues.filter((due) => due.status === "Overdue");
  const isOverdue = overdueDues.length > 0;
  const worstDueDate = overdueDues.map((due) => due.dueDate).sort().at(0);
  const lateDays = worstDueDate ? daysOverdue(worstDueDate) : 0;

  const stateStatus: BillingDue["status"] = !hasRecord ? "Unpaid" : isPaid ? "Paid" : isPartial ? "Partial" : isOverdue ? "Overdue" : "Unpaid";
  const stateLabel = !hasRecord ? "Bu ay kayıt yok" : isPaid ? "Bu ay ödendi" : isPartial ? "Kısmi ödendi" : isOverdue ? `${lateDays} gün gecikti` : "Ödenmedi";

  return <li>
    <div className="grid items-center gap-3 px-4 py-3 md:grid-cols-[minmax(12rem,1.4fr)_minmax(10rem,.9fr)_minmax(9rem,.8fr)_auto]">
      <strong className="min-w-0 truncate text-sm">{student.firstName} {student.lastName}</strong>
      <span className="text-meta truncate">{instruments.map((item) => item.instrumentName).join(", ") || "Kurs yok"}</span>
      <span className={`inline-flex w-fit rounded-full px-2 py-1 text-[.75rem] font-bold ${hasRecord ? STATUS_TONES[stateStatus] : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{stateLabel}</span>
      <div className="flex justify-end"><button type="button" onClick={() => setShowDetail((visible) => !visible)} aria-expanded={showDetail} className="btn btn-quiet text-xs">Detay<Icon name="chevron" className={`h-3 w-3 shrink-0 transition-transform ${showDetail ? "rotate-90" : ""}`} /></button></div>
    </div>
    {showDetail && <div className="px-4 pb-4"><PaymentHistoryCollapse studentId={student.id} /></div>}
  </li>;
}

// "Öğrenci aidat verecek" akışının tamamı tek ekranda: öğrenciyi seç, kursu seç, ilk dönemi
// ve kaç ay ödendiğini gir, "Elden alındı" / "Havale geldi" bas.
//
// Tutarı EKRAN HESAPLAMAZ. Taban tarife, öğrenci indirimi (2 kurs / kardeş / elle) ve peşin
// ödeme kademesi sunucudaki tek bir hesaptan (`/prepay-preview`) gelir; burada yalnızca
// gösterilir ve onaylanırken aynı toplam geri gönderilir. Ekranla sunucu ayrışmışsa
// (araya giren bir tarife değişikliği) işlem durur, yanlış tutarla tahsilat yazılmaz.
function QuickCollectPanel({
  pickerRef,
  onOpenFullAccount,
  onCollected,
}: {
  pickerRef: React.RefObject<HTMLSelectElement | null>;
  onOpenFullAccount: (studentId: string) => void;
  onCollected: () => void;
}) {
  const { data: students } = useStudents();
  const { data: instruments } = useInstruments();
  const { data: teachers } = useTeachers();
  const [studentId, setStudentId] = useState("");
  const [rawEnrollmentId, setRawEnrollmentId] = useState("");
  const { data: enrollments, isLoading: enrollmentsLoading } = useEnrollments(studentId);
  const { data: billing } = useStudentBilling(studentId, { enabled: !!studentId });
  const recordPayment = useRecordPayment(studentId);
  const [pendingMethod, setPendingMethod] = useState<PaymentMethod | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [startPeriod, setStartPeriod] = useState(() => currentPeriod());
  const [months, setMonths] = useState(1);

  const activeEnrollments = useMemo(
    () => enrollments?.filter((enrollment) => enrollment.status === "Active") ?? [],
    [enrollments],
  );

  // Öğrenci değişince önceki seçim otomatik geçersiz kalır - effect'e gerek yok.
  const enrollmentId = activeEnrollments.some((enrollment) => enrollment.id === rawEnrollmentId)
    ? rawEnrollmentId
    : (activeEnrollments.length === 1 ? activeEnrollments[0].id : "");

  const hasStartPeriod = isValidPeriod(startPeriod);
  const prepay = useCreatePrepayPlan(studentId, enrollmentId);
  const { data: preview, isLoading: previewLoading } = usePrepayPreview(enrollmentId, startPeriod, months, {
    enabled: !!enrollmentId && hasStartPeriod,
  });

  // Tek ay ve o ayın aidatı zaten kısmen ödenmişse: kampanya akışı bu satıra dokunamaz
  // (tahsil edilmiş parayla tutarsız bir tutar yazmamak için, bkz. Receivable.Reprice).
  // Bu durumda yapılacak iş zaten sadece KALANI tahsil etmek.
  const existing = billing
    ?.find((row) => row.enrollmentId === enrollmentId)
    ?.receivables.find((receivable) => hasStartPeriod && receivable.period === startPeriod && receivable.status !== "Cancelled");
  const settleRemainingOnly = months === 1 && !!existing && existing.status !== "Paid";
  const remaining = existing ? Math.max(0, existing.amount - existing.totalPaid) : 0;

  const payableTotal = settleRemainingOnly ? remaining : preview?.total ?? 0;
  const currency = preview?.currency ?? existing?.currency ?? "TRY";
  const blockers = settleRemainingOnly ? [] : preview?.blockers ?? [];
  const busy = pendingMethod !== null;

  function enrollmentLabel(enrollment: { instrumentId: string; teacherId: string; courseKind?: string }) {
    const instrument = instruments?.find((item) => item.id === enrollment.instrumentId)?.name;
    const teacher = teachers?.find((item) => item.id === enrollment.teacherId);
    const teacherName = teacher ? `${teacher.firstName} ${teacher.lastName}` : null;
    const kind = enrollment.courseKind === "Group" ? "Grup" : null;
    return [instrument, teacherName, kind].filter(Boolean).join(" · ") || "Kurs";
  }

  async function collect(method: PaymentMethod) {
    if (!enrollmentId) return;
    if (!hasStartPeriod) {
      setError("Ödeme alabilmek için ilk dönemi seçin.");
      return;
    }
    setError(null);
    setPendingMethod(method);
    try {
      if (settleRemainingOnly && existing) {
        await recordPayment.mutateAsync({
          receivableId: existing.id,
          amount: remaining,
          paymentDate: new Date().toISOString().slice(0, 10),
          method,
        });
      } else {
        await prepay.mutateAsync({
          startPeriod,
          months,
          paymentDate: new Date().toISOString().slice(0, 10),
          method,
          expectedTotal: preview?.total,
        });
      }
      onCollected();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme kaydedilemedi.");
    } finally {
      setPendingMethod(null);
    }
  }

  return <div className="mt-4 max-w-xl space-y-3">
    <label className="form-label block">
      <span>Öğrenci</span>
      <select ref={pickerRef} value={studentId} onChange={(event) => { setStudentId(event.target.value); setRawEnrollmentId(""); setError(null); }} className="field min-h-11 text-sm">
        <option value="">Öğrenci seç…</option>
        {students?.filter((student) => student.status === "Active").map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}
      </select>
    </label>

    {studentId && enrollmentsLoading && <div className="skeleton h-24 rounded-xl" />}

    {studentId && !enrollmentsLoading && activeEnrollments.length === 0 && (
      <p className="rounded-xl bg-[var(--surface-muted)] p-3 text-xs text-[var(--muted)]">Bu öğrencinin aktif kaydı bulunmuyor.</p>
    )}

    {studentId && activeEnrollments.length > 1 && (
      <label className="form-label block">
        <span>Hangi kurs?</span>
        <select value={enrollmentId} onChange={(event) => setRawEnrollmentId(event.target.value)} className="field min-h-11 text-sm">
          <option value="">Kurs seç…</option>
          {activeEnrollments.map((enrollment) => <option key={enrollment.id} value={enrollment.id}>{enrollmentLabel(enrollment)}</option>)}
        </select>
      </label>
    )}

    {enrollmentId && (
      <div className="rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/60 p-4">
        <div className="grid gap-2 sm:grid-cols-2">
          <label className="form-label">İlk dönem
            <input type="month" value={startPeriod} onChange={(event) => { setStartPeriod(event.target.value); setError(null); }} required aria-invalid={!hasStartPeriod} className="field min-h-11 bg-white text-xs" />
          </label>
          <label className="form-label">Kaç aylık ödeme?
            <input type="number" inputMode="numeric" min={1} max={24} value={months} onChange={(event) => { setMonths(normalizeMonthCount(event.target.value, 24)); setError(null); }} className="field min-h-11 bg-white text-xs" />
          </label>
        </div>

        {hasStartPeriod && previewLoading && !settleRemainingOnly && <div className="skeleton mt-3 h-20 rounded-xl" />}

        {/* Dönem temizlendiğinde özet kutusu (ve içindeki açıklama) tamamen kayboluyordu:
            tahsilat düğmeleri sessizce pasifleşiyor, kullanıcı SEBEBİNİ göremiyordu.
            Boş dönem artık kendi uyarısını basıyor. */}
        {!hasStartPeriod && <p role="alert" className="mt-3 rounded-lg bg-[var(--warning-soft)] px-3 py-2 text-xs font-semibold text-[var(--warning-strong)]">İlk dönemi seçin — bu alan boşken tahsilat kaydedilemez.</p>}

        {hasStartPeriod && (preview || settleRemainingOnly) && <div className="mt-3 rounded-xl bg-white p-3">
          <p className="text-[.75rem] font-semibold capitalize text-[var(--muted)]">
            {months === 1
              ? formatPeriod(startPeriod)
              : `${formatPeriod(preview?.monthRows[0]?.period ?? startPeriod)} – ${formatPeriod(preview?.monthRows.at(-1)?.period)}`}
            {" · "}{months} ay
          </p>

          {/* İndirim satırları: veliye "neden bu kadar" sorusunun cevabı ekranda duruyor. */}
          {!settleRemainingOnly && preview && <dl className="mt-2 space-y-1 text-[.75rem]">
            <div className="flex justify-between gap-2"><dt className="text-[var(--muted)]">Tarife toplamı</dt><dd className="tabular-nums">{formatMoney(preview.baseTotal, preview.currency)}</dd></div>
            {preview.studentDiscountPercent > 0 && <div className="flex justify-between gap-2"><dt className="text-[var(--muted)]">{preview.studentDiscountReason}</dt><dd className="tabular-nums text-[var(--success-strong)]">−%{preview.studentDiscountPercent}</dd></div>}
            {preview.prepayPercent > 0 && <div className="flex justify-between gap-2"><dt className="text-[var(--muted)]">Peşin ödeme indirimi</dt><dd className="tabular-nums text-[var(--success-strong)]">−%{preview.prepayPercent}</dd></div>}
          </dl>}

          <div className="mt-2 flex flex-wrap items-end justify-between gap-2 border-t border-[var(--line)] pt-2">
            <strong className="text-lg tabular-nums text-[var(--brand-strong)]">{formatMoney(payableTotal, currency)}</strong>
            {settleRemainingOnly
              ? <span className="rounded-full bg-[var(--warning-soft)] px-2.5 py-1 text-[.75rem] font-bold text-[var(--warning-strong)]">Kalan bakiye</span>
              : preview && preview.savingTotal > 0
                ? <span className="rounded-full bg-[var(--success-soft)] px-2.5 py-1 text-[.75rem] font-bold text-[var(--success-strong)]">{formatMoney(preview.savingTotal, preview.currency)} indirim</span>
                : null}
          </div>
        </div>}

        {blockers.length > 0 && <p role="alert" className="mt-3 rounded-lg bg-[var(--warning-soft)] px-3 py-2 text-xs font-semibold text-[var(--warning-strong)]">{blockers.join(" ")} İlk dönemi değiştirin.</p>}

        <div className="mt-3 flex flex-wrap gap-2">
          <button type="button" onClick={() => void collect("Cash")} disabled={!hasStartPeriod || busy || blockers.length > 0 || payableTotal <= 0} className="pressable min-h-11 flex-1 rounded-xl bg-[var(--success-strong)] px-4 text-sm font-bold text-white disabled:opacity-50">{pendingMethod === "Cash" ? "Kaydediliyor…" : months > 1 ? `${months} ayı nakit ödendi` : "Elden alındı"}</button>
          <button type="button" onClick={() => void collect("Transfer")} disabled={!hasStartPeriod || busy || blockers.length > 0 || payableTotal <= 0} className="pressable min-h-11 flex-1 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white disabled:opacity-50">{pendingMethod === "Transfer" ? "Kaydediliyor…" : months > 1 ? `${months} aylık havale geldi` : "Havale geldi"}</button>
        </div>
        {error && <p role="alert" className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
      </div>
    )}

    {studentId && <button type="button" onClick={() => onOpenFullAccount(studentId)} className="inline-flex min-h-11 items-center text-left text-[.75rem] font-bold text-[var(--brand-strong)] underline underline-offset-2">Farklı dönem eklemek veya indirimi değiştirmek için tam hesabı aç →</button>}
  </div>;
}

// Bir öğrenci satırının "Detay"ı: soldaki 12 aylık takvim dönemlerin ödeme durumunu,
// sağdaki panel ise seçili ayın tahsilat ayrıntısını ve (ödenmemişse) tahsilat formunu
// gösterir. Veri yalnızca detay açıldığında çekilir; sayfadaki her satır için gizli istek
// atılmaz.
function PaymentHistoryCollapse({ studentId }: { studentId: string }) {
  const { data: billing, isLoading, isError } = useStudentBilling(studentId);
  const { data: instruments } = useInstruments();
  const [selectedPeriod, setSelectedPeriod] = useState<string | null>(null);

  const rowsByPeriod = useMemo(() => {
    const grouped = new Map<string, Array<{ receivable: NonNullable<typeof billing>[number]["receivables"][number]; instrumentName: string }>>();
    for (const row of billing ?? []) {
      const instrumentName = instruments?.find((instrument) => instrument.id === row.instrumentId)?.name ?? "Kurs";
      for (const receivable of row.receivables) {
        const rows = grouped.get(receivable.period) ?? [];
        rows.push({ receivable, instrumentName });
        grouped.set(receivable.period, rows);
      }
    }
    return grouped;
  }, [billing, instruments]);

  const periods = useMemo(() => [...rowsByPeriod.keys()].filter(isValidPeriod).sort(), [rowsByPeriod]);
  const years = useMemo(() => [...new Set(periods.map((period) => Number(period.slice(0, 4))))].sort((a, b) => a - b), [periods]);
  const fallbackPeriod = useMemo(() => [...periods].reverse().find((period) =>
    rowsByPeriod.get(period)?.some(({ receivable }) => receivable.payments.some((payment) => payment.kind === "Payment"))) ?? periods.at(-1) ?? null,
  [periods, rowsByPeriod]);
  const activePeriod = isValidPeriod(selectedPeriod) ? selectedPeriod : fallbackPeriod;
  const activeYear = activePeriod ? Number(activePeriod.slice(0, 4)) : years.at(-1) ?? new Date().getFullYear();
  const activeYearIndex = years.indexOf(activeYear);
  const selectedRows = activePeriod ? rowsByPeriod.get(activePeriod) ?? [] : [];

  const prepayCoverage = useMemo(() => {
    const coverage = new Map<string, Set<string>>();
    for (const [period, rows] of rowsByPeriod) {
      for (const { receivable } of rows) {
        for (const payment of receivable.payments) {
          if (!payment.prepayPlanId) continue;
          const periodsForPayment = coverage.get(payment.prepayPlanId) ?? new Set<string>();
          periodsForPayment.add(period);
          coverage.set(payment.prepayPlanId, periodsForPayment);
        }
      }
    }
    return new Map([...coverage].map(([id, coveredPeriods]) => [id, [...coveredPeriods].sort()]));
  }, [rowsByPeriod]);

  function selectYear(nextYearIndex: number) {
    const year = years[nextYearIndex];
    if (!year) return;
    const yearPeriods = periods.filter((period) => period.startsWith(`${year}-`));
    const latestPaidPeriod = [...yearPeriods].reverse().find((period) =>
      rowsByPeriod.get(period)?.some(({ receivable }) => receivable.payments.some((payment) => payment.kind === "Payment")));
    setSelectedPeriod(latestPaidPeriod ?? yearPeriods.at(-1) ?? `${year}-01`);
  }

  return <div className="rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/60 p-3">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <div><p className="text-micro text-[var(--brand-strong)]">Aidat takvimi</p><p className="text-meta mt-0.5">Bir aya dokunarak ödeme ayrıntısını gör, ödenmemişse tahsilat al.</p></div>
      {!!years.length && <div className="flex items-center gap-1" role="group" aria-label="Ödeme takvimi yılı">
        <button type="button" onClick={() => selectYear(activeYearIndex - 1)} disabled={activeYearIndex <= 0} className="icon-btn icon-btn-quiet disabled:opacity-35" aria-label="Önceki yıl"><Icon name="arrow-left" className="h-3.5 w-3.5" /></button>
        <strong className="min-w-14 text-center text-xs tabular-nums">{activeYear}</strong>
        <button type="button" onClick={() => selectYear(activeYearIndex + 1)} disabled={activeYearIndex < 0 || activeYearIndex >= years.length - 1} className="icon-btn icon-btn-quiet disabled:opacity-35" aria-label="Sonraki yıl"><Icon name="arrow-right" className="h-3.5 w-3.5" /></button>
      </div>}
    </div>
    {isLoading && <div className="mt-2 space-y-1.5">{[1, 2].map((item) => <div key={item} className="skeleton h-9 rounded-lg" />)}</div>}
    {!isLoading && isError && <p className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">Ödeme geçmişi yüklenemedi.</p>}
    {!isLoading && !isError && !periods.length && <p className="text-meta mt-2">Bu öğrenci için kayıtlı bir aidat dönemi yok.</p>}
    {!isLoading && !isError && periods.length > 0 && <div className="mt-3 grid gap-3 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,.85fr)]">
      <section className="rounded-xl border border-[var(--line)] bg-white p-3" aria-label={`${activeYear} ödeme takvimi`}>
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
          {MONTHS_TR.map((monthName, monthIndex) => {
            const period = `${activeYear}-${String(monthIndex + 1).padStart(2, "0")}`;
            const rows = rowsByPeriod.get(period) ?? [];
            const activeReceivables = rows.filter(({ receivable }) => receivable.status !== "Cancelled");
            const totalDue = activeReceivables.reduce((total, { receivable }) => total + receivable.amount, 0);
            const totalPaid = activeReceivables.reduce((total, { receivable }) => total + receivable.totalPaid, 0);
            const isPaid = totalDue > 0 && totalPaid >= totalDue;
            const isPartial = totalPaid > 0 && !isPaid;
            const isOverdue = activeReceivables.some(({ receivable }) => receivable.status === "Overdue");
            const hasRecord = rows.length > 0;
            const prepayMonths = Math.max(0, ...rows.flatMap(({ receivable }) => receivable.payments.map((payment) => payment.prepayPlanMonths ?? 0)));
            const stateLabel = !hasRecord ? "Kayıt yok" : isPaid ? "Ödendi" : isPartial ? "Kısmi ödendi" : isOverdue ? "Vadesi geçti" : "Ödenmedi";
            const stateClass = isPaid
              ? "border-[var(--success)]/45 bg-[var(--success-soft)] text-[var(--success-strong)]"
              : isPartial
                ? "border-[var(--warning)]/45 bg-[var(--warning-soft)] text-[var(--warning-strong)]"
                : isOverdue
                  ? "border-[var(--danger)]/35 bg-[var(--danger-soft)] text-[var(--danger-strong)]"
                  : hasRecord
                    ? "border-[var(--line)] bg-[var(--surface-muted)] text-[var(--muted)]"
                    : "border-dashed border-[var(--line)] bg-white text-[var(--muted)]";
            return <button
              key={period}
              type="button"
              onClick={() => setSelectedPeriod(period)}
              aria-pressed={activePeriod === period}
              aria-label={`${formatPeriod(period)}: ${stateLabel}`}
              className={`pressable relative min-h-[5.5rem] rounded-xl border p-2 text-left ${stateClass} ${activePeriod === period ? "ring-2 ring-[var(--brand)] ring-offset-1" : ""}`}
            >
              <span className="flex items-center justify-between gap-1"><strong className="text-[.75rem]">{monthName}</strong><span className={`grid h-5 w-5 place-items-center rounded-full border ${isPaid ? "border-[var(--success-strong)] bg-[var(--success-strong)] text-white" : "border-current bg-white/60"}`}>{isPaid ? <Icon name="check" className="h-3 w-3" /> : <span className="h-1.5 w-1.5 rounded-full bg-current" />}</span></span>
              <span className="mt-2 block text-[.75rem] font-bold">{stateLabel}</span>
              {hasRecord && <span className="mt-0.5 block truncate text-[.75rem] tabular-nums">{formatMoney(isPaid || isPartial ? totalPaid : totalDue, activeReceivables[0]?.receivable.currency ?? "TRY")}</span>}
              {prepayMonths > 1 && <span className="mt-1 inline-flex rounded-full bg-[var(--brand-soft)] px-1.5 py-0.5 text-[.75rem] font-bold text-[var(--brand-strong)]">Peşin · {prepayMonths} ay</span>}
            </button>;
          })}
        </div>
        <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 border-t border-[var(--line)] pt-2 text-[.75rem] font-semibold text-[var(--muted)]">
          <span className="inline-flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-[var(--success-strong)]" />Ödendi</span>
          <span className="inline-flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-[var(--warning-strong)]" />Kısmi</span>
          <span className="inline-flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-[var(--danger-strong)]" />Vadesi geçti</span>
          <span className="inline-flex items-center gap-1"><span className="h-2 w-2 rounded-full border border-[var(--line)] bg-white" />Kayıt yok</span>
        </div>
      </section>

      <section className="rounded-xl border border-[var(--line)] bg-white p-4" aria-live="polite">
        <p className="text-micro text-[var(--brand-strong)]">Seçili dönem</p>
        <h4 className="mt-1 font-serif text-base font-bold capitalize">{formatPeriod(activePeriod)}</h4>
        {!selectedRows.length && <div className="mt-4 grid min-h-36 place-items-center rounded-xl bg-[var(--surface-muted)] p-4 text-center"><div><Icon name="calendar" className="mx-auto h-5 w-5 text-[var(--muted)]" /><p className="text-meta mt-2">Bu ay için aidat kaydı bulunmuyor.</p></div></div>}
        {!!selectedRows.length && <div className="mt-3 space-y-2.5">
          {selectedRows.map(({ receivable, instrumentName }) => (
            <ReceivablePeriodCard key={receivable.id} studentId={studentId} receivable={receivable} instrumentName={instrumentName} prepayCoverage={prepayCoverage} />
          ))}
        </div>}
      </section>
    </div>}
  </div>;
}

// Seçili dönemin bir kursa ait tek satırı: ayrıntı + (ödenmemişse) doğrudan burada
// tahsilat alma formu. Önceden bu panel salt-okunurdu, tahsilat almak için takvimi kapatıp
// ana listedeki satıra dönmek gerekiyordu - kullanıcı isteği üzerine aya tıklamak artık
// tahsilata da yetiyor.
function ReceivablePeriodCard({
  studentId,
  receivable,
  instrumentName,
  prepayCoverage,
}: {
  studentId: string;
  receivable: Receivable;
  instrumentName: string;
  prepayCoverage: Map<string, string[]>;
}) {
  const recordPayment = useRecordPayment(studentId);
  const [showForm, setShowForm] = useState(false);
  const [amount, setAmount] = useState(Math.max(0, receivable.amount - receivable.totalPaid));
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [paymentDate, setPaymentDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<string | null>(null);
  const remaining = Math.max(0, receivable.amount - receivable.totalPaid);
  const canCollect = receivable.status !== "Paid" && receivable.status !== "Cancelled";

  async function collect(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await recordPayment.mutateAsync({ receivableId: receivable.id, amount, paymentDate, method });
      setShowForm(false);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme kaydedilemedi.");
    }
  }

  return <article className="@container rounded-xl border border-[var(--line)] p-3">
    <div className="flex flex-wrap items-start justify-between gap-2">
      <span><strong className="block text-xs">{instrumentName}</strong><span className="text-meta mt-0.5 block">Vade {formatDay(receivable.dueDate)}</span></span>
      <span className="text-right"><strong className="block text-sm tabular-nums">{formatMoney(receivable.amount, receivable.currency)}</strong><span className={`text-[.75rem] font-bold ${remaining ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>{remaining ? `${formatMoney(remaining, receivable.currency)} kaldı` : "Tamamı ödendi"}</span></span>
      {canCollect && <button type="button" onClick={() => setShowForm((visible) => !visible)} className="btn btn-primary">Tahsilat</button>}
    </div>

    {/* Sütun sayısı viewport'a göre DEĞİL, kartın kendi genişliğine göre (container query):
        bu form takvimin dar "Seçili dönem" panelinin içinde render ediliyor - `sm:` kırılma
        noktası panel daraldığında taşma yapıyordu, sabit iki sütun da 360px telefonda sığmıyordu. */}
    {showForm && <form onSubmit={collect} className="mt-3 grid grid-cols-1 gap-2 @xs:grid-cols-2 rounded-xl border border-[var(--brand)]/25 bg-[var(--brand-soft)]/45 p-3">
      <label className="form-label">Tutar<input type="number" inputMode="decimal" min={0.01} max={remaining} step={0.01} value={amount} onChange={resetValidity((event) => setAmount(Number(event.target.value)))} onInvalid={onInvalidTurkish} required className="field min-h-11 w-full bg-white text-xs" /></label>
      <label className="form-label">Tarih<input type="date" value={paymentDate} onChange={(event) => setPaymentDate(event.target.value)} required className="field min-h-11 w-full bg-white text-xs" /></label>
      <label className="form-label @xs:col-span-2">Yöntem<select value={method} onChange={(event) => setMethod(event.target.value as PaymentMethod)} className="field min-h-11 w-full bg-white text-xs"><option value="Cash">Nakit</option><option value="Transfer">Havale</option><option value="Card">Kart</option><option value="Other">Diğer</option></select></label>
      <button type="submit" disabled={recordPayment.isPending} className="btn btn-primary @xs:col-span-2">{recordPayment.isPending ? "Kaydediliyor…" : "Ödemeyi kaydet"}</button>
      {error && <p role="alert" className="text-xs font-semibold text-[var(--danger-strong)] @xs:col-span-2">{error}</p>}
    </form>}

    {!receivable.payments.length && <p className="mt-3 rounded-lg bg-[var(--surface-muted)] px-2.5 py-2 text-[.75rem] font-semibold text-[var(--muted)]">Henüz ödeme alınmadı.</p>}
    {!!receivable.payments.length && <div className="mt-3 space-y-1.5 border-t border-[var(--line)] pt-2.5">
      {receivable.payments.map((payment) => {
        const coveredPeriods = payment.prepayPlanId ? prepayCoverage.get(payment.prepayPlanId) ?? [] : [];
        return <div key={payment.id} className="rounded-lg bg-[var(--surface-muted)] px-2.5 py-2 text-[.75rem]">
          <div className="flex flex-wrap items-center justify-between gap-1.5"><span className="font-semibold">{payment.paymentDate} · {paymentMethodLabel(payment.method)}</span><strong className="tabular-nums">{payment.kind === "Correction" && payment.previousAmount != null ? `${payment.previousAmount.toLocaleString("tr-TR")} → ` : ""}{payment.amount.toLocaleString("tr-TR")} {receivable.currency}</strong></div>
          <div className="mt-1 flex flex-wrap items-center gap-1.5">
            {payment.kind === "Correction" && <span className="rounded-full bg-[var(--warning-soft)] px-1.5 py-0.5 font-bold text-[var(--warning-strong)]">Düzeltme</span>}
            {payment.prepayPlanId && <span className="rounded-full bg-[var(--brand-soft)] px-1.5 py-0.5 font-bold text-[var(--brand-strong)]">Peşin ödeme · {payment.prepayPlanMonths} ay</span>}
            {coveredPeriods.length > 1 && <span className="text-[var(--muted)]">{formatPeriod(coveredPeriods[0])} – {formatPeriod(coveredPeriods.at(-1))}</span>}
          </div>
        </div>;
      })}
    </div>}
  </article>;
}
