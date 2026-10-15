"use client";

import { useState, type FormEvent } from "react";
import { ApiError, type Me } from "@/lib/api";
import { useUpdateDisplayName } from "@/lib/use-auth";

const MAX_LENGTH = 100;

// Kullanıcı yalnızca kendi görünen adını değiştirir (PUT /api/auth/me/display-name).
// Alan boş kaydedilemez; seçilmiş adı kaldırmak için ayrı "Varsayılana dön" düğmesi null gönderir.
export function DisplayNameForm({ me }: { me: Me }) {
  const update = useUpdateDisplayName();
  const [value, setValue] = useState(me.displayName ?? "");
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function save(displayName: string | null) {
    setError(null);
    setSaved(false);
    try {
      await update.mutateAsync({ displayName });
      if (displayName === null) setValue("");
      setSaved(true);
    } catch (err) {
      if (err instanceof ApiError) {
        const fieldError = Object.values(err.errors ?? {}).flat()[0];
        setError(fieldError ?? err.detail ?? err.title);
      } else {
        setError("Görünen ad kaydedilemedi.");
      }
    }
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const trimmed = value.trim();
    if (!trimmed) {
      setError("Görünen ad boş olamaz.");
      return;
    }
    void save(trimmed);
  }

  return (
    <form onSubmit={handleSubmit} className="grid gap-3">
      <label className="form-label">Görünen ad
        <input
          type="text"
          required
          maxLength={MAX_LENGTH}
          value={value}
          onChange={(e) => { setValue(e.target.value); setSaved(false); }}
          className="field block text-sm sm:max-w-sm"
          placeholder="Örn. Ayşe Kaya"
          autoComplete="name"
        />
      </label>
      <div className="flex flex-wrap items-center gap-2">
        <button type="submit" disabled={update.isPending} className="btn btn-primary">
          {update.isPending ? "Kaydediliyor…" : "Adı kaydet"}
        </button>
        {me.displayName && (
          <button type="button" onClick={() => void save(null)} disabled={update.isPending} className="btn btn-quiet">
            Varsayılana dön
          </button>
        )}
      </div>
      {error && <p role="alert" className="text-sm font-semibold text-[var(--danger-strong)]">{error}</p>}
      {saved && <p role="status" className="rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-sm font-semibold text-[var(--success-strong)]">Görünen adın güncellendi.</p>}
    </form>
  );
}
