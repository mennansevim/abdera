"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { MonthInput } from "@/components/month-input";
import { FormMessage, Modal, onInvalidTurkish, resetValidity } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  useBillingDues,
  useCorrectPayment,
  useCreatePrepayPlan,
  usePrepayPreview,
  useRecordPayment,
  useStudentBilling,
  type BillingDue,
  type PaymentMethod,
  type PaymentRecord,
  type Receivable,
} from "@/lib/billing";
import { commitmentEndPeriod, currentPeriod, formatDay, formatMoney, formatPeriod, isObligatedPeriod, isValidPeriod } from "@/lib/billing-format";
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

  // Satırdaki "son 3 ay" kutucukları: eskiden yalnızca bu ayın durumu yazıyordu ("23 gün
  // gecikti") - kullanıcı geri bildirimi: "bu gösterimin kimseye faydası yok". Üç ayı yan yana
  // görmek kimin düzenli ödediğini, kimin birikmiş borcu olduğunu tek bakışta söylüyor.
  const recentPeriods = useMemo(() => {
    const [year, month] = currentPeriod().split("-").map(Number);
    return [2, 1, 0].map((back) => {
      const date = new Date(year, month - 1 - back, 1);
      return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
    });
  }, []);
  const duesByStudentPeriod = useMemo(() => {
    const map = new Map<string, Map<string, BillingDue[]>>();
    for (const due of dues ?? []) {
      if (!recentPeriods.includes(due.period) || due.status === "Cancelled") continue;
      const byPeriod = map.get(due.studentId) ?? new Map<string, BillingDue[]>();
      byPeriod.set(due.period, [...(byPeriod.get(due.period) ?? []), due]);
      map.set(due.studentId, byPeriod);
    }
    return map;
  }, [dues, recentPeriods]);

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
      // Kullanıcı isteği: "ödeyenleri en altta göster, ödemeyenler ya da gecikenler yukarıda
      // olsun". Önce aciliyet (paymentRank), eşitlikte ad sırası.
      .sort((a, b) =>
        paymentRank(duesByStudentPeriod.get(a.student.id), recentPeriods, a.enrolledSince)
          - paymentRank(duesByStudentPeriod.get(b.student.id), recentPeriods, b.enrolledSince)
        || `${a.student.firstName} ${a.student.lastName}`.localeCompare(`${b.student.firstName} ${b.student.lastName}`, "tr-TR"));
  }, [overviews, studentSearch, teacherFilter, studentTeacherIds, duesByStudentPeriod, recentPeriods]);

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

      <div className="hidden grid-cols-[minmax(12rem,1.4fr)_minmax(10rem,.9fr)_minmax(9rem,.8fr)_auto] gap-3 border-b border-t border-[var(--line)] bg-[var(--surface-muted)]/55 px-4 py-2.5 text-[.75rem] font-bold uppercase tracking-[.08em] text-[var(--muted)] md:grid"><span>Öğrenci</span><span>Kurslar</span><span>Son 3 ay</span><span className="text-right">İşlem</span></div>
      {isLoading && <div className="space-y-2 p-4">{[1, 2, 3, 4].map((item) => <div key={item} className="skeleton h-16 rounded-xl" />)}</div>}
      {!isLoading && isError && <div className="grid min-h-52 place-items-center p-8 text-center"><div><span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--danger-soft)] text-[var(--danger-strong)]"><Icon name="x" className="h-5 w-5" /></span><p className="mt-3 text-sm font-bold">Öğrenci listesi yüklenemedi</p><p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p><button type="button" onClick={() => void refetch()} disabled={isFetching} className="btn btn-quiet mt-3 disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button></div></div>}
      {!isLoading && !isError && visibleStudents.length > 0 && <ul className="divide-y divide-[var(--line)]">{visibleStudents.map(({ student, instruments, enrolledSince }) => <StudentRow key={student.id} student={student} instruments={instruments} enrolledSince={enrolledSince} periods={recentPeriods} duesByPeriod={duesByStudentPeriod.get(student.id)} />)}</ul>}
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

