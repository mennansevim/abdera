"use client";

import { useState } from "react";
import { MODULE_DESCRIPTIONS, dependenciesOf, dependentsOf, enableModule } from "@/lib/modules";
import type { ModuleEntry } from "@/lib/releases";
import { Icon } from "./icons";
import { Toggle } from "./ui";

// Modül kartları (sihirbaz "Eklentiler" adımı ve okul detayı > Modüller). Standart paket her okulda
// açık; opsiyonel olanlar yalnızca ücretli eklentiler (dış sağlayıcı maliyeti olan, §5.2). Kurallar:
// standart modüller kilitli açık; bir modül açılınca bağımlılıkları da açılır ve bilgi notu
// çıkar; başka bir açık modülün bağımlı olduğu modül kapatılamaz.
export function ModulePicker({ catalog, selected, onChange, disabled = false }: {
  catalog: ModuleEntry[];
  selected: Set<string>;
  onChange: (next: Set<string>) => void;
  disabled?: boolean;
}) {
  const [note, setNote] = useState<string | null>(null);
  const nameOf = (key: string) => catalog.find((m) => m.key === key)?.name ?? key;
  const core = catalog.filter((m) => m.isCore);
  const optional = catalog.filter((m) => !m.isCore);

  function toggle(entry: ModuleEntry, value: boolean) {
    if (value) {
      const { next, added } = enableModule(entry.key, selected, catalog);
      setNote(added.length > 0 ? `${entry.name} için ${added.map(nameOf).join(", ")} da açıldı.` : null);
      onChange(next);
      return;
    }
    const blockers = dependentsOf(entry.key, selected, catalog);
    if (blockers.length > 0) {
      setNote(`${entry.name} kapatılamaz: ${blockers.map((m) => m.name).join(", ")} buna bağlı. Önce onları kapat.`);
      return;
    }
    const next = new Set(selected);
    next.delete(entry.key);
    setNote(null);
    onChange(next);
  }

  return (
    <div className="grid gap-4">
      <div>
        <h3 className="text-micro text-[var(--muted)]">Standart paket (her okulda açık)</h3>
        <div className="mt-2 flex flex-wrap gap-2">
          {core.map((entry) => (
            <span key={entry.key} title={MODULE_DESCRIPTIONS[entry.key]} className="inline-flex items-center gap-1.5 rounded-full border border-[var(--line)] bg-[var(--surface-muted)] px-2.5 py-1 text-xs font-bold">
              <Icon name="check" className="h-3.5 w-3.5 text-[var(--success)]" />{entry.name}
            </span>
          ))}
        </div>
      </div>

      {note && <p role="status" className="rounded-xl bg-[var(--info-soft)] px-3 py-2 text-xs font-semibold text-[var(--info-strong)]">{note}</p>}

      <div>
        <h3 className="text-micro text-[var(--muted)]">Ücretli eklentiler</h3>
        {optional.length === 0 && <p className="text-meta mt-2">Bu sürümde ücretli eklenti yok.</p>}
        <div className="mt-2 grid gap-2 sm:grid-cols-2">
          {optional.map((entry) => {
            const on = selected.has(entry.key);
            const requires = [...dependenciesOf(entry.key, catalog)].filter((key) => !catalog.find((m) => m.key === key)?.isCore);
            const blockers = on ? dependentsOf(entry.key, selected, catalog) : [];
            return (
              <div key={entry.key} className={`flex items-start gap-3 rounded-2xl border p-3 ${on ? "border-[var(--brand)] bg-[var(--brand-soft)]/40" : "border-[var(--line)]"}`}>
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-bold">{entry.name}</p>
                  {MODULE_DESCRIPTIONS[entry.key] && <p className="text-meta mt-0.5">{MODULE_DESCRIPTIONS[entry.key]}</p>}
                  {requires.length > 0 && <p className="mt-1 text-[.72rem] font-semibold text-[var(--muted)]">Gerektirir: {requires.map(nameOf).join(", ")}</p>}
                  {blockers.length > 0 && <p className="mt-1 text-[.72rem] font-semibold text-[var(--warning-strong)]">Bağımlı: {blockers.map((m) => m.name).join(", ")}</p>}
                </div>
                <Toggle checked={on} onChange={(value) => toggle(entry, value)} disabled={disabled} label={`${entry.name} modülü`} />
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
