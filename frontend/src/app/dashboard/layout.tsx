"use client";

import { useRequireAuth } from "@/lib/use-require-auth";
import { AppHeader } from "./app-header";

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const { me, isLoading } = useRequireAuth();

  if (isLoading) {
    return <main className="flex flex-1 items-center justify-center text-sm text-neutral-500">Yükleniyor…</main>;
  }

  if (!me) {
    return null;
  }

  return (
    <div className="flex min-h-screen flex-1 flex-col">
      <AppHeader me={me} />
      <div className="mx-auto w-full max-w-5xl flex-1 px-4 py-8">{children}</div>
    </div>
  );
}
