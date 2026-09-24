"use client";

import { useMemo, useState } from "react";
import { Icon } from "@/components/icons";
import { MonthInput } from "@/components/month-input";
import { AdminGate, FormActions, FormMessage, Modal, PageHeader, RowMenu, RowMenuItem, SearchInput, SectionHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  recurringAmountFor, useChangeRecurringExpenseAmount, useCreateExpense, useCreateRecurringExpense, useEndRecurringExpense,
  useExpenses, useReceivables, useRecurringExpenses,
  type Expense, type ExpenseCategory, type Receivable, type RecurringExpense,
} from "@/lib/billing";

export default function CostsPage() {
  return <AdminGate><CostDashboard /></AdminGate>;
}

// Maliyet/maaş verisi tamamen Admin'e özel (docs/04-permissions.md): AdminGate ekranı, sunucu
// da `/api/expenses` ve aidat uçlarını yalnızca yönetici oturumuna açar. Eskiden bunun üstüne
// bir "şifreni tekrar doğrula" adımı vardı; kullanıcı geri bildirimi üzerine kaldırıldı
// ("admin zaten giriş yapmış") - asıl koruma oturum ve sunucu yetkisi, ek şifre sorusu değil.
//
// Giderin iki türü var (docs/10-decisions.md M9): kira, elektrik/su ortalaması, sabit maaş gibi
// HER AY TEKRAR EDEN kalemler bir kez girilir ve her aya kendiliğinden sayılır; tamir, alet gibi
// TEK SEFERLİK giderler tarihiyle deftere yazılır. Ekrandaki her toplam ikisinin birleşimidir.

const MONTHS = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];
const MONTHS_SHORT = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];
const WEEKDAYS = ["Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz"];
const CATEGORIES: ExpenseCategory[] = ["Salary", "Rent", "Utilities", "Other"];
const CATEGORY_LABEL: Record<ExpenseCategory, string> = { Salary: "Maaş", Utilities: "Elektrik / su", Rent: "Kira", Other: "Diğer" };
// İki seri, dataviz doğrulayıcısından geçti (beyaz zemin, CVD ΔE ≥ 15). Sabit = marka turuncusu.
const SERIES = { recurring: "#d9662a", oneOff: "#9b3f6b" };

type Scope = "month" | "year";
// Seçili dönem: yıllık görünümde `month` yok sayılır. `day` yalnızca aylık takvimde bir güne
// tıklanınca dolar ve defteri o güne daraltır.
interface Period { scope: Scope; year: number; month: number; day: number | null }
type CategoryFilter = ExpenseCategory | "all";

const pad = (value: number) => String(value).padStart(2, "0");
const money = (value: number) => `₺${value.toLocaleString("tr-TR", { maximumFractionDigits: 2 })}`;
// Tarihler sunucudan "YYYY-MM-DD", aylar "YYYY-MM" gelir; saat dilimine takılmamak için Date'e
// çevirmeden metin olarak karşılaştırılır.
const monthKey = (year: number, month: number) => `${year}-${pad(month + 1)}`;
const monthLabel = (key: string) => `${MONTHS_SHORT[Number(key.slice(5, 7)) - 1]} ${key.slice(0, 4)}`;
const inPeriod = (date: string, period: Pick<Period, "scope" | "year" | "month">) =>
  period.scope === "year" ? date.startsWith(`${period.year}-`) : date.startsWith(`${monthKey(period.year, period.month)}-`);
const periodMonths = (period: Pick<Period, "scope" | "year" | "month">) =>
  period.scope === "year" ? Array.from({ length: 12 }, (_, month) => monthKey(period.year, month)) : [monthKey(period.year, period.month)];

function shift(period: Period, step: -1 | 1): Period {
  if (period.scope === "year") return { ...period, year: period.year + step, day: null };
  const index = period.year * 12 + period.month + step;
  return { ...period, year: Math.floor(index / 12), month: index % 12, day: null };
}

function periodLabel(period: Pick<Period, "scope" | "year" | "month">) {
  return period.scope === "year" ? String(period.year) : `${MONTHS[period.month]} ${period.year}`;
}

// Bir aidata düşen tahsilatları ödeme tarihine göre verir. Düzeltilmiş bir tahsilatın
// geçerli tutarı en son düzeltmedir (sunucudaki ComputeEffectivePaymentAmountsAsync ile aynı
// kural); düzeltme satırları ayrıca sayılmaz, yoksa para iki kez gelir yazılırdı.
function collectedPayments(receivables: Receivable[]) {
  return receivables.flatMap((receivable) => {
    const corrections = receivable.payments.filter((payment) => payment.kind === "Correction");
    return receivable.payments
      .filter((payment) => payment.kind === "Payment")
      .map((payment) => {
        const latest = corrections
          .filter((correction) => correction.correctsPaymentId === payment.id)
          .sort((a, b) => (b.recordedAt ?? "").localeCompare(a.recordedAt ?? ""))[0];
        return { date: payment.paymentDate, amount: latest ? latest.amount : payment.amount };
      });
  });
}

interface MonthTotal { key: string; recurring: number; oneOff: number; planned: boolean }