// Kullanıcı kuralı: yalnızca iki durum var - ödendi (yeşil) ya da ödenmedi (kırmızı). Kısmi
// ödeme de ödenmemiş sayılır (kalan tutar ipucunda yazar); "aidat açılmadı" diye bir durum
// yoktur, kayıtlı öğrenci kayıt ayından itibaren her aydan sorumludur.
function isMonthPaid(dues: BillingDue[]) {
  const due = dues.reduce((total, item) => total + item.amount, 0);
  const paid = dues.reduce((total, item) => total + item.totalPaid, 0);
  return dues.length > 0 && due > 0 && paid >= due;
}

function monthTitle(period: string, dues: BillingDue[]) {
  if (isMonthPaid(dues)) {
    const paid = dues.reduce((total, item) => total + item.totalPaid, 0);
    return `${formatPeriod(period)}: Ödendi · ${formatMoney(paid, dues[0].currency)}`;
  }
  if (!dues.length) return `${formatPeriod(period)}: Ödenmedi`;
  const remaining = dues.reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0);
  const lateDue = dues.filter((item) => item.status === "Overdue").map((item) => item.dueDate).sort().at(0);
  return `${formatPeriod(period)}: Ödenmedi · ${formatMoney(remaining, dues[0].currency)} kaldı${lateDue ? ` · ${daysOverdue(lateDue)} gün gecikti` : ""}`;
}

function obligatedPeriods(periods: string[], enrolledSince: string | null) {
  return enrolledSince ? periods.filter((period) => isObligatedPeriod(period, { startedAt: enrolledSince })) : [];
}

// Listedeki sıra (kullanıcı isteği: "ödeyenleri en altta göster"): son 3 aydan ödenmemiş ay
// sayısı çok olan en üstte, hepsini ödemiş olan en altta; aktif kursu olmayan en sonda.
function paymentRank(duesByPeriod: Map<string, BillingDue[]> | undefined, periods: string[], enrolledSince: string | null) {
  const obligated = obligatedPeriods(periods, enrolledSince);
  if (!obligated.length) return 100;
  const unpaid = obligated.filter((period) => !isMonthPaid(duesByPeriod?.get(period) ?? [])).length;
  return unpaid > 0 ? 10 - unpaid : 50;
}

