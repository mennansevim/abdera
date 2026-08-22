"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { ChangePasswordForm } from "../change-password-form";

export default function SettingsPage() {
  const [saved, setSaved] = useState(false);

  return (
    <div className="mx-auto max-w-4xl space-y-5">
      <div>
        <p className="text-micro text-[var(--brand-strong)]">Hesap ve güvenlik</p>
        <h1 className="text-display mt-1 font-serif italic">Ayarlar</h1>
        <p className="text-meta mt-2 max-w-2xl">Hesap güvenliği, bildirim tercihleri ve uygulama davranışları burada yönetilir.</p>
      </div>

      <section className="app-card overflow-hidden">
        <div className="flex items-start gap-3 border-b border-[var(--line)] p-5 sm:p-6">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name="settings" className="h-5 w-5" /></span>
          <div>
            <h2 className="text-title">Şifre değiştir</h2>
            <p className="text-meta mt-1">Kalıcı şifreni güncellemek için mevcut şifreni ve en az 8 karakterli yeni şifreni gir.</p>
          </div>
        </div>
        <div className="p-5 sm:p-6">
          <ChangePasswordForm onDone={() => setSaved(true)} />
          {saved && <p role="status" className="mt-4 rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-sm font-semibold text-[var(--success-strong)]">Şifren güncellendi.</p>}
        </div>
      </section>

      <section className="app-card grid gap-4 p-5 sm:grid-cols-[auto_1fr] sm:p-6">
        <span className="grid h-11 w-11 place-items-center rounded-2xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name="bell" className="h-5 w-5" /></span>
        <div>
          <h2 className="text-title">Mesaj Merkezi</h2>
          <p className="text-meta mt-1">Ders hatırlatmaları, hazır WhatsApp şablonları ve gönderim tercihleri için Mesaj Merkezi’ni kullan.</p>
        </div>
      </section>
    </div>
  );
}
