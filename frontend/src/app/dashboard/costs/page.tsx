"use client";

import { Fragment, Suspense, useMemo, useState } from "react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Icon } from "@/components/icons";
import { MonthInput } from "@/components/month-input";
import { TeacherPayoutForm } from "@/components/teacher-payout-form";
import { AdminGate, FormActions, FormMessage, Modal, PageHeader, Panel, RowMenu, RowMenuItem, SearchInput, SectionHeader, Segmented, StatStrip } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  COURSE_KIND_LABEL, recurringAmountFor, useBillingDues, useChangeRecurringExpenseAmount, useCreateExpense, useCreateRecurringExpense, useEndRecurringExpense,
  useExpenses, useRecurringExpenses,
  type CourseKind, type Expense, type ExpenseCategory, type PaymentMethod, type RecurringExpense,
} from "@/lib/billing";
import { BUCKET_LABEL, collectedPayments, downloadIncomeCsv, fileSlug, IncomeBreakdownModal, splitByBucket, type IncomeEntry } from "./income-breakdown";
import { IncomeLedger } from "./income-ledger";
import { RevenueTargetModal } from "./revenue-target";

export default function CostsPage() {
  // useSearchParams App Router'da bir Suspense sınırı ister.
  return <AdminGate><Suspense><CostDashboard /></Suspense></AdminGate>;
}

// Maliyet/maaş verisi tamamen Admin'e özel (docs/04-permissions.md): AdminGate ekranı, sunucu
// da `/api/expenses` ve aidat uçlarını yalnızca yönetici oturumuna açar. Eskiden bunun üstüne
// bir "şifreni tekrar doğrula" adımı vardı; kullanıcı geri bildirimi üzerine kaldırıldı
// ("admin zaten giriş yapmış") - asıl koruma oturum ve sunucu yetkisi, ek şifre sorusu değil.
//
// Giderin iki türü var (docs/10-decisions.md M9): kira, elektrik/su ortalaması, sabit maaş gibi
// HER AY TEKRAR EDEN kalemler bir kez girilir ve her aya kendiliğinden sayılır; tamir, alet gibi
// TEK SEFERLİK giderler tarihiyle deftere yazılır. Ekrandaki her toplam ikisinin birleşimidir.
//
// Ekran üç sekme: Özet (gelir-gider çubukları + kategori × ay nakit akışı tablosu), Gelirler
// (tahsilat defteri, salt okunur) ve Giderler. Gelirler sekmesinde "ekle" yok: aidatın tek
// tahsilat yolu Aidatlar ekranındaki ödeme penceresi (docs/10-decisions.md H17).

const MONTHS = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];
const MONTHS_SHORT = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];
const WEEKDAYS = ["Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz"];
const CATEGORIES: ExpenseCategory[] = ["Salary", "Rent", "Utilities", "Other"];
const CATEGORY_LABEL: Record<ExpenseCategory, string> = { Salary: "Maaş", Utilities: "Elektrik / su", Rent: "Kira", Other: "Diğer" };
const COURSE_KINDS: CourseKind[] = ["Individual", "Group"];
// İki seri, dataviz doğrulayıcısından geçti (beyaz zemin, CVD ΔE ≥ 15). Sabit = marka turuncusu.
const SERIES = { recurring: "#d9662a", oneOff: "#9b3f6b" };
// Özet çubukları: gelir/gider anlamı taşıdığı için uygulamanın başarı/tehlike tonları; kimlik
// yine lejant ve ipucu metniyle de verilir.
const FLOW = { income: "var(--success)", expense: "var(--danger)" };

type View = "summary" | "income" | "expenses";
type Scope = "month" | "year";
// Seçili dönem: yıllık görünümde `month` yok sayılır. `day` yalnızca aylık takvimde bir güne
// tıklanınca dolar ve defteri o güne daraltır.
interface Period { scope: Scope; year: number; month: number; day: number | null }
type CategoryFilter = ExpenseCategory | "all";

const pad = (value: number) => String(value).padStart(2, "0");
const money = (value: number) => `₺${value.toLocaleString("tr-TR", { maximumFractionDigits: 2 })}`;
const signedMoney = (value: number) => `${value < 0 ? "−" : ""}${money(Math.abs(value))}`;
// Tarihler sunucudan "YYYY-MM-DD", aylar "YYYY-MM" gelir; saat dilimine takılmamak için Date'e
// çevirmeden metin olarak karşılaştırılır.
const monthKey = (year: number, month: number) => `${year}-${pad(month + 1)}`;
const monthLabel = (key: string) => `${MONTHS_SHORT[Number(key.slice(5, 7)) - 1]} ${key.slice(0, 4)}`;
const inPeriod = (date: string, period: Pick<Period, "scope" | "year" | "month">) =>
  period.scope === "year" ? date.startsWith(`${period.year}-`) : date.startsWith(`${monthKey(period.year, period.month)}-`);
const periodMonths = (period: Pick<Period, "scope" | "year" | "month">) =>
  period.scope === "year" ? Array.from({ length: 12 }, (_, month) => monthKey(period.year, month)) : [monthKey(period.year, period.month)];