// Bir öğrenci satırı: ad, aktif kursları, son 3 ayın aidat kutucukları ve geçmiş dahil tüm
// dönemleri açan "Detay". Kutucuğa tıklamak takvimi o ay seçili olarak açar (oradan ödeme alınır).
function StudentRow({ student, instruments, enrolledSince, periods, duesByPeriod }: { student: Student; instruments: StudentInstrumentSummary[]; enrolledSince: string | null; periods: string[]; duesByPeriod?: Map<string, BillingDue[]> }) {
  const [detailPeriod, setDetailPeriod] = useState<string | null>(null);
  const [showDetail, setShowDetail] = useState(false);

  function openMonth(period: string) {
    setDetailPeriod(period);
    setShowDetail(true);
  }

  return <li>
    <div className="grid items-center gap-3 px-4 py-3 md:grid-cols-[minmax(12rem,1.4fr)_minmax(10rem,.9fr)_minmax(9rem,.8fr)_auto]">
      <strong className="min-w-0 truncate text-sm">{student.firstName} {student.lastName}</strong>
      <span className="text-meta truncate">{instruments.map((item) => item.instrumentName).join(", ") || "Kurs yok"}</span>
      <div className="flex gap-1.5" role="group" aria-label="Son 3 ayın aidat durumu">
        {!obligatedPeriods(periods, enrolledSince).length && <span className="text-meta">Aktif kurs yok</span>}
        {obligatedPeriods(periods, enrolledSince).map((period) => {
          const dues = duesByPeriod?.get(period) ?? [];
          const paid = isMonthPaid(dues);
          const title = monthTitle(period, dues);
          const monthName = MONTHS_TR[Number(period.slice(5, 7)) - 1];
          return (
            <button
              key={period}
              type="button"
              onClick={() => openMonth(period)}
              title={title}
              aria-label={title}
              className={`pressable grid h-10 min-w-12 place-items-center rounded-lg border px-2 text-[.75rem] font-extrabold ${paid ? "border-[var(--success)]/45 bg-[var(--success-soft)] text-[var(--success-strong)]" : "border-[var(--danger)]/40 bg-[var(--danger-soft)] text-[var(--danger-strong)]"} ${showDetail && detailPeriod === period ? "ring-2 ring-[var(--brand)] ring-offset-1" : ""}`}
            >
              <span className="flex items-center gap-1">
                <Icon name={paid ? "check" : "x"} className="h-3 w-3" aria-hidden="true" />
                {monthName}
              </span>
            </button>
          );
        })}
      </div>
      <div className="flex justify-end"><button type="button" onClick={() => { setDetailPeriod(null); setShowDetail((visible) => !visible); }} aria-expanded={showDetail} className="btn btn-quiet text-xs">Detay<Icon name="chevron" className={`h-3 w-3 shrink-0 transition-transform ${showDetail ? "rotate-90" : ""}`} /></button></div>
    </div>
    {showDetail && <div className="px-4 pb-4"><PaymentHistoryCollapse key={detailPeriod ?? "latest"} studentId={student.id} initialPeriod={detailPeriod} /></div>}
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
  initialStudentId,
  initialPeriod,
}: {
  pickerRef?: React.RefObject<HTMLSelectElement | null>;
  onOpenFullAccount?: (studentId: string) => void;
  onCollected: () => void;
  // Aidat takviminde bir aya tıklayıp "Ödeme yap" denince öğrenci ve dönem hazır gelir.
  initialStudentId?: string;
  initialPeriod?: string;
}) {
  const { data: students } = useStudents();
  const { data: instruments } = useInstruments();
  const { data: teachers } = useTeachers();
  const [studentId, setStudentId] = useState(initialStudentId ?? "");
  const [rawEnrollmentId, setRawEnrollmentId] = useState("");
  const { data: enrollments, isLoading: enrollmentsLoading } = useEnrollments(studentId);
  const { data: billing } = useStudentBilling(studentId, { enabled: !!studentId });
  const recordPayment = useRecordPayment(studentId);
  const [pendingMethod, setPendingMethod] = useState<PaymentMethod | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [startPeriod, setStartPeriod] = useState(() => initialPeriod ?? currentPeriod());
  const [months, setMonths] = useState(1);
  // Elle girilen tahsilat tutarı (kullanıcı isteği: "ödenecek rakamı o anda editleyebileyim,
  // küsüratlar değişebiliyor"). Hesaplandığı seçime (kurs + dönem + ay sayısı) bağlı tutulur:
  // seçim değişince eski rakam sessizce yeni hesaba taşınmaz, hesaplanan tutara dönülür.
  const [override, setOverride] = useState<{ key: string; value: number | "" } | null>(null);

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

  const computedTotal = settleRemainingOnly ? remaining : preview?.total ?? 0;
  const overrideKey = `${enrollmentId}|${startPeriod}|${months}|${settleRemainingOnly ? "rest" : "plan"}`;
  const activeOverride = override?.key === overrideKey ? override.value : null;
  const payableTotal = activeOverride === null || activeOverride === "" ? computedTotal : activeOverride;
  const isAdjusted = activeOverride !== null && activeOverride !== "" && activeOverride !== computedTotal;
  // Sınırlar sunucudakiyle aynı: kalan bakiyede kalanı, planda tarife toplamını aşamaz.
  const maxPayable = settleRemainingOnly ? remaining : preview?.baseTotal ?? 0;
  const amountInvalid = activeOverride === "" || payableTotal <= 0 || payableTotal > maxPayable;
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
          amount: payableTotal,
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
          ...(isAdjusted ? { agreedTotal: payableTotal } : {}),
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
      <select ref={pickerRef} value={studentId} disabled={!!initialStudentId} onChange={(event) => { setStudentId(event.target.value); setRawEnrollmentId(""); setError(null); }} className="field min-h-11 text-sm">
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
          <div className="form-label">İlk dönem
            <MonthInput label="İlk dönem" value={startPeriod} onChange={(value) => { setStartPeriod(value); setError(null); }} />
          </div>
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
            <span>
              {isAdjusted && <span className="block text-[.75rem] tabular-nums text-[var(--muted)] line-through">{formatMoney(computedTotal, currency)}</span>}
              <strong className="text-lg tabular-nums text-[var(--brand-strong)]">{formatMoney(payableTotal, currency)}</strong>
            </span>
            {settleRemainingOnly
              ? <span className="rounded-full bg-[var(--danger-soft)] px-2.5 py-1 text-[.75rem] font-bold text-[var(--danger-strong)]">Kalan bakiye</span>
              : preview && preview.baseTotal - payableTotal > 0
                ? <span className="rounded-full bg-[var(--success-soft)] px-2.5 py-1 text-[.75rem] font-bold text-[var(--success-strong)]">{formatMoney(preview.baseTotal - payableTotal, preview.currency)} indirim</span>
                : null}
          </div>

          <label className="form-label mt-3 block">
            <span>Tahsil edilen tutar</span>
            <input
              type="number"
              inputMode="decimal"
              min={0.01}
              max={maxPayable || undefined}
              step={0.01}
              value={activeOverride ?? computedTotal}
              onChange={(event) => { setOverride({ key: overrideKey, value: event.target.value === "" ? "" : Number(event.target.value) }); setError(null); }}
              aria-invalid={amountInvalid}
              className="field min-h-11 bg-white text-sm tabular-nums"
            />
          </label>
          {isAdjusted && !amountInvalid && <p className="mt-1.5 text-[.75rem] text-[var(--muted)]">
            {settleRemainingOnly
              ? "Kalandan az girersen ay kısmi ödendi olarak kalır."
              : `Hesaplanan ${formatMoney(computedTotal, currency)}. Aylar yine ödendi sayılır; fark aylara dağıtılıp "Tahsilatta elle düzeltme" olarak kaydedilir.`}
          </p>}
          {isAdjusted && <button type="button" onClick={() => setOverride(null)} className="mt-1 text-[.75rem] font-bold text-[var(--brand-strong)] underline underline-offset-2">Hesaplanan tutara dön</button>}
          {amountInvalid && activeOverride !== null && <p role="alert" className="mt-1.5 text-[.75rem] font-semibold text-[var(--danger-strong)]">
            Tutar 0&apos;dan büyük ve {formatMoney(maxPayable, currency)} ya da daha az olmalı.
          </p>}
        </div>}

        {blockers.length > 0 && <p role="alert" className="mt-3 rounded-lg bg-[var(--warning-soft)] px-3 py-2 text-xs font-semibold text-[var(--warning-strong)]">{blockers.join(" ")}</p>}

        <div className="mt-3 flex flex-wrap gap-2">
          <button type="button" onClick={() => void collect("Cash")} disabled={!hasStartPeriod || busy || blockers.length > 0 || amountInvalid} className="pressable min-h-11 flex-1 rounded-xl bg-[var(--success-strong)] px-4 text-sm font-bold text-white disabled:opacity-50">{pendingMethod === "Cash" ? "Kaydediliyor…" : months > 1 ? `${months} ayı nakit ödendi` : "Elden alındı"}</button>
          <button type="button" onClick={() => void collect("Transfer")} disabled={!hasStartPeriod || busy || blockers.length > 0 || amountInvalid} className="pressable min-h-11 flex-1 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white disabled:opacity-50">{pendingMethod === "Transfer" ? "Kaydediliyor…" : months > 1 ? `${months} aylık havale geldi` : "Havale geldi"}</button>
        </div>
        {error && <p role="alert" className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
      </div>
    )}

    {studentId && onOpenFullAccount && <button type="button" onClick={() => onOpenFullAccount(studentId)} className="inline-flex min-h-11 items-center text-left text-[.75rem] font-bold text-[var(--brand-strong)] underline underline-offset-2">Farklı dönem eklemek veya indirimi değiştirmek için tam hesabı aç →</button>}
  </div>;
}

