"use client";

import { useState } from "react";
import { AdminGate, EmptyState, FormActions, FormMessage, Modal, PageHeader, Pager, Panel, Segmented } from "@/components/ui";
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
  return <AdminGate><BankingPageContent /></AdminGate>;
}

// Banka/havale eşleştirmesi tamamen Admin'e özel (docs/04-permissions.md).
function BankingPageContent() {
  return (
    <div className="space-y-3">
      <PageHeader title="Banka entegrasyonu" description="Sanal IBAN atamaları ve gelen havalelerin aidatlara işlenmesi." />
      <div className="grid items-start gap-3 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <TransactionsSection />
        <VirtualIbanSection />
      </div>
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
    <Panel title="Sanal IBAN ataması" className="xl:sticky xl:top-4">
      <div className="space-y-2.5">
        <p className="text-meta leading-snug">
          Sanal IBAN&apos;a gelen havaleler veliye bağlı aidatlara otomatik işlenmeye çalışılır; belirsiz kalanlar gelen işlemlerde görünür.
        </p>
        <select value={guardianId} onChange={(e) => setGuardianId(e.target.value)} className="field w-full text-sm" aria-label="Veli seç">
          <option value="">Veli seç</option>
          {guardians?.map((g) => (
            <option key={g.id} value={g.id}>{g.firstName} {g.lastName} · {g.phoneNumber}</option>
          ))}
        </select>
        {guardianId && virtualIban && (
          <p className="rounded-xl bg-[var(--success-soft)] px-3 py-2 text-xs font-semibold break-all text-[var(--success-strong)]">
            Atanmış IBAN: {virtualIban.iban} ({virtualIban.provider})
          </p>
        )}
        {guardianId && !virtualIban && (
          <button onClick={handleAssign} disabled={assign.isPending} className="btn btn-primary w-full">
            {assign.isPending ? "Atanıyor…" : "Sanal IBAN ata"}
          </button>
        )}
        {error && <p className="text-sm font-medium text-[var(--danger-strong)]">{error}</p>}
      </div>
    </Panel>
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
    <Panel
      flush
      title="Gelen işlemler"
      meta={data ? `${data.totalCount} kayıt` : undefined}
      actions={
        <Segmented
          label="Duruma göre filtrele"
          options={(["NeedsReview", "Matched", "Ignored", "all"] as const).map((f) => ({ value: f, label: f === "all" ? "Tümü" : STATUS_LABELS[f] }))}
          value={filter}
          onChange={handleFilterChange}
        />
      }
      footer={data && totalPages > 1 ? <Pager page={data.page} totalPages={totalPages} onChange={setPage} /> : undefined}
    >
      {isLoading && <div className="space-y-2 p-4">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-10 rounded-lg" />)}</div>}
      {!isLoading && transactions?.length === 0 && <EmptyState icon="bank" title="Bu filtrede işlem yok." />}
      {!isLoading && !!transactions?.length && (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[46rem] text-sm">
            <thead>
              <tr className="text-micro border-b border-[var(--line)] text-left text-[var(--muted)]">
                <th className="px-4 py-2">Tarih</th>
                <th className="px-3 py-2">Gönderen</th>
                <th className="px-3 py-2">Açıklama</th>
                <th className="px-3 py-2">Tutar</th>
                <th className="px-3 py-2">Durum</th>
                <th className="sticky right-0 bg-[var(--surface)] shadow-[-1px_0_0_var(--line)] px-3 py-2"><span className="sr-only">İşlem</span></th>
              </tr>
            </thead>
            <tbody>
              {transactions.map((t) => (
                <TransactionRow key={t.id} transaction={t} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}

function TransactionRow({ transaction }: { transaction: BankTransaction }) {
  const resolve = useResolveBankTransaction();
  const [receivableId, setReceivableId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [confirmingIgnore, setConfirmingIgnore] = useState(false);

  async function handleResolve(receivableIdOrNull: string | null) {
    setError(null);
    try {
      await resolve.mutateAsync({ transactionId: transaction.id, receivableId: receivableIdOrNull });
      setConfirmingIgnore(false);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "İşlem çözülemedi.");
    }
  }

  return (
    <tr className="border-b border-[var(--line)] align-top last:border-0">
      <td className="text-meta px-4 py-2 whitespace-nowrap">{new Date(transaction.receivedAt).toLocaleString("tr-TR", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}</td>
      <td className="px-3 py-2 font-semibold">{transaction.senderName ?? "—"}</td>
      <td className="text-meta px-3 py-2">{transaction.description ?? "—"}</td>
      <td className="px-3 py-2 font-bold tabular-nums whitespace-nowrap">{transaction.amount.toLocaleString("tr-TR")} {transaction.currency}</td>
      <td className="px-3 py-2">
        <span className={`rounded-full px-2 py-0.5 text-xs font-bold whitespace-nowrap ${STATUS_CLASSES[transaction.status]}`}>{STATUS_LABELS[transaction.status]}</span>
      </td>
      {/* Tablo mobilde yatay kayar; işlem sütunu sağa yapışık kalır ki asıl eylem
          kaydırmadan görünsün. Dar ekranda içerik alt alta dizilip sütunu dar tutar. */}
      <td className="sticky right-0 bg-[var(--surface)] shadow-[-1px_0_0_var(--line)] px-3 py-2">
        {transaction.status === "NeedsReview" && (
          <div className="flex w-40 flex-col items-stretch gap-1.5 lg:w-auto lg:flex-row lg:items-center">
            <input value={receivableId} onChange={(e) => setReceivableId(e.target.value)}
              aria-label="Aidat ID"
              placeholder="Aidat ID"
              title="Aidat ID (Aidatlar sayfasından)"
              className="field w-full text-xs lg:w-32" />
            <button type="button" onClick={() => handleResolve(receivableId)} disabled={!receivableId || resolve.isPending}
              className="btn btn-primary px-2.5 text-xs">
              Bu aidata say
            </button>
            <button type="button" onClick={() => { setError(null); setConfirmingIgnore(true); }} disabled={resolve.isPending}
              className="btn btn-quiet px-2.5 text-xs">
              Hiçbirine sayma
            </button>
          </div>
        )}
        {error && !confirmingIgnore && <p role="alert" className="mt-1 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
        {confirmingIgnore && (
          <Modal open title="İşlem hiçbir aidata sayılmasın mı?" onClose={() => setConfirmingIgnore(false)} size="sm">
            <form
              onSubmit={(event) => { event.preventDefault(); void handleResolve(null); }}
              className="space-y-3.5"
            >
              <p className="text-sm text-[var(--muted)]">
                {transaction.amount.toLocaleString("tr-TR")} {transaction.currency} tutarındaki havale &ldquo;yok sayıldı&rdquo; olarak işaretlenecek ve hiçbir aidata işlenmeyecek.
              </p>
              {error && <FormMessage tone="error">{error}</FormMessage>}
              <FormActions onCancel={() => setConfirmingIgnore(false)} submitLabel="Hiçbirine sayma" pending={resolve.isPending} pendingLabel="İşleniyor…" />
            </form>
          </Modal>
        )}
      </td>
    </tr>
  );
}