// Özetin sütunları: yıllıkta yılın 12 ayı, aylıkta seçili ay ve önceki 5 ay - tek ayı tek
// sütunla göstermek tabloyu anlamsızlaştırırdı, kıyas için geriye bakmak gerekir.
const flowMonths = (period: Pick<Period, "scope" | "year" | "month">) =>
  period.scope === "year" ? periodMonths(period) : Array.from({ length: 6 }, (_, index) => {
    const total = period.year * 12 + period.month - 5 + index;
    return monthKey(Math.floor(total / 12), total % 12);
  });

function shift(period: Period, step: -1 | 1): Period {
  if (period.scope === "year") return { ...period, year: period.year + step, day: null };
  const index = period.year * 12 + period.month + step;
  return { ...period, year: Math.floor(index / 12), month: index % 12, day: null };
}

function periodLabel(period: Pick<Period, "scope" | "year" | "month">) {
  return period.scope === "year" ? String(period.year) : `${MONTHS[period.month]} ${period.year}`;
}

interface MonthTotal { key: string; recurring: number; oneOff: number; planned: boolean }

// Aylık toplam = o aya düşen sabit kalemler + o ayın tek seferlik giderleri. Bugünden sonraki
// aylar "planlanan"dır: sabit kalemler oraya da sayılır ama henüz ödenmiş değildir.
function expenseTotals(recurring: RecurringExpense[], oneOffs: Expense[], months: string[], currentMonth: string): MonthTotal[] {
  return months.map((key) => ({
    key,
    recurring: recurring.reduce((sum, item) => sum + recurringAmountFor(item, key), 0),
    oneOff: oneOffs.filter((expense) => expense.expenseDate.startsWith(`${key}-`)).reduce((sum, expense) => sum + expense.amount, 0),
    planned: key > currentMonth,
  }));
}

const sumTotals = (rows: MonthTotal[]) => rows.reduce((total, row) => total + row.recurring + row.oneOff, 0);

// Nakit akışı tablosunun bir sütunu: gelir ders türüne, gider kategoriye göre kırılır.
interface FlowColumn { key: string; planned: boolean; income: Record<CourseKind, number>; expense: Record<ExpenseCategory, number> }

