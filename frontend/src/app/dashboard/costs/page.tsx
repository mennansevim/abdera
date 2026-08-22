"use client";

import { useMemo, useState } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useCreateExpense, useExpenses, useReceivables, type ExpenseCategory } from "@/lib/billing";
import { useVerifyPassword } from "@/lib/use-auth";

export default function CostsPage() {
  const [unlocked, setUnlocked] = useState(false);
  const [password, setPassword] = useState("");
  const verifyPassword = useVerifyPassword();
  const [error, setError] = useState<string | null>(null);

  async function unlock(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await verifyPassword.mutateAsync(password);
      setUnlocked(true);
      setPassword("");
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? "Şifre doğrulanamadı.") : "Şifre doğrulanamadı.");
    }
  }

  return unlocked ? <CostDashboard onLock={() => setUnlocked(false)} /> : (
    <div className="mx-auto max-w-xl pt-8">
      <section className="app-card overflow-hidden">
        <div className="bg-[var(--brand-strong)] p-6 text-white sm:p-8">
          <span className="grid h-12 w-12 place-items-center rounded-2xl bg-white/15"><Icon name="bank" className="h-6 w-6" /></span>
          <p className="mt-5 text-micro text-white/70">Yöneticiye özel alan</p>
          <h1 className="mt-1 font-serif text-2xl font-bold italic">Maliyet Takibi</h1>
          <p className="mt-2 max-w-md text-sm leading-relaxed text-white/75">Aidat ve işletme maliyetlerini görüntülemek için hesabının şifresini bir kez daha doğrula.</p>
        </div>
        <form onSubmit={unlock} className="space-y-4 p-6 sm:p-8">
          <label className="space-y-1.5 text-sm font-semibold text-[var(--muted)]">Hesap şifren<input type="password" value={password} onChange={(event) => setPassword(event.target.value)} required className="field mt-1 text-sm" autoFocus /></label>
          {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-sm font-medium text-[var(--danger-strong)]">{error}</p>}
          <button type="submit" disabled={verifyPassword.isPending} className="pressable min-h-11 w-full rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white disabled:opacity-50">{verifyPassword.isPending ? "Kontrol ediliyor…" : "Maliyet takibini aç"}</button>
        </form>
      </section>
    </div>
  );
}

function CostDashboard({ onLock }: { onLock: () => void }) {
  const { data: receivables, isLoading } = useReceivables();
  const { data: expenses } = useExpenses();
  const expensesTotal = (expenses ?? []).reduce((total, expense) => total + expense.amount, 0);
  const stats = useMemo(() => {
    const rows = receivables ?? [];
    const pending = rows.filter((row) => row.status !== "Paid" && row.status !== "Cancelled");
    return {
      pendingCount: pending.length,
      pendingAmount: pending.reduce((total, row) => total + Math.max(0, row.amount - row.totalPaid), 0),
      collected: rows.reduce((total, row) => total + row.totalPaid, 0),
      income: rows.filter((row) => row.status !== "Cancelled").reduce((total, row) => total + row.totalPaid, 0),
    };
  }, [receivables]);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3"><div><p className="text-micro text-[var(--brand-strong)]">Finans özeti</p><h1 className="text-display mt-1 font-serif italic">Maliyet Takibi</h1><p className="text-meta mt-2">Bekleyen tahsilatlar, toplanan aidatlar ve işletme finansı.</p></div><button type="button" onClick={onLock} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--muted)]">Ekranı kilitle</button></div>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <CostStat label="Bekleyen ödemeler" value={isLoading ? "…" : `${stats.pendingCount} kayıt`} secondary={`₺${stats.pendingAmount.toLocaleString("tr-TR")}`} tone="warning" />
        <CostStat label="Toplanan aidat" value={isLoading ? "…" : `₺${stats.collected.toLocaleString("tr-TR")}`} tone="success" />
        <CostStat label="Gelirler" value={isLoading ? "…" : `₺${stats.income.toLocaleString("tr-TR")}`} tone="brand" />
        <CostStat label="Giderler" value={isLoading ? "…" : `₺${expensesTotal.toLocaleString("tr-TR")}`} secondary={`${expenses?.length ?? 0} kayıt`} tone="muted" />
      </div>

      <section className="app-card p-5 sm:p-6"><div className="flex items-start gap-3"><span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="wallet" className="h-5 w-5" /></span><div><h2 className="text-title">Toplu ödeme ve aidat eşleştirme</h2><p className="text-meta mt-1">10 veya 12 aylık tahsilatları Aidatlar sayfasındaki Toplu ödeme al alanından kaydettiğinde seçilen ayların aidatları otomatik olarak ödendi işaretlenir.</p></div></div><a href="/dashboard/billing" className="pressable mt-4 inline-flex min-h-11 items-center rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white">Aidatlara git</a></section>

      <ExpenseLedger expenses={expenses ?? []} />
    </div>
  );
}