function CostDashboard() {
  const today = useMemo(() => new Date(), []);
  const currentMonth = monthKey(today.getFullYear(), today.getMonth());
  const [period, setPeriod] = useState<Period>({ scope: "month", year: today.getFullYear(), month: today.getMonth(), day: null });
  const [category, setCategory] = useState<CategoryFilter>("all");
  const [search, setSearch] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const { data: expenses, isLoading: expensesLoading } = useExpenses();
  const { data: recurringData, isLoading: recurringLoading } = useRecurringExpenses();
  const { data: receivables, isLoading: receivablesLoading } = useReceivables();

  const allExpenses = useMemo(() => expenses ?? [], [expenses]);
  const allRecurring = useMemo(() => recurringData ?? [], [recurringData]);
  const payments = useMemo(() => collectedPayments(receivables ?? []), [receivables]);

  // Kategori filtresi özet kartlarını, grafikleri ve defteri birlikte daraltır - "bu yıl kiraya
  // ne verdik" tek tıkla yanıtlanır. Kategori dağılımı kartı ise hep tüm kategorileri gösterir.
  const oneOffs = useMemo(() => category === "all" ? allExpenses : allExpenses.filter((expense) => expense.category === category), [allExpenses, category]);
  const recurring = useMemo(() => category === "all" ? allRecurring : allRecurring.filter((item) => item.category === category), [allRecurring, category]);

  // Aylık toplam = o aya düşen sabit kalemler + o ayın tek seferlik giderleri. Bugünden sonraki
  // aylar "planlanan"dır: sabit kalemler oraya da sayılır ama henüz ödenmiş değildir.
  const totalsFor = useMemo(() => (months: string[]): MonthTotal[] => months.map((key) => ({
    key,
    recurring: recurring.reduce((sum, item) => sum + recurringAmountFor(item, key), 0),
    oneOff: oneOffs.filter((expense) => expense.expenseDate.startsWith(`${key}-`)).reduce((sum, expense) => sum + expense.amount, 0),
    planned: key > currentMonth,
  })), [recurring, oneOffs, currentMonth]);

  const months = useMemo(() => totalsFor(periodMonths(period)), [totalsFor, period]);
  const previous = shift(period, -1);
  const stats = useMemo(() => {
    const sum = (rows: MonthTotal[]) => rows.reduce((total, row) => total + row.recurring + row.oneOff, 0);
    const total = sum(months);
    const toDate = sum(months.filter((row) => !row.planned));
    const recurringTotal = months.reduce((total, row) => total + row.recurring, 0);
    const previousTotal = sum(totalsFor(periodMonths(previous)));
    const income = payments.filter((payment) => inPeriod(payment.date, period)).reduce((total, payment) => total + payment.amount, 0);
    const pending = (receivables ?? [])
      .filter((row) => row.status !== "Paid" && row.status !== "Cancelled")
      .reduce((total, row) => total + Math.max(0, row.amount - row.totalPaid), 0);
    // Net, gelecek ayların planlanan giderini değil bugüne kadarkini düşer - tahsilat da yalnızca
    // gerçekleşmiş parayı sayıyor.
    return { total, toDate, recurringTotal, oneOffTotal: total - recurringTotal, previousTotal, income, net: income - toDate, pending };
  }, [months, totalsFor, previous, payments, period, receivables]);

  const needle = search.trim().toLocaleLowerCase("tr-TR");
  const matches = (text: string | null) => !needle || (text ?? "").toLocaleLowerCase("tr-TR").includes(needle);
  const dayKey = period.scope === "month" && period.day !== null ? `${monthKey(period.year, period.month)}-${pad(period.day)}` : null;
  const listedOneOffs = oneOffs.filter((expense) =>
    inPeriod(expense.expenseDate, period) && (!dayKey || expense.expenseDate === dayKey) && (matches(expense.description) || matches(expense.note)));
  const listedRecurring = dayKey ? [] : recurring
    .map((item) => ({ item, amount: periodMonths(period).reduce((sum, key) => sum + recurringAmountFor(item, key), 0) }))
    .filter(({ item, amount }) => amount > 0 && (matches(item.name) || matches(item.note)));

  const isCurrent = period.year === today.getFullYear() && (period.scope === "year" || period.month === today.getMonth());
  const loading = expensesLoading || recurringLoading || receivablesLoading;
  const change = stats.previousTotal > 0 ? ((stats.total - stats.previousTotal) / stats.previousTotal) * 100 : null;
  const futurePeriod = months.every((row) => row.planned);
  const newExpenseDate = dayKey ?? (isCurrent ? `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}` : null);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Giderler"
        description="Kira, fatura ve maaş gibi sabit giderler bir kez girilir, her aya kendiliğinden sayılır."
        actions={<button type="button" onClick={() => setShowCreate(true)} className="btn btn-primary"><Icon name="plus" className="h-4 w-4" />Gider ekle</button>}
      />

      <section className="app-card flex flex-wrap items-center gap-2 p-3 sm:p-4" aria-label="Dönem ve kategori filtreleri">
        <div className="inline-flex rounded-xl border border-[var(--line)] p-1" role="group" aria-label="Görünüm">
          {([["month", "Aylık"], ["year", "Yıllık"]] as const).map(([value, label]) => (
            <button key={value} type="button" onClick={() => setPeriod((current) => ({ ...current, scope: value, day: null }))} aria-pressed={period.scope === value} className={`pressable min-h-10 rounded-lg px-3 text-xs font-bold ${period.scope === value ? "bg-[var(--brand)] text-white" : "text-[var(--muted)]"}`}>{label}</button>
          ))}
        </div>
        <div className="flex items-center gap-1 rounded-[.9rem] bg-[var(--surface-muted)] p-1">
          <button type="button" onClick={() => setPeriod((current) => shift(current, -1))} className="icon-btn icon-btn-quiet" aria-label={period.scope === "year" ? "Önceki yıl" : "Önceki ay"}><Icon name="arrow-left" className="h-4 w-4" /></button>
          <span aria-live="polite" className="min-w-[7.5rem] text-center text-xs font-bold tabular-nums">{periodLabel(period)}</span>
          <button type="button" onClick={() => setPeriod((current) => shift(current, 1))} className="icon-btn icon-btn-quiet" aria-label={period.scope === "year" ? "Sonraki yıl" : "Sonraki ay"}><Icon name="arrow-right" className="h-4 w-4" /></button>
        </div>
        <button type="button" disabled={isCurrent} onClick={() => setPeriod((current) => ({ ...current, year: today.getFullYear(), month: today.getMonth(), day: null }))} className="btn btn-quiet px-3 text-[.75rem] font-semibold disabled:cursor-default disabled:opacity-50">{period.scope === "year" ? "Bu yıl" : "Bu ay"}</button>
        <span className="mx-1 hidden h-6 w-px bg-[var(--line)] md:block" aria-hidden="true" />
        <div className="flex w-full gap-1.5 overflow-x-auto md:w-auto" role="group" aria-label="Kategoriye göre filtrele">
          {(["all", ...CATEGORIES] as const).map((value) => (
            <button key={value} type="button" onClick={() => setCategory(value)} aria-pressed={category === value} className={`pressable min-h-10 shrink-0 rounded-xl border px-3 text-xs font-bold ${category === value ? "border-[var(--brand)] bg-[var(--brand)] text-white" : "border-[var(--line)] bg-white text-[#5c4d3f] hover:border-[var(--brand)] hover:text-[var(--brand)]"}`}>
              {value === "all" ? "Tümü" : CATEGORY_LABEL[value]}
            </button>
          ))}
        </div>
      </section>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <CostStat
          label={`${category === "all" ? "Toplam gider" : `${CATEGORY_LABEL[category]} gideri`}${futurePeriod ? " (planlanan)" : ""}`}
          value={loading ? "…" : money(stats.total)}
          secondary={[
            `Sabit ${money(stats.recurringTotal)} · tek seferlik ${money(stats.oneOffTotal)}`,
            change !== null ? `önceki ${period.scope === "year" ? "yıla" : "aya"} göre ${change > 0 ? "▲" : change < 0 ? "▼" : ""} %${Math.abs(change).toLocaleString("tr-TR", { maximumFractionDigits: 0 })}` : null,
          ].filter(Boolean).join(" · ")}
          tone="muted"
        />
        <CostStat label="Tahsil edilen aidat" value={loading ? "…" : money(stats.income)} secondary="Ödeme tarihi bu dönemde olanlar" tone="success" />
        <CostStat
          label={category === "all" ? "Net sonuç" : "Tahsilat − bu kategori"}
          value={loading ? "…" : `${stats.net < 0 ? "−" : ""}${money(Math.abs(stats.net))}`}
          secondary={period.scope === "year" && stats.toDate !== stats.total ? `Bugüne kadarki ${money(stats.toDate)} gider düşüldü` : stats.net < 0 ? "Gider tahsilatı aşıyor" : "Tahsilat gideri karşılıyor"}
          tone={stats.net < 0 ? "danger" : "brand"}
        />
        <CostStat label="Bekleyen aidat" value={loading ? "…" : money(stats.pending)} secondary="Tüm dönemler · Aidatlar ekranından tahsil edilir" tone="warning" href="/dashboard/billing" />
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <section className="app-card p-4 sm:p-5">
          {period.scope === "month" ? (
            <>
              <SectionHeader title="Gider takvimi" description="Tek seferlik giderler güne göre; güne dokun, defter o güne daralsın." actions={period.day !== null ? <button type="button" onClick={() => setPeriod((current) => ({ ...current, day: null }))} className="btn btn-quiet px-3 text-[.75rem]">Günü temizle</button> : undefined} />
              <RecurringStrip items={recurring} month={monthKey(period.year, period.month)} planned={months[0]?.planned ?? false} />
              <MonthCalendar period={period} expenses={oneOffs.filter((expense) => inPeriod(expense.expenseDate, period))} today={today} onSelectDay={(day) => setPeriod((current) => ({ ...current, day: current.day === day ? null : day }))} />
            </>
          ) : (
            <>
              <SectionHeader title="Aylara göre giderler" description="Sabit kalemler yılın her ayına sayılır. Bir aya dokun, o ayın takvimine geç." />
              <YearBars year={period.year} months={months} onSelectMonth={(month) => setPeriod({ scope: "month", year: period.year, month, day: null })} />
            </>
          )}
        </section>
        <CategoryBreakdown
          rows={CATEGORIES.map((value) => ({
            category: value,
            amount: allExpenses.filter((expense) => expense.category === value && inPeriod(expense.expenseDate, period)).reduce((sum, expense) => sum + expense.amount, 0)
              + allRecurring.filter((item) => item.category === value).reduce((sum, item) => sum + periodMonths(period).reduce((total, key) => total + recurringAmountFor(item, key), 0), 0),
          }))}
          active={category}
          onSelect={setCategory}
        />
      </div>

      <RecurringExpensesPanel items={allRecurring} loading={recurringLoading} currentMonth={currentMonth} />

      <section className="app-card overflow-hidden">
        <div className="border-b border-[var(--line)] p-4 sm:p-5">
          <SectionHeader
            title="Gider defteri"
            description={dayKey ? `${period.day} ${MONTHS[period.month]} ${period.year}` : `${periodLabel(period)} · kayıtlar silinmez, tarih ve tutarıyla saklanır`}
            actions={<SearchInput value={search} onChange={setSearch} label="Giderlerde ara" placeholder="Açıklamada ara" />}
          />
        </div>
        <ExpenseLedger oneOffs={listedOneOffs} recurring={listedRecurring} scope={period.scope} loading={loading} />
      </section>

      <Modal open={showCreate} title="Gider ekle" onClose={() => setShowCreate(false)} size="sm">
        <CreateExpenseForm
          initialDate={newExpenseDate}
          initialMonth={isCurrent || period.scope === "year" ? currentMonth : monthKey(period.year, period.month)}
          initialCategory={category === "all" ? "Rent" : category}
          onClose={() => setShowCreate(false)}
        />
      </Modal>
    </div>
  );
}

