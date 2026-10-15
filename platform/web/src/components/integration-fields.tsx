"use client";

import { INTEGRATION_FIELDS, INTEGRATION_TITLES, OPS_ALERT_FIELD, type IntegrationField } from "@/lib/modules";

// Entegrasyon ayarları - yalnızca seçili modüllerin alanları gösterilir. Değerler platformun
// sır kasasında şifreli saklanır, ekrana bir daha dönmez; boş bırakılan sağlayıcı kapalı kalır.
// `stored` verilirse (okul detayı) kasada zaten değeri olan alanlar "kayıtlı" diye işaretlenir.
export function IntegrationFields({ modules, values, onChange, stored }: {
  modules: Set<string>;
  values: Record<string, string>;
  onChange: (key: string, value: string) => void;
  stored?: Set<string>;
}) {
  const groups = Object.keys(INTEGRATION_FIELDS).filter((key) => modules.has(key));

  function field(item: IntegrationField) {
    const isStored = stored?.has(item.key);
    return (
      <label key={item.key} className="form-label">
        {item.label}{item.optional && <span className="font-medium"> (isteğe bağlı)</span>}
        <input
          className="field font-mono text-sm"
          type={item.secret ? "password" : "text"}
          autoComplete="off"
          spellCheck={false}
          placeholder={isStored ? "Kayıtlı — değiştirmek için yeni değer gir" : ""}
          value={values[item.key] ?? ""}
          onChange={(event) => onChange(item.key, event.target.value)}
        />
        <span className="form-hint"><span className="font-mono">{item.key}</span>{item.hint ? ` · ${item.hint}` : ""}</span>
      </label>
    );
  }

  return (
    <div className="grid gap-4">
      {groups.length === 0 && <p className="text-meta">Seçili modüller arasında dış hesap gerektiren yok (WhatsApp, E-posta, Yapay zekâ).</p>}
      {groups.map((group) => (
        <fieldset key={group} className="grid gap-3 rounded-2xl border border-[var(--line)] p-3 sm:p-4">
          <legend className="px-1 text-sm font-bold">{INTEGRATION_TITLES[group]}</legend>
          <div className="grid gap-3 sm:grid-cols-2">{INTEGRATION_FIELDS[group].map(field)}</div>
        </fieldset>
      ))}
      <fieldset className="grid gap-3 rounded-2xl border border-[var(--line)] p-3 sm:p-4">
        <legend className="px-1 text-sm font-bold">Operasyon</legend>
        {field(OPS_ALERT_FIELD)}
      </fieldset>
    </div>
  );
}
