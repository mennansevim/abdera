"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { FormMessage, Modal, onInvalidTurkish, resetValidity } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  COURSE_KIND_LABEL,
  useBillingDues,
  useCreatePrepayPlan,
  useMonthlyDuePlan,
  usePrepayPreview,
  useRecordPayment,
  useRunMonthlyDues,
  useStudentBilling,
  type BillingDue,
  type MonthlyDueResult,
  type PaymentMethod,
} from "@/lib/billing";
import { formatDay, formatMoney, formatPeriod, isValidPeriod } from "@/lib/billing-format";
import { useEnrollments, useInstruments, useStudents, useTeachers } from "@/lib/people";
import { StudentBillingSection } from "./student-billing-section";

// Ekran bir "dönem defteri": aynı anda tek bir dönemi gösterir.
//
// Önceki sürüm üç ayrı yerde karmaşıklaşmıştı ve üçü de ölçülebilir bir soruna karşılık
// geliyordu:
//   1. İki arama kutusu vardı; biri "Yeni aidat / Öğrenci hesabından ekle" başlıklı bir
//      kartın içindeydi ama yanındaki iki açılır liste aslında ALTTAKİ listeyi filtreliyordu.
//      Kayıt ekleme formu gibi duran kartın üçte ikisi filtreydi.
//   2. Beş durum sekmesi çakışıyordu: gecikmiş bir aidat hem "Açık aidatlar"da hem
//      "Vadesi geçen"de sayılıyordu, sayılar toplama vurmuyordu (17+6+6+12 = 41 ≠ 29).
//   3. "Dönem" ve "Vade" sütunları aynı değeri onlarca satır boyunca tekrarlıyordu.
//
// Çözüm sırasıyla: tek arama kutusu + oluşturma akışını istek üzerine açılan panele almak;
// birbirini dışlayan üç sekme (Bekleyen + Ödenen = Tümü); dönem ve vadeyi sütundan çıkarıp
// başlıkta bir kez yazmak.
//
// Aidat modeli yeniden tasarlandıktan sonra (docs/10-decisions.md H1) bu ekrandan bir şey
// daha kalktı: "ücret planı". Artık bir öğrenciden tahsilat almak için önce fiyat listesi
// kurup her kurs kaydına ayrı plan açmak gerekmiyor - tutar okulun tarifesinden ve
// öğrencinin indirimlerinden sunucuda hesaplanıyor, ekran yalnızca sonucu teyit ediyor.

type DueFilter = "open" | "paid" | "all";

export interface BillingFilterSummary {
  outstanding: number;
  collected: number;
  overdue: number;
  openCount: number;
  overdueCount: number;
}

const ALL_PERIODS = "all";
const MONTHS_TR = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];

// Birbirini dışlayan üç sekme: Bekleyen + Ödenen = Tümü. Gecikme artık ayrı bir sekme
// değil, satırdaki kırmızı rozet - zaten liste gecikmişten başlıyor.
const FILTERS: Array<{ value: DueFilter; label: string }> = [
  { value: "open", label: "Bekleyen" },
  { value: "paid", label: "Ödenen" },
  { value: "all", label: "Tümü" },
];

const STATUS_LABELS: Record<BillingDue["status"], string> = {
  Unpaid: "Ödenmedi",
  Partial: "Kısmi ödendi",
  Paid: "Ödendi",
  Overdue: "Vadesi geçti",
  Cancelled: "İptal",
};

const STATUS_TONES: Record<BillingDue["status"], string> = {
  Unpaid: "bg-[var(--surface-muted)] text-[var(--muted)]",
  Partial: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  Paid: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  Overdue: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  Cancelled: "bg-[var(--surface-muted)] text-[var(--muted)]",
};