// Bir öğrenci satırının "Detay"ı: soldaki 12 aylık takvim dönemlerin ödeme durumunu,
// sağdaki panel ise seçili ayın tahsilat ayrıntısını ve (ödenmemişse) tahsilat formunu
// gösterir. Veri yalnızca detay açıldığında çekilir; sayfadaki her satır için gizli istek
// atılmaz.
function PaymentHistoryCollapse({ studentId, initialPeriod = null }: { studentId: string; initialPeriod?: string | null }) {
  const { data: billing, isLoading, isError } = useStudentBilling(studentId);
  const { data: instruments } = useInstruments();
  const [selectedPeriod, setSelectedPeriod] = useState<string | null>(initialPeriod);
  const [payPeriod, setPayPeriod] = useState<string | null>(null);
  const thisPeriod = useMemo(() => currentPeriod(), []);

  // Kayıt ayından itibaren her ay öğrencinin sorumluluğunda (kullanıcı kuralı: "kayıt olan her
  // öğrenci aidata tabidir, önündeki 12 ay boyunca ödeme yapmak zorundadır"). Takvim bu
  // aralıktaki her ayı ödendi/ödenmedi gösterir; "açılmadı" diye bir durum yok.
  const isObligated = useCallback(
    (period: string) => (billing ?? []).some((row) => isObligatedPeriod(period, row)),
    [billing],
  );

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
  // Gezinilebilen yıllar: ilk kayıttan, taahhüt edilen 12 ayın sonuna (ya da bugüne) kadar.
  const years = useMemo(() => {
    const bounds = [...periods, thisPeriod];
    for (const row of billing ?? []) {
      if (!row.startedAt) continue;
      bounds.push(row.startedAt.slice(0, 7), commitmentEndPeriod(row.startedAt));
    }
    const sorted = bounds.filter(isValidPeriod).sort();
    const first = Number(sorted[0].slice(0, 4));
    const last = Number(sorted.at(-1)!.slice(0, 4));
    return Array.from({ length: last - first + 1 }, (_, index) => first + index);
  }, [billing, periods, thisPeriod]);
  const fallbackPeriod = isObligated(thisPeriod) ? thisPeriod : periods.at(-1) ?? thisPeriod;
  const activePeriod = isValidPeriod(selectedPeriod) ? selectedPeriod : fallbackPeriod;
  const activeYear = activePeriod ? Number(activePeriod.slice(0, 4)) : years.at(-1) ?? new Date().getFullYear();
  const activeYearIndex = years.indexOf(activeYear);
  const selectedRows = activePeriod ? rowsByPeriod.get(activePeriod) ?? [] : [];
  const selectedActive = selectedRows.filter(({ receivable }) => receivable.status !== "Cancelled");
  const selectedMonthPaid = selectedActive.length > 0
    && selectedActive.every(({ receivable }) => receivable.status === "Paid" || receivable.totalPaid >= receivable.amount);

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
    const yearPeriods = MONTHS_TR.map((_, index) => `${year}-${String(index + 1).padStart(2, "0")}`);
    setSelectedPeriod(yearPeriods.find((period) => isObligated(period)) ?? `${year}-01`);
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
    {!isLoading && !isError && !(billing ?? []).length && <p className="text-meta mt-2">Bu öğrencinin kurs kaydı yok.</p>}
    {!isLoading && !isError && (billing ?? []).length > 0 && <div className="mt-3 grid gap-3 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,.85fr)]">
      <section className="rounded-xl border border-[var(--line)] bg-white p-3" aria-label={`${activeYear} ödeme takvimi`}>
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
          {MONTHS_TR.map((monthName, monthIndex) => {
            const period = `${activeYear}-${String(monthIndex + 1).padStart(2, "0")}`;
            const rows = rowsByPeriod.get(period) ?? [];
            const activeReceivables = rows.filter(({ receivable }) => receivable.status !== "Cancelled");
            const totalDue = activeReceivables.reduce((total, { receivable }) => total + receivable.amount, 0);
            const totalPaid = activeReceivables.reduce((total, { receivable }) => total + receivable.totalPaid, 0);
            const obligated = isObligated(period) || activeReceivables.length > 0;
            const isPaid = activeReceivables.length > 0 && totalDue > 0 && totalPaid >= totalDue;
            const currency = activeReceivables[0]?.receivable.currency ?? "TRY";
            const prepayMonths = Math.max(0, ...rows.flatMap(({ receivable }) => receivable.payments.map((payment) => payment.prepayPlanMonths ?? 0)));
            if (!obligated) {
              // Kayıttan önceki (ya da biten kaydın sonrasındaki) ay: öğrencinin borcu yok.
              return <div key={period} aria-label={`${formatPeriod(period)}: kayıt dönemi dışında`} className="min-h-[5.5rem] rounded-xl border border-transparent bg-[var(--surface-muted)]/50 p-2 text-[var(--muted)]/60">
                <strong className="text-[.75rem]">{monthName}</strong>
              </div>;
            }
            const stateLabel = isPaid ? "Ödendi" : "Ödenmedi";
            return <button
              key={period}
              type="button"
              onClick={() => setSelectedPeriod(period)}
              aria-pressed={activePeriod === period}
              aria-label={`${formatPeriod(period)}: ${stateLabel}`}
              className={`pressable relative min-h-[5.5rem] rounded-xl border p-2 text-left ${isPaid ? "border-[var(--success)]/45 bg-[var(--success-soft)] text-[var(--success-strong)]" : "border-[var(--danger)]/35 bg-[var(--danger-soft)] text-[var(--danger-strong)]"} ${activePeriod === period ? "ring-2 ring-[var(--brand)] ring-offset-1" : ""}`}
            >
              <span className="flex items-center justify-between gap-1"><strong className="text-[.75rem]">{monthName}</strong><span className={`grid h-5 w-5 place-items-center rounded-full ${isPaid ? "bg-[var(--success-strong)]" : "bg-[var(--danger-strong)]"} text-white`}><Icon name={isPaid ? "check" : "x"} className="h-3 w-3" /></span></span>
              <span className="mt-2 block text-[.75rem] font-bold">{stateLabel}</span>
              {isPaid && <span className="mt-0.5 block truncate text-[.75rem] tabular-nums">{formatMoney(totalPaid, currency)}</span>}
              {!isPaid && totalDue > 0 && <span className="mt-0.5 block truncate text-[.75rem] tabular-nums">{formatMoney(Math.max(0, totalDue - totalPaid), currency)} kaldı</span>}
              {prepayMonths > 1 && <span className="mt-1 inline-flex rounded-full bg-[var(--brand-soft)] px-1.5 py-0.5 text-[.75rem] font-bold text-[var(--brand-strong)]">Peşin · {prepayMonths} ay</span>}
            </button>;
          })}
        </div>
        <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 border-t border-[var(--line)] pt-2 text-[.75rem] font-semibold text-[var(--muted)]">
          <span className="inline-flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-[var(--success-strong)]" />Ödendi</span>
          <span className="inline-flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-[var(--danger-strong)]" />Ödenmedi</span>
        </div>
      </section>

      <section className="rounded-xl border border-[var(--line)] bg-white p-4" aria-live="polite">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="text-micro text-[var(--brand-strong)]">Seçili dönem</p>
            <h4 className="mt-1 font-serif text-base font-bold capitalize">{formatPeriod(activePeriod)}</h4>
          </div>
          {/* Ay açılmamış ya da tam ödenmemişse doğrudan bu aya (ve istenirse sonraki aylara)
              ödeme alınır - kullanıcı isteği: "ayın üzerine tıklayınca ödeme yap butonu çıksın". */}
          {activePeriod && !selectedMonthPaid && (isObligated(activePeriod) || selectedActive.length > 0) && <button type="button" onClick={() => setPayPeriod(activePeriod)} className="btn btn-primary">
            <Icon name="wallet" className="h-4 w-4" />Ödeme yap
          </button>}
        </div>
        {!selectedRows.length && <div className={`mt-4 grid min-h-36 place-items-center rounded-xl p-4 text-center ${activePeriod && isObligated(activePeriod) ? "bg-[var(--danger-soft)]/60" : "bg-[var(--surface-muted)]"}`}><div>
          <Icon name={activePeriod && isObligated(activePeriod) ? "x" : "calendar"} className={`mx-auto h-5 w-5 ${activePeriod && isObligated(activePeriod) ? "text-[var(--danger-strong)]" : "text-[var(--muted)]"}`} />
          <p className="text-meta mt-2">{activePeriod && isObligated(activePeriod) ? "Bu ayın aidatı ödenmedi. \"Ödeme yap\" ile tahsil edebilirsin." : "Bu ay öğrencinin kayıt dönemi dışında."}</p>
        </div></div>}
        {!!selectedRows.length && <div className="mt-3 space-y-2.5">
          {selectedRows.map(({ receivable, instrumentName }) => (
            <ReceivablePeriodCard key={receivable.id} studentId={studentId} receivable={receivable} instrumentName={instrumentName} prepayCoverage={prepayCoverage} />
          ))}
        </div>}
      </section>
    </div>}
    <Modal
      open={payPeriod !== null}
      title="Ödeme yap"
      description="Dönem hazır geldi; birkaç ay birden ödendiyse ay sayısını artır. Tutarı gerekirse elle düzeltebilirsin."
      onClose={() => setPayPeriod(null)}
    >
      {payPeriod && <QuickCollectPanel key={payPeriod} initialStudentId={studentId} initialPeriod={payPeriod} onCollected={() => { setSelectedPeriod(payPeriod); setPayPeriod(null); }} />}
    </Modal>
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
  const [revertTarget, setRevertTarget] = useState<PaymentRecord | null>(null);
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
        const effective = payment.kind === "Payment" ? effectivePaymentAmount(receivable.payments, payment) : null;
        return <div key={payment.id} className="rounded-lg bg-[var(--surface-muted)] px-2.5 py-2 text-[.75rem]">
          <div className="flex flex-wrap items-center justify-between gap-1.5"><span className="font-semibold">{payment.paymentDate} · {paymentMethodLabel(payment.method)}</span><strong className="tabular-nums">{payment.kind === "Correction" && payment.previousAmount != null ? `${payment.previousAmount.toLocaleString("tr-TR")} → ` : ""}{payment.amount.toLocaleString("tr-TR")} {receivable.currency}</strong></div>
          <div className="mt-1 flex flex-wrap items-center gap-1.5">
            {payment.kind === "Correction" && <span className="rounded-full bg-[var(--surface)] px-1.5 py-0.5 font-bold text-[var(--muted)]">{payment.amount === 0 ? "Geri alındı" : "Düzeltme"}</span>}
            {effective === 0 && <span className="rounded-full bg-[var(--surface)] px-1.5 py-0.5 font-bold text-[var(--muted)]">Geri alındı</span>}
            {payment.prepayPlanId && <span className="rounded-full bg-[var(--brand-soft)] px-1.5 py-0.5 font-bold text-[var(--brand-strong)]">Peşin ödeme · {payment.prepayPlanMonths} ay</span>}
            {coveredPeriods.length > 1 && <span className="text-[var(--muted)]">{formatPeriod(coveredPeriods[0])} – {formatPeriod(coveredPeriods.at(-1))}</span>}
          </div>
          {effective !== null && effective > 0 && receivable.status !== "Cancelled" && <div className="mt-2 flex justify-end">
            <button type="button" onClick={() => setRevertTarget(payment)} className="btn btn-quiet text-xs">
              <Icon name="swap" className="h-4 w-4" />Ödemeyi geri al
            </button>
          </div>}
        </div>;
      })}
    </div>}
    <RevertPaymentDialog
      studentId={studentId}
      payment={revertTarget}
      currency={receivable.currency}
      period={receivable.period}
      onClose={() => setRevertTarget(null)}
    />
  </article>;
}

