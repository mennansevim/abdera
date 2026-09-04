"use client";

import { useMemo, useState } from "react";
import { Icon } from "@/components/icons";
import { AddButton, AdminGate, FormActions, FormMessage, Modal, PageHeader, SectionHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useCreateExpense, useExpenses, useReceivables, type ExpenseCategory } from "@/lib/billing";
import { useVerifyPassword } from "@/lib/use-auth";

export default function CostsPage() {
  return <AdminGate><CostsPageContent /></AdminGate>;
}

// Maliyet/maaş verisi tamamen Admin'e özel (docs/04-permissions.md) - AdminGate bir
// öğretmenin bu sayfayı doğrudan adresle açmasını (ve kendi şifresiyle aşağıdaki
// "doğrula" kutusuna girip kabuğu görmesini) engeller. Şifre-doğrulama adımı bunun
// YERİNE geçmiyor, ÜSTÜNE ekleniyor: paylaşılan bir bilgisayarda oturum açık kalmış bir
// yönetici için ek bir onay.
function CostsPageContent() {
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
    <div className="mx-auto max-w-md pt-10">
      <section className="app-card p-5 sm:p-6">
        <span className="grid h-11 w-11 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name="bank" className="h-5 w-5" /></span>
        <h1 className="text-display mt-4 font-serif italic">Maliyet takibi</h1>
        <p className="text-meta mt-1">Bu ekran yöneticiye özeldir; açmak için şifreni bir kez daha doğrula.</p>
        <form onSubmit={unlock} className="mt-4 space-y-3.5">
          <label className="form-label">Hesap şifren<input type="password" value={password} onChange={(event) => setPassword(event.target.value)} required className="field text-sm" autoFocus /></label>
          {error && <FormMessage tone="error">{error}</FormMessage>}
          <button type="submit" disabled={verifyPassword.isPending} className="btn btn-primary w-full">{verifyPassword.isPending ? "Kontrol ediliyor…" : "Maliyet takibini aç"}</button>
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
    <div className="space-y-4">
      <PageHeader
        title="Maliyet takibi"
        description="Bekleyen tahsilatlar, toplanan aidatlar ve işletme giderleri."
        actions={<button type="button" onClick={onLock} className="btn btn-quiet">Ekranı kilitle</button>}
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <CostStat label="Bekleyen ödemeler" value={isLoading ? "…" : `${stats.pendingCount} kayıt`} secondary={`₺${stats.pendingAmount.toLocaleString("tr-TR")}`} tone="warning" />
        <CostStat label="Toplanan aidat" value={isLoading ? "…" : `₺${stats.collected.toLocaleString("tr-TR")}`} tone="success" />
        <CostStat label="Gelirler" value={isLoading ? "…" : `₺${stats.income.toLocaleString("tr-TR")}`} tone="brand" />
        <CostStat label="Giderler" value={isLoading ? "…" : `₺${expensesTotal.toLocaleString("tr-TR")}`} secondary={`${expenses?.length ?? 0} kayıt`} tone="muted" />
      </div>

      <section className="app-card flex flex-wrap items-center justify-between gap-3 p-4 sm:p-5">
        <div className="min-w-0">
          <h2 className="text-title">Toplu ödeme ve aidat eşleştirme</h2>
          <p className="text-meta mt-1">10 veya 12 aylık tahsilatı Aidatlar sayfasından kaydettiğinde seçilen ayların aidatları ödendi işaretlenir.</p>
        </div>
        <a href="/dashboard/billing" className="btn btn-primary">Aidatlara git</a>
      </section>

      <ExpenseLedger expenses={expenses ?? []} />
    </div>
  );
}

function ExpenseLedger({ expenses }: { expenses: { id: string; category: ExpenseCategory; description: string; amount: number; currency: string; expenseDate: string }[] }) {
  const [showCreate, setShowCreate] = useState(false);
  const categoryLabels: Record<ExpenseCategory, string> = { Salary: "Maaş ödemeleri", Utilities: "Elektrik / su", Rent: "Kira", Other: "Diğer" };

  return (
    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] p-4 sm:p-5">
        <SectionHeader
          title="Gider defteri"
          description="Maaş, fatura ve kira giderleri - kayıtlar silinmeden tarih ve tutarıyla saklanır."
          actions={<AddButton label="Gider ekle" onClick={() => setShowCreate(true)} />}
        />
      </div>
      <div className="divide-y divide-[var(--line)]">
        {expenses.map((expense) => (
          <div key={expense.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-3 text-sm">
            <span className="min-w-0"><strong>{expense.description}</strong><span className="text-meta ml-2">{categoryLabels[expense.category]} · {expense.expenseDate}</span></span>
            <strong className="tabular-nums">{expense.amount.toLocaleString("tr-TR")} {expense.currency}</strong>
          </div>
        ))}
        {!expenses.length && <p className="text-meta px-4 py-6 text-center">Henüz gider kaydı yok.</p>}
      </div>

      <Modal open={showCreate} title="Gider ekle" onClose={() => setShowCreate(false)} size="sm">
        <CreateExpenseForm onClose={() => setShowCreate(false)} />
      </Modal>
    </section>
  );
}

function CreateExpenseForm({ onClose }: { onClose: () => void }) {
  const createExpense = useCreateExpense();
  const [category, setCategory] = useState<ExpenseCategory>("Other");
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState(0);
  const [expenseDate, setExpenseDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<string | null>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createExpense.mutateAsync({ category, description, amount, expenseDate });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Gider kaydedilemedi.");
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Kategori
          <select value={category} onChange={(event) => setCategory(event.target.value as ExpenseCategory)} className="field text-sm">
            <option value="Salary">Maaş ödemeleri</option>
            <option value="Utilities">Elektrik / su</option>
            <option value="Rent">Kira</option>
            <option value="Other">Diğer</option>
          </select>
        </label>
        <label className="form-label">Tarih<input type="date" value={expenseDate} onChange={(event) => setExpenseDate(event.target.value)} required className="field text-sm" /></label>
      </div>
      <label className="form-label">Açıklama<input value={description} onChange={(event) => setDescription(event.target.value)} required className="field text-sm" placeholder="Örn. Ekim ayı elektrik faturası" /></label>
      <label className="form-label">Tutar (₺)<input type="number" min={0.01} step={0.01} value={amount || ""} onChange={(event) => setAmount(Number(event.target.value))} required className="field text-sm" /></label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Gideri kaydet" pending={createExpense.isPending} />
    </form>
  );
}

function CostStat({ label, value, secondary, tone }: { label: string; value: string; secondary?: string; tone: "warning" | "success" | "brand" | "muted" }) {
  const palette = { warning: "text-[var(--warning-strong)]", success: "text-[var(--success-strong)]", brand: "text-[var(--brand-strong)]", muted: "text-[var(--foreground)]" }[tone];
  return (
    <article className="app-card p-4">
      <p className="text-meta font-bold">{label}</p>
      <p className={`mt-2 text-xl font-bold tabular-nums ${palette}`}>{value}</p>
      {secondary && <p className="text-meta mt-1">{secondary}</p>}
    </article>
  );
}
