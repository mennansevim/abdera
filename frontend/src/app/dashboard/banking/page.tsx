"use client";

import { useState } from "react";
import { PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useGuardians } from "@/lib/people";
import {
  useAssignVirtualIban,
  useBankTransactions,
  useGuardianVirtualIban,
  useResolveBankTransaction,
  type BankTransaction,
  type BankTransactionStatus,
} from "@/lib/banking";

// docs/04-permissions.md: banka/aidat verisi tamamen Admin - app-header.tsx
// ADMIN_ONLY_LINKS'e bak. docs/12-bank-integration.md: otomatik eşleşen işlemler burada
// görünmez (zaten Receivable'a işlendi) - yalnızca NeedsReview/Ignored/tüm liste görünür,
// admin belirsiz kalanları elle çözer.
export default function BankingPage() {
  return (
    <div className="space-y-4">
      <PageHeader title="Banka entegrasyonu" description="Sanal IBAN atamaları ve gelen havalelerin aidatlara işlenmesi." />
      <VirtualIbanSection />
      <TransactionsSection />
    </div>
  );
}

function VirtualIbanSection() {
  const { data: guardians } = useGuardians();
  const [guardianId, setGuardianId] = useState("");
  const { data: virtualIban } = useGuardianVirtualIban(guardianId);
  const assign = useAssignVirtualIban();
  const [error, setError] = useState<string | null>(null);

  async function handleAssign() {
    setError(null);
    try {
      await assign.mutateAsync(guardianId);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Sanal IBAN atanamadı.");
    }
  }

  return (
    <section className="space-y-3">
      <h2 className="text-title">Sanal IBAN ataması</h2>
      <div className="app-card space-y-3 p-4 sm:p-5">
        <p className="text-meta max-w-2xl leading-relaxed">
          Bir veliye sanal IBAN atandığında o IBAN&apos;a gelen havaleler otomatik olarak veliye bağlı aidatlara
          işlenmeye çalışılır. Belirsiz kalan işlemler aşağıdaki listede görünür.
        </p>

        <div className="flex flex-wrap items-end gap-3">
          <select value={guardianId} onChange={(e) => setGuardianId(e.target.value)} className="field min-h-11 w-full max-w-xs text-sm">
            <option value="">Veli seç</option>
            {guardians?.map((g) => (
              <option key={g.id} value={g.id}>{g.firstName} {g.lastName} · {g.phoneNumber}</option>
            ))}
          </select>

          {guardianId && virtualIban && (
            <span className="rounded-xl bg-[var(--success-soft)] px-3 py-2 text-sm font-semibold text-[var(--success-strong)]">
              Atanmış IBAN: {virtualIban.iban} ({virtualIban.provider})
            </span>
          )}
          {guardianId && !virtualIban && (
            <button onClick={handleAssign} disabled={assign.isPending}
              className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)] disabled:opacity-50">
              {assign.isPending ? "Atanıyor…" : "Sanal IBAN ata"}
            </button>
          )}
        </div>
        {error && <p className="text-sm font-medium text-[var(--danger-strong)]">{error}</p>}
      </div>
    </section>
  );
}

const STATUS_LABELS: Record<BankTransactionStatus, string> = {
  Received: "alındı", Matched: "eşleşti", NeedsReview: "gözden geçirilmeli", Ignored: "yok sayıldı",
};
const STATUS_CLASSES: Record<BankTransactionStatus, string> = {
  Received: "bg-[var(--surface-muted)] text-[var(--muted)]",
  Matched: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  NeedsReview: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  Ignored: "bg-[var(--surface-muted)] text-[var(--muted)]",
};

const TRANSACTIONS_PAGE_SIZE = 50;

