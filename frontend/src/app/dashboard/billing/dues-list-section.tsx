"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useQueries } from "@tanstack/react-query";
import { Icon } from "@/components/icons";
import { api, ApiError } from "@/lib/api";
import {
  useBillingDues,
  useCreateFeePlan,
  useCreateReceivable,
  usePriceLists,
  useRecordPayment,
  useStudentBilling,
  type BillingDue,
  type FeePlan,
  type PaymentMethod,
} from "@/lib/billing";
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
//   3. "Dönem" ve "Vade" sütunları aynı değeri onlarca satır boyunca tekrarlıyordu
//      (demo veride 12 satır "Ağustos 2026 / 7 Eylül", 12 satır "Temmuz 2026 / 13 Ağustos").
//
// Çözüm sırasıyla: tek arama kutusu + oluşturma akışını istek üzerine açılan panele almak;
// birbirini dışlayan üç sekme (Bekleyen + Ödenen = Tümü); dönem ve vadeyi sütundan çıkarıp
// başlıkta bir kez yazmak.

type DueFilter = "open" | "paid" | "all";

export interface BillingFilterSummary {
  outstanding: number;
  collected: number;
  overdue: number;
  openCount: number;
  overdueCount: number;
}

const ALL_PERIODS = "all";

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

function formatMoney(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
}

function formatPeriod(period: string) {
  return new Date(`${period}-01T00:00:00`).toLocaleDateString("tr-TR", { month: "long", year: "numeric" });
}

