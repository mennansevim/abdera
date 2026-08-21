"use client";

import { useState } from "react";
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
    <div className="space-y-10">
      <h1 className="text-2xl font-semibold">Banka Entegrasyonu</h1>
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
      <h2 className="text-lg font-semibold">Sanal IBAN Ataması</h2>
      <p className="text-sm text-neutral-500">
        Bir veliye sanal IBAN atandığında o IBAN&apos;a gelen havaleler otomatik olarak veliye bağlı aidatlara
        işlenmeye çalışılır. Belirsiz kalan işlemler aşağıdaki listede görünür.
      </p>

      <div className="flex flex-wrap items-end gap-2">
        <select value={guardianId} onChange={(e) => setGuardianId(e.target.value)}
          className="rounded-md border border-neutral-300 px-2 py-1.5 text-sm">
          <option value="">Veli seç</option>
          {guardians?.map((g) => (
            <option key={g.id} value={g.id}>{g.firstName} {g.lastName} · {g.phoneNumber}</option>
          ))}
        </select>

        {guardianId && virtualIban && (
          <span className="rounded-md bg-green-50 px-3 py-1.5 text-sm text-green-800">
            Atanmış IBAN: {virtualIban.iban} ({virtualIban.provider})
          </span>
        )}
        {guardianId && !virtualIban && (
          <button onClick={handleAssign} disabled={assign.isPending}
            className="rounded-md bg-neutral-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
            {assign.isPending ? "Atanıyor…" : "Sanal IBAN ata"}
          </button>
        )}
      </div>
      {error && <p className="text-sm text-red-600">{error}</p>}
    </section>
  );
}

const STATUS_LABELS: Record<BankTransactionStatus, string> = {
  Received: "alındı", Matched: "eşleşti", NeedsReview: "gözden geçirilmeli", Ignored: "yok sayıldı",
};
const STATUS_COLORS: Record<BankTransactionStatus, string> = {
  Received: "text-neutral-500", Matched: "text-green-700", NeedsReview: "text-amber-600", Ignored: "text-neutral-400",
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
      <h2 className="text-lg font-semibold">Gelen İşlemler</h2>

      <div className="flex flex-wrap gap-2">
        {(["NeedsReview", "Matched", "Ignored", "all"] as const).map((f) => (
          <button key={f} onClick={() => handleFilterChange(f)}
            className={filter === f
              ? "rounded-md bg-neutral-900 px-3 py-1 text-sm text-white"
              : "rounded-md border border-neutral-300 px-3 py-1 text-sm text-neutral-700 hover:bg-neutral-100"}>
            {f === "all" ? "Tümü" : STATUS_LABELS[f]}
          </button>
        ))}
      </div>

      {isLoading && <p className="text-sm text-neutral-500">Yükleniyor…</p>}

      <div className="overflow-x-auto rounded-lg border border-neutral-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-neutral-200 text-left text-xs text-neutral-500">
              <th className="px-3 py-2">Tarih</th>
              <th className="px-3 py-2">Gönderen</th>
              <th className="px-3 py-2">Açıklama</th>
              <th className="px-3 py-2">Tutar</th>
              <th className="px-3 py-2">Durum</th>
              <th className="px-3 py-2" />
            </tr>
          </thead>
          <tbody>
            {transactions?.map((t) => (
              <TransactionRow key={t.id} transaction={t} />
            ))}
            {transactions?.length === 0 && !isLoading && (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-neutral-400">Bu filtrede işlem yok.</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {data && data.totalCount > 0 && (
        <div className="flex items-center justify-between text-sm text-neutral-500">
          <span>
            Toplam {data.totalCount} kayıt - sayfa {data.page} / {totalPages}
          </span>
          <div className="flex gap-2">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="min-h-11 rounded-md border border-neutral-300 px-3 hover:bg-neutral-100 disabled:opacity-50"
            >
              Önceki
            </button>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="min-h-11 rounded-md border border-neutral-300 px-3 hover:bg-neutral-100 disabled:opacity-50"
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
    <tr className="border-t border-neutral-100 align-top">
      <td className="py-2 px-3 text-neutral-500">{new Date(transaction.receivedAt).toLocaleString("tr-TR")}</td>
      <td className="py-2 px-3">{transaction.senderName ?? "—"}</td>
      <td className="py-2 px-3 text-neutral-500">{transaction.description ?? "—"}</td>
      <td className="py-2 px-3">{transaction.amount.toLocaleString("tr-TR")} {transaction.currency}</td>
      <td className={`py-2 px-3 font-medium ${STATUS_COLORS[transaction.status]}`}>{STATUS_LABELS[transaction.status]}</td>
      <td className="py-2 px-3">
        {transaction.status === "NeedsReview" && (
          <div className="flex flex-wrap items-center gap-1">
            <input value={receivableId} onChange={(e) => setReceivableId(e.target.value)}
              placeholder="Aidat ID (Aidatlar sayfasından)"
              className="w-48 rounded border border-neutral-300 px-1.5 py-1 text-xs" />
            <button onClick={() => handleResolve(receivableId)} disabled={!receivableId || resolve.isPending}
              className="rounded border border-neutral-300 px-2 py-1 text-xs hover:bg-neutral-100 disabled:opacity-50">
              Bu aidata say
            </button>
            <button onClick={() => handleResolve(null)} disabled={resolve.isPending}
              className="rounded border border-neutral-300 px-2 py-1 text-xs text-neutral-500 hover:bg-neutral-100">
              Hiçbirine sayma
            </button>
          </div>
        )}
        {error && <p className="mt-1 text-xs text-red-600">{error}</p>}
      </td>
    </tr>
  );
}
