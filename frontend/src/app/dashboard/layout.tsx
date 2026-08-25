"use client";

import { useRequireAuth } from "@/lib/use-require-auth";
import { AppShell } from "./app-header";

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const { me, isLoading, authError } = useRequireAuth();

  if (isLoading) {
    return (
      <main className="grid min-h-dvh place-items-center bg-[var(--background)]">
        <div className="flex flex-col items-center gap-3 text-sm text-[var(--muted)]">
          <span className="brand-mark animate-pulse" aria-hidden="true" />
          Uygulama hazırlanıyor…
        </div>
      </main>
    );
  }

  if (authError && !me) {
    return (
      <main className="grid min-h-dvh place-items-center bg-[var(--background)] p-5">
        <div className="app-card w-full max-w-md p-6 text-center">
          <span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--warning-soft)] text-[var(--warning-strong)]" aria-hidden="true">↻</span>
          <h1 className="mt-4 text-lg font-bold">Bağlantı geçici olarak kesildi</h1>
          <p className="mt-2 text-sm leading-relaxed text-[var(--muted)]">Oturumun korunuyor. Servis yeniden bağlandığında sayfayı yenileyerek devam edebilirsin.</p>
          <button type="button" onClick={() => window.location.reload()} className="pressable mt-4 min-h-10 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white">Yeniden dene</button>
        </div>
      </main>
    );
  }

  if (!me) {
    return null;
  }

  return <AppShell me={me}>{children}</AppShell>;
}
