"use client";

import type { ReactNode } from "react";
import { Icon, type IconName } from "@/components/icons";
import type { GuardianLesson, GuardianMessage, GuardianReceivable } from "@/lib/guardian";

// Veli portalının ortak parçaları. Veli ekranları telefonda, çoğu zaman tek elle ve hızlıca
// okunuyor: metin en az 14px, giriş alanları 16px (iOS 16px altındaki alanda sayfayı yakınlaştırır),
// dokunma hedefleri en az 44px.

export function addDays(date: Date, days: number) {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}

export function sameDay(a: Date, b: Date) {
  return a.toDateString() === b.toDateString();
}

export function formatTime(value: string) {
  return new Date(value).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
}

export function formatTimeRange(startAt: string, endAt: string) {
  return `${formatTime(startAt)} – ${formatTime(endAt)}`;
}

// "Bugün" / "Yarın" / "Perşembe, 30 Ekim" - velinin ilk baktığı şey günün kendisi.
export function formatDayLabel(value: string | Date, today: Date) {
  const date = typeof value === "string" ? new Date(value) : value;
  if (sameDay(date, today)) return "Bugün";
  if (sameDay(date, addDays(today, 1))) return "Yarın";
  return date.toLocaleDateString("tr-TR", { weekday: "long", day: "numeric", month: "long" });
}

export function formatMoney(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
}

