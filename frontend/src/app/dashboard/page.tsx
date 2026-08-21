"use client";

import Link from "next/link";
import { useMe } from "@/lib/use-auth";
import { usePendingChangeRequests } from "@/lib/attendance";
import { useDashboardToday } from "@/lib/dashboard";
import { ChangePasswordForm } from "./change-password-form";
import { TeacherTodayLessons } from "./teacher-today-lessons";

// docs/07-api.md GET /api/dashboard/today (ARC-6/E2, docs/13-audit-fix-prompt.md madde 13).
// docs/00-master-prompt.md'nin Teacher UX'i zaten "My Lessons Today" listesiyle karşılanıyor
// (TeacherTodayLessons) - Admin için burada okul geneli özet + ders değişikliği kuyruğuna
// dikkat çeken kısayollar var.
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
  const { data: today, isLoading } = useDashboardToday();

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

      {isLoading && <p className="text-sm text-neutral-500">Yükleniyor…</p>}

      {today && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <StatTile label="Bugünkü ders" value={today.todayLessons} />
          <StatTile label="Geliyor" value={today.attending} tone="positive" />
          <StatTile label="Gelmiyor" value={today.notAttending} tone="negative" />
          <StatTile label="Yanıt yok" value={today.noResponse} />
          <StatTile
            label="Bekleyen değişiklik talebi"
            value={today.pendingChangeRequests}
            href="/dashboard/change-requests"
            tone={today.pendingChangeRequests > 0 ? "warning" : undefined}
          />
          <StatTile
            label="Vadesi geçmiş aidat"
            value={today.overduePayments}
            href="/dashboard/billing"
            tone={today.overduePayments > 0 ? "warning" : undefined}
          />
          <StatTile label="Yaklaşan doğum günü" value={today.upcomingBirthdays} />
          <StatTile label="Yaklaşan okul etkinliği" value={today.upcomingSchoolEvents} />
        </div>
      )}

      <section className="rounded-lg border border-neutral-200 bg-white p-4 text-sm text-neutral-500">
        Hızlı erişim:{" "}
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
        </Link>
        ,{" "}
        <Link href="/dashboard/change-requests" className="underline">
          Değişiklik Talepleri
        </Link>
        ,{" "}
        <Link href="/dashboard/billing" className="underline">
          Aidatlar
        </Link>{" "}
        ve{" "}
        <Link href="/dashboard/notifications" className="underline">
          Bildirimler
        </Link>
        .
      </section>
    </div>
  );
}

const TONE_CLASSES: Record<"positive" | "negative" | "warning", string> = {
  positive: "border-green-200 bg-green-50 text-green-900",
  negative: "border-red-200 bg-red-50 text-red-900",
  warning: "border-amber-300 bg-amber-50 text-amber-900",
};

function StatTile({
  label,
  value,
  href,
  tone,
}: {
  label: string;
  value: number;
  href?: string;
  tone?: "positive" | "negative" | "warning";
}) {
  const className = `min-h-11 rounded-lg border p-4 ${tone ? TONE_CLASSES[tone] : "border-neutral-200 bg-white"}`;
  const content = (
    <>
      <div className="text-2xl font-semibold">{value}</div>
      <div className="text-xs text-neutral-500">{label}</div>
    </>
  );

  return href ? (
    <Link href={href} className={`block hover:opacity-80 ${className}`}>
      {content}
    </Link>
  ) : (
    <div className={className}>{content}</div>
  );
}
