"use client";

import Link from "next/link";
import { useMe } from "@/lib/use-auth";
import { ChangePasswordForm } from "./change-password-form";

// docs/07-api.md - GET /api/dashboard/today burada gösterilecek; Dashboard modülü
// Phase 6'da geliyor. Şimdilik People/Scheduling'e hızlı erişim.
export default function DashboardPage() {
  const { data: me, refetch } = useMe();
  if (!me) return null;

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Bugün</h1>

      {me.mustChangePassword && (
        <div className="rounded-lg border border-amber-300 bg-amber-50 p-4">
          <p className="text-sm font-medium text-amber-900">
            Geçici şifrenle giriş yaptın. Devam etmeden önce kalıcı bir şifre belirle.
          </p>
          <ChangePasswordForm onDone={() => refetch()} />
        </div>
      )}

      <section className="rounded-lg border border-dashed border-neutral-300 p-6 text-sm text-neutral-500">
        Aidat durumu ve bildirimler Phase 4-5&apos;te burada görünecek. Şimdilik{" "}
        <Link href="/dashboard/students" className="underline">
          Öğrenciler
        </Link>
        ,{" "}
        <Link href="/dashboard/teachers" className="underline">
          Öğretmenler
        </Link>{" "}
        ve{" "}
        <Link href="/dashboard/calendar" className="underline">
          Takvim
        </Link>{" "}
        sekmelerinden veri girebilirsin.
      </section>
    </div>
  );
}
