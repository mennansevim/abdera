"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { useMe, useLogout } from "@/lib/use-auth";
import { ChangePasswordForm } from "./change-password-form";

// docs/07-api.md - GET /api/dashboard/today burada gösterilecek; Dashboard modülü
// Phase 6'da geliyor. Phase 1'de yalnızca oturum akışını doğrulayan bir iskelet var.
export default function DashboardPage() {
  const router = useRouter();
  const { data: me, isLoading, isError, refetch } = useMe();
  const logout = useLogout();

  useEffect(() => {
    if (isError) {
      router.replace("/login");
    }
  }, [isError, router]);

  if (isLoading) {
    return <main className="flex flex-1 items-center justify-center text-sm text-neutral-500">Yükleniyor…</main>;
  }

  if (!me) {
    return null;
  }

  return (
    <main className="mx-auto w-full max-w-3xl flex-1 space-y-6 px-4 py-10">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Bugün</h1>
          <p className="text-sm text-neutral-500">
            {me.email} · {me.role === "Admin" ? "Yönetici" : "Öğretmen"}
          </p>
        </div>
        <button
          onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/login") })}
          className="rounded-md border border-neutral-300 px-3 py-1.5 text-sm text-neutral-700 hover:bg-neutral-100"
        >
          Çıkış yap
        </button>
      </header>

      {me.mustChangePassword && (
        <div className="rounded-lg border border-amber-300 bg-amber-50 p-4">
          <p className="text-sm font-medium text-amber-900">
            Geçici şifrenle giriş yaptın. Devam etmeden önce kalıcı bir şifre belirle.
          </p>
          <ChangePasswordForm onDone={() => refetch()} />
        </div>
      )}

      <section className="rounded-lg border border-dashed border-neutral-300 p-6 text-sm text-neutral-500">
        Ders takvimi, aidat durumu ve bildirimler Phase 2 ve sonrasında burada görünecek —
        bkz. <code className="rounded bg-neutral-100 px-1 py-0.5">docs/07-api.md</code>.
      </section>
    </main>
  );
}