function TransactionsSection() {
  const [filter, setFilter] = useState<BankTransactionStatus | "all">("NeedsReview");
  const [page, setPage] = useState(1);
  const { data, isLoading } = useBankTransactions(filter === "all" ? undefined : filter, page, TRANSACTIONS_PAGE_SIZE);
  const transactions = data?.items;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  function handleFilterChange(next: BankTransactionStatus | "all") {
    setFilter(next);
    setPage(1); // filtre değişince sayfa sıfırlanır - aksi halde boş bir sayfada kalınabilir.
  }

  return (
    <section className="space-y-3">
      <h2 className="text-title">Gelen işlemler</h2>

      <div className="flex flex-wrap gap-2">
        {(["NeedsReview", "Matched", "Ignored", "all"] as const).map((f) => (
          <button key={f} onClick={() => handleFilterChange(f)}
            className={`pressable min-h-10 rounded-full px-3.5 text-xs font-bold ${
              filter === f ? "bg-[var(--brand)] text-white" : "border-2 border-[var(--line)] bg-white text-[var(--muted)] hover:border-[#e0c39d]"
            }`}>
            {f === "all" ? "Tümü" : STATUS_LABELS[f]}
          </button>
        ))}
      </div>

      {isLoading && <div className="space-y-2">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-14 rounded-xl" />)}</div>}

      <div className="app-card overflow-x-auto">
        <table className="w-full min-w-[46rem] text-sm">
          <thead>
            <tr className="text-micro border-b border-[var(--line)] text-left">
              <th className="px-4 py-3">Tarih</th>
              <th className="px-4 py-3">Gönderen</th>
              <th className="px-4 py-3">Açıklama</th>
              <th className="px-4 py-3">Tutar</th>
              <th className="px-4 py-3">Durum</th>
              <th className="px-4 py-3" />
            </tr>
          </thead>
          <tbody>
            {transactions?.map((t) => (
              <TransactionRow key={t.id} transaction={t} />
            ))}
            {transactions?.length === 0 && !isLoading && (
              <tr>
                <td colSpan={6} className="px-4 py-8 text-center text-sm text-[var(--muted)]">Bu filtrede işlem yok.</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {data && data.totalCount > 0 && (
        <div className="flex items-center justify-between text-sm">
          <span className="text-meta">
            Toplam {data.totalCount} kayıt - sayfa {data.page} / {totalPages}
          </span>
          <div className="flex gap-2">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="pressable min-h-10 rounded-xl border-2 border-[var(--line)] bg-white px-3 text-xs font-bold hover:bg-[var(--surface-muted)] disabled:opacity-50"
            >
              Önceki
            </button>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="pressable min-h-10 rounded-xl border-2 border-[var(--line)] bg-white px-3 text-xs font-bold hover:bg-[var(--surface-muted)] disabled:opacity-50"
            >
              Sonraki
            </button>
          </div>
        </div>
      )}
    </section>
  );
}

function TransactionRow({ transaction }: { transaction: BankTransaction }) {
  const resolve = useResolveBankTransaction();
  const [receivableId, setReceivableId] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleResolve(receivableIdOrNull: string | null) {
    setError(null);
    try {
      await resolve.mutateAsync({ transactionId: transaction.id, receivableId: receivableIdOrNull });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "İşlem çözülemedi.");
    }
  }

  return (
    <tr className="border-b border-[var(--line)] align-top last:border-0">
      <td className="text-meta px-4 py-3.5">{new Date(transaction.receivedAt).toLocaleString("tr-TR")}</td>
      <td className="px-4 py-3.5 font-semibold">{transaction.senderName ?? "—"}</td>
      <td className="text-meta px-4 py-3.5">{transaction.description ?? "—"}</td>
      <td className="px-4 py-3.5 font-bold tabular-nums">{transaction.amount.toLocaleString("tr-TR")} {transaction.currency}</td>
      <td className="px-4 py-3.5">
        <span className={`rounded-full px-2.5 py-1 text-xs font-bold ${STATUS_CLASSES[transaction.status]}`}>{STATUS_LABELS[transaction.status]}</span>
      </td>
      <td className="px-4 py-3.5">
        {transaction.status === "NeedsReview" && (
          <div className="flex flex-wrap items-center gap-1.5">
            <input value={receivableId} onChange={(e) => setReceivableId(e.target.value)}
              placeholder="Aidat ID (Aidatlar sayfasından)"
              className="field min-h-9 w-48 text-xs" />
            <button onClick={() => handleResolve(receivableId)} disabled={!receivableId || resolve.isPending}
              className="pressable min-h-9 rounded-lg bg-[var(--brand)] px-2.5 text-xs font-bold text-white hover:bg-[var(--brand-strong)] disabled:opacity-50">
              Bu aidata say
            </button>
            <button onClick={() => handleResolve(null)} disabled={resolve.isPending}
              className="pressable min-h-9 rounded-lg border-2 border-[var(--line)] px-2.5 text-xs font-bold text-[var(--muted)] hover:bg-[var(--surface-muted)]">
              Hiçbirine sayma
            </button>
          </div>
        )}
        {error && <p className="mt-1 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
      </td>
    </tr>
  );
}