// Aylık görünümde takvimin üstünde: o aya kendiliğinden sayılan sabit kalemler. Takvim günleri
// yalnızca tek seferlik giderleri gösterir - kiranın "hangi gün" ödendiği burada önemli değil.
function RecurringStrip({ items, month, planned }: { items: RecurringExpense[]; month: string; planned: boolean }) {
  const active = items.map((item) => ({ item, amount: recurringAmountFor(item, month) })).filter((row) => row.amount > 0);
  if (!active.length) return <p className="text-meta mt-3 rounded-xl bg-[var(--surface-muted)] px-3 py-2">Bu ay için tanımlı sabit gider yok. Kira veya fatura ortalamasını &quot;Gider ekle&quot; ile bir kez gir, her aya sayılsın.</p>;
  const total = active.reduce((sum, row) => sum + row.amount, 0);
  return (
    <div className="mt-3 rounded-xl border border-[var(--line)] px-3 py-2.5">
      <p className="flex flex-wrap items-baseline justify-between gap-2 text-xs font-bold">
        <span className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-[3px]" style={{ background: SERIES.recurring }} aria-hidden="true" />Her ay tekrar eden{planned && " · planlanan"}</span>
        <span className="tabular-nums text-sm">{money(total)}</span>
      </p>
      <div className="mt-2 flex flex-wrap gap-1.5">
        {active.map(({ item, amount }) => (
          <span key={item.id} className="rounded-lg bg-[var(--surface-muted)] px-2 py-1 text-[.75rem]"><strong>{item.name}</strong> <span className="tabular-nums text-[var(--muted)]">{money(amount)}</span></span>
        ))}
      </div>
    </div>
  );
}