function CostDashboard() {
  const today = useMemo(() => new Date(), []);
  const currentMonth = monthKey(today.getFullYear(), today.getMonth());
  const [period, setPeriod] = useState<Period>({ scope: "month", year: today.getFullYear(), month: today.getMonth(), day: null });
  const [category, setCategory] = useState<CategoryFilter>("all");
  const [search, setSearch] = useState("");
  const [incomeSearch, setIncomeSearch] = useState("");
  // Öğretmenler listesindeki "Ödeme yap" buraya `?odeme=<öğretmen>&hafta=<gün>` ile gelir:
  // Giderler sekmesi açılır, "Gider ekle" formu Haftalık sekmesi, öğretmen ve hafta seçili açılır.
  const searchParams = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const payTeacherId = searchParams.get("odeme");
  const payWeek = searchParams.get("hafta");
  const [view, setView] = useState<View>(payTeacherId !== null ? "expenses" : "summary");
  const [showCreate, setShowCreate] = useState(payTeacherId !== null);
  const [breakdown, setBreakdown] = useState<{ title: string; entries: IncomeEntry[] } | null>(null);
  const [showTarget, setShowTarget] = useState(false);
  function closeCreate() {
    setShowCreate(false);
    if (payTeacherId !== null) router.replace(pathname, { scroll: false });
  }
  const { data: expenses, isLoading: expensesLoading } = useExpenses();
  const { data: recurringData, isLoading: recurringLoading } = useRecurringExpenses();
  const { data: dues, isLoading: duesLoading } = useBillingDues();

  const allExpenses = useMemo(() => expenses ?? [], [expenses]);
  const allRecurring = useMemo(() => recurringData ?? [], [recurringData]);
  const payments = useMemo(() => collectedPayments(dues ?? []), [dues]);

  // Kategori filtresi yalnızca Giderler sekmesinde: özet kartlarını, grafikleri ve defteri
  // birlikte daraltır - "bu yıl kiraya ne verdik" tek tıkla yanıtlanır. Kategori dağılımı kartı
  // ve Özet sekmesi ise hep tüm kategorileri gösterir.
  const oneOffs = useMemo(() => category === "all" ? allExpenses : allExpenses.filter((expense) => expense.category === category), [allExpenses, category]);
  const recurring = useMemo(() => category === "all" ? allRecurring : allRecurring.filter((item) => item.category === category), [allRecurring, category]);

  const months = useMemo(() => expenseTotals(recurring, oneOffs, periodMonths(period), currentMonth), [recurring, oneOffs, period, currentMonth]);
  const stats = useMemo(() => {
    const total = sumTotals(months);
    const recurringTotal = months.reduce((sum, row) => sum + row.recurring, 0);
    const previousTotal = sumTotals(expenseTotals(recurring, oneOffs, periodMonths(shift(period, -1)), currentMonth));
    return { total, recurringTotal, oneOffTotal: total - recurringTotal, previousTotal };
  }, [months, recurring, oneOffs, period, currentMonth]);

  // Özet: kategori filtresinden bağımsız, tüm giderler.
  const summary = useMemo(() => {
    const rows = expenseTotals(allRecurring, allExpenses, periodMonths(period), currentMonth);
    const expenseTotal = sumTotals(rows);
    // Net, gelecek ayların planlanan giderini değil bugüne kadarkini düşer - tahsilat da yalnızca
    // gerçekleşmiş parayı sayıyor.
    const toDate = sumTotals(rows.filter((row) => !row.planned));
    const income = payments.filter((payment) => inPeriod(payment.date, period)).reduce((total, payment) => total + payment.amount, 0);
    const pending = (dues ?? [])
      .filter((row) => row.status !== "Paid" && row.status !== "Cancelled")
      .reduce((total, row) => total + Math.max(0, row.amount - row.totalPaid), 0);
    const columns: FlowColumn[] = flowMonths(period).map((key) => ({
      key,
      planned: key > currentMonth,
      income: Object.fromEntries(COURSE_KINDS.map((kind) => [kind,
        payments.filter((payment) => payment.courseKind === kind && payment.date.startsWith(`${key}-`)).reduce((sum, payment) => sum + payment.amount, 0)])) as Record<CourseKind, number>,
      expense: Object.fromEntries(CATEGORIES.map((value) => [value,
        allRecurring.filter((item) => item.category === value).reduce((sum, item) => sum + recurringAmountFor(item, key), 0)
        + allExpenses.filter((expense) => expense.category === value && expense.expenseDate.startsWith(`${key}-`)).reduce((sum, expense) => sum + expense.amount, 0)])) as Record<ExpenseCategory, number>,
    }));
    return { income, expenseTotal, toDate, net: income - toDate, pending, columns };
  }, [allRecurring, allExpenses, payments, dues, period, currentMonth]);

  // Planlayıcının "aylık gider" tabanı: bu ayın sabit kalemleri + son üç tamamlanmış ayın tek
  // seferlik ortalaması (öğretmen haftalık ödemeleri de gider defterine bu yolla düşer).
  const monthlyExpense = useMemo(() => {
    const lastThree = [1, 2, 3].map((back) => {
      const total = today.getFullYear() * 12 + today.getMonth() - back;
      return monthKey(Math.floor(total / 12), total % 12);
    });
    const oneOffAverage = allExpenses.filter((expense) => lastThree.includes(expense.expenseDate.slice(0, 7))).reduce((sum, expense) => sum + expense.amount, 0) / 3;
    return allRecurring.reduce((sum, item) => sum + recurringAmountFor(item, currentMonth), 0) + oneOffAverage;
  }, [allExpenses, allRecurring, currentMonth, today]);

  const needle = search.trim().toLocaleLowerCase("tr-TR");
  const matches = (text: string | null) => !needle || (text ?? "").toLocaleLowerCase("tr-TR").includes(needle);
  const dayKey = period.scope === "month" && period.day !== null ? `${monthKey(period.year, period.month)}-${pad(period.day)}` : null;
  const listedOneOffs = oneOffs.filter((expense) =>
    inPeriod(expense.expenseDate, period) && (!dayKey || expense.expenseDate === dayKey) && (matches(expense.description) || matches(expense.note)));
  const listedRecurring = dayKey ? [] : recurring
    .map((item) => ({ item, amount: periodMonths(period).reduce((sum, key) => sum + recurringAmountFor(item, key), 0) }))
    .filter(({ item, amount }) => amount > 0 && (matches(item.name) || matches(item.note)));

  const incomeNeedle = incomeSearch.trim().toLocaleLowerCase("tr-TR");
  const periodIncome = payments.filter((payment) => inPeriod(payment.date, period));
  const listedIncome = periodIncome.filter((payment) => !incomeNeedle
    || `${payment.studentName} ${payment.instrumentName}`.toLocaleLowerCase("tr-TR").includes(incomeNeedle));
  const incomeSplit = splitByBucket(periodIncome);
  const openBreakdown = (title: string, entries: IncomeEntry[]) => setBreakdown({ title, entries });
  const incomeBy = (methods: PaymentMethod[]) => periodIncome.filter((payment) => methods.includes(payment.method)).reduce((sum, payment) => sum + payment.amount, 0);

  const isCurrent = period.year === today.getFullYear() && (period.scope === "year" || period.month === today.getMonth());
  const loading = expensesLoading || recurringLoading || duesLoading;
  const change = stats.previousTotal > 0 ? ((stats.total - stats.previousTotal) / stats.previousTotal) * 100 : null;
  const futurePeriod = months.every((row) => row.planned);
  const newExpenseDate = dayKey ?? (isCurrent ? `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}` : null);

  // Özetteki bir hücreye dokununca ilgili sekme o ay (ve gider satırıysa o kategori) ile açılır.
  function drill(target: "income" | "expenses", key: string, filter?: ExpenseCategory) {
    setPeriod({ scope: "month", year: Number(key.slice(0, 4)), month: Number(key.slice(5, 7)) - 1, day: null });
    if (target === "expenses") setCategory(filter ?? "all");
    setView(target);
  }

  return (
    <div className="space-y-3">
      <PageHeader
        title="Gelir ve gider"
        description="Tahsil edilen aidat ve okulun giderleri tek yerde. Sabit giderler bir kez girilir, her aya kendiliğinden sayılır."
        actions={
          <>
            <Segmented
              label="Gelir-gider görünümü"
              options={[
                { value: "summary", label: "Özet", icon: "activity" },
                { value: "income", label: "Gelirler", icon: "wallet" },
                { value: "expenses", label: "Giderler", icon: "bank" },
              ]}
              value={view}
              onChange={(value) => { setView(value); setPeriod((current) => ({ ...current, day: null })); }}
            />
            {view === "income"
              ? <Link href="/dashboard/billing" className="btn btn-quiet">Aidatlar ekranına git<Icon name="arrow-right" className="h-4 w-4" /></Link>
              : <button type="button" onClick={() => setShowCreate(true)} className="btn btn-primary"><Icon name="plus" className="h-4 w-4" />Gider ekle</button>}
            {view === "summary" && <button type="button" onClick={() => setShowTarget(true)} className="btn btn-quiet" title="Aylık gelir hedefi ve yol haritası (deneysel)"><Icon name="target" className="h-4 w-4" />Hedef belirle</button>}
          </>
        }
      />

      <section className="app-card flex flex-wrap items-center gap-2 p-3" aria-label={view === "expenses" ? "Dönem ve kategori filtreleri" : "Dönem filtresi"}>
        <Segmented
          label="Görünüm"
          options={[{ value: "month", label: "Aylık" }, { value: "year", label: "Yıllık" }]}
          value={period.scope}
          onChange={(value) => setPeriod((current) => ({ ...current, scope: value, day: null }))}
        />
        <div className="flex items-center gap-1">
          <button type="button" onClick={() => setPeriod((current) => shift(current, -1))} className="icon-btn icon-btn-quiet" aria-label={period.scope === "year" ? "Önceki yıl" : "Önceki ay"}><Icon name="arrow-left" className="h-4 w-4" /></button>
          <span aria-live="polite" className="min-w-[7.5rem] text-center text-xs font-bold tabular-nums">{periodLabel(period)}</span>
          <button type="button" onClick={() => setPeriod((current) => shift(current, 1))} className="icon-btn icon-btn-quiet" aria-label={period.scope === "year" ? "Sonraki yıl" : "Sonraki ay"}><Icon name="arrow-right" className="h-4 w-4" /></button>
        </div>
        <button type="button" disabled={isCurrent} onClick={() => setPeriod((current) => ({ ...current, year: today.getFullYear(), month: today.getMonth(), day: null }))} className="btn btn-quiet px-3 text-[.75rem] font-semibold disabled:cursor-default disabled:opacity-50">{period.scope === "year" ? "Bu yıl" : "Bu ay"}</button>
        {view === "expenses" && (
          <Segmented
            label="Kategoriye göre filtrele"
            className="md:ml-auto"
            options={(["all", ...CATEGORIES] as const).map((value) => ({ value, label: value === "all" ? "Tümü" : CATEGORY_LABEL[value] }))}
            value={category}
            onChange={setCategory}
          />
        )}
      </section>

      {view === "summary" && (
        <>
          <StatStrip
            label="Dönem özeti"
            items={[
              { key: "income", label: "Gelir", value: money(summary.income), loading, hint: "Tahsil edilen aidat · dökümü için dokun", tone: "success", onClick: () => openBreakdown(`${periodLabel(period)} geliri`, periodIncome) },
              { key: "expense", label: `Gider${summary.toDate === 0 && summary.expenseTotal > 0 ? " (planlanan)" : ""}`, value: money(summary.expenseTotal), loading, hint: "Sabit + tek seferlik", tone: "danger", onClick: () => { setCategory("all"); setView("expenses"); } },
              {
                key: "net",
                label: "Net sonuç",
                value: signedMoney(summary.net),
                loading,
                hint: summary.toDate !== summary.expenseTotal ? `Bugüne kadarki ${money(summary.toDate)} gider düşüldü` : summary.net < 0 ? "Gider geliri aşıyor" : "Gelir gideri karşılıyor",
                tone: summary.net < 0 ? "danger" : "brand",
              },
              { key: "pending", label: "Bekleyen aidat", value: money(summary.pending), loading, hint: "Tüm dönemler · Aidatlar ekranından tahsil edilir", tone: "warning", href: "/dashboard/billing" },
            ]}
            footer={!loading && summary.income > 0 && incomeSplit.current !== summary.income ? <IncomeSplitNote split={incomeSplit} period={period} onOpen={() => openBreakdown(`${periodLabel(period)} geliri`, periodIncome)} /> : undefined}
          />
          <Panel title="Gelir ve gider" meta={period.scope === "year" ? String(period.year) : `Son 6 ay · ${periodLabel(period)} dahil`}>
            <FlowBars columns={summary.columns} selected={period.scope === "month" ? monthKey(period.year, period.month) : null} onSelect={(key) => drill("income", key)} loading={loading} />
          </Panel>
          <Panel flush title="Nakit akışı" meta="Gelir hücresine dokun, döküm açılsın; gider hücresi o ayın kayıtlarına iner">
            <CashFlowTable
              columns={summary.columns}
              showTotal={period.scope === "year"}
              selected={period.scope === "month" ? monthKey(period.year, period.month) : null}
              onDrill={drill}
              onIncome={(key, kind) => openBreakdown(
                `${kind ? `${COURSE_KIND_LABEL[kind]} aidat · ` : ""}${monthLabel(key)}`,
                payments.filter((payment) => payment.date.startsWith(`${key}-`) && (!kind || payment.courseKind === kind)))}
              loading={loading}
            />
          </Panel>
        </>
      )}

      {view === "income" && (
        <>
          <StatStrip
            label="Tahsilat özeti"
            items={[
              { key: "total", label: "Tahsil edilen", value: money(summary.income), loading, hint: `${periodIncome.length} ödeme · dökümü için dokun`, tone: "success", onClick: () => openBreakdown(`${periodLabel(period)} geliri`, periodIncome) },
              { key: "cash", label: "Nakit", value: money(incomeBy(["Cash"])), loading },
              { key: "transfer", label: "Havale", value: money(incomeBy(["Transfer"])), loading, hint: "Sanal IBAN eşleşmeleri dahil" },
              { key: "card", label: "Kart / diğer", value: money(incomeBy(["Card", "Other"])), loading },
            ]}
          />
          <Panel
            flush
            title="Tahsilat defteri"
            meta={`${periodLabel(period)} · ${listedIncome.length} ödeme`}
            actions={
              <>
                <SearchInput value={incomeSearch} onChange={setIncomeSearch} label="Tahsilatlarda ara" placeholder="Öğrenci veya enstrüman" />
                <button type="button" disabled={!listedIncome.length} onClick={() => downloadIncomeCsv(listedIncome, `gelir-dokumu-${fileSlug(periodLabel(period))}.csv`)} className="btn btn-quiet" title="Listelenen tahsilatları Excel/CSV olarak indir"><Icon name="download" className="h-4 w-4" />İndir</button>
              </>
            }
          >
            <IncomeLedger entries={listedIncome} scope={period.scope} loading={loading} search={incomeSearch} />
          </Panel>
        </>
      )}

      {view === "expenses" && (
        <>
          <StatStrip
            label="Gider özeti"
            items={[
              {
                key: "total",
                label: `${category === "all" ? "Toplam gider" : `${CATEGORY_LABEL[category]} gideri`}${futurePeriod ? " (planlanan)" : ""}`,
                value: money(stats.total),
                loading,
                hint: change !== null ? `önceki ${period.scope === "year" ? "yıla" : "aya"} göre ${change > 0 ? "▲" : change < 0 ? "▼" : ""} %${Math.abs(change).toLocaleString("tr-TR", { maximumFractionDigits: 0 })}` : undefined,
                tone: "danger",
              },
              { key: "recurring", label: "Sabit", value: money(stats.recurringTotal), loading, hint: "Her aya kendiliğinden sayılır" },
              { key: "oneOff", label: "Tek seferlik", value: money(stats.oneOffTotal), loading, hint: "Tarihiyle girilen giderler" },
            ]}
          />

          <div className="grid gap-3 xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
            <section className="app-card p-4">
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

          <Panel
            flush
            title="Gider defteri"
            meta={dayKey ? `${period.day} ${MONTHS[period.month]} ${period.year}` : `${periodLabel(period)} · kayıtlar silinmez`}
            actions={<SearchInput value={search} onChange={setSearch} label="Giderlerde ara" placeholder="Açıklamada ara" />}
          >
            <ExpenseLedger oneOffs={listedOneOffs} recurring={listedRecurring} scope={period.scope} loading={loading} />
          </Panel>
        </>
      )}

      <IncomeBreakdownModal open={breakdown !== null} title={breakdown?.title ?? ""} entries={breakdown?.entries ?? []} onClose={() => setBreakdown(null)} />
      <RevenueTargetModal open={showTarget} onClose={() => setShowTarget(false)} currentMonth={currentMonth} monthlyExpense={monthlyExpense} />

      <Modal open={showCreate} title="Gider ekle" onClose={closeCreate} size="sm">
        <CreateExpenseForm
          initialKind={payTeacherId !== null ? "weekly" : "recurring"}
          initialTeacherId={payTeacherId ?? undefined}
          initialWeek={payWeek ?? undefined}
          initialDate={newExpenseDate}
          initialMonth={isCurrent || period.scope === "year" ? currentMonth : monthKey(period.year, period.month)}
          initialCategory={category === "all" ? "Rent" : category}
          onClose={closeCreate}
        />
      </Modal>
    </div>
  );
}