// Yöneticinin ilk sorusu "kimi önce arayayım" - en çok geciken en üstte.
const STATUS_ORDER: Record<BillingDue["status"], number> = {
  Overdue: 0, Partial: 1, Unpaid: 2, Paid: 3, Cancelled: 4,
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
  const { data: dues, isLoading, isError, isFetching, refetch } = useBillingDues();
  const { data: teachers } = useTeachers();
  const [filter, setFilter] = useState<DueFilter>("open");
  const [period, setPeriod] = useState<string | null>(null);
  // selectedStudentId artık yalnızca "tam hesabı aç" kaçış kapısı için tutuluyor - hızlı
  // tahsilat panelinin (QuickCollectPanel) kendi öğrenci seçimi ayrıdır.
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null);
  const [showCreatePanel, setShowCreatePanel] = useState(false);
  const [showMonthlyRun, setShowMonthlyRun] = useState(false);
  const [teacherFilter, setTeacherFilter] = useState("all");
  const [studentSearch, setStudentSearch] = useState("");

  // Dönem listesi veriden türetilir - okul ölçeğinde tüm aidatlar zaten tek istekte
  // geliyor, ayrı bir uç nokta açmaya gerek yok (CLAUDE.md: gereksiz bağımlılık ekleme).
  const periods = useMemo(
    () => [...new Set((dues ?? []).map((due) => due.period))].sort().reverse(),
    [dues],
  );

  // Varsayılan dönem "içinde bulunduğumuz ay" - kullanıcının zihnindeki dönem bu. En son
  // dönemi seçmek yanlış olurdu: veride ileri tarihli tek satırlık dönemler olabiliyor ve
  // ekran ilk açılışta boş görünürdü.
  //
  // Varsayılan bir effect'te state'e YAZILMAZ, türetilir: veri geç geldiğinde effect'le
  // yazmak fazladan bir render turu ve "önce boş, sonra dolu" titremesi üretirdi.
  // `period` yalnızca kullanıcı seçim yaptığında dolar.
  const defaultPeriod = useMemo(() => {
    if (!periods.length) return ALL_PERIODS;
    const currentPeriod = new Date().toISOString().slice(0, 7);
    return periods.find((item) => item <= currentPeriod) ?? periods[0];
  }, [periods]);
  const activePeriod = period ?? defaultPeriod;

  const studentPickerRef = useRef<HTMLSelectElement>(null);
  const startAddingDue = useCallback(() => {
    setShowCreatePanel(true);
    // Pencere açıldıktan sonra odaklan - aksi halde eleman henüz DOM'da olmuyor.
    window.setTimeout(() => studentPickerRef.current?.focus(), 0);
  }, []);

  const clearFilters = useCallback(() => {
    setFilter("all");
    setTeacherFilter("all");
    setStudentSearch("");
    setPeriod(ALL_PERIODS);
  }, []);

  // Dönem DIŞINDAKİ daraltmalar ayrı tutulur: aşağıdaki "başka dönemde gecikmiş var"
  // uyarısı bu kümeye bakar, çünkü tam da dönem filtresinin gizlediği şeyi göstermesi gerekir.
  const duesMatchingFilters = useMemo(() => (dues ?? []).filter((due) => {
    const matchesTeacher = teacherFilter === "all" || due.teacherId === teacherFilter;
    const query = studentSearch.trim().toLocaleLowerCase("tr-TR");
    const matchesStudent = !query || due.studentName.toLocaleLowerCase("tr-TR").includes(query);
    return matchesTeacher && matchesStudent;
  }), [dues, studentSearch, teacherFilter]);

  // Liste, sayaçlar ve toplamlar TEK bir filtrelenmiş diziden türetilir - ekranda bir
  // rakam, listede başka bir veri kümesi olmasın.
  const baseDues = useMemo(
    () => duesMatchingFilters.filter((due) => activePeriod === ALL_PERIODS || due.period === activePeriod),
    [activePeriod, duesMatchingFilters]);

  // Dönem defterinin tek gerçek riski: geçmiş bir dönemde kalan gecikmiş aidat, "bu ay"
  // görünümünde tamamen gözden kaybolur - üstelik en acil iş odur. Ekran bu parayı asla
  // sessizce saklamamalı, o yüzden kapsam dışında kalan gecikmişler burada duyurulur.
  const overdueOutsideScope = useMemo(() => {
    if (activePeriod === ALL_PERIODS) return null;
    const rows = duesMatchingFilters.filter((due) => due.status === "Overdue" && due.period !== activePeriod);
    if (!rows.length) return null;
    return {
      count: rows.length,
      amount: rows.reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0),
    };
  }, [activePeriod, duesMatchingFilters]);

  const counts = useMemo(() => ({
    open: baseDues.filter((due) => isOpen(due.status)).length,
    paid: baseDues.filter((due) => due.status === "Paid").length,
    all: baseDues.filter((due) => due.status !== "Cancelled").length,
  }), [baseDues]);

  const filterSummary = useMemo<BillingFilterSummary>(() => ({
    outstanding: baseDues.filter((item) => isOpen(item.status)).reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0),
    collected: baseDues.reduce((total, item) => total + item.totalPaid, 0),
    overdue: baseDues.filter((item) => item.status === "Overdue").reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0),
    openCount: baseDues.filter((item) => isOpen(item.status)).length,
    overdueCount: baseDues.filter((item) => item.status === "Overdue").length,
  }), [baseDues]);

  useEffect(() => onSummaryChange?.(filterSummary), [filterSummary, onSummaryChange]);

  // Dönem başlığındaki vade: seçili dönemdeki tüm aidatlar aynı vadeyi paylaşıyorsa bir
  // kez yazılır. Bu, satırlardan kaldırılan "Vade" sütununun karşılığı.
  const sharedDueDate = useMemo(() => {
    const dates = [...new Set(baseDues.map((due) => due.dueDate))];
    return dates.length === 1 ? dates[0] : null;
  }, [baseDues]);

  const visibleDues = useMemo(() => baseDues
    .filter((due) => filter === "all" ? due.status !== "Cancelled" : filter === "open" ? isOpen(due.status) : due.status === "Paid")
    .sort((a, b) =>
      STATUS_ORDER[a.status] - STATUS_ORDER[b.status] ||
      a.dueDate.localeCompare(b.dueDate) ||
      a.studentName.localeCompare(b.studentName, "tr-TR")),
    [baseDues, filter]);

  const hasActiveFilters = teacherFilter !== "all" || studentSearch.trim() !== "" || filter !== "all";

  return <div className="space-y-4">
    {/* Yalnızca açıkken monte edilir: pencere her açılışta temiz durumla (o anki dönem,
        önceki çalıştırmanın sonucu olmadan) başlasın diye - state'i effect'te sıfırlamak
        yerine bileşeni yeniden kurmak hem daha basit hem fazladan render turu üretmiyor. */}
    {showMonthlyRun && <MonthlyDueRunDialog
      defaultPeriod={activePeriod === ALL_PERIODS ? new Date().toISOString().slice(0, 7) : activePeriod}
      onClose={() => setShowMonthlyRun(false)}
      onGoToStudent={(studentId) => { setShowMonthlyRun(false); setSelectedStudentId(studentId); }}
    />}

    <Modal open={showCreatePanel} title="Tahsilat kaydet" description="Öğrenciyi, ilk dönemi ve kaç aylık ödeme yaptığını seç; aylar otomatik olarak ödendi işaretlensin." onClose={() => setShowCreatePanel(false)}>
      <QuickCollectPanel
        pickerRef={studentPickerRef}
        onOpenFullAccount={(studentId) => { setShowCreatePanel(false); setSelectedStudentId(studentId); }}
        onCollected={() => setShowCreatePanel(false)}
      />
    </Modal>

    {selectedStudentId && <StudentBillingSection key={selectedStudentId} initialStudentId={selectedStudentId} showStudentPicker={false} onClose={() => setSelectedStudentId(null)} />}

    <section className="app-card overflow-hidden">
      {/* Dönem başlığı: sütunlardan kaldırılan Dönem ve Vade bilgisini bir kez taşır.
          "+ Aidat al" da buraya taşındı - önceki başlık/açıklama/arama bloğu tamamen
          kaldırıldı (kullanıcı isteği: gereksiz tekrar, tek bir eylem yeterli). */}
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-b border-[var(--line)] bg-[var(--surface-muted)] px-4 py-3">
        <label className="flex items-center gap-2">
          <span className="sr-only">Döneme göre filtrele</span>
          <select value={activePeriod} onChange={(event) => setPeriod(event.target.value)} className="min-h-9 rounded-lg border border-[var(--line)] bg-white px-2.5 font-serif text-sm font-semibold capitalize">
            <option value={ALL_PERIODS}>Tüm dönemler</option>
            {periods.map((item) => <option key={item} value={item} className="capitalize">{formatPeriod(item)}</option>)}
          </select>
        </label>
        <div className="flex flex-wrap items-center gap-3">
          <p className="text-[.75rem] font-semibold tabular-nums text-[var(--muted)]">
            {sharedDueDate && <>Vade {formatDay(sharedDueDate)} · </>}
            {counts.all} aidat
            {filterSummary.overdue > 0 && <> · <span className="font-bold text-[var(--danger-strong)]">{formatMoney(filterSummary.overdue, "TRY")} gecikmiş</span></>}
            {filterSummary.outstanding > 0 && <> · {formatMoney(filterSummary.outstanding, "TRY")} açık</>}
          </p>
          <button type="button" onClick={() => setShowMonthlyRun(true)} className="btn btn-quiet">Aylık aidatları oluştur</button>
          <button type="button" onClick={startAddingDue} className="btn btn-primary"><Icon name="plus" className="h-4 w-4" />Tahsilat kaydet</button>
        </div>
      </div>

      {overdueOutsideScope && <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-[var(--danger)]/25 bg-[var(--danger-soft)] px-4 py-2.5">
        <Icon name="bell" className="h-4 w-4 shrink-0 text-[var(--danger-strong)]" />
        <p className="text-xs font-bold tabular-nums text-[var(--danger-strong)]">
          Başka dönemlerde {overdueOutsideScope.count} gecikmiş aidat var · {formatMoney(overdueOutsideScope.amount, "TRY")}
        </p>
        <button type="button" onClick={() => { setPeriod(ALL_PERIODS); setFilter("open"); }} className="pressable ml-auto min-h-9 rounded-lg bg-[var(--danger-strong)] px-3 text-[.75rem] font-bold text-white">Hepsini göster</button>
      </div>}

      {/* Durum sekmeleri solda, Öğretmen/Öğrenci filtreleri sağda - kullanıcı isteği. */}
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 pt-3">
        <div className="inline-flex gap-1 rounded-xl bg-[var(--surface-muted)] p-1" aria-label="Aidat durum filtresi">{FILTERS.map((item) => <button key={item.value} type="button" onClick={() => setFilter(item.value)} aria-pressed={filter === item.value} className={`pressable flex min-h-9 shrink-0 items-center gap-2 rounded-lg px-3 text-xs font-bold ${filter === item.value ? "bg-white text-[var(--brand-strong)] shadow-sm" : "text-[var(--muted)] hover:text-[var(--foreground)]"}`}>{item.label}<span className="rounded-full bg-[var(--surface-muted)] px-1.5 py-0.5 text-[.75rem] tabular-nums">{counts[item.value]}</span></button>)}</div>
        <div className="flex flex-wrap items-center gap-2">
          <label className="min-w-[9rem]"><span className="sr-only">Öğretmene göre filtrele</span><select value={teacherFilter} onChange={(event) => setTeacherFilter(event.target.value)} className="field min-h-9 text-xs font-semibold"><option value="all">Tüm öğretmenler</option>{teachers?.filter((teacher) => teacher.status === "Active").map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>)}</select></label>
          <label className="relative min-w-[12rem]"><span className="sr-only">Öğrenci adına göre ara</span><Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--muted)]" /><input type="search" value={studentSearch} onChange={(event) => setStudentSearch(event.target.value)} placeholder="Öğrenci ara…" className="field min-h-9 pl-9 text-xs font-semibold" /></label>
        </div>
      </div>

      {/* Beş sütun: Dönem ve Vade artık yukarıdaki dönem başlığında. */}
      <div className="mt-3 hidden grid-cols-[minmax(12rem,1.4fr)_minmax(9rem,.9fr)_minmax(9rem,.9fr)_minmax(8rem,.8fr)_auto] gap-3 border-b border-t border-[var(--line)] bg-[var(--surface-muted)]/55 px-4 py-2.5 text-[.75rem] font-bold uppercase tracking-[.08em] text-[var(--muted)] md:grid"><span>Öğrenci / enstrüman</span><span>Öğretmen</span><span>Tutar / kalan</span><span>Durum</span><span className="text-right">İşlem</span></div>
      {isLoading && <div className="space-y-2 p-4">{[1, 2, 3, 4].map((item) => <div key={item} className="skeleton h-16 rounded-xl" />)}</div>}
      {!isLoading && isError && <div className="grid min-h-52 place-items-center p-8 text-center"><div><span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--danger-soft)] text-[var(--danger-strong)]"><Icon name="x" className="h-5 w-5" /></span><p className="mt-3 text-sm font-bold">Aidat listesi yüklenemedi</p><p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p><button type="button" onClick={() => void refetch()} disabled={isFetching} className="pressable mt-3 min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--foreground)] disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button></div></div>}
      {!isLoading && !isError && visibleDues.length > 0 && <div className="divide-y divide-[var(--line)]">{visibleDues.map((due) => <DueRow key={due.id} due={due} />)}</div>}
      {!isLoading && !isError && !visibleDues.length && <div className="grid min-h-52 place-items-center p-8 text-center"><div>
        <span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="wallet" className="h-5 w-5" /></span>
        {!dues?.length
          ? <>
              <p className="mt-3 text-sm font-bold">Henüz aidat kaydı yok</p>
              <p className="text-meta mt-1">Bu ayın aidatlarını tek düğmeyle aç - tutarlar okulun tarifesinden, indirimler otomatik hesaplanır. Aidat oluşturulduğunda tahsilat, kısmi ödeme ve gecikme takibi buradan yürür.</p>
              <button type="button" onClick={() => setShowMonthlyRun(true)} className="btn btn-primary mt-3">Dönem aidatı oluştur</button>
            </>
          : <>
              <p className="mt-3 text-sm font-bold">Bu dönemde aidat yok</p>
              <p className="text-meta mt-1">{hasActiveFilters ? "Seçili filtrelerle eşleşen aidat bulunamadı." : "Bu dönem için henüz aidat oluşturulmamış."} Başka bir dönem seçebilir veya yeni bir dönem aidatı ekleyebilirsin.</p>
              <div className="mt-3 flex flex-wrap items-center justify-center gap-2">
                <button type="button" onClick={clearFilters} className="btn btn-quiet">Filtreleri temizle</button>
                <button type="button" onClick={() => setShowMonthlyRun(true)} className="btn btn-primary">Dönem aidatı oluştur</button>
              </div>
            </>}
      </div></div>}
    </section>

  </div>;
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
  const [startPeriod, setStartPeriod] = useState(() => new Date().toISOString().slice(0, 7));
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
            <input type="month" value={startPeriod} onChange={(event) => { setStartPeriod(event.target.value); setError(null); }} required aria-invalid={!hasStartPeriod} className="field min-h-10 bg-white text-xs" />
          </label>
          <label className="form-label">Kaç aylık ödeme?
            <input type="number" min={1} max={24} value={months} onChange={(event) => { setMonths(normalizeMonthCount(event.target.value, 24)); setError(null); }} className="field min-h-10 bg-white text-xs" />
          </label>
        </div>

        {previewLoading && !settleRemainingOnly && <div className="skeleton mt-3 h-20 rounded-xl" />}

        {(preview || settleRemainingOnly) && <div className="mt-3 rounded-xl bg-white p-3">
          <p className="text-[.75rem] font-semibold capitalize text-[var(--muted)]">
            {!hasStartPeriod
              ? "İlk dönemi seçin"
              : months === 1
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

    {studentId && <button type="button" onClick={() => onOpenFullAccount(studentId)} className="text-[.75rem] font-bold text-[var(--brand-strong)] underline underline-offset-2">Farklı dönem eklemek veya indirimi değiştirmek için tam hesabı aç →</button>}
  </div>;
}

function DueRow({ due }: { due: BillingDue }) {
  const recordPayment = useRecordPayment(due.studentId);
  const [showPayment, setShowPayment] = useState(false);
  const [showHistory, setShowHistory] = useState(false);
  const [amount, setAmount] = useState(Math.max(0, due.amount - due.totalPaid));
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [paymentDate, setPaymentDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<string | null>(null);
  const remaining = Math.max(0, due.amount - due.totalPaid);
  const canCollect = due.status !== "Paid" && due.status !== "Cancelled";
  const lateDays = due.status === "Overdue" ? daysOverdue(due.dueDate) : 0;

  async function collect(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await recordPayment.mutateAsync({ receivableId: due.id, amount, paymentDate, method });
      setShowPayment(false);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme kaydedilemedi.");
    }
  }

  return <article className="px-4 py-3">
    <div className="grid items-center gap-3 md:grid-cols-[minmax(12rem,1.4fr)_minmax(9rem,.9fr)_minmax(9rem,.9fr)_minmax(8rem,.8fr)_auto]">
      <div className="flex min-w-0 items-center gap-3"><span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[.75rem] font-bold text-[var(--brand-strong)]">{due.studentName.split(" ").map((part) => part[0]).slice(0, 2).join("")}</span><span className="min-w-0"><strong className="block truncate text-sm">{due.studentName}</strong><span className="text-meta mt-0.5 block truncate">{due.instrumentName}</span></span></div>
      <div className="text-xs"><span className="text-[.75rem] font-bold text-[var(--muted)] md:hidden">Öğretmen · </span>{due.teacherName}</div>
      <div>
        <strong className="block text-xs tabular-nums">{formatMoney(due.amount, due.currency)}</strong>
        <span className={`mt-0.5 block text-[.75rem] tabular-nums ${remaining ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>{remaining ? `${formatMoney(remaining, due.currency)} kaldı` : "Tamamı ödendi"}</span>
        {/* "Bu 5.700 TL nereden geldi" sorusunun cevabı satırın kendisinde - başka
            ekrana gitmeye gerek yok (yeni modelde hesap aidat satırına donuyor). */}
        {due.discountPercent > 0 && <span className="text-meta mt-0.5 block truncate" title={`${formatMoney(due.baseAmount, due.currency)} üzerinden ${due.discountReason ?? ""}`}>
          <s className="opacity-60">{formatMoney(due.baseAmount, due.currency)}</s> · {due.discountReason ?? `%${due.discountPercent} indirim`}
        </span>}
      </div>
      <div><span className={`inline-flex rounded-full px-2 py-1 text-[.75rem] font-bold ${STATUS_TONES[due.status]}`}>{lateDays > 0 ? `${lateDays} gün gecikti` : STATUS_LABELS[due.status]}</span></div>
      <div className="flex justify-end gap-1.5">
        {canCollect && <button type="button" onClick={() => setShowPayment((visible) => !visible)} className="btn btn-primary">Tahsilat</button>}
        {/* "Hesap" yerine "Geçmiş": ayrı bir üst panel açmak yerine satırın hemen altında
            katlanır (collapse) bir bölüm olarak, yalnızca bu öğrencinin eski dönem
            ödemelerini gösterir - kullanıcı isteği üzerine sadeleştirildi. */}
        <button type="button" onClick={() => setShowHistory((visible) => !visible)} aria-expanded={showHistory} className="pressable inline-flex min-h-9 items-center gap-1 rounded-lg border border-[var(--line)] bg-white px-3 text-[.75rem] font-bold text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]">Geçmiş<Icon name="chevron" className={`h-3 w-3 shrink-0 transition-transform ${showHistory ? "rotate-90" : ""}`} /></button>
      </div>
    </div>
    {showPayment && <form onSubmit={collect} className="mt-3 grid gap-2 rounded-xl border border-[var(--brand)]/25 bg-[var(--brand-soft)]/45 p-3 sm:grid-cols-[1fr_1fr_1fr_auto] sm:items-end"><label className="form-label">Tutar<input type="number" min={0.01} max={remaining} step={0.01} value={amount} onChange={resetValidity((event) => setAmount(Number(event.target.value)))} onInvalid={onInvalidTurkish} required className="field min-h-10 bg-white text-xs" /></label><label className="form-label">Tarih<input type="date" value={paymentDate} onChange={(event) => setPaymentDate(event.target.value)} required className="field min-h-10 bg-white text-xs" /></label><label className="form-label">Yöntem<select value={method} onChange={(event) => setMethod(event.target.value as PaymentMethod)} className="field min-h-10 bg-white text-xs"><option value="Cash">Nakit</option><option value="Transfer">Havale</option><option value="Card">Kart</option><option value="Other">Diğer</option></select></label><button type="submit" disabled={recordPayment.isPending} className="btn btn-primary">{recordPayment.isPending ? "Kaydediliyor…" : "Ödemeyi kaydet"}</button>{error && <p role="alert" className="text-xs font-semibold text-[var(--danger-strong)] sm:col-span-4">{error}</p>}</form>}
    {showHistory && <PaymentHistoryCollapse studentId={due.studentId} />}
  </article>;
}

// Ana listedeki bir satırın altında açılır; soldaki 12 aylık takvim dönemlerin
// ödeme durumunu, sağdaki panel ise seçili ayın tahsilat ayrıntısını gösterir. Veri
// yalnızca collapse açıldığında çekilir; sayfadaki her satır için gizli istek atılmaz.
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

  return <div className="mt-3 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/60 p-3">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <div><p className="text-micro text-[var(--brand-strong)]">Geçmiş ödemeler</p><p className="text-meta mt-0.5">Bir aya dokunarak ödeme ayrıntısını gör.</p></div>
      {!!years.length && <div className="flex items-center gap-1" aria-label="Ödeme takvimi yılı">
        <button type="button" onClick={() => selectYear(activeYearIndex - 1)} disabled={activeYearIndex <= 0} className="pressable grid h-8 w-8 place-items-center rounded-lg border border-[var(--line)] bg-white text-[var(--muted)] disabled:opacity-35" aria-label="Önceki yıl"><Icon name="arrow-left" className="h-3.5 w-3.5" /></button>
        <strong className="min-w-14 text-center text-xs tabular-nums">{activeYear}</strong>
        <button type="button" onClick={() => selectYear(activeYearIndex + 1)} disabled={activeYearIndex < 0 || activeYearIndex >= years.length - 1} className="pressable grid h-8 w-8 place-items-center rounded-lg border border-[var(--line)] bg-white text-[var(--muted)] disabled:opacity-35" aria-label="Sonraki yıl"><Icon name="arrow-right" className="h-3.5 w-3.5" /></button>
      </div>}
    </div>
    {isLoading && <div className="mt-2 space-y-1.5">{[1, 2].map((item) => <div key={item} className="skeleton h-9 rounded-lg" />)}</div>}
    {!isLoading && isError && <p className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">Ödeme geçmişi yüklenemedi.</p>}
    {!isLoading && !isError && !periods.length && <p className="text-meta mt-2">Bu öğrenci için kayıtlı bir aidat dönemi yok.</p>}
    {!isLoading && !isError && periods.length > 0 && <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(22rem,1.15fr)_minmax(18rem,.85fr)]">
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
          {selectedRows.map(({ receivable, instrumentName }) => {
            const remaining = Math.max(0, receivable.amount - receivable.totalPaid);
            return <article key={receivable.id} className="rounded-xl border border-[var(--line)] p-3">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <span><strong className="block text-xs">{instrumentName}</strong><span className="text-meta mt-0.5 block">Vade {formatDay(receivable.dueDate)}</span></span>
                <span className="text-right"><strong className="block text-sm tabular-nums">{formatMoney(receivable.amount, receivable.currency)}</strong><span className={`text-[.75rem] font-bold ${remaining ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>{remaining ? `${formatMoney(remaining, receivable.currency)} kaldı` : "Tamamı ödendi"}</span></span>
              </div>
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
          })}
        </div>}
      </section>
    </div>}
  </div>;
}

