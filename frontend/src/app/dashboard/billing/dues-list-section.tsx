"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useBillingDues, useRecordPayment, type BillingDue, type PaymentMethod } from "@/lib/billing";
import { useInstruments, useStudentAutocomplete, useTeachers } from "@/lib/people";
import { StudentBillingSection } from "./student-billing-section";

type DueFilter = "open" | "overdue" | "partial" | "paid" | "all";

export interface BillingFilterSummary {
  outstanding: number;
  collected: number;
  overdue: number;
  openCount: number;
  overdueCount: number;
}

const FILTERS: Array<{ value: DueFilter; label: string }> = [
  { value: "open", label: "Açık aidatlar" },
  { value: "overdue", label: "Vadesi geçen" },
  { value: "partial", label: "Kısmi" },
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
  Unpaid: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  Partial: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  Paid: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  Overdue: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  Cancelled: "bg-[var(--surface-muted)] text-[var(--muted)]",
};

function formatMoney(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
}

function formatPeriod(period: string) {
  return new Date(`${period}-01T00:00:00`).toLocaleDateString("tr-TR", { month: "long", year: "numeric" });
}

export function DuesListSection({ onSummaryChange }: { onSummaryChange?: (summary: BillingFilterSummary) => void }) {
  const { data: dues, isLoading, isError, isFetching, refetch } = useBillingDues();
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();
  const [filter, setFilter] = useState<DueFilter>("open");
  const [search, setSearch] = useState("");
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null);
  const [studentSearch, setStudentSearch] = useState("");
  const [debouncedStudentSearch, setDebouncedStudentSearch] = useState("");
  const [showStudentSuggestions, setShowStudentSuggestions] = useState(false);
  const [teacherFilter, setTeacherFilter] = useState("all");
  const [instrumentFilter, setInstrumentFilter] = useState("all");
  const { data: studentSearchResults, isFetching: studentSearchLoading } = useStudentAutocomplete(debouncedStudentSearch);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedStudentSearch(studentSearch.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [studentSearch]);

  // Bos durumdaki "Donem aidati ekle" cagrisi gercek olusturma akisini acar: yukaridaki
  // ogrenci arama alanina odaklanir, boylece kullanici ogrenciyi secip hesabindan aidat
  // ekleyebilir. Sadece metin gostermek yerine calisan bir aksiyon olmasi onemli -
  // aksi halde bos ekranda kullanici ne yapacagini bilemiyordu.
  const studentSearchRef = useRef<HTMLInputElement>(null);
  const startAddingDue = useCallback(() => {
    const input = studentSearchRef.current;
    if (!input) return;
    input.scrollIntoView({ behavior: "smooth", block: "center" });
    input.focus();
    setShowStudentSuggestions(true);
  }, []);

  const baseDues = useMemo(() => (dues ?? []).filter((due) => {
    const query = search.trim().toLocaleLowerCase("tr-TR");
    const matchesSearch = !query || `${due.studentName} ${due.teacherName} ${due.instrumentName} ${due.period}`.toLocaleLowerCase("tr-TR").includes(query);
    const matchesTeacher = teacherFilter === "all" || due.teacherId === teacherFilter;
    const matchesInstrument = instrumentFilter === "all" || due.instrumentId === instrumentFilter;
    return matchesSearch && matchesTeacher && matchesInstrument;
  }), [dues, instrumentFilter, search, teacherFilter]);

  const counts = useMemo(() => {
    const rows = baseDues;
    return {
      all: rows.filter((due) => due.status !== "Cancelled").length,
      open: rows.filter((due) => due.status === "Unpaid" || due.status === "Partial" || due.status === "Overdue").length,
      overdue: rows.filter((due) => due.status === "Overdue").length,
      partial: rows.filter((due) => due.status === "Partial").length,
      paid: rows.filter((due) => due.status === "Paid").length,
    };
  }, [baseDues]);

  const filterSummary = useMemo<BillingFilterSummary>(() => ({
    outstanding: baseDues.filter((item) => item.status === "Unpaid" || item.status === "Partial" || item.status === "Overdue").reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0),
    collected: baseDues.reduce((total, item) => total + item.totalPaid, 0),
    overdue: baseDues.filter((item) => item.status === "Overdue").reduce((total, item) => total + Math.max(0, item.amount - item.totalPaid), 0),
    openCount: baseDues.filter((item) => item.status === "Unpaid" || item.status === "Partial" || item.status === "Overdue").length,
    overdueCount: baseDues.filter((item) => item.status === "Overdue").length,
  }), [baseDues]);

  useEffect(() => onSummaryChange?.(filterSummary), [filterSummary, onSummaryChange]);

  const visibleDues = useMemo(() => baseDues.filter((due) => {
    const matchesFilter = filter === "all" ? due.status !== "Cancelled"
      : filter === "open" ? due.status === "Unpaid" || due.status === "Partial" || due.status === "Overdue"
        : filter === "overdue" ? due.status === "Overdue"
          : filter === "partial" ? due.status === "Partial" : due.status === "Paid";
    return matchesFilter;
  }), [baseDues, filter]);

  return <div className="space-y-4">
    <section className="app-card overflow-visible p-4 sm:p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div><p className="text-micro text-[var(--brand-strong)]">Yeni aidat</p><h2 className="mt-1 text-title">Öğrenci hesabından ekle</h2><p className="text-meta mt-1">Öğrenciyi ara; öğretmen ve enstrüman kaydına göre ücret planını aç.</p></div>
        <span className="rounded-full bg-[var(--success-soft)] px-2.5 py-1.5 text-[.62rem] font-bold text-[var(--success-strong)]">Hesap bazlı takip</span>
      </div>
      <div className="mt-4 grid gap-3 lg:grid-cols-[minmax(18rem,1.5fr)_minmax(12rem,1fr)_minmax(12rem,1fr)]">
        <div className="relative">
          <label className="space-y-1.5 text-[.68rem] font-bold text-[var(--muted)]"><span>Öğrenci</span><div className="relative"><Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--brand)]" /><input role="combobox" aria-expanded={showStudentSuggestions} aria-controls="billing-student-suggestions" ref={studentSearchRef} value={studentSearch} onFocus={() => setShowStudentSuggestions(true)} onChange={(event) => { setStudentSearch(event.target.value); setSelectedStudentId(null); setShowStudentSuggestions(true); }} placeholder="Öğrenci adı yazın…" className="field min-h-11 pl-9 text-sm" /></div></label>
          {showStudentSuggestions && studentSearch.trim().length >= 2 && <ul id="billing-student-suggestions" role="listbox" className="absolute inset-x-0 top-[4.3rem] z-30 overflow-hidden rounded-xl border border-[var(--line)] bg-white p-1.5 shadow-[0_14px_30px_rgba(80,48,24,.16)]">{studentSearchLoading && <li className="px-3 py-2 text-xs text-[var(--muted)]">Öğrenciler aranıyor…</li>}{!studentSearchLoading && !studentSearchResults?.length && <li className="px-3 py-2 text-xs text-[var(--muted)]">Eşleşen öğrenci bulunamadı.</li>}{studentSearchResults?.map((student) => <li key={`${student.studentId}-${student.teacherId}-${student.instrumentId}`} role="option" aria-selected={selectedStudentId === student.studentId}><button type="button" onMouseDown={(event) => event.preventDefault()} onClick={() => { setStudentSearch(student.studentName); setSelectedStudentId(student.studentId); setShowStudentSuggestions(false); }} className="pressable flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left hover:bg-[var(--surface-muted)]"><span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-[var(--brand-soft)] text-[.62rem] font-bold text-[var(--brand-strong)]">{student.studentName.split(" ").slice(0, 2).map((part) => part[0]).join("")}</span><span className="min-w-0"><span className="block truncate text-xs font-bold">{student.studentName}</span><span className="block truncate text-[.62rem] text-[var(--muted)]">{student.teacherName} · {student.instrumentName}{student.guardianPhoneMasked ? ` · ${student.guardianPhoneMasked}` : ""}</span></span></button></li>)}</ul>}
        </div>
        <label className="space-y-1.5 text-[.68rem] font-bold text-[var(--muted)]"><span>Öğretmene göre aidat</span><select value={teacherFilter} onChange={(event) => setTeacherFilter(event.target.value)} className="field min-h-11 text-sm"><option value="all">Tüm öğretmenler</option>{teachers?.filter((teacher) => teacher.status === "Active").map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>)}</select></label>
        <label className="space-y-1.5 text-[.68rem] font-bold text-[var(--muted)]"><span>Enstrümana göre aidat</span><select value={instrumentFilter} onChange={(event) => setInstrumentFilter(event.target.value)} className="field min-h-11 text-sm"><option value="all">Tüm enstrümanlar</option>{instruments?.map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}</select></label>
      </div>
      {selectedStudentId && <p className="mt-3 rounded-xl bg-[var(--brand-soft)] px-3 py-2.5 text-xs font-semibold text-[var(--brand-strong)]">Öğrenci hesabı aşağıda açıldı. Ücret planını oluşturduktan sonra dönem aidatını buradan ekleyebilirsin.</p>}
    </section>

    {selectedStudentId && <StudentBillingSection key={selectedStudentId} initialStudentId={selectedStudentId} showStudentPicker={false} onClose={() => { setSelectedStudentId(null); setStudentSearch(""); }} />}

    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] p-4 sm:p-5">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div><p className="text-micro text-[var(--brand-strong)]">Aidat listesi</p><h2 className="mt-1 text-title">Öğrenci aidatları</h2><p className="text-meta mt-1">Borcu gör, ödemeyi kaydet veya öğrenci hesabını aç.</p></div>
          <label className="relative w-full sm:w-80"><span className="sr-only">Öğrenci, öğretmen veya enstrüman ara</span><Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--muted)]" /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Öğrenci, öğretmen veya enstrüman ara" className="field min-h-11 pl-9 text-sm" /></label>
        </div>
        <div className="mt-4 flex gap-1 overflow-x-auto rounded-xl bg-[var(--surface-muted)] p-1" aria-label="Aidat durum filtresi">{FILTERS.map((item) => <button key={item.value} type="button" onClick={() => setFilter(item.value)} aria-pressed={filter === item.value} className={`pressable flex min-h-9 shrink-0 items-center gap-2 rounded-lg px-3 text-xs font-bold ${filter === item.value ? "bg-white text-[var(--brand-strong)] shadow-sm" : "text-[var(--muted)] hover:text-[var(--foreground)]"}`}>{item.label}<span className="rounded-full bg-[var(--surface-muted)] px-1.5 py-0.5 text-[.58rem]">{counts[item.value]}</span></button>)}</div>
      </div>

      <div className="hidden grid-cols-[minmax(12rem,1.25fr)_minmax(9rem,.9fr)_minmax(9rem,.9fr)_minmax(9rem,.9fr)_minmax(7rem,.7fr)_minmax(7rem,.75fr)_auto] gap-3 border-b border-[var(--line)] bg-[var(--surface-muted)]/55 px-4 py-2.5 text-[.62rem] font-bold uppercase tracking-[.08em] text-[var(--muted)] md:grid"><span>Öğrenci / enstrüman</span><span>Öğretmen</span><span>Dönem</span><span>Tutar / kalan</span><span>Vade</span><span>Durum</span><span className="text-right">İşlem</span></div>
      {isLoading && <div className="space-y-2 p-4">{[1, 2, 3, 4].map((item) => <div key={item} className="skeleton h-16 rounded-xl" />)}</div>}
      {!isLoading && isError && <div className="grid min-h-52 place-items-center p-8 text-center"><div><span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--danger-soft)] text-[var(--danger-strong)]"><Icon name="x" className="h-5 w-5" /></span><p className="mt-3 text-sm font-bold">Aidat listesi yüklenemedi</p><p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p><button type="button" onClick={() => void refetch()} disabled={isFetching} className="pressable mt-3 min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--foreground)] disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button></div></div>}
      {!isLoading && !isError && visibleDues.length > 0 && <div className="divide-y divide-[var(--line)]">{visibleDues.map((due) => <DueRow key={due.id} due={due} onOpenAccount={() => { setSelectedStudentId(due.studentId); setStudentSearch(due.studentName); }} />)}</div>}
      {!isLoading && !isError && !visibleDues.length && <div className="grid min-h-52 place-items-center p-8 text-center"><div>
        <span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="wallet" className="h-5 w-5" /></span>
        {!dues?.length
          ? <>
              <p className="mt-3 text-sm font-bold">Henüz aidat kaydı yok</p>
              <p className="text-meta mt-1">Bir öğrenci seçip ücret planına göre dönem aidatını oluşturarak başla. Aidat oluşturulduğunda tahsilat, kısmi ödeme ve gecikme takibi buradan yürür.</p>
              <button type="button" onClick={startAddingDue} className="pressable mt-3 min-h-9 rounded-lg bg-[var(--brand)] px-4 text-xs font-bold text-white">Dönem aidatı ekle</button>
            </>
          : <>
              <p className="mt-3 text-sm font-bold">Bu görünümde aidat yok</p>
              <p className="text-meta mt-1">Seçili filtrelerle eşleşen aidat bulunamadı. Filtreleri temizleyebilir veya yeni bir dönem aidatı ekleyebilirsin.</p>
              <div className="mt-3 flex flex-wrap items-center justify-center gap-2">
                <button type="button" onClick={() => { setFilter("all"); setSearch(""); setTeacherFilter("all"); setInstrumentFilter("all"); }} className="pressable min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-xs font-bold">Filtreleri temizle</button>
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
    <div className="grid items-center gap-3 md:grid-cols-[minmax(12rem,1.25fr)_minmax(9rem,.9fr)_minmax(9rem,.9fr)_minmax(9rem,.9fr)_minmax(7rem,.7fr)_minmax(7rem,.75fr)_auto]">
      <div className="flex min-w-0 items-center gap-3"><span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[.66rem] font-bold text-[var(--brand-strong)]">{due.studentName.split(" ").map((part) => part[0]).slice(0, 2).join("")}</span><span className="min-w-0"><strong className="block truncate text-sm">{due.studentName}</strong><span className="text-meta mt-0.5 block truncate">{due.instrumentName}</span></span></div>
      <div className="text-xs"><span className="text-[.62rem] font-bold text-[var(--muted)] md:hidden">Öğretmen · </span>{due.teacherName}</div>
      <div><span className="text-[.62rem] font-bold text-[var(--muted)] md:hidden">Dönem · </span><span className="text-xs font-semibold capitalize">{formatPeriod(due.period)}</span></div>
      <div><strong className="block text-xs tabular-nums">{formatMoney(due.amount, due.currency)}</strong><span className={`mt-0.5 block text-[.62rem] tabular-nums ${remaining ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>{remaining ? `${formatMoney(remaining, due.currency)} kaldı` : "Tamamı ödendi"}</span></div>
      <div className="text-xs"><span className="text-[.62rem] font-bold text-[var(--muted)] md:hidden">Vade · </span>{new Date(`${due.dueDate}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "short", year: "numeric" })}</div>
      <div><span className={`inline-flex rounded-full px-2 py-1 text-[.6rem] font-bold ${STATUS_TONES[due.status]}`}>{STATUS_LABELS[due.status]}</span></div>
      <div className="flex justify-end gap-1.5">{canCollect && <button type="button" onClick={() => setShowPayment((visible) => !visible)} className="pressable min-h-9 rounded-lg bg-[var(--brand)] px-3 text-[.66rem] font-bold text-white">Tahsilat</button>}<button type="button" onClick={onOpenAccount} className="pressable min-h-9 rounded-lg border border-[var(--line)] bg-white px-3 text-[.66rem] font-bold text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]">Hesap</button></div>
    </div>
    {showPayment && <form onSubmit={collect} className="mt-3 grid gap-2 rounded-xl border border-[var(--brand)]/25 bg-[var(--brand-soft)]/45 p-3 sm:grid-cols-[1fr_1fr_1fr_auto] sm:items-end"><label className="space-y-1 text-[.64rem] font-bold text-[var(--muted)]">Tutar<input type="number" min={0.01} max={remaining} step={0.01} value={amount} onChange={(event) => setAmount(Number(event.target.value))} required className="field min-h-10 bg-white text-xs" /></label><label className="space-y-1 text-[.64rem] font-bold text-[var(--muted)]">Tarih<input type="date" value={paymentDate} onChange={(event) => setPaymentDate(event.target.value)} required className="field min-h-10 bg-white text-xs" /></label><label className="space-y-1 text-[.64rem] font-bold text-[var(--muted)]">Yöntem<select value={method} onChange={(event) => setMethod(event.target.value as PaymentMethod)} className="field min-h-10 bg-white text-xs"><option value="Cash">Nakit</option><option value="Transfer">Havale</option><option value="Card">Kart</option><option value="Other">Diğer</option></select></label><button type="submit" disabled={recordPayment.isPending} className="pressable min-h-10 rounded-xl bg-[var(--brand)] px-4 text-xs font-bold text-white disabled:opacity-50">{recordPayment.isPending ? "Kaydediliyor…" : "Ödemeyi kaydet"}</button>{error && <p role="alert" className="text-xs font-semibold text-[var(--danger-strong)] sm:col-span-4">{error}</p>}</form>}
  </article>;
}
