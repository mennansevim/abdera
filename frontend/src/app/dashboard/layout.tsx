"use client";

import { useRequireAuth } from "@/lib/use-require-auth";
import { AppLoader } from "@/components/app-loader";
import { AssistantProvider } from "@/components/assistant/assistant-provider";
import { canUseAssistant } from "@/lib/assistant";
import { AppShell } from "./app-header";

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const { me, isLoading, authError } = useRequireAuth();

  if (isLoading) {
    return <AppLoader message="Uygulama hazırlanıyor…" />;
  }

  if (authError && !me) {
    return (
      <main className="grid min-h-dvh place-items-center bg-[var(--background)] p-5">
        <div className="app-card w-full max-w-md p-6 text-center">
          <span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--warning-soft)] text-[var(--warning-strong)]" aria-hidden="true">↻</span>
          <h1 className="mt-4 text-lg font-bold">Bağlantı geçici olarak kesildi</h1>
          <p className="mt-2 text-sm leading-relaxed text-[var(--muted)]">Oturumun korunuyor. Servis yeniden bağlandığında sayfayı yenileyerek devam edebilirsin.</p>
          <button type="button" onClick={() => window.location.reload()} className="pressable mt-4 min-h-11 rounded-xl bg-[var(--brand-strong)] px-4 text-sm font-bold text-white">Yeniden dene</button>
        </div>
      </main>
    );
  }

  if (!me) {
    return null;
  }

  // Asistan her ekrandan açılır (kenar menüsü, ⌘K, öğrenci satırı) - docs/10-decisions.md AE.
  return <AssistantProvider enabled={canUseAssistant(me.role)}><AppShell me={me}>{children}</AppShell></AssistantProvider>;
}