// Gelir kartının altındaki açıklama: rakam ödeme tarihine göre sayıldığı için bu dönemde
// alınan peşin ve gecikmiş ödemeleri de içerir; Aidatlar ekranının "X/Y ödedi" sayacı ise
// yalnızca dönemin kendi aidatına bakar. Fark burada adıyla gösterilir.
function IncomeSplitNote({ split, period, onOpen }: { split: Record<"current" | "late" | "advance", number>; period: Period; onOpen: () => void }) {
  const parts = [
    { label: period.scope === "month" ? `${MONTHS[period.month]} aidatı` : BUCKET_LABEL.current, value: split.current },
    { label: BUCKET_LABEL.advance.toLocaleLowerCase("tr-TR"), value: split.advance },
    { label: BUCKET_LABEL.late.toLocaleLowerCase("tr-TR"), value: split.late },
  ].filter((part) => part.value > 0);
  return (
    <button type="button" onClick={onOpen} className="pressable flex w-full flex-wrap items-center gap-x-1.5 gap-y-0.5 border-t border-[var(--line)] px-4 py-2 text-left text-[.75rem] hover:bg-[var(--surface-muted)]">
      <span className="font-bold">Gelirin içinde:</span>
      {parts.map((part, index) => <span key={part.label} className="tabular-nums">{index > 0 && "· "}{money(part.value)} {part.label}</span>)}
      <span className="ml-auto inline-flex items-center gap-1 font-bold text-[var(--brand-strong)]">Döküm<Icon name="download" className="h-3.5 w-3.5" /></span>
    </button>
  );
}

