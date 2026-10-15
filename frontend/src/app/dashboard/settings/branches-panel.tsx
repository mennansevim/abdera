"use client";

import { useMemo, useState, type FormEvent } from "react";
import { FormMessage, SectionHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useBranchCategories, useBranchTerms, type BranchCategory } from "@/lib/instance";
import { useInstruments, useSaveInstrument, type Instrument } from "@/lib/people";

// Okulun öğrettiği alanlar (docs/multi-tenant/ARCHITECTURE.md §5.6): kategori (Müzik, Dans, Spor...)
// ve altındaki alt birimler (enstrüman, dans türü, branş...). Yalnızca yönetici görür. Alt birim
// silinmez, pasife alınır: eski kayıtlar adını göstermeye devam eder, yeni kayıtta seçilmez.
export function BranchesPanel() {
  const terms = useBranchTerms();
  const categories = useBranchCategories();
  const { data: branches } = useInstruments();

  return (
    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] px-4 py-2.5">
        <SectionHeader
          title={`Eğitim alanları ve ${terms.branchPluralLower}`}
          description="Okulun öğrettiği kategoriler ve altındaki dersler. Ekranlardaki terim buna göre değişir (ör. yalnız müzikte “Enstrüman”, birden çok alanda “Branş”)."
        />
      </div>
      <div className="space-y-4 p-4">
        {!branches || categories.length === 0
          ? <div className="skeleton h-32 rounded-xl" aria-busy="true" />
          : <BranchList branches={branches} categories={categories} />}
      </div>
    </section>
  );
}

function BranchList({ branches, categories }: { branches: Instrument[]; categories: BranchCategory[] }) {
  const save = useSaveInstrument();
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const [editing, setEditing] = useState<string | null>(null);

  // Kullanılan kategoriler önce, katalog sırasıyla; kategorisi bilinmeyen alt birim "Diğer"e düşer.
  const grouped = useMemo(() => {
    const byCategory = new Map<string, Instrument[]>();
    for (const branch of branches) {
      const key = categories.some((category) => category.key === branch.category) ? branch.category! : "other";
      byCategory.set(key, [...(byCategory.get(key) ?? []), branch]);
    }
    return categories.filter((category) => byCategory.has(category.key)).map((category) => ({ category, items: byCategory.get(category.key)! }));
  }, [branches, categories]);

  async function run(input: Parameters<typeof save.mutateAsync>[0], success: string) {
    setMessage(null);
    try {
      await save.mutateAsync(input);
      setMessage({ tone: "success", text: success });
      return true;
    } catch (err) {
      const fieldError = err instanceof ApiError && err.errors ? Object.values(err.errors)[0]?.[0] : undefined;
      setMessage({ tone: "error", text: fieldError ?? (err instanceof ApiError ? err.detail ?? err.title : "Kaydedilemedi.") });
      return false;
    }
  }

  return (
    <>
      {message && <FormMessage tone={message.tone}>{message.text}</FormMessage>}
      {grouped.length === 0 && <p className="text-meta">Henüz alt birim yok. Aşağıdan bir kategori seçip ekle.</p>}
      {grouped.map(({ category, items }) => (
        <div key={category.key}>
          <p className="text-xs font-bold uppercase tracking-wide text-[var(--muted)]">{category.name} · {category.unitLabelPlural}</p>
          <ul className="mt-2 flex flex-wrap gap-2">
            {items.map((branch) => editing === branch.id
              ? <li key={branch.id} className="w-full"><EditBranch branch={branch} categories={categories} onDone={() => setEditing(null)} onSave={run} pending={save.isPending} /></li>
              : (
                <li key={branch.id}>
                  <button
                    type="button"
                    onClick={() => setEditing(branch.id)}
                    className={`pressable min-h-9 rounded-full border px-3 text-sm font-semibold ${branch.isActive === false ? "border-dashed border-[var(--line)] text-[var(--muted)] line-through" : "border-[var(--line)] bg-[var(--surface)]"}`}
                    title={branch.isActive === false ? "Pasif - düzenlemek için tıkla" : "Düzenlemek için tıkla"}
                  >
                    {branch.name}
                  </button>
                </li>
              ))}
          </ul>
        </div>
      ))}
      <AddBranches branches={branches} categories={categories} onSave={run} pending={save.isPending} />
    </>
  );
}