function ExpenseLedger({ expenses }: { expenses: { id: string; category: ExpenseCategory; description: string; amount: number; currency: string; expenseDate: string }[] }) {
  const createExpense = useCreateExpense();
  const [category, setCategory] = useState<ExpenseCategory>("Other");
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState(0);
  const [expenseDate, setExpenseDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const categoryLabels: Record<ExpenseCategory, string> = { Salary: "Maaş ödemeleri", Utilities: "Elektrik / su", Rent: "Kira", Other: "Diğer" };

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);
    try {
      await createExpense.mutateAsync({ category, description, amount, expenseDate });
      setDescription("");
      setAmount(0);
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Gider kaydedilemedi.");
    }
  }

  return (
    <section className="app-card space-y-4 p-5 sm:p-6">
      <div><p className="text-micro text-[var(--muted)]">Gider defteri</p><h2 className="mt-1 text-title">Maaş, fatura ve kira giderleri</h2><p className="text-meta mt-1">Kayıtlar silinmeden tarih ve tutarıyla saklanır.</p></div>
      <form onSubmit={submit} className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
        <select value={category} onChange={(event) => setCategory(event.target.value as ExpenseCategory)} className="field text-sm"><option value="Salary">Maaş ödemeleri</option><option value="Utilities">Elektrik / su</option><option value="Rent">Kira</option><option value="Other">Diğer</option></select>
        <input value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Açıklama" required className="field text-sm" />
        <input type="number" min={0.01} step={0.01} value={amount || ""} onChange={(event) => setAmount(Number(event.target.value))} placeholder="Tutar" required className="field text-sm" />
        <div className="flex gap-2"><input type="date" value={expenseDate} onChange={(event) => setExpenseDate(event.target.value)} required className="field min-w-0 text-sm" /><button type="submit" disabled={createExpense.isPending} className="pressable min-h-11 shrink-0 rounded-xl bg-[var(--brand)] px-3 text-xs font-bold text-white disabled:opacity-50">Ekle</button></div>
      </form>
      {error && <p role="alert" className="text-sm font-medium text-[var(--danger-strong)]">{error}</p>}
      {saved && <p role="status" className="text-sm font-medium text-[var(--success-strong)]">Gider kaydı eklendi.</p>}
      <div className="divide-y divide-[var(--line)] rounded-xl border border-[var(--line)] bg-white">
        {expenses.map((expense) => <div key={expense.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-3 text-sm"><span><strong>{expense.description}</strong><span className="text-meta ml-2">{categoryLabels[expense.category]} · {expense.expenseDate}</span></span><strong>{expense.amount.toLocaleString("tr-TR")} {expense.currency}</strong></div>)}
        {!expenses.length && <p className="text-meta px-3 py-4">Henüz gider kaydı yok.</p>}
      </div>
    </section>
  );
}

function CostStat({ label, value, secondary, tone }: { label: string; value: string; secondary?: string; tone: "warning" | "success" | "brand" | "muted" }) {
  const palette = { warning: "bg-[var(--warning-soft)] text-[var(--warning-strong)]", success: "bg-[var(--success-soft)] text-[var(--success-strong)]", brand: "bg-[var(--brand-soft)] text-[var(--brand-strong)]", muted: "bg-[var(--surface-muted)] text-[var(--muted)]" }[tone];
  return <article className="app-card p-4"><span className={`grid h-9 w-9 place-items-center rounded-xl ${palette}`}><Icon name="wallet" className="h-4 w-4" /></span><p className="text-display mt-3">{value}</p>{secondary && <p className="mt-1 text-sm font-bold text-[var(--muted)]">{secondary}</p>}<p className="text-meta mt-2">{label}</p></article>;
}
