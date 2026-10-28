"use client";

import { useState } from "react";
import { Panel } from "@/components/ui";
import { useSendWhatsAppTest } from "@/lib/messaging";

// WhatsApp entegrasyonunu bir veliye yazmadan denemek için: numara iste, onaylı karşılama şablonunu o
// numaraya gönder. Gönderim sonucu (Meta'nın kabul etmesi ya da hata nedeni) kartta görünür.
export function WhatsAppTestCard() {
  const [phone, setPhone] = useState("");
  const send = useSendWhatsAppTest();
  const result = send.data;

  return (
    <Panel title="WhatsApp'ı dene" meta="Bir numaraya deneme mesajı gönderir">
      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          send.mutate(phone);
        }}
      >
        <label className="min-w-56 flex-1 text-sm">
          <span className="text-meta mb-1 block">Test telefonu</span>
          <input
            value={phone}
            onChange={(event) => setPhone(event.target.value)}
            inputMode="tel"
            autoComplete="off"
            placeholder="0555 123 45 67"
            required
            className="field text-sm"
          />
        </label>
        <button type="submit" disabled={send.isPending || !phone.trim()} className="btn btn-primary">
          {send.isPending ? "Gönderiliyor…" : "Deneme mesajı gönder"}
        </button>
      </form>

      <div aria-live="polite" className="mt-2 text-sm">
        {send.isError && <p className="text-[var(--danger-strong)]">{send.error.message}</p>}
        {result?.success && (
          <p className="text-[var(--success-strong)]">
            Gönderim kabul edildi ({result.provider}){result.messageId ? ` · ${result.messageId}` : ""}. Telefona ulaştığını kontrol et.
          </p>
        )}
        {result && !result.success && (
          <p className="text-[var(--danger-strong)]">Gönderilemedi ({result.provider}): {result.error ?? "bilinmeyen hata"}</p>
        )}
      </div>
    </Panel>
  );
}