// Ay başına yan yana iki çubuk: gelir ve gider. Gelecek aylar soluk - yalnızca sabit giderlerin
// planlanan tutarını taşır, gelirleri henüz yok. Bir aya dokunmak o ayın tahsilatlarını açar.
function FlowBars({ columns, selected, onSelect, loading }: { columns: FlowColumn[]; selected: string | null; onSelect: (key: string) => void; loading: boolean }) {
  const totals = columns.map((column) => ({
    income: COURSE_KINDS.reduce((sum, kind) => sum + column.income[kind], 0),
    expense: CATEGORIES.reduce((sum, value) => sum + column.expense[value], 0),
  }));
  const max = Math.max(0, ...totals.flatMap((row) => [row.income, row.expense]));
  const height = (value: number) => (max > 0 ? Math.max(value > 0 ? 2 : 0, (value / max) * 88) : 0);

  if (loading) return <div className="skeleton h-48 w-full rounded-xl" />;
  return (
    <div>
      <ul className="flex flex-wrap justify-end gap-3 text-[.75rem] font-semibold text-[var(--muted)]" aria-label="Lejant">
        <li className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-[3px]" style={{ background: FLOW.income }} />Gelir</li>
        <li className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-[3px]" style={{ background: FLOW.expense }} />Gider</li>
      </ul>
      <div className="mt-2 flex h-44 items-end gap-1 border-b border-[var(--line)] sm:gap-2" role="list" aria-label="Aylara göre gelir ve gider">
        {columns.map((column, index) => {
          const { income, expense } = totals[index];
          const label = `${monthLabel(column.key)}${column.planned ? " (planlanan)" : ""}: gelir ${money(income)}, gider ${money(expense)}, net ${signedMoney(income - expense)}`;
          return (
            <button
              key={column.key}
              type="button"
              role="listitem"
              onClick={() => onSelect(column.key)}
              title={label}
              aria-label={label}
              className={`group flex h-full min-w-0 flex-1 items-end justify-center gap-[2px] rounded-t-md px-0.5 ${column.key === selected ? "bg-[var(--brand-soft)]" : "hover:bg-[var(--surface-muted)]"} ${column.planned ? "opacity-40" : ""}`}
            >
              <span className="w-full max-w-6 rounded-t-[4px]" style={{ height: `${height(income)}%`, background: FLOW.income }} />
              <span className="w-full max-w-6 rounded-t-[4px]" style={{ height: `${height(expense)}%`, background: FLOW.expense }} />
            </button>
          );
        })}
      </div>
      <div className="mt-1 flex gap-1 sm:gap-2" aria-hidden="true">
        {columns.map((column) => <span key={column.key} className={`min-w-0 flex-1 text-center text-[.625rem] font-bold ${column.key === selected ? "text-[var(--brand-strong)]" : "text-[var(--muted)]"}`}>{MONTHS_SHORT[Number(column.key.slice(5, 7)) - 1]}</span>)}
      </div>
    </div>
  );
}