function MonthCalendar({ period, expenses, today, onSelectDay }: { period: Period; expenses: Expense[]; today: Date; onSelectDay: (day: number) => void }) {
  const daysInMonth = new Date(period.year, period.month + 1, 0).getDate();
  // Hafta pazartesi başlar (Türkiye takvimi); getDay() pazarı 0 verir.
  const leading = (new Date(period.year, period.month, 1).getDay() + 6) % 7;
  const totals = new Map<number, { amount: number; count: number }>();
  for (const expense of expenses) {
    const day = Number(expense.expenseDate.slice(8, 10));
    const current = totals.get(day) ?? { amount: 0, count: 0 };
    totals.set(day, { amount: current.amount + expense.amount, count: current.count + 1 });
  }
  const max = Math.max(0, ...Array.from(totals.values(), (entry) => entry.amount));
  const isThisMonth = today.getFullYear() === period.year && today.getMonth() === period.month;

  return (
    <div className="mt-3">
      <div className="grid grid-cols-7 gap-1 text-center text-[.6875rem] font-bold text-[var(--muted)]" aria-hidden="true">
        {WEEKDAYS.map((name) => <span key={name}>{name}</span>)}
      </div>
      <div className="mt-1 grid grid-cols-7 gap-1">
        {Array.from({ length: leading }, (_, index) => <span key={`empty-${index}`} />)}
        {Array.from({ length: daysInMonth }, (_, index) => {
          const day = index + 1;
          const entry = totals.get(day);
          const selected = period.day === day;
          // Tek tonlu yoğunluk (tek seferlik serinin rengi): tutar arttıkça zemin koyulaşır, tutar
          // yine yazıyla da görünür.
          const intensity = entry && max > 0 ? 0.18 + 0.62 * (entry.amount / max) : 0;
          const label = `${day} ${MONTHS[period.month]}${entry ? `: ${money(entry.amount)}, ${entry.count} kayıt` : ": tek seferlik gider yok"}`;
          return (
            <button
              key={day}
              type="button"
              onClick={() => onSelectDay(day)}
              aria-pressed={selected}
              aria-label={label}
              title={label}
              className={`pressable relative flex min-h-[3.4rem] min-w-0 flex-col items-start justify-between rounded-xl border p-1.5 text-left sm:min-h-[4.2rem] sm:p-2 ${selected ? "border-[var(--brand)] ring-2 ring-[var(--brand)]" : "border-[var(--line)]"}`}
              style={{ background: entry ? `rgba(155, 63, 107, ${intensity})` : "var(--surface)" }}
            >
              <span className={`text-[.6875rem] font-bold tabular-nums ${isThisMonth && today.getDate() === day ? "rounded-md bg-[var(--brand)] px-1 text-white" : intensity > 0.5 ? "text-white" : "text-[var(--muted)]"}`}>{day}</span>
              {entry && (
                <span className={`w-full truncate text-[.625rem] font-bold tabular-nums sm:text-[.6875rem] ${intensity > 0.5 ? "text-white" : "text-[var(--foreground)]"}`}>
                  {/* Telefonda hücre dar: binlik tutar "3,2B" diye kısalır; tam tutar ipucunda. */}
                  <span className="sm:hidden">{entry.amount >= 1000 ? `${(entry.amount / 1000).toLocaleString("tr-TR", { maximumFractionDigits: 1 })}B` : Math.round(entry.amount)}</span>
                  <span className="max-sm:hidden">{entry.amount >= 10000 ? `₺${(entry.amount / 1000).toLocaleString("tr-TR", { maximumFractionDigits: 1 })}B` : money(entry.amount)}</span>
                </span>
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
}

// Yığılmış aylık çubuk: altta sabit, üstte tek seferlik. Gelecek aylar soluk - yalnızca sabit
// kalemlerin planlanan tutarını taşır. Kimlik renkle değil lejant + ipucu metniyle de verilir.
function YearBars({ year, months, onSelectMonth }: { year: number; months: MonthTotal[]; onSelectMonth: (month: number) => void }) {
  const totals = months.map((row) => row.recurring + row.oneOff);
  const max = Math.max(0, ...totals);
  const peak = max > 0 ? totals.indexOf(max) : -1;
  const yearTotal = totals.reduce((sum, value) => sum + value, 0);
  const activeMonths = totals.filter((value) => value > 0).length;
  const hasPlanned = months.some((row) => row.planned && row.recurring + row.oneOff > 0);

  return (
    <div className="mt-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-meta">
          Yıl toplamı <strong className="tabular-nums text-[var(--foreground)]">{money(yearTotal)}</strong>
          {activeMonths > 0 && <> · aylık ortalama <strong className="tabular-nums text-[var(--foreground)]">{money(Math.round(yearTotal / activeMonths))}</strong></>}
        </p>
        <ul className="flex flex-wrap gap-3 text-[.75rem] font-semibold text-[var(--muted)]" aria-label="Lejant">
          <li className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-[3px]" style={{ background: SERIES.recurring }} />Sabit</li>
          <li className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-[3px]" style={{ background: SERIES.oneOff }} />Tek seferlik</li>
          {hasPlanned && <li className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-[3px] opacity-40" style={{ background: SERIES.recurring }} />Planlanan</li>}
        </ul>
      </div>
      <div className="mt-3 flex h-52 items-end gap-[2px] border-b border-[var(--line)]" role="list" aria-label={`${year} aylık gider çubukları`}>
        {months.map((row, month) => {
          const total = row.recurring + row.oneOff;
          const label = `${MONTHS[month]} ${year}${row.planned ? " (planlanan)" : ""}: ${money(total)} - sabit ${money(row.recurring)}, tek seferlik ${money(row.oneOff)}`;
          const height = (value: number) => (max > 0 ? (value / max) * 85 : 0);
          return (
            <button key={row.key} type="button" role="listitem" onClick={() => onSelectMonth(month)} title={label} aria-label={label} className={`group relative flex h-full min-w-0 flex-1 flex-col items-center justify-end ${row.planned ? "opacity-40" : ""}`}>
              {month === peak && <span className="mb-1 whitespace-nowrap text-[.625rem] font-bold tabular-nums text-[var(--foreground)] max-sm:hidden">{money(total)}</span>}
              {/* Segmentler arasında 2px zemin boşluğu; üst uç 4px yuvarlatılmış. */}
              {row.oneOff > 0 && <span className="w-full max-w-9 rounded-t-[4px] group-hover:brightness-90" style={{ height: `${Math.max(2, height(row.oneOff))}%`, background: SERIES.oneOff }} />}
              {row.oneOff > 0 && row.recurring > 0 && <span className="h-[2px] w-full max-w-9 bg-[var(--surface)]" />}
              {row.recurring > 0 && <span className={`w-full max-w-9 group-hover:brightness-90 ${row.oneOff > 0 ? "" : "rounded-t-[4px]"}`} style={{ height: `${Math.max(2, height(row.recurring))}%`, background: SERIES.recurring }} />}
            </button>
          );
        })}
      </div>
      <div className="mt-1 flex gap-[2px]" aria-hidden="true">
        {MONTHS_SHORT.map((name) => <span key={name} className="min-w-0 flex-1 text-center text-[.625rem] font-bold text-[var(--muted)]">{name}</span>)}
      </div>
    </div>
  );
}

function CategoryBreakdown({ rows, active, onSelect }: { rows: { category: ExpenseCategory; amount: number }[]; active: CategoryFilter; onSelect: (value: CategoryFilter) => void }) {
  const total = rows.reduce((sum, row) => sum + row.amount, 0);
  const sorted = [...rows].sort((a, b) => b.amount - a.amount);

  return (
    <section className="app-card p-4 sm:p-5">
      <SectionHeader title="Kategori dağılımı" description={total > 0 ? `Dönem toplamı ${money(total)} · sabit + tek seferlik` : "Bu dönemde gider yok."} />
      <ul className="mt-4 space-y-3">
        {sorted.map(({ category, amount }) => {
          const share = total > 0 ? (amount / total) * 100 : 0;
          const selected = active === category;
          return (
            <li key={category}>
              <button type="button" onClick={() => onSelect(selected ? "all" : category)} aria-pressed={selected} className={`pressable w-full rounded-xl border p-2.5 text-left ${selected ? "border-[var(--brand)] bg-[var(--brand-soft)]" : "border-transparent hover:border-[var(--line)]"}`}>
                <span className="flex items-baseline justify-between gap-2 text-sm">
                  <strong>{CATEGORY_LABEL[category]}</strong>
                  <span className="tabular-nums"><strong>{money(amount)}</strong><span className="text-meta ml-1.5">%{share.toLocaleString("tr-TR", { maximumFractionDigits: 0 })}</span></span>
                </span>
                <span className="mt-1.5 block h-2 overflow-hidden rounded-full bg-[var(--surface-muted)]">
                  <span className="block h-full rounded-full bg-[var(--brand)]" style={{ width: `${share}%` }} />
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

// Sabit kalemlerin yönetimi. Tutar güncellemesi eski tutarı SİLMEZ: seçilen aydan itibaren yeni
// tutar geçerli olur, önceki aylar eski tutarla kalır; geçmiş satır olarak aşağıda görünür.
function RecurringExpensesPanel({ items, loading, currentMonth }: { items: RecurringExpense[]; loading: boolean; currentMonth: string }) {
  const [changing, setChanging] = useState<RecurringExpense | null>(null);
  const [ending, setEnding] = useState<RecurringExpense | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [showEnded, setShowEnded] = useState(false);
  const active = items.filter((item) => !item.isEnded);
  const ended = items.filter((item) => item.isEnded);
  const visible = showEnded ? [...active, ...ended] : active;
  const monthlyTotal = active.reduce((sum, item) => sum + recurringAmountFor(item, currentMonth), 0);

  return (
    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] p-4 sm:p-5">
        <SectionHeader
          title="Sabit aylık giderler"
          description={active.length ? `Bu ay toplam ${money(monthlyTotal)} · her aya kendiliğinden sayılır` : "Kira, elektrik/su ortalaması, sabit maaş gibi kalemleri bir kez gir."}
          actions={ended.length > 0 ? <button type="button" onClick={() => setShowEnded((value) => !value)} className="btn btn-quiet px-3 text-[.75rem]">{showEnded ? "Bitenleri gizle" : `Bitenler (${ended.length})`}</button> : undefined}
        />
      </div>
      {loading ? <p className="text-meta px-4 py-6 text-center">Yükleniyor…</p> : !visible.length ? <p className="text-meta px-4 py-6 text-center">Henüz sabit gider kalemi yok.</p> : (
        <ul className="divide-y divide-[var(--line)]">
          {visible.map((item) => {
            const open = item.amounts.find((amount) => amount.effectiveUntil === null);
            const last = item.amounts[item.amounts.length - 1];
            const upcoming = open && open.effectiveFrom > currentMonth;
            return (
              <li key={item.id} className={`px-4 py-3 ${item.isEnded ? "opacity-60" : ""}`}>
                <div className="flex items-center justify-between gap-3">
                  <button type="button" onClick={() => setExpanded((value) => value === item.id ? null : item.id)} aria-expanded={expanded === item.id} className="min-w-0 flex-1 text-left">
                    <strong className="block truncate text-sm">{item.name}</strong>
                    <span className="text-meta">
                      {CATEGORY_LABEL[item.category]} · {item.isEnded ? `${monthLabel(last.effectiveUntil!)} sonunda bitti` : upcoming ? `${monthLabel(open.effectiveFrom)} ayından itibaren ${money(open.monthlyAmount)}` : `${monthLabel(open!.effectiveFrom)} ayından beri`}
                      {item.amounts.length > 1 && ` · ${item.amounts.length} tutar dönemi`}
                    </span>
                  </button>
                  <strong className="shrink-0 tabular-nums">{money(recurringAmountFor(item, currentMonth))}<span className="text-meta font-normal"> /ay</span></strong>
                  {!item.isEnded && (
                    <RowMenu label={`${item.name} eylemleri`}>
                      {(close) => (
                        <>
                          <RowMenuItem icon="pencil" onClick={() => { close(); setChanging(item); }}>Tutarı güncelle</RowMenuItem>
                          <RowMenuItem icon="x" tone="danger" onClick={() => { close(); setEnding(item); }}>Sona erdir</RowMenuItem>
                        </>
                      )}
                    </RowMenu>
                  )}
                </div>
                {expanded === item.id && (
                  <ol className="mt-2 space-y-1 rounded-xl bg-[var(--surface-muted)] p-2.5 text-[.75rem]" aria-label="Tutar geçmişi">
                    {[...item.amounts].reverse().map((amount) => (
                      <li key={amount.id} className="flex justify-between gap-2 tabular-nums">
                        <span>{monthLabel(amount.effectiveFrom)} – {amount.effectiveUntil ? monthLabel(amount.effectiveUntil) : "devam ediyor"}</span>
                        <strong>{money(amount.monthlyAmount)}</strong>
                      </li>
                    ))}
                    {item.note && <li className="text-[var(--muted)]">Not: {item.note}</li>}
                  </ol>
                )}
              </li>
            );
          })}
        </ul>
      )}

      <Modal open={changing !== null} title="Tutarı güncelle" onClose={() => setChanging(null)} size="sm">
        {changing && <ChangeAmountForm item={changing} currentMonth={currentMonth} onClose={() => setChanging(null)} />}
      </Modal>
      <Modal open={ending !== null} title="Kalemi sona erdir" onClose={() => setEnding(null)} size="sm">
        {ending && <EndRecurringForm item={ending} currentMonth={currentMonth} onClose={() => setEnding(null)} />}
      </Modal>
    </section>
  );
}

function ChangeAmountForm({ item, currentMonth, onClose }: { item: RecurringExpense; currentMonth: string; onClose: () => void }) {
  const change = useChangeRecurringExpenseAmount();
  const open = item.amounts.find((amount) => amount.effectiveUntil === null)!;
  const [amount, setAmount] = useState(0);
  const [from, setFrom] = useState(open.effectiveFrom > currentMonth ? open.effectiveFrom : currentMonth);
  const [error, setError] = useState<string | null>(null);
  const correction = from === open.effectiveFrom;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await change.mutateAsync({ id: item.id, monthlyAmount: amount, effectiveFrom: from });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Tutar güncellenemedi.");
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3.5">
      <p className="text-meta"><strong className="text-[var(--foreground)]">{item.name}</strong> şu an {money(open.monthlyAmount)} / ay ({monthLabel(open.effectiveFrom)} ayından beri).</p>
      <label className="form-label">Yeni aylık tutar (₺)<input type="number" inputMode="decimal" min={0.01} step={0.01} value={amount || ""} onChange={(event) => setAmount(Number(event.target.value))} required autoFocus className="field text-sm" /></label>
      <div className="form-label">Geçerli olacağı ilk ay<MonthInput value={from} onChange={setFrom} label="Geçerli olacağı ilk ay" /></div>
      <FormMessage tone="success">
        {correction
          ? `${monthLabel(from)} tutarı düzeltilecek; eski tutar kayıtta kalır ama hesaba girmez.`
          : `${monthLabel(from)} ve sonrası yeni tutarla sayılır. Önceki aylar ${money(open.monthlyAmount)} olarak kalır.`}
      </FormMessage>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Tutarı kaydet" pending={change.isPending} />
    </form>
  );
}

function EndRecurringForm({ item, currentMonth, onClose }: { item: RecurringExpense; currentMonth: string; onClose: () => void }) {
  const end = useEndRecurringExpense();
  const open = item.amounts.find((amount) => amount.effectiveUntil === null)!;
  const [lastMonth, setLastMonth] = useState(open.effectiveFrom > currentMonth ? open.effectiveFrom : currentMonth);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await end.mutateAsync({ id: item.id, lastMonth });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Kalem sona erdirilemedi.");
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3.5">
      <p className="text-meta"><strong className="text-[var(--foreground)]">{item.name}</strong> seçilen ayın sonuna kadar sayılır, sonraki aylara yansımaz. Geçmiş aylar olduğu gibi kalır.</p>
      <div className="form-label">Sayılacağı son ay<MonthInput value={lastMonth} onChange={setLastMonth} label="Sayılacağı son ay" /></div>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Sona erdir" pending={end.isPending} />
    </form>
  );
}

function ExpenseLedger({ oneOffs, recurring, scope, loading }: { oneOffs: Expense[]; recurring: { item: RecurringExpense; amount: number }[]; scope: Scope; loading: boolean }) {
  // Sunucu kayıtları tarihe göre yeniden eskiye döndürür; gruplar bu sırayı korur.
  const groups = useMemo(() => {
    const map = new Map<string, Expense[]>();
    for (const expense of oneOffs) {
      const key = scope === "year" ? expense.expenseDate.slice(0, 7) : expense.expenseDate;
      map.set(key, [...(map.get(key) ?? []), expense]);
    }
    return Array.from(map.entries());
  }, [oneOffs, scope]);

  if (loading) return <p className="text-meta px-4 py-6 text-center">Yükleniyor…</p>;
  if (!oneOffs.length && !recurring.length) return <p className="text-meta px-4 py-6 text-center">Bu filtreyle eşleşen gider yok.</p>;

  return (
    <div>
      {recurring.length > 0 && (
        <LedgerGroup heading={scope === "year" ? "Her ay tekrar eden · yıl toplamı" : "Her ay tekrar eden"} total={recurring.reduce((sum, row) => sum + row.amount, 0)}>
          {recurring.map(({ item, amount }) => (
            <LedgerRow key={item.id} title={item.name} meta={`${CATEGORY_LABEL[item.category]} · sabit`} amount={`${amount.toLocaleString("tr-TR", { minimumFractionDigits: 2 })} TRY`} />
          ))}
        </LedgerGroup>
      )}
      {groups.map(([key, rows]) => {
        const [year, month, day] = key.split("-").map(Number);
        const heading = scope === "year" ? `${MONTHS[month - 1]} ${year} · tek seferlik` : `${day} ${MONTHS[month - 1]} ${year}, ${WEEKDAYS[(new Date(year, month - 1, day).getDay() + 6) % 7]}`;
        return (
          <LedgerGroup key={key} heading={heading} total={rows.reduce((sum, row) => sum + row.amount, 0)}>
            {rows.map((expense) => (
              <LedgerRow
                key={expense.id}
                title={expense.description}
                meta={`${CATEGORY_LABEL[expense.category]}${scope === "year" ? ` · ${Number(expense.expenseDate.slice(8, 10))} ${MONTHS_SHORT[month - 1]}` : ""}${expense.note ? ` · ${expense.note}` : ""}`}
                amount={`${expense.amount.toLocaleString("tr-TR", { minimumFractionDigits: 2 })} ${expense.currency}`}
              />
            ))}
          </LedgerGroup>
        );
      })}
    </div>
  );
}

function LedgerGroup({ heading, total, children }: { heading: string; total: number; children: React.ReactNode }) {
  return (
    <div>
      <div className="flex items-center justify-between bg-[var(--surface-muted)] px-4 py-1.5 text-[.75rem] font-bold text-[var(--muted)]">
        <span>{heading}</span>
        <span className="tabular-nums">{money(total)}</span>
      </div>
      <ul className="divide-y divide-[var(--line)]">{children}</ul>
    </div>
  );
}

function LedgerRow({ title, meta, amount }: { title: string; meta: string; amount: string }) {
  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3 text-sm">
      <span className="min-w-0">
        <strong className="block truncate">{title}</strong>
        <span className="text-meta">{meta}</span>
      </span>
      <strong className="shrink-0 tabular-nums">{amount}</strong>
    </li>
  );
}

// Tek "Gider ekle" girişi, iki tür: her ay tekrar eden kalem (varsayılan - kullanıcı isteği:
// "her ay girilmesin") ya da tarihli tek seferlik gider.
function CreateExpenseForm({ initialDate, initialMonth, initialCategory, onClose }: { initialDate: string | null; initialMonth: string; initialCategory: ExpenseCategory; onClose: () => void }) {
  const createExpense = useCreateExpense();
  const createRecurring = useCreateRecurringExpense();
  const [kind, setKind] = useState<"recurring" | "oneOff">("recurring");
  const [category, setCategory] = useState<ExpenseCategory>(initialCategory);
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState(0);
  const [note, setNote] = useState("");
  const [fromMonth, setFromMonth] = useState(initialMonth);
  // toISOString UTC verir; gece yarısından sonra İstanbul'da tarih bir gün geride kalırdı.
  const [expenseDate, setExpenseDate] = useState(() => {
    if (initialDate) return initialDate;
    const date = new Date();
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  });
  const [error, setError] = useState<string | null>(null);
  const pending = createExpense.isPending || createRecurring.isPending;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      if (kind === "recurring") {
        await createRecurring.mutateAsync({ category, name: description, monthlyAmount: amount, effectiveFrom: fromMonth, note: note.trim() || undefined });
      } else {
        await createExpense.mutateAsync({ category, description, amount, expenseDate, note: note.trim() || undefined });
      }
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Gider kaydedilemedi.");
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3.5">
      <div className="grid grid-cols-2 rounded-xl border border-[var(--line)] p-1" role="group" aria-label="Gider türü">
        {([["recurring", "Her ay tekrar eden"], ["oneOff", "Tek seferlik"]] as const).map(([value, label]) => (
          <button key={value} type="button" onClick={() => setKind(value)} aria-pressed={kind === value} className={`pressable min-h-10 rounded-lg px-3 text-xs font-bold ${kind === value ? "bg-[var(--brand)] text-white" : "text-[var(--muted)]"}`}>{label}</button>
        ))}
      </div>
      <label className="form-label">Kategori
        <select value={category} onChange={(event) => setCategory(event.target.value as ExpenseCategory)} className="field text-sm">
          {CATEGORIES.map((value) => <option key={value} value={value}>{CATEGORY_LABEL[value]}</option>)}
        </select>
      </label>
      <label className="form-label">{kind === "recurring" ? "Kalem adı" : "Açıklama"}
        <input value={description} onChange={(event) => setDescription(event.target.value)} required maxLength={kind === "recurring" ? 120 : 160} className="field text-sm" placeholder={kind === "recurring" ? "Örn. Kira, Elektrik/su ortalaması" : "Örn. Piyano akordu"} />
      </label>
      <label className="form-label">{kind === "recurring" ? "Aylık tutar (₺)" : "Tutar (₺)"}
        <input type="number" inputMode="decimal" min={0.01} step={0.01} value={amount || ""} onChange={(event) => setAmount(Number(event.target.value))} required className="field text-sm" />
      </label>
      {kind === "recurring"
        ? <div className="form-label">Başlangıç ayı<MonthInput value={fromMonth} onChange={setFromMonth} label="Başlangıç ayı" /></div>
        : <label className="form-label">Tarih<input type="date" value={expenseDate} onChange={(event) => setExpenseDate(event.target.value)} required className="field text-sm" /></label>}
      <label className="form-label">Not <span className="font-normal text-[var(--muted)]">(isteğe bağlı)</span><input value={note} onChange={(event) => setNote(event.target.value)} className="field text-sm" placeholder={kind === "recurring" ? "Örn. son 6 ayın ortalaması" : "Örn. fatura no"} /></label>
      {kind === "recurring" && <p className="text-meta">Bu tutar {monthLabel(fromMonth)} ayından başlayarak her aya sayılır. Değişirse &quot;Tutarı güncelle&quot; ile yeni tutarı girersin; önceki aylar korunur.</p>}
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Gideri kaydet" pending={pending} />
    </form>
  );
}

function CostStat({ label, value, secondary, tone, href }: { label: string; value: string; secondary?: string; tone: "warning" | "success" | "brand" | "danger" | "muted"; href?: string }) {
  const palette = { warning: "text-[var(--warning-strong)]", success: "text-[var(--success-strong)]", brand: "text-[var(--brand-strong)]", danger: "text-[var(--danger-strong)]", muted: "text-[var(--foreground)]" }[tone];
  const body = (
    <>
      <p className="text-meta font-bold">{label}</p>
      <p className={`mt-2 text-xl font-bold tabular-nums ${palette}`}>{value}</p>
      {secondary && <p className="text-meta mt-1">{secondary}</p>}
    </>
  );
  return href
    ? <a href={href} className="app-card pressable block p-4 hover:border-[var(--brand)]">{body}</a>
    : <article className="app-card p-4">{body}</article>;
}
