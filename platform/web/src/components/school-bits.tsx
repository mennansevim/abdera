"use client";

import { initials, readableTextOn } from "@/lib/format";
import { STATUS_LABEL, type SchoolStatus } from "@/lib/schools";
import { Badge, type Tone } from "./ui";

// Durum renkleri (ARCHITECTURE.md §4.1): Çalışıyor yeşil, Hata kırmızı, Kuruluyor mavi
// (nabızlı), Askıda gri, Silinmeyi bekliyor sarı.
export const STATUS_TONE: Record<SchoolStatus, Tone> = {
  Provisioning: "info",
  Active: "success",
  Failed: "danger",
  Suspended: "muted",
  PendingDestroy: "warning",
  Destroyed: "muted",
};

export function StatusBadge({ status }: { status: SchoolStatus }) {
  return (
    <Badge tone={STATUS_TONE[status]}>
      <span aria-hidden="true" className={`h-1.5 w-1.5 rounded-full bg-current ${status === "Provisioning" ? "pulse-dot" : ""}`} />
      {STATUS_LABEL[status]}
    </Badge>
  );
}

// Okulun baş harf rozeti - logo platformda tutulmaz (okulun kendi DB'sinde), ana renkle çizilir.
export function SchoolAvatar({ name, color, size = "md" }: { name: string; color: string | null; size?: "md" | "lg" }) {
  const background = color && /^#[0-9a-f]{6}$/i.test(color) ? color : "#a84e1f";
  const dimension = size === "lg" ? "h-14 w-14 text-lg" : "h-11 w-11 text-sm";
  return (
    <span
      aria-hidden="true"
      className={`grid ${dimension} shrink-0 place-items-center rounded-2xl font-serif font-bold italic shadow-sm`}
      style={{ background, color: readableTextOn(background) }}
    >
      {initials(name)}
    </span>
  );
}
