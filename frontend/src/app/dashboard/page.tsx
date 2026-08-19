"use client";

import Link from "next/link";
import { useMe } from "@/lib/use-auth";
import { usePendingChangeRequests } from "@/lib/attendance";
import { ChangePasswordForm } from "./change-password-form";
import { TeacherTodayLessons } from "./teacher-today-lessons";

// docs/07-api.md - GET /api/dashboard/today Phase 6'da geliyor. Bu ekran o zamana kadar
// docs/00-master-prompt.md'nin Teacher UX'ini karşılar: "The first screen should be My
// Lessons Today" - Admin için ise kısayollar + ders değişikliği kuyruğuna dikkat çeker.
export default function DashboardPage() {
  const { data: me, refetch } = useMe();
  if (!me) return null;

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">{me.role === "Teacher" ? "Bugünkü Derslerim" : "Bugün"}</h1>

      {me.mustChangePassword && (
        <div className="rounded-lg border border-amber-300 bg-amber-50 p-4">
          <p className="text-sm font-medium text-amber-900">
            Geçici şifrenle giriş yaptın. Devam etmeden önce kalıcı bir şifre belirle.
          </p>
          <ChangePasswordForm onDone={() => refetch()} />
        </div>
      )}

      {me.role === "Teacher" ? <TeacherTodayLessons /> : <AdminOverview />}
    </div>
  );
}

function AdminOverview() {
  const { data: pending } = usePendingChangeRequests();

  return (
    <div className="space-y-4">
      {pending && pending.length > 0 && (
        <Link
          href="/dashboard/change-requests"
          className="block rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900 hover:bg-amber-100"
        >
          Dikkat: {pending.length} bekleyen ders değişikliği talebi var →
        </Link>
      )}

      <section className="rounded-lg border border-dashed border-neutral-300 p-6 text-sm text-neutral-500">
        Aidat durumu ve bildirimler Phase 4-5&apos;te burada görünecek. Şimdilik{" "}
        <Link href="/dashboard/students" className="underline">
          Öğrenciler
        </Link>
        ,{" "}
        <Link href="/dashboard/teachers" className="underline">
          Öğretmenler
        </Link>
        ,{" "}
        <Link href="/dashboard/calendar" className="underline">
          Takvim
        </Link>{" "}
        ve{" "}
        <Link href="/dashboard/change-requests" className="underline">
          Değişiklik Talepleri
        </Link>{" "}
        sekmelerinden veri girebilirsin.
      </section>
    </div>
  );
}