// Bir ödemenin bugünkü geçerli tutarı: en son düzeltme satırı (varsa) özgün tutarın yerine geçer.
function effectivePaymentAmount(rows: PaymentRecord[], payment: PaymentRecord) {
  const corrections = rows
    .filter((row) => row.kind === "Correction" && row.correctsPaymentId === payment.id)
    .sort((a, b) => (a.recordedAt ?? "").localeCompare(b.recordedAt ?? ""));
  return corrections.at(-1)?.amount ?? payment.amount;
}

// Yanlışlıkla kaydedilen tahsilatın geri alınması (yalnızca Admin - aidat ekranının tamamı
// Admin'e açık, sunucu da POST /api/payments/{id}/corrections'ı AdminOnly ile korur).
// Finansal kayıt SİLİNMEZ (CLAUDE.md): ödeme satırı yerinde kalır, tutarı gerekçeli bir
// düzeltmeyle 0'a çekilir, audit_log'a yazılır ve aidat bakiyesi geri açılır.
function RevertPaymentDialog({ studentId, payment, currency, period, onClose }: {
  studentId: string;
  payment: PaymentRecord | null;
  currency: string;
  period: string;
  onClose: () => void;
}) {
  const correctPayment = useCorrectPayment(studentId);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  function close() {
    setReason("");
    setError(null);
    onClose();
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!payment) return;
    setError(null);
    try {
      await correctPayment.mutateAsync({ paymentId: payment.id, correctedAmount: 0, reason: reason.trim() });
      close();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme geri alınamadı.");
    }
  }

  return <Modal open={payment !== null} title="Ödemeyi geri al" description={`${formatPeriod(period)} aidatı`} onClose={close} size="sm">
    {payment && <form onSubmit={submit} className="space-y-3">
      <p className="rounded-xl bg-[var(--surface-muted)] px-3 py-2.5 text-sm">
        <strong className="tabular-nums">{formatMoney(payment.amount, currency)}</strong> · {payment.paymentDate} · {paymentMethodLabel(payment.method)}
      </p>
      <p className="text-meta">Ödeme kaydı silinmez; tutarı sıfıra düzeltilir ve işlem kayıt altına alınır. Aidat yeniden ödenmedi durumuna döner, gerekirse doğru tahsilatı sonra alabilirsin.</p>
      {payment.prepayPlanId && <FormMessage tone="error">Bu ödeme {payment.prepayPlanMonths} aylık peşin ödemenin bir parçası. Yalnızca bu ayın payı geri alınır; diğer aylar etkilenmez.</FormMessage>}
      <label className="form-label">Geri alma nedeni
        <textarea value={reason} onChange={(event) => setReason(event.target.value)} required maxLength={500} rows={2} autoFocus className="field resize-y" placeholder="Ör. yanlış öğrenciye / yanlışlıkla ödendi olarak kaydedildi" />
      </label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-3">
        <button type="button" onClick={close} className="btn btn-quiet">Vazgeç</button>
        <button type="submit" disabled={correctPayment.isPending || !reason.trim()} className="btn bg-[var(--danger)] text-white hover:bg-[var(--danger-strong)]">
          {correctPayment.isPending ? "Geri alınıyor…" : "Ödemeyi geri al"}
        </button>
      </div>
    </form>}
  </Modal>;
}