// Ay başının tek düğmesi: seçilen ayın aidatlarını tüm aktif kurs kayıtları için açar.
// İşlemden ÖNCE ne olacağını gösterir - kaç aidat açılacak, TOPLAM NE KADAR İNDİRİM
// uygulanacak, hangileri zaten var ve en önemlisi hangileri açılamıyor.
//
// Eskiden "eksik" listesi ücret planı olmayan kayıtları gösteriyordu ve bu liste pratikte
// hep doluydu (her kayıt için ayrı plan açmak gerekiyordu). Artık tek eksik sebebi kalmış
// olabilir: o ders türü için yürürlükte bir tarifenin olmaması - yani ayda bir değil,
// sezonda bir karşılaşılacak bir durum.
function MonthlyDueRunDialog({
  defaultPeriod,
  onClose,
  onGoToStudent,
}: {
  defaultPeriod: string;
  onClose: () => void;
  onGoToStudent: (studentId: string) => void;
}) {
  const [period, setPeriod] = useState(defaultPeriod);
  const [result, setResult] = useState<MonthlyDueResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const { data: plan, isLoading } = useMonthlyDuePlan(period);
  const runMonthlyDues = useRunMonthlyDues();

  async function run() {
    setError(null);
    try {
      setResult(await runMonthlyDues.mutateAsync(period));
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Dönem aidatları oluşturulamadı.");
    }
  }

  const missing = result?.missing ?? plan?.missing ?? [];
  const discounted = plan?.ready.filter((row) => row.discountPercent > 0) ?? [];

  return (
    <Modal open title="Aylık aidatları oluştur" description="Seçilen ay için her aktif kursun BORÇ satırını açar - para tahsil etmez. Ödemeyi sonra listedeki satırdan alırsın; birkaç ayı birden tahsil edeceksen Toplu ödeme sekmesini kullan." onClose={onClose}>
      <div className="space-y-3.5">
        <label className="form-label sm:max-w-xs">Dönem
          <input type="month" value={period} onChange={(event) => { setPeriod(event.target.value); setResult(null); setError(null); }} className="field text-sm" />
        </label>

        {isLoading && <div className="skeleton h-20 rounded-xl" />}

        {!isLoading && plan && !result && (
          <>
            <div className="grid gap-2 sm:grid-cols-3">
              <SummaryTile label="Açılacak aidat" value={`${plan.ready.length}`} detail={plan.ready.length ? formatMoney(plan.readyTotal, plan.currency) : "—"} tone="brand" />
              <SummaryTile label="Uygulanan indirim" value={plan.readyDiscountTotal > 0 ? formatMoney(plan.readyDiscountTotal, plan.currency) : "—"} detail={discounted.length ? `${discounted.length} kursta` : "İndirim yok"} tone={plan.readyDiscountTotal > 0 ? "warning" : "muted"} />
              <SummaryTile label="Zaten var" value={`${plan.alreadyExists.length}`} detail="Bu dönemde açılmış" tone="muted" />
            </div>

            {plan.ready.length > 0 && <section className="rounded-xl border border-[var(--line)]">
              <div className="flex items-center justify-between gap-2 border-b border-[var(--line)] bg-[var(--surface-muted)]/60 px-3 py-2">
                <p className="text-[.75rem] font-bold text-[var(--muted)]">Açılacak aidatlar</p>
                <p className="text-[.75rem] font-semibold tabular-nums text-[var(--muted)]">Vade {formatDay(plan.dueDate)}</p>
              </div>
              <ul className="max-h-56 divide-y divide-[var(--line)] overflow-y-auto">
                {plan.ready.map((row) => (
                  <li key={row.enrollmentId} className="flex items-center justify-between gap-3 px-3 py-2 text-xs">
                    <span className="min-w-0">
                      <strong className="block truncate">{row.studentName}</strong>
                      <span className="text-meta block truncate">{row.instrumentName} · {COURSE_KIND_LABEL[row.courseKind]} · {row.teacherName}</span>
                    </span>
                    <span className="shrink-0 text-right">
                      <strong className="block tabular-nums">{formatMoney(row.amount, row.currency)}</strong>
                      {row.discountPercent > 0 && <span className="text-meta block truncate">{row.discountReason}</span>}
                    </span>
                  </li>
                ))}
              </ul>
            </section>}
          </>
        )}

        {result && (
          <FormMessage tone="success">
            {result.createdCount} aidat oluşturuldu · {formatMoney(result.createdTotal, result.currency)}
            {result.createdDiscountTotal > 0 && ` · ${formatMoney(result.createdDiscountTotal, result.currency)} indirim uygulandı`}
            {result.alreadyExistsCount > 0 && ` · ${result.alreadyExistsCount} kayıt zaten vardı`}
          </FormMessage>
        )}

        {missing.length > 0 && (
          <section className="rounded-xl border border-[var(--warning)]/40 bg-[var(--warning-soft)]/50 p-3">
            <p className="text-xs font-bold text-[var(--warning-strong)]">Aidatı açılamayan {missing.length} kurs</p>
            <p className="text-meta mt-0.5">Bu ders türü için o dönemde yürürlükte bir tarife yok. Fiyat politikası ekranından tarifeyi tanımla.</p>
            <ul className="mt-2 max-h-48 space-y-1 overflow-y-auto">
              {missing.map((row) => (
                <li key={row.enrollmentId}>
                  <button type="button" onClick={() => onGoToStudent(row.studentId)} className="pressable flex w-full items-center justify-between gap-2 rounded-lg bg-white px-3 py-2 text-left text-xs hover:bg-[var(--brand-soft)]">
                    <span className="min-w-0"><strong>{row.studentName}</strong><span className="text-meta"> · {row.instrumentName} · {row.teacherName}</span></span>
                    <span className="text-meta shrink-0">{row.reason}</span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        )}

        {error && <FormMessage tone="error">{error}</FormMessage>}

        <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-4">
          <button type="button" onClick={onClose} className="btn btn-quiet">{result ? "Kapat" : "Vazgeç"}</button>
          {!result && (
            <button type="button" onClick={run} disabled={runMonthlyDues.isPending || !plan?.ready.length} className="btn btn-primary">
              {runMonthlyDues.isPending ? "Oluşturuluyor…" : plan?.ready.length ? `${plan.ready.length} aidatı oluştur` : "Açılacak aidat yok"}
            </button>
          )}
        </div>
      </div>
    </Modal>
  );
}

function SummaryTile({ label, value, detail, tone }: { label: string; value: string; detail: string; tone: "brand" | "muted" | "warning" }) {
  const palette = {
    brand: "text-[var(--brand-strong)]",
    muted: "text-[var(--muted)]",
    warning: "text-[var(--warning-strong)]",
  }[tone];
  return (
    <div className="rounded-xl border border-[var(--line)] p-3">
      <p className="text-meta font-bold">{label}</p>
      <p className={`mt-1 text-lg font-bold tabular-nums ${palette}`}>{value}</p>
      <p className="text-meta mt-0.5 truncate">{detail}</p>
    </div>
  );
}
