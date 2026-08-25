"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useInstruments } from "@/lib/people";
import { useCreatePriceList, usePriceLists, type BillingType, type CreatePriceListItemInput } from "@/lib/billing";

export function PriceListsSection() {
  const { data: priceLists, isLoading } = usePriceLists();
  const { data: instruments } = useInstruments();

  return (
    <section className="space-y-4">
      <div>
        <h2 className="text-title">Kurs Bedeli</h2>
        <p className="text-meta mt-1">Enstrüman ve ders süresine göre birim fiyatlar - aidatlar buradan hesaplanır.</p>
      </div>
      <CreatePriceListForm instruments={instruments ?? []} />

      {isLoading && <div className="space-y-2">{Array.from({ length: 2 }, (_, index) => <div key={index} className="skeleton h-24 rounded-2xl" />)}</div>}
      <div className="space-y-4">
        {priceLists?.map((list) => (
          <div key={list.id} className="app-card overflow-hidden">
            <div className="flex items-baseline justify-between border-b-2 border-[var(--line)] px-5 py-4">
              <div>
                <h3 className="font-serif text-lg font-bold italic">{list.name}</h3>
                <p className="text-meta mt-0.5">
                  {list.effectiveFrom} – {list.effectiveUntil ?? "süresiz"}
                </p>
              </div>
            </div>
            <div className="divide-y divide-[var(--line)]">
              {list.items.map((item) => (
                <div key={item.id} className="flex items-center gap-3 px-5 py-3 text-sm font-semibold">
                  <span className="w-32 shrink-0">{instruments?.find((i) => i.id === item.instrumentId)?.name ?? "?"}</span>
                  <span className="w-24 shrink-0 text-[var(--muted)]">{item.durationMinutes} dk</span>
                  <span className="flex-1 text-[var(--muted)]">{item.billingType === "Monthly" ? "Aylık" : "Paket"}</span>
                  <span className="font-bold text-[var(--brand-strong)]">
                    {item.amount.toLocaleString("tr-TR")} {item.currency}
                  </span>
                </div>
              ))}
            </div>
          </div>
        ))}
        {priceLists?.length === 0 && !isLoading && <p className="text-meta">Henüz fiyat listesi yok.</p>}
      </div>
    </section>
  );
}

function CreatePriceListForm({ instruments }: { instruments: { id: string; name: string }[] }) {
  const createPriceList = useCreatePriceList();
  const [name, setName] = useState("");
  const [effectiveFrom, setEffectiveFrom] = useState(() => new Date().toISOString().slice(0, 10));
  const [items, setItems] = useState<CreatePriceListItemInput[]>([
    { instrumentId: "", durationMinutes: 45, billingType: "Monthly", amount: 0 },
  ]);
  const [error, setError] = useState<string | null>(null);

  function updateItem(index: number, patch: Partial<CreatePriceListItemInput>) {
    setItems((prev) => prev.map((it, i) => (i === index ? { ...it, ...patch } : it)));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createPriceList.mutateAsync({ name, effectiveFrom, items });
      setName("");
      setItems([{ instrumentId: "", durationMinutes: 45, billingType: "Monthly", amount: 0 }]);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Fiyat listesi oluşturulamadı.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="app-card space-y-3 p-4 sm:p-5">
      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1.5">
          <label className="text-[.7rem] font-bold text-[var(--muted)]">Liste adı</label>
          <input value={name} onChange={(e) => setName(e.target.value)} required
            className="field min-h-11 text-sm" placeholder="2026-2027 Sezonu" />
        </div>
        <div className="space-y-1.5">
          <label className="text-[.7rem] font-bold text-[var(--muted)]">Başlangıç tarihi</label>
          <input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} required
            className="field min-h-11 text-sm" />
        </div>
      </div>

      <div className="space-y-2">
        {items.map((item, index) => (
          <div key={index} className="flex flex-wrap items-end gap-2 rounded-xl bg-[var(--surface-muted)] p-2.5">
            <select value={item.instrumentId} onChange={(e) => updateItem(index, { instrumentId: e.target.value })} required
              className="field min-h-10 w-auto text-sm">
              <option value="">Enstrüman</option>
              {instruments.map((i) => (
                <option key={i.id} value={i.id}>{i.name}</option>
              ))}
            </select>
            <input type="number" min={15} step={15} value={item.durationMinutes}
              onChange={(e) => updateItem(index, { durationMinutes: Number(e.target.value) })}
              className="field min-h-10 w-20 text-sm" title="Süre (dk)" />
            <select value={item.billingType} onChange={(e) => updateItem(index, { billingType: e.target.value as BillingType })}
              className="field min-h-10 w-auto text-sm">
              <option value="Monthly">Aylık</option>
              <option value="Package">Paket</option>
            </select>
            {item.billingType === "Package" && (
              <input type="number" min={1} placeholder="Ders sayısı" value={item.packageLessonCount ?? ""}
                onChange={(e) => updateItem(index, { packageLessonCount: Number(e.target.value) })}
                className="field min-h-10 w-28 text-sm" />
            )}
            <input type="number" min={0} step={0.01} placeholder="Tutar (TRY)" value={item.amount || ""}
              onChange={(e) => updateItem(index, { amount: Number(e.target.value) })} required
              className="field min-h-10 w-32 text-sm" />
            {items.length > 1 && (
              <button type="button" onClick={() => setItems((prev) => prev.filter((_, i) => i !== index))}
                className="pressable min-h-10 rounded-lg px-2 text-sm font-bold text-[var(--danger-strong)]">
                Sil
              </button>
            )}
          </div>
        ))}
        <button type="button"
          onClick={() => setItems((prev) => [...prev, { instrumentId: "", durationMinutes: 45, billingType: "Monthly", amount: 0 }])}
          className="text-sm font-bold text-[var(--brand)] hover:text-[var(--brand-strong)]">
          + Kalem ekle
        </button>
      </div>

      <button type="submit" disabled={createPriceList.isPending}
        className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)] disabled:opacity-50">
        {createPriceList.isPending ? "Oluşturuluyor…" : "Fiyat listesi oluştur"}
      </button>
      {error && <p className="text-sm font-medium text-[var(--danger-strong)]">{error}</p>}
    </form>
  );
}
