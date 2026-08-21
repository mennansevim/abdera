"use client";

import { useRequireAuth } from "@/lib/use-require-auth";
import { AppShell } from "./app-header";

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const { me, isLoading } = useRequireAuth();

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

  if (!me) {
    return null;
  }

  return <AppShell me={me}>{children}</AppShell>;
}