function formatDay(isoDate: string) {
  return new Date(`${isoDate}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long" });
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
  const { data: instruments } = useInstruments();
  const [filter, setFilter] = useState<DueFilter>("open");
  const [search, setSearch] = useState("");
  const [period, setPeriod] = useState<string | null>(null);
  // selectedStudentId artık yalnızca "tam hesabı aç" kaçış kapısı için tutuluyor - hızlı
  // tahsilat panelinin (QuickCollectPanel) kendi öğrenci seçimi ayrıdır.
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null);
  const [showCreatePanel, setShowCreatePanel] = useState(false);
  const [teacherFilter, setTeacherFilter] = useState("all");
  const [instrumentFilter, setInstrumentFilter] = useState("all");

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
    // Panel açıldıktan sonra odaklan - aksi halde eleman henüz DOM'da olmuyor.
    window.setTimeout(() => {
      const select = studentPickerRef.current;
      if (!select) return;
      select.scrollIntoView({ behavior: "smooth", block: "center" });
      select.focus();
    }, 0);
  }, []);

  const clearFilters = useCallback(() => {
    setFilter("all");
    setSearch("");
    setTeacherFilter("all");
    setInstrumentFilter("all");
    setPeriod(ALL_PERIODS);
  }, []);

  // Dönem DIŞINDAKİ daraltmalar ayrı tutulur: aşağıdaki "başka dönemde gecikmiş var"
  // uyarısı bu kümeye bakar, çünkü tam da dönem filtresinin gizlediği şeyi göstermesi gerekir.
  const duesMatchingFilters = useMemo(() => (dues ?? []).filter((due) => {
    const query = search.trim().toLocaleLowerCase("tr-TR");
    const matchesSearch = !query || `${due.studentName} ${due.teacherName} ${due.instrumentName}`.toLocaleLowerCase("tr-TR").includes(query);
    const matchesTeacher = teacherFilter === "all" || due.teacherId === teacherFilter;
    const matchesInstrument = instrumentFilter === "all" || due.instrumentId === instrumentFilter;
    return matchesSearch && matchesTeacher && matchesInstrument;
  }), [dues, instrumentFilter, search, teacherFilter]);

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

  const hasActiveFilters = Boolean(search.trim()) || teacherFilter !== "all" || instrumentFilter !== "all" || filter !== "all";

  return <div className="space-y-4">
    {showCreatePanel && <section className="app-card overflow-visible p-4 sm:p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-micro text-[var(--brand-strong)]">Aidat al</p>
          <h2 className="mt-1 text-title">Öğrenci seç, ödemeyi kaydet</h2>
          <p className="text-meta mt-1">Elden alındıysa veya havale geldiyse tek dokunuşla işaretle.</p>
        </div>
        <button type="button" onClick={() => setShowCreatePanel(false)} className="pressable min-h-9 rounded-lg border border-[var(--line)] px-3 text-xs font-bold text-[var(--muted)]">Kapat</button>
      </div>
      <QuickCollectPanel
        pickerRef={studentPickerRef}
        onOpenFullAccount={(studentId) => setSelectedStudentId(studentId)}
        onCollected={() => setShowCreatePanel(false)}
      />
    </section>}

    {selectedStudentId && <StudentBillingSection key={selectedStudentId} initialStudentId={selectedStudentId} showStudentPicker={false} onClose={() => setSelectedStudentId(null)} />}

    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] p-4 sm:p-5">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <p className="text-micro text-[var(--brand-strong)]">Aidat listesi</p>
            <h2 className="mt-1 text-title">Öğrenci aidatları</h2>
            <p className="text-meta mt-1">Borcu gör, ödemeyi kaydet veya öğrenci hesabını aç.</p>
          </div>
          <button type="button" onClick={startAddingDue} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-xs font-bold text-white">+ Aidat al</button>
        </div>

        {/* Tek arama kutusu + iki daraltma. Önceki sürümdeki ikinci arama kutusu ve
            "yeni aidat" kartına gizlenmiş filtreler buraya toplandı. */}
        <div className="mt-4 flex flex-wrap items-center gap-2">
          <label className="relative min-w-[14rem] flex-1"><span className="sr-only">Öğrenci, öğretmen veya enstrüman ara</span><Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--muted)]" /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Öğrenci, öğretmen veya enstrüman ara" className="field min-h-11 pl-9 text-sm" /></label>
          <label className="min-w-[10rem]"><span className="sr-only">Öğretmene göre filtrele</span><select value={teacherFilter} onChange={(event) => setTeacherFilter(event.target.value)} className="field min-h-11 text-xs font-semibold"><option value="all">Tüm öğretmenler</option>{teachers?.filter((teacher) => teacher.status === "Active").map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>)}</select></label>
          <label className="min-w-[10rem]"><span className="sr-only">Enstrümana göre filtrele</span><select value={instrumentFilter} onChange={(event) => setInstrumentFilter(event.target.value)} className="field min-h-11 text-xs font-semibold"><option value="all">Tüm enstrümanlar</option>{instruments?.map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}</select></label>
        </div>
      </div>

      {/* Dönem başlığı: sütunlardan kaldırılan Dönem ve Vade bilgisini bir kez taşır. */}
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-b border-[var(--line)] bg-[var(--surface-muted)] px-4 py-3">
        <label className="flex items-center gap-2">
          <span className="sr-only">Döneme göre filtrele</span>
          <select value={activePeriod} onChange={(event) => setPeriod(event.target.value)} className="min-h-9 rounded-lg border border-[var(--line)] bg-white px-2.5 font-serif text-sm font-semibold capitalize">
            <option value={ALL_PERIODS}>Tüm dönemler</option>
            {periods.map((item) => <option key={item} value={item} className="capitalize">{formatPeriod(item)}</option>)}
          </select>
        </label>
        <p className="text-[.68rem] font-semibold tabular-nums text-[var(--muted)]">
          {sharedDueDate && <>Vade {formatDay(sharedDueDate)} · </>}
          {counts.all} aidat
          {filterSummary.overdue > 0 && <> · <span className="font-bold text-[var(--danger-strong)]">{formatMoney(filterSummary.overdue, "TRY")} gecikmiş</span></>}
          {filterSummary.outstanding > 0 && <> · {formatMoney(filterSummary.outstanding, "TRY")} açık</>}
        </p>
      </div>

      {overdueOutsideScope && <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-[var(--danger)]/25 bg-[var(--danger-soft)] px-4 py-2.5">
        <Icon name="bell" className="h-4 w-4 shrink-0 text-[var(--danger-strong)]" />
        <p className="text-xs font-bold tabular-nums text-[var(--danger-strong)]">
          Başka dönemlerde {overdueOutsideScope.count} gecikmiş aidat var · {formatMoney(overdueOutsideScope.amount, "TRY")}
        </p>
        <button type="button" onClick={() => { setPeriod(ALL_PERIODS); setFilter("open"); }} className="pressable ml-auto min-h-9 rounded-lg bg-[var(--danger-strong)] px-3 text-[.66rem] font-bold text-white">Hepsini göster</button>
      </div>}

      <div className="px-4 pt-3">
        <div className="inline-flex gap-1 rounded-xl bg-[var(--surface-muted)] p-1" aria-label="Aidat durum filtresi">{FILTERS.map((item) => <button key={item.value} type="button" onClick={() => setFilter(item.value)} aria-pressed={filter === item.value} className={`pressable flex min-h-9 shrink-0 items-center gap-2 rounded-lg px-3 text-xs font-bold ${filter === item.value ? "bg-white text-[var(--brand-strong)] shadow-sm" : "text-[var(--muted)] hover:text-[var(--foreground)]"}`}>{item.label}<span className="rounded-full bg-[var(--surface-muted)] px-1.5 py-0.5 text-[.58rem] tabular-nums">{counts[item.value]}</span></button>)}</div>
      </div>

      {/* Beş sütun: Dönem ve Vade artık yukarıdaki dönem başlığında. */}
      <div className="mt-3 hidden grid-cols-[minmax(12rem,1.4fr)_minmax(9rem,.9fr)_minmax(9rem,.9fr)_minmax(8rem,.8fr)_auto] gap-3 border-b border-t border-[var(--line)] bg-[var(--surface-muted)]/55 px-4 py-2.5 text-[.62rem] font-bold uppercase tracking-[.08em] text-[var(--muted)] md:grid"><span>Öğrenci / enstrüman</span><span>Öğretmen</span><span>Tutar / kalan</span><span>Durum</span><span className="text-right">İşlem</span></div>
      {isLoading && <div className="space-y-2 p-4">{[1, 2, 3, 4].map((item) => <div key={item} className="skeleton h-16 rounded-xl" />)}</div>}
      {!isLoading && isError && <div className="grid min-h-52 place-items-center p-8 text-center"><div><span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--danger-soft)] text-[var(--danger-strong)]"><Icon name="x" className="h-5 w-5" /></span><p className="mt-3 text-sm font-bold">Aidat listesi yüklenemedi</p><p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p><button type="button" onClick={() => void refetch()} disabled={isFetching} className="pressable mt-3 min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--foreground)] disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button></div></div>}
      {!isLoading && !isError && visibleDues.length > 0 && <div className="divide-y divide-[var(--line)]">{visibleDues.map((due) => <DueRow key={due.id} due={due} />)}</div>}
      {!isLoading && !isError && !visibleDues.length && <div className="grid min-h-52 place-items-center p-8 text-center"><div>
        <span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="wallet" className="h-5 w-5" /></span>
        {!dues?.length
          ? <>
              <p className="mt-3 text-sm font-bold">Henüz aidat kaydı yok</p>
              <p className="text-meta mt-1">Bir öğrenci seçip ücret planına göre dönem aidatını oluşturarak başla. Aidat oluşturulduğunda tahsilat, kısmi ödeme ve gecikme takibi buradan yürür.</p>
              <button type="button" onClick={startAddingDue} className="pressable mt-3 min-h-9 rounded-lg bg-[var(--brand)] px-4 text-xs font-bold text-white">Aidat al</button>
            </>
          : <>
              <p className="mt-3 text-sm font-bold">Bu dönemde aidat yok</p>
              <p className="text-meta mt-1">{hasActiveFilters ? "Seçili filtrelerle eşleşen aidat bulunamadı." : "Bu dönem için henüz aidat oluşturulmamış."} Başka bir dönem seçebilir veya yeni bir dönem aidatı ekleyebilirsin.</p>
              <div className="mt-3 flex flex-wrap items-center justify-center gap-2">
                <button type="button" onClick={clearFilters} className="pressable min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-xs font-bold">Filtreleri temizle</button>
                <button type="button" onClick={startAddingDue} className="pressable min-h-9 rounded-lg bg-[var(--brand)] px-4 text-xs font-bold text-white">Aidat al</button>
              </div>
            </>}
      </div></div>}
    </section>

  </div>;
}

// "Öğrenci aidat verecek" akışının tamamı: öğrenciyi bir LİSTEDEN seç (serbest metin arama
// değil), ardından "Elden alındı" / "Havale geldi" bas - bitsin. Önceki sürüm burada bir
// arama kutusu açıp seçilen öğrenci için koca bir hesap yönetim panelini (dönem/kurs/ücret
// planı seçimi) açıyordu; günlük en sık işlem olan "bu ayın aidatı şimdi ödendi" için çok
// adımlıydı. Bu bileşen o adımları TEK ekranda, iki büyük butona indirger:
//   - Bu ay için aidat kaydı zaten varsa (Unpaid/Partial/Overdue), kalan tutarı doğrudan öder.
//   - Yoksa (ilk kez veya önceki ay tam ödenmiş), önce dönemi backend'in zorunlu tuttuğu
//     ücret planından oluşturur, sonra AYNI işlemde tam ödemeyi kaydeder.
// Farklı bir dönem eklemek/ücret planını değiştirmek gibi seyrek işler için altta "tam
// hesabı aç" bağlantısı bırakılır - bu yüzden StudentBillingSection silinmedi.
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
  const [studentId, setStudentId] = useState("");
  const [rawEnrollmentId, setRawEnrollmentId] = useState("");
  const { data: enrollments, isLoading: enrollmentsLoading } = useEnrollments(studentId);
  const { data: billing } = useStudentBilling(studentId, { enabled: !!studentId });
  const { data: priceLists } = usePriceLists();
  const createReceivable = useCreateReceivable(studentId);
  const recordPayment = useRecordPayment(studentId);
  const [pendingMethod, setPendingMethod] = useState<PaymentMethod | null>(null);
  const [error, setError] = useState<string | null>(null);

  const activeEnrollments = useMemo(() => enrollments?.filter((enrollment) => enrollment.status === "Active") ?? [], [enrollments]);

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
  const feePlanLoading = enrollmentsLoading || feePlanQueries.some((query) => query.isLoading);
  const feePlanByEnrollment = new Map(activeEnrollments.map((enrollment, index) => [enrollment.id, feePlanQueries[index]?.data ?? null]));
  const enrollmentsWithPlan = activeEnrollments.filter((enrollment) => feePlanByEnrollment.get(enrollment.id));
  const enrollmentsWithoutPlan = feePlanLoading ? [] : activeEnrollments.filter((enrollment) => !feePlanByEnrollment.get(enrollment.id));

  // Öğrenci değişince önceki öğrencinin seçimi otomatik geçersiz kalır - effect'e gerek yok.
  const enrollmentId = enrollmentsWithPlan.some((enrollment) => enrollment.id === rawEnrollmentId)
    ? rawEnrollmentId
    : (enrollmentsWithPlan.length === 1 ? enrollmentsWithPlan[0].id : "");
  const feePlan = enrollmentId ? feePlanByEnrollment.get(enrollmentId) : null;

  const currentPeriod = new Date().toISOString().slice(0, 7);
  const currentReceivable = billing
    ?.find((row) => row.enrollmentId === enrollmentId)
    ?.receivables.find((receivable) => receivable.period === currentPeriod && receivable.status !== "Cancelled");
  const alreadyPaid = currentReceivable?.status === "Paid";
  const dueAmount = currentReceivable ? Math.max(0, currentReceivable.amount - currentReceivable.totalPaid) : feePlan?.amount ?? 0;

  function enrollmentLabel(enrollment: { instrumentId: string; teacherId: string }, enrollmentIdValue: string) {
    // Kurs adları burada yalnızca ayrıştırmak için gerekiyor; instrument/teacher isimleri
    // ayrı uçlardan geliyor ama bu panel öğretmen listesini zaten çekmiyor - okulun bu
    // ölçeğinde bir öğrencinin genelde tek aktif kaydı olduğu için (çoklu kayıt nadir),
    // ad yerine sıra numarası yeterince ayırt edici ve ek bir istek gerektirmiyor.
    return `Kurs ${activeEnrollments.findIndex((item) => item.id === enrollmentIdValue) + 1}`;
  }

  async function collect(method: PaymentMethod) {
    if (!enrollmentId) return;
    setError(null);
    setPendingMethod(method);
    try {
      let receivableId = currentReceivable?.id;
      let amount = dueAmount;
      if (!receivableId) {
        const created = await createReceivable.mutateAsync({ enrollmentId, period: currentPeriod });
        receivableId = created.id;
        amount = Math.max(0, created.amount - created.totalPaid);
      }
      await recordPayment.mutateAsync({ receivableId, amount, paymentDate: new Date().toISOString().slice(0, 10), method });
      onCollected();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ödeme kaydedilemedi.");
    } finally {
      setPendingMethod(null);
    }
  }

  return <div className="mt-4 max-w-xl space-y-3">
    <label className="block space-y-1.5 text-[.68rem] font-bold text-[var(--muted)]">
      <span>Öğrenci</span>
      <select ref={pickerRef} value={studentId} onChange={(event) => { setStudentId(event.target.value); setRawEnrollmentId(""); setError(null); }} className="field min-h-11 text-sm">
        <option value="">Öğrenci seç…</option>
        {students?.filter((student) => student.status === "Active").map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}
      </select>
    </label>

    {studentId && feePlanLoading && <div className="skeleton h-24 rounded-xl" />}

    {studentId && !feePlanLoading && activeEnrollments.length === 0 && (
      <p className="rounded-xl bg-[var(--surface-muted)] p-3 text-xs text-[var(--muted)]">Bu öğrencinin aktif kaydı bulunmuyor.</p>
    )}

    {studentId && !feePlanLoading && enrollmentsWithoutPlan.length > 0 && (
      <div className="space-y-2">
        {enrollmentsWithoutPlan.map((enrollment) => (
          <MissingFeePlanInline
            key={enrollment.id}
            enrollmentId={enrollment.id}
            label={activeEnrollments.length > 1 ? enrollmentLabel(enrollment, enrollment.id) : "Bu kurs"}
            priceListItems={(priceLists ?? []).flatMap((list) => list.items).filter((item) => item.instrumentId === enrollment.instrumentId)}
          />
        ))}
      </div>
    )}

    {studentId && !feePlanLoading && enrollmentsWithPlan.length > 1 && !enrollmentId && (
      <label className="block space-y-1.5 text-[.68rem] font-bold text-[var(--muted)]">
        <span>Hangi kurs?</span>
        <select value={rawEnrollmentId} onChange={(event) => setRawEnrollmentId(event.target.value)} className="field min-h-11 text-sm">
          <option value="">Kurs seç…</option>
          {enrollmentsWithPlan.map((enrollment) => <option key={enrollment.id} value={enrollment.id}>{enrollmentLabel(enrollment, enrollment.id)}</option>)}
        </select>
      </label>
    )}

    {enrollmentId && feePlan && (
      alreadyPaid ? (
        <p className="rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-xs font-bold text-[var(--success-strong)]">✓ {formatPeriod(currentPeriod)} aidatı zaten ödendi.</p>
      ) : (
        <div className="rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/60 p-3.5">
          <p className="text-xs font-bold">{formatPeriod(currentPeriod)} aidatı{currentReceivable?.status === "Partial" ? " · kalan" : ""}</p>
          <p className="mt-0.5 text-lg font-bold tabular-nums text-[var(--brand-strong)]">{formatMoney(dueAmount, feePlan.currency)}</p>
          <div className="mt-3 flex flex-wrap gap-2">
            <button type="button" onClick={() => void collect("Cash")} disabled={pendingMethod !== null} className="pressable min-h-11 flex-1 rounded-xl bg-[var(--success-strong)] px-4 text-sm font-bold text-white disabled:opacity-50">{pendingMethod === "Cash" ? "Kaydediliyor…" : "Elden alındı"}</button>
            <button type="button" onClick={() => void collect("Transfer")} disabled={pendingMethod !== null} className="pressable min-h-11 flex-1 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white disabled:opacity-50">{pendingMethod === "Transfer" ? "Kaydediliyor…" : "Havale geldi"}</button>
          </div>
          {error && <p role="alert" className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
        </div>
      )
    )}

    {studentId && <button type="button" onClick={() => onOpenFullAccount(studentId)} className="text-[.68rem] font-bold text-[var(--brand-strong)] underline underline-offset-2">Farklı dönem eklemek veya ücret planını değiştirmek için tam hesabı aç →</button>}
  </div>;
}

function MissingFeePlanInline({ enrollmentId, label, priceListItems }: { enrollmentId: string; label: string; priceListItems: { id: string; durationMinutes: number; billingType: string; amount: number; currency: string }[] }) {
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
    <div className="rounded-xl border border-[var(--warning-soft)] bg-[var(--warning-soft)]/40 p-3">
      <p className="text-xs font-bold">{label}: ücret planı yok</p>
      <p className="text-meta mt-0.5">Aidat alınabilmesi için önce bir ücret planı gerekiyor.</p>
      {priceListItems.length === 0
        ? <p className="text-meta mt-2 font-semibold text-[var(--danger-strong)]">Bu enstrüman için fiyat listesi kalemi yok - önce Fiyat politikası ekranından ekle.</p>
        : <form onSubmit={handleSubmit} className="mt-2 flex flex-wrap items-end gap-2">
            <select value={itemId} onChange={(e) => setItemId(e.target.value)} required className="field min-h-10 w-auto text-sm">
              <option value="">Fiyat kalemi seç</option>
              {priceListItems.map((item) => <option key={item.id} value={item.id}>{item.durationMinutes} dk · {item.billingType === "Monthly" ? "Aylık" : "Paket"} · {item.amount.toLocaleString("tr-TR")} {item.currency}</option>)}
            </select>
            <input type="number" min={1} max={28} value={dueDay} onChange={(e) => setDueDay(Number(e.target.value))} className="field min-h-10 w-20 text-sm" title="Vade günü" />
            <button type="submit" disabled={createFeePlan.isPending || !itemId} className="pressable min-h-10 rounded-lg bg-[var(--brand)] px-3 text-sm font-bold text-white disabled:opacity-50">{createFeePlan.isPending ? "Oluşturuluyor…" : "Ücret planı oluştur"}</button>
          </form>}
      {error && <p className="mt-2 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
    </div>
  );
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

  return <article className="px-4 py-3.5">
    <div className="grid items-center gap-3 md:grid-cols-[minmax(12rem,1.4fr)_minmax(9rem,.9fr)_minmax(9rem,.9fr)_minmax(8rem,.8fr)_auto]">
      <div className="flex min-w-0 items-center gap-3"><span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[.66rem] font-bold text-[var(--brand-strong)]">{due.studentName.split(" ").map((part) => part[0]).slice(0, 2).join("")}</span><span className="min-w-0"><strong className="block truncate text-sm">{due.studentName}</strong><span className="text-meta mt-0.5 block truncate">{due.instrumentName}</span></span></div>
      <div className="text-xs"><span className="text-[.62rem] font-bold text-[var(--muted)] md:hidden">Öğretmen · </span>{due.teacherName}</div>
      <div><strong className="block text-xs tabular-nums">{formatMoney(due.amount, due.currency)}</strong><span className={`mt-0.5 block text-[.62rem] tabular-nums ${remaining ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>{remaining ? `${formatMoney(remaining, due.currency)} kaldı` : "Tamamı ödendi"}</span></div>
      <div><span className={`inline-flex rounded-full px-2 py-1 text-[.6rem] font-bold ${STATUS_TONES[due.status]}`}>{lateDays > 0 ? `${lateDays} gün gecikti` : STATUS_LABELS[due.status]}</span></div>
      <div className="flex justify-end gap-1.5">
        {canCollect && <button type="button" onClick={() => setShowPayment((visible) => !visible)} className="pressable min-h-9 rounded-lg bg-[var(--brand)] px-3 text-[.66rem] font-bold text-white">Tahsilat</button>}
        {/* "Hesap" yerine "Geçmiş": ayrı bir üst panel açmak yerine satırın hemen altında
            katlanır (collapse) bir bölüm olarak, yalnızca bu öğrencinin eski dönem
            ödemelerini gösterir - kullanıcı isteği üzerine sadeleştirildi. */}
        <button type="button" onClick={() => setShowHistory((visible) => !visible)} aria-expanded={showHistory} className="pressable inline-flex min-h-9 items-center gap-1 rounded-lg border border-[var(--line)] bg-white px-3 text-[.66rem] font-bold text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]">Geçmiş<Icon name="chevron" className={`h-3 w-3 shrink-0 transition-transform ${showHistory ? "rotate-90" : ""}`} /></button>
      </div>
    </div>
    {showPayment && <form onSubmit={collect} className="mt-3 grid gap-2 rounded-xl border border-[var(--brand)]/25 bg-[var(--brand-soft)]/45 p-3 sm:grid-cols-[1fr_1fr_1fr_auto] sm:items-end"><label className="space-y-1 text-[.64rem] font-bold text-[var(--muted)]">Tutar<input type="number" min={0.01} max={remaining} step={0.01} value={amount} onChange={(event) => setAmount(Number(event.target.value))} required className="field min-h-10 bg-white text-xs" /></label><label className="space-y-1 text-[.64rem] font-bold text-[var(--muted)]">Tarih<input type="date" value={paymentDate} onChange={(event) => setPaymentDate(event.target.value)} required className="field min-h-10 bg-white text-xs" /></label><label className="space-y-1 text-[.64rem] font-bold text-[var(--muted)]">Yöntem<select value={method} onChange={(event) => setMethod(event.target.value as PaymentMethod)} className="field min-h-10 bg-white text-xs"><option value="Cash">Nakit</option><option value="Transfer">Havale</option><option value="Card">Kart</option><option value="Other">Diğer</option></select></label><button type="submit" disabled={recordPayment.isPending} className="pressable min-h-10 rounded-xl bg-[var(--brand)] px-4 text-xs font-bold text-white disabled:opacity-50">{recordPayment.isPending ? "Kaydediliyor…" : "Ödemeyi kaydet"}</button>{error && <p role="alert" className="text-xs font-semibold text-[var(--danger-strong)] sm:col-span-4">{error}</p>}</form>}
    {showHistory && <PaymentHistoryCollapse studentId={due.studentId} />}
  </article>;
}

const HISTORY_PAGE_SIZE = 5;

// Ana listedeki bir satırın altında açılır; o öğrencinin TÜM dönemlerindeki (bu satırın
// dönemiyle sınırlı değil) geçmiş ödemelerini, en yeniden en eskiye, sayfalı gösterir.
// Yalnızca collapse açıldığında veri çeker (useStudentBilling enabled: showHistory zaten
// DueRow'da true geldiği için burada) - ekrandaki her satır için görünmeyen bir istek
// atılmasın diye.
function PaymentHistoryCollapse({ studentId }: { studentId: string }) {
  const { data: billing, isLoading, isError } = useStudentBilling(studentId);
  const [page, setPage] = useState(0);

  const entries = (billing ?? [])
    .flatMap((row) => row.receivables.flatMap((receivable) =>
      receivable.payments.map((payment) => ({ payment, period: receivable.period, currency: receivable.currency }))))
    .sort((a, b) => b.payment.paymentDate.localeCompare(a.payment.paymentDate) || (b.payment.recordedAt ?? "").localeCompare(a.payment.recordedAt ?? ""));

  const pageCount = Math.max(1, Math.ceil(entries.length / HISTORY_PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);
  const visibleEntries = entries.slice(currentPage * HISTORY_PAGE_SIZE, currentPage * HISTORY_PAGE_SIZE + HISTORY_PAGE_SIZE);

  return <div className="mt-3 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/60 p-3">
    <p className="text-micro text-[var(--brand-strong)]">Geçmiş ödemeler</p>
    {isLoading && <div className="mt-2 space-y-1.5">{[1, 2].map((item) => <div key={item} className="skeleton h-9 rounded-lg" />)}</div>}
    {!isLoading && isError && <p className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">Ödeme geçmişi yüklenemedi.</p>}
    {!isLoading && !isError && !entries.length && <p className="text-meta mt-2">Bu öğrenci için kayıtlı bir ödeme yok.</p>}
    {!isLoading && !isError && entries.length > 0 && <>
      <div className="mt-2 space-y-1.5">
        {visibleEntries.map(({ payment, period, currency }) => (
          <div key={payment.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-white px-3 py-2 text-xs">
            <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5">
              <span className="font-bold capitalize">{formatPeriod(period)}</span>
              <span className="text-[var(--muted)]">· {payment.paymentDate} · {payment.method === "Transfer" ? "Havale" : payment.method === "Cash" ? "Nakit" : payment.method === "Card" ? "Kart" : "Diğer"}</span>
              {payment.kind === "Correction" && <span className="rounded-full bg-[var(--warning-soft)] px-1.5 py-0.5 text-[.6rem] font-bold text-[var(--warning-strong)]">Düzeltme</span>}
            </span>
            <strong className="tabular-nums">
              {payment.kind === "Correction" && payment.previousAmount != null
                ? `${payment.previousAmount.toLocaleString("tr-TR")} → ${payment.amount.toLocaleString("tr-TR")} ${currency}`
                : `${payment.amount.toLocaleString("tr-TR")} ${currency}`}
            </strong>
          </div>
        ))}
      </div>
      {pageCount > 1 && <div className="mt-2.5 flex items-center justify-between gap-2">
        <button type="button" onClick={() => setPage((value) => Math.max(0, value - 1))} disabled={currentPage === 0} className="pressable min-h-8 rounded-lg border border-[var(--line)] bg-white px-2.5 text-[.62rem] font-bold text-[var(--muted)] disabled:opacity-40">‹ Önceki</button>
        <span className="text-[.62rem] font-semibold tabular-nums text-[var(--muted)]">Sayfa {currentPage + 1} / {pageCount}</span>
        <button type="button" onClick={() => setPage((value) => Math.min(pageCount - 1, value + 1))} disabled={currentPage >= pageCount - 1} className="pressable min-h-8 rounded-lg border border-[var(--line)] bg-white px-2.5 text-[.62rem] font-bold text-[var(--muted)] disabled:opacity-40">Sonraki ›</button>
      </div>}
    </>}
  </div>;
}
