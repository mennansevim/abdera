"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { AppShell } from "@/components/app-shell";
import { LoadError } from "@/components/ui";
import { errorMessage, onSessionEvent } from "@/lib/api";
import { meKey, useMe } from "@/lib/auth";

// Operatör ekranlarının ortak kapısı. Yönetim uçları TOTP doğrulanmış oturum ister:
// oturum yoksa giriş, TOTP hiç kurulmamışsa kurulum, kurulmuş ama bu oturumda girilmemişse
// yeniden giriş (kodla) ekranına gidilir. Sayfa açıkken herhangi bir istek 401/403 alırsa
// (oturum süresi doldu, TOTP başka yerden sıfırlandı) aynı karar yeniden verilir.
export default function PanelLayout({ children }: { children: React.ReactNode }) {
  const { data: me, isLoading, error, refetch } = useMe();
  const router = useRouter();
  const client = useQueryClient();

  const destination = me === null ? "/login" : me && !me.totpEnabled ? "/setup-totp" : me && !me.totpVerified ? "/login" : null;

  useEffect(() => {
    if (!isLoading && destination) router.replace(destination);
  }, [isLoading, destination, router]);

  useEffect(() => onSessionEvent(() => {
    client.invalidateQueries({ queryKey: meKey });
  }), [client]);

  if (isLoading) {
    return (
      <main className="grid min-h-dvh place-items-center bg-[var(--background)]">
        <span className="brand-mark pulse-dot font-serif text-sm font-bold italic" aria-label="Yükleniyor">A</span>
      </main>
    );
  }

  if (error && !me) {
    return (
      <main className="grid min-h-dvh place-items-center bg-[var(--background)] p-4">
        <div className="w-full max-w-md"><LoadError message={errorMessage(error)} onRetry={() => refetch()} /></div>
      </main>
    );
  }

  if (!me || destination) return null;

  return <AppShell me={me}>{children}</AppShell>;
}
