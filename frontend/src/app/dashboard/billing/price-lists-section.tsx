"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { AddButton, FormActions, FormMessage, Modal, SectionHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useInstruments } from "@/lib/people";
import { useCreatePriceList, usePriceLists, type BillingType, type CreatePriceListItemInput } from "@/lib/billing";

// Form artık listenin üstünde her zaman açık durmuyor: ekranın asıl içeriği yürürlükteki
// fiyatlar, yeni liste ise sağ üstteki "+" ile açılan pencerede kurulur. Kalem alanlarının
// DAİMA görünen etiketleri korunur - dar ekranda etiketsiz kutular ne olduğu belirsiz
// kalıyordu (önceki sürümün notu).

export function PriceListsSection() {
  const { data: priceLists, isLoading } = usePriceLists();
  const { data: instruments } = useInstruments();
  const [showCreate, setShowCreate] = useState(false);

  return (
    <section className="space-y-4">
      <SectionHeader
        title="Kurs bedeli"
        description="Enstrüman ve ders süresine göre birim fiyatlar - aidatlar buradan hesaplanır."
        actions={<AddButton label="Fiyat listesi ekle" onClick={() => setShowCreate(true)} />}
      />

      {isLoading && <div className="space-y-2">{Array.from({ length: 2 }, (_, index) => <div key={index} className="skeleton h-24 rounded-2xl" />)}</div>}
      <div className="space-y-3">
        {priceLists?.map((list) => (
          <div key={list.id} className="app-card overflow-hidden">
            <div className="flex flex-wrap items-baseline justify-between gap-2 border-b border-[var(--line)] px-4 py-3">
              <h3 className="text-title">{list.name}</h3>
              <p className="text-meta">{list.effectiveFrom} – {list.effectiveUntil ?? "süresiz"} · {list.items.length} kalem</p>
            </div>
            <div className="hidden grid-cols-[minmax(9rem,1.3fr)_minmax(6rem,.8fr)_minmax(6rem,.8fr)_auto] gap-3 border-b border-[var(--line)] px-4 py-2 text-meta font-bold sm:grid">
              <span>Enstrüman</span><span>Süre</span><span>Ders türü</span><span className="text-right">Tutar</span>
            </div>
            <div className="divide-y divide-[var(--line)]">
              {list.items.map((item) => (
                <div key={item.id} className="grid grid-cols-2 gap-2 px-4 py-2.5 text-sm font-semibold sm:grid-cols-[minmax(9rem,1.3fr)_minmax(6rem,.8fr)_minmax(6rem,.8fr)_auto] sm:items-center sm:gap-3">
                  <span className="col-span-2 sm:col-span-1">{instruments?.find((i) => i.id === item.instrumentId)?.name ?? "?"}</span>
                  <span className="text-meta">{item.durationMinutes} dk</span>
                  <span className="text-meta">{item.billingType === "Monthly" ? "Aylık" : `Paket · ${item.packageLessonCount ?? "?"} ders`}</span>
                  <span className="col-span-2 text-right font-bold tabular-nums text-[var(--brand-strong)] sm:col-span-1">
                    {item.amount.toLocaleString("tr-TR")} {item.currency}
                  </span>
                </div>
              ))}
            </div>
          </div>
        ))}
        {priceLists?.length === 0 && !isLoading && <p className="app-card p-6 text-center text-sm text-[var(--muted)]">Henüz fiyat listesi yok.</p>}
      </div>

      <Modal open={showCreate} title="Fiyat listesi ekle" description="Dönem ücretlerini tanımla; sonraki zamlar geçmiş aidatları değiştirmez." onClose={() => setShowCreate(false)}>
        <CreatePriceListForm instruments={instruments ?? []} onClose={() => setShowCreate(false)} onCreated={() => setShowCreate(false)} />
      </Modal>
    </section>
  );
}

function emptyItem(): CreatePriceListItemInput {
  return { instrumentId: "", durationMinutes: 45, billingType: "Monthly", amount: 0 };
}

function CreatePriceListForm({ instruments, onClose, onCreated }: { instruments: { id: string; name: string }[]; onClose: () => void; onCreated: () => void }) {
  const createPriceList = useCreatePriceList();
  const [name, setName] = useState("");
  const [effectiveFrom, setEffectiveFrom] = useState(() => new Date().toISOString().slice(0, 10));
  const [items, setItems] = useState<CreatePriceListItemInput[]>([emptyItem()]);
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
      setItems([emptyItem()]);
      onCreated();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Fiyat listesi oluşturulamadı.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Liste adı
          <input value={name} onChange={(e) => setName(e.target.value)} required className="field text-sm" placeholder="2026-2027 Sezonu" />
        </label>
        <label className="form-label">Başlangıç tarihi
          <input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} required className="field text-sm" />
        </label>
      </div>

      <div className="space-y-2.5">
        {items.map((item, index) => (
          <div key={index} className="rounded-2xl border border-[var(--line)] p-3">
            <div className="mb-2.5 flex items-center justify-between">
              <p className="text-meta font-bold">Kalem {index + 1}</p>
              {items.length > 1 && (
                <button type="button" onClick={() => setItems((prev) => prev.filter((_, i) => i !== index))}
                  aria-label={`Kalem ${index + 1}'i sil`} title="Kalemi sil"
                  className="icon-btn icon-btn-quiet h-8 w-8 hover:border-[var(--danger)] hover:text-[var(--danger-strong)]">
                  <Icon name="x" className="h-3.5 w-3.5" />
                </button>
              )}
            </div>

            <div className="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
              <label className="form-label">Enstrüman
                <select value={item.instrumentId} onChange={(e) => updateItem(index, { instrumentId: e.target.value })} required className="field min-h-10 text-sm">
                  <option value="">Seç…</option>
                  {instruments.map((i) => <option key={i.id} value={i.id}>{i.name}</option>)}
                </select>
              </label>

              <label className="form-label">Ders süresi (dk)
                <input type="number" min={15} step={15} value={item.durationMinutes}
                  onChange={(e) => updateItem(index, { durationMinutes: Number(e.target.value) })} required
                  className="field min-h-10 text-sm" />
              </label>

              <label className="form-label">Ders türü
                <select value={item.billingType} onChange={(e) => updateItem(index, { billingType: e.target.value as BillingType })} className="field min-h-10 text-sm">
                  <option value="Monthly">Aylık</option>
                  <option value="Package">Paket</option>
                </select>
              </label>

              {item.billingType === "Package" && (
                <label className="form-label">Paketteki ders sayısı
                  <input type="number" min={1} value={item.packageLessonCount ?? ""}
                    onChange={(e) => updateItem(index, { packageLessonCount: Number(e.target.value) })} required
                    className="field min-h-10 text-sm" placeholder="Örn. 8" />
                </label>
              )}

              <label className="form-label">Tutar (₺)
                <input type="number" min={0} step={0.01} value={item.amount || ""}
                  onChange={(e) => updateItem(index, { amount: Number(e.target.value) })} required
                  className="field min-h-10 text-sm" placeholder="0,00" />
              </label>
            </div>
          </div>
        ))}

        <button type="button"
          onClick={() => setItems((prev) => [...prev, emptyItem()])}
          className="btn btn-quiet w-full border-dashed">
          <Icon name="plus" className="h-4 w-4" /> Kalem ekle
        </button>
      </div>

      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Fiyat listesi oluştur" pending={createPriceList.isPending} pendingLabel="Oluşturuluyor…" />
    </form>
  );
}