// Muhasebe defteri görünümü: satır = ders türü / gider kategorisi, sütun = ay. Toplam ve net
// satırları türetilmiştir; kategori hücreleri ilgili sekmeye o ay ve kategoriyle iner.
function CashFlowTable({ columns, showTotal, selected, onDrill, onIncome, loading }: {
  columns: FlowColumn[]; showTotal: boolean; selected: string | null; loading: boolean;
  onDrill: (target: "income" | "expenses", key: string, filter?: ExpenseCategory) => void;
  onIncome: (key: string, kind: CourseKind | null) => void;
}) {
  if (loading) return <p className="text-meta px-4 py-6 text-center">Yükleniyor…</p>;
  const incomeOf = (column: FlowColumn) => COURSE_KINDS.reduce((sum, kind) => sum + column.income[kind], 0);
  const expenseOf = (column: FlowColumn) => CATEGORIES.reduce((sum, value) => sum + column.expense[value], 0);
  const total = (pick: (column: FlowColumn) => number) => columns.reduce((sum, column) => sum + pick(column), 0);
  const amount = (value: number) => value === 0 ? "–" : value.toLocaleString("tr-TR", { maximumFractionDigits: 0 });
  const cellTone = (column: FlowColumn) => `${column.key === selected ? "bg-[var(--brand-soft)]" : ""} ${column.planned ? "text-[var(--muted)] italic" : ""}`;
  const sticky = "sticky left-0 z-[1] bg-[var(--surface)] px-4 text-left";

  const dataRow = (label: string, pick: (column: FlowColumn) => number, onCell: (key: string) => void, tone: string) => (
    <tr className="border-t border-[var(--line)]">
      <th scope="row" className={`${sticky} py-1.5 font-semibold`}><span className={`mr-1.5 inline-block h-2 w-2 rounded-full ${tone}`} aria-hidden="true" />{label}</th>
      {columns.map((column) => (
        <td key={column.key} className={`p-0 text-right ${cellTone(column)}`}>
          <button type="button" onClick={() => onCell(column.key)} className="pressable w-full px-2 py-1.5 text-right tabular-nums hover:bg-[var(--surface-muted)]" aria-label={`${label}, ${monthLabel(column.key)}: ${money(pick(column))}`}>{amount(pick(column))}</button>
        </td>
      ))}
      {showTotal && <td className="px-3 py-1.5 text-right font-semibold tabular-nums">{amount(total(pick))}</td>}
    </tr>
  );
  const totalRow = (label: string, pick: (column: FlowColumn) => number, className = "", onCell?: (key: string) => void) => (
    <tr className={`border-t border-[var(--line)] bg-[var(--surface-muted)] font-bold ${className}`}>
      <th scope="row" className={`${sticky} bg-[var(--surface-muted)] py-2`}>{label}</th>
      {columns.map((column) => onCell
        ? <td key={column.key} className={`p-0 text-right ${column.planned ? "text-[var(--muted)] italic" : ""}`}><button type="button" onClick={() => onCell(column.key)} className="pressable w-full px-2 py-2 text-right tabular-nums hover:bg-[var(--surface)]" aria-label={`${label}, ${monthLabel(column.key)}: ${money(pick(column))} - döküm`}>{amount(pick(column))}</button></td>
        : <td key={column.key} className={`px-2 py-2 text-right tabular-nums ${column.planned ? "text-[var(--muted)] italic" : ""}`}>{pick(column) < 0 ? "−" : ""}{amount(Math.abs(pick(column)))}</td>)}
      {showTotal && <td className="px-3 py-2 text-right tabular-nums">{total(pick) < 0 ? "−" : ""}{amount(Math.abs(total(pick)))}</td>}
    </tr>
  );
  const net = (column: FlowColumn) => incomeOf(column) - expenseOf(column);

  return (
    <div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[34rem] text-[.8125rem]">
          <thead>
            <tr className="text-[.6875rem] font-bold text-[var(--muted)]">
              <th scope="col" className={`${sticky} w-40 py-2 font-bold`}>₺</th>
              {columns.map((column) => (
                <th key={column.key} scope="col" className={`px-2 py-2 text-right font-bold ${column.key === selected ? "bg-[var(--brand-soft)] text-[var(--brand-strong)]" : ""}`}>
                  {MONTHS_SHORT[Number(column.key.slice(5, 7)) - 1]}{column.planned && <span className="font-normal"> · plan</span>}
                </th>
              ))}
              {showTotal && <th scope="col" className="px-3 py-2 text-right font-bold">Yıl</th>}
            </tr>
          </thead>
          <tbody>
            {COURSE_KINDS.map((kind) => (
              <Fragment key={kind}>{dataRow(`${COURSE_KIND_LABEL[kind]} aidat`, (column) => column.income[kind], (key) => onIncome(key, kind), "bg-[var(--success)]")}</Fragment>
            ))}
            {totalRow("Toplam gelir", incomeOf, "text-[var(--success-strong)]", (key) => onIncome(key, null))}
            {CATEGORIES.map((value) => (
              <Fragment key={value}>{dataRow(CATEGORY_LABEL[value], (column) => column.expense[value], (key) => onDrill("expenses", key, value), "bg-[var(--danger)]")}</Fragment>
            ))}
            {totalRow("Toplam gider", expenseOf, "text-[var(--danger-strong)]")}
            {totalRow("Net", net)}
          </tbody>
        </table>
      </div>
      <p className="text-meta border-t border-[var(--line)] px-4 py-2">Gelir ödeme tarihine göre sayılır. &quot;plan&quot; sütunları gelecek aylar: yalnızca sabit giderler var, henüz ödenmedi.</p>
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
              className={`pressable relative flex min-h-[3rem] min-w-0 flex-col items-start justify-between rounded-lg border p-1.5 text-left sm:min-h-[3.25rem] ${selected ? "border-[var(--brand)] ring-2 ring-[var(--brand)]" : "border-[var(--line)]"}`}
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
    <section className="app-card p-4">
      <SectionHeader title="Kategori dağılımı" description={total > 0 ? `Dönem toplamı ${money(total)} · sabit + tek seferlik` : "Bu dönemde gider yok."} />
      <ul className="mt-2 space-y-0.5">
        {sorted.map(({ category, amount }) => {
          const share = total > 0 ? (amount / total) * 100 : 0;
          const selected = active === category;
          return (
            <li key={category}>
              <button type="button" onClick={() => onSelect(selected ? "all" : category)} aria-pressed={selected} className={`pressable w-full rounded-xl border px-2.5 py-1.5 text-left ${selected ? "border-[var(--brand)] bg-[var(--brand-soft)]" : "border-transparent hover:border-[var(--line)]"}`}>
                <span className="flex items-baseline justify-between gap-2 text-sm">
                  <strong>{CATEGORY_LABEL[category]}</strong>
                  <span className="tabular-nums"><strong>{money(amount)}</strong><span className="text-meta ml-1.5">%{share.toLocaleString("tr-TR", { maximumFractionDigits: 0 })}</span></span>
                </span>
                <span className="mt-1 block h-1.5 overflow-hidden rounded-full bg-[var(--surface-muted)]">
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
      <div className="border-b border-[var(--line)] px-4 py-2.5">
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
              <li key={item.id} className={`px-4 py-2 ${item.isEnded ? "opacity-60" : ""}`}>
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

function LedgerRow({ title, meta, amount, tone }: { title: string; meta: string; amount: string; tone?: "success" }) {
  return (
    <li className="flex items-center justify-between gap-3 px-4 py-2 text-sm">
      <span className="min-w-0">
        <strong className="block truncate">{title}</strong>
        <span className="text-meta">{meta}</span>
      </span>
      <strong className={`shrink-0 tabular-nums ${tone === "success" ? "text-[var(--success-strong)]" : ""}`}>{amount}</strong>
    </li>
  );
}

// Tek "Gider ekle" girişi, üç tür: her ay tekrar eden kalem (varsayılan - kullanıcı isteği:
// "her ay girilmesin"), tarihli tek seferlik gider ya da öğretmenin haftalık ders ödemesi
// (docs/10-decisions.md O1 - tutarı tamamlanan dersten sunucu hesaplar, ayrı form bileşeni).
type ExpenseKind = "recurring" | "oneOff" | "weekly";
const KIND_LABEL: [ExpenseKind, string][] = [["recurring", "Her ay tekrar eden"], ["oneOff", "Tek seferlik"], ["weekly", "Haftalık"]];

function CreateExpenseForm({ initialKind, initialTeacherId, initialWeek, initialDate, initialMonth, initialCategory, onClose }: { initialKind: ExpenseKind; initialTeacherId?: string; initialWeek?: string; initialDate: string | null; initialMonth: string; initialCategory: ExpenseCategory; onClose: () => void }) {
  const [kind, setKind] = useState<ExpenseKind>(initialKind);
  const tabs = (
    <div className="grid grid-cols-3 rounded-xl border border-[var(--line)] p-1" role="group" aria-label="Gider türü">
      {KIND_LABEL.map(([value, label]) => (
        <button key={value} type="button" onClick={() => setKind(value)} aria-pressed={kind === value} className={`pressable min-h-10 rounded-lg px-2 text-xs font-bold ${kind === value ? "bg-[var(--brand)] text-white" : "text-[var(--muted)]"}`}>{label}</button>
      ))}
    </div>
  );
  if (kind === "weekly") {
    return <div className="space-y-3.5">{tabs}<TeacherPayoutForm initialTeacherId={initialTeacherId} initialWeek={initialWeek} onClose={onClose} /></div>;
  }
  return <SimpleExpenseForm kind={kind} tabs={tabs} initialDate={initialDate} initialMonth={initialMonth} initialCategory={initialCategory} onClose={onClose} />;
}

function SimpleExpenseForm({ kind, tabs, initialDate, initialMonth, initialCategory, onClose }: { kind: "recurring" | "oneOff"; tabs: React.ReactNode; initialDate: string | null; initialMonth: string; initialCategory: ExpenseCategory; onClose: () => void }) {
  const createExpense = useCreateExpense();
  const createRecurring = useCreateRecurringExpense();
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
      {tabs}
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