// Vade gibi takvim günleri "yyyy-MM-dd" gelir; saat eklenmezse UTC gece yarısı sayılıp bir gün kayabilir.
export function formatDate(value: string) {
  return new Date(`${value}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long" });
}

export function formatInstantDate(value: string) {
  return new Date(value).toLocaleDateString("tr-TR", { day: "numeric", month: "long" });
}

// Aylık aidatın dönemi "2026-10", birebir dersin 4 derslik dönem aidatı "D003" (CLAUDE.md, AD3).
// Eski ekran ikisini de ay sanıp dönem aidatında "Invalid Date" yazıyordu.
export function formatPeriod(period: string): { title: string; detail?: string } {
  const month = /^(\d{4})-(\d{2})$/.exec(period);
  if (month) {
    const label = new Date(Number(month[1]), Number(month[2]) - 1, 1).toLocaleDateString("tr-TR", { month: "long", year: "numeric" });
    return { title: label.charAt(0).toLocaleUpperCase("tr-TR") + label.slice(1) };
  }
  const cycle = /^D(\d+)$/.exec(period);
  if (cycle) return { title: `${Number(cycle[1])}. dönem`, detail: "4 ders" };
  return { title: period };
}

export function isOpenReceivable(receivable: GuardianReceivable) {
  return receivable.status !== "Paid" && receivable.status !== "Cancelled";
}

export function remainingOf(receivable: GuardianReceivable) {
  return Math.max(0, receivable.amount - receivable.totalPaid);
}

export function formatRelativeTime(value: string): string {
  const diffMs = Date.now() - new Date(value).getTime();
  const minutes = Math.round(diffMs / 60000);
  if (minutes < 1) return "az önce";
  if (minutes < 60) return `${minutes} dakika önce`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} saat önce`;
  const days = Math.round(hours / 24);
  if (days < 7) return `${days} gün önce`;
  return formatInstantDate(value);
}

export type PillTone = "green" | "amber" | "red" | "neutral" | "brand";

const PILL_TONE: Record<PillTone, string> = {
  green: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  amber: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  red: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  neutral: "bg-[var(--surface-muted)] text-[var(--muted)]",
  brand: "bg-[var(--brand-soft)] text-[var(--brand-strong)]",
};

export function Pill({ tone, icon, children }: { tone: PillTone; icon?: IconName; children: ReactNode }) {
  return (
    <span className={`inline-flex shrink-0 items-center gap-1 rounded-full px-2.5 py-1 text-[.8125rem] font-bold ${PILL_TONE[tone]}`}>
      {icon && <Icon name={icon} className="h-3.5 w-3.5" />}
      {children}
    </span>
  );
}

export function rsvpPill(lesson: GuardianLesson): { tone: PillTone; icon: IconName; label: string } | null {
  if (lesson.status === "Cancelled") return { tone: "red", icon: "x", label: "İptal edildi" };
  // Rescheduled dersin eski saatidir; yeni saati takvimde ayrı bir ders olarak görünür.
  if (lesson.status === "Rescheduled") return { tone: "amber", icon: "swap", label: "Taşındı" };
  if (lesson.rsvpResponse === "Attending") return { tone: "green", icon: "check", label: "Geliyor" };
  if (lesson.rsvpResponse === "AttendingLate") return { tone: "amber", icon: "clock", label: "Geç kalacak" };
  if (lesson.rsvpResponse === "NotAttending") return { tone: "red", icon: "x", label: "Gelemeyecek" };
  if (lesson.status === "Makeup") return { tone: "brand", icon: "swap", label: "Telafi dersi" };
  return null;
}

export function ViewHeader({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <div className="mb-4">
      <h1 className="text-[1.5rem] font-bold tracking-[-0.015em]">{title}</h1>
      {subtitle && <p className="mt-0.5 text-sm text-[var(--muted)]">{subtitle}</p>}
    </div>
  );
}

export function SectionTitle({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <div className="mb-2 mt-6 flex items-center justify-between gap-3">
      <h2 className="text-base font-bold">{children}</h2>
      {action}
    </div>
  );
}

export function TextButton({ onClick, children }: { onClick: () => void; children: ReactNode }) {
  return (
    <button type="button" onClick={onClick} className="pressable -mr-2 flex min-h-11 items-center gap-1 rounded-xl px-2 text-sm font-bold text-[var(--brand-strong)]">
      {children}
      <Icon name="chevron" className="h-4 w-4" />
    </button>
  );
}

export function EmptyCard({ icon, children }: { icon: IconName; children: ReactNode }) {
  return (
    <div className="app-card flex flex-col items-center gap-2 px-5 py-7 text-center">
      <span className="grid h-11 w-11 place-items-center rounded-2xl bg-[var(--surface-muted)] text-[var(--muted)]"><Icon name={icon} className="h-5 w-5" /></span>
      <p className="text-sm text-[var(--muted)]">{children}</p>
    </div>
  );
}

// Hata ile "gerçekten boş" ayrı tutulur - istek düşünce "ders yok" yazmak veliye yanlış bilgi verir.
export function LoadError({ message, onRetry, pending }: { message: string; onRetry: () => unknown; pending?: boolean }) {
  return (
    <div role="alert" className="app-card flex flex-col items-center gap-3 px-5 py-7 text-center">
      <span className="grid h-11 w-11 place-items-center rounded-2xl bg-[var(--danger-soft)] text-[var(--danger-strong)]"><Icon name="alert-triangle" className="h-5 w-5" /></span>
      <div>
        <p className="text-sm font-bold">{message}</p>
        <p className="mt-0.5 text-sm text-[var(--muted)]">Bağlantını kontrol edip yeniden dene.</p>
      </div>
      <button type="button" onClick={() => void onRetry()} disabled={pending} className="pressable min-h-11 rounded-xl bg-[var(--brand-strong)] px-5 text-sm font-bold text-white disabled:opacity-60">
        {pending ? "Yükleniyor…" : "Tekrar dene"}
      </button>
    </div>
  );
}

export function SkeletonStack({ heights }: { heights: string[] }) {
  return (
    <div className="space-y-3" aria-busy="true">
      {heights.map((height, index) => <div key={index} className={`skeleton rounded-2xl ${height}`} />)}
    </div>
  );
}

export function MessageCard({ message, compact }: { message: GuardianMessage; compact?: boolean }) {
  return (
    <article className="app-card flex gap-3 px-4 py-3">
      <span className="mt-0.5 grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name="bell" className="h-4 w-4" /></span>
      <div className="min-w-0 flex-1">
        <p className={`whitespace-pre-line text-sm leading-relaxed ${compact ? "line-clamp-3" : ""}`}>{message.body}</p>
        <p className="mt-1 text-[.8125rem] text-[var(--muted)]">
          {compact
            ? formatRelativeTime(message.createdAt)
            : new Date(message.createdAt).toLocaleString("tr-TR", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" })}
        </p>
      </div>
    </article>
  );
}

// `toISOString()` UTC tarihini verir: İstanbul'da 00:00-03:00 arası "bugün" dün görünür.
export function localIsoDate() {
  const now = new Date();
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}
