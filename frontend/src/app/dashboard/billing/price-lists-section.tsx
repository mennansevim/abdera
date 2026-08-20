"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useInstruments } from "@/lib/people";
import {
  useApplyBulkUpdate,
  useCreatePriceList,
  usePreviewBulkUpdate,
  usePriceLists,
  type BillingType,
  type BulkUpdatePreviewItem,
  type CreatePriceListItemInput,
} from "@/lib/billing";

export function PriceListsSection() {
  const { data: priceLists, isLoading } = usePriceLists();
  const { data: instruments } = useInstruments();

  return (
    <section className="space-y-4">
      <h2 className="text-lg font-semibold">Fiyat Listeleri</h2>
      <CreatePriceListForm instruments={instruments ?? []} />

      {isLoading && <p className="text-sm text-neutral-500">Yükleniyor…</p>}
      <div className="space-y-3">
        {priceLists?.map((list) => (
          <div key={list.id} className="rounded-lg border border-neutral-200 bg-white p-4">
            <div className="mb-2 flex items-center justify-between">
              <div>
                <h3 className="font-medium">{list.name}</h3>
                <p className="text-xs text-neutral-500">
                  {list.effectiveFrom} – {list.effectiveUntil ?? "süresiz"}
                </p>
              </div>
            </div>
            <table className="mb-3 w-full text-sm">
              <tbody>
                {list.items.map((item) => (
                  <tr key={item.id} className="border-t border-neutral-100">
                    <td className="py-1">{instruments?.find((i) => i.id === item.instrumentId)?.name ?? "?"}</td>
                    <td className="py-1 text-neutral-500">{item.durationMinutes} dk</td>
                    <td className="py-1 text-neutral-500">{item.billingType === "Monthly" ? "Aylık" : "Paket"}</td>
                    <td className="py-1 text-right font-medium">
                      {item.amount.toLocaleString("tr-TR")} {item.currency}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <BulkUpdateForm priceListId={list.id} />
          </div>
        ))}
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
    <form onSubmit={handleSubmit} className="space-y-3 rounded-lg border border-neutral-200 bg-white p-4">
      <div className="flex flex-wrap items-end gap-2">
        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Liste adı</label>
          <input value={name} onChange={(e) => setName(e.target.value)} required
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm" placeholder="2026-2027 Sezonu" />
        </div>
        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Başlangıç tarihi</label>
          <input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} required
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm" />
        </div>
      </div>

      <div className="space-y-2">
        {items.map((item, index) => (
          <div key={index} className="flex flex-wrap items-end gap-2 rounded-md border border-neutral-100 bg-neutral-50 p-2">
            <select value={item.instrumentId} onChange={(e) => updateItem(index, { instrumentId: e.target.value })} required
              className="rounded-md border border-neutral-300 px-2 py-1 text-sm">
              <option value="">Enstrüman</option>
              {instruments.map((i) => (
                <option key={i.id} value={i.id}>{i.name}</option>
              ))}
            </select>
            <input type="number" min={15} step={15} value={item.durationMinutes}
              onChange={(e) => updateItem(index, { durationMinutes: Number(e.target.value) })}
              className="w-20 rounded-md border border-neutral-300 px-2 py-1 text-sm" title="Süre (dk)" />
            <select value={item.billingType} onChange={(e) => updateItem(index, { billingType: e.target.value as BillingType })}
              className="rounded-md border border-neutral-300 px-2 py-1 text-sm">
              <option value="Monthly">Aylık</option>
              <option value="Package">Paket</option>
            </select>
            {item.billingType === "Package" && (
              <input type="number" min={1} placeholder="Ders sayısı" value={item.packageLessonCount ?? ""}
                onChange={(e) => updateItem(index, { packageLessonCount: Number(e.target.value) })}
                className="w-28 rounded-md border border-neutral-300 px-2 py-1 text-sm" />
            )}
            <input type="number" min={0} step={0.01} placeholder="Tutar (TRY)" value={item.amount || ""}
              onChange={(e) => updateItem(index, { amount: Number(e.target.value) })} required
              className="w-32 rounded-md border border-neutral-300 px-2 py-1 text-sm" />
            {items.length > 1 && (
              <button type="button" onClick={() => setItems((prev) => prev.filter((_, i) => i !== index))}
                className="text-sm text-red-600">
                Sil
              </button>
            )}
          </div>
        ))}
        <button type="button"
          onClick={() => setItems((prev) => [...prev, { instrumentId: "", durationMinutes: 45, billingType: "Monthly", amount: 0 }])}
          className="text-sm text-neutral-500 underline">
          + Kalem ekle
        </button>
      </div>

      <button type="submit" disabled={createPriceList.isPending}
        className="rounded-md bg-neutral-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
        {createPriceList.isPending ? "Oluşturuluyor…" : "Fiyat listesi oluştur"}
      </button>
      {error && <p className="text-sm text-red-600">{error}</p>}
    </form>
  );
}

function BulkUpdateForm({ priceListId }: { priceListId: string }) {
  const preview = usePreviewBulkUpdate();
  const apply = useApplyBulkUpdate();
  const [percentage, setPercentage] = useState(10);
  const [previewResult, setPreviewResult] = useState<BulkUpdatePreviewItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [applied, setApplied] = useState(false);

  async function handlePreview() {
    setError(null);
    setApplied(false);
    try {
      const result = await preview.mutateAsync({ priceListId, percentageChange: percentage });
      setPreviewResult(result);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Önizleme alınamadı.");
    }
  }

  async function handleApply() {
    setError(null);
    try {
      await apply.mutateAsync({ priceListId, percentageChange: percentage });
      setApplied(true);
      setPreviewResult(null);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Uygulanamadı.");
    }
  }

  return (
    <div className="border-t border-neutral-100 pt-3">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <span className="text-neutral-600">Toplu zam/indirim:</span>
        <input type="number" value={percentage} onChange={(e) => setPercentage(Number(e.target.value))}
          className="w-20 rounded-md border border-neutral-300 px-2 py-1" />
        <span className="text-neutral-500">%</span>
        <button onClick={handlePreview} className="rounded-md border border-neutral-300 px-3 py-1 hover:bg-neutral-100">
          Önizle
        </button>
        {previewResult && (
          <button onClick={handleApply} className="rounded-md bg-neutral-900 px-3 py-1 text-white">
            Uygula
          </button>
        )}
      </div>

      {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
      {applied && <p className="mt-2 text-sm text-green-700">Uygulandı - geçmiş aidatlar etkilenmedi.</p>}
      {previewResult && (
        <ul className="mt-2 space-y-1 text-sm">
          {previewResult.map((p) => (
            <li key={p.itemId} className="rounded bg-amber-50 px-2 py-1 text-amber-900">
              {p.instrumentName} ({p.durationMinutes} dk): {p.oldAmount.toLocaleString("tr-TR")} → {p.newAmount.toLocaleString("tr-TR")} TRY
              {p.activeFeePlanCount > 0 && ` · ${p.activeFeePlanCount} aktif kayıt etkilenecek`}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