function EditBranch({ branch, categories, onDone, onSave, pending }: {
  branch: Instrument;
  categories: BranchCategory[];
  onDone: () => void;
  onSave: (input: { id?: string; name: string; category: string; isActive?: boolean }, success: string) => Promise<boolean>;
  pending: boolean;
}) {
  const [name, setName] = useState(branch.name);
  const [category, setCategory] = useState(branch.category ?? "other");
  const [active, setActive] = useState(branch.isActive !== false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (await onSave({ id: branch.id, name, category, isActive: active }, `${name.trim()} kaydedildi.`)) onDone();
  }

  return (
    <form onSubmit={submit} className="grid gap-2 rounded-xl border border-[var(--line)] p-3 sm:grid-cols-[1fr_1fr_auto_auto] sm:items-end">
      <label className="form-label">Ad<input value={name} onChange={(event) => setName(event.target.value)} required maxLength={100} className="field text-sm" /></label>
      <label className="form-label">Kategori
        <select value={category} onChange={(event) => setCategory(event.target.value)} className="field text-sm">
          {categories.map((item) => <option key={item.key} value={item.key}>{item.name}</option>)}
        </select>
      </label>
      <label className="flex min-h-11 items-center gap-2 text-sm font-semibold">
        <input type="checkbox" checked={active} onChange={(event) => setActive(event.target.checked)} /> Aktif
      </label>
      <div className="flex gap-2">
        <button type="button" onClick={onDone} className="btn btn-quiet">Vazgeç</button>
        <button type="submit" disabled={pending} className="btn btn-primary">Kaydet</button>
      </div>
    </form>
  );
}

function AddBranches({ branches, categories, onSave, pending }: {
  branches: Instrument[];
  categories: BranchCategory[];
  onSave: (input: { name: string; category: string }, success: string) => Promise<boolean>;
  pending: boolean;
}) {
  const [categoryKey, setCategoryKey] = useState(() => categories.find((category) => category.inUse)?.key ?? categories[0]?.key ?? "other");
  const [custom, setCustom] = useState("");
  const category = categories.find((item) => item.key === categoryKey) ?? categories[0];
  const existing = new Set(branches.map((branch) => branch.name.toLocaleLowerCase("tr-TR")));
  const suggestions = (category?.suggestions ?? []).filter((name) => !existing.has(name.toLocaleLowerCase("tr-TR")));

  async function addCustom(event: FormEvent) {
    event.preventDefault();
    if (custom.trim() && (await onSave({ name: custom, category: categoryKey }, `${custom.trim()} eklendi.`))) setCustom("");
  }

  return (
    <div className="rounded-xl bg-[var(--surface-muted,transparent)] border border-[var(--line)] p-3">
      <p className="text-sm font-bold">Ekle</p>
      <div className="mt-2 flex flex-wrap gap-1.5" role="group" aria-label="Kategori">
        {categories.map((item) => (
          <button key={item.key} type="button" onClick={() => setCategoryKey(item.key)} aria-pressed={item.key === categoryKey}
            className={`pressable min-h-9 rounded-full px-3 text-xs font-bold ${item.key === categoryKey ? "bg-[var(--brand)] text-white" : "border border-[var(--line)] text-[var(--muted)]"}`}>
            {item.name}
          </button>
        ))}
      </div>
      {suggestions.length > 0 && (
        <div className="mt-3">
          <p className="text-meta">Öneriler (tıkla, eklensin)</p>
          <div className="mt-1.5 flex flex-wrap gap-1.5">
            {suggestions.map((name) => (
              <button key={name} type="button" disabled={pending} onClick={() => onSave({ name, category: categoryKey }, `${name} eklendi.`)}
                className="pressable min-h-9 rounded-full border border-dashed border-[var(--line)] px-3 text-sm">
                + {name}
              </button>
            ))}
          </div>
        </div>
      )}
      <form onSubmit={addCustom} className="mt-3 flex gap-2">
        <input value={custom} onChange={(event) => setCustom(event.target.value)} maxLength={100}
          placeholder={`Başka bir ${category?.unitLabel.toLocaleLowerCase("tr-TR") ?? "ders"} (ör. ${category?.suggestions[0] ?? "Atölye"})`}
          aria-label="Yeni alt birim adı" className="field text-sm" />
        <button type="submit" disabled={pending || !custom.trim()} className="btn btn-primary shrink-0">Ekle</button>
      </form>
    </div>
  );
}
