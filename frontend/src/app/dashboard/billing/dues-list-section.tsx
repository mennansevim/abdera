"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useBillingDues, useRecordPayment, type BillingDue, type PaymentMethod } from "@/lib/billing";
import { useInstruments, useStudentAutocomplete, useTeachers } from "@/lib/people";
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
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null);
  const [studentSearch, setStudentSearch] = useState("");
  const [debouncedStudentSearch, setDebouncedStudentSearch] = useState("");
  const [showStudentSuggestions, setShowStudentSuggestions] = useState(false);
  const [showCreatePanel, setShowCreatePanel] = useState(false);
  const [teacherFilter, setTeacherFilter] = useState("all");
  const [instrumentFilter, setInstrumentFilter] = useState("all");
  const { data: studentSearchResults, isFetching: studentSearchLoading } = useStudentAutocomplete(debouncedStudentSearch);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedStudentSearch(studentSearch.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [studentSearch]);

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

  const studentSearchRef = useRef<HTMLInputElement>(null);
  const startAddingDue = useCallback(() => {
    setShowCreatePanel(true);
    // Panel açıldıktan sonra odaklan - aksi halde input henüz DOM'da olmuyor.
    window.setTimeout(() => {
      const input = studentSearchRef.current;
      if (!input) return;
      input.scrollIntoView({ behavior: "smooth", block: "center" });
      input.focus();
      setShowStudentSuggestions(true);
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
          <p className="text-micro text-[var(--brand-strong)]">Yeni aidat</p>
          <h2 className="mt-1 text-title">Öğrenci hesabından ekle</h2>
          <p className="text-meta mt-1">Öğrenciyi ara; öğretmen ve enstrüman kaydına göre ücret planını aç.</p>
        </div>
        <button type="button" onClick={() => { setShowCreatePanel(false); setSelectedStudentId(null); setStudentSearch(""); }} className="pressable min-h-9 rounded-lg border border-[var(--line)] px-3 text-xs font-bold text-[var(--muted)]">Kapat</button>
      </div>
      <div className="relative mt-4 max-w-lg">
        <label className="space-y-1.5 text-[.68rem] font-bold text-[var(--muted)]"><span>Öğrenci</span><div className="relative"><Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--brand)]" /><input role="combobox" aria-expanded={showStudentSuggestions} aria-controls="billing-student-suggestions" ref={studentSearchRef} value={studentSearch} onFocus={() => setShowStudentSuggestions(true)} onChange={(event) => { setStudentSearch(event.target.value); setSelectedStudentId(null); setShowStudentSuggestions(true); }} placeholder="Öğrenci adı yazın…" className="field min-h-11 pl-9 text-sm" /></div></label>
        {showStudentSuggestions && studentSearch.trim().length >= 2 && <ul id="billing-student-suggestions" role="listbox" className="absolute inset-x-0 top-[4.3rem] z-30 overflow-hidden rounded-xl border border-[var(--line)] bg-white p-1.5 shadow-[0_14px_30px_rgba(80,48,24,.16)]">{studentSearchLoading && <li className="px-3 py-2 text-xs text-[var(--muted)]">Öğrenciler aranıyor…</li>}{!studentSearchLoading && !studentSearchResults?.length && <li className="px-3 py-2 text-xs text-[var(--muted)]">Eşleşen öğrenci bulunamadı.</li>}{studentSearchResults?.map((student) => <li key={`${student.studentId}-${student.teacherId}-${student.instrumentId}`} role="option" aria-selected={selectedStudentId === student.studentId}><button type="button" onMouseDown={(event) => event.preventDefault()} onClick={() => { setStudentSearch(student.studentName); setSelectedStudentId(student.studentId); setShowStudentSuggestions(false); }} className="pressable flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-[var(--surface-muted)]"><span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-[var(--brand-soft)] text-[.62rem] font-bold text-[var(--brand-strong)]">{student.studentName.split(" ").slice(0, 2).map((part) => part[0]).join("")}</span><span className="min-w-0"><span className="block truncate text-xs font-bold">{student.studentName}</span><span className="block truncate text-[.62rem] text-[var(--muted)]">{student.teacherName} · {student.instrumentName}{student.guardianPhoneMasked ? ` · ${student.guardianPhoneMasked}` : ""}</span></span></button></li>)}</ul>}
      </div>
    </section>}

    {selectedStudentId && <StudentBillingSection key={selectedStudentId} initialStudentId={selectedStudentId} showStudentPicker={false} onClose={() => { setSelectedStudentId(null); setStudentSearch(""); }} />}

    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] p-4 sm:p-5">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <p className="text-micro text-[var(--brand-strong)]">Aidat listesi</p>
            <h2 className="mt-1 text-title">Öğrenci aidatları</h2>
            <p className="text-meta mt-1">Borcu gör, ödemeyi kaydet veya öğrenci hesabını aç.</p>
          </div>
          <button type="button" onClick={startAddingDue} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-xs font-bold text-white">+ Dönem aidatı ekle</button>
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
      {!isLoading && !isError && visibleDues.length > 0 && <div className="divide-y divide-[var(--line)]">{visibleDues.map((due) => <DueRow key={due.id} due={due} onOpenAccount={() => { setShowCreatePanel(true); setSelectedStudentId(due.studentId); setStudentSearch(due.studentName); }} />)}</div>}
      {!isLoading && !isError && !visibleDues.length && <div className="grid min-h-52 place-items-center p-8 text-center"><div>
        <span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="wallet" className="h-5 w-5" /></span>
        {!dues?.length
          ? <>
              <p className="mt-3 text-sm font-bold">Henüz aidat kaydı yok</p>
              <p className="text-meta mt-1">Bir öğrenci seçip ücret planına göre dönem aidatını oluşturarak başla. Aidat oluşturulduğunda tahsilat, kısmi ödeme ve gecikme takibi buradan yürür.</p>
              <button type="button" onClick={startAddingDue} className="pressable mt-3 min-h-9 rounded-lg bg-[var(--brand)] px-4 text-xs font-bold text-white">Dönem aidatı ekle</button>
            </>
          : <>
              <p className="mt-3 text-sm font-bold">Bu dönemde aidat yok</p>
              <p className="text-meta mt-1">{hasActiveFilters ? "Seçili filtrelerle eşleşen aidat bulunamadı." : "Bu dönem için henüz aidat oluşturulmamış."} Başka bir dönem seçebilir veya yeni bir dönem aidatı ekleyebilirsin.</p>
              <div className="mt-3 flex flex-wrap items-center justify-center gap-2">
                <button type="button" onClick={clearFilters} className="pressable min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-xs font-bold">Filtreleri temizle</button>
                <button type="button" onClick={startAddingDue} className="pressable min-h-9 rounded-lg bg-[var(--brand)] px-4 text-xs font-bold text-white">Dönem aidatı ekle</button>
              </div>
            </>}
      </div></div>}
    </section>

  </div>;
}

function DueRow({ due, onOpenAccount }: { due: BillingDue; onOpenAccount: () => void }) {
  const recordPayment = useRecordPayment(due.studentId);
  const [showPayment, setShowPayment] = useState(false);
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
      <div className="flex justify-end gap-1.5">{canCollect && <button type="button" onClick={() => setShowPayment((visible) => !visible)} className="pressable min-h-9 rounded-lg bg-[var(--brand)] px-3 text-[.66rem] font-bold text-white">Tahsilat</button>}<button type="button" onClick={onOpenAccount} className="pressable min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-[.66rem] font-bold text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]">Hesap</button></div>
    </div>
    {showPayment && <form onSubmit={collect} className="mt-3 grid gap-2 rounded-xl border border-[var(--brand)]/25 bg-[var(--brand-soft)]/45 p-3 sm:grid-cols-[1fr_1fr_1fr_auto] sm:items-end"><label className="space-y-1 text-[.64rem] font-bold text-[var(--muted)]">Tutar<input type="number" min={0.01} max={remaining} step={0.01} value={amount} onChange={(event) => setAmount(Number(event.target.value))} required className="field min-h-10 bg-white text-xs" /></label><label className="space-y-1 text-[.64rem] font-bold text-[var(--muted)]">Tarih<input type="date" value={paymentDate} onChange={(event) => setPaymentDate(event.target.value)} required className="field min-h-10 bg-white text-xs" /></label><label className="space-y-1 text-[.64rem] font-bold text-[var(--muted)]">Yöntem<select value={method} onChange={(event) => setMethod(event.target.value as PaymentMethod)} className="field min-h-10 bg-white text-xs"><option value="Cash">Nakit</option><option value="Transfer">Havale</option><option value="Card">Kart</option><option value="Other">Diğer</option></select></label><button type="submit" disabled={recordPayment.isPending} className="pressable min-h-10 rounded-xl bg-[var(--brand)] px-4 text-xs font-bold text-white disabled:opacity-50">{recordPayment.isPending ? "Kaydediliyor…" : "Ödemeyi kaydet"}</button>{error && <p role="alert" className="text-xs font-semibold text-[var(--danger-strong)] sm:col-span-4">{error}</p>}</form>}
  </article>;
}
